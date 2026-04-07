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

---

[← Torna all'indice](../02-tutorial-scene.md)
