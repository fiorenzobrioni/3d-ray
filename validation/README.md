# Validation harness

Internal tooling for the engine review (`PIANO-REVIEW.md`, Phase 0). It renders
canonical scenes with 3D-Ray and with **Mitsuba 3** (the reference), and
measures the difference.

## Setup

```
pip install -r validation/requirements.txt     # mitsuba 3.9.1, numpy, PyYAML, flip-evaluator
```

Mitsuba runs on the CPU (`llvm_ad_rgb`, fallback `--variant scalar_rgb`). The
script builds `RayTracer` and `ImageCompare` in Release itself.

## Usage

```
python3 validation/run.py                          # every scene in validation/scenes/
python3 validation/run.py cornell-lambert lights   # some scenes
python3 validation/run.py --force-reference        # re-render the cached Mitsuba images
python3 validation/run.py --convergence cornell-lambert:d32
```

Output in `validation/out/` (git-ignored): per scene and run, `mitsuba.pfm`,
`3dray-reference.beauty.pfm`, `3dray-default.beauty.pfm`, `diff-*.png`
(false colour: red = 3D-Ray brighter, blue = darker, full colour at 25%), and
the summary `report.md` / `report.json`.

Each scene is rendered twice by 3D-Ray:

- **reference**: `--reference`, the unbiased estimator. It must match Mitsuba;
  any gap is an engine bug.
- **default**: the engine with its default settings, denoiser off. Since the
  review switched the clamps and the transparent shadows off by default, the
  only departure left is the prefiltered HDRI for BSDF rays when no light
  samples the sky; the gap to the reference mode is its price.

## Metrics

| metric | meaning |
|---|---|
| mean ratio | mean luminance 3D-Ray / reference: global energy bias |
| relMSE | mean (t − r)² / (r² + 0.01), Rousselle et al. 2011 |
| FLIP | NVIDIA HDR-FLIP, perceptual difference (0 = identical) |
| bad px | share of pixels whose luminance differs by more than 10% |

`--convergence` renders the reference mode at 16, 64, 256 and 1024 spp and K
independent seeds at 64 spp, then fits the error: an unbiased estimator falls
as 1/N (slope ≈ −1) and its floor is the reference's own noise; a plateau is
bias.

## Scenes

Scenes are ordinary 3D-Ray YAML with a `validation:` block the engine ignores:

```yaml
validation:
  width: 128
  height: 96
  spp: 1024            # 3D-Ray
  reference_spp: 4096  # Mitsuba
  analytic_mean: 1.0   # optional: compare with a closed-form value instead
  runs:
    - { name: d16, depth: 16 }   # 3D-Ray -d 16 = Mitsuba max_depth 17
```

`yaml2mitsuba.py` converts only what it can translate exactly (spheres, quads,
Lambertian, dielectric, white Metal, emissive, point / sphere / area lights,
flat sky, pinhole and thin-lens cameras) and refuses the rest. The Disney BSDF
is mapped to Mitsuba's `principled`, the Disney 2015 base model, and flagged as
an approximation in the report. A material without `color` gets the engine's
default grey 0.5, glass included: write `color: [1, 1, 1]` for clear glass.

## Related tools

- `--reference`, `--seed <n>`: renderer flags (see `Program.cs` help).
- `src/Tools/ImageCompare`: the C# metrics used here, usable on its own.
- `src/RayTracer.Tests/Validation/`: BSDF statistical kit (chi-square, pdf
  integral, white furnace, reciprocity, direct-light consistency), analytic
  scenes and reference-mode tests. Full BSDF report:
  `RAYTRACER_AUDIT=1 dotnet test src/RayTracer.Tests --filter "Category=Audit" --logger "console;verbosity=detailed"`.
