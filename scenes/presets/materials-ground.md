# Terreno — preset materiali (copia-incolla)

Materiali per il suolo: erba, sabbia, terra, roccia/ghiaia, neve, sottobosco,
asfalto, fango. Si abbinano alle scene di [`world.md`](world.md) (blocco
`world.ground`) e agli strati di [`terrains.md`](terrains.md). Per il flusso d'uso
vedi [`README.md`](README.md); schema completo in
[`../../docs/reference/scene-reference.md`](../../docs/reference/scene-reference.md).

> **Tutti opachi diffusi.** Niente `base_metalness`, rugosità alta, nessun
> subsurface (salvo la neve). La variazione viene dalla texture procedurale `noise` e
> dalle `color_ramp`. Per i piani grandi alza `uv_scale` per ripetere il
> dettaglio.

---

## Schema rilevante

```yaml
materials:
  - id: "ground_id"
    type: openpbr
    base_color:
      type: "noise"
      scale: 12.0
      octaves: 5
      noise_strength: 0.25
      color_ramp:
        - { position: 0.0, color: [0.30, 0.25, 0.16] }
        - { position: 1.0, color: [0.52, 0.45, 0.30] }
    specular_roughness: 0.9
    specular_weight: 0.3
```

---

# Sezione A — Vegetale

## A1. Erba prato

```yaml
materials:
  - id: "erba_prato"
    type: openpbr
    base_color:
      type: "noise"
      scale: 24.0
      octaves: 5
      noise_strength: 0.3
      color_ramp:
        - { position: 0.0, color: [0.12, 0.26, 0.06] }
        - { position: 0.6, color: [0.22, 0.42, 0.10] }
        - { position: 1.0, color: [0.40, 0.55, 0.18] }
    specular_roughness: 0.95
    specular_weight: 0.2
```

## A2. Sottobosco / terreno forestale

```yaml
materials:
  - id: "sottobosco"
    type: openpbr
    base_color:
      type: "noise"
      scale: 16.0
      octaves: 6
      noise_strength: 0.4
      color_ramp:
        - { position: 0.0, color: [0.10, 0.07, 0.04] }
        - { position: 0.5, color: [0.24, 0.16, 0.08] }
        - { position: 1.0, color: [0.36, 0.28, 0.14] }
    specular_roughness: 0.95
    specular_weight: 0.2
```

# Sezione B — Minerale

## B1. Sabbia desertica

```yaml
materials:
  - id: "sabbia_desertica"
    type: openpbr
    base_color:
      type: "noise"
      scale: 30.0
      octaves: 4
      noise_strength: 0.15
      color_ramp:
        - { position: 0.0, color: [0.62, 0.50, 0.32] }
        - { position: 1.0, color: [0.82, 0.70, 0.48] }
    specular_roughness: 0.92
    specular_weight: 0.3
```

## B2. Terra secca / argillosa

```yaml
materials:
  - id: "terra_secca"
    type: openpbr
    base_color:
      type: "voronoi"
      scale: 8.0
      # noise_strength: 0.5
      color_ramp:
        - { position: 0.0, color: [0.34, 0.22, 0.13] }
        - { position: 1.0, color: [0.58, 0.42, 0.27] }
    specular_roughness: 0.95
    specular_weight: 0.2
```

Pattern `voronoi` per il fango screpolato. Adatto a deserti, terreni aridi.

## B3. Roccia scura

```yaml
materials:
  - id: "rock_dark"
    type: openpbr
    base_color:
      type: "noise"
      scale: 10.0
      octaves: 6
      noise_strength: 0.45
      color_ramp:
        - { position: 0.0, color: [0.10, 0.10, 0.11] }
        - { position: 1.0, color: [0.34, 0.33, 0.32] }
    specular_roughness: 0.85
    specular_weight: 0.4
```

## B4. Ghiaia

```yaml
materials:
  - id: "ghiaia"
    type: openpbr
    base_color:
      type: "voronoi"
      scale: 30.0
      # noise_strength: 0.6
      color_ramp:
        - { position: 0.0, color: [0.28, 0.27, 0.25] }
        - { position: 1.0, color: [0.62, 0.60, 0.56] }
    specular_roughness: 0.9
    specular_weight: 0.4
```

# Sezione C — Neve e asfalto

## C1. Neve fresca

```yaml
materials:
  - id: "neve_fresca"
    type: openpbr
    base_color:
      type: "noise"
      scale: 40.0
      octaves: 3
      noise_strength: 0.05
    specular_roughness: 0.4
    specular_weight: 0.8
    subsurface_weight: 0.04
    subsurface_radius_scale: [1.0, 1.0, 1.1]
    subsurface_color: [0.95, 0.96, 0.98]
```

Neve: forte diffusione sottosuperficiale fredda. `subsurface_weight` basso
per non renderla vetrosa.

## C2. Asfalto

```yaml
materials:
  - id: "asfalto"
    type: openpbr
    base_color:
      type: "noise"
      scale: 50.0
      octaves: 4
      noise_strength: 0.12
    specular_roughness: 0.75
    specular_weight: 0.5
```

---

## Matrice decisionale

| Caso d'uso | Preset | Si abbina a (world.md) |
|------------|--------|------------------------|
| Prato/parco | `erba_prato` | A5 Overcast meadow |
| Bosco | `sottobosco` | A8 Autumn forest |
| Deserto/spiaggia | `sabbia_desertica` | A1 Desert noon |
| Terreno arido | `terra_secca` | A1/A2 Desert |
| Montagna/rocce | `rock_dark`, `ghiaia` | A3 Alpine |
| Paesaggio innevato | `neve_fresca` | A9 Polar snow |
| Strada/urbano | `asfalto` | studio/urbano |

## CLI tips

```bash
# Piani di terreno molto estesi: aumenta uv_scale nel blocco ground per ripetere il dettaglio
# Neve (SSS): più profondità e campioni
dotnet run --project src/RayTracer -- -i scena.yaml -d 8 -s 128
```
