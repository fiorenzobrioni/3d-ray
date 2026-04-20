---
description: "Arricchisce una scena YAML 3D-Ray esistente: upgrade materiali, aggiunta dettagli geometrici, miglioramento illuminazione, effetti atmosferici"
argument-hint: "<path/to/scene.yaml> [aspetto da migliorare]"
---

# Skill: Upgrade Scene

Prende una scena YAML esistente e la **arricchisce progressivamente**: materiali più realistici, dettagli geometrici aggiuntivi, illuminazione più articolata, atmosfera. Obiettivo: aumentare la qualità visiva senza stravolgere la composizione esistente.

## Input

File e aspetto da migliorare: $ARGUMENTS

Interpreta come `<path/to/scene.yaml> [aspetto opzionale]`. Se manca il file, chiedi quale scena. Se manca l'aspetto, analizza la scena e proponi il piano di upgrade più appropriato.

## Riferimenti

- Schema YAML → [docs/reference/scene-reference.md](../../docs/reference/scene-reference.md)
- Convenzioni → [CLAUDE.md](../../CLAUDE.md)
- Librerie riutilizzabili → [scenes/libraries/README.md](../../scenes/libraries/README.md)

## Fase 1 — Analisi

Leggi la scena e produci un inventario:

| Metrica | Valore |
|---------|--------|
| Entità totali | — |
| Materiali (Disney / Classic / altro) | — / — / — |
| Luci (per tipo) | — |
| Texture usate | sì/no, quali |
| Gruppi / CSG | sì/no, conteggio |
| Atmosfera (HDRI, sky, medium) | — |
| Camere | conteggio + tipi |

Dichiara **cosa manca** per una scena ricca (riferimento: budget di `/sculpt-scene`).

## Fase 2 — Proposta di upgrade

Presenta un piano strutturato con **livelli di intervento**, dal meno al più invasivo:

### Livello 1 — Materiali (non tocca la geometria)

- Convertire lambertian/metal su hero objects in `disney` con parametri PBR coerenti
- Aggiungere texture procedurali (marble, wood, checker, noise) dove c'è tinta piatta
- Introdurre `mix_material` per effetti di usura (ruggine, polvere, muschio) su superfici che beneficiano del realismo
- Aggiungere normal map su superfici piatte che dovrebbero avere micro-dettaglio (pietra, tessuto, intonaco)

### Livello 2 — Illuminazione (non tocca la geometria)

- Analizzare setup attuale: quale ruolo manca (key/fill/rim/practicals)
- Aggiungere fill opposto alla key se le ombre sono nere
- Aggiungere rim/back light se il soggetto si confonde con lo sfondo
- Aggiungere practicals visibili (candele, lampadine) per motivazione narrativa
- Rivedere temperature colore per contrasto caldo/freddo leggibile
- Se scena esterna: passare a HDRI o sky gradient con sun

### Livello 3 — Atmosfera

- Aggiungere `homogeneous` / `height_fog` con `sigma_s` basso per profondità atmosferica
- Per interni oscuri: `medium` globale tenue + practicals emissivi → god-rays
- Per esterni: HDRI appropriato (sunset, overcast, noon)

### Livello 4 — Geometria (più invasivo, da concordare)

- Scomporre oggetti monolitici in `group` con figli (es. un tavolo nudo → tavolo + gambe + traversi)
- Introdurre CSG per smussi, incavi, fori che attualmente mancano
- Aggiungere dettagli secondari (rivetti, cornici, basi) che migliorano la lettura 3D
- Promuovere oggetti ripetuti a `template` + istanze

### Livello 5 — Camere

- Aggiungere camera cinematografica (angolo basso, dutch, bokeh forte) se manca
- Aggiungere closeup su area di alta qualità per valorizzare l'upgrade
- Usa `/add-camera` se già disponibile

## Fase 3 — Scelta dell'utente

Dopo la proposta, chiedi all'utente:
1. **Tutti i livelli** in ordine (1→5) — upgrade completo
2. **Solo livelli specifici** — es. "solo materiali e luci"
3. **Aspetto specifico** — es. "rendi il pavimento più realistico"

Se l'utente ha già indicato l'aspetto in `$ARGUMENTS`, salta questa domanda e vai direttamente all'implementazione.

## Fase 4 — Applicazione

Regole durante l'upgrade:

- **Preserva ID esistenti** quando possibile — evita di rompere riferimenti in `entities`
- **Aggiungi, non sostituire** — se un materiale classico è usato in 10 entità, affianca la versione Disney (`dis_<nome>`) e aggiornala selettivamente sugli hero objects, lasciando il Classic per le istanze secondarie
- **Preserva le camere esistenti** — non cambiare `position`/`look_at` senza chiedere
- **Preserva il mood** — se la scena è notturna, non spostarla in pieno giorno; arricchisci mantenendo l'atmosfera
- **Commenti**: aggiungi una sezione in testa al file (o sotto l'header esistente) con la nota `# UPGRADE: <data/descrizione>` che elenca cosa è stato arricchito

## Fase 5 — Validazione finale

Prima di confermare, verifica:
- [ ] YAML ancora valido (imports, riferimenti materiali, template)
- [ ] Nessun materiale orfano o fantasma introdotto
- [ ] Render depth `-d` ancora adeguato — se ho aggiunto dielectric stratificato o medium denso, alzare e documentare
- [ ] Conteggio entità/materiali/luci aggiornato nell'header

## Output

Diff riassuntivo delle modifiche applicate:
- Materiali aggiunti / convertiti
- Luci aggiunte
- Atmosfera introdotta
- Dettagli geometrici aggiunti
- Cambi a render profile consigliati (se `-d` o `-C` vanno aggiornati)
- Comando preview per il primo render di verifica
