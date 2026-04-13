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
Ogni file YAML di scena ha **5 sezioni principali** (in questo ordine):
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
  metallic: 0.0                            # 0=dielettrico, 1=metallo
  roughness: 0.3                           # 0=specchio, 1=diffuso
  subsurface: 0.0                          # SSS (cera, pelle)
  specular: 0.5                            # Intensità speculare dielettrica
  specular_tint: 0.0                       # Tinta speculare verso il colore
  sheen: 0.0                                # Lucentezza radente (tessuto)
  sheen_tint: 0.5
  clearcoat: 1.0                           # Secondo lobo speculare
  clearcoat_gloss: 0.9                     # Rugosità clearcoat
  spec_trans: 0.0                          # 0=opaco, 1=vetro
  ior: 1.5                                  # Indice di rifrazione
  texture: (opzionale)
  normal_map: (opzionale)
```
- **Quando usarlo:**
  - Metalli: `metallic=1.0`, rugosità variabile
  - Plastiche: `metallic=0.0`, `roughness=0.4–0.8`
  - Vernice auto: `metallic=0.0`, `clearcoat=1.0`
  - Tessuti: `metallic=0.0`, `sheen=0.5–1.0`
  - Pelle: `metallic=0.0`, `subsurface=0.3–0.5`
  - Vetro: `metallic=0.0`, `spec_trans=1.0`, `roughness=0.0`
- **⚠️ Rumore (Noise):** Disney richiede circa 4 volte i campioni rispetto ai materiali classici.
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
   - Testa con bassa risoluzione e pochi campioni (`-w 400 -H 267 -s 4 -d 10`).
4. **Ottimizzazione Performance:**
   - Usa template + istanze per oggetti ripetuti.
   - Importa materiali/luci condivisi dalle librerie.
   - Raggruppa geometrie simili in gruppi per gerarchie più pulite.

---

Questa guida copre tutto il necessario per scrivere file di scena YAML di qualità professionale. Tutte le informazioni provengono direttamente dalla documentazione del progetto, dai file di esempio e dalla struttura del codice sorgente.
