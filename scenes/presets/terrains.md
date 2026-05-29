# Terreni heightfield — preset (copia-incolla)

Ricetta per terreni da heightmap: una mappa di altezza in scala di grigi viene
intersecata direttamente dal motore come primitiva `heightfield` (niente mesh),
con materiali assegnati a fasce di **altitudine** e **pendenza** (`strata`). Per il
flusso d'uso vedi [`README.md`](README.md); schema completo in
[`../../docs/reference/scene-reference.md`](../../docs/reference/scene-reference.md)
e dettagli tecnici in [`../../docs/technical/heightfield.md`](../../docs/technical/heightfield.md).

> **Heightmap come asset.** La mappa di altezza è un PNG (preferibilmente 16-bit
> grayscale) sotto [`../assets/heightmaps/`](../assets/heightmaps/); il path nel
> blocco è relativo alla scena. Genera nuove heightmap con `TerrainGen`. Il
> terreno si può usare come `world.ground` (terreno globale) **oppure** come entità
> `heightfield` posizionabile.

---

## Schema rilevante

```yaml
# campi del heightfield (validi sia in world.ground che nell'entità):
bounds:        [-50, -50, 50, 50]   # [xMin, zMin, xMax, zMax] estensione mondo
height_scale:  25.0                 # altezza massima (valore bianco = 1.0)
heightmap_path: "assets/heightmaps/heightfield-strata-test-height.png"
resolution:    256                  # campionamento della mappa
sea_level:     7.5                  # quota del mare (assoluta)
sea_material:  "water"
strata:                             # materiali per fascia altitudine/pendenza
  - { min_altitude: 0.0, max_altitude: 0.36, min_slope_deg: 0,  max_slope_deg: 35, blend_width: 0.04, material: "sand" }
  - { min_altitude: 0.5, max_altitude: 1.0,  min_slope_deg: 0,  max_slope_deg: 90, blend_width: 0.08, material: "rock" }
material: "ground"                  # fallback fuori dalle fasce
```

`min_altitude`/`max_altitude` sono normalizzati 0–1 sull'altezza; `blend_width`
ammorbidisce il bordo tra fasce; le fasce successive vincono in caso di overlap.

---

# Sezione A — Terreno globale (world.ground)

Incolla i materiali e il blocco `world.ground`: il terreno diventa il suolo della
scena.

```yaml
materials:
  - id: "t_sand"
    type: "disney"
    texture: { type: "noise", scale: 5.0, noise_strength: 0.6,
               colors: [[0.82, 0.72, 0.48], [0.62, 0.50, 0.30]], randomize_offset: true }
    roughness: 0.9
  - id: "t_ground"
    type: "disney"
    texture: { type: "noise", scale: 4.0, noise_strength: 0.5,
               colors: [[0.42, 0.50, 0.20], [0.30, 0.36, 0.14]], randomize_offset: true }
    roughness: 0.92
  - id: "t_rock"
    type: "disney"
    texture: { type: "noise", scale: 6.0, noise_strength: 0.7,
               colors: [[0.32, 0.30, 0.28], [0.18, 0.17, 0.18]], randomize_offset: true }
    roughness: 0.85
  - id: "t_snow"
    type: "disney"
    color: [0.95, 0.97, 1.0]
    roughness: 0.78
  - id: "t_water"
    type: "disney"
    color: [0.85, 0.92, 0.95]
    roughness: 0.08
    specular: 0.5
    spec_trans: 1.0
    ior: 1.33
    transmission_color: [0.30, 0.55, 0.65]
    transmission_depth: 4.0

world:
  sky: { type: "preetham", turbidity: 4.0, sun: { direction: [0.3, 0.8, 0.4], angular_radius: 0.265, shadow_samples: 4 } }
  ground:
    type: "heightfield"          # alias: terrain
    bounds: [-50, -50, 50, 50]
    height_scale: 25.0
    heightmap_path: "assets/heightmaps/heightfield-strata-test-height.png"
    resolution: 256
    sea_level: 7.5
    sea_material: "t_water"
    material: "t_ground"
    strata:
      - { min_altitude: 0.00, max_altitude: 0.36, min_slope_deg: 0, max_slope_deg: 35, blend_width: 0.04, material: "t_sand" }
      - { min_altitude: 0.34, max_altitude: 0.75, min_slope_deg: 0, max_slope_deg: 45, blend_width: 0.06, material: "t_ground" }
      - { min_altitude: 0.50, max_altitude: 1.00, min_slope_deg: 0, max_slope_deg: 90, blend_width: 0.08, material: "t_rock" }
      - { min_altitude: 0.85, max_altitude: 1.00, min_slope_deg: 0, max_slope_deg: 60, blend_width: 0.05, material: "t_snow" }
```

Catena montuosa con spiaggia, prato, roccia e neve in quota, più un piano d'acqua a
`sea_level`. È lo stesso impianto del preset alpino A3 di [`world.md`](world.md).

# Sezione B — Heightfield come entità posizionabile

Quando il terreno non è il suolo globale ma un rilievo da collocare nella scena
(isola, collina), usalo come entità (riusa i materiali `t_*` della Sezione A):

```yaml
entities:
  - type: "heightfield"
    bounds: [-20, -20, 20, 20]
    height_scale: 12.0
    heightmap_path: "assets/heightmaps/heightfield-strata-test-height.png"
    resolution: 256
    material: "t_rock"
    strata:
      - { min_altitude: 0.0, max_altitude: 0.6, min_slope_deg: 0, max_slope_deg: 50, blend_width: 0.06, material: "t_ground" }
      - { min_altitude: 0.6, max_altitude: 1.0, min_slope_deg: 0, max_slope_deg: 90, blend_width: 0.08, material: "t_rock" }
    transform:
      translate: [0, 0, -30]
```

---

## Matrice decisionale

| Caso d'uso | Sezione | Note |
|------------|---------|------|
| Paesaggio/suolo dell'intera scena | A (`world.ground`) | strata altitudine+pendenza |
| Rilievo/isola/collina collocata | B (entità `heightfield`) | con `transform.translate` |
| Mare/lago integrato | A o B + `sea_level`/`sea_material` | acqua trasmissiva |

## CLI tips

```bash
# I bordi tra fasce strata e le penombre vogliono profondità e ombre adeguate
dotnet run --project src/RayTracer -- -i scena.yaml -d 8 -S 4

# Genera una nuova heightmap (output in scenes/assets/heightmaps/)
dotnet run --project src/Tools/TerrainGen/TerrainGen.csproj -- --name mia-valle --type collina --with-cameras
```
