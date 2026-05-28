# 📋 DEVLOG — 3D-Ray

Roadmap, lavori in corso, bug noti, storico cicli.

> Stati: `✅ Fatto` · `🔧 In corso` · `⬜ Da fare`

---

## Ciclo Hardening SSS / MediumInterface / volumetria ✅

Review mirata di subsurface scattering (random walk), medium-interface/stack e volumetria correlata, con fix di correttezza e un'ottimizzazione di prestazioni a qualità invariata. (Diversi "bug" sospettati in fase di review sono stati **scartati come falsi positivi** dopo verifica: l'hero-wavelength MIS spettrale è corretto e non biased; la RR max-canale è la scelta *conservativa*; l'early-exit del ratio tracking scatta solo quando tutti i canali sono <1e-5; i guard div-by-zero/clamp canale sono solo difensivi.)

**Bug corretti.**
- **NEE di superficie dentro un mezzo legato a entità** (`Renderer.ComputeDirectLighting`). Le shadow ray da una superficie *interna* a un mezzo bound (oggetto immerso in acqua/fog, "fish in a tank") erano attenuate solo dal `globalMedium`, mai dallo stack. Ora `ComputeDirectLighting` riceve il medium attivo (`mediums.Top ?? globalMedium`) e l'entità che lo delimita, e clippa il segmento in-medium con `ClipShadowToBoundary` (stesso pattern del path volumetrico). Il `MediumStack` trasporta ora la geometria bounding per slot (`TopEntity`); lo stesso clip è applicato anche alla NEE del path volumetrico su mezzi bound.
- **MIS dell'emissione sull'uscita del random walk SSS** (`RandomWalkSss`). All'escape dopo ≥1 scatter il walk passava `prevIsDelta: true`, contando a peso pieno luci/cielo visti attraverso il bordo → double counting (la NEE al vertice di scatter li aveva già campionati). Ora la pdf di fase dell'ultimo scatter è inoltrata (`prevBsdfPdf = lastPhasePdf, prevIsDelta: false`), coerente col path volumetrico standard; l'escape a `b==0` (rifrazione speculare senza scatter) resta delta.
- **Doppio conteggio della trasmittanza in `NishitaAtmosphereMedium.Sample`**. Il delta tracking porta già la trasmittanza nella *probabilità* di raggiungere il punto, ma `beta` moltiplicava anche la trasmittanza analitica completa (sia sul ramo scatter sia sul pass-through) → atmosfera troppo scura. Ora `beta` usa solo il rapporto cromatico `Tr_vec/Tr_scalar` (ricolora senza riapplicare l'attenuazione scalare). `MaxAttempts` 64 → 256 per ridurre il bias di troncamento su raggi lunghi/densi.

**Performance (qualità invariata).** Nel random walk SSS la query di bordo è ora clippata a `tDist` (`entityRoot.Hit(ray, ε, tDist)`): su uno step di scattering — caso dominante in mezzi densi — il BVH dell'entità mesh (busti/statue) pota tutto oltre la distanza di free-flight. La Russian Roulette del walk usa ora il throughput di path completo (`pathThroughput * relBeta`) terminando prima i walk a basso contributo (es. mezzi con tint scuro). Entrambe restano *unbiased*.

**Test.** `NishitaAtmosphereMediumTests` (nuovo) — invariante scale-indipendente: la media MC di `(scatter ? 0 : beta)` riproduce la `Transmittance` analitica per-canale (fallisce col vecchio peso a doppio conteggio). `SurfaceInBoundMediumNeeTests` (nuovo) — una superficie dentro un mezzo assorbente bound è nettamente più scura che dentro il vuoto. Suite completa verde (492 test).

**File modificati.** `Rendering/RandomWalkSss.cs`, `Rendering/Renderer.cs`, `Volumetrics/MediumStack.cs`, `Volumetrics/NishitaAtmosphereMedium.cs`; test nuovi sotto `RayTracer.Tests/`.

---

## Ciclo SSS embedded (Arnold/Cycles parity) ✅

Aggiunta la strada **material-embedded** al subsurface scattering, complementare al binding entity-level già esistente (`interior_medium`). Dichiarare `subsurface_radius` su un materiale Disney attiva ora automaticamente il Random Walk volumetrico, senza serve una sezione `mediums:` né `interior_medium` sull'entity.

**Razionale.** Le librerie material self-contained (stones, organics, foods, liquids, glasses, minerals-gems, leathers) sono il workflow tipico degli artisti: si importa un singolo file e si usano gli ID. Costringere ogni utilizzatore a importare anche `libraries/mediums/*.yaml` e a ricordare il binding `interior_medium` per riga di entity è boilerplate puro. La parity con Arnold `standard_surface` (`subsurface_type: randomwalk`) e con il Subsurface del Principled BSDF di Cycles fa risparmiare 4-5 righe per scena su materiali traslucenti standard, mantenendo l'override entity-level disponibile per i casi avanzati (volumi condivisi, media eterogenei, override per-entity).

**Implementazione.**
- `SceneLoader.BuildEmbeddedSssMedium` — costruisce un `HomogeneousMedium` anonimo dai campi del materiale. Per canale: `σ_t = 1 / (subsurface_radius · subsurface_scale)`, `σ_s = α · σ_t`, `σ_a = (1 − α) · σ_t`, con `α = subsurface_color` (fallback `color`). Phase function HG con `g = subsurface_anisotropy` (isotropic quando g ≈ 0).
- `SceneLoader.ApplyEmbeddedSssDefaults` — forza i default necessari al lobo di trasmissione Disney solo se l'utente non li ha già impostati: `spec_trans = 1.0`, `transmission_color = [1, 1, 1]`. Senza questi il lobo non emette `MediumTransition.Enter` e lo stack non viene pushato. Gli altri parametri (`metallic`, `roughness`, `ior`, …) sono intoccati.
- `SceneLoader.ResolveEntityMediumInterface` — esteso per consultare la mappa materiale → medium embedded come fallback dell'`interior_medium` esplicito sull'entity. L'esplicito vince sempre (convenzione Arnold/Cycles): lo stesso materiale può essere riutilizzato su entity diverse e ognuna può sostituire il volume in modo indipendente.
- Warning a tre canali (skipping silente del medium embedded): `metallic > 0` (il metallic blend sopprime la trasmissione), `thin_walled: true` (niente volume interno), type non-Disney (`lambertian`/`metal`/`dielectric` non emettono `Enter`).

**File modificati.**
- `SceneData.cs` — 3 nuovi campi sul Disney material: `subsurface_radius`, `subsurface_scale`, `subsurface_anisotropy` (`subsurface_color` esisteva già).
- `SceneLoader.cs` — nuovi helper `BuildEmbeddedSssMedium` + `ApplyEmbeddedSssDefaults`; `ResolveEntityMediumInterface` esteso al lookup material→medium embedded; warning queue per i 3 casi sopra.

**Compatibilità.** Il warning legacy su `subsurface`/`subsurface_color`/`flatness` (parametri Disney 2015 rimossi nel ciclo precedente) ora scatta solo se `subsurface_radius` è **assente** sul materiale — in quel caso resta un fake-SSS legacy genuino da migrare. Quando invece `subsurface_radius` è presente, gli stessi campi smettono di essere "legacy noise" e diventano parte del nuovo path embedded, quindi il warning non è più rilevante.

**Librerie aggiornate** (set di default `subsurface_radius` calibrato per artisti che importano la libreria senza modifiche): `stones`, `organics`, `foods`, `liquids`, `glasses`, `minerals-gems`, `leathers`. **Non** aggiornata `fabrics` (i tessuti sottili sono modellati con `diff_trans` + `thin_walled`, non con SSS volumetrico — il warning `thin_walled` lo ricorderebbe comunque).

**Docs.** `docs/reference/scene-reference.md` + `riferimento-scene.md` (nuova sottosezione "Material-embedded SSS" / "SSS material-embedded" sotto §5.5, con formula σ esplicita, auto-default table, regola di precedenza, casi warning, esempio); `docs/technical/subsurface-scattering.{md,it.md}` (nuova sezione "Two binding paths" / "Due strade di binding" con confronto tabellare entity-bound vs material-embedded); `docs/tutorial/{en,it}/03-materials.md` (callout SSS aggiornato a due strade); `scenes/libraries/materials/README.md` + `scenes/libraries/mediums/README.md` (sezione "Material-embedded SSS" / nota di apertura); README root (feature line nella sezione Volumetria).

---

## Ciclo MediumInterface + Random Walk SSS ✅

Sostituito il vecchio "fake SSS" del Disney BSDF (`subsurface`, `subsurface_color`, `flatness`, lobo flat HK) con un sistema fisicamente corretto.

**Razionale del clean break.** Il vecchio `subsurface` su Disney era una falsa local-approximation che modificava il lobe diffuse (Hanrahan-Krueger flat) — non trasportava luce attraverso la geometria. Materiali fondamentali (marmo, pelle, cera, latte, giada, foglie sottili, alabastro) non avevano look fisicamente corretto. Inoltre nessun medium volumetrico poteva essere bound a un'entity specifica (smoke in CSG, fog in stanza, acqua in tank).

Decisione di policy: **rimozione netta** dei field Disney legacy + **MediumInterface per-entity** + **Random Walk integrator stile Cycles `random_walk_v2`** + tool di migrazione per riscrivere le scene utente esistenti. Niente alias deprecati, niente fallback compatibili: il loader emette warning sui field rimossi e ignora i valori. Il nuovo path SSS è correttezza, non opzionale (`--sss-mode auto` di default).

**Architettura.**
- `MediumInterface { Interior, Exterior }` value struct sull'entity. `MediumStack` `ref struct` zero-allocation (InlineArray8) threadato per `ref` attraverso `TraceRay`. Stack push/pop sincronizzati con `BsdfSample.Transition` (`Enter`/`Exit`). Copy-on-write a ogni transition per non corrompere il frame chiamante quando il walk branccia.
- `MediumBoundHittable` wrapper che stampa `rec.MediumIface` + `rec.EntityRoot` sull'hit. Espone il root come `IHittable` per la restricted-BVH query del walk — niente leak in geometria adiacente.
- `RandomWalkSubsurface` integrator hero-wavelength + balance-heuristic MIS spettrale sui 3 canali, Cycles-style. Sample hero proporzionale a β[c], free-flight `t = -ln(ξ)/σ_t[hero]`, throughput per evento `β *= σ_s · exp(-σ_t·t) / Σ_c q[c] · σ_t[c] · exp(-σ_t[c]·t)`. RR interna da `b ≥ RrStartBounce`, max-bounces hard cap come backup. Depth-aware indirect clamp `_indirectMaxSampleRadiance / (1 + 0.1·b)` per smorzare firefly profondi.

**CLI Fase 4.**
- `--sss-mode auto|off` — default `auto`. `off` declassa media pushati ad assorbimento-only (Beer-Lambert legacy) per preview / A/B.
- `--sss-quality preview|normal|high` — preset random-walk. `preview` (16 vol-bounce, no NEE in-walk), `normal` (64, NEE on), `high` (256, NEE on). Ereditato da `--quality` quando omesso: `draft*` → preview, `medium*` → normal, `final*`/`ultra` → high.
- `--max-volume-bounces N` — override del cap dal preset.

**Showcase scenes (Fase 4).** 7 scene in `scenes/showcases/`:
- `sss-randomwalk-01-marble.yaml` — busto marmo, area light tre quarti.
- `sss-randomwalk-02-skin.yaml` — head sphere preset Jensen "skin1", color bleed visibile.
- `sss-randomwalk-03-milk-glass.yaml` — bicchiere di latte in Cornell, NEE in-walk + GI.
- `sss-nested-glass-marble.yaml` — marmo dentro ampolla di vetro (stress MediumStack).
- `medium-local-fog-room.yaml` — fog locale dentro stanza CSG, esterno limpido.
- `medium-csg-smoke.yaml` — smoke procedurale dentro CSG subtract.
- `medium-water-tank.yaml` — acqua + pesce in acquario di vetro (stack depth 2).
- `medium-atmosphere-bound.yaml` — atmosfera Rayleigh attorno a un pianeta, spazio esterno nero.

**Test.** 5 `SssRandomWalkTests` (σ_s=0 fallback, white-furnace, color-bleed spettrale, dense-medium robustness, SssMode.Off dispatch); 9 `MediumStackTests` (push/pop, overflow, depth, value-copy); 2 `MediumBoundHittableTests`; 2 `MediumInterfaceFogTests` (binding scope vs global medium); 5 `RandomWalkConfigTests` (preset monotonicity, value lock-down); 3 `SssEnergyConservationTests` (closed cavity in tre regimi σ); 2 firefly SSS (marble, milk-Cornell) in `FireflyRegressionTests`. 488 test verdi totali.

**Docs.** `docs/technical/subsurface-scattering.{md,it.md}` (derivation walk, hero-wavelength MIS, Fresnel coupling, preset Jensen 2001), `docs/technical/medium-interface.{md,it.md}` (ownership model, stack semantics, transition rules). Sezione "Mediums Library" aggiunta a `docs/reference/scene-reference.md` + `riferimento-scene.md`. Tutti i riferimenti legacy a `subsurface`/`subsurface_color`/`subsurface_radius`/`flatness` rimossi da reference Disney, capitoli tutorial Disney (`03-materials`), intro ray-tracing (`01-what-is-ray-tracing`), sezione transforms-and-groups (`05`), libreria materiali (`10-libraries-and-projects`) — sostituiti con esempi `interior_medium`. README features list aggiornata.

**CI/Benchmark.** `.github/workflows/dotnet.yml` ora renderizza anche la milk-glass Cornell come smoke test SSS (320×213, 32 spp). `RenderBenchmarks.cs` esteso a misurare cornell-baseline vs marble-SSS in parallelo per quantificare l'overhead SSS (acceptance target: marble entro 2.5× di cornell-equivalente).

**Anti-pattern da evitare** (note di design, per future modifiche):
- NON memorizzare `IMedium` direttamente in `IMaterial` (accoppia lo shading alla topologia di scena).
- NON usare single-current medium invece di stack (rotto su transmissive annidate: vetro contenente liquido SSS, ghiaccio in acqua).
- NON re-applicare Fresnel entry dentro il walk (la `T_entry` è già nel throughput entrante).
- NON usare `_world.Hit` dentro il walk (può leak in altre geometrie); usare sempre `entityRoot.Hit` ricevuto dal `MediumBoundHittable`.
- NON sovrascrivere `_maxDepth` con `_maxVolumeBounces` — sono budget separati (depth dei bounce surface vs walk volumetrico).
- NON cambiare il default `--sss-mode` a `off` (è correttezza, non un opt-in: scene autorate per il walk si rompono visivamente).
- NON re-introdurre `subsurface` come alias deprecato — clean break significa rimozione netta + tool migrazione + warning del loader.

---

## Ristrutturazione Librerie scenes/libraries/

**Rimosso:** `objects/` (11 file, ~150 template) e `starter-kits/` (19 scene
complete) — la libreria objects aveva 3 soli utenti reali (showcase) che ora
definiscono la geometria inline; i starter-kit erano scene complete autonome
non corrispondenti al concetto di libreria importabile.

**Aggiunto:** `geometry-lights.yaml` in `lights/` — 12 preset emissivi `emi_*`
(scala blackbody 2000K→7000K + speciali: fuoco, LED strip, bioluminescenza,
sole diretto) per trasformare qualsiasi geometria in sorgente NEE senza
definire luci esplicite. Copre il gap lasciato dalla rimozione di `emissives.yaml`.

**Aggiunto:** `README.md` in `fonts/` e `terrains/` — documentazione mancante
per le due librerie generate da tool (FontGen, TerrainGen).

**Aggiornato:** `lights/README.md`, `textures/README.md`, `scenes/libraries/README.md`
— allineamento stilistico a `materials/README.md` (no emoji, formato tabellare
uniforme).

**Aggiornato:** `docs/tutorial/{en,it}/10-libraries-and-projects.md` — rimosse
sezioni objects e starter-kits; aggiunte sezioni geometry-lights, fonts, terrains.

---

## 📌 Note rapide

### ✅ Wood texture — riscrittura pro-grade (Arnold/Cycles/Renderman/Mitsuba parity)

Sostituito il vecchio carrier `sin(ring·scale)^sharpness` (profilo
simmetrico, ogni anello identico) con un modello production-grade degli
anelli annuali al livello di Arnold `wood`/`knots`, Cycles Wave Texture
in modalità Rings, RenderMan `PxrWoodKnot` e Substance Designer Wood.
Il vecchio algoritmo aveva due bug strutturali di realismo: il profilo
simmetrico (scuro ai due estremi, chiaro al centro) è l'opposto del
profilo reale (lungo plateau earlywood chiaro + sottile banda
latewood scura ALLA FINE dell'anello), e ogni anello era identico al
successivo, mentre in natura ogni anno di crescita ha la sua larghezza
e il suo colore unici.

**Nuovo pipeline (per shade).**
1. **Texture transform + `space_stretch` anisotropo.** Pre-stretch
   lineare per tagli non isotropi della tavola.
2. **Geological fold (anisotropo).** `DomainWarp.Anisotropic` con
   `fold_amplitude` per asse — simula il bending macro del tronco
   PRIMA del recursive warp, così il warp opera nello spazio piegato.
3. **Recursive (IQ) domain warp.** `DomainWarp.Recursive` itera
   `warp_iterations` volte (0 = no warp, 2 = canonico IQ, 3 = flow
   forte). Uccide il tiling visibile sugli anelli. Sostituisce il
   `distortion` single-iter (la chiave YAML `distortion:` è mappata
   su `warp_amplitude` per back-compat).
4. **Decomposizione radiale/assiale.** `dist = ||p - (p·axis)axis||`
   sul punto warpato.
5. **Radial anisotropy.** Comprime la coordinata radiale del sample
   point per il look quartersawn (medullary rays).
6. **Multi-banda noise sulla distanza radiale.** Grain fBm (alta freq,
   fibra) + figure band (bassa freq, con `figure_aspect` per
   allungamento assiale → strisce perpendicolari al grain — curly
   maple, flame mahogany) + axial_grain opzionale.
7. **Knot 3D-cone projection.** Worley anisotropico nello spazio
   (perp1, perp2, along/aspect) — ogni cella sparsa ospita un nodo
   il cui cono visibile si allarga con la distanza assiale dal
   centro. Dentro il cono il centro dell'anello viene tirato verso
   il feature point del nodo e si aggiunge un cuore scuro.
8. **Per-ring random variation.** Hash deterministico
   `(ringIndex, objectSeed) → [-1, 1]` che perturba per ogni anello:
   - la **larghezza** (`ring_width_variation`) shift della coord
     radiale costante dentro un anello;
   - il **colore** (`ring_color_variation`) shift della lookup ramp.
   È IL feature che separa "wood CG" da "wood reale".
9. **Asymmetric ring profile.** `rise(frac) * fall(frac)` dove
   `rise = smoothstep(0, earlywood_transition, frac)` (ascesa veloce
   dal latewood precedente) e `fall = 1 - smoothstep(1 - latewood_width, 1, frac)`
   (discesa morbida nel latewood). Sostituisce il legacy
   `pow(triangle, sharpness)` simmetrico. `ring_sharpness` ora
   controlla la nitidezza del bordo latewood via `pow(t, 1/sharpness)`.
10. **Knot dark heart** (applicato dopo il ring profile per
    leggibilità indipendente dalla banda).
11. **Open-pore vessels** — Worley anisotropico assialmente
    (`pore_aspect` allungamento) per i corti canali cilindrici di
    quercia/frassino/noce/mogano. Gating per-cella (`pore_density`)
    + falloff smoothstep (`pore_strength`, `pore_scale`). 0 disabilita
    interamente Worley.
12. **Sapwood / heartwood radial gradient.** Smoothstep su
    `heartwood_radius` con `heartwood_blend > 0` che scurisce verso
    il centro (modello noce, ciliegio, ipe). 0 disabilita.
13. **Output.** `Color` (default, ramp/lerp) o `Mask` (`(t,t,t)`)
    per pilotare Disney `roughness_texture` / `sheen_texture` &c.

**YAML schema.** Knob esistenti (`scale`, `grain_strength`,
`noise_strength`, `ring_axis`, `ring_sharpness`, `axial_grain`,
`octaves`, `lacunarity`, `gain`, `grain_scale`, `figure_scale`,
`figure_strength`, `radial_anisotropy`, `knot_density`, `color_ramp`,
`randomize_offset/rotation`) preservati. Aggiunti:
`latewood_width`, `earlywood_transition`, `ring_color_variation`,
`ring_width_variation`, `warp_amplitude`, `warp_scale`,
`warp_iterations`, `fold_amplitude`, `fold_scale`, `space_stretch`,
`figure_aspect`, `pore_density`, `pore_scale`, `pore_strength`,
`pore_aspect`, `heartwood_radius`, `heartwood_blend`, `knot_scale`,
`output: "mask"`. Il `distortion:` legacy è mappato su
`warp_amplitude` per scene che ancora lo settano.

**Invariante di concentricità.** Il `noiseShift` per-istanza NON
viene mai aggiunto al pipeline geometrico (fold/warp/decomposizione
radiale) — quello deve restare deterministico in object space così
che gli anelli restino concentrici attorno all'asse del tronco. La
decorrelazione tra istanze adiacenti si ottiene SIA via `noiseShift`
sul sample point del noise (grain/figure) SIA via `Perlin.GetOrCreate(objectSeed)`
che fornisce un'istanza di Perlin diversa per il warp.

**Migrazione libreria.** `scenes/libraries/materials/woods.yaml`
riscritta interamente: ogni essenza (acero, betulla, frassino, faggio,
quercia, ciliegio, teak, iroko, noce, mogano, wengé, palissandro,
ebano, ebano macassar, pino, abete, cedro, larice, zebrano, padouk,
amaranto, bocote, sbiancato, shou sugi ban, barnwood, tinto nero,
tinto grigio) usa una `color_ramp` 3-stop calibrata sulle foto reali
+ knob species-appropriate (pore_density 0.42-0.48 per quercia/frassino,
0 per essenze close-pore come acero/faggio, knot_density 0.55 per pino,
heartwood_blend 0.18-0.22 per noce/ciliegio, ecc.). Materiali studio
estesi: `dis_acero_curly_studio`/`max`, `dis_mogano_flame_studio`,
`dis_quercia_quartato_studio`/`medullary`, `dis_pino_nodoso_studio`/`heavy`,
`dis_acero_birdseye[_studio]`, `dis_noce_burl_studio`, `dis_cedro_shousugiban`,
`dis_rovere_segato_grezzo`, `dis_betulla_ricca_studio`,
`dis_quercia_pro_mask` (esempio canonico mask-roughness).

**Showcase.** Aggiunto `scenes/showcases/library-woods-v3.yaml`
(6-sfere: quercia / quartato medullary / curly maple max / pino
nodoso heavy / mogano flame / burr walnut). Lo showcase legacy
`library-woods.yaml` resta funzionante grazie alla migrazione dei
materiali.

**Tests.** `MarbleWoodStudioTests` sostituiti i test back-compat
legacy con 18 test mirati sui nuovi invarianti: range [0,1] sotto
stress, decorrelazione object seed, no-op per ogni knob a default,
profilo asimmetrico (latewood al frac > 0.7), variazione anello-su-anello,
mask packing (t,t,t), heartwood center vs edge, warp 0 vs 3 differs
materially, all-knobs-cranked NaN/Inf safe. Aggiornato
`TextureTransformTests.Wood_RingsRemainConcentric_WhenRandomizeOffset`
per disabilitare esplicitamente il warp+per-ring-variation (default ON
nel nuovo pipeline).

---

### ✅ Marble texture — riscrittura pro-grade (Arnold/Cycles/Mitsuba parity)

Sostituita la formula sin-carrier classica
`vein(p) = sin(scale·(p·axis)·freq + str·fBm(p))` con un pipeline
production-grade allineato ai renderer offline. Il vecchio algoritmo aveva
due bug strutturali di realismo: la portante sinusoidale garantiva
periodicità visibile lungo `vein_axis`, e una singola layer fBm non poteva
rappresentare la coesistenza di vene sottili e spesse.

**Nuovo pipeline (per shade).**
1. **Texture transform** — invariato.
2. **Geological fold (anisotropo).** `DomainWarp.Anisotropic` con
   `fold_amplitude` per asse — la componente max è ruotata in modo da
   allinearsi a `vein_axis`. Simula lo shear tettonico a grande scala.
3. **Recursive (IQ) domain warp.** `DomainWarp.Recursive` itera
   `warp_iterations` volte (0 = no warp, 2 = canonico IQ, 3 = aggressivo).
   Uccide ogni tiling visibile sul vein field.
4. **Multi-scale ridged vein field.** `MultiScaleRidgedField.Sample` con
   1-3 layer ridged indipendenti, compositati via log-sum-exp soft-max
   numericamente stabile. Layer a scale decouplated → coesistenza
   thin+thick veins (Calacatta, Arabescato).
5. **Vein-thickness remap.** Smoothstep su `1 - thickness ± softness/2`.
   `vein_thickness` è strettamente monotono rispetto all'area visibile.
   Sostituisce il broken `vein_sharpness` (che faceva la potenza di una
   sinusoide normalizzata).
6. **Background variation.** fBm a bassa freq che sposta la ramp lookup.
7. **Impurità minerali.** Path inline Voronoi sparse + override esterno
   tramite `impurities_texture` (composabilità con qualsiasi pattern).
8. **Color ramp** o lerp 2-colori (convenzione INVERTITA vs legacy:
   stop 0 = base dominante, stop finale = vena rara).

**YAML schema.** Aggiunti `warp_amplitude/warp_scale/warp_iterations`,
`fold_amplitude/fold_scale`, `vein_layers/vein_scale/vein_weight`,
`vein_thickness/vein_softness`, `soft_max_sharpness`,
`background_scale/background_octaves`, `color_variation`,
`impurities_density/scale/weight/texture`. Rimossi `vein_frequency`,
`vein_sharpness`, `secondary_wave`, `noise_type` (marble), `distortion`
(marble) — non più parsati.

**Helper riusabili** in `src/RayTracer/Core/`: `DomainWarp.Recursive` e
`DomainWarp.Anisotropic` (pure functions su `(Perlin, Vector3, params)`,
nessuna allocazione), `MultiScaleRidgedField.Sample` (soft-max stabile
numericamente). Pensati per essere riusati dal futuro rewrite di
`WoodTexture` (grain flow, knot anisotropy, figure layer).

**Tests** (`MarbleWoodStudioTests.cs`). Eliminati i 6 test legacy
tied alla semantica sin-carrier; aggiunti 9 test sul nuovo sistema:
output `[0,1]`, decorrelazione per `objectSeed` (MAD > 0.05), non-
periodicità del campo lungo `vein_axis` (variance > 0.002 su 32
samples), monotonia di `vein_thickness`, gate `impurities_density=0`
(bit-identity vs baseline), override `impurities_texture`, determinismo
`warp_iterations=0`, effetto di `warp_iterations=3` (MAD > 0.05), no-
NaN sotto stress con tutti i knob al massimo. 446/446 verdi.

**Sweep librerie.** Migrati i ~60 materiali marble in
`scenes/libraries/materials/{stones,grounds,organics,plasters,minerals-gems}.yaml`
ai nuovi parametri, con look pro-grade per ogni classe (Carrara thin,
Calacatta 3-layer, Arabescato chaos, Verde Alpi inclusions, ecc.).
Nuovo showcase `scenes/showcases/library-marbles-v3.yaml` con 6 sfere
lookdev. Le scene esistenti che importavano questi materiali ora
rendono con il look pro nuovo — back-compat di parser garantita (i
campi legacy rimossi sono semplicemente ignorati).

**Performance.** ~26 sample Perlin per shade (vs ~7 nel sin-carrier).
Default `vein_layers: 2` e `warp_iterations: 2` scelti conservativi;
per recipe "preview" abbassare a `vein_layers: 1` e `warp_iterations:
1`. Sul rendering totale (BSDF dominante) impatto ~10-20%.

**Docs** aggiornati: `docs/reference/scene-reference.md` + IT,
`docs/tutorial/{en,it}/03-materials.md` (sezione marble + recipe book +
walkthrough 3.8.1 Step 2-4). README aggiornato in nota separata.

### ✅ Marble texture — patch 2: mask output, space stretch, cracks Worley

Tre estensioni del nuovo procedural marble nate dal feedback "manca poco
al livello AAA" sui primi render:

1. **`output: "color" | "mask"`** — opzione che fa restituire alla texture
   lo scalare vena `t ∈ [0, 1]` impacchettato come `(t, t, t)`. Pensata per
   essere appesa sotto `roughness_texture`, `subsurface_texture`,
   `sheen_texture` ecc. del Disney material: lo stesso pattern che pilota
   il colore guida adesso anche parametri scalari del BSDF. Sblocca il salto
   "marmo dipinto → marmo lavorato": vene quasi a specchio sulla base matte,
   SSS che si attenua sotto le vene calcite scure. Costo: il blocco marble
   va duplicato (~26 sample Perlin extra/shade) — trascurabile contro il
   resto del path tracer.

2. **`space_stretch: [x, y, z]`** — pre-multiply lineare sul sample point,
   applicato PRIMA del fold + warp. Produce compressione direzionale
   anisotropa: `(0.5, 1.6, 1.0)` per Verde Alpi (bedding orizzontale),
   `(1.0, 0.5, 1.0)` per Statuario (vene verticali). Default `(1, 1, 1)` =
   identità, no-op bit-identico.

3. **`cracks_density / cracks_scale / cracks_softness / cracks_weight`** —
   overlay Worley F2 − F1 layered sopra il campo ridged via soft-max
   numericamente stabile. Produce le fratture lineari nette tipiche di
   Marquinia, Calacatta, brecce — il ridged multifractal organico non può
   raggiungerle perché ha statistiche troppo curve. `cracks_density: 0`
   (default) salta del tutto la valutazione Worley.

**Update libreria.** Aggiunti `cracks_*` a `dis_nero_marquinia_lucido`,
`dis_calacatta_studio*`, `dis_arabescato_studio*`. `dis_calacatta_studio_lucido`
ora esibisce la roughness mask-driven come esempio canonico per gli
utenti.

**Update showcase.** `library-marbles-v3.yaml` ora dimostra tutte le 6
sfere con `roughness_texture` mask-driven, una sfera (Calacatta) con anche
`subsurface_texture` mask, e cracks attivi su Marquinia/Calacatta/Arabescato.
SpaceStretch attivo su Verde Alpi (orizzontale) e Statuario (verticale).

**Tests.** 6 nuovi test:
* `Marble_OutputMask_PacksScalarAsGrayscale` — R == G == B == t.
* `Marble_OutputMask_MatchesColorPathScalar` — mask coerente con il
  colour-path quando vein/base sono 0/1 (sanity invariant).
* `Marble_SpaceStretch_ProducesDirectionalCompression` — MAD > 0.02 vs
  baseline.
* `Marble_SpaceStretchOne_BitIdenticalToBaseline` — `(1,1,1)` no-op.
* `Marble_CracksDensityZero_BitIdenticalToBaseline` — `0` skip path.
* `Marble_CracksDensityPositive_AddsLinearVeinage` — MAD > 0.03 vs
  baseline.
Anche il test `AllKnobsCranked` ora attiva space_stretch+cracks al max
per coprire NaN safety. 452/452 verdi.

**Docs.** `docs/reference/scene-reference.md` + IT estesi con la
sezione "mask-driven Disney parameters", anisotropic stretch vs fold,
cracks vs vein layers; recipe Calacatta lucido canonica.

### ✅ Ground — overhaul pro-grade (Arnold/Cycles/Mitsuba parity)

Riscrittura della feature `world.ground:` per portarla al livello dei renderer
offline. Prima era un blocco minimo (`type` ignorato, `material`, `y` shorthand)
che produceva sempre e solo un `InfinitePlane` y-up. Ora è un dispatcher pieno
con quattro shape, materiale anonimo, UV transform e flag di visibilità per
categoria di raggio. Compat completa: tutte le scene esistenti continuano a
renderizzare identiche.

**Dispatcher (`SceneLoader.BuildGround`).**
- `type: infinite_plane | plane | quad | disk | heightfield | terrain`
- `point` / `normal` configurabili (fix del bug silente di `tempio-romano.yaml`,
  che già scriveva `point:`+`normal:` ignorati dal parser).
- `size` per quad/disk; `bounds`/`height_scale`/`heightmap_path`/`height_texture`/
  `resolution`/`sea_level`/`sea_material`/`strata` per heightfield (stesso set
  di parametri della entity `heightfield` esistente).
- `orientation` Euler/quaternion (parità sky).
- Type sconosciuto → fallback a `infinite_plane` con warning esplicito.

**Material resolution a tre bande.**
1. `material:` ID — comportamento legacy.
2. Inline shorthand `color/roughness/metallic` → Disney BSDF anonimo (stesso
   pattern del `standard_surface` floor di Arnold).
3. Auto-sync con `sky.ground_albedo` o `sky.ground_color` quando entrambi i
   precedenti mancano (parità `aiSkyDomeLight` preview).
4. Fallback grigio Lambertian.

**UV transform (`UvTransformedHittable`).** Wrapper che remappa `(u, v)` su
ogni hit con scale → offset → rotation attorno a `(0.5, 0.5)`. Aggiorna anche
`DpDu`/`DpDv` (inverso dello scale, ruotati) e tangent/bitangent per mantenere
TBN-consistency e footprint texture corretti. Parametri YAML: `uv_scale`,
`uv_offset`, `uv_rotation`.

**Visibility flags (`HitVisibilityMask` + `VisibilityFilteredHittable`).**
Le 5 categorie (`camera/diffuse/glossy/transmission/shadow`) sono esposte
con la stessa grammatica delle visibility flags sky. Implementazione:
- `HitRecord.VisibilityMask` (bitmask byte) — `CameraInvisible` resta come
  bridge sul bit `Camera` per compat con `CameraInvisibleHittable`.
- `TraceRay` ha un parametro `incomingCategory` (default Camera) e il vecchio
  loop di camera-invisible skip è generalizzato a tutte le categorie. La
  classificazione del raggio post-scatter è in `ClassifyScatteredRay` (delta
  + emisfero della direzione scattered).
- `ShadowRay.Transmittance` skip-pa hits flaggati `Shadow`.

**BVH/wrapper transparency.** `IsInfinitePlane` segue i nuovi wrapper
(`UvTransformedHittable`, `VisibilityFilteredHittable`, già
`CameraInvisibleHittable`) tramite property `Inner` pubblica, così un
infinite-plane wrappato resta fuori dalla BVH (la sua AABB 1e6³ degraderebbe
la qualità del tree).

**Test** (`GroundTests.cs`, 12 casi).
Legacy shorthand, type dispatch (`quad`/`disk`/unknown→fallback),
normal/point configurabili, material shortcut (Disney anonimo), UV transform
(scale + offset), visibility flags (Camera/Shadow + CameraInvisible bridge),
auto-albedo sync con sky.

**Migration note.** Zero breaking change. Le scene con il vecchio schema
(`type`/`material`/`y`) continuano a produrre lo stesso InfinitePlane.
`tempio-romano.yaml` ora interpreta correttamente i `point:`+`normal:` che
prima venivano scartati (potrebbe renderizzare leggermente diverso — miglioramento atteso).

---

### ✅ Sky / Environment — overhaul pro-grade

Riscrittura completa del sistema sky/environment per allinearlo agli standard
offline (Arnold, Cycles, Renderman, Mitsuba). `SkySettings` resta come nome
pubblico per non rompere call site, ma internamente è ora un wrapper attorno a
una nuova interfaccia `ISkyModel` con implementazioni concrete sotto
`src/RayTracer/Rendering/Sky/`:

- **`FlatSky`** — uniforme su sfera. Importance-sample uniforme se non nero.
- **`GradientSky`** — gradient verticale zenith/horizon/ground + sole analitico
  opzionale. Convenzione `direction` corretta: punta TO sun (l'inversione legacy è rimossa).
- **`PreethamSky`** — Preetham/Shirley/Smits 1999. API compatibile Hosek-Wilkie
  (`turbidity`, `ground_albedo`, `sun.direction`). YAML accetta `type: hosek_wilkie`
  o `type: preetham`; oggi sono alias. Coefficienti Y/x/y conversi xyY→CIE XYZ→Rec.709,
  trasmittanza Rayleigh per il colore del sole. Sostituibile con tabelle HW
  complete con un solo file.
- **`HdriSky`** — wrapper IBL su `EnvironmentMap`, ora supporta sun-extracted via
  `HdriSunExtractor`.

Sopra l'`ISkyModel` ci sono:

- **Orientation** — quaternion (rispetto al precedente `rotation` solo Y);
  `orientation.euler [x,y,z]` o `orientation.quaternion [x,y,z,w]` in YAML.
- **Visibility flags** — `camera / diffuse / glossy / transmission / shadow`
  (parità Cycles "Ray Visibility" / Arnold `visibility.*`).
- **Background separato** — `background:` block opzionale (sub-sky model)
  mostrato ai raggi camera mentre `lighting` resta l'illuminazione effettiva.
- **`SunCamera`** flag — nasconde il disco solare dai raggi camera ma lo lascia
  attivo come sorgente luminosa (off-camera key light setup).

**Sole disaccoppiato.** Quando un sky model espone `HasAnalyticalSun=true`, il
`SceneLoader` registra automaticamente un nuovo `PhysicalSun` (`ILight`) accanto
all'`EnvironmentLight`. Cone sampling stratificato, PDF `1/(2π(1-cosα))`,
opzionale limb darkening Hestroffer 1997. Il body del sky escude il sole sui
bounce non-delta (parità Cycles "HDRI sun extraction") per evitare doppio
conteggio — quindi BSDF specular delta riflette il sole, NEE indiretta lo
illumina, niente double-count.

**Loader.** Supporto OpenEXR (scanline RGB, ZIP/ZIPS, half+float) tramite nuovo
`Textures/ExrLoader.cs`; dispatcher per estensione (`.hdr` → HdrLoader, `.exr`
→ ExrLoader). `EnvironmentMap` clamp dei pixel negativi al load (sicurezza EXR
contro NaN/Inf), espone `CopyPixels` per il sun extractor.

**Sun extractor.** `Textures/HdriSunExtractor.cs` rileva il picco di luminanza
solid-angle-weighted, ne stima direzione + angular radius + radianza totale,
in-paint dei pixel del sole con la media circolare a 2× il raggio, restituisce
i parametri del `PhysicalSun` da accoppiare. Opt-in via `sky.sun.extract_from_hdri: true`.

**Migration note.** La convenzione `sun.direction` ora è "direction TOWARDS the
sun" (prima il codice la invertiva internamente). Scene che dipendevano dal vecchio
flip vedranno il sole dal lato opposto — fix banale invertendo il vettore.

Stato test: `dotnet test` 420 verdi (406 + 14 nuovi in `SkyEnvironmentTests.cs`).

#### Completamenti ciclo 2 (tutto ✅)

- **`NishitaSky`** completato (Bruneton-style precomputed transmittance LUT 16×64,
  single-scattering integrazione 16 step lungo il view ray, Rayleigh + Mie HG-0.76,
  earth-scale atmosphere reale 6360 km/8 km/1.2 km). Sample integra correttamente
  alba/tramonto da fisica (Rayleigh 1/λ⁴), zenith blu al mezzogiorno, halo solare
  arancione. Compatibile con `type: nishita` in YAML; turbidity remappata su densità
  Mie. Aerial perspective via medium completato nel ciclo 3 (`NishitaAtmosphereMedium`).
- **`PortalLight`** completato (Bitterli/Wyman/Pharr 2015). `ILight` con
  campionamento area uniforme stratificato sulla finestra, conversione area→solid-
  angle `pdf = d²/(area · cosPortal)`, MIS PDF analitica, ricezione orientata
  (back-face rejection sulla normale del portal). YAML: `type: portal`,
  `anchor + u + v` o legacy `corner + u + v`. Quando il portal punta verso un sky
  HDRI/fisico, la `LightDistribution` power-weighted lo seleziona quasi sempre
  sugli interni (riduzione varianza ~10× sui 95% di NEE che prima sprecava sui muri).
- **Mipmap prefiltering HDRI** completato (lazy build, sin(θ)-weighted 2×2 box,
  log₂ levels). `EnvironmentMap.SampleMip(direction, lod)` con trilinear tra livelli,
  esposizione `MaxMipLevel`. Hook nel BSDF roughness→LOD completato nel ciclo 3 (glossy LOD).
- **Tabelle Hosek-Wilkie complete** — non implementate in questa sessione (28KB di
  costanti tabulati per RGB×9 coefs×2 albedos×10 turb×6 control points). Per ora il
  YAML `type: hosek_wilkie` aliasa a Preetham. Per upgrade futuro è sufficiente
  sostituire `PreethamSky.cs` con un parser dei dati Hosek embedded come risorsa.

Test totali: 427 verdi (420 + 7 nuovi per Nishita/Portal/Mipmap).

#### Completamenti ciclo 3 (tutto ✅)

- **`NishitaAtmosphereMedium`** completato (`src/RayTracer/Volumetrics/`).
  `IMedium` adapter che condivide i coefficienti fisici con `NishitaSky`:
  Rayleigh (σ wavelength-dependent, scale height 8 km) + Mie (grey, 1.2 km,
  σ_a ≈ 0.11·σ_s). Optical depth in forma chiusa (somma di due esponenziali),
  delta tracking con majorante alla quota più bassa del segmento per il free-
  path sampling. Phase function di default HG g=0.76 (Mie forward), override
  via YAML `phase:`. World-to-atmosphere mapping configurabile (`world_scale`,
  `sea_level_y`). YAML: `world.medium.type: atmosphere | nishita | aerial_perspective`.
- **Glossy roughness → SampleMip LOD** completato. `SkySettings.Sample` accetta
  un `mipLod` opzionale; quando il modello è `HdriSky` e LOD>0, ruota su
  `EnvironmentMap.SampleMip` invece di `EvaluateRadiance`. Il `Renderer.SampleSky`
  deriva il LOD da `prevBsdfPdf` con la heuristica
  `lod = 0.5·log₂(W·H / (4π·pdf))`, clamped a `[0, MaxMipLevel]`. Bounce delta
  (pdf=0) e bounce camera (prevIsDelta=true) usano LOD 0 (sharp). Per bounce
  glossy con BSDF lobe ampio (low pdf), il LOD sale fino al livello che copre
  tutto il footprint angolare del lobo — elimina i firefly sui peak HDRI
  senza bias percettibile.
- **Tabelle Hosek-Wilkie complete** — **decisione: non implementate, non
  necessarie**. Motivazione onesta:
  - `NishitaSky` supera HW dove HW vince su Preetham (alba/tramonto, ground bounce):
    Nishita integra single-scattering dai primi principi, HW è solo un fit
    polinomiale a quei dati.
  - Per il midday clear-sky, Preetham (già esposto come alias `hosek_wilkie`)
    è entro il 3-5% di HW.
  - 28 KB di costanti tabulati hardcoded sarebbero un rischio di typo
    silenzioso ad alto impatto.
  - La coverage attuale (Flat / Gradient / Preetham-as-HW / Nishita / HDRI+sun-
    extraction) copre ogni use case di produzione.
  L'alias YAML `type: hosek_wilkie` → Preetham resta per ergonomia (gli utenti
  Arnold/Cycles lo digitano per riflesso).

Test totali: 434 verdi (427 + 7 nuovi: NishitaAtmosphereMedium transmittance +
density, glossy LOD smoothing, e copertura aggiuntiva).

### ✅ CLI — preset `--quality` / `-q`

Aggiunto un flag CLI che impacchetta in un colpo i cinque knob di qualità (`-w -H -s -d -S`) in preset con nome. Dieci preset: `draft-tiny` / `draft-small` / `draft` (480×270 · 960×540 · 1920×1080, `-s 16 -d 4 -S 1`), `medium-tiny` / `medium-small` / `medium` (`-s 128 -d 6 -S 1`), `final-tiny` / `final-small` / `final` (`-s 1024 -d 8 -S 4`), `ultra` (3840×2160, stessi sampling dei final). Qualunque flag esplicito ha la precedenza sul preset, quindi `-q final -d 16` resta possibile per scene con vetri impilati. Implementato come tipo nested `Program.QualityPreset`, parser case-insensitive, errore esplicito su valori sconosciuti. Documentazione: `docs/reference/rendering-profiles.md` + `profili-di-rendering.md` §1a, tutorial cap. 02 (EN/IT), `README.md` Quick Start + tabella CLI + sezione esempi pratici.

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
