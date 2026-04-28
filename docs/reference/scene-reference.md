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
world:      # Environment (sky, ambient light, background, ground)
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
  ambient_light: [0.05, 0.05, 0.08]      # Omnidirectional fill light
  background: [0.5, 0.7, 1.0]            # Sky color (if no sky object)
  ground:                                  # (optional) Auto-generated floor
    type: "infinite_plane"
    material: "floor_name"
    y: 0.0
  sky:                                     # (optional) Replaces background
    type: "gradient"  # or "hdri"
    # ... see details below
  medium:                                  # (optional) Global participating medium
    type: "homogeneous"
    # ... see details below
```
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
#### **HDRI/IBL** (for maximum realism):
```yaml
sky:
  type: "hdri"
  path: "hdri/studio.hdr"                 # Path relative to YAML file
  intensity: 1.0                           # Exposure multiplier
  rotation: 90                             # Y-axis rotation in degrees
```
**Preset Sky Configurations:**
- **Noon** (clean sky, bright sun)
- **Golden Hour** (low warm sun, saturated horizon)
- **Sunset** (dramatic orange horizon)
- **Night** (minimal zenith/horizon values, dim sun disk)
- **Overcast** (high ambient, uniform sky)

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
```
#### **Single Camera** (legacy):
```yaml
camera:
  position: [0, 2, -8]                    # Camera location
  look_at: [0, 0, 0]                      # Target point
  vup: [0, 1, 0]                          # "Up" vector (for roll)
  fov: 60                                  # Vertical field of view (degrees)
  aperture: 0.1                            # Lens diameter (0 = pinhole)
  focal_dist: 8.0                          # Distance to focus plane
```
**Usage from CLI:**
```bash
dotnet run ... -- -i scene.yaml --list-cameras      # List available
dotnet run ... -- -i scene.yaml -c top -o top.png   # By name
dotnet run ... -- -i scene.yaml -c 1 -o cam1.png    # By index (0-based)
```
**⚠️ Depth of Field:** When `aperture > 0`, set `focal_dist` to the actual distance from camera to your main subject (measure in world units). Default `focal_dist: 1.0` will create unintended extreme blur.
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
| `normal_map` | block | — | — | Texturing | Surface perturbation |

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
Textures are defined **within** material definitions:
#### **Procedural Textures:**
**Checker:**
```yaml
texture:
  type: "checker"
  scale: 4.0
  colors: [[0.9, 0.9, 0.9], [0.1, 0.1, 0.1]]
```
**Noise (Perlin):**
```yaml
texture:
  type: "noise"
  scale: 5.0
  noise_strength: 3.0                     # 0=smooth, >0=turbulent
  colors: [[0.0, 0.0, 0.0], [1.0, 1.0, 1.0]]  # optional: default is black→white
```
**Marble:**
```yaml
texture:
  type: "marble"
  scale: 10.0
  noise_strength: 8.0
  colors: [[0.95, 0.95, 0.95], [0.4, 0.4, 0.4]]
```
**Wood:**
```yaml
texture:
  type: "wood"
  scale: 3.0
  noise_strength: 2.0
  colors: [[0.85, 0.65, 0.4], [0.6, 0.4, 0.2]]
```
**All procedurals support:**
```yaml
offset: [5.0, 0.0, 3.0]                  # Translation
rotation: [0.0, 45.0, 0.0]               # Rotation (degrees)
randomize_offset: true                    # Per-object variation
randomize_rotation: true
```
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
- **Valid CSG child types.** Each child must be a solid primitive with well-defined interior/exterior. Supported: `sphere`, `box`, `cylinder`, `cone`, `torus`, `capsule`, `quad`, `disk`, `annulus`, `triangle`, `lathe` (and aliases `revolution` / `surface_of_revolution`), or a nested `csg`. **Not supported and skipped with a warning** (loader emits `CSG entity '…': failed to create one or both children. Skipping.` and the node is dropped): `group`, `mesh` / `obj`, `instance`, `plane` / `infinite_plane`. To union two primitives as a CSG operand, use an explicit `csg: union` rather than wrapping them in a `group`.
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
- `angular_radius` (default `0`): when > 0 the light models a disc of finite angular size. Each shadow ray is perturbed uniformly within the subtended cone, producing a soft penumbra. The real Sun is approximately 0.27°. When active, `shadow_samples` defaults to 16 and `IsDelta` becomes `false`, enabling full MIS weighting. Hard-shadow backward compatibility is preserved at the default 0.
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
  shadow_samples: 16                       # Samples per point
  soft_radius: 0.0                         # Optional. >0 = floor distSq in cosLight/d²
```
- Monte Carlo soft shadows with penumbra
- `shadow_samples` overridable via CLI: `-S 32`
- Defines a physical rectangle in space
- Great for ceiling panels, windows
- Visible to camera & specular rays via an internally-managed emissive quad proxy at the same `corner`/`u`/`v` — closes Veach's MIS estimator on smooth-specular materials. Same approach as Arnold/Cycles/Renderman analytic quad lights.
- `soft_radius` (default `0`): when > 0, the attenuation denominator is clamped to `max(distSq, r²)`, preventing the `cosLight/d²` term from diverging when a stratified sample falls nearly tangent to the receiver in dense volumetric media. The returned geometric distance is unchanged. Recommended for area lights illuminating a dense participating medium (e.g. a ceiling panel in fog).
#### **8.5 Sphere Light (Isotropic Soft Shadows)**
```yaml
- type: "sphere"  # aliases: "sphere_light", "ball", "ball_light"
  position: [0, 5, 0]
  radius: 0.5                              # Larger = softer shadows; also defines proxy size
  color: [1.0, 0.95, 0.85]
  intensity: 30.0
  shadow_samples: 16
```
- Solid-angle sampling (efficient, no wasted samples)
- Isotropic penumbra (circular shadows)
- Visible to camera & specular rays via an internally-managed emissive proxy primitive at the same position/radius — closes Veach's MIS estimator on smooth-specular materials (no "dark hole" highlight on glass/mirror balls). Same approach as Arnold/Cycles/Renderman analytic sphere lights.
- `soft_radius` is intentionally **not** consumed: the solid-angle estimator `L = Intensity × Ω / N` is bounded by `4π · Intensity` even when the receiver is inside the sphere, so the 1/d² floor used by point/spot/area lights is unnecessary here.
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
  ambient_light: [0.05, 0.05, 0.08]
  background: [0.3, 0.6, 1.0]
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
- `/docs/tutorial/02-tutorial-scene/` — Complete tutorial sections:
  - `01-struttura-file.md` — File structure overview
  - `02-world.md` — World/environment config
  - `03-camera.md` — Camera setup
  - `04-materials.md` — Material types
  - `05-textures.md` — Texture details
  - `06-entities.md` — All geometry types
  - `07-lights.md` — All light types
  - `11-groups-and-imports.md` — Hierarchies and modularity
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