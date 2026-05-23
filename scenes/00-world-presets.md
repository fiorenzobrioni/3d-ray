# World (sky + ground) — Reference & Copy-Paste Presets

This file is a copy-paste catalogue for the YAML `world:` block — sky and
ground paired together, plus an optional `medium:`. Pick a preset, paste it
into a new scene, add your `cameras:` / `materials:` / `entities:`, render.

For sky models in isolation see also [`00-sky-presets.md`](00-sky-presets.md);
for the ground dispatcher details see
[`docs/reference/scene-reference.md`](../docs/reference/scene-reference.md).
Each preset here uses the production-grade defaults from the sky/environment
overhaul and the new ground dispatcher (Arnold / Cycles / Mitsuba parity).

---

## Why pair sky + ground?

The two blocks talk to each other through the renderer at several levels:

1. **Albedo coupling.** When `world.ground` has no `material:` and no inline
   `color:`, the loader auto-syncs the ground albedo with `sky.ground_albedo`
   (Preetham / Nishita / Hosek-Wilkie) or `sky.ground_color` (gradient) — the
   same lookdev convenience Arnold's `aiSkyDomeLight` offers in preview
   renders. Vice versa, a deliberate `sky.ground_albedo` controls how much
   warm bounce the analytical sky models return on the upward-facing
   geometry.
2. **Atmospheric scale.** `medium.height_fog` reads `y0` from the world,
   typically at ground level; pairing a large `bounds` heightfield with a
   thick fog at the wrong `y0` produces unnatural haze gradients. The presets
   here ship pre-balanced.
3. **Photometric consistency.** Studio presets (`flat` sky + small disk
   ground) want explicit `lights:` and zero environmental fill. Outdoor
   presets (Preetham / Nishita) rely on the sky as the primary light. Mixing
   them haphazardly causes either washouts or under-exposure.

> **Decoupling rule (recap from sky-presets).** On glossy / diffuse /
> transmission bounces the sky body is shown WITHOUT the analytical sun —
> the `PhysicalSun` covers it via NEE. Camera and delta-mirror rays still
> include the sun in the sky body. Pairings below respect this convention.

---

## Complete YAML Schema Reference

Every key is **optional** unless flagged `[required]`. The full schema for
the sky lives in [`00-sky-presets.md`](00-sky-presets.md); here we focus on
the ground side and the joint contract.

```yaml
world:

  # ── SKY (recap — full schema in 00-sky-presets.md) ─────────────────────
  sky:
    type: "flat"          # flat | gradient | preetham | hosek_wilkie
                          # | nishita | hdri    [required]
    intensity: 1.0
    # type-specific fields: color | zenith_color/horizon_color/ground_color
    # | turbidity/ground_albedo | path | …
    sun:                  # optional; auto-registers a PhysicalSun
      direction:      [0.3, 0.8, 0.2]   # TOWARDS the sun
      angular_radius: 0.265
      limb_darkening: true
      shadow_samples: 4

  # ── GROUND (full schema in docs/reference/scene-reference.md) ──────────
  ground:
    # Shape dispatch
    type: "infinite_plane"     # plane (alias) | quad | disk
                               # | heightfield (alias: terrain)
    # Position & orientation (universal)
    y:    0.0                  # legacy shorthand → point: [0, y, 0]
    point:  [0, 0, 0]          # full anchor (wins over `y`)
    normal: [0, 1, 0]          # surface normal (defaults +Y)
    orientation:
      euler: [0, 30, 0]        # or quaternion: [x, y, z, w]
    # Finite geometry (quad / disk)
    size: 50                   # half-extent (quad) or radius (disk)
    # Heightfield geometry
    bounds:        [-10, -10, 10, 10]   # [xMin, zMin, xMax, zMax]
    height_scale:  3.0
    heightmap_path: "ground/terrain.png"
    height_texture:                       # OR procedural
      type: "noise"
      scale: 0.1
    resolution: 512
    sea_level:    1.0
    sea_material: "water"
    strata:                              # altitude/slope-banded materials
      - { min_altitude: 0,    max_altitude: 0.4, material: "grass" }
      - { min_slope_deg: 35,                       material: "rock" }
    # Material (priority: material → inline shortcut → sky albedo → grey)
    material: "floor_id"
    color: [0.6, 0.5, 0.4]
    roughness: 0.7
    metallic: 0.0
    # UV transform (applied on top of the primitive's native UVs)
    uv_scale:    [10, 10]      # per-axis tile factor
    uv_offset:   [0, 0]        # pan
    uv_rotation: 30            # degrees CCW (viewed from above)
    # Visibility flags (Arnold polymesh.visibility / Cycles Ray Visibility)
    visibility:
      camera:       true
      diffuse:      true
      glossy:       true
      transmission: true
      shadow:       true

  # ── MEDIUM (optional) ─────────────────────────────────────────────────
  medium:
    type: "height_fog"         # homogeneous | height_fog | procedural
                               # | grid | atmosphere | nishita
    sigma_a: [0.002, 0.002, 0.0025]
    sigma_s: [0.020, 0.025, 0.030]
    y0: 0
    scale_height: 6
    phase: "hg"
    g: 0.6
```

---

# Section A — Natural environments (sky + ground pairings)

Outdoor presets where the sky drives most of the lighting and the ground
shape/material is tuned to the scale of the world. Each preset is a
self-contained `world:` block — drop it in, add your subject, render.

## A1. Desert noon — Preetham

```yaml
world:
  sky:
    type: "preetham"
    turbidity: 5.0                    # hazy continental atmosphere
    ground_albedo: [0.50, 0.42, 0.28] # ⇄ ground sand color
    sun:
      direction:      [0.2, 0.95, 0.1]  # near-zenith
      angular_radius: 0.265
      limb_darkening: true
      shadow_samples: 4
  ground:
    type: "quad"
    size: 80
    color:    [0.72, 0.60, 0.42]      # warm dune sand
    roughness: 0.95
    uv_scale:  [30, 30]
```

Sun overhead, hard shadows, washed-out highlights. Perfect for archviz
exterior of light-coloured stones, dunes, vehicle product shots. The big
`uv_scale` lets a small noise texture tile across the visible plane
without losing micro-grain. Aerial perspective is OFF — add `medium:
atmosphere` for misty distance.

---

## A2. Desert sunset — Nishita + aerial perspective

```yaml
world:
  sky:
    type: "nishita"
    turbidity: 4.0
    intensity: 1.0
    sun:
      direction:      [-0.85, 0.12, 0.4]   # just above horizon
      angular_radius: 0.5                  # cinematic disc
      limb_darkening: true
      shadow_samples: 4
  ground:
    type: "quad"
    size: 100
    color:    [0.78, 0.45, 0.20]            # red-orange sand
    roughness: 0.92
    uv_scale:  [40, 40]
  medium:
    type: "atmosphere"
    world_scale: 500.0
    dust_density: 1.5
    air_density: [1, 1, 1]
```

Single-scattering Rayleigh+Mie produces the cinematic red-orange gradient
from first principles, the atmosphere medium gives blue-shift to distant
geometry. Pair with a low hero (rocks, oasis, jeep) to anchor scale.

---

## A3. Alpine sunrise — heightfield with strata

```yaml
world:
  sky:
    type: "nishita"
    turbidity: 2.5
    intensity: 1.0
    sun:
      direction:      [-0.7, 0.18, 0.45]
      angular_radius: 0.5
      limb_darkening: true
      shadow_samples: 4
  ground:
    type: "heightfield"
    bounds:        [-50, -50, 50, 50]
    height_scale:  25
    heightmap_path: "libraries/terrains/heightfield-strata-test-height.png"
    sea_level:     7.5
    sea_material:  "water_alpine"
    material:      "rock_dark"
    strata:
      - { min_altitude: 0.00, max_altitude: 0.36, max_slope_deg: 35, blend_width: 0.04, material: "sand" }
      - { min_altitude: 0.34, max_altitude: 0.75, max_slope_deg: 45, blend_width: 0.06, material: "grass" }
      - { min_altitude: 0.50, max_altitude: 1.00,                     blend_width: 0.08, material: "rock_dark" }
      - { min_altitude: 0.85, max_altitude: 1.00, max_slope_deg: 60, blend_width: 0.05, material: "snow" }
  medium:
    type: "height_fog"
    sigma_a: [0.002, 0.002, 0.0025]
    sigma_s: [0.020, 0.025, 0.030]
    y0: 0
    scale_height: 6
    phase: "hg"
    g: 0.6
```

Mountain vista with stratified materials following altitude and slope —
sand at the lakeshore, grass mid-slope, rock on the cliffs, snow on the
peaks. The `sea_level` carves a glacial lake out of the heightfield in
one parameter. Reuses the baked heightmap shipped with the engine; swap
`heightmap_path` for your own PNG-16. Showcase
`scenes/showcases/ground-alpine-lake.yaml` is the full reference.

---

## A4. Sea at sunset — infinite plane water

```yaml
world:
  sky:
    type: "preetham"
    turbidity: 4.0
    ground_albedo: [0.05, 0.10, 0.15]
    sun:
      direction:      [-0.65, 0.18, 0.55]
      angular_radius: 0.4
      limb_darkening: true
      shadow_samples: 4
  ground:
    type: "infinite_plane"
    point: [0, 0, 0]
    color:    [0.04, 0.10, 0.18]
    roughness: 0.05
    metallic:  0.0
  medium:
    type: "height_fog"
    sigma_a: [0.003, 0.004, 0.005]
    sigma_s: [0.015, 0.020, 0.022]
    y0: 0
    scale_height: 3
    phase: "hg"
    g: 0.5
```

Quasi-mirror infinite ocean. The Preetham warm horizon + the low sun
generate the long specular streak on the water automatically. Pair with
a sailing boat or coastal mesh; the height fog hides the perfect mirror
seam at the horizon.

---

## A5. Overcast meadow

```yaml
world:
  sky:
    type: "flat"
    color: [0.55, 0.58, 0.60]         # warm grey overcast
  ground:
    type: "quad"
    size: 60
    color:    [0.32, 0.36, 0.20]      # mowed grass tone
    roughness: 0.92
    uv_scale:  [25, 25]
```

No sun: pure environmental fill. Soft diffuse shadows, low contrast,
"cloudy day" look. Great for product photography where you want
no harsh highlights, and a fast Preview profile because there's no sun
disc to importance-sample.

---

## A6. Starry night with moon — Gradient

```yaml
world:
  sky:
    type: "gradient"
    zenith_color:  [0.005, 0.005, 0.020]
    horizon_color: [0.020, 0.020, 0.045]
    ground_color:  [0.005, 0.005, 0.010]
    intensity: 1.0
    sun:                                # the "sun" here is the moon
      direction:      [-0.4, 0.6, -0.7]
      color:          [0.70, 0.75, 0.90] # cool blue moonlight
      intensity:      3.0
      angular_radius: 0.5
      limb_darkening: false
      shadow_samples: 4
  ground:
    type: "infinite_plane"
    color:    [0.06, 0.07, 0.10]
    roughness: 0.65
    metallic:  0.0
```

Astrophotography mood. Cool blue moonlight from a single analytical
"sun" preset to the moon direction (no limb darkening — the moon's
profile is flat). Combine with rim lights and emissive stars overlay
in compositing.

---

## A7. Tropical beach morning — Hosek-Wilkie

```yaml
world:
  sky:
    type: "hosek_wilkie"
    turbidity: 2.0                       # pristine air
    ground_albedo: [0.85, 0.78, 0.62]    # bright sand bounces back
    intensity: 1.0
    sun:
      direction:      [0.4, 0.8, 0.3]    # high-ish but warm
      angular_radius: 0.265
      limb_darkening: true
      shadow_samples: 6
  ground:
    type: "quad"
    size: 50
    color:    [0.92, 0.85, 0.68]         # bright coral sand
    roughness: 0.96
    uv_scale:  [40, 40]
```

The high `ground_albedo` is the trick: the analytical sky reflects the
sand's brightness back as ambient warm fill, which is exactly how a real
tropical scene over-bounces. The texture-only ground at uv_scale 40 keeps
grain fine on a 100-m patch. Add the sea as a separate `infinite_plane`
or `quad` entity for shoreline.

---

## A8. Autumn forest golden hour

```yaml
world:
  sky:
    type: "preetham"
    turbidity: 4.0
    ground_albedo: [0.40, 0.22, 0.10]
    sun:
      direction:      [-0.55, 0.30, 0.55] # 17° elevation
      angular_radius: 0.4
      limb_darkening: true
      shadow_samples: 6
  ground:
    type: "heightfield"
    bounds:        [-30, -30, 30, 30]
    height_scale:  4
    height_texture:
      type: "noise"
      scale: 0.18
    resolution: 256
    color:    [0.38, 0.20, 0.08]          # rich brown soil
    roughness: 0.95
```

Mildly undulating forest floor (procedural fBm noise heightfield) under
warm low sun — perfect base for tree instances on top. The procedural
height_texture means no PNG asset is required: instant render.

---

## A9. Polar snow plain

```yaml
world:
  sky:
    type: "gradient"
    zenith_color:  [0.45, 0.55, 0.72]
    horizon_color: [0.75, 0.82, 0.88]
    ground_color:  [0.80, 0.85, 0.92]
    intensity: 1.0
  ground:
    type: "quad"
    size: 100
    color:    [0.92, 0.95, 1.00]
    roughness: 0.78
    metallic:  0.0
    # For subsurface snow add a real material:
    # material: "dis_neve_fresca_v2"  (from libraries/materials/grounds.yaml)
```

Diffuse polar palette. Pair with subsurface snow material from the
library for the SSS softness that the inline shorthand can't express.
Sun-less by design — Arctic overcast skies are nearly isotropic.

---

# Section B — Photography studios (subject-ready)

Indoor presets pre-tuned for placing hero objects on the ground and
adding explicit `lights:` for the key/fill/rim setup. They favour
predictability over realism: the sky is the backdrop, the ground is the
podium.

## B1. White infinite cyclorama (high-key)

```yaml
world:
  sky:
    type: "flat"
    color: [1.0, 1.0, 1.0]
    intensity: 1.0
  ground:
    type: "disk"
    size: 6
    color:    [0.95, 0.95, 0.95]
    roughness: 0.40
    metallic:  0.0
```

The classic packshot setup. Bright environmental fill from the white sky
floods every diffuse surface; the slightly matte disk avoids the floor
being indistinguishable from the sky. Add an `area` key light over the
subject for shape, optionally `visibility.shadow: false` on the disk for
the floating-product look.

---

## B2. Black studio contrast

```yaml
world:
  sky:
    type: "flat"
    color: [0.0, 0.0, 0.0]
  ground:
    type: "disk"
    size: 4
    color:    [0.03, 0.03, 0.035]
    roughness: 0.05
    metallic:  0.0
```

Inverse of B1: pure void background, near-mirror dark podium that
reflects the subject. With NEE skipping the black sky automatically,
every photon comes from your explicit lights — total control. The model
of `scenes/showcases/ground-jewel-studio.yaml`.

---

## B3. Backlit golden hour (key from off-camera)

```yaml
world:
  sky:
    type: "preetham"
    turbidity: 3.5
    ground_albedo: [0.55, 0.50, 0.42]
    sun:
      direction:      [-0.85, 0.12, 0.7]   # low key off-camera
      angular_radius: 0.5
      limb_darkening: true
      shadow_samples: 6
  ground:
    type: "quad"
    size: 20
    material: "dis_cemento_lavato_chiaro"   # from libraries/materials/concretes.yaml
    uv_scale: [8, 8]
```

Warm low sun acts as a rim/back-light, the concrete floor catches both
the sun's specular streak and the ambient blue from the sky. Use
together with a fill area light (5500 K cool) on the camera side for
the classic golden-hour portrait look.

---

## B4. Neon cyberpunk product

```yaml
world:
  sky:
    type: "flat"
    color: [0.02, 0.01, 0.04]              # deep purple-black
  ground:
    type: "quad"
    size: 12
    color:    [0.06, 0.02, 0.10]
    roughness: 0.18
    metallic:  0.6                          # near-metal violet sheen
    uv_scale:  [5, 5]
```

The metallic ground bounces ALL the magenta/cyan from your explicit
neon lights back into the scene. Pair with `emissive` strip-light
entities and one `area` key in the opposite tint. For an emissive
ground itself (Tron-style), add a separate `quad` entity with an
`emissive` material — the `world.ground` block can't carry an
emissive material directly.

---

## B5. Premium HDRI marble studio

```yaml
world:
  sky:
    type: "hdri"
    path: "showcases/hdri/newman_cafeteria_4k.hdr"
    intensity: 1.0
    rotation: 90
    sun:
      extract_from_hdri: true
      shadow_samples: 4
  ground:
    type: "quad"
    size: 12
    material: "dis_carrara_studio_lucido"   # from libraries/materials/stones.yaml
    uv_scale: [3, 3]
```

Photoreal HDRI environment with sun extraction for clean hard shadows,
on a Carrara marble podium that catches the IBL's nuanced colour
spectrum. The `uv_scale [3, 3]` lets the marble vein pattern read at
medium framing. The HDRI ships with the engine — see
`scenes/showcases/hdri/`.

---

## B6. Bistro wood table (shadow-less floor)

```yaml
world:
  sky:
    type: "gradient"
    zenith_color:  [0.45, 0.40, 0.30]
    horizon_color: [0.70, 0.55, 0.30]
    ground_color:  [0.35, 0.22, 0.08]
    intensity: 1.0
  ground:
    type: "quad"
    size: 8
    material: "dis_mogano_laccato"          # from libraries/materials/woods.yaml
    uv_scale: [4, 4]
    visibility:
      shadow: false                         # floor doesn't cast shadow
```

Warm gradient ambient + lacquered mahogany table. The
`visibility.shadow: false` is a creative trick: bottles, glasses and
plates on the table cast their own contact shadows, but the table
itself doesn't cast a hard outline on the floor / wall behind — useful
when you want a clean "floating composition" look from above.

---

# Decision matrix

Quick lookup: pick the preset closest to your scene, copy, tweak.

| Use case                             | Preset | Sky                | Ground                        |
|--------------------------------------|--------|--------------------|--------------------------------|
| Desert / arid landscape, high noon   | A1     | preetham           | quad sand                      |
| Cinematic outdoor sunset             | A2     | nishita + atm.     | quad warm sand                 |
| Mountain vista with lake             | A3     | nishita            | heightfield strata + sea       |
| Sea / lake panorama                  | A4     | preetham           | infinite_plane water           |
| Cloudy day, no harsh shadows         | A5     | flat overcast      | quad grass                     |
| Night astrophotography mood          | A6     | gradient + moon    | infinite_plane                 |
| Bright tropical beach                | A7     | hosek_wilkie       | quad bright sand               |
| Autumn forest floor                  | A8     | preetham warm      | heightfield procedural         |
| Polar snow plain                     | A9     | gradient cool      | quad snow                      |
| White packshot / archviz cleanup     | B1     | flat white         | disk light grey                |
| Black contrast / jewellery studio    | B2     | flat black         | disk near-mirror               |
| Golden hour portrait                 | B3     | preetham low sun   | quad concrete                  |
| Cyberpunk neon product               | B4     | flat purple-black  | quad metallic                  |
| Photoreal IBL marble                 | B5     | hdri + sun extract | quad Carrara marble            |
| Floating-table look (no contact)     | B6     | gradient warm      | quad wood + `shadow: false`    |

---

# CLI tips for world presets

```bash
# Outdoor (sun + sky body) — push shadow samples for penumbra quality
RayTracer -i scene.yaml -w 1920 -H 1080 -s 512 -d 8 -S 8

# Heightfield strata — depth ≥ 8, shadow ≥ 4 (band edges need rays)
RayTracer -i scene.yaml -d 10 -S 4

# Studio (flat black/white + small ground) — low depth is enough
RayTracer -i scene.yaml -d 5 -S 4

# Reflective ground (disk near-mirror, marble lucido) — clamp fireflies
RayTracer -i scene.yaml -C 25

# HDRI premium (B5) — sobol sampler + sun extraction
RayTracer -i scene.yaml --sampler sobol -d 8 -S 6
```

The renderer auto-derives glossy-roughness LOD for HDRI mipmap lookups —
no extra flags needed.
