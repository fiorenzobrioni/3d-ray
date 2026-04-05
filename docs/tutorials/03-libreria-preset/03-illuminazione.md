# 3. Sistemi di Illuminazione

Combinazioni di luci per scolpire la forma degli oggetti.

## **Preset: Three-Point Lighting (Standard)**
Il setup classico del cinema: Key Light (principale), Fill Light (riempimento) e Back Light (contorno).

> **Nota:** I valori di intensità in questo preset assumono oggetti principali a 6–9 unità di distanza dalle luci. Con l'attenuazione quadratica, a ~8 unità `intensity: 100` eroga circa 1.6 unità di luce effettiva — appropriato come key light dominante.

```yaml
lights:
  - type: "point"      # Key Light
    position: [5, 5, -5]
    intensity: 100
  - type: "point"      # Fill Light (ombre più morbide)
    position: [-5, 2, -2]
    intensity: 30
    color: [0.8, 0.8, 1.0]
  - type: "point"      # Back Light (distacca dallo sfondo)
    position: [0, 8, 5]
    intensity: 60
```

## **Preset: Area Light Studio (Soft Shadows)**
Un pannello luminoso da soffitto per ombre morbide con penombra realistica. Ideale per product design.
```yaml
lights:
  - type: "area"
    corner: [-2.0, 4.9, -2.0]
    u: [4.0, 0.0, 0.0]
    v: [0.0, 0.0, 4.0]
    color: [1.0, 0.97, 0.92]
    intensity: 40.0
    shadow_samples: 16
  - type: "point"       # Fill di riempimento laterale
    position: [-6, 2, -3]
    color: [0.8, 0.85, 1.0]
    intensity: 4
```

## **Preset: Studio Spot (Mirror Polish)**
Luce Spot concentrata per creare riflessi spettacolari su metallo o vetro.

> **Nota — Perché `intensity: 200`:** Lo spot accumula due attenuazioni: quadratica con la distanza (~11 unità → ÷121) e conica (solo i punti nel cono interno ricevono piena intensità). `intensity: 200` compensa entrambe producendo il classico highlight spettacolare "a puntino" su superfici metalliche o vetrose. Per scene a distanze diverse, scala proporzionalmente a `d²`.

```yaml
lights:
  - type: "spot"
    position: [0, 10, -5]
    direction: [0, -1, 1]
    intensity: 200
    inner_angle: 10
    outer_angle: 25
    color: [1, 1, 1]
```

## **Preset: Warm & Cool Contrast**
Combinazione di luce calda (arancio) e fredda (blu) sui lati opposti del soggetto.
```yaml
lights:
  - type: "point"
    position: [-5, 3, -2]
    color: [1.0, 0.4, 0.1] # Arancio
    intensity: 80
  - type: "point"
    position: [5, 3, -2]
    color: [0.1, 0.4, 1.0] # Blu
    intensity: 80
```

## **Preset: Moonlight (Luce Lunare)**
Atmosfera notturna fredda con ombre molto allungate. La luna è molto più debole del sole ma,
come unica sorgente in una scena notturna scura, `intensity: 0.4` produce un'illuminazione
sottile e credibile senza sovraesporre.
```yaml
lights:
  - type: "directional"
    direction: [0.2, -1, 0.1]
    color: [0.7, 0.7, 1.0]
    intensity: 0.4
```

---

---

[← Torna all'indice](../03-libreria-preset.md)
