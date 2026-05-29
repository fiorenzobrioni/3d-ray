---
description: "Genera o estende un catalogo di preset copia-incolla (.md) per 3D-Ray sotto scenes/presets/"
argument-hint: "<catalogo o tema> (es. materials-stone, lights, mediums)"
---

# Skill: Create Preset

Genera un nuovo catalogo di preset **copia-incolla** (`.md`) sotto `scenes/presets/`,
oppure estende un catalogo esistente con nuove sezioni. Un catalogo è un documento
Markdown che raccoglie blocchi YAML pronti da **copiare e incollare** dentro le
scene: ogni preset è validato contro lo schema del motore e renderizza senza warning.

## Input

Catalogo o tema da generare/estendere: $ARGUMENTS

Interpreta l'input come il nome di una famiglia (`materials-stone`, `materials-glass`,
`lights`, `mediums`, `terrains`, `world`, `sky`, …) o un tema descrittivo. Se il
catalogo esiste già, **estendilo** con nuove sezioni coerenti; se non esiste, creane
uno nuovo seguendo l'anatomia qui sotto.

## Riferimenti obbligatori

Prima di generare, consulta:
- Schema YAML → [docs/reference/scene-reference.md](../../docs/reference/scene-reference.md)
- Indice cataloghi → [scenes/presets/README.md](../../scenes/presets/README.md)
- Catalogo esemplare → [scenes/presets/materials-stone.md](../../scenes/presets/materials-stone.md)
- 1–2 cataloghi esistenti della stessa famiglia come riferimento strutturale

Verifica nell'indice ([scenes/presets/README.md](../../scenes/presets/README.md))
se esiste già un catalogo che copre la famiglia: se sì, estendilo invece di
crearne uno parallelo.

## Disciplina di curatela

Un catalogo **non** è un dump esaustivo. Poche voci ben scelte e corrette battono
decine di varianti quasi identiche.

- 6–12 preset per catalogo, raggruppati in sezioni tematiche (`# Sezione A — …`).
- Ogni preset deve essere fisicamente plausibile e renderizzare senza warning.
- Ogni preset ha un `id` breve e descrittivo (rinominabile nella scena).
- Niente varianti ridondanti: se due preset differiscono solo per il colore, tieni
  quello più rappresentativo e cita la variazione nel testo.

## Anatomia di un catalogo

Replica la struttura di `materials-stone.md`, nell'ordine:

1. **Titolo + intro** — una riga su cosa contiene il catalogo, link a
   [`README.md`](README.md) per il flusso d'uso e allo schema completo.
2. **Nota in evidenza** (`> **…**`) — la regola d'oro o l'errore tipico della
   famiglia (es. la guida opaco-vs-traslucido per pietra/vetro; per le luci
   `area` servono `corner`/`u`/`v`).
3. **Schema rilevante** — un singolo blocco YAML annotato che mostra i campi
   tipici della famiglia con commenti sui range.
4. **Sezioni tematiche** — `# Sezione A`, `B`, … ognuna con uno o più blocchi
   `materials:` / `lights:` / `mediums:` copia-incolla, **ciascuno seguito da una
   breve motivazione** (1–2 frasi: cosa rende il preset corretto, dove usarlo).
5. **Matrice decisionale** — tabella `| Caso d'uso | Preset | Note chiave |` che
   instrada verso il preset giusto.
6. **CLI tips** — 1–3 comandi `dotnet run --project src/RayTracer -- -i scena.yaml …`
   con i flag che la famiglia tende a richiedere (`-C` per fireflies, `-s`/`-d`
   per volumi/SSS, `-S` per penombre morbide).

### Esempio di blocco preset (da una sezione tematica)

```markdown
## A1. Carrara lucido

```yaml
materials:
  - id: "carrara_lucido"
    type: "disney"
    # ... parametri ...
```

Bianco caldo a vene grigio-blu. La pelle lucida è il `clearcoat` su `roughness`
basso; `spec_trans: 0` esplicito tiene la pietra opaca.
```

## Regole per famiglia

### Pietra e vetro — opaco vs traslucido

Per i cataloghi pietra/vetro la nota in evidenza **deve** chiarire la distinzione,
perché il motore auto-promuove `spec_trans` a 1.0 se trova `subsurface_radius`
**senza** uno `spec_trans` esplicito:

- **Marmo lucido opaco** (Carrara, Calacatta, Statuario, graniti): lucentezza data
  da `clearcoat` + `coat_roughness` basso; `spec_trans: 0` **esplicito**;
  **nessun** `subsurface_radius` grande (eviterebbe l'auto-promozione a vetro).
- **Marmo traslucido** (onice, alabastro, gesso): `spec_trans` esplicito (onice
  0.3–0.7, alabastro 0.05–0.10) + `transmission_color` + `transmission_depth`
  (Beer-Lambert) + `subsurface_radius` per la diffusione interna.

Rimanda a [docs/technical/subsurface-scattering.md](../../docs/technical/subsurface-scattering.md).

### Luci

La nota in evidenza ricorda che `area` richiede `corner`/`u`/`v` (non
`position`/`width`/`height`) e che ogni geometria `emissive` entra automaticamente
in NEE. Indica per ogni sezione il `world` abbinato e il profilo render consigliato.

### Mediums

La nota in evidenza spiega `sigma_a` (assorbimento) vs `sigma_s` (scattering) e il
ruolo di `phase`/`g`. Mostra entrambe le forme d'uso: `world.medium` globale e
`mediums:` + `interior_medium` per entità chiuse.

## Risorse binarie

Texture immagine, font e heightmap **non** vanno nei cataloghi: vivono sotto
[`scenes/assets/`](../../scenes/assets/) (`textures/`, `fonts/`, `heightmaps/`) e si
referenziano per path relativo dalla scena (es. `texture: { path: "assets/textures/wood-floor.png" }`).
Se un preset usa una texture, cita il path sotto `assets/`.

## Dopo la generazione

1. Se hai creato un nuovo catalogo, aggiungi la riga corrispondente alla tabella
   **Cataloghi** in [scenes/presets/README.md](../../scenes/presets/README.md).
2. Valida ogni blocco rispetto allo schema: nessun campo inventato, range corretti.
3. Conferma con: nome file, numero di preset, sezioni coperte.

## Checklist finale

- [ ] File sotto `scenes/presets/<famiglia>.md`
- [ ] Intro + link a `README.md` e allo schema
- [ ] Nota in evidenza con la regola d'oro della famiglia
- [ ] Blocco "Schema rilevante" annotato
- [ ] 6–12 preset in sezioni tematiche, ognuno con motivazione breve
- [ ] Per pietra/vetro: guida opaco-vs-traslucido (`clearcoat`+`spec_trans: 0` vs `spec_trans`+`transmission_*`+`subsurface_radius`)
- [ ] Matrice decisionale + CLI tips
- [ ] ID univoci, brevi, rinominabili
- [ ] Texture/heightmap referenziate da `assets/`, mai incluse nel catalogo
- [ ] Indice in `scenes/presets/README.md` aggiornato (se nuovo catalogo)
