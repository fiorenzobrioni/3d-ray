# Denoising — Rimozione del rumore Monte Carlo guidata dalle feature

Il denoiser rimuove il rumore Monte Carlo residuo dalla **beauty HDR
lineare** prima della trasformazione display (esposizione → ACES → gamma),
guidato da buffer ausiliari (AOV) che il renderer cattura durante la stessa
passata di rendering. È implementato interamente in C# managed
(`src/RayTracer/Denoising/`), SIMD (`Vector<float>` / `Vector256`) e
parallelo, senza dipendenze native.

CLI: `--denoiser none|nlm|nfor` (+ `--denoise-quality fast|high`); i preset
di qualità `draft*`, `standard*` e `pre-final*` abilitano `nfor` di default.
`--aov albedo,normal,depth,beauty,variance` scrive i buffer guida in formato
PFM. Vedi i [profili di rendering](../reference/profili-di-rendering.md) per
l'interazione con i preset.

## 1. Dati catturati durante il rendering

`Renderer.Render(w, h, RenderCaptureOptions)` accumula, accanto alla somma
pixel normale (che resta bit-identica — la cattura aggiunge accumulatori
laterali e non estrae numeri casuali aggiuntivi):

| Buffer | Contenuto |
|---|---|
| `Beauty` | media HDR lineare (pre-esposizione, pre-tonemap) |
| `BeautyA/B` | la stessa radianza divisa nelle medie delle **metà campioni pari/dispari** |
| `AlbedoA/B` | guida albedo della superficie al primo hit non-delta |
| `NormalA/B` | normale di shading world-space (post normal/bump map) al primo hit non-delta |
| `DepthA/B` | distanza world-space del primo hit del raggio camera (−1 = cielo) |

**Regola del primo hit non-delta.** La profondità è registrata alla prima
superficie colpita dal raggio camera, speculare o meno (la depth di un pixel
specchio è la distanza dello specchio). Albedo e normale seguono le catene
speculari perfette (delta): ogni rimbalzo delta moltiplica la propria tinta
in un peso albedo cumulativo, e la prima superficie ruvida/diffusa fa il
commit — l'albedo di un pixel specchio è quindi la tinta dello specchio per
ciò che riflette, e le guide di un pixel di vetro descrivono la scena dietro
il vetro. I miss verso l'ambiente committano il colore del cielo (clampato)
con normale zero; gli eventi di scattering nel medium committano bianco
senza feature. I cammini che terminano dentro una catena speculare (kill di
Russian Roulette, profondità esaurita, hit emissivo) ricadono sull'ultima
superficie vista.

**Varianza dual-buffer.** Con n campioni, la metà A contiene gli indici pari
(⌈n/2⌉ campioni) e la B i dispari. La varianza per pixel della media
completa è stimata come `Var ≈ ((Ā−B̄)/2)²`, stabilizzata con uno smoothing
binomiale 7×7 e con floor a `1e-5·media²`. Con `--sampler prng` le metà sono
indipendenti e la stima è unbiased; con il sampler Sobol di default le
sottosequenze pari/dispari di un'unica sequenza Owen-scrambled sono
*anti-correlate*, quindi la stima **sovrastima** la varianza vera — lo
stadio di selezione compensa (vedi §5). Questo stesso layer di buffer è la
fondazione per il futuro adaptive sampling.

## 2. Pipeline

```
RenderBuffers ──► normalizzazione depth ──► prefiltro feature ──► varianza
                                                                     │
              ┌──────────────────────────────────────────────────────┘
              ▼
   nlm:  NL-means joint, metà cross-filtrate ───────────────► beauty lineare
   nfor: regressione first-order pesata NL-means (per k) ──► set di candidati
         + media non filtrata ──► selezione MSE per pixel ──► beauty lineare
```

La depth è normalizzata in una feature [0,1] indipendente da risoluzione e
scala della scena: le distanze sono clampate a un piano lontano pari a
1.05 × il 99° percentile delle depth finite; i pixel cielo siedono
esattamente sul piano lontano.

## 3. Prefiltro delle feature

Gli AOV grezzi sono a loro volta stime Monte Carlo (anti-aliasati, sfocati
dal DOF, rumorosi dietro il vetro). Ogni feature è filtrata NL-means (raggio
di ricerca 5, raggio patch 3, k = 1) usando la **propria** varianza
dual-buffer, e **cross-filtrata**: i pesi calcolati dalla metà A mediano la
metà B e viceversa, così la feature ripulita non porta rumore
auto-correlato.

## 4. Pesi NL-means

Tutti i pesi usano la distanza patch con cancellazione di varianza (per
canale):

```
d(p,q) = ((u_p − u_q)² − (σ²_p + min(σ²_p, σ²_q))) / (ε + σ²_p + σ²_q)
w(p,q) = exp(−max(0, d̄_patch / k²))
```

Il valore atteso di `(u_p−u_q)²` per due pixel ugualmente rumorosi con la
stessa media vera è `σ²_p+σ²_q`; sottrarre il termine di cancellazione rende
la distanza ≈ 0 per le coppie "stesso segnale, rumore diverso" e la
normalizzazione rende k una manopola adimensionale di intensità del filtro.

**Decomposizione per offset.** Il motore non itera mai per-pixel ×
per-vicino × per-patch. Ognuno dei (2R+1)² offset della finestra è
processato come sweep O(N) sui piani: riga di distanze puntuali SIMD fra
l'immagine e la sua copia traslata, box-average di patch separabile a somme
correnti, poi peso + accumulo vettorizzati con un fast-exp alla Schraudolph
(errore relativo ≈2 % — irrilevante per pesi di filtro) e cutoff a
`exp(−10)`. Le righe sono processate in parallelo senza sincronizzazione
(ogni thread possiede le proprie righe di output).

**`--denoiser nlm`** si ferma qui: la distanza colore più le distanze
puntuali delle feature prefiltrate (albedo/normale/depth con bandwidth
fisse) formano un filtro joint; i pesi della metà B mediano la metà A e
viceversa, e le due metà filtrate si ricombinano pesate per numero di
campioni.

## 5. `--denoiser nfor` — regressione first-order

Per i centri-finestra su una griglia a stride 2, i pesi NL-means colore
della metà opposta guidano un fit ai minimi quadrati pesati dei colori della
finestra contro le feature prefiltrate:

```
f(q) = [1, Δalbedo(3), Δnormale(3), Δdepth(1)]      (8 coefficienti)
β = argmin Σ_q w(p,q) · (colore(q) − βᵀ f(q))²
```

Un'unica matrice di Gram 8×8 (regolarizzata Tikhonov, λ = 1e-3·traccia/8,
feature pre-scalate a deviazione standard globale unitaria) è condivisa dai
tre right-hand side RGB e risolta con un Cholesky scritto a mano. Ogni
finestra risolta predice **tutti** i propri pixel e splatta `w·predizione`
in un accumulatore globale (ricostruzione "collaborativa" — le finestre
sovrapposte si mediano, e lo stride taglia i solve di 4×). Tutto è
cross-filtrato: i pesi della metà B regrediscono la metà A e viceversa, così
il fit non insegue mai il proprio rumore. I tile sono processati in quattro
passate a scacchiera così gli splat di tile processati in parallelo non si
sovrappongono mai; l'accumulo di Gram e i dot product dello splat girano su
lane `Vector256` (8 float = esattamente un vettore di feature).

Dove la regressione batte la media pesata semplice: ogni segnale che le
feature *sanno* spiegare — dettaglio di texture sotto il rumore, gradienti
di shading guidati dalla normale, bordi DOF guidati dalla depth — viene
ricostruito invece che sfocato.

**Selezione dei candidati.** Per livello di qualità la regressione gira a
una (`fast`, k = 0.7, R = 7) o due (`high`, k ∈ {0.5, 1.0}, R = 9) intensità
di filtro. L'MSE per pixel di ogni candidato è stimato contro la **metà
opposta rumorosa** — `E[(F_A − B)²] = MSE(F_A) + Var[B]`, quindi sottrarre
la varianza produce una stima che *vede il bias*, a differenza del
disaccordo fra metà `|F_A − F_B|²` (che misura solo la varianza e
selezionerebbe volentieri un candidato sistematicamente sbagliato). La
**media non filtrata fa sempre parte del set di candidati** (il suo MSE è
esattamente la sua varianza): dove nessuna feature spiega il segnale —
ombre di contatto, caustiche — qualunque filtro cieco alle feature è
biased, e il candidato di sicurezza preserva i pixel originali. Un *margine
di selezione* addebita ai candidati filtrati una frazione della varianza
quando è attivo il sampler Sobol, compensando il bias di anti-correlazione
del §1 — calibrato perché i render Sobol quasi convergiti non regrediscano
mai, mentre quelli a pochi spp mantengono il guadagno pieno. Le mappe di
argmin per pixel sono ammorbidite con un box blur prima del blend, evitando
cuciture dure di selezione. L'output della regressione è clampato non
negativo (è radianza).

Il buffer denoised passa poi per la trasformazione display *identica* al
path non filtrato (`Renderer.ToneMapToDisplay`), e sostituisce la beauty
nell'output `--aov beauty`.

## 6. Memoria e prestazioni

La cattura aggiunge ~24 piani float ≈ 96 B/pixel: ≈ 200 MB a 1080p, ≈ 800 MB
a 4K, più scratch transitorio dei filtri. Tempi di denoise misurati su un
container 4-core 2.8 GHz a 1920×1080: `nlm` ≈ 9 s, `nfor fast` ≈ 13 s,
`nfor high` ≈ 22 s — il costo scala linearmente con i core (una workstation
tipica da 8–16 core atterra a una frazione di questi valori) ed è
indipendente dagli spp. L'overhead di cattura nel render loop è sotto il
rumore di misura, e il path di default senza cattura è bit-identico al
renderer pre-denoiser.

## 7. Limiti noti

- Il **trasporto cieco alle feature** (ombre di contatto sottili, caustiche,
  glow volumetrico) non può essere guidato; il candidato di sicurezza
  mantiene quelle regioni al loro livello di rumore originale invece di
  introdurvi bias.
- I render a **1 spp** non portano informazione dual-buffer (la metà B
  rispecchia la A, la varianza legge zero) — il denoiser degenera in un
  quasi no-op.
- Con il sampler Sobol la stima di varianza è conservativa per costruzione;
  i guadagni ad alti sample count sono volutamente smorzati dal margine di
  selezione.
- Le regioni di medium committano guide senza feature; lì il denoising
  ricade sulla sola similarità di colore.
