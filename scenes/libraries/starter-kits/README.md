# 🎬 Starter Kit — Scene Pronte all'Uso

Questa raccolta contiene **18 scene YAML complete**, pensate come punto di partenza per creare nuove scene. Ogni file è autonomo, dettagliato e renderizzabile immediatamente.

## Come Usare uno Starter Kit

1. **Copia** il file nella cartella `scenes/`
2. **Rinomina** il file con il nome della tua scena
3. **Scegli** un world e un set di luci (decommentando le alternative)
4. **Seleziona** la camera con `--camera <nome>` sulla CLI
5. **Aggiungi** i tuoi oggetti nella sezione `entities`
6. **Renderizza** con un draft veloce per verificare:
   ```
   dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- -i scenes/la-tua-scena.yaml -w 480 -H 270 -s 4 -d 10 -S 2
   ```

## Struttura di ogni file

Ogni starter kit segue la stessa struttura:

- **Commento descrittivo** in cima con ambientazione, stile e parametri di render consigliati
- **World attivo** + alternative commentate per cambiare atmosfera
- **Più camere** con nomi descrittivi, selezionabili da CLI
- **Materiali** realistici e vari (Disney BSDF, dielectric, metal, texture procedurali)
- **Template** per oggetti riutilizzabili (alberi, mobili, lampade, ecc.)
- **Entities** che compongono la scenografia base
- **Luci attive** + schemi alternativi commentati

Per cambiare atmosfera basta commentare il world/lights attivo e decommentare un'alternativa. Le combinazioni consigliate sono indicate nei commenti del file.

---

## 📋 Catalogo delle Scene

### Outdoor

| File | Scena | Descrizione |
|------|-------|-------------|
| `starter-desert-highway.yaml` | **Desert Highway** | Strada nel deserto americano con guard rail metallici, cactus saguaro (template), massi erosi (ellissoidi), striscia gialla tratteggiata. Cielo: mezzogiorno / golden hour / tramonto / notte. |
| `starter-snowy-clearing.yaml` | **Snowy Clearing** | Radura invernale con abeti stilizzati (template: 3 coni + neve), lago ghiacciato (disk dielectric), rocce innevate (template), tronco caduto. Cielo: coperto / mattina sole / notte / tramonto rosa. |
| `starter-zen-garden.yaml` | **Zen Garden** | Giardino giapponese contemplativo. Rocce asimmetriche (ellissoidi), lanterna tōrō (template con emissivo), tsukubai ciotola d'acqua (CSG sfera cava), ponticello in legno rosso, bambù con nodi (tori). |
| `starter-ancient-ruins.yaml` | **Ancient Ruins** | Rovine di tempio classico. Colonne doriche (template intere e spezzate), architrave, arco in pietra (CSG subtraction), piedistalli con sfere di bronzo, macerie sparse, edera stilizzata. |
| `starter-floating-islands.yaml` | **Floating Islands** | Isole sospese nel vuoto in cielo fantasy. Basi rocciose, alberi a palloncino (template), cascata dielectric, ponticello di corda, cristalli magici emissivi. Cielo: viola-arancio / pastello / notte stellata. |
| `starter-golden-hour.yaml` | **Golden Hour** | Cielo procedurale a gradiente con disco solare basso sull'orizzonte, luce direzionale calda radente. Suolo intercambiabile: prato (3 varianti), oceano metallico o terreno a scacchi. |
| `starter-sunset.yaml` | **Tramonto Drammatico** | Orizzonte aperto con sfondo monocromatico arancio e luce direzionale calda monocromatica. Suolo metallico riflettente di default; varianti prato e terreno incluse. Luce singola ad alta intensità. |

### Indoor

| File | Scena | Descrizione |
|------|-------|-------------|
| `starter-photography-studio.yaml` | **Photography Studio** | Studio fotografico professionale. Cyclorama wall (fondale curvo), softbox su stativo (template), piedistallo per il soggetto, setup 3-point lighting. Varianti: high key / dramatic / neon bicolore. |
| `starter-cornell-box-extended.yaml` | **Cornell Box Extended** | La classica Cornell Box arricchita. Pavimento a scacchi, pareti rosso/verde, pannello emissivo, sfera di vetro, toro d'oro Disney BSDF, box in legno noce. Il benchmark per eccellenza. |
| `starter-museum-gallery.yaml` | **Museum Gallery** | Galleria d'arte con 5 piedistalli espositivi. Sculture: sfera di vetro, CSG sfera-cubo in titanio, toro di bronzo, capsule intrecciate in acciaio corten, ceramica organica. Spot museali individuali. |
| `starter-kitchen-counter.yaml` | **Kitchen Counter** | Still life su piano cucina in marmo di Carrara. Bicchiere d'acqua (CSG), ciotola con frutta (CSG + sfere), bottiglia di vino (template), tazza con manico (toro), tagliere in legno. Luce da finestra. |
| `starter-wine-cellar.yaml` | **Wine Cellar** | Cantina con botti (template: cilindro + tori in ferro), scaffali con bottiglie coricati, tavolo d'assaggio con bicchiere di vino, candele emissive. Atmosfera calda e intima da chiaroscuro. |
| `starter-dining-room.yaml` | **Dining Room** | Sala da pranzo classica con tavolo rettangolare, 4 sedie dettagliate (template: seduta, cuscino, schienale, gambe, traverse), vaso con fiori, lampada a sospensione. Parquet, battiscopa, pareti. |
| `starter-infinite-mirror-room.yaml` | **Infinite Mirror Room** | Installazione artistica: due specchi paralleli con sfere emissive multicolore sospese. Effetto tunnel infinito. Pavimento riflettente, sfera di vetro e sfera cromata. Usare `-d 40+` per i riflessi profondi. |

### Showcase

| File | Scena | Descrizione |
|------|-------|-------------|
| `starter-material-showroom.yaml` | **Material Showroom** | Griglia 4×4 di piedistalli cilindrici con sfere in 16 materiali diversi: oro, cromo, rame, acciaio, cristallo, ambra, rubino, vetro smerigliato, marmo, legno, cera, gomma, plastica, ceramica. Catalogo materiali perfetto. |
| `starter-chess-set.yaml` | **Chess Set** | Scacchiera completa con 6 tipi di pezzi (template: pedone, torre, cavallo, alfiere, regina, re). 32 pezzi istanziati in marmo bianco e ebano lucido. Showcase per template/istanze. |
| `starter-pool-table.yaml` | **Pool Table** | Tavolo da biliardo con panno verde, sponde legno/gomma, 11 bilie colorate lucidissime, lampada a sospensione con sphere light, stecca appoggiata. Classica da ray tracer. |
| `starter-underwater.yaml` | **Underwater** | Fondale marino con rocce, coralli ramificati (template), coralli cervello, alghe ondulanti (template capsule), bolle dielectric, stelle marine, bioluminescenza emissiva. Gradiente blu profondo. |

---

## 🎥 Camere Disponibili

Ogni starter kit include più camere selezionabili con `--camera <nome>`. Usa `--list-cameras` per vedere quelle disponibili in un file:

```
dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/la-tua-scena.yaml --list-cameras
```

Tipologie di camere presenti nei vari kit:
- **panorama / panoramica** — Vista d'insieme ampia
- **frontale / classica** — Inquadratura standard frontale
- **dettaglio / macro** — Close-up con DOF (aperture > 0)
- **zenitale** — Vista dall'alto (pianta)
- **laterale / angolare** — Prospettive secondarie
- **drammatica / dutch / low_angle** — Angolazioni cinematografiche

---

## ⚡ Parametri di Render Consigliati

Ogni file include i parametri suggeriti nel commento iniziale. Come riferimento generale:

| Livello | Larghezza | Campioni | Profondità | Shadow | Uso |
|---------|-----------|----------|------------|--------|-----|
| Test | `-w 480 -H 270` | `-s 4` | `-d 10` | `-S 2` | Verifica layout (< 5s) |
| Draft | `-w 800 -H 450` | `-s 16` | `-d 15` | `-S 4` | Bozza veloce (< 30s) |
| Preview | `-w 1280 -H 720` | `-s 64` | `-d 25` | `-S 8` | Anteprima (1-5 min) |
| Finale | `-w 1920 -H 1080` | `-s 256` | `-d 40` | `-S 16` | Produzione (10-30 min) |
| Ultra | `-w 2560 -H 1440` | `-s 512` | `-d 50` | `-S 24` | Portfolio (30+ min) |

> **Nota:** Per la scena Infinite Mirror Room usare `-d 40+` per apprezzare la profondità dei riflessi. Per scene con molto vetro (Kitchen Counter, Wine Cellar) aumentare `-d` a 30+.

---

## 🔧 Feature del Motore Utilizzate

| Feature | Scene che la usano |
|---------|-------------------|
| **CSG** (subtraction, intersection, union) | Zen Garden, Ancient Ruins, Museum Gallery, Kitchen Counter, Cornell Box, Wine Cellar, Dining Room |
| **Template / Istanze** | Desert Highway, Snowy Clearing, Zen Garden, Ancient Ruins, Floating Islands, Photography Studio, Museum Gallery, Kitchen Counter, Wine Cellar, Dining Room, Chess Set, Pool Table, Underwater |
| **Sfere ellissoidali** (scale differenziale) | Desert Highway, Snowy Clearing, Zen Garden, Ancient Ruins, Floating Islands, Underwater |
| **Disney BSDF** (metallic, roughness, clearcoat, spec_trans, subsurface, sheen) | Tutti |
| **Dielectric** (vetro, acqua, ghiaccio) | Snowy Clearing, Zen Garden, Floating Islands, Cornell Box, Kitchen Counter, Wine Cellar, Infinite Mirror Room, Material Showroom, Pool Table, Underwater |
| **Emissive** | Zen Garden, Floating Islands, Cornell Box, Wine Cellar, Infinite Mirror Room, Underwater |
| **Texture procedurali** (noise, marble, wood, checker) | Desert Highway, Snowy Clearing, Ancient Ruins, Kitchen Counter, Wine Cellar, Dining Room, Material Showroom, Chess Set, Pool Table |
| **Torus** | Zen Garden, Cornell Box, Museum Gallery, Wine Cellar, Kitchen Counter, Chess Set |
| **Capsule** | Desert Highway, Museum Gallery, Underwater |
| **Disk / Annulus** | Snowy Clearing, Zen Garden, Kitchen Counter, Underwater |
| **Cone** | Snowy Clearing, Floating Islands, Photography Studio, Pool Table, Underwater |
| **Multi-camera** | Desert Highway, Snowy Clearing, Zen Garden, Ancient Ruins, Floating Islands, Photography Studio, Cornell Box, Museum Gallery, Kitchen Counter, Wine Cellar, Dining Room, Infinite Mirror Room, Material Showroom, Chess Set, Pool Table, Underwater (Golden Hour e Tramonto hanno camera singola) |
| **Gradient Sky** (con sun disk) | Desert Highway, Snowy Clearing, Zen Garden, Ancient Ruins, Floating Islands, Underwater, Golden Hour |
| **Area Light** | Photography Studio, Museum Gallery, Kitchen Counter, Dining Room, Material Showroom, Infinite Mirror Room |
| **Sphere Light** | Wine Cellar, Pool Table, Dining Room |
| **Spot Light** | Museum Gallery, Ancient Ruins, Photography Studio, Cornell Box, Underwater |
