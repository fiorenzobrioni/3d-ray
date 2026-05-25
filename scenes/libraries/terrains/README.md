# Libreria Terreni — 3D-Ray

Template di terreni per la primitiva `heightfield` del motore: ogni file
YAML è un terrain template importabile, affiancato da un heightmap PNG-16
a 16 bit che descrive il profilo altimetrico. I file sono generati dallo
strumento `TerrainGen` incluso nel repository.

## Come usare

I terreni si importano con `imports:` e si istanziano come template.

```yaml
imports:
  - path: "libraries/terrains/heightfield-strata-test.yaml"

entities:
  - type: "instance"
    template: "heightfield_strata_test"
    translate: [0, 0, 0]
```

In alternativa, puoi usare il terrain come ground dispatcher direttamente
nel `world.ground` (senza import del template):

```yaml
world:
  ground:
    type: "heightfield"
    bounds: [-50, -50, 50, 50]          # 100×100 m
    height_scale: 25
    heightmap_path: "libraries/terrains/heightfield-strata-test-height.png"
    material: "dis_roccia_scura"
    sea_level: 7.5
    sea_material: "water_alpine"
    strata:
      - { min_altitude: 0.00, max_altitude: 0.36, material: "dis_sabbia_desertica" }
      - { min_altitude: 0.34, max_altitude: 0.75, material: "dis_erba_prato" }
      - { min_altitude: 0.85, max_altitude: 1.00, material: "dis_neve_fresca" }
```

Il motore interseca il heightfield direttamente via MinMax Mipmap
quadtree — nessuna tessellazione mesh, un'unica primitiva per qualsiasi
risoluzione di heightmap.

## I file della libreria

| File | Tipo | Dimensioni | Note |
|------|------|------------|------|
| `heightfield-strata-test.yaml` | Montagna | 100×100 m | Strati altitudine + lago + foschia |
| `heightfield-strata-test-height.png` | Heightmap PNG-16 | 256×256 px | Heightmap a 16 bit grayscale |

## Materiali inclusi

Il file YAML del terrain include materiali di default ottimizzati per
ogni livello altimetrico (sabbia, erba, roccia, neve). Per sovrascriverli,
ridefinisci lo stesso `id` nella tua scena — la definizione locale vince.

## Rigenerare o creare nuovi terreni

Usa lo strumento `TerrainGen` incluso nel repository:

```bash
# Terrain montagna semplice
dotnet run --project src/Tools/TerrainGen/TerrainGen.csproj -- \
  --name montagna-alpina \
  --type montagna

# Terrain con caratteristiche geografiche
dotnet run --project src/Tools/TerrainGen/TerrainGen.csproj -- \
  --name costa-mediterranea \
  --type collina \
  --include fiumi,laghi,mare \
  --season estate

# Con camera preview pre-configurata
dotnet run --project src/Tools/TerrainGen/TerrainGen.csproj -- \
  --name valle-autunnale \
  --type collina \
  --season autunno \
  --with-cameras
```

L'output viene scritto in `scenes/libraries/terrains/<name>.yaml` più un
heightmap `<name>-height.png`. Con `--with-cameras` genera anche una scena
preview completa in `scenes/<name>-preview.yaml`.

Tipi disponibili: `pianura`, `collina`, `montagna`.
Stagioni: `primavera`, `estate`, `autunno`, `inverno`.
Caratteristiche: `fiumi`, `laghi`, `mare`, `isole`.
