# Caustiche focalizzate — preset copia-incolla

Le **caustiche focalizzate** sono lo spot luminoso che una lente, una sfera di
vetro, una superficie d'acqua o uno specchio concentrano su una superficie. Sono
la luce che gli shadow ray dritti non sanno produrre, perché richiede di seguire
il cammino corretto secondo Snell (rifrazione) o la legge di riflessione.

Il motore le risolve con il **photon mapping per caustiche**: un pre-pass emette
fotoni dalle luci, li traccia attraverso le superfici speculari (vetro, acqua,
metallo, specchio) e li deposita dove cadono sulle superfici diffuse; durante il
rendering la camera li raccoglie con una stima di densità k-nearest. È **generale**:
funziona con qualsiasi geometria speculare e con **tutte** le luci, sole/direzionali
incluse.

Questo catalogo è un insieme di **oggetti pronti** (entità + materiale) da
**copiare e incollare** nelle tue scene. Per il flusso d'uso generale vedi
[`README.md`](README.md); per la teoria
[`../../docs/technical/path-tracing-and-lighting.md`](../../docs/technical/path-tracing-and-lighting.md).

> **Regola d'oro: le caustiche richiedono UN solo opt-in — il flag CLI
> `--caustics on`** (di default sui preset `final` / `ultra`). Con quello attivo il
> motore emette i fotoni e focalizza la luce attraverso **ogni** superficie
> speculare/trasmissiva della scena, depositandoli sulle superfici diffuse.
> **Non servono flag YAML**: niente `caustic_caster`, niente `caustic_receiver`,
> nessuna marcatura per-oggetto. Basta avere in scena una geometria speculare (a
> focalizzare) e una superficie diffusa (a ricevere).
>
> Le luci che guidano le caustiche sono **tutte** le finite — `area`/geometriche,
> `sphere`, `point`/`spot` — **e** le **`directional`/sole**. L'ambiente/HDRI non
> contribuisce alle caustiche focalizzate in questa versione.

---

## Come si attivano

**1 — Al render (CLI).**

```bash
# Attivazione esplicita + budget fotoni più alto per caustiche più pulite
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- \
  -i scenes/mia-scena.yaml -o renders/out.png \
  -w 1280 -H 800 -s 512 -d 8 \
  --caustics on --caustic-photons 4000000
```

- `--caustics on|off` — default **off**; **on di default** sui preset quality
  `final` / `final-tiny` / `final-small` / `ultra`. Un `--caustics` esplicito ha
  sempre la precedenza sul preset.
- `--caustic-photons <n>` — numero di fotoni del pre-pass (default **2M**, **3–4M**
  su `final`/`ultra`). Più fotoni = caustiche **più pulite e più nitide**, pre-pass
  più lento e più memoria. Nessun effetto con `--caustics off`.

**2 — Niente flag YAML.** Con `--caustics on` non devi marcare niente: la sfera di
vetro qui sotto focalizza la luce sul pavimento senza alcuna chiave aggiuntiva.

```yaml
entities:
  - name: "lente"
    type: "sphere"              # geometria curva + vetro → focalizza
    center: [0, 1.0, 0]
    radius: 1.0
    material: "vetro"
world:
  ground:
    type: "infinite_plane"      # superficie diffusa → riceve la caustica
    material: "pavimento"
    y: 0
```

---

## Cosa focalizza e cosa riceve

- **Focalizza** (genera la caustica): qualsiasi geometria con materiale
  **speculare/trasmissivo** — vetro/cristallo (`dielectric`, o `disney` con
  `spec_trans`), metallo/specchio (`metal`, o `disney` con `metallic ≈ 1`),
  acqua. Vale per qualunque forma: sfere e lenti concentrano in uno spot,
  cilindri/tori in bande e anelli, specchi piani proiettano riflessi netti.
- **Riceve** (mostra la caustica): qualsiasi superficie **diffusa/matte**
  (`lambertian` o Disney poco speculare). Pavimenti, pareti, il `ground`.
- **Luci**: tutte le finite (`area`/geometriche, `sphere`, `point`/`spot`) **e**
  le `directional`/sole. Una luce piccola e intensa dà una caustica **netta**; una
  grande la diffonde in un **alone morbido**.

> **Limiti noti (onesti).** Le caustiche da **vetro frosted/rough** (rifrazione
> glossy) e da **environment/HDRI** in questa versione ricadono sul path tracer
> ordinario (più rumorose, non focalizzate dal photon map); le caustiche da
> **sole/`directional`** sono invece pienamente supportate. L'assorbimento interno
> (tinta del vetro lungo lo spessore) non è applicato ai fotoni: vetri molto
> colorati possono avere una caustica leggermente meno satura del previsto.

---

## Sezione A — Lenti rifrattive (vetro / cristallo)

### A1. Sfera di vetro limpido — lente netta

```yaml
entities:
  - name: "lente_vetro"
    type: "sphere"
    center: [0, 1.0, 0]
    radius: 1.0
    material: "vetro_limpido"
materials:
  - id: "vetro_limpido"
    type: "dielectric"
    refraction_index: 1.5
```

La lente di riferimento: vetro liscio `ior 1.5`. Concentra la luce in uno spot
luminoso sul pavimento sotto/dietro la sfera. Alza `--caustic-photons` per renderlo
più definito.

### A2. Cristallo ad alto IOR — fuoco stretto e intenso

```yaml
materials:
  - id: "cristallo"
    type: "dielectric"
    refraction_index: 1.8       # IOR alto → fuoco più stretto e brillante
```

Un IOR più alto piega di più i raggi: la caustica diventa più piccola e intensa.

### A3. Vetro colorato — caustica tinta

```yaml
materials:
  - id: "vetro_rubino"
    type: "disney"
    base_color: [0.85, 0.05, 0.08]
    spec_trans: 1.0
    roughness: 0.0
    ior: 1.5
```

Il vetro Disney trasmissivo proietta una caustica colorata (rubino, smeraldo,
zaffiro cambiando `base_color`). Tieni `roughness: 0.0` per un fuoco nitido.

---

## Sezione B — Specchi e metalli (caustiche riflessive)

### B1. Sfera di metallo lucido — caustica riflessiva netta

```yaml
materials:
  - id: "specchio"
    type: "metal"
    color: [0.95, 0.93, 0.88]
    fuzz: 0.0                    # specchio perfetto → caustica netta
```

Lo specchio convesso concentra la luce riflessa in un arco/alone luminoso (le
caustiche riflessive sono in genere più larghe di quelle rifrattive).

---

## Sezione C — Oltre la sfera

Qualunque geometria curva focalizza secondo la propria forma:

- **Cilindro di vetro** → caustica a **banda** (fuoco astigmatico lungo l'asse).
- **Toro di vetro** → **anello** di luce con cuspidi nette.
- **Solidi CSG con frontiera curva** (es. `box ∩ sphere` = cubo dagli spigoli
  arrotondati, calici, bicchieri) → caustica dalle superfici curve.
- **Superfici d'acqua / piani ondulati** → le classiche caustiche da piscina.

Basta che la geometria abbia materiale speculare/trasmissivo: nessun flag.

---

## Sezione D — Ricevente e luce

### D1. Pavimento diffuso (ricevente ideale)

```yaml
world:
  ground:
    type: "infinite_plane"
    material: "pavimento_opaco"
    y: 0
materials:
  - id: "pavimento_opaco"
    type: "lambertian"
    color: [0.7, 0.7, 0.7]
```

Una superficie **diffusa** è il ricevente ideale: massimo contrasto della
caustica. Specchi e vetri non mostrano uno spot leggibile.

### D2. Luce piccola e alta per una caustica netta

```yaml
lights:
  - type: "area"
    corner: [-0.4, 6.0, -0.4]   # piccola e alta → raggi quasi paralleli
    u: [0.8, 0.0, 0.0]
    v: [0.0, 0.0, 0.8]
    color: [1.0, 0.97, 0.92]
    intensity: 80.0
    shadow_samples: 6
```

Piccola e intensa = spot netto. Per una caustica solare usa una `directional` (è
supportata dal photon map):

```yaml
lights:
  - type: "directional"
    direction: [-0.3, -1.0, -0.2]
    color: [1.0, 0.97, 0.90]
    intensity: 4.0
```

---

## Mini-scena completa

```yaml
camera:
  position: [0, 4.5, 6]
  look_at: [0, 0.4, 0]
  fov: 40

world:
  sky:
    type: "flat"
    color: [0.02, 0.02, 0.03]
  ground:
    type: "infinite_plane"
    material: "pavimento"
    y: 0

entities:
  - name: "lente"
    type: "sphere"
    center: [0, 1.2, 0]
    radius: 1.0
    material: "vetro"

lights:
  - type: "area"
    corner: [-0.6, 6.0, -0.6]
    u: [1.2, 0.0, 0.0]
    v: [0.0, 0.0, 1.2]
    color: [1.0, 0.97, 0.92]
    intensity: 60.0
    shadow_samples: 6

materials:
  - id: "pavimento"
    type: "lambertian"
    color: [0.7, 0.7, 0.7]
  - id: "vetro"
    type: "dielectric"
    refraction_index: 1.5
```

Salvala come `scenes/mia-caustica.yaml` e renderizza:

```bash
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- \
  -i scenes/mia-caustica.yaml -o renders/caustica.png \
  -w 1280 -H 800 -s 512 -d 8 --caustics on --caustic-photons 4000000
```

---

## Consigli CLI

```bash
# Anteprima veloce (caustiche on, budget fotoni ridotto)
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- \
  -i scenes/mia-caustica.yaml -q standard-small --caustics on --caustic-photons 1000000

# Final (i preset final/ultra abilitano già --caustics on con budget alto)
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- \
  -i scenes/mia-caustica.yaml -o renders/final.png -q final

# Caustiche molto pulite + clamp firefly più severo
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- \
  -i scenes/mia-caustica.yaml -q final --caustic-photons 8000000 -C 25
```

- `--caustic-photons` più alto = caustiche più pulite/nitide (più memoria e
  pre-pass più lungo).
- `-C` (firefly clamp) più basso (es. 25) smorza eventuali picchi residui da
  vetri/dielettrici.
- Caustiche **off** di default fuori dai preset `final`/`ultra`: usa
  `--caustics on` per le anteprime o `--caustics off` per disattivarle anche in
  `final`.
