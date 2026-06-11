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
- `-q/--quality <preset>` — shorthand for common quality profiles (`draft-tiny`, `draft-small`, `draft`, `medium-tiny`, `medium-small`, `medium`, `final-tiny`, `final-small`, `final`, `final-fast-tiny`, `final-fast-small`, `final-fast`, `ultra`). Overrides `-s`, `-d`, `-S` (and, for `final-fast`, also forces caustics/SSS off, power NEE, and the indirect clamp). `final-fast` = final-class quality optimised for classic scenes (Lambertian/Disney, non-nested glass, procedural marble). `draft*`/`medium*`/`final-fast*` also enable `--denoiser nfor`; `final`/`ultra` stay unfiltered.
- `--denoiser none|nlm|nfor` + `--denoise-quality fast|high` — feature-guided denoiser on the linear HDR beauty before tone mapping (`Denoising/`, see `docs/technical/denoising.md`). Default `none` without a preset; an explicit flag always beats the preset.
- `--aov <list>` — comma list of `albedo,normal,depth,beauty,variance`; writes linear-HDR `.pfm` files next to `-o` (beauty is post-denoise when a denoiser is active).
- `--list-cameras` — print available cameras and exit; `-c <name|index>` selects one.
- `--sampler sobol|prng` — sampling strategy (default: `sobol`, deterministic Sobol+Owen; `prng` is legacy).
- `--mis balance|power` — multiple-importance-sampling heuristic (default: `balance`).
- `--texture-filtering auto|on|off` — trilinear mip-map filtering override.
- `--exposure <f>` — EV exposure adjustment applied before tone mapping.
- `--indirect-clamp-factor <f>` — stricter firefly clamp for the indirect contribution, applied once camera-relative (default 0.25 = on; 1.0 = off, i.e. same as `-C`).
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
In-solution (`3d-ray.slnx`), run with `dotnet run --project src/Tools/<Name>/<Name>.csproj -- <flags>` (see each tool's `--help`):
- `TextureGen`, `NormalMapGen` — procedural texture / normal-map generators.
- `FontGen` — emits font templates under `scenes/assets/fonts/` for the `extrusion` primitive.
- `TerrainGen` — writes a reusable `scenes/assets/heightmaps/<name>.yaml` (a single `type: heightfield` entity, intersected directly via min/max mipmap — no mesh tessellation) + a 16-bit grayscale heightmap PNG; `--with-cameras` also emits a ready-to-render `scenes/<name>-preview.yaml`.

Standalone (on disk under `src/Tools/`, not in the solution — run directly with `dotnet run --project ...`): scene generators `ChessGen` and `TempleGen`, plus the one-off `MigrateFakeSss` (strips legacy "fake SSS" Disney knobs from scene YAML; `--dry-run`/`--project`).

### CI
`.github/workflows/dotnet.yml` builds Release, runs the full xUnit test suite, and then runs a 320×213 smoke render of `scenes/chess.yaml`. `.github/workflows/render-scenes.yml` is a `workflow_dispatch` matrix render at 1920×1080 — enable scenes by uncommenting entries in its `matrix.scene:` list.

## Architecture

### Solution layout
`3d-ray.slnx` groups three engine projects (`src/RayTracer`, `src/RayTracer.Tests`, `src/RayTracer.Benchmarks`) and four tools (`src/Tools/{TextureGen,NormalMapGen,TerrainGen,FontGen}`); the standalone tools above are intentionally excluded. CI builds `RayTracer.csproj` and runs the test suite; benchmarks are opt-in.

### Rendering pipeline (YAML → pixel)
`Program.cs` → `SceneLoader.Load()` → `Renderer(..).Render(w,h)` → `SaveImage()`. The detailed contract between stages is in `docs/technical/rendering-pipeline.md`. Key invariants that span multiple files:

- **Scene analysis happens in the `Renderer` constructor**, not in `Render()`. It configures Russian Roulette, stratified sampling, and NEE based on the loaded world.
- **World = BVH + non-BVH list.** `SceneLoader` separates finite primitives (into a `BvhNode`) from `InfinitePlane` instances and CSG nodes (kept linear). The returned `world` is a `HittableList` mixing both. `InfinitePlane` inside a `Transform` is still detected and routed to the linear list — its infinite AABB would poison BVH quality.
- **BVH threshold = 4.** Both `SceneLoader` (via `BvhThreshold`) and `Group` build an internal BVH when finite children exceed 4; below that they stay linear.
- **Materials are built in two passes** so `MixMaterial` / `blend` can reference other materials by ID (including mix-of-mix). Unresolved or cyclic refs are replaced with a gray Lambertian fallback plus a deferred warning.
- **Templates vs instances.** `templates:` entries are blueprints — no geometry is produced until an entity of `type: instance` references them. Last-write-wins on template name.
- **YAML imports** resolve relative to the importing file and are prepended to local sections (locals override imported IDs); cycles are blocked. `world`, `camera`, and `cameras` are intentionally NOT imported.
- **Deferred messages.** `SceneLoader` queues warnings/info during load (`Warn()`/`Info()`); `Program.cs` flushes them via `FlushMessages()` after the single-line load output.

### Path tracer (`Rendering/Renderer.cs`)
Parallel over **16×16 tiles** (better load balance / cache locality than scanlines; progress printed by a dedicated reporter thread so workers never touch the Console lock), N samples each via the active sampler (`--sampler sobol` default, deterministic; `prng` legacy — internals in the docs below). **Order matters:** `TraceRay` recurses hit → normal map → emission → NEE → BSDF scatter → Russian Roulette; per-pixel post-processing is firefly clamp (`-C`, default 10) → ACES filmic tone map → gamma 2.2. `-C` clamps per-sample radiance *before* tone mapping — lower it (e.g. 5) for fireflies from dielectrics/media. The indirect clamp (`--indirect-clamp-factor`, default 0.25 = on) is applied **once, camera-relative**, to the throughput-weighted indirect contribution at the primary surface. See `docs/technical/path-tracing-and-lighting.md` + `shading-model.md`.

**Capture/denoise invariant:** `Render(w,h,RenderCaptureOptions)` optionally captures linear-HDR beauty, even/odd dual-buffer halves and first-non-delta-hit AOVs (albedo/normal/depth) — the tone-mapped pixels stay **bit-identical** with capture on, off, or via the legacy overload (no extra RNG draws; enforced by `RenderCaptureTests`). The denoiser (`Denoising/`, `docs/technical/denoising.md`) runs on the linear beauty pre-tonemap; `Renderer.ToneMapToDisplay` re-applies the identical display transform.

### Lights and NEE
Light implementations live under `Lights/`. **Invariants:** any `Emissive` geometry auto-joins the NEE pool as a `GeometryLight`, and the environment (sky/HDRI) participates in NEE as a directional sampler. `LightDistribution` (power-weighted CDF) is built once in the `Renderer` constructor and drives NEE (`--light-sampling power|uniform|all`); indirect bounces use a stricter clamp (`--indirect-clamp-factor`). Hardening mechanics (soft-radius floors, sun-disc sampling, jittered shadow rays, `ISamplable.SurfaceArea`) → `docs/technical/path-tracing-and-lighting.md` + DEVLOG §Ciclo Light Hardening.

### Geometry, CSG, Groups
`Geometry/IHittable.cs` is the core interface. **Invariants:** `Transform` applies scale→rotate→translate and caches its world-space AABB; `Group` inherits transforms to children and builds an internal BVH above 4 children; `type: heightfield` is routed through `SceneLoader.CreateHeightFieldEntity` to a `MinMaxMipmap`-accelerated primitive (not a tessellated mesh). CSG (`CsgObject`, nestable union/intersection/subtraction) and the heightfield are detailed in `docs/technical/{csg-boolean-operations,heightfield}.md`.

The **Surface Displacement Stack** (`DisplacementEngine`) composes optional stages in a *fixed order*: bump → Loop/Catmull-Clark subdivision → scalar displacement → vector displacement → autobump.

### Volumetrics (`Volumetrics/`)
Pluggable `IMedium` + `IPhaseFunction` implementations under `Volumetrics/` (homogeneous, height fog, Nishita atmosphere, procedural fBm, grid). **Invariant:** a `globalMedium` is returned from `SceneLoader.Load` alongside the world, and output is bit-identical when no medium is present.

### Tests — equivalence pattern
The suite (~38 test files, ~400 tests) covers geometry, BVH, materials, lights, samplers, volumetrics, displacement, and rendering regression. Core patterns:

- **Equivalence**: `AabbTests` uses an inline scalar slab-test oracle to validate the `Vector3.Min/Max` SIMD `AABB.Hit`. `BvhEquivalenceTests` runs the same rays through `BvhNode` and `HittableList` and asserts identical hit/miss and `rec.T` within `1e-4`. Seeds are passed via `[InlineData]` so failures replay deterministically.
- **Regression**: `FireflyRegressionTests` renders a stress scene (bright sphere light + dense medium + depth 8) and asserts spike-pixel count stays below a calibrated threshold. Scene files live in `src/RayTracer.Tests/TestScenes/` (e.g. `firefly-stress.yaml`).
- **Gotcha**: `BvhNode` construction mutates the primitives list in place — copy it first.

## Conventions

- YAML keys use `underscore_case` (YamlDotNet `UnderscoredNamingConvention`).
- CLI: lowercase short flags are the common overrides (`-s`, `-d`, `-c`, `-w`, `-o`, `-i`, `-v`); uppercase are "advanced overrides" (`-H` height because `-h` is help, `-S` shadow samples, `-C` clamp); behaviour-tuning flags are long-only. Full list in `ShowHelp()`.
- Default output path when `-o` is omitted: `renders/render-<scene-stem>.png`.
- Output image format is picked from the `-o` extension (`.png`/`.jpg`/`.jpeg`/`.bmp`); unknown → PNG.

## Documentation updates

When planning a change, include doc updates as explicit final steps so they don't slip. Bilingual files under `docs/` come in EN + IT pairs — update both.

- **YAML schema** (parameters added/changed/removed): `docs/reference/scene-reference.md` (EN+IT) + affected `docs/tutorial/{en,it}/` chapters.
- **User-facing features** (new/changed/removed CLI flags, rendering behaviour, tools): root `README.md`.
- **Dev history & planning**: record completed work/design rationale in `DEVLOG.md`; track roadmap, TODO, and known bugs in `PLANNING.md`.

### No third-party renderer names in public docs

Never mention Arnold, Cycles, RenderMan, Blender, V-Ray, Octane, Redshift, or any other external renderer/DCC tool in `README.md` or any file under `docs/`. This applies even when a feature was designed by taking inspiration from one of those systems. Keep the public documentation clean and self-contained.

`DEVLOG.md` and `PLANNING.md` are internal notes — references to external renderers are allowed there when describing completed work, design rationale, or roadmap items that need full technical context.

## Further reading

Docs live under `docs/` (bilingual EN/IT):
- `docs/reference/` — complete YAML schema + rendering profiles.
- `docs/technical/` — pipeline, path tracing, shading, BVH/SAH, quartic/torus, CSG, testing, benchmarks.
- `docs/tutorial/{en,it}/` — chapter-based walkthrough (12 chapters at last count).
- `DEVLOG.md` — development-cycle history + design notes; `PLANNING.md` — roadmap, TODO, known bugs, ideas, pre-commit checklist.
