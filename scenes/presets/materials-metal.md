# Metalli — preset materiali (copia-incolla)

Oro, argento, rame, bronzo, ottone, acciaio, alluminio, cromo, titanio,
ferro/ghisa, anodizzati e qualche rivestimento industriale. Tutti i blocchi sono
pronti da **incollare** nel `materials:` della tua scena — non importare nulla.
Per la filosofia e il flusso d'uso vedi [`README.md`](README.md); schema completo
in [`../../docs/reference/scene-reference.md`](../../docs/reference/scene-reference.md).

> **Regola d'oro: un metallo è `metallic: 1.0` con tinta nel `color`.** Su un
> conduttore la riflessione è colorata (Fresnel del metallo), quindi la `color`
> RGB **non** è un albedo diffuso ma la riflettanza speculare. La lucidatura la
> guida la `roughness`: specchio ≤ 0.05, satinato 0.15-0.25, spazzolato 0.3-0.4,
> battuto/opaco ≥ 0.5. Le spazzolature direzionali usano `anisotropic` +
> `anisotropic_rotation`; le anodizzazioni interferenziali usano
> `thin_film_thickness` + `thin_film_ior`. **Mai** mettere `subsurface_radius`
> su un metallo: con `metallic > 0` viene **ignorato**. I metalli "patinati"
> (ruggine, verderame, corten) abbassano `metallic` perché il lobo torna
> diffusivo: lì la `color` ritorna un quasi-albedo.

---

## Schema rilevante per i metalli

```yaml
materials:
  - id: "metal_id"
    type: "disney"
    metallic: 1.0              # conduttore puro (patine: 0.3-0.7)
    color: [1.0, 0.71, 0.29]   # riflettanza speculare (NON albedo)
    roughness: 0.05            # specchio ≤0.05 · satinato 0.2 · spazzolato 0.35
    specular: 0.8              # intensità del lobo speculare
    specular_tint: 0.0         # tinge il Fresnel verso color (utile su Cu/Au antichi)
    # ── Spazzolatura direzionale ─────────────────────────────────────────
    anisotropic: 0.0           # 0 isotropo · 0.6-0.9 spazzolato netto
    anisotropic_rotation: 0.0  # 0..1 = frazione di 2π attorno alla normale
    # ── Film d'interferenza (anodizzazioni) ──────────────────────────────
    thin_film_thickness: 0     # nm: 0 disabilita · oro ~250-380 · blu ~540 · verde ~640
    thin_film_ior: 1.55        # IOR del film
    # ── Pelle lucida sopra il metallo (anodizzato nero, cromo coat) ───────
    clearcoat: 0.0             # secondo lobo speculare (vernice/ossido)
    coat_roughness: 0.04
    coat_ior: 1.5
    # NB: niente subsurface_radius sui metalli — è ignorato con metallic>0
```

---

# Sezione A — Metalli preziosi (oro, argento)

## A1. Oro 24K lucido

```yaml
materials:
  - id: "oro_lucido"
    type: "disney"
    color: [1.0, 0.71, 0.29]
    metallic: 1.0
    roughness: 0.05
    specular: 0.8
```

Oro puro, caldo, quasi a specchio. La tinta satura dell'oro vive interamente nel
`color`; la `roughness 0.05` dà il riflesso netto da gioielleria.

## A2. Oro rosa (lega Au-Cu)

```yaml
materials:
  - id: "oro_rosa"
    type: "disney"
    color: [0.92, 0.58, 0.42]
    metallic: 1.0
    roughness: 0.10
    specular: 0.75
    specular_tint: 0.4
```

Tono salmone elegante. Lo `specular_tint 0.4` spinge anche il Fresnel verso il
colore base, accentuando il rosato sui bordi.

## A3. Oro spazzolato (anisotropic)

```yaml
materials:
  - id: "oro_spazzolato"
    type: "disney"
    color: [0.95, 0.67, 0.26]
    metallic: 1.0
    roughness: 0.35
    specular: 0.6
    anisotropic: 0.7
    anisotropic_rotation: 0.0
```

Finitura industriale direzionale: l'`anisotropic 0.7` allunga i riflessi lungo la
tangente. Ruota la striatura con `anisotropic_rotation` (0.25 = 90°).

## A4. Argento sterling lucido

```yaml
materials:
  - id: "argento_lucido"
    type: "disney"
    color: [0.97, 0.96, 0.92]
    metallic: 1.0
    roughness: 0.03
    specular: 0.9
```

Argento 925 quasi a specchio, leggermente più freddo dell'alluminio. Per
posateria e gioielleria.

## A5. Argento spazzolato

```yaml
materials:
  - id: "argento_spazzolato"
    type: "disney"
    color: [0.90, 0.89, 0.86]
    metallic: 1.0
    roughness: 0.32
    specular: 0.65
    anisotropic: 0.6
    anisotropic_rotation: 0.0
```

Posateria e finiture contemporanee con spazzolatura lineare morbida.

# Sezione B — Rame, bronzo, ottone

## B1. Rame lucido

```yaml
materials:
  - id: "rame_lucido"
    type: "disney"
    color: [0.95, 0.64, 0.54]
    metallic: 1.0
    roughness: 0.06
    specular: 0.8
    specular_tint: 0.5
```

Rame appena lucidato, riflesso caldo saturo. Lo `specular_tint` mantiene il
rosato anche nei riflessi speculari.

## B2. Bronzo lucido

```yaml
materials:
  - id: "bronzo_lucido"
    type: "disney"
    color: [0.80, 0.50, 0.20]
    metallic: 1.0
    roughness: 0.10
    specular: 0.7
```

Bronzo appena fuso e lucidato, tono ambra-dorato più scuro dell'ottone.

## B3. Ottone lucido

```yaml
materials:
  - id: "ottone_lucido"
    type: "disney"
    color: [0.88, 0.70, 0.22]
    metallic: 1.0
    roughness: 0.08
    specular: 0.75
```

Maniglie, strumenti musicali, ferramenta. Più giallo-verdognolo dell'oro.

## B4. Ottone spazzolato

```yaml
materials:
  - id: "ottone_spazzolato"
    type: "disney"
    color: [0.82, 0.65, 0.18]
    metallic: 1.0
    roughness: 0.35
    specular: 0.55
    anisotropic: 0.65
    anisotropic_rotation: 0.0
```

Finitura d'arredo contemporaneo, riflesso direzionale soft.

## B5. Rame patinato heritage (verderame, `color_ramp`)

```yaml
materials:
  - id: "rame_patinato"
    type: "disney"
    color: [0.55, 0.62, 0.42]
    metallic: 0.65
    roughness: 0.62
    specular: 0.42
    texture:
      type: "noise"
      scale: 2.5
      noise_type: "fbm"
      octaves: 6
      noise_strength: 0.8
      distortion: 0.25
      randomize_offset: true
      color_ramp:
        - { position: 0.00, color: [0.10, 0.32, 0.22], interp: "smoothstep" }
        - { position: 0.35, color: [0.32, 0.55, 0.42], interp: "smoothstep" }
        - { position: 0.70, color: [0.72, 0.45, 0.22], interp: "smoothstep" }
        - { position: 1.00, color: [0.92, 0.55, 0.28], interp: "linear" }
```

Tetti verde-arancio invecchiati. Il `metallic 0.65` abbassato porta il materiale
verso il diffusivo (la patina non è un conduttore puro); la `color_ramp` 4-stop
mescola verderame e rame esposto.

# Sezione C — Acciaio (lucido + spazzolato)

## C1. Acciaio inox lucido (specchio chirurgico)

```yaml
materials:
  - id: "acciaio_lucido"
    type: "disney"
    color: [0.58, 0.57, 0.55]
    metallic: 1.0
    roughness: 0.04
    specular: 0.85
```

Inox a specchio, neutro leggermente caldo. Per elettrodomestici, strumenti,
superfici riflettenti.

## C2. Acciaio spazzolato (anisotropic netto)

```yaml
materials:
  - id: "acciaio_spazzolato"
    type: "disney"
    color: [0.54, 0.53, 0.51]
    metallic: 1.0
    roughness: 0.38
    specular: 0.55
    anisotropic: 0.85
    anisotropic_rotation: 0.0
```

La finitura industriale per eccellenza. L'`anisotropic 0.85` produce la classica
striatura direzionale; per piani circolari (dischi, piastre hi-fi) ruota con
`anisotropic_rotation`.

## C3. Acciaio carbonioso

```yaml
materials:
  - id: "acciaio_carbonioso"
    type: "disney"
    color: [0.32, 0.31, 0.30]
    metallic: 1.0
    roughness: 0.30
    specular: 0.60
```

Alto carbonio, tono più scuro e meno riflettente dell'inox. Utensili, lame,
componenti meccanici.

# Sezione D — Alluminio, cromo, titanio

## D1. Alluminio lucido

```yaml
materials:
  - id: "alluminio_lucido"
    type: "disney"
    color: [0.91, 0.91, 0.92]
    metallic: 1.0
    roughness: 0.05
    specular: 0.85
```

Foglio tirato a specchio, neutro leggermente freddo. Per scocche, profili,
packaging.

## D2. Cromo specchio

```yaml
materials:
  - id: "cromo_specchio"
    type: "disney"
    color: [0.95, 0.93, 0.88]
    metallic: 1.0
    roughness: 0.03
    specular: 1.0
```

Riflesso perfetto: rubinetteria, paraurti, dettagli auto. `specular 1.0` +
`roughness 0.03` = quasi specchio puro → clampa i fireflies (vedi CLI tips).

## D3. Titanio naturale

```yaml
materials:
  - id: "titanio_naturale"
    type: "disney"
    color: [0.62, 0.60, 0.56]
    metallic: 1.0
    roughness: 0.25
    specular: 0.65
```

Grigio caldo a finitura industriale satinata, leggermente più scuro
dell'acciaio. Orologeria, aerospazio, telai.

## D4. Titanio anodizzato blu (thin-film)

```yaml
materials:
  - id: "titanio_anodizzato_blu"
    type: "disney"
    color: [0.78, 0.80, 0.82]
    metallic: 1.0
    roughness: 0.18
    specular: 0.55
    anisotropic: 0.40
    thin_film_thickness: 540
    thin_film_ior: 1.55
```

Il blu interferenziale **non** è nel `color` ma nello strato a 540 nm: cambia il
colore variando `thin_film_thickness` (~250 oro, ~420 viola, ~640 verde). La base
resta argento neutro.

# Sezione E — Ferro e ghisa (opachi, battuti)

## E1. Ferro battuto

```yaml
materials:
  - id: "ferro_battuto"
    type: "disney"
    color: [0.30, 0.28, 0.26]
    metallic: 0.85
    roughness: 0.62
    specular: 0.35
```

Forgiato a mano, scuro, riflesso diffuso. Cancellate, lampade, dettagli rustici.

## E2. Ghisa stagionata (padella ben usata)

```yaml
materials:
  - id: "ghisa_stagionata"
    type: "disney"
    color: [0.12, 0.11, 0.10]
    metallic: 0.80
    roughness: 0.50
    specular: 0.38
```

Quasi-nera, semi-opaca con quel filo di lucido dato dall'olio bruciato.
Pentolame, ghisa da cucina.

# Sezione F — Anodizzato e rivestimenti industriali

## F1. Alluminio anodizzato nero (clearcoat = film d'ossido)

```yaml
materials:
  - id: "alluminio_anodizzato_nero"
    type: "disney"
    color: [0.04, 0.04, 0.05]
    metallic: 0.92
    roughness: 0.20
    specular: 0.65
    clearcoat: 0.25
    coat_roughness: 0.18
    coat_ior: 1.5
```

Elettronica, ottiche, custodie. Il `clearcoat 0.25` simula il sottile film
d'ossido anodico sopra il metallo scuro.

## F2. Vernice a polvere RAL 9005 nero (powder-coat)

```yaml
materials:
  - id: "powdercoat_nero"
    type: "disney"
    color: [0.04, 0.04, 0.04]
    roughness: 0.45
    specular: 0.38
    clearcoat: 0.50
    coat_roughness: 0.18
    coat_ior: 1.5
    bump_map:
      texture:
        type: "voronoi"
        scale: 100.0
        smoothness: 0.5
        colors: [[0.46, 0.46, 0.46], [0.54, 0.54, 0.54]]
      strength: 0.25
```

Polvere a cottura sugli infissi. **Non** è metallica (`metallic` assente = 0): la
vernice copre il metallo, quindi `color` è un albedo. Il `bump_map` voronoi a
scala 100 dà la granulometria caratteristica.

## F3. Acciaio zincato galvanizzato

```yaml
materials:
  - id: "zinco_galvanizzato"
    type: "disney"
    metallic: 0.92
    color: [0.65, 0.65, 0.68]
    roughness: 0.35
    specular: 0.50
```

Rivestimento anti-ruggine grigio satinato, leggermente bluastro. Per recinzioni,
lamiere, carpenteria esterna.

## F4. Acciaio dipinto rosso traffico (RAL 3020)

```yaml
materials:
  - id: "acciaio_dipinto_rosso"
    type: "disney"
    color: [0.78, 0.10, 0.08]
    roughness: 0.42
    specular: 0.40
    clearcoat: 0.50
    coat_roughness: 0.18
    coat_ior: 1.5
```

Acciaio verniciato a smalto: anche qui `metallic 0` perché lo strato di vernice è
dielettrico. Segnaletica, macchinari, idranti.

## F5. Acciaio arrugginito (ruggine → metallic basso)

```yaml
materials:
  - id: "acciaio_arrugginito"
    type: "disney"
    color: [0.55, 0.28, 0.15]
    metallic: 0.35
    roughness: 0.85
    specular: 0.15
```

Ossidazione superficiale: la ruggine è una crosta porosa diffusiva, quindi
`metallic 0.35` e `roughness` alta. Ottimo come base per un `mix` ruggine→vernice
(vedi [`materials-weathering.md`](materials-weathering.md)).

---

## Matrice decisionale

| Caso d'uso | Preset | Note chiave |
|------------|--------|-------------|
| Gioielleria oro calda | `oro_lucido`, `oro_rosa` | tinta nel color, roughness bassa |
| Posateria/gioielli argento | `argento_lucido`, `argento_spazzolato` | freddo, aniso per spazzolato |
| Rame/bronzo/ottone lucidi | `rame_lucido`, `bronzo_lucido`, `ottone_lucido` | specular_tint sul rame |
| Finitura spazzolata direzionale | `oro_spazzolato`, `ottone_spazzolato`, `acciaio_spazzolato` | `anisotropic` + rotation |
| Tetto/dettaglio invecchiato | `rame_patinato` | metallic basso, color_ramp |
| Elettrodomestici/strumenti inox | `acciaio_lucido`, `acciaio_carbonioso` | neutro |
| Scocca/profilo alluminio | `alluminio_lucido` | freddo neutro |
| Rubinetteria/auto a specchio | `cromo_specchio` | clampa fireflies |
| Orologeria/aerospazio | `titanio_naturale`, `titanio_anodizzato_blu` | thin_film per i colori |
| Ferro/ghisa rustici, cucina | `ferro_battuto`, `ghisa_stagionata` | opachi, riflesso soft |
| Custodia elettronica scura | `alluminio_anodizzato_nero` | clearcoat = ossido |
| Infisso/carpenteria verniciata | `powdercoat_nero`, `acciaio_dipinto_rosso` | metallic 0, vernice dielettrica |
| Lamiera esterna anti-ruggine | `zinco_galvanizzato` | satinato bluastro |
| Metallo invecchiato/ruggine | `acciaio_arrugginito` | base per mix weathering |

## CLI tips

```bash
# Metalli a specchio (cromo, oro, argento lucidi): clampa i fireflies dei riflessi
dotnet run --project src/RayTracer -- -i scena.yaml -C 25

# Anodizzati thin-film / spazzolati anisotropic: sobol + più rimbalzi per i riflessi
dotnet run --project src/RayTracer -- -i scena.yaml --sampler sobol -d 8 -S 6

# Powder-coat / verniciati con clearcoat: depth ≥ 6 per il secondo lobo
dotnet run --project src/RayTracer -- -i scena.yaml -d 6
```

Le striature anisotrope dipendono dalle tangenti UV della geometria: su una
sfera ruota l'effetto con `anisotropic_rotation`, su mesh assicurati che le UV
siano coerenti con la direzione di spazzolatura desiderata.
