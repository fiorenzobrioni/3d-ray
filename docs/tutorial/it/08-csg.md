# Capitolo 8: Constructive Solid Geometry (CSG)

A volte le forme viste nel Capitolo 4 non sono sufficienti. Può essere necessaria una sfera con un foro attraverso di essa, un cubo con gli angoli arrotondati, o una parete con una finestra ad arco. La **Constructive Solid Geometry** (CSG) permette di creare forme complesse combinando primitive semplici con operazioni booleane.

---

## 8.1 Le tre operazioni booleane

La CSG lavora con tre operazioni:

| Operazione      | Alias                          | Risultato                           |
|-----------------|--------------------------------|-------------------------------------|
| `union`         | --                             | Volume combinato di entrambe le forme |
| `intersection`  | --                             | Solo il volume sovrapposto          |
| `subtraction`   | `subtract`, `difference`       | La forma di sinistra meno quella di destra |

Ogni entità CSG ha un figlio `left` e un figlio `right`. Ogni figlio è una definizione di entità inline: una primitiva solida (sphere, box, cylinder, cone, torus, capsule, quad, disk, lathe, ...) oppure un altro nodo `csg` per alberi booleani annidati. **I gruppi, le mesh e le istanze di template NON sono supportati come figli CSG** — vedi il callout al termine di questa sezione.

```yaml
- type: "csg"
  operation: "subtraction"
  left: { ... }
  right: { ... }
  material: "materiale_predefinito"
```

> **⚠️ Tipi ammessi come figli CSG.** Il motore CSG richiede che ogni figlio sia una *primitiva solida* con interno/esterno ben definiti (il classificatore all-hits deve poter determinare se un punto è dentro o fuori dal solido). Ammessi: qualunque primitiva solida fra quelle elencate, oppure un altro nodo `csg`. **Non ammessi, scartati con un avviso** (`CSG entity '…': failed to create one or both children. Skipping.`): `type: "group"`, `type: "mesh"` / `type: "obj"`, `type: "instance"`, `type: "plane"` / `type: "infinite_plane"`. Se serve sottrarre l'unione di due box da un cilindro, scrivi l'unione esplicitamente come `csg: union` annidato invece di avvolgere i due box in un `type: "group"`.

> **⚠️ Materiali emissivi dentro il CSG.** Assegnare un materiale `emissive` a un figlio CSG è sintassi valida, ma la superficie risultante **non parteciperà alla Next Event Estimation** (campionamento diretto della luce). Il motore stampa un avviso una-tantum quando viene rilevata questa situazione. La geometria emissiva illuminerà comunque la scena tramite i rimbalzi indiretti, ma con varianza notevolmente più alta (più rumore a parità di campioni). Se serve che la superficie luminosa agisca anche come sorgente di luce diretta, posizionare una sfera luminosa, una luce area o una primitiva emissiva corrispondente nella stessa posizione *fuori* dall'albero CSG.

---

## 8.2 Union: Unire le Forme

La Union produce il volume combinato di due forme. Dove si sovrappongono, l'interno viene fuso in un unico solido.

```yaml
# Pupazzo di neve: tre sfere impilate
- name: "snowman"
  type: "csg"
  operation: "union"
  material: "snow"
  left:
    type: "csg"
    operation: "union"
    left:
      type: "sphere"
      center: [0, 0.5, 0]
      radius: 0.5
    right:
      type: "sphere"
      center: [0, 1.2, 0]
      radius: 0.35
  right:
    type: "sphere"
    center: [0, 1.75, 0]
    radius: 0.25
```

L'operazione Union è utile quando si vogliono trattare più forme come un unico solido -- ad esempio, quando si vuole sottrarre qualcosa dalla forma combinata, o quando si ha bisogno di un unico materiale che copra una forma senza giunture.

---

## 8.3 Intersection: Mantenere la sovrapposizione

L'Intersection mantiene solo il volume in cui entrambe le forme esistono contemporaneamente. Tutto il resto viene scartato.

```yaml
# Lente: intersezione di due sfere sovrapposte
- name: "lens"
  type: "csg"
  operation: "intersection"
  material: "glass"
  left:
    type: "sphere"
    center: [0, 1, -0.3]
    radius: 0.8
  right:
    type: "sphere"
    center: [0, 1, 0.3]
    radius: 0.8
```

Le due sfere si sovrappongono al centro, creando un volume a forma di lente. L'entità della sovrapposizione (controllata dalla distanza tra i centri rispetto ai raggi) determina lo spessore della lente.

### Un altro esempio: Cubo arrotondato

Interseca un box con una sfera per arrotondare gli angoli del cubo:

```yaml
- name: "rounded_cube"
  type: "csg"
  operation: "intersection"
  material: "white_plastic"
  left:
    type: "box"
    scale: [1.4, 1.4, 1.4]
    translate: [0, 1, 0]
  right:
    type: "sphere"
    center: [0, 1, 0]
    radius: 1.0
```

---

## 8.4 Subtraction: Scolpire Fori

La Subtraction rimuove il volume della forma di destra dalla forma di sinistra. Il risultato è la forma di sinistra con la forma di destra "scavata".

```yaml
# Sfera con un foro cilindrico passante
- name: "drilled_sphere"
  type: "csg"
  operation: "subtraction"
  material: "marble"
  left:
    type: "sphere"
    center: [0, 1, 0]
    radius: 1.0
  right:
    type: "cylinder"
    center: [0, 0, 0]
    radius: 0.3
    height: 3.0
```

Il cilindro è più alto della sfera, assicurando che passi completamente attraverso. Il risultato è una sfera con un tunnel cilindrico netto.

> **Importante:** L'ordine conta! `sinistra - destra` non è la stessa cosa di `destra - sinistra`. La forma a sinistra è quella che sopravvive; la forma a destra è quella che scava.

### Esempio: Ingresso ad arco in una parete

```yaml
- name: "wall_with_arch"
  type: "csg"
  operation: "subtraction"
  material: "stone"
  left:
    type: "box"
    scale: [4, 3, 0.3]
    translate: [0, 1.5, 0]
  right:
    type: "csg"
    operation: "union"
    left:
      type: "box"
      scale: [1.0, 1.8, 0.5]
      translate: [0, 0.9, 0]
    right:
      type: "sphere"
      center: [0, 1.8, 0]
      radius: 0.5
```

Questo crea una parete rettangolare con un'apertura ad arco: un vano porta rettangolare sormontato da una cupola semisferica, il tutto sottratto dalla parete.

---

## 8.5 Materiali per figlio

Ogni figlio CSG può avere il proprio materiale. I figli senza un materiale esplicito ereditano il `material:` della CSG padre.

```yaml
- type: "csg"
  operation: "subtraction"
  material: "white_marble"          # Ripiego per i figli senza materiale
  left:
    type: "sphere"
    center: [0, 1, 0]
    radius: 1.0
    material: "white_marble"        # Esplicito: la superficie esterna è marmo
  right:
    type: "cylinder"
    center: [0, 0, 0]
    radius: 0.3
    height: 3.0
    material: "shiny_gold"          # La superficie interna del foro è oro
```

Quando esegui una sottrazione, le superfici interne che vengono "esposte" dalla sottrazione ereditano il loro materiale dal figlio di destra. Questo ti permette di creare oggetti con materiali diversi all'esterno e all'interno -- come un geode o un guscio di cioccolato.

---

## 8.6 Trasformazioni per figlio

Ogni figlio supporta `translate`, `rotate` e `scale`:

```yaml
- type: "csg"
  operation: "subtraction"
  material: "steel"
  left:
    type: "box"
    scale: [2, 2, 2]
    translate: [0, 1, 0]
  right:
    type: "cylinder"
    center: [0, 0, 0]
    radius: 0.4
    height: 3.0
    rotate: [90, 0, 0]        # Foro orizzontale attraverso il cubo
    translate: [0, 1, 0]
```

Le trasformazioni vengono applicate a ciascun figlio indipendentemente prima che venga eseguita l'operazione booleana. L'entità CSG padre può anche avere le sue trasformazioni, che vengono applicate all'intero risultato.

---

## 8.7 CSG Nidificate: Alberi Booleani Complessi

Poiché un figlio CSG può essere a sua volta un'entità CSG, puoi costruire forme arbitrariamente complesse attraverso la nidificazione.

### Esempio: Cubo con tre fori perpendicolari

```yaml
- name: "drilled_cube"
  type: "csg"
  operation: "subtraction"
  material: "steel"
  translate: [0, 1.2, 0]
  left:
    type: "box"
    scale: [2, 2, 2]
  right:
    type: "csg"
    operation: "union"
    left:
      type: "csg"
      operation: "union"
      left:
        # Foro lungo l'asse Y
        type: "cylinder"
        center: [0, -1.5, 0]
        radius: 0.4
        height: 3.0
      right:
        # Foro lungo l'asse X
        type: "cylinder"
        center: [0, 0, 0]
        radius: 0.4
        height: 3.0
        rotate: [0, 0, 90]
    right:
      # Foro lungo l'asse Z
      type: "cylinder"
      center: [0, 0, 0]
      radius: 0.4
      height: 3.0
      rotate: [90, 0, 0]
```

Questo crea un cubo solido con tre tunnel cilindrici scavati lungo ciascun asse. L'approccio è:

1. Unire i tre cilindri in un'unica forma "punta di trapano".
2. Sottrarre la punta combinata dal cubo.

### Esempio: Un semplice calice

```yaml
- name: "goblet"
  type: "csg"
  operation: "union"
  material: "crystal"
  left:
    # Calice: sfera con la parte superiore tagliata e l'interno scavato
    type: "csg"
    operation: "subtraction"
    left:
      type: "csg"
      operation: "subtraction"
      left:
        type: "sphere"
        center: [0, 1.5, 0]
        radius: 0.6
      right:
        type: "sphere"
        center: [0, 1.5, 0]
        radius: 0.55
    right:
      # Taglia la parte superiore
      type: "box"
      scale: [2, 1, 2]
      translate: [0, 2.2, 0]
  right:
    # Stelo + base
    type: "csg"
    operation: "union"
    left:
      type: "cylinder"
      center: [0, 0, 0]
      radius: 0.06
      height: 1.0
    right:
      type: "torus"
      major_radius: 0.3
      minor_radius: 0.06
```

---

## 8.8 Suggerimenti e insidie

1. **Le forme devono sovrapporsi.** Un'intersezione tra due forme che non si toccano non produce nulla. Una sottrazione di una forma che si trova interamente all'esterno del figlio di sinistra non ha alcun effetto.

2. **Rendi la forma che scava più grande.** Quando esegui una sottrazione, estendi il figlio di destra ben oltre la superficie del figlio di sinistra. Un cilindro alto esattamente quanto un cubo potrebbe produrre artefatti sottili ai bordi. Rendilo 1.5--2 volte più grande del necessario.

3. **La CSG è costosa.** Il motore deve testare tutte le intersezioni su entrambi i figli (fino a 16 colpi per figlio per raggio). Usa la CSG per gli oggetti principali della scena, non per riempirla con centinaia di forme CSG identiche. Definisci invece l'oggetto CSG una volta come template e istanzialo.

4. **L'ordine delle sottrazioni conta.** `A - B` è diverso da `B - A`. Il figlio di sinistra è sempre la forma "positiva" che sopravvive.

5. **Combina con le trasformazioni.** L'entità CSG padre supporta `translate`, `rotate` e `scale` proprio come qualsiasi altra entità. Questo permette di posizionare e orientare il risultato CSG finito senza modificare i figli.

---

## 8.9 Esempio Completo: Il laboratorio dello scultore

```yaml
# sculptor-workshop.yaml
# Diversi oggetti CSG che mostrano unione, intersezione e sottrazione.

world:
  sky:
    type: "flat"
    color: [0.04, 0.04, 0.06]

cameras:
  - name: "main"
    position: [0, 3, -8]
    look_at: [0, 1.2, 0]
    fov: 50

lights:
  - type: "area"
    corner: [-4, 5, -3]
    u: [8, 0, 0]
    v: [0, 0, 6]
    color: [1, 0.97, 0.93]
    intensity: 30.0

  - type: "point"
    position: [4, 3, -5]
    color: [0.75, 0.82, 1.0]
    intensity: 25.0

materials:
  - id: "floor"
    type: "lambertian"
    color: [0.3, 0.28, 0.25]
  - id: "marble"
    type: "disney"
    color: [0.92, 0.90, 0.86]
    roughness: 0.12
    specular: 0.7
  - id: "glass"
    type: "dielectric"
    refraction_index: 1.52
  - id: "steel"
    type: "disney"
    color: [0.7, 0.7, 0.72]
    metallic: 1.0
    roughness: 0.15
  - id: "inner_gold"
    type: "disney"
    color: [1.0, 0.76, 0.33]
    metallic: 1.0
    roughness: 0.05
  - id: "stone"
    type: "disney"
    roughness: 0.6
    texture:
      type: "marble"
      scale: 8.0
      noise_strength: 4.0
      colors: [[0.85, 0.82, 0.78], [0.5, 0.48, 0.44]]

entities:
  - type: "infinite_plane"
    point: [0, 0, 0]
    normal: [0, 1, 0]
    material: "floor"

  # 1. Lente (intersezione) -- estrema sinistra
  - name: "lens"
    type: "csg"
    operation: "intersection"
    material: "glass"
    translate: [-3, 1.2, 0]
    left:
      type: "sphere"
      center: [0, 0, -0.25]
      radius: 0.8
    right:
      type: "sphere"
      center: [0, 0, 0.25]
      radius: 0.8

  # 2. Sfera forata (sottrazione) -- centro-sinistra
  - name: "drilled_sphere"
    type: "csg"
    operation: "subtraction"
    translate: [-1, 1.2, 0]
    left:
      type: "sphere"
      center: [0, 0, 0]
      radius: 0.7
      material: "marble"
    right:
      type: "cylinder"
      center: [0, -1, 0]
      radius: 0.25
      height: 2.5
      material: "inner_gold"

  # 3. Parete perforata (sottrazione) -- centro
  - name: "perforated_wall"
    type: "csg"
    operation: "subtraction"
    material: "stone"
    translate: [1, 1.2, 0]
    left:
      type: "box"
      scale: [1.6, 1.8, 0.25]
    right:
      type: "csg"
      operation: "union"
      left:
        type: "sphere"
        center: [0, 0.3, 0]
        radius: 0.3
      right:
        type: "csg"
        operation: "union"
        left:
          type: "sphere"
          center: [-0.35, -0.3, 0]
          radius: 0.2
        right:
          type: "sphere"
          center: [0.35, -0.3, 0]
          radius: 0.2

  # 4. Cubo forato (sottrazione nidificata) -- estrema destra
  - name: "drilled_cube"
    type: "csg"
    operation: "subtraction"
    material: "steel"
    translate: [3, 1.2, 0]
    left:
      type: "box"
      scale: [1.2, 1.2, 1.2]
    right:
      type: "csg"
      operation: "union"
      left:
        type: "csg"
        operation: "union"
        left:
          type: "cylinder"
          center: [0, -1, 0]
          radius: 0.3
          height: 2.5
        right:
          type: "cylinder"
          center: [0, 0, 0]
          radius: 0.3
          height: 2.5
          rotate: [0, 0, 90]
      right:
        type: "cylinder"
        center: [0, 0, 0]
        radius: 0.3
        height: 2.5
        rotate: [90, 0, 0]
```

Esegui il rendering con:

```
RayTracer -i sculptor-workshop.yaml -w 1600 -H 700 -s 256 -d 6
```

---

## Cosa si è imparato

- **Union** fonde due forme in un unico solido.
- **Intersection** mantiene solo la parte in cui entrambe le forme si sovrappongono.
- **Subtraction** scava la forma di destra da quella di sinistra (l'ordine conta!).
- Ogni figlio CSG può avere il proprio materiale e le proprie trasformazioni.
- Le operazioni CSG si nidificano a qualsiasi profondità per forme complesse.
- Rendi le forme usate per scavare più grandi del necessario per evitare artefatti.
- Usa la CSG per gli oggetti principali; istanzia il risultato per copie multiple.

---

[Precedente: Cielo, ambiente ed effetti fotocamera](./07-sky-environment-camera.md) | [Successivo: Mezzi partecipanti (Volumetrics)](./09-volumetrics.md) | [Indice del Tutorial](./README.md)
