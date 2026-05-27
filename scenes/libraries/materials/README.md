# Libreria Materiali — 3D-Ray

Raccolta completa di materiali per il motore 3D-Ray: **20 file YAML
tematici** con **1450 materiali** pronti all'uso, organizzati in due
varianti (Disney BSDF e Classic) e in una collezione di overlay
weathering + ricette mix preconfezionate.

Tutti i materiali sono allineati alla versione corrente del motore:
Disney BSDF a 33 parametri (sheen Charlie + dual-path clearcoat con
`coat_ior` esplicito + `spec_trans` con Beer-Lambert `transmission_color`
+ `transmission_depth` + anisotropia + thin-film + `diff_trans` /
`flatness` / `thin_walled`), texture procedurali production-grade
(marble multi-vein con IQ warp, wood con latewood asimmetrico + figure
+ pori, voronoi con 11 output, Musgrave hetero/hybrid, noise FBM/ridged,
color ramp multi-stop), e displacement stack completo (bump → subdivision
→ scalar → vector → autobump).

## Convenzione dei nomi

Ogni materiale segue lo schema **`prefisso_categoria_variante`**, con
4 prefissi distinti:

| Prefisso | Tipo YAML sottostante | Quando usare |
|----------|----------------------|--------------|
| `dis_` | `disney` | Oggetti protagonisti, close-up, effetti PBR avanzati (clearcoat, sheen, subsurface, spec_trans, thin_film). Richiede sample count più alto. |
| `cls_` | `lambertian` · `metal` · `dielectric` | Superfici grandi / sfondo / render rapidi. Il tipo sotto dipende dal lobo dominante (vedi più sotto), ma il prefisso è unico — chi importa la libreria non deve sapere quale tipo gira sotto. |
| `over_` | `disney` | Overlay weathering (ruggine, muschio, polvere…) pensati per essere applicati ad altri materiali via `type: mix`. Solo in `weathering.yaml`. |
| `mix_` | `mix` | Preset compositi pronti all'uso che combinano un `dis_*` base con un `over_*` overlay e una maschera procedurale. Solo in `mix-recipes.yaml`. |

### Regola di scelta del tipo Classic

Per ogni `dis_*`, la versione `cls_*` (quando esiste) è scelta in base
al **lobo Disney dominante**:

| Lobo Disney dominante | Tipo Classic | Esempi |
|----------------------|--------------|--------|
| Diffuse / subsurface basso | `lambertian` | Porcellana opaca, calcestruzzo, terra, cotone, carta, frutta opaca, foglia, bisque, cuoio grezzo |
| Specular con `metallic ≥ 0.5` | `metal` (`color` + `fuzz`) | Oro, argento, rame, bronzo, ottone, acciaio, ferro, alluminio, titanio, cromo, nichel, zinco, smalti specchianti, peltro |
| Specular con `spec_trans ≥ 0.5` | `dielectric` (`refraction_index` + `color`) | Vetri industriali, ottici, cristalli, acqua/liquidi trasparenti, ghiaccio chiaro, gemme pulite |

Quando TUTTI i lobi Disney non-base portano informazione visiva
irrinunciabile (anodizzazioni con `thin_film`, opali iridescenti,
clearcoat ≥ 0.8, sheen Charlie, subsurface significativo, anisotropia
direzionale, color_ramp evolutivo, voronoi pattern), il Classic
perderebbe identità del materiale → si omette. Le librerie
`industrial-coatings`, `synthetics`, `minerals-gems` hanno per questo
motivo `Classic: 0`.

## Come usare nelle scene

Importa una o più librerie nella sezione `imports:` della scena. I path
sono relativi alla directory della scena (tipicamente `scenes/`):

```yaml
imports:
  - { path: "libraries/materials/metals.yaml" }
  - { path: "libraries/materials/woods.yaml" }
  - { path: "libraries/materials/weathering.yaml" }
  - { path: "libraries/materials/mix-recipes.yaml" }

entities:
  - name: "anello_oro"
    type: "sphere"
    center: [0, 1, 0]
    radius: 1.0
    material: "dis_oro_lucido"            # dal Disney section di metals
  - name: "scrivania"
    type: "box"
    min: [-1, 0, -1]
    max: [1, 0.05, 1]
    material: "cls_quercia"               # dal Classic section di woods
  - name: "cancello_arrugginito"
    type: "box"
    min: [-2, 0, 0]
    max: [2, 1, 0.05]
    material: "mix_acciaio_arrugginito_medio"   # ricetta mix preconfezionata
```

Il loader unisce automaticamente gli imports. Se definisci un materiale
locale con lo stesso ID, il tuo sovrascrive l'importato (last-write-wins).

## I file della libreria

Ogni file ha header standard con conteggi reali, due macro-sezioni
(`DISNEY` → `CLASSIC`, dove applicabile) e sottocategorie nello stesso
ordine in entrambe.

| File | Disney | Classic (lam · met · die) | Totale |
|------|-------:|--------------------------:|-------:|
| `metals.yaml` | 70 | 4 · 57 · 0 | 131 |
| `ceramics.yaml` | 58 | 22 · 32 · 0 | 112 |
| `plastics.yaml` | 66 | 39 · 0 · 0 | 105 |
| `glasses.yaml` | 74 | 0 · 0 · 27 | 101 |
| `fabrics.yaml` | 73 | 28 · 0 · 0 | 101 |
| `foods.yaml` | 72 | 24 · 0 · 4 | 100 |
| `organics.yaml` | 70 | 28 · 0 · 0 | 98 |
| `paints.yaml` | 68 | 25 · 0 · 0 | 93 |
| `stones.yaml` | 65 | 23 · 0 · 0 | 88 |
| `woods.yaml` | 60 | 27 · 0 · 0 | 87 |
| `grounds.yaml` | 20 | 51 · 2 · 2 | 75 |
| `liquids.yaml` | 44 | 0 · 0 · 9 | 53 |
| `plasters.yaml` | 29 | 21 · 0 · 0 | 50 |
| `leathers.yaml` | 37 | 9 · 0 · 0 | 46 |
| `industrial-coatings.yaml` | 43 | — | 43 |
| `concretes.yaml` | 23 | 19 · 0 · 0 | 42 |
| `synthetics.yaml` | 34 | — | 34 |
| `minerals-gems.yaml` | 30 | — | 30 |
| `weathering.yaml` | 26 (over_*) | — | 26 |
| `mix-recipes.yaml` | — | 35 mix_* | 35 |
| **Totale** | **936** | **320 · 91 · 42 = 453** | **1450** |

### `metals.yaml`
Oro (24K, rosa, bianco, antico), argento (sterling, ossidato, brunito),
rame (lucido, antico, verderame, martellato, patinato heritage con
color_ramp 4-stop), bronzo, ottone (incluso patinato verderame), acciaio
(lucido, satinato, spazzolato con anisotropic, spazzolato circolare,
carbonioso, damasco wootz via voronoi f3-f1, arrugginito), ferro
(battuto, forgiato, ghisa, ghisa stagionata, lucido, martellato a mano
con displacement scalare), alluminio (lucido, satinato, spazzolato,
anodizzato nero/rosso/blu con clearcoat 1.5), titanio (naturale, lucido,
anodizzato blu/viola/oro via thin_film 250/420/540nm), cromo (specchio,
satinato, PVD nero), platino, nichel, zinco, peltro e stagno (incluso
colato grezzo), corten (fresco/maturo/scuro), mercurio liquido, niobio
olografico (thin_film 580nm).

### `ceramics.yaml`
Porcellana (bianca, avorio, blu cobalto, nera, rosa, craquelé con
voronoi f2-f1, crepuscolo), bone china, maiolica (azzurra, gialla,
verde, arancio, antica patinata 4-stop), terracotta (naturale, rossa,
chiara, scura, invetriata, smaltata verde, grezza con FBM +
displacement), grès / stoneware (tenmoku, shino, celadon), raku (rame,
nero con voronoi + bump, bianco, iridescente con thin_film 420nm),
celadon (classico, chiaro, scuro, studio con FBM 4-stop, crackle con
voronoi f2-f1), biscotto/bisque, smaltata (8 colori), terra sigillata
(rossa, nera, ocra), satin (porcellana, avorio, grigio, nero, sabbia,
salvia, terracotta).

### `woods.yaml`
Latifoglie chiare (acero, betulla, frassino, faggio), medie (quercia,
ciliegio, teak, iroko), scure (noce, mogano, wengé, palissandro), legni
neri (ebano, macassar), conifere (pino, abete, cedro, larice), esotici
(zebrano, padouk, amaranto, bocote), trattati (sbiancato, shou-sugi-ban,
barnwood, tinto), studio (curly, flame, bird's eye, burl, quartersawn).
Texture wood production-grade con latewood asimmetrico, pori (per
quercia/frassino/noce), nodi (per pino/abete), figure (curly/flame/
ribbon), color_ramp gradiente sapwood→heartwood.

### `glasses.yaml`
Vetri industriali (soda-lime, borosilicato, float, temperato), ottici
(crown, flint), cristallo (piombo, Swarovski, fumè, rosa), colorati
(rosso/blu/verde/ambra/viola Murano/turchese/smeraldo con `spec_trans`
+ `transmission_color` + `transmission_depth` Beer-Lambert), gemme
preziose (diamante, rubino, zaffiro, smeraldo), semipreziose, ghiaccio
(chiaro, torbido, blu, brina), liquidi trasparenti, resine sintetiche,
smerigliati / frosted (`spec_trans` + roughness medio per vera
diffusione in trasmissione, vs. dielectric liscio).

### `stones.yaml`
Marmi bianchi (Carrara, Calacatta, Statuario, Thassos), scuri (Nero
Marquinia, Nero Belgio, Port Laurent), colorati (Verde Guatemala,
Rosso Levanto, Rosa Portogallo, Blu Sodalite, Giallo Siena, Arabescato),
graniti, travertino, ardesia, onice e alabastro (con subsurface),
arenaria (dorata, rossa, grigia), basalto e lava, calcestruzzo grezzo,
mattoni (rosso, arancio, giallo, clinker), pietra spaccata.

### `plastics.yaml`
ABS lucido/opaco (incluso set LEGO), policarbonato, acrilico/PMMA, PVC,
nylon, PLA stampa 3D, teflon/PTFE, polietilene HDPE/LDPE,
polipropilene, bachelite, gomma naturale, silicone medicale
(con subsurface), EVA/gommapiuma, vinile/ecopelle.

### `fabrics.yaml`
Velluto e seta (sheen Charlie + anisotropic), raso/satin, cotone, lino,
lana, denim, tweed, feltro, neoprene, canvas, organza/tulle
(`diff_trans` + `thin_walled` per controluce).

### `paints.yaml`
Auto metallizzata, pastello, perlata cangiante (thin_film), lacca a
specchio pianoforte (clearcoat 0.9+), urushi, smalti lucido/satinato/
opaco, primer, chalk paint, pittura murale, spray, epossidiche, vernici
a polvere, orange-peel automotive (coat con bump voronoi smooth).

### `organics.yaml`
Cera (api, paraffina, soia con subsurface), ambra, avorio, osso, corno,
guscio d'uovo, corallo, madreperla (thin_film), conchiglia, sughero,
carta (kraft, riso, giornale, velina, patinata), cartone, pergamena,
sapone (con SSS), bambù.

### `foods.yaml`
Cioccolato (fondente, latte, bianco, fuso con subsurface), frutta
(mela, arancia, limone, uva, pesca, ciliegia con clearcoat), verdura,
formaggi (parmigiano, brie, gorgonzola con SSS), pane, pasta, dolci
(glassa, meringa, caramello), burro e grassi.

### `grounds.yaml`
Checker, parquet (rovere, noce, teak, wengé, verniciato con `wood`
production-grade), piastrelle, marmo pavimento (con `marble`
production-grade), cemento, asfalto (asciutto/bagnato con clearcoat),
terra, sabbia (dorata, deserto, vulcanica), ghiaia, erba (prato,
secca, muschio), neve (fresca, sporca), moquette, acqua piscina /
pozzanghera.

### `concretes.yaml`
Cemento liscio (autolivellante, casseforme, industriale), esposto
(Tadao Ando, brutalist), lavorato (sabbiato, bocciardato, graffiato),
lavato a vista (con voronoi cell inclusi), colorati (ocra, antracite,
terra siena), asfalto, bitume catrame (con clearcoat viscoso).

### `plasters.yaml`
Rasati civili, graffiati esterni/fini, veneziano (avorio, blu,
pompeiano, salvia, antracite — con marble production-grade +
clearcoat 0.85), marmorino (bianco, blu polvere, terracotta — opaco),
tadelakt marocchino (rosa, ocra, menta — clearcoat + subsurface da
sapone nero), stucco antico (crepato, umido), calce mediterranea
(bianca, avorio, azzurra Santorini), gesso, coloratura.

### `leathers.yaml`
Pieno fiore, anilina, nappa morbida, suede / scamosciato (sheen
Charlie 0.85), vintage invecchiata, patent leather (clearcoat alto),
esotici (pitone, coccodrillo, struzzo, lucertola — voronoi cell per
scaglie), scarpe (box calf, cordovan, militare), cuoio grezzo,
ecoleather sintetico.

### `synthetics.yaml`
Fibra di carbonio (twill, plain, satin, 3D — anisotropic + rotation),
kevlar (giallo, nero, hybrid), vetroresina (gelcoat, grezza,
trasparente), neoprene, PTFE/Teflon, gomma EPDM, silicone medicale
(subsurface alto), poliuretani, vinile auto wrap (matte, gloss, satin,
chrome, chrome rosa, olografico — thin_film), tessuti tecnici
(ripstop, cordura, gore-tex), aerogel.

### `liquids.yaml`
Acque (piscina, mare costiero, profondo, torrente, fontana, tropicale,
torbida, ghiaccio), latticini (intero, scremato, panna, condensato),
sangue (arterioso, venoso, secco, coagulato), oli (motore iridescente,
oliva, semi, benzina), alcolici (vino rosso/bianco/rosé, birra chiara/
stout con Beer-Lambert profondo, whisky, vodka, rum), sciroppi
(miele, acero, caramello, melassa), bevande calde, succhi, refrigeranti
industriali.

### `minerals-gems.yaml`
Quarzi (trasparente, fumè, citrino, rosa, ametista), geodi
(base ruvida + interno cristallino ametista/agata), druse
vector-displaced, cristalli cubici (pirite, halite, galena), calcite
islandese birifrangente, fluorite, malachite radiale, lapislazzuli,
pietra di luna (thin_film adularescenza), opali (bianco, nero, fuoco),
selenite, kyanite (anisotropic), tormalina, granato.

### `industrial-coatings.yaml`
Chassis auto (matte / gloss / perlato con flake noise + thin_film 280nm),
clearcoat protettivo, matte anti-glare, polveri elettrostatiche RAL,
anodizzazione alluminio e titanio (thin_film 320-640nm), zincatura,
cromature, smalti a fuoco, gel coat marino (clearcoat + coat_normal),
termocromiche (color_ramp 4-stop evolutivo), retroriflettenti,
anti-corrosive, fluo safety.

### `weathering.yaml`
26 overlay con prefisso `over_*` pensati per `type: mix`: ruggine
(light, medium, heavy, streak), muschio (sparse, dense, wet), polvere
(light, heavy, gesso, terra), colature sporco (streak, drip), calcare
(bianco, giallo), grasso (dark, light), neve (powder, melting),
vernice scrostata (bianca, militare via voronoi crackle f2-f1),
foglie morte, sale marino, film d'acqua, macchie acqua secca,
verderame patina rame.

### `mix-recipes.yaml`
35 ricette `mix_*` pronte all'uso che combinano un `dis_*` base con un
`over_*` weathering tramite maschera procedurale calibrata. Categorie:
metalli invecchiati (acciaio/ferro/rame con ruggine, verderame),
legni usurati (polvere, foglie autunno, dirt streak, macchie d'acqua,
muschio), intonaci macchiati (dirt, calcare, umidità), pietre
colonizzate (muschio, terra, sale marino), vernici scrostate (su
acciaio, lamiera, legno), generici (neve, grasso officina, pavimento
bagnato, calcare rubinetto, polvere pesante). L'header del file
elenca le librerie da importare prima.

## Disney vs Classic — quando usare cosa

| Scenario | Scelta | Motivazione |
|----------|--------|-------------|
| Pavimento, muro, sfondo | `cls_` | Superficie grande → il rumore Disney dominerebbe l'immagine |
| Metallo puro (`metallic` = 1.0) | `dis_` o `cls_` | Il Disney metallico ha un solo lobo → stesso rumore del classic |
| Oggetto protagonista in primo piano | `dis_` | Clearcoat, sheen, subsurface fanno la differenza |
| Render draft / preview | `cls_` | Converge in 32-64 spp |
| Render finale | `dis_` dove serve | Il rumore extra è accettabile a sample count alto |
| Vetro chiaro e semplice | `cls_` (dielectric) | Più pulito e veloce |
| Vetro smerigliato, colorato profondo, opale | `dis_` | `spec_trans` con `transmission_depth` / thin_film non disponibili in `cls_` |
| Tessuto (velluto, seta) | `dis_` | Lo sheen Charlie è impossibile con `cls_` |
| Materiale traslucente (cera, pelle, marmi onice) | `dis_` | Il `subsurface` è impossibile con `cls_` |
| Materiale invecchiato pronto all'uso | `mix_` | Combina base + weathering + maschera, niente boilerplate |

**Regola pratica**: in una scena tipica, usa Classic per il 70-80% delle
superfici (pavimento, muri, tavoli, sfondo) e Disney solo per i 2-3
oggetti protagonisti. Questo bilancia qualità visiva e tempo di
rendering.

## Feature avanzate Disney sfruttate

- **Sheen Charlie** (`sheen` + `sheen_tint` + `sheen_roughness`) per
  velluto, suede, peluche, microfibra, muschio.
- **Clearcoat dual-path Arnold** (`coat_roughness` + `coat_ior` esplicito)
  per vernici auto, lacche, smaltature, plastiche lucide, parquet
  verniciato, patent leather, veneziano, tadelakt.
- **`spec_trans` + Beer-Lambert** (`transmission_color` +
  `transmission_depth` + `ior`) per vetri colorati profondi, gemme,
  liquidi (vino, birra, miele), ghiaccio, silicone medicale.
- **`anisotropic` + `anisotropic_rotation`** per metalli spazzolati,
  carbon fiber, vinile spazzolato, capelli, oro spazzolato.
- **`thin_film_thickness` + `thin_film_ior`** per anodizzazioni
  alluminio/titanio, opali, perle, madreperla, vinile olografico,
  niobio, perlate automotive.
- **`diff_trans` + `thin_walled`** per foglie, petali, carta velina,
  tessuti sottili in controluce.
- **`flatness`** per superfici cerose/saponose senza vera SSS.
- **Texture procedurali production-grade**: marble multi-vein con
  IQ warp, wood con asymmetric latewood + pore_density + knot_density,
  voronoi con 11 metric/output, color ramp multi-stop.
- **Displacement stack** (bump_map → mesh subdivision → scalar /
  vector displacement → autobump). Per le sfere usare
  `displacement_method: bump_only`.

## Sample count consigliati

| Mix materiali | Preview | Draft | Produzione |
|---------------|--------:|------:|-----------:|
| Solo Classic | 16 spp | 32 spp | 128 spp |
| Classic + Disney misti | 32 spp | 64 spp | 256 spp |
| Tutto Disney + texture | 64 spp | 128 spp | 512 spp |
| Disney + spec_trans/thin_film/SSS | 128 spp | 256 spp | 1024 spp |

Vedi anche `docs/reference/rendering-profiles.md` per le scorciatoie
`-q draft-tiny / draft-small / draft / medium / final / ultra`.

## Materiali con medium companion

Molti materiali traslucenti o rifrattivi guadagnano enormemente dal SSS
volumetrico. Importa la libreria medium corrispondente e assegna
`interior_medium` all'entity per abilitare il Random Walk SSS del motore.

La distinzione fondamentale: il **material** descrive la superficie (BSDF),
il **medium** descrive il volume interno (trasporto volumetrico). Un oggetto
può avere entrambi contemporaneamente. Vedi `scenes/libraries/mediums/README.md`
per la guida completa, la calibrazione della scala e le note sulla phase
function.

| File material | Material ID esempio | Medium da importare | Medium ID | Effetto |
|---|---|---|---|---|
| `stones.yaml` | `dis_carrara_lucido` | `mediums/stones.yaml` | `med_marmo_carrara` | SSS marmo bianco, glow lattiginoso caldo |
| `stones.yaml` | `dis_alabastro_bianco` | `mediums/stones.yaml` | `med_alabastro_bianco` | Retroilluminazione traslucente massima |
| `stones.yaml` | `dis_onice_miele` | `mediums/stones.yaml` | `med_onice_miele` | Ambrato dorato caldo, effetto teatrale |
| `glasses.yaml` | `dis_ghiaccio_blu` | `mediums/ice-snow.yaml` | `med_ghiaccio_blu_glaciale` | Ghiacciaio, iceberg, blocchi artici |
| `glasses.yaml` | `dis_neve_compatta` | `mediums/ice-snow.yaml` | `med_neve_fresca` | Neve retroilluminata con glow bluastro |
| `liquids.yaml` | `dis_acqua_piscina` | `mediums/liquids.yaml` | `med_acqua_pulita` | Acquario, piscina, vasche limpide |
| `liquids.yaml` | `dis_latte_intero` | `mediums/liquids.yaml` | `med_latte_intero` | SSS lattiginoso opaco, colori pastello |
| `organics.yaml` | `dis_cera_api` | `mediums/organics.yaml` | `med_cera_api` | Candela retroilluminata, scultura in cera |
| `organics.yaml` | `dis_pelle_chiara` | `mediums/organics.yaml` | `med_pelle_chiara` | Skin SSS fotorealistico per close-up |
| `foods.yaml` | `dis_cioccolato_fondente` | `mediums/organics.yaml` | `med_cioccolato_fondente` | Chocolate SSS scuro, glassa colata |
| `minerals-gems.yaml` | `dis_quarzo_rosa` | `mediums/stones.yaml` | `med_quarzo_rosa` | Quarzo rosa cristallino quasi vitreo |
| `minerals-gems.yaml` | `dis_ametista_grezza` | `mediums/stones.yaml` | `med_ametista` | Viola/lavanda semitrasparente |
| `minerals-gems.yaml` | `dis_opale_bianco` | `mediums/stones.yaml` | `med_opale_bianco` | Lattiginoso opalescente + iridescenza thin_film |

### Esempio scena minimale con material + medium

```yaml
imports:
  - { path: "libraries/materials/stones.yaml" }
  - { path: "libraries/mediums/stones.yaml" }

entities:
  - name: scultura_marmo
    type: sphere
    center: [0, 1, 0]
    radius: 0.35
    material: dis_carrara_lucido       # Disney con spec_trans: fa entrare la luce
    interior_medium: med_marmo_carrara # Random Walk volumetrico nel volume del marmo

  - name: vaso_alabastro
    type: cylinder
    center: [0.8, 0.5, 0]
    radius: 0.15
    height: 0.5
    material: dis_alabastro_bianco     # Disney translucente
    interior_medium: med_alabastro_bianco  # SSS massimo: quasi come retroilluminato

  - name: pedestal
    type: box
    min: [-1, -0.01, -0.5]
    max: [1,   0.01,  0.5]
    material: cls_carrara_levigato     # Classic per la superficie del piano — nessun medium
```

**Nota:** un Lambertian puro (prefisso `cls_` lambertian) non fa entrare la
luce nel volume — il medium non avrebbe effetto visivo. Usa sempre un material
con `spec_trans > 0` (Disney), `type: dielectric`, oppure Disney con
`flatness > 0` (superfici cerose/saponose) come compagno del medium.

## Material-embedded SSS (`subsurface_radius`)

Molti dei materiali Disney di queste librerie dichiarano `subsurface_radius`
direttamente sul materiale — parity con Arnold `standard_surface` /
Cycles Principled BSDF. Significato pratico: chi importa la libreria
ottiene il SSS volumetrico **automaticamente** senza dover dichiarare la
sezione `mediums:` né `interior_medium` sull'entity.

### Come funziona

Il loader, quando vede `subsurface_radius` su un materiale Disney:

1. Auto-costruisce un `HomogeneousMedium` derivato dalla MFP per canale:
   `σ_t = 1/(radius·scale)`, `σ_s = α·σ_t`, `σ_a = (1−α)·σ_t`
   dove `α = subsurface_color` (o `color` se assente).
2. Auto-imposta i parametri rifrattivi necessari al transmission lobe
   Disney: `spec_trans = 1.0` e `transmission_color = [1, 1, 1]`
   (a meno che l'utente non li abbia già specificati esplicitamente).
3. Auto-inietta il medium su ogni entity che referenzia il materiale e
   **non** ha `interior_medium` esplicito.

### Override

`interior_medium` esplicito sull'entity vince sempre (convenzione
Arnold/Cycles). Lo stesso materiale può essere usato in oggetti diversi:
senza override → SSS standard del materiale; con override → comportamento
custom (es. lo stesso vetro che racchiude acqua, vino o latte).

### Esempio prima/dopo

**Prima** (paradigma classico, entity-bound):
```yaml
imports:
  - { path: "libraries/materials/stones.yaml" }
  - { path: "libraries/mediums/stones.yaml" }
entities:
  - name: scultura
    type: sphere
    radius: 0.35
    material: dis_carrara_lucido
    interior_medium: med_marmo_carrara   # ← richiesto
```

**Dopo** (paradigma material-embedded):
```yaml
imports:
  - { path: "libraries/materials/stones.yaml" }   # niente libreria mediums
entities:
  - name: scultura
    type: sphere
    radius: 0.35
    material: dis_carrara_lucido                  # SSS già incluso nel materiale
```

### Quali file di libreria hanno SSS embedded

| File | Materiali con `subsurface_radius` |
|------|------------------------------------|
| `stones.yaml` | Tutti i marmi colorati + onici + alabastri (Carrara, Statuario, Calacatta già da prima) |
| `organics.yaml` | Cere (api, paraffina, soia, candela), ambre (chiara/scura/rossa), saponi |
| `foods.yaml` | Cioccolati (fondente, latte, bianco, fuso), formaggi traslucenti (brie, mozzarella) |
| `liquids.yaml` | Latticini (intero, scremato, condensato, panna montata) |
| `glasses.yaml` | Opale, ghiacci (chiaro, torbido, blu, fratturato), neve compatta |
| `minerals-gems.yaml` | Quarzo rosa, ametiste |
| `leathers.yaml` | Pelle anilina nera (anisotropy 0.75) |

`fabrics.yaml` non ha SSS embedded: i tessuti non hanno volume continuo.

### Quando usare `mediums/` invece di `subsurface_radius`

Le due strategie sono complementari, non alternative:

| Caso | Strategia |
|------|-----------|
| Marmo/cera/cioccolato standard | `subsurface_radius` (già nei materiali) |
| Vetro contenente acqua/vino/latte | `interior_medium` esplicito sull'entity |
| Stessa superficie, volumi diversi per oggetti gemelli | `interior_medium` esplicito |
| Nebbia globale o atmosfera planetaria | `world.medium` inline o `mediums/atmospherics.yaml` |
| Override del SSS predefinito di un materiale | `interior_medium` esplicito (vince sull'embedded) |
