# Capitolo 5: Trasformazioni, gruppi e organizzazione della scena

Man mano che le scene crescono, occorrono strumenti per posizionare gli oggetti con precisione, comporli in gerarchie, riutilizzarli in modo efficiente e suddividere le definizioni su più file. Questo capitolo copre tutto questo.

---

## 5.1 Il sistema delle trasformazioni

Ogni entità nella sezione `entities:` (e ogni figlio all'interno di un gruppo o di un template) supporta tre campi di trasformazione:

```yaml
- type: "box"
  material: "wood"
  translate: [2, 0.5, -1]
  rotate: [0, 45, 0]
  scale: [1.5, 1, 1.5]
```

### translate (Traslazione)

```yaml
translate: [x, y, z]
```

Sposta l'oggetto dell'offset indicato. `[2, 0, 0]` lo sposta di 2 unità a destra. `[0, 1, 0]` lo sposta di 1 unità verso l'alto.

### rotate (Rotazione)

```yaml
rotate: [rx, ry, rz]
```

Ruota l'oggetto in **gradi** attorno a ciascun asse. Le rotazioni sono applicate nell'ordine: **X, poi Y, infine Z** (angoli di Eulero intrinseci).

- `rotate: [90, 0, 0]` inclina in avanti (attorno all'asse X).
- `rotate: [0, 45, 0]` ruota di 45 gradi (attorno all'asse Y).
- `rotate: [0, 0, 30]` rolla (attorno all'asse Z).

### scale (Scala)

```yaml
scale: [sx, sy, sz]    # Scala non uniforme
scale: 2.0             # Scala uniforme (equivale a [2, 2, 2])
```

Scala l'oggetto lungo ciascun asse. Si può fornire un vettore di tre elementi per una scala non uniforme o un singolo numero per una scala uniforme.

- `scale: [2, 1, 1]` allunga l'oggetto fino a raddoppiarne la larghezza.
- `scale: 0.5` rimpicciolisce l'oggetto della metà in tutte le direzioni.

### Ordine di applicazione

Le trasformazioni sono composte in un ordine fisso:

**Prima Scala, poi Rotazione, infine Traslazione** (SRT: Scale, Rotate, Translate).

Ciò significa che:

1. L'oggetto viene scalato attorno alla sua origine locale.
2. L'oggetto scalato viene ruotato attorno alla sua origine locale.
3. L'oggetto ruotato e scalato viene spostato nella sua posizione finale.

Questo ordine è importante. Un box scalato a `[4, 0.1, 2]` e poi ruotato di 45 gradi crea un ripiano inclinato. Se l'ordine fosse invertito, la rotazione avverrebbe prima della scala, producendo un risultato differente.

---

## 5.2 Gruppi: Composizione Gerarchica

Un **gruppo** (group) raccoglie più entità in una singola unità logica. Qualsiasi trasformazione applicata al gruppo viene ereditata da tutti i suoi figli.

```yaml
entities:
  - name: "simple_table"
    type: "group"
    translate: [3, 0, 0]
    material: "oak"
    children:
      # Piano del tavolo
      - type: "box"
        scale: [1.4, 0.06, 0.8]
        translate: [0, 0.74, 0]

      # Quattro gambe
      - type: "cylinder"
        center: [-0.6, 0, -0.3]
        radius: 0.03
        height: 0.74

      - type: "cylinder"
        center: [0.6, 0, -0.3]
        radius: 0.03
        height: 0.74

      - type: "cylinder"
        center: [-0.6, 0, 0.3]
        radius: 0.03
        height: 0.74

      - type: "cylinder"
        center: [0.6, 0, 0.3]
        radius: 0.03
        height: 0.74
```

### Come si compongono le trasformazioni di gruppo

Ogni figlio ha la sua trasformazione locale. La trasformazione del gruppo viene applicata **sopra** la trasformazione locale del figlio:

**locale_figlio -> trasformazione_gruppo**

Nell'esempio sopra, le quattro gambe sono definite rispetto all'origine locale del tavolo. Il `translate: [3, 0, 0]` del gruppo sposta l'intero assemblaggio di 3 unità a destra.

### Ereditarietà del materiale

Un gruppo può specificare un `material:` che funge da predefinito per tutti i figli. Un figlio che imposta esplicitamente il proprio `material:` sovrascrive quello del gruppo.

```yaml
- type: "group"
  material: "oak"       # Predefinito per tutti i figli
  children:
    - type: "box"           # Usa "quercia" (ereditato)
      ...
    - type: "sphere"
      material: "glass"     # Usa "vetro" (sovrascrive)
      ...
```

### Gruppi nidificati

I gruppi possono contenere altri gruppi, a qualsiasi profondità:

```yaml
- type: "group"
  translate: [0, 0, 0]
  children:
    - type: "group"
      translate: [1, 0, 0]
      children:
        - type: "sphere"
          center: [0, 0.5, 0]
          radius: 0.5
          material: "red"
```

La sfera finisce in `[1, 0.5, 0]` (il suo centro più la traslazione del gruppo interno).

---

## 5.3 Template: Modelli Riutilizzabili

Quando lo stesso oggetto è necessario in più punti -- sedie attorno a un tavolo, alberi in una foresta, luci lungo un corridoio -- lo si definisce una volta come **template** e lo si inserisce nella scena con tutte le **istanze** che si desidera.

I template sono definiti nella sezione `templates:` e **non vengono renderizzati direttamente**. Fungono da modelli.

```yaml
templates:
  - name: "candle"
    material: "white_wax"
    children:
      # Corpo
      - type: "cylinder"
        center: [0, 0, 0]
        radius: 0.015
        height: 0.12

      # Fiamma (emissiva)
      - type: "sphere"
        center: [0, 0.14, 0]
        radius: 0.01
        material: "flame"
```

Un template ha:

| Campo        | Descrizione                                           |
|--------------|-------------------------------------------------------|
| `name`       | Identificatore unico (riferito dalle istanze)         |
| `children`   | Elenco di entità figlie (la geometria del modello)    |
| `material`   | Materiale predefinito opzionale per tutti i figli     |
| `translate`, `rotate`, `scale` | Trasformazione opzionale "posa predefinita" |

---

## 5.4 Istanze: Inserire i Template nella Scena

```yaml
entities:
  - type: "instance"
    template: "candle"
    translate: [-0.1, 0.78, 0.05]

  - type: "instance"
    template: "candle"
    translate: [0.1, 0.78, -0.05]
    material: "red_wax"     # Sovrascrive il materiale predefinito
```

| Campo        | Descrizione                                                   |
|--------------|---------------------------------------------------------------|
| `template`   | Nome del template da istanziare                               |
| `material`   | Sovrascrive il materiale predefinito del template (opzionale) |
| `translate`, `rotate`, `scale` | Trasformazione specifica dell'istanza        |
| `seed`       | Intero per la randomizzazione deterministica delle texture procedurali |

### Sovrascrivere il materiale

Quando imposti un `material:` su un'istanza, questo sostituisce il materiale predefinito del template per tutti i figli che non hanno un proprio materiale esplicito. I figli che definiscono esplicitamente un materiale (come la sfera "fiamma" nell'esempio della candela) mantengono il proprio.

Questo permette di definire un singolo template per un pezzo degli scacchi e istanziarlo sia in bianco che in nero semplicemente sovrascrivendo il materiale.

### Composizione delle trasformazioni

La catena di trasformazione completa è:

**locale_figlio -> trasformazione_template -> trasformazione_istanza**

Se il template ha un `rotate: [0, 90, 0]` e l'istanza ha un `translate: [5, 0, 0]`, ogni figlio viene prima posizionato nello spazio locale del template, poi ruotato di 90 gradi (trasformazione template), infine spostato di 5 unità a destra (trasformazione istanza).

### Variazione delle texture procedurali con il seed

Quando un materiale utilizza texture procedurali con `randomize_offset: true` o `randomize_rotation: true`, il campo `seed` su ogni istanza controlla la specifica variazione casuale:

```yaml
- type: "instance"
  template: "wood_plank"
  seed: 1
  translate: [0, 0, 0]

- type: "instance"
  template: "wood_plank"
  seed: 2
  translate: [1, 0, 0]
```

Ogni asse riceve un motivo venato unico, pur condividendo lo stesso materiale.

---

## 5.5 Importazione YAML: Scene su più file

Man mano che le scene crescono, tenere tutto in un unico file diventa complicato. La sezione `imports:` permette di caricare materiali, entità, luci e template da file YAML esterni.

```yaml
imports:
  - path: "libraries/materials/metals.yaml"
  - path: "libraries/materials/woods.yaml"
  - path: "libraries/objects/furniture.yaml"
  - path: "libraries/lights/studio-3point.yaml"
```

### Come funzionano gli import

1. I percorsi sono risolti **rispetto alla directory del file che importa**. Se la scena si trova in `scenes/my-scene.yaml`, il percorso `"libraries/materials/metals.yaml"` si risolve in `scenes/libraries/materials/metals.yaml`.

2. Il file importato può contribuire alle quattro sezioni: `materials`, `entities`, `lights` e `templates`. Queste vengono unite alla scena principale.

3. **Le definizioni locali vincono.** Se sia il file importato che la scena definiscono un materiale con lo stesso `id`, la versione locale ha la precedenza. Questo permette di importare una libreria e poi sovrascrivere specifici materiali.

4. **Il World e la Camera NON vengono importati.** Il file di scena principale possiede sempre le impostazioni del mondo e le definizioni delle fotocamere.

5. **Sono supportati gli import nidificati.** Un file importato può a sua volta contenere una sezione `imports:` che fa riferimento ad altri file.

6. **Protezione contro gli import circolari.** Se il file A importa il file B e il file B importa il file A, il motore rileva il ciclo e salta il secondo import.

### Esempio: Sovrascrivere un materiale importato

```yaml
imports:
  - path: "libraries/materials/metals.yaml"   # Definisce "dis_oro_lucido"

materials:
  # Sovrascrive l'oro della libreria con una versione personalizzata
  - id: "dis_oro_lucido"
    type: "disney"
    color: [0.95, 0.7, 0.2]     # Oro leggermente diverso
    metallic: 1.0
    roughness: 0.1
```

---

## 5.6 Buone pratiche per l'organizzazione della scena

**Scene piccole (< 50 entità):** Un singolo file YAML va benissimo.

**Scene medie (50--200 entità):**
- Importa librerie di materiali invece di definirli tutti nel file di scena.
- Usa i template per gli oggetti ripetuti.

**Scene grandi (200+ entità):**
- Suddividi in più file: uno per i materiali, uno per i template degli oggetti, uno per la disposizione della scena principale.
- Usa l'ecosistema delle librerie (Capitolo 10).
- Assegnare a ogni entità e template un `name:` descrittivo.
- Usa convenzioni di nomi coerenti (ad esempio, anteponi all'ID del materiale una categoria: `mat_floor`, `mat_wall`, `mat_glass`).

---

## 5.7 Esempio Completo: La Tavola Imbandita

Una scena che utilizza template, gruppi, istanze e import contemporaneamente.

```yaml
# dinner-table.yaml
# Una tavola da pranzo con quattro coperti, a dimostrazione di template,
# gruppi, istanze e sovrascrittura di materiali.

world:
  ambient_light: [0.02, 0.015, 0.01]
  background: [0.01, 0.01, 0.02]

camera:
  position: [0, 3.5, -4]
  look_at: [0, 0.78, 0]
  fov: 50

materials:
  # Tavolo
  - id: "dark_wood"
    type: "disney"
    roughness: 0.25
    clearcoat: 0.5
    clearcoat_gloss: 0.85
    texture:
      type: "wood"
      scale: 6.0
      noise_strength: 1.2
      colors: [[0.35, 0.2, 0.1], [0.22, 0.12, 0.06]]

  # Piatto di porcellana
  - id: "porcelain"
    type: "disney"
    color: [0.95, 0.93, 0.88]
    roughness: 0.12
    specular: 0.7
    subsurface: 0.2

  # Posate in acciaio
  - id: "steel"
    type: "disney"
    color: [0.75, 0.75, 0.78]
    metallic: 1.0
    roughness: 0.15

  # Vetro di cristallo
  - id: "crystal"
    type: "dielectric"
    refraction_index: 1.65
    color: [0.98, 0.98, 1.0]

  # Tovaglia (accento opzionale)
  - id: "linen"
    type: "disney"
    color: [0.88, 0.85, 0.78]
    roughness: 0.7
    sheen: 0.3

  # Pavimento
  - id: "floor"
    type: "lambertian"
    color: [0.25, 0.22, 0.2]

# ── Template ─────────────────────────────────────────────────────────

templates:
  # Un semplice piatto (disco + bordo toroidale)
  - name: "plate"
    material: "porcelain"
    children:
      - type: "disk"
        center: [0, 0, 0]
        radius: 0.12
        normal: [0, 1, 0]
      - type: "torus"
        major_radius: 0.12
        minor_radius: 0.008

  # Un bicchiere da vino (semplificato: stelo + calice)
  - name: "wine_glass"
    material: "crystal"
    children:
      # Base
      - type: "disk"
        center: [0, 0.001, 0]
        radius: 0.035
        normal: [0, 1, 0]
      # Stelo
      - type: "cylinder"
        center: [0, 0, 0]
        radius: 0.005
        height: 0.1
      # Calice
      - type: "sphere"
        center: [0, 0.13, 0]
        radius: 0.045

  # Una forchetta (semplificata)
  - name: "fork"
    material: "steel"
    children:
      - type: "box"
        scale: [0.01, 0.003, 0.12]
        translate: [0, 0.002, 0]

  # Un coperto (piatto + bicchiere + forchetta) come gruppo
  - name: "place_setting"
    children:
      - type: "instance"
        template: "plate"
        translate: [0, 0, 0]
      - type: "instance"
        template: "wine_glass"
        translate: [0.08, 0, -0.1]
      - type: "instance"
        template: "fork"
        translate: [-0.15, 0, 0]

# ── Scena ────────────────────────────────────────────────────────────

entities:
  # Pavimento
  - type: "infinite_plane"
    point: [0, 0, 0]
    normal: [0, 1, 0]
    material: "floor"

  # Tavolo (gruppo: piano + 4 gambe)
  - name: "table"
    type: "group"
    material: "dark_wood"
    children:
      - type: "box"
        scale: [1.4, 0.05, 0.9]
        translate: [0, 0.76, 0]
      - type: "cylinder"
        center: [-0.6, 0, -0.35]
        radius: 0.035
        height: 0.76
      - type: "cylinder"
        center: [0.6, 0, -0.35]
        radius: 0.035
        height: 0.76
      - type: "cylinder"
        center: [-0.6, 0, 0.35]
        radius: 0.035
        height: 0.76
      - type: "cylinder"
        center: [0.6, 0, 0.35]
        radius: 0.035
        height: 0.76

  # Quattro coperti attorno al tavolo
  - type: "instance"
    template: "place_setting"
    translate: [-0.35, 0.79, -0.25]

  - type: "instance"
    template: "place_setting"
    translate: [0.35, 0.79, -0.25]

  - type: "instance"
    template: "place_setting"
    translate: [-0.35, 0.79, 0.25]
    rotate: [0, 180, 0]

  - type: "instance"
    template: "place_setting"
    translate: [0.35, 0.79, 0.25]
    rotate: [0, 180, 0]

lights:
  # Luce calda diffusa dall'alto
  - type: "area"
    corner: [-0.6, 2.5, -0.4]
    u: [1.2, 0, 0]
    v: [0, 0, 0.8]
    color: [1.0, 0.92, 0.78]
    intensity: 40.0
    shadow_samples: 16

  # Riempimento freddo dal lato
  - type: "point"
    position: [3, 2, -2]
    color: [0.7, 0.8, 1.0]
    intensity: 15.0
```

Esegui il rendering con:

```
3d-ray -i dinner-table.yaml -w 1200 -H 800 -s 64 -d 30
```

---

## Cosa si è imparato

- **translate**, **rotate** e **scale** posizionano qualsiasi entità; l'ordine è sempre Scala -> Rotazione -> Traslazione.
- I **Gruppi** compongono più entità in un'unità mobile con trasformazioni e materiali ereditati.
- I **Template** definiscono modelli riutilizzabili; le **istanze** li inseriscono nella scena con sovrascritture di materiali opzionali.
- Gli **Import** uniscono file YAML esterni; le definizioni locali sovrascrivono quelle importate in caso di collisione di ID.
- I template possono fare riferimento ad altri template (un coperto che istanzia un piatto, un bicchiere e una forchetta).

---

[Precedente: Tutte le forme](./04-geometric-primitives.md) | [Successivo: Padronanza dell'illuminazione](./06-lighting.md) | [Indice del Tutorial](./README.md)
