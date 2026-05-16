# Scene Reference Guide

This document is a comprehensive technical reference for creating and configuring 3D-Ray scene files using the YAML format. It provides a complete guide to project structure, documentation, and best practices for writing high-quality scenes.

---

### 1. **PROJECT OVERVIEW**
**3D-Ray** is a high-performance ray-tracing engine built in C# and .NET 10. It uses YAML files to describe complete 3D scenes with:
- Physically-based rendering (PBR) with Disney Principled BSDF
- Advanced path tracing with Next Event Estimation (NEE)
- Multiple light types (point, directional, spot, area, sphere)
- Procedural and image-based textures
- Normal mapping
- CSG boolean operations
- Hierarchical scene graphs (groups and templates)
---
### 2. **YAML SCENE FILE STRUCTURE**
Every scene YAML file has **5 main sections** (recommended order):
```yaml
imports:    # (optional) External YAML files to load
templates:  # (optional) Reusable object blueprints
world:      # Environment (sky, ground, global medium)
cameras:    # Camera list (or camera: for legacy single-camera)
lights:     # Explicit light sources
materials:  # Material definitions
entities:   # 3D objects (primitives, groups, instances, CSG, meshes)
```
**Key Coordinate System:**
- **X** = right
- **Y** = up
- **Z** = toward camera (negative = away)
- **Colors** = `[R, G, B]` with values 0.0–1.0
---
### 3. **WORLD SECTION** — Environment Configuration

```yaml
world:
  sky:                                     # (optional) Global environment emitter
    type: "flat"  # or "gradient" / "hdri"
    # ... see details below
  ground:                                  # (optional) Auto-generated floor
    type: "infinite_plane"
    material: "floor_name"
    y: 0.0
  medium:                                  # (optional) Global participating medium
    type: "homogeneous"
    # ... see details below
```

When `world.sky` is omitted, a flat daylight-blue sky `[0.5, 0.7, 1.0]` is used.

#### **Flat Sky** (uniform colour, default):
```yaml
sky:
  type: "flat"
  color: [0.5, 0.7, 1.0]                  # Uniform radiance over the full sphere
```
A flat sky participates in NEE (uniform sphere sampling, pdf = 1/(4π)) whenever
its luminance is above zero, matching the behaviour of Cycles/Arnold uniform
world backgrounds. Set `color: [0, 0, 0]` for fully black void scenes (Cornell-box
style) — the loader will skip the sky from NEE in that case.

#### **Gradient Sky** (recommended for outdoor scenes):
```yaml
sky:
  type: "gradient"
  zenith_color:  [0.10, 0.30, 0.80]      # Top of sky
  horizon_color: [0.65, 0.80, 1.00]      # Horizon
  ground_color:  [0.30, 0.25, 0.20]      # Reflection of ground
  sun:                                     # (optional)
    direction:  [-0.5, -1.0, -0.3]       # Direction sunlight TRAVELS (sun → scene).
                                          # Sun position is at -direction; here
                                          # the sun is high on the right-front.
    color:      [1.0, 0.98, 0.85]
    intensity:  12.0
    size:       2.5                        # Angular size in degrees
    falloff:    48.0                       # Glow exponent (higher = sharper)
```
The gradient body is sampled via BSDF importance sampling on the miss path; only
the optional sun disk participates in NEE (cone-sampled inside its angular radius).

#### **HDRI/IBL** (for maximum realism):
```yaml
sky:
  type: "hdri"
  path: "hdri/studio.hdr"                 # Path relative to YAML file
  intensity: 1.0                           # Exposure multiplier
  rotation: 90                             # Y-axis rotation in degrees
```
HDRIs are importance-sampled via a luminance CDF over the equirectangular map.

**Preset Sky Configurations:**
- **Noon** (clean gradient, bright sun)
- **Golden Hour** (low warm sun, saturated horizon)
- **Sunset** (dramatic orange horizon)
- **Night** (very dim zenith/horizon, faint sun disk)
- **Overcast** (uniform horizon, no sun disk; or `flat` with a low gray)
- **Studio** (`flat` with a dim neutral colour to fill bounce light)

#### **Volumetrics (Participating Media)**:

3D-Ray supports **four global medium types** (`homogeneous`, `height_fog`, `procedural`, `grid`) and **five phase functions** (`isotropic`, `hg`, `rayleigh`, `double_hg`, `schlick`). The `medium:` field lives at the `world` level.

**Fields common to all types:**

| Field | Type | Description |
|---|---|---|
| `type` | string | `homogeneous` \| `height_fog` \| `procedural` \| `grid` |
| `sigma_a` | RGB | Absorption coefficient (light dimming) |
| `sigma_s` | RGB | Scattering coefficient (visual fog density, god-rays) |
| `phase` | string | Phase function (default `isotropic`); if `g` is present → `hg` |

**Type 1 — `homogeneous`** (constant density, analytic, cheap):
```yaml
medium:
  type: "homogeneous"
  sigma_a: [0.005, 0.005, 0.005]
  sigma_s: [0.06, 0.06, 0.07]
  phase: "hg"
  g: 0.85
```

**Type 2 — `height_fog`** (exponential density in altitude, analytic):
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

**Type 3 — `procedural`** (Perlin fBm, delta tracking):
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

**Type 4 — `grid`** (3D grid inline or from `.vol` file, delta tracking + reconstruction filter):
```yaml
# Variant A — inline data (useful for small grids, e.g. ≤ 8³)
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

# Variant B — external binary file (recommended for large grids)
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
| `tricubic` | 64 | C¹ | Catmull-Rom cardinal spline (τ = 0.5). ~8× per-sample cost, but removes kinks on low-res grids and smooths binary data. Result is clamped to `[0,1]` to preserve the delta-tracking majorant invariant. Accepted aliases: `cubic`, `catmull-rom`, `smooth`. |

On high-resolution grids (128³+) with smoothly varying density the two filters are visually indistinguishable — `trilinear` is enough. On small inline grids or binary 0/1 data, `tricubic` is the standard way to hide artifacts (analogous to Arnold/Houdini "cubic" filter on VDB).

**Available phase functions:**

| `phase` value | Parameters | Typical use |
|---|---|---|
| `isotropic` | — | Uniform scattering in all directions (dense smoke, thick clouds) |
| `hg` | `g` ∈ (-1, 1) | Henyey-Greenstein: `g > 0` forward, `g < 0` backward, `g = 0` ≈ isotropic |
| `rayleigh` | — | Atmospheric scattering `(3/16π)(1+cos²θ)`; sky, aerial perspective |
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
| `procedural` | Perlin fBm (delta tracking) | Noisier (+30–100% time) | Patchy / irregular fog, horror, uneven god-rays, misty forests, water surfaces with patchy haze. |
| `grid` | Density baked on a 3D grid (inline or `.vol`) | Delta tracking + voxel filter | Localized clouds, sim-cached smoke, explosions, hero VFX assets. The medium exists only inside its AABB — outside is vacuum, so other parts of the scene are unaffected. |

> ⚠️ **Sky + sun + `homogeneous` = black render.** A homogeneous global medium has *constant* density extending to infinity, so the Beer–Lambert shadow ray toward the sun (or any environment direction) travels through `exp(-σ_t · ∞) ≈ 0` and direct environmental lighting collapses to zero. Spot/point/area/sphere lights have finite distance and behave correctly, but if the *only* emitters are `sky` + `sun` (or HDRI) the render comes out black. Use `height_fog` instead — its optical depth toward the zenith is bounded by the scale height, which is exactly the "aerial perspective" model used by Arnold, V-Ray and Unreal. This is the physically correct behaviour of `homogeneous` (real atmospheres are not infinite), not a renderer bug.

- **Usage:** Simulates fog, smoke, atmospheric haze, clouds, underwater effects.
- **Rendering tip:** `homogeneous` and `height_fog` are analytic and cheap. `procedural` and `grid` use delta tracking and are noisier — raise `-s` to 400/576/1024 and keep `-d 6-8`. For dense-fog scenes consider `-C 25`. See [Rendering Profiles](./rendering-profiles.md) §8 for the full guide.
- **Effects:** Spot lights → visible god-rays; point lights → halos; directional → aerial perspective (with `height_fog`).
- **Fireflies with point/spot in fog:** the 1/d² attenuation diverges when scattering events land near a point/spot emitter, producing isolated bright pixels. Set `soft_radius` on those lights (see §8.1, §8.3) to a value approximating the physical bulb size (e.g. `0.15`–`0.30`).
- **Fireflies with area lights in fog:** the `cosLight/d²` term in the area estimator can diverge at grazing angles in dense media. Set `soft_radius` on area lights (see §8.4). Sphere lights use a solid-angle estimator that is bounded by construction — no `soft_radius` needed. Also consider `--indirect-clamp-factor 0.25` (CLI) to aggressively suppress deep-bounce spikes.
- **Advanced firefly control:** `--indirect-clamp-factor <f>` (default `1.0` = off) multiplies the primary `--clamp` threshold for all indirect bounces. E.g. `--clamp 100 --indirect-clamp-factor 0.25` uses clamp=25 on bounce depth ≥ 1 — same as Cycles/Arnold "indirect clamp".
- **Light importance sampling:** `--light-sampling power` (default `all`) samples one light per NEE event with probability ∝ `ApproximatePower`. Dramatically reduces variance in scenes with many lights of mixed brightness (e.g. 1 area + 10 dim point lights). Use `uniform` as a reference baseline.
---
### 4. **CAMERA SECTION**
#### **Multi-Camera** (recommended):
```yaml
cameras:
  - name: "main"
    position: [0, 5, -8]
    look_at: [0, 0, 0]
    fov: 45
    aperture: 0.1
    focal_dist: 12
  - name: "top"
    position: [0, 12, 0.01]
    look_at: [0, 0, 0]
    fov: 35
    aperture: 0.0
    focal_dist: 12
  - name: "subject"
    position: [0, 2, -8]
    look_at: [0, 0, 0]
    fov: 45
    aperture: 0.1
    focal_pos: [0.5, 0.6, 1.0]            # focus on this point — see below
```
#### **Single Camera** (legacy):
```yaml
camera:
  position: [0, 2, -8]                    # Camera location
  look_at: [0, 0, 0]                      # Target point
  vup: [0, 1, 0]                          # "Up" vector (for roll)
  fov: 60                                  # Vertical field of view (degrees)
  aperture: 0.1                            # Lens diameter (0 = pinhole)
  focal_dist: 8.0                          # Distance to focus plane (scalar)
  # focal_pos: [0.5, 0.6, 1.0]            # Alternative: focus on a 3D point
```
**Usage from CLI:**
```bash
dotnet run ... -- -i scene.yaml --list-cameras      # List available
dotnet run ... -- -i scene.yaml -c top -o top.png   # By name
dotnet run ... -- -i scene.yaml -c 1 -o cam1.png    # By index (0-based)
```
**⚠️ Depth of Field:** When `aperture > 0`, set `focal_dist` (or `focal_pos`) to the actual distance / world-space point of your main subject. Default `focal_dist: 1.0` will create unintended extreme blur.

#### **`focal_pos` — focus on a point (Arnold/Cycles "Focus Object")**
`focal_pos: [x, y, z]` is an alternative to the scalar `focal_dist`. The loader computes the focus distance as the **projection** of the camera→focal-point vector onto the optical axis:
```
forward    = normalize(look_at − position)
focusDist  = dot(focal_pos − position, forward)
```
The focus plane is perpendicular to the view direction passing through `focal_pos`, so the value is a **projection, not a Euclidean distance**. A focal point off-axis at `(3, 4, -5)` with the camera at the origin looking along `−Z` yields focus distance `5`, not `√50 ≈ 7.07`. This matches Arnold ("Focus Object"), Cycles ("Focal Object/Distance") and RenderMan.

When both `focal_pos` and `focal_dist` are present, `focal_pos` wins (an info message is logged). `focal_pos` is ignored with a warning when it falls behind the camera, coincides with it, or the camera is degenerate (`look_at == position`); the scalar `focal_dist` is used as fallback.
---
### 5. **MATERIALS SECTION** — Six Types
#### **5.1 Lambertian (Diffuse/Matte)**
```yaml
- id: "matte_red"
  type: "lambertian"
  color: [0.8, 0.2, 0.1]
```
- Pure diffuse reflection, no specular highlights
- Most efficient for large surfaces (walls, floors)
- Supports texture and normal_map
#### **5.2 Metal (Specular/Mirror)**
```yaml
- id: "brushed_steel"
  type: "metal"
  color: [0.85, 0.85, 0.88]               # Reflectance tint
  fuzz: 0.1                                # Roughness: 0=mirror, 1=diffuse
```
- Shiny, reflective surfaces
- Physically-based metallic colors (not albedo)
- Supports texture and normal_map
#### **5.3 Dielectric (Glass/Transparent)**
```yaml
- id: "glass"
  type: "dielectric"
  refraction_index: 1.52                  # IOR (crown glass)
  color: [1.0, 1.0, 1.0]                  # (optional) Tint
```
- Transparent with refraction and Fresnel reflection
- Common IORs: water=1.33, glass=1.5-1.52, diamond=2.42
#### **5.4 Emissive (Self-Luminous)**
```yaml
- id: "neon_blue"
  type: "emissive"
  color: [0.2, 0.4, 1.0]
  intensity: 8.0                           # Radiance multiplier
  texture: (optional)                      # Supports procedural texture
```
- Objects glow and emit light into the scene
- Intensity ranges: 0.5–2 (subtle glow), 3–10 (visible neon), 10–25 (bright panel), 25–100 (overexposed)
- Participate in NEE (Next Event Estimation) for reduced noise
- Emits only from front faces
#### **5.5 Disney Principled BSDF (PBR Unified)**
```yaml
- id: "car_paint"
  type: "disney"  # Aliases: "pbr", "disney_bsdf"
  color: [0.8, 0.2, 0.1]

  # ── Core Disney 2012 parameters ─────────────────────────────────────
  metallic: 0.0                            # 0=dielectric, 1=metal
  roughness: 0.3                           # 0=mirror, 1=diffuse
  subsurface: 0.0                          # Subsurface approximation (skin, wax)
  specular: 0.5                            # Dielectric specular intensity (F₀ × 0.08)
  specular_tint: 0.0                       # Tint dielectric specular toward base_color
  sheen: 0.0                               # Grazing luster (fabric, fuzz)
  sheen_tint: 0.5                          # Tint sheen toward base_color
  clearcoat: 1.0                           # Second specular lobe energy
  clearcoat_gloss: 0.9                     # Legacy clearcoat roughness slider
  spec_trans: 0.0                          # 0=opaque, 1=refractive (glass)
  ior: 1.5                                 # Refraction index for spec_trans and base Fresnel

  # ── Anisotropy (Burley 2012 §5.4) ───────────────────────────────────
  anisotropic: 0.0                         # 0=isotropic, 1=fully stretched along tangent
  anisotropic_rotation: 0.0                # 0..1 fraction of 2π around the normal

  # ── Disney 2015 extensions ──────────────────────────────────────────
  diff_trans: 0.0                          # Lambertian diffuse transmission (leaves, fabric)
  flatness: 0.0                            # Blend Lambert -> HK-flat shape
  thin_walled: false                       # Skip refraction: foliage, paper, thin fabric
  subsurface_color: [0.9, 0.6, 0.5]        # Optional tint for the subsurface/flatness/diff_trans lobes

  # ── Beer-Lambert absorption for coloured glass ──────────────────────
  transmission_color: [0.2, 0.8, 0.9]      # Colour of the glass interior at transmission_depth
  transmission_depth: 0.0                  # Distance (scene units) at which the colour is reached

  # ── Arnold-style coat (optional overrides) ──────────────────────────
  coat_ior: 1.5                            # Explicit coat IOR (default 1.5 = lacquer)
  coat_roughness: -1.0                     # ≥ 0 enables Arnold coat; <0 falls back to clearcoat_gloss
  coat_normal_map: "textures/coat.png"     # Dedicated normal map for the clearcoat lobe
  sheen_roughness: 0.3                     # Charlie sheen α (0.04..1)

  # ── Thin-film iridescence (Belcour-Barla 2017) ──────────────────────
  thin_film_thickness: 0.0                 # Film thickness in nanometres (0 disables)
  thin_film_ior: 1.5                       # Film IOR (η₂)

  # ── Texturing ───────────────────────────────────────────────────────
  texture: (optional)                      # Base colour texture
  normal_map: (optional)
  # Any scalar parameter above accepts a *_texture variant, e.g.
  #   roughness_texture: { type: "image", path: "rough.png" }
  # Same for colour parameters (transmission_color_texture, subsurface_color_texture).
```

##### **Disney Properties Summary**
A single-glance reference of every Disney key the loader accepts.
`Status` flags keys that behave differently from the rest: `Legacy` keys
are still honoured but should be replaced in new scenes; `Unused` keys are
parsed for forward-compatibility only and have no effect on the current
renderer (the loader emits an `Info` line when it sees one).

| Property | Type | Default | Range | Status | Notes |
|---|---|---|---|---|---|
| `color` | colour | required | 0–1 | Core | Base albedo (texturable) |
| `metallic` | float | 0.0 | 0–1 | Core | 0 = dielectric, 1 = conductor |
| `roughness` | float | 0.5 | 0–1 | Core | 0 = mirror, 1 = diffuse |
| `specular` | float | 0.5 | 0–1 | Core | Dielectric F₀ scale (F₀ ≈ 0.08 × value) |
| `specular_tint` | float | 0.0 | 0–1 | Core | Tint dielectric Fresnel by base colour |
| `sheen` | float | 0.0 | 0–1 | Core | Grazing-angle luster (fabric, velvet) |
| `sheen_tint` | float | 0.5 | 0–1 | Core | Tint sheen by base colour |
| `sheen_roughness` | float | 0.3 | 0.04–1 | Ext. | Charlie NDF α (Estevez-Kulla 2017) |
| `clearcoat` | float | 0.0 | 0–1 | Core | Independent second specular lobe |
| `clearcoat_gloss` | float | 1.0 | 0–1 | **Legacy** | Disney-2012 slider; superseded by `coat_roughness` |
| `coat_ior` | float | 1.5 | ≥ 1 | Coat | Arnold-style coat IOR |
| `coat_roughness` | float | -1.0 | -1 or 0–1 | Coat | -1 = use `clearcoat_gloss`; any ≥ 0 selects Arnold path |
| `coat_normal_map` | path | — | — | Coat | Dedicated normal map for the coat lobe |
| `spec_trans` | float | 0.0 | 0–1 | Core | 0 = opaque, 1 = glass |
| `ior` | float | 1.5 | ≥ 1 | Core | Refraction index for specular + transmission |
| `transmission_color` | colour | `[1,1,1]` | 0–1 | Core | Interior colour at `transmission_depth` |
| `transmission_depth` | float | 0.0 | ≥ 0 | Core | Beer-Lambert depth (0 = thin, tint applied once) |
| `anisotropic` | float | 0.0 | 0–1 | Aniso | 0 = isotropic, 1 = fully stretched along tangent |
| `anisotropic_rotation` | float | 0.0 | 0–1 | Aniso | Fraction of 2π around the normal |
| `subsurface` | float | 0.0 | 0–1 | 2015 | Blend Lambert ↔ HK-flat lobe |
| `subsurface_color` | colour | — | 0–1 | 2015 | Tint used by subsurface / flatness / diff_trans |
| `subsurface_radius` | `[R,G,B]` | — | ≥ 0 | **Unused** | Parsed but not read — reserved for future random-walk SSS |
| `diff_trans` | float | 0.0 | 0–1 | 2015 | Diffuse transmission (foliage, thin fabric) |
| `flatness` | float | 0.0 | 0–1 | 2015 | Blend Lambert → HK-flat independently of `subsurface` |
| `thin_walled` | bool | false | — | 2015 | Skip interior refraction (foliage, paper) |
| `thin_film_thickness` | float | 0.0 | ≥ 0 (nm) | Thin-film | Belcour-Barla 2017; 100–800 nm = iridescence |
| `thin_film_ior` | float | 1.5 | ≥ 1 | Thin-film | Film η₂ (water = 1.33, soap = 1.40) |
| `texture` | block | — | — | Texturing | Procedural or image, replaces `color` |
| `normal_map` | block | — | — | Texturing | Surface perturbation (image-only) |
| `bump_map` | block | — | — | Texturing | Scalar bump from any procedural/image texture |

> Every scalar parameter accepts a matching `*_texture` variant (e.g.
> `roughness_texture`) and the three colour inputs (`color`,
> `transmission_color`, `subsurface_color`) accept a matching
> `*_texture` block.

##### **Clearcoat: legacy vs Arnold-style**

The coat lobe has two compatible parameterisations:

- **Disney 2012 (legacy).** Single slider `clearcoat_gloss` (1 = mirror,
  0 = rough) with an implicit IOR of 1.5. Kept working for every scene
  authored before the Arnold extension landed.
- **Arnold Standard Surface (preferred).** Tunable `coat_ior` +
  `coat_roughness` (0 = mirror, 1 = rough). Matches every major DCC and
  gives you explicit control over the highlight.

**Selection rule.** `coat_roughness` defaults to `-1` (sentinel). While
it stays negative the engine uses the legacy `clearcoat_gloss` path. As
soon as you set `coat_roughness >= 0` (or bind `coat_roughness_texture`),
the Arnold path takes over and `clearcoat_gloss` is ignored — the
conversion is roughly `coat_roughness ≈ 1 - clearcoat_gloss`.

> **New scenes should use `coat_roughness` + `coat_ior`.** Existing
> scenes keep working without changes; nothing is removed.

##### **`subsurface_radius`: parsed but not used**

`subsurface_radius` is reserved for a future random-walk SSS pipeline.
The current approximate subsurface lobe (`subsurface` + `subsurface_color`
+ optional `flatness`) never reads it. The loader emits an `Info`
message at scene load when the key is present — omit it from new scenes.

- **When to use:**
  - Metals: `metallic=1.0`, varied roughness. Add `anisotropic` for brushed steel.
  - Plastics: `metallic=0.0`, `roughness=0.4–0.8`
  - Car paint: `metallic=0.0`, `clearcoat=1.0` (+ `coat_roughness` for Arnold-style coat)
  - Fabric / velvet: `metallic=0.0`, `sheen=0.8–1.0`, `sheen_roughness=0.2–0.4`
  - Skin: `metallic=0.0`, `subsurface=0.4`, `subsurface_color=[1.0, 0.6, 0.55]`, `flatness=0.3`
  - Clear glass: `spec_trans=1.0`, `roughness=0.0`, `ior=1.52`
  - Coloured glass: add `transmission_color` + `transmission_depth` (e.g. 5 units for a bottle of brandy)
  - Soap bubble / opal: `thin_film_thickness=350..700`, `thin_film_ior=1.33..1.5`
  - Leaves / paper: `diff_trans=0.5`, `thin_walled=true`
- **⚠️ Noise:** Disney still has more lobes than the classics; use ~4× samples for skin/glass/clearcoat hero shots.
- **💡 Best practice:** Use lambertian for big surfaces, Disney only for protagonist objects.
#### **5.6 Mix Material (Blend Two Materials)**
```yaml
- id: "rusty_metal"
  type: "mix"
  material_a: "chrome"
  material_b: "rust"
  blend: 0.4                               # 40% rust, 60% chrome (constant)
  # OR use mask for spatial blending:
  mask:
    type: "noise"                          # Procedural texture
    scale: 3.0
    noise_strength: 5.0
  normal_map: (optional)
```
- Seamlessly blend any two materials
- Mask can be: `noise`, `marble`, `wood`, `checker`, `image`
- Useful for: weathering, wear, transitions, decals
- Mix-of-mix nesting supported
---
### 6. **TEXTURES** — Embedded in Materials
Textures are defined **within** material definitions. All procedural textures
are pro-grade, with the same controls exposed by Arnold (`noise`, `cell_noise`),
Cycles (Noise / Voronoi / Brick / Gradient nodes) and RenderMan (`PxrFractal`,
`PxrVoronoise`, `PxrMarble`, `PxrTile`).

#### **Procedural Textures:**

**Checker:**
```yaml
texture:
  type: "checker"
  scale: 4.0
  colors: [[0.9, 0.9, 0.9], [0.1, 0.1, 0.1]]
```

**Noise:**
```yaml
texture:
  type: "noise"
  noise_type: "fbm"            # perlin | fbm | turbulence | ridged | billow | hetero_terrain | hybrid_multifractal
  scale: 5.0
  octaves: 5                   # 1..16 — fBm/ridged/billow/musgrave octave count
  lacunarity: 2.0              # frequency multiplier between octaves
  gain: 0.5                    # amplitude decay between octaves (fbm/ridged/billow)
  fractal_increment: 1.0       # Musgrave H — only used by hetero_terrain / hybrid_multifractal
  fractal_offset: 0.7          # Musgrave offset / "sea level" — only used by hetero_terrain / hybrid_multifractal
  distortion: 0.0              # domain-warp amplitude (organic shapes)
  noise_strength: 0.0          # legacy: 0=smooth Perlin, >0=turbulent (overridden by noise_type)
  colors: [[0, 0, 0], [1, 1, 1]]
```
The seven noise families map onto the standard pro-renderer modes:
- `perlin` — single-octave smooth gradient noise.
- `fbm` — Σ noise/2^i, the canonical "fractal noise" of Arnold/Cycles/RenderMan.
- `turbulence` — Σ|noise|/2^i with absolute-value sharpening.
- `ridged` — Musgrave ridged multifractal, sharp ridges (rocks, lightning).
- `billow` — Σ|noise| octaves, puffy / cloud-like.
- `hetero_terrain` — Musgrave heterogeneous terrain (Ebert et al. §16.3.3):
  per-octave amplitude scaled by the running accumulated value, so high
  ground gets rougher and valleys stay smooth. The canonical eroded-terrain
  look that pure fBm cannot reach.
- `hybrid_multifractal` — Musgrave hybrid multifractal (§16.3.4): per-octave
  signal multiplied by a running `weight` (clamped to 1), producing
  stratified rock layers and sharp peaks. Used for asteroids, alien rock,
  stratigraphic marble.

`distortion` warps the input position with a secondary Perlin sample
(Inigo Quilez technique); 0.3–0.8 is usually enough. `fractal_increment`
(Musgrave's H, default 1.0) controls how fast high-frequency octaves decay
— H ≈ 0.25 yields rough terrain, H ≥ 1 produces smooth, low-frequency
dominated fields. `fractal_offset` (default 0.7) is the "sea level" bias
added to each octave; higher values flatten valleys, lower values turn
everything into mountains. These two parameters are only used by the
`hetero_terrain` / `hybrid_multifractal` modes — the other noise kinds
ignore them.

**Marble:**
```yaml
texture:
  type: "marble"
  scale: 4.0
  noise_strength: 10.0
  vein_axis: [1, 0, 0.3]       # primary vein propagation direction (default Z)
  vein_frequency: 1.0          # multiplier on the sine term
  vein_sharpness: 4.0          # 1=soft (legacy), 4-8=Carrara-thin veins
  noise_type: "turbulence"     # turbulence | fbm | ridged
  octaves: 7                   # fractal octaves
  lacunarity: 2.0
  gain: 0.5
  distortion: 0.0
  colors: [[0.95, 0.95, 0.95], [0.10, 0.10, 0.15]]
  secondary_wave:              # optional — Statuario / Calacatta cross-veins
    axis: [1, 0, 0]            # second vein direction (auto-orthogonalised)
    frequency: 0.7             # independent of primary frequency
    strength: 0.5              # 0 = disabled (back-compat), ~0.3-0.7 typical
```
The vein axis controls the direction along which veins propagate; pick a
non-axis-aligned vector for natural slabs. `vein_sharpness` exponentiates the
sine wave, narrowing the dark band into a real veining line.

> **Studio-quality `secondary_wave`.** Setting `strength > 0` adds a second
> sinusoid along `secondary_wave.axis` to the primary vein term:
> `sin(wave1) + strength · sin(wave2)`, renormalised so the combined output
> stays in [-1, 1]. The secondary axis is auto-orthogonalised against the
> primary at sample time, so even picking a collinear axis still produces
> visible cross-veining — the Statuario / Calacatta / Arabescato look that
> single-axis marble cannot reach. Combine with a 3+ stop `color_ramp:` for
> vein → mid-tone → base → undertone authoring. `strength = 0` (default)
> is bit-identical to the legacy implementation.

**Wood:**
```yaml
texture:
  type: "wood"
  scale: 4.0
  noise_strength: 2.0          # alias: grain_strength (high-freq grain amplitude)
  ring_axis: [0, 1, 0]         # trunk axis; rings ⊥ axis (default Y)
  ring_sharpness: 3.0          # 1=soft (legacy), 3-6=defined latewood
  axial_grain: 0.3             # long-wave noise along the trunk axis
  octaves: 4                   # fBm octaves on the grain (1 = legacy Perlin)
  lacunarity: 2.0
  gain: 0.5
  distortion: 0.0              # 0=clean rings, ~0.5=knots/waves
  colors: [[0.85, 0.65, 0.40], [0.60, 0.40, 0.20]]
  # ── Studio-quality knobs (opt-in, all default to no-op back-compat) ──
  grain_scale: 1.0             # multiplier on the high-freq noise sample point
  figure_scale: 0.25           # multiplier on the low-freq "figure" sample point
  figure_strength: 0.0         # 0 = disabled, ~0.5-1.5 = curly maple / flame mahogany
  radial_anisotropy: 0.0       # 0 = isotropic (plain-sawn), >0 = quartersawn
  knot_density: 0.0            # 0 = no knots, ~0.5 = sparse, ~1 = packed
```

> **Studio-quality wood controls.**
> * **Two-band perturbation.** `grain_scale` + `noise_strength` (a.k.a.
>   `grain_strength`) drive the high-frequency fibre detail inside each
>   ring; `figure_scale` + `figure_strength` add an independent low-frequency
>   plank-wide undulation — the curly maple stripes, flame mahogany ripples
>   or bird's-eye blooms that pure grain noise cannot reach because its
>   spectrum is too high-frequency. Each band has its own scale and weight.
> * **`radial_anisotropy`.** Stretches the noise sample along the local
>   radial direction (perpendicular to `ring_axis`, pointing away from the
>   trunk). High values (~2–5) compress the radial sample coordinate so
>   noise varies slowly along that axis ⇒ the quartersawn-oak look. 0
>   (default) is isotropic and bit-identical to the legacy texture.
> * **`knot_density`.** Sparse small-scale Voronoi spawns branch knots that
>   locally pull the ring centre toward the knot feature and add a dark
>   heart on top — same kind of behaviour as Arnold's `knots` map and
>   RenderMan's `PxrWoodKnot`. Combine with a 3+ stop `color_ramp:` for
>   sapwood / heartwood / knot tri-tone authoring.

#### **Production-quality marble & wood — recipe book**

The studio-quality knobs interact non-trivially with the BSDF and the
lighting setup. The recipes below come from the
`marble-wood-studio-showcase.yaml` reference scene; copy the matching
snippet and tweak the colour ramp to ship a credible material in minutes.

> **Lighting checklist before tuning a marble.** A polished marble at
> `roughness < 0.2` becomes a near-mirror that reflects the environment
> verbatim — if the sky is bright and untextured the marble reads as
> "blue gradient" instead of marble. Three rules:
>
> 1. **Use a dark or near-black sky for lookdev shots** (`type: "flat"`,
>    `color: [0.001, 0.001, 0.0012]`). The marble pattern then carries
>    the visual, not the environment.
> 2. **Roughness 0.30–0.34 for "lucido"** marble where the texture must
>    read; raise clearcoat (0.85+) for the polished glass-like top layer.
>    Lower roughness only when you want a true mirror finish — for
>    that, you typically *do* want some HDRI reflections.
> 3. **Direct lighting must dominate.** A directional key at intensity
>    5–7 plus a cool fill and a warm rim point lights light up the
>    diffuse component above the specular reflection. Without this
>    triad the BSDF integrator can't separate texture from environment.

**Carrara — white base with thin dark veins.**
```yaml
- id: "carrara"
  type: "disney"
  roughness: 0.32
  specular: 0.5
  texture:
    type: "marble"
    scale: 5.0
    vein_axis: [0, 0, 1]
    vein_frequency: 1.0
    vein_sharpness: 4.0           # → wide base, narrow vein
    noise_strength: 8.0
    octaves: 8
    colors: [[0.96, 0.94, 0.91], [0.10, 0.08, 0.10]]
```
With `vein_sharpness ≥ 3` the average sample lands close to `colors[0]`
(base), so the BASE colour is the dominant area and the vein colour
narrows to thin lines — exactly the real Carrara look.

**Calacatta — cross-veining with gold transitions.**
```yaml
- id: "calacatta"
  type: "disney"
  roughness: 0.30
  texture:
    type: "marble"
    scale: 4.0
    vein_axis: [0, 0, 1]
    vein_sharpness: 3.0           # wider veins than Carrara (Calacatta is bolder)
    noise_strength: 6.0
    octaves: 8
    secondary_wave:
      axis: [1, 0, 0]             # auto-orthogonalised against primary
      frequency: 0.65             # non-integer ratio prevents moiré
      strength: 0.45
    color_ramp:
      - { position: 0.00, color: [0.10, 0.08, 0.08], interp: "smoothstep" }
      - { position: 0.35, color: [0.85, 0.65, 0.30], interp: "smoothstep" }
      - { position: 0.70, color: [0.92, 0.85, 0.72], interp: "smoothstep" }
      - { position: 1.00, color: [0.97, 0.95, 0.90], interp: "linear"     }
```
Convention with the corrected sharpening: ramp **position 0 = vein**
(rare, `t→0`), **position 1 = base** (dominant, `t→1`); intermediate
stops paint the transition.

**Arabescato — strong cross-veining + chaotic distortion.**
```yaml
- id: "arabescato"
  type: "disney"
  roughness: 0.34
  texture:
    type: "marble"
    scale: 3.5
    vein_sharpness: 2.0
    noise_strength: 8.0
    distortion: 0.35              # extra Perlin domain warp
    secondary_wave: { axis: [1, 0.3, 0], frequency: 1.2, strength: 0.7 }
    color_ramp:
      - { position: 0.00, color: [0.08, 0.08, 0.10], interp: "smoothstep" }
      - { position: 0.50, color: [0.55, 0.50, 0.48], interp: "smoothstep" }
      - { position: 1.00, color: [0.94, 0.92, 0.88], interp: "linear"     }
```

**Oak quartersawn — fibrous radial grain.**
```yaml
- id: "oak_quartersawn"
  type: "disney"
  roughness: 0.55
  texture:
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

**Curly maple — wide rippled figure.**
```yaml
- id: "curly_maple"
  type: "disney"
  roughness: 0.42
  texture:
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

**Knotty pine — branch knots with dark hearts.**
```yaml
- id: "knotty_pine"
  type: "disney"
  roughness: 0.55
  texture:
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

A pre-baked catalogue of these recipes — Carrara, Calacatta, Statuario,
Arabescato, Port Laurent, Rosso Levanto + oak quartersawn, curly maple,
flame mahogany, knotty pine, bird's-eye maple, walnut burl, frassino
quartersawn, fir knotty — ships in
`scenes/libraries/materials/stones.yaml` and `woods.yaml` under the
`_studio` suffix. Import once and reference by id.

**Voronoi / Worley (cellular):**
```yaml
texture:
  type: "voronoi"
  scale: 5.0
  metric: "euclidean"          # euclidean | euclidean_squared | manhattan | chebyshev
  output: "f1"                 # f1 | f2 | f2_minus_f1 | f1_plus_f2 | cell
  randomness: 1.0              # 0 = grid, 1 = full random scatter
  distortion: 0.0              # Perlin warp before lookup
  smoothness: 0.0              # 0 = hard min (classic); ∈ (0,1] enables IQ Smooth Voronoi
  colors: [[0, 0, 0], [1, 1, 1]]   # ignored for output: "cell"
```
Mirrors Cycles' Voronoi Texture: `f1` gives stone/pebble blobs,
`f2_minus_f1` gives sharp "crackle" ridges (cracked-mud, snake-skin),
`cell` gives per-cell flat colours. The Chebyshev metric reproduces
hex/square tiling.

> **Note on `f2_minus_f1`.** Mathematically, `F2-F1` is **zero on the cell
> boundary** (perpendicular bisector between two feature points) and grows
> to its maximum at the cell centre. The lerp uses `t = sqrt(F2-F1 / norm)`
> — sqrt compression matches Cycles' "Distance to Edge" response — so
> `t = 0` → `colors[0]` is the **edge colour** and `t = 1` → `colors[1]`
> is the **cell-interior colour**. For the classic crackle look (bright
> thin lines on dark background) put the **bright** colour FIRST and the
> **dark** colour SECOND.

> **Smooth Voronoi (`smoothness`).** When `smoothness > 0` the hard `min()`
> over the 3×3×3 neighbouring cells is replaced by Inigo Quilez' log-sum-exp
> soft-min `-log(Σ exp(-k·d_i)) / k` with `k = 20/smoothness`. F1 becomes
> C∞-continuous across cell boundaries; F2 is built from the same
> accumulation with the dominant (closest-cell) weight excluded, so
> `f2_minus_f1` loses its V-shaped ridge — bordi morbidi, no step alias on
> the crease lines. Use it for polished leather, water-smoothed pebbles,
> supple reptile skin, closed-pore marble. `smoothness = 0` (default) is
> bit-identical to the legacy hard min. The Cell output is intentionally
> unaffected (cell-ID lookup is discrete, matching Cycles' behaviour).
> Numerical contract: the accumulator runs in double precision and the
> sum is rebased on the hard nearest so no `exp()` argument ever exceeds
> `0`; with `smoothness → 0` (i.e. `k → ∞`) the result converges to the
> classic hard `Evaluate` to within float32 precision.

**Brick:**
```yaml
texture:
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
texture:
  type: "gradient"
  mode: "linear"               # linear | quadratic | easing | spherical | radial
  axis: [1, 0, 0]              # gradient direction (linear/quadratic/easing/radial)
  length: 1.0                  # world-space span over which the gradient runs
  colors: [[0, 0, 0], [1, 1, 1]]
```
- `linear` — `t = (p · axis) / length`.
- `quadratic` / `easing` — same `t` then squared or smoothstepped.
- `spherical` — distance from origin / `length`.
- `radial` — distance from the `axis` line / `length` (cylindrical falloff).

**All procedurals support:**
```yaml
offset: [5.0, 0.0, 3.0]                  # Translation
rotation: [0.0, 45.0, 0.0]               # Rotation (degrees)
randomize_offset: true                    # Per-object variation
randomize_rotation: true
```

**Multi-stop color ramp (`color_ramp:`)** — optional override of the
implicit two-colour lerp on `noise`, `marble`, `wood`, `voronoi` and
`gradient`. Mirrors Cycles' ColorRamp node, Arnold's `ramp_rgb` and
RenderMan's `PxrRamp`:
```yaml
texture:
  type: "marble"
  vein_sharpness: 4.0
  color_ramp:
    - { position: 0.00, color: [0.05, 0.05, 0.07], interp: "smoothstep" }
    - { position: 0.45, color: [0.55, 0.45, 0.32], interp: "linear"     }
    - { position: 0.55, color: [0.95, 0.93, 0.88], interp: "linear"     }
    - { position: 1.00, color: [0.05, 0.05, 0.07], interp: "linear"     }
```
- `position` ∈ [0, 1] — clamped if outside; stops auto-sort by position.
- `color: [r, g, b]` — linear-space RGB.
- `interp` (per stop, controls the *outgoing* segment toward the next stop):
  - `linear` — straight lerp (default).
  - `smoothstep` — Hermite cubic `3t² − 2t³` (C¹ continuity, the smooth
    transition Cycles uses for "Ease").
  - `ease` — Perlin smootherstep `6t⁵ − 15t⁴ + 10t³` (C² continuity, the
    quintic curve with zero first and second derivative at both endpoints
    — broad photo-real shoulders).
  - `constant` — hold the colour until the next stop (step function).
- Below the first position the first colour holds; above the last
  position the last colour holds.
- Coincident stops (same `position`) produce a hard break — artist trick
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
texture:
  type: "image"
  path: "textures/brick.png"              # Relative to YAML file
  uv_scale: [2, 1]                        # Tiling factor
```
- Supports: PNG, JPEG, BMP, GIF, TIFF, WebP
- Automatically converted from sRGB to linear
- Bilinear filtering for smoothness
- Wrapping for seamless tiling
- Analytic anti-aliasing (mipmap + EWA anisotropic) when ray differentials
  are available — enabled by default; toggle from the CLI with
  `--texture-filtering <auto|on|off>` (see [rendering-profiles.md §6c](./rendering-profiles.md)).
  The same flag also drives the analytic octave clamp on procedural
  noise/fBm/marble/wood/voronoi.
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
- Applies to any material type (lambertian, metal, dielectric, disney, emissive, mix)
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
  strength: 3.0                            # Perturbation amplitude (0–10, clamped)
  scale: 1.0                               # Uniform UV multiplier (default 1)
```

Like `normal_map` but driven by a **scalar height field** sampled from any
procedural or image texture (Rec.709 luminance). The shading normal is
perturbed via central differences in tangent space (Blinn 1978). Aligns
with Arnold's `bump2d`, RenderMan's `PxrBump`, Cycles' "Bump" node.

| Field      | Type                | Default | Description                                                                 |
|------------|---------------------|---------|-----------------------------------------------------------------------------|
| `texture`  | TextureData         | —       | Inner height field. Any procedural (`noise`, `marble`, `wood`, `voronoi`, `brick`, `gradient`, `checker`) or `image`. |
| `strength` | float ∈ [0, 10]     | `1.0`   | Amplitude of the perturbation. Above ~5 the bump looks rocky; ~0.5–1.0 reads as fine detail. |
| `scale`    | float > 0           | `1.0`   | Uniform UV multiplier stacked on top of the inner texture's own `uv_scale` / `scale`. |

**Composition order** when both `normal_map` and `bump_map` are present
(Arnold/Cycles convention):

1. `normal_map` runs first, replacing the geometric normal.
2. `bump_map` runs second, perturbing the **already-perturbed** normal
   (TBN re-orthogonalised against it).
3. Disney's `coat_normal_map` is **independent** — coat keeps its own
   surface frame and does not see the bump.

Applies to every material type (lambertian, metal, dielectric, disney,
emissive, mix). Works on every primitive that populates the TBN basis —
the engine populates TBN on Sphere, Box, Cylinder, Cone, Quad, Disk,
Annulus, Torus, Capsule, Lathe, Triangle, SmoothTriangle, and
InfinitePlane (i.e. all of them).

The killer advantage over `normal_map` is **procedural input**: infinite
resolution, no asset to ship, full reuse of the existing texture library
(noise/marble/wood/voronoi/brick/gradient).
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
### 7. **ENTITIES SECTION** — 3D Objects

**Common per-entity fields** (apply to every type below: primitive, csg, mesh,
group, instance):

| Field | Default | Notes |
|-------|---------|-------|
| `name` | — | Optional label for logging / debugging |
| `material` | inherited | Material ID, resolved from the `materials` block |
| `seed` | auto | Stable integer that drives procedural texture variation; auto-derived from name+type+index when omitted |
| `visible_to_camera` | `true` | Hide from primary camera rays only. Mirrors Arnold's `camera` visibility flag and Cycles' "Ray Visibility → Camera": the entity still appears in specular reflections/refractions, still receives and casts indirect illumination, and (if emissive) still contributes to direct lighting via NEE. Typical use: emissive panel that acts as a fill light but should not show up as a bright rectangle in the frame, off-screen practicals visible only via reflections. Set on a `group` to propagate to every child. |
| `scale`, `rotate`, `translate` | identity | Optional local transform (applied scale → rotate → translate) |

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
  outer_radius: 1.0
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
#### **7.10 Plane (Infinite)**
```yaml
- name: "floor"
  type: "infinite_plane"
  point: [0, 0, 0]                        # Point on plane
  normal: [0, 1, 0]
  material: "wood"
```
#### **7.11 Triangle / SmoothTriangle**
```yaml
- name: "poly"
  type: "triangle"
  v0: [0, 0, 0]
  v1: [1, 0, 0]
  v2: [0.5, 1, 0]
  material: "red"
- name: "smooth_poly"
  type: "smooth_triangle"
  v0: [0, 0, 0]
  v1: [1, 0, 0]
  v2: [0.5, 1, 0]
  n0: [0, 0, 1]
  n1: [0.1, 0, 0.9]
  n2: [0.1, 0, 0.9]
  material: "plastic"
```
#### **7.12 Mesh (OBJ Files)**
```yaml
- name: "model"
  type: "mesh"
  path: "models/teapot.obj"               # Relative to YAML
  scale: [2.0, 2.0, 2.0]
  translate: [0, 1, 0]
  material: "ceramic"
```
- Supports Wavefront OBJ format
- Auto-builds internal BVH for fast intersection
- Smooth shading with artist UV mapping
- Supports `translate`, `rotate`, `scale`

##### **Subdivision surfaces (Loop / Catmull-Clark)**

The mesh loader can refine the OBJ before BVH construction using the same
two production-grade algorithms shipped by Arnold, RenderMan, Cycles and
Pixar's OpenSubdiv:

```yaml
- name: "smooth_cube"
  type: "mesh"
  path: "models/cube.obj"
  material: "ceramic"
  subdivision_scheme: "catmull_clark"     # loop | catmull_clark | auto | none
  subdivision_iterations: 3               # uniform refinement steps
```

| Field                       | Type   | Default | Notes |
|-----------------------------|--------|---------|-------|
| `subdivision_scheme`        | string | `none`  | `loop` (triangle meshes), `catmull_clark` (quad meshes — also accepts triangles and n-gons in the first iteration), `auto` (picks CC for all-quad input, Loop for all-triangle, CC otherwise), `none`. |
| `subdivision_iterations`    | int    | `0`     | Uniform iteration count. Each iteration multiplies face count by ≈ 4. |
| `subdivision_pixel_error`   | float  | `0`     | Adaptive screen-space target. The loader picks the iteration count that brings the longest projected edge below this many pixels (using the scene's resolved camera). Combined with `subdivision_iterations` by `max(static, adaptive)`. |
| `subdivision_max_iterations`| int    | `6`     | Hard ceiling regardless of the adaptive estimate (caps the 4^N face explosion). |

- **Loop subdivision** (Charles Loop, 1987) — boundary mask per Hoppe et
  al. 1994. Triangles only; n-gons in the source are fan-triangulated first.
- **Catmull-Clark** (Catmull & Clark, 1978) — boundary mask per
  Hoppe / DeRose. Mixed-arity input is handled in the first iteration,
  after which the mesh is all-quads.
- Per-vertex normals are **recomputed from the limit topology** using
  Max 1999's angle-weighted average (the Blender / Maya default). Source
  OBJ normals are propagated through subdivision but overridden at
  triangulation time because the limit surface is smoother than the input.
- UV channels carry through with linear mid-edge masks (vertex-varying
  interpolation in OpenSubdiv terms). UV seams that share a position but
  not a UV are preserved.

##### **Scalar displacement (true silhouette deformation)**

The mesh loader can apply a scalar height-field displacement to the
(sub)divided mesh **before BVH construction**. Unlike `bump_map`, which
perturbs only the shading normal, scalar displacement physically moves
the vertices along the limit-surface smooth normal:

```yaml
- name: "stone_panel"
  type: "mesh"
  path: "models/plane.obj"
  material: "stone"
  subdivision_scheme: "catmull_clark"
  subdivision_iterations: 6
  displacement:
    texture:                                # any ITexture (procedural or image)
      type: "noise"
      noise_type: "fbm"
      scale: 3.5
      octaves: 5
      colors: [[0, 0, 0], [1, 1, 1]]
    scale: 0.30                             # world-unit amplitude
    midlevel: 0.5                           # luminance treated as "flat" (0.5 for 8-bit greys)
    uv_scale: 1.0                           # uniform UV multiplier (default 1.0)
  displacement_bound: 0.30                  # max expected displacement (BVH leaf AABB padding)
```

The vertex update is `v' = v + scale · (h − midlevel) · n_smooth`, where
`h = luminance(texture.Value(u, v, p))` and `n_smooth` is the angle-weighted
average of incident face normals on the limit topology (Max 1999 — the
Blender/Maya/OpenSubdiv default). After displacement the per-vertex shading
normals are recomputed from the displaced topology so the BSDF sees the
new silhouette's actual normal field, not the pre-displacement one.

| Field                  | Type        | Default | Notes |
|------------------------|-------------|---------|-------|
| `displacement.texture` | TextureData | —       | Inner height field. Any procedural (`noise`, `marble`, `wood`, `voronoi`, `brick`, `gradient`, `checker`) or `image`. Sampled as Rec.709 luminance, same convention as `bump_map`. |
| `displacement.scale`   | float       | `0.1`   | Signed amplitude in world units. Negative pushes inward. `0` disables. |
| `displacement.midlevel`| float       | `0`     | Luminance treated as "no displacement". `0.5` for 8-bit greys where 128 means flat (matches RenderMan's `dispMidpoint`). |
| `displacement.uv_scale`| float > 0   | `1.0`   | Uniform UV multiplier stacked on top of the inner texture's own `uv_scale`. |
| `displacement_bound`   | float ≥ 0   | `\|scale\|` | Maximum expected displacement amplitude. Pads every BVH leaf AABB by this much (Arnold's `disp_padding`, RenderMan's `dispBound`). Auto-derived from `scale` when omitted. The loader warns when the actually-applied displacement exceeds the bound so the value can be raised. |

**Pipeline order.** The pipeline runs `subdivide → displace → triangulate
→ BVH`. Displacement on an un-subdivided low-poly mesh moves the original
vertices and is rarely visually useful; combine it with
`subdivision_iterations ≥ 4` (or an adaptive `subdivision_pixel_error`)
to expose enough micro-vertices for a smooth deformation.

**Mesh-only by design.** Scalar displacement is restricted to `type: mesh`
entities. Built-in primitives (`sphere`, `cylinder`, `torus`, …) use
`bump_map` for sub-pixel detail — the same architectural choice made by
Arnold (`displacement` only on `polymesh`) and Cycles (True Displacement
only on subdiv-capable nodes).

**Bump + displacement composition.** When a material declares a `bump_map`
and the entity declares a `displacement`, the displacement handles the
macro silhouette (vertex positions, BVH) and the bump handles sub-pixel
detail on top of the displaced shading normal. This mirrors Arnold's
"autobump" workflow.

##### **Vector displacement (RGB → XYZ offset)**

A scalar height field can only push micro-vertices outward (or inward)
along the smooth normal — a useful constraint that nevertheless rules
out **overhangs**, **crinkles**, and any feature that bends back over
itself. **Vector displacement** lifts that restriction by interpreting
the texture's RGB triplet as a full 3D offset:

```yaml
- name: "sculpt_panel"
  type: "mesh"
  path: "models/plane.obj"
  material: "stone"
  subdivision_scheme: "catmull_clark"
  subdivision_iterations: 6
  displacement:
    mode: "vector"                            # default is "scalar"
    space: "tangent"                          # or "object"
    texture:
      type: "image"
      path: "textures/sculpt_vector_disp.exr" # any RGB ITexture
    scale: 0.5
    midlevel: 0.5                             # 0.5 for unsigned 8-bit storage; 0 for signed-float EXR
  displacement_bound: 0.9
```

The vertex update is `v' = v + scale · (rgb − midlevel) · basis`. The
basis is:

| Space        | R → axis | G → axis | B → axis | Notes |
|--------------|----------|----------|----------|-------|
| `tangent`    | T        | B (bitangent) | N (normal) | Mudbox / Maya / ZBrush / Cycles tangent-bake convention. Requires a UV channel; if absent the loader silently falls back to `object`. |
| `object`     | +X       | +Y       | +Z       | RGB is added directly to the mesh-local position. Independent of UV parametrisation — useful for sculpts shared across assets with different UVs. |

| Field                 | Default     | Notes |
|-----------------------|-------------|-------|
| `displacement.mode`   | `"scalar"`  | `"scalar"` reads luminance and offsets along the normal; `"vector"` reads the full RGB as a 3D offset. |
| `displacement.space`  | `"tangent"` | `"tangent"` or `"object"`. Vector mode only; ignored on scalar. Tangent mode requires a UV channel. |
| `displacement.scale`  | `0.1`       | World-unit amplitude. In vector mode the full RGB triplet is multiplied component-wise. |
| `displacement.midlevel` | `0`       | Subtracted from every channel. `0.5` for 8-bit signed-stored maps where 128 means flat (Mudbox/ZBrush default); `0` for signed-float EXRs. |
| `displacement.uv_scale` | `1.0`     | Uniform UV multiplier. |
| `displacement_bound`  | `\|scale\|·√3` (vector), `\|scale\|` (scalar) | Per-leaf BVH AABB padding. The vector default accounts for the L2 length of an offset whose three components can each reach `\|scale\|`. The loader warns when the actually-applied displacement exceeds the bound and prints the value to use. |

**Tangent-space convention.** The engine derives per-vertex tangents
from the UV gradient (Lengyel 2001 face-tangent formula), accumulates
them angle-weighted across incident triangles, orthonormalises the
result against the smooth normal via Gram-Schmidt, and preserves
handedness from the accumulated bitangent (MikkTSpace's rule). This is
the same convention every consumer of "tangent-space displacement
maps" baked from Mudbox / Maya / ZBrush expects.

**Pipeline order.** Identical to scalar: `subdivide → displace → triangulate
→ BVH`. The displacement engine dispatches on `mode` and applies the
corresponding offset; subdivision and BVH construction are unaware of
which mode ran.

**Bump + vector displacement.** The composition rule is the same as for
scalar: the vector displacement handles the macro silhouette (including
overhangs), and a material-level `bump_map` adds sub-pixel detail on
top of the displaced shading normal. The recomputed normals after a
vector displacement pass already reflect the new silhouette, including
the parts that bend back on themselves, so the bump perturbation
inherits the correct orientation automatically.

The showcase scene `scenes/showcases/vector-displacement-showcase.yaml`
puts three panels (scalar reference, vector tangent-space, vector
object-space) side by side so the silhouette differences are visible
at a glance, plus a CC×4 cube driven by ridged-fBm vector displacement
that demonstrates the overhang-producing behaviour.

##### **Autobump (bump derived from the displacement texture)**

Subdivision + displacement build the macro silhouette down to the
sampling rate of the displacement texture. Detail finer than the
subdivision grid (the high-frequency tail of an fBm, the rim of a
Voronoi cell, the engraved scratch on a sculpt) is geometrically
smoothed out — exactly the artefact Arnold's `autobump_visibility` flag
on a `polymesh` was designed to recover. Setting `autobump: true` on
the displacement block tells the engine to build a residual
`bump_map` from the same texture and attach it to the mesh; the
renderer applies it on top of any material-level `bump_map` at shading
time.

```yaml
- name: "carved_stone"
  type: "mesh"
  path: "models/stone.obj"
  material: "porcelain"
  subdivision_scheme: "catmull_clark"
  subdivision_iterations: 4               # moderate subdivision — autobump fills the rest
  displacement:
    texture:
      type: "noise"
      noise_type: "fbm"
      scale: 4.5
      octaves: 6
    scale: 0.18
    midlevel: 0.5
    autobump: true                        # ← step 5: residual bump from same texture
    autobump_strength: 1.5                # bump amplitude = autobump_strength · |scale|
    autobump_scale: 1.0                   # UV-frequency multiplier (1 = match displacement)
  displacement_bound: 0.20
```

| Field                          | Default | Notes |
|--------------------------------|---------|-------|
| `displacement.autobump`        | `false` | When `true`, the displacement texture is reused as a residual bump map (Arnold's `autobump_visibility`). Off by default — pre-step-5 scenes render byte-identically. |
| `displacement.autobump_strength` | `1.0` | Bump strength multiplier; the final amplitude passed to the internal `BumpMapTexture` is `autobump_strength · \|displacement.scale\|`. Setting it to 0 disables the autobump silently (loader warns). |
| `displacement.autobump_scale`  | `1.0`   | UV-frequency multiplier stacked on top of `displacement.uv_scale`. `>1` samples the bump finer than the displacement (the typical "macro displacement + micro autobump" workflow); `=1` matches the displacement frequency. |

**Composition order.** The engine combines the four perturbation
channels in the same order Arnold/Cycles use:

```
geometry normal (post-displacement)
  → material.normal_map
    → material.bump_map
      → mesh.autobump                 (← derived from displacement.texture)
```

`coat_normal_map` on the Disney BSDF is **independent** — it perturbs
only the clearcoat lobe and is unaffected by the base bump stack
(parity with Arnold's standard surface and Cycles' Principled BSDF).
Vector displacement works the same way: the autobump samples the
luminance of the same vector-displacement texture, recovering the
high-frequency component along the displaced shading normal.

**Mesh-only.** Autobump shares the displacement's mesh-only constraint
— it is only active on `type: mesh` entities. Built-in primitives keep
using a stand-alone `bump_map` for sub-pixel detail.

The showcase scene `scenes/showcases/bump-displacement-combo-showcase.yaml`
puts four panels (flat reference, displacement only, displacement +
autobump, and displacement + autobump + material bump) side by side at
a deliberately moderate `subdivision_iterations: 4` so the recovered
sub-grid detail is immediately visible. The vector-displaced rock
overhead demonstrates the same recovery on a non-planar mesh.

#### **7.13 CSG (Boolean Operations)**
```yaml
# Union (A ∪ B) — fuses two solids into one (e.g. snowman body + head)
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

# Intersection (A ∩ B) — keeps only the volume shared by both solids (lens shape)
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

# Subtraction (A \ B) — removes B from A (bead: sphere with a hole drilled through it)
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
- Operations: `union` (A∪B), `intersection` (A∩B), `subtraction` (A\B); `subtract` and `difference` are accepted aliases of `subtraction`
- Child keys are `left` and `right` (Boolean-tree operands of the node)
- Supports recursively nested CSG trees (a `left` or `right` can itself be a `csg` node)
- **Valid CSG child types.** Each child must be a solid primitive with well-defined interior/exterior. Supported: `sphere`, `box`, `cylinder`, `cone`, `torus`, `capsule`, `quad`, `disk`, `annulus`, `triangle`, `lathe` (and aliases `revolution` / `surface_of_revolution`), `extrusion` (and aliases `prism` / `linear_extrude`), or a nested `csg`. **Not supported and skipped with a warning** (loader emits `CSG entity '…': failed to create one or both children. Skipping.` and the node is dropped): `group`, `mesh` / `obj`, `instance`, `plane` / `infinite_plane`. To union two primitives as a CSG operand, use an explicit `csg: union` rather than wrapping them in a `group`.
- **Emissive materials inside CSG children** are geometrically valid but CSG nodes are not samplable, so they **will not participate in NEE** (Next Event Estimation). The loader prints a one-time warning: `Warning: CSG object contains an Emissive leaf. CSG objects are not sampleable, so their emitters will NOT participate in Next Event Estimation. The emissive surface will still glow via indirect bounces (high variance). Consider wrapping the emissive primitive outside the CSG if direct lighting is needed.` Workaround: place the emissive primitive alongside the CSG at the scene level rather than inside it.
#### **7.14 Group (Hierarchical Composition)**
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
#### **7.15 Template + Instance (Reusable Objects)**
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
#### **7.16 Lathe (Surface of Revolution)**
```yaml
# Linear profile — faceted look of a real turned piece (hard vertex ridges)
- name: "column"
  type: "lathe"                           # aliases: "revolution", "surface_of_revolution"
  profile_type: "linear"                  # default — can be omitted
  material: "marble"
  profile:                                # list of [r, y] points, y monotonic
    - [0.30, 0.0]
    - [0.30, 0.1]
    - [0.25, 0.2]
    - [0.28, 2.0]
    - [0.35, 2.1]

# Catmull-Rom profile — smooth, passes through every control point (centripetal)
- name: "vase"
  type: "lathe"
  profile_type: "catmull_rom"             # aliases: "catmull", "smooth"
  material: "ceramic"
  profile:
    - [0.00, 0.00]                        # closed bottom (r = 0 → cap absent)
    - [0.30, 0.00]
    - [0.55, 0.40]
    - [0.45, 0.80]
    - [0.55, 0.95]
    - [0.00, 0.95]                        # closed top

# Bezier profile — explicit 4 cubic-Bezier control points per segment
- name: "bowl"
  type: "lathe"
  profile_type: "bezier"
  material: "porcelain"
  profile:                                # segment endpoints — (N-1) segments
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
- Three interpolation modes. `linear` stacks analytic frustums — fast and
  exact, but shows hard vertex ridges. `catmull_rom` uses centripetal
  Catmull-Rom (Yuksel et al. 2011) — passes through every point, C¹
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
  Sturm chain + Newton-Raphson hybrid (`SturmSolver`), matching the
  approach used by PovRay's `lathe` and PBRT's `Curve`. Expect ~10× the
  per-ray cost of a Cone hit on spline segments — prefer `linear` when
  faceting is acceptable.

#### **7.17 Extrusion (Linear Extrusion of a 2D Profile)**
```yaml
# Linear concave profile — a 5-pointed star extruded into a prism.
- name: "star_pillar"
  type: "extrusion"                       # aliases: "prism", "linear_extrude"
  profile_type: "linear"                  # default — can be omitted
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
  profile_type: "catmull_rom"             # aliases: "catmull", "smooth"
  height: 4.0
  twist_degrees: 90                       # rotate the top profile around Y
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

# Bezier profile — explicit 4 cubic-Bezier control points per segment, looped
- name: "rounded_badge"
  type: "extrusion"
  profile_type: "bezier"
  height: 0.3
  material: "brass"
  profile:                                # segment endpoints — N segments forming a closed loop
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

# Linear + crease_angle — 12-sided polygon smoothed to read as a cylinder, not a faceted prism
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
  exactly `4 × N` entries — one cubic per profile segment, in a closed
  loop (the last segment wraps back to the first vertex).
- **Concave profiles work**: caps are triangulated by ear clipping, so
  stars, gears, letters, L-shapes, T/U/H sections and architectural
  profiles render correctly without manual decomposition.
- Profile orientation is auto-corrected: clockwise inputs are reversed at
  load time so wall outward normals always face away from the interior.
- `caps: "both"` (default) closes both ends; `"start"` / `"end"` keep only
  one cap (useful for trough/tray shapes); `"none"` produces an open
  shell.
- `twist_degrees` rotates the top profile around the Y axis — combined
  with `taper`, you get the wide range of architectural columns and
  industrial fittings produced by Houdini's `polyextrude` or Blender's
  "Extrude with twist".
- `curve_samples` controls the silhouette quality of `catmull_rom` /
  `bezier` profiles: each input segment becomes that many polyline
  samples (default 16, raise to 24-32 for hero close-ups).
- `crease_angle` (default `0`, `linear` mode only): dihedral threshold in
  degrees for per-vertex normal blending on linear side walls. Adjacent wall
  faces whose normals differ by less than this angle share a blended vertex
  normal (smooth shading, edge disappears in highlights); faces that differ by
  more keep their own flat face normals (hard edge). `0` gives fully faceted
  geometry — the historical default. 30° smooths polyline-approximated curves
  while preserving right-angle corners on letters, gears, and engineered
  sections. Ignored for `catmull_rom` and `bezier`, which always produce
  smooth side walls.
- Internally each extrusion builds its own BVH over the wall + cap
  triangles, so the outer scene BVH sees a single leaf per extrusion
  regardless of profile complexity. Smooth-shaded normals are emitted on
  the side walls for `catmull_rom` / `bezier`; `linear` defaults to flat
  per-face normals — set `crease_angle > 0` to blend normals across edges
  below the threshold and soften polyline-approximated curves without
  switching profile mode.
- Emissive Extrusions participate in NEE automatically: `Sample()` picks
  a triangle proportional to its area, so light from a star-shaped neon
  sign is correctly weighted across walls and caps.

#### **7.18 Transform Order and `center:` Anti-Pattern**

Entity transforms apply in a fixed `scale → rotate → translate` order around the **global origin (0, 0, 0)**:

```
world_pos = translate( rotate( scale( local_pos ) ) )
```

Primitives that expose a `center:` key — **sphere, cylinder, cone, capsule, torus, disk, annulus, lathe** — position their geometry *before* the outer transform is evaluated. Combining `center:` with `rotate:` or `scale:` therefore rotates and scales around the origin, not around the primitive's own center, producing unexpected results.

**Anti-pattern** — do not combine `center:` with `rotate:` or `scale:`:
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

**Correct pattern** — omit `center:` (defaults to `[0, 0, 0]`) and use `translate:` for final placement:
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
- When no `rotate:` or `scale:` is present — `center:` is equivalent to `translate:`.
- Inside **CSG children** (`left`/`right`) — CSG children have no outer transform, so `center:` positions them correctly.
- Inside **groups** when the child itself has no rotation — the group's own `translate`/`rotate` composes correctly on top.

---
### 8. **LIGHTS SECTION** — Five Types
#### **8.1 Point Light (Omnidirectional)**
```yaml
- type: "point"
  position: [2, 5, -3]
  color: [1.0, 0.95, 0.85]
  intensity: 20.0                          # Range: 4–30
  soft_radius: 0.0                         # Optional. >0 floors d² at r² → no 1/d² fireflies
```
- Quadratic falloff with distance
- Simple but effective for interior lighting
- `soft_radius` (default `0`): when set, the attenuation denominator is clamped to `max(d², r²)`. Removes the unbounded 1/d² spike that produces persistent fireflies in fog/medium scenes where scattering events can land arbitrarily close to the emitter. Recommended values approximate the physical bulb radius (e.g. `0.05`–`0.20`). At distances `d ≥ r` the lighting is unchanged.
#### **8.2 Directional Light (Sun)**
```yaml
- type: "directional"  # alias: "sun"
  direction: [-0.5, -1.0, -0.3]           # Direction light TRAVELS (light → scene).
                                          # Sun is at -direction.
  color: [1.0, 0.98, 0.92]
  intensity: 0.8                           # Range: 0.05–2.0
  angular_radius: 0.0                      # Optional. >0 = sun disc (soft shadows).
                                          #   0.27 = real solar disc. Default: 0.
```
- No distance attenuation
- Align with gradient sky `sun.direction` for visual coherence
- Good for outdoor key light
- `angular_radius` (default `0`): when > 0 the light models a disc of finite angular size. Each shadow ray is perturbed uniformly within the subtended cone, producing a soft penumbra. The real Sun is approximately 0.27°. When active, `shadow_samples` defaults to 4 and `IsDelta` becomes `false`, enabling full MIS weighting. Hard-shadow backward compatibility is preserved at the default 0.
#### **8.3 Spot Light (Cone)**
```yaml
- type: "spot"  # alias: "spotlight"
  position: [0, 5, 0]
  direction: [0, -1, 0]                   # Where spotlight points
  color: [1.0, 0.9, 0.7]
  intensity: 40.0
  inner_angle: 15                         # Degrees (full brightness)
  outer_angle: 30                         # Degrees (fade zone)
  soft_radius: 0.0                        # Optional. >0 = "virtual disc" emitter, no 1/d² fireflies
  shadow_samples: 1                       # Default 1. >1 enables jittered source for soft shadows.
```
- Quadratic falloff
- Smooth falloff between inner/outer cones
- Good for dramatic lighting, accent lights
- `soft_radius` (default `0`): same role as on point lights — clamps the attenuation denominator to `max(d², r²)`. Strongly recommended for spotlights illuminating a participating medium (fog, mist, smoke), where the 1/d² spike at scattering events near the emitter is the dominant firefly source. Typical values: `0.10`–`0.30` for a streetlamp-sized bulb.
- `shadow_samples` (default `1`): when > 1 AND `soft_radius > 0`, each shadow sample jitters the source within a disc of radius `soft_radius` perpendicular to `direction`, modelling the physical bulb extent. Produces soft penumbra in fog. If `soft_radius == 0`, extra samples are redundant (no position jitter) — keep at 1 for efficiency.
#### **8.4 Area Light (Soft Shadows)**
```yaml
- type: "area"  # aliases: "area_light", "rect", "rect_light"
  corner: [-1.5, 4.99, -1.5]              # One corner
  u: [3.0, 0.0, 0.0]                      # First edge
  v: [0.0, 0.0, 3.0]                      # Second edge
  color: [1.0, 0.97, 0.9]
  intensity: 35.0                          # Range: 15–60
  shadow_samples: 4                        # Samples per point (default)
  soft_radius: 0.0                         # Optional. >0 = floor distSq in cosLight/d²
  visible_to_camera: true                  # Optional. false = hide proxy from primary rays
```
- Monte Carlo soft shadows with penumbra
- `shadow_samples` overridable via CLI: `-S 32`
- Defines a physical rectangle in space
- Great for ceiling panels, windows
- Visible to camera & specular rays via an internally-managed emissive quad proxy at the same `corner`/`u`/`v` — closes Veach's MIS estimator on smooth-specular materials. Same approach as Arnold/Cycles/Renderman analytic quad lights.
- `soft_radius` (default `0`): when > 0, the attenuation denominator is clamped to `max(distSq, r²)`, preventing the `cosLight/d²` term from diverging when a stratified sample falls nearly tangent to the receiver in dense volumetric media. The returned geometric distance is unchanged. Recommended for area lights illuminating a dense participating medium (e.g. a ceiling panel in fog).
- `visible_to_camera` (default `true`): set to `false` to hide the quad proxy from primary camera rays. NEE keeps illuminating the scene at full intensity; specular reflections / refractions still see the panel; indirect bounces are unaffected. Matches Arnold's `camera` visibility flag and Cycles' "Ray Visibility → Camera".
#### **8.5 Sphere Light (Isotropic Soft Shadows)**
```yaml
- type: "sphere"  # aliases: "sphere_light", "ball", "ball_light"
  position: [0, 5, 0]
  radius: 0.5                              # Larger = softer shadows; also defines proxy size
  color: [1.0, 0.95, 0.85]
  intensity: 30.0
  shadow_samples: 4
  visible_to_camera: true                  # Optional. false = hide proxy from primary rays
```
- Solid-angle sampling (efficient, no wasted samples)
- Isotropic penumbra (circular shadows)
- Visible to camera & specular rays via an internally-managed emissive proxy primitive at the same position/radius — closes Veach's MIS estimator on smooth-specular materials (no "dark hole" highlight on glass/mirror balls). Same approach as Arnold/Cycles/Renderman analytic sphere lights.
- `soft_radius` is intentionally **not** consumed: the solid-angle estimator `L = Intensity × Ω / N` is bounded by `4π · Intensity` even when the receiver is inside the sphere, so the 1/d² floor used by point/spot/area lights is unnecessary here.
- `visible_to_camera` (default `true`): set to `false` to hide the spherical proxy from primary camera rays. NEE keeps illuminating the scene at full intensity; the sphere still appears in mirror reflections and through glass. Matches Arnold's `camera` visibility flag and Cycles' "Ray Visibility → Camera". Has no effect on `point`/`spot`/`directional` (delta) lights which carry no proxy.
#### **Light Calibration Reference:**
| Type | Range | Notes |
|------|-------|-------|
| Point (generic) | 4–30 | Scales with distance² |
| Spot (key) | 15–30 | Narrow cone = higher intensity |
| Directional (fill) | 0.05–0.15 | Secondary light with others |
| Directional (main) | 0.3–2.0 | Only light in outdoor scenes |
| Area (panel) | 20–60 | Depends on rectangle size |
| Sphere (small) | 20–50 | Radius 0.1–0.3 |
| Sphere (large) | 15–40 | Radius 0.5–1.5 |
---
### 9. **IMPORTS** — Modular Libraries
```yaml
imports:
  - path: "libraries/materials/metals.yaml"
  - path: "libraries/lights/studio-3point.yaml"
  - path: "libraries/objects/chess.yaml"
```
- **Order:** Must be first section (before templates/world)
- **Paths:** Relative to the YAML file directory
- **Cyclic protection:** Automatic cycle detection
- **Merge:** All imported materials/templates/lights available to main scene
---
### 10. **FILE STRUCTURE EXAMPLE**
Here's a complete minimal scene:
```yaml
# Simple Scene
world:
  sky:
    type: "flat"
    color: [0.3, 0.6, 1.0]
  ground:
    type: "infinite_plane"
    material: "grass"
    y: 0.0
cameras:
  - name: "main"
    position: [3, 2, -6]
    look_at: [0, 1, 0]
    fov: 45
    aperture: 0.05
    focal_dist: 7
lights:
  - type: "directional"
    direction: [-0.5, -1.0, -0.3]
    color: [1.0, 0.95, 0.85]
    intensity: 1.0
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
    refraction_index: 1.5
  - id: "gold"
    type: "metal"
    color: [0.85, 0.65, 0.2]
    fuzz: 0.1
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
- `/docs/tutorial/en/` — Complete tutorial (12 chapters):
  - `01-what-is-ray-tracing.md` — Introduction to ray tracing
  - `02-first-scene.md` — First scene and file structure
  - `03-materials.md` — All material types
  - `04-geometric-primitives.md` — All geometry types
  - `05-transforms-and-groups.md` — Transforms, groups and hierarchies
  - `06-lighting.md` — All light types
  - `07-sky-environment-camera.md` — Sky, environment and camera
  - `08-csg.md` — CSG boolean operations
  - `09-volumetrics.md` — Participating media and volumetrics
  - `10-libraries-and-projects.md` — Imports, libraries and modularity
  - `11-lathe-surface-of-revolution.md` — Lathe / surface of revolution
  - `12-extrusion-2d-profiles.md` — Linear extrusion of 2D profiles
**Source Code (Scene Parsing):**
- `/src/RayTracer/Scene/SceneLoader.cs` — YAML parsing and scene construction
- `/src/RayTracer/Materials/` — Material implementations
- `/src/RayTracer/Geometry/` — All primitive implementations
- `/src/RayTracer/Lights/` — Light source implementations
**Example Scenes:**
- `/scenes/sample.yaml` — Simple reference scene
- `/scenes/cornell-box.yaml` — Classic Cornell Box with variants
- `/scenes/pendolo-newton.yaml` — Complex scene (Newton's pendulum)
- `/scenes/showcases/` — Feature-specific demonstrations
- `/scenes/libraries/` — Reusable materials, lights, objects, templates
---
### 12. **BEST PRACTICES FOR HIGH-QUALITY SCENES**
1. **Material Strategy:**
   - Use `lambertian` for large background surfaces (no extra samples needed)
   - Use `disney` or `metal` only for protagonist objects
   - Use `mix` material for realistic weathering and wear effects
2. **Lighting Setup:**
   - Start with a directional light + gradient sky for outdoor scenes
   - Add a few point or area lights for fill/accent
   - Use sphere lights for soft, isotropic shadows
   - Override `--shadow-samples` from CLI rather than editing YAML
3. **Camera & Composition:**
   - Use `cameras: []` list for multi-shot capabilities
   - Set `focal_dist` to actual distance to main subject
   - Use aperture=0.0 for draft passes, add aperture for final renders
   - Test with low resolution + Preview profile first (`-w 400 -H 267 -s 64 -d 4 -S 1`)
4. **Performance Optimization:**
   - Use templates + instances for repeated objects
   - Import shared materials/lights from libraries
   - Batch similar geometries into groups for cleaner hierarchies
   - BVH builds automatically for complex scenes
5. **Texture Sourcing:**
   - Polyhaven.com — Free HDRIs and PBR textures (CC0)
   - AmbientCG.com — Complete PBR texture sets
   - Procedurals (noise, marble, wood) for artistic control
6. **Render Parameters:** (see [Rendering Profiles](./rendering-profiles.md) for full tables and tips)
   - Preview: `-s 64 -d 4 -S 1 -w 400`
   - Standard: `-s 256 -d 6 -w 800`
   - Final: `-s 1024 -d 8 -S 4 -w 1920`
   - Ultra: `-s 1600 -d 8 -S 4 -w 3840`
---

This comprehensive guide covers everything needed to write production-quality YAML scene files. All information is sourced directly from the project documentation, example files, and source code structure.