# Chapter 7: Sky, Environment, and Camera Effects

The sky is the largest light source in any outdoor scene. A well-
configured environment can turn a flat render into something truly
photographic. 3D-Ray's sky/environment system is on par with offline
production renderers (Arnold, Cycles, Renderman, Mitsuba): five sky
models, image-based lighting with sun extraction, portal lights for
interiors, physical aerial perspective. This chapter also covers depth
of field and multi-camera setups.

---

## 7.1 Sky Models

The sky is the global environment emitter in 3D-Ray. It determines what
colour a ray receives when it misses all objects and escapes to infinity,
and it participates in Next Event Estimation (NEE) as a light source
whenever it has non-zero radiance. Five models are supported, configured
under `world: > sky:`.

| Model            | Description                                                                  | When to use                                |
|------------------|------------------------------------------------------------------------------|--------------------------------------------|
| `flat`           | Uniform colour over the full sphere (default)                                | Studio, indoors, Cornell-box, fill-only    |
| `gradient`       | Vertical three-band gradient with optional sun disc                          | Stylised previews, quick outdoor look      |
| `preetham`       | Analytical clear-sky daylight (Preetham 1999). YAML alias: `hosek_wilkie`    | Outdoor midday, exposure-stable backgrounds |
| `nishita`        | Physical Rayleigh+Mie scattering with precomputed LUT                        | Sunrise / sunset, aerial perspective       |
| `hdri`           | Equirectangular HDR image (`.hdr` or `.exr`)                                 | Photoreal product / VFX / archviz          |

When no `sky:` block is present, the engine uses a flat sky with the
default daylight blue `[0.5, 0.7, 1.0]`. All models share a common set
of "global" features (visibility flags, background plate, orientation) —
see §7.7 below.

> The sky is the **only** ambient term. Indirect/ambient illumination
> comes from path-traced GI alone — there is no separate ambient
> coefficient. If you want a "fill light" feel, use a `flat` sky with a
> low neutral colour, or a `gradient` with a dim zenith.

> **Sun convention.** From this version onward, `sun.direction` points
> **TOWARDS the sun** (consistent across all sky models and the
> `PhysicalSun` light). Scenes authored under the old convention (where
> `direction` was the propagation direction) need their sun vectors
> inverted.

---

## 7.2 Flat Sky (default)

```yaml
world:
  sky:
    type: "flat"
    color: [0.5, 0.65, 0.9]
```

A flat sky returns its `color` for every escaping ray and participates
in NEE via uniform sphere sampling (pdf = 1/(4π)) when luminance is
positive — same approach Cycles and Arnold use for uniform world
backgrounds. Set `color: [0, 0, 0]` for fully black void scenes
(Cornell-box style); the loader automatically excludes a zero-luminance
flat sky from NEE.

---

## 7.3 Gradient Sky with Sun Disc

A vertical three-band gradient (zenith → horizon → ground) with an
optional analytical sun. The sun is auto-attached as a separate
`PhysicalSun` light with cone sampling and (optionally) Hestroffer
limb darkening — same workflow as Arnold's `aiSkyDomeLight`.

```yaml
world:
  sky:
    type: "gradient"
    zenith_color:  [0.20, 0.35, 0.75]
    horizon_color: [0.85, 0.75, 0.55]
    ground_color:  [0.30, 0.25, 0.20]
    sun:
      direction:      [0.5, 0.8, -0.3]    # direction TOWARDS the sun
      color:          [1.0, 0.95, 0.80]
      intensity:      12.0
      angular_radius: 0.265                # half-angle in degrees (real Sun)
      limb_darkening: true                 # V-band Hestroffer 1997
      shadow_samples: 4                    # stratified soft-shadow samples
```

| Sun parameter      | Default  | Description                                        |
|--------------------|----------|----------------------------------------------------|
| `direction`        | --       | Direction *TOWARDS* the sun (sky position)         |
| `color`            | `[1,1,1]`| Disc tint                                          |
| `intensity`        | `10.0`   | Brightness multiplier                              |
| `angular_radius`   | `0.265°` | Half-angle in degrees (real Sun)                   |
| `size`             | `3.0°`   | Full diameter — used only if `angular_radius` ≤ 0  |
| `limb_darkening`   | `true`   | Apply Hestroffer two-coefficient V-band model      |
| `shadow_samples`   | `4`      | Stratified samples for the paired `PhysicalSun`    |
| `visible_to_camera`| `true`   | When `false`, hides the disc from primary rays     |

### Quick presets

```yaml
# Golden hour
sun:
  direction:      [0.8, 0.15, -0.5]
  color:          [1.0, 0.78, 0.42]
  intensity:      14.0
  angular_radius: 1.5

# Noon
sun:
  direction:      [-0.1, 1.0, -0.2]
  color:          [1.0, 0.98, 0.95]
  intensity:      12.0
  angular_radius: 0.5

# Night with moon
sun:
  direction:      [-0.4, 0.6, -0.7]
  color:          [0.70, 0.75, 0.90]
  intensity:      3.0
  angular_radius: 0.8
```

---

## 7.4 Physical Sky — Hosek-Wilkie / Preetham

The analytical clear-sky daylight model used by Arnold, Cycles, and
RenderMan. A single `turbidity` knob drives the entire atmospheric look —
no manually tuned zenith / horizon / ground colours. Sun direction
controls both the disc position and the sky body's spatial distribution
(brightening near the sun, blue at the zenith).

```yaml
world:
  sky:
    type: "hosek_wilkie"             # alias of "preetham"
    turbidity: 3.0                   # 1 = pristine, 3 = clear, 5 = haze, 10 = smog
    ground_albedo: [0.25, 0.25, 0.22]
    intensity: 1.0
    sun:
      direction:       [-0.35, 0.78, 0.52]
      angular_radius:  0.265
      limb_darkening:  true
      shadow_samples:  4
```

| Parameter        | Default | Description                                                        |
|------------------|---------|--------------------------------------------------------------------|
| `turbidity`      | `3.0`   | Atmospheric clarity (1–10): low = clean, high = hazy               |
| `ground_albedo`  | `[0.3]` | Hemispheric albedo of the conceptual ground; tints the lower sky   |
| `intensity`      | `1.0`   | Multiplier on sky + sun radiance                                   |

> **Implementation note.** Both `type: hosek_wilkie` and `type: preetham`
> currently route to the same Preetham 1999 model. Hosek-Wilkie's
> advantage over Preetham at very low sun elevations is fully matched
> (and exceeded) by the `nishita` model — when in doubt for sunsets,
> switch to Nishita. See `DEVLOG.md` for the design rationale.

---

## 7.5 Nishita Physical Atmosphere

Rayleigh + Mie single-scattering integrated through an Earth-realistic
atmosphere (6360 km planet, 8 km Rayleigh scale height, 1.2 km Mie scale
height). Unlike the analytical models above, Nishita derives sunrise /
sunset chromaticity from first principles — red disc, orange halo, blue
zenith all emerge from the physics, not from fitted coefficients.

```yaml
world:
  sky:
    type: "nishita"
    turbidity: 3.0                  # remapped internally to a Mie-dust scalar
    intensity: 1.0
    sun:
      direction:       [-0.85, 0.12, 0.4]   # low on the horizon → sunset palette
      angular_radius:  0.265
      limb_darkening:  true
      shadow_samples:  4
```

**Performance.** The transmittance LUT (16×64 floats × 3 channels =
12 KB) is built once in the constructor (~20 ms). Per-direction radiance
runs a 16-step view-ray integration with two LUT lookups per sample —
about 3× a Preetham lookup. Negligible at typical render budgets.

**When to choose Nishita over Hosek-Wilkie / Preetham:**

| Scenario                                  | Better choice |
|-------------------------------------------|---------------|
| Sun above 20° elevation, midday           | Preetham (faster) |
| Sunrise, sunset, dawn, dusk               | Nishita       |
| You want a participating atmosphere too   | Nishita (pair with §7.9 medium) |
| Mountain views with depth-of-air haze     | Nishita       |
| Studio lighting / interior render         | `hdri` or `flat` |

---

## 7.6 HDRI Image-Based Lighting

```yaml
world:
  sky:
    type: "hdri"
    path: "textures/venice_sunset_2k.hdr"   # .hdr (Radiance) or .exr (OpenEXR)
    intensity: 1.0
    rotation: 45.0                          # legacy Y-axis rotation (degrees)
    sun:
      extract_from_hdri:  true              # auto-detect the sun and split it out
      extract_threshold:  50                # luminance threshold (× HDRI mean). Default 50
      shadow_samples:     4
```

| Parameter   | Default | Description                                 |
|-------------|---------|---------------------------------------------|
| `path`      | --      | Path to `.hdr` or `.exr` (resolved relative to the scene YAML) |
| `intensity` | `1.0`   | Exposure multiplier                         |
| `rotation`  | `0.0`   | Y-axis rotation in degrees (legacy; prefer `orientation:` — §7.7) |

The HDRI map wraps around the entire scene as a sphere. The engine
builds a 2D luminance-weighted CDF at load time and **importance-samples**
shadow rays toward the brightest areas. EXR support covers scanline
RGB (No compression / ZIP / ZIPS, half + float).

### Automatic mipmap prefiltering (no configuration needed)

The renderer detects when a glossy BSDF bounce escapes onto an HDRI and
automatically reads from a prefiltered mipmap level proportional to the
BSDF lobe width — eliminating the firefly spike from undersampled HDRI
peaks on rough mirrors. The pyramid is built on first use (sin(θ)-weighted
2×2 box filter for energy conservation on equirect maps); scenes that
never need it pay no extra memory.

### Sun extraction

When `extract_from_hdri: true` is set, the loader scans the HDRI for the
brightest peak, in-paints those pixels with the ring-averaged background,
and emits a paired `PhysicalSun` light with cone sampling. Benefits:

- **Crisp shadows** — instead of multi-pixel penumbras from a single
  bright HDRI pixel.
- **~10× lower NEE variance** for direct sun illumination.
- **Independent firefly clamp** — clamp the sky body aggressively without
  dimming the sun.

This is the same workflow as Arnold's `aiSkyDomeLight.aov_indirect`
"sun extraction" or Cycles' "Sun Lamp + HDRI" pairing recommendation.

### Finding the right `rotation`

The full equirect width represents 360° — sun at 25% from the left is at
90°, at 75% is at 270°. Start with `rotation: 0`, adjust in 45° steps,
then fine-tune. For full 3D orientation (pitch + roll, not just yaw) use
the `orientation:` block in §7.7 below.

---

## 7.7 Global Sky Features: Visibility, Background, Orientation

These three features apply to every sky model.

### Visibility flags (per-ray-category)

Parity with Cycles "Ray Visibility" / Arnold `aiSkyDomeLight.visibility.*`.
Each flag can be turned off to hide the sky from one category of rays:

```yaml
world:
  sky:
    type: "hdri"
    path: "studio.hdr"
    visibility:
      camera:       true     # Primary camera rays
      diffuse:      true     # Diffuse / sheen / SSS bounces
      glossy:       true     # Glossy / clearcoat bounces
      transmission: true     # Refractions through glass
      shadow:       true     # NEE shadow rays return sky radiance
    sun:
      visible_to_camera: false   # Hide the sun disc from camera (still lights the scene)
```

Common setups:

- `camera: false` — hides the HDRI from the rendered background while
  still lighting the scene (clean alpha for compositing).
- `glossy: false` — removes the HDRI from reflective materials (clay-render
  previews of metals).
- `sun.visible_to_camera: false` — off-camera key-light setup; the sun
  acts as a hard light source but doesn't blow out the sky in the frame.

### Background plate

A separate `background:` sub-block lets you light the scene with one
environment and show a different one to the camera — standard product /
VFX workflow.

```yaml
world:
  sky:
    type: "hdri"
    path: "lighting.hdr"            # Primary lighting source
    background:
      type: "hdri"
      path: "background.hdr"        # Shown to camera rays
```

The `background:` block accepts the same fields as the top-level `sky:`
block, including its own `path`, `intensity`, `rotation`, and any model
type (`flat`, `gradient`, `preetham`, etc.).

### Orientation (quaternion / Euler XYZ)

Replaces the legacy Y-only `rotation:` field with a full 3D orientation:

```yaml
world:
  sky:
    type: "hdri"
    path: "studio.hdr"
    orientation:
      euler:      [10, 45, 0]              # XYZ intrinsic Euler degrees
      # OR
      quaternion: [0, 0.38, 0, 0.92]       # XYZW; quaternion wins if both present
```

The legacy `rotation:` field is still honoured when `orientation:` is
absent.

---

## 7.8 Portal Lights — Interior Scenes Through Windows

Interior renders with windows / skylights traditionally suffer from
massive variance: NEE samples random directions on the sky CDF, but
≥95% of them hit the walls. **Portal lights** restrict NEE to the
rectangle of the window — instant ~10× variance reduction at the same
sample count.

```yaml
lights:
  - type: "portal"           # alias: "portal_light"
    anchor: [3.0, 1.2, -2.5] # one corner of the window rectangle
    u: [0.0, 0.0, 2.5]       # edge along U (window width)
    v: [0.0, 1.2, 0.0]       # edge along V (window height)
    shadow_samples: 8        # default 8
```

The portal is **intangible**: no geometry, invisible to camera and to
BSDF-sampled rays. It contributes only via NEE. Orient `u, v` so the
cross product `u × v` points TOWARDS the sky.

Algorithm: Bitterli, Wyman, Pharr (2015) "Portal-Masked Environment Map
Sampling". Same approach as Mitsuba `emitters/portal.cpp` and Arnold's
window-light workflow.

---

## 7.9 Aerial Perspective — Nishita Atmospheric Medium

The depth-of-air look offline renderers achieve via Cycles "Volume
Scatter" + sky / Arnold `atmosphere_volume` + sun. Distant geometry
acquires a bluish tint (Rayleigh scattering) and loses luminance
(extinction). The medium shares physical constants with `NishitaSky`,
so atmosphere and sky agree:

```yaml
world:
  sky:
    type: "nishita"
    sun:
      direction: [-0.45, 0.55, 0.7]
  medium:
    type: "atmosphere"           # aliases: "nishita", "aerial_perspective"
    world_scale: 1000.0          # metres per world unit (1000 = "1 wu : 1 km")
    sea_level_y: 0.0             # world Y of altitude 0
    dust_density: 1.2            # Mie density (0 = pristine, 1 = clean, >1 = polluted)
    air_density: [1, 1, 1]       # Rayleigh density multiplier per channel
    # phase defaults to Henyey-Greenstein g=0.76 (Mie forward scattering)
```

`world_scale` is the key mapping knob — choose it based on the scene's
scale (1000 for "stadium scale" scenes, 200 for "city block" scale, 50
for "single room" scale). Optical depth has a closed form (sum of two
exponentials) so there is no extra variance for the transmittance path —
free-path sampling uses delta tracking with a lower-altitude majorant.

---

## 7.10 Depth of Field

In the real world, a camera lens focuses at a specific distance. Objects
at that distance are sharp; objects closer or farther away are blurred.
This is **depth of field** (DOF).

```yaml
cameras:
  - name: "main"
    position: [0, 1.5, -5]
    look_at: [0, 0.8, 0]
    fov: 40
    aperture: 0.15
    focal_dist: 5.0
```

| Parameter    | Default | Description                                          |
|--------------|---------|------------------------------------------------------|
| `aperture`   | `0.0`   | Lens diameter (0 = pinhole, everything in focus)     |
| `focal_dist` | `1.0`   | Distance from camera at which objects are sharp      |
| `focal_pos`  | _none_  | Alternative: focus on a 3D world point (see below)   |

### Focus on a point — `focal_pos` (Arnold/Cycles "Focus Object")

Production renderers let you specify the **focal point** directly:

```yaml
cameras:
  - name: "main"
    position: [0, 1.5, -6]
    look_at: [0, 0.5, 0]
    fov: 45
    aperture: 0.12
    focal_pos: [0.0, 0.5, 0.0]    # exact world-space subject coordinate
```

The loader projects the camera→focal-point vector onto the optical axis,
so `focal_pos` defines the focus *plane*, not a Euclidean sphere — a
focal point at `(3, 4, -5)` with a camera at the origin looking along
`-Z` yields focus distance `5`, not `√50`. This matches every production
renderer. When both `focal_pos` and `focal_dist` are given, `focal_pos`
wins (an info message is logged).

### Practical guidance

- Start with a small aperture (0.05–0.1) and increase until you get the
  desired blur.
- DOF requires more samples for a clean result. Use at least 64 SPP;
  256+ is recommended for production.

---

## 7.11 Multiple Named Cameras

```yaml
cameras:
  - name: "wide"
    position: [0, 3, -8]
    look_at: [0, 1, 0]
    fov: 60

  - name: "closeup"
    position: [1, 1.5, -3]
    look_at: [0.5, 0.8, 0]
    fov: 30
    aperture: 0.1
    focal_dist: 3.5
```

Use the `cameras:` key (plural, list) instead of singular `camera:`.
Each camera must have a unique `name:`. Select from the CLI:

```
RayTracer -i scene.yaml --camera wide
RayTracer -i scene.yaml -c 1               # zero-based index
RayTracer -i scene.yaml --list-cameras     # print names + indices, no render
```

When multiple cameras exist and no `--camera` flag is provided, the
engine uses the first one and prints a warning.

---

## 7.12 Complete Example: Golden Hour Landscape

```yaml
# golden-hour.yaml — outdoor scene with Hosek-Wilkie sky, DOF, multi-camera.

world:
  sky:
    type: "hosek_wilkie"
    turbidity: 3.5
    ground_albedo: [0.3, 0.28, 0.22]
    intensity: 1.0
    sun:
      direction:      [0.8, 0.15, -0.5]    # low warm sun, behind-right
      angular_radius: 1.5                   # slightly enlarged for cinematic glow
      limb_darkening: true
      shadow_samples: 4

cameras:
  - name: "landscape"
    position: [0, 1.5, -8]
    look_at: [0, 0.8, 0]
    fov: 55

  - name: "macro"
    position: [1, 0.8, -3]
    look_at: [0.5, 0.6, 0]
    fov: 30
    aperture: 0.15
    focal_pos: [0.0, 0.5, 0.0]

materials:
  - id: "ground"
    type: "disney"
    color: [0.35, 0.28, 0.18]
    roughness: 0.85

  - id: "gold_sphere"
    type: "disney"
    color: [1.0, 0.76, 0.33]
    metallic: 1.0
    roughness: 0.05

  - id: "glass_sphere"
    type: "disney"
    color: [0.95, 0.95, 0.95]
    spec_trans: 1.0
    ior: 1.52
    roughness: 0.02

entities:
  - type: "infinite_plane"
    point: [0, 0, 0]
    normal: [0, 1, 0]
    material: "ground"

  - type: "sphere"
    center: [0, 0.5, 0]
    radius: 0.5
    material: "gold_sphere"

  - type: "sphere"
    center: [1.2, 0.35, -0.5]
    radius: 0.35
    material: "glass_sphere"
```

```
RayTracer -i golden-hour.yaml -c landscape -w 1920 -H 800 -s 256 -d 6
RayTracer -i golden-hour.yaml -c macro -w 1200 -H 800 -s 1024 -d 8 -S 4
```

---

## What You Have Learned

- **Five sky models** cover every production scenario: `flat` (uniform),
  `gradient` (stylised), `preetham`/`hosek_wilkie` (analytical clear-sky
  daylight), `nishita` (physical Rayleigh+Mie for sunsets and aerial
  perspective), `hdri` (image-based).
- The **sun is decoupled** as a `PhysicalSun` light with cone sampling
  and Hestroffer limb darkening. Sun extraction from HDRIs gives clean
  shadows and lower variance.
- **Visibility flags** (camera/diffuse/glossy/transmission/shadow),
  separate **background plate**, and full 3D **orientation** apply to
  every sky model.
- **Portal lights** restrict NEE to window rectangles for ~10× variance
  reduction on interior scenes.
- **Nishita atmospheric medium** adds physical aerial perspective using
  the same constants as the sky model.
- HDRI **mipmap prefiltering** is automatic on glossy bounces — no
  configuration needed; eliminates firefly spikes.
- **Depth of field** is controlled by `aperture` (lens size) and
  `focal_dist` (or `focal_pos: [x, y, z]` for Arnold/Cycles "Focus Object"
  workflow).
- **Multiple cameras** in one scene file, selectable via `--camera name`
  on the CLI.

For a one-page YAML reference plus ready-to-copy sky presets see
[`scenes/00-sky-presets.md`](../../../scenes/00-sky-presets.md).

---

[Previous: Lighting Mastery](./06-lighting.md) | [Next: Constructive Solid Geometry (CSG)](./08-csg.md) | [Tutorial Index](./README.md)
