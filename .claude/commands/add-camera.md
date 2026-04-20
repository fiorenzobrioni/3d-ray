---
description: "Aggiunge una o più camere a una scena YAML 3D-Ray esistente"
argument-hint: "<path/to/scene.yaml> [tipo inquadratura]"
---

# Skill: Add Camera

Aggiunge nuove angolazioni camera a una scena YAML esistente, calcolando posizione, look_at e parametri coerenti con la geometria della scena.

## Input

File e tipo di inquadratura: $ARGUMENTS

Interpreta l'input come `<path/to/scene.yaml> [tipo inquadratura opzionale]`. Se il tipo non è specificato, analizza la scena e suggerisci le angolazioni mancanti tra quelle standard. Se nessun file è specificato, chiedi all'utente quale file vuole modificare.

## Riferimenti

- Schema camere → [docs/reference/scene-reference.md](../../docs/reference/scene-reference.md)
- Convenzioni → [CLAUDE.md](../../CLAUDE.md)

## Procedura

### 1. Analisi della scena

Leggi il file YAML e identifica:
- **Camere esistenti**: nomi, posizioni, stili già coperti
- **Bounding box della scena**: estensione degli oggetti su X, Y, Z per calcolare distanze sensate
- **Soggetto principale**: l'oggetto più rilevante (centro della composizione)
- **Quota del pavimento**: Y del ground (di solito 0)

### 2. Scelta dell'angolazione

Se l'utente non specifica il tipo, suggerisci le angolazioni mancanti tra queste categorie standard:

| Tipo | Scopo | FOV tipico | Aperture |
|------|-------|------------|----------|
| `principale` / `hero` | Vista d'insieme 3/4 rialzata | 36–46 | 0.0–0.08 |
| `frontale` | Simmetrica centrata, stile catalogo | 38–44 | 0.0 |
| `macro` | Dettaglio ravvicinato su un oggetto | 22–30 | 0.08–0.20 (bokeh forte) |
| `zenitale` | Dall'alto, planimetria | 40–50 | 0.0 |
| `tre_quarti` | 3/4 cinematografico | 40–50 | 0.0–0.06 |
| `drammatica` / `dutch` | Angolo basso o inclinato | 40–55 | 0.04–0.10 |
| `closeup_<nome>` | Primo piano su un elemento specifico | 25–35 | 0.06–0.15 |

Non aggiungere un tipo già presente (stesso nome o stessa posizione/angolo).

### 3. Calcolo parametri

**Posizione e look_at:**
- Calcola la distanza dalla scena in base alla bounding box e al FOV
- Per viste d'insieme: posizione a ~1.5–2× la diagonale della scena
- Per macro: posizione a 0.3–0.5× la diagonale, puntata sull'oggetto specifico
- Per zenitale: posizione alta sopra il centro, Y ≈ 2× altezza massima della scena

**FOV:**
- 22–30°: telephoto (macro, closeup — comprime la prospettiva)
- 35–45°: standard (hero, principale — aspetto naturale)
- 46–55°: grandangolare (drammatica, contesto ampio)

**Aperture e focal_dist:**
- `aperture: 0.0` → tutto a fuoco (zenitale, frontale, catalogo)
- `aperture: 0.03–0.08` → bokeh leggero (hero, tre quarti)
- `aperture: 0.10–0.20` → bokeh forte (macro, closeup)
- `focal_dist` = distanza euclidea tra `position` e `look_at` (calcolare sempre)

**vup:**
- Standard: `[0, 1, 0]`
- Zenitale: `[0, 0, -1]` o `[0, 0, 1]` (il pendolo/oggetto appare dritto)
- Dutch angle: omettere vup e usare rotazione esplicita, oppure vup inclinato

### 4. Inserimento nel YAML

- Aggiungi le nuove camere **in coda** alla lista `cameras:` esistente
- Ogni camera con commento descrittivo in stile progetto:
```yaml
  # ── <Tipo>: <descrizione dell'inquadratura> ──────────────────────────
  - name:       <nome>
    position:   [X, Y, Z]
    look_at:    [X, Y, Z]
    vup:        [0, 1, 0]      # solo se diverso dal default
    fov:        <valore>
    aperture:   <valore>
    focal_dist: <valore>        # = distanza position→look_at
```

- Il commento decorativo usa `# ──` con padding di `─` fino a colonna ~72
- Indentazione: 2 spazi per la lista, 4 spazi per i campi

### 5. Naming

Nomi camera in italiano, `snake_case`, descrittivi:
- `principale`, `frontale`, `macro`, `zenitale`, `tre_quarti`, `drammatica`
- Per closeup specifici: `closeup_<oggetto>` (es. `closeup_sfera`, `closeup_dado`)
- Per angoli specifici: `laterale_destra`, `basso_livello`
- Nomi univoci — verificare che non esistano già

### 6. Checklist

- [ ] Nome univoco (non duplicato tra le camere esistenti)
- [ ] `focal_dist` ≈ distanza position→look_at
- [ ] `aperture` ≥ 0 (mai negativa)
- [ ] `fov` nel range 20–90°
- [ ] `vup` coerente con il tipo di vista
- [ ] Commento decorativo prima di ogni camera
- [ ] Se aperture > 0, focal_dist punta al soggetto principale (non un valore generico)

## Output

Conferma le camere aggiunte con una tabella: nome, tipo, FOV, aperture, oggetto inquadrato.
