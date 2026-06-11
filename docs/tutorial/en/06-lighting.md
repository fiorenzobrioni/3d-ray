# Chapter 6: Lighting Mastery

Lighting defines the mood, depth, and realism of a scene. 3D-Ray supports
six explicit light types plus automatic geometry lights extracted from
emissive surfaces. This chapter covers every one of them and shows you
how to combine them into professional setups.

---

## 6.1 Overview

| Type          | Aliases                                      | Shadows  | Key use                   |
|---------------|----------------------------------------------|----------|---------------------------|
| `point`       | --                                           | Hard     | Lamps, candles, fill      |
| `directional` | `sun`                                        | Hard     | Sunlight, distant sources |
| `spot`        | `spotlight`                                  | Hard     | Stage lights, flashlights |
| `area`        | `area_light`, `rect`, `rect_light`           | Soft     | Softboxes, windows        |
| `sphere`      | `sphere_light`, `ball`, `ball_light`         | Soft     | Light bulbs, lanterns     |
| *(geometry)*  | *(automatic from emissive entities)*         | Soft     | Neon signs, light panels  |

All lights are defined in the `lights:` section. If you omit the
`lights:` section entirely, the engine adds default lights (one
directional and one point). If you include an empty `lights: []`, the
scene has no explicit lights -- useful when relying entirely on emissive
objects or HDRI environment lighting.

### Where ambient lighting comes from

Ambient illumination arises from path-traced GI alone.

If you want soft fill light in your scene, you have three physically
correct options, all configured under `world: > sky:` (see Chapter 7):

- **Flat sky**: `sky.type: flat` with a low `color` (e.g. `[0.02, 0.02, 0.025]`).
  This emits uniformly in every direction and participates in NEE via
  uniform sphere sampling, providing uniform ambient illumination
  from all directions.
- **Gradient sky**: `sky.type: gradient` with low zenith/horizon values
  (and optionally a sun disk). The body of the gradient drives ambient
  fill via path-traced bounces; the sun disk drives sharp directional
  illumination.
- **HDRI sky**: `sky.type: hdri` for fully image-based lighting.
  Importance-sampled CDF guarantees efficient convergence even for very
  bright environments.

Indoor scenes typically do best with a low flat sky (or a black sky
plus emissive panels and area lights). Outdoor scenes use gradient or
HDRI as their dominant light source.

---

## 6.2 Point Light

```yaml
- type: "point"
  position: [2, 3, -1]
  color: [1.0, 0.95, 0.85]
  intensity: 25.0
```

| Parameter     | Default | Description                                       |
|---------------|---------|---------------------------------------------------|
| `position`    | --      | Location in world space                            |
| `color`       | --      | Light color `[R, G, B]`                            |
| `intensity`   | --      | Brightness multiplier                              |
| `soft_radius` | `0`     | Optional. >0 floors d² at r² to suppress fireflies |

A point light radiates equally in all directions from a single point. Its
brightness falls off with the **inverse square** of the distance: double
the distance, one quarter the intensity.

Because point lights are infinitely small, they produce **hard shadows**
with crisp edges. This looks unrealistic for large, nearby light sources
but is fine for distant lights or small accents.

**Intensity guideline:** Because of inverse-square falloff, point lights
need higher intensity values than you might expect. For an object 2 units
away, an intensity of 15--30 is typical. For 5 units, try 50--100.

**`soft_radius` for fog/medium scenes:** when a participating medium is
active, scattering events can land arbitrarily close to a point emitter
and the 1/d² term explodes into firefly pixels. Setting `soft_radius` to
a positive value (e.g. the physical bulb radius, `0.05`–`0.20`) clamps
the attenuation denominator to `max(d², r²)` and removes the singularity
without altering the look at `d ≥ r`. Default `0` = unclamped (original
behaviour).

---

## 6.3 Directional Light

```yaml
- type: "directional"
  direction: [-0.5, -1, 0.3]
  color: [1, 0.98, 0.92]
  intensity: 3.0
  angular_radius: 0.0            # Optional. 0.27 = real Sun disc (soft shadows)
```

| Parameter        | Default | Description                              |
|------------------|---------|------------------------------------------|
| `direction`      | --      | Direction *toward* the scene (from light)|
| `color`          | --      | Light color                              |
| `intensity`      | --      | Brightness multiplier                    |
| `angular_radius` | `0`     | Optional angular disc radius (degrees). 0 = hard shadows |

A directional light sends parallel rays -- all traveling in the same
direction. It has no position (think of it as infinitely far away) and
no distance falloff. It is the standard way to simulate sunlight.

The `direction` vector points **from the light toward the scene**, not
from the scene toward the light. `[-0.5, -1, 0.3]` means light comes
from the upper-left, slightly behind the camera.

Like point lights, directional lights produce **hard shadows** by default.

**Sun disc (`angular_radius`):** when set > 0, the renderer perturbs each shadow
ray within a cone of the given half-angle, producing a realistic soft penumbra. The
default `shadow_samples` is automatically raised to 4 when the disc is active.
The real Sun subtends approximately 0.27°.

```yaml
- type: "sun"
  direction: [-0.5, -1, 0.3]
  color: [1.0, 0.95, 0.80]
  intensity: 2.0
  angular_radius: 0.27    # Realistic solar disc — soft penumbra
```

Also available as `type: "sun"`.

---

## 6.4 Spot Light

```yaml
- type: "spot"
  position: [0, 4, 0]
  direction: [0, -1, 0]
  color: [1, 1, 1]
  intensity: 50.0
  inner_angle: 15
  outer_angle: 30
  shadow_samples: 1              # >1 + soft_radius > 0 → jittered source
```

| Parameter        | Default | Description                                       |
|------------------|---------|---------------------------------------------------|
| `position`       | --      | Location in world space                           |
| `direction`      | --      | Direction the spot is aimed                       |
| `color`          | --      | Light color                                       |
| `intensity`      | --      | Brightness multiplier                             |
| `inner_angle`    | `15`    | Half-angle of the full-intensity cone (degrees)   |
| `outer_angle`    | `30`    | Half-angle of the zero-intensity cone (degrees)   |
| `soft_radius`    | `0`     | Optional. Same role as on point lights — strongly recommended for spotlights inside a fog/medium |
| `shadow_samples` | `1`     | >1 + `soft_radius > 0` → jittered source for soft penumbra in fog |

A spot light emits from a point in a cone. Inside the inner cone the
light is at full intensity. Between the inner and outer cones it
smoothly fades to zero (cosine falloff). Outside the outer cone there is
no light.

Spot lights are ideal for theatrical effects, museum displays, and
flashlights. They also produce hard shadows.

**Multi-sample spot + soft radius:** setting `shadow_samples: 4` and
`soft_radius: 0.15` models a small bulb of radius 0.15 m and samples its
disc for each shadow query, creating a soft penumbra and eliminating
1/d² fireflies in fog. If `soft_radius == 0`, extra shadow samples have
no effect — leave it at 1 for efficiency.

Also available as `type: "spotlight"`.

---

## 6.5 Area Light: Soft Shadows

```yaml
- type: "area"
  corner: [-1, 3, -1]
  u: [2, 0, 0]
  v: [0, 0, 2]
  color: [1, 0.97, 0.93]
  intensity: 35.0
  soft_radius: 0.0               # Optional. >0 = floor distSq in cosLight/d²
```

| Parameter           | Default | Description                                |
|---------------------|---------|--------------------------------------------|
| `corner`            | --      | One corner of the rectangle                |
| `u`                 | --      | First edge vector (from corner)            |
| `v`                 | --      | Second edge vector (from corner)           |
| `color`             | --      | Light color                                |
| `intensity`         | --      | Brightness multiplier                      |
| `shadow_samples`    | `4`     | Number of shadow samples (higher = softer) |
| `soft_radius`       | `0`     | Optional. Clamps `distSq` in the cosLight/d² estimator |
| `visible_to_camera` | `true`  | When `false`, hides the panel from primary camera rays only — NEE, mirror reflections and indirect bounces still see it. See Section 6.8. |

An area light is a flat rectangle that emits light from its entire
surface. Because it has physical size, it produces **soft shadows** with
realistic penumbra (the gradual transition from shadow to light).

The rectangle is defined by `corner` and two edge vectors `u` and `v`,
just like a quad. The four vertices are `corner`, `corner+u`,
`corner+u+v`, and `corner+v`.

**`soft_radius` for fog/medium scenes:** in dense participating media, a
stratified sample on the area light can land nearly tangent to the receiver,
making the `cosLight / d²` attenuation unbounded. Setting `soft_radius` to
a small value (e.g. `0.5`–`2.0`) clamps the denominator at `max(distSq, r²)`,
eliminating these rare spikes. The geometric distance is not changed — only
the attenuation denominator. At distances `d ≥ r` the result is identical.

### Shadow Samples

The `shadow_samples` parameter controls how many random points on the
rectangle are tested per shadow query. More samples produce smoother
shadows but cost more rendering time.

| shadow_samples | Quality           | Speed     |
|----------------|-------------------|-----------|
| 1              | Hard shadow (noisy)| Fastest  |
| 4              | Noticeably soft    | Fast     |
| 9--16          | Smooth penumbra    | Moderate |
| 25--64         | Very smooth        | Slow     |

The CLI flag `-S` overrides `shadow_samples` for **all** area and sphere
lights in the scene, which is useful during draft renders:

```
RayTracer -i scene.yaml -s 16 -S 4      # Fast draft with low shadow quality
RayTracer -i scene.yaml -s 256 -S 16    # Final render with smooth shadows
```

### Note: Area Lights Are Visible (Internally-Managed Proxy)

Area lights are now visible to the camera and to specular rays: the
loader builds an emissive quad proxy at the same `corner`/`u`/`v` and
binds it to the area light so BSDF samples that hit the rectangle
contribute the same radiance NEE would assign — closing Veach's MIS
estimator on smooth-specular materials, which ensures correct specular
highlights on polished surfaces.

You can still drop a separate emissive quad of your own if you want a
custom-shaped panel (see Section 6.7); it stacks on top of the
internal proxy without conflict.

---

## 6.6 Sphere Light

```yaml
- type: "sphere"
  position: [0, 2, 0]
  radius: 0.3
  color: [1, 0.95, 0.85]
  intensity: 30.0
  shadow_samples: 12
```

| Parameter           | Default | Description                             |
|---------------------|---------|-----------------------------------------|
| `position`          | --      | Center of the sphere                    |
| `radius`            | --      | Radius of the light sphere; also defines proxy size |
| `color`             | --      | Light color                             |
| `intensity`         | --      | Brightness multiplier                   |
| `shadow_samples`    | `4`     | Number of shadow samples                |
| `visible_to_camera` | `true`  | When `false`, hides the sphere proxy from primary camera rays only — NEE, mirror reflections and indirect bounces still see it. See Section 6.8. |

A sphere light is like an area light, but spherical. It produces soft
shadows with a circular penumbra and creates perfectly round highlights
(catchlights) in reflective surfaces.

Sphere lights use **solid-angle sampling**, which is 2--10 times more
efficient than the equivalent emissive sphere for small or distant
lights. Prefer sphere lights over emissive spheres when the light source
is the main illumination in the scene.

Sphere lights are **visible** to the camera and to specular rays: an
internally-managed emissive sphere proxy at the same position/radius
backs the analytic light, closing Veach's MIS estimator so smooth glass
or polished metal balls in the scene reflect the light correctly
(rather than showing a dark hole at the mirror direction), which is
the expected behaviour for an analytic sphere light.

Sphere lights deliberately ignore `soft_radius`: the solid-angle
estimator `L = Intensity × Ω / N` is bounded by `4π · Intensity` even
when the receiver is inside the sphere, so the 1/d² floor used by
point/spot/area lights is unnecessary here.

---

## 6.7 Geometry Lights (Emissive Objects)

Any entity with an `emissive` material is automatically registered as a
geometry light. The engine samples its surface during direct illumination
(Next Event Estimation), just like explicit light sources.

```yaml
materials:
  - id: "panel_glow"
    type: "emissive"
    color: [1, 0.95, 0.9]
    intensity: 25.0

entities:
  # A visible light panel on the ceiling
  - name: "ceiling_light"
    type: "quad"
    q: [-0.5, 2.99, -0.5]
    u: [1, 0, 0]
    v: [0, 0, 1]
    material: "panel_glow"
```

### Geometry Lights vs Explicit Lights

| Feature                       | Geometry Light       | Explicit Light (area/sphere) |
|-------------------------------|----------------------|------------------------------|
| Visible in camera             | Yes (toggle on entity) | Yes (toggle on light)      |
| Visible in reflections        | Yes                  | Yes (via internal proxy)     |
| Direct illumination (NEE)     | Yes (automatic)      | Yes                          |
| Soft shadows                  | Yes                  | Yes                          |
| Sampling efficiency           | Good                 | Slightly better (analytic)   |

Both kinds of light support a `visible_to_camera` flag (default `true`) — see
the next section for the camera-visibility toggle.

Use geometry lights when the light source must be a **custom-shaped**
emitter (neon signs, lava flows, light tubes, irregular meshes). Use
explicit `area`/`sphere` lights for canonical rectangular/spherical
emitters — they sample more efficiently while still showing up in
camera and specular reflections.

The engine supports geometry lights on any samplable primitive: spheres,
quads, disks, boxes, cylinders, cones, tori, capsules, annuli, and
meshes.

### Multiple Importance Sampling — why every material participates

Direct illumination from a non-delta light (area, sphere, geometric,
environment) is computed by combining two independent strategies: **NEE**
samples a point on the light, **BSDF sampling** samples the bounce
direction from the material. Weighting the two with the **balance
heuristic** (default) or the **power heuristic** (`--mis power`)
reduces variance compared to using either strategy alone.

Every supported material — `lambertian`, `metal`, `mix`, `disney` —
exposes the `Sample`/`Pdf`/`Evaluate` triple the MIS estimator needs. No
configuration is required: the renderer picks the correct weight
automatically based on the material and light types involved. Pure
delta lights (point, directional, spot) and delta material lobes
(perfect mirror, ideal glass) are special-cased to weight 1 — no other
sampler can reach them.

For scenes with fog or smoke (`global_medium`), the phase function joins
the MIS pool: the renderer weighs NEE in-scattering against the
phase-sampled bounce, suppressing the fireflies that typically appear in
"god ray" volumes lit by a distant light.

---

## 6.8 Camera Visibility (`visible_to_camera`)

Production renderers let you decouple **how a light contributes to the
image** from **whether the light is itself visible in the frame**.
3D-Ray exposes this control under the underscore_case key
`visible_to_camera`.

When set to `false`:

- The light still illuminates the scene at full intensity via NEE
  (direct lighting).
- The light still appears in **mirror reflections, glass refractions and
  indirect bounces**.
- The light's proxy (or the entity's geometry) is **invisible to primary
  camera rays only**, which the renderer detects via `depth == maxDepth`.

### When to use it

| Use case | Setup |
|----------|-------|
| Off-frame fill light that should not appear as a bright shape in the sky | `visible_to_camera: false` on a `sphere`/`area` light placed outside the framing |
| Practical lamp visible only in a mirror in the room | `visible_to_camera: false` on the emissive entity |
| Soft area panel for product photography — clean sky, panel only visible via reflections on the product | `visible_to_camera: false` on the `area` light |
| Light card just outside the FOV that would otherwise clip the edge of the frame | same |

### On explicit lights

```yaml
lights:
  # KEY: visible everywhere (default)
  - type: "sphere"
    position: [ 3.5, 3.8, 1.5]
    radius: 0.35
    color: [1.0, 0.96, 0.88]
    intensity: 45.0

  # FILL: invisible to camera, but reflects in mirrors/glass and lights the scene
  - type: "sphere"
    position: [-3.5, 3.8, 1.5]
    radius: 0.35
    color: [0.65, 0.78, 1.0]
    intensity: 45.0
    visible_to_camera: false
```

### On emissive entities

`visible_to_camera` is also a common per-entity field, so any entity —
not only proxies of explicit lights — can be hidden from primary camera
rays. The natural use is a custom-shaped emissive panel that you want
the surface to illuminate the scene without showing up in the frame:

```yaml
materials:
  - id: "panel_glow"
    type: "emissive"
    color: [1.0, 0.92, 0.80]
    intensity: 2.5

entities:
  - name: "ceiling_panel"
    type: "box"
    material: "panel_glow"
    scale: [4.0, 0.05, 2.5]
    translate: [0, 4.0, 0]
    visible_to_camera: false       # clean ceiling, but room is lit
```

On a `group` the flag propagates to every child (the wrapper is applied
outside the group's internal BVH); a child can also carry its own flag,
which composes by OR (parent OR child invisible ⇒ invisible).

### Limitations

- `visible_to_camera` has no observable effect on `point`/`directional`/
  `spot` (delta) lights — they have no proxy geometry to hide in the
  first place.
- A camera ray that hits an invisible proxy is simply advanced past it
  (with a safety cap of 8 successive skips), so an unbounded stack of
  overlapping invisible emitters in front of the camera will eventually
  saturate the cap. Not a realistic scenario, but worth knowing.

### Worked example: `camera-visibility.yaml`

The scene `scenes/showcases/camera-visibility.yaml` packages
all the above ideas: a warm KEY sphere light visible in the sky and in
the reflections of two chrome balls; a cool FILL sphere light hidden
from the camera but clearly visible in those same reflections; an
emissive ceiling panel hidden from view but still lighting the floor by
NEE. Render it at preview quality with

```
RayTracer -i scenes/showcases/camera-visibility.yaml \
          -o renders/vtc.png -w 480 -H 270 -s 64 -d 6
```

and compare the chrome reflections side by side — that's the test that
the flag is doing the right thing.

---

## 6.9 The Three-Point Lighting Setup

The single most useful lighting technique in photography and 3D graphics
is the three-point setup:

```
                    ┌──────────┐
                    │ KEY LIGHT│   (large area, upper-left)
                    └──────────┘
                          ↓
        ┌─────────┐   [SUBJECT]   ┌──────────┐
        │FILL LIGHT│              │ RIM LIGHT│  (behind subject)
        └─────────┘               └──────────┘
```

### Key Light

The main light source. It defines the dominant shadow direction and
models the shape of the subject. Typically a large area light placed
above and to one side.

```yaml
- type: "area"
  corner: [-4, 4, -2]
  u: [2, 0, 0]
  v: [0, 0, 2]
  color: [1.0, 0.96, 0.90]
  intensity: 45.0
```

### Fill Light

A softer, dimmer light placed on the opposite side of the key. Its job
is to lift the shadows without creating its own dominant shadows.
Typically 1/3 the intensity of the key, and slightly cooler in color.

```yaml
- type: "area"
  corner: [3, 3, -1.5]
  u: [1.2, 0, 0]
  v: [0, 0, 1.2]
  color: [0.72, 0.82, 1.0]
  intensity: 15.0
  shadow_samples: 9
```

### Rim (Back) Light

A light placed behind and above the subject. It creates a bright edge
along the subject's contour, separating it from the background and
adding a sense of depth.

```yaml
- type: "point"
  position: [1, 4.5, 4.5]
  color: [1.0, 0.97, 0.88]
  intensity: 55.0
```

The warm/cool contrast (warm key, cool fill, warm rim) is a classic
formula that works for almost any subject.

---

## 6.10 Lighting Recipes

### Studio Product Photography

High-key setup: large, bright area lights from multiple angles. Minimal
shadows. Clean and commercial.

```yaml
lights:
  - type: "area"
    corner: [-3, 4, -3]
    u: [6, 0, 0]
    v: [0, 0, 6]
    color: [1, 1, 1]
    intensity: 50.0

  - type: "point"
    position: [0, 1, -4]
    color: [1, 1, 1]
    intensity: 10.0
```

### Dramatic Chiaroscuro

A single strong spot light with deep shadows. Inspired by Caravaggio
and film noir.

```yaml
lights:
  - type: "spot"
    position: [-3, 5, -2]
    direction: [0.6, -1, 0.4]
    color: [1.0, 0.9, 0.75]
    intensity: 80.0
    inner_angle: 12
    outer_angle: 25
```

### Outdoor Sunlight

Directional light for the sun, a subtle blue fill for sky light.

```yaml
lights:
  - type: "directional"
    direction: [-0.3, -1, 0.5]
    color: [1.0, 0.98, 0.92]
    intensity: 3.0

  - type: "directional"
    direction: [0, -1, 0]
    color: [0.5, 0.6, 0.85]
    intensity: 0.5

  - type: "point"
    position: [5, 1, -5]
    color: [0.8, 0.85, 1.0]
    intensity: 8.0
```

---

## 6.11 Complete Example: Lighting Comparison

A single sphere and pedestal lit by different light types.

```yaml
# lighting-comparison.yaml
# The same subject under five different lights.
# Render with: RayTracer -i lighting-comparison.yaml -w 1600 -H 500 -s 64

world:
  sky:
    type: "flat"
    color: [0.02, 0.02, 0.03]

cameras:
  - name: "main"
    position: [0, 3, -10]
    look_at: [0, 1, 0]
    fov: 50

lights:
  # 1. Point light over the first sphere
  - type: "point"
    position: [-6, 3.5, -1]
    color: [1, 0.95, 0.9]
    intensity: 25.0

  # 2. Directional light for the second sphere
  - type: "directional"
    direction: [-0.3, -1, 0.5]
    color: [1, 0.98, 0.92]
    intensity: 2.5

  # 3. Spot light for the third sphere
  - type: "spot"
    position: [0, 4, -1]
    direction: [0, -1, 0.2]
    color: [1, 1, 1]
    intensity: 50.0
    inner_angle: 12
    outer_angle: 25

  # 4. Area light for the fourth sphere
  - type: "area"
    corner: [2, 3.5, -1.5]
    u: [2, 0, 0]
    v: [0, 0, 2]
    color: [1, 0.97, 0.93]
    intensity: 30.0

materials:
  - id: "floor"
    type: "lambertian"
    color: [0.35, 0.35, 0.35]
  - id: "white_plastic"
    type: "disney"
    color: [0.9, 0.88, 0.85]
    roughness: 0.3
    specular: 0.6
  - id: "pedestal"
    type: "disney"
    color: [0.6, 0.6, 0.6]
    roughness: 0.15
    specular: 0.7
  - id: "glow"
    type: "emissive"
    color: [1, 0.9, 0.75]
    intensity: 15.0

entities:
  - type: "infinite_plane"
    point: [0, 0, 0]
    normal: [0, 1, 0]
    material: "floor"

  # Five spheres in a row, each lit by a different light
  # 1. Point light
  - type: "sphere"
    center: [-6, 1.5, 0]
    radius: 0.6
    material: "white_plastic"
  - type: "cylinder"
    center: [-6, 0, 0]
    radius: 0.4
    height: 0.9
    material: "pedestal"

  # 2. Directional light
  - type: "sphere"
    center: [-3, 1.5, 0]
    radius: 0.6
    material: "white_plastic"
  - type: "cylinder"
    center: [-3, 0, 0]
    radius: 0.4
    height: 0.9
    material: "pedestal"

  # 3. Spot light
  - type: "sphere"
    center: [0, 1.5, 0]
    radius: 0.6
    material: "white_plastic"
  - type: "cylinder"
    center: [0, 0, 0]
    radius: 0.4
    height: 0.9
    material: "pedestal"

  # 4. Area light
  - type: "sphere"
    center: [3, 1.5, 0]
    radius: 0.6
    material: "white_plastic"
  - type: "cylinder"
    center: [3, 0, 0]
    radius: 0.4
    height: 0.9
    material: "pedestal"

  # 5. Geometry light (emissive sphere)
  - type: "sphere"
    center: [6, 1.5, 0]
    radius: 0.6
    material: "white_plastic"
  - type: "cylinder"
    center: [6, 0, 0]
    radius: 0.4
    height: 0.9
    material: "pedestal"
  - type: "sphere"
    center: [6, 3.2, -0.5]
    radius: 0.15
    material: "glow"
```

This scene places five identical spheres in a row. Each is primarily
illuminated by a different light type, making it easy to compare shadow
quality, highlight shape, and falloff behavior side by side.

---

## 6.12 Caustics (Focused Light Through Glass & Mirrors)

A glass sphere, a tumbler of water, a gemstone or a polished metal ball
doesn't just cast a shadow — it **focuses** light into bright shapes: the
dancing spot at the bottom of a pool, the ring of light a wine glass throws on
the table, the coloured glow under a tinted bottle. These are **caustics**, and
because they need light to follow the bent (refracted) or reflected path, plain
shadow rays can't produce them.

3D-Ray renders caustics with a **photon pre-pass**. You don't mark anything in
the scene — any specular surface (glass, water, metal, mirror) automatically
focuses light, and any diffuse surface (a floor, a wall) receives it. All light
types drive caustics, including the **sun** (`directional`). Just turn them on:

```bash
# Caustics are on by default in the final/ultra presets:
RayTracer -i my-scene.yaml -q final

# Or enable them explicitly on any preset:
RayTracer -i my-scene.yaml -q standard --caustics on
```

A few practical notes:

- **`--caustic-photons <N>`** controls quality: more photons = sharper, less
  noisy caustics (and a slower pre-pass). The presets pick a sensible default.
- **Tinted glass casts a coloured caustic** — a red glass throws a red pool of
  light, because the photons pick up the glass colour as they pass through.
- A small, bright light gives a **sharp** caustic; a large light gives a **soft**
  one. The sun gives crisp, parallel-ray caustics.
- Frosted/rough glass and HDRI-environment caustics are softer and handled by
  the regular path tracer in this version.

Copy-paste-ready glass, metal and lens setups live in
[`scenes/presets/caustics.md`](../../../scenes/presets/caustics.md).

## 6.13 Exposure Compensation (`--exposure`)

Once lights are placed, the tone mapper has to translate scene radiance
into a 0-1 displayable range. 3D-Ray uses the **ACES filmic** curve,
the industry-standard tone map used across film and VFX pipelines. ACES is non-linear: contrast is preserved only
inside its linear sweet-spot at roughly `[0.18, 1.0]` of incoming
radiance. Above ~2.0 the curve flattens onto a 0.95-0.99 plateau where
everything reads "almost white" — base colours, marble veining and
material identity all collapse into the same brightness.

That collapse is the most common reason a well-designed scene looks
"washed out" or "lifeless": the lights are simply too strong and ACES
has nowhere left to roll off the highlights. The fix is **photographic
exposure compensation** — a linear gain `2^EV` applied to every pixel
*before* tone mapping:

```bash
RayTracer -i scene.yaml -o out.png --exposure -1.5
```

EV semantics match a real camera: `EV = 0` (default) is identity,
`EV = -1` darkens by a factor of 2 (one stop down), `EV = +1` brightens
by 2 (one stop up). The flag mirrors the standard photographic exposure compensation
control available in production renderers.

**When to reach for it:**

| Symptom | `--exposure` to try |
|---|---|
| Highlights blow out before mid-tones read at all | `-1` to `-2` |
| Whites look uniformly cream, marble texture invisible | `-1` to `-1.5` |
| Image lands in noisy near-black mid-tones | `+1` to `+2` |
| Lights tuned to land near `0.5` linear already | `0` (skip the flag) |

**When *not* to use it.** Exposure is a global multiplier — it shifts
*every* pixel by the same amount. If only one part of the scene is
washed out (a single hero light, a too-emissive object), rebalance
that light's intensity instead so the scene is correctly exposed
without the flag. Reserve `--exposure` for fast iteration on shots
where you don't want to commit a YAML change, and for compensating
HDRI/IBL scenes whose absolute luminance you don't control.

The exposure pass is applied between the per-sample firefly clamp
(`-C` / `--clamp`) and the ACES curve, so all the standard clamps and
post-processing still behave identically. The only thing that changes
is *which slice* of the ACES curve your radiance lands on.

---

## What You Have Learned

- **Point** lights radiate from a point (inverse-square falloff, hard
  shadows).
- **Directional** lights send parallel rays (no falloff, hard shadows by
  default). Use `angular_radius: 0.27` for a realistic solar disc with soft
  shadows.
- **Spot** lights emit a cone with inner/outer angle control. Use
  `soft_radius` + `shadow_samples > 1` for soft penumbra in fog.
- **Area** lights are rectangles that produce soft shadows; quality
  controlled by `shadow_samples`. Use `soft_radius` to prevent spikes in
  dense media.
- **Sphere** lights produce soft shadows with circular highlights and
  use the bounded solid-angle estimator (no `soft_radius` needed).
- **Emissive entities** automatically become geometry lights -- visible
  and sampled for direct illumination.
- `area` and `sphere` lights are also visible to camera and specular
  rays via an internally-managed emissive proxy primitive, ensuring
  full Veach-MIS convergence.
- Per-light and per-entity **`visible_to_camera: false`** hides the
  proxy/geometry from primary camera rays only; NEE, mirrors, glass and
  indirect bounces still see it.
- The `-S` CLI flag overrides shadow samples globally for fast drafts.
- The **three-point setup** (key, fill, rim) is a reliable starting
  point for any scene.
- **Firefly controls:**
  - `soft_radius` on point/spot/area lights → floors the attenuation denominator
  - `--indirect-clamp-factor 0.25` → tighter clamp on bounce ≥ 1
  - `--light-sampling power` → pick one light ∝ `ApproximatePower` (faster convergence in multi-light scenes)

---

[Previous: Transforms, Groups, and Scene Organization](./05-transforms-and-groups.md) | [Next: Sky, Environment, and Camera Effects](./07-sky-environment-camera.md) | [Tutorial Index](./README.md)
