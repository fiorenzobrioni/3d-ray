---
description: "Genera una nuova scena YAML per il motore 3D-Ray a partire da una descrizione testuale"
argument-hint: "<descrizione della scena>"
---

# Skill: Create Scene

Genera un file YAML completo e renderizzabile per il motore **3D-Ray** a partire dalla descrizione fornita dall'utente.

## Input

Descrizione dell'utente: $ARGUMENTS

## Riferimenti obbligatori

Prima di generare la scena, consulta:

- Schema YAML completo → [docs/reference/scene-reference.md](../../docs/reference/scene-reference.md)
- Librerie disponibili → [scenes/libraries/README.md](../../scenes/libraries/README.md)
- Convenzioni progetto → [CLAUDE.md](../../CLAUDE.md)

Esamina anche 1–2 scene esistenti in `scenes/` simili al tema richiesto come riferimento stilistico.

## Procedura

### 1. Analisi della richiesta

Determina:
- **Ambientazione**: interno/esterno, giorno/notte
- **Oggetti principali**: quali geometrie servono
- **Atmosfera**: mood, illuminazione dominante
- **Complessità**: quanti oggetti, quanti materiali custom

### 2. Selezione librerie

Scegli le librerie da importare:

| Categoria | Path | Quando usarla |
|-----------|------|---------------|
| Materiali | `libraries/materials/{metals,woods,stones,glasses,ceramics,plastics,fabrics,organics,paints,foods,emissives,grounds}.yaml` | Sempre — evita di ridefinire materiali esistenti |
| Luci | `libraries/lights/{studio-3point,outdoor-noon,outdoor-golden-hour,outdoor-sunset,interior-warm,interior-candlelight,museum-gallery,night-moonlight,...}.yaml` | Quando un preset copre il mood richiesto |
| Oggetti | `libraries/objects/{furniture,architecture,decorative-objects,nature,tableware,laboratory,jewelry,...}.yaml` | Quando servono template pre-costruiti |

Se un preset luce importato copre l'illuminazione, **non aggiungere luci duplicate**. Definisci materiali inline solo se non esistono nelle librerie.

### 3. Generazione YAML

Genera il file seguendo **rigorosamente** questo ordine di sezioni:

```yaml
# =============================================================================
#  NOME SCENA — Sottotitolo Descrittivo
#
#  Descrizione breve della scena e del suo contenuto.
#
#  Render consigliato:
#    Preview: dotnet run ... -- -i scenes/<nome>.yaml -w 400  -H 225  -s 64   -d <D>  -S 1
#    Finale:  dotnet run ... -- -i scenes/<nome>.yaml -w 1920 -H 1080 -s 1024 -d <D>  -S 4
# =============================================================================

imports:
  - path: "libraries/materials/..."
  - path: "libraries/lights/..."

world:
  sky:                          # unico emettitore d'ambiente (flat / gradient / hdri)
    type: "flat"
    color: [R, G, B]            # oppure type: "gradient" con zenith/horizon/ground/sun

cameras:
  - name: "principale"
    ...
  - name: "macro"             # almeno 2 camere
    ...

lights: []                    # se non importate da libreria

materials: []                 # solo materiali custom non in libreria

entities: []                  # oggetti della scena
```

### 4. Regole di composizione

**Coordinate e posizionamento:**
- Sistema: X=destra, Y=alto, Z=verso la camera
- `box` è centrato → per appoggiarlo a terra: `translate: [x, altezza/2, z]`
- `cylinder` ha `center:` alla base → si estende verso +Y
- Template sono centrati all'origine con base a Y=0

**Materiali:**
- `lambertian` per superfici grandi (pavimenti, muri, soffitti)
- `disney`/`pbr` solo per oggetti principali (hero objects)
- Prefisso `dis_` per Disney, `cls_` per Classic nei nuovi ID
- Ogni materiale deve avere un `id:` unico

**Camere:**
- Sempre formato lista `cameras:` (mai `camera:`)
- Minimo 2 camere con nomi descrittivi (es. `principale`, `macro`, `zenitale`, `tre_quarti`)
- `vup: [0, 1, 0]` tranne per viste zenitali (`[0, 0, -1]` o `[0, 0, 1]`)
- `aperture: 0.0` per nitidezza totale, `> 0` per bokeh

**Luci e ambiente:**
- Interni notturni / studio: `sky.type: flat` con `color` quasi nero (es. `[0.0, 0.0, 0.0]`); l'illuminazione viene da luci esplicite o oggetti emissivi.
- Studio con fill morbido: `sky.type: flat` con un colore basso neutro (es. `[0.05, 0.05, 0.06]`) — il flat sky partecipa a NEE come uniform sphere.
- Esterni diurni: `sky.type: gradient` con zenith/horizon/ground e disco `sun:` (può essere l'unica sorgente luminosa).
- HDRI: `sky.type: hdri` con `path:`; nessun fill aggiuntivo necessario.
- Non mescolare luce importata e luci duplicate dello stesso tipo.

**Campioni render:**
- Profondità `-d`: 4 (preview) / 6 (standard) / 8+ (finale) / 16-20 (scene con vetro/rifrazione)
- Campioni `-s`: solo quadrati perfetti (64, 256, 1024, 1600)

**Stile commenti:**
- Header decorativo con `# ===...` per le sezioni principali
- Commenti in italiano, chiavi YAML in inglese
- Includere varianti world/luci commentate (almeno 1 alternativa)

### 5. Naming del file

- Pattern: `kebab-case.yaml`
- Salvare in: `scenes/<nome-scena>.yaml`
- I path di import sono relativi a `scenes/`

### 6. Validazione finale

Controlla prima di salvare:
- [ ] Ogni `material` referenziato nelle `entities` è definito o importato
- [ ] Nessun `id:` materiale duplicato
- [ ] `cameras:` è una lista con almeno 2 camere
- [ ] `imports:` è la prima sezione (se presente)
- [ ] Header commento con nome, descrizione e render consigliati
- [ ] Almeno una variante `world:` commentata
- [ ] Box e cilindri posizionati correttamente a terra

## Output

Il file `.yaml` salvato in `scenes/`. Conferma con un riepilogo: nome file, numero oggetti, librerie importate, camere disponibili.
