---
description: "Converte i materiali classici (lambertian/metal/dielectric) di una scena 3D-Ray in materiali OpenPBR equivalenti, preservando i riferimenti nelle entità"
argument-hint: "<path/to/scene.yaml> [solo hero | tutti]"
---

# Skill: Convert Materials

Converte i materiali "classici" di una scena YAML (`lambertian`, `metal`, `dielectric`) nei loro equivalenti **OpenPBR** (`type: openpbr`), preservando tutti i riferimenti nelle `entities`.

## Input

File e scope: $ARGUMENTS

Interpreta come `<path/to/scene.yaml> [scope]` dove scope è:
- `hero` (default) — converte solo i materiali usati su hero object (oggetti principali, pochi)
- `tutti` — converte ogni materiale classico non-emissivo; avvisa che è un cambio pesante
- `<lista-id>` — converte solo gli ID indicati

Se manca il file, chiedi quale scena. Se la scena non ha `format_version: 2` è del formato 1: prima va convertita con `dotnet run --project src/Tools/SceneMigrate -- <scena>` (che trasforma anche i vecchi materiali `disney` in `openpbr`).

## Riferimenti

- Schema materiali → [docs/reference/scene-reference.md](../../docs/reference/scene-reference.md) §5
- Modello OpenPBR → [docs/technical/shading-model.md](../../docs/technical/shading-model.md)
- Convenzioni → [CLAUDE.md](../../CLAUDE.md)

## Principi di conversione

- **Preserva gli ID esistenti** — non cambiare `id:`, così le `entities` continuano a funzionare senza modifiche. (Se l'utente chiede la convenzione `pbr_` prefix, applica un rename coordinato con aggiornamento di tutti i riferimenti.)
- **Emissive non si converte**: lascia `emissive` invariati (l'emissione di `openpbr` non è campionata come luce).
- **Dielectric**: OpenPBR lo rappresenta con `transmission_weight: 1` + `specular_ior`, con gli stessi risultati per il vetro liscio. Converti solo se l'utente lo chiede esplicitamente o se serve un parametro in più (coat, film sottile, tinta in profondità).
- **Mix material**: se un mix referenzia un materiale che stai convertendo, il mix continua a funzionare (stesso ID). Non convertire ricorsivamente l'albero — solo le foglie classiche.
- **Raggruppa le superfici grandi** (pavimenti, muri, soffitti): avvisa l'utente prima di convertirle. OpenPBR su superfici grandi è più costoso e spesso indistinguibile da `lambertian`. Di default NON convertirle.
- Ogni chiave va scritta con il nome del formato 2: il loader rifiuta chiavi sconosciute (con un suggerimento) e avvisa per le chiavi senza effetto sul tipo.

## Tabella di conversione

### lambertian → openpbr

```yaml
# Prima
- id: "legno_scuro"
  type: "lambertian"
  color: [0.3, 0.2, 0.1]

# Dopo
- id: "legno_scuro"
  type: openpbr
  base_color: [0.3, 0.2, 0.1]
  specular_roughness: 0.85     # lambertian puro → matte
  specular_weight: 0.6         # riflesso ambientale minimo per realismo
```

**Rugosità inferita dall'aspetto implicito:**
- Materiale generico diffuso → `0.85`
- Materiale che dovrebbe avere finish satinato (pelle liscia, tessuto seta) → `0.5–0.7`
- Materiale "liscio" ma non lucido (plastica opaca) → `0.4–0.5`

### metal → openpbr

```yaml
# Prima
- id: "oro"
  type: "metal"
  color: [1.0, 0.84, 0.0]
  roughness: 0.15

# Dopo
- id: "oro"
  type: openpbr
  base_color: [1.0, 0.84, 0.0]
  base_metalness: 1.0
  specular_roughness: 0.15     # stessa scala della roughness del metal (alpha = r²)
```

- Un `metal` con `preset` (misurato) va lasciato `metal`: OpenPBR usa il modello F82 (`base_color` + `specular_color` come colore radente), non l'IOR complesso.
- `edge_color` del metal → `specular_color` di OpenPBR.

### dielectric → openpbr (opzionale, solo se richiesto)

```yaml
# Prima
- id: "vetro_trasparente"
  type: "dielectric"
  ior: 1.5

# Dopo
- id: "vetro_trasparente"
  type: openpbr
  specular_roughness: 0.0
  specular_ior: 1.5
  transmission_weight: 1.0
```

**Nota**: il `color` del dielectric è una tinta applicata alla rifrazione; in OpenPBR è `transmission_color` (una volta all'ingresso con `transmission_depth: 0`, oppure la trasmittanza a quella profondità).

### lambertian → openpbr con SSS (pelle, cera, alabastro)

Se il `lambertian` rappresentava una superficie organica traslucente (pelle, cera, frutta, alabastro), aggiungi il subsurface durante la conversione:

```yaml
- id: "pelle_chiara"
  type: openpbr
  base_color: [0.95, 0.78, 0.65]
  specular_roughness: 0.75
  specular_weight: 0.4
  subsurface_weight: 1.0                        # attiva il random walk
  subsurface_color: [1.0, 0.85, 0.70]           # albedo che mostra un pezzo spesso
  subsurface_radius: 1.0                        # cammino libero medio (unità di scena)
  subsurface_radius_scale: [0.35, 0.18, 0.12]   # per canale r, g, b
```

- `subsurface_radius` è **uno scalare**; il colore del cammino libero medio va in `subsurface_radius_scale`.
- Il loader costruisce il mezzo e lo lega a ogni entità che usa il materiale (un `interior_medium` esplicito vince).
- Le scene con SSS beneficiano di `-q pre-final` o superiore (il random walk è senza tetti di rimbalzi: servono solo più campioni).

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
- Aggiungi un commento `# Convertito lambertian → openpbr` o simile solo se chiarisce
- Non toccare `entities` — gli ID restano invariati
- Se non vuoi sostituire un materiale classico ma serve comunque una versione OpenPBR, crea un nuovo materiale con prefisso `pbr_` accanto e aggiorna solo le entità che vuoi convertire

### 4. Validazione post-conversione

- [ ] La scena carica senza errori (il loader controlla chiavi, tipi e valori con riga e colonna)
- [ ] Nessun ID duplicato
- [ ] Tutti i riferimenti in `entities` risolti
- [ ] Mix material che referenziano convertiti: ancora validi
- [ ] Eventuale raccomandazione di alzare `-s` (OpenPBR più rumoroso su scene con molti lobi)

## Output

Riepilogo della conversione:
- N materiali convertiti, N saltati, N ID aggiunti (se rename)
- Lista dei materiali convertiti con mapping rugosità/metalness scelti
- Raccomandazioni di profilo render (se la scena ora richiede più campioni o `-d` più alto)
- Comando preview suggerito per verificare la resa: `/render <scena>` o comando equivalente
