# Capitolo 2: La prima scena

È il momento di scrivere YAML e renderizzare un'immagine. In questo capitolo si
costruirà una scena completa da zero -- tre sfere su un pavimento, illuminate da due
sorgenti luminose -- e si comprenderà ogni riga del file.

---

## 2.1 Il minimo assoluto: una sola sfera

Creare un file chiamato `hello.yaml` e scrivere il seguente contenuto:

```yaml
world:
  sky:
    type: "flat"
    color: [0.05, 0.05, 0.08]

cameras:
  - name: "main"
    position: [0, 1, -5]
    look_at: [0, 0.5, 0]
    fov: 50

lights:
  - type: "directional"
    direction: [-1, -1, 1]
    color: [1, 1, 1]
    intensity: 2.0

materials:
  - id: "white"
    type: "lambertian"
    color: [0.9, 0.9, 0.9]

entities:
  - name: "ball"
    type: "sphere"
    center: [0, 0.5, 0]
    radius: 0.5
    material: "white"
```

Renderizzarlo con:

```
RayTracer -i hello.yaml -w 800 -H 450 -s 16
```

Si dovrebbe vedere una sfera bianca che galleggia su uno sfondo scuro. Si analizza di seguito ogni sezione.

---

## 2.1b Il sistema di coordinate

Prima di posizionare gli oggetti è fondamentale sapere come 3D-Ray orienta il proprio spazio:

- **Y è verso l'alto.** Il pavimento è convenzionalmente a Y = 0. Gli oggetti si trovano sopra (Y positivo) e il cielo è in alto.
- **Sistema destrorso.** Guardando lungo l'asse Z negativo (la direzione predefinita "nello schermo"), X punta a destra e Y punta in alto.
- **Le unità sono metri** per convenzione. Il motore è agnostico rispetto alle unità, ma tutte le scene e le librerie incluse usano 1 unità = 1 metro. Una sfera con `radius: 0.5` è grande circa come un pompelmo; `radius: 10` riempie una stanza.

Riferimento pratico:

| Direzione              | Asse       | Esempio                         |
|------------------------|------------|---------------------------------|
| Destra                 | `+X`       | `translate: [2, 0, 0]`          |
| Sinistra               | `-X`       | `translate: [-2, 0, 0]`         |
| Su                     | `+Y`       | `translate: [0, 1, 0]`          |
| Giù                    | `-Y`       | `translate: [0, -1, 0]`         |
| Dentro la scena        | `+Z`       | `translate: [0, 0, 3]`          |
| Verso la camera        | `-Z`       | `translate: [0, 0, -3]`         |

La camera nella scena sopra si trova a `[0, 1, -5]` — 1 m sopra il pavimento e 5 m davanti alla scena — puntando verso `[0, 0.5, 0]`, il centro della sfera.

> **Suggerimento:** se un oggetto scompare dalla vista, verificare che non si trovi dietro la camera (Z negativo) o sotto il pavimento (Y negativo). Il flag `--verbose` stampa il bounding box della scena per aiutare a localizzare la geometria perduta.

---

## 2.2 La sezione world

```yaml
world:
  sky:
    type: "flat"
    color: [0.05, 0.05, 0.08]
```

**`sky`** è l'emettitore globale dell'ambiente. Sono supportati tre tipi:

- **`flat`** — colore uniforme su tutta la sfera; la modalità più semplice e quella usata qui. Il cielo flat agisce anche come fill light morbido: partecipa a NEE e il suo colore rimbalza su ogni superficie.
- **`gradient`** — sfumatura verticale a tre bande con disco solare opzionale.
- **`hdri`** — immagine HDR equirettangolare per illuminazione image-based completa.

Se ometti `sky:`, il motore usa un cielo flat azzurro-diurno (`[0.5, 0.7, 1.0]`). Il Capitolo 7 tratta in dettaglio gradient e HDRI.

La sezione `world:` supporta anche una **scorciatoia per il terreno**:

```yaml
world:
  ground:
    type: "plane"
    material: "floor_material"
    y: 0.0
```

Questo posiziona un piano di terreno infinito alla coordinata Y specificata usando
il materiale indicato, evitando di aggiungere un'entità piano-infinito separata. Si
può usare questa scorciatoia o un'entità esplicita -- il risultato è identico.

---

## 2.3 La fotocamera

```yaml
cameras:
  - name: "main"
    position: [0, 1, -5]
    look_at: [0, 0.5, 0]
    fov: 50
```

La fotocamera definisce il punto di vista:

| Campo        | Predefinito   | Descrizione                                    |
|--------------|---------------|------------------------------------------------|
| `position`   | --            | Posizione della fotocamera nello spazio world  |
| `look_at`    | --            | Il punto verso cui punta la fotocamera         |
| `vup`        | `[0, 1, 0]`  | La direzione che è considerata "su"            |
| `fov`        | `60`          | Campo visivo verticale in gradi                |
| `aperture`   | `0`           | Diametro dell'obiettivo per la profondità di campo (0 = foro stenopeico) |
| `focal_dist` | `1`           | Distanza di messa a fuoco per la profondità di campo |

**`position`** e **`look_at`** insieme determinano dov'è la fotocamera e dove è
puntata. Il vettore da `position` a `look_at` è la direzione di visione.

**`vup`** (view up) indica alla fotocamera quale direzione è verso l'alto. Non è
quasi mai necessario modificarlo a meno che non si voglia un'inquadratura "Dutch
angle" inclinata.

**`fov`** controlla quanto della scena è visibile. Un valore piccolo (es. 25) agisce
come un obiettivo tele -- gli oggetti appaiono più grandi ma si vede meno della scena.
Un valore grande (es. 90) agisce come un grandangolo.

**`aperture`** e **`focal_dist`** controllano la profondità di campo e sono trattati
nel Capitolo 7. Impostare `aperture` a `0` (predefinito) per avere tutto a fuoco.

> **Suggerimento:** `cameras:` (la forma a lista) è il formato raccomandato -- funziona
> sia per una camera sola che per più camere. La chiave singolare `camera:` è ancora
> supportata per compatibilità. La configurazione multi-fotocamera è trattata nel Capitolo 7.

---

## 2.4 Materiali: Lambertian e Metal

I materiali descrivono l'aspetto di una superficie e come interagisce con la luce.
In questo capitolo introduciamo due tipi di base: **Lambertian** e **Metal**.
(I restanti quattro tipi sono trattati nel Capitolo 3.)

### Lambertian (diffuso opaco)

```yaml
- id: "red_matte"
  type: "lambertian"
  color: [0.8, 0.1, 0.1]
```

Una superficie Lambertian disperde la luce in arrivo uniformemente in tutte le
direzioni sopra la superficie. Non presenta riflessi né lucentezza -- solo un aspetto
piatto e opaco. `color` è l'albedo diffusa: la frazione di luce riflessa per canale RGB.

- `[1, 1, 1]` riflette tutta la luce (bianco puro).
- `[0, 0, 0]` assorbe tutta la luce (nero puro).
- `[0.8, 0.1, 0.1]` riflette principalmente il rosso.

### Metal

```yaml
- id: "gold_mirror"
  type: "metal"
  color: [1.0, 0.76, 0.33]
  fuzz: 0.0

- id: "brushed_steel"
  type: "metal"
  color: [0.7, 0.7, 0.72]
  fuzz: 0.3
```

Una superficie Metal riflette la luce specularmante (come uno specchio). Il campo
`color` rappresenta la riflettanza metallica -- per risultati fisicamente accurati,
usare valori misurati:

| Metallo    | Colore (approssimato)   |
|------------|-------------------------|
| Oro        | `[1.0, 0.76, 0.33]`    |
| Argento    | `[0.97, 0.96, 0.95]`   |
| Rame       | `[0.95, 0.64, 0.54]`   |
| Acciaio    | `[0.7, 0.7, 0.72]`     |
| Alluminio  | `[0.91, 0.92, 0.93]`   |

Il parametro **`fuzz`** controlla la ruvidità:

- `fuzz: 0.0` produce uno specchio perfetto.
- `fuzz: 0.1` produce un riflesso leggermente sfocato (finitura satinata).
- `fuzz: 0.3` produce un riflesso visibilmente sfocato (metallo spazzolato).
- Valori superiori a 0.5 creano un aspetto molto diffuso, quasi opaco.

---

## 2.5 Entità: posizionare gli oggetti

```yaml
entities:
  - name: "floor"
    type: "infinite_plane"
    point: [0, 0, 0]
    normal: [0, 1, 0]
    material: "grey_matte"

  - name: "ball"
    type: "sphere"
    center: [0, 0.5, 0]
    radius: 0.5
    material: "red_matte"
```

La sezione `entities:` è dove si posizionano gli oggetti nella scena. Ogni entità
ha come minimo un `type` e un riferimento a `material` (un ID che corrisponde a un
materiale definito nella sezione `materials:`).

**`name`** è opzionale ma fortemente consigliato -- rende il file di scena leggibile
e aiuta nel debug.

### Sfera

La primitiva più semplice:

```yaml
- type: "sphere"
  center: [0, 1, 0]
  radius: 1.0
  material: "my_material"
```

Una sfera è centrata in `center` con il `radius` dato. Per posizionare una sfera
che poggia su un pavimento a Y=0, impostare `center` a `[x, radius, z]` così che
il fondo della sfera tocchi il piano.

### Piano infinito

```yaml
- type: "infinite_plane"
  point: [0, 0, 0]
  normal: [0, 1, 0]
  material: "my_material"
```

Una superficie piana infinita che passa per `point` con la `normal` data. Un
pavimento ha `normal: [0, 1, 0]` (punta verso l'alto). Una parete posteriore avrebbe
`normal: [0, 0, -1]` (punta verso la fotocamera).

Le altre forme (box, cilindri, tori, coni e altro) si troveranno nel Capitolo 4.

---

## 2.6 Luci: illuminare la scena

Senza luci, l'unica illuminazione proviene dalla luce ambientale e dal colore di
sfondo -- il che produce un'immagine molto fioca e piatta. Si aggiungono ora due sorgenti luminose esplicite.

### Luce direzionale

```yaml
- type: "directional"
  direction: [-1, -1, 1]
  color: [1, 1, 1]
  intensity: 2.0
```

Una luce direzionale invia raggi paralleli nella `direction` specificata. Non ha
posizione -- si pensi a essa come infinitamente lontana, come il sole. Il vettore
`direction` punta *dalla* luce *verso* la scena. `[-1, -1, 1]` significa che la luce
proviene dall'alto a sinistra, davanti.

### Luce point

```yaml
- type: "point"
  position: [3, 3, -2]
  color: [0.9, 0.9, 1.0]
  intensity: 15.0
```

Una luce point irradia da un singolo punto. La sua intensità cala con il quadrato
della distanza (legge dell'inverso del quadrato), proprio come una lampadina reale.
Il valore `intensity` quindi deve essere molto più alto rispetto a una luce
direzionale per ottenere una luminosità simile sull'oggetto.

Entrambi i tipi di luce producono **ombre dure** (bordi netti). Per ombre morbide
e naturali servono luci area o sphere, trattate nel Capitolo 6.

---

## 2.7 Esempio completo: tre sfere su un pavimento

Ecco il file di scena completo che mette tutto insieme:

```yaml
# three-spheres.yaml
# Una sfera rossa opaca, una sfera dorata e una sfera specchio su un pavimento grigio.

world:
  sky:
    type: "flat"
    color: [0.05, 0.05, 0.08]

cameras:
  - name: "main"
    position: [0, 2, -6]
    look_at: [0, 0.5, 0]
    fov: 45

lights:
  # Luce principale dall'alto a sinistra
  - type: "directional"
    direction: [-1, -1, 1]
    color: [1, 0.98, 0.95]
    intensity: 2.5

  # Luce di riempimento da destra
  - type: "point"
    position: [4, 3, -3]
    color: [0.8, 0.85, 1.0]
    intensity: 20.0

materials:
  - id: "grey_floor"
    type: "lambertian"
    color: [0.5, 0.5, 0.5]

  - id: "red_matte"
    type: "lambertian"
    color: [0.8, 0.1, 0.1]

  - id: "gold"
    type: "metal"
    color: [1.0, 0.76, 0.33]
    fuzz: 0.15

  - id: "mirror"
    type: "metal"
    color: [0.95, 0.95, 0.97]
    fuzz: 0.0

entities:
  # Pavimento
  - name: "floor"
    type: "infinite_plane"
    point: [0, 0, 0]
    normal: [0, 1, 0]
    material: "grey_floor"

  # Sfera sinistra -- rossa opaca
  - name: "left_sphere"
    type: "sphere"
    center: [-1.5, 0.5, 0]
    radius: 0.5
    material: "red_matte"

  # Sfera centrale -- oro spazzolato
  - name: "center_sphere"
    type: "sphere"
    center: [0, 0.75, 0]
    radius: 0.75
    material: "gold"

  # Sfera destra -- specchio perfetto
  - name: "right_sphere"
    type: "sphere"
    center: [1.5, 0.5, 0]
    radius: 0.5
    material: "mirror"
```

### Rendering

Iniziare con una rapida anteprima per verificare la composizione:

```
RayTracer -i three-spheres.yaml -w 400 -H 225 -s 64 -d 4 -S 1
```

Poi una bozza per verificare materiali e illuminazione (profilo Standard):

```
RayTracer -i three-spheres.yaml -w 800 -H 450 -s 256 -d 6
```

E infine un render pulito (profilo Final):

```
RayTracer -i three-spheres.yaml -w 1920 -H 1080 -s 1024 -d 8 -S 4
```

### Cosa si dovrebbe vedere

- La **sfera rossa** a sinistra ha un aspetto uniforme e opaco senza riflessi.
- La **sfera dorata** al centro mostra riflessi sfocati della scena, tintati di
  oro caldo.
- La **sfera specchio** a destra mostra riflessi nitidi degli altri oggetti e
  del pavimento.
- Il **pavimento** è un grigio uniforme che si estende fino all'orizzonte.
- Ci sono due regioni d'ombra per ogni sfera, una per ciascuna luce.

---

## Cosa si è imparato

- Una scena ha bisogno come minimo di: `world`, `cameras:`, almeno un `material`,
  almeno un'`entity` e almeno una `light`.
- `lambertian` dà una superficie diffusa opaca; `metal` dà una superficie riflessiva,
  controllata da `fuzz`.
- Le luci `directional` non hanno posizione (raggi paralleli); le luci `point`
  irradiano da una posizione.
- Il flusso di lavoro iterativo (anteprima -> bozza -> finale) fa risparmiare tempo.

---

[Precedente: Che cos'è il ray tracing?](./01-what-is-ray-tracing.md) | [Successivo: I materiali in dettaglio](./03-materials.md) | [Indice del Tutorial](./README.md)
