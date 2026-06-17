# devlog

Cartella dei log di sviluppo e note di design del progetto.

## Struttura

```
devlog/
├── README.md          <- questo file, non modificare
├── CURRENT.md         <- log attivo, sviluppi piu recenti in cima
├── 2024/
│   ├── 2024-03-10.md  <- log storico (archiviato il 10 marzo 2024)
│   └── 2024-09-22.md
└── 2025/
    └── 2025-06-17.md  <- log storico piu recente
```

- `CURRENT.md` - log attivo
- `YYYY/YYYY-MM-DD.md` - log storici archiviati, organizzati per anno di contenuto

## Regole di archiviazione

Il log attivo (`CURRENT.md`) viene archiviato quando:

- supera circa 800 righe, oppure
- si inserisce il primo sviluppo di un anno diverso da quello indicato in `CURRENT.md` (il log viene archiviato prima dell'inserimento)

**Come archiviare:** spostare `CURRENT.md` in `YYYY/YYYY-MM-DD.md`, dove `YYYY` e l'anno indicato nell'intestazione del file e `YYYY-MM-DD` e la data odierna. Creare un nuovo `CURRENT.md` vuoto usando il template qui sotto.

## Template CURRENT.md iniziale

```markdown
# DEVLOG corrente - {nome repo}

Log attivo. Ultimo sviluppo in cima. Per lo storico vedi le sottocartelle per anno.

**Anno di inizio:** YYYY

> Stati: `✅ Fatto` - `🔧 In corso` - `⬜ Da fare`
> Usati nel titolo della sezione per lo stato complessivo del ciclo, e nei sotto-punti per le parti ancora aperte o in lavorazione.

---
```
