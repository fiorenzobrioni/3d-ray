# Rendering Profiles Reference

This document is the practical reference for tuning the three render-quality parameters of 3D-Ray - `-s` (pixel samples), `-d` (max bounce depth), `-S` (shadow samples) - plus the firefly clamp `-C`. It defines three canonical profiles (**Preview**, **Standard**, **Final**), explains how each parameter maps to engine internals, and lists the tips you need to avoid wasting render time.

---

### 1. **THE THREE CANONICAL PROFILES**

| Profile | `-s` | grid | `-d` | `-S` | Typical use |
|---|---|---|---|---|---|
| **Preview** (Draft) | `64` | 8×8 | `4` | `1` | Scene composition, camera placement, material colors. Fast and noisy. |
| **Standard** | `512` | ~23×23 | `8` | `1` | Day-to-day quality renders, CI/CD, reviews. Pair with the denoiser for a clean image. |
| **Final** (Showcase) | `1024` | 32×32 | `8` | `4` | Portfolio, README cover, promotional renders. Crisp and publication-ready. |

All values in the table are **perfect squares** on purpose (see sections 3 and 5 below).

**Ready-to-paste commands:**
```bash
# Preview - seconds to a minute on a typical scene
RayTracer -i my-scene.yaml -w 400 -H 225 -s 64 -d 4 -S 1

# Standard - CI-friendly, good for README inline previews
RayTracer -i my-scene.yaml -w 800 -H 450 -s 512 -d 8

# Final - portfolio / README cover quality
RayTracer -i my-scene.yaml -w 1920 -H 1080 -s 1024 -d 8 -S 4
```

> The `.yaml` extension on `-i` is **optional**: if the path does not
> exist as given, the loader retries with `.yaml` and then `.yml`
> appended - `RayTracer -i my-scene ...` is equivalent to the commands
> above.

---

### 1a. **THE `--quality` / `-q` PRESETS**

If you don't want to write `-w -H -s -d -S` by hand every time, the
`--quality` (alias `-q`) flag bundles all five into a single named
preset. The ladder is **4 quality tiers × 3 resolutions + 1 4K showcase**
(`draft → standard → pre-final → final → ultra`):

| `-q` value | Resolution | `-s` | `-d` | `-S` | Denoiser | Typical use |
|---|---|---|---|---|---|---|
| `draft-tiny`   | 480×270   | 16   | 8 | 1 | nfor fast | Instant sanity check / gross errors |
| `draft-small`  | 960×540   | 16   | 8 | 1 | nfor fast | Super-fast composition / camera placement |
| `draft`        | 1920×1080 | 16   | 8 | 1 | nfor fast | Same speed, Full HD framing |
| `standard-tiny`  | 480×270   | 512 | 12 | 1 | nfor high | Quick check, classic-scene quality |
| `standard-small` | 960×540   | 512 | 12 | 1 | nfor high | Material/lighting iteration, thumbnails |
| `standard`       | 1920×1080 | 512 | 12 | 1 | nfor high | **Day-to-day quality render** (review, CI, classic scenes) |
| `pre-final-tiny`  | 480×270   | 256 | 16 | 1 | nfor high | Full-feature spot check |
| `pre-final-small` | 960×540   | 256 | 16 | 1 | nfor high | Final-look iteration at ¼ resolution |
| `pre-final`       | 1920×1080 | 256 | 16 | 1 | nfor high | **Faithful preview of `final`**, ~4-6× faster |
| `final-tiny`   | 480×270   | 1024 | 16 | 4 | - | Quick full-quality spot check |
| `final-small`  | 960×540   | 1024 | 16 | 4 | - | Showcase thumbnail / contact-sheet |
| `final`        | 1920×1080 | 1024 | 16 | 4 | - | Portfolio, README cover |
| `ultra`        | 3840×2160 | 512  | 32 | 4 | - | 4K showcase |

`-d` is the total number of path vertices; each preset also splits it by
kind of bounce (see section 4), because glass needs many refractions and
diffuse interreflection few:

| Tier | total `-d` | diffuse | glossy | transmission | volume | cut-outs |
|---|---|---|---|---|---|---|
| `draft*` | 8 | 3 | 4 | 8 | 3 | 16 |
| `standard*` | 12 | 4 | 6 | 12 | 4 | 16 |
| `pre-final*`, `final*` | 16 | 8 | 8 | 16 | 8 | 16 |
| `ultra` | 32 | 16 | 16 | 32 | 16 | 32 |

Feature switches per tier: `standard` turns photon caustics and volumetric
SSS **off** and relaxes the indirect clamp factor to 0.5 (it only acts with `-C`);
`pre-final`, `final` and `ultra` run the **full feature set** (caustics on -
2M photons for pre-final, 2–4M for final/ultra - SSS on, all-lights NEE,
no clamp). `ultra` stays at 512 spp on purpose: at 4K the pixel density
hides per-pixel noise that would be visible at 1080p, and 1024 spp would
double an already long render.

The `*-tiny` variants are **quarter resolution** on each axis relative to
full HD (480×270 = ¹⁄₁₆ of the pixels of 1920×1080, ¹⁄₄ of `*-small`),
designed for instant scene validation - catching gross composition or
lighting errors before committing to a longer render.

The `*-small` variants are **exactly half resolution** on each axis
(960×540 = ¼ of the pixels of 1920×1080), so they cost roughly ¼ of the
matching full-HD preset and stay perfectly readable on screen.

**Any explicit flag still wins.** The preset only fills the values you
didn't pass: `-q final -d 32` runs the final preset but raises the depth
to 32 for every kind of bounce (e.g. for a deep stack of glass); `-q standard -w 640 -H 360` shrinks
the standard preset without touching its sampling settings.

**Denoiser.** The `draft*`, `standard*` and `pre-final*` presets enable the
feature-guided denoiser by default (`--denoiser nfor`; `draft*` uses
`--denoise-quality fast`, the others `high`): the linear HDR beauty is
filtered before tone mapping using albedo/normal/depth guides, which is
where low- and mid-spp renders gain the most - and where `standard`'s
512 spp often leave a faint residual grain that the denoiser absorbs for a
few extra seconds. `final` and `ultra` stay unfiltered by design (converged
reference renders keep every unfiltered detail); add an explicit
`--denoiser nfor` if you want them filtered, or `--denoiser none` to switch
the preset default off. See [Denoising](../technical/denoising.md) for the
algorithm and trade-offs.

**Caustics.** The `pre-final`, `final` and `ultra` presets also enable
photon-mapped caustics (`--caustics on`) by default; `draft` and `standard`
leave them off. The photon budget for the pre-pass is controlled by
`--caustic-photons <N>` (2M on pre-final, ~2–4M on final/ultra): more
photons give sharper, less noisy caustics at the cost of a slower pre-pass.
An explicit `--caustics off` (or `--caustics on` on a lower preset)
overrides the preset default. See [Path Tracing and Lighting §2.5](../technical/path-tracing-and-lighting.md).

**`standard` - the day-to-day quality render.** The `standard` tier targets
final-class image quality on a *classic* scene - Lambertian/Disney
surfaces, non-nested glass (at most a couple of crystal spheres one behind
another), procedural marble with ordinary parameters - while stripping the
expensive global-illumination machinery that such scenes don't need.
Relative to `final` it: turns photon **caustics off**, turns volumetric
**SSS off** (`--sss-mode off`), runs **512 spp** with a **single shadow
sample** (512 spp already anti-aliases), trims the bounce budgets (4
diffuse, 6 glossy, 12 transmission), and lets the **NFOR denoiser** absorb
the residual grain. On a scene with no
caustics/SSS this is dramatically faster than `final` for visually
equivalent output. As always, explicit flags win - e.g. `-q standard
--caustics on` re-enables caustics, `-q standard -s 768` raises the sample
count if glass edges still look grainy. Avoid it for scenes that genuinely
rely on focused caustics, deep translucency/SSS, or stacked/nested glass -
use `pre-final`/`final` there.

**`pre-final` - a faithful preview of `final`.** Same feature set as
`final` (caustics on, SSS on, the same bounce budgets, the same light
sampling) with the sampling budgets cut where the denoiser compensates best:
**¼ of the pixel samples** (256) and a **single shadow sample** - penumbra
noise is exactly what feature-guided filtering removes cleanest. The result
previews the final lighting, caustics and translucency at roughly **4-6×
the speed**; use it to iterate on a scene destined for `final`, then run
`final` unfiltered for the deliverable.

```bash
# Instant sanity check, a few seconds
RayTracer -i my-scene -q draft-tiny

# Quick composition check, seconds
RayTracer -i my-scene -q draft-small

# Day-to-day quality render / review, Full HD
RayTracer -i my-scene -q standard

# Full-feature preview of the final look, Full HD
RayTracer -i my-scene -q pre-final

# Portfolio render, Full HD
RayTracer -i my-scene -q final

# 4K showcase
RayTracer -i my-scene -q ultra

# Final preset + custom override (depth raised for a deep stack of glass)
RayTracer -i my-scene -q final -d 32

# Preset denoiser switched off (raw 512 spp output)
RayTracer -i my-scene -q standard --denoiser none
```

> The preset names follow the conventional Preview/Standard/Final quality
> ladder, with `pre-final` as the denoised preview of `final`. The `-small` variants are suited to half-resolution iteration
> checks, and the `-tiny` variants provide an even faster check at quarter
> resolution.

---

### 2. **DEFAULTS**

> **No `-q` means `draft-small`.** When the command line omits `--quality`, the
> renderer applies the `draft-small` preset (960×540, 16 spp, depth 8, single
> shadow sample, NFOR-fast denoiser) - a quick, denoised composition check is a
> better first-run default than a slow, un-denoised pass. Every quality knob
> below still overrides the preset when passed explicitly. The values in the
> table are the resulting `draft-small` defaults.

| Parameter | Default | Source |
|---|---|---|
| `-s` / `--samples` | `16` (4×4 grid) | `draft-small` preset |
| `-d` / `--depth` | `8` (diffuse 3, glossy 4, transmission 8, volume 3) | `draft-small` preset |
| `--depth-transparent` | `16` (cut-outs crossed, outside the total) | `PathDepths.DefaultTransparent` |
| `-S` / `--shadow-samples` | `1` (`draft-small`; without a preset → per-light YAML, default 4) | `Program.cs` |
| `--denoiser` | `nfor` fast (`draft-small`) | `draft-small` preset |
| `-C` / `--clamp` | off (no firefly clamp) | opt-in, suggested start `10` (`Renderer.SuggestedMaxSampleRadiance`) |
| `--indirect-clamp-factor` | `0.25` (only with `-C`: indirect clamp = `-C` × factor) | `Renderer.DefaultIndirectClampFactor` |
| `--transparent-shadows` | `off` (glass casts its physical shadow and caustic) | `Program.cs` |
| `--exposure` | `0` EV (identity) | `Renderer.DefaultExposureEv` |
| `--light-sampling` | `auto` (`all` up to 16 lights, `bvh` above) | `LightSamplingStrategy.Auto` |
| `--texture-filtering` | `auto` (filtering on) | `Renderer.TextureFilteringMode.Auto` |
| `--sampler` | `sobol` (Owen-scrambled) | `Program.cs` / `Sampler.SetKind` |
| `--mis` | `balance` (Veach balance heuristic) | `Program.cs` / `MisHeuristic` |

> The default `sobol` sampler (Burley 2020, hash-based Owen scrambling over a Joe-Kuo direction table) converges noticeably faster than the legacy thread-local PRNG on pixel jitter, lens sampling and early bounces. Pass `--sampler prng` to fall back when comparing against historical images or debugging stochastic regressions.

> **`--mis balance` vs `--mis power`** - both are unbiased Multiple Importance Sampling weights (Veach 1997 §9.2). The default `balance` (`w = p/(p+q)`) is the variance-minimising single-strategy heuristic. The optional `power` (`w = p²/(p²+q²)`) is the β=2 power heuristic and reduces variance further when the two PDFs disagree by a wide margin - typical scenarios are small specular lights against rough diffuse materials, or pinpoint area lights inside a fog volume. The cost is identical; you can switch back and forth without rerunning preprocessing.

The defaults target **fast iteration**, not final quality. Use the Preview profile as the minimum viable "nice-looking" render; use Standard or Final when you need to publish.

---

### 3. **UNDERSTANDING `-s` (PIXEL SAMPLES)**

The engine performs **stratified sampling** on a √N × √N grid per pixel. Passing `-s 16` gives you a 4×4 grid (16 samples); `-s 256` gives you a 16×16 grid (256 samples). Each cell produces one jittered sample.

**Sobol (default) uses the exact count.** The default `sobol` sampler fires exactly the number of samples you request - no rounding occurs. **For `--sampler prng` only:** the engine needs a √N × √N jitter grid, so it rounds √N up: `-s 15` silently becomes 4×4 = 16. To control cost precisely with PRNG, prefer perfect squares: `1, 4, 9, 16, 25, 36, 49, 64, 100, 144, 196, 256, 400, 576, 784, 1024, 1600`.

**Cost:** approximately linear - doubling `-s` roughly doubles render time.

---

### 4. **UNDERSTANDING `-d` (MAX BOUNCE DEPTH)**

`-d` caps the number of **vertices** of a path: at depth 1 a camera ray
sees what it hits, lit directly (emission and next-event estimation); every
further unit is one more bounce. Under it, five limits count the bounces by
the kind of lobe the material sampled:

| Flag | Counts | Why it has its own limit |
|---|---|---|
| `--depth-diffuse` | diffuse bounces (Lambertian, the diffuse base, translucency) | diffuse interreflection loses most of its energy in three or four bounces |
| `--depth-glossy` | glossy and mirror reflections | a corridor of mirrors needs more than a matte room |
| `--depth-transmission` | refractions and straight transmission through glass | every interface costs one: a glass of water is four, a row of bottles a dozen |
| `--depth-volume` | scatters inside a medium (fog, smoke, the medium inside glass) | a dense medium can scatter hundreds of times |
| `--depth-transparent` | cut-outs crossed (geometry opacity 0: leaves, fences) | **outside** the total: a cut-out is not a bounce, crossing it costs nothing |

A path stops at whichever limit it reaches first; the bounce sampled at its
last vertex is still followed for the light it meets (no further bounce), so
the direct light of every vertex is complete. The presets set the split
shown in section 1a; an **explicit `-d` lifts every kind to that value**
(`-d 24` means "24 of anything"), and any `--depth-*` flag still overrides
its own kind, capped by the total.

**Russian roulette** ends most paths long before these limits: from the
fifth bounce on, a path survives with probability equal to its largest
throughput channel (corrected by `η²` across refractions, so a ray inside
glass is not penalised for the radiance compression of the interface) and
is reweighted by 1/q when it does. It is unbiased, it only trades variance
for speed, and it needs no knowledge of the scene. Raising the limits
therefore rarely costs much: the bright paths go on, the dim ones stop.

**When to raise them:**
- **Stacked dielectrics** - liquids inside glasses, rows of wine bottles,
  glass spheres nested inside each other: raise `--depth-transmission` (or
  `-d`) until the inner glass stops going dark.
- **Thick participating media**: raise `--depth-volume`.
- **Mirror corridors**: raise `--depth-glossy`.
- **Foliage made of many cut-out cards**: raise `--depth-transparent`.

---

### 4b. **PATH GUIDING (`--guiding on`)**

Path guiding learns, before the render, where the light reaching each part
of the scene comes from, and then draws part of the bounces from that
knowledge instead of from the material alone. It targets scenes where most
light arrives indirectly from small regions: a room lit by the sun through
a window, light through a door left ajar, a bright patch that lights a whole
interior.

- **Training is part of `-s`.** Passes of 1, 2, 4, ... samples per pixel
  (half of `-s` by default, `--guiding-training <n>` to change it) learn the
  distribution; every pass is a valid estimate of the image and joins it,
  weighted by its noise, so no sample is thrown away.
- **Unbiased and deterministic.** A guided bounce is weighted by the density
  of the BSDF/guide mixture, and light samples use the same density in their
  multiple importance sampling: the image converges to the same result as
  without guiding (checked against the reference renderer on every
  validation scene). The same command gives the same image on any number of
  threads.
- **Only broad surfaces are guided** (diffuse, rough metal from roughness 0.3):
  a sharp highlight is found best by its own BSDF.
- **Resolution matters.** The guide learns from the image's own samples: below
  roughly 240 × 135 pixels it has too few to learn from and can add noise.

Measured on `scenes/review/guiding-room.yaml` (240 × 135, 256 spp): about 20%
less error for the same number of samples, but each sample costs about 1.4×
as much (learning and the guide lookups), so for the same render time plain
path tracing is still slightly ahead in the scenes tested. Guiding is off by
default; try it on hard interiors at high sample counts.

---

### 5. **UNDERSTANDING `-S` (SHADOW SAMPLES)**

`-S` forces a global override of the shadow-ray count used by every area-based light (`AreaLight`, `SphereLight`, `GeometryLight`). Each light builds a √N × √N stratified grid across its surface and shoots one shadow ray per cell.

**Warning - multiplicative cost.** At each surface hit the engine spawns `S` shadow rays **per area light, per pixel sample, per bounce**. With `-s 256`, `-S 4`, two area lights, and 6 bounces you're looking at roughly `256 × 4 × 2 × 6 ≈ 12,000` shadow rays per pixel. Raising `-S` is the single easiest way to wreck your render time.

**Rule of thumb:**
- Default / Preview / Standard → `-S 1`.
- Only raise `-S` to `4` (2×2) or `9` (3×3) when the main render is already clean but the soft penumbras on the floor are the remaining source of noise.
- Use perfect squares (`1, 4, 9, 16`) - same √N×√N stratification as `-s`.

Pixel samples (`-s`) and shadow samples (`-S`) both reduce shadow noise. Prefer spending budget on `-s` first; only reach for `-S` when you've proven that shadows (not GI) are the bottleneck.

---

### 6. **FIREFLY CLAMP (`-C` / `--clamp`)**

`MaxSampleRadiance` (exposed as `-C`) is a hard ceiling on per-sample radiance **before tone mapping**. It catches the rare outliers produced by specular caustics, Disney lobe compensation, and Russian Roulette boost - the pixels that would otherwise appear as bright white dots ("fireflies") in your render.

**Default: off.** The clamp is a trade of bias for noise, and the threshold is an absolute radiance: it does not single out outliers, it cuts every sample brighter than the value. In a scene whose lights are brighter than the threshold it removes real light (a Cornell box lit by a panel of radiance 20 lost 22% of its energy with the former default of `10`). Render without it first, use the denoiser or more samples against noise, and reach for `-C` only when isolated fireflies remain. `10` is a reasonable starting value for lights of radiance up to a few units.

**When to raise `-C`:**
- Strongly sun-lit HDRIs where the sun disk is showing up dimmer than expected.
- Highly emissive scenes where you can verify that the bright source itself (not its caustics) is being suppressed. Try `-C 25–100`.
- Removing `-C` turns the clamp off entirely. The tradeoff is potential fireflies in caustics and deep specular chains.

**When to lower `-C` further:**
- Dense fog / thick homogeneous media + high `-d`.
- Scenes with many small bright emissives seen through glass.
- Try `-C 5` or `-C 3`. You lose some HDR dynamic range in the hottest highlights but gain cleaner shadows and softer penumbras at extreme bounces.

The clamp uses **luminance-preserving scaling**, so it does not shift hue on bright highlights - only brightness.

#### **6a. Depth-aware indirect clamp (`--indirect-clamp-factor`)**

A second, optional clamp tightens suppression specifically on **indirect bounces** (depth ≥ 1), providing finer control over deep-bounce fireflies independently from the primary clamp.

```
--indirect-clamp-factor 0.25
```

This multiplies the primary `-C` threshold for all indirect contributions, so it acts only when `-C` is given. With the default `0.25` and `-C 10` the indirect clamp is `2.5`: deep-bounce radiance is capped at 2.5, primary radiance at 10. Set to `1.0` to disable the extra suppression and have the indirect clamp equal the primary clamp.

**When to lower further:** caustic/specular chains that still produce fireflies at the default. Try `0.1` for heavily volumetric scenes with glass.

**When to raise toward `1.0`:** scenes where indirect highlights look unexpectedly dim - typically pure-emissive Cornell-style setups or HDRIs where the only legitimate bright signal comes from indirect bounces.

#### **6b. Transparent shadows (`--transparent-shadows`)**

```
--transparent-shadows on
```

**Default: off.** Glass then behaves physically: it casts its shadow, and the light it refracts reaches the floor along the paths that really cross it, which is the caustic (sharp but noisy at low sample counts; `--caustics on` focuses it with photons). Index-matched surfaces (`refraction_index: 1`, the transparent shell that bounds a fog or water volume) and smooth `thin_walled` sheets always let shadow rays through: their transmission does not bend light, so this is exact.

`on` is an artistic approximation: shadow rays go straight through glass, tinted by its Fresnel transmittance, so clear glass casts a soft, tinted half-shadow instead of a shadow and a caustic. The refracted light paths from diffuse surfaces through the glass to the lights are dropped so the same light is not counted twice. Use it for quick, noise-free previews of scenes with a lot of glass; leave it off for final images.

#### **6c. Light importance sampling (`--light-sampling`)**

```
--light-sampling power
```

Selects how the renderer chooses which light to query per NEE event:

| Value | Behaviour | When to use |
|---|---|---|
| `auto` | `all` up to 16 lights, `bvh` above | **Default** |
| `all` | Sum over every light | Few lights: every light sampled at every vertex |
| `power` | Sample one light ∝ `ApproximatePower` | Scenes with many lights of mixed brightness, all of them seen from everywhere |
| `uniform` | Sample one light uniformly | Debug / baseline comparison against `power` |
| `bvh` | Sample one light through a light BVH (Conty and Kulla 2018): each light's power weighed by its distance and orientation to the shading point | Many lights spread out in space (a street of lamps, a room of fixtures, emissive strips): the near lights get the samples |

With `all` the renderer fires `ShadowSamples` shadow rays per light per shading point - O(N·S). With `power`, `uniform` or `bvh` it fires shadow rays for one light, then divides by the probability of picking it to remain unbiased - O(S); the same probability enters the multiple-importance weights of the BSDF side, so every strategy converges to the same image. In a scene with 90 small lights in a grid, `bvh` cuts the per-pixel noise about four times against `uniform` at the same sample count. The environment and the analytic sun are picked first with probability 1/(number of infinite lights + 1), then the BVH.

#### **6d. Texture filtering (`--texture-filtering`)**

```
--texture-filtering <auto|on|off>
```

Controls analytic anti-aliasing of procedural and image textures via ray
differentials. The camera fires auxiliary rays through the pixel `+x`/`+y`
neighbours; their footprint at each surface hit drives a pre-filtered
texture lookup - Perlin/fBm clamps octaves above Nyquist, Voronoi adaptive
supersamples, image textures use mipmap + anisotropic filtering (up to 8 trilinear taps
along the footprint's long axis). The footprint is one pixel divided by
√spp, never smaller than an eighth of a pixel; only the camera ray's first
hit is filtered, later bounces are point-sampled.

| Value | Behaviour | When to use |
|---|---|---|
| `auto` | Filtering on (camera emits differentials) | **Default** - always safe |
| `on`   | Same as `auto`, reserved for future heuristics | Identical to `auto` |
| `off`  | No differentials, every texture sampled point-only | Baseline comparison, benchmarks, verifying that aliasing in a render comes from the texture pipeline rather than from camera sampling |

The default `auto` removes moiré, shimmer and high-frequency grain on
distant or grazing-angle surfaces - typically lets you drop `-s` by 4×
on outdoor scenes with procedural ground or wide-angle camera moves
without losing image quality. Disabling with `off` is only useful for
debugging or A/B comparison; the cost of filtering is small (a few
percent at most on typical scenes).

#### **6e. Photographic exposure (`--exposure`)**

```
--exposure <EV>
```

Linear gain `2^EV` applied to every pixel **before** the ACES tone map.
Replicates the concept of photographic exposure compensation familiar from
post-processing workflows. `EV = 0` (default) is
identity; negative values darken (1 EV = factor 2×), positive brighten.

**Why it matters:** ACES filmic is a non-linear curve whose contrast is
preserved only inside its linear sweet-spot at roughly `[0.18, 1.0]` of
incoming radiance. Above ~2.0 the curve flattens onto a 0.95-0.99
plateau where everything looks white regardless of underlying base
colour - procedural textures, marble veining and material identity all
collapse into uniform brightness. Below ~0.05 the rolloff fades to
black. `--exposure` lets you slide the whole scene into the sweet-spot
without re-balancing every light by hand.

| Situation | Suggested `--exposure` |
|---|---|
| Scene is reading washed-out, bright spots saturate first | `-1` to `-2` |
| Scene is too dark, mid-tones fall in the noise floor      | `+1` to `+2` |
| You've already tuned lights to land near `0.5` linear     | `0` (skip the flag) |

Combine with the lighting setup, not as a substitute: re-balancing
light intensities is preferable for shareable scenes (other artists
shouldn't need to remember a flag), but `--exposure` is the fastest
artist-time iteration knob when you don't want to commit a light tweak.

---

### 7. **PRACTICAL TIPS**

- **Start every scene in Preview.** Iterate composition and materials with `-s 64 -d 4 -S 1` until the image reads correctly. Only then promote to Standard or Final.
- **Spend budget on `-s` before `-S` or `-d`.** Pixel samples attack every noise source simultaneously (GI, shadows, specular); the other two target specific problems.
- **Prefer the presets' depth split to a bare `-d`.** An explicit `-d` lifts every kind of bounce to the same limit; to give glass more room, raise `--depth-transmission` alone.
- **Do not combine high `-s` with high `-S` without reason.** `-s 1024 -S 16` is almost never a good trade. `-s 1024 -S 4` usually matches it visually at ¼ the cost.
- **Glass-heavy scenes are the usual reason to raise a limit.** Raise `--depth-transmission` to `24` or `32` only when you see unexpectedly black interiors in stacked dielectrics.
- **Reproducibility in CI.** The `-s`/`-d`/`-S` values in `.github/workflows/*.yml` should be a specific named profile (typically Preview or a trimmed Standard) - not ad-hoc values.

---

### 8. **TIPS FOR PARTICIPATING MEDIA (FOG / SMOKE)**

3D-Ray supports four global medium types - `homogeneous`, `height_fog`,
`procedural`, `grid` - plus phase functions `isotropic`, `hg`, `rayleigh`,
`double_hg`, `schlick`. Each type has very different cost and noise
characteristics.

- **Homogeneous and height_fog are "free"** relative to normal rendering:
  transmittance has a closed form, distances are sampled exactly. A Preview is
  already usable; Standard is almost always sufficient.
- **Procedural (Perlin fBm) and grid are tracked with null collisions**
  (spectral tracking for distances, ratio tracking for shadow rays, against a
  grid of majorants for `grid`): unbiased, but noisier by construction,
  especially in dense regions.
  - Preview shows obvious grain - fine for composition work.
  - For publication-ready images aim for `-s 576` (24×24) or `-s 1024`.
  - If noise concentrates in the light cone, raise `-s` (not `-S`).
- **Firefly clamp with dense fog.** Media with high `sigma_s` and `-d 8+`
  occasionally produce rare bright spikes. If more samples or the denoiser
  do not remove them, add `-C 5` or `-C 3`: you trade a little bias for a
  much cleaner image.
- **Point/spot lights inside a medium.** In `homogeneous`, `height_fog` and
  `atmosphere` their light is also sampled equiangularly (distances spread as
  the angle the light sees) and combined by MIS with the free flight: the
  halo round the emitter, where 1/d² peaks, converges cleanly. On the
  validation scene `fog-sphere-light` the error at equal samples fell 16×.
  `soft_radius` remains a physical bulb size, not a firefly fix.
- **`soft_radius` on area lights inside a medium.** The `cosLight/d²`
  area estimator can diverge at grazing angles in dense media. Set
  `soft_radius` on area lights (e.g. `0.5`–`2.0`). Sphere lights use a
  solid-angle estimator that is bounded by construction and do not
  consume `soft_radius`. Combined with `--indirect-clamp-factor 0.25`,
  this covers all major firefly paths.
- **Do not raise `-d` for the fog.** The volumetric path is already handled
  correctly at `-d 6–8`. More bounces in the fog = more cost, not more
  realism (Russian Roulette terminates the walks anyway).
- **Phase functions with g → 1 (e.g. HG g=0.95)** produce tighter, more
  dramatic god-rays but **increase variance**: if cones look "noisy" lower
  `g` to 0.7–0.85 or switch to `double_hg` with more balanced weights.
- **Rayleigh** is cheap (closed-form) and useful for skies / atmosphere.
  `double_hg` and `schlick` cost about the same as standard HG.
- **Grid medium: watch the resolution.** Inline-YAML grids up to 8³ are fine;
  above that switch to the binary `.vol` format (`file:` field instead of
  `data:`). Grid resolution does not affect render cost - only parse time
  and memory footprint.

**Recommended profile for volumetric showcases:**
```bash
# Volumetric Preview (composition check, ~30-60 s)
RayTracer -i scene.yaml -w 400 -H 225 -s 64 -d 4 -S 1

# Volumetric Standard (review; tracked media still a bit noisy)
RayTracer -i scene.yaml -w 800 -H 450 -s 400 -d 6 -S 1

# Volumetric Final (publication-ready cleanliness)
RayTracer -i scene.yaml -w 1920 -H 1080 -s 1024 -d 8 -S 4 -C 5
```

---

### 9. **RELATED DOCUMENTATION**

- [`docs/technical/path-tracing-and-lighting.md`](../technical/path-tracing-and-lighting.md) - internals of the path tracer, NEE, and Russian Roulette.
- [`docs/technical/rendering-pipeline.md`](../technical/rendering-pipeline.md) - pipeline overview.
- [`docs/reference/scene-reference.md`](./scene-reference.md) - scene YAML schema.
