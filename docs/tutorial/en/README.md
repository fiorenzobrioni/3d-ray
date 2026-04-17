# 3D-Ray Tutorial

A complete guide to creating photorealistic images with the 3D-Ray engine, from first principles to complex scenes.

---

## Chapters

### [01 -- What Is Ray Tracing?](./01-what-is-ray-tracing.md)
How light, cameras, and surfaces interact. The theory behind path tracing,
Monte Carlo sampling, and the iterative rendering workflow. No code yet --
just the mental model you need before writing your first scene.

### [02 -- Your First Scene](./02-first-scene.md)
A hands-on walkthrough that builds a complete scene from scratch: world,
camera, materials, objects, and lights. By the end you will render three
spheres on a floor and understand every line of the YAML file.

### [03 -- Materials in Depth](./03-materials.md)
All six material types explained parameter by parameter: Lambertian, Metal,
Dielectric (glass), Emissive, Disney/PBR, and Mix. Plus procedural textures
(checker, noise, marble, wood), image textures, and normal maps.

### [04 -- All the Shapes](./04-geometric-primitives.md)
Every geometric primitive the engine supports -- from spheres and boxes to
tori, capsules, annuli, quads, and OBJ meshes -- with exact YAML syntax,
default values, and a gallery scene that renders them all.

### [05 -- Transforms, Groups, and Scene Organization](./05-transforms-and-groups.md)
Move, rotate, and scale objects. Compose hierarchies with groups. Define
reusable templates and stamp them as instances. Split scenes across multiple
files with the import system.

### [06 -- Lighting Mastery](./06-lighting.md)
Point, directional, spot, area, and sphere lights. Emissive geometry that
glows. Soft shadows, shadow sample control, and practical lighting setups
(three-point studio, dramatic chiaroscuro, outdoor sun).

### [07 -- Sky, Environment, and Camera Effects](./07-sky-environment-camera.md)
Flat, gradient, and HDRI sky modes. The sun disk. Depth of field with
aperture and focal distance. Multiple named cameras and how to select them.

### [08 -- Constructive Solid Geometry (CSG)](./08-csg.md)
Union, intersection, and subtraction -- the three Boolean operations that
let you sculpt complex shapes from simple primitives. Nested operations,
per-child materials, and practical modeling recipes.

### [09 -- Participating Media (Volumetrics)](./09-volumetrics.md)
Fog, mist, underwater haze, clouds, localized smoke. Four medium types
(homogeneous, height fog, procedural Perlin fBm, 3D grid) and five phase
functions (isotropic, HG, Rayleigh, double-HG, Schlick).

### [10 -- Asset Libraries and Complete Scenes](./10-libraries-and-projects.md)
The bundled library ecosystem: 800+ materials, 154+ object templates,
14 lighting presets, and 18 starter-kit scenes. CLI reference, project
workflow, and a troubleshooting guide.

---

> Each chapter builds on the previous one. If you are new to ray tracing,
> start at Chapter 1 and work through in order. If you already know the
> basics, jump to whichever topic you need.
