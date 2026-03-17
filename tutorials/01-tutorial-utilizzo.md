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
| `--input` | `-i` | `sample.yaml` | Percorso del file di scena (YAML). |
| `--output` | `-o` | `render.png` | Nome e percorso del file immagine da generare. |
| `--width` | — | `1280` | Larghezza dell'immagine in pixel. |
| `--height` | — | `720` | Altezza dell'immagine in pixel. |
| `--samples` | `-s` | `16` | Numero di raggi per pixel (anti-aliasing e riduzione rumore). |
| `--depth` | `-d` | `50` | Massimo numero di rimbalzi per ogni raggio (riflessi, rifrazioni). |

---

## 4. Esempi di Rendering (Profili)

### 4.1 — Profilo "FAST PREVIEW" (Bozza Immediata)
Ideale per testare la posizione della camera o delle luci.
```powershell
dotnet run --project src\RayTracer\RayTracer.csproj -- -i scenes\chess.yaml -s 1 -d 5 --width 400 --height 300
```
- **Qualità**: Molto rumorosa (niente anti-aliasing).
- **Tempo**: < 1 secondo.

### 4.2 — Profilo "DRAFT" (Qualità Media)
Consigliato per valutare texture procedurali e materiali metallici.
```powershell powershell
dotnet run --project src\RayTracer\RayTracer.csproj -- -i scenes\chess.yaml -s 16 -d 20 --width 800 --height 600
```
- **Qualità**: Buona pulizia globale, rumore residuo nelle ombre.
- **Tempo**: 5 - 20 secondi.

### 4.3 — Profilo "PRODUCTION" (Alta Qualità)
Da utilizzare per il risultato finale o per scene con molto vetro.
```powershell
dotnet run --project src\RayTracer\RayTracer.csproj -- -i scenes\chess.yaml -s 128 -d 50 --width 1920 --height 1080
```
- **Qualità**: Immagine cristallina, ombre morbide perfette.
- **Tempo**: Minuti (dipende dalla CPU).

---

## 5. Gestione dell'Output

Il motore determina il formato dell'immagine salvata in base all'estensione del file specificato in `--output`:

- **PNG (`.png`)**: Consigliato. Formato lossless (senza perdita di qualità).
- **JPEG (`.jpg` / `.jpeg`)**: Compresso. Utile per file leggeri.
- **BMP (`.bmp`)**: Formato grezzo non compresso.

---

## 6. Ottimizzazione e Performance

### Texture Procedurali e Randomizzazione
L'uso di texture avanzate come **Marble** o **Wood** con `randomize_offset: true` non impatta significativamente sui tempi di calcolo, poiché si basa su trasformazioni matematiche veloci applicate al punto di impatto.

### Parametro `--samples` (Il peso principale)
Il tempo di rendering è **proporzionale** al numero di samples. Raddoppiare i samples raddoppia il tempo di calcolo. 
- **Suggerimento**: Inizia sempre con pochi samples per rifinire la scena prima di lanciare il render finale.

### Accelerazione Hardware
Il motore utilizza una struttura **BVH (Bounding Volume Hierarchy)** per ottimizzare l'intersezione tra raggi e oggetti. Questo permette di gestire scene con migliaia di oggetti mantenendo tempi di calcolo accettabili.

---

## 7. Risoluzione Problemi

### L'immagine è completamente nera?
1. Verifica che la camera non sia posizionata all'interno di un solido.
2. Controlla che siano presenti luci (`lights`) o che la luce ambiente (`ambient_light`) non sia `[0,0,0]`.

### Le texture appaiono "piatte" o orientate male?
Usa i parametri `rotation` e `offset` nel file YAML della scena per orientare le venature. Consulta il [Tutorial Scene](02_tutorial_scene.md) per i dettagli tecnici.

### Errore di caricamento YAML?
Usa sempre percorsi relativi corretti rispetto alla cartella in cui lanci il comando, oppure usa **percorsi assoluti**.
```powershell
--input C:\Users\Nome\Documents\scena.yaml
```
