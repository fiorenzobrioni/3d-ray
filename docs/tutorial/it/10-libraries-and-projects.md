# Capitolo 10: Librerie di asset e scene complete

3D-Ray viene fornito con un ricco ecosistema di asset predefiniti: oltre 800 materiali, 154 template di oggetti, 14 preset di illuminazione e 18 scene starter-kit complete. Questo capitolo mostra come utilizzarli, fornisce il riferimento completo della CLI e guida nella costruzione di un vero progetto.

---

## 10.1 L'ecosistema delle librerie

Tutte le librerie si trovano nella directory `scenes/libraries/`:

```
scenes/libraries/
  materials/      12 file YAML, oltre 800 materiali
  objects/        12 file YAML, oltre 154 template
  lights/         14 file YAML, preset di illuminazione
  starter-kits/   18 file YAML, scene complete
  textures/       20 file immagine PNG (albedo + normal map)
```

Le librerie vengono caricate tramite la sezione `imports:` nel file della scena. I percorsi sono relativi alla directory del file della scena.

---

## 10.2 Librerie dei Materiali

Dodici file a tema che coprono ogni tipo di superficie di cui si potrebbe aver bisogno:

| File                       | Contenuti                                       | Quantità |
|----------------------------|-------------------------------------------------|----------|
| `materials/metals.yaml`    | Oro, argento, rame, bronzo, ottone, acciaio, alluminio, titanio, cromo, platino, nichel, zinco, stagno, corten | ~120 |
| `materials/ceramics.yaml`  | Porcellana, maiolica, terracotta, gres, raku, celadon, smaltato | ~67 |
| `materials/woods.yaml`     | Quercia, noce, acero, teak, ebano, mogano (grezzo, oliato, cerato, verniciato, laccato) | ~85 |
| `materials/stones.yaml`    | Marmo, granito, ardesia, travertino, basalto, arenaria, cemento, mattone | ~87 |
| `materials/glasses.yaml`   | Vetro industriale, cristallo, vetro colorato, smerigliato, pietre preziose, liquidi, resine | ~96 |
| `materials/plastics.yaml`  | ABS, policarbonato, acrilico, PVC, nylon, gomma, silicone, stampa 3D | ~95 |
| `materials/fabrics.yaml`   | Velluto, seta, cotone, lino, lana, denim, pelle, pizzo | ~100 |
| `materials/paints.yaml`    | Vernice auto, lacca, smalto, vernice gesso, verniciatura a polvere | ~98 |
| `materials/organics.yaml`  | Cera, ambra, avorio, corno, sughero, carta, sapone, bamboo | ~81 |
| `materials/foods.yaml`     | Cioccolato, frutta, formaggio, pane, caramelle, burro | ~91 |
| `materials/emissives.yaml` | LED, incandescenza, fluorescenza, neon, fiamme, schermi, lava | ~83 |
| `materials/grounds.yaml`   | Pavimenti a scacchi, parquet, piastrelle, pavimenti in marmo, terra, sabbia, erba, moquette | ~66 |

### Convenzione dei nomi

I materiali seguono un sistema di prefissi:

- **`dis_`** -- Disney BSDF (PBR completo con clearcoat, sheen, subsurface, spec_trans). Ideale per gli oggetti principali (hero objects) e i primi piani.
- **`cls_`** -- Tipo classico (lambertian, metal o dielectric). Più veloce e meno rumoroso; ideale per grandi superfici e sfondi.

Esempi:
- `dis_oro_lucido` -- Oro lucido Disney
- `cls_oro_lucido` -- Oro lucido Metal classico
- `dis_vetro_sodalime` -- Vetro soda-lime Disney
- `cls_vetro_sodalime` -- Vetro dielettrico classico

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

Per personalizzare un materiale della libreria, ridefiniscilo con lo stesso ID nel tuo file di scena. Le definizioni locali hanno la precedenza:

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

## 10.3 Librerie degli Oggetti

Dodici file a tema con template predefiniti che utilizzano primitive, gruppi e CSG:

| File                             | Template | Esempi di oggetti                    |
|----------------------------------|----------|--------------------------------------|
| `objects/furniture.yaml`         | 10       | Tavoli, sedie, lampade, scaffali     |
| `objects/decorative-objects.yaml`| 10       | Vasi, cornici, candele, orologi      |
| `objects/tableware.yaml`         | 10       | Piatti, bicchieri, posate, teiere    |
| `objects/architecture.yaml`      | 14       | Colonne (Doriche/Ioniche), archi, scale |
| `objects/mechanical.yaml`        | 14       | Ingranaggi, bulloni, pistoni, cuscinetti |
| `objects/jewelry.yaml`           | 14       | Anelli, collane, gemme, tiara        |
| `objects/lighting.yaml`          | 14       | Lampadari, sospensioni, applique     |
| `objects/laboratory.yaml`        | 14       | Provette, matracci, microscopio      |
| `objects/musical.yaml`           | 14       | Violino, chitarra, pianoforte, tamburi |
| `objects/outdoor.yaml`           | 14       | Panchine, fontane, fioriere          |
| `objects/chess.yaml`             | 11       | Set Staunton completo + scacchiere   |
| `objects/nature.yaml`            | 15       | Alberi, fiori, funghi, cristalli     |

### Prefissi dei materiali per libreria

Ogni libreria utilizza un prefisso unico per i suoi materiali incorporati per evitare collisioni:

| Libreria            | Prefisso|
|---------------------|---------|
| furniture           | `frn_`  |
| decorative-objects  | `dec_`  |
| tableware           | `tbw_`  |
| architecture        | `arc_`  |
| mechanical          | `mec_`  |
| jewelry             | `jwl_`  |
| lighting            | `lit_`  |
| laboratory          | `lab_`  |
| musical             | `mus_`  |
| outdoor             | `out_`  |
| chess               | `chs_`  |
| nature              | `nat_`  |

### Convenzioni

Tutti i template seguono regole coerenti:

- **Base a Y=0.** Ogni template poggia a terra quando posizionato con `translate: [x, 0, z]`.
- **Centrato in XZ.** L'origine è nel centro geometrico.
- **Scala 1:1 in metri.** Un tavolo è largo ~1.4 m; una sedia è alta ~0.9 m.

### Utilizzo

```yaml
imports:
  - path: "libraries/objects/furniture.yaml"
  - path: "libraries/materials/metals.yaml"

entities:
  # Posiziona un tavolo all'origine
  - type: "instance"
    template: "tavolo_classico"
    translate: [0, 0, 0]

  # Posiziona una sedia, sovrascrivi il materiale con oro metallico
  - type: "instance"
    template: "sedia_classica"
    translate: [0.7, 0, -0.4]
    rotate: [0, -30, 0]
    material: "dis_oro_lucido"
```

La sovrascrittura del materiale su un'istanza sostituisce il materiale predefinito del template. I figli con un proprio materiale esplicito (come una lampadina emissiva all'interno di una lampada) mantengono il loro materiale originale.

---

## 10.4 Librerie di Illuminazione

Quattordici configurazioni di illuminazione predefinite organizzate per ambiente:

### Studio

| File                         | Configurazione       | Atmosfera                      |
|------------------------------|----------------------|--------------------------------|
| `lights/studio-3point.yaml`  | 3-punti classico     | Universale prodotto/ritratto   |
| `lights/studio-highkey.yaml` | High key             | Pulito, commerciale, moda      |
| `lights/studio-dramatic.yaml`| Low key/Chiaroscuro  | Noir, ombre drammatiche        |
| `lights/studio-product.yaml` | Prodotto/Gioielleria | Riflessi (catchlights) precisi |

### Esterni (Outdoor)

| File                             | Configurazione | Atmosfera                           |
|----------------------------------|----------------|-------------------------------------|
| `lights/outdoor-noon.yaml`       | Sole mezzogiorno| Luce dura, ombre corte             |
| `lights/outdoor-golden-hour.yaml`| Ora d'oro       | Bagliore cinematografico caldo      |
| `lights/outdoor-sunset.yaml`     | Tramonto       | Arancione profondo, ombre lunghe    |
| `lights/outdoor-overcast.yaml`   | Nuvoloso       | Morbida, uniforme, senza ombre nette|

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
  ambient_light: [0.02, 0.02, 0.03]
  background: [0.0, 0.0, 0.0]
```

---

## 10.5 Starter Kit: Scene Complete

Diciotto scene renderizzabili che combinano materiali, oggetti, illuminazione e fotocamere. Si possono usare come punti di partenza -- copiarne uno, rinominarlo e modificarlo.

### Esterni (7)
- `starter-desert-highway.yaml` -- Strada nel deserto con cactus
- `starter-snowy-clearing.yaml` -- Paesaggio invernale con lago ghiacciato
- `starter-zen-garden.yaml` -- Giardino giapponese con lanterna e ponte
- `starter-ancient-ruins.yaml` -- Rovine di un tempio greco
- `starter-floating-islands.yaml` -- Isole fluttuanti fantasy
- `starter-golden-hour.yaml` -- Paesaggio al tramonto
- `starter-sunset.yaml` -- Orizzonte drammatico

### Interni (7)
- `starter-photography-studio.yaml` -- Ciclorama con illuminazione softbox
- `starter-cornell-box-extended.yaml` -- Benchmark classico GI
- `starter-museum-gallery.yaml` -- Sculture su piedistalli
- `starter-kitchen-counter.yaml` -- Piano in marmo con stoviglie
- `starter-wine-cellar.yaml` -- Botti e bottiglie a lume di candela
- `starter-dining-room.yaml` -- Tavolo, sedie, lampada a sospensione
- `starter-infinite-mirror-room.yaml` -- Specchi paralleli, sfere emissive

### Showcase (Esposizione) (4)
- `starter-material-showroom.yaml` -- 16 materiali su piedistalli
- `starter-chess-set.yaml` -- Set Staunton completo
- `starter-pool-table.yaml` -- Tavolo da biliardo con palle
- `starter-underwater.yaml` -- Barriera corallina con bioluminescenza

### Renderizzare uno Starter Kit

```
RayTracer -i scenes/libraries/starter-kits/starter-cornell-box-extended.yaml -w 800 -H 800 -s 64 -d 30
```

La maggior parte degli starter kit definisce più fotocamere. Elencale con:

```
RayTracer -i scenes/libraries/starter-kits/starter-cornell-box-extended.yaml --list-cameras
```

Quindi renderizza una specifica fotocamera:

```
RayTracer -i scenes/libraries/starter-kits/starter-cornell-box-extended.yaml -c "tre_quarti" -s 128
```

---

## 10.6 Libreria delle Texture Immagine

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

## 10.7 Riferimento CLI

L'insieme completo dei parametri della riga di comando:

| Flag | Forma lunga         | Predefinito                   | Descrizione                                                  |
|------|---------------------|-------------------------------|--------------------------------------------------------------|
| `-i` | `--input`           | *(richiesto)*                 | Percorso del file YAML della scena                           |
| `-o` | `--output`          | `renders/render-<scena>.png`  | Percorso dell'immagine di output (PNG, JPG o BMP)            |
| `-w` | `--width`           | `1200`                        | Larghezza dell'immagine in pixel                             |
| `-H` | `--height`          | `800`                         | Altezza dell'immagine in pixel                               |
| `-s` | `--samples`         | `16`                          | Campioni per pixel (arrotondati al quadrato perfetto)        |
| `-d` | `--depth`           | `8`                           | Numero massimo di rimbalzi dei raggi (alza a 16+ solo per vetri impilati) |
| `-S` | `--shadow-samples`  | *(per luce)*                  | Sovrascrive i campioni d'ombra per tutte le luci area/sphere (quadrati perfetti) |
| `-C` | `--clamp`           | `100`                         | Firefly clamp: radianza massima per-campione prima del tone mapping |
| `-c` | `--camera`          | `0`                           | Seleziona la fotocamera per nome o indice base zero          |
|      | `--list-cameras`    |                               | Elenca le fotocamere disponibili ed esce                     |
| `-h` | `--help`            |                               | Mostra l'aiuto                                               |

### Formato di output

Il formato è determinato dall'estensione del file:
- `.png` -- PNG (predefinito, senza perdita di qualità)
- `.jpg` / `.jpeg` -- JPEG (con perdita di qualità, file più piccoli)
- `.bmp` -- BMP (non compresso)

### Arrotondamento dei campioni

Il numero di campioni viene sempre arrotondato per eccesso al quadrato perfetto più vicino:

| Richiesti | Effettivi | Griglia  |
|-----------|-----------|----------|
| 1         | 1         | 1x1      |
| 10        | 16        | 4x4      |
| 20        | 25        | 5x5      |
| 50        | 64        | 8x8      |
| 100       | 100       | 10x10    |
| 200       | 225       | 15x15    |
| 256       | 256       | 16x16    |

---

## 10.8 Costruire un progetto completo: Passo dopo passo

Ecco il flusso di lavoro per creare una scena da zero utilizzando le librerie:

### Passo 1: Scegli uno Starter Kit (o inizia da zero)

Si sceglie uno starter kit vicino a ciò che si desidera, lo si copia e lo si rinomina. Oppure si crea un nuovo file vuoto.

### Passo 2: Configura World e Camera

```yaml
world:
  ambient_light: [0.02, 0.02, 0.03]
  background: [0.0, 0.0, 0.0]

cameras:
  - name: "main"
    position: [0, 2, -6]
    look_at: [0, 1, 0]
    fov: 45
```

### Passo 3: Importa le Librerie

```yaml
imports:
  - path: "libraries/materials/metals.yaml"
  - path: "libraries/materials/stones.yaml"
  - path: "libraries/objects/furniture.yaml"
  - path: "libraries/objects/decorative-objects.yaml"
  - path: "libraries/lights/studio-3point.yaml"
```

### Passo 4: Aggiungi le Entità

```yaml
entities:
  # Pavimento
  - type: "infinite_plane"
    point: [0, 0, 0]
    normal: [0, 1, 0]
    material: "dis_carrara_lucido"

  # Mobili dalla libreria
  - type: "instance"
    template: "tavolo_classico"
    translate: [0, 0, 0]

  # Oggetto personalizzato
  - type: "sphere"
    center: [0, 0.78 , 0]
    radius: 0.15
    material: "dis_oro_lucido"
```

### Passo 5: Itera

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
- **Fotocamera all'interno di un oggetto.** Spostare la `position` della fotocamera fuori da ogni geometria.
- **Fotocamera rivolta nella direzione sbagliata.** Controllare il punto `look_at`.

### Troppo rumore
- Aumentare i campioni: `-s 64` o `-s 256`.
- Aumentare i campioni d'ombra: `-S 16`.
- I materiali Disney densi (subsurface, sheen) necessitano di più campioni rispetto ai tipi classici.

### Rendering molto lento
- Ridurre la risoluzione e i campioni durante i test.
- Usare il flusso di lavoro anteprima/bozza/finale.
- Sostituire i materiali Disney con equivalenti classici per le superfici di sfondo.

### Materiale mancante (l'oggetto appare grigio predefinito)
- Controllare eventuali errori di battitura nell'ID del materiale.
- Assicurarsi che la libreria sia importata correttamente in `imports:`.
- Controllare i messaggi di avviso sulla console per riferimenti a materiali non risolti.

### Colori sbagliati
- I colori sono `[R, G, B]` nell'intervallo **0.0--1.0**, non 0--255. `[255, 0, 0]` non è rosso -- è un bianco estremamente luminoso.

### Il vetro appare strano (troppo scuro o solido)
- Aumentare la profondità dei raggi: `-d 30` o superiore. Il vetro necessita di 2 rimbalzi per superficie.
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
  - path: "libraries/objects/decorative-objects.yaml"

world:
  ambient_light: [0.015, 0.015, 0.02]
  background: [0.0, 0.0, 0.0]

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

  # Piedistallo centrale: vaso decorativo dalla libreria
  - type: "cylinder"
    center: [0, 0, 0]
    radius: 0.35
    height: 0.9
    material: "pedestal"

  - type: "instance"
    template: "vaso_decorativo"
    translate: [0, 0.9, 0]

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
  # Luci spot individuali per ogni piedistallo
  - type: "spot"
    position: [-2, 4, -1]
    direction: [0, -1, 0.2]
    color: [1, 0.97, 0.92]
    intensity: 60.0
    inner_angle: 10
    outer_angle: 22

  - type: "spot"
    position: [0, 4, -1]
    direction: [0, -1, 0.2]
    color: [1, 0.97, 0.92]
    intensity: 60.0
    inner_angle: 10
    outer_angle: 22

  - type: "spot"
    position: [2, 4, -1]
    direction: [0, -1, 0.2]
    color: [1, 0.97, 0.92]
    intensity: 60.0
    inner_angle: 10
    outer_angle: 22

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
RayTracer -i exhibition-hall.yaml -c overview -w 1920 -H 1080 -s 128 -d 30
RayTracer -i exhibition-hall.yaml -c detail -w 1200 -H 800 -s 256 -d 30
```

---

## Cosa si è imparato

- L'ecosistema delle librerie fornisce 800+ materiali, 154+ template, 14 preset di illuminazione e 18 scene starter-kit.
- I materiali utilizzano i prefissi `dis_` (Disney PBR) e `cls_` (Classic).
- I template degli oggetti seguono convenzioni coerenti (base a Y=0, scala 1:1 metro).
- Le librerie vengono caricate tramite `imports:` -- le definizioni locali sovrascrivono quelle importate.
- La CLI offre pieno controllo su risoluzione, qualità, selezione della fotocamera e formato di output.
- Il flusso di lavoro anteprima/bozza/finale è il modo più efficiente per sviluppare le scene.

---

[Precedente: Mezzi partecipanti (Volumetrics)](./09-volumetrics.md) | [Indice del Tutorial](./README.md)
