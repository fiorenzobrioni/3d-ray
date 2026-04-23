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
  ambient_light: [0.02, 0.02, 0.03]
  background: [0.0, 0.0, 0.0]

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

## Le Cinque Librerie

### 🅰️ Tipografia — `typography/`

**36 glifi (A–Z, 0–9) · OBJ smooth + template YAML**

Alfabeto maiuscolo e cifre in stile geometric sans-serif, modellati in 3D
come mesh estruse (curve con `SmoothTriangle`), esportati in OBJ e avvolti
in template istanziabili per comporre parole e numeri nelle scene.

- Cap-height uniforme a 1.0, baseline a y=0, estrusione 0.15 verso −Z
- Advance width proporzionale per glifo (da 0.25 di "I" a 1.15 di "W")
- Template `glyph_A` … `glyph_Z` e `glyph_0` … `glyph_9` con materiale
  default `font_primary` sovrascrivibile per istanza
- Rigenerabile via `dotnet run --project src/Tools/TypographyGen`

**Uso base:**
```yaml
imports:
  - path: "libraries/typography/alphabet.yaml"
entities:
  - { type: "instance", template: "glyph_C", translate: [0.0,  0, 0] }
  - { type: "instance", template: "glyph_I", translate: [0.9,  0, 0] }
  - { type: "instance", template: "glyph_A", translate: [1.2,  0, 0] }
  - { type: "instance", template: "glyph_O", translate: [2.1,  0, 0] }
```

📖 **Documentazione completa:** [`typography/README.md`](typography/README.md)

---

### 🎨 Materiali — `materials/`

**12 file · 800+ materiali PBR**

Raccolta completa di materiali fisicamente corretti con varianti Disney BSDF
(`dis_`) e Classic (`cls_`). Ogni categoria è un file separato importabile
individualmente.

| File | Contenuto |
|------|-----------|
| `metals.yaml` | Oro, argento, rame, bronzo, acciaio, alluminio, titanio, cromo... (120 mat.) |
| `ceramics.yaml` | Porcellana, maiolica, terracotta, grès, raku, celadon... (100 mat.) |
| `woods.yaml` | Rovere, noce, pino, ebano, teak, bambù, compensato... (90 mat.) |
| `stones.yaml` | Marmi, graniti, ardesia, travertino, basalto, quarzo... (90 mat.) |
| `glass.yaml` | Vetro trasparente, colorato, smerigliato, specchio, cristallo... (60 mat.) |
| `plastics.yaml` | ABS, PVC, acrilico, nylon, silicone, gomma, resina... (80 mat.) |
| `fabrics.yaml` | Cotone, velluto, seta, denim, lino, pelle, feltro... (70 mat.) |
| `emissives.yaml` | LED, neon, plasma, lava, brace, bioluminescenza... (50 mat.) |
| `organics.yaml` | Pelle umana, legno vivo, foglie, muschio, corallo... (60 mat.) |
| `procedural.yaml` | Checker, noise, marmo, legno, gradiente — texture parametriche (40 mat.) |
| `special.yaml` | Materiali fisici estremi: black body, retroriflettente, iridescente... (30 mat.) |
| `mix-presets.yaml` | MixMaterial precalibrati: usura, strati, transizioni (50 mat.) |

**Convenzione dei nomi:** `prefisso_categoria_variante`
- `dis_` → Disney BSDF (close-up, effetti PBR avanzati)
- `cls_` → Classic (scene grandi, render veloci)
- *(nessuno)* → Emissivo

📖 **Documentazione completa:** [`materials/README.md`](materials/README.md)

---

### 📦 Oggetti — `objects/`

**12 file · 154 template · ~230 materiali dedicati**

Template di oggetti composti pronti all'istanziazione. Ogni file contiene
oggetti costruiti con primitive, CSG, torus e gruppi annidati. I materiali
dedicati sono inclusi nel file e si fondono con quelli della scena.

| File | Template | Descrizione |
|------|----------|-------------|
| `furniture.yaml` | 10 | Tavoli, sedie, scaffali, lampade, divani, comodini |
| `decorative-objects.yaml` | 10 | Vasi, cornici, candele, orologi, libri, sfere decorative |
| `tableware.yaml` | 10 | Piatti, bicchieri, posate, teiere, tazze, bottiglie |
| `architecture.yaml` | 14 | Colonne, archi, scale, finestre, portali, cornici |
| `mechanical.yaml` | 14 | Ingranaggi, pistoni, molle, viti, tubi, giunti |
| `jewelry.yaml` | 14 | Anelli, collane, orecchini, spille, bracciali, gemme |
| `lighting.yaml` | 14 | Lampade da tavolo/terra, plafoniere, faretti, lanterne |
| `laboratory.yaml` | 14 | Beute, burette, microscopi, bilance, bunsen, storte |
| `musical.yaml` | 14 | Violino, chitarra, pianoforte, tromba, tamburo, flauto |
| `outdoor.yaml` | 14 | Fontane, panchine, lampioni, staccionate, alberi, fioriere |
| `chess.yaml` | 11 | Set Staunton completo: re, regina, alfiere, cavallo, torre, pedone |
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

**18 scene YAML complete e renderizzabili immediatamente**

Scene già pronte con world, camere multiple, materiali, template, luci e
oggetti. Pensate come punto di partenza: copiare, rinominare e modificare.

| Categoria | File | Scena |
|-----------|------|-------|
| **Outdoor** | `starter-desert-highway.yaml` | Strada nel deserto americano con cactus e guardrail |
| **Outdoor** | `starter-snowy-clearing.yaml` | Radura invernale con abeti, lago ghiacciato, rocce |
| **Outdoor** | `starter-zen-garden.yaml` | Giardino giapponese con lanterna tōrō e tsukubai |
| **Outdoor** | `starter-ancient-ruins.yaml` | Rovine di tempio greco con colonne e archi CSG |
| **Outdoor** | `starter-floating-islands.yaml` | Isole fantasy sospese in cielo viola-arancio |
| **Outdoor** | `starter-golden-hour.yaml` | Cielo a gradiente procedurale con sole basso, luce calda radente |
| **Outdoor** | `starter-sunset.yaml` | Orizzonte aperto con sfondo arancio e suolo metallico riflettente |
| **Indoor** | `starter-photography-studio.yaml` | Studio fotografico con cyclorama e softbox |
| **Indoor** | `starter-cornell-box-extended.yaml` | Cornell Box arricchita — benchmark classico |
| **Indoor** | `starter-museum-gallery.yaml` | Galleria d'arte con 5 piedistalli e sculture |
| **Indoor** | `starter-kitchen-counter.yaml` | Still life su piano cucina in marmo di Carrara |
| **Indoor** | `starter-wine-cellar.yaml` | Cantina con botti, scaffali e candele emissive |
| **Indoor** | `starter-dining-room.yaml` | Sala da pranzo con tavolo, sedie e lampada |
| **Indoor** | `starter-infinite-mirror-room.yaml` | Installazione con specchi paralleli e sfere emissive |
| **Showcase** | `starter-material-showroom.yaml` | Griglia 4×4 di 16 materiali su piedistalli |
| **Showcase** | `starter-chess-set.yaml` | Scacchiera completa — 32 pezzi Staunton |
| **Showcase** | `starter-pool-table.yaml` | Tavolo da biliardo con bilie e sphere light |
| **Showcase** | `starter-underwater.yaml` | Fondale marino con coralli e bioluminescenza |

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
