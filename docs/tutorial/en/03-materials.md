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

Explicit `area` and `sphere` lights are now also visible via an
internally-managed emissive proxy (Arnold/Cycles parity), so the choice
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

**Sampling space — important.** Every procedural samples in **object-local
space**: the origin of the sample coordinates sits on the primitive's own
`Center` / `Q` / `Point` anchor, and the axes are world-aligned. Same
convention as Arnold's `space: object`, Cycles' "Texture Coordinate → Object",
RenderMan's `Pref`. The practical consequence is that `scale` is in
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

3D-Ray ships a full pro-grade fractal noise stack — the same family of
modes you find in Arnold's `noise`, Cycles' Noise/Musgrave Texture and
RenderMan's `PxrFractal`:

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
  scale: 4.0
  noise_strength: 10.0
  vein_axis: [1, 0, 0.3]
  vein_sharpness: 5.0
  octaves: 7
  distortion: 0.25
  colors: [[0.92, 0.91, 0.88], [0.18, 0.18, 0.22]]
```

Real Carrara marble has thin, high-contrast, off-axis veins. The new
controls let you reproduce that look:

| Parameter        | Default     | Description                                        |
|------------------|-------------|----------------------------------------------------|
| `vein_axis`      | `[0,0,1]`   | Primary vein propagation direction                  |
| `vein_frequency` | `1.0`       | Multiplier on the sine term frequency               |
| `vein_sharpness` | `1.0`       | 1 = soft (legacy), 4–8 = thin Carrara-style veins   |
| `noise_type`     | `turbulence`| `turbulence` / `fbm` / `ridged` modulator           |
| `octaves`        | `7`         | Octave count for the modulator                      |
| `distortion`     | `0`         | Domain warp on the input position                   |
| `secondary_wave` | --          | Optional cross-vein wave (Statuario / Calacatta)    |

**Studio-quality cross-veining (`secondary_wave`).** Real Statuario,
Calacatta, and Arabescato marbles run their veins along two non-parallel
directions. Setting `secondary_wave.strength > 0` adds a second sinusoid
along `secondary_wave.axis` to the primary vein term — the combined
sine `sin(wave1) + strength · sin(wave2)` is renormalised so the
output range stays well-defined. The secondary axis is auto-orthogonalised
against the primary at sample time, so even picking a collinear axis
produces visible cross-veining. `strength = 0` (default) is bit-identical
to the single-axis legacy output. Pair with a 3+ stop `color_ramp:` for
vein → mid-tone → base → undertone authoring.

```yaml
texture:
  type: "marble"
  vein_axis: [0, 0, 1]
  secondary_wave:
    axis: [1, 0, 0]
    frequency: 0.7
    strength: 0.5
  color_ramp:
    - { position: 0.0, color: [0.20, 0.16, 0.18], interp: "smoothstep" }  # dark vein
    - { position: 0.3, color: [0.78, 0.62, 0.30], interp: "smoothstep" }  # warm gold
    - { position: 0.7, color: [0.96, 0.94, 0.90], interp: "smoothstep" }  # ivory base
    - { position: 1.0, color: [0.90, 0.92, 0.96], interp: "linear"     }  # cool undertone
```

See `scenes/showcases/library-marble-wood.yaml` for the
Carrara / Calacatta / Arabescato comparison.

### Wood

```yaml
texture:
  type: "wood"
  scale: 5.0
  noise_strength: 1.2
  ring_axis: [0, 1, 0]
  ring_sharpness: 3.5
  axial_grain: 0.4
  octaves: 4
  distortion: 0.18
  colors: [[0.78, 0.55, 0.30], [0.42, 0.24, 0.12]]
```

Rings form perpendicular to `ring_axis`: use `[0, 1, 0]` for a tree
trunk seen on cross-cut, `[0, 0, 1]` for a plank, or a slightly tilted
vector for a more organic look. `ring_sharpness` exponentiates a
triangular wave around each ring boundary, producing the dark
latewood lines you see in oak or walnut. `axial_grain` adds long-
wavelength variation along the trunk axis (great for planks).

| Parameter            | Default   | Description                                              |
|----------------------|-----------|----------------------------------------------------------|
| `ring_axis`          | `[0,1,0]` | Trunk / log axis (rings live in the ⊥ plane)             |
| `ring_sharpness`     | `1.0`     | 1 = soft (legacy), 3–6 = defined latewood                |
| `axial_grain`        | `0.0`     | Long-wave variation along the trunk axis                  |
| `octaves`            | `1`       | fBm octaves on the grain (1 = legacy single Perlin)       |
| `distortion`         | `0`       | Domain warp — 0 = clean rings, ~0.5 = knots/waves         |
| `grain_scale`        | `1.0`     | Multiplier on the high-freq grain sample point            |
| `figure_scale`       | `0.25`    | Multiplier on the low-freq figure sample point             |
| `figure_strength`    | `0.0`     | 0 = disabled, ~0.5–1.5 = curly maple / flame mahogany     |
| `radial_anisotropy`  | `0.0`     | 0 = plain-sawn (isotropic), >0 = quartersawn-stretched    |
| `knot_density`       | `0.0`     | 0 = no knots, ~0.5 = sparse knots, ~1 = packed knots      |

**Studio-quality wood.** Four new opt-in knobs upgrade the wood texture
to the Arnold / RenderMan / Cycles parity tier:

- **Two-band perturbation** — `grain_scale` + `noise_strength` (alias
  `grain_strength`) drive the high-frequency fibre detail inside each
  ring; `figure_scale` + `figure_strength` add the independent low-frequency
  plank-wide undulation that gives **curly maple** its stripes, **flame
  mahogany** its ripples, and **bird's-eye** its blooms. The figure band
  is sampled at a decorrelated noise offset so the two bands don't lock
  step.
- **`radial_anisotropy`** — compresses the noise sample's radial
  component, so noise varies slowly along the radial direction. This is
  the visual difference between **plain-sawn** (default 0, isotropic
  features) and **quartersawn** boards (high anisotropy, fibres extend
  radially). The implementation is safe on the trunk axis itself
  (`radial.Length() == 0`) — the path falls back silently.
- **`knot_density`** — sparse small-scale Voronoi spawns branch knots
  that locally pull the ring centre toward the knot feature point and
  add a dark heart on top. Same trick as Arnold's `knots` and
  RenderMan's `PxrWoodKnot`. Combine with a 3-stop `color_ramp:` for
  sapwood / heartwood / knot tri-tone authoring.

```yaml
texture:
  type: "wood"
  scale: 3.0
  noise_strength: 1.5
  ring_axis: [0, 1, 0]
  ring_sharpness: 3.0
  figure_scale: 0.22
  figure_strength: 0.6
  knot_density: 0.7
  color_ramp:
    - { position: 0.00, color: [0.18, 0.10, 0.06], interp: "smoothstep" }  # knot heart
    - { position: 0.20, color: [0.55, 0.32, 0.16], interp: "smoothstep" }  # latewood
    - { position: 0.65, color: [0.90, 0.72, 0.45], interp: "smoothstep" }  # earlywood
    - { position: 1.00, color: [0.96, 0.86, 0.65], interp: "linear"     }  # sapwood
```

See `scenes/showcases/library-marble-wood.yaml` for the
six-sphere comparison: Carrara / Calacatta / Arabescato marbles + oak
quartersawn / curly maple / knotty pine.

---

### 3.8.1 Marble & Wood Studio Lookdev — A Practical Walkthrough

This sub-chapter is the longest in the materials tutorial because nailing
photo-real stone and wood is one of the hardest things in any procedural
renderer. The Arnold and RenderMan default marble shaders ship with
**dozens** of knobs precisely because the look-and-feel depends on so
many interlocking choices: lighting, BSDF parameters, vein geometry,
sharpening response, ramp authoring, randomization. We'll walk through
all of them, with copy-paste recipes from the reference showcase.

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

| Marble       | Sharpness   | Secondary wave | Distortion | Ramp                                  |
|--------------|-------------|----------------|------------|----------------------------------------|
| Carrara      | 4.0         | none           | none       | 2-color (white base, near-black vein)  |
| Calacatta    | 3.0         | strength 0.45  | none       | 4-stop (vein → gold → cream → ivory)   |
| Statuario    | 3.5         | strength 0.35  | 0.15       | 3-stop (vein → grey → white)           |
| Arabescato   | 2.0         | strength 0.7   | 0.35       | 3-stop (black vein → grey → ivory)     |
| Port Laurent | 3.0         | strength 0.4   | none       | 3-stop (gold vein → brown → black)     |
| Rosso Levanto| 4.0         | strength 0.4   | none       | 3-stop (white calcite → red → dark)    |

The four-axis table reads top-to-bottom as "more chaotic, more nuanced":
Carrara is geometric, Arabescato is geological.

#### Step 3 — Sharpness convention (don't get this wrong)

`vein_sharpness` controls how wide the vein region is relative to the
base. The relationship is `t' = 1 − (1−t)^k` where `t = (sin(...) + 1)/2`
is the underlying sine wave, so:

- **`vein_sharpness = 1`** — no sharpening, soft 50/50 blend. Looks like
  pre-step-5 "legacy" output. Veins are wide and blurry.
- **`vein_sharpness = 3`** — average sample lands ~75% of the way from
  vein to base. Bold veins ~25% of surface area. **Calacatta-grade.**
- **`vein_sharpness = 5`** — average ~83%. Thin filigree veins. Real
  Carrara look.
- **`vein_sharpness = 8`** — average ~89%. Hairline veins, ramp must
  have a high-frequency stop or the vein vanishes visually.

Because the dominant area is *base*, when you author a `color_ramp`:

```yaml
color_ramp:
  - { position: 0.00, color: <VEIN COLOUR> }   # rare, t→0
  - ...                                        # transitions, mid-stops
  - { position: 1.00, color: <BASE COLOUR> }   # dominant, t→1
```

If you reverse this (base at position 0, vein at position 1) the
material renders mostly vein-coloured — that's how you'd accidentally
get a "black marble with white veins" look from a Carrara YAML.

#### Step 4 — Secondary wave for cross-veining

Real Calacatta has *two* vein directions: large diagonal veins crossed
by smaller transverse veins. We model this by adding a second sinusoid
along an axis that's automatically orthogonalised against the primary at
sample time (so even a collinear `axis: [0, 0, 1]` still produces
visible cross-veining):

```yaml
secondary_wave:
  axis: [1, 0, 0]                  # secondary direction hint
  frequency: 0.65                  # non-integer ratio → no moiré
  strength: 0.45                   # ≤1 typical; 0 = single-axis (back-compat)
```

The combined signal `sin(w1) + strength·sin(w2)` is renormalised by
`(1 + strength)` so the output range stays in [-1, 1] and the sharpening
curve still works.

**Frequency tip:** if you pick `secondary_wave.frequency` as a non-trivial
ratio of `vein_frequency` (e.g. 0.65, 0.85, 1.2) the cross-pattern is
aperiodic and moiré-free. Equal frequencies produce a regular grid
pattern that looks artificial.

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

| Wood look       | `noise_strength` | `figure_strength` | `radial_anisotropy` | `knot_density` |
|-----------------|------------------|-------------------|---------------------|----------------|
| Plain-sawn oak  | 2.2              | 0.0               | 0.0                 | 0.0            |
| Quartersawn oak | 2.2              | 0.0               | 2.5–3.5             | 0.0            |
| Curly maple     | 0.25             | 1.5–1.8           | 0.0                 | 0.0            |
| Bird's-eye      | 0.15             | 1.0–1.4 + scale 0.45 | 0.0              | 0.0            |
| Flame mahogany  | 0.4              | 1.3–1.5           | 0.0                 | 0.0            |
| Knotty pine     | 0.6              | 0.3 (subtle)      | 0.0                 | 0.7–1.0        |
| Walnut burl    | 0.5              | 1.4               | 0.0                 | 0.6            |

The pattern: **figure dominates the grain** — to get a clean figure look
you must lower the grain (`noise_strength` ≤ 0.6) so the high-frequency
fibres don't drown the slow undulations. The other way around for plain
oak: figure off, grain dialled up to 2.0+ for clear fibrous lines.

#### Step 7 — Ring sharpness vs. scale

`ring_sharpness` controls the latewood band width (the dark line at the
end of each year's growth). Combined with `scale`:

- `scale = 3`, `ring_sharpness = 1` — soft, wide bands. Oak from a young
  fast-growing tree (legacy default).
- `scale = 4.5`, `ring_sharpness = 4` — clear latewood band ~10% of the
  ring width. Classic oak / walnut look.
- `scale = 6`, `ring_sharpness = 5` — tight rings, hairline latewood.
  Old-growth fir, slow-growth pine.
- `scale = 6`, `ring_sharpness = 8` — very tight rings, almost grating-
  like. Aliases on small spheres, only use on close-ups.

#### Step 8 — Authoring knot rings

When `knot_density > 0` the texture spawns small-scale Voronoi knots in
the plane perpendicular to `ring_axis`. Inside a knot the ring centre
is pulled toward the knot feature, producing concentric rings around the
knot — exactly like a branch cross-section embedded in the trunk wood.
Two rules:

1. **High `scale`** (≥ 5): so the knot can host visible internal rings.
   A small `scale` makes knots look like dark spots, not knots.
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

The library bundles 14 studio-quality materials ready to import:

```yaml
imports:
  - { path: "scenes/libraries/materials/stones.yaml" }
  - { path: "scenes/libraries/materials/woods.yaml" }

entities:
  - { type: "sphere", center: [0, 1, 0], radius: 1, material: "dis_calacatta_studio_lucido" }
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
- `cell` — raw RGB hash per cell (Cycles "Color" output). Bright
  saturated rainbow; **ignores `colors:` / `color_ramp:`**. Use it as
  an unconstrained stochastic-RGB identifier or as input to a
  downstream hue/sat / mix-RGB node.
- `random` — scalar in [0, 1) per cell, mapped through `colors:` /
  `color_ramp:`. Matches Cycles 3.0+ "Random" output. **This is what
  you want for almost every "rocks / scales / patches" material** —
  the cells stay inside your muted palette instead of producing
  rainbow.
- `position` — cell-local XYZ of the F1 feature point as RGB. A
  deterministic 3D stochastic-ID, decorrelated from `cell`; useful to
  seed downstream procedurals (Cycles Position output, RenderMan
  PxrVoronoise position).

`metric: "chebyshev"` produces square / hex tiling. `randomness: 0`
collapses features onto a regular grid; `1` is full random scatter.

> **Colour order for `f2_minus_f1`.** `F2 - F1` is **zero on the cell
> boundary** and reaches its **maximum at the cell centre**. The lerp
> applies a sqrt response (mirroring Cycles' "Distance to Edge"), so
> `colors[0]` is what you see ON the edges and `colors[1]` is what you
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
> hard / 0.3 / 0.7 comparison and parity check with Cycles' Smooth F1.

> **Extended outputs (`f3`, `f4`, `f3_minus_f1`, `position`).** These four
> channels expose the 3rd / 4th nearest feature distance, a wider crackle
> band, and the cell-local XYZ of the F1 feature point. Same O(27) cost as
> F1/F2 — the 27 neighbouring cells are already scanned. They always use
> the hard min (smoothness is intentionally ignored, same convention
> Cycles uses for its discrete-topology channels) and `position` also
> bypasses `color_ramp:` because it is a vector identity output, not a
> scalar. See `scenes/showcases/texture-voronoi-extended-outputs.yaml`
> for the 6-sphere side-by-side comparison.

> **`cell` vs `random` — picking the right per-cell channel.** Beginners
> reach for `cell` first because the name fits — they want "one colour per
> cell". Then they wonder why their muted grey palette comes back as
> magenta and lime. **`cell` is Cycles' Color output: a raw RGB hash that
> ignores your palette**. The channel you actually want for palette-aware
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

Returns the shading point's coordinates as RGB. Equivalent to Cycles'
"Texture Coordinate" node, RenderMan `Pref` / `Pworld` / `uvCoord` and
Arnold's `utility` node. Two principal uses:

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
- `generated` — bounds-normalised reference-space. The RenderMan
  `Pref` workflow: declare the canonical AABB and every node
  downstream sees a tidy `[0, 1]³` parameter regardless of
  transforms / displacement.
- `world` — `fract(rec.Point · scale)`. World-locked grid that does
  NOT follow the object — useful for laser-grids, world-aligned dust,
  "you-are-here" debug spheres.

See `scenes/showcases/texture-coordinate.yaml` for the
4-sphere side-by-side comparison (one per mode).

### Multi-Stop Color Ramp

Every procedural texture except `brick` accepts an optional `color_ramp:`
block that overrides the implicit two-colour lerp baked into the texture.
This matches Cycles' ColorRamp node, Arnold's `ramp_rgb` and RenderMan's
`PxrRamp` and unlocks looks that the two-colour `colors: [A, B]` shortcut
cannot express — Statuario marble with golden mid-tone, sapwood/heartwood
wood, photo-real sunset gradients, toon bands, voronoi heat maps.

```yaml
texture:
  type: "marble"
  vein_sharpness: 4.0
  color_ramp:
    - { position: 0.00, color: [0.05, 0.05, 0.07], interp: "smoothstep" }
    - { position: 0.45, color: [0.55, 0.45, 0.32], interp: "linear"     }
    - { position: 0.55, color: [0.95, 0.93, 0.88], interp: "linear"     }
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
luminance (Blinn 1978), aligning with Arnold's `bump2d`, RenderMan's
`PxrBump`, and Cycles' "Bump" node.

```yaml
- id: "marble_with_bump"
  type: "disney"
  color: [0.78, 0.78, 0.80]
  roughness: 0.4
  bump_map:
    texture:                   # ANY ITexture: noise, marble, wood,
      type: "marble"           # voronoi, brick, gradient, image, ...
      scale: 5.0
      vein_axis: [0, 1, 0]
      vein_frequency: 3.0
      vein_sharpness: 2.0
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
  high-frequency detail on top. This is the Arnold/Cycles convention.

The clearcoat lobe of `disney` materials keeps its independent
`coat_normal_map` and does **not** see the bump perturbation — the coat
sits on a stable substrate so scratches and orange-peel look correct.

See `scenes/showcases/texture-bump-map.yaml` for a side-by-side
comparison of bumps derived from `noise`, `marble`, and a concrete image
texture against a flat reference panel.

---

## 3.11.5 Surface Displacement (material-level, Cycles/RenderMan parity)

Bump maps perturb only the shading normal. **Surface displacement**
takes the next step: it physically moves the vertices of a subdivided
mesh before the BVH is built, so the **silhouette** changes — not just
the shading. The displacement lives on the material (Cycles' "Material
Output → Displacement" socket, RenderMan's `PxrDisplace` in the shader
network): one displaced material drives every mesh that uses it, with
no per-entity duplication.

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
is the canonical "height-field displacement" of Arnold
(`displacementShader`), RenderMan (`PxrDisplace`) and Cycles ("True
Displacement"). After the pass the shading normals are recomputed from
the displaced topology so the BSDF sees the new silhouette.

**Vector.** Each micro-vertex moves by `scale · (rgb − midlevel) · basis`,
where the basis is the per-vertex TBN frame (`space: tangent`, R→T,
G→B, B→N — Mudbox/Maya/ZBrush convention) or the identity
(`space: object`, RGB is added directly to the local position). Vector
mode is what produces **overhangs** and **crinkles** that a height
field cannot represent — exactly how sculpted hi-res detail is baked
onto a low-poly cage.

### `displacement_method` (Cycles tri-state)

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
This is Arnold's `autobump_visibility` flag. The bump amplitude is
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

### Mix-displacement (Cycles "Mix Shader → Displacement")

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

This is the same architectural choice Arnold and Cycles make:
displacement needs a polygon mesh, a UV/tangent frame, and a
subdivision pass to expose enough vertices for the deformation. An
analytical sphere has none of those. If you put a material with
`displacement: { scale: 0.02 }` on a `type: "sphere"`, you'll get the
shading (colour + roughness + bump_map) but not the lumps in the
silhouette — same as in Arnold or Cycles.

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
material in the library.

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
  shape as an OBJ once (Blender → Export OBJ with quads if you plan to
  use Catmull-Clark; triangles if you'll use Loop) and load it as a
  mesh with subdivision.
- **CSG operations**: displacement on CSG output is not currently
  supported (CSG works on analytical primitives; the intersection /
  difference / union is computed at intersection time, after which
  there's no mesh to displace). Use a "boolean-baked" OBJ instead.

**All `scenes/showcases/library-*.yaml` files demonstrating a
material library use this pattern.** Look at `library-concretes.yaml`,
`library-leathers.yaml`, `library-marbles-v2.yaml` — the row
of five demo spheres is built with subdivided icosahedra, so every
displaced material in the library shows its true geometric profile
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
