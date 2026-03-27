# Tutorial: Utilizzo dell'App RayTracer

Benvenuto nel manuale d'uso del motore RayTracer. Questo documento ti guiderà attraverso le procedure di configurazione, i parametri di comando e le strategie per ottenere render professionali di alta qualità.

---

## Indice
1. [Prerequisiti di Sistema](#1-prerequisiti-di-sistema)
2. [Sintassi di Base](#2-sintassi-di-base)
3. [Guida ai Parametri CLI](#3-guida-ai-parametri-cli)
4. [Esempi di Rendering (Profili)](#4-esempi-di-rendering-profili)
5. [Gestione dell'Output](#5-gestione-delloutput)
6. [Multi-Camera](#6-multi-camera)
7. [Ottimizzazione e Performance](#7-ottimizzazione-e-performance)
8. [Risoluzione Problemi](#8-risoluzione-problemi)

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
# Windows (PowerShell)
dotnet run --project src\RayTracer\RayTracer.csproj -- -i scenes\chess.yaml -o render.png -w 800 -H 600

# Linux / macOS (bash)
dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/chess.yaml -o render.png -w 800 -H 600
```

---

## 3. Guida ai Parametri CLI

Il motore accetta i seguenti parametri per configurare l'esecuzione:

| Parametro | Alias | Default | Descrizione |
|-----------|-------|---------|-------------|
| `--input` | `-i` | — (**obbligatorio**) | Percorso del file di scena (YAML). Il programma termina con un errore se non specificato. |
| `--output` | `-o` | `output/render-<scena>.png` | Nome e percorso del file immagine da generare. Se omesso, viene generato automaticamente dal nome della scena (es. `-i scenes/chess.yaml` → `output/render-chess.png`). Il formato viene rilevato automaticamente dall'estensione (`.png`, `.jpg`, `.bmp`). |
| `--width` | `-w` | `1200` | Larghezza dell'immagine in pixel. |
| `--height` | `-H` | `800` | Altezza dell'immagine in pixel. |
| `--samples` | `-s` | `16` | Campioni per pixel (anti-aliasing e riduzione del rumore). Vedi nota sotto. |
| `--depth` | `-d` | `50` | Massimo numero di rimbalzi ricorsivi per ogni raggio (riflessi, rifrazioni, scattering). |
| `--shadow-samples` | `-S` | *(da YAML)* | Override globale dei shadow samples per tutte le area light. Vedi nota sotto. |
| `--camera` | `-c` | *(prima camera)* | Seleziona la camera da usare per nome (case-insensitive) o indice 0-based. Funziona solo con la sintassi `cameras:` (lista) nel YAML. Vedi [sezione 6](#6-multi-camera). |
| `--list-cameras` | — | — | Elenca tutte le camere definite nella scena ed esce senza renderizzare. |
| `--help` | `-h` | — | Mostra il messaggio di aiuto ed esce. |

> **Nota sugli alias:** `-H` (maiuscola) è per `--height` perché `-h` è riservato a `--help`. Analogamente, `-S` (maiuscola) è per `--shadow-samples`, mentre `-s` (minuscola) è per `--samples`.

### Note sui Samples (Anti-Aliasing)

Il motore usa il **campionamento stratificato (jittered stratified sampling)**: i campioni vengono distribuiti su una griglia `√N × √N` all'interno di ogni pixel, con un piccolo jitter casuale per campione. Questo garantisce una convergenza molto più rapida rispetto al campionamento puramente casuale (Monte Carlo puro).

Il numero effettivo di campioni è sempre il **quadrato perfetto superiore** più vicino al valore fornito: ad esempio `-s 20` produce `5×5 = 25` campioni effettivi, `-s 64` produce `8×8 = 64`.

### Note sui Shadow Samples (`-S` / `--shadow-samples`)

Il parametro `--shadow-samples` (`-S`) consente di sovrascrivere globalmente il numero di shadow samples di **tutte** le area light nella scena, senza modificare il file YAML. Questo è particolarmente utile per iterare velocemente:

- **Preview rapido:** `-S 4` — ombre molto rumorose ma render quasi istantaneo.
- **Draft:** `-S 8` — buon compromesso per valutare l'illuminazione.
- **Produzione:** `-S 16` — qualità standard (default YAML).
- **Ultra:** `-S 32` — ombre morbidissime, massima qualità.

Se non specificato, ogni area light usa il proprio valore `shadow_samples` definito nel file YAML (default: 16).

> **⚠️ Costo computazionale:** Il costo reale per pixel è `samples × shadow_samples` per ogni area light. Con `-s 128 -S 32`, ogni pixel lancia `128 × 32 = 4096` raggi ombra per luce. Usa `-S 4` durante il draft!

### Tone Mapping ACES

L'output di ogni pixel viene processato attraverso una pipeline di post-processing:
1. **ACES Filmic Curve**: `(x * (2.51x + 0.03)) / (x * (2.43x + 0.59) + 0.14)` — produce un rolloff naturale degli highlight (le luci non "esplodono" in bianco puro) e colori più ricchi nelle mezzatinte.
2. **Gamma 2.2**: correzione per la visualizzazione corretta su monitor standard.

Il tone mapping è sempre attivo e non richiede configurazione.

### Depth of Field (Messa a Fuoco)

Il motore supporta la simulazione della **profondità di campo** tramite i parametri camera `aperture` e `focal_dist`, configurabili nel file YAML della scena:

| Campo YAML | Tipo | Default | Descrizione |
|------------|------|---------|-------------|
| `aperture` | float | `0.0` | Diametro dell'obiettivo. `0` = tutto a fuoco (pinhole). Valori tipici: `0.05`–`0.2`. |
| `focal_dist` | float | `1.0` | Distanza dal piano di messa a fuoco. Impostala pari alla distanza camera→soggetto. |

> **⚠️ Importante:** Appena `aperture > 0`, il piano di fuoco si trova a `focal_dist` unità dalla camera. Il default `1.0` è troppo corto per la maggior parte delle scene — misura la distanza camera→soggetto e usala come `focal_dist`.

---

## 4. Esempi di Rendering (Profili)

### Preview (Pochi Secondi)

Utile per verificare posizionamento camera, oggetti e luci senza attendere:

```powershell
dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/chess.yaml -w 400 -H 267 -s 1 -d 5 -S 4
```

### Draft (Minuti)

Valuta materiali, texture e illuminazione con qualità sufficiente:

```powershell
dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/chess.yaml -w 800 -H 533 -s 16 -d 20
```

### Produzione Full HD (Ore)

Qualità finale con anti-aliasing elevato e ombre morbide:

```powershell
dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/chess.yaml -o final.png -w 1920 -H 1080 -s 128 -d 50 -S 32
```

### Render di una Camera Specifica

Se la scena definisce più camere con `cameras:`, puoi renderizzare da una specifica:

```powershell
dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/chess.yaml -c top -o top.png -s 64
dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/chess.yaml -c 2 -o cam2.png -s 64
```

---

## 5. Gestione dell'Output

### Nome Automatico del File

Se ometti `-o`, il motore genera automaticamente il percorso di output dalla scena di input:

```
-i scenes/chess.yaml     → output/render-chess.png
-i scenes/cornell-box.yaml → output/render-cornell-box.png
```

### Formati Supportati

Il formato di output viene determinato dall'estensione del file specificato con `-o`:

| Estensione | Formato | Note |
|------------|---------|------|
| `.png` | PNG (lossless) | Qualità massima, file più grande. Default. |
| `.jpg` / `.jpeg` | JPEG | Compressione lossy, file più piccolo. |
| `.bmp` | Bitmap | Non compresso, sconsigliato. |

### Directory di Output

Se la directory di output non esiste, viene creata automaticamente. Esempio:

```powershell
dotnet run ... -- -i scenes/chess.yaml -o renders/2024/chess-final.png
```

---

## 6. Multi-Camera

Il motore supporta la definizione di più camere nominate nella stessa scena tramite la sintassi `cameras:` nel YAML. Questo permette di generare render da diverse angolazioni senza modificare il file di scena.

### Elencare le Camere Disponibili

```powershell
dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/chess.yaml --list-cameras
```

Output di esempio:
```
Cameras in scene (3):
  #0  "main"      fov=45°  pos=[0.00, 5.00, -8.00]
  #1  "top"       fov=35°  pos=[0.00, 12.00, 0.01]
  #2  "closeup"   fov=25°  pos=[1.50, 1.20, -4.00]
```

### Selezionare una Camera

Puoi selezionare la camera per **nome** (case-insensitive) o per **indice** (0-based):

```powershell
# Per nome
dotnet run ... -- -i scenes/chess.yaml -c top -o top.png

# Per indice
dotnet run ... -- -i scenes/chess.yaml -c 2 -o closeup.png
```

### Regole di Risoluzione

1. Se la scena usa `cameras:` (lista), il parametro `-c` seleziona per nome o indice.
2. Se la scena usa `camera:` (singola, sintassi legacy), il parametro `-c` viene ignorato con un warning.
3. Se `-c` non è specificato e la lista contiene più camere, viene usata la prima con un warning.
4. Se il nome o l'indice non corrispondono a nessuna camera, viene usata la prima con un warning.

### Sintassi YAML Multi-Camera

Per la sintassi YAML completa con `cameras:`, consulta la [sezione 3 del Tutorial Scene](02-tutorial-scene.md#3-sezione-camera).

---

## 7. Ottimizzazione e Performance

### Tabella dei Costi

| Parametro | Effetto sulla qualità | Effetto sul tempo |
|-----------|----------------------|-------------------|
| `-s` (samples) | Anti-aliasing, riduzione rumore | Lineare: `2× samples = 2× tempo` |
| `-d` (depth) | Riflessi multipli, vetro, caustics | Sublineare: i raggi si estinguono con Russian Roulette |
| `-S` (shadow samples) | Morbidezza delle ombre delle area light | Lineare per area light |
| `-w × -H` (risoluzione) | Dettaglio pixel | Lineare: `2× pixel = 2× tempo` |

### Strategia Iterativa Consigliata

1. **Preview** (`-w 400 -s 1 -d 5 -S 4`): verifica composizione.
2. **Draft** (`-w 800 -s 16 -d 20`): verifica materiali e luci.
3. **Final** (`-w 1920 -s 128 -d 50 -S 32`): render definitivo.

### Materiali e Costo

- **Lambertian**: il più veloce — diffuso puro, nessuna riflessione.
- **Metal**: veloce per `fuzz = 0` (un solo raggio riflesso), più lento con `fuzz` alto.
- **Dielectric**: il più costoso — ogni rimbalzo può generare sia riflessione che rifrazione, raddoppiando i percorsi. Aumenta `-d` per scene con molto vetro.
- **Disney BSDF**: più costoso di lambertian/metal ma più versatile. Con `spec_trans > 0` il costo è simile al dielectric. Per superfici di sfondo non protagoniste, `lambertian` o `metal` sono più veloci.

### BVH (Bounding Volume Hierarchy)

L'accelerazione BVH è automatica per scene con più di 4 oggetti e non richiede configurazione. I piani infiniti vengono esclusi dalla BVH e testati linearmente (non hanno AABB finita).

---

## 8. Risoluzione Problemi

### L'immagine è completamente nera
Possibili cause: la camera è dentro un oggetto, le luci hanno `intensity: 0`, o la scena non ha luci (e non ha `sky` né oggetti emissivi). Prova ad aggiungere una `point` light con alta intensità e verifica la posizione della camera.

### L'immagine ha zone sovraesposte (bianche)
Il tone mapping ACES gestisce automaticamente l'HDR, ma valori di `intensity` troppo alti sulle luci possono saturare la curva. Dimezza le intensità di tutte le luci mantenendo i rapporti tra loro, poi esegui un nuovo preview.

### Le ombre delle area light sono molto rumorose
Aumenta il numero di shadow samples via CLI con `-S 16` o `-S 32`, oppure aumenta i campioni di rendering (`-s`). Le ombre morbide richiedono più campioni per convergere. In alternativa, usa una point light con il risultato netto di ombre nette ma render più veloce.

### Le texture appaiono "piatte" o orientate male
Usa i parametri `rotation` e `offset` nella definizione della texture per orientare le venature. Consulta la [sezione 5.2 del Tutorial Scene](02-tutorial-scene.md#52-trasformazioni-spaziali-offset--rotation) per i dettagli tecnici.

### La scena carica ma non appare nulla / oggetti mancanti
Verifica che tutti i riferimenti `material` nelle entità corrispondano esattamente a un `id` definito nella sezione `materials` (case-sensitive). Un ID non trovato produce un materiale grigio di fallback (Lambertian 50% grigio), non un errore.

### Errore di caricamento YAML
Usa percorsi relativi corretti rispetto alla cartella in cui lanci il comando, oppure percorsi assoluti:
```powershell
--input C:\Users\Nome\Documents\scena.yaml
```
Il file YAML deve usare **spazi** per l'indentazione (niente TAB). Verifica la struttura con un linter YAML online in caso di dubbio.

### Gli oggetti emissivi illuminano poco o la scena è molto rumorosa
I materiali `emissive` illuminano la scena sia tramite rimbalzi indiretti del path tracer sia tramite Next Event Estimation (NEE) per le geometrie campionabili (Sphere, Quad, Triangle, Disk). Per ottenere risultati puliti:
1. Usa campioni alti: `-s 128` o superiore.
2. Aumenta la profondità a `-d 10` o più per permettere ai rimbalzi di propagarsi.
3. Se serve solo un fill minimo, aggiungi una `point` light con `intensity` molto bassa (0.2–1.0) per evitare ombre completamente nere.
4. L'emissione avviene solo dalla **front face**: verifica che la geometria emissiva sia orientata verso la scena (la normale deve puntare verso gli oggetti da illuminare).

### Il gradient sky non appare / il cielo è piatto
1. Verifica che la sezione `sky:` sia **dentro** `world:` (corretto indentamento YAML).
2. Verifica che `type: "gradient"` sia scritto correttamente (deve essere esattamente `gradient`).
3. Se `sky`: è assente, il motore usa il campo `background` come colore piatto. Per outdoor usa `sky`; per indoor usa `background` quando non è visibile il cielo.
4. Il sun disk non fornisce illuminazione diretta sugli oggetti (è solo visuale). Aggiungi una `directional` light con la stessa `direction` per avere ombre e highlight.

### Il sun disk nel cielo è troppo grande / piccolo / assente
1. Il parametro `size` è il diametro angolare in gradi. Il sole reale è ≈ 0.53°. Valori artistici tipici: 2–6°.
2. `intensity` controlla la luminosità del disco — valori troppo bassi lo rendono invisibile, troppo alti saturano il tone mapping.
3. `falloff` controlla l'alone: valori bassi (8–16) producono un alone ampio, alti (64–128) un punto netto.

### `--camera` non funziona / la camera specificata non viene trovata
1. Verifica che la scena usi la sintassi `cameras:` (lista) e non `camera:` (singola). Con la sintassi legacy, `-c` viene ignorato.
2. Usa `--list-cameras` per vedere le camere disponibili.
3. Il match per nome è case-insensitive: `-c Top` e `-c top` funzionano entrambi.
4. Se specifichi un indice fuori range, viene usata la camera 0 con un warning.
