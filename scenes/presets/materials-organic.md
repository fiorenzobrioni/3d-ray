# Organici — preset materiali (copia-incolla)

Tessuti, pelli, cibi e materiali organici. Tutti i blocchi sono pronti da incollare
nel `materials:` della tua scena. Per il flusso d'uso vedi [`README.md`](README.md);
schema completo in [`../../docs/reference/scene-reference.md`](../../docs/reference/scene-reference.md).

> **Tessuto = fuzz, traslucido = subsurface.** I tessuti usano lo strato `fuzz`
> (`fuzz_weight`, `fuzz_color`, `fuzz_roughness`: il riflesso radente delle
> fibre). Pelle, cera e cibi traslucidi usano `subsurface_weight` con
> `subsurface_color`, `subsurface_radius` e `subsurface_radius_scale`: la
> luce entra e diffonde nel mezzo che il loader costruisce.

---

## Schema rilevante

```yaml
materials:
  - id: "organic_id"
    type: openpbr
    base_color: [0.6, 0.1, 0.1]
    specular_roughness: 0.7
    # Tessuti:
    fuzz_weight: 0.6
    fuzz_color: [1.7633544, 0.7938924, 0.7938924]
    fuzz_roughness: 0.3
    # Traslucidi (pelle/cera/cibo):
    subsurface_weight: 0.1            # SEMPRE esplicito quando c'è subsurface_radius
    subsurface_radius_scale: [0.5, 0.25, 0.15]
    subsurface_color: [0.6, 0.1, 0.1]
```

---

# Sezione A — Tessuti

## A1. Cotone opaco

```yaml
materials:
  - id: "cotone"
    type: openpbr
    base_color: [0.85, 0.83, 0.78]
    specular_roughness: 0.85
    specular_weight: 0.4
    fuzz_weight: 0.3
    fuzz_color: [1.0116525, 0.9996135, 0.9695163]
    fuzz_roughness: 0.4
```

## A2. Velluto

```yaml
materials:
  - id: "velluto_rosso"
    type: openpbr
    base_color: [0.45, 0.05, 0.08]
    specular_roughness: 0.9
    specular_weight: 0.3
    fuzz_weight: 1.0
    fuzz_color: [2.367844, 0.6186493, 0.7498389]
    fuzz_roughness: 0.25
```

Sheen alto + roughness alta = il caratteristico bordo luminoso radente del velluto.

## A3. Seta

```yaml
materials:
  - id: "seta_avorio"
    type: openpbr
    base_color: [0.90, 0.86, 0.74]
    specular_roughness: 0.35
    specular_weight: 0.8
    fuzz_weight: 0.7
    fuzz_color: [1.0140119, 1.0000558, 0.9581876]
    fuzz_roughness: 0.2
    specular_roughness_anisotropy: 0.36
```

## A4. Denim

```yaml
materials:
  - id: "denim"
    type: openpbr
    base_color:
      type: "noise"
      scale: 60.0
      octaves: 3
      noise_strength: 0.18
    specular_roughness: 0.88
    specular_weight: 0.3
    fuzz_weight: 0.4
    fuzz_roughness: 0.5
```

# Sezione B — Pelli

## B1. Pelle liscia

```yaml
materials:
  - id: "pelle_liscia"
    type: openpbr
    base_color:
      type: "noise"
      scale: 40.0
      octaves: 4
      noise_strength: 0.12
    specular_roughness: 0.45
    specular_weight: 0.7
    coat_weight: 0.15
    coat_roughness: 0.4
    coat_ior: 1.5
```

## B2. Pelle scamosciata (suede)

```yaml
materials:
  - id: "suede"
    type: openpbr
    base_color: [0.42, 0.30, 0.20]
    specular_roughness: 0.95
    specular_weight: 0.2
    fuzz_weight: 0.5
    fuzz_roughness: 0.6
    fuzz_color: [1.1597714, 0.9712654, 0.8141769]
```

## B3. Pelle invecchiata

```yaml
materials:
  - id: "pelle_invecchiata"
    type: openpbr
    base_color:
      type: "noise"
      scale: 22.0
      octaves: 5
      noise_strength: 0.28
    specular_roughness: 0.6
    specular_weight: 0.6
    coat_weight: 0.1
    coat_roughness: 0.5
    coat_ior: 1.5
```

# Sezione C — Cibi

## C1. Crosta di pane

```yaml
materials:
  - id: "crosta_pane"
    type: openpbr
    base_color:
      type: "noise"
      scale: 16.0
      octaves: 5
      noise_strength: 0.3
    specular_roughness: 0.8
    specular_weight: 0.4
```

## C2. Cioccolato fondente

```yaml
materials:
  - id: "cioccolato"
    type: openpbr
    base_color: [0.14, 0.07, 0.04]
    specular_roughness: 0.25
    specular_weight: 0.9
    coat_weight: 0.3
    coat_roughness: 0.15
    subsurface_weight: 0.05
    subsurface_radius_scale: [0.06, 0.03, 0.02]
    subsurface_color: [0.14, 0.07, 0.04]
    coat_ior: 1.5
```

Lucido superficiale + minima diffusione interna calda: `subsurface_weight` basso.

## C3. Formaggio stagionato

```yaml
materials:
  - id: "formaggio"
    type: openpbr
    base_color:
      type: "noise"
      scale: 20.0
      octaves: 4
      noise_strength: 0.2
    specular_roughness: 0.6
    specular_weight: 0.5
    subsurface_weight: 0.08
    subsurface_radius_scale: [0.4, 0.32, 0.18]
    subsurface_color: [0.92, 0.82, 0.5]
```

# Sezione D — Materiali organici

## D1. Pelle umana (skin)

```yaml
materials:
  - id: "pelle_umana"
    type: openpbr
    base_color: [0.78, 0.55, 0.45]
    specular_roughness: 0.5
    specular_weight: 0.7
    subsurface_weight: 0.12
    specular_ior: 1.4
    subsurface_radius_scale: [0.9, 0.45, 0.30]
    subsurface_color: [0.78, 0.55, 0.45]
```

Diffusione rossastra tipica della pelle: `subsurface_radius_scale` più ampio sul
rosso, `subsurface_weight` basso per restare opaca (non vetrosa).

## D2. Cera di candela

```yaml
materials:
  - id: "cera"
    type: openpbr
    base_color: [0.92, 0.88, 0.78]
    specular_roughness: 0.4
    specular_weight: 0.6
    subsurface_weight: 0.15
    specular_ior: 1.45
    subsurface_radius_scale: [0.8, 0.7, 0.5]
    subsurface_color: [0.92, 0.88, 0.78]
```

## D3. Foglia (verde traslucido)

```yaml
materials:
  - id: "foglia"
    type: openpbr
    base_color: [0.20, 0.45, 0.12]
    specular_roughness: 0.5
    specular_weight: 0.6
    subsurface_weight: 0.18
    subsurface_radius_scale: [0.15, 0.4, 0.1]
    coat_weight: 0.2
    coat_roughness: 0.3
    subsurface_color: [0.2, 0.45, 0.12]
    coat_ior: 1.5
```

---

## Matrice decisionale

| Caso d'uso | Preset | Note chiave |
|------------|--------|-------------|
| Lenzuola/abiti casual | `cotone`, `denim` | fuzz medio, rugosità alta |
| Tendaggi/imbottiti pregiati | `velluto_rosso`, `seta_avorio` | fuzz alto |
| Divani/borse/scarpe | `pelle_liscia`, `suede`, `pelle_invecchiata` | coat leggero o fuzz |
| Still life cibo | `crosta_pane`, `cioccolato`, `formaggio` | SSS esplicito dove serve |
| Personaggi/ritratti | `pelle_umana` | SSS rosso, `subsurface_weight` basso |
| Candele/lumi | `cera` | SSS alto, retroilluminabile |
| Vegetazione close-up | `foglia` | trasmissione verde |

## CLI tips

```bash
# Materiali SSS (pelle, cera, cibi): più profondità e campioni, niente clamp aggressivo
dotnet run --project src/RayTracer -- -i scena.yaml -d 8 -s 256

# Velluto/fuzz: il lobo è sottile, alza i campioni per ridurre il rumore radente
dotnet run --project src/RayTracer -- -i scena.yaml -s 256
```
