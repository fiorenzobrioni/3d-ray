# 7. Materiali Consigliati per CSG

Copia questi materiali nella sezione `materials:` del tuo YAML per usarli nei preset di questa libreria.

```yaml
materials:

  # Vetro e ottiche
  - id: "vetro_chiaro"
    type: "disney"
    color: [1.0, 1.0, 1.0]
    roughness: 0.0
    spec_trans: 1.0
    ior: 1.5

  - id: "vetro_ottico"
    type: "disney"
    color: [0.97, 0.99, 1.0]
    roughness: 0.0
    spec_trans: 1.0
    ior: 1.62

  - id: "diamante"
    type: "disney"
    color: [1.0, 1.0, 1.0]
    roughness: 0.0
    spec_trans: 1.0
    ior: 2.42

  - id: "vetro_smerigliato"
    type: "disney"
    color: [0.95, 0.95, 1.0]
    roughness: 0.35
    spec_trans: 0.9
    ior: 1.5

  # Metalli
  - id: "acciaio_satinato"
    type: "disney"
    color: [0.58, 0.57, 0.55]
    metallic: 1.0
    roughness: 0.45

  - id: "oro"
    type: "disney"
    color: [1.0, 0.71, 0.29]
    metallic: 1.0
    roughness: 0.15

  - id: "metallo_ruggine"
    type: "disney"
    color: [0.55, 0.28, 0.15]
    metallic: 0.7
    roughness: 0.8

  # Plastiche
  - id: "plastica_bianca"
    type: "disney"
    color: [0.92, 0.92, 0.92]
    roughness: 0.6
    metallic: 0.0

  # Pietra e ceramica
  - id: "marmo_bianco"
    type: "lambertian"
    texture:
      type: "marble"
      scale: 15.0
      noise_strength: 10.0
      colors: [[0.96, 0.96, 0.96], [0.55, 0.55, 0.55]]

  - id: "marmo_nero"
    type: "lambertian"
    texture:
      type: "marble"
      scale: 12.0
      noise_strength: 10.0
      colors: [[0.05, 0.05, 0.05], [0.6, 0.6, 0.6]]

  - id: "pietra"
    type: "lambertian"
    texture:
      type: "noise"
      scale: 6.0
      colors: [[0.55, 0.52, 0.48], [0.42, 0.40, 0.36]]

  - id: "cemento"
    type: "lambertian"
    texture:
      type: "noise"
      scale: 8.0
      colors: [[0.60, 0.60, 0.60], [0.50, 0.50, 0.50]]

  - id: "mattoni"
    type: "lambertian"
    color: [0.55, 0.25, 0.18]

  - id: "ceramica_bianca"
    type: "disney"
    color: [0.95, 0.93, 0.90]
    roughness: 0.2
    specular: 0.5

  # Organici
  - id: "avorio"
    type: "lambertian"
    color: [0.96, 0.93, 0.82]

  # Emissivi
  - id: "luce_calda"
    type: "emissive"
    color: [1.0, 0.85, 0.6]
    intensity: 15.0
```

---

---

[← Torna all'indice](../04-libreria-csg.md)
