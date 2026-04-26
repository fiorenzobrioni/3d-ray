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
```

| Parametro   | Predefinito | Descrizione                                     |
|-------------|-------------|-------------------------------------------------|
| `direction` | --          | Direzione *verso* la scena (dalla luce)         |
| `color`     | --          | Colore della luce                               |
| `intensity` | --          | Moltiplicatore di luminosità                    |

Una luce direzionale invia raggi paralleli -- tutti viaggiano nella stessa direzione. Non ha posizione (pensa ad essa come se fosse infinitamente lontana) e non ha decadimento con la distanza. È il modo standard per simulare la luce solare.

Il vettore `direction` punta **dalla luce verso la scena**, non dalla scena verso la luce. `[-0.5, -1, 0.3]` significa che la luce proviene da in alto a sinistra, leggermente dietro la fotocamera.

Come le luci puntiformi, le luci direzionali producono **ombre nette**.

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
```

| Parametro     | Predefinito | Descrizione                                          |
|---------------|-------------|------------------------------------------------------|
| `position`    | --          | Posizione nello spazio world                         |
| `direction`   | --          | Direzione verso cui è puntato lo spot                |
| `color`       | --          | Colore della luce                                    |
| `intensity`   | --          | Moltiplicatore di luminosità                         |
| `inner_angle` | `15`        | Semiampiezza del cono a piena intensità (gradi)      |
| `outer_angle` | `30`        | Semiampiezza del cono a intensità zero (gradi)       |
| `soft_radius` | `0`         | Opzionale. Stesso ruolo della point light — fortemente raccomandato per spot dentro un medium/nebbia |

Una luce spot emette da un punto all'interno di un cono. All'interno del cono interno (inner cone) la luce è alla massima intensità. Tra il cono interno e quello esterno (outer cone) sfuma gradualmente fino a zero (decadimento cosinusoidale). All'esterno del cono esterno non c'è luce.

Le luci spot sono ideali per effetti teatrali, esposizioni museali e torce. Producono anch'esse ombre nette.

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
  shadow_samples: 16
```

| Parametro        | Predefinito | Descrizione                                          |
|------------------|-------------|------------------------------------------------------|
| `corner`         | --          | Un angolo del rettangolo                             |
| `u`              | --          | Primo vettore lato (dall'angolo)                     |
| `v`              | --          | Secondo vettore lato (dall'angolo)                   |
| `color`          | --          | Colore della luce                                    |
| `intensity`      | --          | Moltiplicatore di luminosità                         |
| `shadow_samples` | --          | Numero di campioni d'ombra (più alto = più morbida)  |

Una luce area è un rettangolo piatto che emette luce da tutta la sua superficie. Poiché ha una dimensione fisica, produce **ombre morbide** con una penombra realistica (la transizione graduale dall'ombra alla luce).

Il rettangolo è definito da `corner` e due vettori lato `u` e `v`, proprio come un quad. I quattro vertici sono `corner`, `corner+u`, `corner+u+v` e `corner+v`.

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

### Importante: Le luci Area sono invisibili

Le luci area illuminano la scena ma **non sono visibili** come oggetti. Un raggio che colpisce il rettangolo della luce non vede una superficie luminosa -- attraversa. Se si vuole un pannello luminoso visibile, usare un quad emissivo (vedi Sezione 6.7).

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

| Parametro        | Predefinito | Descrizione                             |
|------------------|-------------|-----------------------------------------|
| `position`       | --          | Centro della sfera                      |
| `radius`         | --          | Raggio della sfera luminosa             |
| `color`          | --          | Colore della luce                       |
| `intensity`      | --          | Moltiplicatore di luminosità            |
| `shadow_samples` | --          | Numero di campioni d'ombra              |

Una luce sferica è come una luce area, ma di forma sferica. Produce ombre morbide con una penombra circolare e crea riflessi perfettamente rotondi (catchlights) nelle superfici riflettenti.

Le luci sferiche utilizzano il **campionamento dell'angolo solido**, che è da 2 a 10 volte più efficiente rispetto a una sfera emissiva equivalente per luci piccole o distanti. Preferisci le luci sferiche alle sfere emissive quando la sorgente luminosa è l'illuminazione principale della scena.

Come le luci area, le luci sferiche sono **invisibili** per la fotocamera.

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
| Visibile in camera            | Sì                   | No                           |
| Visibile nei riflessi         | Sì                   | No                           |
| Illuminazione diretta (NEE)   | Sì (automatico)      | Sì                           |
| Ombre morbide                 | Sì                   | Sì                           |
| Efficienza                    | Buona                | Leggermente migliore         |

Usare le luci geometriche quando la sorgente luminosa deve essere **vista** (insegne al neon, flussi di lava, sfere incandescenti, lampadine). Usare le luci esplicite quando si vuole una sorgente luminosa invisibile (softbox fuori campo, luci di riempimento).

Il motore supporta le luci geometriche su qualsiasi primitiva campionabile: sfere, quad, dischi, box, cilindri, coni, tori, capsule, annuli e mesh.

### Multiple Importance Sampling — perché vale per tutti i materiali

L'illuminazione diretta su una luce non delta (area, sphere, geometric, environment) è calcolata combinando due strategie indipendenti: la **NEE** campiona un punto sulla luce, il **BSDF sampling** campiona la direzione di rimbalzo dal materiale. Pesare i due contributi con la **balance heuristic** (default) o la **power heuristic** (`--mis power`) riduce la varianza rispetto a usare una sola strategia.

Tutti i materiali supportati — `lambertian`, `metal`, `mix`, `disney` — espongono la tripla `Sample`/`Pdf`/`Evaluate` necessaria al MIS. Non c'è nessuna configurazione: il motore applica automaticamente i pesi corretti in base al tipo di materiale e di luce. Le luci delta puro (point, directional, spot) e i lobi delta dei materiali (specchio perfetto, vetro ideale) sono trattati come casi speciali e ricevono peso 1 — non possono essere campionati dall'altra strategia.

Per scene con nebbia o fumo (`global_medium`), anche la phase function partecipa al MIS: il motore pesa l'in-scattering NEE contro il phase-sampled bounce, riducendo i fireflies tipici dei "shaft" di luce attraverso il volume.

---

## 6.8 Lo Schema di Illuminazione a Tre Punti

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
  shadow_samples: 16
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

## 6.9 Ricette di Illuminazione

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
    shadow_samples: 16

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

## 6.10 Esempio Completo: Confronto Luci

Un'unica sfera e un piedistallo illuminati da diversi tipi di luce.

```yaml
# lighting-comparison.yaml
# Lo stesso soggetto sotto cinque diverse luci.
# Render con: RayTracer -i lighting-comparison.yaml -w 1600 -H 500 -s 64

world:
  ambient_light: [0.01, 0.01, 0.015]
  background: [0.02, 0.02, 0.03]

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
    shadow_samples: 16

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

## Cosa si è imparato

- Le luci **Point** irradiano da un punto (decadimento con l'inverso del quadrato, ombre nette).
- Le luci **Directional** inviano raggi paralleli (nessun decadimento, ombre nette).
- Le luci **Spot** emettono un cono con controllo degli angoli interno ed esterno.
- Le luci **Area** sono rettangoli che producono ombre morbide; la qualità è controllata da `shadow_samples`.
- Le luci **Sphere** producono ombre morbide con riflessi circolari.
- Le **entità emissive** diventano automaticamente luci geometriche -- visibili e campionate per l'illuminazione diretta.
- Il flag CLI `-S` sovrascrive globalmente i campioni d'ombra per prove veloci.
- Lo **schema a tre punti** (chiave, riempimento, contorno) è un punto di partenza affidabile per ogni scena.

---

[Precedente: Trasformazioni, gruppi e organizzazione della scena](./05-transforms-and-groups.md) | [Successivo: Cielo, ambiente ed effetti fotocamera](./07-sky-environment-camera.md) | [Indice del Tutorial](./README.md)
