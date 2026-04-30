# Libreria Materiali — 3D-Ray

Raccolta completa di materiali PBR per il motore di ray tracing 3D-Ray.
12 file YAML tematici con oltre **1080 materiali** pronti all'uso, organizzati
per categoria e con doppia variante Disney/Classic. Ogni materiale Disney
usa le chiavi moderne del BSDF (`coat_roughness` + `coat_ior` al posto del
legacy `clearcoat_gloss`, `sheen_roughness` dove appropriato, `subsurface_color`
per il subsurface, `transmission_color` + `transmission_depth` per i vetri
colorati anziché il colore Beer-Lambert legacy).

La libreria è allineata alla versione corrente del motore (Disney 2015 +
estensioni Arnold + thin-film). Nessun materiale usa `subsurface_radius`
(parsato ma inutilizzato — vedi `docs/reference/scene-reference.md` §5).

---

## Come usare nelle scene

Importa uno o più file nella sezione `imports:` della tua scena YAML.
I path sono relativi alla directory della scena (tipicamente `scenes/`):

```yaml
imports:
  - path: "libraries/materials/metals.yaml"
  - path: "libraries/materials/woods.yaml"
  - path: "libraries/materials/emissives.yaml"

entities:
  - name: "sfera_oro"
    type: "sphere"
    center: [0, 1, 0]
    radius: 1.0
    material: "dis_oro_lucido"      # ← ID dalla libreria metals
```

Puoi importare quante librerie vuoi: il loader le unisce automaticamente.
Se definisci un materiale locale con lo stesso ID, il tuo sovrascrive quello
importato (last-write-wins).

---

## Convenzione dei nomi

Ogni materiale segue lo schema: **`prefisso_categoria_variante`**

### Prefisso tipo (3 lettere)

| Prefisso | Tipo YAML | Quando usare |
|----------|-----------|--------------|
| `dis_` | `disney` | Oggetti protagonisti, close-up, effetti PBR avanzati (clearcoat, sheen, subsurface, spec_trans). Richiede ≥128 spp. |
| `cls_` | `lambertian`, `metal`, `dielectric` | Superfici grandi, sfondo, render veloci. Zero rumore da lobi multipli. |
| *(nessuno)* | `emissive` | Sorgenti luminose. Nessuna distinzione dis/cls — tipo unico. |

### Esempi di lettura

```
dis_oro_lucido          → Disney, oro, finitura lucida
cls_carrara_lucido      → Classic, marmo Carrara, lucido (metal+fuzz)
dis_velluto_bordeaux    → Disney, velluto, colore bordeaux
cls_checker_bn          → Classic, checker, bianco/nero
neon_rosa               → Emissivo, neon, colore rosa
led_caldo               → Emissivo, LED, temperatura calda
```

---

## I file della libreria

### Metalli — `metals.yaml`
Leghe nobili e industriali: oro (24K, rosa, bianco, antico), argento,
rame (lucido, ossidato, martellato), bronzo, ottone, acciaio (inox,
damasco, arrugginito), ferro (battuto, ghisa), alluminio (anodizzato
in vari colori), titanio (anodizzato blu, viola, oro), cromo (specchio,
nero PVD), platino, nichel, zinco, stagno/peltro, corten.
Include alias di retrocompatibilità con il vecchio `metals.yaml`.

**128 materiali** · 15 categorie

### Ceramiche — `ceramics.yaml`
Porcellana (bianca, avorio, blu cobalto, nera, rosa, craquelé, crepuscolo), bone china,
maiolica (azzurra, gialla, verde, arancio), terracotta (naturale, invetriata,
smaltata verde), grès/stoneware (tenmoku, shino, celadon), raku (rame,
nero, iridescente), celadon (classico, chiaro, scuro), biscotto/bisque,
ceramica smaltata (8 colori), terra sigillata (rossa, nera, ocra).

**88 materiali** · 9 categorie

### Legni — `woods.yaml`
Latifoglie chiare (acero, betulla, frassino, faggio), medie (quercia,
ciliegio, teak, iroko), scure (noce, mogano, wengé, palissandro), legni
neri (ebano, ebano Macassar), conifere (pino, abete, cedro, larice),
esotici (zebrano, padouk, amaranto, bocote), trattati (sbiancato,
shou sugi ban, barnwood, tinto nero/grigio). Ogni essenza in più
finiture: grezzo, olio, cera, verniciato, laccato.

**72 materiali** · 7 categorie

### Vetri e trasparenti — `glasses.yaml`
Vetri industriali (soda-lime, borosilicato, float, temperato), ottici
(crown, flint, dense flint), cristallo (piombo, Swarovski, fumé, rosa),
colorati (rosso, blu cobalto, verde bottiglia, ambra, viola Murano,
turchese, giallo, smeraldo), smerigliati (satinato, sabbiato, acidato,
opalino), gemme preziose (diamante, rubino, zaffiro, smeraldo),
semipreziose (ametista, citrino, acquamarina, topazio, tormalina,
granato, peridoto, opale), ghiaccio (chiaro, torbido, blu, brina, neve),
liquidi (acqua, vino, birra, miele, olio, latte, glicerina), resine e
sintetici (PMMA, policarbonato, epossidica, silicone, nylon).

**96 materiali** · 10 categorie

### Pietre e minerali — `stones.yaml`
Marmi bianchi (Carrara, Calacatta, Statuario, Thassos), scuri (Nero
Marquinia, Nero Belgio, Port Laurent), colorati (Verde Guatemala,
Rosso Levanto, Rosa Portogallo, Blu Sodalite, Giallo Siena, Arabescato),
graniti (grigio, rosa, nero assoluto, bianco, fiammato), travertino,
ardesia (grigia, nera, verde), onice (bianco, miele, verde — traslucenti),
alabastro, arenaria (dorata, rossa, grigia), basalto e lava, calcestruzzo
(grezzo, liscio, microcemento), mattoni (rosso, arancio, giallo, clinker).

**87 materiali** · 12 categorie

### Plastiche e polimeri — `plastics.yaml`
ABS (lucido/opaco, 7 colori LEGO), policarbonato, acrilico/PMMA, PVC,
nylon (naturale, nero, SLS), PLA stampa 3D (7 colori), teflon/PTFE,
polietilene HDPE/LDPE, polipropilene, bachelite (nera, marrone, rossa),
gomma naturale (nera, grigia, rossa, bianca, blu), gomma siliconica
(bianca, rossa, trasparente, medicale), EVA/gommapiuma (5 colori),
vinile/ecopelle (nero, marrone, bianco, rosso).

**95 materiali** · 14 categorie

### Tessuti e pelli — `fabrics.yaml`
Velluto (7 colori), seta (6 colori), raso/satin (5 colori), cotone
(bianco, nero, grezzo, rosso, blu), lino, lana (5 colori), denim (4
tonalità), tweed, feltro (4 colori), pelle vacchetta (marrone, nera,
cognac, chiara, invecchiata), scamosciata (4 colori), pelle verniciata
(nera, rossa, bianca), cuoio (naturale, scuro, grezzo), neoprene,
canvas/tela (3 colori), organza/tulle (4 varianti semi-trasparenti).

**100 materiali** · 16 categorie

### Vernici e finiture — `paints.yaml`
Auto metallizzata (8 colori), auto pastello (6 colori), auto perlata
(bianco perla, champagne, blu, rosso), lacca (pianoforte nero/bianco,
rossa cinese, ciliegia, blu notte), smalto lucido/satinato/opaco,
primer (grigio, bianco, rosso antiruggine), chalk paint (6 colori),
pittura murale (6 colori), spray (5 varianti), vernice epossidica,
vernice a polvere.

**98 materiali** · 13 categorie

### Organici e naturali — `organics.yaml`
Cera (api, paraffina, soia, candele colorate), ambra (chiara, scura,
rossa, grezza), avorio e osso (lucido, invecchiato, grezzo, levigato),
corno e tartaruga, guscio d'uovo, corallo (rosso, rosa, bianco, nero),
madreperla (bianca, rosa, abalone), conchiglia, sughero, carta (bianca,
kraft, riso, giornale, velina, patinata), cartone, pergamena/vellum,
sapone (bianco, miele, lavanda, oliva), bambù.

**81 materiali** · 14 categorie

### Alimenti e bevande — `foods.yaml`
Cioccolato (fondente, latte, bianco, fuso, cacao), frutta (mela rossa/
verde, arancia, limone, uva, pesca, ciliegia, banana, fragola), verdura
(pomodoro, peperoni, melanzana, zucca), formaggi (parmigiano, cheddar,
brie, gouda, mozzarella, gorgonzola), pane e pasta, dolci (zucchero,
caramello, marzapane, glassa, meringa), burro e grassi, liquidi (acqua,
latte, vino, birra, olio, miele, succo, caffè), condimenti, carne e pesce.

**91 materiali** · 10 categorie

### Emissivi e luci — `emissives.yaml`
Temperatura colore calibrata (1800K–7500K), LED (caldo, neutro, freddo,
daylight, warm dim, strip), incandescenza (tungsteno 40/60/100W, Edison
vintage, alogena), fluorescente (warm/cool/daylight/verdastro/rosa),
neon colorati (11 colori), fiamme (candela, torcia, focolare, falò, gas,
saldatura), braci e lava (braci, lava, ferro rovente/ciliegia/bianco),
schermi (monitor, warm, cinema, OLED, gaming), insegne e display,
effetti speciali (magia, portale, laser, plasma, radioattivo),
bioluminescenza (lucciola, medusa, plancton, fungo).

**83 materiali** · 11 categorie

### Pavimenti e terreni — `grounds.yaml`
Checker (7 pattern), parquet (rovere chiaro/scuro, noce, teak, wengé,
verniciato), piastrelle (gres, cotto), marmo pavimento, cemento e
asfalto (grezzo, liscio, nuovo, vecchio, bagnato), terra e fango,
sabbia (dorata, bianca, deserto, bagnata, vulcanica), ghiaia, erba
(prato, secca, campo, muschio), neve (fresca, compatta, sporca),
moquette (5 colori), acqua (calma, scura, pozzanghera).
**Prevalenza classic** — 86% dei materiali sono lambertian/metal.

**66 materiali** · 12 categorie

---

## Disney vs Classic — quando usare cosa

| Scenario | Scelta | Motivazione |
|----------|--------|-------------|
| Pavimento, muro, sfondo | `cls_` | Superficie grande → rumore Disney domina l'immagine |
| Metallo puro (metallic=1.0) | `dis_` o `cls_` | Il Disney metallico ha un solo lobo → stesso rumore del classic |
| Oggetto protagonista in primo piano | `dis_` | Clearcoat, sheen, subsurface fanno la differenza |
| Render draft / preview | `cls_` | Converge in 32–64 spp |
| Render finale ≥256 spp | `dis_` dove serve | Il rumore extra è accettabile |
| Vetro chiaro e semplice | `cls_` (dielectric) | Più pulito e veloce |
| Vetro smerigliato, colorato, opale | `dis_` | Roughness + spec_trans non disponibili in dielectric |
| Tessuto (velluto, seta) | `dis_` | Sheen è impossibile da ottenere con classic |
| Materiale traslucente (cera, pelle) | `dis_` | Subsurface è impossibile con classic |

**Regola pratica**: in una scena tipica, usa classic per il 70–80% delle
superfici (pavimento, muri, tavoli, sfondo) e Disney solo per i 2–3 oggetti
protagonisti. Questo bilancia qualità visiva e tempo di rendering.

---

## Sample count consigliati

| Mix materiali | Preview | Draft | Produzione |
|---------------|---------|-------|------------|
| Solo classic | 16 spp | 32 spp | 128 spp |
| Classic + Disney | 32 spp | 64 spp | 256 spp |
| Tutto Disney | 64 spp | 128 spp | 512 spp |

---

## Totale libreria

| File | Materiali |
|------|-----------|
| metals.yaml | 128 |
| ceramics.yaml | 88 |
| woods.yaml | 72 |
| glasses.yaml | 96 |
| stones.yaml | 87 |
| plastics.yaml | 95 |
| fabrics.yaml | 100 |
| paints.yaml | 98 |
| organics.yaml | 81 |
| foods.yaml | 91 |
| emissives.yaml | 83 |
| grounds.yaml | 66 |
| **Totale** | **1085** |
