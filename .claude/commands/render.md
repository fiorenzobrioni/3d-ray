---
description: "Renderizza una scena 3D-Ray con profilo qualità. Output di default: renders/00-test-<scene-stem>.png"
argument-hint: "<path/to/scene.yaml> [draft|standard|pre-final|final|ultra] [--camera <nome>]"
---

# Skill: Render

Lancia il motore 3D-Ray su una scena specifica con un preset di qualità. Il file di output va in `renders/00-test-<scene-stem>.png` salvo override esplicito.

## Input

Scena e profilo: $ARGUMENTS

Interpreta come `<path/to/scene.yaml> [profilo] [flag extra]`. Se manca il file, chiedi quale scena. Se manca il profilo, usa **draft** (veloce, per verifica rapida).

## Profili di qualità

| Profilo | Flag `-q` | Dimensione | spp | Depth | Denoiser auto | Tempo tipico |
|---------|-----------|-----------|-----|-------|--------------|--------------|
| **draft** (default) | `draft-small` | 960×540 | 16 | 4 | nfor-fast | secondi |
| **standard** | `standard` | 1920×1080 | 256 | 8 | nfor-fast | ~5–15 min |
| **pre-final** | `pre-final` | 1920×1080 | 256 | 10 | nfor | ~10–20 min |
| **final** | `final` | 1920×1080 | 1024 | 10 | nessuno | ore |
| **ultra** | `ultra` | 3840×2160 | 1024 | 10 | nessuno | ore |

Varianti dimensione disponibili per ogni livello (tranne `ultra`):
- `-tiny` = 480×270 (es. `draft-tiny`) — smoke test velocissimo
- `-small` = 960×540 (es. `standard-small`) — default per draft
- senza suffisso = risoluzione piena del livello

> `pre-final` è il modo corretto per un'anteprima fedele di `final`: stessa feature set
> (SSS, caustics, denoiser), un quarto dei campioni, 4–6× più veloce.

## Procedura

### 1. Normalizzazione input

- Verifica che `<path/to/scene.yaml>` esista. Se il path non inizia con `scenes/`, prova a cercare `scenes/<arg>.yaml` come fallback.
- Estrai lo **stem** del file (nome senza estensione, es. `chess.yaml` → `chess`, `showcases/disk-showcase.yaml` → `disk-showcase`).
- Mappa il profilo dell'utente al flag `-q` dalla tabella sopra (es. `draft` → `draft-small`). Usa `draft-small` di default.

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
  -q <preset>
```

Aggiungi i seguenti flag **solo se pertinenti** alla scena o richiesti dall'utente:

| Flag | Quando aggiungerlo |
|------|--------------------|
| `-c <nome>` | l'utente ha passato `--camera <nome>` |
| `-C <n>` | scena con dielectric annidato, media densi o volumetria pesante — firefly clamp 5–25 |
| `--denoiser nfor` | già attivo per `draft`/`standard`/`pre-final`; aggiungere solo per override su `final`/`ultra` |
| `--caustics on` | scene con superfici di vetro + luci puntuali (prismi, bicchieri, piscine) |
| `--sss-mode off` | scena senza materiali SSS, per saltare il dispatch |
| `--sss-quality high` | scena con SSS in render finale (override del default del preset) |
| `-v` | debug del caricamento scena o analisi delle luci |
| `-d <n>` | override depth: 12+ per dielectric stratificato, 16–20 per liquidi in bicchieri |

### 4. Esecuzione

Lancia il comando via Bash. Per `draft` il timeout di default (2 min) è sufficiente; per `standard`/`pre-final` considera di aumentarlo o avvisare l'utente che il comando richiederà più tempo; per `final`/`ultra` avvisare sempre.

Cattura l'output per estrarre:
- Righe `Loading scene...`, `Lights:`, `Sky:`, `Quality:`
- Tempo di render (`Render completed in X.XXs`)
- Path di output salvato

### 5. Report finale

Dopo l'esecuzione, riassumi:
- Scena caricata, sorgenti luci, sky mode
- Preset qualità usato, risoluzione, campioni, depth
- Denoiser attivo (se presente)
- Tempo di render
- Path del file PNG prodotto (assoluto o relativo)
- Eventuali warning dal loader (deferred messages)

Se il render fallisce, mostra l'errore dal motore e suggerisci:
- `/scene-review <scena>` se sembra un problema di YAML
- Profilo più basso o `-d` ridotto se è un timeout

## Esempi

```
/render scenes/chess.yaml
  → draft-small, output: renders/00-test-chess.png

/render scenes/chess.yaml standard
  → standard (1920×1080, 256 spp, nfor), output: renders/00-test-chess.png

/render scenes/pendolo-newton.yaml final --camera macro
  → final con camera "macro", output: renders/00-test-pendolo-newton.png

/render scenes/cristallo.yaml draft -C 10
  → draft-small con firefly clamp basso per dielectric stratificato

/render scenes/cornell-box.yaml pre-final --caustics on
  → pre-final con fotoni caustic per il vetro nel Cornell box
```

## Note

- Il comando richiede .NET 10 SDK e la prima esecuzione compila il progetto (più lenta). Le successive sono istantanee al primo byte.
- Il prefisso `00-test-` è deliberato: usa un nome diverso (`-o <altro-nome>.png`) quando vuoi archiviare il risultato come render definitivo.
- Flag espliciti (`-s`, `-d`, `-w`, `-H`) sovrascrivono sempre i valori del preset.
