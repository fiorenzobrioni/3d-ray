# DevLog

Cartella dei log di sviluppo e note di design del progetto. Per roadmap, TODO e idee vedi [`PLANNING.md`](../PLANNING.md).

## Struttura

```text
devlog/
├── README.md              <- questo file, non modificare
├── devlog.md              <- log attivo corrente
└── devlog-YYYY-MM-DD.md   <- log archiviati (la data indica quando è stato archiviato)
```

Esiste sempre un solo file attivo: `devlog.md`. Quando questo file raggiunge o supera le 1000 righe, viene archiviato rinominandolo e ne viene creato uno nuovo vuoto (usando il template fornito qui sotto).

## Regole di Scrittura e Archiviazione

- **Scrivi sempre nel file corrente:** `devlog.md`. Se non esiste, crealo usando il template qui sotto.
- **Ordine Cronologico Inverso:** L'ultimo sviluppo va inserito **sempre in cima** al file, subito sotto il titolo principale.
- **Procedura di Archiviazione (Superate le 1000 righe):**
  1. Rinomina l'attuale `devlog.md` in `devlog-YYYY-MM-DD.md` usando la data del giorno corrente.
  2. Non modificare più il file appena archiviato.
  3. Crea immediatamente un nuovo file `devlog.md` inserendo il template base riportato  qui sotto.
  4. Scrivi la tua nuova voce di log nel nuovo file.

## Template per un nuovo devlog.md

Copia il testo qui sotto quando crei un nuovo file `devlog.md`:

```markdown
# DEVLOG Latent

Diario di sviluppo del progetto. Le voci più recenti vanno in alto.
Ogni voce annota cosa è stato fatto, decisioni prese, problemi incontrati e cosa viene dopo.

Formato suggerito per ogni voce:

## YYYY-MM-DD - Titolo breve
**Fatto:** cosa è stato completato  
**Decisioni:** scelte tecniche/di design e il perché  
**Problemi:** cosa si è bloccato e come (o se) è stato risolto  
**Prossimo:** il passo successivo  

---
```
