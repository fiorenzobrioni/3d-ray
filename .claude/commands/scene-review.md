---
description: "Valida una scena YAML 3D-Ray: controlla errori, anti-pattern e suggerisce ottimizzazioni. Usabile anche per modificare o implementare nuove feature in scene esistenti."
argument-hint: "[path/to/scene.yaml]"
---

# Scene Review

Analizza il file scena YAML indicato e produci un report di validazione strutturato, seguito da un'offerta di applicare i fix e/o implementare modifiche richieste.

## File da analizzare

$ARGUMENTS

Se nessun file è specificato, chiedi all'utente quale file vuole analizzare.

## Riferimenti

Consulta lo schema completo per i valori validi di ogni campo:
- [docs/reference/scene-reference.md](../../docs/reference/scene-reference.md)

Consulta le convenzioni del progetto:
- [CLAUDE.md](../../CLAUDE.md)

## Controlli da eseguire

### 1. Struttura

- [ ] YAML valido (nessun errore di sintassi)
- [ ] Sezioni nell'ordine raccomandato: `imports` → `templates` → `world` → `cameras` → `lights` → `materials` → `entities`
- [ ] `imports:` è la prima sezione (se presente)
- [ ] Usa `cameras:` lista (**non** il legacy `camera:` singolo)
- [ ] Almeno 2 camere con nomi univoci e descrittivi
- [ ] File naming: `kebab-case.yaml`, showcase: `<feature>-showcase.yaml`

### 2. Materiali

- [ ] Ogni materiale ha `id:` unico (nessun duplicato)
- [ ] Ogni materiale ha `type:` valido
- [ ] Nessun materiale orfano (definito ma mai usato in entities/ground/templates)
- [ ] Nessun materiale fantasma (referenziato in entities ma mai definito né importato)
- [ ] Colori RGB in range 0.0–1.0 (non 0–255)
- [ ] `lambertian` per superfici grandi (pavimenti, muri) — `disney` solo per hero objects
- [ ] `metal.fuzz`: 0.0–1.0
- [ ] `dielectric.refraction_index`: range realistico 1.0–2.5
- [ ] `emissive.intensity`: proporzionata alla scena (0.5–100)
- [ ] `disney` parametri: `metallic`, `roughness`, `specular`, `clearcoat`, `sheen`, `spec_trans` tutti in 0.0–1.0

### 3. Geometria e posizionamento

- [ ] `box`: centrato all'origine — per appoggiarlo a terra serve `translate: [x, altezza/2, z]`, **non** `[x, 0, z]`
- [ ] `cylinder`: `center:` è la base — si estende verso +Y
- [ ] `sphere.radius` > 0, `cylinder.height` > 0
- [ ] `torus`: `major_radius` > `minor_radius`
- [ ] `cone`: `top_radius` ≥ 0 se specificato
- [ ] `scale`: valori positivi (negativi = specchiatura, raramente intenzionale)
- [ ] `mesh.path`: file `.obj` esistente, path relativo
- [ ] `csg`: `operation` valida (`union`, `intersection`, `subtraction`), entrambi gli operandi definiti
- [ ] Template/istanze: ogni `template` referenziato in un'istanza esiste, nessun template orfano

### 4. Camere

- [ ] `fov`: 20–90° (avviso se < 15° o > 100°)
- [ ] `aperture`: ≥ 0.0 (errore se negativo)
- [ ] Se `aperture` > 0: `focal_dist` deve corrispondere alla distanza camera→soggetto (non un valore generico come 1.0)
- [ ] `vup`: `[0, 1, 0]` standard, `[0, 0, ±1]` per zenitali — altri valori richiedono giustificazione
- [ ] Nomi camera univoci

### 5. Luci

- [ ] Almeno una sorgente di luce (esplicita o oggetto emissivo)
- [ ] Parametri obbligatori per tipo: `point` → position+intensity, `spot` → position+direction+inner_angle+outer_angle, `directional` → direction+intensity, `area` → corner+u+v, `sphere` → position+radius+intensity
- [ ] `spot`: `inner_angle` < `outer_angle`
- [ ] Intensità proporzionate (nessuna luce con intensità 0 o valori estremi non giustificati)
- [ ] Coerenza luce-cielo: direzione `directional` allineata con `sky.sun.direction` se entrambi presenti

### 6. World e ambiente

- [ ] `sky` presente o omesso (default = flat azzurro). I campi rimossi `ambient_light` / `background` NON devono comparire — segnalare come errore se presenti.
- [ ] Se `sky.type: flat`: `color` con valori 0.0–1.0; `[0,0,0]` ammesso per black-void.
- [ ] Se `sky.type: gradient`: `zenith_color`, `horizon_color`, `ground_color` presenti; eventualmente `sun:` con `direction`/`color`/`intensity`/`size`/`falloff`.
- [ ] Se `sky.type: hdri`: `path:` esiste sotto la directory di scena; `intensity` ragionevole (0.5–3.0); `rotation` in gradi.
- [ ] `ground.material`: referenzia un materiale definito.
- [ ] Se `medium` presente: `type` valido, `sigma_a`/`sigma_s` > 0, `phase` valida, `g` in (-1, 1) per HG.

### 7. Import

- [ ] Path relativi (nessun path assoluto tipo `C:\...`)
- [ ] File importati esistono (`libraries/materials/*.yaml`, `libraries/lights/*.yaml`, ecc.)
- [ ] Nessun import circolare

### 8. Stile e documentazione

- [ ] Header commento con nome scena, descrizione e render consigliati (Draft/Finale)
- [ ] Campioni render sono quadrati perfetti: 64, 256, 1024, 1600
- [ ] Profondità `-d` adeguata: 4–8 normale, 12–20 per vetro/emissivi stratificati
- [ ] Almeno una variante `world:` commentata
- [ ] Commenti in italiano, chiavi YAML in inglese

## Formato output

Produci il report in questo formato:

```
## Review: <nome-file.yaml>

### 🔴 Errori (da correggere)
- **[MATERIALE]** `dis_gold` referenziato in entity "sfera" ma mai definito
- **[CAMERA]** Aperture negativa (-0.05) nella camera "macro"

### 🟡 Avvisi (da valutare)
- **[PERFORMANCE]** Pavimento usa `disney` — suggerito `lambertian` per superfici grandi
- **[POSIZIONE]** Box "tavolo" con translate [0, 0, 0] — probabile Y errato (serve height/2)

### 🟢 Suggerimenti
- **[STILE]** Manca header con render consigliati
- **[CAMERA]** Aggiungere una seconda camera (attualmente solo 1)

### ✅ Riepilogo
- Materiali: 12 definiti, 11 usati, 1 orfano
- Entità: 24
- Camere: 3
- Luci: 2 esplicite + 1 emissivo
```

Se non ci sono errori/avvisi in una categoria, omettila. Se il file è valido, conferma con "Nessun problema rilevato."

## Dopo il report

Chiedi all'utente cosa vuole fare:

1. **Fix automatici** — applica le correzioni per tutti gli errori 🔴 e gli avvisi 🟡 accettati
2. **Modifiche o nuove feature** — implementa aggiunte o cambiamenti alla scena descritti dall'utente (nuovi oggetti, materiali, luci, camere, effetti)
3. **Entrambe** — prima i fix, poi le modifiche

Per le modifiche, consulta `docs/reference/scene-reference.md` per la sintassi corretta di ogni feature da aggiungere.
