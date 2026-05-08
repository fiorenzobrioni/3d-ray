---
description: "Genera un showcase YAML per dimostrare una feature specifica del motore 3D-Ray"
argument-hint: "<feature da dimostrare>"
---

# Skill: Create Showcase

Genera un file `<feature>-showcase.yaml` che dimostra sistematicamente le varianti di una feature del motore 3D-Ray (geometria, materiale, luce, texture, ecc.).

## Input

Feature da dimostrare: $ARGUMENTS

## Riferimenti obbligatori

Prima di generare, consulta:

- Schema YAML → [docs/reference/scene-reference.md](../../docs/reference/scene-reference.md) per tutti i parametri della feature
- Showcase esistenti → `scenes/showcases/` per evitare duplicati e seguire lo stile

Esamina 1–2 showcase esistenti in `scenes/showcases/` come riferimento strutturale.

## Principi di design

Un showcase **non** è una scena artistica. È una **dimostrazione tecnica sistematica**:

- Ogni oggetto mostra **una variazione** del parametro in esame
- Le variazioni sono organizzate in **gruppi logici** (righe, stazioni, piedistalli)
- L'ambiente è **neutro** per non distrarre dalla feature
- I materiali sono **autocontenuti** (definiti inline, nessun import da librerie)
- I commenti **spiegano cosa dimostra** ogni oggetto

## Procedura

### 1. Inventario varianti

Elenca tutte le varianti della feature da dimostrare. Esempi:

| Feature | Varianti tipiche |
|---------|-----------------|
| Geometria | Dimensioni diverse, rotazioni, materiali diversi (opaco, metallo, vetro, emissivo), combinazione con CSG |
| Materiale | Sweep di parametri (es. roughness 0→1), applicato su geometrie diverse, interazione con luci |
| Luce | Intensità, colore, angoli, confronto ombre soft/hard |
| Texture | Scale diverse, colori, combinazione con materiali diversi |

Pianifica **4–9 oggetti** organizzati in gruppi.

### 2. Layout

Scegli un layout in base al numero di varianti:

- **Lineare** (4–5 oggetti): fila singola su asse X, spaziatura ~3 unità, eventualmente su piedistalli
- **Griglia** (6–9 oggetti): righe su asse X, colonne su asse Z, spaziatura ~3 unità
- **Radiale** (4–8 oggetti): oggetto centrale + satelliti disposti a cerchio
- **A file tematiche** (6+ oggetti): ogni riga dimostra una categoria di variazione

Piedistalli (box in marmo/pietra) sono opzionali — usarli quando gli oggetti sono piccoli o serve separazione visiva.

### 3. Generazione YAML

Struttura del file:

```yaml
# ═══════════════════════════════════════════════════════════════════════════
#  <Feature> Showcase — <Sottotitolo Descrittivo>
#
#  Dimostra <cosa>:
#    1. <Variante 1> — <descrizione breve>
#    2. <Variante 2> — <descrizione breve>
#    ...
#
#  Render consigliato:
#    Draft:  -w 400  -H 225  -s 64   -d <D>  -S 1
#    Finale: -w 1920 -H 1080 -s 1024 -d <D>  -S 4
# ═══════════════════════════════════════════════════════════════════════════

world:
  sky: { ... }          # flat scuro per emissivi, gradient neutro per outdoor, hdri per realismo
  ground:
    type: "infinite_plane"
    material: "pavimento"
    y: 0

cameras:
  - name: "principale"  # Vista d'insieme — tutti gli oggetti visibili
    ...
  - name: "closeup_<variante>"  # Dettaglio su una variante interessante
    ...

materials:
  - id: "pavimento"     # Checker o tinta neutra
    ...
  # Un materiale per ogni variante, con commento che spiega la scelta

entities:
  # Organizzati per gruppo, ogni oggetto con commento numerato
```

### 4. Regole specifiche showcase

**Ambiente neutro:**
- Outdoor: `sky.type: gradient` con colori desaturati, `ground` checker grigio o pietra.
- Indoor / emissivi: `sky.type: flat` con `color: [0.0, 0.0, 0.0]` — gli emissivi e le luci esplicite forniscono tutta l'illuminazione.
- Studio fill morbido: `sky.type: flat` con un grigio basso neutro (es. `[0.04, 0.04, 0.05]`) — partecipa a NEE come uniform sphere.

**Camere:**
- `principale`: vista d'insieme, tutti gli oggetti nel frame, FOV 38–48
- Almeno 1 `closeup_<nome>`: dettaglio su una variante interessante, FOV 25–35, opzionale bokeh
- Per griglie grandi: aggiungere `zenitale` (dall'alto)

**Materiali — tutti inline:**
- `pavimento`: sempre `lambertian` con texture `checker` (colori neutri grigi)
- Oggetti: variare tipo materiale per mostrare interazioni con la feature
- Includere almeno: un opaco (`lambertian`/`disney`), un metallico, un vetro (`dielectric`) — se pertinente alla feature

**Luci:**
- Setup semplice: 1–2 point light (key + fill), eventualmente 1 directional
- Per showcase di luci: ogni tipo di luce è un oggetto della demo, non il setup

**Entità — commenti strutturati:**
```yaml
entities:
  # ═══════════════════════════════════════════════════════════════════════
  #  GRUPPO 1: <Nome del Gruppo>
  # ═══════════════════════════════════════════════════════════════════════

  # 1. <Cosa dimostra>
  - name: "<nome_descrittivo>"
    type: "<feature>"
    ...

  # 2. <Cosa dimostra>
  - name: "<nome_descrittivo>"
    ...
```

**Profondità render `-d`:**
- Geometrie opache: `4` (preview) / `8` (finale)
- Scene con vetro/dielectric: `8` / `12`
- Scene con emissivi come unica luce: `8` / `16`

### 5. Naming e salvataggio

- File: `scenes/showcases/<feature>-showcase.yaml`
- Se la feature è composta (es. capsule + annulus): `<feat1>-<feat2>-showcase.yaml`
- Verificare che non esista già un showcase per la stessa feature in `scenes/showcases/`

### 6. Checklist finale

- [ ] Header con lista numerata delle varianti dimostrate
- [ ] Render consigliati (Draft + Finale) con `-d` adeguato alla complessità
- [ ] Nessun import — tutti i materiali sono inline
- [ ] Camera `principale` inquadra tutti gli oggetti
- [ ] Almeno 1 camera closeup
- [ ] Ogni entità ha un commento numerato che spiega cosa dimostra
- [ ] Oggetti posizionati correttamente (box centrato, cylinder dalla base)
- [ ] Pavimento checker neutro
- [ ] Nessun `id:` materiale duplicato

## Output

Il file `.yaml` salvato in `scenes/showcases/`. Conferma con: nome file, numero varianti dimostrate, layout scelto, camere disponibili.
