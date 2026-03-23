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
  ambient_light: [0.05, 0.05, 0.08]   # Luce di base addizionale su ogni superficie
  background: [0.4, 0.6, 1.0]          # Colore del cielo (sorgente di luce globale)
  ground:
    type: "infinite_plane"
    material: "nome_materiale"          # Riferimento a un materiale definito sotto
    y: 0.0                              # Altezza del piano (default: 0)
```

### Parametri

| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `ambient_light` | `[R, G, B]` | `[0.1, 0.1, 0.1]` | Luce piatta aggiunta a ogni superficie colpita |
| `background` | `[R, G, B]` | `[0.5, 0.7, 1.0]` | Colore del cielo piatto (per scene indoor o da studio). Ignorato se `sky` è presente |
| `ground.type` | stringa | — | Sempre `"infinite_plane"` |
| `ground.material` | stringa | — | ID del materiale per il terreno |
| `ground.y` | float | `0.0` | Quota verticale del piano |
| `sky` | oggetto | — | Configurazione del cielo procedurale (opzionale, vedi [2.1](#21-gradient-sky-e-sun-disk)) |

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
    zenith_color:  [0.10, 0.30, 0.80]  # Blu profondo (dritto in alto)
    horizon_color: [0.70, 0.85, 1.00]  # Azzurro pallido (linea d'orizzonte)
    ground_color:  [0.30, 0.25, 0.20]  # Marrone scuro (sotto l'orizzonte)
    sun:
      direction: [-0.8, -0.25, -0.5]   # Direzione DA cui arriva la luce
      color: [1.0, 0.85, 0.5]
      intensity: 20.0
      size: 4.0
      falloff: 24.0
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
| `focal_dist` | float | `1.0` | Distanza dal punto di vista al piano di messa a fuoco. Gli oggetti a questa distanza saranno nitidi. |

---

## 4. Sezione `materials`

I materiali definiscono come le superfici interagiscono con la luce.

### 4.1 Lambertian (Diffuso/Opaco)
Materiale opaco che diffonde la luce uniformemente in tutte le direzioni.
```yaml
  - id: "rosso_opaco"
    type: "lambertian"
    color: [0.8, 0.2, 0.1]
```

### 4.2 Metal (Metallico/Speculare)
Riflette la luce come uno specchio. Il parametro `fuzz` controlla la rugosità.
```yaml
  - id: "oro"
    type: "metal"
    color: [0.85, 0.65, 0.1]
    fuzz: 0.1
```
| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `fuzz` | float | `0.0` | Rugosità: 0.0=specchio perfetto, 1.0=diffusione quasi totale |

### 4.3 Dielectric (Vetro/Trasparente)
Materiale trasparente con rifrazione e riflesso Fresnel.
```yaml
  - id: "vetro"
    type: "dielectric"
    refraction_index: 1.5
    color: [1.0, 0.95, 0.95]
```
| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `refraction_index` | float | `1.5` | Indice di rifrazione (1.0=aria, 1.33=acqua, 1.5=vetro, 2.42=diamante) |
| `color` | `[R, G, B]` | `[1, 1, 1]` | Tinting del vetro (bianco=trasparente, colorato=vetro colorato) |

### 4.4 Emissive (Luminoso)
Materiale auto-luminoso: l'oggetto emette luce propria e brilla nella scena senza bisogno di illuminazione esterna. La luce emessa si propaga tramite i rimbalzi indiretti del path tracer, illuminando naturalmente gli oggetti circostanti.

Usi tipici: neon, LED, insegne, lava, fiamme, sfere magiche, pannelli luminosi, indicatori.

```yaml
  - id: "neon_magenta"
    type: "emissive"
    color: [1.0, 0.0, 0.8]
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

Ogni entità può avere trasformazioni applicate in ordine **Scale → Rotate → Translate**:

```yaml
  - name: "cubo_ruotato"
    type: "box"
    scale: [2, 1, 1]
    rotate: [0, 45, 0]           # Rotazione 45° attorno a Y
    translate: [3, 0.5, 0]
    material: "materiale"
```

### 6.9 Parametro Seed
Ogni entità ha un seed numerico opzionale che determina la randomizzazione delle texture procedurali:
```yaml
  - name: "sfera_1"
    type: "sphere"
    center: [0, 1, 0]
    radius: 1
    seed: 42                    # Seed fisso = texture identica tra render
    material: "marmo_variegato"
```

Se `seed` non è specificato, viene generato casualmente. Usa seed fissi per risultati riproducibili.

---

## 7. Sezione `lights`

Le luci esplicite della scena. Puoi combinare più tipi di luce per ottenere l'effetto desiderato.

### 7.1 Point Light (Puntiforme)
Luce omnidirezionale da un singolo punto. L'intensità decade con il quadrato della distanza.
```yaml
  - type: "point"
    position: [0, 10, -5]
    color: [1.0, 1.0, 1.0]
    intensity: 8
```
| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `position` | `[X, Y, Z]` | `[0, 10, 0]` | Posizione della luce |
| `color` | `[R, G, B]` | `[1, 1, 1]` | Colore |
| `intensity` | float | `1.0` | Intensità. Valori tipici: 4–20. |

### 7.2 Directional Light (Sole)
Non ha attenuazione con la distanza.
```yaml
  - type: "directional"
    direction: [-1, -1, -1]     # Direzione DA cui arriva la luce
    color: [1, 1, 0.9]
    intensity: 0.08
```
| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `direction` | `[X, Y, Z]` | `[-1, -1, -1]` | Direzione della luce (viene normalizzata) |
| `color` | `[R, G, B]` | `[1, 1, 1]` | Colore |
| `intensity` | float | `1.0` | Intensità. Valori tipici: 0.03–0.2. |

> **Alias:** Puoi usare anche `type: "sun"` come alias per `"directional"`.

> **💡 Tip: Allinea Directional Light e Sun Disk.** Se usi un gradient sky con sun disk, imposta la stessa `direction` sulla directional light per coerenza visiva: il sole visibile nel cielo e l'illuminazione diretta arrivano dalla stessa parte.

### 7.3 Spot Light (Faretto)
Luce conica con posizione e direzione. Ha un cono interno (piena intensità) e un cono esterno (sfumatura smooth). L'attenuazione angolare usa un'interpolazione quadratica tra i due coni.
```yaml
  - type: "spot"
    position: [0, 8, -3]
    direction: [0, -1, 0]       # Punta verso il basso
    color: [1.0, 0.95, 0.9]
    intensity: 12
    inner_angle: 15              # Mezzo-angolo cono interno (gradi)
    outer_angle: 30              # Mezzo-angolo cono esterno (gradi)
```
| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `position` | `[X, Y, Z]` | `[0, 10, 0]` | Posizione del faretto |
| `direction` | `[X, Y, Z]` | `[0, -1, 0]` | Direzione verso cui punta |
| `color` | `[R, G, B]` | `[1, 1, 1]` | Colore della luce |
| `intensity` | float | `1.0` | Intensità. Valori tipici: 6–30. |
| `inner_angle` | float | `15` | Mezzo-angolo del cono interno (piena intensità), in gradi |
| `outer_angle` | float | `30` | Mezzo-angolo del cono esterno (sfumatura a zero), in gradi |

> **Alias:** Puoi usare anche `type: "spotlight"`.

### 7.4 Area Light (Emettitore Rettangolare)

Sorgente luminosa rettangolare che produce **ombre morbide** fisicamente corrette con gradiente di penombra. Definita da un angolo (`corner`) e due vettori che formano il rettangolo.

Il motore usa campionamento Monte Carlo: per ogni punto della scena vengono sparati `shadow_samples` raggi verso punti casuali sulla superficie della luce, e il risultato è la media. Più shadow samples = penombra più morbida e meno rumorosa.

```yaml
  - type: "area"
    corner: [-1.0, 4.9, -1.0]  # Un angolo del rettangolo
    u: [2.0, 0.0, 0.0]          # Primo lato (larghezza: 2 unità in X)
    v: [0.0, 0.0, 2.0]          # Secondo lato (profondità: 2 unità in Z)
    color: [1.0, 0.95, 0.9]
    intensity: 40.0
    shadow_samples: 16
```
| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `corner` | `[X, Y, Z]` | — (**obbligatorio**) | Un angolo del rettangolo luminoso |
| `u` | `[X, Y, Z]` | — (**obbligatorio**) | Primo vettore lato (definisce larghezza e direzione) |
| `v` | `[X, Y, Z]` | — (**obbligatorio**) | Secondo vettore lato (definisce l'altro asse) |
| `color` | `[R, G, B]` | `[1, 1, 1]` | Colore emesso |
| `intensity` | float | `20.0` | Intensità totale. Valori tipici: 15–60. |
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
| `point` generica | 4 – 20 | Scala con il quadrato della distanza: raddoppiare la distanza richiede ×4 l'intensità |
| `spot` key light | 15 – 30 | Valori più alti per coni stretti (`inner_angle` < 15°) |
| `spot` fill / rim | 5 – 15 | Tipicamente 1/3 – 1/2 della key |
| `point` accent / bounce | 0.5 – 2 | Luci di dettaglio, quasi invisibili da sole |
| `directional` (sole) | 0.05 – 0.15 | Non ha attenuazione con la distanza: valori bassi bastano |
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

### 9.1 — Showcase Materiali (Confronto)

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
    intensity: 8
```

### 9.2 — Scena Architettonica con Area Light e Geometrie Miste

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

### 9.4 — Golden Hour Landscape (Gradient Sky + Sun Disk)

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

### 9.5 — HDRI Studio (Environment-Lit Materials)

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

### Performance
11. **Campioni e area light:** Il costo reale per pixel è `samples × shadow_samples` per ogni area light. Con `-s 128 -S 16`, ogni pixel lancia oltre 2000 raggi. Usa `-S 4` da CLI per il draft — non serve modificare il YAML!
12. **Vetro e dielettrico:** I materiali dielettrici (vetro) sono i più costosi perché ogni rimbalzo può generare sia riflessione che rifrazione. Aumenta `--depth` per scene con molto vetro.

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
