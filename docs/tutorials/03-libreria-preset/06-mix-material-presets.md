# Mix Material — Preset di Materiali Compositi

Collezione di ricette pronte per effetti di blending comuni. Ogni preset
richiede che i materiali base siano definiti separatamente (vedi prerequisiti).

---

## **Weathering: Metallo Arrugginito (Noise Mask)**

Prerequisiti: `cromo` (metal), `ruggine` (disney roughness=0.9)

```yaml
  - id: "metallo_arrugginito"
    type: "mix"
    material_a: "cromo"
    material_b: "ruggine"
    mask:
      type: "noise"
      scale: 3.0
      noise_strength: 5.0
```

## **Weathering: Patina su Bronzo**

Prerequisiti: `bronzo` (metal fuzz=0.08), `patina_verde` (disney roughness=0.85)

```yaml
  - id: "bronzo_patinato"
    type: "mix"
    material_a: "bronzo"
    material_b: "patina_verde"
    mask:
      type: "noise"
      scale: 2.5
      noise_strength: 4.0
```

## **Naturale: Lava che si Raffredda**

Prerequisiti: `roccia_scura` (lambertian), `lava` (emissive intensity=6-10)

```yaml
  - id: "lava_cooling"
    type: "mix"
    material_a: "roccia_scura"
    material_b: "lava"
    mask:
      type: "marble"
      scale: 5.0
      noise_strength: 8.0
      colors: [[0.0, 0.0, 0.0], [1.0, 1.0, 1.0]]
```

## **Naturale: Neve su Roccia**

Prerequisiti: `roccia` (lambertian con noise texture), `neve` (lambertian bianco)

```yaml
  - id: "roccia_innevata"
    type: "mix"
    material_a: "roccia"
    material_b: "neve"
    mask:
      type: "noise"
      scale: 2.0
      noise_strength: 3.0
```

## **Usura: Vernice Scrostata**

Prerequisiti: `vernice` (disney con clearcoat), `sottostrato` (lambertian o wood)

```yaml
  - id: "vernice_scrostata"
    type: "mix"
    material_a: "vernice"
    material_b: "sottostrato"
    mask:
      type: "noise"
      scale: 5.0
      noise_strength: 4.0
```

## **Decorativo: Scacchiera Bicolore**

Prerequisiti: qualsiasi coppia di materiali

```yaml
  - id: "scacchiera_materiali"
    type: "mix"
    material_a: "materiale_chiaro"
    material_b: "materiale_scuro"
    mask:
      type: "checker"
      scale: 4.0
      colors: [[1.0, 1.0, 1.0], [0.0, 0.0, 0.0]]
```

## **Transizione Morbida: Gradiente Costante**

Per transizioni semplici senza pattern spaziale.

```yaml
  # Blend uniforme 30% materiale B
  - id: "transizione_morbida"
    type: "mix"
    material_a: "base"
    material_b: "overlay"
    blend: 0.3
```

---

[← Torna all'indice](../03-libreria-preset.md)
