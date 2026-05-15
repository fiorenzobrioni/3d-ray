# Capitolo 4: Tutte le forme

3D-Ray offre un ricco set di primitive geometriche -- dalle semplici sfere ai tori, capsule e mesh OBJ. Questo capitolo documenta ognuna di esse con la sintassi YAML esatta, i valori predefiniti e le note d'uso.

---

## 4.1 Sphere (Sfera)

```yaml
- type: "sphere"
  center: [0, 1, 0]
  radius: 1.0
  material: "my_material"
```

| Parametro  | Predefinito | Descrizione                         |
|------------|-------------|-------------------------------------|
| `center`   | `[0, 0, 0]` | Punto centrale nello spazio world   |
| `radius`   | `1.0`       | Raggio della sfera                  |

La sfera è la primitiva più semplice e veloce. Ha un'intersezione analitica (nessuna approssimazione) ed è la forma ideale per i test.

Per posizionare una sfera appoggiata su un pavimento a Y=0, imposta `center` a `[x, radius, z]`.

---

## 4.2 Box (Cubo Unitario)

```yaml
- type: "box"
  material: "wood"
  translate: [0, 0.5, 0]
  scale: [2, 1, 1.5]
  rotate: [0, 30, 0]
```

Il box è sempre un **cubo unitario** che va da -0.5 a +0.5 su ogni asse. Non ha parametri `center` o `size`. Tutto il posizionamento e il dimensionamento avviene tramite le trasformazioni.

Questo design significa che **si deve** usare `translate`, `scale` e/o `rotate` per posizionare un box nella scena.

| Trasformazione       | Effetto sul cubo unitario                        |
|----------------------|--------------------------------------------------|
| `scale: [2, 1, 1.5]` | Lo rende largo 2 unità, alto 1 e profondo 1.5    |
| `translate: [0, 0.5, 0]` | Lo solleva in modo che la base poggi su Y=0  |
| `rotate: [0, 45, 0]`   | Lo ruota di 45 gradi attorno all'asse Y        |

**Esempio: il piano di un tavolo** (box sottile e largo su Y=0.75):

```yaml
- type: "box"
  material: "wood"
  scale: [2.0, 0.08, 1.0]
  translate: [0, 0.79, 0]
```

---

## 4.3 Infinite Plane (Piano Infinito)

```yaml
- type: "infinite_plane"
  point: [0, 0, 0]
  normal: [0, 1, 0]
  material: "floor"
```

| Parametro | Predefinito | Descrizione                                     |
|-----------|-------------|-------------------------------------------------|
| `point`   | --          | Qualsiasi punto che giace sul piano             |
| `normal`  | --          | La direzione verso cui guarda il piano (perpendicolare) |

Un piano infinito si estende all'infinito in tutte le direzioni. È perfetto per pavimenti, pareti e soffitti.

Orientamenti comuni:

| Superficie      | `point`       | `normal`       |
|-----------------|---------------|----------------|
| Pavimento       | `[0, 0, 0]`   | `[0, 1, 0]`    |
| Soffitto        | `[0, 3, 0]`   | `[0, -1, 0]`   |
| Parete di fondo | `[0, 0, 5]`   | `[0, 0, -1]`   |
| Parete sinistra | `[-3, 0, 0]`  | `[1, 0, 0]`    |

> **Nota:** I piani infiniti non possono essere delimitati (bounded), quindi sono gestiti separatamente dalla struttura di accelerazione BVH. Usali con parsimonia (1--3 per scena). Per superfici piane delimitate, usa un quad.

Disponibile anche come `type: "plane"`.

---

## 4.4 Cylinder (Cilindro)

```yaml
- type: "cylinder"
  center: [0, 0, 0]
  radius: 0.3
  height: 2.0
  material: "steel"
```

| Parametro | Predefinito | Descrizione                                   |
|-----------|-------------|-----------------------------------------------|
| `center`  | `[0, 0, 0]` | Centro del disco di base                      |
| `radius`  | `1.0`       | Raggio del cilindro                           |
| `height`  | `1.0`       | Altezza (si estende verso l'alto dal centro)  |

Il cilindro è allineato all'asse Y (verticale) e chiuso alle estremità. Il `center` è il centro del disco inferiore; il cilindro si estende verso l'alto per l'altezza `height`.

**Esempio: un pilastro** dal pavimento a 3 unità di altezza:

```yaml
- type: "cylinder"
  center: [0, 0, 0]
  radius: 0.25
  height: 3.0
  material: "marble"
```

---

## 4.5 Cone e Truncated Cone (Cono e Tronco di Cono)

```yaml
# Cono appuntito (top_radius = 0)
- type: "cone"
  center: [0, 0, 0]
  radius: 0.5
  top_radius: 0.0
  height: 1.5
  material: "red"

# Cono troncato / frustum
- type: "cone"
  center: [0, 0, 0]
  radius: 0.6
  top_radius: 0.2
  height: 1.0
  material: "blue"
```

| Parametro    | Predefinito | Descrizione                                   |
|--------------|-------------|-----------------------------------------------|
| `center`     | `[0, 0, 0]` | Centro del disco di base                      |
| `radius`     | `1.0`       | Raggio inferiore                              |
| `top_radius` | `0.0`       | Raggio superiore (0 = punta acuta)            |
| `height`     | `1.0`       | Altezza (si estende verso l'alto dal centro)  |

Quando `top_radius` è 0 il risultato è un cono a punta. Quando è maggiore di 0 il cono è troncato (un frustum) -- una forma utile per gambe di tavoli, paralumi e vasi.

Alias del tipo: `cone`, `truncated_cone`, `frustum`.

---

## 4.6 Torus (Toro / Ciambella)

```yaml
- type: "torus"
  major_radius: 1.0
  minor_radius: 0.3
  material: "gold"
  translate: [0, 0.3, 0]
```

| Parametro      | Predefinito | Descrizione                                       |
|----------------|-------------|---------------------------------------------------|
| `major_radius` | `1.0`       | Distanza dal centro al centro del tubo            |
| `minor_radius` | `0.25`      | Raggio del tubo stesso                            |

Il toro viene sempre creato nel **piano XZ all'origine**. Usa `translate` per posizionarlo e `rotate` per inclinarlo.

- Un anello (come una ciambella o uno pneumatico) giace piatto per impostazione predefinita.
- Per metterlo in piedi, `rotate: [90, 0, 0]`.
- Il rapporto `minor_radius / major_radius` controlla la "cicciosità".

Alias del tipo: `torus`, `donut`.

> **Nota tecnica:** Il toro utilizza un risolutore quartico analitico per l'intersezione dei raggi -- nessuna tassellazione, perfettamente liscio a qualsiasi livello di zoom.

---

## 4.7 Capsule (Capsula)

```yaml
- type: "capsule"
  center: [0, 0, 0]
  radius: 0.3
  height: 1.5
  material: "white"
```

| Parametro | Predefinito | Descrizione                                   |
|-----------|-------------|-----------------------------------------------|
| `center`  | `[0, 0, 0]` | Centro dell'emisfero di base                  |
| `radius`  | `1.0`       | Raggio del cilindro e degli emisferi          |
| `height`  | `1.0`       | Altezza della sezione cilindrica              |

Una capsula è un cilindro con estremità emisferiche -- come una pillola o una salsiccia. L'altezza totale è `height + 2 * radius`.

Alias del tipo: `capsule`, `pill`.

---

## 4.8 Annulus (Anello / Disco Forato)

```yaml
- type: "annulus"
  center: [0, 0.01, 0]
  radius: 1.0
  inner_radius: 0.5
  normal: [0, 1, 0]
  material: "metal"
```

| Parametro      | Predefinito | Descrizione                         |
|----------------|-------------|-------------------------------------|
| `center`       | `[0, 0, 0]` | Punto centrale                      |
| `radius`       | `1.0`       | Raggio esterno                      |
| `inner_radius` | `0.0`       | Raggio interno (il foro)            |
| `normal`       | --          | Direzione della faccia              |

Un annulus è un disco piatto con un foro circolare al centro -- come una rondella o un anello visto dall'alto.

Alias del tipo: `annulus`, `ring_disk`.

---

## 4.9 Disk (Disco)

```yaml
- type: "disk"
  center: [0, 0, 0]
  radius: 1.0
  normal: [0, 1, 0]
  material: "white"
```

| Parametro | Predefinito | Descrizione                          |
|-----------|-------------|--------------------------------------|
| `center`  | `[0, 0, 0]` | Punto centrale                       |
| `radius`  | `1.0`       | Raggio del disco                     |
| `normal`  | --          | Direzione della faccia               |

Un disco circolare piatto e pieno. Usalo per sottobicchieri, monete o superfici circolari di tavoli. (È essenzialmente un annulus con `inner_radius: 0`.)

---

## 4.10 Triangle e Smooth Triangle (Triangolo e Triangolo Liscio)

### Triangolo Piatto (Triangle)

```yaml
- type: "triangle"
  v0: [-1, 0, 0]
  v1: [1, 0, 0]
  v2: [0, 1.5, 0]
  material: "red"
```

Tre vertici definiscono un triangolo piatto con una singola normale di superficie (calcolata dal prodotto vettoriale degli spigoli). L'ordine di avvolgimento determina quale lato è la faccia anteriore.

### Triangolo Liscio (Smooth Triangle)

```yaml
- type: "smooth_triangle"
  v0: [-1, 0, 0]
  v1: [1, 0, 0]
  v2: [0, 1.5, 0]
  n0: [-0.3, 0.2, -1]
  n1: [0.3, 0.2, -1]
  n2: [0, 1, -1]
  uv0: [0, 0]
  uv1: [1, 0]
  uv2: [0.5, 1]
  material: "textured"
```

| Parametro | Descrizione                                     |
|-----------|-------------------------------------------------|
| `v0`, `v1`, `v2` | Posizioni dei vertici                          |
| `n0`, `n1`, `n2` | Normali per vertice (per interpolazione Gouraud) |
| `uv0`, `uv1`, `uv2` | Coordinate texture per vertice              |

Quando sono fornite le normali per vertice, la normale della superficie viene interpolata uniformemente sulla faccia (Gouraud shading), nascondendo l'aspetto sfaccettato di una mesh triangolare. Le coordinate UV consentono una corretta mappatura della texture.

Raramente definirai triangoli a mano. Sono i mattoni delle mesh caricate da file OBJ (vedi Sezione 4.12).

---

## 4.11 Quad (Quadrilatero / Parallelogramma)

```yaml
- type: "quad"
  q: [-1, 0, -1]
  u: [2, 0, 0]
  v: [0, 0, 2]
  material: "checker_floor"
```

| Parametro | Descrizione                                       |
|-----------|---------------------------------------------------|
| `q`       | Un angolo del parallelogramma                     |
| `u`       | Primo vettore lato (da q)                         |
| `v`       | Secondo vettore lato (da q)                       |

Un quad è un parallelogramma piatto definito da un punto d'angolo e due vettori lato. I quattro vertici sono: `q`, `q+u`, `q+u+v`, `q+v`.

La normale della faccia è `cross(u, v)` (normalizzata). Questo è importante per i quad emissivi: emettono luce solo nella direzione della normale.

**Esempio: un quad pavimento** (2x2 metri centrato nell'origine):

```yaml
- type: "quad"
  q: [-1, 0, -1]
  u: [2, 0, 0]
  v: [0, 0, 2]
  material: "floor"
```

**Esempio: un pannello a parete:**

```yaml
- type: "quad"
  q: [-1.5, 0, 3]
  u: [3, 0, 0]
  v: [0, 2.5, 0]
  material: "wall"
```

I quad sono estremamente utili per i pannelli luminosi (quad emissivi), pareti, pavimenti con confini definiti e cornici di quadri.

---

## 4.12 Mesh (File OBJ)

```yaml
- type: "mesh"
  path: "models/teapot.obj"
  material: "porcelain"
  scale: 0.05
  translate: [0, 0, 0]
```

| Parametro | Descrizione                                        |
|-----------|----------------------------------------------------|
| `path`    | Percorso relativo del file OBJ                     |

Il caricatore di mesh legge file Wavefront OBJ. Supporta:

- **Facce triangolari** (flat shading)
- **Facce triangolari lisce** (quando le normali dei vertici sono presenti nell'OBJ)
- **Coordinate texture UV** dall'OBJ

Al caricamento, il motore costruisce automaticamente un BVH per i triangoli della mesh, rendendo i test di intersezione efficienti anche per mesh con milioni di triangoli.

Alias del tipo: `mesh`, `obj`.

Tutti i campi di trasformazione standard (`translate`, `rotate`, `scale`) funzionano sulle mesh. I modelli OBJ vengono spesso esportati a scale molto diverse, quindi lo `scale` è frequentemente necessario.

### 4.12.1 Superfici di Suddivisione (Loop / Catmull-Clark)

Quando l'OBJ è low-poly, il renderer può raffinarlo al caricamento usando gli stessi due algoritmi production-grade disponibili in Arnold, RenderMan, Cycles e nell'OpenSubdiv di Pixar. Il risultato è la superficie limite verso cui convergono le regole di subdivision: la silhouette diventa completamente liscia dopo poche iterazioni.

```yaml
# Mesh quad → Catmull-Clark
- type: "mesh"
  path: "models/cube.obj"
  material: "porcellana"
  subdivision_scheme: "catmull_clark"
  subdivision_iterations: 3

# Mesh triangolare → Loop
- type: "mesh"
  path: "models/icosa.obj"
  material: "rame"
  subdivision_scheme: "loop"
  subdivision_iterations: 4
```

| Campo                       | Default | Note |
|-----------------------------|---------|------|
| `subdivision_scheme`        | `none`  | `loop`, `catmull_clark`, `auto`, `none`. `auto` sceglie CC per input quad puro, Loop per triangoli puri, CC negli altri casi. |
| `subdivision_iterations`    | `0`     | Numero di iterazioni uniformi. Il numero di facce cresce di ~4× ad ogni passo. |
| `subdivision_pixel_error`   | `0`     | Target adattivo — il loader sceglie il numero di iterazioni che porta l'edge proiettato più lungo sotto questa soglia in pixel. |
| `subdivision_max_iterations`| `6`     | Tetto rigido per evitare esplosioni di memoria. |

Il loader stampa lo scheme e il numero di iterazioni effettivamente applicati:

```
Mesh: cubo_smussato — 768 faces, 8 vertices (subdivision: CatmullClark × 3)
```

Dietro le quinte il motore costruisce la topologia limite, ricalcola le normali per-vertice come media pesata sugli angoli delle facce incidenti (default di Blender/Maya) e poi emette i triangoli risultanti nel BVH interno della mesh. Le normali dell'OBJ vengono propagate attraverso le iterazioni di subdivision ma sostituite al momento della triangolazione finale perché la superficie limite è più liscia dell'input.

---

## 4.13 Riepilogo Alias dei Tipi

Molte primitive accettano più nomi di tipo:

| Nome primario    | Alias                                  |
|------------------|----------------------------------------|
| `sphere`         | --                                     |
| `box`            | --                                     |
| `infinite_plane` | `plane`                                |
| `cylinder`       | --                                     |
| `cone`           | `truncated_cone`, `frustum`            |
| `torus`          | `donut`                                |
| `capsule`        | `pill`                                 |
| `annulus`        | `ring_disk`                            |
| `disk`           | --                                     |
| `triangle`       | --                                     |
| `smooth_triangle`| --                                     |
| `quad`           | --                                     |
| `mesh`           | `obj`                                  |
| `group`          | --                                     |
| `instance`       | --                                     |
| `csg`            | --                                     |

---

## 4.14 Esempio Completo: Galleria delle Forme

Una scena che renderizza una di ogni primitiva in fila.

```yaml
# shape-gallery.yaml
# Ogni primitiva geometrica in una singola scena.

world:
  sky:
    type: "flat"
    color: [0.06, 0.06, 0.09]

cameras:
  - name: "main"
    position: [0, 5, -14]
    look_at: [0, 1, 0]
    fov: 55

lights:
  - type: "area"
    corner: [-5, 6, -4]
    u: [10, 0, 0]
    v: [0, 0, 8]
    color: [1, 0.97, 0.93]
    intensity: 25.0

  - type: "point"
    position: [5, 4, -6]
    color: [0.7, 0.8, 1.0]
    intensity: 30.0

materials:
  - id: "floor"
    type: "lambertian"
    color: [0.35, 0.35, 0.35]
  - id: "red"
    type: "disney"
    color: [0.85, 0.12, 0.1]
    roughness: 0.3
  - id: "blue"
    type: "disney"
    color: [0.1, 0.2, 0.85]
    roughness: 0.3
  - id: "green"
    type: "disney"
    color: [0.1, 0.7, 0.15]
    roughness: 0.3
  - id: "orange"
    type: "disney"
    color: [0.9, 0.45, 0.05]
    roughness: 0.3
  - id: "purple"
    type: "disney"
    color: [0.5, 0.1, 0.7]
    roughness: 0.3
  - id: "gold"
    type: "disney"
    color: [1.0, 0.76, 0.33]
    metallic: 1.0
    roughness: 0.1
  - id: "glass"
    type: "dielectric"
    refraction_index: 1.52
  - id: "cyan"
    type: "disney"
    color: [0.1, 0.7, 0.75]
    roughness: 0.3
  - id: "pink"
    type: "disney"
    color: [0.9, 0.3, 0.5]
    roughness: 0.3
  - id: "white"
    type: "disney"
    color: [0.9, 0.88, 0.85]
    roughness: 0.25
    specular: 0.6

entities:
  # Pavimento
  - type: "infinite_plane"
    point: [0, 0, 0]
    normal: [0, 1, 0]
    material: "floor"

  # Fila posteriore (da sinistra a destra): sphere, box, cylinder, cone, torus
  - type: "sphere"
    center: [-4, 0.7, 2]
    radius: 0.7
    material: "red"

  - type: "box"
    material: "blue"
    scale: [1.2, 1.2, 1.2]
    translate: [-2, 0.6, 2]
    rotate: [0, 25, 0]

  - type: "cylinder"
    center: [0, 0, 2]
    radius: 0.5
    height: 1.3
    material: "green"

  - type: "cone"
    center: [2, 0, 2]
    radius: 0.6
    top_radius: 0.0
    height: 1.4
    material: "orange"

  - type: "torus"
    major_radius: 0.6
    minor_radius: 0.2
    material: "gold"
    translate: [4, 0.2, 2]

  # Fila anteriore (da sinistra a destra): capsule, annulus, disk, quad, cono troncato
  - type: "capsule"
    center: [-4, 0, -1]
    radius: 0.3
    height: 0.7
    material: "purple"

  - type: "annulus"
    center: [-2, 0.01, -1]
    radius: 0.7
    inner_radius: 0.35
    normal: [0, 1, 0]
    material: "gold"

  - type: "disk"
    center: [0, 0.01, -1]
    radius: 0.7
    normal: [0, 1, 0]
    material: "cyan"

  - type: "quad"
    q: [1.3, 0, -1.7]
    u: [1.4, 0, 0]
    v: [0, 1.4, 0]
    material: "pink"

  - type: "cone"
    center: [4, 0, -1]
    radius: 0.6
    top_radius: 0.3
    height: 1.2
    material: "white"
```

Esegui il rendering con:

```
RayTracer -i shape-gallery.yaml -w 1600 -H 700 -s 256 -d 6
```

---

## Cosa si è imparato

- Il motore supporta 13 primitive geometriche più gruppi, istanze e operazioni CSG.
- Il **box** è un cubo unitario -- usa le trasformazioni per ogni dimensionamento e posizionamento.
- Il **torus** è definito nel piano XZ all'origine.
- I **piani infiniti** servono per superfici non delimitate (pavimenti, pareti).
- I **quad** servono per superfici piane delimitate (pannelli, cornici, emettitori di luce).
- Le **mesh** caricano file OBJ con costruzione automatica del BVH.
- La maggior parte delle primitive ha alias per comodità.

---

[Precedente: I materiali in dettaglio](./03-materials.md) | [Successivo: Trasformazioni, gruppi e organizzazione della scena](./05-transforms-and-groups.md) | [Indice del Tutorial](./README.md)
