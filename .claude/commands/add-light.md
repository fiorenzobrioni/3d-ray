---
description: "Aggiunge una o più luci a una scena YAML 3D-Ray esistente, calcolando tipo, posizione e intensità coerenti con la scena"
argument-hint: "<path/to/scene.yaml> [tipo o ruolo della luce]"
---

# Skill: Add Light

Aggiunge nuove sorgenti luminose a una scena YAML esistente, scegliendo tipo e parametri coerenti con la geometria, il mood e l'illuminazione già presente.

## Input

File e tipo/ruolo della luce: $ARGUMENTS

Interpreta come `<path/to/scene.yaml> [tipo o ruolo opzionale]`. Se manca il file, chiedi quale scena. Se manca il tipo, analizza il setup esistente e suggerisci le luci mancanti.

## Riferimenti

- Schema luci → [docs/reference/scene-reference.md](../../docs/reference/scene-reference.md)
- NEE e sphere light → [docs/technical/path-tracing-and-lighting.md](../../docs/technical/path-tracing-and-lighting.md)
- Convenzioni → [CLAUDE.md](../../CLAUDE.md)
- Setup pronti → copia il blocco di luci adatto da [scenes/presets/lights.md](../../scenes/presets/lights.md) nella sezione `lights:` della scena

## Procedura

### 1. Analisi della scena

Leggi il file YAML e identifica:
- **Luci esistenti**: tipi, posizioni, intensità, colori, ruoli inferibili
- **Bounding box della scena**: estensione degli oggetti
- **Soggetto principale**: centro della composizione
- **Mood**: interno/esterno, giorno/notte, caldo/freddo
- **Setup mancante**: quale ruolo non è coperto (key? fill? rim? accent? practical?)
- **Ambiente**: il blocco `world.sky` (flat / gradient / hdri) — già fornisce luce ambientale?

### 2. Scelta del tipo

| Tipo | Quando usarlo | Parametri chiave |
|------|---------------|------------------|
| `point` | Lampadine, candele, torce puntiformi (piccole, omnidirezionali) | position + intensity + color |
| `directional` | Sole, luna, luce parallela infinita | direction + intensity + color |
| `spot` | Faretti, torce, cono di luce focalizzato | position + direction + inner_angle + outer_angle |
| `area` | Finestre, pannelli soffusi, softbox | corner + u + v + intensity |
| `sphere` | Lampadine fisiche con raggio visibile (globi, lanterne) | position + radius + intensity |
| `physical_sun` | Sole fisico con limb darkening e campionamento del disco | direction + intensity (auto-generato da sky nishita/gradient — non aggiungere manualmente se già presente) |
| `portal` | Portale per finestre/aperture con HDRI (Bitterli 2015) — migliora il campionamento NEE in interni illuminati da HDRI | corner + u + v (come area, senza intensity) |
| Emissive geometry | Insegne, neon, lava — la geometria stessa emette | materiale `emissive` assegnato a un `IHittable` |

**Sphere light vs sphere emissiva**: per lampadine piccole/distanti preferisci `sphere_light` (solid-angle sampling, 2–10× più efficiente). Per oggetti grandi o vicini, la sfera emissiva è adeguata.

**physical_sun**: non aggiungere mai un `physical_sun` manuale se la scena usa `sky.type: nishita` o `sky.type: gradient` con `sun:` — il motore lo genera automaticamente. Aggiungilo manualmente solo per controllare parametri avanzati (limb darkening, jitter) in assenza di quei tipi sky.

**portal**: utile per interni con finestre e HDRI esterno. Definisce geometricamente l'apertura (come un `area` light) ma instrada il campionamento NEE verso l'HDRI invece di fare uniform sampling. Usalo per stanze con una finestra unica — migliora drasticamente il rumore nelle zone d'ombra.

### 3. Ruoli e setup canonico

Se l'utente chiede un ruolo generico (es. "fill", "rim", "practical"), applica questa tabella:

| Ruolo | Scopo | Posizione tipica | Intensità relativa | Colore |
|-------|-------|------------------|--------------------|---------|
| **Key** | Definisce la forma dominante | 30–45° laterale rispetto alla camera, sopra il soggetto | 1.0 (riferimento) | Caldo per interni, neutro per outdoor noon |
| **Fill** | Ammorbidisce le ombre | Opposta alla key, altezza simile | 0.2–0.5 × key | Spesso complementare (se key calda, fill fredda) |
| **Rim / back** | Separa dallo sfondo | Dietro il soggetto, leggermente laterale | 0.5–1.5 × key | Contrastante con la key |
| **Accent** | Evidenzia un dettaglio | Puntata su un oggetto specifico | 0.3–1.0 × key | A discrezione |
| **Practical** | Sorgente visibile in scena | Dentro a un oggetto (lampada, candela) | 0.5–3.0 (locale, non per illuminare tutta la scena) | Colore dell'oggetto (candela calda, schermo freddo) |
| **Ambient** | Illuminazione omogenea | N/A — via `world.sky` (flat/gradient/hdri); l'illuminazione ambient è path-traced | colore del cielo basso (es. `[0.02, 0.02, 0.025]`) | Colore del cielo/ambiente |

### 4. Temperature colore

| Mood | Valori RGB tipici |
|------|-------------------|
| Candela / fuoco | `[1.0, 0.65, 0.35]` |
| Interno caldo (lampadina a incandescenza) | `[1.0, 0.85, 0.65]` |
| Interno neutro (LED 4000K) | `[1.0, 0.95, 0.85]` |
| Mezzogiorno (sole) | `[1.0, 0.98, 0.92]` |
| Golden hour | `[1.0, 0.75, 0.5]` |
| Sunset | `[1.0, 0.6, 0.3]` |
| Luna / notte | `[0.55, 0.7, 1.0]` |
| Insegna neon rosa | `[1.0, 0.3, 0.7]` × intensità |
| Schermo / monitor | `[0.6, 0.75, 1.0]` |

### 5. Calcolo posizione e intensità

**Posizione:**
- Relativa al soggetto e alla bounding box
- Per key/fill/rim: distanza ≈ 1.5–3× diagonale del soggetto
- Per practical: posizione coincidente con l'oggetto sorgente visibile

**Intensità:**
- `point` e `sphere`: intensità scala con 1/r² — lampadina a 2 m dal soggetto ≈ 5–15, area light grande ≈ 1–5
- `directional`: indipendente dalla distanza, tipicamente 1.0–3.0 per il sole, 0.05–0.3 per la luna
- `area`: intensità per unità di area — una finestra 2×2 con intensità 5 = luce soffusa diurna
- `spot`: simile a `point` ma concentrato sul cono; compensa con intensità più alta

**Coerenza con luci esistenti:**
- Non introdurre una seconda "key" — se c'è già una sorgente dominante, ricalibra il ruolo della nuova luce
- Rispetta il rapporto key:fill tra 2:1 e 8:1 (ratio più alto = scene drammatiche, più basso = illuminazione piatta)

### 6. Inserimento nel YAML

- Aggiungi le luci in coda alla lista `lights:` esistente
- Commento descrittivo in stile progetto prima di ogni luce:

```yaml
lights:
  # ── <RUOLO>: <descrizione e motivazione> ────────────────────────────
  - type:       "..."
    position:   [X, Y, Z]
    direction:  [X, Y, Z]   # per directional/spot
    intensity:  N
    color:      [R, G, B]
    # ...parametri specifici del tipo
```

- Per `spot`: verifica `inner_angle` < `outer_angle` (valori in gradi)
- Per `area`: `corner` + `u` + `v` definiscono il quadrilatero; `shadow_samples` ≥ 4 per soft shadows puliti
- Per `sphere`: `radius` > 0 (se molto piccolo → preferisci `point`)

### 7. Naming

- Le luci non hanno `id` obbligatorio, ma se aggiungi un campo `name` commentato (o un commento descrittivo) usa ruolo + oggetto illuminato (es. `# key_finestra_sud`, `# practical_candela_tavolo`).

### 8. Checklist

- [ ] Tipo appropriato al ruolo (no directional per una candela, no point per il sole)
- [ ] Intensità calibrata rispetto alle luci esistenti (rapporto key:fill sensato)
- [ ] Colore coerente col mood della scena
- [ ] Per `spot`: `inner_angle` < `outer_angle`
- [ ] Per `area`: `shadow_samples` adeguato (4–16 in base a quanto vuoi morbide le ombre)
- [ ] Nessun duplicato evidente con una luce già presente
- [ ] Commento descrittivo prima della luce con ruolo e motivazione
- [ ] Se aggiungo una practical: esiste anche la geometria visibile (lampadina, candela) o solo la luce?

## Output

Conferma le luci aggiunte con una tabella: tipo, ruolo, intensità, colore, effetto previsto. Suggerisci se serve aumentare `shadow_samples` globali (`-S`) o `depth` (`-d`) per le nuove sorgenti.
