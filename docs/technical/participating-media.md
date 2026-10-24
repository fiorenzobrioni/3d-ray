# Participating Media

How 3D-Ray samples fog, smoke, the atmosphere and the interior of
subsurface materials. Every estimator here is unbiased: given enough samples
the image converges to the solution of the volume rendering equation, in
every colour channel, with no cap on the number of steps along a ray.

Code: `src/RayTracer/Volumetrics/` (media and phase functions),
`src/RayTracer/Rendering/PathIntegrator.cs` (the path loop),
`src/RayTracer/Rendering/Equiangular.cs`, `src/RayTracer/Geometry/ShadowRay.cs`
(shadow rays through media), `src/RayTracer/Rendering/RandomWalkSss.cs`
(subsurface). Tests: `src/RayTracer.Tests/Validation/MediumTransportTests.cs`,
`SamplingRoutineTests.cs` (phase functions).

## 1. The interface

A medium answers two questions about a ray segment `[0, tMax)`:

- `Sample(ray, tMax, throughput)`: a free flight. It returns a `MediumEvent`:
  either a scattering event at `t`, with a weight that already holds σ_s
  over the density of sampling it, or no event before `tMax`, with the
  transmittance over the probability of getting there. A zero weight means
  absorbed. The event also names its phase function (a medium with several
  species picks one).
- `Transmittance(ray, tMax)`: the transmittance, exact or an unbiased
  estimate (shadow rays).

The path's throughput is passed to `Sample` because coloured media sample
by it (§3).

## 2. Analytic media

`homogeneous`, `height_fog` and `atmosphere` have a closed-form optical
depth τ(t) = ∫₀ᵗ σ_t ds (`AnalyticMedium`). The free flight inverts it: a
rate-1 exponential variate E gives the distance where τ_c(t) = E for the
chosen channel c.

- **Homogeneous**: τ = σ_t t, t = E / σ_t.
- **Height fog**: σ(y) = σ₀ exp(−(y − y0)/H). Along a ray the density is
  f₀ exp(−b s) with b = d_y / H, τ = σ f₀ (1 − e^{−bt}) / b and its inverse
  t = −ln(1 − bE/(σf₀)) / b (no event when the argument is not positive).
  Double precision and series near b = 0: the float form overflowed to ∞ × 0
  far below y0 and filled whole renders with NaN.
- **Atmosphere** (aerial perspective): air (Rayleigh, scale height 8 km),
  dust (Mie, 1.2 km) and ozone (a tent from 10 to 40 km) over a flat ground,
  with the coefficients of the physical skies. Each profile's integral over
  altitude has a closed form, so τ(t) = Σ_k σ_k (C_k(h(t)) − C_k(h₀)) / (dh/dt)
  is exact; its inverse is found by a safeguarded Newton iteration. At a
  scattering event the species (air or dust) is picked in proportion to the
  light it scatters, σ_s,k ⊙ h, and its own phase function is used: air
  scatters with the Rayleigh function, dust forward. The medium acts only on
  finite segments: the physical sky already holds the light of the whole
  atmosphere along an open ray, and the sun's disc its transmittance.

## 3. Coloured media: spectral MIS

With three channels of different extinction, one distance distribution
cannot follow all of them. The channel that drives the sampling is picked
in proportion to the path throughput, q_c = β_c / Σβ (the "hero" channel),
and the estimate divides by the mixture of the three densities:

```
scatter at t:  weight = σ_s(t) ⊙ Tr(t) / Σ_c q_c σ_t,c(t) Tr_c(t)
no event:      weight = Tr(tMax)        / Σ_c q_c Tr_c(tMax)
```

This is one-sample MIS with the balance heuristic over the channels: every
channel is unbiased and its weight is bounded by its albedo over q_c.
Channels that no longer carry light are never chosen.

## 4. Heterogeneous media: null collisions

`procedural` (Perlin fBm) and `grid` have no closed-form optical depth
(`TrackedMedium`). Candidate collisions are drawn against a majorant
σ̄ ≥ σ_t (grey: one rate for the three channels), and at each one the
light either scatters or passes a "null" particle of density
σ_n = σ̄ − σ_t (Miller, Georgiev and Jarosz 2019).

- **Distances: spectral tracking** (Kutz et al. 2017). With h the throughput
  times the walk's weight so far, the event is scattering with probability
  P_s = |σ_s ⊙ h| / (|σ_s ⊙ h| + |σ_n ⊙ h|), null otherwise; the weight is
  multiplied by σ_s / (σ̄ P_s) or σ_n / (σ̄ P_n). Absorption lives in the
  weights.
- **Transmittance: ratio tracking** (Novák et al. 2014): the product of
  (1 − σ_t / σ̄) over the collisions, at most 1.
- **No truncation.** A Russian roulette on the weight ends walks that have
  nothing left to carry. The old samplers stopped after 4096 collisions
  (biased on long rays) and weighted every channel with the chosen
  channel's σ_s / σ_t (biased for coloured media).
- **Random numbers.** The first collision uses the vertex's own stratified
  pair; the rest are "loose" draws outside the vertex's block of sampler
  dimensions, so their variable count never shifts the vertex's light
  sample and bounce onto other dimensions.

### Majorant grid

A voxel grid walks a coarse grid of majorants (about four voxels per cell,
at most 32 per axis) with a 3D DDA. Each majorant cell holds the largest
density its points can reconstruct to: the maximum of the voxels the
filter reads for any point in the cell, times, for the Catmull-Rom
tricubic, its overshoot bound. Per axis the Catmull-Rom weights' positive
part sums to at most P = 1.125 and the negative part to N = 0.125, so over
data in [0, M] three separable passes give at most (1 + 1.25³)/2 × M ≈ 1.477 M.
Thin and empty regions cost few collisions; the bound is never violated
(`TrackedMedium.Violations` counts violations; the tests require none).

## 5. Shadow rays through media

Next-event estimation opens a media context (`ShadowRay.BeginMedia`): the
path's medium stack and the global medium. The shadow ray is attenuated over
every stretch by the medium it is in, and switches media at every boundary
it crosses (a refractive solid, or an entity bound to a medium: entering
pushes the interior, leaving pops it), as the path does. A shadow ray that
leaves a transmissive receiver from its far side crosses the receiver's own
boundary first. Before, only the medium at the start was counted, and only
up to its own boundary: a fog volume between a surface and a light did not
shadow it.

## 6. Equiangular sampling

For a point or spot light (no phase-sampled ray can reach it) in an
analytic medium, the light scattered into a segment has two estimators:

- (A) the path's free flight, with next-event estimation at its scattering
  point, density p_ff(t) = Σ_c q_c σ_t,c Tr_c;
- (C) an equiangular distance toward the light (Kulla and Fajardo 2012),
  t = Δ + D tan(θ), θ uniform between the angles of the segment's ends,
  density p_eq(t) = D / ((θ_b − θ_a)(D² + (t − Δ)²)), with a light sample
  there.

They are combined by the balance heuristic over the distance, with the
light pick probabilities on both sides: A's light sample is weighted by
p_ff / (p_ff + p_eq) and C by the complement. Both are one more vertex, so
neither runs on the last segment a depth limit allows. On the validation
scene `fog-sphere-light` the error at equal samples fell 16×; a single
scattering test against quadrature matches to 0.03% with 3.8× less noise.

## 7. Phase functions

Isotropic, Henyey-Greenstein, two-lobe HG, Rayleigh and Schlick, each
sampled by the exact inverse of its CDF in cos θ from one stratified pair
(`Sample(wo, u)`): the first coordinate alone sets the angle,
monotonically, so stratification survives; the two-lobe function picks its
lobe with the first coordinate, rescaled. The pdf is the value, the weight
exactly 1 (chi-square tests in `SamplingRoutineTests`).

## 8. Subsurface (random walk)

A scattering homogeneous medium inside an entity is walked by
`RandomWalkSss.cs` with the same spectral MIS (§3), a light sample at every
bounce through the entity's boundary as a null interface, and a Russian
roulette on the full throughput after 8 bounces, with no cap. The material's
colour is the albedo the surface shows; see
[Subsurface Scattering](./subsurface-scattering.md) for the inversion to
the single-scattering albedo.

## 9. Validation

- `MediumTransportTests`: free flights unbiased per channel against
  quadrature, for three steering throughputs; ratio tracking unbiased; no
  majorant violation on random tricubic data; white furnaces (a scattering,
  non-absorbing medium in a white sky returns exactly 1) for a tricubic
  grid, procedural noise, height fog and the random walk; shadow rays
  through a fog box and out of it; equiangular against quadrature; the
  subsurface inversion against its colour.
- `validation/scenes/`: `fog-sphere-light` (global coloured fog, sphere and
  point light), `medium-chromatic` (strongly chromatic scattering sphere,
  the random walk), `smoke-grid` (heterogeneous coloured smoke on a grid),
  compared pixel by pixel with an independent volumetric path tracer: mean
  ratios 1.001, 0.998 and 1.000.
