# Guida ai Profili di Rendering

Questo documento è il riferimento pratico per regolare i tre parametri di qualità di 3D-Ray — `-s` (campioni per pixel), `-d` (profondità massima dei rimbalzi), `-S` (campioni d'ombra) — più il firefly clamp `-C`. Definisce tre profili canonici (**Preview**, **Standard**, **Final**), spiega come ciascun parametro si mappa sull'architettura interna del motore ed elenca i tip per non sprecare tempo di render.

---

### 1. **I TRE PROFILI CANONICI**

| Profilo | `-s` | griglia | `-d` | `-S` | Uso tipico |
|---|---|---|---|---|---|
| **Preview** (Bozza) | `64` | 8×8 | `4` | `1` | Composizione scena, posizionamento camere, colori materiali. Veloce e granuloso. |
| **Standard** | `512` | ~23×23 | `8` | `1` | Render di qualità quotidiani, CI/CD, review. Abbinalo al denoiser per un'immagine pulita. |
| **Final** (Vetrina) | `1024` | 32×32 | `8` | `4` | Portfolio, copertina del README, render promozionali. Croccante e pubblicabile. |

Tutti i valori della tabella sono **quadrati perfetti** di proposito (vedi sezioni 3 e 5).

**Comandi pronti da copiare:**
```bash
# Preview — da pochi secondi a un minuto su una scena tipica
RayTracer -i my-scene.yaml -w 400 -H 225 -s 64 -d 4 -S 1

# Standard — adatto a CI/CD, ottimo per anteprime in linea nel README
RayTracer -i my-scene.yaml -w 800 -H 450 -s 512 -d 8

# Final — qualità portfolio / copertina del README
RayTracer -i my-scene.yaml -w 1920 -H 1080 -s 1024 -d 8 -S 4
```

> L'estensione `.yaml` su `-i` è **opzionale**: se il percorso indicato
> non esiste così com'è, il loader prova ad aggiungere `.yaml` e poi
> `.yml` — `RayTracer -i my-scene ...` equivale ai comandi qui sopra.

---

### 1a. **PRESET `--quality` / `-q`**

Se non vuoi riscrivere `-w -H -s -d -S` ogni volta, il flag
`--quality` (alias `-q`) impacchetta tutti e cinque i parametri in un
preset con nome. La scala è **4 livelli di qualità × 3 risoluzioni + 1
preset 4K showcase** (`draft → standard → pre-final → final → ultra`):

| `-q` | Risoluzione | `-s` | `-d` | `-S` | Denoiser | Uso tipico |
|---|---|---|---|---|---|---|
| `draft-tiny`   | 480×270   | 16   | 4 | 1 | nfor fast | Sanity check istantaneo / errori macroscopici |
| `draft-small`  | 960×540   | 16   | 4 | 1 | nfor fast | Composizione e camere super-veloce |
| `draft`        | 1920×1080 | 16   | 4 | 1 | nfor fast | Stessa velocità, framing Full HD |
| `standard-tiny`  | 480×270   | 512 | 8 | 1 | nfor high | Check rapido, qualità scena classica |
| `standard-small` | 960×540   | 512 | 8 | 1 | nfor high | Iterazione materiali e luci, thumbnail |
| `standard`       | 1920×1080 | 512 | 8 | 1 | nfor high | **Render di qualità quotidiano** (review, CI, scene classiche) |
| `pre-final-tiny`  | 480×270   | 256 | 8 | 1 | nfor high | Spot check con feature complete |
| `pre-final-small` | 960×540   | 256 | 8 | 1 | nfor high | Iterazione della resa final a ¼ di risoluzione |
| `pre-final`       | 1920×1080 | 256 | 8 | 1 | nfor high | **Anteprima fedele di `final`**, ~4-6× più veloce |
| `final-tiny`   | 480×270   | 1024 | 8 | 4 | — | Spot check rapido a qualità piena |
| `final-small`  | 960×540   | 1024 | 8 | 4 | — | Thumbnail showcase / contact-sheet |
| `final`        | 1920×1080 | 1024 | 8 | 4 | — | Portfolio, copertina README |
| `ultra`        | 3840×2160 | 512  | 8 | 4 | — | Showcase 4K |

Feature per tier: `standard` spegne caustiche fotoniche e SSS volumetrico e
usa NEE power-weighted con clamp indiretto rilassato (0.5); `pre-final`,
`final` e `ultra` girano con la **feature-set completa** (caustiche on — 2M
fotoni per pre-final, 2–4M per final/ultra — SSS high, NEE su tutte le luci,
clamp di default). `ultra` resta a 512 spp di proposito: a 4K la densità di
pixel nasconde il rumore per-pixel che a 1080p sarebbe visibile, e 1024 spp
raddoppierebbero un render già lungo.

Le varianti `*-tiny` sono a **un quarto di risoluzione** rispetto al
full HD (480×270 = ¹⁄₁₆ dei pixel di 1920×1080, ¹⁄₄ di `*-small`),
pensate per validare la scena al volo — individuare errori macroscopici
di composizione o illuminazione prima di lanciare un render più lungo.

Le varianti `*-small` sono **esattamente metà risoluzione** su ogni
asse (960×540 = ¼ dei pixel di 1920×1080), quindi costano circa ¼ del
preset full-HD corrispondente restando leggibili a schermo.

**Qualunque flag esplicito vince.** Il preset compila solo i valori
che non hai passato manualmente: `-q final -d 16` lancia il preset
final ma porta la depth a 16 (utile per scene con vetri impilati);
`-q standard -w 640 -H 360` rimpicciolisce il preset standard senza
toccarne il sampling.

**Denoiser.** I preset `draft*`, `standard*` e `pre-final*` abilitano di
default il denoiser feature-guided (`--denoiser nfor`; `draft*` usa
`--denoise-quality fast`, gli altri `high`): la beauty HDR lineare viene
filtrata prima del tone mapping usando le guide albedo/normale/profondità —
è ai campionamenti bassi e medi che il denoiser rende di più, ed è lì che i
512 spp di `standard` lasciano spesso quella grana residua che il denoiser
assorbe in pochi secondi extra. `final` e `ultra` restano non filtrati per
scelta (i render di riferimento convergiti mantengono ogni dettaglio non
filtrato); aggiungi un `--denoiser nfor` esplicito per filtrarli, o
`--denoiser none` per spegnere il default del preset. Vedi
[Denoising](../technical/denoising.it.md) per l'algoritmo e i trade-off.

**Caustiche.** I preset `pre-final`, `final` e `ultra` abilitano anche le
caustiche via photon mapping (`--caustics on`) di default; `draft` e
`standard` le lasciano spente. Il budget di fotoni del pre-pass è controllato
da `--caustic-photons <N>` (2M su pre-final, ~2–4M su final/ultra): più
fotoni = caustiche più nitide e meno rumorose, al costo di un pre-pass più
lento. Un `--caustics off`
esplicito (o `--caustics on` su un preset più basso) ha la precedenza sul
default del preset. Vedi [Path Tracing e Illuminazione §2.5](../technical/path-tracing-and-lighting.md).

**`standard` — il render di qualità quotidiano.** Il tier `standard` punta
a qualità d'immagine da final su una scena *classica* — superfici
Lambertian/Disney, vetri non annidati (al massimo un paio di sfere di
cristallo una davanti all'altra), marmo procedurale con parametri ordinari —
eliminando la costosa macchineria di illuminazione globale che queste scene
non usano. Rispetto a `final`: disattiva le **caustiche** a photon mapping,
disattiva il **SSS** volumetrico (`--sss-mode off`), gira a **512 spp** con
**un solo shadow sample** (512 spp fanno già anti-aliasing), passa a NEE
**power-weighted single-light** (`--light-sampling power`, che scala meglio
del default globale `all`), rilassa il clamp indiretto a `0.5` e lascia al
**denoiser NFOR** la grana residua. Su una scena senza caustiche/SSS è
nettamente più veloce di `final` a parità di resa. Come sempre i flag
espliciti vincono — es. `-q standard --caustics on` riattiva le caustiche,
`-q standard -s 768` alza i campioni se i bordi del vetro restano rumorosi.
Evitalo per scene che dipendono davvero da caustiche focalizzate,
traslucenza/SSS profondo o vetri impilati/annidati — lì usa
`pre-final`/`final` (e un `-d` più alto).

**`pre-final` — anteprima fedele di `final`.** Stessa feature-set di `final`
(caustiche on, SSS high, depth 8, NEE su tutte le luci, clamp indiretto di
default) con i budget di campionamento tagliati dove il denoiser compensa
meglio: **¼ dei campioni per pixel** (256) e **un solo shadow sample** — il
rumore delle penombre è esattamente ciò che il filtro guidato dalle feature
rimuove più pulito. Il risultato anticipa illuminazione, caustiche e
traslucenza del final a circa **4-6× la velocità**; usalo per iterare su una
scena destinata a `final`, poi lancia `final` non filtrato per la consegna.

```bash
# Sanity check istantaneo, pochi secondi
RayTracer -i my-scene -q draft-tiny

# Controllo composizione veloce, secondi
RayTracer -i my-scene -q draft-small

# Render di qualità quotidiano / review, Full HD
RayTracer -i my-scene -q standard

# Anteprima con feature complete della resa final, Full HD
RayTracer -i my-scene -q pre-final

# Portfolio, Full HD
RayTracer -i my-scene -q final

# Showcase 4K
RayTracer -i my-scene -q ultra

# Preset final + override custom (depth alzata per vetri impilati)
RayTracer -i my-scene -q final -d 16

# Denoiser del preset disattivato (output grezzo a 512 spp)
RayTracer -i my-scene -q standard --denoiser none
```

> I nomi dei preset seguono la scala convenzionale Preview/Standard/Final,
> con `pre-final` come anteprima denoised di `final`.
> Le varianti `-small` sono adatte a check iterativi a metà risoluzione,
> e le varianti `-tiny` offrono un check ancora più rapido a un quarto
> della risoluzione.

---

### 2. **VALORI DI DEFAULT**

| Parametro | Default | Origine |
|---|---|---|
| `-s` / `--samples` | `16` (griglia 4×4) | `Program.cs` |
| `-d` / `--depth` | `8` | `Program.cs` |
| `-S` / `--shadow-samples` | non impostato → valore YAML per-luce (default: 4) | `Program.cs` |
| `-C` / `--clamp` | `10` (firefly clamp) | `Renderer.DefaultMaxSampleRadiance` |
| `--indirect-clamp-factor` | `0.25` (clamp indiretto = `2.5`) | `Renderer.DefaultIndirectClampFactor` |
| `--exposure` | `0` EV (identità) | `Renderer.DefaultExposureEv` |
| `--light-sampling` | `all` (somma su tutte le luci) | `LightSamplingStrategy.All` |
| `--texture-filtering` | `auto` (filtering attivo) | `Renderer.TextureFilteringMode.Auto` |
| `--sampler` | `sobol` (Owen scramble) | `Program.cs` / `Sampler.SetKind` |
| `--mis` | `balance` (balance heuristic Veach) | `Program.cs` / `MisHeuristic` |

> Il sampler default `sobol` (Burley 2020, Owen scrambling hash-based sulla tabella Joe-Kuo) converge più rapidamente del PRNG thread-local su pixel jitter, lens sampling e primi bounce. Passa `--sampler prng` per tornare al vecchio comportamento — utile quando confronti con render storici o debugghi regressioni stocastiche.

> **`--mis balance` vs `--mis power`** — entrambi sono pesi Multiple Importance Sampling unbiased (Veach 1997 §9.2). Il default `balance` (`w = p/(p+q)`) è la balance heuristic, il peso a varianza minima per qualunque coppia di sampler. L'opzione `power` (`w = p²/(p²+q²)`) usa la power heuristic con β=2 e riduce ulteriormente la varianza quando le due PDF differiscono molto — tipicamente luci speculari piccole su materiali ruvidi, o luci puntiformi attraverso fog. Il costo computazionale è identico; puoi cambiare in qualunque momento senza rifare preprocessing.

I default sono pensati per **iterazione rapida**, non per qualità finale. Considera il profilo Preview come il minimo per un render "presentabile"; usa Standard o Final quando devi pubblicare.

---

### 3. **COMPRENDERE `-s` (CAMPIONI PER PIXEL)**

Il motore esegue **stratified sampling** su una griglia √N × √N per pixel. Passare `-s 16` produce una griglia 4×4 (16 campioni); `-s 256` una griglia 16×16 (256 campioni). Ogni cella produce un campione jittered.

**Il sampler Sobol (predefinito) usa il conteggio esatto.** Il sampler `sobol` predefinito esegue esattamente il numero di campioni richiesto — nessun arrotondamento. **Solo per `--sampler prng`:** il motore ha bisogno di una griglia √N × √N, quindi arrotonda √N per eccesso: `-s 15` diventa silenziosamente 4×4 = 16. Per controllare il costo con precisione con PRNG, preferisci quadrati perfetti: `1, 4, 9, 16, 25, 36, 49, 64, 100, 144, 196, 256, 400, 576, 784, 1024, 1600`.

**Costo:** approssimativamente lineare — raddoppiando `-s` raddoppi all'incirca il tempo di render.

---

### 4. **COMPRENDERE `-d` (PROFONDITÀ MASSIMA DEI RIMBALZI)**

`-d` limita il numero di rimbalzi indiretti che un raggio può effettuare. Nel path tracing i primi 4–6 rimbalzi indiretti contribuiscono per circa il 99% all'illuminazione realistica nella maggior parte delle scene.

**Perché il default è 8 (non 50):** il renderer usa **Russian Roulette adattiva** (`Renderer.cs`). Per scene con illuminazione normale RR si attiva al 4° rimbalzo terminando stocasticamente i path a basso contributo; per scene indirect-dominant (solo emissive, luci deboli) si attiva all'8° con una soglia di sopravvivenza più alta. Alzare `-d` oltre questo punto raramente cambia l'immagine ma costa sempre tempo.

**Quando alzare `-d` sopra 8:**
- **Dielettrici impilati** — liquidi nei bicchieri, file di bottiglie di vino, sfere di vetro annidate. Ogni interfaccia di entrata/uscita consuma un rimbalzo, quindi 10 interfacce di vetro richiedono `-d 16–20` o il vetro più interno diventa inaspettatamente nero.
- **Mezzi partecipanti densi con geometria complessa** dietro al volume.

Per tutto il resto (oggetti opachi, singole lastre di vetro, metalli, interni normali) **`-d 4–8` è abbondante**.

---

### 5. **COMPRENDERE `-S` (CAMPIONI D'OMBRA)**

`-S` forza un override globale del numero di raggi d'ombra usati da ogni luce ad area (`AreaLight`, `SphereLight`, `GeometryLight`). Ogni luce costruisce una griglia stratificata √N × √N sulla propria superficie e lancia un raggio d'ombra per cella.

**Attenzione — costo moltiplicativo.** A ogni intersezione di superficie il motore lancia `S` raggi d'ombra **per ogni luce ad area, per ogni campione pixel, per ogni rimbalzo**. Con `-s 256`, `-S 4`, due luci ad area e 6 rimbalzi parli di circa `256 × 4 × 2 × 6 ≈ 12.000` raggi d'ombra per pixel. Alzare `-S` è il modo più rapido per distruggere i tempi di render.

**Regola empirica:**
- Default / Preview / Standard → `-S 1`.
- Alza `-S` a `4` (2×2) o `9` (3×3) solo quando il render generale è già pulito ma le penombre morbide a terra restano l'unica fonte di rumore.
- Usa quadrati perfetti (`1, 4, 9, 16`) — stessa stratificazione √N×√N di `-s`.

I campioni pixel (`-s`) e i campioni d'ombra (`-S`) riducono entrambi il rumore d'ombra. Spendi prima il budget su `-s`; ricorri a `-S` solo quando hai verificato che il collo di bottiglia siano le ombre (non la GI).

---

### 6. **FIREFLY CLAMP (`-C` / `--clamp`)**

`MaxSampleRadiance` (esposto come `-C`) è il limite massimo per la radianza per-campione **prima del tone mapping**. Cattura gli outlier rari prodotti da caustiche speculari, compensazione delle Disney lobe e boost della Russian Roulette — i pixel che altrimenti apparirebbero come puntini bianchi luminosi ("fireflies") nel render.

**Default:** `10`. Dopo il tone mapping ACES qualunque luminanza ≳ 5 satura già al bianco, quindi `10` lascia intatti tutti i highlight visibili pur uccidendo gli spike rari. Un valore di `10` è un buon punto di partenza per la maggior parte delle scene.

**Quando alzare `-C`:**
- HDRI con sole molto intenso dove il disco solare appare meno luminoso del previsto.
- Scene molto emissive in cui hai verificato che è la sorgente luminosa stessa (non le sue caustiche) ad essere soppressa. Prova `-C 25–100`.
- Disattiva il clamp di fatto con un valore molto alto. Il rischio sono fireflies in caustiche e catene speculari profonde.

**Quando abbassare ulteriormente `-C`:**
- Nebbia densa / mezzi omogenei spessi + `-d` alto.
- Scene con molti piccoli emissive luminosi visti attraverso il vetro.
- Prova `-C 5` o `-C 3`. Perdi un po' di gamma dinamica HDR sui highlight più caldi ma guadagni ombre più pulite e penombre più morbide ai rimbalzi estremi.

Il clamp usa **scaling con preservazione della luminanza**, quindi non altera la tinta sui highlight luminosi — solo la luminosità.

#### **6a. Clamp indiretto depth-aware (`--indirect-clamp-factor`)**

Un secondo clamp opzionale riduce la soppressione specificamente sui **bounce indiretti** (depth ≥ 1), offrendo un controllo indipendente sui fireflies dei bounce profondi rispetto al clamp primario.

```
--indirect-clamp-factor 0.25
```

Questo moltiplica la soglia `-C` per tutti i contributi indiretti. Con il default `0.25` e `-C 10` il clamp indiretto è `2.5`: la radianza dei bounce profondi è limitata a 2.5, quella primaria a 10. Imposta `1.0` per disattivare la soppressione aggiuntiva e avere clamp indiretto uguale a quello primario.

**Quando abbassare ulteriormente:** catene caustic/speculare che producono ancora fireflies al default. Scendi fino a `0.1` per scene molto volumetriche con vetro.

**Quando alzare verso `1.0`:** scene in cui i highlight indiretti appaiono inaspettatamente smorzati — tipicamente Cornell box puramente emissive o HDRI in cui l'unico segnale luminoso legittimo arriva da bounce indiretti.

#### **6b. Light importance sampling (`--light-sampling`)**

```
--light-sampling power
```

Seleziona come il renderer sceglie quale luce interrogare per ogni evento NEE:

| Valore | Comportamento | Quando usarlo |
|---|---|---|
| `all` | Somma su tutte le luci (originale) | **Default** — sicuro, backward compat |
| `power` | Campiona una luce ∝ `ApproximatePower` | Scene con molte luci di luminosità mista |
| `uniform` | Campiona una luce uniformemente | Debug / baseline di confronto con `power` |

Con `all` il renderer lancia `ShadowSamples` raggi d'ombra per luce per shading point — O(N·S). Con `power` o `uniform` li lancia per una sola luce e divide per la probabilità di campionamento per restare unbiased — O(S). In una scena con 1 area light intensa + 20 point light deboli, `power` converge notevolmente più in fretta.

#### **6c. Texture filtering (`--texture-filtering`)**

```
--texture-filtering <auto|on|off>
```

Controlla l'anti-aliasing analitico di texture procedurali e image via
ray differentials. La camera emette raggi ausiliari attraverso i vicini
`+x`/`+y` del pixel; la dimensione del footprint a ogni hit di superficie
pilota una lookup pre-filtrata della texture — Perlin/fBm clampano le
ottave sopra Nyquist, Voronoi fa supersampling adattivo, le texture image
usano mipmap + filtering anisotropico EWA.

| Valore | Comportamento | Quando usarlo |
|---|---|---|
| `auto` | Filtering attivo (la camera emette differentials) | **Default** — sempre sicuro |
| `on`   | Identico ad `auto`, riservato a euristiche future | Equivalente ad `auto` |
| `off`  | Nessun differential, ogni texture campionata point-only | Confronto baseline, benchmark, verifica che un eventuale aliasing arrivi dal pipeline texture e non dal sampling della camera |

Il default `auto` rimuove moiré, shimmer e grana ad alta frequenza su
superfici lontane o ad angoli radenti — tipicamente permette di
dimezzare `-s` di 4× su scene outdoor con terreno procedurale o
movimenti di camera grandangolari senza perdere qualità. Disattivare
con `off` ha senso solo per debug o A/B comparison; il costo del
filtering è minimo (pochi punti percentuali nelle scene tipiche).

#### **6d. Esposizione fotografica (`--exposure`)**

```
--exposure <EV>
```

Guadagno lineare `2^EV` applicato a ogni pixel **prima** del tone map
ACES. Replica il concetto di compensazione dell'esposizione fotografica
comune nei workflow di post-produzione. `EV = 0` (default) è
identità; valori negativi scuriscono (1 EV = fattore 2×), valori
positivi schiariscono.

**Perché è importante:** ACES filmic è una curva non-lineare il cui
contrasto è preservato solo dentro la sweet-spot lineare a circa
`[0.18, 1.0]` di radianza in ingresso. Sopra ~2.0 la curva si appiattisce
sul plateau 0.95-0.99 dove tutto appare bianco indipendentemente dal
base color sottostante — texture procedurali, venature dei marmi e
identità del materiale collassano tutti in luminosità uniforme. Sotto
~0.05 il rolloff svanisce nel nero. `--exposure` permette di scivolare
l'intera scena dentro la sweet-spot senza ribilanciare ogni luce a mano.

| Situazione | `--exposure` suggerito |
|---|---|
| La scena appare lavata, i punti luce saturano per primi | `-1` a `-2` |
| La scena è troppo scura, i mid-tone cadono nel rumore | `+1` a `+2` |
| Hai già tarato le luci per finire vicino a `0.5` lineare | `0` (omettere il flag) |

Va combinato con il setup delle luci, non sostituito: ribilanciare
le intensità delle luci è preferibile per scene condivise (gli altri
artisti non devono ricordare un flag), ma `--exposure` è il knob più
veloce in iterazione quando non vuoi committare una modifica alla luce.

---

### 7. **TIP PRATICI**

- **Inizia ogni scena in Preview.** Itera composizione e materiali con `-s 64 -d 4 -S 1` finché l'immagine non si legge correttamente. Solo allora promuovi a Standard o Final.
- **Spendi il budget su `-s` prima di `-S` o `-d`.** I campioni pixel attaccano ogni fonte di rumore contemporaneamente (GI, ombre, speculare); gli altri due agiscono su problemi specifici.
- **`-d 4` è il sweet spot per Preview** perché la Russian Roulette in scene normali si attiva esattamente al 4° rimbalzo — oltre, ti affidi comunque a RR.
- **Non combinare `-s` alto con `-S` alto senza motivo.** `-s 1024 -S 16` è quasi sempre uno scambio cattivo. `-s 1024 -S 4` di solito eguaglia visivamente a ¼ del costo.
- **Le scene cariche di vetro sono l'unico motivo legittimo per superare `-d 8`.** Alza `-d` a `16` o `20` solo quando vedi interni inaspettatamente neri nei dielettrici impilati.
- **Riproducibilità in CI.** I valori `-s`/`-d`/`-S` nei `.github/workflows/*.yml` dovrebbero essere un profilo nominato specifico (tipicamente Preview o uno Standard ridotto) — non valori ad-hoc.

---

### 8. **TIP SPECIFICI PER MEZZI PARTECIPANTI (NEBBIA / FUMO)**

3D-Ray supporta quattro tipi di medium globali — `homogeneous`, `height_fog`,
`procedural`, `grid` — più le phase function `isotropic`, `hg`, `rayleigh`,
`double_hg`, `schlick`. Ogni tipo ha un costo/rumore molto diverso.

- **Homogeneous e height_fog sono "gratis"** rispetto al rendering normale: la
  trasmittanza ha forma chiusa, nessun delta tracking. Un Preview è già
  usabile; uno Standard è quasi sempre sufficiente.
- **Procedural (Perlin fBm) e grid usano delta tracking** (Woodcock) + ratio
  tracking: più rumorosi per costruzione, soprattutto nelle zone dense.
  - Preview mostra grana evidente — va bene per la composizione.
  - Per immagini pubblicabili punta a `-s 576` (24×24) o `-s 1024`.
  - Se vedi rumore concentrato nel cono luminoso, aumenta `-s` (non `-S`).
- **Firefly clamp con nebbia densa.** Mezzi con `sigma_s` alto e `-d 8+`
  producono talvolta spike luminosi rari che sopravvivono al `-C 10` di
  default. Abbassa a `-C 5` o `-C 3` senza timore: perdi poco dinamica,
  guadagni molto pulito.
- **`soft_radius` su luci point/spot dentro un medium.** Con un medium
  partecipante attivo, l'attenuazione 1/d² delle luci `point`/`spot` diverge
  agli eventi di scattering vicini all'emettitore, producendo pixel-firefly
  isolati che neanche più samples riescono a livellare. Imposta `soft_radius`
  su quelle luci a un valore vicino al raggio fisico del bulbo (es.
  `0.10`–`0.25`): il denominatore viene clampato a `max(d², r²)`, lo spike
  sparisce, e a `d ≥ r` il look è invariato. Default `0` = nessun clamp
  (comportamento originale). Vedi `riferimento-scene.md` §8.
- **`soft_radius` su luci area dentro un medium.** Il termine `cosLight/d²`
  dello stimatore area può divergere ad angoli radenti in media densi.
  Imposta `soft_radius` sulle area light (es. `0.5`–`2.0`). Le sphere
  light usano uno stimatore ad angolo solido limitato per costruzione e
  non consumano `soft_radius`. Combinato con
  `--indirect-clamp-factor 0.25`, copre tutti i principali percorsi firefly.
- **Non alzare `-d` per la nebbia.** Il path volumetrico è già gestito
  correttamente a `-d 6–8`. Più rimbalzi nella nebbia = più costo, non più
  realismo (la Russian Roulette termina comunque i cammini).
- **Phase function con g → 1 (es. HG g=0.95)** rende god-ray più stretti e
  drammatici ma **aumenta la varianza**: se vedi coni "rumorosi", abbassa `g`
  a 0.7-0.85 oppure passa a `double_hg` con pesi più equilibrati.
- **Rayleigh** è economica (closed-form) e utile per cieli/atmosfera.
  `double_hg` e `schlick` costano quanto HG standard.
- **Grid medium: attenzione alla risoluzione.** Griglie inline-YAML fino a 8³
  sono ok; sopra passa al formato binario `.vol` (campo `file:` invece di
  `data:`). La risoluzione non incide sul costo di rendering — solo sul
  tempo di parsing e sull'uso di memoria.

**Profilo consigliato per showcase volumetric:**
```bash
# Preview volumetric (controllo composizione, ~30-60 s)
RayTracer -i scene.yaml -w 400 -H 225 -s 64 -d 4 -S 1

# Standard volumetric (review; delta-tracking ancora un po' rumoroso)
RayTracer -i scene.yaml -w 800 -H 450 -s 400 -d 6 -S 1

# Final volumetric (pulizia pubblicabile)
RayTracer -i scene.yaml -w 1920 -H 1080 -s 1024 -d 8 -S 4 -C 5
```

---

### 9. **DOCUMENTAZIONE CORRELATA**

- [`docs/technical/path-tracing-and-lighting.md`](../technical/path-tracing-and-lighting.md) — interni del path tracer, NEE e Russian Roulette.
- [`docs/technical/rendering-pipeline.md`](../technical/rendering-pipeline.md) — panoramica della pipeline.
- [`docs/reference/riferimento-scene.md`](./riferimento-scene.md) — schema YAML delle scene.
