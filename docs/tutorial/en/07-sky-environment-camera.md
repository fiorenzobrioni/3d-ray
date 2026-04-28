# Chapter 7: Sky, Environment, and Camera Effects

The sky is the largest light source in any outdoor scene. A well-
configured environment can turn a flat render into something truly
photographic. This chapter also covers depth of field and multi-camera
setups.

---

## 7.1 Sky Modes

The sky determines what color a ray receives when it misses all objects
and escapes to infinity. 3D-Ray supports three sky modes, configured
under `world: > sky:`.

| Mode       | Description                                      |
|------------|--------------------------------------------------|
| `flat`     | Single solid color (uses `background`)           |
| `gradient` | Vertical three-band gradient with optional sun   |
| `hdri`     | Equirectangular HDR image (image-based lighting) |

When there is no `sky:` block, the engine uses a flat sky with the
`background:` color.

---

## 7.2 Flat Sky (Default)

```yaml
world:
  background: [0.5, 0.65, 0.9]
```

Without a `sky:` section, rays that escape the scene return the
`background` color. This is perfectly adequate for studio scenes with
controlled lighting (where the background is usually black) or simple
outdoor sketches.

You can also be explicit:

```yaml
world:
  sky:
    type: "flat"
```

This behaves identically to using the `background:` color.

---

## 7.3 Gradient Sky with Sun Disk

A gradient sky creates a realistic vertical blend from zenith (straight
up) to horizon to ground, and optionally adds a visible sun disk.

```yaml
world:
  sky:
    type: "gradient"
    zenith_color: [0.25, 0.45, 0.9]
    horizon_color: [0.7, 0.8, 0.95]
    ground_color: [0.4, 0.35, 0.3]
```

| Parameter       | Default | Description                                   |
|-----------------|---------|-----------------------------------------------|
| `zenith_color`  | --      | Color directly overhead                        |
| `horizon_color` | --      | Color at the horizon                           |
| `ground_color`  | --      | Color below the horizon                        |

The gradient interpolates vertically: rays pointing straight up get the
zenith color; rays at the horizon get the horizon color; rays pointing
below the horizon get the ground color.

### Adding a Sun Disk

```yaml
world:
  sky:
    type: "gradient"
    zenith_color: [0.2, 0.35, 0.75]
    horizon_color: [0.85, 0.75, 0.55]
    ground_color: [0.3, 0.25, 0.2]
    sun:
      direction: [-0.5, -0.3, 1]
      color: [1.0, 0.95, 0.8]
      intensity: 10.0
      size: 3.0
      falloff: 32.0
```

| Sun Parameter | Default | Description                                |
|---------------|---------|--------------------------------------------|
| `direction`   | --      | Direction *toward* the sun (from scene)    |
| `color`       | --      | Sun disk color                             |
| `intensity`   | `10.0`  | Brightness multiplier                      |
| `size`        | `3.0`   | Angular diameter in degrees                |
| `falloff`     | `32.0`  | Edge sharpness (higher = crisper edge)     |

The sun disk appears as a bright spot in the sky. It participates in
direct illumination (the engine can sample it for Next Event
Estimation), which means it produces shadows and highlights like an
explicit light source. You can use a gradient sky with a sun disk as
the **sole light source** in an outdoor scene.

### Preset: Golden Hour

```yaml
world:
  sky:
    type: "gradient"
    zenith_color: [0.15, 0.25, 0.55]
    horizon_color: [0.95, 0.65, 0.3]
    ground_color: [0.25, 0.18, 0.12]
    sun:
      direction: [-0.8, -0.15, 0.5]
      color: [1.0, 0.75, 0.4]
      intensity: 15.0
      size: 4.0
      falloff: 24.0
```

### Preset: Noon

```yaml
world:
  sky:
    type: "gradient"
    zenith_color: [0.25, 0.45, 0.9]
    horizon_color: [0.6, 0.75, 0.95]
    ground_color: [0.35, 0.3, 0.25]
    sun:
      direction: [0.1, -1, 0.2]
      color: [1.0, 0.98, 0.95]
      intensity: 12.0
      size: 2.5
      falloff: 40.0
```

### Preset: Night with Moon

```yaml
world:
  ambient_light: [0.005, 0.005, 0.01]
  sky:
    type: "gradient"
    zenith_color: [0.01, 0.01, 0.04]
    horizon_color: [0.03, 0.03, 0.06]
    ground_color: [0.01, 0.01, 0.02]
    sun:
      direction: [0.4, -0.6, 0.7]
      color: [0.7, 0.75, 0.9]
      intensity: 3.0
      size: 1.5
      falloff: 50.0
```

---

## 7.4 HDRI Image-Based Lighting

For maximum realism, use a High Dynamic Range Image (HDRI) as the sky.
An HDRI captures the full light field of a real environment -- every
direction has a measured brightness and color.

```yaml
world:
  sky:
    type: "hdri"
    path: "textures/venice_sunset_2k.hdr"
    intensity: 1.0
    rotation: 45.0
```

| Parameter   | Default | Description                                 |
|-------------|---------|---------------------------------------------|
| `path`      | --      | Path to an equirectangular HDR file         |
| `intensity` | `1.0`   | Brightness multiplier                       |
| `rotation`  | `0.0`   | Horizontal rotation in degrees              |

The HDRI map wraps around the entire scene as a sphere. The engine uses
**importance sampling** to concentrate shadow rays toward the brightest
areas of the map, which speeds up convergence dramatically.

### Tips for HDRI Lighting

- HDRI files are typically `.hdr` (Radiance) or `.exr` (OpenEXR) format.
- Free HDRIs are available from sites like Poly Haven (CC0 license).
- Use `intensity` to brighten or darken the environment without
  changing the file. Values of 0.5--2.0 are typical.
- HDRI lighting provides soft, natural illumination with complex
  color gradients. It is often the only light source you need for
  outdoor scenes.
- For indoor scenes using HDRI as environment, set `ambient_light`
  to `[0, 0, 0]` — the environment map supplies all indirect light.

### Finding the right `rotation`

`rotation` turns the environment sphere around the Y axis in degrees,
effectively choosing which direction the sun (or brightest spot) faces.
Here is a practical workflow:

1. **Quick preview pass.** Render a low-resolution preview (`-s 16 -w 400`).
   Note where the shadows fall on your objects — that is where the
   dominant light is coming from.
2. **Visualize the HDRI.** Open the `.hdr` file in a viewer (e.g., the
   free [hdrview](https://github.com/wkjarosz/hdrview), or any photo
   editor that supports HDR). Locate the sun or brightest hotspot. Note
   roughly how far from the center-left it sits in the equirectangular
   panorama.
3. **Map panorama position to degrees.** The full width of the
   equirectangular image represents 360°. If the sun is at 25% from the
   left edge, it is at 90°. If it is at 75%, it is at 270°. A `rotation`
   of X° rotates the environment so that the 0° meridian of the HDRI
   faces the `+Z` direction of your scene plus X°.
4. **Iterate.** Start with `rotation: 0` and adjust in 45° steps until
   shadows fall where you want them, then fine-tune by 10–15° increments.

> **Tip:** If the sun direction matters for a specific shot (e.g., the
> sun should be at camera-left to create a rim light on your subject),
> work backwards: decide the desired shadow angle in world space, then
> pick a `rotation` that places the HDRI's bright spot at that angle
> from the `+Z` axis.

---

## 7.5 Depth of Field

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

| Parameter    | Default | Description                                  |
|--------------|---------|----------------------------------------------|
| `aperture`   | `0.0`  | Lens diameter (0 = everything in focus)       |
| `focal_dist` | `1.0`  | Distance from camera at which objects are sharp |

### How It Works

- `aperture: 0` (the default) gives a perfect pinhole camera --
  everything is in focus regardless of distance.
- `aperture > 0` simulates a real lens. The larger the aperture, the
  shallower the depth of field (more blur for out-of-focus objects).
- `focal_dist` sets the focus distance. Objects at exactly this
  distance from the camera are perfectly sharp.

### Practical Guidance

1. Set `focal_dist` to the distance between the camera and your subject.
   The vector from `position` to `look_at` has this length.
2. Start with a small aperture (0.05--0.1) and increase until you get
   the desired blur.
3. DOF requires **more samples** for a clean result. Use at least 64
   SPP; 256+ is recommended for production.

### Example: Focusing on the Middle Sphere

```yaml
cameras:
  - name: "main"
    position: [0, 1, -6]
    look_at: [0, 0.5, 0]
    fov: 45
    aperture: 0.12
    focal_dist: 6.0       # Distance to the subject row
```

Objects closer to and farther from the camera than 6 units will appear
blurred, with increasing blur the farther they are from the focal plane.

---

## 7.6 Multiple Named Cameras

You can define several cameras in a single scene file and switch between
them on the command line without editing the YAML.

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

  - name: "topdown"
    position: [0, 8, 0.01]
    look_at: [0, 0, 0]
    fov: 45
```

Use the `cameras:` key (plural, list) instead of the singular `camera:`.
Each camera must have a unique `name:`.

### Selecting a Camera from the CLI

```
RayTracer -i scene.yaml --camera wide
RayTracer -i scene.yaml --camera closeup
RayTracer -i scene.yaml -c 2            # By zero-based index (topdown)
```

### Listing Available Cameras

```
RayTracer -i scene.yaml --list-cameras
```

This prints the names and indices of all defined cameras without
rendering.

When multiple cameras exist and no `--camera` flag is provided, the
engine uses the **first camera** in the list and prints a warning.

> **Note:** The singular `camera:` key (no list) still works for scenes
> with a single camera. If both `camera:` and `cameras:` are present,
> `cameras:` takes precedence.

---

## 7.7 Complete Example: Golden Hour Landscape

```yaml
# golden-hour.yaml
# An outdoor scene with gradient sky, sun disk, DOF, and multiple cameras.

world:
  ambient_light: [0.02, 0.015, 0.01]
  sky:
    type: "gradient"
    zenith_color: [0.15, 0.25, 0.55]
    horizon_color: [0.95, 0.65, 0.3]
    ground_color: [0.2, 0.15, 0.1]
    sun:
      direction: [-0.8, -0.2, 0.6]
      color: [1.0, 0.78, 0.42]
      intensity: 14.0
      size: 4.0
      falloff: 24.0

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
    focal_dist: 3.5

  - name: "dramatic"
    position: [-2, 0.4, -4]
    look_at: [0, 0.5, 0]
    fov: 70

lights:
  # The gradient sky + sun is the primary light source.
  # Add a subtle fill to lift the deepest shadows.
  - type: "directional"
    direction: [0.5, -0.5, -0.3]
    color: [0.4, 0.5, 0.7]
    intensity: 0.4

materials:
  - id: "ground"
    type: "disney"
    color: [0.35, 0.28, 0.18]
    roughness: 0.85

  - id: "stone"
    type: "disney"
    color: [0.55, 0.52, 0.48]
    roughness: 0.65

  - id: "grass"
    type: "disney"
    color: [0.18, 0.35, 0.08]
    roughness: 0.8
    subsurface: 0.15

  - id: "gold_sphere"
    type: "disney"
    color: [1.0, 0.76, 0.33]
    metallic: 1.0
    roughness: 0.05

  - id: "glass_sphere"
    type: "dielectric"
    refraction_index: 1.52

entities:
  # Ground plane
  - type: "infinite_plane"
    point: [0, 0, 0]
    normal: [0, 1, 0]
    material: "ground"

  # Some stones
  - type: "sphere"
    center: [-1.5, 0.25, 1]
    radius: 0.25
    material: "stone"
    scale: [1, 0.6, 1]

  - type: "sphere"
    center: [2, 0.2, 2]
    radius: 0.2
    material: "stone"
    scale: [1.2, 0.5, 0.9]

  # Hero objects
  - type: "sphere"
    center: [0, 0.5, 0]
    radius: 0.5
    material: "gold_sphere"

  - type: "sphere"
    center: [1.2, 0.35, -0.5]
    radius: 0.35
    material: "glass_sphere"

  # Grass tufts (small spheres)
  - type: "sphere"
    center: [-0.8, 0.08, -0.5]
    radius: 0.08
    material: "grass"

  - type: "sphere"
    center: [0.5, 0.06, 0.8]
    radius: 0.06
    material: "grass"
```

### Rendering the Three Cameras

```
RayTracer -i golden-hour.yaml -c landscape -w 1920 -H 800 -s 256 -d 6
RayTracer -i golden-hour.yaml -c macro -w 1200 -H 800 -s 1024 -d 8 -S 4
RayTracer -i golden-hour.yaml -c dramatic -w 1920 -H 800 -s 256 -d 6
```

The "macro" camera has DOF enabled -- the gold sphere will be sharp while
the background softly blurs.

---

## What You Have Learned

- The **flat** sky uses the background color and is best for studios.
- The **gradient** sky provides a three-band vertical blend; adding a
  `sun:` disk turns it into a full outdoor light source.
- **HDRI** maps provide photorealistic environment lighting with
  importance sampling.
- **Depth of field** is controlled by `aperture` (lens size) and
  `focal_dist` (focus distance). Larger aperture = more blur.
- **Multiple cameras** let you define several viewpoints and switch
  between them with `--camera name` on the CLI.

---

[Previous: Lighting Mastery](./06-lighting.md) | [Next: Constructive Solid Geometry (CSG)](./08-csg.md) | [Tutorial Index](./README.md)
