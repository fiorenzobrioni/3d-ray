# Capitolo 10: Librerie di asset e scene complete

3D-Ray viene fornito con un ricco ecosistema di asset predefiniti: 1450
materiali, 14 configurazioni di illuminazione (più preset emissivi per luci
geometriche), texture immagine, template di font e heightfield per terreni.
Questo capitolo mostra come utilizzarli, fornisce il riferimento completo
della CLI e guida nella costruzione di un vero progetto.

---

## 10.1 L'ecosistema delle librerie

Tutte le librerie si trovano nella directory `scenes/libraries/`:

```
scenes/libraries/
  materials/      20 file YAML, 1450 materiali
  lights/         14 file YAML + geometry-lights.yaml
  textures/       20 file immagine PNG (albedo + normal map)
  fonts/          Template caratteri 3D (generati da FontGen)
  terrains/       Template heightfield (generati da TerrainGen)
```

Le librerie vengono caricate tramite la sezione `imports:` nel file della
scena. I percorsi sono relativi alla directory del file della scena.

---

## 10.2 Librerie dei Materiali

Venti file a tema che coprono ogni tipo di superficie di cui si potrebbe
aver bisogno:

| File                          | Contenuti                                       | Quantità |
|-------------------------------|-------------------------------------------------|----------|
| `materials/metals.yaml`       | Oro, argento, rame, bronzo, ottone, acciaio (incl. damasco), ferro, ghisa, alluminio (incl. anodizzati), titanio (anodizzato thin_film), cromo, platino, nichel, zinco, peltro, corten, mercurio, niobio olografico | 131 |
| `materials/ceramics.yaml`     | Porcellana, bone china, maiolica, terracotta, grès, raku, celadon (incl. crackle), biscotto, smaltate, sigillate, satin | 112 |
| `materials/plastics.yaml`     | ABS, policarbonato, acrilico, PVC, nylon, PLA, teflon, bachelite, gomma, silicone medicale, EVA, vinile | 105 |
| `materials/glasses.yaml`      | Vetri industriali/ottici, cristalli, gemme preziose e semipreziose, ghiaccio, liquidi, resine, smerigliati frosted | 101 |
| `materials/fabrics.yaml`      | Velluto e seta (sheen Charlie), raso, cotone, lino, lana, denim, tweed, feltro, neoprene, canvas, organza/tulle | 101 |
| `materials/foods.yaml`        | Cioccolato, frutta, verdura, formaggi, pane, pasta, dolci, burro/grassi | 100 |
| `materials/organics.yaml`     | Cera, ambra, avorio, corno, corallo, madreperla (thin_film), conchiglia, sughero, carta, sapone, bambù | 98 |
| `materials/paints.yaml`       | Auto metallizzata/pastello/perlata cangiante, lacche, smalti, chalk paint, pittura murale, spray | 93 |
| `materials/stones.yaml`       | Marmi bianchi/scuri/colorati (texture `marble` production-grade), graniti, travertino, ardesia, onice/alabastro (SSS), basalto, mattoni | 88 |
| `materials/woods.yaml`        | Latifoglie chiare/medie/scure, ebano, esotici, trattati (shou-sugi-ban, barnwood), studio (curly, flame, bird's eye, burl) | 87 |
| `materials/grounds.yaml`      | Checker, parquet, piastrelle, marmo pavimento, cemento, asfalto, terra, sabbia, ghiaia, erba, neve, moquette, acque | 75 |
| `materials/liquids.yaml`      | Acque, latticini, sangue, oli, alcolici (Beer-Lambert), sciroppi, bevande calde, succhi, refrigeranti | 53 |
| `materials/plasters.yaml`     | Rasati, graffiati, veneziano (clearcoat alto), marmorino, tadelakt, stucco antico, calce mediterranea, gesso | 50 |
| `materials/leathers.yaml`     | Pieno fiore, anilina, nappa, suede (sheen), patent (clearcoat), esotici (voronoi cell), box calf, cuoio grezzo, ecoleather | 46 |
| `materials/industrial-coatings.yaml` | Chassis auto, clearcoat, polveri RAL, anodizzazione Al/Ti, zincatura, cromature, smalti a fuoco, gel coat, termocromiche | 43 |
| `materials/concretes.yaml`    | Cemento liscio/esposto/lavorato/lavato a vista, colorati, asfalto (incl. bagnato), bitume catrame | 42 |
| `materials/synthetics.yaml`   | Carbon fiber (anisotropic), kevlar, vetroresina, neoprene, PTFE, silicone medicale, poliuretani, vinile auto wrap (olografico), tessuti tecnici, aerogel | 34 |
| `materials/minerals-gems.yaml`| Quarzi, geodi, druse, cristalli cubici, calcite islandese birifrangente, fluorite, malachite, lapislazzuli, pietra di luna, opali, kyanite | 30 |
| `materials/weathering.yaml`   | 26 overlay `over_*` per `type: mix`: ruggine, muschio, polvere, calcare, grasso, neve, vernice scrostata, foglie, sale marino | 26 |
| `materials/mix-recipes.yaml`  | 35 ricette `mix_*` pronte all'uso (metalli arrugginiti, legni usurati, intonaci macchiati, pietre colonizzate, vernici scrostate) | 35 |

**Totale: 1450 materiali.**

### Convenzione dei nomi

I materiali seguono un sistema di prefissi:

- **`dis_`** — Disney BSDF (PBR completo con clearcoat, sheen, spec_trans,
  thin_film). Abbina un binding `interior_medium` sull'entity per Random
  Walk SSS. Ideale per gli oggetti principali (hero objects) e i primi piani.
- **`cls_`** — Tipo classico (`lambertian`, `metal` o `dielectric`, scelto
  in base al lobo dominante). Più veloce e meno rumoroso; ideale per grandi
  superfici e sfondi.
- **`over_`** — Overlay weathering (in `weathering.yaml`) da usare via
  `type: mix`.
- **`mix_`** — Ricetta composita pronta all'uso (in `mix-recipes.yaml`) che
  combina `dis_*` base + `over_*` overlay + maschera procedurale.

Esempi:
- `dis_oro_lucido` — Oro lucido Disney
- `cls_oro_lucido` — Oro lucido Metal classico
- `dis_vetro_sodalime` — Vetro soda-lime Disney
- `cls_vetro_sodalime` — Vetro dielettrico classico
- `mix_acciaio_arrugginito_medio` — Acciaio satinato + ruggine medium con maschera FBM

### Utilizzo

```yaml
imports:
  - path: "libraries/materials/metals.yaml"
  - path: "libraries/materials/glasses.yaml"

entities:
  - type: "sphere"
    center: [0, 1, 0]
    radius: 1.0
    material: "dis_oro_lucido"      # Usa il materiale della libreria tramite ID

  - type: "sphere"
    center: [2, 0.5, 0]
    radius: 0.5
    material: "cls_diamante"        # Diamante classico
```

### Sovrascrivere un materiale della libreria

Per personalizzare un materiale della libreria, ridefiniscilo con lo stesso
ID nel tuo file di scena. Le definizioni locali hanno la precedenza:

```yaml
imports:
  - path: "libraries/materials/metals.yaml"

materials:
  # Il mio oro personalizzato (sovrascrive dis_oro_lucido della libreria)
  - id: "dis_oro_lucido"
    type: "disney"
    color: [0.98, 0.8, 0.3]
    metallic: 1.0
    roughness: 0.08
```

---

## 10.3 Librerie di Illuminazione

Quattordici configurazioni di illuminazione predefinite organizzate per
ambiente, più un file dedicato ai preset emissivi per luci geometriche.

### Studio

| File                         | Configurazione       | Atmosfera                      |
|------------------------------|----------------------|--------------------------------|
| `lights/studio-3point.yaml`  | 3-punti classico     | Universale prodotto/ritratto   |
| `lights/studio-highkey.yaml` | High key             | Pulito, commerciale, moda      |
| `lights/studio-dramatic.yaml`| Low key/Chiaroscuro  | Noir, ombre drammatiche        |
| `lights/studio-product.yaml` | Prodotto/Gioielleria | Riflessi (catchlights) precisi |

### Esterni (Outdoor)

| File                             | Configurazione  | Atmosfera                           |
|----------------------------------|-----------------|-------------------------------------|
| `lights/outdoor-noon.yaml`       | Sole mezzogiorno| Luce dura, ombre corte              |
| `lights/outdoor-golden-hour.yaml`| Ora d'oro       | Bagliore cinematografico caldo      |
| `lights/outdoor-sunset.yaml`     | Tramonto        | Arancione profondo, ombre lunghe    |
| `lights/outdoor-overcast.yaml`   | Nuvoloso        | Morbida, uniforme, senza ombre nette|

### Notte / Interni / Creativi

| File                               | Configurazione       | Atmosfera                    |
|------------------------------------|----------------------|------------------------------|
| `lights/night-moonlight.yaml`      | Notte di luna        | Freddo-blu, misterioso       |
| `lights/interior-warm.yaml`        | Interno caldo        | Accogliente, domestico       |
| `lights/interior-candlelight.yaml` | Lume di candela      | Romantico, medievale         |
| `lights/neon-cyberpunk.yaml`       | Neon/Cyberpunk       | Sci-fi, colori vibranti      |
| `lights/theatre-stage.yaml`        | Palco teatrale       | Spot drammatici              |
| `lights/museum-gallery.yaml`       | Galleria museale     | Spot precisi per esposizioni |

### Utilizzo

```yaml
imports:
  - path: "libraries/lights/studio-3point.yaml"

# Ogni libreria di luci suggerisce una configurazione world corrispondente nei commenti dell'intestazione.
world:
  sky:
    type: "flat"
    color: [0.0, 0.0, 0.0]
```

### Preset Luci Geometriche (`geometry-lights.yaml`)

`lights/geometry-lights.yaml` contiene 12 materiali emissivi `emi_*` che
trasformano qualsiasi geometria in una sorgente luminosa partecipante alla
NEE — senza aggiungere un'entità luce esplicita. I preset coprono la
scala blackbody completa e diverse sorgenti speciali:

| Preset               | Temperatura colore | Uso                                |
|----------------------|--------------------|------------------------------------|
| `emi_candela`        | 2000 K             | Fiamma di candela, lampada a olio  |
| `emi_tungsteno`      | 3000 K             | Lampadina a incandescenza          |
| `emi_alogeno`        | 3200 K             | Faretto alogeno, lampada fotografica|
| `emi_fluorescente`   | 4000 K             | Tubo fluorescente da ufficio       |
| `emi_daylight`       | 5500 K             | Fill bilanciato luce del giorno    |
| `emi_cool_white`     | 7000 K             | LED bianco freddo, fill cielo coperto|
| `emi_fuoco`          | —                  | Bagliore animato di fuoco (caldo)  |
| `emi_brace`          | —                  | Braci / carboni incandescenti      |
| `emi_led_strip_warm` | —                  | Striscia LED calda (architetturale)|
| `emi_led_strip_cool` | —                  | Striscia LED fredda (task lighting)|
| `emi_bioluminescenza`| —                  | Bagliore bioluminescente           |
| `emi_sole_diretto`   | —                  | Disco solare diretto (intensità molto alta)|

Importa il file e applica il materiale a qualsiasi geometria. Il motore
registra automaticamente la geometria come `GeometryLight` e la include
nel campionamento NEE:

```yaml
imports:
  - path: "libraries/lights/geometry-lights.yaml"

entities:
  # Una sfera lampada a temperatura tungsteno
  - type: "sphere"
    center: [0, 3, 0]
    radius: 0.15
    material: "emi_tungsteno"

  # Un cilindro candela a temperatura candela
  - type: "cylinder"
    center: [1, 0, 0]
    radius: 0.03
    height: 0.2
    material: "emi_candela"
```

Suggerimento: le luci geometriche partecipano automaticamente alla NEE
pesata per potenza. Per scene con molte luci geometriche,
`--light-sampling power` riduce la varianza quando le intensità delle
luci differiscono notevolmente.

### Light hardening: ridurre i firefly senza alzare gli spp

Tutti i preset della libreria sono calibrati con i parametri di *light
hardening* introdotti dal motore (vedi DEVLOG §Ciclo Light Hardening
e `docs/reference/scene-reference.md` §8). Le tre manopole chiave:

- **`soft_radius`** (point, spot, area) — modella il diametro fisico della
  sorgente. Il termine `1/d²` (e `cosLight/d²` per le area) viene chiuso
  sotto a `max(d², r²)`, eliminando i firefly persistenti che compaiono
  quando un raggio (o un evento di scattering nella foschia) atterra
  vicinissimo all'emettitore. Valori tipici: 0.05–0.20 per lampadine,
  0.10–0.30 per neon o riflettori, 0.20 per softbox in foschia.
- **`angular_radius`** (directional) — diametro angolare in gradi del
  disco solare/lunare. `0.27` = sole reale, `0.5` = luna piena. Quando
  attivo produce penombre fisiche (cone-sampling, `shadow_samples` interno
  4) anziché ombre dure infinitamente nette. Le scene outdoor della
  libreria lo usano già su tutti i sole/luna.
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

## 10.4 Libreria delle Texture Immagine

La cartella `scenes/libraries/textures/` contiene 20 file PNG:

### Coppie Albedo + Normal Map
- `brick-wall.png` + `brick-wall-normal.png`
- `brick-wall-white.png` (condivide `brick-wall-normal.png`)
- `concrete.png` + `concrete-normal.png`
- `metal-scratched.png` + `metal-scratched-normal.png`
- `wood-floor.png` + `wood-floor-normal.png`
- `wood-planks.png` + `wood-planks-normal.png`

### Solo Normal Map
- `fabric-weave-normal.png` -- sovrapposizione texture tessuta
- `stone-cobble-normal.png` -- pavimentazione a ciottoli
- `tiles-normal.png` -- linee di fuga delle piastrelle
- `flat-normal.png` -- piatta neutrale (disabilita la mappatura delle normali in modo pulito)

### Specialità
- `earth.png` -- pianeta Terra
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
      path: "libraries/textures/brick-wall.png"
      uv_scale: [2, 2]
    normal_map:
      path: "libraries/textures/brick-wall-normal.png"
      strength: 1.0
      uv_scale: [2, 2]
```

---

## 10.5 Librerie di Font

La directory `scenes/libraries/fonts/` contiene file di template di
caratteri 3D generati dallo strumento `FontGen`. Ogni file copre una
famiglia di font e include template per le lettere maiuscole (A–Z),
le minuscole (a–z) e le cifre (0–9).

File di esempio: `fonts/font-open-sans.yaml` — famiglia Open Sans.

### Utilizzo

Riferisci un template di carattere usando `type: "instance"`:

```yaml
imports:
  - path: "libraries/fonts/font-open-sans.yaml"

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

### Generare librerie di font

Usa lo strumento `FontGen` per creare nuovi file font da qualsiasi font
di sistema o file .ttf/.otf:

```
dotnet run --project src/Tools/FontGen/FontGen.csproj -- \
  --font "Open Sans" --height 0.2 --chars "CIAO"
```

Usa `--list-fonts` per vedere tutti i font di sistema disponibili. I
template generati vengono scritti in `scenes/libraries/fonts/`.

---

## 10.6 Librerie di Terreni

La directory `scenes/libraries/terrains/` contiene file template per
heightfield generati dallo strumento `TerrainGen`. Ogni voce consiste
in un file YAML template e un heightmap PNG in scala di grigi a 16 bit.

Esempio: `terrains/heightfield-strata-test.yaml` +
`terrains/heightfield-strata-test-height.png`.

Il motore interseca l'heightfield direttamente tramite un quadtree
min/max mipmap (senza tessellazione mesh). Una sola primitiva sostituisce
un'intera mesh di terreno.

### Utilizzo come istanza template

```yaml
imports:
  - path: "libraries/terrains/heightfield-strata-test.yaml"

entities:
  - type: "instance"
    template: "terrain_strata_test"
    translate: [0, 0, 0]
    material: "dis_terra_secca"
```

### Utilizzo come dispatcher di terreno

I template di terreno possono anche essere referenziati in `world.ground`
per sostituire il piano di terra infinito implicito:

```yaml
world:
  ground:
    type: "heightfield"
    heightmap: "libraries/terrains/heightfield-strata-test-height.png"
    width: 100
    depth: 100
    max_height: 8.0
    material: "dis_erba_prato"
```

### Generare librerie di terreni

Usa lo strumento `TerrainGen` per creare nuovi template heightfield:

```
dotnet run --project src/Tools/TerrainGen/TerrainGen.csproj -- \
  --name mie-colline --type collina --season estate --with-cameras
```

`--type` accetta `pianura`, `collina` o `montagna`. Lo strumento scrive
il template YAML in `scenes/libraries/terrain/<name>.yaml` e un
heightmap `<name>-height.png` abbinato. Con `--with-cameras` genera
anche un `scenes/<name>-preview.yaml` pronto da renderizzare.

---

## 10.7 Riferimento CLI

L'insieme completo dei parametri della riga di comando:

| Flag | Forma lunga         | Predefinito                   | Descrizione                                                  |
|------|---------------------|-------------------------------|--------------------------------------------------------------|
| `-i` | `--input`           | *(richiesto)*                 | Percorso del file YAML della scena                           |
| `-o` | `--output`          | `renders/render-<scena>.png`  | Percorso dell'immagine di output (PNG, JPG o BMP)            |
| `-w` | `--width`           | `1200`                        | Larghezza dell'immagine in pixel                             |
| `-H` | `--height`          | `800`                         | Altezza dell'immagine in pixel                               |
| `-s` | `--samples`         | `16`                          | Campioni per pixel (Sobol: conteggio esatto; PRNG: arrotondato al quadrato perfetto superiore) |
| `-d` | `--depth`           | `8`                           | Numero massimo di rimbalzi dei raggi (alza a 16+ solo per vetri impilati) |
| `-S` | `--shadow-samples`  | *(per luce)*                  | Sovrascrive i campioni d'ombra per tutte le luci area/sphere (quadrati perfetti) |
| `-C` | `--clamp`           | `100`                         | Firefly clamp: radianza massima per-campione prima del tone mapping |
| `-c` | `--camera`          | `0`                           | Seleziona la fotocamera per nome o indice base zero          |
|      | `--sampler`         | `sobol`                       | Sampler per-pixel: `sobol` (Owen-scrambled) o `prng`         |
|      | `--mis`             | `balance`                     | Heuristica MIS: `balance` o `power`                          |
|      | `--light-sampling`  | `all`                         | Strategia NEE: `all`, `power`, `uniform`                     |
|      | `--indirect-clamp-factor` | `1.0`                   | Clamp più stretto sui rimbalzi indiretti (`0.25` → clamp indiretto = ¼ di `-C`) |
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

Ecco il flusso di lavoro per creare una scena da zero utilizzando le librerie:

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

### Passo 2: Importa le Librerie

```yaml
imports:
  - path: "libraries/materials/metals.yaml"
  - path: "libraries/materials/stones.yaml"
  - path: "libraries/lights/studio-3point.yaml"
```

### Passo 3: Aggiungi le Entità

```yaml
entities:
  # Pavimento
  - type: "infinite_plane"
    point: [0, 0, 0]
    normal: [0, 1, 0]
    material: "dis_carrara_lucido"

  # Una sfera con materiale dalla libreria al centro
  - type: "sphere"
    center: [0, 1.0, 0]
    radius: 0.5
    material: "dis_oro_lucido"

  # Oggetto personalizzato
  - type: "sphere"
    center: [0, 0.78, 0]
    radius: 0.15
    material: "dis_diamante"
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

## 10.9 Guida alla risoluzione dei problemi

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
- Assicurarsi che la libreria sia importata correttamente in `imports:`.
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
- Controllare che il percorso del file della texture sia corretto e relativo al file della scena.
- Verificare che il file esista e sia in un formato supportato (PNG, JPEG, BMP).

### Gli import non funzionano
- I percorsi sono relativi al **file che importa**, non alla directory di lavoro corrente.
- Controllare la presenza di import circolari (il motore avvisa sulla console).
- Assicurarsi che il file importato abbia la struttura YAML corretta (`materials:`, `templates:`, ecc.).

---

## 10.10 Esempio Completo: Sala Espositiva

Una scena che combina diverse librerie in un progetto coeso.

```yaml
# exhibition-hall.yaml
# Una stanza simile a un museo che mostra oggetti di diverse categorie di libreria.

imports:
  - path: "libraries/materials/metals.yaml"
  - path: "libraries/materials/stones.yaml"
  - path: "libraries/lights/geometry-lights.yaml"

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
  # Pavimento personalizzato
  - id: "hall_floor"
    type: "disney"
    roughness: 0.1
    specular: 0.8
    texture:
      type: "checker"
      scale: 0.3
      colors: [[0.85, 0.82, 0.78], [0.25, 0.22, 0.2]]

  # Piedistallo
  - id: "pedestal"
    type: "disney"
    color: [0.2, 0.2, 0.22]
    roughness: 0.08
    specular: 0.7

  # Parete di fondo
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
    material: "dis_oro_lucido"

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
    material: "dis_diamante"

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

- L'ecosistema delle librerie fornisce 1450 materiali, 14 configurazioni di
  illuminazione più preset emissivi `emi_*`, texture immagine, template di
  font e heightfield per terreni.
- I materiali utilizzano i prefissi `dis_` (Disney PBR) e `cls_` (Classic).
- I materiali emissivi `emi_*` da `geometry-lights.yaml` trasformano qualsiasi
  geometria in una sorgente NEE partecipante — senza bisogno di un'entità
  luce esplicita.
- Le librerie font (`fonts/`) forniscono template di caratteri 3D generati
  da FontGen; le librerie terreno (`terrains/`) forniscono template heightfield
  generati da TerrainGen.
- Le librerie vengono caricate tramite `imports:` -- le definizioni locali
  sovrascrivono quelle importate.
- La CLI offre pieno controllo su risoluzione, qualità, selezione della
  fotocamera e formato di output.
- Il flusso di lavoro anteprima/bozza/finale è il modo più efficiente per
  sviluppare le scene.

---

[Precedente: Mezzi partecipanti (Volumetrics)](./09-volumetrics.md) | [Successivo: Superfici di rivoluzione (Lathe)](./11-lathe-surface-of-revolution.md) | [Indice del Tutorial](./README.md)
