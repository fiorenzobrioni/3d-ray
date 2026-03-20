# Tutorial: Creazione dei File di Scena (YAML)

## Indice
1. [Struttura del File](#1-struttura-del-file)
2. [Sezione `world`](#2-sezione-world)
3. [Sezione `camera`](#3-sezione-camera)
4. [Sezione `materials`](#4-sezione-materials)
   - [4.1 Lambertian (Opaco)](#41-lambertian-diffusoopaco)
   - [4.2 Metal (Metallico)](#42-metal-metallicospeculare)
   - [4.3 Dielectric (Vetro)](#43-dielectric-vetrotrasparente)
5. [Sezione `textures`](#5-sezione-textures)
   - [5.1 Tipi di Texture Procedurali](#51-tipi-di-texture-procedurali)
   - [5.2 Trasformazioni Spaziali (Offset & Rotation)](#52-trasformazioni-spaziali-offset--rotation)
   - [5.3 Randomizzazione per Oggetto](#53-randomizzazione-per-oggetto)
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
| `background` | `[R, G, B]` | `[0.5, 0.7, 1.0]` | Colore del cielo — agisce come illuminazione globale |
| `ground.type` | stringa | — | Sempre `"infinite_plane"` |
| `ground.material` | stringa | — | ID del materiale per il terreno |
| `ground.y` | float | `0.0` | Quota verticale del piano |

### Come funzionano le 3 sorgenti di illuminazione

Il renderer ha **tre fonti di luce** che lavorano insieme:

| Sorgente | Cosa controlla | Effetto |
|----------|---------------|---------|
| `background` | Colore del cielo | I raggi che rimbalzano sugli oggetti e "escono" dalla scena raccolgono questo colore. Agisce come una sorgente di luce ambiente globale (Global Illumination). |
| `ambient_light` | Luce piatta di riempimento | Viene **sommata** alla luce diretta su ogni punto colpito. Aiuta a schiarire le ombre. |
| `lights:` | Luci esplicite | Point, Directional, Spot, Area — illuminano selettivamente la scena. |

### Esempi di ambienti

**Scena notturna / studio nero (solo luci esplicite):**
```yaml
world:
  ambient_light: [0.0, 0.0, 0.0]
  background: [0.0, 0.0, 0.0]
```

**Scena diurna all'aperto:**
```yaml
world:
  ambient_light: [0.05, 0.05, 0.08]
  background: [0.4, 0.6, 1.0]
```

**Atmosfera calda al tramonto:**
```yaml
world:
  ambient_light: [0.05, 0.03, 0.01]
  background: [0.8, 0.4, 0.1]
```

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
| `fov` | float | `60` | Campo visivo verticale in gradi |
| `aperture` | float | `0` | Apertura: controlla la sfocatura DOF (0 = nitido ovunque) |
| `focal_dist` | float | `1` | Distanza a cui gli oggetti sono a fuoco |

### Effetti di Messa a Fuoco (Depth of Field)

Per ottenere lo **sfondo sfocato** tipico della fotografia ritrattistica o macro:
```yaml
camera:
  position: [0, 1, -5]
  look_at: [0, 0, 0]
  aperture: 0.3         # Valore alto = sfocatura pronunciata
  focal_dist: 5.0       # Messa a fuoco a 5 unità (esattamente sul soggetto)
```

Per un'immagine **completamente nitida** (stile videogioco o rendering tecnico):
```yaml
camera:
  aperture: 0.0          # Nessuna sfocatura
```

### Dutch Angle (Camera Inclinata)
Per un effetto cinematografico "inclinato":
```yaml
camera:
  vup: [0.2, 1, 0]      # Inclina leggermente l'orizzonte
```

---

## 4. Sezione `materials`

Ogni materiale ha un `id` univoco e un `type` fisico. Puoi usare `texture` invece di `color` per pattern avanzati.

### 4.1 — Lambertian (Diffuso/Opaco)

Superficie opaca che diffonde la luce uniformemente in tutte le direzioni. Ideale per muri, terreno, tessuti e oggetti colorati non riflettenti.

```yaml
materials:
  - id: "rosso_opaco"
    type: "lambertian"
    color: [0.8, 0.1, 0.1]    # Rosso intenso
```

| Campo | Tipo | Descrizione |
|-------|------|-------------|
| `color` | `[R, G, B]` | Colore diffuso (0–1 per canale) |
| `texture` | oggetto | Pattern procedurale (alternativo a `color`) |

**Esempi di colori naturali consigliati:**
```yaml
  - id: "erba"
    type: "lambertian"
    color: [0.2, 0.6, 0.15]
  - id: "terra"
    type: "lambertian"
    color: [0.55, 0.35, 0.2]
  - id: "azzurro_cielo"
    type: "lambertian"
    color: [0.4, 0.6, 0.9]
  - id: "bianco_pulito"
    type: "lambertian"
    color: [0.95, 0.95, 0.95]
```

### 4.2 — Metal (Metallico/Speculare)

Superficie riflettente come specchi o metalli. Il parametro `fuzz` controlla la rugosità superficiale.

```yaml
  - id: "rame_lucido"
    type: "metal"
    color: [0.85, 0.5, 0.25]
    fuzz: 0.05
```

| Campo | Tipo | Descrizione |
|-------|------|-------------|
| `color` | `[R, G, B]` | Colore metallico |
| `fuzz` | float (`0.0`–`1.0`) | Rugosità superficiale |
| `texture` | oggetto | Pattern procedurale (alternativo a `color`) |

| Range `fuzz` | Effetto Visivo | Uso Suggerito |
|--------------|----------------|---------------|
| `0.0` | Specchio perfetto | Specchi, superfici cromate |
| `0.01 – 0.05` | Metallo lucido | Acciaio inox, carrozzerie |
| `0.1 – 0.3` | Metallo satinato | Alluminio spazzolato, ottone |
| `0.5 – 1.0` | Metallo grezzo | Piombo, ferro arrugginito |

### 4.3 — Dielectric (Vetro/Trasparente)

Materiale trasparente che rifrange la luce. Molto realistico per liquidi, vetrate e cristalli colorati. Implementa il modello di Fresnel (approssimazione di Schlick) per riflessioni angolo-dipendenti.

Supporta `color` e `texture` per creare effetti di **Surface Tinting** (vetro colorato).

```yaml
  - id: "vetro_fume"
    type: "dielectric"
    refraction_index: 1.5
    color: [0.2, 0.2, 0.2]    # Tintura grigio scuro
```

| Campo | Tipo | Descrizione |
|-------|------|-------------|
| `refraction_index` | float | Indice di rifrazione (IOR) |
| `color` | `[R, G, B]` | Tintura superficiale (opzionale, default bianco = vetro neutro) |
| `texture` | oggetto | Pattern procedurale trasparente (opzionale) |

**Indici di rifrazione (IOR) comuni:**

| Materiale | IOR |
|-----------|-----|
| Aria | 1.0 |
| Acqua | 1.33 |
| Vetro Standard | 1.52 |
| Cristallo | 1.6 |
| Diamante | 2.42 |

> **💡 Suggerimento Professionale:** Puoi usare una `texture` di tipo `marble` su un `dielectric` per creare un incredibile effetto **"Marmo di Cristallo"** semitrasparente!

---

## 5. Sezione `textures`

Le texture permettono di mappare pattern complessi sulla superficie degli oggetti. Possono essere applicate a materiali `lambertian`, `metal` o `dielectric` sostituendo il parametro `color` con `texture`.

### 5.1 Tipi di Texture Procedurali

#### **Checker (Scacchiera)**
Pattern 3D a quadrati alternati nello spazio 3D. Il parametro `scale` controlla la dimensione dei quadrati.
```yaml
    texture:
      type: "checker"
      scale: 2.0
      colors: [[0.1, 0.1, 0.1], [0.9, 0.9, 0.9]]
```
| Campo | Tipo | Descrizione |
|-------|------|-------------|
| `scale` | float | Dimensione dei quadrati. Valori **più grandi** producono quadrati **più grandi**. |
| `colors` | `[[R,G,B], [R,G,B]]` | I due colori alternati (pari e dispari) |

> **⚠️ Nota tecnica:** La checker è valutata nello spazio 3D (non UV). Un `scale: 1.0` produce quadrati di 1 unità di scena, `scale: 0.5` produce quadrati di mezzo metro. Valori consigliati: `1.0–5.0` per pavimenti, `0.1–0.5` per pattern fini su sfere.

#### **Noise (Perlin Noise)**
Genera un rumore smussato per effetti naturali, sporcizia o rugosità. Produce un colore in scala di grigi.
```yaml
    texture:
      type: "noise"
      scale: 5.0
```
| Campo | Tipo | Descrizione |
|-------|------|-------------|
| `scale` | float | Frequenza del rumore. Più alto = dettagli più fini e frequenti. |

#### **Marble (Marmo)**
Simula venature di marmo striate usando la turbolenza di Perlin.
```yaml
    texture:
      type: "marble"
      scale: 7.0
      noise_strength: 15.0
      colors: [[0.95, 0.95, 0.95], [0.1, 0.2, 0.3]]
```
| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `scale` | float | `4.0` | Frequenza delle venature. Più alto = venature più fitte. |
| `noise_strength` | float | `10.0` | Controlla quanto le venature sono "distorte" e irregolari. |
| `colors` | `[[R,G,B], [R,G,B]]` | — | `[0]` = colore base (prevalente), `[1]` = colore venature |

#### **Wood (Legno)**
Genera anelli di accrescimento concentrici attorno all'asse Y (nel sistema di riferimento locale dell'oggetto).
```yaml
    texture:
      type: "wood"
      scale: 12.0
      noise_strength: 3.5
      colors: [[0.85, 0.65, 0.40], [0.60, 0.40, 0.20]]
```
| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `scale` | float | `4.0` | Densità degli anelli. Più alto = anelli più fitti. |
| `noise_strength` | float | `2.0` | Irregolarità degli anelli. Valori alti distorcono molto gli anelli. |
| `colors` | `[[R,G,B], [R,G,B]]` | — | `[0]` = legno chiaro (tra gli anelli), `[1]` = legno scuro (anelli) |

### 5.2 Trasformazioni Spaziali (Offset & Rotation)

Puoi manipolare come la texture "avvolge" l'oggetto senza cambiare la posizione dell'oggetto stesso. Disponibili per `noise`, `marble` e `wood`.

- **`offset`**: `[X, Y, Z]` per traslare il pattern nello spazio locale.
- **`rotation`**: `[X, Y, Z]` (in gradi) per ruotare le venature (ordine: X, poi Y, poi Z).

**Esempio: Legno con venature orizzontali su un piano visto dall'alto**
```yaml
    texture:
      type: "wood"
      rotation: [90, 0, 0]   # Ruota gli anelli, che per default sono attorno a Y
      scale: 10.0
```

**Esempio: Marmo con venature verticali (pilastro)**
```yaml
    texture:
      type: "marble"
      rotation: [0, 0, 90]
      scale: 8.0
```

### 5.3 Randomizzazione per Oggetto

Questa funzione permette di usare **lo stesso materiale** su più oggetti ma con venature **diverse** per ognuno. Il motore usa il `seed` di ogni oggetto per generare offset e rotazioni deterministici — lo stesso seed produce sempre lo stesso risultato tra render diversi.

- **`randomize_offset: true`**: sposta la texture in modo pseudo-casuale unico per ogni oggetto.
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
    q: [0, 0, 5]          # Punto d'origine (angolo)
    u: [4, 0, 0]           # Vettore primo lato (larghezza)
    v: [0, 3, 0]           # Vettore secondo lato (altezza)
    material: "muro"
```
| Campo | Tipo | Descrizione |
|-------|------|-------------|
| `q` | `[X, Y, Z]` | Punto d'origine (angolo) del quad |
| `u` | `[X, Y, Z]` | Vettore del primo lato |
| `v` | `[X, Y, Z]` | Vettore del secondo lato |

> I 4 vertici risultanti sono: Q, Q+U, Q+V, Q+U+V.

### 6.6 Disk (Disco)
Un disco piatto definito da centro, normale e raggio.
```yaml
  - name: "base_circolare"
    type: "disk"
    center: [0, 0, 0]
    normal: [0, 1, 0]
    radius: 2.0
    material: "metallo"
```
| Campo | Tipo | Descrizione |
|-------|------|-------------|
| `center` | `[X, Y, Z]` | Centro del disco |
| `normal` | `[X, Y, Z]` | Direzione della normale (orientazione del disco) |
| `radius` | float | Raggio |

### 6.7 Plane / Infinite Plane (Piano Infinito)
Un piano infinito definito da un punto e una normale. Utile per pavimenti o pareti senza bordi visibili.
```yaml
  - name: "pavimento"
    type: "infinite_plane"
    point: [0, 0, 0]
    normal: [0, 1, 0]
    material: "terreno"
```
| Campo | Tipo | Descrizione |
|-------|------|-------------|
| `point` | `[X, Y, Z]` | Un punto qualsiasi sul piano |
| `normal` | `[X, Y, Z]` | Direzione perpendicolare al piano |

> Il piano infinito è anche definibile nella sezione `world.ground` per comodità. I piani infiniti sono esclusi dall'accelerazione BVH e testati separatamente, il che è corretto poiché non hanno un bounding box finito.

### 6.8 Trasformazioni (Translate, Rotate, Scale)

Le trasformazioni vengono applicate **nell'ordine: Scale → Rotate → Translate**. Sono disponibili per tutte le primitive e sono **obbligatorie** per il Box (che è un cubo unitario da trasformare).

```yaml
  - name: "cubo_ruotato"
    type: "box"
    scale: [2, 1, 1]            # Allungato in X
    rotate: [0, 45, 0]          # Ruotato di 45° attorno a Y
    translate: [3, 0.5, 0]      # Posizionato a X=3, con centro a Y=0.5
    material: "legno"
```

| Campo | Tipo | Descrizione |
|-------|------|-------------|
| `scale` | `[X, Y, Z]` o `float` | Dimensionamento (vettore o uniforme) |
| `rotate` | `[X, Y, Z]` | Rotazione in gradi (ordine: X poi Y poi Z) |
| `translate` | `[X, Y, Z]` | Traslazione nel mondo |

> **Nota tecnica:** Le normali sono trasformate correttamente usando la matrice **inversa trasposta** della trasformazione. Questo garantisce normali corrette anche in presenza di scaling non uniforme.

### 6.9 Parametro Seed

Ogni entità può avere un `seed` opzionale (intero). Se non specificato, il motore assegna un seed casuale ad ogni render. Il seed viene usato dalle texture con `randomize_offset` o `randomize_rotation` per generare variazioni uniche per-oggetto in modo **deterministico** — lo stesso seed produce sempre le stesse venature.

```yaml
  - name: "sfera_1"
    type: "sphere"
    center: [0, 1, 0]
    radius: 1.0
    seed: 42            # Seed fisso → venature identiche tra render diversi
    material: "marmo"
```

---

## 7. Sezione `lights`

> **Default automatico:** Se la sezione `lights` è assente o vuota, il motore aggiunge automaticamente una `directional` e una `point` light di base.

### 7.1 Point Light (Puntiforme)
Luce che irradia in tutte le direzioni da un punto. Attenuazione con il quadrato della distanza (`Intensity / d²`).
```yaml
  - type: "point"
    position: [0, 10, -5]
    color: [1, 1, 1]
    intensity: 10.0
```
| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `position` | `[X, Y, Z]` | `[0, 10, 0]` | Posizione nel mondo |
| `color` | `[R, G, B]` | `[1, 1, 1]` | Colore della luce |
| `intensity` | float | `1.0` | Intensità. Valori tipici: 4–20. |

### 7.2 Directional Light (Sole)
Luce parallela infinita (come il sole). Non ha attenuazione con la distanza.
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
| `shadow_samples` | int | `16` | Raggi ombra per punto. 8=preview, 16=produzione, 32=qualità massima. |

> **Alias:** Puoi usare anche `type: "area_light"`, `type: "rect"` o `type: "rect_light"`.

> **⚠️ Costo computazionale:** Il `shadow_samples` ha un impatto diretto sul tempo di render. Usa `shadow_samples: 4` durante il draft e `shadow_samples: 16-32` per il render finale. Con `-s 128` campioni pixel e `shadow_samples: 16`, ogni pixel lancia `128 × 16 = 2048` raggi ombra per questa sola luce.

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
2. Esegui un preview rapido (`-s 1 --width 400`).
3. Se l'immagine è sovraesposta, **dimezza tutte le intensità** e ripeti.
4. Se è sottoesposta, **raddoppiale** e ripeti.
5. Quando l'esposizione globale è corretta, bilancia le singole sorgenti tra loro.
6. Tieni nota dei valori finali: potrai riusarli come punto di partenza per scene simili.

---

## 8. Illuminazione: Come Funziona

### Il rendering è un path tracer
Ogni pixel spara raggi nella scena. Quando un raggio colpisce una superficie, il materiale genera un raggio rimbalzato. Il processo continua fino a:
- Il raggio esce dalla scena → riceve il colore del `background` (= luce del cielo)
- Il raggio raggiunge la profondità massima (`--depth`) → restituisce nero

Questo significa che il `background` è effettivamente una **sorgente di luce**. Se vuoi una scena dove solo le luci esplicite illuminano, devi impostare `background: [0, 0, 0]`.

### Combinazioni di Luci Consigliate

**Esterno Diurno:**
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

---

## 10. Regole e Best Practices

### Sintassi e Funzionamento
1. **Colori:** Nelle texture usa sempre una lista di liste: `colors: [[R,G,B], [R,G,B]]`.
2. **Coordinate:** Y positivo è sempre verso l'alto.
3. **Box:** Usa sempre `scale` + `translate` per i box. Il cubo unitario ha centro nell'origine.
4. **IDs materiale:** Ogni `id` deve essere univoco. I riferimenti `material` nelle entità sono **case-sensitive** e devono corrispondere esattamente. Un ID non trovato produce un materiale grigio di fallback senza errore.
5. **BVH:** Il motore ottimizza automaticamente le scene con più di 4 oggetti usando una BVH basata sull'asse con maggiore estensione dei centroidi.
6. **Luci di default:** Se non specifichi nessuna luce nella sezione `lights`, il motore aggiunge automaticamente una directional + una point light.
7. **Area Light:** I campi `corner`, `u` e `v` sono tutti obbligatori. Se uno è mancante, la luce viene saltata con un warning in console.

### Performance
8. **Campioni e area light:** Il costo reale per pixel è `samples × shadow_samples` per ogni area light. Con `-s 128` e `shadow_samples: 16` su una sola area light, ogni pixel lancia oltre 2000 raggi. Usa `shadow_samples: 4` per il draft.
9. **Vetro e dielettrico:** I materiali dielettrici (vetro) sono i più costosi perché ogni rimbalzo può generare sia riflessione che rifrazione. Aumenta `--depth` per scene con molto vetro.

### Checklist prima del render finale

- [ ] Tutti gli `id` dei materiali sono univoci e referenziati correttamente nelle entità.
- [ ] La `camera.position` non si trova all'interno di un oggetto solido.
- [ ] Le texture con variazioni per-oggetto hanno `randomize_offset` o `randomize_rotation` attivo.
- [ ] Il file YAML usa correttamente gli **spazi** per l'indentazione (niente TAB).
- [ ] È stata eseguita un'anteprima a bassa risoluzione (`--width 400 -s 1`).
- [ ] Le area light hanno `corner`, `u` e `v` tutti definiti.
- [ ] Se la scena deve essere buia, `background` è `[0, 0, 0]`.
- [ ] I seed degli oggetti con texture randomizzate sono fissi (se vuoi risultati riproducibili tra render).
