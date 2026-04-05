# 9. Esempi Completi

## Scena Minima

```yaml
world:
  ambient_light: [0.1, 0.1, 0.1]
  background: [0.5, 0.7, 1.0]

camera:
  position: [0, 1, -4]
  look_at: [0, 0, 0]
  fov: 60

materials:
  - id: "rosso"
    type: "lambertian"
    color: [0.8, 0.2, 0.1]

entities:
  - name: "sfera"
    type: "sphere"
    center: [0, 0, 0]
    radius: 1
    material: "rosso"

lights:
  - type: "point"
    position: [3, 5, -3]
    color: [1, 1, 1]
    intensity: 20
```

## Showcase Materiali (Confronto)

Tre sfere affiancate che mostrano i tre comportamenti fisici principali: Diffuso, Metallico e Vetro.

```yaml
world:
  ambient_light: [0.08, 0.08, 0.1]
  background: [0.3, 0.5, 0.9]
  ground: { type: "infinite_plane", material: "pavimento", y: 0 }

camera:
  position: [0, 1.5, -6]
  look_at: [0, 0.5, 0]
  fov: 50

materials:
  - id: "pavimento"
    type: "lambertian"
    color: [0.5, 0.5, 0.5]
  - id: "diffuso"
    type: "lambertian"
    color: [0.8, 0.3, 0.2]
  - id: "metallico"
    type: "metal"
    color: [0.9, 0.9, 0.9]
    fuzz: 0.05
  - id: "vetro"
    type: "dielectric"
    refraction_index: 1.5

entities:
  - { name: "mat_sx", type: "sphere", center: [-2.5, 1, 0], radius: 1, material: "diffuso" }
  - { name: "mat_centro", type: "sphere", center: [0, 1, 0], radius: 1, material: "metallico" }
  - { name: "mat_dx", type: "sphere", center: [2.5, 1, 0], radius: 1, material: "vetro" }

lights:
  - type: "point"
    position: [0, 8, -5]
    color: [1, 1, 1]
    intensity: 60
```

## Scena con Normal Map

```yaml
world:
  ambient_light: [0.03, 0.03, 0.04]
  background: [0.05, 0.05, 0.08]

camera:
  position: [0, 1.5, -5]
  look_at: [0, 1.5, 0]
  fov: 46

materials:
  - id: "muro_mattoni"
    type: "lambertian"
    texture:
      type: "image"
      path: "textures/brick-wall.png"
      uv_scale: [2, 1.5]
    normal_map:
      path: "textures/brick-wall-normal.png"
      strength: 1.2
      uv_scale: [2, 1.5]

  - id: "acciaio"
    type: "metal"
    color: [0.88, 0.88, 0.90]
    fuzz: 0.02
    normal_map:
      path: "textures/metal-scratched-normal.png"
      strength: 1.5

entities:
  - name: "parete"
    type: "quad"
    q: [-3, 0, 3]
    u: [6, 0, 0]
    v: [0, 3, 0]
    material: "muro_mattoni"

  - name: "sfera_metallo"
    type: "sphere"
    center: [0, 1.2, 0]
    radius: 1.2
    material: "acciaio"

lights:
  - type: "point"
    position: [-6, 3, 0]
    color: [1.0, 0.95, 0.85]
    intensity: 80
```

## Scena Architettonica con Area Light e Geometrie Miste

```yaml
world:
  ambient_light: [0.02, 0.02, 0.03]
  background: [0.0, 0.0, 0.0]
  ground: { type: "infinite_plane", material: "pavimento_chiaro", y: 0 }

camera:
  position: [5, 3, -10]
  look_at: [0, 1.5, 0]
  fov: 40

materials:
  - id: "pavimento_chiaro"
    type: "lambertian"
    texture: { type: "checker", scale: 5.0, colors: [[0.7, 0.7, 0.7], [0.8, 0.8, 0.8]] }
  - id: "marmo_colonna"
    type: "lambertian"
    texture: { type: "marble", scale: 8.0, randomize_rotation: true }
  - id: "vetro_cristallo"
    type: "dielectric"
    refraction_index: 1.8

lights:
  - type: "area"
    corner: [-2.0, 4.9, -2.0]
    u: [4.0, 0.0, 0.0]
    v: [0.0, 0.0, 4.0]
    color: [1.0, 0.97, 0.92]
    intensity: 40.0
    shadow_samples: 16
  - type: "point"
    position: [-5, 3, -5]
    color: [0.8, 0.8, 1.0]
    intensity: 3

entities:
  - { name: "col_sx", type: "cylinder", center: [-3, 0, 0], radius: 0.3, height: 4, material: "marmo_colonna" }
  - { name: "col_dx", type: "cylinder", center: [3, 0, 0], radius: 0.3, height: 4, material: "marmo_colonna" }
  - name: "trave"
    type: "box"
    scale: [7.0, 0.5, 0.8]
    translate: [0.0, 4.25, 0.0]
    material: "marmo_colonna"
  - { name: "gioiello", type: "sphere", center: [0, 2, 0], radius: 0.8, material: "vetro_cristallo" }
```

## 9.3 — Neon Lab (Solo Illuminazione Emissiva)

Stanza buia illuminata esclusivamente da oggetti con materiale `emissive`. Dimostra emissione colorata, riflessione su metallo e rifrazione nel vetro.

```yaml
world:
  ambient_light: [0.005, 0.005, 0.008]
  background: [0.0, 0.0, 0.0]
  ground: { type: "infinite_plane", material: "pavimento", y: 0 }

camera:
  position: [0, 2.5, -8]
  look_at: [0, 1.2, 0]
  fov: 50

materials:
  - id: "pavimento"
    type: "lambertian"
    color: [0.12, 0.12, 0.14]
  - id: "specchio"
    type: "metal"
    color: [0.92, 0.92, 0.94]
    fuzz: 0.02
  - id: "neon_magenta"
    type: "emissive"
    color: [1.0, 0.05, 0.6]
    intensity: 8.0
  - id: "neon_ciano"
    type: "emissive"
    color: [0.05, 0.85, 1.0]
    intensity: 8.0
  - id: "pannello"
    type: "emissive"
    color: [1.0, 0.97, 0.92]
    intensity: 12.0

entities:
  - { name: "neon_sx", type: "sphere", center: [-2.5, 1, 0], radius: 0.6, material: "neon_magenta" }
  - { name: "neon_dx", type: "sphere", center: [2.5, 1, 0], radius: 0.6, material: "neon_ciano" }
  - name: "pannello_soffitto"
    type: "quad"
    q: [-1.0, 4.5, -1.0]
    u: [2.0, 0.0, 0.0]
    v: [0.0, 0.0, 2.0]
    material: "pannello"
  - { name: "specchio_sfera", type: "sphere", center: [0, 0.8, -1], radius: 0.8, material: "specchio" }

lights:
  - type: "point"
    position: [0, 5, -3]
    color: [0.5, 0.5, 0.6]
    intensity: 0.5
```

## Scena con Gradient Sky + Sun Disk (Golden Hour)

Sfere metalliche riflettono il gradiente del cielo; la sfera di vetro lo rifrange. Il sun disk è visibile nei riflessi.

```yaml
world:
  ambient_light: [0.04, 0.03, 0.02]
  sky:
    type: "gradient"
    zenith_color:  [0.15, 0.25, 0.55]
    horizon_color: [0.85, 0.55, 0.25]
    ground_color:  [0.20, 0.15, 0.10]
    sun:
      direction: [-0.8, -0.25, -0.5]
      color: [1.0, 0.85, 0.5]
      intensity: 20.0
      size: 4.0
      falloff: 24.0
  ground: { type: "infinite_plane", material: "terreno", y: 0 }

camera:
  position: [0, 1.8, -8]
  look_at: [0, 0.8, 0]
  fov: 55

materials:
  - id: "terreno"
    type: "lambertian"
    texture: { type: "checker", scale: 1.5, colors: [[0.25, 0.22, 0.18], [0.35, 0.32, 0.26]] }
  - id: "specchio"
    type: "metal"
    color: [0.95, 0.95, 0.97]
    fuzz: 0.0
  - id: "oro"
    type: "metal"
    color: [0.85, 0.65, 0.12]
    fuzz: 0.05
  - id: "vetro"
    type: "dielectric"
    refraction_index: 1.52

entities:
  - { name: "mirror", type: "sphere", center: [-2.5, 1, 0], radius: 1.0, material: "specchio" }
  - { name: "gold", type: "sphere", center: [0, 1, 0], radius: 1.0, material: "oro" }
  - { name: "glass", type: "sphere", center: [2.5, 1, 0], radius: 1.0, material: "vetro" }

lights:
  - type: "directional"
    direction: [-0.8, -0.25, -0.5]
    color: [1.0, 0.88, 0.55]
    intensity: 0.12
  - type: "directional"
    direction: [0.5, -0.7, 0.3]
    color: [0.5, 0.6, 0.85]
    intensity: 0.03
```

## Scena HDRI con Materiali PBR

Sfere con materiali diversi illuminate esclusivamente da un environment map HDR. Nessuna luce esplicita — tutta l'illuminazione viene dalla fotografia dell'ambiente.

```yaml
world:
  ambient_light: [0.0, 0.0, 0.0]
  sky:
    type: "hdri"
    path: "hdri/studio_small_09_4k.hdr"
    intensity: 1.0
    rotation: 0
  ground: { type: "infinite_plane", material: "pavimento", y: 0 }

camera:
  position: [0, 1.5, -5]
  look_at: [0, 0.8, 0]
  fov: 50

materials:
  - id: "pavimento"
    type: "metal"
    color: [0.15, 0.15, 0.18]
    fuzz: 0.4
  - id: "specchio"
    type: "metal"
    color: [0.97, 0.97, 0.98]
    fuzz: 0.0
  - id: "oro"
    type: "metal"
    color: [0.85, 0.65, 0.12]
    fuzz: 0.02
  - id: "vetro"
    type: "dielectric"
    refraction_index: 1.52
  - id: "diffuso"
    type: "lambertian"
    color: [0.85, 0.85, 0.85]

entities:
  - { name: "mirror", type: "sphere", center: [-2, 0.8, 0], radius: 0.8, material: "specchio" }
  - { name: "gold", type: "sphere", center: [0, 0.8, 0], radius: 0.8, material: "oro" }
  - { name: "glass", type: "sphere", center: [2, 0.8, 0], radius: 0.8, material: "vetro" }

lights: []
```

---

---

[← Torna all'indice](../02-tutorial-scene.md)
