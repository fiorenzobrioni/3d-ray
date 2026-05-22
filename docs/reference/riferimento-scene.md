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

#### **Gradient Sky** (stilizzato — preview / outdoor):
```yaml
sky:
  type: "gradient"
  zenith_color:  [0.10, 0.30, 0.80]      # Parte superiore del cielo
  horizon_color: [0.65, 0.80, 1.00]      # Orizzonte
  ground_color:  [0.30, 0.25, 0.20]      # Riflesso del terreno
  sun:                                     # (opzionale)
    direction:      [0.5, 1.0, 0.3]      # Direzione VERSO il sole (posizione del disco).
    color:          [1.0, 0.98, 0.85]
    intensity:      12.0
    angular_radius: 0.265                  # Semiangolo in gradi (consigliato)
    size:           2.5                    # Diametro totale in gradi (alternativa)
    limb_darkening: true                   # Limb darkening Hestroffer (1997)
    shadow_samples: 4                      # Campioni stratificati per il PhysicalSun accoppiato
    visible_to_camera: true                # Nasconde il disco dalla camera, lo tiene come luce
```
**Cambio convenzione `direction`.** Ora punta VERSO il sole. Il vecchio codice
invertiva il segno internamente; scene legacy basate su quel flip vedranno il
sole dal lato opposto — basta invertire il vettore. Il disco è agganciato
automaticamente a un `PhysicalSun` separato con cone sampling e limb darkening.

#### **Hosek-Wilkie / Preetham** (sky fisico clear-sky):
```yaml
sky:
  type: "hosek_wilkie"                     # alias di "preetham" (modello analitico)
  turbidity:     3.0                       # 1 = aria pulitissima, 3 = clear, 5 = foschia, 10 = smog
  ground_albedo: [0.3, 0.3, 0.3]
  intensity:     1.0
  sun:
    direction:       [0.3, 0.8, 0.2]      # direzione VERSO il sole
    angular_radius:  0.265                 # default = disco solare reale
    limb_darkening:  true
    shadow_samples:  4
```
Distribuzione daylight analitica parametrizzata da torbidità atmosferica e
albedo del terreno. Il modello espone direttamente la direzione del sole come
luce analitica: viene auto-registrato un `PhysicalSun` accanto all'environment —
ombre nitide da cone sampling senza dover campionare 1 px su CDF. La trasmittanza
Rayleigh tinge il sole di caldo alle elevazioni basse (alba/tramonto).

#### **HDRI/IBL** (image-based, .hdr + .exr):
```yaml
sky:
  type: "hdri"
  path: "hdri/studio.hdr"                 # .hdr (Radiance) oppure .exr (OpenEXR)
  intensity: 1.0                           # Moltiplicatore esposizione
  rotation: 90                             # Rotazione asse Y in gradi (legacy)
  sun:                                     # (opzionale) sun extraction
    extract_from_hdri: true                # auto-detect del sole e split
    extract_threshold: 50                  # multiplo della media HDRI (def. 50)
    shadow_samples: 4
```
Le HDRI sono importance-sampled tramite CDF 2D pesata per luminanza. **OpenEXR**
supportato (scanline RGB, no-compression / ZIP / ZIPS, half + float). La **sun
extraction** rileva il picco più brillante, sostituisce quei pixel con la media
circolare del background, e emette un `PhysicalSun` accoppiato per ombre nitide
— stesso workflow di `aiSkyDomeLight` in Arnold. I valori negativi (alcune
compressioni EXR) sono clampati a 0 al load.

#### **Flag di visibilità** (per tipo di raggio, parità Cycles / Arnold):
```yaml
sky:
  type: "hdri"
  path: "studio.hdr"
  visibility:
    camera:       true     # Raggi camera vedono il body del cielo
    diffuse:      true     # Bounce diffuse / sheen / SSS vedono il cielo
    glossy:       true     # Bounce glossy / clearcoat vedono il cielo
    transmission: true     # Rifrazioni vedono il cielo
    shadow:       true     # Raggi NEE shadow recuperano la radianza del cielo
  sun:
    visible_to_camera: false    # Disco solare invisibile alla camera (continua a illuminare)
```

#### **Background plate** (solo camera):
```yaml
sky:
  type: "hdri"
  path: "lighting.hdr"        # Sorgente di illuminazione primaria
  background:
    type: "hdri"
    path: "background.hdr"    # Immagine diversa mostrata ai raggi camera
```

#### **Orientation** (rotazione 3D completa):
```yaml
sky:
  type: "hdri"
  path: "studio.hdr"
  orientation:
    euler:      [10, 45, 0]   # Euler XYZ intrinseco in gradi
    # OPPURE
    quaternion: [0, 0.38, 0, 0.92]   # XYZW; il quaternion vince se entrambi presenti
```
Sostituisce il vecchio campo `rotation:` solo-Y. Il campo legacy è ancora
onorato quando `orientation:` è assente.

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
- **Esposizione fotografica:** `--exposure <EV>` (default `0`) applica un guadagno lineare `2^EV` a ogni pixel prima del tone map ACES. Usa EV negativo (`-1`, `-2`) quando la scena appare lavata perché le luci portano la radianza in ingresso sopra ~2.0, dove ACES si appiattisce sul plateau 0.95-0.99 e nasconde il contrasto delle texture. EV positivo schiarisce scene che cadono sotto la zona lineare della curva. Parità con Arnold `exposure`, Cycles "Film → Exposure", RenderMan display-filter `exposure`.
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
> L'estensione `.yaml` su `-i` è **opzionale**: se il path indicato non
> esiste così com'è, il loader prova ad aggiungere `.yaml` e poi `.yml`
> (per esempio `-i scene` risolve a `scene.yaml`).

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

#### **Spazio di campionamento (object-local).**
Tutte le procedurali campionano su `rec.LocalPoint`, che le primitive built-in
espongono nel **frame object-local** della primitiva stessa: Sphere / Cylinder /
Cone / Capsule / Disk / Annulus sottraggono `Center` dal punto di hit world-
space, Quad sottrae `Q`, InfinitePlane sottrae `Point`; Box / Torus / Lathe
sono già all'origine e sempre incapsulati in un `Transform`. È la stessa
convenzione di Arnold con `space: object`, di Cycles con "Texture Coordinate
→ Object" e di RenderMan con `Pref` — la texture tila **per-entità**
indipendentemente da dove la primitiva è piazzata nel mondo.

Conseguenza pratica sui valori consigliati di `scale`:

| Dimensione primitiva (raggio / semi-extent oggetto) | `scale` consigliato per ~3–8 cicli visibili |
|-----------------------------------------------------|----------------------------------------------|
| ~0.3 wu (sfera piccola, gemma)                      | `6 – 12`                                     |
| ~1 wu (riferimento canonico)                        | `3 – 6`                                      |
| ~3 wu (sfera hero grande, slab)                     | `1 – 2`                                      |
| ~10 wu (quad pavimento, parete stanza)              | `0.3 – 0.6`                                  |

I valori di default mostrati sotto presuppongono la **primitiva canonica
raggio 1 wu**. Per ottenere lo stesso numero di feature visibili su una
primitiva più piccola, moltiplica la scale per il rapporto canonical-to-actual.
Se ti serve il comportamento *legacy* world-locked (es. un pattern marble che
continua senza soluzione di continuità tra molti box affiancati), pilota il
materiale da un nodo `texture` con sopra un `coordinate` in `mode: "world"`.

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
  noise_type: "fbm"            # perlin | fbm | turbulence | ridged | billow | hetero_terrain | hybrid_multifractal
  scale: 5.0
  octaves: 5                   # 1..16 — ottave per fBm/ridged/billow/musgrave
  lacunarity: 2.0              # moltiplicatore di frequenza fra ottave
  gain: 0.5                    # decadimento di ampiezza fra ottave (fbm/ridged/billow)
  fractal_increment: 1.0       # Musgrave H — solo per hetero_terrain / hybrid_multifractal
  fractal_offset: 0.7          # Musgrave offset / "sea level" — solo per hetero_terrain / hybrid_multifractal
  distortion: 0.0              # domain warp (deforma il dominio per organicità)
  noise_strength: 0.0          # legacy: 0=Perlin liscio, >0=turbolento (sovrascritto da noise_type)
  colors: [[0, 0, 0], [1, 1, 1]]
```
Le sette famiglie corrispondono alle modalità standard dei renderer professionali:
- `perlin` — gradient noise liscio a singola ottava.
- `fbm` — Σ noise/2^i, il "fractal noise" canonico di Arnold/Cycles/RenderMan.
- `turbulence` — Σ|noise|/2^i con valore assoluto per nitidezza.
- `ridged` — ridged multifractal di Musgrave, ridge nette (roccia, fulmini).
- `billow` — Σ|noise| sulle ottave, gonfio/cumuliforme.
- `hetero_terrain` — terreno eterogeneo di Musgrave (Ebert et al. §16.3.3):
  l'ampiezza di ogni ottava viene moltiplicata per il valore accumulato
  corrente, così le quote alte diventano rugose e le valli restano lisce.
  Il look canonico del terreno eroso, irraggiungibile con fBm puro.
- `hybrid_multifractal` — multifrattale ibrido di Musgrave (§16.3.4):
  il segnale di ogni ottava viene moltiplicato per un `weight` corrente
  (clampato a 1), producendo strati rocciosi stratificati e picchi netti.
  Usato per asteroidi, rocce aliene, marmi stratigrafici.

`distortion` deforma la posizione di input con un campione Perlin secondario
(tecnica di Inigo Quilez); 0.3–0.8 è di solito sufficiente. `fractal_increment`
(la H di Musgrave, default 1.0) controlla la velocità di decadimento delle
ottave alta-frequenza — H ≈ 0.25 produce terreno rugoso, H ≥ 1 produce campi
lisci dominati dalla bassa frequenza. `fractal_offset` (default 0.7) è il
bias di "sea level" aggiunto a ogni ottava; valori alti appiattiscono le
valli, valori bassi trasformano tutto in montagne. Questi due parametri
sono usati solo dalle modalità `hetero_terrain` / `hybrid_multifractal`
— gli altri tipi di noise li ignorano.

**Marble:**
```yaml
texture:
  type: "marble"
  scale: 4.0
  noise_strength: 10.0
  vein_axis: [1, 0, 0.3]       # direzione primaria di propagazione delle venature (default Z)
  vein_frequency: 1.0          # moltiplicatore sul termine sinusoidale
  vein_sharpness: 4.0          # 1=morbido (legacy), 4-8=venature sottili Carrara
  noise_type: "turbulence"     # turbulence | fbm | ridged
  octaves: 7                   # ottave del termine frattale
  lacunarity: 2.0
  gain: 0.5
  distortion: 0.0
  colors: [[0.95, 0.95, 0.95], [0.10, 0.10, 0.15]]
  secondary_wave:              # opzionale — venature incrociate Statuario / Calacatta
    axis: [1, 0, 0]            # seconda direzione di vena (auto-ortogonalizzata)
    frequency: 0.7             # indipendente dalla frequenza primaria
    strength: 0.5              # 0 = disattivato (back-compat), ~0.3-0.7 tipico
```
L'asse delle venature controlla la direzione di propagazione; un vettore non
allineato agli assi produce lastre naturali. `vein_sharpness` eleva la
sinusoide a potenza, restringendo la banda scura in una vera vena.

> **`secondary_wave` studio-quality.** Con `strength > 0` aggiunge una
> seconda sinusoide lungo `secondary_wave.axis` al termine principale:
> `sin(wave1) + strength · sin(wave2)`, rinormalizzato in [-1, 1]. L'asse
> secondario viene auto-ortogonalizzato contro il primario al sample-time,
> quindi anche scegliendolo collineare si ottengono comunque venature
> incrociate visibili — è il look Statuario / Calacatta / Arabescato
> irraggiungibile con una sola direzione. Combinabile con un `color_ramp:`
> a 3+ stop per autorialità vena → mid-tone → base → undertone.
> `strength = 0` (default) è bit-identico al comportamento legacy.

**Wood:**
```yaml
texture:
  type: "wood"
  scale: 4.0
  noise_strength: 2.0          # alias: grain_strength (ampiezza grain alta-freq)
  ring_axis: [0, 1, 0]         # asse del tronco; anelli ⊥ asse (default Y)
  ring_sharpness: 3.0          # 1=morbido (legacy), 3-6=legno tardivo definito
  axial_grain: 0.3             # noise a lunga lunghezza d'onda lungo l'asse
  octaves: 4                   # ottave fBm della venatura (1 = Perlin legacy)
  lacunarity: 2.0
  gain: 0.5
  distortion: 0.0              # 0=anelli puliti, ~0.5=nodi/onde
  colors: [[0.85, 0.65, 0.40], [0.60, 0.40, 0.20]]
  # ── Studio-quality (opt-in, tutti i default sono no-op back-compat) ──
  grain_scale: 1.0             # moltiplicatore sul sample-point del grain (alta freq)
  figure_scale: 0.25           # moltiplicatore sul sample-point della "figure" (bassa freq)
  figure_strength: 0.0         # 0 = disattivata, ~0.5-1.5 = curly maple / flame mahogany
  radial_anisotropy: 0.0       # 0 = isotropo (piano-sawn), >0 = quartato
  knot_density: 0.0            # 0 = nessun nodo, ~0.5 = sparsi, ~1 = pieno
```

> **Controlli studio-quality.**
> * **Bande grain + figure.** `grain_scale` + `noise_strength` (alias
>   `grain_strength`) pilotano il dettaglio fibra ad alta frequenza dentro
>   gli anelli; `figure_scale` + `figure_strength` aggiungono un'ondulazione
>   indipendente a bassa frequenza — le strisce di acero curly, le
>   ripple di mogano flame, i fiori del bird's-eye, irraggiungibili con il
>   solo grain perché il suo spettro è troppo alto.
> * **`radial_anisotropy`.** Stira il punto di campionamento del noise lungo
>   la direzione radiale locale (perpendicolare a `ring_axis`, uscente dal
>   tronco). Valori alti (~2–5) comprimono la coordinata radiale del
>   sample-point quindi il noise varia poco lungo quell'asse ⇒ look rovere
>   quartato. 0 (default) è isotropo e bit-identico al legacy.
> * **`knot_density`.** Voronoi a piccola scala genera nodi sparsi: il
>   centro dell'anello viene tirato verso il feature point del nodo e si
>   aggiunge un cuore scuro — stesso comportamento di Arnold `knots` e
>   RenderMan `PxrWoodKnot`. Combinabile con un `color_ramp:` a 3+ stop
>   per autorialità sapwood / heartwood / nodo.

#### **Marmi e legni production-quality — ricettario**

I knob studio-quality interagiscono in modo non banale con il BSDF e
l'illuminazione. Le ricette seguenti derivano dalla scena di riferimento
`library-marble-wood.yaml`; copia lo snippet e modifica il color
ramp per ottenere un materiale credibile in pochi minuti.

> **Checklist illuminazione prima di tunare un marmo.** Un marmo lucido
> a `roughness < 0.2` diventa quasi uno specchio e riflette l'ambiente
> verbatim — se il cielo è chiaro e senza dettaglio, il marmo si legge
> come "gradiente azzurro" invece che come marmo. Tre regole:
>
> 1. **Cielo scuro o quasi nero per i lookdev shot** (`type: "flat"`,
>    `color: [0.001, 0.001, 0.0012]`). La trama del marmo porta tutto
>    il peso visivo, non l'ambiente.
> 2. **Roughness 0.30–0.34 per il "lucido"** dove la texture deve
>    leggersi; alza clearcoat (0.85+) per lo strato superiore tipo
>    vernice lucida. Roughness più bassa solo quando serve un vero
>    riflesso a specchio — in quel caso conviene avere un HDRI con
>    contenuto da riflettere.
> 3. **L'illuminazione diretta deve dominare.** Direzionale key a
>    intensity 5–7 più una point fill fredda e una rim point calda
>    illuminano la componente diffusa sopra il riflesso speculare.
>    Senza questa tripletta l'integratore BSDF non riesce a separare
>    texture e ambiente.

**Carrara — base bianca con vena scura sottile.**
```yaml
- id: "carrara"
  type: "disney"
  roughness: 0.32
  specular: 0.5
  texture:
    type: "marble"
    scale: 5.0
    vein_axis: [0, 0, 1]
    vein_frequency: 1.0
    vein_sharpness: 4.0           # → base ampia, vena stretta
    noise_strength: 8.0
    octaves: 8
    colors: [[0.96, 0.94, 0.91], [0.10, 0.08, 0.10]]
```
Con `vein_sharpness ≥ 3` la media campionaria si avvicina a `colors[0]`
(base), quindi la BASE è il colore dominante e la vena si restringe in
sottili linee — esattamente il look Carrara reale.

**Calacatta — venature incrociate con transizioni dorate.**
```yaml
- id: "calacatta"
  type: "disney"
  roughness: 0.30
  texture:
    type: "marble"
    scale: 4.0
    vein_axis: [0, 0, 1]
    vein_sharpness: 3.0           # vene più larghe del Carrara (Calacatta è più audace)
    noise_strength: 6.0
    octaves: 8
    secondary_wave:
      axis: [1, 0, 0]             # auto-ortogonalizzato vs primaria
      frequency: 0.65             # rapporto non intero → no moiré
      strength: 0.45
    color_ramp:
      - { position: 0.00, color: [0.10, 0.08, 0.08], interp: "smoothstep" }
      - { position: 0.35, color: [0.85, 0.65, 0.30], interp: "smoothstep" }
      - { position: 0.70, color: [0.92, 0.85, 0.72], interp: "smoothstep" }
      - { position: 1.00, color: [0.97, 0.95, 0.90], interp: "linear"     }
```
Convenzione con sharpening corretto: ramp **position 0 = vena** (rara,
`t→0`), **position 1 = base** (dominante, `t→1`); gli stop intermedi
dipingono la transizione.

**Arabescato — cross-veining forte + distorsione caotica.**
```yaml
- id: "arabescato"
  type: "disney"
  roughness: 0.34
  texture:
    type: "marble"
    scale: 3.5
    vein_sharpness: 2.0
    noise_strength: 8.0
    distortion: 0.35              # warp Perlin extra
    secondary_wave: { axis: [1, 0.3, 0], frequency: 1.2, strength: 0.7 }
    color_ramp:
      - { position: 0.00, color: [0.08, 0.08, 0.10], interp: "smoothstep" }
      - { position: 0.50, color: [0.55, 0.50, 0.48], interp: "smoothstep" }
      - { position: 1.00, color: [0.94, 0.92, 0.88], interp: "linear"     }
```

**Rovere quartato — venatura radiale fibrosa.**
```yaml
- id: "rovere_quartato"
  type: "disney"
  roughness: 0.55
  texture:
    type: "wood"
    scale: 4.5
    ring_axis: [0, 1, 0]
    ring_sharpness: 4.0           # latewood netto e sottile
    noise_strength: 2.2
    octaves: 5
    radial_anisotropy: 3.0        # stretch quartato
    color_ramp:
      - { position: 0.00, color: [0.30, 0.18, 0.08], interp: "smoothstep" }
      - { position: 0.55, color: [0.82, 0.62, 0.38], interp: "smoothstep" }
      - { position: 1.00, color: [0.95, 0.82, 0.62], interp: "linear"     }
```
Il grain "si stira" lungo la direzione radiale locale. Combina con
ramp a 3 stop per autorialità sapwood / heartwood / earlywood.

**Curly maple — figure a onda larga.**
```yaml
- id: "curly_maple"
  type: "disney"
  roughness: 0.42
  texture:
    type: "wood"
    scale: 5.0
    ring_sharpness: 5.0           # bande strette → look "curly"
    noise_strength: 0.25          # quasi-spegne il grain così domina la figure
    figure_scale: 0.10            # bassa freq → ripple larghe
    figure_strength: 1.8
    color_ramp:
      - { position: 0.00, color: [0.55, 0.38, 0.20], interp: "smoothstep" }
      - { position: 0.45, color: [0.85, 0.72, 0.48], interp: "smoothstep" }
      - { position: 1.00, color: [0.98, 0.92, 0.76], interp: "linear"     }
```

**Pino nodoso — nodi di branca con cuore scuro.**
```yaml
- id: "pino_nodoso"
  type: "disney"
  roughness: 0.55
  texture:
    type: "wood"
    scale: 6.0                    # scale alta così i nodi ospitano anelli interni visibili
    ring_sharpness: 4.0
    noise_strength: 0.6
    figure_scale: 0.25
    figure_strength: 0.3
    knot_density: 1.0             # massimo numero di nodi
    color_ramp:
      - { position: 0.00, color: [0.05, 0.03, 0.02], interp: "smoothstep" }  # cuore nodo
      - { position: 0.18, color: [0.35, 0.18, 0.08], interp: "smoothstep" }  # latewood
      - { position: 0.65, color: [0.90, 0.68, 0.40], interp: "smoothstep" }  # earlywood
      - { position: 1.00, color: [0.97, 0.86, 0.60], interp: "linear"     }  # sapwood
```
Usa un **ramp a 4 stop** quando `knot_density > 0`: la position 0
riserva il tono più scuro al cuore del nodo, le 0.18–0.65 tengono la
gradazione normale degli anelli, la 1 è il sapwood più chiaro.

Un catalogo pre-cotto di queste ricette — Carrara, Calacatta, Statuario,
Arabescato, Port Laurent, Rosso Levanto + rovere quartato, curly maple,
flame mahogany, pino nodoso, bird's-eye maple, walnut burl, frassino
quartato, abete nodoso — è disponibile in
`scenes/libraries/materials/stones.yaml` e `woods.yaml` sotto il suffisso
`_studio`. Importa una volta e referenzia per id.

**Voronoi / Worley (cellulare):**
```yaml
texture:
  type: "voronoi"
  scale: 5.0
  metric: "euclidean"          # euclidean | euclidean_squared | manhattan | chebyshev
  output: "f1"                 # f1 | f2 | f3 | f4 |
                               # f2_minus_f1 | f3_minus_f1 |
                               # f1_plus_f2 | cell | random | position
  randomness: 1.0              # 0 = griglia, 1 = sparpagliamento casuale
  distortion: 0.0              # warp Perlin prima del lookup
  smoothness: 0.0              # 0 = hard min (classico); ∈ (0,1] abilita Smooth Voronoi (IQ)
  colors: [[0, 0, 0], [1, 1, 1]]   # endpoint della palette, ignorato per "cell" e "position"
```
Replica il nodo Voronoi di Cycles: `f1` produce ciottoli/blob,
`f2_minus_f1` crea "crackle" netti (terra screpolata, pelle di rettile),
`random` produce colore stocastico per-cella vincolato dalla palette
(rocce, scaglie, mosaici). La metrica Chebyshev produce pattern a
tessere quadrate/esagonali.

> **ID stocastico per-cella — `cell` vs `random` vs `position`.** Tre
> canali per-cella Cycles-compatibili con ruoli distinti:
> - `cell` — **hash RGB grezzo** dell'ID cella, l'output "Color" letterale
>   di Cycles. Colori arcobaleno saturi per cella, **ignora `colors:` e
>   `color_ramp:`**. Da usare quando vuoi un identificatore di colore
>   casuale non vincolato (es. come input di un nodo hue/sat o mix-RGB a
>   valle) o quando ti serve parità bit-identica con Cycles.
> - `random` — **scalare in [0, 1) per cella** mappato attraverso
>   `colors:` / `color_ramp:`, lo stesso percorso degli output distanza.
>   Ruolo identico al "Random" output di Cycles 3.0+. È quello che vuoi
>   per quasi tutti i materiali "rocce / ciottoli / scaglie / patch":
>   scegli `random` ogni volta che fornisci una palette muted e vuoi che
>   le celle ci restino dentro.
> - `position` — **XYZ cell-local del feature point F1 impacchettato in
>   RGB**. Decorrelato da `cell`, utile come ID stocastico 3D per seeding
>   di procedurali a valle o per trasformazioni UV random-per-island.
>   Bypassa `color_ramp:` (è un output identity vettoriale, non scalare).
>   Output "Position" di Cycles, position di RenderMan PxrVoronoise,
>   attributo `P_` di Houdini Voronoi.

> **Canali estesi (`f3`, `f4`, `f3_minus_f1`).** F3 e F4 sono le distanze
> al 3° e 4° feature più vicino nella finestra 3×3×3 di celle — stesso
> costo O(27) di F1/F2 dato che le 27 celle sono già scansionate. Si
> usano per shading cellulare gerarchico (cuoio multi-scala, mosaici
> cell-in-cell, voronoi-on-voronoi). `f3_minus_f1` produce una banda
> border più larga e a frequenza più bassa di `f2_minus_f1` — rim morbidi,
> gradienti tipo mortar. I canali estesi usano sempre il hard min —
> `smoothness` viene intenzionalmente ignorato (stessa convenzione di
> Cycles per gli output Cell / Random: i descrittori di topologia
> discreta non vengono smussati).

> **Nota su `f2_minus_f1`.** Matematicamente, `F2-F1` è **zero sul bordo
> della cella** (bisettrice fra due punti-feature) e cresce fino al massimo
> al centro della cella. Il lerp usa `t = sqrt(F2-F1 / norm)` — la
> compressione sqrt riproduce la risposta "Distance to Edge" di Cycles —
> quindi `t = 0` → `colors[0]` è il **colore del bordo** e `t = 1` →
> `colors[1]` è il **colore dell'interno cella**. Per il classico look
> crackle (linee chiare sottili su sfondo scuro) metti il colore **chiaro**
> al PRIMO posto e quello **scuro** al SECONDO.

> **Smooth Voronoi (`smoothness`).** Con `smoothness > 0` il `min()` hard
> sulle 3×3×3 celle vicine viene sostituito dal soft-min log-sum-exp di
> Inigo Quilez `-log(Σ exp(-k·d_i)) / k` con `k = 20/smoothness`. F1
> diventa C∞ attraverso i bordi cella; F2 viene calcolato dalla stessa
> accumulazione escludendo il peso dominante (quello della cella più
> vicina), così `f2_minus_f1` perde il ridge a V — bordi morbidi, niente
> alias a step lungo le creste. Utile per cuoio levigato, ciottoli
> arrotondati dall'acqua, pelle di rettile, marmo poro-chiuso.
> `smoothness = 0` (default) è bit-identica al hard min legacy. Gli
> output `cell` / `random` sono volutamente immuni al parametro
> (lookup per-cella discreto, come in Cycles). Contratto numerico: l'accumulatore lavora in doppia
> precisione e la somma è ri-ancorata alla distanza hard più vicina, così
> nessun argomento di `exp()` supera mai `0`; con `smoothness → 0`
> (i.e. `k → ∞`) il risultato converge al hard `Evaluate` classico entro
> la precisione float32.

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
  length: 1.0                  # span del gradiente (in unità object-local)
  colors: [[0, 0, 0], [1, 1, 1]]
```
- `linear` — `t = (p · axis) / length`.
- `quadratic` / `easing` — stesso `t` poi elevato al quadrato o smoothstepped.
- `spherical` — distanza dall'origine / `length`.
- `radial` — distanza dalla retta `axis` / `length` (decadimento cilindrico).

**Coordinate (debug / driver di coord-space):**
```yaml
texture:
  type: "coordinate"             # alias: coord | coords | texture_coord | tex_coord | st
  mode: "object"                 # object | uv | generated | world
  scale: 1.0                     # moltiplicatore sui coord prima di fract() / clamp generated
  bounds_min: [-1, -1, -1]       # solo per mode: "generated" — corner inferiore del reference box
  bounds_max: [1, 1, 1]          # solo per mode: "generated" — corner superiore
  offset: [0, 0, 0]
  rotation: [0, 0, 0]
```
Ritorna le coordinate del shading point come RGB. Equivale al nodo
"Texture Coordinate" di Cycles, a `Pref` / `Pworld` / `uvCoord` di
RenderMan e al node `utility` di Arnold. Due usi principali:
(1) **overlay di debug** per verificare a colpo d'occhio gli unwrap UV
e l'allineamento object/world space, (2) **driver XYZ deterministico**
per pilotare un'altra texture (via mix material) con un sistema di
coordinate scelto al posto del sample-point object-local implicito.

- `object` — `fract(rec.LocalPoint · scale)`. Stesso spazio in cui
  campionano tutte le altre procedurali (Noise/Marble/Wood/Voronoi).
- `uv` — `(u, v, 0)` raw (no fract). Mostra il parametrizzazione UV
  della primitiva direttamente; la cucitura sferica è visibile come
  linea.
- `generated` — `clamp((LocalPoint − bounds_min) / (bounds_max − bounds_min), 0, 1)`.
  Il workflow "reference-space" reso popolare da `Pref` di RenderMan:
  l'artista dichiara l'AABB canonico dell'oggetto (tipicamente la
  rest-pose box) e ogni nodo a valle vede un parametro `[0, 1]³`
  pulito indipendentemente da come la superficie viene trasformata o
  displaced al render time. Default `[-1, 1]³`, che corrisponde
  all'AABB object-space di sfera/cubo/cilindro unitari. Smooth, niente
  fract — i corner mappano esattamente sugli estremi del color-cube.
- `world` — `fract(rec.Point · scale)`. Grid world-locked che NON
  segue l'oggetto quando si muove; ideale per laser-grid, gusci di
  polvere world-aligned, debug spheres tipo "you-are-here".

I parametri standard `offset` / `rotation` agiscono PRIMA del wrap
`fract` (Object / World) o PRIMA della normalizzazione bounds
(Generated). `color_ramp:` è volutamente non supportato — Coordinate
è un output identity vettoriale, non scalare mappabile su un ramp 1-D.

> **Back-compat dell'overload `Value(in HitRecord rec)`.** Aggiungere
> Coordinate ha richiesto di esporre `rec.Point` alle texture, quindi
> questo ciclo introduce un overload `ITexture.Value(in HitRecord rec)`
> con default che inoltra `(rec.U, rec.V, rec.LocalPoint, rec.ObjectSeed,
> rec.Footprint)`. Tutte le texture esistenti (Noise, Marble, Wood,
> Voronoi, Brick, Gradient, Checker, Image, SolidColor) ereditano
> l'inoltro default e si comportano bit-identicamente al codice
> pre-ciclo su ogni input. Solo Coordinate fa override dell'overload
> per leggere `rec.Point` e `rec.LocalPoint` separatamente.

**Tutte le procedurali supportano:**
```yaml
offset: [5.0, 0.0, 3.0]                  # Traslazione (unità object-local)
rotation: [0.0, 45.0, 0.0]               # Rotazione (gradi)
randomize_offset: true                    # Decorrelamento per-oggetto
randomize_rotation: true                  # Orientamento per-oggetto
```
`randomize_offset` somma al sample point un offset hash-of-`seed` di magnitudine
**±10 wu** (era ±1000 wu nei cicli precedenti: il valore alto faceva collassare
in righe parallele le procedurali radiali come `wood`, spingendo il sample
troppo lontano dall'asse degli anelli — vedi nota Sampling space sopra). Con il
nuovo campionamento object-local, due istanze dello stesso materiale a
posizioni diverse leggono già regioni di texture diverse; `randomize_offset` è
oggi un knob *aggiuntivo* di decorrelamento, non più necessario per la
variazione per-entità. Tieni `randomize_rotation: true` sulle procedurali
condivise così identici material ID non si leggono come cloni su una griglia
in stile tavole di legno affiancate.

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

#### **Surface Displacement (material-level — parità Cycles/RenderMan)**

Vera deformazione geometrica delle mesh subdivise. A differenza di
`bump_map` (che perturba solo la normale di shading) il displacement
sposta fisicamente i vertici, quindi **la silhouette cambia** — il
contorno contro il cielo riflette la deformazione. Il displacement è
parte del material (socket "Material Output → Displacement" di Cycles,
`PxrDisplace` nella shader network di RenderMan): un material displaced
guida ogni mesh che lo referenzia, senza duplicazione per-entity.

```yaml
materials:
  - id: "stone_displaced"
    type: "disney"
    color: [0.82, 0.66, 0.42]
    roughness: 0.78
    displacement:
      mode: "scalar"                  # scalar | vector
      space: "tangent"                # vector mode: tangent | object
      texture:                        # qualunque ITexture (procedurale o image)
        type: "noise"
        noise_type: "fbm"
        scale: 3.5
        octaves: 5
        colors: [[0, 0, 0], [1, 1, 1]]
      scale: 0.30                     # ampiezza con segno (world units)
      midlevel: 0.5                   # valore texture "piatto" (0.5 per 8-bit)
      uv_scale: 1.0
      bound: 0.30                     # padding AABB foglia BVH; autoderivato se omesso
      displacement_method: "both"     # both | displacement | bump_only
      autobump: true                  # equivalente Arnold autobump_visibility
      autobump_strength: 1.5
      autobump_scale: 1.0
```

L'update vertex per scalar è `v' = v + scale · (h − midlevel) · n_smooth`
con `h = luminanza Rec.709 della texture`. Vector legge la tripletta
RGB e fa offset lungo la base TBN per-vertex (`tangent`: R→T, G→B, B→N,
convenzione di bake Mudbox/Maya/ZBrush/Cycles) o direttamente come
offset locale `(x, y, z)` (`object`). Le normali smooth post-displacement
sono ricalcolate dalla topologia displaced così il BSDF vede la
silhouette nuova.

| Campo                              | Tipo        | Default | Note |
|------------------------------------|-------------|---------|------|
| `displacement.mode`                | string      | `"scalar"` | `"scalar"` legge luminanza e fa offset lungo la normale; `"vector"` legge RGB come offset 3D. |
| `displacement.space`               | string      | `"tangent"` | Solo vector. `"tangent"` richiede UV; il loader fallback silenzioso a `"object"` se assente. |
| `displacement.texture`             | TextureData | —       | Height field interno. Qualunque procedurale o `image`. |
| `displacement.scale`               | float       | `0.1`   | Ampiezza con segno (world units). Negativo spinge verso l'interno. |
| `displacement.midlevel`            | float       | `0`     | Valore texture = "nessun displacement". `0.5` per 8-bit / unsigned EXR. |
| `displacement.uv_scale`            | float > 0   | `1.0`   | Moltiplicatore UV uniforme. |
| `displacement.bound`               | float ≥ 0   | `\|scale\|` (scalar) / `\|scale\|·√3` (vector) | Massimo displacement atteso. Padding AABB foglia BVH (Arnold `disp_padding`, RenderMan `dispBound`). |
| `displacement.displacement_method` | string      | `"both"` | Tri-state Cycles. `"both"` displacement + autobump; `"displacement"` solo geometrico; `"bump_only"` solo bump. |
| `displacement.autobump`            | bool        | `false` | Deriva un bump residuo dalla stessa texture e l'attacca alla mesh (Arnold `autobump_visibility`). |
| `displacement.autobump_strength`   | float ≥ 0   | `1.0`   | Moltiplicatore d'ampiezza; risultante = `autobump_strength · \|scale\|`. |
| `displacement.autobump_scale`      | float > 0   | `1.0`   | Moltiplicatore frequenza UV dell'autobump. `>1` per campionare più fine del displacement. |

**Solo mesh.** Il displacement material-level è applicato solo dal ramo
`type: mesh` (stessa scelta architetturale di Arnold `polymesh` e
Cycles True Displacement). Entità non-mesh che referenziano un material
displaced producono un warning in load e usano solo lo shading senza
deformazione geometrica.

> **Sostituisci le primitive analitiche con proxy mesh per il displacement.**
> Quando ti serve una sfera/cubo/toro displaced, carica un proxy
> poligonale e lascia che `subdivision_scheme:` lo ri-tessellare sotto
> controllo screen-space adattivo. I proxy stock sono in `scenes/models/`:
> - `subdivision-icosahedron.obj` — sfera unitaria (subdivision Loop)
> - `subdivision-cube.obj` — cubo unitario (Catmull-Clark)
> - varianti più dense generate via `dotnet run --project src/Tools/...`
>
> Esempio: una `type: "sphere"` analitica in `(x, y, z)` con raggio `r`
> diventa
> ```yaml
> - type: "mesh"
>   path: "../models/subdivision-icosahedron.obj"
>   subdivision_scheme: "loop"
>   subdivision_pixel_error: 6.0          # adattivo: stop a ≤6 px per edge
>   subdivision_max_iterations: 5
>   scale: [r, r, r]
>   translate: [x, y, z]
>   material: "materiale_displaced"
> ```
> La subdivision adattiva tiene il costo proporzionale alla dimensione
> a schermo: le sfere lontane restano grossolane, quelle in primo piano
> si raffinano da sole. Usa la forma a iterazioni fisse
> (`subdivision_iterations: N`) solo per render CI / regression
> deterministici. Tutti i file `scenes/showcases/library-*.yaml` che
> dimostrano una libreria materiali adottano questo pattern
> (es. `library-concretes.yaml`,
> `library-leathers.yaml`, `stones-…`).

**Ordine di composizione.** L'engine combina le perturbazioni nello
stesso ordine di Arnold/Cycles:

```
normale geometrica (post-displacement)
  → material.normal_map
    → material.bump_map
      → mesh.autobump                 (← derivato da displacement.texture)
```

`coat_normal_map` del Disney BSDF perturba solo il clearcoat e resta
indipendente da questo stack.

**Pipeline.** `subdivide → displace → triangulate → BVH`. Su mesh non
subdivise il displacement sposta solo i vertici originali, raramente
utile; combinare con `subdivision_iterations ≥ 4` (o
`subdivision_pixel_error` adattivo).

**Override per-entity.** Aggiungere `displacement_enabled: false` su
una mesh entity per disabilitare il displacement del material risolto
per quella singola istanza (il material resta comunque condiviso con
altre mesh che invece lo applicano). Utile per LOD/proxy.

**Mix material displacement (Cycles "Mix Shader → Displacement").** Un
material `type: mix` con `displacement: { blend_with_mask: true }`
vector-blenda le offset per-vertex dei due child usando la STESSA
mask/blend del Mix BSDF. Risultato C0-continuo lungo le cuciture; il
loader emette warning e disabilita il mix-displacement se uno dei due
child non ha proprio displacement. L'autobump si compone come
`MixBumpMapTexture` con lo stesso fattore.

```yaml
- id: "weathered_rock"
  type: "mix"
  material_a: "rock_clean"
  material_b: "rock_moss"
  mask:
    type: "noise"
    scale: 3.0
  displacement:
    blend_with_mask: true
```

Le scene showcase
`scenes/showcases/texture-displacement-scalar.yaml`,
`texture-displacement-vector.yaml`,
`texture-displacement-combo.yaml` e
`texture-displacement-material-mix.yaml` coprono tutti i flussi.

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

##### **Surface displacement (material-level)**

> **Nota di migrazione (dal `2026-05`).** Il displacement è ora
> dichiarato sul material sotto `materials:`, non sull'entity. Vedi la
> sottosezione **Surface Displacement** del §5 ("Materiali") per lo
> schema completo (scalar/vector, autobump, `displacement_method`,
> Mix-blend). Le mesh entity possono sopprimere il displacement
> ereditato per-istanza con `displacement_enabled: false`.

```yaml
materials:
  - id: "pietra_displaced"
    type: "disney"
    color: [0.82, 0.66, 0.42]
    roughness: 0.78
    displacement:
      texture: { type: "noise", noise_type: "fbm", scale: 3.5, octaves: 5 }
      scale: 0.30
      midlevel: 0.5
      bound: 0.30

entities:
  - name: "pannello_pietra"
    type: "mesh"
    path: "models/plane.obj"
    material: "pietra_displaced"
    subdivision_scheme: "catmull_clark"
    subdivision_iterations: 6
    # displacement_enabled: false   # opzionale bypass per-istanza
```

Tutti i campi scalar/vector/autobump e il tri-state Cycles
`displacement_method` sono documentati nella sezione material. L'entity
mesh accetta solo `displacement_enabled: bool` (default `true`) per
sopprimere un displacement ereditato per-istanza.

#### **7.13 HeightField (Terreno stile Mitsuba)**

Una superficie continua `y = h(x, z) · height_scale` sul rettangolo XZ
definito da `bounds: [xMin, zMin, xMax, zMax]`. La funzione altezza
proviene o da una heightmap PNG-16 grayscale prebakeata, oppure da una
texture procedurale campionata a tempo di costruzione su una griglia
interna. L'intersezione è accelerata da una min/max mipmap
(Tevs/Ihrke/Seidel 2008) — una sola primitiva sostituisce un'intera
mesh di terreno tassellata.

```yaml
# Variante baked (il formato che TerrainGen emette)
- name: "terrain"
  type: "heightfield"
  bounds: [-50, -50, 50, 50]
  max_height: 25
  height_scale: 25
  heightmap_path: "libraries/terrain/myterrain-height.png"
  sea_level: 7.5
  sea_material: "water"
  strata:
    - { min_altitude: 0.00, max_altitude: 0.18, material: "sand",  blend_width: 0.04 }
    - { min_altitude: 0.14, max_altitude: 0.55, material: "grass", blend_width: 0.08 }
    - { min_altitude: 0.50, max_altitude: 0.85, min_slope_deg: 25, material: "rock", blend_width: 0.08 }
    - { min_altitude: 0.80, max_altitude: 1.00, material: "snow",  blend_width: 0.04 }
  material: "grass"   # fallback se nessuna band vince

# Variante procedurale — heightmap sintetizzata al caricamento da una noise texture
- name: "procedural_terrain"
  type: "heightfield"
  bounds: [-50, -50, 50, 50]
  max_height: 25
  height_scale: 25
  resolution: 512
  height_texture:
    type: "noise"
    noise_type: "hetero_terrain"
    scale: 0.012
    octaves: 5
    lacunarity: 2.0
    fractal_offset: 0.65
  material: "rock"
```

| Campo             | Tipo    | Default | Note |
|-------------------|---------|---------|------|
| `bounds`          | `[f]`   | —       | `[xMin, zMin, xMax, zMax]`. L'AABB Y è `[0, max_height]`. |
| `max_height`      | float   | `25`    | Tetto world-space usato per l'AABB; `max_height` ≥ altezza del picco. |
| `height_scale`    | float   | `1`     | Moltiplicatore applicato ai valori normalizzati della heightmap (unità PNG-16 = 1). Il picco world-space è `max(heightmap) × height_scale`. |
| `heightmap_path`  | string  | —       | Path PNG risolto rispetto alla scena master. 16-bit grayscale (`L16`) preferito; 8-bit accettato con un warning di perdita di precisione. Mutuamente esclusivo con `height_texture` (vince il path). |
| `height_texture`  | object  | —       | Blocco `TextureData` completo — qualsiasi noise procedurale. La luminanza di `Value(u, v, p)` diventa l'altezza. |
| `resolution`      | int     | `512`   | Solo in modalità procedurale: lato della griglia pre-campionata che alimenta la piramide min/max. La qualità visiva è determinata dalla bisezione per-pixel; questo controlla la tightness dell'accelerazione. |
| `max_steps`       | int     | `256`   | Riservato per raffinamenti iterativi futuri; la pipeline v1 usa sempre 12 step di bisezione. |
| `sea_level`       | float?  | nessuno | Y world-space di un piano d'acqua opzionale clippato al footprint della heightfield. Visibile solo dove il terreno sotto sta sotto `sea_level` (niente piani d'acqua fluttuanti). |
| `sea_material`    | string? | nessuno | ID del materiale applicato al piano d'acqua. Obbligatorio quando `sea_level` è impostato. |
| `strata`          | lista   | nessuna | Band di materiali pilotate da altitudine/pendenza; vedi sotto. |
| `material`        | string  | —       | Materiale di fallback usato nei punti di shading dove nessuna band `strata` vince. |

##### **Strata bands**

Ogni voce `strata` definisce una finestra di altitudine e/o pendenza
mappata a un materiale. Il motore calcola
`altitude_norm = (hit.Y − sea_level) / (height_scale − sea_level)` e
`slope_deg = acos(normal.Y)`, poi assegna a ogni band un peso a plateau
con fade ai bordi; vince la band col punteggio più alto. Le band
possono sovrapporsi — la zona di sovrapposizione allarga l'alone di
dominanza della band vincente.

| Campo           | Tipo   | Default | Note |
|-----------------|--------|---------|------|
| `min_altitude`  | float  | `0`     | Bordo inferiore normalizzato del plateau di altitudine (0 = livello mare, 1 = picco). |
| `max_altitude`  | float  | `1`     | Bordo superiore normalizzato. |
| `min_slope_deg` | float  | `0`     | Bordo inferiore del plateau di pendenza (gradi dalla verticale; 0 = piano). |
| `max_slope_deg` | float  | `90`    | Bordo superiore. |
| `blend_width`   | float  | `0`     | Larghezza dell'alone di fade oltre il plateau. In v1 la selezione è winner-takes-all sul peso combinato; il lerp dei materiali tra band adiacenti è un follow-up. |
| `material`      | string | —       | ID del materiale per questa band. |

Il sistema strata è esattamente quello che TerrainGen emette per dare a
una singola heightfield la stratificazione sabbia → erba → roccia →
neve che il vecchio approccio per-mesh produceva con OBJ separati per
stratum. Vedi `docs/technical/heightfield.md` per l'algoritmo.

#### **7.14 CSG (Operazioni Booleane)**
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

#### **7.15 Group (Composizione Gerarchica)**
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

#### **7.16 Template + Instance (Oggetti Riutilizzabili)**
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

#### **7.17 Lathe (Superficie di Rivoluzione)**
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

#### **7.18 Extrusion (Estrusione lineare di un profilo 2D)**
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

#### **7.19 Ordine delle trasformazioni e anti-pattern `center:`**

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
