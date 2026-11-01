# Sintetici — preset materiali (copia-incolla)

Plastiche, gomme, acrilici, vernici (auto, opache, smalti) e ceramiche (smaltate,
porcellana, opache). Tutti i blocchi sono pronti da incollare nel `materials:`
della tua scena. Per il flusso d'uso vedi [`README.md`](README.md); schema completo
in [`../../docs/reference/scene-reference.md`](../../docs/reference/scene-reference.md).

> **Lucido = coat.** Plastiche lucide, vernici auto e ceramiche smaltate
> condividono lo stesso schema: base diffusa/colorata + un coat liscio
> (`coat_weight`, `coat_roughness`). La vernice metallizzata aggiunge
> `base_metalness` per i fiocchi sotto il coat. Sono tutti opachi: niente
> `transmission_weight` né `subsurface_weight` (salvo acrilici e porcellana
> sottile).

---

## Schema rilevante

```yaml
materials:
  - id: "synthetic_id"
    type: openpbr
    base_color: [0.85, 0.10, 0.10]
    specular_roughness: 0.30          # lucido ≤ 0.2 · satinato 0.3-0.5 · opaco ≥ 0.6
```

---

# Sezione A — Plastiche e gomme

## A1. Plastica lucida (ABS colorato)

```yaml
materials:
  - id: "plastica_lucida"
    type: openpbr
    base_color: [0.85, 0.12, 0.12]
    specular_roughness: 0.12
    coat_weight: 0.6
    coat_roughness: 0.06
    coat_ior: 1.5
```

## A2. Plastica opaca

```yaml
materials:
  - id: "plastica_opaca"
    type: openpbr
    base_color: [0.20, 0.45, 0.70]
    specular_roughness: 0.55
    specular_weight: 0.6
```

## A3. Gomma nera

```yaml
materials:
  - id: "gomma_nera"
    type: openpbr
    base_color: [0.03, 0.03, 0.035]
    specular_roughness: 0.75
    specular_weight: 0.4
```

## A4. Acrilico traslucido

```yaml
materials:
  - id: "acrilico_traslucido"
    type: openpbr
    base_color: [0.85, 0.92, 0.95]
    specular_roughness: 0.06
    transmission_weight: 0.85
    specular_ior: 1.49
    transmission_color: [0.85, 0.95, 0.92]
    transmission_depth: 1.5
```

Plexiglas/PMMA: trasmissivo come il vetro ma IOR più basso. `transmission_weight` esplicito.

# Sezione B — Vernici

## B1. Vernice auto metallizzata

```yaml
materials:
  - id: "vernice_auto_rossa"
    type: openpbr
    base_color: [0.55, 0.04, 0.05]
    base_metalness: 0.8
    specular_roughness: 0.28
    coat_weight: 1.0
    coat_roughness: 0.03
    coat_ior: 1.5
```

Fiocchi metallici (`base_metalness 0.8`, `specular_roughness` media) sotto un coat
liscio a specchio: il look "carrozzeria". Cambia `color` per la tinta.

## B2. Vernice opaca (matte)

```yaml
materials:
  - id: "vernice_opaca"
    type: openpbr
    base_color: [0.15, 0.16, 0.18]
    specular_roughness: 0.7
    specular_weight: 0.4
```

## B3. Smalto lucido

```yaml
materials:
  - id: "smalto_bianco"
    type: openpbr
    base_color: [0.92, 0.92, 0.90]
    specular_roughness: 0.1
    coat_weight: 0.85
    coat_roughness: 0.04
    coat_ior: 1.5
```

# Sezione C — Ceramiche

## C1. Ceramica smaltata

```yaml
materials:
  - id: "ceramica_smaltata"
    type: openpbr
    base_color: [0.90, 0.88, 0.82]
    specular_roughness: 0.08
    specular_weight: 1.1
    coat_weight: 0.9
    coat_roughness: 0.03
    coat_ior: 1.5
```

Smalto ceramico: diffuso chiaro sotto un coat vetroso. Per piastrelle, stoviglie.

## C2. Porcellana bianca

```yaml
materials:
  - id: "porcellana_bianca"
    type: openpbr
    base_color: [0.95, 0.95, 0.94]
    specular_roughness: 0.12
    coat_weight: 0.7
    coat_roughness: 0.05
    subsurface_weight: 0.06
    subsurface_radius_scale: [0.30, 0.28, 0.25]
    subsurface_color: [0.95, 0.95, 0.94]
    coat_ior: 1.5
```

Lieve traslucenza della porcellana sottile: `subsurface_weight` basso +
raggio contenuto. Per tazze, statuine fini.

## C3. Ceramica opaca (terracotta)

```yaml
materials:
  - id: "terracotta"
    type: openpbr
    base_color:
      type: "noise"
      scale: 14.0
      octaves: 4
      noise_strength: 0.15
    specular_roughness: 0.7
    specular_weight: 0.5
```

---

## Matrice decisionale

| Caso d'uso | Preset | Note chiave |
|------------|--------|-------------|
| Oggetto plastica lucida | `plastica_lucida` | coat medio |
| Plastica tecnica opaca | `plastica_opaca`, `gomma_nera` | roughness alta |
| Diffusore/lente plexiglas | `acrilico_traslucido` | transmission_weight 0.85, specular_ior 1.49 |
| Carrozzeria auto | `vernice_auto_rossa` | metallic flake + coat 1.0 |
| Superficie verniciata opaca | `vernice_opaca` | no coat |
| Sanitari/stoviglie smaltate | `ceramica_smaltata`, `smalto_bianco` | coat vetroso |
| Porcellana fine | `porcellana_bianca` | SSS leggero esplicito |
| Vaso/coccio rustico | `terracotta` | noise + matte |

## CLI tips

```bash
# Vernice auto / smalto / ceramica smaltata: clampa i fireflies del coat
dotnet run --project src/RayTracer -- -i scena.yaml -C 40

# Acrilico/porcellana traslucidi: più profondità per la trasmissione/SSS
dotnet run --project src/RayTracer -- -i scena.yaml -d 8 -s 128
```
