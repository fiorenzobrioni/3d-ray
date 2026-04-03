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
