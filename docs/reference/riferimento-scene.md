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
world:      # Ambiente (cielo, luce ambientale, sfondo, terreno)
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
  ambient_light: [0.05, 0.05, 0.08]      # Luce di riempimento omnidirezionale
  background: [0.5, 0.7, 1.0]            # Colore del cielo (se non c'è un oggetto sky)
  ground:                                  # (opzionale) Pavimento autogenerato
    type: "infinite_plane"
    material: "floor_name"
    y: 0.0
  sky:                                     # (opzionale) Sostituisce lo sfondo
    type: "gradient"  # o "hdri"
    # ... vedi dettagli sotto
  medium:                                  # (opzionale) Mezzo partecipante globale
    type: "homogeneous"
    # ... vedi dettagli sotto
```

#### **Gradient Sky** (raccomandato per scene all'aperto):
```yaml
sky:
  type: "gradient"
  zenith_color:  [0.10, 0.30, 0.80]      # Parte superiore del cielo
  horizon_color: [0.65, 0.80, 1.00]      # Orizzonte
  ground_color:  [0.30, 0.25, 0.20]      # Riflesso del terreno
  sun:                                     # (opzionale)
    direction:  [-0.5, -1.0, -0.3]       # Direzione da cui PROVIENE la luce solare
    color:      [1.0, 0.98, 0.85]
    intensity:  12.0
    size:       2.5                        # Dimensione angolare in gradi
    falloff:    48.0                       # Esponente bagliore (più alto = più nitido)
```

#### **HDRI/IBL** (per il massimo realismo):
```yaml
sky:
  type: "hdri"
  path: "hdri/studio.hdr"                 # Percorso relativo al file YAML
  intensity: 1.0                           # Moltiplicatore esposizione
  rotation: 90                             # Rotazione asse Y in gradi
```

**Configurazioni Sky Predefinite:**
- **Noon** (cielo pulito, sole luminoso)
- **Golden Hour** (sole basso e caldo, orizzonte saturato)
- **Sunset** (orizzonte arancione drammatico)
- **Night** (valori minimi per zenith/orizzonte, disco solare fioco)
- **Overcast** (luce ambientale alta, cielo uniforme)

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

- **Uso:** Simula nebbia, fumo, foschia atmosferica, nubi, effetti subacquei.
- **Tip rendering:** `homogeneous` e `height_fog` sono analitici ed economici. `procedural` e `grid` usano delta tracking e sono più rumorosi — alza `-s` a 400/576/1024 e mantieni `-d 6-8`. Per scene con nebbia densa considera `-C 25`. Vedi [Profili di Rendering](./profili-di-rendering.md) §8 per la guida completa.
- **Effetti:** Luci spot → god-ray visibili; point light → aloni; directional → aerial perspective (con `height_fog`).

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
```

#### **Camera Singola** (legacy):
```yaml
camera:
  position: [0, 2, -8]                    # Posizione camera
  look_at: [0, 0, 0]                      # Punto di mira
  vup: [0, 1, 0]                          # Vettore "su" (per il rollio)
  fov: 60                                  # Campo visivo verticale (gradi)
  aperture: 0.1                            # Diametro lente (0 = pinhole)
  focal_dist: 8.0                          # Distanza dal piano di fuoco
```

**Uso dalla CLI:**
```bash
dotnet run ... -- -i scene.yaml --list-cameras      # Elenca le disponibili
dotnet run ... -- -i scene.yaml -c top -o top.png   # Per nome
dotnet run ... -- -i scene.yaml -c 1 -o cam1.png    # Per indice (base 0)
```

**⚠️ Profondità di Campo:** Quando `aperture > 0`, imposta `focal_dist` con la distanza effettiva tra la camera e il soggetto principale. Il default `focal_dist: 1.0` creerà un sfocatura estrema non voluta.

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
| `normal_map` | blocco | — | — | Texturing | Perturbazione della superficie |

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
Le texture sono definite **all'interno** delle definizioni dei materiali:

#### **Texture Procedurali:**
**Checker:**
```yaml
texture:
  type: "checker"
  scale: 4.0
  colors: [[0.9, 0.9, 0.9], [0.1, 0.1, 0.1]]
```
**Noise (Perlin):**
```yaml
texture:
  type: "noise"
  scale: 5.0
  noise_strength: 3.0                     # 0=liscio, >0=turbolento
```
**Marble:**
```yaml
texture:
  type: "marble"
  scale: 10.0
  noise_strength: 8.0
  colors: [[0.95, 0.95, 0.95], [0.4, 0.4, 0.4]]
```
**Wood:**
```yaml
texture:
  type: "wood"
  scale: 3.0
  noise_strength: 2.0
  colors: [[0.85, 0.65, 0.4], [0.6, 0.4, 0.2]]
```
**Tutte le procedurali supportano:**
```yaml
offset: [5.0, 0.0, 3.0]                  # Traslazione
rotation: [0.0, 45.0, 0.0]               # Rotazione (gradi)
randomize_offset: true                    # Variazione per ogni oggetto
randomize_rotation: true
```

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

#### **7.13 CSG (Operazioni Booleane)**
```yaml
- name: "lens"
  type: "csg"
  operation: "intersection"                # "union", "intersection", "subtraction"
  operand_a:
    type: "sphere"
    center: [-0.5, 0, 0]
    radius: 1.0
  operand_b:
    type: "sphere"
    center: [0.5, 0, 0]
    radius: 1.0
  material: "glass"
```
- Supporta alberi CSG nidificati ricorsivamente

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

---

### 8. **SEZIONE LIGHTS** — Cinque Tipi
#### **8.1 Point Light (Omnidirezionale)**
```yaml
- type: "point"
  position: [2, 5, -3]
  color: [1.0, 0.95, 0.85]
  intensity: 20.0                          # Range: 4–30
```
- Decadimento quadratico con la distanza

#### **8.2 Directional Light (Sole)**
```yaml
- type: "directional"  # alias: "sun"
  direction: [-0.5, -1.0, -0.3]           # Da dove PROVIENE la luce
  color: [1.0, 0.98, 0.92]
  intensity: 0.8                           # Range: 0.05–2.0
```
- Nessuna attenuazione con la distanza

#### **8.3 Spot Light (Cono)**
```yaml
- type: "spot"  # alias: "spotlight"
  position: [0, 5, 0]
  direction: [0, -1, 0]                   # Dove punta il faretto
  color: [1.0, 0.9, 0.7]
  intensity: 40.0
  inner_angle: 15                         # Gradi (piena luminosità)
  outer_angle: 30                         # Gradi (zona di sfumatura)
```

#### **8.4 Area Light (Ombre Morbide)**
```yaml
- type: "area"  # alias: "area_light", "rect", "rect_light"
  corner: [-1.5, 4.99, -1.5]              # Un angolo
  u: [3.0, 0.0, 0.0]                      # Primo bordo
  v: [0.0, 0.0, 3.0]                      # Secondo bordo
  color: [1.0, 0.97, 0.9]
  intensity: 35.0                          # Range: 15–60
  shadow_samples: 16                       # Campioni per punto
```
- Ombre morbide Monte Carlo con penombra

#### **8.5 Sphere Light (Ombre Morbide Isotropiche)**
```yaml
- type: "sphere"  # alias: "sphere_light", "ball", "ball_light"
  position: [0, 5, 0]
  radius: 0.5                              # Più grande = ombre più morbide
  color: [1.0, 0.95, 0.85]
  intensity: 30.0
  shadow_samples: 16
```

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
  ambient_light: [0.05, 0.05, 0.08]
  background: [0.3, 0.6, 1.0]
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
- `/docs/tutorial/02-tutorial-scene/` — Tutorial completi divisi per sezione.
**Codice Sorgente (Parsing Scene):**
- `/src/RayTracer/Scene/SceneLoader.cs` — Parsing YAML e costruzione scena
**Scene di Esempio:**
- `/scenes/sample.yaml` — Scena di riferimento semplice
- `/scenes/cornell-box.yaml` — Classica Cornell Box
- `/scenes/libraries/` — Materiali, luci e oggetti riutilizzabili

---

### 12. **BEST PRACTICES PER SCENE DI ALTA QUALITÀ**
1. **Strategia Materiali:**
   - Usa `lambertian` per grandi superfici di sfondo.
   - Usa `disney` o `metal` solo per gli oggetti protagonisti.
   - Usa il materiale `mix` per effetti realistici di usura.
2. **Configurazione Luci:**
   - Inizia con una luce direzionale + gradient sky per scene outdoor.
   - Aggiungi alcune luci point o area per riempimento/accento.
   - Usa sphere lights per ombre morbide ed isotropiche.
3. **Camera e Composizione:**
   - Usa la lista `cameras: []` per gestire più inquadrature.
   - Imposta `focal_dist` sulla distanza reale dal soggetto.
   - Testa con il profilo Preview (`-w 400 -H 267 -s 64 -d 4 -S 1`) — vedi [Profili di Rendering](./profili-di-rendering.md).
4. **Ottimizzazione Performance:**
   - Usa template + istanze per oggetti ripetuti.
   - Importa materiali/luci condivisi dalle librerie.
   - Raggruppa geometrie simili in gruppi per gerarchie più pulite.

---

Questa guida copre tutto il necessario per scrivere file di scena YAML di qualità professionale. Tutte le informazioni provengono direttamente dalla documentazione del progetto, dai file di esempio e dalla struttura del codice sorgente.
