# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**3D-Ray** is a C#/.NET 10 path-tracing renderer. Scenes are described in YAML, parsed into a scene graph, rendered in parallel across CPU cores, and written out as PNG/JPEG/BMP via `SixLabors.ImageSharp`. `README.md` is in English (primary); `README.it.md` is the Italian translation - both must stay in sync. `docs/` is bilingual EN+IT; the engine and code are in English.

## Common Commands

All commands run from the repo root.

### Build
```
dotnet build src/RayTracer/RayTracer.csproj -c Release
```

### Render a scene (main entry point)
```
# default: draft-small preset (960×540, 16 spp, depth 8, nfor denoiser)
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- \
  -i scenes/<scene>.yaml -o renders/out.png

# explicit quality preset
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- \
  -i scenes/<scene>.yaml -o renders/out.png -q final
```
Required: `-i`. See `src/RayTracer/Program.cs` `ShowHelp()` for the full CLI. Canonical quality profiles and the `-s`/`-d`/`-S`/`-C` trade-offs are in `docs/reference/rendering-profiles.md`.

Key flags:
- `-q/--quality <preset>` - quality ladder `draft → standard → pre-final → final → ultra`, each (except `ultra`) with `-tiny` (480×270) and `-small` (960×540) variants. **Omitting `-q` defaults to `draft-small`** (960×540, 16 spp, depth 8, nfor-fast denoiser). Any explicit flag (`-s`, `-d`, `-w`, `-H`, `-S`) overrides the preset's value. Each preset also splits the depth by kind of bounce (draft 3/4/8/3 diffuse/glossy/transmission/volume of 8, standard 4/6/12/4 of 12, pre-final and final 8/8/16/8 of 16, ultra 16/16/32/16 of 32); an explicit `-d` lifts every kind to that value, `--depth-diffuse|--depth-glossy|--depth-transmission|--depth-volume|--depth-transparent` set one kind. `standard` = final-class quality for classic scenes (Lambertian/Disney, non-nested glass, procedural marble) without the GI extras; `pre-final` = faithful preview of `final` (full feature set, 256 spp + denoiser, ~4-6× faster). `draft*`/`standard*`/`pre-final*` enable `--denoiser nfor`; `final`/`ultra` stay unfiltered.
- `--denoiser none|nlm|nfor` + `--denoise-quality fast|high` - feature-guided denoiser on the linear HDR beauty before tone mapping (`Denoising/`, see `docs/technical/denoising.md`). Default `nfor fast` via the implicit `draft-small` preset; an explicit flag always beats the preset.
- `--aov <list>` - comma list of `albedo,normal,depth,beauty,variance`; linear-HDR guide buffers next to `-o` (beauty is post-denoise when a denoiser is active). Default: separate `.pfm` files; with `-o *.exr` they become layers of the main multilayer EXR; `--aov-format pfm|exr` forces one file per AOV.
- `--list-cameras` - print available cameras and exit; `-c <name|index>` selects one.
- `--sampler sobol|prng` - sampling strategy (default: `sobol`, deterministic padded Sobol with Owen scrambling, a 2D-stratified pair per decision; `prng` is legacy).
- `--mis balance|power` - multiple-importance-sampling heuristic (default: `balance`).
- `--texture-filtering auto|on|off` - trilinear mip-map filtering override.
- `--exposure <f>` - EV exposure adjustment applied before the view transform.
- `--view agx|aces|standard|legacy` (default `agx`) + `--look none|punchy|golden` - display transform; `aces` is the real ACES 1.x RRT+ODT (checked against OpenColorIO), `legacy` the pre-review per-channel curve. `--white-balance <K>` (or the camera's `white_balance`). Display only: EXR stays scene-linear.
- `--filter gaussian|blackman-harris|box` (default `gaussian`, σ 0.5 px) - pixel reconstruction filter. The `Renderer` constructor defaults stay box filter and legacy view, so library callers and tests are unchanged unless they opt in.
- `--bit-depth 8|16` (16 for PNG) and `--dither on|off` (default on, TPDF ±1 code on 8-bit output).
- `--reference` - unbiased reference estimator for validation (no clamps, no straight-through transparent shadows, no prefiltered HDRI for BSDF rays, uniform per-kind depths; refuses the denoiser and `--integrator vcm`). `--seed <n>` - independent sample sequences (0 = bit-identical default).
- `-C <n>` - opt-in firefly clamp (off by default: an absolute radiance ceiling is biased, it removed 22% of a Cornell box's light at the old default 10). `--indirect-clamp-factor <f>` - indirect clamp = `-C` × f, applied once camera-relative, only with `-C` (default 0.25; 1.0 = same as `-C`).
- `--transparent-shadows on|off` - default off: glass casts its physical shadow and caustic; straight transmission (IOR-1 shells, smooth thin-walled) always passes shadow rays, exactly. `on` = straight-through tinted shadows through all glass as an approximation, with the refracted diffuse→glass→light paths dropped so nothing is counted twice.
- `--guiding on|off` + `--guiding-training <n>` - path guiding (default off; training = half of `-s`, part of it, its samples join the image). Unbiased, deterministic; measured ~20% less error at equal samples but ~1.4× cost per sample in the scenes tested (`docs/reference/rendering-profiles.md` §4b).
- `--integrator path|bdpt|vcm` - light transport (default `path`, every preset): `bdpt` bidirectional path tracing (unbiased, surfaces and media), `vcm` = bdpt + vertex merging with a shrinking radius (`--vcm-radius`, fraction of the scene radius, default 0.003; consistent, refused by `--reference`) for caustics seen through glass/water. There is no photon map any more (`--caustics` was removed in Phase 8b).
- `--light-sampling auto|all|power|uniform|bvh` - NEE light selection (default `auto`: every light up to 16, a light BVH above; all unbiased).
- `-v/--verbose` - print per-tile progress and timing.

### Tests (xUnit)
```
dotnet test src/RayTracer.Tests/RayTracer.Tests.csproj
dotnet test src/RayTracer.Tests/RayTracer.Tests.csproj --filter "FullyQualifiedName~BvhEquivalenceTests"
dotnet test src/RayTracer.Tests/RayTracer.Tests.csproj --filter "FullyQualifiedName=RayTracer.Tests.AabbTests.Hit_AxisAlignedRay_BehaviourMatchesReference"
```
The test project is **not** referenced by `RayTracer.csproj`. Tests are run by CI (see `ci.yml`) and can also be invoked explicitly. See `docs/technical/testing.md`.

### Benchmarks (BenchmarkDotNet)
BenchmarkDotNet refuses to run outside Release.
```
dotnet run -c Release --project src/RayTracer.Benchmarks -- --filter '*'
dotnet run -c Release --project src/RayTracer.Benchmarks -- --filter '*Bvh*' --job short
```

### Tools
In-solution (`3d-ray.slnx`), run with `dotnet run --project src/Tools/<Name>/<Name>.csproj -- <flags>` (see each tool's `--help`):
- `TextureGen`, `NormalMapGen` - procedural texture / normal-map generators.
- `ImageCompare` - compares a linear HDR render (PFM/EXR) with a reference: mean ratio, relMSE, SMAPE, bad-pixel share, false-colour diff (`--json` for scripts).
- `FontGen` - emits font templates under `scenes/assets/fonts/` for the `extrusion` primitive.
- `TerrainGen` - writes a reusable `scenes/assets/heightmaps/<name>.yaml` (a single `type: heightfield` entity, intersected directly via min/max mipmap - no mesh tessellation) + a 16-bit grayscale heightmap PNG; `--with-cameras` also emits a ready-to-render `scenes/<name>-preview.yaml`.

Standalone (on disk under `src/Tools/`, not in the solution - run directly with `dotnet run --project ...`): scene generators `ChessGen` and `TempleGen`, plus the one-off `MigrateFakeSss` (strips legacy "fake SSS" Disney knobs from scene YAML; `--dry-run`/`--project`).

### CI
`.github/workflows/ci.yml` builds Release and runs the full xUnit test suite on Linux, Windows, and macOS in parallel; publishes self-contained single-file binaries for all three platforms as artifacts; and runs a 320×213 smoke render of `scenes/chess.yaml` on Linux. `.github/workflows/render-scenes.yml` is a `workflow_dispatch` matrix render at 1920×1080 - enable scenes by uncommenting entries in its `matrix.scene:` list.

## Architecture

### Solution layout
`3d-ray.slnx` groups three engine projects (`src/RayTracer`, `src/RayTracer.Tests`, `src/RayTracer.Benchmarks`) and five tools (`src/Tools/{TextureGen,NormalMapGen,TerrainGen,FontGen,ImageCompare}`); the standalone tools above are intentionally excluded. CI builds `RayTracer.csproj` and runs the test suite; benchmarks are opt-in.

### Rendering pipeline (YAML → pixel)
`Program.cs` → `SceneLoader.Load()` → `Renderer(..).Render(w,h)` → `SaveImage()`. The detailed contract between stages is in `docs/technical/rendering-pipeline.md`. Key invariants that span multiple files:

- **Scene analysis happens in the `Renderer` constructor**, not in `Render()`. It configures Russian Roulette, stratified sampling, and NEE based on the loaded world.
- **World = BVH + non-BVH list.** `SceneLoader` separates finite primitives (into a `BvhNode`) from `InfinitePlane` instances and CSG nodes (kept linear). The returned `world` is a `HittableList` mixing both. `InfinitePlane` inside a `Transform` is still detected and routed to the linear list - its infinite AABB would poison BVH quality.
- **BVH threshold = 4.** Both `SceneLoader` (via `BvhThreshold`) and `Group` build an internal BVH when finite children exceed 4; below that they stay linear.
- **Materials are built in two passes** so `MixMaterial` / `blend` can reference other materials by ID (including mix-of-mix). Unresolved or cyclic refs are replaced with a gray Lambertian fallback plus a deferred warning.
- **Templates vs instances.** `templates:` entries are blueprints - no geometry is produced until an entity of `type: instance` references them. Last-write-wins on template name.
- **YAML imports** resolve relative to the importing file and are prepended to local sections (locals override imported IDs); cycles are blocked. `world`, `camera`, and `cameras` are intentionally NOT imported.
- **Deferred messages.** `SceneLoader` queues warnings/info during load (`Warn()`/`Info()`); `Program.cs` flushes them via `FlushMessages()` after the single-line load output.

### Path tracer (`Rendering/Renderer.cs`)
Parallel over **16×16 tiles** (better load balance / cache locality than scanlines; progress printed by a dedicated reporter thread so workers never touch the Console lock), N samples each via the active sampler (`--sampler sobol` default, deterministic; `prng` legacy - internals in the docs below). **Order matters:** the path is an iterative loop with an explicit throughput (`Rendering/PathIntegrator.cs`, `TracePath`, PIANO-REVIEW.md Phase 7): each vertex goes hit → medium → normal map → emission → NEE → BSDF sample → per-kind depth count → Russian roulette (standard, on β × η², from the fifth bounce, measured; no scene heuristics); every contribution is added as β × X. When a depth limit stops a path, the bounce sampled at its last vertex is still traced for the emission/sky it meets (MIS-weighted, no NEE, no further bounce: `PathState.LastSegment`), so the last vertex's light samples keep their BSDF counterpart. Optional **path guiding** (`--guiding on`, `Rendering/PathGuiding.cs` + `Rendering/Guiding/`, PIANO-REVIEW.md Phase 7b): SD-tree trained in passes on the first indices of each pixel's Sobol sequence (the render continues the sequence; passes combined by inverse variance, per-sample weights in `--reference`), deterministic per-leaf splatting, one stratified pair split between BSDF and guide (`Sampler.Provide2D`), mixture density also in the NEE MIS, only `IMaterial.Guidable` surfaces; Depths count vertices (`-d`) and, separately, bounces per lobe kind (`BsdfSample.Lobe`: `--depth-diffuse|glossy|transmission|volume`, cut-outs under `--depth-transparent`, outside the total); a new lobe must report its kind, and the lobe also sets the next ray's visibility category; camera samples are placed by the pixel reconstruction filter (`--filter`, Gaussian by default, applied by filter importance sampling in `PixelFilter`: each sample counts for its own pixel only); per-pixel post-processing is the optional firefly clamp (`-C`, off by default) → exposure → `DisplayTransform` (white balance, view transform `--view agx|aces|standard|legacy`, sRGB encoding; `Rendering/ColorManagement.cs`). `-C` clamps per-sample radiance *before* tone mapping and is biased, so it is opt-in. The indirect clamp (`--indirect-clamp-factor`, only with `-C`) is applied **once, camera-relative**, to the throughput-weighted indirect contribution at the primary surface. **Shadow rays toward a light's own geometry** go through `ShadowRay.TransmittanceToPoint` (both ends offset along their normals): never aim a shadow ray from a shifted origin along a direction measured from the unshifted point, or the emitter shadows itself. See `docs/technical/path-tracing-and-lighting.md` + `shading-model.md`.

**Sampler dimensions and ray offsets** (`Core/Sampling/`, `Core/MathUtils.cs`, PIANO-REVIEW.md Phase 1): every 2D decision (a direction, a point on a light, a lens position) draws its pair with `MathUtils.Random2D()` / `Sampler.Sample2D()`, never two `Sample1D()` calls: only an aligned pair is jointly stratified. The camera owns a fixed block of dimensions and every path vertex its own (`Sampler.BeginVertex()` at the top of each iteration of `TracePath`), so a branch taken at one vertex never shifts the next vertex's dimensions. Ray origins are offset with `MathUtils.OffsetOrigin` and hit tests start at `MathUtils.RayEpsilon`, never a fixed epsilon: both scale with the scene (`MathUtils.SceneScale`, per thread, set and cleared by the renderer on its workers).

**Capture/denoise invariant:** `Render(w,h,RenderCaptureOptions)` optionally captures linear-HDR beauty, even/odd dual-buffer halves and first-non-delta-hit AOVs (albedo/normal/depth) - the tone-mapped pixels stay **bit-identical** with capture on, off, or via the legacy overload (no extra RNG draws; enforced by `RenderCaptureTests`). The denoiser (`Denoising/`, `docs/technical/denoising.md`) runs on the linear beauty pre-tonemap; `Renderer.ToneMapToDisplay` re-applies the identical display transform.

### Bidirectional integrators (`Rendering/Bidirectional/`)
`--integrator bdpt|vcm` (PIANO-REVIEW.md Phase 8b, `docs/technical/bidirectional.md`): one code base in the VCM formulation (Georgiev et al. 2012; recursive dVCM/dVC/dVM MIS quantities, balance heuristic), BDPT = merging off. Per iteration (= one sample per pixel) light subpaths are traced per pixel in chunks of 2^18, stored, splatted into the camera (light tracing through `Camera.ConnectToFilm`/`FilmDirectionPdf` and the pixel filter, normalised by the pixel count), then camera subpaths connect to the light subpath of their own index and merge with the chunk's vertices (`VertexGrid`). **Invariants:** every light implements `SampleEmission`/`EmissionPdfOverCos` (checked by `EmissionTests`); a light subpath's dVCM is set at its first vertex (receiver-dependent NEE densities); light subpaths use the adjoint BSDF (no (η_i/η_t)² on refraction); straight-transmitting surfaces are null interfaces (no vertex), as for shadow rays; medium vertices use the phase function, cosines 1, never merged (their merging MIS terms dropped); subsurface entities behind a refraction are walked by the camera subpaths with the path tracer's random walk (light subpaths stop there); a pixel's camera and light subpaths share one shutter time; Russian roulette is outside the MIS densities. `LightTransportTests` is rerun with BDPT (`BdptLightTransportTests`): a new light type must pass there too.

### Materials (`Materials/`)
`IMaterial` is `Evaluate(V, L)` (BSDF without the cosine) / `Pdf` / `Sample`, plus transmission queries (`HasTransmission`, `TransmitsStraight`, `ShadowTransmittance`, `ShadowAbsorption`, `TryGetDielectricIor`). There is no `Scatter` and no `EvaluateDirect`: NEE weighs a light sample by `Evaluate` times `|cos|` (`Renderer.DirectIntegrand`), the same function the BSDF sampler estimates, so the two MIS strategies cannot diverge. A null `Sample` means absorbed. Each lobe keeps only directions inside its own support, so the reported mixture pdf equals the sampler's density. Types: `lambertian`, `metal` (complex IOR presets or F82, Kulla-Conty), `dielectric` (smooth or Walter 2007 rough, `(η_i/η_t)²` radiance on refraction), `emissive`, `openpbr` (`OpenPbr.cs`, OpenPBR Surface v1.1, inputs typed `Scene/MaterialParam`: number, colour or texture), `disney` (translated onto `OpenPbr` by `DisneyMaterial.Create`; `DisneyBsdf` no longer exists), `mix`. The albedo tables (`MicrofacetAlbedo`, `AnisotropicAlbedo`, `DielectricAlbedo`) are generated by `validation/gen_*.py` (numpy, minutes each): edit the generator and rerun it, never the `.Data.cs` by hand. Details: `docs/technical/shading-model.md`.

### Textures (`Textures/`)
**Invariants:** an image texture's `color_space` defaults by role (`SceneLoader.TextureRole`: a colour is decoded from sRGB, data such as roughness, weights, masks, bump and displacement is read raw); a new texture slot must pass its role. Every primitive reports `DpDu`/`DpDv` of its own UV mapping, with `Tangent` along +U and `Bitangent` along +V (`PrimitiveUvDerivativeTests` checks a new one); mesh tangents are MikkTSpace's (`MeshTangents`). A bump's height is in scene units: `Renderer.BumpFrameAt` turns a step on the surface into a step in UV and in `LocalPoint` (which carries the transforms' scale, not their rotation: `HitRecord.LocalToWorld`). Procedural noise defaults to improved Perlin (`noise_basis: classic` for the old pattern); octaves dropped by the footprint count at their mean. `EnvironmentMap` lookup and importance sampling share one mapping (`EnvironmentMap.DirectionOf`).

### Lights and NEE
Light implementations live under `Lights/`. **Invariants:** any `Emissive` geometry auto-joins the NEE pool as a `GeometryLight`, and the environment (sky/HDRI) participates in NEE as a directional sampler. The light strategy is picked in the `Renderer` constructor (`--light-sampling auto|all|power|uniform|bvh`, `auto` = all up to 16 lights, `LightBvh` above), and every MIS density goes through `Rendering/LightMisTable.cs`: a light sample's density carries its pick probability on **both** sides, the environment and portals are one weight group (their densities add), a light no BSDF ray can reach keeps weight 1, an emitter hit is owned by the surface hit, not its material. A new light type must be registered there, and checked by `Validation/LightTransportTests.cs` (render at depth 2 against the quadrature of its integral) and `LightPowerTests` (`ApproximatePower` against the measured flux). Stratified shadow samples use `MathUtils.Stratum2D(index, count)` (exactly `count` cells), never a √n grid. Photometric units (`power`, `candela`, `luminance`, `illuminance`, `temperature`, IES) are converted in `SceneLoader.LightEmission` with `Lights/LightUnits.cs`: a scene written in them is in nits and needs the camera's `ev100`; `intensity` stays unitless. Physical skies (`Rendering/Sky/`) derive from `AnalyticSky`: the sun's disc comes from the same top-of-atmosphere illuminance as the sky through `Atmosphere`, never a separate constant; `HosekWilkieData.cs` is generated by `validation/gen_hosek_wilkie_data.py`. Indirect bounces use a stricter clamp (`--indirect-clamp-factor`). Hardening mechanics (soft-radius floors, sun-disc sampling, jittered shadow rays, `ISamplable.SurfaceArea`) and the Phase 6 details → `docs/technical/path-tracing-and-lighting.md` §7 + `docs/technical/multiple-importance-sampling.md`.

### Geometry, CSG, Groups
`Geometry/IHittable.cs` is the core interface. **Invariants:** `Transform` applies scale→rotate→translate and caches its world-space AABB; `Group` inherits transforms to children and builds an internal BVH above 4 children; `type: heightfield` is routed through `SceneLoader.CreateHeightFieldEntity` to a `MinMaxMipmap`-accelerated primitive (not a tessellated mesh). CSG (`CsgObject`, nestable union/intersection/subtraction) and the heightfield are detailed in `docs/technical/{csg-boolean-operations,heightfield,geometry-and-meshes}.md`.

Triangles (flat and smooth) use the watertight test of `TriangleIntersection` (Woop et al. 2013, no epsilon on the determinant) and exact bounds: `AABB.Hit` is conservative (tExit × (1 + 2γ₃)), so **no primitive pads its box by an absolute epsilon**. A sampled point's UV must equal `Hit`'s there, and `ISamplable.AreaPdf` is the true area density (non-uniform `Transform`s make it vary); `GeometryAuditTests` checks a new primitive (implicit-function first hit, Cauchy-Crofton area, sampling). CSG merges both children's crossings in t, grouped within a tolerance across the surface (/|cos θ| along the ray), takes each child's state from `FrontFace` (never parity) and has a surface only where the result's state changes (`CsgAuditTests`, exact intervals). `AnimatedTransform` pads its motion box by the interpolation-error bound max|p''|/8.

The **Surface Displacement Stack** (`DisplacementEngine`) composes optional stages in a *fixed order*: bump → Loop/Catmull-Clark subdivision (onto the limit surface) → scalar displacement (along the limit normals) → vector displacement → autobump. Subdivision vertex rules live in `SubdivisionTopology` (DeRose 1998: sharp/semi-sharp creases, corners, `edge_and_corner` boundaries); `LimitSurface` moves the last level onto the limit surface with exact limit normals; a change to a rule must keep `SubdivisionLimitTests` (limit points invariant under further subdivision) green.

**Model files** (`Scene/MeshImporter.cs`): `.obj` (+ MTL, `ObjLoader`/`MtlLoader`), `.gltf`/`.glb` (`GltfLoader`), `.ply` (`PlyLoader`) are read into a `PolyMesh` with a material slot per face and go through one pipeline (subdivision, displacement, `PolyMesh.TriangulateFace` with ear clipping, MikkTSpace, one `Mesh` per material in a `Group`). The entity's `material` covers every face; otherwise the file's materials (translated into OpenPBR), renamed by `material_overrides`. glTF V is read as 1 − v.

### Volumetrics (`Volumetrics/`)
Pluggable `IMedium` + `IPhaseFunction` (PIANO-REVIEW.md Phase 8, `docs/technical/participating-media.md`). **Invariants:** `IMedium.Sample(ray, tMax, throughput)` returns a `MediumEvent` (scattered or not, t, the weight to multiply into β, the event's phase function); every estimator is unbiased per channel, with no cap on steps. Analytic media (`AnalyticMedium`: homogeneous, height fog, atmosphere) invert a closed-form optical depth; coloured media pick the channel by the path throughput and divide by the channel mixture (spectral MIS). Heterogeneous media (`TrackedMedium`: procedural, grid) use spectral tracking for distances and ratio tracking for transmittance against a grey majorant (`NextMajorant`; the grid walks a majorant grid with a DDA, the tricubic bounded by (1 + 1.25³)/2); a new tracked medium must bound σ_t (`TrackedMedium.Violations` stays 0), and draws after the first collision are `Sampler.SampleLoose1D`, never vertex dimensions. Shadow rays see media only inside a `ShadowRay.BeginMedia`/`EndMedia` context (the path's stack + global medium; they switch media at every boundary they cross); NEE in a medium must open one. Point/spot lights in analytic media are also sampled equiangularly (`Rendering/Equiangular.cs`), MIS-paired with the free-flight NEE (`EquiangularNeeWeight`). The atmosphere medium is aerial perspective: it ignores segments that run to the sky (`ActsOn`). Phase functions sample from an explicit pair (`Sample(wo, u)`), pdf = value. The SSS random walk has no bounce cap and NEE at every bounce; subsurface colours go through `SceneLoader.SubsurfaceMedium` (albedo inversion). A `globalMedium` is returned from `SceneLoader.Load` alongside the world, and output is bit-identical when no medium is present. Camera rays are normalised at the start of `TracePath` (t is a distance everywhere).

### Reference scenes
Never delete a scene created for a test, a validation or a showcase (owner's rule). Verification scenes go in `scenes/review/` with a row in its `README.md` (what it checks, how to render it, the expected result; comparison images in `scenes/review/images/`); feature showcases in `scenes/showcases/`; Mitsuba comparison scenes in `validation/scenes/`. A white-furnace scene needs `lights: []`, or the loader adds two default lights.

### Tests - equivalence pattern
The suite (~1270 tests) covers geometry, BVH, materials, lights, samplers, volumetrics, displacement, and rendering regression. Core patterns:

- **Equivalence**: `AabbTests` uses an inline scalar slab-test oracle to validate the `Vector3.Min/Max` SIMD `AABB.Hit`. `BvhEquivalenceTests` runs the same rays through `BvhNode` and `HittableList` and asserts identical hit/miss and `rec.T` within `1e-4`. Seeds are passed via `[InlineData]` so failures replay deterministically.
- **Regression**: `FireflyRegressionTests` renders a stress scene (bright sphere light + dense medium + depth 8) and asserts spike-pixel count stays below a calibrated threshold. Scene files live in `src/RayTracer.Tests/TestScenes/` (e.g. `firefly-stress.yaml`).
- **Gotcha**: `BvhNode` construction mutates the primitives list in place - copy it first.

## Conventions

- Coordinates: right-handed, +Y up, as Mitsuba, glTF and OBJ (a camera at -Z looking at +Z sees +X on its left). `validation/yaml2mitsuba.py` passes positions and `look_at` through unchanged.
- YAML keys use `underscore_case` (YamlDotNet `UnderscoredNamingConvention`).
- CLI: lowercase short flags are the common overrides (`-s`, `-d`, `-c`, `-w`, `-o`, `-i`, `-v`); uppercase are "advanced overrides" (`-H` height because `-h` is help, `-S` shadow samples, `-C` clamp); behaviour-tuning flags are long-only. Full list in `ShowHelp()`.
- Default output path when `-o` is omitted: `renders/render-<scene-stem>.png`.
- Output image format is picked from the `-o` extension (`.png`/`.jpg`/`.jpeg`/`.bmp` tone-mapped display; `.exr` scene-linear pre-tone-map HDR, multilayer half RGB + float Z, ZIP - writer `Rendering/ExrImage.cs`, reader `Textures/ExrLoader.cs`); unknown → PNG.

## Documentation updates

When planning a change, include doc updates as explicit final steps so they don't slip. Bilingual files under `docs/` come in EN + IT pairs - update both.

- **YAML schema** (parameters added/changed/removed): `docs/reference/scene-reference.md` (EN+IT) + affected `docs/tutorial/{en,it}/` chapters.
- **User-facing features** (new/changed/removed CLI flags, rendering behaviour, tools): root `README.md` (English, primary) **and** `README.it.md` (Italian). Both files must be kept in sync - any change made to one must be applied to the other with the same information in the respective language.
- **Dev history & planning**: record completed work/design rationale in `devlog/devlog.md`; track roadmap, TODO, and known bugs in `PLANNING.md`. Newest entry always on top. When `devlog.md` reaches 1000 lines, archive it as `devlog/devlog-YYYY-MM-DD.md` and create a new `devlog/devlog.md` using the template in `devlog/README.md`.

### Engine review in progress

`PIANO-REVIEW.md` (Italian, internal like `PLANNING.md`) is the active plan for the full review of the engine's math, optics and physics, phase by phase (0 to 11). It stays in the repo until every phase is closed. **No compatibility constraints (owner's rule):** the review aims at premium engine quality and picks the best solution, never a worse one to keep existing scenes, keys, defaults or API working. The scenes in the repo are tests from before the review, not fixed points: at the end of the review they are fixed where the review changed something, rewritten, or moved to `scenes/obsolete/` to be redesigned or deleted (PIANO-REVIEW.md §2.0). The scenes the review itself creates (`scenes/review/`, `validation/scenes/`) are kept. When working on a phase: follow its checklist, tick the boxes, update the status table at the top and the phase's "Esito" section, and log the details in `devlog/devlog.md`. The validation tooling it relies on: `--reference` mode, the BSDF statistical test kit in `src/RayTracer.Tests/Validation/`, `src/Tools/ImageCompare`, and the Mitsuba 3 harness in `validation/` (see `validation/README.md`).

**Practical notes for a review session** (each cloud session starts from a fresh container):
- Mitsuba is installed on demand, only when a phase needs the reference renders: if `python3 -c "import mitsuba"` fails, run `pip install -r validation/requirements.txt` (pins mitsuba 3.9.1) before `validation/run.py`.
- `validation/run.py` runs the Release build of `src/RayTracer` with `--no-build`: do not rebuild that project in Release while it runs. For ad-hoc renders build a separate copy (`dotnet build src/RayTracer/RayTracer.csproj -c Release -o <scratch>/rt` and run `dotnet <scratch>/rt/RayTracer.dll`).
- Run the full test suite with the default output folder: with `-o` outside the repo, the tests that look for the repo root (`MaterialDisplacementTests`, `BumpDisplacementCombinationTests`) fail. A separate `-o` is fine for a filtered run while another suite is running.
- Before closing a phase: full suite green, docs and scenes updated, commit and push; CI runs on Linux, Windows and macOS.

### No third-party renderer names in public docs

Never mention Arnold, Cycles, RenderMan, Blender, V-Ray, Octane, Redshift, or any other external renderer/DCC tool in `README.md`, `README.it.md` or any file under `docs/`. This applies even when a feature was designed by taking inspiration from one of those systems. Keep the public documentation clean and self-contained.

Files in `devlog/`, `PLANNING.md`, `PIANO-REVIEW.md` and `validation/` are internal notes - references to external renderers are allowed there when describing completed work, design rationale, or roadmap items that need full technical context.

### Writing style

When writing or editing any document in this repository (`README.md`, `README.it.md`, files under `docs/` or `devlog/`, `PLANNING.md`, etc.):

- **Never use the em dash `—`**. Use a regular hyphen `-`, a colon `:`, a comma, or parentheses depending on context.
- **Never use the middle dot `·`**. Use a regular hyphen `-` or a comma instead.

## Further reading

Docs live under `docs/`. Match the existing language coverage of each area when generating or updating docs:
- `docs/reference/` - complete YAML schema + rendering profiles. **EN + IT** (paired files, e.g. `scene-reference.md` / `riferimento-scene.md`).
- `docs/technical/` - pipeline, path tracing, shading, BVH/SAH, quartic/torus, CSG, testing, benchmarks. **EN only**.
- `docs/tutorial/{en,it}/` - chapter-based walkthrough (12 chapters at last count). **EN + IT**.
- `devlog/devlog.md` - active development log; archived logs are named `devlog/devlog-YYYY-MM-DD.md`. `PLANNING.md` - roadmap, TODO, known bugs, ideas, pre-commit checklist. **IT only**.
