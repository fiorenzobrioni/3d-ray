# 8. Scene Starter CSG

Scene complete e bilanciate che mostrano la CSG in contesti reali.

## Studio Ottiche

Lenti e prismi su un piano riflettente, illuminazione area light da soffitto.

```yaml
world:
  ambient_light: [0.05, 0.05, 0.08]
  background: [0.08, 0.08, 0.12]

camera:
  position: [0, 2.5, -6]
  look_at: [0, 1.2, 0]
  fov: 42

materials:
  - id: "vetro_ottico"
    type: "disney"
    color: [0.97, 0.99, 1.0]
    roughness: 0.0
    spec_trans: 1.0
    ior: 1.62
  - id: "piano_nero"
    type: "disney"
    color: [0.04, 0.04, 0.04]
    roughness: 0.1
    metallic: 0.0

entities:
  - name: "suolo"
    type: "infinite_plane"
    point: [0, 0, 0]
    normal: [0, 1, 0]
    material: "piano_nero"

  - name: "lente_sx"
    type: "csg"
    operation: "intersection"
    material: "vetro_ottico"
    left:
      type: "sphere"
      center: [-2, 1.2, 0]
      radius: 1.3
    right:
      type: "sphere"
      center: [-1.3, 1.2, 0]
      radius: 1.3

  - name: "lente_dx"
    type: "csg"
    operation: "intersection"
    material: "vetro_ottico"
    left:
      type: "sphere"
      center: [1.5, 1.2, 0.4]
      radius: 1.5
    right:
      type: "sphere"
      center: [1.5, 1.2, -0.4]
      radius: 1.5

lights:
  - type: "area"
    corner: [-3.0, 5.9, -3.0]
    u: [6.0, 0.0, 0.0]
    v: [0.0, 0.0, 6.0]
    color: [0.9, 0.92, 1.0]
    intensity: 30.0
    shadow_samples: 16
```

## Esposizione Metalli

Oggetti industriali CSG (dado, tubo, anello) in uno studio con illuminazione cinematografica.

```yaml
world:
  ambient_light: [0.03, 0.03, 0.03]
  background: [0.0, 0.0, 0.0]

camera:
  position: [0, 3, -7]
  look_at: [0, 1, 0]
  fov: 38

materials:
  - id: "acciaio"
    type: "disney"
    color: [0.58, 0.57, 0.55]
    metallic: 1.0
    roughness: 0.35
  - id: "piedistallo"
    type: "disney"
    color: [0.1, 0.1, 0.12]
    roughness: 0.15
    metallic: 0.0

entities:
  - name: "suolo"
    type: "infinite_plane"
    point: [0, 0, 0]
    normal: [0, 1, 0]
    material: "piedistallo"

  - name: "anello_grande"
    type: "csg"
    operation: "subtraction"
    material: "acciaio"
    left:
      type: "cylinder"
      center: [-2.5, 0, 0]
      radius: 0.9
      height: 0.35
    right:
      type: "cylinder"
      center: [-2.5, -0.5, 0]
      radius: 0.62
      height: 1.5

  - name: "dado_centrale"
    type: "csg"
    operation: "subtraction"
    material: "acciaio"
    left:
      type: "box"
      scale: [1.4, 1.4, 1.4]
      translate: [0, 0.7, 0]
    right:
      type: "cylinder"
      center: [0, -0.5, 0]
      radius: 0.38
      height: 3.0

  - name: "tubo_dx"
    type: "csg"
    operation: "subtraction"
    material: "acciaio"
    left:
      type: "cylinder"
      center: [2.5, 0, 0]
      radius: 0.5
      height: 2.5
    right:
      type: "cylinder"
      center: [2.5, -0.5, 0]
      radius: 0.38
      height: 3.5

lights:
  - type: "point"
    position: [-4, 6, -4]
    color: [1.0, 0.97, 0.90]
    intensity: 180
  - type: "point"
    position: [5, 3, -2]
    color: [0.7, 0.75, 1.0]
    intensity: 40
```

---

---

[← Torna all'indice](../04-libreria-csg.md)
