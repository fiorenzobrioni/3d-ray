# Tutorial 01: Guida Rapida e Utilizzo

Benvenuto nel motore RayTracer! Questa guida ti aiuterà a configurare l'ambiente, compilare il progetto e lanciare il tuo primo render in pochi minuti.

---

## 1. Prerequisiti
Per eseguire il renderer sono necessari:
- **.NET 10 SDK** (o superiore).
- Una CPU multi-core (il motore scala linearmente con il numero di core).

## 2. Compilazione
Apri un terminale nella cartella del progetto ed esegui:
```powershell
dotnet build src/RayTracer/RayTracer.csproj -c Release
```

## 3. Il Tuo Primo Render
Il modo più semplice per iniziare è renderizzare una delle scene di esempio incluse:

```powershell
# Esegui un render veloce (draft) della scena dei pendoli
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- -i scenes/pendolo-newton.yaml -s 16 -w 800 -H 450
```

L'immagine verrà salvata automaticamente nella cartella `output/`.

---

## 4. Guida ai Parametri CLI
Puoi personalizzare ogni render tramite la riga di comando:

| Parametro | Alias | Default | Descrizione |
|-----------|-------|---------|-------------|
| `--input` | `-i` | — | **Obbligatorio.** Percorso del file YAML della scena. |
| `--output`| `-o` | `output/` | Percorso del file immagine (PNG, JPG o BMP). |
| `--width` | `-w` | `1200` | Larghezza in pixel. |
| `--height`| `-H` | `800` | Altezza in pixel. |
| `--samples`| `-s` | `16` | Campioni per pixel (qualità/rumore). |
| `--depth` | `-d` | `50` | Massimo numero di rimbalzi della luce. |
| `--shadow-samples`| `-S` | *(YAML)* | Qualità delle ombre per le Area Light. |
| `--camera`| `-c` | `0` | Seleziona una camera specifica per nome o indice. |

> [!TIP]
> Il numero di campioni (`-s`) viene sempre arrotondato al quadrato perfetto superiore (es. `-s 20` diventa `25`).

---

## 5. Strategia di Rendering (Workflow)
Non lanciare subito render ad altissima qualità! Usa questo approccio iterativo:

1. **Preview (Secondi)**: `-w 400 -s 1 -d 5` -> Verifica inquadratura e luci.
2. **Draft (Minuti)**: `-w 800 -s 16 -d 20` -> Verifica materiali e colori.
3. **Finale (Ore)**: `-w 1920 -s 256 -d 50` -> Render definitivo senza rumore.

---

## 6. Risoluzione Problemi comuni
- **Immagine Nera**: Verifica che ci siano luci nella scena o che la camera non sia "dentro" un oggetto.
- **Troppo Rumore**: Aumenta i campioni (`-s`) o i campioni ombra (`-S`).
- **Lentezza Eccessiva**: Riduci la risoluzione o i campioni durante i test. Assicurati di compilare in modalità `-c Release`.

---

[Vai al Tutorial 02: Costruire una Scena](./02-costruire-una-scena.md) | [Torna al README](../../README.md)
