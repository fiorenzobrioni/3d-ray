# 🧭 PLANNING — 3D-Ray

Roadmap, lavori in corso, TODO, bug noti e idee. Per lo storico dei cicli di sviluppo e le note di design vedi [`DEVLOG.md`](DEVLOG.md).

> Stati: `✅ Fatto` · `🔧 In corso` · `⬜ Da fare`

---

## 🗺️ Roadmap

### Fase 0 — Fondamenta ✅

Path tracer multi-bounce, parallel render, BVH SAH, camera DOF + multi-camera, primitive base + trasformazioni, materiali Lambertian/Metal/Dielectric, luci Point/Directional/Spot/Area + NEE, Russian Roulette adattiva, stratified sampling, ACES + gamma + firefly guard, YAML loader, output PNG/JPEG/BMP, CI smoke test.

### Fase 1 — Visivo ✅

| # | Feature |
|---|---------|
| 1 | Emissive Material (diventa GeometryLight per NEE) |
| 2 | Gradient Sky (zenith / horizon / ground + sun disc) |
| 3 | Image Textures (PNG/JPG/BMP/GIF/TIFF/WebP, bilinear, tiling) |
| 4 | IBL / HDRI (Radiance .hdr, CDF 2D marginal+conditional, rotazione Y) |
| 5 | Normal Mapping (TBN + Gram-Schmidt, OpenGL/DirectX) |

### Fase 2 — Materiali & geometria ✅

| # | Feature |
|---|---------|
| 6 | Disney BSDF / PBR (vedi sotto) |
| 7 | OBJ Mesh Loader (smooth normals, UV, TBN, BVH interno) |
| 8 | Torus (quartica Ferrari, UV toroidale, NEE, CSG, Transform) |
| 9 | Mix Material (selezione stocastica scatter, blend deterministico NEE, mask qualsiasi texture) |
| 10 | Sphere Light (solid-angle sampling PBRT §6.2.3, 2-10× più efficiente di GeometryLight) |
| 11 | Scene Graph / Groups (transform ereditate, template+instance, import YAML con merge) |

**Disney BSDF** include: lobi diffuse / GGX / clearcoat / transmission, Kulla-Conty multi-scattering (LUT 32×32), GGX anisotropico (VNDF), Beer-Lambert via medium-switch, parametri Disney 2015 (`thin_walled`, `diff_trans`, `flatness`, `subsurface_color`), clearcoat stile Arnold (`coat_ior`, `coat_roughness`, `coat_normal`), Charlie sheen (Estevez-Kulla 2017), thin-film iridescence (Belcour-Barla 2017). MIS-correct (`Sample`/`Evaluate`/`Pdf` consistenti, furnace + reciprocity test).

### Fase 3 — Sampling avanzato 🔧

| # | Feature | Stato |
|---|---------|-------|
| 12 | Importance Sampling (GGX su Metal/Disney, env via CDF 2D, cosine-weighted diffuse) | ✅ |
| 13 | Multi-Importance Sampling (tutti i materiali + phase function, balance/power heuristic) | ✅ |
| 14 | Adaptive Sampling | ⬜ (dopo #15) |
| 15 | Tile-based Rendering | ⬜ |
| 16 | Denoiser (bilateral/NLMeans guidato da normal/albedo/depth) | ⬜ (dopo #15) |
| 17 | HDR Output (PFM/EXR pre-tone-mapping) | ⬜ |
| +  | Sobol + Owen Scrambling sampler (`--sampler sobol`, default attivo) | ✅ |

### Fase 4 — Cinematografici 🔧

| # | Feature | Stato |
|---|---------|-------|
| 18 | Motion Blur | ⬜ |
| 19 | Volumetric Rendering | 🔧 Stage 1 + 1.5 ✅ |
| 20 | Subsurface Scattering | ⬜ |
| 21 | CSG (union/intersection/subtraction, all-hits, normali corrette) | ✅ |
| 22 | Instancing (geometria condivisa, override material/seed per-istanza) | ✅ |
| +  | Extrusion primitive (linear/catmull_rom/bezier + twist + taper + caps) | ✅ |
| +  | Transparent shadow rays (vetro Fresnel-tinted in NEE — Strada 1) | ✅ |

**Volumetrics Stage 1+1.5**: medium globale opt-in (`world.medium`), output bit-identico se assente. `IMedium`: Homogeneous (Beer-Lambert + free-path), HeightFog (densità esponenziale closed-form), HeterogeneousProcedural (Perlin fBm, delta+ratio tracking), Grid (`.vol` o inline, slab clip + trilinear + delta tracking). `IPhaseFunction`: Isotropic, HG, Rayleigh, Double-HG (Nubis), Schlick (fast-HG). Stage 2 (deferred): EmissiveMedium, MediumInterface per-entity, SSS random-walk, OpenVDB nativo, spectral tracking — tutti richiedono modifiche ad `IMedium` / `HitRecord` / `Renderer.TraceRay`.

### Fase 5 — Frontiera 🔧

| # | Feature | Stato |
|---|---------|-------|
| 23 | Bidirectional Path Tracing (dopo #13) | ⬜ |
| 24 | Spectral Rendering (lunghezze d'onda → dispersione prismatica) | ⬜ |
| 25 | Surface Displacement Stack (bump map, mesh subdivision Loop/Catmull-Clark, scalar/vector displacement, autobump) | ✅ |
| 26 | GPU Acceleration (CUDA/Vulkan, progetto separato) | ⬜ |

### Dipendenze chiave

```
#3 Image Tex ─► #5 Normal Map, #9 Mix Material
#6 Disney   ─► #20 SSS
#7 OBJ      ─► #22 Instancing, #25 Displacement ✅
#11 Scene G ─► #22 Instancing
#12 IS      ─► #13 MIS ─► #23 BDPT
#15 Tiles   ─► #14 Adaptive, #16 Denoiser
```

---

## 🌟 Roadmap caustiche

Strategia incrementale per le caustiche, in ordine di costo crescente. Strada 1 è la baseline ora attiva; Strada 2 è il prossimo target sensato; Strada 3 è opzionale e architetturalmente invasiva.

| Strada | Cosa risolve | Effort | Stato |
|--------|--------------|--------|-------|
| 1. Transparent shadow rays | Ombra dura del vetro → soft Fresnel-tinted (Arnold/Cycles default) | 1-2 giorni | ✅ |
| 2. MNEE (Manifold Next Event Estimation) | Caustiche focalizzate 1–2 interfacce attraverso vetro liscio solido (sfera/lente) + specchio, luci d'area. Cycles 3.2 "Shadow Caustics" | 2-3 settimane | ✅ |
| 2b. Specular Manifold Sampling | Caustiche da vetro frosted/rough (`roughness > 0` con `spec_trans > 0`) + riflessione rough (metallo spazzolato): perturbazione stocastica della manifold (Zeltner 2020, Hanika 2015) | 1-2 settimane | ✅ |
| 2c. Caster su tutta la geometria | Estende i caster da sole sfere a tutte le primitive curve (cilindro, cono, capsula, toro), alle mesh smooth (clamp per-triangolo) e ai solidi CSG (clamp di membership sulla frontiera booleana) | 1 settimana | ✅ |
| 3. SPPM / VCM (photon mapping) | Caustiche multi-bounce, dispersive, indipendenti da differenziabilità della geometria. RenderMan PxrVCM | 6-10 settimane (SPPM) / 3-5 mesi (VCM) | ⬜ |

**Strada 1 — Transparent shadow rays ✅** Implementata. Lo shadow ray attraversa superfici trasmissive accumulando `(1 − Fresnel) · tint` per canale + Beer-Lambert `exp(−σ_a · d)` sul segmento interno fra entrata e uscita. Helper `Geometry/ShadowRay.Transmittance` con cap di 8 traversate; override `IMaterial.ShadowTransmittance` + `IMaterial.ShadowAbsorption` su `Dielectric` (no σ_a) / `DisneyBsdf` (entrambi quando `spec_trans > 0`) / `MixMaterial` (blend di entrambi).
**Limiti residui**: nessuna rifrazione dello shadow ray (no caustiche di lente in NEE); `roughness > 0` con `spec_trans > 0` (frosted glass) ignorata — lo shadow ray va dritto come vetro liscio. Per entrambi servono MNEE (Strada 2) o SPPM/VCM (Strada 3).

**Strada 2 — MNEE ✅** Implementata (vedi DEVLOG §Ciclo Caustiche Fase 2). Walker Newton-Raphson sulla manifold speculare (`Rendering/ManifoldWalker.cs`): trova `x → p₁(→ p₂) → luce` che soddisfa Snell/riflessione. Residuo unificato (half-vector generalizzato `η_a·ω_a + η_b·ω_b`, componente tangenziale = 0) per riflessione + rifrazione a 1/2 interfacce; Jacobiano per differenze finite in `(u,v)` (niente ray cast nel loop); termine geometrico `G = dΩ_x/dA_y` per perturbazione della luce e ri-solve. Conteggio singolo unbiased: shadow ray dritto reso opaco ai caster per i receiver (`ShadowRay.BlockCausticCasters`, thread-local) + soppressione del cammino forward via stato "caustic carrier" in `TraceRay`.
**Implementato**: `IManifoldSurface.EvaluateManifold` su `Sphere` (+ `Transform`); `HitRecord.DnDu/DnDv` (derivate della normale) su `Sphere`/`Transform`; opt-in YAML `caustic_caster`/`caustic_receiver` (+ `world.ground`); CLI `--caustics on|off`; `CausticCasterRegistry`; test analitici closed-form (`MnEeCausticTests`) + regressione di render (`MnEeRenderTests`).
**Non ancora coperto (follow-up)**: caster non-sferici (cilindro/cono/toro/mesh — serve `EvaluateManifold` + `DnDu/DnDv` per quelle primitive); catene > 2 interfacce; luci delta (punto/spot/direzionale) e ambiente; dispersione. Robustezza: dove il Newton non converge la luce trasmessa viene persa (no fallback MIS) — accettabile sui caster convessi, da raffinare.

**Strada 2b — Specular Manifold Sampling ✅** Implementata (vedi DEVLOG §Ciclo SMS Fase 2b). Estende `ManifoldWalker` a caster **rough/frosted** riusando l'infrastruttura MNEE: il residuo di Newton è generalizzato così che il half-vector `ĥ` punti a una **normale di microfaccetta `m`** campionata dalla VNDF GGX del caster (Heitz 2018), invece della normale geometrica `n` — il caso liscio è l'offset nullo (`m = n`). Nuovo entry `ManifoldWalker.ConnectRough` (offset stocastico, una prova dello **stimatore biased** di Zeltner 2020 §4.1); il `Renderer` media `--sms-samples` prove per connessione rough. Throughput rough = Fresnel(`m`) · `G1(L)` (cancellazione VNDF identica a `DisneyBsdf.ScatterTransmission`), con Beer-Lambert invariato; la riflessione rough usa Schlick-conduttore con `F0 = tint`. Caster rough: `DisneyBsdf` (`roughness > 0.04`, `spec_trans ≥ 0.5`) per la rifrazione e `Metallic ≈ 1` / `Metal` (qualsiasi `fuzz`) per la riflessione. Costo zero garantito con `--caustics off` o senza entità marcate, e bit-identico sul percorso liscio (un solo branch inlinato in più). Test: `SmsCausticTests` (analitici — riduzione al limite liscio, ammissibilità, riflessione — + regressione di render con guardia anti-firefly).
**Stimatore unbiased deferito**: la variante a probabilità reciproca (Zeltner §4.2; sequenza di Bernoulli di re-solve da seed casuali) è ground-truth ma con control-flow diverso e code lunghe che rischierebbero il budget per-pixel; lo stimatore biased — bias controllato che →0 con `roughness→0` — è la scelta praticabile. Anisotropia del caster: fallback isotropo (l'offset usa `αx/αy` ma il frame tangente è quello geometrico). Hook lasciato in `ManifoldWalker.ConnectRough`.

**Strada 2c — Caster su tutta la geometria ✅** Implementata (vedi DEVLOG §Ciclo Caster Fase 2c). Il walker è generalizzato da **una** superficie a **una chart per vertice** (`ReadOnlySpan<IManifoldSurface>`), così le due interfacce di una rifrazione possono stare su chart diverse; il seeding è dietro la nuova astrazione `IManifoldCaster`/`ManifoldSeed` (`AnalyticManifoldCaster` per le primitive a chart unica). **Implementato**: `EvaluateManifold` su `Cylinder`/`Cone`/`Capsule`/`Torus` (inversione delle convenzioni UV di `Hit`); `Mesh` come `IManifoldCaster` (seeding sul BVH interno, chart = triangolo via `HitRecord.HitPrimitive`, baricentriche dal punto); `CsgObject` come `IManifoldCaster` (seeding sul risultato booleano, chart = primitiva curva sottostante) con clamp di membership `ContainsPoint`; `Transform` inoltra (analitico → self-chart, mesh → bake world-space); `IClampedChart` applica il clamp **a convergenza** (triangolo / frontiera CSG) senza strozzare Newton; `SceneLoader.CanCastCaustics` filtra piatte/flat-mesh/CSG-piatto con warning. Test: `MeshCausticTests` + `CsgCausticTests`. **Deferito a 2d**: mesh **edge-crossing** completo con adiacenza e **planar-mirror NEE**.

**Strada 2d — Mesh edge-crossing 🟦 (tier 1 fatto, tier 2 roadmap)** Quando il vertice converge appena oltre il bordo del triangolo del seed, il clamp per-triangolo (`IClampedChart.Accept`) scarta la connessione → le mesh fortemente curve castavano poco o nulla.
- **Tier 1 — neighbor-seed ✅** (vedi DEVLOG §Ciclo Mesh Neighbor-Seed). Retro **lato chiamante**, solver condiviso intatto: alla `Mesh` viene costruita una **adiacenza dei facet** (`PrepareCausticAdjacency`, vertici saldati per posizione, edge→triangoli) e una nuova capability **solo-mesh** `INeighborSeedCaster.FacetNeighbors`. Se il solve primario è rigettato, `ManifoldWalker.RetryNeighborSeeds` ri-semina il vertice colpevole sul centroide di **ogni facet adiacente** e ri-risolve lo stesso solve; il clamp del vicino accetta solo se il vertice ci atterra davvero → resta unbiased. Caster analitici/CSG non implementano l'interfaccia ⇒ ramo identico bit-per-bit (516 test invariati). Test: `MeshCausticTests` (adiacenza + connessione off-axis che il clamp-only scarterebbe). **Limite**: recupera solo vertici a **una faccia** dal seed; mesh coarse molto curve restano più rumorose/deboli degli analitici.
- **Tier 2 — edge-walk in-solve ⬜** (roadmap, parte da qui). Hand-off del chart *dentro* il loop di Newton: quando un passo della line-search esce dal triangolo, rilevare il bordo attraversato, passare al vicino e riconvertire le baricentriche, proseguendo il solve attraverso il grafo di adiacenza (con budget di hop anti-ping-pong). Richiede che anche i ri-solve perturbati di `ComputeGeometricTerm` attraversino i bordi, o `G` diventa incoerente (fireflies). Tocca il solver numerico condiviso: va gated dietro la stessa capability solo-mesh per tenere analitici/CSG bit-identici, e validato come non-distorto (riferimento SPPM o sfera-mesh vs sfera analitica). Copre i vertici a distanza arbitraria che il tier 1 manca. Anche **planar-mirror NEE** resta qui.

**Strada 3 — SPPM/VCM ⬜** Pass 1 emette fotoni dalle luci e li deposita su superfici diffuse in un kd-tree; pass 2 fa density estimation durante il rendering. SPPM raffina il raggio progressivamente per convergenza unbiased; VCM combina BDPT vertex connections + photon merging via MIS (Georgiev 2012, gold standard).
**Pro**: caustiche multi-bounce, dispersive (con spectral), indipendenti dalla geometria; beneficio collaterale su indirect diffuse (final gathering accelerato).
**Contro**: due pass in serie (cambia l'orchestrazione del Renderer); 50-500 MB di memoria per il photon map; dispersione richiede upgrade spettrale a monte (RGB-only sbaglia il prisma); separazione delta-vs-diffuse nel cammino fotone è sottile.
**Lavoro stimato**: `Acceleration/PhotonMap.cs` (~600 righe, kd-tree con range query); `Rendering/PhotonEmitter.cs` (~400 righe); modifica profonda di `Renderer` (fase build prima del shading); CLI `--photons N --photon-radius r --sppm-iterations n`; profilo Caustic.

**Decisione corrente**: Strada 1 (default) + Strada 2 / 2b / 2c ✅ + Strada 2d tier 1 ✅ (opt-in via `--caustics on` + flag per-entità) coprono le caustiche di lente su tutta la geometria curva (primitive, mesh smooth, CSG); le mesh ora castano nel caso comune (vertice ≤1 faccia dal seed). Strada 2d tier 2 (edge-walk in-solve + planar-mirror NEE) e Strada 3 (multi-bounce/dispersive) restano roadmap.

**Limiti noti delle caustiche** (per quando si riprende la fase): (1) **luci delta** (point/spot/directional puntuale) non innescano MNEE/SMS — l'estimatore campiona un punto sull'**area** dell'emettitore (`pdf_A`, perturbazione di `y` per `G`), che una sorgente Dirac non possiede; servirebbe una formulazione a connessione deterministica. Una `directional` con `angular_radius > 0` non è delta e casta. (2) **"Vetro dentro vetro"**: `Dielectric`/`DisneyBsdf` calcolano l'IOR sempre contro l'aria (`ri = frontFace ? 1/n : n`), e `ManifoldWalker.EtaOnSide` assume esterno = aria; un'interfaccia dielettrico-dielettrico annidata (vino↔parete del calice) viene resa come due rifrazioni con film d'aria spurio invece dell'IOR relativo `η_in/η_out`. Serve IOR relativo dallo stack dei media (nested/priority dielectrics). Tocca `cristallo.yaml` (flag e luce lasciati in attesa di questo).

---

## 📋 TODO

- [ ] Review dei materiali in `scenes/libraries/materials/`: aggiornare quelli che beneficiano di surface displacement e aggiungere nuove librerie pro (pelli, cementi, sassi, marmi porosi e simili).
- [ ] **HeightField strata: layered stack BSDF "no-compromise"** — il selettore strata oggi è winner-takes-all con jitter Perlin 3-ottave + aspect bias `±Z`. La versione pro è uno **stack N-ary** con coverage weights normalizzati. Implementazione: nuovo `LayeredStratumMaterial` proxy `IMaterial` che incapsula la lista di `(StratumBand, IMaterial)` + funzione di weight geometrico `(altNorm, slopeDeg, curvature, aspect) → R^N`; `Scatter` campiona via distribuzione 1D pesata (PDF MIS-consistente); `EvaluateDirect` somma pesata. Back-compat via `strata_blending: "winner" | "stochastic" | "weighted"` (default `winner`).

  Tre estensioni obbligatorie per la parità con i terrain shader pro:

  **(a) Curvatura/concavità come weight input.** Laplaciano discreto della heightmap al hit (5-stencil) → `curvature ∈ [-1, +1]`. Concavità bonus a snow/ground; convessità bonus a rock. Costo: 4 sample heightmap extra per hit.

  **(b) Per-band noise mask configurabile.** Ogni `StratumBand` ottiene `noise_mask: { scale, octaves, amplitude, seed }` opzionale, che sovrascrive il jitter globale per quella band (snow: alta freq, sand: bassa freq, rock: media freq). Senza questo i confini sembrano tutti uguali.

  **(c) Sun-aware aspect bias.** L'aspect bias legge `SkyData.SunDir` e calcola `aspectCool = -dot(horizontalNormal, horizontalSunDir)` invece di usare `+Z` hardcoded.

  Vincoli: normal-map per-band composte nel `n_shading` finale; MIS sampling weights coerenti; doc + showcase + tutorial IT+EN; regression test terreno flat a due band con verifica del lerp al confine. Aggiornare `docs/technical/heightfield.md` §5 e §8.
- [ ] Refactoring: spostare `Seed` da `IHittable` a un'interfaccia dedicata (es. `ISeeded`); nodi strutturali (BvhNode, Transform) non hanno bisogno di seed.
- [ ] Review completa dei tutorial (`tutorial/`): correttezza vs codice, omissioni, grammatica, esempi, indici.
- [ ] Spezzare `SceneLoader.cs`.

---

## 🐛 Bug noti

_Nessun bug noto al momento._

---

## 💡 Idee / Appunti

_Spazio per idee e spunti futuri da valutare._

---

## 🧪 Checklist verifiche

Da eseguire prima di un commit importante.

- [ ] **Smoke**: render `primitive-showcase.yaml` (16 spp), no crash.
- [ ] **Visual regression**: confronto `cornell-box.yaml` con baseline.
- [ ] **Performance**: tempo render scena standard non +5% senza motivo.
- [ ] **YAML**: ogni nuova proprietà ha default sensato.
- [ ] **CSG**: render `csg-showcase.yaml` — union/intersection/subtraction visivamente corrette.
- [ ] **HDRI**: render `hdri-showcase.yaml` — riflessi/rifrazioni/GI corrette.
- [ ] **Mix**: render `mix-material-showcase.yaml` — blend costante (3 livelli), maschere (noise/marble/wood/checker), lava emissiva.
- [ ] **Group**: render `group-showcase.yaml` — transform ereditate, template/istanze, import.
- [ ] **Torus**: render `torus-showcase.yaml` con camera `pinhole`/`dof_soft`/`dof_extreme` — no contorni fantasma, occlusione torus/cone e torus/cylinder corretta.
- [ ] **Import**: materiali e template importati da file esterni funzionano.
- [ ] **Template override**: il materiale dell'istanza sovrascrive quello del template.
- [ ] **Transparent shadows**: render `cornell-box-spheres.yaml` — la sfera di vetro centrale proietta un alone Fresnel-tinted, non un'ombra dura.
