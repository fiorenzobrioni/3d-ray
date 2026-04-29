# 🛠️ DEVLOG — RayForge

Documento di lavoro per il progetto **RayForge**, l'editor visuale di scene per il motore [3D-Ray](DEVLOG.md).

> **Convenzione stati:** `✅ Completato` · `🔧 In corso` · `⬜ Da fare` · `⏸️ Sospeso`

> Documento gemello di [`DEVLOG.md`](DEVLOG.md) (motore RayTracer). Da consultare e aggiornare a ogni sessione di lavoro per verificare lo stato di avanzamento.

---

## RayForge

**RayForge** è l'editor visuale di scene per il motore di rendering **3D-Ray** (`RayTracer`). È un'applicazione desktop **Windows-only**, scritta in **C# / .NET 10**, pensata come *scene assembler*: un ambiente in cui l'utente costruisce, modifica e visualizza in **wireframe** scene composte di primitive analitiche, gruppi gerarchici, operazioni CSG, materiali, luci, camere e volumetrici, e le serializza nel formato YAML 3D-Ray. Non è un modeler poligonale: non gestisce edit di vertici/edge/face, sculpting, retopology, UV unwrap o animazione. Per geometrie custom si importano mesh `.obj`/`.gltf` prodotte da DCC esterni (Blender, Maya, ecc.).

**Filosofia del prodotto.** Lo scope è ispirato a tool come Katana (lookdev/lighting/scene assembly) e USD composer, non a Blender o Maya. L'utente costruisce la scena attraverso primitive parametriche, gerarchie e materiali, vede il risultato in wireframe nel viewport interattivo, e quando vuole un preview fotorealistico invoca il path tracer 3D-Ray direttamente dall'editor con i profili di qualità Preview/Standard/Final già definiti dal motore.

**Filosofia tecnica.** L'editor è un livello sopra il motore, non un fork: referenzia `RayTracer.csproj` come dipendenza. La scena editata vive in un *Document Model* mutabile separato dai tipi runtime del motore (immutabili e ottimizzati per il rendering); il document si serializza/deserializza in YAML 3D-Ray e si "compila" nei tipi engine al momento del render preview. Questo disaccoppia ciclo di edit e ciclo di rendering, abilita undo/redo pulito e libera l'engine da esigenze di mutabilità che lo rallenterebbero.

**Stack.**
- **Linguaggio / runtime**: C# 13, .NET 10, target `net10.0-windows`, x64.
- **Render API viewport**: Direct3D 11 via [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows). D3D11 è sufficiente per wireframe e UI; D3D12 sarebbe overkill.
- **UI**: [Hexa.NET.ImGui](https://github.com/HexaEngine/Hexa.NET.ImGui) (binding moderno e attivamente mantenuto di Dear ImGui per .NET) con docking branch nativo. Include ImGuizmo per i gizmo di trasformazione.
- **Windowing**: Win32 puro via P/Invoke (no WinForms / WPF / WinUI). ImGui occupa l'intera finestra; un layer applicativo aggiuntivo darebbe solo overhead.
- **YAML**: `YamlDotNet` (la stessa versione usata dal motore).
- **File dialogs**: `NativeFileDialogExtended` (da aggiungere quando serve, Phase 2).

**Layout di default.** Single-3D viewport massimizzato + Outliner + Inspector come panel dockable. Nessun layout hardcoded: l'utente può salvare layout personalizzati. Il classico **quad view** (Top / Front / Side / Persp) è offerto come *layout preset* selezionabile, non come default.

**Roadmap d'insieme.** 7 fasi, ~10–12 settimane part-time per arrivare a v1 utile. Si parte dalle fondamenta (Phase 0: window + D3D11 + ImGui dockspace), si costruisce un viewport wireframe interattivo (Phase 1), si aggiungono document model + outliner + I/O YAML (Phase 2), inspector + gizmo (Phase 3), luci e camere (Phase 4), materiali (Phase 5), integrazione render del motore (Phase 6), e infine polish (Phase 7).

---

## 📌 Note e Appunti

- **Ambiente di sviluppo**: il target è `net10.0-windows`, quindi build & run vanno fatti su Windows. La prima verifica della build su una macchina Windows è la **prima azione di Phase 0** — alcuni nomi di simboli dei backend Hexa.NET.ImGui (es. `ImGuiImplWin32.WndProcHandler`) potrebbero richiedere micro-aggiustamenti rispetto a quanto scritto nello skeleton.
- **Document Model vs tipi engine**: tenerli **separati** dal giorno 1. Tentazione da evitare: editare direttamente `Sphere`, `BvhNode`, ecc. Risultato sarebbe un accoppiamento patologico e undo/redo difficile.
- **YAML save flat in v1**: il `SceneLoader` engine risolve `import:`, `templates:`, `mix` references. Re-serializzare preservando questi costrutti è un grosso lavoro a parte. v1 salva *flat* (templates espansi, import inlinati). Documentare bene questa limitazione nella UI ("Save as flat YAML"). v2+ introduce un writer structure-preserving.
- **Undo/Redo dal day 1**: command pattern per ogni mutazione del Document. Aggiungerlo dopo costa caro.
- **Threading**: render preview SEMPRE su background thread. ImGui è UI thread only.
- **CSG e quartic in viewport**: per v1, mostrare bounding box wireframe per nodi CSG complessi e per superfici quartiche, non tessellazione approssimata. Decidere in Phase 1 se vale la pena tessellare le quartiche.
- **DPI awareness**: dichiarare l'app per-monitor DPI aware (manifest o `SetProcessDpiAwarenessContext`). ImGui ha `FontGlobalScale` per gestire scaling.
- **Differenziazione naming DEVLOG**: il file motore è `DEVLOG.md`. Quando comodo, valutare rename a `DEVLOG-RAYTRACER.md` per uniformità con `DEVLOG-RAYFORGE.md`.

---

## 🗺️ Roadmap

> Ogni fase elenca task atomici che diventano `✅` quando completati. Aggiornare ad ogni commit significativo.

---

### Fase 0 — Skeleton: Window + D3D11 + ImGui Dockspace

> **Obiettivo**: l'app si apre su Windows, mostra un dockspace fullscreen con menu bar, ImGui demo window dockabile, swap chain D3D11 funzionante, resize che gestisce correttamente i back buffer.

| Task | Stato |
|------|-------|
| Skeleton progetto: `src/RayForge/RayForge.csproj` (net10.0-windows, WinExe, x64) | ✅ |
| Aggiunta a `3d-ray.slnx` nella nuova folder `/RayForge/` | ✅ |
| Reference a `RayTracer.csproj` (per accesso ai tipi engine in fasi successive) | ✅ |
| Skeleton sorgenti: `Program.cs`, `Application`, `Win32Native`, `Win32Window`, `GraphicsDevice`, `ImGuiHost`, `MainDockspace` | ✅ |
| Verifica build su Windows e fix incrementali ai bindings Hexa.NET.ImGui (esatti nomi simboli) | ⬜ |
| App si apre con finestra Win32 + swap chain D3D11 + clear color visibile | ⬜ |
| ImGui context inizializzato con `DockingEnable` + Win32 backend + D3D11 backend | ⬜ |
| Dockspace fullscreen + main menu bar (File / Edit / Add / View / Render) | ⬜ |
| ImGui demo window dockabile e visibile, toggle da menu View | ⬜ |
| WM_SIZE → resize swap chain corretto, no glitch al resize | ⬜ |
| Manifest per-monitor DPI awareness | ⬜ |
| Verifica chiusura pulita: shutdown ImGui, dispose D3D11, no warning debug layer | ⬜ |
| Icona applicazione (placeholder) | ⬜ |

---

### Fase 1 — Viewport Wireframe

> **Obiettivo**: viewport 3D interattivo con camera orbit/pan/dolly, primitive renderizzate in wireframe, grid floor e gizmo assi.

| Task | Stato |
|------|-------|
| `Viewport` panel ImGui con render target D3D11 dedicato (texture + RTV + SRV per ImGui::Image) | ⬜ |
| Pipeline wireframe: vertex shader trasformazione + pixel shader uniform, line list topology, depth test | ⬜ |
| Camera orbit (Alt+LMB), pan (Alt+MMB), dolly (Alt+RMB / scroll) — modello stile Blender | ⬜ |
| Frustum + matrici View/Proj con near/far adattivi alla scena | ⬜ |
| Mesh procedurali per **Sphere** (UV sphere wireframe) | ⬜ |
| Mesh procedurali per **Box** (12 edges) | ⬜ |
| Mesh procedurali per **Plane** finito (4 edges + cross interno opzionale) | ⬜ |
| Mesh procedurali per **Cylinder** (cap circles + side lines) | ⬜ |
| Mesh procedurali per **Cone**, **Capsule**, **Disk**, **Annulus** (le primitive engine già esistenti) | ⬜ |
| Mesh procedurali per **Torus** | ⬜ |
| Mesh procedurali per **Triangle**, **Quad** | ⬜ |
| Visualizzazione **InfinitePlane** come grid finita centrata sulla camera | ⬜ |
| Visualizzazione **CSG** e **Quartic** come AABB wireframe (semplificazione v1) | ⬜ |
| Grid floor (XZ) con linee maggiori/minori | ⬜ |
| Axis gizmo (X rosso, Y verde, Z blu) overlay corner | ⬜ |
| Stato visivo: oggetto default / hovered / selected (color highlight) | ⬜ |
| Picking: click su entità → selezione (ray-AABB test, sufficiente per v1) | ⬜ |
| FPS counter + statistiche (entità visibili, draw call) in overlay | ⬜ |
| **Milestone**: scena hardcoded con 5 primitive visibili in wireframe, camera orbita, picking funziona | ⬜ |

---

### Fase 2 — Document Model + Outliner + I/O YAML

> **Obiettivo**: rappresentazione mutabile della scena, panel Outliner, apertura/salvataggio YAML, undo/redo via command pattern.

| Task | Stato |
|------|-------|
| `RayForge.Document` namespace: `SceneDoc`, `EntityDoc`, `LightDoc`, `CameraDoc`, `MaterialDoc`, `WorldSettingsDoc` | ⬜ |
| Sotto-tipi per ogni primitiva: `SphereDoc`, `BoxDoc`, `PlaneDoc`, ..., `GroupDoc`, `CsgDoc`, `TransformDoc` | ⬜ |
| YAML Reader (Phase 2 v1): parse diretto via `YamlDotNet` su Document mirror, no `SceneLoader` | ⬜ |
| YAML Writer flat (templates espansi, import inlinati) | ⬜ |
| Round-trip test: load → save → reload → confronto strutturale | ⬜ |
| Command pattern: `ICommand` interface + `CommandStack` con undo/redo, dirty-flag | ⬜ |
| Comandi base: `AddEntity`, `RemoveEntity`, `RenameEntity`, `ReparentEntity`, `EditProperty<T>` | ⬜ |
| Outliner panel: ImGui tree, espansione, selezione (singola/multipla con Ctrl/Shift) | ⬜ |
| Outliner: rinomina inline (double-click), drag-drop reorder e reparent | ⬜ |
| Outliner: context menu (delete, duplicate, group selection, ungroup) | ⬜ |
| File menu: New, Open, Save, Save As — wired a YAML reader/writer | ⬜ |
| Recent files list (max 8) persistita in `%APPDATA%/RayForge/recent.json` | ⬜ |
| Dirty flag → asterisco nel titolo finestra, conferma su New/Open/Exit se dirty | ⬜ |
| Shortcuts: Ctrl+N, Ctrl+O, Ctrl+S, Ctrl+Shift+S, Ctrl+Z, Ctrl+Y | ⬜ |
| **Milestone**: apri `scenes/chess.yaml`, vedi entità nell'outliner, salva e ricarica identico | ⬜ |

---

### Fase 3 — Inspector + Gizmo Trasformazioni

> **Obiettivo**: editing live dei parametri delle entità con gizmo nel viewport e property grid contestuale.

| Task | Stato |
|------|-------|
| Inspector panel contestuale alla selezione (singola entità per v1) | ⬜ |
| Property editor per `Vector3` (3 drag-float) | ⬜ |
| Property editor per `float`, `int`, `bool`, `string`, `enum` | ⬜ |
| Property editor per `Color3` (color picker ImGui) | ⬜ |
| Property editor per riferimenti a materiale (dropdown da MaterialBrowser) | ⬜ |
| Inspector → emette `EditPropertyCommand` su ogni change (con coalescing per drag continui) | ⬜ |
| Sezione Transform: position / rotation (Euler) / scale | ⬜ |
| Sezione Primitive params: raggi, dimensioni, ecc. — uno per primitive type | ⬜ |
| Gizmo translate (W) tramite ImGuizmo nel viewport | ⬜ |
| Gizmo rotate (E) | ⬜ |
| Gizmo scale (R) | ⬜ |
| Snap toggle (Ctrl mentre si trascina) con valori configurabili | ⬜ |
| Add menu: crea entità con valori sensati e seleziona la nuova entità | ⬜ |
| Group / Ungroup: comando dedicato (selezione → child di un nuovo `Group`) | ⬜ |
| CSG node creation: drag-drop di entità in `Union` / `Intersection` / `Subtraction` | ⬜ |
| **Milestone**: costruisci scena da zero con 5 oggetti, salva, riapri, render manuale OK | ⬜ |

---

### Fase 4 — Luci e Camere

> **Obiettivo**: gestione di tutte le luci e camere con visualizzazione viewport.

| Task | Stato |
|------|-------|
| `LightDoc` per Point, Directional, Spot, Area, Sphere | ⬜ |
| Helper visivo per Point: icona billboard + sfera wireframe range | ⬜ |
| Helper visivo per Directional: freccia + disco angolare (`AngularRadiusDeg`) | ⬜ |
| Helper visivo per Spot: cono wireframe, icona | ⬜ |
| Helper visivo per Area: rettangolo wireframe + normale | ⬜ |
| Helper visivo per Sphere light: sfera wireframe colorata per emissione | ⬜ |
| Inspector per parametri luce: color, intensity, falloff/soft-radius, shadow-samples, ecc. | ⬜ |
| `CameraDoc` con tutti i parametri engine (eye, lookAt, up, fov, aperture, focal_dist) | ⬜ |
| Multi-camera: lista camere, "Active Camera" del Document | ⬜ |
| **Look Through Camera** mode: viewport usa la camera selezionata | ⬜ |
| Frustum overlay per camere non attive (line wireframe) | ⬜ |
| Camera bookmark / navigazione rapida (NumPad?) | ⬜ |
| **Milestone**: scena multi-camera con luci miste, switch viewport tra camere | ⬜ |

---

### Fase 5 — Materiali

> **Obiettivo**: gestione completa della libreria materiali, assegnazione, preview.

| Task | Stato |
|------|-------|
| `MaterialDoc` per Lambertian, Metal, Dielectric, Emissive, Mix, Blend, Disney BSDF | ⬜ |
| Material Browser panel: lista materiali del Document, thumbnail, rinomina, delete | ⬜ |
| Inspector materiale: editor per ogni tipo, parametri completi | ⬜ |
| Assegnazione materiale a entità: dropdown nell'inspector entità + drag-drop dal browser | ⬜ |
| Preview ball: shading fake (Blinn-Phong custom) sulla sfera 64×64 in tempo reale | ⬜ |
| Materiali con texture references: file picker + thumbnail | ⬜ |
| Mix/Blend: selettore di materiale figlio con dropdown a cascata | ⬜ |
| Validazione: cicli mix/blend rilevati e segnalati | ⬜ |
| **Milestone**: cambi materiale a un'entità, render preview riflette | ⬜ |

---

### Fase 6 — Render Preview Integrato

> **Obiettivo**: invocare il motore 3D-Ray sulla scena editata, mostrare il risultato dentro RayForge.

| Task | Stato |
|------|-------|
| `RenderJob` async: serializza Document → YAML temporaneo → `SceneLoader.Load()` → `Renderer.Render()` su background thread | ⬜ |
| Render View panel con immagine D3D11 (texture aggiornata da CPU buffer) | ⬜ |
| Aggiornamento progressivo: scanline o tile (in base a quanto offre il `Renderer` engine) | ⬜ |
| Cancel button: fermata pulita del rendering | ⬜ |
| Selettore profilo: Preview / Standard / Final (vedi `docs/reference/rendering-profiles.md`) | ⬜ |
| Override CLI-equivalenti: `-w`, `-H`, `-s`, `-d`, `-S`, `-C`, `--light-sampling`, ecc. | ⬜ |
| Save image (PNG/JPG/BMP) | ⬜ |
| Stats panel: tempo, samples completati, ETA stimato | ⬜ |
| **Milestone**: scena costruita interamente in RayForge → render PNG salvabile | ⬜ |

---

### Fase 7 — Polish

> **Obiettivo**: rifinire UX, layout, robustezza. Lavoro continuo, post-v1.

| Task | Stato |
|------|-------|
| Layout preset **Quad View** (Top / Front / Side / Persp) salvato | ⬜ |
| Layout preset **Lighting** (viewport grande + outliner + light list) | ⬜ |
| Layout preset **Lookdev** (viewport + render view + material browser) | ⬜ |
| Salvataggio/caricamento layout custom utente | ⬜ |
| Volumetrici editing: medium globale + per-object (Homogeneous, HeightFog, Procedural, Grid) | ⬜ |
| Templates editor + instancing UI dedicata (Phase 7 estesa) | ⬜ |
| Import OBJ/glTF (richiede prima il loader engine-side se non già presente) | ⬜ |
| Autosave + crash recovery | ⬜ |
| Settings persistenti (`%APPDATA%/RayForge/settings.json`) | ⬜ |
| Theme dark/light + font scaling | ⬜ |
| Keyboard shortcuts customizzabili | ⬜ |
| About dialog con versione, link DEVLOG, attribuzioni licenze | ⬜ |
| Installer / portable zip release | ⬜ |

---

## 📝 TODO

> Lista volatile di lavori puntuali fuori roadmap o da indirizzare presto. Da sfoltire spesso.

- [ ] **PRIMO PASSO**: clonare il repo su una macchina Windows ed eseguire `dotnet build src/RayForge/RayForge.csproj -c Release` per verificare la build dello skeleton. Iterare sui simboli dei backend Hexa.NET.ImGui se necessario.
- [ ] Verificare versioni esatte dei pacchetti Hexa.NET.ImGui disponibili su NuGet al momento della build (lo skeleton usa 2.2.7 come placeholder).
- [ ] Decidere se aggiungere un manifest `app.manifest` per DPI awareness o usare API call in startup (`SetProcessDpiAwarenessContext`).
- [ ] Decidere se RayForge debba avere una sezione dedicata nel README principale o un `README.md` proprio in `src/RayForge/`.
- [ ] Eventuale rename di `DEVLOG.md` → `DEVLOG-RAYTRACER.md` per uniformità (decisione separata, basso impatto).
- [ ] Aggiungere una scena di test minima (`scenes/rayforge-smoke.yaml`) da usare come default per smoke test viewport.

---

## 📚 Riferimenti Tecnici

### Stack RayForge

- **Vortice.Windows** — bindings .NET moderni per Direct3D 11/12, DXGI, D2D, WIC, Mathematics: <https://github.com/amerkoleci/Vortice.Windows>
- **Hexa.NET.ImGui** — bindings .NET attivi e ben mantenuti di Dear ImGui (con docking branch incluso): <https://github.com/HexaEngine/Hexa.NET.ImGui>
- **Dear ImGui** — la libreria UI sottostante: <https://github.com/ocornut/imgui>
- **ImGuizmo** — gizmo translate/rotate/scale per ImGui: <https://github.com/CedricGuillemet/ImGuizmo>
- **YamlDotNet** — parser/emitter YAML per .NET: <https://github.com/aaubry/YamlDotNet>
- **NativeFileDialogExtended** — file dialogs nativi cross-platform (estensione di nativefiledialog): <https://github.com/btzy/nativefiledialog-extended>

### Riferimenti Direct3D 11 / DXGI

- **Programming Guide for Direct3D 11** (Microsoft Learn): <https://learn.microsoft.com/windows/win32/direct3d11/dx-graphics-overviews>
- **DXGI overview**: <https://learn.microsoft.com/windows/win32/direct3ddxgi/dxgi-overview>
- **Flip model swap chains** (necessario per FLIP_DISCARD usato da RayForge): <https://learn.microsoft.com/windows/win32/direct3ddxgi/dxgi-flip-model>

### Riferimenti pattern editor / scene assembler

- **Katana (Foundry)** — il riferimento concettuale per lo scope "scene assembler / lookdev / lighting": <https://www.foundry.com/products/katana>
- **USD (OpenUSD)** — schema scene description di riferimento per il futuro: <https://openusd.org/>

### Documentazione interna 3D-Ray

- [`DEVLOG.md`](DEVLOG.md) — DEVLOG del motore RayTracer.
- [`docs/reference/`](docs/reference/) — schema YAML completo + profili di rendering Preview/Standard/Final.
- [`docs/technical/rendering-pipeline.md`](docs/technical/rendering-pipeline.md) — pipeline YAML → pixel.
- [`docs/technical/path-tracing-and-lighting.md`](docs/technical/path-tracing-and-lighting.md)
- [`docs/technical/shading-model.md`](docs/technical/shading-model.md)
- [`docs/technical/csg-boolean-operations.md`](docs/technical/csg-boolean-operations.md)
- [`CLAUDE.md`](CLAUDE.md) — istruzioni per agenti AI sul repo, contiene mappa architetturale.
