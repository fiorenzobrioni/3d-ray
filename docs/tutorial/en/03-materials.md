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

Every glass surface a ray enters and exits costs two bounces. The default
depth is `-d 8` (enough for most scenes thanks to Russian Roulette). If you
have nested glass objects (e.g. a glass of water, bottles behind bottles),
raise the ray depth to at least 16:

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

The difference from explicit lights (area, sphere, point) is that
emissive entities are **visible** in the scene: they appear in
reflections, refractions, and behind glass. An explicit area light is
invisible -- it illuminates the scene but has no visible form.

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
subsurface scattering, sheen, and transmission into a single material
with intuitive parameters. You can use it instead of lambertian, metal,
or dielectric for any surface.

Type aliases: `disney`, `disney_bsdf`, `pbr` (all create the same
material).

### Complete Parameter Reference

| Parameter             | Default  | Range        | Description                                     |
|-----------------------|----------|--------------|-------------------------------------------------|
| `color`               | --       | 0--1         | Base albedo color                                |
| `metallic`            | `0.0`    | 0--1         | 0 = dielectric (plastic, wood), 1 = conductor (metal) |
| `roughness`           | `0.5`    | 0--1         | 0 = mirror-smooth, 1 = fully diffuse            |
| `subsurface`          | `0.0`    | 0--1         | Blend toward subsurface scattering diffuse model |
| `specular`            | `0.5`    | 0--1         | Dielectric specular intensity (Fresnel F0)       |
| `specular_tint`       | `0.0`    | 0--1         | Tint specular reflection by base color           |
| `sheen`               | `0.0`    | 0--1         | Grazing-angle soft highlight (fabric, velvet)    |
| `sheen_tint`          | `0.5`    | 0--1         | Tint the sheen by base color                     |
| `sheen_roughness`     | `0.3`    | 0.04--1      | Charlie sheen α — width of the grazing halo     |
| `clearcoat`           | `0.0`    | 0--1         | Second specular lobe (lacquer, varnish)          |
| `clearcoat_gloss`     | `1.0`    | 0--1         | **Legacy** — prefer `coat_roughness` (≈ `1 - clearcoat_gloss`) |
| `coat_ior`            | `1.5`    | 1+           | Clearcoat IOR (overrides the default lacquer)   |
| `coat_roughness`      | `-1.0`   | -1 or 0--1   | Sentinel `-1` falls back to `clearcoat_gloss`; any `≥ 0` enables the Arnold coat and `clearcoat_gloss` is ignored |
| `coat_normal_map`     | --       | image path   | Normal map applied **only** to the clearcoat lobe |
| `spec_trans`          | `0.0`    | 0--1         | Specular transmission (0 = opaque, 1 = glass)    |
| `transmission_color`  | `[1,1,1]`| 0--1         | Colour of the glass interior at `transmission_depth` |
| `transmission_depth`  | `0.0`    | 0+           | Distance (scene units) at which `transmission_color` is reached |
| `ior`                 | `1.5`    | 1+           | Index of refraction (specular and transmission) |
| `anisotropic`         | `0.0`    | 0--1         | 0 = isotropic, 1 = stretched along the tangent  |
| `anisotropic_rotation`| `0.0`    | 0--1         | Fraction of 2π rotation around the normal       |
| `diff_trans`          | `0.0`    | 0--1         | Diffuse transmission fraction (foliage, thin fabric) |
| `flatness`            | `0.0`    | 0--1         | Blend Lambert → HK-flat diffuse shape           |
| `thin_walled`         | `false`  | bool         | Skip refraction / double-hit — foliage, paper   |
| `subsurface_color`    | --       | 0--1 colour  | Tint used by the subsurface / flatness / diff_trans lobes |
| `subsurface_radius`   | --       | `[R,G,B]` ≥ 0 | **Unused** — parsed for a future random-walk SSS pipeline, currently has no effect |
| `thin_film_thickness` | `0.0`    | 0+ (nm)      | Iridescent film thickness (bubbles, opal, AR)   |
| `thin_film_ior`       | `1.5`    | 1+           | Iridescent film IOR (η₂)                        |
| `texture`             | --       | --           | Procedural or image texture (replaces color)    |
| `normal_map`          | --       | --           | Surface detail via normal perturbation          |

> **Texturing every parameter.** Every scalar parameter accepts a
> `*_texture` variant (e.g. `roughness_texture`) and the three colour
> inputs (`color`, `transmission_color`, `subsurface_color`) accept a
> matching `*_texture` block. Example: `roughness_texture: { type: "image", path: "rough.png" }`.

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
5. **Subsurface** (`subsurface` > 0): light penetrates the surface and
   diffuses inside. Gives a softer, flatter look to thin objects.
   Used for skin, wax, porcelain, leaves.
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

**Porcelain (subsurface):**
```yaml
- id: "porcelain"
  type: "disney"
  color: [0.95, 0.93, 0.88]
  roughness: 0.15
  specular: 0.7
  subsurface: 0.3
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

**Car paint with Arnold-style clearcoat and a coat normal map:**
```yaml
- id: "metallic_pearl"
  type: "disney"
  color: [0.05, 0.1, 0.35]
  metallic: 0.9
  roughness: 0.25
  clearcoat: 1.0
  coat_ior: 1.55
  coat_roughness: 0.15         # any ≥ 0 value switches to Arnold coat
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
  subsurface_color: [0.35, 0.65, 0.25]
```

**Porcelain skin (HK flatness + subsurface tint):**
```yaml
- id: "porcelain_skin"
  type: "disney"
  color: [0.95, 0.82, 0.76]
  metallic: 0.0
  roughness: 0.4
  subsurface: 0.5
  subsurface_color: [1.0, 0.55, 0.45]
  flatness: 0.4
```

### Quick Cheat-Sheet

A compact reference covering the whole Disney surface taxonomy.
Use it to pick a starting point, then tune `roughness` and `specular` for
the specific hero look. Only the non-default keys are listed — omit a key
to keep its default.

| Material family | Core recipe |
|---|---|
| Matte diffuse (plaster, unfinished wood) | `roughness: 0.9`, `specular: 0.2`, optional `sheen: 0.1–0.2` |
| Flat matte (paper, concrete) | `roughness: 0.85`, `flatness: 0.5–0.8`, `specular: 0.2` |
| Polished plastic | `metallic: 0`, `roughness: 0.2–0.4`, `specular: 0.5`, optional `clearcoat: 0.3` |
| Rubber / silicone | `metallic: 0`, `roughness: 0.7–0.9`, `specular: 0.25`, `sheen: 0.2`, `sheen_roughness: 0.5` |
| Velvet / fabric | `roughness: 0.9`, `sheen: 1.0`, `sheen_tint: 0.7`, `sheen_roughness: 0.2–0.4` |
| Skin / porcelain | `metallic: 0`, `roughness: 0.4`, `subsurface: 0.5`, `subsurface_color: [0.9, 0.5, 0.45]`, `flatness: 0.3`, `sheen: 0.05` |
| Leaf / paper (translucent) | `roughness: 0.4`, `thin_walled: true`, `diff_trans: 0.5`, `subsurface_color: <interior tint>`, optional `flatness: 0.3` |
| Polished metal (gold, silver, chrome) | `metallic: 1`, `roughness: 0.02–0.15`, `specular: 0.9–1.0` |
| Rough / satin metal | `metallic: 1`, `roughness: 0.4–0.7`, `specular: 0.6` |
| Brushed metal | `metallic: 1`, `roughness: 0.25`, `anisotropic: 0.7–0.9`, `anisotropic_rotation: 0.0–1.0` |
| Car paint (legacy slider) | `metallic: 0`, `roughness: 0.3`, `clearcoat: 1`, `clearcoat_gloss: 0.9` |
| Car paint (Arnold coat) | `metallic: 0–0.9`, `roughness: 0.25`, `clearcoat: 1`, `coat_ior: 1.55`, `coat_roughness: 0.05–0.15` |
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

### Checker

```yaml
texture:
  type: "checker"
  scale: 1.0
  colors: [[0.9, 0.9, 0.9], [0.1, 0.1, 0.1]]
```

A 3D checkerboard pattern that alternates between two colors. The `scale`
controls the size of each square (smaller scale = larger squares).

### Noise (Perlin)

```yaml
texture:
  type: "noise"
  scale: 4.0
  noise_strength: 1.0
```

Organic, cloud-like variation based on Perlin noise. The color output is
driven by the noise function and the material's base `color`.

| Parameter        | Default | Description                            |
|------------------|---------|----------------------------------------|
| `scale`          | `1.0`   | Frequency of the noise pattern         |
| `noise_strength` | --      | Turbulence (0 = smooth, higher = choppier) |

### Marble

```yaml
texture:
  type: "marble"
  scale: 8.0
  noise_strength: 5.0
  colors: [[0.93, 0.90, 0.87], [0.55, 0.53, 0.50]]
```

Simulates marble veining. `colors` defines two colors: the base stone
and the vein color. `noise_strength` controls how pronounced the veins
are. Higher values create wilder, more turbulent veining.

### Wood

```yaml
texture:
  type: "wood"
  scale: 6.0
  noise_strength: 1.5
  colors: [[0.55, 0.35, 0.18], [0.35, 0.20, 0.10]]
```

Simulates wood grain with concentric rings. `colors` defines the early
wood (lighter rings) and late wood (darker rings). `noise_strength`
controls the irregularity of the rings -- higher values create knots and
figure.

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

## 3.11 Complete Example: Material Gallery

A scene that showcases eight different materials side by side.

```yaml
# material-gallery.yaml
# Eight spheres on pedestals, each with a different material.

world:
  ambient_light: [0.02, 0.02, 0.03]
  background: [0.02, 0.02, 0.03]

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
    shadow_samples: 16

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
