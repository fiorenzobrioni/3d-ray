# 3D-Ray: High-Performance C# .NET 10 RayTracer Engine

![C#](https://img.shields.io/badge/language-C%23-239120?logo=c-sharp&logoColor=white) ![.NET 10](https://img.shields.io/badge/framework-.NET%2010-512BD4?logo=dotnet&logoColor=white) ![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-0078D4) ![License](https://img.shields.io/badge/license-MIT-blue) [![CI](https://github.com/fiorenzobrioni/3d-ray/actions/workflows/dotnet.yml/badge.svg)](https://github.com/fiorenzobrioni/3d-ray/actions/workflows/dotnet.yml)

Un moderno motore di ray tracing ad alte prestazioni sviluppato in C# e .NET 10, con configurazione di scene tramite YAML e capacità di rendering avanzate basate su fisica (PBR).

> **English Description:** *A modern, parallelized ray-tracing engine built with C# and .NET 10, featuring YAML scene configuration and advanced physically-based rendering capabilities.*

![Sphere Showcase](renders/sphere-showcase.png)

---

## 🔍 Panoramica (Overview)

3D-Ray trasforma una descrizione YAML in un'immagine fotorealistica, senza dover scrivere codice. È pensato per chi vuole comporre scene ricche — interni, still life, paesaggi atmosferici, composizioni artistiche — sfruttando scene graph gerarchico con gruppi, trasformazioni e template, librerie riutilizzabili di materiali e oggetti, un BSDF Disney unificato che copre dal metallo spazzolato alle bolle di sapone, effetti volumetrici (nebbia, fumo, nubi) e illuminazione basata su HDRI.

Il motore è progettato per il calcolo parallelo multi-core, con BVH automatica, Next Event Estimation e campionamento Sobol per convergere in fretta, e chiude con un tone mapping ACES filmic per un look cinematografico.

Per la roadmap dettagliata, le feature in corso e quelle pianificate consulta il [**DEVLOG**](./DEVLOG.md).

---

## ✨ Caratteristiche Principali (Key Features)

### Rendering
- 🚀 **Rendering Parallelo** — sfrutta tutti i core logici della CPU per una scalabilità lineare delle prestazioni.
- 🔁 **Path Tracing** con rimbalzi multipli configurabili: riflessi, rifrazioni, occlusione ambientale e color bleeding emergono naturalmente dalla simulazione fisica.
- 📷 **Camera con Depth of Field** — apertura e distanza di messa a fuoco configurabili per effetti bokeh fotorealistici.
- 🎬 **Multi-Camera** — più camere definite nella stessa scena, selezionabili da CLI per nome o indice per generare più inquadrature dallo stesso file YAML.
- 🎯 **Next Event Estimation con MIS** — campionamento diretto delle luci con Multiple Importance Sampling completo: tutti i materiali (Lambertian, Metal, Mix, Disney) e la phase function dei volumetrici partecipano. Balance heuristic di default, power heuristic opzionale via `--mis power`.
- 🧮 **Campionamento Stratificato** — riduce il rumore a parità di campioni totali.
- 🔢 **Sobol + Owen Scrambling** — sequenza quasi-Monte Carlo a bassa discrepanza che converge più in fretta del PRNG classico su pixel jitter, lens sampling e primi bounce.
- 🎲 **Russian Roulette** adattiva — terminazione stocastica dei raggi calibrata sull'illuminazione della scena per efficienza ottimale.
- 🎞️ **Tone Mapping ACES Filmic** — post-processing cinematografico con highlight naturali e colori ricchi.
- 🖼️ **Output multi-formato** — PNG, JPEG e BMP con rilevamento automatico dall'estensione del file.

### Accelerazione
- 📦 **BVH (Bounding Volume Hierarchy)** — struttura di accelerazione spaziale con **Surface Area Heuristic (SAH) a binning**, fat leaves e *ordered traversal*. Build parallelizzato per scene grandi. Intersezioni raggio-oggetto in tempo **O(log N)**, attivazione automatica in base alla complessità della scena.

### Geometrie
- ⚪ **Sphere** — sfera analitica
- 📦 **Box** — parallelepipedo allineato agli assi
- 🔩 **Cylinder** — cilindro finito con caps
- 🍦 **Cone** — cono finito o tronco di cono con caps
- 💊 **Capsule** — cilindro con estremità emisferiche
- 🍩 **Torus** — toro con intersezione analitica esatta
- ⭕ **Annulus** — disco con foro circolare (rondella)
- ⏺ **Disk** — disco piatto
- ▰ **Quad** — quadrilatero parametrico
- 🔺 **Triangle / SmoothTriangle** — triangolo con shading flat o interpolato per-vertex (Phong)
- ▬ **Infinite Plane** — piano infinito per pavimenti e sfondi
- 🏠 **Mesh (OBJ)** — modelli 3D da file Wavefront OBJ con smooth shading, UV mapping dell'artista e BVH interno dedicato
- 🔷 **CSG (Constructive Solid Geometry)** — operazioni booleane su solidi: **Union** (A ∪ B), **Intersection** (A ∩ B) e **Subtraction** (A \ B), annidabili ricorsivamente per forme arbitrariamente complesse
- 🏺 **Lathe (Superficie di Rivoluzione)** — profilo 2D fatto ruotare attorno all'asse Y per ottenere vasi, calici, colonne e lampade senza tassellatura. Tre modalità di profilo: **linear** (segmenti con spigoli netti, look tornito), **Catmull-Rom** (curva liscia che passa per ogni punto) e **Bezier cubico** (control point manuali).

### Struttura della Scena
- 🌳 **Scene Graph (Gruppi)** — Composizione gerarchica di oggetti con trasformazioni ereditate. Gruppi annidabili con primitive, CSG, mesh e altri gruppi.
- 🏭 **Template / Istanze** — Definisci oggetti composti una volta come template, istanzia N volte con trasformazioni e materiali indipendenti. Librerie di oggetti importabili da file YAML separati.
- 📦 **Import YAML** — Scomposizione di scene complesse in file separati. Librerie riutilizzabili di materiali, template, oggetti e luci con import annidati e protezione ciclica.
- 🎁 **Starter Kit** — 18 scene complete pronte all'uso (dining room, photography studio, wine cellar, underwater, zen garden…) da personalizzare come punto di partenza. Vedi [`scenes/libraries/starter-kits/`](./scenes/libraries/starter-kits/).

### Materiali
- 🎨 **Lambertian** — diffuso opaco
- 🪞 **Metal** — riflesso speculare con rugosità (`fuzz`) configurabile
- 💎 **Dielectric** — vetro e trasparenti con rifrazione e riflesso Fresnel
- 💡 **Emissive** — materiale auto-luminoso; gli oggetti emissivi partecipano automaticamente alla NEE come sorgenti di luce geometriche
- 🌟 **Disney Principled BSDF** — materiale PBR unificato (`"disney"` / `"pbr"`): un singolo tipo copre plastica, metallo, vetro, vernice auto, tessuto, pelle, bolle di sapone e qualsiasi combinazione. Oltre ai parametri classici (`metallic`, `roughness`, `specular`, `sheen`, `clearcoat`, `spec_trans`, `ior`) supporta:
  - **Anisotropia** per highlight allungati stile metallo spazzolato, capelli e vinile.
  - **Multi-scattering energy compensation** per metalli rugosi convincenti (oro e rame anche a roughness alta).
  - **Beer-Lambert per il vetro** con assorbimento dipendente dallo spessore: liquori, bottiglie colorate, acque profonde.
  - **Diffuse transmission & thin-walled** per fogli, foglie, tendaggi e paralumi.
  - **Subsurface shaping** con tinte sotto-pelle dedicate per pelle, cera e marmo.
  - **Clearcoat avanzato** con IOR e normal map proprie per carrozzerie, lacche e vinile protetto.
  - **Charlie sheen** per microfibre realistiche (velluto, pesca, muschio).
  - **Thin-film iridescence** per bolle di sapone, opal e rivestimenti dicroici.

  Dettagli matematici e riferimenti bibliografici in [`docs/technical/shading-model.md`](./docs/technical/shading-model.md).
- 🔀 **Mix Material** — blending tra due materiali qualsiasi con peso costante o texture mask spaziale (noise, marble, image…). Per effetti di ruggine, usura, transizioni graduali, decal e composizioni ricorsive (mix-of-mix)

### Texture
- ♟ **Checker** — scacchiera 3D procedurale
- 🌀 **Noise** — rumore Perlin (liscio o turbolento)
- 🏔 **Marble** — marmo procedurale
- 🪵 **Wood** — legno procedurale
- 🖼 **Image Texture** — texture da file (PNG, JPEG, BMP, GIF, TIFF, WebP) con bilinear filtering e tiling configurabile
- 🗺 **Normal Map** — dettaglio geometrico superficiale senza triangoli aggiuntivi; compatibile OpenGL e DirectX-style (`flip_y`)

Tutte le texture procedurali supportano **offset**, **rotation** e **randomizzazione per-oggetto** tramite seed deterministico.

### Sistema di Trasformazione
- 🔄 **Transform** — scala, rotazione e traslazione applicabili a qualsiasi primitiva, inclusi i nodi CSG.

### Sistema di Illuminazione
- 💡 **Point Light** — luce puntiforme con attenuazione quadratica
- ☀️ **Directional Light** — luce parallela (sole), senza attenuazione
- 🔦 **Spot Light** — faretto con cono interno/esterno e falloff liscio
- 🟧 **Area Light** — emettitore rettangolare con soft shadows fisicamente corretti via campionamento Monte Carlo
- 🟡 **Sphere Light** — Luce sferica con solid-angle sampling: penumbra circolare uniforme, zero campioni sprecati, efficienza 2–10× superiore alla sfera emissiva equivalente per sfere piccole/distanti. Ideale per lampadine, lanterne e globi luminosi.
- ✨ **Emissive Objects** — qualsiasi geometria con materiale `emissive` diventa sorgente di luce visibile con illuminazione indiretta naturale
- 🌐 **Environment Light** — gradient sky e HDRI partecipano alla NEE come sorgenti di luce direzionali campionabili

### Ambiente
- 🌅 **Gradient Sky** — cielo procedurale con gradiente verticale a 3 bande (zenith, orizzonte, terreno) e sun disk con glow halo. Configurabile via YAML con preset per mezzogiorno, golden hour, tramonto e notte.
- 🌍 **IBL / HDRI** — Image-Based Lighting da file Radiance `.hdr`: illuminazione globale fotorealistica catturata da fotografie HDR a 360° con importance sampling per convergenza rapida. Compatibile con [Poly Haven](https://polyhaven.com/hdris) (CC0).

### Volumetria (Participating Media)
- 🌫️ **Homogeneous Medium** — mezzo partecipante uniforme globale per nebbia densa, foschia e effetti subacquei. Beer-Lambert analitico, economico, adatto come base di partenza.
- 🏔️ **Height Fog** — foschia atmosferica con densità che cala esponenzialmente con la quota (`scale_height`, `y0`). Modello "aerial perspective" per scene outdoor: montagne, strade all'alba, vedute urbane.
- 🌀 **Procedural Medium (Perlin fBm)** — nebbia eterogenea generata da rumore Perlin multi-ottava con delta tracking e ratio tracking. Sacche di densità irregolari, god-ray non omogenei, atmosfere da film horror o nubi sparse.
- 🧊 **Grid Medium** — densità campionata su griglia 3D regolare (inline YAML o file binario `.vol`) confinata in una AABB world-space, con filtro di ricostruzione selezionabile: **trilineare** (default, veloce) o **tricubico** Catmull-Rom (più liscio) per rimuovere i kink visibili sulle griglie a bassa risoluzione. Ideale per fumo localizzato, esplosioni, nuvole isolate.
- 🎇 **Cinque phase function** — `isotropic` (scattering uniforme), `hg` (Henyey-Greenstein, asimmetria direzionale), `rayleigh` (scattering atmosferico), `double_hg` (due lobi misti per nubi realistiche stile Nubis) e `schlick` (approssimazione fast-HG). Ogni mezzo combinabile con qualsiasi phase function.

---

## 🚀 Quick Start

### Prerequisiti
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

> I comandi qui sotto sono `dotnet` standard: funzionano identici su bash, zsh e PowerShell.

### Compilazione
```bash
cd 3d-ray
dotnet build src/RayTracer/RayTracer.csproj -c Release
```

### Esecuzione

Render di prova (profilo Standard):
```bash
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- -i scenes/pendolo-newton.yaml -s 256 -d 6 -o renders/render-draft.png -w 480 -H 270
```

Render finale Full HD (profilo Final):
```bash
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- -i scenes/pendolo-newton.yaml -s 1024 -d 8 -S 4 -o renders/render-final.png -w 1920 -H 1080
```

> Per i profili completi (Preview / Standard / Final) e i tip su `-d`, `-s`, `-S`, `-C` consulta la guida [Profili di Rendering](./docs/reference/profili-di-rendering.md) ([English version](./docs/reference/rendering-profiles.md)).

---

## 📁 Struttura del Progetto

```
3d-ray/
├── docs/                    # Documentazione del progetto
│   ├── reference/           # Riferimento YAML completo (EN/IT)
│   ├── technical/           # Approfondimenti tecnici interni
│   └── tutorial/            # Tutorial in 11 capitoli (EN/IT)
│       ├── en/              # Tutorial in English
│       └── it/              # Tutorial in italiano
├── src/
│   ├── RayTracer/              # Motore principale
│   │   ├── Acceleration/       # BVH
│   │   ├── Camera/             # Camera con DOF
│   │   ├── Core/               # Ray, HitRecord, MathUtils, sampling
│   │   ├── Geometry/           # Primitive (Sphere, Box, Cylinder, CsgObject, Group...)
│   │   ├── Lights/             # Point, Directional, Spot, Area, Sphere, GeometryLight, EnvironmentLight
│   │   ├── Materials/          # Lambertian, Metal, Dielectric, Emissive, Disney BSDF, MixMaterial
│   │   ├── Rendering/          # Renderer, SkySettings, EnvironmentMap
│   │   ├── Scene/              # SceneLoader, SceneData
│   │   ├── Textures/           # Checker, Noise, Marble, Wood, Image, NormalMap
│   │   └── Volumetrics/        # Homogeneous, HeightFog, Procedural, GridMedium e phase function
│   ├── RayTracer.Tests/        # Suite xUnit (equivalenza BVH, AABB, ...)
│   ├── RayTracer.Benchmarks/   # Harness BenchmarkDotNet
│   └── Tools/
│       ├── TextureGen/         # Generatore texture procedurali (PNG)
│       ├── NormalMapGen/       # Generatore flat normal map per test
│       └── ChessGen/           # Generatore scacchiera con set Staunton
├── scenes/                     # File YAML di scene
│   ├── libraries/              # Risorse riutilizzabili via import YAML
│   │   ├── materials/          # Materiali PBR (Disney/Classic)
│   │   ├── objects/            # Template di oggetti composti
│   │   ├── lights/             # Setup di illuminazione pronti all'uso
│   │   ├── starter-kits/       # Scene complete pronte all'uso, da personalizzare
│   │   └── textures/           # Texture PNG (albedo e normal map)
│   ├── showcases/              # Scene dimostrative per singola feature
│   └── *.yaml                  # Scene principali del progetto
├── renders/                    # Immagini renderizzate
└── .github/workflows/          # CI con smoke test
```

---

## 🛠️ Tool Inclusi

### TextureGen
Genera texture procedurali pronte all'uso (mattoni, legno, marmo, griglia UV):
```bash
dotnet run --project src/Tools/TextureGen/TextureGen.csproj
```

### NormalMapGen
Genera una normal map piatta per testare il sistema di normal mapping:
```bash
dotnet run --project src/Tools/NormalMapGen/NormalMapGen.csproj
```

### ChessGen
Genera il file YAML di una scacchiera Staunton completa (board 8×8 +
32 pezzi posizionati con trasformazioni). Usato per produrre
`scenes/chess.yaml`:
```bash
dotnet run --project src/Tools/ChessGen/ChessGen.csproj
```

---

## 📖 Guida all'Uso e CLI

### Parametri CLI

| Parametro | Alias | Default | Descrizione |
|-----------|-------|---------|-------------|
| `--input` | `-i` | — (**obbligatorio**) | Percorso del file YAML della scena. |
| `--output` | `-o` | `renders/render-<scena>.png` | File di output. Se omesso, generato dal nome della scena. |
| `--width` | `-w` | `1200` | Larghezza in pixel. |
| `--height` | `-H` | `800` | Altezza in pixel. |
| `--samples` | `-s` | `16` | Campioni per pixel. Con il sampler Sobol (default) viene usato il conteggio esatto; con `--sampler prng` viene arrotondato al quadrato perfetto superiore (`√N × √N`). |
| `--depth` | `-d` | `8` | Massimo numero di rimbalzi ricorsivi per raggio. Alza a `16+` solo per dielettrici impilati (vetri annidati, liquidi nei bicchieri). |
| `--shadow-samples` | `-S` | *(da YAML)* | Override globale dei shadow samples per tutte le area light. Usa quadrati perfetti (`1, 4, 9, 16`). |
| `--clamp` | `-C` | `100` | Firefly clamp: massima radianza per-campione prima del tone mapping. Abbassa (es. `25`) per scene problematiche con vetri/nebbia, alza per highlight molto intensi. |
| `--indirect-clamp-factor` | — | `1.0` | Fattore di clamp per i bounce indiretti (depth ≥ 1). `1.0` = disabilitato (default). `0.25` → clamp indiretto = 25 se `-C 100`. Stile Cycles/Arnold. |
| `--camera` | `-c` | *(prima camera)* | Seleziona la camera per nome o indice (0-based). |
| `--sampler` | — | `sobol` | Campionatore per-pixel: `sobol` (Owen-scrambled, default) o `prng` (legacy thread-local). Nessuna differenza di interfaccia scena: cambia solo la sequenza dei numeri casuali. |
| `--mis` | — | `balance` | Heuristica MIS che combina Light Sampling (NEE) e BSDF/Phase Sampling: `balance` (Veach 1997 §9.2.2) o `power` (β=2, §9.2.4). Stesso costo computazionale; `power` riduce ulteriormente la varianza quando le PDF disagree (luce piccola + materiale ruvido, sole nella nebbia). |
| `--light-sampling` | — | `all` | Strategia NEE: `all` = somma tutte le luci (default, backward compat); `power` = campiona una luce ∝ `ApproximatePower` (varianza minore in scene multi-luce); `uniform` = campionamento uniforme (debug). |
| `--list-cameras` | — | — | Elenca le camere disponibili nella scena ed esce. |
| `--verbose` | `-v` | — | Mostra informazioni dettagliate durante il caricamento e l'analisi della scena (import, template, σ del medium, tuning Russian Roulette). Utile per debug e sviluppo scene. |
| `--help` | `-h` | — | Mostra il messaggio di aiuto ed esce. |

> **Nota:** `-H` è maiuscola perché `-h` è riservato a `--help`. Le maiuscole sono usate per gli "override avanzati": `-S` (`--shadow-samples`) e `-C` (`--clamp`); `-s` minuscola per `--samples`, `-c` minuscola per `--camera`.

> **Profili di rendering pronti all'uso:** vedi [Profili di Rendering](./docs/reference/profili-di-rendering.md) · [Rendering Profiles (EN)](./docs/reference/rendering-profiles.md).

---

## 💡 Esempi Pratici

### Profilo Preview (composizione, camere, materiali — secondi)
```bash
dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/chess.yaml -o preview.png -w 400 -H 267 -s 64 -d 4 -S 1
```

### Profilo Standard (CI/CD, review, log — minuti)
```bash
dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/chess.yaml -o draft.png -w 800 -H 533 -s 256 -d 6
```

### Profilo Final (portfolio, copertina README — Full HD)
```bash
dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/chess.yaml -o final.png -w 1920 -H 1080 -s 1024 -d 8 -S 4
```

### Output in JPEG
Il formato viene rilevato automaticamente dall'estensione:
```bash
dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/chess.yaml -o render.jpg -s 32
```

### Multi-Camera
```bash
dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/chess.yaml --list-cameras
dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/chess.yaml -c top -o top.png
dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/chess.yaml -c 2 -o cam2.png
```

---

## 📖 Documentazione e Guide (Documentation)

### 📚 Tutorial

Guida completa in 11 capitoli: dalla teoria del ray tracing alla creazione di scene di produzione con materiali PBR, illuminazione avanzata, CSG, volumetria, librerie di asset e superfici di rivoluzione analitiche (lathe). Disponibile in inglese e italiano.  
*11-chapter guide from ray tracing theory to production scenes with PBR materials, advanced lighting, CSG, volumetrics, asset libraries, and analytic surfaces of revolution (lathe). Available in English and Italian.*

[EN](./docs/tutorial/en/README.md) · [IT](./docs/tutorial/it/README.md) · [Indice bilingue / Bilingual index](./docs/tutorial/README.md)

### 📋 Reference

Riferimento tecnico completo di ogni chiave YAML accettata dal motore: world, camera, materiali, primitive, luci, CSG, import e template. Disponibile in inglese e italiano.  
*Complete technical reference for every YAML key the engine accepts: world, camera, materials, primitives, lights, CSG, imports, and templates. Available in English and Italian.*

[EN](./docs/reference/scene-reference.md) · [IT](./docs/reference/riferimento-scene.md) · [Indice bilingue / Bilingual index](./docs/reference/README.md)

**Profili di Rendering / Rendering Profiles** — guida pratica ai parametri CLI di qualità render (`-s`, `-d`, `-S`, `-C`) con tre profili canonici (Preview / Standard / Final) e tip per non sprecare tempo di render.  
*Practical guide to the render-quality CLI parameters (`-s`, `-d`, `-S`, `-C`) with three canonical profiles and tips for avoiding wasted render time.*

[EN](./docs/reference/rendering-profiles.md) · [IT](./docs/reference/profili-di-rendering.md)

---

## 📖 Documentazione Tecnica

Per chi vuole approfondire gli aspetti matematici e le scelte implementative:

- [**Pipeline di Rendering**](./docs/technical/rendering-pipeline.md) — Flusso completo dall'YAML al pixel: inizializzazione, scene analysis, TraceRay e post-processing.
- [**Modello di Shading e Materiali**](./docs/technical/shading-model.md) — Disney BSDF, Fresnel (Schlick) e Normal Mapping (TBN).
- [**Path Tracing e Illuminazione**](./docs/technical/path-tracing-and-lighting.md) — NEE, Russian Roulette, campionamento HDRI e Sphere Light.
- [**Multiple Importance Sampling (MIS)**](./docs/technical/multiple-importance-sampling.md) — Estimatore di Veach, heuristiche balance/power, contratti `Sample`/`Pdf`/`Evaluate` e casi limite (lobi delta, MixMaterial, phase function in volumi).
- [**Strutture di Accelerazione (BVH)**](./docs/technical/acceleration-structures.md) — Bounding Volume Hierarchy e SAH.
- [**Geometria del Toro e Risolutore di Quartiche**](./docs/technical/quartic-solver-and-torus.md) — Intersezione analitica raggio-toro e metodo di Ferrari.
- [**CSG — Constructive Solid Geometry**](./docs/technical/csg-boolean-operations.md) — Algoritmo di classificazione a intervalli, gestione delle normali e alberi booleani annidati.
- [**Benchmark (`RayTracer.Benchmarks`)**](./docs/technical/benchmarks.md) — Harness BenchmarkDotNet per AABB e BVH: esecuzione, output, aggiunta di nuovi benchmark.
- [**Testing (`RayTracer.Tests`)**](./docs/technical/testing.md) — Suite xUnit: test di equivalenza BVH ↔ HittableList, differenziali AABB, pattern riusabili.

---

## 🤖 Collaborazione AI

Questo progetto è stato sviluppato con il supporto di tecnologie di Intelligenza Artificiale agentica e modelli di linguaggio avanzati:

![Antigravity](https://img.shields.io/badge/Developed%20with-Antigravity-9B51E0?logo=google&logoColor=white)
![Claude AI](https://img.shields.io/badge/Assist-Claude%20AI-D17051?logo=anthropic&logoColor=white)
![GitHub Copilot](https://img.shields.io/badge/Assist-GitHub%20Copilot-000000?logo=githubcopilot&logoColor=white)

---

## 📄 Licenza

Questo progetto è distribuito sotto licenza **MIT**. Consulta il file [LICENSE](LICENSE) per i dettagli.
