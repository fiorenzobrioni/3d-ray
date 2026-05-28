# Capitolo 7: Cielo, ambiente ed effetti fotocamera

Il cielo è la più grande sorgente luminosa in qualsiasi scena
all'aperto. Un ambiente ben configurato può trasformare un rendering
piatto in qualcosa di veramente fotografico. Il sistema sky/environment
di 3D-Ray è allineato ai renderer offline di produzione (Arnold, Cycles,
Renderman, Mitsuba): cinque modelli di cielo, image-based lighting con
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
| `preetham`       | Daylight clear-sky analitico (Preetham 1999). Alias YAML: `hosek_wilkie`     | Outdoor a mezzogiorno, background stabili  |
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
1/(4π)) quando la luminanza è positiva — stesso approccio di
Cycles/Arnold per gli "uniform world backgrounds". Imposta `color:
[0, 0, 0]` per scene black-void stile Cornell-box; il loader esclude
automaticamente da NEE un cielo flat con luminanza zero.

---

## 7.3 Gradient Sky con Disco Solare

Gradiente verticale a tre bande (zenith → orizzonte → ground) con sole
analitico opzionale. Il sole è agganciato automaticamente come luce
`PhysicalSun` separata con cone sampling e (opzionalmente) limb
darkening Hestroffer — stesso workflow di `aiSkyDomeLight` in Arnold.

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

## 7.4 Physical Sky — Hosek-Wilkie / Preetham

Il modello daylight clear-sky analitico usato da Arnold, Cycles e
RenderMan. Un singolo knob `turbidity` controlla l'intero look
atmosferico — niente colori zenith/horizon/ground regolati a mano. La
direzione del sole controlla sia la posizione del disco sia la
distribuzione spaziale del cielo (schiarimento attorno al sole, blu allo
zenith).

```yaml
world:
  sky:
    type: "hosek_wilkie"             # alias di "preetham"
    turbidity: 3.0                   # 1 = pulitissimo, 3 = clear, 5 = foschia, 10 = smog
    ground_albedo: [0.25, 0.25, 0.22]
    intensity: 1.0
    sun:
      direction:       [-0.35, 0.78, 0.52]
      angular_radius:  0.265
      shadow_samples:  4
```

| Parametro        | Default | Descrizione                                                              |
|------------------|---------|--------------------------------------------------------------------------|
| `turbidity`      | `3.0`   | Pulizia atmosferica (1–10): basso = pulito, alto = foschia               |
| `ground_albedo`  | `[0.3]` | Albedo emisferica del terreno concettuale; tinge la parte bassa del cielo|
| `intensity`      | `1.0`   | Moltiplicatore su radianza cielo + sole                                  |

> **Nota implementativa.** Sia `type: hosek_wilkie` che `type: preetham`
> instradano attualmente sullo stesso modello Preetham 1999. Il vantaggio
> di Hosek-Wilkie su Preetham alle elevazioni solari molto basse è
> completamente (e con margine) coperto dal modello `nishita` — nel
> dubbio per i tramonti, passa a Nishita. Vedi `DEVLOG.md` per la
> motivazione di design.

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
    turbidity: 3.0                  # remappato internamente a un fattore Mie/polvere
    intensity: 1.0
    sun:
      direction:       [-0.85, 0.12, 0.4]   # basso sull'orizzonte → palette sunset
      angular_radius:  0.265
      shadow_samples:  4
```

**Performance.** La LUT di trasmittanza (16×64 float × 3 canali =
12 KB) è costruita una sola volta in costruzione (~20 ms). La radianza
per-direzione fa un'integrazione di view-ray a 16 passi con due lookup
LUT per passo — circa 3× un lookup Preetham. Trascurabile a budget di
render tipici.

**Quando scegliere Nishita su Hosek-Wilkie / Preetham:**

| Scenario                                  | Scelta migliore |
|-------------------------------------------|-----------------|
| Sole oltre 20° di elevazione, mezzogiorno | Preetham (più veloce) |
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

### Mipmap prefiltering automatico (zero configurazione)

Il renderer rileva quando un bounce glossy fugge su un HDRI e legge
automaticamente da un livello mipmap pre-filtrato proporzionale alla
larghezza del lobo BSDF — eliminando i firefly dai peak HDRI sotto-
campionati sui mirror rough. La piramide è costruita on-demand (box
filter 2×2 con peso sin(θ) per conservazione di energia su equirect); le
scene che non la usano non pagano memoria.

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

È lo stesso workflow di Arnold `aiSkyDomeLight.aov_indirect` "sun
extraction" o della raccomandazione Cycles "Sun Lamp + HDRI".

### Trovare la `rotation` giusta

L'intera larghezza equirect rappresenta 360° — sole al 25% dal bordo
sinistro = 90°, al 75% = 270°. Inizia con `rotation: 0`, regola a passi
di 45°, poi affina. Per orientation 3D completa (pitch + roll, non solo
yaw) usa il blocco `orientation:` in §7.7 sotto.

---

## 7.7 Feature Globali Sky: Visibility, Background, Orientation

Queste tre feature si applicano a ogni modello di cielo.

### Flag di visibilità (per categoria di raggio)

Parità Cycles "Ray Visibility" / Arnold `aiSkyDomeLight.visibility.*`.
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
Sampling". Stesso approccio di Mitsuba `emitters/portal.cpp` e del
workflow window-light di Arnold.

---

## 7.9 Aerial Perspective — Nishita Atmospheric Medium

Il look "depth-of-air" che i renderer offline ottengono con Cycles
"Volume Scatter" + sky / Arnold `atmosphere_volume` + sun. La geometria
distante acquista una tinta bluastra (scattering Rayleigh) e perde
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
    air_density: [1, 1, 1]       # moltiplicatore densità Rayleigh per canale
    # phase di default Henyey-Greenstein g=0.76 (Mie forward scattering)
```

`world_scale` è il knob chiave del mapping — scegli in base alla scala
della scena (1000 per scene "scala stadio", 200 per "isolato urbano", 50
per "singola stanza"). L'optical depth ha forma chiusa (somma di due
esponenziali) quindi nessuna varianza extra sulla trasmittanza — il
free-path sampling usa delta tracking con majorante alla quota più bassa.

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

### Fuoco su un punto — `focal_pos` (Arnold/Cycles "Focus Object")

I renderer di produzione lasciano specificare il **punto focale**
direttamente:

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

---

## 7.11 Camere Multiple Nominate

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

## 7.12 Esempio Completo: Golden Hour Landscape

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
- Il **mipmap prefiltering HDRI** è automatico sui bounce glossy —
  nessuna configurazione richiesta; elimina i firefly.
- La **profondità di campo** è controllata da `aperture` (dimensione
  obiettivo) e `focal_dist` (o `focal_pos: [x, y, z]` per il workflow
  Arnold/Cycles "Focus Object").
- **Camere multiple** in un unico file scena, selezionabili via
  `--camera nome` dalla CLI.

Per preset pronti da copiare vedi:
- [`scenes/00-sky-presets.md`](../../../scenes/00-sky-presets.md) — preset solo-sky (10 voci, solo blocco sky).
- [`scenes/00-world-presets.md`](../../../scenes/00-world-presets.md) — preset sky + ground accoppiati (15 voci, blocco `world:` completo con shape del ground, materiale, UV e flag di visibilità).

---

[Precedente: Padronanza dell'illuminazione](./06-lighting.md) | [Successivo: Geometria Solida Costruttiva (CSG)](./08-csg.md) | [Indice del tutorial](./README.md)
