# Librerie 3D-Ray — Guida alle Risorse Riutilizzabili

Raccolta di risorse pronte all'uso per il motore di ray tracing **3D-Ray**.
Le librerie sono file YAML importabili che portano materiali, luci, font e
terrain nella tua scena con una singola riga di import.

**Contenuto:**
- `materials/` — 20 file, 1450 materiali Disney BSDF e Classic
- `lights/` — 14 setup luci esplicite + 1 file materiali emissivi (`emi_*`)
- `textures/` — 20 texture PNG (albedo + normal map)
- `fonts/` — 1 font 3D per la primitiva `extrusion`
- `terrains/` — 1 template heightfield con heightmap PNG-16

---

## Come funzionano gli import

Il sistema di import carica file YAML esterni e fonde il loro contenuto con
quello della scena principale. Le sezioni `materials`, `entities`, `lights`
e `templates` vengono unite; le definizioni locali con lo stesso ID/nome
sovrascrivono sempre quelle importate (local wins).

```yaml
# La tua scena
imports:
  - path: "libraries/materials/metals.yaml"
  - path: "libraries/lights/studio-3point.yaml"

world:
  sky:
    type: "flat"
    color: [0.0, 0.0, 0.0]

cameras:
  - name: "main"
    position: [0, 2, -6]
    look_at: [0, 1, 0]
    fov: 45

entities:
  - name: "sfera"
    type: "sphere"
    center: [0, 0.85, 0]
    radius: 0.35
    material: "dis_oro_lucido"      # dal metals.yaml
```

**Percorsi relativi:** i path negli import sono risolti relativamente alla
directory del file che li contiene. Se la tua scena è in `scenes/`, usa
`"libraries/materials/metals.yaml"` (senza `scenes/`).

Le texture non seguono il meccanismo degli `imports:` — si referenziano
con il percorso relativo direttamente nel campo `texture_path` o
`normal_map` del materiale, senza alcun import aggiuntivo.

---

## Materiali — `materials/`

**20 file · 1450 materiali**

Raccolta completa di materiali fisicamente corretti con quattro prefissi:
`dis_` (Disney BSDF), `cls_` (Classic — `lambertian` / `metal` /
`dielectric` scelto in base al lobo dominante), `over_` (overlay
weathering per `type: mix`), `mix_` (preset compositi pronti all'uso).
Tutti i materiali Disney usano le chiavi moderne del BSDF (`coat_roughness`
+ `coat_ior`, `sheen_roughness`, `subsurface_color`, `thin_film_*`,
`transmission_color` + `transmission_depth` per Beer-Lambert).

| File | Contenuto | Materiali |
|------|-----------|----------:|
| `metals.yaml` | Oro, argento, rame, bronzo, ottone, acciaio, ferro, alluminio, titanio, cromo, platino, nichel, zinco, peltro, corten, mercurio, niobio | 131 |
| `ceramics.yaml` | Porcellana, bone china, maiolica, terracotta, grès, raku, celadon, biscotto, smaltate, sigillate | 112 |
| `plastics.yaml` | ABS, policarbonato, acrilico, PVC, nylon, PLA, teflon, bachelite, gomma, silicone, EVA, vinile | 105 |
| `glasses.yaml` | Vetri industriali/ottici, cristalli, gemme, ghiaccio, liquidi, resine, smerigliati | 101 |
| `fabrics.yaml` | Velluto, seta, raso, cotone, lino, lana, denim, tweed, feltro, neoprene, organza | 101 |
| `foods.yaml` | Cioccolato, frutta, verdura, formaggi, pane, pasta, dolci | 100 |
| `organics.yaml` | Cera, ambra, avorio, corno, corallo, madreperla, sughero, carta, sapone | 98 |
| `paints.yaml` | Auto metallizzata/pastello/perlata, lacche, smalti, chalk paint, pittura murale | 93 |
| `stones.yaml` | Marmi, graniti, travertino, ardesia, onice, basalto, mattoni | 88 |
| `woods.yaml` | Latifoglie chiare/medie/scure, ebano, esotici, trattati, figure decorative | 87 |
| `grounds.yaml` | Checker, parquet, piastrelle, cemento, asfalto, terra, sabbia, erba, neve, acque | 75 |
| `liquids.yaml` | Acque, latticini, sangue, oli, alcolici, sciroppi, bevande | 53 |
| `plasters.yaml` | Rasati, graffiati, veneziano, marmorino, tadelakt, calce, gesso | 50 |
| `leathers.yaml` | Pieno fiore, anilina, nappa, suede, patent, esotici, box calf, ecoleather | 46 |
| `industrial-coatings.yaml` | Chassis auto, polveri RAL, anodizzazione, zincatura, cromature, gel coat | 43 |
| `concretes.yaml` | Cemento liscio/esposto/lavorato/lavato, colorati, asfalto bagnato, bitume | 42 |
| `synthetics.yaml` | Carbon fiber, kevlar, vetroresina, PTFE, silicone, poliuretani, aerogel | 34 |
| `minerals-gems.yaml` | Quarzi, geodi, cristalli, calcite, fluorite, malachite, lapislazzuli, opali | 30 |
| `weathering.yaml` | 26 overlay `over_*`: ruggine, muschio, polvere, calcare, vernice scrostata | 26 |
| `mix-recipes.yaml` | 35 ricette `mix_*` composte: metalli arrugginiti, legni usurati, intonaci macchiati | 35 |

Documentazione completa: [`materials/README.md`](materials/README.md)

---

## Luci — `lights/`

**14 setup luci esplicite + 1 file materiali emissivi**

Setup di illuminazione completi per ogni ambiente, più `geometry-lights.yaml`
con 12 preset `emi_*` per trasformare qualsiasi geometria in sorgente NEE.

| Categoria | File | Atmosfera |
|-----------|------|-----------|
| Studio | `studio-3point.yaml` | Universale — 3-point classico |
| Studio | `studio-highkey.yaml` | Moda, cosmetica, pulito |
| Studio | `studio-dramatic.yaml` | Chiaroscuro, noir, Caravaggio |
| Studio | `studio-product.yaml` | Gioielleria, still life di precisione |
| Outdoor | `outdoor-noon.yaml` | Mezzogiorno, ombre corte |
| Outdoor | `outdoor-golden-hour.yaml` | Ora d'oro, cinematografico |
| Outdoor | `outdoor-sunset.yaml` | Tramonto, epico, ombre infinite |
| Outdoor | `outdoor-overcast.yaml` | Cielo coperto, diffuso, architettura |
| Notturno | `night-moonlight.yaml` | Notte lunare, misteriosa |
| Interni | `interior-warm.yaml` | Domestico, 3000 K, accogliente |
| Interni | `interior-candlelight.yaml` | Candele, medievale, romantico |
| Creativo | `neon-cyberpunk.yaml` | Neon, sci-fi, cyberpunk |
| Creativo | `theatre-stage.yaml` | Teatro, opera, palcoscenico |
| Creativo | `museum-gallery.yaml` | Galleria, museale, spot exhibit |
| Emissivi | `geometry-lights.yaml` | 12 preset `emi_*` — candela → sole |

Documentazione completa: [`lights/README.md`](lights/README.md)

---

## Texture — `textures/`

**20 texture PNG** (albedo e normal map) utilizzabili direttamente nei
materiali tramite i campi `texture_path` e `normal_map`. Non richiedono
`imports:` — si referenziano con il percorso relativo.

```yaml
materials:
  - id: "parquet"
    type: "disney"
    texture_path: "libraries/textures/wood-floor.png"
    normal_map:   "libraries/textures/wood-floor-normal.png"
    roughness: 0.45
    metallic: 0.0
```

Documentazione completa: [`textures/README.md`](textures/README.md)

---

## Font — `fonts/`

**1 font 3D** per la primitiva `extrusion`: ogni carattere è un template
estruso lungo Y, istanziabile con `type: "instance"`. I font sono generati
dallo strumento `FontGen`.

```yaml
imports:
  - path: "libraries/fonts/font-open-sans.yaml"

entities:
  - type: "instance"
    template: "lettera_H_maiusc_open-sans"
    translate: [0, 0, 0]
```

| File | Font | Caratteri |
|------|------|-----------|
| `font-open-sans.yaml` | Open Sans Regular | A–Z, a–z, 0–9 |

Documentazione completa: [`fonts/README.md`](fonts/README.md)

---

## Terreni — `terrains/`

**1 template heightfield** con heightmap PNG-16 a 16 bit. Il motore
interseca il heightfield via MinMax Mipmap quadtree — nessuna tessellazione
mesh. I file sono generati dallo strumento `TerrainGen`.

```yaml
imports:
  - path: "libraries/terrains/heightfield-strata-test.yaml"

entities:
  - type: "instance"
    template: "heightfield_strata_test"
    translate: [0, 0, 0]
```

| File | Tipo | Dimensioni |
|------|------|------------|
| `heightfield-strata-test.yaml` | Montagna | 100×100 m |

Documentazione completa: [`terrains/README.md`](terrains/README.md)

---

## Combinare le librerie

Le librerie sono progettate per lavorare insieme. Alcuni pattern tipici:

### Showcase materiali (studio pulito)
```yaml
imports:
  - path: "libraries/materials/metals.yaml"
  - path: "libraries/lights/studio-product.yaml"
```

### Still life con lampada emissiva
```yaml
imports:
  - path: "libraries/lights/geometry-lights.yaml"
  - path: "libraries/materials/ceramics.yaml"
  - path: "libraries/materials/woods.yaml"

entities:
  - name: "globo"
    type: "sphere"
    center: [0, 2.5, 0]
    radius: 0.20
    material: "emi_tungsteno"
```

### Scena outdoor al tramonto
```yaml
imports:
  - path: "libraries/materials/stones.yaml"
  - path: "libraries/lights/outdoor-sunset.yaml"
  - path: "libraries/terrains/heightfield-strata-test.yaml"

entities:
  - type: "instance"
    template: "heightfield_strata_test"
    translate: [0, 0, 0]
```

### Interno serale con LED strip
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

### Testo 3D con materiale metallico
```yaml
imports:
  - path: "libraries/fonts/font-open-sans.yaml"
  - path: "libraries/materials/metals.yaml"
  - path: "libraries/lights/studio-3point.yaml"

entities:
  - type: "instance"
    template: "lettera_H_maiusc_open-sans"
    material: "dis_oro_lucido"
    translate: [0, 0, 0]
```

---

## Best practice — trasformazioni su primitive

Le primitive con parametro `center:` (sphere, cylinder, cone, capsule, torus)
non vanno combinate con `rotate:` o `scale:`: le trasformazioni
`scale → rotate → translate` vengono sempre applicate attorno all'**origine
globale** del sistema di coordinate, non attorno al `center:` della
primitiva.

```yaml
# Sbagliato — il rotate ruota la sfera attorno all'origine, non al suo centro
- type: "sphere"
  center: [0, 1.5, 0]
  radius: 0.3
  rotate: [0, 0, 90]

# Corretto — la sfera è in (0,0,0), si scala/ruota localmente, poi posiziona
- type: "sphere"
  radius: 0.3
  rotate: [0, 0, 90]
  translate: [0, 1.5, 0]
```

`box` e `mesh` non hanno `center:` e usano nativamente `translate:`, quindi
sono immuni dal problema.

---

## Parametri di render di riferimento

| Profilo | CLI | Tempo stimato |
|---------|-----|---------------|
| Smoke test | `-w 160 -H 90 -s 1 -d 5` | < 1 s |
| Preview | `-w 400 -H 225 -s 64 -d 4 -S 1` | < 5 s |
| Standard | `-w 800 -H 450 -s 256 -d 6` | 1–3 min |
| Final | `-w 1920 -H 1080 -s 1024 -d 8 -S 4` | 10–20 min |
| Ultra (4K) | `-w 3840 -H 2160 -s 1600 -d 8 -S 4` | 40+ min |

Vedi `docs/reference/rendering-profiles.md` per le scorciatoie
`-q draft-tiny / draft / medium / final / ultra`.
