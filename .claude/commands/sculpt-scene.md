---
description: "Genera una scena YAML 3D-Ray complessa e ricca, lavorando come uno scultore: CSG, gruppi gerarchici, mix material, volumetria, illuminazione cinematografica"
argument-hint: "<descrizione della scena>"
---

# Skill: Sculpt Scene

Genera una scena YAML 3D-Ray **ricca di dettaglio, atmosfera e composizione**, lavorando come uno scultore: la forma emerge per sottrazione, stratificazione e rifinitura, non per assemblaggio di primitive semplici.

## Input

Descrizione dell'utente: $ARGUMENTS

## Mentalità scultore

A differenza di `/create-scene` (scena pulita con libreria), qui l'obiettivo è **profondità e ricchezza visiva**. Ogni oggetto principale deve avere:
- Forma derivata da CSG o gruppi, non una singola primitiva nuda
- Materiali stratificati (mix material, texture, normal map dove sensato)
- Interazione con illuminazione motivata (key/fill/rim, sorgenti pratiche visibili)
- Contesto atmosferico (medium, HDRI, sky gradient con sun)

## Riferimenti obbligatori

- Schema YAML → [docs/reference/scene-reference.md](../../docs/reference/scene-reference.md)
- Librerie → [scenes/libraries/README.md](../../scenes/libraries/README.md)
- Convenzioni → [CLAUDE.md](../../CLAUDE.md)
- CSG → [docs/technical/csg-boolean-operations.md](../../docs/technical/csg-boolean-operations.md)
- Path tracing e NEE → [docs/technical/path-tracing-and-lighting.md](../../docs/technical/path-tracing-and-lighting.md)

Esamina 2–3 scene di produzione ricche come riferimento stilistico (es. `scenes/alchemist-lab.yaml`, `scenes/castello-sforzesco.yaml`, `scenes/big-ben.yaml`, `scenes/chess.yaml`).

## Budget minimo di complessità

A meno che l'utente chieda esplicitamente "semplice" o "minimale":

| Aspetto | Minimo |
|---------|--------|
| Entità totali | 15+ |
| Materiali unici | 8+ (almeno 3 Disney PBR) |
| Sorgenti luminose | 3+ (setup multi-sorgente con ruoli distinti) |
| Operazioni CSG o gruppi gerarchici | ≥ 1 gruppo con 3+ figli o ≥ 1 CSG |
| Texture (procedurali o immagine) | ≥ 1 |
| Elemento atmosferico | 1 tra: medium globale, HDRI, sky gradient con sun |
| Camere | 3+ (principale + dettaglio + un'alternativa cinematografica) |

## Procedura iterativa (pipeline da scultore)

### Fase 1 — Blocco (forme grossolane)

Identifica il soggetto principale, la scala, l'ambientazione. Piazza i volumi maggiori come box/cylinder/sphere in posizione approssimativa. Non curare ancora i materiali.

### Fase 2 — Desgrossatura (gerarchia)

Subdividi i volumi in `group` con figli logici. Un oggetto composto (es. un tavolo) = un gruppo con piano, gambe, traversi. Applica `transform` al gruppo per posizionarlo — i figli sono relativi.

### Fase 3 — Rifinitura (CSG e dettaglio)

Dove serve forma non-primitiva:
- **Sottrazione**: scavare un incavo, una fessura, un'apertura (tazza = sfera − sfera interna; muro con finestra = box − box)
- **Unione**: fondere organicamente due forme (goccia = sfera ∪ cono)
- **Intersezione**: ottagono, lente, gemma (box ∩ cilindro ruotato)
- Annidare CSG ricorsivamente per forme complesse

Aggiungi smussi visivi e dettagli di superficie (bordi, rivetti, rilievi) con primitive extra o CSG.

### Fase 4 — Finitura materiali (stratificazione)

Ogni superficie importante deve avere:
- **Base PBR corretta**: `disney` con parametri coerenti (metallic, roughness realistici)
- **Texture dove serve varietà**: checker, marble, wood, noise, image
- **Mix material per realismo**: ruggine su metallo, polvere su legno, muschio su pietra (peso costante o mask di rumore)
- **Normal map** per dettagli micro (tessuto, pietra scolpita, intonaco) — solo dove aggiunge valore

Superfici grandi (muri, pavimenti, soffitti): `lambertian` con texture — non Disney (spreco di calcolo).

### Fase 5 — Illuminazione (come scolpire con la luce)

La luce scolpisce la forma. Usa almeno 3 sorgenti con ruoli distinti:
- **Key** (principale, definisce la forma): `directional` per il sole, `area` per una finestra, `spot` per un faretto
- **Fill** (ammorbidisce le ombre): `point` opposto alla key, intensità 30–50% della key, spesso colore complementare
- **Rim / back** (stacca il soggetto dallo sfondo): dietro il soggetto, intensità media, spesso colore contrastante
- **Pratiche** (sorgenti visibili in scena): candele, lampadine, fuoco — geometria emissiva o `sphere_light`, sempre motivate visivamente

Temperature colore coerenti: caldo (interni, candela, tramonto) = `[1.0, 0.8, 0.55]`; freddo (luna, cielo, ombre) = `[0.6, 0.75, 1.0]`. Contrasto caldo/freddo = leggibilità 3D.

### Fase 6 — Atmosfera (profondità e mood)

Aggiungi UN elemento atmosferico:
- **HDRI**: per illuminazione ambientale fotografica e riflessi ricchi (esterni, scene realistiche)
- **Sky gradient con sun**: per outdoor stilizzati con fonti di luce coerenti
- **Medium globale leggero**: `homogeneous` o `height_fog` con `sigma_s` basso (0.005–0.03) per nebbia sottile che dà profondità
- **Medium denso**: `heterogeneous_procedural` o `grid` per fumo localizzato, nuvole, esplosioni — richiede `-d 12+` e `-C 25`

### Fase 7 — Composizione camere

Non bastano 2 camere. Minimo 3, meglio 4–5:
- `principale` — vista d'insieme, 3/4 rialzata, FOV 38–46
- `dettaglio_<x>` — primo piano su un'area di alta qualità, FOV 25–35, aperture 0.08–0.15 per bokeh
- `cinematografica` — angolo basso o dutch, FOV 45–55, per drammaticità
- `zenitale` — se la planimetria ha valore compositivo

## Struttura YAML

```yaml
# ═══════════════════════════════════════════════════════════════════════════
#  NOME SCENA — Sottotitolo Cinematografico
#
#  Descrizione dettagliata dell'ambientazione, del mood, del soggetto e
#  degli elementi chiave che rendono la scena leggibile.
#
#  COMPOSIZIONE: <numero> gruppi, <numero> materiali (di cui <n> PBR),
#                <numero> luci (<tipi>), atmosfera: <HDRI / fog / sky>.
#
#  Render consigliato:
#    Preview:  -w 400  -H 225  -s 64   -d <D>  -S 1
#    Standard: -w 800  -H 450  -s 256  -d <D>  -S 2
#    Finale:   -w 1920 -H 1080 -s 1024 -d <D>  -S 4
# ═══════════════════════════════════════════════════════════════════════════

imports:              # librerie per accelerare (materiali + setup luci se pertinente)
  - path: "libraries/materials/..."
  - path: "libraries/lights/..."  # opzionale

world: ...            # con almeno 1 variante commentata

cameras: ...          # 3+ camere con nomi descrittivi

lights: ...           # setup multi-sorgente se non importato

materials: ...        # 8+ unici, mix di Disney e Classic dove sensato

templates: ...        # per oggetti ripetuti o molto composti

entities: ...         # gerarchia con gruppi e CSG, commenti per zone della scena
```

## Regole di composizione

- **Sistema coordinate**: X=destra, Y=alto, Z=verso camera
- **Box centrato** → per appoggiarlo `translate: [x, h/2, z]`
- **Cylinder** ha `center:` alla base → cresce verso +Y
- **Gruppi** ereditano trasformazioni — figli in coordinate locali
- **Template** istanziati con `type: "instance"` — ogni istanza può avere materiale e trasformazione propri
- **ID materiali univoci**, prefisso `dis_` (Disney) o `cls_` (Classic)
- **Render depth `-d`**: 8 base, 12–16 con dielectric stratificato o volumetria densa, 20 per scene con liquidi in bicchieri
- **Campioni `-s`**: quadrati perfetti (64, 256, 1024, 1600)

## Naming

File: `scenes/<nome-scena>.yaml` in `kebab-case`. Non in `scenes/showcases/` (quello è per dimostrazioni tecniche, non scene scultoree).

## Checklist finale (scultore)

- [ ] ≥ 15 entità, ≥ 8 materiali unici, ≥ 3 luci con ruoli distinti
- [ ] Almeno 1 gruppo gerarchico O 1 CSG
- [ ] Almeno 1 texture (procedurale o immagine)
- [ ] Almeno 1 elemento atmosferico (HDRI, sky con sun, o medium)
- [ ] ≥ 3 camere tra cui una cinematografica o di dettaglio
- [ ] Temperature colore luce coerenti e con contrasto caldo/freddo leggibile
- [ ] Superfici grandi in `lambertian` (no Disney per muri/pavimenti)
- [ ] Header con composizione dichiarata (gruppi, materiali, luci, atmosfera)
- [ ] Almeno 1 variante `world:` commentata
- [ ] Se `-d < 12` e ci sono dielectric annidati → alza o documenta

## Output

File `.yaml` salvato in `scenes/`. Conferma con riepilogo scultoreo:
- Forme principali e tecniche usate (CSG, gruppi)
- Palette materiali (PBR vs Classic, mix usati)
- Setup luci (tipi e ruoli)
- Atmosfera (elemento dominante)
- Camere disponibili
- Comando preview consigliato
