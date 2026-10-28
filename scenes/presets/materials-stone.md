# Pietra — preset materiali (copia-incolla)

Marmi, graniti, travertino, onice/alabastro, cemento e mattone. Tutti i blocchi
sono pronti da incollare nel `materials:` della tua scena. Per il flusso d'uso
vedi [`README.md`](README.md); schema completo in
[`../../docs/reference/scene-reference.md`](../../docs/reference/scene-reference.md).

> **Regola d'oro: opaco vs traslucido.** Il marmo lucido reale è **opaco** con una
> sottile pelle lucida: non è vetro. Solo onice e alabastro sono davvero
> traslucidi. Per un marmo opaco dichiara la lucidatura con il coat
> (`coat_weight` + `coat_roughness` basso) e niente `subsurface_weight` né
> `transmission_weight`. Per un marmo traslucido dai `subsurface_weight` con
> `subsurface_color`, `subsurface_radius` e `subsurface_radius_scale` (il
> loader costruisce il mezzo del random walk). Vedi
> [`../../docs/technical/subsurface-scattering.md`](../../docs/technical/subsurface-scattering.md).

---

## Schema rilevante per la pietra

```yaml
materials:
  - id: "stone_id"
    type: openpbr
    # ── Pattern procedurale (marmo) ──────────────────────────────────────
    base_color:
      type: "marble"          # marble | noise | voronoi
      scale: 2.4
      colors: [[0.96,0.95,0.94], [0.32,0.34,0.40]]  # base, vena
      vein_axis: [0, 1, 0]
      vein_layers: 2           # 1-3; vein_scale/vein_weight devono avere questa lunghezza
      vein_scale:  [1.0, 2.4]
      vein_weight: [1.0, 0.50]
      vein_thickness: 0.12     # 0-1
      vein_softness:  0.07     # 0-1
      warp_amplitude: 0.9
      warp_iterations: 2
      fold_amplitude: [0.8, 0.25, 0.45]
      color_variation: 0.08
      randomize_offset: true
      # color_ramp: [...] multi-stop per studio-grade
    # ── Superficie ───────────────────────────────────────────────────────
    specular_roughness: 0.10            # lucido ≤ 0.14 · levigato 0.30-0.45 · spacco ≥ 0.8
    specular_weight: 1.44
    coat_weight: 0.90            # OPACO lucido: coat alto, niente subsurface
    coat_roughness: 0.05
    coat_ior: 1.5
    # ── Traslucenza (SOLO onice/alabastro) ───────────────────────────────
    subsurface_weight: 0.0            # opaco = 0 (esplicito) · onice 0.3-0.7 · alabastro 0.05-0.10
    specular_ior: 1.49
    subsurface_radius_scale: [0.85, 0.75, 0.55]   # SOLO traslucidi (gesso/onice)
    subsurface_scatter_anisotropy: 0.1
    subsurface_color: [1, 1, 1]
```

---

# Sezione A — Marmi bianchi opachi lucidi

Carrara/Calacatta/Statuario lucidati a specchio: opachi, lucentezza data dal
coat (`coat_weight`). **Nessun** `subsurface_weight`.

## A1. Carrara lucido

```yaml
materials:
  - id: "carrara_lucido"
    type: openpbr
    base_color:
      type: "marble"
      scale: 2.4
      colors: [[0.96, 0.95, 0.94], [0.32, 0.34, 0.40]]
      vein_axis: [0, 1, 0]
      vein_layers: 2
      vein_scale:  [1.0, 2.4]
      vein_weight: [1.0, 0.50]
      vein_thickness: 0.12
      vein_softness: 0.07
      warp_amplitude: 0.9
      warp_iterations: 2
      fold_amplitude: [0.8, 0.25, 0.45]
      color_variation: 0.08
      randomize_offset: true
    specular_roughness: 0.10
    specular_weight: 1.44
    coat_weight: 0.90
    coat_roughness: 0.05
    coat_ior: 1.5
```

Bianco caldo a vene grigio-blu sottili. La pelle lucida è il `coat_weight 0.90` su
`specular_roughness 0.10`; senza subsurface la pietra resta opaca.

## A2. Calacatta lucido (vene dorate marcate)

```yaml
materials:
  - id: "calacatta_lucido"
    type: openpbr
    base_color:
      type: "marble"
      scale: 2.0
      colors: [[0.97, 0.96, 0.93], [0.62, 0.50, 0.30]]
      vein_axis: [0, 1, 0]
      vein_layers: 2
      vein_scale:  [0.8, 2.0]
      vein_weight: [1.0, 0.45]
      vein_thickness: 0.16
      vein_softness: 0.06
      warp_amplitude: 1.0
      warp_iterations: 2
      fold_amplitude: [0.9, 0.30, 0.50]
      color_variation: 0.07
      randomize_offset: true
    specular_roughness: 0.09
    specular_weight: 1.5
    coat_weight: 0.92
    coat_roughness: 0.04
    coat_ior: 1.5
```

Vene oro/grigio più larghe e drammatiche del Carrara. Stesso impianto opaco-lucido.

## A3. Statuario lucido (bianco fondo, vena netta)

```yaml
materials:
  - id: "statuario_lucido"
    type: openpbr
    base_color:
      type: "marble"
      scale: 2.2
      colors: [[0.98, 0.97, 0.96], [0.30, 0.31, 0.36]]
      vein_axis: [0, 1, 0]
      vein_layers: 2
      vein_scale:  [1.0, 2.6]
      vein_weight: [1.0, 0.40]
      vein_thickness: 0.10
      vein_softness: 0.05
      warp_amplitude: 0.8
      warp_iterations: 2
      fold_amplitude: [0.75, 0.22, 0.40]
      color_variation: 0.05
      randomize_offset: true
    specular_roughness: 0.08
    specular_weight: 1.56
    coat_weight: 0.93
    coat_roughness: 0.04
    coat_ior: 1.5
```

# Sezione B — Marmi scuri e levigati

## B1. Nero Marquinia lucido

```yaml
materials:
  - id: "nero_marquinia_lucido"
    type: openpbr
    base_color:
      type: "marble"
      scale: 2.2
      colors: [[0.05, 0.05, 0.06], [0.92, 0.90, 0.86]]
      vein_axis: [0, 1, 0]
      vein_layers: 2
      vein_scale:  [1.0, 2.8]
      vein_weight: [1.0, 0.55]
      vein_thickness: 0.08
      vein_softness: 0.05
      warp_amplitude: 1.0
      warp_iterations: 2
      fold_amplitude: [0.9, 0.30, 0.55]
      color_variation: 0.04
      randomize_offset: true
    specular_roughness: 0.07
    specular_weight: 1.6
    coat_weight: 0.94
    coat_roughness: 0.04
    coat_ior: 1.5
```

Nero profondo a vene bianche nette. Il coat alto dà il riflesso da lastra
lucidata; clamp dei fireflies consigliato (vedi CLI tips).

## B2. Carrara levigato (honed, opaco matte)

```yaml
materials:
  - id: "carrara_levigato"
    type: openpbr
    base_color:
      type: "marble"
      scale: 2.4
      colors: [[0.96, 0.95, 0.94], [0.32, 0.34, 0.40]]
      vein_axis: [0, 1, 0]
      vein_layers: 2
      vein_scale:  [1.0, 2.4]
      vein_weight: [1.0, 0.50]
      vein_thickness: 0.12
      vein_softness: 0.07
      warp_amplitude: 0.9
      warp_iterations: 2
      fold_amplitude: [0.8, 0.25, 0.45]
      color_variation: 0.08
      randomize_offset: true
    specular_roughness: 0.38
    specular_weight: 0.8
```

Levigatura fine senza lucidatura: niente coat, `specular_roughness 0.38`. Per
sculture, piani bagno, pavimenti opachi.

# Sezione C — Marmi traslucidi (onice, alabastro)

Qui la trasmissione è **corretta** e va autorata esplicitamente.

## C1. Onice miele (retroilluminabile)

```yaml
materials:
  - id: "onice_miele"
    type: openpbr
    base_color:
      type: "noise"
      scale: 2.5
      noise_type: "hetero_terrain"
      octaves: 6
      fractal_increment: 0.7
      fractal_offset: 0.6
      noise_strength: 0.6
      color_ramp:
        - { position: 0.00, color: [0.45, 0.22, 0.08], interp: "smoothstep" }
        - { position: 0.50, color: [0.78, 0.48, 0.18], interp: "smoothstep" }
        - { position: 1.00, color: [0.96, 0.78, 0.42], interp: "linear" }
    specular_roughness: 0.10
    subsurface_weight: 0.65
    specular_ior: 1.486
    subsurface_radius_scale: [0.50, 0.35, 0.15]
    coat_weight: 0.50
    coat_roughness: 0.08
    coat_ior: 1.5
    subsurface_color: [0.92, 0.65, 0.32]
```

Onice ambrato a bande, splendido **retroilluminato** (piano d'onice con luce
dietro). `subsurface_weight 0.65` con `subsurface_color` ambra e
`subsurface_radius_scale` danno la traslucenza colorata e la diffusione interna.

## C2. Alabastro bianco (gesso traslucido morbido)

```yaml
materials:
  - id: "alabastro_bianco"
    type: openpbr
    base_color:
      type: "marble"
      scale: 1.0
      colors: [[0.94, 0.92, 0.88], [0.85, 0.80, 0.72]]
      vein_axis: [0, 1, 0]
      vein_layers: 1
      vein_scale:  [1.0]
      vein_weight: [1.0]
      vein_thickness: 0.50
      vein_softness: 0.35
      warp_amplitude: 0.7
      warp_iterations: 2
      fold_amplitude: [0.55, 0.30, 0.45]
      color_variation: 0.10
      randomize_offset: true
    specular_roughness: 0.32
    specular_weight: 0.84
    subsurface_weight: 0.08
    specular_ior: 1.52
    subsurface_radius_scale: [0.85, 0.75, 0.55]
    subsurface_scatter_anisotropy: 0.1
    subsurface_color: [1, 1, 1]
```

Gesso traslucido: quota bassa (`subsurface_weight 0.08`) ma forte diffusione
sottosuperficiale (raggio alto). Tipicamente honed, niente coat.

# Sezione D — Graniti

## D1. Granito grigio lucido

```yaml
materials:
  - id: "granito_grigio_lucido"
    type: openpbr
    base_color:
      type: "noise"
      scale: 28.0
      octaves: 4
      color_ramp:
        - { position: 0.0, color: [0.18, 0.18, 0.20] }
        - { position: 0.5, color: [0.42, 0.42, 0.45] }
        - { position: 1.0, color: [0.78, 0.76, 0.74] }
    specular_roughness: 0.14
    specular_weight: 1.2
    coat_weight: 0.7
    coat_roughness: 0.06
    coat_ior: 1.5
```

## D2. Nero assoluto lucido

```yaml
materials:
  - id: "nero_assoluto_lucido"
    type: openpbr
    base_color:
      type: "noise"
      scale: 36.0
      octaves: 3
      noise_strength: 0.10
    specular_roughness: 0.08
    specular_weight: 1.4
    coat_weight: 0.85
    coat_roughness: 0.04
    coat_ior: 1.5
```

# Sezione E — Travertino, mattone, cemento (opachi matte)

## E1. Travertino chiaro

```yaml
materials:
  - id: "travertino_chiaro"
    type: openpbr
    base_color:
      type: "noise"
      scale: 12.0
      octaves: 5
      color_ramp:
        - { position: 0.0, color: [0.62, 0.55, 0.44] }
        - { position: 1.0, color: [0.86, 0.80, 0.68] }
    specular_roughness: 0.55
    specular_weight: 0.6
```

## E2. Mattone rosso

```yaml
materials:
  - id: "mattone_rosso"
    type: openpbr
    base_color:
      type: "noise"
      scale: 18.0
      octaves: 4
      noise_strength: 0.25
    specular_roughness: 0.85
    specular_weight: 0.3
```

## E3. Cemento liscio

```yaml
materials:
  - id: "cemento_liscio"
    type: openpbr
    base_color:
      type: "noise"
      scale: 8.0
      octaves: 5
      noise_strength: 0.12
    specular_roughness: 0.65
    specular_weight: 0.4
```

---

## Matrice decisionale

| Caso d'uso | Preset | Note chiave |
|------------|--------|-------------|
| Piano/parete marmo bianco lucido | `carrara_lucido`, `statuario_lucido` | coat 0.9, niente subsurface |
| Marmo bianco vene dorate marcate | `calacatta_lucido` | vene più larghe |
| Marmo scuro lucido | `nero_marquinia_lucido` | clamp fireflies |
| Scultura/bagno opaco | `carrara_levigato` | honed, niente coat |
| Lastra/lampada retroilluminata | `onice_miele` | `subsurface_weight 0.65` |
| Statua traslucida morbida | `alabastro_bianco` | SSS, `subsurface_weight` basso |
| Piano cucina/pavimento granito | `granito_grigio_lucido`, `nero_assoluto_lucido` | grana noise fine |
| Esterni/rustico | `travertino_chiaro`, `mattone_rosso`, `cemento_liscio` | matte, niente coat |

## CLI tips

```bash
# Marmo lucido / scuro a specchio: clampa i fireflies del coat
dotnet run --project src/RayTracer -- -i scena.yaml -C 25

# Onice/alabastro traslucidi: SSS rumoroso → più profondità e campioni
dotnet run --project src/RayTracer -- -i scena.yaml -d 8 -s 256
```
