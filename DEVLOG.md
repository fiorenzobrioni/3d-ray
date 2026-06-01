# 📋 DEVLOG — 3D-Ray

Storico dei cicli di sviluppo e note di design. Per roadmap, TODO, bug noti e checklist vedi [`PLANNING.md`](PLANNING.md).

> Stati: `✅ Fatto` · `🔧 In corso` · `⬜ Da fare`

---

## Ciclo Caustiche — niente più "base scura" (rimosso BlockCausticCasters) ✅

Con `--caustics on` la scena diventava **più scura e meno realistica** di
`--caustics off`: il vino quasi nero, il tavolo sotto il calice una macchia
spenta invece di un fuoco luminoso. Causa: `ShadowRay.BlockCausticCasters`
rendeva **opaco** ogni caustic caster allo shadow ray dritto di un receiver, per
non contare due volte la luce focalizzata (shadow trasparente + MNEE). Ma MNEE è
incompleto — sul vino *racchiuso* la connessione isolata è occlusa dal cristallo
(vedi ciclo IOR relativo), sulle interfacce quasi piatte non focalizza — quindi
il blocco **toglieva** la luce trasmessa diffusa senza che MNEE la
rimpiazzasse → grandi regioni scure.

**Fix**: rimosso del tutto `BlockCausticCasters` (campo thread-static in
`ShadowRay` + set/restore in `Renderer.ComputeDirectLighting`). Ora lo shadow
trasparente passa **sempre** attraverso i caster (riportando il fill di luce
trasmessa, `(1−F)·tint·Beer-Lambert`) e MNEE aggiunge il fuoco caustico **sopra**
invece di sostituirlo. Risultato: con caustiche on la scena non è mai più scura
di off, più i punti caustici focalizzati. Costo: un lieve double-count
dell'energia media trasmessa nella regione focale (il fuoco è un po' più
luminoso del fisicamente esatto) — accettabile e di gran lunga preferibile alle
regioni scure. La soppressione dell'emissione sul **path forward**
(`causticChain`/`suppressCausticEmission`) resta invariata: è un guard separato
per il cammino BSDF-sampled, non per lo shadow ray. `--caustics off` invariato
(bit-identico); 46 test caustiche/MNEE/SMS verdi.

## Ciclo IOR relativo — dielettrici annidati ✅

Risolto il "film d'aria spurio" tra due dielettrici a contatto. Prima la
rifrazione (direzione **e** Fresnel) assumeva sempre il vuoto fuori dalla
superficie — `eta = rec.FrontFace ? 1/n : n` in `Dielectric` e in
`DisneyBsdf.ScatterTransmission`, e `EtaOnSide` nel `ManifoldWalker`
hardcodava `η = 1` sul lato esterno. Un liquido dentro un vetro (il vino nel
calice di cristallo di `cristallo.yaml`) rifrangeva quindi come se fosse
immerso in aria, non nel cristallo che lo tocca: bordo e fuoco caustico nel
posto sbagliato, e un alone d'aria fittizio all'interfaccia vino/vetro.

Tre fasi, ognuna **bit-identica** sulle scene non annidate (verificato con MD5
prima/dopo su Cornell box con sfera di vetro e sulla scena SMS caustics).

**Fase A — path forward (camera/indiretto).** Nuovo `IorStack`: stack per-raggio
zero-alloc degli IOR dei dielettrici in cui il raggio si trova, gemello di
`MediumStack` ma alimentato dal **materiale** (un vetro liscio non ha medium
partecipante eppure ha IOR 1.5). `Top` = IOR del medium corrente (1.0 = aria a
stack vuoto), `Enclosing` = il medium un livello più fuori. `IMaterial.
TryGetDielectricIor` espone l'IOR dell'interfaccia (Dielectric sempre; Disney
solido con `spec_trans > 0`, non thin-walled). Il renderer risolve la `eta`
relativa prima di `Sample`/`Scatter` e la marca su `HitRecord.RelativeEta`
(front: `Top/n`; back: `n/Enclosing`); sentinella `0` = non impostata → i
materiali tornano alla forma legacy air-relative. Con aria fuori (`Top/Enclosing
= 1`) la relativa si riduce esattamente a `1/n` / `n`. Lo stack è threadato in
`TraceRay`/`ShadeSurface`/`ShadeSampleBounce` e nel random-walk SSS, con
push/pop alle stesse transizioni rifrattive del medium stack (e inferendo
l'attraversamento sul path legacy `Scatter` di `Dielectric`).

**Fase B — MNEE/caustiche.** `CausticInterface` guadagna `AmbientIor` (default
1.0) + helper `WithAmbientIor`. `EtaOnSide` usa `ci.AmbientIor` sul lato esterno
invece dell'`1` hardcodato; con `AmbientIor = 1` identico al legacy. Passato via
`ci`, già threadato in ogni metodo del walker → nessuna proliferazione di firme.

**Fase C — risoluzione del medium che racchiude un caster annidato.**
`TryGetCausticInterface` ora, quando il segmento dritto x→y colpisce un caster
trasmissivo, sonda un punto appena oltre la superficie (lungo la normale
geometrica esterna) e cerca il caster trasmissivo **più interno** (`ResolveAmbientIor`
+ `TryEnclosingCasterIor`, test di contenimento via back-face del primo
intersect) il cui solido contiene quel punto, marcando il suo IOR come ambiente.
Così il vino vede `AmbientIor = 1.70` sui fianchi appoggiati al cristallo e
`= 1.0` sul pelo libero esposto all'aria — l'ambiente corretto **per-hit**.
Questo resta nel modello MNEE esistente "un caster per connessione" (il vino e
la coppa sono caster separati, risolti indipendentemente): è l'approssimazione
dominante e robusta, non la catena 4-vertici multi-chart vera e propria.

**Limite noto / lavoro futuro.** La risoluzione dell'ambiente di Fase C è
corretta e generale (utile per caster annidati **parzialmente esposti**), ma sul
vino *completamente racchiuso* di `cristallo.yaml` è di fatto **inerte**: la
connessione MNEE isolata al vino (ricevitore→vino e vino→luce) attraversa il
cristallo che lo avvolge, quindi `SegmentOccluded` la **scarta** sempre (il
counter di probe conferma che l'ambiente 1.70 viene applicato a ~40% degli hit
del vino, ma il render 300×200 con/senza Fase C è byte-identico). Il fuoco
caustico rosso sul tavolo viene perciò dal **path forward** (Fase A, ora con IOR
relativo corretto), non da MNEE attraverso il vino. Il vero cammino a 4
interfacce su due dielettrici annidati distinti come **singolo** solve di Newton
multi-chart (aria→cristallo→vino→cristallo→aria) non è implementato:
richiederebbe `MaxVertices`/`MaxMneeInterfaces` a 4, IOR/assorbimento
per-segmento, seeding ordinato su chart diversi e una visibilità che attraversi
i caster intermedi — con rischio di convergenza/firefly elevato (vedi
`PLANNING.md`). Verifica forward (camera, caustiche off) su `cristallo.yaml`
prima/dopo: differenza visibile all'interfaccia vino↔cristallo; scene non
annidate bit-identiche.

## Ciclo Caustiche — auto-classificazione caster/receiver ✅

Tolto l'attrito del doppio opt-in. Prima le caustiche richiedevano **due** azioni:
il flag CLI `--caustics on` **e** i flag YAML `caustic_caster`/`caustic_receiver`
su ogni entità — faticoso da ricordare e posizionare a mano scena per scena.

**Ora un solo switch**: con `--caustics on` (default sui preset `final`/`ultra`) il
motore **classifica da solo** ogni entità. Receiver è il default per ogni superficie
(incluso il `world.ground`); caster diventa automaticamente un'entità la cui
**geometria** può focalizzare luce (`CanCastCaustics`: primitiva curva / mesh smooth /
CSG con frontiera curva) **e** il cui **materiale** è speculare/trasmissivo. Il gate
materiale è il nuovo helper `SceneLoader.MaterialCanCast`, che sonda
`IMaterial.GetCausticInterface(default).IsCaster` (Dielectric/Metal sempre caster;
Disney da `spec_trans ≥ 0.5` non thin-walled oppure `metallic ≈ 1`; la roughness pilota
solo `IsRough`). Un caster **non** è auto-receiver (superficie speculare liscia → nessuna
caustica visibile, solo lavoro MNEE sprecato): `receiver = !caster`.

**I flag YAML restano come override opzionali a 3 stati** (`bool?` in `SceneData`):
assente/`null` = auto; `true` = forza (un caster forzato richiede comunque geometria
focalizzante, altrimenti warning); `false` = escludi. Servono come *scalpello di
performance/regia* — su una scena con molti caster eleggibili (lampadario di cristalli)
si può limitare il casting a pochi oggetti hero pur tenendo `--caustics on`. CLI globale
e flag per-oggetto non sono ridondanti: uno è l'interruttore globale, l'altro lo scope.

**Refactor**: `ApplyCausticFlags(hittable, bool? casterOverride, bool? receiverOverride,
IMaterial material, …)` — rimosso l'early-return sul "nessun flag" così ogni entità fluisce
e ottiene `receiver = !caster`; il ramo off (`!_enableCaustics`) è invariato → **bit-identico
con `--caustics off`**. Ground a L456 riceve di default (`CausticReceiver != false`). Warning
su group/instance/template ora solo su opt-in **esplicito** (`== true`). Istanze/template
restano esclusi (geometria condivisa, niente stato caustico per-istanza).

**Costo**: con auto ogni superficie diffusa è receiver, quindi `ComputeCaustics` gira a quasi
ogni hit; ma la guardia esistente `_causticsActive = enableCaustics && _caustics.Count > 0`
rende il tutto **gratis** quando non si registra alcun caster, e `MaterialCanCast` evita di
registrare geometria curva ma diffusa (es. gambe-cilindro in legno di `cristallo.yaml`).

**Scena**: `cristallo.yaml` ripulita dai flag (ora ridondanti) + nuova **area light**
zenitale piccola come driver di caustiche; `vino_rosso.transmission_depth` 0.02→0.06 perché
il fuoco rosso arrivi al tavolo senza spegnersi. Render di verifica: 3 caster auto-rilevati
(coppa, stelo/base, vino), gambe in legno correttamente escluse, caustica rossa sul tavolo.

**Test**: `GroupCausticTests` — il test "flag sul gruppo" ora usa un figlio diffuso
(isola il comportamento del flag di gruppo dall'auto-classificazione) + due nuovi test
(`AutoClassification_RegistersSpecularCurvedEntity_WithoutFlags`,
`AutoClassification_OptOut_ExcludesCaster`). Suite caustiche tutta verde.

---

## Ciclo Caustiche — emettitori virtuali finiti (sphere/point/spot) ✅

Estensione della copertura **luci** delle caustiche. Prima solo `area` e geometriche
emissive (le uniche a implementare `ILight.TrySampleEmissivePoint`) guidavano MNEE/SMS;
`SphereLight` — pur non-delta — era saltata silenziosamente, e `point`/`spot` erano
escluse dal gate `if (light.IsDelta) continue;` in `Renderer.ComputeCaustics`. Ora
castano anche **sfera, point e spot**, con l'approccio **emettitore virtuale finito**
(riusa l'intero `ManifoldWalker`, nessuna nuova matematica nel core).

**SphereLight** — emettitore d'area esatto: `TrySampleEmissivePoint` campiona uniforme
sull'intera sfera (`MathUtils.RandomUnitVector`), `pdf_A = 1/(4πR²)`, normale uscente,
`L_e = Color·Intensity`. La costante deriva dall'equivalenza con `IlluminateAndTest`
(che converge a `Intensity·πR²/d²`): un emettitore sferico di radianza L_e ha intensità
`I = L_e·πR²`, quindi L_e = `Color·Intensity` — identico a un `GeometryLight` su `Sphere`
emissiva (verificato da un test di equivalenza al livello di campionamento).

**PointLight/SpotLight** — bulbo sferico finito di raggio `r = soft_radius` (default
`DefaultBulbRadius = 0.05` quando 0; un punto matematico ha area nulla che il walk non
può integrare). `L_e = Color·Intensity/(πr²)`, `pdf_A = 1/(4πr²)`: per un'intensità I
modellata come sfera di radianza L_e vale `I = L_e·πr²`, e la connessione banale
ricostruisce `I/d²`. Il **contributo è invariante in r** (il `1/(πr²)` cancella il
`4πr²` di `1/pdf_A`) — r regola solo nitidezza/rumore. Lo spot aggiunge
`DirectionalEmissionScale(emitDir)`: nuovo hook di `ILight` (default `Vector3.One`,
bit-identico per tutte le altre luci) che applica la falloff di cono smoothstep² alla
radianza emessa lungo `emitDir = normalize(LastVertex − y)`; segno verificato contro
`IlluminateAndTest` (`Dot(emitDir, Direction)`, senza negazione — `emitDir` è già
lungo il fascio).

**Renderer** — rimosso il gate `IsDelta`: ora a filtrare è solo `TrySampleEmissivePoint`
(false ⇒ skip a costo zero via il `break` esistente, quindi `directional`/environment/
portal restano escluse). `AccumulateCaustic` riceve `ILight light` e moltiplica `L_e`
per `DirectionalEmissionScale` prima del return. CLI `--mnee-samples N` (default 1) per
ripulire il rumore del bulbo finito, più rumoroso di una luce d'area.

**Test**: `DeltaLightCausticTests` (contratto di `TrySampleEmissivePoint` per i tre tipi;
invarianza in r; equivalenza sphere↔emissive-sphere; falloff di cono + `One` per le luci
isotrope) + `MnEeRenderTests` esteso (sphere/point/spot: `peak(on) > peak(off)`, lo spot
come guardia end-to-end sul segno del cono). Costo zero con `--caustics off` o senza
entità flaggate; le luci esistenti restano bit-identiche (hook di default `One`).

## Ciclo Caustiche 2c bis — CSG world/transform + group ✅

Chiusura dei buchi di copertura del caster prima di passare alla fase successiva
della roadmap caustiche. La scena diagnostica `scenes/showcases/glass-caustics.yaml`
(quattro primitive curve + tre CSG: un dado `intersection` in world, uno stelo
`union` e una coppa `subtraction` annidata sotto `translate` d'entità) rendeva le
caustiche solo per le primitive; **nessun** CSG castava, in nessuno spazio. Tre bug
distinti, tutti sul recupero della **chart world-space** per il manifold walk.

**Bug 1 — CSG con leaf trasformato.** `CsgObject.SeedManifold` ray-casta il solido
booleano e usa `rec.HitPrimitive` come chart. Ma un leaf con `translate`/`rotate`
proprio è un `Transform(Sphere)`, e `Transform.Hit` inoltrava `HitPrimitive` invariato
= la `Sphere` **object-space** (centro all'origine). `EvaluateManifold(u,v)` risolveva
quindi attorno all'origine, non alla posizione reale → Newton non convergeva (o
convergeva sul vertice sbagliato). Era il motivo per cui solo i leaf non trasformati
(sfera centrata, come nei test 2c originali) castavano.
**Fix**: `Transform.Hit` rimappa `rec.HitPrimitive` a **`this`** quando non-null —
`Transform` è già la `IManifoldSurface` world-space corrispondente (il suo
`EvaluateManifold` mappa la stessa `(u,v)` object→world). Il `null` delle regioni
piatte non-focalizzanti (cap di cilindro/cono) è preservato così il seeder le salta
ancora. Il percorso analitico usa `_surface` direttamente (non l'identità di
`HitPrimitive`), quindi resta invariato.

**Bug 2 — CSG sotto transform d'entità.** Un `translate:` sull'entità `type: csg`
produce `Transform(CsgObject)`, rigettato sia da `CanCastCaustics` (`Transform` →
inner né mesh né `IManifoldSurface`) sia da `Transform.CreateManifoldCaster` (→ null).
**Fix**: nuovo `TransformedManifoldCaster` (nested in `Transform`) che mappa gli
endpoint world→object con la matrice inversa, semina il caster interno in object space,
e avvolge ogni chart in un `TransformedChart` che solleva `EvaluateManifold` in world
(punto via matrice forward, normale via normal matrix) e rimappa il vertice convergente
indietro in object space per il clamp di membership `IClampedChart.Accept` (che compone
gli inside-test dei figli, definiti in object space). `CanCastCaustics` accetta
`Transform(CsgObject)` con frontiera curva.
**Transform annidati**: `CreateManifoldCaster` **appiattisce** la catena di
`Transform` (matrice composta + payload innermost) prima di scegliere il ramo, perché
un `Transform` è *sempre* `IManifoldSurface` ma sa valutare parametricamente solo se il
suo inner è una primitiva analitica — un CSG no. Senza appiattimento un CSG con
**transform proprio dentro un group** (`Transform(Transform(CsgObject))`) cadeva nel
ramo analitico e la chart non valutava (Newton falliva). `CanCastCaustics` sbuccia
anch'esso i `Transform` annidati per concordare col builder.

**Bug 3 — caster dentro `type: group`.** I flag `caustic_caster`/`caustic_receiver`
erano onorati solo nel loop d'entità top-level, mai in `BuildChildList`: un figlio
flaggato (primitiva, CSG, mesh) dentro un group non si registrava e nessuna caustica
appariva. Inoltre la geometria di seeding va registrata in **world space**, mentre un
figlio di group vive in group-local.
**Fix**: helper condiviso `SceneLoader.ApplyCausticFlags` chiamato sia al top-level
(`toWorld = Identity`) sia per ogni figlio di group, con la matrice **group→world**
accumulata e composta lungo la catena di group annidati (`childToWorld = childTransform *
groupToWorld`); registra `Transform(localHittable, toWorld)` come geometria di seeding.
Un group flaggato in toto emette warning (non è un singolo caster); instance/template
sono esclusi (geometria condivisa fra istanze → `groupToWorld = null`, warning).

**Costo zero off-path**: tutto dietro `_enableCaustics`; il solo branch sempre attivo è
il rimap `if (rec.HitPrimitive != null)` in `Transform.Hit`, che riscrive un campo già
popolato dalle primitive — nessun cambiamento osservabile fuori dal seeding caustiche.
**Test**: `CsgCausticTests` (leaf trasformato `FocusesAtWorldPosition` + CSG sotto
transform `UnderTransform`, doppio transform `UnderNestedTransforms`), `GroupCausticTests`
(group trasformato, group annidati, CSG in group — anche con transform proprio —,
group-in-toto rigettato). Suite intera: 527 verde.

---

## Ciclo Mesh Neighbor-Seed — edge-crossing tier 1 ✅

Prima tappa della **Strada 2d** (`PLANNING.md`): far castare le **mesh
triangolari**. Il limite di 2c era il clamp per-triangolo — quando il vertice di
Newton converge appena oltre il bordo del triangolo del seed, `IClampedChart.Accept`
lo scarta e la connessione è persa, così le mesh smooth fortemente curve castavano
poco o nulla.

**Approccio scelto (neighbor-seed):** retry **lato chiamante** che lascia il
solver numerico condiviso intatto — niente rischio di regressione sui caster
analitici/CSG già funzionanti. Quando il solve primario è rigettato, si ri-semina
il vertice colpevole su ogni facet **adiacente** e si ri-risolve lo *stesso* solve;
il clamp del vicino accetta solo se il vertice ci atterra davvero → stimatore
**unbiased** (una connessione recuperata è quella che il facet del seed avrebbe
dovuto produrre). Scartata la variante in-solve (hand-off del chart dentro Newton)
perché tocca il codice numerico più delicato e va certificata come non-distorta:
resta come tier 2 in roadmap, costruibile sopra questa adiacenza.

**Implementato:**
- `Mesh` → `INeighborSeedCaster`: adiacenza dei facet costruita una volta in
  `PrepareCausticAdjacency()` (vertici saldati per posizione su griglia 1e-5,
  mappa edge→triangoli, vicini per-triangolo). `FacetNeighbors(seed, span)`
  restituisce le chart adiacenti al facet del seed, riseminata al centroide.
- `CausticCasterRegistry` chiama `PrepareCausticAdjacency` sul percorso di
  registrazione **single-thread**, così il retry gira lock-free nel render parallelo.
- `ManifoldWalker`: `Connect`/`ConnectRough` rifattorizzati estraendo
  `TrySmooth`/`TryRough` (solve da seed esplicito) + `RetryNeighborSeeds` (ciclo
  sui vicini, primo successo vince). Nuova `INeighborSeedCaster` solo su `Mesh`:
  i caster che non la implementano (analitici, CSG) prendono il ramo identico
  **bit-per-bit** (516 test invariati).

**Limite (documentato):** recupera solo vertici a **una faccia** dal seed; mesh
coarse molto curve restano più rumorose/deboli degli analitici (verificato a
render: la sfera-mesh di vetro casta una caustica speckled vs ~nulla di prima).
Vertici a distanza arbitraria → tier 2 (edge-walk in-solve).

**Test:** `MeshCausticTests` — adiacenza (`FacetNeighbors` trova il facet
edge-adiacente, lone facet = 0 vicini), connessione **off-axis** attraverso la
sfera-mesh che un walk solo-clamp scarterebbe, registry che prepara l'adiacenza.
Suite completa **519 verde**.

**Nota di scoping emersa (in `PLANNING.md`):** le **luci delta** (point/spot)
non innescano caustiche — MNEE/SMS campiona un punto sull'*area* dell'emettitore,
assente in una sorgente Dirac; e il **"vetro dentro vetro"** è reso con un film
d'aria spurio perché l'IOR è sempre calcolato contro l'aria (manca l'IOR relativo
dallo stack dei media). Entrambi restano roadmap; `cristallo.yaml` tiene flag e
luce in attesa del secondo.

---

## Ciclo Caster Fase 2c — caustiche su tutta la geometria ✅

Implementazione della **Strada 2c** della roadmap caustiche (`PLANNING.md`):
estendere i caster di caustiche dalle **sole sfere** a **tutta la geometria
curva** — primitive (cilindro, cono, capsula, toro), **mesh smooth** e solidi
**CSG**. Opt-in, costo zero senza flag, percorso analitico **bit-identico** a
prima del ciclo. Costruito estendendo l'infrastruttura MNEE/SMS, non riscrivendola.

**Cardine: una chart per vertice.** Prima il walker (`Rendering/ManifoldWalker.cs`)
portava **una** `IManifoldSurface` per tutta la connessione — sufficiente per una
sfera (chart unica), impossibile per una mesh dove le due interfacce di una
rifrazione stanno su **triangoli diversi**. Il walker ora porta
`ReadOnlySpan<IManifoldSurface> surfs` (una chart per vertice), passato giù a
`Solve`/`Evaluate`/`ComputeGeometricTerm`/`ResolveWiPerturbed`. Le chart sono
reference type → non `stackalloc`-abili: vivono in due `[InlineArray]` sullo stack
(`SeedBuf`/`SurfBuf`), zero heap sull'hot path. Per le primitive analitiche tutte
le `surfs[i]` sono lo stesso oggetto → output identico → `MnEeCausticTests` /
`SmsCausticTests` invariati.

**Astrazione di seeding (`Geometry/IManifoldCaster.cs`).** Il seeding (l'unica
parte geometria-specifica) è dietro `IManifoldCaster.SeedManifold(x, y, ci,
seeds, out k)` che riempie `ManifoldSeed { Chart, Uv }`. Implementazioni:
- `Rendering/AnalyticManifoldCaster.cs` — primitive a chart unica (sfera,
  cilindro, cono, capsula, toro, anche sotto `Transform`): ray-cast del segmento
  per le crossing rifrattive, scan 8×4 per il seed riflessivo (la logica che
  prima era inline nel walker).
- `Mesh` — ray-cast sul **BVH interno**; per ogni crossing la chart è il triangolo
  colpito (recuperato da `HitRecord.HitPrimitive`), con baricentriche ricavate dal
  punto (`SmoothTriangle.Barycentric`).
- `CsgObject` — ray-cast sul risultato booleano; la chart è la **primitiva curva
  sottostante** (il `SurfaceHit.Rec` del combinatore propaga `HitPrimitive`).
- `Transform.CreateManifoldCaster()` — inner analitico → `AnalyticManifoldCaster`
  con `this` come chart; inner mesh → **bake** una mesh world-space una tantum.

**`EvaluateManifold` sulle primitive curve.** Cilindro, cono, capsula e toro
implementano `IManifoldSurface` invertendo le convenzioni UV dei rispettivi `Hit`
(verificato per round-trip in `MeshCausticTests`): solo le superfici curve
(niente cappe piatte), così la caustica nasce dalla curvatura reale.

**Modello di clamp (`IClampedChart`).** Il vincolo "vertice nel dominio" non gira
*dentro* Newton per le mesh — strozzerebbe il solve sui triangoli piccoli (il
vertice deve potersi muovere, e quasi sempre parte vicino a un bordo). Le mesh
**estrapolano** il piano del triangolo durante Newton e applicano il clamp
per-triangolo **a convergenza** via `IClampedChart.Accept` (baricentriche dentro
il triangolo del seed). Il CSG è l'analogo: `Accept` tiene il vertice solo se sta
sulla **frontiera del risultato booleano**, testato con `CsgObject.ContainsPoint`
ai due lati della normale (parità di crossing per-figlio, composta per operazione).
Le primitive curve mantengono il clamp nativo in `EvaluateManifold` (una sola
chart ampia, nessuna fragilità).

**Gate & warning (`Scene/SceneLoader.cs`).** `CanCastCaustics(hittable, out
reason)` sostituisce il vecchio `is IManifoldSurface`: accetta primitive curve
(anche via `Transform`), mesh con normali per-vertice (`Mesh.HasVertexNormals`) e
CSG con frontiera curva (`CsgHasCurvedBoundary`); rifiuta con warning specifico
piatte/flat-mesh/CSG-piatto. In lock-step con `CausticCasterRegistry.BuildSeeder`.

**Limite noto (deferito a 2d).** Per le mesh vale il **clamp per-triangolo**: una
connessione il cui vertice speculare scivola nel triangolo adiacente viene persa
(caustica non distorta, solo qualche connessione in meno — visibile su mesh
grossolane). L'edge-crossing con adiacenza e la planar-mirror NEE sono Fase 2d.

**Scene.** `cristallo.yaml` (vino/stelo/coppa CSG come caster → caustica rossa del
vino sul tavolo), `cornell-box-sphere.yaml` (variante a 3 sfere con caustiche
rifrattiva + riflessa) e lo showcase `sms-ice-caustics.yaml` (cubetto di ghiaccio
mesh frosted + tumbler CSG di vetro). Test: `MeshCausticTests` + `CsgCausticTests`
(suite 516 verde).

---

## Ciclo SMS Fase 2b — Specular Manifold Sampling ✅

Implementazione della **Strada 2b** della roadmap caustiche (`PLANNING.md`):
**Specular Manifold Sampling** (Zeltner, Georgiev & Jakob 2020; Hanika et al.
2015), le caustiche da vetro **frosted/rough** e da metallo **spazzolato** che la
MNEE liscia della Fase 2 non può produrre. Opt-in, single-pass, zero memoria
extra, costruito riusando l'infrastruttura MNEE — non un solver separato.

**Cosa risolve.** Per un caster liscio la MNEE risolve *l'unico* cammino
speculare (`ĥ = η_a·ω_a + η_b·ω_b ∥ n`). Per un caster rough il microfacet è
distribuito (GGX): non c'è un cammino unico ma un continuo. SMS campiona una
**normale di microfaccetta `m`** dalla VNDF del caster e risolve la manifold
imponendo `ĥ ∥ m`; il caso liscio è l'offset nullo (`m = n`). Una sfera di vetro
smerigliato marcata `caustic_caster` proietta così un alone morbido (non lo spot
netto della lente liscia) sul `caustic_receiver`, e un metallo `fuzz > 0` una
caustica riflessiva sfocata.

**Generalizzazione del residuo (`Rendering/ManifoldWalker.cs`).** Il residuo di
Newton è esteso con un offset di microfaccetta per vertice (span locale, vuoto =
liscio): `m` è ricostruito dal frame tangente di Frisvad
(`Microfacet.BuildTangentFrame`), e `F = (Dot(s_m, ĥ), Dot(t_m, ĥ))` con `(s_m,
t_m)` ONB attorno a `m`. L'offset è **costante durante la Newton iteration**,
quindi il Jacobiano per differenze finite in `(u,v)` resta invariato. Nuovo entry
`ConnectRough` (seeding condiviso con `Connect`, poi un campione VNDF per vertice
visto dalla direzione incidente lato-receiver → `Solve` con offset fisso). Tutto
`stackalloc`/`Span`, zero heap nell'hot path; i draw VNDF passano per
`MathUtils.RandomFloat()` → stesso stream Sobol/PRNG di NEE (deterministico sotto
`Sampler.BeginPixelSample`).

**Stimatore biased (Zeltner §4.1).** Per prova: campiona `m` dalla VNDF, risolve a
offset fisso, pesa con `f_rough(m)/p_vndf(m) · T · G / pdf_A`. La cancellazione
VNDF collassa `f/p` a `Fresnel(m) · G1(L)` — **identica a
`DisneyBsdf.ScatterTransmission`** (`D` e `G1(V)` cancellati dal sampling), quindi
il throughput rough riusa la stessa derivazione. `G = dΩ_x/dA_y` è il termine
geometrico MNEE ri-calcolato perturbando la luce **con lo stesso offset fisso**.
Il `Renderer` (`ComputeCaustics`) media `--sms-samples` prove per connessione
rough (`invSamples / _smsSamples`); helper `AccumulateCaustic` condiviso fra il
ramo liscio e quello rough, con gli early-out a buon mercato (orientazione,
`EvaluateDirect == 0`, throughput morto) **prima** dei 2 ray di occlusione.
Il bias è controllato e →0 con `roughness → 0`; lo stimatore **unbiased** a
probabilità reciproca (§4.2) è deferito alla Fase 2c (hook lasciato nel walker) —
control-flow diverso, code lunghe a rischio budget per-pixel.

**Riflessione rough.** Il ramo riflettente della MNEE (`SeedReflection`,
`IsTransmissive = false`), prima presente ma inutilizzato (nessun materiale
emetteva un caster riflettente), è ora alimentato da `Metal.GetCausticInterface`
(`fuzz = 0` → mirror liscio, `fuzz > 0` → SMS riflettente, `α = fuzz²`) e dalla
lobe metallica di `DisneyBsdf` (`metallic ≈ 1`). Il Fresnel riflettente è passato
a **Schlick-conduttore** con `F0 = tint` (riflettanza del metallo), corretto per
i conduttori dove il vecchio Fresnel dielettrico era sbagliato.

**Opt-in & CLI.** `CausticInterface` esteso con `IsRough`/`AlphaX`/`AlphaY`/
`Roughness` (secondo costruttore; quello liscio resta source-compatibile).
`DisneyBsdf.GetCausticInterface` non scarta più `roughness > 0.04`: ritorna un
caster rough rifrattivo (o riflettente metallico) con l'`α` anisotropo mappato
come `ShadingParams`. Nuovo flag `--sms-samples <n>` (default 4); i preset
quality **final/ultra** attivano `--caustics on` di default (innocuo senza entità
marcate — registro vuoto → costo zero) con `sms-samples 8`; un `--caustics`
esplicito vince sul preset.

**Impatto performance.** Costo zero con `--caustics off` o senza entità marcate
(gate `_causticsActive` invariato). Percorso liscio bit-identico (un solo branch
inlinato `rough` mai preso; `MnEeCausticTests` resta verde). Sui pixel
`caustic_receiver` che vedono un caster rough: ~`N×` una connessione MNEE liscia,
con `N = --sms-samples` (ogni prova ≈ 1 solve + 1 Jacobiano + 2 ray di
occlusione). Whole-frame dipende dalla copertura a schermo dei riceventi rough.

**Marcatura per-entità (confermata).** Resta a livello di entità
(`caustic_caster`/`caustic_receiver`) + capacità per-materiale
(`GetCausticInterface` → `None` se non idoneo): il materiale dice «posso?»,
l'entità «devo, qui?». Identico al design "Shadow Caustics" per-oggetto di Cycles;
il costo SMS scala con le coppie caster×receiver, quindi l'opt-in per entità
mantiene lo scope stretto. Sempre-on di default sarebbe un disastro di performance
e non è ciò che fa nessun renderer di produzione per la MNEE.

**Limiti Fase 2b / follow-up.** Stimatore unbiased (2c); anisotropia del caster
in fallback isotropo (l'`α` è anisotropo ma il frame è geometrico); caster
ancora solo sferici (eredita il limite `IManifoldSurface`); catene ≤ 2
interfacce; luci d'area (non delta/ambiente). Dove il Newton non converge la
caustica di quella prova è persa (innocuo, biased).

---

## Ciclo Caustiche Fase 2 — MNEE ✅

Implementazione della **Strada 2** della roadmap caustiche (`PLANNING.md`):
**Manifold Next Event Estimation**, le caustiche focalizzate di lente/sfera di
vetro che gli shadow ray dritti della Fase 1 non possono produrre. Opt-in,
unbiased, single-pass, zero memoria extra. Riferimenti: Jakob & Marschner 2012
(Manifold Exploration), Hanika/Droske/Manzi 2015 (MNEE); analogo allo "Shadow
Caustics" di Cycles 3.2.

**Cosa risolve.** Una sfera/lente di vetro solido marcata `caustic_caster`
concentra la luce di un'area light in uno spot luminoso su una superficie
`caustic_receiver`, con l'ombra circostante correttamente più scura (energia
concentrata, non sparsa). Verificato a vista (sfera di vetro su pavimento) e in
regressione.

**Solver (`Rendering/ManifoldWalker.cs`).** Per un ricevente `x` e un punto
luce `y`: seeding dal segmento dritto `x→y` (1 crossing = singola interfaccia,
2 = vetro solido entrata+uscita), poi Newton-Raphson sulle incognite `(u,v)`
di ogni vertice. Residuo = componente tangenziale dell'half-vector generalizzato
`ĥ = η_a·ω_a + η_b·ω_b` (zero ⇔ `ĥ ∥ n` ⇔ Snell/riflessione). Lo **stesso
residuo unifica riflessione** (η = 1 ai due lati) **e rifrazione 1/2 interfacce**
— il rapporto η per lato è scelto dalla geometria (`dot(endpoint − p, n) > 0 ⇒
aria, altrimenti vetro`). Jacobiano per differenze finite in `(u,v)` (solo
aritmetica, niente ray cast nel loop interno → veloce), line-search smorzata,
risolutore lineare ≤4×4 con pivoting. Termine geometrico `G = dΩ_x/dA_y`
calcolato perturbando `y` sul piano della luce e ri-risolvendo (ray differentials
a differenze finite). Stima: `L = f_r(x,ω_x)·L_e·T·G/pdf_A`, con `T` = ∏ Fresnel
trasmesso × Beer-Lambert interno.

**Geometria.** `IManifoldSurface.EvaluateManifold(u,v)` (inverso di
`ray → (u,v)`) su `Sphere` (forma chiusa dall'inverso di `GetSphereUV`),
propagato da `Transform` (matrice forward per il punto, normal matrix per la
normale). Nuovi campi `HitRecord.DnDu/DnDv` (derivate della normale, per future
primitive/Jacobiani analitici): per la sfera `∂N/∂· = ∂P/∂· · (1/R)`; `Transform`
li propaga con la normal matrix + ri-proiezione sul piano tangente.

**Conteggio singolo (unbiased).** La luce trasmessa è stimata **una sola volta**,
da MNEE, partizionando le strategie (niente MIS complicato):
1. lo shadow ray dritto (Fase 1) è reso **opaco** ai `caustic_caster` per i punti
   `caustic_receiver` — flag thread-local `ShadowRay.BlockCausticCasters`, alzato
   solo attorno al loop NEE di un receiver;
2. il cammino **forward** `receiver diffuso → caster(≤2) → luce` è **soppresso**
   via uno stato "caustic carrier" (`int causticChain`) propagato in
   `TraceRay`/`ShadeSurface`/`ShadeSampleBounce`: conta le interfacce speculari
   `caustic_caster` attraversate dall'ultimo rimbalzo diffuso su un
   `caustic_receiver`; l'emissione è soppressa quando il contatore è in `[1, 2]`.

**Opt-in & CLI.** Flag YAML `caustic_caster`/`caustic_receiver` su `EntityData`
(+ `world.ground`), wrappati in `CausticFlagHittable` **solo** quando
`--caustics on` (così l'off-path non ha nemmeno la virtual call). I caster
manifold-evaluabili sono raccolti in `CausticCasterRegistry` (con cull per AABB
sul segmento). Buffer `stackalloc`/`Span` nel walker → zero allocazioni
nell'hot path. Costo zero garantito senza flag o con `--caustics off`.

**Impatto performance.** Solo i punti `caustic_receiver` e i caster rilevanti
pagano: per shadow sample attivo ~3–8 iterazioni Newton (solo aritmetica) +
2 ray di visibilità + 2 ri-solve per `G`. Sulla scena di test (sfera + area
light, 400×300/64spp) il render passa da ~5s (off) a ~10s (on); su scene
showcase con pochi pixel/luci interessati l'overhead totale è minore.

**Test.** `MnEeCausticTests` (analitici closed-form, stile `BvhEquivalenceTests`):
sfera di vetro on-axis → vertici esatti a ±R, `wi = +Z`, throughput Fresnel
`(1−0.04)² = 0.9216`; caso off-axis → residuo di Snell ~0 ai vertici; nessun
caster sul segmento → skip senza bias. `MnEeRenderTests` (regressione end-to-end):
la caustica emerge con `--caustics on` e non con off. Suite completa verde
(499 test).

**Limiti Fase 2 / follow-up.** Caster valutabili parametricamente = solo sfera
(+ trasformata); cilindro/cono/toro/mesh richiedono `EvaluateManifold` +
`DnDu/DnDv` dedicati. Catene ≤ 2 interfacce; luci d'area/geometriche (non delta
né ambiente). Vetro frosted (`roughness > 0`) → Strada 2b (Specular Manifold
Sampling). Dove il Newton non converge la luce trasmessa è persa (no fallback
MIS) — innocuo sui caster convessi, da raffinare.

---

## Ciclo Preset & Assets ✅

Riarchitettura della gestione dei materiali/luci/mediums condivisi: **cataloghi di
preset copia-incolla** sotto `scenes/presets/` (`materials-*.md`, `lights.md`,
`mediums.md`, `terrains.md`, `world.md`, `sky.md` + `README.md` indice). Ogni
catalogo segue un'anatomia fissa — schema di riferimento, blocchi
`materials:`/`lights:`/`mediums:` pronti da incollare con razionale, matrice
decisionale, CLI tips — con curazione "pochi e corretti". Le risorse binarie
(texture, font, heightmap) vivono sotto `scenes/assets/{textures,fonts,heightmaps}/`;
i tool `TextureGen`, `NormalMapGen`, `FontGen`, `TerrainGen` scrivono lì.

**Fix di correttezza marmi (motore).** `MaterialData.SpecTrans` è ora `float?`
(nullable). L'auto-promozione SSS in `SceneLoader.ApplyEmbeddedSssDefaults`
(`spec_trans → 1.0` quando un materiale ha `subsurface_radius`) scatta solo se il
valore non è autorato: uno `spec_trans: 0` esplicito è rispettato. Prima un marmo
lucido opaco con `subsurface_radius` veniva reso silenziosamente trasmissivo
(vetroso); ora i preset di marmo opaco usano `clearcoat` + `spec_trans: 0`
esplicito, mentre onice/alabastro autorano `spec_trans`+`transmission_*`.

**Migrazione.** Tutte le scene e gli showcase sono self-contained (materiali/luci/
mediums inline-ati, path binari ripuntati a `assets/`); nessuno showcase rimosso.
Skill `create-preset` per generare/estendere i cataloghi.

**Test.** `EmbeddedSssSpecTransTests` (nuovo) — `spec_trans: 0` esplicito non
promosso, assenza promossa a 1.0, valore frazionario preservato. Suite verde.

**File principali.** `Scene/SceneData.cs`, `Scene/SceneLoader.cs`,
`src/Tools/{TextureGen,NormalMapGen,FontGen,TerrainGen}`, `scenes/presets/*`,
`scenes/assets/*`, doc EN+IT.

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
