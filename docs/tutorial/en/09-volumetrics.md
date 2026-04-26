# Chapter 9: Participating Media (Volumetrics)

Real air is not perfectly transparent. Fog scatters light, water absorbs
red wavelengths, smoke glows when caught in a beam. 3D-Ray supports
**four global medium types** (homogeneous, height fog, procedural, grid)
and **five phase functions** (isotropic, HG, Rayleigh, double-HG, Schlick),
enough to cover most practical cases: uniform fog, atmospheric haze,
clouds, localized smoke, sky.

---

## 9.1 What Are Participating Media?

In a vacuum, light travels in straight lines forever. In a participating
medium (air, water, smoke), two things happen:

- **Absorption** -- the medium swallows photons. Light grows dimmer as
  it travels farther. Colored absorption creates tinted atmospheres
  (blue underwater, orange sunset haze).

- **Scattering** -- photons change direction when they hit particles in
  the medium. This is why fog glows when headlights shine through it,
  and why the sky is blue.

The combination of absorption and scattering determines how light behaves
as it traverses the volume.

---

## 9.2 Configuring the Global Medium

The medium is defined under `world: > medium:`:

```yaml
world:
  medium:
    type: "homogeneous"
    sigma_a: [0.01, 0.01, 0.01]
    sigma_s: [0.06, 0.06, 0.06]
    phase: "hg"
    g: 0.85
```

| Parameter | Type      | Default       | Description                        |
|-----------|-----------|---------------|------------------------------------|
| `type`    | `string`  | --            | `"homogeneous"`, `"height_fog"`, `"procedural"`, `"grid"` |
| `sigma_a` | `[R,G,B]` | --           | Absorption coefficient per channel |
| `sigma_s` | `[R,G,B]` | --           | Scattering coefficient per channel |
| `phase`   | `string`  | `"isotropic"` | `"isotropic"`, `"hg"`, `"rayleigh"`, `"double_hg"`, `"schlick"` |
| `g`       | `float`   | `0.0`         | Asymmetry parameter (for `"hg"` / `"schlick"`) |

Type-specific extra fields (height fog: `y0`, `scale_height`; procedural:
`frequency`, `octaves`, `lacunarity`, `gain`, `seed`; grid: `bounds_min`,
`bounds_max`, `nx`, `ny`, `nz`, `data`/`file`) are documented in section 9.4.

### sigma_a (Absorption)

Controls how quickly light is absorbed. Units are inverse world-units
(1/unit). Higher values mean denser, more opaque medium.

- `[0.01, 0.01, 0.01]` -- very faint absorption (slight haze).
- `[0.1, 0.05, 0.01]` -- colored absorption: red is absorbed fastest,
  blue least. This creates a blue tint (like underwater).

### sigma_s (Scattering)

Controls how much light is deflected by particles. Higher values mean
denser fog with more visible light shafts.

- `[0.02, 0.02, 0.02]` -- thin mist.
- `[0.1, 0.1, 0.1]` -- noticeable fog.
- `[0.5, 0.5, 0.5]` -- thick, pea-soup fog.

The total extinction coefficient is `sigma_t = sigma_a + sigma_s`. This
determines the overall opacity of the medium (how quickly visibility
drops with distance).

---

## 9.3 Phase Functions: How Light Scatters

The phase function determines the angular distribution of scattered
light. 3D-Ray supports five.

### Isotropic (Default)

```yaml
phase: "isotropic"
```

Light scatters equally in all directions. The simplest model; works well
for dense smoke, thick clouds, very turbid media.

### Henyey-Greenstein (HG)

```yaml
phase: "hg"
g: 0.85
```

Allows directional bias of scattering:

| `g` value | Behavior                                   |
|-----------|--------------------------------------------|
| `0.0`     | Identical to isotropic                     |
| `0.3`     | Mild forward scattering (thin haze)        |
| `0.7`     | Strong forward scattering (fog, clouds)    |
| `0.85`    | Very forward-peaked (dense fog, mist)      |
| `-0.3`    | Backward scattering (unusual, artistic)    |

Forward scattering (`g > 0`) is physically accurate for most real-world
media (fog, dust, aerosols) and creates bright halos around light sources.

Aliases: `"hg"`, `"henyey_greenstein"`.

### Rayleigh (Atmospheric Scattering)

```yaml
phase: "rayleigh"
```

Formula `p(θ) = (3/16π)(1 + cos²θ)`: the scattering profile of air
molecules, used in every sky model and aerial-perspective framework
(Bruneton, Hosek-Wilkie). No parameters. Suitable for very thin fog
intended to simulate planetary atmosphere.

### Double Henyey-Greenstein (Realistic Clouds)

```yaml
phase: "double_hg"
g1: 0.85
g2: -0.3
w: 0.7
```

Linear combination of two HG lobes: a forward one (`g1 ≈ 0.85`) and a
side/backward one (`g2 ≈ -0.3`), mixed with weight `w ∈ [0,1]`. The model
used by Nubis (Guerrilla Games) for the volumetric clouds in *Horizon Zero
Dawn*, and by Arnold for cumulus-style cloud rendering. Produces a soft
silver lining around cloud edges that single-HG cannot reproduce.

### Schlick (fast-HG)

```yaml
phase: "schlick"
g: 0.6
```

A rational approximation of HG that avoids `sqrt`:
`p(θ) = (1 - k²) / (4π · (1 + k · cosθ)²)` with `k ≈ 1.55·g − 0.55·g³`.
Used by RenderMan and Cycles when maximum throughput matters. Visually
nearly indistinguishable from HG for `|g| < 0.9`.

**Which one to pick?**
- Generic fog / smoky haze → `hg` with `g = 0.6-0.85`.
- Sky and atmospheric haze at planetary scale → `rayleigh`.
- Realistic cumulus clouds with silver lining → `double_hg`.
- Path tracer with millions of phase evaluations → `schlick` (speed).
- Dense smoke, turbid underwater → `isotropic`.

### MIS on the phase function

When a ray scatters at a medium point, the renderer computes the
in-scattered radiance by combining two strategies: **NEE** (a shadow ray
toward every light, with the phase function in place of the BRDF) and
**phase sampling** (an importance-sampled bounce). The two densities —
`light.PdfSolidAngle` and `phase.Pdf` — are combined under the same
balance/power heuristic used on surfaces.

In practice this gives a **visible reduction of fireflies** in scenes
with a strong directional light through fog (god rays): a phase-sampled
bounce that lands directly on a light is now MIS-weighted instead of
being suppressed. Switching to `--mis power` can help further when the
sun is small (near-pinpoint) relative to the phase lobe.

---

## 9.4 Beyond Homogeneous: Heterogeneous Medium Types

Uniform fog has limits: in the real world density changes with altitude,
forms irregular patches, or is confined to a localized volume (a cloud,
a column of smoke). 3D-Ray offers three additional types to cover these
cases.

### 9.4.1 `height_fog` — Exponential Altitude Falloff

Density falls exponentially with height: `σ_T(y) = σ_T0 · exp(-(y - y0) / H)`.
The "atmosphere / aerial perspective" model used by Arnold
`atmosphere_volume` and V-Ray `EnvironmentFog`. The integral along a ray
has a closed form → **cost nearly identical to homogeneous**, no delta
tracking needed.

```yaml
world:
  medium:
    type: "height_fog"
    sigma_a: [0.02, 0.02, 0.025]
    sigma_s: [0.25, 0.28, 0.32]
    y0: 0.0                # Reference height (nominal density)
    scale_height: 2.0      # Y distance for 1/e density falloff
    phase: "hg"
    g: 0.6
```

- **`y0`**: above this height density decreases; below, it increases.
- **`scale_height`**: small `H` → thin layer hugging the ground; large `H`
  → gentle gradient visible across the entire scene.

**Typical uses:** outdoor scenes with mountains, roads at dawn, sea at
the horizon, city views with smog. Adds "breathing room" to a scene
without the heaviness of uniform fog.

**Tip:** if the camera is low and looks almost horizontally through a lot
of fog, raise `-s` to at least 256.

### 9.4.2 `procedural` — Perlin fBm

Density driven by **Perlin noise with fractal brownian motion** (fBm).
Free-path sampling uses **delta tracking (Woodcock)** and transmittance is
estimated via **ratio tracking**. Analogous to Arnold `standard_volume`
with a noise input or RenderMan `PxrVolume` in procedural mode.

```yaml
world:
  medium:
    type: "procedural"
    sigma_a: [0.01, 0.01, 0.01]
    sigma_s: [0.5, 0.5, 0.55]
    frequency: 0.45        # Noise frequency (world units)
    octaves: 4             # fBm octave count (1-8)
    lacunarity: 2.0        # Frequency multiplier between octaves (≥ 1)
    gain: 0.55             # Amplitude multiplier between octaves (0.01-0.99)
    seed: 42               # Deterministic seed
    phase: "hg"
    g: 0.75
```

- **`frequency`** high → small, dense pockets; low → large blobs.
- **`octaves`** 3–4 is enough for convincing fog; 6+ adds fine detail
  (but more render noise).
- **`lacunarity`** = 2.0 is the classic doubling-between-octaves choice.
- **`gain`** < 0.5 → soft noise, > 0.5 → harder, more clumped noise.
- **`seed`**: change it to vary the noise shape at fixed other parameters.

**Typical uses:** rooms with irregular fog, horror scenes, uneven
god-rays, misty forests, water surfaces with patchy haze.

**Tip:** delta tracking is noisier → raise `-s` to 400 or 1024 for final
renders.

### 9.4.3 `grid` — Density from a 3D Grid

Density sampled on a **regular 3D grid** inside a world-space AABB, with
a selectable reconstruction filter (trilinear by default, tricubic as an
option). Outside the AABB: vacuum. Analogous to PBRT's `GridMedium`,
Arnold's `volume` (VDB mode) and V-Ray's `VolumeGrid`. Two forms: inline
data in the YAML or an external binary `.vol` file.

**Form A — inline (for small grids, ≤ 8³):**

```yaml
world:
  medium:
    type: "grid"
    sigma_a: [0.1, 0.1, 0.1]
    sigma_s: [3.0, 3.0, 3.2]
    bounds_min: [-1.5, 0.5, -1.5]
    bounds_max: [ 1.5, 3.5,  1.5]
    nx: 4
    ny: 4
    nz: 4
    interpolation: "trilinear"   # Optional: "trilinear" (default) or "tricubic"
    phase: "hg"
    g: 0.5
    data:
      # nx*ny*nz values in [0,1]; z-major layout (y outer, x inner per z-slice)
      - 0.0
      - 0.0
      # ... (64 total values for nx=ny=nz=4)
```

**Form B — binary `.vol` file (recommended for grids ≥ 16³):**

```yaml
world:
  medium:
    type: "grid"
    sigma_a: [0.1, 0.1, 0.1]
    sigma_s: [3.0, 3.0, 3.2]
    interpolation: "tricubic"    # Catmull-Rom smoothing
    phase: "hg"
    g: 0.5
    file: "cloud-64x64x64.vol"   # Path relative to the YAML
```

The `.vol` format (VOL1) is: magic string `"VOL1"` (4 bytes) + `nx`, `ny`,
`nz` (3 × int32 little-endian) + `bounds_min.{x,y,z}`, `bounds_max.{x,y,z}`
(6 × float32 little-endian) + `nx*ny*nz` float32 densities. It is meant
as a simple intermediate step: easy to generate from Houdini/Blender via
a Python script.

**Typical uses:** localized smoke, isolated clouds, explosions, pre-
simulated smoke "assets" imported from other tools. Grid resolution does
not affect render cost (only parse time and memory).

**Reconstruction filter (`interpolation`).** When a sample falls between
voxels, 3D-Ray interpolates density in one of two ways:

- **`trilinear`** (default, 8 taps, C⁰). Cheap. At low resolutions
  (≤ 16³) the density field has a discontinuous derivative at cell
  boundaries → visible linear banding in the render. This is a universal
  artifact of low-budget volumetric renderers (Arnold, V-Ray, RenderMan)
  and in production it is solved by using dense grids (128³–1024³) where
  the jumps are sub-pixel.
- **`tricubic`** (64 taps, C¹, Catmull-Rom cardinal spline with τ = 0.5).
  About 8× per-sample cost, but the density field is continuously
  differentiable → no kinks even on tiny grids. The result is clamped to
  `[0,1]` to preserve the delta-tracking majorant invariant. Accepted
  aliases: `cubic`, `catmull-rom`, `smooth`. Matches the "cubic"/"smooth"
  filter offered by Arnold, Houdini and RenderMan on VDB grids.

**Tip:** outside the AABB the medium is vacuum → rays that miss it are
free. Size the bounds carefully to maximize performance. With
`tricubic`, expect renders to be ~5–10% slower on rays that cross the
AABB.

---

## 9.5 Practical Recipes

### Light Fog

A subtle haze that softens distant objects and adds atmosphere without
obscuring the scene.

```yaml
world:
  medium:
    type: "homogeneous"
    sigma_a: [0.005, 0.005, 0.005]
    sigma_s: [0.04, 0.04, 0.04]
    phase: "hg"
    g: 0.8
```

### Dense Mist

Visibility drops to a few units. Light sources create bright, dramatic
halos.

```yaml
world:
  medium:
    type: "homogeneous"
    sigma_a: [0.01, 0.01, 0.01]
    sigma_s: [0.15, 0.15, 0.15]
    phase: "hg"
    g: 0.85
```

### Underwater

Water absorbs red light faster than blue. The deeper you look, the
bluer the scene becomes. Moderate scattering creates visible light shafts
from the surface.

```yaml
world:
  medium:
    type: "homogeneous"
    sigma_a: [0.12, 0.06, 0.02]
    sigma_s: [0.02, 0.02, 0.02]
    phase: "hg"
    g: 0.6
```

### Tinted Haze (Golden Hour Atmosphere)

Warm atmospheric haze that scatters orange-gold light, creating a dreamy
golden hour effect.

```yaml
world:
  medium:
    type: "homogeneous"
    sigma_a: [0.002, 0.005, 0.015]
    sigma_s: [0.03, 0.025, 0.015]
    phase: "hg"
    g: 0.75
```

### Thick Smoke

Very dense, nearly opaque medium with strong isotropic scattering.

```yaml
world:
  medium:
    type: "homogeneous"
    sigma_a: [0.05, 0.05, 0.05]
    sigma_s: [0.4, 0.38, 0.35]
    phase: "isotropic"
```

---

## 9.6 Rendering Considerations

Volumetric rendering is more demanding than surface-only rendering.
Keep these tips in mind:

1. **Increase samples.** The medium adds another source of noise (random
   scattering events along each ray). `homogeneous` and `height_fog` are
   analytic and look decent at 64 SPP. `procedural` and `grid` use delta
   tracking → need 256+ SPP for clean results, 1024+ for publication.

2. **Don't overdo depth.** The volumetric path is already handled
   correctly at `-d 6-8`. Russian Roulette terminates long paths
   automatically, so values above `-d 10` rarely improve quality and
   always cost time.

3. **Firefly clamp with dense fog.** Media with high `sigma_s` + `-d 8+`
   occasionally produce rare bright spikes. Lower `-C` to `25` or `15`
   without hesitation: you lose little dynamic range, gain a lot of
   cleanliness.

4. **Spot lights create god rays.** A spot light through fog produces
   a visible cone of light. Dramatic effect, especially with `procedural`
   (irregular god-rays) or `height_fog` (god-rays that thin out with
   altitude).

5. **Point lights glow.** In fog, every point light gets a soft radial
   halo whose size depends on medium density.

6. **The medium is global** (except `grid`, which is confined to its
   AABB). `homogeneous`, `height_fog`, `procedural` fill the whole world
   and affect every ray including shadow rays. `grid` lets rays that
   never intersect its AABB pass through without attenuation.

7. **Start thin, then thicken.** It is easier to add fog than to remove
   it. Begin with small `sigma_s` values (0.01–0.03 for homogeneous /
   height_fog, 0.3–0.5 for procedural / grid) and increase until you
   get the desired effect.

8. **Phase functions with `g` → 1** (e.g. HG with `g = 0.95`) produce
   tighter, more dramatic god-rays but **increase variance**: if cones
   look noisy, lower `g` to 0.7-0.85 or switch to `double_hg` with more
   balanced weights.

9. **`lights: []` + global medium → tendency to come out black.** With
   no explicit lights, the flux-based classifier flags the scene as
   indirect-dominant and switches Russian Roulette to its conservative
   tuning (≥ 8 bounces, 0.5 minimum survival). When fog attenuates every
   segment, the light from a gradient sky or HDRI alone struggles to
   reach the sensor and the render comes out very dark. Fix: add at
   least one explicit `directional` or `sphere` light that declares the
   sun as a separate `ILight` (HDRI/gradient stays as fill); the
   classifier flips to "direct-dominant" and god-rays become visible.
   Seen in practice in `scenes/foggy-hdri.yaml`.

---

## 9.7 Complete Example: Cathedral in Fog

```yaml
# cathedral-fog.yaml
# Stone pillars in fog with a spot light creating a visible beam.

world:
  ambient_light: [0.005, 0.005, 0.008]
  background: [0.01, 0.01, 0.02]
  medium:
    type: "homogeneous"
    sigma_a: [0.008, 0.008, 0.008]
    sigma_s: [0.07, 0.07, 0.07]
    phase: "hg"
    g: 0.82

cameras:
  - name: "main"
    position: [0, 1.5, -6]
    look_at: [0, 2, 2]
    fov: 55

lights:
  # The main effect: a spot light creating a visible beam through the fog
  - type: "spot"
    position: [0, 4.8, 4]
    direction: [0, -0.7, -0.3]
    color: [1.0, 0.92, 0.75]
    intensity: 120.0
    inner_angle: 10
    outer_angle: 22

  # Faint fill so the pillars are not completely black
  - type: "point"
    position: [0, 4, -4]
    color: [0.5, 0.55, 0.7]
    intensity: 8.0

materials:
  - id: "floor"
    type: "disney"
    roughness: 0.7
    texture:
      type: "checker"
      scale: 0.5
      colors: [[0.25, 0.22, 0.2], [0.15, 0.13, 0.12]]

  - id: "stone_pillar"
    type: "disney"
    roughness: 0.6
    specular: 0.3
    texture:
      type: "marble"
      scale: 5.0
      noise_strength: 3.0
      colors: [[0.65, 0.6, 0.55], [0.4, 0.37, 0.33]]
      randomize_offset: true

  - id: "ceiling"
    type: "lambertian"
    color: [0.2, 0.18, 0.16]

entities:
  # Floor
  - type: "infinite_plane"
    point: [0, 0, 0]
    normal: [0, 1, 0]
    material: "floor"

  # Ceiling
  - type: "infinite_plane"
    point: [0, 5, 0]
    normal: [0, -1, 0]
    material: "ceiling"

  # Left row of pillars
  - type: "cylinder"
    center: [-2, 0, -2]
    radius: 0.3
    height: 5.0
    material: "stone_pillar"
    seed: 1

  - type: "cylinder"
    center: [-2, 0, 2]
    radius: 0.3
    height: 5.0
    material: "stone_pillar"
    seed: 2

  - type: "cylinder"
    center: [-2, 0, 6]
    radius: 0.3
    height: 5.0
    material: "stone_pillar"
    seed: 3

  # Right row of pillars
  - type: "cylinder"
    center: [2, 0, -2]
    radius: 0.3
    height: 5.0
    material: "stone_pillar"
    seed: 4

  - type: "cylinder"
    center: [2, 0, 2]
    radius: 0.3
    height: 5.0
    material: "stone_pillar"
    seed: 5

  - type: "cylinder"
    center: [2, 0, 6]
    radius: 0.3
    height: 5.0
    material: "stone_pillar"
    seed: 6
```

Render with:

```
RayTracer -i cathedral-fog.yaml -w 1200 -H 800 -s 256 -d 12
```

The spot light creates a dramatic visible beam cutting through the fog
between the pillars. The forward-peaked HG phase function (g=0.82)
concentrates the glow around the beam direction, just like real fog.

---

## 9.8 Showcase Scenes

The repository ships four ready-to-render showcases, one per medium type,
in `scenes/showcases/`:

| Scene | Medium type | What it shows |
|---|---|---|
| `volumetric-01-homogeneous-showcase.yaml` | `homogeneous` | Classic god-ray from a spot light in uniform fog |
| `volumetric-02-height-fog-showcase.yaml` | `height_fog` | Outdoor aerial perspective with vertical density gradient |
| `volumetric-03-procedural-showcase.yaml` | `procedural` | Irregular Perlin fog with non-uniform god-rays |
| `volumetric-04-grid-showcase.yaml` | `grid` | Localized smoke in an inline 4³ grid |

Each scene includes a descriptive header and ready-to-paste commands for
the Preview/Standard/Final profiles.

---

## What You Have Learned

- **sigma_a** controls absorption (light dimming over distance).
- **sigma_s** controls scattering (fog density, light shafts).
- 3D-Ray supports **four medium types**: `homogeneous` (uniform, analytic),
  `height_fog` (exponential altitude falloff, analytic), `procedural`
  (Perlin fBm, delta tracking) and `grid` (3D grid from data or `.vol`
  file, delta tracking).
- Five phase functions: `isotropic`, `hg`, `rayleigh` (atmosphere),
  `double_hg` (realistic clouds), `schlick` (fast-HG).
- Analytic media (`homogeneous`, `height_fog`) are cheap; delta-tracking
  ones (`procedural`, `grid`) are noisier → more SPP.
- The medium is global and affects all rays, except `grid` which is
  confined to its AABB.
- Volumetric scenes need more samples than surface-only ones; `-d 6-8`
  is enough, don't overdo it.
- Spot lights in fog create god rays; point lights create halos.

---

[Previous: Constructive Solid Geometry (CSG)](./08-csg.md) | [Next: Asset Libraries and Complete Scenes](./10-libraries-and-projects.md) | [Tutorial Index](./README.md)
