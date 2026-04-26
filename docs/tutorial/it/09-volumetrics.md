# Capitolo 9: Mezzi partecipanti (Volumetrics)

L'aria reale non è perfettamente trasparente. La nebbia diffonde la luce, l'acqua assorbe le lunghezze d'onda del rosso, il fumo brilla quando viene attraversato da un raggio. 3D-Ray supporta **quattro tipi di mezzo partecipante globale** (omogeneo, height fog, procedurale, grid) e **cinque phase function** (isotropic, HG, Rayleigh, double-HG, Schlick), sufficienti per coprire la maggior parte dei casi pratici: nebbia uniforme, foschia atmosferica, nubi, fumo localizzato, cielo.

---

## 9.1 Cosa sono i mezzi partecipanti?

Nel vuoto, la luce viaggia in linea retta all'infinito. In un mezzo partecipante (aria, acqua, fumo), accadono due cose:

- **Assorbimento (Absorption)** -- il mezzo "ingoia" i fotoni. La luce si affievolisce man mano che viaggia più lontano. L'assorbimento colorato crea atmosfere tinte (blu sott'acqua, foschia arancione al tramonto).

- **Scattering (Diffusione)** -- i fotoni cambiano direzione quando colpiscono le particelle nel mezzo. Questo è il motivo per cui la nebbia brilla quando i fari la attraversano e perché il cielo è blu.

La combinazione di assorbimento e scattering determina il comportamento della luce mentre attraversa il volume.

---

## 9.2 Configurazione del mezzo globale

Il mezzo è definito sotto `world: > medium:`:

```yaml
world:
  medium:
    type: "homogeneous"
    sigma_a: [0.01, 0.01, 0.01]
    sigma_s: [0.06, 0.06, 0.06]
    phase: "hg"
    g: 0.85
```

| Parametro | Tipo      | Predefinito   | Descrizione                                     |
|-----------|-----------|---------------|-------------------------------------------------|
| `type`    | `string`  | --            | `"homogeneous"`, `"height_fog"`, `"procedural"`, `"grid"` |
| `sigma_a` | `[R,G,B]` | --            | Coefficiente di assorbimento per canale         |
| `sigma_s` | `[R,G,B]` | --            | Coefficiente di scattering per canale           |
| `phase`   | `string`  | `"isotropic"` | `"isotropic"`, `"hg"`, `"rayleigh"`, `"double_hg"`, `"schlick"` |
| `g`       | `float`   | `0.0`         | Parametro di asimmetria (per `"hg"` / `"schlick"`) |

Campi aggiuntivi specifici per tipo (height fog: `y0`, `scale_height`; procedural: `frequency`, `octaves`, `lacunarity`, `gain`, `seed`; grid: `bounds_min`, `bounds_max`, `nx`, `ny`, `nz`, `data`/`file`) sono documentati nella sezione 9.4.

### sigma_a (Assorbimento)

Controlla quanto velocemente la luce viene assorbita. Le unità sono l'inverso delle unità world (1/unità). Valori più alti indicano un mezzo più denso e opaco.

- `[0.01, 0.01, 0.01]` -- assorbimento molto lieve (leggera foschia).
- `[0.1, 0.05, 0.01]` -- assorbimento colorato: il rosso viene assorbito più velocemente, il blu meno. Questo crea una tinta bluastra (come sott'acqua).

### sigma_s (Scattering)

Controlla quanta luce viene deviata dalle particelle. Valori più alti indicano una nebbia più densa con fasci di luce più visibili.

- `[0.02, 0.02, 0.02]` -- foschia sottile.
- `[0.1, 0.1, 0.1]` -- nebbia evidente.
- `[0.5, 0.5, 0.5]` -- nebbia spessa, impenetrabile.

Il coefficiente di estinzione totale è `sigma_t = sigma_a + sigma_s`. Questo determina l'opacità complessiva del mezzo (quanto velocemente la visibilità cala con la distanza).

---

## 9.3 Funzioni di Fase: Come la luce si diffonde

La funzione di fase determina la distribuzione angolare della luce diffusa. 3D-Ray ne supporta cinque.

### Isotropic (Isotropa - Predefinita)

```yaml
phase: "isotropic"
```

La luce si diffonde equamente in tutte le direzioni. Modello più semplice; funziona bene per fumo denso, nubi spesse, mezzi molto turbidi.

### Henyey-Greenstein (HG)

```yaml
phase: "hg"
g: 0.85
```

Permette una distorsione direzionale dello scattering:

| Valore di `g` | Comportamento                                      |
|---------------|----------------------------------------------------|
| `0.0`         | Identico a isotropic                               |
| `0.3`         | Lieve scattering in avanti (foschia sottile)       |
| `0.7`         | Forte scattering in avanti (nebbia, nuvole)        |
| `0.85`        | Scattering molto concentrato in avanti (nebbia densa, caligine) |
| `-0.3`        | Scattering all'indietro (insolito, artistico)      |

Lo scattering in avanti (`g > 0`) è fisicamente accurato per la maggior parte dei mezzi reali (nebbia, polvere, aerosol) e crea bagliori luminosi intorno alle sorgenti luminose.

Alias: `"hg"`, `"henyey_greenstein"`.

### Rayleigh (Scattering atmosferico)

```yaml
phase: "rayleigh"
```

Formula `p(θ) = (3/16π)(1 + cos²θ)`: scattering tipico delle molecole d'aria, usato in tutti i modelli di cielo e aerial perspective (Bruneton, Hosek-Wilkie). Nessun parametro. Adatto a nebbie molto sottili pensate per simulare atmosfera planetaria.

### Double Henyey-Greenstein (nubi realistiche)

```yaml
phase: "double_hg"
g1: 0.85
g2: -0.3
w: 0.7
```

Combinazione lineare di due lobi HG: uno forward (`g1 ≈ 0.85`) e uno laterale/backward (`g2 ≈ -0.3`), pesati da `w ∈ [0,1]`. È il modello usato da Nubis (Guerrilla Games) per le nubi volumetriche di *Horizon Zero Dawn* e da Arnold per il rendering di cumuli. Dà un silver-lining morbido attorno al contorno delle nubi che HG singolo non riesce a produrre.

### Schlick (fast-HG)

```yaml
phase: "schlick"
g: 0.6
```

Approssimazione razionale di HG senza `sqrt`: `p(θ) = (1 - k²) / (4π · (1 + k · cosθ)²)` con `k ≈ 1.55·g − 0.55·g³`. Usata da RenderMan e Cycles quando si vuole massimizzare il throughput. Visivamente quasi indistinguibile da HG per `|g| < 0.9`.

**Quale scegliere?**
- Nebbia generica / nube fumosa → `hg` con `g = 0.6-0.85`.
- Cielo e foschia atmosferica su scala planetaria → `rayleigh`.
- Nubi cumuli realistiche con silver-lining → `double_hg`.
- Path tracer con milioni di valutazioni di phase → `schlick` (velocità).
- Fumo denso, scena sottomarina torbida → `isotropic`.

### MIS sulla phase function

Quando un raggio scattera in un punto del medium, il motore calcola l'in-scattering combinando due strategie: **NEE** (shadow ray verso ogni luce, con phase function come BRDF) e **phase sampling** (rimbalzo importance-sampled secondo la phase). Le due densità — la `light.PdfSolidAngle` e la `phase.Pdf` — vengono pesate con la stessa balance/power heuristic usata sulle superfici.

L'effetto pratico è una **riduzione visibile dei fireflies** nelle scene con luce direzionale forte attraverso fog (god ray): ogni rimbalzo phase-sampled che colpisce direttamente la luce viene pesato con MIS invece di essere semplicemente azzerato. Switchare con `--mis power` può aiutare ulteriormente quando il sole è piccolo (puntiforme rispetto al volume) e la phase è larga.

---

## 9.4 Oltre l'omogeneo: tipi di mezzo eterogenei

La nebbia uniforme ha un limite: nel mondo reale la densità cambia con l'altitudine, forma sacche irregolari, o è confinata in un volume localizzato (una nuvola, una colonna di fumo). 3D-Ray offre tre tipi aggiuntivi per coprire questi casi.

### 9.4.1 `height_fog` — foschia esponenziale in altezza

Densità che cala esponenzialmente con la quota: `σ_T(y) = σ_T0 · exp(-(y - y0) / H)`. È il modello "atmosphere / aerial perspective" di Arnold `atmosphere_volume` e V-Ray `EnvironmentFog`. L'integrale lungo il raggio ha forma chiusa → **costo quasi identico al medium omogeneo**, nessun delta tracking.

```yaml
world:
  medium:
    type: "height_fog"
    sigma_a: [0.02, 0.02, 0.025]
    sigma_s: [0.25, 0.28, 0.32]
    y0: 0.0                # Quota di riferimento (dove la densità è nominale)
    scale_height: 2.0      # Distanza in Y per calo 1/e della densità
    phase: "hg"
    g: 0.6
```

- **`y0`**: sopra questa quota la densità decresce, sotto cresce.
- **`scale_height`**: `H` piccolo → strato sottile attaccato al suolo; `H` grande → gradiente dolce visibile su tutta la scena.

**Uso tipico:** scene outdoor con montagne, strade all'alba, mare all'orizzonte, vedute urbane con smog. Permette di "far respirare" la scena senza appesantirla di nebbia uniforme.

**Tip:** se la camera è bassa e guarda quasi orizzontalmente lungo raggi che attraversano molta nebbia, alza `-s` almeno a 256.

### 9.4.2 `procedural` — Perlin fBm

Densità guidata da **noise Perlin con fractal brownian motion** (fBm). Il free-path sampling usa **delta tracking (Woodcock)** e la trasmittanza è stimata via **ratio tracking**. Analogo ad Arnold `standard_volume` con input noise o RenderMan `PxrVolume` in modalità procedurale.

```yaml
world:
  medium:
    type: "procedural"
    sigma_a: [0.01, 0.01, 0.01]
    sigma_s: [0.5, 0.5, 0.55]
    frequency: 0.45        # Frequenza del noise (world units)
    octaves: 4             # Numero di ottave fBm (1-8)
    lacunarity: 2.0        # Moltiplicatore di frequenza fra ottave (≥ 1)
    gain: 0.55             # Moltiplicatore di ampiezza fra ottave (0.01-0.99)
    seed: 42               # Seed deterministico
    phase: "hg"
    g: 0.75
```

- **`frequency`** alto → sacche piccole e fitte; basso → macchie grandi.
- **`octaves`** 3–4 bastano per una nebbia convincente; 6+ aggiunge dettaglio fine (ma più rumore nel render).
- **`lacunarity`** = 2.0 è il classico (raddoppio fra ottave).
- **`gain`** < 0.5 → noise dolce, > 0.5 → noise più duro.
- **`seed`**: cambialo per variare la forma del noise a parità di altri parametri.

**Uso tipico:** stanze con nebbia irregolare, scene horror, god-ray non omogenei, foreste nebbiose, superfici d'acqua con foschia a chiazze.

**Tip:** delta tracking è più rumoroso → alza `-s` a 400 o 1024 per render finali.

### 9.4.3 `grid` — densità da griglia 3D

Densità campionata su una **griglia 3D regolare** dentro una AABB world-space, con filtro di ricostruzione selezionabile (trilineare di default, tricubico opzionale). Fuori dall'AABB: vuoto. Analogo a PBRT `GridMedium`, Arnold `volume` (modalità VDB) e V-Ray `VolumeGrid`. Due forme: dati inline nello YAML o file binario esterno `.vol`.

**Forma A — inline (per griglie piccole, ≤ 8³):**

```yaml
world:
  medium:
    type: "grid"
    sigma_a: [0.1, 0.1, 0.1]
    sigma_s: [3.0, 3.0, 3.2]
    bounds_min: [-1.5, 0.5, -1.5]
    bounds_max: [ 1.5, 3.5,  1.5]
    nx: 4
    ny: 4
    nz: 4
    interpolation: "trilinear"   # Opzionale: "trilinear" (default) o "tricubic"
    phase: "hg"
    g: 0.5
    data:
      # nx*ny*nz valori in [0,1]; layout z-major (y outer, x inner per slice z)
      - 0.0
      - 0.0
      # ... (64 valori totali per nx=ny=nz=4)
```

**Forma B — file binario `.vol` (consigliata per griglie ≥ 16³):**

```yaml
world:
  medium:
    type: "grid"
    sigma_a: [0.1, 0.1, 0.1]
    sigma_s: [3.0, 3.0, 3.2]
    interpolation: "tricubic"    # Smoothing Catmull-Rom
    phase: "hg"
    g: 0.5
    file: "cloud-64x64x64.vol"   # Path relativo allo YAML
```

Il formato `.vol` (VOL1) è: magic `"VOL1"` (4 byte) + `nx`, `ny`, `nz` (3 × int32 little-endian) + `bounds_min.{x,y,z}`, `bounds_max.{x,y,z}` (6 × float32 little-endian) + `nx*ny*nz` float32 di densità. È pensato come passo intermedio semplice: si può generare facilmente da Houdini/Blender tramite uno script Python.

**Uso tipico:** fumo localizzato, nuvole isolate, esplosioni, "asset" di fumo pre-simulati importati da altri software. La risoluzione della griglia non incide sul costo di rendering (solo sul parsing e sulla memoria).

**Filtro di ricostruzione (`interpolation`).** Quando un sample cade tra i voxel, 3D-Ray interpola la densità in uno dei due modi:

- **`trilinear`** (default, 8 taps, C⁰). Economico. A risoluzioni basse (≤ 16³) la derivata del campo di densità è discontinua ai confini delle celle → si vedono bande lineari nel render. È un artefatto universale dei renderer volumetrici a basso budget (Arnold, V-Ray, RenderMan) e in produzione si risolve usando griglie fitte (128³–1024³) dove i salti sono sub-pixel.
- **`tricubic`** (64 taps, C¹, cardinal spline Catmull-Rom con τ = 0.5). ~8× il costo per sample, ma il campo di densità è derivabile con continuità → niente kink anche su griglie minuscole. Il risultato viene clampato in `[0,1]` per preservare l'invariante del majorant del delta tracking. Alias accettati: `cubic`, `catmull-rom`, `smooth`. Corrisponde al filtro "cubic"/"smooth" offerto da Arnold, Houdini e RenderMan su VDB.

**Tip:** fuori dalla AABB il medium è vuoto → i raggi che non la intersecano sono gratis. Dimensiona bene i bounds per massimizzare le performance. Se usi `tricubic`, aspettati render ~5–10% più lenti sui raggi che attraversano la AABB.

---

## 9.5 Ricette pratiche

### Nebbia leggera (Light Fog)

Una foschia sottile che ammorbidisce gli oggetti distanti e aggiunge atmosfera senza oscurare la scena.

```yaml
world:
  medium:
    type: "homogeneous"
    sigma_a: [0.005, 0.005, 0.005]
    sigma_s: [0.04, 0.04, 0.04]
    phase: "hg"
    g: 0.8
```

### Caligine densa (Dense Mist)

La visibilità cala a poche unità. Le sorgenti luminose creano bagliori luminosi e drammatici.

```yaml
world:
  medium:
    type: "homogeneous"
    sigma_a: [0.01, 0.01, 0.01]
    sigma_s: [0.15, 0.15, 0.15]
    phase: "hg"
    g: 0.85
```

### Sott'acqua (Underwater)

L'acqua assorbe la luce rossa più velocemente di quella blu. Più si guarda in profondità, più la scena diventa blu. Uno scattering moderato crea fasci di luce visibili dalla superficie.

```yaml
world:
  medium:
    type: "homogeneous"
    sigma_a: [0.12, 0.06, 0.02]
    sigma_s: [0.02, 0.02, 0.02]
    phase: "hg"
    g: 0.6
```

### Foschia tinta (Golden Hour Atmosphere)

Foschia atmosferica calda che diffonde una luce giallo-oro, creando un effetto magico tipico dell'ora d'oro.

```yaml
world:
  medium:
    type: "homogeneous"
    sigma_a: [0.002, 0.005, 0.015]
    sigma_s: [0.03, 0.025, 0.015]
    phase: "hg"
    g: 0.75
```

### Fumo denso (Thick Smoke)

Mezzo molto denso, quasi opaco, con forte scattering isotropo.

```yaml
world:
  medium:
    type: "homogeneous"
    sigma_a: [0.05, 0.05, 0.05]
    sigma_s: [0.4, 0.38, 0.35]
    phase: "isotropic"
```

---

## 9.6 Considerazioni sul rendering

Il rendering volumetrico è più impegnativo del rendering solo superficiale. Tieni a mente questi suggerimenti:

1. **Aumentare i campioni (samples).** Il mezzo aggiunge un'altra fonte di rumore (eventi di scattering casuali lungo ogni raggio). `homogeneous` e `height_fog` sono analitici e già a 64 SPP danno risultati decenti. `procedural` e `grid` usano delta tracking → servono 256+ SPP per risultati puliti, 1024+ per publication-ready.

2. **Non esagerare con la profondità (depth).** Il path volumetrico è già gestito correttamente a `-d 6-8`. Russian Roulette termina automaticamente i path lunghi, quindi valori sopra `-d 10` raramente migliorano la qualità e costano sempre tempo.

3. **Firefly clamp con nebbia densa.** Mezzi con `sigma_s` alto + `-d 8+` talvolta producono spike luminosi rari. Abbassa `-C` a `25` o `15` senza timore: perdi poco dinamica, guadagni pulizia.

4. **`soft_radius` su point/spot light dentro la nebbia.** L'attenuazione 1/d² delle luci point e spot diverge quando un evento di scattering nel mezzo cade vicino all'emettitore, producendo pixel-firefly isolati che nessun aumento di `-s` riesce a livellare. Imposta `soft_radius` su quelle luci a un valore vicino al raggio fisico del bulbo (es. `0.10`–`0.25`): il denominatore viene clampato a `max(d², r²)`, la singolarità sparisce, e a distanze `d ≥ r` il look è invariato. È di gran lunga il singolo cambio più efficace per scene di lampioni nella nebbia.

5. **Le luci spot creano i fasci di luce (God Rays).** Una luce spot attraverso la nebbia produce un cono di luce visibile. Effetto spettacolare, specialmente con `procedural` (god-ray irregolari) o `height_fog` (god-ray che si rarefanno salendo).

6. **Le luci puntiformi brillano.** Nella nebbia ogni point light riceve un alone radiale morbido la cui dimensione dipende dalla densità del mezzo.

7. **Il mezzo è globale** (tranne `grid`, che è confinato alla AABB). `homogeneous`, `height_fog`, `procedural` riempiono l'intero spazio del mondo e colpiscono ogni raggio compresi quelli d'ombra. `grid` lascia passare senza attenuazione i raggi che non intersecano la sua AABB.

8. **Inizia da valori sottili, poi aumenta.** È più facile aggiungere nebbia che rimuoverla. Parti con `sigma_s` bassi (0.01–0.03 per homogeneous/height_fog, 0.3–0.5 per procedural/grid) e aumenta fino all'effetto desiderato.

9. **Phase function con `g` → 1** (es. HG con `g = 0.95`) rende god-ray più stretti e drammatici ma **aumenta la varianza**: se vedi coni rumorosi, abbassa `g` a 0.7-0.85 oppure passa a `double_hg` con pesi più equilibrati.

10. **`lights: []` + global medium → tendenza al nero.** Senza luci esplicite il classifier basato sul flusso considera la scena indirect-dominant e attiva la Russian Roulette conservativa (≥ 8 bounces, sopravvivenza minima 0.5). Con la nebbia che attenua ogni segmento, la luce dal solo gradient sky / HDRI fatica a raggiungere il sensore: il render esce molto scuro. Soluzione: aggiungi almeno una `directional` o `sphere` esplicita che dichiari il sole come ILight separato (l'HDRI/gradient resta come fill); il classifier passa a "direct-dominant" e i fasci di luce nella nebbia diventano visibili. Visto in pratica nella scena `scenes/foggy-hdri.yaml`.

---

## 9.7 Esempio Completo: Cattedrale nella Nebbia

```yaml
# cathedral-fog.yaml
# Pilastri di pietra nella nebbia con una luce spot che crea un raggio visibile.

world:
  ambient_light: [0.005, 0.005, 0.008]
  background: [0.01, 0.01, 0.02]
  medium:
    type: "homogeneous"
    sigma_a: [0.008, 0.008, 0.008]
    sigma_s: [0.07, 0.07, 0.07]
    phase: "hg"
    g: 0.82

cameras:
  - name: "main"
    position: [0, 1.5, -6]
    look_at: [0, 2, 2]
    fov: 55

lights:
  # L'effetto principale: una luce spot che crea un fascio visibile nella nebbia
  - type: "spot"
    position: [0, 4.8, 4]
    direction: [0, -0.7, -0.3]
    color: [1.0, 0.92, 0.75]
    intensity: 120.0
    inner_angle: 10
    outer_angle: 22

  # Lieve riempimento per far sì che i pilastri non siano completamente neri
  - type: "point"
    position: [0, 4, -4]
    color: [0.5, 0.55, 0.7]
    intensity: 8.0

materials:
  - id: "floor"
    type: "disney"
    roughness: 0.7
    texture:
      type: "checker"
      scale: 0.5
      colors: [[0.25, 0.22, 0.2], [0.15, 0.13, 0.12]]

  - id: "stone_pillar"
    type: "disney"
    roughness: 0.6
    specular: 0.3
    texture:
      type: "marble"
      scale: 5.0
      noise_strength: 3.0
      colors: [[0.65, 0.6, 0.55], [0.4, 0.37, 0.33]]
      randomize_offset: true

  - id: "ceiling"
    type: "lambertian"
    color: [0.2, 0.18, 0.16]

entities:
  # Pavimento
  - type: "infinite_plane"
    point: [0, 0, 0]
    normal: [0, 1, 0]
    material: "floor"

  # Soffitto
  - type: "infinite_plane"
    point: [0, 5, 0]
    normal: [0, -1, 0]
    material: "ceiling"

  # Fila sinistra di pilastri
  - type: "cylinder"
    center: [-2, 0, -2]
    radius: 0.3
    height: 5.0
    material: "stone_pillar"
    seed: 1

  - type: "cylinder"
    center: [-2, 0, 2]
    radius: 0.3
    height: 5.0
    material: "stone_pillar"
    seed: 2

  - type: "cylinder"
    center: [-2, 0, 6]
    radius: 0.3
    height: 5.0
    material: "stone_pillar"
    seed: 3

  # Fila destra di pilastri
  - type: "cylinder"
    center: [2, 0, -2]
    radius: 0.3
    height: 5.0
    material: "stone_pillar"
    seed: 4

  - type: "cylinder"
    center: [2, 0, 2]
    radius: 0.3
    height: 5.0
    material: "stone_pillar"
    seed: 5

  - type: "cylinder"
    center: [2, 0, 6]
    radius: 0.3
    height: 5.0
    material: "stone_pillar"
    seed: 6
```

Esegui il rendering con:

```
RayTracer -i cathedral-fog.yaml -w 1200 -H 800 -s 256 -d 12
```

La luce spot crea un drammatico fascio visibile che taglia la nebbia tra i pilastri. La funzione di fase HG concentrata in avanti (g=0.82) focalizza il bagliore attorno alla direzione del raggio, proprio come nella nebbia reale.

---

## 9.8 Scene di showcase

Nel repository trovi quattro showcase pronte, una per ogni tipo di mezzo, in `scenes/showcases/`:

| Scena | Tipo mezzo | Cosa mostra |
|---|---|---|
| `volumetric-01-homogeneous-showcase.yaml` | `homogeneous` | God-ray classico di uno spot in nebbia uniforme |
| `volumetric-02-height-fog-showcase.yaml` | `height_fog` | Aerial perspective outdoor con gradiente verticale |
| `volumetric-03-procedural-showcase.yaml` | `procedural` | Nebbia irregolare Perlin con god-ray non omogenei |
| `volumetric-04-grid-showcase.yaml` | `grid` | Fumo localizzato in una griglia 4³ inline |

Ogni scena include un header descrittivo e i comandi pronti per i profili Preview/Standard/Final.

---

## Cosa si è imparato

- **sigma_a** controlla l'assorbimento (oscuramento della luce con la distanza).
- **sigma_s** controlla lo scattering (densità della nebbia, fasci di luce).
- 3D-Ray supporta **quattro tipi di mezzo**: `homogeneous` (uniforme, analitico), `height_fog` (esponenziale in altezza, analitico), `procedural` (Perlin fBm, delta tracking) e `grid` (griglia 3D da dati o file `.vol`, delta tracking).
- Cinque phase function: `isotropic`, `hg`, `rayleigh` (atmosfera), `double_hg` (nubi realistiche), `schlick` (fast-HG).
- I mezzi analitici (`homogeneous`, `height_fog`) sono economici; quelli con delta tracking (`procedural`, `grid`) sono più rumorosi → più SPP.
- Il mezzo è globale e influenza tutti i raggi, tranne `grid` che è confinato alla sua AABB.
- Le scene volumetriche necessitano di più campioni rispetto a quelle solo-superficie; `-d 6-8` è sufficiente, non esagerare.
- Le luci spot nella nebbia creano fasci di luce (god rays); le luci puntiformi creano aloni.

---

[Precedente: Constructive Solid Geometry (CSG)](./08-csg.md) | [Successivo: Librerie di asset e scene complete](./10-libraries-and-projects.md) | [Indice del Tutorial](./README.md)
