# Sky / Environment — Reference & Copy-Paste Presets

This file is a copy-paste catalogue for the YAML `world.sky:` block.
Pick a preset, paste it into a new scene, render. Each preset is
self-contained and uses the production-grade defaults from the
sky/environment overhaul (see [`docs/technical/path-tracing-and-lighting.md`](../../docs/technical/path-tracing-and-lighting.md)
and tutorial [chapter 7](../../docs/tutorial/en/07-sky-environment-camera.md) /
[capitolo 7](../../docs/tutorial/it/07-sky-environment-camera.md)).

---

## What is `SkyEnvironment`?

`SkyEnvironment` (exposed in YAML as `world.sky:`) is the global
environment emitter. It does three jobs at once:

1. **Sky body** — returns linear HDR radiance for every direction a ray
   escapes into. This is the "background" you see when looking at the
   sky and the indirect light that fills your scene by GI.
2. **Analytical sun** — when the chosen model exposes a sun
   (gradient + sun, preetham, nishita, or hdri+extracted sun), a paired
   `PhysicalSun` light is **auto-registered** by the loader, with cone
   sampling, optional Hestroffer limb darkening, and stratified
   soft-shadow samples. You do not declare it manually in `lights:`.
3. **NEE integration** — non-trivial skies (hdri / sun-bearing / non-black
   flat) participate in Next Event Estimation through an automatic
   `EnvironmentLight` registered by the loader.

> **Decoupling rule.** On glossy / diffuse / transmission bounces (non-
> delta), the sky body is shown **without** the analytical sun — the
> `PhysicalSun` covers it via NEE. On delta bounces (mirror, refraction)
> and camera rays the sky body **includes** the sun. This avoids
> double-counting and matches Arnold / Cycles HDRI-sun-extraction
> behaviour.

> **Sun direction convention (since the sky overhaul).** `sun.direction`
> points **TOWARDS the sun**. Old scenes that used "propagation
> direction" need their sun vectors inverted.

---

## Complete YAML Schema Reference

Every key is **optional** unless flagged `[required]`. Keys are grouped
by which sky `type:` consumes them.

```yaml
world:
  sky:
    # ── Common: all models ─────────────────────────────────────────────
    type: "flat"                    # [required] flat | gradient | preetham
                                    #            hosek_wilkie | nishita | hdri
    intensity: 1.0                  # global multiplier on sky body + sun

    # ── Flat-only ──────────────────────────────────────────────────────
    color: [0.5, 0.7, 1.0]          # uniform sphere colour (linear)

    # ── Gradient-only ──────────────────────────────────────────────────
    zenith_color:  [0.10, 0.30, 0.80]
    horizon_color: [0.70, 0.85, 1.00]
    ground_color:  [0.30, 0.25, 0.20]

    # ── Hosek-Wilkie / Preetham / Nishita ──────────────────────────────
    turbidity:     3.0              # 1 = pristine, 3 = clear, 5 = haze, 10 = smog
    ground_albedo: [0.30, 0.30, 0.30]  # tints sky's ground bounce

    # ── HDRI-only ──────────────────────────────────────────────────────
    path: "studio.hdr"              # .hdr (Radiance RGBE) or .exr (OpenEXR)
                                    # Resolved relative to the scene YAML.
    rotation: 0.0                   # legacy Y-axis rotation in degrees
                                    # (prefer `orientation:` below for full 3D)

    # ── Sun (optional sub-block; applies to all models) ────────────────
    sun:
      direction:         [0.3, 0.8, 0.2]   # direction TOWARDS the sun
      color:             [1.0, 1.0, 1.0]   # disc tint (overrides physical model's)
      intensity:         10.0
      angular_radius:    0.265        # half-angle in degrees (real Sun)
      size:              3.0          # full diameter — used only if angular_radius <= 0
      shadow_samples:    4            # stratified samples for PhysicalSun shadows
      visible_to_camera: true         # false = hide disc from camera rays
      # HDRI only:
      extract_from_hdri: false        # detect bright peak, split as PhysicalSun
      extract_threshold: 50           # luminance threshold (× HDRI mean)

    # ── Per-ray-category visibility (Cycles/Arnold parity) ─────────────
    visibility:
      camera:       true     # primary camera rays
      diffuse:      true     # diffuse / sheen / SSS bounces
      glossy:       true     # glossy / clearcoat bounces
      transmission: true     # refractions through dielectrics
      shadow:       true     # NEE shadow rays return sky radiance

    # ── Background plate (camera-visible only) ─────────────────────────
    background:                     # optional separate sky for camera rays
      type: "hdri"                  # any sky model — same schema as above
      path: "background.hdr"
      intensity: 1.0
      # ... all other model-specific fields apply here too

    # ── 3D orientation (replaces legacy `rotation:`) ───────────────────
    orientation:
      euler:      [10, 45, 0]              # XYZ intrinsic Euler degrees
      # OR
      quaternion: [0, 0.38, 0, 0.92]       # XYZW; quaternion wins if both given
```

### Companion: aerial perspective (Nishita medium)

Pair `type: nishita` with this `world.medium:` for physical depth-of-air:

```yaml
world:
  medium:
    type: "atmosphere"            # aliases: nishita | aerial_perspective
    world_scale: 1000.0           # metres per world unit (1000 = "1 wu : 1 km")
    sea_level_y: 0.0              # world Y of altitude 0
    air_density: [1, 1, 1]        # Rayleigh density per channel
    dust_density: 1.0             # Mie density (0=pristine, 1=clean, >1=polluted)
    phase: "hg"                   # default Henyey-Greenstein g=0.76 (Mie forward)
    g: 0.76
```

### Companion: portal light for interiors

Window opening, virtual rectangle, ~10× variance reduction:

```yaml
lights:
  - type: "portal"           # alias: portal_light
    anchor: [3.0, 1.2, -2.5] # one corner of the window rectangle
    u: [0.0, 0.0, 2.5]       # edge along U
    v: [0.0, 1.2, 0.0]       # edge along V
    shadow_samples: 8        # default 8
    # Orient u, v so cross(u, v) points TOWARDS the sky.
```

---

# Presets

## 1. Black void (Cornell-box / studio testing)

```yaml
world:
  sky:
    type: "flat"
    color: [0, 0, 0]
```

Skipped from NEE automatically. Use it for closed boxes where every
light source is explicit, or when you want zero environmental contribution.

---

## 2. Neutral studio fill

```yaml
world:
  sky:
    type: "flat"
    color: [0.18, 0.18, 0.18]   # ≈ 18% grey, perceptually neutral
```

Soft uniform fill for product photography. Participates in NEE via
uniform sphere sampling. Combine with an `area` or `sphere` light for
the key, and the sky gives free Lambertian ambient.

---

## 3. Clear midday — Hosek-Wilkie

```yaml
world:
  sky:
    type: "hosek_wilkie"
    turbidity: 3.0
    ground_albedo: [0.25, 0.25, 0.22]
    intensity: 1.0
    sun:
      direction:      [0.3, 1.0, 0.4]    # high in the sky
      angular_radius: 0.265
      shadow_samples: 4
```

The most physically grounded analytical clear-sky. Excellent for
archviz, outdoor product, daytime landscapes. Sun visible to camera by
default; combine with hero objects and a `ground:` plane.

---

## 4. Sunset / golden hour — Nishita

```yaml
world:
  sky:
    type: "nishita"
    turbidity: 4.0
    intensity: 1.0
    sun:
      direction:      [-0.85, 0.12, 0.4]   # sun just above horizon
      angular_radius: 0.5                   # slightly enlarged for cinematic glow
      shadow_samples: 4
```

Single-scattering Rayleigh+Mie produces the red-orange-blue gradient
from first principles. Pair with the aerial perspective medium below
for full atmospheric realism.

---

## 5. Sunset with aerial perspective — full atmospheric stack

```yaml
world:
  sky:
    type: "nishita"
    intensity: 1.0
    sun:
      direction:      [-0.7, 0.25, 0.6]
      angular_radius: 0.4
      shadow_samples: 4
  medium:
    type: "atmosphere"
    world_scale: 500.0          # 500 m/wu — "country road" scale
    dust_density: 1.5           # slightly hazy
    air_density: [1, 1, 1]
```

Distant geometry gains a bluish tint and loses contrast (Rayleigh
scattering); the sun's warm tint deepens through the atmosphere.
Production-grade depth-of-air look. Increase `dust_density` (1.5 → 3)
for misty / overcast feel.

---

## 6. Night with moon — Gradient

```yaml
world:
  sky:
    type: "gradient"
    zenith_color:  [0.005, 0.005, 0.020]
    horizon_color: [0.020, 0.020, 0.045]
    ground_color:  [0.005, 0.005, 0.010]
    intensity: 1.0
    sun:
      direction:      [-0.4, 0.6, -0.7]   # moon position
      color:          [0.70, 0.75, 0.90]  # cool blue moonlight
      intensity:      3.0
      angular_radius: 0.5                  # moon disc
      shadow_samples: 4
```

Stylised nocturne. The "sun" parameter doubles as a moon. Add `light`
of `type: point` warm-toned for distant streetlamps if needed.

---

## 7. Photoreal HDRI with sun extraction

```yaml
world:
  sky:
    type: "hdri"
    path: "showcases/hdri/your_outdoor_hdri.hdr"   # .hdr or .exr
    intensity: 1.0
    orientation:
      euler: [0, 90, 0]                            # full 3D rotation
    sun:
      extract_from_hdri:  true
      extract_threshold:  40                       # lower if the HDRI sun is soft
      shadow_samples:     4
```

The default for VFX / archviz with captured environments. The sun is
auto-separated for clean shadows; the HDRI body provides natural fill.
Drop in any Poly Haven HDRI (CC0) and tweak `orientation.euler[1]` to
position the sun relative to the camera.

---

## 8. Indoor with portal light — HDRI through a window

```yaml
world:
  sky:
    type: "hdri"
    path: "showcases/hdri/your_outdoor_hdri.hdr"
    intensity: 5.0                                 # bright outside, dim inside
    sun:
      extract_from_hdri: true
      shadow_samples: 4

lights:
  - type: "portal"
    anchor: [3.0, 1.2, -2.5]                       # window position (right wall)
    u: [0.0, 0.0, 2.5]                             # width along Z
    v: [0.0, 1.2, 0.0]                             # height along Y
    shadow_samples: 8
```

For interior renders. The portal restricts NEE to the window rectangle —
typical variance reduction ≈ 10× over plain HDRI NEE. Orient `u, v` so
`u × v` points OUT of the room.

---

## 9. Off-camera HDRI key (alpha-clean background)

```yaml
world:
  sky:
    type: "hdri"
    path: "showcases/hdri/your_lighting_hdri.hdr"
    intensity: 1.5
    visibility:
      camera:       false        # invisible to camera (clean alpha for compositing)
      diffuse:      true
      glossy:       true
      transmission: true
      shadow:       true
    sun:
      visible_to_camera: false   # hide the hot sun disc too
```

Production / VFX setup: HDRI lights the scene and shows in reflections,
but the rendered background is transparent / black for compositing. Add
a `background:` sub-block to show a different plate to the camera.

---

## 10. Stylised clear day — Gradient with crisp sun

```yaml
world:
  sky:
    type: "gradient"
    zenith_color:  [0.18, 0.35, 0.75]
    horizon_color: [0.78, 0.88, 1.00]
    ground_color:  [0.30, 0.25, 0.20]
    intensity: 1.0
    sun:
      direction:      [0.4, 0.9, 0.2]
      color:          [1.0, 0.97, 0.90]
      intensity:      15.0
      angular_radius: 0.8                # slightly enlarged for nicer specular
      shadow_samples: 4
```

Fast, predictable, art-directable. Use for animation previews and
illustration-style renders. Tune zenith / horizon / ground independently
to match a colour script.

---

# Sky model decision matrix

When in doubt:

| Scenario                              | Recommended sky                          |
|---------------------------------------|------------------------------------------|
| Black void / Cornell box              | `flat` `[0, 0, 0]`                       |
| Studio / product / interior key       | `flat` (low neutral) + explicit lights   |
| Stylised outdoor, animation preview   | `gradient` with sun                      |
| Outdoor midday photoreal              | `hosek_wilkie` (= preetham)              |
| Sunrise / sunset / mountain vistas    | `nishita` + atmosphere medium            |
| Captured environment (Poly Haven)     | `hdri` + sun extraction                  |
| Interior lit by a window              | any sky + `portal` light                 |
| Off-camera HDRI key, clean alpha      | `hdri` + `visibility.camera: false`      |

# CLI tips for sky-heavy scenes

```
# Preview with low samples — sky body, sun, IBL all work at any spp
RayTracer -i scene.yaml -w 800 -H 450 -s 32 -d 4

# Production — push shadow samples (-S) up for sun penumbra quality
RayTracer -i scene.yaml -w 1920 -H 1080 -s 512 -d 8 -S 8

# Suppress HDRI fireflies on rough metals via firefly clamp
RayTracer -i scene.yaml -C 25 ...
```

The renderer auto-derives glossy-roughness LOD for HDRI mipmap lookups —
no extra flags needed.
