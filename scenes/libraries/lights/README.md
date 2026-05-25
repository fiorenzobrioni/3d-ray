# Libreria Luci — 3D-Ray

Raccolta di **15 setup di illuminazione** pronti all'uso: 14 file con
sorgenti esplicite (point/directional/spot/area/sphere) organizzati per
ambiente e atmosfera, più `geometry-lights.yaml` con materiali emissivi
`emi_*` per trasformare qualsiasi geometria in sorgente NEE.

## Come usare le sorgenti esplicite

Importa un file nella sezione `imports:` della tua scena. Il loader
fonde automaticamente le luci importate con quelle locali (le locali
vincono in caso di conflitto di nome — ma i setup non usano ID, quindi
le luci si sommano sempre).

```yaml
imports:
  - path: "libraries/lights/studio-3point.yaml"

world:
  sky:
    type: "flat"
    color: [0.02, 0.02, 0.03]

cameras:
  - name: "main"
    position: [0, 2, -6]
    look_at: [0, 1, 0]
    fov: 45

entities:
  - name: "soggetto"
    type: "sphere"
    center: [0, 1, 0]
    radius: 1.0
    material: "mio_materiale"
```

Ogni file include nell'intestazione YAML il world abbinato consigliato
— copialo nella sezione `world:` per l'atmosfera ottimale.

Le luci in lista non hanno ID univoco, quindi non si sovrascrivono per
nome. Per modificare un singolo setup, non importare il file e definisci
le luci direttamente nella scena locale.

## Come usare i materiali emissivi (geometry-lights.yaml)

`geometry-lights.yaml` porta invece una sezione `materials:` con preset
`emi_*` — quando applicati a qualsiasi geometria, il renderer li aggiunge
automaticamente al pool NEE.

```yaml
imports:
  - path: "libraries/lights/geometry-lights.yaml"

entities:
  - name: "globo_tungsteno"
    type: "sphere"
    center: [0, 2, 0]
    radius: 0.15
    material: "emi_tungsteno"

  - name: "striscia_led"
    type: "box"
    scale: [1.2, 0.03, 0.03]
    translate: [0, 2.8, 0]
    material: "emi_led_strip_warm"
```

I due approcci sono combinabili: puoi avere luci esplicite importate
e materiali emissivi nello stesso file.

## I file della libreria

### Studio / Fotografico

| File | Setup | Luci usate | Atmosfera |
|------|-------|------------|-----------|
| `studio-3point.yaml` | 3-Point Classico | area + area + point | Universale — prodotti, showcase, materiali |
| `studio-highkey.yaml` | High Key | area + point×3 | Pulito, commerciale, cosmetica, moda |
| `studio-dramatic.yaml` | Low Key / Chiaroscuro | spot + point×2 | Drammatico, noir, Caravaggio |
| `studio-product.yaml` | Product / Gioielleria | sphere + area×2 + spot + point | Metalli, gemme, still life di precisione |

### Outdoor / Naturale

| File | Setup | Luci usate | Atmosfera |
|------|-------|------------|-----------|
| `outdoor-noon.yaml` | Mezzogiorno | directional×3 | Estate, ombre corte, luce bianca dura |
| `outdoor-golden-hour.yaml` | Ora d'Oro | directional×3 | Cinematografico, caldo-freddo, romantico |
| `outdoor-sunset.yaml` | Tramonto | directional×2 + point | Epico, rosso fuoco, ombre infinite |
| `outdoor-overcast.yaml` | Cielo Coperto | area + directional×2 | Architettura, diffuso, zero ombre dure |

### Notturno / Interni

| File | Setup | Luci usate | Atmosfera |
|------|-------|------------|-----------|
| `night-moonlight.yaml` | Notte con Luna | directional×2 + point | Misteriosa, noir, fantasy |
| `interior-warm.yaml` | Interni Caldi 3000 K | sphere + point×2 + directional | Accogliente, domestico, serale |
| `interior-candlelight.yaml` | Luce di Candele | point×3 + directional | Medievale, romantico, horror gotico |

### Colorato / Creativo

| File | Setup | Luci usate | Atmosfera |
|------|-------|------------|-----------|
| `neon-cyberpunk.yaml` | Neon / Cyberpunk | point×3 + spot | Sci-fi, distopico, rave, arcade |
| `theatre-stage.yaml` | Teatro / Palcoscenico | spot×3 + directional + point | Opera, spettacolo, auditorium |
| `museum-gallery.yaml` | Galleria / Museo | spot×4 + area | Arte contemporanea, esposizioni |

### Materiali Emissivi

| File | Preset | Temperatura / Effetto |
|------|--------|-----------------------|
| `geometry-lights.yaml` | `emi_candela` | 2000 K — candela, lanterna, torcia |
| | `emi_tungsteno` | 3000 K — lampadina incandescente |
| | `emi_alogeno` | 3200 K — faretto alogeno |
| | `emi_fluorescente` | 4000 K — tubo fluorescente, LED neutro |
| | `emi_daylight` | 5500 K — luce diurna, flash fotografico |
| | `emi_cool_white` | 7000 K — LED freddo, display |
| | `emi_fuoco` | Fuoco / fiamma — arancio intenso |
| | `emi_brace` | Brace / ember — rosso scuro |
| | `emi_led_strip_warm` | LED strip calda — striscia lineare |
| | `emi_led_strip_cool` | LED strip fredda — retroilluminazione |
| | `emi_bioluminescenza` | Verde-ciano tenuo — effetti speciali |
| | `emi_sole_diretto` | Sole diretto — intensità massima |

## Tipi di sorgente esplicita disponibili nel motore

| Tipo YAML | Parametri chiave | Quando usare |
|-----------|-----------------|--------------|
| `point` | `position`, `color`, `intensity`, `soft_radius` | Lampadine, accenti, fill, bounce simulati |
| `directional` | `direction`, `color`, `intensity`, `angular_radius` | Sole, luna, cielo diffuso, luce da lontano |
| `spot` | `position`, `direction`, `color`, `intensity`, `inner_angle`, `outer_angle`, `soft_radius`, `shadow_samples` | Faretti, riflettori, neon direzionali |
| `area` | `corner`, `u`, `v`, `color`, `intensity`, `shadow_samples`, `soft_radius` | Softbox, soffitti, finestre, luci diffuse |
| `sphere` | `position`, `radius`, `color`, `intensity`, `shadow_samples` | Lampadine a globo, lanterne, catchlight circolari |

La sphere light usa il solid-angle sampling (2–10× più efficiente dell'area
per sorgenti piccole/lontane) e produce catchlight circolari — essenziale
per gioielleria e prodotti lucidi.

## Light Hardening — Anti-firefly

I setup di questa libreria sono calibrati con i parametri di light
hardening del motore (vedi `docs/reference/scene-reference.md` §8 e
DEVLOG §Ciclo Light Hardening):

- **`soft_radius`** (point/spot/area): clamp del termine `1/d²` a
  `max(d², r²)`. Modella il diametro fisico della sorgente. Valori usati:
  0.05–0.25 a seconda del corpo simulato. Indispensabile per scene con
  foschia/medium o materiali speculari close-up.
- **`angular_radius`** (directional): diametro angolare in gradi. `0.27` =
  sole reale; `0.5` = luna piena. Quando attivo, produce penombre fisiche
  (cone-sampling con `shadow_samples` interno 4).
- **`shadow_samples`** (spot/area/sphere): campioni jitterati per la
  visibilità. Su spot ha effetto solo se `soft_radius > 0`.

I setup outdoor hanno `angular_radius: 0.27` sul sole; interior e studio
hanno `soft_radius` su tutte le point e spot.

Per scene volumetriche, aggiungere anche:
```
--indirect-clamp-factor 0.25   # clamp rimbalzi indiretti
--light-sampling power         # NEE pesata per potenza
```

## World abbinati consigliati

Ogni file YAML ha nell'intestazione il world consigliato. Riferimento rapido:

| Setup | Sky consigliato |
|-------|----------------|
| Studio (tutti) | `flat [0.00–0.02, 0.00–0.02, 0.00–0.03]` |
| Mezzogiorno | `flat [0.06, 0.08, 0.12]` |
| Ora d'Oro | `gradient zenith blu, orizzonte arancio` |
| Tramonto | `flat [0.04, 0.02, 0.01]` |
| Cielo Coperto | `flat [0.08, 0.09, 0.10]` |
| Notte Luna | `flat [0.005, 0.005, 0.012]` |
| Interni Caldi | `flat [0.02, 0.015, 0.008]` |
| Candele | `flat [0.004, 0.002, 0.001]` |
| Neon | `flat [0.01, 0.00, 0.02]` |
| Teatro | `flat [0.00, 0.00, 0.00]` |
| Galleria | `flat [0.025, 0.025, 0.028]` |

## Parametri di render consigliati

| Profilo | Risoluzione | Campioni | Profondità | Shadow | Tempo |
|---------|-------------|----------|------------|--------|-------|
| Preview | `400×225` | `-s 64` | `-d 4` | `-S 1` | < 5 s |
| Standard | `800×450` | `-s 256` | `-d 6` | — | 1–3 min |
| Final | `1920×1080` | `-s 1024` | `-d 8` | `-S 4` | 10–20 min |

Setup con poco ambient e sorgenti puntiformi (candele, neon, studio
dramatic) richiedono spp più alto per ridurre il rumore nelle penombre.
Considera `-S 4` anche nel profilo Standard per questi casi.

Vedi `docs/reference/rendering-profiles.md` per le scorciatoie
`-q draft-tiny / draft / medium / final / ultra`.

## Esempi di combinazioni

### Showcase materiali (studio pulito)
```yaml
imports:
  - path: "libraries/lights/studio-3point.yaml"
  - path: "libraries/materials/metals.yaml"
```

### Still life con lampada a globo emissiva
```yaml
imports:
  - path: "libraries/lights/geometry-lights.yaml"
  - path: "libraries/materials/ceramics.yaml"

entities:
  - name: "globo"
    type: "sphere"
    center: [0, 2.5, 0]
    radius: 0.20
    material: "emi_tungsteno"
```

### Scena outdoor tramonto
```yaml
imports:
  - path: "libraries/lights/outdoor-sunset.yaml"
  - path: "libraries/materials/stones.yaml"
```

### Interno con LED strip warm
```yaml
imports:
  - path: "libraries/lights/geometry-lights.yaml"
  - path: "libraries/lights/interior-warm.yaml"
  - path: "libraries/materials/woods.yaml"

entities:
  - name: "strip_sottopensile"
    type: "box"
    scale: [0.80, 0.02, 0.02]
    translate: [0, 2.2, 0.25]
    material: "emi_led_strip_warm"
```
