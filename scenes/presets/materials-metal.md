# Metalli — preset materiali (copia-incolla)

Oro, argento, rame, bronzo, ottone, acciaio, alluminio, cromo, titanio,
ferro/ghisa, anodizzati e qualche rivestimento industriale. Tutti i blocchi sono
pronti da **incollare** nel `materials:` della tua scena.
Per il flusso d'uso vedi [`README.md`](README.md); schema completo
in [`../../docs/reference/scene-reference.md`](../../docs/reference/scene-reference.md).

> **Regola d'oro: un metallo è `base_metalness: 1.0` con tinta nel `base_color`.**
> Su un conduttore la riflessione è colorata (Fresnel del metallo), quindi il
> `base_color` RGB **non** è un albedo diffuso ma la riflettanza a incidenza
> normale (`specular_color` è la tinta verso il radente). La lucidatura la guida
> la `specular_roughness`: specchio ≤ 0.05, satinato 0.15-0.25, spazzolato
> 0.3-0.4, battuto/opaco ≥ 0.5. Le spazzolature direzionali usano
> `specular_roughness_anisotropy` + `specular_rotation` (gradi); le
> anodizzazioni interferenziali usano `thin_film_weight` + `thin_film_thickness`
> (micrometri) + `thin_film_ior`. Il subsurface non si applica ai metalli. I
> metalli "patinati" (ruggine, verderame, corten) abbassano `base_metalness`
> perché il lobo torna diffusivo: lì il `base_color` ritorna un quasi-albedo.

---

## Schema rilevante per i metalli

```yaml
materials:
  - id: "metal_id"
    type: openpbr
    base_metalness: 1.0              # conduttore puro (patine: 0.3-0.7)
    base_color: [1.0, 0.71, 0.29]   # riflettanza speculare (NON albedo)
    specular_roughness: 0.05            # specchio ≤0.05 · satinato 0.2 · spazzolato 0.35
    # ── Spazzolatura direzionale ─────────────────────────────────────────
    # ── Film d'interferenza (anodizzazioni) ──────────────────────────────
    # ── Pelle lucida sopra il metallo (anodizzato nero, cromo coat) ───────
    # NB: niente subsurface_radius sui metalli — è ignorato con metallic>0
```

---

# Sezione A — Metalli preziosi (oro, argento)

## A1. Oro 24K lucido

```yaml
materials:
  - id: "oro_lucido"
    type: openpbr
    base_color: [1.0, 0.71, 0.29]
    base_metalness: 1.0
    specular_roughness: 0.05
```

Oro puro, caldo, quasi a specchio. La tinta satura dell'oro vive interamente nel
`base_color`; la `specular_roughness 0.05` dà il riflesso netto da gioielleria.

## A2. Oro rosa (lega Au-Cu)

```yaml
materials:
  - id: "oro_rosa"
    type: openpbr
    base_color: [0.92, 0.58, 0.42]
    base_metalness: 1.0
    specular_roughness: 0.10
```

Tono salmone elegante. Lo `specular_color` rosato spinge anche il Fresnel verso il
colore base, accentuando il rosato sui bordi.

## A3. Oro spazzolato (anisotropo)

```yaml
materials:
  - id: "oro_spazzolato"
    type: openpbr
    base_color: [0.95, 0.67, 0.26]
    base_metalness: 1.0
    specular_roughness: 0.35
    specular_roughness_anisotropy: 0.63
```

Finitura industriale direzionale: la `specular_roughness_anisotropy 0.63` allunga i riflessi
lungo la tangente. Ruota la striatura con `specular_rotation` (gradi: 90 = un
quarto di giro).

## A4. Argento sterling lucido

```yaml
materials:
  - id: "argento_lucido"
    type: openpbr
    base_color: [0.97, 0.96, 0.92]
    base_metalness: 1.0
    specular_roughness: 0.03
```

Argento 925 quasi a specchio, leggermente più freddo dell'alluminio. Per
posateria e gioielleria.

## A5. Argento spazzolato

```yaml
materials:
  - id: "argento_spazzolato"
    type: openpbr
    base_color: [0.90, 0.89, 0.86]
    base_metalness: 1.0
    specular_roughness: 0.32
    specular_roughness_anisotropy: 0.54
```

Posateria e finiture contemporanee con spazzolatura lineare morbida.

# Sezione B — Rame, bronzo, ottone

## B1. Rame lucido

```yaml
materials:
  - id: "rame_lucido"
    type: openpbr
    base_color: [0.95, 0.64, 0.54]
    base_metalness: 1.0
    specular_roughness: 0.06
```

Rame appena lucidato, riflesso caldo saturo. Il `base_color` mantiene il
rosato anche nei riflessi speculari.

## B2. Bronzo lucido

```yaml
materials:
  - id: "bronzo_lucido"
    type: openpbr
    base_color: [0.80, 0.50, 0.20]
    base_metalness: 1.0
    specular_roughness: 0.10
```

Bronzo appena fuso e lucidato, tono ambra-dorato più scuro dell'ottone.

## B3. Ottone lucido

```yaml
materials:
  - id: "ottone_lucido"
    type: openpbr
    base_color: [0.88, 0.70, 0.22]
    base_metalness: 1.0
    specular_roughness: 0.08
```

Maniglie, strumenti musicali, ferramenta. Più giallo-verdognolo dell'oro.

## B4. Ottone spazzolato

```yaml
materials:
  - id: "ottone_spazzolato"
    type: openpbr
    base_color: [0.82, 0.65, 0.18]
    base_metalness: 1.0
    specular_roughness: 0.35
    specular_roughness_anisotropy: 0.585
```

Finitura d'arredo contemporaneo, riflesso direzionale soft.

## B5. Rame patinato heritage (verderame, `color_ramp`)

```yaml
materials:
  - id: "rame_patinato"
    type: openpbr
    base_metalness: 0.65
    specular_roughness: 0.62
    specular_weight: 0.84
    base_color:
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

Tetti verde-arancio invecchiati. La `base_metalness 0.65` abbassata porta il materiale
verso il diffusivo (la patina non è un conduttore puro); la `color_ramp` 4-stop
mescola verderame e rame esposto.

# Sezione C — Acciaio (lucido + spazzolato)

## C1. Acciaio inox lucido (specchio chirurgico)

```yaml
materials:
  - id: "acciaio_lucido"
    type: openpbr
    base_color: [0.58, 0.57, 0.55]
    base_metalness: 1.0
    specular_roughness: 0.04
```

Inox a specchio, neutro leggermente caldo. Per elettrodomestici, strumenti,
superfici riflettenti.

## C2. Acciaio spazzolato (anisotropia netta)

```yaml
materials:
  - id: "acciaio_spazzolato"
    type: openpbr
    base_color: [0.54, 0.53, 0.51]
    base_metalness: 1.0
    specular_roughness: 0.38
    specular_roughness_anisotropy: 0.765
```

La finitura industriale per eccellenza. La `specular_roughness_anisotropy 0.765` produce la classica
striatura direzionale; per piani circolari (dischi, piastre hi-fi) ruota con
`specular_rotation`.

## C3. Acciaio carbonioso

```yaml
materials:
  - id: "acciaio_carbonioso"
    type: openpbr
    base_color: [0.32, 0.31, 0.30]
    base_metalness: 1.0
    specular_roughness: 0.30
```

Alto carbonio, tono più scuro e meno riflettente dell'inox. Utensili, lame,
componenti meccanici.

# Sezione D — Alluminio, cromo, titanio

## D1. Alluminio lucido

```yaml
materials:
  - id: "alluminio_lucido"
    type: openpbr
    base_color: [0.91, 0.91, 0.92]
    base_metalness: 1.0
    specular_roughness: 0.05
```

Foglio tirato a specchio, neutro leggermente freddo. Per scocche, profili,
packaging.

## D2. Cromo specchio

```yaml
materials:
  - id: "cromo_specchio"
    type: openpbr
    base_color: [0.95, 0.93, 0.88]
    base_metalness: 1.0
    specular_roughness: 0.03
```

Riflesso perfetto: rubinetteria, paraurti, dettagli auto. `base_metalness 1` +
`specular_roughness 0.03` = quasi specchio puro → clampa i fireflies (vedi CLI tips).

## D3. Titanio naturale

```yaml
materials:
  - id: "titanio_naturale"
    type: openpbr
    base_color: [0.62, 0.60, 0.56]
    base_metalness: 1.0
    specular_roughness: 0.25
```

Grigio caldo a finitura industriale satinata, leggermente più scuro
dell'acciaio. Orologeria, aerospazio, telai.

## D4. Titanio anodizzato blu (thin-film)

```yaml
materials:
  - id: "titanio_anodizzato_blu"
    type: openpbr
    base_color: [0.78, 0.80, 0.82]
    base_metalness: 1.0
    specular_roughness: 0.18
    specular_roughness_anisotropy: 0.36
    thin_film_thickness: 0.54
    thin_film_ior: 1.55
    thin_film_weight: 1
```

Il blu interferenziale **non** è nel `color` ma nello strato a 540 nm: cambia il
colore variando `thin_film_thickness` (~250 oro, ~420 viola, ~640 verde). La base
resta argento neutro.

# Sezione E — Ferro e ghisa (opachi, battuti)

## E1. Ferro battuto

```yaml
materials:
  - id: "ferro_battuto"
    type: openpbr
    base_color: [0.30, 0.28, 0.26]
    base_metalness: 0.85
    specular_roughness: 0.62
    specular_weight: 0.7
```

Forgiato a mano, scuro, riflesso diffuso. Cancellate, lampade, dettagli rustici.

## E2. Ghisa stagionata (padella ben usata)

```yaml
materials:
  - id: "ghisa_stagionata"
    type: openpbr
    base_color: [0.12, 0.11, 0.10]
    base_metalness: 0.80
    specular_roughness: 0.50
    specular_weight: 0.76
```

Quasi-nera, semi-opaca con quel filo di lucido dato dall'olio bruciato.
Pentolame, ghisa da cucina.

# Sezione F — Anodizzato e rivestimenti industriali

## F1. Alluminio anodizzato nero (coat = film d'ossido)

```yaml
materials:
  - id: "alluminio_anodizzato_nero"
    type: openpbr
    base_color: [0.04, 0.04, 0.05]
    base_metalness: 0.92
    specular_roughness: 0.20
    specular_weight: 1.3
    coat_weight: 0.25
    coat_roughness: 0.18
    coat_ior: 1.5
```

Elettronica, ottiche, custodie. Il `coat_weight 0.25` simula il sottile film
d'ossido anodico sopra il metallo scuro.

## F2. Vernice a polvere RAL 9005 nero (powder-coat)

```yaml
materials:
  - id: "powdercoat_nero"
    type: openpbr
    base_color: [0.04, 0.04, 0.04]
    specular_roughness: 0.45
    specular_weight: 0.76
    coat_weight: 0.50
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

Polvere a cottura sugli infissi. **Non** è metallica (`base_metalness` assente = 0): la
vernice copre il metallo, quindi `base_color` è un albedo. Il `bump_map` voronoi a
scala 100 dà la granulometria caratteristica.

## F3. Acciaio zincato galvanizzato

```yaml
materials:
  - id: "zinco_galvanizzato"
    type: openpbr
    base_metalness: 0.92
    base_color: [0.65, 0.65, 0.68]
    specular_roughness: 0.35
```

Rivestimento anti-ruggine grigio satinato, leggermente bluastro. Per recinzioni,
lamiere, carpenteria esterna.

## F4. Acciaio dipinto rosso traffico (RAL 3020)

```yaml
materials:
  - id: "acciaio_dipinto_rosso"
    type: openpbr
    base_color: [0.78, 0.10, 0.08]
    specular_roughness: 0.42
    specular_weight: 0.8
    coat_weight: 0.50
    coat_roughness: 0.18
    coat_ior: 1.5
```

Acciaio verniciato a smalto: anche qui `base_metalness 0` perché lo strato di vernice è
dielettrico. Segnaletica, macchinari, idranti.

## F5. Acciaio arrugginito (ruggine → metalness bassa)

```yaml
materials:
  - id: "acciaio_arrugginito"
    type: openpbr
    base_color: [0.55, 0.28, 0.15]
    base_metalness: 0.35
    specular_roughness: 0.85
    specular_weight: 0.3
```

Ossidazione superficiale: la ruggine è una crosta porosa diffusiva, quindi
`base_metalness 0.35` e `specular_roughness` alta. Ottimo come base per un `mix` ruggine→vernice
(vedi [`materials-weathering.md`](materials-weathering.md)).

---

## Matrice decisionale

| Caso d'uso | Preset | Note chiave |
|------------|--------|-------------|
| Gioielleria oro calda | `oro_lucido`, `oro_rosa` | tinta nel base_color, rugosità bassa |
| Posateria/gioielli argento | `argento_lucido`, `argento_spazzolato` | freddo, aniso per spazzolato |
| Rame/bronzo/ottone lucidi | `rame_lucido`, `bronzo_lucido`, `ottone_lucido` | tinta nel base_color |
| Finitura spazzolata direzionale | `oro_spazzolato`, `ottone_spazzolato`, `acciaio_spazzolato` | `specular_roughness_anisotropy` + `specular_rotation` |
| Tetto/dettaglio invecchiato | `rame_patinato` | metalness bassa, color_ramp |
| Elettrodomestici/strumenti inox | `acciaio_lucido`, `acciaio_carbonioso` | neutro |
| Scocca/profilo alluminio | `alluminio_lucido` | freddo neutro |
| Rubinetteria/auto a specchio | `cromo_specchio` | clampa fireflies |
| Orologeria/aerospazio | `titanio_naturale`, `titanio_anodizzato_blu` | thin_film per i colori |
| Ferro/ghisa rustici, cucina | `ferro_battuto`, `ghisa_stagionata` | opachi, riflesso soft |
| Custodia elettronica scura | `alluminio_anodizzato_nero` | coat = ossido |
| Infisso/carpenteria verniciata | `powdercoat_nero`, `acciaio_dipinto_rosso` | metalness 0, vernice dielettrica |
| Lamiera esterna anti-ruggine | `zinco_galvanizzato` | satinato bluastro |
| Metallo invecchiato/ruggine | `acciaio_arrugginito` | base per mix weathering |

## CLI tips

```bash
# Metalli a specchio (cromo, oro, argento lucidi): clampa i fireflies dei riflessi
dotnet run --project src/RayTracer -- -i scena.yaml -C 25

# Anodizzati thin-film / spazzolati anisotropi: sobol + più rimbalzi per i riflessi
dotnet run --project src/RayTracer -- -i scena.yaml --sampler sobol -d 8 -S 6

# Powder-coat / verniciati con coat: depth ≥ 6 per il secondo lobo
dotnet run --project src/RayTracer -- -i scena.yaml -d 6
```

Le striature anisotrope dipendono dalle tangenti UV della geometria: su una
sfera ruota l'effetto con `specular_rotation`, su mesh assicurati che le UV
siano coerenti con la direzione di spazzolatura desiderata.
