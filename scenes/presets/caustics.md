# Caustiche focalizzate — preset copia-incolla

Le **caustiche focalizzate** sono lo spot luminoso che una lente, una sfera di
vetro, una superficie d'acqua o uno specchio concentrano su una superficie. Sono
la luce che gli shadow ray dritti non sanno produrre, perché richiede di seguire
il cammino corretto secondo Snell (rifrazione) o la legge di riflessione.

Il path tracing (l'integratore di default) le rende già, ma solo quando un
rimbalzo colpisce per caso la luce attraverso il vetro: escono granulose,
tanto più quanto la luce è piccola. Le rendono pulite gli **integratori
bidirezionali**, che tracciano la luce anche dalle sorgenti: `--integrator bdpt`
(path tracing bidirezionale) per le caustiche viste direttamente o in uno
specchio, `--integrator vcm` (vertex connection and merging) anche per quelle
viste **attraverso** un vetro o l'acqua, come il fondo di una piscina. Sono
**generali**: qualsiasi geometria speculare, liscia o ruvida, e **tutte** le
luci, sole e cielo HDRI compresi.

Questo catalogo è un insieme di **oggetti pronti** (entità + materiale) da
**copiare e incollare** nelle tue scene. Per il flusso d'uso generale vedi
[`README.md`](README.md); per la teoria
[`../../docs/technical/path-tracing-and-lighting.md`](../../docs/technical/path-tracing-and-lighting.md).

> **Regola d'oro: nessun flag YAML.** Niente `caustic_caster`, niente
> `caustic_receiver`, nessuna marcatura per-oggetto: basta avere in scena una
> geometria speculare (a focalizzare) e una superficie diffusa (a ricevere), e
> scegliere al render l'integratore (`--integrator bdpt` o `vcm`).

---

## Come si attivano

**1 - Al render (CLI).**

```bash
# Caustiche viste direttamente o in uno specchio: path tracing bidirezionale
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- \
  -i scenes/mia-scena.yaml -o renders/out.png \
  -w 1280 -H 800 -s 512 -d 8 --integrator bdpt

# Caustiche viste attraverso vetro o acqua: VCM
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- \
  -i scenes/mia-scena.yaml -o renders/out.png \
  -w 1280 -H 800 -s 512 -d 8 --integrator vcm
```

- `--integrator path|bdpt|vcm`: default `path` (anche in tutti i preset).
  `bdpt` è privo di bias; `vcm` è consistente (il bias del merging si annulla
  al crescere di `-s`).
- `-s`: le iterazioni. Più iterazioni = caustiche più pulite (e, con `vcm`,
  più nitide).
- `--vcm-radius <f>`: il raggio di merging iniziale, frazione del raggio della
  scena (default 0.003). Più grande = caustiche lisce prima, più sfocate con
  pochi campioni.

**2 - Niente flag YAML.** Non devi marcare niente: la sfera di
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
    point: [0, 0, 0]
```

---

## Cosa focalizza e cosa riceve

- **Focalizza** (genera la caustica): qualsiasi geometria con materiale
  **speculare/trasmissivo** — vetro/cristallo (`dielectric`, o `openpbr` con
  `transmission_weight`), metallo/specchio (`metal`, o `openpbr` con `base_metalness ≈ 1`),
  acqua. Vale per qualunque forma: sfere e lenti concentrano in uno spot,
  cilindri/tori in bande e anelli, specchi piani proiettano riflessi netti.
- **Riceve** (mostra la caustica): qualsiasi superficie **diffusa/matte**
  (`lambertian` o `openpbr` poco speculare). Pavimenti, pareti, il `ground`.
- **Luci**: tutte le finite (`area`/geometriche, `sphere`, `point`/`spot`) **e**
  le `directional`/sole. Una luce piccola e intensa dà una caustica **netta**; una
  grande la diffonde in un **alone morbido**.

> **Limiti noti (onesti).** Le caustiche viste attraverso la nebbia (un fascio
> focalizzato dentro un volume) vengono solo dalle connessioni, senza merging:
> restano più rumorose.

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
    ior: 1.5
```

La lente di riferimento: vetro liscio `ior 1.5`. Concentra la luce in uno spot
luminoso sul pavimento sotto/dietro la sfera. Con `--integrator bdpt` lo spot
converge in fretta; alza `-s` per renderlo più pulito.

### A2. Cristallo ad alto IOR — fuoco stretto e intenso

```yaml
materials:
  - id: "cristallo"
    type: "dielectric"
    ior: 1.8       # IOR alto → fuoco più stretto e brillante
```

Un IOR più alto piega di più i raggi: la caustica diventa più piccola e intensa.

### A3. Vetro colorato — caustica tinta

```yaml
materials:
  - id: "vetro_rubino"
    type: openpbr
    transmission_weight: 1.0
    specular_roughness: 0.0
    base_color: [0.5, 0.5, 0.5]
    transmission_color: [0.5, 0.5, 0.5]
```

Il vetro `openpbr` trasmissivo proietta una caustica colorata (rubino, smeraldo,
zaffiro cambiando `base_color`). Tieni `specular_roughness: 0.0` per un fuoco nitido.

---

## Sezione B — Specchi e metalli (caustiche riflessive)

### B1. Sfera di metallo lucido — caustica riflessiva netta

```yaml
materials:
  - id: "specchio"
    type: "metal"
    color: [0.95, 0.93, 0.88]
    roughness: 0.0                    # specchio perfetto → caustica netta
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
    point: [0, 0, 0]
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
    luminance: 77.8
    shadow_samples: 6
```

Piccola e intensa = spot netto. Per una caustica solare usa una `directional`:

```yaml
lights:
  - type: "directional"
    direction: [-0.3, -1.0, -0.2]
    color: [1.0, 0.97, 0.90]
    illuminance: 3.89
```

---

## Mini-scena completa

```yaml
format_version: 2
cameras:
  - position: [0, 4.5, 6]
    look_at: [0, 0.4, 0]
    fov: 40

world:
  sky:
    type: "flat"
    color: [0.02, 0.02, 0.03]
  ground:
    type: "infinite_plane"
    material: "pavimento"
    point: [0, 0, 0]

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
    luminance: 58.4
    shadow_samples: 6

materials:
  - id: "pavimento"
    type: "lambertian"
    color: [0.7, 0.7, 0.7]
  - id: "vetro"
    type: "dielectric"
    ior: 1.5
```

Salvala come `scenes/mia-caustica.yaml` e renderizza:

```bash
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- \
  -i scenes/mia-caustica.yaml -o renders/caustica.png \
  -w 1280 -H 800 -s 512 -d 8 --integrator bdpt
```

---

## Consigli CLI

```bash
# Anteprima veloce
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- \
  -i scenes/mia-caustica.yaml -q standard-small --integrator bdpt

# Final
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- \
  -i scenes/mia-caustica.yaml -o renders/final.png -q final --integrator bdpt

# Caustica vista attraverso l'acqua o un vetro (piscina, oggetto in un bicchiere)
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- \
  -i scenes/mia-caustica.yaml -o renders/final.png -q final --integrator vcm
```

- `-s` più alto = caustiche più pulite; con `vcm` anche più nitide.
- `-C` (firefly clamp) basso (es. 25) smorza eventuali picchi residui da
  vetri/dielettrici, a costo di un po' di bias.
- Tutti i preset rendono in path tracing: l'integratore si sceglie a mano.
