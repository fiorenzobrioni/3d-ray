# Volumi e atmosfere — preset mediums (copia-incolla)

Nebbie, foschie, atmosfera fisica, ghiaccio/neve, liquidi e SSS volumetrici. I
mediums si usano come `world.medium` (globale) oppure come `interior_medium` di
un'entità chiusa. Per il flusso d'uso vedi [`README.md`](README.md); schema
completo in [`../../docs/reference/scene-reference.md`](../../docs/reference/scene-reference.md).

> **σ_a e σ_s.** `sigma_a` è l'assorbimento (toglie luce, colora per sottrazione),
> `sigma_s` lo scattering (diffonde, crea la foschia). `phase` con `g` controlla la
> direzionalità (`g>0` forward, tipico di nebbia/fumo; `g≈0` isotropo). Valori
> piccoli (0.001–0.05) per atmosfere estese, più alti per volumi densi e piccoli.

---

## Schema rilevante

```yaml
# Globale, nella scena:
world:
  medium:
    type: "height_fog"        # homogeneous | height_fog | nishita | procedural | grid
    sigma_a: [0.003, 0.003, 0.004]
    sigma_s: [0.008, 0.009, 0.010]
    y0: 0.0                    # quota di riferimento (height_fog)
    scale_height: 8.0          # densità ∝ exp(-(y - y0)/scale_height)
    phase: "hg"               # isotropic | hg | rayleigh | schlick
    g: 0.6

# Oppure come interno di un'entità (blocco mediums + riferimento):
mediums:
  - id: "med_acqua"
    type: "homogeneous"
    sigma_a: [0.20, 0.10, 0.06]
    sigma_s: [0.02, 0.03, 0.04]
    phase: "hg"
    g: 0.0
entities:
  - type: "box"
    # ...
    interior_medium: "med_acqua"
```

---

# Sezione A — Atmosfera ed esterni

## A1. Foschia leggera (height fog)

```yaml
world:
  medium:
    type: "height_fog"
    sigma_a: [0.003, 0.003, 0.004]
    sigma_s: [0.008, 0.009, 0.010]
    y0: 0.0
    scale_height: 8.0
    phase: "hg"
    g: 0.65
```

Velo atmosferico sottile che si dirada con la quota. Da' profondità ai paesaggi.

## A2. Nebbia mattutina densa

```yaml
world:
  medium:
    type: "height_fog"
    sigma_a: [0.010, 0.011, 0.012]
    sigma_s: [0.035, 0.038, 0.040]
    y0: 0.0
    scale_height: 5.0
    phase: "hg"
    g: 0.50
```

## A3. Atmosfera fisica (Nishita)

```yaml
world:
  medium:
    type: "nishita"
    # scattering di Rayleigh (aria) + Mie (aerosol), accoppiato al cielo nishita
```

Atmosfera planetaria fisicamente basata: usala con `sky: { type: "nishita" }` per
cieli e tramonti realistici con prospettiva aerea coerente.

# Sezione B — Ghiaccio e neve

## B1. Ghiaccio (volume trasmissivo)

```yaml
mediums:
  - id: "med_ghiaccio"
    type: "homogeneous"
    sigma_a: [0.04, 0.02, 0.015]
    sigma_s: [0.06, 0.07, 0.09]
    phase: "hg"
    g: 0.2
```

Da abbinare a una geometria con materiale vetro/dielettrico (vedi
[`materials-glass.md`](materials-glass.md)) e `interior_medium: "med_ghiaccio"`:
l'assorbimento azzurrino cresce con lo spessore.

## B2. Neve volumetrica (SSS denso)

```yaml
mediums:
  - id: "med_neve"
    type: "homogeneous"
    sigma_a: [0.01, 0.01, 0.012]
    sigma_s: [0.9, 0.9, 0.95]
    phase: "hg"
    g: 0.0
```

# Sezione C — Liquidi

## C1. Acqua

```yaml
mediums:
  - id: "med_acqua"
    type: "homogeneous"
    sigma_a: [0.35, 0.10, 0.05]
    sigma_s: [0.02, 0.03, 0.04]
    phase: "hg"
    g: 0.0
```

Assorbimento rosso/giallo maggiore → il blu-verde residuo tipico dell'acqua
profonda. Abbinare a un materiale acqua (`spec_trans: 1`, `ior: 1.33`).

## C2. Liquido torbido (latte/lattiginoso)

```yaml
mediums:
  - id: "med_torbido"
    type: "homogeneous"
    sigma_a: [0.02, 0.02, 0.03]
    sigma_s: [0.8, 0.8, 0.7]
    phase: "hg"
    g: 0.4
```

# Sezione D — SSS volumetrico per materiali

Per pietra/cera/pelle l'SSS si attiva direttamente dal materiale Disney tramite
`subsurface_radius` (vedi [`materials-stone.md`](materials-stone.md) e
[`materials-organic.md`](materials-organic.md)). In alternativa, per il controllo
fine, costruisci il volume come medium interno:

```yaml
mediums:
  - id: "med_marmo_interno"
    type: "homogeneous"
    sigma_a: [0.2, 0.25, 0.35]
    sigma_s: [2.0, 2.0, 1.8]
    phase: "hg"
    g: 0.0
```

---

## Matrice decisionale

| Effetto | Preset | Uso |
|---------|--------|-----|
| Profondità paesaggio | A1 foschia leggera | `world.medium` |
| Atmosfera fitta/mattino | A2 nebbia densa | `world.medium` |
| Cielo/atmosfera fisica | A3 nishita | `world.medium` + sky nishita |
| Cubetto/blocco di ghiaccio | B1 ghiaccio | `interior_medium` |
| Cumulo di neve | B2 neve | `interior_medium` |
| Bicchiere d'acqua/mare | C1 acqua | `interior_medium` |
| Latte/liquidi opachi | C2 torbido | `interior_medium` |
| Marmo/cera retroilluminati | D / material SSS | `interior_medium` o `subsurface_radius` |

## CLI tips

```bash
# I volumi sono rumorosi: alza i campioni e la profondità
dotnet run --project src/RayTracer -- -i scena.yaml -s 256 -d 8

# Volumi densi che generano fireflies: abbassa il clamp
dotnet run --project src/RayTracer -- -i scena.yaml -C 25
```
