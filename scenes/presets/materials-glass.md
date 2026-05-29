# Vetro, gemme, liquidi — preset materiali (copia-incolla)

La famiglia **trasmissiva**: vetri (limpidi, smerigliati, colorati, cristallo),
gemme tagliate (diamante, rubino, zaffiro, smeraldo) e liquidi (acqua, vino,
olio, latte). Tutti i blocchi sono pronti da **incollare** nel `materials:` della
tua scena. Per il flusso d'uso vedi
[`README.md`](README.md); schema completo in
[`../../docs/reference/scene-reference.md`](../../docs/reference/scene-reference.md).

> **Regola d'oro: dichiara sempre `spec_trans`.** Un materiale Disney è opaco
> finché non gli dai `spec_trans: 1.0` (vetro limpido) o un valore intermedio
> (smerigliato/traslucido). **Attenzione all'auto-promozione:** quando il loader
> trova `subsurface_radius` e **nessun** `spec_trans`, promuove automaticamente
> `spec_trans` a 1.0 (serve ad attivare il lobo di trasmissione che alimenta il
> medium SSS random-walk). Quindi per latte, ghiaccio torbido e simili **autora
> esplicitamente** `spec_trans` per controllare quanta luce passa davvero. Il
> colore in trasmissione di vetri colorati e liquidi scuri è fisico (Beer-Lambert):
> `transmission_color` + `transmission_depth` — più sottile lo strato, più chiaro
> il colore. Vedi [`../../docs/technical/path-tracing-and-lighting.md`](../../docs/technical/path-tracing-and-lighting.md).

---

## Schema rilevante per la trasmissione

```yaml
materials:
  - id: "glass_id"
    type: "disney"
    color: [0.98, 0.99, 1.0]        # tinta superficiale (Fresnel + base)
    # ── Trasmissione ─────────────────────────────────────────────────────
    spec_trans: 1.0                 # 0 opaco · 1 vetro limpido · 0.4-0.9 frosted/torbido
    ior: 1.52                        # aria 1.0 · acqua 1.33 · vetro 1.5-1.52
                                     # cristallo 1.65 · zaffiro 1.77 · diamante 2.42
    roughness: 0.0                   # 0 = limpido a specchio · ≥ 0.25 = smerigliato
    specular: 0.8                    # intensità del Fresnel speculare di superficie
    # ── Assorbimento volumetrico (Beer-Lambert) ──────────────────────────
    transmission_color: [0.85, 0.05, 0.04]   # colore raggiunto a transmission_depth
    transmission_depth: 1.0          # distanza (unità scena) a cui si satura il colore
    # ── Iridescenza a film sottile (gemme, bolle) ────────────────────────
    thin_film_thickness: 480         # nm (0 disattiva) · 100-800 = iridescenza
    thin_film_ior: 1.45
    # ── Sottosuperficie (latte, cera, ghiaccio torbido) ──────────────────
    subsurface_radius: [0.50, 0.55, 0.65]    # MFP per canale RGB (unità scena)
    subsurface_anisotropy: 0.8       # HG g del medium auto-costruito
    # ── Strato lucido (faccette gemma) ───────────────────────────────────
    clearcoat: 0.3
    coat_roughness: 0.05
    coat_ior: 1.5
```

---

# Sezione A — Vetri

## A1. Vetro limpido (soda-lime / finestra)

```yaml
materials:
  - id: "vetro_limpido"
    type: "disney"
    color: [0.98, 0.99, 1.0]
    roughness: 0.0
    spec_trans: 1.0
    ior: 1.52
    specular: 0.8
```

Il vetro più comune: finestre, bicchieri, lastre. `spec_trans: 1.0` +
`roughness: 0.0` + `ior: 1.52` = trasparenza limpida a specchio. Niente
`transmission_color`: il vetro è otticamente neutro.

## A2. Vetro float (leggera tinta verde sul bordo)

```yaml
materials:
  - id: "vetro_float"
    type: "disney"
    color: [0.92, 0.97, 0.94]
    roughness: 0.0
    spec_trans: 1.0
    ior: 1.52
    specular: 0.8
    transmission_color: [0.92, 0.97, 0.94]
    transmission_depth: 8.0
```

Vetro architettonico: incolore di faccia, la classica tinta verde sul taglio
emerge solo nello spessore. `transmission_depth: 8.0` alto = il verde si vede
solo attraversando molto vetro.

## A3. Vetro smerigliato (frosted)

```yaml
materials:
  - id: "vetro_frosted"
    type: "disney"
    color: [0.92, 0.92, 0.92]
    roughness: 0.42
    spec_trans: 0.65
    ior: 1.52
    specular: 0.45
    transmission_color: [0.92, 0.92, 0.92]
    transmission_depth: 0.5
    bump_map:
      texture:
        type: "voronoi"
        scale: 80.0
        smoothness: 0.6
        colors: [[0.42, 0.42, 0.42], [0.58, 0.58, 0.58]]
      strength: 0.5
```

Sabbiatura reale: la chiave è alzare `roughness` (0.42) e abbassare `spec_trans`
(0.65) — la luce trasmessa si diffonde, l'immagine dietro si sfoca. Il `bump_map`
voronoi aggiunge la grana microscopica. Per privacy più spinta sali ancora di
roughness.

## A4. Cristallo al piombo (lead crystal)

```yaml
materials:
  - id: "cristallo_piombo"
    type: "disney"
    color: [0.97, 0.97, 1.0]
    roughness: 0.0
    spec_trans: 1.0
    ior: 1.65
    specular: 0.90
```

Cristallo PbO > 24%: l'`ior: 1.65` più alto del vetro comune dà i riflessi
brillanti e la rifrazione marcata dei calici e dei lampadari. `specular: 0.90`
per il "fuoco" da taglio.

# Sezione B — Vetri colorati (Beer-Lambert profondo)

I vetri colorati hanno assorbimento volumetrico marcato: il colore in trasmissione
varia con lo spessore. `transmission_color` + `transmission_depth` corto producono
la saturazione fisica corretta.

## B1. Ambra (bottiglia farmacia / whisky)

```yaml
materials:
  - id: "vetro_ambra"
    type: "disney"
    color: [0.90, 0.60, 0.12]
    roughness: 0.0
    spec_trans: 1.0
    ior: 1.52
    specular: 0.78
    transmission_color: [0.85, 0.50, 0.06]
    transmission_depth: 1.5
```

Giallo-arancio caldo da bottiglia medicinale. `transmission_depth: 1.5` dà
l'assorbimento giusto per il vetro spesso di una bottiglia.

## B2. Verde bottiglia

```yaml
materials:
  - id: "vetro_verde"
    type: "disney"
    color: [0.15, 0.50, 0.18]
    roughness: 0.0
    spec_trans: 1.0
    ior: 1.52
    specular: 0.75
    transmission_color: [0.08, 0.45, 0.12]
    transmission_depth: 1.2
```

Il classico verde scuro da bottiglia di vino. `transmission_depth` corto (1.2)
= colore saturo già su spessore modesto.

## B3. Blu cobalto

```yaml
materials:
  - id: "vetro_blu"
    type: "disney"
    color: [0.08, 0.15, 0.85]
    roughness: 0.0
    spec_trans: 1.0
    ior: 1.52
    specular: 0.8
    transmission_color: [0.04, 0.10, 0.78]
    transmission_depth: 1.0
```

Blu profondo da ossido di cobalto (bottiglie deco, vetrate). Saturazione intensa
con `transmission_depth: 1.0`.

## B4. Cristallo fumé (smoked / tinta neutra)

```yaml
materials:
  - id: "cristallo_fume"
    type: "disney"
    color: [0.55, 0.52, 0.50]
    roughness: 0.0
    spec_trans: 1.0
    ior: 1.65
    specular: 0.85
    transmission_color: [0.45, 0.42, 0.40]
    transmission_depth: 3.0
```

Vetro fumé: tinta grigia neutra che scurisce senza colorare. `ior: 1.65` da
cristallo, `transmission_depth: 3.0` per un grigio dosato (tavoli, separatori).

# Sezione C — Gemme

## C1. Diamante

```yaml
materials:
  - id: "diamante"
    type: "disney"
    color: [0.98, 0.98, 1.0]
    roughness: 0.0
    spec_trans: 1.0
    ior: 2.42
    specular: 1.0
    clearcoat: 0.3
    coat_roughness: 0.05
    coat_ior: 1.5
```

L'`ior: 2.42` estremo è ciò che produce il fuoco interno e le riflessioni totali
multiple del diamante. `specular: 1.0` + `clearcoat` leggero esaltano lo scintillio
delle faccette. Vuole `-d` alto (vedi CLI tips): la luce rimbalza molte volte dentro.

## C2. Rubino (corindone rosso)

```yaml
materials:
  - id: "rubino"
    type: "disney"
    color: [0.78, 0.06, 0.10]
    roughness: 0.04
    spec_trans: 0.92
    ior: 1.770
    specular: 0.60
    transmission_color: [0.62, 0.04, 0.06]
    transmission_depth: 0.3
    thin_film_thickness: 280
    thin_film_ior: 1.55
```

Corindone rosso, `ior: 1.77`. `transmission_depth: 0.3` molto corto = rosso
saturo già su pochi millimetri. Il `thin_film` aggiunge i sub-flash colorati
tipici delle gemme tagliate.

## C3. Zaffiro blu

```yaml
materials:
  - id: "zaffiro_blu"
    type: "disney"
    color: [0.10, 0.22, 0.62]
    roughness: 0.04
    spec_trans: 0.92
    ior: 1.770
    specular: 0.60
    transmission_color: [0.05, 0.15, 0.55]
    transmission_depth: 0.3
    thin_film_thickness: 540
    thin_film_ior: 1.55
```

Stesso corindone del rubino (`ior: 1.77`), tinta blu profonda. Il `thin_film` a
540 nm dà il "silk" sericeo che riflette l'azzurro.

## C4. Smeraldo (berillo verde)

```yaml
materials:
  - id: "smeraldo"
    type: "disney"
    color: [0.18, 0.62, 0.32]
    roughness: 0.05
    spec_trans: 0.92
    ior: 1.580
    specular: 0.60
    transmission_color: [0.08, 0.55, 0.22]
    transmission_depth: 0.4
```

Berillo verde, `ior: 1.58` più basso di rubino/zaffiro (meno brillante, più
vellutato — è la firma dello smeraldo). Verde saturo con `transmission_depth: 0.4`.

# Sezione D — Liquidi

Si rendono come **superficie** del liquido. Per profondità volumetriche reali
(es. una pozza torbida) abbina un `medium` per-entità o un `globalMedium`.

## D1. Acqua pura

```yaml
materials:
  - id: "acqua"
    type: "disney"
    color: [0.95, 0.97, 1.0]
    roughness: 0.0
    spec_trans: 1.0
    ior: 1.333
    specular: 0.75
```

`ior: 1.333` è l'indice dell'acqua: rifrazione e Fresnel corretti per bicchieri,
piscine, gocce. Quasi incolore.

## D2. Vino rosso

```yaml
materials:
  - id: "vino_rosso"
    type: "disney"
    color: [0.45, 0.06, 0.10]
    roughness: 0.05
    spec_trans: 1.0
    ior: 1.345
    specular: 0.50
    transmission_color: [0.45, 0.04, 0.06]
    transmission_depth: 0.4
```

Rosso bordeaux profondo. `transmission_depth: 0.4` cortissimo = il vino in un
calice è quasi nero al centro e rosso vivo sui bordi sottili (Beer-Lambert reale).

## D3. Olio d'oliva

```yaml
materials:
  - id: "olio_oliva"
    type: "disney"
    color: [0.92, 0.85, 0.32]
    roughness: 0.05
    spec_trans: 0.78
    ior: 1.467
    specular: 0.45
    transmission_color: [0.78, 0.75, 0.18]
    transmission_depth: 0.6
```

Giallo-verde dorato, `ior: 1.467` da olio. `spec_trans: 0.78` un po' sotto 1
perché l'olio extravergine non è perfettamente limpido.

## D4. Latte (traslucido via SSS)

```yaml
materials:
  - id: "latte"
    type: "disney"
    color: [0.97, 0.95, 0.90]
    spec_trans: 0.30
    subsurface_radius: [0.50, 0.55, 0.65]
    subsurface_anisotropy: 0.8
    roughness: 0.25
    specular: 0.42
```

Il latte **non** è vetro: è quasi opaco per forte diffusione interna. Qui
`spec_trans: 0.30` è autorato **esplicitamente** (altrimenti il loader lo
promuoverebbe a 1.0 vedendo `subsurface_radius`) per tenere il materiale denso;
`subsurface_radius` dà la traslucenza cremosa. Stesso schema per cera e panna.

---

## Matrice decisionale

| Caso d'uso | Preset | Note chiave |
|------------|--------|-------------|
| Finestra / bicchiere limpido | `vetro_limpido` | `spec_trans 1`, `ior 1.52` |
| Lastra architettonica | `vetro_float` | tinta solo nello spessore (`depth 8`) |
| Porta doccia / privacy | `vetro_frosted` | roughness alta, `spec_trans` < 1 |
| Calice / lampadario brillante | `cristallo_piombo` | `ior 1.65` |
| Bottiglia medicinale / whisky | `vetro_ambra` | Beer-Lambert ambra |
| Bottiglia di vino | `vetro_verde` | verde saturo `depth 1.2` |
| Vetrata / deco blu | `vetro_blu` | cobalto `depth 1.0` |
| Tavolo / separatore fumé | `cristallo_fume` | grigio neutro |
| Gioiello con fuoco | `diamante` | `ior 2.42`, alza `-d` |
| Gemma rossa / blu / verde | `rubino`, `zaffiro_blu`, `smeraldo` | `ior` 1.77 / 1.58 |
| Acqua in bicchiere / piscina | `acqua` | `ior 1.333` |
| Calice di vino | `vino_rosso` | `depth 0.4` cortissimo |
| Bottiglia d'olio | `olio_oliva` | `spec_trans 0.78` |
| Latte / cera / panna | `latte` | `spec_trans` esplicito + SSS |

## CLI tips

Le scene trasmissive e con SSS sono rumorose: la luce rimbalza e si rifrange
molte volte. Servono **più profondità** (`-d`), **più campioni** (`-s`) e un
**clamp più basso** (`-C`) per domare i fireflies della trasmissione.

```bash
# Vetro / gemme: la rifrazione multipla richiede profondità alta
dotnet run --project src/RayTracer -c Release -- -i scena.yaml -d 12 -s 512 -C 25

# Diamante (ior 2.42): ancora più rimbalzi interni
dotnet run --project src/RayTracer -c Release -- -i scena.yaml -d 16 -s 512 -C 20

# Liquidi colorati / latte (SSS): più campioni, clamp basso, clamp indiretto stretto
dotnet run --project src/RayTracer -c Release -- -i scena.yaml -d 10 -s 512 -C 25 --indirect-clamp-factor 0.25

# Vetro retroilluminato (vetrata, lampada): più shadow samples
dotnet run --project src/RayTracer -c Release -- -i scena.yaml -d 12 -S 9 -C 20
```
