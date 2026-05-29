# Organici — preset materiali (copia-incolla)

Tessuti, pelli, cibi e materiali organici. Tutti i blocchi sono pronti da incollare
nel `materials:` della tua scena. Per il flusso d'uso vedi [`README.md`](README.md);
schema completo in [`../../docs/reference/scene-reference.md`](../../docs/reference/scene-reference.md).

> **Tessuto = sheen, traslucido = SSS esplicito.** I tessuti usano il lobo
> `sheen` (riflesso radente alla Charlie). Pelle, cera e cibi traslucidi usano
> `subsurface_radius`: poiché il motore, in presenza di `subsurface_radius` senza
> `spec_trans`, auto-promuove `spec_trans` a 1.0 (look vetroso), per questi
> materiali **autora sempre `spec_trans` esplicito** (basso, 0.05-0.2).

---

## Schema rilevante

```yaml
materials:
  - id: "organic_id"
    type: "disney"
    color: [0.6, 0.1, 0.1]
    roughness: 0.7
    # Tessuti:
    sheen: 0.6
    sheen_tint: 0.4
    sheen_roughness: 0.3
    # Traslucidi (pelle/cera/cibo):
    spec_trans: 0.1            # SEMPRE esplicito quando c'è subsurface_radius
    subsurface_radius: [0.5, 0.25, 0.15]
    subsurface_anisotropy: 0.0
```

---

# Sezione A — Tessuti

## A1. Cotone opaco

```yaml
materials:
  - id: "cotone"
    type: "disney"
    color: [0.85, 0.83, 0.78]
    roughness: 0.85
    specular: 0.2
    sheen: 0.3
    sheen_tint: 0.5
    sheen_roughness: 0.4
```

## A2. Velluto

```yaml
materials:
  - id: "velluto_rosso"
    type: "disney"
    color: [0.45, 0.05, 0.08]
    roughness: 0.9
    specular: 0.15
    sheen: 1.0
    sheen_tint: 0.6
    sheen_roughness: 0.25
```

Sheen alto + roughness alta = il caratteristico bordo luminoso radente del velluto.

## A3. Seta

```yaml
materials:
  - id: "seta_avorio"
    type: "disney"
    color: [0.90, 0.86, 0.74]
    roughness: 0.35
    specular: 0.4
    sheen: 0.7
    sheen_tint: 0.3
    sheen_roughness: 0.2
    anisotropic: 0.4
```

## A4. Denim

```yaml
materials:
  - id: "denim"
    type: "disney"
    color: [0.20, 0.30, 0.48]
    texture:
      type: "noise"
      scale: 60.0
      octaves: 3
      noise_strength: 0.18
    roughness: 0.88
    specular: 0.15
    sheen: 0.4
    sheen_roughness: 0.5
```

# Sezione B — Pelli

## B1. Pelle liscia

```yaml
materials:
  - id: "pelle_liscia"
    type: "disney"
    color: [0.35, 0.18, 0.10]
    texture:
      type: "noise"
      scale: 40.0
      octaves: 4
      noise_strength: 0.12
    roughness: 0.45
    specular: 0.35
    clearcoat: 0.15
    coat_roughness: 0.4
```

## B2. Pelle scamosciata (suede)

```yaml
materials:
  - id: "suede"
    type: "disney"
    color: [0.42, 0.30, 0.20]
    roughness: 0.95
    specular: 0.1
    sheen: 0.5
    sheen_roughness: 0.6
```

## B3. Pelle invecchiata

```yaml
materials:
  - id: "pelle_invecchiata"
    type: "disney"
    color: [0.28, 0.15, 0.09]
    texture:
      type: "noise"
      scale: 22.0
      octaves: 5
      noise_strength: 0.28
    roughness: 0.6
    specular: 0.3
    clearcoat: 0.1
    coat_roughness: 0.5
```

# Sezione C — Cibi

## C1. Crosta di pane

```yaml
materials:
  - id: "crosta_pane"
    type: "disney"
    color: [0.55, 0.35, 0.16]
    texture:
      type: "noise"
      scale: 16.0
      octaves: 5
      noise_strength: 0.3
    roughness: 0.8
    specular: 0.2
```

## C2. Cioccolato fondente

```yaml
materials:
  - id: "cioccolato"
    type: "disney"
    color: [0.14, 0.07, 0.04]
    roughness: 0.25
    specular: 0.45
    clearcoat: 0.3
    coat_roughness: 0.15
    spec_trans: 0.05
    subsurface_radius: [0.06, 0.03, 0.02]
```

Lucido superficiale + minima diffusione interna calda. `spec_trans` esplicito basso.

## C3. Formaggio stagionato

```yaml
materials:
  - id: "formaggio"
    type: "disney"
    color: [0.92, 0.82, 0.50]
    texture:
      type: "noise"
      scale: 20.0
      octaves: 4
      noise_strength: 0.2
    roughness: 0.6
    specular: 0.25
    spec_trans: 0.08
    subsurface_radius: [0.4, 0.32, 0.18]
```

# Sezione D — Materiali organici

## D1. Pelle umana (skin)

```yaml
materials:
  - id: "pelle_umana"
    type: "disney"
    color: [0.78, 0.55, 0.45]
    roughness: 0.5
    specular: 0.35
    spec_trans: 0.12
    ior: 1.4
    subsurface_radius: [0.9, 0.45, 0.30]
    subsurface_anisotropy: 0.0
```

Diffusione rossastra tipica della pelle: `subsurface_radius` più ampio sul rosso,
`spec_trans` basso **esplicito** per restare opaca (non vetrosa).

## D2. Cera di candela

```yaml
materials:
  - id: "cera"
    type: "disney"
    color: [0.92, 0.88, 0.78]
    roughness: 0.4
    specular: 0.3
    spec_trans: 0.15
    ior: 1.45
    subsurface_radius: [0.8, 0.7, 0.5]
    subsurface_anisotropy: 0.0
```

## D3. Foglia (verde traslucido)

```yaml
materials:
  - id: "foglia"
    type: "disney"
    color: [0.20, 0.45, 0.12]
    roughness: 0.5
    specular: 0.3
    spec_trans: 0.18
    subsurface_radius: [0.15, 0.4, 0.1]
    thin_walled: false
    clearcoat: 0.2
    coat_roughness: 0.3
```

---

## Matrice decisionale

| Caso d'uso | Preset | Note chiave |
|------------|--------|-------------|
| Lenzuola/abiti casual | `cotone`, `denim` | sheen medio, roughness alta |
| Tendaggi/imbottiti pregiati | `velluto_rosso`, `seta_avorio` | sheen alto |
| Divani/borse/scarpe | `pelle_liscia`, `suede`, `pelle_invecchiata` | coat leggero o sheen |
| Still life cibo | `crosta_pane`, `cioccolato`, `formaggio` | SSS esplicito dove serve |
| Personaggi/ritratti | `pelle_umana` | SSS rosso, spec_trans basso |
| Candele/lumi | `cera` | SSS alto, retroilluminabile |
| Vegetazione close-up | `foglia` | trasmissione verde |

## CLI tips

```bash
# Materiali SSS (pelle, cera, cibi): più profondità e campioni, niente clamp aggressivo
dotnet run --project src/RayTracer -- -i scena.yaml -d 8 -s 256

# Velluto/sheen: il lobo è sottile, alza i campioni per ridurre il rumore radente
dotnet run --project src/RayTracer -- -i scena.yaml -s 256
```
