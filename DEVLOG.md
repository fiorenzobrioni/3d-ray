# 📋 DEVLOG — 3D-Ray Development Log

Documento di lavoro per roadmap, attività, bug noti e note di sviluppo.

> **Convenzione stati:** `✅ Completato` · `🔧 In corso` · `⬜ Da fare`

---

## 📌 Note & Appunti Rapidi

- Aggiornare i reference e i tutorial ogni volta che si aggiunge una nuova primitiva o una feature.
- Creare 2 scene starter-kit con effetti volumetrici/nebbia e aggiornare i README di `starter-kits` e `libraries`.
- Revisione app **ChessGen**:
  - Riallineare il codice per generare `chess.yaml` nel formato attuale.
  - Modificare l'app affinché generi un `template` per ogni pedina e usi le `instance` sulla scacchiera (più ordinato e compatto).
- Valutare parametro CLI per **log verboso**: togliere dal log di default info di dettaglio (es: children counts, mesh stats) per una lettura più pulita.
- Idee per scene creative:
  - **Macro Photography**: Primo piano estremo di un orologio meccanico (usando `Annulus` e `Cylinder`) con DOF molto spinta.

---

## 🗺️ Roadmap

La roadmap è divisa in due parti: **Fase 0** copre le fondamenta del motore (già implementate prima della pianificazione delle fasi successive); le **Fasi 1–5** coprono le feature sviluppate o pianificate in modo incrementale.

---

### Fase 0 — Fondamenta del Motore ✅

> Tutto quello che esisteva prima che la roadmap per fasi venisse definita.

| Feature | Stato |
|---------|-------|
| Path Tracer multi-bounce con profondità configurabile | ✅ |
| Rendering parallelo multi-core (`Parallel.For`) | ✅ |
| BVH (Bounding Volume Hierarchy) con SAH binning, fat leaves, ordered traversal e build parallelo | ✅ |
| Camera con thin lens model e Depth of Field (`aperture`, `focal_dist`) | ✅ |
| Multi-Camera — lista `cameras:` con selezione da CLI (`--camera`) | ✅ |
| Primitivi: Sphere, Box, Cylinder, Triangle, Quad, InfinitePlane | ✅ |
| Primitivi: Cone (tronco), Capsule, Disk, Annulus, SmoothTriangle | ✅ |
| Sistema di Trasformazioni — Scale, Rotate, Translate su qualsiasi primitiva | ✅ |
| Materiali: Lambertian, Metal, Dielectric | ✅ |
| Luci: Point, Directional, Spot, Area (con soft shadows e stratified sampling) | ✅ |
| Next Event Estimation (NEE) — campionamento diretto di tutte le luci | ✅ |
| Russian Roulette adattiva — calibrata sul tipo di illuminazione della scena | ✅ |
| Campionamento Stratificato `√N × √N` per pixel e per area light | ✅ |
| ACES Filmic Tone Mapping + Gamma 2.2 + Firefly Guard | ✅ |
| YAML Scene Loader con validazione e fallback | ✅ |
| Seed deterministico per texture procedurali per-oggetto | ✅ |
| Output PNG / JPEG / BMP con rilevamento automatico dall'estensione | ✅ |
| CI con smoke test (GitHub Actions) | ✅ |

---

### Fase 1 — Fondamenta visive ✅

> Obiettivo: trasformare il motore da progetto educativo a renderer presentabile, con il massimo impatto visivo e il minimo rischio architetturale.

| # | Feature | Stato |
|---|---------|-------|
| 1 | Emissive Material | ✅ Completato |
| 2 | Gradient Sky | ✅ Completato |
| 3 | Image Textures (PNG/JPG/BMP/TIFF/WebP) | ✅ Completato |
| 4 | IBL / HDRI | ✅ Completato |
| 5 | Normal Mapping | ✅ Completato |

**1. Emissive Material ✅** — Superfici auto-illuminanti. Gli oggetti emissivi con geometria campionabile (Sphere, Box, Cylinder, Cone, Torus, Capsule, Annulus, Mesh, SmoothTriangle, Quad, Triangle, Disk) partecipano automaticamente alla NEE come GeometryLight.

**2. Gradient Sky ✅** — Cielo procedurale con gradiente verticale a 3 bande (zenith, orizzonte, terreno) e sun disk con glow halo configurabile. Partecipa alla NEE come EnvironmentLight.

**3. Image Textures ✅** — Caricamento texture da file (PNG, JPEG, BMP, GIF, TIFF, WebP) con bilinear filtering e tiling configurabile via ImageSharp.

**4. IBL / HDRI ✅** — Environment map in formato Radiance `.hdr` con importance sampling via CDF 2D (marginal + conditional) per NEE efficiente. Supporta rotazione Y-axis e moltiplicatore di intensità. Partecipa alla NEE come EnvironmentLight.

**5. Normal Mapping ✅** — Perturbazione normali tramite texture nello spazio TBN (Tangent-Bitangent-Normal) con ortogonalizzazione di Gram-Schmidt. Compatibile OpenGL e DirectX-style (`flip_y`).

---

### Fase 2 — Materiali e geometria professionali 🔄

> Obiettivo: sistema PBR completo, geometria da file, strumenti di composizione avanzati.

| # | Feature | Stato |
|---|---------|-------|
| 6 | Disney BSDF / PBR | ✅ Completato |
| 7 | OBJ Mesh Loader | ✅ Completato |
| 8 | Torus Primitive | ✅ Completato |
| 9 | Mix Material | ✅ Completato |
| 10 | Sphere Light | ✅ Completato |
| 11 | Scene Graph / Groups | ✅ Completato |

**6. Disney BSDF / PBR ✅** — Materiale unificato con sampling stocastico multi-lobo:
- **Lobi base**: diffuse, specular GGX, clearcoat, transmission.
- **Multi-scattering Kulla-Conty** — recupero energia dispersa dal singolo bounce GGX (LUT E(μ, α) 32×32 lazy-initialised con guardia anti-deadlock sul thread pool).
- **Anisotropia GGX** (`anisotropic`, `anisotropic_rotation`) con VNDF sampling e shading frame tangente-aware.
- **Beer-Lambert per la trasmissione** (`transmission_color`, `transmission_depth`) — assorbimento volumetrico all'interno del vetro via medium-switch su `BsdfSample.NextSegmentAbsorption`.
- **Disney 2015: thin_walled, diff_trans, flatness, subsurface_color** — lobo di diffuse transmission (foglie, fogli, tendaggi), blend "HK flat" e tinte sotto-pelle indipendenti dal `base_color`.
- **Clearcoat stile Arnold** (`coat_ior`, `coat_roughness`, `coat_normal`) — lobo di coat con IOR esplicita e normal map dedicata (fallback al classico `clearcoat_gloss` di Disney 2012 quando l'artista non forza la roughness).
- **Charlie sheen** (`sheen_roughness`) — fitting Estevez-Kulla 2017 (NDF invertita + Λ polinomiale) che rimpiazza lo Schlick approssimato.
- **Thin-film iridescence** (`thin_film_thickness`, `thin_film_ior`) — Belcour-Barla 2017 per bolle di sapone, opal, AR coating.
- Pesi di lobo calibrati e coerenti tra `Sample` / `Evaluate` / `Pdf` per un MIS corretto (furnace / reciprocity test nella suite `DisneyBsdfTests`).

**7. OBJ Mesh Loader ✅** — Parser Wavefront OBJ con smooth normals (interpolazione Phong), artist UV, TBN da gradiente UV per normal mapping, BVH interno dedicato. Supporta `v/vt/vn`, indici negativi, quad auto-triangolati. Alias YAML: `"mesh"`, `"obj"`.

**8. Torus Primitive ✅** — Intersezione analitica via risolutore di quartiche (metodo di Ferrari) in `QuarticSolver`. La direzione del raggio viene normalizzata prima del calcolo dei coefficienti per garantire c₄ = 1 e un condizionamento ottimale della quartica indipendentemente dal focal_dist della camera o da scale Transform. Le radici vengono validate contro l'equazione implicita del toro per scartare falsi positivi. UV toroidale, `ISamplable` per NEE, compatibile CSG e Transform. Alias YAML: `"torus"`, `"donut"`, `"ring"`.

**9. Mix Material ✅** — Materiale composito che interpola tra due materiali figli con peso costante o texture mask spaziale. Selezione stocastica dei lobi per lo scatter (unbiased, compatibile con qualsiasi combinazione di materiali), media pesata deterministica per EvaluateDirect (bassa varianza NEE), blend pesato per emissione. Mask: qualsiasi tipo di texture (noise, marble, wood, checker, image). Luminanza Rec.709 per conversione RGB→scalare. Supporto mix-of-mix tramite risoluzione iterativa delle dipendenze nel loader. Alias YAML: `"mix"`, `"blend"`. Scena di test: `mix-material-showcase.yaml`.

**10. Sphere Light ✅** — Luce sferica dedicata con solid-angle sampling sulla porzione visibile (PBRT §6.2.3). Campiona direzioni uniformemente nel cono sotteso dalla sfera: cos(θ) = 1 − ξ₁(1 − cos(θ_max)), φ = 2πξ₂. Zero campioni sprecati sulla faccia posteriore, varianza 1/Ω vs 1/r² del GeometryLight equivalente — 2–10× più efficiente per sfere piccole/distanti. Stratificazione √N × √N nello spazio (cos θ, φ) per penombra a basso rumore. Intersezione analitica raggio-sfera per punto esatto sulla superficie. Caso degenere (punto interno alla sfera) gestito con Ω = 4π. Alias YAML: `"sphere"`, `"sphere_light"`, `"ball"`, `"ball_light"`. Scena di test: `sphere-light-showcase.yaml`.

**11. Scene Graph / Groups ✅** — Raggruppamento gerarchico con trasformazioni ereditate. Nuova classe `Group : IHittable` con BVH interno, lista figli tipizzata, seed propagation deterministica. Composizione arbitraria: primitive, CSG, mesh OBJ e gruppi annidati a profondità illimitata. Materiale fallback ereditabile dai figli. Extraction ricorsiva delle geometry lights per NEE (gestisce Transform-wrapped Groups con composizione corretta delle matrici). **Template/Instance system:** sezione `templates:` per definire oggetti composti come blueprint riutilizzabili, tipo `"instance"` per istanziarli con transform e materiale indipendenti. I template supportano trasformazioni come "posa di default" che compone con quella dell'istanza (`child → template → instance`). Override materiale per-istanza. Template importabili da file YAML esterni per librerie di oggetti. **Import YAML:** sistema `imports:` per scomporre scene complesse in file separati — librerie di materiali, template, entità e luci riutilizzabili. Import annidati con protezione ciclica. Merge semantics: materiali/template locali override importati (last-write-wins), entità e luci prepended. World/camera non importati. Alias YAML: `"group"`, `"instance"`. Scene di test: `group-showcase.yaml`.

---

### Fase 3 — Convergenza e sampling avanzato 🔄

> Obiettivo: migliorare la qualità del campionamento e ridurre i tempi di rendering.

| # | Feature | Stato |
|---|---------|-------|
| 12 | Importance Sampling | ✅ Completato |
| 13 | Multi-Importance Sampling | ✅ Completato |
| 14 | Adaptive Sampling | ⬜ Da fare |
| 15 | Tile-based Rendering | ⬜ Da fare |
| 16 | Denoiser | ⬜ Da fare |
| 17 | HDR Output (EXR/PFM) | ⬜ Da fare |
| +  | Sobol + Owen Scrambling Sampler | ✅ Completato |

**12. Importance Sampling ✅** — GGX importance sampling in `Metal` e `DisneyBsdf` (specular, clearcoat, transmission). Environment map importance sampling via CDF 2D. Il diffuse usa cosine-weighted sampling by construction.

**13. Multi-Importance Sampling ✅** — Copertura MIS completa su superfici, environment e volumetrica. Tutti i materiali rilevanti (`DisneyBsdf`, `Lambertian`, `Metal`, `MixMaterial`) implementano la tripla `Sample`/`Pdf`/`Evaluate`; `Dielectric` resta intenzionalmente delta. La phase function partecipa al MIS volumetrico tramite `IPhaseFunction.Pdf(wo,wi)`. Heuristiche selezionabili `--mis <balance|power>` (default balance). Per il contratto, le formule e i casi limite vedi [`docs/technical/multiple-importance-sampling.md`](docs/technical/multiple-importance-sampling.md).

**Sampler Sobol + Owen Scrambling ✅** — Sequenza quasi-Monte Carlo a bassa discrepanza (tabelle Joe-Kuo, scrambling hash-based di Burley 2020) selezionabile con `--sampler sobol` (default: `prng` per retro-compatibilità). Riduce varianza nel campionamento di pixel, lens, AA e dei primi bounce rispetto al PRNG; senza `--sampler sobol` il renderer è bit-identico al comportamento precedente.

**14. Adaptive Sampling ⬜** — Campionamento per pixel basato sulla varianza. Pixel convergenti terminano in anticipo. Dipende da: #15.

**15. Tile-based Rendering ⬜** — Sostituzione di `Parallel.For` su righe con sistema a tile (es. 32×32). Benefici: cache locality, preview progressivo, prerequisito per adaptive sampling e denoiser.

**16. Denoiser ⬜** — Filtro post-processo (bilateral o NLMeans) guidato da buffer ausiliari (normal, albedo, depth). Dipende da: #15.

**17. HDR Output ⬜** — Salvataggio buffer lineare pre-tone-mapping in formato PFM o EXR.

---

### Fase 4 — Effetti cinematografici ⬜

> Obiettivo: effetti di rendering avanzati per qualità cinematografica.

| # | Feature | Stato |
|---|---------|-------|
| 18 | Motion Blur | ⬜ Da fare |
| 19 | Volumetric Rendering | 🔧 In corso (Stage 1 + Stage 1.5 ✅) |
| 20 | Subsurface Scattering | ⬜ Da fare |
| 21 | CSG (Boolean Operations) | ✅ Completato |
| 22 | Instancing | ✅ Completato |

**18. Motion Blur ⬜** — Parametro temporale nel `Ray` con interpolazione posizioni.

**19. Volumetric Rendering 🔧** — Fog/fumo con Beer-Lambert e free-path sampling.

*Stage 1 ✅* — Fondamenta
Medium globale opt-in via world.medium (default null ⇒ output bit-identical su scene esistenti, verificato).
IMedium + HomogeneousMedium con Beer-Lambert + free-path sampling spectrally-aware (uniform channel pick, MIS-style pdf).
Fasi: IsotropicPhase (1/4π) e HenyeyGreensteinPhase (g ∈ [-0.999, 0.999]).
Renderer.TraceRay esteso: campiona evento volumetrico prima di shading; se scatter, NEE+phase+ricorsione; altrimenti surface path moltiplicato per Tr/pdf.
ComputeDirectLighting attenua ogni shadow ray con medium.Transmittance lungo la distanza alla luce.
MediumInterface placeholder per Stage 2 (boundary per-entità).
Scena `volumetric-01-homogeneous-showcase.yaml` (ex `volumetric-fog-showcase.yaml`) per validare god-rays da spotlight.

Note implementative: il free-path channel-pick uniforme è la scelta più semplice; un MIS spectrally-balanced verrà valutato se compaiono firefly cromatici. Il path bit-identical è garantito perché quando _globalMedium == null non viene consumato alcun random number aggiuntivo e il branch volumetrico è completamente bypassato.

*Stage 1.5 ✅* — Tipi aggiuntivi di medium e phase function (senza cambi architetturali)

Estensione del catalogo di volumetrics senza toccare `IMedium` / `IPhaseFunction` / `HitRecord` / `Renderer`. Tutti i nuovi tipi si agganciano via dispatch in `SceneLoader` e implementano le interfacce esistenti.

Nuovi `IMedium`:
- **HeightFogMedium** — densità esponenziale in altezza `σ_T(y) = σ_T0·exp(-(y - y0)/H)`, integrale lungo il raggio in forma chiusa (niente delta tracking). Analogo ad Arnold `atmosphere_volume` / V-Ray `EnvironmentFog` / `PxrAtmosphere`. Showcase: `volumetric-02-height-fog-showcase.yaml`.
- **HeterogeneousProceduralMedium** — densità da Perlin fBm (`PerlinNoise` utility interna, Ken Perlin improved 2002), delta tracking (Woodcock) per free-path + ratio tracking per transmittance. Analogo ad Arnold `standard_volume` + noise / PBRT `CloudMedium`. Showcase: `volumetric-03-procedural-showcase.yaml`.
- **GridMedium** — griglia 3D dentro AABB world-space, slab-clip + trilinear interpolation + delta tracking con majorant `σ_base_T · maxDensity`. Formato custom `.vol` (VOL1) o dati inline YAML. Analogo a PBRT `GridMedium` / Arnold `volume` (VDB) / V-Ray `VolumeGrid`. Showcase: `volumetric-04-grid-showcase.yaml`.

Nuove `IPhaseFunction`:
- **RayleighPhase** — `(3/16π)(1 + cos²θ)`, closed-form inverse-CDF sampling. Per atmosfere planetarie.
- **DoubleHenyeyGreensteinPhase** — combinazione `w·HG(g1) + (1-w)·HG(g2)`, selezione stocastica del lobo. Modello Nubis (Guerrilla) per cumuli.
- **SchlickPhase** — approssimazione razionale di HG senza `sqrt`, `k ≈ 1.55g − 0.55g³`. Fast-HG stile RenderMan / Cycles.

Estensione YAML: `medium.type ∈ {homogeneous, height_fog, procedural, grid}` (default `homogeneous`); `medium.phase ∈ {isotropic, hg, rayleigh, double_hg, schlick}`. Nessuna regressione: ogni path del `Renderer` non tocca i nuovi tipi.

File: `src/RayTracer/Volumetrics/{HeightFogMedium,HeterogeneousProceduralMedium,GridMedium,PerlinNoise,RayleighPhase,DoubleHenyeyGreensteinPhase,SchlickPhase}.cs`. Dispatch esteso in `SceneLoader.BuildGlobalMedium` / `BuildPhaseFunction`.

*Stage 2 ⬜* — Cambi architetturali (deferred)
Richiedono modifiche a `IMedium` / `HitRecord` / `Renderer.TraceRay` e perciò sono rinviati:
- EmissiveMedium / blackbody (estensione `IMedium` con `Emission`/`SampleEmission`, accumulo emissione nel path volumetrico).
- MediumInterface per-entity (stack di medium per transitions inside/outside, campo dedicato su `HitRecord`).
- SSS random-walk (dipende da MediumInterface per-entity).
- OpenVDB / NanoVDB nativo (P/Invoke, dipendenza pesante).
- Spectral tracking / null-scattering avanzato (refactor del path volumetrico).

**20. Subsurface Scattering ⬜** — BSSRDF o random-walk SSS per materiali traslucidi (pelle, cera, marmo). Il parametro `subsurface` del Disney BSDF è già presente come approssimazione flat.

**21. CSG ✅** — Operazioni booleane union, intersection, subtraction con algoritmo all-hits per correttezza su solidi non-convessi. Annidamento ricorsivo arbitrario, materiali per-figlio, compatibilità BVH (AABB tight per tipo di operazione) e Transform (Jacobian area-preserving). Normali invertite automaticamente sulla superficie tagliante della subtraction con propagazione corretta del frame TBN.

**22. Instancing ✅** — Copie efficienti con geometria condivisa e transform individuale. Il `SceneLoader` mantiene un `templateCache` (`Dictionary<string, IHittable>`) che costruisce ogni template **una sola volta** alla prima richiesta; ogni `type: instance` successivo riusa lo stesso riferimento, condividendo geometria, BVH e mesh — N istanze di una mesh pesante passano da O(N) a O(1) in memoria. La nuova classe `Instance : IHittable` avvolge il template condiviso e aggiunge per-istanza: (a) override di `rec.ObjectSeed` al ritorno di `Hit()` per dare seed indipendenti alle texture procedurali nonostante la geometria condivisa, (b) override opzionale di `rec.Material` quando l'istanza dichiara un proprio `material:`. **Semantica del material override**: se l'istanza specifica un materiale, *tutti* i figli del template usano quel materiale (uniforma anche figli con materiale esplicito); se l'istanza non lo specifica, i materiali per-figlio del template restano invariati — è la scelta intuitiva e non richiede flag/marker sui figli. Catena di trasformazioni: `child_local → template_transform → instance_transform`. **Limitazione accettata**: gli emissive interni a un template istanziato non vengono registrati come `GeometryLight` separati per istanza (NEE non li considera; restano visibili tramite BSDF sampling). Una scena con centinaia di luci istanziate richiederebbe per-instance light registration con composizione di transform — complicazione non giustificata per casi d'uso reali. Dipende da: #7, #11.

---

### Fase 5 — Frontiera (ricerca) ⬜

> Feature ad alto costo implementativo, riservate a esigenze specifiche o interesse accademico.

| # | Feature | Stato |
|---|---------|-------|
| 23 | Bidirectional Path Tracing | ⬜ Da fare |
| 24 | Spectral Rendering | ⬜ Da fare |
| 25 | Displacement Mapping | ⬜ Da fare |
| 26 | GPU Acceleration | ⬜ Da fare |

**23. Bidirectional Path Tracing ⬜** — Raggi da camera + luci, connessione sotto-path. Dipende da: #13.

**24. Spectral Rendering ⬜** — Lunghezze d'onda individuali per dispersione prismatica.

**25. Displacement Mapping ⬜** — Modifica geometria da height map con tessellation runtime. Dipende da: #7.

**26. GPU Acceleration ⬜** — Rewrite target a lungo termine (CUDA/Vulkan Compute). Praticamente un progetto separato.

---

### Dipendenze tra feature

```
#3 Image Textures ──► #5 Normal Mapping
                  ──► #9 Mix Material (maschere blend)
#6 Disney BSDF   ──► #20 SSS (parametro subsurface già presente)
#7 OBJ Loader    ──► #22 Instancing
                  ──► #25 Displacement Mapping
#11 Scene Graph  ──► #22 Instancing (parzialmente implementato come Templates)
#12 Importance S.──► #13 MIS ──► #23 Bidirectional PT
#15 Tile-based   ──► #14 Adaptive Sampling
                 ──► #16 Denoiser
```

---

## 🗓️ Ciclo di Review — Disney BSDF & Sampling (Apr 2026)

Branch `claude/review-disney-materials-QO2mO`. Snapshot delle feature
atterrate in questo ciclo. Per i dettagli matematici e i riferimenti
bibliografici, il riferimento canonico è
[`docs/technical/shading-model.md`](docs/technical/shading-model.md).

- MIS balance heuristic introdotta su `DisneyBsdf` (estesa a tutti i materiali
  nel ciclo successivo, vedi [MIS doc](docs/technical/multiple-importance-sampling.md)).
- Disney BSDF: parametri mancanti — `anisotropic`/`anisotropic_rotation`,
  `transmission_color`/`transmission_depth` (Beer-Lambert con medium-switch),
  `thin_walled`, `diff_trans`, `flatness`, `subsurface_color`,
  `coat_ior`/`coat_roughness`/`coat_normal`, `sheen_roughness`,
  `thin_film_thickness`/`thin_film_ior`. Tutti texturabili.
- Kulla-Conty multi-scattering compensation (LUT 32×32 con lazy init protetta).
- VNDF sampling (Heitz 2018) per specular e transmission.
- Charlie sheen (Estevez-Kulla 2017) al posto dello Schlick.
- Thin-film iridescence (Belcour-Barla 2017).
- Sampler Sobol + Owen scrambling (opt-in `--sampler sobol`, ora default).
- Pulizia `IMaterial`: rimossi i campi Blinn-Phong, sostituiti da
  `NeedsDirectLighting` e `IsDeltaScatter`.
- Suite di test `DisneyBsdfTests` (lobi riflessivi, reciprocity, sheen Charlie,
  anisotropic VNDF).

---

## 🗓️ Ciclo di Completamento MIS (Apr 2026)

Branch: `claude/complete-mis-implementation-q98Tb`. Chiusura della feature
#13: il MIS ora copre tutti i materiali (non solo `DisneyBsdf`), la phase
function volumetrica, ed è disponibile la heuristica `power` oltre a
`balance` via `--mis`.

Deliverable:

- Codice — `Lambertian`/`Metal`/`MixMaterial` implementano `Sample`/`Pdf`/`Evaluate`;
  `IPhaseFunction.Pdf` aggiunto; `Renderer.MisWeight` centralizza il calcolo;
  flag CLI `--mis <balance|power>`.
- Test — `MisMaterialsTests` (PDF integrate-to-1, sampler consistency, mix
  linearity, delta-mirror). 140/140 verdi.
- Scene — `scenes/furnace-{lambert,metal,mix}.yaml` (energy conservation),
  `scenes/test-env-fog.yaml` (regressione env+medium),
  `scenes/foggy-hdri.yaml` (phase-MIS showcase),
  `scenes/showcases/showcase-mix-metal-rust.yaml` (MixMaterial sotto area light).
- Verifica visiva — render before/after locali su Cornell, mix-metal-rust,
  foggy-hdri: la differenza più marcata è sul phase-MIS (foggy-hdri),
  con riduzione visibile dei firefly speckle sul pavimento e nello sfondo.

Per il contratto API, le formule delle heuristiche e i casi limite (lobi
delta, MixMaterial mixture estimator, Metal NDF→VNDF) vedi
[`docs/technical/multiple-importance-sampling.md`](docs/technical/multiple-importance-sampling.md).
Documentazione utente in `docs/reference/{rendering-profiles.md, profili-di-rendering.md}`,
tutorial 06 e 09 in EN+IT, riga `--mis` in `README.md`.

---

## 🗓️ Ciclo di Review — Rendering & Scene Classification (Apr 2026)

Riepilogo delle modifiche in questo ciclo (branch
`claude/review-raytracer-rendering-7hQpu`):

- **Scene classifier basato sul flusso** — sostituito
  `ILight.Illuminate(Vector3.Zero)` con `ILight.ApproximatePower(AABB sceneBounds)`,
  un flusso radiante deterministico, receiver-independent, scene-scale-aware
  (Rec.709 luminance). Il Renderer normalizza il flusso totale per la
  superficie della sfera di scena (`4π R²`) ottenendo un'irradianza media
  invariante per traslazione e scala uniforme. Threshold ricalibrata
  `1.0 → 0.5`. Formule per tipo: `4π·I` (point/sphere), `I·π·R²` (directional),
  `I·(Ω_core + Ω_fall/3)` (spot), `π·L·A` (area/geometry), `π·L̄·π·R²`
  (environment).
- **Finite-scene bounds** — nuovo helper `Renderer.ComputeFiniteSceneBounds`
  che unisce solo i figli del `HittableList` con estensione assiale sotto la
  soglia di sentinel (1e5), filtrando l'AABB ±1e6 di `InfinitePlane`. Prima,
  qualsiasi scena con pavimento inflava R a ~1700 e rendeva invisibili al
  classificatore point/spot light a intensità realistica (chess veniva
  classificato come indirect-dominant per errore).
- **Rimozione `ILight.Illuminate()`** — aveva un unico chiamante (il
  costruttore del Renderer) e un contratto receiver-dependent che richiedeva
  FIX #7..#14 su più implementazioni per workaround. Eliminato insieme ai
  suoi asterischi; ora il contratto è espresso da `ApproximatePower`.
- **Verifica su scene di riferimento** — Cornell (indirect ✓, 0.427),
  chess (direct ✓), foggy-road (indirect ✓), alchemist-lab (indirect ✓),
  sample/teapot (direct ✓). 72/72 test passano; render Cornell 400² s=64 d=10
  senza dark-spot regressions.

Doc aggiornata: `docs/technical/rendering-pipeline.md` §2.1 + tabella runtime,
`docs/technical/path-tracing-and-lighting.md` §5.6.

---

## ✅ TODO

- [ ] Refactoring: spostare la proprietà `Seed` dall'interfaccia `IHittable` a un'interfaccia più appropriata (es. `ISeeded` o specifica per le primitive), in quanto nodi strutturali come `BvhNode` o `Transform` non necessitano di un seed.
- [ ] Creare librerie di template: `scenes/libraries/chess-pieces.yaml`, `scenes/libraries/furniture.yaml`
- [x] Feature #22 completa: Instancing con geometria condivisa a livello BVH (memory-efficient per scene con migliaia di istanze)
- [ ] Fare una review completa dei tutorial (`tutorial/`): correttezza rispetto al codice, omissioni di feature, grammatica, esempi, indici.
- Spezzare il file SceneLoader.cs in più file (al momento è un file troppo grande).
- [ ] Aggiornare la checklist di testing con le scene di riferimento corrette.

---

## 🐛 Bug Noti

| # | Descrizione | Severità | Scena / File | Stato |
|---|-------------|----------|--------------|-------|
| 1 | ~~Il parametro `seed` nei materiali procedurali non produce risultati riproducibili tra render della stessa scena.~~ **Risolto** da tre commit in sequenza: `fix(perlin): make procedural noise textures deterministic per object seed` (determinismo di `Perlin.GetOrCreate`) + `fix(seed): stable hash for scene seed fallback` (hash FNV-1a stabile al posto di `string.GetHashCode()` randomizzato per processo) + `fix(seed): replace HashCode.Combine in seed fallback` (mixer Boost-style al posto di `System.HashCode.Combine`, anch'esso randomizzato per processo in .NET). Ora vale: **pattern procedurale identico tra render della stessa scena**, sia con `seed:` esplicito sia senza. La variazione per-oggetto senza seed è comunque derivata dall'indice/tipo/nome in modo stabile cross-run. | 🔴 **Alta** | Qualsiasi scena con texture `marble`/`wood`/`noise` | ✅ |
| 2 | L'RNG globale del path tracing è seedato da `Environment.TickCount` in `MathUtils.cs:12`, quindi due render della stessa scena producono **rumore di rendering diverso** (pattern high-frequency da sampling stocastico di luci, BSDF, DoF, RR, ecc.). A sample count alto (≥ 64) il rumore si media e l'immagine converge, ma a sample count basso le differenze sono visibili. Questo **non riguarda** i pattern procedurali (risolto dal bug #1), ma la riproducibilità bit-identica dell'immagine finale. Vedi sezione "RNG globale e determinismo totale" nei Riferimenti Tecnici per dettagli su architettura della fix e impatti. | 🟠 **Media** | Qualsiasi scena renderizzata con pochi sample | ⬜ |

Severità: 🔴 **Alta** 🟠 **Media** 🟡 **Bassa**

---

## 📚 Riferimenti Tecnici

### RNG globale e determinismo totale (riferimento per bug #2)

**Stato attuale.** `MathUtils.cs:12-17` espone un RNG thread-local seedato da un counter atomico inizializzato a `Environment.TickCount`:

```csharp
private static int _globalSeed = Environment.TickCount;
private static readonly ThreadLocal<Random> _threadRng = new(
    () => new Random(Interlocked.Increment(ref _globalSeed)));
public static Random Rng => _threadRng.Value!;
```

Questo RNG è usato ovunque nel renderer per decisioni stocastiche: direzioni BSDF, campionamento luci (NEE), lens sampling (DoF), Russian Roulette, stratified jitter, ecc. Essendo seedato dal tempo di sistema, ogni esecuzione parte da uno stato diverso → lo stesso YAML renderizzato due volte dà pixel diversi (visibile soprattutto con basso numero di sample).

**Perché è un problema (a volte).**
- **Visual regression testing**: non esiste un baseline riferito perché l'output cambia sempre.
- **Comparazioni A/B**: cambio un parametro (materiale, luce, camera) e voglio vedere *quel* diff, invece vedo diff + rumore casuale.
- **Animazioni / sequenze**: frame consecutivi renderizzati in momenti diversi hanno rumore scorrelato → flickering temporale.
- **Debug**: "perché questo pixel ha quel valore?" non è riproducibile.

**Perché non è stato fixato insieme al bug #1.**
- Scope diverso: bug #1 riguardava i **pattern procedurali** (determinismo spaziale della texture, cross-run). Bug #2 riguarda il **rumore di sampling** (determinismo stocastico del percorso dei ray, cross-run).
- Costo architetturale maggiore: un fix richiede di rimpiazzare l'RNG globale thread-local con un seeding deterministico per pixel × sample, tipicamente una funzione pura `hash(pixelX, pixelY, sampleIndex, bounceDepth) → state`. Tutti i siti di uso di `MathUtils.Rng` vanno rivisti per prendere lo stato come parametro.
- Rischio performance: la nuova strategia deve evitare di allocare `Random` per pixel e non deve perdere l'indipendenza tra thread.

**Architettura proposta per la fix (quando verrà fatta).**
1. Introdurre uno `Sampler` per-pixel che incapsula lo stato RNG, seedato deterministicamente da `(pixelX, pixelY, sampleIndex)` tramite hash stabile (PCG, xoshiro, o splittable RNG alla Salmon).
2. Propagare il `Sampler` attraverso la call chain del path tracer (`TraceRay`, `Scatter`, `EvaluateDirect`, `SampleLight`, …) come parametro esplicito invece che via singleton.
3. Rimuovere `MathUtils.Rng` e le helper statiche `RandomFloat`/`RandomInUnitSphere`/ecc., sostituendole con metodi del `Sampler`.
4. Opzionale: aggiungere CLI flag `--render-seed N` per fissare esplicitamente il seed del frame (default: 0, tutti i render identici; override per variazione intenzionale tra frame di animazione).

**Impatti attesi.**
- Output **bit-identico** tra render della stessa scena.
- Smoke test CI diventa verificabile contro un baseline stabile (possibilmente anche con hash dell'immagine).
- Possibile leggero overhead (hash per sample invece di `rng.Next`) compensato dalla rimozione del counter atomico globale e dal fatto che ogni pixel ha uno stato RNG indipendente (meglio per cache locality con #15 Tile-based Rendering).
- Cambio di output visivo rispetto ai render attuali: l'immagine "finale" di una scena sarà diversa da qualsiasi render specifico fatto finora, ma sarà l'unica immagine che quella scena può produrre da quel momento in poi.

**Dipendenze.** Idealmente va affrontato insieme o dopo #15 (Tile-based Rendering), perché il tile è il granulo naturale per il seeding per-pixel e per parallelizzare lo stato del sampler senza contesa.

---

## 💡 Ciclo Light Hardening

**Data:** 2026-04

### Motivazione
Review completa del sistema di sorgenti luminose in `src/RayTracer/Lights/` con focus sull'eliminazione dei firefly. Tutti i 7 punti identificati dalla review sono stati implementati in un singolo ciclo.

### Modifiche effettuate

**1. SoftRadius su AreaLight / SphereLight / GeometryLight**
- Aggiunto parametro `softRadius` (default `0f` = backward compat) ai costruttori di `AreaLight`, `SphereLight`, `GeometryLight`.
- `AreaLight`: applica `distSq = max(distSq, r²)` nel denominatore `cosLight / d²`.
- `GeometryLight`: stesso clamp nel path area-sampling (`ComputeAreaSample`).
- `SphereLight`: accetta il parametro per coerenza API; il solid-angle path (normale) non ne ha bisogno, subsuming già il termine 1/d² nel pdf cone.
- YAML: `soft_radius` su tutti e tre i tipi.

**2. Depth-aware indirect firefly clamp**
- Nuovo campo `_indirectMaxSampleRadiance = _maxSampleRadiance × indirectClampFactor`.
- `ClampRadianceIndirect()`: versione di `ClampRadiance` con soglia più bassa per i bounce indiretti.
- Applicato in `ShadeSurface` (path Scatter legacy) e `ShadeSampleBounce` (path Sample MIS).
- CLI: `--indirect-clamp-factor <f>` (default `1.0` = no extra clamp, backward compat).
- Stile Cycles/Arnold: `--clamp 100 --indirect-clamp-factor 0.25` → clamp=25 su depth ≥ 1.

**3. Light Importance Sampling (power-based picking)**
- Nuovo `Lights/LightDistribution.cs`: CDF su `ILight.ApproximatePower(sceneBounds)`. Fallback a distribuzione uniforme se `totalPower == 0`.
- `LightSamplingStrategy` enum: `All` (default) / `Power` / `Uniform`.
- Renderer: costruisce `LightDistribution` una sola volta (O(N) setup). Loop NEE aggiornato con estimatore single-light `contribution / pPick` e MIS con `pNee = pPick × pLightSample`.
- CLI: `--light-sampling <all|power|uniform>`.

**4. Sun disc su DirectionalLight**
- Parametro `angularRadiusDeg` (default `0f` = hard shadows, backward compat).
- Uniform cone sampling (PBRT §6.2.3) con base ONB Frisvad (stesso di SphereLight).
- `IsDelta = false` quando attivo; `PdfSolidAngle = 1/Ω` per direzioni nel cono.
- `ShadowSamples` default auto: 1 se delta, 16 se disco attivo.
- YAML: `angular_radius` (gradi) su `directional`/`sun`.
- Valore reale Sole: `angular_radius: 0.27`.

**5. ShadowSamples configurabile su SpotLight**
- Parametro `shadowSamples` (default `1` = backward compat).
- Nuovo `IlluminateAndTestStratified`: jitter del punto sorgente entro un disco di raggio `softRadius` quando `softRadius > 0 && shadowSamples > 1`.
- Se `softRadius == 0`, campioni aggiuntivi non hanno effetto (degenera al caso singolo).
- YAML: `shadow_samples` su `spot`.

**6. Eliminato PRNG dal costruttore di GeometryLight**
- Aggiunto `float SurfaceArea { get; }` a `ISamplable` (default: `Sample().Area` per backward compat).
- Implementato con formule chiuse su tutte le geometrie: Sphere (4πR²), Triangle (½|cross|), SmoothTriangle, Quad (|U×V|), Mesh (_totalArea), Disk (πR²), Cylinder (2πRH+2πR²), Cone (π(R+r)·slant+caps), Torus (4π²Rr), Capsule, Box (6), Lathe (_totalArea), Transform (approssimazione con avgNormalLen).
- `GeometryLight` usa `geometry.SurfaceArea` invece di `geometry.Sample()`.
- Risultato: scene-load 100% deterministico, sequenza PRNG per-pixel riproducibile.

**7. Test di regressione firefly**
- Nuova scena `scenes/tests/firefly-stress.yaml`: sphere light vicina al piano, medium omogeneo denso (σ_s=2), depth=8.
- `FireflyRegressionTests.cs` (xUnit): 5 nuovi test.
  - `FireflyStress_SpikePixelCount_WithinThreshold`: render 64×64/16SPP, conta pixel near-saturati.
  - `AreaLight_SoftRadius_ZeroIsIdentical`: backward compat.
  - `ISamplable_SurfaceArea_IsDeterministic`: 100 chiamate identiche, no PRNG.
  - `LightDistribution_Power_SumsToOne`: sum pdf = 1, ordinamento per potenza.
  - `DirectionalLight_DiscMode_IsNotDelta`: IsDelta=false, PdfSolidAngle > 0 on-axis.

### Backward compatibility
Tutti i nuovi parametri hanno default che preservano il comportamento precedente:
- `softRadius: 0` → nessun clamp.
- `angularRadiusDeg: 0` → ombre nette.
- `shadowSamples: 1` su SpotLight → singolo sample.
- `indirectClampFactor: 1.0` → clamp indiretto = clamp primario.
- `lightSampling: All` → somma su tutte le luci.

### Test suite
Prima: 141 test. Dopo: 146 test (5 nuovi). Tutti verdi.

---

## 🪚 Ciclo Extrusion Primitive

**Data:** 2026-05

### Motivazione
Il motore aveva la `Lathe` (superficie di rivoluzione) ma mancava il duale naturale: una primitiva che estrude un profilo 2D chiuso lungo un asse. Questa è la capacità di modellazione di base che POV-Ray chiama `prism`, Blender / Houdini / Maya / OpenSCAD chiamano "extrude" / `polyextrude` / `linear_extrude`. In Arnold / RenderMan / Cycles l'estrusione passa sempre da mesh — averla nativamente nel motore è un valore aggiunto concreto: niente OBJ esterno per stelle, ingranaggi, lettere, profilati architettonici, sezioni a L/U/T/H, washer, basi di lampade, cornici, pezzi araldici.

### Modifiche effettuate

**1. Nuova primitiva `Extrusion` (`src/RayTracer/Geometry/Extrusion.cs`)**
- `Extrusion : IHittable, ISamplable`. Triangola le pareti laterali e i cap, costruisce internamente una `BvhNode` (stesso pattern di `Mesh`).
- Tre modi di profilo speculari al Lathe: `linear` (polilinea, `Triangle` flat per ridge sharp), `catmull_rom` (centripetale chiuso, `SmoothTriangle` con normali medie sui bordi adiacenti), `bezier` (4 control point per segmento, loop chiuso).
- Twist (`twist_degrees`) e taper (`taper`) opzionali per colonne/raccordi industriali.
- `caps: both | start | end | none` per gusci aperti o tray-shape.
- UV: U = arc length normalizzata sul perimetro del profilo, V = altezza ∈ [0, 1]. Niente seam-stretch su texture wrapping.
- `SurfaceArea` deterministico = somma chiusa delle aree dei triangoli; supporta NEE area-weighted automatico per Extrusion emissive (insegne al neon a forma di stella, ecc.).

**2. Utility 2D (`src/RayTracer/Geometry/Polygon2D.cs`)**
- `SignedArea` (shoelace) per orientamento auto.
- `EarClip` O(n²) per triangolare profili concavi semplici (stelle, gear, lettere, L/U/T/H) — fallback robusto su poligoni numericamente degenerati.
- `TessellateClosedCatmullRom` (centripetale α=½, formula Barry-Goldman per knot non uniformi).
- `TessellateClosedBezier` per cubic Bezier in loop chiuso.

**3. SceneLoader registration**
- `type: "extrusion"` con alias `"prism"`, `"linear_extrude"` (mirror di `lathe` / `revolution` / `surface_of_revolution`).
- Nuovi campi YAML in `SceneData`: `caps`, `twist_degrees`, `taper`, `curve_samples` (riusa `profile`, `profile_type`, `profile_bezier_controls`, `height` esistenti).
- Validazione completa: rifiuto profili < 3 punti, height/taper non positivi, control Bezier count errato; auto-rimozione duplicati consecutivi e chiusura ridondante; downgrade trasparente CR → linear con warning quando troppi pochi punti.
- CSG: `extrusion` accettato come figlio CSG via il dispatch standard di `CreateEntity` — nessuna modifica a `BuildCsgChild` necessaria.

**4. Test (`src/RayTracer.Tests/ExtrusionTests.cs` — 9 nuovi)**
- `Square_Profile_MatchesBox`: 500 raggi random, equivalenza con `Box`.
- `DenseNgon_MatchesCylinder`: 64-gon vs `Cylinder`, tolleranza 8e-3 (silhouette discretization).
- `ConcaveStar_HitsLieInsideAabb`: stella a 5 punte, AABB containment + nessun foro spurio.
- `Square_SurfaceArea_MatchesBox`, `NoCaps_OmitsTopAndBottom`, `LShape_Concave_SurfaceAreaMatchesClosedForm`: closed-form su area.
- Constructor validation: `TooFewPoints_Throws`, `NonPositiveHeight_Throws`, `BezierWithWrongControlCount_Throws`.

**5. Showcase**
- `scenes/showcases/extrusion-showcase.yaml`: tre soggetti hero — stella araldica dorata (linear concavo), colonna scanalata twistata (catmull_rom + twist + taper), ingranaggio meccanico (linear con 24 denti). Illuminazione cinematografica 3-point + sun disc.

**6. Documentazione**
- `docs/reference/scene-reference.md` §7.17 (EN) e `riferimento-scene.md` §7.17 (IT): full schema YAML, esempi linear/catmull_rom/bezier, nota su caps/concavità/twist/taper/curve_samples.
- Lista figli CSG aggiornata in entrambe le lingue.

### Backward compatibility
Tutti i nuovi alias / campi sono nuovi — nessuna scena esistente è toccata.

### Test suite
Prima: 146 test. Dopo: 155 test (9 nuovi). Tutti verdi.

---

## 🧪 Checklist Verifiche (Testing)

Procedure da eseguire prima di ogni commit importante.

- [ ] **Smoke Test**: Eseguire il render di `primitive-showcase.yaml` (16 samples) e verificare che non ci siano crash.
- [ ] **Visual Regression**: Confrontare il render di `cornell-box.yaml` con l'immagine di riferimento.
- [ ] **Performance Check**: Verificare che il tempo di render di una scena standard non sia aumentato più del 5% senza motivo.
- [ ] **YAML Validation**: Assicurarsi che ogni nuova proprietà YAML abbia un valore di default sensato nel codice.
- [ ] **CSG Regression**: Render di `csg-showcase.yaml` — verificare union, intersection e subtraction visivamente.
- [ ] **HDRI Test**: Render di `hdri-showcase.yaml` — verificare riflessi, rifrazioni e illuminazione globale.
- [ ] **Mix Material Test**: Render di `mix-material-showcase.yaml` — verificare blend costante (3 livelli), maschere procedurali (noise, marble, wood), lava emissiva con blend marble e checker bicolore.
- [ ] **Group Test**: Render di `group-showcase.yaml` — verificare trasformazioni ereditate, template/istanze, import.
- [ ] **Torus Test**: Render di `torus-showcase.yaml` con camera `pinhole`, `dof_soft` e `dof_extreme` — verificare assenza di contorni fantasma, deformazioni geometriche con DoF, occlusione corretta torus/cono e torus/cilindro. Verificare annulus (piatto, verticale, inclinato) e torus emissivo.
- [ ] **Import Test**: Verificare che materiali e template importati da file esterni funzionino correttamente.
- [ ] **Template Override**: Verificare che il materiale dell'istanza sovrascriva quello del template.
