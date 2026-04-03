# 2. Forme Ottiche e Lenti

## Lente Piano-Convessa

Una delle lenti più usate in ottica: un lato piatto, uno curvo.

```yaml
- name: "lente_piano_convessa"
  type: "csg"
  operation: "intersection"
  material: "vetro_ottico"
  left:
    type: "sphere"
    center: [0, 1, -0.3]
    radius: 1.1
  right:
    type: "box"
    scale: [2.0, 2.0, 1.0]
    translate: [0, 1, 0.2]
```

## Prisma Triangolare

Un cilindro ritagliato per ottenere una sezione triangolare:

```yaml
- name: "prisma"
  type: "csg"
  operation: "intersection"
  material: "vetro_ottico"
  left:
    type: "cylinder"
    center: [0, 0, 0]
    radius: 1.2
    height: 2.0
    translate: [0, 0, 0]
  right:
    type: "csg"
    operation: "intersection"
    left:
      type: "box"
      scale: [3.0, 2.5, 1.2]
      rotate: [0, 0, 30]
      translate: [0, 1, 0]
    right:
      type: "box"
      scale: [3.0, 2.5, 1.2]
      rotate: [0, 0, -30]
      translate: [0, 1, 0]
```

## Gemma / Diamante Tagliato

Una forma a gemma ottenuta da un icosaedro approssimato (sfera intersecata con box multipli):

```yaml
- name: "gemma"
  type: "csg"
  operation: "intersection"
  material: "diamante"
  left:
    type: "csg"
    operation: "intersection"
    left:
      type: "sphere"
      center: [0, 1, 0]
      radius: 1.0
    right:
      type: "box"
      scale: [1.6, 1.1, 1.6]
      translate: [0, 1, 0]
  right:
    type: "box"
    scale: [1.6, 1.1, 1.6]
    rotate: [0, 45, 0]
    translate: [0, 1, 0]
```

---

---

[← Torna all'indice](../04-libreria-csg.md)
