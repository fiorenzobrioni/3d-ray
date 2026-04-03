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

### 1.2 Lobi di Shading
Il materiale Disney in 3D-Ray campiona stocasticamente diversi lobi:
1.  **Diffuse**: Modello Disney Diffuse con retro-riflessione radente e approssimazione di scattering sottosuperficiale (Subsurface).
2.  **Specular**: Lobi GGX per riflessi metallici e dielettrici.
3.  **Clearcoat**: Secondo lobo speculare con IOR fisso (1.5) e rugosità indipendente.
4.  **Sheen**: Riflesso vellutato radente, campionato con un lobo dedicato per ridurre il rumore sui tessuti.
5.  **Transmission**: Rifrazione di Fresnel (Vetro) integrata con la rugosità GGX per effetti smerigliati.

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
