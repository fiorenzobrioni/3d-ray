# devlog

Cartella dei log di sviluppo e note di design del progetto. Per roadmap, TODO e bug noti vedi `PLANNING.md`.

## Struttura

```
devlog/
├── README.md   <- questo file, non modificare
├── 2025.md     <- log 2025
└── 2026.md     <- log 2026 (anno piu alto = log attivo)
```

Un file per anno. Il file con l'anno piu alto e sempre quello attivo: nessun file speciale, il nome dice tutto.

## Regole

- Scrivi sempre nel file dell'anno corrente (`YYYY.md`). Se non esiste, crealo usando il template qui sotto.
- Quando cambia l'anno, crea il nuovo `YYYY.md` e non toccare piu il vecchio.
- Ultimo sviluppo sempre in cima al file.
- Se un file supera circa 1000 righe (caso raro), spezzalo in `YYYY-a.md` e `YYYY-b.md`.

## Template per un nuovo anno

```markdown
# DEVLOG {nome repo} - YYYY

Log di sviluppo YYYY. Ultimo sviluppo in cima. Per roadmap e TODO vedi `PLANNING.md`.

> Stati: `✅ Fatto` - `🔧 In corso` - `⬜ Da fare`
> Usati nel titolo della sezione per lo stato complessivo del ciclo, e nei sotto-punti per le parti ancora aperte o in lavorazione.

---
```
