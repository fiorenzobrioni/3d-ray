# Capitolo 10: Preset e progetti

3D-Ray viene fornito con un ricco catalogo di blocchi pronti all'uso:
centinaia di materiali, una dozzina di set di illuminazione (più preset
emissivi per luci geometriche), ricette di mezzi partecipanti, ricette per
terreni, texture immagine e template di font 3D. Questo capitolo mostra come
utilizzarli, fornisce il riferimento completo della CLI e guida nella
costruzione di un vero progetto.

I preset di materiali, luci, mezzi, world, sky e terreni sono **cataloghi
copia-incolla** sotto `scenes/presets/` — file Markdown pieni di blocchi YAML
validati. Le risorse binarie che referenziano (texture immagine, template di
font, heightmap) vivono sotto `scenes/assets/`.

---

## 10.1 I cataloghi di preset

Tutti i cataloghi di preset vivono in `scenes/presets/` come file Markdown.
Apri quello che ti serve, copi un blocco YAML e lo incolli nella tua scena:

```
scenes/presets/
  README.md                  Come funziona il flusso copia-incolla
  world.md                   Cielo + terreno abbinati (+ medium opzionale)
  sky.md                     Modelli di cielo in isolamento (flat, gradient, Preetham, Nishita, Hosek, HDRI)
  materials-stone.md         Marmi, graniti, travertino, onice, alabastro, mattone, cemento
  materials-metal.md         Metalli grezzi e lucidati, vernici industriali
  materials-wood.md          Legni grezzi, laccati, verniciati
  materials-glass.md         Vetri, gemme/minerali, liquidi (famiglia trasmissiva)
  materials-organic.md       Tessuti, pelli, cibi, organici
  materials-synthetic.md     Plastiche, sintetici, vernici, ceramiche
  materials-ground.md        Materiali per il terreno (si abbinano a world.md)
  materials-weathering.md    Overlay di invecchiamento e ricette `mix`
  lights.md                  Set di luci pronti (3-point, high-key, golden hour, neon, …)
  mediums.md                 Atmosfere, nebbie, ghiaccio/neve, SSS, liquidi volumetrici
  terrains.md                Ricetta heightfield + strati altimetrici/pendenza
```

Le risorse binarie referenziate dai cataloghi vivono sotto `scenes/assets/`:

```
scenes/assets/
  textures/     Texture immagine PNG (albedo + normal/roughness/AO map)
  fonts/        Template caratteri 3D (generati da FontGen) + sorgenti .ttf
  heightmaps/   Heightmap PNG grayscale a 16 bit (generati da TerrainGen)
```

### Il flusso copia-incolla

1. Apri il catalogo della famiglia che ti serve (tabelle sotto).
2. Copia il blocco `materials:` / `lights:` / `mediums:` del preset scelto.
3. Incollalo nella tua scena. Le voci con lo stesso `id` si fondono nel
   blocco locale della scena.
4. Referenzia l'`id` dalle tue entità (`material: "..."`) e ritocca colore,
   scala o roughness a piacere.

```yaml
# nella tua scena
materials:
  - id: "carrara_lucido"        # ← incollato da presets/materials-stone.md
    type: "disney"
    # ...

entities:
  - type: "sphere"
    center: [0, 1, 0]
    radius: 1
    material: "carrara_lucido"
```

Gli `id` dei preset sono brevi e descrittivi e sono pensati per essere
**rinominati** liberamente nella tua scena. Ogni blocco è validato contro lo
schema del motore e renderizza senza warning.

---

## 10.2 Cataloghi dei materiali

Otto cataloghi a tema coprono ogni tipo di superficie di cui si potrebbe
aver bisogno. La maggior parte dei materiali usa il **Disney BSDF**
(`type: disney`); un tipo classico (`lambertian`, `metal`, `dielectric`)
si usa solo dove è la scelta corretta (es. semplici superfici di sfondo).

| Catalogo                      | Contenuti                                       |
|-------------------------------|-------------------------------------------------|
| `materials-stone.md`          | Marmi bianchi/scuri/colorati, graniti, travertino, ardesia, onice/alabastro (SSS), basalto, mattoni, cemento |
| `materials-metal.md`          | Oro, argento, rame, bronzo, ottone, acciaio, ferro/ghisa, alluminio (incl. anodizzati), titanio (thin-film), cromo, più vernici industriali |
| `materials-wood.md`           | Latifoglie chiare/medie/scure, ebano, esotici, trattati (shou-sugi-ban, barnwood), legni studio figurati |
| `materials-glass.md`          | Vetri industriali/ottici, cristalli, gemme (preziose + semipreziose), ghiaccio, liquidi, frosted |
| `materials-organic.md`        | Tessuti (sheen), pelli, cibi, cera, ambra, avorio, madreperla, sughero, carta |
| `materials-synthetic.md`      | Plastiche, carbon fiber (anisotropic), kevlar, siliconi, vernici, ceramiche, porcellana |
| `materials-ground.md`         | Erba, sottobosco, sabbia desertica, terra secca, roccia, ghiaia, neve fresca, acqua — si abbina a `world.md` |
| `materials-weathering.md`     | Overlay `over_*` per `type: mix` (ruggine, muschio, polvere, calcare) e ricette `mix_*` pronte |

### Convenzioni

La maggior parte dei materiali è `type: disney` (PBR completo con clearcoat,
sheen, `spec_trans`, `thin_film`). Abbina un materiale Disney a un binding
`interior_medium` sull'entity per il Random Walk SSS. Il catalogo
weathering usa due famiglie di id:

- **`over_`** — un overlay di invecchiamento, combinato via `type: mix` su un
  materiale base con una maschera procedurale.
- **`mix_`** — una ricetta composita pronta all'uso (base + overlay + maschera).

### Utilizzo

Copia i blocchi che ti servono direttamente nella sezione `materials:` della
tua scena, poi referenziali per `id`:

```yaml
materials:
  - id: "oro_lucido"            # ← incollato da presets/materials-metal.md
    type: "disney"
    color: [1.0, 0.71, 0.29]
    metallic: 1.0
    roughness: 0.05
    specular: 0.8

  - id: "diamante"             # ← incollato da presets/materials-glass.md
    type: "disney"
    # ...

entities:
  - type: "sphere"
    center: [0, 1, 0]
    radius: 1.0
    material: "oro_lucido"

  - type: "sphere"
    center: [2, 0.5, 0]
    radius: 0.5
    material: "diamante"
```

### Personalizzare un preset

Poiché incolli il blocco nella tua scena, personalizzarlo significa solo
modificare i valori che hai copiato — cambia `color`, `roughness` o `scale`
sul posto, e rinomina l'`id` come preferisci:

```yaml
materials:
  # Un oro più caldo e lucido basato sul preset dell'oro lucido
  - id: "mio_oro"
    type: "disney"
    color: [0.98, 0.8, 0.3]
    metallic: 1.0
    roughness: 0.08
```

---

## 10.3 Catalogo dell'illuminazione (`lights.md`)

`lights.md` è organizzato in sezioni per ambiente. Copia il blocco `lights:`
di una sezione (e, dove suggerito, il blocco `world:` abbinato) nella tua
scena:

### Studio e prodotto

| Sezione            | Configurazione       | Atmosfera                      |
|--------------------|----------------------|--------------------------------|
| 3-point            | 3-punti classico     | Universale prodotto/ritratto   |
| High-key           | High key             | Pulito, commerciale, moda      |
| Drammatico (low-key)| Low key/Chiaroscuro | Noir, ombre drammatiche        |
| Prodotto           | Prodotto/Gioielleria | Riflessi (catchlights) precisi |

### Esterni e notte

| Sezione      | Configurazione | Atmosfera                           |
|--------------|----------------|-------------------------------------|
| Golden hour  | Sole caldo     | Bagliore cinematografico caldo      |
| Mezzogiorno  | Sole alto      | Luce dura, ombre corte              |
| Coperto      | Nuvoloso       | Morbida, uniforme, senza ombre nette|
| Luce di luna | Notte di luna  | Freddo-blu, misterioso              |
| Neon/Cyberpunk| Neon          | Sci-fi, colori vibranti             |

### Utilizzo

```yaml
lights:                          # ← incollato da presets/lights.md (3-point)
  - type: "area"
    # key light ...
  - type: "area"
    # fill light ...
  - type: "area"
    # rim light ...

# Ogni sezione di luci suggerisce una configurazione world corrispondente nelle note.
world:
  sky:
    type: "flat"
    color: [0.0, 0.0, 0.0]
```

### Luci geometriche (emissive)

La sezione "Luci geometriche (emissive)" di `lights.md` contiene **materiali**
emissivi `emi_*` che trasformano qualsiasi geometria in una sorgente luminosa
partecipante alla NEE — senza aggiungere un'entità luce esplicita. Coprono
la scala blackbody (candela 2000 K → bianco freddo 7000 K) più sorgenti
speciali (fuoco, braci, strisce LED, bioluminescenza, disco solare diretto).

Copia il blocco `materials:` e applica il materiale a qualsiasi geometria. Il
motore registra automaticamente la geometria come `GeometryLight` e la include
nel campionamento NEE:

```yaml
materials:
  - id: "emi_tungsteno"         # ← incollato da presets/lights.md
    type: "emissive"
    # ...

entities:
  # Una sfera lampada a temperatura tungsteno
  - type: "sphere"
    center: [0, 3, 0]
    radius: 0.15
    material: "emi_tungsteno"
```

Suggerimento: le luci geometriche partecipano automaticamente alla NEE
pesata per potenza. Per scene con molte luci geometriche,
`--light-sampling power` riduce la varianza quando le intensità delle
luci differiscono notevolmente.

### Light hardening: ridurre i firefly senza alzare gli spp

Tutti i preset di luce sono calibrati con i parametri di *light hardening*
del motore (vedi `devlog/2026/2026-06-17.md` §Ciclo Light Hardening e
`docs/reference/scene-reference.md` §8). Le tre manopole chiave:

- **`soft_radius`** (point, spot, area) — modella il diametro fisico della
  sorgente. Il termine `1/d²` (e `cosLight/d²` per le area) viene chiuso
  sotto a `max(d², r²)`, eliminando i firefly persistenti che compaiono
  quando un raggio (o un evento di scattering nella foschia) atterra
  vicinissimo all'emettitore. Valori tipici: 0.05–0.20 per lampadine,
  0.10–0.30 per neon o riflettori, 0.20 per softbox in foschia.
- **`angular_radius`** (directional) — diametro angolare in gradi del
  disco solare/lunare. `0.27` = sole reale, `0.5` = luna piena. Quando
  attivo produce penombre fisiche (cone-sampling, `shadow_samples` interno
  4) anziché ombre dure infinitamente nette. I preset outdoor lo usano già
  su tutti i sole/luna.
- **`shadow_samples`** (spot, area, sphere) — campioni jitterati per la
  visibilità. Su `spot` ha effetto solo se anche `soft_radius > 0`.

Per le scene volumetriche pesanti (mezzi `procedural`/`grid`, oppure
`height_fog` denso) considera anche da CLI:

```
--indirect-clamp-factor 0.25   # clamp dei rimbalzi indiretti a 1/4 di -C
--light-sampling power         # campiona una luce per evento NEE in
                               # proporzione alla potenza (riduce varianza
                               # con molte luci di brillanza mista)
```

In sintesi: prima di alzare `-s` per togliere il rumore, controlla se
i firefly arrivano da `1/d²` non clampato (visibili come pixel
isolati molto luminosi vicini alle sorgenti). In quel caso `soft_radius`
è la cura giusta — gratis e fisicamente coerente.

---

## 10.4 Texture immagine (`scenes/assets/textures/`)

La cartella `scenes/assets/textures/` contiene le texture immagine PNG
incluse, referenziate per path relativo dal file della scena.

### Set Albedo + map
- `brick-wall.png` + `brick-wall-normal.png` (+ roughness, AO)
- `brick-wall-white.png` (condivide `brick-wall-normal.png`)
- `concrete.png` + `concrete-normal.png` (+ roughness, AO)
- `metal-scratched.png` + `metal-scratched-normal.png` (+ roughness, metallic)
- `wood-floor.png` + `wood-floor-normal.png` (+ roughness, AO)
- `wood-planks.png` + `wood-planks-normal.png` (+ roughness, AO)
- `earth.png` + `earth-roughness.png`

### Solo normal map
- `fabric-weave-normal.png` -- sovrapposizione texture tessuta
- `stone-cobble-normal.png` -- pavimentazione a ciottoli
- `tiles-normal.png` -- linee di fuga delle piastrelle
- `flat-normal.png` -- piatta neutrale (disabilita la mappatura delle normali in modo pulito)

### Specialità
- `checkerboard.png` -- test UV
- `grid-uv.png` -- griglia di verifica UV numerata
- `logo-3dray.png` -- logo del motore

### Utilizzo

```yaml
materials:
  - id: "brick_wall"
    type: "disney"
    roughness: 0.7
    texture:
      type: "image"
      path: "assets/textures/brick-wall.png"
      uv_scale: [2, 2]
    normal_map:
      path: "assets/textures/brick-wall-normal.png"
      strength: 1.0
      uv_scale: [2, 2]
```

---

## 10.5 Template di font 3D (`scenes/assets/fonts/`)

La directory `scenes/assets/fonts/` contiene file di template di caratteri
3D generati dallo strumento `FontGen`. Ogni file copre una famiglia di font
e include template per le lettere maiuscole (A–Z), le minuscole (a–z) e le
cifre (0–9). I font sorgente `.ttf` abbinati vivono sotto
`scenes/assets/fonts/ttf/`.

File di esempio: `assets/fonts/font-open-sans.yaml` — famiglia Open Sans.

Un file di template di font è una delle poche cose che si *importano*
davvero (anziché copiare-incollare), perché porta con sé decine di template
di estrusione. Riferisci un template di carattere usando `type: "instance"`:

```yaml
imports:
  - path: "assets/fonts/font-open-sans.yaml"

materials:
  # Definisci o sovrascrivi il materiale font usato da tutti i caratteri
  - id: "font_material"
    type: "disney"
    color: [0.9, 0.85, 0.7]
    metallic: 0.0
    roughness: 0.3

entities:
  - type: "instance"
    template: "lettera_A_maiusc_open-sans"
    translate: [0, 0, 0]
    scale: [1, 1, 1]

  - type: "instance"
    template: "lettera_b_minusc_open-sans"
    translate: [0.7, 0, 0]
```

Ogni template usa un materiale chiamato `font_material`. Ridefiniscilo
nella tua scena per applicare la tua superficie a tutti i caratteri
senza modificare il file del font.

### Generare template di font

Usa lo strumento `FontGen` per creare nuovi file font da qualsiasi font
di sistema o file .ttf/.otf:

```
dotnet run --project src/Tools/FontGen/FontGen.csproj -- \
  --font "Open Sans" --height 0.2 --chars "CIAO"
```

Usa `--list-fonts` per vedere tutti i font di sistema disponibili. I
template generati vengono scritti in `scenes/assets/fonts/`.

---

## 10.6 Terreni heightfield (`terrains.md` + `scenes/assets/heightmaps/`)

Il catalogo `terrains.md` fornisce una ricetta heightfield copia-incolla:
una heightmap in scala di grigi (PNG a 16 bit sotto
`scenes/assets/heightmaps/`) viene intersecata direttamente dal motore come
primitiva `heightfield` — senza tessellazione mesh — con materiali assegnati
a fasce di altitudine/pendenza (`strata`).

Il motore interseca l'heightfield direttamente tramite un quadtree min/max
mipmap, quindi una sola primitiva sostituisce un'intera mesh di terreno.

### Come dispatcher di terreno (`world.ground`)

Incolla i materiali del terreno e il blocco `world.ground` dal catalogo;
l'heightfield diventa il suolo della scena:

```yaml
world:
  ground:
    type: "heightfield"
    heightmap_path: "assets/heightmaps/heightfield-strata-test-height.png"
    bounds: [-50, -50, 50, 50]
    height_scale: 25.0
    strata:
      - { min_altitude: 0.0, max_altitude: 0.36, min_slope_deg: 0, max_slope_deg: 35, blend_width: 0.04, material: "t_sand" }
      - { min_altitude: 0.5, max_altitude: 1.0,  min_slope_deg: 0, max_slope_deg: 90, blend_width: 0.08, material: "t_rock" }
    material: "t_ground"
```

### Come entità posizionabile

Gli stessi campi funzionano su un'entità `type: heightfield`, che puoi
posizionare e trasformare ovunque nella scena:

```yaml
entities:
  - type: "heightfield"
    heightmap_path: "assets/heightmaps/heightfield-strata-test-height.png"
    bounds: [-50, -50, 50, 50]
    height_scale: 25.0
    material: "t_ground"
```

### Generare heightmap

Usa lo strumento `TerrainGen` per creare nuove heightmap (e un template di
terreno pronto da incollare):

```
dotnet run --project src/Tools/TerrainGen/TerrainGen.csproj -- \
  --name mie-colline --type collina --season estate --with-cameras
```

`--type` accetta `pianura`, `collina` o `montagna`. Lo strumento scrive una
heightmap `<name>-height.png` in `scenes/assets/heightmaps/`; con
`--with-cameras` genera anche un `scenes/<name>-preview.yaml` pronto da
renderizzare.

---

## 10.7 Riferimento CLI

L'insieme completo dei parametri della riga di comando:

| Flag | Forma lunga         | Predefinito                   | Descrizione                                                  |
|------|---------------------|-------------------------------|--------------------------------------------------------------|
| `-i` | `--input`           | *(richiesto)*                 | Percorso del file YAML della scena                           |
| `-o` | `--output`          | `renders/render-<scena>.png`  | Percorso dell'immagine di output (PNG, JPG o BMP)            |
| `-w` | `--width`           | `960` (draft-small)           | Larghezza dell'immagine in pixel                             |
| `-H` | `--height`          | `540` (draft-small)           | Altezza dell'immagine in pixel                               |
| `-s` | `--samples`         | `16` (draft-small)            | Campioni per pixel (Sobol: conteggio esatto; PRNG: arrotondato al quadrato perfetto superiore) |
| `-d` | `--depth`           | `4` (draft-small; `8` nei preset di qualità — alza a 16+ per vetri impilati) | Numero massimo di rimbalzi dei raggi |
| `-S` | `--shadow-samples`  | *(per luce)*                  | Sovrascrive i campioni d'ombra per tutte le luci area/sphere (quadrati perfetti) |
| `-C` | `--clamp`           | `10`                          | Firefly clamp: radianza massima per-campione prima del tone mapping |
| `-c` | `--camera`          | `0`                           | Seleziona la fotocamera per nome o indice base zero          |
|      | `--sampler`         | `sobol`                       | Sampler per-pixel: `sobol` (Owen-scrambled) o `prng`         |
|      | `--mis`             | `balance`                     | Heuristica MIS: `balance` o `power`                          |
|      | `--light-sampling`  | `all`                         | Strategia NEE: `all`, `power`, `uniform`                     |
|      | `--indirect-clamp-factor` | `0.25`                  | Fattore di clamp del contributo indiretto, applicato una volta relativo alla camera (`0.25` → clamp indiretto = ¼ di `-C`; `1.0` = disattivato) |
|      | `--texture-filtering` | `auto`                      | Anti-aliasing analitico delle texture procedurali / image via ray differentials: `auto` / `on` (filtering attivo) o `off` (point-sampled, per benchmark) |
|      | `--list-cameras`    |                               | Elenca le fotocamere disponibili ed esce                     |
| `-v` | `--verbose`         |                               | Output dettagliato di scene-load e tuning Russian Roulette   |
| `-h` | `--help`            |                               | Mostra l'aiuto                                               |

### Formato di output

Il formato è determinato dall'estensione del file:
- `.png` -- PNG (predefinito, senza perdita di qualità)
- `.jpg` / `.jpeg` -- JPEG (con perdita di qualità, file più piccoli)
- `.bmp` -- BMP (non compresso)

### Arrotondamento dei campioni

L'arrotondamento dipende dal sampler attivo (`--sampler`, default `sobol`):

- **Sobol (predefinito):** viene usato il conteggio esatto richiesto — `-s 15` esegue esattamente 15 campioni per pixel.
- **PRNG (`--sampler prng`):** il conteggio viene arrotondato per eccesso al quadrato perfetto superiore. La tabella mostra come PRNG arrotonda:

| Richiesti | Effettivi (PRNG) | Griglia  |
|-----------|-----------------|----------|
| 1         | 1               | 1x1      |
| 10        | 16              | 4x4      |
| 20        | 25              | 5x5      |
| 50        | 64              | 8x8      |
| 100       | 100             | 10x10    |
| 200       | 225             | 15x15    |
| 256       | 256             | 16x16    |

---

## 10.8 Costruire un progetto completo: Passo dopo passo

Ecco il flusso di lavoro per creare una scena da zero utilizzando i cataloghi
di preset:

### Passo 1: Configura World e Camera

```yaml
world:
  sky:
    type: "flat"
    color: [0.0, 0.0, 0.0]

cameras:
  - name: "main"
    position: [0, 2, -6]
    look_at: [0, 1, 0]
    fov: 45
```

### Passo 2: Incolla materiali e luci

Apri `presets/materials-metal.md`, `presets/materials-stone.md` e la sezione
3-point di `presets/lights.md`, e copia i blocchi che ti servono nelle sezioni
`materials:` e `lights:` della tua scena:

```yaml
materials:
  - id: "carrara_lucido"        # ← presets/materials-stone.md
    type: "disney"
    # ...
  - id: "oro_lucido"            # ← presets/materials-metal.md
    type: "disney"
    # ...

lights:
  # ← blocco 3-point incollato da presets/lights.md
  - type: "area"
    # ...
```

### Passo 3: Aggiungi le entità

```yaml
entities:
  # Pavimento
  - type: "infinite_plane"
    point: [0, 0, 0]
    normal: [0, 1, 0]
    material: "carrara_lucido"

  # Una sfera con un materiale incollato al centro
  - type: "sphere"
    center: [0, 1.0, 0]
    radius: 0.5
    material: "oro_lucido"

  # Oggetto personalizzato
  - type: "sphere"
    center: [0, 0.78, 0]
    radius: 0.15
    material: "diamante"
```

### Passo 4: Itera

Usa i tre profili di rendering canonici:

```
# Preview — composizione / camere / materiali (secondi)
RayTracer -i my-scene.yaml -w 400 -H 225 -s 64 -d 4 -S 1

# Standard — render di review e CI/CD (minuti)
RayTracer -i my-scene.yaml -w 800 -H 450 -s 256 -d 6

# Final — qualità portfolio / copertina README
RayTracer -i my-scene.yaml -w 1920 -H 1080 -s 1024 -d 8 -S 4
```

Per la spiegazione completa di ciascun parametro, il comportamento della
Russian Roulette, l'eccezione dei vetri impilati che impone `-d 16+` e la
manopola `-C`/`--clamp` del firefly clamp, consulta
**[Profili di Rendering](../../reference/profili-di-rendering.md)**.

---

## 10.9 Organizzare un progetto più grande

Quando una scena supera un paio di centinaia di entità, un po' di struttura
ripaga:

- **Costruisci la tua palette dai cataloghi.** Incolla solo i blocchi di
  materiali, luci e mezzi che usi davvero, e assegna loro `id` specifici
  della scena (`mat_floor`, `mat_wall`, `mat_glass`). Così la sezione
  `materials:` resta leggibile.
- **Suddividi la tua scena con `imports:`.** Sposta i template di oggetti
  ripetuti in un file separato e includilo con `imports:`. I percorsi sono
  relativi al file che importa; le definizioni locali sovrascrivono quelle
  importate; `world` e `cameras` non vengono mai importati. (Vedi il
  Capitolo 5 per la meccanica completa.)
- **Tieni le risorse binarie sotto `scenes/assets/`.** Texture immagine,
  template di font e heightmap stanno in `assets/{textures,fonts,heightmaps}/`
  e si referenziano per path relativo. Gli asset generati (FontGen,
  TerrainGen) finiscono lì.
- **Dai un nome a tutto.** Assegna a ogni entità e template un `name:`
  descrittivo, così l'output `--verbose` e i warning sono facili da leggere.

---

## 10.10 Guida alla risoluzione dei problemi

### Immagine nera
- **Nessuna luce.** Aggiungere luci nella sezione `lights:` o usare oggetti emissivi / cielo HDRI.
- **Tutte le luci hanno intensità zero.** Verificare che `intensity` sia positivo.
- **Fotocamera all'interno di un oggetto.** Spostare la `position` della fotocamera fuori da ogni geometria.
- **Fotocamera rivolta nella direzione sbagliata.** Controllare il punto `look_at`.
- **`sky.color: [0,0,0]` senza luci.** Una scena necessita di almeno una sorgente luminosa o un cielo non nullo. Impostare `sky.type: flat` con `color: [0.02, 0.02, 0.02]` fornisce un emettitore globale fioco che aiuta a localizzare la geometria anche senza luci esplicite.

### Scena piatta o sbiadita
- **Cielo troppo luminoso rispetto alla key light.** Un cielo flat con `color` alto (es. `[0.5, 0.5, 0.5]`) inietta molto fill su ogni superficie; se domina la key light si perde il contrasto delle ombre. Abbassare il colore del cielo o aumentare l'`intensity` della key light.
- **Nessuna luce direzionale dominante.** Una scena illuminata solo dal cielo non ha una direzione d'ombra chiara; aggiungere una key light (directional, area o sun disk in un gradient sky) con intensità maggiore per stabilire il contrasto.
- **Tutte le luci hanno lo stesso colore.** Gli ambienti reali mescolano luce calda e fredda. Provare una key light calda (`[1.0, 0.9, 0.75]`) abbinata a un cielo flat freddo (`[0.05, 0.07, 0.1]`).

### Troppo rumore
- Aumentare i campioni: `-s 64` o `-s 256`.
- Aumentare i campioni d'ombra: `-S 16`.
- I materiali Disney densi (sheen, thin-film) e il Random Walk SSS necessitano di più campioni rispetto ai tipi classici.
- **La profondità di campo è attiva.** Un `aperture` non zero richiede molti più campioni per eliminare il rumore del bokeh. Usare almeno `-s 256` per render DOF puliti.
- **Materiale emissivo dentro un nodo CSG.** Il motore avvisa di questo problema; la superficie emissiva non può partecipare alla Next Event Estimation e causa alta varianza. Spostare la primitiva emissiva fuori dall'albero CSG.

### Rendering molto lento
- Ridurre la risoluzione e i campioni durante i test.
- Usare il flusso di lavoro anteprima/bozza/finale.
- Sostituire i materiali Disney con equivalenti classici per le superfici di sfondo.
- **Troppi campioni d'ombra.** Il costo di `-S` è moltiplicativo: `-S 9` con due area light e `-s 256` a 6 rimbalzi equivale a oltre 27.000 raggi ombra per pixel. Usare `-S 1` o `-S 4` a meno che non si necessiti specificamente di ombre morbide più nitide.

### Materiale mancante (l'oggetto appare grigio predefinito)
- Controllare eventuali errori di battitura nell'ID del materiale.
- Assicurarsi che il materiale sia effettivamente definito nella sezione `materials:` della scena (il blocco incollato deve essere presente, non solo referenziato).
- Controllare i messaggi di avviso sulla console per riferimenti a materiali non risolti.

### Colori sbagliati
- I colori sono `[R, G, B]` nell'intervallo **0.0--1.0**, non 0--255. `[255, 0, 0]` non è rosso -- è un bianco estremamente luminoso.

### Oggetto nel posto sbagliato o invisibile
- Controllare il sistema di coordinate: **Y è in alto**, il pavimento è a Y = 0.
  Gli oggetti posizionati a Y negativo sono sotto il suolo.
- Un `translate` di `[0, 0, 5]` sposta l'oggetto **nella scena** (Z positivo),
  non verso la fotocamera. Per avvicinarsi alla fotocamera predefinita, usare Z negativo.
- Usare `--verbose` per stampare il bounding box della scena e localizzare gli oggetti persi.

### Il vetro appare strano (troppo scuro o solido)
- Aumentare la profondità dei raggi: `-d 16` o superiore (il vetro consuma 2 rimbalzi per superficie, quindi vetri impilati o annidati esauriscono rapidamente il default `-d 8`).
- Assicurarsi che ci sia luce dietro/intorno all'oggetto di vetro (il vetro trasmette la luce, quindi ha bisogno di qualcosa da trasmettere).

### Le texture non vengono visualizzate (ripiego magenta/rosa)
- Controllare che il percorso del file della texture sia corretto e relativo al file della scena (es. `assets/textures/brick-wall.png`).
- Verificare che il file esista e sia in un formato supportato (PNG, JPEG, BMP).

### Gli import non funzionano
- I percorsi sono relativi al **file che importa**, non alla directory di lavoro corrente.
- Controllare la presenza di import circolari (il motore avvisa sulla console).
- Assicurarsi che il file importato abbia la struttura YAML corretta (`materials:`, `templates:`, ecc.).

---

## 10.11 Esempio completo: Sala espositiva

Una scena che combina materiali e luci incollati da diversi cataloghi in un
progetto coeso.

```yaml
# exhibition-hall.yaml
# Una stanza simile a un museo che mostra oggetti di diverse famiglie di materiali.
# Materiali incollati da presets/materials-metal.md e materials-glass.md;
# il materiale emi_daylight dalla sezione luci geometriche di presets/lights.md.

world:
  sky:
    type: "flat"
    color: [0.0, 0.0, 0.0]

cameras:
  - name: "overview"
    position: [0, 3, -7]
    look_at: [0, 1.2, 0]
    fov: 50

  - name: "detail"
    position: [1.5, 1.5, -4]
    look_at: [0.5, 1.2, 0]
    fov: 35
    aperture: 0.08
    focal_dist: 4.5

materials:
  # ── Incollati dai cataloghi ─────────────────────────────────────────
  - id: "oro_lucido"            # presets/materials-metal.md
    type: "disney"
    color: [1.0, 0.71, 0.29]
    metallic: 1.0
    roughness: 0.05
    specular: 0.8

  - id: "diamante"             # presets/materials-glass.md
    type: "disney"
    # ... (incolla il blocco diamante completo)

  - id: "emi_daylight"         # presets/lights.md (luci geometriche)
    type: "emissive"
    # ... (incolla il blocco emi_daylight completo)

  # ── Superfici personalizzate ────────────────────────────────────────
  - id: "hall_floor"
    type: "disney"
    roughness: 0.1
    specular: 0.8
    texture:
      type: "checker"
      scale: 0.3
      colors: [[0.85, 0.82, 0.78], [0.25, 0.22, 0.2]]

  - id: "pedestal"
    type: "disney"
    color: [0.2, 0.2, 0.22]
    roughness: 0.08
    specular: 0.7

  - id: "wall"
    type: "disney"
    color: [0.15, 0.14, 0.13]
    roughness: 0.6

entities:
  # Pavimento
  - type: "infinite_plane"
    point: [0, 0, 0]
    normal: [0, 1, 0]
    material: "hall_floor"

  # Parete di fondo
  - type: "quad"
    q: [-6, 0, 5]
    u: [12, 0, 0]
    v: [0, 5, 0]
    material: "wall"

  # Tre piedistalli con oggetti
  # Piedistallo sinistro: sfera d'oro
  - type: "cylinder"
    center: [-2, 0, 0]
    radius: 0.35
    height: 0.9
    material: "pedestal"

  - type: "sphere"
    center: [-2, 1.25, 0]
    radius: 0.35
    material: "oro_lucido"

  # Piedistallo centrale: sfera emissiva daylight (luce geometrica)
  - type: "cylinder"
    center: [0, 0, 0]
    radius: 0.35
    height: 0.9
    material: "pedestal"

  - type: "sphere"
    center: [0, 1.25, 0]
    radius: 0.35
    material: "emi_daylight"

  # Piedistallo destro: sfera di diamante
  - type: "cylinder"
    center: [2, 0, 0]
    radius: 0.35
    height: 0.9
    material: "pedestal"

  - type: "sphere"
    center: [2, 1.25, 0]
    radius: 0.35
    material: "diamante"

lights:
  # Luci spot individuali per ogni piedistallo.
  # `soft_radius` modella il bulbo fisico (8 cm) e azzera i firefly 1/d² su
  # materiali speculari close-up. `shadow_samples` produce penombre morbide.
  - type: "spot"
    position: [-2, 4, -1]
    direction: [0, -1, 0.2]
    color: [1, 0.97, 0.92]
    intensity: 60.0
    inner_angle: 10
    outer_angle: 22
    soft_radius: 0.08
    shadow_samples: 4

  - type: "spot"
    position: [0, 4, -1]
    direction: [0, -1, 0.2]
    color: [1, 0.97, 0.92]
    intensity: 60.0
    inner_angle: 10
    outer_angle: 22
    soft_radius: 0.08
    shadow_samples: 4

  - type: "spot"
    position: [2, 4, -1]
    direction: [0, -1, 0.2]
    color: [1, 0.97, 0.92]
    intensity: 60.0
    inner_angle: 10
    outer_angle: 22
    soft_radius: 0.08
    shadow_samples: 4

  # Lieve riempimento ambientale
  - type: "area"
    corner: [-3, 4.5, -3]
    u: [6, 0, 0]
    v: [0, 0, 4]
    color: [0.6, 0.65, 0.8]
    intensity: 5.0
    shadow_samples: 4
```

Renderizza con:

```
# Standard — revisione rapida
RayTracer -i exhibition-hall.yaml -c overview -w 800 -H 450 -s 256 -d 6

# Final — qualità portfolio
RayTracer -i exhibition-hall.yaml -c overview -w 1920 -H 1080 -s 1024 -d 8 -S 4
RayTracer -i exhibition-hall.yaml -c detail -w 1200 -H 800 -s 1024 -d 8 -S 4
```

---

## Cosa si è imparato

- I cataloghi di preset sotto `scenes/presets/` sono raccolte copia-incolla di
  YAML validato: materiali, luci, mezzi, abbinamenti world/sky e ricette di
  terreno.
- Il flusso è apri un catalogo → copia un blocco `materials:` / `lights:` /
  `mediums:` → incollalo nella tua scena → referenzia l'`id` dalle tue entità
  e ritocca.
- I materiali emissivi `emi_*` dalla sezione luci geometriche di `lights.md`
  trasformano qualsiasi geometria in una sorgente NEE partecipante — senza
  bisogno di un'entità luce esplicita.
- Le risorse binarie vivono sotto `scenes/assets/`: texture immagine
  (`assets/textures/`), template di font (`assets/fonts/`, generati da
  FontGen) e heightmap (`assets/heightmaps/`, generati da TerrainGen).
- `imports:` serve per suddividere la tua scena su più file e per includere
  template di font generati — non per i materiali, che si incollano.
- La CLI offre pieno controllo su risoluzione, qualità, selezione della
  fotocamera e formato di output.
- Il flusso di lavoro anteprima/bozza/finale è il modo più efficiente per
  sviluppare le scene.

---

[Precedente: Mezzi partecipanti (Volumetrics)](./09-volumetrics.md) | [Successivo: Superfici di rivoluzione (Lathe)](./11-lathe-surface-of-revolution.md) | [Indice del Tutorial](./README.md)
