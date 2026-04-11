# Tutorial 03: Tecniche Avanzate

In questo modulo esploreremo come gestire la complessità in scene di grandi dimensioni, come riutilizzare gli oggetti e come creare forme complesse tramite operazioni booleane (CSG) o modelli esterni (OBJ).

---

## 1. Gruppi (Gerarchie)
I gruppi permettono di comporre più oggetti insieme e muoverli come un'unica entità. Qualsiasi trasformazione (translate, rotate, scale) applicata al gruppo viene ereditata dai figli.

```yaml
- name: "tavolo"
  type: "group"
  translate: [5, 0, 0]
  children:
    - type: "box"    # Il piano del tavolo
      scale: [2, 0.1, 1]
      translate: [0, 0.75, 0]
    - type: "cylinder" # Una gamba
      center: [-0.9, 0, -0.4]
      radius: 0.05
      height: 0.75
```

## 2. Template e Istanze (DRY)
Se devi usare lo stesso oggetto molte volte (es. i peli di un tappeto o le sedie di un teatro), usa i **templates**. Rendi il file YAML più leggibile e facile da mantenere.

```yaml
templates:
  - name: "sedia"
    children:
       # ... definizione della sedia ...

entities:
  - type: "instance"
    template: "sedia"
    translate: [0, 0, 0]
  - type: "instance"
    template: "sedia"
    translate: [2, 0, 0]
    material: "plastica_rossa"  # Posso sovrascrivere il materiale!
```

## 3. Import YAML (Modularià)
Puoi dividere la tua scena in più file. Carica librerie di materiali, setup di luci o collezioni di oggetti con una riga:

```yaml
imports:
  - path: "libraries/materials/metals.yaml"
  - path: "libraries/objects/furniture.yaml"
```

> [!TIP]
> Consulta la cartella `scenes/libraries/` per scoprire centinaia di materiali e oggetti già pronti all'uso!

## 4. CSG (Constructive Solid Geometry)
Il CSG ti permette di modellare oggetti "scolpendo" le forme:
- **Union**: Unisce due solidi.
- **Intersection**: Tiene solo la parte sovrapposta.
- **Subtraction**: Sottrae una forma dall'altra (es. un buco in un muro).

```yaml
- type: "csg"
  operation: "subtraction"
  left: { type: "box", scale: [2, 2, 0.1] } # Il muro
  right: { type: "sphere", radius: 0.5 }    # Il buco sferico
```

## 5. Mesh (Modelli OBJ)
Vuoi importare un modello creato in Blender? Usa il tipo `mesh`:

```yaml
- name: "statua"
  type: "mesh"
  path: "models/statue.obj"
  material: "marmo"
  scale: 0.1
```
Il motore carica il file e crea automaticamente una struttura di accelerazione (BVH) interna per rendere il render velocissimo anche con milioni di triangoli.

---

[Vai al Tutorial 04: Catalogo dei Preset](./04-catalogo-preset.md) | [Indice Tutorial](../tutorial/)
