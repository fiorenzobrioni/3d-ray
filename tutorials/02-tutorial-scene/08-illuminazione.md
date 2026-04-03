# 8. Illuminazione: Come Funziona

## Il rendering è un path tracer
Ogni pixel spara raggi nella scena. Quando un raggio colpisce una superficie, il materiale genera un raggio rimbalzato. Il processo continua fino a:
- Il raggio esce dalla scena → riceve il colore del cielo (`background` piatto oppure `sky` gradiente)
- Il raggio raggiunge la profondità massima (`--depth`) → restituisce nero

Questo significa che il cielo è effettivamente una **sorgente di luce**. Se vuoi una scena dove solo le luci esplicite illuminano, devi impostare `background: [0, 0, 0]` (e non definire `sky`).

Con il **gradient sky**, il colore del cielo varia in base alla direzione del raggio: azzurro dallo zenit, caldo dall'orizzonte. Questo produce un'illuminazione globale molto più ricca e naturale rispetto al background piatto — le ombre hanno una tinta azzurra (luce dal cielo) e le superfici rivolte verso l'orizzonte ricevono luce più calda.

## Ordine di calcolo per ogni hit

Per ogni punto colpito da un raggio, il motore calcola la luce finale considerando diversi fattori:
- **Dettaglio di superficie**: Se presente una normal map, vengono aggiunti i piccoli rilievi.
- **Auto-illuminazione**: Se l'oggetto è emissivo, aggiunge la sua luminosità.
- **Illuminazione Diretta**: Viene calcolato l'impatto di tutte le luci presenti nella scena (ombre e riflessi).
- **Illuminazione Indiretta**: Viene calcolata la luce rimbalzata dalle altre superfici vicine.

---

## Combinazioni di Luci Consigliate

**Esterno Diurno (background piatto legacy):**
```yaml
lights:
  - type: "directional"
    direction: [-0.5, -1, -0.3]
    color: [1.0, 0.95, 0.85]
    intensity: 0.09
  - type: "point"
    position: [0, 20, 0]
    intensity: 10
```

**Esterno con Gradient Sky (raccomandato):**
```yaml
world:
  ambient_light: [0.04, 0.04, 0.06]
  sky:
    type: "gradient"
    zenith_color:  [0.10, 0.30, 0.80]
    horizon_color: [0.65, 0.80, 1.00]
    ground_color:  [0.30, 0.25, 0.20]
    sun:
      direction: [-0.5, -0.8, -0.3]
      color: [1.0, 0.95, 0.85]
      intensity: 15.0
      size: 2.5
      falloff: 40.0

lights:
  - type: "directional"
    direction: [-0.5, -0.8, -0.3]     # Stessa direzione del sun disk!
    color: [1.0, 0.95, 0.85]
    intensity: 0.09
```

**Studio con Area Light (ombre morbide):**
```yaml
lights:
  - type: "area"
    corner: [-2.0, 4.9, -2.0]
    u: [4.0, 0.0, 0.0]
    v: [0.0, 0.0, 4.0]
    color: [1.0, 0.97, 0.9]
    intensity: 40.0
    shadow_samples: 16
  - type: "point"
    position: [-5, 3, -3]
    color: [0.8, 0.85, 1.0]
    intensity: 3
```

**Studio con Spot:**
```yaml
lights:
  - type: "spot"
    position: [0, 8, -5]
    direction: [0, -1, 0.5]
    color: [1.0, 0.95, 0.9]
    intensity: 15
    inner_angle: 20
    outer_angle: 40
```

**Interno Intimo:**
```yaml
lights:
  - type: "point"
    position: [0, 3, 0]
    color: [1.0, 0.8, 0.5]
    intensity: 4
```

**Scena illuminata solo da oggetti emissivi (Neon Lab):**
```yaml
world:
  ambient_light: [0.005, 0.005, 0.008]
  background: [0.0, 0.0, 0.0]

# Nessuna luce esplicita — la scena è illuminata dagli emissivi
lights: []

materials:
  - id: "neon_ciano"
    type: "emissive"
    color: [0.05, 0.85, 1.0]
    intensity: 8.0

entities:
  - { name: "neon", type: "sphere", center: [0, 2, 0], radius: 0.5, material: "neon_ciano" }
```

> **💡 Nota:** Quando la scena è illuminata solo da materiali emissivi, non ci sono luci per il Next Event Estimation. Tutta l'illuminazione arriva dai rimbalzi indiretti. Usa campioni alti (`-s 128+`) e profondità adeguata (`-d 10+`) per risultati puliti. Puoi aggiungere una `point` light con intensità molto bassa (0.2–1.0) come fill minimale per evitare ombre completamente nere.

---

---

[← Torna all'indice](../02-tutorial-scene.md)
