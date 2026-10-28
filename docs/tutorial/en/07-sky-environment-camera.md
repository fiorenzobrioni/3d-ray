# Chapter 7: Sky, Environment, and Camera Effects

The sky is the largest light source in any outdoor scene. A well-
configured environment can turn a flat render into something truly
photographic. 3D-Ray's sky/environment system features five sky
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
| `flat`           | Uniform colour over the full sphere                                          | Studio, indoors, Cornell-box, fill-only    |
| `gradient`       | Vertical three-band gradient with optional sun disc                          | Stylised previews, quick outdoor look      |
| `hosek_wilkie`   | Analytical clear-sky daylight (Hosek-Wilkie 2012)                            | Outdoor daylight, the closest to measured skies |
| `preetham`       | Analytical clear-sky daylight (Preetham 1999), the earlier model             | Baseline, comparisons                      |
| `nishita`        | Physical Rayleigh+Mie scattering with precomputed LUT                        | Sunrise / sunset, aerial perspective       |
| `hdri`           | Equirectangular HDR image (`.hdr` or `.exr`)                                 | Photoreal product / VFX / archviz          |

When no `sky:` block is present, the environment is black. All models
share a common set of "global" features (`exposure` in stops, visibility
flags, background plate, rotation): see §7.7 below.

> The sky is the **only** ambient term. Indirect/ambient illumination
> comes from path-traced GI alone — there is no separate ambient
> coefficient. If you want a "fill light" feel, use a `flat` sky with a
> low neutral colour, or a `gradient` with a dim zenith.

> **Sun convention.** `sun.direction` points **TOWARDS the sun**
> (consistent across all sky models and the `PhysicalSun` light), while a
> `directional` light's `direction` is the direction the light travels.

---

## 7.2 Flat Sky

```yaml
world:
  sky:
    type: "flat"
    color: [0.5, 0.65, 0.9]
```

A flat sky returns its `color` (default 1) for every escaping ray and participates
in NEE via uniform sphere sampling (pdf = 1/(4π)) when luminance is
positive, providing uniform ambient illumination from all directions.
Set `color: [0, 0, 0]` for fully black void scenes
(Cornell-box style); the loader automatically excludes a zero-luminance
flat sky from NEE.

---

## 7.3 Gradient Sky with Sun Disc

A vertical three-band gradient (zenith → horizon → ground) with an
optional analytical sun. The sun is auto-attached as a separate
`PhysicalSun` light with cone sampling and (optionally) Hestroffer
limb darkening.

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
      luminance:      11.4                 # luminance of the disc
      angular_radius: 0.265                # half-angle in degrees (real Sun)
      shadow_samples: 4                    # stratified soft-shadow samples
```

| Sun parameter      | Default  | Description                                        |
|--------------------|----------|----------------------------------------------------|
| `direction`        | --       | Direction *TOWARDS* the sun (sky position)         |
| `color`            | `[1,1,1]`| Disc tint (scaled to luminance 1)                  |
| `luminance`        | `10`     | Luminance of the disc, on the scale of the sky colours |
| `angular_radius`   | `1.5°`   | Half-angle in degrees (the real Sun 0.265)         |
| `shadow_samples`   | `4`      | Stratified samples for the paired `PhysicalSun`    |
| `visibility`       | --       | `{ camera: false }` hides the disc from primary rays |

The light the disc gives a surface facing it is `luminance × π sin²θ`
(θ the angular radius): at the same luminance a larger disc gives more
light and softer shadows.

### Quick presets

```yaml
# Golden hour
sun:
  direction:      [0.8, 0.15, -0.5]
  color:          [1.0, 0.78, 0.42]
  luminance:      9.8
  angular_radius: 1.5

# Noon
sun:
  direction:      [-0.1, 1.0, -0.2]
  color:          [1.0, 0.98, 0.95]
  luminance:      11.8
  angular_radius: 0.5

# Night with moon
sun:
  direction:      [-0.4, 0.6, -0.7]
  color:          [0.70, 0.75, 0.90]
  luminance:      2.3
  angular_radius: 0.8
```

---

## 7.4 Physical Sky - Hosek-Wilkie / Preetham

Analytical clear-sky daylight. A single `turbidity` knob drives the
entire atmospheric look - no manually tuned zenith / horizon / ground
colours. Sun direction controls both the disc position and the sky
body's spatial distribution (brightening near the sun, blue at the
zenith).

```yaml
world:
  sky:
    type: "hosek_wilkie"             # or "preetham"
    turbidity: 3.0                   # 1 = pure air, 3 = clear, 6 = hazy, 10 = very hazy
    ground_albedo: [0.25, 0.25, 0.22]
    exposure: -9.97                  # stops: about a scene without a camera exposure
    sun:
      direction:       [-0.35, 0.78, 0.52]
      shadow_samples:  4
```

| Parameter        | Default      | Description                                                        |
|------------------|--------------|--------------------------------------------------------------------|
| `turbidity`      | `3.0`        | Linke turbidity (1-10): low = clean, high = hazy                   |
| `ground_albedo`  | `[0.3]`      | Albedo of the ground below the horizon; also tints the Hosek-Wilkie sky |
| `exposure`       | `0`          | Stops: the sky and its sun × 2^exposure                            |

**Two models.** `hosek_wilkie` (Hosek and Wilkie 2012) is fitted to
measured skies and holds down to low suns; `preetham` (1999) is the
earlier model, brighter for the same turbidity and with a pinkish tint
near the horizon, kept as a baseline.

**The sun matches the sky.** The disc is the sunlight at the top of the
atmosphere (133.8 klux), attenuated along its path through an atmosphere
with the dust of the same turbidity, spread over the disc. On a clear
day the sun gives the ground about as much light as the sky with the
sun at 15°, three to four times as much at 45°. Shadows are as deep as
in a photograph of a sunny day.

**Exposing a daylight scene.** The physical skies are in nits (a clear
noon sky is thousands of nits, the sun's disc about 1.5×10⁹). Either
dim them with the sky's `exposure` (about -10 stops brings them to the
values of a scene without a camera exposure, as in the example above), or
leave them alone and give the camera a photographic exposure,
`ev100: 15` for a sunny day:

```yaml
cameras:
  - position: [0, 1.6, -7]
    look_at:  [0, 0.6, 0]
    ev100: 15                          # the "sunny 16" rule
```

At load the console reports what the sky gives a horizontal plane
(`Sky light: 75190 lux on the ground, 58593 of it from the sun`). See
`scenes/review/daylight-units.yaml`.

**Below the horizon** the sky shows a ground of `ground_albedo` lit by
that sky and sun; a scene with its own ground plane hides it.

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
    turbidity: 3.0                  # haze knob: a light dust load (clean air at 3)
    exposure: -12.7
    sun:
      direction:       [-0.85, 0.12, 0.4]   # low on the horizon → sunset palette
      shadow_samples:  4
```

**Sun and units.** The sun's illuminance at the top of the atmosphere,
133 800 lux, feeds both the scattering and the disc (`exposure: -12.7`
brings it to about 20, a scene without a camera exposure). The model
scatters light once: its sky is
darker against the sun than a measured one (about half), and for that
reason `turbidity` maps to a light dust load here (with the aerosol of a
real turbidity 3 the horizon would turn dark).

**Performance.** The transmittance LUT (16×64 floats × 3 channels =
12 KB) is built once in the constructor (~20 ms). Per-direction radiance
runs a 16-step view-ray integration with two LUT lookups per sample —
about 3× a Preetham lookup. Negligible at typical render budgets.

**When to choose Nishita over Hosek-Wilkie / Preetham:**

| Scenario                                  | Better choice |
|-------------------------------------------|---------------|
| Sun above 15° elevation, daylight         | Hosek-Wilkie (closest to measured skies) |
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
    rotate: [0, -90, 0]                     # turn the environment about Y (degrees)
    sun:
      extract_from_hdri:  true              # auto-detect the sun and split it out
      extract_threshold:  50                # luminance threshold (× HDRI mean). Default 50
      shadow_samples:     4
```

| Parameter   | Default | Description                                 |
|-------------|---------|---------------------------------------------|
| `path`      | --      | Path to `.hdr` or `.exr` (resolved relative to the scene YAML) |
| `exposure`  | `0`     | Stops: the image × 2^exposure               |
| `rotate`    | `[0, 0, 0]` | Euler degrees, as an entity's `rotate` (§7.7) |

The HDRI map wraps around the entire scene as a sphere. The engine
builds a 2D luminance-weighted CDF at load time and **importance-samples**
shadow rays toward the brightest areas. EXR support covers scanline
RGB (No compression / ZIP / ZIPS, half + float).

### Sampling the HDRI

The HDRI is a light: next-event estimation samples it in proportion to its
brightness and the BSDF samples share the work through multiple importance
sampling. The light side samples the map minus its mean (Karlík et al.
2019), so the even part of the sky is left to the BSDF, which finds it
well, and the light samples go to the bright spots: less noise on rough
mirrors and in the shade.

A glossy bounce that escapes onto an HDRI the lights do not sample (the
sky hidden from shadow rays, for example) reads a prefiltered mipmap level
proportional to the BSDF lobe width instead. It is not used for an HDRI
that is a light: under MIS the prefiltered value is not the radiance the
light side estimates, and the image came out too bright.

### Sun extraction

When `extract_from_hdri: true` is set, the loader scans the HDRI for the
brightest peak, in-paints those pixels with the ring-averaged background,
and emits a paired `PhysicalSun` light with cone sampling. Benefits:

- **Crisp shadows** — instead of multi-pixel penumbras from a single
  bright HDRI pixel.
- **~10× lower NEE variance** for direct sun illumination.
- **Independent firefly clamp** — clamp the sky body aggressively without
  dimming the sun.

This is the standard sun-extraction workflow for image-based lighting.

### Finding the right rotation

The full equirect width represents 360°: sun at 25% from the left is at
90°, at 75% is at 270°. Start with `rotate: [0, 0, 0]`, adjust the Y
angle in 45° steps, then fine-tune. Tilt the horizon with the X and Z
angles (§7.7 below).

---

## 7.7 Global Sky Features: Visibility, Background, Rotation

These features apply to every sky model, as does `exposure` (stops).

### Visibility flags (per-ray-category)

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
      visibility: { camera: false }   # Hide the sun disc from camera (still lights the scene)
```

Common setups:

- `camera: false` — hides the HDRI from the rendered background while
  still lighting the scene (clean alpha for compositing).
- `glossy: false` — removes the HDRI from reflective materials (clay-render
  previews of metals).
- `sun: { visibility: { camera: false } }`: off-camera key-light setup; the sun
  acts as a hard light source but doesn't blow out the sky in the frame.

**Ground parity.** The exact same `visibility.*` flags exist on
`world.ground:` — see the
[ground reference](../../reference/scene-reference.md). A
`visibility.shadow: false` ground keeps the floor visible to camera while
letting NEE shadow rays pass through it (clean "infinite floor without
contact shadow" lookdev), and `visibility.camera: false` produces an
invisible floor that still bounces indirect light.

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
block, including its own `path`, `exposure`, `rotate`, and any model
type (`flat`, `gradient`, `preetham`, etc.).

### Rotation (`rotate`)

The sky turns in the world as an entity would with the same `rotate`
(Euler degrees, X first unless `rotate_order` says otherwise):

```yaml
world:
  sky:
    type: "hdri"
    path: "studio.hdr"
    rotate: [10, 45, 0]       # tilt 10° about X, then turn 45° about Y
    # rotate_order: xyz        # default
```

A feature of the sky at +X turned by `[0, 90, 0]` ends at −Z.

---

## 7.8 Portal Lights — Interior Scenes Through Windows

Interior renders with windows / skylights traditionally suffer from
massive variance: NEE samples random directions on the sky CDF, but
≥95% of them hit the walls. **Portal lights** restrict NEE to the
rectangle of the window — instant ~10× variance reduction at the same
sample count.

```yaml
lights:
  - type: "portal"
    corner: [3.0, 1.2, -2.5] # one corner of the window rectangle
    u: [0.0, 0.0, 2.5]       # edge along U (window width)
    v: [0.0, 1.2, 0.0]       # edge along V (window height)
    shadow_samples: 8        # default 8
```

The portal is **intangible**: no geometry, invisible to camera and to
BSDF-sampled rays. It contributes only via NEE. Orient `u, v` so the
cross product `u × v` points TOWARDS the sky.

Algorithm: Bitterli, Wyman, Pharr (2015) "Portal-Masked Environment Map
Sampling".

---

## 7.9 Aerial Perspective — Nishita Atmospheric Medium

Physical aerial perspective: distant geometry
acquires a bluish tint (Rayleigh scattering) and loses luminance
(extinction). The medium shares physical constants with `NishitaSky`,
so atmosphere and sky agree:

```yaml
units: { length: 1000 }
world:
  sky:
    type: "nishita"
    sun:
      direction: [-0.45, 0.55, 0.7]
    exposure: -12.7
  medium:
    type: "atmosphere"
    y0: 0.0                      # world Y of altitude 0 (sea level)
    dust_density: 1.2            # Mie density (0 = pristine, 1 = clean, >1 = polluted)
    air_density: 1.0             # air (Rayleigh) density, 1 = standard atmosphere
    # air scatters with the Rayleigh phase function; `phase` sets the dust's
    # (default Henyey-Greenstein g = 0.76, forward)
```

The scene's `units: { length }` is the key mapping knob: it says how many
metres one scene unit is, so the atmosphere has its real thickness. A
landscape modelled in kilometres uses `length: 1000` (or `km`) as above;
choose it from the scale the scene was modelled at. Optical depth has a closed form for air, dust
and ozone, so transmittance is exact and distances are sampled exactly.
The medium is aerial perspective: it acts between the eye and the surfaces
it sees, while rays that run to the sky cross it untouched (the Nishita sky
already holds that light; counting it twice washed the sky out).

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
    focus_distance: 5.0
```

| Parameter    | Default | Description                                          |
|--------------|---------|------------------------------------------------------|
| `aperture`   | `0.0`   | Lens diameter (0 = pinhole, everything in focus)     |
| `focus_distance` | `1.0` | Distance from camera at which objects are sharp    |
| `focus_point`  | _none_ | Alternative: focus on a 3D world point (see below)  |

### Focus on a point: `focus_point`

You can specify the **focal point** directly instead of a distance:

```yaml
cameras:
  - name: "main"
    position: [0, 1.5, -6]
    look_at: [0, 0.5, 0]
    fov: 45
    aperture: 0.12
    focus_point: [0.0, 0.5, 0.0]    # exact world-space subject coordinate
```

The loader projects the camera→focal-point vector onto the optical axis,
so `focus_point` defines the focus *plane*, not a Euclidean sphere: a
focal point at `(3, 4, -5)` with a camera at the origin looking along
`-Z` yields focus distance `5`, not `√50`. This matches every production
renderer. When both `focus_point` and `focus_distance` are given,
`focus_point` wins (an info message is logged).

### Practical guidance

- Start with a small aperture (0.05–0.1) and increase until you get the
  desired blur.
- DOF requires more samples for a clean result. Use at least 64 SPP;
  256+ is recommended for production.

### A real lens: focal length and f-stop

Instead of `fov` and `aperture` you can describe the lens as a
photographer does. With the scene modelled in metres:

```yaml
cameras:
  - name: "portrait"
    position: [0, 1.2, 0]
    look_at: [0, 0.9, -2.5]
    focal_length: 50        # mm; sensor_width defaults to 36 (full frame)
    f_stop: 1.4             # pupil = 50 / 1.4 = 35.7 mm
    focus_point: [0, 0.9, -2.5]
```

`focal_length` and `sensor_width` set the field of view (the sensor width
spans the image width), `f_stop` the pupil diameter. A scene modelled in
centimetres says so with `units: { length: cm }`, so that f/1.4 still
means a 35.7 mm pupil. Halving the f-number doubles the pupil and the blur; a
longer lens at the same f-number blurs the background more, as on a real
camera.

### Bokeh shape

Out-of-focus highlights take the shape of the aperture. `aperture_blades: 6`
gives hexagons (5 to 9 blades are typical), `aperture_rotation` turns
the polygon, and `aperture_ratio: 0.5` gives the tall ovals of an
anamorphic lens. Small, very bright highlights need many samples: each
pixel of the bokeh disc only receives light from the lens samples that
hit the source. `scenes/showcases/camera-physical.yaml` shows all of them.

### White balance and the view transform

`white_balance: 3200` (kelvin) makes a 3200 K tungsten light render
white, as a camera set to "tungsten" does: lower values cool the image,
higher values warm it, `6504` changes nothing. The display image is then
made by the view transform chosen on the command line: `--view agx` (the
default) rolls bright saturated light off towards white as film does,
`--view aces` gives the ACES 1.x look with more contrast, and
`--view standard` shows the linear values with only the sRGB curve.
`--exposure` (in stops) is applied before the view transform.

---

## 7.11 Motion Blur

A real camera's shutter stays open for a finite time, so anything that moves
during the exposure leaves a streak. 3D-Ray reproduces this physically: the
scene animates on a normalized `[0, 1]` timeline, and the camera's
`shutter_interval: [open, close]` is the slice of that timeline the exposure integrates.

Animate an entity by giving it a `motion:` list. Its base
`translate`/`rotate`/`scale` is the pose at `time: 0`; each keyframe adds a pose
at its own time (omitted components inherit the base pose):

```yaml
format_version: 2
cameras:
  - position: [0, 2, 8]
    look_at: [0, 1, 0]
    shutter_interval: [0.0, 1.0]          # full exposure: integrate the whole motion

entities:
  # A sphere streaking to the right.
  - type: "sphere"
    center: [0, 0, 0]
    radius: 0.6
    material: "chrome"
    translate: [-2, 0.6, 0]    # pose at time 0
    motion:
      - { time: 1.0, translate: [2, 0.6, 0] }   # pose at time 1
```

The faster an object travels between its keyframes, the longer its streak. A
**narrower shutter is a shorter exposure**: `shutter_interval: [0.45, 0.55]` exposes only
the middle tenth of the motion, so the streaks shrink ~10× — handy for an A/B
"frozen vs blurred" comparison from two cameras over the same animation.

Rotation blurs too, on the shortest arc between keyframes:

```yaml
  - type: "box"
    material: "checker"
    scale: [1.4, 1.4, 1.4]
    translate: [3, 0.7, 0]
    motion:
      - { time: 1.0, translate: [3, 0.7, 0], rotate: [0, 150, 0] }
```

You can animate the **camera** the same way — a `motion:` list of
`position`/`look_at`/`up`/`fov` keyframes gives a moving-camera blur (a dolly or
whip pan):

```yaml
cameras:
  - position: [0, 2, 8]
    look_at: [0, 1, 0]
    shutter_interval: [0.0, 1.0]
    motion:
      - { time: 1.0, position: [1.5, 2, 8] }   # gentle dolly during the exposure
```

Notes:
- Motion blur needs more samples than a still frame for a clean streak — treat it
  like DOF and use 256+ SPP for production.
- `motion:` is supported on top-level entities of any type (it is ignored on
  group/instance *children* — animate the parent instead).
- When nothing is animated the shutter is ignored and the render is identical to
  a still frame.

See the `motion-blur-showcase.yaml` and `motion-blur-billiard-showcase.yaml`
scenes, and `docs/technical/motion-blur.md` for the algorithm.

---

## 7.12 Multiple Named Cameras

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
    focus_distance: 3.5
```

`cameras:` is a list; give each camera a unique `name:`. Select from the CLI:

```
RayTracer -i scene.yaml --camera wide
RayTracer -i scene.yaml -c 1               # zero-based index
RayTracer -i scene.yaml --list-cameras     # print names + indices, no render
```

When multiple cameras exist and no `--camera` flag is provided, the
engine uses the first one and prints a warning.

---

## 7.13 Complete Example: Golden Hour Landscape

```yaml
# golden-hour.yaml — outdoor scene with Hosek-Wilkie sky, DOF, multi-camera.

format_version: 2
world:
  sky:
    type: "hosek_wilkie"
    turbidity: 3.5
    ground_albedo: [0.3, 0.28, 0.22]
    exposure: -9.97
    sun:
      direction:      [0.8, 0.15, -0.5]    # low warm sun, behind-right
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
    focus_point: [0.0, 0.5, 0.0]

materials:
  - id: "ground"
    type: openpbr
    base_color: [0.35, 0.28, 0.18]
    specular_roughness: 0.85

  - id: "gold_sphere"
    type: openpbr
    base_color: [1.0, 0.76, 0.33]
    base_metalness: 1.0
    specular_roughness: 0.05

  - id: "glass_sphere"
    type: openpbr
    base_color: [0.95, 0.95, 0.95]
    transmission_weight: 1.0
    specular_ior: 1.52
    specular_roughness: 0.02
    transmission_color: [0.95, 0.95, 0.95]

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
  separate **background plate**, `exposure` and a full 3D `rotate` apply
  to every sky model.
- **Portal lights** restrict NEE to window rectangles for ~10× variance
  reduction on interior scenes.
- **Nishita atmospheric medium** adds physical aerial perspective using
  the same constants as the sky model.
- **HDRIs** are importance-sampled with the MIS compensation of Karlík
  et al. 2019: light samples go to the bright spots, the BSDF covers the
  even sky.
- **Physical skies** have a sun consistent with the sky and are in nits:
  exposed with the camera's `ev100`, or dimmed with their `exposure`.
- **Depth of field** is controlled by `aperture` (lens size) and
  `focus_distance` (or `focus_point: [x, y, z]` to focus on a specific world point).
- **Multiple cameras** in one scene file, selectable via `--camera name`
  on the CLI.

For ready-to-copy presets see:
- [`scenes/presets/sky.md`](../../../scenes/presets/sky.md): sky-only presets.
- [`scenes/presets/world.md`](../../../scenes/presets/world.md): paired sky + ground presets (full `world:` block including ground shape, material, UV and visibility flags).

---

[Previous: Lighting Mastery](./06-lighting.md) | [Next: Constructive Solid Geometry (CSG)](./08-csg.md) | [Tutorial Index](./README.md)
