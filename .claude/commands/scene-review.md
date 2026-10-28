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

- [ ] `format_version: 2` presente (senza è una scena del formato 1: convertirla con `dotnet run --project src/Tools/SceneMigrate -- <scena>`)
- [ ] La scena carica senza errori né avvisi del validatore (chiavi sconosciute, valori di tipo sbagliato, chiavi senza effetto sul `type`): eseguire un render `-q draft-tiny` e leggere i messaggi
- [ ] Sezioni nell'ordine raccomandato: `format_version` → `units` → `render` → `imports` → `templates` → `world` → `cameras` → `lights` → `materials` → `mediums` → `entities`
- [ ] Usa `cameras:` lista
- [ ] Almeno 2 camere con nomi univoci e descrittivi
- [ ] File naming: `kebab-case.yaml`, showcase: `<feature>-showcase.yaml`

### 2. Materiali

- [ ] Ogni materiale ha `id:` unico (nessun duplicato)
- [ ] Ogni materiale ha `type:` valido
- [ ] Nessun materiale orfano (definito ma mai usato in entities/ground/templates)
- [ ] Nessun materiale fantasma (referenziato in entities ma mai definito né importato)
- [ ] Colori RGB in range 0.0–1.0 (non 0–255)
- [ ] `lambertian` per superfici grandi (pavimenti, muri) — `openpbr` solo per hero objects
- [ ] `metal.roughness`: 0.0–1.0
- [ ] `dielectric.ior`: range realistico 1.0–2.5
- [ ] `emissive.luminance`: proporzionata alla scena (0.5–100 senza esposizione, nit reali con `ev100`)
- [ ] `openpbr`: pesi (`*_weight`, `base_metalness`) e rugosità in 0.0–1.0; `thin_film_thickness` in micrometri (0.1–1)
- [ ] SSS: `subsurface_weight` > 0 con `subsurface_radius` scalare > 0 e `subsurface_radius_scale` `[r, g, b]`
- [ ] `interior_medium:` su entità chiuse: referenzia un medium definito nella scena

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
- [ ] Se `aperture` > 0: `focus_distance` (o `focus_point`) deve corrispondere alla distanza camera→soggetto (non il default 1)
- [ ] `up`: `[0, 1, 0]` standard, `[0, 0, ±1]` per zenitali — altri valori richiedono giustificazione
- [ ] Nomi camera univoci

### 5. Luci

- [ ] Almeno una sorgente di luce (esplicita o oggetto emissivo)
- [ ] Parametri per tipo: `point` → position+`intensity` (cd), `spot` → position+direction+`intensity`+inner_angle+outer_angle, `directional` → direction+`illuminance` (lux), `area` → corner+u+v+`luminance` (nit), `sphere` → position+radius+`luminance`; oppure `power` al posto della grandezza; il `color` è solo una tinta
- [ ] `spot`: `inner_angle` < `outer_angle`
- [ ] Luminosità proporzionate (nessuna luce a 0 o valori estremi non giustificati); valori reali (lux, nit) solo con l'`ev100` della camera
- [ ] Coerenza luce-cielo: direzione `directional` allineata con `sky.sun.direction` se entrambi presenti

### 6. World e ambiente

- [ ] `sky` presente o omesso (omesso = ambiente nero).
- [ ] Se `sky.type: flat`: `color` con valori 0.0–1.0; `[0,0,0]` ammesso per black-void.
- [ ] Se `sky.type: gradient`: `zenith_color`, `horizon_color`, `ground_color` presenti; eventualmente `sun:` con `direction`/`color`/`luminance`/`angular_radius`.
- [ ] Se `sky.type: hdri`: `path:` esiste sotto la directory di scena; `exposure` ragionevole (−2..+2 stop); `rotate` in gradi.
- [ ] Se `sky.type: nishita`, `hosek_wilkie` o `preetham`: `sun.direction` valido (verso il sole), altitudine ≥ −10°; non aggiungere `directional` separato (il sole è auto-generato); cielo in nit: `ev100` sulla camera oppure `exposure` sul cielo (circa −10).
- [ ] `ground.type`: valido tra `infinite_plane | quad | disk` (default `infinite_plane`); un terreno è un'entità `heightfield`.
- [ ] `ground.material`: referenzia un materiale definito.
- [ ] `world.medium` (globale) e `interior_medium:` su entità (locale): entrambi validi, non devono coincidere.
- [ ] Se `world.medium` o `interior_medium:` presente: `type` valido, `sigma_a`/`sigma_s` > 0, `phase` valida, `g` in (-1, 1) per HG.

### 7. Risorse e preset

- [ ] Path relativi (nessun path assoluto tipo `C:\...`)
- [ ] Texture/heightmap referenziate esistono sotto `assets/` (`assets/textures/*`, `assets/heightmaps/*`)
- [ ] I blocchi copiati dai cataloghi preset (`scenes/presets/<catalogo>.md`) sono autocontenuti nella scena, con `id` univoci

### 8. Stile e documentazione

- [ ] Header commento con nome scena, descrizione e render consigliati usando preset `-q` (es. `-q draft-small` / `-q final`), non flag manuali
- [ ] Se presenti campioni `-s` manuali nell'header: verificare che siano motivati (override specifico); preferire `-q <preset>`
- [ ] Profondità `-d` adeguata: 4–8 normale, 12–20 per vetro/emissivi stratificati; se l'header usa `-q final -d 16` l'override è corretto
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
- **[PERFORMANCE]** Pavimento usa `openpbr` — suggerito `lambertian` per superfici grandi
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
