# 4. Forme Architettoniche

## Arco a Tutto Sesto

Un box rettangolare con un cilindro orizzontale scavato in cima:

```yaml
- name: "arco"
  type: "csg"
  operation: "subtraction"
  material: "pietra"
  left:
    type: "box"
    scale: [3.0, 4.0, 0.6]
    translate: [0, 2.0, 0]
  right:
    type: "csg"
    operation: "union"
    left:
      # Apertura ad arco (cilindro orizzontale)
      type: "cylinder"
      center: [0, 0, 0]
      radius: 0.9
      height: 1.0
      rotate: [90, 0, 0]
      translate: [0, 2.8, 0]
    right:
      # Parte bassa dritta dell'apertura
      type: "box"
      scale: [1.8, 2.8, 1.0]
      translate: [0, 1.4, 0]
```

## Colonna con Capitello

Una colonna classica con una base quadrata e un capitello cubico:

```yaml
- name: "colonna_classica"
  type: "csg"
  operation: "union"
  material: "marmo_bianco"
  left:
    # Fusto cilindrico
    type: "cylinder"
    center: [0, 0.2, 0]
    radius: 0.3
    height: 3.0
  right:
    type: "csg"
    operation: "union"
    left:
      # Base (plinto)
      type: "box"
      scale: [0.9, 0.2, 0.9]
      translate: [0, 0.1, 0]
    right:
      # Capitello
      type: "box"
      scale: [0.8, 0.3, 0.8]
      translate: [0, 3.35, 0]
```

## Finestra ad Arco

Apertura rettangolare con semicerchio superiore in un muro:

```yaml
- name: "finestra_arco"
  type: "csg"
  operation: "subtraction"
  material: "mattoni"
  left:
    type: "box"
    scale: [5.0, 4.0, 0.4]
    translate: [0, 2.0, 0]
  right:
    type: "csg"
    operation: "union"
    left:
      # Parte rettangolare bassa
      type: "box"
      scale: [1.2, 2.0, 1.0]
      translate: [0, 1.0, 0]
    right:
      # Semicerchio superiore
      type: "sphere"
      center: [0, 2.0, 0]
      radius: 0.65
```

---

---

[← Torna all'indice](../04-libreria-csg.md)
