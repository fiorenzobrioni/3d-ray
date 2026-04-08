# 11. Gruppi, Template, Istanze e Import YAML

## 11.1 Gruppi — Composizione Gerarchica

I gruppi permettono di **comporre oggetti in modo gerarchico**: un gruppo contiene una lista di figli, ciascuno con le proprie trasformazioni locali. Il gruppo stesso può avere una trasformazione (translate, rotate, scale) che si applica a **tutti** i figli contemporaneamente.

### Sintassi Base

```yaml
- name: "lampione"
  type: "group"
  translate: [5, 0, 0]
  rotate: [0, 45, 0]
  material: "ferro"          # Materiale fallback per i figli
  children:
    - type: "cylinder"
      center: [0, 0, 0]
      radius: 0.08
      height: 3.0
    - type: "sphere"
      center: [0, 3.2, 0]
      radius: 0.25
      material: "vetro_latte"  # Override del materiale del padre
```

### Campi

| Campo | Tipo | Obbligatorio | Descrizione |
|-------|------|:---:|-------------|
| `type` | stringa | ✓ | Deve essere `"group"` |
| `children` | lista | ✓ | Figli del gruppo. Qualsiasi tipo di entità. |
| `material` | stringa | — | Materiale fallback per i figli senza `material` proprio. |
| `translate` | `[x, y, z]` | — | Traslazione del gruppo intero. |
| `rotate` | `[x, y, z]` | — | Rotazione del gruppo intero (gradi). |
| `scale` | scalare o `[x, y, z]` | — | Scala del gruppo intero. |
| `seed` | int | — | Seed per le texture procedurali dei figli. |

### Trasformazioni Ereditate

L'ordine di applicazione delle trasformazioni è:

1. **Trasformazione locale del figlio** (scale → rotate → translate del figlio)
2. **Trasformazione del gruppo** (scale → rotate → translate del gruppo)
3. **Trasformazione del gruppo padre** (se annidato in un altro gruppo)

Questo è lo stesso modello usato dai ray tracer professionali (PBRT, Mitsuba, Arnold).

### Annidamento Ricorsivo

I gruppi possono contenere altri gruppi a profondità arbitraria, oltre a primitive, CSG e mesh OBJ:

```yaml
- name: "braccio_robot"
  type: "group"
  translate: [0, 2, 0]
  rotate: [0, 0, 30]
  material: "metallo"
  children:
    - type: "cylinder"
      center: [0, 0, 0]
      radius: 0.15
      height: 1.5
    - name: "avambraccio"
      type: "group"
      translate: [0, 1.5, 0]
      rotate: [0, 0, -45]
      children:
        - type: "cylinder"
          center: [0, 0, 0]
          radius: 0.12
          height: 1.2
        - type: "sphere"
          center: [0, 1.2, 0]
          radius: 0.15
```

### Geometry Lights nei Gruppi

I figli emissivi all'interno di un gruppo partecipano automaticamente alla **NEE**. Il sistema compone le trasformazioni correttamente per il campionamento in world space.

---

## 11.2 Template e Istanze — Oggetti Riutilizzabili

I template permettono di **definire un oggetto composto una volta** e **istanziarlo più volte** in posizioni diverse. Il YAML resta compatto e DRY (Don't Repeat Yourself).

### Sintassi

```yaml
# 1. DEFINISCI il template (non viene renderizzato)
templates:
  - name: "pedina"
    material: "legno_noce"
    children:
      - type: "cylinder"
        center: [0, 0, 0]
        radius: 0.4
        height: 0.15
      - type: "torus"
        center: [0, 0.15, 0]
        major_radius: 0.38
        minor_radius: 0.06
      - type: "sphere"
        center: [0, 0.35, 0]
        radius: 0.3

# 2. ISTANZIA quante volte vuoi
entities:
  - name: "pedina_e2"
    type: "instance"
    template: "pedina"
    translate: [0, 0, 0]

  - name: "pedina_d2"
    type: "instance"
    template: "pedina"
    translate: [2, 0, 0]
    material: "legno_acero"    # Override del materiale

  - name: "pedina_a7"
    type: "instance"
    template: "pedina"
    translate: [-3, 0, 5]
    scale: 1.2                 # Variante più grande
```

### Campi del Template

| Campo | Tipo | Obbligatorio | Descrizione |
|-------|------|:---:|-------------|
| `name` | stringa | ✓ | Identificatore univoco del template. |
| `children` | lista | ✓ | Figli del template (stessa sintassi di un group). |
| `material` | stringa | — | Materiale di default per i figli. |
| `translate` | `[x, y, z]` | — | Trasformazione "posa di default". |
| `rotate` | `[x, y, z]` | — | Rotazione della posa di default. |
| `scale` | scalare o `[x, y, z]` | — | Scala della posa di default. |

> **Nota sul `center` nei figli:** Nei children dei template e dei gruppi, tutte le primitive che supportano `center` (sphere, cylinder, cone, capsule, torus, annulus, disk) lo interpretano come posizione locale nell'object space del gruppo. Questo è il modo più naturale per comporre oggetti: allineare i pezzi usando `center` relativo, poi posizionare il gruppo nel mondo con `translate` sull'istanza.

### Campi dell'Instance

| Campo | Tipo | Obbligatorio | Descrizione |
|-------|------|:---:|-------------|
| `type` | stringa | ✓ | Deve essere `"instance"` |
| `template` | stringa | ✓ | Nome del template da istanziare. |
| `material` | stringa | — | Override del materiale (sovrascrive quello del template). |
| `translate` | `[x, y, z]` | — | Posizione dell'istanza nel mondo. |
| `rotate` | `[x, y, z]` | — | Rotazione dell'istanza. |
| `scale` | scalare o `[x, y, z]` | — | Scala dell'istanza. |
| `seed` | int | — | Seed per texture procedurali (override del template). |

### Composizione delle Trasformazioni

Se il template ha una trasformazione, questa funge da **"posa di default"** che si compone con la trasformazione dell'istanza:

```
figlio_locale → template_transform → instance_transform
```

**Esempio pratico:** un template `"bottiglia"` con `rotate: [90, 0, 0]` (distesa orizzontale) mantiene quella rotazione quando l'istanza aggiunge `translate: [5, 0, 0]` — la bottiglia resta orizzontale e viene spostata.

L'istanza **non sovrascrive** la trasformazione del template: le compone. Se vuoi una posa diversa, crea un secondo template o applica la rotazione inversa nell'istanza.

### Override del Materiale

L'istanza può sovrascrivere il materiale del template. La priorità è:

1. Materiale del figlio (se specificato nel template)
2. Materiale dell'istanza (override globale)
3. Materiale del template (default)
4. Lambertian grigio (fallback di sistema)

```yaml
templates:
  - name: "sfera_base"
    material: "bianco"        # Default
    children:
      - type: "sphere"
        center: [0, 0.5, 0]
        radius: 0.5

entities:
  - type: "instance"
    template: "sfera_base"     # Usa materiale "bianco"
    translate: [0, 0, 0]

  - type: "instance"
    template: "sfera_base"
    material: "oro"            # Override → materiale "oro"
    translate: [2, 0, 0]
```

### Contenuti Supportati

Un template può contenere qualsiasi tipo di figlio:

| Tipo figlio | Supporto |
|-------------|:---:|
| Primitive (sphere, box, cylinder, ...) | ✓ |
| CSG (union, intersection, subtraction) | ✓ |
| Mesh (OBJ) | ✓ |
| Gruppi annidati | ✓ |

### Template Importati da File Esterni

I template si integrano perfettamente con il sistema di import. Puoi creare una **libreria di oggetti** in un file separato e importarla in qualsiasi scena:

**`libraries/chess-pieces.yaml`**
```yaml
materials:
  - id: "legno_chiaro"
    type: "disney"
    color: [0.85, 0.75, 0.55]
    roughness: 0.4

templates:
  - name: "pedina_bianca"
    material: "legno_chiaro"
    children:
      - type: "cylinder"
        center: [0, 0, 0]
        radius: 0.4
        height: 0.15
      - type: "sphere"
        center: [0, 0.35, 0]
        radius: 0.3
```

**`scacchiera.yaml`**
```yaml
imports:
  - path: "libraries/chess-pieces.yaml"

entities:
  # 8 pedine piazzate dalla stessa definizione
  - type: "instance"
    template: "pedina_bianca"
    translate: [-3.5, 0, -2.5]
  - type: "instance"
    template: "pedina_bianca"
    translate: [-2.5, 0, -2.5]
  # ... ecc.
```

---

## 11.3 Import YAML — Librerie Riutilizzabili

Il sistema di import permette di scomporre scene complesse in file separati.

### Sintassi

```yaml
imports:
  - path: "libraries/studio-materials.yaml"
  - path: "libraries/furniture.yaml"
  - path: "lighting/3point-setup.yaml"
```

### Semantica di Merge

| Sezione | Comportamento |
|---------|---------------|
| `materials` | Importati **prima** dei locali → locali con stesso `id` vincono. |
| `entities` | Importati **prima** dei locali. |
| `lights` | Importati **prima** dei locali. |
| `templates` | Importati **prima** dei locali → locali con stesso `name` vincono. |
| `world` | **Non importato.** Sempre del file principale. |
| `camera`/`cameras` | **Non importate.** Sempre del file principale. |

### Risoluzione dei Percorsi

I percorsi nei file importati sono risolti **relativamente al file che li contiene**, non alla scena principale.

### Import Annidati

Un file importato può a sua volta importare altri file. I cicli sono rilevati e interrotti con un warning.

### Più sezioni templates/entities

Ogni file YAML può avere al massimo **una** sezione `templates:` e una `entities:` (limitazione del formato YAML). Tuttavia, con gli import puoi avere template e entità provenienti da **N file diversi** — il merge li unisce automaticamente.

**Pattern consigliato:**
```
scene.yaml              → camera, world, entities locali
├── libraries/metals.yaml      → materials
├── libraries/chess-pieces.yaml → materials + templates
└── lighting/studio.yaml       → lights
```

---

---

[← Torna all'indice](../02-tutorial-scene.md)
