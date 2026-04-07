# Review Completa — `docs/tutorials/`

> **Data:** 5 Aprile 2026
> **Scope:** Analisi strutturale, verifica link, copertura feature, audit preset e materiali

---

## 1. Struttura Documentale — Stato Attuale

La cartella `docs/tutorials/` è organizzata in 4 documenti principali con sotto-cartelle dedicate:

| # | Documento | Sotto-pagine | Valutazione |
|---|-----------|-------------|-------------|
| 01 | `01-tutorial-utilizzo.md` | — (monolitico) | ✅ Completo e ben strutturato |
| 02 | `02-tutorial-scene.md` (indice) | 11 sotto-pagine (01–11) | ✅ Copertura eccellente |
| 03 | `03-libreria-preset.md` (indice) | 9 sotto-pagine (01–09) | ⚠️ Lacune materiali (vedi §4) |
| 04 | `04-libreria-csg.md` (indice) | 9 sotto-pagine (01–09) | ✅ Completo |

---

## 2. Verifica Link

### 2.1 Link CORRETTI ✅

| Da | A | Stato |
|----|---|-------|
| `README.md` → `./docs/tutorials/01-tutorial-utilizzo.md` | Tutorial utilizzo | ✅ |
| `README.md` → `./docs/tutorials/02-tutorial-scene.md` | Tutorial scene | ✅ |
| `README.md` → `./docs/tutorials/03-libreria-preset.md` | Libreria preset | ✅ |
| `README.md` → `./docs/tutorials/04-libreria-csg.md` | Libreria CSG | ✅ |
| `01-tutorial-utilizzo.md` → `02-tutorial-scene/05-textures.md` | Textures | ✅ |
| `01-tutorial-utilizzo.md` → `02-tutorial-scene/03-camera.md` | Camera | ✅ |
| `02-tutorial-scene.md` → tutte le 11 sotto-pagine | Sotto-pagine | ✅ |
| `03-libreria-preset.md` → tutte le 9 sotto-pagine | Sotto-pagine | ✅ |
| `04-libreria-csg.md` → tutte le 9 sotto-pagine | Sotto-pagine | ✅ |
| Tutti i "← Torna all'indice" nelle sotto-pagine | Indici padre | ✅ |
| `02-tutorial-scene.md` → `03-libreria-preset.md` (Prossimi Passi) | Libreria preset | ✅ |
| `02-tutorial-scene.md` → `04-libreria-csg.md` (Prossimi Passi) | Libreria CSG | ✅ |
| `03-libreria-preset.md` → `04-libreria-csg.md` (nota in calce) | Libreria CSG | ✅ |
| `04-materials.md` → `../03-libreria-preset/05-materiali-pbr.md` | PBR preset | ✅ |
| `04-materials.md` → `../03-libreria-preset/04-materiali-base.md` | Materiali base | ✅ |

### 2.2 Link ERRATI 🔴

#### **Galleria Scene — Link alle scene YAML (11-galleria-scene-esempio.md)**

I link alle scene YAML nella galleria usano percorsi del tipo:

```markdown
[disney-bsdf-showcase.yaml](../scenes/disney-bsdf-showcase.yaml)
[cornell-box.yaml](../scenes/cornell-box.yaml)
```

Il file si trova in `docs/tutorials/02-tutorial-scene/11-galleria-scene-esempio.md`.
Il percorso `../scenes/` risolve a `docs/tutorials/scenes/` — **che non esiste**.
Le scene sono in `scenes/` alla radice del repo.

**Correzione:** Il percorso corretto è `../../../scenes/disney-bsdf-showcase.yaml` (3 livelli su).

**Tutti i link interessati:**
- `disney-bsdf-showcase.yaml`
- `normal-map-showcase.yaml`
- `image-texture-showcase.yaml`
- `gradient-sky-showcase.yaml`
- `hdri-showcase.yaml`
- `cornell-box.yaml`
- `cornell-box-crystal.yaml`

> **Nota:** Questo è l'unico blocco di link rotti nella documentazione, ma riguarda tutti i link della galleria scene. Sono ~7+ link da correggere.

#### **Ancora mancante: `#galleria-csg`**

In `04-libreria-csg.md`, il link:
```markdown
[→ Galleria Scene CSG Complete](02-tutorial-scene/11-galleria-scene-esempio.md#galleria-csg)
```
punta all'ancora `#galleria-csg`, ma nel file `11-galleria-scene-esempio.md` non è presente alcun heading con questo id. C'è una sezione generica su scene CSG (come il `cristallo.yaml` e `csg-showcase.yaml`) ma non c'è una sezione dedicata con un heading che generi l'ancora `#galleria-csg`.

**Correzione:** Aggiungere un heading `## Galleria CSG` (o equivalente che generi `#galleria-csg`) nel file `11-galleria-scene-esempio.md`, oppure modificare il link per puntare alla sezione corretta.

---

## 3. Copertura Feature — Analisi Completezza

### 3.1 Feature Implementate e Documentate ✅

| Feature | Tutorial Scene | Preset Lib | Note |
|---------|---------------|------------|------|
| Camera + DOF | 03-camera.md | 02-preset-camera.md | ✅ Completo |
| Multi-Camera | 03-camera.md + 01-utilizzo | 09-multi-camera.md | ✅ Completo |
| World / Background | 02-world.md | — | ✅ Completo |
| Gradient Sky + Sun disk | 02-world.md | 07-sky-hdri.md | ✅ Completo |
| HDRI / IBL | 02-world.md | 07-sky-hdri.md | ✅ Completo |
| Materiale Lambertian | 04-materials.md | 04-materiali-base.md | ✅ Completo |
| Materiale Metal | 04-materials.md | 04-materiali-base.md | ✅ Completo |
| Materiale Dielectric | 04-materials.md | 04-materiali-base.md | ✅ Completo |
| Materiale Emissive | 04-materials.md | 04-materiali-base.md | ✅ Completo con calibrazione |
| Materiale Disney BSDF | 04-materials.md | 05-materiali-pbr.md | ✅ Eccellente |
| Texture Checker | 05-textures.md | 04-materiali-base.md | ✅ |
| Texture Noise | 05-textures.md | — | ✅ |
| Texture Marble | 05-textures.md | 04-materiali-base.md | ✅ |
| Texture Wood | 05-textures.md | 04-materiali-base.md | ✅ |
| Texture Image | 05-textures.md | — | ✅ |
| Normal Map | 05-textures.md | — | ✅ |
| Randomize offset/rotation | 05-textures.md | — | ✅ |
| Sphere | 06-entities.md §6.1 | 06-oggetti-base.md | ✅ |
| Box | 06-entities.md §6.2 | 06-oggetti-base.md | ✅ (con min/max) |
| Cylinder | 06-entities.md §6.3 | 06-oggetti-base.md | ✅ |
| Cone / Frustum | 06-entities.md §6.4 | — | ✅ |
| Torus | 06-entities.md §6.5 (supposto) | 06-oggetti-base.md | ✅ |
| Capsule | 06-entities.md | 06-oggetti-base.md | ✅ |
| Annulus | 06-entities.md | 06-oggetti-base.md | ✅ |
| Disk | 06-entities.md | 06-oggetti-base.md | ✅ |
| Quad | 06-entities.md | 06-oggetti-base.md | ✅ |
| Triangle / SmoothTriangle | 06-entities.md | — | ✅ |
| Infinite Plane | 06-entities.md | — | ✅ (via world.ground) |
| Mesh / OBJ | 06-entities.md §6.15 | — | ✅ Completo |
| CSG | 06-entities.md §6.14 | 04-libreria-csg (intera) | ✅ Eccellente |
| Trasformazioni | 06-entities.md (inline) | — | ⚠️ Vedi §3.2 |
| Point Light | 07-lights.md §7.1 | 03-illuminazione.md | ✅ |
| Directional Light | 07-lights.md §7.2 | 03-illuminazione.md | ✅ |
| Spot Light | 07-lights.md §7.3 | 03-illuminazione.md | ✅ |
| Area Light | 07-lights.md §7.4 | 03-illuminazione.md | ✅ |
| Geometry Light (NEE) | 08-illuminazione.md | — | ✅ |
| Environment Light | 02-world.md | 07-sky-hdri.md | ✅ |
| CLI completo | 01-utilizzo.md | — | ✅ Completo |
| Profili render | 01-utilizzo.md | — | ✅ |
| Troubleshooting | 01-utilizzo.md §8 | — | ✅ Molto utile |
| Tools (TextureGen, NormalMapGen) | 01-utilizzo.md §9 | — | ✅ |

### 3.2 Lacune e Suggerimenti per Tutorial Mancanti ⚠️

#### **A. Trasformazioni — Sezione Dedicata Mancante**

Il sistema di trasformazioni (`translate`, `rotate`, `scale`) è spiegato *inline* in ciascuna entità, ma non esiste una sezione dedicata che spieghi:
- L'ordine di applicazione delle trasformazioni (Scale → Rotate → Translate)
- Come funziona `scale` come scalare unico vs array `[X, Y, Z]`
- Come le trasformazioni interagiscono con `min`/`max` per i Box
- Come le trasformazioni sui nodi CSG si propagano ai figli
- Gotcha comuni (es. ruotare un cilindro di 90° per farlo orizzontale)

**Raccomandazione:** Aggiungere una sezione `06-entities.md §6.16 Trasformazioni (Scale, Rotate, Translate)` oppure un paragrafo introduttivo prima delle entità specifiche.

#### **B. Seed e Determinismo — Non documentato come sezione**

Il campo `seed` è usato nelle scene (`primitive-showcase.yaml`, `sphere-showcase.yaml`) ma non è documentato esplicitamente nel tutorial. Dalla sorgente, ogni entità accetta un campo opzionale `seed: <int>` che controlla la randomizzazione delle texture procedurali.

**Raccomandazione:** Aggiungere una nota nel §5.3 (Randomizzazione) o nel §6 (Entities) che documenti il campo `seed` a livello di entità.

#### **C. Alias dei Tipi — Tabella Riassuntiva Mancante**

SceneLoader.cs supporta molteplici alias per diversi tipi, ma non c'è un riassunto unico. Dall'analisi del codice:

| Tipo Canonico | Alias Accettati |
|---------------|-----------------|
| `cone` | `truncated_cone`, `frustum` |
| `torus` | `donut`, `ring` |
| `capsule` | `pill`, `sphylinder` |
| `annulus` | `ring_disk`, `washer` |
| `plane` | `infinite_plane` |
| `mesh` | `obj` |
| `directional` | `sun` |
| `spot` | `spotlight` |
| `area` | `area_light`, `rect`, `rect_light` |
| `disney` | `disney_bsdf`, `pbr` |

Alcuni di questi sono documentati nelle rispettive sezioni, ma una tabella unica sarebbe molto utile.

**Raccomandazione:** Aggiungere in `10-regole.md` o in `01-struttura-file.md` una tabella completa degli alias.

---

## 4. Audit Preset e Materiali — Materiali Mancanti

### 4.1 Materiali referenziati in `06-oggetti-base.md` ma ASSENTI dal catalogo materiali

Il documento `06-oggetti-base.md` (Oggetti e Primitive Base) usa nomi di materiale nei suoi snippet YAML che non hanno una definizione nel catalogo `04-materiali-base.md` né in `05-materiali-pbr.md`. L'utente che copia un oggetto preset non trova il materiale corrispondente.

| Materiale | Usato in | Presente in 04? | Presente in 05? |
|-----------|---------|-----------------|-----------------|
| `marmo_base` | Piedistallo Moderno | ❌ | ❌ |
| `metallo_scuro` | Base Espositiva Circolare | ❌ | ❌ |
| `muro_bianco` | Parete con Cornice | ❌ | ❌ |
| `gomma_rossa` | O-ring | ❌ | ❌ |
| `gomma_nera` | Guarnizione Piatta | ❌ | ❌ (`gomma` esiste in 05) |
| `neon_rosa` | Neon Circolare | ❌ | ❌ |
| `neon_bianco` | Neon Anello Piatto | ❌ | ❌ |
| `rosso` | Bersaglio (anello) | ❌ | ❌ |
| `bianco` | Bersaglio (anello) | ❌ | ❌ |

### 4.2 Materiali referenziati in `09-esempi.md` ma assenti dal catalogo

| Materiale | Usato in | Presente? |
|-----------|---------|-----------|
| `pavimento` | Showcase Materiali, Scena Arch. | ❌ Inline, ok |
| `pavimento_chiaro` | Scena Architetturale | ❌ Inline, ok |
| `rosso` | Scena Minima | ❌ |
| `acciaio` | Scena Normal Map | Sì (04) ✅ |

> **Nota:** Negli esempi completi del §9 i materiali sono definiti inline nella scena stessa — questo è corretto e intenzionale. Ma il documento `06-oggetti-base.md` fornisce solo *snippet di entità* senza i materiali, il che è problematico.

### 4.3 Materiali referenziati nella Libreria CSG ma assenti dal catalogo generale

Il documento `04-libreria-csg/07-materiali.md` fornisce un proprio catalogo autonomo di materiali per CSG. Molti di questi sono duplicati (con parametri leggermente diversi) di quelli in `04-materiali-base.md` e `05-materiali-pbr.md`:
- `acciaio_satinato` — presente in entrambi ✅
- `vetro_chiaro` — presente in 05 ✅
- `diamante` — presente in 05 ✅
- `oro` — presente in entrambi ✅
- `plastica_bianca` — solo in CSG materiali, assente nel catalogo generale ❌
- `metallo_ruggine` — solo in CSG materiali ❌
- `marmo_bianco` — diverso dal `marmo_carrara` in 04 (altro parametro noise)
- `pietra` — solo in CSG materiali ❌
- `cemento` — solo in CSG materiali ❌
- `mattoni` — solo in CSG materiali ❌
- `ceramica_bianca` — solo in CSG materiali ❌
- `avorio` — solo in CSG materiali ❌
- `luce_calda` — solo in CSG materiali ❌ (`led_caldo` in 04 ha parametri diversi)

---

## 5. Proposte di Miglioramento

### 5.1 CRITICO — Correggere i link rotti nella galleria scene

**File:** `docs/tutorials/02-tutorial-scene/11-galleria-scene-esempio.md`

Tutti i link `(../scenes/*.yaml)` devono diventare `(../../../scenes/*.yaml)`.

### 5.2 CRITICO — Aggiungere l'ancora `#galleria-csg`

**File:** `docs/tutorials/02-tutorial-scene/11-galleria-scene-esempio.md`

Aggiungere un heading dedicato per le scene CSG:
```markdown
## Galleria CSG
```

### 5.3 ALTO — Completare il catalogo materiali `04-materiali-base.md`

Aggiungere le seguenti definizioni di materiali mancanti:

```yaml
## Materiali Base Solidi

### Bianco Puro
  - id: "bianco"
    type: "lambertian"
    color: [0.92, 0.92, 0.92]

### Rosso Base
  - id: "rosso"
    type: "lambertian"
    color: [0.75, 0.15, 0.10]

### Grigio Medio
  - id: "grigio"
    type: "lambertian"
    color: [0.5, 0.5, 0.5]

### Nero Opaco
  - id: "nero"
    type: "lambertian"
    color: [0.03, 0.03, 0.03]

## Superfici Architettoniche

### Muro Bianco
  - id: "muro_bianco"
    type: "lambertian"
    color: [0.88, 0.86, 0.82]

### Pavimento Generico (Scacchiera Discreta)
  - id: "pavimento"
    type: "lambertian"
    texture:
      type: "checker"
      scale: 2.0
      colors: [[0.15, 0.15, 0.15], [0.25, 0.25, 0.25]]

### Cemento
  - id: "cemento"
    type: "lambertian"
    texture:
      type: "noise"
      scale: 8.0
      colors: [[0.60, 0.60, 0.60], [0.50, 0.50, 0.50]]

### Mattoni
  - id: "mattoni"
    type: "lambertian"
    color: [0.55, 0.25, 0.18]

### Pietra
  - id: "pietra"
    type: "lambertian"
    texture:
      type: "noise"
      scale: 6.0
      colors: [[0.55, 0.52, 0.48], [0.42, 0.40, 0.36]]

## Metalli Aggiuntivi

### Metallo Scuro
  - id: "metallo_scuro"
    type: "metal"
    color: [0.12, 0.12, 0.15]
    fuzz: 0.08

### Metallo Ruggine (Disney)
  - id: "metallo_ruggine"
    type: "disney"
    color: [0.55, 0.28, 0.15]
    metallic: 0.7
    roughness: 0.8

## Pietre e Ceramiche

### Marmo Base (Generico)
  - id: "marmo_base"
    type: "lambertian"
    texture:
      type: "marble"
      scale: 12.0
      noise_strength: 8.0
      colors: [[0.90, 0.88, 0.85], [0.50, 0.48, 0.45]]
      randomize_offset: true

### Marmo Bianco (Calacatta Style)
  - id: "marmo_bianco"
    type: "lambertian"
    texture:
      type: "marble"
      scale: 15.0
      noise_strength: 10.0
      colors: [[0.96, 0.96, 0.96], [0.55, 0.55, 0.55]]
      randomize_offset: true

### Ceramica Bianca
  - id: "ceramica_bianca"
    type: "disney"
    color: [0.95, 0.93, 0.90]
    roughness: 0.2
    specular: 0.5

### Avorio
  - id: "avorio"
    type: "lambertian"
    color: [0.96, 0.93, 0.82]

## Gomme

### Gomma Nera
  - id: "gomma_nera"
    type: "disney"
    color: [0.05, 0.05, 0.05]
    roughness: 0.95
    specular: 0.1

### Gomma Rossa
  - id: "gomma_rossa"
    type: "disney"
    color: [0.7, 0.08, 0.05]
    roughness: 0.9
    specular: 0.08

## Plastiche Aggiuntive

### Plastica Bianca
  - id: "plastica_bianca"
    type: "disney"
    color: [0.92, 0.92, 0.92]
    roughness: 0.6
    metallic: 0.0

### Plastica Arancione
  - id: "plastica_arancio"
    type: "disney"
    color: [0.9, 0.45, 0.05]
    roughness: 0.5
    metallic: 0.0

## Emissivi Aggiuntivi

### Neon Rosa
  - id: "neon_rosa"
    type: "emissive"
    color: [1.0, 0.2, 0.6]
    intensity: 12.0

### Neon Bianco (Daylight)
  - id: "neon_bianco"
    type: "emissive"
    color: [1.0, 0.98, 0.95]
    intensity: 15.0

### Neon Blu
  - id: "neon_blu"
    type: "emissive"
    color: [0.2, 0.4, 1.0]
    intensity: 8.0

### Luce Calda (Emissive per scene CSG)
  - id: "luce_calda"
    type: "emissive"
    color: [1.0, 0.85, 0.6]
    intensity: 15.0
```

### 5.4 MEDIO — Nuovi preset consigliati

#### Preset Illuminazione Mancanti

Il catalogo `03-illuminazione.md` copre bene i setup classici ma manca di:

**Preset: Rim Light Drammatico (Contorno)**
```yaml
lights:
  - type: "point"
    position: [0, 4, 5]      # Dietro il soggetto
    color: [1.0, 0.95, 0.85]
    intensity: 80
  - type: "point"
    position: [0, 2, -3]     # Fill frontale tenue
    color: [0.5, 0.5, 0.6]
    intensity: 5
```

**Preset: Cross Lighting (Due spot incrociati)**
```yaml
lights:
  - type: "spot"
    position: [-5, 6, -3]
    direction: [0.5, -0.6, 0.3]
    color: [1.0, 0.9, 0.75]
    intensity: 80
    inner_angle: 12
    outer_angle: 28
  - type: "spot"
    position: [5, 6, -3]
    direction: [-0.5, -0.6, 0.3]
    color: [0.75, 0.85, 1.0]
    intensity: 60
    inner_angle: 12
    outer_angle: 28
```

**Preset: HDRI-Only (Zero luci, solo environment map)**
```yaml
world:
  ambient_light: [0.0, 0.0, 0.0]
  sky:
    type: "hdri"
    path: "hdri/your_hdri.hdr"
    intensity: 1.2
lights: []   # Nessuna luce esplicita — solo HDRI
```

#### Preset Camera Mancanti

**Preset: Low Angle (Angolo dal Basso)**
```yaml
camera:
  position: [2, 0.3, -5]
  look_at: [0, 2, 0]
  fov: 55
```

**Preset: Macro con DOF Estremo**
```yaml
camera:
  position: [0.3, 1.5, -2.5]
  look_at: [0, 1.2, 0]
  fov: 25
  aperture: 0.25
  focal_dist: 2.8
```

#### Preset Materiali PBR Mancanti in `05-materiali-pbr.md`

**Ceramica (usata nelle scene showcase)**
```yaml
- id: "ceramica"
  type: "disney"
  color: [0.92, 0.88, 0.82]
  roughness: 0.3
  metallic: 0.0
  specular: 0.5
```

**Vetro Ambra (usato in primitive-showcase)**
```yaml
- id: "vetro_ambra"
  type: "disney"
  color: [0.9, 0.6, 0.2]
  roughness: 0.0
  spec_trans: 1.0
  ior: 1.5
```

**Vetro Ottico (presente solo nei materiali CSG)**
```yaml
- id: "vetro_ottico"
  type: "disney"
  color: [0.97, 0.99, 1.0]
  roughness: 0.0
  spec_trans: 1.0
  ior: 1.62
```

**Cera / Candela (subsurface)**
```yaml
- id: "cera"
  type: "disney"
  color: [0.90, 0.82, 0.60]
  roughness: 0.7
  subsurface: 0.6
  specular: 0.15
```

**Vernice Auto Blu**
```yaml
- id: "vernice_auto_blu"
  type: "disney"
  color: [0.05, 0.15, 0.65]
  roughness: 0.3
  clearcoat: 1.0
  clearcoat_gloss: 0.95
```

**Rame Ossidato (Patina verde)**
```yaml
- id: "rame_ossidato"
  type: "disney"
  color: [0.35, 0.55, 0.45]
  metallic: 0.6
  roughness: 0.7
```

**Ottone**
```yaml
- id: "ottone"
  type: "disney"
  color: [0.78, 0.57, 0.11]
  metallic: 1.0
  roughness: 0.25
```

**Legno Laccato (Metal + Wood texture)**
```yaml
- id: "legno_laccato"
  type: "metal"
  fuzz: 0.08
  texture:
    type: "wood"
    scale: 22.0
    colors: [[0.35, 0.05, 0.02], [0.15, 0.02, 0.01]]
```

### 5.5 BASSO — Miglioramenti strutturali

#### A. Tabella alias YAML completa

Aggiungere in `10-regole.md` dopo la regola 12:

```markdown
## Alias dei Tipi YAML

Molti tipi accettano nomi alternativi per comodità:

| Tipo | Alias accettati |
|------|-----------------|
| `cone` | `truncated_cone`, `frustum` |
| `torus` | `donut`, `ring` |
| `capsule` | `pill`, `sphylinder` |
| `annulus` | `ring_disk`, `washer` |
| `plane` | `infinite_plane` |
| `mesh` | `obj` |
| `disney` | `disney_bsdf`, `pbr` |
| `directional` | `sun` |
| `spot` | `spotlight` |
| `area` | `area_light`, `rect`, `rect_light` |
```

#### B. Sezione Seed in `05-textures.md`

Aggiungere dopo §5.3:

```markdown
## 5.3.1 Seed Deterministico per Entità

Ogni entità accetta un campo opzionale `seed`:

| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `seed` | int | *(auto)* | Seed per la randomizzazione delle texture. Se omesso, viene calcolato deterministicamente da indice, tipo e nome dell'entità. |

Un seed fisso garantisce che le venature di marmo/legno siano identiche 
tra render successivi — utile per iterare sull'illuminazione senza 
cambiare l'aspetto dei materiali.
```

---

## 6. Riepilogo Azioni

| # | Priorità | Azione | File coinvolti |
|---|----------|--------|---------------|
| 1 | 🔴 Critico | Correggere link scene nella galleria (`../scenes/` → `../../../scenes/`) | `11-galleria-scene-esempio.md` |
| 2 | 🔴 Critico | Aggiungere ancora `## Galleria CSG` | `11-galleria-scene-esempio.md` |
| 3 | 🟠 Alto | Aggiungere ~20 materiali mancanti al catalogo | `04-materiali-base.md` |
| 4 | 🟠 Alto | Aggiungere ~8 materiali PBR mancanti | `05-materiali-pbr.md` |
| 5 | 🟡 Medio | Aggiungere preset illuminazione (rim, cross, HDRI-only) | `03-illuminazione.md` |
| 6 | 🟡 Medio | Aggiungere preset camera (low angle, macro DOF) | `02-preset-camera.md` |
| 7 | 🟡 Medio | Sezione trasformazioni dedicata | `06-entities.md` |
| 8 | 🟢 Basso | Tabella alias tipi YAML | `10-regole.md` |
| 9 | 🟢 Basso | Documentare campo `seed` | `05-textures.md` |

---

*Fine della review.*
