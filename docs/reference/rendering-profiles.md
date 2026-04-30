# Rendering Profiles Reference

This document is the practical reference for tuning the three render-quality parameters of 3D-Ray — `-s` (pixel samples), `-d` (max bounce depth), `-S` (shadow samples) — plus the firefly clamp `-C`. It defines three canonical profiles (**Preview**, **Standard**, **Final**), explains how each parameter maps to engine internals, and lists the tips you need to avoid wasting render time.

---

### 1. **THE THREE CANONICAL PROFILES**

| Profile | `-s` | grid | `-d` | `-S` | Typical use |
|---|---|---|---|---|---|
| **Preview** (Draft) | `64` | 8×8 | `4` | `1` | Scene composition, camera placement, material colors. Fast and noisy. |
| **Standard** (Medium) | `256` | 16×16 | `6` | `1` (or `4`) | CI/CD, review renders, log previews. Clean with a filmic grain. |
| **Final** (Showcase) | `1024` | 32×32 | `8` | `4` | Portfolio, README cover, promotional renders. Crisp and publication-ready. |

All values in the table are **perfect squares** on purpose (see sections 3 and 5 below).

**Ready-to-paste commands:**
```bash
# Preview — seconds to a minute on a typical scene
RayTracer -i my-scene.yaml -w 400 -H 225 -s 64 -d 4 -S 1

# Standard — CI-friendly, good for README inline previews
RayTracer -i my-scene.yaml -w 800 -H 450 -s 256 -d 6

# Final — portfolio / README cover quality
RayTracer -i my-scene.yaml -w 1920 -H 1080 -s 1024 -d 8 -S 4
```

---

### 2. **DEFAULTS**

| Parameter | Default | Source |
|---|---|---|
| `-s` / `--samples` | `16` (4×4 grid) | `Program.cs` |
| `-d` / `--depth` | `8` | `Program.cs` |
| `-S` / `--shadow-samples` | unset → per-light YAML value | `Program.cs` |
| `-C` / `--clamp` | `10` (firefly clamp) | `Renderer.DefaultMaxSampleRadiance` |
| `--indirect-clamp-factor` | `0.25` (indirect clamp = `2.5`) | `Renderer.DefaultIndirectClampFactor` |
| `--light-sampling` | `all` (sum over every light) | `LightSamplingStrategy.All` |
| `--sampler` | `sobol` (Owen-scrambled) | `Program.cs` / `Sampler.SetKind` |
| `--mis` | `balance` (Veach balance heuristic) | `Program.cs` / `MisHeuristic` |

> The default `sobol` sampler (Burley 2020, hash-based Owen scrambling over a Joe-Kuo direction table) converges noticeably faster than the legacy thread-local PRNG on pixel jitter, lens sampling and early bounces. Pass `--sampler prng` to fall back when comparing against historical images or debugging stochastic regressions.

> **`--mis balance` vs `--mis power`** — both are unbiased Multiple Importance Sampling weights (Veach 1997 §9.2). The default `balance` (`w = p/(p+q)`) is the variance-minimising single-strategy heuristic. The optional `power` (`w = p²/(p²+q²)`) is the β=2 power heuristic and reduces variance further when the two PDFs disagree by a wide margin — typical scenarios are small specular lights against rough diffuse materials, or pinpoint area lights inside a fog volume. The cost is identical; you can switch back and forth without rerunning preprocessing.

The defaults target **fast iteration**, not final quality. Use the Preview profile as the minimum viable "nice-looking" render; use Standard or Final when you need to publish.

---

### 3. **UNDERSTANDING `-s` (PIXEL SAMPLES)**

The engine performs **stratified sampling** on a √N × √N grid per pixel. Passing `-s 16` gives you a 4×4 grid (16 samples); `-s 256` gives you a 16×16 grid (256 samples). Each cell produces one jittered sample.

**Sobol (default) uses the exact count.** The default `sobol` sampler fires exactly the number of samples you request — no rounding occurs. **For `--sampler prng` only:** the engine needs a √N × √N jitter grid, so it rounds √N up: `-s 15` silently becomes 4×4 = 16. To control cost precisely with PRNG, prefer perfect squares: `1, 4, 9, 16, 25, 36, 49, 64, 100, 144, 196, 256, 400, 576, 784, 1024, 1600`.

**Cost:** approximately linear — doubling `-s` roughly doubles render time.

---

### 4. **UNDERSTANDING `-d` (MAX BOUNCE DEPTH)**

`-d` caps the number of indirect bounces a ray may perform. In path tracing, the first 4–6 indirect bounces contribute about 99% of realistic illumination for the majority of scenes.

**Why the default is 8 (not 50):** the renderer uses **adaptive Russian Roulette** (`Renderer.cs`). For normally-lit scenes RR kicks in at bounce 4 and stochastically kills low-contribution paths; for indirect-dominant scenes (emissive-only, dim lights) it activates at bounce 8 with a higher survival floor. Raising `-d` past that point rarely changes the image but always costs time.

**When to raise `-d` above 8:**
- **Stacked dielectrics** — liquids inside glasses, rows of wine bottles, glass spheres nested inside each other. Every enter/exit interface consumes a bounce, so 10 glass interfaces need `-d 16–20` or the inner glass goes unexpectedly black.
- **Thick participating media with complex geometry** behind the volume.

For everything else (solid objects, single glass panes, metals, normal interiors) **`-d 4–8` is plenty**.

---

### 5. **UNDERSTANDING `-S` (SHADOW SAMPLES)**

`-S` forces a global override of the shadow-ray count used by every area-based light (`AreaLight`, `SphereLight`, `GeometryLight`). Each light builds a √N × √N stratified grid across its surface and shoots one shadow ray per cell.

**Warning — multiplicative cost.** At each surface hit the engine spawns `S` shadow rays **per area light, per pixel sample, per bounce**. With `-s 256`, `-S 4`, two area lights, and 6 bounces you're looking at roughly `256 × 4 × 2 × 6 ≈ 12,000` shadow rays per pixel. Raising `-S` is the single easiest way to wreck your render time.

**Rule of thumb:**
- Default / Preview / Standard → `-S 1`.
- Only raise `-S` to `4` (2×2) or `9` (3×3) when the main render is already clean but the soft penumbras on the floor are the remaining source of noise.
- Use perfect squares (`1, 4, 9, 16`) — same √N×√N stratification as `-s`.

Pixel samples (`-s`) and shadow samples (`-S`) both reduce shadow noise. Prefer spending budget on `-s` first; only reach for `-S` when you've proven that shadows (not GI) are the bottleneck.

---

### 6. **FIREFLY CLAMP (`-C` / `--clamp`)**

`MaxSampleRadiance` (exposed as `-C`) is the hard ceiling on per-sample radiance **before tone mapping**. It catches the rare outliers produced by specular caustics, Disney lobe compensation, and Russian Roulette boost — the pixels that would otherwise appear as bright white dots ("fireflies") in your render.

**Default:** `10`. After ACES tone mapping any luminance ≳ 5 already saturates to white, so `10` leaves all visible highlights untouched while killing rare bright spikes. Aligns with Cycles `clamp_indirect = 10` and Arnold `AA_clamp ≈ 10`.

**When to raise `-C`:**
- Strongly sun-lit HDRIs where the sun disk is showing up dimmer than expected.
- Highly emissive scenes where you can verify that the bright source itself (not its caustics) is being suppressed. Try `-C 25–100`.
- Disable effectively with a very high value. The tradeoff is potential fireflies in caustics and deep specular chains.

**When to lower `-C` further:**
- Dense fog / thick homogeneous media + high `-d`.
- Scenes with many small bright emissives seen through glass.
- Try `-C 5` or `-C 3`. You lose some HDR dynamic range in the hottest highlights but gain cleaner shadows and softer penumbras at extreme bounces.

The clamp uses **luminance-preserving scaling**, so it does not shift hue on bright highlights — only brightness.

#### **6a. Depth-aware indirect clamp (`--indirect-clamp-factor`)**

A second, optional clamp tightens suppression specifically on **indirect bounces** (depth ≥ 1), mirroring the "indirect clamp" feature in Cycles and Arnold.

```
--indirect-clamp-factor 0.25
```

This multiplies the primary `-C` threshold for all indirect contributions. With the default `0.25` and `-C 10` the indirect clamp is `2.5`: deep-bounce radiance is capped at 2.5, primary radiance at 10. Set to `1.0` to disable the extra suppression and have the indirect clamp equal the primary clamp.

**When to lower further:** caustic/specular chains that still produce fireflies at the default. Try `0.1` for heavily volumetric scenes with glass.

**When to raise toward `1.0`:** scenes where indirect highlights look unexpectedly dim — typically pure-emissive Cornell-style setups or HDRIs where the only legitimate bright signal comes from indirect bounces.

#### **6b. Light importance sampling (`--light-sampling`)**

```
--light-sampling power
```

Selects how the renderer chooses which light to query per NEE event:

| Value | Behaviour | When to use |
|---|---|---|
| `all` | Sum over every light (original) | **Default** — always safe, backward compat |
| `power` | Sample one light ∝ `ApproximatePower` | Scenes with many lights of mixed brightness |
| `uniform` | Sample one light uniformly | Debug / baseline comparison against `power` |

With `all` the renderer fires `ShadowSamples` shadow rays per light per shading point — O(N·S). With `power` or `uniform` it fires shadow rays for one light, then divides by the sampling probability to remain unbiased — O(S). In a scene with 1 bright area light + 20 dim point lights, `power` converges substantially faster.

---

### 7. **PRACTICAL TIPS**

- **Start every scene in Preview.** Iterate composition and materials with `-s 64 -d 4 -S 1` until the image reads correctly. Only then promote to Standard or Final.
- **Spend budget on `-s` before `-S` or `-d`.** Pixel samples attack every noise source simultaneously (GI, shadows, specular); the other two target specific problems.
- **`-d 4` is the sweet spot for Preview** because Russian Roulette in normal scenes activates at exactly 4 bounces — beyond that you're relying on RR anyway.
- **Do not combine high `-s` with high `-S` without reason.** `-s 1024 -S 16` is almost never a good trade. `-s 1024 -S 4` usually matches it visually at ¼ the cost.
- **Glass-heavy scenes are the only legitimate reason to go past `-d 8`.** Scale `-d` to `16` or `20` only when you see unexpectedly black interiors in stacked dielectrics.
- **Reproducibility in CI.** The `-s`/`-d`/`-S` values in `.github/workflows/*.yml` should be a specific named profile (typically Preview or a trimmed Standard) — not ad-hoc values.

---

### 8. **TIPS FOR PARTICIPATING MEDIA (FOG / SMOKE)**

3D-Ray supports four global medium types — `homogeneous`, `height_fog`,
`procedural`, `grid` — plus phase functions `isotropic`, `hg`, `rayleigh`,
`double_hg`, `schlick`. Each type has very different cost and noise
characteristics.

- **Homogeneous and height_fog are "free"** relative to normal rendering:
  transmittance has a closed form, no delta tracking required. A Preview is
  already usable; Standard is almost always sufficient.
- **Procedural (Perlin fBm) and grid use delta tracking** (Woodcock) + ratio
  tracking: noisier by construction, especially in dense regions.
  - Preview shows obvious grain — fine for composition work.
  - For publication-ready images aim for `-s 576` (24×24) or `-s 1024`.
  - If noise concentrates in the light cone, raise `-s` (not `-S`).
- **Firefly clamp with dense fog.** Media with high `sigma_s` and `-d 8+`
  occasionally produce rare bright spikes that survive the default `-C 10`.
  Lower to `-C 5` or `-C 3` without hesitation: you lose little dynamic
  range and gain a much cleaner image.
- **`soft_radius` on point/spot lights inside a medium.** When a participating
  medium is active, the 1/d² attenuation of a `point`/`spot` light diverges at
  scattering events near the emitter, producing isolated firefly pixels that
  more samples cannot smooth out. Set `soft_radius` on those lights to a value
  approximating the physical bulb size (e.g. `0.10`–`0.25`): the attenuation
  denominator is clamped to `max(d², r²)`, the spike is removed, and the look
  at `d ≥ r` is unchanged. Default `0` = unclamped (original behaviour). See
  `scene-reference.md` §8.
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
  `data:`). Grid resolution does not affect render cost — only parse time
  and memory footprint.

**Recommended profile for volumetric showcases:**
```bash
# Volumetric Preview (composition check, ~30-60 s)
RayTracer -i scene.yaml -w 400 -H 225 -s 64 -d 4 -S 1

# Volumetric Standard (review; delta-tracking still a bit noisy)
RayTracer -i scene.yaml -w 800 -H 450 -s 400 -d 6 -S 1

# Volumetric Final (publication-ready cleanliness)
RayTracer -i scene.yaml -w 1920 -H 1080 -s 1024 -d 8 -S 4 -C 5
```

---

### 9. **RELATED DOCUMENTATION**

- [`docs/technical/path-tracing-and-lighting.md`](../technical/path-tracing-and-lighting.md) — internals of the path tracer, NEE, and Russian Roulette.
- [`docs/technical/rendering-pipeline.md`](../technical/rendering-pipeline.md) — pipeline overview.
- [`docs/reference/scene-reference.md`](./scene-reference.md) — scene YAML schema.
