# Tutorial: Creazione dei File di Scena (YAML)

## Indice
1. [Struttura del File](#1-struttura-del-file)
2. [Sezione `world`](#2-sezione-world)
3. [Sezione `camera`](#3-sezione-camera)
4. [Sezione `materials`](#4-sezione-materials)
   - [4.1 Lambertian (Opaco)](#41-lambertian-diffusoopaco)
   - [4.2 Metal (Metallico)](#42-metal-metallicospeculare)
   - [4.3 Dielectric (Vetro)](#43-dielectric-vetrotrasparente)
5. [Sezione `textures` (Novità Professionali)](#5-sezione-textures-novità-professionali)
   - [5.1 Tipi di Texture Procedurali](#51-tipi-di-texture-procedurali)
   - [5.2 Trasformazioni Spaziali (Offset & Rotation)](#52-trasformazioni-spaziali-offset--rotation)
   - [5.3 Randomizzazione per Oggetto](#53-randomizzazione-per-oggetto)
6. [Sezione `entities`](#6-sezione-entities)
7. [Sezione `lights`](#7-sezione-lights)
8. [Esempi Completi](#8-esempi-completi)
9. [Regole e Best Practices](#9-regole-e-best-practices)

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
| `fov` | float | `60` | Campo visivo verticale |
| `aperture` | float | `0` | Apertura: controlla la sfocatura (0 = nitido ovunque) |
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

Per un'immagine **completamente nitida** (stile videogioco o rendering isometrico):
```yaml
camera:
  aperture: 0.0          # Nessuna sfocatura
  focal_dist: 1.0        # Valore ignorato
```

---

## 4. Sezione `materials`

Ogni materiale ha un `id` univoco e un `type` fisico. Novità: ora puoi usare `texture` invece di `color` per pattern avanzati.

### 4.1 — Lambertian (Opaco)

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

| Range `fuzz` | Effetto Visivo | Uso Suggerito |
|--------------|----------------|---------------|
| `0.0` | Specchio perfetto | Specchi, superfici cromate |
| `0.01 - 0.05` | Metallo lucido | Acciaio inox, carrozzerie |
| `0.1 - 0.3` | Metallo satinato | Alluminio spazzolato, ottone |
| `0.5 - 1.0` | Metallo grezzo | Piombo, ferro arrugginito |

### 4.3 — Dielectric (Vetro/Trasparente)

Materiale trasparente che rifrange la luce. Molto realistico per liquidi, vetrate e cristalli colorati.

**Novità:** Ora supporta i parametri `color` e `texture` per creare effetti di **Surface Tinting** (vetro colorato).

```yaml
  - id: "vetro_fume"
    type: "dielectric"
    refraction_index: 1.5
    color: [0.2, 0.2, 0.2]    # Tintura grigio scuro
```

| Campo | Tipo | Descrizione |
|-------|------|-------------|
| `refraction_index` | float | Indice di rifrazione (IOR) |
| `color` | `[R, G, B]` | Tintura superficiale (opzionale) |
| `texture` | oggetto | Pattern procedurale trasparente (opzionale) |

**Indici di rifrazione (IOR) comuni:**
- **Acqua**: 1.33
- **Vetro Standard**: 1.52
- **Diamante**: 2.42

> **💡 Suggerimento Professionale:** Puoi usare una `texture` di tipo `marble` su un `dielectric` per creare un incredibile effetto **"Marmo di Cristallo"** semitrasparente!

---

## 5. Sezione `textures` (Novità Professionali)

Le texture permettono di mappare pattern complessi sulla superficie degli oggetti. Possono essere applicate a materiali `lambertian`, `metal` o `dielectric` sostituendo il parametro `color` con `texture`.

### 5.1 Tipi di Texture Procedurali

#### **Checker (Scacchiera)**
Pattern 3D a quadrati alternati.
```yaml
    texture:
      type: "checker"
      scale: 2.0
      colors: [[0.1, 0.1, 0.1], [0.9, 0.9, 0.9]]
```

#### **Noise (Perlin Noise)**
Genera un rumore smussato per effetti naturali o sporcizia.
```yaml
    texture:
      type: "noise"
      scale: 5.0
```

#### **Marble (Marmo)**
Simula venature di marmo striate usando la turbolenza matematica.
```yaml
    texture:
      type: "marble"
      scale: 7.0
      noise_strength: 15.0 # Controlla quanto le vene sono "storte"
      colors: [[0.95, 0.95, 0.95], [0.1, 0.2, 0.3]]
```

#### **Wood (Legno)**
Genera anelli di accrescimento concentrici (default attorno ad asse Y).
```yaml
    texture:
      type: "wood"
      scale: 12.0
      noise_strength: 3.5
      colors: [[0.4, 0.2, 0.1], [0.2, 0.1, 0.05]]
```

### 5.2 Trasformazioni Spaziali (Offset & Rotation)

Puoi manipolare come la texture "avvolge" l'oggetto senza cambiare la posizione dell'oggetto stesso.
- **`offset`**: `[X, Y, Z]` per traslare il pattern.
- **`rotation`**: `[X, Y, Z]` (in gradi) per ruotare le venature.

**Esempio: Legno con venature orizzontali (sdraiato)**
```yaml
    texture:
      type: "wood"
      rotation: [90, 0, 0] # Ruota la texture attorno a X
      scale: 10.0
```

### 5.3 Randomizzazione per Oggetto

Questa funzione permette di usare **lo stesso materiale** su più oggetti ma con venature diverse per ognuno.
- **`randomize_offset`**: sposta la texture in modo casuale per ogni oggetto.
- **`randomize_rotation`**: ruota la texture in modo casuale per ogni oggetto.

---

## 6. Sezione `entities`

Gli oggetti 3D nella scena. Ora ogni entità supporta il parametro `seed`.

```yaml
entities:
  - name: "sfera_principale"
    type: "sphere"
    center: [0, 1, 0]
    radius: 1.0
    material: "marmo_bianco"
    seed: 42 # Seed fisso per rendere le venature costanti tra i render
```

### Parametro `seed`
Se non specificato, il motore assegna un `seed` casuale ad ogni oggetto. Se attivata la randomizzazione nel materiale, due oggetti con seed diversi mostreranno parti diverse della texture (es. un diverso blocco di legno o di marmo).

### Primitive
- **Sphere:** `center`, `radius`
- **Box:** `min`, `max` (allineato agli assi)
- **Cylinder:** `center` (base), `radius`, `height`
- **Triangle:** `v0`, `v1`, `v2`
- **Plane:** `point`, `normal` (piano infinito)

---

## 7. Sezione `lights`

### 7.1 Point Light (Puntiforme)
```yaml
  - type: "point"
    position: [0, 10, -5]
    color: [1, 1, 1]
    intensity: 100.0
```

### 7.2 Directional Light (Sole)
```yaml
  - type: "directional"
    direction: [-1, -1, -1] # Direzione DA cui arriva la luce
    color: [1, 1, 0.9]
    intensity: 0.8
```

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
    intensity: 100 # Riempimento ombre
```

**Interno Intimo:**
```yaml
lights:
  - type: "point"
    position: [0, 3, 0]
    color: [1.0, 0.8, 0.5] # Luce calda
    intensity: 40
```

---

## 8. Esempi Completi

### 8.1 — Showcase Materiali (Confronto)

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

### 8.2 — Variazione Naturale (Pezzi di marmo unici)
Utilizzo di un solo materiale per generare sfere di marmo tutte diverse.

```yaml
world:
  ambient_light: [0.1, 0.1, 0.1]
  background: [0.05, 0.05, 0.1]
  ground: { type: "infinite_plane", material: "pavimento", y: 0 }

camera:
  position: [0, 5, -12]
  look_at: [0, 1, 0]

materials:
  - id: "pavimento"
    type: "lambertian"
    texture: { type: "checker", scale: 4.0, colors: [[0.1, 0.1, 0.1], [0.2, 0.2, 0.2]] }
  - id: "marmo_professionale"
    type: "metal"
    fuzz: 0.05
    texture:
      type: "marble"
      scale: 10.0
      randomize_offset: true   # Rende ogni sfera unica
      randomize_rotation: true # Rende ogni sfera unica

entities:
  - name: "marmo_1"
    type: "sphere"
    center: [-2, 1, 0]
    radius: 1
    material: "marmo_professionale"
  - name: "marmo_2"
    type: "sphere"
    center: [2, 1, 0]
    radius: 1
    material: "marmo_professionale"
```

### 8.3 — Scena Architettonica con Geometrie Miste
Questo esempio mostra come combinare cilindri, box e texture professionali (Vetro e Marmo) per creare una struttura greca classica.

```yaml
world:
  ambient_light: [0.08, 0.08, 0.1]
  background: [0.35, 0.55, 0.95]
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
  - type: "directional"
    direction: [-0.5, -1, -0.5]
    intensity: 0.8

entities:
  # Colonne in marmo (ognuna avrà venature diverse grazie alla randomizzazione)
  - { name: "col_sx", type: "cylinder", center: [-3, 0, 0], radius: 0.3, height: 4, material: "marmo_colonna" }
  - { name: "col_dx", type: "cylinder", center: [3, 0, 0], radius: 0.3, height: 4, material: "marmo_colonna" }
  
  # Architrave (trave orizzontale)
  - { name: "trave", type: "box", min: [-3.5, 4, -0.4], max: [3.5, 4.5, 0.4], material: "marmo_colonna" }

  # Sfera decorativa in cristallo
  - { name: "gioiello", type: "sphere", center: [0, 2, 0], radius: 0.8, material: "vetro_cristallo" }
```

---

## 9. Regole e Best Practices

### Sintassi e Funzionamento
1.  **Indici di Rifrazione:** Vetro = 1.5, Acqua = 1.33, Diamante = 2.42.
2.  **Colori:** Nelle texture usa sempre una lista di liste per i colori: `colors: [[R,G,B], [R,G,B]]`.
3.  **Coordinate:** Y positivo è sempre verso l'alto. Se il terreno è a Y=0, i tuoi oggetti dovrebbero avere coordinate Y positive.
4.  **Performance:** Le sfere di vetro sono le più costose da renderizzare. Usa `--samples 1` per test rapidi di inquadratura.
5.  **BVH:** Il motore ottimizza automaticamente le scene con più di 4 oggetti usando una gerarchia di volumi avvolgenti (BVH).

### Checklist prima del render finale

- [ ] Tutti gli `id` dei materiali sono univoci e referenziati correttamente.
- [ ] La `camera.position` non si trova all'interno di un oggetto solido.
- [ ] Le texture che richiedono variazioni hanno `randomize_offset` o `randomize_rotation` attivo.
- [ ] Il file YAML usa correttamente gli spazi per l'indentazione (niente TAB).
- [ ] È stata eseguita un'anteprima a bassa risoluzione (`--width 400 --samples 1`).
