# 5. Forme Creative

## Scodella / Coppetta

Sfera cava tagliata a metà:

```yaml
- name: "scodella"
  type: "csg"
  operation: "subtraction"
  left:
    # Guscio: sfera grande meno sfera interna
    type: "csg"
    operation: "subtraction"
    left:
      type: "sphere"
      center: [0, 0, 0]
      radius: 1.0
      material: "ceramica_bianca"
    right:
      type: "sphere"
      center: [0, 0, 0]
      radius: 0.88
      material: "ceramica_bianca"
  right:
    # Taglia la metà superiore
    type: "box"
    scale: [3, 2, 3]
    translate: [0, 1.5, 0]
```

## Luna Crescente

Due sfere: sottrae la sfera di destra leggermente spostata da quella di sinistra:

```yaml
- name: "luna_crescente"
  type: "csg"
  operation: "subtraction"
  left:
    type: "sphere"
    center: [0, 1, 0]
    radius: 1.0
    material: "avorio"
  right:
    type: "sphere"
    center: [0.55, 1, -0.3]
    radius: 0.9
    material: "avorio"
```

## Stella a 4 Punte

Union di due box ruotati:

```yaml
- name: "stella_4_punte"
  type: "csg"
  operation: "union"
  material: "oro"
  left:
    type: "box"
    scale: [2.5, 0.6, 0.6]
    translate: [0, 1, 0]
  right:
    type: "box"
    scale: [0.6, 0.6, 2.5]
    translate: [0, 1, 0]
```

## Croce 3D

Tre box ortogonali in union:

```yaml
- name: "croce_3d"
  type: "csg"
  operation: "union"
  material: "marmo_bianco"
  left:
    type: "csg"
    operation: "union"
    left:
      type: "box"
      scale: [3.0, 0.7, 0.7]
      translate: [0, 1, 0]
    right:
      type: "box"
      scale: [0.7, 3.0, 0.7]
      translate: [0, 1, 0]
  right:
    type: "box"
    scale: [0.7, 0.7, 3.0]
    translate: [0, 1, 0]
```

## Sfera con Cavità Cubica (Negativo)

```yaml
- name: "sfera_cavita"
  type: "csg"
  operation: "subtraction"
  left:
    type: "sphere"
    center: [0, 1, 0]
    radius: 1.0
    material: "marmo_nero"
  right:
    type: "box"
    scale: [1.1, 1.1, 1.1]
    rotate: [20, 35, 10]
    translate: [0, 1, 0]
    material: "marmo_nero"
```

---

---

[← Torna all'indice](../04-libreria-csg.md)
