# Multiple Importance Sampling (MIS)

> **Vedi anche:** [Path Tracing e Illuminazione](./path-tracing-and-lighting.md) §2.2 per il
> contesto generale, [Shading Model](./shading-model.md) per i lobi BSDF
> coinvolti, [Pipeline di Rendering](./rendering-pipeline.md) per il flusso di
> dati attorno a `TraceRay`.

Questo documento descrive come 3D-Ray combina le diverse strategie di campionamento dell'illuminazione (light sampling / BSDF sampling / phase sampling) sotto l'estimatore Multiple Importance Sampling di Veach (1997). Copre il contratto fra le interfacce `IMaterial`, `ILight`, `IPhaseFunction` e il `Renderer`, le heuristiche disponibili (`balance`, `power`) e i casi limite — lobi delta, materiali legacy, mixture stocastica, in-scattering volumetrico.

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
Vector3 Evaluate(Vector3 V, Vector3 L, HitRecord rec);   // BRDF senza coseno
float   Pdf     (Vector3 V, Vector3 L, HitRecord rec);   // PDF in solid angle
BsdfSample? Sample(Vector3 V, HitRecord rec);            // (Wo, F, Pdf, IsDelta)
```

- `Evaluate` ritorna $f(V, L)$ **senza** il coseno $|n \cdot L|$ — il chiamante moltiplica.
- `Pdf` è la densità in solid angle del sampler proprio del materiale, valutata nella direzione data. Per i lobi non-delta deve integrare a 1 sulla regione campionabile.
- `Sample` produce una direzione `Wo` e riporta `F` (BRDF), `Pdf` (densità), `IsDelta` (true per Dirac), `NextSegmentAbsorption` (Beer-Lambert switch quando il sample è transmission).

Implementanti:

| Materiale | Sample/Pdf/Evaluate | Note |
|---|---|---|
| `DisneyBsdf` | ✅ multi-lobo (diffuse, specular GGX, sheen, clearcoat, multiscatter, diff_trans) | Transmission gestita come delta; VNDF Heitz 2018 per i lobi specular. |
| `Lambertian` | ✅ cosine-hemisphere | $f = \rho/\pi$, $p = \cos\theta/\pi$. |
| `Metal` | ✅ Cook-Torrance GGX | NDF importance sampling; `fuzz=0` → delta. |
| `MixMaterial` | ✅ blend lineare in `Pdf`/`Evaluate`, one-sample mixture in `Sample` | Vedi §5.3. |
| `Dielectric` | ❌ delta puro | Entrambi i lobi (riflessione + rifrazione) sono Dirac per costruzione fisica. `IsDeltaScatter = true`. |

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

1. **`ComputeDirectLighting`** (surface NEE) — riga ~795. Per ogni shadow ray non in ombra, peso `wNee = MisWeight(pLight, pBsdf)` se la luce è non-delta e il materiale espone `Pdf > 0`.
2. **`WeightEmission`** (BSDF-hit-emitter) — riga ~559. Quando un raggio BSDF colpisce un emitter NEE-registrato, peso `wBsdf = MisWeight(prevBsdfPdf, pLight)`. Per `prevIsDelta = true` il peso è 1 (i lobi delta non possono essere raggiunti da NEE).
3. **`SampleSky`** (BSDF-escape-into-sky) — riga ~687. Quando un raggio BSDF esce dalla scena verso un environment campionabile, peso `wBsdf = MisWeight(prevBsdfPdf, pEnv)`.
4. **`ComputeDirectLightingMedium`** (volumetric NEE) — riga ~890. Analogo a (1) ma con la phase function al posto della BRDF e `phasePdf` al posto di `pBsdf`.

L'identificazione della "controparte" delle PDF si basa sui flag `IsDelta` (luci) e `IsDeltaScatter` / `BsdfSample.IsDelta` (materiali); quando uno dei due lati è delta, il peso degenera a 1.

## 5. Casi limite

### 5.1 Lobi delta puri (specchio perfetto, vetro ideale)

Una distribuzione Dirac non ha densità solid-angle finita. Convenzione di 3D-Ray:

- `BsdfSample.IsDelta = true`, `Pdf = 1` (sentinel, non una vera densità).
- Il `Renderer` usa `IsDelta` come marker: nel bounce successivo `prevBsdfPdf = 0`, `prevIsDelta = true`, e `WeightEmission` / `SampleSky` ritornano radianza a peso 1.
- NEE non può raggiungere queste superfici perché `material.Pdf(V, L) = 0` per qualunque `L`; il peso `wNee` rimane 1 ma il contributo è zero (`material.EvaluateDirect` ritorna zero).

`Dielectric` e `Metal` con `fuzz = 0` rientrano in questa categoria.

### 5.2 Materiali legacy via `Scatter`

L'API legacy `Scatter()` resta esposta da `IMaterial` per compatibilità. Quando il `Renderer` non riceve un `BsdfSample` da `Sample()` (ritorno `null`), ricade su `Scatter()` con `prevBsdfPdf = 0` e `prevIsDelta = material.IsDeltaScatter`. Questo riproduce la vecchia semantica "NEE replaced emission" — è ancora unbiased, ma perde la varianza che il MIS recupererebbe.

A oggi tutti i materiali rilevanti (`DisneyBsdf`, `Lambertian`, `Metal`, `MixMaterial`) implementano `Sample()`; il fallback a `Scatter()` resta solo per la sicurezza di custom material di terze parti.

### 5.3 `MixMaterial` — mixture estimator

Per una miscela $f_\text{mix} = (1-t) f_A + t f_B$:

- **`Evaluate(V, L)`** ritorna la combinazione lineare diretta dei figli — nessuna sorpresa.
- **`Pdf(V, L)`** ritorna $(1-t) p_A(L) + t\, p_B(L)$. Questa è la densità **marginale** del sampler stocastico ed è quella richiesta dal MIS NEE.
- **`Sample(V)`** seleziona stocasticamente uno dei due figli con probabilità $t / (1-t)$ e ritorna direttamente il `BsdfSample` del figlio scelto. Il `Renderer` calcola `attenuation = F · cos / Pdf`; integrando sulla scelta stocastica si ottiene esattamente $\int (1-t) f_A + t f_B$ (PBRT v4 §9.5.3 *one-sample model*).

Un caso da tenere a mente: se uno dei figli è delta e l'altro no, e il sample selezionato è quello delta, il `BsdfSample` ritorna `IsDelta = true` e il `Renderer` lo tratta come un bounce delta singolo — il MIS di NEE nel bounce successivo continua a valutare `mix.Pdf` (combinazione del lobo non-delta), che è la quantità giusta per il peso, ma il contributo del lobo delta non viene MIS-pesato (è peso 1 by construction).

### 5.4 Metal NDF non-VNDF

`Metal` campiona la NDF GGX (non la sua versione *visible*, VNDF), quindi alcuni sample finiscono sotto la superficie e vengono scartati (`null` ritornato da `Sample()`). La PDF analitica `Metal.Pdf` non integra esattamente a 1 per `fuzz` alti — riportare la densità "lorda" del sampler. Il MIS resta unbiased perché l'estimatore `F · cos / Pdf` usa la **stessa** PDF al numeratore (in `BsdfSample.Pdf`) e al denominatore: il rapporto è corretto.

Migrare a VNDF (Heitz 2018, già usato da `DisneyBsdf`) è un'ottimizzazione di varianza ortogonale, non un fix MIS.

### 5.5 Phase function in volumi

Il bounce volumetrico thread `prevBsdfPdf = phasePdf` come per le superfici. Combinato con la trasmittanza Beer-Lambert applicata alle shadow ray (`_globalMedium.Transmittance` in `ComputeDirectLighting` / `ComputeDirectLightingMedium`), questo dà MIS pieno fra phase-sampling e light-sampling per l'in-scattering. La riduzione di rumore è particolarmente visibile sulle scene "god ray" (sole forte attraverso fog forward-scattering).

## 6. Verifica

La suite `MisMaterialsTests` (`src/RayTracer.Tests/MisMaterialsTests.cs`) copre:

- **PDF integrate-to-1** — Lambertian, le 5 phase function. Tolleranza ±5% su 65k campioni uniformi.
- **Sample / Pdf / Evaluate consistency** — Lambertian e Metal (rough): la `BsdfSample.F` e `BsdfSample.Pdf` riportate da `Sample` devono coincidere con `Evaluate(V, Wo)` e `Pdf(V, Wo)` (entro 1e-3).
- **MixMaterial linearity** — `Pdf_mix == (1-t)·Pdf_A + t·Pdf_B` analiticamente, su 5 valori di blend.
- **Delta-mirror invariants** — `Metal` con `fuzz = 0`: `Sample` ritorna `IsDelta = true, Pdf = 1`, mentre `Pdf(V, L) = 0` per ogni `L`.

Le scene `scenes/furnace-{lambert,metal,mix}.yaml` sono banchi diagnostici di conservazione di energia: una sfera di albedo 1 in un environment di luminanza 1 deve restituire un'immagine in cui la sfera è indistinguibile dallo sfondo, a meno di rumore Monte Carlo (e, per Metal, della perdita NDF discussa al §5.4).

## 7. Riferimenti

- Eric Veach. *Robust Monte Carlo Methods for Light Transport Simulation*. PhD thesis, Stanford, 1997. §9.2 (heuristiche balance/power), §10.3 (path-space MIS).
- Matt Pharr, Wenzel Jakob, Greg Humphreys. *Physically Based Rendering: From Theory to Implementation*, 4th edition. §13.10 (one-sample MIS), §14.3 (bidirectional path tracing).
- Eric Heitz. *Sampling the GGX Distribution of Visible Normals*. JCGT 7(4), 2018. (Per il VNDF di `DisneyBsdf`; futuro upgrade di `Metal`.)
