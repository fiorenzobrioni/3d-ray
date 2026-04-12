# Capitolo 7: Cielo, ambiente ed effetti fotocamera

Il cielo è la più grande sorgente luminosa in qualsiasi scena all'aperto. Un ambiente ben configurato può trasformare un rendering piatto in qualcosa di veramente fotografico. Questo capitolo tratta anche la profondità di campo (depth of field) e le configurazioni multi-fotocamera.

---

## 7.1 Modalità Cielo (Sky Modes)

Il cielo determina il colore che riceve un raggio quando non colpisce alcun oggetto e fugge all'infinito. 3D-Ray supporta tre modalità di cielo, configurabili sotto `world: > sky:`.

| Modalità   | Descrizione                                          |
|------------|------------------------------------------------------|
| `flat`     | Colore singolo a tinta unita (usa `background`)      |
| `gradient` | Gradiente verticale a tre bande con sole opzionale   |
| `hdri`     | Immagine HDR equirettangolare (image-based lighting) |

Quando non è presente il blocco `sky:`, il motore utilizza un cielo piatto con il colore di `background:`.

---

## 7.2 Cielo Piatto (Flat Sky - Predefinito)

```yaml
world:
  background: [0.5, 0.65, 0.9]
```

Senza una sezione `sky:`, i raggi che escono dalla scena restituiscono il colore di `background`. Questo è perfettamente adeguato per scene da studio con illuminazione controllata (dove lo sfondo è solitamente nero) o semplici bozze all'aperto.

Puoi anche essere esplicito:

```yaml
world:
  sky:
    type: "flat"
```

Questo si comporta in modo identico all'uso del colore `background:`.

---

## 7.3 Cielo Gradiente con Disco Solare

Un cielo gradiente crea una sfumatura verticale realistica dallo zenit (direttamente sopra) all'orizzonte fino al suolo, e opzionalmente aggiunge un disco solare visibile.

```yaml
world:
  sky:
    type: "gradient"
    zenith_color: [0.25, 0.45, 0.9]
    horizon_color: [0.7, 0.8, 0.95]
    ground_color: [0.4, 0.35, 0.3]
```

| Parametro       | Predefinito | Descrizione                           |
|-----------------|-------------|---------------------------------------|
| `zenith_color`  | --          | Colore direttamente sopra la testa    |
| `horizon_color` | --          | Colore all'orizzonte                  |
| `ground_color`  | --          | Colore sotto l'orizzonte              |

Il gradiente interpola verticalmente: i raggi che puntano verso l'alto ricevono il colore zenit; i raggi all'orizzonte ricevono il colore orizzonte; i raggi che puntano sotto l'orizzonte ricevono il colore ground.

### Aggiungere un disco solare

```yaml
world:
  sky:
    type: "gradient"
    zenith_color: [0.2, 0.35, 0.75]
    horizon_color: [0.85, 0.75, 0.55]
    ground_color: [0.3, 0.25, 0.2]
    sun:
      direction: [-0.5, -0.3, 1]
      color: [1.0, 0.95, 0.8]
      intensity: 10.0
      size: 3.0
      falloff: 32.0
```

| Parametro Sole | Predefinito | Descrizione                                     |
|----------------|-------------|-------------------------------------------------|
| `direction`    | --          | Direzione *verso* il sole (dalla scena)         |
| `color`        | --          | Colore del disco solare                         |
| `intensity`    | `10.0`      | Moltiplicatore di luminosità                    |
| `size`         | `3.0`       | Diametro angolare in gradi                      |
| `falloff`      | `32.0`      | Nitidezza del bordo (più alto = bordo più netto)|

Il disco solare appare come un punto luminoso nel cielo. Partecipa all'illuminazione diretta (il motore lo campiona per la Next Event Estimation), il che significa che produce ombre e riflessi proprio come una sorgente luminosa esplicita. Puoi usare un cielo gradiente con un disco solare come **unica sorgente luminosa** in una scena all'aperto.

### Preset: Golden Hour (Ora d'oro)

```yaml
world:
  sky:
    type: "gradient"
    zenith_color: [0.15, 0.25, 0.55]
    horizon_color: [0.95, 0.65, 0.3]
    ground_color: [0.25, 0.18, 0.12]
    sun:
      direction: [-0.8, -0.15, 0.5]
      color: [1.0, 0.75, 0.4]
      intensity: 15.0
      size: 4.0
      falloff: 24.0
```

### Preset: Mezzogiorno (Noon)

```yaml
world:
  sky:
    type: "gradient"
    zenith_color: [0.25, 0.45, 0.9]
    horizon_color: [0.6, 0.75, 0.95]
    ground_color: [0.35, 0.3, 0.25]
    sun:
      direction: [0.1, -1, 0.2]
      color: [1.0, 0.98, 0.95]
      intensity: 12.0
      size: 2.5
      falloff: 40.0
```

### Preset: Notte con Luna

```yaml
world:
  ambient_light: [0.005, 0.005, 0.01]
  sky:
    type: "gradient"
    zenith_color: [0.01, 0.01, 0.04]
    horizon_color: [0.03, 0.03, 0.06]
    ground_color: [0.01, 0.01, 0.02]
    sun:
      direction: [0.4, -0.6, 0.7]
      color: [0.7, 0.75, 0.9]
      intensity: 3.0
      size: 1.5
      falloff: 50.0
```

---

## 7.4 Illuminazione basata su immagini HDRI

Per il massimo realismo, usa un'immagine ad alta gamma dinamica (HDRI) come cielo. Un'HDRI cattura l'intero campo luminoso di un ambiente reale -- ogni direzione ha una luminosità e un colore misurati.

```yaml
world:
  sky:
    type: "hdri"
    path: "textures/venice_sunset_2k.hdr"
    intensity: 1.0
    rotation: 45.0
```

| Parametro   | Predefinito | Descrizione                                     |
|-------------|-------------|-------------------------------------------------|
| `path`      | --          | Percorso di un file HDR equirettangolare        |
| `intensity` | `1.0`       | Moltiplicatore di luminosità                    |
| `rotation`  | `0.0`       | Rotazione orizzontale in gradi                  |

La mappa HDRI avvolge l'intera scena come una sfera. Il motore utilizza l'**importance sampling** per concentrare i raggi d'ombra verso le aree più luminose della mappa, accelerando drasticamente la convergenza.

### Suggerimenti per l'illuminazione HDRI

- I file HDRI sono tipicamente nel formato `.hdr` (Radiance) o `.exr` (OpenEXR).
- HDRIs gratuiti sono disponibili su siti come Poly Haven (licenza CC0).
- Usa `intensity` per schiarire o scurire l'ambiente senza modificare il file. Valori di 0.5--2.0 sono tipici.
- Usa `rotation` per ruotare l'ambiente in modo che il sole si trovi dove desideri rispetto alla tua scena.
- L'illuminazione HDRI fornisce un'illuminazione morbida e naturale con gradienti di colore complessi. Spesso è l'unica sorgente luminosa necessaria per scene all'aperto.

---

## 7.5 Profondità di Campo (Depth of Field)

Nel mondo reale, l'obiettivo di una fotocamera mette a fuoco a una distanza specifica. Gli oggetti a quella distanza sono nitidi; gli oggetti più vicini o più lontani sono sfocati. Questo effetto è chiamato **profondità di campo** (DOF).

```yaml
camera:
  position: [0, 1.5, -5]
  look_at: [0, 0.8, 0]
  fov: 40
  aperture: 0.15
  focal_dist: 5.0
```

| Parametro    | Predefinito | Descrizione                                          |
|--------------|-------------|------------------------------------------------------|
| `aperture`   | `0.0`       | Diametro dell'obiettivo (0 = tutto a fuoco)          |
| `focal_dist` | `1.0`       | Distanza dalla fotocamera alla quale gli oggetti sono nitidi |

### Come funziona

- `aperture: 0` (il predefinito) produce una fotocamera a foro stenopeico perfetta -- tutto è a fuoco indipendentemente dalla distanza.
- `aperture > 0` simula un obiettivo reale. Più grande è l'apertura, più ridotta sarà la profondità di campo (più sfocatura per gli oggetti fuori fuoco).
- `focal_dist` imposta la distanza di messa a fuoco. Gli oggetti esattamente a questa distanza dalla fotocamera saranno perfettamente nitidi.

### Guida pratica

1. Imposta `focal_dist` sulla distanza tra la fotocamera e il tuo soggetto. Il vettore da `position` a `look_at` ha questa lunghezza.
2. Inizia con un'apertura piccola (0.05--0.1) e aumentala fino a ottenere la sfocatura desiderata.
3. La DOF richiede **più campioni** per un risultato pulito. Usa almeno 64 SPP; 256+ è raccomandato per la produzione.

### Esempio: Mettere a fuoco la sfera centrale

```yaml
camera:
  position: [0, 1, -6]
  look_at: [0, 0.5, 0]
  fov: 45
  aperture: 0.12
  focal_dist: 6.0       # Distanza dalla fila dei soggetti
```

Gli oggetti più vicini e più lontani dalla fotocamera rispetto a 6 unità appariranno sfocati, con una sfocatura crescente man mano che si allontanano dal piano focale.

---

## 7.6 Fotocamere multiple con nome

Puoi definire diverse fotocamere in un unico file di scena e passare da una all'altra tramite la riga di comando senza modificare il file YAML.

```yaml
cameras:
  - name: "wide"
    position: [0, 3, -8]
    look_at: [0, 1, 0]
    fov: 60

  - name: "closeup"
    position: [1, 1.5, -3]
    look_at: [0.5, 0.8, 0]
    fov: 30
    aperture: 0.1
    focal_dist: 3.5

  - name: "topdown"
    position: [0, 8, 0.01]
    look_at: [0, 0, 0]
    fov: 45
```

Usa la chiave `cameras:` (plurale, lista) invece della singolare `camera:`. Ogni fotocamera deve avere un `name:` unico.

### Selezionare una fotocamera dalla riga di comando (CLI)

```
3d-ray -i scena.yaml --camera wide
3d-ray -i scena.yaml --camera closeup
3d-ray -i scena.yaml -c 2            # Tramite indice base zero (topdown)
```

### Elencare le fotocamere disponibili

```
3d-ray -i scena.yaml --list-cameras
```

Questo stampa i nomi e gli indici di tutte le fotocamere definite senza avviare il rendering.

Quando esistono più fotocamere e non viene fornito il flag `--camera`, il motore utilizza la **prima fotocamera** della lista e stampa un avviso.

> **Nota:** La chiave singola `camera:` (senza lista) funziona ancora per scene con una sola fotocamera. Se sono presenti sia `camera:` che `cameras:`, `cameras:` ha la precedenza.

---

## 7.7 Esempio Completo: Paesaggio all'Ora d'Oro

```yaml
# golden-hour.yaml
# Una scena all'aperto con cielo gradiente, disco solare, DOF e fotocamere multiple.

world:
  ambient_light: [0.02, 0.015, 0.01]
  sky:
    type: "gradient"
    zenith_color: [0.15, 0.25, 0.55]
    horizon_color: [0.95, 0.65, 0.3]
    ground_color: [0.2, 0.15, 0.1]
    sun:
      direction: [-0.8, -0.2, 0.6]
      color: [1.0, 0.78, 0.42]
      intensity: 14.0
      size: 4.0
      falloff: 24.0

cameras:
  - name: "landscape"
    position: [0, 1.5, -8]
    look_at: [0, 0.8, 0]
    fov: 55

  - name: "macro"
    position: [1, 0.8, -3]
    look_at: [0.5, 0.6, 0]
    fov: 30
    aperture: 0.15
    focal_dist: 3.5

  - name: "dramatic"
    position: [-2, 0.4, -4]
    look_at: [0, 0.5, 0]
    fov: 70

materials:
  - id: "ground"
    type: "disney"
    color: [0.35, 0.28, 0.18]
    roughness: 0.85

  - id: "stone"
    type: "disney"
    color: [0.55, 0.52, 0.48]
    roughness: 0.65

  - id: "grass"
    type: "disney"
    color: [0.18, 0.35, 0.08]
    roughness: 0.8
    subsurface: 0.15

  - id: "gold_sphere"
    type: "disney"
    color: [1.0, 0.76, 0.33]
    metallic: 1.0
    roughness: 0.05

  - id: "glass_sphere"
    type: "dielectric"
    refraction_index: 1.52

entities:
  # Piano del suolo
  - type: "infinite_plane"
    point: [0, 0, 0]
    normal: [0, 1, 0]
    material: "ground"

  # Alcune pietre
  - type: "sphere"
    center: [-1.5, 0.25, 1]
    radius: 0.25
    material: "stone"
    scale: [1, 0.6, 1]

  - type: "sphere"
    center: [2, 0.2, 2]
    radius: 0.2
    material: "stone"
    scale: [1.2, 0.5, 0.9]

  # Oggetti principali
  - type: "sphere"
    center: [0, 0.5, 0]
    radius: 0.5
    material: "gold_sphere"

  - type: "sphere"
    center: [1.2, 0.35, -0.5]
    radius: 0.35
    material: "glass_sphere"

  # Ciuffi d'erba (piccole sfere)
  - type: "sphere"
    center: [-0.8, 0.08, -0.5]
    radius: 0.08
    material: "grass"

  - type: "sphere"
    center: [0.5, 0.06, 0.8]
    radius: 0.06
    material: "grass"

lights:
  # Il cielo gradiente + il sole sono la sorgente principale.
  # Aggiungiamo un sottile filler per schiarire le ombre più profonde.
  - type: "directional"
    direction: [0.5, -0.5, -0.3]
    color: [0.4, 0.5, 0.7]
    intensity: 0.4
```

### Renderizzare le tre fotocamere

```
3d-ray -i golden-hour.yaml -c landscape -w 1920 -H 800 -s 128 -d 30
3d-ray -i golden-hour.yaml -c macro -w 1200 -H 800 -s 256 -d 30
3d-ray -i golden-hour.yaml -c dramatic -w 1920 -H 800 -s 128 -d 30
```

La fotocamera "macro" ha la DOF abilitata -- la sfera d'oro sarà nitida mentre lo sfondo sfumerà morbidamente.

---

## Cosa hai imparato

- Il cielo **flat** utilizza il colore di background ed è ideale per scene in studio.
- Il cielo **gradient** fornisce una sfumatura verticale a tre bande; l'aggiunta di un disco `sun:` lo trasforma in una sorgente luminosa completa per esterni.
- Le mappe **HDRI** forniscono un'illuminazione ambientale fotorealistica con importance sampling.
- La **profondità di campo** (DOF) è controllata da `aperture` (dimensione dell'obiettivo) e `focal_dist` (distanza di messa a fuoco). Apertura più grande = più sfocatura.
- Le **fotocamere multiple** consentono di definire diversi punti di vista e passare dall'uno all'altro con `--camera nome` sulla riga di comando.

---

[Precedente: Padronanza dell'illuminazione](./06-lighting.md) | [Successivo: Constructive Solid Geometry (CSG)](./08-csg.md) | [Indice del Tutorial](./README.md)
