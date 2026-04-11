# Tutorial 04: Preset Catalog and Libraries

The 3D-Ray engine comes with a vast collection of ready-to-use assets located in the `scenes/libraries/` folder. This catalog allows you to create professional scenes without having to define every single material or object from scratch.

---

## 1. Material Library
You will find hundreds of PBR materials based on the **Disney BSDF** model, categorized by type:

- **Metals (`materials/metals.yaml`)**: Gold, Silver, Copper, Steel (in polished, satin, and brushed variants).
- **Glass (`materials/glass.yaml`)**: Crystal, Colored glass, Frosted glass, Liquids.
- **Natural (`materials/organics.yaml`)**: Woods (Walnut, Oak, Mahogany), Veined marbles, Stones, Terrain.
- **Plastics and Paints (`materials/plastics.yaml`)**: Black ABS, Metallic car paint, Rubber.

### Usage Example:
```yaml
imports:
  - path: "libraries/materials/metals.yaml"
entities:
  - type: "sphere"
    material: "dis_oro_lucido"  # 'dis_' prefix for Disney PBR materials
```

---

## 2. Object Library (Templates)
Over 150 complex objects built using primitives and CSG. Browse the `libraries/objects/` folder to discover:

- **Furniture (`furniture.yaml`)**: Tables, chairs, shelves, lamps.
- **Jewelry (`jewelry.yaml`)**: Rings, brilliant-cut gems, diamonds.
- **Science (`laboratory.yaml`)**: Test tubes, flasks, microscopes.
- **Chess (`chess.yaml`)**: Complete Staunton set with boards.
- **Industry (`mechanical.yaml`)**: Gears, bolts, pistons.

### Usage Example:
```yaml
imports:
  - path: "libraries/objects/chess.yaml"
entities:
  - type: "instance"
    template: "re_staunton"
    material: "dis_oro_lucido"  # You can dip the king in gold!
```

---

## 3. Lighting Setups
Don't waste time positioning every single light. Use the predefined sets in `libraries/lights/`:

- **Studio 3-Point**: The classic setup for portraits and products (Key, Fill, Rim).
- **Global Illumination**: Sets for bright outdoor and indoor environments.
- **Starter Kits**: Complete scenes (e.g., `starter-material-showroom.yaml`) that you can use as a base for your own experiments.

---

## 4. How to Explore the Libraries
Every folder in the library contains a detailed `README.md` file with a complete list of all material IDs and available template names.

> [!IMPORTANT]
> Remember that you can always **override** a library material by defining it with the same `id` in your local scene file.

---

[Back to Tutorial Index](../tutorial/) | [Go to Scene Reference Guide](../../docs/reference/scene-reference.md)
