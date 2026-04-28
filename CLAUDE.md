# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**3D-Ray** is a C#/.NET 10 path-tracing renderer. Scenes are described in YAML, parsed into a scene graph, rendered in parallel across CPU cores, and written out as PNG/JPEG/BMP via `SixLabors.ImageSharp`. README and most docs are in Italian; the engine and code are in English.

## Common Commands

All commands run from the repo root.

### Build
```
dotnet build src/RayTracer/RayTracer.csproj -c Release
```

### Render a scene (main entry point)
```
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- \
  -i scenes/<scene>.yaml -o renders/out.png -w 1920 -H 1080 -s 1024 -d 8 -S 4
```
Required: `-i`. See `src/RayTracer/Program.cs` `ShowHelp()` for the full CLI. Canonical quality profiles (Preview/Standard/Final) and the `-s`/`-d`/`-S`/`-C` trade-offs are in `docs/reference/rendering-profiles.md`.

Useful flags: `--list-cameras` (print cameras and exit), `-c <name|index>` (select camera).

### Tests (xUnit)
```
dotnet test src/RayTracer.Tests/RayTracer.Tests.csproj
dotnet test src/RayTracer.Tests/RayTracer.Tests.csproj --filter "FullyQualifiedName~BvhEquivalenceTests"
dotnet test src/RayTracer.Tests/RayTracer.Tests.csproj --filter "FullyQualifiedName=RayTracer.Tests.AabbTests.Hit_AxisAlignedRay_BehaviourMatchesReference"
```
The test project is **not** referenced by `RayTracer.csproj` and is not run by CI; it must be invoked explicitly. See `docs/technical/testing.md`.

### Benchmarks (BenchmarkDotNet)
BenchmarkDotNet refuses to run outside Release.
```
dotnet run -c Release --project src/RayTracer.Benchmarks -- --filter '*'
dotnet run -c Release --project src/RayTracer.Benchmarks -- --filter '*Bvh*' --job short
```

### Tools
```
dotnet run --project src/Tools/TextureGen/TextureGen.csproj
dotnet run --project src/Tools/NormalMapGen/NormalMapGen.csproj
dotnet run --project src/Tools/ChessGen/ChessGen.csproj
```

### CI
`.github/workflows/dotnet.yml` builds Release and runs a 320×213 smoke render of `scenes/chess.yaml`. `.github/workflows/render-scenes.yml` is a `workflow_dispatch` matrix render at 1920×1080 — enable scenes by uncommenting entries in its `matrix.scene:` list.

## Architecture

### Solution layout
`3d-ray.slnx` groups three engine projects (`src/RayTracer`, `src/RayTracer.Tests`, `src/RayTracer.Benchmarks`) and the tools (`src/Tools/{TextureGen,NormalMapGen,ChessGen}`). Only `RayTracer.csproj` is built by CI; tests and benchmarks are opt-in.

### Rendering pipeline (YAML → pixel)
`Program.cs` → `SceneLoader.Load()` → `Renderer(..).Render(w,h)` → `SaveImage()`. The detailed contract between stages is in `docs/technical/rendering-pipeline.md`. Key invariants that span multiple files:

- **Scene analysis happens in the `Renderer` constructor**, not in `Render()`. It configures Russian Roulette, stratified sampling, and NEE based on the loaded world.
- **World = BVH + non-BVH list.** `SceneLoader` separates finite primitives (into a `BvhNode`) from `InfinitePlane` instances and CSG nodes (kept linear). The returned `world` is a `HittableList` mixing both. `InfinitePlane` inside a `Transform` is still detected and routed to the linear list — its infinite AABB would poison BVH quality.
- **BVH threshold = 4.** `SceneLoader.BvhThreshold` and similarly `Group` with >4 finite children gets an internal BVH. Below that, linear search wins.
- **Materials are built in two passes** so `MixMaterial` / `blend` can reference other materials by ID (including mix-of-mix). Unresolved or cyclic refs are replaced with a gray Lambertian fallback plus a deferred warning.
- **Templates vs instances.** `templates:` entries are blueprints — no geometry is produced until an entity of `type: instance` references them. Last-write-wins on template name.
- **YAML imports** are resolved relative to the importing file, prepended to local sections (so locals override imported IDs), and protected against cycles via a `HashSet<string>` of absolute paths. `world`, `camera`, and `cameras` are intentionally NOT imported.
- **Deferred messages.** `SceneLoader` queues warnings/info through `Warn()`/`Info()` during load so they don't mangle the single-line `"Loading scene... done (X ms)"` output. `Program.cs` calls `SceneLoader.FlushMessages()` after the done-line prints.

### Path tracer (`Rendering/Renderer.cs`)
Parallel over pixels; per pixel does `√N × √N` stratified samples. `TraceRay` recurses: hit → normal map → emission → NEE (direct light sampling) → BSDF scatter → Russian Roulette. Post-processing is per-pixel: firefly clamp (`-C`, default 100) → ACES filmic tone map → gamma 2.2. `--clamp`/`-C` applies to per-sample radiance before tone mapping — lower it (e.g. 25) when dielectrics or participating media produce fireflies. See `docs/technical/path-tracing-and-lighting.md` and `docs/technical/shading-model.md`.

### Lights and NEE
`ILight` has point/directional/spot/area/sphere implementations under `Lights/`. Additionally, any geometry with an `Emissive` material becomes a `GeometryLight` and joins the NEE pool automatically; the environment (gradient sky or HDRI) also participates in NEE as a directional sampler.

**Light Hardening (see DEVLOG §Ciclo Light Hardening):**
- `SoftRadius` on all light types: floors the attenuation denominator `max(distSq, r²)` to prevent 1/d² divergence in volumetric media.
- `DirectionalLight.AngularRadiusDeg`: sun disc with cone-sampling shadow rays (0.27° = real Sun). `IsDelta = false` when active; `ShadowSamples` defaults to 16.
- `SpotLight.ShadowSamples + SoftRadius`: disc-jittered shadow rays model bulb size in fog.
- `AreaLight.SoftRadius` / `GeometryLight.SoftRadius`: floor the `cosLight/d²` area estimator denominator.
- `LightDistribution`: power-weighted CDF for NEE single-light picking. Built once in `Renderer` constructor. CLI: `--light-sampling power|uniform|all`.
- Indirect firefly clamp: `_indirectMaxSampleRadiance = _maxSampleRadiance × indirectClampFactor`. Applied in `ShadeSurface` and `ShadeSampleBounce`. CLI: `--indirect-clamp-factor` (default 1.0 = no extra suppression).
- `ISamplable.SurfaceArea`: deterministic closed-form area property on all 12 geometry classes. Replaces the PRNG-consuming `Sample()` call in `GeometryLight` constructor.

### Geometry, CSG, Groups
`Geometry/IHittable.cs` is the core interface. `CsgObject` implements Union/Intersection/Subtraction via interval classification (see `docs/technical/csg-boolean-operations.md`) and is nestable. `Group` is a scene-graph node that inherits transforms down to children and builds its own internal BVH above 4 children. `Transform` wraps any `IHittable` with scale→rotate→translate and caches its world-space AABB.

### Volumetrics (`Volumetrics/`)
`IMedium` covers Homogeneous, HeightFog, HeterogeneousProceduralMedium (Perlin fBm with delta/ratio tracking), and GridMedium (trilinear default, tricubic Catmull-Rom option). Phase functions are pluggable: Isotropic, HG, double-HG, Rayleigh, Schlick. A `globalMedium` is returned from `SceneLoader.Load` alongside the world.

### Tests — equivalence pattern
The test suite asserts **algorithmic equivalence against a reference implementation**, not absolute numbers. `AabbTests` uses an inline scalar slab-test oracle to validate the `Vector3.Min/Max` SIMD `AABB.Hit`. `BvhEquivalenceTests` runs the same rays through `BvhNode` and `HittableList` and asserts identical hit/miss and `rec.T` within `1e-4`. Seeds are passed via `[InlineData]` so failures replay deterministically. Copy the primitives list before handing it to `BvhNode` — construction mutates in place.

## Conventions

- YAML keys use `underscore_case` (YamlDotNet `UnderscoredNamingConvention`).
- CLI: lowercase short flags are the common overrides (`-s`, `-d`, `-c`, `-w`, `-o`, `-i`); uppercase are "advanced overrides" (`-H` height because `-h` is help, `-S` shadow samples, `-C` clamp). New long-only flags: `--indirect-clamp-factor`, `--light-sampling`.
- Default output path when `-o` is omitted: `renders/render-<scene-stem>.png`.
- Output image format is picked from the `-o` extension (`.png`/`.jpg`/`.jpeg`/`.bmp`); unknown → PNG.

## Further reading

Docs live under `docs/` (bilingual EN/IT):
- `docs/reference/` — complete YAML schema + rendering profiles.
- `docs/technical/` — pipeline, path tracing, shading, BVH/SAH, quartic/torus, CSG, testing, benchmarks.
- `docs/tutorial/` — 10-chapter walkthrough.
