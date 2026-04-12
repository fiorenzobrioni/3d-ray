# Chapter 8: Constructive Solid Geometry (CSG)

Sometimes the shapes from Chapter 4 are not enough. You need a sphere
with a hole through it, or a cube with rounded edges, or a wall with an
arched window. **Constructive Solid Geometry** (CSG) lets you create
complex shapes by combining simple primitives with Boolean operations.

---

## 8.1 The Three Boolean Operations

CSG works with three operations:

| Operation       | Aliases                        | Result                              |
|-----------------|--------------------------------|-------------------------------------|
| `union`         | --                             | Combined volume of both shapes      |
| `intersection`  | --                             | Only the overlapping volume         |
| `subtraction`   | `subtract`, `difference`       | Left shape minus the right shape    |

Every CSG entity has a `left` child and a `right` child. Each child is
an inline entity definition (any primitive, group, or even another CSG).

```yaml
- type: "csg"
  operation: "subtraction"
  left: { ... }
  right: { ... }
  material: "default_material"
```

---

## 8.2 Union: Merging Shapes

Union produces the combined volume of two shapes. Where they overlap,
the interior is merged into a single solid.

```yaml
# Snowman: three stacked spheres
- name: "snowman"
  type: "csg"
  operation: "union"
  material: "snow"
  left:
    type: "csg"
    operation: "union"
    left:
      type: "sphere"
      center: [0, 0.5, 0]
      radius: 0.5
    right:
      type: "sphere"
      center: [0, 1.2, 0]
      radius: 0.35
  right:
    type: "sphere"
    center: [0, 1.75, 0]
    radius: 0.25
```

Union is useful when you want to treat multiple shapes as a single
solid -- for example, when you later want to subtract something from the
combined form, or when you need a single material to cover a seamless
shape.

---

## 8.3 Intersection: Keeping the Overlap

Intersection keeps only the volume where both shapes exist
simultaneously. Everything else is discarded.

```yaml
# Lens: intersection of two overlapping spheres
- name: "lens"
  type: "csg"
  operation: "intersection"
  material: "glass"
  left:
    type: "sphere"
    center: [0, 1, -0.3]
    radius: 0.8
  right:
    type: "sphere"
    center: [0, 1, 0.3]
    radius: 0.8
```

The two spheres overlap in the middle, creating a lens-shaped volume.
The amount of overlap (controlled by the distance between centers
relative to the radii) determines the thickness of the lens.

### Another Example: Rounded Cube

Intersect a box with a sphere to round the cube's corners:

```yaml
- name: "rounded_cube"
  type: "csg"
  operation: "intersection"
  material: "white_plastic"
  left:
    type: "box"
    scale: [1.4, 1.4, 1.4]
    translate: [0, 1, 0]
  right:
    type: "sphere"
    center: [0, 1, 0]
    radius: 1.0
```

---

## 8.4 Subtraction: Carving Holes

Subtraction removes the volume of the right shape from the left shape.
The result is the left shape with the right shape carved out.

```yaml
# Sphere with a cylindrical hole through it
- name: "pierced_sphere"
  type: "csg"
  operation: "subtraction"
  material: "marble"
  left:
    type: "sphere"
    center: [0, 1, 0]
    radius: 1.0
  right:
    type: "cylinder"
    center: [0, 0, 0]
    radius: 0.3
    height: 3.0
```

The cylinder is taller than the sphere, ensuring it passes completely
through. The result is a sphere with a clean cylindrical tunnel.

> **Important:** Order matters! `left - right` is not the same as
> `right - left`. The left shape is the one that survives; the right
> shape is the one that carves.

### Example: Arched Doorway in a Wall

```yaml
- name: "wall_with_arch"
  type: "csg"
  operation: "subtraction"
  material: "stone"
  left:
    type: "box"
    scale: [4, 3, 0.3]
    translate: [0, 1.5, 0]
  right:
    type: "csg"
    operation: "union"
    left:
      type: "box"
      scale: [1.0, 1.8, 0.5]
      translate: [0, 0.9, 0]
    right:
      type: "sphere"
      center: [0, 1.8, 0]
      radius: 0.5
```

This creates a rectangular wall with an arched opening: a rectangular
door slot topped by a half-sphere dome, all subtracted from the wall.

---

## 8.5 Per-Child Materials

Each CSG child can have its own material. Children without an explicit
material inherit the parent CSG's `material:`.

```yaml
- type: "csg"
  operation: "subtraction"
  material: "white_marble"          # Fallback for children without material
  left:
    type: "sphere"
    center: [0, 1, 0]
    radius: 1.0
    material: "white_marble"        # Explicit: outer surface is marble
  right:
    type: "cylinder"
    center: [0, 0, 0]
    radius: 0.3
    height: 3.0
    material: "polished_gold"       # The inner surface of the hole is gold
```

When you subtract, the interior surfaces that are "exposed" by the
subtraction inherit their material from the right child. This lets you
create objects with different materials on the outside and inside -- like
a geode or a chocolate shell.

---

## 8.6 Per-Child Transforms

Each child supports `translate`, `rotate`, and `scale`:

```yaml
- type: "csg"
  operation: "subtraction"
  material: "steel"
  left:
    type: "box"
    scale: [2, 2, 2]
    translate: [0, 1, 0]
  right:
    type: "cylinder"
    center: [0, 0, 0]
    radius: 0.4
    height: 3.0
    rotate: [90, 0, 0]        # Horizontal hole through the cube
    translate: [0, 1, 0]
```

Transforms are applied to each child independently before the Boolean
operation is performed. The parent CSG entity can also have its own
transforms, which are applied to the entire result.

---

## 8.7 Nested CSG: Complex Boolean Trees

Because a CSG child can itself be a CSG entity, you can build arbitrarily
complex shapes through nesting.

### Example: Cube with Three Perpendicular Holes

```yaml
- name: "drilled_cube"
  type: "csg"
  operation: "subtraction"
  material: "steel"
  translate: [0, 1.2, 0]
  left:
    type: "box"
    scale: [2, 2, 2]
  right:
    type: "csg"
    operation: "union"
    left:
      type: "csg"
      operation: "union"
      left:
        # Hole along Y axis
        type: "cylinder"
        center: [0, -1.5, 0]
        radius: 0.4
        height: 3.0
      right:
        # Hole along X axis
        type: "cylinder"
        center: [0, 0, 0]
        radius: 0.4
        height: 3.0
        rotate: [0, 0, 90]
    right:
      # Hole along Z axis
      type: "cylinder"
      center: [0, 0, 0]
      radius: 0.4
      height: 3.0
      rotate: [90, 0, 0]
```

This creates a solid cube with three cylindrical tunnels drilled through
it along each axis. The approach is:

1. Union the three cylinders into a single "drill" shape.
2. Subtract the combined drill from the cube.

### Example: A Simple Goblet

```yaml
- name: "goblet"
  type: "csg"
  operation: "union"
  material: "crystal"
  left:
    # Bowl: sphere with top cut off and interior hollowed
    type: "csg"
    operation: "subtraction"
    left:
      type: "csg"
      operation: "subtraction"
      left:
        type: "sphere"
        center: [0, 1.5, 0]
        radius: 0.6
      right:
        type: "sphere"
        center: [0, 1.5, 0]
        radius: 0.55
    right:
      # Cut off the top
      type: "box"
      scale: [2, 1, 2]
      translate: [0, 2.2, 0]
  right:
    # Stem + base
    type: "csg"
    operation: "union"
    left:
      type: "cylinder"
      center: [0, 0, 0]
      radius: 0.06
      height: 1.0
    right:
      type: "torus"
      major_radius: 0.3
      minor_radius: 0.06
```

---

## 8.8 Tips and Pitfalls

1. **Shapes must overlap.** An intersection between two shapes that do
   not touch produces nothing. A subtraction of a shape that is entirely
   outside the left child has no effect.

2. **Make the carving shape larger.** When subtracting, extend the right
   child well beyond the surface of the left child. A cylinder that is
   exactly as tall as a cube may produce thin artifacts at the edges.
   Make it 1.5--2x larger than needed.

3. **CSG is expensive.** The engine must test all intersections on both
   children (up to 16 hits per child per ray). Use CSG for hero objects,
   not for filling a scene with hundreds of identical CSG shapes.
   Instead, define the CSG object once as a template and instance it.

4. **Subtraction order matters.** `A - B` is different from `B - A`.
   The left child is always the "positive" shape that survives.

5. **Combine with transforms.** The parent CSG entity supports
   `translate`, `rotate`, and `scale` just like any other entity. This
   lets you position and orient the finished CSG result without
   modifying the children.

---

## 8.9 Complete Example: The Sculptor's Workshop

```yaml
# sculptor-workshop.yaml
# Several CSG objects demonstrating union, intersection, and subtraction.

world:
  ambient_light: [0.02, 0.02, 0.03]
  background: [0.04, 0.04, 0.06]

camera:
  position: [0, 3, -8]
  look_at: [0, 1.2, 0]
  fov: 50

materials:
  - id: "floor"
    type: "lambertian"
    color: [0.3, 0.28, 0.25]
  - id: "marble"
    type: "disney"
    color: [0.92, 0.90, 0.86]
    roughness: 0.12
    specular: 0.7
  - id: "glass"
    type: "dielectric"
    refraction_index: 1.52
  - id: "steel"
    type: "disney"
    color: [0.7, 0.7, 0.72]
    metallic: 1.0
    roughness: 0.15
  - id: "gold_inside"
    type: "disney"
    color: [1.0, 0.76, 0.33]
    metallic: 1.0
    roughness: 0.05
  - id: "stone"
    type: "disney"
    roughness: 0.6
    texture:
      type: "marble"
      scale: 8.0
      noise_strength: 4.0
      colors: [[0.85, 0.82, 0.78], [0.5, 0.48, 0.44]]

entities:
  - type: "infinite_plane"
    point: [0, 0, 0]
    normal: [0, 1, 0]
    material: "floor"

  # 1. Lens (intersection) -- far left
  - name: "lens"
    type: "csg"
    operation: "intersection"
    material: "glass"
    translate: [-3, 1.2, 0]
    left:
      type: "sphere"
      center: [0, 0, -0.25]
      radius: 0.8
    right:
      type: "sphere"
      center: [0, 0, 0.25]
      radius: 0.8

  # 2. Pierced sphere (subtraction) -- center-left
  - name: "pierced_sphere"
    type: "csg"
    operation: "subtraction"
    translate: [-1, 1.2, 0]
    left:
      type: "sphere"
      center: [0, 0, 0]
      radius: 0.7
      material: "marble"
    right:
      type: "cylinder"
      center: [0, -1, 0]
      radius: 0.25
      height: 2.5
      material: "gold_inside"

  # 3. Perforated wall (subtraction) -- center
  - name: "perforated_wall"
    type: "csg"
    operation: "subtraction"
    material: "stone"
    translate: [1, 1.2, 0]
    left:
      type: "box"
      scale: [1.6, 1.8, 0.25]
    right:
      type: "csg"
      operation: "union"
      left:
        type: "sphere"
        center: [0, 0.3, 0]
        radius: 0.3
      right:
        type: "csg"
        operation: "union"
        left:
          type: "sphere"
          center: [-0.35, -0.3, 0]
          radius: 0.2
        right:
          type: "sphere"
          center: [0.35, -0.3, 0]
          radius: 0.2

  # 4. Drilled cube (nested subtraction) -- far right
  - name: "drilled_cube"
    type: "csg"
    operation: "subtraction"
    material: "steel"
    translate: [3, 1.2, 0]
    left:
      type: "box"
      scale: [1.2, 1.2, 1.2]
    right:
      type: "csg"
      operation: "union"
      left:
        type: "csg"
        operation: "union"
        left:
          type: "cylinder"
          center: [0, -1, 0]
          radius: 0.3
          height: 2.5
        right:
          type: "cylinder"
          center: [0, 0, 0]
          radius: 0.3
          height: 2.5
          rotate: [0, 0, 90]
      right:
        type: "cylinder"
        center: [0, 0, 0]
        radius: 0.3
        height: 2.5
        rotate: [90, 0, 0]

lights:
  - type: "area"
    corner: [-4, 5, -3]
    u: [8, 0, 0]
    v: [0, 0, 6]
    color: [1, 0.97, 0.93]
    intensity: 30.0
    shadow_samples: 16

  - type: "point"
    position: [4, 3, -5]
    color: [0.75, 0.82, 1.0]
    intensity: 25.0
```

Render with:

```
3d-ray -i sculptor-workshop.yaml -w 1600 -H 700 -s 64 -d 30
```

---

## What You Have Learned

- **Union** merges two shapes into one solid.
- **Intersection** keeps only where both shapes overlap.
- **Subtraction** carves the right shape out of the left (order
  matters!).
- Each CSG child can have its own material and transforms.
- CSG operations nest to any depth for complex shapes.
- Make carving shapes oversized to avoid thin-surface artifacts.
- Use CSG for hero objects; instance the result for multiple copies.

---

[Previous: Sky, Environment, and Camera Effects](./07-sky-environment-camera.md) | [Next: Participating Media (Volumetrics)](./09-volumetrics.md) | [Tutorial Index](./README.md)
