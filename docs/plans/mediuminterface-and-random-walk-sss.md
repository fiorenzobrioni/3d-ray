# MediumInterface + Random Walk SSS — Piano di Implementazione

## Stato

| Fase | Descrizione                                                | Stato            |
|------|------------------------------------------------------------|------------------|
| 1    | MediumInterface plumbing (no SSS yet)                      | ✅ Completata    |
| 2    | YAML schema + libreria mediums + clean-break Disney        | ✅ Completata    |
| 3    | Random Walk SSS integrator                                 | ✅ Completata    |
| 4    | CLI, quality presets, full MediumInterface use cases       | ✅ Completata    |
| 5    | Tests, scenes, docs                                        | ⬜ Pending       |

### Fase 1 — log

- `MediumInterface` struct, `MediumStack` (InlineArray8 zero-alloc), `MediumBoundHittable` wrapper aggiunti.
- `HitRecord.MediumIface` campo aggiunto (default = empty).
- `BsdfSample.Transition` enum (`None|Enter|Exit`) aggiunto accanto al legacy `NextSegmentAbsorption`.
- `DisneyBsdf.ScatterTransmission` emette `Transition` su refraction front/back face.
- `SceneData.Mediums` (libreria con `id`), `EntityData.InteriorMedium`/`ExteriorMedium` aggiunti.
- `SceneLoader.BuildGlobalMedium` → `BuildMedium` riusabile + medium-library con duplicate-id warn + wrap entity con `MediumBoundHittable` se binding presente.
- `Renderer.TraceRay` / `ShadeSurface` / `ShadeSampleBounce` con `ref MediumStack mediums` plumbed. `activeMedium = mediums.Top ?? _globalMedium`. Copy-on-write della stack al transmission. `ComputeDirectLightingMedium` accetta ora il medium parametro.
- Back-compat: `currentAbsorption` legacy resta in funzione → scene `cristallo.yaml` e Disney `transmission_color`+`transmission_depth` invariate.
- Test: 9 nuovi (`MediumStackTests`, `MediumBoundHittableTests`); 464+9 = 473 verdi.
- Smoke render: `cristallo.yaml`, `volumetric-01-homogeneous.yaml`, scena E2E sintetica con `interior_medium` — tutti OK.

### Fase 2 — log

- **Disney clean break**: rimossi `Subsurface`, `SubsurfaceColor`, `Flatness` (proprietà, ctor params, ShadingParams fields, EvalParams binding). Rimosso `ResolveSubsurfaceColor` helper. Il lobo diffuse Disney è ora Lambert+Burley retro-reflection puro; `diff_trans` continua a esistere ed è tintato da `baseColor` (Cycles-style).
- I YAML field `subsurface`, `subsurface_color`, `subsurface_radius`, `flatness` (+ `*_texture` variants) restano parsabili in `MaterialData` (compat YAML); il loader emette un `Warn` se non-default e ignora il valore.
- `SceneLoader` Disney constructor invocation aggiornato (rimossi binding subsurface/subsurfaceColor/flatness; il `Verbose` su `subsurface_radius` diventa un `Warn` consolidato).
- **Tool migrazione**: nuovo `src/Tools/MigrateFakeSss/` (eseguibile, non in solution). Strip line-based per scalar + block field (`subsurface_texture:` ecc), report dettagliato, `--dry-run` opzionale. Eseguito su `scenes/`: **23 YAML modificati, 491 righe rimosse**.
- File impattati e ripuliti: librerie `materials/plastics.yaml`, `materials/weathering.yaml`, `materials/leathers.yaml`, `materials/organics.yaml`, `materials/foods.yaml`, `materials/glasses.yaml`, `terrains/heightfield-strata-test.yaml`, e diverse scene applicative (tempio-romano.yaml ecc).
- **Test cleanup**: rimossi 2 test puntuali su feature eliminata (`Flatness_FullyFlat_MatchesSubsurfaceFullyOn`, `SubsurfaceColor_OverridesBaseColorInFlatLobe`). Aggiornato `DiffTrans_ProducesBackHemisphereSamples_*` per usare `baseColor` come tint. 471 test verdi.
- Smoke render: `cristallo.yaml` (no legacy field — invariato), `tempio-romano.yaml` (marble migrato — render OK, look Lambert come atteso fino a Fase 3).
- **Effetto visivo**: scene con materiali "fake SSS" perdono il flat-blend e tornano a Lambert puro. È il comportamento atteso del clean break — il look fisicamente corretto torna in Fase 3 una volta abilitato Random Walk SSS via `interior_medium`.

### Fase 3 — log

- **Random walk integrator** (`Rendering/RandomWalkSss.cs` come `partial Renderer`): Cycles-style `random_walk_v2` con free-flight hero-wavelength sampling + balance-heuristic MIS spettrale sui 3 canali. Hero pick proporzionale a β[c], `t = -ln(ξ)/σ_t[hero]`, throughput update `β *= σ_s · exp(-σ_t·t) / Σ_c q[c] · σ_t[c] · exp(-σ_t[c]·t)` per evento di scatter (forma analoga senza σ_s per escape transmittance-only). Phase HG con sampler-density matching → phase/pdf = 1.
- **Restricted-BVH query**: `MediumBoundHittable` ora stampa `rec.EntityRoot = _inner` accanto a `rec.MediumIface`. Il walk usa `entityRoot.Hit(...)` per la boundary detection — niente leak in geometria adiacente.
- **Dispatch** in `Renderer.ShadeSampleBounce`: quando `s.Transition == Enter && nextMediums.Top is HomogeneousMedium && IsScatteringMedium && rec.EntityRoot != null && _sssMode == Auto`, sostituisce la chiamata a `TraceRay` con `RandomWalkSubsurface`. Altri casi (σ_s=0, no binding, SssMode.Off) restano sul path legacy.
- **SssMode.Off**: nuovo helper `ResolvePushedMedium` declassa il medium pushato a "absorption only" (σ_a preservato, σ_s azzerato) quando l'utente disabilita SSS via CLI. Beer-Lambert tinta lungo il segmento ancora applicato, niente eventi di scattering — preview rapidi e A/B comparison senza il costo del walk.
- **Hero-wavelength clamp**: nuovo `ClampWalkInScattering(L, b)` con ramp `_indirectMaxSampleRadiance / (1 + 0.1·b)` per smorzare fireflies in profondità (Cycles `clamp_walk_volume`).
- **Russian Roulette in-walk**: kicks da `b ≥ RrStartBounce` (default 3), `qRr = max(β.X, β.Y, β.Z)` clampato a [0.05, 0.95]. Max-bounces hard cap (default 64) come backup contro low-albedo che RR non termina abbastanza in fretta.
- **`HomogeneousMedium`** ora espone `SigmaA`/`SigmaS`/`SigmaT` come proprietà pubbliche (immutabili, già cached al construct), consumate dal walk.
- **`Renderer` ctor**: nuovi parametri `sssMode = Auto`, `walkConfig = Normal`. `RandomWalkConfig` struct con preset `Preview / Normal / High` pronto al wiring Fase 4 CLI.
- **Test**: 5 nuovi (`SssRandomWalkTests`): σ_s=0 fallback al path legacy, white-furnace energy conservation (η=1 matched IOR + σ_a=0), spectral color-bleed (σ_a R<G<B → R>G>B in output), dense-medium robustness (finite values, no NaN, max-bounces=8), SssMode.Off dispatch toggle. 471 + 5 = 476 verdi.
- **Smoke render**: `cristallo.yaml` e `chess.yaml` invariati (path legacy preservato). `tmp/sss-test.yaml` con marble sphere + `interior_medium: marble_int` (Jensen 2001 preset) renderizza correttamente — il look diffuso interno è ora trasportato dal walk anziché simulato dal flat-blend HK rimosso in Fase 2.

### Fase 4 — log

- **CLI** (`Program.cs`): aggiunti `--sss-mode auto|off` (default `auto`), `--sss-quality preview|normal|high`, `--max-volume-bounces N`. Help + banner di rendering aggiornati. Il banner stampa la linea "SSS quality: ..." solo quando il preset SSS è esplicito o un override `--max-volume-bounces` è in vigore, evitando rumore visivo sulle scene non-SSS.
- **Quality preset wiring**: ogni `QualityPreset` (10 preset esistenti) ora porta una `RandomWalkConfig` matched al tier — `draft*` → `Preview` (16 bounce, no NEE in-walk), `medium*` → `Normal` (64, NEE on), `final*`/`ultra` → `High` (256, NEE on). `--sss-quality` esplicito vince sempre sul tier inferito. `--max-volume-bounces` rispetta gli altri campi del preset.
- **Showcase scenes** (`scenes/showcases/`, 7 nuove):
  - `sss-randomwalk-01-marble.yaml` — busto in marmo Carrara (preset Jensen 2001), area light tre quarti. Dimostra il color-bleed warm-shadow caratteristico del marmo via random walk.
  - `sss-randomwalk-02-skin.yaml` — head sphere preset "skin1" Jensen 2001 (g=0.92 forward HG). Color bleed rosso/arancio sulle ombre, halo rosa sul bordo controluce.
  - `sss-randomwalk-03-milk-glass.yaml` — bicchiere di latte in Cornell box (`nee_in_walk=true` essenziale per il color bleed sulle pareti).
  - `sss-nested-glass-marble.yaml` — marmo dentro ampolla di vetro: stress test del MediumStack (depth max 2: vetro non-binding + marmo binding).
  - `medium-local-fog-room.yaml` — nebbia locale dentro stanza CSG (subtract). Esterno limpido, god-ray visibile solo all'interno.
  - `medium-csg-smoke.yaml` — fumo procedurale dentro CSG cubo-meno-sfera; superficie matched-IOR per non aggiungere refrazione.
  - `medium-water-tank.yaml` — acqua + pesce in acquario di vetro (stack depth 2: glass → water).
  - `medium-atmosphere-bound.yaml` — atmosfera Rayleigh (σ_s blu-eccesso 1/λ⁴) attorno a un pianeta, esterno = vacuum scuro.
- **Tests** (Fase 4-specifici):
  - `RandomWalkConfigTests.cs` (5 test): lock-down dei valori di `Preset.Preview/Normal/High`, monotonia su MaxVolumeBounces, costruzione esplicita round-trip.
  - `MediumInterfaceFogTests.cs` (2 test): binding scoped (fog locale scurisce solo l'interno della sfera, lasciando i corner intatti) vs global medium (oscura tutto). Verifica della invariante chiave del MediumInterface.
- **Docs**:
  - `docs/technical/subsurface-scattering.{md,it.md}` — NEW: derivation walk, hero-wavelength MIS, Fresnel coupling, RR strategy, CLI knobs, tabella preset Jensen 2001, guida migrazione.
  - `docs/technical/medium-interface.{md,it.md}` — NEW: ownership model, stack semantics, refraction transitions, restricted-BVH query, performance notes.
  - `docs/reference/scene-reference.md` + `riferimento-scene.md` — aggiunta sezione "Mediums Library" con schema YAML, regole di risoluzione, tabella binding entity, CLI SSS.
  - `DEVLOG.md` — entry "Ciclo MediumInterface + Random Walk SSS" con rationale clean-break.
  - `README.md` — voci feature MediumInterface + SSS Random Walk; parametri CLI `--sss-mode`, `--sss-quality`, `--max-volume-bounces`.
- **Smoke render**: tutti i 7 showcase renderizzano a `draft-tiny -s 4` senza warning né NaN. Il banner stampa correttamente "Mediums: N registered" e (con `-v`) il dettaglio di ogni medium costruito.
- **Test**: 476 → 483 verdi (+5 RandomWalkConfig, +2 MediumInterfaceFog). Nessuna regressione sui 476 esistenti.

## Context

Il renderer 3D-Ray è oggi un path tracer fisicamente plausibile con:
- Volumetrica **solo globale** (`world.medium`): nessun medium può essere legato a una geometria/oggetto specifico.
- Disney BSDF con un parametro `subsurface` che **simula** il subsurface scattering tramite Hanrahan-Krueger "flat blend" — è una **fake local approximation** che modifica la lobe diffuse, non trasporta luce attraverso la geometria.
- Beer-Lambert per-segmento già funzionante via `BsdfSample.NextSegmentAbsorption` + `currentAbsorption` passato in `TraceRay`, ma è solo assorbimento (`σ_a`), no scattering (`σ_s`).
- Il forward-compat field `subsurface_radius` è già parsato in `SceneData` (line 526) ma ignorato dal materiale.

**Problema**: senza vero SSS volumetrico non si possono rappresentare correttamente marmo, pelle, latte, cera, giada, foglie sottili, alabastro, candele — tutta una categoria di materiali fondamentale per un renderer pro-grade. Inoltre non si possono mai bindare media volumetrici a oggetti specifici (smoke in CSG, fog in stanza, water tank).

**Obiettivo**: aggiungere (a) `MediumInterface` per binding `IMedium` per-entity (interior + exterior), prerequisito strutturale, e (b) Random Walk SSS attivato come uso primario, con qualità Arnold/Cycles/RenderMan/Mitsuba e senza compromessi.

**Decisioni di policy** (confermate):
- **Clean break Mitsuba-style** per Disney: rimuovere i parametri `subsurface`, `subsurface_color`, `subsurface_radius` (HK flat fake). SSS si attiva **solo** quando l'entity ha `interior_medium` con `σ_s > 0`.
- **Hero-wavelength + MIS** spettrale (Cycles `random_walk_v2`).
- **CLI default `--sss-mode auto`** (rispetta i materiali) — SSS è correctness, non opzionale.
- **Full coverage MediumInterface**: piano include showcase e test anche per per-object fog/smoke/atmosphere bound oltre a SSS.

---

## Architettura

### MediumInterface ownership
`MediumInterface { IMedium? Interior; IMedium? Exterior }` è una **value struct** carrierata su `EntityData` (entity-level), risolta dal loader contro una libreria globale `Dictionary<string,IMedium>` indicizzata dal nuovo blocco YAML `mediums:`. Due istanze dello stesso materiale possono avere media interior diversi (un busto di marmo bianco e uno di marmo rosa con lo stesso `disney`).

### Medium stack (non single-current)
Un `MediumStack` value-struct (capacità 8, `InlineArray8` .NET 8+, zero-alloc) viene passato per `ref` attraverso `TraceRay`. `Top ?? _globalMedium` è il medium attivo. Push/pop sincronizzati su refraction `Enter`/`Exit`. Necessario per correttezza in scene nested (vetro contenente liquido SSS, ghiaccio dentro acqua, ecc.).

### Trigger SSS
SSS Random Walk si attiva quando: ray sample = refraction (delta o glossy transmission), il nuovo top dello stack ha `σ_s > 0`. In quel caso `TraceRay` non ricorre lineare ma chiama `RandomWalkSSS(...)` che termina al boundary di uscita (lo stesso medium pushed → quando il pop "lo svuota" siamo fuori).

### Hero-wavelength MIS
Per evento di scatter: pick canale `c` con probabilità `throughput[c] / sum(throughput)`; track per-channel pdf per balance heuristic; ogni evento aggiorna `throughput *= σ_s / σ_t` per canale; RR interna dopo bounce 3. Spectrally unbiased, ~3× più veloce di 3-channel parallel.

---

## Fasi

### Fase 1 — MediumInterface plumbing (no SSS yet)

Obiettivo: routing per-entity di media; comportamento Beer-Lambert esistente preservato ma guidato dallo stack invece di `NextSegmentAbsorption`.

File da creare/modificare:
- **NEW** `src/RayTracer/Volumetrics/MediumInterface.cs` — value struct `{Interior, Exterior}`, helper `MediumOn(bool frontFace)`.
- **NEW** `src/RayTracer/Volumetrics/MediumStack.cs` — `InlineArray8<IMedium?>` ref-struct con `Push/Pop/Top/Depth/Clone`. Overflow → drop oldest + Warn.
- **MODIFY** `src/RayTracer/Core/HitRecord.cs` (dopo line 17 `Material`): aggiungi `public MediumInterface MediumIface;`.
- **MODIFY** `src/RayTracer/Scene/SceneData.cs`:
  - aggiungi a `SceneData` un campo `List<MediumData>? Mediums;`
  - aggiungi a `EntityData` campi `string? InteriorMedium; string? ExteriorMedium;`
- **MODIFY** `src/RayTracer/Scene/SceneLoader.cs`:
  - refactor `BuildGlobalMedium` (line 3647) → `BuildMedium(MediumData)` riusabile.
  - dopo build materials, build `mediumLibrary: Dictionary<string,IMedium>` da `data.Mediums`.
  - in entity creation, risolvi `InteriorMedium`/`ExteriorMedium` contro la library; warning su id non trovato → fallback vacuum.
  - sull'`IHittable` finale dell'entity, applica wrapper `MediumBoundHittable`.
- **NEW** `src/RayTracer/Geometry/MediumBoundHittable.cs` — wrapper trasparente che setta `rec.MediumIface` su ogni hit. Evita di modificare ogni primitiva concreta.
- **MODIFY** `src/RayTracer/Materials/BsdfSample.cs`:
  - aggiungi `enum MediumTransition { None, Enter, Exit }` e campo `MediumTransition Transition;`.
  - tieni `NextSegmentAbsorption` `[Obsolete]` per il ciclo Fase 1→2, poi rimuovi in Fase 4.
- **MODIFY** `src/RayTracer/Materials/DisneyBsdf.cs` (`ScatterTransmission` line 938-948): emetti `Transition = FrontFace ? Enter : Exit` invece di `NextSegmentAbsorption`.
- **MODIFY** `src/RayTracer/Materials/Dielectric.cs`: stesso pattern per refraction lobes.
- **MODIFY** `src/RayTracer/Rendering/Renderer.cs`:
  - `TraceRay` signature (line 891): rimuovi `currentAbsorption`, aggiungi `ref MediumStack mediums`.
  - lungo i call site (line 694, 785, 1021): passa `ref mediums`; su `Transition = Enter` push `hit.MediumIface.Interior`; su `Exit` pop (con safety check).
  - `ApplyBeerLambert` (line 1045): leggi σ_a dal `mediums.Top` (HomogeneousMedium → σ_a property); altri tipi di medium delegano a `IMedium.Transmittance` lungo il segmento. **Importante**: l'absorption è ora un caso speciale del Transmittance — fasi successive lo unificheranno.
  - Sample/Phase volumetric loop (line 978-1036): usa `mediums.Top ?? _globalMedium` invece di `_globalMedium`. Mantieni `_globalMedium` come exterior fallback in fondo allo stack.

Acceptance:
- Tutti i test esistenti `VolumetricsTests`, `FireflyRegressionTests`, snapshot glass scenes verdi.
- **NEW** `MediumStackTests.cs`: push/pop balanced, overflow drop-oldest, depth tracking.
- **NEW** `MediumInterfaceRoutingTests.cs`: ray sintetico through nested sphere-in-cube, verifica top dello stack a ogni hit.
- Render `scenes/cristallo.yaml` — diff vs main = solo MC noise.

Dipendenze: nessuna.

---

### Fase 2 — YAML schema + libreria mediums + clean-break Disney

Obiettivo: dichiarare media riusabili, binding entity, rimozione parametri Disney fake.

Schema YAML:
```yaml
mediums:
  - id: marble_int
    type: homogeneous
    sigma_a: [0.0021, 0.0041, 0.0071]
    sigma_s: [2.19, 2.62, 3.00]
    phase: { type: hg, g: 0.0 }
  - id: smoke_local
    type: grid
    file: "volumes/smoke.vol"
    bounds_min: [-1, 0, -1]
    bounds_max: [1, 2, 1]
    sigma_a: [0.01, 0.01, 0.01]
    sigma_s: [2.0, 2.0, 2.0]
    phase: { type: hg, g: 0.4 }

entities:
  - type: sphere
    material: marble_surface
    interior_medium: marble_int
    exterior_medium: null   # null → eredita da stack (o globale)
  - type: csg
    op: subtract
    a: { type: cube, ... }
    b: { type: sphere, ... }
    interior_medium: smoke_local
```

**Clean-break Disney**:
- **MODIFY** `src/RayTracer/Materials/DisneyBsdf.cs`: rimuovi `Subsurface`, `SubsurfaceColor`, `Flatness`, `SubsurfaceRadius` (e relative texture variants). Rimuovi tutto il blocco HK flat (line 1252-1273) — la lobe diffuse torna a Lambert puro.
- **MODIFY** `src/RayTracer/Scene/SceneData.cs` (line 526-541): rimuovi i field `Subsurface*`. Lascia un warning nel loader se il YAML li contiene ancora: `Material '<id>': legacy 'subsurface' field ignored — use entity.interior_medium for real SSS (see docs/technical/subsurface-scattering.md)`.
- **MODIFY** `src/RayTracer/Scene/SceneLoader.cs` (line 912-944): rimuovi binding di subsurface params al ctor Disney.

**Tool migrazione**:
- **NEW** `src/Tools/MigrateFakeSss/` — eseguibile `.csproj` (non in solution, come `ChessGen`/`TempleGen`). Scanna ricorsivamente una directory YAML, per ogni `material.subsurface > 0`:
  - genera un nuovo `mediums:` entry con σ derivati da preset Jensen 2001 (`marble`, `skin1`, `skin2`, `milk`, `cream`, `ketchup`, `potato`, `apple`, `jade`, `chicken`, `wax`) se il color del materiale matcha; altrimenti deriva `σ_s = 1/(radius * color)`, `σ_a` da `(1-color)/radius`.
  - aggiunge `interior_medium: <new_id>` alla prima entity che usa quel material (con commento `# migrated from subsurface=X`).
  - emette report `migration-report.txt` con before/after per ogni materiale.
- Stratega: `dotnet run --project src/Tools/MigrateFakeSss -- scenes/` produce diff, l'artista raffina.

File impattati noti da migrare: `scenes/libraries/materials/glasses.yaml` (latte, ghiaccio sporco, opali ~10-15 entries), `scenes/showcases/library-liquids.yaml` (latte slot), eventuali scene utente.

Acceptance:
- Test schema parsing positivi + negativi (unknown medium id → fallback + warn, cyclic medium ref → error).
- Render `scenes/cristallo.yaml` invariato.
- Render scene "puramente Lambert/metal" identiche al pre-Fase 2.
- Render scene con `subsurface:>0` legacy: produce warning, output Lambert (atteso, va migrato).
- Tool migrazione esegue su `scenes/`, produce report e YAML migrati che renderizzano senza warning.

Dipendenze: Fase 1.

---

### Fase 3 — Random Walk SSS integrator

Obiettivo: vero random walk in mezzi omogenei legati a geometrie, hero-wavelength + MIS.

File:
- **NEW** `src/RayTracer/Rendering/RandomWalkSss.cs` — funzione statica/instance:
  ```
  Vector3 Walk(Ray entryRay, IMedium medium, IHittable entityRoot, 
               ref MediumStack mediums, RayCategory cat,
               int maxVolumeBounces, Vector3 throughputIn, 
               float indirectClampFactor, bool neeInsideWalk)
  ```
- **MODIFY** `src/RayTracer/Rendering/Renderer.cs`:
  - transmission recursion (line 785, ShadeSampleBounce): se `s.Transition == Enter` AND `mediums.Top` ha `σ_s > 0`, call `RandomWalkSss.Walk(...)` invece di `TraceRay`.
  - aggiungi field `_maxVolumeBounces` (default 64), `_sssMode` (Auto|Off|Approx), `_sssQuality` preset.

Algoritmo (hero-wavelength, Cycles `random_walk_v2`):
1. Picking canale hero `c` proporzionale a `throughput[c]`; salva `heroPdf = throughput[c] / sum`.
2. Loop `b = 0..maxVolumeBounces`:
   - `σ_t = σ_a + σ_s` (per canale); free-flight `t = -ln(1-ξ) / σ_t[c]`.
   - Intersect ray contro la **stessa entity** (restricted-BVH query: usa il root `IHittable` dell'entity, passato come param — evita di re-intersecare scene intera). Se `t >= tBoundary` → escape: refrange di nuovo fuori usando il BSDF di superficie al punto di uscita; pop il medium; return throughput accumulato + radiance.
   - Altrimenti scattering event a `p = entryRay.At(t)`:
     - **NEE in-scattering**: se `neeInsideWalk` (alta qualità), `Lnee += ComputeDirectLightingMedium(p, dir)` con clamp aggressivo `_indirectMaxSampleRadiance * 1/(1+b·0.1)` per smorzare fireflies in profondità.
     - Phase sample: HG con `g` dal medium (default 0); update `dir = wi`, `entryRay = Ray(p, wi)`.
     - Update throughput: `throughput *= σ_s / σ_t` per canale; ribilancia hero-pdf.
   - **RR interna** da `b >= 3`: `q = max(throughput.X, .Y, .Z)`, terminate se `ξ > q`, else `throughput /= q`.
3. Se max bounces raggiunto senza escape: return throughput accumulato (no radiance, kill path) — questo limita energia ma evita inf loop su low-albedo media.

**Energy/Fresnel coupling**:
- Entry Fresnel: già applicato dal BSDF di superficie (Disney) prima del walk; il throughput entrante è `T_entry · viewBSDF`.
- Exit: al boundary, ricampiona Fresnel transmission del BSDF al punto di uscita (re-evaluate Disney sulla superficie locale del medium boundary). Reflection interna (TIR / Fresnel reflection) → ricomincia walk con direzione riflessa, decrement bounce count. Pro: gestisce naturalmente il "polished marble look".

**Restricted-BVH query**: per garantire che il walk resti dentro l'entity (no leak in altre geometrie attraverso le quali il random walk potrebbe accidentalmente passare), `MediumBoundHittable` espone `Inner` come `IHittable` da passare a `Walk` come `entityRoot`. Walk usa solo `entityRoot.Hit(...)`, non `_world.Hit`. Costo: O(log primitives_in_entity) per intersection.

**Modalità SSS**:
- `--sss-mode auto`: default. Se entity ha interior medium con `σ_s > 0`, walk si attiva.
- `--sss-mode off`: ignora il binding, il refraction segue il path classico (medium diventa pure-absorption pretending `σ_s = 0`). Utile per preview rapidi e A/B comparison.
- `--sss-mode approx`: **non implementato in clean-break** (rimosso con i parametri Disney). Documentare che è alias di `off`.

Acceptance:
- **White-furnace test** (`SssEnergyConservationTests.cs`): chiuso sphere, `σ_a=0`, `σ_s=1`, uniform unit emitter outside → exit radiance = input entro 1% a 1024 spp.
- **Diffusion equivalence**: slab sottile vs profilo analitico Jensen, tolleranza 5%.
- **Marble bench**: render scene marble bust 256×256 @ 256 spp, no pixel > 50× mean luminance.
- **Skin bench**: render head sphere parametri skin1 Jensen, color-bleed visibile (red-shifted attorno alle "orecchie").
- **No regression**: scene non-SSS (chess, cristallo) tempo render < +5%.

Dipendenze: Fase 1+2.

---

### Fase 4 — CLI, quality presets, full MediumInterface use cases

Obiettivo: rifinire CLI, integrare quality presets, dimostrare l'intera potenza di MediumInterface oltre al SSS.

CLI (Program.cs):
- `--max-volume-bounces N` (default 64) — limite walk depth.
- `--sss-mode auto|off` — default `auto`.
- `--sss-quality preview|normal|high` — preset:
  - `preview`: `maxVolumeBounces=16`, `rrStart=1`, `neeInsideWalk=false`.
  - `normal` (default): `maxVolumeBounces=64`, `rrStart=3`, `neeInsideWalk=true`.
  - `high`: `maxVolumeBounces=256`, `rrStart=6`, `neeInsideWalk=true`.
- Integrazione `QualityPreset` (line 433-476): `draft*` → preview, `medium*` → normal, `final*`/`ultra` → high.

Full coverage MediumInterface — showcase scenes (NEW):
- `scenes/showcases/sss-randomwalk-01-marble.yaml` — busto marmo + area light.
- `scenes/showcases/sss-randomwalk-02-skin.yaml` — head sphere skin Jensen.
- `scenes/showcases/sss-randomwalk-03-milk-glass.yaml` — milk-glass cornell.
- `scenes/showcases/sss-nested-glass-marble.yaml` — stress MediumStack: marble dentro ampolla di vetro.
- `scenes/showcases/medium-local-fog-room.yaml` — fog locale dentro CSG-room, esterno limpido.
- `scenes/showcases/medium-csg-smoke.yaml` — smoke grid medium dentro CSG-subtract.
- `scenes/showcases/medium-water-tank.yaml` — water medium dentro tank di vetro, pesce dentro.
- `scenes/showcases/medium-atmosphere-bound.yaml` — nishita atmosphere bound a un planet sphere, exterior vacuum.

Test (NEW):
- `MediumStackTests.cs` — push/pop, overflow, nested transitions.
- `RandomWalkSssTests.cs` — white-furnace, diffusion, max-bounces clamp, hero-wavelength unbiasedness vs 3-channel reference.
- `SssEnergyConservationTests.cs` — closed cavity radiance balance.
- `MediumInterfaceFogTests.cs` — fog locale: ray entrante perde transmittance, ray esterno no.
- Estendi `FireflyRegressionTests.cs` — caso milk-cornell + caso marble.

Documentazione:
- **UPDATE** `docs/reference/scene-reference.md` + `riferimento-scene.md`:
  - nuova sezione "Mediums library" (top-level `mediums:`).
  - sezione "Entity medium binding" (`interior_medium`, `exterior_medium`).
  - rimozione documentazione `subsurface`/`subsurface_color`/`subsurface_radius`/`flatness` da Disney; aggiunta nota "Subsurface scattering in 3D-Ray uses physically-based random walk via interior_medium — see docs/technical/subsurface-scattering.md".
- **NEW** `docs/technical/subsurface-scattering.md` + `subsurface-scattering.it.md`:
  - derivation random walk, hero-wavelength rationale, σ_s/σ_a derivation, Fresnel coupling, RR strategy, CLI knobs.
  - tabella preset Jensen 2001.
  - guida migrazione (link al tool).
- **NEW** `docs/technical/medium-interface.md` + `.it.md`: ownership model, stack semantics, refraction transitions, performance notes.
- **UPDATE** `DEVLOG.md` — entry "Ciclo MediumInterface + Random Walk SSS" con rationale clean-break.
- **UPDATE** `README.md` — sezione features, lista showcase.

Acceptance:
- Tutti i test verdi (esistenti + nuovi).
- Tutti gli showcase renderizzabili a `medium-small` senza crash.
- CI render smoke test esteso a un SSS scene (es. milk-glass cornell a 320×213, 32 spp).
- Benchmark BenchmarkDotNet `RenderBenchmarks.cs`: marble scene render time entro 2.5× di scene equivalente senza SSS.

Dipendenze: Fase 3.

---

## File critici (riepilogo)

| File | Cambio |
|------|--------|
| `src/RayTracer/Volumetrics/MediumInterface.cs` | NEW value struct |
| `src/RayTracer/Volumetrics/MediumStack.cs` | NEW InlineArray8 stack |
| `src/RayTracer/Geometry/MediumBoundHittable.cs` | NEW wrapper, espone `Inner` |
| `src/RayTracer/Rendering/RandomWalkSss.cs` | NEW integrator |
| `src/RayTracer/Core/HitRecord.cs` | +1 field `MediumIface` |
| `src/RayTracer/Materials/BsdfSample.cs` | +`Transition` enum |
| `src/RayTracer/Materials/DisneyBsdf.cs` | Rimozione Subsurface*, emit Transition |
| `src/RayTracer/Materials/Dielectric.cs` | emit Transition |
| `src/RayTracer/Scene/SceneData.cs` | +`mediums:`, +`interior_medium`/`exterior_medium`, − Subsurface fields |
| `src/RayTracer/Scene/SceneLoader.cs` | medium library, entity binding, wrapper, − legacy parsing |
| `src/RayTracer/Rendering/Renderer.cs` | `ref MediumStack`, walk dispatch, mode/quality |
| `src/RayTracer/Program.cs` | CLI flags `--sss-mode`, `--sss-quality`, `--max-volume-bounces` |
| `src/Tools/MigrateFakeSss/` | NEW tool migrazione legacy YAML |

## Anti-pattern da evitare

- NON memorizzare `IMedium` direttamente in `IMaterial` (couples shading a scene topology).
- NON usare single-current invece di stack (rotto su nested transmissive).
- NON re-applicare Fresnel entry dentro il walk (entry T già nel throughput entrante).
- NON usare `_world.Hit` dentro il walk (può leak in altre geometrie); usa `entityRoot.Hit` ricevuto dal wrapper.
- NON sovrascrivere `_maxDepth` con `_maxVolumeBounces` — sono budget separati.
- NON cambiare default `--sss-mode` a `off` (è correctness, non extra optional).
- NON re-introdurre `subsurface` come alias deprecato — clean break significa rimozione netta + tool migrazione + warning chiaro nel loader.

## Verifica end-to-end

```bash
# 1. Build & test
dotnet build src/RayTracer/RayTracer.csproj -c Release
dotnet test src/RayTracer.Tests/RayTracer.Tests.csproj

# 2. Verifica regressione su scene non-SSS (devono essere ~identiche)
dotnet run --project src/RayTracer -c Release -- \
  -i scenes/chess.yaml -o renders/chess-post.png -q medium-small
dotnet run --project src/RayTracer -c Release -- \
  -i scenes/cristallo.yaml -o renders/cristallo-post.png -q medium-small

# 3. Render SSS showcase (visivo)
dotnet run --project src/RayTracer -c Release -- \
  -i scenes/showcases/sss-randomwalk-01-marble.yaml -q final-small
dotnet run --project src/RayTracer -c Release -- \
  -i scenes/showcases/sss-randomwalk-02-skin.yaml -q final-small
dotnet run --project src/RayTracer -c Release -- \
  -i scenes/showcases/sss-nested-glass-marble.yaml -q final-small

# 4. Full MediumInterface use cases
dotnet run --project src/RayTracer -c Release -- \
  -i scenes/showcases/medium-local-fog-room.yaml -q medium-small
dotnet run --project src/RayTracer -c Release -- \
  -i scenes/showcases/medium-csg-smoke.yaml -q medium-small

# 5. Migrazione librerie
dotnet run --project src/Tools/MigrateFakeSss -- scenes/

# 6. Benchmark regression
dotnet run -c Release --project src/RayTracer.Benchmarks -- --filter '*Render*'
```

Sanity manuali:
- `sss-randomwalk-02-skin.yaml`: color-bleed rosso visibile dietro orecchio/naso (firma SSS).
- `medium-local-fog-room.yaml`: fog visibile solo dentro la stanza, esterno limpido.
- `sss-nested-glass-marble.yaml`: nessun crash/NaN dopo molti spp; stack depth max osservata in verbose ≤ 4.
