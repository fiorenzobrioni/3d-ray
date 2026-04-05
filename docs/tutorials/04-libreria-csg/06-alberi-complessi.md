# 6. Alberi CSG Complessi

## 6.1 Cubo Svizzero (A \ (B ∪ C ∪ D))

Un cubo con tre fori ortogonali passanti — ispirato al cubo di Menger:

```yaml
- name: "cubo_svizzero"
  type: "csg"
  operation: "subtraction"
  material: "plastica_bianca"
  left:
    type: "box"
    scale: [2.0, 2.0, 2.0]
    translate: [0, 1, 0]
  right:
    type: "csg"
    operation: "union"
    left:
      type: "csg"
      operation: "union"
      left:
        # Foro X
        type: "cylinder"
        center: [0, 1, 0]
        radius: 0.5
        height: 3.0
        rotate: [0, 0, 90]
      right:
        # Foro Y
        type: "cylinder"
        center: [0, 0, 0]
        radius: 0.5
        height: 3.0
    right:
      # Foro Z
      type: "cylinder"
      center: [0, 1, 0]
      radius: 0.5
      height: 3.0
      rotate: [90, 0, 0]
```

## 6.2 Ruota con Raggi

Hub centrale + cerchio esterno + raggi, ottenuti per differenza da un disco pieno:

```yaml
- name: "ruota_raggi"
  type: "csg"
  operation: "subtraction"
  material: "acciaio_satinato"
  left:
    # Disco pieno
    type: "cylinder"
    center: [0, 0, 0]
    radius: 1.2
    height: 0.25
  right:
    type: "csg"
    operation: "union"
    left:
      # 4 fori per i raggi (approssimati con box ruotati)
      type: "csg"
      operation: "union"
      left:
        type: "box"
        scale: [2.2, 0.35, 0.25]
        translate: [0, 0.12, 0]
      right:
        type: "box"
        scale: [2.2, 0.35, 0.25]
        rotate: [0, 90, 0]
        translate: [0, 0.12, 0]
    right:
      # Foro centrale (mozzo)
      type: "cylinder"
      center: [0, -0.5, 0]
      radius: 0.25
      height: 1.5
```

## 6.3 Lampada a Sospensione

Uno dei preset più completi: paralume (sfera cava aperta sotto) con bulbo emissivo:

```yaml
# Materiali necessari per questo preset:
# - "vetro_smerigliato": disney, roughness: 0.3, spec_trans: 0.9, ior: 1.5, color: [0.95,0.95,1.0]
# - "luce_calda":       emissive, color: [1.0, 0.85, 0.6], intensity: 15.0

- name: "paralume"
  type: "csg"
  operation: "subtraction"
  left:
    type: "csg"
    operation: "subtraction"
    left:
      # Guscio esterno
      type: "sphere"
      center: [0, 2.5, 0]
      radius: 0.85
      material: "vetro_smerigliato"
    right:
      # Guscio interno (rende cavo)
      type: "sphere"
      center: [0, 2.5, 0]
      radius: 0.75
      material: "vetro_smerigliato"
  right:
    # Apertura inferiore
    type: "box"
    scale: [2.0, 1.0, 2.0]
    translate: [0, 1.75, 0]

- name: "bulbo"
  type: "sphere"
  center: [0, 2.45, 0]
  radius: 0.3
  material: "luce_calda"
```

## 6.4 Pillola Farmaceutica (Multi-Materiale via CSG)

Esempio di come creare un singolo volume con materiali diversi (bicolore) utilizzando la CSG. Invece di affiancare due capsule, ne usiamo una sola che viene "tagliata" in due metà da operazioni di intersezione con volumi invisibili.

```yaml
- name: "pillola_bicolore"
  type: "csg"
  operation: "union"
  translate: [0, 1, 0]
  left:
    # Metà inferiore (bianca)
    type: "csg"
    operation: "intersection"
    material: "bianco"
    left:
      type: "capsule"
      center: [0, 0, 0]
      radius: 0.4
      height: 1.0
    right:
      type: "box"
      scale: [2, 1, 2]         # Altezza 1.0
      translate: [0, 0, 0]     # Copre Y da -0.5 a 0.5 (taglio a 0.5)
  right:
    # Metà superiore (rossa)
    type: "csg"
    operation: "intersection"
    material: "rosso"
    left:
      type: "capsule"
      center: [0, 0, 0]
      radius: 0.4
      height: 1.0
    right:
      type: "box"
      scale: [2, 1, 2]         # Altezza 1.0
      translate: [0, 1, 0]     # Copre Y da 0.5 a 1.5 (giunzione perfetta a 0.5)
```

---

---

[← Torna all'indice](../04-libreria-csg.md)
