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
| `-C` / `--clamp` | `100` (firefly clamp) | `Renderer.DefaultMaxSampleRadiance` |

The defaults target **fast iteration**, not final quality. Use the Preview profile as the minimum viable "nice-looking" render; use Standard or Final when you need to publish.

---

### 3. **UNDERSTANDING `-s` (PIXEL SAMPLES)**

The engine performs **stratified sampling** on a √N × √N grid per pixel. Passing `-s 16` gives you a 4×4 grid (16 samples); `-s 256` gives you a 16×16 grid (256 samples). Each cell produces one jittered sample.

**Perfect squares are free.** If you pass a non-square value the engine rounds √N up, so `-s 100` becomes 10×10 = 100 (exact), but `-s 15` silently becomes 4×4 = 16. To control cost exactly, prefer: `1, 4, 9, 16, 25, 36, 49, 64, 100, 144, 196, 256, 400, 576, 784, 1024, 1600`.

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

**Default:** `100`. High enough to preserve highlights on emissive elements; low enough to kill numerical spikes.

**When to lower `-C`:**
- Dense fog / thick homogeneous media + forced high `-d`.
- Scenes with many small bright emissives seen through glass.
- Try `-C 25` (aggressive) or `-C 15` (heavy). You lose some HDR dynamic range in the hottest highlights but gain cleaner shadows and softer penumbras at extreme bounces.

**When to raise `-C`:**
- Strongly sun-lit HDRIs where the sun disk is showing up dimmer than expected.
- Try `-C 500` or disable clamping effectively with a very high value. The tradeoff is potential fireflies.

The clamp uses **luminance-preserving scaling**, so it does not shift hue on bright highlights — only brightness.

---

### 7. **PRACTICAL TIPS**

- **Start every scene in Preview.** Iterate composition and materials with `-s 64 -d 4 -S 1` until the image reads correctly. Only then promote to Standard or Final.
- **Spend budget on `-s` before `-S` or `-d`.** Pixel samples attack every noise source simultaneously (GI, shadows, specular); the other two target specific problems.
- **`-d 4` is the sweet spot for Preview** because Russian Roulette in normal scenes activates at exactly 4 bounces — beyond that you're relying on RR anyway.
- **Do not combine high `-s` with high `-S` without reason.** `-s 1024 -S 16` is almost never a good trade. `-s 1024 -S 4` usually matches it visually at ¼ the cost.
- **Glass-heavy scenes are the only legitimate reason to go past `-d 8`.** Scale `-d` to `16` or `20` only when you see unexpectedly black interiors in stacked dielectrics.
- **Reproducibility in CI.** The `-s`/`-d`/`-S` values in `.github/workflows/*.yml` should be a specific named profile (typically Preview or a trimmed Standard) — not ad-hoc values.

---

### 8. **RELATED DOCUMENTATION**

- [`docs/technical/path-tracing-and-lighting.md`](../technical/path-tracing-and-lighting.md) — internals of the path tracer, NEE, and Russian Roulette.
- [`docs/technical/rendering-pipeline.md`](../technical/rendering-pipeline.md) — pipeline overview.
- [`docs/reference/scene-reference.md`](./scene-reference.md) — scene YAML schema.
