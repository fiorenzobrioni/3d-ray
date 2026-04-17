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
Every scene YAML file has **5 main sections** (in this order):
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
    direction:  [-0.5, -1.0, -0.3]       # Where sunlight comes FROM
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
  metallic: 0.0                            # 0=dielectric, 1=metal
  roughness: 0.3                           # 0=mirror, 1=diffuse
  subsurface: 0.0                          # SSS (wax, skin)
  specular: 0.5                            # Dielektric specular intensity
  specular_tint: 0.0                       # Tint specular toward color
  sheen: 0.0                                # Grazing luster (fabric)
  sheen_tint: 0.5
  clearcoat: 1.0                           # Second specular lobe
  clearcoat_gloss: 0.9                     # Clearcoat roughness
  spec_trans: 0.0                          # 0=opaque, 1=glass
  ior: 1.5                                  # Refraction index
  texture: (optional)
  normal_map: (optional)
```
- **When to use:**
  - Metals: `metallic=1.0`, varied roughness
  - Plastics: `metallic=0.0`, `roughness=0.4–0.8`
  - Car paint: `metallic=0.0`, `clearcoat=1.0`
  - Fabric: `metallic=0.0`, `sheen=0.5–1.0`
  - Skin: `metallic=0.0`, `subsurface=0.3–0.5`
  - Glass: `metallic=0.0`, `spec_trans=1.0`, `roughness=0.0`
- **⚠️ Noise:** Disney requires ~4× samples vs classic materials (less lobes = less variance)
- **💡 Best practice:** Use lambertian for big surfaces, Disney only for protagonist objects
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
- name: "lens"
  type: "csg"
  operation: "intersection"                # "union", "intersection", "subtraction"
  operand_a:
    type: "sphere"
    center: [-0.5, 0, 0]
    radius: 1.0
  operand_b:
    type: "sphere"
    center: [0.5, 0, 0]
    radius: 1.0
  material: "glass"
```
- Supports recursively nested CSG trees
- Operations: `union` (A∪B), `intersection` (A∩B), `subtraction` (A\B)
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
---
### 8. **LIGHTS SECTION** — Five Types
#### **8.1 Point Light (Omnidirectional)**
```yaml
- type: "point"
  position: [2, 5, -3]
  color: [1.0, 0.95, 0.85]
  intensity: 20.0                          # Range: 4–30
```
- Quadratic falloff with distance
- Simple but effective for interior lighting
#### **8.2 Directional Light (Sun)**
```yaml
- type: "directional"  # alias: "sun"
  direction: [-0.5, -1.0, -0.3]           # Where light comes FROM
  color: [1.0, 0.98, 0.92]
  intensity: 0.8                           # Range: 0.05–2.0
```
- No distance attenuation
- Align with gradient sky `sun.direction` for visual coherence
- Good for outdoor key light
#### **8.3 Spot Light (Cone)**
```yaml
- type: "spot"  # alias: "spotlight"
  position: [0, 5, 0]
  direction: [0, -1, 0]                   # Where spotlight points
  color: [1.0, 0.9, 0.7]
  intensity: 40.0
  inner_angle: 15                         # Degrees (full brightness)
  outer_angle: 30                         # Degrees (fade zone)
```
- Quadratic falloff
- Smooth falloff between inner/outer cones
- Good for dramatic lighting, accent lights
#### **8.4 Area Light (Soft Shadows)**
```yaml
- type: "area"  # aliases: "area_light", "rect", "rect_light"
  corner: [-1.5, 4.99, -1.5]              # One corner
  u: [3.0, 0.0, 0.0]                      # First edge
  v: [0.0, 0.0, 3.0]                      # Second edge
  color: [1.0, 0.97, 0.9]
  intensity: 35.0                          # Range: 15–60
  shadow_samples: 16                       # Samples per point
```
- Monte Carlo soft shadows with penumbra
- `shadow_samples` overridable via CLI: `-S 32`
- Defines a physical rectangle in space
- Great for ceiling panels, windows
#### **8.5 Sphere Light (Isotropic Soft Shadows)**
```yaml
- type: "sphere"  # aliases: "sphere_light", "ball", "ball_light"
  position: [0, 5, 0]
  radius: 0.5                              # Larger = softer shadows
  color: [1.0, 0.95, 0.85]
  intensity: 30.0
  shadow_samples: 16
```
- Solid-angle sampling (efficient, no wasted samples)
- Isotropic penumbra (circular shadows)
- Better than emissive sphere for sampling efficiency
- Small sphere + emissive sphere co-located = best of both worlds
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