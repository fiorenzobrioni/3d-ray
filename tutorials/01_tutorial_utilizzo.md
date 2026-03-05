# Tutorial: Utilizzo dell'App RayTracer

## Indice
1. [Prerequisiti](#1-prerequisiti)
2. [Primo Avvio](#2-primo-avvio)
3. [Parametri CLI](#3-parametri-cli)
4. [Esempi Pratici](#4-esempi-pratici)
5. [Ottimizzazione Render](#5-ottimizzazione-render)
6. [Risoluzione Problemi](#6-risoluzione-problemi)

---

## 1. Prerequisiti

- **.NET 10 SDK** installato e disponibile nel PATH
- Il progetto compilato con successo:
  ```bash
  dotnet build
  ```

---

## 2. Primo Avvio

Il modo più semplice per avviare il renderer è usare la scena di esempio inclusa:

```bash
dotnet run -- --input ../../scenes/sample.yaml --output render.png
```

Questo produrrà un'immagine `render.png` a **1280×720** con **16 campioni per pixel** e **50 rimbalzi massimi** (valori di default).

### Output tipico della console

```
╔══════════════════════════════════════════╗
║       RayTracer .NET 10 Engine           ║
╚══════════════════════════════════════════╝

  Scene:       ../../scenes/sample.yaml
  Output:      render.png
  Resolution:  1280 x 720
  Samples/px:  16
  Max depth:   50

Loading scene... done (75 ms)
  Lights: 2

Rendering: 100.0% (720/720 scanlines)
Render completed in 12.34s
Saving render.png... done!

Output saved to: C:\Fiorenzo\Experiments\3d-ray\src\RayTracer\render.png
```

---

## 3. Parametri CLI

| Parametro | Alias | Default | Descrizione |
|-----------|-------|---------|-------------|
| `--input` | `-i` | `scenes/sample.yaml` | Percorso del file `.yaml` della scena |
| `--output` | `-o` | `render.png` | Nome del file immagine generato |
| `--width` | — | `1280` | Larghezza dell'immagine in pixel |
| `--height` | — | `720` | Altezza dell'immagine in pixel |
| `--samples` | `-s` | `16` | Campioni per pixel (anti-aliasing) |
| `--depth` | `-d` | `50` | Massimo numero di rimbalzi del raggio |

### Formati di output supportati

Il formato viene determinato automaticamente dall'estensione del file:

| Estensione | Formato |
|------------|---------|
| `.png` | PNG (lossless, consigliato) |
| `.jpg` / `.jpeg` | JPEG (compresso con perdita) |
| `.bmp` | Bitmap (non compresso) |

---

## 4. Esempi Pratici

### 4.1 — Render veloce di anteprima

Per una preview rapida con bassa qualità (ideale per testare la composizione della scena):

```bash
dotnet run -- -i ../../scenes/sample.yaml -o preview.png --width 320 --height 180 -s 1 -d 5
```

- **1 campione/pixel**: niente anti-aliasing, immagine rumorosa ma velocissima
- **Profondità 5**: riflessi e rifrazioni limitati
- **Tempo stimato**: < 1 secondo

### 4.2 — Render di qualità media

Buon compromesso tra velocità e qualità:

```bash
dotnet run -- -i ../../scenes/sample.yaml -o medio.png --width 800 --height 450 -s 16 -d 20
```

- **16 campioni**: anti-aliasing visibile, rumore ancora presente nelle ombre morbide
- **Tempo stimato**: 5–15 secondi

### 4.3 — Render di alta qualità

Per il risultato finale:

```bash
dotnet run -- -i scenes/sample.yaml -o finale.png --width 1920 --height 1080 -s 128 -d 50
```

- **128 campioni**: immagine pulita con anti-aliasing di alta qualità
- **Profondità 50**: tutte le riflessioni multiple e rifrazioni del vetro
- **Tempo stimato**: da qualche minuto a decine di minuti, a seconda del numero di core

### 4.4 — Render in formato JPEG

```bash
dotnet run -- -i scenes/sample.yaml -o render.jpg --width 1280 --height 720 -s 32
```

### 4.5 — Render da una scena personalizzata

```bash
dotnet run -- -i C:\MieScene\stanza.yaml -o C:\Output\stanza_hd.png --width 2560 --height 1440 -s 64 -d 40
```

---

## 5. Ottimizzazione Render

### Campioni e Rumore

| Campioni (`-s`) | Qualità | Uso tipico |
|-----------------|---------|------------|
| 1–4 | Molto bassa | Test rapido posizionamento |
| 8–16 | Bassa-Media | Anteprima composizione |
| 32–64 | Media-Alta | Bozza quasi definitiva |
| 128–256 | Alta | Render finale |
| 512+ | Massima | Scene complesse con vetro/riflessi |

### Profondità e Rimbalzi

| Profondità (`-d`) | Effetto |
|-------------------|---------|
| 1–3 | Solo illuminazione diretta, niente riflessi |
| 5–10 | Riflessi singoli, rifrazione semplice |
| 20–50 | Riflessioni multiple, caustiche nel vetro |
| 50+ | Necessario solo per scene con molte superfici riflettenti annidate |

### Parallelismo

Il renderer sfrutta automaticamente **tutti i core logici** della CPU tramite `Parallel.For`. Non è necessaria alcuna configurazione. Il rendering scala linearmente con il numero di core disponibili.

### Strategia consigliata per lo sviluppo di scene

1. **Componi** la scena con `320×180`, `1 sample`, `depth 3` → pochi decimi di secondo
2. **Verifica** i materiali con `640×360`, `4 samples`, `depth 10` → circa 1 secondo
3. **Produci** il render finale con `1920×1080`, `128 samples`, `depth 50` → tempo variabile

---

## 6. Risoluzione Problemi

### L'immagine è completamente nera
- Verifica che la camera non sia dentro un oggetto
- Controlla che `focal_dist` sia positivo e coerente con la distanza tra `position` e `look_at`
- Assicurati che ci siano luci nella scena (o che vengano usate quelle di default)

### L'immagine è molto rumorosa (granulosa)
- Aumenta il numero di `--samples`
- Il rumore è normale con pochi campioni, specialmente nelle zone in ombra e nelle superfici di vetro

### Il file YAML non viene trovato
- Usa percorsi assoluti oppure verifica la directory di lavoro corrente
- Esempio con percorso assoluto: `--input C:\Scene\test.yaml`

### Errore "Failed to parse YAML scene"
- Verifica la sintassi YAML (indentazione con spazi, non tab)
- Controlla che tutti i campi obbligatori siano presenti
- Consulta il [Tutorial Scene](02_tutorial_scene.md) per lo schema corretto

### Il render è troppo lento
- Riduci risoluzione, campioni o profondità per i test
- Le sfere di vetro (`dielectric`) sono le più costose: ogni rimbalzo genera riflessione + rifrazione
