# Libreria CSG — Oggetti e Preset Booleani

Una collezione di forme CSG pronte da copiare e usare nelle tue scene. Ogni preset è un frammento YAML completo della sezione `entities` con i materiali necessari inclusi come commento o nella sezione dedicata.

Per la documentazione completa della sintassi CSG consulta la [sezione 6.10 del Tutorial Scene](02-tutorial-scene.md#610-csg--constructive-solid-geometry).

---

## Indice
1. [Come Usare i Preset](#1-come-usare-i-preset)
2. [Operazioni Base](#2-operazioni-base)
   - [2.1 Union — Fusione di Volumi](#21-union--fusione-di-volumi)
   - [2.2 Intersection — Volume Comune](#22-intersection--volume-comune)
   - [2.3 Subtraction — Intaglio](#23-subtraction--intaglio)
3. [Forme Optiche e Lenti](#3-forme-optiche-e-lenti)
4. [Forme Industriali e Meccaniche](#4-forme-industriali-e-meccaniche)
5. [Forme Architettoniche](#5-forme-architettoniche)
6. [Forme Creative](#6-forme-creative)
7. [Alberi CSG Complessi](#7-alberi-csg-complessi)
8. [Materiali Consigliati per CSG](#8-materiali-consigliati-per-csg)
9. [Scene Starter CSG](#9-scene-starter-csg)
10. [Regole e Best Practices CSG](#10-regole-e-best-practices-csg)

---

## 1. Come Usare i Preset

Ogni preset è pensato per essere incollato direttamente nella sezione `entities:` del tuo YAML. I `material` referenziati nei preset devono essere definiti nella sezione `materials:` della tua scena. La [sezione 8](#8-materiali-consigliati-per-csg) include un set di materiali pronti da usare con questi preset.

```yaml
# Struttura del tuo file YAML
materials:
  # ... copia i materiali dalla sezione 8, o usa i tuoi ...

entities:
  # ... incolla qui i preset che vuoi ...

lights:
  # ... la tua illuminazione ...
```

---

## 2. Operazioni Base

### 2.1 Union — Fusione di Volumi

Due sfere che si fondono in un unico solido organico.

```yaml
- name: "blob_doppio"
  type: "csg"
  operation: "union"
  material: "plastica_bianca"
  left:
    type: "sphere"
    center: [-0.6, 1, 0]
    radius: 0.9
  right:
    type: "sphere"
    center: [0.6, 1, 0]
    radius: 0.9
```

Sfera + cilindro fuso (a forma di capsula):

```yaml
- name: "capsula"
  type: "csg"
  operation: "union"
  material: "plastica_bianca"
  left:
    type: "sphere"
    center: [0, 0.5, 0]
    radius: 0.5
  right:
    type: "csg"
    operation: "union"
    left:
      type: "cylinder"
      center: [0, 0.5, 0]
      radius: 0.4
      height: 1.5
    right:
      type: "sphere"
      center: [0, 2.0, 0]
      radius: 0.5
```

---

### 2.2 Intersection — Volume Comune

**Lente biconvessa** — l'intersezione di due sfere che si sovrappongono produce una lente classica:

```yaml
- name: "lente_biconvessa"
  type: "csg"
  operation: "intersection"
  material: "vetro_chiaro"
  left:
    type: "sphere"
    center: [0, 1, -0.5]
    radius: 1.2
  right:
    type: "sphere"
    center: [0, 1, 0.5]
    radius: 1.2
```

**Forma di diamante grezzo** — intersezione di un cubo ruotato con una sfera:

```yaml
- name: "diamante_grezzo"
  type: "csg"
  operation: "intersection"
  material: "diamante"
  left:
    type: "sphere"
    center: [0, 1, 0]
    radius: 1.1
  right:
    type: "box"
    scale: [1.4, 1.4, 1.4]
    rotate: [45, 45, 0]
    translate: [0, 1, 0]
```

**Cubo con angoli arrotondati** — intersezione di una sfera leggermente più grande e un cubo:

```yaml
- name: "cubo_arrotondato"
  type: "csg"
  operation: "intersection"
  material: "plastica_bianca"
  left:
    type: "sphere"
    center: [0, 1, 0]
    radius: 1.15
  right:
    type: "box"
    scale: [1.8, 1.8, 1.8]
    translate: [0, 1, 0]
```

---

### 2.3 Subtraction — Intaglio

**Scodella (sfera cava)** — sfera con metà inferiore e centro rimossi:

```yaml
- name: "scodella"
  type: "csg"
  operation: "subtraction"
  left:
    type: "sphere"
    center: [0, 1, 0]
    radius: 1.0
    material: "ceramica_bianca"
  right:
    type: "sphere"
    center: [0, 1, 0]
    radius: 0.85
    material: "ceramica_bianca"
```

> **💡 Nota:** Per una scodella vera devi anche tagliare la metà superiore con un box — vedi il preset completo nella [sezione 6 (Forme Creative)](#6-forme-creative).

**Dado con foro** — box con cilindro passante:

```yaml
- name: "dado_foro"
  type: "csg"
  operation: "subtraction"
  material: "acciaio_satinato"
  left:
    type: "box"
    scale: [1.6, 1.6, 1.6]
    translate: [0, 0.8, 0]
  right:
    type: "cylinder"
    center: [0, -0.5, 0]
    radius: 0.45
    height: 3.0
```

**Muro con finestra** — box grande con box piccolo sottratto:

```yaml
- name: "muro_finestra"
  type: "csg"
  operation: "subtraction"
  material: "cemento"
  left:
    type: "box"
    scale: [4.0, 3.0, 0.3]
    translate: [0, 1.5, 0]
  right:
    type: "box"
    scale: [1.2, 1.2, 1.0]
    translate: [0, 1.8, 0]
```

---

## 3. Forme Optiche e Lenti

### Lente Piano-Convessa

Una delle lenti più usate in ottica: un lato piatto, uno curvo.

```yaml
- name: "lente_piano_convessa"
  type: "csg"
  operation: "intersection"
  material: "vetro_ottico"
  left:
    type: "sphere"
    center: [0, 1, -0.3]
    radius: 1.1
  right:
    type: "box"
    scale: [2.0, 2.0, 1.0]
    translate: [0, 1, 0.2]
```

### Prisma Triangolare

Un cilindro ritagliato per ottenere una sezione triangolare:

```yaml
- name: "prisma"
  type: "csg"
  operation: "intersection"
  material: "vetro_ottico"
  left:
    type: "cylinder"
    center: [0, 0, 0]
    radius: 1.2
    height: 2.0
    translate: [0, 0, 0]
  right:
    type: "csg"
    operation: "intersection"
    left:
      type: "box"
      scale: [3.0, 2.5, 1.2]
      rotate: [0, 0, 30]
      translate: [0, 1, 0]
    right:
      type: "box"
      scale: [3.0, 2.5, 1.2]
      rotate: [0, 0, -30]
      translate: [0, 1, 0]
```

### Gemma / Diamante Tagliato

Una forma a gemma ottenuta da un icosaedro approssimato (sfera intersecata con box multipli):

```yaml
- name: "gemma"
  type: "csg"
  operation: "intersection"
  material: "diamante"
  left:
    type: "csg"
    operation: "intersection"
    left:
      type: "sphere"
      center: [0, 1, 0]
      radius: 1.0
    right:
      type: "box"
      scale: [1.6, 1.1, 1.6]
      translate: [0, 1, 0]
  right:
    type: "box"
    scale: [1.6, 1.1, 1.6]
    rotate: [0, 45, 0]
    translate: [0, 1, 0]
```

---

## 4. Forme Industriali e Meccaniche

### Dado Esagonale (Approssimato)

Un cilindro intersecato con tre box ruotati di 60° ciascuno produce l'impronta esagonale:

```yaml
- name: "dado_esagonale"
  type: "csg"
  operation: "intersection"
  material: "acciaio_satinato"
  left:
    type: "csg"
    operation: "intersection"
    left:
      type: "cylinder"
      center: [0, 0, 0]
      radius: 1.0
      height: 0.7
    right:
      type: "csg"
      operation: "intersection"
      left:
        type: "box"
        scale: [2.0, 1.0, 1.15]
        translate: [0, 0.35, 0]
      right:
        type: "csg"
        operation: "intersection"
        left:
          type: "box"
          scale: [2.0, 1.0, 1.15]
          rotate: [0, 60, 0]
          translate: [0, 0.35, 0]
        right:
          type: "box"
          scale: [2.0, 1.0, 1.15]
          rotate: [0, -60, 0]
          translate: [0, 0.35, 0]
  right:
    # Foro centrale per il bullone
    type: "cylinder"
    center: [0, -0.1, 0]
    radius: 0.35
    height: 1.0
```

### Bullone

Testa cilindrica (dado semplificato) con gambo:

```yaml
- name: "bullone"
  type: "csg"
  operation: "union"
  material: "acciaio_satinato"
  left:
    # Testa del bullone
    type: "cylinder"
    center: [0, 0, 0]
    radius: 0.55
    height: 0.35
  right:
    # Gambo
    type: "cylinder"
    center: [0, -1.5, 0]
    radius: 0.22
    height: 1.5
```

### Anello / Toro Approssimato

Un cilindro grande con un cilindro più piccolo sottratto dal centro:

```yaml
- name: "anello"
  type: "csg"
  operation: "subtraction"
  material: "oro"
  left:
    type: "cylinder"
    center: [0, 0, 0]
    radius: 1.0
    height: 0.4
  right:
    type: "cylinder"
    center: [0, -0.5, 0]
    radius: 0.7
    height: 1.5
```

### Tubo Cavo

Un cilindro con un cilindro coassiale sottratto:

```yaml
- name: "tubo"
  type: "csg"
  operation: "subtraction"
  material: "metallo_ruggine"
  left:
    type: "cylinder"
    center: [0, 0, 0]
    radius: 0.5
    height: 3.0
  right:
    type: "cylinder"
    center: [0, -0.5, 0]
    radius: 0.4
    height: 4.0
```

### Ingranaggio Semplificato

Un disco con foro centrale e denti cubici aggiunti con union:

```yaml
- name: "ingranaggio"
  type: "csg"
  operation: "subtraction"
  material: "acciaio_satinato"
  left:
    # Corpo principale: disco con foro
    type: "csg"
    operation: "subtraction"
    left:
      type: "cylinder"
      center: [0, 0, 0]
      radius: 1.0
      height: 0.3
    right:
      type: "cylinder"
      center: [0, -0.2, 0]
      radius: 0.3
      height: 0.8
  right:
    # (placeholder — aggiungere denti con union non è pratico in YAML puro;
    #  questo preset mostra la struttura base con foro centrale)
    type: "box"
    scale: [0.01, 0.01, 0.01]
    translate: [999, 999, 999]   # Nodo vuoto: box infinitamente piccolo lontano
```

---

## 5. Forme Architettoniche

### Arco a Tutto Sesto

Un box rettangolare con un cilindro orizzontale scavato in cima:

```yaml
- name: "arco"
  type: "csg"
  operation: "subtraction"
  material: "pietra"
  left:
    type: "box"
    scale: [3.0, 4.0, 0.6]
    translate: [0, 2.0, 0]
  right:
    type: "csg"
    operation: "union"
    left:
      # Apertura ad arco (cilindro orizzontale)
      type: "cylinder"
      center: [0, 0, 0]
      radius: 0.9
      height: 1.0
      rotate: [90, 0, 0]
      translate: [0, 2.8, 0]
    right:
      # Parte bassa dritta dell'apertura
      type: "box"
      scale: [1.8, 2.8, 1.0]
      translate: [0, 1.4, 0]
```

### Colonna con Capitello

Una colonna classica con una base quadrata e un capitello cubico:

```yaml
- name: "colonna_classica"
  type: "csg"
  operation: "union"
  material: "marmo_bianco"
  left:
    # Fusto cilindrico
    type: "cylinder"
    center: [0, 0.2, 0]
    radius: 0.3
    height: 3.0
  right:
    type: "csg"
    operation: "union"
    left:
      # Base (plinto)
      type: "box"
      scale: [0.9, 0.2, 0.9]
      translate: [0, 0.1, 0]
    right:
      # Capitello
      type: "box"
      scale: [0.8, 0.3, 0.8]
      translate: [0, 3.35, 0]
```

### Finestra ad Arco

Apertura rettangolare con semicerchio superiore in un muro:

```yaml
- name: "finestra_arco"
  type: "csg"
  operation: "subtraction"
  material: "mattoni"
  left:
    type: "box"
    scale: [5.0, 4.0, 0.4]
    translate: [0, 2.0, 0]
  right:
    type: "csg"
    operation: "union"
    left:
      # Parte rettangolare bassa
      type: "box"
      scale: [1.2, 2.0, 1.0]
      translate: [0, 1.0, 0]
    right:
      # Semicerchio superiore
      type: "sphere"
      center: [0, 2.0, 0]
      radius: 0.65
```

---

## 6. Forme Creative

### Scodella / Coppetta

Sfera cava tagliata a metà:

```yaml
- name: "scodella"
  type: "csg"
  operation: "subtraction"
  left:
    # Guscio: sfera grande meno sfera interna
    type: "csg"
    operation: "subtraction"
    left:
      type: "sphere"
      center: [0, 0, 0]
      radius: 1.0
      material: "ceramica_bianca"
    right:
      type: "sphere"
      center: [0, 0, 0]
      radius: 0.88
      material: "ceramica_bianca"
  right:
    # Taglia la metà superiore
    type: "box"
    scale: [3, 2, 3]
    translate: [0, 1.5, 0]
```

### Luna Crescente

Due sfere: sottrae la sfera di destra leggermente spostata da quella di sinistra:

```yaml
- name: "luna_crescente"
  type: "csg"
  operation: "subtraction"
  left:
    type: "sphere"
    center: [0, 1, 0]
    radius: 1.0
    material: "avorio"
  right:
    type: "sphere"
    center: [0.55, 1, -0.3]
    radius: 0.9
    material: "avorio"
```

### Stella a 4 Punte

Union di due box ruotati:

```yaml
- name: "stella_4_punte"
  type: "csg"
  operation: "union"
  material: "oro"
  left:
    type: "box"
    scale: [2.5, 0.6, 0.6]
    translate: [0, 1, 0]
  right:
    type: "box"
    scale: [0.6, 0.6, 2.5]
    translate: [0, 1, 0]
```

### Croce 3D

Tre box ortogonali in union:

```yaml
- name: "croce_3d"
  type: "csg"
  operation: "union"
  material: "marmo_bianco"
  left:
    type: "csg"
    operation: "union"
    left:
      type: "box"
      scale: [3.0, 0.7, 0.7]
      translate: [0, 1, 0]
    right:
      type: "box"
      scale: [0.7, 3.0, 0.7]
      translate: [0, 1, 0]
  right:
    type: "box"
    scale: [0.7, 0.7, 3.0]
    translate: [0, 1, 0]
```

### Sfera con Cavità Cubica (Negativo)

```yaml
- name: "sfera_cavita"
  type: "csg"
  operation: "subtraction"
  left:
    type: "sphere"
    center: [0, 1, 0]
    radius: 1.0
    material: "marmo_nero"
  right:
    type: "box"
    scale: [1.1, 1.1, 1.1]
    rotate: [20, 35, 10]
    translate: [0, 1, 0]
    material: "marmo_nero"
```

---

## 7. Alberi CSG Complessi

### Cubo Svizzero (A \ (B ∪ C ∪ D))

Un cubo con tre fori ortogonali passanti — ispirato al cubo di Menger:

```yaml
- name: "cubo_svizzero"
  type: "csg"
  operation: "subtraction"
  material: "plastica_bianca"
  left:
    type: "box"
    scale: [2.0, 2.0, 2.0]
    translate: [0, 1, 0]
  right:
    type: "csg"
    operation: "union"
    left:
      type: "csg"
      operation: "union"
      left:
        # Foro X
        type: "cylinder"
        center: [0, 1, 0]
        radius: 0.5
        height: 3.0
        rotate: [0, 0, 90]
      right:
        # Foro Y
        type: "cylinder"
        center: [0, 0, 0]
        radius: 0.5
        height: 3.0
    right:
      # Foro Z
      type: "cylinder"
      center: [0, 1, 0]
      radius: 0.5
      height: 3.0
      rotate: [90, 0, 0]
```

### Ruota con Raggi

Hub centrale + cerchio esterno + raggi, ottenuti per differenza da un disco pieno:

```yaml
- name: "ruota_raggi"
  type: "csg"
  operation: "subtraction"
  material: "acciaio_satinato"
  left:
    # Disco pieno
    type: "cylinder"
    center: [0, 0, 0]
    radius: 1.2
    height: 0.25
  right:
    type: "csg"
    operation: "union"
    left:
      # 4 fori per i raggi (approssimati con box ruotati)
      type: "csg"
      operation: "union"
      left:
        type: "box"
        scale: [2.2, 0.35, 0.25]
        translate: [0, 0.12, 0]
      right:
        type: "box"
        scale: [2.2, 0.35, 0.25]
        rotate: [0, 90, 0]
        translate: [0, 0.12, 0]
    right:
      # Foro centrale (mozzo)
      type: "cylinder"
      center: [0, -0.5, 0]
      radius: 0.25
      height: 1.5
```

### Lampada a Sospensione

Uno dei preset più completi: paralume (sfera cava aperta sotto) con bulbo emissivo:

```yaml
# Materiali necessari per questo preset:
# - "vetro_smerigliato": disney, roughness: 0.3, spec_trans: 0.9, ior: 1.5, color: [0.95,0.95,1.0]
# - "luce_calda":       emissive, color: [1.0, 0.85, 0.6], intensity: 15.0

- name: "paralume"
  type: "csg"
  operation: "subtraction"
  left:
    type: "csg"
    operation: "subtraction"
    left:
      # Guscio esterno
      type: "sphere"
      center: [0, 2.5, 0]
      radius: 0.85
      material: "vetro_smerigliato"
    right:
      # Guscio interno (rende cavo)
      type: "sphere"
      center: [0, 2.5, 0]
      radius: 0.75
      material: "vetro_smerigliato"
  right:
    # Apertura inferiore
    type: "box"
    scale: [2.0, 1.0, 2.0]
    translate: [0, 1.75, 0]

- name: "bulbo"
  type: "sphere"
  center: [0, 2.45, 0]
  radius: 0.3
  material: "luce_calda"
```

---

## 8. Materiali Consigliati per CSG

Copia questi materiali nella sezione `materials:` del tuo YAML per usarli nei preset di questa libreria.

```yaml
materials:

  # Vetro e ottiche
  - id: "vetro_chiaro"
    type: "disney"
    color: [1.0, 1.0, 1.0]
    roughness: 0.0
    spec_trans: 1.0
    ior: 1.5

  - id: "vetro_ottico"
    type: "disney"
    color: [0.97, 0.99, 1.0]
    roughness: 0.0
    spec_trans: 1.0
    ior: 1.62

  - id: "diamante"
    type: "disney"
    color: [1.0, 1.0, 1.0]
    roughness: 0.0
    spec_trans: 1.0
    ior: 2.42

  - id: "vetro_smerigliato"
    type: "disney"
    color: [0.95, 0.95, 1.0]
    roughness: 0.35
    spec_trans: 0.9
    ior: 1.5

  # Metalli
  - id: "acciaio_satinato"
    type: "disney"
    color: [0.58, 0.57, 0.55]
    metallic: 1.0
    roughness: 0.45

  - id: "oro"
    type: "disney"
    color: [1.0, 0.71, 0.29]
    metallic: 1.0
    roughness: 0.15

  - id: "metallo_ruggine"
    type: "disney"
    color: [0.55, 0.28, 0.15]
    metallic: 0.7
    roughness: 0.8

  # Plastiche
  - id: "plastica_bianca"
    type: "disney"
    color: [0.92, 0.92, 0.92]
    roughness: 0.6
    metallic: 0.0

  # Pietra e ceramica
  - id: "marmo_bianco"
    type: "lambertian"
    texture:
      type: "marble"
      scale: 15.0
      noise_strength: 10.0
      colors: [[0.96, 0.96, 0.96], [0.55, 0.55, 0.55]]

  - id: "marmo_nero"
    type: "lambertian"
    texture:
      type: "marble"
      scale: 12.0
      noise_strength: 10.0
      colors: [[0.05, 0.05, 0.05], [0.6, 0.6, 0.6]]

  - id: "pietra"
    type: "lambertian"
    texture:
      type: "noise"
      scale: 6.0
      colors: [[0.55, 0.52, 0.48], [0.42, 0.40, 0.36]]

  - id: "cemento"
    type: "lambertian"
    texture:
      type: "noise"
      scale: 8.0
      colors: [[0.60, 0.60, 0.60], [0.50, 0.50, 0.50]]

  - id: "mattoni"
    type: "lambertian"
    color: [0.55, 0.25, 0.18]

  - id: "ceramica_bianca"
    type: "disney"
    color: [0.95, 0.93, 0.90]
    roughness: 0.2
    specular: 0.5

  # Organici
  - id: "avorio"
    type: "lambertian"
    color: [0.96, 0.93, 0.82]

  # Emissivi
  - id: "luce_calda"
    type: "emissive"
    color: [1.0, 0.85, 0.6]
    intensity: 15.0
```

---

## 9. Scene Starter CSG

Scene complete e bilanciate che mostrano la CSG in contesti reali.

### Studio Ottiche

Lenti e prismi su un piano riflettente, illuminazione area light da soffitto.

```yaml
world:
  ambient_light: [0.05, 0.05, 0.08]
  background: [0.08, 0.08, 0.12]

camera:
  position: [0, 2.5, -6]
  look_at: [0, 1.2, 0]
  fov: 42

materials:
  - id: "vetro_ottico"
    type: "disney"
    color: [0.97, 0.99, 1.0]
    roughness: 0.0
    spec_trans: 1.0
    ior: 1.62
  - id: "piano_nero"
    type: "disney"
    color: [0.04, 0.04, 0.04]
    roughness: 0.1
    metallic: 0.0

entities:
  - name: "suolo"
    type: "infinite_plane"
    point: [0, 0, 0]
    normal: [0, 1, 0]
    material: "piano_nero"

  - name: "lente_sx"
    type: "csg"
    operation: "intersection"
    material: "vetro_ottico"
    left:
      type: "sphere"
      center: [-2, 1.2, 0]
      radius: 1.3
    right:
      type: "sphere"
      center: [-1.3, 1.2, 0]
      radius: 1.3

  - name: "lente_dx"
    type: "csg"
    operation: "intersection"
    material: "vetro_ottico"
    left:
      type: "sphere"
      center: [1.5, 1.2, 0.4]
      radius: 1.5
    right:
      type: "sphere"
      center: [1.5, 1.2, -0.4]
      radius: 1.5

lights:
  - type: "area"
    corner: [-3.0, 5.9, -3.0]
    u: [6.0, 0.0, 0.0]
    v: [0.0, 0.0, 6.0]
    color: [0.9, 0.92, 1.0]
    intensity: 30.0
    shadow_samples: 16
```

### Esposizione Metalli

Oggetti industriali CSG (dado, tubo, anello) in uno studio con illuminazione cinematografica.

```yaml
world:
  ambient_light: [0.03, 0.03, 0.03]
  background: [0.0, 0.0, 0.0]

camera:
  position: [0, 3, -7]
  look_at: [0, 1, 0]
  fov: 38

materials:
  - id: "acciaio"
    type: "disney"
    color: [0.58, 0.57, 0.55]
    metallic: 1.0
    roughness: 0.35
  - id: "piedistallo"
    type: "disney"
    color: [0.1, 0.1, 0.12]
    roughness: 0.15
    metallic: 0.0

entities:
  - name: "suolo"
    type: "infinite_plane"
    point: [0, 0, 0]
    normal: [0, 1, 0]
    material: "piedistallo"

  - name: "anello_grande"
    type: "csg"
    operation: "subtraction"
    material: "acciaio"
    left:
      type: "cylinder"
      center: [-2.5, 0, 0]
      radius: 0.9
      height: 0.35
    right:
      type: "cylinder"
      center: [-2.5, -0.5, 0]
      radius: 0.62
      height: 1.5

  - name: "dado_centrale"
    type: "csg"
    operation: "subtraction"
    material: "acciaio"
    left:
      type: "box"
      scale: [1.4, 1.4, 1.4]
      translate: [0, 0.7, 0]
    right:
      type: "cylinder"
      center: [0, -0.5, 0]
      radius: 0.38
      height: 3.0

  - name: "tubo_dx"
    type: "csg"
    operation: "subtraction"
    material: "acciaio"
    left:
      type: "cylinder"
      center: [2.5, 0, 0]
      radius: 0.5
      height: 2.5
    right:
      type: "cylinder"
      center: [2.5, -0.5, 0]
      radius: 0.38
      height: 3.5

lights:
  - type: "point"
    position: [-4, 6, -4]
    color: [1.0, 0.97, 0.90]
    intensity: 180
  - type: "point"
    position: [5, 3, -2]
    color: [0.7, 0.75, 1.0]
    intensity: 40
```

---

## 10. Regole e Best Practices CSG

1. **Entrambi i figli sono obbligatori.** Se `left` o `right` mancano, l'entità viene saltata con un warning in console.
2. **`operation` deve essere un valore valido.** Valori accettati: `union`, `intersection`, `subtraction` (alias: `subtract`, `difference`). Un valore errato salta l'entità.
3. **`infinite_plane` non può essere figlio CSG.** Il piano infinito non ha un AABB finita e non produce un intervallo di ray chiuso. Usa invece un box molto grande e piatto come piano finito.
4. **La sottrazione non è commutativa.** `A \ B` e `B \ A` producono forme diverse. Nella `subtraction`, `left` è il solido che rimane, `right` è lo stampo che viene rimosso.
5. **Le normali nella subtraction vengono invertite.** La superficie di `right` che risulta dopo il taglio ha la normale rivolta verso l'interno del solido `right` — invertita automaticamente dal motore per produrre l'orientazione corretta verso l'esterno della cavità.
6. **Materiale per figlio vs materiale fallback.** Se un figlio specifica `material:`, usa quel materiale. Se non lo specifica, eredita il `material:` del nodo padre CSG. Se anche il padre non ha `material:`, viene usato il grigio di fallback del motore.
7. **Trasformazioni sul nodo padre CSG.** Puoi applicare `translate`, `rotate`, `scale` al nodo CSG radice per posizionarlo nella scena senza dover modificare le coordinate dei figli.
8. **Profondità di annidamento.** Non c'è un limite fisso, ma ogni livello aggiunge 2 test di intersezione. Per alberi con più di 4–5 livelli, testa le prestazioni con `-s 1` prima di fare il render finale.
9. **Debug del YAML.** Se un'entità CSG non appare, controlla la console: il motore stampa warning espliciti per ogni problema di configurazione (figli mancanti, operation sconosciuta, figlio non creato).
10. **Intersezione con oggetti trasparenti.** Le operazioni CSG funzionano con materiali dielettrici e Disney (spec_trans > 0), ma i riflessi interni sono calcolati solo fino a `--depth` rimbalzi. Per lenti con rifrazioni realistiche, usa `-d 20` o più.
