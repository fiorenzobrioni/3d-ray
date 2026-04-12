# Chapter 9: Participating Media (Volumetrics)

Real air is not perfectly transparent. Fog scatters light, water absorbs
red wavelengths, smoke glows when caught in a beam. 3D-Ray supports a
**global homogeneous participating medium** that simulates these effects.

---

## 9.1 What Are Participating Media?

In a vacuum, light travels in straight lines forever. In a participating
medium (air, water, smoke), two things happen:

- **Absorption** -- the medium swallows photons. Light grows dimmer as
  it travels farther. Colored absorption creates tinted atmospheres
  (blue underwater, orange sunset haze).

- **Scattering** -- photons change direction when they hit particles in
  the medium. This is why fog glows when headlights shine through it,
  and why the sky is blue.

The combination of absorption and scattering determines how light behaves
as it traverses the volume.

---

## 9.2 Configuring the Global Medium

The medium is defined under `world: > medium:`:

```yaml
world:
  medium:
    type: "homogeneous"
    sigma_a: [0.01, 0.01, 0.01]
    sigma_s: [0.06, 0.06, 0.06]
    phase: "hg"
    g: 0.85
```

| Parameter | Type      | Default       | Description                        |
|-----------|-----------|---------------|------------------------------------|
| `type`    | `string`  | --            | Currently only `"homogeneous"`     |
| `sigma_a` | `[R,G,B]` | --           | Absorption coefficient per channel |
| `sigma_s` | `[R,G,B]` | --           | Scattering coefficient per channel |
| `phase`   | `string`  | `"isotropic"` | Phase function type               |
| `g`       | `float`   | `0.0`         | Asymmetry parameter (for `"hg"`)  |

### sigma_a (Absorption)

Controls how quickly light is absorbed. Units are inverse world-units
(1/unit). Higher values mean denser, more opaque medium.

- `[0.01, 0.01, 0.01]` -- very faint absorption (slight haze).
- `[0.1, 0.05, 0.01]` -- colored absorption: red is absorbed fastest,
  blue least. This creates a blue tint (like underwater).

### sigma_s (Scattering)

Controls how much light is deflected by particles. Higher values mean
denser fog with more visible light shafts.

- `[0.02, 0.02, 0.02]` -- thin mist.
- `[0.1, 0.1, 0.1]` -- noticeable fog.
- `[0.5, 0.5, 0.5]` -- thick, pea-soup fog.

The total extinction coefficient is `sigma_t = sigma_a + sigma_s`. This
determines the overall opacity of the medium (how quickly visibility
drops with distance).

---

## 9.3 Phase Functions: How Light Scatters

The phase function determines the angular distribution of scattered
light.

### Isotropic (Default)

```yaml
phase: "isotropic"
```

Light scatters equally in all directions. This is the simplest model and
works well for generic haze or smoke.

### Henyey-Greenstein

```yaml
phase: "hg"
g: 0.85
```

The Henyey-Greenstein (HG) phase function allows directional bias:

| `g` value | Behavior                                   |
|-----------|--------------------------------------------|
| `0.0`     | Identical to isotropic                     |
| `0.3`     | Mild forward scattering (thin haze)        |
| `0.7`     | Strong forward scattering (fog, clouds)    |
| `0.85`    | Very forward-peaked (dense fog, mist)      |
| `-0.3`    | Backward scattering (unusual, artistic)    |

Forward scattering (`g > 0`) means light tends to continue in roughly
the same direction after hitting a particle. This is physically accurate
for most real-world media (fog, dust, aerosols) and creates bright halos
around light sources when seen through the medium.

Aliases: `"hg"`, `"henyey_greenstein"`.

---

## 9.4 Practical Recipes

### Light Fog

A subtle haze that softens distant objects and adds atmosphere without
obscuring the scene.

```yaml
world:
  medium:
    type: "homogeneous"
    sigma_a: [0.005, 0.005, 0.005]
    sigma_s: [0.04, 0.04, 0.04]
    phase: "hg"
    g: 0.8
```

### Dense Mist

Visibility drops to a few units. Light sources create bright, dramatic
halos.

```yaml
world:
  medium:
    type: "homogeneous"
    sigma_a: [0.01, 0.01, 0.01]
    sigma_s: [0.15, 0.15, 0.15]
    phase: "hg"
    g: 0.85
```

### Underwater

Water absorbs red light faster than blue. The deeper you look, the
bluer the scene becomes. Moderate scattering creates visible light shafts
from the surface.

```yaml
world:
  medium:
    type: "homogeneous"
    sigma_a: [0.12, 0.06, 0.02]
    sigma_s: [0.02, 0.02, 0.02]
    phase: "hg"
    g: 0.6
```

### Tinted Haze (Golden Hour Atmosphere)

Warm atmospheric haze that scatters orange-gold light, creating a dreamy
golden hour effect.

```yaml
world:
  medium:
    type: "homogeneous"
    sigma_a: [0.002, 0.005, 0.015]
    sigma_s: [0.03, 0.025, 0.015]
    phase: "hg"
    g: 0.75
```

### Thick Smoke

Very dense, nearly opaque medium with strong isotropic scattering.

```yaml
world:
  medium:
    type: "homogeneous"
    sigma_a: [0.05, 0.05, 0.05]
    sigma_s: [0.4, 0.38, 0.35]
    phase: "isotropic"
```

---

## 9.5 Rendering Considerations

Volumetric rendering is more demanding than surface-only rendering.
Keep these tips in mind:

1. **Increase samples.** The medium adds another source of noise (random
   scattering events along each ray). Use at least 64 SPP; 256+ for
   clean results.

2. **Increase depth.** Each scattering event counts as a bounce. With a
   dense medium, rays may scatter several times before reaching a light.
   Use `-d 30` or higher.

3. **Spot lights create god rays.** A spot light shining through fog
   produces a visible cone of light. This is one of the most dramatic
   effects possible with participating media.

4. **Point lights glow.** In fog, every point light gets a soft radial
   halo whose size depends on the medium density.

5. **The medium is global.** It affects every ray in the scene, including
   shadow rays (lights appear dimmer through fog). There is no way to
   confine the medium to a specific volume -- it fills the entire world.

6. **Start thin, then thicken.** It is easier to add fog than to remove
   it. Begin with very small `sigma_s` values (0.01--0.03) and increase
   until you get the desired effect.

---

## 9.6 Complete Example: Cathedral in Fog

```yaml
# cathedral-fog.yaml
# Stone pillars in fog with a spot light creating a visible beam.

world:
  ambient_light: [0.005, 0.005, 0.008]
  background: [0.01, 0.01, 0.02]
  medium:
    type: "homogeneous"
    sigma_a: [0.008, 0.008, 0.008]
    sigma_s: [0.07, 0.07, 0.07]
    phase: "hg"
    g: 0.82

camera:
  position: [0, 1.5, -6]
  look_at: [0, 2, 2]
  fov: 55

materials:
  - id: "floor"
    type: "disney"
    roughness: 0.7
    texture:
      type: "checker"
      scale: 0.5
      colors: [[0.25, 0.22, 0.2], [0.15, 0.13, 0.12]]

  - id: "stone_pillar"
    type: "disney"
    roughness: 0.6
    specular: 0.3
    texture:
      type: "marble"
      scale: 5.0
      noise_strength: 3.0
      colors: [[0.65, 0.6, 0.55], [0.4, 0.37, 0.33]]
      randomize_offset: true

  - id: "ceiling"
    type: "lambertian"
    color: [0.2, 0.18, 0.16]

entities:
  # Floor
  - type: "infinite_plane"
    point: [0, 0, 0]
    normal: [0, 1, 0]
    material: "floor"

  # Ceiling
  - type: "infinite_plane"
    point: [0, 5, 0]
    normal: [0, -1, 0]
    material: "ceiling"

  # Left row of pillars
  - type: "cylinder"
    center: [-2, 0, -2]
    radius: 0.3
    height: 5.0
    material: "stone_pillar"
    seed: 1

  - type: "cylinder"
    center: [-2, 0, 2]
    radius: 0.3
    height: 5.0
    material: "stone_pillar"
    seed: 2

  - type: "cylinder"
    center: [-2, 0, 6]
    radius: 0.3
    height: 5.0
    material: "stone_pillar"
    seed: 3

  # Right row of pillars
  - type: "cylinder"
    center: [2, 0, -2]
    radius: 0.3
    height: 5.0
    material: "stone_pillar"
    seed: 4

  - type: "cylinder"
    center: [2, 0, 2]
    radius: 0.3
    height: 5.0
    material: "stone_pillar"
    seed: 5

  - type: "cylinder"
    center: [2, 0, 6]
    radius: 0.3
    height: 5.0
    material: "stone_pillar"
    seed: 6

lights:
  # The main effect: a spot light creating a visible beam through the fog
  - type: "spot"
    position: [0, 4.8, 4]
    direction: [0, -0.7, -0.3]
    color: [1.0, 0.92, 0.75]
    intensity: 120.0
    inner_angle: 10
    outer_angle: 22

  # Faint fill so the pillars are not completely black
  - type: "point"
    position: [0, 4, -4]
    color: [0.5, 0.55, 0.7]
    intensity: 8.0
```

Render with:

```
3d-ray -i cathedral-fog.yaml -w 1200 -H 800 -s 256 -d 40
```

The spot light creates a dramatic visible beam cutting through the fog
between the pillars. The forward-peaked HG phase function (g=0.82)
concentrates the glow around the beam direction, just like real fog.

---

## What You Have Learned

- **sigma_a** controls absorption (light dimming over distance).
- **sigma_s** controls scattering (fog density, light shafts).
- The **isotropic** phase function scatters equally; **Henyey-
  Greenstein** allows directional bias (forward scattering for fog).
- The medium is global and affects all rays including shadows.
- Volumetric scenes need more samples and depth than surface-only
  scenes.
- Spot lights in fog create god rays; point lights create halos.

---

[Previous: Constructive Solid Geometry (CSG)](./08-csg.md) | [Next: Asset Libraries and Complete Scenes](./10-libraries-and-projects.md) | [Tutorial Index](./README.md)
