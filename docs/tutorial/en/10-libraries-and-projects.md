# Chapter 10: Asset Libraries and Complete Scenes

3D-Ray ships with a rich ecosystem of pre-built assets: over 800
materials, 154 object templates, 14 lighting presets, and 18 complete
starter-kit scenes. This chapter shows you how to use them, gives you the
full CLI reference, and walks through building a real project.

---

## 10.1 The Library Ecosystem

All libraries live in the `scenes/libraries/` directory:

```
scenes/libraries/
  materials/      12 YAML files, 800+ materials
  objects/        12 YAML files, 154+ templates
  lights/         14 YAML files, lighting presets
  starter-kits/   18 YAML files, complete scenes
  textures/       20 PNG image files (albedo + normal maps)
```

Libraries are loaded via the `imports:` section in your scene file. Paths
are relative to your scene file's directory.

---

## 10.2 Material Libraries

Twelve themed files covering every surface type you are likely to need:

| File                      | Contents                                       | Count |
|---------------------------|-------------------------------------------------|-------|
| `materials/metals.yaml`   | Gold, silver, copper, bronze, brass, steel, aluminum, titanium, chrome, platinum, nickel, zinc, tin, corten | ~120 |
| `materials/ceramics.yaml` | Porcelain, majolica, terracotta, stoneware, raku, celadon, glazed | ~67 |
| `materials/woods.yaml`    | Oak, walnut, maple, teak, ebony, mahogany (raw, oiled, waxed, varnished, lacquered) | ~85 |
| `materials/stones.yaml`   | Marble, granite, slate, travertine, basalt, sandstone, concrete, brick | ~87 |
| `materials/glasses.yaml`  | Industrial glass, crystal, colored glass, frosted, gemstones, liquids, resins | ~96 |
| `materials/plastics.yaml` | ABS, polycarbonate, acrylic, PVC, nylon, rubber, silicone, 3D-print | ~95 |
| `materials/fabrics.yaml`  | Velvet, silk, cotton, linen, wool, denim, leather, lace | ~100 |
| `materials/paints.yaml`   | Auto paint, lacquer, enamel, chalk paint, powder coat | ~98 |
| `materials/organics.yaml` | Wax, amber, ivory, horn, cork, paper, soap, bamboo | ~81 |
| `materials/foods.yaml`    | Chocolate, fruit, cheese, bread, candy, butter | ~91 |
| `materials/emissives.yaml`| LED, incandescent, fluorescent, neon, flames, screens, lava | ~83 |
| `materials/grounds.yaml`  | Checker floors, parquet, tiles, marble floors, earth, sand, grass, carpet | ~66 |

### Naming Convention

Materials follow a prefix system:

- **`dis_`** -- Disney BSDF (full PBR with clearcoat, sheen, subsurface,
  spec_trans). Best for hero objects and close-ups.
- **`cls_`** -- Classic type (lambertian, metal, or dielectric). Faster
  and less noisy; best for large surfaces and backgrounds.

Examples:
- `dis_oro_lucido` -- Disney polished gold
- `cls_oro_lucido` -- Classic metal polished gold
- `dis_vetro_sodalime` -- Disney soda-lime glass
- `cls_vetro_sodalime` -- Classic dielectric glass

### Usage

```yaml
imports:
  - path: "libraries/materials/metals.yaml"
  - path: "libraries/materials/glasses.yaml"

entities:
  - type: "sphere"
    center: [0, 1, 0]
    radius: 1.0
    material: "dis_oro_lucido"      # Use the library material by ID

  - type: "sphere"
    center: [2, 0.5, 0]
    radius: 0.5
    material: "cls_diamante"        # Classic diamond
```

### Overriding a Library Material

To customize a library material, redefine it with the same ID in your
scene file. Local definitions take precedence:

```yaml
imports:
  - path: "libraries/materials/metals.yaml"

materials:
  # My custom gold (overrides the library's dis_oro_lucido)
  - id: "dis_oro_lucido"
    type: "disney"
    color: [0.98, 0.8, 0.3]
    metallic: 1.0
    roughness: 0.08
```

---

## 10.3 Object Libraries

Twelve themed files with pre-built templates using primitives, groups,
and CSG:

| File                           | Templates | Example objects                    |
|--------------------------------|-----------|------------------------------------|
| `objects/furniture.yaml`       | 10        | Tables, chairs, lamps, shelves     |
| `objects/decorative-objects.yaml`| 10      | Vases, frames, candles, clocks     |
| `objects/tableware.yaml`       | 10        | Plates, glasses, cutlery, teapots  |
| `objects/architecture.yaml`    | 14        | Columns (Doric/Ionic), arches, stairs |
| `objects/mechanical.yaml`      | 14        | Gears, bolts, pistons, bearings    |
| `objects/jewelry.yaml`         | 14        | Rings, necklaces, gems, tiara      |
| `objects/lighting.yaml`        | 14        | Chandeliers, pendants, sconces     |
| `objects/laboratory.yaml`      | 14        | Test tubes, flasks, microscope     |
| `objects/musical.yaml`         | 14        | Violin, guitar, piano, drums       |
| `objects/outdoor.yaml`         | 14        | Benches, fountains, planters       |
| `objects/chess.yaml`           | 11        | Full Staunton set + boards         |
| `objects/nature.yaml`          | 15        | Trees, flowers, mushrooms, crystals|

### Material Prefixes per Library

Each library uses a unique prefix for its embedded materials to avoid
collisions:

| Library             | Prefix  |
|---------------------|---------|
| furniture           | `frn_`  |
| decorative-objects  | `dec_`  |
| tableware           | `tbw_`  |
| architecture        | `arc_`  |
| mechanical          | `mec_`  |
| jewelry             | `jwl_`  |
| lighting            | `lit_`  |
| laboratory          | `lab_`  |
| musical             | `mus_`  |
| outdoor             | `out_`  |
| chess               | `chs_`  |
| nature              | `nat_`  |

### Conventions

All templates follow consistent rules:

- **Base at Y=0.** Every template sits on the ground when placed with
  `translate: [x, 0, z]`.
- **Centered in XZ.** The origin is at the geometric center.
- **1:1 scale in meters.** A table is ~1.4 m wide; a chair is ~0.9 m
  tall.

### Usage

```yaml
imports:
  - path: "libraries/objects/furniture.yaml"
  - path: "libraries/materials/metals.yaml"

entities:
  # Place a table at the origin
  - type: "instance"
    template: "tavolo_classico"
    translate: [0, 0, 0]

  # Place a chair, override material to gold metal
  - type: "instance"
    template: "sedia_classica"
    translate: [0.7, 0, -0.4]
    rotate: [0, -30, 0]
    material: "dis_oro_lucido"
```

Material override on an instance replaces the template's default
material. Children with their own explicit material (like an emissive
light bulb inside a lamp) keep their original material.

---

## 10.4 Lighting Libraries

Fourteen preset lighting setups organized by environment:

### Studio

| File                    | Setup                | Atmosphere                 |
|-------------------------|----------------------|----------------------------|
| `lights/studio-3point.yaml` | Classic 3-point   | Universal product/portrait |
| `lights/studio-highkey.yaml` | High key         | Clean, commercial, fashion |
| `lights/studio-dramatic.yaml`| Low key/Chiaroscuro| Noir, dramatic shadows   |
| `lights/studio-product.yaml` | Product/Jewelry  | Precise catchlights       |

### Outdoor

| File                           | Setup          | Atmosphere                |
|--------------------------------|----------------|---------------------------|
| `lights/outdoor-noon.yaml`     | Noon sun       | Hard light, short shadows |
| `lights/outdoor-golden-hour.yaml`| Golden hour  | Warm cinematic glow       |
| `lights/outdoor-sunset.yaml`   | Sunset         | Deep orange, long shadows |
| `lights/outdoor-overcast.yaml` | Overcast       | Soft, even, no hard shadows|

### Night / Interior / Creative

| File                             | Setup           | Atmosphere               |
|----------------------------------|-----------------|--------------------------|
| `lights/night-moonlight.yaml`    | Moonlit night   | Blue-cold, mysterious    |
| `lights/interior-warm.yaml`      | Warm interior   | Cozy, domestic           |
| `lights/interior-candlelight.yaml`| Candlelight    | Romantic, medieval       |
| `lights/neon-cyberpunk.yaml`     | Neon/Cyberpunk  | Sci-fi, vibrant colors   |
| `lights/theatre-stage.yaml`      | Theatre stage   | Dramatic spots           |
| `lights/museum-gallery.yaml`     | Museum gallery  | Precise exhibit spots    |

### Usage

```yaml
imports:
  - path: "libraries/lights/studio-3point.yaml"

# Each light library suggests a matching world config in its header comments.
world:
  ambient_light: [0.02, 0.02, 0.03]
  background: [0.0, 0.0, 0.0]
```

---

## 10.5 Starter Kits: Complete Scenes

Eighteen renderable scenes that combine materials, objects, lighting, and
cameras. Use them as starting points for your own creations -- copy one,
rename it, and modify it.

### Outdoor (7)
- `starter-desert-highway.yaml` -- Desert road with cacti
- `starter-snowy-clearing.yaml` -- Winter landscape with frozen lake
- `starter-zen-garden.yaml` -- Japanese garden with lantern and bridge
- `starter-ancient-ruins.yaml` -- Greek temple ruins
- `starter-floating-islands.yaml` -- Fantasy floating islands
- `starter-golden-hour.yaml` -- Sunset landscape
- `starter-sunset.yaml` -- Dramatic horizon

### Indoor (7)
- `starter-photography-studio.yaml` -- Cyclorama with softbox lighting
- `starter-cornell-box-extended.yaml` -- Classic GI benchmark
- `starter-museum-gallery.yaml` -- Sculptures on pedestals
- `starter-kitchen-counter.yaml` -- Marble counter with tableware
- `starter-wine-cellar.yaml` -- Barrels and bottles by candlelight
- `starter-dining-room.yaml` -- Table, chairs, pendant lamp
- `starter-infinite-mirror-room.yaml` -- Parallel mirrors, emissive spheres

### Showcase (4)
- `starter-material-showroom.yaml` -- 16 materials on pedestals
- `starter-chess-set.yaml` -- Complete Staunton set
- `starter-pool-table.yaml` -- Billiard table with balls
- `starter-underwater.yaml` -- Coral reef with bioluminescence

### Rendering a Starter Kit

```
RayTracer -i scenes/libraries/starter-kits/starter-cornell-box-extended.yaml -w 800 -H 800 -s 64 -d 30
```

Most starter kits define multiple cameras. List them with:

```
RayTracer -i scenes/libraries/starter-kits/starter-cornell-box-extended.yaml --list-cameras
```

Then render a specific one:

```
RayTracer -i scenes/libraries/starter-kits/starter-cornell-box-extended.yaml -c "tre_quarti" -s 128
```

---

## 10.6 Image Textures Library

The `scenes/libraries/textures/` folder contains 20 PNG files:

### Albedo + Normal Map Pairs
- `brick-wall.png` + `brick-wall-normal.png`
- `brick-wall-white.png` (shares `brick-wall-normal.png`)
- `concrete.png` + `concrete-normal.png`
- `metal-scratched.png` + `metal-scratched-normal.png`
- `wood-floor.png` + `wood-floor-normal.png`
- `wood-planks.png` + `wood-planks-normal.png`

### Normal Maps Only
- `fabric-weave-normal.png` -- woven texture overlay
- `stone-cobble-normal.png` -- cobblestone paving
- `tiles-normal.png` -- tile grout lines
- `flat-normal.png` -- neutral flat (disables normal mapping cleanly)

### Specialty
- `earth.png` -- planet Earth
- `checkerboard.png` -- UV testing
- `grid-uv.png` -- numbered UV verification grid
- `logo-3dray.png` -- engine logo

### Usage

```yaml
materials:
  - id: "brick_wall"
    type: "disney"
    roughness: 0.7
    texture:
      type: "image"
      path: "libraries/textures/brick-wall.png"
      uv_scale: [2, 2]
    normal_map:
      path: "libraries/textures/brick-wall-normal.png"
      strength: 1.0
      uv_scale: [2, 2]
```

---

## 10.7 CLI Reference

The complete set of command-line parameters:

| Flag | Long form          | Default                      | Description                                   |
|------|--------------------|------------------------------|-----------------------------------------------|
| `-i` | `--input`          | *(required)*                 | Path to the scene YAML file                   |
| `-o` | `--output`         | `output/render-<scene>.png`  | Output image path (PNG, JPG, or BMP)          |
| `-w` | `--width`          | `1200`                       | Image width in pixels                         |
| `-H` | `--height`         | `800`                        | Image height in pixels                        |
| `-s` | `--samples`        | `16`                         | Samples per pixel (rounded up to nearest perfect square) |
| `-d` | `--depth`          | `50`                         | Maximum ray bounces                           |
| `-S` | `--shadow-samples` | *(per light)*                | Override shadow samples for all area/sphere lights |
| `-c` | `--camera`         | `0`                          | Select camera by name or zero-based index     |
|      | `--list-cameras`   |                              | List available cameras and exit                |
| `-h` | `--help`           |                              | Show help                                     |

### Output Format

The format is determined by the file extension:
- `.png` -- PNG (default, lossless)
- `.jpg` / `.jpeg` -- JPEG (lossy, smaller files)
- `.bmp` -- BMP (uncompressed)

### Samples Rounding

The sample count is always rounded up to the nearest perfect square:

| Requested | Actual | Grid     |
|-----------|--------|----------|
| 1         | 1      | 1x1      |
| 10        | 16     | 4x4      |
| 20        | 25     | 5x5      |
| 50        | 64     | 8x8      |
| 100       | 100    | 10x10    |
| 200       | 225    | 15x15    |
| 256       | 256    | 16x16    |

---

## 10.8 Building a Complete Project: Step by Step

Here is the workflow for creating a scene from scratch using libraries:

### Step 1: Choose a Starter Kit (or Start Blank)

Pick a starter kit close to what you want, copy it, and rename it. Or
create a new empty file.

### Step 2: Set Up World and Camera

```yaml
world:
  ambient_light: [0.02, 0.02, 0.03]
  background: [0.0, 0.0, 0.0]

cameras:
  - name: "main"
    position: [0, 2, -6]
    look_at: [0, 1, 0]
    fov: 45
```

### Step 3: Import Libraries

```yaml
imports:
  - path: "libraries/materials/metals.yaml"
  - path: "libraries/materials/stones.yaml"
  - path: "libraries/objects/furniture.yaml"
  - path: "libraries/objects/decorative-objects.yaml"
  - path: "libraries/lights/studio-3point.yaml"
```

### Step 4: Add Entities

```yaml
entities:
  # Floor
  - type: "infinite_plane"
    point: [0, 0, 0]
    normal: [0, 1, 0]
    material: "dis_carrara_lucido"

  # Furniture from the library
  - type: "instance"
    template: "tavolo_classico"
    translate: [0, 0, 0]

  # Custom object
  - type: "sphere"
    center: [0, 0.78 , 0]
    radius: 0.15
    material: "dis_oro_lucido"
```

### Step 5: Iterate

```
# Quick preview (seconds)
RayTracer -i my-scene.yaml -w 400 -H 225 -s 1 -d 5 -S 1

# Draft (minutes)
RayTracer -i my-scene.yaml -w 800 -H 450 -s 16 -d 20 -S 4

# Final (production)
RayTracer -i my-scene.yaml -w 1920 -H 1080 -s 256 -d 50 -S 16
```

---

## 10.9 Troubleshooting Guide

### Black Image
- **No lights.** Add lights to the `lights:` section or use emissive
  objects / HDRI sky.
- **Camera inside an object.** Move the camera `position` outside all
  geometry.
- **Camera facing the wrong way.** Check `look_at`.

### Too Much Noise
- Increase samples: `-s 64` or `-s 256`.
- Increase shadow samples: `-S 16`.
- Dense Disney materials (subsurface, sheen) need more samples than
  classic types.

### Very Slow Render
- Reduce resolution and samples during testing.
- Use the preview/draft/final workflow.
- Replace Disney materials with classic equivalents for background
  surfaces.

### Missing Material (Object Appears Default Grey)
- Check for typos in the material `id`.
- Ensure the library is imported in `imports:`.
- Check console warnings for unresolved material references.

### Wrong Colors
- Colors are `[R, G, B]` in the range **0.0--1.0**, not 0--255.
  `[255, 0, 0]` is not red -- it is an extremely bright white.

### Glass Looks Wrong (Too Dark or Solid)
- Increase ray depth: `-d 30` or higher. Glass needs 2 bounces per
  surface.
- Ensure there is light behind/around the glass object (glass transmits
  light, so it needs something to transmit).

### Textures Not Showing (Magenta/Pink Fallback)
- Check that the texture file path is correct and relative to the scene
  file.
- Verify the file exists and is a supported format (PNG, JPEG, BMP).

### Imports Not Working
- Paths are relative to the **importing file**, not the working
  directory.
- Check for circular imports (the engine warns on the console).
- Ensure the imported file has the correct YAML structure (`materials:`,
  `templates:`, etc.).

---

## 10.10 Complete Example: Exhibition Hall

A scene that combines multiple libraries into a cohesive project.

```yaml
# exhibition-hall.yaml
# A museum-like room showcasing objects from different library categories.

imports:
  - path: "libraries/materials/metals.yaml"
  - path: "libraries/materials/stones.yaml"
  - path: "libraries/objects/decorative-objects.yaml"

world:
  ambient_light: [0.015, 0.015, 0.02]
  background: [0.0, 0.0, 0.0]

cameras:
  - name: "overview"
    position: [0, 3, -7]
    look_at: [0, 1.2, 0]
    fov: 50

  - name: "detail"
    position: [1.5, 1.5, -4]
    look_at: [0.5, 1.2, 0]
    fov: 35
    aperture: 0.08
    focal_dist: 4.5

materials:
  # Custom floor
  - id: "hall_floor"
    type: "disney"
    roughness: 0.1
    specular: 0.8
    texture:
      type: "checker"
      scale: 0.3
      colors: [[0.85, 0.82, 0.78], [0.25, 0.22, 0.2]]

  # Pedestal
  - id: "pedestal"
    type: "disney"
    color: [0.2, 0.2, 0.22]
    roughness: 0.08
    specular: 0.7

  # Back wall
  - id: "wall"
    type: "disney"
    color: [0.15, 0.14, 0.13]
    roughness: 0.6

entities:
  # Floor
  - type: "infinite_plane"
    point: [0, 0, 0]
    normal: [0, 1, 0]
    material: "hall_floor"

  # Back wall
  - type: "quad"
    q: [-6, 0, 5]
    u: [12, 0, 0]
    v: [0, 5, 0]
    material: "wall"

  # Three pedestals with objects
  # Left pedestal: gold sphere
  - type: "cylinder"
    center: [-2, 0, 0]
    radius: 0.35
    height: 0.9
    material: "pedestal"

  - type: "sphere"
    center: [-2, 1.25, 0]
    radius: 0.35
    material: "dis_oro_lucido"

  # Center pedestal: library decorative vase
  - type: "cylinder"
    center: [0, 0, 0]
    radius: 0.35
    height: 0.9
    material: "pedestal"

  - type: "instance"
    template: "vaso_decorativo"
    translate: [0, 0.9, 0]

  # Right pedestal: diamond sphere
  - type: "cylinder"
    center: [2, 0, 0]
    radius: 0.35
    height: 0.9
    material: "pedestal"

  - type: "sphere"
    center: [2, 1.25, 0]
    radius: 0.35
    material: "dis_diamante"

lights:
  # Individual spot lights for each pedestal
  - type: "spot"
    position: [-2, 4, -1]
    direction: [0, -1, 0.2]
    color: [1, 0.97, 0.92]
    intensity: 60.0
    inner_angle: 10
    outer_angle: 22

  - type: "spot"
    position: [0, 4, -1]
    direction: [0, -1, 0.2]
    color: [1, 0.97, 0.92]
    intensity: 60.0
    inner_angle: 10
    outer_angle: 22

  - type: "spot"
    position: [2, 4, -1]
    direction: [0, -1, 0.2]
    color: [1, 0.97, 0.92]
    intensity: 60.0
    inner_angle: 10
    outer_angle: 22

  # Subtle ambient fill
  - type: "area"
    corner: [-3, 4.5, -3]
    u: [6, 0, 0]
    v: [0, 0, 4]
    color: [0.6, 0.65, 0.8]
    intensity: 5.0
    shadow_samples: 4
```

Render with:

```
RayTracer -i exhibition-hall.yaml -c overview -w 1920 -H 1080 -s 128 -d 30
RayTracer -i exhibition-hall.yaml -c detail -w 1200 -H 800 -s 256 -d 30
```

---

## What You Have Learned

- The library ecosystem provides 800+ materials, 154+ templates, 14
  lighting presets, and 18 starter-kit scenes.
- Materials use `dis_` (Disney PBR) and `cls_` (Classic) prefixes.
- Object templates follow consistent conventions (base at Y=0, 1:1
  meter scale).
- Libraries are loaded via `imports:` -- local definitions override
  imported ones.
- The CLI gives full control over resolution, quality, camera
  selection, and output format.
- The preview/draft/final workflow is the most efficient way to develop
  scenes.

---

[Previous: Participating Media (Volumetrics)](./09-volumetrics.md) | [Tutorial Index](./README.md)
