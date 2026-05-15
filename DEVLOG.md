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
- [x] ~~Valutare se coerente: aggiungere proprietà `focal_pos: [x, y, z]` (alternativa a `focal_dist`) per la `camera` per indicare la posizione del fuoco al posto della distanza. Per semplicita' in certe situazioni potrebbe essere comodo indicare direttamente la posizione del fuoco.~~ Fatto: `focal_pos` su `CameraData`, distanza calcolata come proiezione del vettore camera→punto sull'asse ottico (Arnold "Focus Object"/Cycles "Focal Object"/RenderMan). Vedi voce 2026-05 nello storico.
- [x] ~~Texture più realistiche (devono essere come quelle dei ray tracer professionali tipo Arnold, Cycles, Renderman, ecc.) di quelle attuali per legno, marmo, noise e tutti le texture gestite finora. Aggiungi eventuali texture ora assenti ma presenti in ray tracer come Arnold, Cycles, Renderman, ecc.. Implementazione pro a livello di Arnold, Cycles, Renderman, ecc.. Senza compromessi. Aggiungi nuova scena showcase e aggiorna anche reference scene IT e EN e i tutorial IT e EN.~~ Fatto: upgrade pro di noise/marble/wood (octaves/lacunarity/gain/distortion + assi/sharpness configurabili) e nuove texture `voronoi`, `brick`, `gradient`. Vedi voce 2026-05 nello storico.
- [ ] Refactoring: spostare `Seed` da `IHittable` a un'interfaccia dedicata (es. `ISeeded`); nodi strutturali (BvhNode, Transform) non hanno bisogno di seed.
- [ ] Review completa dei tutorial (`tutorial/`): correttezza vs codice, omissioni, grammatica, esempi, indici.
- [ ] Spezzare `SceneLoader.cs`.
- [ ] **Stack completo "surface displacement" livello Arnold/RenderMan/Cycles** — deformazioni superficiali visibili con parità feature ai render pro. Da sviluppare come unico ciclo a step incrementali, ciascuno utile da solo: (1) **Bump map scalare da `ITexture` qualunque** (procedurali + immagini): nuovo canale `bump_map: { texture: …, strength: …, scale: … }` sui materiali, perturbazione della normale geometrica via differenze finite di luminanza in tangent-space (TBN già esistente da #5 normal mapping); funziona su tutte le primitive senza modifiche al BVH — sblocca subito le texture pro (#27) come dettagli superficiali. (2) **Mesh subdivision** (Loop su tri, Catmull-Clark opzionale su quad) sul loader OBJ con `subdivision_iterations` e/o `subdivision_pixel_error` (adattivo screen-space). (3) **Scalar displacement vero** sulla mesh subdivisa (`v += h(u,v) · n_smooth`), con `displacement_bound` per gonfiare gli AABB del BVH così i micro-poligoni spostati non scappano dal box originale — questo è il #25 della Fase 5 e fornisce le silhouette modificate, non solo lo shading. (4) **Vector displacement** (texture RGB → offset XYZ) per overhangs e crinkles, riusando l'infrastruttura di (3). (5) **Combinazione bump + displacement** (autobump-like di Arnold: il displacement gestisce la macro-silhouette, il bump residuo i dettagli sub-pixel) e priorità di applicazione coerente con Disney BSDF (`coat_normal_map` indipendente già esistente, base normal/bump/displacement compongono il `n_shading` finale). Vincolo architetturale: il displacement vero resta limitato alle mesh — sphere/torus/cylinder/ecc. supportano solo bump (stessa scelta di Arnold/Cycles). Doc + showcase + tutorial EN/IT per ogni step.

- [ ] **Texturing "VFX production-grade": parità completa con Arnold/RenderMan/Cycles oltre la matematica core.** I primitivi di noise (Perlin, fBm, ridged, billow, Worley, marble, wood, brick, gradient) usano già le equazioni canoniche dei testi di riferimento (Perlin 1985/2002, Ebert-Musgrave-Peachey-Perlin "Texturing & Modeling", Worley 1996, IQ domain warp), e per un singolo sample il risultato è equivalente. Mancano però gli strati di infrastruttura e tooling che separano un raytracer "decente con texture credibili" da uno "pronto per VFX dove le texture devono reggere zoom 4K, movimento camera, e workflow di lookdev". Da sviluppare come unico ciclo a step incrementali ordinati per impatto visivo decrescente, ciascuno utile da solo:

  (1) **Anti-aliasing analitico con filter footprint (ray differentials).** È il fix più urgente: oggi le procedurali sono **point-sampled**, a distanza producono moiré/shimmer dove Arnold/RM mostrerebbero sfumatura. Estendere `Ray` con ray differentials `(∂P/∂x, ∂P/∂y, ∂D/∂x, ∂D/∂y)` in screen-space come in PBRT §10.1, propagati attraverso `Hit()` di ogni primitiva (`sphere`/`cylinder`/`torus`/`cone`/`quad`/`triangle`/`disk`/`annulus`/`capsule`/`lathe`/`extrusion`/`CSG` — formula chiusa per ciascuna, le derivate sono già implicite nelle parametrizzazioni esistenti) e attraverso `Transform` (Jacobiana = matrice inversa-trasposta). Estendere `ITexture` con overload `Value(u, v, p, seed, FilterFootprint footprint)` con default che fa pass-through alla versione point-sampled corrente (back-compat). Implementazioni filtered: **Perlin/fBm** via clamp ottave a `λ = ⌊log₂(1/maxAxis(footprint))⌋` (Heidrich-Slusallek 1998 "Improved Perlin Noise" §4) — sopra Nyquist le ottave alte vengono droppate analiticamente; **Worley** via supersampling adattivo 4-16 jitter samples in footprint (`PxrVoronoise` fa lo stesso); **ImageTexture** via mipmap pyramid generata in ctor + EWA filtering (Heckbert 1989) per anisotropia corretta a basso angolo. CLI flag `--texture-filtering on|off|auto` (default `auto`). Test: rendering `textures-pro-showcase.yaml` a 4K, 16 spp → confronto rumore vs baseline 256 spp; risultato atteso: stesso aspetto. Bench: `TextureFilteringBench` per costo per-sample.

  (2) **Color ramp multi-stop.** Nuova classe `ColorRamp` esposta come blocco YAML opzionale `color_ramp: [...]` al posto del semplice `colors:` su qualunque texture procedurale. Lista di stop `{ position: float ∈ [0,1], color: [r,g,b], interp: "linear" | "smoothstep" | "constant" | "ease" }`. Applicato in `ITexture.Value` dopo il calcolo del valore scalare di noise (sostituisce il `Vector3.Lerp(colorA, colorB, t)` finale di noise/marble/wood/voronoi/gradient — brick ha tre colori specifici e resta indipendente o usa ramp 3-stop). Esempio YAML:
  ```yaml
  texture:
    type: "marble"
    color_ramp:
      - { position: 0.00, color: [0.05, 0.05, 0.07], interp: "linear" }
      - { position: 0.45, color: [0.95, 0.93, 0.88], interp: "smoothstep" }
      - { position: 0.55, color: [0.95, 0.93, 0.88], interp: "linear" }
      - { position: 1.00, color: [0.05, 0.05, 0.07], interp: "linear" }
  ```
  `colors:` resta come scorciatoia equivalente a un ramp a 2 stop (back-compat totale). Sblocca: marmo Statuario con vena dorata/nera/grigia, wood con sapwood/heartwood/knot 3-color, gradient artistici complessi.

  (3) **Smooth Voronoi.** Aggiungere `smoothness ∈ [0,1]` a `VoronoiTexture`: quando > 0 sostituisce `min()` su F1 con soft-min `-log(Σ exp(-k·d_i)) / k` (k = 20/smoothness, IQ "Smooth Voronoi"); F2-F1 con smoothness > 0 diventa "smooth crackle" (bordi morbidi, niente alias a step). Utile per cuoio levigato, ciottoli arrotondati, pelle di rettile più realistica. Showcase: tre sfere `hard / smooth=0.3 / smooth=0.7`.

  (4) **Musgrave multifractal completo.** Aggiungere a `Perlin` i metodi `HeteroTerrain(p, octaves, lacunarity, H, offset)` e `HybridMultifractal(p, octaves, lacunarity, H, offset)` da Musgrave "Texturing & Modeling" §16. Parametro `H` (fractal increment, controlla roughness vs altitudine), `offset` (sea-level/threshold). Esposti in `NoiseTexture` come `noise_type: "hetero_terrain"` e `"hybrid_multifractal"` con i parametri YAML `fractal_increment` (H) e `fractal_offset`. Sblocca terreni proceduralmente erosi (terra a quote diverse con roughness diversa) e pattern roccia stratificati irraggiungibili con fBm puro.

  (5) **Marble e Wood "studio quality".** Upgrade dei due shader esistenti senza breaking change:
  - **Marble**: blocco YAML opzionale `secondary_wave: { axis: [...], frequency: ..., strength: ... }` per stratificare una seconda sinusoide ortogonale alla principale → marmi a doppia direzione di venatura (Statuario, Calacatta, Arabescato). La somma `sin(wave1) + 0.5 · sin(wave2)` produce un campo non più rigidamente unidirezionale. Usa il color ramp del punto (2) per vena/base/sotto-tinta a 3+ stop.
  - **Wood**: separazione di `grain_scale` (alta freq, dettaglio fibra interna agli anelli) e `figure_scale` (bassa freq, ondulazione tavola tipo curly maple), entrambi pesati indipendentemente da `grain_strength` / `figure_strength`. Aggiunta di `radial_anisotropy: float` che stretchera il noise lungo l'asse radiale vs tangenziale (rovere quartato = anisotropia alta, piano-sawn = bassa). Sapwood/heartwood gradient via color ramp 3-stop dal punto (2). Optional `knot_density` per spawn casuale di nodi via Voronoi piccolo-scala mascherato.

  (6) **F3/F4 e output Voronoi estesi.** Aggiungere F3, F4 a `WorleyNoise.Evaluate` (mantiene complessità O(27) sulle 27 celle, costo marginale poiché già si scansionano), e nuovi `OutputMode.F3`, `F4`, `F3MinusF1`, `Position` (posizione XYZ del feature più vicino come RGB — utile per shading per-cella o per modulare un'altra texture). Cycles e Houdini espongono F3/F4 di default; in pratica raro ma necessario per cellulare gerarchico e shading custom.

  (7) **Coordinate texture node.** Nuova `CoordinateTexture` che ritorna `(p.x, p.y, p.z)` o `(u, v, 0)` come RGB, con `mode: "world" | "object" | "uv" | "generated"` e trasformazione (offset/rotation/scale) standard. Analogo al "Texture Coordinate" node di Cycles. Utile per debug visivo dei UV/coord-spaces e per pilotare altre texture (texture-driven mix masks già supportato ma quello è specifico per il mix material; questo è generico). Bassa priorità — QoL per artisti.

  Per ogni step: aggiornare `docs/reference/scene-reference.md` + `riferimento-scene.md` + `docs/tutorial/{en,it}/03-materials.md`, aggiungere showcase dedicato o sezione in `textures-pro-showcase.yaml`, regression test dove ha senso (es. `RayDifferentialTests` per (1) — proiezione di footprint noto attraverso transform e verifica numerica; `ColorRampTests` per (2) — interpolazioni su stop noti; `SmoothVoronoiTests` per (3) — continuità verificata su griglia). Validazione finale: rendering `textures-pro-showcase.yaml` a 1920×1080 e 4K con 64 spp, confronto con baseline pre-cambio sulla stessa scena (`renders/textures-pro-showcase.png` attuale è il punto di partenza). Voce DEVLOG storica a fine ciclo che elenca tutti gli step completati e i file toccati. Vincoli architetturali: back-compat totale (le scene YAML esistenti devono renderizzare identiche se non usano le nuove feature); tutti i 187+ test esistenti devono continuare a passare; nessun degrado di performance > 5% sui benchmark esistenti (`dotnet run -c Release --project src/RayTracer.Benchmarks -- --filter '*'`).

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

- **2026-05 — Bump map (step 1/5 surface displacement stack).** Branch `claude/surface-displacement-implementation-Gyg0x`. Primo step della roadmap "Stack completo surface displacement livello Arnold/RenderMan/Cycles": nuovo canale `bump_map: { texture, strength, scale }` su tutti i materiali (Lambertian, Metal, Dielectric, Emissive, DisneyBsdf, MixMaterial). La normale di shading è perturbata in tangent space via differenze centrate (Blinn 1978, du=dv=1e-3) della luminanza Rec.709 della texture interna; ogni sample perturba sia (u,v) sia il punto 3D `p` lungo T/B, così sia le texture procedurali 3D (noise/marble/wood/voronoi) sia le texture image 2D producono un gradiente coerente. Compone con `normal_map` (applicato prima) con re-ortogonalizzazione Gram-Schmidt della TBN contro la N già perturbata (convenzione Arnold/Cycles); il `coat_normal_map` Disney resta indipendente per design. Funziona su tutte le primitive (Sphere, Box, Cylinder, Cone, Quad, Disk, Annulus, Torus, Capsule, Lathe, Triangle, SmoothTriangle, InfinitePlane — tutte popolano `rec.Tangent`/`rec.Bitangent`). Nuovo `Textures/BumpMapTexture` (clamp strength [0,10], scale>0); nuovo `BumpMapData` YAML + dispatcher `LoadBumpMap` in `SceneLoader` che riusa `CreateTexture()`; hook `ApplyBumpMap` in `Renderer.ShadeSurface` subito dopo `ApplyNormalMap`. Showcase `scenes/showcases/bump-map-showcase.yaml` con 4 quad fianco a fianco (flat reference / noise fBm / marble veins / image concrete) sotto luce point radente. 9 unit test in `BumpMapTests` (luminance reduction, gradient direzione, clamp strength/scale, scale-shifts-sample-point). Doc: `scene-reference.md` §6, `riferimento-scene.md` §6, tutorial §3.11 (EN+IT). Back-compat: 196/196 test, omettendo `bump_map` le scene rendono identiche. **Step rimanenti del TODO genitore (2-5: subdivision, scalar displacement, vector displacement, autobump combination): cicli successivi.**


Sintesi cronologica dei cicli grossi. Per i dettagli matematici e i riferimenti vedi i doc tecnici puntati.

- **2026-05 — Pro-grade procedural textures.** Branch `claude/implement-devlog-todo-nz1Ps`. Allineamento di noise/marble/wood ad Arnold (`noise`, `marble`), Cycles (Noise/Voronoi/Brick/Gradient) e RenderMan (`PxrFractal`, `PxrVoronoise`, `PxrMarble`, `PxrTile`). Engine: `Perlin.Fbm/Ridged/Billow/NoiseVector` + nuovo `Core/WorleyNoise` (F1/F2 con metriche euclidean/manhattan/chebyshev + cell-id hash). `NoiseTexture` ora con `noise_type` (perlin/fbm/turbulence/ridged/billow), `octaves`, `lacunarity`, `gain`, `distortion`; `MarbleTexture` con `vein_axis`, `vein_frequency`, `vein_sharpness` (Carrara look ≥4), `noise_type` modulator, `distortion`; `WoodTexture` con `ring_axis`, `ring_sharpness` (triangular-wave sharpening), `axial_grain`, `octaves` fBm, `distortion`. Nuove `VoronoiTexture` (output f1/f2/f2_minus_f1/f1_plus_f2/cell), `BrickTexture` (running-bond + per-brick color variation + weathering noise), `GradientTexture` (linear/quadratic/easing/spherical/radial). Backward-compat preservato: omettendo i nuovi parametri le scene legacy renderizzano identiche (NoiseStrength=0 → Perlin, NoiseStrength>0 → 7-octave Turbulence; marble VeinSharpness=1 → output legacy; wood Octaves=1 → single-Perlin legacy). SceneLoader: extension a `TextureData` (24 nuovi campi), dispatch su alias `voronoi|worley|cell|cellular`, `brick|bricks|tile`, `gradient|ramp`. Showcase `scenes/showcases/textures-pro-showcase.yaml` (3×3 sfere su pavimento marmo). Doc: `scene-reference.md` §6, `riferimento-scene.md` §6, tutorial §3.8 (EN+IT). 187/187 test passano.

- **2026-05 — `focal_pos` (camera focus point).** Branch `claude/implement-renderer-integration-QsPuK`. Allineamento ad Arnold "Focus Object", Cycles "Focal Object" e RenderMan: la camera può specificare un punto 3D `focal_pos: [x, y, z]` invece dello scalare `focal_dist`. Il loader (`SceneLoader.ComputeFocusDistance`) calcola la distanza come **proiezione** del vettore camera→focal-point sull'asse ottico (`(F − P) · normalize(lookAt − P)`) — quindi un punto off-axis a `(3, 4, −5)` con camera all'origine guardando `−Z` produce focus distance `5`, non `√50`. Precedenza: `focal_pos` vince su `focal_dist`; warning + fallback su input degeneri (punto dietro la camera, camera con `lookAt == position`, lista YAML malformata). `Camera` invariata (single-source-of-truth sullo scalare `focusDist`). Showcase `scenes/showcases/focal-pos-showcase.yaml` con 4 camere (near/middle/far + sanity A/B equiv via `focal_dist`). 9 unit test in `CameraFocalPosTests`. Doc: scene-reference §4, tutorial §7.5 (EN+IT).

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
