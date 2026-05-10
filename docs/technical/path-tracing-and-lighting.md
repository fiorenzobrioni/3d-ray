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
1.  **Campiona ogni luce** nella scena (`ComputeDirectLighting`).
2.  Lancia un **raggio d'ombra (Shadow Ray)** verso un punto casuale della luce (per le Area Light) o verso la posizione della luce (Point/Spot).
3.  Calcola il contributo BRDF della superficie per quella specifica direzione della luce.
4.  Se la luce è visibile dal punto di hit, aggiunge il contributo (radiance × BRDF) all'accumulatore.

### 2.2 Separazione tra Luce Diretta e Indiretta — Multiple Importance Sampling
Per evitare di contare due volte l'energia emessa (Double Counting) e per ridurre la varianza, il motore implementa il **Multiple Importance Sampling** di Veach (1997). Due strategie campionano lo stesso integrando dell'illuminazione diretta:

1. **Light sampling (NEE)** — campiona un punto sulla luce con densità $p_L(\omega)$.
2. **BSDF sampling** — campiona la direzione del rimbalzo dalla distribuzione di importance del materiale, con densità $p_B(\omega)$.

I due contributi vengono pesati con la **balance heuristic** (default) o la **power heuristic** β=2 (selezionabile via `--mis power`):

$$w_\text{balance}(p, q) = \frac{p}{p + q}, \qquad w_\text{power}(p, q) = \frac{p^2}{p^2 + q^2}.$$

La power heuristic riduce la varianza per coppie molto asimmetriche (es. luce piccola + materiale ruvido) sopprimendo più aggressivamente lo sampler peggio adattato.

Tutti i materiali (Lambertian, Metal, MixMaterial, DisneyBsdf) implementano la tripla simmetrica `Sample` / `Pdf` / `Evaluate` su cui poggia la combinazione MIS; il `Renderer` propaga `prevBsdfPdf` e `prevIsDelta` lungo il path e li usa per pesare l'emissione al successivo hit (`WeightEmission`) e il sky miss (`SampleSky`). Lobi delta puri (specchio perfetto, rifrazione ideale, point/directional/spot light) sono riconosciuti dai flag `IsDeltaScatter` / `IsDelta` e ricevono peso 1 — non possono essere campionati dall'altra strategia.

L'estensione MIS copre anche il **bounce volumetrico**: la phase function espone `Pdf(wo, wi)` e il suo valore viene threadato come `prevBsdfPdf` quando il raggio prosegue dopo un evento di scattering nel medium globale. Combinato con la trasmittanza Beer-Lambert applicata già nelle shadow ray, questo dà MIS pieno fra phase-sampling e light-sampling per l'in-scattering.

> Per i contratti fra `IMaterial` / `ILight` / `IPhaseFunction` e il `Renderer`, le formule complete delle heuristiche, e la trattazione dei casi limite (lobi delta, mixture estimator, Metal NDF vs VNDF) vedi il documento dedicato [Multiple Importance Sampling](./multiple-importance-sampling.md).

### 2.3 Transparent Shadow Rays

Lo shadow ray del NEE non è un semplice test booleano di occlusione: attraversa le superfici trasmissive (vetro `dielectric`, Disney con `spec_trans > 0`, mix di entrambi) accumulando un fattore di trasmissione per canale, esattamente come fanno Arnold e Cycles per default. Senza questo passaggio, una lente o una bottiglia di vetro proietterebbe un'ombra dura identica a un occluder opaco — visivamente sbagliato.

**Algoritmo** (`Geometry/ShadowRay.Transmittance`)

1. Si parte da throughput = (1, 1, 1) lungo lo shadow ray.
2. Si esegue `world.Hit` sul segmento corrente; se non c'è hit, la luce è raggiunta — return throughput.
3. Al primo hit si interroga `IMaterial.ShadowTransmittance(wi, rec)`; se è zero (opaco) si termina con `Vector3.Zero`.
4. Altrimenti `throughput *= ShadowTransmittance`, l'origine avanza appena oltre l'hit (offset lungo la normale geometrica), e si itera.
5. Cap di sicurezza a 8 traversate (≥ 4 interfacce per geometrie a guscio annidato).

**Modelli di trasmittanza per materiale**

- **`Dielectric`**: $T = (1 - F_\text{dielectric}(\cos\theta, \eta)) \cdot \text{albedo}$, dove $F$ è la formula di Fresnel completa (`MathUtils.FresnelDielectric`). Una sfera di vetro accumula due fattori (entrata + uscita), riproducendo il rim più scuro a grazing angles. Nessun assorbimento volumetrico.
- **`DisneyBsdf`** (solo se `spec_trans > 0`):
  - Per-interfaccia: $T = \text{specTrans} \cdot (1 - F) \cdot \text{tint}$, con `tint` da `ResolveTransmission` (legacy `sqrt(baseColor)` se `transmission_color` non è impostato, oppure `transmission_color` puro per `transmission_depth = 0`, oppure $\mathbf{1}$ in modalità Beer-Lambert).
  - Volumetrico Beer-Lambert: $\sigma_a = -\ln(\text{transmission\_color}) / \text{transmission\_depth}$ esposto via `IMaterial.ShadowAbsorption`. Il walker integra $\exp(-\sigma_a \cdot d)$ sul segmento interno fra il front-face hit (entrata) e il successivo back-face hit (uscita). Risultato: l'ombra di un rubino, smeraldo, ambra o zaffiro è correttamente colorata, in coerenza con la trasmissione del lobo BSDF che già usa lo stesso $\sigma_a$ via medium-switch.
- **`MixMaterial`**: blend lineare di `ShadowTransmittance` e `ShadowAbsorption` dei due child.
- Default per tutti gli altri materiali: `Vector3.Zero` per entrambi (opaco).

**Limiti dell'approssimazione**

Il raggio non viene rifratto né perturbato: viaggia in linea retta. Questo significa:
- ✅ Ombre del vetro ammorbidite dalla trasmissione Fresnel.
- ✅ Vetri colorati con `transmission_depth` proiettano ombra tinta via Beer-Lambert.
- ✅ Color bleeding diffuso attraverso uno specchio o un vetro tinto.
- ❌ Caustiche focalizzate (la "lente" di una sfera di vetro che concentra la luce in uno spot luminoso) NON emergono dalla NEE: continuano a venire solo dal path tracing forward indiretto, con la consueta varianza alta.
- ❌ Vetro frosted/rough (Disney `roughness > 0` con `spec_trans > 0`): lo shadow ray va dritto come fosse vetro liscio. Una soft-transmission GGX sullo shadow ray richiederebbe sampling speculare con MIS, fuori scope per Strada 1.

Per caustiche e roughness corretti su shadow ray servono Manifold Next Event Estimation (MNEE) o photon mapping / VCM. Vedi la sezione *Roadmap caustiche* del `DEVLOG.md`.

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

### 3.1 Adattività alla Scena
3D-Ray implementa una variante **adattiva** della Russian Roulette:
- Analizza la potenza totale delle luci durante la costruzione del renderer.
- **Scene a illuminazione diretta (Normal)**: RR più aggressiva dopo 4 rimbalzi, poiché la maggior parte dell'energia proviene dai primi bounce.
- **Scene a illuminazione indiretta (Low-light)**: RR conservativa (almeno 8 rimbalzi), per dare al raggio più possibilità di trovare una sorgente di luce emissiva o uscire verso il cielo.

La probabilità di sopravvivenza del raggio è legata alla sua riflettanza (luminanza): raggi "deboli" che trasportano poca energia vengono eliminati prima, concentrando il calcolo sui percorsi più luminosi.

---

## 4. Pipeline di Post-Processing

Il risultato finale del Path Tracer è un'immagine HDR (High Dynamic Range), che viene trasformata per la visualizzazione tramite:
1.  **ACES Filmic Tone Mapping**: Utilizziamo una curva cinematografica (Narkowicz approximation) che gestisce in modo naturale le sovraesposizioni e rende i gradienti di luce più ricchi.
2.  **Gamma Correction (2.2)**: Rimappatura della luce lineare (fisica) allo spazio di colore sRGB del monitor.
3.  **Firefly Guard**: Clamping adattivo dei campioni prima dell'accumulo per eliminare i pixel "caldi" causati da percorsi statistici ad altissima energia ma bassa probabilità.

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

Il texel $(x, y)$ viene convertito in coordinate angolari $(φ, θ)$ e quindi in una direzione 3D nello spazio mondo. Viene lanciato un shadow ray in quella direzione; se non ci sono occlusioni, il contributo della luce viene accumulato.

### 5.4 Calcolo della PDF in Angolo Solido

La probabilità di aver selezionato quel texel è il prodotto delle due probabilità condizionate:

$$p_\text{pixel}(x, y) = p_\text{row}(y) \cdot p_\text{col}(x \mid y)$$

Per usare questa probabilità nell'equazione di rendering, deve essere espressa rispetto all'angolo solido $d\omega$ anziché rispetto al texel. La conversione è:

$$p_\Omega(\omega) = \frac{p_\text{pixel}}{\Delta\omega}, \quad \Delta\omega = \sin\theta_\text{colatitudine} \cdot \Delta\theta \cdot \Delta\phi$$

dove $\Delta\theta = \pi/H$ e $\Delta\phi = 2\pi/W$ sono le dimensioni angolari di un texel. Il denominatore è l'angolo solido del singolo texel campionato.

### 5.5 Campionamento del Gradient Sky (Sun Disk)

Per il cielo procedurale con sun disk, il campionamento diretto è più semplice: la direzione campionata è sempre quella del sole (`SunDirection`), perturbata da un piccolo jitter proporzionale all'angolo del disco (`size`). La PDF corrispondente è l'inverso dell'angolo solido del disco solare.

Il sole viene trattato come una luce direzionale con area finita: può proiettare ombre morbide se `size` è grande (sole basso e diffuso), oppure ombre nette con `size` piccolo (sole allo zenit in cielo sereno).

Il corpo del gradiente (zenith / horizon / ground) **non** è campionato direttamente: è una sorgente diffusa a bassa frequenza per cui il BSDF importance sampling sul percorso di miss è già ottimale, e una CDF analitica avrebbe varianza simile al BSDF puro.

### 5.6 Campionamento del Flat Sky

Quando il cielo è di tipo `flat` con luminanza > 0, viene campionato uniformemente sulla sfera unitaria: PDF costante `1/(4π)` per tutte le direzioni. Il caller (`EnvironmentLight.IlluminateAndTest`) scarta automaticamente le direzioni nell'emisfero inferiore della normale (rifiuto via `n·l ≤ 0`), quindi metà dei sample sono "sprecati" su superfici planari ma il bias resta zero. Questa strategia rispecchia il comportamento dei "uniform world backgrounds" di Cycles e Arnold: piccola riduzione di varianza per ogni bounce diffuso, irrilevante per riflessioni speculari (dove il BSDF è già focalizzato).

### 5.6 Stima Deterministica della Luminanza (per la Russian Roulette)

Il costruttore del renderer, prima di avviare `Parallel.For`, effettua un'analisi della scena per decidere i parametri della Russian Roulette (scene in luce diretta vs. indiretta). Questa analisi chiama `EnvironmentLight.ApproximatePower(sceneBounds)`, che deve essere **completamente deterministica** — senza chiamate a `RandomFloat()`.

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

## Riferimenti

- Codice sorgente: `src/RayTracer/Rendering/Renderer.cs`, `src/RayTracer/Lights/EnvironmentLight.cs`
- Codice HDRI sampling: `src/RayTracer/Textures/EnvironmentMap.cs`
- [Importance Sampling of HDR Environment Maps — Pharr, Jakob, Humphreys — PBRT](https://pbr-book.org/3ed-2018/Light_Transport_I_Surface_Reflection/Sampling_Light_Sources)
- Codice sorgente: `src/RayTracer/Lights/SphereLight.cs`
- [PBRT §6.2.3 — Sampling Spheres](https://pbr-book.org/3ed-2018/Light_Transport_I_Surface_Reflection/Sampling_Light_Sources#SamplingSpheres)
- [Pharr, Jakob, Humphreys — "Physically Based Rendering", Cap. 6](https://pbr-book.org/)
