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
└── BvhEquivalenceTests.cs    # BvhNode.Hit vs HittableList.Hit
```

Il progetto **non è incluso** nel path di build CI principale (`src/RayTracer/RayTracer.csproj`), ma è presente nella soluzione `3d-ray.slnx`. Un hook CI dedicato (es. `dotnet test src/RayTracer.Tests/`) è il prossimo passo naturale.

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
- Intersezione `Torus` (risolutore quartico). Non testato unit — validato solo dalle scene.
- CSG: operazioni booleane su primitive. Idealmente equivalenza CSG vs intersezione esplicita su casi canonici.
- Materiali: Lambertian / Metal / Dielectric sampling distributions (test statistici).

## 9. Riferimenti

- [xUnit v2 docs](https://xunit.net/)
- `docs/technical/acceleration-structures.md` — algoritmi BVH testati qui.
- `docs/technical/benchmarks.md` — misurazione performance complementare.
