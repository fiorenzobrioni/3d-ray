# Capitolo 3: I materiali in dettaglio

Nel capitolo precedente si sono utilizzati i materiali Lambertian e Metal. Il motore supporta in totale sei tipi di materiale, oltre a un ricco sistema di texture. Questo capitolo li analizza tutti, parametro per parametro.

---

## 3.1 Riferimento rapido: I sei tipi di materiale

| Tipo         | Alias                | Effetto principale              |
|--------------|----------------------|---------------------------------|
| `lambertian` | --                   | Opaco diffuso                   |
| `metal`      | --                   | Riflessione speculare           |
| `dielectric` | --                   | Vetro trasparente / rifrazione  |
| `emissive`   | --                   | Bagliore auto-illuminante       |
| `disney`     | `disney_bsdf`, `pbr` | PBR universale (12 parametri)   |
| `mix`        | `blend`              | Miscela due materiali insieme   |

Ogni materiale è definito nella sezione `materials:` con un `id` unico:

```yaml
materials:
  - id: "mio_materiale"
    type: "lambertian"
    color: [0.8, 0.2, 0.2]
```

Le entità (entities) fanno riferimento ai materiali tramite il loro `id`:

```yaml
entities:
  - type: "sphere"
    material: "mio_materiale"
    ...
```

---

## 3.2 Lambertian (Opaco Diffuso)

```yaml
- id: "chalk"
  type: "lambertian"
  color: [0.95, 0.92, 0.88]
```

Il materiale più semplice. La luce viene diffusa equamente in tutte le direzioni sopra la superficie (emisfero pesato con il coseno). Non ci sono riflessioni speculari, né punti di luce (highlights) -- solo una finitura piatta e opaca.

| Parametro | Tipo      | Predefinito    | Descrizione                                    |
|-----------|-----------|----------------|------------------------------------------------|
| `color`   | `[R,G,B]` | `[0.5,0.5,0.5]` | Albedo diffuso (0.0--1.0)                      |
| `texture` | blocco    | --             | Texture procedurale o immagine (sostituisce color) |

Quando è presente un blocco `texture:`, esso sovrascrive `color`. Maggiori informazioni sulle texture nella Sezione 3.7.

---

## 3.3 Metal (Metallo)

```yaml
- id: "copper_satin"
  type: "metal"
  color: [0.95, 0.64, 0.54]
  fuzz: 0.12
```

Le superfici metalliche riflettono la luce in modo speculare. Il motore utilizza un modello microfacet GGX per highlight realistici.

| Parametro | Tipo      | Predefinito | Descrizione                                     |
|-----------|-----------|-------------|-------------------------------------------------|
| `color`   | `[R,G,B]` | --          | Colore di riflettanza (tinta metallica)         |
| `fuzz`    | `float`   | `0.0`       | Rugosità: 0 = specchio perfetto, maggiore = sfocato |

Il valore `fuzz` è mappato internamente a un parametro di rugosità (`alpha = fuzz * fuzz`). Tenerlo nell'intervallo 0.0--0.6 per metalli realistici; valori più alti iniziano a sembrare poco naturali.

---

## 3.4 Dielectric (Vetro e Materiali Trasparenti)

```yaml
- id: "window_glass"
  type: "dielectric"
  refraction_index: 1.52

- id: "red_glass"
  type: "dielectric"
  refraction_index: 1.52
  color: [0.9, 0.1, 0.08]
```

I materiali dielettrici sono trasparenti. La luce viene divisa ad ogni superficie secondo le equazioni di Fresnel: una parte viene riflessa, una parte viene rifratta (deviata). Il parametro `refraction_index` determina quanto la luce viene deviata.

| Parametro          | Tipo      | Predefinito | Descrizione                         |
|--------------------|-----------|-------------|-------------------------------------|
| `refraction_index` | `float`   | `1.5`       | Indice di rifrazione (IOR)          |
| `color`            | `[R,G,B]` | `[1,1,1]`   | Tinta per il vetro colorato         |

### Indici di Rifrazione Comuni

| Materiale  | IOR   |
|------------|-------|
| Aria       | 1.000 |
| Acqua      | 1.333 |
| Ghiaccio   | 1.31  |
| Vetro      | 1.52  |
| Cristallo  | 1.65  |
| Diamante   | 2.42  |
| Rubino     | 1.77  |
| Smeraldo   | 1.57  |

### Importante: Il vetro richiede più profondità dei raggi (Ray Depth)

Ogni superficie di vetro che un raggio entra ed esce costa due rimbalzi (bounces). Il default è `-d 8` (sufficiente per la maggior parte delle scene grazie alla Russian Roulette). Se ci sono oggetti di vetro nidificati (ad esempio un bicchiere d'acqua, bottiglie dietro bottiglie), alza la profondità dei raggi ad almeno 16:

```
RayTracer -i my-scene.yaml -s 64 -d 16
```

Consulta [Profili di Rendering](../../reference/profili-di-rendering.md) per la spiegazione completa di quando alzare `-d`.

---

## 3.5 Emissive (Superfici Auto-Illuminanti)

```yaml
- id: "warm_glow"
  type: "emissive"
  color: [1.0, 0.85, 0.6]
  intensity: 10.0
```

Una superficie emissiva irradia la propria luce. Non riflette né diffonde la luce in entrata -- brilla.

| Parametro   | Tipo      | Predefinito | Descrizione                          |
|-------------|-----------|-------------|--------------------------------------|
| `color`     | `[R,G,B]` | --          | Colore dell'emissione                |
| `intensity` | `float`   | `1.0`       | Moltiplicatore di luminosità         |

La radianza effettivamente emessa è `color * intensity`. Un'intensità di 1.0 è a malapena visibile; valori tra 5 e 50 sono tipici per l'illuminazione di una stanza; 100+ crea sorgenti molto luminose.

### Oggetti Emissivi come Luci Geometriche

Qualsiasi entità con un materiale emissivo viene rilevata automaticamente dal motore e registrata come **luce geometrica** per la Next Event Estimation (NEE). Ciò significa che il motore campiona attivamente queste superfici quando calcola l'illuminazione diretta -- proprio come le sorgenti luminose esplicite.

La differenza rispetto alle luci esplicite (area, sphere, point) è che le entità emissive sono **visibili** nella scena: appaiono nelle riflessioni, nelle rifrazioni e dietro il vetro. Una luce area esplicita è invisibile -- illumina la scena ma non ha una forma visibile.

Usa i materiali emissivi per pannelli luminosi, sfere incandescenti, insegne al neon, lava, fuoco e tutto ciò che dovrebbe sia emettere luce che essere visto.

---

## 3.6 Disney/PBR (Materiale Principled)

```yaml
- id: "red_car_paint"
  type: "disney"
  color: [0.7, 0.05, 0.05]
  metallic: 0.0
  roughness: 0.3
  specular: 0.6
  clearcoat: 1.0
  clearcoat_gloss: 0.9
```

Il Disney Principled BSDF (noto anche come PBR) è il tipo di materiale più versatile. Combina riflessione diffusa, speculare, metallica, clearcoat, subsurface scattering, sheen e trasmissione in un unico materiale con parametri intuitivi. Lo si può usare al posto di lambertian, metal o dielectric per qualsiasi superficie.

Alias del tipo: `disney`, `disney_bsdf`, `pbr` (tutti creano lo stesso materiale).

### Riferimento Completo dei Parametri

| Parametro         | Predefinito | Intervallo | Descrizione                                         |
|-------------------|-------------|------------|-----------------------------------------------------|
| `color`           | --          | 0--1       | Colore albedo di base                               |
| `metallic`        | `0.0`       | 0--1       | 0 = dielettrico (plastica, legno), 1 = conduttore (metallo) |
| `roughness`       | `0.5`       | 0--1       | 0 = perfettamente liscio, 1 = completamente diffuso |
| `subsurface`      | `0.0`       | 0--1       | Miscela verso il modello diffuso subsurface scattering |
| `specular`        | `0.5`       | 0--1       | Intensità speculare dielettrica (Fresnel F0)        |
| `specular_tint`   | `0.0`       | 0--1       | Tinge la riflessione speculare con il colore di base |
| `sheen`           | `0.0`       | 0--1       | Highlight morbido ad angoli radenti (tessuto, velluto) |
| `sheen_tint`      | `0.5`       | 0--1       | Tinge lo sheen con il colore di base                |
| `clearcoat`       | `0.0`       | 0--1       | Secondo lobo speculare (lacca, vernice)             |
| `clearcoat_gloss` | `1.0`       | 0--1       | Lucentezza del clearcoat (1 = lucido, 0 = satinato) |
| `spec_trans`      | `0.0`       | 0--1       | Trasmissione speculare (0 = opaco, 1 = vetro)       |
| `ior`             | `1.5`       | 1+         | Indice di rifrazione (usato per specular e trasmissione) |
| `texture`         | --          | --         | Texture procedurale o immagine (sostituisce color)  |
| `normal_map`      | --          | --         | Dettagli di superficie tramite perturbazione delle normali |

### Come i Parametri Lavorano Insieme

Il materiale Disney è un sistema a strati:

1. **Strato di base** (`metallic` = 0): una superficie dielettrica con riflessione diffusa, controllata da `roughness`. Come plastica, legno, pelle.
2. **Modalità metallo** (`metallic` = 1): riflessione solo speculare tinta dal `color`. Come oro, acciaio, rame.
3. **Strato Clearcoat** (`clearcoat` > 0): un rivestimento lucido indipendente sopra qualsiasi cosa ci sia sotto. Come la vernice delle auto o il legno laccato.
4. **Trasmissione** (`spec_trans` > 0): la luce attraversa il materiale. Combinato con `roughness` > 0 si ottiene il vetro smerigliato.
5. **Subsurface** (`subsurface` > 0): la luce penetra la superficie e si diffonde all'interno. Dona un aspetto più morbido e piatto agli oggetti sottili. Usato per pelle, cera, porcellana, foglie.
6. **Sheen** (`sheen` > 0): un tenue bagliore agli angoli radenti. Usato per tessuti, velluto e alcuni materiali organici.

### Ricette: Materiali del Mondo Reale

**Oro lucido:**
```yaml
- id: "shiny_gold"
  type: "disney"
  color: [1.0, 0.76, 0.33]
  metallic: 1.0
  roughness: 0.05
  specular: 0.8
```

**Plastica rossa:**
```yaml
- id: "red_plastic"
  type: "disney"
  color: [0.8, 0.1, 0.1]
  metallic: 0.0
  roughness: 0.3
  specular: 0.5
```

**Vernice auto (con clearcoat):**
```yaml
- id: "blue_car"
  type: "disney"
  color: [0.02, 0.1, 0.45]
  metallic: 0.0
  roughness: 0.4
  clearcoat: 1.0
  clearcoat_gloss: 0.9
```

**Vetro smerigliato:**
```yaml
- id: "frosted_glass"
  type: "disney"
  color: [0.95, 0.97, 1.0]
  roughness: 0.3
  spec_trans: 0.85
  ior: 1.52
  specular: 0.7
```

**Velluto viola:**
```yaml
- id: "purple_velvet"
  type: "disney"
  color: [0.3, 0.05, 0.2]
  roughness: 0.85
  sheen: 1.0
  sheen_tint: 0.5
```

**Porcellana (subsurface):**
```yaml
- id: "porcelain"
  type: "disney"
  color: [0.95, 0.93, 0.88]
  roughness: 0.15
  specular: 0.7
  subsurface: 0.3
```

**Acciaio spazzolato:**
```yaml
- id: "brushed_steel"
  type: "disney"
  color: [0.7, 0.7, 0.72]
  metallic: 1.0
  roughness: 0.35
```

---

## 3.7 Materiale Mix/Blend

```yaml
- id: "worn_metal"
  type: "mix"
  material_a: "clean_steel"
  material_b: "rust"
  blend: 0.4
```

Un materiale Mix miscela due altri materiali. Sia `material_a` che `material_b` devono fare riferimento a materiali già definiti (o importati).

| Parametro    | Tipo    | Predefinito | Descrizione                                     |
|--------------|---------|-------------|-------------------------------------------------|
| `material_a` | `string` | --          | ID del primo materiale                          |
| `material_b` | `string` | --          | ID del secondo materiale                         |
| `blend`      | `float` | `0.5`       | Fattore di miscela costante (0 = tutto A, 1 = tutto B) |
| `mask`       | blocco  | --          | Texture che controlla spazialmente la miscela   |

### Mix con una Maschera Texture

Invece di una miscela uniforme, si può usare una texture procedurale per controllare *dove* appare ogni materiale:

```yaml
materials:
  - id: "clean_steel"
    type: "disney"
    color: [0.7, 0.7, 0.72]
    metallic: 1.0
    roughness: 0.15

  - id: "rust"
    type: "disney"
    color: [0.55, 0.25, 0.08]
    metallic: 0.3
    roughness: 0.7

  - id: "worn_steel"
    type: "mix"
    material_a: "clean_steel"
    material_b: "rust"
    mask:
      type: "noise"
      scale: 3.0
      noise_strength: 2.0
```

Dove la texture noise è scura (vicino a 0) vedrai l'acciaio pulito; dove è chiara (vicino a 1) vedrai la ruggine. Il risultato è una superficie usurata realistica e variabile nello spazio.

I materiali Mix possono essere annidati: puoi creare un mix di un mix per effetti complessi multi-strato.

---

## 3.8 Texture Procedurali

Qualsiasi materiale che accetta un campo `color` può anche accettare un blocco `texture:`. Quando presente, la texture genera valori di colore proceduralmente basati sulla posizione 3D di ogni punto della superficie, sostituendo il `color` piatto.

### Checker

```yaml
texture:
  type: "checker"
  scale: 1.0
  colors: [[0.9, 0.9, 0.9], [0.1, 0.1, 0.1]]
```

Un pattern a scacchiera 3D che alterna due colori. Il parametro `scale` controlla la dimensione di ogni quadrato (scala minore = quadrati più grandi).

### Noise (Perlin)

```yaml
texture:
  type: "noise"
  scale: 4.0
  noise_strength: 1.0
```

Variazione organica, simile a nuvole, basata sul rumore di Perlin. L'output del colore è guidato dalla funzione di rumore e dal `color` di base del materiale.

| Parametro        | Predefinito | Descrizione                                       |
|------------------|-------------|---------------------------------------------------|
| `scale`          | `1.0`       | Frequenza del pattern di rumore                   |
| `noise_strength` | --          | Turbolenza (0 = liscio, più alto = più frastagliato) |

### Marble (Marmo)

```yaml
texture:
  type: "marble"
  scale: 8.0
  noise_strength: 5.0
  colors: [[0.93, 0.90, 0.87], [0.55, 0.53, 0.50]]
```

Simula le venature del marmo. `colors` definisce due colori: la pietra di base e il colore della venatura. `noise_strength` controlla quanto sono pronunciate le venature. Valori più alti creano venature più selvagge e turbolente.

### Wood (Legno)

```yaml
texture:
  type: "wood"
  scale: 6.0
  noise_strength: 1.5
  colors: [[0.55, 0.35, 0.18], [0.35, 0.20, 0.10]]
```

Simula le venature del legno con anelli concentrici. `colors` definisce il legno primaverile (anelli più chiari) e quello autunnale (anelli più scuri). `noise_strength` controlla l'irregolarità degli anelli -- valori più alti creano nodi e figure.

### Trasformazione e Randomizzazione Texture

Tutte le texture procedurali supportano questi parametri aggiuntivi:

| Parametro            | Tipo      | Predefinito | Descrizione                                         |
|----------------------|-----------|-------------|-----------------------------------------------------|
| `offset`             | `[x,y,z]` | --          | Sposta la texture nello spazio 3D                   |
| `rotation`           | `[x,y,z]` | --          | Ruota il pattern della texture                      |
| `randomize_offset`   | `bool`    | `false`     | Offset casuale per istanza (usa il `seed` dell'entità) |
| `randomize_rotation` | `bool`    | `false`     | Rotazione casuale per istanza                      |

I flag `randomize_*` sono estremamente utili quando lo stesso materiale viene applicato a più oggetti: ogni istanza riceve una variazione di texture unica in modo che gli oggetti non sembrino tutti identici.

Per una randomizzazione deterministica, impostare il campo `seed` su ogni entità:

```yaml
entities:
  - type: "sphere"
    seed: 42
    material: "wood_with_random"
    ...
```

---

## 3.9 Texture Immagine

```yaml
- id: "earth"
  type: "lambertian"
  texture:
    type: "image"
    path: "textures/earth.png"
    uv_scale: [1.0, 1.0]
```

Le texture immagine caricano un'immagine da un file e la avvolgono sulla superficie utilizzando le coordinate UV dell'oggetto.

| Parametro  | Tipo       | Predefinito | Descrizione                                    |
|------------|------------|-------------|------------------------------------------------|
| `path`     | `string`   | --          | Percorso relativo del file immagine            |
| `uv_scale` | `[U, V]`   | `[1, 1]`    | Ripetizione della texture (2.0 = ripete due volte) |

Formati supportati: PNG, JPEG, BMP, GIF, TIFF, WebP.

Il percorso viene risolto rispetto alla directory del file di scena. Se la scena si trova in `scenes/my-scene.yaml` e la texture è in `scenes/textures/brick.png`, usare `path: "textures/brick.png"`.

---

## 3.10 Normal Maps (Mappe delle Normali)

Le normal map aggiungono l'illusione del dettaglio superficiale (protuberanze, solchi, graffi) senza aggiungere geometria effettiva. Funzionano perturbando la normale della superficie in ogni punto di ombreggiatura.

```yaml
- id: "brick_wall"
  type: "disney"
  color: [0.65, 0.3, 0.2]
  roughness: 0.7
  normal_map:
    path: "textures/brick-normal.png"
    strength: 1.0
    uv_scale: [2.0, 2.0]
```

| Parametro  | Tipo       | Predefinito | Descrizione                                          |
|------------|------------|-------------|------------------------------------------------------|
| `path`     | `string`   | --          | Percorso del file normal map                         |
| `strength` | `float`    | `1.0`       | Intensità della perturbazione (0 = piatto, 1 = pieno) |
| `uv_scale` | `[U, V]`   | --          | Ripetizione UV per la normal map                     |
| `flip_y`   | `bool`     | `false`     | Inverte l'asse Y (imposta `true` per mappe DirectX) |

3D-Ray utilizza la convenzione OpenGL per le normal map (Y verso l'alto). Se le normal map provengono da uno strumento che utilizza la convenzione DirectX (Y verso il basso), impostare `flip_y: true`.

Le normal map possono essere aggiunte a qualsiasi tipo di materiale che le supporti (Lambertian, Metal, Disney). Vengono applicate prima di tutti i calcoli di ombreggiatura (shading).

---

## 3.11 Esempio Completo: Galleria dei Materiali

Una scena che mostra otto diversi materiali uno accanto all'altro.

```yaml
# material-gallery.yaml
# Otto sfere su piedistalli, ognuna con un materiale diverso.

world:
  ambient_light: [0.02, 0.02, 0.03]
  background: [0.02, 0.02, 0.03]

cameras:
  - name: "main"
    position: [0, 3, -10]
    look_at: [0, 1.2, 0]
    fov: 48

lights:
  # Luce principale (ampia area per ombre morbide)
  - type: "area"
    corner: [-4, 5, -3]
    u: [8, 0, 0]
    v: [0, 0, 6]
    color: [1.0, 0.97, 0.92]
    intensity: 30.0
    shadow_samples: 16

  # Luce di riempimento in basso a destra
  - type: "point"
    position: [6, 2, -5]
    color: [0.7, 0.8, 1.0]
    intensity: 25.0

materials:
  # Pavimento
  - id: "floor"
    type: "lambertian"
    color: [0.3, 0.3, 0.3]

  # 1. Rosso opaco (Lambertian)
  - id: "mat_lambertian"
    type: "lambertian"
    color: [0.8, 0.15, 0.1]

  # 2. Oro spazzolato (Metal)
  - id: "mat_metal"
    type: "metal"
    color: [1.0, 0.76, 0.33]
    fuzz: 0.15

  # 3. Vetro trasparente (Dielectric)
  - id: "mat_glass"
    type: "dielectric"
    refraction_index: 1.52

  # 4. Bagliore blu (Emissive)
  - id: "mat_emissive"
    type: "emissive"
    color: [0.2, 0.4, 1.0]
    intensity: 8.0

  # 5. Vernice auto (Disney, clearcoat)
  - id: "mat_carpaint"
    type: "disney"
    color: [0.6, 0.02, 0.02]
    metallic: 0.0
    roughness: 0.35
    clearcoat: 1.0
    clearcoat_gloss: 0.9

  # 6. Marmo (Disney + texture)
  - id: "mat_marble"
    type: "disney"
    roughness: 0.1
    specular: 0.8
    texture:
      type: "marble"
      scale: 12.0
      noise_strength: 5.0
      colors: [[0.95, 0.92, 0.88], [0.5, 0.48, 0.45]]

  # 7. Legno (Disney + texture)
  - id: "mat_wood"
    type: "disney"
    roughness: 0.35
    clearcoat: 0.4
    clearcoat_gloss: 0.8
    texture:
      type: "wood"
      scale: 6.0
      noise_strength: 1.5
      colors: [[0.55, 0.35, 0.18], [0.30, 0.18, 0.08]]

  # 8. Pavimento a scacchi (Disney + texture checker)
  - id: "mat_checker"
    type: "disney"
    roughness: 0.15
    specular: 0.6
    texture:
      type: "checker"
      scale: 0.5
      colors: [[0.9, 0.88, 0.85], [0.15, 0.12, 0.1]]

entities:
  # Pavimento
  - type: "infinite_plane"
    point: [0, 0, 0]
    normal: [0, 1, 0]
    material: "floor"

  # Fila di sfere a y=1.3 (su piedistalli immaginari)
  - type: "sphere"
    center: [-5.25, 1.3, 0]
    radius: 0.65
    material: "mat_lambertian"

  - type: "sphere"
    center: [-3.75, 1.3, 0]
    radius: 0.65
    material: "mat_metal"

  - type: "sphere"
    center: [-2.25, 1.3, 0]
    radius: 0.65
    material: "mat_glass"

  - type: "sphere"
    center: [-0.75, 1.3, 0]
    radius: 0.65
    material: "mat_emissive"

  - type: "sphere"
    center: [0.75, 1.3, 0]
    radius: 0.65
    material: "mat_carpaint"

  - type: "sphere"
    center: [2.25, 1.3, 0]
    radius: 0.65
    material: "mat_marble"

  - type: "sphere"
    center: [3.75, 1.3, 0]
    radius: 0.65
    material: "mat_wood"

  - type: "sphere"
    center: [5.25, 1.3, 0]
    radius: 0.65
    material: "mat_checker"
```

Esegui il rendering con:

```
RayTracer -i material-gallery.yaml -w 1600 -H 600 -s 1024 -d 8 -S 4
```

---

## Cosa si è imparato

- I materiali **Dielectric** sono trasparenti con rifrazione controllabile. Il vetro richiede una maggiore profondità dei raggi.
- I materiali **Emissive** brillano e agiscono automaticamente come sorgenti luminose.
- Il materiale **Disney/PBR** sostituisce tutti gli altri tipi con 12 parametri intuitivi. Usalo per qualsiasi superficie, dal metallo al vetro al tessuto.
- I materiali **Mix/Blend** combinano spazialmente due materiali usando un fattore di miscela o una maschera texture.
- Le **Texture procedurali** (checker, noise, marble, wood) generano pattern da coordinate 3D -- non sono necessari file immagine.
- Le **Texture immagine** avvolgono un file immagine su una superficie tramite mappatura UV.
- Le **Normal map** aggiungono dettagli superficiali senza geometria extra.

---

[Precedente: La prima scena](./02-first-scene.md) | [Successivo: Tutte le forme](./04-geometric-primitives.md) | [Indice del Tutorial](./README.md)
