# Chapter 3: Materials in Depth

In the previous chapter you used Lambertian and Metal materials. The
engine supports six material types in total, plus a rich texture system.
This chapter covers every one of them, parameter by parameter.

---

## 3.1 Quick Reference: The Six Material Types

| Type         | Aliases              | Key effect                    |
|--------------|----------------------|-------------------------------|
| `lambertian` | --                   | Diffuse matte                 |
| `metal`      | --                   | Specular reflection           |
| `dielectric` | --                   | Transparent glass / refraction|
| `emissive`   | --                   | Self-luminous glow            |
| `disney`     | `disney_bsdf`, `pbr` | Universal PBR (anisotropy, glass absorption, thin-film, sheen...) |
| `mix`        | `blend`              | Blend two materials together  |

Every material is defined in the `materials:` section with a unique `id`:

```yaml
materials:
  - id: "my_material"
    type: "lambertian"
    color: [0.8, 0.2, 0.2]
```

Entities reference materials by their `id`:

```yaml
entities:
  - type: "sphere"
    material: "my_material"
    ...
```

---

## 3.2 Lambertian (Diffuse Matte)

```yaml
- id: "chalk"
  type: "lambertian"
  color: [0.95, 0.92, 0.88]
```

The simplest material. Light is scattered equally in all directions above
the surface (cosine-weighted hemisphere). There are no reflections, no
highlights -- just a flat, matte finish.

| Parameter | Type      | Default        | Description                 |
|-----------|-----------|----------------|-----------------------------|
| `color`   | `[R,G,B]` | `[0.5,0.5,0.5]` | Diffuse albedo (0.0--1.0) |
| `texture` | block     | --             | Procedural or image texture (replaces color) |

When a `texture:` block is present it overrides `color`. More on textures
in Section 3.7.

---

## 3.3 Metal

```yaml
- id: "copper_satin"
  type: "metal"
  color: [0.95, 0.64, 0.54]
  fuzz: 0.12
```

Metal surfaces reflect light specularly. The engine uses a GGX microfacet
model for realistic highlights.

| Parameter | Type      | Default | Description                            |
|-----------|-----------|---------|----------------------------------------|
| `color`   | `[R,G,B]` | --     | Reflectance color (metallic tint)       |
| `fuzz`    | `float`   | `0.0`  | Roughness: 0 = perfect mirror, higher = blurrier |

The `fuzz` value maps internally to a roughness parameter
(`alpha = fuzz * fuzz`). Keep it in the 0.0--0.6 range for realistic
metals; higher values start to look unrealistic.

---

## 3.4 Dielectric (Glass and Transparent Materials)

```yaml
- id: "window_glass"
  type: "dielectric"
  refraction_index: 1.52

- id: "red_glass"
  type: "dielectric"
  refraction_index: 1.52
  color: [0.9, 0.1, 0.08]
```

Dielectric materials are transparent. Light is split at each surface
according to the Fresnel equations: some is reflected, some is refracted
(bent). The `refraction_index` determines how much the light bends.

| Parameter          | Type      | Default | Description                      |
|--------------------|-----------|---------|----------------------------------|
| `refraction_index` | `float`   | `1.5`  | Index of refraction (IOR)         |
| `color`            | `[R,G,B]` | `[1,1,1]` | Tint color for colored glass   |

### Common Indices of Refraction

| Material   | IOR   |
|------------|-------|
| Air        | 1.000 |
| Water      | 1.333 |
| Ice        | 1.31  |
| Glass      | 1.52  |
| Crystal    | 1.65  |
| Diamond    | 2.42  |
| Ruby       | 1.77  |
| Emerald    | 1.57  |

### Important: Glass Needs More Ray Depth

Every glass surface a ray enters and exits costs two bounces. The
`draft-small` default preset uses `-d 4`; quality presets (`-q standard`
and above) use `-d 8`, which is enough for most scenes thanks to Russian
Roulette. If you have nested glass objects (e.g. a glass of water, bottles
behind bottles), raise the ray depth to at least 16:

```
RayTracer -i my-scene.yaml -s 64 -d 16
```

See [Rendering Profiles](../../reference/rendering-profiles.md) for the full
explanation of when to raise `-d`.

---

## 3.5 Emissive (Self-Luminous Surfaces)

```yaml
- id: "warm_glow"
  type: "emissive"
  color: [1.0, 0.85, 0.6]
  intensity: 10.0
```

An emissive surface radiates its own light. It does not reflect or scatter
incoming light -- it glows.

| Parameter   | Type      | Default | Description                         |
|-------------|-----------|---------|-------------------------------------|
| `color`     | `[R,G,B]` | --     | Emission color                       |
| `intensity` | `float`   | `1.0`  | Brightness multiplier                |

The actual emitted radiance is `color * intensity`. An intensity of 1.0
is barely visible; values of 5--50 are typical for room lighting; 100+
creates very bright sources.

### Emissive Objects as Geometry Lights

Any entity with an emissive material is automatically detected by the
engine and registered as a **geometry light** for Next Event Estimation
(NEE). This means the engine actively samples these surfaces when
computing direct illumination -- just like explicit light sources.

Explicit `area` and `sphere` lights are now also visible via an
internally-managed emissive proxy, so the choice
between free emissives and explicit lights is mostly about shape: use
an explicit light when the emitter is a canonical rectangle or sphere
(it samples more efficiently); use a free emissive material when you
want a custom shape or texture-driven emission. `point`/`spot`/
`directional` lights remain delta — they have no visible geometry by
construction.

Use emissive materials for light panels, glowing orbs, neon signs, lava,
fire, and anything that should both emit light and be seen.

---

## 3.6 Disney/PBR (Principled Material)

```yaml
- id: "red_car_paint"
  type: "disney"
  color: [0.7, 0.05, 0.05]
  metallic: 0.0
  roughness: 0.3
  specular: 0.6
  clearcoat: 1.0
  clearcoat_gloss: 0.9
```

The Disney Principled BSDF (also known as PBR) is the most versatile
material type. It combines diffuse, specular, metallic, clearcoat,
sheen, and transmission into a single material with intuitive
parameters. You can use it instead of lambertian, metal, or dielectric
for any surface. For subsurface scattering, pair Disney's `spec_trans`
with an `interior_medium` binding on the entity (see Chapter 7).

Type aliases: `disney`, `disney_bsdf`, `pbr` (all create the same
material).

### Complete Parameter Reference

| Parameter             | Default  | Range        | Description                                     |
|-----------------------|----------|--------------|-------------------------------------------------|
| `color`               | --       | 0--1         | Base albedo color                                |
| `metallic`            | `0.0`    | 0--1         | 0 = dielectric (plastic, wood), 1 = conductor (metal) |
| `roughness`           | `0.5`    | 0--1         | 0 = mirror-smooth, 1 = fully diffuse            |
| `specular`            | `0.5`    | 0--1         | Dielectric specular intensity (Fresnel F0)       |
| `specular_tint`       | `0.0`    | 0--1         | Tint specular reflection by base color           |
| `sheen`               | `0.0`    | 0--1         | Grazing-angle soft highlight (fabric, velvet)    |
| `sheen_tint`          | `0.5`    | 0--1         | Tint the sheen by base color                     |
| `sheen_roughness`     | `0.3`    | 0.04--1      | Charlie sheen α — width of the grazing halo     |
| `clearcoat`           | `0.0`    | 0--1         | Second specular lobe (lacquer, varnish)          |
| `clearcoat_gloss`     | `1.0`    | 0--1         | **Legacy** — prefer `coat_roughness` (≈ `1 - clearcoat_gloss`) |
| `coat_ior`            | `1.5`    | 1+           | Clearcoat IOR (overrides the default lacquer)   |
| `coat_roughness`      | `-1.0`   | -1 or 0--1   | Sentinel `-1` falls back to `clearcoat_gloss`; any `≥ 0` enables the physical coat model and `clearcoat_gloss` is ignored |
| `coat_normal_map`     | --       | image path   | Normal map applied **only** to the clearcoat lobe |
| `spec_trans`          | `0.0`    | 0--1         | Specular transmission (0 = opaque, 1 = glass)    |
| `transmission_color`  | `[1,1,1]`| 0--1         | Colour of the glass interior at `transmission_depth` |
| `transmission_depth`  | `0.0`    | 0+           | Distance (scene units) at which `transmission_color` is reached |
| `ior`                 | `1.5`    | 1+           | Index of refraction (specular and transmission) |
| `anisotropic`         | `0.0`    | 0--1         | 0 = isotropic, 1 = stretched along the tangent  |
| `anisotropic_rotation`| `0.0`    | 0--1         | Fraction of 2π rotation around the normal       |
| `diff_trans`          | `0.0`    | 0--1         | Diffuse transmission fraction (foliage, thin fabric) |
| `thin_walled`         | `false`  | bool         | Skip refraction / double-hit — foliage, paper   |
| `thin_film_thickness` | `0.0`    | 0+ (nm)      | Iridescent film thickness (bubbles, opal, AR)   |
| `thin_film_ior`       | `1.5`    | 1+           | Iridescent film IOR (η₂)                        |
| `texture`             | --       | --           | Procedural or image texture (replaces color)    |
| `normal_map`          | --       | --           | Surface detail via normal perturbation          |

> **Texturing every parameter.** Every scalar parameter accepts a
> `*_texture` variant (e.g. `roughness_texture`) and the two colour
> inputs (`color`, `transmission_color`) accept a matching `*_texture`
> block. Example: `roughness_texture: { type: "image", path: "rough.png" }`.

> **Subsurface scattering.** The legacy Disney 2015 `subsurface` and
> `flatness` fake-SSS fields have been removed. Physically-correct SSS
> now comes from one of two interoperable paths:
>
> 1. **Material-embedded** — declare `subsurface_radius` (plus optional
>    `subsurface_color`, `subsurface_scale`, `subsurface_anisotropy`) on
>    the Disney material. The loader auto-builds a `HomogeneousMedium`
>    with `σ_t = 1 / (radius · scale)`, `σ_s = α · σ_t`,
>    `σ_a = (1 − α) · σ_t` and auto-injects it on every entity that does
>    not already have an explicit `interior_medium`. This emulates
>    volumetric subsurface scattering inside the material.
> 2. **Entity-bound** — declare a `mediums:` entry and bind it
>    via `interior_medium` on the entity. Maximum control, supports
>    heterogeneous media. See Chapter 7.
>
> An explicit `interior_medium` always wins over the embedded medium.
> Full derivation in
> [docs/technical/subsurface-scattering.md](../../technical/subsurface-scattering.md).

### How the Parameters Work Together

Think of the Disney material as a layered system:

1. **Base layer** (`metallic` = 0): a dielectric surface with diffuse
   reflection, controlled by `roughness`. Like plastic, wood, skin.
2. **Metal mode** (`metallic` = 1): specular-only reflection tinted by
   `color`. Like gold, steel, copper.
3. **Clearcoat layer** (`clearcoat` > 0): an independent glossy coating
   on top of whatever is underneath. Like car paint or lacquered wood.
4. **Transmission** (`spec_trans` > 0): light passes through the
   material. Combined with `roughness` > 0 you get frosted glass.
5. **Subsurface scattering** (`spec_trans: 1.0` + `interior_medium` on
   the entity): light refracts through the surface and is transported
   by a true volumetric random walk inside the bound medium. Used for
   skin, wax, marble, milk, jade, candles. Configured at the entity
   level rather than on the material — see Chapter 7.
6. **Sheen** (`sheen` > 0): a soft glow at grazing angles. Used for
   fabric, velvet, some organic materials. `sheen_roughness` controls
   the width of the glow (small values = crisp halo, large = soft wash).
7. **Anisotropy** (`anisotropic` > 0): stretches the specular highlight
   along the tangent direction. Brushed metal, hair, vinyl records.
   Rotate the tangent frame with `anisotropic_rotation`.
8. **Thin-film iridescence** (`thin_film_thickness` > 0): multiplies the
   Fresnel by a wavelength-dependent thin-film factor. Soap bubbles,
   opal, dielectric AR coatings.
9. **Coloured glass** (`spec_trans > 0` with `transmission_color` and a
   `transmission_depth > 0`): switches the transmission path to
   Beer-Lambert absorption, so the light passing through the glass is
   exponentially tinted with distance (thicker sections are darker).
10. **Thin-walled surfaces** (`thin_walled: true` with `diff_trans` or
    `spec_trans`): the engine stops modelling refraction through two
    interfaces — ideal for leaves, paper, thin fabric, stained-glass.

### Recipes: Real-World Materials

**Polished gold:**
```yaml
- id: "polished_gold"
  type: "disney"
  color: [1.0, 0.76, 0.33]
  metallic: 1.0
  roughness: 0.05
  specular: 0.8
```

**Red plastic:**
```yaml
- id: "red_plastic"
  type: "disney"
  color: [0.8, 0.1, 0.1]
  metallic: 0.0
  roughness: 0.3
  specular: 0.5
```

**Car paint (with clearcoat):**
```yaml
- id: "blue_car"
  type: "disney"
  color: [0.02, 0.1, 0.45]
  metallic: 0.0
  roughness: 0.4
  clearcoat: 1.0
  clearcoat_gloss: 0.9
```

**Frosted glass:**
```yaml
- id: "frosted"
  type: "disney"
  color: [0.95, 0.97, 1.0]
  roughness: 0.3
  spec_trans: 0.85
  ior: 1.52
  specular: 0.7
```

**Velvet fabric:**
```yaml
- id: "purple_velvet"
  type: "disney"
  color: [0.3, 0.05, 0.2]
  roughness: 0.85
  sheen: 1.0
  sheen_tint: 0.5
```

**Porcelain (true SSS via interior_medium):**
```yaml
mediums:
  - id: porcelain_int
    type: homogeneous
    sigma_a: [0.005, 0.007, 0.012]
    sigma_s: [4.5, 4.2, 3.8]
    phase: hg
    g: 0.4

materials:
  - id: porcelain
    type: disney
    color: [1.0, 1.0, 1.0]
    roughness: 0.15
    specular: 0.7
    spec_trans: 1.0
    ior: 1.46

entities:
  - type: sphere
    material: porcelain
    interior_medium: porcelain_int
```

**Brushed steel (anisotropic):**
```yaml
- id: "brushed_steel"
  type: "disney"
  color: [0.7, 0.7, 0.72]
  metallic: 1.0
  roughness: 0.35
  anisotropic: 0.75
  anisotropic_rotation: 0.0    # brush aligned with the tangent U direction
```

**Coloured glass (bottle of brandy):**
```yaml
- id: "brandy_glass"
  type: "disney"
  color: [1.0, 1.0, 1.0]
  metallic: 0.0
  roughness: 0.0
  spec_trans: 1.0
  ior: 1.52
  transmission_color: [0.95, 0.55, 0.15]
  transmission_depth: 6.0      # the colour is reached after 6 scene units
```

**Soap bubble (thin-film iridescence):**
```yaml
- id: "soap_bubble"
  type: "disney"
  color: [1.0, 1.0, 1.0]
  metallic: 0.0
  roughness: 0.01
  spec_trans: 1.0
  thin_walled: true            # no double refraction
  thin_film_thickness: 520     # ~520 nm film
  thin_film_ior: 1.33
```

**Car paint with physical clearcoat and a coat normal map:**
```yaml
- id: "metallic_pearl"
  type: "disney"
  color: [0.05, 0.1, 0.35]
  metallic: 0.9
  roughness: 0.25
  clearcoat: 1.0
  coat_ior: 1.55
  coat_roughness: 0.15         # any ≥ 0 value switches to physical coat model
  coat_normal_map: "textures/orange_peel_normal.png"
```

**Velvet with Charlie sheen:**
```yaml
- id: "moss_velvet"
  type: "disney"
  color: [0.1, 0.25, 0.08]
  metallic: 0.0
  roughness: 0.95
  sheen: 1.0
  sheen_tint: 1.0
  sheen_roughness: 0.25        # crisp, slightly narrow halo
```

**Green leaf (diffuse transmission + thin-walled):**
```yaml
- id: "tree_leaf"
  type: "disney"
  color: [0.25, 0.5, 0.18]
  metallic: 0.0
  roughness: 0.8
  diff_trans: 0.55             # half of the diffuse energy goes through
  thin_walled: true
```

**Skin (Random Walk SSS via interior_medium):**
```yaml
mediums:
  - id: skin_int
    type: homogeneous
    sigma_a: [0.032, 0.17, 0.48]    # Jensen 2001 "skin1"
    sigma_s: [9.25, 11.0, 12.6]
    phase: hg
    g: 0.92                          # strong forward HG

materials:
  - id: skin_surface
    type: disney
    color: [1.0, 1.0, 1.0]
    metallic: 0.0
    roughness: 0.35
    specular: 0.5
    spec_trans: 1.0
    ior: 1.4

entities:
  - type: sphere
    material: skin_surface
    interior_medium: skin_int
```

### Quick Cheat-Sheet

A compact reference covering the whole Disney surface taxonomy.
Use it to pick a starting point, then tune `roughness` and `specular` for
the specific hero look. Only the non-default keys are listed — omit a key
to keep its default.

| Material family | Core recipe |
|---|---|
| Matte diffuse (plaster, unfinished wood) | `roughness: 0.9`, `specular: 0.2`, optional `sheen: 0.1–0.2` |
| Flat matte (paper, concrete) | `roughness: 0.85`, `specular: 0.2` |
| Polished plastic | `metallic: 0`, `roughness: 0.2–0.4`, `specular: 0.5`, optional `clearcoat: 0.3` |
| Rubber / silicone | `metallic: 0`, `roughness: 0.7–0.9`, `specular: 0.25`, `sheen: 0.2`, `sheen_roughness: 0.5` |
| Velvet / fabric | `roughness: 0.9`, `sheen: 1.0`, `sheen_tint: 0.7`, `sheen_roughness: 0.2–0.4` |
| Skin / porcelain / marble / milk / wax | surface: `metallic: 0`, `roughness: 0.3–0.5`, `specular: 0.5`, `spec_trans: 1`, `ior: 1.4`; **entity binds `interior_medium`** to a `homogeneous` medium (Jensen 2001 preset) — see Chapter 7 |
| Leaf / paper (translucent) | `roughness: 0.4`, `thin_walled: true`, `diff_trans: 0.5` |
| Polished metal (gold, silver, chrome) | `metallic: 1`, `roughness: 0.02–0.15`, `specular: 0.9–1.0` |
| Rough / satin metal | `metallic: 1`, `roughness: 0.4–0.7`, `specular: 0.6` |
| Brushed metal | `metallic: 1`, `roughness: 0.25`, `anisotropic: 0.7–0.9`, `anisotropic_rotation: 0.0–1.0` |
| Car paint (legacy slider) | `metallic: 0`, `roughness: 0.3`, `clearcoat: 1`, `clearcoat_gloss: 0.9` |
| Car paint (physical coat) | `metallic: 0–0.9`, `roughness: 0.25`, `clearcoat: 1`, `coat_ior: 1.55`, `coat_roughness: 0.05–0.15` |
| Lacquered wood / piano black | `roughness: 0.1`, `clearcoat: 1`, `coat_roughness: 0.05`, `specular: 0.7` |
| Ceramic / porcelain (hard) | `metallic: 0`, `roughness: 0.15`, `specular: 0.7`, `clearcoat: 0.5`, `coat_roughness: 0.2` |
| Clear glass | `spec_trans: 1`, `roughness: 0.0`, `ior: 1.5`, `specular: 1.0` |
| Coloured glass / gemstone | `spec_trans: 1`, `roughness: 0.0–0.02`, `ior: 1.5–1.77`, `transmission_color: <tint>`, `transmission_depth: 0.3–1.0` |
| Diamond | `spec_trans: 1`, `roughness: 0.003`, `ior: 2.42`, `specular: 1.0` |
| Frosted glass | `spec_trans: 1`, `roughness: 0.2–0.3`, `ior: 1.5`, `specular: 0.7` |
| Soap bubble / iridescent film | `spec_trans: 1`, `roughness: 0.02`, `ior: 1.33`, `thin_walled: true`, `thin_film_thickness: 300–700 (nm)`, `thin_film_ior: 1.33` |
| Anodised / painted metal | `metallic: 0.9`, `roughness: 0.25`, `clearcoat: 0.4`, `coat_roughness: 0.15` |

> **Tip.** When converting a Disney-2012 scene to the modern parameters,
> the near-one-liner is `coat_roughness ≈ 1 - clearcoat_gloss` (and drop
> the old key). For sheen-heavy materials, set `sheen_roughness` to 0.4
> or higher so the Charlie halo is softer than the default 0.3 peak.

---

## 3.7 Mix/Blend Material

```yaml
- id: "weathered_metal"
  type: "mix"
  material_a: "clean_steel"
  material_b: "rust"
  blend: 0.4
```

A Mix material blends two other materials together. Both `material_a`
and `material_b` must reference materials already defined (or imported).

| Parameter    | Type    | Default | Description                          |
|--------------|---------|---------|--------------------------------------|
| `material_a` | `string` | --    | ID of the first material              |
| `material_b` | `string` | --    | ID of the second material             |
| `blend`      | `float` | `0.5`  | Constant blend factor (0 = all A, 1 = all B) |
| `mask`       | block   | --     | Texture that spatially controls the blend     |

### Blend with a Texture Mask

Instead of a uniform blend, you can use a procedural texture to control
*where* each material appears:

```yaml
materials:
  - id: "clean_steel"
    type: "disney"
    color: [0.7, 0.7, 0.72]
    metallic: 1.0
    roughness: 0.15

  - id: "rust"
    type: "disney"
    color: [0.55, 0.25, 0.08]
    metallic: 0.3
    roughness: 0.7

  - id: "weathered_steel"
    type: "mix"
    material_a: "clean_steel"
    material_b: "rust"
    mask:
      type: "noise"
      scale: 3.0
      noise_strength: 2.0
```

Where the noise texture is dark (near 0) you see clean steel; where it
is bright (near 1) you see rust. The result is a realistic, spatially
varying weathered surface.

Mix materials can be nested: you can create a mix of a mix for complex
multi-layer effects.

---

## 3.8 Procedural Textures

Any material that accepts a `color` field can also accept a `texture:`
block. When present, the texture generates color values procedurally
based on the 3D position of each surface point, replacing the flat
`color`.

**Sampling space — important.** Every procedural samples in **object-local
space**: the origin of the sample coordinates sits on the primitive's own
`Center` / `Q` / `Point` anchor, and the axes are world-aligned. The
practical consequence is that `scale` is in
*cycles per object-local unit*, so a sphere of radius `r` sees approximately
`scale × 2r` cycles across its diameter. The examples below are tuned for a
**radius-~1 reference sphere**; on a tiny gem (`r ≈ 0.3`) double or triple the
`scale` value, on a large hero slab (`r ≈ 3`) halve it. If you need the
*world-locked* legacy behavior (e.g. a marble pattern continuing seamlessly
across many tiled boxes), drive your material from a `coordinate` texture node
with `mode: "world"`.

### Checker

```yaml
texture:
  type: "checker"
  scale: 1.0
  colors: [[0.9, 0.9, 0.9], [0.1, 0.1, 0.1]]
```

A 3D checkerboard pattern that alternates between two colors. The `scale`
controls the size of each square (smaller scale = larger squares).

### Noise

```yaml
texture:
  type: "noise"
  noise_type: "fbm"          # perlin | fbm | turbulence | ridged | billow | hetero_terrain | hybrid_multifractal
  scale: 4.0
  octaves: 5
  lacunarity: 2.0
  gain: 0.5
  fractal_increment: 1.0     # Musgrave H — only hetero_terrain / hybrid_multifractal
  fractal_offset: 0.7        # Musgrave offset / "sea level" — only hetero_terrain / hybrid_multifractal
  distortion: 0.3
  colors: [[0, 0, 0], [1, 1, 1]]
```

3D-Ray ships a full pro-grade fractal noise stack with the following
modes:

| `noise_type`           | Look                                                  | Use for                              |
|------------------------|-------------------------------------------------------|--------------------------------------|
| `perlin`               | Smooth gradient noise (single octave)                 | Soft variation, low-frequency        |
| `fbm`                  | Sum of octaves (the canonical fractal noise)          | Stone, dirt, terrain, paper          |
| `turbulence`           | Σ\|noise\| (sharpened absolute-value variant)         | Clouds, smoke, dirt detail           |
| `ridged`               | Musgrave ridged multifractal                          | Rock, lightning, marble veins        |
| `billow`               | Σ\|noise\| octaves, normalised                        | Puffy clouds, foam, rust             |
| `hetero_terrain`       | Musgrave §16.3.3 — peaks rough, valleys smooth        | Eroded terrain, mountains, coastline |
| `hybrid_multifractal`  | Musgrave §16.3.4 — stratified layers + sharp peaks    | Asteroids, alien rock, marble strata |

| Parameter           | Default | Description                                                            |
|---------------------|---------|------------------------------------------------------------------------|
| `noise_type`        | auto    | Noise family (see table)                                               |
| `scale`             | `1.0`   | Frequency of the noise pattern                                         |
| `octaves`           | `5`     | fBm/ridged/billow/musgrave octave count (1..16)                        |
| `lacunarity`        | `2.0`   | Frequency multiplier between successive octaves                        |
| `gain`              | `0.5`   | Amplitude decay between successive octaves (fbm/ridged/billow)         |
| `fractal_increment` | `1.0`   | Musgrave H — only hetero_terrain / hybrid_multifractal                 |
| `fractal_offset`    | `0.7`   | Musgrave offset / "sea level" — only hetero_terrain / hybrid_multifractal |
| `distortion`        | `0`     | Domain-warp amplitude (organic / non-axis-aligned)                     |
| `noise_strength`    | --      | Legacy: 0 = smooth Perlin, >0 = turbulent                              |

When `noise_type` is omitted, the texture falls back to legacy behaviour
driven by `noise_strength` — so existing scenes render unchanged.

**Musgrave multifractals.** `hetero_terrain` and `hybrid_multifractal`
are the two "true terrain" fractals from Ebert/Musgrave/Peachey/Perlin
*Texturing &amp; Modeling, 3rd ed.* §16.3. Unlike fBm — whose statistics
are identical at every altitude — they multiply each octave's contribution
by the running accumulated value (heterogeneous) or by a running weight
(hybrid), so high ground picks up more roughness and valleys stay smooth.
`H` (the fractal increment, default 1.0) controls how fast the high
frequencies decay; H ≈ 0.25 gives rough mountains, H ≥ 1 gives smooth
hills. `offset` (default 0.7) is the per-octave additive bias, the
"sea level" knob. See `scenes/showcases/texture-musgrave-multifractal.yaml`
for the four-panel comparison fBm / hetero / hybrid / low-offset alpine.

### Marble

```yaml
texture:
  type: "marble"
  scale: 2.4
  vein_axis: [0, 1, 0]
  warp_amplitude: 0.9
  warp_iterations: 2
  fold_amplitude: [0.8, 0.25, 0.45]
  vein_layers: 2
  vein_scale:  [1.0, 2.4]
  vein_weight: [1.0, 0.50]
  vein_thickness: 0.13
  vein_softness: 0.07
  colors: [[0.96, 0.95, 0.94], [0.32, 0.34, 0.40]]
```

Marble is built from four orthogonal blocks: recursive (Inigo Quilez) domain
warp, anisotropic geological fold, multi-scale ridged vein field, and a
smoothstep thickness remap. Each block solves a specific failure mode of
naïve sine-carrier marble:

| Parameter         | Default                | What it controls                                                 |
|-------------------|------------------------|------------------------------------------------------------------|
| `vein_axis`       | `[0,1,0]`              | Dominant direction of the anisotropic fold                       |
| `warp_amplitude`  | `0.75`                 | IQ recursive warp magnitude — kills every visible tiling         |
| `warp_iterations` | `2`                    | Warp depth: 0 = no warp, 2 = canonical, 3 = aggressive flow      |
| `fold_amplitude`  | `[0.6, 0.2, 0.4]`      | Per-axis shear amplitude (max component aligns with `vein_axis`) |
| `vein_layers`     | `2`                    | 1..3 independent ridged layers composited via soft-max           |
| `vein_scale`      | `[1.0, 2.6]`           | Per-layer scale — decoupling makes thin + thick veins coexist    |
| `vein_weight`     | `[1.0, 0.55]`          | Per-layer soft-max weight                                        |
| `vein_thickness`  | `0.15`                 | Fraction of surface occupied by veins (monotone)                 |
| `vein_softness`   | `0.08`                 | Smoothstep half-width on the thickness threshold                 |
| `color_variation` | `0.08`                 | How much background fBm shifts the ramp lookup                   |
| `impurities_density` | `0.0`               | Sparse Voronoi specks (inclusions); 0 disables the inline path   |
| `impurities_texture` | `null`              | External texture replacing the inline impurities path            |

**Why this beats sine-carrier marble.** The classic `sin(scale·(p·axis) +
turb(p))` formula guarantees visible periodicity along `vein_axis` no
matter how chaotic the turbulence is — that's the "CG 2000" tell. Here
the carrier is a ridged multifractal field (sharp natural ridges, never
sinusoidal), the input position is pre-warped recursively (IQ "warp warp
warp"), and the global slab is sheared anisotropically by the fold —
three independent mechanisms each killing periodicity at a different
scale.

The multi-scale layer system replaces the old "primary + optional
secondary wave" hack: instead of two sinusoids, you blend 1-3 independent
ridged fields via a numerically-stable log-sum-exp soft-max. The
soft-max is a smooth `max()` — wherever one layer has a higher ridge,
it wins; the boundaries between dominant layers are C¹ continuous and
free of aliasing. This is what makes Calacatta-style slabs work: thin
fine veins coexist with bold thick veins because each is a separate
layer.

```yaml
# Calacatta — 3 layers + ramp 4-stop
texture:
  type: "marble"
  scale: 1.9
  vein_axis: [0, 1, 0]
  warp_amplitude: 1.1
  fold_amplitude: [0.95, 0.35, 0.55]
  vein_layers: 3
  vein_scale:  [0.65, 1.5, 3.4]
  vein_weight: [1.0, 0.70, 0.40]
  vein_thickness: 0.22
  vein_softness: 0.10
  color_ramp:
    - { position: 0.00, color: [0.97, 0.95, 0.90], interp: "linear" }
    - { position: 0.30, color: [0.92, 0.85, 0.72], interp: "smoothstep" }
    - { position: 0.65, color: [0.85, 0.62, 0.28], interp: "smoothstep" }
    - { position: 1.00, color: [0.18, 0.10, 0.05], interp: "linear" }
```

See `scenes/showcases/library-marbles-v3.yaml` for the 6-sphere comparison
(Carrara / Marquina / Verde Alpi / Calacatta / Statuario / Arabescato).

### Wood

The wood texture is a production-grade annual-ring model. The legacy symmetric
`sin(dist)^sharpness` carrier has been replaced with an asymmetric
earlywood/latewood profile, per-ring random width and colour variation,
recursive IQ domain warp, multi-band noise, open-pore vessels, and
3-D cone knot projection — every algorithmic upgrade documented below
ships ON by default with sensible values.

```yaml
texture:
  type: "wood"
  scale: 4.5
  grain_strength: 1.8
  ring_axis: [0, 1, 0]
  latewood_width: 0.24
  ring_sharpness: 4.0
  ring_color_variation: 0.22
  ring_width_variation: 0.18
  warp_amplitude: 0.55
  pore_density: 0.45
  pore_aspect: 6.0
  color_ramp:
    - { position: 0.00, color: [0.26, 0.14, 0.05] }
    - { position: 0.55, color: [0.76, 0.55, 0.28] }
    - { position: 1.00, color: [0.92, 0.78, 0.50] }
```

Rings form perpendicular to `ring_axis`: use `[0, 1, 0]` for a tree
trunk seen on cross-cut, `[0, 0, 1]` for a plank, or a slightly tilted
vector for a more organic look.

#### Key parameters

| Parameter              | Default      | Description                                              |
|------------------------|--------------|----------------------------------------------------------|
| `ring_axis`            | `[0,1,0]`    | Trunk / log axis (rings live in the ⊥ plane)             |
| `grain_strength`       | `1.5`        | Amplitude of the high-freq fBm grain band                |
| `latewood_width`       | `0.22`       | Width of the dark latewood band per ring (0.15-0.30)     |
| `ring_sharpness`       | `3.0`        | Crispness of the latewood transition (1-6)               |
| `earlywood_transition` | `0.05`       | Smooth rise out of the prior latewood (0.005-0.5)        |
| `ring_color_variation` | `0.15`       | Per-ring colour shift amplitude (the realism upgrade)    |
| `ring_width_variation` | `0.10`       | Per-ring radial-width offset amplitude                   |
| `warp_amplitude`       | `0.4`        | Recursive IQ domain warp amplitude                       |
| `warp_iterations`      | `2`          | 0 = no warp, 2 = canonical IQ, 3 = heavy flow            |
| `fold_amplitude`       | `[0.3,0.1,0.3]` | Per-axis anisotropic fold amplitude                  |
| `space_stretch`        | `[1,1,1]`    | Linear pre-stretch (non-isotropic plank cuts)            |
| `grain_scale`          | `1.0`        | Frequency multiplier on the grain sample point           |
| `figure_strength`      | `0.0`        | 0 = disabled, 0.4-1.5 = curly maple / flame mahogany     |
| `figure_scale`         | `0.25`       | Frequency multiplier on the figure sample point          |
| `figure_aspect`        | `1.0`        | Axial elongation of figure; 3-5 = perpendicular stripes  |
| `axial_grain`          | `0.0`        | Long-wave noise along the trunk axis                     |
| `pore_density`         | `0.0`        | Open-pore vessels (0.30-0.55 for oak / ash / walnut)     |
| `pore_scale`           | `16.0`       | Pore spatial frequency                                   |
| `pore_aspect`          | `4.0`        | Axial elongation of pores (4-6 = vessel-like)            |
| `pore_strength`        | `0.4`        | Pore darkening strength                                  |
| `radial_anisotropy`    | `0.0`        | 0 = plain-sawn, 3-5 = quartersawn medullary rays         |
| `knot_density`         | `0.0`        | 3-D cone knots (0.5-1.0 for pine / spruce / cedar)       |
| `knot_scale`           | `0.6`        | Knot frequency multiplier                                |
| `heartwood_radius`     | `0.0`        | Sapwood/heartwood transition radius (0 = disabled)       |
| `heartwood_blend`      | `0.25`       | +ve darkens centre (natural for walnut / cherry)         |
| `output`               | `"color"`    | `"mask"` for FloatTexture-driven Disney params            |

#### Why the rewrite?

The legacy `sin(ring · scale) ^ sharpness` carrier produced perfectly
symmetric rings — dark on both ends, bright in the middle — and every
ring was identical. Real annual rings are **asymmetric**: a long bright
earlywood plateau followed by a thin sharp dark latewood band that
becomes the visible dark line at the boundary with the next year's ring.
Combined with deterministic per-ring random width and colour shifts (no
two rings look the same in nature), this is the single biggest "looks
fake → looks real" upgrade.

- **Asymmetric ring profile.** `latewood_width` controls the width of
  the dark band at the END of each annual ring (not centred on the
  middle as in the legacy symmetric profile). The boundary between two
  rings is the visible "dark line" of real wood.
- **Per-ring variation.** `ring_color_variation` and `ring_width_variation`
  apply a deterministic per-ring hash so adjacent rings differ in
  brightness and width — the single feature that makes wood look real
  instead of CG. Keep both around 0.10-0.25 for natural year-to-year
  variation; 0 is the "every ring identical" legacy look (avoid).
- **Recursive IQ domain warp.** `warp_amplitude` + `warp_iterations`
  replace the single-iteration `distortion` knob. The legacy
  `distortion:` YAML key is mapped to `warp_amplitude` for back-compat.
- **Multi-band noise.** `grain_strength` (high-freq fBm fibre detail)
  + `figure_strength` (low-freq curly / flame / ribbon undulations) +
  `axial_grain` (long-wave along axis). The figure band can be axially
  elongated via `figure_aspect` to align its stripes perpendicular to
  the grain — natural orientation of curly maple and flame mahogany.
- **Open-pore vessels.** `pore_density` spawns sparse dark micro-specks
  via an axially-anisotropic Worley — cells are elongated along the
  trunk axis by `pore_aspect` to look like the short cylindrical
  channels of real open-pore species. 0 = closed-pore (maple, beech,
  cherry, ebony) and bypasses Worley entirely.
- **Sapwood / heartwood gradient.** `heartwood_radius` defines the
  radial transition centre; `heartwood_blend > 0` darkens toward the
  centre, modelling the heartwood/sapwood demarcation of walnut,
  cherry, ipe.
- **`radial_anisotropy`.** Stretches the noise sample along the local
  radial direction. High values (~3-5) reproduce the quartersawn-oak
  medullary "tiger ray" look.
- **`knot_density`.** 3-D cone projection — each sparse Worley cell
  hosts a knot whose visible cone widens with axial distance from the
  cell centre. Combine with a 4-5 stop `color_ramp:` for cuore-nodo
  / latewood / earlywood / sapwood tone authoring.

#### Mask-driven Disney parameters

Set `output: "mask"` on a wood texture block to return the scalar ring
parameter `t ∈ [0, 1]` (1 at the bright earlywood plateau, 0 at the
dark latewood / pore) packed as `(t, t, t)`. Drop the same block under
`roughness_texture` / `sheen_texture` to drive scalar BSDF parameters
from the latewood pattern — latewood can be polished while earlywood
stays matte (the "cera su quercia" look), sheen can ride on the
open-pore earlywood only.

```yaml
texture:
  type: "wood"
  scale: 4.5
  grain_strength: 1.8
  latewood_width: 0.24
  ring_sharpness: 4.0
  ring_color_variation: 0.22
  pore_density: 0.48
  pore_aspect: 6.0
  color_ramp:
    - { position: 0.00, color: [0.26, 0.14, 0.05] }
    - { position: 1.00, color: [0.92, 0.78, 0.50] }
roughness: 0.55
roughness_texture:
  type: "wood"
  scale: 4.5
  grain_strength: 1.8
  latewood_width: 0.24
  ring_sharpness: 4.0
  ring_color_variation: 0.22
  pore_density: 0.48
  pore_aspect: 6.0
  output: "mask"
  color_ramp:
    - { position: 0.0, color: [0.32, 0.32, 0.32] }   # latewood → polished
    - { position: 1.0, color: [0.78, 0.78, 0.78] }   # earlywood → matte
```

See `scenes/showcases/library-woods-v3.yaml` for the six-sphere
comparison (oak / quartersawn / curly maple / knotty pine / flame
mahogany / burr walnut), and copy the canonical `quercia_pro_mask`
preset from `scenes/presets/materials-wood.md` for the mask
recipe applied to a full polished-oak look.

---

### 3.8.1 Marble & Wood Studio Lookdev — A Practical Walkthrough

This sub-chapter is the longest in the materials tutorial because nailing
photo-real stone and wood is one of the hardest things in any procedural
renderer. The look-and-feel depends on many interlocking choices: lighting,
BSDF parameters, vein geometry, sharpening response, ramp authoring,
randomization. We'll walk through all of them, with copy-paste recipes
from the reference showcase.

#### Step 1 — Fix the lighting before you tune the texture

A common trap: you write a "perfect" Carrara material, render, and the
spheres come out bluish-grey. The texture is correct — the lighting
isn't. **Polished marble at `roughness < 0.2` is essentially a mirror**,
and on a textureless sky-gradient environment it picks up the sky colour
instead of letting the diffuse texture read.

The studio backdrop used by `library-marble-wood.yaml`:

```yaml
world:
  sky:
    type: "flat"
    color: [0.001, 0.001, 0.0012]   # near-black: no environment reflection

lights:
  # Strong direct key — dominates over the specular reflection,
  # the diffuse texture becomes visible.
  - type: "directional"
    direction: [-0.4, -0.8, 0.45]
    color: [1.0, 0.98, 0.94]
    intensity: 6.5
    angular_radius: 0.6          # soft shadow edges
  - type: "point"
    position: [-7, 6, -4]
    color: [0.90, 0.93, 1.00]    # cool fill
    intensity: 55
  - type: "point"
    position: [0, 3.5, 5]
    color: [1.0, 0.82, 0.62]     # warm rim from behind for silhouette
    intensity: 45
```

For an interior render with a textured environment (HDRI, kitchen
windows, etc.) you can keep a brighter sky — the HDRI content will
reflect interestingly off the marble. For a clean lookdev sphere, near-
black is the safe default.

#### Step 2 — Choose the marble personality

| Marble        | Layers | Thickness | Softness | Warp iter | Fold amp                | Notes                          |
|---------------|--------|-----------|----------|-----------|-------------------------|--------------------------------|
| Carrara       | 2      | 0.13      | 0.07     | 2         | `[0.8, 0.25, 0.45]`     | Thin grey-blue veins on white  |
| Marquina      | 2      | 0.13      | 0.05     | 2         | `[0.8, 0.20, 0.48]`     | Inverted ramp, thin pale veins |
| Calacatta     | 3      | 0.22      | 0.10     | 2         | `[0.95, 0.35, 0.55]`    | Cream + gold + dark vein       |
| Statuario     | 3      | 0.20      | 0.08     | 3         | `[1.0, 0.35, 0.55]`     | Pure white + grey dramatic     |
| Arabescato    | 3      | 0.34      | 0.12     | 3         | `[1.1, 0.5, 0.7]`       | Chaotic banded grey/white      |
| Port Laurent  | 2      | 0.20      | 0.10     | 2         | `[0.85, 0.30, 0.50]`    | Gold veins on black            |
| Verde Alpi    | 2      | 0.20      | 0.09     | 2         | `[0.85, 0.30, 0.55]`    | + `impurities_density: 0.06`   |

The table reads top-to-bottom as "more chaotic, more nuanced": Carrara is
restrained, Arabescato is geological. Bumping `warp_iterations` from 2 to
3 unlocks the truly organic flow needed by Statuario and Arabescato but
adds ~3 Perlin lattice samples per shade — the shading cost is worth it
on the marble protagonist of a scene.

#### Step 3 — Thickness convention (Carrara is mostly white)

`vein_thickness` is the fraction of slab area mapped to the vein region.
Real marble is mostly base colour with thin vein structures, so the
defaults aim low:

- **`vein_thickness = 0.10`** — hairline veins. Marquinia / Nero Belgio.
- **`vein_thickness = 0.13–0.18`** — thin veins on a dominant base.
  **Carrara / Statuario grade.**
- **`vein_thickness = 0.22–0.30`** — medium veins. **Calacatta / Port
  Laurent grade.**
- **`vein_thickness = 0.30–0.40`** — chaotic banded surface. **Arabescato
  grade.**
- **`vein_thickness ≥ 0.40`** — diffuse nebulae rather than veins.
  **Onyx / alabaster grade** (pair with high `vein_softness` 0.20-0.30
  for the watery transitions).

Because the dominant area is *base*, when you author a `color_ramp`:

```yaml
color_ramp:
  - { position: 0.00, color: <BASE COLOUR> }   # dominant, t → 0
  - ...                                        # transitions, mid-stops
  - { position: 1.00, color: <VEIN COLOUR> }   # rare, t → 1
```

This is the **opposite** of the legacy sin-carrier convention. The new
field's natural reading is "ridge magnitude": low values are the
background (base), high values are the peaks (vein). If you accidentally
reverse the ramp, the material renders mostly vein-coloured — a quick
sanity check before committing a slab.

#### Step 4 — Multi-scale layers vs single-layer

Real Calacatta has *both* thin veins and bold thick veins on the same
slab. The new system reaches this without any "secondary wave" hack: you
add independent ridged layers at decoupled scales and let a numerically
stable soft-max pick the dominant one per pixel.

```yaml
vein_layers: 3
vein_scale:  [0.65, 1.5, 3.4]   # large — medium — fine
vein_weight: [1.0,  0.70, 0.40] # leading layer dominates the silhouette
```

The soft-max is the smooth equivalent of `max()`: where one layer's
ridge is higher, that layer wins; transitions between dominant layers
are C¹ continuous (no aliasing). At `soft_max_sharpness = 8` the
crossover is crisp; lower it for softer blending.

**Tip:** keep `vein_scale[1] / vein_scale[0]` and `vein_scale[2] /
vein_scale[1]` close to ~2.3, not integer ratios — that prevents the
finer layers from beating audibly against the coarse one.

#### Step 5 — Roughness, clearcoat, and the "polished marble" trick

For a kitchen-counter-grade polished marble you want **two specular
layers**:

```yaml
roughness: 0.32       # base layer — diffuse texture still reads
specular: 0.5
clearcoat: 0.9        # polished varnish on top
coat_roughness: 0.05  # near-mirror clearcoat
```

The `clearcoat` is a second specular layer over the base. The base layer
is rough enough that the marble pattern survives, the coat adds the
glass-like sheen on top. For a "satinato" / honed finish, drop the
clearcoat entirely and raise `roughness` to 0.4–0.5.

#### Step 6 — Wood: pick the cut and the figure

Wood has three orthogonal cuts: plain-sawn, quartersawn, and rift. Plus
optional figure (curly, flame, bird's-eye, burl) and optional knots.

| Wood look       | `grain_strength` | `figure_strength` | `radial_anisotropy` | `pore_density` | `knot_density` |
|-----------------|------------------|-------------------|---------------------|----------------|----------------|
| Plain-sawn oak  | 1.8              | 0.30              | 0.0                 | 0.45           | 0.0            |
| Quartersawn oak | 2.0              | 0.55              | 4.5                 | 0.45           | 0.0            |
| Curly maple     | 0.25             | 1.5–1.8 (aspect 4)| 0.0                 | 0.0            | 0.0            |
| Bird's-eye      | 0.25             | 0.4 + scale 0.55  | 0.0                 | 0.0            | 0.3 (aspect 1) |
| Flame mahogany  | 0.4              | 1.4–1.6 (aspect 6)| 0.0                 | 0.0            | 0.0            |
| Knotty pine     | 0.8              | 0.3 (subtle)      | 0.0                 | 0.0            | 1.0            |
| Burr walnut     | 0.5              | 1.4               | 0.0                 | 0.0            | 0.5            |

The pattern: **figure dominates the grain** — to get a clean figure look
lower the grain (`grain_strength` ≤ 0.6) so the high-frequency fibres
don't drown the slow undulations. The other way around for plain oak:
figure subtle, grain dialled up to 1.8+ for clear fibrous lines, and
`pore_density` 0.4+ for the vessel speckling.

**Always set `ring_color_variation` and `ring_width_variation`** in the
0.15-0.25 range. They are the single biggest "looks fake → looks real"
upgrade — without per-ring random shifts every ring looks identical and
the texture immediately reads as CG.

#### Step 7 — Ring sharpness vs. latewood width

The new asymmetric profile splits the legacy `ring_sharpness` into two
orthogonal knobs:

- `latewood_width` — fraction of each ring occupied by the dark latewood
  band at the END. 0.15-0.20 for hardwoods (maple, walnut, cherry),
  0.22-0.30 for softwoods (pine, spruce, cedar).
- `ring_sharpness` — crispness of the latewood transition. 1.0 = soft
  S-curve; 3-6 = razor-sharp dark line.

Combined with `scale`:

- `scale = 3`, `latewood_width = 0.18`, `ring_sharpness = 2.5` — soft
  wide bands. Acero or faggio look.
- `scale = 4.5`, `latewood_width = 0.24`, `ring_sharpness = 4.0` —
  classic oak / walnut look with crisp latewood line.
- `scale = 6`, `latewood_width = 0.28`, `ring_sharpness = 5.0` — tight
  rings with razor latewood. Slow-growth pine, larice vecchio.

#### Step 8 — Open-pore vessels (oak, ash, walnut, mahogany)

Set `pore_density` > 0 to enable the open-pore vessel pass. The texture
samples an axially-anisotropic Worley field — cells are elongated along
the trunk axis by `pore_aspect` to look like the short cylindrical
channels of real vessels. Use:

- **Oak / ash:** `pore_density: 0.42-0.48`, `pore_scale: 16-18`,
  `pore_aspect: 5-6`, `pore_strength: 0.50`.
- **Walnut:** `pore_density: 0.40`, `pore_aspect: 5.5`.
- **Mahogany:** `pore_density: 0.25`, `pore_aspect: 5.0` (semi-pore).
- **Cera / verniciato finishes:** lower `pore_strength` to 0.40 (the
  finish partially fills the pores).
- **Closed-pore species** (maple, beech, cherry, ebony): leave
  `pore_density: 0` and bypass Worley entirely.

#### Step 9 — Authoring knot rings

When `knot_density > 0` the texture projects 3-D cone-shaped knots in
the trunk axis direction. Each sparse Worley cell hosts at most one
knot; inside the knot's visible cone the ring centre is pulled toward
the knot feature point and a dark heart is added on top. Two rules:

1. **`scale` ≥ 5** so the knot can host visible internal rings. A
   small `scale` makes knots read as dark spots, not knots.
2. **4-stop ramp** that reserves position 0 for the knot heart:
   ```yaml
   color_ramp:
     - { position: 0.00, color: [0.05, 0.03, 0.02] }   # KNOT HEART (very dark)
     - { position: 0.18, color: [0.35, 0.18, 0.08] }   # latewood
     - { position: 0.65, color: [0.90, 0.68, 0.40] }   # earlywood
     - { position: 1.00, color: [0.97, 0.86, 0.60] }   # sapwood
   ```
   The `t *= (1 − knotDarken)` step inside the texture pushes `t → 0`
   at knot centres regardless of which ring band the sample fell into,
   so position 0 always shows. Without that dedicated knot stop, the
   knot would just darken the local ring colour — visible but less
   recognisable as a knot.

#### Step 9b — Sapwood / heartwood radial gradient

Walnut, cherry, ipe and other species with strong heart/sapwood
demarcation use `heartwood_radius` + `heartwood_blend`:

```yaml
heartwood_radius: 1.6     # in pre-scale units
heartwood_blend: 0.22     # +ve darkens centre (cuore)
```

For planks cut from the inner portion of the trunk this produces the
classic darker centre / lighter edges look that no amount of grain
tuning can reach.

#### Step 9 — Randomization for instancing

Procedural textures sample in object-local space, so **two instances of the
same material placed at different world positions already read the same
texture region** (each primitive's local origin coincides with its own
center). Without an extra knob, a row of identical wooden spheres would
therefore show the **identical** pattern. The randomization knob breaks that:

```yaml
texture:
  type: "wood"
  randomize_offset: true     # decorrelate sample point (±10 wu hash-of-seed)
  randomize_rotation: true   # different ring-axis orientation per object
```

Each entity gets a different `objectSeed` (from `seed:` on the entity, or
auto-incremented otherwise), and `TextureTransform.Apply()` adds a per-seed
offset and rotation before the procedural samples. The offset magnitude is
**±10 wu** — large enough to decorrelate fBm / voronoi / marble (≫ one
feature period at typical `scale` values) but small enough that radial
procedurals like `wood` keep their concentric-ring curvature.
**`randomize_rotation` is the primary tool** for shared wood/marble materials
because it gives the most natural look; `randomize_offset` is an additional
decorrelation knob, useful but no longer required.

#### Step 10 — The pre-baked catalogue

The preset catalogues `scenes/presets/materials-stone.md` and
`materials-wood.md` ship 14 studio-quality recipes ready to copy. Open the
catalogue, copy the `materials:` block of the recipe you want into your scene,
and reference its `id`:

```yaml
materials:
  # Pasted from scenes/presets/materials-stone.md
  - id: "calacatta_studio_lucido"
    type: "disney"
    # ...

entities:
  - { type: "sphere", center: [0, 1, 0], radius: 1, material: "calacatta_studio_lucido" }
```

Catalogue (`_studio` suffix throughout):
- **Marbles:** `dis_carrara_studio`, `dis_carrara_studio_lucido`,
  `dis_calacatta_studio`, `dis_calacatta_studio_lucido`,
  `dis_statuario_studio`, `dis_statuario_studio_lucido`,
  `dis_arabescato_studio`, `dis_arabescato_studio_lucido`,
  `dis_port_laurent_studio_lucido`, `dis_rosso_levanto_studio_lucido`
  + Classic Lambertian variants.
- **Woods:** `dis_acero_curly_studio`, `dis_acero_birdseye_studio`,
  `dis_acero_sapwood_studio`, `dis_mogano_flame_studio`,
  `dis_quercia_quartato_studio`, `dis_frassino_quartato_studio`,
  `dis_pino_nodoso_studio`, `dis_abete_nodoso_studio`,
  `dis_noce_burl_studio` + Classic variants.

Each is tuned with the recipes above. Start from a `_studio` entry,
swap colour ramp stops to match your reference photo, and you have a
production-ready material.

### Voronoi / Worley (cellular)

```yaml
texture:
  type: "voronoi"
  scale: 5.0
  metric: "euclidean"        # euclidean | manhattan | chebyshev | euclidean_squared
  output: "f2_minus_f1"      # f1 | f2 | f3 | f4 |
                             # f2_minus_f1 | f3_minus_f1 |
                             # f1_plus_f2 | cell | random | position
  randomness: 0.9
  smoothness: 0.0            # 0 = hard min (classic); ∈ (0,1] enables Smooth Voronoi
  colors: [[0.05, 0.05, 0.05], [0.95, 0.90, 0.70]]
```

Worley cellular noise is the workhorse for pebbles, stones, foam,
cracked mud, reptile skin and abstract tile patterns. The output mode
selects the visual:

- `f1` — distance to the nearest feature → stone / pebble blobs.
- `f2` — second-nearest distance.
- `f3`, `f4` — 3rd and 4th nearest distances (hierarchical cellular
  shading, voronoi-on-voronoi, multi-scale leather).
- `f2_minus_f1` — sharp ridges between cells (the famous "crackle").
- `f3_minus_f1` — wider, lower-frequency border band (soft rims,
  mortar gradients).
- `cell` — raw RGB hash per cell. Bright saturated rainbow;
  **ignores `colors:` / `color_ramp:`**. Use it as an unconstrained
  stochastic-RGB identifier or as input to a downstream hue/sat /
  mix-RGB node.
- `random` — scalar in [0, 1) per cell, mapped through `colors:` /
  `color_ramp:`. **This is what you want for almost every "rocks /
  scales / patches" material** — the cells stay inside your muted
  palette instead of producing rainbow.
- `position` — cell-local XYZ of the F1 feature point as RGB. A
  deterministic 3D stochastic-ID, decorrelated from `cell`; useful to
  seed downstream procedurals.

`metric: "chebyshev"` produces square / hex tiling. `randomness: 0`
collapses features onto a regular grid; `1` is full random scatter.

> **Colour order for `f2_minus_f1`.** `F2 - F1` is **zero on the cell
> boundary** and reaches its **maximum at the cell centre**. The lerp
> applies a sqrt response, so `colors[0]` is what you see ON the edges
> and `colors[1]` is what you
> see in the cell interiors. For the classic crackle look — bright thin
> lines on dark background — write `colors: [[bright], [dark]]`. The
> example above intentionally does this.

> **Smooth Voronoi (`smoothness`).** Setting `smoothness > 0` swaps the
> hard `min()` over the 27-cell neighbourhood for Inigo Quilez' log-sum-exp
> soft-min `-log(Σ exp(-k·d_i)) / k` with `k = 20/smoothness`. F1 becomes
> C∞-continuous everywhere; F2 is built from the same accumulator with
> the dominant weight excluded so `f2_minus_f1` loses its sharp V-ridge —
> bordi morbidi, no step alias along the cracks. Use it for polished
> leather, water-rounded pebbles, supple reptile skin, closed-pore marble.
> `smoothness = 0` (default) is bit-identical to the legacy behaviour;
> the `cell` / `random` outputs are intentionally unaffected (per-cell
> lookup is discrete).
> See `scenes/showcases/texture-voronoi-smooth.yaml` for the three-sphere
> hard / 0.3 / 0.7 comparison for the three smoothness levels.

> **Extended outputs (`f3`, `f4`, `f3_minus_f1`, `position`).** These four
> channels expose the 3rd / 4th nearest feature distance, a wider crackle
> band, and the cell-local XYZ of the F1 feature point. Same O(27) cost as
> F1/F2 — the 27 neighbouring cells are already scanned. They always use
> the hard min (smoothness is intentionally ignored for discrete-topology
> channels) and `position` also
> bypasses `color_ramp:` because it is a vector identity output, not a
> scalar. See `scenes/showcases/texture-voronoi-extended-outputs.yaml`
> for the 6-sphere side-by-side comparison.

> **`cell` vs `random` — picking the right per-cell channel.** Beginners
> reach for `cell` first because the name fits — they want "one colour per
> cell". Then they wonder why their muted grey palette comes back as
> magenta and lime. **`cell` is a raw RGB hash that ignores your palette**.
> The channel you actually want for palette-aware
> per-cell colour — rocks within a brown range, scales within a green
> range, terrazzo tiles within a cream range — is `random`. It hashes the
> cell ID to a scalar and lerps your `colors:` (or samples your
> `color_ramp:`) on it, exactly like the distance outputs. Rule of thumb:
> if you wrote `colors:` you almost certainly want `random`; reach for
> `cell` only when you genuinely want unconstrained stochastic RGB
> identifiers (e.g. as input to a downstream hue/sat node).

### Brick

```yaml
texture:
  type: "brick"
  brick_width: 0.4
  brick_height: 0.18
  mortar_size: 0.025
  row_offset: 0.5
  color_variation: 0.6
  noise_scale: 0.15
  colors:
    - [0.72, 0.32, 0.22]    # brick A
    - [0.52, 0.18, 0.12]    # brick B
    - [0.86, 0.83, 0.78]    # mortar
```

Running-bond brickwork on the XY plane with three colours (brick A,
brick B, mortar). `row_offset: 0` switches to stack-bond. Set
`noise_scale > 0` to add weathered per-brick variation.

### Gradient

```yaml
texture:
  type: "gradient"
  mode: "spherical"          # linear | quadratic | easing | spherical | radial
  axis: [0, 1, 0]
  length: 1.0
  colors: [[1.0, 0.85, 0.30], [0.10, 0.05, 0.30]]
```

Useful for art direction (skies inside materials, atmosphere domes,
hand-tuned roughness ramps). `linear` projects onto `axis`; `spherical`
uses distance from the origin; `radial` uses distance from the `axis`
line (cylindrical falloff).

### Coordinate (Texture Coordinate node)

```yaml
texture:
  type: "coordinate"
  mode: "object"             # object | uv | generated | world
  scale: 1.0
  bounds_min: [-1, -1, -1]   # only used in mode: "generated"
  bounds_max: [1, 1, 1]
```

Returns the shading point's coordinates as RGB. Useful for debug overlays
and as an explicit coordinate driver for downstream textures. Two principal
uses:

1. **Debug overlay** to verify UV unwraps and object/world space
   alignment at a glance. Pop a `mode: "uv"` texture on a sphere and
   you see the spherical UV seam line immediately; `mode: "world"`
   shows whether the BVH has correctly placed the geometry in world
   space.
2. **Deterministic XYZ driver** to feed another texture (via mix
   material) with an explicit coordinate system instead of the
   implicit object-local sample point every procedural uses by
   default.

- `object` — `fract(LocalPoint · scale)`. Same space every other
  procedural samples in.
- `uv` — `(u, v, 0)` raw. Smooth gradient, no fract.
- `generated` — bounds-normalised reference-space. Declare the canonical
  AABB and every node downstream sees a tidy `[0, 1]³` parameter
  regardless of transforms / displacement.
- `world` — `fract(rec.Point · scale)`. World-locked grid that does
  NOT follow the object — useful for laser-grids, world-aligned dust,
  "you-are-here" debug spheres.

See `scenes/showcases/texture-coordinate.yaml` for the
4-sphere side-by-side comparison (one per mode).

### Multi-Stop Color Ramp

Every procedural texture except `brick` accepts an optional `color_ramp:`
block that overrides the implicit two-colour lerp baked into the texture.
This unlocks looks that the two-colour `colors: [A, B]` shortcut
cannot express — Statuario marble with golden mid-tone, sapwood/heartwood
wood, photo-real sunset gradients, toon bands, voronoi heat maps.

```yaml
texture:
  type: "marble"
  vein_thickness: 0.20
  color_ramp:
    - { position: 0.00, color: [0.95, 0.93, 0.88], interp: "linear"     }
    - { position: 0.45, color: [0.55, 0.45, 0.32], interp: "smoothstep" }
    - { position: 1.00, color: [0.05, 0.05, 0.07], interp: "linear"     }
```

- `position` ∈ [0, 1] — clamped to range; stops auto-sort ascending.
- `color: [r, g, b]` — linear-space RGB.
- `interp` describes the *outgoing* segment of each stop:
  - `linear` — straight lerp (default).
  - `smoothstep` — `3t² − 2t³` Hermite cubic (C¹).
  - `ease` — `6t⁵ − 15t⁴ + 10t³` Perlin smootherstep (C²).
  - `constant` — hold the colour until the next stop.
- Out-of-range parameter clamps to the nearest stop's colour.
- Coincident stops (same `position`) produce a hard break.
- When both `colors:` and `color_ramp:` are supplied, `color_ramp:` wins.
- Omitting `color_ramp:` keeps the legacy two-colour behaviour
  byte-identical to before — scenes that don't use the feature don't
  change.

### Texture Transform and Randomization

All procedural textures support these additional parameters:

| Parameter            | Type      | Default | Description                   |
|----------------------|-----------|---------|-------------------------------|
| `offset`             | `[x,y,z]` | --     | Shift the texture in 3D space  |
| `rotation`           | `[x,y,z]` | --     | Rotate the texture pattern     |
| `randomize_offset`   | `bool`    | `false`| Random offset per instance (uses entity `seed`) |
| `randomize_rotation` | `bool`    | `false`| Random rotation per instance    |

The `randomize_*` flags are extremely useful when the same material is
applied to multiple objects: each instance gets a unique texture variation
so the objects do not all look identical.

To get deterministic randomization, set the `seed` field on each entity:

```yaml
entities:
  - type: "sphere"
    seed: 42
    material: "wood_with_random"
    ...
```

---

## 3.9 Image Textures

```yaml
- id: "earth"
  type: "lambertian"
  texture:
    type: "image"
    path: "textures/earth.png"
    uv_scale: [1.0, 1.0]
```

Image textures load a picture from a file and wrap it onto the surface
using the object's UV coordinates.

| Parameter  | Type       | Default    | Description                     |
|------------|------------|------------|---------------------------------|
| `path`     | `string`   | --         | Relative path to image file     |
| `uv_scale` | `[U, V]`  | `[1, 1]`  | Tile the texture (2.0 = repeat twice) |

Supported formats: PNG, JPEG, BMP, GIF, TIFF, WebP.

The path is resolved relative to the scene file's directory. If your scene
is at `scenes/my-scene.yaml` and the texture is at
`scenes/textures/brick.png`, use `path: "textures/brick.png"`.

---

## 3.10 Normal Maps

Normal maps add the illusion of surface detail (bumps, grooves, scratches)
without adding actual geometry. They work by perturbing the surface
normal at each shading point.

```yaml
- id: "brick_wall"
  type: "disney"
  color: [0.65, 0.3, 0.2]
  roughness: 0.7
  normal_map:
    path: "textures/brick-normal.png"
    strength: 1.0
    uv_scale: [2.0, 2.0]
```

| Parameter  | Type       | Default | Description                         |
|------------|------------|---------|-------------------------------------|
| `path`     | `string`   | --     | Path to the normal map image         |
| `strength` | `float`    | `1.0`  | Perturbation intensity (0 = flat, 1 = full) |
| `uv_scale` | `[U, V]`  | --     | UV tiling for the normal map         |
| `flip_y`   | `bool`     | `false`| Flip Y axis (set true for DirectX-format maps) |

3D-Ray uses the OpenGL normal map convention (Y-up). If your normal maps
come from a tool that uses the DirectX convention (Y-down), set
`flip_y: true`.

Normal maps can be added to any material type that supports them
(Lambertian, Metal, Disney). They are applied before all shading
computations.

---

## 3.11 Bump Maps

Bump maps are the conceptual cousin of normal maps but with one crucial
difference: the input is a **scalar height field** sampled from any
procedural or image texture, not a baked RGB normal asset. The shading
normal is perturbed via tangent-space finite differences of the
luminance (Blinn 1978).

```yaml
- id: "marble_with_bump"
  type: "disney"
  color: [0.78, 0.78, 0.80]
  roughness: 0.4
  bump_map:
    texture:                   # ANY ITexture: noise, marble, wood,
      type: "marble"           # voronoi, brick, gradient, image, ...
      scale: 2.0
      vein_axis: [0, 1, 0]
      vein_layers: 2
      vein_thickness: 0.3
      colors: [[0, 0, 0], [1, 1, 1]]
    strength: 3.0              # 0–10, clamped
    scale: 1.0                 # uniform UV multiplier (default 1)
```

| Parameter  | Type           | Default | Description                                                  |
|------------|----------------|---------|--------------------------------------------------------------|
| `texture`  | TextureData    | —       | Inner height-field texture. Procedural or image.            |
| `strength` | float ∈ [0,10] | `1.0`   | Amplitude of the perturbation. Above ~5 the bump looks rocky. |
| `scale`    | float > 0      | `1.0`   | Uniform UV multiplier on top of the inner texture's own scaling. |

**Why bump maps when we already have normal maps?**

- **Procedural input**. The bump source can be a `noise`, `marble`,
  `wood`, `voronoi`, `brick`, `gradient`, or `checker` texture — no
  pre-baked asset required, infinite resolution at any zoom level.
- **Texture reuse**. Any image texture already used for albedo can be
  fed straight into `bump_map` as a height field — the luminance becomes
  the height, gradient direction becomes the perturbation axis.
- **Composes with normal maps**. If both are present, `normal_map`
  applies first (medium-frequency relief), then `bump_map` layers
  high-frequency detail on top.

The clearcoat lobe of `disney` materials keeps its independent
`coat_normal_map` and does **not** see the bump perturbation — the coat
sits on a stable substrate so scratches and orange-peel look correct.

See `scenes/showcases/texture-bump-map.yaml` for a side-by-side
comparison of bumps derived from `noise`, `marble`, and a concrete image
texture against a flat reference panel.

---

## 3.11.5 Surface Displacement (material-level)

Bump maps perturb only the shading normal. **Surface displacement**
takes the next step: it physically moves the vertices of a subdivided
mesh before the BVH is built, so the **silhouette** changes — not just
the shading. The displacement lives on the material: one displaced
material drives every mesh that uses it, with no per-entity duplication.

```yaml
materials:
  - id: "carved_stone"
    type: "disney"
    color: [0.82, 0.66, 0.42]
    roughness: 0.78
    displacement:
      mode: "scalar"                   # scalar | vector
      texture:                          # any procedural or image
        type: "noise"
        noise_type: "fbm"
        scale: 3.5
        octaves: 5
        colors: [[0, 0, 0], [1, 1, 1]]
      scale: 0.30                       # signed world-unit amplitude
      midlevel: 0.5                     # texture value treated as "flat"
      bound: 0.30                       # BVH padding (auto-derived if omitted)
      displacement_method: "both"       # both | displacement | bump_only
      autobump: true
      autobump_strength: 1.5

entities:
  - type: "mesh"
    path: "models/stone.obj"
    material: "carved_stone"            # ← any mesh that uses this material
    subdivision_scheme: "catmull_clark"  #   is displaced; no per-entity block
    subdivision_iterations: 5
```

### Modes

**Scalar.** Each micro-vertex moves along its smooth normal by
`scale · (h − midlevel)`, where `h = Rec.709 luminance(texture)`. This
is the canonical "height-field displacement". After the pass the shading
normals are recomputed from
the displaced topology so the BSDF sees the new silhouette.

**Vector.** Each micro-vertex moves by `scale · (rgb − midlevel) · basis`,
where the basis is the per-vertex TBN frame (`space: tangent`, R→T,
G→B, B→N) or the identity
(`space: object`, RGB is added directly to the local position). Vector
mode is what produces **overhangs** and **crinkles** that a height
field cannot represent — exactly how sculpted hi-res detail is baked
onto a low-poly cage.

### `displacement_method`

- `both` (default) — geometric displacement + autobump (if requested).
- `displacement` — geometric only; no autobump even if requested.
- `bump_only` — skip the geometric pass; treat the texture as a pure
  bump map. Useful for quick lookdev: same material, dial geometric on
  or off without rewriting blocks.

### Autobump

Setting `autobump: true` derives a residual bump map from the same
displacement texture and attaches it to the mesh. The renderer applies
it on top of any material-level `bump_map` at shading time, recovering
sub-pixel detail finer than the subdivision grid resolved geometrically.
The bump amplitude is
`autobump_strength · |scale|`; `autobump_scale > 1` samples the bump
finer than the displacement (macro-displacement + micro-bump workflow).

### Composition order

```
geometry normal (post-displacement)
  → material.normal_map
    → material.bump_map
      → mesh.autobump            (← derived from displacement.texture)
```

`coat_normal_map` on Disney materials remains independent — it
perturbs only the clearcoat lobe.

### Per-instance override

A single mesh entity can suppress an inherited displacement via
`displacement_enabled: false` on the entity block. The material remains
shared; this one instance is flat. Useful for LOD copies or proxies.

### Mix-displacement

A `MixMaterial` with `displacement: { blend_with_mask: true }`
vector-blends the two children's per-vertex displacement offsets using
the SAME mask the BSDF mix uses, so the geometry is C0-continuous
across material seams:

```yaml
- id: "weathered_rock"
  type: "mix"
  material_a: "rock_clean"              # both children must have their own
  material_b: "rock_moss"               # displacement (loader warns otherwise)
  mask:
    type: "noise"
    scale: 3.0
  displacement:
    blend_with_mask: true
```

The resulting autobump is also blended via the same mask
(`MixBumpMapTexture`), so the recovered sub-pixel detail follows the
material boundary smoothly.

### Mesh-only — and how to fix it on spheres

Material-level displacement is applied only to `type: mesh` entities.
Sphere/cylinder/box/CSG entities that reference a displaced material
emit a load-time warning and use the surface shading without the
geometric pass.

Displacement needs a polygon mesh, a UV/tangent frame, and a subdivision
pass to expose enough vertices for the deformation. An analytical sphere
has none of those. If you put a material with `displacement: { scale: 0.02 }`
on a `type: "sphere"`, you'll get the shading (colour + roughness +
bump_map) but not the lumps in the silhouette.

**The fix: replace the analytical primitive with a mesh proxy and let
adaptive subdivision do the work.** The engine ships unit-radius
polygonal proxies in `scenes/models/`:

| Proxy file                       | Replaces             | Subdivision  |
|----------------------------------|----------------------|--------------|
| `subdivision-icosahedron.obj`    | analytical sphere    | `loop`       |
| `subdivision-cube.obj`           | analytical cube      | `catmull_clark` |

**Recipe — analytical sphere ⇒ subdivided icosahedron.** Take a
`type: "sphere"` entity at `(x, y, z)` with `radius: r`:

```yaml
# Before — displacement silently dropped:
- name: "rock"
  type: "sphere"
  center: [0, 0.9, 0]
  radius: 0.9
  material: "dis_cemento_lavato_chiaro"     # ← has displacement

# After — displacement applied:
- name: "rock"
  type: "mesh"
  path: "../models/subdivision-icosahedron.obj"
  subdivision_scheme: "loop"
  subdivision_pixel_error: 6.0              # adaptive: stop when projected
  subdivision_max_iterations: 5             #   edges fall below 6 px
  scale: [0.9, 0.9, 0.9]                    # icosahedron is unit radius
  translate: [0, 0.9, 0]                    # ← was center
  material: "dis_cemento_lavato_chiaro"
```

**Why adaptive (`subdivision_pixel_error`) over fixed iterations?**
Adaptive subdivision keeps the cost proportional to the on-screen
size: a sphere that occupies 30 px of the final image gets the
minimum subdivision (still enough for displacement amplitude to read);
a sphere filling 800 px refines automatically until edges fall under
the pixel-error threshold. For a typical 1920 × 1080 render with a
mid-range FOV, `subdivision_pixel_error: 6.0` and
`subdivision_max_iterations: 5` is a good default — caps the
icosahedron at ≈5120 triangles, fine enough for every displaced
material in the catalogues.

Use fixed iterations (`subdivision_iterations: 4`) only when you need
deterministic geometry counts — regression tests, CI snapshot diffs,
or pre-baking a mesh for export.

**Other primitives.**

- **Boxes / cubes**: `subdivision-cube.obj` with `subdivision_scheme:
  "catmull_clark"` — Catmull-Clark naturally rounds the corners as
  iterations increase, so for a sharp displaced box clamp
  `subdivision_max_iterations: 2`.
- **Torus / cylinder / lathe surfaces**: there's no stock subdivided
  proxy. If you need displacement on those silhouettes, model the
  shape as an OBJ once (export with quads if you plan to use Catmull-Clark;
  triangles if you'll use Loop) and load it as a
  mesh with subdivision.
- **CSG operations**: displacement on CSG output is not currently
  supported (CSG works on analytical primitives; the intersection /
  difference / union is computed at intersection time, after which
  there's no mesh to displace). Use a "boolean-baked" OBJ instead.

**All `scenes/showcases/library-*.yaml` files demonstrating a
material family use this pattern.** Look at `library-concretes.yaml`,
`library-leathers.yaml`, `library-marbles-v2.yaml` — the row
of five demo spheres is built with subdivided icosahedra, so every
displaced material shows its true geometric profile
side-by-side with the non-displaced ones.

### Showcases

- `scenes/showcases/texture-displacement-scalar.yaml` — height-field
  panels (Perlin fBm, Voronoi cracks) and a ridged-fBm asteroid.
- `scenes/showcases/texture-displacement-vector.yaml` — scalar vs
  tangent-space vs object-space side by side, plus a CC×4 cube with
  ridged-fBm vector displacement.
- `scenes/showcases/texture-displacement-combo.yaml` — flat /
  displacement only / displacement + autobump / displacement + autobump
  + material bump, at deliberately moderate subdivision to make the
  recovery visible.
- `scenes/showcases/texture-displacement-material-mix.yaml` — two
  meshes sharing one displaced material (reuse), one instance with
  `displacement_enabled: false` (bypass), and a Mix-displacement
  vector-blending Perlin × Voronoi.

---

## 3.12 Complete Example: Material Gallery

A scene that showcases eight different materials side by side.

```yaml
# material-gallery.yaml
# Eight spheres on pedestals, each with a different material.

world:
  sky:
    type: "flat"
    color: [0.02, 0.02, 0.03]

cameras:
  - name: "main"
    position: [0, 3, -10]
    look_at: [0, 1.2, 0]
    fov: 48

lights:
  # Key light (large area for soft shadows)
  - type: "area"
    corner: [-4, 5, -3]
    u: [8, 0, 0]
    v: [0, 0, 6]
    color: [1.0, 0.97, 0.92]
    intensity: 30.0

  # Fill light from lower right
  - type: "point"
    position: [6, 2, -5]
    color: [0.7, 0.8, 1.0]
    intensity: 25.0

materials:
  # Floor
  - id: "floor"
    type: "lambertian"
    color: [0.3, 0.3, 0.3]

  # 1. Matte red (Lambertian)
  - id: "mat_lambertian"
    type: "lambertian"
    color: [0.8, 0.15, 0.1]

  # 2. Brushed gold (Metal)
  - id: "mat_metal"
    type: "metal"
    color: [1.0, 0.76, 0.33]
    fuzz: 0.15

  # 3. Clear glass (Dielectric)
  - id: "mat_glass"
    type: "dielectric"
    refraction_index: 1.52

  # 4. Blue glow (Emissive)
  - id: "mat_emissive"
    type: "emissive"
    color: [0.2, 0.4, 1.0]
    intensity: 8.0

  # 5. Car paint (Disney, clearcoat)
  - id: "mat_carpaint"
    type: "disney"
    color: [0.6, 0.02, 0.02]
    metallic: 0.0
    roughness: 0.35
    clearcoat: 1.0
    clearcoat_gloss: 0.9

  # 6. Marble (Disney + texture)
  - id: "mat_marble"
    type: "disney"
    roughness: 0.1
    specular: 0.8
    texture:
      type: "marble"
      scale: 12.0
      noise_strength: 5.0
      colors: [[0.95, 0.92, 0.88], [0.5, 0.48, 0.45]]

  # 7. Wood (Disney + texture)
  - id: "mat_wood"
    type: "disney"
    roughness: 0.35
    clearcoat: 0.4
    clearcoat_gloss: 0.8
    texture:
      type: "wood"
      scale: 6.0
      noise_strength: 1.5
      colors: [[0.55, 0.35, 0.18], [0.30, 0.18, 0.08]]

  # 8. Checker floor (Disney + checker texture)
  - id: "mat_checker"
    type: "disney"
    roughness: 0.15
    specular: 0.6
    texture:
      type: "checker"
      scale: 0.5
      colors: [[0.9, 0.88, 0.85], [0.15, 0.12, 0.1]]

entities:
  # Floor
  - type: "infinite_plane"
    point: [0, 0, 0]
    normal: [0, 1, 0]
    material: "floor"

  # Row of spheres at y=1.3 (on imaginary pedestals)
  - type: "sphere"
    center: [-5.25, 1.3, 0]
    radius: 0.65
    material: "mat_lambertian"

  - type: "sphere"
    center: [-3.75, 1.3, 0]
    radius: 0.65
    material: "mat_metal"

  - type: "sphere"
    center: [-2.25, 1.3, 0]
    radius: 0.65
    material: "mat_glass"

  - type: "sphere"
    center: [-0.75, 1.3, 0]
    radius: 0.65
    material: "mat_emissive"

  - type: "sphere"
    center: [0.75, 1.3, 0]
    radius: 0.65
    material: "mat_carpaint"

  - type: "sphere"
    center: [2.25, 1.3, 0]
    radius: 0.65
    material: "mat_marble"

  - type: "sphere"
    center: [3.75, 1.3, 0]
    radius: 0.65
    material: "mat_wood"

  - type: "sphere"
    center: [5.25, 1.3, 0]
    radius: 0.65
    material: "mat_checker"
```

Render with:

```
RayTracer -i material-gallery.yaml -w 1600 -H 600 -s 1024 -d 8 -S 4
```

---

## What You Have Learned

- **Dielectric** materials are transparent with controllable refraction.
  Glass needs higher ray depth.
- **Emissive** materials glow and automatically act as light sources.
- The **Disney/PBR** material replaces all other types with 12 intuitive
  parameters. Use it for any surface from metal to glass to fabric.
- **Mix/Blend** materials spatially combine two materials using a blend
  factor or a texture mask.
- **Procedural textures** (checker, noise, marble, wood) generate
  patterns from 3D coordinates -- no image files needed.
- **Image textures** wrap a picture file onto a surface via UV mapping.
- **Normal maps** add surface detail without extra geometry.

---

[Previous: Your First Scene](./02-first-scene.md) | [Next: All the Shapes](./04-geometric-primitives.md) | [Tutorial Index](./README.md)
