# Invecchiamento e mix — preset materiali (copia-incolla)

Ruggine, vernice scrostata, sporco e patine, ottenuti combinando due materiali con
una maschera procedurale tramite il materiale `mix`. Tutti i blocchi sono pronti da
incollare nel `materials:` della tua scena. Per il flusso d'uso vedi
[`README.md`](README.md); schema completo in
[`../../docs/reference/scene-reference.md`](../../docs/reference/scene-reference.md).

> **Come funziona il `mix`.** Un materiale `type: "mix"` fonde `material_a` e
> `material_b` (referenziati per `id`, devono esistere nella stessa scena)
> secondo `blend`: un numero (la quota di B, default 0.5) oppure una texture
> usata come maschera (dove vale 0 vince A, dove vale 1 vince B). I materiali
> referenziati possono a loro volta essere dei `mix` (mix-of-mix). Incolla **i
> tre blocchi insieme** (base, overlay, mix).

---

## Schema del mix

```yaml
materials:
  - id: "mix_id"
    type: "mix"
    material_a: "base_pulita"     # id di un materiale definito altrove
    material_b: "overlay_patina"  # id di un materiale definito altrove
    blend:
      type: "noise"               # noise | voronoi
      scale: 3.5
      noise_type: "fbm"
      octaves: 5
```

---

# Sezione A — Ruggine su metallo

Tre blocchi: acciaio pulito, ruggine, e il mix mascherato.

```yaml
materials:
  - id: "acciaio_pulito"
    type: openpbr
    base_color: [0.56, 0.57, 0.58]
    base_metalness: 1.0
    specular_roughness: 0.25

  - id: "ruggine"
    type: openpbr
    base_color:
      type: "noise"
      scale: 12.0
      octaves: 5
      noise_strength: 0.4
      color_ramp:
        - { position: 0.0, color: [0.30, 0.13, 0.06] }
        - { position: 1.0, color: [0.55, 0.28, 0.14] }
    specular_roughness: 0.92

  - id: "acciaio_arrugginito"
    type: "mix"
    material_a: "acciaio_pulito"
    material_b: "ruggine"
    blend:
      type: "noise"
      scale: 3.5
      noise_type: "fbm"
      octaves: 5
```

La maschera `fbm` decide dove affiora la ruggine. Alza `blend`/`scale` per
superfici più corrose.

# Sezione B — Vernice scrostata

```yaml
materials:
  - id: "vernice_blu"
    type: openpbr
    base_color: [0.12, 0.28, 0.50]
    specular_roughness: 0.35
    coat_weight: 0.4
    coat_roughness: 0.2
    coat_ior: 1.5

  - id: "metallo_nudo"
    type: openpbr
    base_color: [0.45, 0.44, 0.43]
    base_metalness: 1.0
    specular_roughness: 0.55

  - id: "vernice_scrostata"
    type: "mix"
    material_a: "vernice_blu"
    material_b: "metallo_nudo"
    blend:
      type: "voronoi"
      scale: 6.0
      # noise_strength: 0.7
```

Maschera `voronoi` = chiazze nette di vernice saltata sul metallo sottostante.

# Sezione C — Patina e sporco su pietra

```yaml
materials:
  - id: "pietra_pulita"
    type: openpbr
    base_color:
      type: "noise"
      scale: 10.0
      octaves: 5
      noise_strength: 0.2
    specular_roughness: 0.8

  - id: "muschio"
    type: openpbr
    base_color: [0.16, 0.26, 0.10]
    specular_roughness: 0.95
    specular_weight: 0.2

  - id: "pietra_muschiata"
    type: "mix"
    material_a: "pietra_pulita"
    material_b: "muschio"
    blend:
      type: "noise"
      scale: 2.5
      noise_type: "fbm"
      octaves: 6
```

# Sezione D — Patina su rame (verderame)

```yaml
materials:
  - id: "rame_lucido"
    type: openpbr
    base_color: [0.72, 0.45, 0.20]
    base_metalness: 1.0
    specular_roughness: 0.2

  - id: "verderame"
    type: openpbr
    base_color: [0.28, 0.55, 0.50]
    specular_roughness: 0.85

  - id: "rame_patinato"
    type: "mix"
    material_a: "rame_lucido"
    material_b: "verderame"
    blend:
      type: "noise"
      scale: 4.0
      noise_type: "fbm"
      octaves: 5
```

---

## Matrice decisionale

| Effetto | Mix preset | Maschera |
|---------|-----------|----------|
| Acciaio corroso | `acciaio_arrugginito` | noise fbm |
| Vernice saltata su metallo | `vernice_scrostata` | voronoi a chiazze |
| Pietra/muro invecchiato | `pietra_muschiata` | noise fbm grande |
| Rame/bronzo ossidato | `rame_patinato` | noise fbm |

Combina con i preset base di [`materials-metal.md`](materials-metal.md) e
[`materials-stone.md`](materials-stone.md): basta referenziarne gli `id` come
`material_a`/`material_b`.

## CLI tips

```bash
# I mix metallo/ruggine hanno aree lucide e opache: clamp moderato dei fireflies
dotnet run --project src/RayTracer -- -i scena.yaml -C 50
```
