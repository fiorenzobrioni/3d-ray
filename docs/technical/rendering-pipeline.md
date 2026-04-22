# Pipeline di Rendering: dall'YAML al Pixel

Questo documento descrive il flusso architetturale completo del motore 3D-Ray, dal caricamento della scena alla scrittura del pixel finale. Non duplica la trattazione matematica dei documenti esistenti — la integra con una mappa navigabile che collega le fasi, i file sorgente, gli invarianti e i contratti tra componenti.

**Documenti correlati:**
- [Path Tracing e Illuminazione](./path-tracing-and-lighting.md) — NEE, Russian Roulette, campionamento HDRI, Sphere Light
- [Modello di Shading e Materiali](./shading-model.md) — Disney BSDF, Fresnel, Normal Mapping
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
       │  (world, camera, lights, ambientLight, sky)
       ▼
┌─────────────┐
│  Renderer   │  Costruttore: scene analysis + configurazione RR
│ constructor │  Render():    loop parallelo sui pixel
└──────┬──────┘
       │  Per ogni pixel: √N×√N campioni stratificati
       ▼
┌─────────────┐
│  TraceRay   │  Ricorsione: hit → normal map → emission → NEE → scatter → RR
└──────┬──────┘
       │  Radiance HDR lineare
       ▼
┌─────────────┐
│ Post-process│  Firefly clamp → ACES tone map → gamma 2.2
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

2. **`EnvironmentLight`** — se il cielo supporta il campionamento diretto (`CanSampleDirectly` = true per HDRI e gradient sky con sun disk), viene creato un `EnvironmentLight` e aggiunto alla lista.

Se non ci sono luci e il YAML non ha una sezione `lights:` esplicita, viene aggiunto un lighting di default (una directional + una point).

**Contratto:** Il parametro `shadowSamplesOverride` da CLI (`-S`) ha la precedenza sul valore per-luce del YAML. Se null, ogni luce usa il proprio valore.

### 1.7 Costruzione del Cielo

`BuildSkySettings()` crea un oggetto `SkySettings` in base al tipo:

- **`flat`** (default) — colore uniforme dal campo `background`.
- **`gradient`** — zenith/horizon/ground con interpolazione, più sun disk opzionale.
- **`hdri`** — carica il file `.hdr` tramite `HdrLoader`, costruisce l'`EnvironmentMap` con CDF per importance sampling.

### 1.8 Output

`Load()` restituisce una tupla `(IHittable world, Camera camera, List<ILight> lights, Vector3 ambientLight, SkySettings sky)` pronta per il Renderer.

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

### 2.3 Registrazione degli Emitter (per il Double-Counting Guard)

```csharp
_registeredEmitterMaterials = lights
    .OfType<GeometryLight>()
    .Select(gl => gl.Material)
    .ToHashSet();
```

Questo set viene usato da `TraceRay` per sapere quali materiali emissivi sono raggiungibili dalla NEE. Solo per questi l'emissione viene soppressa dopo un bounce diffuso (dove la NEE ha già contato il contributo diretto).

**Vedi:** [Path Tracing e Illuminazione §2.2](./path-tracing-and-lighting.md) per la trattazione completa del double-counting.

---

## Fase 3 — Render Loop

**File:** `Renderer.cs` · **Metodo:** `Render(int width, int height)`

### 3.1 Parallelizzazione

Il rendering usa `Parallel.For` sulle **scanline** (righe di pixel):

```csharp
Parallel.For(0, height, new ParallelOptions {
    MaxDegreeOfParallelism = Environment.ProcessorCount
}, j => { ... });
```

Ogni thread elabora una scanline completa in modo indipendente. Non c'è stato condiviso mutabile durante il rendering — `_world`, `_lights`, `_camera` e tutti i parametri sono readonly. L'unico stato thread-local è il PRNG (basato su `ThreadLocal<Random>` in `MathUtils`).

Il progresso viene stampato ogni 20 righe tramite `Interlocked.Increment`.

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

Il numero di campioni effettivi è sempre un quadrato perfetto: `-s 20` → `5×5 = 25`. Per ogni campione:

1. **Coordinate UV** nel viewport: `u = (i + jitter) / width`, `v = (height - j - 1 + jitter) / height`
2. **Generazione del raggio** tramite `Camera.GetRay(u, v)`
3. **Tracing** con `TraceRay(ray, maxDepth)`
4. **Firefly clamp** sul campione singolo
5. **Accumulo** nel colore cumulativo

Dopo tutti i campioni, il colore medio viene tone-mappato:

```csharp
Vector3 linearColor = cumulativeColor / actualSamples;
pixels[j, i] = AcesToneMap(linearColor);
```

### 3.3 Generazione del Raggio (Camera Thin-Lens)

**File:** `Camera.cs`

La camera implementa il modello **thin-lens** per il depth of field:

1. Il punto di origine del raggio viene perturbato casualmente all'interno di un disco di raggio `aperture / 2` sul piano della lente.
2. La direzione punta dal punto perturbato verso il punto corrispondente sul piano focale a distanza `focal_dist`.

Con `aperture = 0` il disco degenera in un punto (pinhole) e tutti gli oggetti sono a fuoco.

---

## Fase 4 — TraceRay (Il Cuore del Path Tracer)

**File:** `Renderer.cs` · **Metodo:** `TraceRay(Ray ray, int depth, float prevBsdfPdf, bool prevIsDelta, Vector3 currentAbsorption = default)`

Questa è la funzione ricorsiva che risolve l'equazione del rendering. Ogni invocazione rappresenta un **bounce** (rimbalzo) del raggio nella scena.

### 4.1 Condizione di Uscita

```csharp
if (depth <= 0) return Vector3.Zero;
```

Se il raggio ha esaurito i bounce, restituisce nero (energia zero). Il parametro `maxDepth` di default è 8 (CLI `-d`); in pratica la Russian Roulette termina la maggior parte dei path prima di questo limite. Per scene con vetri impilati alzalo a 16+ (vedi [Profili di Rendering](../reference/profili-di-rendering.md)).

### 4.2 Hit Test

```csharp
if (!_world.Hit(ray, Epsilon, Infinity, ref rec))
    return CalculateSkyColor(ray);
```

Il raggio viene testato contro il world (BVH + primitivi non-BVH). Se non colpisce nulla, il raggio è "sfuggito" dalla scena → campiona il cielo.

**Vedi:** [Strutture di Accelerazione](./acceleration-structures.md) per il funzionamento del BVH.

Il `HitRecord` risultante contiene: punto di hit, normale, UV, tangente/bitangente, materiale, front/back face, e il seed dell'oggetto (per texture procedurali).

### 4.3 Normal Map

```csharp
if (material?.NormalMap != null && rec.Tangent.LengthSquared() > 0.5f)
    ApplyNormalMap(ref rec, material.NormalMap);
```

Se il materiale ha una normal map, la normale del `HitRecord` viene perturbata **prima** di qualsiasi calcolo di shading. La trasformazione usa la matrice TBN (Tangent-Bitangent-Normal) ortonormalizzata con Gram-Schmidt. Le back-face invertono T e B per preservare l'handedness.

**Punto chiave:** la perturbazione avviene in-place su `rec.Normal`. Tutto il codice successivo (emissione, NEE, scatter) usa la normale perturbata.

**Vedi:** [Modello di Shading §Normal Mapping](./shading-model.md) per la matematica TBN.

### 4.4 Emissione (con MIS balance heuristic)

```csharp
Vector3 raw = material.Emit(u, v, localPoint, objectSeed, frontFace);
emitted = WeightEmission(raw, material, ray, prevBsdfPdf, prevIsDelta);
```

Il peso dipende dallo stato del bounce precedente:

| Percorso | `prevIsDelta` | `prevBsdfPdf` | Emissione | Motivo |
|----------|:-:|:-:|:-:|---|
| Camera → superficie | `true` | — | ✅ Piena | Il raggio primario non può essere campionato da NEE |
| Mirror / glass → emissivo | `true` | — | ✅ Piena | Bounce delta: nessuna NEE può raggiungerlo |
| Disney sample (non-delta) → emissivo registrato | `false` | `> 0` | ⚖️ MIS | `w_bsdf = prevBsdfPdf / (prevBsdfPdf + p_light)` |
| Lambert/Metal/Mix Scatter → emissivo registrato | `false` | `0` | ❌ Soppressa | Modalità legacy: NEE ha già contato l'emettitore |
| Qualunque → emissivo **non registrato** | — | — | ✅ Piena | Back-face, non-ISamplable — la NEE non può raggiungerlo |

### 4.5 Direct Lighting (NEE) — `ComputeDirectLighting`

```csharp
bool needsLightSampling = material?.NeedsDirectLighting ?? true;
if (needsLightSampling)
    directLight = ComputeDirectLighting(rec, ray, material);
```

La NEE viene attivata solo per materiali che rispondono alla luce diretta. Le sorgenti di luce stesse (`Emissive`) ritornano `NeedsDirectLighting = false` e saltano l'intero passaggio.

**Struttura interna di `ComputeDirectLighting`:**

```
Per ogni luce nella scena:
    Per ogni shadow sample (1 per point/spot/directional, N per area/sphere):
        1. Campiona un punto sulla luce (stratificato per area/sphere)
        2. Lancia shadow ray dal hit point verso il punto campionato
        3. Se non in ombra:
           brdf = material.EvaluateDirect(toLight, toEye, normal)
           accumula += lightColor × brdf
    Somma al risultato (non media — la pre-divisione per ShadowSamples
    è già nella formula energetica di ogni luce)
```

**Dispatch per tipo di luce nel shadow loop:**

- `AreaLight` → `IlluminateAndTestStratified(hitPoint, normal, world, sampleIndex)`
- `SphereLight` → `IlluminateAndTestStratified(hitPoint, normal, world, sampleIndex)`
- Tutte le altre → `IlluminateAndTest(hitPoint, normal, world)` (generico)

La distinzione esiste perché area e sphere light supportano campionamento stratificato (il sample index determina la cella nella griglia `√N×√N`), mentre le altre luci sono puntuali e non ne hanno bisogno.

**Contratto EvaluateDirect:** restituisce la risposta BRDF (diffusa + speculare) **senza** il colore/albedo del materiale. L'albedo è applicato separatamente dalla scatter attenuation in `TraceRay`, mantenendo la coerenza energetica tra illuminazione diretta e indiretta.

**Vedi:** [Path Tracing e Illuminazione §2](./path-tracing-and-lighting.md) per la trattazione completa della NEE.

### 4.6 Scatter / Sample (Illuminazione Indiretta)

```csharp
// Percorso preferito: MIS via Sample()
BsdfSample? mis = material.Sample(viewDir, rec);
if (mis.HasValue)
    return ShadeSampleBounce(material, rec, mis.Value, depth, emitted, directLight, currentAbsorption);

// Fallback legacy: Scatter() + convenzione IsDeltaScatter
if (material.Scatter(ray, rec, out Vector3 attenuation, out Ray scattered))
{
    // Russian Roulette...
    bool nextIsDelta = material.IsDeltaScatter;
    Vector3 indirect = TraceRay(scattered, depth - 1,
                                 prevBsdfPdf: 0f,
                                 prevIsDelta: nextIsDelta,
                                 currentAbsorption: currentAbsorption);
    return emitted + attenuation * (directLight + indirect);
}
```

Il renderer preferisce la tripla simmetrica `Sample / Evaluate / Pdf` quando il materiale la implementa (Disney BSDF): il sample restituisce direzione, F, PDF, flag `IsDelta` e un'eventuale `NextSegmentAbsorption` per il medium-switch, così il bounce successivo può calcolare il peso MIS corretto per l'emissione.

Per i materiali "legacy" (Lambertian, Metal, Dielectric, Mix) il renderer cade su `Scatter()` e passa `prevBsdfPdf = 0` al ramo successivo. La convenzione `prevBsdfPdf = 0` attiva la modalità "NEE replaced emission": al prossimo hit gli emettitori registrati vedono il loro contributo azzerato (evitando il doppio conteggio con NEE sulla superficie corrente). Se invece il bounce è delta (`material.IsDeltaScatter == true` — Dielectric, Metal con `fuzz=0`), `nextIsDelta = true` dice al renderer che l'emissione deve passare a pieno peso (una NEE non può raggiungere una BSDF delta, quindi non c'è nulla da sopprimere).

- **Lambertian** → rimbalzo cosine-weighted casuale nell'emisfero
- **Metal** → riflessione GGX importance-sampled con roughness
- **Dielectric** → riflessione o rifrazione (Schlick + Snell) — delta BSDF
- **Disney BSDF** → selezione stocastica tra lobi (diffuse, specular, clearcoat, transmission, sheen, multi-scatter, diff-trans) con VNDF sampling e MIS balance heuristic

L'`attenuation` è il colore/albedo del materiale moltiplicato per i fattori energetici del bounce (Fresnel, compensazione lobe, ecc.).

### 4.7 Russian Roulette

```csharp
int bouncesUsed = _maxDepth - depth;
if (bouncesUsed >= _rrMinBounces)
{
    float survivalProb = Max(Luminance(attenuation), _rrMinSurvival);
    survivalProb = Min(survivalProb, 0.95f);

    if (RandomFloat() > survivalProb)
        return emitted + attenuation * directLight;  // Path terminato

    attenuation /= survivalProb;  // Compensazione energetica
}
```

Dopo `_rrMinBounces` bounce, ogni path viene terminato con probabilità `1 - survivalProb`. I path sopravvissuti vengono "potenziati" dividendo per `survivalProb`, mantenendo lo stimatore Monte Carlo **unbiased** (non introduce bias sistematico).

La probabilità di sopravvivenza è legata alla luminanza dell'attenuazione: path che trasportano poca energia vengono eliminati più frequentemente. Il floor `_rrMinSurvival` impedisce boost eccessivi (max 6.7× in scene normali, 2× in scene indirette).

**Vedi:** [Path Tracing e Illuminazione §3](./path-tracing-and-lighting.md) per l'analisi di adattività.

### 4.8 Composizione Finale del Bounce

```
radiance = emitted + attenuation × (directLight + indirect)
```

Dove:
- `emitted` = auto-illuminazione della superficie (zero per materiali non emissivi)
- `attenuation` = colore/albedo × fattori energetici del materiale
- `directLight` = contributo NEE (ambient + tutte le luci)
- `indirect` = radiance ricorsiva dal bounce successivo

Se il materiale non fa scatter (es. `Emissive`, che assorbe tutto):
```
radiance = emitted + directLight
```

### 4.9 Sky Fallback

Se il raggio non colpisce nulla:

```csharp
return CalculateSkyColor(ray);
```

Che delega a `SkySettings.Sample(ray)`:
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
2. **Luminance-preserving clamp** — se la luminanza del campione supera `MaxSampleRadiance` (100), il vettore viene scalato uniformemente. Questo preserva la tinta del colore (niente hue shift) eliminando i picchi estremi da caustiche, boost RR, o compensazione dei lobe Disney.

### 5.2 Media dei Campioni

```csharp
Vector3 linearColor = cumulativeColor / actualSamples;
```

Il colore finale del pixel è la media aritmetica di tutti i campioni stratificati. Questo è il pixel HDR lineare.

### 5.3 ACES Filmic Tone Mapping + Gamma

```csharp
pixels[j, i] = AcesToneMap(linearColor);
```

La curva ACES Filmic (approssimazione Narkowicz) mappa la radiance HDR lineare in valori LDR:

$$\text{ACES}(x) = \frac{x(2.51x + 0.03)}{x(2.43x + 0.59) + 0.14}$$

Seguita da gamma correction `pow(x, 1/2.2)` per la conversione in spazio sRGB.

Il tone mapping produce un rolloff naturale degli highlight: le luci brillanti non "esplodono" in bianco piatto ma mantengono un gradiente morbido.

---

## Fase 6 — Salvataggio dell'Immagine

**File:** `Program.cs` · **Metodo:** `SaveImage()`

L'array `Vector3[height, width]` (valori sRGB [0, 1]) viene convertito in `Image<Rgba32>` tramite ImageSharp. Il formato di output è determinato dall'estensione del file (`-o`): PNG (lossless, default), JPEG (lossy), BMP.

Ogni canale viene convertito con clamp e cast intero: `byte channel = (byte)Math.Clamp((int)(value * 255.999f), 0, 255)`.

---

## Appendice A — Mappa dei Metodi e Contratti

| Metodo | File | Chiamante | Frequenza | Thread-safe? |
|--------|------|-----------|-----------|:----:|
| `SceneLoader.Load()` | SceneLoader.cs | Program.Main | 1× per run | N/A (single-thread) |
| `Renderer()` costruttore | Renderer.cs | Program.Main | 1× per run | N/A (single-thread) |
| `Renderer.Render()` | Renderer.cs | Program.Main | 1× per run | Sì (Parallel.For) |
| `Camera.GetRay()` | Camera.cs | Render loop | W×H×N volte | Sì (readonly + thread-local PRNG) |
| `TraceRay()` | Renderer.cs | Render loop | W×H×N×bounces | Sì (readonly state) |
| `ComputeDirectLighting()` | Renderer.cs | TraceRay | Per ogni hit diffuso/speculare | Sì |
| `ILight.ApproximatePower()` | Lights/*.cs | Costruttore Renderer | 1× per luce | **Solo single-thread** |
| `ILight.IlluminateAndTest()` | Lights/*.cs | ComputeDirectLighting | Per ogni hit × luce × sample | Sì |
| `IMaterial.Scatter()` | Materials/*.cs | TraceRay | Per ogni hit | Sì (thread-local PRNG) |
| `IMaterial.EvaluateDirect()` | Materials/*.cs | ComputeDirectLighting | Per ogni hit × luce non-ombrata | Sì (pure function) |
| `IMaterial.Emit()` | Materials/*.cs | TraceRay | Per ogni hit | Sì (pure function) |
| `SkySettings.Sample()` | SkySettings.cs | CalculateSkyColor | Per ogni ray-miss | Sì (readonly) |

### Contratti chiave

**`ApproximatePower()` vs `IlluminateAndTest()`:**
- `ApproximatePower(sceneBounds)` è **solo per scene classification** (costruttore Renderer). Restituisce il flusso radiante totale della luce (Rec.709 luminance), deterministico, receiver-independent, finito (usa `sceneBounds` per directional/environment).
- `IlluminateAndTest()` / `IlluminateAndTestStratified()` sono per il **rendering**. Usano PRNG, dividono per ShadowSamples, testano le ombre.
- I due metodi non condividono il path di esecuzione. Modificare uno non influisce sull'altro.

**`EvaluateDirect()` vs `Scatter()`:**
- `EvaluateDirect()` restituisce la risposta BRDF **senza albedo** (la moltiplica il caller con `lightColor`).
- `Scatter()` restituisce l'`attenuation` **con albedo** (diventa il moltiplicatore per la radiance indiretta).
- Entrambi usano lo stesso modello BRDF (GGX per Metal, Cook-Torrance per Disney), ma valutano in modi diversi: `EvaluateDirect` è analitico su una direzione specifica, `Scatter` è importance-sampled.

**`prevBsdfPdf` / `prevIsDelta`:**
- Propagati bounce-per-bounce via i parametri di `TraceRay`.
- Camera ray → `prevIsDelta = true`, emissione sempre mostrata.
- Bounce delta (mirror/refraction) → `prevIsDelta = true`, emissione mostrata.
- Sample MIS Disney → `prevIsDelta = false`, `prevBsdfPdf = pdf dal BsdfSample`, emissione pesata via balance heuristic al prossimo hit.
- Scatter legacy (Lambert/Metal/Mix) → `prevIsDelta = material.IsDeltaScatter`, `prevBsdfPdf = 0` — emissione dai GeometryLight registrati soppressa (NEE ha già contato), da altri emettitori passa integra.

---

## Appendice B — Diagramma di Flusso di TraceRay

```
TraceRay(ray, depth, prevBsdfPdf, prevIsDelta, currentAbsorption)
│
├── depth ≤ 0? ──────────────────────────── → return Zero
│
├── world.Hit(ray)? 
│   ├── NO ──────────────────────────────── → return CalculateSkyColor(ray)
│   └── YES
│       │
│       ├── Apply normal map (se presente)
│       │
│       ├── Compute emitted (MIS-weighted):
│       │   ├── prevIsDelta? → emitted = raw (full weight)
│       │   ├── material is registered Emissive? → w_bsdf = prevBsdfPdf/(prevBsdfPdf+p_light)
│       │   └── unregistered emitter → emitted = raw (full weight)
│       │
│       ├── Compute directLight:
│       │   ├── material.NeedsDirectLighting? → ComputeDirectLighting(...) con MIS balance
│       │   └── altrimenti (Emissive) → ambientLight
│       │
│       ├── material.Sample(V, rec)?  // percorso MIS preferito
│       │   └── YES (BsdfSample { Wo, F, Pdf, IsDelta, NextSegmentAbsorption })
│       │       └── ShadeSampleBounce → TraceRay(depth-1, s.Pdf, s.IsDelta, ...)
│       │
│       ├── material.Scatter()? // fallback per materiali legacy
│       │   ├── NO → return emitted + directLight
│       │   └── YES (attenuation, scattered)
│       │       │
│       │       ├── Russian Roulette:
│       │       │   ├── bouncesUsed < minBounces? → skip RR
│       │       │   ├── random > survivalProb? → return emitted + att × directLight
│       │       │   └── survived → attenuation /= survivalProb
│       │       │
│       │       ├── attenuation ≈ 0? → return emitted + att × directLight (early-out)
│       │       │
│       │       └── indirect = TraceRay(scattered, depth-1,
│       │                              prevBsdfPdf=0, prevIsDelta=material.IsDeltaScatter)
│       │           return emitted + attenuation × (directLight + indirect)
```
