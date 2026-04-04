# 3D-Ray: High-Performance .NET 10 RayTracer Engine

![C#](https://img.shields.io/badge/language-C%23-239120?logo=c-sharp&logoColor=white) ![.NET 10](https://img.shields.io/badge/framework-.NET%2010-512BD4?logo=dotnet&logoColor=white) ![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-0078D4) ![License](https://img.shields.io/badge/license-MIT-blue)

Un moderno motore di ray tracing ad alte prestazioni sviluppato in C# e .NET 10, con configurazione di scene tramite YAML e capacità di rendering avanzate basate su fisica (PBR).

> **English Description:** *A modern, parallelized ray-tracing engine built with C# and .NET 10, featuring YAML scene configuration and advanced physically-based rendering capabilities.*

![Sample Sphere Showcase](output/sample-sphere-showcase.png)

---

## 🔍 Panoramica (Overview)

**3D-Ray** è un motore di rendering ray-tracing ad alte prestazioni sviluppato in C# su piattaforma .NET 10. È progettato per sviluppatori e appassionati di computer grafica che necessitano di uno strumento flessibile e potente per generare immagini fotorealistiche partendo da descrizioni testuali delle scene.

Il motore risolve il problema della visualizzazione di geometrie complesse e materiali fisicamente basati (PBR) attraverso un'architettura modulare ottimizzata per il calcolo parallelo multi-core, con una pipeline di post-processing ACES filmic per risultati visivi di qualità cinematografica.

---

## ✨ Caratteristiche Principali (Key Features)

### Rendering
- 🚀 **Rendering Parallelo** — sfrutta tutti i core logici della CPU per una scalabilità lineare delle prestazioni.
- 🔁 **Path Tracing** con rimbalzi multipli configurabili: riflessi, rifrazioni, occlusione ambientale e color bleeding emergono naturalmente dalla simulazione fisica.
- 📷 **Camera con Depth of Field** — apertura e distanza di messa a fuoco configurabili per effetti bokeh fotorealistici.
- 🎯 **Next Event Estimation (NEE)** — campionamento diretto delle sorgenti di luce per convergenza più rapida e meno rumore.
- 🧮 **Campionamento Stratificato** — riduce il rumore a parità di campioni totali.
- 🎲 **Russian Roulette** adattiva — terminazione stocastica dei raggi calibrata sull'illuminazione della scena per efficienza ottimale.
- 🎞️ **Tone Mapping ACES Filmic** — post-processing cinematografico con highlight naturali e colori ricchi.

### Accelerazione
- 📦 **BVH (Bounding Volume Hierarchy)** — struttura di accelerazione spaziale per intersezioni raggio-oggetto in tempo **O(log N)**, attivata automaticamente in base alla complessità della scena.

### Geometrie
- ⚪ **Sphere** — sfera analitica
- 📦 **Box** — parallelepipedo allineato agli assi
- 🔩 **Cylinder** — cilindro finito con caps
- 🍦 **Cone** — cono finito o tronco di cono con caps
- 💊 **Capsule** — cilindro con estremità emisferiche
- 🍩 **Torus** — toro con intersezione analitica esatta ([dettagli tecnici](./docs/technical/quartic-solver-and-torus.md))
- ⭕ **Annulus** — disco con foro circolare (rondella)
- ⏺ **Disk** — disco piatto
- ▰ **Quad** — quadrilatero parametrico
- 🔺 **Triangle / SmoothTriangle** — triangolo con shading flat o interpolato per-vertex (Phong)
- ▬ **Infinite Plane** — piano infinito per pavimenti e sfondi
- 🏠 **Mesh (OBJ)** — modelli 3D da file Wavefront OBJ con smooth shading, UV mapping dell'artista e BVH interno dedicato
- 🔷 **CSG (Constructive Solid Geometry)** — operazioni booleane su solidi: **Union** (A ∪ B), **Intersection** (A ∩ B) e **Subtraction** (A \ B), annidabili ricorsivamente per forme arbitrariamente complesse ([dettagli tecnici](./docs/technical/csg-boolean-operations.md))

### Materiali
- 🎨 **Lambertian** — diffuso opaco
- 🪞 **Metal** — riflesso speculare con rugosità (`fuzz`) configurabile
- 💎 **Dielectric** — vetro e trasparenti con rifrazione e riflesso Fresnel
- 💡 **Emissive** — materiale auto-luminoso; gli oggetti emissivi partecipano automaticamente alla NEE come sorgenti di luce geometriche
- 🌟 **Disney Principled BSDF** — materiale PBR unificato (`"disney"` / `"pbr"`): un singolo tipo copre plastica, metallo, vetro, vernice auto, tessuto, pelle e qualsiasi combinazione tramite parametri `metallic`, `roughness`, `subsurface`, `specular`, `sheen`, `clearcoat`, `spec_trans` e `ior`

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
- ✨ **Emissive Objects** — qualsiasi geometria con materiale `emissive` diventa sorgente di luce visibile con illuminazione indiretta naturale
- 🌐 **Environment Light** — gradient sky e HDRI partecipano alla NEE come sorgenti di luce direzionali campionabili

### Ambiente
- 🌅 **Gradient Sky** — cielo procedurale con gradiente verticale a 3 bande (zenith, orizzonte, terreno) e sun disk con glow halo. Configurabile via YAML con preset per mezzogiorno, golden hour, tramonto e notte.
- 🌍 **IBL / HDRI** — Image-Based Lighting da file Radiance `.hdr`: illuminazione globale fotorealistica catturata da fotografie HDR a 360° con importance sampling per convergenza rapida. Compatibile con [Poly Haven](https://polyhaven.com/hdris) (CC0).

---

## 🚀 Quick Start

### Prerequisiti
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Compilazione
```powershell
cd 3d-ray
dotnet build src/RayTracer/RayTracer.csproj -c Release
```

### Esecuzione

Render di prova (veloce):
```powershell
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- -i scenes/pendolo-newton.yaml -s 16 -d 20 -o output/render-draft.png -w 480 -H 270
```

Render finale Full HD:
```powershell
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- -i scenes/pendolo-newton.yaml -s 256 -d 60 -o output/render-final.png -w 1920 -H 1080
```

---

## 📁 Struttura del Progetto

```
3d-ray/
├── docs/                    # Documentazione del progetto
│   └── technical/           # Deep dive tecnici
├── src/
│   ├── RayTracer/           # Motore principale
│   │   ├── Acceleration/    # BVH
│   │   ├── Camera/          # Camera con DOF
│   │   ├── Core/            # Ray, HitRecord, MathUtils
│   │   ├── Geometry/        # Primitive (Sphere, Box, Cylinder, CsgObject...)
│   │   ├── Lights/          # Point, Directional, Spot, Area, GeometryLight, EnvironmentLight
│   │   ├── Materials/       # Lambertian, Metal, Dielectric, Emissive, Disney BSDF
│   │   ├── Rendering/       # Renderer, SkySettings, EnvironmentMap
│   │   ├── Scene/           # SceneLoader, SceneData
│   │   └── Textures/        # Checker, Noise, Marble, Wood, Image, NormalMap
│   └── Tools/
│       ├── TextureGen/      # Generatore texture procedurali (PNG)
│       └── NormalMapGen/    # Generatore flat normal map per test
├── scenes/                  # File YAML di esempio
├── output/                  # Immagini renderizzate
├── tutorials/               # Tutorial per l'uso del motore
└── .github/workflows/       # CI con smoke test
```

---

## 🛠️ Tool Inclusi

### TextureGen
Genera texture procedurali pronte all'uso (mattoni, legno, marmo, griglia UV):
```powershell
dotnet run --project src/Tools/TextureGen/TextureGen.csproj
```

### NormalMapGen
Genera una normal map piatta per testare il sistema di normal mapping:
```powershell
dotnet run --project src/Tools/NormalMapGen/NormalMapGen.csproj
```

---

## 📖 Guida all'Uso e CLI

### Parametri CLI

| Parametro | Alias | Default | Descrizione |
|-----------|-------|---------|-------------|
| `--input` | `-i` | — (**obbligatorio**) | Percorso del file YAML della scena. |
| `--output` | `-o` | `output/render-<scena>.png` | File di output. Se omesso, generato dal nome della scena. |
| `--width` | `-w` | `1200` | Larghezza in pixel. |
| `--height` | `-H` | `800` | Altezza in pixel. |
| `--samples` | `-s` | `16` | Campioni per pixel. Il numero effettivo viene arrotondato al quadrato perfetto superiore (`√N × √N`). |
| `--depth` | `-d` | `50` | Massimo numero di rimbalzi ricorsivi per raggio. |
| `--shadow-samples` | `-S` | *(da YAML)* | Override globale dei shadow samples per tutte le area light. |
| `--camera` | `-c` | *(prima camera)* | Seleziona la camera per nome o indice (0-based). |
| `--list-cameras` | — | — | Elenca le camere disponibili nella scena ed esce. |
| `--help` | `-h` | — | Mostra il messaggio di aiuto ed esce. |

> **Nota:** `-H` è maiuscola perché `-h` è riservato a `--help`. `-S` (maiuscola) è per `--shadow-samples`, `-s` (minuscola) per `--samples`.

---

## 💡 Esempi Pratici

### Anteprima Rapida
```powershell
dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/chess.yaml -o preview.png -w 400 -H 267 -s 1 -d 5 -S 4
```

### Qualità Draft
```powershell
dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/chess.yaml -o draft.png -w 800 -H 533 -s 16 -d 20
```

### Produzione Full HD
```powershell
dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/chess.yaml -o final.png -w 1920 -H 1080 -s 128 -d 50 -S 32
```

### Output in JPEG
Il formato viene rilevato automaticamente dall'estensione:
```powershell
dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/chess.yaml -o render.jpg -s 32
```

### Multi-Camera
```powershell
dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/chess.yaml --list-cameras
dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/chess.yaml -c top -o top.png
dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/chess.yaml -c 2 -o cam2.png
```

---

## 📚 Tutorials

- [**Guida all'Uso**](./tutorials/01-tutorial-utilizzo.md) — Dettagli completi sui parametri CLI, profili di rendering, ottimizzazione e risoluzione problemi.
- [**Creazione delle Scene**](./tutorials/02-tutorial-scene.md) — Guida completa alla sintassi YAML: geometrie, materiali, texture, luci, camera e trasformazioni.
- [**Libreria di Preset e Asset**](./tutorials/03-libreria-preset.md) — Catalogo di ambienti, configurazioni camera, sistemi di illuminazione e materiali pronti all'uso.
- [**Libreria CSG — Oggetti e Preset Booleani**](./tutorials/04-libreria-csg.md) — Catalogo di forme CSG pronte all'uso: lenti, anelli, colonne scavate, bulloni e alberi booleani complessi.

---

## 📖 Documentazione Tecnica

Per chi vuole approfondire gli aspetti matematici e le scelte implementative:

- [**Modello di Shading e Materiali**](./docs/technical/shading-model.md) — Disney BSDF, Fresnel (Schlick) e Normal Mapping (TBN).
- [**Path Tracing e Illuminazione**](./docs/technical/path-tracing-and-lighting.md) — Funzionamento del motore, NEE e Russian Roulette.
- [**Strutture di Accelerazione (BVH)**](./docs/technical/acceleration-structures.md) — Bounding Volume Hierarchy e SAH.
- [**Geometria del Toro e Risolutore di Quartiche**](./docs/technical/quartic-solver-and-torus.md) — Intersezione analitica raggio-toro e metodo di Ferrari.
- [**CSG — Constructive Solid Geometry**](./docs/technical/csg-boolean-operations.md) — Algoritmo di classificazione a intervalli, gestione delle normali e alberi booleani annidati.

---

## 🤖 Collaborazione AI

Questo progetto è stato sviluppato con il supporto di tecnologie di Intelligenza Artificiale agentica e modelli di linguaggio avanzati:

![Antigravity](https://img.shields.io/badge/Developed%20with-Antigravity-9B51E0?logo=google&logoColor=white)
![Claude AI](https://img.shields.io/badge/Assist-Claude%20AI-D17051?logo=anthropic&logoColor=white)
![Gemini AI](https://img.shields.io/badge/Assist-Gemini%20AI-4285F4?logo=google-gemini&logoColor=white)

---

## 📄 Licenza

Questo progetto è distribuito sotto licenza **MIT**. Consulta il file [LICENSE](LICENSE) per i dettagli.
