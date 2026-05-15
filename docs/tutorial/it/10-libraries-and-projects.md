# Capitolo 10: Librerie di asset e scene complete

3D-Ray viene fornito con un ricco ecosistema di asset predefiniti: oltre 1100 materiali, ~150 template di oggetti, 14 preset di illuminazione e 17 scene starter-kit complete. Questo capitolo mostra come utilizzarli, fornisce il riferimento completo della CLI e guida nella costruzione di un vero progetto.

---

## 10.1 L'ecosistema delle librerie

Tutte le librerie si trovano nella directory `scenes/libraries/`:

```
scenes/libraries/
  materials/      12 file YAML, oltre 1100 materiali
  objects/        11 file YAML, ~150 template
  lights/         14 file YAML, preset di illuminazione
  starter-kits/   17 file YAML, scene complete
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

Undici file a tema con template predefiniti che utilizzano primitive,
gruppi, CSG e la **primitiva `lathe` (superficie di rivoluzione)** per
corpi torniti di livello professionale:

| File                             | Template | Esempi di oggetti                    |
|----------------------------------|----------|--------------------------------------|
| `objects/furniture.yaml`         | 11       | Tavoli, sedie, lampade, scaffali, candelabri torniti (lathe) |
| `objects/decorative-objects.yaml`| 12       | Vasi Ming (lathe), anfore greche (lathe), orologi, piedistalli (lathe) |
| `objects/tableware.yaml`         | 11       | Calici (lathe), bottiglie (lathe), decanter (lathe), teiere |
| `objects/architecture.yaml`      | 15       | Colonne (lathe), archi, scale, balaustri (lathe), pinnacoli (lathe) |
| `objects/mechanical.yaml`        | 14       | Ingranaggi, bulloni, pistoni, cuscinetti |
| `objects/jewelry.yaml`           | 14       | Anelli, collane, gemme, tiara        |
| `objects/lighting.yaml`          | 15       | Lampadari, plafoniere (lathe), paralumi (lathe), lampioni |
| `objects/laboratory.yaml`        | 14       | Beute (lathe), palloni (lathe), imbuti (lathe), microscopio |
| `objects/musical.yaml`           | 14       | Violino, chitarra, campane in bronzo (lathe), timpani (lathe) |
| `objects/outdoor.yaml`           | 15       | Panchine, fontane, fioriere (lathe), vasi giardino (lathe) |
| `objects/nature.yaml`            | 15       | Alberi, fiori, funghi, cristalli     |

### Template basati su lathe

Oltre **26 template** distribuiti su 9 librerie sfruttano la primitiva
`lathe` per i corpi assi-simmetrici: calici da vino, bottiglie, colonne
tornite, balaustri, vasi Ming, vetreria da laboratorio, campane, paralumi. Un singolo profilo Catmull-Rom genera una silhouette
C¹ continua impossibile da ottenere impilando sfere/coni/torus e
sostituisce tipicamente 5–15 primitive con una sola. Per il vetro
trasparente (Pyrex, cristallo) il lathe è mantenuto solido — la
rifrazione del materiale produce naturalmente l'effetto ottico della
parete sottile. Per i gusci sottili opachi (fioriere in terracotta,
paralumi in tessuto) una sottrazione CSG di due lathe genera una
parete di spessore costante. Vedi il
**[Capitolo 11: Superfici di rivoluzione (Lathe)](./11-lathe-surface-of-revolution.md)**
per lo schema completo, i modi di profilo e il costo di intersezione.

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

### ⚠️ `center:` vs `translate:` su primitive con asse

Le primitive che espongono un parametro `center:` (sphere, cylinder, cone,
capsule, torus, disk, annulus, lathe) non vanno combinate con `rotate:` o
`scale:`. Le trasformazioni `scale → rotate → translate` vengono sempre
applicate attorno all'**origine globale**, non attorno al `center:` della
primitiva. Combinandoli si ottiene un riposizionamento inatteso (la
primitiva viene "scagliata" dall'origine).

```yaml
# ❌ Sbagliato — il rotate ruota la sfera attorno all'origine, non al suo centro
- type: "sphere"
  center: [0, 1.5, 0]
  radius: 0.3
  rotate: [0, 0, 90]

# ✅ Corretto — la sfera è in (0,0,0), si scala/ruota localmente, poi posiziona
- type: "sphere"
  radius: 0.3
  rotate: [0, 0, 90]
  translate: [0, 1.5, 0]
```

`box` e `mesh` non hanno parametro `center:` e usano nativamente `translate:`,
quindi sono immuni dal problema. Anche le `instance` di template usano
direttamente `translate:`/`rotate:`/`scale:` ed è il pattern corretto.

**Quando `center:` è sicuro:** quando non sono presenti `rotate:` né
`scale:` (è equivalente a `translate:`); dentro i figli CSG (`left`/`right`),
che non hanno trasformazione esterna; e dentro i `group` quando il figlio
non ha rotazione propria — la `translate`/`rotate` del group si compone
correttamente sopra il `center:` del figlio.

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
  sky:
    type: "flat"
    color: [0.0, 0.0, 0.0]
```

### 🛡️ Light hardening: ridurre i firefly senza alzare gli spp

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

## 10.5 Starter Kit: Scene Complete

Diciotto scene renderizzabili che combinano materiali, oggetti, illuminazione e fotocamere. Si possono usare come punti di partenza -- copiarne uno, rinominarlo e modificarlo.

### Esterni (7)
- `starter-desert-highway.yaml` -- Strada nel deserto con cactus
- `starter-snowy-clearing.yaml` -- Paesaggio invernale con lago ghiacciato
- `starter-zen-garden.yaml` -- Giardino giapponese con lanterna e ponte
- `starter-ancient-ruins.yaml` -- Rovine di un tempio greco
- `starter-floating-islands.yaml` -- Isole fluttuanti fantasy
- `starter-mountain-peak.yaml` ✨ -- Vette innevate al tramonto, mezzo `procedural` per nuvole basse
- `starter-foliage-canopy.yaml` ✨ -- Sottobosco con foglie translucide (`diff_trans` + `thin_walled`) e dappled light

### Interni (8)
- `starter-photography-studio.yaml` -- Ciclorama con illuminazione softbox
- `starter-cornell-box-extended.yaml` -- Benchmark classico GI
- `starter-museum-gallery.yaml` -- Sculture su piedistalli
- `starter-kitchen-counter.yaml` -- Piano in marmo con stoviglie
- `starter-still-life-fruit.yaml` ✨ -- Natura morta fiamminga, bicchiere di vino con `transmission_color/depth`, ceramica satin
- `starter-wine-cellar.yaml` -- Botti e bottiglie a lume di candela
- `starter-dining-room.yaml` -- Tavolo, sedie, lampada a sospensione
- `starter-infinite-mirror-room.yaml` -- Specchi paralleli, sfere emissive

### Showcase (Esposizione) (3)
- `starter-material-showroom.yaml` -- 16 materiali su piedistalli
- `starter-jewelry-closeup.yaml` ✨ -- Anello con diamante (IOR 2.42), smeraldi e opale `thin_film`
- `starter-pool-table.yaml` -- Tavolo da biliardo con palle
- `starter-underwater.yaml` -- Barriera corallina con bioluminescenza

> ✨ I 4 starter kit nuovi (Mountain Peak, Foliage Canopy, Still Life with
> Fruit, Jewelry Close-Up) sono entrati nella collezione per dimostrare
> feature del motore non coperte prima: mezzi partecipanti procedurali e
> di altezza, foglie translucide con il pattern Disney 2015
> (`diff_trans` + `thin_walled`), gemme con IOR alti e iridescenza
> `thin_film`, ceramica satin / vetro smerigliato delle nuove famiglie
> nei materials. Il vecchio `starter-chess-set.yaml` (con `objects/chess.yaml`)
> è stato rimosso in attesa di rifattorizzazione futura; gli starter
> "lean" `starter-golden-hour.yaml` e `starter-sunset.yaml` sono stati
> rimossi perché contenevano solo l'header world+sole — la stessa
> illuminazione è disponibile come libreria di luci importabile in
> `lights/outdoor-golden-hour.yaml` e `lights/outdoor-sunset.yaml`.

### Renderizzare uno Starter Kit

```
# La Cornell box è indirect-dominant: -d è alzato sopra il default del profilo Standard.
RayTracer -i scenes/libraries/starter-kits/starter-cornell-box-extended.yaml -w 800 -H 800 -s 256 -d 20 -S 4
```

La maggior parte degli starter kit definisce più fotocamere. Elencale con:

```
RayTracer -i scenes/libraries/starter-kits/starter-cornell-box-extended.yaml --list-cameras
```

Quindi renderizza una specifica fotocamera:

```
RayTracer -i scenes/libraries/starter-kits/starter-cornell-box-extended.yaml -c "tre_quarti" -s 256 -d 20
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

### Passo 1: Scegli uno Starter Kit (o inizia da zero)

Si sceglie uno starter kit vicino a ciò che si desidera, lo si copia e lo si rinomina. Oppure si crea un nuovo file vuoto.

### Passo 2: Configura World e Camera

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
- I materiali Disney densi (subsurface, sheen) necessitano di più campioni rispetto ai tipi classici.
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
  - path: "libraries/objects/decorative-objects.yaml"

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

- L'ecosistema delle librerie fornisce 1100+ materiali, ~150 template, 14 preset di illuminazione e 17 scene starter-kit.
- I materiali utilizzano i prefissi `dis_` (Disney PBR) e `cls_` (Classic).
- I template degli oggetti seguono convenzioni coerenti (base a Y=0, scala 1:1 metro).
- Le librerie vengono caricate tramite `imports:` -- le definizioni locali sovrascrivono quelle importate.
- La CLI offre pieno controllo su risoluzione, qualità, selezione della fotocamera e formato di output.
- Il flusso di lavoro anteprima/bozza/finale è il modo più efficiente per sviluppare le scene.

---

[Precedente: Mezzi partecipanti (Volumetrics)](./09-volumetrics.md) | [Successivo: Superfici di rivoluzione (Lathe)](./11-lathe-surface-of-revolution.md) | [Indice del Tutorial](./README.md)
