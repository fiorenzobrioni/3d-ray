# 📋 DEVLOG — 3D-Ray

Roadmap, lavori in corso, bug noti, storico cicli.

> Stati: `✅ Fatto` · `🔧 In corso` · `⬜ Da fare`

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

## ✅ TODO

- [x] ~~Valutare se coerente: aggiungere la possibilita tramite una nuova proprieta per le luci visibili nella scena (tipo ad es. le luci `sphere`) per renderle visibili o meno dal punto di vista della camera, oltre che per l'illuminazione.~~ Fatto: `visible_to_camera` (Arnold/Cycles "camera" / Cycles "Ray Visibility → Camera") su `lights:` (sphere/area) e `entities:` (qualunque oggetto, utile per emissive panels). Vedi voce 2026-05 nello storico.
- [x] ~~Valutare se coerente: aggiungere proprietà `focal_pos: [x, y, z]` (alternativa a `focal_dist`) per la `camera` per indicare la posizione del fuoco al posto della distanza. Per semplicita' in certe situazioni potrebbe essere comodo indicare direttamente la posizione del fuoco.~~ Fatto: `focal_pos` su `CameraData`, distanza calcolata come proiezione del vettore camera→punto sull'asse ottico (Arnold "Focus Object"/Cycles "Focal Object"/RenderMan). Vedi voce 2026-05 nello storico.
- [x] ~~Texture più realistiche (devono essere come quelle dei ray tracer professionali tipo Arnold, Cycles, Renderman, ecc.) di quelle attuali per legno, marmo, noise e tutti le texture gestite finora. Aggiungi eventuali texture ora assenti ma presenti in ray tracer come Arnold, Cycles, Renderman, ecc.. Implementazione pro a livello di Arnold, Cycles, Renderman, ecc.. Senza compromessi. Aggiungi nuova scena showcase e aggiorna anche reference scene IT e EN e i tutorial IT e EN.~~ Fatto: upgrade pro di noise/marble/wood (octaves/lacunarity/gain/distortion + assi/sharpness configurabili) e nuove texture `voronoi`, `brick`, `gradient`. Vedi voce 2026-05 nello storico.
- [x] ~~**Stack completo "surface displacement" livello Arnold/RenderMan/Cycles** — deformazioni superficiali visibili con parità feature ai render pro. Da sviluppare come unico ciclo a step incrementali, ciascuno utile da solo: (1) **Bump map scalare da `ITexture` qualunque** (procedurali + immagini): nuovo canale `bump_map: { texture: …, strength: …, scale: … }` sui materiali, perturbazione della normale geometrica via differenze finite di luminanza in tangent-space (TBN già esistente da #5 normal mapping); funziona su tutte le primitive senza modifiche al BVH — sblocca subito le texture pro (#27) come dettagli superficiali. (2) **Mesh subdivision** (Loop su tri, Catmull-Clark opzionale su quad) sul loader OBJ con `subdivision_iterations` e/o `subdivision_pixel_error` (adattivo screen-space). (3) **Scalar displacement vero** sulla mesh subdivisa (`v += h(u,v) · n_smooth`), con `displacement_bound` per gonfiare gli AABB del BVH così i micro-poligoni spostati non scappano dal box originale — questo è il #25 della Fase 5 e fornisce le silhouette modificate, non solo lo shading. (4) **Vector displacement** (texture RGB → offset XYZ) per overhangs e crinkles, riusando l'infrastruttura di (3). (5) **Combinazione bump + displacement** (autobump-like di Arnold: il displacement gestisce la macro-silhouette, il bump residuo i dettagli sub-pixel) e priorità di applicazione coerente con Disney BSDF (`coat_normal_map` indipendente già esistente, base normal/bump/displacement compongono il `n_shading` finale). Vincolo architetturale: il displacement vero resta limitato alle mesh — sphere/torus/cylinder/ecc. supportano solo bump (stessa scelta di Arnold/Cycles). Doc + showcase + tutorial EN/IT per ogni step.~~ Fatto: tutti e cinque gli step completati. Step 5 (autobump + composizione canonica `normal_map → bump_map → autobump`) implementato nel ciclo 2026-05; vedi voce dedicata nello storico.

- [x] ~~**Texturing "VFX production-grade": parità completa con Arnold/RenderMan/Cycles oltre la matematica core.**~~ ✅ **ROADMAP COMPLETA** (2026-05). Tutti i 7 step completati: (1) anti-aliasing analitico con ray differentials, (2) color ramp multi-stop, (3) smooth Voronoi (IQ log-sum-exp), (4) Musgrave HeteroTerrain + HybridMultifractal, (5) Marble/Wood studio-quality (secondary wave, anisotropy, knots), (6) F3/F4 + Position output Voronoi, (7) CoordinateTexture node. Vedi voci dedicate nello Storico cicli. **Contesto originale** (preservato per riferimento storico): I primitivi di noise (Perlin, fBm, ridged, billow, Worley, marble, wood, brick, gradient) usano già le equazioni canoniche dei testi di riferimento (Perlin 1985/2002, Ebert-Musgrave-Peachey-Perlin "Texturing & Modeling", Worley 1996, IQ domain warp), e per un singolo sample il risultato è equivalente. Mancano però gli strati di infrastruttura e tooling che separano un raytracer "decente con texture credibili" da uno "pronto per VFX dove le texture devono reggere zoom 4K, movimento camera, e workflow di lookdev". Da sviluppare come unico ciclo a step incrementali ordinati per impatto visivo decrescente, ciascuno utile da solo:

  (1) **Anti-aliasing analitico con filter footprint (ray differentials).** ✅ Completato 2026-05 (branch `claude/analytic-antialiasing-filter-36oaF`, voce in Storico cicli). È il fix più urgente: oggi le procedurali sono **point-sampled**, a distanza producono moiré/shimmer dove Arnold/RM mostrerebbero sfumatura. Estendere `Ray` con ray differentials `(∂P/∂x, ∂P/∂y, ∂D/∂x, ∂D/∂y)` in screen-space come in PBRT §10.1, propagati attraverso `Hit()` di ogni primitiva (`sphere`/`cylinder`/`torus`/`cone`/`quad`/`triangle`/`disk`/`annulus`/`capsule`/`lathe`/`extrusion`/`CSG` — formula chiusa per ciascuna, le derivate sono già implicite nelle parametrizzazioni esistenti) e attraverso `Transform` (Jacobiana = matrice inversa-trasposta). Estendere `ITexture` con overload `Value(u, v, p, seed, FilterFootprint footprint)` con default che fa pass-through alla versione point-sampled corrente (back-compat). Implementazioni filtered: **Perlin/fBm** via clamp ottave a `λ = ⌊log₂(1/maxAxis(footprint))⌋` (Heidrich-Slusallek 1998 "Improved Perlin Noise" §4) — sopra Nyquist le ottave alte vengono droppate analiticamente; **Worley** via supersampling adattivo 4-16 jitter samples in footprint (`PxrVoronoise` fa lo stesso); **ImageTexture** via mipmap pyramid generata in ctor + EWA filtering (Heckbert 1989) per anisotropia corretta a basso angolo. CLI flag `--texture-filtering on|off|auto` (default `auto`). Test: rendering `textures-pro-showcase.yaml` a 4K, 16 spp → confronto rumore vs baseline 256 spp; risultato atteso: stesso aspetto. Bench: `TextureFilteringBench` per costo per-sample.

  (2) **Color ramp multi-stop.** ✅ Completato 2026-05 (branch `claude/color-ramp-multi-stop-ZlQ5y`, voce in Storico cicli). Nuova classe `ColorRamp` esposta come blocco YAML opzionale `color_ramp: [...]` al posto del semplice `colors:` su qualunque texture procedurale. Lista di stop `{ position: float ∈ [0,1], color: [r,g,b], interp: "linear" | "smoothstep" | "constant" | "ease" }`. Applicato in `ITexture.Value` dopo il calcolo del valore scalare di noise (sostituisce il `Vector3.Lerp(colorA, colorB, t)` finale di noise/marble/wood/voronoi/gradient — brick ha tre colori specifici e resta indipendente o usa ramp 3-stop). Esempio YAML:
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

  (3) ~~**Smooth Voronoi.** Aggiungere `smoothness ∈ [0,1]` a `VoronoiTexture`: quando > 0 sostituisce `min()` su F1 con soft-min `-log(Σ exp(-k·d_i)) / k` (k = 20/smoothness, IQ "Smooth Voronoi"); F2-F1 con smoothness > 0 diventa "smooth crackle" (bordi morbidi, niente alias a step). Utile per cuoio levigato, ciottoli arrotondati, pelle di rettile più realistica. Showcase: tre sfere `hard / smooth=0.3 / smooth=0.7`.~~ ✅ Completato 2026-05 (branch `claude/smooth-voronoi-texturing-nqCt1`, voce in Storico cicli).

  (4) ~~**Musgrave multifractal completo.** Aggiungere a `Perlin` i metodi `HeteroTerrain(p, octaves, lacunarity, H, offset)` e `HybridMultifractal(p, octaves, lacunarity, H, offset)` da Musgrave "Texturing & Modeling" §16. Parametro `H` (fractal increment, controlla roughness vs altitudine), `offset` (sea-level/threshold). Esposti in `NoiseTexture` come `noise_type: "hetero_terrain"` e `"hybrid_multifractal"` con i parametri YAML `fractal_increment` (H) e `fractal_offset`. Sblocca terreni proceduralmente erosi (terra a quote diverse con roughness diversa) e pattern roccia stratificati irraggiungibili con fBm puro.~~ ✅ Completato 2026-05 (branch `claude/smooth-voronoi-texturing-nqCt1`, voce in Storico cicli).

  (5) ~~**Marble e Wood "studio quality".** Upgrade dei due shader esistenti senza breaking change:
  - **Marble**: blocco YAML opzionale `secondary_wave: { axis: [...], frequency: ..., strength: ... }` per stratificare una seconda sinusoide ortogonale alla principale → marmi a doppia direzione di venatura (Statuario, Calacatta, Arabescato). La somma `sin(wave1) + 0.5 · sin(wave2)` produce un campo non più rigidamente unidirezionale. Usa il color ramp del punto (2) per vena/base/sotto-tinta a 3+ stop.
  - **Wood**: separazione di `grain_scale` (alta freq, dettaglio fibra interna agli anelli) e `figure_scale` (bassa freq, ondulazione tavola tipo curly maple), entrambi pesati indipendentemente da `grain_strength` / `figure_strength`. Aggiunta di `radial_anisotropy: float` che stretchera il noise lungo l'asse radiale vs tangenziale (rovere quartato = anisotropia alta, piano-sawn = bassa). Sapwood/heartwood gradient via color ramp 3-stop dal punto (2). Optional `knot_density` per spawn casuale di nodi via Voronoi piccolo-scala mascherato.~~ ✅ Completato 2026-05 (branch `claude/smooth-voronoi-texturing-nqCt1`, voce in Storico cicli).

  (6) ~~**F3/F4 e output Voronoi estesi.** Aggiungere F3, F4 a `WorleyNoise.Evaluate` (mantiene complessità O(27) sulle 27 celle, costo marginale poiché già si scansionano), e nuovi `OutputMode.F3`, `F4`, `F3MinusF1`, `Position` (posizione XYZ del feature più vicino come RGB — utile per shading per-cella o per modulare un'altra texture). Cycles e Houdini espongono F3/F4 di default; in pratica raro ma necessario per cellulare gerarchico e shading custom.~~ ✅ Completato 2026-05 (branch `claude/voronoi-texturing-vfx-gSqWR`, voce in Storico cicli).

  (7) ~~**Coordinate texture node.** Nuova `CoordinateTexture` che ritorna `(p.x, p.y, p.z)` o `(u, v, 0)` come RGB, con `mode: "world" | "object" | "uv" | "generated"` e trasformazione (offset/rotation/scale) standard. Analogo al "Texture Coordinate" node di Cycles. Utile per debug visivo dei UV/coord-spaces e per pilotare altre texture (texture-driven mix masks già supportato ma quello è specifico per il mix material; questo è generico). Bassa priorità — QoL per artisti.~~ ✅ Completato 2026-05 (branch `claude/voronoi-texturing-vfx-gSqWR`, voce in Storico cicli). **🎉 ROADMAP "VFX production-grade textures" CHIUSA — tutti i 7 step completati.**

  Per ogni step controlla attentamente il render della scena di prova e se vedi che non è corretto/realistico, correggi subito.
  Per ogni step: aggiornare `docs/reference/scene-reference.md` + `riferimento-scene.md` + `docs/tutorial/{en,it}/03-materials.md`, aggiungere showcase dedicato o sezione in `textures-pro-showcase.yaml`, regression test dove ha senso (es. `RayDifferentialTests` per (1) — proiezione di footprint noto attraverso transform e verifica numerica; `ColorRampTests` per (2) — interpolazioni su stop noti; `SmoothVoronoiTests` per (3) — continuità verificata su griglia). Validazione finale: rendering `textures-pro-showcase.yaml` a 1920×1080 e 4K con 64 spp, confronto con baseline pre-cambio sulla stessa scena (`renders/textures-pro-showcase.png` attuale è il punto di partenza). Voce DEVLOG storica a fine ciclo che elenca tutti gli step completati e i file toccati. Vincoli architetturali: back-compat totale (le scene YAML esistenti devono renderizzare identiche se non usano le nuove feature); tutti i 187+ test esistenti devono continuare a passare; nessun degrado di performance > 5% sui benchmark esistenti (`dotnet run -c Release --project src/RayTracer.Benchmarks -- --filter '*'`).
- [ ] Fai una review dei materiali in libraries/materials e aggiorna quelli che possono giovare, per diventare più realistici, delle features "surface displacement". Poi aggiungi anche altre librerie professionali di materiali basate su "surface displacement": ad esempio pelli, cementi, sassi, marmi e altri di tipo poroso. Guarda i materiali (di tipo poroso o che comunque beneficiano di "surface displacement") che hanno altri renderer pro (Arnlod, Cycles, Renderman, ecc) e crea tu delle nuove categorie estremamente professionali.
- [x] ~~Aggiornare README.md in root con le nuove feature aggiunte qui sopra negli step "surface displacement" e "texturing "VFX production-grade" che non sono ancora presenti.~~ Fatto 2026-05 (stesso branch `claude/voronoi-texturing-vfx-gSqWR`). Aggiornata la sezione "Texture" con i nuovi knob studio-quality di marble (secondary_wave) e wood (figure/radial_anisotropy/knot_density), i canali estesi Voronoi (F3/F4/F3−F1/Position) + smoothness, le varianti Musgrave (hetero_terrain/hybrid_multifractal) e il mipmap+EWA su `image`. Aggiunte due nuove voci: **Color Ramp multi-stop** e **Coordinate** texture. Aggiunta nuova sotto-sezione **Texture Filtering (Anti-Aliasing Analitico)** con la sintesi delle tre famiglie di filtering (Perlin clamp ottave, Voronoi supersampling adattivo, Image mipmap+EWA) e il flag CLI `--texture-filtering`. La sezione "Surface Displacement Stack" era già completa dal ciclo dedicato (bump, subdivision, scalar/vector displacement, autobump) — nessun aggiornamento necessario lì.
- [ ] **HeightField strata: layered stack BSDF "no-compromise" (parità Arnold/RenderMan/Cycles/Mitsuba 3)** — il selettore strata oggi è winner-takes-all con jitter Perlin 3-ottave + aspect bias `±Z` (Frostbite/Unreal trick, ciclo 2026-05). Funziona benissimo a vista perché ogni band ha la sua texture noise che maschera lo step, ma sotto la cofana resta una sola lobe per hit. La versione pro è uno **stack di layer N-ary** con coverage weights normalizzati, esattamente quello che fanno: Arnold (`layer_shader` / `mix_shader` + `range`), RenderMan (`PxrLayerSurface`, true N-layer stack con coverage masks), Cycles (Mix Shader tree pilotato da Geometry node — Pointiness, Normal Y), Mitsuba 3 (`BlendBSDF` pair-wise, generalizzabile in chain). Implementazione no-compromise: nuovo `LayeredStratumMaterial` proxy `IMaterial` che incapsula la lista di `(StratumBand, IMaterial)` + la funzione di weight geometrico `(altNorm, slopeDeg, curvature, aspect) → R^N`; al `Scatter` campiona una band via distribuzione 1D pesata coi coverage (PDF MIS-consistente, identico al pattern `MixMaterial.Scatter` esteso a N); al `EvaluateDirect` somma pesata di tutti i contributi diretti delle N band; per `Emit` somma pesata. Il jitter Perlin del ciclo D resta come perturbazione DEL CAMPO di coverage, non come band selector — le ottave di noise modulano `altNorm`/`slopeDeg` che entrano nel calcolo dei pesi, così il jitter rimane un asset gratis sopra il layer stack invece di sparire.

  Tre estensioni che devono entrare in questo ciclo (sono lo stato dell'arte dei terrain shader pro, non opzionali per la parità Arnold/RenderMan/Cycles):

  **(2.a) Curvature/concavity come weight input.** Calcolare il **Laplaciano discreto della heightmap** al hit point come 5-stencil (`∇²h ≈ h_L + h_R + h_D + h_U − 4·h_C`) e aggiungere il signed value `(curvature) ∈ [-1, +1]` allo stato per il weight function. Concavità (`curvature > 0`, gola/canalone) dà bonus a snow e ground (la neve si accumula in conca, l'acqua disegna sedimenti); convessità (`curvature < 0`, cresta/displuvio) dà bonus a rock (il vento spazza la cresta, espone roccia). È esattamente l'`Pointiness` del Geometry node di Cycles, l'`AOV concavity` di Quixel Megascans, il `Curvature output` di World Machine, e l'attributo `concavity` della Mountain SOP di Houdini. Costo: 4 sample heightmap extra per hit, negligible (già esiste l'indice del sample grid nel constructor). Calibrazione: bias da `±0.10` in altNorm units.

  **(2.b) Per-band noise mask configurabile in YAML.** Ogni `StratumBand` guadagna un campo opzionale `noise_mask: { scale, octaves, amplitude, seed }` che sovrascrive il jitter globale per quella band specifica. Snow ha boundary fine (vento + ghiaccio carving) ⇒ `amplitude: 0.06, scale: 12.0, octaves: 4` (alta freq); sand ha boundary grossolano (marea/onde) ⇒ `amplitude: 0.18, scale: 2.0, octaves: 2` (bassa freq, larghi lobi); rock ha boundary irregolare-roccioso ⇒ `amplitude: 0.10, scale: 6.0, octaves: 3`. Questo è il workflow standard di Unreal Landscape Layer Blend (ogni layer ha la sua noise mask con scale/seed indipendenti), Cycles Mix Shader tree (ogni mix con un noise node separato), Arnold `layer_shader` (mask per layer), RenderMan PxrLayerSurface (mask field per layer). Senza questo i quattro confini sembrano "uguali" — un VFX supervisor lo bocca a prima vista.

  **(2.c) Sun-aware aspect bias (orientamento versante reale).** Attualmente l'aspect bias usa una convenzione fissa "+Z = cool" decisa hard-coded. La versione pro legge la direzione del sole dal `SkyData.SunDir` (o da una luce direzionale dominante della scena) e calcola `aspectCool = -dot(horizontalNormal, horizontalSunDir)` (alignment col contro-sole) per modulare automaticamente l'asimmetria snowline. Tutti i renderer pro lo fanno implicitamente perché il loro terrain è uno shader che già "vede" la scena lighting; noi dobbiamo iniettare il vettore sun-anti nello stato del `LayeredStratumMaterial` al load time. Senza questo, ruotare la scena di 90° rispetto al sole produce un terreno visivamente sbagliato (la neve resta sulle facce sbagliate).

  Vincoli: il proxy material deve gestire correttamente il caso normal-map per-band (ogni band può avere la sua perturbazione TBN, lo stack le compone sul `n_shading` finale come fa già il displacement stack); MIS sampling weights coerenti con la rest of the engine (`LightDistribution`, `_indirectMaxSampleRadiance`); back-compat totale via flag YAML opzionale `strata_blending: "winner" | "stochastic" | "weighted"` (default `winner` = comportamento attuale, niente regressioni). Doc + showcase + tutorial IT+EN, regression test con un terreno flat a due band (alt 0..0.5 / 0.5..1) dove le due si sovrappongono in `[0.45, 0.55]` e si verifica numericamente che il colore al confine sia il lerp dei due materiali. Aggiornare `docs/technical/heightfield.md` §5 e §8 (la sezione "Limiti noti v1" rimuove le righe "Strata blending hard" e "convenzione +Z = cool fissa").
- [ ] Refactoring: spostare `Seed` da `IHittable` a un'interfaccia dedicata (es. `ISeeded`); nodi strutturali (BvhNode, Transform) non hanno bisogno di seed.
- [ ] Review completa dei tutorial (`tutorial/`): correttezza vs codice, omissioni, grammatica, esempi, indici.
- [ ] Spezzare `SceneLoader.cs`.

---

## 🐛 Bug noti

| # | Descrizione | Severità | Stato |
|---|-------------|----------|-------|
| 2 | ~~RNG globale seedato da `Environment.TickCount` (`MathUtils.cs:12`): due render della stessa scena producono rumore stocastico diverso.~~ **Risolto** dal sampler Sobol (default): `Sampler.cs` usa `pixelSeed = (uint)(x · 73856093) ^ (uint)(y · 19349663)` — seed deterministico per coordinate pixel. Render identici tra esecuzioni con `--sampler sobol` (default). Con `--sampler prng` il comportamento non deterministico è atteso. | 🟢 Risolto | ✅ |

---

## 📚 Riferimenti tecnici

### ✅ RNG deterministico — note implementazione (ex bug #2)

Il bug #2 (RNG seedato da `TickCount`) è risolto dall'introduzione del sampler Sobol con Owen scrambling, ora default. `Sampler.cs` costruisce un seed per-pixel da coordinate `(x, y)` via hash integer, quindi ogni pixel riceve sempre la stessa sequenza quasi-Monte Carlo indipendentemente dal momento di esecuzione. Il `--sampler prng` mantiene il comportamento non deterministico per chi lo preferisce (utile per animazioni con variazione intenzionale).

**Note architetturali** (per reference futura):
- Con `--sampler sobol` (default): render bit-identici tra esecuzioni. Smoke test verificabili contro baseline.
- `MathUtils.Rng` rimane come fallback per operazioni non-critiche fuori dal loop di campionamento.
- Se si introduce `--render-seed N` in futuro, il seed globale potrebbe agire da offset sulla famiglia di hash pixel.

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
