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
| `disney`     | `disney_bsdf`, `pbr` | PBR universale (anisotropia, assorbimento vetro, thin-film, sheen...) |
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

Le luci esplicite `area` e `sphere` sono ora visibili anche loro tramite un proxy emissivo gestito internamente (parità Arnold/Cycles), quindi la scelta tra emissivi liberi e luci esplicite è principalmente di forma: usa una luce esplicita quando l'emettitore è un rettangolo o una sfera canonica (campiona meglio); usa un materiale emissivo libero quando vuoi una forma personalizzata o controllare l'emissione tramite texture. Le luci `point`/`spot`/`directional` rimangono delta — non hanno geometria visibile per costruzione.

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

| Parametro              | Predefinito | Intervallo    | Descrizione                                         |
|------------------------|-------------|---------------|-----------------------------------------------------|
| `color`                | --          | 0--1          | Colore albedo di base                               |
| `metallic`             | `0.0`       | 0--1          | 0 = dielettrico (plastica, legno), 1 = conduttore (metallo) |
| `roughness`            | `0.5`       | 0--1          | 0 = perfettamente liscio, 1 = completamente diffuso |
| `subsurface`           | `0.0`       | 0--1          | Miscela verso il modello diffuso subsurface scattering |
| `specular`             | `0.5`       | 0--1          | Intensità speculare dielettrica (Fresnel F₀)        |
| `specular_tint`        | `0.0`       | 0--1          | Tinge la riflessione speculare con il colore di base |
| `sheen`                | `0.0`       | 0--1          | Highlight morbido ad angoli radenti (tessuto, velluto) |
| `sheen_tint`           | `0.5`       | 0--1          | Tinge lo sheen con il colore di base                |
| `sheen_roughness`      | `0.3`       | 0.04--1       | α del Charlie sheen — larghezza dell'alone radente |
| `clearcoat`            | `0.0`       | 0--1          | Secondo lobo speculare (lacca, vernice)             |
| `clearcoat_gloss`      | `1.0`       | 0--1          | **Legacy** — preferire `coat_roughness` (≈ `1 - clearcoat_gloss`) |
| `coat_ior`             | `1.5`       | 1+            | IOR del coat (default 1.5 = lacca)                 |
| `coat_roughness`       | `-1.0`      | -1 o 0--1     | Sentinella `-1` usa il `clearcoat_gloss` legacy; qualsiasi `≥ 0` attiva il coat stile Arnold e `clearcoat_gloss` viene ignorato |
| `coat_normal_map`      | --          | path immagine | Normal map applicata **solo** al lobo clearcoat     |
| `spec_trans`           | `0.0`       | 0--1          | Trasmissione speculare (0 = opaco, 1 = vetro)       |
| `transmission_color`   | `[1,1,1]`   | 0--1          | Colore raggiunto dentro il vetro a `transmission_depth` |
| `transmission_depth`   | `0.0`       | 0+            | Distanza (unità scena) a cui si raggiunge quel colore |
| `ior`                  | `1.5`       | 1+            | Indice di rifrazione (specular + trasmissione)      |
| `anisotropic`          | `0.0`       | 0--1          | 0 = isotropo, 1 = allungato lungo la tangente      |
| `anisotropic_rotation` | `0.0`       | 0--1          | Frazione di rotazione di 2π intorno alla normale    |
| `diff_trans`           | `0.0`       | 0--1          | Trasmissione diffusa (foglie, tele sottili)         |
| `flatness`             | `0.0`       | 0--1          | Blend Lambert → HK-flat (Disney 2015)               |
| `thin_walled`          | `false`     | bool          | Disattiva la doppia rifrazione — foglie, carta      |
| `subsurface_color`     | --          | colore 0--1   | Tinta per i lobi subsurface / flatness / diff_trans |
| `subsurface_radius`    | --          | `[R,G,B]` ≥ 0 | **Non usato** — letto dal parser ma riservato a una futura SSS random-walk; oggi non ha alcun effetto |
| `thin_film_thickness`  | `0.0`       | 0+ (nm)       | Spessore del film iridescente (bolle, opal, AR)    |
| `thin_film_ior`        | `1.5`       | 1+            | IOR del film iridescente (η₂)                      |
| `texture`              | --          | --            | Texture procedurale o immagine (sostituisce color)  |
| `normal_map`           | --          | --            | Dettagli di superficie tramite perturbazione delle normali |

> **Texturing di ogni parametro.** Ogni parametro scalare accetta la
> variante `*_texture` (per esempio `roughness_texture`) e i tre input
> colore (`color`, `transmission_color`, `subsurface_color`) accettano
> un blocco `*_texture` dedicato. Esempio:
> `roughness_texture: { type: "image", path: "rough.png" }`.

### Come i Parametri Lavorano Insieme

Il materiale Disney è un sistema a strati:

1. **Strato di base** (`metallic` = 0): una superficie dielettrica con riflessione diffusa, controllata da `roughness`. Come plastica, legno, pelle.
2. **Modalità metallo** (`metallic` = 1): riflessione solo speculare tinta dal `color`. Come oro, acciaio, rame.
3. **Strato Clearcoat** (`clearcoat` > 0): un rivestimento lucido indipendente sopra qualsiasi cosa ci sia sotto. Come la vernice delle auto o il legno laccato.
4. **Trasmissione** (`spec_trans` > 0): la luce attraversa il materiale. Combinato con `roughness` > 0 si ottiene il vetro smerigliato.
5. **Subsurface** (`subsurface` > 0): la luce penetra la superficie e si diffonde all'interno. Dona un aspetto più morbido e piatto agli oggetti sottili. Usato per pelle, cera, porcellana, foglie.
6. **Sheen** (`sheen` > 0): un tenue bagliore agli angoli radenti. Usato per tessuti, velluto e alcuni materiali organici. `sheen_roughness` controlla la larghezza del bagliore (valori bassi = alone stretto, valori alti = halo morbido).
7. **Anisotropia** (`anisotropic` > 0): allunga l'highlight speculare lungo la direzione tangente. Acciaio spazzolato, capelli, vinile. Ruota il frame tangente con `anisotropic_rotation`.
8. **Iridescenza thin-film** (`thin_film_thickness` > 0): moltiplica il Fresnel per un fattore film sottile dipendente dalla lunghezza d'onda. Bolle di sapone, opal, anti-riflesso dielettrico.
9. **Vetro colorato** (`spec_trans > 0` con `transmission_color` e `transmission_depth > 0`): il percorso di trasmissione usa l'assorbimento Beer-Lambert, quindi la luce che attraversa il vetro viene tinta esponenzialmente con la distanza (le sezioni più spesse diventano più scure).
10. **Superfici thin-walled** (`thin_walled: true` con `diff_trans` o `spec_trans`): il motore disattiva la doppia rifrazione — ideale per foglie, carta, tele sottili, vetrate.

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

**Acciaio spazzolato (anisotropo):**
```yaml
- id: "brushed_steel"
  type: "disney"
  color: [0.7, 0.7, 0.72]
  metallic: 1.0
  roughness: 0.35
  anisotropic: 0.75
  anisotropic_rotation: 0.0     # spazzolatura lungo la tangente U
```

**Vetro colorato (bottiglia di brandy):**
```yaml
- id: "brandy_glass"
  type: "disney"
  color: [1.0, 1.0, 1.0]
  metallic: 0.0
  roughness: 0.0
  spec_trans: 1.0
  ior: 1.52
  transmission_color: [0.95, 0.55, 0.15]
  transmission_depth: 6.0       # il colore si raggiunge dopo 6 unità scena
```

**Bolla di sapone (thin-film iridescence):**
```yaml
- id: "soap_bubble"
  type: "disney"
  color: [1.0, 1.0, 1.0]
  metallic: 0.0
  roughness: 0.01
  spec_trans: 1.0
  thin_walled: true             # niente doppia rifrazione
  thin_film_thickness: 520      # ~520 nm di film
  thin_film_ior: 1.33
```

**Vernice auto con coat stile Arnold e coat normal map:**
```yaml
- id: "metallic_pearl"
  type: "disney"
  color: [0.05, 0.1, 0.35]
  metallic: 0.9
  roughness: 0.25
  clearcoat: 1.0
  coat_ior: 1.55
  coat_roughness: 0.15          # ≥ 0 abilita il coat stile Arnold
  coat_normal_map: "textures/orange_peel_normal.png"
```

**Velluto con Charlie sheen:**
```yaml
- id: "moss_velvet"
  type: "disney"
  color: [0.1, 0.25, 0.08]
  metallic: 0.0
  roughness: 0.95
  sheen: 1.0
  sheen_tint: 1.0
  sheen_roughness: 0.25         # alone radente stretto e nitido
```

**Foglia verde (diffuse transmission + thin-walled):**
```yaml
- id: "tree_leaf"
  type: "disney"
  color: [0.25, 0.5, 0.18]
  metallic: 0.0
  roughness: 0.8
  diff_trans: 0.55              # metà dell'energia diffusa attraversa
  thin_walled: true
  subsurface_color: [0.35, 0.65, 0.25]
```

**Pelle di porcellana (flatness HK + tinta subsurface):**
```yaml
- id: "porcelain_skin"
  type: "disney"
  color: [0.95, 0.82, 0.76]
  metallic: 0.0
  roughness: 0.4
  subsurface: 0.5
  subsurface_color: [1.0, 0.55, 0.45]
  flatness: 0.4
```

### Cheat-Sheet Rapido

Riferimento compatto che copre l'intera tassonomia delle superfici Disney.
Usalo come punto di partenza e poi affina `roughness` e `specular` per il
look finale. Sono elencate solo le chiavi non-default — omettile per
mantenere il valore predefinito.

| Famiglia materiale | Ricetta essenziale |
|---|---|
| Diffuso opaco (intonaco, legno grezzo) | `roughness: 0.9`, `specular: 0.2`, opzionale `sheen: 0.1–0.2` |
| Opaco "piatto" (carta, cemento) | `roughness: 0.85`, `flatness: 0.5–0.8`, `specular: 0.2` |
| Plastica lucida | `metallic: 0`, `roughness: 0.2–0.4`, `specular: 0.5`, opzionale `clearcoat: 0.3` |
| Gomma / silicone | `metallic: 0`, `roughness: 0.7–0.9`, `specular: 0.25`, `sheen: 0.2`, `sheen_roughness: 0.5` |
| Velluto / tessuto | `roughness: 0.9`, `sheen: 1.0`, `sheen_tint: 0.7`, `sheen_roughness: 0.2–0.4` |
| Pelle / porcellana | `metallic: 0`, `roughness: 0.4`, `subsurface: 0.5`, `subsurface_color: [0.9, 0.5, 0.45]`, `flatness: 0.3`, `sheen: 0.05` |
| Foglia / carta (traslucida) | `roughness: 0.4`, `thin_walled: true`, `diff_trans: 0.5`, `subsurface_color: <tinta interna>`, opzionale `flatness: 0.3` |
| Metallo lucido (oro, argento, cromo) | `metallic: 1`, `roughness: 0.02–0.15`, `specular: 0.9–1.0` |
| Metallo ruvido / satinato | `metallic: 1`, `roughness: 0.4–0.7`, `specular: 0.6` |
| Metallo spazzolato | `metallic: 1`, `roughness: 0.25`, `anisotropic: 0.7–0.9`, `anisotropic_rotation: 0.0–1.0` |
| Vernice auto (slider legacy) | `metallic: 0`, `roughness: 0.3`, `clearcoat: 1`, `clearcoat_gloss: 0.9` |
| Vernice auto (coat Arnold) | `metallic: 0–0.9`, `roughness: 0.25`, `clearcoat: 1`, `coat_ior: 1.55`, `coat_roughness: 0.05–0.15` |
| Legno laccato / pianoforte nero | `roughness: 0.1`, `clearcoat: 1`, `coat_roughness: 0.05`, `specular: 0.7` |
| Ceramica / porcellana dura | `metallic: 0`, `roughness: 0.15`, `specular: 0.7`, `clearcoat: 0.5`, `coat_roughness: 0.2` |
| Vetro trasparente | `spec_trans: 1`, `roughness: 0.0`, `ior: 1.5`, `specular: 1.0` |
| Vetro colorato / gemma | `spec_trans: 1`, `roughness: 0.0–0.02`, `ior: 1.5–1.77`, `transmission_color: <tinta>`, `transmission_depth: 0.3–1.0` |
| Diamante | `spec_trans: 1`, `roughness: 0.003`, `ior: 2.42`, `specular: 1.0` |
| Vetro smerigliato | `spec_trans: 1`, `roughness: 0.2–0.3`, `ior: 1.5`, `specular: 0.7` |
| Bolla di sapone / film iridescente | `spec_trans: 1`, `roughness: 0.02`, `ior: 1.33`, `thin_walled: true`, `thin_film_thickness: 300–700 (nm)`, `thin_film_ior: 1.33` |
| Metallo anodizzato / verniciato | `metallic: 0.9`, `roughness: 0.25`, `clearcoat: 0.4`, `coat_roughness: 0.15` |

> **Tip.** Per convertire una scena Disney-2012 al parametro moderno, la
> regola spannometrica è `coat_roughness ≈ 1 - clearcoat_gloss` (poi
> rimuovi la chiave legacy). Per materiali con sheen forte, imposta
> `sheen_roughness` a 0.4 o più: l'halo Charlie risulta più morbido del
> picco stretto del default 0.3.

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

### Noise

```yaml
texture:
  type: "noise"
  noise_type: "fbm"          # perlin | fbm | turbulence | ridged | billow | hetero_terrain | hybrid_multifractal
  scale: 4.0
  octaves: 5
  lacunarity: 2.0
  gain: 0.5
  fractal_increment: 1.0     # Musgrave H — solo hetero_terrain / hybrid_multifractal
  fractal_offset: 0.7        # Musgrave offset / "sea level" — solo hetero_terrain / hybrid_multifractal
  distortion: 0.3
  colors: [[0, 0, 0], [1, 1, 1]]
```

3D-Ray include uno stack di rumore frattale completo e di livello
professionale — la stessa famiglia di modalità presente in Arnold
(`noise`), Cycles (Noise/Musgrave Texture) e RenderMan (`PxrFractal`):

| `noise_type`           | Aspetto                                                | Utile per                              |
|------------------------|--------------------------------------------------------|----------------------------------------|
| `perlin`               | Gradient noise liscio (singola ottava)                 | Variazione morbida a bassa freq.       |
| `fbm`                  | Somma di ottave (fractal noise canonico)               | Pietra, sporco, terreno, carta         |
| `turbulence`           | Σ\|noise\| (variante absolute-value nitida)            | Nuvole, fumo, sporco fino              |
| `ridged`               | Ridged multifractal di Musgrave                        | Roccia, fulmini, venature marmo        |
| `billow`               | Σ\|noise\| sulle ottave, normalizzato                  | Nuvole gonfie, schiuma, ruggine        |
| `hetero_terrain`       | Musgrave §16.3.3 — picchi rugosi, valli lisce          | Terreno eroso, montagne, costa         |
| `hybrid_multifractal`  | Musgrave §16.3.4 — strati stratificati + picchi netti  | Asteroidi, rocce aliene, marmi strati  |

| Parametro            | Predefinito | Descrizione                                                              |
|----------------------|-------------|--------------------------------------------------------------------------|
| `noise_type`         | auto        | Famiglia di rumore (vedi tabella)                                        |
| `scale`              | `1.0`       | Frequenza del pattern di rumore                                          |
| `octaves`            | `5`         | Numero di ottave fBm/ridged/billow/musgrave (1..16)                      |
| `lacunarity`         | `2.0`       | Moltiplicatore di frequenza fra ottave successive                        |
| `gain`               | `0.5`       | Decadimento di ampiezza fra ottave (fbm/ridged/billow)                   |
| `fractal_increment`  | `1.0`       | H di Musgrave — solo hetero_terrain / hybrid_multifractal                |
| `fractal_offset`     | `0.7`       | offset / "sea level" di Musgrave — solo hetero_terrain / hybrid_multifractal |
| `distortion`         | `0`         | Domain warp (rende il pattern organico/non assiale)                      |
| `noise_strength`     | --          | Legacy: 0 = Perlin liscio, >0 = turbolento                               |

Se `noise_type` viene omesso, la texture ricade sul comportamento legacy
guidato da `noise_strength` — quindi le scene esistenti renderizzano
identiche.

**Multifrattali di Musgrave.** `hetero_terrain` e `hybrid_multifractal`
sono i due frattali "veri terreno" di Ebert/Musgrave/Peachey/Perlin,
*Texturing &amp; Modeling, 3rd ed.* §16.3. Diversamente da fBm — che ha
statistica identica a ogni quota — moltiplicano il contributo di ogni
ottava per il valore accumulato corrente (heterogeneous) o per un peso
corrente (hybrid), così le quote alte raccolgono più rugosità e le valli
restano lisce. `H` (fractal increment, default 1.0) controlla la velocità
di decadimento delle alta-frequenza; H ≈ 0.25 produce montagne rugose,
H ≥ 1 colline lisce. `offset` (default 0.7) è il bias additivo per ottava,
il "sea level". Vedi `scenes/showcases/musgrave-multifractal-showcase.yaml`
per il confronto a quattro pannelli fBm / hetero / hybrid / alpine.

### Marble (Marmo)

```yaml
texture:
  type: "marble"
  scale: 4.0
  noise_strength: 10.0
  vein_axis: [1, 0, 0.3]
  vein_sharpness: 5.0
  octaves: 7
  distortion: 0.25
  colors: [[0.92, 0.91, 0.88], [0.18, 0.18, 0.22]]
```

Il vero marmo di Carrara ha venature sottili, ad alto contrasto e non
allineate agli assi. I nuovi controlli permettono di riprodurre quel
look:

| Parametro        | Predefinito  | Descrizione                                        |
|------------------|--------------|----------------------------------------------------|
| `vein_axis`      | `[0,0,1]`    | Direzione primaria di propagazione delle venature  |
| `vein_frequency` | `1.0`        | Moltiplicatore sulla frequenza della sinusoide     |
| `vein_sharpness` | `1.0`        | 1 = morbido (legacy), 4–8 = venature Carrara       |
| `noise_type`     | `turbulence` | Modulatore `turbulence` / `fbm` / `ridged`         |
| `octaves`        | `7`          | Numero di ottave del modulatore                    |
| `distortion`     | `0`          | Domain warp sulla posizione di input               |
| `secondary_wave` | --           | Onda di venatura incrociata (Statuario / Calacatta)|

**Venature incrociate studio-quality (`secondary_wave`).** Statuario,
Calacatta e Arabescato hanno venature che corrono lungo due direzioni
non parallele. Con `secondary_wave.strength > 0` si aggiunge una
seconda sinusoide lungo `secondary_wave.axis` al termine primario —
la somma `sin(wave1) + strength · sin(wave2)` è rinormalizzata in modo
che l'output resti ben definito. L'asse secondario viene auto-
ortogonalizzato contro il primario al sample-time, quindi anche
sceglierlo collineare produce venature incrociate visibili.
`strength = 0` (default) è bit-identico all'output legacy. Da
combinare con un `color_ramp:` a 3+ stop per vena → mid-tone → base →
undertone.

```yaml
texture:
  type: "marble"
  vein_axis: [0, 0, 1]
  secondary_wave:
    axis: [1, 0, 0]
    frequency: 0.7
    strength: 0.5
  color_ramp:
    - { position: 0.0, color: [0.20, 0.16, 0.18], interp: "smoothstep" }  # vena scura
    - { position: 0.3, color: [0.78, 0.62, 0.30], interp: "smoothstep" }  # caldo aureo
    - { position: 0.7, color: [0.96, 0.94, 0.90], interp: "smoothstep" }  # base avorio
    - { position: 1.0, color: [0.90, 0.92, 0.96], interp: "linear"     }  # undertone freddo
```

Vedi `scenes/showcases/marble-wood-studio-showcase.yaml` per il
confronto Carrara / Calacatta / Arabescato.

### Wood (Legno)

```yaml
texture:
  type: "wood"
  scale: 5.0
  noise_strength: 1.2
  ring_axis: [0, 1, 0]
  ring_sharpness: 3.5
  axial_grain: 0.4
  octaves: 4
  distortion: 0.18
  colors: [[0.78, 0.55, 0.30], [0.42, 0.24, 0.12]]
```

Gli anelli si formano perpendicolari a `ring_axis`: usa `[0, 1, 0]` per
un tronco in sezione, `[0, 0, 1]` per un'asse, o un vettore leggermente
inclinato per un look più organico. `ring_sharpness` eleva a potenza
un'onda triangolare attorno al bordo di ogni anello, producendo le
linee scure del legno tardivo tipiche di rovere e noce.
`axial_grain` aggiunge una variazione a lunga lunghezza d'onda lungo
l'asse del tronco (ottimo per le assi).

| Parametro            | Predefinito | Descrizione                                              |
|----------------------|-------------|----------------------------------------------------------|
| `ring_axis`          | `[0,1,0]`   | Asse tronco/log (gli anelli sono sul piano ⊥)            |
| `ring_sharpness`     | `1.0`       | 1 = morbido (legacy), 3–6 = legno tardivo definito       |
| `axial_grain`        | `0.0`       | Variazione a lunga lunghezza d'onda lungo l'asse         |
| `octaves`            | `1`         | Ottave fBm sulla venatura (1 = Perlin legacy)            |
| `distortion`         | `0`         | Domain warp — 0 = anelli puliti, ~0.5 = nodi/onde        |
| `grain_scale`        | `1.0`       | Moltiplicatore sul sample-point del grain (alta freq)    |
| `figure_scale`       | `0.25`      | Moltiplicatore sul sample-point della figure (bassa freq)|
| `figure_strength`    | `0.0`       | 0 = disattivata, ~0.5–1.5 = curly maple / flame mahogany |
| `radial_anisotropy`  | `0.0`       | 0 = piano-sawn (isotropo), >0 = quartato                 |
| `knot_density`       | `0.0`       | 0 = nessun nodo, ~0.5 = sparsi, ~1 = pieno               |

**Legno studio-quality.** Quattro nuovi knob opt-in portano la texture
wood al livello Arnold / RenderMan / Cycles:

- **Bande grain + figure** — `grain_scale` + `noise_strength` (alias
  `grain_strength`) pilotano il dettaglio fibra ad alta frequenza dentro
  gli anelli; `figure_scale` + `figure_strength` aggiungono l'ondulazione
  indipendente a bassa frequenza che dà a **curly maple** le sue strisce,
  a **flame mahogany** le sue ripple e a **bird's-eye** i suoi fiori. La
  banda figure viene campionata con offset di noise decorrelato, così le
  due bande non si bloccano in lock-step.
- **`radial_anisotropy`** — comprime la componente radiale del sample
  point del noise, così il noise varia poco lungo la direzione radiale.
  È la differenza visiva fra **piano-sawn** (default 0, feature isotrope)
  e tavole **quartato** (anisotropia alta, fibre stirate radialmente).
  L'implementazione è sicura sull'asse del tronco (`radial.Length() == 0`)
  — il path silenziosamente fallback.
- **`knot_density`** — Voronoi a piccola scala genera nodi sparsi che
  tirano localmente il centro dell'anello verso il feature point del nodo
  e aggiungono un cuore scuro. Stesso trucco di Arnold `knots` e
  RenderMan `PxrWoodKnot`. Combinabile con `color_ramp:` a 3 stop per
  autorialità sapwood / heartwood / nodo.

```yaml
texture:
  type: "wood"
  scale: 3.0
  noise_strength: 1.5
  ring_axis: [0, 1, 0]
  ring_sharpness: 3.0
  figure_scale: 0.22
  figure_strength: 0.6
  knot_density: 0.7
  color_ramp:
    - { position: 0.00, color: [0.18, 0.10, 0.06], interp: "smoothstep" }  # cuore nodo
    - { position: 0.20, color: [0.55, 0.32, 0.16], interp: "smoothstep" }  # latewood
    - { position: 0.65, color: [0.90, 0.72, 0.45], interp: "smoothstep" }  # earlywood
    - { position: 1.00, color: [0.96, 0.86, 0.65], interp: "linear"     }  # sapwood
```

Vedi `scenes/showcases/marble-wood-studio-showcase.yaml` per il
confronto a sei sfere: marmi Carrara / Calacatta / Arabescato + legni
rovere quartato / curly maple / knotty pine.

---

### 3.8.1 Lookdev Studio Marmo & Legno — Tutorial Pratico

Questo sotto-capitolo è il più lungo del tutorial sui materiali perché
ottenere pietra e legno foto-realistici è una delle cose più difficili
in qualunque renderer procedurale. Gli shader di marmo default di
Arnold e RenderMan hanno **decine** di knob proprio perché il look
dipende da scelte interdipendenti: illuminazione, parametri BSDF,
geometria delle vene, risposta di sharpening, autorialità delle ramp,
randomizzazione. Vediamole tutte, con ricette copia-incolla dalla scena
di riferimento.

#### Step 1 — Sistema l'illuminazione prima della texture

Trappola comune: scrivi un materiale Carrara "perfetto", renderizzi, e
le sfere vengono fuori grigio-bluastre. La texture è corretta — è
l'illuminazione che non va. **Un marmo lucido a `roughness < 0.2` è
sostanzialmente uno specchio**, e su un cielo gradient senza dettaglio
prende il colore del cielo invece di lasciar leggere la texture diffusa.

Il backdrop studio usato da `marble-wood-studio-showcase.yaml`:

```yaml
world:
  sky:
    type: "flat"
    color: [0.001, 0.001, 0.0012]   # quasi nero: niente riflessione ambientale

lights:
  # Key direzionale forte — domina sulla riflessione speculare, la
  # texture diffusa diventa leggibile.
  - type: "directional"
    direction: [-0.4, -0.8, 0.45]
    color: [1.0, 0.98, 0.94]
    intensity: 6.5
    angular_radius: 0.6          # bordi ombra morbidi
  - type: "point"
    position: [-7, 6, -4]
    color: [0.90, 0.93, 1.00]    # fill freddo
    intensity: 55
  - type: "point"
    position: [0, 3.5, 5]
    color: [1.0, 0.82, 0.62]     # rim caldo dal retro per silhouette
    intensity: 45
```

Per un interno con ambiente texturato (HDRI, finestre cucina, ecc.) puoi
tenere un cielo più chiaro — il contenuto dell'HDRI si rifletterà in modo
interessante sul marmo. Per una sfera lookdev pulita, near-black è il
default sicuro.

#### Step 2 — Scegli la personalità del marmo

| Marmo        | Sharpness   | Secondary wave | Distortion | Ramp                                       |
|--------------|-------------|----------------|------------|---------------------------------------------|
| Carrara      | 4.0         | nessuna        | nessuna    | 2-colori (base bianca, vena quasi nera)     |
| Calacatta    | 3.0         | strength 0.45  | nessuna    | 4-stop (vena → oro → cream → avorio)        |
| Statuario    | 3.5         | strength 0.35  | 0.15       | 3-stop (vena → grigio → bianco)             |
| Arabescato   | 2.0         | strength 0.7   | 0.35       | 3-stop (vena nera → grigio → ivory)         |
| Port Laurent | 3.0         | strength 0.4   | nessuna    | 3-stop (vena oro → marrone → nero)          |
| Rosso Levanto| 4.0         | strength 0.4   | nessuna    | 3-stop (calcite bianca → rosso → scuro)     |

La tabella, dall'alto verso il basso, scorre da "geometrico" a
"geologico": Carrara è ordinato, Arabescato è caotico.

#### Step 3 — Convenzione di sharpness (non sbagliarla)

`vein_sharpness` controlla quanto è larga la regione di vena rispetto
alla base. La relazione è `t' = 1 − (1−t)^k` dove
`t = (sin(...) + 1)/2` è la sinusoide di base, quindi:

- **`vein_sharpness = 1`** — nessuno sharpening, blend 50/50 morbido.
  Look "legacy" pre-step-5. Vene larghe e sfocate.
- **`vein_sharpness = 3`** — campione medio al ~75% verso la base.
  Vene audaci ~25% di superficie. **Tipico Calacatta.**
- **`vein_sharpness = 5`** — campione medio ~83%. Vene a filigrana
  sottile. Look Carrara vero.
- **`vein_sharpness = 8`** — campione medio ~89%. Vene capillari, la
  ramp deve avere stop ad alta frequenza altrimenti la vena sparisce
  visivamente.

Siccome l'area dominante è la *base*, quando crei un `color_ramp`:

```yaml
color_ramp:
  - { position: 0.00, color: <COLORE VENA> }   # raro, t→0
  - ...                                        # transizioni, stop intermedi
  - { position: 1.00, color: <COLORE BASE> }   # dominante, t→1
```

Se inverti (base in posizione 0, vena in posizione 1) il materiale
viene per lo più colorato di vena — è così che, partendo da un YAML
"Carrara", ottieni accidentalmente un "marmo nero con vene bianche".

#### Step 4 — Secondary wave per cross-veining

Il Calacatta reale ha *due* direzioni di venatura: grandi diagonali
attraversate da venature trasversali più piccole. Lo modelliamo
aggiungendo una seconda sinusoide lungo un asse auto-ortogonalizzato
contro il primario al sample-time (quindi anche un `axis: [0, 0, 1]`
collineare produce comunque cross-veining visibile):

```yaml
secondary_wave:
  axis: [1, 0, 0]                  # hint direzione secondaria
  frequency: 0.65                  # rapporto non intero → niente moiré
  strength: 0.45                   # ≤1 tipico; 0 = single-axis (back-compat)
```

Il segnale combinato `sin(w1) + strength·sin(w2)` è rinormalizzato per
`(1 + strength)` così l'output resta in [-1, 1] e la curva di sharpening
continua a funzionare.

**Tip frequenza:** se scegli `secondary_wave.frequency` come rapporto
non banale di `vein_frequency` (es. 0.65, 0.85, 1.2) il cross-pattern
è aperiodico e moiré-free. Frequenze uguali producono un grid pattern
regolare che sembra artificiale.

#### Step 5 — Roughness, clearcoat, e il trucco "marmo lucido"

Per un marmo lucido tipo top cucina servono **due strati speculari**:

```yaml
roughness: 0.32       # strato base — la texture diffusa resta leggibile
specular: 0.5
clearcoat: 0.9        # vernice lucida sopra
coat_roughness: 0.05  # clearcoat near-mirror
```

Il `clearcoat` è un secondo strato speculare sopra il base. Il base
è abbastanza ruvido che il pattern del marmo sopravvive, il coat
aggiunge la sheen tipo vetro in alto. Per una finitura "satinata"
togli il clearcoat e alza `roughness` a 0.4–0.5.

#### Step 6 — Legno: scegli il taglio e la figure

Il legno ha tre tagli ortogonali: piano-sawn, quartato, e rift. Più
figure opzionale (curly, flame, bird's-eye, burl) e nodi opzionali.

| Look legno         | `noise_strength` | `figure_strength` | `radial_anisotropy` | `knot_density` |
|--------------------|------------------|-------------------|---------------------|----------------|
| Rovere piano-sawn  | 2.2              | 0.0               | 0.0                 | 0.0            |
| Rovere quartato    | 2.2              | 0.0               | 2.5–3.5             | 0.0            |
| Acero curly        | 0.25             | 1.5–1.8           | 0.0                 | 0.0            |
| Bird's-eye         | 0.15             | 1.0–1.4 + scale 0.45 | 0.0              | 0.0            |
| Mogano flame       | 0.4              | 1.3–1.5           | 0.0                 | 0.0            |
| Pino nodoso        | 0.6              | 0.3 (sottile)     | 0.0                 | 0.7–1.0        |
| Noce burl          | 0.5              | 1.4               | 0.0                 | 0.6            |

Il pattern: **la figure domina sul grain** — per ottenere un look figure
pulito devi abbassare il grain (`noise_strength` ≤ 0.6) così le fibre
ad alta frequenza non sopraffanno le ondulazioni lente. Viceversa per
il rovere piano: figure off, grain alto a 2.0+ per linee fibrose
chiare.

#### Step 7 — Ring sharpness vs. scale

`ring_sharpness` controlla la larghezza della banda di legno tardivo (la
linea scura a fine di ogni anno di crescita). Combinato con `scale`:

- `scale = 3`, `ring_sharpness = 1` — bande morbide e larghe. Rovere di
  un albero giovane a crescita veloce (default legacy).
- `scale = 4.5`, `ring_sharpness = 4` — latewood netto ~10% della
  larghezza dell'anello. Classico rovere / noce.
- `scale = 6`, `ring_sharpness = 5` — anelli stretti, latewood capillare.
  Abete d'altura, pino a crescita lenta.
- `scale = 6`, `ring_sharpness = 8` — anelli molto stretti, quasi tipo
  grattugia. Aliasa su sfere piccole, usare solo in close-up.

#### Step 8 — Autorialità anelli dei nodi

Quando `knot_density > 0` la texture genera nodi Voronoi a piccola scala
nel piano perpendicolare al `ring_axis`. Dentro un nodo il centro
dell'anello viene tirato verso il feature point del nodo, producendo
anelli concentrici attorno al nodo — esattamente come una sezione di
branca incastonata nel legno del tronco. Due regole:

1. **`scale` alto** (≥ 5): così il nodo può ospitare anelli interni
   visibili. Un `scale` basso fa apparire i nodi come puntini scuri,
   non nodi veri.
2. **Ramp a 4 stop** che riserva la posizione 0 al cuore del nodo:
   ```yaml
   color_ramp:
     - { position: 0.00, color: [0.05, 0.03, 0.02] }   # CUORE NODO (molto scuro)
     - { position: 0.18, color: [0.35, 0.18, 0.08] }   # latewood
     - { position: 0.65, color: [0.90, 0.68, 0.40] }   # earlywood
     - { position: 1.00, color: [0.97, 0.86, 0.60] }   # sapwood
   ```
   Il passo `t *= (1 − knotDarken)` dentro la texture spinge `t → 0`
   ai centri dei nodi indipendentemente dalla banda d'anello in cui
   ricadeva il campione, quindi la posizione 0 viene sempre mostrata.
   Senza quello stop dedicato al nodo, il nodo si limiterebbe a scurire
   il colore d'anello locale — visibile ma meno riconoscibile come nodo.

#### Step 9 — Randomizzazione per instancing

Se metti più oggetti di legno / marmo nella scena e usano tutti lo
stesso materiale, mostreranno tutti lo **stesso** pattern. Il knob:

```yaml
texture:
  type: "wood"
  randomize_offset: true     # origine texture diversa per oggetto
  randomize_rotation: true   # orientamento texture diverso per oggetto
```

Ogni entità riceve un `objectSeed` diverso (da `seed:` sull'entità, o
auto-incrementato), e l'`Apply()` di `TextureTransform` genera un
offset+rotazione per-seed. **Abilitalo sempre per materiali condivisi.**

#### Step 10 — Il catalogo pre-cotto

La libreria contiene 14 materiali studio-quality pronti all'import:

```yaml
imports:
  - { path: "scenes/libraries/materials/stones.yaml" }
  - { path: "scenes/libraries/materials/woods.yaml" }

entities:
  - { type: "sphere", center: [0, 1, 0], radius: 1, material: "dis_calacatta_studio_lucido" }
```

Catalogo (suffisso `_studio` dappertutto):
- **Marmi:** `dis_carrara_studio`, `dis_carrara_studio_lucido`,
  `dis_calacatta_studio`, `dis_calacatta_studio_lucido`,
  `dis_statuario_studio`, `dis_statuario_studio_lucido`,
  `dis_arabescato_studio`, `dis_arabescato_studio_lucido`,
  `dis_port_laurent_studio_lucido`, `dis_rosso_levanto_studio_lucido`
  + variants Classic Lambertian.
- **Legni:** `dis_acero_curly_studio`, `dis_acero_birdseye_studio`,
  `dis_acero_sapwood_studio`, `dis_mogano_flame_studio`,
  `dis_quercia_quartato_studio`, `dis_frassino_quartato_studio`,
  `dis_pino_nodoso_studio`, `dis_abete_nodoso_studio`,
  `dis_noce_burl_studio` + variants Classic.

Ciascuno è tunato con le ricette qui sopra. Parti da un entry `_studio`,
sostituisci gli stop della color ramp con quelli della tua foto di
riferimento, e hai un materiale production-ready.

### Voronoi / Worley (cellulare)

```yaml
texture:
  type: "voronoi"
  scale: 5.0
  metric: "euclidean"        # euclidean | manhattan | chebyshev | euclidean_squared
  output: "f2_minus_f1"      # f1 | f2 | f3 | f4 |
                             # f2_minus_f1 | f3_minus_f1 |
                             # f1_plus_f2 | cell | position
  randomness: 0.9
  smoothness: 0.0            # 0 = hard min (classico); ∈ (0,1] abilita Smooth Voronoi
  colors: [[0.05, 0.05, 0.05], [0.95, 0.90, 0.70]]
```

Il rumore cellulare di Worley è il cavallo di battaglia per ciottoli,
sassi, schiuma, terra screpolata, pelle di rettile e pattern a tessere
astratte. La modalità di output seleziona il look:

- `f1` — distanza dal punto-feature più vicino → ciottoli / blob.
- `f2` — distanza dal secondo più vicino.
- `f3`, `f4` — distanza al 3° e 4° feature (cellulare gerarchico,
  voronoi-on-voronoi, cuoio multi-scala).
- `f2_minus_f1` — ridge nette fra le celle (il famoso "crackle").
- `f3_minus_f1` — banda border più larga e a frequenza più bassa
  (rim morbidi, gradienti tipo mortar).
- `cell` — ogni cella riceve un colore casuale stabile (mosaico).
- `position` — XYZ cell-local del feature point F1 come RGB; ID
  stocastico deterministico per cella, da iniettare in un'altra
  procedurale (output Position di Cycles, position di PxrVoronoise).

`metric: "chebyshev"` produce piastrelle quadrate/esagonali.
`randomness: 0` collassa i feature su una griglia regolare; `1` è
sparpagliamento totale.

> **Ordine dei colori per `f2_minus_f1`.** `F2 - F1` vale **zero sul
> bordo cella** e raggiunge il **massimo al centro cella**. Il lerp
> applica una risposta sqrt (riproducendo la "Distance to Edge" di
> Cycles), quindi `colors[0]` è ciò che vedi SUI bordi e `colors[1]`
> è ciò che vedi DENTRO le celle. Per il classico look crackle — linee
> chiare sottili su sfondo scuro — scrivi `colors: [[chiaro], [scuro]]`.
> L'esempio qui sopra fa esattamente questo.

> **Smooth Voronoi (`smoothness`).** Con `smoothness > 0` il `min()` hard
> sulle 27 celle vicine viene sostituito dal soft-min log-sum-exp di
> Inigo Quilez `-log(Σ exp(-k·d_i)) / k` con `k = 20/smoothness`. F1
> diventa C∞ ovunque; F2 viene costruito dalla stessa accumulazione
> escludendo il peso dominante, così `f2_minus_f1` perde il ridge a V —
> bordi morbidi, niente alias a step lungo le creste. Utile per cuoio
> levigato, ciottoli arrotondati dall'acqua, pelle di rettile, marmo
> poro-chiuso. `smoothness = 0` (default) è bit-identica al comportamento
> legacy; l'output `cell` è volutamente immune (cell-ID è discreto).
> Vedi `scenes/showcases/smooth-voronoi-showcase.yaml` per il confronto
> a tre sfere hard / 0.3 / 0.7 e la parità con Cycles "Smooth F1".

> **Output estesi (`f3`, `f4`, `f3_minus_f1`, `position`).** Questi quattro
> canali espongono la distanza al 3°/4° feature, una banda crackle più
> larga e l'XYZ cell-local del feature point F1. Stesso costo O(27) di
> F1/F2 — le 27 celle vicine sono già scansionate. Usano sempre il hard
> min (smoothness viene intenzionalmente ignorato, stessa convenzione di
> Cycles per i canali di topologia discreta) e `position` bypassa anche
> `color_ramp:` perché è un output identity vettoriale, non scalare. Vedi
> `scenes/showcases/voronoi-extended-outputs-showcase.yaml` per il
> confronto a 6 sfere fianco a fianco.

### Brick (Mattoni)

```yaml
texture:
  type: "brick"
  brick_width: 0.4
  brick_height: 0.18
  mortar_size: 0.025
  row_offset: 0.5
  color_variation: 0.6
  noise_scale: 0.15
  colors:
    - [0.72, 0.32, 0.22]    # mattone A
    - [0.52, 0.18, 0.12]    # mattone B
    - [0.86, 0.83, 0.78]    # malta
```

Muratura a corsi sfalsati (running-bond) sul piano XY con tre colori
(mattone A, mattone B, malta). `row_offset: 0` passa a stack-bond.
Imposta `noise_scale > 0` per aggiungere variazione "stagionata" per
ciascun mattone.

### Gradient (Gradiente)

```yaml
texture:
  type: "gradient"
  mode: "spherical"          # linear | quadratic | easing | spherical | radial
  axis: [0, 1, 0]
  length: 1.0
  colors: [[1.0, 0.85, 0.30], [0.10, 0.05, 0.30]]
```

Utile per direzione artistica (cieli dentro i materiali, cupole
atmosferiche, rampe di roughness messe a punto a mano). `linear`
proietta su `axis`; `spherical` usa la distanza dall'origine; `radial`
usa la distanza dalla retta `axis` (decadimento cilindrico).

### Coordinate (Texture Coordinate node)

```yaml
texture:
  type: "coordinate"
  mode: "object"             # object | uv | generated | world
  scale: 1.0
  bounds_min: [-1, -1, -1]   # solo per mode: "generated"
  bounds_max: [1, 1, 1]
```

Ritorna le coordinate del shading point come RGB. Equivale al nodo
"Texture Coordinate" di Cycles, a `Pref` / `Pworld` / `uvCoord` di
RenderMan e al node `utility` di Arnold. Due usi principali:

1. **Overlay di debug** per verificare a colpo d'occhio gli unwrap UV
   e l'allineamento object/world space. Metti una texture `mode: "uv"`
   su una sfera e vedi subito la linea di cucitura dell'unwrap
   sferico; `mode: "world"` mostra se il BVH ha posizionato
   correttamente la geometria nello spazio mondo.
2. **Driver XYZ deterministico** per pilotare un'altra texture (via
   mix material) con un sistema di coordinate esplicito al posto del
   sample-point object-local implicito che ogni procedurale usa di
   default.

- `object` — `fract(LocalPoint · scale)`. Stesso spazio in cui
  campionano tutte le altre procedurali.
- `uv` — `(u, v, 0)` raw. Gradiente liscio, niente fract.
- `generated` — reference-space normalizzato via bounds. Il workflow
  `Pref` di RenderMan: dichiari l'AABB canonico e ogni nodo a valle
  vede un parametro `[0, 1]³` pulito a prescindere da
  trasformazioni/displacement.
- `world` — `fract(rec.Point · scale)`. Grid world-locked che NON
  segue l'oggetto — utile per laser-grid, polvere world-aligned,
  debug spheres tipo "you-are-here".

Vedi `scenes/showcases/coordinate-texture-showcase.yaml` per il
confronto a 4 sfere fianco a fianco (una per ogni mode).

### Color Ramp Multi-Stop

Ogni texture procedurale eccetto `brick` accetta un blocco opzionale
`color_ramp:` che sovrascrive il lerp implicito a due colori della
texture. Equivalente al nodo ColorRamp di Cycles, `ramp_rgb` di Arnold e
`PxrRamp` di RenderMan — sblocca look irraggiungibili con la shortcut
`colors: [A, B]`: marmo Statuario con vena dorata, legno
sapwood/heartwood, gradienti tramonto fotorealistici, bande toon, heat
map su voronoi.

```yaml
texture:
  type: "marble"
  vein_sharpness: 4.0
  color_ramp:
    - { position: 0.00, color: [0.05, 0.05, 0.07], interp: "smoothstep" }
    - { position: 0.45, color: [0.55, 0.45, 0.32], interp: "linear"     }
    - { position: 0.55, color: [0.95, 0.93, 0.88], interp: "linear"     }
    - { position: 1.00, color: [0.05, 0.05, 0.07], interp: "linear"     }
```

- `position` ∈ [0, 1] — viene clampato fuori range; gli stop sono
  riordinati per `position` crescente.
- `color: [r, g, b]` — RGB linear-space.
- `interp` descrive il segmento *in uscita* da ogni stop:
  - `linear` — lerp standard (default).
  - `smoothstep` — Hermite cubico `3t² − 2t³` (C¹).
  - `ease` — smootherstep di Perlin `6t⁵ − 15t⁴ + 10t³` (C²).
  - `constant` — mantiene il colore fino al prossimo stop.
- Sotto il primo `position` vince il primo colore; sopra l'ultimo vince
  l'ultimo.
- Stop coincidenti (stessa `position`) producono una transizione netta.
- Quando sono presenti sia `colors:` sia `color_ramp:`, vince
  `color_ramp:`.
- Omettendo `color_ramp:` il comportamento legacy a due colori resta
  byte-identico — le scene che non usano la feature non cambiano.

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

## 3.11 Bump Maps

I bump map sono il cugino concettuale delle normal map ma con una
differenza cruciale: l'input è un **campo scalare di altezza** campionato
da una qualunque texture procedurale o image, non un asset RGB già
sbakato. La normale di shading viene perturbata con differenze centrate
in tangent space sulla luminanza (Blinn 1978). Parità con il `bump2d` di
Arnold, il `PxrBump` di RenderMan e il nodo "Bump" di Cycles.

```yaml
- id: "marble_with_bump"
  type: "disney"
  color: [0.78, 0.78, 0.80]
  roughness: 0.4
  bump_map:
    texture:                   # QUALSIASI ITexture: noise, marble, wood,
      type: "marble"           # voronoi, brick, gradient, image, ...
      scale: 5.0
      vein_axis: [0, 1, 0]
      vein_frequency: 3.0
      vein_sharpness: 2.0
      colors: [[0, 0, 0], [1, 1, 1]]
    strength: 3.0              # 0–10, clamp
    scale: 1.0                 # moltiplicatore UV uniforme (default 1)
```

| Parametro  | Tipo           | Predefinito | Descrizione                                                       |
|------------|----------------|-------------|-------------------------------------------------------------------|
| `texture`  | TextureData    | —           | Campo di altezza interno. Procedurale o image.                    |
| `strength` | float ∈ [0,10] | `1.0`       | Ampiezza della perturbazione. Oltre ~5 il bump appare roccioso.   |
| `scale`    | float > 0      | `1.0`       | Moltiplicatore UV uniforme sopra l'eventuale scala interna.       |

**Perché i bump map quando esistono già le normal map?**

- **Input procedurale**. La sorgente del bump può essere `noise`,
  `marble`, `wood`, `voronoi`, `brick`, `gradient` o `checker` — niente
  asset pre-bakato, risoluzione infinita a qualunque zoom.
- **Riuso delle texture**. Qualsiasi texture image già usata come
  albedo può essere riusata come campo di altezza: la luminanza diventa
  l'altezza, la direzione del gradiente diventa l'asse di perturbazione.
- **Si compone con le normal map**. Se entrambe sono presenti, prima
  agisce `normal_map` (rilievo a media frequenza) poi `bump_map` aggiunge
  il dettaglio fine sopra. Convenzione Arnold/Cycles.

Il lobo clearcoat dei materiali `disney` mantiene il proprio
`coat_normal_map` indipendente e **non** vede la perturbazione del bump:
il coat sta su un substrato stabile, così graffi e orange-peel
rimangono coerenti.

Vedi `scenes/showcases/bump-map-showcase.yaml` per un confronto
fianco a fianco di bump derivati da `noise`, `marble` e da una texture
image (concrete) contro un pannello piatto di riferimento.

---

## 3.12 Esempio Completo: Galleria dei Materiali

Una scena che mostra otto diversi materiali uno accanto all'altro.

```yaml
# material-gallery.yaml
# Otto sfere su piedistalli, ognuna con un materiale diverso.

world:
  sky:
    type: "flat"
    color: [0.02, 0.02, 0.03]

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
