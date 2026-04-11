# Tutorial 03: Advanced Techniques

In this module, we will explore how to manage complexity in large-scale scenes, how to reuse objects, and how to create complex shapes via boolean operations (CSG) or external models (OBJ).

---

## 1. Groups (Hierarchies)
Groups allow you to compose multiple objects together and move them as a single entity. Any transformation (translate, rotate, scale) applied to the group is inherited by its children.

```yaml
- name: "table"
  type: "group"
  translate: [5, 0, 0]
  children:
    - type: "box"    # The tabletop
      scale: [2, 0.1, 1]
      translate: [0, 0.75, 0]
    - type: "cylinder" # A leg
      center: [-0.9, 0, -0.4]
      radius: 0.05
      height: 0.75
```

## 2. Templates and Instances (DRY)
If you need to use the same object many times (e.g., chairs in a theater or trees in a forest), use **templates**. It keeps your YAML readable and easy to maintain.

```yaml
templates:
  - name: "chair"
    children:
       # ... chair definition ...

entities:
  - type: "instance"
    template: "chair"
    translate: [0, 0, 0]
  - type: "instance"
    template: "chair"
    translate: [2, 0, 0]
    material: "red_plastic"  # You can override materials!
```

## 3. YAML Imports (Modularity)
You can split your scene into multiple files. Load material libraries, lighting setups, or collections of objects with a single line:

```yaml
imports:
  - path: "libraries/materials/metals.yaml"
  - path: "libraries/objects/furniture.yaml"
```

> [!TIP]
> Check the `scenes/libraries/` folder to discover hundreds of high-quality materials and objects ready to use!

## 4. CSG (Constructive Solid Geometry)
CSG allows you to model objects by "sculpting" shapes:
- **Union**: Merges two solids.
- **Intersection**: Keeps only the overlapping part.
- **Subtraction**: Subtracts one shape from another (e.g., a hole in a wall).

```yaml
- type: "csg"
  operation: "subtraction"
  left: { type: "box", scale: [2, 2, 0.1] } # The wall
  right: { type: "sphere", radius: 0.5 }    # The spherical hole
```

## 5. Mesh (OBJ Models)
Want to import a model created in Blender? Use the `mesh` type:

```yaml
- name: "statue"
  type: "mesh"
  path: "models/statue.obj"
  material: "marble"
  scale: 0.1
```
The engine loads the file and automatically creates an internal acceleration structure (BVH) to make rendering lightning-fast even with millions of triangles.

---

[Go to Tutorial 04: Preset Catalog](./04-preset-catalog.md) | [Tutorial Index](../tutorial/)
