# Piano di review del motore 3D-Ray

Verifica completa di matematica, ottica e fisica del motore, modulo per modulo, dalle fondamenta ai
sottosistemi complessi. Obiettivo: capire cosa è corretto, cosa è un'approssimazione voluta, cosa è
sbagliato e cosa conviene rifare da zero per arrivare a un motore di livello "premium".

Documento interno (come `PLANNING.md` e `devlog/`): può citare renderer esterni.

> Stato del codice al momento della stesura (2026-10-20): circa 50.000 righe in `src/RayTracer`,
> build Release pulita, **590 test xUnit tutti verdi**. Warning NuGet: `SixLabors.ImageSharp 3.1.12`
> ha vulnerabilità note (moderate e high) e va aggiornato.

---

## Stato di avanzamento

Documento vivo: resta nel repo finché tutte le fasi non sono chiuse. A ogni sessione di lavoro si
aggiornano questa tabella, le caselle della fase e la sezione "Esito" della fase con i risultati
(etichette e verdetto); il dettaglio va nel devlog.

| Fase | Contenuto | Stato |
|---|---|---|
| 0 | Infrastruttura di verifica | ✅ Chiusa (2026-10-20), esito in §4 Fase 0 |
| 1 | Fondamenta matematiche, sampler | ✅ Chiusa (2026-10-20), esito in §4 Fase 1 |
| 2 | Geometria e intersezioni | ⬜ |
| 3 | Camera, film e colore | ✅ Chiusa (2026-10-21), esito in §4 Fase 3 |
| 4 | Texture e dati | ⬜ |
| 5 | Materiali e BSDF | ⬜ |
| 6 | Luci, cielo | ⬜ |
| 7 | Integratore | ⬜ |
| 8 | Volumi e SSS | ⬜ |
| 9 | Post-processing e denoiser | ⬜ |
| 10 | Formato scene v2 | ⬜ |
| 11 | Architettura e performance | ⬜ |

Correzioni anticipate fuori ordine: 3.19, 3.20, 3.21 (più 3.24 e 3.25 emersi nel farle) ✅ 2026-10-20.

---

## 1. Riferimenti: cosa vuol dire "premium"

Il metro di paragone sono i motori di produzione e i due renderer di ricerca usati come verità
numerica:

| Area | Riferimento di settore |
|---|---|
| Materiale unico | **OpenPBR Surface** (ASWF, 2024: erede di Autodesk Standard Surface e Adobe Standard Material), Arnold `standard_surface`, RenderMan `PxrSurface`, MaterialX |
| Integratore | path tracer iterativo con MIS, many-light sampling (light BVH), path guiding (RenderMan, Cycles, Karma), caustiche via MNEE o guiding |
| Colore | pipeline color-managed (OCIO): working space lineare (Rec.709 o ACEScg), view transform AgX / ACES / "Standard", sRGB OETF corretta |
| Camera | camera fisica: focale in mm, sensore, f-stop, otturatore, ISO/esposizione, bokeh |
| Luci | unità fisiche (W, lm, cd, nit, lux), temperatura colore, profili IES, normalizzazione |
| Cielo | Hosek-Wilkie o Prague (Wilkie 2021), oppure atmosfera precomputata (Bruneton) |
| Volumi | null-scattering (delta/ratio tracking) con MIS spettrale, VDB |
| Denoiser | Intel Open Image Denoise (OIDN, Apache-2.0) |
| Scene | OpenUSD come formato di interscambio, glTF 2.0 per gli asset |
| Verità numerica | **Mitsuba 3** e **PBRT-v4**: stessa scena, confronto pixel per pixel |

---

## 2. Metodologia (vale per ogni modulo)

### 2.1 Classificazione dei risultati

Ogni problema trovato riceve un'etichetta:

- **[BUG]** matematica o fisica sbagliata: va corretta.
- **[BIAS]** approssimazione voluta che sposta il valore atteso (clamp, hack di energia): va resa
  opzionale e spenta nella modalità di riferimento.
- **[NON-STANDARD]** funziona ma diverge da come lo fa il settore (unità, parametri, nomi).
- **[DEBITO]** codice duplicato, API doppie, commenti obsoleti, file troppo grandi.

E ogni modulo riceve un verdetto finale: **tenere**, **correggere**, **estendere** o **rifare**.

### 2.2 Strumenti di verifica (da costruire nella Fase 0)

1. **Test statistici sulle BSDF e sulle luci** (stile Mitsuba): per ogni `Sample`/`Pdf`
   - integrazione numerica: `∫ pdf dω = 1` (o = probabilità del lobo non-delta);
   - **chi-quadro**: istogramma delle direzioni campionate contro la `Pdf` integrata per cella;
   - coerenza: `peso restituito da Sample == Evaluate*cos / Pdf`;
   - reciprocità: `f(V,L) == f(L,V)` dove la fisica lo richiede;
   - **white furnace**: albedo direzionale `≤ 1` (e `= 1` per i materiali senza assorbimento
     con compensazione multi-scattering).
2. **Scene analitiche** con soluzione esatta: sfera Lambert in ambiente uniforme (radianza =
   albedo x L), piano sotto luce puntiforme (`I cosθ / d²`), irradianza di un rettangolo
   (form factor chiuso), sfera emissiva vista da lontano, Cornell box (dati misurati Cornell).
3. **Confronto con Mitsuba 3 / PBRT-v4**: ~15 scene canoniche tradotte nei loro formati
   (convertitore YAML → Mitsuba XML per il sottoinsieme comune), render a convergenza in EXR,
   metriche **relMSE** e **FLIP**, immagini in falsi colori della differenza.
4. **Test di non-polarizzazione**: la media di N render indipendenti a pochi spp deve convergere
   al riferimento con errore che scende come `1/√N`. Se si ferma a un plateau, c'è un bias.
5. **Modalità di riferimento** (`--reference`): spegne tutto ciò che è [BIAS] (vedi 3.8) così che
   il motore "puro" sia confrontabile con Mitsuba.

### 2.3 Deliverable per modulo

- sezione nel devlog con risultati ed etichette;
- test nuovi in `RayTracer.Tests` che bloccano la correttezza raggiunta;
- render di confronto in `validation/out/` (rigenerabili con `validation/run.py`, non versionati);
- aggiornamento di `PLANNING.md` (bug noti, TODO).

---

## 3. Problemi già trovati durante la stesura del piano

Nati dal campionamento a mano del codice durante la stesura, poi **misurati** con gli strumenti
della Fase 0 (righe 3.17 e seguenti: emersi direttamente dalle misure). Ogni punto si chiude nella
fase indicata.

| # | Etichetta | Dove | Problema |
|---|---|---|---|
| 3.1 | **[BUG]** | `Rendering/Sky/PreethamSky.cs:197` | Funzione di Perez sbagliata. Corretta: `(1 + A*e^(B/cosθ)) (1 + C*e^(Dγ) + E*cos²γ)`. Nel codice: `(C + D*e^(Eγ) + E*cos²γ)`, cioè coefficienti scalati di una posizione ed `E` usato due volte. Anche la normalizzazione `F(0, θs)` eredita l'errore. Il sole usa una costante empirica (`SolarConstantRel = 20`) e trasmittanze ad hoc. |
| 3.2 | **[BUG]** confermato | `Materials/DisneyBsdf.cs:1302, 1335, 1384, 1440, 1604, 1674, 1706` | Dopo aver scelto un lobo con probabilità `p`, il peso viene diviso per `max(p, 0.1)` (o `0.05`), ma `p` non è mai forzato a quel minimo in fase di scelta. **Misurato (Fase 0)**: il renderer usa `Sample()`, che per i lobi non-delta ricalcola `Evaluate`/`Pdf` e quindi non è toccato; il bug colpisce i lobi **delta** (speculare liscio, clearcoat liscio, trasmissione). Plastica liscia: riflesso speculare sottostimato del 9.4% a ogni angolo (p = 0.0906). Test `KnownBug_Disney_SmoothSpecularMatchesFresnel`. Fase 5. |
| 3.3 | **[BUG]** | `Scene/SceneLoader.cs:1802, 1902` + `Textures/ImageTexture.cs:63` | Le mappe dati (roughness, metallic, coat roughness...) passano per `ImageTexture`, che applica sempre la decodifica sRGB. Una roughness map viene quindi "linearizzata" come fosse un colore: valori sbagliati. Manca un attributo `color_space` per texture. |
| 3.4 | ✅ **corretto** (2026-10-21, Fase 3), era **[NON-STANDARD]** | `Textures/ImageTexture.cs` | Decodifica sRGB come `pow(x, 2.2)` invece della curva sRGB a tratti (IEC 61966-2-1): il byte 10 diventava 8.0e-4 invece di 3.0e-3, ombre delle texture troppo scure. Ora `Srgb.Decode` (stessa curva dell'uscita). |
| 3.5 | ✅ **corretto** (2026-10-21, Fase 3), era **[NON-STANDARD]** | `Rendering/ColorManagement.cs` | Display transform = fit di Narkowicz dell'ACES applicato **per canale** su Rec.709 + gamma 2.2 pura: hue shift e saturazione "Notorious 6" sulle alte luci, nessuna gestione del colore. Ora `--view agx` (default), `aces` (ACES 1.x RRT + ODT vero, entro 7e-4 da OpenColorIO), `standard` (curva sRGB a tratti); la curva vecchia resta come `legacy`. Bilanciamento del bianco, dithering, PNG 16 bit. |
| 3.6 | **[NON-STANDARD]** ridimensionato | `Materials/DisneyBsdf.cs:642-648, 773-778` | La trasmissione **rough** (vetro satinato) è esclusa da `Evaluate`/`Pdf`. **Misurato (Fase 0)**: `Sample()` la marca come delta, quindi non c'è bias ma solo varianza (niente NEE attraverso il vetro satinato). Il vero problema del vetro Disney è il guadagno di energia (3.17). Serve comunque il modello di Walter et al. 2007 completo (BTDF + pdf). Fase 5. |
| 3.7 | **[BUG]** confermato, grave | `Materials/Metal.cs:86-91` vs `135-162` | Il lobo lambertiano "di riempimento" `albedo*fuzz/π` esiste **solo** in `EvaluateDirect` (la NEE), non in `Evaluate`/`Sample`: luce diretta e luce campionata dalla BSDF stimano due materiali diversi (scarto fino al 96%) e il MIS li mescola. **Misurato**: sfere di metallo bianco rough +37% di luminanza rispetto a Mitsuba, aspetto da materiale diffuso. Il `MixMaterial` lo eredita. Inoltre il GGX singolo-scattering perde molta energia ad alta rugosità (fuzz 1, bianco: albedo 0.32-0.51) e il Fresnel è Schlick con `F0 = colore`: niente Kulla-Conty, IOR complesso o F82. Test `KnownBug_Metal_DirectLightingMatchesSampling`. Fase 5. |
| 3.8 | **[BIAS]** di default, misurato | `Rendering/Renderer.cs:208, 221, 376-378` + materiali | Attivi di default: clamp per-sample 10, clamp indiretto 0.25 (= 2.5), ombre trasparenti "dritte" del vetro, Russian Roulette con soglie da una classificazione euristica della scena, `DeadPathThroughputEpsilon`. Tutti spenti da `--reference` (Fase 0). Il peso misurato dei primi due è in 3.20 e 3.21. Fase 7. |
| 3.9 | ✅ **corretto** (2026-10-20, Fase 1), era **[BIAS]** | `Textures/EnvironmentMap.cs` (`SampleDirection`) | L'importance sampling dell'HDRI restituiva sempre il **centro** del texel scelto: le direzioni NEE erano un insieme discreto (ombre a gradini e bias con HDRI piccole o sole concentrato). Ora la posizione dentro il texel è continua (lo scarto di ciascun numero casuale dentro il proprio intervallo della CDF) e la pdf usa il θ della direzione effettiva, come `PdfDirection`. Chi-quadro su una mappa 16x8 con un texel brillante: prima 1.3e6 su 799 gradi di libertà, ora passa. Test `EnvironmentMap_SamplesItsPdf_ContinuouslyInsideTexels`. |
| 3.10 | ✅ **corretto** (2026-10-20, Fase 1), era **[NON-STANDARD]**, misurato peggio del caso | `Core/Sampling/OwenSobol.cs` + `Sampler.cs` | Solo le dimensioni 0-1 erano vero Sobol; dalla 2 in su van der Corput con hash, e due dimensioni consecutive (la coppia di una decisione 2D) **non** erano stratificate insieme: pendenza dell'errore da -0.32 a -0.56 contro -0.50 dei numeri indipendenti, cioè peggio del PRNG. Le dimensioni erano assegnate con un contatore globale per campione, quindi path con rami diversi le disallineavano. Ora: Sobol "padded" (PBRT-v4), ogni coppia di dimensioni è la coppia 0-1 di Sobol con un rimescolamento Owen dell'indice proprio della coppia; blocco fisso per la camera (8 dimensioni) e per ogni vertice del path (64, `Sampler.BeginVertex`); `MathUtils.Random2D` / `Sampler.Sample2D` per ogni decisione 2D. Pendenze ora -1.38 (liscio) e -0.75 (bordo) su ogni coppia. Test `SamplerQualityTests`. |
| 3.11 | **[NON-STANDARD]** | `Rendering/Renderer.cs:1897` + `Core/MathUtils.cs:44` | Il fattore "shadow terminator" è applicato anche alla direzione di vista, oltre a quella della luce: non corrisponde a nessun modello pubblicato (Chiang 2019 / Estevez 2019 agiscono sul lato luce). Va verificato con il furnace test. |
| 3.12 | **[DEBITO]** | `Materials/IMaterial.cs` | Due API di materiale in parallelo: `Scatter`/`EvaluateDirect` (storica) e `Evaluate`/`Pdf`/`Sample` (MIS). Doppio codice = doppio rischio di incoerenza. Il commento a `Renderer.cs:1876` dice ancora che Lambert/Metal/Dielectric non implementano `Pdf`. |
| 3.13 | ✅ **verificato, nessun bug** | `DisneyBsdf` + `Renderer` | `Sample()` restituisce per i lobi non-delta `Evaluate`/`Pdf` della miscela, quindi peso e MIS sono coerenti: il chi-quadro e il cross-check dell'albedo passano su tutti i lobi rough (Fase 0). |
| 3.14 | **[NON-STANDARD]** | tutte le luci + `Emissive` | `intensity` senza unità: impossibile illuminare "come nella realtà" (sole 100.000 lux, lampadina 800 lm) e scambiare scene con altri motori. Nessuna temperatura colore, nessun IES. |
| 3.15 | **[DEBITO]** | `scenes/cornell-box.yaml` e altre | Commenti obsoleti: dicono che l'emissivo "non usa NEE", mentre oggi entra nel pool NEE come `GeometryLight`. |
| 3.16 | **[DEBITO]** | `Rendering/Renderer.cs` (2314 righe), `Scene/SceneLoader.cs` (4368), `Materials/DisneyBsdf.cs` (2006) | `TraceRay` è ricorsivo, con molti casi speciali: ostacola path guiding, light path expressions, BDPT e il debug. |
| 3.17 | **[BUG]** | `Materials/DisneyBsdf.cs` (sheen, clearcoat, trasmissione) | **Guadagno di energia** nel white furnace (albedo > 1 con materiale bianco): sheen fino a 1.38, clearcoat liscio 1.39 a incidenza radente, vetro liscio 1.015-1.13, vetro rough 1.03-1.12; diffuso Disney r=1 1.026 a incidenza radente (retro-riflessione di Burley, [NON-STANDARD]). Render: sfera di vetro Disney in furnace +3.1% (deve valere 1), sfera sheen +34% e clearcoat +5% rispetto al principled di Mitsuba. Gli strati vanno sommati con albedo-scaling che conserva l'energia (OpenPBR). Test `KnownBug_Disney_WhiteFurnaceAlbedoDoesNotExceedOne`. Fase 5. |
| 3.18 | **[BUG]** piccolo | `Materials/DisneyBsdf.cs:783-870` | La `Pdf` riporta la densità VNDF anche sotto l'orizzonte (convenzione legittima, il campione viene scartato), ma lì il lobo `diff_trans` ha supporto: per le direzioni posteriori la pdf dichiarata supera quella reale e il peso sottostima l'energia di circa l'1%. Test `KnownBug_Disney_DiffTransSampleWeightIntegratesTheBsdf`. Fase 5. |
| 3.19 | ✅ **corretto** (2026-10-20), era **[BUG]** grave | `Geometry/ShadowRay.cs` (`TransmittanceToPoint`), `Lights/AreaLight.cs`, `SphereLight.cs`, `GeometryLight.cs`, `Geometry/Sphere.cs` | Il raggio d'ombra partiva da `OffsetOrigin` (punto + 1e-4 lungo la normale) ma con la direzione calcolata dal punto non spostato: colpiva il **proxy emissivo della luce stessa**, e oltre ~52° dalla normale di una luce d'area non arrivava luce diretta (-12% nella scena `lights`, ellisse netta sul pavimento; taglio anche su emissivi e luci sferiche). Corretto con il "spawn ray to" di PBRT: entrambi gli estremi spostati lungo la propria normale, il raggio corre esattamente fra i due. Due cause di precisione emerse nel farlo, corrette insieme: campionamento conico delle sfere (luce sferica ed emissivi sferici) rifatto con la costruzione geometrica di PBRT-v4 e la forma per angoli piccoli (prima l'1-2% dei campioni mancava le sfere piccole o lontane, e sotto 1e-4 sr la pdf valeva 0 e spegneva la NEE delle sfere emissive); `Sphere.Hit` con discriminante robusto `r² - |l|²` e radici stabili (errore su t fino a 1.8e-2 a 200 unità, ora pochi ulp). Misura: `lights` da 0.975 a **1.0000**. Test `LightSelfShadowTests`, `SphereIntersectionPrecisionTests`. |
| 3.20 | ✅ **corretto** (2026-10-20), era **[BIAS]** di default grave | `Rendering/Renderer.cs` (`SuggestedMaxSampleRadiance`), `Program.cs` | Il clamp per-sample era una soglia **assoluta** di radianza (10) attiva di default: Cornell box con pannello di radianza 20 a **-22%** di energia. Ora è opzionale (`-C <n>`), e il clamp indiretto agisce solo insieme a `-C`. Misura: Cornell in modalità normale da 0.782 a **1.0000**. Test `FireflyClamp_IsOptIn_AndReferenceModeOverridesIt`. |
| 3.21 | ✅ **corretto** (2026-10-20), era **[BIAS]** di default grave | `Geometry/ShadowRay.cs`, `Rendering/Renderer.cs` (stato di catena, `SuppressChainEmission`, `LightSampleOwnsTransport`), `IMaterial.TransmitsStraight`, `Rendering/RandomWalkSss.cs` | Le ombre trasparenti "dritte" si sommavano ai cammini BSDF che attraversano davvero il vetro (doppio conteggio): l'ombra della sfera di vetro spariva. Ora per default i raggi d'ombra sono opachi al vetro che rifrange (ombra e caustica fisiche) e attraversano solo la trasmissione **rettilinea** (gusci IOR 1, fogli `thin_walled` lisci), che è esatta. `--transparent-shadows on` riporta l'approssimazione come opzione artistica coerente: i cammini "diffuso → attraverso il vetro → luce" (e cielo campionato dalla NEE) vengono scartati e il campione di luce che ha attraversato il vetro prende peso MIS 1, così ogni trasporto ha un solo stimatore. Il random walk SSS vede il bordo della propria entità come interfaccia nulla anche per la sua NEE (prima: Fresnel solo sulla NEE e non sull'uscita, ora coerenti). Misura: `glass-shadow` in modalità normale da 1.067 (relMSE 0.23) a **1.0001** (relMSE 4e-4); nuova scena `null-shell` **1.0001**. Con `--transparent-shadows on`: +8.6%, l'approssimazione toglie ombra e caustica per costruzione. |
| 3.22 | **[NON-STANDARD]** | `Scene/SceneLoader.cs:1032` | Un materiale senza `color` riceve grigio 0.5 per **ogni** tipo, anche `dielectric` ed `emissive`: un vetro scritto senza colore trasmette il 25% attraverso una sfera, un emissivo emette metà dell'intensità scritta. Default fisici: vetro 1, emissivo 1. Fase 10. |
| 3.23 | **[NON-STANDARD]** | `Scene/SceneLoader.cs:131-132` | Il loader usa `IgnoreUnmatchedProperties`: una chiave YAML sbagliata (`refraction_idx`, `roughnes`) viene ignorata in silenzio e il materiale usa il default. Serve un errore o almeno un warning con il nome della chiave. Fase 10. |
| 3.24 | ✅ **corretto** (2026-10-20), era **[BUG]** | `Materials/Dielectric.cs` | `Dielectric.Scatter` sceglieva riflessione/rifrazione con l'approssimazione di Schlick, mentre la sua `ShadowTransmittance` usava il Fresnel esatto. Schlick dà `(1-cos)^5` anche con IOR 1: un guscio a indice adattato mostrava bordo e riflesso inesistenti, e il vetro sbagliava di qualche percento a incidenza radente. Ora Fresnel esatto: `glass-shadow` FLIP da 0.032 a 0.016. |
| 3.25 | ✅ **corretto** (2026-10-20), era **[BUG]** piccolo | `Lights/GeometryLight.cs` | `IlluminateAndTestStratified` non passava il tempo dell'otturatore ai campionatori: con il motion blur i raggi d'ombra degli emissivi testavano la scena all'istante 0. |
| 3.26 | ✅ **corretto** (2026-10-20, Fase 1), era **[BUG]** | `Core/MathUtils.cs` (`OrthonormalBasis`, `LocalToWorld`), copie private in `Sphere`, `SphereLight`, `PhysicalSun`, `DirectionalLight`, `Metal`, `Microfacet` | Le basi ortonormali usavano la forma di Frisvad, che perde l'ortogonalità vicino a -Z (errore 4.5e-2). Ora una sola implementazione, Duff et al. 2017 (errore 2.9e-7 ovunque). Test `OrthonormalBasis_IsOrthonormalEverywhere`. |
| 3.27 | ✅ **corretto** (2026-10-20, Fase 1), era **[BUG]** | `Core/MathUtils.cs` (`OffsetOrigin`, `RayEpsilon`), `Renderer`, `ShadowRay`, luci, `CausticPhotonTracer` | Offset dei raggi e `tMin` fissi a 1e-4: una scena di 10 µm usciva nera, una scena a 10 km dall'origine perdeva il 44% della luce per auto-intersezione. Ora l'offset è relativo alla coordinata più grande del punto (2^-15, circa 256 ulp, Wächter e Binder), con un minimo proporzionale al raggio della scena (2^-19); anche le guardie di distanza delle luci. Misure in §4 Fase 1. Test `ScaleInvarianceTests`. |
| 3.28 | ✅ **corretto** (2026-10-20, Fase 1), era **[BUG]** piccolo | `Core/AABB.cs` | Un raggio parallelo a un asse con l'origine esattamente sul piano di una faccia dà `0 * ∞ = NaN` nello slab test, e `MathF.Max` lo propagava: il box (e in un nodo BVH tutto il sottoalbero) veniva scartato. Ora `float.MaxNumber`/`MinNumber` ignorano il NaN (convenzione conservativa di PBRT), senza costo misurabile. Test `Hit_RayInAFacePlane_IsConservative`. |
| 3.29 | ✅ **corretto** (2026-10-20, Fase 1), era **[BUG]** | `Core/QuarticSolver.cs` (`SolveCubicMonic`, `RefineRoot`) | La cubica classificava il discriminante con una soglia **assoluta** 1e-12: la risolvente di Ferrari per un raggio che sfiora il tubo del toro (tre radici distinte vicine a 1e-3) veniva presa per una radice doppia e due radici vicine della quartica tornavano fuse (coppia distante 1e-2 restituita distante 5e-4; sotto 1e-4 una radice persa nell'1-3% dei casi). Il raffinamento di Newton, partendo fra le due radici, saltava fino a 2e-2. Ora soglia relativa al cubo dei coefficienti e Newton con guardia sul residuo: coppie fino a 1e-5 sempre trovate, errore entro il condizionamento; radici distinte da 4e-8 a 1.5e-10. Vale anche per il lathe (`SolveCubic`). Test `Quartic_CloseRoots_AreBothFound`. |
| 3.30 | **[NON-STANDARD]** | `Core/Perlin.cs:109-111` | Interpolazione con il fade cubico `3t² - 2t³`: il valore e il gradiente sono continui ma la curvatura salta di 12.7 attraverso le facce del reticolo (visibile come pieghe sulla griglia nei bump map di rumore, che derivano due volte). Gradienti normalizzati sul cubo: anisotropia del 5.7% fra assi e diagonali. Improved Perlin (fade quintico, 12 gradienti) cambia l'aspetto di ogni texture procedurale: Fase 4, con un'opzione per l'aspetto attuale. Anche `MultiScaleRidgedField` e `DomainWarp` (costruiti su Perlin) vanno verificati lì. |
| 3.31 | **[NON-STANDARD]** piccolo | `Rendering/Renderer.cs:700` | I differenziali di raggio sono ridotti di `1/√spp` senza minimo: ad alti spp il filtro delle texture scende sotto il pixel (PBRT-v4 usa `max(1/8, 1/√spp)`), con aliasing e accessi di cache peggiori. Il footprint dei raggi di camera è verificato (§4 Fase 1); la propagazione dopo riflessione e rifrazione non è ancora testata. Fase 4. |
| 3.32 | **[DEBITO]** | `Core/MathUtils.cs` (`Refract`) | Con la riflessione totale interna `Refract` prende `Abs(1 - sin²)` invece di segnalarla e restituisce una direzione senza significato: oggi tutti i chiamanti controllano prima, ma l'API non lo impone. Fase 5 (con il vetro). |
| 3.33 | **[BUG]** | `Denoising/` (NFOR) | Attorno a luci piccole e molto luminose sfocate (bokeh) il denoiser disegna un **alone scuro** più scuro dello sfondo: la regressione al primo ordine sbanda su valori HDR estremi. Senza denoiser l'alone non c'è (verificato con `scenes/showcases/camera-physical.yaml`). Colpisce i preset `draft*`, `standard*`, `pre-final*`. Fase 9. |
| 3.34 | **[NON-STANDARD]** rimandato | camera | Esposizione fisica (ISO, tempo di posa, EV100) non implementata: senza unità fotometriche per le luci (3.14) un'esposizione in EV100 non ha un riferimento. Va fatta con le unità nella Fase 6. |

---

## 4. Le fasi

Ordine pensato perché ogni fase si appoggi su quelle già verificate: un errore nel campionamento
falsa tutti i test dei materiali, un errore nel tone mapping falsa ogni giudizio "a occhio".

### Fase 0 - Infrastruttura di verifica (prerequisito)

- [x] Flag `--reference`: niente clamp, niente clamp indiretto, niente denoiser, niente ombre
      trasparenti, niente photon map, niente soglia "path morto", RR a soglie fisse (non dipende
      più dalla classificazione della scena). Più `--seed <n>` per render indipendenti e il
      conteggio dei NaN/Inf azzerati (stampato a fine render).
- [x] Libreria di test statistici (chi-quadro, integrazione pdf, furnace, cross-check dell'albedo,
      reciprocità, coerenza NEE/BSDF, coerenza Sample/Evaluate/Pdf):
      `src/RayTracer.Tests/Validation/BsdfTestKit.cs`, gate in `BsdfStatisticalTests`, report
      completo in `BsdfAuditTests`. Phase function e luci: da agganciare nelle Fasi 6 e 8.
- [x] Tool `ImageCompare` in `src/Tools/`: rapporto delle medie, relMSE, RMSE, SMAPE, pixel
      fuori del 10%, mappa differenza in falsi colori (FLIP calcolato dall'harness Python).
- [x] Mitsuba 3.9.1 (`pip install -r validation/requirements.txt`), convertitore YAML → Mitsuba
      e prime 6 scene canoniche in `validation/scenes/` (harness `validation/run.py`, vedi
      `validation/README.md`). Le altre scene di §5 si aggiungono nella fase che le usa.
- [x] Test di convergenza `1/N` (spp crescenti + K seed indipendenti, pendenza e soglia residua):
      `validation/run.py --convergence`.
- [x] Scene analitiche come test xUnit (`AnalyticSceneTests`): furnace Lambert, vetro, specchio,
      legge dell'inverso del quadrato.

#### Esito Fase 0 (2026-10-20)

Suite: 653 test verdi, 4 skip che riproducono bug noti (si riattivano quando il bug è corretto);
+6 s sulla suite.

**Verificato corretto** (ora protetto da test):

- Integratore: Cornell box lambertiana in modalità di riferimento = Mitsuba (rapporto 0.9999 a
  profondità 32, 0.998 a profondità 2; relMSE 1e-4). Convergenza: pendenza -0.98 sui seed
  indipendenti, soglia residua 9e-6 (il rumore del riferimento): stimatore non distorto.
- Luce puntiforme esatta (rapporto 1.0000, legge dell'inverso del quadrato anche in xUnit).
- Furnace: Lambert = albedo, vetro `dielectric` = 1, specchio = 1.
- Vetro: in modalità di riferimento le caustiche per path tracing puro coincidono con Mitsuba
  (rapporto 0.986, il residuo è 3.19).
- BSDF: Lambert, Metal (campionamento), tutti i lobi rough del Disney e il Mix campionano dalla
  propria Pdf (chi-quadro), pesi coerenti (cross-check albedo), reciprocità esatta ovunque.

**Risultati della validazione** (`validation/run.py`, 3D-Ray contro Mitsuba 3):

| Scena | Riferimento | Default | Note |
|---|---|---|---|
| cornell-lambert d32 | 0.9999 | **0.782** | default: clamp `-C 10` (3.20) |
| cornell-lambert d2 | 0.998 | **0.757** | idem |
| lights | **0.975** | **0.975** | luce d'area -12%, sferica -1.7% (3.19) |
| metal-rough | **1.376** | 1.290 | lobo extra nella NEE del Metal (3.7) |
| glass-shadow | 0.986 | 1.067, relMSE x50 | ombre trasparenti (3.21) |
| disney-spheres | 1.022 | 1.017 | sheen +34%, clearcoat +5% (3.17; modello diverso) |
| furnace-disney-glass | **1.031** (atteso 1) | 1.031 | energia creata (3.17) |

**Nuovi problemi**: da 3.17 a 3.23 in §3, e 3.2, 3.6, 3.7, 3.13 aggiornati con le misure.

**Verdetto**: infrastruttura completa. La base matematica dell'integratore è sana; i problemi di
realismo vengono da materiali (Metal, strati Disney), luci (offset dei raggi d'ombra) e dai bias
attivi di default. Nota: i bug 3.19, 3.20 e 3.21 sono gravi, visibili in ogni scena e con
correzioni piccole; valutare se anticiparli prima della Fase 1.

#### Correzioni anticipate 3.19, 3.20, 3.21 (2026-10-20)

Decise dopo la Fase 0 perché gravi e visibili in ogni scena. Stessa validazione, prima e dopo
(rapporto delle luminanze medie contro Mitsuba 3; tra parentesi relMSE):

| Scena | Modalità | Prima | Dopo |
|---|---|---|---|
| cornell-lambert d32 | normale | 0.782 | **1.0000** (1e-4) |
| cornell-lambert d2 | normale | 0.757 | **0.998** (6e-5) |
| lights | riferimento e normale | 0.975 (1.5e-3) | **1.0000** (1.8e-5) |
| glass-shadow | normale | 1.067 (0.23) | **1.0001** (4e-4) |
| glass-shadow | riferimento | 0.986 (4e-3) | **0.9997** (5e-4) |
| null-shell (nuova) | entrambe | | **1.0001** (6e-5) |

Convergenza: `lights` non ha più un plateau (la soglia residua scende con il rumore del
riferimento: 5.2e-5 a 4096 spp di Mitsuba, 2.0e-5 a 16384); `glass-shadow` pendenza -1.00 sui seed
indipendenti. Restano fuori, come atteso: `metal-rough` (3.7), `furnace-disney-glass` e
`disney-spheres` (3.17), da chiudere nella Fase 5. Suite: 661 verdi, 4 skip di bug noti.

Effetti sul rendering da sapere: le scene con vetro ora mostrano ombre e caustiche fisiche
(rumorose con pochi campioni: `--caustics on` o `--transparent-shadows on` per un'anteprima), e
le scene con luci molto intense possono mostrare qualche firefly in più senza il clamp (il
denoiser dei preset draft/standard/pre-final li assorbe; altrimenti `-C`).

### Fase 1 - Fondamenta matematiche (`Core/`)

| Modulo | Cosa verificare |
|---|---|
| `MathUtils`, `Ray`, `AABB` | Epsilon fisso vs offset basato sull'errore floating point (PBRT "spawn ray" con bound d'errore): fixed epsilon = acne o peter-panning a scale diverse. `AABB.Hit` con NaN e raggi paralleli agli assi. |
| Costruzione basi ortonormali | Metodo branchless di Duff et al. 2017 (Frisvad ha errori noti vicino a -Z). Ortogonalità del frame tangente con normal map. |
| Routine di campionamento | Coseno-emisfero, sfera, cono, disco concentrico (Shirley-Chiu): chi-quadro e pdf. |
| `Sampling/` | Vedi 3.10. Test di discrepanza, test di convergenza su integrali noti 2D/4D, dimensioni stabili per rimbalzo, correlazioni tra pixel vicini. Valutare blue-noise screen-space (Heitz-Belcour 2019). |
| `QuarticSolver`, `SturmSolver` | Precisione float vs double sul toro e lathe, radici multiple, casi limite (raggio tangente). |
| `Perlin`, `WorleyNoise`, `MultiScaleRidgedField`, `DomainWarp` | Range di uscita, continuità, gradienti analitici, assenza di artefatti di griglia. |
| `RayDifferential`, `FootprintMath`, `FilterFootprint` | Conformità a PBRT §10.1, comportamento dopo riflessione/rifrazione. |

- [x] Offset dei raggi relativi alla scena (3.27); `AABB.Hit` con NaN (3.28) e raggi paralleli agli assi.
- [x] Basi ortonormali di Duff et al. (3.26). Il frame tangente con normal map va alla Fase 4 (MikkTSpace).
- [x] Routine di campionamento: chi-quadro e integrale della pdf di sfera, emisfero, cono, disco concentrico, phase function, HDRI (3.9).
- [x] Sampler rifatto (3.10): stratificazione 2D, dimensioni per vertice, correlazioni. Blue-noise screen-space: valutato, rimandato alla Fase 9 (interagisce con il denoiser).
- [x] Solver: radici note, coppie vicine, toro contro un riferimento in doppia precisione (3.29).
- [x] Perlin e Worley misurati; correzione del Perlin rimandata alla Fase 4 (3.30).
- [x] Footprint dei differenziali di camera contro la differenza finita; dopo riflessione/rifrazione e minimo 1/8 alla Fase 4 (3.31).

#### Esito Fase 1 (2026-10-20)

Suite: 697 test verdi, 4 skip dei bug noti della Fase 0 (+36 test). I nuovi test sono in
`src/RayTracer.Tests/Validation/` (più uno in `AabbTests`). Quelli che proteggono una correzione
(sampler, scala, basi, HDRI, AABB, radici vicine) sono stati verificati **fallire** sul codice di
prima; gli altri (rumore, Sturm, toro, differenziali) fissano un comportamento misurato corretto.

**Sampler (3.10).** Misura: integrali 2D noti su 512 pixel indipendenti, pendenza dell'errore RMS
contro il numero di campioni (Gaussiana liscia e bordo di un disco, cioè un'ombra):

| Coppia di dimensioni | Prima | Dopo | Numeri indipendenti |
|---|---|---|---|
| 0-1 (pixel) | -1.38 / -0.75 | -1.38 / -0.75 | -0.50 / -0.50 |
| 2-3, 8-9, 40-41 | da -0.32 a -0.56 | **-1.38 / -0.74** | -0.50 / -0.50 |
| inizio dispari (9-10) con `Sample2D` | | **-1.36 / -0.73** | |

Correlazione fra dimensioni dello stesso campione ≤ 0.006 (rumore statistico). Effetto sui render a
parità di campioni (relMSE contro Mitsuba): Cornell 256 spp da 4.3e-4 a **3.0e-4** (PRNG 6.8e-4),
`null-shell` da 1.6e-4 a **7.0e-5**, `lights` 16 spp da 7.7e-4 a **3.7e-4**. Il rumore cambia
disegno (i render non sono più bit-identici alle versioni precedenti; `--seed 0` resta
deterministico).

**Offset dei raggi (3.27).** Rapporto della luminanza media contro la stessa scena a scala 1:

| Scena | Prima (epsilon 1e-4) | Dopo |
|---|---|---|
| scalata a 10 µm | 0.000 (nera) | **1.0000** |
| scalata a 1 mm | 0.999 | **1.0000** |
| scalata a 1 km | 0.933 | **1.0000** |
| spostata a 10 km | 0.557 | **1.014** |
| spostata a 100 km | 0.684 | 0.937 |

A 100 km il limite è la precisione float delle coordinate stesse: la soluzione è il rendering
relativo alla camera (Fase 11). Gli showcase con geometrie delicate (lathe, heightfield, CSG,
extrusion, toro, Cornell, vetro) rifatti con i nuovi offset: relMSE ≤ 1.2e-4 contro prima, sotto il
rumore fra due seed, nessuna acne.

**Basi ortonormali (3.26)**: errore massimo da 4.5e-2 (Frisvad vicino a -Z) a 2.9e-7.
**HDRI (3.9)**: campionamento continuo, chi-quadro da 1.3e6 a superato.
**AABB (3.28)**: NaN conservativo, costo nullo nel benchmark BVH.
**Solver (3.29)**: quartica con radici distinte 1.5e-10, coppie vicine fino a 1e-5 sempre trovate,
Sturm 1.2e-6 (la tolleranza è un residuo, cioè un micron su un segmento di profilo di un metro),
toro contro il riferimento in doppia precisione su 20.000 raggi: 0 hit persi, 0 inventati, errore su
t 8.7e-7.
**Rumore (3.30)**: Perlin C1 corretto (salto di gradiente < 0.05), curvatura discontinua (12.7),
anisotropia 5.7%; Worley F1 1-Lipschitz (1.0001).
**Differenziali (3.31)**: errore del footprint al 95° percentile 5.6e-5 sul piano; sulla sfera
0.104 con pixel 1/64 e 0.013 con pixel 1/512 (primo ordine, come atteso).

**Validazione contro Mitsuba** dopo la Fase 1 (rapporto; relMSE prima e dopo):

| Scena | Riferimento | Default |
|---|---|---|
| cornell-lambert d32 | 0.9997 | 0.9998 (1e-4 → 6.0e-5) |
| cornell-lambert d2 | 0.998 | 0.998 |
| lights | 0.9999 (1.8e-5 → 1.6e-5) | 0.9999 |
| glass-shadow | 0.9998 | 0.9997 (4e-4, invariato: domina il rumore delle caustiche) |
| null-shell | 1.0001 | 1.0001 (6e-5 → 3.7e-5) |
| metal-rough, disney-spheres, furnace-disney-glass | invariati | 3.7 e 3.17, Fase 5 |

Convergenza: Cornell pendenza -1.02 sui spp e -0.95 sui seed; `null-shell` -0.89 e -0.74, perché
l'errore arriva alla soglia del rumore del riferimento (2.6e-5).

Emerso nel farlo e corretto subito: `MathUtils.SceneScale` è per thread e il renderer lo lasciava
impostato sui thread del pool alla fine del render; ora ogni tile lo azzera come già faceva per
`ShadowRay.BlockAllTransmission`.

**Verdetto**: le fondamenta sono ora verificate e protette da test. Il sampler è stato rifatto
(era il problema più serio: peggio del caso su ogni decisione dopo la prima), gli offset sono
indipendenti dalla scala, solver e basi sono a precisione di macchina. Restano alla Fase 4 il
Perlin migliorato e i differenziali dopo i rimbalzi, alla Fase 5 `Refract`, alla Fase 11 il
rendering relativo alla camera.

### Fase 2 - Geometria e intersezioni (`Geometry/`, `Acceleration/`)

- [ ] Per ogni primitiva (sphere, quad, box, disk, annulus, cylinder, cone, capsule, torus,
      triangle, smooth triangle, mesh, lathe, extrusion, heightfield, infinite plane):
      hit corretto, normale geometrica vs di shading, `FrontFace`, UV, `dpdu/dpdv`, area e pdf di
      campionamento (`ISamplable`, `ISolidAngleSamplable`) che integrano a 1.
- [ ] Triangoli: Möller-Trumbore vs intersezione **watertight** (Woop et al. 2013), standard in
      Embree/PBRT: niente "buchi" lungo gli spigoli delle mesh.
- [ ] `Transform`: normali con inversa trasposta, scala non uniforme, scala negativa (inversione
      dell'orientamento), `AnimatedTransform` e bound dell'otturatore.
- [ ] CSG: intervalli dentro/fuori, coplanarità, normali sulle facce sottratte.
- [ ] Subdivision Loop/Catmull-Clark: confronto con le superfici limite di OpenSubdiv (bordi,
      creases, vertici straordinari). Displacement stack: ordine, normali dopo displacement.
- [ ] BVH: correttezza (esiste già il test di equivalenza) e qualità SAH (costo medio per raggio);
      performance rimandata alla Fase 11.
- [ ] `ObjLoader`: n-gon, normali mancanti, MTL. Proposta: aggiungere glTF 2.0 e PLY.

### Fase 3 - Camera, film e colore (`Camera/`, `Rendering/` output)

- [x] Camera: generazione raggi pinhole e thin lens (campionamento lente concentrico), profondità
      di campo coerente con la formula della lente sottile, motion blur camera.
- [x] Filtro di ricostruzione dei pixel: era un box implicito; ora Gaussiano (default),
      Blackman-Harris o box, con *filter importance sampling*.
- [x] **Camera fisica** (estensione): `focal_length`, `sensor_width`, `f_stop`, `meters_per_unit`,
      messa a fuoco con le chiavi esistenti; bokeh poligonale e anamorfico. Esposizione da ISO e
      tempo di posa (EV100) rimandata alla Fase 6 con le unità fotometriche (3.34).
- [x] **Color management** (punto 3.4-3.5): working space dichiarato Rec.709 lineare, view
      transform `agx` (default), `aces` (RRT+ODT reale), `standard`, `legacy`; look AgX `punchy` e
      `golden`; dithering sull'8 bit; PNG 16 bit. I confronti restano su PFM/EXR lineari. `filmic`
      non fatto: AgX ne è il successore dello stesso autore. ACEScg come working space e ACES 2.0
      rimandati alla Fase 10 (semantica del colore nel formato v2).
- [x] White balance (temperatura in Kelvin, adattamento cromatico Bradford).

#### Esito Fase 3 (2026-10-21)

Suite: 776 test verdi, 4 skip dei bug noti (+79 test). Tutti nuovi, in `src/RayTracer.Tests/Validation/`.

**Camera: corretta, non è cambiato nulla nel modello.** La proiezione prospettica coincide con
quella scritta indipendentemente (errore angolare < 2e-6 rad); i raggi di un pixel convergono sul
piano di fuoco entro 1e-5 della distanza; la sfocatura di ogni profondità segue la formula della
lente sottile `A |d - f| / d`; il campionamento della lente è uniforme. Il motion blur della camera
era già coperto da `MotionBlurTests`.

**Filtro di ricostruzione: era un box implicito.** Ora Gaussiano σ 0.5 px (default della CLI,
stessa definizione di Mitsuba), Blackman-Harris o box, per filter importance sampling. La risposta
al gradino di un bordo renderizzato coincide con la CDF del filtro entro 0.01 per tutti e tre. Il
box resta bit-identico a prima (stessi numeri del sampler, stessa espressione).

**Camera fisica**: `focal_length` + `sensor_width` → campo visivo, `f_stop` → pupilla (in metri
con `meters_per_unit`), diaframma a lamelle e anamorfico campionati uniformemente (chi-quadro per
settori e raggio). Showcase `scenes/showcases/camera-physical.yaml`.

**Gestione del colore** (3.4, 3.5):

| Vista | 0.18 → | 1.0 → | Verifica |
|---|---|---|---|
| `agx` (default) | 0.500 | 0.792 | contro un'implementazione indipendente, < 2e-4 |
| `aces` | 0.356 | 0.812 | contro OpenColorIO 2.6 (config ACES 1.3), 24 colori, < 1e-3 (scarto massimo misurato 7e-4) |
| `standard` | 0.461 | 1.000 | curva sRGB a tratti |
| `legacy` | 0.548 | 0.905 | identica alla curva di prima |

Grigi neutri e monotoni in ogni vista. Texture decodificate con la curva sRGB a tratti (il byte 10
passava da 3.0e-3 a 8.0e-4 con la potenza 2.2). Bilanciamento del bianco: 6504 K è l'identità
(< 2e-3), una luce della temperatura scelta diventa neutra da 1900 a 9000 K. Dithering a 8 bit:
media di un campo uniforme esatta entro 0.01 livelli.

**Validazione contro Mitsuba** con lo stesso filtro Gaussiano da entrambe le parti (riferimenti
rigenerati). Il gestore dei colori non entra nel confronto: l'harness confronta PFM lineari.

| Scena | Riferimento | Default | relMSE default, Fase 1 → Fase 3 |
|---|---|---|---|
| cornell-lambert d32 | 0.9998 | 0.9999 | 6.0e-5 → 5.7e-5 |
| cornell-lambert d2 | 0.998 | 0.998 | 3.8e-5 → 4.1e-5 |
| lights | 0.9997 | 0.9997 | 1.6e-5 → 1.3e-5 |
| glass-shadow | 0.9997 | 0.9995 | 4.1e-4 → 4.0e-4 |
| null-shell | 1.0001 | 1.0001 | 3.7e-5 → 1.9e-5 |
| metal-rough, disney-spheres, furnace-disney-glass | invariati | | 3.7 e 3.17, Fase 5 |

Rapporti invariati; il relMSE scende dove pesava la differenza di filtro fra i due motori (prima
box contro box, ma con il rumore distribuito diversamente). Convergenza Cornell: pendenza -1.14 sui
spp e -0.98 sui seed, soglia 3.2e-5 (il rumore del riferimento): stimatore non distorto.

**Nuovi problemi**: 3.33 (NFOR disegna un alone scuro attorno al bokeh di luci piccole e intense,
Fase 9), 3.34 (esposizione fisica da ISO e tempo di posa, Fase 6 con le unità delle luci).

**Verdetto**: camera tenuta ed estesa; colore rifatto. Il default della CLI cambia aspetto a ogni
render (AgX al posto della curva vecchia, filtro Gaussiano al posto del box): `--view legacy
--filter box` riproduce i render precedenti.

### Fase 4 - Texture e dati (`Textures/`)

- [ ] Attributo `color_space` per texture (`srgb`, `linear`, `raw`) e default automatico per
      ruolo (colore = sRGB, roughness/metallic/normal/bump/displacement = raw). Corregge 3.3.
- [ ] Curva sRGB esatta (3.4). Loader `.hdr`/`.exr`: canali, esponente RGBE, half float.
- [ ] Filtraggio: mipmap trilineare già presente; premium = anisotropico (EWA). UDIM e texture
      a tile/cache per scene grandi.
- [ ] Normal map: convenzione tangenti **MikkTSpace** (lo standard di tutti i baker: senza, le
      normal map importate mostrano cuciture), OpenGL/DirectX, `strength`.
- [ ] Bump: scala in unità mondo, coerenza con il displacement (test esistenti da rivedere).
- [ ] Procedurali (marble, wood, voronoi, noise, brick, gradient, color ramp): range, antialias
      (clamp delle ottave vs footprint), coerenza tra spazio oggetto e mondo.
- [ ] `HdriSunExtractor`: conservazione dell'energia quando il sole viene tolto dall'HDRI.

### Fase 5 - Materiali e BSDF (`Materials/`): il cuore del realismo

Ordine interno, dal più semplice:

1. **Lambertian**: furnace = albedo, chi-quadro del coseno.
2. **Dielectric liscio**: Fresnel esatto, TIR, IOR relativo con `IorStack` (vetro in acqua,
   liquido nel bicchiere: priorità alla Schmidt-Budge 2002), Beer-Lambert.
3. **Microfacet core** (`Microfacet.cs`): GGX D normalizzata (`∫ D cosθm dωm = 1`), Smith
   G1 e G2 **height-correlated**, campionamento VNDF (Heitz 2018 o *spherical caps* di Dupuy 2023),
   anisotropia (parametrizzazione roughness → alpha).
4. **Conduttore**: Fresnel complesso con IOR `n + ik` (preset oro, argento, rame, alluminio,
   ferro...), modello artistico F82 di OpenPBR, togliere il lobo lambertiano (3.7), Kulla-Conty.
5. **Dielectric rough** (Walter 2007): BTDF, pdf, reciprocità con il fattore `η²`, MIS (3.6).
6. **Disney/Principled**: ogni lobo da solo e poi la miscela; probabilità dei lobi (3.2), MIS
   (3.13), LUT Kulla-Conty (confronto con valori di riferimento), sheen Charlie, clearcoat,
   thin film (Belcour-Barla), `thin_walled`, `diff_trans`, retro-riflessione di Burley.
7. **MixMaterial**: scelta stocastica vs blend, coerenza tra `Sample` e `Pdf` della miscela.
8. **Emissive**: radianza unilaterale/bilaterale, unità.
9. **Shadow terminator e normali di shading** (3.11): correzione dell'aggiunto di Veach per i
   path dalla luce (photon map), confronto con Hanika 2021.

**Decisione strategica da prendere alla fine della fase**: adottare **OpenPBR Surface** come
materiale unico (stack fisico: fuzz, coat, specular, metal/dielectric base, subsurface,
trasmissione, thin film, con layering ad albedo-scaling che conserva l'energia) e trasformare
`lambertian`/`metal`/`dielectric`/`disney` in preset che lo configurano. È ciò che fanno oggi
Arnold, Karma, RenderMan (via MaterialX) e V-Ray: una sola BSDF ben testata invece di cinque.
Unificare contemporaneamente l'API (3.12) su `Sample`/`Evaluate`/`Pdf`.

### Fase 6 - Luci e illuminazione diretta (`Lights/`, `Rendering/Sky/`)

- [ ] Per ogni luce (point, spot, directional, area, sphere, geometry, portal, environment,
      physical sun): radianza, pdf in angolo solido, conversione area → angolo solido,
      `ApproximatePower` coerente con la potenza reale (serve alla scelta proporzionale).
- [ ] Area light rettangolari: campionamento sferico del rettangolo (Ureña 2013) invece che
      per area: molto meno rumore vicino alla luce.
- [ ] Spot: falloff fisico, profili **IES** (estensione).
- [ ] **Unità fisiche** (3.14): `power` in W o lm, `intensity` in cd, `luminance` in nit, sole in
      lux; `temperature` in Kelvin (corpo nero); flag `normalize` (potenza indipendente
      dall'area, come Arnold/Karma); esposizione per luce in stop.
- [ ] Many-light sampling: **light BVH** (Conty-Kulla 2018) per scene con molte luci/emissivi.
- [ ] HDRI: campionamento continuo nel texel (3.9), rotazione, MIS compensation
      (Karlík 2019).
- [ ] **Cielo**: correggere Preetham (3.1) o, meglio, sostituirlo con **Hosek-Wilkie** o
      **Prague 2021**; validare Nishita (ozono, unità, coerenza sole/cielo). Sole e cielo devono
      condividere le stesse unità fotometriche.
- [x] Ombre trasparenti (3.8, 3.21): `--transparent-shadows`, spente per default e in riferimento
      (2026-10-20). Da valutare un controllo per materiale nella Fase 10.

### Fase 7 - Integratore (`Rendering/Renderer.cs`)

- [ ] Riscrivere `TraceRay` come **loop iterativo** con throughput esplicito (prerequisito per
      path guiding, LPE/AOV per lobo, debug, profondità per tipo di lobo).
- [ ] MIS: pesi corretti per luce scelta (`p_pick x p_light`), emissione colpita via BSDF, lobi
      delta, coerenza delle pdf (3.13). Test Veach (scena dei 4 piani lucidi e 4 luci).
- [ ] Russian Roulette standard sul throughput (con correzione `η²` dopo le rifrazioni), niente
      classificazione euristica della scena.
- [ ] Profondità separate per tipo (diffuse, glossy, transmission, volume) come i motori di
      produzione: il vetro richiede molti rimbalzi, il diffuso pochi.
- [x] Clamp: spento di default, `-C` opzionale (3.8, 3.20; 2026-10-20).
- [ ] Caustiche: la photon map è biased per natura. Valutare **MNEE** (manifold next-event
      estimation, per le caustiche attraverso vetro/acqua) e/o **path guiding** (roadmap #23)
      come strada premium non biased.
- [ ] Unbiasedness: confronto con il `path` integrator di Mitsuba a parità di profondità su
      tutte le scene canoniche.

### Fase 8 - Volumi e SSS (`Volumetrics/`, `Rendering/RandomWalkSss.cs`)

- [ ] Phase function (isotropica, HG, double HG, Rayleigh, Schlick): normalizzazione,
      campionamento, chi-quadro.
- [ ] Mezzi omogenei cromatici: campionamento a canale "hero" con MIS spettrale (Wrenninge 2017 /
      Miller 2019 null-scattering) invece del campionamento su un singolo canale.
- [ ] Delta tracking e ratio tracking: non-polarizzazione, majorant corretti (mezzi grid e
      procedurali), confronto con Mitsuba `volpath`.
- [ ] Atmosfera Nishita: densità, coefficienti Rayleigh/Mie, assorbimento dell'ozono.
- [ ] SSS random walk: parametrizzazione artistica (colore + mean free path) con inversione
      dell'albedo di Christensen-Burley 2015 o Chiang 2016, test energetici, confronto con
      Mitsuba.
- [ ] Estensioni premium: mezzi emissivi, VDB (NanoVDB), interfacce mezzo per entità.

### Fase 9 - Post-processing e denoiser (`Denoising/`)

- [ ] NFOR: stima della varianza dual-buffer, selezione MSE, nessuna perdita di dettaglio sulle
      texture (test su scena con texture fini).
- [ ] Integrare **OIDN** come opzione (`--denoiser oidn`): è lo standard di fatto in produzione
      su CPU. Licenza Apache-2.0, libreria nativa.
- [ ] Adaptive sampling (roadmap #14) sulla varianza dual-buffer già esistente.

### Fase 10 - Formato scene YAML

**Risposta breve: sì, va modificato, ma per evoluzione (v2 con tool di migrazione), non da zero.**
YAML come formato di authoring è una buona scelta; i problemi sono di semantica, non di sintassi.

Cosa manca rispetto a un formato premium:

1. **Versione** del formato: `format_version: 2`, con migrazione automatica dalla v1
   (generalizzare `MigrateFakeSss` in un tool `SceneMigrate`).
2. **Unità**: `units: { length: meters }` a livello scena; luci in unità fotometriche; camera
   in millimetri.
3. **Colore**: `color_space` per texture e, opzionalmente, per colori (`[r,g,b]` sRGB o lineare
   va dichiarato), `temperature: 3200` come alternativa a un RGB.
4. **Blocco `render:`** nel file (risoluzione, spp, profondità per tipo, clamp, denoiser,
   view transform), con la CLI che lo sovrascrive. Oggi il "come renderizzare" vive solo nei
   commenti delle scene.
5. **Camera fisica**: `focal_length`, `sensor`, `f_stop`, `focus_distance`, `shutter`,
   `exposure`. `fov` resta come alternativa.
6. **Materiali**: nomi dei parametri allineati a OpenPBR/MaterialX, texture come nodo
   (`{ file, color_space, filter, wrap, uv_set, transform }`), mappe IOR complesse per i metalli.
7. **Luci**: `power`/`intensity`/`exposure`/`normalize`/`temperature`/`ies`.
8. **Trasformazioni**: un blocco uniforme `transform: { translate, rotate, rotate_order, scale }`
   o `matrix` per tutte le entità, invece di parametri specifici per primitiva.
9. **Validazione**: JSON Schema del formato (autocompletamento e validazione nell'editor) ed
   errore esplicito sulle chiavi sconosciute (da verificare come si comporta oggi YamlDotNet).
10. **Interscambio**: import glTF 2.0 (con mappatura PBR) nel breve; OpenUSD nel lungo periodo
    (è il formato di scena di Karma, RenderMan, Arnold, Omniverse).
11. **Codice**: spezzare `SceneLoader.cs` (4368 righe) per sezione (camera, luci, materiali,
    texture, entità, mondo), già nel TODO.

### Fase 11 - Architettura e performance (dopo la correttezza)

- [ ] Aggiornare `SixLabors.ImageSharp` (vulnerabilità note).
- [ ] BVH appiattito con traversata a stack esplicito (già nelle idee), SAH a bin, valutare
      **Embree** (Apache-2.0) per mesh grandi.
- [ ] Precisione: float vs double nei solver e nelle scene molto grandi.
- [ ] Rendering spettrale (roadmap #24): necessario per dispersione e per metalli/thin film
      davvero accurati; da decidere dopo la Fase 5 (impatta tutta l'API dei materiali).

---

## 5. Scene canoniche di validazione

Da creare in `scenes/validation/` (YAML) e `scenes/validation/mitsuba/` (XML):

1. White furnace: sfera in ambiente uniforme, una per materiale.
2. Sfera Lambert sotto luce puntiforme e su piano (soluzione analitica).
3. Cornell box originale (con dati spettrali misurati, confronto Mitsuba).
4. Scena MIS di Veach (4 lastre a rugosità crescente x 4 luci di dimensione crescente).
5. Sfere di conduttori (oro, rame, alluminio, cromo) a rugosità 0 / 0.2 / 0.5 sotto HDRI.
6. Vetro: sfera piena, lastra spessa, vetro satinato, bicchiere con liquido (IOR annidati).
7. Coat + sheen + thin film su sfere (materiale-test tipo "shader ball").
8. SSS: busto o drago con pelle/giada/marmo.
9. Volume: cubo di fumo omogeneo e eterogeneo, nebbia con luce spot (god rays).
10. Cielo fisico a 5 altezze del sole (alba, mattina, mezzogiorno, tramonto, crepuscolo).
11. HDRI con sole forte: ombre nette, nessun gradino.
12. Piano a scacchi all'orizzonte: aliasing e filtraggio texture.
13. Profondità di campo e motion blur.
14. Interno illuminato da finestra (GI difficile): convergenza e rumore.
15. Molte luci (100+ emissivi piccoli): many-light sampling.

---

## 6. Verdetto preliminare per modulo

Basato sulla sezione 3, aggiornato con le misure delle Fasi 0, 1 e 3: da confermare fase per fase.

| Modulo | Verdetto preliminare | Motivo |
|---|---|---|
| Core math, noise, solver | **tenere** (Fase 1) | verificati e corretti (offset, basi, AABB, cubica); resta il Perlin (Fase 4) |
| Sampler | **rifatto** (Fase 1) | Sobol "padded" con dimensioni per vertice: stratificato in 2D su ogni decisione |
| Geometria e BVH | tenere / correggere | ampia e testata; triangolo watertight |
| Camera | **tenere** (Fase 3) | modello verificato; aggiunte camera fisica, bokeh a lamelle e anamorfico, filtro di ricostruzione |
| Tone mapping e colore | **rifatto** (Fase 3) | AgX, ACES 1.x verificato con OpenColorIO, sRGB esatto, bilanciamento del bianco; resta l'esposizione fisica (Fase 6) |
| Texture | correggere | `color_space`, sRGB esatto, MikkTSpace |
| Materiali | **rifare attorno a OpenPBR** | Fase 0: Metal con NEE incoerente (+37%), strati Disney che creano energia (sheen, coat, vetro), lobi delta sottostimati; doppia API. Si riusano Microfacet, VNDF, Fresnel, LUT (campionamento e reciprocità verificati) |
| Luci | correggere + unità fisiche | Fase 0: luce puntiforme esatta; il bug dei raggi d'ombra (3.19) è corretto e ora tutte le luci analitiche coincidono con Mitsuba; unità assenti |
| Cielo | **rifare** | Preetham sbagliato, sole empirico |
| Integratore | **rifare il loop** | Fase 0: la matematica è corretta (Cornell = Mitsuba entro 0.1%, convergenza 1/N); i bias di default (clamp, ombre trasparenti) sono corretti; resta la struttura ricorsiva. Si riusano NEE, MIS, mezzi |
| Volumi e SSS | verificare | da confrontare con Mitsuba prima di giudicare |
| Denoiser | tenere + OIDN | NFOR va bene come opzione |
| Formato scene | evolvere a v2 | semantica (unità, colore, render) più che sintassi |

## 7. Ordine di lavoro consigliato

1. Fase 0 (senza strumenti di verifica ogni giudizio è "a occhio").
2. Fasi 1 e 3 insieme: campionamento e colore sono i due filtri attraverso cui si guarda tutto.
3. Fase 5 (materiali) e Fase 6 (luci), che sono la gran parte del "non realistico".
4. Fase 7 (integratore), con il nuovo loop iterativo costruito sui materiali già verificati.
5. Fasi 2, 4, 8, 9 in parallelo dove serve.
6. Fase 10 (formato v2) quando materiali, luci e camera nuovi sono stabili, così il formato
   descrive il motore definitivo e la migrazione si fa una volta sola.
7. Fase 11.
