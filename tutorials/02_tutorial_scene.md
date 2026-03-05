# Tutorial: Creazione dei File di Scena (YAML)

## Indice
1. [Struttura del File](#1-struttura-del-file)
2. [Sezione `world`](#2-sezione-world)
3. [Sezione `camera`](#3-sezione-camera)
4. [Sezione `materials`](#4-sezione-materials)
5. [Sezione `entities`](#5-sezione-entities)
6. [Sezione `lights`](#6-sezione-lights)
7. [Esempi Completi](#7-esempi-completi)
8. [Regole e Best Practices](#8-regole-e-best-practices)

---

## 1. Struttura del File

Ogni file di scena è un documento YAML con 5 sezioni principali:

```yaml
world:      # Ambiente globale (cielo, terreno, luce ambiente)
camera:     # Punto di vista e ottica
materials:  # Definizione dei materiali (colori, proprietà fisiche)
entities:   # Oggetti 3D nella scena
lights:     # Sorgenti di luce
```

> **Nota:** I colori sono sempre espressi come `[R, G, B]` con valori da `0.0` a `1.0`. Le coordinate usano il sistema: **X** = destra, **Y** = alto, **Z** = verso la camera (negativo = lontano).

---

## 2. Sezione `world`

Definisce l'ambiente globale della scena.

```yaml
world:
  ambient_light: [0.1, 0.1, 0.1]   # Luce di base che illumina tutto
  background: [0.5, 0.7, 1.0]       # Colore del cielo (gradiente alto)
  ground:
    type: "infinite_plane"
    material: "nome_materiale"       # Riferimento a un materiale definito sotto
    y: 0.0                           # Altezza del piano (default: 0)
```

### Parametri

| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `ambient_light` | `[R, G, B]` | `[0.1, 0.1, 0.1]` | Illuminazione minima globale |
| `background` | `[R, G, B]` | `[0.5, 0.7, 1.0]` | Colore del cielo in alto |
| `ground.type` | stringa | — | Sempre `"infinite_plane"` |
| `ground.material` | stringa | — | ID del materiale per il terreno |
| `ground.y` | float | `0.0` | Quota verticale del piano |

### Esempio: Scena notturna

```yaml
world:
  ambient_light: [0.02, 0.02, 0.05]
  background: [0.02, 0.02, 0.08]
  ground:
    type: "infinite_plane"
    material: "terreno_scuro"
    y: 0
```

### Esempio: Scena diurna luminosa

```yaml
world:
  ambient_light: [0.15, 0.15, 0.15]
  background: [0.4, 0.65, 1.0]
  ground:
    type: "infinite_plane"
    material: "erba"
    y: -1
```

---

## 3. Sezione `camera`

Controlla il punto di vista, l'inquadratura e l'effetto profondità di campo.

```yaml
camera:
  position: [0, 2, -8]       # Posizione della fotocamera
  look_at: [0, 0, 0]         # Punto verso cui guarda
  fov: 60                     # Campo visivo verticale (gradi)
  aperture: 0.1               # Apertura lente (0 = tutto a fuoco)
  focal_dist: 8.0             # Distanza di messa a fuoco
```

### Parametri

| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `position` | `[X, Y, Z]` | `[0, 1, -5]` | Dove si trova la camera |
| `look_at` | `[X, Y, Z]` | `[0, 0, 0]` | Punto di mira |
| `fov` | float (gradi) | `60` | Campo visivo verticale |
| `aperture` | float | `0` | Apertura: controlla la sfocatura (0 = nitido ovunque) |
| `focal_dist` | float | `1` | Distanza a cui gli oggetti sono perfettamente a fuoco |

### Effetto Depth of Field (Sfocatura)

Per ottenere lo **sfondo sfocato** tipico della fotografia:

```yaml
camera:
  position: [0, 1, -5]
  look_at: [0, 0, 0]
  fov: 45
  aperture: 0.3         # Valore alto = sfocatura pronunciata
  focal_dist: 5.0       # La distanza position→look_at = tutto a fuoco lì
```

Per un'immagine **completamente nitida**:

```yaml
camera:
  position: [0, 1, -5]
  look_at: [0, 0, 0]
  fov: 60
  aperture: 0.0          # Nessuna profondità di campo
  focal_dist: 1.0
```

### Inquadrature tipiche

**Dall'alto (vista a volo d'uccello):**
```yaml
camera:
  position: [0, 15, -1]
  look_at: [0, 0, 0]
  fov: 50
```

**Vista ravvicinata di un oggetto:**
```yaml
camera:
  position: [1, 0.5, -2]
  look_at: [0, 0.5, 0]
  fov: 35
  aperture: 0.15
  focal_dist: 2.2
```

---

## 4. Sezione `materials`

Ogni materiale ha un `id` univoco e un `type` che ne definisce il comportamento fisico. Gli oggetti fanno riferimento ai materiali tramite l'`id`.

### 4.1 — Lambertian (Diffuso/Opaco)

Superficie opaca che diffonde la luce uniformemente in tutte le direzioni. Ideale per: muri, terreno, oggetti colorati opachi.

```yaml
materials:
  - id: "rosso_opaco"
    type: "lambertian"
    color: [0.8, 0.1, 0.1]    # Rosso intenso
```

| Campo | Tipo | Descrizione |
|-------|------|-------------|
| `color` | `[R, G, B]` | Colore diffuso (0–1 per canale) |

**Esempi di colori utili:**

```yaml
  - id: "bianco"
    type: "lambertian"
    color: [0.9, 0.9, 0.9]

  - id: "grigio_medio"
    type: "lambertian"
    color: [0.5, 0.5, 0.5]

  - id: "erba"
    type: "lambertian"
    color: [0.2, 0.6, 0.15]

  - id: "terra"
    type: "lambertian"
    color: [0.55, 0.35, 0.2]

  - id: "azzurro_cielo"
    type: "lambertian"
    color: [0.4, 0.6, 0.9]
```

### 4.2 — Metal (Metallico/Speculare)

Superficie riflettente come specchi o metalli. Il parametro `fuzz` controlla la rugosità.

```yaml
  - id: "specchio"
    type: "metal"
    color: [0.95, 0.95, 0.95]
    fuzz: 0.0                   # 0 = specchio perfetto

  - id: "alluminio_satinato"
    type: "metal"
    color: [0.8, 0.8, 0.85]
    fuzz: 0.3                   # Riflessione sfumata

  - id: "rame"
    type: "metal"
    color: [0.85, 0.5, 0.25]
    fuzz: 0.1
```

| Campo | Tipo | Range | Descrizione |
|-------|------|-------|-------------|
| `color` | `[R, G, B]` | 0–1 | Colore della superficie riflettente |
| `fuzz` | float | 0.0–1.0 | 0 = specchio perfetto, 1 = completamente sfumato |

**Guida visiva al fuzz:**

| `fuzz` | Effetto |
|--------|---------|
| `0.0` | Specchio perfetto (come Chrome) |
| `0.05` | Metallo lucido (acciaio inox) |
| `0.1–0.2` | Metallo semi-lucido (alluminio lavorato) |
| `0.3–0.5` | Metallo satinato/spazzolato |
| `0.7–1.0` | Quasi diffuso, riflessi molto sfumati |

### 4.3 — Dielectric (Vetro/Trasparente)

Materiale trasparente che rifrange la luce (vetro, acqua, diamante). Non ha un colore proprio: la luce lo attraversa.

```yaml
  - id: "vetro"
    type: "dielectric"
    refraction_index: 1.5       # Vetro standard

  - id: "acqua"
    type: "dielectric"
    refraction_index: 1.33

  - id: "diamante"
    type: "dielectric"
    refraction_index: 2.42
```

| Campo | Tipo | Descrizione |
|-------|------|-------------|
| `refraction_index` | float | Indice di rifrazione del materiale |

**Indici di rifrazione comuni:**

| Materiale | Indice |
|-----------|--------|
| Aria | 1.0 |
| Acqua | 1.33 |
| Vetro | 1.5 |
| Cristallo | 1.8 |
| Diamante | 2.42 |

> **Trucco:** Per creare una **bolla di sapone**, usa una sfera dieletrica con un'altra sfera dieletrica leggermente più piccola all'interno.

---

## 5. Sezione `entities`

Gli oggetti 3D della scena. Ogni entità ha un `name`, un `type` e un riferimento al `material`.

### 5.1 — Sphere (Sfera)

```yaml
entities:
  - name: "sfera_1"
    type: "sphere"
    center: [0, 1, 0]          # Centro della sfera
    radius: 1.0                 # Raggio
    material: "vetro"
```

| Campo | Tipo | Descrizione |
|-------|------|-------------|
| `center` | `[X, Y, Z]` | Centro della sfera |
| `radius` | float | Raggio |

**Esempio: Sfera appoggiata sul piano a Y=0**

Se il piano del terreno è a `y: 0` e il raggio è `0.5`, il centro deve essere a `y: 0.5`:

```yaml
  - name: "pallina"
    type: "sphere"
    center: [0, 0.5, 0]        # Appoggiata a terra
    radius: 0.5
    material: "rosso_opaco"
```

### 5.2 — Box (Parallelepipedo AABB)

Parallelepipedo allineato agli assi. Definito da due vertici opposti (minimo e massimo).

```yaml
  - name: "cubo_1"
    type: "box"
    min: [-1, 0, -1]           # Vertice con coordinate minime
    max: [1, 2, 1]             # Vertice con coordinate massime
    material: "bianco"
```

| Campo | Tipo | Descrizione |
|-------|------|-------------|
| `min` | `[X, Y, Z]` | Angolo con valori minimi |
| `max` | `[X, Y, Z]` | Angolo con valori massimi |

**Esempio: Cubo perfetto 1×1×1 appoggiato a terra**

```yaml
  - name: "cubo"
    type: "box"
    min: [-0.5, 0, -0.5]
    max: [0.5, 1, 0.5]
    material: "grigio_medio"
```

**Esempio: Tavolo (piano sottile)**

```yaml
  - name: "piano_tavolo"
    type: "box"
    min: [-2, 0.9, -1]
    max: [2, 1.0, 1]           # Spessore 0.1
    material: "terra"
```

### 5.3 — Triangle (Triangolo)

Definito da 3 vertici. Base per mesh poligonali future.

```yaml
  - name: "triangolo_1"
    type: "triangle"
    v0: [0, 0, 0]
    v1: [2, 0, 0]
    v2: [1, 2, 0]
    material: "rosso_opaco"
```

| Campo | Tipo | Descrizione |
|-------|------|-------------|
| `v0`, `v1`, `v2` | `[X, Y, Z]` | I tre vertici del triangolo |

### 5.4 — Cylinder (Cilindro)

Cilindro allineato all'asse Y con tappi superiore e inferiore.

```yaml
  - name: "colonna"
    type: "cylinder"
    center: [0, 0, 0]          # Base del cilindro
    radius: 0.3
    height: 3.0                 # Si estende da center.Y a center.Y + height
    material: "bianco"
```

| Campo | Tipo | Descrizione |
|-------|------|-------------|
| `center` | `[X, Y, Z]` | Punto base (centro del disco inferiore) |
| `radius` | float | Raggio |
| `height` | float | Altezza (si estende verso l'alto in Y) |

### 5.5 — Plane / Infinite Plane (Piano Infinito)

Un piano infinito definibile anche come entità (oltre che come `ground`):

```yaml
  - name: "muro_fondo"
    type: "plane"
    point: [0, 0, 5]
    normal: [0, 0, -1]         # Rivolto verso la camera
    material: "bianco"
```

| Campo | Tipo | Descrizione |
|-------|------|-------------|
| `point` | `[X, Y, Z]` | Un punto qualsiasi sul piano |
| `normal` | `[X, Y, Z]` | Direzione perpendicolare al piano |

---

## 6. Sezione `lights`

### 6.1 — Point Light (Luce Puntiforme)

Emette luce in tutte le direzioni da un punto. L'intensità diminuisce con il quadrato della distanza.

```yaml
lights:
  - type: "point"
    position: [0, 10, -5]
    color: [1.0, 1.0, 1.0]     # Bianca
    intensity: 100.0
```

| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `position` | `[X, Y, Z]` | `[0, 10, 0]` | Posizione nello spazio |
| `color` | `[R, G, B]` | `[1, 1, 1]` | Colore della luce |
| `intensity` | float | `1.0` | Intensità (più alta = più luminosa, compensa la distanza) |

> **Nota sull'intensità:** Una luce puntiforme a 10 unità di distanza con intensità 100 produce la stessa illuminazione di una a 1 unità con intensità 1 (legge dell'inverso del quadrato).

### 6.2 — Directional Light (Luce Direzionale / Sole)

Luce parallela infinita, come il sole. Non ha posizione né attenuazione.

```yaml
  - type: "directional"
    direction: [-0.5, -1.0, -0.3]   # Direzione DA cui arriva la luce
    color: [1.0, 0.95, 0.85]         # Bianco caldo (luce solare)
    intensity: 0.8
```

| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `direction` | `[X, Y, Z]` | `[-1, -1, -1]` | Direzione della luce (viene da qui) |
| `color` | `[R, G, B]` | `[1, 1, 1]` | Colore |
| `intensity` | float | `1.0` | Intensità (0–1 tipico) |

### Combinazioni consigliate

**Esterno diurno:**
```yaml
lights:
  - type: "directional"
    direction: [-0.3, -1, -0.5]
    color: [1.0, 0.97, 0.9]
    intensity: 0.9
  - type: "point"
    position: [0, 15, 0]
    color: [1.0, 1.0, 1.0]
    intensity: 50.0
```

**Interno con lampada:**
```yaml
lights:
  - type: "point"
    position: [0, 3, 0]
    color: [1.0, 0.85, 0.6]    # Luce calda
    intensity: 30.0
```

**Scena drammatica:**
```yaml
lights:
  - type: "point"
    position: [-5, 2, -3]
    color: [0.3, 0.5, 1.0]     # Luce blu fredda
    intensity: 40.0
  - type: "point"
    position: [5, 2, -3]
    color: [1.0, 0.4, 0.2]     # Luce arancione calda
    intensity: 40.0
```

> **Default:** Se la sezione `lights` è omessa, il motore aggiunge automaticamente una luce direzionale e una puntiforme di default.

---

## 7. Esempi Completi

### 7.1 — Scena Minima

La scena più semplice possibile: una sfera su un piano.

```yaml
world:
  ambient_light: [0.1, 0.1, 0.1]
  background: [0.5, 0.7, 1.0]
  ground:
    type: "infinite_plane"
    material: "pavimento"

camera:
  position: [0, 1, -4]
  look_at: [0, 0.5, 0]
  fov: 60

materials:
  - id: "pavimento"
    type: "lambertian"
    color: [0.6, 0.6, 0.6]
  - id: "rosso"
    type: "lambertian"
    color: [0.8, 0.2, 0.1]

entities:
  - name: "sfera"
    type: "sphere"
    center: [0, 0.5, 0]
    radius: 0.5
    material: "rosso"
```

### 7.2 — Showcase Materiali

Tre sfere affiancate che mostrano i tre tipi di materiale:

```yaml
world:
  ambient_light: [0.08, 0.08, 0.1]
  background: [0.3, 0.5, 0.9]
  ground:
    type: "infinite_plane"
    material: "pavimento"

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

lights:
  - type: "directional"
    direction: [-1, -1, -0.5]
    color: [1.0, 1.0, 0.95]
    intensity: 0.8
  - type: "point"
    position: [3, 8, -4]
    color: [1, 1, 1]
    intensity: 80

entities:
  # Sfera diffusa (sinistra)
  - name: "lambertian_sphere"
    type: "sphere"
    center: [-2.5, 1, 0]
    radius: 1.0
    material: "diffuso"

  # Sfera metallica (centro)
  - name: "metal_sphere"
    type: "sphere"
    center: [0, 1, 0]
    radius: 1.0
    material: "metallico"

  # Sfera di vetro (destra)
  - name: "glass_sphere"
    type: "sphere"
    center: [2.5, 1, 0]
    radius: 1.0
    material: "vetro"
```

### 7.3 — Scena Architettonica con Geometrie Miste

```yaml
world:
  ambient_light: [0.08, 0.08, 0.1]
  background: [0.35, 0.55, 0.95]
  ground:
    type: "infinite_plane"
    material: "pavimento_chiaro"
    y: 0

camera:
  position: [5, 3, -8]
  look_at: [0, 1, 0]
  fov: 45
  aperture: 0.08
  focal_dist: 10.0

materials:
  - id: "pavimento_chiaro"
    type: "lambertian"
    color: [0.75, 0.72, 0.68]
  - id: "marmo_bianco"
    type: "metal"
    color: [0.95, 0.93, 0.9]
    fuzz: 0.02
  - id: "vetro_cristallo"
    type: "dielectric"
    refraction_index: 1.8
  - id: "mattone"
    type: "lambertian"
    color: [0.7, 0.3, 0.15]
  - id: "oro"
    type: "metal"
    color: [0.85, 0.65, 0.15]
    fuzz: 0.0

lights:
  - type: "directional"
    direction: [-0.4, -1, -0.6]
    color: [1.0, 0.97, 0.9]
    intensity: 0.9
  - type: "point"
    position: [0, 8, -2]
    color: [1, 1, 1]
    intensity: 60

entities:
  # Colonna sinistra
  - name: "colonna_sx"
    type: "cylinder"
    center: [-3, 0, 0]
    radius: 0.3
    height: 4.0
    material: "marmo_bianco"

  # Colonna destra
  - name: "colonna_dx"
    type: "cylinder"
    center: [3, 0, 0]
    radius: 0.3
    height: 4.0
    material: "marmo_bianco"

  # Architrave (trave orizzontale)
  - name: "architrave"
    type: "box"
    min: [-3.5, 4, -0.4]
    max: [3.5, 4.5, 0.4]
    material: "marmo_bianco"

  # Muro sullo sfondo
  - name: "muro"
    type: "box"
    min: [-5, 0, 2]
    max: [5, 5, 2.3]
    material: "mattone"

  # Sfera di cristallo sul piedistallo
  - name: "piedistallo"
    type: "box"
    min: [-0.4, 0, -0.4]
    max: [0.4, 0.8, 0.4]
    material: "marmo_bianco"

  - name: "sfera_cristallo"
    type: "sphere"
    center: [0, 1.5, 0]
    radius: 0.6
    material: "vetro_cristallo"

  # Sfera dorata decorativa
  - name: "sfera_oro"
    type: "sphere"
    center: [2, 0.35, -2]
    radius: 0.35
    material: "oro"
```

---

## 8. Regole e Best Practices

### Sintassi YAML
- Usa **spazi** per l'indentazione (mai tab)
- I vettori sono liste: `[X, Y, Z]`
- Le stringhe possono essere con o senza virgolette: `type: "sphere"` o `type: sphere`
- I commenti iniziano con `#`

### Coordinate
- **Y positivo** = verso l'alto
- Se il terreno è a `y: 0`, posiziona gli oggetti con il fondo a `y: 0` o sopra
- Il raggio di una sfera centrata a `[0, R, 0]` sarà esattamente appoggiata al piano `y=0`

### Materiali
- Definisci **prima** i materiali, **poi** usali negli `entities` e nel `ground`
- L'`id` deve essere univoco
- Se un `material` referenziato non esiste, viene usato un grigio diffuso di default

### Performance
- Le **sfere di vetro** (`dielectric`) sono le più costose per il rendering
- I **Box** sono molto efficienti grazie al test slab
- Il **BVH** si attiva automaticamente con più di 4 oggetti finiti
- Usa il **piano infinito** solo per terreno/muri: NON viene incluso nel BVH

### Checklist prima del render finale

- [ ] Tutti gli `id` dei materiali sono univoci
- [ ] Tutti gli `entities` referenziano materiali definiti
- [ ] La `camera.position` non è dentro nessun oggetto
- [ ] Il `ground.y` è coerente con la posizione degli oggetti
- [ ] Almeno una luce è presente (o sarà aggiunta quella di default)
- [ ] Il file è stato testato con un render rapido (`-s 1 --width 320 --height 180`)
