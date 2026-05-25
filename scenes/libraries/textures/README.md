# Libreria Texture — 3D-Ray

Raccolta di **20 texture PNG** pronte all'uso nei materiali della scena.
Le texture non si importano con `imports:` — si referenziano direttamente
con il percorso relativo nel campo appropriato del materiale YAML
(ad esempio `texture_path`, `normal_map`, ecc.).

---

## Come usare

Le texture **non richiedono alcun import**. Basta indicare il percorso relativo
nel materiale, risolto rispetto alla directory della scena (tipicamente `scenes/`):

```yaml
materials:
  - id: "pavimento_legno"
    type: "disney"
    texture_path: "libraries/textures/wood-floor.png"
    normal_map:   "libraries/textures/wood-floor-normal.png"
    roughness: 0.45
    metallic: 0.0

  - id: "muro_mattoni"
    type: "disney"
    texture_path: "libraries/textures/brick-wall.png"
    normal_map:   "libraries/textures/brick-wall-normal.png"
    roughness: 0.85
    metallic: 0.0
```

**Percorsi relativi:** i path sono risolti relativamente alla directory
del file di scena. Se la scena è in `scenes/`, usa
`"libraries/textures/nome-file.png"` (senza `scenes/`).

---

## Coppia Albedo + Normal Map

La maggior parte delle texture è disponibile in coppia:
un file **albedo** (colore diffuso) e un file **normal map** (`-normal.png`).
Usarle insieme aggiunge micro-dettaglio volumetrico senza costo geometrico.

| Albedo | Normal Map | Descrizione |
|--------|------------|-------------|
| `brick-wall.png` | `brick-wall-normal.png` | Muro di mattoni rossi, corsi regolari |
| `brick-wall-white.png` | `brick-wall-normal.png` | Muro di mattoni bianchi intonacati |
| `concrete.png` | `concrete-normal.png` | Calcestruzzo grezzo, superficie rugosa |
| `metal-scratched.png` | `metal-scratched-normal.png` | Metallo graffiato e consumato |
| `wood-floor.png` | `wood-floor-normal.png` | Parquet a doghe larghe, rovere naturale |
| `wood-planks.png` | `wood-planks-normal.png` | Assi di legno rustiche, nodi evidenti |

---

## Texture Solo Normal Map

Normal map generiche da abbinare a materiali di colore uniforme
o da usare come layer aggiuntivo:

| File | Descrizione |
|------|-------------|
| `fabric-weave-normal.png` | Tessitura intrecciata — per tessuti e tele |
| `metal-scratched-normal.png` | Graffi e rigature metalliche |
| `stone-cobble-normal.png` | Selciato in pietra, ciottoli irregolari |
| `tiles-normal.png` | Piastrelle con fughe a griglia regolare |

---

## Texture Speciali

| File | Tipo | Descrizione |
|------|------|-------------|
| `checkerboard.png` | Albedo | Scacchiera classica bianco/nero, per test UV |
| `grid-uv.png` | Albedo | Griglia UV numerata, per verifica mappatura |
| `flat-normal.png` | Normal | Normal map piatta (RGB 128,128,255) — disabilita effetto normal senza rimuovere il campo |
| `earth.png` | Albedo | Mappa diffusa della Terra — per globi e pianeti |
| `logo-3dray.png` | Albedo | Logo del motore — per scene dimostrative e label |
| `normal-mapping-normal-map.png` | Normal | Normal map demo ad alto contrasto per tutorial |

---

## Convenzioni tecniche

### Formato e risoluzione

Tutte le texture sono file **PNG a 8 bit per canale**. La risoluzione varia
da 256×256 (texture procedurali) fino a 2048×2048 (foto ad alta qualità).

### Normal Map — Spazio Tangente

Le normal map usano la convenzione **OpenGL (Y-up)**:
- Canale R → asse X (destra)
- Canale G → asse Y (su)
- Canale B → asse Z (verso la camera)

Il colore neutro è `RGB(128, 128, 255)` — equivalente a nessuna perturbazione.
Usa `flat-normal.png` per disabilitare l'effetto normal map in modo pulito
senza rimuovere il campo dalla definizione del materiale.

### Tiling

Le texture tileable si ripetono senza cuciture. Per controllare la frequenza
di ripetizione, usa il campo `texture_scale` (se supportato dal materiale)
oppure scala l'oggetto in modo inverso.

---

## Esempi pratici

### Pavimento in legno con normal map

```yaml
materials:
  - id: "parquet"
    type: "disney"
    texture_path: "libraries/textures/wood-floor.png"
    normal_map:   "libraries/textures/wood-floor-normal.png"
    roughness: 0.45
    metallic: 0.0
    clearcoat: 0.3
    clearcoat_roughness: 0.15
```

### Muro di mattoni + normal map

```yaml
materials:
  - id: "mattoni"
    type: "disney"
    texture_path: "libraries/textures/brick-wall.png"
    normal_map:   "libraries/textures/brick-wall-normal.png"
    roughness: 0.90
    metallic: 0.0
```

### Globo terrestre

```yaml
materials:
  - id: "globo_terra"
    type: "disney"
    texture_path: "libraries/textures/earth.png"
    roughness: 0.55
    metallic: 0.0

entities:
  - name: "terra"
    type: "sphere"
    center: [0, 1, 0]
    radius: 1.0
    material: "globo_terra"
```

### Normal map su materiale di colore solido

```yaml
materials:
  - id: "cemento_grezzo"
    type: "disney"
    color: [0.55, 0.52, 0.50]
    normal_map: "libraries/textures/concrete-normal.png"
    roughness: 0.92
    metallic: 0.0
```

### Grid UV — verifica della mappatura

```yaml
materials:
  - id: "debug_uv"
    type: "disney"
    texture_path: "libraries/textures/grid-uv.png"
    roughness: 1.0
    metallic: 0.0
```

---

## Catalogo completo

| File | Dimensione | Tipo | Note |
|------|-----------|------|------|
| `brick-wall.png` | ~26 KB | Albedo | Mattoni rossi |
| `brick-wall-white.png` | ~21 KB | Albedo | Mattoni bianchi |
| `brick-wall-normal.png` | ~94 KB | Normal | Per entrambi i brick-wall |
| `checkerboard.png` | ~2 KB | Albedo | Test UV |
| `concrete.png` | ~41 KB | Albedo | Calcestruzzo grezzo |
| `concrete-normal.png` | ~189 KB | Normal | |
| `earth.png` | ~106 KB | Albedo | Mappa diffusa Terra |
| `fabric-weave-normal.png` | ~127 KB | Normal | Tessitura tessuto |
| `flat-normal.png` | ~2 KB | Normal | Normal neutra (no effetto) |
| `grid-uv.png` | ~5 KB | Albedo | Griglia debug UV |
| `logo-3dray.png` | ~12 KB | Albedo | Logo motore |
| `metal-scratched.png` | ~50 KB | Albedo | Metallo graffiato |
| `metal-scratched-normal.png` | ~93 KB | Normal | |
| `normal-mapping-normal-map.png` | ~226 KB | Normal | Demo tutorial |
| `stone-cobble-normal.png` | ~278 KB | Normal | Selciato |
| `tiles-normal.png` | ~45 KB | Normal | Piastrelle |
| `wood-floor.png` | ~218 KB | Albedo | Parquet rovere |
| `wood-floor-normal.png` | ~71 KB | Normal | |
| `wood-planks.png` | ~245 KB | Albedo | Assi rustiche |
| `wood-planks-normal.png` | ~99 KB | Normal | |
