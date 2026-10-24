# Rendering: Path Tracing e Illuminazione

> **Nota:** Per una vista d'insieme del flusso completo (dal caricamento YAML al pixel finale), 
> consulta la [Pipeline di Rendering](./rendering-pipeline.md). 
> Questo documento approfondisce la matematica dei singoli algoritmi.

Questo documento spiega il funzionamento del motore di rendering di 3D-Ray, focalizzandosi sugli algoritmi di integrazione della luce e sulle tecniche di campionamento.

## 1. Architettura del Path Tracer

3D-Ray è un motore di rendering basato su **Path Tracing** (integratore Monte Carlo ricorsivo). Per ogni pixel dell'immagine, vengono sparati raggi che rimbalzano all'interno della scena, accumulando il "colore" (radiance) raccolto lungo il percorso.

### 1.1 Equazione del Rendering
Il motore risolve numericamente l'equazione del rendering (LTE - Light Transport Equation):

$$ L_o(p, \omega_o) = L_e(p, \omega_o) + \int_{\Omega} f_r(p, \omega_i, \omega_o) L_i(p, \omega_i) (n \cdot \omega_i) d\omega_i $$

dove:
- $L_o$ è la luce uscente dal punto $p$ in direzione $\omega_o$.
- $L_e$ è la luce emessa dal punto stesso.
- L'integrale calcola la luce riflessa da tutte le direzioni $d\omega_i$ all'interno dell'emisfero $\Omega$.

---

## 2. Next Event Estimation (NEE)

Per accelerare la convergenza (ridurre il rumore) su scene con sorgenti di luce esplicite, il motore utilizza **Next Event Estimation (NEE)**, chiamata anche campionamento diretto delle luci (Direct Light Sampling).

### 2.1 Funzionamento
Ad ogni "hit" del raggio sulla superficie, invece di aspettare che un rimbalzo casuale colpisca una luce per accumulare l'energia, il motore:
1.  **Campiona le luci** (`ComputeDirectLighting`): tutte, oppure una sola scelta con probabilità nota (`--light-sampling`, §7.3).
2.  Lancia un **raggio d'ombra (Shadow Ray)** verso un punto casuale della luce (per le Area Light) o verso la posizione della luce (Point/Spot).
3.  Se la luce è visibile dal punto di hit, valuta l'integrando in quella direzione: `material.Evaluate(V, L) × |N.L|` (`Renderer.DirectIntegrand`, la stessa BSDF che il rimbalzo indiretto campiona), moltiplicato per il fattore di shadow terminator sul lato della luce (`LightSideTerminator`, Chiang et al. 2019: attenua la luce che una normale perturbata vedrebbe sotto l'orizzonte vero; vale 1 dove la normale non è perturbata).
4.  Aggiunge all'accumulatore il contributo (peso MIS × radiance della luce × integrando × trasmittanza del mezzo).

### 2.2 Separazione tra Luce Diretta e Indiretta — Multiple Importance Sampling
Per evitare di contare due volte l'energia emessa (Double Counting) e per ridurre la varianza, il motore implementa il **Multiple Importance Sampling** di Veach (1997). Due strategie campionano lo stesso integrando dell'illuminazione diretta:

1. **Light sampling (NEE)** — campiona un punto sulla luce con densità $p_L(\omega)$.
2. **BSDF sampling** — campiona la direzione del rimbalzo dalla distribuzione di importance del materiale, con densità $p_B(\omega)$.

I due contributi vengono pesati con la **balance heuristic** (default) o la **power heuristic** β=2 (selezionabile via `--mis power`):

$$w_\text{balance}(p, q) = \frac{p}{p + q}, \qquad w_\text{power}(p, q) = \frac{p^2}{p^2 + q^2}.$$

La power heuristic riduce la varianza per coppie molto asimmetriche (es. luce piccola + materiale ruvido) sopprimendo più aggressivamente lo sampler peggio adattato.

Tutti i materiali (Lambertian, Metal, Dielectric, OpenPbr, che serve anche il tipo `disney`, e MixMaterial) implementano la tripla simmetrica `Sample` / `Pdf` / `Evaluate` su cui poggia la combinazione MIS; `Emissive` non ha BSDF e il suo `Sample` nullo termina il cammino. Il campione BSDF non-delta pesa `F × |N.Wo| × terminator / pdf`, con lo stesso fattore di shadow terminator della NEE perché appartiene all'integrando; un campione delta pesa `F`. Il `Renderer` propaga `prevBsdfPdf` e `prevIsDelta` lungo il path e li usa per pesare l'emissione al successivo hit (`WeightEmission`) e il sky miss (`SampleSky`): dopo un lobo delta, o per un emettitore che la NEE non campiona, l'emissione passa a peso pieno; altrimenti riceve il peso MIS `prevBsdfPdf / (prevBsdfPdf + pLight)` (balance). Lobi delta puri (specchio perfetto, rifrazione ideale) e luci delta (point/directional/spot) sono riconosciuti da `BsdfSample.IsDelta` / `ILight.IsDelta` e ricevono peso 1: non possono essere campionati dall'altra strategia.

L'estensione MIS copre anche il **bounce volumetrico**: la phase function espone `Pdf(wo, wi)` e il suo valore viene threadato come `prevBsdfPdf` quando il raggio prosegue dopo un evento di scattering nel medium globale. Combinato con la trasmittanza Beer-Lambert applicata già nelle shadow ray, questo dà MIS pieno fra phase-sampling e light-sampling per l'in-scattering.

> Per i contratti fra `IMaterial` / `ILight` / `IPhaseFunction` e il `Renderer`, le formule complete delle heuristiche, e la trattazione dei casi limite (lobi delta, mixture estimator, Metal NDF vs VNDF) vedi il documento dedicato [Multiple Importance Sampling](./multiple-importance-sampling.md).

### 2.3 Transparent Shadow Rays

**Quando lo shadow ray attraversa una superficie.** Per default le superfici sono **opache** agli shadow ray, con un'eccezione esatta: la trasmissione **rettilinea** (`IMaterial.TransmitsStraight`), cioè un'interfaccia con indice adattato (`refraction_index: 1`, il guscio che delimita un volume di nebbia o acqua) o un foglio `thin_walled` liscio. Lì la luce non viene deviata, quindi attraversarla in linea retta è il trasporto vero. Il vetro che rifrange invece blocca lo shadow ray: la luce che lo attraversa arriva solo lungo i cammini BSDF che lo attraversano davvero (ombra fisica più caustica, rumorosa con pochi campioni; `--caustics on` la ricostruisce con i fotoni, §2.5).

Con `--transparent-shadows on` anche il vetro che rifrange lascia passare lo shadow ray, in linea retta e tinto dalla trasmittanza di Fresnel: un'approssimazione artistica senza rumore (mezz'ombra morbida al posto di ombra e caustica). Per non contare due volte la stessa luce, il renderer scarta l'emissione raggiunta da una catena speculare che parte da un vertice diffuso e attraversa solo superfici che lo shadow ray attraversa anch'esso (bit `ChainShadowPassable` dello stato di catena), e dà peso MIS pieno al campione di luce che ha attraversato una superficie (`ShadowRay.LastCrossedTransparent`): è l'unico stimatore rimasto di quel trasporto. La stessa regola vale per il cielo campionato dal NEE. Il random walk del subsurface scattering, che esce dal proprio oggetto senza rifrazione, vede il bordo della propria entità come interfaccia nulla anche per i suoi campioni di luce (`ShadowRay.PassThroughEntity`), coerente con la sua uscita.

> Fino alla Fase 0 della review il vetro era trasparente agli shadow ray per default e i cammini rifratti non venivano scartati: la luce attraverso il vetro era contata due volte e l'ombra di una sfera di vetro spariva.

Quando lo shadow ray attraversa una superficie, accumula un fattore di trasmissione per canale:

**Algoritmo** (`Geometry/ShadowRay.Transmittance`)

1. Si parte da throughput = (1, 1, 1) lungo lo shadow ray.
2. Si esegue `world.Hit` sul segmento corrente; se non c'è hit, la luce è raggiunta — return throughput.
3. Al primo hit si interroga `IMaterial.ShadowTransmittance(wi, rec)`; se è zero (opaco) si termina con `Vector3.Zero`.
4. Altrimenti `throughput *= ShadowTransmittance`, l'origine avanza appena oltre l'hit (offset lungo la normale geometrica), e si itera.
5. Cap di sicurezza a 8 traversate (≥ 4 interfacce per geometrie a guscio annidato).

**Modelli di trasmittanza per materiale**

- **`Dielectric`**: $T = (1 - F_\text{dielectric}(\cos\theta, \eta)) \cdot \text{albedo}$, dove $F$ è la formula di Fresnel completa (`MathUtils.FresnelDielectric`). Una sfera di vetro accumula due fattori (entrata + uscita), riproducendo il rim più scuro a grazing angles. Nessun assorbimento volumetrico.
- **`OpenPbr`** (anche il tipo `disney`; solo con `transmission_weight` o `subsurface_weight` > 0):
  - Per-interfaccia: $T = (1 - M) \cdot T' \cdot (1 - F) \cdot \text{tint}$, con $M$ la metalness e $T' = T + (1 - T) S$ la quota che attraversa l'interfaccia dielettrica (trasmissione più subsurface), attenuato dal coat (assorbimento e Fresnel del coat) e dal fuzz, poi miscelato con 1 secondo l'opacità (una superficie in parte ritagliata lascia passare il resto). `tint` è `transmission_color`, salvo quando c'è una `transmission_depth` (allora il colore è nel Beer-Lambert). Da dentro l'oggetto vale solo il Fresnel dell'interfaccia.
  - Volumetrico Beer-Lambert: $\sigma_a = -\ln(\text{transmission\_color}) / \text{transmission\_depth}$ esposto via `IMaterial.ShadowAbsorption` (zero quando l'interno è un mezzo legato all'oggetto, che attenua per conto suo). Il walker integra $\exp(-\sigma_a \cdot d)$ sul segmento interno fra il front-face hit (entrata) e il successivo back-face hit (uscita). Risultato: l'ombra di un rubino, smeraldo, ambra o zaffiro è correttamente colorata, in coerenza con la trasmissione del lobo BSDF che già usa lo stesso $\sigma_a$ via medium-switch.
- **`MixMaterial`**: blend lineare di `ShadowTransmittance` e `ShadowAbsorption` dei due child.
- Default per tutti gli altri materiali: `Vector3.Zero` per entrambi (opaco).

**Limiti dell'approssimazione**

Il raggio non viene rifratto né perturbato: viaggia in linea retta. Questo significa:
- ✅ Ombre del vetro ammorbidite dalla trasmissione Fresnel.
- ✅ Vetri colorati con `transmission_depth` proiettano ombra tinta via Beer-Lambert.
- ✅ Color bleeding diffuso attraverso uno specchio o un vetro tinto.
- ✅ (con `--caustics on`) Caustiche focalizzate attraverso qualunque superficie speculare (vetro/specchio/acqua/metallo liscio), prodotte dal pre-pass di photon mapping: vedi §2.5. Senza caustiche attive lo spot focalizzato viene solo dal path tracing forward indiretto, con varianza alta.

Il vetro/metallo **rough/frosted** e l'ambiente **HDRI** non sono ancora coperti dal pre-pass di fotoni e ricadono sul path tracing forward (più rumoroso) — vedi §2.5 e la sezione *Roadmap caustiche* del `PLANNING.md`.

### 2.5 Caustiche Focalizzate — Photon Mapping

Lo shadow ray dritto di §2.3 non può riprodurre la **caustica focalizzata** — lo spot luminoso che una lente o una sfera di vetro concentra su una superficie, o il riflesso che uno specchio curvo proietta su un muro. Quei cammini di luce attraversano una o più interfacce speculari prima di raggiungere una superficie diffusa (cammini della forma `L S+ D`), e il path tracing forward li campiona con varianza altissima. Con `--caustics on` il motore li ricostruisce con un **pre-pass di photon mapping** (Jensen): una passata di fotoni dalle luci, separata e precedente alla passata camera.

**Attivazione.** Basta `--caustics on` (di default sui preset `final`/`ultra`); `--caustic-photons <N>` regola il budget di fotoni emessi (default ~2–4M a seconda del preset — più alto su `final`/`ultra`). Non c'è alcun flag YAML né alcuna chiave per-entità: il pre-pass è **globale e automatico** e scopre da solo ogni caster speculare e ogni receiver diffuso. Costo zero con `--caustics off` (l'output è bit-identico a prima e il pre-pass non viene eseguito).

**Generalità.** A differenza del vecchio solver basato su manifold, questo approccio è **generale**: funziona per **qualunque** geometria speculare (specchio, vetro, acqua, metallo, primitive curve, mesh, CSG, piani) e per **tutti** i tipi di luce, comprese le sorgenti **direzionali/sole** — che il vecchio solver non poteva gestire.

**Algoritmo.**
1. **Emissione fotoni** — il pre-pass emette `--caustic-photons` fotoni di caustica dalle luci della scena, campionate secondo la loro potenza. Ogni tipo di luce contribuisce: area/geometrica (un punto e una direzione sulla superficie emittente), `sphere`, `point`/`spot` (la direzione iniziale tiene conto dell'attenuazione di cono dello spot), e **`directional`/sole** (raggi paralleli da una direzione fissa — impossibile con il solver manifold ad area).
2. **Trasporto speculare** — ogni fotone viene tracciato in avanti. Quando colpisce una superficie **speculare** (rifrazione attraverso vetro/acqua, oppure riflessione su uno specchio/metallo liscio *mirror-like*) viene riflesso/rifratto secondo Fresnel e prosegue, eventualmente attraverso più interfacce (la catena `S+`). Quando colpisce una superficie **diffusa** il fotone viene **depositato** lì: il suo cammino è `L S+ D`, cioè una caustica. I fotoni che colpiscono una superficie diffusa per primi (cammino `L D`, illuminazione diretta) sono scartati — quella luce è già coperta dal NEE. **Una riflessione quasi-speculare su un substrato non-specchio NON è un caster di caustiche**: lo *specular* liscio di un dielettrico o un **clearcoat** liscio sopra una base diffusa/colorata (es. le bilie da biliardo in resina laccata, `metallic 0`) riflette solo una sottile frazione di Fresnel — seguirla deposita pochi fotoni sparsi che il gather renderebbe come dischi spuri. Quel riflesso glossy resta interamente catturato dal path tracer forward; il pre-pass di fotoni segue la riflessione solo se la superficie è *mirror-like* (predominantemente metallica). La rifrazione (vetro) è sempre un caster.
3. **Gather con stima di densità** — i fotoni depositati sono organizzati in una struttura spaziale (es. k-d tree). Durante la passata camera, a ogni hit su una superficie diffusa il renderer raccoglie i **k fotoni più vicini** e ne ricava la radianza di caustica con una **stima di densità ai k-nearest** (l'energia dei fotoni divisa per l'area del disco che li racchiude). Più fotoni = stima più nitida e meno rumorosa; alza `--caustic-photons` per ripulire il rumore. Il gather usa un **filtro a cono** (PBRT §16.2): ogni fotone è pesato in base alla distanza dal punto di gather (1 al centro, → 0 al bordo del kernel), così la caustica ha un decadimento radiale morbido invece di un bordo netto a disco. La normalizzazione del cono conserva l'energia nel limite denso, quindi le caustiche focalizzate mantengono la loro luminosità. Quando il gather è **sotto-popolato** (meno di k fotoni nell'intero raggio di ricerca, cioè una regione sparsa di fotoni riflessi deboli e isolati) la stima viene sfumata via con uno *smoothstep* sull'occupazione: questo evita che un fotone isolato stampi un **disco piatto a raggio pieno** sul ricevitore — l'artefatto "anello di dischi" sotto caster glossy quasi-speculari (es. sfere con clearcoat). Le caustiche dense focalizzate (gather pieno, k fotoni) non sono toccate.

**Conteggio singolo.** La stessa luce non viene contata due volte: il contributo `L S+ D` raccolto dalla mappa di fotoni è esattamente quello che il path tracer faticherebbe a campionare con il BSDF, perciò il cammino BSDF-sampled corrispondente (rimbalzo diffuso → catena speculare → emettitore) viene escluso dal contributo della passata camera. Diretta (`L D`, via NEE) e indiretta diffusa restano interamente sulla passata camera; solo le caustiche speculari passano dalla mappa di fotoni.

**Limiti noti (in questa versione).**
- Le caustiche da vetro/metallo **rough/frosted** (glossy, superficie microfacet con `roughness` non trascurabile) **non** sono prodotte dal pre-pass: ricadono sul path tracer forward (più rumoroso). Solo le interfacce speculari lisce depositano fotoni di caustica.
- Le caustiche da ambiente **HDRI** non sono coperte dal pre-pass e ricadono anch'esse sul path tracer; le luci direzionali/sole **sono** invece supportate.
- La tinta interna **Beer-Lambert** lungo il cammino dei fotoni non è applicata: l'assorbimento dentro un dielettrico colorato non attenua il fotone, quindi su vetro colorato spesso la caustica può avere una lieve differenza di colore.

Caustiche multi-bounce/dispersive e i casi sopra restano per le fasi successive (vedi la sezione *Roadmap caustiche* del `PLANNING.md`).

### 2.4 Conservazione dell'Area per Emissivi Trasformati (Jacobian)

Quando un oggetto emissivo viene avvolto in un `Transform` (scale, rotate, translate), il sistema di NEE deve campionare punti sulla superficie in **world space**. Il problema: `Sample()` della primitiva interna restituisce un punto e una normale in **object space**, con un'area calcolata in object space. Usare quell'area direttamente produrrebbe un'illuminazione energeticamente errata — ad esempio, scalare una sfera emissiva di 2× in tutte le direzioni dovrebbe quadruplicare l'area esposta e quindi la luce emessa, ma senza correzione il renderer userebbe l'area originale della sfera unitaria.

La conversione corretta usa la formula del **Jacobian della trasformazione di superficie**. Dato un elemento di superficie $dA_\text{obj}$ in object space con normale $\hat{n}_\text{obj}$, l'area corrispondente in world space è:

$$dA_\text{world} = |\det(M_{3\times3})| \cdot |M^{-T} \cdot \hat{n}_\text{obj}| \cdot dA_\text{obj}$$

**Derivazione:** Un elemento di superficie è lo span dei vettori tangenti $(\partial p/\partial u, \partial p/\partial v)$. In world space diventano $(M \cdot \partial p/\partial u, M \cdot \partial p/\partial v)$. Usando l'identità del prodotto vettoriale per trasformazioni lineari:

$$(M \cdot a) \times (M \cdot b) = \det(M) \cdot M^{-T} \cdot (a \times b)$$

si ottiene che la normale di superficie trasformata ha modulo $|\det(M)| \cdot |M^{-T} \cdot \hat{n}|$, che è esattamente il fattore di scala dell'area.

In pratica, i due termini hanno significati distinti:
- $|\det(M_{3\times3})|$ — scala volumetrica della trasformazione (prodotto dei fattori di scala per matrici TRS)
- $|M^{-T} \cdot \hat{n}_\text{obj}|$ — quanto la normale si "allunga" dopo la trasformazione inversa trasposta, che dipende dalla direzione locale della superficie

Entrambi vengono precalcolati nel costruttore di `Transform` per evitare overhead per-campione.

La normale in world space si ottiene separatamente normalizzando $M^{-T} \cdot \hat{n}_\text{obj}$, con la lunghezza del vettore non-normalizzato che contribuisce al calcolo dell'area. Questa separazione è cruciale: normalizzare prima e moltiplicare dopo perderebbe l'informazione di scala contenuta in $|M^{-T} \cdot \hat{n}_\text{obj}|$.

---

## 3. Russian Roulette (RR)

Per gestire raggi che rimbalzano potenzialmente all'infinito e mantenere l'efficienza, il motore utilizza una tecnica di terminazione stocastica non polarizzata (unbiased) chiamata **Russian Roulette**.

### 3.1 Roulette sul throughput

Il path è un loop iterativo con un throughput esplicito β (il prodotto dei pesi BSDF/pdf, delle trasmittanze dei mezzi e dell'assorbimento di Beer-Lambert lungo il percorso; `Rendering/PathIntegrator.cs`). Dal secondo rimbalzo in poi, dopo ogni campionamento, il path sopravvive con probabilità

$$q = \min\left(1,\; \max_c \left(\beta_c \cdot \eta_\text{scale}\right)\right)$$

e, se sopravvive, β viene diviso per q. Non ci sono soglie minime, tetti o classificazioni della scena: la roulette è non distorta per costruzione e scambia solo varianza con velocità.

$\eta_\text{scale}$ è il prodotto di $\eta^2$ (con $\eta = \eta_t/\eta_i$) su ogni rifrazione: la radianza trasportata attraverso un'interfaccia è scalata di $(\eta_i/\eta_t)^2$, e senza questa correzione un raggio dentro il vetro avrebbe un β artificialmente basso e verrebbe ucciso per un effetto che si annulla all'uscita.

### 3.2 Profondità per tipo di rimbalzo

La profondità totale (`-d`) conta i vertici del path; sotto di essa il path conta i rimbalzi secondo il lobo che il materiale ha campionato (`BsdfSample.Lobe`): diffuse, glossy, transmission, più gli scattering nei volumi. Ogni tipo ha il suo limite (`--depth-diffuse`, `--depth-glossy`, `--depth-transmission`, `--depth-volume`): il vetro richiede molte rifrazioni, l'interriflessione diffusa poche. I cut-out (opacità geometrica 0) attraversati hanno un limite proprio (`--depth-transparent`, default 16) e non contano nel totale: non sono rimbalzi. Il lobo determina anche la categoria del raggio successivo per i flag di visibilità (`visibility.diffuse`, `visibility.glossy`, `visibility.transmission`).

---

## 4. Pipeline di Post-Processing

Il risultato finale del Path Tracer è un'immagine HDR (High Dynamic Range), che viene trasformata per la visualizzazione tramite:
1.  **Firefly clamp (opzionale)**: con `-C <n>` ogni campione oltre la soglia di luminanza viene scalato prima dell'accumulo; spento per default perché toglie anche luce legittima. NaN e Inf sono sempre scartati.
2.  **Esposizione e bilanciamento del bianco**: guadagno lineare `2^EV`, poi adattamento cromatico Bradford (`--white-balance` o `white_balance` della camera).
3.  **View transform** (`--view agx|aces|standard|legacy`, default `agx`) e **codifica sRGB** a tratti (`Rendering/ColorManagement.cs`). Dettagli in [Pipeline di Rendering](./rendering-pipeline.md) §5.

---

## 5. Campionamento dell'Ambiente (Flat, HDRI e Gradient Sky)

Quando un raggio sfugge alla scena senza colpire alcuna geometria, campiona il cielo. Tutti e tre i tipi di cielo (`flat`, `gradient` con sun disk, `hdri`) possono fungere da sorgente di luce per la NEE tramite `EnvironmentLight` quando hanno radianza non-nulla. Questa sezione descrive come funziona il campionamento diretto dell'ambiente.

### 5.1 Il Problema del Campionamento Uniforme

Un file HDRI è una fotografia a 360° in cui la luminanza è distribuita in modo molto irregolare: un sole in cielo può concentrare il 90% dell'energia totale in meno dell'1% dei texel. Se si campionasse una direzione casuale uniforme sull'emisfero, la probabilità di colpire il sole sarebbe bassissima, producendo un rumore estremo (fireflies su ogni punto di highlight e convergenza lentissima).

**Importance sampling** risolve questo problema campionando le direzioni con una probabilità proporzionale alla loro luminanza: i texel brillanti vengono campionati più spesso, quelli scuri quasi mai.

### 5.2 Costruzione delle CDF (al caricamento della scena)

Le Cumulative Distribution Functions (CDF) vengono costruite una sola volta al momento del caricamento dell'HDRI, con complessità O(W × H).

**Correzione per l'angolo solido (sin θ)**

La mappa HDRI è in proiezione equirettangolare (latitudine/longitudine). I texel vicini ai poli dell'immagine (zenith e nadir) rappresentano porzioni di sfera molto più piccole di quelli all'equatore. Se non si correggesse questo effetto, si sovracampionerebbero le zone polari.

Per ogni texel alla riga $y$ (con $\theta$ = angolo polare da 0 a π), la sua luminanza viene pesata per $\sin\theta$:

$$w(x, y) = L(x, y) \cdot \sin\!\left(\pi \cdot \frac{y + 0.5}{H}\right)$$

**CDF condizionale per riga** — $p(x \mid y)$

Per ogni riga $y$, si costruisce la CDF orizzontale normalizzando la somma cumulativa dei pesi:

$$\text{condCdf}[y][x] = \frac{\sum_{i=0}^{x} w(i, y)}{\sum_{i=0}^{W-1} w(i, y)}$$

**CDF marginale per colonna** — $p(y)$

La somma totale dei pesi di ogni riga diventa il peso della riga stessa nella CDF marginale verticale:

$$\text{margCdf}[y] = \frac{\sum_{j=0}^{y} \text{rowWeight}(j)}{\sum_{j=0}^{H-1} \text{rowWeight}(j)}$$

### 5.3 Campionamento al Render (per ogni hit di superficie)

Ogni volta che la NEE campiona l'environment light, vengono generate due coordinate casuali $r_1, r_2 \in [0,1)$:

1. **Scelta della riga** — ricerca binaria su `margCdf` con $r_1$ → indice $y$
2. **Scelta della colonna** — ricerca binaria su `condCdf[y]` con $r_2$ → indice $x$

Il punto dentro il texel non è il centro: lo scarto di $r_1$ e $r_2$ dentro il proprio intervallo della CDF è a sua volta uniforme e dà la posizione continua $(x + f_x, y + f_y)$. Così le direzioni coprono tutto il texel (con il centro fisso le direzioni erano un insieme discreto: ombre a gradini e stima distorta con HDRI piccole). La posizione viene convertita in coordinate angolari $(φ, θ)$ e quindi in una direzione 3D nello spazio mondo. Viene lanciato un shadow ray in quella direzione; se non ci sono occlusioni, il contributo della luce viene accumulato.

### 5.4 Calcolo della PDF in Angolo Solido

La probabilità di aver selezionato quel texel è il prodotto delle due probabilità condizionate:

$$p_\text{pixel}(x, y) = p_\text{row}(y) \cdot p_\text{col}(x \mid y)$$

Per usare questa probabilità nell'equazione di rendering, deve essere espressa rispetto all'angolo solido $d\omega$ anziché rispetto al texel. La conversione è:

$$p_\Omega(\omega) = \frac{p_\text{pixel}}{\Delta\omega}, \quad \Delta\omega = \sin\theta_\text{colatitudine} \cdot \Delta\theta \cdot \Delta\phi$$

dove $\Delta\theta = \pi/H$ e $\Delta\phi = 2\pi/W$ sono le dimensioni angolari di un texel e $\theta$ è quello della direzione campionata (non del centro del texel): la densità è uniforme in $(u, v)$ dentro il texel, quindi in angolo solido varia come $1/\sin\theta_\text{colatitudine}$. `PdfDirection` usa la stessa formula, così i due lati del MIS vedono la stessa densità (verificato con un chi-quadro in `SamplingRoutineTests`).

### 5.5 Campionamento del Gradient Sky (Sun Disk)

Per il cielo procedurale con sun disk, il campionamento diretto è più semplice: la direzione campionata è sempre quella del sole (`SunDirection`), perturbata da un piccolo jitter proporzionale all'angolo del disco (`size`). La PDF corrispondente è l'inverso dell'angolo solido del disco solare.

Il sole viene trattato come una luce direzionale con area finita: può proiettare ombre morbide se `size` è grande (sole basso e diffuso), oppure ombre nette con `size` piccolo (sole allo zenit in cielo sereno).

Il corpo del gradiente (zenith / horizon / ground) **non** è campionato direttamente: è una sorgente diffusa a bassa frequenza per cui il BSDF importance sampling sul percorso di miss è già ottimale, e una CDF analitica avrebbe varianza simile al BSDF puro.

### 5.6 Campionamento del Flat Sky

Quando il cielo è di tipo `flat` con luminanza > 0, viene campionato uniformemente sulla sfera unitaria: PDF costante `1/(4π)` per tutte le direzioni. Il caller (`EnvironmentLight.IlluminateAndTest`) scarta automaticamente le direzioni nell'emisfero inferiore della normale (rifiuto via `n·l ≤ 0`), quindi metà dei sample sono "sprecati" su superfici planari ma il bias resta zero. Questa strategia fornisce una piccola riduzione di varianza per ogni bounce diffuso, irrilevante per riflessioni speculari (dove il BSDF è già focalizzato).

### 5.7 Stima Deterministica della Luminanza

La scelta delle luci proporzionale alla potenza (`LightDistribution`, `LightBvh`) e la distribuzione dei fotoni delle caustiche chiamano `EnvironmentLight.ApproximatePower(sceneBounds)` nel costruttore, prima di avviare `Parallel.For`: il valore deve essere **completamente deterministico**, senza chiamate a `RandomFloat()`.

Per l'HDRI, la luminanza stimata viene calcolata iterando una sola volta il buffer di pixel e calcolando la media pesata:

$$L_\text{avg} = \frac{\sum_{i=0}^{W \cdot H - 1} \text{Luminance}(\text{pixel}_i)}{W \cdot H} \cdot \text{intensity}$$

Questo valore viene calcolato con lazy evaluation (la prima volta che viene richiesto) e poi cached. Per il gradient sky, la stima è una media pesata analitica di `ZenithColor`, `HorizonColor` e `GroundColor`, con il contributo del sole scalato per il suo angolo solido normalizzato. Per il flat sky, la stima è semplicemente `Luminance(FlatColor)`.

---

## 6. Sphere Light — Solid-Angle Sampling

La Sphere Light implementa il campionamento in angolo solido della porzione visibile di una sfera (PBRT §6.2.3), un'alternativa significativamente più efficiente al campionamento uniforme sulla superficie usato da GeometryLight.

### 6.1 Il Problema del Campionamento Superficiale

Quando una sfera emissiva viene campionata come GeometryLight, il metodo `Sphere.Sample()` sceglie un punto uniformemente sulla superficie dell'intera sfera (4πR²). Ma dal punto di vista del punto di shading P, solo la metà frontale della sfera è visibile — l'altra metà guarda dall'altra parte. I campioni che cadono sulla metà posteriore hanno `cos(θ_light) ≤ 0` e vengono scartati.

Per sfere piccole o distanti, il "cappuccio visibile" è una piccola frazione dell'intera superficie, e la percentuale di campioni utili scende ancora di più. Una sfera di raggio R a distanza d sottende un angolo solido Ω = 2π(1 − cos(θ_max)) dove cos(θ_max) = √(1 − R²/d²). Ad esempio:

| R/d | Ω / (4π) | Campioni utili (GeometryLight) |
|-----|----------|-------------------------------|
| 0.5 | 6.7% | ~13% |
| 0.2 | 1.0% | ~2% |
| 0.1 | 0.25% | ~0.5% |

Con GeometryLight, il 98–99% dei campioni viene buttato per sfere piccole/distanti.

### 6.2 Algoritmo: Solid-Angle Sampling

La Sphere Light risolve il problema campionando direzioni uniformemente all'interno del cono sotteso dalla sfera, garantendo che ogni campione sia per definizione un punto visibile.

Dato un punto P a distanza d dal centro C di una sfera di raggio R (con d > R):

1. **Angolo del cono visibile:**
   - sin²(θ_max) = R²/d²
   - cos(θ_max) = √(1 − R²/d²)

2. **Angolo solido sotteso:**
   - Ω = 2π(1 − cos(θ_max))

3. **Campionamento uniforme nel cono** (ξ₁, ξ₂ ∈ [0,1) uniformi):
   - cos(θ) = 1 − ξ₁(1 − cos(θ_max))
   - φ = 2πξ₂
   - Conversione a coordinate cartesiane nel frame locale del cono
   - Trasformazione al frame world (ONB con asse Z = direzione verso il centro)

4. **Intersezione raggio-sfera** per trovare il punto esatto sulla superficie:
   - Ray: P + tω (dove ω è la direzione campionata)
   - Si risolve l'equazione quadratica t² + 2t(oc·ω) + (|oc|² − R²) = 0
   - La radice positiva più piccola dà il punto di intersezione

5. **Test d'ombra** dal punto di shading al punto sulla sfera

### 6.3 Formula Energetica

Con il campionamento in angolo solido, la PDF è uniforme sull'angolo solido:

pdf(ω) = 1/Ω = 1/(2π(1 − cos(θ_max)))

La radianza L da un emettitore Lambertiano è costante in tutte le direzioni visibili. L'integratore Monte Carlo per un singolo campione:

E_sample = L / pdf = L × Ω

L'estimatore completo (diviso per N campioni):

E = Intensity × 2π(1 − cos(θ_max)) / N_samples

**Comportamento asintotico:** Per sfere distanti (R << d), Ω ≈ πR²/d², quindi E ≈ Intensity × πR² / (d² × N). Questo converge al comportamento di una point light (attenuazione quadratica) — fisicamente corretto e intuitivo.

### 6.4 Stratificazione

Lo spazio 2D del cono (cos θ, φ) viene suddiviso in una griglia √N × √N con jitter per cella, identica alla strategia dell'AreaLight. Questo riduce significativamente la varianza della penumbra a parità di campioni.

La stratificazione avviene nello spazio (ξ₁, ξ₂) prima del mapping al cono:
- ξ₁ = (i_cella + rand()) / √N  (distribuzione lungo cos θ)
- ξ₂ = (j_cella + rand()) / √N  (distribuzione lungo φ)

### 6.5 Caso Degenere: Punto Interno alla Sfera

Se d < R (il punto di shading è dentro la sfera), cos(θ_max) = −1 e Ω = 4π (sfera completa). Il sampling degenera nel campionamento dell'intera sfera — comportamento corretto ma senza il vantaggio del solid-angle sampling. In pratica questo caso è raro (un punto interno a una sorgente luminosa) e non problematico.

### 6.6 Confronto Quantitativo: SphereLight vs GeometryLight

Per una sfera di raggio 0.3 a distanza 5 dal punto di shading, con 16 shadow samples:

| Metrica | SphereLight | GeometryLight |
|---------|-------------|---------------|
| Campioni sul cappuccio visibile | 16/16 (100%) | ~1–2/16 (~9%) |
| Varianza relativa (normalizzata) | 1× | ~10× |
| Campioni necessari per stessa qualità | N | ~10N |

Il vantaggio cresce proporzionalmente alla distanza: più la sfera è piccola/lontana, più il solid-angle sampling è superiore.

---

## 7. Luci: stratificazione, unità, scelta, cielo (review Fase 6)

### 7.1 Stratificazione esatta
Gli `shadow_samples` di ogni luce sono stratificati su una griglia di esattamente N celle (`MathUtils.StratumGrid`/`Stratum2D`: righe = il più grande divisore di N non oltre √N), per qualunque N. Prima la griglia era √N × √N arrotondata: con 8 campioni ne copriva 9 celle e una restava vuota, con un errore sistematico dello 0.7-1.1%. Il box usa una striscia 6×1 (una faccia per stratum), le mesh e le primitive a CDF lo stratum lungo la CDF.

### 7.2 Luci d'area rettangolari: angolo solido sferico
Un rettangolo è campionato in modo uniforme nell'angolo solido che sottende (Ureña, Fajardo, King 2013, `Core/SphericalRectangle.cs`, in doppia precisione): la pdf è $1/\Omega$ e il peso di ogni campione è $L\,\Omega$, senza il termine $\cos/d^2$ che esplode vicino alla luce. Deviazione standard relativa di un campione a distanza 0.3, 1 e 4 volte il lato: 4.8%, 1.7%, 0.19% (per area: 27%, 6.1%, 0.71%). Sotto $10^{-3}$ sr e per i parallelogrammi resta il campionamento per area. La luce emette da una faccia sola e la pdf è 0 da dietro. Vale anche per i portali.

### 7.3 Scelta della luce
`--light-sampling all` somma tutte le luci; `power`/`uniform` ne scelgono una con una CDF globale; `bvh` con un BVH delle luci (`Lights/LightBvh.cs`, Conty e Kulla 2018): ogni nodo porta potenza, bounding box e cono delle normali, e l'importanza di un nodo per il punto è la potenza divisa per la distanza al quadrato, moltiplicata per il coseno del cono ridotto dell'angolo sotteso (senza la normale del ricevitore, che non serve alla correttezza). Le luci infinite (environment, sole) sono scelte per prime con probabilità $1/(n_\text{inf}+1)$. La probabilità di una luce si ricostruisce scendendo l'albero lungo i bit del suo percorso (`Pmf`), ed entra nei pesi MIS di entrambi i lati. Un nodo con importanza nulla in entrambi i figli restituisce −1 (nessun campione), che non introduce distorsione: lì nessuna luce del sottoalbero può contribuire. `auto` usa `all` fino a 16 luci e `bvh` oltre. Con 90 luci a griglia: stessa media di `all` (0.8372 contro 0.8345), deviazione standard per pixel 0.064 contro 0.275 di `uniform`.

### 7.4 Unità fisiche
L'unità di radianza della scena è il nit. `LightUnits` converte watt in lumen (683 lm/W), riduce il colore a una tinta di luminanza 1, dà il colore di un corpo nero (luogo di Planck); il loader traduce potenza, candele, luminanza e illuminamento nell'intensità di ogni tipo di luce (`SceneLoader.LightEmission`). Un profilo IES (`Lights/IesProfile.cs`, LM-63 tipo C) è tabulato in candele assolute, con i piani orizzontali mancanti ricostruiti dalla simmetria del file e il flusso integrato numericamente; `power` o `candela` lo riscalano. L'esposizione EV100 della camera vale $-\text{EV}_{100} - \log_2 1.2$ stop (convenzione basata sulla saturazione).

### 7.5 Cieli fisici
`Rendering/Sky/Atmosphere.cs` è l'atmosfera comune: scattering di Rayleigh e di Mie, assorbimento della polvere e dell'ozono (coefficienti di Bruneton 2017 a 680/550/440 nm), una tabella di trasmittanza. `AnalyticSky` è la base dei tre cieli fisici:

- **Sole coerente con il cielo**: il disco vale l'illuminanza solare al limite dell'atmosfera (133.8 klux, Darula et al. 2005, oppure 20 nella scala relativa di Nishita) per la trasmittanza lungo il percorso del sole, divisa per l'angolo solido del disco. Prima ogni modello dava al disco come radianza il valore che usava come irradianza dello scattering: il sole era circa 15 000 volte troppo debole rispetto al suo cielo.
- **Oscuramento al bordo** (`PhysicalSun.LimbFactor`): legge lineare con u = 0.6 sul coseno dell'angolo sulla superficie del sole, normalizzata così che la media sul disco valga 1. Prima la legge usava l'angolo nel cielo, che sul disco vero non scende mai sotto cos 0.265°: non faceva nulla.
- **Terreno sotto l'orizzonte**: albedo × illuminanza a terra / π, con la stessa illuminanza integrata (48×96 direzioni) che dà anche la luminanza media.

Hosek-Wilkie (`HosekWilkieSky.cs`) usa le tabelle RGB degli autori, generate da `validation/gen_hosek_wilkie_data.py` e verificate punto per punto contro il loro codice C di riferimento; la torbidità entra come torbidità di Linke anche nella polvere dell'atmosfera del sole. Preetham ha la funzione di Perez corretta $(1 + A e^{B/\cos\theta})(1 + C e^{D\gamma} + E \cos^2\gamma)$ normalizzata allo zenit. Nishita integra lo scattering singolo attraverso la stessa atmosfera.

## Riferimenti

- Codice sorgente: `src/RayTracer/Rendering/Renderer.cs`, `src/RayTracer/Lights/EnvironmentLight.cs`
- Codice HDRI sampling: `src/RayTracer/Textures/EnvironmentMap.cs`
- [Importance Sampling of HDR Environment Maps — Pharr, Jakob, Humphreys — PBRT](https://pbr-book.org/3ed-2018/Light_Transport_I_Surface_Reflection/Sampling_Light_Sources)
- Codice sorgente: `src/RayTracer/Lights/SphereLight.cs`
- [PBRT §6.2.3 — Sampling Spheres](https://pbr-book.org/3ed-2018/Light_Transport_I_Surface_Reflection/Sampling_Light_Sources#SamplingSpheres)
- [Pharr, Jakob, Humphreys — "Physically Based Rendering", Cap. 6](https://pbr-book.org/)
