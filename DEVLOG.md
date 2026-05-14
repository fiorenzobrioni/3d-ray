# 📋 DEVLOG — 3D-Ray

Roadmap, lavori in corso, bug noti, storico cicli.

> Stati: `✅ Fatto` · `🔧 In corso` · `⬜ Da fare`

---

## 📌 Note rapide

- Aggiornare reference e tutorial quando si aggiunge una primitiva o una feature.
- Creare 2 starter-kit con effetti volumetrici/nebbia; aggiornare i README di `starter-kits` e `libraries`.
- ChessGen: riallineare al formato YAML attuale e generare `template + instance` per pedina.
- Valutare un flag CLI `--quiet` o `--verbose` per ridurre il log di default (children counts, mesh stats fuori da default).
- Spezzare `SceneLoader.cs` (file troppo grande).
- Idea scena: macro orologio meccanico (Annulus + Cylinder) con DOF spinta.

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

### Fase 5 — Frontiera ⬜

| # | Feature |
|---|---------|
| 23 | Bidirectional Path Tracing (dopo #13) |
| 24 | Spectral Rendering (lunghezze d'onda → dispersione prismatica) |
| 25 | Displacement Mapping (height map runtime tessellation, dopo #7) |
| 26 | GPU Acceleration (CUDA/Vulkan, progetto separato) |

### Dipendenze chiave

```
#3 Image Tex ─► #5 Normal Map, #9 Mix Material
#6 Disney   ─► #20 SSS
#7 OBJ      ─► #22 Instancing, #25 Displacement
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
| 2. MNEE (Manifold Next Event Estimation) | Caustiche focalizzate single-bounce attraverso una specular (lenti, bicchieri d'acqua, finestre). Cycles 3.2 "Shadow Caustics" | 2-3 settimane | ⬜ |
| 3. SPPM / VCM (photon mapping) | Caustiche multi-bounce, dispersive, indipendenti da differenziabilità della geometria. RenderMan PxrVCM | 6-10 settimane (SPPM) / 3-5 mesi (VCM) | ⬜ |

**Strada 1 — Transparent shadow rays ✅** Implementata. Lo shadow ray attraversa superfici trasmissive accumulando `(1 − Fresnel) · tint` per canale + Beer-Lambert `exp(−σ_a · d)` sul segmento interno fra entrata e uscita. Helper `Geometry/ShadowRay.Transmittance` con cap di 8 traversate; override `IMaterial.ShadowTransmittance` + `IMaterial.ShadowAbsorption` su `Dielectric` (no σ_a) / `DisneyBsdf` (entrambi quando `spec_trans > 0`) / `MixMaterial` (blend di entrambi).
**Limiti residui**: nessuna rifrazione dello shadow ray (no caustiche di lente in NEE); `roughness > 0` con `spec_trans > 0` (frosted glass) ignorata — lo shadow ray va dritto come vetro liscio. Per entrambi servono MNEE (Strada 2) o SPPM/VCM (Strada 3).

**Strada 2 — MNEE ⬜** Walker Newton-Raphson sulla manifold della superficie speculare; cerca un cammino `x → y_spec → light` che soddisfi Snell. Single-vertex robusto (Hanika/Droske/Manzi 2015, riferimento Cycles/Mitsuba).
**Pro**: unbiased, niente seconda passata, zero memoria extra, MIS-friendly, 10-100× più veloce del PT puro per caustiche.
**Contro**: limitato a 1 (forse 2) interfacce in serie; richiede normali differenziabili (`∂n/∂u`, `∂n/∂v`); fallisce su mesh hard-edge e bordi CSG (skip senza bias).
**Lavoro stimato**: nuovo `IManifoldGeometry` con derivate parametriche su `Sphere`/`Cylinder`/`Cone`/`Torus`/`Disk`/`Quad`/`SmoothTriangle`; `Rendering/ManifoldWalker.cs` (~400 righe); hook in `ComputeDirectLighting`; opt-in YAML `caustic_caster`/`caustic_receiver` per non sprecare campioni dove non serve. Test analitico (sfera-vetro vs piano, soluzione closed-form) come `BvhEquivalenceTests` per il BVH.

**Strada 3 — SPPM/VCM ⬜** Pass 1 emette fotoni dalle luci e li deposita su superfici diffuse in un kd-tree; pass 2 fa density estimation durante il rendering. SPPM raffina il raggio progressivamente per convergenza unbiased; VCM combina BDPT vertex connections + photon merging via MIS (Georgiev 2012, gold standard).
**Pro**: caustiche multi-bounce, dispersive (con spectral), indipendenti dalla geometria; beneficio collaterale su indirect diffuse (final gathering accelerato).
**Contro**: due pass in serie (cambia l'orchestrazione del Renderer); 50-500 MB di memoria per il photon map; dispersione richiede upgrade spettrale a monte (RGB-only sbaglia il prisma); separazione delta-vs-diffuse nel cammino fotone è sottile.
**Lavoro stimato**: `Acceleration/PhotonMap.cs` (~600 righe, kd-tree con range query); `Rendering/PhotonEmitter.cs` (~400 righe); modifica profonda di `Renderer` (fase build prima del shading); CLI `--photons N --photon-radius r --sppm-iterations n`; profilo Caustic.

**Decisione corrente**: Strada 1 sufficiente per i casi d'uso showcase quotidiani; Strada 2 è il candidato per quando si vorranno caustiche pulite delle lenti in `csg-showcase.yaml`; Strada 3 resta opzione lunga-roadmap (utile solo se servono caustiche multi-bounce/dispersive).

---

## ✅ TODO

- [x] ~~Valutare se coerente: aggiungere la possibilita tramite una nuova proprieta per le luci visibili nella scena (tipo ad es. le luci `sphere`) per renderle visibili o meno dal punto di vista della camera, oltre che per l'illuminazione.~~ Fatto: `visible_to_camera` (Arnold/Cycles "camera" / Cycles "Ray Visibility → Camera") su `lights:` (sphere/area) e `entities:` (qualunque oggetto, utile per emissive panels). Vedi voce 2026-05 nello storico.
- [ ] Valutare se coerente: aggiungere proprietà `focal_pos: [x, y, z]` (alternativa a `focal_dist`) per la `camera` per indicare la posizione del fuoco al posto della distanza. Per semplicita' in certe situazioni potrebbe essere comodo indicare direttamente la posizione del fuoco.
- [ ] Texture più realistiche di quelle attuali per legno marmo e altri materiali (soprattutto per il legno che ora è poco realistico). Devono essere come nei ray tracer professionali tipo Arnold, Cycles, Renderman, ecc.
- [ ] Refactoring: spostare `Seed` da `IHittable` a un'interfaccia dedicata (es. `ISeeded`); nodi strutturali (BvhNode, Transform) non hanno bisogno di seed.
- [ ] Review completa dei tutorial (`tutorial/`): correttezza vs codice, omissioni, grammatica, esempi, indici.
- [ ] Spezzare `SceneLoader.cs`.

---

## 🐛 Bug noti

| # | Descrizione | Severità | Stato |
|---|-------------|----------|-------|
| 2 | RNG globale seedato da `Environment.TickCount` (`MathUtils.cs:12`): due render della stessa scena producono rumore stocastico diverso. A spp ≥ 64 il rumore si media; a spp basso le differenze sono visibili. Non riguarda i pattern procedurali (bug #1 risolto). Architettura proposta: sampler per-pixel deterministico via hash `(pixelX, pixelY, sampleIndex)`. Da affrontare con #15 Tile-based. | 🟠 Media | ⬜ |

---

## 📚 Riferimenti tecnici

### RNG globale e determinismo (riferimento bug #2)

`MathUtils.cs:12-17` espone un RNG thread-local seedato da `Environment.TickCount`. Usato per BSDF / NEE / DoF / RR / jitter, quindi ogni esecuzione parte da uno stato diverso → pixel diversi tra render della stessa scena.

**Quando dà fastidio**: visual regression testing, A/B di parametri (diff = parametro + rumore), animazioni (flickering temporale tra frame), debug ("perché questo pixel?").

**Architettura proposta**:
1. `Sampler` per-pixel che incapsula lo stato RNG, seedato da hash deterministico `(pixelX, pixelY, sampleIndex)` (PCG / xoshiro / splittable RNG).
2. Propagare il `Sampler` lungo `TraceRay` / `Scatter` / `EvaluateDirect` / `SampleLight` come parametro esplicito (rimuove `MathUtils.Rng` singleton).
3. CLI `--render-seed N` (default 0 → render identici; override per variazione intenzionale tra frame).

**Impatti**: output bit-identico, smoke test verificabile contro baseline (anche hash dell'immagine), rimozione del counter atomico globale, miglior cache locality con #15 Tiles. L'immagine "finale" della scena cambierà rispetto ai render attuali.

**Dipendenza**: meglio dopo (o insieme a) #15 Tile-based, perché il tile è il granulo naturale per il seeding per-pixel.

---

## 🗓️ Storico cicli

Sintesi cronologica dei cicli grossi. Per i dettagli matematici e i riferimenti vedi i doc tecnici puntati.

- **2026-05 — `visible_to_camera` (camera visibility flag).** Branch `claude/implement-renderer-integration-QsPuK`. Allineamento ad Arnold (`camera` visibility) e Cycles ("Ray Visibility → Camera"): proxy di `sphere`/`area` light e qualunque entity (utile su emissive panels) possono essere nascosti dai raggi primari mantenendo NEE, riflessioni speculari, rifrazioni e indirect attivi. Implementazione: nuovo `Geometry/CameraInvisibleHittable` (modello `BackFaceCulledHittable`), nuovo flag `HitRecord.CameraInvisible`, skip-loop in `Renderer.TraceRay` con cap di 8 attivo solo su `depth == _maxDepth` (chiusura MIS di Veach preservata: proxy resta nel BVH per i non-primari). YAML `visible_to_camera: bool = true` su `LightData` ed `EntityData`. Showcase `scenes/showcases/visible-to-camera-showcase.yaml`, regression `CameraVisibilityTests` (4 test). Doc: scene-reference §7 (campi comuni entity), §8.4/§8.5.

- **2026-05 — Transparent shadow rays.** Branch `claude/fix-transparent-shadows-4n8Ph` (PR #50). Strada 1 della roadmap caustiche. Lo shadow ray attraversa superfici trasmissive con `(1 − F) · tint` per interfaccia + `exp(−σ_a · d)` Beer-Lambert sul segmento interno; nuovo `Geometry/ShadowRay.Transmittance`, override `IMaterial.{ShadowTransmittance, ShadowAbsorption}` su `Dielectric` / `DisneyBsdf` (entrambi quando `spec_trans > 0`) / `MixMaterial`; tutti gli 8 light dispatcher aggiornati. Vetri colorati con `transmission_depth > 0` (rubino, smeraldo, ambra, zaffiro) ora proiettano ombra tinta. Doc: `docs/technical/path-tracing-and-lighting.md` §2.3.

- **2026-05 — Extrusion primitive.** Nuovo `Extrusion : IHittable, ISamplable` (linear / catmull_rom / bezier, twist + taper, caps configurabili, BVH interno) + utility 2D `Polygon2D` (signed area, ear clipping, tessellation chiusa). Doc: scene-reference §7.17. 9 test nuovi (155 totali).

- **2026-05 — World/Sky cleanup.** Branch `claude/review-ambient-light-sky-vX2YO`. Rimosso il termine `world.ambient_light` non-fisico (sommato come radianza grezza in `ComputeDirectLighting`, fuori da BRDF/coseno). Sostituito `world.background` con `world.sky.type: flat`. Aggiunto NEE flat-sky uniforme (`pdf = 1/(4π)`) allineato a Cycles/Arnold. Test guard-rail `BlackLambertianFloor_RendersBlack_WithBlackSky`. Breaking YAML.

- **2026-04 — Light hardening.** Branch firefly-killer su `Lights/`. `SoftRadius` su Area/Sphere/Geometry (clamp `distSq = max(., r²)`). Depth-aware indirect clamp (`--indirect-clamp-factor`). LightDistribution power-CDF (`--light-sampling`). Sun disc su DirectionalLight (`angular_radius`). ShadowSamples + softRadius su SpotLight. `ISamplable.SurfaceArea` deterministico → niente PRNG nel costruttore di GeometryLight (scene-load 100% deterministico). 5 test regressione firefly nuovi.

- **2026-04 — MIS completion.** Branch `claude/complete-mis-implementation-q98Tb`. MIS esteso a tutti i materiali (Lambertian, Metal, MixMaterial, oltre a Disney) e alla phase function volumetrica. CLI `--mis <balance|power>`. Test `MisMaterialsTests` (140/140). Doc: `multiple-importance-sampling.md`.

- **2026-04 — Disney BSDF & sampling review.** Branch `claude/review-disney-materials-QO2mO`. Tutti i parametri Disney mancanti (`anisotropic`, `transmission_color`/`depth`, `thin_walled`, `diff_trans`, `flatness`, `subsurface_color`, `coat_*`, `sheen_roughness`, `thin_film_*`). Kulla-Conty LUT, VNDF (Heitz 2018), Charlie sheen (Estevez-Kulla 2017), thin-film (Belcour-Barla 2017), Sobol+Owen sampler. Cleanup `IMaterial` (rimossi campi Blinn-Phong, sostituiti da `NeedsDirectLighting` / `IsDeltaScatter`).

- **2026-04 — Scene classification refactor.** Branch `claude/review-raytracer-rendering-7hQpu`. Sostituito `ILight.Illuminate(Vector3.Zero)` con `ApproximatePower(AABB)` deterministico, receiver-independent. Finite-scene bounds helper (filtra `InfinitePlane` ±1e6 che inflava R a ~1700). Threshold ricalibrata 1.0 → 0.5. Doc: `rendering-pipeline.md` §2.1.

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
