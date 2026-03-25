# Tutorial: Creazione dei File di Scena (YAML)

## Indice
1. [Struttura del File](#1-struttura-del-file)
2. [Sezione `world`](#2-sezione-world)
   - [2.1 Gradient Sky e Sun Disk](#21-gradient-sky-e-sun-disk)
   - [2.2 HDRI / IBL (Environment Map)](#22-hdri--ibl-environment-map)
3. [Sezione `camera`](#3-sezione-camera)
4. [Sezione `materials`](#4-sezione-materials)
   - [4.1 Lambertian (Opaco)](#41-lambertian-diffusoopaco)
   - [4.2 Metal (Metallico)](#42-metal-metallicospeculare)
   - [4.3 Dielectric (Vetro)](#43-dielectric-vetrotrasparente)
   - [4.4 Emissive (Luminoso)](#44-emissive-luminoso)
5. [Sezione `textures`](#5-sezione-textures)
   - [5.1 Tipi di Texture Procedurali](#51-tipi-di-texture-procedurali)
   - [5.2 Trasformazioni Spaziali (Offset & Rotation)](#52-trasformazioni-spaziali-offset--rotation)
   - [5.3 Randomizzazione per Oggetto](#53-randomizzazione-per-oggetto)
   - [5.4 Image Texture (Texture da File)](#54-image-texture-texture-da-file)
   - [5.5 Normal Map](#55-normal-map)
6. [Sezione `entities`](#6-sezione-entities)
   - [6.1 Sphere (Sfera)](#61-sphere-sfera)
   - [6.2 Box (Cubo/Parallelepipedo)](#62-box-cuboparallelepipedo)
   - [6.3 Cylinder (Cilindro)](#63-cylinder-cilindro)
   - [6.4 Triangle (Triangolo)](#64-triangle-triangolo)
   - [6.5 Quad (Quadrilatero)](#65-quad-quadrilatero)
   - [6.6 Disk (Disco)](#66-disk-disco)
   - [6.7 Plane / Infinite Plane (Piano Infinito)](#67-plane--infinite-plane-piano-infinito)
   - [6.8 Trasformazioni (Translate, Rotate, Scale)](#68-trasformazioni-translate-rotate-scale)
   - [6.9 Parametro Seed](#69-parametro-seed)
7. [Sezione `lights`](#7-sezione-lights)
   - [7.1 Point Light](#71-point-light-puntiforme)
   - [7.2 Directional Light](#72-directional-light-sole)
   - [7.3 Spot Light](#73-spot-light-faretto)
   - [7.4 Area Light](#74-area-light-emettitore-rettangolare)
   - [7.5 Calibrazione dell'Intensità](#75--calibrazione-dellintensità)
8. [Illuminazione: Come Funziona](#8-illuminazione-come-funziona)
9. [Esempi Completi](#9-esempi-completi)
10. [Regole e Best Practices](#10-regole-e-best-practices)

---

## 1. Struttura del File

Ogni file di scena è un documento YAML con 5 sezioni principali:

```yaml
world:      # Ambiente globale (cielo, terreno, luce ambiente)
camera:     # Punto di vista e ottica
materials:  # Definizione dei materiali (colori, texture, proprietà fisiche)
entities:   # Oggetti 3D nella scena
lights:     # Sorgenti di luce
```

> **Nota:** I colori sono sempre espressi come `[R, G, B]` con valori da `0.0` a `1.0`. Le coordinate usano il sistema: **X** = destra, **Y** = alto, **Z** = verso la camera (negativo = lontano dalla camera).

---

## 2. Sezione `world`

Definisce l'ambiente globale della scena.

```yaml
world:
  ambient_light: [0.05, 0.05, 0.08]   # Luce ambiente omnidirezionale
  background:    [0.1, 0.1, 0.15]     # Colore di sfondo (se sky è assente)
  ground:
    type: "infinite_plane"
    material: "pavimento"
    y: 0
  sky:
    type: "gradient"
    # ... vedi sotto
```

| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `ambient_light` | `[R, G, B]` | `[0.05, 0.05, 0.08]` | Luce omnidirezionale di fill che illumina tutte le superfici uniformemente, indipendentemente dalla direzione. |
| `background` | `[R, G, B]` | `[0.5, 0.7, 1.0]` | Colore dei raggi che escono dalla scena senza colpire nulla. Usato solo se `sky` è assente. |
| `ground` | oggetto | — | Piano infinito autogenerato. Richiede un `material` definito nella sezione `materials`. |
| `sky` | oggetto | — | Configurazione del cielo. Se presente, sovrascrive `background`. Vedi sotto. |

### Come funzionano le sorgenti di illuminazione

Il renderer ha più fonti di luce che lavorano insieme:

| Sorgente | Cosa controlla | Effetto |
|----------|---------------|---------|
| `background` / `sky` | Colore del cielo | I raggi che rimbalzano sugli oggetti e "escono" dalla scena raccolgono questo colore. Agisce come una sorgente di luce ambiente globale (Global Illumination). Con `sky: { type: "gradient" }` il colore varia in base alla direzione del raggio. |
| `ambient_light` | Luce piatta di riempimento | Viene **sommata** alla luce diretta su ogni punto colpito. Aiuta a schiarire le ombre. |
| `lights:` | Luci esplicite | Point, Directional, Spot, Area — illuminano selettivamente la scena. |
| Materiali `emissive` | Oggetti luminosi | Emettono luce propria che si propaga tramite rimbalzi indiretti. |

### Esempi di ambienti

**Scena notturna / studio nero (solo luci esplicite):**
```yaml
world:
  ambient_light: [0.0, 0.0, 0.0]
  background: [0.0, 0.0, 0.0]
```

**Scena diurna all'aperto (background piatto legacy):**
```yaml
world:
  ambient_light: [0.05, 0.05, 0.08]
  background: [0.4, 0.6, 1.0]
```

**Scena diurna con gradient sky (raccomandato per outdoor):**
```yaml
world:
  ambient_light: [0.05, 0.05, 0.08]
  sky:
    type: "gradient"
    zenith_color:  [0.10, 0.30, 0.80]
    horizon_color: [0.65, 0.80, 1.00]
    ground_color:  [0.30, 0.28, 0.22]
```

**Atmosfera calda al tramonto:**
```yaml
world:
  ambient_light: [0.05, 0.03, 0.01]
  background: [0.8, 0.4, 0.1]
```

---

### 2.1 Gradient Sky e Sun Disk

Il gradient sky sostituisce il background piatto con un cielo procedurale che varia colore in base alla direzione del raggio. Produce illuminazione globale molto più naturale: la luce dal cielo è azzurra in alto e calda all'orizzonte, colorando le ombre e i rimbalzi in modo realistico.

```yaml
world:
  sky:
    type: "gradient"
    zenith_color:  [0.10, 0.30, 0.80]   # Colore allo zenit (dritto in su) (blu scuro)
    horizon_color: [0.65, 0.80, 1.00]   # Colore all'orizzonte (azzurro chiaro)
    ground_color:  [0.30, 0.25, 0.20]   # Colore sotto l'orizzonte (marrone scuro)
    sun:
      direction:  [-0.5, -0.8, -0.3]    # Verso cui punta il sole (normalizzato)
      color:      [1.0, 0.95, 0.85]
      intensity:  12.0
      size:       2.5                    # Raggio angolare in gradi
      falloff:    40.0                   # Estensione del glow attorno al disco
```

#### Parametri Sky

| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `type` | stringa | — | `"gradient"` per attivare il cielo procedurale. Qualsiasi altro valore (o campo assente) → background piatto legacy. |
| `zenith_color` | `[R, G, B]` | `[0.10, 0.30, 0.80]` | Colore dello zenit (dritto in alto). |
| `horizon_color` | `[R, G, B]` | `[0.70, 0.85, 1.00]` | Colore all'orizzonte. La transizione usa `sqrt(y)` per un'ampia fascia orizzontale realistica. |
| `ground_color` | `[R, G, B]` | `[0.30, 0.25, 0.20]` | Colore sotto l'orizzonte (riflesso del terreno nel cielo). |
| `sun` | oggetto | — | Opzionale: configura un disco solare procedurale con glow. |

#### Parametri Sun Disk

| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `direction` | `[X, Y, Z]` | — | Direzione DA cui arriva la luce solare (stessa convenzione della Directional Light). Viene normalizzata. |
| `color` | `[R, G, B]` | `[1, 1, 1]` | Colore del disco solare. |
| `intensity` | float | `10.0` | Moltiplicatore di luminosità del sole. Valori tipici: 5–50. |
| `size` | float | `3.0` | Diametro angolare del disco in gradi. Sole reale ≈ 0.53°. Valori artistici: 1–6°. |
| `falloff` | float | `32.0` | Esponente del glow attorno al disco. Basso (8) = alone ampio, alto (128) = alone stretto. |

> **Nota:** Usa `background` per scene indoor o da studio (colore piatto). Usa `sky` per scene outdoor (gradiente + sun disk). Non serve specificare entrambi: se `sky` è presente, `background` viene ignorato.

> **Sun disk vs Directional Light:** Il sun disk è puramente **visuale** — è il colore che i raggi ricevono quando escono dalla scena in quella direzione. Per avere illuminazione diretta (ombre, highlight), aggiungi una `directional` light con la stessa `direction` nella sezione `lights:`.

#### Preset Sky per ora del giorno

**Mezzogiorno (sole alto, cielo pulito):**
```yaml
  sky:
    type: "gradient"
    zenith_color:  [0.10, 0.30, 0.80]
    horizon_color: [0.65, 0.80, 1.00]
    ground_color:  [0.30, 0.28, 0.22]
    sun:
      direction: [-0.2, -1.0, -0.3]
      color: [1.0, 0.98, 0.92]
      intensity: 15.0
      size: 2.0
      falloff: 48.0
```

**Golden Hour (sole basso, luce calda):**
```yaml
  sky:
    type: "gradient"
    zenith_color:  [0.15, 0.25, 0.55]
    horizon_color: [0.85, 0.55, 0.25]
    ground_color:  [0.20, 0.15, 0.10]
    sun:
      direction: [-0.8, -0.25, -0.5]
      color: [1.0, 0.85, 0.5]
      intensity: 20.0
      size: 4.0
      falloff: 24.0
```

**Tramonto drammatico:**
```yaml
  sky:
    type: "gradient"
    zenith_color:  [0.08, 0.05, 0.20]
    horizon_color: [0.95, 0.30, 0.05]
    ground_color:  [0.10, 0.05, 0.02]
    sun:
      direction: [-1.0, -0.08, -0.2]
      color: [1.0, 0.4, 0.05]
      intensity: 30.0
      size: 6.0
      falloff: 12.0
```

**Notte serena (senza sole):**
```yaml
  sky:
    type: "gradient"
    zenith_color:  [0.01, 0.01, 0.04]
    horizon_color: [0.04, 0.04, 0.08]
    ground_color:  [0.01, 0.01, 0.02]
```

---

### 2.2 HDRI / IBL (Environment Map)

L'Image-Based Lighting (IBL) usa una fotografia HDR a 360° dell'ambiente reale come sorgente di illuminazione. Ogni raggio che esce dalla scena campiona la mappa HDR, producendo riflessi, rifrazioni e illuminazione globale catturate dalla realtà — il livello più alto di realismo per l'ambiente.

Il motore supporta file in formato **Radiance HDR** (`.hdr`), scaricabili gratuitamente da [Poly Haven](https://polyhaven.com/hdris). File 2K o 4K sono raccomandati.

```yaml
world:
  ambient_light: [0.0, 0.0, 0.0]      # Zero — tutta la luce dall'HDRI
  sky:
    type: "hdri"
    path: "hdri/studio_small_09_4k.hdr" # Relativo al file YAML
    intensity: 1.0                       # Esposizione (>1 più luminoso)
    rotation: 90                         # Rotazione Y in gradi (0–360)
```

#### Parametri HDRI

| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `type` | stringa | — | `"hdri"` per attivare l'environment map. |
| `path` | stringa | — (**obbligatorio**) | Percorso del file `.hdr`. Relativo alla directory del file YAML. |
| `intensity` | float | `1.0` | Moltiplicatore di luminosità. >1 per ambienti scuri, <1 per sovraesposti. |
| `rotation` | float | `0.0` | Rotazione dell'ambiente attorno all'asse Y in gradi. Utile per allineare il sole o la finestra dell'HDRI con la scena. |

> **HDRI vs Gradient Sky vs Background piatto:**
> - **`background`** — colore piatto, per interni chiusi e studi neri.
> - **`sky: { type: "gradient" }`** — cielo procedurale con gradiente e sun disk, per outdoor stilizzati.
> - **`sky: { type: "hdri" }`** — environment map fotografica, per il massimo realismo. Particolarmente efficace con sfere metalliche e vetro.

> **Luci esplicite con HDRI:** L'HDRI fornisce illuminazione globale tramite i rimbalzi del path tracer. Non servono luci esplicite (`lights: []`), ma puoi aggiungerne per enfatizzare ombre direzionali o highlight specifici. Se la sezione `lights:` è completamente omessa dal YAML, il motore aggiunge luci default; usa `lights: []` esplicito per avere solo luce HDRI.

> **Performance:** Il file HDR viene caricato una volta al load della scena. Il sampling durante il render è una singola lettura con bilinear filtering — costo trascurabile.

---

## 3. Sezione `camera`

Controlla il punto di vista, l'inquadratura e l'effetto profondità di campo.

```yaml
camera:
  position: [0, 2, -8]       # Posizione della fotocamera
  look_at: [0, 0, 0]         # Punto verso cui guarda
  vup: [0, 1, 0]             # Vettore "alto" (roll della camera)
  fov: 60                     # Campo visivo verticale (gradi)
  aperture: 0.1               # Apertura lente (0 = tutto a fuoco)
  focal_dist: 8.0             # Distanza di messa a fuoco
```

### Parametri

| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `position` | `[X, Y, Z]` | `[0, 1, -5]` | Dove si trova la camera |
| `look_at` | `[X, Y, Z]` | `[0, 0, 0]` | Punto di mira |
| `vup` | `[X, Y, Z]` | `[0, 1, 0]` | Vettore verso l'alto. Cambialo per inclinare la camera (Dutch angle). |
| `fov` | float | `60` | Campo visivo verticale in gradi. 30°=teleobiettivo, 60°=standard, 90°=grandangolo. |
| `aperture` | float | `0.0` | Diametro dell'apertura della lente. 0.0 = tutto a fuoco (pinhole). Valori > 0 producono depth of field. |
| `focal_dist` | float | `1.0` | Distanza dal piano di fuoco (in unità di scena). |

> **⚠️ Importante — Depth of Field:** Il valore di default `focal_dist: 1.0` è valido solo se `aperture: 0` (tutto a fuoco). Appena `aperture > 0`, il piano di fuoco si trova a 1 unità dalla camera — tipicamente dentro o vicinissimo agli oggetti, producendo bokeh estremo non intenzionale. **Misura la distanza camera→soggetto** e usala come `focal_dist`. Esempio: camera in `[0, 2, -8]`, soggetto in `[0, 1, 0]` → distanza ≈ `8.1` → `focal_dist: 8.1`.

---

## 4. Sezione `materials`

I materiali definiscono come le superfici interagiscono con la luce.

> **Normal mapping:** Qualsiasi materiale (Lambertian, Metal, Dielectric, Emissive) accetta un campo opzionale `normal_map` che aggiunge dettaglio di superficie senza geometria aggiuntiva. Vedi [sezione 5.5](#55-normal-map) per la sintassi completa.

### 4.1 Lambertian (Diffuso/Opaco)
Materiale opaco che diffonde la luce uniformemente in tutte le direzioni.
```yaml
  - id: "rosso_opaco"
    type: "lambertian"
    color: [0.8, 0.2, 0.1]
```

Con texture e normal map:
```yaml
  - id: "muro_mattoni"
    type: "lambertian"
    texture:
      type: "image"
      path: "textures/brick-wall.png"
      uv_scale: [2, 2]
    normal_map:
      path: "textures/brick-wall-normal.png"
      strength: 1.2
      uv_scale: [2, 2]
```

### 4.2 Metal (Metallico/Speculare)
Riflette la luce come uno specchio. Il parametro `fuzz` controlla la rugosità.

```yaml
  - id: "argento"
    type: "metal"
    color: [0.9, 0.9, 0.9]
    fuzz: 0.05
```

Con normal map per graffi e imperfezioni:
```yaml
  - id: "acciaio_graffiato"
    type: "metal"
    color: [0.85, 0.85, 0.88]
    fuzz: 0.03
    normal_map:
      path: "textures/metal-scratched-normal.png"
      strength: 1.5
```

| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `color` | `[R, G, B]` | `[0.5, 0.5, 0.5]` | Colore di riflessione (tint metallico) |
| `fuzz` | float | `0.0` | Rugosità: `0` = specchio perfetto, `1` = molto opaco |

### 4.3 Dielectric (Vetro/Trasparente)
Materiale trasparente con rifrazione e riflesso Fresnel.
```yaml
  - id: "vetro"
    type: "dielectric"
    refraction_index: 1.52
```

| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `refraction_index` | float | `1.5` | Indice di rifrazione (IOR). Vetro comune = 1.52, diamante = 2.42, acqua = 1.33, aria = 1.00029 |
| `color` | `[R, G, B]` | `[1, 1, 1]` | Tint del vetro (es. `[0.8, 1.0, 0.8]` per vetro verde) |

> Il materiale Dielectric supporta `normal_map` per simulare vetro satinato o brocche intagliate.

### 4.4 Emissive (Luminoso)
Materiale auto-luminoso: l'oggetto emette luce propria e brilla nella scena senza bisogno di illuminazione esterna. La luce emessa si propaga tramite i rimbalzi indiretti del path tracer, illuminando naturalmente gli oggetti circostanti.

Usi tipici: neon, LED, insegne, lava, fiamme, sfere magiche, pannelli luminosi, indicatori.

```yaml
  - id: "neon_blu"
    type: "emissive"
    color: [0.2, 0.4, 1.0]
    intensity: 8.0
```

| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `color` | `[R, G, B]` | `[0.5, 0.5, 0.5]` | Colore della luce emessa |
| `intensity` | float | `1.0` | Moltiplicatore di luminosità. La radiance emessa è `color × intensity`. |
| `texture` | oggetto | — | Opzionale: texture procedurale per emissione non uniforme (es. lava con texture marble) |

> **Comportamento nel path tracer:**
> - L'emissione è **additiva** e non dipende dall'illuminazione esterna: l'oggetto è visibile anche in una scena completamente buia.
> - L'emissione avviene solo dalla **front face** — il retro della superficie è buio, come un vero pannello LED.
> - Gli oggetti emissivi **non scatterano** raggi: non hanno componente diffusa né speculare. Tutta la loro energia va in emissione.
> - L'illuminazione indiretta funziona naturalmente: un neon magenta colora di rosa le pareti vicine tramite i rimbalzi del path tracer. Usa campioni alti (`-s 64+`) per risultati puliti.

#### Calibrazione dell'intensità emissiva

| Effetto desiderato | Range `intensity` | Note |
|--------------------|-------------------|------|
| Glow tenue (indicatore, LED spento) | 0.5 – 2 | Appena visibile, non illumina la scena |
| Neon / LED visibile | 3 – 10 | L'oggetto brilla e colora leggermente i dintorni |
| Pannello luminoso (sorgente primaria) | 10 – 25 | Illumina la scena come una area light |
| Lava / plasma (over-bright) | 25 – 100 | Effetto bloom, satura il tone mapping ACES |

> **💡 Tip: Emissive con texture procedurale.** Puoi usare una texture `marble` o `noise` su un materiale emissivo per creare effetti lava, plasma o pattern luminosi non uniformi:
> ```yaml
>   - id: "lava"
>     type: "emissive"
>     intensity: 15.0
>     texture:
>       type: "marble"
>       scale: 3.0
>       noise_strength: 6.0
>       colors: [[1.0, 0.3, 0.0], [1.0, 0.8, 0.0]]
> ```

> **💡 Tip: Emissive vs Area Light.** Un `quad` con materiale `emissive` è visualmente simile a un'area light, ma con differenze importanti:
> - L'**area light** usa Next Event Estimation (NEE) e produce ombre morbide controllate con `shadow_samples`.
> - L'**emissive** illumina solo tramite rimbalzi indiretti del path tracer — richiede più campioni (`-s`) per convergere, ma l'oggetto è fisicamente **visibile** nella scena (puoi vederlo, rifletterlo nello specchio, rifrangerlo nel vetro).
> - Per pannelli a soffitto che devono essere visti: usa `emissive`. Per illuminazione pura senza geometria visibile: usa `area` light.

---

### 4.5 Disney Principled BSDF (PBR Unificato)

Il materiale più potente del renderer. Un singolo tipo può rappresentare qualsiasi superficie reale attraverso la combinazione di più lobi fisici. Ispirato al modello di Brent Burley (*"Physically Based Shading at Disney"*, SIGGRAPH 2012).

**Dichiarazione minima:**
```yaml
- id: "plastica"
  type: "disney"
  color: [0.8, 0.2, 0.1]
  roughness: 0.4
```

**Alias YAML validi:** `"disney"`, `"disney_bsdf"`, `"pbr"` (tutti equivalenti).

**Parametri completi:**

| Parametro | Range | Default | Descrizione |
|-----------|-------|---------|-------------|
| `color` | `[R,G,B]` | — | Colore base (albedo diffuso o colore metallico) |
| `metallic` | 0–1 | `0` | 0 = dielettrico (plastica, legno, pelle); 1 = metallo (oro, cromo) |
| `roughness` | 0–1 | `0.5` | 0 = superficie a specchio; 1 = perfettamente diffuso |
| `subsurface` | 0–1 | `0` | Approssimazione SSS: 0 = Lambert, 1 = effetto cera/pelle |
| `specular` | 0–2 | `0.5` | Intensità del lobe speculare dielettrico (controlla F0) |
| `specular_tint` | 0–1 | `0` | Tinta lo specular verso `color`. 0 = bianco, 1 = tinta completa |
| `sheen` | 0–1 | `0` | Lucentezza a radente (tessuti, velluto) |
| `sheen_tint` | 0–1 | `0.5` | Tinta lo sheen verso `color` |
| `clearcoat` | 0–1 | `0` | Secondo lobe speculare (vernice auto, lacca) |
| `clearcoat_gloss` | 0–1 | `1` | Lucidità del clearcoat: 1 = a specchio, 0 = satinato |
| `spec_trans` | 0–1 | `0` | Trasmissione speculare: 0 = opaco, 1 = vetro |
| `ior` | ≥1.0 | `1.5` | Indice di rifrazione per trasmissione e specular dielettrico |

**Texture** e **normal_map** sono supportati esattamente come negli altri materiali.

**Esempi per tipo di superficie:**

```yaml
# Plastica opaca
- id: "plastica_rossa"
  type: "disney"
  color: [0.8, 0.1, 0.1]
  roughness: 0.8
  metallic: 0.0

# Oro
- id: "oro"
  type: "disney"
  color: [1.0, 0.71, 0.29]
  metallic: 1.0
  roughness: 0.15

# Cromo a specchio
- id: "cromo"
  type: "disney"
  color: [0.95, 0.93, 0.88]
  metallic: 1.0
  roughness: 0.02

# Vernice auto (rosso con clearcoat)
- id: "vernice_auto"
  type: "disney"
  color: [0.7, 0.05, 0.05]
  roughness: 0.3
  clearcoat: 1.0
  clearcoat_gloss: 0.9

# Velluto
- id: "velluto_blu"
  type: "disney"
  color: [0.05, 0.1, 0.5]
  roughness: 0.9
  sheen: 1.0
  sheen_tint: 0.8

# Pelle / cera (subsurface scattering)
- id: "pelle"
  type: "disney"
  color: [0.85, 0.6, 0.45]
  roughness: 0.6
  subsurface: 0.4
  specular: 0.2

# Vetro trasparente
- id: "vetro"
  type: "disney"
  color: [1.0, 1.0, 1.0]
  roughness: 0.0
  spec_trans: 1.0
  ior: 1.5
```

**Quando usare Disney vs tipi legacy:**

| Vuoi... | Usa |
|---------|-----|
| Solo colore diffuso semplice | `lambertian` |
| Riflessione speculare semplice | `metal` |
| Vetro o rifrazione base | `dielectric` |
| Fonte di luce | `emissive` |
| Qualsiasi altro materiale reale (metallo, vernice, tessuto, pelle, vetro PBR) | `disney` |

> **💡 Tip performance:** Il Disney BSDF è computazionalmente più costoso dei materiali legacy (specialmente con `clearcoat` e `spec_trans` attivi). Per scene con molti oggetti, usa `lambertian` per le superfici di sfondo e `disney` solo per i materiali protagonisti.

---

## 5. Sezione `textures`

Le texture procedurali vengono definite all'interno del materiale.

### 5.1 Tipi di Texture Procedurali

**Checker (Scacchiera 3D):**
```yaml
    texture:
      type: "checker"
      scale: 4.0
      colors: [[0.9, 0.9, 0.9], [0.1, 0.1, 0.1]]
```

**Noise (Rumore Perlin):**
```yaml
    texture:
      type: "noise"
      scale: 5.0
      noise_strength: 3.0   # 0 = Perlin liscio, > 0 = turbolento (default: 0)
```

**Marble (Marmo):**
```yaml
    texture:
      type: "marble"
      scale: 10.0
      noise_strength: 8.0
      colors: [[0.95, 0.95, 0.95], [0.4, 0.4, 0.4]]
```

**Wood (Legno):**
```yaml
    texture:
      type: "wood"
      scale: 3.0
      noise_strength: 2.0
      colors: [[0.85, 0.65, 0.4], [0.6, 0.4, 0.2]]
```

### 5.2 Trasformazioni Spaziali (Offset & Rotation)

Tutte le texture procedurali supportano offset e rotazione per controllarne l'orientamento nello spazio 3D:

```yaml
    texture:
      type: "marble"
      scale: 10.0
      offset: [5.0, 0.0, 3.0]       # Traslazione della texture
      rotation: [0.0, 45.0, 0.0]     # Rotazione in gradi (X, Y, Z)
```

### 5.3 Randomizzazione per Oggetto

Per far apparire ogni oggetto unico anche con lo stesso materiale:

- **`randomize_offset: true`**: aggiunge un offset pseudo-casuale diverso per ogni oggetto.
- **`randomize_rotation: true`**: ruota la texture in modo pseudo-casuale per ogni oggetto.

**Esempio: Sfere di marmo tutte diverse con un unico materiale**
```yaml
materials:
  - id: "marmo_variegato"
    type: "metal"
    fuzz: 0.04
    texture:
      type: "marble"
      scale: 10.0
      randomize_offset: true
      randomize_rotation: true
```

### 5.4 Image Texture (Texture da File)

Carica un'immagine da file e la proietta sulla superficie usando le coordinate UV della primitiva. Supporta tutti i formati gestiti da ImageSharp: PNG, JPEG, BMP, GIF, TIFF, WebP.

```yaml
    texture:
      type: "image"
      path: "textures/brick_wall.png"    # Relativo al file YAML
      uv_scale: [2, 1]                   # Tiling: 2× in U, 1× in V
```

| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `type` | stringa | — | `"image"` |
| `path` | stringa | — (**obbligatorio**) | Percorso del file immagine. Relativo alla directory del file YAML della scena. |
| `uv_scale` | `[U, V]` | `[1, 1]` | Fattore di tiling su ciascun asse UV. `[3, 3]` = la texture si ripete 3 volte su ogni asse. Se si specifica un solo valore `[2]`, viene usato per entrambi gli assi. |

> **Conversione sRGB → lineare:** Le immagini vengono convertite automaticamente dallo spazio sRGB allo spazio lineare tramite `pow(channel, 2.2)` al caricamento. Questo è necessario per un rendering fisicamente corretto — il tone mapping ACES lavora in spazio lineare.

> **Bilinear filtering:** Le coordinate UV continue vengono interpolate tra i 4 pixel circostanti, producendo bordi smooth anche con texture a risoluzione moderata.

> **Tiling:** I valori UV fuori dall'intervallo [0, 1] vengono wrappati tramite `frac()` per una ripetizione seamless.

> **Fallback magenta:** Se il file non viene trovato o non può essere caricato, il motore stampa un warning in console e usa un colore magenta vivace — facile da individuare nel render per capire dove manca un file.

**Esempio: Pavimento in legno con tiling 4×4**
```yaml
materials:
  - id: "parquet"
    type: "lambertian"
    texture:
      type: "image"
      path: "textures/wood_floor.png"
      uv_scale: [4, 4]
```

**Esempio: Sfera con mappa terrestre**
```yaml
materials:
  - id: "terra"
    type: "lambertian"
    texture:
      type: "image"
      path: "textures/earth.png"

entities:
  - { name: "globo", type: "sphere", center: [0, 1, 0], radius: 1, material: "terra" }
```

**Esempio: Metallo texturato (riflesso + pattern)**
```yaml
materials:
  - id: "acciaio_graffiato"
    type: "metal"
    fuzz: 0.25
    texture:
      type: "image"
      path: "textures/metal_scratched.png"
```

> **💡 Tip: Generare texture di test.** Il progetto include un tool `TextureGen` che genera texture procedurali pronte all'uso (mattoni, legno, terra, griglie UV). Eseguilo con `dotnet run --project src/Tools/TextureGen/TextureGen.csproj`.

---

### 5.5 Normal Map

Il normal mapping perturba la normale di shading pixel per pixel usando un'immagine RGB, simulando dettaglio geometrico (fughe, graffi, rilievi, trame) senza aggiungere triangoli alla scena. L'effetto influenza tutto il calcolo di illuminazione: diffuso N·L, speculare N·H, direzione di scatter e shadow ray origin.

```yaml
materials:
  - id: "muro_mattoni"
    type: "lambertian"
    texture:
      type: "image"
      path: "textures/brick-wall.png"
      uv_scale: [2, 2]
    normal_map:
      path: "textures/brick-wall-normal.png"
      strength: 1.0
      uv_scale: [2, 2]
```

#### Parametri

| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `path` | stringa | — (**obbligatorio**) | Percorso del file normal map. Relativo alla directory del file YAML. Se il file non esiste, il motore stampa un warning e continua senza normal map (superficie liscia). |
| `strength` | float | `1.0` | Intensità della perturbazione. `0.0` = nessun effetto, `1.0` = effetto pieno, `2.0` = esagerato. Valore massimo: `3.0`. |
| `uv_scale` | `[U, V]` | `[1, 1]` | Tiling UV. **Deve corrispondere al `uv_scale` della texture albedo** per evitare disallineamenti tra colore e bump. |
| `flip_y` | bool | `false` | Inverte il canale verde (G). Imposta `true` per mappe DirectX-style (usate da alcuni tool come Substance Painter in modalità DirectX). Le mappe OpenGL-style (default, es. da Blender, AmbientCG, Poly Haven) non richiedono inversione. |

#### Formato delle Normal Map

Le normal map sono immagini RGB dove ogni canale codifica un asse del vettore normale nel tangent space:

| Canale | Asse | Colore neutro (128) | Significato |
|--------|------|----------------------|-------------|
| **R** (Rosso) | X | Grigio medio | Inclinazione sinistra/destra |
| **G** (Verde) | Y | Grigio medio | Inclinazione su/giù |
| **B** (Blu) | Z | Quasi bianco (255) | Profondità (verso l'esterno) |

Una normale piatta (nessuna perturbazione) corrisponde al colore RGB `(128, 128, 255)` — la tipica tinta violetto-azzurra delle normal map.

> **Le normal map NON vengono corrette in gamma.** A differenza delle texture albedo che vengono convertite sRGB→lineare, le normal map contengono dati direzionali e vengono lette come valori lineari. Non applicare correzione gamma manualmente.

#### Dove trovare Normal Map

Normal map gratuite e CC0 (libere da royalty):
- [ambientcg.com](https://ambientcg.com) — set PBR completi con albedo + normal + roughness
- [polyhaven.com/textures](https://polyhaven.com/textures) — alta qualità, set 4K con tutti i canali PBR
- [3dtextures.me](https://3dtextures.me) — vasta libreria CC0

#### Generare Normal Map di Test con NormalMapGen

Il progetto include il tool `NormalMapGen` che genera normal map procedurali abbinate alle texture di `TextureGen`:

```powershell
cd 3d-ray
dotnet run --project src/Tools/NormalMapGen/NormalMapGen.csproj
```

Normal map generate nella cartella `scenes/textures/`:

| File | Contenuto |
|------|-----------|
| `brick-wall-normal.png` | Mattoni: fughe incavate con bevel, superficie ruvida |
| `wood-floor-normal.png` | Parquet: doghe, fughe, venature |
| `wood-planks-normal.png` | Assi larghe: giunture, nodi, grana grossa |
| `concrete-normal.png` | Cemento: pori e ondulazioni multi-frequenza |
| `metal-scratched-normal.png` | Metallo: graffi lineari casuali |
| `stone-cobble-normal.png` | Ciottoli: forma Voronoi irregolare |
| `fabric-weave-normal.png` | Tessuto: trama intrecciata |
| `tiles-normal.png` | Piastrelle: fughe con bevel |
| `flat-normal.png` | Piatta `(128,128,255)` — test di riferimento (nessuna perturbazione) |

#### Compatibilità con le Primitive

Il normal mapping funziona su tutte le primitive. Il frame TBN (Tangent, Bitangent, Normal) viene calcolato internamente da ogni primitiva in base al suo UV mapping nativo, trasformato correttamente nel caso di oggetti con `scale`, `rotate`, `translate`.

| Primitiva | UV mapping | Note |
|-----------|------------|------|
| Sphere | Sferico (lon/lat) | TBN allineato alle direzioni di phi e theta |
| Quad | Baricentric (alpha, beta) | T lungo U, B lungo V — identico all'UV |
| Box | Planare per faccia | T e B allineati agli assi della faccia |
| Cylinder (corpo) | Cilindrico (theta, altezza) | T tangenziale, B verticale |
| Cylinder (caps) | Planare | T lungo X, B lungo Z |
| Disk | Planare locale | T e B calcolati dalla normale del disco |
| Infinite Plane | Planare tiled | T e B da base ortonormale locale |
| Triangle | Baricentric | T lungo edge V0→V1, B lungo V0→V2 |

#### Esempi

**Muro di mattoni con rilievo pronunciato:**
```yaml
  - id: "mattoni_rilievo"
    type: "lambertian"
    texture:
      type: "image"
      path: "textures/brick-wall.png"
      uv_scale: [3, 2]
    normal_map:
      path: "textures/brick-wall-normal.png"
      strength: 1.5
      uv_scale: [3, 2]            # stesso uv_scale della texture albedo
```

**Metallo graffiato con riflesso:**
```yaml
  - id: "acciaio"
    type: "metal"
    color: [0.88, 0.88, 0.90]
    fuzz: 0.02
    normal_map:
      path: "textures/metal-scratched-normal.png"
      strength: 1.2
```

**Vetro satinato (normal map su dielectric):**
```yaml
  - id: "vetro_satinato"
    type: "dielectric"
    refraction_index: 1.52
    normal_map:
      path: "textures/concrete-normal.png"
      strength: 0.4              # bassa intensità per effetto satinato sottile
```

**Materiale con mappa DirectX-style** (canale verde invertito):
```yaml
  - id: "pietra"
    type: "lambertian"
    color: [0.6, 0.55, 0.5]
    normal_map:
      path: "textures/stone-directx-normal.png"
      strength: 1.0
      flip_y: true               # inverte il canale G per mappe DirectX
```

> **💡 Consigli pratici:**
> - Mantieni sempre `uv_scale` identico tra `texture` e `normal_map` per evitare disallineamento visibile tra colore e bump.
> - Per una luce radente (quasi parallela alla superficie), l'effetto del normal mapping è massimo e le fughe/rilievi diventano molto evidenti. Per una luce frontale, l'effetto è più sottile.
> - Il file `flat-normal.png` generato da NormalMapGen (solo `(128,128,255)`) è il test ideale: applicarlo non deve cambiare nulla nel render — verificabile visivamente.
> - Usa `strength: 2.0`–`3.0` solo per test o effetti volutamente esagerati. Per materiali realistici, `0.8`–`1.5` dà risultati più credibili.

---

## 6. Sezione `entities`

Gli oggetti 3D nella scena. Ogni entità ha un `name`, un `type`, parametri specifici per la geometria e un `material` (riferimento all'id del materiale).

### 6.1 Sphere (Sfera)
```yaml
  - name: "sfera_principale"
    type: "sphere"
    center: [0, 1, 0]
    radius: 1.0
    material: "marmo_bianco"
```
| Campo | Tipo | Descrizione |
|-------|------|-------------|
| `center` | `[X, Y, Z]` | Centro della sfera nel mondo |
| `radius` | float | Raggio |

### 6.2 Box (Cubo/Parallelepipedo)

Il Box è definito come un **cubo unitario** centrato nell'origine (da -0.5 a 0.5 su tutti gli assi). Viene poi posizionato e dimensionato tramite le trasformazioni `scale` e `translate`.

```yaml
  - name: "piedistallo"
    type: "box"
    scale: [2.0, 0.5, 2.0]         # Larghezza 2, altezza 0.5, profondità 2
    translate: [0.0, 0.25, 0.0]     # Posizionato con la base a Y=0
    material: "marmo_base"
```
| Campo | Tipo | Descrizione |
|-------|------|-------------|
| `scale` | `[X, Y, Z]` | Dimensioni del box (larghezza, altezza, profondità) |
| `translate` | `[X, Y, Z]` | Posizione del **centro** del box nel mondo |
| `rotate` | `[X, Y, Z]` | Rotazione in gradi (opzionale) |

#### Sintassi alternativa: `min`/`max` (coordinate assolute)

In alternativa a `scale`+`translate`, puoi specificare direttamente gli angoli del box:

```yaml
  - name: "cassa"
    type: "box"
    min: [-1, 0, -2]
    max: [1, 1.5, 2]
    material: "legno"
```

| Campo | Tipo | Descrizione |
|-------|------|-------------|
| `min` | `[X, Y, Z]` | Angolo minimo del box (coordinata assoluta) |
| `max` | `[X, Y, Z]` | Angolo massimo del box (coordinata assoluta) |

> **Nota:** `min`/`max` e `scale`/`translate` sono mutualmente esclusivi per definire forma e posizione. Puoi comunque aggiungere `rotate` a un box definito con `min`/`max` — la rotazione viene applicata sopra.

> **⚠️ Importante:** Il `translate` posiziona il **centro** del box. Se vuoi che la base sia a Y=0, traslaci di `altezza / 2` in Y. Esempio: box alto 1.0 con base a terra → `translate: [0, 0.5, 0]`.

### 6.3 Cylinder (Cilindro)
Cilindro finito allineato all'asse Y, con dischi di chiusura (caps) in alto e in basso.
```yaml
  - name: "colonna"
    type: "cylinder"
    center: [0, 0, 0]        # Centro della base inferiore
    radius: 0.4
    height: 3.0
    material: "marmo"
```
| Campo | Tipo | Descrizione |
|-------|------|-------------|
| `center` | `[X, Y, Z]` | Centro della **base inferiore** del cilindro |
| `radius` | float | Raggio del cilindro |
| `height` | float | Altezza (estensione verso +Y dal center) |

### 6.4 Triangle (Triangolo)
Triangolo definito da tre vertici. Usa l'algoritmo Möller–Trumbore per l'intersezione.
```yaml
  - name: "triangolo"
    type: "triangle"
    v0: [0, 0, 0]
    v1: [1, 0, 0]
    v2: [0.5, 1, 0]
    material: "rosso"
```
| Campo | Tipo | Descrizione |
|-------|------|-------------|
| `v0` | `[X, Y, Z]` | Primo vertice |
| `v1` | `[X, Y, Z]` | Secondo vertice |
| `v2` | `[X, Y, Z]` | Terzo vertice |

### 6.5 Quad (Quadrilatero)
Un parallelogramma definito da un punto d'origine Q e due vettori U e V che definiscono i lati.
```yaml
  - name: "parete"
    type: "quad"
    q: [-5, 0, 5]
    u: [10, 0, 0]
    v: [0, 5, 0]
    material: "muro"
```
| Campo | Tipo | Descrizione |
|-------|------|-------------|
| `q` | `[X, Y, Z]` | Punto d'origine del quad |
| `u` | `[X, Y, Z]` | Primo vettore lato |
| `v` | `[X, Y, Z]` | Secondo vettore lato |

### 6.6 Disk (Disco)
Disco piatto con centro, normale e raggio.
```yaml
  - name: "disco"
    type: "disk"
    center: [0, 0, 0]
    normal: [0, 1, 0]
    radius: 2.0
    material: "metallo"
```

### 6.7 Plane / Infinite Plane (Piano Infinito)
Piano infinito utile per pavimenti o sfondi.
```yaml
  - name: "pavimento"
    type: "infinite_plane"
    point: [0, 0, 0]
    normal: [0, 1, 0]
    material: "scacchiera"
```

### 6.8 Trasformazioni (Translate, Rotate, Scale)

Qualsiasi entità supporta trasformazioni opzionali:

```yaml
  - name: "cubo_ruotato"
    type: "box"
    material: "legno"
    scale:     [1.0, 2.0, 1.0]    # Prima scala
    rotate:    [0, 45, 0]          # Poi ruota (gradi attorno agli assi X, Y, Z)
    translate: [2, 1, 0]           # Poi trasla
```

Le trasformazioni vengono applicate nell'ordine: **Scale → Rotate → Translate**.

### 6.9 Parametro Seed

Il parametro `seed` controlla la randomizzazione delle texture procedurali per ogni oggetto. Specificarlo rende il risultato **riproducibile** tra render successivi:

```yaml
  - name: "sfera_marmo"
    type: "sphere"
    center: [0, 1, 0]
    radius: 1.0
    material: "marmo_variegato"
    seed: 42    # Seed fisso = texture identica
```

Se `seed` è omesso, viene generato un valore casuale ogni volta che la scena viene caricata — le venature cambiano tra render successivi.

---

## 7. Sezione `lights`

Le luci esplicite della scena. Puoi combinare più tipi di luce per ottenere l'effetto desiderato.

### 7.1 Point Light (Puntiforme)
Luce omnidirezionale da un singolo punto.
```yaml
  - type: "point"
    position: [2, 5, -3]
    color: [1.0, 0.95, 0.85]
    intensity: 20.0
```

| Campo | Default | Descrizione |
|-------|---------|-------------|
| `position` | `[0, 10, 0]` | Posizione della sorgente nello spazio |
| `color` | `[1, 1, 1]` | Colore della luce |
| `intensity` | `1.0` | Intensità (attenuazione quadratica con la distanza). Valori tipici: 4–30. |

### 7.2 Directional Light (Sole)

```yaml
  - type: "directional"
    direction: [-0.5, -1.0, -0.3]
    color: [1.0, 0.98, 0.92]
    intensity: 0.8
```

| Campo | Default | Descrizione |
|-------|---------|-------------|
| `direction` | `[-1, -1, -1]` | Direzione **verso cui punta** la luce (non la sorgente). Viene normalizzata internamente. |
| `color` | `[1, 1, 1]` | Colore della luce |
| `intensity` | `1.0` | Intensità. Senza attenuazione con la distanza — valori tipici: 0.05–0.15. |

> **Alias:** Puoi usare anche `type: "sun"` come alias per `"directional"`.

> **💡 Tip: Allinea Directional Light e Sun Disk.** Se usi un gradient sky con sun disk, imposta la stessa `direction` sulla directional light per coerenza visiva: il sole visibile nel cielo e l'illuminazione diretta arrivano dalla stessa parte.

### 7.3 Spot Light (Faretto)
Luce conica con posizione e direzione. Ha un cono interno (piena intensità) e un cono esterno (sfumatura smooth). L'attenuazione angolare usa un'interpolazione quadratica tra i due coni.
```yaml
  - type: "spot"
    position: [0, 5, 0]
    direction: [0, -1, 0]    # Punta verso il basso
    color: [1.0, 0.9, 0.7]
    intensity: 40.0
    inner_angle: 15
    outer_angle: 30
```

| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `position` | `[X, Y, Z]` | `[0, 10, 0]` | Posizione del faretto |
| `direction` | `[X, Y, Z]` | `[0, -1, 0]` | Direzione verso cui punta il cono |
| `color` | `[R, G, B]` | `[1, 1, 1]` | Colore della luce |
| `intensity` | float | `1.0` | Intensità. Valori tipici: 6–30. |
| `inner_angle` | float | `15` | Angolo del cono interno (luce piena), in gradi |
| `outer_angle` | float | `30` | Angolo del cono esterno (fade out), in gradi |

> **Alias:** Puoi usare anche `type: "spotlight"`.

### 7.4 Area Light (Emettitore Rettangolare)

Sorgente luminosa rettangolare che produce **ombre morbide** fisicamente corrette con gradiente di penombra. Definita da un angolo (`corner`) e due vettori che formano il rettangolo.

Il motore usa campionamento Monte Carlo: per ogni punto della scena vengono sparati `shadow_samples` raggi verso punti casuali sulla superficie della luce, e il risultato è la media. Più shadow samples = penombra più morbida e meno rumorosa.

```yaml
  - type: "area"
    corner: [-1.5, 4.99, -1.5]    # Un angolo del rettangolo
    u: [3.0, 0.0, 0.0]            # Primo lato (larghezza: 3 unità in X)
    v: [0.0, 0.0, 3.0]            # Secondo lato (profondità: 3 unità in Z)
    color: [1.0, 0.97, 0.9]
    intensity: 35.0
    shadow_samples: 16
```

| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `corner` | `[X, Y, Z]` | — (**obbligatorio**) | Un angolo del rettangolo luminoso |
| `u` | `[X, Y, Z]` | — (**obbligatorio**) | Primo vettore lato del rettangolo |
| `v` | `[X, Y, Z]` | — (**obbligatorio**) | Secondo vettore lato del rettangolo |
| `color` | `[R, G, B]` | `[1, 1, 1]` | Colore emesso |
| `intensity` | float | `1.0` | Intensità. Valori tipici: 15–60. |
| `shadow_samples` | int | `16` | Raggi ombra per punto (default per-luce). Sovrascrivibile da CLI con `-S`. |

> **Alias:** Puoi usare anche `type: "area_light"`, `type: "rect"` o `type: "rect_light"`.

> **💡 Override da CLI:** Il parametro `--shadow-samples` (`-S`) da riga di comando sovrascrive il valore `shadow_samples` di **tutte** le area light nella scena. Questo permette di iterare sulla qualità senza modificare il file YAML.

> **⚠️ Costo computazionale:** Il `shadow_samples` ha un impatto diretto sul tempo di render. Con `-s 128` campioni pixel e `-S 16`, ogni pixel lancia `128 × 16 = 2048` raggi ombra per questa sola luce.

**Esempio: Pannello luminoso da soffitto**
```yaml
  - type: "area"
    corner: [-1.5, 4.99, -1.5]
    u: [3.0, 0.0, 0.0]
    v: [0.0, 0.0, 3.0]
    color: [1.0, 0.97, 0.9]
    intensity: 35.0
    shadow_samples: 16
```

---

### 7.5 — Calibrazione dell'Intensità

> 💡 **Nota sui valori tipici:** I range indicati nelle tabelle dei paragrafi 7.1–7.4 sono stati calibrati empiricamente su scene reali. Se l'immagine risulta sovraesposta o sottoesposta, scala **tutte** le intensità in modo uniforme mantenendo i rapporti tra le sorgenti.

#### Valori di riferimento per tipo di luce

| Tipo luce | Range consigliato | Note |
|-----------|-------------------|------|
| `point` generica | 4 – 30 | Scala con il quadrato della distanza: raddoppiare la distanza richiede ×4 l'intensità |
| `spot` key light | 15 – 30 | Valori più alti per coni stretti (`inner_angle` < 15°) |
| `spot` fill / rim | 5 – 15 | Tipicamente 1/3 – 1/2 della key |
| `point` accent / bounce | 0.5 – 2 | Luci di dettaglio, quasi invisibili da sole |
| `directional` fill / multi-luce | 0.05 – 0.15 | Sorgente secondaria in scene con più luci |
| `directional` luce principale | 0.3 – 2.0 | Come unica luce outdoor (tramonto, luna): valori più alti compensano l'assenza di altre sorgenti |
| `area` pannello | 20 – 60 | Dipende dall'area del rettangolo e dalla distanza dalla scena |

#### Workflow di calibrazione

1. Aggiungi le luci con i valori centrali del range.
2. Esegui un preview rapido (`-s 1 -w 400 -S 4`).
3. Se l'immagine è sovraesposta, **dimezza tutte le intensità** e ripeti.
4. Se è sottoesposta, **raddoppiale** e ripeti.
5. Quando l'esposizione globale è corretta, bilancia le singole sorgenti tra loro.
6. Tieni nota dei valori finali: potrai riusarli come punto di partenza per scene simili.

---

## 8. Illuminazione: Come Funziona

### Il rendering è un path tracer
Ogni pixel spara raggi nella scena. Quando un raggio colpisce una superficie, il materiale genera un raggio rimbalzato. Il processo continua fino a:
- Il raggio esce dalla scena → riceve il colore del cielo (`background` piatto oppure `sky` gradiente)
- Il raggio raggiunge la profondità massima (`--depth`) → restituisce nero

Questo significa che il cielo è effettivamente una **sorgente di luce**. Se vuoi una scena dove solo le luci esplicite illuminano, devi impostare `background: [0, 0, 0]` (e non definire `sky`).

Con il **gradient sky**, il colore del cielo varia in base alla direzione del raggio: azzurro dallo zenit, caldo dall'orizzonte. Questo produce un'illuminazione globale molto più ricca e naturale rispetto al background piatto — le ombre hanno una tinta azzurra (luce dal cielo) e le superfici rivolte verso l'orizzonte ricevono luce più calda.

### Ordine di calcolo per ogni hit

Quando un raggio colpisce una superficie, il renderer esegue questi passi nell'ordine:

1. **Normal map** — se il materiale ha una `normal_map` e la primitiva ha un frame TBN valido, la normale di shading viene perturbata prima di qualsiasi altra operazione.
2. **Emissione** — la superficie aggiunge la propria radiance emessa.
3. **Direct lighting (NEE)** — per ogni luce nella scena, viene calcolato il contributo diffuso (N·L) e speculare (N·H) usando la normale perturbata.
4. **Scatter (indirect)** — il materiale genera un raggio secondario nella direzione scatter, che porta la normale perturbata nel bounce successivo.

Il risultato è che il normal mapping influenza l'intera pipeline: ombre più profonde nelle fughe, highlight speculari spostati, riflessi nei materiali metal perturbati correttamente.

### Combinazioni di Luci Consigliate

**Esterno Diurno (background piatto legacy):**
```yaml
lights:
  - type: "directional"
    direction: [-0.5, -1, -0.3]
    color: [1.0, 0.95, 0.85]
    intensity: 0.09
  - type: "point"
    position: [0, 20, 0]
    intensity: 10
```

**Esterno con Gradient Sky (raccomandato):**
```yaml
world:
  ambient_light: [0.04, 0.04, 0.06]
  sky:
    type: "gradient"
    zenith_color:  [0.10, 0.30, 0.80]
    horizon_color: [0.65, 0.80, 1.00]
    ground_color:  [0.30, 0.25, 0.20]
    sun:
      direction: [-0.5, -0.8, -0.3]
      color: [1.0, 0.95, 0.85]
      intensity: 15.0
      size: 2.5
      falloff: 40.0

lights:
  - type: "directional"
    direction: [-0.5, -0.8, -0.3]     # Stessa direzione del sun disk!
    color: [1.0, 0.95, 0.85]
    intensity: 0.09
```

**Studio con Area Light (ombre morbide):**
```yaml
lights:
  - type: "area"
    corner: [-2.0, 4.9, -2.0]
    u: [4.0, 0.0, 0.0]
    v: [0.0, 0.0, 4.0]
    color: [1.0, 0.97, 0.9]
    intensity: 40.0
    shadow_samples: 16
  - type: "point"
    position: [-5, 3, -3]
    color: [0.8, 0.85, 1.0]
    intensity: 3
```

**Studio con Spot:**
```yaml
lights:
  - type: "spot"
    position: [0, 8, -5]
    direction: [0, -1, 0.5]
    color: [1.0, 0.95, 0.9]
    intensity: 15
    inner_angle: 20
    outer_angle: 40
```

**Interno Intimo:**
```yaml
lights:
  - type: "point"
    position: [0, 3, 0]
    color: [1.0, 0.8, 0.5]
    intensity: 4
```

**Scena illuminata solo da oggetti emissivi (Neon Lab):**
```yaml
world:
  ambient_light: [0.005, 0.005, 0.008]
  background: [0.0, 0.0, 0.0]

# Nessuna luce esplicita — la scena è illuminata dagli emissivi
lights: []

materials:
  - id: "neon_ciano"
    type: "emissive"
    color: [0.05, 0.85, 1.0]
    intensity: 8.0

entities:
  - { name: "neon", type: "sphere", center: [0, 2, 0], radius: 0.5, material: "neon_ciano" }
```

> **💡 Nota:** Quando la scena è illuminata solo da materiali emissivi, non ci sono luci per il Next Event Estimation. Tutta l'illuminazione arriva dai rimbalzi indiretti. Usa campioni alti (`-s 128+`) e profondità adeguata (`-d 10+`) per risultati puliti. Puoi aggiungere una `point` light con intensità molto bassa (0.2–1.0) come fill minimale per evitare ombre completamente nere.

---

## 9. Esempi Completi

### Scena Minima

```yaml
world:
  ambient_light: [0.1, 0.1, 0.1]
  background: [0.5, 0.7, 1.0]

camera:
  position: [0, 1, -4]
  look_at: [0, 0, 0]
  fov: 60

materials:
  - id: "rosso"
    type: "lambertian"
    color: [0.8, 0.2, 0.1]

entities:
  - name: "sfera"
    type: "sphere"
    center: [0, 0, 0]
    radius: 1
    material: "rosso"

lights:
  - type: "point"
    position: [3, 5, -3]
    color: [1, 1, 1]
    intensity: 20
```

### Showcase Materiali (Confronto)

Tre sfere affiancate che mostrano i tre comportamenti fisici principali: Diffuso, Metallico e Vetro.

```yaml
world:
  ambient_light: [0.08, 0.08, 0.1]
  background: [0.3, 0.5, 0.9]
  ground: { type: "infinite_plane", material: "pavimento", y: 0 }

camera:
  position: [0, 1.5, -6]
  look_at: [0, 0.5, 0]
  fov: 50

materials:
  - id: "pavimento"
    type: "lambertian"
    color: [0.5, 0.5, 0.5]
  - id: "diffuso"
    type: "lambertian"
    color: [0.8, 0.3, 0.2]
  - id: "metallico"
    type: "metal"
    color: [0.9, 0.9, 0.9]
    fuzz: 0.05
  - id: "vetro"
    type: "dielectric"
    refraction_index: 1.5

entities:
  - { name: "mat_sx", type: "sphere", center: [-2.5, 1, 0], radius: 1, material: "diffuso" }
  - { name: "mat_centro", type: "sphere", center: [0, 1, 0], radius: 1, material: "metallico" }
  - { name: "mat_dx", type: "sphere", center: [2.5, 1, 0], radius: 1, material: "vetro" }

lights:
  - type: "point"
    position: [0, 8, -5]
    color: [1, 1, 1]
    intensity: 60
```

### Scena con Normal Map

```yaml
world:
  ambient_light: [0.03, 0.03, 0.04]
  background: [0.05, 0.05, 0.08]

camera:
  position: [0, 1.5, -5]
  look_at: [0, 1.5, 0]
  fov: 46

materials:
  - id: "muro_mattoni"
    type: "lambertian"
    texture:
      type: "image"
      path: "textures/brick-wall.png"
      uv_scale: [2, 1.5]
    normal_map:
      path: "textures/brick-wall-normal.png"
      strength: 1.2
      uv_scale: [2, 1.5]

  - id: "acciaio"
    type: "metal"
    color: [0.88, 0.88, 0.90]
    fuzz: 0.02
    normal_map:
      path: "textures/metal-scratched-normal.png"
      strength: 1.5

entities:
  - name: "parete"
    type: "quad"
    q: [-3, 0, 3]
    u: [6, 0, 0]
    v: [0, 3, 0]
    material: "muro_mattoni"

  - name: "sfera_metallo"
    type: "sphere"
    center: [0, 1.2, 0]
    radius: 1.2
    material: "acciaio"

lights:
  - type: "point"
    position: [-6, 3, 0]
    color: [1.0, 0.95, 0.85]
    intensity: 80
```

### Scena Architettonica con Area Light e Geometrie Miste

```yaml
world:
  ambient_light: [0.02, 0.02, 0.03]
  background: [0.0, 0.0, 0.0]
  ground: { type: "infinite_plane", material: "pavimento_chiaro", y: 0 }

camera:
  position: [5, 3, -10]
  look_at: [0, 1.5, 0]
  fov: 40

materials:
  - id: "pavimento_chiaro"
    type: "lambertian"
    texture: { type: "checker", scale: 5.0, colors: [[0.7, 0.7, 0.7], [0.8, 0.8, 0.8]] }
  - id: "marmo_colonna"
    type: "lambertian"
    texture: { type: "marble", scale: 8.0, randomize_rotation: true }
  - id: "vetro_cristallo"
    type: "dielectric"
    refraction_index: 1.8

lights:
  - type: "area"
    corner: [-2.0, 4.9, -2.0]
    u: [4.0, 0.0, 0.0]
    v: [0.0, 0.0, 4.0]
    color: [1.0, 0.97, 0.92]
    intensity: 40.0
    shadow_samples: 16
  - type: "point"
    position: [-5, 3, -5]
    color: [0.8, 0.8, 1.0]
    intensity: 3

entities:
  - { name: "col_sx", type: "cylinder", center: [-3, 0, 0], radius: 0.3, height: 4, material: "marmo_colonna" }
  - { name: "col_dx", type: "cylinder", center: [3, 0, 0], radius: 0.3, height: 4, material: "marmo_colonna" }
  - name: "trave"
    type: "box"
    scale: [7.0, 0.5, 0.8]
    translate: [0.0, 4.25, 0.0]
    material: "marmo_colonna"
  - { name: "gioiello", type: "sphere", center: [0, 2, 0], radius: 0.8, material: "vetro_cristallo" }
```

### 9.3 — Neon Lab (Solo Illuminazione Emissiva)

Stanza buia illuminata esclusivamente da oggetti con materiale `emissive`. Dimostra emissione colorata, riflessione su metallo e rifrazione nel vetro.

```yaml
world:
  ambient_light: [0.005, 0.005, 0.008]
  background: [0.0, 0.0, 0.0]
  ground: { type: "infinite_plane", material: "pavimento", y: 0 }

camera:
  position: [0, 2.5, -8]
  look_at: [0, 1.2, 0]
  fov: 50

materials:
  - id: "pavimento"
    type: "lambertian"
    color: [0.12, 0.12, 0.14]
  - id: "specchio"
    type: "metal"
    color: [0.92, 0.92, 0.94]
    fuzz: 0.02
  - id: "neon_magenta"
    type: "emissive"
    color: [1.0, 0.05, 0.6]
    intensity: 8.0
  - id: "neon_ciano"
    type: "emissive"
    color: [0.05, 0.85, 1.0]
    intensity: 8.0
  - id: "pannello"
    type: "emissive"
    color: [1.0, 0.97, 0.92]
    intensity: 12.0

entities:
  - { name: "neon_sx", type: "sphere", center: [-2.5, 1, 0], radius: 0.6, material: "neon_magenta" }
  - { name: "neon_dx", type: "sphere", center: [2.5, 1, 0], radius: 0.6, material: "neon_ciano" }
  - name: "pannello_soffitto"
    type: "quad"
    q: [-1.0, 4.5, -1.0]
    u: [2.0, 0.0, 0.0]
    v: [0.0, 0.0, 2.0]
    material: "pannello"
  - { name: "specchio_sfera", type: "sphere", center: [0, 0.8, -1], radius: 0.8, material: "specchio" }

lights:
  - type: "point"
    position: [0, 5, -3]
    color: [0.5, 0.5, 0.6]
    intensity: 0.5
```

### Golden Hour Landscape (Gradient Sky + Sun Disk)

Scena outdoor con cielo procedurale e sole basso. Sfere metalliche riflettono il gradiente del cielo; la sfera di vetro lo rifrange. Il sun disk è visibile nei riflessi.

```yaml
world:
  ambient_light: [0.04, 0.03, 0.02]
  sky:
    type: "gradient"
    zenith_color:  [0.15, 0.25, 0.55]
    horizon_color: [0.85, 0.55, 0.25]
    ground_color:  [0.20, 0.15, 0.10]
    sun:
      direction: [-0.8, -0.25, -0.5]
      color: [1.0, 0.85, 0.5]
      intensity: 20.0
      size: 4.0
      falloff: 24.0
  ground: { type: "infinite_plane", material: "terreno", y: 0 }

camera:
  position: [0, 1.8, -8]
  look_at: [0, 0.8, 0]
  fov: 55

materials:
  - id: "terreno"
    type: "lambertian"
    texture: { type: "checker", scale: 1.5, colors: [[0.25, 0.22, 0.18], [0.35, 0.32, 0.26]] }
  - id: "specchio"
    type: "metal"
    color: [0.95, 0.95, 0.97]
    fuzz: 0.0
  - id: "oro"
    type: "metal"
    color: [0.85, 0.65, 0.12]
    fuzz: 0.05
  - id: "vetro"
    type: "dielectric"
    refraction_index: 1.52

entities:
  - { name: "mirror", type: "sphere", center: [-2.5, 1, 0], radius: 1.0, material: "specchio" }
  - { name: "gold", type: "sphere", center: [0, 1, 0], radius: 1.0, material: "oro" }
  - { name: "glass", type: "sphere", center: [2.5, 1, 0], radius: 1.0, material: "vetro" }

lights:
  - type: "directional"
    direction: [-0.8, -0.25, -0.5]
    color: [1.0, 0.88, 0.55]
    intensity: 0.12
  - type: "directional"
    direction: [0.5, -0.7, 0.3]
    color: [0.5, 0.6, 0.85]
    intensity: 0.03
```

### Scena HDRI con Materiali PBR

Sfere con materiali diversi illuminate esclusivamente da un environment map HDR. Nessuna luce esplicita — tutta l'illuminazione viene dalla fotografia dell'ambiente.

```yaml
world:
  ambient_light: [0.0, 0.0, 0.0]
  sky:
    type: "hdri"
    path: "hdri/studio_small_09_4k.hdr"
    intensity: 1.0
    rotation: 0
  ground: { type: "infinite_plane", material: "pavimento", y: 0 }

camera:
  position: [0, 1.5, -5]
  look_at: [0, 0.8, 0]
  fov: 50

materials:
  - id: "pavimento"
    type: "metal"
    color: [0.15, 0.15, 0.18]
    fuzz: 0.4
  - id: "specchio"
    type: "metal"
    color: [0.97, 0.97, 0.98]
    fuzz: 0.0
  - id: "oro"
    type: "metal"
    color: [0.85, 0.65, 0.12]
    fuzz: 0.02
  - id: "vetro"
    type: "dielectric"
    refraction_index: 1.52
  - id: "diffuso"
    type: "lambertian"
    color: [0.85, 0.85, 0.85]

entities:
  - { name: "mirror", type: "sphere", center: [-2, 0.8, 0], radius: 0.8, material: "specchio" }
  - { name: "gold", type: "sphere", center: [0, 0.8, 0], radius: 0.8, material: "oro" }
  - { name: "glass", type: "sphere", center: [2, 0.8, 0], radius: 0.8, material: "vetro" }

lights: []
```

---

## 10. Regole e Best Practices

### Sintassi e Funzionamento
1. **Colori:** Nelle texture usa sempre una lista di liste: `colors: [[R,G,B], [R,G,B]]`.
2. **Coordinate:** Y positivo è sempre verso l'alto.
3. **Box:** Usa sempre `scale` + `translate` per i box. Il cubo unitario ha centro nell'origine.
4. **IDs materiale:** Ogni `id` deve essere univoco. I riferimenti `material` nelle entità sono **case-sensitive** e devono corrispondere esattamente. Un ID non trovato produce un materiale grigio di fallback senza errore.
5. **BVH:** Il motore ottimizza automaticamente le scene con più di 4 oggetti usando una BVH basata sull'asse con maggiore estensione dei centroidi.
6. **Luci di default:** Se la sezione `lights` è completamente assente dal YAML, il motore aggiunge automaticamente una directional + una point light. Per avere zero luci (scene HDRI-only o emissive-only), scrivi esplicitamente `lights: []`.
7. **Area Light:** I campi `corner`, `u` e `v` sono tutti obbligatori. Se uno è mancante, la luce viene saltata con un warning in console.
8. **Sky:** Usa `background` per interni, `sky: { type: "gradient" }` per outdoor procedurale, `sky: { type: "hdri" }` per illuminazione fotografica. Se `sky` è presente, `background` viene ignorato. Se usi il sun disk del gradient sky, allinea la `direction` con la directional light per coerenza.
9. **Image Texture:** I percorsi in `texture: { type: "image", path: "..." }` sono relativi alla directory del file YAML della scena. File non trovato → fallback magenta visibile con warning in console.
10. **HDRI:** Il percorso in `sky: { type: "hdri", path: "..." }` è relativo alla directory del YAML. Usa `rotation` per ruotare l'ambiente e allineare il sole/finestra con la scena. Con HDRI, usa `lights: []` per luce solo dall'environment map, oppure aggiungi luci per ombre direzionali extra.
11. **Normal Map:** Il `uv_scale` della normal map deve coincidere con quello della texture albedo per evitare disallineamenti. File non trovato → warning in console, superficie rimane liscia. La normale piatta di riferimento è RGB `(128, 128, 255)`: usare `flat-normal.png` generata da NormalMapGen per verificare che il sistema funzioni senza perturbazioni.

### Performance
12. **Campioni e area light:** Il costo reale per pixel è `samples × shadow_samples` per ogni area light. Con `-s 128 -S 16`, ogni pixel lancia oltre 2000 raggi. Usa `-S 4` da CLI per il draft — non serve modificare il YAML!
13. **Vetro e dielettrico:** I materiali dielettrici (vetro) sono i più costosi perché ogni rimbalzo può generare sia riflessione che rifrazione. Aumenta `--depth` per scene con molto vetro.

### Checklist prima del render finale

- [ ] Tutti gli `id` dei materiali sono univoci e referenziati correttamente nelle entità.
- [ ] La `camera.position` non si trova all'interno di un oggetto solido.
- [ ] Le texture con variazioni per-oggetto hanno `randomize_offset` o `randomize_rotation` attivo.
- [ ] Il file YAML usa correttamente gli **spazi** per l'indentazione (niente TAB).
- [ ] È stata eseguita un'anteprima a bassa risoluzione (`-w 400 -s 1 -S 4`).
- [ ] Le area light hanno `corner`, `u` e `v` tutti definiti.
- [ ] Se la scena deve essere buia, `background` è `[0, 0, 0]` e `sky` è assente.
- [ ] I seed degli oggetti con texture randomizzate sono fissi (se vuoi risultati riproducibili tra render).
- [ ] Se usi gradient sky con sun disk, la `direction` è allineata con la directional light.
- [ ] I file delle image texture e degli HDRI esistono nel percorso indicato (relativo al YAML).
- [ ] Per scene HDRI-only o emissive-only, usa `lights: []` esplicito (non omettere la sezione).
- [ ] Se usi `normal_map`, il `uv_scale` coincide con quello della texture albedo.
- [ ] I file delle normal map esistono nel percorso indicato (file mancante → superficie liscia senza errore, ma visivamente sbagliato).
- [ ] Le normal map OpenGL-style non richiedono `flip_y`; le DirectX-style richiedono `flip_y: true`.
