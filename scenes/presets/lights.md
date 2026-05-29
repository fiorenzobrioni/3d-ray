# Luci — preset (copia-incolla)

Set di illuminazione pronti: studio (3-point, high-key, drammatico, prodotto),
esterni (golden hour, mezzogiorno, coperto), notte/luna, neon. Tutti i blocchi sono
pronti da incollare nel `lights:` della tua scena. Per il flusso d'uso vedi
[`README.md`](README.md); schema completo in
[`../../docs/reference/scene-reference.md`](../../docs/reference/scene-reference.md).

> **Le luci `area` richiedono `corner`, `u`, `v`** (gli spigoli del rettangolo),
> non `position`/`width`/`height`: il motore altrimenti le salta con un warning.
> Qualsiasi geometria con materiale `Emissive` diventa automaticamente una luce
> (vedi Sezione G).

---

## Schema rilevante

```yaml
lights:
  - type: "area"               # rettangolo: key/fill/soft
    corner: [-4, 4, -2]
    u: [2, 0, 0]
    v: [0, 0, 2]
    color: [1.0, 0.96, 0.90]
    intensity: 45.0
    shadow_samples: 9

  - type: "point"              # sorgente puntiforme
    position: [3, 4, 2]
    color: [1, 1, 1]
    intensity: 30.0

  - type: "spot"
    position: [0, 6, 0]
    direction: [0, -1, 0]
    cone_angle_deg: 30
    penumbra_deg: 8
    color: [1, 0.95, 0.85]
    intensity: 50.0

  - type: "directional"        # sole/luna
    direction: [0.3, -0.8, 0.4]
    color: [1.0, 0.95, 0.85]
    intensity: 6.0
    angular_radius_deg: 0.53   # disco solare → penombre morbide
```

---

# Sezione A — Studio 3-point

Abbinato a: `world.md` B1/B2 (ciclorama). Render: `-s 256 -d 6`.

```yaml
lights:
  - type: "area"   # KEY
    corner: [-4.0, 4.0, -2.0]
    u: [2.0, 0.0, 0.0]
    v: [0.0, 0.0, 2.0]
    color: [1.0, 0.96, 0.90]
    intensity: 45.0
    shadow_samples: 9
  - type: "area"   # FILL
    corner: [3.0, 3.0, -1.5]
    u: [1.2, 0.0, 0.0]
    v: [0.0, 0.0, 1.2]
    color: [0.72, 0.82, 1.0]
    intensity: 15.0
    shadow_samples: 9
  - type: "area"   # RIM
    corner: [-1.0, 3.5, 4.0]
    u: [2.0, 0.0, 0.0]
    v: [0.0, 1.0, 0.0]
    color: [1.0, 1.0, 1.0]
    intensity: 30.0
    shadow_samples: 9
```

# Sezione B — High-key (chiave alta)

Abbinato a: `world.md` B1 (ciclorama bianco). Sfondo e soggetto luminosi, ombre
quasi assenti.

```yaml
lights:
  - type: "area"
    corner: [-5.0, 5.0, -3.0]
    u: [4.0, 0.0, 0.0]
    v: [0.0, 0.0, 4.0]
    color: [1.0, 1.0, 1.0]
    intensity: 60.0
    shadow_samples: 12
  - type: "area"
    corner: [-5.0, 1.0, 4.0]
    u: [10.0, 0.0, 0.0]
    v: [0.0, 4.0, 0.0]
    color: [1.0, 1.0, 1.0]
    intensity: 25.0
    shadow_samples: 9
```

# Sezione C — Drammatico (low-key)

Abbinato a: `world.md` B2 (studio nero). Una sola key dura, resto in ombra.

```yaml
lights:
  - type: "spot"
    position: [-3.0, 6.0, 2.0]
    direction: [0.4, -1.0, -0.3]
    cone_angle_deg: 24
    penumbra_deg: 6
    color: [1.0, 0.97, 0.92]
    intensity: 80.0
    shadow_samples: 12
```

# Sezione D — Prodotto (product)

Abbinato a: `world.md` B4. Due soft box laterali + rim per il contorno.

```yaml
lights:
  - type: "area"
    corner: [-4.0, 3.0, 1.0]
    u: [1.5, 0.0, 0.0]
    v: [0.0, 2.0, 0.0]
    color: [1.0, 1.0, 1.0]
    intensity: 40.0
    shadow_samples: 12
  - type: "area"
    corner: [2.5, 3.0, 1.0]
    u: [1.5, 0.0, 0.0]
    v: [0.0, 2.0, 0.0]
    color: [1.0, 1.0, 1.0]
    intensity: 40.0
    shadow_samples: 12
  - type: "area"
    corner: [-1.0, 4.0, 4.0]
    u: [2.0, 0.0, 0.0]
    v: [0.0, 1.0, 0.0]
    color: [0.9, 0.95, 1.0]
    intensity: 25.0
    shadow_samples: 9
```

# Sezione E — Esterni

## E1. Golden hour

Abbinato a: `world.md` A2/A8. Sole basso e caldo.

```yaml
lights:
  - type: "directional"
    direction: [0.85, -0.25, 0.45]
    color: [1.0, 0.72, 0.42]
    intensity: 5.0
    angular_radius_deg: 0.53
```

## E2. Mezzogiorno

Abbinato a: `world.md` A1. Sole alto neutro.

```yaml
lights:
  - type: "directional"
    direction: [0.2, -0.95, 0.1]
    color: [1.0, 0.98, 0.92]
    intensity: 8.0
    angular_radius_deg: 0.53
```

## E3. Coperto (overcast)

Abbinato a: `world.md` A5. Luce diffusa dall'alto, ombre molli.

```yaml
lights:
  - type: "area"
    corner: [-15.0, 18.0, -15.0]
    u: [30.0, 0.0, 0.0]
    v: [0.0, 0.0, 30.0]
    color: [0.92, 0.94, 1.0]
    intensity: 4.0
    shadow_samples: 16
```

# Sezione F — Notte e neon

## F1. Luce di luna

Abbinato a: `world.md` A6. Direzionale fredda fioca.

```yaml
lights:
  - type: "directional"
    direction: [0.4, -0.7, 0.3]
    color: [0.55, 0.65, 0.95]
    intensity: 0.8
    angular_radius_deg: 0.5
```

## F2. Neon / cyberpunk

Abbinato a: `world.md` B4. Tubi colorati come geometrie emissive (vedi Sezione G)
o due aree sature contrapposte.

```yaml
lights:
  - type: "area"
    corner: [-4.0, 1.0, -2.0]
    u: [0.1, 3.0, 0.0]
    v: [0.0, 0.0, 4.0]
    color: [1.0, 0.1, 0.55]
    intensity: 35.0
    shadow_samples: 9
  - type: "area"
    corner: [4.0, 1.0, -2.0]
    u: [0.1, 3.0, 0.0]
    v: [0.0, 0.0, 4.0]
    color: [0.1, 0.7, 1.0]
    intensity: 35.0
    shadow_samples: 9
```

# Sezione G — Luci geometriche (emissive)

Una geometria con materiale `emissive` emette luce ed entra automaticamente nel
campionamento diretto (NEE). Utile per pannelli, tubi neon, lampade visibili.

```yaml
materials:
  - id: "emi_pannello"
    type: "emissive"
    color: [1.0, 0.95, 0.85]
    intensity: 12.0

entities:
  - type: "quad"
    corner: [-2, 5, -2]
    u: [4, 0, 0]
    v: [0, 0, 4]
    material: "emi_pannello"
```

---

## Matrice decisionale

| Look | Sezione | World abbinato | Render |
|------|---------|----------------|--------|
| Ritratto/still life neutro | A 3-point | B1/B2 | `-s 256 -d 6` |
| Catalogo luminoso | B high-key | B1 | `-s 256` |
| Cinematografico/teatrale | C drammatico | B2 | `-S 12` |
| Packshot prodotto | D product | B4 | `-s 256 -S 12` |
| Paesaggio diurno | E2 mezzogiorno | A1 | `-S 4` |
| Tramonto/alba | E1 golden hour | A2/A8 | `-S 4` |
| Cielo coperto | E3 overcast | A5 | `-S 16` |
| Notte lunare | F1 luna | A6 | `-d 8` |
| Insegne/neon | F2 / G emissive | B4 | `-C 25` |

## CLI tips

```bash
# Penombre morbide (area/sole con disco): alza i campioni d'ombra
dotnet run --project src/RayTracer -- -i scena.yaml -S 12

# Neon/emissive molto intense: clampa i fireflies
dotnet run --project src/RayTracer -- -i scena.yaml -C 25
```
