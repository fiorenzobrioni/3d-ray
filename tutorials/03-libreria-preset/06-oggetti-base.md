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

## **O-ring / Guarnizione**
Un toro sottile in gomma per dettagli meccanici o idraulici.
```yaml
  - name: "oring"
    type: "torus"
    major_radius: 1.2
    minor_radius: 0.1
    translate: [0, 0.5, 0]
    material: "gomma_rossa"
```

## **Guarnizione O-ring Piatta**
Anello piatto in gomma, più efficiente del toro per scopi decorativi su superfici piane.
```yaml
  - name: "guarnizione_piatta"
    type: "annulus"
    center: [0, 0, 0]
    normal: [0, 1, 0]
    radius: 2.0
    inner_radius: 1.6
    material: "gomma_nera"
```

## **Neon Circolare (Anello Luminoso)**
Sorgente luminosa toroidale emissiva.
```yaml
  - name: "neon_anello"
    type: "torus"
    major_radius: 1.0
    minor_radius: 0.05
    translate: [0, 3, 0]
    material: "neon_rosa"        # materiale emissive, intensity: 12
```

## **Neon ad Anello Piatto (Emissivo)**
Sorgente luminosa anulare sottile, ideale per plafoniere moderne o retroilluminazione.
```yaml
  - name: "neon_anello_piatto"
    type: "annulus"
    center: [0, 3, 0]
    normal: [0, 1, 0]
    radius: 1.2
    inner_radius: 1.0
    material: "neon_bianco"      # emissive, intensity: 15
```

## **Capsula Medica**
Una pillola bicolore pronta per il rendering macro.
```yaml
  - name: "capsula_superiore"
    type: "capsule"
    center: [0, 0, 0]
    radius: 0.2
    height: 0.6
    material: "plastica_rossa"
  - name: "capsula_inferiore"
    type: "capsule"
    center: [0, -0.6, 0]
    radius: 0.2
    height: 0.6
    material: "plastica_bianca"
```

## **Rondella / Anello Piatto**
Dettaglio meccanico da applicare alla base di un bullone.
```yaml
  - name: "rondella"
    type: "annulus"
    center: [0, 0, 0]
    normal: [0, 1, 0]
    radius: 0.4
    inner_radius: 0.2
    material: "acciaio"
```

## **Bersaglio (Anelli Concentrici)**
Esempio di come creare motivi geometrici piatti impilando diversi Annulus e un Disk finale.
```yaml
  # Anello esterno rosso
  - name: "bersaglio_esterno"
    type: "annulus"
    center: [0, 2, 5]
    normal: [0, 0, -1]
    radius: 1.5
    inner_radius: 1.0
    material: "rosso"

  # Anello medio bianco
  - name: "bersaglio_medio"
    type: "annulus"
    center: [0, 2, 5]
    normal: [0, 0, -1]
    radius: 1.0
    inner_radius: 0.5
    material: "bianco"

  # Centro rosso (disco pieno)
  - name: "bersaglio_centro"
    type: "disk"
    center: [0, 2, 5]
    normal: [0, 0, -1]
    radius: 0.5
    material: "rosso"
```

---

---

[← Torna all'indice](../03-libreria-preset.md)
