# Chapter 5: Transforms, Groups, and Scene Organization

As scenes grow, you need tools to position objects precisely, compose them
into hierarchies, reuse them efficiently, and split definitions across
files. This chapter covers all of that.

---

## 5.1 The Transform System

Every entity in the `entities:` section (and every child inside a group
or template) supports three transform fields:

```yaml
- type: "box"
  material: "wood"
  translate: [2, 0.5, -1]
  rotate: [0, 45, 0]
  scale: [1.5, 1, 1.5]
```

### translate

```yaml
translate: [x, y, z]
```

Moves the object by the given offset. `[2, 0, 0]` shifts it 2 units to
the right. `[0, 1, 0]` shifts it 1 unit upward.

### rotate

```yaml
rotate: [rx, ry, rz]
```

Rotates the object in **degrees** around each axis. The rotations are
applied in order: **X, then Y, then Z** (intrinsic Euler angles).

- `rotate: [90, 0, 0]` tilts forward (around the X axis).
- `rotate: [0, 45, 0]` turns 45 degrees (around the Y axis).
- `rotate: [0, 0, 30]` rolls (around the Z axis).

### scale

```yaml
scale: [sx, sy, sz]    # Non-uniform scaling
scale: 2.0             # Uniform scaling (same as [2, 2, 2])
```

Scales the object along each axis. You can provide a three-element vector
for non-uniform scaling or a single number for uniform scaling.

- `scale: [2, 1, 1]` stretches the object to twice its width.
- `scale: 0.5` shrinks it to half size in all directions.

### Application Order

Transforms are composed in a fixed order:

**Scale first, then Rotate, then Translate** (SRT).

This means:

1. The object is scaled around its local origin.
2. The scaled object is rotated around its local origin.
3. The rotated, scaled object is moved to its final position.

This order is important. A box scaled to `[4, 0.1, 2]` and then rotated
45 degrees creates a tilted tabletop. If the order were reversed, the
rotation would happen before the scaling, producing a different result.

---

## 5.2 Groups: Hierarchical Composition

A **group** collects multiple entities into a single logical unit. Any
transform applied to the group is inherited by all its children.

```yaml
entities:
  - name: "simple_table"
    type: "group"
    translate: [3, 0, 0]
    material: "oak"
    children:
      # Tabletop
      - type: "box"
        scale: [1.4, 0.06, 0.8]
        translate: [0, 0.74, 0]

      # Four legs
      - type: "cylinder"
        center: [-0.6, 0, -0.3]
        radius: 0.03
        height: 0.74

      - type: "cylinder"
        center: [0.6, 0, -0.3]
        radius: 0.03
        height: 0.74

      - type: "cylinder"
        center: [-0.6, 0, 0.3]
        radius: 0.03
        height: 0.74

      - type: "cylinder"
        center: [0.6, 0, 0.3]
        radius: 0.03
        height: 0.74
```

### How Group Transforms Compose

Each child has its own local transform. The group's transform is applied
**on top of** the child's local transform:

**child_local -> group_transform**

In the example above, the four legs are defined relative to the table's
local origin. The group's `translate: [3, 0, 0]` moves the entire
assembly 3 units to the right.

### Material Inheritance

A group can specify a `material:` that acts as a default for all
children. A child that explicitly sets its own `material:` overrides the
group default.

```yaml
- type: "group"
  material: "oak"           # Default for all children
  children:
    - type: "box"           # Uses "oak" (inherited)
      ...
    - type: "sphere"
      material: "glass"     # Uses "glass" (overrides)
      ...
```

### Nested Groups

Groups can contain other groups, to any depth:

```yaml
- type: "group"
  translate: [0, 0, 0]
  children:
    - type: "group"
      translate: [1, 0, 0]
      children:
        - type: "sphere"
          center: [0, 0.5, 0]
          radius: 0.5
          material: "red"
```

The sphere ends up at `[1, 0.5, 0]` (its own center plus the inner
group's translation).

---

## 5.3 Templates: Reusable Blueprints

If you need the same object in multiple places -- chairs around a table,
trees in a forest, lights along a hallway -- define it once as a
**template** and stamp it into the scene as many **instances** as you
need.

Templates are defined in the `templates:` section and are **not rendered
directly**. They serve as blueprints.

```yaml
templates:
  - name: "candle"
    material: "wax_white"
    children:
      # Body
      - type: "cylinder"
        center: [0, 0, 0]
        radius: 0.015
        height: 0.12

      # Flame (emissive)
      - type: "sphere"
        center: [0, 0.14, 0]
        radius: 0.01
        material: "flame"
```

A template has:

| Field       | Description                                      |
|-------------|--------------------------------------------------|
| `name`      | Unique identifier (referenced by instances)      |
| `children`  | List of child entities (the blueprint geometry)  |
| `material`  | Optional default material for all children       |
| `translate`, `rotate`, `scale` | Optional "default pose" transform |

---

## 5.4 Instances: Stamping Templates into the Scene

```yaml
entities:
  - type: "instance"
    template: "candle"
    translate: [-0.1, 0.78, 0.05]

  - type: "instance"
    template: "candle"
    translate: [0.1, 0.78, -0.05]
    material: "wax_red"        # Override the default material
```

| Field       | Description                                              |
|-------------|----------------------------------------------------------|
| `template`  | Name of the template to instantiate                      |
| `material`  | Override the template's default material (optional)      |
| `translate`, `rotate`, `scale` | Instance-specific transform          |
| `seed`      | Integer for deterministic procedural texture randomization |

### Material Override

When you set `material:` on an instance, it replaces the template's
default material for all children that do not have their own explicit
material. Children that explicitly define a material (like the "flame"
sphere in the candle example) keep theirs.

This lets you define a single chess piece template and instance it in
both white and black simply by overriding the material.

### Transform Composition

The full transform chain is:

**child_local -> template_transform -> instance_transform**

If the template has a `rotate: [0, 90, 0]` and the instance has a
`translate: [5, 0, 0]`, each child is first positioned in the
template's local space, then rotated 90 degrees (template transform),
then moved 5 units right (instance transform).

### Procedural Texture Variation with seed

When a material uses procedural textures with `randomize_offset: true`
or `randomize_rotation: true`, the `seed` on each instance controls the
specific random variation:

```yaml
- type: "instance"
  template: "wooden_plank"
  seed: 1
  translate: [0, 0, 0]

- type: "instance"
  template: "wooden_plank"
  seed: 2
  translate: [1, 0, 0]
```

Each plank gets a unique wood grain pattern even though they share the
same material.

---

## 5.5 YAML Imports: Multi-File Scenes

As scenes grow, keeping everything in one file becomes unwieldy. The
`imports:` section lets you load materials, entities, lights, and
templates from external YAML files.

```yaml
imports:
  - path: "libraries/materials/metals.yaml"
  - path: "libraries/materials/woods.yaml"
  - path: "libraries/lights/studio-3point.yaml"
```

### How Imports Work

1. Paths are resolved **relative to the importing file's directory**.
   If your scene is at `scenes/my-scene.yaml`, the path
   `"libraries/materials/metals.yaml"` resolves to
   `scenes/libraries/materials/metals.yaml`.

2. The imported file can contribute to four sections: `materials`,
   `entities`, `lights`, and `templates`. These are merged into the
   importing scene.

3. **Local definitions win.** If both the imported file and your scene
   define a material with the same `id`, your local version takes
   precedence. This lets you import a library and then override
   specific materials.

4. **World and Camera are NOT imported.** The main scene file always
   owns the world settings and camera definitions.

5. **Nested imports are supported.** An imported file can itself contain
   `imports:` that reference other files.

6. **Circular import protection** is built in. If file A imports file B
   and file B imports file A, the engine detects the cycle and skips
   the second import.

### Example: Overriding an Imported Material

```yaml
imports:
  - path: "libraries/materials/metals.yaml"   # Defines "dis_oro_lucido"

materials:
  # Override the library's gold with a custom version
  - id: "dis_oro_lucido"
    type: "disney"
    color: [0.95, 0.7, 0.2]     # Slightly different gold
    metallic: 1.0
    roughness: 0.1
```

---

## 5.6 Best Practices for Scene Organization

**Small scenes (< 50 entities):** A single YAML file is fine.

**Medium scenes (50--200 entities):**
- Import material libraries rather than defining every material inline.
- Use templates for repeated objects.

**Large scenes (200+ entities):**
- Split into multiple files: one for materials, one for object
  templates, one for the main scene layout.
- Use the library ecosystem (Chapter 10).
- Give every entity and template a descriptive `name:`.
- Use consistent naming conventions (e.g. prefix material IDs with a
  category: `mat_floor`, `mat_wall`, `mat_glass`).

---

## 5.7 Complete Example: The Dinner Table

A scene that uses templates, groups, instances, and imports together.

```yaml
# dinner-table.yaml
# A dining table with four place settings, demonstrating templates,
# groups, instances, and material override.

world:
  sky:
    type: "flat"
    color: [0.01, 0.01, 0.02]

cameras:
  - name: "main"
    position: [0, 3.5, -4]
    look_at: [0, 0.78, 0]
    fov: 50

lights:
  # Warm overhead area light
  - type: "area"
    corner: [-0.6, 2.5, -0.4]
    u: [1.2, 0, 0]
    v: [0, 0, 0.8]
    color: [1.0, 0.92, 0.78]
    intensity: 40.0

  # Cool fill from the side
  - type: "point"
    position: [3, 2, -2]
    color: [0.7, 0.8, 1.0]
    intensity: 15.0

materials:
  # Table
  - id: "dark_wood"
    type: "disney"
    roughness: 0.25
    clearcoat: 0.5
    clearcoat_gloss: 0.85
    texture:
      type: "wood"
      scale: 6.0
      noise_strength: 1.2
      colors: [[0.35, 0.2, 0.1], [0.22, 0.12, 0.06]]

  # Porcelain plate
  - id: "porcelain"
    type: "disney"
    color: [0.95, 0.93, 0.88]
    roughness: 0.12
    specular: 0.7
    subsurface: 0.2

  # Steel cutlery
  - id: "steel"
    type: "disney"
    color: [0.75, 0.75, 0.78]
    metallic: 1.0
    roughness: 0.15

  # Crystal glass
  - id: "crystal"
    type: "dielectric"
    refraction_index: 1.65
    color: [0.98, 0.98, 1.0]

  # Table cloth (optional accent)
  - id: "linen"
    type: "disney"
    color: [0.88, 0.85, 0.78]
    roughness: 0.7
    sheen: 0.3

  # Floor
  - id: "floor"
    type: "lambertian"
    color: [0.25, 0.22, 0.2]

# ── Templates ────────────────────────────────────────────────────────

templates:
  # A simple plate (disk + torus rim)
  - name: "plate"
    material: "porcelain"
    children:
      - type: "disk"
        center: [0, 0, 0]
        radius: 0.12
        normal: [0, 1, 0]
      - type: "torus"
        major_radius: 0.12
        minor_radius: 0.008

  # A wine glass (simplified: stem + bowl)
  - name: "wine_glass"
    material: "crystal"
    children:
      # Base
      - type: "disk"
        center: [0, 0.001, 0]
        radius: 0.035
        normal: [0, 1, 0]
      # Stem
      - type: "cylinder"
        center: [0, 0, 0]
        radius: 0.005
        height: 0.1
      # Bowl
      - type: "sphere"
        center: [0, 0.13, 0]
        radius: 0.045

  # A fork (simplified)
  - name: "fork"
    material: "steel"
    children:
      - type: "box"
        scale: [0.01, 0.003, 0.12]
        translate: [0, 0.002, 0]

  # A place setting (plate + glass + fork) as a group
  - name: "place_setting"
    children:
      - type: "instance"
        template: "plate"
        translate: [0, 0, 0]
      - type: "instance"
        template: "wine_glass"
        translate: [0.08, 0, -0.1]
      - type: "instance"
        template: "fork"
        translate: [-0.15, 0, 0]

# ── Scene ────────────────────────────────────────────────────────────

entities:
  # Floor
  - type: "infinite_plane"
    point: [0, 0, 0]
    normal: [0, 1, 0]
    material: "floor"

  # Table (group: tabletop + 4 legs)
  - name: "table"
    type: "group"
    material: "dark_wood"
    children:
      - type: "box"
        scale: [1.4, 0.05, 0.9]
        translate: [0, 0.76, 0]
      - type: "cylinder"
        center: [-0.6, 0, -0.35]
        radius: 0.035
        height: 0.76
      - type: "cylinder"
        center: [0.6, 0, -0.35]
        radius: 0.035
        height: 0.76
      - type: "cylinder"
        center: [-0.6, 0, 0.35]
        radius: 0.035
        height: 0.76
      - type: "cylinder"
        center: [0.6, 0, 0.35]
        radius: 0.035
        height: 0.76

  # Four place settings around the table
  - type: "instance"
    template: "place_setting"
    translate: [-0.35, 0.79, -0.25]

  - type: "instance"
    template: "place_setting"
    translate: [0.35, 0.79, -0.25]

  - type: "instance"
    template: "place_setting"
    translate: [-0.35, 0.79, 0.25]
    rotate: [0, 180, 0]

  - type: "instance"
    template: "place_setting"
    translate: [0.35, 0.79, 0.25]
    rotate: [0, 180, 0]
```

Render with:

```
RayTracer -i dinner-table.yaml -w 1200 -H 800 -s 256 -d 6
```

---

## What You Have Learned

- **translate**, **rotate**, and **scale** position any entity; the
  order is always Scale -> Rotate -> Translate.
- **Groups** compose multiple entities into a movable unit with
  inherited transforms and materials.
- **Templates** define reusable blueprints; **instances** stamp them
  into the scene with optional material overrides.
- **Imports** merge external YAML files; local definitions override
  imported ones on ID collision.
- Templates can reference other templates (a place setting that
  instances a plate, glass, and fork).

---

[Previous: All the Shapes](./04-geometric-primitives.md) | [Next: Lighting Mastery](./06-lighting.md) | [Tutorial Index](./README.md)
