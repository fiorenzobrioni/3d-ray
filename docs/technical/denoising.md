# Denoising: Feature-Guided Monte Carlo Noise Removal

The denoiser removes residual Monte Carlo noise from the **linear HDR
beauty** before the display transform (exposure → view transform →
encoding), guided by auxiliary buffers (AOVs) the renderer captures during
the same render pass. Two families live in `src/RayTracer/Denoising/`:

- **`oidn`**: Intel Open Image Denoise 2.2 (Apache-2.0), a neural network
  trained on path-traced images, through a minimal binding of its C API
  (§6). The native library ships with the build for Linux x64, Windows x64
  and macOS (x64 and arm64).
- **`nfor`** and **`nlm`**: managed C#, SIMD (`Vector<float>` /
  `Vector256`) and parallel, no native code (§3-§5).

CLI: `--denoiser none|nlm|nfor|oidn` (+ `--denoise-quality fast|high`); the
`draft*`, `standard*` and `pre-final*` quality presets enable `oidn` by
default, `nfor` where the native library does not load (a platform the
build does not ship it for; an explicit `--denoiser oidn` is an error
there). `--aov albedo,normal,depth,beauty,variance` writes the guide
buffers as separate PFM files, as separate EXR files (`--aov-format exr`),
or (when the main output is `-o *.exr`) as layers of that same multilayer
EXR. See the [rendering profiles](../reference/rendering-profiles.md)
for the preset interaction.

## 1. Data captured during rendering

`Renderer.Render(w, h, RenderCaptureOptions)` accumulates, next to the
normal pixel sum (which stays bit-identical: capture adds side
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
and the first rough/diffuse surface commits, so a mirror pixel's albedo is
the mirror tint times what it reflects, and a glass pixel's guides describe
the scene behind the glass. Environment misses commit the (clamped) sky
colour with a zero normal; medium scattering events commit featureless
white. Paths that end inside a specular chain (Russian-Roulette kill, depth
exhaustion, emissive hit) fall back to the last surface seen.

**Dual-buffer variance.** With n samples, half A holds the first ⌈n/2⌉
samples of the pixel's sequence and half B the rest. The per-pixel variance
of the full mean is estimated as `Var ≈ ((Ā−B̄)/2)²` (`nA × nB/n² × (Ā−B̄)²` when
the halves differ), stabilised with a 7×7 binomial smooth and floored at
`1e-5 × mean²` for the filters. How the halves are cut matters with a
stratified sampler. Measured over 24 independent seeds
(`DualBufferVarianceTests`), the estimate over the true variance of the
pixel mean:

| sampler | halves | 16 spp | 64 spp | 256 spp |
|---|---|---|---|---|
| Sobol | first / second half of the sequence | 1.14 | 1.13 | 1.08 |
| Sobol | even / odd samples (before) | 2.37 | 3.01 | |
| PRNG (jittered grid) | even / odd cells | 1.24 | 1.44 | |

Each half of a Sobol sequence is itself a stratified net, so the two halves
are nearly independent estimates; the even and odd samples instead fill
each other's gaps (in the first dimension they cover the two halves of the
pixel), so they disagree far more than the full mean varies. The PRNG's
jittered grid has the same property for any split of its cells. With path
guiding each training pass is cut the same way, and the bidirectional
integrators cut their iterations. Adaptive sampling (§8) keeps the halves the
two halves of the sequence as it doubles the pixel's count.

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

**Extreme highlights.** Before the variance is estimated, what the
brightest pixels hold above 8 times the image's 99th-percentile luminance
is set aside (`NforDenoiser.SplitHighlights`, per half, scaling the colour
by luminance so the hue stays): the filter sees them clamped, and the excess
is added back unfiltered at the end. The image keeps its energy and a
highlight its size. Without it, a pixel thousands of times brighter than
its neighbours (the sun in a mirror, a small light) dominated every
regression window that held it and came out as a square with a dark rim.
The factor was 32 until Phase 9: out-of-focus lights (bokeh,
`showcases/camera-physical.yaml`) still came out as squares, relMSE 0.51
against the noisy image's 0.055 at 16 spp; at 8 it is 0.11. By definition
the split touches less than 1% of the pixels.

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
accumulation using a Schraudolph fast-exp (≈2 % relative error, irrelevant
for filter weights) with an `exp(−10)` cutoff. Rows are processed in
parallel with no synchronisation (each thread owns its output rows).

**`--denoiser nlm`** stops here: the colour distance plus pointwise
prefiltered-feature distances (albedo/normal/depth with fixed bandwidths)
form a joint filter; weights from half B average half A and vice versa, and
the two filtered halves recombine sample-weighted.

## 5. `--denoiser nfor`: first-order regression

For window centres on a stride-2 grid, the NL-means colour weights of the
opposite half drive a weighted least-squares fit of the window's colours
against the prefiltered features:

```
f(q) = [1, Δalbedo(3), Δnormal(3), Δdepth(1)]      (8 coefficients)
β = argmin Σ_q w(p,q) × (colour(q) − βᵀ f(q))²
```

One 8×8 Gram matrix (Tikhonov-regularised, λ = 1e-3 × trace/8, features
pre-scaled to unit global standard deviation) is shared by the three RGB
right-hand sides and solved with a hand-rolled Cholesky. Each solved window
predicts **all** of its pixels and splats `w × prediction` into a global
accumulator ("collaborative" reconstruction: overlapping windows average
out, and the stride cuts solves 4×). Everything is cross-filtered: weights
from half B regress half A and vice versa, so the fit never chases its own
noise. Tiles are processed in four checkerboard passes so the splats of
concurrently processed tiles never overlap; the Gram accumulation and splat
dot products run on `Vector256` lanes (8 floats = exactly one feature
vector).

Where the regression beats the plain weighted average: any signal the
features *do* explain (texture detail under noise, normal-driven shading
gradients, depth-driven DOF edges) is reconstructed instead of blurred.

**Candidate selection.** Per quality level the regression runs at one
(`fast`, k = 0.7, R = 7) or two (`high`, k ∈ {0.5, 1.0}, R = 9) filter
strengths. The per-pixel MSE of each candidate is estimated against the
**noisy opposite half**: `E[(F_A − B)²] = MSE(F_A) + Var[B]`, so
subtracting the variance yields an estimate that *sees bias*, unlike the
half-disagreement `|F_A − F_B|²` (which only measures variance and would
happily select a systematically wrong candidate). The **unfiltered mean is
always part of the candidate set** (its MSE is exactly its variance): where
no feature explains the signal (contact shadows, caustics) any
feature-blind filter is biased, and the safety-net candidate preserves the
original pixels instead. The variance enters the comparison twice (the
unfiltered candidate's MSE, and subtracted from the filtered ones), so an
overstated variance favours filtering: with the even/odd halves of §1 a
*selection margin* charged the filtered candidates half the variance to
compensate. With the halves of the sequence it is gone (measured on two
scenes at 4-64 spp, no margin was best or equal).
The per-pixel argmin maps are box-softened before blending, avoiding hard
selection seams. A filtered candidate that overshoots below zero in any
channel at a pixel (regression extrapolated across an HDR edge) is out of
the running there, and a pixel whose blend still overshoots keeps its
unfiltered mean: clamping the negative channel alone tinted it, a blue seam
along a sunlit silhouette.

The denoised buffer then goes through the *identical* display transform as
the unfiltered path (`Renderer.ToneMapToDisplay`), and replaces the beauty
in the `--aov beauty` output and in the `R,G,B` channels of a `-o *.exr`
scene-linear output.

## 6. `--denoiser oidn`: Intel Open Image Denoise

`OidnDenoiser` hands OIDN's "RT" filter the linear HDR beauty (the full mean,
`hdr = true`) with the albedo and normal guides (full means of the halves,
albedo clamped to [0,1], normals to [-1,1]). The guides follow the same
first-non-delta-hit rule as for NFOR (§1), which is what the OIDN
documentation asks for: a mirror or glass pixel carries the guides of what
it shows. They are first denoised by two RT filters of their own and the
main filter is told they are clean (`cleanAux`), as the documentation
recommends for noisy guides (depth of field, motion blur, glass, textures
finer than a pixel). Without that step a procedural marble finer than a
pixel, whose albedo is still noisy at 64 spp, came out with its noise kept
as detail: relMSE 0.0053 against 0.0014 with the prefiltered guides
(`showcases/texture-pro.yaml`). Prefiltering the guides with the
dual-buffer NL-means of §3 instead was no better (0.0015) and lost contrast
at 2 spp. `--denoise-quality high` picks OIDN's high-quality networks,
`fast` the balanced ones; OIDN 2.2 runs the same networks for both on the
CPU, so the two give the same image today.

**Binding** (`Denoising/Oidn.cs`): `DllImport` of the C API (device, filter,
shared float3 images pinned for the filter's lifetime, errors read back
after every commit and execute). The native libraries (`OpenImageDenoise`,
its core and CPU device modules, TBB) are the native assets of the
`SimpleImageIO` NuGet package, an MIT-licensed image library whose own code
the build leaves out: only OIDN and TBB are copied next to the
executable, also in the self-contained single-file publish. `OIDN_DIR`
points the loader at another OIDN 2.x installation (its folder, or its
`lib`/`bin`), for a newer release or a platform the package lacks.
`Oidn.IsAvailable` probes the library once by committing a CPU device.

**Behaviour.** The network was trained on path-traced images and has no
explicit notion of variance: it removes what looks like Monte Carlo noise.
Two consequences, both measured below: isolated bright samples that are
real but unconverged (caustic fireflies through glass) go with their
energy (the image can be a few percent darker than a converged one), and
where the noise is explained by a guide (the 8-spp coverage of a texture
3 pixels across, identical in the albedo), it is kept as texture
(`DenoiserDetailTests`: contrast kept, error equal to the noisy image's,
where NFOR halves it).

## 7. Comparison

320×180, the denoiser's high quality, relMSE against 1024 spp without
denoiser (`src/Tools/ImageCompare`); mean ratio in brackets where it moves.

| scene | spp | none | nfor | oidn |
|---|---|---|---|---|
| `review/guiding-room` (sunlit room) | 4 | 0.116 | 0.0024 | 0.00085 |
| | 16 | 0.0246 | 0.00053 | 0.00041 |
| | 64 | 0.0048 | 0.00032 | 0.00027 |
| `validation/cornell-lambert` | 4 | 0.0198 | 0.0062 | 0.00082 |
| | 16 | 0.0040 | 0.0012 | 0.00047 |
| | 64 | 0.00046 | 0.00017 | 0.00014 |
| `showcases/texture-pro` (fine procedural textures) | 4 | 0.160 | 0.0068 | 0.0040 |
| | 16 | 0.0374 | 0.0026 | 0.0020 |
| | 64 | 0.0087 | 0.0019 | 0.0014 |
| `showcases/camera-physical` (bokeh) | 4 | 0.172 | 0.144 | 0.013 (0.89) |
| | 16 | 0.055 | 0.108 | 0.014 |
| | 64 | 0.012 | 0.133 | 0.0043 |
| `showcases/extrusion` (sun, glass) | 4 | 0.689 | 0.536 | 0.0057 (0.96) |
| | 16 | 0.279 | 0.192 | 0.0044 (0.96) |
| | 64 | 0.050 | 0.030 | 0.0037 (0.97) |

OIDN has the lowest error everywhere and is the fastest: 0.25 s for
480×270 where NFOR high takes 2.1 s, about 4 times faster at 1080p (§9). The darker means are the caustic
fireflies of §6, still present in the 1024-spp references. NFOR stays as
the managed fallback and for scenes where its explicit variance model
helps (pixel-scale texture coverage, above). Its bokeh weakness (the
regression window turns out-of-focus lights into squares) is reduced by
the highlight split of §2 but not solved.

## 8. Adaptive sampling (`--adaptive on`)

The same halves drive adaptive sampling (`Rendering/AdaptiveSampling.cs`,
on by default from the `standard` presets up). Every pixel takes the first
`--adaptive-min-spp` samples of its Sobol sequence (default 4/√threshold
rounded up to a power of two: 64 at the default threshold 0.01, a stratified
net), then passes double the count of the pixels still noisy, up to `-s`.
After each pass a pixel's error is

```
e = Σ_c σ_c / (1e-4 + √(Σ_c I_c)),    σ_c = |Ā_c − B̄_c| × √(nA × nB) / n
```

with `I` the exposed mean (the exposure scales it, so the threshold is about
the noise of a display value through a square-root transfer) and `σ` the
standard error of the mean read from the halves. A pixel stops when it and
its eight neighbours are below `--adaptive-threshold` (stopped neighbours
count as below): the halves of one pixel are a noisy estimate, and a single
pixel that happens to look converged does not stop alone. Before each pass
the samples taken become half A and the pass fills half B, so the halves stay
the first and second half of the pixel's sequence (§1); a last pass cut by
`-s` leaves unequal halves, whose counts go to `RenderBuffers.HalfCountA`.
Ray differentials keep the footprint of the full budget.

Path tracing with the Sobol sampler only: refused with `--reference` (a
pixel that stops on its own samples biases the mean slightly), `--guiding`
(the training passes are global and their inverse-variance weights assume
uniform counts), `--integrator bdpt|vcm` (light tracing reaches every pixel)
and `--sampler prng`. A preset's default yields silently, an explicit flag is
an error.

Measured at 160×90, budget 512, against 4096-spp references:

| scene | uniform 512 | adaptive 0.01 | samples | time |
|---|---|---|---|---|
| `validation/cornell-lambert` | relMSE 7.0e-5 | 7.0e-5 | 246 spp | −13% |
| `showcases/texture-pro` | 1.03e-3 | 1.03e-3 | 471 spp | −6% |
| `showcases/extrusion` | 2.87e-2 | 2.87e-2 | 327 spp | −2% to −10% |
| `review/guiding-room` | 3.9e-4 | 3.9e-4 | 495 spp | 0% |

And `-q final-tiny` on the Cornell box: 394 spp on average instead of 1024,
9% less time, relMSE 2.3e-6 against the uniform render, same mean. The
pixels that converge first are the cheap ones (background, sky), so the
time saved is far below the samples saved; a scene noisy everywhere (the
sunlit room) keeps nearly every pixel to the end, at no cost. At threshold
0.02 the Cornell box takes 169 spp (−27% time) for relMSE 8e-5.

## 9. Memory and performance

Capture adds ~24 float planes ≈ 96 B/pixel: ≈ 200 MB at 1080p, ≈ 800 MB at
4K, plus transient filter scratch (OIDN: three interleaved RGB copies).
Adaptive sampling keeps a per-pixel accumulator between passes, about
100 B/pixel (200 MB at 1080p, 830 MB at 4K). Denoise wall times measured on a 4-core
2.8 GHz container at 1920×1080: `nlm` ≈ 9 s, `nfor fast` ≈ 13 s,
`nfor high` ≈ 22 s. Measured again in Phase 9 on a 4-core container of the
same class: `nfor high` 41 s, `oidn` 8-11 s (three networks: the two guides
and the beauty). The cost scales linearly with cores and is
independent of spp. Capture overhead in the render loop is below
measurement noise, and the default no-capture path is bit-identical to the
pre-denoiser renderer.

## 10. Known limitations

- **Feature-blind transport** (thin contact shadows, caustics, volumetric
  glow) cannot be guided; NFOR's safety-net candidate keeps such regions at
  their original noise level, OIDN smooths them and drops unconverged
  caustic fireflies with their energy.
- **1 spp** renders carry no dual-buffer information (half B mirrors half A,
  variance reads zero): NFOR degenerates to a near no-op (OIDN does not use
  the variance).
- Media regions commit featureless guides; denoising there falls back to
  pure colour similarity.
- **HDR edges with NFOR.** Along a sunlit edge against a much darker
  background a thin seam of slightly tinted pixels can remain, and
  out-of-focus lights spread into squares: the first-order regression still
  overshoots there. OIDN, the presets' default, has neither.
