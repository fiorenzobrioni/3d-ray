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

---

## 6.2 Point Light

```yaml
- type: "point"
  position: [2, 3, -1]
  color: [1.0, 0.95, 0.85]
  intensity: 25.0
```

| Parameter   | Default | Description                             |
|-------------|---------|-----------------------------------------------|
| `position`  | --      | Location in world space                        |
| `color`     | --      | Light color `[R, G, B]`                        |
| `intensity` | --      | Brightness multiplier                          |

A point light radiates equally in all directions from a single point. Its
brightness falls off with the **inverse square** of the distance: double
the distance, one quarter the intensity.

Because point lights are infinitely small, they produce **hard shadows**
with crisp edges. This looks unrealistic for large, nearby light sources
but is fine for distant lights or small accents.

**Intensity guideline:** Because of inverse-square falloff, point lights
need higher intensity values than you might expect. For an object 2 units
away, an intensity of 15--30 is typical. For 5 units, try 50--100.

---

## 6.3 Directional Light

```yaml
- type: "directional"
  direction: [-0.5, -1, 0.3]
  color: [1, 0.98, 0.92]
  intensity: 3.0
```

| Parameter   | Default | Description                              |
|-------------|---------|------------------------------------------|
| `direction` | --      | Direction *toward* the scene (from light)|
| `color`     | --      | Light color                              |
| `intensity` | --      | Brightness multiplier                    |

A directional light sends parallel rays -- all traveling in the same
direction. It has no position (think of it as infinitely far away) and
no distance falloff. It is the standard way to simulate sunlight.

The `direction` vector points **from the light toward the scene**, not
from the scene toward the light. `[-0.5, -1, 0.3]` means light comes
from the upper-left, slightly behind the camera.

Like point lights, directional lights produce **hard shadows**.

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
```

| Parameter     | Default | Description                                  |
|---------------|---------|----------------------------------------------|
| `position`    | --      | Location in world space                      |
| `direction`   | --      | Direction the spot is aimed                  |
| `color`       | --      | Light color                                  |
| `intensity`   | --      | Brightness multiplier                        |
| `inner_angle` | `15`    | Half-angle of the full-intensity cone (degrees) |
| `outer_angle` | `30`    | Half-angle of the zero-intensity cone (degrees) |

A spot light emits from a point in a cone. Inside the inner cone the
light is at full intensity. Between the inner and outer cones it
smoothly fades to zero (cosine falloff). Outside the outer cone there is
no light.

Spot lights are ideal for theatrical effects, museum displays, and
flashlights. They also produce hard shadows.

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
  shadow_samples: 16
```

| Parameter        | Default | Description                                |
|------------------|---------|--------------------------------------------|
| `corner`         | --      | One corner of the rectangle                |
| `u`              | --      | First edge vector (from corner)            |
| `v`              | --      | Second edge vector (from corner)           |
| `color`          | --      | Light color                                |
| `intensity`      | --      | Brightness multiplier                      |
| `shadow_samples` | --      | Number of shadow samples (higher = softer) |

An area light is a flat rectangle that emits light from its entire
surface. Because it has physical size, it produces **soft shadows** with
realistic penumbra (the gradual transition from shadow to light).

The rectangle is defined by `corner` and two edge vectors `u` and `v`,
just like a quad. The four vertices are `corner`, `corner+u`,
`corner+u+v`, and `corner+v`.

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

### Important: Area Lights Are Invisible

Area lights illuminate the scene but are **not visible** as objects. A
ray that hits the light rectangle does not see a glowing surface -- it
passes through. If you want a visible light panel, use an emissive quad
(see Section 6.7).

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

| Parameter        | Default | Description                             |
|------------------|---------|-----------------------------------------|
| `position`       | --      | Center of the sphere                    |
| `radius`         | --      | Radius of the light sphere              |
| `color`          | --      | Light color                             |
| `intensity`      | --      | Brightness multiplier                   |
| `shadow_samples` | --      | Number of shadow samples                |

A sphere light is like an area light, but spherical. It produces soft
shadows with a circular penumbra and creates perfectly round highlights
(catchlights) in reflective surfaces.

Sphere lights use **solid-angle sampling**, which is 2--10 times more
efficient than the equivalent emissive sphere for small or distant
lights. Prefer sphere lights over emissive spheres when the light source
is the main illumination in the scene.

Like area lights, sphere lights are **invisible** to the camera.

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
| Visible in camera             | Yes                  | No                           |
| Visible in reflections        | Yes                  | No                           |
| Direct illumination (NEE)     | Yes (automatic)      | Yes                          |
| Soft shadows                  | Yes                  | Yes                          |
| Efficiency                    | Good                 | Slightly better              |

Use geometry lights when the light source should be **seen** (neon
signs, lava flows, glowing orbs, light bulbs). Use explicit lights when
you want an invisible light source (off-screen softboxes, fill lights).

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

## 6.8 The Three-Point Lighting Setup

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
  shadow_samples: 16
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

## 6.9 Lighting Recipes

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
    shadow_samples: 16

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

## 6.10 Complete Example: Lighting Comparison

A single sphere and pedestal lit by different light types.

```yaml
# lighting-comparison.yaml
# The same subject under five different lights.
# Render with: RayTracer -i lighting-comparison.yaml -w 1600 -H 500 -s 64

world:
  ambient_light: [0.01, 0.01, 0.015]
  background: [0.02, 0.02, 0.03]

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
    shadow_samples: 16

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

## What You Have Learned

- **Point** lights radiate from a point (inverse-square falloff, hard
  shadows).
- **Directional** lights send parallel rays (no falloff, hard shadows).
- **Spot** lights emit a cone with inner/outer angle control.
- **Area** lights are rectangles that produce soft shadows; quality
  controlled by `shadow_samples`.
- **Sphere** lights produce soft shadows with circular highlights.
- **Emissive entities** automatically become geometry lights -- visible
  and sampled for direct illumination.
- The `-S` CLI flag overrides shadow samples globally for fast drafts.
- The **three-point setup** (key, fill, rim) is a reliable starting
  point for any scene.

---

[Previous: Transforms, Groups, and Scene Organization](./05-transforms-and-groups.md) | [Next: Sky, Environment, and Camera Effects](./07-sky-environment-camera.md) | [Tutorial Index](./README.md)
