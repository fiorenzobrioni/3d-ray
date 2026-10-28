# Scene Reference Guide

This document is a comprehensive technical reference for creating and configuring 3D-Ray scene files using the YAML format, **format version 2**. It provides a complete guide to project structure, documentation, and best practices for writing high-quality scenes.

---

### 1. **PROJECT OVERVIEW**
**3D-Ray** is a high-performance ray-tracing engine built in C# and .NET 10. It uses YAML files to describe complete 3D scenes with:
- Physically-based materials: the OpenPBR Surface uber-material plus exact Lambertian, conductor and dielectric models
- Path tracing with Next Event Estimation, MIS, path guiding, bidirectional integrators
- Lights in photometric units (candela, nits, lux, lumens, watts), colour temperature, IES profiles
- Procedural and image-based textures, on any colour or scalar input
- Normal and bump mapping, mesh subdivision and displacement
- CSG boolean operations, hierarchical scene graphs (groups and templates)
---
### 2. **YAML SCENE FILE STRUCTURE**
A scene file is a mapping of sections (recommended order):
```yaml
format_version: 2      # required: the version of the scene format
units: { length: m }   # (optional) metres per scene unit (default 1 m)
render:                # (optional) how to render: resolution, samples, view, ...
imports:               # (optional) scene files whose materials, lights, entities, templates are merged
templates:             # (optional) reusable object blueprints
world:                 # environment (sky, ground, global medium)
cameras:               # camera list
lights:                # light sources
materials:             # material definitions
mediums:               # (optional) named participating media
entities:              # 3D objects (primitives, groups, instances, CSG, meshes)
```

**One name per concept.** The format has no aliases: every key, every `type` and
every enum value has one spelling.

**Strict validation.** The loader checks the whole file before building the
scene and reports every problem at once, with line and column:

- an unknown key, a value of the wrong kind (text where a number goes), an
  unknown `type` or enum value is an **error**, with a suggestion when the key
  is close to a valid one (`roughnes` → "did you mean 'roughness'?") or was a
  key of format 1 (`refraction_index` → "now 'ior'");
- a valid key that has no effect on the block's `type` (a `roughness` on a
  `lambertian`) is a **warning**.

A file without `format_version` is a scene of format 1: the loader refuses it
and points at **SceneMigrate**, which converts it (see §13).

**JSON Schema.** `docs/reference/scene-schema.json` describes the format (every
key with its kind and description, the values of every `type` and enum, the
keys each type takes). An editor with a YAML language server completes and
checks scenes against it; the first line of the repository's scenes points at
it:
```yaml
# yaml-language-server: $schema=../docs/reference/scene-schema.json
```

**Conventions:**
- **Coordinates**: right-handed, **Y** up (as Mitsuba, glTF and OBJ). A camera at −Z looking at +Z sees +X on its left.
- **Lengths** in scene units (`units.length` metres each); **angles** in degrees.
- **Colours** `[R, G, B]` are linear Rec.709; `"#rrggbb"` is an sRGB colour, decoded to linear (`"#808080"` = 0.2158). Any colour may also be one grey number.
- **Light** is in photometric units: radiance in nits (cd/m²). Without an exposure 1 nit is the linear value 1 the view transform receives; the camera's `ev100` exposes like a real camera.

#### **Units** (`units:`)
```yaml
units:
  length: cm        # a number of metres (0.01), or m, cm, mm, km, in, ft
```
The length unit matters only where a quantity carries metres: the aperture an
`f_stop` gives (millimetres), a light's `intensity` in candela and its `power`
(they are per square metre at the receiver), the atmosphere medium. Media
coefficients (`sigma_a`, `sigma_s`) and subsurface radii are per scene unit.

#### **Render block** (`render:`)
How to render the scene, in the file. Each key is the command-line flag of the
same name, with underscores (`light_sampling` = `--light-sampling`):
```yaml
render:
  quality: final              # a -q preset as the base
  width: 2048                 # the scene's resolution (aspect included)
  height: 858
  samples: 512
  depth: 16                   # also depth_diffuse, depth_glossy, depth_transmission, depth_volume, depth_transparent
  denoiser: oidn              # none | nlm | nfor | oidn (+ denoise_quality)
  adaptive: true              # + adaptive_threshold, adaptive_min_spp
  integrator: path            # path | bdpt | vcm (+ vcm_radius)
  view: agx                   # agx | aces | standard (+ look: none | punchy | golden)
  exposure: 0                 # stops, added to the camera's exposure
  filter: gaussian            # gaussian | blackman-harris | box
  camera: hero                # which camera, by name or index
  # also: shadow_samples, light_sampling, mis, sampler, guiding, guiding_training,
  # clamp, indirect_clamp_factor, transparent_shadows, texture_filtering, sss_mode,
  # white_balance, bit_depth, dither, seed
```
Precedence: the preset (`-q` on the command line, else `render.quality`, else
`draft-small`), then the keys of `render:`, then the command-line flags. A `-q`
given on the command line wins over the scene's keys that a preset sets
(resolution, samples, depths, shadow samples, denoiser, adaptive sampling,
light sampling, indirect clamp, SSS mode). A scene that sets `width` and
`height` keeps its aspect: a preset with a `-tiny` / `-small` suffix scales it
to a quarter / a half (`ultra` doubles it) instead of replacing it.
---
### 3. **WORLD SECTION** - Environment Configuration

```yaml
world:
  sky:                                     # (optional) the environment; none = black
    type: "flat"  # flat | gradient | hdri | hosek_wilkie | preetham | nishita
  ground:                                  # (optional) a floor - full schema below
    type: "infinite_plane"                 # infinite_plane | quad | disk
    material: "floor_name"
    point: [0, 0, 0]
  medium:                                  # (optional) global participating medium
    type: "homogeneous"
```

Without `world.sky` the environment is black (no light). Every sky takes
`exposure` (stops: the sky × 2^exposure), `rotate` (below), `visibility` and a
`background` plate.

#### **Flat Sky** (uniform colour):
```yaml
sky:
  type: "flat"
  color: [0.5, 0.7, 1.0]                  # uniform radiance over the sphere (default 1)
```
A flat sky participates in NEE (uniform sphere sampling, pdf = 1/(4π)) whenever
its luminance is above zero. Set `color: [0, 0, 0]` for a black void.

#### **Gradient Sky** (stylised outdoor / quick previews):
```yaml
sky:
  type: "gradient"
  zenith_color:  [0.10, 0.30, 0.80]      # top of sky
  horizon_color: [0.65, 0.80, 1.00]      # horizon
  ground_color:  [0.30, 0.25, 0.20]      # below the horizon
  sun:                                     # (optional)
    direction:      [0.5, 1.0, 0.3]       # direction TOWARDS the sun
    luminance:      10                    # luminance of the disc (default 10)
    angular_radius: 1.5                    # half-angle in degrees (the real sun 0.265)
    color:          [1.0, 0.98, 0.85]     # tint (luminance 1)
    shadow_samples: 4
```
The gradient sky is on the relative scale of its colours, and the disc's
`luminance` is on the same scale: a surface facing it receives
E = L π sin²θ, so a larger disc gives more light at the same luminance. It is
registered as a separate `PhysicalSun` light (cone sampling, limb darkening).

#### **Hosek-Wilkie / Preetham** (analytical clear-sky daylight):
```yaml
sky:
  type: "hosek_wilkie"                     # or "preetham"
  turbidity:     3.0                       # Linke turbidity: 1 = pure air, 3 = clear, 6 = hazy, 10 = very hazy
  ground_albedo: [0.3, 0.3, 0.3]           # ground below the horizon (also tints the Hosek-Wilkie sky)
  sun:
    direction:      [0.3, 0.8, 0.2]       # direction TOWARDS the sun
    shadow_samples: 4
```
Two analytic clear-sky models, parametrised by turbidity, ground albedo and the
sun's elevation:

- **`hosek_wilkie`**: Hosek and Wilkie 2012, RGB version, from the authors'
  fitted tables. Fits measured skies well, low suns included. Turbidity 1-10,
  ground albedo 0-1 per channel.
- **`preetham`**: Preetham, Shirley and Smits 1999, the earlier model. Brighter
  sky for the same turbidity, a pinkish tint near the horizon at mid
  elevations, poor at low suns: kept as a baseline.

**The sun comes from the same light as the sky.** Its disc shows the sun's
illuminance at the top of the atmosphere (133.8 klux), attenuated along its
path through an atmosphere of Rayleigh and Mie scattering and ozone with the
dust of the same turbidity, spread over the disc's solid angle. On a clear day
with the sun at 45° it gives a horizontal plane about 3-4 times what the sky
gives; at 15° about as much. The disc is registered as a separate `PhysicalSun`
light (cone sampling, limb darkening). It warms and dims as the sun sets.

**Physical skies are in nits**, the scale of the photometric lights: a clear
noon sky is thousands of nits, the sun's disc about 1.5×10⁹. Expose for them
with the camera's `ev100` (about 15 for a sunny day), or dim them with
`exposure`. At load the console reports the illuminance the sky gives a
horizontal plane on the ground, and the sun's part of it (`Sky light: 75190 lux
on the ground, 58593 of it from the sun`).

**Below the horizon** the sky shows a ground: a Lambertian plane of
`ground_albedo` lit by that sky and sun, reached over the first 3° below the
horizon so there is no seam. A scene with its own ground plane hides it.

The sun below the horizon: the sky stays at its sunset state and the disc goes
dark (use `nishita` for twilight).

#### **Nishita** (physical Rayleigh + Mie atmosphere):
```yaml
sky:
  type: "nishita"
  turbidity: 3.0                           # haze knob: dust 0.5 at 1, 0.9 at 3, 2.3 at 10
  ground_albedo: [0.3, 0.3, 0.3]
  sun:
    direction:      [0.4, 0.5, 0.3]       # direction TOWARDS the sun
    shadow_samples: 4
```
Integrated single-scattering model (Nishita et al. 1993): the view ray is marched
through an atmosphere of Rayleigh and Mie scattering and ozone absorption over a
6360 km planet; at each step the sun's light, attenuated down to that point
(transmittance table), is scattered toward the viewer. Sunrise and sunset
colours come from the physics, not from a fit, and the sky keeps working with
the sun at the horizon. In nits, from the same 133 800 lux at the top of the
atmosphere as the analytic skies.

`turbidity` here maps to a light dust load (an exceptionally clean atmosphere at
3): the model scatters once, and with the aerosol of a real turbidity 3 its
horizon would go dark where multiple scattering keeps a real one bright. For the
same reason its sky is darker than a measured one against the sun (about half):
for a daylight balance closest to measurements use `hosek_wilkie`.

#### **HDRI/IBL** (image-based, .hdr + .exr):
```yaml
sky:
  type: "hdri"
  path: "hdri/studio.hdr"                 # .hdr (Radiance) or .exr (OpenEXR)
  exposure: 0                              # stops
  rotate: [0, 90, 0]                       # turn the environment 90° about Y
  sun:                                     # (optional) sun extraction
    extract_from_hdri: true                # auto-detect the sun and split it out
    extract_threshold: 50                  # multiple of HDRI mean luminance (def. 50)
    shadow_samples: 4
```
HDRIs are importance-sampled via a luminance-weighted 2D CDF over the
equirectangular map. **OpenEXR** is supported via a built-in scanline-RGB
loader (No compression / ZIP / ZIPS / RLE, half + float). **Sun extraction**
detects the brightest peak, replaces those pixels with the ring-averaged
background, and emits a paired `PhysicalSun` for clean shadows. Negative pixel
values (some EXR compressions) are clamped to 0 at load.

#### **Rotation** (`rotate`):
```yaml
sky:
  type: "hdri"
  path: "studio.hdr"
  rotate: [10, 45, 0]       # Euler degrees, as an entity's rotate
  rotate_order: xyz         # (optional) default xyz: X first
```
The sky turns in the world as an entity would with the same `rotate`: a sky
feature at +X turned by `[0, 90, 0]` ends at −Z.

#### **Visibility flags** (per ray type):
```yaml
sky:
  type: "hdri"
  path: "studio.hdr"
  visibility:
    camera:       true     # Camera rays see the sky body
    diffuse:      true     # Diffuse / sheen / SSS bounces see the sky
    glossy:       true     # Glossy / clearcoat bounces see the sky
    transmission: true     # Refractions see the sky
    shadow:       true     # NEE shadow rays return sky radiance
  sun:
    visibility: { camera: false }    # Sun disc invisible to camera (still illuminates)
```
Each `false` disables the sky's contribution for that ray category. Common setups:
`camera: false` hides the HDRI from the rendered background while still lighting
the scene; `glossy: false` removes the HDRI from reflective materials (useful for
clay-render previews). A bounce's category is the lobe the material sampled
(a rough metal or coat reflection is `glossy`, a refraction `transmission`, a
volume or subsurface scatter `diffuse`); a ray that passes through a cut-out
keeps its category, so a camera ray through a leaf is still a camera ray. The
same `visibility:` block exists on the ground, on every entity and on the
area and sphere lights.

#### **Background plate** (camera-visible only):
```yaml
sky:
  type: "hdri"
  path: "lighting.hdr"        # Primary lighting source
  background:
    type: "hdri"
    path: "background.hdr"    # Different image shown to camera rays
```
A separate `background:` sub-block lets you light the scene with one environment
and show a different one to the camera - standard product / VFX workflow.

**Preset Sky Configurations:**
- **Noon** (clean gradient, bright sun)
- **Golden Hour** (low warm sun, saturated horizon)
- **Sunset** (dramatic orange horizon)
- **Night** (very dim zenith/horizon, faint sun disk)
- **Overcast** (uniform horizon, no sun disk; or `flat` with a low gray)
- **Studio** (`flat` with a dim neutral colour to fill bounce light)

#### **Ground** (world floor):

The `world.ground:` block is a shorthand for the scene's floor: an infinite
plane, a quad or a disk through `point` with `normal`, with a material, a UV
transform and per-ray-category visibility flags. A terrain is an entity of
`type: heightfield` (§7.13).

```yaml
world:
  ground:
    type: "infinite_plane"     # infinite_plane (default) | quad | disk
    point: [0, 0, 0]           # a point on the plane / the centre of the quad or disk
    normal: [0, 1, 0]          # surface normal (default +Y)
    size: 50                   # half side (quad) or radius (disk), default 50
    material: "floor_id"       # from `materials:`
    # ── UV transform (applied on top of the primitive's UVs) ─────────
    uv_scale:    [10, 10]      # per-axis tile factor
    uv_offset:   [0, 0]        # pan
    uv_rotation: 30            # degrees, CCW from above
    # ── Visibility flags ─────────────────────────────────────────────
    visibility:
      camera:       true       # primary camera rays see the ground
      diffuse:      true       # indirect diffuse bounces hit it
      glossy:       true       # mirror / glossy reflections hit it
      transmission: true       # refractions see it through glass
      shadow:       true       # NEE shadow rays are blocked by it
```

**Material.** `material:` names a material; without it the ground is a
Lambertian of the sky's `ground_albedo` (or a gradient sky's `ground_color`),
so a floor matches the sky's horizon, else grey 0.5.

**BVH partitioning.** A `quad` / `disk` ground is finite -
it joins the BVH together with regular entities. An `infinite_plane` ground
is kept in the linear list outside the BVH (its 1e6³ AABB would poison BVH
quality). Both behaviours are preserved through the visibility / UV
wrappers.

**Visibility semantics.** Each `visibility.*` flag set to `false` makes the
ground transparent to rays of that category - the ray advances past the
surface as if it weren't there. Use `visibility.shadow: false` to keep a
visible floor that does not cast occlusion, or `visibility.camera: false`
for a shadow-catcher-style invisible floor that still bounces indirect
light (the ALPHA matte / shadow-catcher AOV is a separate, planned
feature).

#### **Volumetrics (Participating Media)**:

3D-Ray supports **four global medium types** (`homogeneous`, `height_fog`, `procedural`, `grid`) and **five phase functions** (`isotropic`, `hg`, `rayleigh`, `double_hg`, `schlick`). The `medium:` field lives at the `world` level.

**Fields common to all types:**

| Field | Type | Description |
|---|---|---|
| `type` | string | `homogeneous` \| `height_fog` \| `procedural` \| `grid` |
| `sigma_a` | RGB | Absorption coefficient (light dimming) |
| `sigma_s` | RGB | Scattering coefficient (visual fog density, god-rays) |
| `phase` | string | Phase function (default `isotropic`); if `g` is present → `hg` |

**Type 1 - `homogeneous`** (constant density, analytic, cheap):
```yaml
medium:
  type: "homogeneous"
  sigma_a: [0.005, 0.005, 0.005]
  sigma_s: [0.06, 0.06, 0.07]
  phase: "hg"
  g: 0.85
```

**Type 2 - `height_fog`** (exponential density in altitude, analytic):
```yaml
medium:
  type: "height_fog"
  sigma_a: [0.02, 0.02, 0.025]
  sigma_s: [0.25, 0.28, 0.32]
  y0: 0.0                              # Reference height (nominal density)
  scale_height: 2.0                    # Y distance for a 1/e density falloff
  phase: "hg"
  g: 0.6
```

**Type 5 - `atmosphere`** (Nishita aerial perspective, shares constants with `type: nishita` sky):
```yaml
medium:
  type: "atmosphere"
  y0: 0.0                          # world Y of altitude 0 (sea level)
  air_density: 1.0                 # air (Rayleigh) density, 1 = standard atmosphere
  dust_density: 1.0                # dust (Mie) density (0 = pristine, 1 = clean, >1 = polluted)
  ozone_density: 1.0               # ozone (absorption around 25 km), 1 = standard
  phase: "hg"                      # the dust's phase function (default Henyey-Greenstein g = 0.76)
  g: 0.76
```
The Earth's atmosphere over a flat ground, with the same air, dust and ozone
as the physical skies: air scatters with the Rayleigh phase function (always),
dust with `phase` (forward Henyey-Greenstein by default), ozone only absorbs.
Each species' optical depth has a closed form, so the transmittance is exact
and distances are sampled exactly (no tracking noise); at a scattering event
the species is picked in proportion to the light it scatters. The medium is
**aerial perspective**: it acts only between the eye (or a scattering point)
and the surface it sees. A ray or shadow ray that runs to the sky crosses it
untouched, because the physical sky already holds the whole atmosphere's light
and the sun's disc its transmittance. Pair it with `world.sky.type: nishita`.

**Type 3 - `procedural`** (Perlin fBm, null-collision tracking):
```yaml
medium:
  type: "procedural"
  sigma_a: [0.01, 0.01, 0.01]
  sigma_s: [0.5, 0.5, 0.55]
  frequency: 0.45                      # Noise frequency (world units)
  octaves: 4                           # fBm octave count (1-8)
  lacunarity: 2.0                      # Frequency multiplier between octaves (≥1)
  gain: 0.55                           # Amplitude multiplier between octaves (0.01-0.99)
  seed: 42                             # Deterministic noise seed
  phase: "hg"
  g: 0.75
```

**Type 4 - `grid`** (3D grid inline or from `.vol` file, null-collision tracking against a grid of majorants + reconstruction filter):
```yaml
# Variant A - inline data (useful for small grids, e.g. ≤ 8³)
medium:
  type: "grid"
  sigma_a: [0.1, 0.1, 0.1]
  sigma_s: [3.0, 3.0, 3.2]
  bounds_min: [-1.5, 0.5, -1.5]        # World-space AABB of the volume
  bounds_max: [ 1.5, 3.5,  1.5]
  nx: 4                                # Grid resolution (min. 2 per axis)
  ny: 4
  nz: 4
  interpolation: "trilinear"           # Optional: "trilinear" (default) or "tricubic"
  phase: "hg"
  g: 0.5
  data: [0.0, 0.0, ...]                # nx*ny*nz floats in [0,1], z-major layout
```
```yaml
# Variant B - external binary file (recommended for large grids)
medium:
  type: "grid"
  sigma_a: [0.1, 0.1, 0.1]
  sigma_s: [3.0, 3.0, 3.2]
  interpolation: "tricubic"            # Catmull-Rom smoothing; useful on low-res grids
  phase: "hg"
  g: 0.5
  file: "cloud-64x64x64.vol"           # Path relative to the YAML; bounds and resolution read from file header
```

**`.vol` file format (VOL1):** magic string `"VOL1"` (4 bytes) + `nx`, `ny`, `nz` (3 × int32 little-endian) + `bounds_min.{x,y,z}`, `bounds_max.{x,y,z}` (6 × float32 little-endian) + `nx·ny·nz` float32 density values, z-major layout (y outer, x inner inside each z-slice).

**Reconstruction filters (`interpolation`):**

| Value | Taps | Continuity | When to use |
|---|---|---|---|
| `trilinear` (default) | 8 | C⁰ | Default. Cheap, but at low resolutions (≤16³) the derivative jumps at cell boundaries → visible linear banding. |
| `tricubic` | 64 | C¹ | Catmull-Rom cardinal spline (τ = 0.5). ~8× per-sample cost, but removes kinks on low-res grids and smooths binary data. Result is clamped to `[0,1]`; the majorant grid allows for its overshoot between voxels (at most (1 + 1.25³)/2 of the largest voxel it reads), so the bound always holds. |

On high-resolution grids (128³+) with smoothly varying density the two filters are visually indistinguishable - `trilinear` is enough. On small inline grids or binary 0/1 data, `tricubic` is the standard way to hide artifacts by reconstructing density smoothly across voxel boundaries.

**Available phase functions:**

| `phase` value | Parameters | Typical use |
|---|---|---|
| `isotropic` | - | Uniform scattering in all directions (dense smoke, thick clouds) |
| `hg` | `g` ∈ (-1, 1) | Henyey-Greenstein: `g > 0` forward, `g < 0` backward, `g = 0` ≈ isotropic |
| `rayleigh` | - | Atmospheric scattering `(3/16π)(1+cos²θ)`; sky, aerial perspective |
| `double_hg` | `g1`, `g2`, `w` | Two HG lobes mixed with weight `w` ∈ [0,1]; realistic clouds (Nubis) |
| `schlick` | `g` | Fast rational approximation of HG (no sqrt) |

Examples:
```yaml
# Rayleigh sky
phase: "rayleigh"

# Realistic cumulus cloud (forward g1=0.85 + side lobe g2=-0.3)
phase: "double_hg"
g1: 0.85
g2: -0.3
w: 0.7

# Fast HG
phase: "schlick"
g: 0.6
```

**Choosing the right medium type:**

| Type | Density profile | Cost | When to use |
|---|---|---|---|
| `homogeneous` | Constant everywhere | Analytic, cheap | Indoor scenes, bounded interiors, underwater interiors, smoke columns confined by geometry. **Avoid when the only lighting is `sky` + `sun` or HDRI** (see warning below). |
| `height_fog` | Exponential falloff with altitude (`exp(-(y-y0)/H)`) | Analytic, cheap | Outdoor scenes lit by sky / sun / HDRI: aerial perspective, mountains at dawn, sea horizon, smog. **Default choice for any outdoor scene with directional / environment lighting.** |
| `procedural` | Perlin fBm (null-collision tracking) | Noisier (+30–100% time) | Patchy / irregular fog, horror, uneven god-rays, misty forests, water surfaces with patchy haze. |
| `grid` | Density baked on a 3D grid (inline or `.vol`) | Tracking against a majorant grid + voxel filter | Localized clouds, sim-cached smoke, explosions, hero VFX assets. The medium exists only inside its AABB - outside is vacuum, so other parts of the scene are unaffected. |

> ⚠️ **Sky + sun + `homogeneous` = black render.** A homogeneous global medium has *constant* density extending to infinity, so the Beer–Lambert shadow ray toward the sun (or any environment direction) travels through `exp(-σ_t · ∞) ≈ 0` and direct environmental lighting collapses to zero. Spot/point/area/sphere lights have finite distance and behave correctly, but if the *only* emitters are `sky` + `sun` (or HDRI) the render comes out black. Use `height_fog` instead - its optical depth toward the zenith is bounded by the scale height, which is the standard "aerial perspective" behaviour. This is the physically correct behaviour of `homogeneous` (real atmospheres are not infinite), not a renderer bug.

- **Usage:** Simulates fog, smoke, atmospheric haze, clouds, underwater effects.
- **How media are sampled (every estimator unbiased):** `homogeneous`, `height_fog` and `atmosphere` have closed-form optical depths: the distance to the next scattering event is sampled exactly. `procedural` and `grid` are tracked with null collisions against a majorant (a coarse grid of majorants for `grid`): spectral tracking for distances, ratio tracking for shadow rays, with no cap on the number of steps. Coloured media pick the colour channel that drives the sampling in proportion to the light the path still carries (spectral MIS), so every channel converges. Shadow rays are attenuated by every medium they cross, switching media at the boundaries of medium-bound entities.
- **Rendering tip:** `homogeneous` and `height_fog` are analytic and cheap. `procedural` and `grid` need collisions along the ray and are noisier - raise `-s` to 400/576/1024 and keep `-d 6-8`. See [Rendering Profiles](./rendering-profiles.md) §8 for the full guide.
- **Equiangular sampling:** in an analytic medium (`homogeneous`, `height_fog`, `atmosphere`), the light of point and spot lights is also sampled at distances distributed as the angle the light sees (Kulla and Fajardo 2012) and combined by MIS with the free flight: halos and spot beams converge with a fraction of the noise. Automatic, no setting.
- **Effects:** Spot lights → visible god-rays; point lights → halos; directional → aerial perspective (with `height_fog`).
- **Point/spot lights in fog:** the 1/d² attenuation diverges near the emitter; equiangular sampling (above) already places samples where it peaks, so halos stay clean. `soft_radius` (see §8.1, §8.3) still models a physical bulb size if wanted.
- **Fireflies with area lights in fog:** the `cosLight/d²` term in the area estimator can diverge at grazing angles in dense media. Set `soft_radius` on area lights (see §8.4). Sphere lights use a solid-angle estimator that is bounded by construction - no `soft_radius` needed. If spikes remain, the opt-in clamp `-C 10 --indirect-clamp-factor 0.25` (CLI) suppresses deep-bounce spikes at the cost of some bias.
- **Advanced firefly control:** `--clamp` is off by default (it is biased: an absolute radiance ceiling also removes legitimate light). When given, `--indirect-clamp-factor <f>` (default `0.25`; `1.0` = no extra suppression) multiplies it for the indirect contribution. E.g. `--clamp 10 --indirect-clamp-factor 0.25` gives an indirect clamp of 2.5 (primary 10). The clamp is applied **once, camera-relative**, to the throughput-weighted indirect contribution at the primary surface - not compounded at every bounce.
- **Photographic exposure:** `--exposure <EV>` (default `0`) applies a linear gain `2^EV` to every pixel before ACES tone mapping. Use negative EV (`-1`, `-2`) when the scene reads washed-out because the lights drive arriving radiance above ~2.0, where ACES flattens onto a 0.95-0.99 plateau and hides texture contrast. Positive EV brightens scenes that fall below the curve's linear range. Mirrors the standard photographic exposure compensation found in production renderers.
- **Light importance sampling:** `--light-sampling auto|all|power|uniform|bvh` (default `auto`). `all` samples every light at each vertex; `power` and `uniform` pick one light per NEE event (∝ `ApproximatePower`, or uniformly); `bvh` picks one light by a light BVH that weighs each light's power by its distance and orientation to the shading point (Conty and Kulla 2018): with many lights, the near ones get the samples. `auto` is `all` up to 16 lights and `bvh` above. Every strategy is unbiased.

#### **Named Mediums** (top-level `mediums:` block)

Beyond the single `world.medium`, 3D-Ray exposes a top-level `mediums:` block where named participating media are declared once and bound to specific entities via `interior_medium` / `exterior_medium`. This is the foundation for:
- **Subsurface scattering** (marble, skin, wax, jade, milk) - the SSS random-walk integrator activates automatically when an entity is bound to a `homogeneous` medium with `σ_s > 0`.
- **Per-object volumetric containers** - fog inside a CSG-room, smoke inside a teapot, water inside a glass tank, planet atmosphere - without affecting the rest of the scene the way `world.medium` would.

```yaml
mediums:
  - id: marble_int                     # required in the mediums block
    type: homogeneous                  # any of: homogeneous, height_fog, procedural, grid, atmosphere
    sigma_a: [0.0021, 0.0041, 0.0071]
    sigma_s: [2.19, 2.62, 3.00]
    phase: hg
    g: 0.0

  - id: room_fog
    type: homogeneous
    sigma_a: [0.0, 0.0, 0.0]
    sigma_s: [0.42, 0.45, 0.50]
    phase: hg
    g: 0.55

entities:
  - type: sphere
    material: marble_surface
    interior_medium: marble_int        # → SSS random walk dispatched on refraction enter

  - type: csg
    # op: subtract
    # a: { type: box, ... }
    # b: { type: sphere, ... }
    interior_medium: room_fog          # → local Beer-Lambert/scattering inside the CSG
```

**Resolution rules:**
- IDs are case-insensitive. Duplicates are last-write-wins with a deferred warning (same convention as `materials:`).
- An unknown `interior_medium` / `exterior_medium` id falls back to vacuum and prints a load-time warning.
- Mediums are imported across YAML files identically to materials (see §2). The inline `world.medium` is **never** imported.
- A medium can be referenced by multiple entities - it's a blueprint, not an instance.

**Entity binding fields:**

| Field | Type | Description |
|---|---|---|
| `interior_medium` | string \| null | ID of a medium that fills the entity's interior. Activates SSS random walk on refraction into the entity when the medium scatters (`σ_s > 0`). |
| `exterior_medium` | string \| null | ID of a medium representing the space *outside* this entity. Rarely needed - defaults to the parent medium in the stack (or `world.medium`). |

**Subsurface scattering** (`interior_medium` + `homogeneous` + `σ_s > 0`):

The random-walk integrator is automatically dispatched when:
1. The ray refracts (transmission lobe) **into** an entity, and
2. The entity's `interior_medium` is a `homogeneous` medium with `σ_s > 0`, and
3. CLI `--sss-mode` is `auto` (default).

The surface material's transmission (`transmission_weight`/`specular_ior` on `openpbr`, or a `dielectric`) controls the entry/exit Fresnel; the medium controls the volumetric transport. CLI knobs:

| Flag | Default | Effect |
|---|---|---|
| `--sss-mode auto\|off` | `auto` | `off` declasses bound media to absorption-only (legacy Beer-Lambert), useful for fast A/B previews. |

The walk is unbiased: a light sample at every bounce, no cap on the number of bounces (a Russian roulette on the path throughput ends it).

Two ways to give an object subsurface scattering:

1. `subsurface_weight` with `subsurface_color`, `subsurface_radius` and `subsurface_radius_scale` on an `openpbr` material (material-embedded SSS - see §5.5), or
2. an explicit `interior_medium` binding on the entity with the Jensen 2001 presets in `docs/technical/subsurface-scattering.md`.

The two paths are interoperable: an explicit `interior_medium` always wins over the medium derived from the material.

---
### 4. **CAMERA SECTION**
#### **Cameras**:
```yaml
cameras:
  - name: "main"
    position: [0, 5, -8]
    look_at: [0, 0, 0]
    fov: 45
    aperture: 0.1
    focus_distance: 12
  - name: "top"
    position: [0, 12, 0.01]
    look_at: [0, 0, 0]
    fov: 35
    aperture: 0.0
    focus_distance: 12
  - name: "subject"
    position: [0, 2, -8]
    look_at: [0, 0, 0]
    fov: 45
    aperture: 0.1
    focus_point: [0.5, 0.6, 1.0]            # focus on this point - see below
```
Cameras are always a list; a scene with one camera has a list of one. The
keys of a camera:
```yaml
cameras:
  - name: "main"                            # (optional) for -c <name>
    position: [0, 2, -8]                    # Camera location
    look_at: [0, 0, 0]                      # Target point
    up: [0, 1, 0]                           # "Up" vector (for roll), default +Y
    fov: 60                                 # Vertical field of view (degrees), default 60
    aperture: 0.1                           # Lens diameter in scene units (0 = pinhole)
    focus_distance: 8.0                     # Distance to focus plane, default 1
  # focus_point: [0.5, 0.6, 1.0]            # Alternative: focus on a 3D point
```
**Usage from CLI:**
```bash
dotnet run ... -- -i scene.yaml --list-cameras      # List available
dotnet run ... -- -i scene.yaml -c top -o top.png   # By name
dotnet run ... -- -i scene.yaml -c 1 -o cam1.png    # By index (0-based)
```
> The `.yaml` extension on `-i` is **optional**: when the file is not found
> as given, the loader retries with `.yaml` then `.yml` appended (e.g.
> `-i scene` resolves to `scene.yaml`).
**⚠️ Depth of Field:** When `aperture > 0`, set `focus_distance` (or `focus_point`) to the actual distance / world-space point of your main subject. The default `focus_distance: 1` will create unintended extreme blur.

#### **`focus_point` - focus on a point**
`focus_point: [x, y, z]` is an alternative to the scalar `focus_distance`. The loader computes the focus distance as the **projection** of the camera→focal-point vector onto the optical axis:
```
forward    = normalize(look_at − position)
focusDist  = dot(focus_point − position, forward)
```
The focus plane is perpendicular to the view direction passing through `focus_point`, so the value is a **projection, not a Euclidean distance**. A focal point off-axis at `(3, 4, -5)` with the camera at the origin looking along `−Z` yields focus distance `5`, not `√50 ≈ 7.07`.

When both `focus_point` and `focus_distance` are present, `focus_point` wins (an info message is logged). `focus_point` is ignored with a warning when it falls behind the camera, coincides with it, or the camera is degenerate (`look_at == position`); the scalar `focus_distance` is used as fallback.

#### **Physical camera: focal length, f-stop, bokeh, white balance**
The same thin lens can be described as on a real camera. These keys win over `fov` and `aperture` when present:

| Key | Default | Meaning |
|---|---|---|
| `focal_length` | - | Focal length in mm. With `sensor_width` it sets the field of view: the sensor width spans the image width, the height follows the image aspect. The lens is treated as focused at infinity (no focus breathing). |
| `sensor_width` | `36` | Sensor width in mm (36 = full frame, 23.5 = APS-C, 17.3 = Micro Four Thirds). |
| `f_stop` | - | f-number N: pupil diameter = focal length / N. Without `focal_length`, the focal length that gives `fov` on that sensor is used. |
| `aperture_blades` | `0` | Number of straight diaphragm blades: `0` = round, `5`-`9` give polygonal bokeh. |
| `aperture_rotation` | `0` | Rotation of the blade polygon, in degrees. |
| `aperture_ratio` | `1` | Width / height of the aperture: below 1 the bokeh becomes a tall oval, as with an anamorphic lens (`0.5` for a 2x squeeze). |
| `ev100` | - | Exposure value at ISO 100, for a scene in photometric units (lights in lumens, candela, nits or lux; a physical sky): the image is exposed as on a camera. 15 for a sunny day (the "sunny 16" rule), 12 overcast, 7-8 a lit interior, 3-5 a dim one. Adds to `--exposure`. |
| `iso`, `shutter_speed` | - | Instead of `ev100`, with `f_stop`: EV100 = log₂(N²/t) − log₂(ISO/100) (`shutter_speed` in seconds, `0.008` = 1/125 s). |
| `white_balance` | - | White balance in kelvin (1667-25000): a light of this colour temperature renders neutral. Lower values cool the image, higher values warm it; `6504` is the identity. Applied to the display image only (EXR stays scene-linear); `--white-balance` on the CLI wins. |

```yaml
cameras:
  - position: [0, 1.2, 0]
    look_at:  [0, 0.9, -2.5]
    focal_length: 50          # a 50 mm lens on full frame: vertical fov 22.9° at 16:9
    f_stop: 1.4               # pupil 35.7 mm (scene in metres, see units: below)
    focus_point: [0, 0.9, -2.5]
    aperture_blades: 6        # hexagonal bokeh
    white_balance: 3200       # tungsten light renders white
```
**Physical exposure.** A scene in nits needs an exposure, as a photograph does:
with `ev100` (or ISO, shutter and f-stop) a surface of luminance `L` reaches the
display transform as `L / (1.2 × 2^EV100)` (the saturation-based convention: an
18% grey card lit as the EV calls for lands near mid-grey). Without it, a
photometric scene is far too bright. See `scenes/review/daylight-units.yaml`
(sunny day, EV100 15) and `scenes/review/light-units.yaml` (interior, EV100 5).

The blur of a point at distance `d` with the focus at `f` is the thin-lens circle of confusion: on the focus plane it has diameter `A |d - f| / d`, where `A` is the pupil diameter. See `scenes/showcases/camera-physical.yaml`.

The millimetre pupil of `f_stop` is turned into scene units by the scene's
`units: { length }` (§2): a scene modelled in centimetres writes
`units: { length: cm }`.

#### **`shutter_interval` - motion-blur exposure interval**
Motion blur integrates the scene over the time the shutter is open. The scene's
animation runs on a normalized timeline `[0, 1]` (see the entity `motion:` key in
§7 and camera `motion:` below); `shutter_interval: [open, close]` selects the sub-interval
each camera sample is exposed over, with `0 ≤ open < close ≤ 1`.
```yaml
cameras:
  - position: [0, 2, 8]
    look_at: [0, 1, 0]
    shutter_interval: [0.0, 1.0]                      # full exposure - integrate the whole motion arc
  # shutter_interval: [0.45, 0.55]         # short exposure - motion nearly frozen (streaks ~10× shorter)
```
Each camera sample draws one time uniformly inside `[open, close]` (one extra
low-discrepancy dimension, taken only when the scene is animated) and the whole
path - camera ray, shadow rays, bounces - is traced at that single instant. A
narrower interval is a shorter exposure: the motion is more frozen and the
streaks shorter. Default `[0, 1]` when anything is animated.

`shutter_interval` lives on the camera, so each entry of a `cameras:` list can carry its
own value (e.g. a "full" and a "short" exposure of the same scene). An invalid
interval is reset to `[0, 1]` with a warning. If nothing in the scene is animated
the shutter is reported and ignored - output stays identical to a still render.

#### **`motion` - camera motion blur**
An optional `motion:` list animates the camera itself. The base
`position`/`look_at`/`up`/`fov` is the implicit keyframe at `time: 0`; each entry
adds a keyframe at its own normalized time, and any field omitted in a keyframe
inherits the base pose. Between keyframes the pose is interpolated and the view
basis rebuilt per ray, so the frame stays orthonormal at every instant.
```yaml
cameras:
  - position: [0, 2, 8]
    look_at: [0, 1, 0]
    fov: 45
    motion:
      - { time: 1.0, position: [1.5, 2, 8], look_at: [0.5, 1, 0] }   # gentle dolly over the exposure
```
---
### 5. **MATERIALS SECTION** - Six Types
Six types: `lambertian`, `metal`, `dielectric`, `emissive`, `openpbr` and
`mix`. Every colour input takes `[r, g, b]`, one grey number, a hex string
(`"#d4af37"`, sRGB-encoded, decoded to linear) or an inline texture block
(§6); every `openpbr` input takes a number or a texture as well. Every type
takes `normal_map`, `bump_map` and a `displacement:` block (§6).

#### **5.1 Lambertian (Diffuse/Matte)**
```yaml
- id: "matte_red"
  type: "lambertian"
  color: [0.8, 0.2, 0.1]                  # albedo, default 0.5
```
- Pure diffuse reflection, no specular highlights
- Most efficient for large surfaces (walls, floors)
- `color` takes a texture block: `color: { type: "checker", scale: 4, colors: [[0.9, 0.9, 0.9], [0.1, 0.1, 0.1]] }`
#### **5.2 Metal (Specular/Mirror)**
```yaml
- id: "brushed_steel"
  type: "metal"
  color: [0.85, 0.85, 0.88]               # Colour at normal incidence (F0), default white
  edge_color: [0.95, 0.95, 0.97]          # (optional) Colour near grazing (F82 tint), default white
  roughness: 0.1                          # GGX roughness, alpha = roughness²; 0 = mirror (default)

- id: "gold"
  type: "metal"
  preset: "gold"                           # Measured metal: exact complex Fresnel
  roughness: 0.25
```
- GGX microfacets with height-correlated masking-shadowing, sampled from the visible normals.
- Two ways to give the colour: `color` + `edge_color` (the F82 model: Schlick's curve from `color`, with the dip toward `edge_color` that real metals show near 82°), or a measured complex index of refraction with `preset` (`gold`, `silver`, `copper`, `aluminium`, `chromium`, `tungsten`, `nickel`, `iridium`, `molybdenum`, `rhodium`, `tantalum`, `titanium_nitride`, `vanadium`, `beryllium`, `mercury`) or explicit `eta: [r, g, b]` + `k: [r, g, b]`. A complex IOR sets the colour by itself: `color` and `edge_color` are then ignored.
- `multiple_scattering` (default `true`): light that bounces more than once between the microfacets is added back (Kulla-Conty), so a rough metal keeps its brightness instead of darkening as the roughness grows. Turn it off only to compare with renderers that lack it.
- `color` takes a texture block.
#### **5.3 Dielectric (Glass/Transparent)**
```yaml
- id: "glass"
  type: "dielectric"
  ior: 1.52                               # IOR (crown glass), default 1.5
  color: [1.0, 1.0, 1.0]                  # (optional) Tint, default white (clear glass)
  roughness: 0.2                          # (optional) Frosted glass, alpha = roughness², default 0 = clear
```
- Transparent with refraction and exact Fresnel reflection
- `roughness` > 0 gives frosted glass: GGX microfacets on both the reflected and the transmitted side, with direct lighting through the surface.
- Radiance is scaled by (n1/n2)² on refraction: a camera under water sees the sky brighter by n², as it should.
- Common IORs: water=1.33, glass=1.5-1.52, diamond=2.42
#### **5.4 Emissive (Self-Luminous)**
```yaml
- id: "neon_blue"
  type: "emissive"
  luminance: 2000                          # nits (cd/m²), default 1
  color: [0.3, 0.5, 1.0]                   # tint (scaled to luminance 1), or a texture block
  # temperature: 3000                      # black-body colour in kelvin, multiplying the tint
```
- Objects glow and emit light into the scene; they join the NEE pool as lights.
- `luminance` is the radiance of the surface in nits. In a scene exposed with the camera's `ev100`, a neon tube is about 1000-5000 nits, an LED panel 3000-10 000, a TV screen 200-500. In a scene without an exposure, values of 1-25 read as a glow to a bright panel.
- A colour is a tint: it is divided by its own luminance, so `luminance` alone sets the brightness. A texture keeps its values and `luminance` scales it.
- Emits only from front faces
#### **5.5 OpenPBR Surface (Layered Uber-Material)**
The OpenPBR Surface v1.1 model of the Academy Software Foundation, with its parameter names and defaults. One material covers plastic, metal, glass, skin, car paint, fabric, paper and soap bubbles, and it conserves energy in every combination: a white material under a uniform white sky disappears (see `scenes/review/openpbr-white-furnace.yaml`).

```yaml
- id: "car_paint"
  type: "openpbr"
  base_color: [0.55, 0.05, 0.08]          # number, [r, g, b], "#rrggbb" or a texture block
  base_metalness: 0.8
  specular_roughness: 0.4
  coat_weight: 1
  coat_roughness: 0.03
  coat_color: [0.95, 0.85, 0.85]          # coat absorption tint
```

Every input takes a number, an `[r, g, b]` list (or one grey number), a hex string or an inline texture block, for example `specular_roughness: { type: "noise", scale: 4, colors: [[0.2,0.2,0.2],[0.5,0.5,0.5]] }`.

| Layer | Parameters (default) |
|---|---|
| Base | `base_weight` (1), `base_color` (0.8), `base_metalness` (0), `base_diffuse_roughness` (0) |
| Specular | `specular_weight` (1), `specular_color` (1), `specular_roughness` (0.3), `specular_roughness_anisotropy` (0), `specular_rotation` (0, degrees around the normal), `specular_ior` (1.5) |
| Transmission | `transmission_weight` (0), `transmission_color` (1), `transmission_depth` (0), `transmission_scatter` (0), `transmission_scatter_anisotropy` (0) |
| Subsurface | `subsurface_weight` (0), `subsurface_color` (0.8), `subsurface_radius` (1, one number), `subsurface_radius_scale` ([1, 0.5, 0.25]), `subsurface_scatter_anisotropy` (0) |
| Coat | `coat_weight` (0), `coat_color` (1), `coat_roughness` (0), `coat_roughness_anisotropy` (0), `coat_ior` (1.6), `coat_darkening` (1), `coat_normal_map` |
| Fuzz | `fuzz_weight` (0), `fuzz_color` (1), `fuzz_roughness` (0.5) |
| Thin film | `thin_film_weight` (0), `thin_film_thickness` (0.5, **micrometres**), `thin_film_ior` (1.4) |
| Emission | `emission_luminance` (0, nits), `emission_color` (1) |
| Geometry | `geometry_opacity` (1), `geometry_thin_walled` (false), `normal_map`, `bump_map` |

The model, lobe by lobe:
- **Metal**: GGX with the F82 Fresnel (`base_color` at normal incidence, `specular_color` near grazing) and multiple scattering, so a rough white metal stays white.
- **Dielectric**: the interface of `specular_ior`; `specular_weight` scales its reflectance at normal incidence (0 removes the reflection, values above 1 raise it). Rough reflection and refraction are GGX microfacets. `specular_roughness_anisotropy` stretches the highlight along the surface tangent, turned by `specular_rotation`.
- **Diffuse**: energy-preserving Oren-Nayar (EON); `base_diffuse_roughness` 0 is Lambert. It lies under the specular layer and receives only the light the specular does not reflect.
- **Transmission**: refraction into the object. With `transmission_depth` 0 the `transmission_color` tints the refraction once on the way in; with a depth it is the transmittance at that depth (Beer-Lambert inside). With `transmission_scatter` and a depth, the interior is a scattering medium.
- **Subsurface**: light enters through the same interface and walks in a medium the loader binds to the object: mean free path `subsurface_radius × subsurface_radius_scale` per channel, single-scattering albedo chosen so that the object's overall colour is `subsurface_color`.
- **Coat**: a clear or tinted lacquer of `coat_ior` over everything below. `coat_color` is the tint at normal incidence and darkens towards grazing; `coat_darkening` 1 keeps the physical darkening from internal reflections, 0 removes it. A rough coat also roughens the base.
- **Fuzz**: the sheen of fibres or dust on top (velvet, peach skin), tinted by `fuzz_color`.
- **Thin film**: iridescent interference on the base reflection (soap bubbles, oil, anodised metal).
- **Emission**: radiance `emission_color × emission_luminance`, under the coat; not sampled as a light (use `emissive` for light sources).
- **Thin-walled**: a sheet with no interior (leaves, paper, windows). Transmission is a sheet of glass; subsurface becomes diffuse reflection and transmission split by `subsurface_scatter_anisotropy`.

Notes:
- Transmission and subsurface share the object's interior: with `subsurface_weight` > 0 the interior is the subsurface medium.
- Dispersion (`transmission_dispersion_*`) is not supported: the renderer works in RGB.
- **⚠️ Noise:** OpenPBR has more lobes than the simple types; use ~4× samples for skin/glass/coat hero shots, and lambertian for big surfaces.

Typical settings:
- Metals: `base_metalness: 1`, varied `specular_roughness`; add `specular_roughness_anisotropy` for brushed steel.
- Plastics: `specular_roughness: 0.4-0.8`.
- Car paint: `coat_weight: 1`, `coat_roughness: 0.03`.
- Fabric / velvet: `fuzz_weight: 0.8-1`, `fuzz_roughness: 0.3-0.5`.
- Clear glass: `transmission_weight: 1`, `specular_roughness: 0`, `specular_ior: 1.52`; coloured glass adds `transmission_color` + `transmission_depth` (e.g. 5 units for a bottle of brandy).
- Soap bubble / opal: `thin_film_weight: 1`, `thin_film_thickness: 0.35-0.7`, `thin_film_ior: 1.33-1.5`.
- Leaves / paper: `geometry_thin_walled: true`, `subsurface_weight: 1`, `subsurface_scatter_anisotropy` between -1 (all reflected) and 1 (all transmitted).

##### **Material-embedded SSS**

`subsurface_weight` > 0 on an `openpbr` material is the shortcut path to
subsurface scattering: the loader builds a `HomogeneousMedium` and binds it
to every entity that uses the material and does **not** already carry an
explicit `interior_medium`.

```yaml
materials:
  - id: marmo_carrara
    type: openpbr
    base_color: [0.92, 0.89, 0.85]
    specular_roughness: 0.25
    subsurface_weight: 1
    subsurface_color: [0.95, 0.90, 0.85]          # albedo the surface shows
    subsurface_radius: 1                          # mean free path in scene units
    subsurface_radius_scale: [0.45, 0.35, 0.22]   # per RGB channel

entities:
  - name: scultura
    type: sphere
    center: [0, 0.8, 0]
    radius: 0.35
    material: marmo_carrara
    # no interior_medium, no mediums: section - SSS still works
```

**How the medium is built.** Let `A` be `subsurface_color` and `r` the
mean free path `subsurface_radius × subsurface_radius_scale` per channel. The
medium's single-scattering albedo `α` is the one whose thick slab shows `A`
under uniform light (the inversion of Chiang, Kutz and Burley 2016 with the
anisotropy term of OpenPBR):

```
s   = 4.09712 + 4.20863 A − √(9.59217 + 41.6808 A + 17.7126 A²)
α   = (1 − s²) / (1 − g s²)
σ_t = 1 / r
σ_s = α · σ_t
σ_a = (1 − α) · σ_t
phase = HG(g = subsurface_scatter_anisotropy)   # isotropic when g ≈ 0
```

The resulting medium is anonymous (no `mediums:` `id`) and lives only for
the lifetime of the scene. Textured subsurface inputs give a medium of their
default value. A thin-walled material has no interior, so no medium.

**Precedence.** An explicit `interior_medium` on the entity always wins over
the material-embedded medium: one material can be reused across entities,
each one swapping the volume if it needs to. Use the entity-bound path
(top-level `mediums:` block + `interior_medium`) when two entities must use
different volumes for the same surface look, or when the volume is
heterogeneous (`procedural` / `grid`).

##### **From the Disney parameters (format 1)**

Format 1 had a `disney` type (Burley 2012/2015). It is gone from format 2:
its light transport was already OpenPBR's, and `SceneMigrate` (§13) writes
every Disney material as the `openpbr` material it built:

| Disney (format 1) | OpenPBR (format 2) |
|---|---|
| `color` / `texture` | `base_color` |
| `metallic` | `base_metalness` |
| `roughness` (0.5) | `specular_roughness` |
| `specular` (0.5) | `specular_weight` = 2 × specular (1 on a full metal) |
| `specular_tint` | `specular_color` = white blended towards the hue of the base colour |
| `anisotropic`, `anisotropic_rotation` | `specular_roughness_anisotropy` = 0.9 × anisotropic, `specular_rotation` = 360° × rotation |
| `ior` | `specular_ior` |
| `spec_trans`, `transmission_color`, `transmission_depth` | `transmission_weight`, `transmission_color` (squared when there is no depth: format 1 tinted every refraction), `transmission_depth` |
| `subsurface_radius` × `subsurface_scale`, `subsurface_color`, `subsurface_anisotropy` | `subsurface_weight` (= `spec_trans`, default 1), `subsurface_radius_scale`, `subsurface_color`, `subsurface_scatter_anisotropy` |
| `diff_trans`, `thin_walled` | `geometry_thin_walled: true`, `subsurface_weight: 1`, `subsurface_scatter_anisotropy` = 2 × diff_trans − 1 |
| `clearcoat`, `clearcoat_gloss` / `coat_roughness`, `coat_ior` (1.5) | `coat_weight`, `coat_roughness` = √(0.1 − 0.099 × gloss) when not given, `coat_ior` |
| `sheen`, `sheen_tint`, `sheen_roughness` | `fuzz_weight`, `fuzz_color` (tinted towards the base hue), `fuzz_roughness` |
| `thin_film_thickness` (nm), `thin_film_ior` | `thin_film_weight: 1`, `thin_film_thickness` (µm = nm / 1000), `thin_film_ior` |
| `subsurface`, `flatness` | none (they had no effect since the fake-SSS lobes were removed) |

#### **5.6 Mix Material (Blend Two Materials)**
```yaml
- id: "rusty_metal"
  type: "mix"
  material_a: "chrome"
  material_b: "rust"
  blend: 0.3                               # share of material_b: a number (default 0.5)...
  # blend:                                 # ...or a texture mask for spatial blending
  #   type: "noise"
  #   scale: 3.0
  #   noise_strength: 5.0
  # normal_map: { path: "textures/rust-normal.png" }   # optional, on top of both
```
- Seamlessly blend any two materials
- A mask can be any texture: `noise`, `marble`, `wood`, `checker`, `voronoi`, `image`, ...
- Useful for: weathering, wear, transitions, decals
- Mix-of-mix nesting supported

---
### 6. **TEXTURES** - Embedded in Materials
Textures are defined **within** material definitions. All procedural textures
are pro-grade, with a comprehensive set of controls for noise, voronoi, marble,
wood, brick, gradient and coordinate textures.

#### **Sampling space (metric object-local).**
Every procedural samples on `rec.LocalPoint`, which is the shading point in the
object's **own axes, in world units** - the entity's `scale` is applied but its
rotation and translation are not. This is the metric object-space projection
production renderers use for procedural texturing.

The key consequence: **the texture's feature size is set by its own `scale` and
is invariant to the entity's scale.** Stretching a box 10× on X does *not*
stretch or deform the pattern - it shows 10× as many features of the *same*
size, exactly like a longer plank has more growth rings of the same width.
Non-uniform `scale` therefore never turns wood rings into ellipses or smears a
marble vein. The pattern stays attached to the object's axes/origin, so it
rotates and moves with the object.

`scale` is an absolute frequency in **cycles per world unit**: at `scale: 4` a
wood ring (or noise cell, marble band) is ≈ `1/4 = 0.25` world units wide on
every object, regardless of how large or non-uniformly scaled that object is.
Pick `scale` from the desired real feature size, not from the object's size:

| Desired feature size | `scale` |
|----------------------|---------|
| coarse (~0.5 wu)     | `~2`    |
| medium (~0.25 wu)    | `~4`    |
| fine (~0.1 wu)       | `~10`   |

If you instead want the **normalised** unit-cube workflow (a pattern that always
fits the object's bounds), stack a `coordinate` texture with `mode: "generated"` (plus
explicit `bounds`); for a **world-locked** pattern that continues seamlessly
across many tiled objects use `mode: "world"`.

#### **Anisotropic scale (per-axis frequency).**
`scale` also accepts a per-axis `[sx, sy, sz]` vector to *deliberately* stretch
the pattern along the object's own axes - independently of the entity's scale.
A scalar (`scale: 4`) is isotropic and unchanged; a vector gives a different
frequency per axis:

```yaml
color:
  type: "wood"
  scale: [8, 1, 1]      # 8 cycles/wu on X, 1 on Y and Z → grain stretched along X
  offset:   [0, 0, 0]   # optional sample-point translation (world units)
  rotation: [0, 0, 0]   # optional sample-point rotation (degrees, X→Y→Z)
```

Honoured by the solid procedurals - `noise`, `marble`, `wood`, `voronoi`. Each
component is `cycles/wu` on that axis, so `[8, 8, 8]` is identical to the scalar
`8`. The dominant component drives the anti-aliasing octave clamp, so stretching
never introduces aliasing. `offset` and `rotation` (both 3D, applied to the
sample point before the frequency) compose with the vector scale. All three
default to a no-op, so existing scenes are unaffected.

#### **Procedural Textures:**

**Checker:**
```yaml
color:
  type: "checker"
  scale: 4.0
  colors: [[0.9, 0.9, 0.9], [0.1, 0.1, 0.1]]
```

**Noise:**
```yaml
color:
  type: "noise"
  noise_type: "fbm"            # perlin | fbm | turbulence | ridged | billow | hetero_terrain | hybrid_multifractal
  scale: 5.0
  octaves: 5                   # 1..16 - fBm/ridged/billow/musgrave octave count
  lacunarity: 2.0              # frequency multiplier between octaves
  gain: 0.5                    # amplitude decay between octaves (fbm/ridged/billow)
  fractal_increment: 1.0       # Musgrave H - only used by hetero_terrain / hybrid_multifractal
  fractal_offset: 0.7          # Musgrave offset / "sea level" - only used by hetero_terrain / hybrid_multifractal
  distortion: 0.0              # domain-warp amplitude (organic shapes)
  noise_strength: 0.0          # 0=smooth Perlin, >0=turbulent (overridden by noise_type)
  colors: [[0, 0, 0], [1, 1, 1]]
```
The lattice noise under every procedural built on it (`noise`, `marble`,
`wood`, the domain warp of `voronoi`, the per-brick variation of `brick`) is
improved Perlin noise: its curvature is continuous across the lattice, so a
bump of the noise shows no creases along the grid, and it looks the same
along every direction.

The seven noise families map onto the standard pro-renderer modes:
- `perlin` - single-octave smooth gradient noise.
- `fbm` - Σ noise/2^i, the canonical "fractal noise".
- `turbulence` - Σ|noise|/2^i with absolute-value sharpening.
- `ridged` - Musgrave ridged multifractal, sharp ridges (rocks, lightning).
- `billow` - Σ|noise| octaves, puffy / cloud-like.
- `hetero_terrain` - Musgrave heterogeneous terrain (Ebert et al. §16.3.3):
  per-octave amplitude scaled by the running accumulated value, so high
  ground gets rougher and valleys stay smooth. The canonical eroded-terrain
  look that pure fBm cannot reach.
- `hybrid_multifractal` - Musgrave hybrid multifractal (§16.3.4): per-octave
  signal multiplied by a running `weight` (clamped to 1), producing
  stratified rock layers and sharp peaks. Used for asteroids, alien rock,
  stratigraphic marble.

`distortion` warps the input position with a secondary Perlin sample
(Inigo Quilez technique); 0.3–0.8 is usually enough. `fractal_increment`
(Musgrave's H, default 1.0) controls how fast high-frequency octaves decay
- H ≈ 0.25 yields rough terrain, H ≥ 1 produces smooth, low-frequency
dominated fields. `fractal_offset` (default 0.7) is the "sea level" bias
added to each octave; higher values flatten valleys, lower values turn
everything into mountains. These two parameters are only used by the
`hetero_terrain` / `hybrid_multifractal` modes - the other noise kinds
ignore them.

**Marble** - production-grade ridged multifractal + recursive (Inigo Quilez)
domain warp + anisotropic geological fold + optional mineral impurities.
No periodic carrier - every visible
tiling is killed by the recursive warp.

```yaml
color:
  type: "marble"
  scale: 2.4
  colors: [[0.96, 0.95, 0.94], [0.32, 0.34, 0.40]]
  vein_axis: [0, 1, 0]         # orientation of the dominant fold direction

  # Recursive (IQ) domain warp - kills tiling, produces organic flow
  warp_amplitude: 0.9          # world-unit displacement of the warp field
  warp_scale: 2.0              # spatial period of the warp field
  warp_iterations: 2           # 0 = baseline, 2 = canonical, 3 = aggressive

  # Anisotropic geological fold - large-scale tectonic shear
  fold_amplitude: [0.8, 0.25, 0.45]   # per-axis amplitude (max → vein_axis)
  fold_scale: 6.0              # period of the fold field

  # Multi-scale ridged vein field - soft-max compositing
  vein_layers: 2               # 1..3 independent layers
  vein_scale:  [1.0, 2.4]      # per-layer scale (length = vein_layers)
  vein_weight: [1.0, 0.50]     # per-layer soft-max weight
  octaves: 5
  lacunarity: 2.0
  gain: 0.5
  soft_max_sharpness: 8        # crispness of the layer composite

  # Vein-thickness remap (replaces the broken legacy vein_sharpness)
  vein_thickness: 0.13         # 0..1 - fraction of surface occupied by veins
  vein_softness: 0.07          # smoothstep half-width on the threshold

  # Background tonal variation
  background_scale: 12.0
  background_octaves: 3
  color_variation: 0.08        # how much background fBm shifts ramp lookup

  # Mineral impurities (Verde Alpi, Blu Sodalite specks)
  impurities_density: 0.0      # 0 disables; ~0.05 = Verde Alpi inclusions
  impurities_scale: 8.0
  impurity_weight: 0.12
  # impurities_texture: { type: "voronoi", ... }   # optional external override

  # Anisotropic pre-stretch (geological compression)
  space_stretch: [1.0, 1.0, 1.0]   # (0.4, 1.8, 1.0) = horizontal compression

  # Secondary linear cracks (Worley F2 − F1 overlay)
  cracks_density: 0.0          # 0 disables; ~0.30 = Marquinia/Calacatta cracks
  cracks_scale: 2.0
  cracks_softness: 0.04        # 0.02 = razor-thin, 0.10 = soft branching
  cracks_weight: 0.9           # soft-max weight vs the ridged layer

  # Output mode - `color` (default) or `mask`
  output: "color"              # set to "mask" to drive specular_roughness etc.

  randomize_offset: true
  # color_ramp: [...]          # optional multi-stop ramp; wins over `colors`
```

> **Mask-driven parameters.** Set `output: "mask"` on a marble
> texture block to return the scalar vein parameter `t ∈ [0, 1]` packed
> as `(t, t, t)`. This is the canonical way to drive `specular_roughness`,
> `fuzz_weight` &co. from the same vein pattern the colour uses: vein
> zones can be glossier than the matte base, fuzz can ride on the base
> only. Duplicate the marble block under the appropriate input
> with `output: "mask"` and (optionally) a 2-stop `color_ramp` that
> remaps `[0, 1]` to the target parameter range. The double evaluation
> costs ~26 extra Perlin samples per shade on the protagonist material -
> negligible against the BSDF + NEE.

> **Anisotropic stretch vs fold.** `space_stretch` is a linear
> pre-multiply on the sample point - a uniform compression that runs
> BEFORE the fold and warp. Use it for the long, planar "bedded" look of
> stratified marbles (the Y-comp of Statuario, the horizontal compression
> of Verde Alpi). The fold (`fold_amplitude`) is a non-linear noise-driven
> shear that produces curved geological deformation. Both compose
> multiplicatively in the sample-point space.

> **Cracks vs vein layers.** The ridged multi-scale field produces
> organic vein-like ridges; the cracks Worley overlay produces sharp
> long linear fractures (F2 − F1 ridge between cells). They compose via
> soft-max so the two networks coexist cleanly. Set `cracks_density: 0`
> to skip the Worley evaluation entirely - no perf cost when disabled.

The vein axis no longer drives a sinusoidal carrier - it controls only the
dominant direction of the anisotropic fold. Real organic flow comes from the
recursive warp + ridged multifractal pair. Bumping `warp_iterations` from
2→3 doubles the per-shade Perlin sample cost (~14 → ~17 lattice samples on
top of the ridged layers) but visibly tames any residual tiling on
high-resolution slabs.

`vein_thickness` is strictly monotone wrt visible vein area: 0.12-0.18 ≈
Carrara/Statuario (thin), 0.22-0.30 ≈ Calacatta/Port Laurent (medium),
0.30-0.40 ≈ Arabescato (chaotic bands), 0.40+ ≈ onyx/alabaster (diffuse
nebulae rather than veins). `vein_softness` controls how crisp the
transition is - 0.04-0.08 = razor-sharp edges (Marquina), 0.15-0.25 =
watery (onyx).

> **Impurity composability.** The default inline `impurities_density` path
> uses a sparse Voronoi cell hash - cheap and YAML-flat. When the inline
> path is too restrictive (you want the impurities to follow an image,
> a custom Voronoi, or any other procedural), set `impurities_texture` to
> a full nested texture block; its luminance overrides the inline path
> regardless of `impurities_density`.

**Wood:**
```yaml
color:
  type: "wood"
  scale: 4.0
  noise_strength: 1.5          # high-freq grain amplitude
  ring_axis: [0, 1, 0]         # trunk axis; rings ⊥ axis (default Y)

  # Asymmetric earlywood/latewood ring profile (replaces the legacy
  # symmetric pow(triangle, sharpness)). Real annual rings have a long
  # bright earlywood plateau ending in a thin sharp dark latewood band.
  latewood_width: 0.22         # 0.15-0.20 hardwoods, 0.25-0.30 softwoods
  ring_sharpness: 3.0          # 1=soft, 3-6=razor latewood edge
  earlywood_transition: 0.05   # smooth rise from prior latewood (0.005-0.5)

  # Per-ring random variation - the single biggest realism upgrade.
  # 0 = every ring identical (the "CG wood" look); 0.15 = natural year-
  # to-year variation. Width offset uses the same per-ring hash.
  ring_color_variation: 0.15
  ring_width_variation: 0.10

  # Recursive IQ domain warp (replaces single-iter `distortion`).
  warp_amplitude: 0.4
  warp_scale: 2.5
  warp_iterations: 2           # 0=off, 2=canonical IQ, 3=heavy flow

  # Anisotropic geological fold - large-scale trunk bending.
  fold_amplitude: [0.3, 0.1, 0.3]
  fold_scale: 4.0

  # Anisotropic space pre-stretch (non-isotropic plank cuts).
  space_stretch: [1.0, 1.0, 1.0]

  # Multi-band noise on the radial distance.
  grain_scale: 1.0             # frequency mult on the grain fBm
  octaves: 4
  lacunarity: 2.0
  gain: 0.5
  figure_strength: 0.0         # 0 disables; 0.4-1.5 = curly / flame / ribbon
  figure_scale: 0.25
  figure_aspect: 1.0           # axial elongation of figure; 3-5 = perpendicular stripes
  axial_grain: 0.0             # long-wave noise along the trunk axis

  # Open-pore vessels (oak, ash, walnut). 0 disables.
  pore_density: 0.0            # 0.30-0.55 for open-pore species
  pore_scale: 16.0
  pore_aspect: 4.0             # axial elongation; 4-6 = vessel-like
  pore_strength: 0.4

  # Sapwood/heartwood radial gradient. 0 disables.
  heartwood_radius: 0.0
  heartwood_blend: 0.25        # +ve darkens centre; natural for walnut/cherry

  # Quartersawn vs plain-sawn cut.
  radial_anisotropy: 0.0       # 0 = isotropic, 2-5 = quartersawn medullary rays

  # Knots (3D-cone projection). 0 disables.
  knot_density: 0.0            # 0.5-1.0 = pine/spruce/cedar
  knot_scale: 0.6              # frequency multiplier; higher = more knots

  # Output mode - `color` (default) or `mask`.
  output: "color"              # set to "mask" to drive specular_roughness &c

  colors: [[0.85, 0.65, 0.40], [0.45, 0.28, 0.14]]
  # color_ramp: [...]          # optional multi-stop ramp; wins over `colors`

  randomize_offset: true
  randomize_rotation: true
```

> **Production-grade wood controls.** The texture was rewritten end-to-end
> to a production-grade annual-ring model. All knobs above are active
> by default with sensible values -
> the legacy "every ring looks the same" symmetric profile is gone.
>
> * **Asymmetric ring profile.** `latewood_width` controls the width of
>   the dark band at the END of each annual ring (not centred on the
>   middle as in the legacy symmetric pow-of-triangle). The boundary
>   between two rings is the visible "dark line" of real wood. Combine
>   with `ring_sharpness` (1-6) to dial the crispness of that boundary.
> * **Per-ring colour + width variation.** `ring_color_variation` and
>   `ring_width_variation` apply a deterministic per-ring hash so adjacent
>   rings differ in brightness and width - the single feature that makes
>   wood look real instead of CG. The hash combines the integer ring index
>   with the object seed, so different instances see different ring
>   sequences while staying reproducible across renders. Keep both around
>   0.10-0.25 for natural year-to-year variation; 0 is the legacy "every
>   ring identical" look (avoid).
> * **Recursive IQ domain warp.** `warp_amplitude` + `warp_iterations`
>   are a recursive domain warp. 2 iterations is the
>   canonical IQ recipe; 3 produces aggressive geological flow.
> * **Anisotropic geological fold.** `fold_amplitude` + `fold_scale` apply
>   a large-scale anisotropic shear BEFORE the recursive warp - the warp
>   then operates in the folded space, producing the kind of curved ring
>   patterns seen near trunk bends.
> * **Multi-band noise.** `noise_strength` (high-freq fBm, fibre detail)
>   + `figure_strength` (low-freq, curly maple / flame mahogany ripples)
>   + `axial_grain` (long-wave along axis). The figure band can be
>   axially elongated via `figure_aspect` to align its stripes
>   perpendicular to the grain - the natural orientation of curly maple
>   and flame mahogany.
> * **Open-pore vessels.** `pore_density` spawns sparse dark micro-specks
>   via an axially-anisotropic Worley - the cells are elongated along
>   the trunk axis by `pore_aspect` so the vessels look like the short
>   cylindrical channels of real open-pore species (oak 0.45, ash 0.42,
>   walnut 0.40, mahogany 0.25). 0 = closed-pore (maple, beech, cherry,
>   ebony) and bypasses the Worley evaluation entirely.
> * **Sapwood / heartwood gradient.** `heartwood_radius` defines the
>   radial transition centre; `heartwood_blend > 0` darkens toward the
>   centre, modelling the heartwood/sapwood demarcation of walnut, cherry,
>   ipe. Set `heartwood_radius: 0` (default) to disable the path.
> * **`radial_anisotropy`.** Stretches the noise sample along the local
>   radial direction (perpendicular to `ring_axis`). High values (~3-5)
>   reproduce the quartersawn-oak medullary "tiger ray" look.
> * **`knot_density`.** 3-D cone projection - each sparse Worley cell
>   hosts a knot whose visible cone widens with axial distance from the
>   cell centre. Inside the cone the ring centre is pulled toward the
>   knot feature and a dark heart is added on top. Combine with a 4-5 stop
>   `color_ramp:` for cuore-nodo / latewood / earlywood / sapwood
>   tone authoring.

> **Mask-driven parameters.** Set `output: "mask"` on a wood
> texture block to return the scalar ring parameter `t ∈ [0, 1]` (1 at
> the bright earlywood plateau, 0 at the dark latewood / pore) packed
> as `(t, t, t)`. Drop the same block under `specular_roughness` /
> `fuzz_weight` / etc. to drive scalar BSDF parameters from the
> latewood pattern - latewood can be polished while earlywood stays
> matte (the cera-su-quercia look), fuzz can ride on the open-pore
> earlywood only. The double evaluation costs ~30 extra Perlin samples
> per shade on the protagonist material - negligible against the
> BSDF + NEE.

#### **Production-quality marble & wood - recipe book**

The studio-quality knobs interact non-trivially with the BSDF and the
lighting setup. The recipes below are collected in the
`scenes/presets/materials-stone.md` and `materials-wood.md` preset
catalogues; copy the matching snippet and tweak the colour ramp to ship a
credible material in minutes.

> **Lighting checklist before tuning a marble.** A polished marble at
> `roughness < 0.2` becomes a near-mirror that reflects the environment
> verbatim - if the sky is bright and untextured the marble reads as
> "blue gradient" instead of marble. Three rules:
>
> 1. **Use a dark or near-black sky for lookdev shots** (`type: "flat"`,
>    `color: [0.001, 0.001, 0.0012]`). The marble pattern then carries
>    the visual, not the environment.
> 2. **Roughness 0.30–0.34 for "lucido"** marble where the texture must
>    read; raise clearcoat (0.85+) for the polished glass-like top layer.
>    Lower roughness only when you want a true mirror finish - for
>    that, you typically *do* want some HDRI reflections.
> 3. **Direct lighting must dominate.** A directional key at intensity
>    5–7 plus a cool fill and a warm rim point lights light up the
>    diffuse component above the specular reflection. Without this
>    triad the BSDF integrator can't separate texture from environment.

**Carrara - white base with thin grey-blue veins.**
```yaml
- id: "carrara"
  type: openpbr
  specular_roughness: 0.18
  specular_weight: 1.1
  coat_weight: 0.65
  coat_roughness: 0.08
  base_color:
    type: "marble"
    scale: 2.4
    colors: [[0.96, 0.95, 0.94], [0.32, 0.34, 0.40]]
    vein_axis: [0, 1, 0]
    warp_amplitude: 0.9
    fold_amplitude: [0.8, 0.25, 0.45]
    vein_layers: 2
    vein_scale:  [1.0, 2.4]
    vein_weight: [1.0, 0.50]
    vein_thickness: 0.13
    vein_softness: 0.07
    color_variation: 0.08
    randomize_offset: true
  coat_ior: 1.5
```
Default vein_thickness 0.13 leaves the surface dominantly white with thin
ridged cracks. The fold + recursive warp curve the veins organically along
`vein_axis` - no straight lines no matter the camera angle.

**Calacatta Gold - 3 layers + ramp 4-stop from cream to gold to dark.**
```yaml
- id: "calacatta_gold"
  type: openpbr
  specular_roughness: 0.10
  coat_weight: 0.92
  coat_roughness: 0.06
  specular_weight: 1.4
  base_color:
    type: "marble"
    scale: 1.9
    vein_axis: [0, 1, 0]
    warp_amplitude: 1.1
    warp_iterations: 2
    fold_amplitude: [0.95, 0.35, 0.55]
    vein_layers: 3
    vein_scale:  [0.65, 1.5, 3.4]
    vein_weight: [1.0, 0.70, 0.40]
    vein_thickness: 0.22
    vein_softness: 0.10
    color_variation: 0.09
    randomize_offset: true
    color_ramp:
      - { position: 0.00, color: [0.97, 0.95, 0.90], interp: "linear" }
      - { position: 0.30, color: [0.92, 0.85, 0.72], interp: "smoothstep" }
      - { position: 0.65, color: [0.85, 0.62, 0.28], interp: "smoothstep" }
      - { position: 1.00, color: [0.18, 0.10, 0.05], interp: "linear" }
  coat_ior: 1.5
```
Convention: ramp **position 0 = base** (`t → 0`, dominant area where ridged
field is low), **position 1 = vein** (`t → 1`, rare peaks of the ridged
field); intermediate stops paint the gold transition. The 3-layer system
guarantees thin AND thick veins coexist on the same slab.

**Arabescato - chaotic 3-layer + extreme warp.**
```yaml
- id: "arabescato"
  type: openpbr
  specular_roughness: 0.18
  coat_weight: 0.65
  coat_roughness: 0.08
  base_color:
    type: "marble"
    scale: 1.8
    vein_axis: [0, 1, 0]
    warp_amplitude: 1.4
    warp_iterations: 3            # aggressive flow
    fold_amplitude: [1.1, 0.5, 0.7]
    vein_layers: 3
    vein_scale:  [0.55, 1.4, 3.2]
    vein_weight: [1.0, 0.75, 0.45]
    vein_thickness: 0.34          # broad bands (Arabescato is bold)
    vein_softness: 0.12
    color_variation: 0.10
    randomize_offset: true
    color_ramp:
      - { position: 0.00, color: [0.94, 0.92, 0.88], interp: "linear" }
      - { position: 0.55, color: [0.55, 0.50, 0.48], interp: "smoothstep" }
      - { position: 1.00, color: [0.08, 0.08, 0.10], interp: "linear" }
  coat_ior: 1.5
```

**Calacatta lucido - mask-driven roughness for the "lavorato" look.**
The canonical pro recipe: the same marble block drives BOTH the colour
ramp AND the specular roughness. Vein zones become near-mirror (`specular_roughness
0.06`), matte base stays at `0.18` - the slab reads as a real
polished surface where the vein materials behave differently from the
matrix.
```yaml
- id: "calacatta_pro"
  type: openpbr
  base_color:
    type: "marble"
    scale: 1.9
    vein_axis: [0, 1, 0]
    warp_amplitude: 1.1
    fold_amplitude: [0.95, 0.35, 0.55]
    vein_layers: 3
    vein_scale:  [0.65, 1.5, 3.4]
    vein_weight: [1.0, 0.70, 0.40]
    vein_thickness: 0.22
    vein_softness: 0.09
    cracks_density: 0.25            # linear fractures (Worley)
    cracks_scale: 3.5
    randomize_offset: true
    color_ramp:
      - { position: 0.00, color: [0.97, 0.95, 0.90], interp: "linear" }
      - { position: 0.30, color: [0.92, 0.85, 0.72], interp: "smoothstep" }
      - { position: 0.65, color: [0.85, 0.62, 0.28], interp: "smoothstep" }
      - { position: 1.00, color: [0.18, 0.10, 0.05], interp: "linear" }
  specular_roughness:
    type: "marble"
    output: "mask"                  # ← returns the scalar t as (t,t,t)
    scale: 1.9
    vein_axis: [0, 1, 0]
    warp_amplitude: 1.1
    fold_amplitude: [0.95, 0.35, 0.55]
    vein_layers: 3
    vein_scale:  [0.65, 1.5, 3.4]
    vein_weight: [1.0, 0.70, 0.40]
    vein_thickness: 0.22
    vein_softness: 0.09
    cracks_density: 0.25
    cracks_scale: 3.5
    randomize_offset: true
    color_ramp:
      - { position: 0.0, color: [0.28, 0.28, 0.28] }   # base → roughness 0.28
      - { position: 1.0, color: [0.06, 0.06, 0.06] }   # vein → roughness 0.06
  coat_weight: 0.92
  coat_roughness: 0.05
  coat_ior: 1.5
```
The two marble blocks must use the same `scale`/`warp_*`/`vein_*` so
the colour and mask are spatially in phase. The two-stop ramp on the
mask maps `[0, 1]` to the desired roughness range. Same pattern works
for `fuzz_weight` (fuzz on the matte base only), `specular_weight`,
etc.

**Verde Alpi - green base with mineral impurities (olivine specks).**
```yaml
- id: "verde_alpi"
  type: openpbr
  specular_roughness: 0.20
  coat_weight: 0.55
  coat_roughness: 0.12
  base_color:
    type: "marble"
    scale: 2.3
    vein_axis: [0, 1, 0]
    warp_amplitude: 1.05
    fold_amplitude: [0.85, 0.30, 0.55]
    vein_layers: 2
    vein_scale:  [0.85, 2.0]
    vein_weight: [1.0, 0.55]
    vein_thickness: 0.20
    vein_softness: 0.09
    color_variation: 0.10
    impurities_density: 0.06      # ← inline Voronoi specks
    impurities_scale: 9.0
    impurity_weight: 0.20
    randomize_offset: true
    color_ramp:
      - { position: 0.00, color: [0.16, 0.40, 0.22], interp: "linear" }
      - { position: 0.55, color: [0.06, 0.22, 0.10], interp: "smoothstep" }
      - { position: 1.00, color: [0.02, 0.08, 0.04], interp: "linear" }
  coat_ior: 1.5
```
The inline impurities path uses a sparse Voronoi cell hash with smoothstep
falloff to produce per-cell dark specks. Replace with `impurities_texture:
{ ... }` to drive impurities from any other texture (image, custom Voronoi,
crackle pattern).

**Oak quartersawn - fibrous radial grain.**
```yaml
- id: "oak_quartersawn"
  type: openpbr
  specular_roughness: 0.55
  base_color:
    type: "wood"
    scale: 4.5
    ring_axis: [0, 1, 0]
    ring_sharpness: 4.0           # crisp latewood band
    noise_strength: 2.2
    octaves: 5
    radial_anisotropy: 3.0        # quartersawn stretch
    color_ramp:
      - { position: 0.00, color: [0.30, 0.18, 0.08], interp: "smoothstep" }
      - { position: 0.55, color: [0.82, 0.62, 0.38], interp: "smoothstep" }
      - { position: 1.00, color: [0.95, 0.82, 0.62], interp: "linear"     }
```
The grain "stretches" along the local radial direction. Combine with a
3-stop ramp for sapwood / heartwood / earlywood authoring.

**Curly maple - wide rippled figure.**
```yaml
- id: "curly_maple"
  type: openpbr
  specular_roughness: 0.42
  base_color:
    type: "wood"
    scale: 5.0
    ring_sharpness: 5.0           # tight bands → "curly" look
    noise_strength: 0.25          # near-mute the grain so figure dominates
    figure_scale: 0.10            # low freq → wide ripples
    figure_strength: 1.8
    color_ramp:
      - { position: 0.00, color: [0.55, 0.38, 0.20], interp: "smoothstep" }
      - { position: 0.45, color: [0.85, 0.72, 0.48], interp: "smoothstep" }
      - { position: 1.00, color: [0.98, 0.92, 0.76], interp: "linear"     }
```

**Knotty pine - branch knots with dark hearts.**
```yaml
- id: "knotty_pine"
  type: openpbr
  specular_roughness: 0.55
  base_color:
    type: "wood"
    scale: 6.0                    # high scale so knots hold visible inner rings
    ring_sharpness: 4.0
    noise_strength: 0.6
    figure_scale: 0.25
    figure_strength: 0.3
    knot_density: 1.0             # max number of knots
    color_ramp:
      - { position: 0.00, color: [0.05, 0.03, 0.02], interp: "smoothstep" }  # knot heart
      - { position: 0.18, color: [0.35, 0.18, 0.08], interp: "smoothstep" }  # latewood
      - { position: 0.65, color: [0.90, 0.68, 0.40], interp: "smoothstep" }  # earlywood
      - { position: 1.00, color: [0.97, 0.86, 0.60], interp: "linear"     }  # sapwood
```
Use a **4-stop ramp** when `knot_density > 0`: position 0 reserves the
darkest tone for the knot heart, positions 0.18–0.65 hold the normal
ring band gradient, position 1 is the brightest sapwood.

A pre-baked catalogue of these recipes - Carrara, Calacatta, Statuario,
Arabescato, Port Laurent, Rosso Levanto + oak quartersawn, curly maple,
flame mahogany, knotty pine, bird's-eye maple, walnut burl, frassino
quartersawn, fir knotty - ships as copy-paste presets in
`scenes/presets/materials-stone.md` and `materials-wood.md` under the
`_studio` suffix. Copy the recipe into your scene's `materials:` block and
reference it by id.

**Voronoi / Worley (cellular):**
```yaml
color:
  type: "voronoi"
  scale: 5.0
  metric: "euclidean"          # euclidean | euclidean_squared | manhattan | chebyshev
  output: "f1"                 # f1 | f2 | f3 | f4 |
                               # f2_minus_f1 | f3_minus_f1 |
                               # f1_plus_f2 | cell | random | position
  randomness: 1.0              # 0 = grid, 1 = full random scatter
  distortion: 0.0              # Perlin warp before lookup
  smoothness: 0.0              # 0 = hard min (classic); ∈ (0,1] enables IQ Smooth Voronoi
  colors: [[0, 0, 0], [1, 1, 1]]   # palette endpoints, ignored for "cell" and "position"
```
The voronoi texture: `f1` gives stone/pebble blobs,
`f2_minus_f1` gives sharp "crackle" ridges (cracked-mud, snake-skin),
`random` gives palette-aware per-cell stochastic colour (rocks,
scales, mosaics). The Chebyshev metric reproduces hex/square tiling.

> **Per-cell stochastic ID - `cell` vs `random` vs `position`.** Three
> per-cell channels with distinct roles:
> - `cell` - **raw RGB hash** of the cell ID. Saturated rainbow per cell,
>   **ignores `colors:` and `color_ramp:`**. Use it when you want an
>   unconstrained random colour identifier (e.g. as the input to a
>   downstream hue/sat or mix-RGB node).
> - `random` - **scalar in [0, 1) per cell** mapped through `colors:` /
>   `color_ramp:`, same path the distance outputs use. This is what nearly
>   every "rocks / pebbles / scales / patches" material wants - pick this
>   whenever you provide a muted palette and expect the cells to land inside it.
> - `position` - **cell-local XYZ of the F1 feature point packed as
>   RGB**. Decorrelated from `cell`, useful as a stochastic 3-vector ID
>   for seeding downstream procedurals or driving random-per-island UV
>   transforms. Bypasses `color_ramp:` (vector identity output, not a
>   scalar).

> **Extended channels (`f3`, `f4`, `f3_minus_f1`).** F3 and F4 are the
> 3rd and 4th nearest feature distances inside the 3×3×3 cell window -
> same O(27) cost as F1/F2 since every cell is already scanned. Use them
> for hierarchical cellular shading (multi-scale leather, cell-in-cell
> mosaics, voronoi-on-voronoi). `f3_minus_f1` gives a wider, lower-
> frequency border band than `f2_minus_f1` - softer rims, mortar-style
> gradients. The extended channels always use the hard min - `smoothness`
> is intentionally ignored for them (discrete-topology descriptors aren't
> softened).

> **Note on `f2_minus_f1`.** Mathematically, `F2-F1` is **zero on the cell
> boundary** (perpendicular bisector between two feature points) and grows
> to its maximum at the cell centre. The lerp uses `t = sqrt(F2-F1 / norm)`
> - sqrt compression - so `t = 0` → `colors[0]` is the **edge colour** and `t = 1` → `colors[1]`
> is the **cell-interior colour**. For the classic crackle look (bright
> thin lines on dark background) put the **bright** colour FIRST and the
> **dark** colour SECOND.

> **Smooth Voronoi (`smoothness`).** When `smoothness > 0` the hard `min()`
> over the 3×3×3 neighbouring cells is replaced by Inigo Quilez' log-sum-exp
> soft-min `-log(Σ exp(-k·d_i)) / k` with `k = 20/smoothness`. F1 becomes
> C∞-continuous across cell boundaries; F2 is built from the same
> accumulation with the dominant (closest-cell) weight excluded, so
> `f2_minus_f1` loses its V-shaped ridge - bordi morbidi, no step alias on
> the crease lines. Use it for polished leather, water-smoothed pebbles,
> supple reptile skin, closed-pore marble. `smoothness = 0` (default) is
> bit-identical to the legacy hard min. The `cell` / `random` outputs are
> intentionally unaffected (per-cell lookup is discrete).
> Numerical contract: the accumulator runs in double precision and the
> sum is rebased on the hard nearest so no `exp()` argument ever exceeds
> `0`; with `smoothness → 0` (i.e. `k → ∞`) the result converges to the
> classic hard `Evaluate` to within float32 precision.

**Brick:**
```yaml
color:
  type: "brick"
  brick_width: 0.4
  brick_height: 0.18
  mortar_size: 0.025
  row_offset: 0.5              # 0=stack-bond, 0.5=running-bond
  color_variation: 0.6         # 0=all bricks same colour, 1=full A/B contrast
  noise_scale: 0.15            # weathering noise per-brick (0=off)
  colors:
    - [0.72, 0.32, 0.22]       # brick colour A
    - [0.52, 0.18, 0.12]       # brick colour B
    - [0.86, 0.83, 0.78]       # mortar colour
```
Defaults to running-bond brickwork laid on the XY plane; use `rotation` to
remount the pattern on walls oriented differently.

**Gradient:**
```yaml
color:
  type: "gradient"
  mode: "linear"               # linear | quadratic | easing | spherical | radial
  axis: [1, 0, 0]              # gradient direction (linear/quadratic/easing/radial)
  length: 1.0                  # span (in object-local units) over which the gradient runs
  colors: [[0, 0, 0], [1, 1, 1]]
```
- `linear` - `t = (p · axis) / length`.
- `quadratic` / `easing` - same `t` then squared or smoothstepped.
- `spherical` - distance from origin / `length`.
- `radial` - distance from the `axis` line / `length` (cylindrical falloff).

**Coordinate (debug / coord-space driver):**
```yaml
color:
  type: "coordinate"
  mode: "object"                 # object | uv | generated | world
  scale: 1.0                     # multiplier on the coords before fract() / generated clamp
  bounds_min: [-1, -1, -1]       # only used by mode: "generated" - reference-box lower corner
  bounds_max: [1, 1, 1]          # only used by mode: "generated" - reference-box upper corner
  offset: [0, 0, 0]
  rotation: [0, 0, 0]
```
Returns the shading point's coordinates as RGB. Two principal uses:
(1) **debug overlay** to verify UV unwraps and object/world space alignment
at a glance, and (2) **deterministic XYZ driver** to feed another texture
(via mix material) with a chosen coordinate system instead of the implicit
object-local sample point every procedural uses by default.

- `object` - `fract(rec.LocalPoint · scale)`. Same space every other
  procedural (Noise/Marble/Wood/Voronoi) samples in.
- `uv` - `(u, v, 0)` raw (no fract). Shows the primitive's UV
  parameterisation directly; the seam line of spherical UVs is visible.
- `generated` - `clamp((LocalPoint − bounds_min) / (bounds_max − bounds_min), 0, 1)`.
  Artists declare the canonical AABB of the object (typically the
  rest-pose box) and every downstream node sees a tidy `[0, 1]³`
  parameter regardless of how the surface is transformed or displaced
  at render time. Defaults to the unit cube `[-1, 1]³`, matching the
  object-space AABB of a unit sphere / cube / cylinder. Smooth, no
  fract - corners map exactly to the colour-cube extremes.
- `world` - `fract(rec.Point · scale)`. World-locked grid that does
  NOT follow the object when it moves; ideal for laser-grids,
  world-aligned dust shells and "you-are-here" debug spheres.

The standard `offset` / `rotation` transform applies BEFORE the
`fract` wrap (Object / World) or BEFORE the bounds normalisation
(Generated). `color_ramp:` is intentionally not supported - Coordinate
is a vector identity output, not a scalar mappable to a 1-D ramp.

> **Back-compat for the `Value(in HitRecord rec)` overload.** Adding
> Coordinate required exposing `rec.Point` to textures, so this cycle
> introduces an `ITexture.Value(in HitRecord rec)` overload with a
> default that forwards `(rec.U, rec.V, rec.LocalPoint, rec.ObjectSeed,
> rec.Footprint)`. Every existing texture (Noise, Marble, Wood,
> Voronoi, Brick, Gradient, Checker, Image, SolidColor) inherits the
> default forwarding and therefore behaves bit-identically to the
> pre-cycle code on every input. Only Coordinate overrides the
> overload to read `rec.Point` and `rec.LocalPoint` separately.

**All procedurals support:**
```yaml
offset: [5.0, 0.0, 3.0]                  # Translation (object-local units)
rotation: [0.0, 45.0, 0.0]               # Rotation (degrees)
randomize_offset: true                    # Per-object decorrelation
randomize_rotation: true                  # Per-object orientation
```
`randomize_offset` adds a hash-of-`seed`-driven offset of magnitude **±10 wu**
to the sample point (was ±1000 wu in earlier cycles; the larger value collapsed
radial procedurals like `wood` into parallel stripes by pushing the sample far
from the ring axis - see Sampling space note above). With the new object-local
sampling, two instances of the same material at different positions already
read different texture regions; `randomize_offset` is now an *additional*
decorrelation knob, no longer required for per-entity variation. Keep
`randomize_rotation: true` for shared procedurals so identical material IDs
don't read like clones on a wood plank-style grid.

**Multi-stop color ramp (`color_ramp:`)** - optional override of the
implicit two-colour lerp on `noise`, `marble`, `wood`, `voronoi` and
`gradient`:
```yaml
color:
  type: "marble"
  vein_thickness: 0.20
  color_ramp:
    - { position: 0.00, color: [0.95, 0.93, 0.88], interp: "linear"     }
    - { position: 0.45, color: [0.55, 0.45, 0.32], interp: "smoothstep" }
    - { position: 1.00, color: [0.05, 0.05, 0.07], interp: "linear"     }
```
- `position` ∈ [0, 1] - clamped if outside; stops auto-sort by position.
- `color: [r, g, b]` - linear-space RGB.
- `interp` (per stop, controls the *outgoing* segment toward the next stop):
  - `linear` - straight lerp (default).
  - `smoothstep` - Hermite cubic `3t² − 2t³` (C¹ continuity).
  - `ease` - Perlin smootherstep `6t⁵ − 15t⁴ + 10t³` (C² continuity, the
    quintic curve with zero first and second derivative at both endpoints
    - broad photo-real shoulders).
  - `constant` - hold the colour until the next stop (step function).
- Below the first position the first colour holds; above the last
  position the last colour holds.
- Coincident stops (same `position`) produce a hard break - artist trick
  for sharp transitions.
- The two-colour `colors:` shorthand still works as a 2-stop linear
  ramp; supplying `color_ramp:` overrides it (`colors:` is ignored when
  both are present). Existing scenes that never set `color_ramp:` render
  byte-identical to before.

Unlocks: Statuario / Calacatta marble (vein → mid → base → undertone),
sapwood / heartwood / knot wood, photo-real sunset gradients, toon-shade
bands, heat-map false-colours, custom voronoi-driven palettes.

#### **Image Texture:**
```yaml
color:
  type: "image"
  path: "textures/brick.png"              # Relative to YAML file
  uv_scale: [2, 1]                        # Tiling factor
  color_space: "srgb"                     # Optional: srgb | linear | raw
```
- Supports: PNG, JPEG, BMP, GIF, TIFF, WebP (8 or 16 bits per channel, read at
  full precision), plus Radiance `.hdr` and OpenEXR `.exr` (always linear)
- `color_space` says how the stored values are read. Default by role: a texture
  that gives a **colour** (`color`, `base_color`, `transmission_color`, ...) is
  decoded from sRGB; a texture that gives **data** (roughness, metallic, weights,
  IOR, a mix mask, bump, displacement, heightfield) is read as stored (`raw`).
  Set it only to override the role, e.g. a roughness map saved in sRGB.
  `linear` and `raw` are synonyms (also `data`, `non-color`).
- Bilinear filtering for smoothness
- Wrapping for seamless tiling
- Analytic anti-aliasing (mipmap + anisotropic, up to 8 trilinear taps) when ray differentials
  are available - enabled by default; toggle from the CLI with
  `--texture-filtering <auto|on|off>` (see [rendering-profiles.md §6c](./rendering-profiles.md)).
  The same flag drives the analytic octave clamp of the `noise` texture
  (every family: the octaves finer than the footprint are replaced by their
  average, so distant noise fades to its mean) and the footprint
  supersampling of `voronoi`; `marble` and `wood` are point-sampled and
  rely on the pixel samples.
#### **Normal Map:**
```yaml
normal_map:
  path: "textures/brick-normal.png"
  strength: 1.0                            # Perturbation intensity
  uv_scale: [2, 1]
  flip_y: false                            # Set true for DirectX-style maps
```
- Adds per-pixel surface detail without geometry
- Format: RGB where R=X, G=Y, B=Z (tangent space)
- Neutral color: RGB(128, 128, 255) = no perturbation
- Read as data (never sRGB-decoded), 8 or 16 bits per channel, or EXR;
  texel-centred bilinear lookup, wrapping at the edges
- On OBJ meshes the tangent frame is MikkTSpace's (per-vertex tangents,
  bitangent rebuilt from the interpolated normal), the frame normal-map
  bakers write in: a baked map shows no seams between faces
- Applies to any material type (lambertian, metal, dielectric, emissive, openpbr, mix)
- Free sources: ambientcg.com, polyhaven.com, 3dtextures.me

#### **Bump Map:**
```yaml
bump_map:
  texture:                                 # ANY ITexture: procedural or image
    type: "noise"                          # noise/marble/wood/voronoi/brick/gradient/image/...
    noise_type: "fbm"
    scale: 6
    octaves: 4
    colors: [[0, 0, 0], [1, 1, 1]]
  strength: 0.02                           # Height of a texture value of 1, in scene units (0-10)
  scale: 1.0                               # Uniform UV multiplier (default 1)
```

Like `normal_map` but driven by a **scalar height field** sampled from any
procedural or image texture (Rec.709 luminance). The shading normal is
perturbed via central differences in tangent space (Blinn 1978).

The height is in **scene units**: `strength × luminance(texture)`, the same
surface a `displacement` of that height would build. The slope is measured
per unit of length on the surface, whatever the object's size, scale or
rotation, for image and procedural textures alike: an image bump on a large
floor is gentler than the same image on a small tile, as real relief would be.

| Field      | Type                | Default | Description                                                                 |
|------------|---------------------|---------|-----------------------------------------------------------------------------|
| `texture`  | TextureData         | -       | Inner height field. Any procedural (`noise`, `marble`, `wood`, `voronoi`, `brick`, `gradient`, `checker`) or `image`. |
| `strength` | float ∈ [0, 10]     | `1.0`   | Height, in scene units, of a texture value of 1. A procedural with features about a unit wide reads as fine detail at ~0.05-0.5; an image needs a height in proportion to the size of the surface it covers. |
| `scale`    | float > 0           | `1.0`   | Uniform UV multiplier stacked on top of the inner texture's own `uv_scale` / `scale`. |

**Composition order** when both `normal_map` and `bump_map` are present:

1. `normal_map` runs first, replacing the geometric normal.
2. `bump_map` runs second, perturbing the **already-perturbed** normal
   (TBN re-orthogonalised against it).
3. OpenPBR's `coat_normal_map` is **independent** - coat keeps its own
   surface frame and does not see the bump.

Applies to every material type (lambertian, metal, dielectric, emissive,
openpbr, mix). Works on every primitive that populates the TBN basis -
the engine populates TBN on Sphere, Box, Cylinder, Cone, Quad, Disk,
Annulus, Torus, Capsule, Lathe, Triangle, SmoothTriangle, and
InfinitePlane (i.e. all of them).

The killer advantage over `normal_map` is **procedural input**: infinite
resolution, no asset to ship, full reuse of the existing procedural
texture types (noise/marble/wood/voronoi/brick/gradient).

#### **Surface Displacement (Material-Level)**

True geometric displacement of subdivided polygonal meshes. Unlike
`bump_map` (which perturbs only the shading normal) the displacement
physically moves the vertices, so the **silhouette changes** - outlines
against the sky reflect the deformation. The displacement is part of the
material: one displaced material drives every mesh that uses it, with no
per-entity duplication.

```yaml
materials:
  - id: "stone_displaced"
    type: openpbr
    base_color: [0.82, 0.66, 0.42]
    specular_roughness: 0.78
    displacement:
      mode: "scalar"                  # scalar | vector
      space: "tangent"                # vector mode only: tangent | object
      texture:                        # any ITexture (procedural or image)
        type: "noise"
        noise_type: "fbm"
        scale: 3.5
        octaves: 5
        colors: [[0, 0, 0], [1, 1, 1]]
      scale: 0.30                     # signed world-unit amplitude
      midlevel: 0.5                   # texture value treated as "flat" (0.5 for 8-bit)
      uv_scale: 1.0
      bound: 0.30                     # BVH leaf AABB padding; auto-derived when omitted
      method: "both"     # both | displacement | bump_only
      autobump: true                  # derive residual bump from displacement texture
      autobump_strength: 1.5
      autobump_scale: 1.0
```

The vertex update for scalar mode is `v' = v + scale · (h − midlevel) ·
n_smooth` with `h = Rec.709 luminance(texture.Value(u, v, p))`. Vector
mode reads the full RGB triplet and offsets along the per-vertex TBN
basis (`tangent` space - R→T, G→B, B→N, standard bake convention) or
directly as a local `(x, y, z)` offset (`object`
space). The post-displacement smooth normals are recomputed from the
displaced topology so the BSDF sees the new silhouette's actual normal
field, not the pre-displacement one.

| Field                             | Type        | Default | Notes |
|-----------------------------------|-------------|---------|-------|
| `displacement.mode`               | string      | `"scalar"` | `"scalar"` reads luminance and offsets along the normal; `"vector"` reads the full RGB as a 3D offset. |
| `displacement.space`              | string      | `"tangent"` | Vector mode only. `"tangent"` requires a UV channel; the loader silently falls back to `"object"` when absent. |
| `displacement.texture`            | TextureData | -       | Inner height field. Any procedural (`noise`, `marble`, `wood`, `voronoi`, `brick`, `gradient`, `checker`) or `image`. |
| `displacement.scale`              | float       | `0.1`   | Signed amplitude in world units. Negative pushes inward. `0` disables. |
| `displacement.midlevel`           | float       | `0`     | Reference texture value = "no displacement". `0.5` for 8-bit greys / unsigned-RGB EXRs. |
| `displacement.uv_scale`           | float > 0   | `1.0`   | Uniform UV multiplier stacked on top of the inner texture's own `uv_scale`. |
| `displacement.bound`              | float ≥ 0   | `\|scale\|` (scalar) / `\|scale\|·√3` (vector) | Maximum expected displacement amplitude. Pads BVH leaf AABBs to avoid self-intersection artefacts. |
| `displacement.method`             | string      | `"both"` | `"both"` runs geometric displacement + autobump (if requested); `"displacement"` runs only the geometric pass and forces autobump off; `"bump_only"` skips the geometric pass and turns the texture into a pure bump map. |
| `displacement.autobump`           | bool        | `false` | Derive a residual `bump_map` from the displacement texture and attach it to the mesh - recovers sub-pixel detail finer than the subdivision grid. |
| `displacement.autobump_strength`  | float ≥ 0   | `1.0`   | Bump amplitude multiplier; the final amplitude is `autobump_strength · \|scale\|`. |
| `displacement.autobump_scale`     | float > 0   | `1.0`   | UV-frequency multiplier for the autobump's inner texture. `>1` samples finer than the displacement (macro displacement + micro bump workflow). |

**Mesh-only by design.** Material-level displacement is applied only by
the `type: mesh` entity branch. Non-mesh entities that
reference a displaced material emit a load-time warning and use the
material's surface shading without the geometric displacement.

> **Substitute analytical primitives with mesh proxies for displacement.**
> When you want a displaced sphere/cube/torus, load a polygonal proxy
> mesh and let `subdivision_scheme:` re-tessellate it under adaptive
> screen-space control. Stock proxies ship in `scenes/models/`:
> - `subdivision-icosahedron.obj` - unit sphere (Loop subdivision)
> - `subdivision-cube.obj` - unit cube (Catmull-Clark)
> - higher-poly variants generated via `dotnet run --project src/Tools/...`
>
> Example: an analytical `type: "sphere"` at `(x, y, z)` with radius `r`
> becomes
> ```yaml
> - type: "mesh"
>   path: "../models/subdivision-icosahedron.obj"
>   subdivision_scheme: "loop"
>   subdivision_pixel_error: 6.0          # adaptive: stop at ≤6 px edges
>   subdivision_max_iterations: 5
>   scale: [r, r, r]
>   translate: [x, y, z]
>   material: "displaced_material"
> ```
> Adaptive subdivision keeps the cost proportional to the on-screen size:
> background spheres stay coarse, foreground ones refine automatically.
> Use the fixed-iteration form (`subdivision_iterations: N`) only for
> deterministic CI / regression renders.

**Composition order.** When all three perturbations are present:

```
geometry normal (post-displacement)
  → material.normal_map
    → material.bump_map
      → mesh.autobump                 (← derived from displacement.texture)
```

`coat_normal_map` on an `openpbr` material perturbs only the coat lobe
and is unaffected by this stack.

**Pipeline.** `subdivide → displace → triangulate → BVH`. Displacement
on an un-subdivided low-poly mesh moves the original vertices and is
rarely visually useful; combine it with `subdivision_iterations ≥ 4`
(or an adaptive `subdivision_pixel_error`) to expose enough
micro-vertices for a smooth deformation.

**Per-entity suppression.** Add `displacement_enabled: false` on a mesh
entity to disable the resolved material's displacement for that single
instance (the material is still shared with other meshes that DO get
displaced). Useful for low-detail proxies or LOD copies.

**Mix material displacement.** A
`type: mix` material with `displacement: { blend_with_mask: true }`
vector-blends the per-vertex displacement offsets of its two child
materials by the SAME mask/blend factor the BSDF mix uses. The result
is C0-continuous across material seams; both children must declare
their own displacement (the loader warns and skips the mix-displacement
otherwise). The autobump composes as a `MixBumpMapTexture` on the same
factor.

```yaml
- id: "weathered_rock"
  type: "mix"
  material_a: "rock_clean"               # both children carry their own displacement
  material_b: "rock_moss"
  blend:
    type: "noise"
    scale: 3.0
  displacement:
    blend_with_mask: true                # vector-blend children's displacement
```

The showcase scenes
`scenes/showcases/texture-displacement-scalar.yaml`,
`texture-displacement-vector.yaml`,
`texture-displacement-combo.yaml`, and
`texture-displacement-material-mix.yaml` cover every authoring path.

#### **Per-Entity Seed:**
```yaml
entities:
  - name: "marble_sphere"
    type: "sphere"
    center: [0, 1, 0]
    radius: 1.0
    material: "marble_textured"
    seed: 1234                             # Deterministic texture randomization
```
---
### 7. **ENTITIES SECTION** - 3D Objects

**Common per-entity fields** (apply to every type below: primitive, csg, mesh,
group, instance):

| Field | Default | Notes |
|-------|---------|-------|
| `name` | - | Optional label for logging / debugging |
| `material` | inherited | Material ID, resolved from the `materials` block |
| `seed` | auto | Stable integer that drives procedural texture variation; auto-derived from name+type+index when omitted |
| `visibility` | all `true` | `{ camera, diffuse, glossy, transmission, shadow }`, as on the sky (§3): each `false` hides the entity from rays of that category. `camera: false` hides it from primary camera rays only: it still appears in reflections and refractions, receives and casts indirect light, and (if emissive) still lights the scene via NEE (a fill panel that should not show in the frame). Set on a `group` to propagate to every child. |
| `scale`, `rotate`, `translate` | identity | Optional local transform (applied scale → rotate → translate). `scale` is a number or `[x, y, z]`; `rotate` is Euler degrees |
| `rotate_order` | `xyz` | Order of the Euler rotations: `xyz` (X first, then Y, then Z), `xzy`, `yxz`, `yzx`, `zxy`, `zyx` |
| `matrix` | - | A 4×4 matrix, 16 numbers row by row acting on column vectors (translation in the last column), instead of `scale`/`rotate`/`translate` (an exported transform) |
| `motion` | - | Optional list of keyframes for transform motion blur (see §7.0). Top-level entities only |

#### **7.0 Motion Blur (`motion:`)**

A non-empty `motion:` list turns an entity's static transform into an *animated*
one: the renderer interpolates its pose over the shutter interval (see the camera
`shutter_interval` key) and blurs the swept geometry. The base
`scale`/`rotate`/`translate` is the implicit keyframe at `time: 0`; each `motion`
entry adds a keyframe at its own normalized time in `[0, 1]`. Components omitted
in a keyframe inherit the **base** pose (not the previous keyframe).
```yaml
# A sphere streaking to the right while spinning.
- type: "sphere"
  center: [0, 0, 0]
  radius: 0.6
  material: "chrome"
  translate: [-2, 0.6, 0]                  # pose at time 0
  rotate: [0, 0, 0]
  motion:
    - { time: 0.5, translate: [0, 0.6, 0] }                  # omitted rotate/scale ⇒ base pose
    - { time: 1.0, translate: [2, 0.6, 0], rotate: [0, 90, 0] }
```
- Translation and scale are interpolated linearly; rotation follows the shortest
  quaternion arc, so a key spinning `0° → 350°` sweeps `−10°`, not the long way.
- Times are clamped to `[0, 1]` (warned); duplicate times keep the last entry;
  ray times outside the keyframe range clamp to the end poses.
- Supported on **top-level entities of any type** (primitive, csg, mesh, group,
  instance). `motion:` on a template definition or a group/instance *child* is
  ignored with a warning - animate the enclosing top-level entity instead.
- **Animated emitters:** an emissive animated entity still blurs correctly for
  the camera and reflections, but its direct-light (NEE) contribution is sampled
  at one fixed mid-animation snapshot (a warning is logged). Fast-moving emitters
  therefore have slightly biased direct-light positions.

See `docs/technical/motion-blur.md` for the algorithm, and the
`motion-blur-showcase.yaml` / `motion-blur-billiard-showcase.yaml` scenes.

> **Caustics need no per-entity keys.** Every integrator renders the light that
> glass, water and mirrors focus; the bidirectional ones (`--integrator bdpt`,
> `--integrator vcm`) render it cleanly, VCM also when it is seen through glass
> or water (see `docs/reference/rendering-profiles.md` §4c).

#### **7.1 Sphere**
```yaml
- name: "ball"
  type: "sphere"
  center: [0, 1, 0]
  radius: 1.0
  material: "glass"
```
#### **7.2 Box**
```yaml
- name: "crate"
  type: "box"
  scale: [2.0, 0.5, 2.0]                 # Width, height, depth
  translate: [0.0, 0.25, 0.0]             # Center position
  rotate: [0, 45, 0]                      # Rotation (degrees, XYZ)
  material: "wood"
```
- **⚠️ Box is centered:** Translate moves the center. To put base on ground: `translate: [x, height/2, z]`
#### **7.3 Cylinder**
```yaml
- name: "column"
  type: "cylinder"
  center: [0, 0, 0]                       # Center of BOTTOM
  radius: 0.4
  height: 3.0                              # Extends upward (+Y)
  material: "marble"
```
#### **7.4 Cone (or Frustum)**
```yaml
# Pointed cone
- name: "traffic_cone"
  type: "cone"
  center: [0, 0, 0]
  radius: 1.0                              # Base radius
  height: 2.0
  material: "orange_plastic"
# Truncated cone (frustum)
- name: "bucket"
  type: "cone"
  center: [0, 0, 0]
  radius: 1.5                              # Base
  top_radius: 1.0                          # Top (= frustum)
  height: 2.0
  material: "metal"
```
#### **7.5 Torus (Donut/Ring)**
```yaml
- name: "ring"
  type: "torus"
  center: [0, 1, 0]
  major_radius: 2.0                       # Distance from center to tube center
  minor_radius: 0.5                        # Tube radius
  material: "gold"
```
- Variants: ring torus (R>r), horn torus (R=r), spindle (R<r)
#### **7.6 Capsule (Pill/Sphylinder)**
```yaml
- name: "battery"
  type: "capsule"
  center: [0, 0, 0]
  radius: 0.5
  height: 2.0                              # Cylinder part height
  material: "plastic"
  # Total height = height + 2×radius = 3.0
```
#### **7.7 Annulus (Disk with Hole)**
```yaml
- name: "washer"
  type: "annulus"
  center: [0, 0, 0]
  # outer_radius: 1.0
  inner_radius: 0.5
  material: "steel"
```
#### **7.8 Disk (Flat Circle)**
```yaml
- name: "platform"
  type: "disk"
  center: [0, 0, 0]
  normal: [0, 1, 0]
  radius: 2.0
  material: "metal"
```
#### **7.9 Quad (Rectangular Plane)**
```yaml
- name: "wall"
  type: "quad"
  q: [-5, 0, 5]                           # Origin corner
  u: [10, 0, 0]                            # First edge vector
  v: [0, 5, 0]                             # Second edge vector
  material: "brick"
```
#### **7.10 Infinite Plane**
```yaml
- name: "floor"
  type: "infinite_plane"
  point: [0, 0, 0]                        # Point on plane
  normal: [0, 1, 0]
  material: "wood"
```
#### **7.11 Triangle** (flat, or smooth with per-vertex normals)
```yaml
- name: "poly"
  type: "triangle"
  v0: [0, 0, 0]
  v1: [1, 0, 0]
  v2: [0.5, 1, 0]
  material: "red"
- name: "smooth_poly"
  type: triangle
  v0: [0, 0, 0]
  v1: [1, 0, 0]
  v2: [0.5, 1, 0]
  n0: [0, 0, 1]                            # n0, n1, n2: shading normals (smooth triangle)
  n1: [0.1, 0, 0.9]
  n2: [0.1, 0, 0.9]
  material: "plastic"
```
#### **7.12 Mesh (OBJ, glTF, PLY files)**
```yaml
- name: "model"
  type: "mesh"
  path: "models/teapot.obj"               # .obj, .gltf, .glb or .ply, relative to the YAML
  scale: [2.0, 2.0, 2.0]
  translate: [0, 1, 0]
  material: "ceramic"                     # optional: covers every face
```
- Formats, picked by the extension:
  - **Wavefront OBJ** with its **MTL** library (`mtllib`, `usemtl`): polygons
    of any size (concave ones are ear-clipped, not fanned), negative indices,
    smoothing groups (`s`: a face without normals is shaded with the
    angle-weighted normals of its group, flat with `s off`). MTL statements
    read: `Kd`/`map_Kd`, `Ks`, `Ns` (Phong exponent, turned into a roughness),
    `Pr`/`map_Pr`, `Pm`/`map_Pm`, `Ni`, `d`/`Tr`/`map_d` (opacity; with
    `illum` 4, 6, 7 or 9 it is the glass's transmission), `Tf`, `Ke`/`map_Ke`,
    `bump`/`map_Bump` (`-bm`), `norm`/`map_Kn`, `Pc`, `Pcr`, `Ps`, `aniso`;
    map option `-s`.
  - **glTF 2.0** (`.gltf` with external or embedded buffers, `.glb`): the
    default scene's nodes (matrices or TRS, a mirroring node reverses the
    winding), triangles, strips and fans, normals, `TEXCOORD_0`, normalised
    and sparse accessors. Materials: metallic-roughness with base colour,
    alpha (`OPAQUE`, `MASK` as a cut-out, `BLEND` as stochastic opacity),
    normal and emissive maps, and `KHR_materials_emissive_strength`, `_ior`,
    `_transmission`, `_volume` (without it transmission is thin-walled),
    `_clearcoat`, `_sheen`, `_specular`, `_iridescence`, `_anisotropy`,
    `_unlit`, `KHR_texture_transform`, `EXT_texture_webp`. Not read: points
    and lines, skins and morph targets (rest pose), vertex colours, Draco and
    meshopt compression, KTX2 textures, occlusion maps.
  - **PLY** (ASCII, binary little and big endian): positions, normals
    (`nx ny nz`), texture coordinates (`u v`, `s t`, `texture_u texture_v`),
    faces of any size. No materials.
- **Materials**: `material` on the entity covers every face. Without it the
  file's own materials are used (OBJ/MTL, glTF), translated into `openpbr`,
  and faces without one take the default (or the enclosing group's)
  material. `material_overrides` replaces file materials by name:
  ```yaml
  - type: "mesh"
    path: "models/car.glb"
    material_overrides:
      "Body": "red_paint"                 # file material name: scene material id
  ```
  A model with several materials becomes one mesh per material, each sampled
  as an area light on its own when emissive.
- Auto-builds an internal BVH; smooth shading with the file's normals and UVs
  (a face with UVs and no normals keeps its UVs, shaded flat).
- Supports `translate`, `rotate`, `scale`

##### **Subdivision surfaces (Loop / Catmull-Clark)**

The mesh loader can refine the model before BVH construction using the
standard Loop and Catmull-Clark subdivision algorithms:

```yaml
- name: "smooth_cube"
  type: "mesh"
  path: "models/cube.obj"
  material: "ceramic"
  subdivision_scheme: "catmull_clark"     # loop | catmull_clark | auto | none
  subdivision_iterations: 3               # uniform refinement steps
  subdivision_crease_angle: 45            # edges sharper than 45° become creases
  subdivision_crease_sharpness: 2         # sharp for 2 levels, then smooth
```

| Field                       | Type   | Default | Notes |
|-----------------------------|--------|---------|-------|
| `subdivision_scheme`        | string | `none`  | `loop` (triangle meshes), `catmull_clark` (quad meshes - also accepts triangles and n-gons in the first iteration), `auto` (picks CC for all-quad input, Loop for all-triangle, CC otherwise), `none`. |
| `subdivision_iterations`    | int    | `0`     | Uniform iteration count. Each iteration multiplies face count by ≈ 4. |
| `subdivision_pixel_error`   | float  | `0`     | Adaptive screen-space target. The loader picks the iteration count that brings the longest projected edge below this many pixels (using the scene's resolved camera). Combined with `subdivision_iterations` by `max(static, adaptive)`. |
| `subdivision_max_iterations`| int    | `6`     | Hard ceiling regardless of the adaptive estimate (caps the 4^N face explosion). |
| `subdivision_boundary`      | string | `edge_and_corner` | Open boundaries: `edge_and_corner` keeps a boundary vertex with a single face in place (the corner of a sheet); `edge_only` rounds it along the boundary curve. Boundary edges are always sharp. |
| `subdivision_crease_angle`  | float  | `0`     | Edges whose two faces meet at more than this angle (degrees, between their normals) become creases. `0` = none. |
| `subdivision_crease_sharpness` | float | infinite | Sharpness of those creases: sharp for this many levels, then smooth, a fraction blending the two (semi-sharp creases). Omitted: always sharp. |

- **Loop subdivision** (Charles Loop, 1987) and **Catmull-Clark** (Catmull
  & Clark, 1978), with the vertex rules of DeRose, Kass and Truong 1998: a
  vertex with two sharp edges follows the crease curve (1/8, 6/8, 1/8), three
  or more make a corner (fixed), a crease edge's sharpness drops by one per
  level. Loop takes triangles only (polygons are triangulated first);
  Catmull-Clark takes any polygon in the first iteration, then all quads.
- After the last level the vertices are **moved onto the limit surface**
  with the limit masks, and each gets the **limit surface's normal** from the
  tangent masks (exact at smooth vertices of any valence). On a crease or a
  corner each face corner gets the normal of its own side (sector), so a
  crease stays sharp in the shading. Normals given in the file are not used
  after subdivision.
- UV channels carry through with linear mid-edge masks (face-varying
  linear interpolation). UV seams that share a position but not a UV are
  preserved.

##### **Surface displacement (material-level)**

> Displacement is declared on the material under `materials:`, not on the
> entity. See the **Surface Displacement** subsection of §6 for the full
> schema (scalar/vector mode, autobump, `method`, Mix-blend). Mesh entities
> can suppress an inherited displacement per-instance via
> `displacement_enabled: false`.

```yaml
materials:
  - id: "stone_displaced"
    type: openpbr
    base_color: [0.82, 0.66, 0.42]
    specular_roughness: 0.78
    displacement:
      texture: { type: "noise", noise_type: "fbm", scale: 3.5, octaves: 5 }
      scale: 0.30
      midlevel: 0.5
      bound: 0.30

entities:
  - name: "stone_panel"
    type: "mesh"
    path: "models/plane.obj"
    material: "stone_displaced"
    subdivision_scheme: "catmull_clark"
    subdivision_iterations: 6
    # displacement_enabled: false   # optional per-entity bypass
```

All vector/scalar/autobump fields and `method` are documented
in the material section. The mesh
entity itself accepts only `displacement_enabled: bool` (default `true`)
to suppress an inherited displacement per-instance.

#### **7.13 HeightField (Terrain)**

A continuous surface `y = h(x, z) · height_scale` over the XZ rectangle
defined by `bounds: [xMin, zMin, xMax, zMax]`. The height function comes
either from a baked PNG-16 grayscale heightmap or from a procedural
texture sampled at construction time onto an internal grid. Intersection
is accelerated by a min/max mipmap (Tevs/Ihrke/Seidel 2008) - one
primitive replaces an entire tessellated terrain mesh.

```yaml
# Baked-heightmap variant (the format TerrainGen emits)
- name: "terrain"
  type: "heightfield"
  bounds: [-50, -50, 50, 50]
  height_scale: 25
  heightmap_path: "assets/heightmaps/myterrain-height.png"
  sea_level: 7.5
  sea_material: "water"
  strata:
    - { min_altitude: 0.00, max_altitude: 0.18, material: "sand",  blend_width: 0.04 }
    - { min_altitude: 0.14, max_altitude: 0.55, material: "grass", blend_width: 0.08 }
    - { min_altitude: 0.50, max_altitude: 0.85, min_slope: 25, material: "rock", blend_width: 0.08 }
    - { min_altitude: 0.80, max_altitude: 1.00, material: "snow",  blend_width: 0.04 }
  material: "grass"   # fallback when no band weight wins

# Procedural variant - heightmap synthesised at load time from a noise texture
- name: "procedural_terrain"
  type: "heightfield"
  bounds: [-50, -50, 50, 50]
  height_scale: 25
  resolution: 512
  height_texture:
    type: "noise"
    noise_type: "hetero_terrain"
    scale: 0.012
    octaves: 5
    lacunarity: 2.0
    fractal_offset: 0.65
  material: "rock"
```

| Field             | Type    | Default | Notes |
|-------------------|---------|---------|-------|
| `bounds`          | `[f]`   | -       | `[xMin, zMin, xMax, zMax]`. The Y AABB is derived from `height_scale` and the heightmap's peak sample. |
| `height_scale`    | float   | `1`     | Multiplier applied to the normalised heightmap values (PNG-16 unit = 1). The world peak is `max(heightmap) × height_scale`. |
| `heightmap_path`  | string  | -       | PNG path resolved relative to the master scene. 16-bit grayscale (`L16`) preferred; 8-bit accepted with a precision-loss warning. Mutually exclusive with `height_texture` (path wins). |
| `height_texture`  | object  | -       | Full `TextureData` block - any procedural noise type. The luminance of `Value(u, v, p)` becomes the height. |
| `resolution`      | int     | `512`   | Only used in procedural mode: side length of the pre-sampled grid that backs the min/max pyramid. Visual quality is set by the per-pixel bisection; this controls the acceleration's tightness. |
| `sea_level`       | float?  | none    | World-space Y of an optional water plane clipped to the heightfield footprint. Only rendered where the terrain underneath sits below `sea_level` (no floating water sheets). |
| `sea_material`    | string? | none    | Material ID applied to the water plane. Required when `sea_level` is set. |
| `strata`          | list    | none    | Altitude/slope-driven material bands; see below. |
| `material`        | string  | -       | Fallback material used at shading points where no `strata` band wins. |

##### **Strata bands**

Each `strata` entry defines an altitude and/or slope window mapped to a
material. The engine evaluates `altitude_norm = (hit.Y − sea_level) / (height_scale − sea_level)`
and `slope_deg = acos(normal.Y)`, then scores every band on its
plateau-with-fade weight; the highest-scoring band's material wins the
shading point. Bands may overlap - the overlap region effectively
widens the dominant band's halo.

| Field           | Type   | Default | Notes |
|-----------------|--------|---------|-------|
| `min_altitude`  | float  | `0`     | Normalised lower edge of the altitude plateau (0 = sea level, 1 = peak). |
| `max_altitude`  | float  | `1`     | Normalised upper edge. |
| `min_slope`     | float  | `0`     | Lower edge of the slope plateau (degrees off vertical; 0 = flat ground). |
| `max_slope`     | float  | `90`    | Upper edge. |
| `blend_width`   | float  | `0`     | Soft-fade halo width outside the plateau. v1 selection is winner-takes-all on combined weight; proper inter-band material lerp is a follow-up. |
| `material`      | string | -       | Material ID for this band. |

The strata machinery is what TerrainGen emits to give a single
heightfield the sand → grass → rock → snow band stratification that
the old per-mesh approach produced through separate stratum OBJs. See
`docs/technical/heightfield.md` for the algorithm.

#### **7.14 CSG (Boolean Operations)**
```yaml
# Union (A ∪ B) - fuses two solids into one (e.g. snowman body + head)
- name: "snowman"
  type: "csg"
  operation: "union"
  left:
    type: "sphere"
    center: [0, 0, 0]
    radius: 1.0
  right:
    type: "sphere"
    center: [0, 1.4, 0]
    radius: 0.7
  material: "snow"

# Intersection (A ∩ B) - keeps only the volume shared by both solids (lens shape)
- name: "lens"
  type: "csg"
  operation: "intersection"
  left:
    type: "sphere"
    center: [-0.5, 0, 0]
    radius: 1.0
  right:
    type: "sphere"
    center: [0.5, 0, 0]
    radius: 1.0
  material: "glass"

# Subtraction (A \ B) - removes B from A (bead: sphere with a hole drilled through it)
- name: "bead"
  type: "csg"
  operation: "subtraction"
  left:
    type: "sphere"
    center: [0, 0, 0]
    radius: 1.0
  right:
    type: "cylinder"
    center: [0, -1.5, 0]
    radius: 0.3
    height: 3.0
  material: "wood"
```
- Operations: `union` (A∪B), `intersection` (A∩B), `subtraction` (A\B)
- Child keys are `left` and `right` (Boolean-tree operands of the node)
- Supports recursively nested CSG trees (a `left` or `right` can itself be a `csg` node)
- **Valid CSG child types.** Each child must be a solid primitive with well-defined interior/exterior. Supported: `sphere`, `box`, `cylinder`, `cone`, `torus`, `capsule`, `quad`, `disk`, `annulus`, `triangle`, `lathe`, `extrusion`, or a nested `csg`. **Not supported and skipped with a warning** (loader emits `CSG entity '…': failed to create one or both children. Skipping.` and the node is dropped): `group`, `mesh`, `instance`, `infinite_plane`. To union two primitives as a CSG operand, use an explicit `csg: union` rather than wrapping them in a `group`.
- **Emissive materials inside CSG children** are geometrically valid but CSG nodes are not samplable, so they **will not participate in NEE** (Next Event Estimation). The loader prints a one-time warning: `Warning: CSG object contains an Emissive leaf. CSG objects are not sampleable, so their emitters will NOT participate in Next Event Estimation. The emissive surface will still glow via indirect bounces (high variance). Consider wrapping the emissive primitive outside the CSG if direct lighting is needed.` Workaround: place the emissive primitive alongside the CSG at the scene level rather than inside it.
#### **7.15 Group (Hierarchical Composition)**
```yaml
- name: "lamppost"
  type: "group"
  translate: [5, 0, 0]
  rotate: [0, 45, 0]
  material: "iron"                         # Fallback for children
  children:
    - type: "cylinder"
      center: [0, 0, 0]
      radius: 0.08
      height: 3.0
    - type: "sphere"
      center: [0, 3.2, 0]
      radius: 0.25
      material: "glass"                    # Override
```
- Nesting supported (groups can contain groups)
- Transformations compose hierarchically
- All children inherit material unless overridden
#### **7.16 Template + Instance (Reusable Objects)**
```yaml
templates:
  - name: "chess_pawn"
    material: "wood"
    children:
      - type: "cylinder"
        center: [0, 0, 0]
        radius: 0.4
        height: 0.15
      - type: "sphere"
        center: [0, 0.35, 0]
        radius: 0.3
entities:
  - name: "pawn_e2"
    type: "instance"
    template: "chess_pawn"
    translate: [0, 0, 0]
  - name: "pawn_d2"
    type: "instance"
    template: "chess_pawn"
    translate: [2, 0, 0]
    material: "ebony"                     # Override material
    scale: 1.2                             # Override size
```
#### **7.17 Lathe (Surface of Revolution)**
```yaml
# Linear profile - faceted look of a real turned piece (hard vertex ridges)
- name: "column"
  type: "lathe"
  profile_type: "linear"                  # default - can be omitted
  material: "marble"
  profile:                                # list of [r, y] points, y monotonic
    - [0.30, 0.0]
    - [0.30, 0.1]
    - [0.25, 0.2]
    - [0.28, 2.0]
    - [0.35, 2.1]

# Catmull-Rom profile - smooth, passes through every control point (centripetal)
- name: "vase"
  type: "lathe"
  profile_type: "catmull_rom"
  material: "ceramic"
  profile:
    - [0.00, 0.00]                        # closed bottom (r = 0 → cap absent)
    - [0.30, 0.00]
    - [0.55, 0.40]
    - [0.45, 0.80]
    - [0.55, 0.95]
    - [0.00, 0.95]                        # closed top

# Bezier profile - explicit 4 cubic-Bezier control points per segment
- name: "bowl"
  type: "lathe"
  profile_type: "bezier"
  material: "porcelain"
  profile:                                # segment endpoints - (N-1) segments
    - [0.0, 0.0]
    - [0.5, 0.3]
    - [0.5, 0.6]
  profile_bezier_controls:                # 4 × (N-1) control points, concatenated
    - [0.0, 0.0]
    - [0.3, 0.0]
    - [0.5, 0.1]
    - [0.5, 0.3]
    - [0.5, 0.3]
    - [0.5, 0.45]
    - [0.5, 0.5]
    - [0.5, 0.6]
```
- Revolves a 2D profile 360° around the local Y axis. Positioning goes
  through `center`/`translate`/`rotate` like any other primitive.
- Three interpolation modes. `linear` stacks analytic frustums - fast and
  exact, but shows hard vertex ridges. `catmull_rom` uses centripetal
  Catmull-Rom (Yuksel et al. 2011) - passes through every point, C¹
  continuous, no self-intersections. `bezier` lets you author every cubic
  control point yourself; `profile_bezier_controls` must hold exactly
  `4 × (N − 1)` entries.
- The cap discs at the bottom / top are added automatically when the
  profile leaves the axis (`r > 0`) at that end.
- V coordinate on the lateral surface is the normalised cumulative
  arc length of the profile; U is the azimuthal angle like Cylinder/Cone.
- Catmull-Rom requires at least 4 points; profiles with 2 or 3 points are
  transparently downgraded to `linear` with a loader warning.
- Emissive Lathes participate in NEE automatically: `Sample()` uses the
  area-weighted CDF across segments and caps so shadows and direct
  lighting receive noise-free samples.
- Ray intersection is analytic quadratic for `linear`; for spline modes
  the ray-surface equation is a polynomial of degree 6 solved with a
  Sturm chain + Newton-Raphson hybrid (`SturmSolver`), the standard
  approach for spline surfaces of revolution. Expect ~10× the
  per-ray cost of a Cone hit on spline segments - prefer `linear` when
  faceting is acceptable.

#### **7.18 Extrusion (Linear Extrusion of a 2D Profile)**
```yaml
# Linear concave profile - a 5-pointed star extruded into a prism.
- name: "star_pillar"
  type: "extrusion"
  profile_type: "linear"                  # default - can be omitted
  height: 1.5
  caps: "both"                            # both | start | end | none (default: both)
  material: "gold"
  profile:                                # closed loop of [x, z] points (CCW preferred)
    - [ 1.000,  0.000]
    - [ 0.234,  0.339]
    - [ 0.309,  0.951]
    - [-0.089,  0.405]
    - [-0.809,  0.588]
    - [-0.378,  0.000]
    - [-0.809, -0.588]
    - [-0.089, -0.405]
    - [ 0.309, -0.951]
    - [ 0.234, -0.339]

# Catmull-Rom smooth profile + twist + taper (architectural column)
- name: "twisted_column"
  type: "extrusion"
  profile_type: "catmull_rom"
  height: 4.0
  twist: 90                       # rotate the top profile around Y
  taper: 0.85                             # uniform XZ scale at the top end
  curve_samples: 24                       # polyline samples per profile segment
  caps: "both"
  material: "marble"
  profile:                                # 8-lobed cross-section
    - [ 1.00,  0.00]
    - [ 0.40,  0.40]
    - [ 0.00,  1.00]
    - [-0.40,  0.40]
    - [-1.00,  0.00]
    - [-0.40, -0.40]
    - [ 0.00, -1.00]
    - [ 0.40, -0.40]

# Bezier profile - explicit 4 cubic-Bezier control points per segment, looped
- name: "rounded_badge"
  type: "extrusion"
  profile_type: "bezier"
  height: 0.3
  material: "brass"
  profile:                                # segment endpoints - N segments forming a closed loop
    - [ 1.0,  0.0]
    - [ 0.0,  1.0]
    - [-1.0,  0.0]
    - [ 0.0, -1.0]
  profile_bezier_controls:                # 4 × N control points, concatenated
    - [ 1.0,  0.0]
    - [ 1.0,  0.55]
    - [ 0.55, 1.0]
    - [ 0.0,  1.0]
    - [ 0.0,  1.0]
    - [-0.55, 1.0]
    - [-1.0,  0.55]
    - [-1.0,  0.0]
    - [-1.0,  0.0]
    - [-1.0, -0.55]
    - [-0.55,-1.0]
    - [ 0.0, -1.0]
    - [ 0.0, -1.0]
    - [ 0.55,-1.0]
    - [ 1.0, -0.55]
    - [ 1.0,  0.0]

# Linear + crease_angle - 12-sided polygon smoothed to read as a cylinder, not a faceted prism
- name: "round_column"
  type: "extrusion"
  profile_type: "linear"
  height: 2.0
  crease_angle: 40            # blend normals on edges whose dihedral is below 40°
  caps: "both"
  material: "plaster"
  profile:
    - [ 1.000,  0.000]
    - [ 0.866,  0.500]
    - [ 0.500,  0.866]
    - [ 0.000,  1.000]
    - [-0.500,  0.866]
    - [-0.866,  0.500]
    - [-1.000,  0.000]
    - [-0.866, -0.500]
    - [-0.500, -0.866]
    - [ 0.000, -1.000]
    - [ 0.500, -0.866]
    - [ 0.866, -0.500]
```
- Sweeps a closed 2D profile in the XZ plane along the local +Y axis,
  producing a prism that goes from `y = 0` to `y = height`. Positioning
  goes through `center` / `translate` / `rotate` like any other primitive.
- Three interpolation modes mirror the lathe: `linear` keeps the polyline
  as-is for sharp ridges; `catmull_rom` (centripetal) gives a smooth
  silhouette through every control point; `bezier` lets you author every
  cubic control point yourself. `profile_bezier_controls` must hold
  exactly `4 × N` entries - one cubic per profile segment, in a closed
  loop (the last segment wraps back to the first vertex).
- **Concave profiles work**: caps are triangulated by ear clipping, so
  stars, gears, letters, L-shapes, T/U/H sections and architectural
  profiles render correctly without manual decomposition.
- Profile orientation is auto-corrected: clockwise inputs are reversed at
  load time so wall outward normals always face away from the interior.
- `caps: "both"` (default) closes both ends; `"start"` / `"end"` keep only
  one cap (useful for trough/tray shapes); `"none"` produces an open
  shell.
- `twist` (degrees) rotates the top profile around the Y axis - combined
  with `taper`, you get the wide range of architectural columns and
  industrial fittings with an intricate twisted profile.
- `curve_samples` controls the silhouette quality of `catmull_rom` /
  `bezier` profiles: each input segment becomes that many polyline
  samples (default 16, raise to 24-32 for hero close-ups).
- `crease_angle` (default `0`, `linear` mode only): dihedral threshold in
  degrees for per-vertex normal blending on linear side walls. Adjacent wall
  faces whose normals differ by less than this angle share a blended vertex
  normal (smooth shading, edge disappears in highlights); faces that differ by
  more keep their own flat face normals (hard edge). `0` gives fully faceted
  geometry - the historical default. 30° smooths polyline-approximated curves
  while preserving right-angle corners on letters, gears, and engineered
  sections. Ignored for `catmull_rom` and `bezier`, which always produce
  smooth side walls.
- Internally each extrusion builds its own BVH over the wall + cap
  triangles, so the outer scene BVH sees a single leaf per extrusion
  regardless of profile complexity. Smooth-shaded normals are emitted on
  the side walls for `catmull_rom` / `bezier`; `linear` defaults to flat
  per-face normals - set `crease_angle > 0` to blend normals across edges
  below the threshold and soften polyline-approximated curves without
  switching profile mode.
- Emissive Extrusions participate in NEE automatically: `Sample()` picks
  a triangle proportional to its area, so light from a star-shaped neon
  sign is correctly weighted across walls and caps.

#### **7.19 Transform Order and `center:` Anti-Pattern**

Entity transforms apply in a fixed `scale → rotate → translate` order around the **global origin (0, 0, 0)**:

```
world_pos = translate( rotate( scale( local_pos ) ) )
```

Primitives that expose a `center:` key - **sphere, cylinder, cone, capsule, torus, disk, annulus, lathe** - position their geometry *before* the outer transform is evaluated. Combining `center:` with `rotate:` or `scale:` therefore rotates and scales around the origin, not around the primitive's own center, producing unexpected results.

**Anti-pattern** - do not combine `center:` with `rotate:` or `scale:`:
```yaml
# ❌ WRONG: center moves the cylinder to [0, 0.5, 0], then rotate: [0, 0, 90]
# pivots around the global origin, flinging the cylinder to [-0.5, 0, 0].
- name: "arm"
  type: "cylinder"
  center: [0, 0.5, 0]   # ← do not use with rotate/scale
  rotate: [0, 0, 90]
  radius: 0.05
  height: 1.0
  material: "iron"
```

**Correct pattern** - omit `center:` (defaults to `[0, 0, 0]`) and use `translate:` for final placement:
```yaml
# ✅ CORRECT: primitive sits at origin, rotated around origin, then translated.
- name: "arm"
  type: "cylinder"
  rotate: [0, 0, 90]       # ① rotate around global origin
  translate: [0, 0.5, 0]   # ② move into final position
  radius: 0.05
  height: 1.0
  material: "iron"
```

**When `center:` is safe:**
- When no `rotate:` or `scale:` is present - `center:` is equivalent to `translate:`.
- Inside **CSG children** (`left`/`right`) - CSG children have no outer transform, so `center:` positions them correctly.
- Inside **groups** when the child itself has no rotation - the group's own `translate`/`rotate` composes correctly on top.

---
### 8. **LIGHTS SECTION** - Six Types
#### **8.0 How bright: one photometric quantity per type**
Each light type has one quantity, in physical units, or its `power`:

| Key | Unit | Lights | Meaning |
|---|---|---|---|
| `intensity` | cd | point, spot | Luminous intensity, default 1 (a 60 W bulb is about 65 cd, 800 lm). |
| `luminance` | nit (cd/m²) | area, sphere | Luminance of the emitting surface, default 1. |
| `illuminance` | lux | directional | Illuminance on a surface facing the light, default 1 (direct sun about 100 000 lux). |
| `power` | W or lm (`power_unit: "watt"` default, or `"lumen"`) | point, spot, area, sphere | Emitted flux, instead of the quantity above. Watts are radiant, at 683 lm/W. A spot's power is that of the point light it would be: the cone cuts the light, it does not concentrate it. Resizing an area or sphere light keeps its power. |
| `temperature` | K | all | Black-body colour (Planckian locus), multiplying `color`: 2700 a warm bulb, 4000 neutral, 6500 daylight. It changes the colour, not the luminance. |
| `exposure` | stops | all | Multiplies the light by 2^exposure. |
| `ies` | file | point, spot | An IES LM-63 photometric profile (type C), relative to the scene. The light emits the profile's absolute candela distribution, nadir along the spot's `direction` (a point light's `direction`, default straight down). `power` or `intensity` rescale it to that flux or that peak. TILT data are ignored with a warning. |

The `color` only tints the light: it is divided by its luminance, so
`[1, 0.5, 0.2]` and `[2, 1, 0.4]` give the same light. The scene unit of
radiance is the **nit** (cd/m²): a scene lit with real values needs an
exposure on the camera (`ev100`, §4). A scene without an exposure can still use
small values (a point light of `intensity: 20`, an area light of `luminance:
5`), as format 1 did with its unitless intensities. Candela and power carry
metres: they are converted with the scene's `units: { length }` (§2), so a
scene modelled in centimetres keeps the same light.

```yaml
lights:
  - type: "point"          # a 60 W-equivalent bulb
    position: [0, 2.4, 0]
    power: 800
    power_unit: lumen
    temperature: 2700
  - type: "spot"           # a downlight with a measured profile
    position: [1.5, 2.8, 0]
    direction: [0, -1, 0]
    ies: "assets/ies/downlight.ies"
    power: 900
    power_unit: lumen
  - type: "area"           # an LED panel
    corner: [-0.3, 2.79, -0.3]
    u: [0.6, 0, 0]
    v: [0, 0, 0.6]
    luminance: 3000
    temperature: 4000
```
`scenes/review/light-units.yaml` checks each unit against a hand calculation
(four lights over a grey floor, measured within 0.1%).

**Sampling.** Rectangular area lights and portals are sampled uniformly in the
solid angle they subtend (Ureña et al. 2013) instead of over their area: much less
noise close to the light. The sample of each light carries the probability of
picking that light (`--light-sampling`, below) in its multiple-importance weight;
the environment and portals share one weight group.

#### **8.1 Point Light (Omnidirectional)**
```yaml
- type: "point"
  position: [2, 5, -3]
  color: [1.0, 0.95, 0.85]
  intensity: 19.1                          # Range: 4–30
  soft_radius: 0.0                         # Optional. >0 floors d² at r² → no 1/d² fireflies
```
- Quadratic falloff with distance
- Simple but effective for interior lighting
- `soft_radius` (default `0`): when set, the attenuation denominator is clamped to `max(d², r²)`. Removes the unbounded 1/d² spike that produces persistent fireflies in fog/medium scenes where scattering events can land arbitrarily close to the emitter. Recommended values approximate the physical bulb radius (e.g. `0.05`–`0.20`). At distances `d ≥ r` the lighting is unchanged.
#### **8.2 Directional Light (Sun)**
```yaml
- type: "directional"
  direction: [-0.5, -1.0, -0.3]           # Direction light TRAVELS (light → scene).
                                          # Sun is at -direction.
  color: [1.0, 0.98, 0.92]
  illuminance: 0.8                        # lux; 0.05–2.0 without an exposure
  angular_radius: 0.0                      # Optional. >0 = sun disc (soft shadows).
                                          #   0.27 = real solar disc. Default: 0.
```
- No distance attenuation
- Align with gradient sky `sun.direction` for visual coherence
- Good for outdoor key light
- `angular_radius` (default `0`): when > 0 the light models a disc of finite angular size. Each shadow ray is perturbed uniformly within the subtended cone, producing a soft penumbra. The real Sun is approximately 0.27°. When active, `shadow_samples` defaults to 4 and `IsDelta` becomes `false`, enabling full MIS weighting. Hard-shadow backward compatibility is preserved at the default 0.
#### **8.3 Spot Light (Cone)**
```yaml
- type: "spot"
  position: [0, 5, 0]
  direction: [0, -1, 0]                   # Where spotlight points
  color: [1.0, 0.9, 0.7]
  intensity: 35                           # cd
  inner_angle: 15                         # Degrees (full brightness)
  outer_angle: 30                         # Degrees (fade zone)
  soft_radius: 0.0                        # Optional. >0 = "virtual disc" emitter, no 1/d² fireflies
  shadow_samples: 1                       # Default 1. >1 enables jittered source for soft shadows.
```
- Quadratic falloff
- Smooth falloff between inner/outer cones
- Good for dramatic lighting, accent lights
- `soft_radius` (default `0`): same role as on point lights - clamps the attenuation denominator to `max(d², r²)`. Strongly recommended for spotlights illuminating a participating medium (fog, mist, smoke), where the 1/d² spike at scattering events near the emitter is the dominant firefly source. Typical values: `0.10`–`0.30` for a streetlamp-sized bulb.
- `shadow_samples` (default `1`): when > 1 AND `soft_radius > 0`, each shadow sample jitters the source within a disc of radius `soft_radius` perpendicular to `direction`, modelling the physical bulb extent. Produces soft penumbra in fog. If `soft_radius == 0`, extra samples are redundant (no position jitter) - keep at 1 for efficiency.
#### **8.4 Area Light (Soft Shadows)**
```yaml
- type: "area"
  corner: [-1.5, 4.99, -1.5]              # One corner
  u: [3.0, 0.0, 0.0]                      # First edge
  v: [0.0, 0.0, 3.0]                      # Second edge
  color: [1.0, 0.97, 0.9]
  luminance: 35                            # nits; 15–60 without an exposure
  shadow_samples: 4                        # Samples per point (default)
  soft_radius: 0.0                         # Optional. >0 = floor distSq in cosLight/d²
```
- Monte Carlo soft shadows with penumbra
- `shadow_samples` overridable via CLI: `-S 32`
- Defines a physical rectangle in space
- Great for ceiling panels, windows
- Visible to camera & specular rays via an internally-managed emissive quad proxy at the same `corner`/`u`/`v` - closes Veach's MIS estimator on smooth-specular materials.
- `soft_radius` (default `0`): when > 0, the attenuation denominator is clamped to `max(distSq, r²)`, preventing the `cosLight/d²` term from diverging when a stratified sample falls nearly tangent to the receiver in dense volumetric media. The returned geometric distance is unchanged. Recommended for area lights illuminating a dense participating medium (e.g. a ceiling panel in fog).
- `visibility` (all `true`): the same block as on entities. `visibility: { camera: false }` hides the quad proxy from primary camera rays: NEE keeps illuminating the scene at full intensity, specular reflections / refractions still see the panel, indirect bounces are unaffected.
#### **8.4b Portal Light (window onto the environment)**
```yaml
- type: "portal"
  corner: [3.0, 1.2, -2.5]                # one corner of the window rectangle
  u: [0.0, 0.0, 2.5]                       # edge along U (window width)
  v: [0.0, 1.2, 0.0]                       # edge along V (window height)
  shadow_samples: 8                        # default 8
```
- Bitterli/Wyman/Pharr 2015 portal-masked environment sampling - restricts NEE
  on the sky to the rectangle of the window, dropping the wasted-sample rate
  on interior scenes from ~95% to ~5%. Typical variance reduction ≈10× at the
  same `shadow_samples`.
- The portal is **intangible**: it carries no geometry, is invisible to camera,
  reflections, and BSDF-sampled rays. It contributes only via NEE.
- Requires a non-trivial sky (HDRI / Hosek-Wilkie / Nishita / gradient with sun);
  is skipped at load time when the sky cannot be sampled directly.
- The portal normal `n = normalize(u × v)` defines the "outside" direction.
  Receivers on the wrong side return zero contribution - orient `u, v` so the
  cross product points TOWARDS the sky.
- `shadow_samples` overridable via CLI `-S`. Stratified into a √N × √N grid.

#### **8.5 Sphere Light (Isotropic Soft Shadows)**
```yaml
- type: "sphere"
  position: [0, 5, 0]
  radius: 0.5                              # Larger = softer shadows; also defines proxy size
  color: [1.0, 0.95, 0.85]
  luminance: 30                            # nits
  shadow_samples: 4
```
- Solid-angle sampling (efficient, no wasted samples)
- Isotropic penumbra (circular shadows)
- Visible to camera & specular rays via an internally-managed emissive proxy primitive at the same position/radius - closes Veach's MIS estimator on smooth-specular materials (no "dark hole" highlight on glass/mirror balls).
- `soft_radius` does not apply: the solid-angle estimator `L = luminance × Ω / N` is bounded by `4π × luminance` even when the receiver is inside the sphere, so the 1/d² floor used by point/spot/area lights is unnecessary here.
- `visibility: { camera: false }` hides the spherical proxy from primary camera rays. NEE keeps illuminating the scene at full intensity; the sphere still appears in mirror reflections and through glass. `point`/`spot`/`directional` (delta) lights carry no proxy and take no `visibility`.
#### **Light Calibration Reference:**
For a scene without a camera exposure (values as format 1's unitless intensities):

| Type | Range | Notes |
|------|-------|-------|
| Point (generic) | `intensity` 4–30 | Scales with distance² |
| Spot (key) | `intensity` 15–30 | The cone does not concentrate the light |
| Directional (fill) | `illuminance` 0.05–0.15 | Secondary light with others |
| Directional (main) | `illuminance` 0.3–2.0 | Only light in outdoor scenes |
| Area (panel) | `luminance` 20–60 | Depends on rectangle size |
| Sphere (small) | `luminance` 20–50 | Radius 0.1–0.3 |
| Sphere (large) | `luminance` 15–40 | Radius 0.5–1.5 |

Photometric references (§8.0), with the camera exposed in `ev100`:

| Source | Value | Exposure |
|---|---|---|
| Candle | 1 cd, about 12 lm | EV100 1-3 |
| Household bulb | 800 lm (60 W equivalent), 2700 K | EV100 5-7 for the room |
| LED panel, office | 3000-4000 lm, luminance 3000-10 000 nits, 4000 K | EV100 7-8 |
| Street lamp | 10 000-20 000 lm | EV100 3-5 at night |
| Direct sun | 100 000 lux (`directional`, `illuminance`), 5500-6500 K | EV100 15 |
| Overcast sky | 1000-10 000 lux on the ground | EV100 11-13 |
---
### 9. **IMPORTS** - Reusing Scene Fragments
```yaml
imports:
  - "assets/fonts/font-roboto.yaml"
  - "fragments/studio-3point.yaml"
```
- **Form:** a list of paths (strings)
- **Paths:** Relative to the YAML file directory
- **Cyclic protection:** Automatic cycle detection
- **Merge:** All imported materials/templates/lights are available to the main scene; local ids override imported ones
- **Typical use:** Pull in generated font templates (`assets/fonts/font-<x>.yaml` from `FontGen`) or your own reusable scene fragments. For ready-made materials, lights, mediums, terrains and skies, copy the recipes from the preset catalogues in `scenes/presets/*.md` directly into your scene instead.
---
### 10. **FILE STRUCTURE EXAMPLE**
Here's a complete minimal scene:
```yaml
# Simple Scene
format_version: 2
world:
  sky:
    type: "flat"
    color: [0.3, 0.6, 1.0]
  ground:
    type: "infinite_plane"
    material: "grass"
    point: [0, 0, 0]
cameras:
  - name: "main"
    position: [3, 2, -6]
    look_at: [0, 1, 0]
    fov: 45
    aperture: 0.05
    focus_distance: 7
lights:
  - type: "directional"
    direction: [-0.5, -1.0, -0.3]
    color: [1.0, 0.95, 0.85]
    illuminance: 0.95
  - type: "point"
    position: [5, 8, -3]
    color: [1.0, 1.0, 1.0]
    intensity: 50.0
materials:
  - id: "grass"
    type: "lambertian"
    color: [0.3, 0.6, 0.2]
  - id: "glass"
    type: "dielectric"
    ior: 1.5
  - id: "gold"
    type: "metal"
    color: [0.85, 0.65, 0.2]
    roughness: 0.1
entities:
  - name: "sphere_glass"
    type: "sphere"
    center: [0, 1.5, 0]
    radius: 1.0
    material: "glass"
  - name: "cube_gold"
    type: "box"
    scale: [1.0, 1.0, 1.0]
    translate: [-2, 0.5, 0]
    material: "gold"
```
---
### 11. **KEY FILES IN PROJECT**
**Documentation:**
- `/docs/tutorial/en/` - Complete tutorial (12 chapters):
  - `01-what-is-ray-tracing.md` - Introduction to ray tracing
  - `02-first-scene.md` - First scene and file structure
  - `03-materials.md` - All material types
  - `04-geometric-primitives.md` - All geometry types
  - `05-transforms-and-groups.md` - Transforms, groups and hierarchies
  - `06-lighting.md` - All light types
  - `07-sky-environment-camera.md` - Sky, environment and camera
  - `08-csg.md` - CSG boolean operations
  - `09-volumetrics.md` - Participating media and volumetrics
  - `10-libraries-and-projects.md` - Presets, imports and modularity
  - `11-lathe-surface-of-revolution.md` - Lathe / surface of revolution
  - `12-extrusion-2d-profiles.md` - Linear extrusion of 2D profiles
**Source Code (Scene Parsing):**
- `/src/RayTracer/Scene/SceneData.cs` - the format: every key, its type and its default
- `/src/RayTracer/Scene/SceneValidator.cs` - the strict validation (errors and warnings with line and column)
- `/src/RayTracer/Scene/SceneLoader*.cs` - scene construction
- `/src/RayTracer/Materials/` - Material implementations
- `/src/RayTracer/Geometry/` - All primitive implementations
- `/src/RayTracer/Lights/` - Light source implementations
**Example Scenes:**
- `/scenes/sample.yaml` - Simple reference scene
- `/scenes/cornell-box.yaml` - Classic Cornell Box with variants
- `/scenes/pendolo-newton.yaml` - Complex scene (Newton's pendulum)
- `/scenes/showcases/` - Feature-specific demonstrations
- `/scenes/presets/` - Copy-paste preset catalogues (materials, lights, mediums, terrains, sky, world)
- `/scenes/assets/` - Binary assets: `textures/`, `fonts/`, `heightmaps/`
---
### 12. **BEST PRACTICES FOR HIGH-QUALITY SCENES**
1. **Material Strategy:**
   - Use `lambertian` for large background surfaces (no extra samples needed)
   - Use `openpbr` or `metal` only for protagonist objects
   - Use `mix` material for realistic weathering and wear effects
2. **Lighting Setup:**
   - Start with a directional light + gradient sky for outdoor scenes
   - Add a few point or area lights for fill/accent
   - Use sphere lights for soft, isotropic shadows
   - Override `--shadow-samples` from CLI rather than editing YAML
3. **Camera & Composition:**
   - Add several entries to `cameras:` for multi-shot capabilities
   - Set `focus_distance` (or `focus_point`) on the main subject
   - Use aperture=0.0 for draft passes, add aperture for final renders
   - Test with low resolution + Preview profile first (`-w 400 -H 267 -s 64 -d 4 -S 1`)
4. **Performance Optimization:**
   - Use templates + instances for repeated objects
   - Copy shared materials/lights from the preset catalogues in `scenes/presets/`
   - Batch similar geometries into groups for cleaner hierarchies
   - BVH builds automatically for complex scenes
5. **Texture Sourcing:**
   - Polyhaven.com - Free HDRIs and PBR textures (CC0)
   - AmbientCG.com - Complete PBR texture sets
   - Procedurals (noise, marble, wood) for artistic control
6. **Render Parameters:** (see [Rendering Profiles](./rendering-profiles.md) for full tables and tips)
   - Preview: `-s 64 -d 4 -S 1 -w 400`
   - Standard: `-s 256 -d 6 -w 800`
   - Final: `-s 1024 -d 8 -S 4 -w 1920`
   - Ultra: `-s 1600 -d 8 -S 4 -w 3840`
---
### 13. **SCENEMIGRATE** - From format 1 to format 2
Scenes written before format 2 have no `format_version` and are refused by the
loader. `SceneMigrate` converts them in place:
```bash
dotnet run --project src/Tools/SceneMigrate -- scenes/my-scene.yaml          # one file
dotnet run --project src/Tools/SceneMigrate -- my-scenes/ --dry-run         # a directory, nothing written
```
A directory is searched for `*.yaml` recursively; files that already have a
`format_version` are left alone. The conversion edits the text, so comments and
layout stay; keys that format 1 read but ignored are commented out. Every
result is checked with the engine's validator, and every value that could not
be carried over exactly is printed as a **note** (for example a Disney
parameter driven by a texture where the mapping needs a constant).

What it changes, so the scene renders as it did:
- adds `format_version: 2` and the JSON Schema comment;
- renames keys (`vup` → `up`, `focal_dist` → `focus_distance`, `focal_pos` →
  `focus_point`, `shutter` → `shutter_interval`, `refraction_index` → `ior`,
  `fuzz` → `roughness`, `visible_to_camera` → `visibility`, `twist_degrees` →
  `twist`, `min_slope_deg` → `min_slope`, `displacement_method` → `method`, a
  single `camera:` → a `cameras:` list, ...) and spells out every alias of a
  `type` or enum value;
- writes `disney` materials as the `openpbr` materials they built (§5.5) and a
  material `texture:` as the texture block of its colour input;
- turns a light's unitless `intensity` and `color` into the type's quantity
  and a tint (`intensity` × the colour's luminance; the area of an area light
  for `normalize`), and `candela` into `intensity`;
- turns the sky's `intensity` into `exposure`, its `rotation`/`orientation`
  into `rotate`, a format-1 relative physical sky into the photometric one
  with an exposure, and the gradient sun's `intensity`/`size` into `luminance`
  and `angular_radius`;
- moves `meters_per_unit`/`world_scale` into `units: { length }`;
- writes out what format 1 added by default and format 2 does not: the blue
  sky of a scene without one, the two lights of a scene without `lights:`,
  the grey 0.5 of a material without a colour.

The repository's scenes, test scenes and documentation examples were migrated
with it, and every scene was checked by rendering it with the format-1 engine
and with format 2: identical or within float rounding. The few exceptions differ in
noise, not in the light they carry (an emitter hidden from the camera now joins
the light sampling, disabled entities commented out shift the automatic texture
seed of the entities after them), or by the approximations the notes report
(below 0.1%). An HDRI rotated with format 1's `rotation` turned twice as far as
written (once in its lookup, once in the sky orientation): the migrated
`rotate` keeps the image format 1 rendered.

---

This comprehensive guide covers everything needed to write production-quality YAML scene files. All information is sourced directly from the project documentation, example files, and source code structure.