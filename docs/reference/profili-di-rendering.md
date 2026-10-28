# Guida ai Profili di Rendering

Questo documento è il riferimento pratico per regolare i tre parametri di qualità di 3D-Ray - `-s` (campioni per pixel), `-d` (profondità massima dei rimbalzi), `-S` (campioni d'ombra) - più il firefly clamp `-C`. Definisce tre profili canonici (**Preview**, **Standard**, **Final**), spiega come ciascun parametro si mappa sull'architettura interna del motore ed elenca i tip per non sprecare tempo di render.

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
# Preview - da pochi secondi a un minuto su una scena tipica
RayTracer -i my-scene.yaml -w 400 -H 225 -s 64 -d 4 -S 1

# Standard - adatto a CI/CD, ottimo per anteprime in linea nel README
RayTracer -i my-scene.yaml -w 800 -H 450 -s 512 -d 8

# Final - qualità portfolio / copertina del README
RayTracer -i my-scene.yaml -w 1920 -H 1080 -s 1024 -d 8 -S 4
```

> L'estensione `.yaml` su `-i` è **opzionale**: se il percorso indicato
> non esiste così com'è, il loader prova ad aggiungere `.yaml` e poi
> `.yml` - `RayTracer -i my-scene ...` equivale ai comandi qui sopra.

---

### 1a. **PRESET `--quality` / `-q`**

Se non vuoi riscrivere `-w -H -s -d -S` ogni volta, il flag
`--quality` (alias `-q`) impacchetta tutti e cinque i parametri in un
preset con nome. La scala è **4 livelli di qualità × 3 risoluzioni + 1
preset 4K showcase** (`draft → standard → pre-final → final → ultra`):

| `-q` | Risoluzione | `-s` | `-d` | `-S` | Denoiser | Uso tipico |
|---|---|---|---|---|---|---|
| `draft-tiny`   | 480×270   | 16   | 8 | 1 | oidn fast | Sanity check istantaneo / errori macroscopici |
| `draft-small`  | 960×540   | 16   | 8 | 1 | oidn fast | Composizione e camere super-veloce |
| `draft`        | 1920×1080 | 16   | 8 | 1 | oidn fast | Stessa velocità, framing Full HD |
| `standard-tiny`  | 480×270   | 512 | 12 | 1 | oidn high | Check rapido, qualità scena classica |
| `standard-small` | 960×540   | 512 | 12 | 1 | oidn high | Iterazione materiali e luci, thumbnail |
| `standard`       | 1920×1080 | 512 | 12 | 1 | oidn high | **Render di qualità quotidiano** (review, CI, scene classiche) |
| `pre-final-tiny`  | 480×270   | 256 | 16 | 1 | oidn high | Spot check con feature complete |
| `pre-final-small` | 960×540   | 256 | 16 | 1 | oidn high | Iterazione della resa final a ¼ di risoluzione |
| `pre-final`       | 1920×1080 | 256 | 16 | 1 | oidn high | **Anteprima fedele di `final`**, ~4-6× più veloce |
| `final-tiny`   | 480×270   | 1024 | 16 | 4 | - | Spot check rapido a qualità piena |
| `final-small`  | 960×540   | 1024 | 16 | 4 | - | Thumbnail showcase / contact-sheet |
| `final`        | 1920×1080 | 1024 | 16 | 4 | - | Portfolio, copertina README |
| `ultra`        | 3840×2160 | 512  | 32 | 4 | - | Showcase 4K |

Da `standard*` in su i preset attivano anche l'**adaptive sampling**
(`--adaptive on`, sezione 4d): `-s` diventa il tetto, e i pixel che
convergono prima si fermano.

`-d` è il numero totale di vertici del percorso; ogni preset lo divide anche
per tipo di rimbalzo (vedi sezione 4), perché il vetro richiede molte
rifrazioni e l'interriflessione diffusa poche:

| Tier | `-d` totale | diffuse | glossy | transmission | volume | cut-out |
|---|---|---|---|---|---|---|
| `draft*` | 8 | 3 | 4 | 8 | 3 | 16 |
| `standard*` | 12 | 4 | 6 | 12 | 4 | 16 |
| `pre-final*`, `final*` | 16 | 8 | 8 | 16 | 8 | 16 |
| `ultra` | 32 | 16 | 16 | 32 | 16 | 32 |

Feature per tier: `standard` spegne il SSS volumetrico e rilassa il fattore
del clamp indiretto a 0.5 (agisce solo con `-C`); `pre-final`, `final` e
`ultra` girano con la **feature-set completa** (SSS attivo, NEE su tutte le
luci, senza clamp). Ogni preset rende in path tracing: gli integratori
bidirezionali si scelgono a mano, scena per scena (sezione 4c). `ultra` resta a 512 spp di proposito: a 4K la densità di
pixel nasconde il rumore per-pixel che a 1080p sarebbe visibile, e 1024 spp
raddoppierebbero un render già lungo.

Le varianti `*-tiny` sono a **un quarto di risoluzione** rispetto al
full HD (480×270 = ¹⁄₁₆ dei pixel di 1920×1080, ¹⁄₄ di `*-small`),
pensate per validare la scena al volo - individuare errori macroscopici
di composizione o illuminazione prima di lanciare un render più lungo.

Le varianti `*-small` sono **esattamente metà risoluzione** su ogni
asse (960×540 = ¼ dei pixel di 1920×1080), quindi costano circa ¼ del
preset full-HD corrispondente restando leggibili a schermo.

**Qualunque flag esplicito vince.** Il preset compila solo i valori
che non hai passato manualmente: `-q final -d 32` lancia il preset
final ma porta a 32 la profondità di ogni tipo di rimbalzo (utile per
una pila profonda di vetri);
`-q standard -w 640 -H 360` rimpicciolisce il preset standard senza
toccarne il sampling.

**Denoiser.** I preset `draft*`, `standard*` e `pre-final*` abilitano di
default Intel Open Image Denoise (`--denoiser oidn`; `draft*` usa
`--denoise-quality fast`, gli altri `high`): la beauty HDR lineare viene
filtrata prima del tone mapping usando le guide albedo e normale - è ai
campionamenti bassi e medi che il denoiser rende di più, ed è lì che i
512 spp di `standard` lasciano spesso quella grana residua che il denoiser
assorbe in una frazione di secondo. Dove la libreria nativa non si carica
(una piattaforma per cui la build non la include) i preset usano il
`--denoiser nfor` integrato, con una nota nell'output. Misurato contro
immagini convergenti, `oidn` lascia da 1.2 a 90 volte meno errore di `nfor`
e da 3 a 140 volte meno che senza denoiser a 4-64 spp; toglie anche i
firefly di caustica non ancora convergenti, con la loro energia (una scena
con vetro può uscire più scura di qualche percento). `final` e `ultra`
restano non filtrati per scelta (i render di riferimento convergenti
mantengono ogni dettaglio non filtrato); aggiungi un `--denoiser oidn`
esplicito per filtrarli, o `--denoiser none` per spegnere il default del
preset. Vedi [Denoising](../technical/denoising.md) per gli algoritmi e le
misure.

**Caustiche.** Il path tracing rende le caustiche come sono, prive di bias
ma rumorose dove solo un rimbalzo fortunato trova la luce. Una scena il cui
aspetto dipende da loro si rende con `--integrator bdpt` (caustiche viste
direttamente o in uno specchio) o `--integrator vcm` (anche viste attraverso
un vetro o l'acqua) sopra qualunque preset: vedi la sezione 4c.

**`standard` - il render di qualità quotidiano.** Il tier `standard` punta
a qualità d'immagine da final su una scena *classica* - superfici
Lambertian/OpenPBR, vetri non annidati (al massimo un paio di sfere di
cristallo una davanti all'altra), marmo procedurale con parametri ordinari -
eliminando la costosa macchineria di illuminazione globale che queste scene
non usano. Rispetto a `final`: disattiva il **SSS** volumetrico (`--sss-mode off`), gira a **512 spp** con
**un solo shadow sample** (512 spp fanno già anti-aliasing), riduce i
budget dei rimbalzi (4 diffuse, 6 glossy, 12 transmission) e lascia al
**denoiser** la grana residua. Su una scena senza SSS è
nettamente più veloce di `final` a parità di resa. Come sempre i flag
espliciti vincono - es. `-q standard --sss-mode auto` riattiva il SSS,
`-q standard -s 768` alza i campioni se i bordi del vetro restano rumorosi.
Evitalo per scene che dipendono davvero da traslucenza/SSS profondo o
vetri impilati/annidati - lì usa `pre-final`/`final`.

**`pre-final` - anteprima fedele di `final`.** Stessa feature-set di `final`
(SSS attivo, gli stessi budget di rimbalzi, lo stesso
campionamento delle luci) con i budget di campionamento tagliati dove il denoiser compensa
meglio: **¼ dei campioni per pixel** (256) e **un solo shadow sample** - il
rumore delle penombre è esattamente ciò che il filtro guidato dalle feature
rimuove più pulito. Il risultato anticipa illuminazione e
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
RayTracer -i my-scene -q final -d 32

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

> **Senza `-q` si usa `draft-small`.** Quando la riga di comando omette
> `--quality`, il renderer applica il preset `draft-small` (960×540, 16 spp,
> depth 8, un solo shadow sample, denoiser OIDN): un check di composizione
> rapido e già denoised è un default di primo lancio migliore di una passata
> lenta e rumorosa. Ogni parametro di qualità qui sotto vince comunque sul
> preset se passato esplicitamente. I valori in tabella sono i default
> risultanti da `draft-small`.

| Parametro | Default | Origine |
|---|---|---|
| `-s` / `--samples` | `16` (griglia 4×4) | preset `draft-small` |
| `-d` / `--depth` | `8` (diffuse 3, glossy 4, transmission 8, volume 3) | preset `draft-small` |
| `--depth-transparent` | `16` (cut-out attraversati, fuori dal totale) | `PathDepths.DefaultTransparent` |
| `-S` / `--shadow-samples` | `1` (`draft-small`; senza preset → valore YAML per-luce, default 4) | `Program.cs` |
| `--denoiser` | `oidn` fast (`draft-small`; `nfor` dove OIDN non si carica) | preset `draft-small` |
| `--adaptive` | `off` (`draft-small`; attivo da `standard` in su, soglia `0.01`) | preset |
| `-C` / `--clamp` | spento (nessun firefly clamp) | opzionale, valore di partenza suggerito `10` (`Renderer.SuggestedMaxSampleRadiance`) |
| `--indirect-clamp-factor` | `0.25` (solo con `-C`: clamp indiretto = `-C` × fattore) | `Renderer.DefaultIndirectClampFactor` |
| `--transparent-shadows` | `off` (il vetro proietta la sua ombra e caustica fisiche) | `Program.cs` |
| `--integrator` | `path` (path tracing; `bdpt`, `vcm` su richiesta) | `IntegratorKind.Path` |
| `--exposure` | `0` EV (identità) | `Renderer.DefaultExposureEv` |
| `--light-sampling` | `auto` (`all` fino a 16 luci, `bvh` oltre) | `LightSamplingStrategy.Auto` |
| `--texture-filtering` | `auto` (filtering attivo) | `Renderer.TextureFilteringMode.Auto` |
| `--sampler` | `sobol` (Owen scramble) | `Program.cs` / `Sampler.SetKind` |
| `--mis` | `balance` (balance heuristic Veach) | `Program.cs` / `MisHeuristic` |

> Il sampler default `sobol` (Burley 2020, Owen scrambling hash-based sulla tabella Joe-Kuo) converge più rapidamente del PRNG thread-local su pixel jitter, lens sampling e primi bounce. Passa `--sampler prng` per tornare al vecchio comportamento - utile quando confronti con render storici o debugghi regressioni stocastiche.

> **`--mis balance` vs `--mis power`** - entrambi sono pesi Multiple Importance Sampling unbiased (Veach 1997 §9.2). Il default `balance` (`w = p/(p+q)`) è la balance heuristic, il peso a varianza minima per qualunque coppia di sampler. L'opzione `power` (`w = p²/(p²+q²)`) usa la power heuristic con β=2 e riduce ulteriormente la varianza quando le due PDF differiscono molto - tipicamente luci speculari piccole su materiali ruvidi, o luci puntiformi attraverso fog. Il costo computazionale è identico; puoi cambiare in qualunque momento senza rifare preprocessing.

I default sono pensati per **iterazione rapida**, non per qualità finale. Considera il profilo Preview come il minimo per un render "presentabile"; usa Standard o Final quando devi pubblicare.

---

### 3. **COMPRENDERE `-s` (CAMPIONI PER PIXEL)**

Il motore esegue **stratified sampling** su una griglia √N × √N per pixel. Passare `-s 16` produce una griglia 4×4 (16 campioni); `-s 256` una griglia 16×16 (256 campioni). Ogni cella produce un campione jittered.

**Il sampler Sobol (predefinito) usa il conteggio esatto.** Il sampler `sobol` predefinito esegue esattamente il numero di campioni richiesto - nessun arrotondamento. **Solo per `--sampler prng`:** il motore ha bisogno di una griglia √N × √N, quindi arrotonda √N per eccesso: `-s 15` diventa silenziosamente 4×4 = 16. Per controllare il costo con precisione con PRNG, preferisci quadrati perfetti: `1, 4, 9, 16, 25, 36, 49, 64, 100, 144, 196, 256, 400, 576, 784, 1024, 1600`.

**Costo:** approssimativamente lineare - raddoppiando `-s` raddoppi all'incirca il tempo di render.

---

### 4. **COMPRENDERE `-d` (PROFONDITÀ MASSIMA DEI RIMBALZI)**

`-d` limita il numero di **vertici** di un percorso: a profondità 1 un
raggio di camera vede ciò che colpisce, illuminato direttamente (emissione e
next-event estimation); ogni unità in più è un rimbalzo in più. Sotto il
totale, cinque limiti contano i rimbalzi secondo il tipo di lobo campionato
dal materiale:

| Flag | Conta | Perché ha un limite suo |
|---|---|---|
| `--depth-diffuse` | rimbalzi diffusi (Lambertian, base diffusa, traslucenza) | l'interriflessione diffusa perde quasi tutta l'energia in tre o quattro rimbalzi |
| `--depth-glossy` | riflessioni glossy e speculari | un corridoio di specchi ne chiede più di una stanza opaca |
| `--depth-transmission` | rifrazioni e trasmissioni dritte attraverso il vetro | ogni interfaccia ne costa una: un bicchiere d'acqua sono quattro, una fila di bottiglie una dozzina |
| `--depth-volume` | scattering dentro un mezzo (nebbia, fumo, il mezzo dentro un vetro) | un mezzo denso può diffondere centinaia di volte |
| `--depth-transparent` | cut-out attraversati (opacità geometrica 0: foglie, recinzioni) | **fuori** dal totale: un cut-out non è un rimbalzo, attraversarlo non costa nulla |

Un percorso si ferma al primo limite che raggiunge; il rimbalzo campionato
al suo ultimo vertice viene comunque seguito per la luce che incontra (senza
altri rimbalzi), così la luce diretta di ogni vertice è completa. I preset usano la
divisione della sezione 1a; un **`-d` esplicito porta ogni tipo a quel
valore** (`-d 24` vuol dire "24 di qualunque cosa") e ogni flag
`--depth-*` sovrascrive comunque il proprio tipo, entro il totale.

La **Russian roulette** chiude la maggior parte dei percorsi molto prima di
questi limiti: dal quinto rimbalzo in poi un percorso sopravvive con
probabilità pari al canale più alto del suo throughput (corretto di `η²`
attraverso le rifrazioni, così un raggio dentro il vetro non è penalizzato
dalla compressione di radianza dell'interfaccia) e, se sopravvive, viene
ripesato per 1/q. È non distorta, scambia solo varianza con velocità e non
richiede conoscenza della scena. Alzare i limiti quindi costa raramente
molto: i percorsi luminosi proseguono, quelli fiochi si fermano.

**Quando alzarli:**
- **Dielettrici impilati** - liquidi nei bicchieri, file di bottiglie di
  vino, sfere di vetro annidate: alza `--depth-transmission` (o `-d`)
  finché il vetro più interno non smette di scurirsi.
- **Mezzi partecipanti densi**: alza `--depth-volume`.
- **Corridoi di specchi**: alza `--depth-glossy`.
- **Fogliame fatto di molte card con cut-out**: alza `--depth-transparent`.

---

### 4b. **PATH GUIDING (`--guiding on`)**

Il path guiding impara, prima del render, da dove arriva la luce in ogni
parte della scena, e poi campiona una parte dei rimbalzi da quella
conoscenza invece che dal solo materiale. È pensato per le scene in cui la
luce arriva soprattutto di rimbalzo da regioni piccole: una stanza
illuminata dal sole attraverso una finestra, la luce da una porta
socchiusa, una macchia luminosa che illumina tutto un interno.

- **L'addestramento fa parte di `-s`.** Passate di 1, 2, 4, ... campioni per
  pixel (metà di `-s` per default, `--guiding-training <n>` per cambiarlo)
  imparano la distribuzione; ogni passata è una stima valida dell'immagine e
  vi entra, pesata dal suo rumore, quindi nessun campione va sprecato.
- **Non distorto e deterministico.** Un rimbalzo guidato è pesato con la
  densità della miscela BSDF/guida, e i campioni delle luci usano la stessa
  densità nel loro multiple importance sampling: l'immagine converge allo
  stesso risultato che senza guiding (verificato contro il renderer di
  riferimento su tutte le scene di validazione). Lo stesso comando dà la
  stessa immagine con qualunque numero di thread.
- **Si guidano solo le superfici ampie** (diffuse, metallo ruvido da
  roughness 0.3): un riflesso netto lo trova meglio la sua BSDF.
- **La risoluzione conta.** La guida impara dai campioni dell'immagine
  stessa: sotto circa 240 × 135 pixel ne ha troppo pochi e può aggiungere
  rumore.

Misurato su `scenes/review/guiding-room.yaml` (240 × 135, 256 spp): circa il
20% di errore in meno a parità di campioni, ma ogni campione costa circa
1.4 volte (apprendimento e ricerche nella guida), quindi a parità di tempo il
path tracing semplice resta ancora leggermente avanti nelle scene provate.
Il guiding è spento per default; provalo sugli interni difficili ad alti
campioni.

---

### 4c. **INTEGRATORI BIDIREZIONALI (`--integrator bdpt|vcm`)**

Il path tracing costruisce ogni cammino dalla camera e trova la luce
campionandola (next-event estimation) o colpendola. Certa luce la trova
solo per caso: una luce piccola vista attraverso un vetro, la caustica che
una sfera di vetro concentra sul pavimento, la rete luminosa sul fondo di una
piscina. Due altri integratori costruiscono i cammini da entrambi i capi:

- **`bdpt`, path tracing bidirezionale.** Ogni campione traccia un
  sottocammino dalla camera e uno da una luce, poi unisce ogni vertice
  dell'uno a ogni vertice dell'altro, collega i vertici di luce direttamente
  alla camera (light tracing) e colpisce le luci dal lato della camera. Ogni
  modo di costruire lo stesso cammino è pesato dal multiple importance
  sampling, così ogni tipo di luce lo porta la strategia che lo trova meglio.
  Privo di bias, ammesso con `--reference`; superfici e mezzi partecipanti
  (vertici di scattering in nebbia e fumo, entità subsurface attraverso il
  random walk del path tracer). Le caustiche viste direttamente o in uno
  specchio convergono, a circa 2-3× il costo di un campione di path
  tracing, spesso con meno rumore a parità di tempo negli interni
  illuminati attraverso un vetro.
- **`vcm`, vertex connection and merging.** `bdpt` più la fusione dei vertici
  di luce vicini a ogni vertice di camera (photon mapping progressivo) nello
  stesso multiple importance sampling. Rende la luce che nessuna connessione
  può unire: una caustica **vista attraverso** un vetro o l'acqua (luce →
  acqua → fondo → acqua → camera). Il raggio di merging parte da
  `--vcm-radius` × il raggio della scena (default 0.003) e si riduce con le
  iterazioni come i^-1/8, così il bias del merging si annulla al crescere di
  `-s`: il VCM è consistente, non privo di bias, ed è rifiutato con
  `--reference`. Il merging avviene sulle superfici (non nei mezzi).

`-s` è il numero di iterazioni: un sottocammino di camera e uno di luce per
pixel ciascuna. `-d` limita la lunghezza dei cammini come nel path tracing
(i limiti per tipo non si applicano). Non con `--guiding`; denoiser, AOV e
output EXR funzionano come sempre.

Misurato su `scenes/review/vcm-pool.yaml` (480 × 270, stesso tempo, circa
40 s): path tracing e `bdpt` mostrano il fondo della piscina come firefly
sparsi, `vcm` mostra la rete di caustiche (`scenes/review/images/vcm-pool.jpg`).
Sulle scene di validazione `bdpt` e `vcm` concordano con il renderer di
riferimento come il path tracing (rapporti medi entro lo 0.3%, come i suoi, nebbia e
fumo compresi).

```bash
# Caustiche viste attraverso l'acqua: il fondo di una piscina
RayTracer -i scenes/review/vcm-pool.yaml -q standard-small --integrator vcm -s 256
# Caustiche viste direttamente, senza bias
RayTracer -i mia-scena -q final --integrator bdpt
```

---

### 4d. **ADAPTIVE SAMPLING (`--adaptive on`)**

Con l'adaptive sampling ogni pixel fa una prima passata di
`--adaptive-min-spp` campioni (default 64), poi passate successive
raddoppiano i campioni dei pixel ancora rumorosi, fino a `-s`. Il rumore di
un pixel si legge dalle due metà dei suoi campioni; il pixel si ferma,
insieme agli otto vicini, quando il rumore scende sotto
`--adaptive-threshold` (default 0.01, circa il rumore di un valore a
schermo). Attivo di default nei preset `standard*`, `pre-final*`, `final*`
e `ultra`; `draft*` ha meno campioni della prima passata.

| Scena (misurata) | Campioni in media | Tempo | Errore |
|---|---|---|---|
| Cornell box, `-q final-tiny` | 394 su 1024 | −9% | uguale (relMSE 2.3e-6 fra le due) |
| Cornell box, 160×90, `-s 512` | 246 | −13% | uguale |
| Texture procedurali, 160×90, `-s 512` | 471 | −6% | uguale |
| Stanza al sole, 160×90, `-s 512` | 495 | 0% | uguale |

I pixel che convergono prima sono di solito quelli economici (sfondo,
cielo), quindi il tempo cala meno del numero di campioni. Alza la soglia
(`0.02`) per anteprime più rapide, abbassala (`0.005`) per le consegne. Non
disponibile con `--reference` (fermarsi sui campioni del pixel stesso
introduce un piccolo bias), `--guiding`, `--integrator bdpt|vcm` o
`--sampler prng`; lì i preset lo tolgono in silenzio, un `--adaptive on`
esplicito è un errore.

```bash
# Final con una soglia più severa
RayTracer -i mia-scena -q final --adaptive-threshold 0.005
# Campionamento uniforme, ogni pixel tutti i -s
RayTracer -i mia-scena -q final --adaptive off
```

---

### 5. **COMPRENDERE `-S` (CAMPIONI D'OMBRA)**

`-S` forza un override globale del numero di raggi d'ombra usati da ogni luce ad area (`AreaLight`, `SphereLight`, `GeometryLight`). Ogni luce costruisce una griglia stratificata √N × √N sulla propria superficie e lancia un raggio d'ombra per cella.

**Attenzione - costo moltiplicativo.** A ogni intersezione di superficie il motore lancia `S` raggi d'ombra **per ogni luce ad area, per ogni campione pixel, per ogni rimbalzo**. Con `-s 256`, `-S 4`, due luci ad area e 6 rimbalzi parli di circa `256 × 4 × 2 × 6 ≈ 12.000` raggi d'ombra per pixel. Alzare `-S` è il modo più rapido per distruggere i tempi di render.

**Regola empirica:**
- Default / Preview / Standard → `-S 1`.
- Alza `-S` a `4` (2×2) o `9` (3×3) solo quando il render generale è già pulito ma le penombre morbide a terra restano l'unica fonte di rumore.
- Usa quadrati perfetti (`1, 4, 9, 16`) - stessa stratificazione √N×√N di `-s`.

I campioni pixel (`-s`) e i campioni d'ombra (`-S`) riducono entrambi il rumore d'ombra. Spendi prima il budget su `-s`; ricorri a `-S` solo quando hai verificato che il collo di bottiglia siano le ombre (non la GI).

---

### 6. **FIREFLY CLAMP (`-C` / `--clamp`)**

`MaxSampleRadiance` (esposto come `-C`) è un limite massimo per la radianza per-campione **prima del tone mapping**. Cattura gli outlier rari prodotti da caustiche speculari, materiali a più lobi e boost della Russian Roulette - i pixel che altrimenti apparirebbero come puntini bianchi luminosi ("fireflies") nel render.

**Default: spento.** Il clamp scambia rumore con bias, e la soglia è una radianza assoluta: non isola gli outlier, taglia ogni campione più luminoso del valore. In una scena con luci più luminose della soglia toglie luce vera (una Cornell box illuminata da un pannello di radianza 20 perdeva il 22% dell'energia con il vecchio default `10`). Renderizza prima senza, usa il denoiser o più campioni contro il rumore, e ricorri a `-C` solo se restano fireflies isolati. `10` è un valore di partenza ragionevole per luci di radianza fino a qualche unità.

**Quando alzare `-C`:**
- HDRI con sole molto intenso dove il disco solare appare meno luminoso del previsto.
- Scene molto emissive in cui hai verificato che è la sorgente luminosa stessa (non le sue caustiche) ad essere soppressa. Prova `-C 25–100`.
- Togliendo `-C` il clamp si spegne del tutto. Il rischio sono fireflies in caustiche e catene speculari profonde.

**Quando abbassare ulteriormente `-C`:**
- Nebbia densa / mezzi omogenei spessi + `-d` alto.
- Scene con molti piccoli emissive luminosi visti attraverso il vetro.
- Prova `-C 5` o `-C 3`. Perdi un po' di gamma dinamica HDR sui highlight più caldi ma guadagni ombre più pulite e penombre più morbide ai rimbalzi estremi.

Il clamp usa **scaling con preservazione della luminanza**, quindi non altera la tinta sui highlight luminosi - solo la luminosità.

#### **6a. Clamp indiretto depth-aware (`--indirect-clamp-factor`)**

Un secondo clamp opzionale riduce la soppressione specificamente sui **bounce indiretti** (depth ≥ 1), offrendo un controllo indipendente sui fireflies dei bounce profondi rispetto al clamp primario.

```
--indirect-clamp-factor 0.25
```

Questo moltiplica la soglia `-C` per tutti i contributi indiretti, quindi agisce solo quando `-C` è indicato. Con il default `0.25` e `-C 10` il clamp indiretto è `2.5`: la radianza dei bounce profondi è limitata a 2.5, quella primaria a 10. Imposta `1.0` per disattivare la soppressione aggiuntiva e avere clamp indiretto uguale a quello primario.

**Quando abbassare ulteriormente:** catene caustic/speculare che producono ancora fireflies al default. Scendi fino a `0.1` per scene molto volumetriche con vetro.

**Quando alzare verso `1.0`:** scene in cui i highlight indiretti appaiono inaspettatamente smorzati - tipicamente Cornell box puramente emissive o HDRI in cui l'unico segnale luminoso legittimo arriva da bounce indiretti.

#### **6b. Ombre trasparenti (`--transparent-shadows`)**

```
--transparent-shadows on
```

**Default: off.** Il vetro si comporta in modo fisico: proietta la sua ombra, e la luce che rifrange arriva sul pavimento lungo i cammini che lo attraversano davvero, cioè la caustica (nitida ma rumorosa con pochi campioni in path tracing; `--integrator bdpt` o `vcm` la risolvono, sezione 4c). Le superfici con indice adattato (`refraction_index: 1`, il guscio trasparente che delimita un volume di nebbia o acqua) e i fogli `geometry_thin_walled` lisci lasciano sempre passare i raggi d'ombra: la loro trasmissione non devia la luce, quindi è esatto.

`on` è un'approssimazione artistica: i raggi d'ombra attraversano il vetro in linea retta, tinti dalla sua trasmittanza di Fresnel, così il vetro chiaro proietta una mezz'ombra morbida e tinta invece di un'ombra e una caustica. I cammini di luce rifratta dalle superfici diffuse attraverso il vetro verso le luci vengono scartati, così la stessa luce non è contata due volte. Usalo per anteprime veloci e senza rumore di scene con molto vetro; lascialo spento per le immagini finali.

#### **6c. Light importance sampling (`--light-sampling`)**

```
--light-sampling power
```

Seleziona come il renderer sceglie quale luce interrogare per ogni evento NEE:

| Valore | Comportamento | Quando usarlo |
|---|---|---|
| `auto` | `all` fino a 16 luci, `bvh` oltre | **Default** |
| `all` | Somma su tutte le luci | Poche luci: ogni luce campionata a ogni vertice |
| `power` | Campiona una luce ∝ `ApproximatePower` | Scene con molte luci di luminosità mista, tutte visibili da ovunque |
| `uniform` | Campiona una luce uniformemente | Debug / baseline di confronto con `power` |
| `bvh` | Campiona una luce attraverso un BVH delle luci (Conty e Kulla 2018): la potenza di ogni luce pesata con la sua distanza e il suo orientamento rispetto al punto | Molte luci sparse nello spazio (una strada di lampioni, una stanza di faretti, strisce emissive): i campioni vanno alle luci vicine |

Con `all` il renderer lancia `ShadowSamples` raggi d'ombra per luce per shading point - O(N·S). Con `power`, `uniform` o `bvh` li lancia per una sola luce e divide per la probabilità di averla scelta per restare non distorto - O(S); la stessa probabilità entra nei pesi di importance sampling multiplo dal lato della BSDF, quindi ogni strategia converge alla stessa immagine. In una scena con 90 piccole luci a griglia, `bvh` riduce il rumore per pixel di circa quattro volte rispetto a `uniform` a parità di campioni. L'environment e il sole analitico sono scelti per primi con probabilità 1/(numero di luci infinite + 1), poi il BVH.

#### **6d. Texture filtering (`--texture-filtering`)**

```
--texture-filtering <auto|on|off>
```

Controlla l'anti-aliasing analitico di texture procedurali e image via
ray differentials. La camera emette raggi ausiliari attraverso i vicini
`+x`/`+y` del pixel; la dimensione del footprint a ogni hit di superficie
pilota una lookup pre-filtrata della texture - Perlin/fBm clampano le
ottave sopra Nyquist, Voronoi fa supersampling adattivo, le texture image
usano mipmap + filtering anisotropico (fino a 8 campioni trilineari lungo
l'asse lungo del footprint). Il footprint vale un pixel diviso per √spp,
mai meno di un ottavo di pixel; viene filtrato solo il primo hit del raggio
di camera, i rimbalzi successivi sono campionati in un punto.

| Valore | Comportamento | Quando usarlo |
|---|---|---|
| `auto` | Filtering attivo (la camera emette differentials) | **Default** - sempre sicuro |
| `on`   | Identico ad `auto`, riservato a euristiche future | Equivalente ad `auto` |
| `off`  | Nessun differential, ogni texture campionata point-only | Confronto baseline, benchmark, verifica che un eventuale aliasing arrivi dal pipeline texture e non dal sampling della camera |

Il default `auto` rimuove moiré, shimmer e grana ad alta frequenza su
superfici lontane o ad angoli radenti - tipicamente permette di
dimezzare `-s` di 4× su scene outdoor con terreno procedurale o
movimenti di camera grandangolari senza perdere qualità. Disattivare
con `off` ha senso solo per debug o A/B comparison; il costo del
filtering è minimo (pochi punti percentuali nelle scene tipiche).

#### **6e. Esposizione fotografica (`--exposure`)**

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
base color sottostante - texture procedurali, venature dei marmi e
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
- **Preferisci la divisione dei preset a un `-d` nudo.** Un `-d` esplicito porta ogni tipo di rimbalzo allo stesso limite; per dare più spazio al vetro alza solo `--depth-transmission`.
- **Non combinare `-s` alto con `-S` alto senza motivo.** `-s 1024 -S 16` è quasi sempre uno scambio cattivo. `-s 1024 -S 4` di solito eguaglia visivamente a ¼ del costo.
- **Le scene cariche di vetro sono il motivo tipico per alzare un limite.** Alza `--depth-transmission` a `24` o `32` solo quando vedi interni inaspettatamente neri nei dielettrici impilati.
- **Riproducibilità in CI.** I valori `-s`/`-d`/`-S` nei `.github/workflows/*.yml` dovrebbero essere un profilo nominato specifico (tipicamente Preview o uno Standard ridotto) - non valori ad-hoc.

---

### 8. **TIP SPECIFICI PER MEZZI PARTECIPANTI (NEBBIA / FUMO)**

3D-Ray supporta quattro tipi di medium globali - `homogeneous`, `height_fog`,
`procedural`, `grid` - più le phase function `isotropic`, `hg`, `rayleigh`,
`double_hg`, `schlick`. Ogni tipo ha un costo/rumore molto diverso.

- **Homogeneous e height_fog sono "gratis"** rispetto al rendering normale: la
  trasmittanza ha forma chiusa, le distanze sono campionate esattamente. Un Preview è già
  usabile; uno Standard è quasi sempre sufficiente.
- **Procedural (Perlin fBm) e grid sono attraversati con collisioni nulle**
  (spectral tracking per le distanze, ratio tracking per i raggi d'ombra, su
  una griglia di majorant per `grid`): senza distorsione, ma più rumorosi per
  costruzione, soprattutto nelle zone dense.
  - Preview mostra grana evidente - va bene per la composizione.
  - Per immagini pubblicabili punta a `-s 576` (24×24) o `-s 1024`.
  - Se vedi rumore concentrato nel cono luminoso, aumenta `-s` (non `-S`).
- **Firefly clamp con nebbia densa.** Mezzi con `sigma_s` alto e `-d 8+`
  producono talvolta spike luminosi rari. Se più campioni o il denoiser non
  li tolgono, aggiungi `-C 5` o `-C 3`: scambi un po' di bias con
  un'immagine molto più pulita.
- **Luci point/spot dentro un medium.** In `homogeneous`, `height_fog` e
  `atmosphere` la loro luce è campionata anche in modo equiangolare (distanze
  distribuite come l'angolo visto dalla luce) e combinata per MIS con il volo
  libero: l'alone intorno all'emettitore, dove 1/d² ha il picco, converge
  pulito. Sulla scena di validazione `fog-sphere-light` l'errore a parità di
  campioni è sceso di 16 volte. `soft_radius` resta la dimensione fisica del
  bulbo, non un rimedio ai firefly.
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
  `data:`). La risoluzione non incide sul costo di rendering - solo sul
  tempo di parsing e sull'uso di memoria.

**Profilo consigliato per showcase volumetric:**
```bash
# Preview volumetric (controllo composizione, ~30-60 s)
RayTracer -i scene.yaml -w 400 -H 225 -s 64 -d 4 -S 1

# Standard volumetric (review; i mezzi con tracking ancora un po' rumorosi)
RayTracer -i scene.yaml -w 800 -H 450 -s 400 -d 6 -S 1

# Final volumetric (pulizia pubblicabile)
RayTracer -i scene.yaml -w 1920 -H 1080 -s 1024 -d 8 -S 4 -C 5
```

---

### 9. **DOCUMENTAZIONE CORRELATA**

- [`docs/technical/path-tracing-and-lighting.md`](../technical/path-tracing-and-lighting.md) - interni del path tracer, NEE e Russian Roulette.
- [`docs/technical/rendering-pipeline.md`](../technical/rendering-pipeline.md) - panoramica della pipeline.
- [`docs/reference/riferimento-scene.md`](./riferimento-scene.md) - schema YAML delle scene.
