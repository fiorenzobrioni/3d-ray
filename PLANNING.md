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
| 15 | Tile-based Rendering (tile 16×16, progress su thread reporter dedicato) | ✅ |
| 16 | Denoiser (bilateral/NLMeans guidato da normal/albedo/depth) | ⬜ (dopo #15) |
| 17 | HDR Output (PFM/EXR pre-tone-mapping) | ⬜ |
| +  | Sobol + Owen Scrambling sampler (`--sampler sobol`, default attivo) | ✅ |

### Fase 4 — Cinematografici 🔧

| # | Feature | Stato |
|---|---------|-------|
| 18 | Motion Blur | ⬜ |
| 19 | Volumetric Rendering | 🔧 Stage 1 + 1.5 ✅ |
| 20 | Subsurface Scattering | ✅ |
| 21 | CSG (union/intersection/subtraction, all-hits, normali corrette) | ✅ |
| 22 | Instancing (geometria condivisa, override material/seed per-istanza) | ✅ |
| +  | Extrusion primitive (linear/catmull_rom/bezier + twist + taper + caps) | ✅ |
| +  | Transparent shadow rays (vetro Fresnel-tinted in NEE — Strada 1) | ✅ |

**Volumetrics Stage 1+1.5**: medium globale opt-in (`world.medium`), output bit-identico se assente. `IMedium`: Homogeneous (Beer-Lambert + free-path), HeightFog (densità esponenziale closed-form), HeterogeneousProcedural (Perlin fBm, delta+ratio tracking), Grid (`.vol` o inline, slab clip + trilinear + delta tracking). `IPhaseFunction`: Isotropic, HG, Rayleigh, Double-HG (Nubis), Schlick (fast-HG). Stage 2 (deferred): EmissiveMedium, MediumInterface per-entity, SSS random-walk, OpenVDB nativo, spectral tracking — tutti richiedono modifiche ad `IMedium` / `HitRecord` / `Renderer.TraceRay`.

### Fase 5 — Frontiera 🔧

| # | Feature | Stato |
|---|---------|-------|
| 23 | Path Guiding — PPG-style (SD-tree spaziale + quadtree direzionale per foglia, MIS tra guide PDF e BSDF PDF, `--path-guiding on\|off`, `--guiding-budget N` spp di training; dopo #13) | ⬜ |
| 24 | Spectral Rendering (lunghezze d'onda → dispersione prismatica) | ⬜ |
| 25 | Surface Displacement Stack (bump map, mesh subdivision Loop/Catmull-Clark, scalar/vector displacement, autobump) | ✅ |
| 26 | GPU Acceleration (CUDA/Vulkan, progetto separato) | ⬜ |

### Dipendenze chiave

```
#3 Image Tex ─► #5 Normal Map, #9 Mix Material
#6 Disney   ─► #20 SSS
#7 OBJ      ─► #22 Instancing, #25 Displacement ✅
#11 Scene G ─► #22 Instancing
#12 IS      ─► #13 MIS ─► #23 Path Guiding
#15 Tiles   ─► #14 Adaptive, #16 Denoiser
```

---

## 🌟 Caustiche ✅

**Completate** — caustiche via **photon mapping** (un solo opt-in: `--caustics on`, zero flag YAML). Pre-pass di emissione fotoni da tutte le luci (area/geometriche, sphere, point/spot **e** directional/sole), trasporto attraverso le interfacce speculari e density estimate k-nearest al gather. Generale su qualsiasi geometria speculare. Dettagli, motivazioni e limiti residui in DEVLOG §Ciclo Caustiche — Photon Mapping.

---

## 📋 TODO

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

Ottimizzazioni valutate nel ciclo Review e **rimandate** (da riprendere):
- **Traversata BVH a stack esplicito** — richiede un array di nodi appiattito +
  stack di indici locale (`stackalloc int[]`, immune a rientranza) invece dello
  stack di riferimenti condiviso per-thread, che si corromperebbe con i Group
  annidati. Vale come step verso adaptive sampling/packet tracing.
- **Clamp di ottave footprint-aware per Marble/Wood** — come `NoiseTexture.ComputeMaxOctaves`:
  riduce ottave (e aliasing) a distanza. Modifica di anti-aliasing più ampia, da
  validare su scene marmo-pesanti.
- **`HitRecord` più snello** — spostare `FilterFootprint`/`MediumInterface`/autobump
  fuori dallo struct core e calcolarli pigramente dopo l'hit finale.

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
