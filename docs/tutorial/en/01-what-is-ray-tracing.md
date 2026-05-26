# Chapter 1: What Is Ray Tracing?

Before writing a single line of scene description, it helps to understand
*what the engine is doing* when it turns your YAML file into an image.
This chapter introduces the core ideas -- no code yet, just the mental
model you will rely on for the rest of the tutorial.

---

## 1.1 Light, Surfaces, and the Camera

In the physical world a light source emits photons. Those photons travel
in straight lines until they hit a surface (or scatter inside a medium 
like fog), where they may be absorbed, reflected, refracted, or re-emitted. 
A tiny fraction of them eventually enters a camera (or your eye), and the 
pattern they form becomes an image.

Simulating this process forwards -- from the light source to the camera --
is extremely wasteful. Most photons never reach the camera at all. Ray
tracing reverses the process: it sends rays **from the camera into the
scene** and traces their journey backwards toward the light. This is called
*backward ray tracing*, and it is the foundation of virtually every
photorealistic renderer, including 3D-Ray.

The camera sits at a point in space, aimed at a target. For every pixel in
the output image the engine shoots one or more rays through that pixel into
the scene. Each ray either hits an object or escapes into the sky.

---

## 1.2 What Happens When a Ray Hits a Surface

When a ray strikes a surface, the outcome depends on the **material**:

- **Diffuse (Lambertian)** surfaces scatter the incoming ray in a random
  direction weighted by the surface normal. Think of chalk, plaster, or
  matte paint.

- **Metallic** surfaces reflect the ray according to the laws of
  reflection, with some random spread (controlled by a "fuzz" or
  "roughness" parameter). Think of brushed steel or polished gold.

- **Dielectric (glass)** surfaces both reflect *and* refract the ray. A
  fraction of the light bounces off the surface; the rest bends as it
  enters or exits the material. The ratio depends on the angle of
  incidence (the *Fresnel effect*) and the material's index of refraction.

- **Emissive** surfaces add their own light to the ray. They do not just
  reflect incoming light -- they glow.

- **Disney/PBR** surfaces combine all of the above in a single, physically
  inspired model with parameters like metallic, roughness, clearcoat,
  sheen, and transmission. Subsurface scattering is added separately via
  entity-bound participating media — see the SSS chapter.

After the material decides what happens, a new ray is generated (reflected,
refracted, or scattered) and the process repeats. The ray keeps bouncing
until it either hits an emissive surface, reaches a light source, or
exhausts its maximum number of bounces.

---

## 1.3 Path Tracing and Monte Carlo Integration

A single ray path -- camera to surface to surface to light -- captures only
one possible route that light could take. To produce a smooth, noise-free
image we need to average many such paths. This is **Monte Carlo
integration**: the engine fires many random rays per pixel, each following
a slightly different path, and averages the results.

To make this process dramatically more efficient, 3D-Ray relies on **Next 
Event Estimation (NEE)**. Instead of just bouncing randomly and blindly hoping
to hit a light source (which causes immense noise), at *each* intersection 3D-Ray
explicitly fires deterministic "shadow rays" directly toward the known light
sources. This separates direct lighting from the purely stochastic indirect 
bounces, accelerating the convergence of photorealistic scenes.

The key parameter is **samples per pixel** (SPP). With 1 sample the image
is extremely noisy -- every pixel is essentially a single random guess.
With 16 samples the picture becomes recognizable. With 256 or more it
approaches photographic quality.

> **Technical note:** 3D-Ray uses *stratified (jittered) sampling*. Each
> pixel is divided into a grid of sub-cells (for example, 4x4 = 16 cells
> for 16 samples). One ray is sent through a random point inside each
> cell. This produces a more even distribution than purely random sampling
> and converges faster. With the default **Sobol** sampler the engine
> uses the exact sample count you request. With the legacy `--sampler
> prng`, it rounds up to the nearest perfect square (e.g. requesting 20
> gives you 25 = 5×5). See [Rendering Profiles §3](../../reference/rendering-profiles.md).

---

## 1.4 Ray Depth: How Many Bounces?

Every time a ray hits a surface and spawns a new ray, that counts as one
**bounce**. The `depth` parameter sets the maximum number of bounces per
path.

- **Diffuse scenes** rarely need more than 5--10 bounces because each
  bounce absorbs most of the light.
- **Glass objects** are expensive: every surface the ray enters and exits
  costs two bounces. A glass sphere inside a glass box can easily need
  20+ bounces.
- **Indoor scenes** lit only by emissive surfaces (like a Cornell Box)
  benefit from higher depth because the light must bounce many times to
  illuminate the room.

The default depth in 3D-Ray is **8**, which is plenty for the vast majority
of scenes thanks to Russian Roulette (section 1.5). Raise it to 16–20 only
when rendering stacked glass (liquids in glasses, nested dielectrics). See
[Rendering Profiles](../../reference/rendering-profiles.md) for the full guide.

---

## 1.5 Russian Roulette: Knowing When to Stop

Not all paths are equally useful. A ray that has bounced many times and
carries very little energy is unlikely to contribute meaningfully to the
final pixel color. Rather than always tracing to the maximum depth, the
engine uses **Russian Roulette**: at each bounce beyond a minimum number,
there is a probability that the path will be terminated early. The
surviving paths are weighted up to compensate, so the result is unbiased.

This is entirely automatic -- you do not need to configure it. It simply
means the engine spends its effort where it matters most.

---

## 1.6 BVH: Finding Objects Fast

A scene may contain thousands or millions of triangles. Testing every ray
against every object would be impractically slow. 3D-Ray builds a
**Bounding Volume Hierarchy** (BVH) -- a tree of nested bounding boxes
that lets the engine skip vast regions of the scene that a ray cannot
possibly hit. With a BVH the cost of finding the nearest intersection
grows logarithmically with the number of objects, making even complex
scenes tractable.

BVH construction is automatic. You never need to configure it.

---

## 1.7 The Iterative Rendering Workflow

High-quality images take time. You should never jump straight to a
production render. Instead, use a three-stage workflow:

| Stage       | Resolution | Samples | Depth | Shadow Samples | Purpose                        |
|-------------|------------|---------|-------|----------------|--------------------------------|
| **Preview** | 400x225    | 1       | 4     | 1              | Check composition and framing  |
| **Draft**   | 800x450    | 16      | 6     | 1              | Check materials and lighting   |
| **Final**   | 1920x1080  | 256     | 8     | 4              | Production-quality output      |

> The depth and shadow-samples values above match the canonical profiles
> in [Rendering Profiles](../../reference/rendering-profiles.md), which
> also lists the recommended sample counts for each stage.

A preview render takes seconds and tells you immediately if the camera is
pointed in the right direction and the objects are roughly where you want
them. A draft takes a few minutes and lets you evaluate colors, materials,
and lighting. Only when everything looks right do you commit to a final
render that may take an hour or more.

The CLI flags that control these settings are:

| Flag | Long form         | Default | What it does                          |
|------|-------------------|---------|---------------------------------------|
| `-w` | `--width`         | 1200    | Image width in pixels                 |
| `-H` | `--height`        | 800     | Image height in pixels                |
| `-s` | `--samples`       | 16      | Samples per pixel                     |
| `-d` | `--depth`         | 8       | Maximum ray bounces                   |
| `-S` | `--shadow-samples`| *(per light)* | Override shadow sample count for all area/sphere lights |
| `-C` | `--clamp`         | 100     | Firefly clamp (per-sample radiance)   |

You will learn the full CLI in Chapter 10. For now, just remember the
preview/draft/final pattern -- it will save you hours.

---

## 1.8 Anatomy of a 3D-Ray Scene File

Every scene is described in a single YAML file (or a main file that
imports others). At the top level it contains up to seven sections:

```
world:        Global settings -- sky (flat / gradient / hdri), ground, fog
cameras:      One or more camera definitions
materials:    Named material definitions
entities:     The objects in the scene
lights:       Explicit light sources
templates:    Reusable object blueprints (not rendered directly)
imports:      Paths to external YAML files to merge in
```

You will learn each section in detail over the following chapters. For now,
the important thing is the overall structure: a scene is a *world* viewed
through a *camera*, populated with *entities* that have *materials*,
illuminated by *lights*, optionally organized with *templates* and
*imports*.

---

## 1.9 The Coordinate System

3D-Ray uses a **right-handed, Y-up** coordinate system:

- **X** points to the right.
- **Y** points up.
- **Z** points toward the camera (out of the screen in a default view).

When you place a sphere at `[0, 1, 0]`, it sits one unit above the origin.
A floor is typically an infinite plane at `y = 0`. Positive Z is "in front
of" the default camera position.

Colors are specified as `[R, G, B]` triplets in the range **0.0 to 1.0**
(not 0 to 255). `[1, 0, 0]` is pure red; `[0.5, 0.5, 0.5]` is medium
grey.

---

## Summary

| Concept              | What it means                                          |
|----------------------|--------------------------------------------------------|
| Backward ray tracing | Rays go from camera into the scene, not from lights    |
| Path tracing         | Follow each ray through multiple bounces               |
| Next Event Estimation| Explicit shadow rays toward lights to reduce noise     |
| Monte Carlo sampling | Average many random paths per pixel to reduce noise    |
| Samples per pixel    | More samples = less noise = longer render              |
| Ray depth            | Maximum number of surface bounces per path             |
| Russian Roulette     | Probabilistic early termination of dim paths           |
| BVH                  | Acceleration structure for fast ray-object tests       |
| Stratified sampling  | Divide pixel into sub-cells for more even ray coverage |

With this foundation in place, you are ready to write your first scene.

---

[Next: Your First Scene](./02-first-scene.md) | [Tutorial Index](./README.md)
