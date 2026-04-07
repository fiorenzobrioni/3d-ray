# 4. Sezione `materials`

I materiali definiscono come le superfici interagiscono con la luce.

> **Normal mapping:** Qualsiasi materiale (Lambertian, Metal, Dielectric, Emissive) accetta un campo opzionale `normal_map` che aggiunge dettaglio di superficie senza geometria aggiuntiva. Vedi [sezione 5.5](05-textures.md#55-normal-map) per la sintassi completa.

## 4.1 Lambertian (Diffuso/Opaco)
Materiale opaco che diffonde la luce uniformemente in tutte le direzioni.
```yaml
  - id: "rosso_opaco"
    type: "lambertian"
    color: [0.8, 0.2, 0.1]
```

Con texture e normal map:
```yaml
  - id: "muro_mattoni"
    type: "lambertian"
    texture:
      type: "image"
      path: "textures/brick-wall.png"
      uv_scale: [2, 2]
    normal_map:
      path: "textures/brick-wall-normal.png"
      strength: 1.2
      uv_scale: [2, 2]
```

## 4.2 Metal (Metallico/Speculare)
Riflette la luce come uno specchio. Il parametro `fuzz` controlla la rugosità.

```yaml
  - id: "argento"
    type: "metal"
    color: [0.9, 0.9, 0.9]
    fuzz: 0.05
```

Con normal map per graffi e imperfezioni:
```yaml
  - id: "acciaio_graffiato"
    type: "metal"
    color: [0.85, 0.85, 0.88]
    fuzz: 0.03
    normal_map:
      path: "textures/metal-scratched-normal.png"
      strength: 1.5
```

| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `color` | `[R, G, B]` | `[0.5, 0.5, 0.5]` | Colore di riflessione (tint metallico) |
| `fuzz` | float | `0.0` | Rugosità: `0` = specchio perfetto, `1` = molto opaco |

## 4.3 Dielectric (Vetro/Trasparente)
Materiale trasparente con rifrazione e riflesso Fresnel.
```yaml
  - id: "vetro"
    type: "dielectric"
    refraction_index: 1.52
```

| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `refraction_index` | float | `1.5` | Indice di rifrazione (IOR). Vetro comune = 1.52, diamante = 2.42, acqua = 1.33, aria = 1.00029 |
| `color` | `[R, G, B]` | `[1, 1, 1]` | Tint del vetro (es. `[0.8, 1.0, 0.8]` per vetro verde) |

> Il materiale Dielectric supporta `normal_map` per simulare vetro satinato o brocche intagliate.

## 4.4 Emissive (Luminoso)
Materiale auto-luminoso: l'oggetto emette luce propria e brilla nella scena senza bisogno di illuminazione esterna. La luce emessa si propaga tramite i rimbalzi del path tracer. Tutte le primitive geometriche (Sphere, Box, Cylinder, Cone, Torus, Capsule, Annulus, Mesh, SmoothTriangle, Quad, Triangle, Disk) supportano `ISamplable` e partecipano alla NEE come Geometry Lights quando usate con materiale emissivo, riducendo significativamente il rumore rispetto al path tracing puro.

Usi tipici: neon, LED, insegne, lava, fiamme, sfere magiche, pannelli luminosi, indicatori.

```yaml
  - id: "neon_blu"
    type: "emissive"
    color: [0.2, 0.4, 1.0]
    intensity: 8.0
```

| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `color` | `[R, G, B]` | `[0.5, 0.5, 0.5]` | Colore della luce emessa |
| `intensity` | float | `1.0` | Moltiplicatore di luminosità. La radiance emessa è `color × intensity`. |
| `texture` | oggetto | — | Opzionale: texture procedurale per emissione non uniforme (es. lava con texture marble) |

> **Comportamento nel path tracer:**
> - L'emissione è **additiva** e non dipende dall'illuminazione esterna: l'oggetto è visibile anche in una scena completamente buia.
> - L'emissione avviene solo dalla **front face** — il retro della superficie è buio, come un vero pannello LED.
> - Gli oggetti emissivi **non scatterano** raggi: non hanno componente diffusa né speculare. Tutta la loro energia va in emissione.
> - L'illuminazione indiretta funziona naturalmente: un neon magenta colora di rosa le pareti vicine tramite i rimbalzi del path tracer. Usa campioni alti (`-s 64+`) per risultati puliti.

### Calibrazione dell'intensità emissiva

| Effetto desiderato | Range `intensity` | Note |
|--------------------|-------------------|------|
| Glow tenue (indicatore, LED spento) | 0.5 – 2 | Appena visibile, non illumina la scena |
| Neon / LED visibile | 3 – 10 | L'oggetto brilla e colora leggermente i dintorni |
| Pannello luminoso (sorgente primaria) | 10 – 25 | Illumina la scena come una area light |
| Lava / plasma (over-bright) | 25 – 100 | Effetto bloom, satura il tone mapping ACES |

> **💡 Tip: Emissive con texture procedurale.** Puoi usare una texture `marble` o `noise` su un materiale emissivo per creare effetti lava, plasma o pattern luminosi non uniformi:
> ```yaml
>   - id: "lava"
>     type: "emissive"
>     intensity: 15.0
>     texture:
>       type: "marble"
>       scale: 3.0
>       noise_strength: 6.0
>       colors: [[1.0, 0.3, 0.0], [1.0, 0.8, 0.0]]
> ```

> **💡 Tip: Emissive vs Area Light.** Un `quad` con materiale `emissive` è visualmente simile a un'area light, ma con differenze importanti:
> - L'**area light** usa Next Event Estimation (NEE) e produce ombre morbide controllate con `shadow_samples`.
> - L'**emissive** illumina tramite rimbalzi del path tracer e, per le geometrie campionabili (Sphere, Box, Cylinder, Cone, Torus, Capsule, Annulus, Mesh, SmoothTriangle, Quad, Triangle, Disk), anche tramite NEE diretta. Richiede più campioni (`-s`) per convergere, ma l'oggetto è fisicamente **visibile** nella scena (puoi vederlo, rifletterlo nello specchio, rifrangerlo nel vetro).
> - Per pannelli a soffitto che devono essere visti: usa `emissive`. Per illuminazione pura senza geometria visibile: usa `area` light.

---

## 4.5 Disney Principled BSDF (PBR Unificato)

Il materiale più potente del renderer. Un singolo tipo può rappresentare qualsiasi superficie reale attraverso la combinazione di più lobi fisici. Ispirato al modello di Brent Burley (*"Physically Based Shading at Disney"*, SIGGRAPH 2012).

**Dichiarazione minima:**
```yaml
- id: "plastica"
  type: "disney"
  color: [0.8, 0.2, 0.1]
  roughness: 0.4
```

**Alias YAML validi:** `"disney"`, `"disney_bsdf"`, `"pbr"` (tutti equivalenti).

**Parametri completi:**

| Parametro | Range | Default | Descrizione |
|-----------|-------|---------|-------------|
| `color` | `[R,G,B]` | — | Colore base (albedo diffuso o colore metallico) |
| `metallic` | 0–1 | `0` | 0 = dielettrico (plastica, legno, pelle); 1 = metallo (oro, cromo) |
| `roughness` | 0–1 | `0.5` | 0 = superficie a specchio; 1 = perfettamente diffuso |
| `subsurface` | 0–1 | `0` | Approssimazione SSS: 0 = Lambert, 1 = effetto cera/pelle |
| `specular` | 0–2 | `0.5` | Intensità del lobe speculare dielettrico (controlla F0) |
| `specular_tint` | 0–1 | `0` | Tinta lo specular verso `color`. 0 = bianco, 1 = tinta completa |
| `sheen` | 0–1 | `0` | Lucentezza a radente (tessuti, velluto) |
| `sheen_tint` | 0–1 | `0.5` | Tinta lo sheen verso `color` |
| `clearcoat` | 0–1 | `0` | Secondo lobe speculare (vernice auto, lacca) |
| `clearcoat_gloss` | 0–1 | `1` | Lucidità del clearcoat: 1 = a specchio, 0 = satinato |
| `spec_trans` | 0–1 | `0` | Trasmissione speculare: 0 = opaco, 1 = vetro |
| `ior` | ≥1.0 | `1.5` | Indice di rifrazione per trasmissione e specular dielettrico |

**Texture** e **normal_map** sono supportati esattamente come negli altri materiali.

**Esempi per tipo di superficie:**

```yaml
# Plastica opaca
- id: "plastica_rossa"
  type: "disney"
  color: [0.8, 0.1, 0.1]
  roughness: 0.8
  metallic: 0.0

# Oro
- id: "oro"
  type: "disney"
  color: [1.0, 0.71, 0.29]
  metallic: 1.0
  roughness: 0.15

# Cromo a specchio
- id: "cromo"
  type: "disney"
  color: [0.95, 0.93, 0.88]
  metallic: 1.0
  roughness: 0.02

# Vernice auto (rosso con clearcoat)
- id: "vernice_auto"
  type: "disney"
  color: [0.7, 0.05, 0.05]
  roughness: 0.3
  clearcoat: 1.0
  clearcoat_gloss: 0.9

# Velluto
- id: "velluto_blu"
  type: "disney"
  color: [0.05, 0.1, 0.5]
  roughness: 0.9
  sheen: 1.0
  sheen_tint: 0.8

# Pelle / cera (subsurface scattering)
- id: "pelle"
  type: "disney"
  color: [0.85, 0.6, 0.45]
  roughness: 0.6
  subsurface: 0.4
  specular: 0.2

# Vetro trasparente
- id: "vetro"
  type: "disney"
  color: [1.0, 1.0, 1.0]
  roughness: 0.0
  spec_trans: 1.0
  ior: 1.5
```

**Quando usare Disney vs materiali classici:**

| Vuoi... | Usa | Perché |
|---------|-----|--------|
| Pavimenti, muri, soffitti | `lambertian` | Massima pulizia, zero rumore da lobi multipli |
| Superfici di sfondo (tavoli, piedistalli) | `lambertian` o `metal` | Il Fresnel Disney non giustifica il rumore su superfici non protagoniste |
| Metalli (oro, cromo, rame, acciaio) | `disney` metallic=1.0 | **Nessun rumore aggiuntivo** — un solo lobo attivo, GGX corretto |
| Plastica, ceramica (oggetti protagonisti) | `disney` | Fresnel realistico, roughness GGX, subsurface |
| Vernice auto, lacca (clearcoat) | `disney` con clearcoat | Due strati speculari — impossibile con classici |
| Tessuto, velluto (sheen) | `disney` con sheen | Riflesso radente — impossibile con classici |
| Pelle, cera (subsurface) | `disney` con subsurface | Scattering sottosuperficiale — impossibile con classici |
| Vetro chiaro e semplice | `dielectric` | Più pulito e veloce |
| Vetro colorato o smerigliato | `disney` spec_trans | Tint del colore + roughness non disponibili in `dielectric` |

> **⚠️ Rumore e sample count:** Il materiale Disney utilizza una selezione stocastica a 5 lobi che lo rende **intrinsecamente più rumoroso** dei materiali classici a parità di campioni. Non è un difetto — è il costo della maggiore accuratezza fisica. Per compensare:
>
> - **Approccio misto** (consigliato): usa `lambertian` per pavimenti e superfici grandi, `disney` solo per gli oggetti protagonisti. Questo riduce drasticamente il rumore senza perdere realismo dove conta.
> - **Tutto Disney**: aumenta i campioni. Una scena tutta Disney richiede circa **4× i campioni** di una scena classica per lo stesso livello di pulizia (es. 256 spp invece di 64).
> - **Disney metallici**: l'eccezione — con `metallic: 1.0` (e senza clearcoat/sheen) il Disney ha un solo lobo attivo e produce lo stesso rumore del `metal` classico. Usali liberamente anche su grandi superfici.

> **💡 Regola pratica:** Se la superficie è grande e lontana dalla camera, usa un classico. Se la superficie è un oggetto protagonista con feature uniche (clearcoat, sheen, subsurface, vetro smerigliato), usa Disney e alza i campioni.

---

## 4.6 Mix Material (Blending tra Materiali)

Il **Mix Material** interpola tra due materiali usando un peso costante o una texture come maschera spaziale. Essenziale per effetti di weathering (ruggine su metallo), usura, transizioni graduali, decal, e qualsiasi situazione in cui due materiali coesistono sulla stessa superficie.

### Sintassi YAML

#### Blend costante

```yaml
materials:
  - id: "metallo_pulito"
    type: "metal"
    color: [0.85, 0.85, 0.88]
    fuzz: 0.02

  - id: "ruggine"
    type: "disney"
    color: [0.55, 0.25, 0.10]
    roughness: 0.9
    metallic: 0.3

  - id: "metallo_arrugginito"
    type: "mix"
    material_a: "metallo_pulito"   # Materiale per blend → 0
    material_b: "ruggine"          # Materiale per blend → 1
    blend: 0.4                     # 40% ruggine, 60% metallo pulito
```

#### Maschera texture (blend spaziale)

```yaml
  - id: "weathered"
    type: "mix"
    material_a: "metallo_pulito"
    material_b: "ruggine"
    mask:                            # Qualsiasi tipo di texture
      type: "noise"
      scale: 3.0
      noise_strength: 5.0
```

Quando è specificata una `mask`, il campo `blend` viene ignorato. La luminanza (Rec.709) del colore della texture ad ogni punto della superficie determina il fattore di blend.

#### Parametri

| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `type` | stringa | — | `"mix"` (alias: `"blend"`) |
| `material_a` | stringa | — (**obbligatorio**) | ID del primo materiale (blend=0) |
| `material_b` | stringa | — (**obbligatorio**) | ID del secondo materiale (blend=1) |
| `blend` | float | `0.5` | Peso costante [0, 1]. Ignorato se `mask` è presente. |
| `mask` | TextureData | — | Texture che guida il blend spazialmente. Luminanza del colore = fattore di blend. |
| `normal_map` | NormalMapData | — | Normal map opzionale applicata al mix. |

### Tipi di Maschera

Qualsiasi texture supportata dal motore può essere usata come maschera:

**Noise (Perlin)** — Pattern organici e irregolari, ideale per ruggine, sporco, usura naturale:
```yaml
    mask:
      type: "noise"
      scale: 4.0
      noise_strength: 3.0
```

**Marble** — Transizioni venate, effetto pietra o crepe:
```yaml
    mask:
      type: "marble"
      scale: 8.0
      noise_strength: 6.0
      colors: [[1.0, 1.0, 1.0], [0.0, 0.0, 0.0]]
```

**Wood** — Anelli concentrici, transizioni circolari:
```yaml
    mask:
      type: "wood"
      scale: 3.0
      noise_strength: 1.5
      colors: [[0.9, 0.9, 0.9], [0.1, 0.1, 0.1]]
```

**Checker** — Pattern regolare a scacchiera:
```yaml
    mask:
      type: "checker"
      scale: 6.0
      colors: [[1.0, 1.0, 1.0], [0.0, 0.0, 0.0]]
```

**Image** — Maschera da file immagine (grayscale o colore):
```yaml
    mask:
      type: "image"
      path: "textures/rust_mask.png"
      uv_scale: [2, 2]
```

> **Tip:** Per le maschere procedurali (noise, marble, wood), i `colors` controllano le due estremità del blend. Bianco `[1,1,1]` = 100% material_b, nero `[0,0,0]` = 100% material_a. Per maschere con colori custom, viene usata la luminanza Rec.709: `L = 0.2126R + 0.7152G + 0.0722B`.

### Esempi Pratici

#### Ruggine su metallo

```yaml
materials:
  - id: "cromo"
    type: "metal"
    color: [0.85, 0.85, 0.88]
    fuzz: 0.02

  - id: "ruggine"
    type: "disney"
    color: [0.55, 0.25, 0.10]
    roughness: 0.9
    metallic: 0.3

  - id: "metallo_corroso"
    type: "mix"
    material_a: "cromo"
    material_b: "ruggine"
    mask:
      type: "noise"
      scale: 3.0
      noise_strength: 5.0

entities:
  - name: "tubo_arrugginito"
    type: "cylinder"
    center: [0, 0, 0]
    radius: 0.3
    height: 3.0
    material: "metallo_corroso"
```

#### Lava che si raffredda

```yaml
materials:
  - id: "roccia_scura"
    type: "lambertian"
    color: [0.15, 0.12, 0.10]

  - id: "lava_incandescente"
    type: "emissive"
    color: [1.0, 0.35, 0.05]
    intensity: 8.0

  - id: "lava_cooling"
    type: "mix"
    material_a: "roccia_scura"
    material_b: "lava_incandescente"
    mask:
      type: "marble"
      scale: 5.0
      noise_strength: 8.0
      colors: [[0.0, 0.0, 0.0], [1.0, 1.0, 1.0]]

entities:
  - name: "colata_lavica"
    type: "sphere"
    center: [0, 1, 0]
    radius: 1.0
    material: "lava_cooling"
```

#### Vernice scrostata su legno

```yaml
materials:
  - id: "legno_naturale"
    type: "lambertian"
    texture:
      type: "wood"
      scale: 4.0
      noise_strength: 2.0

  - id: "vernice_blu"
    type: "disney"
    color: [0.15, 0.30, 0.65]
    roughness: 0.3
    clearcoat: 0.6

  - id: "vernice_scrostata"
    type: "mix"
    material_a: "vernice_blu"
    material_b: "legno_naturale"
    mask:
      type: "noise"
      scale: 5.0
      noise_strength: 4.0
```

#### Patina su bronzo (mix-of-mix)

I MixMaterial possono referenziare altri MixMaterial per composizioni complesse:

```yaml
materials:
  - id: "bronzo"
    type: "metal"
    color: [0.80, 0.50, 0.20]
    fuzz: 0.08

  - id: "patina_verde"
    type: "disney"
    color: [0.30, 0.55, 0.35]
    roughness: 0.85

  - id: "sporco"
    type: "lambertian"
    color: [0.20, 0.18, 0.12]

  # Prima: bronzo + patina
  - id: "bronzo_patinato"
    type: "mix"
    material_a: "bronzo"
    material_b: "patina_verde"
    mask:
      type: "noise"
      scale: 3.0
      noise_strength: 4.0

  # Poi: bronzo patinato + sporco
  - id: "statua_antica"
    type: "mix"
    material_a: "bronzo_patinato"
    material_b: "sporco"
    blend: 0.15
```

### Come Funziona (Dettagli Tecnici)

#### Scatter (illuminazione indiretta)

Il MixMaterial usa **selezione stocastica dei lobi**: ad ogni intersezione raggio-superficie, un numero casuale seleziona il materiale A o B con probabilità proporzionale al fattore di blend. Questo approccio:

- È **non biased** (non introduce errore sistematico)
- Funziona con **qualsiasi combinazione** di materiali (anche Dielectric + Disney, Emissive + Metal, ecc.)
- Converge al valore corretto `(1-t)×colorA + t×colorB` su molti campioni
- È lo stesso algoritmo usato da renderer professionali (Blender Cycles Mix Shader, Mitsuba, PBRT)

#### EvaluateDirect (illuminazione diretta / NEE)

Per la luce diretta, il MixMaterial usa una **media pesata deterministica** delle risposte BRDF di entrambi i materiali. Questo produce varianza inferiore rispetto alla selezione stocastica per la NEE.

#### Emissione

L'emissione è una blend pesata: `(1-t)×emitA + t×emitB`. Permette transizioni fluide tra zone emissive e non (es. lava che si raffredda).

### Note e Limitazioni

- **Normal map:** Il MixMaterial accetta la propria normal map tramite il campo `normal_map:`. Le normal map dei materiali figli NON vengono applicate durante il mixing (il Renderer perturba la normale una sola volta al livello top-level).

- **NEE / Geometry Light:** Oggetti con MixMaterial che includono un figlio emissivo funzionano correttamente (emettono luce), ma non partecipano all'importance sampling NEE come GeometryLight. L'emissione avviene tramite il path tracing standard. Questo introduce più rumore rispetto a un oggetto puramente emissivo, ma il risultato è fisicamente corretto.

- **Ordine di definizione:** I materiali referenziati da `material_a` e `material_b` devono essere definiti **prima** del mix material nel file YAML (o in qualsiasi ordine se sono materiali non-mix). Il loader risolve le dipendenze automaticamente, inclusi mix-of-mix.

- **`randomize_offset` / `randomize_rotation`:** Le texture maschera procedurali supportano la randomizzazione per oggetto, utile quando più oggetti condividono lo stesso mix material ma devono avere pattern diversi.

---

---

[← Torna all'indice](../02-tutorial-scene.md)
