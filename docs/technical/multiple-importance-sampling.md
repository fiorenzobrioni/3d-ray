# Multiple Importance Sampling (MIS)

> **Vedi anche:** [Path Tracing e Illuminazione](./path-tracing-and-lighting.md) §2.2 per il
> contesto generale, [Shading Model](./shading-model.md) per i lobi BSDF
> coinvolti, [Pipeline di Rendering](./rendering-pipeline.md) per il flusso di
> dati attorno a `TraceRay`.

Questo documento descrive come 3D-Ray combina le diverse strategie di campionamento dell'illuminazione (light sampling / BSDF sampling / phase sampling) sotto l'estimatore Multiple Importance Sampling di Veach (1997). Copre il contratto fra le interfacce `IMaterial`, `ILight`, `IPhaseFunction` e il `Renderer`, le heuristiche disponibili (`balance`, `power`) e i casi limite: lobi delta, mixture stocastica, in-scattering volumetrico.

## 1. Il problema

Per un singolo hit di superficie con materiale non emissivo, l'illuminazione diretta è l'integrale:

$$L_d(p, \omega_o) = \int_{\Omega} f_r(p, \omega_i, \omega_o)\, L_i(p, \omega_i)\, |n \cdot \omega_i|\, d\omega_i$$

Due distribuzioni di campionamento sono naturali:

- **Light sampling (NEE)** — densità $p_L(\omega)$: si campiona un punto sulla luce e si valuta la BRDF per quella direzione. Buono quando la luce è piccola e la BRDF è larga (Lambertian sotto un sole).
- **BSDF sampling** — densità $p_B(\omega)$: si campiona dalla BRDF e si controlla se la direzione colpisce una luce. Buono quando la BRDF è stretta (specular su area light grande).

Usare solo una delle due lascia varianza ovunque l'altra sarebbe stata migliore. **MIS** combina entrambe in un unico estimatore:

$$\hat{L}_\text{mis} = \frac{w_L(\omega_L)\,f(\omega_L)\,L(\omega_L)\,|n\cdot\omega_L|}{p_L(\omega_L)} + \frac{w_B(\omega_B)\,f(\omega_B)\,L(\omega_B)\,|n\cdot\omega_B|}{p_B(\omega_B)}$$

con pesi $w_L + w_B = 1$ che soddisfano $w_X(\omega) = 0$ quando $p_X(\omega) = 0$.

## 2. Le heuristiche

3D-Ray supporta due varianti, selezionabili a runtime via `--mis <balance|power>`:

| Heuristica | Formula | Quando preferire |
|---|---|---|
| **Balance** (default, Veach §9.2.2) | $w_X = \dfrac{p_X}{p_X + p_Y}$ | Default sicuro: minimizza la varianza fra le combinazioni unbiased single-sample. |
| **Power** β=2 (Veach §9.2.4) | $w_X = \dfrac{p_X^2}{p_X^2 + p_Y^2}$ | PDF molto asimmetriche (luce puntiforme + materiale ruvido, sole nella nebbia). Sopprime più aggressivamente lo sampler peggio adattato. |

Entrambe sono **unbiased** e hanno costo computazionale identico (due moltiplicazioni in più per la power). L'helper `Renderer.MisWeight(p, q)` dispatcha sull'enum `MisHeuristic` e centralizza tutti i punti di calcolo:

```csharp
private float MisWeight(float p, float q)
{
    if (_misHeuristic == MisHeuristic.Power)
    {
        float p2 = p * p, q2 = q * q;
        return p2 / (p2 + q2 + 1e-30f);
    }
    return p / (p + q + 1e-30f);
}
```

Il termine `1e-30f` evita 0/0 quando entrambe le PDF sono nulle (caso che il chiamante filtra comunque a monte).

## 3. Contratto fra interfacce

### 3.1 `IMaterial` — la tripla simmetrica

```csharp
Vector3 Evaluate(Vector3 V, Vector3 L, in HitRecord rec);   // BSDF senza coseno
float   Pdf     (Vector3 V, Vector3 L, in HitRecord rec);   // PDF in solid angle
BsdfSample? Sample(Vector3 V, in HitRecord rec);            // (Wo, F, Pdf, IsDelta, Transition, ...)
```

- `Evaluate` ritorna $f(V, L)$ **senza** il coseno $|n \cdot L|$: il chiamante moltiplica. È l'unica descrizione del materiale: la NEE pesa ogni campione di luce non in ombra con `Evaluate(V, L) × |N.L|` (`Renderer.DirectIntegrand`), moltiplicato per il fattore di shadow terminator sul lato della luce (Chiang et al. 2019; lo stesso fattore entra nel peso del rimbalzo BSDF), e il rimbalzo BSDF stima la stessa funzione. Non esistono un `Scatter()` né un `EvaluateDirect()` separati.
- `Pdf` è la densità in solid angle del sampler proprio del materiale, valutata nella direzione data, senza i lobi delta. Integra alla probabilità di un campione non-delta (1 per un materiale senza lobi delta) sulla regione campionabile.
- `Sample` produce una direzione `Wo` e riporta `F` (BSDF), `Pdf` (densità), `IsDelta` (true per Dirac), `Transition` (`Enter`/`Exit` sulle rifrazioni, per la pila dei mezzi) e `NextSegmentAbsorption` (Beer-Lambert del segmento successivo). Per un sample non-delta `F` e `Pdf` coincidono con `Evaluate` e `Pdf` in `Wo`. Ritorna null quando il cammino è assorbito: non c'è fallback.

Il renderer pesa un sample non-delta con `F × |N.Wo| × terminator / Pdf` e un sample delta con `F` soltanto.

Implementanti:

| Materiale | Sample/Pdf/Evaluate | Note |
|---|---|---|
| `OpenPbr` (anche il tipo `disney`, tradotto da `DisneyMaterial.Create`) | ✅ miscela di lobi (metallo F82 + Kulla-Conty, interfaccia dielettrica GGX con trasmissione di Walter, diffuso EON, coat, fuzz, subsurface sottile) | Ogni lobo tiene solo le direzioni del proprio supporto; `F` e `Pdf` del sample sono quelli dell'intera miscela. La trasmissione rough è un lobo non-delta in `Evaluate`/`Pdf`; trasmissione e riflessione lisce sono delta. VNDF (Heitz 2018) per i lobi GGX. |
| `Lambertian` | ✅ cosine-hemisphere | $f = \rho/\pi$, $p = \cos\theta/\pi$. |
| `Metal` | ✅ GGX VNDF + lobo di scattering multiplo (Kulla-Conty) | `Pdf` è la miscela dei due sampler; `fuzz=0` → delta. |
| `Dielectric` | ✅ liscio: due lobi delta; rough: Walter 2007 | Liscio: Fresnel sceglie riflessione o rifrazione, `IsDeltaScatter = true`. Rough: riflessione e trasmissione microfacet non-delta, con il fattore di radianza $(\eta_i/\eta_t)^2$ in rifrazione. |
| `MixMaterial` | ✅ blend lineare in `Pdf`/`Evaluate`, one-sample mixture in `Sample` | Vedi §5.3. |
| `Emissive` | default dell'interfaccia | `Evaluate`/`Pdf` nulli, `Sample` ritorna null; `NeedsDirectLighting = false`. |

### 3.2 `ILight` — campionabilità da NEE

```csharp
bool   IsDelta { get; }                                  // true per point/dir/spot
float  PdfSolidAngle(Vector3 hitPoint, Vector3 wi);      // 0 per delta lights
```

| Luce | `IsDelta` | `PdfSolidAngle` |
|---|---|---|
| `PointLight`, `DirectionalLight`, `SpotLight` | `true` | 0 (irraggiungibili da BSDF sampling). |
| `AreaLight` | `false` | $\dfrac{r^2}{A \cos\theta_\text{light}}$ (PBRT §14.2.4). |
| `SphereLight` | `false` | $\dfrac{1}{2\pi(1-\cos\theta_\text{max})}$ (uniform cone sampling). |
| `GeometryLight` | `false` | Delegato a `ISolidAngleSamplable` (sphere) o area-sampling fallback. |
| `EnvironmentLight` | `false` | Delegato a `SkySettings.PdfSolidAngle` (CDF 2D per HDRI o gradient analitico). |

### 3.3 `IPhaseFunction` — analogo per i media

```csharp
float Evaluate(Vector3 wo, Vector3 wi);
(Vector3 Wi, float Pdf) Sample(Vector3 wo);
float Pdf(Vector3 wo, Vector3 wi) => Evaluate(wo, wi);   // default
```

Per le 5 phase function fornite (Isotropic, Henyey-Greenstein, double-HG, Rayleigh, Schlick) il sampler è esatto: il PDF della direzione campionata coincide con `Evaluate(wo, wi)`. Il default dell'interfaccia evita boilerplate; un'implementazione futura con sampler approssimato dovrà fare override.

## 4. Punti di applicazione nel `Renderer`

Il `Renderer` applica MIS in quattro punti:

1. **`ComputeDirectLighting`** (surface NEE). Per ogni shadow ray non in ombra, contributo `wNee × lightColor × Evaluate × |N.L| × terminator × Tr`, con `wNee = MisWeight(pPick × pLight, pBsdf)` se la luce è non-delta e `material.Pdf > 0` in quella direzione (altrimenti 1).
2. **`WeightEmission`** (BSDF-hit-emitter). Quando un raggio BSDF non-delta colpisce un emitter NEE-registrato, peso `wBsdf = MisWeight(prevBsdfPdf, pLight)`. Per `prevIsDelta = true` (raggio di camera o lobo delta) e per gli emettitori non registrati il peso è 1: nessun campione di luce li ha contati.
3. **`SampleSky`** (BSDF-escape-into-sky). Quando un raggio BSDF esce dalla scena verso un environment campionabile, peso `wBsdf = MisWeight(prevBsdfPdf, pEnv)`.
4. **`ComputeDirectLightingMedium`** (volumetric NEE). Analogo a (1) ma con la phase function al posto della BRDF e `phasePdf` al posto di `pBsdf`.

L'identificazione della "controparte" delle PDF si basa sui flag `ILight.IsDelta` (luci) e `BsdfSample.IsDelta` (il lobo effettivamente campionato); quando uno dei due lati è delta, il peso degenera a 1. `IMaterial.IsDeltaScatter` (vero solo se ogni lobo è delta) serve ad altro, per esempio a saltare il gather delle caustiche.

## 5. Casi limite

### 5.1 Lobi delta puri (specchio perfetto, vetro ideale)

Una distribuzione Dirac non ha densità solid-angle finita. Convenzione di 3D-Ray:

- `BsdfSample.IsDelta = true`, `Pdf = 1` (sentinel, non una vera densità); il peso del rimbalzo è `F` soltanto, senza coseno né pdf.
- Il `Renderer` usa `IsDelta` come marker: nel bounce successivo `prevBsdfPdf = 0`, `prevIsDelta = true`, e `WeightEmission` / `SampleSky` ritornano radianza a peso 1.
- NEE non può raggiungere questi lobi: `material.Pdf(V, L)` e `material.Evaluate(V, L)` li escludono, quindi il loro contributo al campione di luce è zero.

`Dielectric` liscio, `Metal` con `fuzz = 0` e i lobi lisci di `OpenPbr` (rugosità sotto la soglia delta, α ≤ 1e-3) rientrano in questa categoria. Un materiale con lobi delta e non-delta insieme (es. coat liscio su base diffusa) ha `IsDeltaScatter = false`: i suoi sample delta passano comunque a peso pieno, quelli non-delta con il peso MIS.

### 5.2 `Sample` nullo

Ogni materiale campiona con `Sample()`; l'API `Scatter()` e la modalità "NEE replaced emission" con `prevBsdfPdf = 0` sono state rimosse. Un `Sample()` che ritorna null (luce assorbita, direzione sotto la superficie, `Emissive`) termina il cammino in quel punto: la superficie contribuisce solo con emissione e NEE. Non c'è un secondo campionamento: un fallback su un'altra API, per una miscela il cui figlio scelto non campionava, estraeva un altro figlio e pesava male entrambi.

### 5.3 `MixMaterial` — mixture estimator

Per una miscela $f_\text{mix} = (1-t) f_A + t f_B$:

- **`Evaluate(V, L)`** ritorna la combinazione lineare dei figli pesata con $t$: $(1-t) f_A + t f_B$.
- **`Pdf(V, L)`** ritorna $(1-t) p_A(L) + t\, p_B(L)$. Questa è la densità **marginale** del sampler stocastico ed è quella richiesta dal MIS NEE.
- **`Sample(V)`** sceglie il figlio B con probabilità $t$ (A con $1-t$) e ne prende il sample. Se il sample è non-delta, riporta in quella direzione la `F` e la `Pdf` **dell'intera miscela** ($f_\text{mix}$, $p_\text{mix}$): la densità marginale della procedura è proprio $p_\text{mix}$, quindi `F × cos / Pdf` è lo stimatore giusto e la pdf coincide con quella che la NEE interroga (PBRT v4, *one-sample model*). Riportare la pdf del solo figlio scelto introdurrebbe bias quando le due pdf differiscono.

Se il sample del figlio scelto è delta, viene restituito così com'è (direzione, `F`, `IsDelta = true`; solo la pdf sentinella è scalata per la probabilità di scelta). Il peso del rimbalzo è `F`: poiché il figlio è stato scelto con probabilità pari al suo peso nella miscela, la media è esattamente la parte delta di $(1-t) f_A + t f_B$. Il bounce successivo vede `prevIsDelta = true` e l'emissione passa a peso pieno.

### 5.4 Sample scartati sotto la superficie

`Metal`, `Dielectric` rough e i lobi GGX di `OpenPbr` campionano le normali visibili (VNDF, Heitz 2018); una riflessione su una microfaccetta può comunque finire sotto la superficie, e quel sample viene scartato (`null`). La `Pdf` analitica riporta la densità del sampler includendo quella massa, quindi integra a meno di 1 sulle direzioni accettate. Il MIS resta unbiased: lo stimatore `F × cos / Pdf` usa la stessa densità con cui la direzione è stata davvero estratta, e la NEE usa la stessa `Pdf` per il peso. Il kit statistico (`BsdfTestKit`) confronta per questo il chi-quadro come distribuzione condizionata sul supporto e verifica la normalizzazione con l'albedo.

### 5.5 Phase function in volumi

Il bounce volumetrico thread `prevBsdfPdf = phasePdf` come per le superfici. Combinato con la trasmittanza Beer-Lambert applicata alle shadow ray (`_globalMedium.Transmittance` in `ComputeDirectLighting` / `ComputeDirectLightingMedium`), questo dà MIS pieno fra phase-sampling e light-sampling per l'in-scattering. La riduzione di rumore è particolarmente visibile sulle scene "god ray" (sole forte attraverso fog forward-scattering).

## 6. Verifica

La suite `MisMaterialsTests` (`src/RayTracer.Tests/MisMaterialsTests.cs`) copre:

- **PDF integrate-to-1** — Lambertian, le 5 phase function. Tolleranza ±5% su 65k campioni uniformi.
- **Sample / Pdf / Evaluate consistency** — Lambertian e Metal (rough): la `BsdfSample.F` e `BsdfSample.Pdf` riportate da `Sample` devono coincidere con `Evaluate(V, Wo)` e `Pdf(V, Wo)` (entro 1e-3).
- **MixMaterial linearity** — `Pdf_mix == (1-t)·Pdf_A + t·Pdf_B` analiticamente, su 5 valori di blend.
- **Delta-mirror invariants** — `Metal` con `fuzz = 0`: `Sample` ritorna `IsDelta = true, Pdf = 1`, mentre `Pdf(V, L) = 0` per ogni `L`.

Le scene `scenes/review/white-furnace-classic.yaml` (`lambertian`, `metal`, `dielectric`) e `scenes/review/openpbr-white-furnace.yaml` (le configurazioni OpenPBR della specifica) sono banchi diagnostici di conservazione di energia: una sfera di albedo 1 in un environment di luminanza 1 deve restituire un'immagine in cui la sfera è indistinguibile dallo sfondo, a meno di rumore Monte Carlo (il vetro ruvido perde circa il 2% per lo scattering singolo dell'interfaccia). I gate statistici per materiale sono in `src/RayTracer.Tests/Validation/` (vedi [Testing](./testing.md) §4.3).

## 7. Riferimenti

- Eric Veach. *Robust Monte Carlo Methods for Light Transport Simulation*. PhD thesis, Stanford, 1997. §9.2 (heuristiche balance/power), §10.3 (path-space MIS).
- Matt Pharr, Wenzel Jakob, Greg Humphreys. *Physically Based Rendering: From Theory to Implementation*, 4th edition. §13.10 (one-sample MIS), §14.3 (bidirectional path tracing).
- Eric Heitz. *Sampling the GGX Distribution of Visible Normals*. JCGT 7(4), 2018. (VNDF di `Metal`, `Dielectric` rough e dei lobi GGX di `OpenPbr`.)
