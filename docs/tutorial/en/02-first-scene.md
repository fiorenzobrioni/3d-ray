# Chapter 2: Your First Scene

Time to write YAML and render an image. In this chapter you will build a
complete scene from scratch -- three spheres sitting on a floor, lit by two
light sources -- and understand every line of the file.

---

## 2.1 The Absolute Minimum: One Sphere

Create a file called `hello.yaml` and type the following:

```yaml
world:
  ambient_light: [0.03, 0.03, 0.04]
  background: [0.05, 0.05, 0.08]

cameras:
  - name: "main"
    position: [0, 1, -5]
    look_at: [0, 0.5, 0]
    fov: 50

lights:
  - type: "directional"
    direction: [-1, -1, 1]
    color: [1, 1, 1]
    intensity: 2.0

materials:
  - id: "white"
    type: "lambertian"
    color: [0.9, 0.9, 0.9]

entities:
  - name: "ball"
    type: "sphere"
    center: [0, 0.5, 0]
    radius: 0.5
    material: "white"
```

Render it:

```
RayTracer -i hello.yaml -w 800 -H 450 -s 16
```

You should see a white sphere floating against a dark background. Let us
walk through every section.

---

## 2.2 The World Section

```yaml
world:
  ambient_light: [0.03, 0.03, 0.04]
  background: [0.05, 0.05, 0.08]
```

**`ambient_light`** is a constant amount of light that reaches every
surface in the scene regardless of whether a light source can see it.
Think of it as a very faint fill light that prevents pitch-black shadows.
Keep it low (0.01--0.05 per channel) or your scene will look washed out.

**`background`** is the color returned by rays that miss every object and
escape into empty space. In a studio scene you typically set this to black
or near-black. In an outdoor scene it represents the sky (though for
richer skies you will use the `sky:` block in Chapter 7).

Both values are `[R, G, B]` in the 0.0--1.0 range.

The `world:` section also supports a **ground shorthand**:

```yaml
world:
  ground:
    type: "plane"
    material: "floor_material"
    y: 0.0
```

This places an infinite ground plane at the specified Y coordinate using
the given material, saving you from adding a separate infinite-plane
entity. You can use either this shorthand or an explicit entity -- they
produce the same result.

---

## 2.3 The Camera

```yaml
cameras:
  - name: "main"
    position: [0, 1, -5]
    look_at: [0, 0.5, 0]
    fov: 50
```

The camera defines your viewpoint:

| Field        | Default      | Description                                    |
|--------------|--------------|------------------------------------------------|
| `position`   | --           | Where the camera is in world space              |
| `look_at`    | --           | The point the camera aims at                    |
| `vup`        | `[0, 1, 0]` | Which direction is "up"                         |
| `fov`        | `60`         | Vertical field of view in degrees               |
| `aperture`   | `0`          | Lens diameter for depth of field (0 = pinhole)  |
| `focal_dist` | `1`          | Focus distance for depth of field               |

**`position`** and **`look_at`** together determine where the camera is and
what it is pointed at. The vector from `position` to `look_at` is the
view direction.

**`vup`** (view up) tells the camera which way is up. You almost never
need to change this unless you want a tilted "Dutch angle" shot.

**`fov`** controls how much of the scene is visible. A small value (e.g.
25) acts like a telephoto lens -- objects appear larger but you see less of
the scene. A large value (e.g. 90) acts like a wide-angle lens.

**`aperture`** and **`focal_dist`** control depth of field and are covered
in Chapter 7. Set `aperture` to `0` (the default) for everything to be
in focus.

> **Tip:** `cameras:` (the list form) is the recommended format -- it
> works for one camera or many. The singular `camera:` key still works
> for backward compatibility. Multi-camera setup is covered in Chapter 7.

---

## 2.4 Materials: Lambertian and Metal

Materials describe how a surface looks and how it interacts with light.
In this chapter we introduce two basic types: **Lambertian** and **Metal**.
(The remaining four types are covered in Chapter 3.)

### Lambertian (Diffuse Matte)

```yaml
- id: "red_matte"
  type: "lambertian"
  color: [0.8, 0.1, 0.1]
```

A Lambertian surface scatters incoming light equally in all directions
above the surface. It has no reflections, no shine -- just a flat, matte
appearance. `color` is the diffuse albedo: the fraction of light reflected
per RGB channel.

- `[1, 1, 1]` reflects all light (pure white).
- `[0, 0, 0]` absorbs all light (pure black).
- `[0.8, 0.1, 0.1]` reflects mostly red.

### Metal

```yaml
- id: "gold_mirror"
  type: "metal"
  color: [1.0, 0.76, 0.33]
  fuzz: 0.0

- id: "brushed_steel"
  type: "metal"
  color: [0.7, 0.7, 0.72]
  fuzz: 0.3
```

A Metal surface reflects light specularly (like a mirror). The `color`
field represents the metallic reflectance -- for physically accurate
results, use measured values:

| Metal          | Color (approximate)     |
|----------------|-------------------------|
| Gold           | `[1.0, 0.76, 0.33]`    |
| Silver         | `[0.97, 0.96, 0.95]`   |
| Copper         | `[0.95, 0.64, 0.54]`   |
| Steel          | `[0.7, 0.7, 0.72]`     |
| Aluminum       | `[0.91, 0.92, 0.93]`   |

The **`fuzz`** parameter controls roughness:

- `fuzz: 0.0` produces a perfect mirror.
- `fuzz: 0.1` produces a slightly blurred reflection (satin finish).
- `fuzz: 0.3` produces a visibly blurred reflection (brushed metal).
- Values above 0.5 create a very diffused, almost matte look.

---

## 2.5 Entities: Placing Objects

```yaml
entities:
  - name: "floor"
    type: "infinite_plane"
    point: [0, 0, 0]
    normal: [0, 1, 0]
    material: "grey_matte"

  - name: "ball"
    type: "sphere"
    center: [0, 0.5, 0]
    radius: 0.5
    material: "red_matte"
```

The `entities:` section is where you place objects in the scene. Every
entity has at minimum a `type` and a `material` reference (an ID that
matches a material defined in the `materials:` section).

**`name`** is optional but highly recommended -- it makes the scene file
readable and helps with debugging.

### Sphere

The simplest solid:

```yaml
- type: "sphere"
  center: [0, 1, 0]
  radius: 1.0
  material: "my_material"
```

A sphere sits at `center` with the given `radius`. If you want a sphere
resting on a floor at Y=0, set `center` to `[x, radius, z]` so the
bottom of the sphere just touches the plane.

### Infinite Plane

```yaml
- type: "infinite_plane"
  point: [0, 0, 0]
  normal: [0, 1, 0]
  material: "my_material"
```

An infinite flat surface passing through `point` with the given `normal`.
A floor is `normal: [0, 1, 0]` (pointing up). A back wall would be
`normal: [0, 0, -1]` (pointing toward the camera).

You will learn about all the other shapes (boxes, cylinders, tori, cones,
and more) in Chapter 4.

---

## 2.6 Lights: Illuminating the Scene

Without lights, the only illumination comes from the ambient light and the
background color -- which produces a very dim, flat image. Let us add two
explicit light sources.

### Directional Light

```yaml
- type: "directional"
  direction: [-1, -1, 1]
  color: [1, 1, 1]
  intensity: 2.0
```

A directional light sends parallel rays in the specified `direction`. It
has no position -- think of it as infinitely far away, like the sun.
The `direction` vector points *from* the light *toward* the scene.
`[-1, -1, 1]` means light comes from the upper-left-front.

### Point Light

```yaml
- type: "point"
  position: [3, 3, -2]
  color: [0.9, 0.9, 1.0]
  intensity: 15.0
```

A point light radiates from a single point. Its intensity falls off with
the square of the distance (inverse-square law), just like a real light
bulb. The `intensity` value therefore needs to be much higher than a
directional light's to achieve a similar brightness at the object.

Both light types produce **hard shadows** (crisp edges). For soft, natural
shadows you need area or sphere lights, covered in Chapter 6.

---

## 2.7 Complete Example: Three Spheres on a Floor

Here is the full scene file that puts everything together:

```yaml
# three-spheres.yaml
# A matte red sphere, a gold sphere, and a mirror sphere on a grey floor.

world:
  ambient_light: [0.02, 0.02, 0.03]
  background: [0.05, 0.05, 0.08]

cameras:
  - name: "main"
    position: [0, 2, -6]
    look_at: [0, 0.5, 0]
    fov: 45

lights:
  # Main light from upper-left
  - type: "directional"
    direction: [-1, -1, 1]
    color: [1, 0.98, 0.95]
    intensity: 2.5

  # Fill light from right
  - type: "point"
    position: [4, 3, -3]
    color: [0.8, 0.85, 1.0]
    intensity: 20.0

materials:
  - id: "grey_floor"
    type: "lambertian"
    color: [0.5, 0.5, 0.5]

  - id: "red_matte"
    type: "lambertian"
    color: [0.8, 0.1, 0.1]

  - id: "gold"
    type: "metal"
    color: [1.0, 0.76, 0.33]
    fuzz: 0.15

  - id: "mirror"
    type: "metal"
    color: [0.95, 0.95, 0.97]
    fuzz: 0.0

entities:
  # Floor
  - name: "floor"
    type: "infinite_plane"
    point: [0, 0, 0]
    normal: [0, 1, 0]
    material: "grey_floor"

  # Left sphere -- matte red
  - name: "left_sphere"
    type: "sphere"
    center: [-1.5, 0.5, 0]
    radius: 0.5
    material: "red_matte"

  # Center sphere -- brushed gold
  - name: "center_sphere"
    type: "sphere"
    center: [0, 0.75, 0]
    radius: 0.75
    material: "gold"

  # Right sphere -- perfect mirror
  - name: "right_sphere"
    type: "sphere"
    center: [1.5, 0.5, 0]
    radius: 0.5
    material: "mirror"
```

### Rendering

Start with a quick preview to check composition:

```
RayTracer -i three-spheres.yaml -w 400 -H 225 -s 1 -d 5
```

Then a draft to check materials and lighting:

```
RayTracer -i three-spheres.yaml -w 800 -H 450 -s 16 -d 20
```

And finally a clean render:

```
RayTracer -i three-spheres.yaml -w 1920 -H 1080 -s 256 -d 50
```

### What You Should See

- The **red sphere** on the left has a smooth, matte appearance with no
  reflections.
- The **gold sphere** in the center shows blurred reflections of the
  scene, tinted warm gold.
- The **mirror sphere** on the right shows crisp reflections of the other
  objects and the floor.
- The **floor** is a uniform grey extending to the horizon.
- There are two shadow regions for each sphere, one from each light.

---

## What You Have Learned

- A scene needs at minimum: `world`, `cameras:`, at least one `material`,
  at least one `entity`, and at least one `light`.
- `lambertian` gives a matte diffuse surface; `metal` gives a reflective
  one, controlled by `fuzz`.
- `directional` lights have no position (parallel rays); `point` lights
  radiate from a location.
- The iterative workflow (preview -> draft -> final) saves time.

---

[Previous: What Is Ray Tracing?](./01-what-is-ray-tracing.md) | [Next: Materials in Depth](./03-materials.md) | [Tutorial Index](./README.md)
