# Capitolo 7: Cielo, ambiente ed effetti fotocamera

Il cielo è la più grande sorgente luminosa in qualsiasi scena
all'aperto. Un ambiente ben configurato può trasformare un rendering
piatto in qualcosa di veramente fotografico. Il sistema sky/environment
di 3D-Ray include cinque modelli di cielo, image-based lighting con
sun extraction, portal light per interni, aerial perspective fisica.
Questo capitolo tratta anche la profondità di campo (depth of field) e
le configurazioni multi-fotocamera.

---

## 7.1 Modelli di Cielo (Sky Models)

Il cielo è l'emettitore globale d'ambiente in 3D-Ray. Determina il
colore che riceve un raggio quando non colpisce alcun oggetto e fugge
all'infinito, e partecipa alla Next Event Estimation (NEE) come
sorgente luminosa quando la sua radianza è non-zero. Sono supportati
cinque modelli, configurabili sotto `world: > sky:`.

| Modello          | Descrizione                                                                  | Quando usarlo                              |
|------------------|------------------------------------------------------------------------------|--------------------------------------------|
| `flat`           | Colore uniforme su tutta la sfera (default)                                  | Studio, interni, Cornell-box, fill-only    |
| `gradient`       | Gradiente verticale a tre bande con disco solare opzionale                   | Preview stilizzate, look outdoor rapido    |
| `hosek_wilkie`   | Cielo diurno sereno analitico (Hosek-Wilkie 2012). Alias YAML: `physical`    | Luce diurna esterna, il più vicino ai cieli misurati |
| `preetham`       | Cielo diurno sereno analitico (Preetham 1999), il modello precedente         | Riferimento, confronti                     |
| `nishita`        | Scattering fisico Rayleigh+Mie con LUT precomputata                          | Alba / tramonto, aerial perspective        |
| `hdri`           | Immagine HDR equirettangolare (`.hdr` o `.exr`)                              | Photoreal product / VFX / archviz          |

Quando non è presente il blocco `sky:`, il motore usa un cielo piatto
con il default daylight blu `[0.5, 0.7, 1.0]`. Tutti i modelli
condividono una serie di feature "globali" (visibility flags, background
plate, orientation) — vedi §7.7 sotto.

> Il cielo è l'**unico** termine ambientale. L'illuminazione indiretta
> viene dalla GI path-traced — non esiste un coefficiente ambient
> separato. Se vuoi un effetto "fill light", usa un `flat` con un colore
> basso neutro, o un `gradient` con zenith fioco.

> **Convenzione sole.** Da questa versione in poi, `sun.direction` punta
> **VERSO il sole** (convenzione uniforme su tutti i modelli sky e sulla
> luce `PhysicalSun`). Scene legacy che usavano la vecchia convenzione
> (dove `direction` era la direzione di propagazione) richiedono di
> invertire il vettore.

---

## 7.2 Flat Sky (default)

```yaml
world:
  sky:
    type: "flat"
    color: [0.5, 0.65, 0.9]
```

Un cielo flat restituisce il suo `color` per ogni raggio in fuga e
partecipa a NEE tramite campionamento uniforme della sfera (pdf =
1/(4π)) quando la luminanza è positiva, fornendo illuminazione ambientale
uniforme da ogni direzione. Imposta `color:
[0, 0, 0]` per scene black-void stile Cornell-box; il loader esclude
automaticamente da NEE un cielo flat con luminanza zero.

---

## 7.3 Gradient Sky con Disco Solare

Gradiente verticale a tre bande (zenith → orizzonte → ground) con sole
analitico opzionale. Il sole è agganciato automaticamente come luce
`PhysicalSun` separata con cone sampling e (opzionalmente) limb
darkening Hestroffer.

```yaml
world:
  sky:
    type: "gradient"
    zenith_color:  [0.20, 0.35, 0.75]
    horizon_color: [0.85, 0.75, 0.55]
    ground_color:  [0.30, 0.25, 0.20]
    sun:
      direction:      [0.5, 0.8, -0.3]    # direzione VERSO il sole
      color:          [1.0, 0.95, 0.80]
      intensity:      12.0
      angular_radius: 0.265                # semiangolo in gradi (sole reale)
      shadow_samples: 4                    # campioni stratificati per le ombre soft
```

| Parametro sole     | Default  | Descrizione                                          |
|--------------------|----------|------------------------------------------------------|
| `direction`        | --       | Direzione *VERSO* il sole (posizione nel cielo)      |
| `color`            | `[1,1,1]`| Tinta del disco                                      |
| `intensity`        | `10.0`   | Moltiplicatore di luminosità                         |
| `angular_radius`   | `0.265°` | Semiangolo in gradi (sole reale)                     |
| `size`             | `3.0°`   | Diametro totale — usato solo se `angular_radius` ≤ 0 |
| `shadow_samples`   | `4`      | Campioni stratificati per il `PhysicalSun` accoppiato|
| `visible_to_camera`| `true`   | Quando `false` nasconde il disco dai raggi primari   |

### Preset rapidi

```yaml
# Golden hour
sun:
  direction:      [0.8, 0.15, -0.5]
  color:          [1.0, 0.78, 0.42]
  intensity:      14.0
  angular_radius: 1.5

# Mezzogiorno
sun:
  direction:      [-0.1, 1.0, -0.2]
  color:          [1.0, 0.98, 0.95]
  intensity:      12.0
  angular_radius: 0.5

# Notte con luna
sun:
  direction:      [-0.4, 0.6, -0.7]
  color:          [0.70, 0.75, 0.90]
  intensity:      3.0
  angular_radius: 0.8
```

---

## 7.4 Physical Sky - Hosek-Wilkie / Preetham

Cielo diurno sereno analitico. Un singolo knob `turbidity` controlla
l'intero look atmosferico - niente colori zenith/horizon/ground regolati
a mano. La direzione del sole controlla sia la posizione del disco sia la
distribuzione spaziale del cielo (schiarimento attorno al sole, blu allo
zenith).

```yaml
world:
  sky:
    type: "hosek_wilkie"             # oppure "preetham"
    turbidity: 3.0                   # 1 = aria pura, 3 = sereno, 6 = foschia, 10 = molta foschia
    ground_albedo: [0.25, 0.25, 0.22]
    units: "relative"                # oppure "photometric" (nit)
    intensity: 1.0
    sun:
      direction:       [-0.35, 0.78, 0.52]
      angular_radius:  0.265
      shadow_samples:  4
```

| Parametro        | Default      | Descrizione                                                              |
|------------------|--------------|--------------------------------------------------------------------------|
| `turbidity`      | `3.0`        | Torbidità di Linke (1-10): basso = pulito, alto = foschia                |
| `ground_albedo`  | `[0.3]`      | Albedo del terreno sotto l'orizzonte; tinge anche il cielo di Hosek-Wilkie |
| `units`          | `relative`   | `relative` (kcd/m², la scala storica) oppure `photometric` (nit)         |
| `intensity`      | `1.0`        | Moltiplicatore su radianza cielo + sole                                  |

**Due modelli.** `hosek_wilkie` (Hosek e Wilkie 2012) è adattato a cieli
misurati e regge anche con il sole basso; `preetham` (1999) è il modello
precedente, più luminoso a parità di torbidità e con una tinta rosata
vicino all'orizzonte, tenuto come riferimento.

**Il sole è coerente con il cielo.** Il disco è la luce solare al limite
dell'atmosfera (133.8 klux), attenuata lungo il suo percorso in
un'atmosfera con la polvere della stessa torbidità, distribuita sul
disco. In una giornata serena il sole dà al terreno circa quanto il cielo
con il sole a 15°, da tre a quattro volte tanto a 45°. Le ombre sono
profonde come in una fotografia di una giornata di sole.

**Esporre una scena diurna.** Con `units: "photometric"` il cielo è in
nit (un cielo sereno a mezzogiorno vale migliaia di nit, il disco del
sole circa 1.5×10⁹) e la camera prende un'esposizione fotografica,
`ev100: 15` per una giornata di sole:

```yaml
camera:
  position: [0, 1.6, -7]
  look_at:  [0, 0.6, 0]
  ev100: 15                          # la regola del "sunny 16"
```

Al caricamento la console riporta quello che il cielo dà a un piano
orizzontale (`Sky light: 75190 lux on the ground, 58593 of it from the
sun`). Vedi `scenes/review/daylight-units.yaml`.

**Sotto l'orizzonte** il cielo mostra un terreno di albedo
`ground_albedo` illuminato da quel cielo e da quel sole; una scena con un
suo piano di terra lo nasconde.

---

## 7.5 Nishita — Atmosfera Fisica

Single-scattering Rayleigh + Mie integrato attraverso un'atmosfera
Earth-realistic (pianeta 6360 km, scale height Rayleigh 8 km, Mie
1.2 km). A differenza dei modelli analitici sopra, Nishita deriva la
cromaticità di alba/tramonto dai primi principi — disco rosso, halo
arancione e zenith blu emergono dalla fisica, non da coefficienti
fittati.

```yaml
world:
  sky:
    type: "nishita"
    turbidity: 3.0                  # manopola della foschia: poca polvere (aria pulita a 3)
    units: "relative"               # oppure "photometric" (nit)
    intensity: 1.0
    sun:
      direction:       [-0.85, 0.12, 0.4]   # basso sull'orizzonte → palette sunset
      angular_radius:  0.265
      shadow_samples:  4
```

**Sole e unità.** L'illuminanza del sole al limite dell'atmosfera
alimenta sia lo scattering sia il disco: 20 nella scala `relative`,
133 800 lux in quella `photometric`. Il modello diffonde la luce una
volta sola: il suo cielo è più scuro rispetto al sole di uno misurato
(circa la metà), e per questo qui `turbidity` corrisponde a poca polvere
(con l'aerosol di una vera torbidità 3 l'orizzonte diventerebbe scuro).

**Performance.** La LUT di trasmittanza (16×64 float × 3 canali =
12 KB) è costruita una sola volta in costruzione (~20 ms). La radianza
per-direzione fa un'integrazione di view-ray a 16 passi con due lookup
LUT per passo — circa 3× un lookup Preetham. Trascurabile a budget di
render tipici.

**Quando scegliere Nishita su Hosek-Wilkie / Preetham:**

| Scenario                                  | Scelta migliore |
|-------------------------------------------|-----------------|
| Sole oltre 15° di elevazione, luce diurna | Hosek-Wilkie (il più vicino ai cieli misurati) |
| Alba, tramonto, crepuscolo                | Nishita         |
| Vuoi anche un'atmosfera partecipante      | Nishita (con il medium di §7.9) |
| Vedute di montagna con foschia atmosferica| Nishita         |
| Lighting da studio / render di interno    | `hdri` o `flat` |

---

## 7.6 HDRI Image-Based Lighting

```yaml
world:
  sky:
    type: "hdri"
    path: "textures/venice_sunset_2k.hdr"   # .hdr (Radiance) o .exr (OpenEXR)
    intensity: 1.0
    rotation: 45.0                          # rotazione asse Y legacy (gradi)
    sun:
      extract_from_hdri:  true              # auto-detect del sole e split
      extract_threshold:  50                # soglia di luminanza (× media HDRI). Default 50
      shadow_samples:     4
```

| Parametro   | Default | Descrizione                                                |
|-------------|---------|------------------------------------------------------------|
| `path`      | --      | Percorso a `.hdr` o `.exr` (risolto relativamente al YAML) |
| `intensity` | `1.0`   | Moltiplicatore esposizione                                 |
| `rotation`  | `0.0`   | Rotazione asse Y in gradi (legacy; preferisci `orientation:` — §7.7) |

La mappa HDRI avvolge l'intera scena come una sfera. Il motore
costruisce una CDF 2D pesata per luminanza al load time e
**importance-sampling** dei raggi shadow verso le aree più luminose. Il
supporto EXR copre lo scanline RGB (No compression / ZIP / ZIPS, half +
float).

### Campionamento dell'HDRI

L'HDRI è una luce: la next-event estimation la campiona in proporzione
alla sua luminosità e i campioni della BSDF si dividono il lavoro con
l'importance sampling multiplo. Il lato della luce campiona la mappa meno
la sua media (Karlík et al. 2019), così la parte uniforme del cielo resta
alla BSDF, che la trova bene, e i campioni di luce vanno ai punti
luminosi: meno rumore sugli specchi ruvidi e in ombra.

Un rimbalzo glossy che esce su un'HDRI che le luci non campionano (il
cielo nascosto ai raggi d'ombra, per esempio) legge invece un livello
mipmap pre-filtrato proporzionale alla larghezza del lobo della BSDF. Non
si usa per un'HDRI che è una luce: con il MIS il valore pre-filtrato non è
la radianza che il lato della luce stima, e l'immagine veniva troppo
chiara.

### Sun extraction

Quando `extract_from_hdri: true` è attivo, il loader scansiona l'HDRI
per il picco più luminoso, in-painta quei pixel con la media circolare
del background, e emette una luce `PhysicalSun` accoppiata con cone
sampling. Benefici:

- **Ombre nitide** — invece di penumbre multi-pixel da un singolo pixel
  HDRI luminoso.
- **~10× minor varianza NEE** per l'illuminazione solare diretta.
- **Clamp firefly indipendente** — puoi clampare aggressivamente il body
  del cielo senza smorzare il sole.

È il workflow standard di sun extraction per l'illuminazione image-based.

### Trovare la `rotation` giusta

L'intera larghezza equirect rappresenta 360° — sole al 25% dal bordo
sinistro = 90°, al 75% = 270°. Inizia con `rotation: 0`, regola a passi
di 45°, poi affina. Per orientation 3D completa (pitch + roll, non solo
yaw) usa il blocco `orientation:` in §7.7 sotto.

---

## 7.7 Feature Globali Sky: Visibility, Background, Orientation

Queste tre feature si applicano a ogni modello di cielo.

### Flag di visibilità (per categoria di raggio)

Ogni flag può essere disattivato per nascondere il cielo da una
categoria di raggi:

```yaml
world:
  sky:
    type: "hdri"
    path: "studio.hdr"
    visibility:
      camera:       true     # Raggi primari della camera
      diffuse:      true     # Bounce diffuse / sheen / SSS
      glossy:       true     # Bounce glossy / clearcoat
      transmission: true     # Rifrazioni attraverso vetro
      shadow:       true     # Raggi NEE shadow restituiscono radianza del cielo
    sun:
      visible_to_camera: false   # Nasconde il disco solare dalla camera (continua a illuminare)
```

Setup comuni:

- `camera: false` — nasconde l'HDRI dal background renderizzato
  continuando a illuminare la scena (alpha pulito per il compositing).
- `glossy: false` — rimuove l'HDRI dai materiali riflettenti (preview
  clay-render di metalli).
- `sun.visible_to_camera: false` — setup off-camera key-light; il sole
  agisce come luce dura ma non sovraespone il cielo nel frame.

**Parità con il ground.** Le stesse flag `visibility.*` esistono anche
su `world.ground:` — vedi il
[reference ground](../../reference/riferimento-scene.md). Un ground con
`visibility.shadow: false` mantiene il pavimento visibile alla camera
ma lascia passare i raggi NEE shadow attraverso (lookdev pulito di
"pavimento infinito senza contact shadow"); `visibility.camera: false`
produce un pavimento invisibile che però continua a far rimbalzare
la luce indiretta.

### Background plate

Un sub-blocco `background:` separato permette di illuminare la scena con
un environment e mostrare alla camera una plate diversa — workflow
standard product / VFX.

```yaml
world:
  sky:
    type: "hdri"
    path: "lighting.hdr"            # Sorgente di illuminazione primaria
    background:
      type: "hdri"
      path: "background.hdr"        # Mostrato ai raggi camera
```

Il blocco `background:` accetta gli stessi campi del `sky:` di livello
superiore, incluso il suo `path`, `intensity`, `rotation`, e qualsiasi
tipo di modello (`flat`, `gradient`, `preetham`, ecc.).

### Orientation (quaternion / Euler XYZ)

Sostituisce il legacy `rotation:` solo-Y con un'orientation 3D completa:

```yaml
world:
  sky:
    type: "hdri"
    path: "studio.hdr"
    orientation:
      euler:      [10, 45, 0]              # Euler XYZ intrinseco in gradi
      # OPPURE
      quaternion: [0, 0.38, 0, 0.92]       # XYZW; il quaternion vince se entrambi presenti
```

Il campo legacy `rotation:` è ancora onorato quando `orientation:` è
assente.

---

## 7.8 Portal Light — Scene di Interno con Finestre

I render di interni con finestre/lucernari soffrono tradizionalmente di
varianza massiccia: i campioni NEE puntano in direzioni casuali sulla
CDF del cielo, ma ≥95% colpisce i muri. Le **portal light** restringono
NEE al rettangolo della finestra — riduzione di varianza ~10× istantanea
a parità di sample.

```yaml
lights:
  - type: "portal"           # alias: "portal_light"
    anchor: [3.0, 1.2, -2.5] # un angolo del rettangolo finestra
    u: [0.0, 0.0, 2.5]       # lato lungo U (larghezza finestra)
    v: [0.0, 1.2, 0.0]       # lato lungo V (altezza finestra)
    shadow_samples: 8        # default 8
```

Il portal è **intangibile**: nessuna geometria, invisibile alla camera e
ai raggi BSDF. Contribuisce solo via NEE. Orienta `u, v` in modo che il
prodotto vettoriale `u × v` punti VERSO il cielo.

Algoritmo: Bitterli, Wyman, Pharr (2015) "Portal-Masked Environment Map
Sampling".

---

## 7.9 Aerial Perspective — Nishita Atmospheric Medium

Aerial perspective fisica: la geometria distante acquista una tinta
bluastra (scattering Rayleigh) e perde
luminanza (estinzione). Il medium condivide le costanti fisiche con
`NishitaSky`, così atmosfera e cielo coincidono:

```yaml
world:
  sky:
    type: "nishita"
    sun:
      direction: [-0.45, 0.55, 0.7]
  medium:
    type: "atmosphere"           # alias: "nishita", "aerial_perspective"
    world_scale: 1000.0          # metri per unità mondo (1000 = "1 wu : 1 km")
    sea_level_y: 0.0             # Y world dell'altitudine 0
    dust_density: 1.2            # densità Mie (0 = puro, 1 = pulito, >1 = inquinato)
    air_density: 1.0             # densità dell'aria (Rayleigh), 1 = atmosfera standard
    # l'aria diffonde con la phase function di Rayleigh; `phase` imposta
    # quella della polvere (default Henyey-Greenstein g = 0.76, in avanti)
```

`world_scale` è il knob chiave del mapping — scegli in base alla scala
della scena (1000 per scene "scala stadio", 200 per "isolato urbano", 50
per "singola stanza"). L'optical depth ha forma chiusa per aria, polvere
e ozono, quindi la trasmittanza è esatta e le distanze sono campionate
esattamente. Il mezzo è aerial perspective: agisce tra l'occhio e le
superfici che vede, mentre i raggi che vanno verso il cielo lo attraversano
intatti (il cielo Nishita contiene già quella luce; contarla due volte
sbiadiva il cielo).

---

## 7.10 Profondità di Campo (Depth of Field)

Nel mondo reale, l'obiettivo di una fotocamera mette a fuoco a una
distanza specifica. Gli oggetti a quella distanza sono nitidi; più
vicini o più lontani sono sfocati. Questo è la **profondità di campo**
(DOF).

```yaml
cameras:
  - name: "main"
    position: [0, 1.5, -5]
    look_at: [0, 0.8, 0]
    fov: 40
    aperture: 0.15
    focal_dist: 5.0
```

| Parametro    | Default | Descrizione                                              |
|--------------|---------|----------------------------------------------------------|
| `aperture`   | `0.0`   | Diametro obiettivo (0 = pinhole, tutto a fuoco)          |
| `focal_dist` | `1.0`   | Distanza dalla camera alla quale gli oggetti sono nitidi |
| `focal_pos`  | _nessuno_| Alternativa: fuoco su un punto 3D world (vedi sotto)    |

### Fuoco su un punto — `focal_pos`

È possibile specificare il **punto focale** direttamente invece della distanza:

```yaml
cameras:
  - name: "main"
    position: [0, 1.5, -6]
    look_at: [0, 0.5, 0]
    fov: 45
    aperture: 0.12
    focal_pos: [0.0, 0.5, 0.0]    # coordinata world esatta del soggetto
```

Il loader proietta il vettore camera→focal-point sull'asse ottico,
quindi `focal_pos` definisce il *piano* di fuoco, non una sfera
euclidea — un focal point a `(3, 4, -5)` con la camera all'origine che
guarda lungo `-Z` produce focus distance `5`, non `√50`. Replica ogni
renderer di produzione. Quando sono presenti sia `focal_pos` che
`focal_dist`, `focal_pos` vince (un messaggio info è loggato).

### Guida pratica

- Inizia con un'aperture piccola (0.05–0.1) e aumenta finché ottieni la
  blur desiderata.
- DOF richiede più sample per risultato pulito. Almeno 64 SPP; 256+
  consigliati per produzione.

### Un obiettivo vero: focale e f-stop

Al posto di `fov` e `aperture` puoi descrivere l'obiettivo come un
fotografo. Con la scena modellata in metri:

```yaml
cameras:
  - name: "ritratto"
    position: [0, 1.2, 0]
    look_at: [0, 0.9, -2.5]
    focal_length: 50        # mm; sensor_width vale 36 per default (pieno formato)
    f_stop: 1.4             # pupilla = 50 / 1.4 = 35.7 mm
    focal_pos: [0, 0.9, -2.5]
```

`focal_length` e `sensor_width` fissano il campo visivo (la larghezza del
sensore copre la larghezza dell'immagine), `f_stop` il diametro della
pupilla. Una scena modellata in centimetri aggiunge `meters_per_unit: 0.01`,
così f/1.4 resta una pupilla di 35.7 mm. Dimezzare il numero f raddoppia la
pupilla e la sfocatura; un obiettivo più lungo allo stesso numero f sfoca di
più lo sfondo, come su una fotocamera vera.

### Forma del bokeh

Le luci fuori fuoco prendono la forma del diaframma. `aperture_blades: 6`
dà esagoni (da 5 a 9 lamelle sono tipiche), `aperture_rotation` ruota il
poligono e `aperture_ratio: 0.5` dà gli ovali verticali di un obiettivo
anamorfico. Le luci piccole e molto luminose chiedono molti campioni: ogni
pixel del disco di bokeh riceve luce solo dai campioni di lente che colpiscono
la sorgente. `scenes/showcases/camera-physical.yaml` le mostra tutte.

### Bilanciamento del bianco e trasformazione di visualizzazione

`white_balance: 3200` (kelvin) fa apparire bianca una luce al tungsteno a
3200 K, come una fotocamera impostata su "tungsteno": valori più bassi
raffreddano l'immagine, più alti la scaldano, `6504` non cambia nulla.
L'immagine per lo schermo la produce poi la trasformazione di visualizzazione
scelta da riga di comando: `--view agx` (il default) porta la luce intensa e
satura verso il bianco come fa la pellicola, `--view aces` dà l'aspetto ACES
1.x con più contrasto, `--view standard` mostra i valori lineari con la sola
curva sRGB. `--exposure` (in stop) si applica prima della trasformazione.

---

## 7.11 Motion Blur

L'otturatore di una camera reale resta aperto per un tempo finito, così tutto
ciò che si muove durante l'esposizione lascia una scia. 3D-Ray lo riproduce
fisicamente: la scena si anima su una timeline normalizzata `[0, 1]`, e la
`shutter: [open, close]` della camera è la fetta di quella timeline che
l'esposizione integra.

Anima un'entità con una lista `motion:`. La sua posa base
`translate`/`rotate`/`scale` è il keyframe a `time: 0`; ogni voce aggiunge una
posa al proprio tempo (le componenti omesse ereditano la posa base):

```yaml
camera:
  position: [0, 2, 8]
  look_at: [0, 1, 0]
  shutter: [0.0, 1.0]          # esposizione piena — integra tutto il moto

entities:
  # Una sfera che sfreccia a destra.
  - type: "sphere"
    center: [0, 0, 0]
    radius: 0.6
    material: "chrome"
    translate: [-2, 0.6, 0]    # posa a time 0
    motion:
      - { time: 1.0, translate: [2, 0.6, 0] }   # posa a time 1
```

Più un oggetto è veloce tra i suoi keyframe, più lunga è la scia. Un
**otturatore più stretto è un'esposizione più breve**: `shutter: [0.45, 0.55]`
espone solo il decimo centrale del moto, quindi le scie si accorciano ~10× —
utile per un confronto A/B "congelato vs sfumato" da due camere sulla stessa
animazione.

Anche la rotazione si sfuma, sull'arco più breve tra i keyframe:

```yaml
  - type: "box"
    material: "checker"
    scale: [1.4, 1.4, 1.4]
    translate: [3, 0.7, 0]
    motion:
      - { time: 1.0, translate: [3, 0.7, 0], rotate: [0, 150, 0] }
```

Puoi animare la **camera** allo stesso modo — una lista `motion:` di keyframe
`position`/`look_at`/`vup`/`fov` dà un blur da camera in movimento (carrellata o
panoramica veloce):

```yaml
camera:
  position: [0, 2, 8]
  look_at: [0, 1, 0]
  shutter: [0.0, 1.0]
  motion:
    - { time: 1.0, position: [1.5, 2, 8] }   # leggera carrellata sull'esposizione
```

Note:
- Il motion blur richiede più sample di un fotogramma fermo per una scia pulita —
  trattalo come la DOF e usa 256+ SPP per la produzione.
- `motion:` è supportato sulle entità top-level di qualsiasi tipo (è ignorato sui
  *figli* di group/instance — anima invece il genitore).
- Quando nulla è animato lo shutter è ignorato e il render è identico a un
  fotogramma fermo.

Vedi le scene `motion-blur-showcase.yaml` e `motion-blur-billiard-showcase.yaml`,
e `docs/technical/motion-blur.md` per l'algoritmo.

---

## 7.12 Camere Multiple Nominate

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
```

Usa la chiave `cameras:` (plurale, lista) invece del singolare
`camera:`. Ogni camera deve avere un `name:` unico. Selezione da CLI:

```
RayTracer -i scene.yaml --camera wide
RayTracer -i scene.yaml -c 1               # indice zero-based
RayTracer -i scene.yaml --list-cameras     # stampa nomi + indici, no render
```

Quando esistono più camere e nessun flag `--camera` è fornito, il motore
usa la prima e stampa un warning.

---

## 7.13 Esempio Completo: Golden Hour Landscape

```yaml
# golden-hour.yaml — scena outdoor con sky Hosek-Wilkie, DOF, multi-camera.

world:
  sky:
    type: "hosek_wilkie"
    turbidity: 3.5
    ground_albedo: [0.3, 0.28, 0.22]
    intensity: 1.0
    sun:
      direction:      [0.8, 0.15, -0.5]    # sole basso caldo, dietro-destra
      angular_radius: 1.5                   # leggermente ingrandito per glow cinematico
      shadow_samples: 4

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
    focal_pos: [0.0, 0.5, 0.0]

materials:
  - id: "ground"
    type: "disney"
    color: [0.35, 0.28, 0.18]
    roughness: 0.85

  - id: "gold_sphere"
    type: "disney"
    color: [1.0, 0.76, 0.33]
    metallic: 1.0
    roughness: 0.05

  - id: "glass_sphere"
    type: "disney"
    color: [0.95, 0.95, 0.95]
    spec_trans: 1.0
    ior: 1.52
    roughness: 0.02

entities:
  - type: "infinite_plane"
    point: [0, 0, 0]
    normal: [0, 1, 0]
    material: "ground"

  - type: "sphere"
    center: [0, 0.5, 0]
    radius: 0.5
    material: "gold_sphere"

  - type: "sphere"
    center: [1.2, 0.35, -0.5]
    radius: 0.35
    material: "glass_sphere"
```

```
RayTracer -i golden-hour.yaml -c landscape -w 1920 -H 800 -s 256 -d 6
RayTracer -i golden-hour.yaml -c macro -w 1200 -H 800 -s 1024 -d 8 -S 4
```

---

## Cosa hai imparato

- **Cinque modelli di cielo** coprono ogni scenario di produzione:
  `flat` (uniforme), `gradient` (stilizzato),
  `preetham`/`hosek_wilkie` (daylight analitico clear-sky), `nishita`
  (Rayleigh+Mie fisico per tramonti e aerial perspective), `hdri`
  (image-based).
- Il **sole è disaccoppiato** come luce `PhysicalSun` con cone sampling
  e limb darkening Hestroffer. La sun extraction da HDRI dà ombre nitide
  e minor varianza.
- **Visibility flags** (camera/diffuse/glossy/transmission/shadow),
  **background plate** separato, e **orientation 3D** completa si
  applicano a ogni modello sky.
- Le **portal light** restringono NEE ai rettangoli delle finestre per
  ~10× riduzione varianza su scene di interno.
- Il **Nishita atmospheric medium** aggiunge aerial perspective fisica
  usando le stesse costanti del modello sky.
- Le **HDRI** sono campionate per importanza con la compensazione MIS di
  Karlík et al. 2019: i campioni di luce vanno ai punti luminosi, la BSDF
  copre il cielo uniforme.
- I **cieli fisici** hanno un sole coerente con il cielo e si possono
  scrivere in nit (`units: "photometric"`), esposti con l'`ev100` della
  camera.
- La **profondità di campo** è controllata da `aperture` (dimensione
  obiettivo) e `focal_dist` (o `focal_pos: [x, y, z]` per mettere a fuoco
  un punto specifico nel mondo).
- **Camere multiple** in un unico file scena, selezionabili via
  `--camera nome` dalla CLI.

Per preset pronti da copiare vedi:
- [`scenes/00-sky-presets.md`](../../../scenes/00-sky-presets.md) — preset solo-sky (10 voci, solo blocco sky).
- [`scenes/00-world-presets.md`](../../../scenes/00-world-presets.md) — preset sky + ground accoppiati (15 voci, blocco `world:` completo con shape del ground, materiale, UV e flag di visibilità).

---

[Precedente: Padronanza dell'illuminazione](./06-lighting.md) | [Successivo: Geometria Solida Costruttiva (CSG)](./08-csg.md) | [Indice del tutorial](./README.md)
