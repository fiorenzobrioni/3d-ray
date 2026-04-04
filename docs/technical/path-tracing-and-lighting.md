# Rendering: Path Tracing e Illuminazione

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

### 2.2 Separazione tra Luce Diretta e Indiretta
Per evitare di contare due volte l'energia emessa (Double Counting):
- Se un rimbalzo indiretto colpisce un oggetto emissivo (che partecipa già alla NEE), la sua emissione viene soppressa se il rimbalzo precedente è di tipo diffuso.
- Se il raggio colpisce una luce partendo direttamente dalla camera o dopo un rimbalzo speculare (dove la NEE non è attiva), l'emissione viene accumulata normalmente.

### 2.3 Conservazione dell'Area per Emissivi Trasformati (Jacobian)

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

## 5. Campionamento dell'Ambiente (HDRI e Gradient Sky)

Quando un raggio sfugge alla scena senza colpire alcuna geometria, campiona il cielo. Se il cielo è un gradiente procedurale o un'HDRI, può anche fungere da sorgente di luce per la NEE tramite `EnvironmentLight`. Questa sezione descrive come funziona il campionamento diretto dell'ambiente.

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

### 5.6 Stima Deterministica della Luminanza (per la Russian Roulette)

Il costruttore del renderer, prima di avviare `Parallel.For`, effettua un'analisi della scena per decidere i parametri della Russian Roulette (scene in luce diretta vs. indiretta). Questa analisi chiama `EnvironmentLight.Illuminate()`, che deve essere **completamente deterministica** — senza chiamate a `RandomFloat()`.

Per l'HDRI, la luminanza stimata viene calcolata iterando una sola volta il buffer di pixel e calcolando la media pesata:

$$L_\text{avg} = \frac{\sum_{i=0}^{W \cdot H - 1} \text{Luminance}(\text{pixel}_i)}{W \cdot H} \cdot \text{intensity}$$

Questo valore viene calcolato con lazy evaluation (la prima volta che viene richiesto) e poi cached. Per il gradient sky, la stima è una media pesata analitica di `ZenithColor`, `HorizonColor` e `GroundColor`, con il contributo del sole scalato per il suo angolo solido normalizzato.

---

## Riferimenti

- Codice sorgente: `src/RayTracer/Rendering/Renderer.cs`, `src/RayTracer/Lights/EnvironmentLight.cs`
- Codice HDRI sampling: `src/RayTracer/Textures/EnvironmentMap.cs`
- [Importance Sampling of HDR Environment Maps — Pharr, Jakob, Humphreys — PBRT](https://pbr-book.org/3ed-2018/Light_Transport_I_Surface_Reflection/Sampling_Light_Sources)
