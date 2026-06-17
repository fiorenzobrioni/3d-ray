# 3D-Ray: Path Tracer CPU Professionale - C# / .NET 10

> 🌐 [English](README.md) | **Italiano**

[![C#](https://img.shields.io/badge/language-C%23-239120?logo=c-sharp&logoColor=white)](https://dotnet.microsoft.com/)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-0078D4)
[![License: MIT](https://img.shields.io/badge/license-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![CI](https://github.com/fiorenzobrioni/3d-ray/actions/workflows/dotnet.yml/badge.svg)](https://github.com/fiorenzobrioni/3d-ray/actions/workflows/dotnet.yml)

Un'esplorazione personale del ray tracing cresciuta, una feature alla volta, in un path tracer CPU completo, scritto interamente in C#/.NET 10 senza dipendenze native. Disney Principled BSDF, NEE+MIS, denoiser NFOR, volumetria completa, caustiche, displacement - tutto da un singolo file YAML.

![Spheres Classic](renders/spheres-classic.png)

---

## 🔍 Panoramica (Overview)

3D-Ray è un path tracer nato dalla curiosità e dalla passione per il rendering. Quello che è iniziato come uno studio personale degli algoritmi di trasporto della luce ha accumulato nel tempo un feature set solido, scritto interamente in C#/.NET 10, che sfrutta tutti i core della CPU tramite le primitive parallele di .NET, senza codice nativo né dipendenze da engine esterni. Descrivi la scena (luci, materiali, geometria, camera) in un file YAML e ottieni un'immagine fisicamente accurata con un look cinematografico. Niente codice. Niente boilerplate.

Il sistema di materiali ruota attorno a un **Disney Principled BSDF completo**: un singolo tipo che copre l'intera gamma, dall'intonaco opaco all'oro a specchio, dal vetro d'acqua profonda con assorbimento Beer-Lambert dipendente dallo spessore alla pellicola di sapone iridescente con interferenza thin-film, con energy compensation per metalli rugosi, subsurface shaping per pelle e cera, Charlie sheen per velluto e microfibre. Un Mix Material a livelli con mask spaziali gestisce usura, ruggine, invecchiamento e composizioni ricorsive senza limiti di profondità.

Il sistema di illuminazione è di classe professionale. **Next Event Estimation con Multiple Importance Sampling** converge velocemente anche su illuminazioni complesse e occluse. Le **caustiche focalizzate** via photon mapping illuminano qualsiasi geometria speculare (vetro, acqua, cristalli, specchi) senza configurazione per-oggetto; si abilitano con `--caustics on` o i preset `final`/`ultra`. Il **motion blur** segue il tempo di esposizione per-camera, dal freeze-frame alle lunghe esposizioni cinematografiche. Lo **stack volumetrico completo** (nebbia omogenea, height fog, atmosfera Nishita, nubi fBm, media partecipanti) si integra direttamente con la NEE all'interno del volume.

Il rendering è parallelizzato su tutti i core logici via tile scheduler 16×16, accelerato da un BVH con costruzione SAH parallela, e ripulito opzionalmente da un **denoiser NFOR feature-guided** che opera sulla beauty HDR lineare prima del tone mapping. L'output AOV (albedo, normal, depth, variance) finisce come layer in un **OpenEXR multilayer** per il compositing downstream. Il tone mapping ACES filmic chiude la pipeline.

Una scala di qualità da `draft-small` (anteprima istantanea, pochi secondi) a `final`/`ultra` (non filtrato, pronto per il portfolio) permette di iterare veloce e consegnare pulito: un solo parametro, stesso YAML.

Per la roadmap dettagliata e le feature in corso consulta il [**PLANNING**](./PLANNING.md); per lo storico dei cicli di sviluppo il [**DEVLOG**](./devlog/).

---

## ✨ Caratteristiche Principali (Key Features)

### Rendering
- 🚀 **Rendering Parallelo** - sfrutta tutti i core logici della CPU per una scalabilità lineare delle prestazioni.
- 🔁 **Path Tracing** con rimbalzi multipli configurabili: riflessi, rifrazioni, occlusione ambientale e color bleeding emergono naturalmente dalla simulazione fisica.
- 📷 **Camera con Depth of Field** - apertura e distanza di messa a fuoco configurabili per effetti bokeh fotorealistici.
- 🎬 **Multi-Camera** - più camere definite nella stessa scena, selezionabili da CLI per nome o indice per generare più inquadrature dallo stesso file YAML.
- 💨 **Motion Blur** - sfocatura da movimento fisica per oggetti e camera: gli elementi in moto tracciano scie proporzionali alla loro velocità durante l'esposizione. L'apertura dell'otturatore è configurabile per-camera, dal freeze-frame alle lunghe esposizioni cinematografiche.
- 🎯 **Next Event Estimation con MIS** - a ogni rimbalzo il motore punta direttamente le luci invece di aspettare che un raggio le incontri per caso, bilanciando automaticamente i due approcci per convergere più in fretta con meno rumore. Funziona con tutti i materiali e anche all'interno dei volumi (nebbia, fumo).
- 🔬 **Caustiche focalizzate** - le macchie di luce concentrate che vetro, acqua, cristalli e specchi proiettano su una superficie: fuochi di lente, riflessi luminosi, ombre colorate sotto un vetro tinto. Funzionano con qualsiasi geometria speculare e con tutte le luci (sole compreso), senza configurazione aggiuntiva nella scena.
- 🧮 **Campionamento Stratificato** - riduce il rumore a parità di campioni totali.
- 🔢 **Campionamento Sobol** - distribuisce i campioni in modo più uniforme del caso puro, così l'immagine si pulisce più in fretta a parità di campioni per pixel.
- 🎲 **Russian Roulette** adattiva - abbandona in anticipo i raggi che contribuiscono poco all'immagine, concentrando il tempo di calcolo dove fa davvero la differenza.
- 🎞️ **Tone Mapping ACES Filmic** - post-processing cinematografico con highlight naturali e colori ricchi.
- 🧹 **Denoiser feature-guided** - ripulisce la grana residua dell'immagine sfruttando le informazioni di colore, normali e profondità della scena. Preserva bordi nitidi e dettaglio nelle zone già pulite, applicando il filtraggio solo dove il rumore è più visibile.
- 📤 **Output HDR e AOV (PFM/EXR)** - con `-o scena.exr` l'immagine principale viene salvata in OpenEXR ad alta gamma dinamica, prima del tone mapping: nessun highlight bruciato, con esposizione e color grading regolabili in post senza ri-renderizzare. I buffer ausiliari richiesti con `--aov albedo,normal,depth,beauty,variance` finiscono come layer dello stesso file multilayer, oppure come file separati PFM/EXR (`--aov-format`).
- 🖼️ **Output multi-formato** - PNG, JPEG, BMP (display, tone-mapped) ed EXR (HDR lineare) con rilevamento automatico dall'estensione del file.

### Accelerazione
- 📦 **BVH (Bounding Volume Hierarchy)** - struttura di accelerazione spaziale che fa scalare il render anche a scene con moltissime geometrie, riducendo drasticamente il lavoro per raggio. Costruzione parallelizzata sulle scene grandi e attivazione automatica in base alla complessità della scena.

### Geometrie
- ⚪ **Sphere** - sfera analitica
- 📦 **Box** - parallelepipedo allineato agli assi
- 🔩 **Cylinder** - cilindro finito con caps
- 🍦 **Cone** - cono finito o tronco di cono con caps
- 💊 **Capsule** - cilindro con estremità emisferiche
- 🍩 **Torus** - toro con intersezione analitica esatta
- ⭕ **Annulus** - disco con foro circolare (rondella)
- ⏺ **Disk** - disco piatto
- ▰ **Quad** - quadrilatero parametrico
- 🔺 **Triangle / SmoothTriangle** - triangolo con shading flat o interpolato per-vertex (Phong)
- ▬ **Infinite Plane** - piano infinito per pavimenti e sfondi
- 🏠 **Mesh (OBJ)** - modelli 3D da file Wavefront OBJ con smooth shading, UV mapping dell'artista e BVH interno dedicato
- 🏔️ **HeightField** - superficie di terreno continua intersecata analiticamente. La heightmap può essere un PNG-16 (output di `TerrainGen`) o sintetizzata da noise procedurale al caricamento. Supporta band di strata per altitudine e pendenza (sabbia/erba/roccia/neve), piano d'acqua opzionale e tutti i materiali del motore.
- 🔷 **CSG (Constructive Solid Geometry)** - operazioni booleane su solidi: **Union** (A ∪ B), **Intersection** (A ∩ B) e **Subtraction** (A \ B), annidabili ricorsivamente per forme arbitrariamente complesse
- 🏺 **Lathe (Superficie di Rivoluzione)** - profilo 2D fatto ruotare attorno all'asse Y per ottenere vasi, calici, colonne e lampade senza tassellatura. Tre modalità di profilo: **linear** (segmenti con spigoli netti, look tornito), **Catmull-Rom** (curva liscia che passa per ogni punto) e **Bezier cubico** (control point manuali).
- 🪚 **Extrusion (Estrusione lineare di un profilo 2D)** - profilo 2D chiuso fatto scorrere lungo l'asse Y per ottenere prismi a sezione qualunque: stelle, ingranaggi, lettere, scudi, profilati architettonici, sezioni a L/U/T/H, washer, medaglioni. **I profili concavi sono supportati** grazie alla triangolazione automatica delle facce di chiusura. Stesse tre modalità del Lathe (**linear**, **Catmull-Rom**, **Bezier**) più due modificatori opzionali: **twist** (rotazione del profilo lungo l'altezza) e **taper** (rastremazione della sezione superiore) per colonne attorcigliate, raccordi industriali e forme che combinerebbero altrimenti più operatori in un editor 3D.

### Struttura della Scena
- 🌳 **Scene Graph (Gruppi)** - Composizione gerarchica di oggetti con trasformazioni ereditate. Gruppi annidabili con primitive, CSG, mesh e altri gruppi.
- 🏭 **Template / Istanze** - Definisci oggetti composti una volta come template, istanzia N volte con trasformazioni e materiali indipendenti. Librerie di oggetti importabili da file YAML separati.
- 📦 **Import YAML** - Scomposizione di scene complesse in file separati. Librerie riutilizzabili di materiali, template, oggetti e luci con import annidati e protezione ciclica.
### Materiali
- 🎨 **Lambertian** - diffuso opaco
- 🪞 **Metal** - riflesso speculare con rugosità (`fuzz`) configurabile
- 💎 **Dielectric** - vetro e trasparenti con rifrazione e riflesso Fresnel
- 💡 **Emissive** - materiale auto-luminoso; gli oggetti emissivi partecipano automaticamente alla NEE come sorgenti di luce geometriche
- 🌟 **Disney Principled BSDF** - materiale PBR unificato (`"disney"` / `"pbr"`): un singolo tipo copre plastica, metallo, vetro, vernice auto, tessuto, pelle, bolle di sapone e qualsiasi combinazione. Oltre ai parametri classici (`metallic`, `roughness`, `specular`, `sheen`, `clearcoat`, `spec_trans`, `ior`) supporta:
  - **Anisotropia** per highlight allungati stile metallo spazzolato, capelli e vinile.
  - **Multi-scattering energy compensation** per metalli rugosi convincenti (oro e rame anche a roughness alta).
  - **Beer-Lambert per il vetro** con assorbimento dipendente dallo spessore: liquori, bottiglie colorate, acque profonde.
  - **Diffuse transmission & thin-walled** per fogli, foglie, tendaggi e paralumi.
  - **Subsurface shaping** con tinte sotto-pelle dedicate per pelle, cera e marmo.
  - **Clearcoat avanzato** con IOR e normal map proprie per carrozzerie, lacche e vinile protetto.
  - **Charlie sheen** per microfibre realistiche (velluto, pesca, muschio).
  - **Thin-film iridescence** per bolle di sapone, opal e rivestimenti dicroici.

- 🔀 **Mix Material** - blending tra due materiali qualsiasi con peso costante o texture mask spaziale (noise, marble, image…). Per effetti di ruggine, usura, transizioni graduali, decal e composizioni ricorsive (mix-of-mix)

### Texture
- ♟ **Checker** - scacchiera 3D procedurale
- 🌀 **Noise** - rumore Perlin (liscio o turbolento) con `noise_type`, `octaves`, `lacunarity`, `gain`, `distortion`; modalità `perlin` / `fbm` / `turbulence` / `ridged` / `billow` più i due multifrattali **Musgrave** `hetero_terrain` e `hybrid_multifractal` per terreni erosi e roccia stratificata
- 🏔 **Marble** - marmo procedurale realistico con venature multi-strato, distorsione che elimina il tiling visibile, pieghe geologiche, variazione cromatica di fondo e impurità minerali.
- 🪵 **Wood** - legno procedurale realistico con anelli di crescita asimmetrici e variabili, venatura e figure del taglio, pori, gradiente alburno/durame e nodi. Il pattern degli anelli può pilotare anche `roughness` e `sheen` del Disney BSDF.
- 🔷 **Voronoi / Worley** - pattern cellulari con dieci canali di output e metriche euclidean/manhattan/chebyshev, ideali per rocce, scaglie, mosaici e ciottoli. Colore per-cella libero o pilotato da palette/color ramp, con bordi netti o ammorbiditi (`smoothness`).
- 🧱 **Brick** - pattern mattoni running-bond con variazione per-mattone e weathering
- 🌈 **Gradient** - sfumature lineari, quadratiche, easing, sferiche e radiali
- 🖼 **Image Texture** - texture da file (PNG, JPEG, BMP, GIF, TIFF, WebP) con bilinear filtering, tiling configurabile e **mipmap pyramid + EWA anisotropic filtering** per niente moiré né shimmer a basso angolo o a 4K
- 🗺 **Normal Map** - dettaglio geometrico superficiale senza triangoli aggiuntivi; compatibile OpenGL e DirectX-style (`flip_y`)
- 🎨 **Color Ramp multi-stop** - blocco `color_ramp:` opzionale che sostituisce il lerp implicito a due colori su noise/marble/wood/voronoi/gradient. Stop multipli a posizione libera con quattro modi di interpolazione (linear, smoothstep, ease, constant): marmi a 3+ toni, sapwood/heartwood, gradienti sunset, toon bands, heat-map.
- 🧭 **Coordinate** - ritorna le coordinate del punto di shading come RGB nei quattro spazi canonici (`object`, `uv`, `generated`, `world`). Due usi: overlay di debug visivo (UV unwrap, allineamento object/world space) e driver XYZ deterministico per pilotare un'altra texture via mix material.

Tutte le texture procedurali supportano **offset**, **rotation** e **randomizzazione per-oggetto** tramite seed deterministico.

### Texture Filtering (Anti-Aliasing Analitico)
- 🔬 **Anti-aliasing analitico delle texture** - le texture si adattano automaticamente alla distanza e all'angolo di visione:
  - **Texture procedurali** (noise, voronoi, marble…) - niente shimmer né moiré a qualsiasi distanza
  - **Image texture** - filtering anisotropico per nitidezza a basso angolo e a distanza

  Risultato: niente shimmer/moiré a distanza, niente alias a basso angolo, senza aumentare i campioni globali.

### Surface Displacement Stack
- 🟢 **Bump map** - dettaglio di superficie ottenuto perturbando la normale di shading da una texture qualunque (procedurale o image), senza aggiungere geometria. Disponibile su ogni materiale e su tutte le primitive.
- 🔺 **Mesh subdivision** - raffinamento delle mesh OBJ con gli algoritmi Loop (mesh triangolari) e Catmull-Clark (mesh quad/miste), in modalità uniforme o adattiva screen-space.
- 🎯 **Displacement material-level** - il displacement vive sul materiale: una sola definizione guida automaticamente tutte le mesh che lo referenziano, senza configurazione per-entità. Modalità tri-state (bump-only, displacement, o entrambi) e override per singola istanza.
- 🏔️ **Scalar displacement** - deformazione reale della mesh subdivisa lungo la normale: cambia la silhouette dell'oggetto, non solo lo shading.
- 🗿 **Vector displacement** - offset 3D dei vertici letto dal triplet RGB della texture, in tangent space o object space. Permette overhang, pieghe e dettagli che si ripiegano su sé stessi.
- ✨ **Autobump** - bump residuo derivato automaticamente dalla stessa texture di displacement, recupera la frequenza alta che la griglia di subdivision non riesce a rappresentare.
- 🧬 **Mix-displacement** - il mix tra due materiali estende la sua mask spaziale anche al displacement: la transizione tra le due superfici rimane continua e senza cuciture visibili, incluso il bump residuo derivato automaticamente da entrambi.

### Sistema di Trasformazione
- 🔄 **Transform** - scala, rotazione e traslazione applicabili a qualsiasi primitiva, inclusi i nodi CSG.

### Sistema di Illuminazione
- 💡 **Point Light** - luce puntiforme con attenuazione quadratica
- ☀️ **Directional Light** - luce parallela (sole), senza attenuazione
- 🔦 **Spot Light** - faretto con cono interno/esterno e falloff liscio
- 🟧 **Area Light** - emettitore rettangolare con soft shadows fisicamente corretti via campionamento Monte Carlo
- 🟡 **Sphere Light** - luce sferica con solid-angle sampling: penumbra circolare uniforme e zero campioni sprecati. Ideale per lampadine, lanterne e globi luminosi.
- ✨ **Emissive Objects** - qualsiasi geometria con materiale `emissive` diventa sorgente di luce visibile con illuminazione indiretta naturale
- 🌐 **Environment Light** - tutti i tipi di cielo (flat, gradient, Hosek-Wilkie, HDRI) partecipano al campionamento diretto delle luci; il sole analitico è disaccoppiato dal corpo del cielo e combinabile con qualsiasi tipo di sky.

### Ambiente
- ☁️ **Flat Sky** - cielo a colore uniforme. Default `[0.5, 0.7, 1.0]` quando `world.sky` è omesso; partecipa a NEE quando luminanza > 0.
- 🌅 **Gradient Sky** - cielo procedurale con gradiente verticale a 3 bande (zenith, orizzonte, terreno) e sole analitico opzionale agganciato a un `PhysicalSun` con cone sampling stratificato e limb darkening fisicamente corretto.
- ☀️ **Physical Sky (Preetham/Hosek-Wilkie)** - daylight analitico parametrizzato da `turbidity` e `ground_albedo`. `type: hosek_wilkie` o `type: preetham`.
- 🌌 **Nishita Sky** - atmosfera fisica Rayleigh+Mie con LUT trasmittanza precomputata e integrazione single-scattering. Alba e tramonto fisicamente corretti: disco rosso, halo arancione e zenith blu emergono dalla simulazione fisica, non da fitting.
- 🪟 **Portal Light** - finestra o lucernario che si affaccia sull'environment. Concentra il campionamento luminoso nell'apertura per ridurre significativamente il rumore negli interni illuminati dalla luce naturale.
- 🔍 **HDRI mipmap prefiltering** - filtraggio gerarchico dell'immagine ambientale che riduce automaticamente i firefly nelle riflessioni glossy, senza sacrificare la definizione delle zone scure o l'energia complessiva dell'illuminazione.
- 🌫️ **Aerial perspective (Nishita medium)** - attenuazione atmosferica della geometria distante con la stessa fisica Rayleigh + Mie del cielo: montagne, edifici e foreste sfumano nell'azzurro dell'atmosfera in modo fisicamente coerente con il colore del cielo sovrastante.
- 🌍 **IBL / HDRI** - illuminazione ambientale da panoramiche ad alta gamma dinamica (`.hdr` o `.exr`): cattura riflessioni e luce diffusa dall'ambiente reale o sintetico. L'**estrazione automatica del sole** rileva il picco luminoso nella HDRI e lo converte in una sorgente separata per ombre nitide e meno rumore.
- 🎛️ **Visibility flags** - controllo granulare della visibilità per ogni sorgente di luce: può essere visibile dalla camera, proiettare ombre, contribuire alle superfici diffuse, alle riflessioni glossy o alla trasmissione, ognuno in modo indipendente.
- 🖼️ **Background plate** - illumina la scena con un'HDRI e mostra alla camera un'immagine di sfondo diversa: utile per compositing con riprese reali o per sostituire il cielo senza ri-renderizzare l'illuminazione.
- 🧭 **Orientation** quaternion / Euler XYZ - rotazioni complete in 3D per tutti gli oggetti della scena: asse qualunque via angoli Eulero o quaternioni per interpolazione corretta senza gimbal lock.
- 🏞️ **Ground production-grade** - terreno dedicato con quattro forme (piano infinito, quad, disco, heightfield), posizione e normale configurabili, materiale PBR inline, UV transform completa e visibilità granulare per categoria di raggio. Il colore si sincronizza automaticamente con il cielo quando nessun materiale è specificato.

### Volumetria (Participating Media)
- 🌫️ **Homogeneous Medium** - mezzo partecipante uniforme globale per nebbia densa, foschia e effetti subacquei. Beer-Lambert analitico, economico, adatto come base di partenza.
- 🏔️ **Height Fog** - foschia atmosferica con densità che cala esponenzialmente con la quota (`scale_height`, `y0`). Modello "aerial perspective" per scene outdoor: montagne, strade all'alba, vedute urbane.
- 🌀 **Procedural Medium (Perlin fBm)** - nebbia eterogenea generata da rumore Perlin multi-ottava con delta tracking e ratio tracking. Sacche di densità irregolari, god-ray non omogenei, atmosfere da film horror o nubi sparse.
- 🧊 **Grid Medium** - densità campionata su griglia 3D regolare (inline YAML o file binario `.vol`) confinata in una AABB world-space, con filtro di ricostruzione selezionabile: **trilineare** (default, veloce) o **tricubico** Catmull-Rom (più liscio) per rimuovere i kink visibili sulle griglie a bassa risoluzione. Ideale per fumo localizzato, esplosioni, nuvole isolate.
- 🎇 **Cinque phase function** - `isotropic` (scattering uniforme), `hg` (Henyey-Greenstein, asimmetria direzionale), `rayleigh` (scattering atmosferico), `double_hg` (due lobi misti per nubi realistiche) e `schlick` (approssimazione fast-HG). Ogni mezzo combinabile con qualsiasi phase function.
- 🧬 **MediumInterface per-entity** - mezzo partecipante assegnato alla singola entità: nebbia locale in una stanza, fumo in una teiera, acqua in un acquario, atmosfera di un pianeta - senza riempire l'intera scena. Gestisce correttamente volumi trasmissivi annidati, come un vetro che contiene un liquido.
- 🪨 **Subsurface scattering volumetrico (Random Walk)** - diffusione sotto-superficie fisica per marmo, pelle, cera, latte e giada: la luce penetra il volume, si diffonde e riemerge con la traslucenza caratteristica di questi materiali. Tre livelli di qualità (preview / normal / high) per bilanciare velocità e fedeltà.
- 🧪 **Material-embedded SSS** - subsurface scattering volumetrico dichiarato direttamente sul materiale Disney, senza configurazione separata dei volumi: bastano un colore e un raggio di diffusione per ottenere traslucenza fisica su marmo, cera, latte, pelle e altri materiali organici.

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

Sanity check istantaneo (preset `draft-tiny`, 480×270 - pochi secondi):
```bash
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- -i scenes/pendolo-newton -q draft-tiny -o renders/render-sanity.png
```

Render di prova rapido (preset `draft-small`, 960×540 - è il **default** quando si omette `-q`):
```bash
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- -i scenes/pendolo-newton -q draft-small -o renders/render-draft.png
# oppure equivalentemente, omettendo -q:
# dotnet run ... -- -i scenes/pendolo-newton -o renders/render-draft.png
```

Render finale Full HD (preset `final`, 1920×1080, qualità portfolio):
```bash
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- -i scenes/pendolo-newton -q final -o renders/render-final.png
```

Render finale 4K (preset `ultra`, 3840×2160):
```bash
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- -i scenes/pendolo-newton -q ultra -o renders/render-4k.png
```

Render classico con parametri espliciti - il vecchio modo continua a funzionare e ogni flag esplicito vince comunque sul preset (es. `-q final -d 16` per scene con vetri impilati):
```bash
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- -i scenes/pendolo-newton -s 1024 -d 8 -S 4 -o renders/render-final.png -w 1920 -H 1080
```

> **Nota - estensione `.yaml` opzionale:** il flag `-i` accetta sia il percorso completo (`scenes/pendolo-newton.yaml`) sia la versione senza estensione (`scenes/pendolo-newton`). Quando l'estensione è omessa, il loader prova ad aggiungere automaticamente `.yaml` e poi `.yml`. Gli esempi in questo README usano la forma compatta senza estensione.

> Per i profili completi (Preview / Standard / Final), i tip su `-d`, `-s`, `-S`, `-C` e la compensazione fotografica `--exposure` consulta la guida [Profili di Rendering](./docs/reference/profili-di-rendering.md) ([English version](./docs/reference/rendering-profiles.md)).

---

## 👋 La tua prima scena (Hello World)

Gli esempi del Quick Start renderizzano scene già pronte; qui invece **scrivi la tua**. Una scena è un file YAML con quattro sezioni: **com'è l'ambiente** (`world`), **da dove guardiamo** (`cameras`), **di cosa sono fatti gli oggetti** (`materials`) e **quali oggetti ci sono** (`entities`). La scena minima: una sfera rossa su un pavimento a scacchi, illuminata dal cielo.

Crea il file `scenes/hello.yaml`:

```yaml
# Hello World - una sfera rossa su un pavimento a scacchi, illuminata dal cielo.

world:
  # Cielo a gradiente: fa anche da luce ambientale (illumina la scena).
  sky:
    type: "gradient"
    zenith_color:  [0.2, 0.4, 0.9]   # blu in alto
    horizon_color: [0.8, 0.9, 1.0]   # chiaro all'orizzonte
    sun:                             # sole analitico per ombre nette
      direction: [-0.5, -1.0, -0.3]
      intensity: 8.0
  # Pavimento infinito a scacchi; "y: 0" lo appoggia all'altezza zero.
  ground: { type: "infinite_plane", material: "floor", y: 0 }

cameras:
  - name: "main"
    position: [0, 1.5, -5]   # dove si trova l'osservatore
    look_at:  [0, 0.5, 0]    # verso cosa guarda
    fov: 40                  # campo visivo in gradi

materials:
  - id: "floor"              # pavimento: scacchiera procedurale
    type: "lambertian"       # diffuso opaco
    texture:
      type: "checker"
      scale: 2.0                              # dimensione delle caselle
      colors: [[0.8, 0.8, 0.8], [0.15, 0.15, 0.15]]
  - id: "red"                # sfera
    type: "lambertian"
    color: [0.8, 0.2, 0.2]   # rosso

entities:
  - name: "ball"
    type: "sphere"
    center: [0, 0.5, 0]      # posizione del centro
    radius: 0.5
    material: "red"          # riferimento all'id sopra
```

Renderizzala con un'anteprima rapida (la trovi già pronta nel repo):

```bash
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- \
  -i scenes/hello -q draft-small -o renders/hello.png
  # -q draft-small è il preset di default: può essere omesso
```

Il risultato è in `renders/hello.png`. Da qui puoi cambiare `color`, aggiungere altre sfere in `entities` o provare materiali `metal` e `dielectric`. Per l'elenco completo delle chiavi YAML vedi il [Reference](./docs/reference/scene-reference.md); per un percorso guidato il [Tutorial](./docs/tutorial/it/README.md).

![Hello World](renders/hello.png)

---

## 📁 Struttura del Progetto

```
3d-ray/
├── docs/                    # Documentazione del progetto
│   ├── reference/           # Riferimento YAML completo (EN/IT)
│   ├── technical/           # Approfondimenti tecnici interni
│   └── tutorial/            # Tutorial in 12 capitoli (EN/IT)
│       ├── en/              # Tutorial in English
│       └── it/              # Tutorial in italiano
├── src/
│   ├── RayTracer/              # Motore principale
│   │   ├── Acceleration/       # BVH
│   │   ├── Camera/             # Camera con DOF
│   │   ├── Core/               # Ray, HitRecord, MathUtils, sampling
│   │   ├── Denoising/          # Denoiser feature-guided (NLM, NFOR)
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
│       ├── TerrainGen/         # Generatore di Terrain heightfield stratificati
│       ├── FontGen/            # Generatore di font 3D partendo da font di sistema o file .ttf/.otf
│       ├── TextureGen/         # Generatore texture procedurali (PNG)
│       ├── NormalMapGen/       # Generatore flat normal map per test
│       ├── ChessGen/           # Generatore scena scacchiera chess.yaml
│       └── TempleGen/          # Generatore scena tempio-romano.yaml
├── scenes/                     # File YAML di scene
│   ├── presets/                # Cataloghi copia-incolla: materiali, luci, mediums, cielo/terreno, terreni
│   ├── assets/                 # Risorse binarie
│   │   ├── textures/           # Texture PNG (albedo e normal map)
│   │   ├── fonts/              # Template caratteri 3D per extrusion (generati da FontGen)
│   │   └── heightmaps/         # Heightmap PNG-16 (generate da TerrainGen)
│   ├── showcases/              # Scene dimostrative per singola feature
│   └── *.yaml                  # Scene principali del progetto
├── renders/                    # Immagini renderizzate
└── .github/workflows/          # CI con smoke test
```

---

## 🛠️ Tool Inclusi

### TextureGen
Genera un set completo di texture PBR procedurali pronte all'uso (mattoni, legno, cemento, metallo, terra, scacchiera, griglia UV):
```bash
dotnet run --project src/Tools/TextureGen/TextureGen.csproj
```

### NormalMapGen
Genera un set di normal map PBR procedurali pronte all'uso (mattoni, legno, cemento, metallo, pietra, tessuto, piastrelle, flat di riferimento):
```bash
dotnet run --project src/Tools/NormalMapGen/NormalMapGen.csproj
```

### FontGen
Genera template di caratteri 3D da font di sistema o file `.ttf`/`.otf`, pronti per la primitiva `extrusion`. Supporta serif, sans-serif e display font; il flag `--list-fonts` elenca i font installati sulla macchina.
```bash
dotnet run --project src/Tools/FontGen/FontGen.csproj -c Release -- --font "Times New Roman"
dotnet run --project src/Tools/FontGen/FontGen.csproj -c Release -- --font "Impact" --chars "ABC123"
```
Output: `scenes/assets/fonts/font-<nome>.yaml`

### ChessGen
Genera il file YAML di una scacchiera Staunton completa (board 8×8 + 32 pezzi posizionati con trasformazioni). Usato per produrre `scenes/chess.yaml`:
```bash
dotnet run --project src/Tools/ChessGen/ChessGen.csproj
```

### TempleGen
Genera il file YAML di un tempio romano dettagliato con colonne scanalate (`extrusion`), frontone, celle CSG e materiali PBR. Usato per produrre `scenes/tempio-romano.yaml`:
```bash
dotnet run --project src/Tools/TempleGen/TempleGen.csproj
```

### TerrainGen
Genera una heightmap PNG-16 e il corrispondente template YAML pronto per `type: heightfield`. Supporta tipi di terreno diversi, idrologia (fiumi, laghi, mare, isole), stagioni e band di strata (sabbia/erba/roccia/neve). Con `--with-cameras` aggiunge anche una scena di preview pronta al render.
```bash
dotnet run --project src/Tools/TerrainGen/TerrainGen.csproj -- \
  --name <stem> --type pianura|collina|montagna \
  --include fiumi,laghi,mare,isole --season primavera|estate|autunno|inverno \
  [--seed N] [--size U] [--resolution N] [--with-cameras]
```
Output: `scenes/assets/heightmaps/<stem>-height.png` + `scenes/assets/heightmaps/<stem>.yaml`  
Con `--with-cameras`: anche `scenes/<stem>-preview.yaml` (scena pronta al render con cinque camere).

---

## 📖 Guida all'Uso e CLI

### Parametri CLI

| Parametro | Alias | Default | Descrizione |
|-----------|-------|---------|-------------|
| `--input` | `-i` | - (**obbligatorio**) | Percorso del file YAML della scena. L'estensione `.yaml` (o `.yml`) è **opzionale**: se il path non esiste così com'è, il loader prova ad aggiungerla automaticamente (es. `-i scenes/chess` ⇒ `scenes/chess.yaml`). |
| `--output` | `-o` | `renders/render-<scena>.png` | File di output. Se omesso, generato dal nome della scena. L'estensione sceglie il formato: `.png`/`.jpg`/`.bmp` = immagine display tone-mapped; `.exr` = radianza **scene-linear pre-tone-mapping** (OpenEXR multilayer, RGB half + eventuali layer AOV, compressione ZIP) per compositing e grading in post. |
| `--quality` | `-q` | `draft-small` | Preset di qualità che riempie in un colpo `-w -H -s -d -S` (e, per `standard`, anche caustiche/SSS/NEE). **Quando omesso il renderer usa `draft-small`** (960×540, 16 spp, depth 4, denoiser NFOR): un check di composizione veloce e già denoised è un default di primo lancio migliore di una passata lenta e rumorosa. Scala: `draft` → `standard` → `pre-final` → `final` → `ultra`. I primi quattro livelli hanno varianti `-tiny` (480×270) e `-small` (960×540); `ultra` è fisso a 3840×2160. I preset `draft*`/`standard*`/`pre-final*` attivano anche il denoiser (`--denoiser nfor`); `final`/`ultra` no. **Qualunque flag esplicito vince sul preset** (es. `-q final -d 16` per scene con vetri impilati). `standard` = qualità final su scene classiche (Lambertian/Disney, vetri non annidati, marmo procedurale) senza gli extra costosi; `pre-final` = anteprima fedele di `final` (feature complete, 256 spp + denoiser, ~4-6× più veloce) - vedi i [Profili di Rendering](./docs/reference/profili-di-rendering.md). |
| `--width` | `-w` | `960` (da `draft-small`) | Larghezza in pixel. |
| `--height` | `-H` | `540` (da `draft-small`) | Altezza in pixel. |
| `--samples` | `-s` | `16` (da `draft-small`) | Campioni per pixel. Con il sampler Sobol (default) viene usato il conteggio esatto; con `--sampler prng` viene arrotondato al quadrato perfetto superiore (`√N × √N`). |
| `--depth` | `-d` | `4` (da `draft-small`) | Massimo numero di rimbalzi ricorsivi per raggio. Alza a `8` (o `16+` per dielettrici impilati: vetri annidati, liquidi nei bicchieri). |
| `--shadow-samples` | `-S` | *(da YAML)* | Override globale dei shadow samples per tutte le area light. Usa quadrati perfetti (`1, 4, 9, 16`). |
| `--clamp` | `-C` | `10` | Firefly clamp: massima radianza per-campione prima del tone mapping. Abbassa (es. `5`) per scene problematiche con vetri/nebbia, alza per highlight molto intensi. |
| `--indirect-clamp-factor` | - | `0.25` | Fattore di clamp per i bounce indiretti (depth ≥ 1). Default `0.25` = attivo (clamp indiretto = `2.5` con `-C 10`); `1.0` = disabilitato. Applicato una sola volta, relativo alla camera. |
| `--exposure` | - | `0` EV | Compensazione fotografica in stop, applicata come `2^EV` **prima** del tone map ACES. Negativo scurisce (`-1` = ½, `-2` = ¼), positivo schiarisce. Usalo per scivolare scene troppo luminose nella sweet-spot lineare di ACES dove il contrasto delle texture resta visibile. |
| `--camera` | `-c` | *(prima camera)* | Seleziona la camera per nome o indice (0-based). |
| `--sampler` | - | `sobol` | Campionatore per-pixel: `sobol` (Owen-scrambled, default) o `prng` (legacy thread-local). Nessuna differenza di interfaccia scena: cambia solo la sequenza dei numeri casuali. |
| `--mis` | - | `balance` | Heuristica MIS che combina Light Sampling (NEE) e BSDF/Phase Sampling: `balance` o `power` (β=2). Stesso costo computazionale; `power` riduce ulteriormente la varianza quando le PDF disagree (luce piccola + materiale ruvido, sole nella nebbia). |
| `--light-sampling` | - | `all` | Strategia NEE: `all` = somma tutte le luci (default, backward compat); `power` = campiona una luce ∝ `ApproximatePower` (varianza minore in scene multi-luce); `uniform` = campionamento uniforme (debug). |
| `--texture-filtering` | - | `auto` | Anti-aliasing analitico delle texture procedurali e image via ray differentials: `auto`/`on` = filtering attivo (Perlin/fBm octave clamp, Voronoi supersampling adattivo, image mipmap + EWA anisotropico); `off` = point-sampled puro (utile come baseline per benchmark/AB). |
| `--caustics` | - | `off`¹ | Caustiche focalizzate via **photon mapping**: `on` attiva un pre-pass che emette fotoni di caustica dalle luci, li traccia attraverso le superfici speculari (specchio/vetro/acqua/metallo) e li deposita dove atterrano su superfici diffuse (cammini `L S+ D`); la passata camera li raccoglie con una stima di densità ai k-nearest. È **generale** (qualunque geometria speculare, **tutti** i tipi di luce - comprese `directional`/sole) e **non richiede alcun flag YAML per-oggetto**. Con `off` il rendering è identico a prima e il costo è nullo. Limiti noti in questa versione: caustiche da vetro/metallo **rough/frosted** e da ambiente **HDRI** ricadono sul path tracer (più rumoroso); la tinta interna Beer-Lambert lungo il cammino dei fotoni non è applicata (lieve differenza di colore su vetro colorato spesso). ¹Attivo di default sui preset `pre-final*`, `final` e `ultra` (un `--caustics` esplicito ha la precedenza). |
| `--caustic-photons` | - | `2–4M`² | Budget di fotoni emessi dal pre-pass di caustica (in milioni di fotoni). Valori più alti = caustiche più nitide e meno rumorose, pre-pass più lento. Nessun effetto con `--caustics off`. ²Default dipendente dal preset (più alto su `final`/`ultra`). |
| `--denoiser` | - | `none`³ | Denoiser feature-guided applicato alla radianza HDR lineare **prima** del tone mapping: `nfor` (regressione first-order guidata da albedo/normale/profondità con selezione per-pixel del candidato a minimo errore stimato - consigliato), `nlm` (media pesata joint NL-means, più rapido e morbido), `none`. La selezione include sempre l'immagine non filtrata come candidato di sicurezza: dove il filtro non può vincere (es. ombre di contatto sottili, invisibili alle feature) il rumore originale viene preservato invece di introdurre bias. ³Attivo di default sui preset `draft*` (nfor fast), `standard*` e `pre-final*` (nfor high); `final`/`ultra` restano puri. Un `--denoiser` esplicito ha sempre la precedenza. |
| `--denoise-quality` | - | `high` | Compromesso velocità/qualità del denoiser: `high` = finestra di ricerca 19×19, due candidati di intensità combinati per-pixel; `fast` = finestra 15×15, candidato singolo (~2× più veloce). |
| `--aov` | - | - | Lista separata da virgole di buffer ausiliari (HDR lineare, scene-referred): `albedo`, `normal` (world-space), `depth` (distanza camera, `-1` = cielo), `beauty` (radianza lineare pre-esposizione; **post-denoise** se il denoiser è attivo), `variance` (varianza dual-buffer grezza). Con `-o out.png` produce file PFM separati (`out.albedo.pfm`, ...); con `-o out.exr` gli AOV diventano **layer del file multilayer** (`albedo.R/G/B`, `normal.X/Y/Z`, `Z` float32, `variance.R/G/B`). |
| `--aov-format` | - | *(auto)* | Forza un file separato per ogni AOV nel formato dato: `pfm` o `exr` (es. `out.depth.exr` con un singolo canale `Z` float32). Senza flag: layer incorporati se `-o` è `.exr`, altrimenti file PFM separati. |
| `--sss-mode` | - | `auto` | Dispatch del random walk subsurface scattering: `auto` (default) - i media bound a entità con `σ_s > 0` attivano il walk; `off` - i media pushati sono declassati ad assorbimento solo (Beer-Lambert legacy), utile per preview rapide e A/B comparison. |
| `--sss-quality` | - | da `-q` | Preset random-walk: `preview` (16 vol-bounce, no NEE in-walk), `normal` (64, NEE on), `high` (256, NEE on). Se omesso, ereditato dal preset `-q` (`draft*` → preview, `pre-final*`/`final*`/`ultra` → high; su `standard*` il SSS è disattivato). |
| `--max-volume-bounces` | - | da `--sss-quality` | Cap massimo sui bounce del random walk in un'entità. Override del valore del preset, utile per stress test su media densi (`--max-volume-bounces 16`) o per qualità extra (`--max-volume-bounces 512`). |
| `--list-cameras` | - | - | Elenca le camere disponibili nella scena ed esce. |
| `--verbose` | `-v` | - | Mostra informazioni dettagliate durante il caricamento e l'analisi della scena (import, template, σ del medium, tuning Russian Roulette). Utile per debug e sviluppo scene. |
| `--help` | `-h` | - | Mostra il messaggio di aiuto ed esce. |

> **Nota:** `-H` è maiuscola perché `-h` è riservato a `--help`. Le maiuscole sono usate per gli "override avanzati": `-S` (`--shadow-samples`) e `-C` (`--clamp`); `-s` minuscola per `--samples`, `-c` minuscola per `--camera`.

> **Profili di rendering pronti all'uso:** vedi [Profili di Rendering](./docs/reference/profili-di-rendering.md) · [Rendering Profiles (EN)](./docs/reference/rendering-profiles.md).

---

## 💡 Esempi Pratici

### Preset `draft-tiny` (sanity check istantaneo - 480×270)
```bash
dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/chess -q draft-tiny -o sanity.png
```

### Preset `draft-small` (composizione, camere, materiali - secondi, 960×540)
```bash
dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/chess -q draft-small -o preview.png
```

### Preset `standard` (render di qualità quotidiano - Full HD)
Qualità da final su scene classiche (Lambertian/Disney, vetri non annidati,
marmo procedurale): caustiche e SSS volumetrico disattivati, 512 spp, 1
shadow sample, NEE power-weighted, clamp indiretto rilassato e denoiser NFOR
sulla grana residua. Molto più veloce di `final` su questo tipo di scene.
```bash
dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/chess -q standard -o standard.png
```

### Preset `pre-final` (anteprima fedele del final - Full HD)
Stessa feature-set di `final` (caustiche, SSS, depth 8) con 256 spp, 1 shadow
sample e denoiser high: anticipa la resa final a ~4-6× la velocità.
```bash
dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/chess -q pre-final -o preview-final.png
```

### Preset `final` (portfolio, copertina README - Full HD)
```bash
dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/chess -q final -o final.png
```

### Preset `ultra` (4K showcase)
```bash
dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/chess -q ultra -o cover-4k.png
```

### Preset + override (il flag esplicito vince)
Lancia il preset `final` ma alza la depth a 16 per una scena con vetri impilati:
```bash
dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/chess -q final -d 16 -o glass-final.png
```

### Parametri classici (senza preset)
Tutti i flag puoi continuare a passarli a mano: utile per profili custom o per regression test che non devono dipendere dai preset.
```bash
# Profilo Final ricreato a mano
dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/chess -o final.png -w 1920 -H 1080 -s 1024 -d 8 -S 4

# Profilo Standard tile orizzontale 800×533
dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/chess -o draft.png -w 800 -H 533 -s 512 -d 8
```

### Output in JPEG
Il formato viene rilevato automaticamente dall'estensione:
```bash
dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/chess -q standard -o render.jpg
```

### Multi-Camera
```bash
dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/chess --list-cameras
dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/chess -q final -c zenitale -o zenitale.png
dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/chess -q final -c 2 -o hero.png
```

> **Nota:** in tutti questi esempi `-i scenes/chess` equivale a `-i scenes/chess.yaml` - l'estensione `.yaml` (o `.yml`) è opzionale e viene aggiunta automaticamente dal loader se il file non viene trovato così com'è.

---

## 📖 Documentazione e Guide (Documentation)

### 📚 Tutorial

Guida completa in 12 capitoli: dalla teoria del ray tracing alla creazione di scene di produzione con materiali PBR, illuminazione avanzata, CSG, volumetria, preset e progetti, superfici di rivoluzione (lathe) ed estrusioni di profili 2D (extrusion). Disponibile in inglese e italiano.  
*12-chapter guide from ray tracing theory to production scenes with PBR materials, advanced lighting, CSG, volumetrics, presets and projects, surfaces of revolution (lathe) and 2D-profile extrusions (extrusion). Available in English and Italian.*

[EN](./docs/tutorial/en/README.md) · [IT](./docs/tutorial/it/README.md) · [Indice bilingue / Bilingual index](./docs/tutorial/README.md)

### 📋 Reference

Riferimento tecnico completo di ogni chiave YAML accettata dal motore: world, camera, materiali, primitive, luci, CSG, import e template. Disponibile in inglese e italiano.  
*Complete technical reference for every YAML key the engine accepts: world, camera, materials, primitives, lights, CSG, imports, and templates. Available in English and Italian.*

[EN](./docs/reference/scene-reference.md) · [IT](./docs/reference/riferimento-scene.md) · [Indice bilingue / Bilingual index](./docs/reference/README.md)

**Profili di Rendering / Rendering Profiles** - guida pratica ai parametri CLI di qualità render (`-s`, `-d`, `-S`, `-C`) con tre profili canonici (Preview / Standard / Final) e tip per non sprecare tempo di render.  
*Practical guide to the render-quality CLI parameters (`-s`, `-d`, `-S`, `-C`) with three canonical profiles and tips for avoiding wasted render time.*

[EN](./docs/reference/rendering-profiles.md) · [IT](./docs/reference/profili-di-rendering.md)

---

## 📖 Documentazione Tecnica

Per chi vuole approfondire gli aspetti matematici e le scelte implementative:

- [**Pipeline di Rendering**](./docs/technical/rendering-pipeline.md) - Flusso completo dall'YAML al pixel: inizializzazione, scene analysis, TraceRay e post-processing.
- [**Modello di Shading e Materiali**](./docs/technical/shading-model.md) - Disney BSDF, Fresnel (Schlick) e Normal Mapping (TBN).
- [**Path Tracing e Illuminazione**](./docs/technical/path-tracing-and-lighting.md) - NEE, Russian Roulette, campionamento HDRI e Sphere Light.
- [**Multiple Importance Sampling (MIS)**](./docs/technical/multiple-importance-sampling.md) - Estimatore di Veach, heuristiche balance/power, contratti `Sample`/`Pdf`/`Evaluate` e casi limite (lobi delta, MixMaterial, phase function in volumi).
- [**Strutture di Accelerazione (BVH)**](./docs/technical/acceleration-structures.md) - Bounding Volume Hierarchy e SAH.
- [**Geometria del Toro e Risolutore di Quartiche**](./docs/technical/quartic-solver-and-torus.md) - Intersezione analitica raggio-toro e metodo di Ferrari.
- [**CSG - Constructive Solid Geometry**](./docs/technical/csg-boolean-operations.md) - Algoritmo di classificazione a intervalli, gestione delle normali e alberi booleani annidati.
- [**HeightField**](./docs/technical/heightfield.md) - Primitivo terreno con intersezione analitica senza tassellatura: patch bilineari, accelerazione min/max mipmap, caricamento da PNG-16 o sintesi procedurale.
- [**Motion Blur**](./docs/technical/motion-blur.md) - Implementazione del motion blur su trasformazioni: time-sampled TRS, interpolazione quaternionica, invariante bit-identico quando nulla è animato.
- [**Denoising**](./docs/technical/denoising.md) - Architettura del denoiser feature-guided: buffer AOV catturati in parallelo al render, algoritmi NLM e NFOR con selezione per-pixel del candidato a minore errore stimato.
- [**MediumInterface - Per-Entity Participating Media**](./docs/technical/medium-interface.md) - Assegnazione di media partecipanti per-entità: ownership model, stack semantics per raggi in volumi annidati e transizioni al boundary.
- [**Subsurface Scattering**](./docs/technical/subsurface-scattering.md) - Integrazione volumetrica random walk per SSS: derivazione, dispatching su eventi di rifrazione, confronto con l'approssimazione dipolo.
- [**Benchmark (`RayTracer.Benchmarks`)**](./docs/technical/benchmarks.md) - Harness BenchmarkDotNet per AABB e BVH: esecuzione, output, aggiunta di nuovi benchmark.
- [**Testing (`RayTracer.Tests`)**](./docs/technical/testing.md) - Suite xUnit: test di equivalenza BVH ↔ HittableList, differenziali AABB, pattern riusabili.

---

## 🤖 Collaborazione AI

Questo progetto è stato sviluppato con il supporto di tecnologie di Intelligenza Artificiale agentica e modelli di linguaggio avanzati:

![Antigravity](https://img.shields.io/badge/Developed%20with-Antigravity-9B51E0?logo=google&logoColor=white)
![Claude AI](https://img.shields.io/badge/Assist-Claude%20AI-D17051?logo=anthropic&logoColor=white)
![GitHub Copilot](https://img.shields.io/badge/Assist-GitHub%20Copilot-000000?logo=githubcopilot&logoColor=white)

---

## 📄 Licenza

Questo progetto è distribuito sotto licenza **MIT**. Consulta il file [LICENSE](LICENSE) per i dettagli.
