# Subsurface Scattering — Random Walk Integrator

3D-Ray's subsurface scattering is a brute-force volumetric random walk, dispatched on refraction events into geometry bound to a scattering `homogeneous` medium. There is no "diffusion approximation" shortcut, no dipole, no separable kernel — every photon that enters the volume is transported by the full free-flight estimator until it escapes the boundary, gets killed by Russian Roulette, or hits the hard volume-bounce cap.

This is the same recipe Cycles ships as `random_walk_v2` and Arnold ships as the default `randomwalk` mode of `standard_surface`. The reason it has displaced the dipole pretty much everywhere in production: it is **the** path-traced integrator for arbitrary geometry, handles thin features and curvature correctly, energy-conserves by construction, and shares its sampling machinery with the existing volumetric path tracer.

## Derivation

The volumetric rendering equation (PBRT §15.1) describes the radiance along a ray inside a participating medium with absorption coefficient σ_a, scattering coefficient σ_s, and phase function f_p:

```
L(p, ω) = T_r(t_b) · L(p + t_b ω, ω)
       + ∫₀^t_b T_r(t) · [σ_a L_e + σ_s L_s] dt
```

where T_r(t) = exp(-σ_t · t) is the transmittance and σ_t = σ_a + σ_s is the extinction. For a non-emissive medium (the SSS case — the surface emits, not the volume) L_e = 0 and we Monte-Carlo-estimate the integral by:

1. Sampling a free-flight distance t from the exponential pdf σ_t · exp(-σ_t · t).
2. If t ≥ t_boundary, the ray escapes — return the transmittance-weighted boundary radiance.
3. Otherwise a scattering event occurs at p + t · ω; sample a new direction ω′ from f_p; restart with throughput multiplied by σ_s / σ_t (single-scatter albedo).

The accumulated throughput on escape is the surface BTDF "see-through" factor of the SSS volume, applied to the radiance crossing the exit boundary.

## Hero-wavelength MIS

The σ-coefficients are RGB-valued (one σ_t per channel). A naive 3-channel parallel walk fires 3 mean-free-paths' worth of work per scatter event; an unbiased single-channel walk picks a channel per scatter and discards the other two, but underestimates the rare events that would have escaped along a different channel.

The hero-wavelength estimator (Wilkie et al. 2014, popularised by Cycles 2.7) splits the difference. Each scatter event picks a hero channel `c` with probability proportional to `throughput[c]`; the free-flight distance is drawn from σ_t[c]; and the throughput is then balance-weighted across all 3 channels:

```
β  *=  σ_s · exp(-σ_t · t) / Σ_c q[c] · σ_t[c] · exp(-σ_t[c] · t)
```

where `q[c] = throughput[c] / sum(throughput)` is the hero-pick weight. The result is spectrally unbiased: in the limit, the per-channel estimator matches the 3-channel parallel run, but in practice converges 2-3× faster because each walk amortises one path's worth of intersection cost across all three channels.

The phase function is HG with `g` from the medium (default 0). The HG-sample density matches the HG-eval density, so phase/pdf = 1 — no extra MIS factor on the scatter direction.

## Energy & Fresnel coupling

The entry Fresnel transmission is applied by the surface BSDF *before* the walk: the throughput passed to `RandomWalkSubsurface` is already `T_entry · viewBSDF`. The walk itself never sees the surface again until it tries to escape.

At a boundary hit during the walk, the surface BSDF is sampled to decide whether the photon:

- Refracts out (probability = Fresnel transmission). Walk terminates; the exit ray is fed back into `TraceRay` to continue accumulating radiance in world space.
- Reflects back (probability = Fresnel reflection / TIR). Walk continues with the reflected direction; bounce counter decrements as normal. This handles the polished-marble look — without it, all the light that hits the back of the surface at grazing angle would leak out instead of refracting back into the volume.

## Russian Roulette + max-bounces cap

In-walk Russian Roulette kicks in at `b >= RrStartBounce` (default 3, configurable via `--sss-quality`). The survival probability is `q = max(β.X, β.Y, β.Z)` clamped to `[0.05, 0.95]`. On survival the throughput is divided by `q` to remain unbiased; on death the walk returns the radiance accumulated so far.

The max-bounces hard cap (default 64) is a guard rail against worst-case low-albedo paths that RR would terminate eventually but not fast enough to bound the per-pixel cost. On a milk-like medium (albedo ≈ 1) most walks terminate by RR around bounce 30-40; on a dense waxy medium the cap takes over.

## Indirect firefly clamp inside the walk

Deep scatter events produce dim indirect contributions over a wide directional range — exactly the configuration that produces fireflies on poorly-sampled lights at depth. The walk applies a depth-aware ramp:

```
clamp(b) = _indirectMaxSampleRadiance / (1 + 0.1 · b)
```

so the bounce-2 NEE is clamped at the global indirect limit, but the bounce-32 NEE is clamped much more tightly (≈ 25% of the global). This matches Cycles' `clamp_walk_volume` and Arnold's `indirect_specular`-style depth-aware clamp.

## CLI knobs

| Flag | Default | Notes |
|---|---|---|
| `--sss-mode auto\|off` | `auto` | `off` declasses pushed media to absorption-only (legacy Beer-Lambert), useful for preview / A/B. |
| `--sss-quality preview\|normal\|high` | inherits from `-q` | One-shot configuration of MaxVolumeBounces / RrStartBounce / NeeInsideWalk. |
| `--max-volume-bounces <n>` | preset-dependent (16/64/256) | Hard cap on walk depth. Trades cost against energy on dense media. |

Quality presets follow the same Preview/Normal/High convention as the main `--quality` flag:

| Preset | MaxVolumeBounces | RrStartBounce | NeeInsideWalk | Tier |
|---|---|---|---|---|
| `preview` | 16 | 1 | off | Composition / sanity check. Light enters only via boundary refraction. |
| `normal`  | 64 | 3 | on  | Production default. NEE samples lights at every internal scatter event. |
| `high`    | 256 | 6 | on  | Portfolio / hero shots. Deep walks fully resolved. |

When the user passes `-q draft-small`, the SSS tier defaults to `preview`. `-q medium`/`-q final`/`-q ultra` default to `normal`/`high`/`high`. An explicit `--sss-quality` always wins over the inferred tier.

## Jensen 2001 preset reference

Reproducible σ-coefficients for common materials (`σ_t = σ_a + σ_s`, units 1/world-unit, assuming 1 wu ≈ 1 m):

| Material | σ_a (R, G, B) | σ_s (R, G, B) | Phase | Notes |
|---|---|---|---|---|
| Marble (white) | 0.0021, 0.0041, 0.0071 | 2.19, 2.62, 3.00 | HG, g=0 | Carrara look — slightly warm |
| Skin1 (Caucasian) | 0.032, 0.17, 0.48 | 9.25, 11.0, 12.6 | HG, g=0.92 | Strong forward HG, red-dominant |
| Skin2 (Darker) | 0.063, 0.21, 0.40 | 6.4, 8.9, 10.5 | HG, g=0.92 | Higher melanin |
| Whole milk | 0.0011, 0.0024, 0.014 | 2.55, 3.21, 3.77 | iso | Slight yellow |
| Cream (heavy) | 0.0002, 0.00028, 0.00136 | 7.38, 5.47, 3.15 | iso | Strong forward extinction, "creamy" feel |
| Wax / candle | 0.012, 0.012, 0.022 | 4.0, 4.0, 3.6 | HG, g=0.4 | Translucent, yellow shift |
| Jade | 0.027, 0.0078, 0.043 | 3.0, 4.5, 4.0 | HG, g=0.4 | Green-shifted |
| Ketchup | 0.061, 0.97, 1.45 | 0.18, 0.07, 0.03 | iso | High σ_a in G/B → deep red |
| Apple (red) | 0.0030, 0.0034, 0.0460 | 2.29, 2.39, 1.97 | iso | Subtle warm bleed |

These are the canonical post-Jensen values you'll find rendered in Mitsuba / PBRT documentation. For other materials, derive σ from the published reduced σ_s′ via `σ_s = σ_s′ / (1 − g)` and pick `g` per the medium's typical anisotropy (skin/wax ≈ 0.9 forward, milk/marble ≈ 0).

## Migration from legacy Disney `subsurface`

Pre-Phase-2 scenes that set `subsurface > 0` on Disney materials are no longer SSS — the parameter is parsed but ignored, and the loader emits a warning. Replace it with an `interior_medium` binding:

```yaml
# BEFORE (Phase 1)
materials:
  - id: marble
    type: disney
    color: [1.0, 1.0, 1.0]
    subsurface: 0.8                     # legacy "flat HK" lobe blend

# AFTER (Phase 3+)
mediums:
  - id: marble_int
    type: homogeneous
    sigma_a: [0.0021, 0.0041, 0.0071]
    sigma_s: [2.19, 2.62, 3.00]

materials:
  - id: marble_surface
    type: disney
    color: [1.0, 1.0, 1.0]
    spec_trans: 1.0
    ior: 1.5

entities:
  - type: sphere
    material: marble_surface
    interior_medium: marble_int
```

The standalone tool `src/Tools/MigrateFakeSss/` automates this rewrite for a YAML tree.
