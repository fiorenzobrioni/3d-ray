# 📚 Librerie 3D-Ray — Guida alle Risorse Riutilizzabili

Benvenuto nella raccolta di risorse pronte all'uso per il motore di ray tracing
**3D-Ray**. Le librerie sono file YAML importabili che portano materiali, oggetti,
setup di luci e scene complete nella tua scena con una singola riga di import.

---

## Come Funzionano gli Import

Il sistema di import carica file YAML esterni e fonde il loro contenuto con
quello della scena principale. Le sezioni `materials`, `entities`, `lights`
e `templates` vengono unite; le definizioni locali con lo stesso ID/nome
sovrascrivono sempre quelle importate (local wins).

```yaml
# La tua scena
imports:
  - path: "libraries/materials/metals.yaml"
  - path: "libraries/objects/furniture.yaml"
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
  - type: "instance"
    template: "tavolo_classico"     # ← dal furniture.yaml
    translate: [0, 0, 0]

  - name: "sfera"
    type: "sphere"
    center: [0, 0.85, 0]
    radius: 0.35
    material: "dis_oro_lucido"      # ← dal metals.yaml
```

> **Percorsi relativi:** i path negli import sono risolti relativamente alla
> directory del file che li contiene. Se la tua scena è in `scenes/`, usa
> `"libraries/materials/metals.yaml"` (senza `scenes/`).

---

## Le Quattro Librerie

### 🎨 Materiali — `materials/`

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
| `metals.yaml` | Oro, argento, rame, bronzo, ottone, acciaio (incluso damasco wootz), ferro, ghisa, alluminio (incl. anodizzati), titanio (anodizzato thin_film), cromo, platino, nichel, zinco, peltro, corten, mercurio, niobio olografico | 131 |
| `ceramics.yaml` | Porcellana, bone china, maiolica, terracotta, grès, raku (incl. iridescente), celadon (incl. crackle), biscotto, smaltate, sigillate, finiture satin | 112 |
| `plastics.yaml` | ABS, policarbonato, acrilico, PVC, nylon, PLA, teflon, bachelite, gomma, silicone medicale, EVA, vinile | 105 |
| `glasses.yaml` | Vetri industriali/ottici, cristalli, gemme preziose e semipreziose, ghiaccio, liquidi, resine, smerigliati frosted | 101 |
| `fabrics.yaml` | Velluto e seta (sheen Charlie), raso, cotone, lino, lana, denim, tweed, feltro, neoprene, canvas, organza/tulle | 101 |
| `foods.yaml` | Cioccolato, frutta, verdura, formaggi, pane, pasta, dolci, burro/grassi | 100 |
| `organics.yaml` | Cera, ambra, avorio, corno, corallo, madreperla (thin_film), conchiglia, sughero, carta, sapone, bambù | 98 |
| `paints.yaml` | Auto metallizzata/pastello/perlata cangiante (thin_film), lacche (pianoforte, urushi), smalti, chalk paint, pittura murale, spray | 93 |
| `stones.yaml` | Marmi bianchi/scuri/colorati (con `marble` production-grade), graniti, travertino, ardesia, onice/alabastro (SSS), basalto, mattoni | 88 |
| `woods.yaml` | Latifoglie chiare/medie/scure, ebano, esotici, trattati (shou sugi ban, barnwood), studio (curly, flame, bird's eye, burl) | 87 |
| `grounds.yaml` | Checker, parquet, piastrelle, marmo pavimento, cemento, asfalto, terra, sabbia, ghiaia, erba, neve, moquette, acque | 75 |
| `liquids.yaml` | Acque, latticini, sangue, oli, alcolici (Beer-Lambert profondo), sciroppi, bevande calde, succhi, refrigeranti | 53 |
| `plasters.yaml` | Rasati, graffiati, veneziano (clearcoat alto), marmorino, tadelakt (subsurface), stucco antico, calce mediterranea, gesso | 50 |
| `leathers.yaml` | Pieno fiore, anilina, nappa, suede (sheen 0.85), patent (clearcoat), esotici (voronoi cell), box calf, cuoio grezzo, ecoleather | 46 |
| `industrial-coatings.yaml` | Chassis auto, clearcoat, polveri RAL, anodizzazione Al/Ti (thin_film 320-640nm), zincatura, cromature, smalti a fuoco, gel coat, termocromiche | 43 |
| `concretes.yaml` | Cemento liscio/esposto/lavorato/lavato a vista, colorati, asfalto (incl. bagnato), bitume catrame | 42 |
| `synthetics.yaml` | Carbon fiber (anisotropic), kevlar, vetroresina, neoprene, PTFE, silicone medicale, poliuretani, vinile auto wrap (olografico), tessuti tecnici, aerogel | 34 |
| `minerals-gems.yaml` | Quarzi, geodi, druse, cristalli cubici, calcite islandese birifrangente, fluorite, malachite, lapislazzuli, pietra di luna, opali, kyanite | 30 |
| `weathering.yaml` | 26 overlay `over_*` per `type: mix`: ruggine, muschio, polvere, calcare, grasso, neve, vernice scrostata, foglie, sale marino, film d'acqua | 26 |
| `mix-recipes.yaml` | 35 ricette `mix_*` pronte all'uso che combinano `dis_*` + `over_*` + maschera procedurale (metalli arrugginiti, legni usurati, intonaci macchiati, pietre colonizzate, vernici scrostate) | 35 |

**Convenzione dei nomi:** `prefisso_categoria_variante`
- `dis_` → Disney BSDF (close-up, effetti PBR avanzati)
- `cls_` → Classic (scene grandi, render veloci): `lambertian`,
  `metal` o `dielectric` scelto sul lobo dominante
- `over_` → overlay weathering (in `weathering.yaml`)
- `mix_` → ricetta composita pronta all'uso (in `mix-recipes.yaml`)

📖 **Documentazione completa:** [`materials/README.md`](materials/README.md)

---

### 📦 Oggetti — `objects/`

**11 file · ~150 template · ~220 materiali dedicati**

Template di oggetti composti pronti all'istanziazione. Ogni file contiene
oggetti costruiti con primitive, CSG, torus, **lathe (superficie di
rivoluzione)** e gruppi annidati. I corpi assi-simmetrici (calici, bottiglie,
vasi, colonne tornite, balaustri, vetreria, pezzi Staunton, campane) sono
generati con `lathe` a profilo Catmull-Rom / Linear per silhouette C¹
continua e meno primitive — vedi il cap. 11 del tutorial
([`docs/tutorial/it/11-lathe-surface-of-revolution.md`](../../docs/tutorial/it/11-lathe-surface-of-revolution.md)).
I materiali dedicati sono inclusi nel file e si fondono con quelli della
scena.

| File | Template | Descrizione |
|------|----------|-------------|
| `furniture.yaml` | 11 | Tavoli, sedie, scaffali, lampade, comodini, candelabri (lathe) |
| `decorative-objects.yaml` | 12 | Vasi Ming/greci (lathe), clessidre, globi, sfere su piedistallo (lathe) |
| `tableware.yaml` | 11 | Calici (lathe), bottiglie (lathe), decanter (lathe), teiere, tazze |
| `architecture.yaml` | 15 | Colonne (lathe), archi, scale, balaustri (lathe), pinnacoli (lathe) |
| `mechanical.yaml` | 14 | Ingranaggi, pistoni, molle, viti, tubi, giunti |
| `jewelry.yaml` | 14 | Anelli, collane, orecchini, spille, bracciali, gemme |
| `lighting.yaml` | 15 | Lampadari, plafoniere (lathe), paralumi (lathe), lampioni |
| `laboratory.yaml` | 14 | Beute (lathe), palloni (lathe), imbuti (lathe), microscopi |
| `musical.yaml` | 14 | Violino, chitarra, tromba, campane (lathe), timpani (lathe) |
| `outdoor.yaml` | 15 | Fontane, panchine, fioriere (lathe), vasi giardino (lathe) |
| `nature.yaml` | 15 | Alberi, cespugli, fiori, funghi, cristalli, bonsai |

**Uso base:**
```yaml
imports:
  - path: "libraries/objects/furniture.yaml"

entities:
  - type: "instance"
    template: "tavolo_classico"
    translate: [0, 0, 0]
  - type: "instance"
    template: "sedia_classica"
    translate: [0, 0, -0.7]
    rotate: [0, 180, 0]
```

📖 **Documentazione completa:** [`objects/README.md`](objects/README.md)

---

### 💡 Luci — `lights/`

**14 file · setup pronti per ogni ambiente**

Setup di illuminazione completi e commentati. Ogni file porta una sezione
`lights:` ottimizzata per una specifica atmosfera, con spiegazione del ruolo
di ogni sorgente e il world abbinato consigliato.

| Categoria | File | Atmosfera |
|-----------|------|-----------|
| **Studio** | `studio-3point.yaml` | Universale — 3-point classico |
| **Studio** | `studio-highkey.yaml` | Moda, cosmetica, pulito |
| **Studio** | `studio-dramatic.yaml` | Chiaroscuro, noir, Caravaggio |
| **Studio** | `studio-product.yaml` | Gioielleria, still life di precisione |
| **Outdoor** | `outdoor-noon.yaml` | Mezzogiorno, ombre corte |
| **Outdoor** | `outdoor-golden-hour.yaml` | Ora d'oro, cinematografico |
| **Outdoor** | `outdoor-sunset.yaml` | Tramonto, epico, ombre infinite |
| **Outdoor** | `outdoor-overcast.yaml` | Cielo coperto, diffuso, architettura |
| **Notturno** | `night-moonlight.yaml` | Notte lunare, misteriosa |
| **Interni** | `interior-warm.yaml` | Domestico, 3000 K, accogliente |
| **Interni** | `interior-candlelight.yaml` | Candele, medievale, romantico |
| **Creativo** | `neon-cyberpunk.yaml` | Neon, sci-fi, cyberpunk |
| **Creativo** | `theatre-stage.yaml` | Teatro, opera, palcoscenico |
| **Creativo** | `museum-gallery.yaml` | Galleria, museale, spot exhibit |

📖 **Documentazione completa:** [`lights/README.md`](lights/README.md)

---

### 🎬 Starter Kit — `starter-kits/`

**17 scene YAML complete e renderizzabili immediatamente**

Scene già pronte con world, camere multiple, materiali, template, luci e
oggetti. Pensate come punto di partenza: copiare, rinominare e modificare.

| Categoria | File | Scena |
|-----------|------|-------|
| **Outdoor** | `starter-desert-highway.yaml` | Strada nel deserto americano con cactus e guardrail |
| **Outdoor** | `starter-snowy-clearing.yaml` | Radura invernale con abeti, lago ghiacciato, rocce |
| **Outdoor** | `starter-zen-garden.yaml` | Giardino giapponese con lanterna tōrō e tsukubai |
| **Outdoor** | `starter-ancient-ruins.yaml` | Rovine di tempio greco con colonne e archi CSG |
| **Outdoor** | `starter-floating-islands.yaml` | Isole fantasy sospese in cielo viola-arancio |
| **Outdoor** | `starter-mountain-peak.yaml` ✨ | Catena di vette innevate al tramonto + nuvole procedurali |
| **Outdoor** | `starter-foliage-canopy.yaml` ✨ | Sottobosco con dappled light e foglie translucide (`diff_trans`) |
| **Indoor** | `starter-photography-studio.yaml` | Studio fotografico con cyclorama e softbox |
| **Indoor** | `starter-cornell-box-extended.yaml` | Cornell Box arricchita — benchmark classico |
| **Indoor** | `starter-museum-gallery.yaml` | Galleria d'arte con 5 piedistalli e sculture |
| **Indoor** | `starter-kitchen-counter.yaml` | Still life su piano cucina in marmo di Carrara |
| **Indoor** | `starter-still-life-fruit.yaml` ✨ | Natura morta fiamminga (frutta, vino, ceramica satin) |
| **Indoor** | `starter-wine-cellar.yaml` | Cantina con botti, scaffali e candele emissive |
| **Indoor** | `starter-dining-room.yaml` | Sala da pranzo con tavolo, sedie e lampada |
| **Indoor** | `starter-infinite-mirror-room.yaml` | Installazione con specchi paralleli e sfere emissive |
| **Showcase** | `starter-material-showroom.yaml` | Griglia 4×4 di 16 materiali su piedistalli |
| **Showcase** | `starter-jewelry-closeup.yaml` ✨ | Macro anello con diamante, smeraldi e opale iridescente |
| **Showcase** | `starter-pool-table.yaml` | Tavolo da biliardo con bilie e sphere light |
| **Showcase** | `starter-underwater.yaml` | Fondale marino con coralli e bioluminescenza |

> ✨ Nuovi starter kit: dimostrano feature che mancavano nella collezione
> precedente (mezzi procedurali, `diff_trans`, gemme con IOR alto/thin-film,
> ceramica satin / vetro smerigliato). Tutti sfruttano i parametri di
> light hardening del motore.

**Uso:** copia il file in `scenes/`, rinominalo e renderizza subito.

```powershell
# Preview rapido (< 5 secondi)
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- \
  -i scenes/mia-scena.yaml -w 400 -H 225 -s 64 -d 4 -S 1
```

📖 **Documentazione completa:** [`starter-kits/README.md`](starter-kits/README.md)

---

> **📁 Nota — Texture:** nella cartella [`libraries/textures/`](textures/README.md)
> sono disponibili **20 texture PNG** (albedo e normal map) utilizzabili
> direttamente nei materiali della tua scena tramite i campi `texture_path`
> e `normal_map`. Non seguono il meccanismo degli `imports:` — si referenziano
> con il percorso relativo senza alcun import aggiuntivo.

---

## ⚠️ Best practice — trasformazioni su primitive

Le primitive con parametro `center:` (sphere, cylinder, cone, capsule, torus)
**non vanno combinate con `rotate:` o `scale:`**: le trasformazioni
`scale → rotate → translate` vengono sempre applicate attorno all'**origine
globale** del sistema di coordinate, non attorno al `center:` della
primitiva. Combinandoli si ottiene un riposizionamento inatteso (la primitiva
viene "scagliata" dall'origine).

```yaml
# ❌ Sbagliato — il rotate ruota la sfera attorno all'origine, non al suo centro
- type: "sphere"
  center: [0, 1.5, 0]
  radius: 0.3
  rotate: [0, 0, 90]

# ✅ Corretto — la sfera è in (0,0,0), si scala/ruota localmente, poi posiziona
- type: "sphere"
  radius: 0.3
  rotate: [0, 0, 90]
  translate: [0, 1.5, 0]
```

`box` e `mesh` non hanno `center:` e usano nativamente `translate:`, quindi
sono immuni dal problema. Vedi `objects/README.md` per dettagli.

---

## Combinare le Librerie

Le quattro librerie sono progettate per lavorare insieme. Alcuni pattern tipici:

### Showcase di materiali
```yaml
imports:
  - path: "libraries/materials/metals.yaml"
  - path: "libraries/lights/studio-product.yaml"
```

### Scena indoor completa
```yaml
imports:
  - path: "libraries/materials/woods.yaml"
  - path: "libraries/objects/furniture.yaml"
  - path: "libraries/objects/lighting.yaml"
  - path: "libraries/lights/interior-warm.yaml"
```

### Scena outdoor al tramonto
```yaml
imports:
  - path: "libraries/materials/stones.yaml"
  - path: "libraries/objects/outdoor.yaml"
  - path: "libraries/lights/outdoor-sunset.yaml"
```

### Galleria d'arte
```yaml
imports:
  - path: "libraries/materials/ceramics.yaml"
  - path: "libraries/materials/metals.yaml"
  - path: "libraries/objects/decorative-objects.yaml"
  - path: "libraries/lights/museum-gallery.yaml"
```

---

## Parametri di Render di Riferimento

Profili canonici allineati a [Profili di Rendering](../../docs/reference/profili-di-rendering.md):

| Profilo | CLI | Tempo stimato |
|---------|-----|---------------|
| Smoke test | `-w 160 -H 90 -s 1 -d 5` | < 1 s |
| Preview | `-w 400 -H 225 -s 64 -d 4 -S 1` | < 5 s |
| Standard | `-w 800 -H 450 -s 256 -d 6` | 1–3 min |
| Final | `-w 1920 -H 1080 -s 1024 -d 8 -S 4` | 10–20 min |
| Ultra (4K) | `-w 3840 -H 2160 -s 1600 -d 8 -S 4` | 40+ min |

> Per scene glass-heavy / indirect-dominant (Cornell Box, Kitchen Counter,
> Wine Cellar) alza a `-d 16–20`. Per `starter-infinite-mirror-room.yaml`
> usa `-d 32`.
