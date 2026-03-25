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
| `--output` | `-o` | `render.png` | Nome e percorso del file immagine da generare. Il formato viene rilevato automaticamente dall'estensione (`.png`, `.jpg`, `.bmp`). |
| `--width` | `-w` | `1200` | Larghezza dell'immagine in pixel. |
| `--height` | `-H` | `800` | Altezza dell'immagine in pixel. |
| `--samples` | `-s` | `16` | Campioni per pixel (anti-aliasing e riduzione del rumore). Vedi nota sotto. |
| `--depth` | `-d` | `50` | Massimo numero di rimbalzi ricorsivi per ogni raggio (riflessi, rifrazioni, scattering). |
| `--shadow-samples` | `-S` | *(da YAML)* | Override globale dei shadow samples per tutte le area light. Vedi nota sotto. |
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

> **Esempio:** camera in `[0, 2, -8]`, soggetto in `[0, 1, 0]` → distanza ≈ 8.1 → `focal_dist: 8.1`, `aperture: 0.08`.
>
> **Nota:** Con `aperture > 0` e `focal_dist: 1.0` (default), il piano di fuoco è a 1 unità dalla camera: il risultato è un bokeh estremo non intenzionale. **Misura sempre la distanza camera→soggetto** prima di abilitare il DOF.

Vedi la [sezione Camera del Tutorial Scene](02-tutorial-scene.md#3-sezione-camera) per la sintassi YAML completa.

---

## 4. Esempi di Rendering (Profili)

### 4.1 — Profilo "FAST PREVIEW" (Bozza Immediata)
Ideale per testare la posizione della camera o delle luci.
```powershell
# Windows
dotnet run --project src\RayTracer\RayTracer.csproj -- -i scenes\chess.yaml -s 1 -d 5 -w 400 -H 267 -S 4
# Linux / macOS
dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/chess.yaml -s 1 -d 5 -w 400 -H 267 -S 4
```
- **Qualità**: Molto rumorosa (1 campione = niente anti-aliasing). Errori di jitter visibili.
- **Tempo**: < 1 secondo.

### 4.2 — Profilo "DRAFT" (Qualità Media)
Consigliato per valutare texture procedurali e materiali metallici.
```powershell
dotnet run --project src\RayTracer\RayTracer.csproj -- -i scenes\chess.yaml -s 16 -d 20 -w 800 -H 533
```
- **Qualità**: Buona pulizia globale, rumore residuo nelle ombre e nei materiali dielettrici.
- **Tempo**: 5 — 30 secondi (dipende dalla complessità della scena).

### 4.3 — Profilo "PRODUCTION" (Alta Qualità)
Da utilizzare per il risultato finale o per scene con molto vetro e area light.
```powershell
dotnet run --project src\RayTracer\RayTracer.csproj -- -i scenes\chess.yaml -s 128 -d 50 -w 1920 -H 1080 -S 16
```
- **Qualità**: Immagine pulita, ombre morbide degli area light ben definite.
- **Tempo**: Minuti (dipende dalla CPU e dalla complessità della scena).

### 4.4 — Profilo "ULTRA" (Qualità Massima)
Per render finali con vetro, depth of field e area light ad alta qualità.
```powershell
dotnet run --project src\RayTracer\RayTracer.csproj -- -i scenes\chess.yaml -s 256 -d 50 -w 3840 -H 2160 -S 32
```
- **Qualità**: Qualità fotorealistica, zero rumore visibile.
- **Tempo**: Da decine di minuti a ore.

---

## 5. Gestione dell'Output

Il motore determina il formato dell'immagine salvata in base all'estensione del file specificato in `--output`:

| Formato | Estensione | Note |
|---------|------------|------|
| **PNG** | `.png` | Consigliato. Formato lossless (senza perdita di qualità). Default se l'estensione non è riconosciuta. |
| **JPEG** | `.jpg` / `.jpeg` | Compresso con perdita. File più leggeri ma possibile degradazione dei dettagli fini (rumore da compressione sui gradienti). |
| **BMP** | `.bmp` | Formato non compresso. File molto grandi — usare solo se necessario per compatibilità. |

---

## 6. Ottimizzazione e Performance

### Strategia Iterativa Consigliata

1. **Preview** (`-s 1 -w 400 -S 4`): Verifica inquadratura e posizionamento oggetti.
2. **Draft** (`-s 16 -w 800`): Valuta materiali, texture e bilanciamento luci.
3. **Production** (`-s 128 -w 1920 -S 16`): Render finale Full HD.
4. **Ultra** (`-s 256 -w 3840 -S 32`): 4K con qualità massima.

### Multi-Threading
Il motore parallelizza il rendering per scanline usando `Parallel.For` con `MaxDegreeOfParallelism = Environment.ProcessorCount`. Tutti i core logici della CPU vengono utilizzati automaticamente senza configurazione.

---

## 7. Risoluzione Problemi

### L'immagine è completamente nera
1. Verifica che la camera non si trovi all'interno di un oggetto solido.
2. Controlla che siano presenti luci (`lights`) con `intensity` > 0.
3. Verifica che `background` non sia `[0, 0, 0]` — il background agisce come sorgente di luce globale per i raggi rimbalzati.
4. Se vuoi una scena buia illuminata solo da luci esplicite, imposta `background: [0, 0, 0]` e `ambient_light: [0, 0, 0]`, poi aggiungi luci `point` o `spot` con intensità sufficienti. I range tipici variano con la distanza: consulta la [tabella di calibrazione nel Tutorial Scene](02-tutorial-scene.md#75--calibrazione-dellintensità) per i valori di riferimento.

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
I materiali `emissive` illuminano la scena solo tramite rimbalzi indiretti del path tracer (non usano Next Event Estimation come le luci esplicite). Per ottenere risultati puliti:
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
2. Il parametro `falloff` controlla l'alone: valori bassi (8–16) = glow ampio e morbido, valori alti (64–128) = bordo netto.
3. Se non vedi il sole, verifica che la `direction` punti nella direzione corretta e che la camera guardi verso quella parte del cielo.

### L'HDRI non si carica o il cielo è magenta
1. Verifica che il file `.hdr` esista nel percorso indicato (relativo alla directory del file YAML).
2. Solo il formato **Radiance HDR** (`.hdr`) è supportato. File `.exr` o altri formati HDR non sono gestiti.
3. Controlla il log in console: il motore stampa dimensioni e tempo di caricamento se il file è valido.
4. Se il cielo appare magenta, il file non è stato trovato o è corrotto.

### Le image texture non appaiono (magenta al loro posto)
1. Verifica che il percorso in `path:` sia corretto e relativo alla cartella del file YAML.
2. Formati supportati: PNG, JPEG, BMP, GIF, TIFF, WebP. Assicurati che l'estensione corrisponda.
3. Controlla i warning in console — il motore stampa il percorso completo che ha tentato di caricare.

### I box con `min`/`max` appaiono spostati o hanno dimensioni errate
Esistono due sintassi per definire un box, entrambe valide ma mutuamente esclusive:

**Metodo 1 — `scale`/`translate` sul cubo unitario (raccomandato):**
```yaml
- name: "cubo"
  type: "box"
  scale: [2, 1, 2]
  translate: [0, 0.5, 0]
  material: "rosso"
```

**Metodo 2 — `min`/`max` (coordinate assolute degli angoli):**
```yaml
- name: "cubo"
  type: "box"
  min: [-1, 0, -1]
  max: [1, 1, 1]
  material: "rosso"
```

> **Attenzione:** Non mescolare i due metodi sullo stesso oggetto. Se specifichi sia `min`/`max` che `scale`/`translate`, i primi definiscono la forma e i secondi vengono applicati come trasformazione aggiuntiva (utile per aggiungere `rotate`).
