# Capitolo 6: Padronanza dell'illuminazione

L'illuminazione definisce l'atmosfera, la profondità e il realismo di una scena. 3D-Ray supporta sei tipi di luce espliciti, oltre alle luci geometriche estratte automaticamente dalle superfici emissive. Questo capitolo le analizza tutte e mostra come combinarle in configurazioni professionali.

---

## 6.1 Panoramica

| Tipo          | Alias                                        | Ombre    | Utilizzo principale       |
|---------------|----------------------------------------------|----------|---------------------------|
| `point`       | --                                           | Nette    | Lampade, candele, riempimento |
| `directional` | `sun`                                        | Nette    | Luce solare, sorgenti lontane |
| `spot`        | `spotlight`                                  | Nette    | Luci da palco, torce      |
| `area`        | `area_light`, `rect`, `rect_light`           | Morbide  | Softbox, finestre         |
| `sphere`      | `sphere_light`, `ball`, `ball_light`         | Morbide  | Lampadine, lanterne       |
| *(geometry)*  | *(automatico da entità emissive)*            | Morbide  | Insegne al neon, pannelli luminosi |

Tutte le luci sono definite nella sezione `lights:`. Se ometti completamente la sezione `lights:`, il motore aggiunge delle luci predefinite (una direzionale e una puntiforme). Se includi una sezione `lights: []` vuota, la scena non avrà luci esplicite -- utile quando ci si affida interamente a oggetti emissivi o all'illuminazione ambientale HDRI.

### Da dove viene la luce ambientale

L'illuminazione ambientale nasce esclusivamente dalla GI path-traced.

Se vuoi un fill light morbido, hai tre opzioni fisicamente corrette, tutte configurabili sotto `world: > sky:` (vedi Capitolo 7):

- **Cielo flat**: `sky.type: flat` con un `color` basso (es. `[0.02, 0.02, 0.025]`). Emette uniformemente in ogni direzione e partecipa a NEE tramite uniform sphere sampling, fornendo illuminazione ambientale uniforme da ogni direzione.
- **Cielo gradient**: `sky.type: gradient` con valori bassi di zenith/horizon (e opzionalmente un disco solare). Il corpo del gradiente fornisce il fill ambient via path-traced bounces; il disco solare fornisce illuminazione direzionale netta.
- **Cielo HDRI**: `sky.type: hdri` per l'illuminazione image-based completa. La CDF importance-sampled garantisce convergenza efficiente anche per environment molto luminosi.

Le scene d'interni di solito stanno meglio con un cielo flat basso (oppure un cielo nero più pannelli emissivi e area light). Le scene all'aperto usano gradient o HDRI come sorgente luminosa dominante.

---

## 6.2 Point Light (Luce Puntiforme)

```yaml
- type: "point"
  position: [2, 3, -1]
  color: [1.0, 0.95, 0.85]
  intensity: 25.0
```

| Parametro     | Predefinito | Descrizione                                                        |
|---------------|-------------|--------------------------------------------------------------------|
| `position`    | --          | Posizione nello spazio world                                       |
| `color`       | --          | Colore della luce `[R, G, B]`                                      |
| `intensity`   | --          | Moltiplicatore di luminosità                                       |
| `soft_radius` | `0`         | Opzionale. >0 clampa d² a r² per eliminare i fireflies in nebbia    |

Una luce puntiforme irradia uniformemente in tutte le direzioni da un singolo punto. La sua luminosità diminuisce con l'**inverso del quadrato** della distanza: raddoppiando la distanza, l'intensità diventa un quarto.

Poiché le luci puntiformi sono infinitamente piccole, producono **ombre nette** (hard shadows) con bordi precisi. Questo appare poco realistico per sorgenti luminose grandi e vicine, ma va bene per luci distanti o piccoli accenti.

**Guida all'intensità:** A causa del decadimento con l'inverso del quadrato, le luci puntiformi richiedono valori di intensità più alti di quanto ci si potrebbe aspettare. Per un oggetto a 2 unità di distanza, un'intensità di 15--30 è tipica. Per 5 unità, provare 50--100.

**`soft_radius` per scene con nebbia/medium:** quando è attivo un medium partecipante, gli eventi di scattering possono cadere arbitrariamente vicino a un emettitore puntiforme e il termine 1/d² esplode in pixel-firefly. Impostando `soft_radius` a un valore positivo (es. il raggio fisico del bulbo, `0.05`–`0.20`) il denominatore dell'attenuazione viene clampato a `max(d², r²)` e la singolarità sparisce, senza alterare il look a `d ≥ r`. Default `0` = nessun clamp (comportamento originale).

---

## 6.3 Directional Light (Luce Direzionale)

```yaml
- type: "directional"
  direction: [-0.5, -1, 0.3]
  color: [1, 0.98, 0.92]
  intensity: 3.0
  angular_radius: 0.0            # Opzionale. 0.27 = disco solare reale (ombre morbide)
```

| Parametro        | Predefinito | Descrizione                                     |
|------------------|-------------|-------------------------------------------------|
| `direction`      | --          | Direzione *verso* la scena (dalla luce)         |
| `color`          | --          | Colore della luce                               |
| `intensity`      | --          | Moltiplicatore di luminosità                    |
| `angular_radius` | `0`         | Opzionale. Raggio angolare del disco (gradi). 0 = ombre nette |

Una luce direzionale invia raggi paralleli -- tutti viaggiano nella stessa direzione. Non ha posizione (pensa ad essa come se fosse infinitamente lontana) e non ha decadimento con la distanza. È il modo standard per simulare la luce solare.

Il vettore `direction` punta **dalla luce verso la scena**, non dalla scena verso la luce. `[-0.5, -1, 0.3]` significa che la luce proviene da in alto a sinistra, leggermente dietro la fotocamera.

Come le luci puntiformi, le luci direzionali producono **ombre nette** per default.

**Disco solare (`angular_radius`):** quando > 0, il renderer perturba ogni raggio d'ombra all'interno di un cono dell'ampiezza specificata, producendo una penombra morbida realistica. Il valore `shadow_samples` viene portato automaticamente a 4 quando il disco è attivo. Il Sole reale sottende circa 0.27°.

```yaml
- type: "sun"
  direction: [-0.5, -1, 0.3]
  color: [1.0, 0.95, 0.80]
  intensity: 2.0
  angular_radius: 0.27    # Disco solare reale — penombra morbida
```

Disponibile anche come `type: "sun"`.

---

## 6.4 Spot Light (Luce Spot)

```yaml
- type: "spot"
  position: [0, 4, 0]
  direction: [0, -1, 0]
  color: [1, 1, 1]
  intensity: 50.0
  inner_angle: 15
  outer_angle: 30
  shadow_samples: 1              # >1 + soft_radius > 0 → sorgente jitterata
```

| Parametro        | Predefinito | Descrizione                                          |
|------------------|-------------|------------------------------------------------------|
| `position`       | --          | Posizione nello spazio world                         |
| `direction`      | --          | Direzione verso cui è puntato lo spot                |
| `color`          | --          | Colore della luce                                    |
| `intensity`      | --          | Moltiplicatore di luminosità                         |
| `inner_angle`    | `15`        | Semiampiezza del cono a piena intensità (gradi)      |
| `outer_angle`    | `30`        | Semiampiezza del cono a intensità zero (gradi)       |
| `soft_radius`    | `0`         | Opzionale. Stesso ruolo della point light — fortemente raccomandato per spot dentro un medium/nebbia |
| `shadow_samples` | `1`         | >1 + `soft_radius > 0` → sorgente jitterata per penombra morbida in nebbia |

Una luce spot emette da un punto all'interno di un cono. All'interno del cono interno (inner cone) la luce è alla massima intensità. Tra il cono interno e quello esterno (outer cone) sfuma gradualmente fino a zero (decadimento cosinusoidale). All'esterno del cono esterno non c'è luce.

Le luci spot sono ideali per effetti teatrali, esposizioni museali e torce. Producono anch'esse ombre nette.

**Multi-sample spot + soft radius:** con `shadow_samples: 4` e `soft_radius: 0.15` il motore campiona un disco di raggio 0.15 m per ogni raggio d'ombra, creando una penombra morbida ed eliminando i fireflies 1/d² in nebbia. Se `soft_radius == 0`, campioni aggiuntivi non hanno effetto — tenere a 1 per efficienza.

Disponibile anche come `type: "spotlight"`.

---

## 6.5 Area Light: Ombre Morbide

```yaml
- type: "area"
  corner: [-1, 3, -1]
  u: [2, 0, 0]
  v: [0, 0, 2]
  color: [1, 0.97, 0.93]
  intensity: 35.0
```

| Parametro           | Predefinito | Descrizione                                          |
|---------------------|-------------|------------------------------------------------------|
| `corner`            | --          | Un angolo del rettangolo                             |
| `u`                 | --          | Primo vettore lato (dall'angolo)                     |
| `v`                 | --          | Secondo vettore lato (dall'angolo)                   |
| `color`             | --          | Colore della luce                                    |
| `intensity`         | --          | Moltiplicatore di luminosità                         |
| `shadow_samples`    | `4`         | Numero di campioni d'ombra (più alto = più morbida)  |
| `soft_radius`       | `0`         | Opzionale. Clampa `distSq` nel termine cosLight/d²   |
| `visible_to_camera` | `true`      | Se `false`, nasconde il pannello solo dai raggi primari — NEE, riflessioni speculari e indirect restano invariati. Vedi Sezione 6.8. |

Una luce area è un rettangolo piatto che emette luce da tutta la sua superficie. Poiché ha una dimensione fisica, produce **ombre morbide** con una penombra realistica (la transizione graduale dall'ombra alla luce).

Il rettangolo è definito da `corner` e due vettori lato `u` e `v`, proprio come un quad. I quattro vertici sono `corner`, `corner+u`, `corner+u+v` e `corner+v`.

**`soft_radius` per scene con nebbia/medium:** in media partecipanti densi, un campione stratificato sull'area light può cadere quasi tangente al ricevitore, rendendo il termine `cosLight / d²` illimitato. Impostare `soft_radius` a un valore piccolo (es. `0.5`–`2.0`) clampa il denominatore a `max(distSq, r²)`, eliminando questi rari spike. La distanza geometrica non è alterata — solo il denominatore dell'attenuazione.

### Campioni d'ombra (Shadow Samples)

Il parametro `shadow_samples` controlla quanti punti casuali sul rettangolo vengono testati per ogni calcolo d'ombra. Più campioni producono ombre più morbide ma richiedono più tempo di rendering.

| shadow_samples | Qualità           | Velocità  |
|----------------|-------------------|-----------|
| 1              | Ombra netta (noisy)| Massima   |
| 4              | Visibilmente soft | Alta      |
| 9--16          | Penombra fluida   | Moderata  |
| 25--64         | Molto fluida      | Bassa     |

Il flag CLI `-S` sovrascrive `shadow_samples` per **tutte** le luci area e sphere nella scena, il che è utile durante i render di prova:

```
RayTracer -i scena.yaml -s 16 -S 4      # Prova veloce con bassa qualità d'ombra
RayTracer -i scena.yaml -s 256 -S 16    # Rendering finale con ombre morbide
```

### Nota: Le luci Area sono visibili (proxy gestito internamente)

Le luci area sono visibili alla camera e ai raggi specular: il loader
costruisce un quad emissivo proxy alla stessa `corner`/`u`/`v` e lo
collega all'area light, così i sample BSDF che colpiscono il rettangolo
contribuiscono la stessa radianza che NEE assegnerebbe — chiudendo lo
stimatore MIS di Veach sui materiali specular smooth, garantendo
corretto highlight speculare sulle superfici riflettenti.

Puoi comunque aggiungere un quad emissivo separato se vuoi un pannello
di forma personalizzata (vedi Sezione 6.7); si somma al proxy interno
senza conflitti.

---

## 6.6 Sphere Light (Luce Sferica)

```yaml
- type: "sphere"
  position: [0, 2, 0]
  radius: 0.3
  color: [1, 0.95, 0.85]
  intensity: 30.0
  shadow_samples: 12
```

| Parametro           | Predefinito | Descrizione                             |
|---------------------|-------------|-----------------------------------------|
| `position`          | --          | Centro della sfera                      |
| `radius`            | --          | Raggio della sfera luminosa; definisce anche la dimensione del proxy |
| `color`             | --          | Colore della luce                       |
| `intensity`         | --          | Moltiplicatore di luminosità            |
| `shadow_samples`    | `4`         | Numero di campioni d'ombra              |
| `visible_to_camera` | `true`      | Se `false`, nasconde la sfera proxy solo dai raggi primari — NEE, riflessioni speculari e indirect restano invariati. Vedi Sezione 6.8. |

Una luce sferica è come una luce area, ma di forma sferica. Produce ombre morbide con una penombra circolare e crea riflessi perfettamente rotondi (catchlights) nelle superfici riflettenti.

Le luci sferiche utilizzano il **campionamento dell'angolo solido**, che è da 2 a 10 volte più efficiente rispetto a una sfera emissiva equivalente per luci piccole o distanti. Preferisci le luci sferiche alle sfere emissive quando la sorgente luminosa è l'illuminazione principale della scena.

Le luci sferiche sono **visibili** alla camera e ai raggi specular: una sfera emissiva proxy gestita internamente, alla stessa posizione/raggio, supporta la luce analitica e chiude lo stimatore MIS di Veach. I vetri lisci e i metalli lucidati nella scena riflettono ora la luce correttamente (invece di mostrare un buco scuro nella direzione di mirror), che è il comportamento atteso per una sphere light analitica.

Le luci sferiche ignorano deliberatamente `soft_radius`: lo stimatore ad angolo solido `L = Intensity × Ω / N` è limitato superiormente da `4π · Intensity` anche quando il ricevitore è dentro la sfera, quindi il floor 1/d² usato dalle point/spot/area è qui inutile.

---

## 6.7 Luci Geometriche (Oggetti Emissivi)

Qualsiasi entità con un materiale `emissive` viene registrata automaticamente come luce geometrica. Il motore campiona la sua superficie durante l'illuminazione diretta (Next Event Estimation), proprio come le sorgenti luminose esplicite.

```yaml
materials:
  - id: "panel_glow"
    type: "emissive"
    color: [1, 0.95, 0.9]
    intensity: 25.0

entities:
  # Un pannello luminoso visibile sul soffitto
  - name: "ceiling_light"
    type: "quad"
    q: [-0.5, 2.99, -0.5]
    u: [1, 0, 0]
    v: [0, 0, 1]
    material: "panel_glow"
```

### Luci Geometriche vs Luci Esplicite

| Caratteristica                | Luce Geometrica      | Luce Esplicita (area/sphere) |
|-------------------------------|----------------------|------------------------------|
| Visibile in camera            | Sì (toggle sull'entity) | Sì (toggle sulla luce)    |
| Visibile nei riflessi         | Sì                   | Sì (tramite proxy interno)   |
| Illuminazione diretta (NEE)   | Sì (automatico)      | Sì                           |
| Ombre morbide                 | Sì                   | Sì                           |
| Efficienza di campionamento   | Buona                | Leggermente migliore (analitica) |

Entrambe le tipologie di luce supportano un flag `visible_to_camera` (default
`true`) — vedi la prossima sezione per il toggle di camera-visibility.

Usare le luci geometriche quando la sorgente deve essere un emettitore di **forma personalizzata** (insegne al neon, flussi di lava, tubi luminosi, mesh irregolari). Usare le luci `area`/`sphere` esplicite per emettitori canonici rettangolari/sferici — campionano in modo più efficiente e si vedono comunque in camera e nei riflessi specular.

Il motore supporta le luci geometriche su qualsiasi primitiva campionabile: sfere, quad, dischi, box, cilindri, coni, tori, capsule, annuli e mesh.

### Multiple Importance Sampling — perché vale per tutti i materiali

L'illuminazione diretta su una luce non delta (area, sphere, geometric, environment) è calcolata combinando due strategie indipendenti: la **NEE** campiona un punto sulla luce, il **BSDF sampling** campiona la direzione di rimbalzo dal materiale. Pesare i due contributi con la **balance heuristic** (default) o la **power heuristic** (`--mis power`) riduce la varianza rispetto a usare una sola strategia.

Tutti i materiali supportati — `lambertian`, `metal`, `mix`, `disney` — espongono la tripla `Sample`/`Pdf`/`Evaluate` necessaria al MIS. Non c'è nessuna configurazione: il motore applica automaticamente i pesi corretti in base al tipo di materiale e di luce. Le luci delta puro (point, directional, spot) e i lobi delta dei materiali (specchio perfetto, vetro ideale) sono trattati come casi speciali e ricevono peso 1 — non possono essere campionati dall'altra strategia.

Per scene con nebbia o fumo (`global_medium`), anche la phase function partecipa al MIS: il motore pesa l'in-scattering NEE contro il phase-sampled bounce, riducendo i fireflies tipici dei "shaft" di luce attraverso il volume.

---

## 6.8 Visibilità dalla camera (`visible_to_camera`)

I renderer di produzione permettono di disaccoppiare **come una luce
contribuisce all'immagine** da **se la luce è essa stessa visibile nel
frame**. 3D-Ray espone questo controllo con la chiave underscore_case
`visible_to_camera`.

Quando impostato a `false`:

- La luce continua a illuminare la scena a piena intensità tramite NEE
  (illuminazione diretta).
- La luce appare comunque nelle **riflessioni a specchio, nelle
  rifrazioni del vetro e nei rimbalzi indiretti**.
- Il proxy della luce (o la geometria dell'entity) è **invisibile solo
  ai raggi primari della camera**, che il renderer rileva tramite
  `depth == maxDepth`.

### Quando usarlo

| Caso d'uso | Setup |
|------------|-------|
| Luce fill off-frame che non deve apparire come una forma luminosa nel cielo | `visible_to_camera: false` su una luce `sphere`/`area` posizionata fuori dall'inquadratura |
| Lampada pratica visibile solo in uno specchio nella stanza | `visible_to_camera: false` sull'entity emissiva |
| Pannello area soft per product photography — cielo pulito, pannello visibile solo via riflessione sul prodotto | `visible_to_camera: false` sulla luce `area` |
| Light card appena fuori dal FOV che altrimenti taglierebbe il bordo del frame | idem |

### Su luci esplicite

```yaml
lights:
  # KEY: visibile ovunque (default)
  - type: "sphere"
    position: [ 3.5, 3.8, 1.5]
    radius: 0.35
    color: [1.0, 0.96, 0.88]
    intensity: 45.0

  # FILL: invisibile alla camera, ma riflessa in specchi/vetri e illumina la scena
  - type: "sphere"
    position: [-3.5, 3.8, 1.5]
    radius: 0.35
    color: [0.65, 0.78, 1.0]
    intensity: 45.0
    visible_to_camera: false
```

### Su entity emissive

`visible_to_camera` è anche un campo comune a tutte le entity, quindi
qualsiasi entity — non solo i proxy delle luci esplicite — può essere
nascosta dai raggi primari. L'uso naturale è un pannello emissivo di
forma personalizzata che deve illuminare la scena senza apparire nel
frame:

```yaml
materials:
  - id: "panel_glow"
    type: "emissive"
    color: [1.0, 0.92, 0.80]
    intensity: 2.5

entities:
  - name: "ceiling_panel"
    type: "box"
    material: "panel_glow"
    scale: [4.0, 0.05, 2.5]
    translate: [0, 4.0, 0]
    visible_to_camera: false       # soffitto pulito, ma la stanza è illuminata
```

Su un `group` il flag propaga a tutti i figli (il wrapper è applicato
fuori dalla BVH interna del gruppo); un figlio può portarsi anche un
proprio flag, che si compone in OR (parent OR figlio invisibile ⇒
invisibile).

### Limiti

- `visible_to_camera` non ha effetto osservabile su luci `point`/
  `directional`/`spot` (delta) — non hanno una geometria proxy da
  nascondere.
- Un raggio camera che colpisce un proxy invisibile viene semplicemente
  fatto avanzare oltre (con un cap di sicurezza di 8 skip successivi),
  quindi uno stack illimitato di emettitori invisibili sovrapposti
  davanti alla camera satura il cap. Caso non realistico ma utile da
  sapere.

### Esempio pratico: `camera-visibility.yaml`

La scena `scenes/showcases/camera-visibility.yaml` raccoglie
tutte le idee di sopra: una sphere light KEY calda visibile in cielo e
nelle riflessioni di due sfere chrome; una sphere light FILL fredda
nascosta dalla camera ma chiaramente visibile in quelle stesse
riflessioni; un pannello emissivo a soffitto nascosto dalla vista ma che
continua a illuminare il pavimento via NEE. Render preview con

```
RayTracer -i scenes/showcases/camera-visibility.yaml \
          -o renders/vtc.png -w 480 -H 270 -s 64 -d 6
```

e confronta le riflessioni nelle chrome — quello è il test che il flag
sta facendo la cosa giusta.

---

## 6.9 Lo Schema di Illuminazione a Tre Punti

La tecnica di illuminazione più utile in fotografia e grafica 3D è lo schema a tre punti:

```
                    ┌──────────┐
                    │ KEY LIGHT│   (grande area, in alto a sinistra)
                    └──────────┘
                          ↓
        ┌───────────┐ [SOGGETTO] ┌──────────┐
        │ FILL LIGHT│            │ RIM LIGHT│  (dietro al soggetto)
        └───────────┘            └──────────┘
```

### Key Light (Luce Chiave)

La sorgente luminosa principale. Definisce la direzione dell'ombra dominante e modella la forma del soggetto. Tipicamente una grande luce area posizionata in alto e di lato.

```yaml
- type: "area"
  corner: [-4, 4, -2]
  u: [2, 0, 0]
  v: [0, 0, 2]
  color: [1.0, 0.96, 0.90]
  intensity: 45.0
```

### Fill Light (Luce di Riempimento)

Una luce più morbida e debole posizionata sul lato opposto rispetto alla chiave. Il suo compito è schiarire le ombre senza creare ombre dominanti proprie. Tipicamente ha 1/3 dell'intensità della chiave ed è di un colore leggermente più freddo.

```yaml
- type: "area"
  corner: [3, 3, -1.5]
  u: [1.2, 0, 0]
  v: [0, 0, 1.2]
  color: [0.72, 0.82, 1.0]
  intensity: 15.0
  shadow_samples: 9
```

### Rim Light (Luce di Controluce/Contorno)

Una luce posizionata dietro e sopra il soggetto. Crea un bordo luminoso lungo il contorno del soggetto, separandolo dallo sfondo e aggiungendo un senso di profondità.

```yaml
- type: "point"
  position: [1, 4.5, 4.5]
  color: [1.0, 0.97, 0.88]
  intensity: 55.0
```

Il contrasto caldo/freddo (chiave calda, riempimento freddo, contorno caldo) è una formula classica che funziona per quasi ogni soggetto.

---

## 6.10 Ricette di Illuminazione

### Fotografia Still Life da Studio

Configurazione high-key: luci area grandi e luminose da più angolazioni. Ombre minime. Stile pulito e commerciale.

```yaml
lights:
  - type: "area"
    corner: [-3, 4, -3]
    u: [6, 0, 0]
    v: [0, 0, 6]
    color: [1, 1, 1]
    intensity: 50.0

  - type: "point"
    position: [0, 1, -4]
    color: [1, 1, 1]
    intensity: 10.0
```

### Chiaroscuro Drammatico

Una singola luce spot forte con ombre profonde. Ispirato a Caravaggio e al film noir.

```yaml
lights:
  - type: "spot"
    position: [-3, 5, -2]
    direction: [0.6, -1, 0.4]
    color: [1.0, 0.9, 0.75]
    intensity: 80.0
    inner_angle: 12
    outer_angle: 25
```

### Luce Solare Esterna

Luce direzionale per il sole, un sottile riempimento blu per la luce del cielo.

```yaml
lights:
  - type: "directional"
    direction: [-0.3, -1, 0.5]
    color: [1.0, 0.98, 0.92]
    intensity: 3.0

  - type: "directional"
    direction: [0, -1, 0]
    color: [0.5, 0.6, 0.85]
    intensity: 0.5

  - type: "point"
    position: [5, 1, -5]
    color: [0.8, 0.85, 1.0]
    intensity: 8.0
```

---

## 6.11 Esempio Completo: Confronto Luci

Un'unica sfera e un piedistallo illuminati da diversi tipi di luce.

```yaml
# lighting-comparison.yaml
# Lo stesso soggetto sotto cinque diverse luci.
# Render con: RayTracer -i lighting-comparison.yaml -w 1600 -H 500 -s 64

world:
  sky:
    type: "flat"
    color: [0.02, 0.02, 0.03]

cameras:
  - name: "main"
    position: [0, 3, -10]
    look_at: [0, 1, 0]
    fov: 50

lights:
  # 1. Luce puntiforme sulla prima sfera
  - type: "point"
    position: [-6, 3.5, -1]
    color: [1, 0.95, 0.9]
    intensity: 25.0

  # 2. Luce direzionale per la seconda sfera
  - type: "directional"
    direction: [-0.3, -1, 0.5]
    color: [1, 0.98, 0.92]
    intensity: 2.5

  # 3. Luce spot per la terza sfera
  - type: "spot"
    position: [0, 4, -1]
    direction: [0, -1, 0.2]
    color: [1, 1, 1]
    intensity: 50.0
    inner_angle: 12
    outer_angle: 25

  # 4. Luce area per la quarta sfera
  - type: "area"
    corner: [2, 3.5, -1.5]
    u: [2, 0, 0]
    v: [0, 0, 2]
    color: [1, 0.97, 0.93]
    intensity: 30.0

materials:
  - id: "floor"
    type: "lambertian"
    color: [0.35, 0.35, 0.35]
  - id: "white_plastic"
    type: "disney"
    color: [0.9, 0.88, 0.85]
    roughness: 0.3
    specular: 0.6
  - id: "pedestal"
    type: "disney"
    color: [0.6, 0.6, 0.6]
    roughness: 0.15
    specular: 0.7
  - id: "glow"
    type: "emissive"
    color: [1, 0.9, 0.75]
    intensity: 15.0

entities:
  - type: "infinite_plane"
    point: [0, 0, 0]
    normal: [0, 1, 0]
    material: "floor"

  # Cinque sfere in fila, ognuna illuminata da una luce diversa
  # 1. Point light
  - type: "sphere"
    center: [-6, 1.5, 0]
    radius: 0.6
    material: "white_plastic"
  - type: "cylinder"
    center: [-6, 0, 0]
    radius: 0.4
    height: 0.9
    material: "pedestal"

  # 2. Directional light
  - type: "sphere"
    center: [-3, 1.5, 0]
    radius: 0.6
    material: "white_plastic"
  - type: "cylinder"
    center: [-3, 0, 0]
    radius: 0.4
    height: 0.9
    material: "pedestal"

  # 3. Spot light
  - type: "sphere"
    center: [0, 1.5, 0]
    radius: 0.6
    material: "white_plastic"
  - type: "cylinder"
    center: [0, 0, 0]
    radius: 0.4
    height: 0.9
    material: "pedestal"

  # 4. Area light
  - type: "sphere"
    center: [3, 1.5, 0]
    radius: 0.6
    material: "white_plastic"
  - type: "cylinder"
    center: [3, 0, 0]
    radius: 0.4
    height: 0.9
    material: "pedestal"

  # 5. Luce geometrica (sfera emissiva)
  - type: "sphere"
    center: [6, 1.5, 0]
    radius: 0.6
    material: "white_plastic"
  - type: "cylinder"
    center: [6, 0, 0]
    radius: 0.4
    height: 0.9
    material: "pedestal"
  - type: "sphere"
    center: [6, 3.2, -0.5]
    radius: 0.15
    material: "glow"
```

Questa scena pone cinque sfere identiche in fila. Ognuna è illuminata principalmente da un tipo di luce diverso, rendendo facile confrontare fianco a fianco la qualità dell'ombra, la forma del riflesso e il comportamento del decadimento.

---

## 6.12 Caustiche (luce focalizzata da vetro e specchi)

Una sfera di vetro, un bicchiere d'acqua, una gemma o una sfera di metallo
lucido non si limitano a proiettare un'ombra: **focalizzano** la luce in forme
luminose — la macchia che danza sul fondo di una piscina, l'anello di luce che
un calice lascia sul tavolo, il bagliore colorato sotto una bottiglia tinta.
Sono le **caustiche**, e poiché richiedono che la luce segua il cammino piegato
(rifratto) o riflesso, i normali shadow ray non sanno produrle.

3D-Ray le rende con un **pre-pass di fotoni**. Non devi marcare niente nella
scena: qualsiasi superficie speculare (vetro, acqua, metallo, specchio)
focalizza la luce in automatico, e qualsiasi superficie diffusa (un pavimento,
una parete) la riceve. Tutte le luci guidano le caustiche, **sole**
(`directional`) compreso. Basta attivarle:

```bash
# Le caustiche sono attive di default nei preset final/ultra:
RayTracer -i mia-scena.yaml -q final

# Oppure attivale esplicitamente su qualsiasi preset:
RayTracer -i mia-scena.yaml -q standard --caustics on
```

Qualche nota pratica:

- **`--caustic-photons <N>`** regola la qualità: più fotoni = caustiche più
  nitide e meno rumorose (e pre-pass più lento). I preset scelgono un default
  ragionevole.
- **Il vetro tinto proietta una caustica colorata** — un vetro rosso getta una
  pozza di luce rossa, perché i fotoni assorbono il colore del vetro che
  attraversano.
- Una luce piccola e intensa dà una caustica **netta**; una grande la rende
  **morbida**. Il sole dà caustiche nitide a raggi paralleli.
- Le caustiche da vetro smerigliato/rough e da ambiente HDRI sono più morbide e
  gestite dal path tracer ordinario in questa versione.

Configurazioni pronte di vetro, metallo e lenti sono in
[`scenes/presets/caustics.md`](../../../scenes/presets/caustics.md).

## 6.13 Compensazione di esposizione (`--exposure`)

Una volta piazzate le luci, il tone mapper deve tradurre la radianza
della scena in un range visualizzabile 0-1. 3D-Ray usa la curva **ACES
filmic**, lo standard industriale adottato nelle pipeline cinematografiche e VFX. ACES
è non-lineare: il contrasto è preservato solo dentro la sua sweet-spot
lineare, a circa `[0.18, 1.0]` di radianza in ingresso. Sopra ~2.0 la
curva si appiattisce sul plateau 0.95-0.99 dove tutto appare "quasi
bianco" — base color, venature dei marmi e identità del materiale
collassano tutti nella stessa luminosità.

Quel collasso è il motivo più comune per cui una scena ben costruita
sembra "lavata" o "spenta": le luci sono semplicemente troppo forti e
ACES non ha più spazio per smorzare gli highlight. Il fix è la
**compensazione fotografica di esposizione** — un guadagno lineare
`2^EV` applicato a ogni pixel *prima* del tone mapping:

```bash
RayTracer -i scene.yaml -o out.png --exposure -1.5
```

La semantica EV è quella di una macchina fotografica reale: `EV = 0`
(default) è identità, `EV = -1` scurisce di un fattore 2 (uno stop in
meno), `EV = +1` schiarisce di un fattore 2 (uno stop in più). Il flag
replica il concetto di compensazione dell'esposizione fotografica
disponibile nei renderer di produzione.

**Quando usarlo:**

| Sintomo | `--exposure` da provare |
|---|---|
| Gli highlight bruciano prima ancora che i mid-tone si leggano | `-1` a `-2` |
| I bianchi appaiono uniformemente crema, le venature dei marmi invisibili | `-1` a `-1.5` |
| L'immagine finisce in mid-tone scuri rumorosi | `+1` a `+2` |
| Le luci sono già tarate per atterrare vicino a `0.5` lineare | `0` (omettere il flag) |

**Quando *non* usarlo.** L'esposizione è un moltiplicatore globale —
sposta *ogni* pixel della stessa quantità. Se solo una parte della
scena è lavata (una singola hero light, un oggetto troppo emissivo),
ribilancia l'intensità di quella luce invece, così la scena è esposta
correttamente senza bisogno del flag. Riserva `--exposure` per
iterazione veloce su shot dove non vuoi committare un cambio YAML, e
per compensare scene HDRI/IBL la cui luminanza assoluta non controlli.

Il pass di esposizione è applicato tra il firefly clamp per-sample
(`-C` / `--clamp`) e la curva ACES, quindi tutti i clamp standard e
il post-processing si comportano in modo identico. L'unica cosa che
cambia è *quale fetta* della curva ACES vede la tua radianza.

---

## Cosa si è imparato

- Le luci **Point** irradiano da un punto (decadimento con l'inverso del quadrato, ombre nette).
- Le luci **Directional** inviano raggi paralleli (nessun decadimento, ombre nette per default). Usa `angular_radius: 0.27` per un disco solare realistico.
- Le luci **Spot** emettono un cono con controllo degli angoli interno ed esterno. Usa `soft_radius` + `shadow_samples > 1` per penombra morbida in nebbia.
- Le luci **Area** sono rettangoli che producono ombre morbide; la qualità è controllata da `shadow_samples`. Usa `soft_radius` per prevenire spike in media densi.
- Le luci **Sphere** producono ombre morbide con riflessi circolari e usano lo stimatore ad angolo solido limitato (non serve `soft_radius`).
- Le **entità emissive** diventano automaticamente luci geometriche -- visibili e campionate per l'illuminazione diretta.
- Le luci `area` e `sphere` sono inoltre visibili alla camera e ai raggi specular tramite un proxy emissivo gestito internamente, garantendo piena convergenza Veach-MIS.
- Per-luce e per-entity **`visible_to_camera: false`** nasconde il proxy/la geometria solo dai raggi primari della camera; NEE, specchi, vetro e rimbalzi indiretti continuano a vederla.
- Il flag CLI `-S` sovrascrive globalmente i campioni d'ombra per prove veloci.
- Lo **schema a tre punti** (chiave, riempimento, contorno) è un punto di partenza affidabile per ogni scena.
- **Controlli firefly:**
  - `soft_radius` su luci point/spot/area → clampa il denominatore dell'attenuazione
  - `--indirect-clamp-factor 0.25` → clamp più stretto sui bounce ≥ 1
  - `--light-sampling power` → sceglie una luce per evento NEE ∝ `ApproximatePower` (convergenza più rapida in scene multi-luce)

---

[Precedente: Trasformazioni, gruppi e organizzazione della scena](./05-transforms-and-groups.md) | [Successivo: Cielo, ambiente ed effetti fotocamera](./07-sky-environment-camera.md) | [Indice del Tutorial](./README.md)
