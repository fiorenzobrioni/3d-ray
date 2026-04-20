---
description: "Renderizza una scena 3D-Ray con profilo Preview/Standard/Final. Output di default: renders/00-test-<scene-stem>.png"
argument-hint: "<path/to/scene.yaml> [preview|standard|final] [--camera <nome>]"
---

# Skill: Render

Lancia il motore 3D-Ray su una scena specifica con un profilo di qualità. Il file di output va in `renders/00-test-<scene-stem>.png` salvo override esplicito.

## Input

Scena e profilo: $ARGUMENTS

Interpreta come `<path/to/scene.yaml> [profilo] [flag extra]`. Se manca il file, chiedi quale scena. Se manca il profilo, usa **preview** (veloce, per verifica rapida).

## Profili

| Profilo | `-w` | `-H` | `-s` | `-d` | `-S` | Tempo tipico |
|---------|-----:|-----:|-----:|-----:|-----:|--------------|
| **preview** (default) | 400 | 225 | 64 | 4 | 1 | secondi |
| **standard** | 800 | 450 | 256 | 6 | 1 | minuti |
| **final** | 1920 | 1080 | 1024 | 8 | 4 | decine di minuti |

Per scene con dielectric annidato o medium denso, alza `-d` a 12+ anche in preview; avvisa l'utente se la scena richiede profili custom (lo si capisce dall'header dei file scena o da `/scene-review`).

## Procedura

### 1. Normalizzazione input

- Verifica che `<path/to/scene.yaml>` esista. Se il path non inizia con `scenes/`, prova a cercare `scenes/<arg>.yaml` come fallback.
- Estrai lo **stem** del file (nome senza estensione, es. `chess.yaml` → `chess`, `showcases/disk-showcase.yaml` → `disk-showcase`).
- Determina il profilo dall'argomento o usa `preview` di default.

### 2. Output path

Default: `renders/00-test-<stem>.png`

Il prefisso `00-test-` tiene i render di test in cima alla directory `renders/` per una ricerca visiva rapida. L'utente può comunque sovrascrivere con `-o <path>` nei flag extra.

Se `renders/` non esiste, crealo prima di lanciare il render (il motore lo farebbe comunque, ma è bene verificare).

### 3. Costruzione del comando

Template base (dalla root del progetto):

```bash
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- \
  -i <scene> \
  -o renders/00-test-<stem>.png \
  -w <W> -H <H> -s <S> -d <D> -S <SS>
```

Aggiungi:
- `-c <nome>` se l'utente ha passato `--camera <nome>` nei flag extra
- `-C <clamp>` se la scena ha volumetria/dielectric pesante (firefly clamp 25–50)
- Altri flag passati esplicitamente dall'utente

### 4. Esecuzione

Lancia il comando via Bash. Rispetta il timeout Bash di default (2 min) per i profili preview; per standard/final considera di aumentarlo o avvisare l'utente che il comando potrebbe richiedere più tempo.

Cattura l'output per estrarre:
- Righe `Loading scene...`, `Lights:`, `Sky:`
- Tempo di render (`Render completed in X.XXs`)
- Path di output salvato

### 5. Report finale

Dopo l'esecuzione, riassumi:
- Scena caricata, sorgenti luci, sky mode
- Risoluzione, campioni, depth usati
- Tempo di render
- Path del file PNG prodotto (assoluto o relativo)
- Eventuali warning dal loader (deferred messages)

Se il render fallisce, mostra l'errore dal motore e suggerisci:
- `/scene-review <scena>` se sembra un problema di YAML
- Profilo più basso se è un timeout

## Esempi

```
/render scenes/chess.yaml
  → preview, output: renders/00-test-chess.png

/render scenes/alchemist-lab.yaml standard
  → standard, output: renders/00-test-alchemist-lab.png

/render scenes/pendolo-newton.yaml final --camera macro
  → final con camera "macro", output: renders/00-test-pendolo-newton.png

/render scenes/cristallo.yaml preview -C 25
  → preview con firefly clamp basso per dielectric stratificato
```

## Note

- Il comando richiede .NET 10 SDK e la prima esecuzione compila il progetto (più lenta). Le successive sono istantanee al primo byte.
- Il prefisso `00-test-` è deliberato: usa un nome diverso (`-o <altro-nome>.png`) quando vuoi archiviare il risultato come render definitivo.
