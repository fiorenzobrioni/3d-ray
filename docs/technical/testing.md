# Testing — `RayTracer.Tests`

Progetto xUnit dedicato ai test di correttezza dei componenti critici. Vive in `src/RayTracer.Tests/` ed è indipendente dal motore principale e dai benchmark. La priorità è **equivalenza algoritmica**: i componenti ottimizzati devono produrre lo stesso risultato delle implementazioni di riferimento.

## 1. Perché esiste

Il renderer ha due componenti *accelerati* le cui ottimizzazioni potrebbero introdurre regressioni silenziose — un pixel sbagliato in un angolo della scena è difficile da notare a occhio:

- **`AABB.Hit`** — slab test Kay-Kajiya vettorizzato (`Vector3.Min/Max`) con `InvDirection` precomputato. Deve comportarsi come lo slab test scalare classico, anche nei casi degeneri (raggi assi-allineati → `1/0 = ±∞`).
- **`BvhNode.Hit`** — BVH con SAH binning, fat leaves e ordered traversal. Deve produrre lo stesso `(hit, rec.T)` della fallback lineare `HittableList.Hit` su ogni raggio.

La regola di test è: **differenziale vs oracolo**. Non asseriamo valori numerici precisi — asseriamo uguaglianza con un'implementazione di riferimento semplice e manifestamente corretta.

## 2. Struttura del progetto

```
src/RayTracer.Tests/
├── RayTracer.Tests.csproj    # xunit 2.9.2, Microsoft.NET.Test.Sdk 17.11.1, net10.0
├── AabbTests.cs              # Differential test AABB.Hit vs slab scalare
├── BvhEquivalenceTests.cs    # BvhNode.Hit vs HittableList.Hit
├── ...                       # geometria, materiali, luci, volumi, rendering (circa 50 file)
├── TestScenes/               # scene YAML usate dai test di regressione
└── Validation/               # correttezza fisica (§4.3)
```

Il progetto non è referenziato da `src/RayTracer/RayTracer.csproj`, ma è presente nella soluzione `3d-ray.slnx` e la CI (`.github/workflows/ci.yml`) lo esegue con `dotnet test` su Linux, Windows e macOS.

## 3. Eseguire i test

### 3.1 Tutti i test

```bash
dotnet test src/RayTracer.Tests/RayTracer.Tests.csproj
```

### 3.2 Una singola classe di test

```bash
dotnet test src/RayTracer.Tests/RayTracer.Tests.csproj --filter "FullyQualifiedName~BvhEquivalenceTests"
```

### 3.3 Un singolo test `[Fact]`

```bash
dotnet test src/RayTracer.Tests/RayTracer.Tests.csproj \
  --filter "FullyQualifiedName=RayTracer.Tests.AabbTests.Hit_AxisAlignedRay_BehaviourMatchesReference"
```

### 3.4 Output verboso

```bash
dotnet test src/RayTracer.Tests/RayTracer.Tests.csproj --logger "console;verbosity=detailed"
```

## 4. Panoramica dei test

### 4.1 `AabbTests.cs`

Contiene una versione scalare `ReferenceHit(box, ray, tMin, tMax)` — trascrizione diretta dello slab test "libro di testo" — usata come oracolo.

| Test | Cosa verifica |
|---|---|
| `Hit_DirectFrontRay_Accepts` | Raggio frontale colpisce il box unitario. |
| `Hit_BehindBox_Rejects` | Raggio che punta lontano dal box non lo colpisce. |
| `Hit_RayFromInsideBox_Accepts` | L'origine dentro al box → hit (tMin=0). |
| `Hit_AxisAlignedRay_BehaviourMatchesReference` | 200 raggi asse-allineati (direction = X/Y/Z) → `InvDirection` ha componenti `±∞`: l'implementazione SIMD deve comportarsi come il reference. |
| `Hit_RayInAFacePlane_IsConservative` | Raggio asse-allineato con l'origine esattamente sul piano di una faccia: su quell'asse lo slab vale `0 * ∞ = NaN`, che non deve scartare il box (un nodo BVH perderebbe tutto il sottoalbero). Le riduzioni usano `float.MaxNumber`/`MinNumber`, che ignorano il NaN. |
| `Hit_GeneralRays_BehaviourMatchesReference` | 500 raggi random normalizzati → equivalenza totale con il reference. |
| `Ray_InvDirection_IsComponentwiseReciprocal` | `new Ray(o, d).InvDirection == (1/d.X, 1/d.Y, 1/d.Z)`. |

### 4.2 `BvhEquivalenceTests.cs`

Ogni test costruisce due scene con le **stesse primitive** — una usa `HittableList` (fallback lineare), l'altra usa `BvhNode`. Per ogni raggio:

1.  Entrambe ritornano lo stesso `(hit, miss)`.
2.  Se hit, entrambe ritornano lo stesso `rec.T` entro `1e-4`.

| Test | Primitivi | Focus |
|---|---|---|
| `Bvh_Matches_LinearList(count, seed)` × 8 | 1/2/3/4/5/20/200/2000 sfere | Copre i boundary `span == 1`, `span == MaxPrimitivesPerLeaf (4)`, primo split interno, scene grandi. |
| `Bvh_Matches_LinearList_AxisAlignedRays` | 50 sfere | Raggi con direzione asse-allineata → slab con `±∞`. |
| `Bvh_Matches_LinearList_ClusteredPrimitives` | 16 sfere stesso centro | Caso degenere: tutti i centroidi coincidono → fallback median split. |
| `Bvh_Matches_LinearList_RayStartingInsideAABB` | 30 sfere | Raggio che parte dentro il box totale — il top-slab non deve culling. |

Una modifica al BVH che rompe uno di questi test sta introducendo una regressione di correttezza.

### 4.3 `Validation/`: correttezza fisica

Test che verificano che il motore converga al valore **giusto**, non solo che non si rompa. Girano
con `--reference` (lo stimatore senza bias: niente clamp, niente ombre trasparenti "dritte", RR a
soglie fisse).

| File | Cosa verifica |
|---|---|
| `BsdfTestKit.cs` | Strumenti statistici per ogni materiale con API `Sample`/`Evaluate`/`Pdf`: chi-quadro di Pearson (le direzioni campionate seguono la `Pdf` dichiarata), integrale della `Pdf` sulla sfera, albedo direzionale (white furnace), confronto tra albedo campionato e integrale di `Evaluate * cos` per quadratura (il peso del campione è lo stimatore giusto), coerenza di `F` e `Pdf` del campione con `Evaluate` e `Pdf`, reciprocità. Il peso controllato è quello del renderer: `F * |cos| / Pdf` per un campione non-delta, `F` per un campione delta. |
| `BsdfStatisticalTests.cs` | I gate costruiti sul kit per il catalogo `BsdfCatalog.cs`. I test con `Skip` riproducono bug noti e citano la sezione di `PIANO-REVIEW.md` che li corregge: si riattivano con la correzione. |
| `BsdfAuditTests.cs` | Report completo del kit, opt-in: `RAYTRACER_AUDIT=1 dotnet test --filter "Category=Audit" --logger "console;verbosity=detailed"` (`RAYTRACER_AUDIT_ONLY=<nome>` aggiunge le celle peggiori del chi-quadro). |
| `AnalyticSceneTests.cs` | Scene con soluzione esatta: sfera lambertiana in ambiente uniforme (= albedo), sfera di vetro liscio, specchio e dielettrici annidati (= 1), radianza dentro una sfera di vetro chiaro scalata di $\eta^2$ e fornace della stessa sfera vista da fuori (il fattore torna indietro all'uscita), per `dielectric` e per il `disney` liscio, lastra unitaria di vetro OpenPBR a indice 1 con trasmittanza esatta $C^{1/D}$ (`transmission_color` $C$ a `transmission_depth` $D$, Beer-Lambert), piano sotto luce puntiforme (legge dell'inverso del quadrato). |
| `MicrofacetTests.cs` | Il nucleo GGX di tutti i lobi glossy: NDF normalizzata, identità dell'area proiettata con il termine di masking di Smith, il campionamento delle normali visibili estrae la densità promessa, G2 correlato in altezza fra la versione non correlata e il minimo. |
| `MetalTests.cs` | Il conduttore: Fresnel complesso dei preset contro valori di riferimento, proprietà del modello F82 (Schlick con tinta bianca, la tinta raggiunta a 82°), medie di Fresnel contro quadratura, un metallo bianco riflette tutta la luce a ogni rugosità (lobo Kulla-Conty), tabella dell'albedo a scattering singolo contro l'integrale diretto. |
| `RoughDielectricTests.cs` | Il dielettrico ruvido (Walter 2007): riflessione e trasmissione campionate secondo la propria pdf, da fuori e da dentro (con riflessione totale), coerenti con `Evaluate`; al tendere a zero della rugosità la ripartizione è quella di Fresnel; reciprocità generalizzata della rifrazione. |
| `OpenPbrTests.cs` | Il materiale OpenPBR: fornace bianca in ogni configurazione elencata dalla specifica, chi-quadro e peso del campione per ogni lobo (anche da dentro), riduzioni esatte al conduttore (metalness 1) e al dielettrico ruvido (trasmissione 1) e a Lambert (specular 0), `specular_weight`, colore e oscuramento del coat, emissione. |
| `OpenPbrLobeTests.cs` | I lobi di OpenPBR contro il proprio integrale: EON (un bianco resta bianco a ogni rugosità, albedo in forma chiusa, Lambert a rugosità 0), fuzz LTC (il sampler estrae la densità del lobo, albedo minore di 1), tabelle di albedo del dielettrico e anisotropa contro l'integrale diretto della BRDF. |
| `ReferenceModeTests.cs` | `--reference` toglie il clamp e rende opachi i raggi d'ombra; `--seed 0` è bit-identico, un seed diverso dà un'immagine indipendente. |
| `SamplerQualityTests.cs` | Il sampler: integra funzioni 2D note (una gaussiana liscia e un disco, cioè un bordo d'ombra) su 512 pixel indipendenti e misura la pendenza dell'errore RMS rispetto al numero di campioni. Numeri indipendenti danno N^-0.5; una coppia stratificata in 2D N^-1.4 sul liscio e N^-0.75 sul bordo. Gate su `Sample2D` a varie profondità e sui blocchi di dimensioni per vertice (`Sampler.BeginVertex`). Report opt-in con `RAYTRACER_AUDIT=1` (anche correlazioni fra dimensioni). |
| `SamplingRoutineTests.cs` | Chi-quadro e integrale della pdf delle routine di base: sfera e emisfero uniformi, cono verso una sfera (anche piccolissima o lontana), le phase function dei volumi, il campionamento importance della HDRI (continuo dentro i texel, pdf coerente con `PdfDirection`), disco concentrico; base ortonormale entro 1e-5 anche vicino a -Z. |
| `ScaleInvarianceTests.cs` | Lo stesso angolo di stanza scalato a 10 µm e a 1 km, e spostato a 10 km dall'origine, deve dare la stessa luminanza media entro il 2%: gli offset dei raggi seguono la scena. |
| `SolverPrecisionTests.cs` | Risolutore quartico e di Sturm su polinomi con radici note; intersezione del toro contro un riferimento in doppia precisione (marcia fitta della funzione implicita più bisezione): nessun hit perso o inventato, errore su t sotto 1e-5. |
| `NoiseQualityTests.cs` | Perlin: intervallo, zeri sul reticolo, continuità di valore e gradiente attraverso le facce del reticolo (C1), isotropia del gradiente. Worley: F1 è 1-Lipschitz (nessuna cella vicina dimenticata). |
| `RayDifferentialAccuracyTests.cs` | Footprint delle texture: le derivate UV dalla proiezione sul piano tangente dei differenziali di camera contro la differenza finita ottenuta intersecando davvero i raggi ausiliari. Esatto sul piano; sulla sfera l'errore cala con il pixel (primo ordine). |
| `CameraModelTests.cs` | La camera: un punto del mondo proiettato in prospettiva cade sul pixel previsto (errore angolare < 2e-6 rad); i raggi di un pixel con lente sottile convergono sul piano di fuoco (entro 1e-5 della distanza) e campionano la lente in modo uniforme; la sfocatura di ogni profondità segue la formula della lente sottile. |
| `PhysicalCameraTests.cs` | Le chiavi fotografiche: `focal_length` + `sensor_width` danno il campo visivo, `f_stop` (con `meters_per_unit`) il diametro della pupilla; il diaframma a lamelle è campionato in modo uniforme (settori equiprobabili, distribuzione radiale ρ² nei triangoli); `aperture_ratio` stira il bokeh. |
| `PixelFilterTests.cs` | Filtri di ricostruzione: le posizioni campionate seguono il profilo (chi-quadro) e un bordo netto renderizzato dà, colonna per colonna, la risposta al gradino del filtro (box, Gaussiano, Blackman-Harris). |
| `ColorManagementTests.cs` | Curva sRGB a tratti; ACES 1.x contro OpenColorIO (24 colori, entro 1e-3); AgX e il look punchy contro un'implementazione indipendente; grigi neutri e monotoni in ogni vista; la vista `legacy` identica alla curva di prima. |
| `WhiteBalanceTests.cs` | Bilanciamento del bianco: 6504 K è l'identità, una luce della temperatura scelta diventa neutra (da 1900 a 9000 K), il luogo dei colori è continuo. |
| `DisplayQuantizerTests.cs` | Quantizzazione: il dithering conserva la media di un campo uniforme ed è deterministico; arrotondamento e 16 bit esatti agli estremi. |

Fuori da `Validation/`, due file coprono i materiali OpenPBR dal lato del caricamento e della traduzione:

| File | Cosa verifica |
|---|---|
| `OpenPbrLoaderTests.cs` | Il tipo `openpbr` in YAML: i default della specifica, ogni input come numero, colore o blocco texture, il mezzo del subsurface legato agli oggetti che usano il materiale, nessun interno per una superficie thin-walled. |
| `DisneyBsdfTests.cs` | Il tipo `disney` tradotto su `OpenPbr` (`DisneyMaterial.Create`): reciprocità, integrale della pdf, coerenza di `Sample` con `Evaluate`/`Pdf`, anisotropia, scattering multiplo del metallo, trasmissione (sigma di Beer-Lambert in entrata, nessun cambio di mezzo per il vetro sottile e thin-walled), `diff_trans`, coat, sheen, film sottile, vetro quasi delta. |

Il confronto con un renderer di riferimento esterno su scene complete vive fuori dalla suite, in
`validation/` (vedi `validation/README.md`), insieme allo strumento `src/Tools/ImageCompare`.

## 5. Pattern: test di equivalenza

La tecnica usata in `BvhEquivalenceTests` è riusabile per qualsiasi ottimizzazione di algoritmo geometrico:

```csharp
private static void AssertEquivalent(List<IHittable> primitives, IEnumerable<Ray> rays)
{
    var reference = new HittableList(new List<IHittable>(primitives));
    var optimized = new BvhNode(new List<IHittable>(primitives));

    foreach (var ray in rays)
    {
        var refRec = new HitRecord();
        var optRec = new HitRecord();
        bool refHit = reference.Hit(ray, 0.001f, 1e30f, ref refRec);
        bool optHit = optimized.Hit(ray, 0.001f, 1e30f, ref optRec);

        Assert.Equal(refHit, optHit);
        if (refHit)
            Assert.InRange(optRec.T, refRec.T - 1e-4f, refRec.T + 1e-4f);
    }
}
```

Punti chiave:

- **Copia della lista** passata ad entrambe le implementazioni: `new BvhNode` può mutare la lista in place durante la build (swap della partizione).
- **Tolleranza `1e-4`** su `rec.T`: float, reassociation, ordine di somma → micro-differenze inevitabili.
- **RNG seeded**: test riproducibili. Se fallisce, il seed è nella stack trace → replay esatto.

## 6. Aggiungere un nuovo test

1.  Creare un file `XyzTests.cs` in `src/RayTracer.Tests/`.
2.  Annotare i test con `[Fact]` o `[Theory]` + `[InlineData(...)]`.
3.  Usare `Assert.Equal` / `Assert.True` / `Assert.InRange` / `Assert.Throws<T>`.
4.  Se il test copre un algoritmo ottimizzato, preferire un **test di equivalenza** contro un reference scalare inline.
5.  `dotnet test` per eseguirlo.

### Esempio minimo

```csharp
using Xunit;

namespace RayTracer.Tests;

public class MyThingTests
{
    [Fact]
    public void Behaviour_IsAsExpected()
    {
        var subject = new MyThing(...);
        Assert.Equal(42, subject.Answer());
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(10, 55)]
    public void Fib_MatchesExpected(int n, int expected)
    {
        Assert.Equal(expected, Fib.Compute(n));
    }
}
```

## 7. Convenzioni di naming

I test seguono il pattern `MethodName_Condition_ExpectedOutcome`, familiare da xUnit e NUnit:

- `Hit_DirectFrontRay_Accepts`
- `Hit_BehindBox_Rejects`
- `Bvh_Matches_LinearList_ClusteredPrimitives`

I test che tirano RNG portano il seed nei parametri (`[InlineData(count, seed)]`) così i fallimenti sono replicabili deterministicamente.

## 8. Cosa NON viene testato (ancora)

Aree scoperte che sono candidate naturali per future espansioni:

- Build del BVH parallelo (`ParallelBuildSpanThreshold`): scene ≥ 10k primitive → ramo `Parallel.Invoke`. Attualmente copertura indiretta via `Bvh_Matches_LinearList(2000, …)`.
- `Transform.BoundingBox` cache (Phase 1): bbox cachato vs ricomputato. Test differenziale dopo rotazione/scala.
- CSG: operazioni booleane su primitive. Idealmente equivalenza CSG vs intersezione esplicita su casi canonici.
- Luci con il kit statistico di `Validation/` (oggi copre materiali, phase function e HDRI).
- Differenziali di raggio dopo riflessione e rifrazione (oggi solo i raggi di camera).

## 9. Riferimenti

- [xUnit v2 docs](https://xunit.net/)
- `docs/technical/acceleration-structures.md` — algoritmi BVH testati qui.
- `docs/technical/benchmarks.md` — misurazione performance complementare.
