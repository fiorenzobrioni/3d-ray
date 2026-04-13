# 💡 Libreria Luci — Setup di Illuminazione

Raccolta di **14 setup di illuminazione** pronti all'uso, organizzati per
ambiente e atmosfera. Ogni file YAML è una libreria importabile che aggiunge
la sezione `lights:` alla tua scena senza toccare materiali, oggetti o camera.

---

## Come Usare

Importa un file nella sezione `imports:` della tua scena. Il loader fonde
automaticamente le luci importate con quelle locali (le locali vincono).

```yaml
imports:
  - path: "libraries/lights/studio-3point.yaml"

world:
  ambient_light: [0.02, 0.02, 0.03]
  background: [0.0, 0.0, 0.0]

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

> **Nota:** ogni file include nella sua intestazione il world abbinato
> consigliato. Copialo nella sezione `world:` della tua scena per ottenere
> l'atmosfera ottimale per quel setup.

### Sovrascrivere una singola luce

Le luci sono definite in lista: non hanno ID univoco, quindi non possono essere
sovrascritte per nome. Per modificare una luce specifica, **non importare** il
file di libreria e definisci le luci direttamente nella scena locale.

---

## I File della Libreria

### 🎬 Studio / Fotografico

| File | Setup | Luci usate | Atmosfera |
|------|-------|------------|-----------|
| `studio-3point.yaml` | **3-Point Classico** | area + area + point | Universale — prodotti, showcase, materiali |
| `studio-highkey.yaml` | **High Key** | area + point×3 | Pulito, commerciale, cosmetica, moda |
| `studio-dramatic.yaml` | **Low Key / Chiaroscuro** | spot + point×2 | Drammatico, noir, Caravaggio |
| `studio-product.yaml` | **Product / Gioielleria** | sphere + area×2 + spot + point | Metalli, gemme, still life di precisione |

### ☀️ Outdoor / Naturale

| File | Setup | Luci usate | Atmosfera |
|------|-------|------------|-----------|
| `outdoor-noon.yaml` | **Mezzogiorno** | directional×3 | Estate, ombre corte, luce bianca dura |
| `outdoor-golden-hour.yaml` | **Ora d'Oro** | directional×3 | Cinematografico, caldo-freddo, romantico |
| `outdoor-sunset.yaml` | **Tramonto** | directional×2 + point | Epico, rosso fuoco, ombre infinite |
| `outdoor-overcast.yaml` | **Cielo Coperto** | area + directional×2 | Architettura, diffuso, zero ombre dure |

### 🌙 Notturno / Domestico

| File | Setup | Luci usate | Atmosfera |
|------|-------|------------|-----------|
| `night-moonlight.yaml` | **Notte con Luna** | directional×2 + point | Misteriosa, noir, fantasy |
| `interior-warm.yaml` | **Interni Caldi 3000 K** | sphere + point×2 + directional | Accogliente, domestico, serale |
| `interior-candlelight.yaml` | **Luce di Candele** | point×3 + directional | Medievale, romantico, horror gotico |

### 🎨 Colorato / Creativo

| File | Setup | Luci usate | Atmosfera |
|------|-------|------------|-----------|
| `neon-cyberpunk.yaml` | **Neon / Cyberpunk** | point×3 + spot | Sci-fi, distopico, rave, arcade |
| `theatre-stage.yaml` | **Teatro / Palcoscenico** | spot×3 + directional + point | Opera, spettacolo, auditorium |
| `museum-gallery.yaml` | **Galleria / Museo** | spot×4 + area | Arte contemporanea, esposizioni |

---

## Tipi di Luce Disponibili nel Motore

Tutti i setup usano esclusivamente i tipi di luce nativi del motore:

| Tipo YAML | Parametri chiave | Quando usare |
|-----------|-----------------|--------------|
| `point` | `position`, `color`, `intensity` | Lampadine, accenti, fill, bounce simulati |
| `directional` | `direction`, `color`, `intensity` | Sole, luna, cielo diffuso, luce da lontano |
| `spot` | `position`, `direction`, `color`, `intensity`, `inner_angle`, `outer_angle` | Faretti, riflettori, neon direzionali |
| `area` | `corner`, `u`, `v`, `color`, `intensity`, `shadow_samples` | Softbox, soffitti, finestre, luci diffuse |
| `sphere` | `position`, `radius`, `color`, `intensity`, `shadow_samples` | Lampadine a globo, lanterne, catchlight |

> **Sphere vs Area:** la sphere light usa il solid-angle sampling (2–10×
> più efficiente dell'area per sorgenti piccole/lontane) e produce catchlight
> circolari — essenziale per gioielleria e prodotti lucidi.

---

## World Abbinati Consigliati

Ogni file di libreria ha nell'intestazione YAML il world consigliato.
Riferimento rapido:

| Setup | `ambient_light` | Sky |
|-------|----------------|-----|
| Studio (tutti) | `[0.00–0.02, 0.00–0.02, 0.00–0.03]` | Nessuno — `background: [0,0,0]` |
| Mezzogiorno | `[0.06, 0.08, 0.12]` | `gradient` cielo azzurro diurno |
| Ora d'Oro | `[0.05, 0.04, 0.02]` | `gradient` orizzonte ambrato |
| Tramonto | `[0.04, 0.02, 0.01]` | `gradient` orizzonte rosso fuoco |
| Cielo Coperto | `[0.08, 0.09, 0.10]` | `gradient` grigio uniforme |
| Notte Luna | `[0.005, 0.005, 0.012]` | `gradient` notte con luna |
| Interni Caldi | `[0.02, 0.015, 0.008]` | Nessuno — `background: [0.01,0.01,0.02]` |
| Candele | `[0.004, 0.002, 0.001]` | Nessuno — `background: [0,0,0]` |
| Neon | `[0.01, 0.00, 0.02]` | Nessuno — `background: [0,0,0]` |
| Teatro | `[0.00, 0.00, 0.00]` | Nessuno — `background: [0,0,0]` |
| Galleria | `[0.025, 0.025, 0.028]` | Nessuno — `background: [0,0,0]` |

---

## Parametri di Render Consigliati

| Livello | Risoluzione | Campioni | Profondità | Shadow | Tempo |
|---------|-------------|----------|------------|--------|-------|
| Test | `480×270` | `-s 4` | `-d 10` | `-S 2` | < 5 s |
| Draft | `800×450` | `-s 16` | `-d 15` | `-S 4` | < 30 s |
| Preview | `1280×720` | `-s 64` | `-d 25` | `-S 8` | 1–5 min |
| Finale | `1920×1080` | `-s 256` | `-d 40` | `-S 16` | 10–30 min |
| Ultra | `2560×1440` | `-s 512` | `-d 55` | `-S 24` | 30+ min |

> Setup con poco ambient e sorgenti puntiformi (candele, neon, studio dramatic)
> richiedono **spp più alto** per ridurre il rumore nelle zone di penombra.
> Partire da `-s 64` in draft per questi setup.

---

## Esempi di Combinazioni

### Showcase di materiali (studio pulito)
```yaml
imports:
  - path: "libraries/lights/studio-3point.yaml"
  - path: "libraries/materials/metals.yaml"
```

### Scultura su piedistallo (galleria)
```yaml
imports:
  - path: "libraries/lights/museum-gallery.yaml"
  - path: "libraries/materials/ceramics.yaml"
  - path: "libraries/objects/decorative-objects.yaml"
```

### Scena outdoor tramonto
```yaml
imports:
  - path: "libraries/lights/outdoor-sunset.yaml"
  - path: "libraries/objects/outdoor.yaml"
```

### Interno serale con candele
```yaml
imports:
  - path: "libraries/lights/interior-candlelight.yaml"
  - path: "libraries/objects/furniture.yaml"
  - path: "libraries/objects/lighting.yaml"
```
