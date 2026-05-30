# Caustiche focalizzate — preset copia-incolla

Le **caustiche focalizzate**: lo spot luminoso che una lente, una sfera di vetro o
una sfera metallica concentra su una superficie. Sono la luce che gli shadow ray
dritti non sanno produrre, perché richiede di seguire il cammino corretto secondo
Snell (rifrazione) o la legge di riflessione. Il motore le risolve con due
tecniche complementari:

- **MNEE** (Manifold Next Event Estimation) — caster **lisci** (vetro/cristallo a
  specchio, metallo lucido): spot netto e definito.
- **SMS** (Specular Manifold Sampling) — caster **rough/frosted** (vetro
  smerigliato, metallo spazzolato): alone morbido e sfumato.

Questo catalogo è un insieme di **oggetti pronti** (entità + materiale, con il
flag caster già impostato) da **copiare e incollare** nelle tue scene. Per il
flusso d'uso generale vedi [`README.md`](README.md); per la teoria
[`../../docs/technical/path-tracing-and-lighting.md`](../../docs/technical/path-tracing-and-lighting.md)
§2.5 (MNEE) e §2.5.1 (SMS).

> **Regola d'oro: le caustiche richiedono DUE opt-in, entrambi necessari.**
> (1) il flag CLI `--caustics on` al render, e (2) i flag YAML `caustic_caster` /
> `caustic_receiver` sulle entità. Manca uno dei due → nessuna caustica e **costo
> zero** (il rendering è identico a prima). I caster oggi devono essere **sfere**
> (anche dentro un `Transform`): box, cilindri, coni, tori, mesh, CSG e heightfield
> **non** funzionano come caster — il loader emette un warning e ricade sullo
> shadow ray normale. Le luci devono essere **area/geometriche** (le luci
> puntiformi/spot/direzionali e il cielo/HDRI non guidano le caustiche).

---

## Come si attivano

**1 — Al render (CLI).**

```bash
# Attivazione esplicita + qualità delle caustiche frosted (SMS)
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- \
  -i scenes/mia-scena.yaml -o renders/out.png \
  -w 1280 -H 800 -s 512 -d 6 \
  --caustics on --sms-samples 8
```

- `--caustics on|off` — default **off**; **on di default** sui preset quality
  `final` / `final-tiny` / `final-small` / `ultra`. Un `--caustics` esplicito ha
  sempre la precedenza sul preset.
- `--sms-samples <n>` — prove stocastiche SMS per connessione su un caster
  **rough** (default **4**, **8** su `final`/`ultra`). Più alto = caustiche
  frosted più **morbide e meno rumorose**, render più lento. Nessun effetto sui
  caster lisci (MNEE) o con `--caustics off`.

**2 — Sulle entità (YAML).** Marca chi focalizza e chi raccoglie:

```yaml
entities:
  - type: "sphere"            # ← chi focalizza la luce
    center: [0, 1.2, 0]
    radius: 1.0
    material: "vetro_limpido"
    caustic_caster: true
world:
  ground:
    type: "infinite_plane"    # ← chi raccoglie la caustica
    material: "pavimento"
    y: 0
    caustic_receiver: true
```

### Flag YAML

| Chiave | Default | Dove | Effetto |
|--------|---------|------|---------|
| `caustic_caster` | `false` | su un'entità **sfera** | L'oggetto focalizza la luce sui receiver. Liscio → MNEE; rough → SMS. Richiede `--caustics on`. |
| `caustic_receiver` | `false` | su un'entità o su `world.ground` | La superficie su cui le caustiche vengono raccolte. Solo i punti marcati pagano il costo del walk. |

---

## Quando usarle (e quanto costano)

Usale per: una **gemma o sfera di vetro/cristallo** che concentra uno spot
luminoso su un piano; una **sfera metallica** che proietta una caustica
riflessiva; il vetro **smerigliato** che proietta un alone soffuso. Costo **zero**
quando non sono attive o senza entità marcate; quando attive, paga solo il
ricevente: i pixel `caustic_receiver` che vedono un caster **rough** costano circa
`N×` (con `N = --sms-samples`) una connessione MNEE liscia.

### Vincoli (precisi)

- **Caster: solo sfere** (anche scalate a ellissoide dentro un `Transform`). Ogni
  altra primitiva flaggata `caustic_caster` viene **ignorata con warning**.
- **Materiale del caster rifrattivo**: `dielectric`, oppure `disney` con
  `spec_trans ≥ 0.5` e **non** thin-walled. `roughness ≤ 0.04` → MNEE liscio;
  `roughness > 0.04` → SMS frosted.
- **Materiale del caster riflessivo**: `metal` (qualunque `fuzz`), oppure `disney`
  con `metallic ≈ 1`. `fuzz = 0` (o roughness ≤ 0.04) → specchio liscio (MNEE);
  altrimenti SMS riflessivo.
- **Receiver**: qualunque superficie, ma rende meglio su **diffuso/matte**
  (`lambertian` o Disney poco speculare). Un receiver a specchio/vetro non mostra
  uno spot leggibile.
- **Luci**: solo **area/geometriche**. Luce piccola e luminosa → caustica più
  **nitida**; luce grande → caustica più **morbida**.

---

# Sezione A — Caster rifrattivi (vetro / cristallo)

## A1. Sfera di vetro limpido — lente MNEE

```yaml
entities:
  - type: "sphere"
    center: [0, 1.2, 0]
    radius: 1.0
    material: "vetro_limpido"
    caustic_caster: true
materials:
  - id: "vetro_limpido"
    type: "dielectric"
    refraction_index: 1.5
```

La lente di riferimento: vetro liscio `ior 1.5`, risolto da MNEE. Concentra la
luce dell'area light in uno **spot netto e brillante** sul pavimento, con l'ombra
attorno correttamente più scura (l'energia è concentrata, non sparsa). È il caso
più nitido ed economico (1 connessione per shadow sample).

## A2. Sfera di cristallo ad alto IOR — caustica stretta e intensa

```yaml
entities:
  - type: "sphere"
    center: [0, 1.2, 0]
    radius: 1.0
    material: "cristallo"
    caustic_caster: true
materials:
  - id: "cristallo"
    type: "disney"
    color: [0.99, 0.99, 1.0]
    spec_trans: 1.0
    roughness: 0.0
    ior: 1.9
```

IOR più alto (cristallo al piombo ~1.7, fino a ~2.0) piega di più la luce: il
fuoco si stringe e lo **spot diventa più piccolo e intenso**. Ottimo per gemme e
fermacarte di cristallo. Oltre ~2.0 (diamante 2.42) il fuoco può cadere molto
vicino alla sfera.

## A3. Sfera di vetro colorato — caustica tinta (Beer-Lambert)

```yaml
entities:
  - type: "sphere"
    center: [0, 1.2, 0]
    radius: 1.0
    material: "rubino"
    caustic_caster: true
materials:
  - id: "rubino"
    type: "disney"
    color: [0.95, 0.96, 1.0]
    spec_trans: 1.0
    roughness: 0.0
    ior: 1.77
    transmission_color: [0.80, 0.05, 0.06]   # colore raggiunto a transmission_depth
    transmission_depth: 0.8                   # più sottile = colore più chiaro
```

L'assorbimento volumetrico interno (Beer-Lambert: `transmission_color` +
`transmission_depth`) **tinge la caustica** del colore della gemma: lo spot
proiettato diventa rosso rubino. Per uno smeraldo usa
`transmission_color: [0.05, 0.55, 0.20]`, per uno zaffiro `[0.05, 0.12, 0.7]` con
`ior: 1.77`. Il colore è fisico: dipende dallo spessore di vetro attraversato.

## A4. Sfera di vetro frosted — alone morbido (SMS)

```yaml
entities:
  - type: "sphere"
    center: [0, 1.2, 0]
    radius: 1.0
    material: "vetro_smerigliato"
    caustic_caster: true
materials:
  - id: "vetro_smerigliato"
    type: "disney"
    color: [0.96, 0.98, 1.0]
    spec_trans: 1.0
    roughness: 0.14        # > 0.04 ⇒ attiva SMS
    ior: 1.5
```

Vetro smerigliato: la `roughness > 0.04` attiva **SMS**, che diffonde il fuoco in
un **alone soffuso** invece dello spot netto della lente liscia. `roughness`
~0.08–0.12 = appena velato; ~0.15–0.20 = decisamente lattiginoso. Alza
`--sms-samples` (8–16) per smorzare il rumore tipico del frosted.

---

# Sezione B — Caster riflessivi (metallo)

## B1. Sfera di metallo lucido — caustica riflessiva netta (MNEE)

```yaml
entities:
  - type: "sphere"
    center: [0, 1.0, 0]
    radius: 0.9
    material: "specchio"
    caustic_caster: true
materials:
  - id: "specchio"
    type: "metal"
    color: [0.95, 0.95, 0.96]
    fuzz: 0.0
```

Specchio perfetto (`fuzz: 0`): MNEE traccia la riflessione e proietta una
**caustica riflessiva netta** — l'arco di luce che una sfera cromata getta accanto
alla propria ombra. La tinta dello spot segue il `color` del metallo (oro
`[0.95, 0.78, 0.45]`, rame `[0.95, 0.64, 0.54]`).

## B2. Sfera di metallo spazzolato — caustica riflessiva sfocata (SMS)

```yaml
entities:
  - type: "sphere"
    center: [0, 1.0, 0]
    radius: 0.9
    material: "metallo_spazzolato"
    caustic_caster: true
materials:
  - id: "metallo_spazzolato"
    type: "metal"
    color: [0.95, 0.78, 0.45]
    fuzz: 0.12             # > 0 ⇒ attiva SMS riflessivo (α = fuzz²)
```

Metallo spazzolato/satinato (`fuzz > 0`): SMS sfuma la caustica riflessiva in una
banda morbida e calda. `fuzz` ~0.08–0.12 = satinato fine; ~0.15–0.2 = decisamente
diffuso. Anche qui `--sms-samples` alto pulisce il rumore.

---

# Sezione C — Receiver e luce

## C1. Pavimento ricevente (diffuso)

```yaml
world:
  ground:
    type: "infinite_plane"
    material: "pavimento_opaco"
    y: 0
    caustic_receiver: true
materials:
  - id: "pavimento_opaco"
    type: "lambertian"
    color: [0.7, 0.7, 0.72]
```

Il ricevente ideale: un piano **diffuso** chiaro mostra la caustica con il massimo
contrasto. Funziona identico su un'entità qualsiasi (basta `caustic_receiver:
true`); evita receiver a specchio/vetro, dove lo spot non è leggibile.

## C2. Area light per caustiche

```yaml
lights:
  - type: area
    corner: [-0.4, 6.0, -0.4]   # luce piccola e alta = caustica nitida
    u: [0.8, 0.0, 0.0]
    v: [0.0, 0.0, 0.8]
    color: [1.0, 0.97, 0.92]
    intensity: 80.0
    shadow_samples: 6
```

La nitidezza della caustica dipende dalla **dimensione** della luce: piccola e
luminosa (come qui, 0.8×0.8) → fuoco definito; grande (es. 2.4×2.4) → caustica più
morbida e penombra ampia. Alza `shadow_samples` per ammorbidire i bordi. Solo le
luci `area`/geometriche guidano le caustiche.

---

# Sezione D — Mini-scena completa (pronta da renderizzare)

Scena minima che combina un caster di vetro frosted, un ricevente diffuso e una
area light. Salvala come `scenes/mia-caustica.yaml` e renderizza con
`--caustics on`. (Una versione con anche un caster metallico è in
[`../sms-caustics.yaml`](../sms-caustics.yaml).)

```yaml
camera:
  position: [0, 4.5, 6]
  look_at: [0, 0.6, 0]
  fov: 40

world:
  sky:
    type: "flat"
    color: [0.02, 0.02, 0.03]
  ground:
    type: "infinite_plane"
    material: "pavimento_opaco"
    y: 0
    caustic_receiver: true

entities:
  - type: "sphere"
    center: [0, 1.3, 0]
    radius: 1.0
    material: "vetro_smerigliato"
    caustic_caster: true

lights:
  - type: area
    corner: [-0.4, 6.0, -0.4]
    u: [0.8, 0.0, 0.0]
    v: [0.0, 0.0, 0.8]
    color: [1.0, 0.97, 0.92]
    intensity: 80.0
    shadow_samples: 6

materials:
  - id: "pavimento_opaco"
    type: "lambertian"
    color: [0.7, 0.7, 0.72]
  - id: "vetro_smerigliato"
    type: "disney"
    color: [0.96, 0.98, 1.0]
    spec_trans: 1.0
    roughness: 0.12
    ior: 1.5
```

```bash
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- \
  -i scenes/mia-caustica.yaml -o renders/mia-caustica.png \
  -q final --sms-samples 8
```

---

## Matrice decisionale

| Caso d'uso | Preset | Note chiave |
|------------|--------|-------------|
| Lente di vetro, spot netto | A1 `vetro_limpido` (`dielectric`) | MNEE, costo minimo |
| Gemma/cristallo, fuoco stretto | A2 `cristallo` (`ior 1.7–2.0`) | spot piccolo e intenso |
| Caustica colorata (rubino/smeraldo) | A3 `rubino` (`transmission_*`) | tinta fisica Beer-Lambert |
| Vetro smerigliato, alone morbido | A4 `vetro_smerigliato` (`roughness 0.1–0.2`) | SMS, alza `--sms-samples` |
| Specchio, arco riflessivo netto | B1 `specchio` (`metal fuzz 0`) | MNEE riflessivo |
| Metallo satinato, banda sfocata | B2 `metallo_spazzolato` (`fuzz 0.1–0.15`) | SMS riflessivo |
| Superficie che raccoglie | C1 `pavimento_opaco` | diffuso, `caustic_receiver: true` |

## CLI tips

```bash
# Preview rapida (caustiche on esplicito, poche prove SMS)
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- \
  -i scenes/mia-caustica.yaml -q medium-small --caustics on --sms-samples 4

# Finale: i preset final/ultra attivano già --caustics on (sms-samples 8)
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- \
  -i scenes/mia-caustica.yaml -o renders/final.png -q final

# Caustiche frosted molto pulite (più lento) + clamp fireflies più severo
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- \
  -i scenes/mia-caustica.yaml -q final --sms-samples 16 -C 25
```

- `--sms-samples` alto pulisce il rumore del frosted; `-C` (firefly clamp) più
  basso (es. 25) spegne eventuali spike da vetro/dielettrici.
- Caustiche **off** di default fuori dai preset `final`/`ultra`: usa
  `--caustics on` per le anteprime, o `--caustics off` per disattivarle anche in
  un render `final`.
