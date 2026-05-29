# Sintetici — preset materiali (copia-incolla)

Plastiche, gomme, acrilici, vernici (auto, opache, smalti) e ceramiche (smaltate,
porcellana, opache). Tutti i blocchi sono pronti da incollare nel `materials:`
della tua scena. Per il flusso d'uso vedi [`README.md`](README.md); schema completo
in [`../../docs/reference/scene-reference.md`](../../docs/reference/scene-reference.md).

> **Lucido = clearcoat.** Plastiche lucide, vernici auto e ceramiche smaltate
> condividono lo stesso schema: base diffusa/colorata + un `clearcoat` liscio. La
> vernice metallizzata aggiunge `metallic` per i fiocchi sotto il coat. Sono tutti
> opachi: `metallic` 0 (salvo flake auto), niente `subsurface_radius`.

---

## Schema rilevante

```yaml
materials:
  - id: "synthetic_id"
    type: "disney"
    color: [0.85, 0.10, 0.10]
    roughness: 0.30          # lucido ≤ 0.2 · satinato 0.3-0.5 · opaco ≥ 0.6
    specular: 0.5
    metallic: 0.0            # flake metallizzato auto: 0.6-0.9
    clearcoat: 0.0           # lucido/auto/smalto: 0.7-1.0
    coat_roughness: 0.05
```

---

# Sezione A — Plastiche e gomme

## A1. Plastica lucida (ABS colorato)

```yaml
materials:
  - id: "plastica_lucida"
    type: "disney"
    color: [0.85, 0.12, 0.12]
    roughness: 0.12
    specular: 0.5
    clearcoat: 0.6
    coat_roughness: 0.06
```

## A2. Plastica opaca

```yaml
materials:
  - id: "plastica_opaca"
    type: "disney"
    color: [0.20, 0.45, 0.70]
    roughness: 0.55
    specular: 0.3
```

## A3. Gomma nera

```yaml
materials:
  - id: "gomma_nera"
    type: "disney"
    color: [0.03, 0.03, 0.035]
    roughness: 0.75
    specular: 0.2
```

## A4. Acrilico traslucido

```yaml
materials:
  - id: "acrilico_traslucido"
    type: "disney"
    color: [0.85, 0.92, 0.95]
    roughness: 0.06
    specular: 0.5
    spec_trans: 0.85
    ior: 1.49
    transmission_color: [0.85, 0.95, 0.92]
    transmission_depth: 1.5
```

Plexiglas/PMMA: trasmissivo come il vetro ma IOR più basso. `spec_trans` esplicito.

# Sezione B — Vernici

## B1. Vernice auto metallizzata

```yaml
materials:
  - id: "vernice_auto_rossa"
    type: "disney"
    color: [0.55, 0.04, 0.05]
    metallic: 0.8
    roughness: 0.28
    specular: 0.5
    clearcoat: 1.0
    coat_roughness: 0.03
```

Fiocchi metallici (`metallic 0.8`, `roughness` media) sotto un `clearcoat`
liscio a specchio: il look "carrozzeria". Cambia `color` per la tinta.

## B2. Vernice opaca (matte)

```yaml
materials:
  - id: "vernice_opaca"
    type: "disney"
    color: [0.15, 0.16, 0.18]
    roughness: 0.7
    specular: 0.2
```

## B3. Smalto lucido

```yaml
materials:
  - id: "smalto_bianco"
    type: "disney"
    color: [0.92, 0.92, 0.90]
    roughness: 0.1
    specular: 0.5
    clearcoat: 0.85
    coat_roughness: 0.04
```

# Sezione C — Ceramiche

## C1. Ceramica smaltata

```yaml
materials:
  - id: "ceramica_smaltata"
    type: "disney"
    color: [0.90, 0.88, 0.82]
    roughness: 0.08
    specular: 0.55
    clearcoat: 0.9
    coat_roughness: 0.03
```

Smalto ceramico: diffuso chiaro sotto un coat vetroso. Per piastrelle, stoviglie.

## C2. Porcellana bianca

```yaml
materials:
  - id: "porcellana_bianca"
    type: "disney"
    color: [0.95, 0.95, 0.94]
    roughness: 0.12
    specular: 0.5
    clearcoat: 0.7
    coat_roughness: 0.05
    spec_trans: 0.06
    ior: 1.5
    subsurface_radius: [0.30, 0.28, 0.25]
    subsurface_anisotropy: 0.0
```

Lieve traslucenza della porcellana sottile: `spec_trans` basso **esplicito** +
`subsurface_radius` contenuto. Per tazze, statuine fini.

## C3. Ceramica opaca (terracotta)

```yaml
materials:
  - id: "terracotta"
    type: "disney"
    color: [0.65, 0.32, 0.20]
    texture:
      type: "noise"
      scale: 14.0
      octaves: 4
      noise_strength: 0.15
    roughness: 0.7
    specular: 0.25
```

---

## Matrice decisionale

| Caso d'uso | Preset | Note chiave |
|------------|--------|-------------|
| Oggetto plastica lucida | `plastica_lucida` | clearcoat medio |
| Plastica tecnica opaca | `plastica_opaca`, `gomma_nera` | roughness alta |
| Diffusore/lente plexiglas | `acrilico_traslucido` | spec_trans 0.85, ior 1.49 |
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
