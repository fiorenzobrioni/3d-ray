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

**13. Multi-Importance Sampling ✅** — Copertura MIS completa su superfici, environment e volumetrica. La tripla simmetrica `Evaluate(V,L)` / `Pdf(V,L)` / `Sample(V)` su `IMaterial` è ora implementata da **tutti** i materiali rilevanti: `DisneyBsdf`, `Lambertian`, `Metal` (Cook-Torrance GGX, fuzz=0 → delta sample), `MixMaterial` (Pdf/Evaluate come blend lineare dei figli, Sample con one-sample mixture estimator). `Dielectric` resta intenzionalmente delta (entrambi i lobi sono Dirac) e segnala `IsDeltaScatter=true`. La phase function partecipa al MIS per i bounce volumetrici: `IPhaseFunction.Pdf(wo,wi)` è esposto e treadato come `prevBsdfPdf` nel ramo medium di `TraceRay`; `ComputeDirectLightingMedium` pesa l'in-scattering NEE contro la phase Pdf con la stessa heuristica. Il Renderer espone l'enum `MisHeuristic { Balance, Power }` e il flag CLI `--mis <balance|power>` (default balance); l'helper `MisWeight(p,q)` centralizza la formula e sostituisce le precedenti formule inline per emission-weight, sky-weight e NEE. Test MIS in `MisMaterialsTests`: PDF integrate-to-1 per Lambertian e per le 5 phase function, Sample/Pdf/Evaluate consistency per Lambertian/Metal, linearità di Pdf/Evaluate per MixMaterial, delta-mirror invariants per Metal con fuzz=0.

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

Riepilogo delle feature atterrate in questo ciclo (branch
`claude/review-disney-materials-QO2mO`):

- **MIS balance heuristic** — `IMaterial` passa da `Scatter` da solo a tripla
  simmetrica `Sample / Evaluate / Pdf`; il path tracer propaga `prevBsdfPdf` +
  `prevIsDelta` e combina NEE con BSDF sampling tramite Veach (`ComputeDirectLighting` + `WeightEmission`). Le luci espongono `IsDelta` e `PdfSolidAngle`.
- **Disney BSDF: parametri mancanti** — aggiunti `anisotropic` /
  `anisotropic_rotation`, `transmission_color` / `transmission_depth` (Beer-Lambert
  con medium-switch), `thin_walled`, `diff_trans`, `flatness`, `subsurface_color`,
  `coat_ior` / `coat_roughness` / `coat_normal`, `sheen_roughness`,
  `thin_film_thickness` / `thin_film_ior`. Parametri texturabili.
- **Kulla-Conty multi-scattering** — LUT 32×32 con lazy init protetta da
  dead-lock del thread pool; compensa la dispersione d'energia del singolo
  bounce GGX per dielettrici e metalli rugosi.
- **VNDF sampling** — Heitz 2018 per specular e transmission; rimossi i
  clamp empirici di roughness. PDF riportata full-sphere per back-hemisphere L.
- **Charlie sheen** — fit Estevez-Kulla 2017 rimpiazza lo Schlick sheen.
- **Thin-film iridescence** — Belcour-Barla 2017 per bolle, opal, AR coating.
- **Sampler Sobol + Owen scramble** — opt-in via `--sampler sobol`.
- **Pulizia IMaterial** — rimossi i vestigi Blinn-Phong
  (`DiffuseWeight`/`SpecularExponent`/`SpecularStrength`); sostituiti da due
  booleani espliciti: `NeedsDirectLighting` (spegne la NEE per gli emissivi) e
  `IsDeltaScatter` (marca i bounce delta per non sopprimere l'emissione al
  prossimo hit). Lambert/Metal/Dielectric/Mix aggiornati coerentemente.
- **Suite di test `DisneyBsdfTests`** — correttezza dei lobi riflessivi,
  reciprocity, copertura delle nuove feature (sheen Charlie, anisotropic VNDF).

---

## 🗓️ Ciclo di Completamento MIS (Apr 2026)

Branch: `claude/complete-mis-implementation-q98Tb`. Estende la copertura
MIS da DisneyBsdf a tutti i materiali rilevanti, alla phase function
volumetrica e alla scelta della heuristica.

- **Materiali superficiali** — `Lambertian`, `Metal` (Cook-Torrance GGX,
  fuzz=0 → delta) e `MixMaterial` ora implementano la tripla
  `Sample`/`Pdf`/`Evaluate`. `Dielectric` resta intenzionalmente delta.
- **Phase function MIS** — `IPhaseFunction.Pdf(wo,wi)` esposto (default
  uguale a `Evaluate` per Isotropic / HG / dHG / Rayleigh / Schlick) e
  threadato come `prevBsdfPdf` nel ramo medium di `TraceRay`.
  `ComputeDirectLightingMedium` pesa l'in-scattering NEE con MIS.
- **Heuristic selezionabile** — `MisHeuristic { Balance, Power }` enum
  esposto al `Renderer`; flag CLI `--mis <balance|power>` (default
  `balance`). Helper `MisWeight(p,q)` centralizzato e usato da
  `WeightEmission`, `SampleSky`, `ComputeDirectLighting` e
  `ComputeDirectLightingMedium`.
- **Test** — `MisMaterialsTests` aggiunge: PDF integrate-to-1 per
  Lambertian e per le 5 phase function (entro 5%); Sample↔Pdf↔Evaluate
  consistency per Lambertian/Metal; linearità di Pdf/Evaluate per
  MixMaterial; delta-mirror invariants per Metal con fuzz=0. 140/140
  test totali passano.
- **Scene introdotte e committate**
  - `scenes/furnace-{lambert,metal,mix}.yaml` — banco diagnostico
    energetico per Sample/Pdf/Evaluate.
  - `scenes/test-env-fog.yaml` — regressione: shadow ray verso
    EnvironmentLight attraverso `global_medium` (verifica Beer-Lambert).
  - `scenes/foggy-hdri.yaml` — scena cinematografica HDRI + fog per
    showcase del phase MIS.
  - `scenes/showcases/showcase-mix-metal-rust.yaml` — gradient di mix
    cromo→ruggine sotto area light (caso d'uso peggiore pre-MIS).
- **Verifica visiva** — confronti before/after sulle tre scene
  rappresentative (`renders/mis-comparison/<scene>-{before,after}.png`,
  non committati): meno fireflies sulle pareti adiacenti alla luce
  (Cornell-like), highlight più puliti sui mix metal/diffuse, shaft
  atmosferici più rapidi a convergere su HDRI + fog. Render time
  invariato — il ramo MIS è una moltiplicazione in più per shadow
  sample, irrilevante rispetto al costo di shadow + BSDF eval.

Documentazione aggiornata: `docs/technical/path-tracing-and-lighting.md`
§2.2 (sezione MIS estesa), `docs/reference/rendering-profiles.md` +
versione IT (flag `--mis`), tutorial 06 e 09 in EN+IT (paragrafi MIS),
`README.md` (voce NEE+MIS).

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
