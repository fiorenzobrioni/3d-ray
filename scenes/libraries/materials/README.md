# Libreria Materiali — 3D-Ray

Raccolta completa di materiali PBR per il motore di ray tracing 3D-Ray.
**21 file YAML tematici** con oltre **1300 materiali** pronti all'uso,
organizzati per categoria e con doppia variante Disney/Classic. Ogni
materiale Disney usa le chiavi moderne del BSDF (`coat_roughness` +
`coat_ior` al posto del legacy `clearcoat_gloss`, `sheen_roughness` dove
appropriato, `subsurface_color` per il subsurface, `transmission_color`
+ `transmission_depth` per i vetri colorati anziché il colore
Beer-Lambert legacy).

La libreria è allineata alla versione corrente del motore (Disney 2015 +
estensioni Arnold + thin-film). Nessun materiale usa `subsurface_radius`
(parsato ma inutilizzato — vedi `docs/reference/scene-reference.md` §5).

## Sezioni VFX Extended (v2)

Tutte le 13 librerie originali hanno una sezione **EXTENDED VFX
MATERIALS (v2)** in coda che sfrutta le feature di texturing più recenti:

- **Surface displacement stack completo**: bump_map procedurale, scalar
  displacement, vector displacement (tangent/object), autobump, combo
  bump+displacement, `displacement_method: bump_only` come fallback
  sicuro per sfere (geometric displacement applica solo a mesh).
- **VFX texturing avanzato**: smooth voronoi (smoothness 0–1), voronoi
  extended outputs (f1/f2/f3/f4/f2_minus_f1/f3_minus_f1/cell/position),
  Musgrave hetero_terrain e hybrid_multifractal, color_ramp multi-stop
  (linear/smoothstep/ease/constant), marble studio (vein_sharpness,
  secondary_wave), wood studio (figure_strength, radial_anisotropy,
  knot_density, grain_scale).
- **Thin-film interferenza** per anodizzazioni, scaglie iridescenti,
  bolle di sapone, perle, opali, niobio olografico.

9 librerie NUOVE sono state aggiunte (concretes, plasters, leathers,
weathering, synthetics, liquids, minerals-gems, biological,
industrial-coatings) che usano le nuove feature fin dalla nascita.

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
ceramica smaltata (8 colori), terra sigillata (rossa, nera, ocra),
**satin** ✨ (porcellana, avorio, grigio pietra, antracite, sabbia, salvia,
terracotta — finitura cera tra opaco e smaltato, ideale per stoviglie
moderne e vasi minimal scandinavi).

**~99 materiali** · 10 categorie

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
turchese, giallo, smeraldo), gemme preziose (diamante, rubino, zaffiro,
smeraldo), semipreziose (ametista, citrino, acquamarina, topazio,
tormalina, granato, peridoto, opale), ghiaccio (chiaro, torbido, blu,
brina, neve), liquidi (acqua, vino, birra, miele, olio, latte,
glicerina), resine e sintetici (PMMA, policarbonato, epossidica,
silicone, nylon), **smerigliati / frosted** ✨ (vetro smerigliato
neutro, acidato fine, sabbiato verde, sabbiato ambra, acidato fumé,
inciso, plexi satin — tutti Disney `spec_trans` + roughness medio per
vera diffusione fisica in trasmissione, vs. il classico dielectric
liscio).

**~104 materiali** · 11 categorie

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

## Nuove librerie (Batch 1-2)

### Cementi e Asfalti — `concretes.yaml`
Cemento liscio (autolivellante, casseforme, industriale), esposto (Tadao
Ando, brutalist con casseri legno, fugato), lavorato (sabbiato, bocciardato,
graffiato), lavato a vista (chiaro/scuro), cemento armato grezzo, colorati
(ocra, antracite, terra siena, blu industriale), asfalto (fresco, consumato
light/medium/heavy, bagnato), bitume.
**~28 materiali** · 7 categorie

### Intonaci e Stucchi — `plasters.yaml`
Rasato civile (3 finiture), graffiato esterno/fine, veneziano (avorio, blu,
pompeiano, salvia, antracite — marble studio + clearcoat), marmorino
(bianco, blu polvere, terracotta — opaco), tadelakt marocchino (rosa, ocra,
menta), stucco antico (crepato, umido, rosa veneziano), calce mediterranea
(bianca, avorio, azzurra Santorini), gesso (liscio, satinato), coloratura.
**~30 materiali** · 9 categorie

### Pelli — `leathers.yaml`
Pieno fiore (5 conce), nappa morbida (4 colori), suede/scamosciato (5 tipi,
sheen 0.85), vintage invecchiata (3 patine), patent leather verniciata (3
colori), esotici (pitone naturale/albino, coccodrillo nero/marrone, struzzo,
lucertola — voronoi cell scaglie), scarpe (box calf, cordovan, militare),
sintetici ecoleather/vinile.
**~38 materiali** · 8 categorie

### Weathering Overlays — `weathering.yaml`
Overlay `over_*` mix-ready: ruggine (light/medium/heavy/streak), muschio
(sparse/dense/wet), polvere (light/heavy/gesso/terra), colature sporco,
calcare/limescale, grasso (dark/light), neve sottile (polverosa/melting),
vernice scrostata (bianca/militare via voronoi crackle), foglie morte, sale
marino, film d'acqua, macchie acqua secca, verderame patina rame.
**~28 overlays** · pensati per `type: mix` con maschera procedurale

### Minerali e Gemme Grezze — `minerals-gems.yaml`
Quarzi (trasparente/fumè/citrino/rosa/ametista), geodi (base ruvida + interni
cristallini ametista/agata), druse vector-displaced, cristalli cubici (pirite/
halite/galena), calcite islandese birifrangente, fluorite multicolore
(viola/rainbow/verde), malachite radiale, lapislazzuli, pietra di luna +
pesca (thin_film adularescenza), opali (bianco/nero/fuoco), cristalli rari
(selenite, kyanite, tormalina watermelon, granato).
**~28 materiali** · 11 categorie

### Sintetici Tecnici — `synthetics.yaml`
Fibra di carbonio (twill/plain/satin/3D/matte/rosso — anisotropic+rotation),
kevlar (giallo/nero/hybrid), vetroresina (gelcoat/grezza/trasparente),
neoprene (nero/blu/foderato), PTFE/Teflon, gomma EPDM, silicone medicale
(traslucido alto subsurface), poliuretani (rigido/flessibile), vinile auto
wrap (matte/gloss/satin/chrome/chrome-rosa/olografico), tessuti tecnici
(ripstop/cordura/gore-tex), aerogel.
**~30 materiali** · 11 categorie

### Liquidi — `liquids.yaml`
Acque (piscina/mare-costiero/profondo/torrente/fontana/tropicale/torbida/
ghiaccio), latticini (intero/scremato/panna/condensato), sangue (arterioso/
venoso/secco/coagulato), oli (motore-iridescente/oliva/semi/benzina),
alcolici (vino RG/B/rose, birra chiara/stout, whisky/vodka/rum), sciroppi
(miele/acero/caramello/melassa), bevande calde (caffè/tè/matcha/cioccolata),
succhi, liquidi industriali (refrigeranti/ammoniaca).
**~40 materiali** · 9 categorie

### Tessuti Biologici Viventi — `biological.yaml`
Pelle umana 8 varianti (caucasica chiara/scura, mediterranea, asiatica,
africana, bambino, anziana, abbronzata — subsurface multi-layer), muscolo/
fegato/lingua bovina, pelle elefante/squalo, scaglie pesce (carpa/salmone/
koi/trota — thin_film), scaglie rettile (geco/iguana/drago barbuto), piume
(copertura/piumino/corvo/struzzo/pavone iridescente), occhio (3 iridi + sclera
+ cornea), membrane sottili diff_trans (ala pipistrello/palmo rana), labbra/
gengiva/lingua umana.
**~40 materiali** · 9 categorie

### Vernici Industriali — `industrial-coatings.yaml`
Chassis auto (matte black/grey/cement, gloss black/white/rosso/blu-metallic),
clearcoat protettivo, matte anti-glare, polveri elettrostatiche RAL
(9005/9010/3020/5015/1023/6018/7035), anodizzazione alluminio (naturale/oro/
nero/blu/rosso/viola/verde via thin_film 320-640nm), zincatura (lucida/opaca/
invecchiata), cromature, smalti a fuoco (bianco/rosso/blu), gel coat marino,
termocromiche, retroriflettenti, anti-corrosive, fluo safety.
**~30 materiali** · 11 categorie

---

## Totale libreria

| File | Materiali (originali + v2) |
|------|-----------|
| metals.yaml | 128 + 10 v2 |
| ceramics.yaml | 99 + 8 v2 |
| woods.yaml | 84 + 8 v2 |
| glasses.yaml | 104 + 10 v2 |
| stones.yaml | 98 + 10 v2 |
| plastics.yaml | 95 + 8 v2 |
| fabrics.yaml | 100 + 8 v2 |
| paints.yaml | 98 + 8 v2 |
| organics.yaml | 81 + 8 v2 |
| foods.yaml | 91 + 9 v2 |
| emissives.yaml | 83 + 7 v2 |
| grounds.yaml | 66 + 8 v2 |
| concretes.yaml *(new)* | 28 |
| plasters.yaml *(new)* | 30 |
| leathers.yaml *(new)* | 38 |
| weathering.yaml *(new)* | 28 |
| minerals-gems.yaml *(new)* | 28 |
| synthetics.yaml *(new)* | 30 |
| liquids.yaml *(new)* | 40 |
| biological.yaml *(new)* | 40 |
| industrial-coatings.yaml *(new)* | 30 |
| **Totale** | **~1330** |

## Showcases dedicati

In `scenes/showcases/` ci sono showcase mirati per le nuove librerie:

- **Surface displacement**: `concretes-showcase`, `plasters-showcase`,
  `leathers-showcase`, `weathering-overlay-showcase`
- **VFX texturing**: `minerals-gems-showcase`, `synthetics-showcase`,
  `liquids-showcase`, `biological-showcase`, `industrial-coatings-showcase`
- **Studio v2** (per estensioni VFX delle librerie esistenti):
  `marbles-studio-v2-showcase`, `woods-studio-v2-showcase`,
  `glass-features-showcase`
- **Cross-library thematic**: `wet-surfaces-showcase` (liquidi + ceramiche +
  intonaci), `weathered-surfaces-showcase` (metalli + cementi + weathering
  overlays)
