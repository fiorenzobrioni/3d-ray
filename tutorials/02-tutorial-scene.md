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

> **Nota:** I colori sono sempre espressi come `[R, G, B]` con valori da `0.0` a `1.0`. Le coordinate usano il sistema: **X** = destra, **Y** = alto, **Z** = verso la camera (negativo = lontano).

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
| `lights:` | Luci esplicite | Point, Directional, Spot — illuminano selettivamente la scena. |

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

Materiale trasparente che rifrange la luce. Molto realistico per liquidi, vetrate e cristalli colorati.

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
| `color` | `[R, G, B]` | Tintura superficiale (opzionale, default bianco) |
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
Pattern 3D a quadrati alternati. Il parametro `scale` controlla la dimensione dei quadrati.
```yaml
    texture:
      type: "checker"
      scale: 2.0
      colors: [[0.1, 0.1, 0.1], [0.9, 0.9, 0.9]]
```
| Campo | Tipo | Descrizione |
|-------|------|-------------|
| `scale` | float | Dimensione dei quadrati. Più piccolo = quadrati più grandi. |
| `colors` | `[[R,G,B], [R,G,B]]` | I due colori alternati (pari e dispari) |

#### **Noise (Perlin Noise)**
Genera un rumore smussato per effetti naturali, sporcizia o rugosità. Produce un colore in scala di grigi.
```yaml
    texture:
      type: "noise"
      scale: 5.0
```
| Campo | Tipo | Descrizione |
|-------|------|-------------|
| `scale` | float | Frequenza del rumore. Più alto = dettagli più fini. |

#### **Marble (Marmo)**
Simula venature di marmo striate usando la turbolenza matematica.
```yaml
    texture:
      type: "marble"
      scale: 7.0
      noise_strength: 15.0
      colors: [[0.95, 0.95, 0.95], [0.1, 0.2, 0.3]]
```
| Campo | Tipo | Descrizione |
|-------|------|-------------|
| `scale` | float | Frequenza delle venature. Più alto = venature più fitte. |
| `noise_strength` | float | Controlla quanto le venature sono "distorte" e irregolari. |
| `colors` | `[[R,G,B], [R,G,B]]` | `[0]` = colore base, `[1]` = colore venature |

#### **Wood (Legno)**
Genera anelli di accrescimento concentrici (default attorno ad asse Y).
```yaml
    texture:
      type: "wood"
      scale: 12.0
      noise_strength: 3.5
      colors: [[0.4, 0.2, 0.1], [0.2, 0.1, 0.05]]
```
| Campo | Tipo | Descrizione |
|-------|------|-------------|
| `scale` | float | Densità degli anelli. Più alto = anelli più fitti. |
| `noise_strength` | float | Irregolarità degli anelli. |
| `colors` | `[[R,G,B], [R,G,B]]` | `[0]` = legno chiaro, `[1]` = legno scuro |

### 5.2 Trasformazioni Spaziali (Offset & Rotation)

Puoi manipolare come la texture "avvolge" l'oggetto senza cambiare la posizione dell'oggetto stesso. Disponibili per `noise`, `marble` e `wood`.

- **`offset`**: `[X, Y, Z]` per traslare il pattern nello spazio.
- **`rotation`**: `[X, Y, Z]` (in gradi) per ruotare le venature.

**Esempio: Legno con venature orizzontali (tavolo visto dall'alto)**
```yaml
    texture:
      type: "wood"
      rotation: [90, 0, 0]
      scale: 10.0
```

### 5.3 Randomizzazione per Oggetto

Questa funzione permette di usare **lo stesso materiale** su più oggetti ma con venature **diverse** per ognuno. Il motore usa il `seed` di ogni oggetto per generare offset e rotazioni deterministici.

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
| `center` | `[X, Y, Z]` | Centro della sfera |
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
| `scale` | `[X, Y, Z]` | Dimensioni del box (largezza, altezza, profondità) |
| `translate` | `[X, Y, Z]` | Posizione del **centro** del box |
| `rotate` | `[X, Y, Z]` | Rotazione in gradi (opzionale) |

> **⚠️ Importante:** Il `translate` posiziona il **centro** del box. Se vuoi che la base del box sia a Y=0, devi traslare di `altezza / 2` in Y. Esempio: un box alto 1.0 con base a terra → `translate: [0, 0.5, 0]`.

### 6.3 Cylinder (Cilindro)
Cilindro finito allineato all'asse Y, con dischi di chiusura in alto e in basso.
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
Triangolo definito da tre vertici. Usa l'algoritmo Möller-Trumbore per l'intersezione.
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
    q: [0, 0, 5]          # Punto d'origine
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
Un piano infinito definito da un punto e una normale. Utile per pavimenti o pareti senza bordi.
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

> Il piano infinito è anche definibile nella sezione `world.ground` per comodità.

### 6.8 Trasformazioni (Translate, Rotate, Scale)

Le trasformazioni vengono applicate **nell'ordine: Scale → Rotate → Translate**. Sono disponibili per tutte le primitive, ma sono **obbligatorie** per il Box (che è un cubo unitario da trasformare).

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

### 6.9 Parametro Seed

Ogni entità può avere un `seed` opzionale. Se non specificato, il motore assegna un seed casuale. Il seed viene usato dalle texture con `randomize_offset` o `randomize_rotation` per generare variazioni uniche per-oggetto.

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

### 7.1 Point Light (Puntiforme)
Luce che irradia in tutte le direzioni da un punto. Attenuazione con il quadrato della distanza.
```yaml
  - type: "point"
    position: [0, 10, -5]
    color: [1, 1, 1]
    intensity: 100.0
```
| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `position` | `[X, Y, Z]` | `[0, 10, 0]` | Posizione nel mondo |
| `color` | `[R, G, B]` | `[1, 1, 1]` | Colore della luce |
| `intensity` | float | `1.0` | Intensità. Valori tipici: 40–200. |

### 7.2 Directional Light (Sole)
Luce parallela infinita (come il sole). Non ha attenuazione con la distanza.
```yaml
  - type: "directional"
    direction: [-1, -1, -1]     # Direzione DA cui arriva la luce
    color: [1, 1, 0.9]
    intensity: 0.8
```
| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `direction` | `[X, Y, Z]` | `[-1, -1, -1]` | Direzione della luce (viene normalizzata) |
| `color` | `[R, G, B]` | `[1, 1, 1]` | Colore |
| `intensity` | float | `1.0` | Intensità. Valori tipici: 0.3–2.0. |

> **Alias:** Puoi usare anche `type: "sun"` come alias per `"directional"`.

### 7.3 Spot Light (Faretto)
Luce conica con posizione e direzione. Ha un cono interno (piena intensità) e un cono esterno (sfumatura).
```yaml
  - type: "spot"
    position: [0, 8, -3]
    direction: [0, -1, 0]       # Punta verso il basso
    color: [1.0, 0.95, 0.9]
    intensity: 120
    inner_angle: 15              # Angolo cono interno (gradi)
    outer_angle: 30              # Angolo cono esterno (gradi)
```
| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `position` | `[X, Y, Z]` | `[0, 10, 0]` | Posizione del faretto |
| `direction` | `[X, Y, Z]` | `[0, -1, 0]` | Direzione verso cui punta |
| `color` | `[R, G, B]` | `[1, 1, 1]` | Colore della luce |
| `intensity` | float | `1.0` | Intensità. Valori tipici: 60–300. |
| `inner_angle` | float | `15` | Mezzo-angolo del cono interno (piena intensità), in gradi |
| `outer_angle` | float | `30` | Mezzo-angolo del cono esterno (sfumatura a zero), in gradi |

> **Alias:** Puoi usare anche `type: "spotlight"`.

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
    intensity: 0.9
  - type: "point"
    position: [0, 20, 0]
    intensity: 100
```

**Studio con Spot:**
```yaml
lights:
  - type: "spot"
    position: [0, 8, -5]
    direction: [0, -1, 0.5]
    color: [1.0, 0.95, 0.9]
    intensity: 150
    inner_angle: 20
    outer_angle: 40
```

**Interno Intimo:**
```yaml
lights:
  - type: "point"
    position: [0, 3, 0]
    color: [1.0, 0.8, 0.5]
    intensity: 40
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
```

### 9.2 — Scena Architettonica con Geometrie Miste e SpotLight

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
  - type: "spot"
    position: [0, 10, 0]
    direction: [0, -1, 0]
    intensity: 200
    inner_angle: 25
    outer_angle: 45
  - type: "point"
    position: [-5, 3, -5]
    color: [0.8, 0.8, 1.0]
    intensity: 30

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
3. **Box:** Usa sempre `scale` + `translate` per i box. Il centro del cubo unitario è l'origine.
4. **Performance:** Le sfere e i box di vetro sono le più costose da renderizzare. Usa `-s 1` per test rapidi.
5. **BVH:** Il motore ottimizza automaticamente le scene con più di 4 oggetti usando una BVH basata sull'asse più lungo.
6. **Luci di default:** Se non specifichi nessuna luce nella sezione `lights`, il motore aggiunge automaticamente una directional + una point light.

### Checklist prima del render finale

- [ ] Tutti gli `id` dei materiali sono univoci e referenziati correttamente.
- [ ] La `camera.position` non si trova all'interno di un oggetto solido.
- [ ] Le texture che richiedono variazioni hanno `randomize_offset` o `randomize_rotation` attivo.
- [ ] Il file YAML usa correttamente gli spazi per l'indentazione (niente TAB).
- [ ] È stata eseguita un'anteprima a bassa risoluzione (`--width 400 -s 1`).
- [ ] Se la scena deve essere buia, `background` è `[0, 0, 0]`.
