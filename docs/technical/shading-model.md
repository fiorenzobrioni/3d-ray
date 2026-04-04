# Modello di Shading e Materiali

Questo documento approfondisce la fisica e la matematica dietro l'interazione raggio-superficie in 3D-Ray, con particolare focus sul modello PBR avanzato.

## 1. Disney Principled BSDF

Il materiale principale (`type: "disney"`) implementa il modello BRDF unificato proposto da Brent Burley (Disney Animation) nel 2012. Questo modello permette di rappresentare una vasta gamma di materiali reali (metalli, plastiche, vetri, tessuti) attraverso un set di parametri artistici che vengono mappati internamente a funzioni di distribuzione micro-sfaccettature fisicamente corrette.

### 1.1 Microfacet Model (Cook-Torrance)
Per i lobi speculari e il clearcoat, il motore utilizza il modello di Cook-Torrance:

$$ f(l, v) = \frac{D(h) G(l, v, h) F(v, h)}{4(n \cdot l)(n \cdot v)} $$

*   **D(h) - Normal Distribution Function (NDF)**: Utilizziamo la distribuzione **GGX** (Trowbridge-Reitz), nota per le sue code lunghe che simulano meglio i riflessi del mondo reale.
    $$ D(h) = \frac{\alpha^2}{\pi ((n \cdot h)^2 (\alpha^2 - 1) + 1)^2} $$
    dove $\alpha = \text{Roughness}^2$.

*   **G(l, v, h) - Geometry Term**: Implementiamo il modello **Smith** correlato all'altezza per mascheramento e ombreggiamento (masking/shadowing), che assicura la conservazione dell'energia.

*   **F(v, h) - Fresnel Term**: Utilizziamo l'approssimazione di **Schlick** per calcolare la riflettanza in base all'angolo di incidenza:
    $$ F = F_0 + (1 - F_0)(1 - \cos \theta)^5 $$

### 1.2 I Cinque Lobi
Il materiale Disney in 3D-Ray campiona stocasticamente cinque lobi:

1. **Diffuse** — Disney Diffuse con retro-riflessione radente e approssimazione di scattering sottosuperficiale (`subsurface`).
2. **Specular** — Lobo GGX per riflessi metallici e dielettrici. Per i metalli F₀ ≈ luminanza del `baseColor`; per i dielettrici F₀ ≈ `0.04 × specular`.
3. **Transmission** — Rifrazione di Fresnel integrata con la rugosità GGX per effetti smerigliati (`spec_trans`).
4. **Sheen** — Riflesso vellutato radente per tessuti e seta, campionato con cosine-weighted sampling.
5. **Clearcoat** — Secondo lobo speculare con IOR fisso 1.5 e rugosità indipendente per vernici e lacche.

### 1.3 Selezione Stocastica dei Lobi e Importance Sampling

Ogni rimbalzo indiretto del path tracer deve scegliere **una** direzione scattered. Poiché il Disney BSDF è una somma di contributi, la scelta avviene stocasticamente: si seleziona un lobo con probabilità proporzionale alla sua energia attesa, si campiona una direzione da quel lobo, e si compensa l'attenuation per mantenere l'estimatore Monte Carlo unbiased.

#### Calcolo dei pesi per lobo

I pesi non sono costanti arbitrari: approssimano il contributo energetico di ciascun lobo prima di conoscere la direzione incidente.

```
diffuseW  = (1 - metallic) × (1 - specTrans)
specularW = max(0.1, lerp(F₀_dielectric, 1, metallic))
transW    = (1 - metallic) × specTrans
sheenW    = sheen × 0.25 × diffuseW
clearW    = clearcoat × 0.04          ← F₀ del clearcoat (IOR ≈ 1.5)
```

Il floor `max(0.1, ...)` su `specularW` garantisce che il lobo speculare venga sempre campionato, anche per dielettrici con F₀ molto basso — fondamentale perché il riflesso speculare è visivamente dominante su superfici lisce anche a F₀ = 0.04.

Le probabilità normalizzate sono:

$$p_i = \frac{w_i}{\sum_j w_j}$$

#### Compensazione Monte Carlo (unbiased estimator)

Selezionato il lobo $i$ con probabilità $p_i$, la direzione campionata da quel lobo produce un'attenuation $a_i$. Per mantenere l'estimatore corretto, l'attenuation viene divisa per la probabilità di selezione:

$$\text{attenuation}_\text{final} = \frac{a_i}{p_i}$$

In questo modo, in media sui molti campioni, ogni lobo contribuisce con il suo peso corretto indipendentemente da quanto spesso viene scelto.

#### Campionamento GGX per il lobo Specular

Invece di campionare una direzione casuale e valutare la BRDF (campionamento ingenuo), il lobo speculare usa **GGX importance sampling**: si campiona direttamente la normal distribution function per generare una half-vector $H$ allineata con il picco della NDF.

$$H = \text{GGX\_sample}(\alpha, \xi_1, \xi_2)$$

La direzione scattered è la riflessione di $V$ attorno a $H$. La PDF di questo campionamento è:

$$p(H) = \frac{D(H) \cdot (N \cdot H)}{4 \cdot (V \cdot H)}$$

Questo riduce drasticamente la varianza per materiali lucidi (roughness bassa), dove campionare una direzione casuale produrrebbe quasi sempre un contributo nullo perché il lobo è molto stretto.

Il clearcoat usa lo stesso schema con il proprio $\alpha_\text{cc}$ derivato da `clearcoatGloss`.

#### Frosted glass: GGX normals per la trasmissione (FIX #7a)

Il lobo di trasmissione per vetri smerigliati (`spec_trans > 0`, `roughness > 0`) aveva originariamente un bug: la direzione rifratta veniva perturbata con rumore uniforme sulla sfera. Questo non corrispondeva alla distribuzione GGX usata dal lobo speculare, producendo alta varianza per vetri con roughness media.

La soluzione corretta campiona prima una micronormale $H$ dalla GGX NDF, poi calcola la rifrazione attraverso $H$ invece che attraverso la normale geometrica $N$:

```
H = GGX_sample(α, ξ₁, ξ₂)      // micronormale dal NDF
direction = Refract(ray, H, eta)  // rifrazione attraverso H, non N
```

Questo produce una distribuzione di direzioni rifratte coerente con il modello GGX, eliminando il disaccoppiamento tra trasmissione e riflessione che causava il rumore eccessivo.

#### Sheen come lobo separato (FIX #7c)

Originariamente lo sheen era sommato al diffuse nello stesso campionamento. Il problema: per materiali con `sheen` alto e `roughness` bassa (seta, tessuti lucidi), il lobo sheen ha forma diversa dal diffuse (concentrato ai bordi grazing) — campionarli insieme aumentava la varianza.

La soluzione è trattare lo sheen come quinto lobo indipendente con cosine-weighted sampling proprio, con peso `sheenW = sheen × 0.25 × diffuseW`. Questo riduce il rumore sui tessuti a parità di campioni, a costo di un'iterazione aggiuntiva nella selezione stocastica.

#### Consistenza direct/indirect (FIX #3)

Il renderer calcola l'illuminazione in due passi per ogni hit: direct lighting (NEE, via `EvaluateDirect`) e indirect lighting (via `Scatter`). Per coerenza energetica, entrambi devono usare lo stesso modello di BRDF — altrimenti la somma produce banding e fireflies che non convergono.

L'implementazione originale usava Blinn-Phong per il direct e GGX per l'indirect. Le due distribuzioni hanno forme fondamentalmente diverse (Blinn-Phong ha code corte, GGX ha code lunghe): la mancata corrispondenza richiedeva campioni molto alti per mediare. La correzione sostituisce il calcolo direct con il Cook-Torrance completo (GGX NDF + Smith geometry + Schlick Fresnel), producendo una BRDF identica nei due percorsi.

---

## 2. Normal Mapping e Spazio Tangente

Il motore supporta Normal Mapping su tutti i materiali e primitive.

### 2.1 Matrice TBN
Per perturbare la normale di shading, costruiamo una base ortonormale locale chiamata matrice TBN (Tangent, Bitangent, Normal):
1.  **Tangent (T)** e **Bitangent (B)** sono derivati dalle coordinate UV della primitiva.
2.  Utilizziamo l'**ortogonalizzazione di Gram-Schmidt** per garantire che T e B siano perfettamente perpendicolari alla normale geometrica N.
3.  In caso di back-face hitting (interno dell'oggetto), invertiamo T e B per mantenere la "handedness" corretta ed evitare che i rilievi appaiano invertiti.

La normale perturbata viene quindi calcolata come:
$$ n_{\text{world}} = \text{normalize}(T \cdot n_x + B \cdot n_y + N \cdot n_z) $$
dove $(n_x, n_y, n_z)$ sono i valori campionati dalla texture (rimappati da $[0, 1]$ a $[-1, 1]$).

---

## 3. Conservazione dell'Energia e Firefly Guard

Per garantire render puliti (senza "fireflies") e fisicamente stabili, il motore applica diverse protezioni:
- **Albedo Clamping**: I colori base sono limitati per evitare materiali che generano più energia di quella che ricevono.
- **NDF Limiting**: Il termine D(h) della GGX viene limitato per rugosità estremamente basse per evitare singolarità matematiche.
- **Lobe Weighting**: Il campionamento stocastico è pesato sull'energia attesa di ogni lobo (Importance Sampling), riducendo drasticamente il rumore nel path tracing indiretto.

---

## Riferimenti

- Codice sorgente: `src/RayTracer/Materials/DisneyBsdf.cs`
- Burley, B. (2012). *Physically Based Shading at Disney*. SIGGRAPH Course Notes.
- Walter et al. (2007). *Microfacet Models for Refraction through Rough Surfaces*. EGSR.
