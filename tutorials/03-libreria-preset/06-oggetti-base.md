# 6. Oggetti e Primitive Base

Strutture di supporto pronte all'uso.

> **Nota:** Il box usa il **centro** come riferimento per il `translate`. Per posizionare la base a Y=0, trasloca di `altezza / 2` in Y.

## **Piedistallo Moderno**
Un semplice blocco (altezza 0.8) su cui esporre un oggetto.
```yaml
  - name: "piedistallo"
    type: "box"
    scale: [2.0, 0.8, 2.0]
    translate: [0.0, 0.4, 0.0]   # Metà altezza in Y → base a Y=0
    material: "marmo_base"
```

## **Base Espositiva Circolare**
Un cilindro basso e largo per presentare prodotti.
```yaml
  - name: "base_expo"
    type: "cylinder"
    center: [0.0, 0.0, 0.0]   # Centro della base inferiore
    radius: 3.0
    height: 0.2
    material: "metallo_scuro"
```

## **Parete con Cornice**
Una parete (quad) con una cornice applicata.
```yaml
  - name: "parete"
    type: "quad"
    q: [-5, 0, 5]
    u: [10, 0, 0]
    v: [0, 8, 0]
    material: "muro_bianco"
  - name: "cornice"
    type: "box"
    scale: [4.0, 3.0, 0.1]
    translate: [0.0, 4.0, 4.9]   # Leggermente davanti alla parete
    material: "legno_noce"
```

## **Teca di Vetro**
Un box trasparente protettivo.
```yaml
  - name: "teca"
    type: "box"
    scale: [4, 4, 4]
    translate: [0, 2, 0]
    material: "vetro_fume"
```

## **Colonnato (Due Colonne + Trave)**
Struttura architettonica classica.
```yaml
  - { name: "col_sx", type: "cylinder", center: [-3, 0, 0], radius: 0.35, height: 4.0, material: "marmo_carrara" }
  - { name: "col_dx", type: "cylinder", center: [3, 0, 0], radius: 0.35, height: 4.0, material: "marmo_carrara" }
  - name: "trave"
    type: "box"
    scale: [7.5, 0.5, 0.9]
    translate: [0.0, 4.25, 0.0]
    material: "marmo_carrara"
```

---

---

[← Torna all'indice](../03-libreria-preset.md)
