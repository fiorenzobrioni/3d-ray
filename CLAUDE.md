# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**3D-Ray** is a C#/.NET 10 path-tracing renderer. Scenes are described in YAML, parsed into a scene graph, rendered in parallel across CPU cores, and written out as PNG/JPEG/BMP via `SixLabors.ImageSharp`. `README.md` is in Italian; `docs/` is bilingual EN+IT; the engine and code are in English.

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
Required: `-i`. See `src/RayTracer/Program.cs` `ShowHelp()` for the full CLI. Canonical quality profiles and the `-s`/`-d`/`-S`/`-C` trade-offs are in `docs/reference/rendering-profiles.md`.

Key flags:
- `-q/--quality <preset>` — shorthand for common quality profiles (`draft-tiny`, `draft-small`, `draft`, `medium-tiny`, `medium-small`, `medium`, `final-tiny`, `final-small`, `final`, `ultra`). Overrides `-s`, `-d`, `-S`.
- `--list-cameras` — print available cameras and exit; `-c <name|index>` selects one.
- `--sampler sobol|prng` — sampling strategy (default: `sobol`, deterministic Sobol+Owen; `prng` is legacy).
- `--mis balance|power` — multiple-importance-sampling heuristic (default: `balance`).
- `--texture-filtering auto|on|off` — trilinear mip-map filtering override.
- `--exposure <f>` — EV exposure adjustment applied before tone mapping.
- `--indirect-clamp-factor <f>` — stricter firefly clamp for indirect bounces (default 1.0, i.e. same as `-C`).
- `-v/--verbose` — print per-tile progress and timing.

### Tests (xUnit)
```
dotnet test src/RayTracer.Tests/RayTracer.Tests.csproj
dotnet test src/RayTracer.Tests/RayTracer.Tests.csproj --filter "FullyQualifiedName~BvhEquivalenceTests"
dotnet test src/RayTracer.Tests/RayTracer.Tests.csproj --filter "FullyQualifiedName=RayTracer.Tests.AabbTests.Hit_AxisAlignedRay_BehaviourMatchesReference"
```
The test project is **not** referenced by `RayTracer.csproj`. Tests are run by CI (see `dotnet.yml`) and can also be invoked explicitly. See `docs/technical/testing.md`.

### Benchmarks (BenchmarkDotNet)
BenchmarkDotNet refuses to run outside Release.
```
dotnet run -c Release --project src/RayTracer.Benchmarks -- --filter '*'
dotnet run -c Release --project src/RayTracer.Benchmarks -- --filter '*Bvh*' --job short
```

### Tools
In-solution (`3d-ray.slnx`):
```
dotnet run --project src/Tools/TextureGen/TextureGen.csproj
dotnet run --project src/Tools/NormalMapGen/NormalMapGen.csproj
dotnet run --project src/Tools/FontGen/FontGen.csproj -- --font "<family>" [--height 0.2] [--chars "ABC"] [--list-fonts]
dotnet run --project src/Tools/TerrainGen/TerrainGen.csproj -- --name <stem> [--type pianura|collina|montagna] [--season ...] [--include fiumi,laghi,mare,isole] [--with-cameras]
```
`TerrainGen` writes a reusable terrain template to `scenes/libraries/terrain/<name>.yaml` plus a `<name>-height.png` 16-bit grayscale heightmap — the YAML wraps a single `type: heightfield` entity that the engine intersects directly via min/max mipmap (no mesh tessellation). `--with-cameras` additionally emits a complete `scenes/<name>-preview.yaml` ready to render. `FontGen` emits font templates under `scenes/libraries/fonts/` for use with the `extrusion` primitive. See each tool's `Program.cs` / `--help` for full flags.

Standalone scene generators (on disk under `src/Tools/`, not in the solution — run directly with `dotnet run --project ...`): `ChessGen`, `TempleGen`.

### CI
`.github/workflows/dotnet.yml` builds Release, runs the full xUnit test suite, and then runs a 320×213 smoke render of `scenes/chess.yaml`. `.github/workflows/render-scenes.yml` is a `workflow_dispatch` matrix render at 1920×1080 — enable scenes by uncommenting entries in its `matrix.scene:` list.

## Architecture

### Solution layout
`3d-ray.slnx` groups three engine projects (`src/RayTracer`, `src/RayTracer.Tests`, `src/RayTracer.Benchmarks`) and four tools (`src/Tools/{TextureGen,NormalMapGen,TerrainGen,FontGen}`). `ChessGen` and `TempleGen` live on disk under `src/Tools/` but are intentionally not in the solution. CI builds `RayTracer.csproj` and runs the test suite; benchmarks are opt-in.

### Rendering pipeline (YAML → pixel)
`Program.cs` → `SceneLoader.Load()` → `Renderer(..).Render(w,h)` → `SaveImage()`. The detailed contract between stages is in `docs/technical/rendering-pipeline.md`. Key invariants that span multiple files:

- **Scene analysis happens in the `Renderer` constructor**, not in `Render()`. It configures Russian Roulette, stratified sampling, and NEE based on the loaded world.
- **World = BVH + non-BVH list.** `SceneLoader` separates finite primitives (into a `BvhNode`) from `InfinitePlane` instances and CSG nodes (kept linear). The returned `world` is a `HittableList` mixing both. `InfinitePlane` inside a `Transform` is still detected and routed to the linear list — its infinite AABB would poison BVH quality.
- **BVH threshold = 4.** Both `SceneLoader` (via `BvhThreshold`) and `Group` build an internal BVH when finite children exceed 4. Below that, linear search wins.
- **Materials are built in two passes** so `MixMaterial` / `blend` can reference other materials by ID (including mix-of-mix). Unresolved or cyclic refs are replaced with a gray Lambertian fallback plus a deferred warning.
- **Templates vs instances.** `templates:` entries are blueprints — no geometry is produced until an entity of `type: instance` references them. Last-write-wins on template name.
- **YAML imports** are resolved relative to the importing file, prepended to local sections (so locals override imported IDs), and protected against cycles via a `HashSet<string>` of absolute paths. `world`, `camera`, and `cameras` are intentionally NOT imported.
- **Deferred messages.** `SceneLoader` queues warnings/info through `Warn()`/`Info()` during load so they don't mangle the single-line `"Loading scene... done (X ms)"` output. `Program.cs` calls `SceneLoader.FlushMessages()` after the done-line prints.

### Path tracer (`Rendering/Renderer.cs`)
Parallel over pixels; per pixel generates N samples using the active sampler. With `--sampler sobol` (default) samples form a (0,2,2)-net via Sobol+Owen scrambling — deterministic per pixel via a hash seed. With `--sampler prng` the legacy `√N × √N` stratified grid is used instead. `TraceRay` recurses: hit → normal map → emission → NEE (direct light sampling) → BSDF scatter → Russian Roulette. Post-processing is per-pixel: firefly clamp (`-C`, default 100) → ACES filmic tone map → gamma 2.2. `--clamp`/`-C` applies to per-sample radiance before tone mapping — lower it (e.g. 25) when dielectrics or participating media produce fireflies. See `docs/technical/path-tracing-and-lighting.md` and `docs/technical/shading-model.md`.

### Lights and NEE
`ILight` has point/directional/spot/area/sphere implementations under `Lights/`. Additionally, any geometry with an `Emissive` material becomes a `GeometryLight` and joins the NEE pool automatically; the environment (gradient sky or HDRI) also participates in NEE as a directional sampler.

**Light Hardening** (full rationale in DEVLOG §Ciclo Light Hardening): every light type has a `SoftRadius` floor on `1/d²`; `DirectionalLight.AngularRadiusDeg` enables sun-disc cone sampling; `SpotLight` and area/geometry lights use disc-jittered shadow rays; `LightDistribution` (power-weighted CDF, built once in the `Renderer` constructor) drives NEE — selectable via `--light-sampling power|uniform|all`. Indirect bounces use a stricter firefly clamp `_indirectMaxSampleRadiance = _maxSampleRadiance × indirectClampFactor` (CLI `--indirect-clamp-factor`, default 1.0). `ISamplable.SurfaceArea` exposes deterministic closed-form area on every samplable geometry class, replacing PRNG-consuming `Sample()` calls.

### Geometry, CSG, Groups
`Geometry/IHittable.cs` is the core interface. `CsgObject` implements Union/Intersection/Subtraction via interval classification (see `docs/technical/csg-boolean-operations.md`) and is nestable. `Group` is a scene-graph node that inherits transforms down to children and builds its own internal BVH above 4 children. `Transform` wraps any `IHittable` with scale→rotate→translate and caches its world-space AABB. `HeightField` (Mitsuba-style terrain primitive — see `docs/technical/heightfield.md`) accelerates intersection with a `MinMaxMipmap` quadtree so one primitive can replace a tessellated terrain mesh; the loader routes `type: heightfield` through `SceneLoader.CreateHeightFieldEntity`.

The **Surface Displacement Stack** (`DisplacementEngine`) applies layered surface detail in order: bump map → Loop/Catmull-Clark mesh subdivision → scalar displacement → vector displacement → autobump. Each stage is optional and composes cleanly with the others.

### Volumetrics (`Volumetrics/`)
`IMedium` covers Homogeneous, HeightFog, NishitaAtmosphereMedium (physically-based sky atmosphere), HeterogeneousProceduralMedium (Perlin fBm with delta/ratio tracking), and GridMedium (trilinear default, tricubic Catmull-Rom option). Phase functions are pluggable: Isotropic, HG, double-HG, Rayleigh, Schlick. A `globalMedium` is returned from `SceneLoader.Load` alongside the world.

### Tests — equivalence pattern
The suite (~27 test files, 464 tests) covers geometry, BVH, materials, lights, samplers, volumetrics, displacement, and rendering regression. Core patterns:

- **Equivalence**: `AabbTests` uses an inline scalar slab-test oracle to validate the `Vector3.Min/Max` SIMD `AABB.Hit`. `BvhEquivalenceTests` runs the same rays through `BvhNode` and `HittableList` and asserts identical hit/miss and `rec.T` within `1e-4`. Seeds are passed via `[InlineData]` so failures replay deterministically.
- **Regression**: `FireflyRegressionTests` renders a stress scene (bright sphere light + dense medium + depth 8) and asserts spike-pixel count stays below a calibrated threshold. Scene files live in `src/RayTracer.Tests/TestScenes/`.
- **Construction note**: copy the primitives list before handing it to `BvhNode` — construction mutates in place.

## Conventions

- YAML keys use `underscore_case` (YamlDotNet `UnderscoredNamingConvention`).
- CLI: lowercase short flags are the common overrides (`-s`, `-d`, `-c`, `-w`, `-o`, `-i`, `-v`); uppercase are "advanced overrides" (`-H` height because `-h` is help, `-S` shadow samples, `-C` clamp). Long-only flags include `--indirect-clamp-factor`, `--light-sampling`, `--list-cameras`, `--sampler`, `--mis`, `--texture-filtering`, `--exposure`, `--quality`.
- Default output path when `-o` is omitted: `renders/render-<scene-stem>.png`.
- Output image format is picked from the `-o` extension (`.png`/`.jpg`/`.jpeg`/`.bmp`); unknown → PNG.

## Documentation updates

When planning a change, include doc updates as explicit final steps so they don't slip. Bilingual files under `docs/` come in EN + IT pairs — update both.

- **YAML schema** (parameters added/changed/removed): `docs/reference/scene-reference.md` + `riferimento-scene.md`, the affected `docs/tutorial/{en,it}/` chapters, and `DEVLOG.md`.
- **User-facing features** (new/changed/removed CLI flags, rendering behaviour, tools): root `README.md` + `DEVLOG.md`.

## Further reading

Docs live under `docs/` (bilingual EN/IT):
- `docs/reference/` — complete YAML schema + rendering profiles.
- `docs/technical/` — pipeline, path tracing, shading, BVH/SAH, quartic/torus, CSG, testing, benchmarks.
- `docs/tutorial/{en,it}/` — chapter-based walkthrough (12 chapters at last count).
