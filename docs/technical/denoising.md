# Denoising — Feature-Guided Monte Carlo Noise Removal

The denoiser removes residual Monte Carlo noise from the **linear HDR
beauty** before the display transform (exposure → ACES → gamma), guided by
auxiliary buffers (AOVs) the renderer captures during the same render pass.
It is implemented entirely in managed C# (`src/RayTracer/Denoising/`), SIMD
(`Vector<float>` / `Vector256`) and parallel, with no native dependencies.

CLI: `--denoiser none|nlm|nfor` (+ `--denoise-quality fast|high`); the
`draft*`, `medium*` and `final-fast*` quality presets enable `nfor` by
default. `--aov albedo,normal,depth,beauty,variance` writes the guide
buffers as PFM files. See the [rendering profiles](../reference/rendering-profiles.md)
for the preset interaction.

## 1. Data captured during rendering

`Renderer.Render(w, h, RenderCaptureOptions)` accumulates, next to the
normal pixel sum (which stays bit-identical — capture adds side
accumulators and draws no extra randomness):

| Buffer | Content |
|---|---|
| `Beauty` | linear HDR mean (pre-exposure, pre-tonemap) |
| `BeautyA/B` | the same radiance split into the means of the **even/odd sample halves** |
| `AlbedoA/B` | surface albedo guide at the first non-delta hit |
| `NormalA/B` | world-space shading normal (post normal/bump map) at the first non-delta hit |
| `DepthA/B` | world-space distance of the first camera hit (−1 = sky) |

**First-non-delta-hit rule.** Depth is recorded at the first surface the
camera ray hits, regardless of specularity (a mirror pixel's depth is the
mirror's distance). Albedo and normal follow perfect-specular (delta)
chains: each delta bounce multiplies its tint into a running albedo weight,
and the first rough/diffuse surface commits — so a mirror pixel's albedo is
the mirror tint times what it reflects, and a glass pixel's guides describe
the scene behind the glass. Environment misses commit the (clamped) sky
colour with a zero normal; medium scattering events commit featureless
white. Paths that end inside a specular chain (Russian-Roulette kill, depth
exhaustion, emissive hit) fall back to the last surface seen.

**Dual-buffer variance.** With n samples, half A holds the even sample
indices (⌈n/2⌉ samples) and half B the odd ones. The per-pixel variance of
the full mean is estimated as `Var ≈ ((Ā−B̄)/2)²`, stabilised with a 7×7
binomial smooth and floored at `1e-5·mean²`. With `--sampler prng` the
halves are independent and the estimate is unbiased; with the default Sobol
sampler the even/odd subsequences of one Owen-scrambled sequence are
*anti-correlated*, so the estimate **overstates** the true variance — the
selection stage compensates (see §5). This same buffer layer is the
foundation for future adaptive sampling.

## 2. Pipeline

```
RenderBuffers ──► depth normalisation ──► feature prefiltering ──► variance
                                                                      │
              ┌───────────────────────────────────────────────────────┘
              ▼
   nlm:  joint NL-means, cross-filtered halves ──────────────► linear beauty
   nfor: NL-means-weighted first-order regression (per k) ──► candidate set
         + unfiltered mean ──► per-pixel MSE selection ─────► linear beauty
```

Depth is normalised to a resolution- and scene-scale-independent [0,1]
feature: distances clamp to a far plane at 1.05 × the 99th percentile of
finite depth; sky pixels sit exactly at the far plane.

## 3. Feature prefiltering

The raw AOVs are themselves Monte Carlo estimates (anti-aliased, DOF-blurred,
noisy behind glass). Each feature is NL-means filtered (search radius 5,
patch radius 3, k = 1) using its **own** dual-buffer variance, and
**cross-filtered**: weights computed from half A average half B and vice
versa, so the cleaned feature carries no self-correlated noise.

## 4. NL-means weights

All weights use the variance-cancelled patch distance (per channel):

```
d(p,q) = ((u_p − u_q)² − (σ²_p + min(σ²_p, σ²_q))) / (ε + σ²_p + σ²_q)
w(p,q) = exp(−max(0, d̄_patch / k²))
```

The expected value of `(u_p−u_q)²` for two equally noisy pixels with the
same true mean is `σ²_p+σ²_q`; subtracting the cancellation term makes the
distance ≈ 0 for "same signal, different noise" pairs and the normalisation
makes k a unitless filter-strength knob.

**Offset decomposition.** The engine never loops per-pixel × per-neighbour ×
per-patch. Each of the (2R+1)² window offsets is processed as O(N) plane
sweeps: SIMD pointwise distance row between the image and its shifted copy,
separable running-sum patch box average, then vectorised weight +
accumulation using a Schraudolph fast-exp (≈2 % relative error — irrelevant
for filter weights) with an `exp(−10)` cutoff. Rows are processed in
parallel with no synchronisation (each thread owns its output rows).

**`--denoiser nlm`** stops here: the colour distance plus pointwise
prefiltered-feature distances (albedo/normal/depth with fixed bandwidths)
form a joint filter; weights from half B average half A and vice versa, and
the two filtered halves recombine sample-weighted.

## 5. `--denoiser nfor` — first-order regression

For window centres on a stride-2 grid, the NL-means colour weights of the
opposite half drive a weighted least-squares fit of the window's colours
against the prefiltered features:

```
f(q) = [1, Δalbedo(3), Δnormal(3), Δdepth(1)]      (8 coefficients)
β = argmin Σ_q w(p,q) · (colour(q) − βᵀ f(q))²
```

One 8×8 Gram matrix (Tikhonov-regularised, λ = 1e-3·trace/8, features
pre-scaled to unit global standard deviation) is shared by the three RGB
right-hand sides and solved with a hand-rolled Cholesky. Each solved window
predicts **all** of its pixels and splats `w·prediction` into a global
accumulator ("collaborative" reconstruction — overlapping windows average
out, and the stride cuts solves 4×). Everything is cross-filtered: weights
from half B regress half A and vice versa, so the fit never chases its own
noise. Tiles are processed in four checkerboard passes so the splats of
concurrently processed tiles never overlap; the Gram accumulation and splat
dot products run on `Vector256` lanes (8 floats = exactly one feature
vector).

Where the regression beats the plain weighted average: any signal the
features *do* explain — texture detail under noise, normal-driven shading
gradients, depth-driven DOF edges — is reconstructed instead of blurred.

**Candidate selection.** Per quality level the regression runs at one
(`fast`, k = 0.7, R = 7) or two (`high`, k ∈ {0.5, 1.0}, R = 9) filter
strengths. The per-pixel MSE of each candidate is estimated against the
**noisy opposite half** — `E[(F_A − B)²] = MSE(F_A) + Var[B]`, so
subtracting the variance yields an estimate that *sees bias*, unlike the
half-disagreement `|F_A − F_B|²` (which only measures variance and would
happily select a systematically wrong candidate). The **unfiltered mean is
always part of the candidate set** (its MSE is exactly its variance): where
no feature explains the signal — contact shadows, caustics — any
feature-blind filter is biased, and the safety-net candidate preserves the
original pixels instead. A *selection margin* charges the filtered
candidates a fraction of the variance when the Sobol sampler is active,
compensating the anti-correlation bias of §1 — calibrated so near-converged
Sobol renders never regress while low-spp renders keep their full gains.
The per-pixel argmin maps are box-softened before blending, avoiding hard
selection seams. Regression output is clamped non-negative (radiance).

The denoised buffer then goes through the *identical* display transform as
the unfiltered path (`Renderer.ToneMapToDisplay`), and replaces the beauty
in the `--aov beauty` output.

## 6. Memory and performance

Capture adds ~24 float planes ≈ 96 B/pixel: ≈ 200 MB at 1080p, ≈ 800 MB at
4K, plus transient filter scratch. Denoise wall times measured on a 4-core
2.8 GHz container at 1920×1080: `nlm` ≈ 9 s, `nfor fast` ≈ 13 s,
`nfor high` ≈ 22 s — the cost scales linearly with cores (a typical 8–16
core workstation lands at a fraction of that) and is independent of spp.
Capture overhead in the render loop is below measurement noise, and the
default no-capture path is bit-identical to the pre-denoiser renderer.

## 7. Known limitations

- **Feature-blind transport** (thin contact shadows, caustics, volumetric
  glow) cannot be guided; the safety-net candidate keeps such regions at
  their original noise level rather than biasing them.
- **1 spp** renders carry no dual-buffer information (half B mirrors half A,
  variance reads zero) — the denoiser degenerates to a near no-op.
- With the Sobol sampler the variance estimate is conservative by
  construction; gains at high sample counts are intentionally damped by the
  selection margin.
- Media regions commit featureless guides; denoising there falls back to
  pure colour similarity.
