# Guida di Riferimento per le Scene

Questo documento è un riferimento tecnico completo per la creazione e configurazione dei file di scena di 3D-Ray utilizzando il formato YAML. Offre una guida approfondita alla struttura del progetto, alla documentazione e alle best practices per scrivere scene di alta qualità.

---

### 1. **PANORAMICA DEL PROGETTO**
**3D-Ray** è un motore di ray-tracing ad alte prestazioni scritto in C# e .NET 10. Utilizza file YAML per descrivere scene 3D complete con:
- Physically-based rendering (PBR) con Disney Principled BSDF
- Path tracing avanzato con Next Event Estimation (NEE)
- Diversi tipi di luci (point, directional, spot, area, sphere)
- Texture procedurali e basate su immagini
- Normal mapping
- Operazioni booleane CSG
- Scene graph gerarchici (gruppi e template)

---

### 2. **STRUTTURA DEI FILE DI SCENA YAML**
Ogni file YAML di scena ha **5 sezioni principali** (ordine consigliato):
```yaml
imports:    # (opzionale) File YAML esterni da caricare
templates:  # (opzionale) Blueprint di oggetti riutilizzabili
world:      # Ambiente (cielo, terreno, mezzo globale)
cameras:    # Lista camere (o camera: per la forma legacy a camera singola)
lights:     # Sorgenti luminose esplicite
materials:  # Definizioni dei materiali
entities:   # Oggetti 3D (primitive, gruppi, istanze, CSG, mesh)
```

**Sistema di Coordinate Chiave:**
- **X** = destra
- **Y** = su
- **Z** = verso la camera (negativo = lontano)
- **Colori** = `[R, G, B]` con valori 0.0–1.0

---

### 3. **SEZIONE WORLD** — Configurazione dell'Ambiente

```yaml
world:
  sky:                                     # (opzionale) Emettitore globale dell'ambiente
    type: "flat"  # oppure "gradient" / "hdri"
    # ... vedi dettagli sotto
  ground:                                  # (opzionale) Pavimento autogenerato
    type: "infinite_plane"
    material: "floor_name"
    y: 0.0
  medium:                                  # (opzionale) Mezzo partecipante globale
    type: "homogeneous"
    # ... vedi dettagli sotto
```

Quando `world.sky` è omesso, viene usato un cielo flat azzurro-diurno `[0.5, 0.7, 1.0]`.

#### **Flat Sky** (colore uniforme, default):
```yaml
sky:
  type: "flat"
  color: [0.5, 0.7, 1.0]                  # Radianza uniforme su tutta la sfera
```
Un cielo flat partecipa a NEE (campionamento uniforme della sfera, pdf = 1/(4π))
quando la sua luminanza è > 0, allineato al comportamento dei "uniform world
backgrounds" di Cycles/Arnold. Imposta `color: [0, 0, 0]` per scene black-void
stile Cornell-box — in questo caso il loader esclude automaticamente il cielo
da NEE.

#### **Gradient Sky** (raccomandato per scene all'aperto):
```yaml
sky:
  type: "gradient"
  zenith_color:  [0.10, 0.30, 0.80]      # Parte superiore del cielo
  horizon_color: [0.65, 0.80, 1.00]      # Orizzonte
  ground_color:  [0.30, 0.25, 0.20]      # Riflesso del terreno
  sun:                                     # (opzionale)
    direction:  [-0.5, -1.0, -0.3]       # Direzione di PROPAGAZIONE della luce (sole → scena).
                                          # La posizione del sole è -direction: con [-0.5,-1,-0.3]
                                          # il sole è in alto a destra-davanti.
    color:      [1.0, 0.98, 0.85]
    intensity:  12.0
    size:       2.5                        # Dimensione angolare in gradi
    falloff:    48.0                       # Esponente bagliore (più alto = più nitido)
```
Il corpo del gradiente è campionato dal BSDF importance sampling sul percorso di
miss; solo il disco solare opzionale partecipa a NEE (cone-sampling all'interno
della sua dimensione angolare).

#### **HDRI/IBL** (per il massimo realismo):
```yaml
sky:
  type: "hdri"
  path: "hdri/studio.hdr"                 # Percorso relativo al file YAML
  intensity: 1.0                           # Moltiplicatore esposizione
  rotation: 90                             # Rotazione asse Y in gradi
```
Le HDRI sono importance-sampled tramite una CDF di luminanza sulla mappa
equirettangolare.

**Configurazioni Sky Predefinite:**
- **Noon** (gradiente pulito, sole luminoso)
- **Golden Hour** (sole basso e caldo, orizzonte saturato)
- **Sunset** (orizzonte arancione drammatico)
- **Night** (zenith/orizzonte molto fiochi, disco solare debole)
- **Overcast** (orizzonte uniforme, niente disco solare; oppure `flat` con grigio basso)
- **Studio** (`flat` con un colore neutro fioco per riempire il bounce indiretto)

#### **Volumetria (Mezzi Partecipanti)**:

3D-Ray supporta **quattro tipi di medium globali** (`homogeneous`, `height_fog`, `procedural`, `grid`) e **cinque phase function** (`isotropic`, `hg`, `rayleigh`, `double_hg`, `schlick`). Il campo `medium:` è a livello di `world`.

**Parametri comuni a tutti i tipi:**

| Campo | Tipo | Descrizione |
|---|---|---|
| `type` | string | `homogeneous` \| `height_fog` \| `procedural` \| `grid` |
| `sigma_a` | RGB | Coefficiente di assorbimento (oscuramento della luce) |
| `sigma_s` | RGB | Coefficiente di scattering (densità visiva della nebbia, fasci di luce) |
| `phase` | string | Phase function (default `isotropic`); se `g` è presente → `hg` |

**Tipo 1 — `homogeneous`** (densità costante, analitico, economico):
```yaml
medium:
  type: "homogeneous"
  sigma_a: [0.005, 0.005, 0.005]
  sigma_s: [0.06, 0.06, 0.07]
  phase: "hg"
  g: 0.85
```

**Tipo 2 — `height_fog`** (densità esponenziale in altezza, analitico):
```yaml
medium:
  type: "height_fog"
  sigma_a: [0.02, 0.02, 0.025]
  sigma_s: [0.25, 0.28, 0.32]
  y0: 0.0                              # Quota di riferimento (densità nominale)
  scale_height: 2.0                    # Distanza in Y per un calo 1/e della densità
  phase: "hg"
  g: 0.6
```

**Tipo 3 — `procedural`** (Perlin fBm, delta tracking):
```yaml
medium:
  type: "procedural"
  sigma_a: [0.01, 0.01, 0.01]
  sigma_s: [0.5, 0.5, 0.55]
  frequency: 0.45                      # Frequenza noise (world units)
  octaves: 4                           # Numero di ottave fBm (1-8)
  lacunarity: 2.0                      # Moltiplicatore frequenza fra ottave (≥1)
  gain: 0.55                           # Moltiplicatore ampiezza fra ottave (0.01-0.99)
  seed: 42                             # Seed deterministico del noise
  phase: "hg"
  g: 0.75
```

**Tipo 4 — `grid`** (griglia 3D inline o da file `.vol`, delta tracking + filtro di ricostruzione):
```yaml
# Variante A — dati inline (utile per griglie piccole, es. ≤ 8³)
medium:
  type: "grid"
  sigma_a: [0.1, 0.1, 0.1]
  sigma_s: [3.0, 3.0, 3.2]
  bounds_min: [-1.5, 0.5, -1.5]        # AABB world-space del volume
  bounds_max: [ 1.5, 3.5,  1.5]
  nx: 4                                # Risoluzione griglia (min. 2 per asse)
  ny: 4
  nz: 4
  interpolation: "trilinear"           # Opzionale: "trilinear" (default) o "tricubic"
  phase: "hg"
  g: 0.5
  data: [0.0, 0.0, ...]                # Array di nx*ny*nz float in [0,1], layout z-major

# Variante B — file binario esterno (consigliato per griglie grandi)
medium:
  type: "grid"
  sigma_a: [0.1, 0.1, 0.1]
  sigma_s: [3.0, 3.0, 3.2]
  interpolation: "tricubic"            # Smoothing Catmull-Rom; utile su griglie basso-res
  phase: "hg"
  g: 0.5
  file: "cloud-64x64x64.vol"           # Path relativo allo YAML; bounds e risoluzione dall'header del file
```

**Formato `.vol` (VOL1):** magic string `"VOL1"` (4 byte) + `nx`, `ny`, `nz` (3 × int32 little-endian) + `bounds_min.{x,y,z}`, `bounds_max.{x,y,z}` (6 × float32 little-endian) + `nx·ny·nz` float32 di densità, layout z-major (y outer, x inner dentro ogni slice z).

**Filtri di ricostruzione (`interpolation`):**

| Valore | Taps | Continuità | Quando usarlo |
|---|---|---|---|
| `trilinear` (default) | 8 | C⁰ | Default. Cheap, ma a risoluzioni basse (≤16³) la derivata salta ai confini delle celle → bande lineari visibili. |
| `tricubic` | 64 | C¹ | Catmull-Rom cardinal spline (τ = 0.5). ~8× costo per sample, ma rimuove i kink su griglie basso-res e levigna i dati binari. Risultato clampato in `[0,1]` per preservare l'invariante del majorant. Alias accettati: `cubic`, `catmull-rom`, `smooth`. |

Su griglie ad alta risoluzione (128³+) con densità smoothly varying i due filtri convergono visivamente — `trilinear` è sufficiente. Su griglie piccole inline o su dati binari 0/1, `tricubic` è il modo standard per nascondere gli artefatti (analogo a Arnold/Houdini "cubic" filter su VDB).

**Phase function disponibili:**

| Valore `phase` | Parametri | Uso tipico |
|---|---|---|
| `isotropic` | — | Scattering uniforme in tutte le direzioni (fumo denso, nubi spesse) |
| `hg` | `g` ∈ (-1, 1) | Henyey-Greenstein: `g > 0` forward, `g < 0` backward, `g = 0` ≈ isotropo |
| `rayleigh` | — | Scattering atmosferico `(3/16π)(1+cos²θ)`; cielo, aerial perspective |
| `double_hg` | `g1`, `g2`, `w` | Due lobi HG combinati con peso `w` ∈ [0,1]; nubi realistiche (Nubis) |
| `schlick` | `g` | Approssimazione razionale rapida di HG (senza sqrt) |

Esempi:
```yaml
# Cielo Rayleigh
phase: "rayleigh"

# Nube realistica tipo cumulo (forward g1=0.85 + lobo lato g2=-0.3)
phase: "double_hg"
g1: 0.85
g2: -0.3
w: 0.7

# HG fast
phase: "schlick"
g: 0.6
```

**Quale tipo di medium scegliere:**

| Tipo | Profilo di densità | Costo | Quando usarlo |
|---|---|---|---|
| `homogeneous` | Costante ovunque | Analitico, economico | Scene indoor, interni delimitati, ambienti subacquei chiusi, colonne di fumo confinate da geometria. **Da evitare quando l'illuminazione è solo `sky` + `sun` o HDRI** (vedi avviso sotto). |
| `height_fog` | Decadimento esponenziale con l'altitudine (`exp(-(y-y0)/H)`) | Analitico, economico | Scene outdoor illuminate da sky / sun / HDRI: aerial perspective, montagne all'alba, orizzonte sul mare, smog. **Scelta di default per ogni scena outdoor con illuminazione direzionale / ambientale.** |
| `procedural` | Perlin fBm (delta tracking) | Più rumoroso (+30–100% di tempo) | Nebbia a chiazze / irregolare, horror, god-ray non uniformi, foreste nebbiose, superfici d'acqua con foschia a macchie. |
| `grid` | Densità campionata su griglia 3D (inline o `.vol`) | Delta tracking + filtro voxel | Nubi localizzate, fumo da cache di simulazione, esplosioni, asset VFX hero. Il medium esiste solo dentro la sua AABB — fuori è vuoto e il resto della scena non è influenzato. |

> ⚠️ **Sky + sun + `homogeneous` = render nero.** Un medium globale `homogeneous` ha densità *costante* estesa all'infinito, quindi lo shadow ray Beer–Lambert verso il sole (o verso qualsiasi direzione del cielo) attraversa `exp(-σ_t · ∞) ≈ 0` e il direct lighting ambientale collassa a zero. Le luci spot/point/area/sphere hanno distanza finita e si comportano correttamente, ma se gli *unici* emettitori sono `sky` + `sun` (o HDRI) il render esce nero. Usa `height_fog` al posto suo — la sua profondità ottica verso lo zenit è limitata dallo `scale_height`, che è esattamente il modello "aerial perspective" usato da Arnold, V-Ray e Unreal. È il comportamento fisicamente corretto di `homogeneous` (le atmosfere reali non sono infinite), non un bug del renderer.

- **Uso:** Simula nebbia, fumo, foschia atmosferica, nubi, effetti subacquei.
- **Tip rendering:** `homogeneous` e `height_fog` sono analitici ed economici. `procedural` e `grid` usano delta tracking e sono più rumorosi — alza `-s` a 400/576/1024 e mantieni `-d 6-8`. Per scene con nebbia densa considera `-C 25`. Vedi [Profili di Rendering](./profili-di-rendering.md) §8 per la guida completa.
- **Effetti:** Luci spot → god-ray visibili; point light → aloni; directional → aerial perspective (con `height_fog`).
- **Fireflies con point/spot in nebbia:** l'attenuazione 1/d² diverge quando un evento di scattering cade vicino a un emettitore puntiforme/spot, producendo pixel isolati luminosi. Imposta `soft_radius` su quelle luci (vedi §8.1, §8.3) a un valore vicino al raggio fisico del bulbo (es. `0.15`–`0.30`).
- **Fireflies con area light in nebbia:** il termine `cosLight/d²` nel stimatore area può divergere ad angoli radenti in media densi. Imposta `soft_radius` sulle area light (vedi §8.4). Le sphere light usano uno stimatore ad angolo solido limitato per costruzione — non serve `soft_radius`. Considera anche `--indirect-clamp-factor 0.25` (CLI) per sopprimere aggressivamente gli spike nei bounce profondi.
- **Controllo avanzato firefly:** `--indirect-clamp-factor <f>` (default `1.0` = disabilitato) moltiplica la soglia `--clamp` per tutti i bounce indiretti. Es. `--clamp 100 --indirect-clamp-factor 0.25` usa clamp=25 a depth ≥ 1 — stile Cycles/Arnold "indirect clamp".
- **Light importance sampling:** `--light-sampling power` (default `all`) campiona una sola luce per evento NEE con probabilità ∝ `ApproximatePower`. Riduce drasticamente la varianza in scene con molte luci di luminosità mista. Usa `uniform` come baseline di confronto.

---

### 4. **SEZIONE CAMERA**
#### **Multi-Camera** (raccomandato):
```yaml
cameras:
  - name: "main"
    position: [0, 5, -8]
    look_at: [0, 0, 0]
    fov: 45
    aperture: 0.1
    focal_dist: 12
  - name: "top"
    position: [0, 12, 0.01]
    look_at: [0, 0, 0]
    fov: 35
    aperture: 0.0
    focal_dist: 12
  - name: "subject"
    position: [0, 2, -8]
    look_at: [0, 0, 0]
    fov: 45
    aperture: 0.1
    focal_pos: [0.5, 0.6, 1.0]            # fuoco su questo punto — vedi sotto
```

#### **Camera Singola** (legacy):
```yaml
camera:
  position: [0, 2, -8]                    # Posizione camera
  look_at: [0, 0, 0]                      # Punto di mira
  vup: [0, 1, 0]                          # Vettore "su" (per il rollio)
  fov: 60                                  # Campo visivo verticale (gradi)
  aperture: 0.1                            # Diametro lente (0 = pinhole)
  focal_dist: 8.0                          # Distanza dal piano di fuoco (scalare)
  # focal_pos: [0.5, 0.6, 1.0]            # Alternativa: fuoco su un punto 3D
```

**Uso dalla CLI:**
```bash
dotnet run ... -- -i scene.yaml --list-cameras      # Elenca le disponibili
dotnet run ... -- -i scene.yaml -c top -o top.png   # Per nome
dotnet run ... -- -i scene.yaml -c 1 -o cam1.png    # Per indice (base 0)
```

**⚠️ Profondità di Campo:** Quando `aperture > 0`, imposta `focal_dist` (o `focal_pos`) con la distanza / il punto effettivo del soggetto principale. Il default `focal_dist: 1.0` creerà una sfocatura estrema non voluta.

#### **`focal_pos` — fuoco su un punto (Arnold/Cycles "Focus Object")**
`focal_pos: [x, y, z]` è un'alternativa allo scalare `focal_dist`. Il loader calcola la distanza di fuoco come **proiezione** del vettore camera→focal-point sull'asse ottico:
```
forward    = normalize(look_at − position)
focusDist  = dot(focal_pos − position, forward)
```
Il piano focale è perpendicolare alla direzione di vista e passa per `focal_pos`, quindi il valore è una **proiezione, non una distanza euclidea**. Un focal point off-axis a `(3, 4, -5)` con camera all'origine e look lungo `−Z` produce focus distance `5`, non `√50 ≈ 7.07`. Stesso comportamento di Arnold ("Focus Object"), Cycles ("Focal Object/Distance") e RenderMan.

Quando entrambi `focal_pos` e `focal_dist` sono specificati, `focal_pos` vince (viene loggato un info message). `focal_pos` viene ignorato con un warning quando cade alle spalle della camera, coincide con essa o la camera è degenerata (`look_at == position`); in quel caso si usa lo scalare `focal_dist` come fallback.

---

### 5. **SEZIONE MATERIALI** — Sei Tipi
#### **5.1 Lambertian (Diffuso/Opaco)**
```yaml
- id: "matte_red"
  type: "lambertian"
  color: [0.8, 0.2, 0.1]
```
- Riflessione diffusa pura, senza riflessi speculari
- Più efficiente per grandi superfici (pareti, pavimenti)
- Supporta texture e normal_map

#### **5.2 Metal (Speculare/Specchio)**
```yaml
- id: "brushed_steel"
  type: "metal"
  color: [0.85, 0.85, 0.88]               # Tinta di riflettanza
  fuzz: 0.1                                # Rugosità: 0=specchio, 1=diffuso
```
- Superfici lucide e riflettenti
- Colori metallici basati sulla fisica
- Supporta texture e normal_map

#### **5.3 Dielectric (Vetro/Trasparente)**
```yaml
- id: "glass"
  type: "dielectric"
  refraction_index: 1.52                  # IOR (indice di rifrazione)
  color: [1.0, 1.0, 1.0]                  # (opzionale) Tinta
```
- Trasparente con rifrazione e riflessione di Fresnel
- IOR comuni: acqua=1.33, vetro=1.5-1.52, diamante=2.42

#### **5.4 Emissive (Auto-illuminato)**
```yaml
- id: "neon_blue"
  type: "emissive"
  color: [0.2, 0.4, 1.0]
  intensity: 8.0                           # Moltiplicatore di radianza
  texture: (opzionale)                     # Supporta texture procedurali
```
- Gli oggetti brillano ed emettono luce nella scena
- Range di intensità: 0.5–2 (bagliore sottile), 3–10 (neon visibile), 10–25 (pannello luminoso), 25–100 (sovraesposto)
- Partecipano alla NEE (Next Event Estimation) per ridurre il rumore
- Emettono solo dalle facce anteriori

#### **5.5 Disney Principled BSDF (PBR Unificato)**
```yaml
- id: "car_paint"
  type: "disney"  # Alias: "pbr", "disney_bsdf"
  color: [0.8, 0.2, 0.1]

  # ── Parametri classici Disney 2012 ──────────────────────────────────
  metallic: 0.0                            # 0=dielettrico, 1=metallo
  roughness: 0.3                           # 0=specchio, 1=diffuso
  subsurface: 0.0                          # Approssimazione subsurface (pelle, cera)
  specular: 0.5                            # Intensità speculare per dielettrici (F₀ × 0.08)
  specular_tint: 0.0                       # Tinta dello specular dielettrico verso base_color
  sheen: 0.0                               # Lucentezza radente (tessuti, velluto)
  sheen_tint: 0.5                          # Tinta dello sheen verso base_color
  clearcoat: 1.0                           # Energia del secondo lobo speculare
  clearcoat_gloss: 0.9                     # Rugosità legacy del clearcoat (slider Disney 2012)
  spec_trans: 0.0                          # 0=opaco, 1=rifrattivo (vetro)
  ior: 1.5                                 # Indice di rifrazione per spec_trans e Fresnel

  # ── Anisotropia (Burley 2012 §5.4) ──────────────────────────────────
  anisotropic: 0.0                         # 0=isotropo, 1=allungato sulla tangente
  anisotropic_rotation: 0.0                # 0..1 frazione di 2π intorno alla normale

  # ── Estensioni Disney 2015 ──────────────────────────────────────────
  diff_trans: 0.0                          # Lambert diffuse transmission (foglie, fogli)
  flatness: 0.0                            # Blend Lambert → HK-flat (Disney 2015)
  thin_walled: false                       # Disattiva la rifrazione: foglie, carta, tele sottili
  subsurface_color: [0.9, 0.6, 0.5]        # Tinta indipendente per subsurface/flatness/diff_trans

  # ── Assorbimento Beer-Lambert per vetri colorati ────────────────────
  transmission_color: [0.2, 0.8, 0.9]      # Colore del vetro raggiunto a transmission_depth
  transmission_depth: 0.0                  # Distanza (unità scena) a cui si raggiunge quel colore

  # ── Coat stile Arnold (override opzionali) ──────────────────────────
  coat_ior: 1.5                            # IOR del coat (default 1.5 = lacca)
  coat_roughness: -1.0                     # ≥ 0 abilita il coat stile Arnold; <0 usa clearcoat_gloss
  coat_normal_map: "textures/coat.png"     # Normal map dedicata al clearcoat
  sheen_roughness: 0.3                     # α dello sheen Charlie (0.04..1)

  # ── Thin-film iridescence (Belcour-Barla 2017) ──────────────────────
  thin_film_thickness: 0.0                 # Spessore del film in nanometri (0 = disabilitato)
  thin_film_ior: 1.5                       # IOR del film (η₂)

  # ── Texturing ───────────────────────────────────────────────────────
  texture: (opzionale)                     # Texture del base color
  normal_map: (opzionale)
  # Tutti i parametri scalari e i colour map sopra accettano la versione
  # *_texture, ad es. roughness_texture: { type: "image", path: "rough.png" }.
```

##### **Riepilogo proprietà Disney**
Riferimento a colpo d'occhio di ogni chiave Disney accettata dal loader.
Il campo `Stato` marca le chiavi che si comportano in modo diverso dalle
altre: quelle `Legacy` sono ancora onorate ma vanno sostituite nelle nuove
scene; quelle `Non usata` sono parsate per forward-compatibility ma non
hanno effetto sul renderer corrente (il loader emette un `Info` al
caricamento quando ne trova una).

| Proprietà | Tipo | Default | Range | Stato | Note |
|---|---|---|---|---|---|
| `color` | colore | obbligatorio | 0–1 | Core | Albedo di base (texturabile) |
| `metallic` | float | 0.0 | 0–1 | Core | 0 = dielettrico, 1 = conduttore |
| `roughness` | float | 0.5 | 0–1 | Core | 0 = specchio, 1 = diffuso |
| `specular` | float | 0.5 | 0–1 | Core | Scala F₀ dielettrici (F₀ ≈ 0.08 × valore) |
| `specular_tint` | float | 0.0 | 0–1 | Core | Tinge il Fresnel dielettrico col colore di base |
| `sheen` | float | 0.0 | 0–1 | Core | Alone radente (tessuti, velluto) |
| `sheen_tint` | float | 0.5 | 0–1 | Core | Tinge lo sheen col colore di base |
| `sheen_roughness` | float | 0.3 | 0.04–1 | Ext. | α Charlie NDF (Estevez-Kulla 2017) |
| `clearcoat` | float | 0.0 | 0–1 | Core | Secondo lobo speculare indipendente |
| `clearcoat_gloss` | float | 1.0 | 0–1 | **Legacy** | Slider Disney-2012; sostituito da `coat_roughness` |
| `coat_ior` | float | 1.5 | ≥ 1 | Coat | IOR del coat stile Arnold |
| `coat_roughness` | float | -1.0 | -1 oppure 0–1 | Coat | -1 = usa `clearcoat_gloss`; qualsiasi ≥ 0 attiva il path Arnold |
| `coat_normal_map` | path | — | — | Coat | Normal map dedicata al lobo coat |
| `spec_trans` | float | 0.0 | 0–1 | Core | 0 = opaco, 1 = vetro |
| `ior` | float | 1.5 | ≥ 1 | Core | Indice di rifrazione (speculare + trasmissione) |
| `transmission_color` | colore | `[1,1,1]` | 0–1 | Core | Colore interno a `transmission_depth` |
| `transmission_depth` | float | 0.0 | ≥ 0 | Core | Distanza Beer-Lambert (0 = sottile, tinta applicata una volta) |
| `anisotropic` | float | 0.0 | 0–1 | Aniso | 0 = isotropo, 1 = stirato lungo la tangente |
| `anisotropic_rotation` | float | 0.0 | 0–1 | Aniso | Frazione di 2π attorno alla normale |
| `subsurface` | float | 0.0 | 0–1 | 2015 | Blend Lambert ↔ lobo HK-flat |
| `subsurface_color` | colore | — | 0–1 | 2015 | Tinta per subsurface / flatness / diff_trans |
| `subsurface_radius` | `[R,G,B]` | — | ≥ 0 | **Non usata** | Parsata ma mai letta — riservata per una futura SSS random-walk |
| `diff_trans` | float | 0.0 | 0–1 | 2015 | Trasmissione diffusa (foglie, tele sottili) |
| `flatness` | float | 0.0 | 0–1 | 2015 | Blend Lambert → HK-flat indipendente da `subsurface` |
| `thin_walled` | bool | false | — | 2015 | Disattiva la rifrazione interna (foglie, carta) |
| `thin_film_thickness` | float | 0.0 | ≥ 0 (nm) | Thin-film | Belcour-Barla 2017; 100–800 nm = iridescenza |
| `thin_film_ior` | float | 1.5 | ≥ 1 | Thin-film | η₂ del film (acqua = 1.33, sapone = 1.40) |
| `texture` | blocco | — | — | Texturing | Procedurale o immagine, sostituisce `color` |
| `normal_map` | blocco | — | — | Texturing | Perturbazione della superficie (solo image) |
| `bump_map` | blocco | — | — | Texturing | Bump scalare da una qualunque texture procedurale/image |

> Ogni parametro scalare accetta la variante `*_texture` (ad esempio
> `roughness_texture`) e i tre input colore (`color`,
> `transmission_color`, `subsurface_color`) accettano un blocco
> `*_texture` dedicato.

##### **Clearcoat: legacy vs stile Arnold**

Il lobo coat è disponibile in due parametrizzazioni compatibili:

- **Disney 2012 (legacy).** Un unico slider `clearcoat_gloss` (1 = a
  specchio, 0 = ruvido) con IOR implicito 1.5. Mantenuto funzionante per
  tutte le scene scritte prima delle estensioni Arnold.
- **Arnold Standard Surface (preferito).** `coat_ior` + `coat_roughness`
  tunable (0 = a specchio, 1 = ruvido). Corrisponde alla convenzione dei
  principali DCC e dà controllo esplicito sull'highlight.

**Regola di selezione.** `coat_roughness` ha default `-1` (sentinella).
Finché rimane negativo il motore usa il path legacy basato su
`clearcoat_gloss`. Appena imposti `coat_roughness >= 0` (o colleghi
`coat_roughness_texture`) il path Arnold prende il sopravvento e
`clearcoat_gloss` viene ignorato — la conversione spannometrica è
`coat_roughness ≈ 1 - clearcoat_gloss`.

> **Le nuove scene dovrebbero usare `coat_roughness` + `coat_ior`.** Le
> scene esistenti continuano a funzionare invariate; nulla viene rimosso.

##### **`subsurface_radius`: parsata ma non usata**

`subsurface_radius` è riservata per una futura pipeline di SSS
random-walk. Il lobo subsurface approssimato attuale (`subsurface` +
`subsurface_color` + opzionale `flatness`) non la legge. Il loader emette
un messaggio `Info` al caricamento quando la chiave è presente — omettila
nelle nuove scene.

- **Quando usarlo:**
  - Metalli: `metallic=1.0`, rugosità variabile. Aggiungi `anisotropic` per acciaio spazzolato.
  - Plastiche: `metallic=0.0`, `roughness=0.4–0.8`
  - Vernice auto: `metallic=0.0`, `clearcoat=1.0` (+ `coat_roughness` per il coat stile Arnold)
  - Tessuti / velluto: `metallic=0.0`, `sheen=0.8–1.0`, `sheen_roughness=0.2–0.4`
  - Pelle: `metallic=0.0`, `subsurface=0.4`, `subsurface_color=[1.0, 0.6, 0.55]`, `flatness=0.3`
  - Vetro chiaro: `spec_trans=1.0`, `roughness=0.0`, `ior=1.52`
  - Vetro colorato: aggiungi `transmission_color` + `transmission_depth` (es. 5 unità per una bottiglia di brandy)
  - Bolle / opal: `thin_film_thickness=350..700`, `thin_film_ior=1.33..1.5`
  - Foglie / carta: `diff_trans=0.5`, `thin_walled=true`
- **⚠️ Rumore (Noise):** Disney ha più lobi dei classici; per pelle/vetro/clearcoat in primo piano conta di usare circa 4× i campioni.
- **💡 Best practice:** Usa lambertian per le grandi superfici, Disney solo per gli oggetti protagonisti.

#### **5.6 Mix Material (Fonde Due Materiali)**
```yaml
- id: "rusty_metal"
  type: "mix"
  material_a: "chrome"
  material_b: "rust"
  blend: 0.4                               # 40% ruggine, 60% cromo (costante)
  # OPPURE usa una maschera per il blending spaziale:
  mask:
    type: "noise"                          # Texture procedurale
    scale: 3.0
    noise_strength: 5.0
  normal_map: (opzionale)
```
- Fonde in modo fluido due materiali qualsiasi
- La maschera può essere: `noise`, `marble`, `wood`, `checker`, `image`
- Utile per: usura, invecchiamento, transizioni, decalcomanie
- Supporta il nesting Mix-of-mix

---

### 6. **TEXTURES** — Integrate nei Materiali
Le texture sono definite **all'interno** delle definizioni dei materiali.
Tutte le texture procedurali sono di livello professionale e replicano i
controlli esposti da Arnold (`noise`, `cell_noise`), Cycles (nodi Noise /
Voronoi / Brick / Gradient) e RenderMan (`PxrFractal`, `PxrVoronoise`,
`PxrMarble`, `PxrTile`).

#### **Texture Procedurali:**

**Checker:**
```yaml
texture:
  type: "checker"
  scale: 4.0
  colors: [[0.9, 0.9, 0.9], [0.1, 0.1, 0.1]]
```

**Noise:**
```yaml
texture:
  type: "noise"
  noise_type: "fbm"            # perlin | fbm | turbulence | ridged | billow
  scale: 5.0
  octaves: 5                   # 1..16 — ottave per fBm/ridged/billow
  lacunarity: 2.0              # moltiplicatore di frequenza fra ottave
  gain: 0.5                    # decadimento di ampiezza fra ottave
  distortion: 0.0              # domain warp (deforma il dominio per organicità)
  noise_strength: 0.0          # legacy: 0=Perlin liscio, >0=turbolento (sovrascritto da noise_type)
  colors: [[0, 0, 0], [1, 1, 1]]
```
Le cinque famiglie corrispondono alle modalità standard dei renderer professionali:
- `perlin` — gradient noise liscio a singola ottava.
- `fbm` — Σ noise/2^i, il "fractal noise" canonico di Arnold/Cycles/RenderMan.
- `turbulence` — Σ|noise|/2^i con valore assoluto per nitidezza.
- `ridged` — ridged multifractal di Musgrave, ridge nette (roccia, fulmini).
- `billow` — Σ|noise| sulle ottave, gonfio/cumuliforme.

`distortion` deforma la posizione di input con un campione Perlin secondario
(tecnica di Inigo Quilez); 0.3–0.8 è di solito sufficiente.

**Marble:**
```yaml
texture:
  type: "marble"
  scale: 4.0
  noise_strength: 10.0
  vein_axis: [1, 0, 0.3]       # direzione di propagazione delle venature (default Z)
  vein_frequency: 1.0          # moltiplicatore sul termine sinusoidale
  vein_sharpness: 4.0          # 1=morbido (legacy), 4-8=venature sottili Carrara
  noise_type: "turbulence"     # turbulence | fbm | ridged
  octaves: 7                   # ottave del termine frattale
  lacunarity: 2.0
  gain: 0.5
  distortion: 0.0
  colors: [[0.95, 0.95, 0.95], [0.10, 0.10, 0.15]]
```
L'asse delle venature controlla la direzione di propagazione; un vettore non
allineato agli assi produce lastre naturali. `vein_sharpness` eleva la
sinusoide a potenza, restringendo la banda scura in una vera vena.

**Wood:**
```yaml
texture:
  type: "wood"
  scale: 4.0
  noise_strength: 2.0
  ring_axis: [0, 1, 0]         # asse del tronco; anelli ⊥ asse (default Y)
  ring_sharpness: 3.0          # 1=morbido (legacy), 3-6=legno tardivo definito
  axial_grain: 0.3             # noise a lunga lunghezza d'onda lungo l'asse
  octaves: 4                   # ottave fBm della venatura (1 = Perlin legacy)
  lacunarity: 2.0
  gain: 0.5
  distortion: 0.0              # 0=anelli puliti, ~0.5=nodi/onde
  colors: [[0.85, 0.65, 0.40], [0.60, 0.40, 0.20]]
```

**Voronoi / Worley (cellulare):**
```yaml
texture:
  type: "voronoi"
  scale: 5.0
  metric: "euclidean"          # euclidean | euclidean_squared | manhattan | chebyshev
  output: "f1"                 # f1 | f2 | f2_minus_f1 | f1_plus_f2 | cell
  randomness: 1.0              # 0 = griglia, 1 = sparpagliamento casuale
  distortion: 0.0              # warp Perlin prima del lookup
  colors: [[0, 0, 0], [1, 1, 1]]   # ignorato per output: "cell"
```
Replica il nodo Voronoi di Cycles: `f1` produce ciottoli/blob,
`f2_minus_f1` crea "crackle" netti (terra screpolata, pelle di rettile),
`cell` assegna a ciascuna cella un colore piatto. La metrica Chebyshev
produce pattern a tessere quadrate/esagonali.

> **Nota su `f2_minus_f1`.** Matematicamente, `F2-F1` è **zero sul bordo
> della cella** (bisettrice fra due punti-feature) e cresce fino al massimo
> al centro della cella. Il lerp usa `t = sqrt(F2-F1 / norm)` — la
> compressione sqrt riproduce la risposta "Distance to Edge" di Cycles —
> quindi `t = 0` → `colors[0]` è il **colore del bordo** e `t = 1` →
> `colors[1]` è il **colore dell'interno cella**. Per il classico look
> crackle (linee chiare sottili su sfondo scuro) metti il colore **chiaro**
> al PRIMO posto e quello **scuro** al SECONDO.

**Brick:**
```yaml
texture:
  type: "brick"
  brick_width: 0.4
  brick_height: 0.18
  mortar_size: 0.025
  row_offset: 0.5              # 0=stack-bond, 0.5=running-bond
  color_variation: 0.6         # 0=mattoni uniformi, 1=contrasto totale A/B
  noise_scale: 0.15            # noise di "stagionatura" per ogni mattone (0=off)
  colors:
    - [0.72, 0.32, 0.22]       # colore mattone A
    - [0.52, 0.18, 0.12]       # colore mattone B
    - [0.86, 0.83, 0.78]       # malta
```
Di default genera un muro a corsi sfalsati sul piano XY; usa `rotation`
per riproiettare il pattern su pareti orientate diversamente.

**Gradient:**
```yaml
texture:
  type: "gradient"
  mode: "linear"               # linear | quadratic | easing | spherical | radial
  axis: [1, 0, 0]              # direzione del gradiente (linear/quadratic/easing/radial)
  length: 1.0                  # span in world-space del gradiente
  colors: [[0, 0, 0], [1, 1, 1]]
```
- `linear` — `t = (p · axis) / length`.
- `quadratic` / `easing` — stesso `t` poi elevato al quadrato o smoothstepped.
- `spherical` — distanza dall'origine / `length`.
- `radial` — distanza dalla retta `axis` / `length` (decadimento cilindrico).

**Tutte le procedurali supportano:**
```yaml
offset: [5.0, 0.0, 3.0]                  # Traslazione
rotation: [0.0, 45.0, 0.0]               # Rotazione (gradi)
randomize_offset: true                    # Variazione per ogni oggetto
randomize_rotation: true
```

**Color ramp multi-stop (`color_ramp:`)** — override opzionale del lerp a
due colori implicito su `noise`, `marble`, `wood`, `voronoi` e
`gradient`. Equivalente al nodo ColorRamp di Cycles, `ramp_rgb` di Arnold
e `PxrRamp` di RenderMan:
```yaml
texture:
  type: "marble"
  vein_sharpness: 4.0
  color_ramp:
    - { position: 0.00, color: [0.05, 0.05, 0.07], interp: "smoothstep" }
    - { position: 0.45, color: [0.55, 0.45, 0.32], interp: "linear"     }
    - { position: 0.55, color: [0.95, 0.93, 0.88], interp: "linear"     }
    - { position: 1.00, color: [0.05, 0.05, 0.07], interp: "linear"     }
```
- `position` ∈ [0, 1] — viene clampato fuori range; gli stop sono
  riordinati automaticamente per `position` crescente.
- `color: [r, g, b]` — RGB linear-space.
- `interp` (per-stop, descrive il segmento *in uscita* dallo stop verso
  quello successivo):
  - `linear` — lerp standard (default).
  - `smoothstep` — Hermite cubico `3t² − 2t³` (continuità C¹, il "Ease"
    di Cycles).
  - `ease` — smootherstep di Perlin `6t⁵ − 15t⁴ + 10t³` (C², zero
    derivata prima e seconda agli estremi — spalle fotorealistiche).
  - `constant` — mantiene il colore dello stop fino al successivo
    (funzione a gradini).
- Sotto il primo `position` vince il primo colore; sopra l'ultimo
  `position` vince l'ultimo colore.
- Stop coincidenti (stesso `position`) producono una transizione netta —
  trucco da artista per bordi duri.
- Lo shorthand a due colori `colors:` continua a funzionare come ramp a
  2 stop lineare; specificare `color_ramp:` sovrascrive (in tal caso
  `colors:` viene ignorato). Le scene esistenti che non usano
  `color_ramp:` rendono byte-identiche al pre-cambio.

Sblocca: marmo Statuario / Calacatta (vena → mid → base → sotto-tinta),
legno sapwood / heartwood / nodo, gradienti tramonto fotorealistici,
bande toon-shading, false-color heat-map, palette voronoi-driven.

#### **Image Texture:**
```yaml
texture:
  type: "image"
  path: "textures/brick.png"              # Relativo al file YAML
  uv_scale: [2, 1]                        # Fattore di piastrellatura (tiling)
```
- Supporta: PNG, JPEG, BMP, GIF, TIFF, WebP
- Convertito automaticamente da sRGB a lineare
- Filtro bilineare per la morbidezza
- Anti-aliasing analitico (mipmap + EWA anisotropico) quando sono
  disponibili i ray differentials — attivo di default; toggle da CLI
  con `--texture-filtering <auto|on|off>` (vedi
  [profili-di-rendering.md §6c](./profili-di-rendering.md)). Lo stesso
  flag controlla anche l'octave clamp analitico delle texture
  procedurali noise/fBm/marble/wood/voronoi.

#### **Normal Map:**
```yaml
normal_map:
  path: "textures/brick-normal.png"
  strength: 1.0                            # Intensità perturbazione
  uv_scale: [2, 1]
  flip_y: false                            # Imposta true per mappe stile DirectX
```
- Aggiunge dettagli superficiali per pixel senza geometria
- Neutrale: RGB(128, 128, 255) = nessuna perturbazione
- Si applica a qualsiasi tipo di materiale

#### **Bump Map:**
```yaml
bump_map:
  texture:                                 # QUALSIASI ITexture: procedurale o image
    type: "noise"                          # noise/marble/wood/voronoi/brick/gradient/image/...
    noise_type: "fbm"
    scale: 6
    octaves: 4
    colors: [[0, 0, 0], [1, 1, 1]]
  strength: 3.0                            # Ampiezza della perturbazione (0–10, clamp)
  scale: 1.0                               # Moltiplicatore UV uniforme (default 1)
```

Come `normal_map`, ma pilotata da un **campo scalare di altezza** campionato
da una qualunque texture procedurale o image (luminanza Rec.709). La normale
di shading è perturbata con differenze centrate in tangent space
(Blinn 1978). Parità con `bump2d` di Arnold, `PxrBump` di RenderMan e il
nodo "Bump" di Cycles.

| Campo      | Tipo                | Default | Descrizione                                                                  |
|------------|---------------------|---------|------------------------------------------------------------------------------|
| `texture`  | TextureData         | —       | Campo di altezza. Qualunque procedurale (`noise`, `marble`, `wood`, `voronoi`, `brick`, `gradient`, `checker`) o `image`. |
| `strength` | float ∈ [0, 10]     | `1.0`   | Ampiezza della perturbazione. Oltre ~5 il bump appare roccioso; ~0.5–1.0 dà dettagli fini. |
| `scale`    | float > 0           | `1.0`   | Moltiplicatore UV uniforme che si somma all'eventuale `uv_scale` / `scale` della texture interna. |

**Ordine di composizione** quando sono presenti sia `normal_map` che
`bump_map` (convenzione Arnold/Cycles):

1. `normal_map` agisce per prima, sostituendo la normale geometrica.
2. `bump_map` agisce dopo, perturbando la normale **già perturbata**
   (TBN ri-ortogonalizzata contro di essa).
3. Il `coat_normal_map` Disney è **indipendente** — il coat mantiene un
   proprio frame di superficie e non vede il bump.

Si applica a tutti i tipi di materiale (lambertian, metal, dielectric,
disney, emissive, mix). Funziona su tutte le primitive che popolano la
TBN — il motore la popola su Sphere, Box, Cylinder, Cone, Quad, Disk,
Annulus, Torus, Capsule, Lathe, Triangle, SmoothTriangle e InfinitePlane
(cioè su tutte).

Il vantaggio chiave su `normal_map` è l'**input procedurale**:
risoluzione infinita, nessun asset da spedire e riuso completo della
libreria di texture esistente (noise/marble/wood/voronoi/brick/gradient).

#### **Seed Per-Entity:**
```yaml
entities:
  - name: "marble_sphere"
    type: "sphere"
    center: [0, 1, 0]
    radius: 1.0
    material: "marble_textured"
    seed: 1234                             # Randomizzazione deterministica delle texture
```

---

### 7. **SEZIONE ENTITIES** — Oggetti 3D

**Campi comuni per ogni entità** (validi per tutti i tipi sotto: primitive, csg, mesh, group, instance):

| Campo | Default | Note |
|-------|---------|------|
| `name` | — | Etichetta opzionale per log / debug |
| `material` | ereditato | ID materiale, risolto dal blocco `materials` |
| `seed` | auto | Intero stabile che pilota la variazione delle texture procedurali; auto-derivato da name+type+index quando omesso |
| `visible_to_camera` | `true` | Nasconde l'entità solo dai raggi primari della camera. Replica il flag `camera` di Arnold e "Ray Visibility → Camera" di Cycles: l'entità rimane visibile in riflessioni/rifrazioni speculari, continua a ricevere e proiettare illuminazione indiretta, e (se emissiva) contribuisce ancora alla luce diretta via NEE. Utile per nascondere pannelli luminosi off-frame che fanno da fill, o pratici visibili solo nelle riflessioni. Impostato su un `group` propaga a tutti i figli. |
| `scale`, `rotate`, `translate` | identità | Trasformazione locale opzionale (ordine scale → rotate → translate) |

#### **7.1 Sphere**
```yaml
- name: "ball"
  type: "sphere"
  center: [0, 1, 0]
  radius: 1.0
  material: "glass"
```
#### **7.2 Box**
```yaml
- name: "crate"
  type: "box"
  scale: [2.0, 0.5, 2.0]                 # Larghezza, altezza, profondità
  translate: [0.0, 0.25, 0.0]             # Posizione del centro
  rotate: [0, 45, 0]                      # Rotazione (gradi, XYZ)
  material: "wood"
```
- **⚠️ Il Box è centrato:** Translate sposta il centro. Per poggiarlo a terra: `translate: [x, altezza/2, z]`

#### **7.3 Cylinder**
```yaml
- name: "column"
  type: "cylinder"
  center: [0, 0, 0]                       # Centro della BASE
  radius: 0.4
  height: 3.0                              # Si estende verso l'alto (+Y)
  material: "marble"
```

#### **7.4 Cone (o Frustum)**
```yaml
# Cono a punta
- name: "traffic_cone"
  type: "cone"
  center: [0, 0, 0]
  radius: 1.0                              # Raggio base
  height: 2.0
  material: "orange_plastic"
# Cono tronco (frustum)
- name: "bucket"
  type: "cone"
  center: [0, 0, 0]
  radius: 1.5                              # Base
  top_radius: 1.0                          # Cima
  height: 2.0
  material: "metal"
```

#### **7.5 Torus (Ciambella/Anello)**
```yaml
- name: "ring"
  type: "torus"
  center: [0, 1, 0]
  major_radius: 2.0                       # Distanza dal centro al centro del tubo
  minor_radius: 0.5                        # Raggio del tubo
  material: "gold"
```

#### **7.6 Capsule (Pillola)**
```yaml
- name: "battery"
  type: "capsule"
  center: [0, 0, 0]
  radius: 0.5
  height: 2.0                              # Altezza della parte cilindrica
  material: "plastic"
  # Altezza totale = height + 2×radius = 3.0
```

#### **7.7 Annulus (Disco con buco)**
```yaml
- name: "washer"
  type: "annulus"
  center: [0, 0, 0]
  outer_radius: 1.0
  inner_radius: 0.5
  material: "steel"
```

#### **7.8 Disk (Cerchio piatto)**
```yaml
- name: "platform"
  type: "disk"
  center: [0, 0, 0]
  normal: [0, 1, 0]
  radius: 2.0
  material: "metal"
```

#### **7.9 Quad (Piano rettangolare)**
```yaml
- name: "wall"
  type: "quad"
  q: [-5, 0, 5]                           # Angolo di origine
  u: [10, 0, 0]                            # Primo vettore bordo
  v: [0, 5, 0]                             # Secondo vettore bordo
  material: "brick"
```

#### **7.10 Plane (Infinito)**
```yaml
- name: "floor"
  type: "infinite_plane"
  point: [0, 0, 0]                        # Punto sul piano
  normal: [0, 1, 0]
  material: "wood"
```

#### **7.11 Triangle / SmoothTriangle**
```yaml
- name: "poly"
  type: "triangle"
  v0: [0, 0, 0]
  v1: [1, 0, 0]
  v2: [0.5, 1, 0]
  material: "red"
- name: "smooth_poly"
  type: "smooth_triangle"
  v0: [0, 0, 0]
  v1: [1, 0, 0]
  v2: [0.5, 1, 0]
  n0: [0, 0, 1]
  n1: [0.1, 0, 0.9]
  n2: [0.1, 0, 0.9]
  material: "plastic"
```

#### **7.12 Mesh (File OBJ)**
```yaml
- name: "model"
  type: "mesh"
  path: "models/teapot.obj"               # Relativo allo YAML
  scale: [2.0, 2.0, 2.0]
  translate: [0, 1, 0]
  material: "ceramic"
```
- Supporta il formato Wavefront OBJ
- Costruisce automaticamente un BVH interno per intersezioni veloci

##### **Superfici di suddivisione (Loop / Catmull-Clark)**

Il loader può raffinare la mesh OBJ prima della costruzione del BVH
usando gli stessi due algoritmi production-grade disponibili in Arnold,
RenderMan, Cycles e nell'OpenSubdiv di Pixar:

```yaml
- name: "cubo_smussato"
  type: "mesh"
  path: "models/cube.obj"
  material: "ceramica"
  subdivision_scheme: "catmull_clark"     # loop | catmull_clark | auto | none
  subdivision_iterations: 3               # passi di raffinamento uniforme
```

| Campo                       | Tipo   | Default | Note |
|-----------------------------|--------|---------|------|
| `subdivision_scheme`        | string | `none`  | `loop` (mesh triangolari), `catmull_clark` (mesh quad — accetta anche tri e n-gon alla prima iterazione), `auto` (sceglie CC per input quad puro, Loop per triangoli puri, CC negli altri casi), `none`. |
| `subdivision_iterations`    | int    | `0`     | Numero di iterazioni uniformi. Ogni passo moltiplica il numero di facce per ≈ 4. |
| `subdivision_pixel_error`   | float  | `0`     | Target screen-space adattivo. Il loader sceglie il numero di iterazioni che porta l'edge proiettato più lungo sotto questa soglia in pixel (usa la camera risolta della scena). Si combina con `subdivision_iterations` via `max(statico, adattivo)`. |
| `subdivision_max_iterations`| int    | `6`     | Tetto rigido anche per la stima adattiva (limita l'esplosione 4^N delle facce). |

- **Loop** (Charles Loop, 1987) — maschere di bordo come in Hoppe et al.
  1994. Solo triangoli; gli n-gon in input vengono pre-triangolati a ventaglio.
- **Catmull-Clark** (Catmull & Clark, 1978) — maschere di bordo
  Hoppe / DeRose. L'input misto è gestito alla prima iterazione, dopo la
  quale la mesh è tutta a quadrati.
- Le normali per-vertice sono **ricalcolate dalla topologia limite** con
  la media pesata sugli angoli (Max 1999 — default di Blender e Maya).
  Le normali del file OBJ vengono propagate ma sostituite alla
  triangolazione finale perché la superficie limite è più liscia
  dell'input.
- I canali UV passano attraverso la subdivision con maschere lineari
  sul midpoint dell'edge (interpolazione vertex-varying come in OpenSubdiv).
  Le cuciture UV che condividono la posizione ma non l'UV sono preservate.

##### **Displacement scalare (deformazione di silhouette vera)**

Il loader mesh può applicare un displacement scalare (height-field) alla
mesh (sub)divisa **prima della costruzione del BVH**. A differenza di
`bump_map`, che perturba solo la normale di shading, il displacement
sposta fisicamente i vertici lungo la normale liscia della superficie
limite:

```yaml
- name: "pannello_pietra"
  type: "mesh"
  path: "models/plane.obj"
  material: "pietra"
  subdivision_scheme: "catmull_clark"
  subdivision_iterations: 6
  displacement:
    texture:                                # qualunque ITexture (procedurale o image)
      type: "noise"
      noise_type: "fbm"
      scale: 3.5
      octaves: 5
      colors: [[0, 0, 0], [1, 1, 1]]
    scale: 0.30                             # ampiezza in unità di mondo
    midlevel: 0.5                           # luminanza trattata come "piatto" (0.5 per heightmap 8-bit)
    uv_scale: 1.0                           # moltiplicatore UV uniforme (default 1.0)
  displacement_bound: 0.30                  # max displacement atteso (padding AABB foglia BVH)
```

L'update del vertice è `v' = v + scale · (h − midlevel) · n_smooth`, dove
`h = luminance(texture.Value(u, v, p))` e `n_smooth` è la media pesata
sugli angoli delle normali di faccia incidenti sulla topologia limite
(Max 1999 — default di Blender/Maya/OpenSubdiv). Dopo il displacement le
normali di shading per-vertice vengono ricalcolate dalla topologia
spostata, così il BSDF vede il campo di normali reale della nuova
silhouette, non quello pre-displacement.

| Campo                  | Tipo        | Default | Note |
|------------------------|-------------|---------|------|
| `displacement.texture` | TextureData | —       | Height field interno. Qualunque procedurale (`noise`, `marble`, `wood`, `voronoi`, `brick`, `gradient`, `checker`) o `image`. Campionato come luminanza Rec.709, stessa convenzione di `bump_map`. |
| `displacement.scale`   | float       | `0.1`   | Ampiezza con segno in unità di mondo. Negativo spinge verso l'interno. `0` disabilita. |
| `displacement.midlevel`| float       | `0`     | Luminanza trattata come "nessun displacement". `0.5` per heightmap 8-bit dove 128 significa piatto (matches `dispMidpoint` di RenderMan). |
| `displacement.uv_scale`| float > 0   | `1.0`   | Moltiplicatore UV uniforme che si stacca sopra al `uv_scale` interno della texture. |
| `displacement_bound`   | float ≥ 0   | `\|scale\|` | Massima ampiezza attesa del displacement. Gonfia ogni AABB foglia del BVH di questo valore (`disp_padding` di Arnold, `dispBound` di RenderMan). Auto-derivato da `scale` se omesso. Il loader emette un warning quando il displacement effettivamente applicato supera il bound, così l'utente sa di doverlo alzare. |

**Ordine della pipeline.** Il flusso è `subdivide → displace → triangulate
→ BVH`. Il displacement su una mesh low-poly non subdivisa sposta i
vertici originali ed è raramente utile; combinalo con
`subdivision_iterations ≥ 4` (o con un `subdivision_pixel_error`
adattivo) per esporre abbastanza micro-vertici da produrre una
deformazione fluida.

**Solo mesh, per design.** Il displacement scalare è ristretto alle
entity `type: mesh`. Le primitive built-in (`sphere`, `cylinder`,
`torus`, …) usano `bump_map` per il dettaglio sub-pixel — stessa scelta
architetturale di Arnold (`displacement` solo su `polymesh`) e Cycles
(True Displacement solo su nodi subdiv-capable).

**Composizione bump + displacement.** Quando un materiale dichiara un
`bump_map` e l'entity dichiara un `displacement`, il displacement
gestisce la macro-silhouette (posizioni dei vertici, BVH) e il bump
aggiunge il dettaglio sub-pixel sulla normale di shading già modificata.
Replica il workflow "autobump" di Arnold.

##### **Vector displacement (RGB → offset XYZ)**

Un height field scalare può solo spingere i micro-vertici verso fuori
(o verso l'interno) lungo la normale di shading — un vincolo che
esclude **overhang**, **crinkles** e qualunque feature che si pieghi
su se stessa. Il **vector displacement** rimuove questo vincolo
interpretando il triplet RGB della texture come offset 3D completo:

```yaml
- name: "sculpt_panel"
  type: "mesh"
  path: "models/plane.obj"
  material: "stone"
  subdivision_scheme: "catmull_clark"
  subdivision_iterations: 6
  displacement:
    mode: "vector"                            # default è "scalar"
    space: "tangent"                          # oppure "object"
    texture:
      type: "image"
      path: "textures/sculpt_vector_disp.exr" # qualunque ITexture RGB
    scale: 0.5
    midlevel: 0.5                             # 0.5 per storage 8-bit unsigned; 0 per EXR float signed
  displacement_bound: 0.9
```

L'aggiornamento dei vertici è `v' = v + scale · (rgb − midlevel) · basis`.
Il basis dipende dallo `space`:

| Space        | R → asse | G → asse | B → asse | Note |
|--------------|----------|----------|----------|------|
| `tangent`    | T        | B (bitangente) | N (normale) | Convenzione Mudbox / Maya / ZBrush / Cycles tangent-bake. Richiede UV; se mancano il loader passa silenziosamente a `object`. |
| `object`     | +X       | +Y       | +Z       | RGB sommato direttamente alla posizione locale della mesh. Indipendente dalla parametrizzazione UV — utile per sculpt condivisi tra asset con UV diversi. |

| Campo                 | Default     | Note |
|-----------------------|-------------|------|
| `displacement.mode`   | `"scalar"`  | `"scalar"` legge la luminanza e sposta lungo la normale; `"vector"` legge l'RGB completo come offset 3D. |
| `displacement.space`  | `"tangent"` | `"tangent"` o `"object"`. Solo in vector mode; ignorato in scalar. Tangent richiede UV. |
| `displacement.scale`  | `0.1`       | Ampiezza in unità di mondo. In vector mode moltiplica componente per componente l'intero RGB. |
| `displacement.midlevel` | `0`       | Sottratto da ogni canale. `0.5` per mappe 8-bit signed-stored dove 128 significa piatto (default Mudbox/ZBrush); `0` per EXR signed-float. |
| `displacement.uv_scale` | `1.0`     | Moltiplicatore UV uniforme. |
| `displacement_bound`  | `\|scale\|·√3` (vector), `\|scale\|` (scalar) | Padding AABB foglia BVH. Il default vector copre la lunghezza L2 di un offset le cui tre componenti possono ciascuna arrivare a `\|scale\|`. Il loader emette un warning quando il displacement applicato supera il bound, indicando il valore corretto. |

**Convenzione tangent-space.** L'engine deriva le tangenti per-vertex
dal gradiente UV (formula di Lengyel 2001), le accumula pesate per
angolo sui triangoli incidenti, le ortonormalizza contro la normale
smooth via Gram-Schmidt e preserva l'handedness dalla bitangente
accumulata (regola di MikkTSpace). È la stessa convenzione che ogni
consumer di "tangent-space displacement maps" bakate da Mudbox / Maya /
ZBrush si aspetta.

**Ordine pipeline.** Identico allo scalar: `subdivide → displace →
triangulate → BVH`. L'engine dispatcha sul `mode` e applica l'offset
corrispondente; subdivision e costruzione BVH non sanno quale modalità
è girata.

**Bump + vector displacement.** La regola di composizione è la stessa
dello scalar: il vector displacement gestisce la macro-silhouette
(incluse parti con overhang) e un `bump_map` a livello materiale aggiunge
il dettaglio sub-pixel sopra alla normale già modificata. Le normali
ricalcolate dopo il vector pass riflettono già la nuova silhouette,
incluse le parti che si piegano su se stesse, così la perturbazione bump
eredita automaticamente l'orientamento corretto.

La scena showcase `scenes/showcases/vector-displacement-showcase.yaml`
affianca tre pannelli (riferimento scalar, vector tangent-space, vector
object-space) per vedere le differenze di silhouette a colpo d'occhio,
più un cubo CC×4 con ridged-fBm vector displacement che dimostra il
comportamento overhang-producing.

##### **Autobump (bump derivato dalla texture di displacement)**

Subdivision + displacement costruiscono la macro-silhouette fino al
passo di campionamento della texture di displacement. I dettagli più
fini della griglia di subdivision (la coda alta-frequenza di un fBm,
il bordo di una cella Voronoi, il graffio inciso su uno sculpt)
vengono ammorbiditi dalla geometria — esattamente l'artefatto che il
flag `autobump_visibility` di Arnold su un `polymesh` è stato pensato
per recuperare. Impostando `autobump: true` sul blocco `displacement`,
l'engine costruisce una bump map residua dalla stessa texture e la
attacca alla mesh; al tempo dello shading il renderer la applica sopra
ad un eventuale `bump_map` a livello materiale.

```yaml
- name: "pietra_scolpita"
  type: "mesh"
  path: "models/stone.obj"
  material: "porcellana"
  subdivision_scheme: "catmull_clark"
  subdivision_iterations: 4               # subdivision moderata — l'autobump completa il resto
  displacement:
    texture:
      type: "noise"
      noise_type: "fbm"
      scale: 4.5
      octaves: 6
    scale: 0.18
    midlevel: 0.5
    autobump: true                        # ← step 5: bump residuo dalla stessa texture
    autobump_strength: 1.5                # ampiezza bump = autobump_strength · |scale|
    autobump_scale: 1.0                   # moltiplicatore frequenza UV (1 = stessa del displacement)
  displacement_bound: 0.20
```

| Campo                            | Default | Note |
|----------------------------------|---------|------|
| `displacement.autobump`          | `false` | Quando `true`, la texture di displacement è riutilizzata come bump map residua (`autobump_visibility` di Arnold). Disattivato per default — le scene pre-step-5 rendono byte-identiche. |
| `displacement.autobump_strength` | `1.0`   | Moltiplicatore di forza del bump; l'ampiezza finale passata al `BumpMapTexture` interno è `autobump_strength · \|displacement.scale\|`. Impostandolo a 0 disattiva silenziosamente l'autobump (il loader emette warning). |
| `displacement.autobump_scale`    | `1.0`   | Moltiplicatore di frequenza UV stackato su `displacement.uv_scale`. `>1` campiona il bump più fine del displacement (workflow tipico "macro displacement + micro autobump"); `=1` fa match con la frequenza del displacement. |

**Ordine di composizione.** L'engine combina i quattro canali di
perturbazione nello stesso ordine di Arnold/Cycles:

```
normale geometrica (post-displacement)
  → material.normal_map
    → material.bump_map
      → mesh.autobump                 (← derivato da displacement.texture)
```

Il `coat_normal_map` del BSDF Disney è **indipendente** — perturba
solo il lobo di clearcoat ed è invariante rispetto allo stack del bump
di base (parità con la standard surface di Arnold e il Principled BSDF
di Cycles). Il vector displacement funziona allo stesso modo:
l'autobump campiona la luminanza della stessa texture di
vector-displacement, recuperando la componente alta-frequenza lungo la
normale di shading già modificata.

**Solo mesh.** L'autobump condivide il vincolo mesh-only del
displacement — è attivo solo su entity `type: mesh`. Le primitive
built-in continuano a usare un `bump_map` autonomo per il dettaglio
sub-pixel.

La scena showcase `scenes/showcases/bump-displacement-combo-showcase.yaml`
affianca quattro pannelli (riferimento piatto, solo displacement,
displacement + autobump, displacement + autobump + bump materiale) a
una `subdivision_iterations: 4` volutamente moderata, così il
dettaglio sub-griglia recuperato dall'autobump è subito visibile. Lo
scoglio vector-displaced sopra al centro dimostra lo stesso recupero
su una mesh non planare.

#### **7.13 CSG (Operazioni Booleane)**
```yaml
# Union (A ∪ B) — fonde due solidi in uno solo (es. corpo + testa di un pupazzo di neve)
- name: "pupazzo"
  type: "csg"
  operation: "union"
  left:
    type: "sphere"
    center: [0, 0, 0]
    radius: 1.0
  right:
    type: "sphere"
    center: [0, 1.4, 0]
    radius: 0.7
  material: "neve"

# Intersection (A ∩ B) — tiene solo il volume condiviso tra i due solidi (forma a lente)
- name: "lente"
  type: "csg"
  operation: "intersection"
  left:
    type: "sphere"
    center: [-0.5, 0, 0]
    radius: 1.0
  right:
    type: "sphere"
    center: [0.5, 0, 0]
    radius: 1.0
  material: "vetro"

# Subtraction (A \ B) — rimuove B da A (perla: sfera con foro passante)
- name: "perla"
  type: "csg"
  operation: "subtraction"
  left:
    type: "sphere"
    center: [0, 0, 0]
    radius: 1.0
  right:
    type: "cylinder"
    center: [0, -1.5, 0]
    radius: 0.3
    height: 3.0
  material: "legno"
```
- Operazioni: `union` (A∪B), `intersection` (A∩B), `subtraction` (A\B); `subtract` e `difference` sono alias accettati di `subtraction`
- Le chiavi dei figli sono `left` e `right` (operandi del nodo booleano)
- Supporta alberi CSG nidificati ricorsivamente (un `left` o `right` può essere a sua volta un nodo `csg`)
- **Tipi ammessi come figli CSG.** Ogni figlio deve essere una primitiva solida con interno/esterno ben definiti. Supportati: `sphere`, `box`, `cylinder`, `cone`, `torus`, `capsule`, `quad`, `disk`, `annulus`, `triangle`, `lathe` (alias `revolution` / `surface_of_revolution`), `extrusion` (alias `prism` / `linear_extrude`), oppure un `csg` annidato. **Non supportati e scartati con un avviso** (il loader emette `CSG entity '…': failed to create one or both children. Skipping.` e il nodo viene rimosso): `group`, `mesh` / `obj`, `instance`, `plane` / `infinite_plane`. Per unire due primitive come operando CSG, usa un `csg: union` esplicito invece di avvolgerle in un `group`.
- **Materiali emissivi dentro i figli CSG.** Sono geometricamente validi, ma i nodi CSG non sono campionabili, quindi **non parteciperanno alla NEE** (Next Event Estimation). Il loader stampa un avviso una-tantum: `Warning: CSG object contains an Emissive leaf. CSG objects are not sampleable, so their emitters will NOT participate in Next Event Estimation. The emissive surface will still glow via indirect bounces (high variance). Consider wrapping the emissive primitive outside the CSG if direct lighting is needed.` Soluzione alternativa: posizionare la primitiva emissiva accanto al CSG a livello di scena, non al suo interno.

#### **7.14 Group (Composizione Gerarchica)**
```yaml
- name: "lamppost"
  type: "group"
  translate: [5, 0, 0]
  rotate: [0, 45, 0]
  material: "iron"                         # Fallback per i figli
  children:
    - type: "cylinder"
      center: [0, 0, 0]
      radius: 0.08
      height: 3.0
    - type: "sphere"
      center: [0, 3.2, 0]
      radius: 0.25
      material: "glass"                    # Override
```
- Le trasformazioni si compongono gerarchicamente

#### **7.15 Template + Instance (Oggetti Riutilizzabili)**
```yaml
templates:
  - name: "chess_pawn"
    material: "wood"
    children:
      - type: "cylinder"
        center: [0, 0, 0]
        radius: 0.4
        height: 0.15
      - type: "sphere"
        center: [0, 0.35, 0]
        radius: 0.3
entities:
  - name: "pawn_e2"
    type: "instance"
    template: "chess_pawn"
    translate: [0, 0, 0]
  - name: "pawn_d2"
    type: "instance"
    template: "chess_pawn"
    translate: [2, 0, 0]
    material: "ebony"                     # Override materiale
    scale: 1.2                             # Override dimensione
```

#### **7.16 Lathe (Superficie di Rivoluzione)**
```yaml
# Profilo Linear — look sfaccettato del tornio reale (spigoli vivi sui vertici)
- name: "colonna"
  type: "lathe"                           # alias: "revolution", "surface_of_revolution"
  profile_type: "linear"                  # default — può essere omesso
  material: "marmo"
  profile:                                # lista di punti [r, y], y monotona
    - [0.30, 0.0]
    - [0.30, 0.1]
    - [0.25, 0.2]
    - [0.28, 2.0]
    - [0.35, 2.1]

# Profilo Catmull-Rom — liscio, passa per ogni punto di controllo (centripeto)
- name: "vaso"
  type: "lathe"
  profile_type: "catmull_rom"             # alias: "catmull", "smooth"
  material: "ceramica"
  profile:
    - [0.00, 0.00]                        # base chiusa (r = 0 → cap assente)
    - [0.30, 0.00]
    - [0.55, 0.40]
    - [0.45, 0.80]
    - [0.55, 0.95]
    - [0.00, 0.95]                        # apertura chiusa

# Profilo Bezier — 4 control point cubici espliciti per ogni segmento
- name: "ciotola"
  type: "lathe"
  profile_type: "bezier"
  material: "porcellana"
  profile:                                # estremi dei segmenti — (N-1) segmenti
    - [0.0, 0.0]
    - [0.5, 0.3]
    - [0.5, 0.6]
  profile_bezier_controls:                # 4 × (N-1) control point, concatenati
    - [0.0, 0.0]
    - [0.3, 0.0]
    - [0.5, 0.1]
    - [0.5, 0.3]
    - [0.5, 0.3]
    - [0.5, 0.45]
    - [0.5, 0.5]
    - [0.5, 0.6]
```
- Fa ruotare un profilo 2D di 360° attorno all'asse Y locale. Il
  posizionamento passa da `center`/`translate`/`rotate` come per ogni
  altra primitiva.
- Tre modalità di interpolazione. `linear` impila frustum analitici —
  veloce ed esatto, ma mostra gli spigoli vivi ai vertici. `catmull_rom`
  usa Catmull-Rom centripeto (Yuksel et al. 2011) — passa per ogni
  punto, C¹ continuo, niente auto-intersezioni. `bezier` lascia
  all'utente i 4 control point cubici per segmento;
  `profile_bezier_controls` deve contenere esattamente `4 × (N − 1)`
  voci.
- I dischi di cap inferiore e superiore vengono aggiunti
  automaticamente quando il profilo lascia l'asse (`r > 0`) a
  quell'estremo.
- La coordinata V sulla superficie laterale è l'arco cumulativo
  normalizzato del profilo; U è l'angolo azimutale come per
  Cylinder/Cone.
- Catmull-Rom richiede almeno 4 punti; profili con 2 o 3 punti vengono
  degradati in modo trasparente a `linear` con un warning del loader.
- I Lathe emissivi partecipano automaticamente al NEE: `Sample()` usa
  la CDF pesata per area su segmenti e cap, così ombre e illuminazione
  diretta ricevono campioni senza rumore.
- L'intersezione raggio-superficie è quadratica analitica per `linear`;
  per le modalità spline l'equazione è un polinomio di grado 6
  risolto con ibrido Sturm chain + Newton-Raphson (`SturmSolver`), lo
  stesso approccio della `lathe` di PovRay e della `Curve` di PBRT.
  Aspettati ~10× il costo per-raggio di un hit su Cone sui segmenti
  spline — preferisci `linear` quando lo sfaccettato è accettabile.

#### **7.17 Extrusion (Estrusione lineare di un profilo 2D)**
```yaml
# Profilo lineare concavo — una stella a 5 punte estrusa in un prisma
- name: "pilastro_stella"
  type: "extrusion"                       # alias: "prism", "linear_extrude"
  profile_type: "linear"                  # default — può essere omesso
  height: 1.5
  caps: "both"                            # both | start | end | none (default: both)
  material: "oro"
  profile:                                # loop chiuso di [x, z] (CCW preferito)
    - [ 1.000,  0.000]
    - [ 0.234,  0.339]
    - [ 0.309,  0.951]
    - [-0.089,  0.405]
    - [-0.809,  0.588]
    - [-0.378,  0.000]
    - [-0.809, -0.588]
    - [-0.089, -0.405]
    - [ 0.309, -0.951]
    - [ 0.234, -0.339]

# Profilo Catmull-Rom + twist + taper (colonna architettonica)
- name: "colonna_attorcigliata"
  type: "extrusion"
  profile_type: "catmull_rom"             # alias: "catmull", "smooth"
  height: 4.0
  twist_degrees: 90                       # rotazione del profilo superiore attorno a Y
  taper: 0.85                             # scala XZ uniforme dell'estremità superiore
  curve_samples: 24                       # campioni polilinea per segmento di profilo
  caps: "both"
  material: "marmo"
  profile:                                # sezione 8-lobi
    - [ 1.00,  0.00]
    - [ 0.40,  0.40]
    - [ 0.00,  1.00]
    - [-0.40,  0.40]
    - [-1.00,  0.00]
    - [-0.40, -0.40]
    - [ 0.00, -1.00]
    - [ 0.40, -0.40]

# Profilo Bezier — 4 control points cubici per segmento, in loop chiuso
- name: "medaglione_arrotondato"
  type: "extrusion"
  profile_type: "bezier"
  height: 0.3
  material: "ottone"
  profile:                                # endpoint dei segmenti — N segmenti chiusi
    - [ 1.0,  0.0]
    - [ 0.0,  1.0]
    - [-1.0,  0.0]
    - [ 0.0, -1.0]
  profile_bezier_controls:                # 4 × N control points concatenati
    - [ 1.0,  0.0]
    - [ 1.0,  0.55]
    - [ 0.55, 1.0]
    - [ 0.0,  1.0]
    - [ 0.0,  1.0]
    - [-0.55, 1.0]
    - [-1.0,  0.55]
    - [-1.0,  0.0]
    - [-1.0,  0.0]
    - [-1.0, -0.55]
    - [-0.55,-1.0]
    - [ 0.0, -1.0]
    - [ 0.0, -1.0]
    - [ 0.55,-1.0]
    - [ 1.0, -0.55]
    - [ 1.0,  0.0]

# Linear + crease_angle — poligono a 12 lati letto come cilindro, non come prisma sfaccettato
- name: "colonna_tonda"
  type: "extrusion"
  profile_type: "linear"
  height: 2.0
  crease_angle: 40            # blend normali sugli edge il cui diedro è inferiore a 40°
  caps: "both"
  material: "intonaco"
  profile:
    - [ 1.000,  0.000]
    - [ 0.866,  0.500]
    - [ 0.500,  0.866]
    - [ 0.000,  1.000]
    - [-0.500,  0.866]
    - [-0.866,  0.500]
    - [-1.000,  0.000]
    - [-0.866, -0.500]
    - [-0.500, -0.866]
    - [ 0.000, -1.000]
    - [ 0.500, -0.866]
    - [ 0.866, -0.500]
```
- Estrude un profilo 2D chiuso nel piano XZ lungo l'asse Y locale,
  producendo un prisma da `y = 0` a `y = height`. Il posizionamento
  passa per `center` / `translate` / `rotate` come ogni altra primitiva.
- Tre modalità di interpolazione speculari al lathe: `linear` mantiene
  la polilinea per ridge taglienti; `catmull_rom` (centripetale) dà una
  silhouette liscia che passa per ogni punto; `bezier` consente di
  controllare ogni cubica. `profile_bezier_controls` deve contenere
  esattamente `4 × N` punti — una cubica per segmento del profilo, con
  l'ultimo segmento che chiude il loop sul primo vertice.
- **I profili concavi funzionano**: i cap vengono triangolati con
  ear-clipping, quindi stelle, ingranaggi, lettere, sezioni a L / T / U
  / H e profili architettonici si renderizzano correttamente senza
  decomposizione manuale.
- L'orientamento del profilo è auto-corretto: input orari (CW) vengono
  invertiti al caricamento perché le normali esterne delle pareti
  puntino sempre fuori.
- `caps: "both"` (default) chiude entrambe le estremità; `"start"` /
  `"end"` ne chiudono solo una (utile per vasche/scodelle); `"none"`
  produce un guscio aperto.
- `twist_degrees` ruota il profilo superiore attorno all'asse Y —
  combinato con `taper` ottieni l'intera gamma di colonne architettoniche
  e raccordi industriali prodotti dal `polyextrude` di Houdini o dal
  modificatore "Extrude with twist" di Blender.
- `curve_samples` controlla la qualità della silhouette per
  `catmull_rom` / `bezier`: ogni segmento di input diventa quel numero
  di campioni di polilinea (default 16, 24-32 per primi piani da hero).
- `crease_angle` (default `0`, solo modalità `linear`): soglia diedra in gradi
  per il blending delle normali ai vertici sulle pareti laterali lineari. Coppie
  di pareti adiacenti le cui normali di faccia differiscono meno di questo
  valore condividono una normale blended (shading liscio, l'edge scompare nei
  riflessi speculari); coppie che differiscono di più mantengono le proprie
  normali piane (spigolo netto). `0` produce geometria completamente sfaccettata
  — comportamento storico. 30° ammorbidisce le curve approssimate con polilinea
  mantenendo nitidi gli angoli retti su lettere, ingranaggi e sezioni
  ingegneristiche. Ignorato per `catmull_rom` e `bezier`, che producono sempre
  pareti smooth-shaded.
- Internamente ogni extrusion costruisce la propria BVH sopra triangoli
  di pareti + cap, quindi la BVH globale vede una sola foglia per
  extrusion indipendentemente dalla complessità del profilo. Le normali
  smooth-shaded vengono emesse sulle pareti per `catmull_rom` / `bezier`;
  `linear` usa di default normali piane — imposta `crease_angle > 0` per
  blend delle normali sugli edge sotto la soglia e ammorbidire le curve
  approssimate con polilinea senza cambiare modalità di profilo.
- Le Extrusion emissive partecipano al NEE automaticamente: `Sample()`
  sceglie un triangolo proporzionalmente alla sua area, quindi la luce
  da un'insegna al neon a forma di stella è pesata correttamente fra
  pareti e cap.

#### **7.18 Ordine delle trasformazioni e anti-pattern `center:`**

Le trasformazioni delle entità seguono un ordine fisso `scale → rotate → translate` attorno all'**origine globale (0, 0, 0)**:

```
pos_mondo = translate( rotate( scale( pos_locale ) ) )
```

Le primitive che espongono la chiave `center:` — **sphere, cylinder, cone, capsule, torus, disk, annulus, lathe** — posizionano la propria geometria *prima* che la matrice di trasformazione esterna venga valutata. Combinare `center:` con `rotate:` o `scale:` fa sì che rotazione e scala vengano applicate attorno all'origine, non attorno al centro della primitiva, producendo posizioni inaspettate.

**Anti-pattern** — non combinare `center:` con `rotate:` o `scale:`:
```yaml
# ❌ SBAGLIATO: center sposta il cilindro a [0, 0.5, 0], poi rotate: [0, 0, 90]
# ruota attorno all'origine globale, spostando il cilindro a [-0.5, 0, 0].
- name: "braccio"
  type: "cylinder"
  center: [0, 0.5, 0]   # ← non usare insieme a rotate/scale
  rotate: [0, 0, 90]
  radius: 0.05
  height: 1.0
  material: "ferro"
```

**Pattern corretto** — omettere `center:` (default `[0, 0, 0]`) e usare `translate:` per il posizionamento finale:
```yaml
# ✅ CORRETTO: la primitiva è all'origine, ruotata attorno all'origine, poi traslata.
- name: "braccio"
  type: "cylinder"
  rotate: [0, 0, 90]       # ① ruota attorno all'origine globale
  translate: [0, 0.5, 0]   # ② poi sposta nella posizione finale
  radius: 0.05
  height: 1.0
  material: "ferro"
```

**Quando `center:` è sicuro:**
- Quando non sono presenti `rotate:` né `scale:` — `center:` è equivalente a `translate:`.
- Dentro i **figli CSG** (`left`/`right`) — i figli CSG non hanno una trasformazione esterna, quindi `center:` li posiziona correttamente.
- Dentro i **group** quando il figlio non ha una propria rotazione — la `translate`/`rotate` del group si compone correttamente sopra.

---

### 8. **SEZIONE LIGHTS** — Cinque Tipi
#### **8.1 Point Light (Omnidirezionale)**
```yaml
- type: "point"
  position: [2, 5, -3]
  color: [1.0, 0.95, 0.85]
  intensity: 20.0                          # Range: 4–30
  soft_radius: 0.0                         # Opzionale. >0 → niente fireflies da 1/d²
```
- Decadimento quadratico con la distanza
- `soft_radius` (default `0`): se impostato, il denominatore dell'attenuazione viene clampato a `max(d², r²)`. Elimina la singolarità 1/d² che genera fireflies persistenti nelle scene con nebbia/medium partecipanti, dove gli eventi di scattering possono cadere arbitrariamente vicini all'emettitore. Valori consigliati: simili al raggio fisico del bulbo (es. `0.05`–`0.20`). A distanze `d ≥ r` la luce è invariata.

#### **8.2 Directional Light (Sole)**
```yaml
- type: "directional"  # alias: "sun"
  direction: [-0.5, -1.0, -0.3]           # Direzione di propagazione (luce → scena).
                                          # Sole posizionato in -direction.
  color: [1.0, 0.98, 0.92]
  intensity: 0.8                           # Range: 0.05–2.0
  angular_radius: 0.0                      # Opzionale. >0 = disco solare (ombre morbide).
                                          #   0.27 = disco solare reale. Default: 0.
```
- Nessuna attenuazione con la distanza
- Allineare con la direzione `sun.direction` del cielo a gradiente per coerenza visiva
- `angular_radius` (default `0`): quando > 0, modella un disco di dimensione angolare finita. Ogni raggio d'ombra viene perturbato uniformemente all'interno del cono subteso, producendo una penombra morbida. Il sole reale sottende circa 0.27°. Quando attivo, `shadow_samples` default diventa 4 e `IsDelta` diventa `false`, abilitando il pesaggio MIS completo.

#### **8.3 Spot Light (Cono)**
```yaml
- type: "spot"  # alias: "spotlight"
  position: [0, 5, 0]
  direction: [0, -1, 0]                   # Dove punta il faretto
  color: [1.0, 0.9, 0.7]
  intensity: 40.0
  inner_angle: 15                         # Gradi (piena luminosità)
  outer_angle: 30                         # Gradi (zona di sfumatura)
  soft_radius: 0.0                        # Opzionale. >0 = "disco virtuale", niente fireflies 1/d²
  shadow_samples: 1                       # Default 1. >1 + soft_radius > 0 → sorgente jitterata
```
- `soft_radius` (default `0`): stesso ruolo della point light — clampa il denominatore a `max(d², r²)`. Fortemente raccomandato per spot che illuminano un medium partecipante (nebbia, foschia, fumo): in questi casi il picco 1/d² agli eventi di scattering vicino all'emettitore è la principale sorgente di fireflies. Valori tipici: `0.10`–`0.30` per un bulbo da lampione.
- `shadow_samples` (default `1`): quando > 1 E `soft_radius > 0`, ogni raggio d'ombra jitterizza la posizione della sorgente su un disco di raggio `soft_radius` perpendicolare a `direction`, modellando l'estensione fisica del bulbo. Se `soft_radius == 0`, campioni aggiuntivi non hanno effetto (nessun jitter di posizione) — tenerlo a 1 per efficienza.

#### **8.4 Area Light (Ombre Morbide)**
```yaml
- type: "area"  # alias: "area_light", "rect", "rect_light"
  corner: [-1.5, 4.99, -1.5]              # Un angolo
  u: [3.0, 0.0, 0.0]                      # Primo bordo
  v: [0.0, 0.0, 3.0]                      # Secondo bordo
  color: [1.0, 0.97, 0.9]
  intensity: 35.0                          # Range: 15–60
  shadow_samples: 4                        # Campioni per punto (default)
  soft_radius: 0.0                         # Opzionale. >0 = floor distSq in cosLight/d²
  visible_to_camera: true                  # Opzionale. false = nasconde il proxy dai raggi primari
```
- Ombre morbide Monte Carlo con penombra
- `shadow_samples` sovrascrivibile via CLI: `-S 32`
- Visibile alla camera e ai raggi specular tramite un quad emissivo proxy posizionato a `corner`/`u`/`v` — chiude lo stimatore MIS di Veach sui materiali specular smooth. Stesso approccio di Arnold/Cycles/Renderman per le quad light analitiche.
- `soft_radius` (default `0`): quando > 0, il denominatore dell'attenuazione viene clampato a `max(distSq, r²)`, impedendo al termine `cosLight/d²` di divergere quando un campione stratificato cade quasi tangente al ricevitore nei media volumetrici densi. La distanza geometrica restituita è invariata. Consigliato per area light che illuminano media partecipanti densi (es. pannello a soffitto in nebbia).
- `visible_to_camera` (default `true`): impostato a `false` nasconde il quad proxy ai raggi primari della camera. La NEE continua a illuminare la scena a piena intensità; le riflessioni speculari e le rifrazioni continuano a vedere il pannello; i rimbalzi indiretti sono invariati. Replica il flag `camera` di Arnold e "Ray Visibility → Camera" di Cycles.

#### **8.5 Sphere Light (Ombre Morbide Isotropiche)**
```yaml
- type: "sphere"  # alias: "sphere_light", "ball", "ball_light"
  position: [0, 5, 0]
  radius: 0.5                              # Più grande = ombre più morbide; definisce anche la dimensione del proxy
  color: [1.0, 0.95, 0.85]
  intensity: 30.0
  shadow_samples: 4
  visible_to_camera: true                  # Opzionale. false = nasconde il proxy dai raggi primari
```
- Campionamento ad angolo solido (efficiente, nessun campione sprecato)
- Penombra circolare isotropica
- Visibile alla camera e ai raggi specular tramite una sfera emissiva proxy gestita internamente, alla stessa posizione/raggio — chiude lo stimatore MIS di Veach sui materiali specular smooth (niente "buco nero" dove la luce dovrebbe riflettersi su vetri/specchi). Stesso approccio di Arnold/Cycles/Renderman per le sphere light analitiche.
- `soft_radius` è deliberatamente **non** consumato: lo stimatore ad angolo solido `L = Intensity × Ω / N` è limitato superiormente da `4π · Intensity` anche quando il ricevitore è dentro la sfera, quindi il floor 1/d² usato da point/spot/area è qui inutile.
- `visible_to_camera` (default `true`): impostato a `false` nasconde la sfera proxy ai raggi primari della camera. La NEE continua a illuminare la scena a piena intensità; la sfera resta visibile nelle riflessioni a specchio e attraverso il vetro. Replica il flag `camera` di Arnold e "Ray Visibility → Camera" di Cycles. Nessun effetto su `point`/`spot`/`directional` (luci delta) che non hanno proxy.

#### **Riferimento Calibrazione Luci:**
| Tipo | Range | Note |
|------|-------|-------|
| Point (generico) | 4–30 | Scala con distanza² |
| Spot (key) | 15–30 | Cono stretto = intensità maggiore |
| Directional (riempimento) | 0.05–0.15 | Luce secondaria |
| Directional (principale) | 0.3–2.0 | Unica luce in scene esterne |
| Area (pannello) | 20–60 | Dipende dalla dimensione del rettangolo |
| Sphere (piccola) | 20–50 | Raggio 0.1–0.3 |
| Sphere (grande) | 15–40 | Raggio 0.5–1.5 |

---

### 9. **IMPORTS** — Librerie Modulari
```yaml
imports:
  - path: "libraries/materials/metals.yaml"
  - path: "libraries/lights/studio-3point.yaml"
  - path: "libraries/objects/chess.yaml"
```
- **Ordine:** Deve essere la prima sezione (prima di templates/world)
- **Percorsi:** Relativi alla directory del file YAML

---

### 10. **ESEMPIO STRUTTURA FILE**
Ecco una scena minimale completa:
```yaml
# Scena Semplice
world:
  sky:
    type: "flat"
    color: [0.3, 0.6, 1.0]
  ground:
    type: "infinite_plane"
    material: "grass"
    y: 0.0
cameras:
  - name: "main"
    position: [3, 2, -6]
    look_at: [0, 1, 0]
    fov: 45
    aperture: 0.05
    focal_dist: 7
lights:
  - type: "directional"
    direction: [-0.5, -1.0, -0.3]
    color: [1.0, 0.95, 0.85]
    intensity: 1.0
  - type: "point"
    position: [5, 8, -3]
    color: [1.0, 1.0, 1.0]
    intensity: 50.0
materials:
  - id: "grass"
    type: "lambertian"
    color: [0.3, 0.6, 0.2]
  - id: "glass"
    type: "dielectric"
    refraction_index: 1.5
  - id: "gold"
    type: "metal"
    color: [0.85, 0.65, 0.2]
    fuzz: 0.1
entities:
  - name: "sphere_glass"
    type: "sphere"
    center: [0, 1.5, 0]
    radius: 1.0
    material: "glass"
  - name: "cube_gold"
    type: "box"
    scale: [1.0, 1.0, 1.0]
    translate: [-2, 0.5, 0]
    material: "gold"
```

---

### 11. **FILE CHIAVE NEL PROGETTO**
**Documentazione:**
- `/docs/tutorial/it/` — Tutorial completo (12 capitoli):
  - `01-what-is-ray-tracing.md` — Introduzione al ray tracing
  - `02-first-scene.md` — Prima scena e struttura del file
  - `03-materials.md` — Tutti i tipi di materiale
  - `04-geometric-primitives.md` — Tutti i tipi di geometria
  - `05-transforms-and-groups.md` — Trasformazioni, gruppi e gerarchie
  - `06-lighting.md` — Tutti i tipi di luce
  - `07-sky-environment-camera.md` — Cielo, ambiente e camera
  - `08-csg.md` — Operazioni booleane CSG
  - `09-volumetrics.md` — Mezzi partecipanti e volumetria
  - `10-libraries-and-projects.md` — Import, librerie e modularità
  - `11-lathe-surface-of-revolution.md` — Lathe / superficie di rivoluzione
  - `12-extrusion-2d-profiles.md` — Estrusione lineare di profili 2D
**Codice Sorgente (Parsing Scene):**
- `/src/RayTracer/Scene/SceneLoader.cs` — Parsing YAML e costruzione scena
- `/src/RayTracer/Materials/` — Implementazioni dei materiali
- `/src/RayTracer/Geometry/` — Implementazioni di tutte le primitive
- `/src/RayTracer/Lights/` — Implementazioni delle sorgenti luminose
**Scene di Esempio:**
- `/scenes/sample.yaml` — Scena di riferimento semplice
- `/scenes/cornell-box.yaml` — Classica Cornell Box con varianti
- `/scenes/pendolo-newton.yaml` — Scena complessa (pendolo di Newton)
- `/scenes/showcases/` — Dimostrazioni per funzionalità specifiche
- `/scenes/libraries/` — Materiali, luci, oggetti e template riutilizzabili

---

### 12. **BEST PRACTICES PER SCENE DI ALTA QUALITÀ**
1. **Strategia Materiali:**
   - Usa `lambertian` per grandi superfici di sfondo (nessun campione extra necessario)
   - Usa `disney` o `metal` solo per gli oggetti protagonisti
   - Usa il materiale `mix` per effetti realistici di usura e invecchiamento
2. **Configurazione Luci:**
   - Inizia con una luce direzionale + gradient sky per scene outdoor
   - Aggiungi alcune luci point o area per riempimento/accento
   - Usa sphere lights per ombre morbide ed isotropiche
   - Sovrascrivi `--shadow-samples` dalla CLI invece di modificare lo YAML
3. **Camera e Composizione:**
   - Usa la lista `cameras: []` per gestire più inquadrature
   - Imposta `focal_dist` sulla distanza effettiva dal soggetto principale
   - Usa aperture=0.0 per i pass di bozza, aggiungi apertura per i render finali
   - Testa con bassa risoluzione + profilo Preview (`-w 400 -H 267 -s 64 -d 4 -S 1`)
4. **Ottimizzazione Performance:**
   - Usa template + istanze per oggetti ripetuti
   - Importa materiali/luci condivisi dalle librerie
   - Raggruppa geometrie simili in gruppi per gerarchie più pulite
   - La BVH viene costruita automaticamente per scene complesse
5. **Fonti Texture:**
   - Polyhaven.com — HDRI e texture PBR gratuite (CC0)
   - AmbientCG.com — Set completi di texture PBR
   - Procedurali (noise, marble, wood) per controllo artistico
6. **Parametri di Rendering:** (vedi [Profili di Rendering](./profili-di-rendering.md) per tabelle complete e consigli)
   - Preview: `-s 64 -d 4 -S 1 -w 400`
   - Standard: `-s 256 -d 6 -w 800`
   - Final: `-s 1024 -d 8 -S 4 -w 1920`
   - Ultra: `-s 1600 -d 8 -S 4 -w 3840`

---

Questa guida copre tutto il necessario per scrivere file di scena YAML di qualità professionale. Tutte le informazioni provengono direttamente dalla documentazione del progetto, dai file di esempio e dalla struttura del codice sorgente.
