# 5. Sezione `textures`

Le texture procedurali vengono definite all'interno del materiale.

## 5.1 Tipi di Texture Procedurali

**Checker (Scacchiera 3D):**
```yaml
    texture:
      type: "checker"
      scale: 4.0
      colors: [[0.9, 0.9, 0.9], [0.1, 0.1, 0.1]]
```

**Noise (Rumore Perlin):**
```yaml
    texture:
      type: "noise"
      scale: 5.0
      noise_strength: 3.0   # 0 = Perlin liscio, > 0 = turbolento (default: 0)
```

**Marble (Marmo):**
```yaml
    texture:
      type: "marble"
      scale: 10.0
      noise_strength: 8.0
      colors: [[0.95, 0.95, 0.95], [0.4, 0.4, 0.4]]
```

**Wood (Legno):**
```yaml
    texture:
      type: "wood"
      scale: 3.0
      noise_strength: 2.0
      colors: [[0.85, 0.65, 0.4], [0.6, 0.4, 0.2]]
```

## 5.2 Trasformazioni Spaziali (Offset & Rotation)

Tutte le texture procedurali supportano offset e rotazione per controllarne l'orientamento nello spazio 3D:

```yaml
    texture:
      type: "marble"
      scale: 10.0
      offset: [5.0, 0.0, 3.0]       # Traslazione della texture
      rotation: [0.0, 45.0, 0.0]     # Rotazione in gradi (X, Y, Z)
```

## 5.3 Randomizzazione per Oggetto

Per far apparire ogni oggetto unico anche con lo stesso materiale:

- **`randomize_offset: true`**: aggiunge un offset pseudo-casuale diverso per ogni oggetto.
- **`randomize_rotation: true`**: ruota la texture in modo pseudo-casuale per ogni oggetto.

**Esempio: Sfere di marmo tutte diverse con un unico materiale**
```yaml
materials:
  - id: "marmo_variegato"
    type: "metal"
    fuzz: 0.04
    texture:
      type: "marble"
      scale: 10.0
      randomize_offset: true
      randomize_rotation: true
```

## 5.4 Image Texture (Texture da File)

Carica un'immagine da file e la proietta sulla superficie usando le coordinate UV della primitiva. Supporta tutti i formati gestiti da ImageSharp: PNG, JPEG, BMP, GIF, TIFF, WebP.

```yaml
    texture:
      type: "image"
      path: "textures/brick_wall.png"    # Relativo al file YAML
      uv_scale: [2, 1]                   # Tiling: 2× in U, 1× in V
```

| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `type` | stringa | — | `"image"` |
| `path` | stringa | — (**obbligatorio**) | Percorso del file immagine. Relativo alla directory del file YAML della scena. |
| `uv_scale` | `[U, V]` | `[1, 1]` | Fattore di tiling su ciascun asse UV. `[3, 3]` = la texture si ripete 3 volte su ogni asse. Se si specifica un solo valore `[2]`, viene usato per entrambi gli assi. |

> **Conversione sRGB → lineare:** Le immagini vengono convertite automaticamente dallo spazio sRGB allo spazio lineare tramite `pow(channel, 2.2)` al caricamento. Questo è necessario per un rendering fisicamente corretto — il tone mapping ACES lavora in spazio lineare.

> **Bilinear filtering:** Le coordinate UV continue vengono interpolate tra i 4 pixel circostanti, producendo bordi smooth anche con texture a risoluzione moderata.

> **Tiling:** I valori UV fuori dall'intervallo [0, 1] vengono wrappati tramite `frac()` per una ripetizione seamless.

> **Fallback magenta:** Se il file non viene trovato o non può essere caricato, il motore stampa un warning in console e usa un colore magenta vivace — facile da individuare nel render per capire dove manca un file.

**Esempio: Pavimento in legno con tiling 4×4**
```yaml
materials:
  - id: "parquet"
    type: "lambertian"
    texture:
      type: "image"
      path: "textures/wood_floor.png"
      uv_scale: [4, 4]
```

**Esempio: Sfera con mappa terrestre**
```yaml
materials:
  - id: "terra"
    type: "lambertian"
    texture:
      type: "image"
      path: "textures/earth.png"

entities:
  - { name: "globo", type: "sphere", center: [0, 1, 0], radius: 1, material: "terra" }
```

**Esempio: Metallo texturato (riflesso + pattern)**
```yaml
materials:
  - id: "acciaio_graffiato"
    type: "metal"
    fuzz: 0.25
    texture:
      type: "image"
      path: "textures/metal_scratched.png"
```

> **💡 Tip: Generare texture di test.** Il progetto include un tool `TextureGen` che genera texture procedurali pronte all'uso (mattoni, legno, terra, griglie UV). Eseguilo con `dotnet run --project src/Tools/TextureGen/TextureGen.csproj`.

---

## 5.5 Normal Map

Il normal mapping perturba la normale di shading pixel per pixel usando un'immagine RGB, simulando dettaglio geometrico (fughe, graffi, rilievi, trame) senza aggiungere triangoli alla scena. L'effetto influenza tutto il calcolo di illuminazione: diffuso N·L, speculare N·H, direzione di scatter e shadow ray origin.

```yaml
materials:
  - id: "muro_mattoni"
    type: "lambertian"
    texture:
      type: "image"
      path: "textures/brick-wall.png"
      uv_scale: [2, 2]
    normal_map:
      path: "textures/brick-wall-normal.png"
      strength: 1.0
      uv_scale: [2, 2]
```

### Parametri

| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `path` | stringa | — (**obbligatorio**) | Percorso del file normal map. Relativo alla directory del file YAML. Se il file non esiste, il motore stampa un warning e continua senza normal map (superficie liscia). |
| `strength` | float | `1.0` | Intensità della perturbazione. `0` = nessuna perturbazione, `1` = normale, `2+` = effetto esagerato. |
| `uv_scale` | `[U, V]` | `[1, 1]` | Deve coincidere con quello della texture albedo per evitare disallineamenti. |
| `flip_y` | bool | `false` | Inverte il canale verde (G). Imposta `true` per mappe DirectX-style (usate da alcuni tool come Substance Painter in modalità DirectX). Le mappe OpenGL-style (default, es. da Blender, AmbientCG, Poly Haven) non richiedono inversione. |

### Formato delle Normal Map

Le normal map sono immagini RGB dove ogni canale codifica un asse del vettore normale nel tangent space:

| Canale | Asse | Colore neutro (128) | Significato |
|--------|------|----------------------|-------------|
| **R** (Rosso) | X | Grigio medio | Inclinazione sinistra/destra |
| **G** (Verde) | Y | Grigio medio | Inclinazione su/giù |
| **B** (Blu) | Z | Quasi bianco (255) | Profondità (verso l'esterno) |

Una normale piatta (nessuna perturbazione) corrisponde al colore RGB `(128, 128, 255)` — la tipica tinta violetto-azzurra delle normal map.

> **Le normal map NON vengono corrette in gamma.** A differenza delle texture albedo che vengono convertite sRGB→lineare, le normal map contengono dati direzionali e vengono lette come valori lineari. Non applicare correzione gamma manualmente.

### Dove trovare Normal Map

Normal map gratuite e CC0 (libere da royalty):
- [ambientcg.com](https://ambientcg.com) — set PBR completi con albedo + normal + roughness
- [polyhaven.com/textures](https://polyhaven.com/textures) — alta qualità, set 4K con tutti i canali PBR
- [3dtextures.me](https://3dtextures.me) — vasta libreria CC0

### Generare Normal Map di Test con NormalMapGen

Il progetto include il tool `NormalMapGen` che genera normal map procedurali abbinate alle texture di `TextureGen`:

```powershell
cd 3d-ray
dotnet run --project src/Tools/NormalMapGen/NormalMapGen.csproj
```

Normal map generate nella cartella `scenes/textures/`:

| File | Contenuto |
|------|-----------|
| `brick-wall-normal.png` | Mattoni: fughe incavate con bevel, superficie ruvida |
| `wood-floor-normal.png` | Parquet: doghe, fughe, venature |
| `wood-planks-normal.png` | Assi larghe: giunture, nodi, grana grossa |
| `concrete-normal.png` | Cemento: pori e ondulazioni multi-frequenza |
| `metal-scratched-normal.png` | Metallo: graffi lineari casuali |
| `stone-cobble-normal.png` | Ciottoli: forma Voronoi irregolare |
| `fabric-weave-normal.png` | Tessuto: trama intrecciata |
| `tiles-normal.png` | Piastrelle: fughe con bevel |
| `flat-normal.png` | Piatta `(128,128,255)` — test di riferimento (nessuna perturbazione) |

### Compatibilità con le Primitive

Il normal mapping funziona su tutte le primitive. Il frame TBN (Tangent, Bitangent, Normal) viene calcolato internamente da ogni primitiva in base al suo UV mapping nativo, trasformato correttamente nel caso di oggetti con `scale`, `rotate`, `translate`.

| Primitiva | UV mapping | Note |
|-----------|------------|------|
| Sphere | Sferico (lon/lat) | TBN allineato alle direzioni di phi e theta |
| Quad | Baricentric (alpha, beta) | T lungo U, B lungo V — identico all'UV |
| Box | Planare per faccia | T e B allineati agli assi della faccia |
| Cylinder (corpo) | Cilindrico (theta, altezza) | T tangenziale, B verticale |
| Cylinder (caps) | Planare | T lungo X, B lungo Z |
| Disk | Planare locale | T e B calcolati dalla normale del disco |
| Infinite Plane | Planare tiled | T e B da base ortonormale locale |
| Triangle | Baricentric | T lungo edge V0→V1, B lungo V0→V2 |

### Esempi

**Muro di mattoni con rilievo pronunciato:**
```yaml
  - id: "mattoni_rilievo"
    type: "lambertian"
    texture:
      type: "image"
      path: "textures/brick-wall.png"
      uv_scale: [3, 2]
    normal_map:
      path: "textures/brick-wall-normal.png"
      strength: 1.5
      uv_scale: [3, 2]            # stesso uv_scale della texture albedo
```

**Metallo graffiato con riflesso:**
```yaml
  - id: "acciaio"
    type: "metal"
    color: [0.88, 0.88, 0.90]
    fuzz: 0.02
    normal_map:
      path: "textures/metal-scratched-normal.png"
      strength: 1.2
```

**Vetro satinato (normal map su dielectric):**
```yaml
  - id: "vetro_satinato"
    type: "dielectric"
    refraction_index: 1.52
    normal_map:
      path: "textures/concrete-normal.png"
      strength: 0.4              # bassa intensità per effetto satinato sottile
```

**Materiale con mappa DirectX-style** (canale verde invertito):
```yaml
  - id: "pietra"
    type: "lambertian"
    color: [0.6, 0.55, 0.5]
    normal_map:
      path: "textures/stone-directx-normal.png"
      strength: 1.0
      flip_y: true               # inverte il canale G per mappe DirectX
```

> **💡 Tips:**
> - Mantieni sempre `uv_scale` identico tra `texture` e `normal_map` per evitare disallineamento visibile tra colore e bump.
> - Per una luce radente (quasi parallela alla superficie), l'effetto del normal mapping è massimo e le fughe/rilievi diventano molto evidenti. Per una luce frontale, l'effetto è più sottile.
> - Il file `flat-normal.png` generato da NormalMapGen (solo `(128,128,255)`) è il test ideale: applicarlo non deve cambiare nulla nel render — verificabile visivamente.
> - Usa `strength: 2.0`–`3.0` solo per test o effetti volutamente esagerati. Per materiali realistici, `0.8`–`1.5` dà risultati più credibili.

---

---

[← Torna all'indice](../02-tutorial-scene.md)
