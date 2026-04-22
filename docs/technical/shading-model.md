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

### 1.2 I Lobi del BSDF
Il materiale Disney in 3D-Ray campiona stocasticamente i seguenti lobi:

1. **Diffuse** — Disney Diffuse con retro-riflessione radente; il parametro `subsurface` e il parametro Disney 2015 `flatness` mescolano il Lambert classico con la forma "HK-flat" di Hanrahan-Krueger (modulata da `subsurface_color` quando l'artista vuole una tinta sotto-pelle indipendente dal `base_color`).
2. **Diffuse transmission** (`diff_trans`, Disney 2015) — Lobo Lambertiano nell'emisfero opposto per fogli, foglie, tendaggi: una frazione dell'energia diffusa viene rifratta invece che riflessa. Combinato con `thin_walled: true` evita la double-refraction tipica dei fogli sottili.
3. **Specular anisotropo** — Lobo GGX anisotropo (Burley 2012 §5.4) con `anisotropic` che controlla il rapporto αx/αy e `anisotropic_rotation` che ruota il frame tangente — highlight allungati per metallo spazzolato, capelli, vinile. Sampled con **VNDF** (Heitz 2018). Per i metalli F₀ ≈ `base_color`; per i dielettrici F₀ ≈ `0.04 × specular`. Una correzione di **thin-film iridescence** (Belcour-Barla 2017) sostituisce F₀ con la riflettanza di un film sottile di spessore `thin_film_thickness` e IOR `thin_film_ior` — bolle di sapone, opal, anti-riflesso.
4. **Multi-scattering compensation (Kulla-Conty 2017)** — Lobo additivo che recupera l'energia che il Smith single-scatter dropperebbe ad alta roughness (metalli e dielettrici a roughness > 0.3 altrimenti perdono fino al 30% della riflettanza). LUT 32×32 precalcolata con lazy init protetta da possibili dead-lock del thread pool.
5. **Specular transmission** (`spec_trans`) — Rifrazione di Fresnel campionata attraverso micronormali GGX per vetro smerigliato. Con `transmission_color` + `transmission_depth` il renderer attiva l'assorbimento **Beer-Lambert** all'interno del materiale (medium-switch riportato in `BsdfSample.NextSegmentAbsorption`), convertendo `color` in σ_a = −ln(color) / depth per un'attenuazione esponenziale lungo la distanza percorsa nel vetro.
6. **Charlie sheen** (`sheen`, `sheen_tint`, `sheen_roughness`) — Lobo microfacet "Charlie" di Estevez-Kulla 2017 (NDF invertita con coda grazing + Λ polinomiale) al posto dello Schlick sheen; produce velluto, pesca e microfibra in modo energeticamente pulito.
7. **Clearcoat** (`clearcoat`, `coat_ior`, `coat_roughness`, `coat_normal`) — Secondo lobo speculare per vernici e lacche. Nella modalità classica Disney 2012 l'α è derivato da `clearcoat_gloss`; quando l'artista forza `coat_roughness ≥ 0` o `coat_ior ≠ 1.5`, il coat diventa un lobo stile Arnold `standard_surface` con IOR esplicita e `coat_normal` dedicato (la vernice trasparente si perturba indipendentemente dalla base).

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

#### Charlie sheen (Estevez-Kulla 2017)

Originariamente lo sheen era sommato al diffuse con un taglio radente alla Schlick. Il modello attuale (`src/RayTracer/Materials/SheenCharlie.cs`) sostituisce l'approssimazione con un microfacet "Charlie": NDF invertita

$$ D_\text{Charlie}(\theta_h) = \frac{(2 + 1/\alpha)\, \sin^{1/\alpha} \theta_h}{2\pi} $$

combinata con un Λ polinomiale fittato (Estevez-Kulla 2017). Il parametro `sheen_roughness` (default 0.3) controlla la larghezza della cintura radente indipendentemente dalla `roughness` base; `sheen_tint` interpola la tinta tra bianco e la tinta del `base_color`. Il lobo è campionato cosine-weighted e compensato dal peso `sheenW = sheen × 0.25 × diffuseW`.

#### VNDF sampling (Heitz 2018)

Specular e transmission campionano le micronormali dalla **Visible NDF** invece che dalla NDF completa: si disegnano solo le micronormali effettivamente viste dal punto di vista, eliminando la selezione-rigetto per le micronormali nascoste. Elimina i clamp empirici di roughness e converge più velocemente a grazing. Per back-hemisphere L la PDF viene riportata sulla sfera completa (convenzione PBRT 2018): le micronormali che riflettono nel semispazio sbagliato trasportano peso nullo via G2/G1, ma restano nell'integrale di normalizzazione.

#### Beer-Lambert per il vetro

Quando `transmission_color` è impostato con `transmission_depth > 0`, il Disney BSDF converte il colore in coefficienti di assorbimento

$$ \sigma_a = -\frac{\ln(\text{transmissionColor})}{\text{transmissionDepth}} $$

e — al momento di una rifrazione che *entra* nel materiale — restituisce nella `BsdfSample.NextSegmentAbsorption` il σ_a da applicare al segmento di raggio successivo. Il Renderer traccia l'assorbimento lungo ogni segmento interno con `exp(−σ_a · t)`; quando il raggio esce, il sample riporta σ_a = 0 per tornare al vuoto. Il risultato: spessori reali di vetro colorato (brandy, assenzio, birra nell'ambra), invece della tinta uniforme dell'attenuation in superficie.

#### Multi-scattering compensation (Kulla-Conty 2017)

Il modello Smith single-scatter perde una frazione crescente di energia al crescere di α (fino al 30% a α = 0.9 per i metalli). Il termine di compensazione

$$ f_\text{ms}(\mu_i, \mu_o, \alpha) = \frac{(1 - E(\mu_i, \alpha))(1 - E(\mu_o, \alpha))}{\pi (1 - E_\text{avg}(\alpha))} \cdot F_\text{ms} $$

ripristina il bilanciamento via una LUT 2D di E(μ, α) (32×32, linear-interp) e un integrale LUT 1D di E_avg(α). Il fattore di Fresnel effettivo F_ms viene calcolato dalla media direzionale della Schlick. Si applica anche ai dielettrici, scalato dal loro F̄ dipendente dall'IOR. La LUT è lazy-init con guardia anti-deadlock (il primo accesso viene wrappato in un `Task.Run` se la initialization avviene già sul thread pool, altrimenti rischieremmo un deadlock del pool su `Task.Run().Wait()`).

#### MIS balance heuristic (Veach 1997)

Per ogni hit, il renderer combina due stimatori del direct lighting via **balance heuristic**:

$$ w_\text{NEE} = \frac{p_\text{light}}{p_\text{light} + p_\text{bsdf}}, \quad w_\text{BSDF} = \frac{p_\text{bsdf}}{p_\text{light} + p_\text{bsdf}} $$

- **NEE contribution**: shadow ray verso la luce, pesata da `material.Evaluate(V, L) × cos × lightColor × w_NEE`, con `p_bsdf` fornita da `material.Pdf(V, L)`.
- **BSDF contribution**: emissione vista al prossimo hit (o nella sky miss), pesata da `w_BSDF = prevBsdfPdf / (prevBsdfPdf + p_light)` — la `prevBsdfPdf` è quella restituita da `BsdfSample.Pdf` al bounce precedente.

I materiali legacy che esportano solo `Scatter()` (Lambert, Metal, Dielectric, Mix) usano la convenzione `prevBsdfPdf = 0`: NEE vede peso 1, l'emissione al prossimo hit viene soppressa dagli emettitori NEE-registrati (per evitare doppio conteggio) ma passa intera dagli emettitori non registrati e dai bounce delta (`IMaterial.IsDeltaScatter = true`).

Le luci espongono `IsDelta` (i point/directional/spot sono delta — NEE = 1, nessuna corrispondenza BSDF) e `PdfSolidAngle(origin, dir)` per chiudere il cerchio sul lato luminoso.

#### Consistenza direct/indirect (FIX #3)

Il renderer calcola l'illuminazione in due passi per ogni hit: direct lighting (NEE, via `EvaluateDirect`) e indirect lighting (via `Sample`/`Scatter`). Per coerenza energetica, entrambi usano lo stesso modello di BRDF (Cook-Torrance completo — GGX NDF + Smith geometry + Schlick Fresnel) così che la somma non produca banding né fireflies da mismatch fra stimatori.

---

## 2. Varianza e Convergenza: Disney BSDF vs Materiali Classici

### 2.1 Perché il Disney è più rumoroso

Il materiale Disney utilizza la **selezione stocastica dei lobi**: ad ogni rimbalzo il path tracer sceglie casualmente un solo lobo tra i cinque disponibili (diffuse, specular, transmission, sheen, clearcoat), campiona una direzione da quel lobo, e divide l'attenuation per la probabilità di selezione (`1/p`) per mantenere l'estimatore Monte Carlo corretto.

Questa compensazione `1/p` è la fonte principale di varianza aggiuntiva rispetto ai materiali classici:

- **Lambertian**: un solo lobo (cosine-weighted), attenuation = albedo. Nessuna selezione, nessuna compensazione. Varianza minima.
- **Metal**: un solo lobo (GGX specular), attenuation = albedo × ggxWeight (≤ 1.0). Nessuna compensazione. Varianza bassa.
- **Dielectric**: scelta binaria riflessione/rifrazione via Fresnel, ma entrambi i path restituiscono lo stesso peso. Nessuna compensazione. Varianza bassa.
- **Disney opaco** (es. plastica): due lobi attivi (diffuse + specular). Il ~9% dei campioni seleziona lo specular con compensazione ~11×, il ~91% seleziona il diffuse con compensazione ~1.1×. L'oscillazione tra i due crea grana visibile.
- **Disney trasmissivo** (vetro): due lobi attivi (transmission + specular). La trasmissione gestisce già il Fresnel internamente, ma il lobo specular aggiunge campioni ridondanti con alta compensazione.

### 2.2 Quando il Disney NON aggiunge rumore

Il Disney metallico puro (`metallic=1.0`, senza clearcoat né sheen) ha un solo lobo attivo — lo specular con probabilità ~100%. La compensazione `1/p ≈ 1.0` non amplifica nulla. In questa configurazione, il Disney è equivalente al `metal` classico in termini di rumore.

### 2.3 Guida pratica: quando usare Disney vs classici

| Superficie | Materiale consigliato | Motivazione |
|---|---|---|
| Pavimento, muri, soffitti | `lambertian` | Grandi superfici, differenza visiva minima, rumore massimo per area coperta |
| Tavoli, piedistalli, supporti | `lambertian` o `metal` | Superfici di sfondo, il Fresnel Disney non giustifica il costo |
| Metalli (oro, cromo, acciaio) | `disney` metallic=1.0 | **Nessun rumore aggiuntivo**, GGX corretto, Fresnel colorato per metalli |
| Plastica protagonista, pelle, cera | `disney` | Effetti unici (subsurface, sheen, Fresnel) non ottenibili con classici |
| Vernice auto, lacca | `disney` con clearcoat | Effetto a due strati non ottenibile con classici |
| Vetro chiaro semplice | `dielectric` | Più pulito e veloce del Disney equivalente |
| Vetro colorato, smerigliato | `disney` spec_trans | Feature uniche (tint + roughness) non disponibili in `dielectric` |
| Tessuti, velluto | `disney` con sheen | Effetto radente unico del Disney |

### 2.4 Sample count consigliati per materiali Disney

A causa della varianza aggiuntiva, i materiali Disney richiedono più campioni per convergere allo stesso livello di pulizia dei classici:

| Qualità target | Solo classici | Mix classici + Disney | Tutto Disney |
|---|---|---|---|
| Preview (rumoroso) | 16 spp | 32 spp | 64 spp |
| Draft (grana leggera) | 32 spp | 64 spp | 128 spp |
| Produzione (pulito) | 128 spp | 256 spp | 512 spp |

> **Nota tecnica:** La varianza del Disney decresce come `1/√spp` (legge dei grandi numeri). Per dimezzare il rumore visibile serve 4× i campioni. Passare da 64 a 256 spp dimezza il rumore; passare da 256 a 1024 lo dimezza ancora.

---

## 3. Normal Mapping e Spazio Tangente

Il motore supporta Normal Mapping su tutti i materiali e primitive.

### 3.1 Matrice TBN
Per perturbare la normale di shading, costruiamo una base ortonormale locale chiamata matrice TBN (Tangent, Bitangent, Normal):
1.  **Tangent (T)** e **Bitangent (B)** sono derivati dalle coordinate UV della primitiva.
2.  Utilizziamo l'**ortogonalizzazione di Gram-Schmidt** per garantire che T e B siano perfettamente perpendicolari alla normale geometrica N.
3.  In caso di back-face hitting (interno dell'oggetto), invertiamo T e B per mantenere la "handedness" corretta ed evitare che i rilievi appaiano invertiti.

La normale perturbata viene quindi calcolata come:
$$ n_{\text{world}} = \text{normalize}(T \cdot n_x + B \cdot n_y + N \cdot n_z) $$
dove $(n_x, n_y, n_z)$ sono i valori campionati dalla texture (rimappati da $[0, 1]$ a $[-1, 1]$).

---

## 4. Conservazione dell'Energia e Firefly Guard

Per garantire render puliti (senza "fireflies") e fisicamente stabili, il motore applica diverse protezioni:
- **Albedo Clamping**: I colori base sono limitati per evitare materiali che generano più energia di quella che ricevono.
- **NDF Limiting**: Il termine D(h) della GGX viene limitato per rugosità estremamente basse per evitare singolarità matematiche.
- **Lobe Weighting**: Il campionamento stocastico è pesato sull'energia attesa di ogni lobo (Importance Sampling), riducendo drasticamente il rumore nel path tracing indiretto.

---

## 5. Interfaccia `IMaterial`

Il renderer consuma i materiali attraverso `IMaterial` (`src/RayTracer/Materials/IMaterial.cs`). L'interfaccia è deliberatamente sottile:

- `bool Scatter(rayIn, rec, out attenuation, out scattered)` — bounce indiretto "legacy" (Lambert, Metal, Dielectric, Mix). Deve restituire una direzione e un'attenuazione; la sua PDF è implicita.
- `Vector3 EvaluateDirect(toLight, toEye, normal, rec)` — valore della BRDF moltiplicato per cos(θ_L), senza albedo (l'albedo è applicato dal Renderer attraverso l'attenuation). Chiamato per ogni shadow ray da NEE.
- Tripla simmetrica per MIS: `Evaluate(V, L, rec)` (BRDF senza cosine), `Pdf(V, L, rec)` (solid-angle PDF), `Sample(V, rec) -> BsdfSample?` (direzione + F + PDF + flag delta + medium-switch).
- `Vector3 Emit(...)` — emissione (nero per non emissivi).
- `NormalMapTexture? NormalMap` — normal map opzionale.

Due flag booleani sostituiscono i vecchi campi Blinn-Phong (`DiffuseWeight`, `SpecularExponent`, `SpecularStrength`):

- `bool NeedsDirectLighting` (default true). Lo override `false` su `Emissive` spegne la NEE per le sorgenti stesse — ricevono zero illuminazione esterna e contribuiscono solo via `Emit`.
- `bool IsDeltaScatter` (default false). Lo override `true` su `Dielectric` (e su `Metal` con `Fuzz = 0`) marca il bounce come delta: il prossimo hit vede l'emissione a pieno peso invece che soppressa dalla convenzione legacy NEE.

Il `DisneyBsdf` ignora entrambi i flag: usa sempre il path `Sample()` (che gestisce il caso delta via `BsdfSample.IsDelta` per la transmission) e partecipa al MIS tramite `Evaluate`/`Pdf`.

## Riferimenti

- Codice sorgente: `src/RayTracer/Materials/DisneyBsdf.cs`, `src/RayTracer/Materials/IMaterial.cs`, `src/RayTracer/Rendering/Renderer.cs`.
- Burley, B. (2012). *Physically Based Shading at Disney*. SIGGRAPH Course Notes.
- Burley, B. (2015). *Extending the Disney BRDF to a BSDF with Integrated Subsurface Scattering*. SIGGRAPH Course Notes. — `diff_trans`, `flatness`, `thin_walled`, `subsurface_color`.
- Walter et al. (2007). *Microfacet Models for Refraction through Rough Surfaces*. EGSR.
- Heitz, E. (2018). *Sampling the GGX Distribution of Visible Normals*. JCGT. — VNDF sampling.
- Kulla, C. & Conty, A. (2017). *Revisiting Physically Based Shading at Imageworks*. SIGGRAPH Course. — Multi-scatter compensation, Charlie sheen.
- Estevez, A.C. & Kulla, C. (2017). *Production Friendly Microfacet Sheen BRDF*. SIGGRAPH. — Charlie sheen NDF.
- Belcour, L. & Barla, P. (2017). *A Practical Extension to Microfacet Theory for the Modeling of Varying Iridescence*. SIGGRAPH. — Thin-film iridescence.
- Veach, E. (1997). *Robust Monte Carlo Methods for Light Transport Simulation*. PhD Thesis. — MIS balance heuristic.
- Burley, B. (2020). *Practical Hash-based Owen Scrambling*. JCGT. — Sampler Sobol.
