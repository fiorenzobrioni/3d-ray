# 🧭 PLANNING - 3D-Ray

Roadmap, lavori in corso, TODO, bug noti e idee. Per lo storico dei cicli di sviluppo e le note di design vedi la cartella [`devlog/`](devlog/).

> Stati: `✅ Fatto` · `🔧 In corso` · `⬜ Da fare`

---

## 🗺️ Roadmap

### Fase 0 - Fondamenta ✅

Path tracer multi-bounce, parallel render, BVH SAH, camera DOF + multi-camera, primitive base + trasformazioni, materiali Lambertian/Metal/Dielectric, luci Point/Directional/Spot/Area + NEE, Russian Roulette (standard dalla review Fase 7), stratified sampling, ACES + gamma + firefly guard, YAML loader, output PNG/JPEG/BMP, CI smoke test.

### Fase 1 - Visivo

| # | Feature | Stato |
|---|---------|-------|
| 1 | Emissive Material (diventa GeometryLight per NEE) | ✅ |
| 2 | Gradient Sky (zenith / horizon / ground + sun disc) | ✅ |
| 3 | Image Textures (PNG/JPG/BMP/GIF/TIFF/WebP, bilinear, tiling) | ✅ |
| 4 | IBL / HDRI (Radiance .hdr, CDF 2D marginal+conditional, rotazione Y) | ✅ |
| 5 | Normal Mapping (TBN + Gram-Schmidt, OpenGL/DirectX) | ✅ |

### Fase 2 - Materiali & geometria

| # | Feature | Stato |
|---|---------|-------|
| 6 | OpenPBR Surface e Disney tradotto su OpenPBR (vedi sotto) | ✅ |
| 7 | OBJ Mesh Loader (smooth normals, UV, TBN, BVH interno) | ✅ |
| 8 | Torus (quartica Ferrari, UV toroidale, NEE, CSG, Transform) | ✅ |
| 9 | Mix Material (selezione stocastica scatter, blend deterministico NEE, mask qualsiasi texture) | ✅ |
| 10 | Sphere Light (solid-angle sampling PBRT §6.2.3, 2-10× più efficiente di GeometryLight) | ✅ |
| 11 | Scene Graph / Groups (transform ereditate, template+instance, import YAML con merge) | ✅ |

**OpenPBR Surface** (`type: openpbr`, review Fase 5): metallo F82 con Kulla-Conty, dielettrico GGX/Walter, diffuso EON, subsurface con random walk, coat, fuzz (Zeltner 2022), film sottile, emissione, opacità, thin-walled; strati che conservano l'energia, tabelle di albedo offline. Il tipo `disney` è tradotto su OpenPBR (`DisneyMaterial`): stessi parametri, niente più energia creata da sheen e clearcoat. Dettagli in `PIANO-REVIEW.md` §3.38-3.39 e `docs/technical/shading-model.md`.

### Fase 3 - Sampling avanzato 🔧

| # | Feature | Stato |
|---|---------|-------|
| 12 | Importance Sampling (GGX su Metal/Disney, env via CDF 2D, cosine-weighted diffuse) | ✅ |
| 13 | Multi-Importance Sampling (tutti i materiali + phase function, balance/power heuristic) | ✅ |
| 14 | Adaptive Sampling | ⬜ (dopo #15; il layer dual-buffer A/B + `SampleCount` per pixel di #16 è la fondazione pronta) |
| 15 | Tile-based Rendering (tile 16×16, progress su thread reporter dedicato) | ✅ |
| 16 | Denoiser (NFOR: NL-means + regressione first-order guidata da albedo/normal/depth, dual-buffer cross-filtering, selezione MSE con safety net; `--denoiser nlm\|nfor`, default nei preset draft/standard/pre-final) | ✅ |
| 17 | HDR Output (PFM/EXR pre-tone-mapping) | ✅ PFM (`--aov`) + EXR (`-o *.exr` multilayer scene-linear, `--aov-format pfm\|exr`; writer `ExrImage` zero-dipendenze, half/float + ZIP) |
| +  | Sobol + Owen Scrambling sampler (`--sampler sobol`, default attivo) | ✅ |
| +  | Integratore iterativo (`Rendering/PathIntegrator.cs`, review Fase 7): throughput esplicito, Russian Roulette standard su β × η², profondità per tipo di lobo (`--depth-diffuse/-glossy/-transmission/-volume/-transparent`) | ✅ |

### Fase 4 - Cinematografici 🔧

| # | Feature | Stato |
|---|---------|-------|
| 18 | Motion Blur (trasformazioni animate + camera, `AnimatedTransform` TRS keyframe con slerp, `shutter` per-camera, bound shutter-union sul BVH statico, output bit-identico se nulla è animato) | ✅ |
| 19 | Volumetric Rendering | 🔧 Stage 1 + 1.5 ✅ |
| 20 | Subsurface Scattering | ✅ |
| 21 | CSG (union/intersection/subtraction, all-hits, normali corrette) | ✅ |
| 22 | Instancing (geometria condivisa, override material/seed per-istanza) | ✅ |
| +  | Extrusion primitive (linear/catmull_rom/bezier + twist + taper + caps) | ✅ |
| +  | Transparent shadow rays (vetro Fresnel-tinted in NEE - Strada 1; dal 2026-10-20 opzionale con `--transparent-shadows on`, senza doppio conteggio) | ✅ |

**Volumetrics Stage 1+1.5**: medium globale opt-in (`world.medium`), output bit-identico se assente. `IMedium`: Homogeneous (Beer-Lambert + free-path), HeightFog (densità esponenziale closed-form), HeterogeneousProcedural (Perlin fBm, delta+ratio tracking), Grid (`.vol` o inline, slab clip + trilinear + delta tracking). `IPhaseFunction`: Isotropic, HG, Rayleigh, Double-HG (Nubis), Schlick (fast-HG). Stage 2 (deferred): EmissiveMedium, MediumInterface per-entity, SSS random-walk, OpenVDB nativo, spectral tracking - tutti richiedono modifiche ad `IMedium` / `HitRecord` / `Renderer.TraceRay`.

### Fase 5 - Frontiera 🔧

| # | Feature | Stato |
|---|---------|-------|
| 23 | Path Guiding - PPG-style (SD-tree spaziale + quadtree direzionale per foglia, MIS tra guide PDF e BSDF PDF, `--path-guiding on\|off`, `--guiding-budget N` spp di training; dopo #13; il loop iterativo che lo rende possibile è fatto, review Fase 7; con MNEE/SMS per le caustiche da luci piccole: `PIANO-REVIEW.md` Fase 7b) | ✅ (Fase 7b: `--guiding on`) |
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
#15 Tiles   ─► #14 Adaptive, #16 Denoiser ✅ (i buffer dual-buffer di #16 servono #14)
```

---

## 🌟 Caustiche ✅

**Completate** - con gli integratori bidirezionali (review Fase 8b, 2026-10-24): `--integrator bdpt` (caustiche viste direttamente o in uno specchio, privo di bias) e `--integrator vcm` (anche viste attraverso vetro o acqua, consistente), su superfici e mezzi, zero flag YAML. La photon map (`--caustics on`, 2026-06) è stata tolta: stima di densità distorta e non consistente, solo interfacce lisce e luci analitiche. Dettagli in `PIANO-REVIEW.md` §4 Fase 8b e `docs/technical/bidirectional.md`. Resta fuori: il merging nei mezzi (caustiche volumetriche, fasci di fotoni).

---

## 📋 TODO

- [ ] **HeightField strata: layered stack BSDF "no-compromise"** - il selettore strata oggi è winner-takes-all con jitter Perlin 3-ottave + aspect bias `±Z`. La versione pro è uno **stack N-ary** con coverage weights normalizzati. Implementazione: nuovo `LayeredStratumMaterial` proxy `IMaterial` che incapsula la lista di `(StratumBand, IMaterial)` + funzione di weight geometrico `(altNorm, slopeDeg, curvature, aspect) → R^N`; `Scatter` campiona via distribuzione 1D pesata (PDF MIS-consistente); `EvaluateDirect` somma pesata. Back-compat via `strata_blending: "winner" | "stochastic" | "weighted"` (default `winner`).

  Tre estensioni obbligatorie per la parità con i terrain shader pro:

  **(a) Curvatura/concavità come weight input.** Laplaciano discreto della heightmap al hit (5-stencil) → `curvature ∈ [-1, +1]`. Concavità bonus a snow/ground; convessità bonus a rock. Costo: 4 sample heightmap extra per hit.

  **(b) Per-band noise mask configurabile.** Ogni `StratumBand` ottiene `noise_mask: { scale, octaves, amplitude, seed }` opzionale, che sovrascrive il jitter globale per quella band (snow: alta freq, sand: bassa freq, rock: media freq). Senza questo i confini sembrano tutti uguali.

  **(c) Sun-aware aspect bias.** L'aspect bias legge `SkyData.SunDir` e calcola `aspectCool = -dot(horizontalNormal, horizontalSunDir)` invece di usare `+Z` hardcoded.

  Vincoli: normal-map per-band composte nel `n_shading` finale; MIS sampling weights coerenti; doc + showcase + tutorial IT+EN; regression test terreno flat a due band con verifica del lerp al confine. Aggiornare `docs/technical/heightfield.md` §5 e §8.
- [ ] Refactoring: spostare `Seed` da `IHittable` a un'interfaccia dedicata (es. `ISeeded`); nodi strutturali (BvhNode, Transform) non hanno bisogno di seed.
- [ ] Review completa dei tutorial (`tutorial/`): correttezza vs codice, omissioni, grammatica, esempi, indici.
- [ ] Spezzare `SceneLoader.cs`.
- [ ] **Texture, rimandati dalla Fase 4 della review**: clamp delle ottave sul footprint per `marble` e `wood` (oggi campionate in un punto); compressioni EXR PIZ, DWAA/DWAB, B44, PXR24 e file a tile (oggi errore esplicito); UDIM e cache di texture a tile per scene grandi; l'autobump di una mesh dentro un transform scalato usa l'altezza del displacement in unità oggetto mentre il bump è in unità di scena (e la texture 3D del displacement è letta nello spazio oggetto non scalato); sui gradini piatti del lathe V va dal raggio interno all'esterno qualunque sia il verso del profilo; `UvTransformedHittable` ruota T e B come +U e +V (corretto ora che tutte le primitive li allineano, da ricontrollare se cambia una convenzione).
- [ ] **Review completa del motore** (matematica, ottica, fisica, formato scene): piano in [`PIANO-REVIEW.md`](PIANO-REVIEW.md), da eseguire per fasi (0 → 11). Fase 0 (infrastruttura di verifica) ✅, Fase 1 (fondamenta matematiche e sampler) ✅, Fase 3 (camera, film e colore) ✅, Fase 4 (texture e dati) ✅, Fase 5 (materiali e BSDF) ✅, Fase 6 (luci e cielo) ✅, Fase 7 (integratore) ✅, Fase 7b (path guiding) ✅, Fase 8 (volumi null-scattering) ✅, Fase 8b (BDPT e VCM al posto della photon map) ✅.
- [ ] **Luci e cielo, rimandati dalla Fase 6 della review**: scattering multiplo per il cielo di Nishita (Hillaire 2020, tabella di multi-scattering): oggi il suo cielo è circa metà di uno misurato rispetto al sole, e per questo la sua `turbidity` mappa su poca polvere invece che sulla torbidità di Linke; un falloff fisico del cono degli spot (oggi smoothstep artistica, la distribuzione fisica la dà l'IES); dati TILT dei file IES; modello Prague 2021 se servisse un cielo diurno sopra Hosek-Wilkie (crepuscolo, cieli torbidi).
- [ ] **Scene da rivedere alla fine della review** (decisione del proprietario; nessun vincolo di compatibilità, `PIANO-REVIEW.md` §2.0: le scene si correggono, si riscrivono o si spostano in `scenes/obsolete/`): le otto scene con un cielo fisico ora hanno il sole alla luminosità vera rispetto al cielo (`3d-ray-logo`, `extrusion`, `ground-alpine-lake`, `ground-feature-overview`, `sky-aerial`, `sky-nishita`, `sky-portal`, `sky-preetham`; prima/dopo in `scenes/review/images/sky-before-after.jpg`), e quelle con `hosek_wilkie` un modello diverso da Preetham; valutare il passaggio a unità fotometriche con `ev100` dove la scena descrive una situazione reale.

---

## 🐛 Bug noti

Emersi durante la stesura del piano di review (dettagli e posizioni in [`PIANO-REVIEW.md`](PIANO-REVIEW.md) §3):

- [x] **Preetham**: funzione di Perez con coefficienti sbagliati (`PreethamSky.cs`, §3.1): corretto 2026-10-22 (Fase 6).
- [x] **Disney BSDF**: peso del lobo diviso per `max(p, 0.1)` senza floor in selezione, energia sottostimata (§3.2): corretto 2026-10-21 (Fase 5), il `disney` è tradotto su OpenPBR.
- [x] **Texture dati** (roughness/metallic...) decodificate come sRGB (§3.3): corretto 2026-10-21 (Fase 4), `color_space` con default per ruolo.
- [x] **Vetro satinato** escluso da `Evaluate`/`Pdf`: niente NEE/MIS (§3.6): corretto 2026-10-21 (Fase 5), Walter 2007.
- [x] **Metal**: lobo lambertiano di riempimento non fisico (§3.7): corretto 2026-10-21 (Fase 5), Kulla-Conty.
- [x] **HDRI importance sampling** solo ai centri dei texel (§3.9): corretto 2026-10-20 (Fase 1), campionamento continuo dentro il texel.

Misurati nella Fase 0 della review (validazione contro Mitsuba 3, `validation/`):

- [x] **Raggi d'ombra che colpiscono il proxy della luce** (§3.19): corretto 2026-10-20, insieme alla precisione di campionamento e intersezione delle sfere.
- [x] **Clamp `-C 10` di default** (§3.20): corretto 2026-10-20, ora opzionale.
- [x] **Ombre trasparenti di default** (§3.21): corrette 2026-10-20, `--transparent-shadows on` opzionale e senza doppio conteggio.
- [x] **`Dielectric` con Fresnel di Schlick** (§3.24) e **tempo del motion blur non passato agli emissivi** (§3.25): corretti 2026-10-20.
- [x] **Metal**: lobo lambertiano solo nella NEE, rough metal +37% (§3.7): corretto 2026-10-21, `metal-rough` 1.0032 contro Mitsuba.
- [x] **Disney**: energia creata da sheen, clearcoat e vetro (§3.17); `diff_trans` -1% (§3.18): corretti 2026-10-21 con la traduzione su OpenPBR.
- [x] **Default `color` 0.5 anche per vetro ed emissivo** (§3.22): corretto 2026-10-21, ora bianco.
- [ ] Chiavi YAML sconosciute ignorate in silenzio (§3.23): Fase 10.

Fase 1 della review (fondamenta matematiche):

- [x] **Sampler Sobol** peggio del caso dalla seconda decisione in poi (§3.10): rifatto 2026-10-20 (Sobol "padded", dimensioni per vertice).
- [x] **Offset dei raggi fissi a 1e-4** (§3.27), **basi ortonormali di Frisvad** (§3.26), **`AABB.Hit` con NaN** (§3.28), **cubica con soglia assoluta** che fondeva radici vicine del toro (§3.29): corretti 2026-10-20.
- [x] **Perlin** con fade cubico (curvatura discontinua) e gradienti anisotropi (§3.30): corretto 2026-10-21 (Fase 4), improved Perlin di default, `noise_basis: classic`.
- [x] **Differenziali di raggio** senza il minimo 1/8 (§3.31): corretto 2026-10-21 (Fase 4). **`MathUtils.Refract`** con riflessione totale interna silenziosa (§3.32): corretto 2026-10-21 (Fase 5).

Fase 3 della review (camera, film e colore):

- [x] **Decodifica sRGB delle texture con potenza 2.2** (§3.4) e **tone mapping ACES per canale con gamma 2.2** (§3.5): corretti 2026-10-21 (curva sRGB a tratti, view transform AgX / ACES 1.x / standard, `legacy` per i render vecchi).
- [ ] **NFOR disegna un alone scuro** attorno a luci piccole e molto luminose sfocate (§3.33): mitigato 2026-10-22 (luci estreme tolte dal filtro, candidati che scendono sotto zero esclusi); resta una cucitura sottile lungo i bordi HDR. Fase 9 (OIDN).
- [x] **Esposizione fisica** (ISO, tempo di posa, EV100) senza unità delle luci (§3.34): fatta 2026-10-22 (Fase 6), con le unità fotometriche (§3.14).

Fase 4 della review (texture e dati), corretti 2026-10-21:

- [x] **Mipmap** che perdeva righe e colonne dispari e **livello** scelto in UV invece che in texel (§3.40, §3.41); **loader Radiance** senza orientamento e con mantissa troncata, **EXR** senza scala di grigi, RLE e segno nelle mappe dati (§3.42, §3.43).
- [x] **HDRI**: campionamento d'importanza che mirava al texel specchiato e sole estratto all'azimut specchiato senza conservare l'energia (§3.44, §3.45).
- [x] **Bump** con pendenza presa lungo l'asse sbagliato sugli oggetti ruotati e altezza per unità di UV (§3.46); **frame tangente**: ∂P/∂u mancante su dieci primitive, bitangente capovolta su tre facce del box, mesh senza tangenti per vertice (§3.47); **ottave filtrate** che aumentavano il contrasto del rumore lontano (§3.48).

Fase 6 della review (luci e cielo), corretti 2026-10-22:

- [x] **MIS delle luci**: probabilità di scelta assente dal lato BSDF (luce diretta al 76-89% con `power`/`uniform`), portali sommati all'environment (+7-19%), dischi irraggiungibili pesati, emissivi riconosciuti dal materiale invece che dalla superficie (§3.49-3.51, §3.53). **Stratificazione** con celle vuote (§3.52), potenza della sphere light senza raggio (§3.54).
- [x] **Mip pre-filtrato dell'HDRI sotto MIS**: +26% su un pavimento diffuso (§3.56).
- [x] **Sole dei cieli fisici circa 15 000 volte troppo debole** rispetto al suo cielo, oscuramento al bordo senza effetto (§3.57); `hosek_wilkie` alias di Preetham (§3.58).

Fase 7 della review (integratore), corretti 2026-10-24:
- [x] **Ultimo vertice** della profondità: NEE pesata dal MIS contro un rimbalzo mai tracciato (Cornell d2 0.9981 contro Mitsuba): ora il rimbalzo è seguito per l'emissione (§3.62, Fase 7b).
- [x] **Roulette** dal quinto rimbalzo invece che dal secondo: efficienza degli interni +25-55% (§3.63, Fase 7b).

- [x] **Categoria dei raggi** per i flag di visibilità sbagliata: uscite del SSS e scattering nei mezzi come "camera", glossy come "diffuse", cielo negli specchi come "camera" (§3.61).
- [x] **Russian Roulette** con soglie da una classificazione euristica della scena e soglia dei path morti (§3.8): ora standard, non distorta, senza euristiche.

Emersi nella Fase 6, per la Fase 8 (volumi):

- [x] **NaN/Inf nei mezzi** (§3.59): erano in tutte le 20 scene con mezzi; corretti con la Fase 8 (2026-10-24).
- [x] **Banda di firefly** del mezzo atmosferico verso il sole (§3.60): corretta con la Fase 8.

Emersi nella Fase 8 (volumi), tutti corretti: raggi camera non normalizzati nei mezzi (§3.64), raggi d'ombra che ignoravano i mezzi attraversati (§3.65), random walk con tetti e NEE limitata (§3.66, `--sss-quality` e `--max-volume-bounces` rimossi), colore SSS Disney senza inversione dell'albedo (§3.67), campionamento dei mezzi distorto per i canali e troncato (§3.68).

- [ ] **Estensioni dei volumi rinviate dalla Fase 8**: mezzi emissivi (fuoco, esplosioni), lettura di griglie VDB / NanoVDB, interfaccia di mezzo "null" dedicata (oggi un guscio `dielectric` con IOR 1), campionamento equiangolare anche per sphere e area light piccole (oggi solo point e spot, servirebbe il MIS a tre strategie con il campionamento della phase function).

---

## 💡 Idee / Appunti

Ottimizzazioni valutate nel ciclo Review e **rimandate** (da riprendere):
- **Traversata BVH a stack esplicito** - richiede un array di nodi appiattito +
  stack di indici locale (`stackalloc int[]`, immune a rientranza) invece dello
  stack di riferimenti condiviso per-thread, che si corromperebbe con i Group
  annidati. Vale come step verso adaptive sampling/packet tracing.
- **Clamp di ottave footprint-aware per Marble/Wood** - come `NoiseTexture.ComputeMaxOctaves`:
  riduce ottave (e aliasing) a distanza. Modifica di anti-aliasing più ampia, da
  validare su scene marmo-pesanti.
- **`HitRecord` più snello** - spostare `FilterFootprint`/`MediumInterface`/autobump
  fuori dallo struct core e calcolarli pigramente dopo l'hit finale.

Ottimizzazioni degli integratori bidirezionali possibili (Fase 8b):
- **Vertici di luce più leggeri** - oggi ogni vertice salva l'`HitRecord` completo
  (232 byte), da cui i blocchi di 2^18 sottocammini: un record ridotto (punto,
  normali, UV, materiale) permetterebbe di normalizzare il merging su tutti i
  pixel e di ridurre la memoria.
- **Light path per pixel del lato camera** - la connessione usa il sottocammino di
  luce dello stesso indice; più connessioni per vertice (o un sottocammino scelto
  a caso) ridurrebbero la correlazione fra pixel vicini.

---

## 🧪 Checklist verifiche

Da eseguire prima di un commit importante.

- [ ] **Smoke**: render `primitive-showcase.yaml` (16 spp), no crash.
- [ ] **Visual regression**: confronto `cornell-box.yaml` con baseline. ⚠️ Dal ciclo Denoiser i preset `draft*`/`standard*`/`pre-final*` includono `--denoiser nfor`: le baseline generate con quei preset vanno confrontate a parità di flag (o rigenerate); per un confronto col motore "puro" aggiungere `--denoiser none`.
- [ ] **Performance**: tempo render scena standard non +5% senza motivo.
- [ ] **YAML**: ogni nuova proprietà ha default sensato.
- [ ] **CSG**: render `csg-showcase.yaml` - union/intersection/subtraction visivamente corrette.
- [ ] **HDRI**: render `hdri-showcase.yaml` - riflessi/rifrazioni/GI corrette.
- [ ] **Mix**: render `mix-material-showcase.yaml` - blend costante (3 livelli), maschere (noise/marble/wood/checker), lava emissiva.
- [ ] **Group**: render `group-showcase.yaml` - transform ereditate, template/istanze, import.
- [ ] **Torus**: render `torus-showcase.yaml` con camera `pinhole`/`dof_soft`/`dof_extreme` - no contorni fantasma, occlusione torus/cone e torus/cylinder corretta.
- [ ] **Import**: materiali e template importati da file esterni funzionano.
- [ ] **Template override**: il materiale dell'istanza sovrascrive quello del template.
- [ ] **Transparent shadows**: render `cornell-box-spheres.yaml` con `--transparent-shadows on` - la sfera di vetro centrale proietta un alone Fresnel-tinted; senza il flag proietta ombra e caustica fisiche.
