# Modello di Shading e Materiali

Questa pagina descrive come i materiali di 3D-Ray rispondono alla luce: il nucleo microfacet comune, i materiali classici, il materiale a strati OpenPBR e la traduzione del tipo `disney` su di esso, l'interfaccia che il renderer usa. Il riferimento YAML dei parametri è in [`docs/reference/scene-reference.md`](../reference/scene-reference.md) §5.

---

## 1. I materiali

| Tipo YAML | Classe | Uso |
|---|---|---|
| `lambertian` | `Lambertian` | Diffuso ideale. |
| `metal` | `Metal` | Conduttore GGX: Fresnel complesso misurato (`preset`, `eta`/`k`) o F82 (`color` + `edge_color`), scattering multiplo. |
| `dielectric` | `Dielectric` | Vetro liscio (due lobi delta) o satinato (Walter 2007). |
| `emissive` | `Emissive` | Sorgente di luce di superficie, campionata dalla NEE. |
| `openpbr` | `OpenPbr` | Materiale a strati OpenPBR Surface v1.1: metallo, dielettrico, diffuso, vetro, subsurface, coat, fuzz, film sottile, emissione, opacità, thin-walled. |
| `disney` / `pbr` | `OpenPbr` (via `DisneyMaterial.Create`) | I parametri Disney tradotti su OpenPBR (§5). |
| `mix` | `MixMaterial` | Miscela di due materiali con peso costante o maschera. |

Ogni materiale conserva l'energia: con colori bianchi e senza assorbimento una sfera sotto un cielo bianco uniforme sparisce nello sfondo (scene `scenes/review/*white-furnace*.yaml`, misure nel loro README).

---

## 2. Il nucleo microfacet (`Microfacet.cs`)

Tutti i lobi lucidi usano lo stesso modello:

- **NDF GGX anisotropa**, normalizzata: $\int D(h)\,(h\cdot n)\,d\omega_h = 1$.
- **Smith correlato in altezza**: $G_2 = 1 / (1 + \Lambda(v) + \Lambda(l))$, con il $\Lambda$ della GGX. Soddisfa l'identità dell'area proiettata (test `MicrofacetTests`).
- **Campionamento delle normali visibili** (Heitz 2018): la direzione riflessa ha pdf $G_1(v)\,D(h) / (4\,v\cdot n)$. Le direzioni che finiscono sotto l'orizzonte sono scartate (il campione è perso, la pdf dichiarata resta esatta dove il lobo ha supporto).
- **Anisotropia** alla OpenPBR: con rugosità $r$ e anisotropia $a$, $\alpha_t = r^2 \sqrt{2 / (1 + (1-a)^2)}$, $\alpha_b = (1-a)\,\alpha_t$.

**Albedo direzionale tabulata.** Il singolo scattering perde la luce che rimbalza più volte fra le microfacette, e gli strati di OpenPBR pesano ciò che sta sotto un'interfaccia con l'albedo di quell'interfaccia. Le albedo sono integrate offline con una quadratura deterministica sulle normali visibili (nessun rumore Monte Carlo) e scritte in tabelle C#:

| Tabella | Generatore | Contenuto |
|---|---|---|
| `MicrofacetAlbedo` | `validation/gen_microfacet_albedo.py` | $E(\mu, \alpha)$ e $E_{avg}(\alpha)$ di un riflettore bianco isotropo |
| `AnisotropicAlbedo` | `validation/gen_aniso_albedo.py` | $E(\mu, \phi, r, a)$ di un riflettore bianco anisotropo e la sua media |
| `DielectricAlbedo` | `validation/gen_dielectric_albedo.py` | $E_R(\mu, \alpha, \eta)$ del lobo riflesso di un dielettrico (Fresnel esatto) e la media emisferica del Fresnel $E_F(\eta)$ |

Ogni tabella ha un test che la confronta con l'integrale diretto del BRDF (`OpenPbrLobeTests`).

**Soglia delta.** Sotto rugosità 0.0317 ($\alpha \le 10^{-3}$, un lobo più stretto di 0.06°) un lobo speculare è trattato come specchio perfetto: è campionato come delta, le caustiche a fotoni lo seguono e nessun raggio d'ombra incontra un lobo quasi singolare.

---

## 3. Metallo (`Metal.cs`, lobo metallico di OpenPBR)

- **Fresnel**: per un metallo misurato le equazioni esatte per l'indice complesso $n + ik$ (15 preset, riduzione RGB di dati spettrali misurati); altrimenti il modello F82 (Kutz 2021): la curva di Schlick dal colore a incidenza normale, abbassata verso $\mu = 1/7$ fino a `edge_color` per la depressione dei metalli reali.
- **Scattering multiplo** (Kulla e Conty 2017): un lobo $f_{ms} = F_{ms}\,(1 - E(\omega_o))(1 - E(\omega_i)) / (\pi\,(1 - E_{avg}))$ restituisce la luce che il singolo scattering perde, nel colore della media del Fresnel: un metallo bianco resta bianco a ogni rugosità. Per un lobo anisotropo $E$ viene dalla tabella anisotropa, letta a entrambe le direzioni: il lobo resta reciproco ed esatto.
- **Campionamento**: normali visibili con probabilità $E(\mu_o)$, coseno per il resto (la forma del lobo multiplo).

Validato contro un renderer di riferimento indipendente (`validation/`): `metal-presets` 1.0001, `metal-rough` 1.0032 (il residuo è la scelta del $G_2$ correlato in altezza; con il $G$ separabile il risultato è 1.0000).

---

## 4. Dielettrico (`Dielectric.cs`, interfaccia di OpenPBR)

- **Liscio**: Fresnel esatto non polarizzato, riflessione o rifrazione scelte con probabilità $F$, ciascuna un lobo delta. Riflessione totale interna senza consumare numeri casuali.
- **Ruvido** (Walter et al. 2007): riflessione GGX e trasmissione con il semivettore $h \propto v + \eta\,l$, pdf dalle normali visibili con lo jacobiano della rifrazione. Rispetta la reciprocità generalizzata $f(v \to l)/n_v^2 = f(l \to v)/n_l^2$.
- **Radianza alla rifrazione**: la radianza attraversando l'interfaccia scala di $(\eta_i/\eta_t)^2$ (si conserva $L/n^2$). Il photon tracer, che trasporta potenza, toglie il fattore.
- **Dielettrici annidati**: l'indice relativo viene dalla pila degli IOR del raggio (`IorStack`), quindi vino in un bicchiere rifrange contro il vetro, non contro l'aria.
- **NEE attraverso la superficie**: una superficie che trasmette in modo non delta (vetro satinato, diffusione thin-walled) riceve luce anche da dietro (`IMaterial.HasTransmission`).

Validato contro il renderer di riferimento: `glass-rough` 1.0013, `glass-shadow` 0.9995, `null-shell` 1.0001.

---

## 5. OpenPBR Surface (`OpenPbr.cs`)

Il modello della Academy Software Foundation, ridotto a una miscela di lobi come la specifica stessa deriva:

$$ f = \alpha \Big( F\,f_{fuzz} + (1 - F\,E_{fuzz})\,\big[\,C\,f_{coat} + \mathrm{lerp}(1, A\,(1 - E_{coat}), C)\;\Delta_C\; f_{base} \big] \Big) $$
$$ f_{base} = \mathrm{lerp}\big(\, f^R + T'\,f^T + D(\omega_o, \omega_i)\,f_{diff},\; f_{metal},\; M \big) $$

con $\alpha$ l'opacità, $F, C, M, T$ i pesi di fuzz, coat, metallicità e trasmissione, $A$ l'assorbimento del coat lungo i due cammini, $\Delta_C$ l'oscuramento del coat dovuto alle riflessioni interne.

| Lobo | Modello |
|---|---|
| $f_{metal}$ | §3, F82 da `base_color` e `specular_color`, scalato da `specular_weight` |
| $f^R$, $f^T$ | §4, interfaccia di `specular_ior`; `specular_weight` scala la riflettanza a incidenza normale cambiando l'indice; film sottile di Belcour e Barla sul Fresnel |
| $f_{diff}$ | EON (Portsmouth, Kutz e Hill 2024): Oren-Nayar di Fujii più un lobo multiplo che conserva l'energia; rugosità 0 è Lambert |
| $f_{coat}$ | riflessione GGX di `coat_ior`; un coat ruvido rende più ruvida la base |
| $f_{fuzz}$ | lo sheen a scattering multiplo di Zeltner, Burley e Chiang 2022 (coseno trasformato linearmente) |

**Strati.** Ciò che sta sotto un'interfaccia riceve solo la luce che l'interfaccia non riflette:

- **Diffuso sotto lo speculare**: peso reciproco $D = (1 - E_R(\omega_o))(1 - E_R(\omega_i)) / (1 - \bar E_R)$, esatto per una base lambertiana e simmetrico nelle due direzioni (la forma della specifica, $1 - E_R(\omega_o)$, non è reciproca).
- **Coat e fuzz**: la forma della specifica, con la sola albedo della direzione di vista. È quella che conserva l'energia sopra qualsiasi base (anche uno specchio); il prezzo è che questi due strati non sono reciproci. Il renderer usa le BSDF non delta solo nel verso della camera, quindi la non reciprocità non introduce errori nelle immagini.

**Trasmissione e subsurface.** $T' = T + (1 - T)\,S$: la luce del subsurface entra dalla stessa interfaccia del vetro e il random walk del renderer la porta dentro il mezzo che il loader lega all'oggetto (cammino libero medio `subsurface_radius` × `subsurface_radius_scale`, albedo di scattering singolo invertita perché il colore complessivo sia `subsurface_color`). Senza profondità `transmission_color` tinge la rifrazione all'ingresso; con `transmission_depth` è la trasmittanza a quella profondità (Beer-Lambert).

**Thin-walled.** Un foglio senza interno: la trasmissione è una lastra (dritta se liscia, il lobo riflesso specchiato attraverso il foglio se ruvida), il subsurface diventa riflessione e trasmissione diffuse ripartite da `subsurface_scatter_anisotropy`.

**Campionamento.** Un lobo è scelto con probabilità proporzionale alla sua albedo stimata; ogni lobo tiene solo le direzioni dentro il proprio supporto, così la pdf della miscela riportata in quel punto è esattamente la densità del campionatore. I lobi delta (coat liscio, base liscia) restituiscono il loro peso diviso per la probabilità di scelta.

**Verifiche** (`OpenPbrTests`, `OpenPbrLobeTests`, `DisneyBsdfTests`): fornace bianca in tutte le configurazioni elencate dalla specifica, chi-quadro del campionamento per ogni lobo, riduzioni esatte al `metal` e al vetro ruvido, reciprocità dove è dovuta.

---

## 6. Il tipo `disney` (`DisneyMaterial.cs`)

I parametri Disney sono tradotti su OpenPBR; il trasporto della luce è quello del §5.

| Disney | OpenPBR |
|---|---|
| `color`, `metallic`, `roughness` | `base_color`, `base_metalness`, `specular_roughness` |
| `specular` (0.5) | `specular_weight` = 2 × specular (Disney scala F0 di 2 × specular) |
| `specular_tint` | `specular_color` = lerp(1, tinta di `color`, tint) |
| `anisotropic`, `anisotropic_rotation` | `specular_roughness_anisotropy` = 0.9 a; la rotazione gira la tangente |
| `sheen`, `sheen_tint`, `sheen_roughness` | `fuzz_weight`, `fuzz_color`, `fuzz_roughness` |
| `clearcoat`, `clearcoat_gloss` | `coat_weight`; `coat_roughness` = $\sqrt{\alpha}$ con $\alpha$ = lerp(0.1, 0.001, gloss) se non è scritto |
| `spec_trans`, `ior` | `transmission_weight` (o `subsurface_weight` con il mezzo incorporato), `specular_ior` |
| `transmission_color` senza profondità | il colore al quadrato all'ingresso (Disney tingeva ogni rifrazione); senza colore, `color` |
| `diff_trans` | subsurface thin-walled con g = 2 × diff_trans − 1 |
| `thin_film_thickness` (nm) | `thin_film_weight` 1 e spessore in micrometri |

Il diffuso è Lambert. Le differenze di aspetto rispetto al vecchio modello sono elencate nel riferimento delle scene (§5.5) e mostrate in `scenes/review/images/disney-to-openpbr.jpg`.

---

## 7. Rumore e campioni

Il materiale OpenPBR sceglie un lobo per rimbalzo, ma riporta peso e pdf della miscela intera: un campione di qualsiasi lobo pesa $f / p$ con la stessa $f$, quindi la varianza dipende dalla forma della BSDF, non dal numero di lobi. Le superfici con un lobo delta e uno diffuso (plastica liscia, vernice con coat liscio) sono le più rumorose, perché il riflesso nitido dell'ambiente arriva solo dai campioni del lobo delta. Il denoiser dei preset `draft`, `standard` e `pre-final` copre bene questo caso; per i render finali `-q final` (1024 campioni) è il punto di partenza.

---

## 8. Normal mapping e shadow terminator

### 8.1 Matrice TBN
La normale di shading è perturbata in una base ortonormale locale (Tangent, Bitangent, Normal):
1. T e B sono derivati dalle coordinate UV della primitiva.
2. Gram-Schmidt li rende perpendicolari alla normale.
3. Su una faccia posteriore T e B sono invertiti, per non capovolgere i rilievi.

$$ n_{\text{world}} = \text{normalize}(T \cdot n_x + B \cdot n_y + N \cdot n_z) $$

### 8.2 Shadow terminator
Una normale perturbata può vedere luce dove la superficie vera è in ombra. Il fattore di Chiang (2019) sul lato della luce attenua quei contributi; fa parte dell'integrando, quindi pesa allo stesso modo il campione di luce della NEE e il campione della BSDF (§3.11 del piano di revisione). Dove la normale non è perturbata vale 1.

---

## 9. Interfaccia `IMaterial`

Il renderer consuma i materiali attraverso `IMaterial` (`src/RayTracer/Materials/IMaterial.cs`):

- `Evaluate(V, L, rec)`: la BSDF senza il coseno. È l'unica descrizione del materiale: la NEE pesa un campione di luce con `Evaluate` per il coseno $|\cos\theta_L|$, il rimbalzo indiretto campiona con `Sample`, il MIS combina le due strategie con `Pdf`. Le due stime riguardano quindi per costruzione la stessa funzione.
- `Pdf(V, L, rec)`: densità in angolo solido del campionatore, senza i lobi delta.
- `Sample(V, rec)`: direzione, valore, pdf, flag delta, transizione di mezzo; null significa luce assorbita.
- Query di trasmissione: `HasTransmission` (NEE da dietro), `TransmitsStraight` (attraversamento esatto per i raggi d'ombra), `ShadowTransmittance` e `ShadowAbsorption` (ombre trasparenti), `IsSpecularTransmissive` (caustiche a fotoni), `TryGetDielectricIor` (pila degli IOR).
- `Emit(...)`, `AovAlbedo(...)`, `NormalMap`, `BumpMap`, `Displacement`.
- `NeedsDirectLighting` (falso per `Emissive`) e `IsDeltaScatter` (vero quando ogni lobo è delta).

---

## Riferimenti

- Codice: `src/RayTracer/Materials/` (`OpenPbr.cs`, `DisneyMaterial.cs`, `Metal.cs`, `Dielectric.cs`, `Microfacet.cs`, `Eon.cs`, `FuzzLtc.cs`, tabelle `*Albedo*.cs`), `src/RayTracer/Rendering/Renderer.cs`.
- Academy Software Foundation (2024-2026). *OpenPBR Surface* v1.1.
- Walter, B. et al. (2007). *Microfacet Models for Refraction through Rough Surfaces*. EGSR.
- Heitz, E. (2014). *Understanding the Masking-Shadowing Function in Microfacet-Based BRDFs*. JCGT.
- Heitz, E. (2018). *Sampling the GGX Distribution of Visible Normals*. JCGT.
- Kulla, C. e Conty, A. (2017). *Revisiting Physically Based Shading at Imageworks*. SIGGRAPH Course.
- Kutz, P., Hasan, M. e Edmondson, P. (2021). Il modello F82 del Fresnel dei metalli, white paper.
- Portsmouth, J., Kutz, P. e Hill, S. (2024). *EON: A practical energy-preserving rough diffuse BRDF*.
- Zeltner, T., Burley, B. e Chiang, M. J.-Y. (2022). *Practical Multiple-Scattering Sheen Using Linearly Transformed Cosines*. SIGGRAPH Talks.
- Belcour, L. e Barla, P. (2017). *A Practical Extension to Microfacet Theory for the Modeling of Varying Iridescence*. SIGGRAPH.
- Burley, B. (2012, 2015). *Physically Based Shading at Disney* e *Extending the Disney BRDF to a BSDF with Integrated Subsurface Scattering*. SIGGRAPH Course Notes.
- Chiang, M. J.-Y., Li, Y. K. e Burley, B. (2019). *Taming the Shadow Terminator*. SIGGRAPH Talks.
- Veach, E. (1997). *Robust Monte Carlo Methods for Light Transport Simulation*. Tesi di dottorato.
