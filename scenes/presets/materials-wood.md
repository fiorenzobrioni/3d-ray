# Legno — preset materiali (copia-incolla)

Rovere, noce, mogano, teak, pino, frassino, ebano e bambù, nelle finiture grezzo,
oliato/cerato, laccato e verniciato. Tutti i blocchi sono pronti da incollare nel
`materials:` della tua scena. Per il flusso d'uso vedi [`README.md`](README.md);
schema completo in [`../../docs/reference/scene-reference.md`](../../docs/reference/scene-reference.md).

> **Asse finitura.** Il legno è un dielettrico opaco: niente `base_metalness`
> né subsurface. La finitura si gioca su `specular_roughness` e sul coat:
> grezzo = rugosità alta senza coat; oliato/cerato = rugosità media + coat
> leggero; laccato/verniciato = coat forte e liscio.

---

## Schema rilevante per il legno

```yaml
materials:
  - id: "wood_id"
    type: openpbr
    base_color:
      type: "wood"            # procedurale: anelli, fiammatura, pori
      scale: 4.2
      noise_strength: 1.6
      ring_sharpness: 3.5
      latewood_width: 0.20
      figure_strength: 0.40   # fiammatura/marezzatura
      pore_density: 0.30
      pore_strength: 0.40
      octaves: 5
      randomize_offset: true
      randomize_rotation: true
      color_ramp:             # dal cuore scuro all'alburno chiaro
        - { position: 0.00, color: [0.12, 0.06, 0.03] }
        - { position: 1.00, color: [0.66, 0.44, 0.24] }
    specular_roughness: 0.55           # grezzo 0.6-0.85 · oliato 0.45-0.6 · laccato ≤ 0.15
    specular_weight: 0.8
    coat_weight: 0.22           # 0 grezzo · 0.2-0.4 oliato · ≥ 0.8 laccato
    coat_roughness: 0.32
    coat_ior: 1.5
```

---

# Sezione A — Rovere

## A1. Rovere naturale oliato

```yaml
materials:
  - id: "rovere_oliato"
    type: openpbr
    base_color:
      type: "wood"
      scale: 4.5
      noise_strength: 1.5
      ring_sharpness: 3.0
      latewood_width: 0.22
      figure_strength: 0.30
      pore_density: 0.35
      pore_strength: 0.45
      octaves: 5
      randomize_offset: true
      randomize_rotation: true
      color_ramp:
        - { position: 0.00, color: [0.32, 0.22, 0.12] }
        - { position: 0.55, color: [0.55, 0.40, 0.24] }
        - { position: 1.00, color: [0.74, 0.58, 0.38] }
    specular_roughness: 0.52
    specular_weight: 0.76
    coat_weight: 0.25
    coat_roughness: 0.30
    coat_ior: 1.5
```

Rovere chiaro con pori marcati e finitura a olio satinata. Parquet, tavoli, arredo.

## A2. Rovere laccato lucido

```yaml
materials:
  - id: "rovere_laccato"
    type: openpbr
    base_color:
      type: "wood"
      scale: 4.5
      noise_strength: 1.4
      ring_sharpness: 3.0
      latewood_width: 0.22
      pore_density: 0.30
      pore_strength: 0.30
      octaves: 5
      randomize_offset: true
      color_ramp:
        - { position: 0.00, color: [0.30, 0.20, 0.11] }
        - { position: 1.00, color: [0.70, 0.54, 0.34] }
    specular_roughness: 0.18
    coat_weight: 0.9
    coat_roughness: 0.05
    coat_ior: 1.5
```

# Sezione B — Noce e mogano

## B1. Noce cerato (caldo, satinato)

```yaml
materials:
  - id: "noce_cerato"
    type: openpbr
    base_color:
      type: "wood"
      scale: 4.2
      noise_strength: 1.6
      ring_sharpness: 3.5
      latewood_width: 0.20
      figure_strength: 0.40
      figure_scale: 0.18
      pore_density: 0.30
      pore_strength: 0.40
      octaves: 5
      randomize_offset: true
      randomize_rotation: true
      color_ramp:
        - { position: 0.00, color: [0.12, 0.06, 0.03] }
        - { position: 0.55, color: [0.40, 0.24, 0.13] }
        - { position: 1.00, color: [0.66, 0.44, 0.24] }
    specular_roughness: 0.55
    specular_weight: 0.8
    coat_weight: 0.22
    coat_roughness: 0.32
    coat_ior: 1.5
```

Noce scuro caldo con fiammatura: arredo classico, strumenti, finiture pregiate.

## B2. Mogano laccato

```yaml
materials:
  - id: "mogano_laccato"
    type: openpbr
    base_color:
      type: "wood"
      scale: 3.8
      noise_strength: 1.3
      ring_sharpness: 2.5
      figure_strength: 0.55
      figure_scale: 0.16
      pore_density: 0.25
      octaves: 5
      randomize_offset: true
      color_ramp:
        - { position: 0.00, color: [0.24, 0.07, 0.05] }
        - { position: 1.00, color: [0.55, 0.20, 0.12] }
    specular_roughness: 0.14
    coat_weight: 0.92
    coat_roughness: 0.04
    coat_ior: 1.5
```

Rosso-bruno profondo a specchio: pianoforti, cruscotti, mobili lucidati.

# Sezione C — Legni chiari ed esotici

## C1. Pino grezzo

```yaml
materials:
  - id: "pino_grezzo"
    type: openpbr
    base_color:
      type: "wood"
      scale: 5.5
      noise_strength: 1.8
      ring_sharpness: 4.0
      latewood_width: 0.30
      pore_density: 0.15
      octaves: 5
      randomize_offset: true
      color_ramp:
        - { position: 0.00, color: [0.52, 0.38, 0.20] }
        - { position: 1.00, color: [0.82, 0.68, 0.44] }
    specular_roughness: 0.78
    specular_weight: 0.5
```

## C2. Teak da esterni

```yaml
materials:
  - id: "teak_esterni"
    type: openpbr
    base_color:
      type: "wood"
      scale: 4.8
      noise_strength: 1.5
      ring_sharpness: 3.0
      pore_density: 0.28
      pore_strength: 0.4
      octaves: 5
      randomize_offset: true
      color_ramp:
        - { position: 0.00, color: [0.40, 0.27, 0.14] }
        - { position: 1.00, color: [0.68, 0.50, 0.30] }
    specular_roughness: 0.62
    specular_weight: 0.6
    coat_weight: 0.1
    coat_roughness: 0.031622775
    coat_ior: 1.5
```

## C3. Ebano lucido

```yaml
materials:
  - id: "ebano_lucido"
    type: openpbr
    base_color:
      type: "wood"
      scale: 4.0
      noise_strength: 0.8
      ring_sharpness: 2.0
      pore_density: 0.2
      octaves: 4
      randomize_offset: true
    specular_roughness: 0.12
    coat_weight: 0.85
    coat_roughness: 0.05
    coat_ior: 1.5
```

# Sezione D — Bambù

## D1. Bambù chiaro

```yaml
materials:
  - id: "bambu_chiaro"
    type: openpbr
    base_color:
      type: "wood"
      scale: 7.0
      noise_strength: 1.2
      ring_sharpness: 5.0
      latewood_width: 0.10
      pore_density: 0.1
      octaves: 4
      randomize_offset: true
      color_ramp:
        - { position: 0.00, color: [0.78, 0.66, 0.40] }
        - { position: 1.00, color: [0.90, 0.82, 0.58] }
    specular_roughness: 0.35
    specular_weight: 0.8
    coat_weight: 0.4
    coat_roughness: 0.15
    coat_ior: 1.5
```

---

## Matrice decisionale

| Caso d'uso | Preset | Note chiave |
|------------|--------|-------------|
| Parquet/tavolo naturale | `rovere_oliato`, `noce_cerato` | coat leggero, pori marcati |
| Mobile/superficie a specchio | `rovere_laccato`, `mogano_laccato` | clearcoat ≥ 0.9 |
| Strumenti/finiture pregiate | `noce_cerato`, `ebano_lucido` | fiammatura, coat |
| Travi/rustico/casse | `pino_grezzo` | roughness alta, no coat |
| Arredo esterno | `teak_esterni` | coat minimo |
| Pavimento/pannello moderno | `bambu_chiaro` | grana fitta regolare |

## CLI tips

```bash
# Legno laccato (coat lucido): clampa eventuali fireflies dei riflessi
dotnet run --project src/RayTracer -- -i scena.yaml -C 50
```
