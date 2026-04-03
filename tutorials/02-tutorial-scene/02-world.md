# 2. Sezione `world`

Definisce l'ambiente globale della scena.

```yaml
world:
  ambient_light: [0.05, 0.05, 0.08]   # Luce ambiente omnidirezionale
  background:    [0.1, 0.1, 0.15]     # Colore di sfondo (se sky è assente)
  ground:
    type: "infinite_plane"
    material: "pavimento"
    y: 0
  sky:
    type: "gradient"
    # ... vedi sotto
```

| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `ambient_light` | `[R, G, B]` | `[0.05, 0.05, 0.08]` | Luce omnidirezionale di fill che illumina tutte le superfici uniformemente, indipendentemente dalla direzione. |
| `background` | `[R, G, B]` | `[0.5, 0.7, 1.0]` | Colore dei raggi che escono dalla scena senza colpire nulla. Usato solo se `sky` è assente. |
| `ground` | oggetto | — | Piano infinito autogenerato. Richiede un `material` definito nella sezione `materials`. |
| `sky` | oggetto | — | Configurazione del cielo. Se presente, sovrascrive `background`. Vedi sotto. |

## Come funzionano le sorgenti di illuminazione

Il renderer ha più fonti di luce che lavorano insieme:

| Sorgente | Cosa controlla | Effetto |
|----------|---------------|---------|
| `background` / `sky` | Colore del cielo | I raggi che rimbalzano sugli oggetti e "escono" dalla scena raccolgono questo colore. Agisce come una sorgente di luce ambiente globale (Global Illumination). Con `sky: { type: "gradient" }` il colore varia in base alla direzione del raggio. |
| `ambient_light` | Luce piatta di riempimento | Viene **sommata** alla luce diretta su ogni punto colpito. Aiuta a schiarire le ombre. |
| `lights:` | Luci esplicite | Point, Directional, Spot, Area — illuminano selettivamente la scena. |
| Materiali `emissive` | Oggetti luminosi | Emettono luce propria che si propaga tramite rimbalzi indiretti. |

## Esempi di ambienti

**Scena notturna / studio nero (solo luci esplicite):**
```yaml
world:
  ambient_light: [0.0, 0.0, 0.0]
  background: [0.0, 0.0, 0.0]
```

**Scena diurna all'aperto (background piatto legacy):**
```yaml
world:
  ambient_light: [0.05, 0.05, 0.08]
  background: [0.4, 0.6, 1.0]
```

**Scena diurna con gradient sky (raccomandato per outdoor):**
```yaml
world:
  ambient_light: [0.05, 0.05, 0.08]
  sky:
    type: "gradient"
    zenith_color:  [0.10, 0.30, 0.80]
    horizon_color: [0.65, 0.80, 1.00]
    ground_color:  [0.30, 0.28, 0.22]
```

**Atmosfera calda al tramonto:**
```yaml
world:
  ambient_light: [0.05, 0.03, 0.01]
  background: [0.8, 0.4, 0.1]
```

---

## 2.1 Gradient Sky e Sun Disk

Il gradient sky sostituisce il background piatto con un cielo procedurale che varia colore in base alla direzione del raggio. Produce illuminazione globale molto più naturale: la luce dal cielo è azzurra in alto e calda all'orizzonte, colorando le ombre e i rimbalzi in modo realistico.

```yaml
world:
  sky:
    type: "gradient"
    zenith_color:  [0.10, 0.30, 0.80]   # Colore allo zenit (dritto in su) (blu scuro)
    horizon_color: [0.65, 0.80, 1.00]   # Colore all'orizzonte (azzurro chiaro)
    ground_color:  [0.30, 0.25, 0.20]   # Colore sotto l'orizzonte (marrone scuro)
    sun:
      direction:  [-0.5, -0.8, -0.3]    # Verso cui punta il sole (normalizzato)
      color:      [1.0, 0.95, 0.85]
      intensity:  12.0
      size:       2.5                    # Raggio angolare in gradi
      falloff:    40.0                   # Estensione del glow attorno al disco
```

### Parametri Sky

| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `type` | stringa | — | `"gradient"` per attivare il cielo procedurale. Qualsiasi altro valore (o campo assente) → background piatto legacy. |
| `zenith_color` | `[R, G, B]` | `[0.10, 0.30, 0.80]` | Colore dello zenit (dritto in alto). |
| `horizon_color` | `[R, G, B]` | `[0.70, 0.85, 1.00]` | Colore all'orizzonte. La transizione usa `sqrt(y)` per un'ampia fascia orizzontale realistica. |
| `ground_color` | `[R, G, B]` | `[0.30, 0.25, 0.20]` | Colore sotto l'orizzonte (riflesso del terreno nel cielo). |
| `sun` | oggetto | — | Opzionale: configura un disco solare procedurale con glow. |

### Parametri Sun Disk

| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `direction` | `[X, Y, Z]` | — | Direzione DA cui arriva la luce solare (stessa convenzione della Directional Light). Viene normalizzata. |
| `color` | `[R, G, B]` | `[1, 1, 1]` | Colore del disco solare. |
| `intensity` | float | `10.0` | Moltiplicatore di luminosità del sole. Valori tipici: 5–50. |
| `size` | float | `3.0` | Diametro angolare del disco in gradi. Sole reale ≈ 0.53°. Valori artistici: 1–6°. |
| `falloff` | float | `32.0` | Esponente del glow attorno al disco. Basso (8) = alone ampio, alto (128) = alone stretto. |

> **Nota:** Usa `background` per scene indoor o da studio (colore piatto). Usa `sky` per scene outdoor (gradiente + sun disk). Non serve specificare entrambi: se `sky` è presente, `background` viene ignorato.

> **Sun disk vs Directional Light:** Il sun disk è puramente **visuale** — è il colore che i raggi ricevono quando escono dalla scena in quella direzione. Per avere illuminazione diretta (ombre, highlight), aggiungi una `directional` light con la stessa `direction` nella sezione `lights:`.

### Preset Sky per ora del giorno

**Mezzogiorno (sole alto, cielo pulito):**
```yaml
  sky:
    type: "gradient"
    zenith_color:  [0.10, 0.30, 0.80]
    horizon_color: [0.65, 0.80, 1.00]
    ground_color:  [0.30, 0.28, 0.22]
    sun:
      direction: [-0.2, -1.0, -0.3]
      color: [1.0, 0.98, 0.92]
      intensity: 15.0
      size: 2.0
      falloff: 48.0
```

**Golden Hour (sole basso, luce calda):**
```yaml
  sky:
    type: "gradient"
    zenith_color:  [0.15, 0.25, 0.55]
    horizon_color: [0.85, 0.55, 0.25]
    ground_color:  [0.20, 0.15, 0.10]
    sun:
      direction: [-0.8, -0.25, -0.5]
      color: [1.0, 0.85, 0.5]
      intensity: 20.0
      size: 4.0
      falloff: 24.0
```

**Tramonto drammatico:**
```yaml
  sky:
    type: "gradient"
    zenith_color:  [0.08, 0.05, 0.20]
    horizon_color: [0.95, 0.30, 0.05]
    ground_color:  [0.10, 0.05, 0.02]
    sun:
      direction: [-1.0, -0.08, -0.2]
      color: [1.0, 0.4, 0.05]
      intensity: 30.0
      size: 6.0
      falloff: 12.0
```

**Notte serena (senza sole):**
```yaml
  sky:
    type: "gradient"
    zenith_color:  [0.01, 0.01, 0.04]
    horizon_color: [0.04, 0.04, 0.08]
    ground_color:  [0.01, 0.01, 0.02]
```

---

## 2.2 HDRI / IBL (Environment Map)

L'Image-Based Lighting (IBL) usa una fotografia HDR a 360° dell'ambiente reale come sorgente di illuminazione. Ogni raggio che esce dalla scena campiona la mappa HDR, producendo riflessi, rifrazioni e illuminazione globale catturate dalla realtà — il livello più alto di realismo per l'ambiente.

Il motore supporta file in formato **Radiance HDR** (`.hdr`), scaricabili gratuitamente da [Poly Haven](https://polyhaven.com/hdris). File 2K o 4K sono raccomandati.

```yaml
world:
  ambient_light: [0.0, 0.0, 0.0]      # Zero — tutta la luce dall'HDRI
  sky:
    type: "hdri"
    path: "hdri/studio_small_09_4k.hdr" # Relativo al file YAML
    intensity: 1.0                       # Esposizione (>1 più luminoso)
    rotation: 90                         # Rotazione Y in gradi (0–360)
```

### Parametri HDRI

| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `type` | stringa | — | `"hdri"` per attivare l'environment map. |
| `path` | stringa | — (**obbligatorio**) | Percorso del file `.hdr`. Relativo alla directory del file YAML. |
| `intensity` | float | `1.0` | Moltiplicatore di luminosità. >1 per ambienti scuri, <1 per sovraesposti. |
| `rotation` | float | `0.0` | Rotazione dell'ambiente attorno all'asse Y in gradi. Utile per allineare il sole o la finestra dell'HDRI con la scena. |

> **HDRI vs Gradient Sky vs Background piatto:**
> - **`background`** — colore piatto, per interni chiusi e studi neri.
> - **`sky: { type: "gradient" }`** — cielo procedurale con gradiente e sun disk, per outdoor stilizzati.
> - **`sky: { type: "hdri" }`** — environment map fotografica, per il massimo realismo. Particolarmente efficace con sfere metalliche e vetro.

> **Luci esplicite con HDRI:** L'HDRI fornisce illuminazione globale tramite i rimbalzi del path tracer. Non servono luci esplicite (`lights: []`), ma puoi aggiungerne per enfatizzare ombre direzionali o highlight specifici. Se la sezione `lights:` è completamente omessa dal YAML, il motore aggiunge luci default; usa `lights: []` esplicito per avere solo luce HDRI.

> **Performance:** Il file HDR viene caricato una volta al load della scena. Il sampling durante il render è una singola lettura con bilinear filtering — costo trascurabile.

---

---

[← Torna all'indice](../02-tutorial-scene.md)
