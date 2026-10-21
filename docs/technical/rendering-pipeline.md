# Pipeline di Rendering: dall'YAML al Pixel

Questo documento descrive il flusso architetturale completo del motore 3D-Ray, dal caricamento della scena alla scrittura del pixel finale. Non duplica la trattazione matematica dei documenti esistenti — la integra con una mappa navigabile che collega le fasi, i file sorgente, gli invarianti e i contratti tra componenti.

**Documenti correlati:**
- [Path Tracing e Illuminazione](./path-tracing-and-lighting.md) — NEE, Russian Roulette, campionamento HDRI, Sphere Light
- [Modello di Shading e Materiali](./shading-model.md): OpenPBR, materiali classici, interfaccia `IMaterial`, Normal Mapping
- [Strutture di Accelerazione (BVH)](./acceleration-structures.md) — Bounding Volume Hierarchy e SAH

**File sorgente chiave:**
- `src/RayTracer/Program.cs` — Entry point, parsing CLI, orchestrazione
- `src/RayTracer/Scene/SceneLoader.cs` — Parsing YAML, costruzione del world
- `src/RayTracer/Rendering/Renderer.cs` — Cuore del path tracer
- `src/RayTracer/Camera/Camera.cs` — Modello thin-lens

---

## Vista d'Insieme

```
  YAML file
      │
      ▼
┌─────────────┐
│ SceneLoader  │  Parse YAML → materiali, geometrie, luci, camera, sky
└──────┬──────┘
       │  (world, camera, lights, sky, globalMedium)
       ▼
┌─────────────┐
│  Renderer   │  Costruttore: scene analysis + RR + photon map caustiche (se --caustics on)
│ constructor │  Render():    loop parallelo sui pixel
└──────┬──────┘
       │  Per ogni pixel: N campioni stratificati (Sobol o griglia √N×√N)
       ▼
┌─────────────┐
│  TraceRay   │  Ricorsione: hit → normal map → emission → NEE (+ gather caustiche) → Sample → RR
└──────┬──────┘
       │  Radiance HDR lineare
       ▼
┌─────────────┐
│ Post-process│  Firefly clamp (opz.) → esposizione → bilanciamento del bianco → view transform
└──────┬──────┘
       │  sRGB [0,1]
       ▼
   pixels[j,i]
```

---

## Fase 1 — Caricamento della Scena

**File:** `SceneLoader.cs` · **Metodo:** `Load()`

### 1.1 Import YAML

Prima del parsing dei materiali, il loader processa la sezione `imports:`:

1. Per ogni file importato, il percorso viene risolto relativamente alla directory del file che importa.
2. Il file viene deserializzato come `SceneData` e i suoi eventuali `imports:` annidati vengono processati ricorsivamente.
3. Un `HashSet<string>` di percorsi assoluti previene import ciclici.
4. Le sezioni `materials`, `entities`, `lights` e `templates` importate vengono **prepese** a quelle locali.
5. Le sezioni `world`, `camera`/`cameras` **non** vengono importate.

La semantica prepend + dictionary last-write-wins garantisce che le definizioni locali con lo stesso ID sovrascrivano quelle importate.

### 1.2 Parsing YAML

Il loader deserializza il file YAML in strutture dati intermedie (`SceneData`, `MaterialData`, `LightData`, ecc.) tramite la libreria YamlDotNet con naming convention `underscore_case`.

### 1.3 Costruzione dei Materiali (due passate)

I materiali vengono costruiti in due passate per supportare `MixMaterial` (che referenzia altri materiali per ID):

1. **Passata 1:** tutti i materiali non-mix vengono creati e inseriti nel dizionario `materials[id]`.
2. **Passata 2:** i materiali `mix`/`blend` vengono risolti iterativamente — un mix-di-mix si risolve quando entrambi i figli sono già nel dizionario. Il loop si ripete finché il numero di materiali irrisolti decresce; se non decresce, i riferimenti sono ciclici o inesistenti e viene emesso un warning.

**Contratto:** Ogni `MaterialData.Id` deve essere univoco. Se un materiale referenziato non esiste, il loader sostituisce un Lambertian grigio di fallback ed emette un warning.

### 1.4 Costruzione dei Template

I template dalla sezione `templates:` vengono registrati in un `Dictionary<string, EntityData>` keyed by `Name`. I template NON producono geometria renderizzabile — sono blueprint. La loro `EntityData` viene conservata per essere riutilizzata quando si incontra un'entità `type: "instance"`.

Last-write-wins: template locali con lo stesso nome sovrascrivono template importati.

### 1.5 Costruzione delle Geometrie

Le entità YAML vengono trasformate in oggetti `IHittable`. Per ogni entità:

1. La primitiva viene creata (`Sphere`, `Box`, `Cylinder`, ecc.).
2. Se presente un blocco `transform`, la primitiva viene avvolta in un oggetto `Transform` che applica scale → rotate → translate nell'ordine corretto.
3. Se la primitiva è un `InfinitePlane` (anche dentro un Transform), viene separata dalla lista BVH — il suo AABB infinito degraderebbe la struttura ad albero.

Le primitive finite vengono inserite nel BVH; le infinite (piani) e i nodi CSG vengono mantenuti in una lista separata. Il world finale è un `HittableList` che contiene il BVH e le primitive non-BVH.

#### Gruppi (Scene Graph)

Le entità `type: "group"` vengono costruite da `CreateGroupEntity()` → `BuildChildList()`:
- Ogni figlio risolve tipo, materiale (proprio o ereditato) e trasformazione locale.
- I figli vengono assemblati in un `Group` con BVH interno se > 4 figli finiti.
- La trasformazione del gruppo viene applicata dal caller come `Transform` wrapper.

#### Istanze (Template)

Le entità `type: "instance"` vengono costruite da `CreateInstanceEntity()`:
1. Il template viene cercato nel dizionario per nome.
2. Il materiale viene risolto: istanza override → template default → fallback.
3. `BuildChildList()` costruisce una nuova copia della geometria dal template.
4. La trasformazione del template ("posa di default") viene applicata come primo `Transform`.
5. La trasformazione dell'istanza viene applicata dal caller come secondo `Transform` sopra.
6. La catena risultante è: `child_local → template_transform → instance_transform`.

### 1.6 Costruzione delle Luci

Le luci esplicite dal YAML vengono create tramite `CreateLight()`. Poi due passaggi automatici:

1. **`ExtractGeometryLights()`** — scansiona tutte le geometrie cercando primitive `ISamplable` con materiale `Emissive`. Per ognuna crea un `GeometryLight` e lo aggiunge alla lista luci. La scansione avviene tramite `ExtractGeometryLightsRecursive()`, che naviga l'albero della scena gestendo tre casi:
   - **Group nudo**: itera i figli ricorsivamente.
   - **Transform wrapping Group**: compone e propaga la matrice del Transform esterno su ogni figlio emissivo per garantirne il posizionamento in world space.
   - **Tutto il resto**: delega a `ResolveEmissiveSamplable()`. I singoli `Transform` sono gestiti come `ISamplable` (delegano a `Sample()` sulla primitiva interna correggendo l'area con il Jacobian e convertendo in world space).

2. **`EnvironmentLight`** — se il cielo supporta il campionamento diretto (`CanSampleDirectly` = true per HDRI, gradient sky con sun disk, e flat sky con luminanza > 0), viene creato un `EnvironmentLight` e aggiunto alla lista.

Se non ci sono luci e il YAML non ha una sezione `lights:` esplicita, viene aggiunto un lighting di default (una directional + una point).

**Contratto:** Il parametro `shadowSamplesOverride` da CLI (`-S`) ha la precedenza sul valore per-luce del YAML. Se null, ogni luce usa il proprio valore.

### 1.7 Costruzione del Cielo

`BuildSkySettings()` crea un oggetto `SkySettings` in base al tipo dichiarato in `world.sky.type`:

- **`flat`** (default quando `world.sky` è omesso) — colore uniforme dal campo `color` (default daylight blu `[0.5, 0.7, 1.0]`). Partecipa a NEE come uniform sphere sampler quando la luminanza è > 0.
- **`gradient`** — zenith/horizon/ground con interpolazione, più sun disk opzionale. Solo il sun disk partecipa a NEE.
- **`hdri`** — carica il file `.hdr` tramite `HdrLoader`, costruisce l'`EnvironmentMap` con CDF per importance sampling.

### 1.8 Output

`Load()` restituisce una tupla `(IHittable world, Camera camera, List<ILight> lights, SkySettings sky, IMedium? globalMedium)` pronta per il Renderer.

---

## Fase 2 — Inizializzazione del Renderer

**File:** `Renderer.cs` · **Metodo:** costruttore

Il costruttore riceve gli output del loader e prepara lo stato per il rendering multi-thread.

### 2.1 Scene Analysis (Classificazione dell'Illuminazione)

Lo scopo è decidere se la scena è "indirect-dominant" (illuminata prevalentemente da superfici emissive e cielo in scene chiuse) o "normal" (illuminata da luci esplicite). Questo determina l'aggressività della Russian Roulette.

La metrica è l'**irradianza media** (Rec.709 luminance) proiettata sulla sfera che racchiude la scena:

```csharp
AABB sceneBounds = ClampInfiniteExtents(world.BoundingBox(), ±1e3);
float totalFlux = lights.Sum(l => l.ApproximatePower(sceneBounds));
float sceneRadius = 0.5f · |sceneBounds.Max − sceneBounds.Min|;
float meanIrradiance = totalFlux / (4π · sceneRadius²);
bool isIndirectDominant = meanIrradiance < IndirectDominantThreshold; // 0.5
```

**⚠️ CONTRATTO CRITICO — `ILight.ApproximatePower(sceneBounds)`:**

Il metodo restituisce il flusso radiante approssimato della luce (Φ in unità di luminanza Rec.709). È usato **esclusivamente dal costruttore del Renderer** per la scene classification; il rendering vero e proprio usa `IlluminateAndTest()` / `IlluminateAndTestStratified()`.

Tre invarianti obbligatori:

| Invariante | Motivo |
|------------|--------|
| **Deterministico** — niente `RandomFloat()` | Il costruttore gira single-thread prima del `Parallel.For`. Un risultato non-deterministico renderebbe la classificazione RR instabile tra run. |
| **Receiver-independent** — nessuna dipendenza da un punto di shading | Il flusso è una proprietà intrinseca della luce, non dell'osservatore. Evaluare a un punto arbitrario (come faceva il vecchio `Illuminate(Vector3.Zero)`) rende la classificazione dipendente dalla posizione del mondo. |
| **Finito** — lights con apertura infinita usano `sceneBounds` | `DirectionalLight` e `EnvironmentLight` non hanno flusso finito senza un riferimento geometrico. Usare la sezione trasversale della scena (π·R²) bounds il flusso in modo fisicamente coerente. |

Formula per tipo di luce (tutti i risultati sono moltiplicati per `Luminance(Color)`):

| Tipo | Flusso Φ | Note |
|------|---------|------|
| `PointLight` | `4π · I` | Emettitore isotropo, integrato su sfera completa |
| `DirectionalLight` | `I · π · R²` | Irradianza × sezione trasversale della scena |
| `SpotLight` | `I · (Ω_core + Ω_fall/3)` | `Ω_core = 2π(1−cosInner)`, `Ω_fall = 2π(cosInner−cosOuter)` |
| `AreaLight` | `π · I · A` | Emettitore lambertiano, `A = |U × V|` |
| `SphereLight` | `4π · I` | Intensità radiante (W/sr) integrata su 4π |
| `GeometryLight` | `π · emission · A` | Lambertiano dal materiale `Emissive`, area dal Sample() |
| `EnvironmentLight` | `π · L̄_sky · π · R²` | Irradianza emisferica × sezione scena |

> **Nota per futuri sviluppatori:** quando si aggiunge un nuovo tipo di luce, il suo `ApproximatePower` deve rispettare questi tre invarianti. Usare `PointLight` (caso finito) o `DirectionalLight` (caso infinito con `sceneBounds`) come modelli di riferimento.

### 2.2 Configurazione Russian Roulette

In base alla classificazione:

| Tipo scena | `_rrMinBounces` | `_rrMinSurvival` | Max boost |
|------------|:-:|:-:|:-:|
| Normal (luci esplicite forti) | 4 | 0.15 | 6.7× |
| Indirect-dominant (emissive/sky) | 8 | 0.50 | 2.0× |

La soglia è `IndirectDominantThreshold = 0.5` sull'irradianza media (Rec.709 luminance per unità di area della sfera di scena).

**Invarianza di scala**. La normalizzazione per `4π·R²` rende la metrica indipendente dalle unità world-space della scena: raddoppiare tutte le coordinate lascia la classificazione invariata (PointLight: 4π·I / (4π·(2R)²) vs 4π·I / (4π·R²) → scala come 1/R², ma lo stesso fa la distanza tipica delle luci, quindi l'"illuminamento per bounce" è lo stesso).

**Clamp delle estensioni infinite**. `InfinitePlane` riporta un AABB fittizio a ±1e6 per compatibilità con il BVH. Senza clamp, il raggio di scena divergerebbe e `EnvironmentLight`/`DirectionalLight` (che scalano con π·R²) produrrebbero un meanIrradiance fisso e arbitrario. Il clamp a ±1e3 restituisce un raggio realistico per tutte le scene pratiche.

> **Perché la distinzione?** In scene a luce diretta, la NEE cattura la maggior parte dell'energia — i bounce indiretti sono una piccola correzione e possono essere terminati aggressivamente. In scene emissive-only chiuse (Cornell), TUTTA l'energia arriva dai bounce indiretti; terminare i path troppo presto produce macchie scure.

### 2.3 Registrazione degli Emitter (per il peso MIS dell'emissione)

```csharp
_emitterToLight = new Dictionary<Emissive, ILight>();
foreach (var gl in lights.OfType<GeometryLight>())
    _emitterToLight[gl.Material] = gl;
foreach (var light in lights)                 // proxy visibili di sphere/area light
    if (light.ProxyMaterial is { } proxy)
        _emitterToLight[proxy] = light;
```

Il dizionario dice a `WeightEmission` quali materiali emissivi la NEE può raggiungere e con quale luce: per questi l'emissione colpita da un raggio BSDF non-delta riceve il peso MIS calcolato con la `PdfSolidAngle` della luce (§4.4). Gli emettitori assenti dal dizionario (back-face, primitive non `ISamplable`, emissione di `openpbr`) passano a peso pieno, perché nessun campione di luce li ha già contati.

**Vedi:** [Path Tracing e Illuminazione §2.2](./path-tracing-and-lighting.md) per la trattazione completa del double-counting.

### 2.4 Photon Map delle Caustiche (solo con `--caustics on`)

Quando le caustiche sono attive, il costruttore lancia un **pre-pass di photon mapping** (`CausticPhotonTracer.Build`): emette fotoni da tutte le luci, li traccia attraverso le interfacce speculari (vetro/acqua/metallo/specchio) e deposita su una `PhotonMap` (spatial hash grid) quelli che atterrano su una superficie diffusa dopo ≥1 rimbalzo speculare (cammini `L S+ D`). Costruita una volta sola; con `--caustics off` o senza superfici speculari il passo è saltato e l'output è bit-identico a prima. **Vedi** [Path Tracing e Illuminazione §2.5](./path-tracing-and-lighting.md).

---

## Fase 3 — Render Loop

**File:** `Renderer.cs` · **Metodo:** `Render(int width, int height)`

### 3.1 Parallelizzazione

Il rendering usa `Parallel.For` su **tile di 16×16 pixel**:

```csharp
const int TileSize = 16;
Parallel.For(0, totalTiles, new ParallelOptions {
    MaxDegreeOfParallelism = Environment.ProcessorCount
}, tile => { ... });
```

Le tile bilanciano il carico meglio delle scanline (le righe di cielo costano poco, quelle centrali piene di geometria molto) e tengono ogni worker su un blocco di pixel contiguo in cache. Non c'è stato condiviso mutabile durante il rendering: `_world`, `_lights`, `_camera` e tutti i parametri sono readonly. Lo stato per thread è il sampler (Sobol per pixel o PRNG di `MathUtils`) e qualche switch impostato all'inizio di ogni tile (`ShadowRay.BlockAllTransmission`, `MathUtils.SceneScale`).

Il progresso (tile completate, contate con `Interlocked.Increment`) viene stampato da un thread dedicato, così nessun worker tocca il lock della console.

### 3.2 Campionamento Stratificato (per pixel)

Per ogni pixel `(i, j)`, il motore lancia `√N × √N` raggi distribuiti su una griglia stratificata con jitter casuale:

```
┌──────────────────┐
│ (0,0) │ (1,0) │ (2,0) │   ← cella della griglia
│   ×   │   ×   │   ×   │   ← punto jittered dentro la cella
├───────┼───────┼───────┤
│ (0,1) │ (1,1) │ (2,1) │
│   ×   │   ×   │   ×   │
├───────┼───────┼───────┤
│ (0,2) │ (1,2) │ (2,2) │
│   ×   │   ×   │   ×   │
└──────────────────┘
         (√N = 3, N = 9 campioni)
```

La griglia esterna serve al sampler `prng`, dove il numero di campioni effettivi è un quadrato perfetto (`-s 20` → `5×5 = 25`). Con `sobol` (default) i campioni sono esattamente `-s` e la coppia 2D di Sobol li dispone già stratificati sul pixel, senza griglia esterna. Per ogni campione:

1. **Coordinate UV** nel viewport: `u = (i + filter.SamplePosition(jitterU)) / width`, `v = (height - j - 1 + filter.SamplePosition(jitterV)) / height`. Il filtro di ricostruzione (`--filter`, `Rendering/PixelFilter.cs`) è applicato per *filter importance sampling*: il campione viene spostato rispetto al centro del pixel con densità proporzionale al filtro (Gaussiano σ = 0.5 px per default, Blackman-Harris, oppure box, per cui la posizione è il jitter stesso) e conta con peso 1 solo per il suo pixel. Niente splatting sui vicini: il rumore dei pixel resta indipendente, come assumono il denoiser e la stima della varianza a doppio buffer.
2. **Generazione del raggio** tramite `Camera.GetRay(u, v)`
3. **Tracing** con `TraceRay(ray, maxDepth, prevBsdfPdf: 0, prevIsDelta: true, ...)`, con pile di mezzi e IOR vuote
4. **Firefly clamp** sul campione singolo
5. **Accumulo** nel colore cumulativo

Dopo tutti i campioni, il colore medio passa per la trasformazione di visualizzazione:

```csharp
Vector3 linearColor = cumulativeColor / actualSamples;
pixels[j, i] = _display.Apply(linearColor * _exposureScale);
```

### 3.3 Generazione del Raggio (Camera Thin-Lens)

**File:** `Camera.cs`

La camera implementa il modello **thin-lens** per il depth of field:

1. Il punto di origine del raggio viene perturbato casualmente all'interno di un disco di raggio `aperture / 2` sul piano della lente (o di un poligono regolare inscritto con `aperture_blades`, stirato in orizzontale da `aperture_ratio`; campionamento uniforme in `Camera.SampleAperture`). `focal_length`, `sensor_width` e `f_stop` sono convertiti in campo visivo e apertura da `SceneLoader.ApplyPhysicalCamera`.
2. La direzione punta dal punto perturbato verso il punto corrispondente sul piano focale a distanza `focal_dist`.

Con `aperture = 0` il disco degenera in un punto (pinhole) e tutti gli oggetti sono a fuoco.

---

## Fase 4 — TraceRay (Il Cuore del Path Tracer)

**File:** `Renderer.cs`, **Metodi:** `TraceRay(Ray ray, int depth, float prevBsdfPdf, bool prevIsDelta, ref MediumStack mediums, ref IorStack iors, Vector3 currentAbsorption, Vector3 pathThroughput, RayCategory incomingCategory, int causticState)`, `ShadeSurface`, `ShadeSampleBounce`

Questa è la funzione ricorsiva che risolve l'equazione del rendering. Ogni invocazione rappresenta un **bounce** (rimbalzo) del raggio nella scena.

### 4.1 Condizione di Uscita

```csharp
if (depth <= 0) return Vector3.Zero;
```

Se il raggio ha esaurito i bounce, restituisce nero (energia zero). Il parametro `maxDepth` di default è 8 (CLI `-d`); in pratica la Russian Roulette termina la maggior parte dei path prima di questo limite. Per scene con vetri impilati alzalo a 16+ (vedi [Profili di Rendering](../reference/profili-di-rendering.md)).

### 4.2 Hit Test

```csharp
hit = _world.Hit(currentRay, MathUtils.RayEpsilon, MathUtils.Infinity, ref rec);
...
Vector3 result = !hit
    ? SampleSky(currentRay, prevBsdfPdf, prevIsDelta, causticState)
    : ShadeSurface(currentRay, ref rec, depth, prevBsdfPdf, prevIsDelta, ...);
```

`tMin` non è una costante fissa: `MathUtils.RayEpsilon` è proporzionale al raggio della scena, e l'origine dei raggi secondari è spostata con `MathUtils.OffsetOrigin`, di una distanza relativa alla coordinata più grande del punto (circa 256 ulp). Così l'immagine non cambia se la scena viene scalata (10 µm o 1 km) o spostata lontano dall'origine.

Il raggio viene testato contro il world (BVH + primitivi non-BVH). Se non colpisce nulla, il raggio è "sfuggito" dalla scena → campiona il cielo con il peso MIS (§4.9).

**Vedi:** [Strutture di Accelerazione](./acceleration-structures.md) per il funzionamento del BVH.

Il `HitRecord` risultante contiene: punto di hit, normale, UV, tangente/bitangente, materiale, front/back face, e il seed dell'oggetto (per texture procedurali).

### 4.3 Normal Map

```csharp
if (material?.NormalMap != null && rec.Tangent.LengthSquared() > 0.5f)
    ApplyNormalMap(ref rec, material.NormalMap);
```

Se il materiale ha una normal map, la normale del `HitRecord` viene perturbata **prima** di qualsiasi calcolo di shading. La trasformazione usa la matrice TBN (Tangent-Bitangent-Normal) ortonormalizzata con Gram-Schmidt. Le back-face invertono T e B per preservare l'handedness.

**Punto chiave:** la perturbazione avviene in-place su `rec.Normal`. Tutto il codice successivo (emissione, NEE, `Sample`) usa la normale perturbata; la normale non perturbata resta in `rec.ShadingNg` come riferimento per il fattore di shadow terminator (§4.5).

**Vedi:** [Modello di Shading §Normal Mapping](./shading-model.md) per la matematica TBN.

### 4.4 Emissione (con MIS balance heuristic)

```csharp
Vector3 raw = material.Emit(u, v, localPoint, objectSeed, frontFace);
emitted = WeightEmission(raw, material, ray, prevBsdfPdf, prevIsDelta);
```

Il peso dipende dallo stato del bounce precedente:

| Percorso | `prevIsDelta` | `prevBsdfPdf` | Emissione | Motivo |
|----------|:-:|:-:|:-:|---|
| Camera → superficie | `true` | (0) | ✅ Piena | Il raggio primario non può essere campionato da NEE |
| Lobo delta (specchio, vetro liscio) → emissivo | `true` | (0) | ✅ Piena | Bounce delta: nessuna NEE può raggiungerlo |
| Sample non-delta → emissivo registrato | `false` | `> 0` | ⚖️ MIS | `w_bsdf = prevBsdfPdf / (prevBsdfPdf + p_light)` (balance, o power con `--mis power`) |
| Qualunque → emissivo **non registrato** | (qualunque) | (qualunque) | ✅ Piena | Assente da `_emitterToLight` (§2.3): la NEE non può raggiungerlo |

Ogni materiale campiona con `Sample`, quindi un bounce non-delta porta sempre una pdf positiva: non esiste più un caso con `prevBsdfPdf = 0` che sopprime l'emissione.

### 4.5 Direct Lighting (NEE) — `ComputeDirectLighting`

```csharp
bool needsLightSampling = material?.NeedsDirectLighting ?? true;
Vector3 directLight = needsLightSampling
    ? DirectLightingAt(in rec, viewDir, material, shadowMedium, shadowMediumEntity)
    : Vector3.Zero;
```

La NEE viene attivata solo per materiali che rispondono alla luce diretta. Le sorgenti di luce stesse (`Emissive`) ritornano `NeedsDirectLighting = false` e saltano l'intero passaggio. `DirectLightingAt` alza `ShadowRay.ReceiverTransmits` quando il materiale ha un lobo di trasmissione non-delta (`HasTransmission`: vetro satinato, trasmissione diffusa), così vengono tenuti anche i campioni di luce dietro la superficie, poi chiama `ComputeDirectLighting`.

**Struttura interna di `ComputeDirectLighting`:**

```
Per ogni luce (tutte con --light-sampling all, default; una scelta con
probabilità pPick con power/uniform, contributo diviso per pPick):
    Per ogni shadow sample (light.ShadowSamples):
        1. SampleLight → light.IlluminateAndTestStratified(...)
           (punto sulla luce, shadow ray, lightColor già diviso per ShadowSamples)
        2. Se non in ombra:
           integrand = DirectIntegrand(material, rec, viewDir, dirToLight)
                     = material.Evaluate(viewDir, dirToLight, rec) × |N.L|
           integrand *= LightSideTerminator(rec, dirToLight)
           Tr = trasmittanza del mezzo lungo lo shadow ray (se c'è un mezzo)
           wNee = MisWeight(pPick × pLight, material.Pdf(viewDir, dirToLight))
                  (1 per luci delta o pdf BSDF nulla)
           accumula += wNee × lightColor × integrand × Tr
```

Il dispatch per tipo di luce è una sola chiamata virtuale: ogni luce decide da sé se stratificare (area e sphere su una griglia `√N×√N` scelta dal sample index) o campionare un punto solo.

**Contratto dell'integrando:** `Evaluate` restituisce la BSDF completa (colori, Fresnel, tutti i lobi non-delta) **senza** il coseno; `DirectIntegrand` moltiplica per $|N \cdot L|$ (il valore assoluto serve alla luce che arriva da dietro una superficie trasmissiva). È la stessa funzione che il rimbalzo indiretto campiona con `Sample`, quindi le due strategie del MIS stimano lo stesso integrando. I lobi delta hanno `Evaluate = 0` e non ricevono nulla dalla NEE. Una superficie senza materiale vale Lambert bianco.

**Shadow terminator:** `LightSideTerminator` applica il fattore di Chiang et al. (2019) sul lato della superficie dove si trova la luce (per una luce dietro una superficie trasmissiva l'orizzonte è quello posteriore), confrontando la normale perturbata con `rec.ShadingNg`. Vale 1 dove la normale non è perturbata. Lo stesso fattore entra nel peso del rimbalzo BSDF (§4.6); non c'è un fattore sul lato della vista.

**Gather delle caustiche (con `--caustics on`).** Su una superficie diffusa il contributo della photon map (§2.4) viene stimato con una density estimate ai k-nearest e **sommato** alla luce diretta. Per non contare due volte la luce trasmessa, quando le caustiche sono attive le interfacce **trasmissive lisce** (vetro/acqua) diventano **opache** agli shadow ray NEE: la luce rifratta (e tinta dall'assorbimento del vetro) è fornita solo dalla photon map, così il vetro proietta un'ombra rifrattiva corretta e colorata invece di un riempimento dritto. Vetro rough/frosted e percorso `--caustics off` restano invariati.

**Vedi:** [Path Tracing e Illuminazione §2](./path-tracing-and-lighting.md) per la trattazione completa della NEE.

### 4.6 Sample (Illuminazione Indiretta)

```csharp
BsdfSample? mis = material.Sample(viewDir, rec);
if (mis.HasValue)
    return ShadeSampleBounce(material, in rec, mis.Value, depth, emitted, directLight,
                             currentAbsorption, pathThroughput, ref mediums, ref iors, causticState);
return emitted + directLight;   // null: il cammino è assorbito qui
```

Ogni materiale campiona con `Sample` (Lambertian, Metal, Dielectric, OpenPbr, MixMaterial); `Emissive` usa il default dell'interfaccia, che ritorna null. Non c'è un percorso di fallback: un `Sample` nullo termina il cammino. Il `BsdfSample` riporta direzione `Wo`, valore `F` (senza coseno), `Pdf` in angolo solido, flag `IsDelta`, la transizione di mezzo (`Transition`) e un'eventuale `NextSegmentAbsorption` (Beer-Lambert del segmento successivo).

In `ShadeSampleBounce` il peso del rimbalzo è:

```csharp
attenuation = s.IsDelta
    ? s.F                                                          // lobo delta: F porta già tutto il peso
    : s.F * (absNdotWo * LightSideTerminator(in rec, s.Wo) / s.Pdf);  // non-delta, absNdotWo = |N.Wo|
```

Il fattore di shadow terminator è lo stesso della NEE (§4.5), perché appartiene all'integrando. Il ramo successivo riceve `prevBsdfPdf = s.Pdf` e `prevIsDelta = false` per un lobo non-delta, `prevBsdfPdf = 0` e `prevIsDelta = true` per un lobo delta: così `WeightEmission` e `SampleSky` scelgono fra peso MIS e peso pieno (§4.4). Su una transizione `Enter`/`Exit` il renderer copia e aggiorna la pila dei mezzi e quella degli IOR; un `Enter` in un mezzo omogeneo che diffonde passa al random walk del subsurface (vedi [MediumInterface](./medium-interface.md)).

- **Lambertian** → campionamento cosine-weighted nell'emisfero
- **Metal** → GGX con campionamento delle normali visibili più il lobo di scattering multiplo; `fuzz = 0` è uno specchio delta
- **Dielectric** → liscio: riflessione o rifrazione delta scelta con Fresnel; rough: microfacet di Walter 2007, riflessione e trasmissione non-delta
- **OpenPbr** (anche il tipo `disney`) → scelta stocastica di un lobo (fuzz, coat, metallo, interfaccia dielettrica, diffuso, subsurface sottile), poi `F` e `Pdf` dell'intera miscela nella direzione scelta; i lobi lisci sono delta
- **MixMaterial** → sceglie un figlio con probabilità pari al peso della miscela (vedi [MIS §5.3](./multiple-importance-sampling.md))

### 4.7 Russian Roulette

```csharp
Vector3 nextThroughput = pathThroughput * attenuation;
int bouncesUsed = _maxDepth - depth;
if (bouncesUsed >= _rrMinBounces)
{
    float survivalProb = Max(nextThroughput.X, nextThroughput.Y, nextThroughput.Z);
    survivalProb = Clamp(survivalProb, _rrMinSurvival, 0.95f);

    if (RandomFloat() > survivalProb)
        return emitted + directLight;  // Path terminato

    attenuation    /= survivalProb;    // Compensazione energetica
    nextThroughput /= survivalProb;
}
```

Dopo `_rrMinBounces` bounce, ogni path viene terminato con probabilità `1 - survivalProb`. I path sopravvissuti vengono "potenziati" dividendo per `survivalProb`, mantenendo lo stimatore Monte Carlo **unbiased** (non introduce bias sistematico).

La probabilità di sopravvivenza è il canale massimo della throughput accumulata lungo il path (PBRT §13.7.1): path che trasportano poca energia vengono eliminati più frequentemente. Il floor `_rrMinSurvival` impedisce boost eccessivi (max 6.7× in scene normali, 2× in scene indirette).

**Vedi:** [Path Tracing e Illuminazione §3](./path-tracing-and-lighting.md) per l'analisi di adattività.

### 4.8 Composizione Finale del Bounce

```
radiance = emitted + directLight + attenuation × indirect
```

Dove:
- `emitted` = auto-illuminazione della superficie, già pesata da `WeightEmission` (zero per materiali non emissivi)
- `directLight` = contributo NEE, già completo: la BSDF è dentro l'integrando (`Evaluate × |N.L| × terminator × visibilità`), quindi non viene moltiplicato per `attenuation`
- `attenuation` = peso del campione BSDF (§4.6), eventualmente diviso per la probabilità di sopravvivenza della RR
- `indirect` = radiance ricorsiva dal bounce successivo (ripulita da NaN/Inf; alla superficie primaria passa per il clamp indiretto opzionale)

Se `Sample` ritorna null (es. `Emissive`, che assorbe tutto) o la RR termina il path:
```
radiance = emitted + directLight
```

### 4.9 Sky Fallback

Se il raggio non colpisce nulla:

```csharp
return SampleSky(currentRay, prevBsdfPdf, prevIsDelta, causticState);
```

`SampleSky` legge il cielo con `SkySettings.Sample(...)`: raggi di camera e bounce delta vedono anche il sole analitico, i bounce non-delta solo il corpo del cielo (il sole arriva dalla NEE); sulle HDRI un bounce glossy legge un livello di mip scelto dalla sua pdf. Se il cielo è campionato dalla NEE (`EnvironmentLight`) e il bounce non è delta, la radianza riceve il peso MIS `MisWeight(prevBsdfPdf, pEnv)`; altrimenti passa a peso pieno. Il cielo per tipo:
- **Flat** → colore uniforme
- **Gradient** → interpolazione zenith/horizon/ground + sun disk con glow
- **HDRI** → bilinear sampling della mappa equirettangolare nella direzione del raggio

---

## Fase 5 — Post-Processing (per campione e per pixel)

### 5.1 Firefly Clamp (per campione)

```csharp
sample = ClampRadiance(sample);
```

Applicato a ogni campione individuale PRIMA dell'accumulo:

1. **NaN/Inf guard** — qualsiasi componente non-finita diventa zero.
2. **Luminance-preserving clamp (opzionale)** - solo con `-C <n>`: se la luminanza del campione supera `MaxSampleRadiance`, il vettore viene scalato uniformemente. Questo preserva la tinta del colore (niente hue shift) eliminando i picchi estremi da caustiche, boost RR o lobi glossy stretti. È spento per default perché la soglia è una radianza assoluta e toglie anche luce legittima (biased). Il clamp indiretto (`--indirect-clamp-factor`, default 0.25, attivo solo con `-C`) è applicato **una sola volta**, relativo alla camera, sul contributo indiretto pesato per la throughput alla superficie primaria, non accumulato a ogni rimbalzo.

### 5.2 Media dei Campioni

```csharp
Vector3 linearColor = cumulativeColor / actualSamples;
```

Il colore finale del pixel è la media aritmetica di tutti i campioni stratificati. Questo è il pixel HDR lineare.

### 5.3 Gestione del colore: esposizione, bilanciamento del bianco, view transform

```csharp
pixels[j, i] = _display.Apply(linearColor * _exposureScale);
```

**File:** `Rendering/ColorManagement.cs`. Lo spazio di lavoro è Rec.709 lineare (primarie sRGB, bianco D65): colori della scena, texture decodificate e radianza sono in questo spazio. `DisplayTransform.Apply` riceve la radianza già moltiplicata per l'esposizione (`2^EV`) e applica, nell'ordine:

1. **Bilanciamento del bianco** (opzionale, `white_balance` della camera o `--white-balance`): adattamento cromatico Bradford dalla cromaticità della temperatura scelta (luogo del corpo nero sotto 3500 K, luogo della luce diurna CIE sopra 4500 K, miscela lineare in mezzo) al bianco D65 dello schermo. 6504 K è l'identità.
2. **View transform** (`--view`):
   - `agx` (default): Rec.2020, matrice di inset (avvicina le primarie al bianco, così la luce satura molto intensa sfuma verso il bianco invece di saturare un canale e virare di tinta), codifica log2 su 16.5 stop attorno a 0.18, sigmoide (polinomio di sesto grado), look opzionale (`--look punchy|golden`, CDL), outset, linearizzazione 2.2, ritorno in Rec.709.
   - `aces`: ACES 1.x Output Transform per schermo sRGB 100 nit (RRT con glow e red modifier, desaturazione globale, curve tonali segmentate, ODT con compensazione dim surround), trascritta dal CTL dell'Academy; verificata contro OpenColorIO entro 7e-4 di valore di schermo (`ColorManagementTests`).
   - `standard`: nessun tone mapping, taglio a [0, 1].
   - `legacy`: la curva usata prima della review (approssimazione di Narkowicz applicata per canale, poi gamma 2.2 pura), conservata per riprodurre render precedenti.
3. **Codifica sRGB** a tratti (IEC 61966-2-1), non una potenza 2.2: le due differiscono soprattutto nelle ombre.

AgX e ACES sono curve diverse: un grigio 0.18 va a 0.50 con AgX, 0.36 con ACES 1.x, 0.55 con `legacy`.

---

## Fase 6 — Salvataggio dell'Immagine

**File:** `Program.cs` · **Metodo:** `SaveImage()`

L'array `Vector3[height, width]` (valori sRGB [0, 1]) viene quantizzato da `Rendering/DisplayQuantizer.cs` e convertito in immagine tramite ImageSharp. A 8 bit, per default, con dithering triangolare di ±1 livello (hash deterministico del pixel): toglie le bande nei gradienti morbidi e lascia ogni pixel con la media esatta (`--dither off` arrotonda e basta). `--bit-depth 16` scrive un PNG a 16 bit per canale. Il formato di output è determinato dall'estensione del file (`-o`): PNG (lossless, default), JPEG (lossy), BMP.

Ogni canale viene convertito con clamp e cast intero: `byte channel = (byte)Math.Clamp((int)(value * 255.999f), 0, 255)`.

**Output HDR (`-o *.exr`).** Con estensione `.exr` la Fase 5 (tone mapping) viene saltata per il file principale: `Program.SaveExrBeauty()` scrive la beauty **scene-linear pre-esposizione** catturata dal renderer (post-denoise se il denoiser è attivo) come OpenEXR single-part scanline tramite `Rendering/ExrImage.cs` — canali `R,G,B` half-float, compressione ZIP (blocchi da 16 scanline, framing zlib, fallback raw quando il blocco non comprime), attributi spec-compliant + cromaticità Rec.709. Gli AOV richiesti con `--aov` vengono incorporati come layer dello stesso file (`albedo.R/G/B`, `normal.X/Y/Z`, `Z` float32 con sentinella `-1`, `variance.R/G/B`) salvo `--aov-format` espliciti. Il lettore simmetrico è `Textures/ExrLoader.cs` (`LoadChannels`, senza clamp).

---

## Appendice A — Mappa dei Metodi e Contratti

| Metodo | File | Chiamante | Frequenza | Thread-safe? |
|--------|------|-----------|-----------|:----:|
| `SceneLoader.Load()` | SceneLoader.cs | Program.Main | 1× per run | N/A (single-thread) |
| `Renderer()` costruttore | Renderer.cs | Program.Main | 1× per run | N/A (single-thread) |
| `Renderer.Render()` | Renderer.cs | Program.Main | 1× per run | Sì (Parallel.For) |
| `Camera.GetRay()` | Camera.cs | Render loop | W×H×N volte | Sì (readonly + thread-local PRNG) |
| `TraceRay()` | Renderer.cs | Render loop | W×H×N×bounces | Sì (readonly state) |
| `ComputeDirectLighting()` | Renderer.cs | ShadeSurface (via DirectLightingAt) | Per ogni hit con `NeedsDirectLighting` | Sì |
| `ILight.ApproximatePower()` | Lights/*.cs | Costruttore Renderer | 1× per luce | **Solo single-thread** |
| `ILight.IlluminateAndTestStratified()` | Lights/*.cs | ComputeDirectLighting (via SampleLight) | Per ogni hit × luce × sample | Sì |
| `IMaterial.Sample()` | Materials/*.cs | ShadeSurface | Per ogni hit | Sì (sampler per thread) |
| `IMaterial.Evaluate()` / `Pdf()` | Materials/*.cs | ComputeDirectLighting (via DirectIntegrand), Sample | Per ogni hit × luce non-ombrata | Sì (pure function) |
| `IMaterial.Emit()` | Materials/*.cs | ShadeSurface | Per ogni hit | Sì (pure function) |
| `SkySettings.Sample()` | SkySettings.cs | SampleSky | Per ogni ray-miss | Sì (readonly) |

### Contratti chiave

**`ApproximatePower()` vs `IlluminateAndTest()`:**
- `ApproximatePower(sceneBounds)` è **solo per scene classification** (costruttore Renderer). Restituisce il flusso radiante totale della luce (Rec.709 luminance), deterministico, receiver-independent, finito (usa `sceneBounds` per directional/environment).
- `IlluminateAndTest()` / `IlluminateAndTestStratified()` sono per il **rendering**. Usano PRNG, dividono per ShadowSamples, testano le ombre.
- I due metodi non condividono il path di esecuzione. Modificare uno non influisce sull'altro.

**`Evaluate()` / `Pdf()` / `Sample()`:**
- `Evaluate(V, L, rec)` restituisce la BSDF completa (colori e Fresnel inclusi) **senza** il coseno; la NEE la moltiplica per $|N \cdot L|$, il fattore di shadow terminator e `lightColor`.
- `Sample(V, rec)` restituisce un `BsdfSample` il cui `F` coincide con `Evaluate` e il cui `Pdf` coincide con `Pdf` nella direzione campionata (per i lobi non-delta); il renderer ne ricava il peso `F × |cos| × terminator / Pdf`, o `F` per un lobo delta.
- È un'unica descrizione del materiale: NEE e rimbalzo BSDF stimano lo stesso integrando, e il MIS li combina con `Pdf`.

**`prevBsdfPdf` / `prevIsDelta`:**
- Propagati bounce-per-bounce via i parametri di `TraceRay`.
- Camera ray → `prevIsDelta = true`, emissione sempre mostrata.
- Bounce delta (specchio, vetro liscio, lobi lisci di OpenPbr) → `prevIsDelta = true`, emissione mostrata.
- Sample non-delta → `prevIsDelta = false`, `prevBsdfPdf = pdf dal BsdfSample`, emissione dei `Emissive` registrati e cielo campionato dalla NEE pesati con l'euristica MIS al prossimo hit; gli altri emettitori passano integri.
- Evento di scattering in un mezzo → `prevIsDelta = false`, `prevBsdfPdf = pdf della phase function`.

---

## Appendice B — Diagramma di Flusso di TraceRay

```
TraceRay(ray, depth, prevBsdfPdf, prevIsDelta, mediums, iors, ...)
│
├── depth ≤ 0 o throughput trascurabile? ── → return Zero
│
├── world.Hit(ray)? (salta le superfici nascoste a questa categoria di raggio)
│   ├── NO ──────────────────────────────── → return SampleSky(ray, prevBsdfPdf, prevIsDelta)
│   └── YES → ShadeSurface
│       │
│       ├── Apply normal map / bump / autobump (se presenti)
│       │
│       ├── Compute emitted (WeightEmission):
│       │   ├── prevIsDelta? → emitted = raw (full weight)
│       │   ├── material is registered Emissive? → w_bsdf = prevBsdfPdf/(prevBsdfPdf+p_light)
│       │   └── unregistered emitter → emitted = raw (full weight)
│       │
│       ├── Compute directLight:
│       │   ├── material.NeedsDirectLighting? → ComputeDirectLighting(...):
│       │   │       Σ wNee × lightColor × Evaluate × |N.L| × terminator × Tr
│       │   └── altrimenti (Emissive) → Vector3.Zero
│       │
│       └── material.Sample(V, rec)?
│           ├── NO (null) → return emitted + directLight
│           └── YES (BsdfSample { Wo, F, Pdf, IsDelta, Transition, NextSegmentAbsorption })
│               └── ShadeSampleBounce:
│                   ├── attenuation = IsDelta ? F : F × |N.Wo| × terminator / Pdf
│                   ├── Russian Roulette sulla throughput:
│                   │   ├── bouncesUsed < minBounces? → skip RR
│                   │   ├── random > survivalProb? → return emitted + directLight
│                   │   └── survived → attenuation /= survivalProb
│                   ├── Transition Enter/Exit? → aggiorna pile mezzi/IOR
│                   │   (Enter in mezzo che diffonde → RandomWalkSubsurface)
│                   └── indirect = TraceRay(scattered, depth-1,
│                                           prevBsdfPdf = IsDelta ? 0 : Pdf, prevIsDelta = IsDelta)
│                       return emitted + directLight + attenuation × indirect
```
