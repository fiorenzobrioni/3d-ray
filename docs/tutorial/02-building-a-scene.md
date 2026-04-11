# Tutorial 02: Building a Scene (Hands-On)

In this tutorial, we move from theory to practice. We will build a classic scene together: a **red metallic sphere** resting on a **marble floor**, all illuminated by studio lights.

By the end, you will have a complete YAML file that you can render to see the result.

---

## 1. File Backbone
Every scene starts with the world and camera definition. Create a new file named `still-life.yaml`.

```yaml
world:
  ambient_light: [0.05, 0.05, 0.06]  # Subtle global ambient light
  background: [0.1, 0.1, 0.12]       # Background color (almost black)

cameras:
  - name: "main"
    position: [0, 2, -5]             # Positioned high and back
    look_at: [0, 1, 0]                # Points towards the center of the scene
    fov: 40                          # Field of view (zoom)
```

## 2. Defining Materials
Before creating objects, we need to define how they appear. We will use the **Disney (PBR)** material for maximum realism.

```yaml
materials:
  - id: "marble_floor"
    type: "disney"
    color: [0.9, 0.9, 0.9]
    roughness: 0.1                   # Very smooth/reflective
    specular: 0.8
    texture:                         # Add procedural veins
      type: "marble"
      scale: 15.0

  - id: "red_metal"
    type: "disney"
    color: [0.8, 0.1, 0.1]
    metallic: 1.0                    # Metallic behavior
    roughness: 0.15                  # Some blur in reflection
```

## 3. Adding Objects (Entities)
Now we place the floor and our main sphere.

```yaml
entities:
  # Floor (an infinite plane)
  - name: "floor"
    type: "infinite_plane"
    point: [0, 0, 0]
    normal: [0, 1, 0]
    material: "marble_floor"

  # Sphere
  - name: "main_sphere"
    type: "sphere"
    center: [0, 1, 0]                # Resting on the floor (Y=1 since radius=1)
    radius: 1.0
    material: "red_metal"
```

## 4. Lighting
Without lights, the scene will be black. Let's add a main light (Key Light) and a fill light.

```yaml
lights:
  - type: "area"                     # Rectangular light for soft shadows
    corner: [-2, 4, -2]
    u: [4, 0, 0]
    v: [0, 0, 4]
    color: [1, 1, 1]
    intensity: 15.0
    shadow_samples: 16

  - type: "point"                    # A small light to brighten the shadows
    position: [3, 2, -2]
    color: [0.8, 0.8, 1.0]           # Slightly bluish
    intensity: 3.0
```

---

## 5. Final Render
Save the file and run the command from the terminal:

```powershell
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- -i still-life.yaml -o output/test.png -s 64
```

### What did we learn?
- Objects are defined in the `entities` section.
- Visual appearance is controlled by `materials`.
- The **Disney** material is the most versatile for achieving photorealistic results.
- **Area Lights** produce much nicer shadows than Point Lights, but require more samples (`-s`).

---

> [!TIP]
> For a complete list of all available geometries and materials, check the [Scene Reference Guide](../../docs/reference/scene-reference.md).

[Go to Tutorial 03: Advanced Techniques](./03-advanced-techniques.md) | [Tutorial Index](../tutorial/)
