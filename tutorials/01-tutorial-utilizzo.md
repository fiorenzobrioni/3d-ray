# Tutorial: Utilizzo dell'App RayTracer

Benvenuto nel manuale d'uso del motore RayTracer. Questo documento ti guiderà attraverso le procedure di configurazione, i parametri di comando e le strategie per ottenere render professionali di alta qualità.

---

## Indice
1. [Prerequisiti di Sistema](#1-prerequisiti-di-sistema)
2. [Sintassi di Base](#2-sintassi-di-base)
3. [Guida ai Parametri CLI](#3-guida-ai-parametri-cli)
4. [Esempi di Rendering (Profili)](#4-esempi-di-rendering-profili)
5. [Gestione dell'Output](#5-gestione-delloutput)
6. [Ottimizzazione e Performance](#6-ottimizzazione-e-performance)
7. [Risoluzione Problemi](#7-risoluzione-problemi)

---

## 1. Prerequisiti di Sistema

Per eseguire il renderer sono necessari:
- **.NET 10 SDK** (o superiore).
- Risorse hardware: Il motore è multi-thread e scala linearmente con il numero di core della CPU.
- Compilazione del progetto:
  ```powershell
  dotnet build src\RayTracer\RayTracer.csproj
  ```

---

## 2. Sintassi di Base

L'applicazione viene eseguita tramite interfaccia a riga di comando (CLI). La struttura standard del comando è:

```powershell
dotnet run --project src\RayTracer\RayTracer.csproj -- [parametri]
```

Se preferisci usare l'eseguibile già compilato:
```powershell
.\src\RayTracer\bin\Debug\net10.0\RayTracer.exe [parametri]
```

### Esempio rapido:
```powershell
dotnet run --project src\RayTracer\RayTracer.csproj -- --input scenes\chess.yaml --output render.png --width 800 --height 600
```

---

## 3. Guida ai Parametri CLI

Il motore accetta i seguenti parametri per configurare l'esecuzione:

| Parametro | Alias | Default | Descrizione |
|-----------|-------|---------|-------------|
| `--input` | `-i` | — (**obbligatorio**) | Percorso del file di scena (YAML). |
| `--output` | `-o` | `render.png` | Nome e percorso del file immagine da generare. |
| `--width` | — | `1200` | Larghezza dell'immagine in pixel. |
| `--height` | — | `800` | Altezza dell'immagine in pixel. |
| `--samples` | `-s` | `16` | Numero di raggi per pixel (anti-aliasing e riduzione rumore). |
| `--depth` | `-d` | `50` | Massimo numero di rimbalzi per ogni raggio (riflessi, rifrazioni). |
| `--help` | `-h` | — | Mostra il messaggio di aiuto. |

### Note sui Samples (Anti-Aliasing)

Il motore usa il **campionamento stratificato (jittered)**: i samples vengono distribuiti in una griglia `√N × √N` all'interno di ogni pixel, con un piccolo jitter casuale. Questo garantisce una convergenza molto più rapida rispetto al campionamento puramente casuale.

Il numero effettivo di samples è arrotondato al quadrato perfetto superiore (es. 16 → 4×4 = 16, 20 → 5×5 = 25, 64 → 8×8 = 64).

### Tone Mapping

Il motore utilizza il **tone mapping ACES filmic** con correzione gamma 2.2. Questo produce:
- Rolloff naturale degli highlights (le luci non "esplodono" in bianco puro)
- Colori più ricchi e saturi nelle mezzatinte
- Gestione corretta di scene HDR con forte contrasto

---

## 4. Esempi di Rendering (Profili)

### 4.1 — Profilo "FAST PREVIEW" (Bozza Immediata)
Ideale per testare la posizione della camera o delle luci.
```powershell
dotnet run --project src\RayTracer\RayTracer.csproj -- -i scenes\chess.yaml -s 1 -d 5 --width 400 --height 300
```
- **Qualità**: Molto rumorosa (niente anti-aliasing effettivo).
- **Tempo**: < 1 secondo.

### 4.2 — Profilo "DRAFT" (Qualità Media)
Consigliato per valutare texture procedurali e materiali metallici.
```powershell
dotnet run --project src\RayTracer\RayTracer.csproj -- -i scenes\chess.yaml -s 16 -d 20 --width 800 --height 600
```
- **Qualità**: Buona pulizia globale, rumore residuo nelle ombre.
- **Tempo**: 5 — 20 secondi.

### 4.3 — Profilo "PRODUCTION" (Alta Qualità)
Da utilizzare per il risultato finale o per scene con molto vetro.
```powershell
dotnet run --project src\RayTracer\RayTracer.csproj -- -i scenes\chess.yaml -s 128 -d 50 --width 1920 --height 1080
```
- **Qualità**: Immagine cristallina, ombre morbide perfette.
- **Tempo**: Minuti (dipende dalla CPU e dalla complessità della scena).

### 4.4 — Profilo "ULTRA" (Qualità Massima)
Per render finali con vetro, DOF e riflessi multipli.
```powershell
dotnet run --project src\RayTracer\RayTracer.csproj -- -i scenes\chess.yaml -s 256 -d 50 --width 3840 --height 2160
```
- **Qualità**: Qualità fotorealistica, zero rumore visibile.
- **Tempo**: Da minuti a ore.

---

## 5. Gestione dell'Output

Il motore determina il formato dell'immagine salvata in base all'estensione del file specificato in `--output`:

| Formato | Estensione | Note |
|---------|------------|------|
| **PNG** | `.png` | Consigliato. Formato lossless (senza perdita di qualità). |
| **JPEG** | `.jpg` / `.jpeg` | Compresso. Utile per file leggeri, sconsigliato per qualità massima. |
| **BMP** | `.bmp` | Formato grezzo non compresso. File molto pesanti. |

---

## 6. Ottimizzazione e Performance

### Accelerazione BVH (Bounding Volume Hierarchy)
Il motore costruisce automaticamente un albero BVH per scene con più di 4 oggetti. L'algoritmo usa l'euristica dell'**asse più lungo** per suddividere gli oggetti in modo ottimale, garantendo intersezioni raggio-oggetto in tempo **O(log N)**.

### Texture Procedurali e Randomizzazione
L'uso di texture avanzate come **Marble** o **Wood** con `randomize_offset: true` non impatta significativamente sui tempi di calcolo, poiché si basa su trasformazioni matematiche veloci applicate al punto di impatto.

### Parametro `--samples` (Il peso principale)
Il tempo di rendering è **proporzionale** al numero di samples. Raddoppiare i samples raddoppia il tempo di calcolo. 
- **Suggerimento**: Inizia sempre con pochi samples (`-s 1` o `-s 4`) per rifinire la scena prima di lanciare il render finale.

### Multi-Threading
Il motore parallelizza il rendering per scanline usando `Parallel.For`. Tutti i core della CPU vengono utilizzati automaticamente.

---

## 7. Risoluzione Problemi

### L'immagine è completamente nera?
1. Verifica che la camera non sia posizionata all'interno di un solido.
2. Controlla che siano presenti luci (`lights`) con `intensity` > 0.
3. Verifica che `background` non sia `[0, 0, 0]` — il background agisce come sorgente di illuminazione globale per i raggi rimbalzati.
4. Se vuoi una scena scura con solo luci esplicite, imposta `background: [0, 0, 0]` e `ambient_light: [0, 0, 0]`, poi aggiungi luci `point` o `spot` con intensità sufficienti.

### L'immagine ha zone troppo luminose (bianche)?
Il tone mapping ACES gestisce automaticamente l'HDR, ma valori di `intensity` troppo alti sulle luci possono saturare. Riduci l'intensità delle luci o allontanale dagli oggetti.

### Le texture appaiono "piatte" o orientate male?
Usa i parametri `rotation` e `offset` nella definizione della texture per orientare le venature. Consulta il [Tutorial Scene](02-tutorial-scene.md) per i dettagli tecnici.

### Errore di caricamento YAML?
Usa sempre percorsi relativi corretti rispetto alla cartella in cui lanci il comando, oppure usa **percorsi assoluti**.
```powershell
--input C:\Users\Nome\Documents\scena.yaml
```
