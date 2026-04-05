# 8. Scene Base Complete (Stage Starter)

Questi "modelli pronti" includono tutto il necessario bilanciato per iniziare.

## **Stage A: Studio Fotografico Professionale**
Un set pulito con illuminazione area light e fill laterale.
```yaml
world:
  ambient_light: [0.08, 0.08, 0.08]
  background: [0.9, 0.9, 0.9]
  ground: { type: "infinite_plane", material: "studio_floor", y: 0 }

camera:
  position: [0, 2, -10]
  look_at: [0, 1, 0]
  fov: 40

materials:
  - id: "studio_floor"
    type: "lambertian"
    texture: { type: "checker", scale: 4.0, colors: [[0.8, 0.8, 0.8], [0.85, 0.85, 0.85]] }
  - id: "piedistallo_mat"
    type: "lambertian"
    color: [0.9, 0.9, 0.9]

lights:
  - type: "area"
    corner: [-2.5, 4.9, -2.5]
    u: [5.0, 0.0, 0.0]
    v: [0.0, 0.0, 5.0]
    color: [1.0, 0.97, 0.92]
    intensity: 35.0
    shadow_samples: 16
  - type: "point"
    position: [-8, 4, -5]
    color: [0.85, 0.85, 1.0]
    intensity: 20

entities:
  - name: "pedestal"
    type: "box"
    scale: [2.0, 0.2, 2.0]
    translate: [0.0, 0.1, 0.0]
    material: "piedistallo_mat"
```

## **Stage B: Tramonto (Esterno Drammatico)**
Un orizzonte vasto con luce calda diagonale e suolo metallico riflettente.
```yaml
world:
  ambient_light: [0.05, 0.05, 0.1]
  background: [0.8, 0.4, 0.2]
  ground: { type: "infinite_plane", material: "ocean_metal", y: 0 }

camera:
  position: [0, 1, -15]
  look_at: [0, 1.5, 0]
  fov: 35

materials:
  - id: "ocean_metal"
    type: "metal"
    color: [0.1, 0.2, 0.4]
    fuzz: 0.1

lights:
  # intensity: 2 — valore alto intenzionale: la directional è l'unica sorgente
  # di questa scena outdoor drammatica. Senza attenuazione con la distanza,
  # deve compensare da sola per illuminare l'intera superficie.
  - type: "directional"
    direction: [-1, -0.2, -1]
    color: [1, 0.4, 0.1]
    intensity: 2
```

## **Stage C: Neon Cyber-Point (Creativo)**
Un set ad alto contrasto con luci magenta e ciano, stile sci-fi.
```yaml
world:
  ambient_light: [0.01, 0, 0.02]
  background: [0.0, 0.0, 0.0]

camera:
  position: [0, 2, -10]
  look_at: [0, 1.5, 0]
  fov: 60

materials:
  - id: "metal_neon"
    type: "metal"
    color: [0.1, 0.1, 0.2]
    fuzz: 0.05

lights:
  - type: "point"
    position: [-4, 3, 0]
    color: [1, 0, 1]
    intensity: 150
  - type: "point"
    position: [4, 3, 0]
    color: [0, 1, 1]
    intensity: 150
```

## **Stage D: Galleria d'Arte (Area Light + Arch)**
Interno architettonico con illuminazione da soffitto morbida, pavimento in marmo e colonnato.
```yaml
world:
  ambient_light: [0.03, 0.03, 0.04]
  background: [0.0, 0.0, 0.0]
  ground: { type: "infinite_plane", material: "pavimento_marmo", y: 0 }

camera:
  position: [0, 2.5, -12]
  look_at: [0, 2, 0]
  fov: 45

materials:
  - id: "pavimento_marmo"
    type: "lambertian"
    texture:
      type: "marble"
      scale: 6.0
      noise_strength: 8.0
      colors: [[0.9, 0.88, 0.85], [0.4, 0.35, 0.3]]
  - id: "colonna_marmo"
    type: "lambertian"
    texture:
      type: "marble"
      scale: 10.0
      randomize_rotation: true
      colors: [[0.95, 0.95, 0.95], [0.6, 0.6, 0.6]]
  - id: "esposto"
    type: "dielectric"
    refraction_index: 1.7
    color: [0.85, 0.7, 0.2]

lights:
  - type: "area"
    corner: [-3.0, 5.9, -3.0]
    u: [6.0, 0.0, 0.0]
    v: [0.0, 0.0, 6.0]
    color: [1.0, 0.96, 0.88]
    intensity: 45.0
    shadow_samples: 16
  - type: "spot"
    position: [0, 5.5, 0]
    direction: [0, -1, 0]
    color: [1.0, 0.98, 0.9]
    intensity: 20
    inner_angle: 15
    outer_angle: 30

entities:
  - { name: "col_sx", type: "cylinder", center: [-3.5, 0, 1], radius: 0.3, height: 5.5, material: "colonna_marmo" }
  - { name: "col_dx", type: "cylinder", center: [3.5, 0, 1], radius: 0.3, height: 5.5, material: "colonna_marmo" }
  - { name: "oggetto_esposto", type: "sphere", center: [0, 1.5, 0], radius: 1.2, material: "esposto" }
```

## **Stage E: Golden Hour con Gradient Sky**
Scena outdoor con cielo procedurale, sole basso e luce calda. Ideale per paesaggi e architettura esterna.
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

lights:
  - type: "directional"
    direction: [-0.8, -0.25, -0.5]
    color: [1.0, 0.88, 0.55]
    intensity: 0.12
```

---

> **💡 Consigli d'uso:**
> - Usa `randomize_offset: true` e `randomize_rotation: true` nelle texture procedurali per far apparire ogni oggetto unico anche con lo stesso materiale.
> - Per gli Stage che usano area light, usa `-S 4 -s 1` da CLI per il draft, poi `-S 16 -s 128` per il render finale — non serve modificare il YAML!
> - I seed fissi negli oggetti garantiscono che le venature siano identiche tra render successivi — utile per iterare sull'illuminazione senza cambiare l'aspetto dei materiali.

---

---

[← Torna all'indice](../03-libreria-preset.md)
