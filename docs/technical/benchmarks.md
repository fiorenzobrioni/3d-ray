# Benchmark — `RayTracer.Benchmarks`

Progetto `BenchmarkDotNet` dedicato ai micro-benchmark dei componenti *hot-path* del renderer. Vive in `src/RayTracer.Benchmarks/` ed è indipendente dalla CLI principale: serve solo a misurare e confrontare varianti di codice, non partecipa alla pipeline di build del motore.

## 1. Perché esiste

Senza numeri riproducibili ogni ottimizzazione è una congettura. Gli interventi su BVH e AABB (Phase 2-4 della review) si appoggiano a due benchmark sintetici:

- **`AabbBenchmarks`** — misura il costo puro dello slab test `AABB.Hit`. È il test più frequente del renderer: un raggio primario ne esegue decine o centinaia per pixel.
- **`BvhBenchmarks`** — misura il costo combinato di traversal BVH + intersezione `Sphere`, su nuvole di sfere di dimensioni diverse (100 / 1.000 / 10.000). Rappresenta una scena "intersection-bound" tipica.

Entrambi usano seed deterministici (`Random(42)`) → i risultati sono comparabili run-to-run.

## 2. Struttura del progetto

```
src/RayTracer.Benchmarks/
├── RayTracer.Benchmarks.csproj   # BenchmarkDotNet 0.14, net10.0, ProjectReference a RayTracer
├── Program.cs                    # entry point: BenchmarkSwitcher.FromAssembly(...).Run(args)
├── AabbBenchmarks.cs             # [Benchmark] HitSweep su 1024 raggi
└── BvhBenchmarks.cs              # [Benchmark] HitSweep su N sfere × 1024 raggi
```

Il progetto **non è incluso** nel path di build CI (`src/RayTracer/RayTracer.csproj`), quindi non rallenta la pipeline principale. È comunque presente nella soluzione `3d-ray.slnx` per comodità in IDE.

## 3. Eseguire i benchmark

> ⚠️ BenchmarkDotNet richiede la configurazione **Release**, altrimenti rifiuta di eseguire (il JIT Debug introduce troppo rumore).

### 3.1 Tutti i benchmark

```bash
dotnet run -c Release --project src/RayTracer.Benchmarks -- --filter '*'
```

### 3.2 Un singolo benchmark per nome

```bash
# Solo AABB
dotnet run -c Release --project src/RayTracer.Benchmarks -- --filter '*Aabb*'

# Solo BVH
dotnet run -c Release --project src/RayTracer.Benchmarks -- --filter '*Bvh*'
```

### 3.3 Modalità "quick" (meno iteration, utile in sviluppo)

```bash
dotnet run -c Release --project src/RayTracer.Benchmarks -- --filter '*Aabb*' --job short
```

`--job short` riduce warm-up e misurazioni; il risultato è rumoroso ma arriva in ~30 s invece di ~2-3 min.

### 3.4 Parametri disponibili

I benchmark usano `[Params(...)]` per generare varianti:

| Classe | Parametro | Valori |
|---|---|---|
| `AabbBenchmarks` | `RayCount` | 1024 |
| `BvhBenchmarks` | `SphereCount` | 100, 1000, 10000 |
| `BvhBenchmarks` | `RayCount` | 1024 |

Passare `--list flat` per vedere l'elenco completo generato.

## 4. Esempio di output

Output tipico su una macchina sviluppo (Ryzen 5950X, .NET 10, Release):

```
BenchmarkDotNet v0.14.0, Linux Ubuntu 22.04
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0, X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.0, X64 RyuJIT AVX2

| Method   | SphereCount | RayCount | Mean       | Error    | StdDev   | Allocated |
|--------- |------------ |--------- |-----------:|---------:|---------:|----------:|
| HitSweep | 100         | 1024     |   112.4 us |  0.21 us |  0.20 us |       - |
| HitSweep | 1000        | 1024     |   312.8 us |  0.82 us |  0.77 us |       - |
| HitSweep | 10000       | 1024     |   548.1 us |  1.14 us |  1.06 us |       - |
```

Osservazioni attese:

- La crescita è **sub-lineare** in `SphereCount` (da 100 a 10.000 → 100× di primitive, tempo solo ~5×) — è il cuore del beneficio O(log N) del BVH.
- `Allocated = 0` deve restare tale: il hot path non alloca. Se compare un numero > 0 è una regressione.

Per `AabbBenchmarks` l'output è una singola riga: 1024 slab test completano tipicamente in ~2-4 µs, < 4 ns per chiamata.

## 5. Interpretare i risultati

### Confrontare due implementazioni

BenchmarkDotNet salva i run in `BenchmarkDotNet.Artifacts/`. Per confrontare due branch:

```bash
# Sul branch "baseline"
dotnet run -c Release --project src/RayTracer.Benchmarks -- --filter '*Bvh*' --exporters json
mv BenchmarkDotNet.Artifacts/results ./baseline-results

# Sul branch "perf-fix"
dotnet run -c Release --project src/RayTracer.Benchmarks -- --filter '*Bvh*' --exporters json
# Confronto manuale dei JSON o tramite strumento esterno
```

Per confronti più seri esistono tool come `ResultsComparer` (samples BenchmarkDotNet) o `benchmark-compare`.

### Metriche chiave

- **Mean**: media temporale. È la metrica principale.
- **Error** (IC 99.9 %): se è grande rispetto a `Mean`, i numeri sono inaffidabili — aumentare `--job medium/long`.
- **StdDev**: deviazione standard delle iterazioni.
- **Allocated**: bytes allocati. Qualsiasi regressione da 0 è da investigare.

## 6. Aggiungere un nuovo benchmark

1. Creare una classe `Xyz Benchmarks.cs` in `src/RayTracer.Benchmarks/`.
2. Annotare i metodi pubblici con `[Benchmark]`.
3. Usare `[GlobalSetup]` per setup costosi (costruzione scena, popolamento array) — non entra nel tempo misurato.
4. Evitare allocazioni nel metodo `[Benchmark]`. Usare campi pre-popolati.
5. `dotnet run -c Release --project src/RayTracer.Benchmarks -- --filter '*Xyz*'` per eseguirlo.

### Esempio minimo

```csharp
using BenchmarkDotNet.Attributes;

namespace RayTracer.Benchmarks;

[MemoryDiagnoser]
public class MyComponentBenchmarks
{
    private MyComponent _subject = null!;

    [Params(64, 1024)]
    public int N;

    [GlobalSetup]
    public void Setup() => _subject = MyComponent.Build(N);

    [Benchmark]
    public int Operation() => _subject.DoWork();
}
```

## 7. Limiti e caveat

- I benchmark sono **sintetici**: nuvole di sfere random, non scene realistiche. Validano la *forma* della curva di scalabilità ma non il comportamento end-to-end del renderer.
- La costruzione del BVH (che dalla Phase 4 è parallela) **non** è benchmarkata dalle classi attuali — solo il traversal. Un futuro `BvhBuildBenchmarks` è un buon candidato.
- Il path `Random.NextDouble()` non è thread-safe, ma qui è usato solo in `Setup`, quindi è innocuo.
- I numeri variano tra macchine: non confrontare assoluti tra host diversi, sempre confronti su stesso host / stessa configurazione.

## 8. Riferimenti

- [BenchmarkDotNet docs](https://benchmarkdotnet.org/)
- [PerfView](https://github.com/microsoft/perfview) per profiling complementare (ETW).
- `docs/technical/acceleration-structures.md` — teoria del BVH misurato qui.
