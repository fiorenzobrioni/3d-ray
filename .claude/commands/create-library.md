---
description: "Genera un nuovo file libreria YAML (materiali, luci, oggetti o starter-kit) per 3D-Ray"
argument-hint: "<categoria> <tema>"
---

# Skill: Create Library

Genera un nuovo file libreria YAML da aggiungere a `scenes/libraries/`, seguendo le convenzioni strutturali e di naming del progetto.

## Input

Categoria e tema da creare: $ARGUMENTS

Interpreta l'input come `<categoria> <tema>` dove la categoria è una tra `materials`, `lights`, `objects`, `starter-kits` e il tema descrive il contenuto (es. `materials gemstones`, `lights horror`, `objects bathroom fixtures`).

## Riferimenti obbligatori

Prima di generare, consulta:
- Schema YAML → [docs/reference/scene-reference.md](../../docs/reference/scene-reference.md)
- Sistema librerie → [scenes/libraries/README.md](../../scenes/libraries/README.md)
- README della categoria specifica (es. [scenes/libraries/materials/README.md](../../scenes/libraries/materials/README.md))
- 1–2 file esistenti della stessa categoria come riferimento strutturale

Verifica che non esista già una libreria che copra lo stesso tema in `scenes/libraries/<categoria>/`.

## Procedura per categoria

---

### Materials — `scenes/libraries/materials/<tema>.yaml`

**Struttura file:**

```yaml
# ============================================================================
# Libreria Materiali — <Tema>
# ============================================================================
#
# Importa con:  imports: [{ path: "libraries/materials/<tema>.yaml" }]
#
# <Descrizione del contenuto e delle varianti>.
#
# ── Convenzione varianti ─────────────────────────────────────────────────
#   dis_  (Disney BSDF) — close-up, effetti PBR avanzati
#   cls_  (Classic)      — superfici grandi, render veloci
#
# ── Categorie ────────────────────────────────────────────────────────────
#   <CATEGORIA 1>    <descrizione breve>
#   <CATEGORIA 2>    <descrizione breve>
#   ...
# ============================================================================

materials:
  # ═══════════════════════════════════════════════════════════════════════
  #  <CATEGORIA 1> — <descrizione>
  # ═══════════════════════════════════════════════════════════════════════

  # ── Disney ─────────────────────────────────────────────────────────────
  # <Variante> — <descrizione breve>
  - id: "dis_<categoria>_<variante>"
    type: "disney"
    color: [R, G, B]
    roughness: ...
    metallic: ...

  # ── Classic ────────────────────────────────────────────────────────────
  - id: "cls_<categoria>_<variante>"
    type: "lambertian"    # oppure "metal", "dielectric"
    color: [R, G, B]
```

**Regole materiali:**
- Ogni materiale in doppia variante: `dis_` (Disney) + `cls_` (Classic)
- Eccezioni: emissivi (nessun prefisso), dielectric (solo `cls_` o nessun prefisso)
- ID: `prefisso_categoria_variante` in snake_case (es. `dis_rubino_taglio_brillante`)
- Colori RGB: valori fisicamente plausibili 0.0–1.0
- Raggruppare per sottocategoria con separatori `# ═══`
- Commento prima di ogni materiale che spiega l'aspetto
- Obiettivo: 30–100+ materiali per file

---

### Lights — `scenes/libraries/lights/<categoria>-<mood>.yaml`

**Struttura file:**

```yaml
# =============================================================================
#  LIBRERIA LUCI — <Nome Setup>
#  scenes/libraries/lights/<file>.yaml
# =============================================================================
#
#  <Descrizione del setup e del suo scopo>.
#
#  SCHEMA:
#    <Ruolo 1> — <tipo e posizione> → <effetto>
#    <Ruolo 2> — <tipo e posizione> → <effetto>
#    ...
#
#  LUCI USATE:  <tipo> · <tipo> · <tipo>
#
#  USO CONSIGLIATO:
#    imports:
#      - path: "libraries/lights/<file>.yaml"
#
#  WORLD ABBINATO:
#    world:
#      ambient_light: [R, G, B]
#      background: [R, G, B]
#
#  RENDER CONSIGLIATO:
#    Draft  : -w 400  -H 225 -s 64   -d 4  -S 1
#    Preview: -w 800  -H 450 -s 256  -d 6
#    Final  : -w 1920 -H 1080 -s 1024 -d 8  -S 4
#
#  NOTE:
#    - Il soggetto è assunto centrato attorno all'origine, altezza ~1–2 u.
#    ...
# =============================================================================

lights:
  # ── KEY LIGHT ─────────────────────────────────────────────────────────
  #  <Descrizione ruolo e motivazione>.
  - type: "..."
    ...

  # ── FILL LIGHT ────────────────────────────────────────────────────────
  - type: "..."
    ...

  # ── VARIANTE ALTERNATIVA ──────────────────────────────────────────────
  #  <Descrizione>.
  #- type: "..."
  #  ...
```

**Regole luci:**
- L'header DEVE includere: schema luci, world abbinato, render consigliato
- Ogni luce con commento che spiega il ruolo (key, fill, rim, accent, ecc.)
- Includere almeno 1 variante alternativa commentata
- Naming file: `<categoria>-<mood>.yaml` (es. `outdoor-storm.yaml`, `studio-lowkey.yaml`)
- Soggetto assunto al centro, ~1–2 unità di altezza

---

### Objects — `scenes/libraries/objects/<tema>.yaml`

**Struttura file:**

```yaml
# ============================================================================
# Libreria Oggetti — <Tema>
# ============================================================================
#
# Importa con:  imports: [{ path: "libraries/objects/<tema>.yaml" }]
#
# <Descrizione della raccolta>.
#
# ── Materiali inclusi ────────────────────────────────────────────────────
#   Prefisso ID:  <pfx>_   (<tema abbreviato>)
#
# ── Convenzioni di design ────────────────────────────────────────────────
#   ORIGINE    Base a Y=0, centrati in XZ
#   SCALA      Dimensioni in unità ≈ metri
#   DETTAGLIO  Geometria più ricca possibile (torus, CSG, ellissoidi)
#
# ── Catalogo template ────────────────────────────────────────────────────
#   <TEMPLATE_1>   <dimensioni>. <descrizione>.
#   <TEMPLATE_2>   <dimensioni>. <descrizione>.
#   ...
# ============================================================================

materials:
  # ── Materiali di default per i template ────────────────────────────────
  - id: "<pfx>_<materiale>"
    type: "disney"
    ...

templates:
  - name: "<nome_template>"
    children:
      - name: "<parte>"
        type: "<primitiva>"
        ...
```

**Regole oggetti:**
- Ogni template con base a Y=0, centrato in XZ
- Dimensioni realistiche (≈ metri)
- Materiali di default inclusi nel file, prefisso dedicato (es. `frn_`, `lab_`, `mus_`)
- Catalogo completo nell'header con dimensioni e descrizione
- Istanziabile con `type: "instance"`, `template: "<nome>"`, `translate: [x, 0, z]`
- Usare geometrie ricche: torus per bordi, sfere scalate per ellissoidi, CSG per volumi cavi
- Obiettivo: 10–15 template per file

---

### Starter Kits — `scenes/libraries/starter-kits/starter-<tema>.yaml`

**Struttura file:**

```yaml
# ═══════════════════════════════════════════════════════════════════════════
#  STARTER KIT — <Nome Scena>
#
#  <Descrizione ambientazione e contenuto>.
#
#  AMBIENTAZIONE: <Indoor/Outdoor> — <dettaglio>
#  STILE: <mood/estetica>
#
#  Render consigliato:
#    Draft:    -w 400  -H 225  -s 64   -d <D>  -S 1
#    Preview:  -w 800  -H 450  -s 256  -d <D>
#    Finale:   -w 1920 -H 1080 -s 1024 -d <D>  -S 4
# ═══════════════════════════════════════════════════════════════════════════

world: ...          # ATTIVO + almeno 2 alternative commentate

cameras: ...        # 4–6 camere con nomi descrittivi

lights: ...         # Setup attivo + variante commentata

materials: ...      # Tutti inline, autocontenuto

templates: ...      # Oggetti composti della scena

entities: ...       # Composizione completa, renderizzabile
```

**Regole starter kit:**
- File completamente autocontenuto (nessun import)
- Renderizzabile immediatamente senza modifiche
- World attivo + almeno 2 alternative commentate con annotazione dello stile
- 4–6 camere con nomi descrittivi
- Materiali realistici e vari (mix di Disney, lambertian, metal, dielectric)
- Template per oggetti ripetuti
- Naming: `starter-<tema>.yaml`

---

## Dopo la generazione

1. Aggiornare il README della categoria (`scenes/libraries/<categoria>/README.md`) con il nuovo file
2. Aggiornare il README principale (`scenes/libraries/README.md`) se aggiunge una nuova sottocategoria
3. Confermare con: nome file, numero elementi (materiali/template/luci), categorie coperte

## Checklist finale

- [ ] File nella directory corretta: `scenes/libraries/<categoria>/`
- [ ] Naming file corretto per la categoria
- [ ] Header commento con descrizione, istruzioni di import e catalogo
- [ ] ID materiali univoci con prefisso corretto (`dis_`/`cls_`/`<pfx>_`)
- [ ] Nessun duplicato con file esistenti nella stessa categoria
- [ ] Template con base a Y=0, centrati in XZ (solo objects)
- [ ] World abbinato e render consigliato nell'header (solo lights)
- [ ] Almeno 1 variante commentata (lights e starter-kits)
- [ ] README della categoria aggiornato
