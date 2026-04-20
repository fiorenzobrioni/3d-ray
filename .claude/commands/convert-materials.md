---
description: "Converte i materiali classici (lambertian/metal/dielectric) di una scena 3D-Ray in Disney BSDF PBR equivalenti, preservando i riferimenti nelle entità"
argument-hint: "<path/to/scene.yaml> [solo hero | tutti]"
---

# Skill: Convert Materials

Converte i materiali "classici" di una scena YAML (`lambertian`, `metal`, `dielectric`) nei loro equivalenti **Disney BSDF** PBR, preservando tutti i riferimenti nelle `entities`.

## Input

File e scope: $ARGUMENTS

Interpreta come `<path/to/scene.yaml> [scope]` dove scope è:
- `hero` (default) — converte solo i materiali usati su hero object (oggetti principali, pochi)
- `tutti` — converte ogni materiale classico non-emissivo; avvisa che è un cambio pesante
- `<lista-id>` — converte solo gli ID indicati

Se manca il file, chiedi quale scena.

## Riferimenti

- Schema materiali → [docs/reference/scene-reference.md](../../docs/reference/scene-reference.md)
- Disney BSDF → [docs/technical/shading-model.md](../../docs/technical/shading-model.md)
- Convenzioni → [CLAUDE.md](../../CLAUDE.md)

## Principi di conversione

- **Preserva gli ID esistenti** — non cambiare `id:`, così le `entities` continuano a funzionare senza modifiche. (Se l'utente chiede la convenzione `dis_` prefix, applica un rename coordinato con aggiornamento di tutti i riferimenti.)
- **Emissive non si converte**: Disney BSDF non copre l'emissione. Lascia `emissive` invariati.
- **Dielectric**: Disney li può rappresentare via `spec_trans: 1.0` + `ior`, ma con compromessi. Converti solo se l'utente lo chiede esplicitamente.
- **Mix material**: se un mix referenzia un materiale che stai convertendo, il mix continua a funzionare (stesso ID). Non convertire ricorsivamente l'albero — solo le foglie classiche.
- **Raggruppa le superfici grandi** (pavimenti, muri, soffitti): avvisa l'utente prima di convertirle. Disney su superfici grandi è costoso e spesso indistinguibile da `lambertian`. Di default NON convertirle.

## Tabella di conversione

### lambertian → disney

```yaml
# Prima
- id: "legno_scuro"
  type: "lambertian"
  color: [0.3, 0.2, 0.1]

# Dopo
- id: "legno_scuro"
  type: "disney"
  color: [0.3, 0.2, 0.1]
  metallic: 0.0
  roughness: 0.85       # lambertian puro → matte
  specular: 0.3         # riflesso ambientale minimo per realismo
```

**Roughness inferita dall'aspetto implicito:**
- Materiale generico diffuso → `0.85`
- Materiale che dovrebbe avere finish satinato (pelle liscia, tessuto seta) → `0.5–0.7`
- Materiale "liscio" ma non lucido (plastica opaca) → `0.4–0.5`

### metal → disney

```yaml
# Prima
- id: "oro"
  type: "metal"
  color: [1.0, 0.84, 0.0]
  fuzz: 0.15

# Dopo
- id: "oro"
  type: "disney"
  color: [1.0, 0.84, 0.0]
  metallic: 1.0
  roughness: 0.15       # fuzz ≈ roughness (mappatura diretta)
  specular: 1.0
```

**Mapping fuzz → roughness:**
- `fuzz 0.0` → `roughness 0.05` (specchio)
- `fuzz 0.1–0.3` → `roughness 0.1–0.3` (metallo spazzolato)
- `fuzz 0.5+` → `roughness 0.5+` (metallo molto ruvido, quasi diffuso)

### dielectric → disney (opzionale, solo se richiesto)

```yaml
# Prima
- id: "vetro_trasparente"
  type: "dielectric"
  refraction_index: 1.5

# Dopo
- id: "vetro_trasparente"
  type: "disney"
  color: [1.0, 1.0, 1.0]
  metallic: 0.0
  roughness: 0.0
  specular: 0.5
  spec_trans: 1.0
  ior: 1.5
```

**Nota**: `dielectric` classico ha comportamento fisicamente corretto per vetro puro. Disney con `spec_trans: 1.0` approssima lo stesso effetto ma con più parametri tunabili. Per scene con molti vetri (bicchieri, prismi), valuta se la conversione porta valore.

## Procedura

### 1. Analisi

Leggi il file e produci l'inventario:

| Materiale | Tipo attuale | Usato da (entità/ground/template) | Azione proposta |
|-----------|--------------|-----------------------------------|-----------------|
| `legno_scuro` | lambertian | tavolo, sedia, mensola | Convertire (hero) |
| `pavimento` | lambertian | ground | Saltare (superficie grande) |
| `oro_anello` | metal | anello | Convertire (hero) |
| `vetro` | dielectric | bicchiere | Chiedere conferma |
| `candela_fiamma` | emissive | fiamma | Saltare (emissive) |

### 2. Proposta

Mostra all'utente:
- Quali materiali verranno convertiti
- Quali saranno preservati (e perché)
- Esempio del prima/dopo su 1–2 materiali rappresentativi
- Avviso sulle superfici grandi se presenti
- Avviso sui `dielectric` se presenti

### 3. Applicazione

- Modifica **in place** i materiali nel file YAML
- Mantieni i commenti esistenti sopra ogni materiale (se presenti)
- Aggiungi un commento `# Convertito lambertian → disney` o simile solo se chiarisce
- Non toccare `entities` — gli ID restano invariati
- Non toccare `imports` — se un materiale classico viene da libreria, NON modificarlo (è condiviso con altre scene). Se serve comunque una versione Disney, crea un nuovo materiale con prefisso `dis_` accanto e aggiorna le entità che vuoi convertire

### 4. Validazione post-conversione

- [ ] YAML sintatticamente valido
- [ ] Nessun ID duplicato
- [ ] Tutti i riferimenti in `entities` risolti
- [ ] Mix material che referenziano convertiti: ancora validi
- [ ] Eventuale raccomandazione di alzare `-s` (Disney più rumoroso su scene con molti lobi)

## Output

Riepilogo della conversione:
- N materiali convertiti, N saltati, N ID aggiunti (se rename)
- Lista dei materiali convertiti con mapping roughness/metallic scelti
- Raccomandazioni di profilo render (se la scena ora richiede più campioni o `-d` più alto)
- Comando preview suggerito per verificare la resa: `/render <scena>` o comando equivalente
