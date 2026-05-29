# Pietra — preset materiali (copia-incolla)

Marmi, graniti, travertino, onice/alabastro, cemento e mattone. Tutti i blocchi
sono pronti da incollare nel `materials:` della tua scena. Per la filosofia e il
flusso d'uso vedi [`README.md`](README.md); schema completo in
[`../../docs/reference/scene-reference.md`](../../docs/reference/scene-reference.md).

> **Regola d'oro: opaco vs traslucido.** Il marmo lucido reale è **opaco** con una
> sottile pelle lucida — non è vetro. Solo onice e alabastro sono davvero
> traslucidi. Il motore, quando trova `subsurface_radius` e **nessun** `spec_trans`,
> **auto-promuove** `spec_trans` a 1.0 (necessario perché il lobo di trasmissione
> attivi il medium SSS random-walk). Quindi per un marmo opaco **non** mettere
> `subsurface_radius`: dichiara la lucidatura con `clearcoat`. Per un marmo
> traslucido autora **esplicitamente** `spec_trans`, `transmission_color`,
> `transmission_depth`. Vedi [`../../docs/technical/subsurface-scattering.md`](../../docs/technical/subsurface-scattering.md).

---

## Schema rilevante per la pietra

```yaml
materials:
  - id: "stone_id"
    type: "disney"
    # ── Pattern procedurale (marmo) ──────────────────────────────────────
    texture:
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
    roughness: 0.10            # lucido ≤ 0.14 · levigato 0.30-0.45 · spacco ≥ 0.8
    specular:  0.72
    clearcoat: 0.90            # OPACO lucido: clearcoat alto, niente subsurface
    coat_roughness: 0.05
    coat_ior: 1.5
    # ── Traslucenza (SOLO onice/alabastro) ───────────────────────────────
    spec_trans: 0.0            # opaco = 0 (esplicito) · onice 0.3-0.7 · alabastro 0.05-0.10
    transmission_color: [0.88, 0.55, 0.18]
    transmission_depth: 0.3    # Beer-Lambert: lo spessore conta
    ior: 1.49
    subsurface_radius: [0.85, 0.75, 0.55]   # SOLO traslucidi (gesso/onice)
    subsurface_anisotropy: 0.1
```

---

# Sezione A — Marmi bianchi opachi lucidi

Carrara/Calacatta/Statuario lucidati a specchio: opachi, lucentezza data dal
`clearcoat`. **Nessun** `subsurface_radius` (eviterebbe l'auto-promozione a vetro).

## A1. Carrara lucido

```yaml
materials:
  - id: "carrara_lucido"
    type: "disney"
    texture:
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
    roughness: 0.10
    specular: 0.72
    clearcoat: 0.90
    coat_roughness: 0.05
    coat_ior: 1.5
    spec_trans: 0.0
```

Bianco caldo a vene grigio-blu sottili. La pelle lucida è il `clearcoat 0.90` su
`roughness 0.10`; `spec_trans: 0` esplicito tiene la pietra opaca.

## A2. Calacatta lucido (vene dorate marcate)

```yaml
materials:
  - id: "calacatta_lucido"
    type: "disney"
    texture:
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
    roughness: 0.09
    specular: 0.75
    clearcoat: 0.92
    coat_roughness: 0.04
    coat_ior: 1.5
    spec_trans: 0.0
```

Vene oro/grigio più larghe e drammatiche del Carrara. Stesso impianto opaco-lucido.

## A3. Statuario lucido (bianco fondo, vena netta)

```yaml
materials:
  - id: "statuario_lucido"
    type: "disney"
    texture:
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
    roughness: 0.08
    specular: 0.78
    clearcoat: 0.93
    coat_roughness: 0.04
    coat_ior: 1.5
    spec_trans: 0.0
```

# Sezione B — Marmi scuri e levigati

## B1. Nero Marquinia lucido

```yaml
materials:
  - id: "nero_marquinia_lucido"
    type: "disney"
    texture:
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
    roughness: 0.07
    specular: 0.80
    clearcoat: 0.94
    coat_roughness: 0.04
    coat_ior: 1.5
    spec_trans: 0.0
```

Nero profondo a vene bianche nette. Il `clearcoat` alto dà il riflesso da lastra
lucidata; clamp dei fireflies consigliato (vedi CLI tips).

## B2. Carrara levigato (honed, opaco matte)

```yaml
materials:
  - id: "carrara_levigato"
    type: "disney"
    texture:
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
    roughness: 0.38
    specular: 0.40
    spec_trans: 0.0
```

Levigatura fine senza lucidatura: niente `clearcoat`, `roughness 0.38`. Per
sculture, piani bagno, pavimenti opachi.

# Sezione C — Marmi traslucidi (onice, alabastro)

Qui la trasmissione è **corretta** e va autorata esplicitamente.

## C1. Onice miele (retroilluminabile)

```yaml
materials:
  - id: "onice_miele"
    type: "disney"
    color: [0.92, 0.65, 0.32]
    texture:
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
    roughness: 0.10
    specular: 0.50
    spec_trans: 0.65
    ior: 1.486
    transmission_color: [0.88, 0.55, 0.18]
    transmission_depth: 0.3
    subsurface_radius: [0.50, 0.35, 0.15]
    clearcoat: 0.50
    coat_roughness: 0.08
    coat_ior: 1.5
```

Onice ambrato a bande, splendido **retroilluminato** (piano d'onice con luce
dietro). `spec_trans 0.65` + `transmission_color/depth` danno la traslucenza
colorata; `subsurface_radius` aggiunge la diffusione interna.

## C2. Alabastro bianco (gesso traslucido morbido)

```yaml
materials:
  - id: "alabastro_bianco"
    type: "disney"
    texture:
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
    roughness: 0.32
    specular: 0.42
    spec_trans: 0.08
    ior: 1.52
    subsurface_radius: [0.85, 0.75, 0.55]
    subsurface_anisotropy: 0.1
```

Gesso traslucido: trasmissione bassa (`spec_trans 0.08`) ma forte diffusione
sottosuperficiale (`subsurface_radius` alto). Tipicamente honed, niente clearcoat.

# Sezione D — Graniti

## D1. Granito grigio lucido

```yaml
materials:
  - id: "granito_grigio_lucido"
    type: "disney"
    texture:
      type: "noise"
      scale: 28.0
      octaves: 4
      color_ramp:
        - { position: 0.0, color: [0.18, 0.18, 0.20] }
        - { position: 0.5, color: [0.42, 0.42, 0.45] }
        - { position: 1.0, color: [0.78, 0.76, 0.74] }
    roughness: 0.14
    specular: 0.6
    clearcoat: 0.7
    coat_roughness: 0.06
    spec_trans: 0.0
```

## D2. Nero assoluto lucido

```yaml
materials:
  - id: "nero_assoluto_lucido"
    type: "disney"
    color: [0.04, 0.04, 0.045]
    texture:
      type: "noise"
      scale: 36.0
      octaves: 3
      noise_strength: 0.10
    roughness: 0.08
    specular: 0.7
    clearcoat: 0.85
    coat_roughness: 0.04
    spec_trans: 0.0
```

# Sezione E — Travertino, mattone, cemento (opachi matte)

## E1. Travertino chiaro

```yaml
materials:
  - id: "travertino_chiaro"
    type: "disney"
    texture:
      type: "noise"
      scale: 12.0
      octaves: 5
      color_ramp:
        - { position: 0.0, color: [0.62, 0.55, 0.44] }
        - { position: 1.0, color: [0.86, 0.80, 0.68] }
    roughness: 0.55
    specular: 0.3
    spec_trans: 0.0
```

## E2. Mattone rosso

```yaml
materials:
  - id: "mattone_rosso"
    type: "disney"
    color: [0.55, 0.22, 0.16]
    texture:
      type: "noise"
      scale: 18.0
      octaves: 4
      noise_strength: 0.25
    roughness: 0.85
    specular: 0.15
```

## E3. Cemento liscio

```yaml
materials:
  - id: "cemento_liscio"
    type: "disney"
    color: [0.55, 0.55, 0.57]
    texture:
      type: "noise"
      scale: 8.0
      octaves: 5
      noise_strength: 0.12
    roughness: 0.65
    specular: 0.2
```

---

## Matrice decisionale

| Caso d'uso | Preset | Note chiave |
|------------|--------|-------------|
| Piano/parete marmo bianco lucido | `carrara_lucido`, `statuario_lucido` | clearcoat 0.9, `spec_trans: 0` |
| Marmo bianco vene dorate marcate | `calacatta_lucido` | vene più larghe |
| Marmo scuro lucido | `nero_marquinia_lucido` | clamp fireflies |
| Scultura/bagno opaco | `carrara_levigato` | honed, no clearcoat |
| Lastra/lampada retroilluminata | `onice_miele` | `spec_trans 0.65` + transmission |
| Statua traslucida morbida | `alabastro_bianco` | SSS alto, spec_trans basso |
| Piano cucina/pavimento granito | `granito_grigio_lucido`, `nero_assoluto_lucido` | grana noise fine |
| Esterni/rustico | `travertino_chiaro`, `mattone_rosso`, `cemento_liscio` | matte, no clearcoat |

## CLI tips

```bash
# Marmo lucido / scuro a specchio: clampa i fireflies del clearcoat
dotnet run --project src/RayTracer -- -i scena.yaml -C 25

# Onice/alabastro traslucidi: SSS rumoroso → più profondità e campioni
dotnet run --project src/RayTracer -- -i scena.yaml -d 8 -s 256
```
