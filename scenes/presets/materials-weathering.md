# Invecchiamento e mix — preset materiali (copia-incolla)

Ruggine, vernice scrostata, sporco e patine, ottenuti combinando due materiali con
una maschera procedurale tramite il materiale `mix`. Tutti i blocchi sono pronti da
incollare nel `materials:` della tua scena. Per il flusso d'uso vedi
[`README.md`](README.md); schema completo in
[`../../docs/reference/scene-reference.md`](../../docs/reference/scene-reference.md).

> **Come funziona il `mix`.** Un materiale `type: "mix"` fonde `material_a` e
> `material_b` (referenziati per `id`, devono esistere nella stessa scena) secondo
> una `mask` texture: dove la maschera è 0 vince A, dove è 1 vince B. Il campo
> `blend` è la quota di fondo quando non c'è maschera. I materiali referenziati
> possono a loro volta essere dei `mix` (mix-of-mix). Incolla **i tre blocchi
> insieme** (base, overlay, mix).

---

## Schema del mix

```yaml
materials:
  - id: "mix_id"
    type: "mix"
    material_a: "base_pulita"     # id di un materiale definito altrove
    material_b: "overlay_patina"  # id di un materiale definito altrove
    blend: 0.5                     # quota di B quando la mask è assente
    mask:
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
    type: "disney"
    color: [0.56, 0.57, 0.58]
    metallic: 1.0
    roughness: 0.25

  - id: "ruggine"
    type: "disney"
    color: [0.42, 0.20, 0.10]
    texture:
      type: "noise"
      scale: 12.0
      octaves: 5
      noise_strength: 0.4
      color_ramp:
        - { position: 0.0, color: [0.30, 0.13, 0.06] }
        - { position: 1.0, color: [0.55, 0.28, 0.14] }
    metallic: 0.0
    roughness: 0.92

  - id: "acciaio_arrugginito"
    type: "mix"
    material_a: "acciaio_pulito"
    material_b: "ruggine"
    blend: 0.4
    mask:
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
    type: "disney"
    color: [0.12, 0.28, 0.50]
    roughness: 0.35
    clearcoat: 0.4
    coat_roughness: 0.2

  - id: "metallo_nudo"
    type: "disney"
    color: [0.45, 0.44, 0.43]
    metallic: 1.0
    roughness: 0.55

  - id: "vernice_scrostata"
    type: "mix"
    material_a: "vernice_blu"
    material_b: "metallo_nudo"
    blend: 0.3
    mask:
      type: "voronoi"
      scale: 6.0
      noise_strength: 0.7
```

Maschera `voronoi` = chiazze nette di vernice saltata sul metallo sottostante.

# Sezione C — Patina e sporco su pietra

```yaml
materials:
  - id: "pietra_pulita"
    type: "disney"
    color: [0.62, 0.60, 0.55]
    texture:
      type: "noise"
      scale: 10.0
      octaves: 5
      noise_strength: 0.2
    roughness: 0.8

  - id: "muschio"
    type: "disney"
    color: [0.16, 0.26, 0.10]
    roughness: 0.95
    specular: 0.1

  - id: "pietra_muschiata"
    type: "mix"
    material_a: "pietra_pulita"
    material_b: "muschio"
    blend: 0.35
    mask:
      type: "noise"
      scale: 2.5
      noise_type: "fbm"
      octaves: 6
```

# Sezione D — Patina su rame (verderame)

```yaml
materials:
  - id: "rame_lucido"
    type: "disney"
    color: [0.72, 0.45, 0.20]
    metallic: 1.0
    roughness: 0.2

  - id: "verderame"
    type: "disney"
    color: [0.28, 0.55, 0.50]
    roughness: 0.85
    metallic: 0.0

  - id: "rame_patinato"
    type: "mix"
    material_a: "rame_lucido"
    material_b: "verderame"
    blend: 0.5
    mask:
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
