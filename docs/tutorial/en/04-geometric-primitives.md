# Chapter 4: All the Shapes

3D-Ray provides a rich set of geometric primitives -- from simple spheres
to tori, capsules, and OBJ meshes. This chapter documents every one with
exact YAML syntax, default values, and usage notes.

---

## 4.1 Sphere

```yaml
- type: "sphere"
  center: [0, 1, 0]
  radius: 1.0
  material: "my_material"
```

| Parameter  | Default     | Description                     |
|------------|-------------|---------------------------------|
| `center`   | `[0, 0, 0]`| Center point in world space     |
| `radius`   | `1.0`       | Radius of the sphere            |

The sphere is the simplest and fastest primitive. It has an analytic
intersection (no approximation) and is the ideal test shape.

To place a sphere resting on a floor at Y=0, set `center` to
`[x, radius, z]`.

---

## 4.2 Box (Unit Cube)

```yaml
- type: "box"
  material: "wood"
  translate: [0, 0.5, 0]
  scale: [2, 1, 1.5]
  rotate: [0, 30, 0]
```

The box is always a **unit cube** ranging from -0.5 to +0.5 on each
axis. It has no `center` or `size` parameters. All positioning and
sizing is done through transforms.

This design means you **must** use `translate`, `scale`, and/or `rotate`
to place a box in your scene.

| Transform   | Effect on the unit cube                      |
|-------------|----------------------------------------------|
| `scale: [2, 1, 1.5]` | Makes it 2 units wide, 1 tall, 1.5 deep |
| `translate: [0, 0.5, 0]` | Lifts it so the bottom sits on Y=0  |
| `rotate: [0, 45, 0]`   | Rotates 45 degrees around Y axis       |

**Example: a tabletop** (thin, wide box on Y=0.75):

```yaml
- type: "box"
  material: "wood"
  scale: [2.0, 0.08, 1.0]
  translate: [0, 0.79, 0]
```

---

## 4.3 Infinite Plane

```yaml
- type: "infinite_plane"
  point: [0, 0, 0]
  normal: [0, 1, 0]
  material: "floor"
```

| Parameter | Default | Description                                   |
|-----------|---------|-----------------------------------------------|
| `point`   | --      | Any point that lies on the plane               |
| `normal`  | --      | The direction the plane faces (perpendicular)  |

An infinite plane extends forever in all directions. It is perfect for
floors, walls, and ceilings.

Common orientations:

| Surface    | `point`       | `normal`       |
|------------|---------------|----------------|
| Floor      | `[0, 0, 0]`  | `[0, 1, 0]`   |
| Ceiling    | `[0, 3, 0]`  | `[0, -1, 0]`  |
| Back wall  | `[0, 0, 5]`  | `[0, 0, -1]`  |
| Left wall  | `[-3, 0, 0]` | `[1, 0, 0]`   |

> **Note:** Infinite planes cannot be bounded, so they are handled
> separately from the BVH acceleration structure. Use them sparingly
> (1--3 per scene). For bounded flat surfaces, use a quad instead.

Also available as `type: "plane"`.

---

## 4.4 Cylinder

```yaml
- type: "cylinder"
  center: [0, 0, 0]
  radius: 0.3
  height: 2.0
  material: "steel"
```

| Parameter | Default     | Description                            |
|-----------|-------------|----------------------------------------|
| `center`  | `[0, 0, 0]`| Center of the base disk                |
| `radius`  | `1.0`       | Cylinder radius                        |
| `height`  | `1.0`       | Height (extends upward from center)    |

The cylinder is Y-aligned (vertical) and capped on both ends. The
`center` is the middle of the bottom disk; the cylinder extends upward
by `height`.

**Example: a pillar** from the floor to 3 units high:

```yaml
- type: "cylinder"
  center: [0, 0, 0]
  radius: 0.25
  height: 3.0
  material: "marble"
```

---

## 4.5 Cone and Truncated Cone (Frustum)

```yaml
# Sharp cone (top_radius = 0)
- type: "cone"
  center: [0, 0, 0]
  radius: 0.5
  top_radius: 0.0
  height: 1.5
  material: "red"

# Truncated cone / frustum
- type: "cone"
  center: [0, 0, 0]
  radius: 0.6
  top_radius: 0.2
  height: 1.0
  material: "blue"
```

| Parameter    | Default     | Description                           |
|--------------|-------------|---------------------------------------|
| `center`     | `[0, 0, 0]`| Center of the base disk               |
| `radius`     | `1.0`       | Bottom radius                         |
| `top_radius` | `0.0`       | Top radius (0 = sharp point)          |
| `height`     | `1.0`       | Height (extends upward from center)   |

When `top_radius` is 0 the result is a pointed cone. When it is greater
than 0 the cone is truncated (a frustum) -- a useful shape for table
legs, lampshades, and vases.

Type aliases: `cone`, `truncated_cone`, `frustum`.

---

## 4.6 Torus (Donut)

```yaml
- type: "torus"
  major_radius: 1.0
  minor_radius: 0.3
  material: "gold"
  translate: [0, 0.3, 0]
```

| Parameter      | Default | Description                                 |
|----------------|---------|---------------------------------------------|
| `major_radius` | `1.0`   | Distance from the center to the tube center |
| `minor_radius` | `0.25`  | Radius of the tube itself                   |

The torus is always created in the **XZ plane at the origin**. Use
`translate` to position it and `rotate` to tilt it.

- A ring (like a donut or a tire) lies flat by default.
- To stand it upright, `rotate: [90, 0, 0]`.
- The ratio `minor_radius / major_radius` controls the "fatness".

Type aliases: `torus`, `donut`.

> **Technical note:** The torus uses an analytic quartic solver for
> ray intersection -- no tessellation, perfectly smooth at any zoom
> level.

---

## 4.7 Capsule

```yaml
- type: "capsule"
  center: [0, 0, 0]
  radius: 0.3
  height: 1.5
  material: "white"
```

| Parameter | Default     | Description                                |
|-----------|-------------|--------------------------------------------|
| `center`  | `[0, 0, 0]`| Center of the base hemisphere              |
| `radius`  | `1.0`       | Radius of the cylinder and hemispheres     |
| `height`  | `1.0`       | Height of the cylindrical section          |

A capsule is a cylinder with hemispherical caps on both ends -- like a
pill or a sausage. The total height is `height + 2 * radius`.

Type aliases: `capsule`, `pill`.

---

## 4.8 Annulus (Ring Disk)

```yaml
- type: "annulus"
  center: [0, 0.01, 0]
  radius: 1.0
  inner_radius: 0.5
  normal: [0, 1, 0]
  material: "metal"
```

| Parameter      | Default     | Description                         |
|----------------|-------------|-------------------------------------|
| `center`       | `[0, 0, 0]`| Center point                        |
| `radius`       | `1.0`       | Outer radius                        |
| `inner_radius` | `0.0`       | Inner radius (the hole)             |
| `normal`       | --          | Face direction                      |

An annulus is a flat disk with a circular hole in the middle -- like a
washer or a ring viewed from above.

Type aliases: `annulus`, `ring_disk`.

---

## 4.9 Disk

```yaml
- type: "disk"
  center: [0, 0, 0]
  radius: 1.0
  normal: [0, 1, 0]
  material: "white"
```

| Parameter | Default     | Description                          |
|-----------|-------------|--------------------------------------|
| `center`  | `[0, 0, 0]`| Center point                         |
| `radius`  | `1.0`       | Radius of the disk                   |
| `normal`  | --          | Face direction                       |

A solid, flat circular disk. Use it for coasters, coin faces, or
circular table surfaces. (It is essentially an annulus with
`inner_radius: 0`.)

---

## 4.10 Triangle and Smooth Triangle

### Flat Triangle

```yaml
- type: "triangle"
  v0: [-1, 0, 0]
  v1: [1, 0, 0]
  v2: [0, 1.5, 0]
  material: "red"
```

Three vertices define a flat triangle with a single surface normal
(computed from the cross product of the edges). The winding order
determines which side is the front face.

### Smooth Triangle

```yaml
- type: "smooth_triangle"
  v0: [-1, 0, 0]
  v1: [1, 0, 0]
  v2: [0, 1.5, 0]
  n0: [-0.3, 0.2, -1]
  n1: [0.3, 0.2, -1]
  n2: [0, 1, -1]
  uv0: [0, 0]
  uv1: [1, 0]
  uv2: [0.5, 1]
  material: "textured"
```

| Parameter | Description                             |
|-----------|-----------------------------------------|
| `v0`, `v1`, `v2` | Vertex positions                |
| `n0`, `n1`, `n2` | Per-vertex normals (for Gouraud interpolation) |
| `uv0`, `uv1`, `uv2` | Per-vertex texture coordinates |

When per-vertex normals are provided, the surface normal is interpolated
smoothly across the face (Gouraud shading), which hides the faceted look
of a triangle mesh. UV coordinates enable proper texture mapping.

You rarely define triangles by hand. They are the building blocks of
meshes loaded from OBJ files (see Section 4.12).

---

## 4.11 Quad (Parallelogram)

```yaml
- type: "quad"
  q: [-1, 0, -1]
  u: [2, 0, 0]
  v: [0, 0, 2]
  material: "checker_floor"
```

| Parameter | Description                                       |
|-----------|---------------------------------------------------|
| `q`       | One corner of the parallelogram                   |
| `u`       | First edge vector (from q)                        |
| `v`       | Second edge vector (from q)                       |

A quad is a flat parallelogram defined by a corner point and two edge
vectors. The four vertices are: `q`, `q+u`, `q+u+v`, `q+v`.

The face normal is `cross(u, v)` (normalized). This matters for emissive
quads: they only emit light in the direction of the normal.

**Example: a floor quad** (2x2 meters centered at origin):

```yaml
- type: "quad"
  q: [-1, 0, -1]
  u: [2, 0, 0]
  v: [0, 0, 2]
  material: "floor"
```

**Example: a wall panel:**

```yaml
- type: "quad"
  q: [-1.5, 0, 3]
  u: [3, 0, 0]
  v: [0, 2.5, 0]
  material: "wall"
```

Quads are extremely useful for light panels (emissive quads), walls,
floors with defined boundaries, and picture frames.

---

## 4.12 Mesh (OBJ Files)

```yaml
- type: "mesh"
  path: "models/teapot.obj"
  material: "porcelain"
  scale: 0.05
  translate: [0, 0, 0]
```

| Parameter | Description                                        |
|-----------|----------------------------------------------------|
| `path`    | Relative path to the OBJ file                      |

The mesh loader reads Wavefront OBJ files. It supports:

- **Triangle faces** (flat shading)
- **Smooth triangle faces** (when vertex normals are present in the OBJ)
- **UV texture coordinates** from the OBJ

On load, the engine automatically builds a BVH for the mesh triangles,
making intersection tests efficient even for meshes with millions of
triangles.

Type aliases: `mesh`, `obj`.

All standard transform fields (`translate`, `rotate`, `scale`) work on
meshes. OBJ models are often exported at vastly different scales, so
`scale` is frequently needed.

### 4.12.1 Subdivision Surfaces (Loop / Catmull-Clark)

When an OBJ is low-poly the renderer can refine it on load using the same
two algorithms shipped by Arnold, RenderMan, Cycles and Pixar's
OpenSubdiv. The result is bound by the limit surface — the smoother
"continuous" geometry the subdivision rules converge to — and the silhouette
becomes fully smooth after a handful of iterations.

```yaml
# Quad mesh → Catmull-Clark
- type: "mesh"
  path: "models/cube.obj"
  material: "porcelain"
  subdivision_scheme: "catmull_clark"
  subdivision_iterations: 3

# Triangle mesh → Loop
- type: "mesh"
  path: "models/icosa.obj"
  material: "copper"
  subdivision_scheme: "loop"
  subdivision_iterations: 4
```

| Field                       | Default | Notes |
|-----------------------------|---------|-------|
| `subdivision_scheme`        | `none`  | `loop`, `catmull_clark`, `auto`, `none`. `auto` picks CC for all-quad input, Loop for all-triangle, CC otherwise. |
| `subdivision_iterations`    | `0`     | Uniform iteration count. Face count roughly ×4 per iteration. |
| `subdivision_pixel_error`   | `0`     | Adaptive target — the loader picks the iteration count that brings the longest projected edge below this many pixels. |
| `subdivision_max_iterations`| `6`     | Hard cap to prevent runaway memory use. |

The loader prints the actual scheme and iteration count it applied:

```
Mesh: smooth_cube — 768 faces, 8 vertices (subdivision: CatmullClark × 3)
```

Behind the scenes the engine builds the limit topology, recomputes
per-vertex normals as the angle-weighted average of incident face normals
(the Blender/Maya default), and then emits the resulting triangles into
the mesh's internal BVH. Source OBJ normals are propagated through the
subdivision steps but overridden at the final triangulation because the
limit surface is smoother than the input.

### 4.12.2 Displacement (see Chapter 3)

> **Since `2026-05`** displacement is declared on the **material**, not
> on the entity (Cycles/RenderMan parity). See **Chapter 3 — Materials,
> "Surface Displacement"** for the full guide (scalar, vector, autobump,
> `displacement_method`, Mix-displacement). The mesh entity only
> accepts `displacement_enabled: false` to bypass the resolved
> material's displacement for that single instance.

The only entity-level parameter that still matters for displacement is
`subdivision_iterations` (above) — geometric displacement needs a
micro-mesh to produce smooth silhouettes, so paired with displaced
materials you typically run `subdivision_iterations: 4–6`.

Vector displacement (`mode: vector`, tangent/object space) and
**autobump** also live under `materials:`. Full documentation with
tables, conventions and showcases is in **Chapter 3 — Materials**.
From this entity all you need is appropriate subdivision and
(optionally) `displacement_enabled: false` to disable displacement
per instance.

---

## 4.13 HeightField (procedural terrain)

A `heightfield` is a continuous surface `y = h(x, z) · height_scale`
over an XZ rectangle. The height comes either from a baked PNG-16
heightmap (e.g. the output of `TerrainGen`, see Chapter 10) or from a
procedural noise sampled at load time onto an internal grid. The engine
intersects it via a min/max mipmap (Tevs/Ihrke/Seidel 2008) — no
tessellation, no mesh BVH, one entity replaces a terrain mesh.

```yaml
- name: "terrain"
  type: "heightfield"
  bounds: [-50, -50, 50, 50]                # [xMin, zMin, xMax, zMax]
  height_scale: 25                          # multiplier for normalised samples
  heightmap_path: "libraries/terrain/myterrain-height.png"
  sea_level: 7.5                            # optional water plane (world Y)
  sea_material: "water"
  strata:
    - { min_altitude: 0.00, max_altitude: 0.18, material: "sand"  }
    - { min_altitude: 0.14, max_altitude: 0.55, material: "grass" }
    - { min_altitude: 0.50, max_altitude: 0.85, min_slope_deg: 25, material: "rock" }
    - { min_altitude: 0.80, max_altitude: 1.00, material: "snow"  }
  material: "grass"                          # fallback when no band wins
```

The **procedural variant** swaps `heightmap_path` for a full
`height_texture` block — any noise type works, but the two Musgrave
multifractals (`hetero_terrain`, `hybrid_multifractal`) are the
canonical eroded-terrain look:

```yaml
- name: "procedural_terrain"
  type: "heightfield"
  bounds: [-50, -50, 50, 50]
  height_scale: 25
  resolution: 512                           # internal sample grid (procedural only)
  height_texture:
    type: "noise"
    noise_type: "hetero_terrain"
    scale: 0.012
    octaves: 5
    lacunarity: 2.0
    fractal_offset: 0.65
  material: "rock"
```

**Strata** is the runtime equivalent of "one mesh per stratum" — every
band declares an altitude/slope window mapped to a material; the
runtime picks the highest-scoring band at each hit. See the **HeightField**
section of the reference for the full schema; `docs/technical/heightfield.md`
covers the algorithm.

Type aliases: `heightfield`, `height_field`, `terrain`.

---

## 4.14 Type Alias Summary

Many primitives have multiple accepted type names:

| Primary name     | Aliases                                |
|------------------|----------------------------------------|
| `sphere`         | --                                     |
| `box`            | --                                     |
| `infinite_plane` | `plane`                                |
| `cylinder`       | --                                     |
| `cone`           | `truncated_cone`, `frustum`            |
| `torus`          | `donut`                                |
| `capsule`        | `pill`                                 |
| `annulus`        | `ring_disk`                            |
| `disk`           | --                                     |
| `triangle`       | --                                     |
| `smooth_triangle`| --                                     |
| `quad`           | --                                     |
| `mesh`           | `obj`                                  |
| `heightfield`    | `height_field`, `terrain`              |
| `group`          | --                                     |
| `instance`       | --                                     |
| `csg`            | --                                     |

---

## 4.15 Complete Example: Shape Gallery

A scene that renders one of each primitive in a row.

```yaml
# shape-gallery.yaml
# Every geometric primitive in a single scene.

world:
  sky:
    type: "flat"
    color: [0.06, 0.06, 0.09]

cameras:
  - name: "main"
    position: [0, 5, -14]
    look_at: [0, 1, 0]
    fov: 55

lights:
  - type: "area"
    corner: [-5, 6, -4]
    u: [10, 0, 0]
    v: [0, 0, 8]
    color: [1, 0.97, 0.93]
    intensity: 25.0

  - type: "point"
    position: [5, 4, -6]
    color: [0.7, 0.8, 1.0]
    intensity: 30.0

materials:
  - id: "floor"
    type: "lambertian"
    color: [0.35, 0.35, 0.35]
  - id: "red"
    type: "disney"
    color: [0.85, 0.12, 0.1]
    roughness: 0.3
  - id: "blue"
    type: "disney"
    color: [0.1, 0.2, 0.85]
    roughness: 0.3
  - id: "green"
    type: "disney"
    color: [0.1, 0.7, 0.15]
    roughness: 0.3
  - id: "orange"
    type: "disney"
    color: [0.9, 0.45, 0.05]
    roughness: 0.3
  - id: "purple"
    type: "disney"
    color: [0.5, 0.1, 0.7]
    roughness: 0.3
  - id: "gold"
    type: "disney"
    color: [1.0, 0.76, 0.33]
    metallic: 1.0
    roughness: 0.1
  - id: "glass"
    type: "dielectric"
    refraction_index: 1.52
  - id: "cyan"
    type: "disney"
    color: [0.1, 0.7, 0.75]
    roughness: 0.3
  - id: "pink"
    type: "disney"
    color: [0.9, 0.3, 0.5]
    roughness: 0.3
  - id: "white"
    type: "disney"
    color: [0.9, 0.88, 0.85]
    roughness: 0.25
    specular: 0.6

entities:
  # Floor
  - type: "infinite_plane"
    point: [0, 0, 0]
    normal: [0, 1, 0]
    material: "floor"

  # Back row (left to right): sphere, box, cylinder, cone, torus
  - type: "sphere"
    center: [-4, 0.7, 2]
    radius: 0.7
    material: "red"

  - type: "box"
    material: "blue"
    scale: [1.2, 1.2, 1.2]
    translate: [-2, 0.6, 2]
    rotate: [0, 25, 0]

  - type: "cylinder"
    center: [0, 0, 2]
    radius: 0.5
    height: 1.3
    material: "green"

  - type: "cone"
    center: [2, 0, 2]
    radius: 0.6
    top_radius: 0.0
    height: 1.4
    material: "orange"

  - type: "torus"
    major_radius: 0.6
    minor_radius: 0.2
    material: "gold"
    translate: [4, 0.2, 2]

  # Front row (left to right): capsule, annulus, disk, quad, truncated cone
  - type: "capsule"
    center: [-4, 0, -1]
    radius: 0.3
    height: 0.7
    material: "purple"

  - type: "annulus"
    center: [-2, 0.01, -1]
    radius: 0.7
    inner_radius: 0.35
    normal: [0, 1, 0]
    material: "gold"

  - type: "disk"
    center: [0, 0.01, -1]
    radius: 0.7
    normal: [0, 1, 0]
    material: "cyan"

  - type: "quad"
    q: [1.3, 0, -1.7]
    u: [1.4, 0, 0]
    v: [0, 1.4, 0]
    material: "pink"

  - type: "cone"
    center: [4, 0, -1]
    radius: 0.6
    top_radius: 0.3
    height: 1.2
    material: "white"
```

Render with:

```
RayTracer -i shape-gallery.yaml -w 1600 -H 700 -s 256 -d 6
```

---

## What You Have Learned

- The engine supports 13 geometric primitives plus groups, instances,
  and CSG operations.
- The **box** is a unit cube -- use transforms for all sizing and
  positioning.
- The **torus** is defined in the XZ plane at the origin.
- **Infinite planes** are for unbounded surfaces (floors, walls).
- **Quads** are for bounded flat surfaces (panels, frames, light
  emitters).
- **Meshes** load OBJ files with automatic BVH construction.
- Most primitives have type aliases for convenience.

---

[Previous: Materials in Depth](./03-materials.md) | [Next: Transforms, Groups, and Scene Organization](./05-transforms-and-groups.md) | [Tutorial Index](./README.md)
