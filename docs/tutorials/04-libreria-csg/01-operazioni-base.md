# 1. Operazioni Base CSG

## 2.1 Union — Fusione di Volumi

Due sfere che si fondono in un unico solido organico.

```yaml
- name: "blob_doppio"
  type: "csg"
  operation: "union"
  material: "plastica_bianca"
  left:
    type: "sphere"
    center: [-0.6, 1, 0]
    radius: 0.9
  right:
    type: "sphere"
    center: [0.6, 1, 0]
    radius: 0.9
```

**Clessidra (Due coni fusi):**
```yaml
- name: "clessidra"
  type: "csg"
  operation: "union"
  material: "vetro"
  left:
    type: "cone"
    center: [0, 0.2, 0]
    radius: 0.8
    height: 1.2
  right:
    type: "cone"
    center: [0, 2.6, 0]
    radius: 0.8
    height: 1.2
    rotate: [180, 0, 0]      # Capovolto per far toccare le punte
```

---

## 2.2 Intersection — Volume Comune

**Lente biconvessa** — l'intersezione di due sfere che si sovrappongono produce una lente classica:

```yaml
- name: "lente_biconvessa"
  type: "csg"
  operation: "intersection"
  material: "vetro_chiaro"
  left:
    type: "sphere"
    center: [0, 1, -0.5]
    radius: 1.2
  right:
    type: "sphere"
    center: [0, 1, 0.5]
    radius: 1.2
```

**Forma di diamante grezzo** — intersezione di un cubo ruotato con una sfera:

```yaml
- name: "diamante_grezzo"
  type: "csg"
  operation: "intersection"
  material: "diamante"
  left:
    type: "sphere"
    center: [0, 1, 0]
    radius: 1.1
  right:
    type: "box"
    scale: [1.4, 1.4, 1.4]
    rotate: [45, 45, 0]
    translate: [0, 1, 0]
```

**Cubo con angoli arrotondati** — intersezione di una sfera leggermente più grande e un cubo:

```yaml
- name: "cubo_arrotondato"
  type: "csg"
  operation: "intersection"
  material: "plastica_bianca"
  left:
    type: "sphere"
    center: [0, 1, 0]
    radius: 1.15
  right:
    type: "box"
    scale: [1.8, 1.8, 1.8]
    translate: [0, 1, 0]
```

---

## 2.3 Subtraction — Intaglio

**Scodella (sfera cava)** — sfera con metà inferiore e centro rimossi:

```yaml
- name: "scodella"
  type: "csg"
  operation: "subtraction"
  left:
    type: "sphere"
    center: [0, 1, 0]
    radius: 1.0
    material: "ceramica_bianca"
  right:
    type: "sphere"
    center: [0, 1, 0]
    radius: 0.85
    material: "ceramica_bianca"
```

> **💡 Nota:** Per una scodella vera devi anche tagliare la metà superiore con un box — vedi il preset completo nella [sezione Forme Creative](14-csg-forme-creative.md).

**Dado con foro** — box con cilindro passante:

```yaml
- name: "dado_foro"
  type: "csg"
  operation: "subtraction"
  material: "acciaio_satinato"
  left:
    type: "box"
    scale: [1.6, 1.6, 1.6]
    translate: [0, 0.8, 0]
  right:
    type: "cylinder"
    center: [0, -0.5, 0]
    radius: 0.45
    height: 3.0
```

**Muro con finestra** — box grande con box piccolo sottratto:

```yaml
- name: "muro_finestra"
  type: "csg"
  operation: "subtraction"
  material: "cemento"
  left:
    type: "box"
    scale: [4.0, 3.0, 0.3]
    translate: [0, 1.5, 0]
  right:
    type: "box"
    scale: [1.2, 1.2, 1.0]
    translate: [0, 1.8, 0]
```

---

---

[← Torna all'indice](../04-libreria-csg.md)
