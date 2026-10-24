# Piano di review del motore 3D-Ray

Verifica completa di matematica, ottica e fisica del motore, modulo per modulo, dalle fondamenta ai
sottosistemi complessi. Obiettivo: capire cosa è corretto, cosa è un'approssimazione voluta, cosa è
sbagliato e cosa conviene rifare da zero per arrivare a un motore di livello "premium".

Documento interno (come `PLANNING.md` e `devlog/`): può citare renderer esterni.

> **Principio guida (proprietario, 2026-10-23): qualità premium del motore, nessun vincolo di
> compatibilità.** La review corregge e aggiorna l'app nel modo migliore possibile, senza
> preoccuparsi delle scene esistenti né di altri usi del formato o dell'API. Le scene del repo
> sono test serviti a sviluppare il motore prima della review: non sono punti fermi e non
> devono influire su come si fa la review. È il contrario: dopo la review le scene si correggono
> dove usano chiavi, materiali, proprietà, oggetti o altro che la review ha cambiato; se non
> vanno più bene si riscrivono, oppure si parcheggiano come obsolete (`scenes/obsolete/`) per
> essere riprogettate o eliminate. Vedi §2.0.

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
| 4 | Texture e dati | ✅ Chiusa (2026-10-21), esito in §4 Fase 4 |
| 5 | Materiali e BSDF | ✅ Chiusa (2026-10-21), esito in §4 Fase 5 |
| 6 | Luci, cielo | ✅ Chiusa (2026-10-22), esito in §4 Fase 6 |
| 7 | Integratore | ✅ Chiusa (2026-10-24), esito in §4 Fase 7 |
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

### 2.0 Nessun vincolo di compatibilità

Per ogni scelta vale la soluzione migliore per il motore. "Le scene esistenti cambierebbero
aspetto", "una chiave andrebbe rinominata", "un default cambierebbe" non sono argomenti per
tenere una soluzione peggiore: un default si cambia, una chiave si rinomina o si toglie, un
comportamento sbagliato non si conserva dietro un'opzione. Le scene si adeguano dopo, nella
revisione finale delle scene (fine della review): corrette, riscritte, oppure spostate in
`scenes/obsolete/` per essere riprogettate o eliminate.

Restano le regole del proprietario sulle scene create dalla review (`scenes/review/`,
`validation/scenes/`, showcase nuovi): non si cancellano, perché documentano una verifica.

**Scelte delle fasi già chiuse prese anche per compatibilità, da riconsiderare** (Fase 10, quando
si decide il formato, o prima se una fase tocca il codice):

- [ ] `intensity` senza unità accanto alle unità fotometriche (Fase 6): valutare se le unità
      fisiche diventano la forma principale, con la scena in nit e l'esposizione EV100 di default.
- [ ] `units: relative` di default dei cieli fisici e la scala relativa di Nishita (20) (Fase 6):
      valutare `photometric` come default, coerente con le luci fisiche.
- [ ] `noise_basis: classic` e il riscalamento a 0.67 del Perlin migliorato per tenere tarati
      range e color ramp delle scene vecchie (Fase 4).
- [ ] Il costruttore del `Renderer` con filtro box e view `legacy` come default per chi usa la
      libreria e per i test esistenti (Fase 3); la view `legacy` stessa.
- [ ] Il grigio 0.5 di default per i materiali senza `color` diversi da vetro ed emissivo (Fase 5).
- [ ] Il tipo `disney` tradotto su OpenPBR invece di essere sostituito (Fase 5): tenerlo come
      alias comodo o toglierlo a favore di `openpbr`.
- [ ] Chiavi e alias storici del loader (`rotation` del cielo, `size` del sole, alias dei tipi di
      luce, `hosek_wilkie` / `physical`): un nome per concetto.

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
| 3.1 | ✅ **corretto** (2026-10-22, Fase 6), era **[BUG]** | `Rendering/Sky/PreethamSky.cs:197` | Funzione di Perez sbagliata. Corretta: `(1 + A*e^(B/cosθ)) (1 + C*e^(Dγ) + E*cos²γ)`. Nel codice: `(C + D*e^(Eγ) + E*cos²γ)`, cioè coefficienti scalati di una posizione ed `E` usato due volte. Anche la normalizzazione `F(0, θs)` eredita l'errore. Il sole usa una costante empirica (`SolarConstantRel = 20`) e trasmittanze ad hoc. **Esito**: Perez corretta e normalizzata allo zenit (test `Preetham_ZenithIsTheZenithLuminance`, `Preetham_GlowsAroundTheSun`). Il sole di ogni cielo fisico viene dalla stessa luce del cielo (§3.57). `hosek_wilkie` non è più un alias di Preetham ma il modello vero (§3.58). |
| 3.2 | ✅ **corretto** (2026-10-21, Fase 5), era **[BUG]** | `Materials/DisneyBsdf.cs:1302, 1335, 1384, 1440, 1604, 1674, 1706` | Dopo aver scelto un lobo con probabilità `p`, il peso viene diviso per `max(p, 0.1)` (o `0.05`), ma `p` non è mai forzato a quel minimo in fase di scelta. **Misurato (Fase 0)**: il renderer usa `Sample()`, che per i lobi non-delta ricalcola `Evaluate`/`Pdf` e quindi non è toccato; il bug colpisce i lobi **delta** (speculare liscio, clearcoat liscio, trasmissione). Plastica liscia: riflesso speculare sottostimato del 9.4% a ogni angolo (p = 0.0906). Test `KnownBug_Disney_SmoothSpecularMatchesFresnel`. Fase 5. **Esito**: il tipo `disney` è tradotto su OpenPBR (§3.38), che sceglie ogni lobo delta con la stessa probabilità per cui ne divide il peso. Test `Disney_SmoothSpecularMatchesFresnel` riattivato: il riflesso della plastica liscia è il Fresnel esatto a ogni angolo. Nel render spariscono i firefly della plastica liscia (`scenes/review/disney-to-openpbr.yaml`). |
| 3.3 | ✅ **corretto** (2026-10-21, Fase 4), era **[BUG]** | `Scene/SceneLoader.cs:1802, 1902` + `Textures/ImageTexture.cs:63` | Le mappe dati (roughness, metallic, coat roughness...) passano per `ImageTexture`, che applica sempre la decodifica sRGB. Una roughness map viene quindi "linearizzata" come fosse un colore: valori sbagliati. Manca un attributo `color_space` per texture. **Esito**: `color_space: srgb | linear | raw` su ogni texture immagine, con default per ruolo: i colori decodificati da sRGB, i dati (roughness, metallic, pesi, IOR, maschere, bump, displacement, heightfield) letti come sono. Le immagini a 16 bit sono lette a 16 bit (prima a 8). Test `TextureDataTests`. Nel repo una sola scena cambia: il bump di cemento di `showcases/texture-bump-map.yaml`. |
| 3.4 | ✅ **corretto** (2026-10-21, Fase 3), era **[NON-STANDARD]** | `Textures/ImageTexture.cs` | Decodifica sRGB come `pow(x, 2.2)` invece della curva sRGB a tratti (IEC 61966-2-1): il byte 10 diventava 8.0e-4 invece di 3.0e-3, ombre delle texture troppo scure. Ora `Srgb.Decode` (stessa curva dell'uscita). |
| 3.5 | ✅ **corretto** (2026-10-21, Fase 3), era **[NON-STANDARD]** | `Rendering/ColorManagement.cs` | Display transform = fit di Narkowicz dell'ACES applicato **per canale** su Rec.709 + gamma 2.2 pura: hue shift e saturazione "Notorious 6" sulle alte luci, nessuna gestione del colore. Ora `--view agx` (default), `aces` (ACES 1.x RRT + ODT vero, entro 7e-4 da OpenColorIO), `standard` (curva sRGB a tratti); la curva vecchia resta come `legacy`. Bilanciamento del bianco, dithering, PNG 16 bit. |
| 3.6 | ✅ **corretto** (2026-10-21, Fase 5) | `Materials/Dielectric.cs`, `DisneyBsdf.cs:642-648, 773-778` | La trasmissione **rough** era esclusa da `Evaluate`/`Pdf` (solo varianza, niente NEE attraverso il vetro satinato). Il `dielectric` classico ora ha `roughness` con il modello completo di Walter et al. 2007 (BTDF, pdf dalle normali visibili, reciprocità generalizzata, MIS). Il vetro rough del Disney resta da fare con la decisione OpenPBR. **Esito**: il vetro ruvido del `disney` è ora la trasmissione di Walter di OpenPBR (stesso BSDF del `dielectric`, test di riduzione esatta `FullTransmission_IsTheRoughDielectric`). |
| 3.7 | ✅ **corretto** (2026-10-21, Fase 5), era **[BUG]** grave | `Materials/Metal.cs` | Il lobo lambertiano di riempimento esisteva solo nella NEE (+37% rispetto a Mitsuba). Metal riscritto: GGX con Smith correlato in altezza e campionamento VNDF, Fresnel da IOR complesso misurato (`preset`, `eta`/`k`, 15 metalli con gli stessi n, k di Mitsuba) o modello F82 (`color` + `edge_color`), scattering multiplo di Kulla-Conty con tabella E(μ, α) integrata offline (`validation/gen_microfacet_albedo.py`). Misure: `metal-rough` da 1.378 a **1.0032**, `metal-presets` (oro, rame, alluminio) **1.0001**, metallo bianco nel furnace = 1 a ogni rugosità. Il +0.3% residuo è la scelta del G correlato in altezza: Mitsuba usa il G separabile (verificato valutando il suo `roughconductor`), e con il G separabile il risultato è 1.0000. |
| 3.8 | ✅ **corretto** (2026-10-20 i clamp e le ombre trasparenti, 2026-10-24 la roulette, Fase 7), era **[BIAS]** di default, misurato | `Rendering/Renderer.cs:208, 221, 376-378` + materiali | Attivi di default: clamp per-sample 10, clamp indiretto 0.25 (= 2.5), ombre trasparenti "dritte" del vetro, Russian Roulette con soglie da una classificazione euristica della scena, `DeadPathThroughputEpsilon`. Tutti spenti da `--reference` (Fase 0). Il peso misurato dei primi due è in 3.20 e 3.21. Fase 7. Fase 7: roulette standard su β × η², tolti il classificatore e `DeadPathThroughputEpsilon`. |
| 3.9 | ✅ **corretto** (2026-10-20, Fase 1), era **[BIAS]** | `Textures/EnvironmentMap.cs` (`SampleDirection`) | L'importance sampling dell'HDRI restituiva sempre il **centro** del texel scelto: le direzioni NEE erano un insieme discreto (ombre a gradini e bias con HDRI piccole o sole concentrato). Ora la posizione dentro il texel è continua (lo scarto di ciascun numero casuale dentro il proprio intervallo della CDF) e la pdf usa il θ della direzione effettiva, come `PdfDirection`. Chi-quadro su una mappa 16x8 con un texel brillante: prima 1.3e6 su 799 gradi di libertà, ora passa. Test `EnvironmentMap_SamplesItsPdf_ContinuouslyInsideTexels`. |
| 3.10 | ✅ **corretto** (2026-10-20, Fase 1), era **[NON-STANDARD]**, misurato peggio del caso | `Core/Sampling/OwenSobol.cs` + `Sampler.cs` | Solo le dimensioni 0-1 erano vero Sobol; dalla 2 in su van der Corput con hash, e due dimensioni consecutive (la coppia di una decisione 2D) **non** erano stratificate insieme: pendenza dell'errore da -0.32 a -0.56 contro -0.50 dei numeri indipendenti, cioè peggio del PRNG. Le dimensioni erano assegnate con un contatore globale per campione, quindi path con rami diversi le disallineavano. Ora: Sobol "padded" (PBRT-v4), ogni coppia di dimensioni è la coppia 0-1 di Sobol con un rimescolamento Owen dell'indice proprio della coppia; blocco fisso per la camera (8 dimensioni) e per ogni vertice del path (64, `Sampler.BeginVertex`); `MathUtils.Random2D` / `Sampler.Sample2D` per ogni decisione 2D. Pendenze ora -1.38 (liscio) e -0.75 (bordo) su ogni coppia. Test `SamplerQualityTests`. |
| 3.11 | ✅ **corretto** (2026-10-21, Fase 5), era **[NON-STANDARD]** | `Rendering/Renderer.cs:1897` + `Core/MathUtils.cs:44` | Il fattore "shadow terminator" è applicato anche alla direzione di vista, oltre a quella della luce: non corrisponde a nessun modello pubblicato (Chiang 2019 / Estevez 2019 agiscono sul lato luce). Va verificato con il furnace test. **Esito**: il fattore lato vista è tolto (compensava la retro-riflessione del vecchio Disney, che non c'è più). Il fattore lato luce (Chiang 2019) era applicato solo alla NEE: il campionamento BSDF stimava un integrando diverso e il MIS mescolava due funzioni. Ora entra nel peso di entrambe le strategie (`ShadeSampleBounce`). Nessun effetto dove la normale non è perturbata. |
| 3.12 | ✅ **corretto** (2026-10-21, Fase 5), era **[DEBITO]** | `Materials/IMaterial.cs`, `Renderer`, `CausticPhotonTracer` | Due API in parallelo. `Scatter` è stato tolto: ogni materiale campiona con `Sample` (anche il vetro liscio) e null vuol dire assorbito. Il ripiego su `Scatter` introduceva due distorsioni: un `Sample` che restituiva null perché la direzione finiva sotto l'orizzonte (luce assorbita) veniva **ripescato** da `Scatter`, aggiungendo energia (+1.7% su `metal-rough` con il nuovo Metal), e il `MixMaterial` con un componente senza `Sample` ripescava un componente diverso (con Lambert + vetro liscio il Lambert contava (1 + t) volte). **Esito**: tolto anche `EvaluateDirect`: la NEE pesa ogni campione di luce con `Evaluate` per `|cos θ|` (`Renderer.DirectIntegrand`), la stessa funzione che il campionamento BSDF stima, quindi le due strategie MIS non possono più divergere (era la radice del §3.7). Render identici (Mitsuba: cornell, glass-rough, metal-rough, lights, stesse metriche alla quarta cifra). L'API dei materiali è ora `Evaluate` / `Pdf` / `Sample` più le query di trasmissione. |
| 3.13 | ✅ **verificato, nessun bug** | `DisneyBsdf` + `Renderer` | `Sample()` restituisce per i lobi non-delta `Evaluate`/`Pdf` della miscela, quindi peso e MIS sono coerenti: il chi-quadro e il cross-check dell'albedo passano su tutti i lobi rough (Fase 0). |
| 3.14 | ✅ **corretto** (2026-10-22, Fase 6), era **[NON-STANDARD]** | tutte le luci + `Emissive` | `intensity` senza unità: impossibile illuminare "come nella realtà" (sole 100.000 lux, lampadina 800 lm) e scambiare scene con altri motori. Nessuna temperatura colore, nessun IES. **Esito**: `power` (W a 683 lm/W o lm), `candela`, `luminance` (nit), `illuminance` (lux), `temperature` (corpo nero), `normalize`, `exposure` su ogni luce; `luminance` e `temperature` sull'`emissive`; profili IES LM-63 tipo C su point e spot. Scena in nit, colore ridotto a tinta. Verificato contro un calcolo a mano entro lo 0.1% (`scenes/review/light-units.yaml`) e dai test `PhysicalUnitsTests`, `IesProfileTests`. Cieli fisici in nit con `units: photometric`. |
| 3.15 | **[DEBITO]** | `scenes/cornell-box.yaml` e altre | Commenti obsoleti: dicono che l'emissivo "non usa NEE", mentre oggi entra nel pool NEE come `GeometryLight`. |
| 3.16 | ✅ **corretto** il loop (2026-10-24, Fase 7), resta **[DEBITO]** la dimensione dei file | `Rendering/Renderer.cs` (2314 righe), `Scene/SceneLoader.cs` (4368), `Materials/DisneyBsdf.cs` (2006) | `TraceRay` è ricorsivo, con molti casi speciali: ostacola path guiding, light path expressions, BDPT e il debug. Fase 7: `TracePath` iterativo in `Rendering/PathIntegrator.cs`; le dimensioni dei file sono della Fase 11. |
| 3.17 | ✅ **corretto** (2026-10-21, Fase 5), era **[BUG]** | `Materials/DisneyBsdf.cs` (sheen, clearcoat, trasmissione) | **Guadagno di energia** nel white furnace (albedo > 1 con materiale bianco): sheen fino a 1.38, clearcoat liscio 1.39 a incidenza radente, vetro liscio 1.015-1.13, vetro rough 1.03-1.12; diffuso Disney r=1 1.026 a incidenza radente (retro-riflessione di Burley, [NON-STANDARD]). Render: sfera di vetro Disney in furnace +3.1% (deve valere 1), sfera sheen +34% e clearcoat +5% rispetto al principled di Mitsuba. Gli strati vanno sommati con albedo-scaling che conserva l'energia (OpenPBR). Test `KnownBug_Disney_WhiteFurnaceAlbedoDoesNotExceedOne`. Fase 5. **Esito**: con la traduzione su OpenPBR gli strati si compongono con l'albedo-scaling: test `Disney_WhiteFurnaceAlbedoDoesNotExceedOne` riattivato (sheen, coat liscio, vetro liscio e ruvido, diffuso r 1), fornace della sfera di vetro Disney 1.00 (`Furnace_GlassSphereFromOutside_StaysLossless`). Il diffuso è Lambert: la retro-riflessione di Burley creava energia a incidenza radente. |
| 3.18 | ✅ **corretto** (2026-10-21, Fase 5), era **[BUG]** piccolo | `Materials/DisneyBsdf.cs:783-870` | La `Pdf` riporta la densità VNDF anche sotto l'orizzonte (convenzione legittima, il campione viene scartato), ma lì il lobo `diff_trans` ha supporto: per le direzioni posteriori la pdf dichiarata supera quella reale e il peso sottostima l'energia di circa l'1%. Test `KnownBug_Disney_DiffTransSampleWeightIntegratesTheBsdf`. Fase 5. **Esito**: `diff_trans` è il subsurface thin-walled di OpenPBR, ogni lobo scarta le direzioni fuori dal suo supporto. Test `Disney_DiffTransSampleWeightIntegratesTheBsdf` riattivato. |
| 3.19 | ✅ **corretto** (2026-10-20), era **[BUG]** grave | `Geometry/ShadowRay.cs` (`TransmittanceToPoint`), `Lights/AreaLight.cs`, `SphereLight.cs`, `GeometryLight.cs`, `Geometry/Sphere.cs` | Il raggio d'ombra partiva da `OffsetOrigin` (punto + 1e-4 lungo la normale) ma con la direzione calcolata dal punto non spostato: colpiva il **proxy emissivo della luce stessa**, e oltre ~52° dalla normale di una luce d'area non arrivava luce diretta (-12% nella scena `lights`, ellisse netta sul pavimento; taglio anche su emissivi e luci sferiche). Corretto con il "spawn ray to" di PBRT: entrambi gli estremi spostati lungo la propria normale, il raggio corre esattamente fra i due. Due cause di precisione emerse nel farlo, corrette insieme: campionamento conico delle sfere (luce sferica ed emissivi sferici) rifatto con la costruzione geometrica di PBRT-v4 e la forma per angoli piccoli (prima l'1-2% dei campioni mancava le sfere piccole o lontane, e sotto 1e-4 sr la pdf valeva 0 e spegneva la NEE delle sfere emissive); `Sphere.Hit` con discriminante robusto `r² - |l|²` e radici stabili (errore su t fino a 1.8e-2 a 200 unità, ora pochi ulp). Misura: `lights` da 0.975 a **1.0000**. Test `LightSelfShadowTests`, `SphereIntersectionPrecisionTests`. |
| 3.20 | ✅ **corretto** (2026-10-20), era **[BIAS]** di default grave | `Rendering/Renderer.cs` (`SuggestedMaxSampleRadiance`), `Program.cs` | Il clamp per-sample era una soglia **assoluta** di radianza (10) attiva di default: Cornell box con pannello di radianza 20 a **-22%** di energia. Ora è opzionale (`-C <n>`), e il clamp indiretto agisce solo insieme a `-C`. Misura: Cornell in modalità normale da 0.782 a **1.0000**. Test `FireflyClamp_IsOptIn_AndReferenceModeOverridesIt`. |
| 3.21 | ✅ **corretto** (2026-10-20), era **[BIAS]** di default grave | `Geometry/ShadowRay.cs`, `Rendering/Renderer.cs` (stato di catena, `SuppressChainEmission`, `LightSampleOwnsTransport`), `IMaterial.TransmitsStraight`, `Rendering/RandomWalkSss.cs` | Le ombre trasparenti "dritte" si sommavano ai cammini BSDF che attraversano davvero il vetro (doppio conteggio): l'ombra della sfera di vetro spariva. Ora per default i raggi d'ombra sono opachi al vetro che rifrange (ombra e caustica fisiche) e attraversano solo la trasmissione **rettilinea** (gusci IOR 1, fogli `thin_walled` lisci), che è esatta. `--transparent-shadows on` riporta l'approssimazione come opzione artistica coerente: i cammini "diffuso → attraverso il vetro → luce" (e cielo campionato dalla NEE) vengono scartati e il campione di luce che ha attraversato il vetro prende peso MIS 1, così ogni trasporto ha un solo stimatore. Il random walk SSS vede il bordo della propria entità come interfaccia nulla anche per la sua NEE (prima: Fresnel solo sulla NEE e non sull'uscita, ora coerenti). Misura: `glass-shadow` in modalità normale da 1.067 (relMSE 0.23) a **1.0001** (relMSE 4e-4); nuova scena `null-shell` **1.0001**. Con `--transparent-shadows on`: +8.6%, l'approssimazione toglie ombra e caustica per costruzione. |
| 3.22 | ✅ **corretto** (2026-10-21, Fase 5) per vetro ed emissivo, era **[NON-STANDARD]** | `Scene/SceneLoader.cs:1032` | Un materiale senza `color` riceve grigio 0.5 per **ogni** tipo, anche `dielectric` ed `emissive`: un vetro scritto senza colore trasmette il 25% attraverso una sfera, un emissivo emette metà dell'intensità scritta. Default fisici: vetro 1, emissivo 1. Fase 10. **Esito**: `dielectric` ed `emissive` senza colore ora sono bianchi (vetro trasparente, emissivo all'intensità scritta). Il vetro grigio toccava 13 scene del repo (§3.37), l'emissivo nessuna. Gli altri tipi tengono il grigio 0.5 come colore neutro di partenza. |
| 3.23 | **[NON-STANDARD]** | `Scene/SceneLoader.cs:131-132` | Il loader usa `IgnoreUnmatchedProperties`: una chiave YAML sbagliata (`refraction_idx`, `roughnes`) viene ignorata in silenzio e il materiale usa il default. Serve un errore o almeno un warning con il nome della chiave. Fase 10. |
| 3.24 | ✅ **corretto** (2026-10-20), era **[BUG]** | `Materials/Dielectric.cs` | `Dielectric.Scatter` sceglieva riflessione/rifrazione con l'approssimazione di Schlick, mentre la sua `ShadowTransmittance` usava il Fresnel esatto. Schlick dà `(1-cos)^5` anche con IOR 1: un guscio a indice adattato mostrava bordo e riflesso inesistenti, e il vetro sbagliava di qualche percento a incidenza radente. Ora Fresnel esatto: `glass-shadow` FLIP da 0.032 a 0.016. |
| 3.25 | ✅ **corretto** (2026-10-20), era **[BUG]** piccolo | `Lights/GeometryLight.cs` | `IlluminateAndTestStratified` non passava il tempo dell'otturatore ai campionatori: con il motion blur i raggi d'ombra degli emissivi testavano la scena all'istante 0. |
| 3.26 | ✅ **corretto** (2026-10-20, Fase 1), era **[BUG]** | `Core/MathUtils.cs` (`OrthonormalBasis`, `LocalToWorld`), copie private in `Sphere`, `SphereLight`, `PhysicalSun`, `DirectionalLight`, `Metal`, `Microfacet` | Le basi ortonormali usavano la forma di Frisvad, che perde l'ortogonalità vicino a -Z (errore 4.5e-2). Ora una sola implementazione, Duff et al. 2017 (errore 2.9e-7 ovunque). Test `OrthonormalBasis_IsOrthonormalEverywhere`. |
| 3.27 | ✅ **corretto** (2026-10-20, Fase 1), era **[BUG]** | `Core/MathUtils.cs` (`OffsetOrigin`, `RayEpsilon`), `Renderer`, `ShadowRay`, luci, `CausticPhotonTracer` | Offset dei raggi e `tMin` fissi a 1e-4: una scena di 10 µm usciva nera, una scena a 10 km dall'origine perdeva il 44% della luce per auto-intersezione. Ora l'offset è relativo alla coordinata più grande del punto (2^-15, circa 256 ulp, Wächter e Binder), con un minimo proporzionale al raggio della scena (2^-19); anche le guardie di distanza delle luci. Misure in §4 Fase 1. Test `ScaleInvarianceTests`. |
| 3.28 | ✅ **corretto** (2026-10-20, Fase 1), era **[BUG]** piccolo | `Core/AABB.cs` | Un raggio parallelo a un asse con l'origine esattamente sul piano di una faccia dà `0 * ∞ = NaN` nello slab test, e `MathF.Max` lo propagava: il box (e in un nodo BVH tutto il sottoalbero) veniva scartato. Ora `float.MaxNumber`/`MinNumber` ignorano il NaN (convenzione conservativa di PBRT), senza costo misurabile. Test `Hit_RayInAFacePlane_IsConservative`. |
| 3.29 | ✅ **corretto** (2026-10-20, Fase 1), era **[BUG]** | `Core/QuarticSolver.cs` (`SolveCubicMonic`, `RefineRoot`) | La cubica classificava il discriminante con una soglia **assoluta** 1e-12: la risolvente di Ferrari per un raggio che sfiora il tubo del toro (tre radici distinte vicine a 1e-3) veniva presa per una radice doppia e due radici vicine della quartica tornavano fuse (coppia distante 1e-2 restituita distante 5e-4; sotto 1e-4 una radice persa nell'1-3% dei casi). Il raffinamento di Newton, partendo fra le due radici, saltava fino a 2e-2. Ora soglia relativa al cubo dei coefficienti e Newton con guardia sul residuo: coppie fino a 1e-5 sempre trovate, errore entro il condizionamento; radici distinte da 4e-8 a 1.5e-10. Vale anche per il lathe (`SolveCubic`). Test `Quartic_CloseRoots_AreBothFound`. |
| 3.30 | ✅ **corretto** (2026-10-21, Fase 4), era **[NON-STANDARD]** | `Core/Perlin.cs:109-111` | Interpolazione con il fade cubico `3t² - 2t³`: il valore e il gradiente sono continui ma la curvatura salta di 12.7 attraverso le facce del reticolo (visibile come pieghe sulla griglia nei bump map di rumore, che derivano due volte). Gradienti normalizzati sul cubo: anisotropia del 5.7% fra assi e diagonali. Improved Perlin (fade quintico, 12 gradienti) cambia l'aspetto di ogni texture procedurale: Fase 4, con un'opzione per l'aspetto attuale. Anche `MultiScaleRidgedField` e `DomainWarp` (costruiti su Perlin) vanno verificati lì. **Esito**: `PerlinBasis.Improved` (Perlin 2002: fade quintico, 16 gradienti sugli spigoli del cubo) è la base di default di noise, marble, wood, warp del voronoi e brick, riscalata a 0.67 per avere la deviazione standard della base classica (range e color ramp restano tarati); `noise_basis: classic` riproduce esattamente il disegno di prima. Salto di curvatura sulle facce del reticolo: da oltre 5 volte a circa 1 volta quello dentro la cella; anisotropia fra asse e diagonale sotto il 2%. Gli strata dell'heightfield restano sulla base classica (spostano solo bande di colore). `MultiScaleRidgedField` e `DomainWarp` verificati: usano `Noise`/`Ridged` senza assunzioni sulla base. Test `PerlinBasisTests`. |
| 3.31 | ✅ **corretto** (2026-10-21, Fase 4), era **[NON-STANDARD]** piccolo | `Rendering/Renderer.cs:700` | I differenziali di raggio sono ridotti di `1/√spp` senza minimo: ad alti spp il filtro delle texture scende sotto il pixel (PBRT-v4 usa `max(1/8, 1/√spp)`), con aliasing e accessi di cache peggiori. Il footprint dei raggi di camera è verificato (§4 Fase 1); la propagazione dopo riflessione e rifrazione non è ancora testata. Fase 4. **Esito**: footprint `max(1/8, 1/√spp)` del pixel (`Renderer.DifferentialFloor`). La propagazione dopo riflessione e rifrazione non esiste per scelta: solo il primo hit del raggio di camera è filtrato, i rimbalzi sono campionati in un punto e l'antialias lo fa la media dei campioni (non distorto). Test `DifferentialFloorTests` (1, 16 e 1024 spp; nessun lookup filtrato oltre il primo hit). |
| 3.32 | ✅ **corretto** (2026-10-21, Fase 5), era **[DEBITO]** | `Core/MathUtils.cs` (`Refract`) | Con la riflessione totale interna `Refract` prendeva `Abs(1 - sin²)` e restituiva una direzione senza significato. Ora restituisce il vettore nullo e i chiamanti ripiegano sulla riflessione (vicino all'angolo critico l'arrotondamento poteva sfuggire al controllo preventivo). |
| 3.33 | **[BUG]** | `Denoising/` (NFOR) | Attorno a luci piccole e molto luminose sfocate (bokeh) il denoiser disegna un **alone scuro** più scuro dello sfondo: la regressione al primo ordine sbanda su valori HDR estremi. Senza denoiser l'alone non c'è (verificato con `scenes/showcases/camera-physical.yaml`). Colpisce i preset `draft*`, `standard*`, `pre-final*`. Fase 9. **Mitigato nella Fase 6**: il sole a piena luminosità in uno specchio diventava un quadrato bianco con il bordo scuro; ora le luci estreme (oltre 32 volte il 99° percentile) sono tolte dal filtro e riaggiunte intatte, e un candidato che scende sotto zero perde la selezione (test `DenoiserHighlightTests`). Resta una cucitura sottile e leggermente tinta lungo i bordi illuminati dal sole su sfondo scuro (`showcases/extrusion.yaml`): la soluzione vera è un denoiser appreso (OIDN, Fase 9). |
| 3.34 | ✅ **corretto** (2026-10-22, Fase 6), era **[NON-STANDARD]** rimandato | camera | Esposizione fisica (ISO, tempo di posa, EV100) non implementata: senza unità fotometriche per le luci (3.14) un'esposizione in EV100 non ha un riferimento. Va fatta con le unità nella Fase 6. **Esito**: `ev100`, oppure `iso` + `shutter_speed` + `f_stop`; esposizione `-EV100 - log2(1.2)` (convenzione basata sulla saturazione). Regola del "sunny 16" verificata: con un cielo fotometrico e EV100 15 un grigio 18% al sole viene grigio medio (`scenes/review/daylight-units.yaml`). |
| 3.35 | ✅ **corretto** (2026-10-21, Fase 5), era **[BUG]** | `Materials/Dielectric.cs`, `DisneyBsdf.cs` (trasmissione), `Rendering/CausticPhotonTracer.cs` | Alla rifrazione mancava il fattore `(η_i/η_t)²` della radianza (si conserva `L/n²`, Veach 1997 §5.2). Si compensa per un oggetto attraversato avanti e indietro, quindi non si vedeva, ma una camera dentro l'acqua o il vetro vedeva il fuori `n²` volte troppo scuro e una luce dentro il vetro usciva `n²` volte troppo forte. **Misurato**: camera al centro di una sfera di vetro n 1.33 in ambiente uniforme, 1.00 invece di 1.769 (Mitsuba 1.7689). Ora 1.769 per `dielectric` e per il vetro Disney; i fotoni, che portano potenza, tolgono il fattore. Test `InsideClearGlass_RadianceIsScaledByEtaSquared`. |
| 3.36 | ✅ **corretto** (2026-10-21, Fase 5), era **[BUG]** | `Rendering/Renderer.cs` (`LightSideTerminator`, `DirectLightingAt`), `Lights/*`, `ShadowRay` | La NEE non raggiungeva mai una luce dietro una superficie che trasmette (vetro satinato, `diff_trans` del Disney): il fattore di shadow terminator valeva 0 sotto l'orizzonte geometrico, alcune luci scartavano quei campioni e l'origine del raggio d'ombra stava sempre dal lato della camera. Il MIS dava comunque al campionamento BSDF solo la sua quota, quindi luce persa. Ora `IMaterial.HasTransmission`, terminator dal lato della luce, origine dal lato della luce (`MathUtils.OffsetToward`). Misura: `glass-rough` (vetro satinato contro il `roughdielectric` di Mitsuba) da 0.948 a **1.0013**. |
| 3.37 | ✅ **corretto** (2026-10-21, Fase 5), era **[BUG]** | `Scene/SceneLoader.cs` (`dielectric`) | Un `dielectric` senza `color` riceveva il default condiviso grigio 0.5: il vetro dimezzava la luce a ogni attraversamento (una sfera trasmetteva il 25%). Riguardava 13 vetri delle scene del repo (`cornell-box`, `sample`, `castello-sforzesco`, `pendolo-newton`, showcase), tutti pensati trasparenti. Ora il default è bianco. Fornace bianca di riferimento: `scenes/review/white-furnace-classic.yaml`. |
| 3.38 | ✅ **fatto** (2026-10-21, Fase 5) | `Materials/OpenPbr.cs`, `Eon.cs`, `FuzzLtc.cs`, `DielectricAlbedo.cs`, `Scene/MaterialParam.cs` | Nuovo materiale `openpbr` (OpenPBR Surface v1.1, decisione del proprietario del 2026-10-21: OpenPBR, con il `disney` tradotto su di esso). Riduzione a miscela di lobi della specifica: metallo F82 con Kulla-Conty, interfaccia dielettrica (GGX + Walter verso vetro o mezzo SSS), diffuso EON scalato da 1 − E_R (tabella E_R(μ, α, η) integrata offline, `validation/gen_dielectric_albedo.py`), coat con assorbimento, darkening e roughening, fuzz di Zeltner 2022, film sottile, emissione, opacità, thin-walled. Ogni parametro YAML accetta numero, colore o texture. Verifiche: fornace bianca in tutte le configurazioni della specifica (test del BSDF e render completo `scenes/review/openpbr-white-furnace.yaml`, 0.997-1.000), chi-quadro per lobo, riduzioni esatte a `metal` e al vetro ruvido. I lobi anisotropi leggevano le tabelle isotrope alla rugosità proiettata (metallo entro l'1% ad anisotropia 0.5, +6.6% a 0.72): sostituito da una tabella anisotropa, §3.39. |
| 3.39 | ✅ **fatto** (2026-10-21, Fase 5) | `Materials/DisneyMaterial.cs`, `OpenPbr.cs`, `AnisotropicAlbedo.cs` | Il tipo `disney` è tradotto su OpenPBR (`DisneyMaterial.Create`), `DisneyBsdf.cs` rimosso (con `EnergyCompensationLut` e `SheenCharlie`, ormai inutilizzati). Mappatura: `specular` → `specular_weight` = 2 × specular (Disney scala F0 di 2 × specular), `specular_tint`/`sheen_tint` → colore verso la tinta di `color`, `sheen` → fuzz, `clearcoat`/`clearcoat_gloss` → coat (rugosità √α con α = lerp(0.1, 0.001, gloss)), `spec_trans` → trasmissione (o subsurface con mezzo incorporato), `diff_trans` → subsurface thin-walled con g = 2 × diff_trans − 1, `anisotropic` → 0.9 a, film in nm → µm. Differenze di aspetto documentate nel riferimento delle scene. Nel materiale OpenPBR, lungo la strada: diffuso sotto lo speculare con il peso reciproco (1 − E(μV))(1 − E(μL))/(1 − Ē) (esatto per una base lambertiana, dove la forma della specifica non è reciproca); lobi anisotropi con una tabella offline dell'albedo GGX anisotropa (`validation/gen_aniso_albedo.py`), reciproca ed esatta (la rugosità proiettata creava il 6.6% di energia ad anisotropia 0.72); lobo speculare delta sotto roughness 0.03; interfaccia a IOR 1 attraversata dritta dai due lati. Coat e fuzz restano con lo scaling della sola direzione di vista della specifica: conserva l'energia su ogni base, non è reciproco (i test controllano l'energia). |
| 3.40 | ✅ **corretto** (2026-10-21, Fase 4), era **[BUG]** | `Textures/ImageTexture.cs` (mipmap) | La piramide dimezzava le dimensioni per difetto: una dimensione dispari perdeva l'ultima riga o colonna a ogni livello (1000 → 500 → 250 → 125 → 62), e il livello 1×1 non era la media dell'immagine. Ora ogni livello arrotonda per eccesso e ogni texel media esattamente l'area che copre nel livello sotto (pesi frazionari). Test `MipPyramid_KeepsTheMean_ForAnySize`. |
| 3.41 | ✅ **corretto** (2026-10-21, Fase 4), era **[BUG]** | `Textures/ImageTexture.cs` (LOD) | Il livello di mipmap era scelto misurando il footprint in UV per il lato lungo dell'immagine: su un'immagine non quadrata l'asse corto contava i texel del lato lungo e sfocava troppo (4×256: un quarto di texel letto come 64 texel). Ora il footprint è misurato in texel su ciascun asse. Test `NonSquareImage_PicksItsLevelInTexels`. |
| 3.42 | ✅ **corretto** (2026-10-21, Fase 4), era **[BUG]** | `Textures/HdrLoader.cs` | Il loader Radiance ignorava l'orientamento della riga di risoluzione: un file `+Y` (righe dal basso) o `-X` usciva capovolto o specchiato. La decodifica RGBE usava `mantissa · 2^(e−136)` senza il mezzo bit di Radiance (`(m + 0.5) · 2^(e−136)`): i writer troncano la mantissa, così ogni HDRI era 0.2-0.8% troppo scuro. Test `RadianceHdr_*`. |
| 3.43 | ✅ **corretto** (2026-10-21, Fase 4), era **[LIMITE]** | `Textures/ExrLoader.cs` | Un EXR in scala di grigi (solo canale `Y`, il formato tipico di una mappa dati) era rifiutato; la compressione RLE non era letta; come texture i valori negativi erano tagliati anche per una mappa dati (un displacement con segno). Ora `Y` riempie i tre canali, RLE è decodificato, una mappa dati tiene il segno (un colore resta ≥ 0). PIZ, DWAA/DWAB, B44, PXR24 e i file a tile restano non supportati con errore esplicito (PLANNING). |
| 3.44 | ✅ **corretto** (2026-10-21, Fase 4), era **[BUG]** | `Textures/EnvironmentMap.cs` (`SampleDirection`, `PdfDirection`) | Il campionamento d'importanza dell'HDRI costruiva la direzione con `z = −cos θ cos φ` mentre la lettura (`Sample`, quello che vede la camera) usa `φ = atan2(x, z)`: ogni direzione campionata leggeva il texel specchiato attorno a φ = π/2. Non distorto (la pdf era quella del campionatore) ma mirato lontano dai texel luminosi: con un sole che porta il 90% dell'energia, 0 campioni su 4000 lo vedevano (ora oltre l'80%). La lettura era anche sfasata di un texel rispetto alle celle della CDF (`u × (W−1)`): ora è centrata sul texel. Test `EnvironmentSunTests.SampledDirections_LookUpTheTexelSampled`. |
| 3.45 | ✅ **corretto** (2026-10-21, Fase 4), era **[BUG]** | `Textures/HdriSunExtractor.cs` | Il sole estratto dall'HDRI stava all'azimut specchiato (stessa convenzione di 3.44): ombre incoerenti con lo sfondo. L'energia data al sole era la somma dei soli texel sopra soglia, quella tolta dall'in-paint un'altra: l'alone sotto soglia andava perso e lo sfondo sotto il disco era contato due volte. Il centroide era una media di angoli (un sole sulla cucitura ±180° finiva dall'altra parte del cielo), le distanze ignoravano cos(latitudine), l'in-paint si fermava a un raggio invece di due. Ora: seme al texel più luminoso, centro come media vettoriale, distanze angolari sulla sfera, e il sole riceve esattamente l'energia tolta (mappa più sole integrano alla mappa originale entro lo 0.1%). Test `EnvironmentSunTests.SunExtraction_ConservesEnergy_AndFindsTheSun`. |
| 3.46 | ✅ **corretto** (2026-10-21, Fase 4), era **[BUG]** | `Textures/BumpMapTexture.cs`, `Rendering/Renderer.cs` (`ApplyBumpMap`) | Il bump di una texture 3D spostava il punto locale (`LocalPoint`, senza rotazione) lungo la tangente in coordinate mondo: su un oggetto ruotato la pendenza era presa lungo l'asse sbagliato (a 90° nulla). L'altezza di un bump da immagine era per unità di UV, quindi dipendeva da quanto è grande la superficie. Ora l'altezza è `strength × luminanza` in unità di scena per ogni tipo di texture: un passo sulla superficie muove (u, v) con l'inversa di ∂P/∂u, ∂P/∂v e il punto 3D con l'inversa della rotazione dei transform (`HitRecord.LocalToWorld`, `BumpFrame`). I bump procedurali delle scene del repo (32 su 33) non cambiano; cambia solo il bump da immagine. Test `NormalAndBumpFrameTests`. |
| 3.47 | ✅ **corretto** (2026-10-21, Fase 4), era **[BUG]** | `Geometry/*` (∂P/∂u), `Geometry/Box.cs`, `MeshTangents.cs`, `Textures/NormalMapTexture.cs` | Dieci primitive (box, cilindro, cono, tronco, disco, anello, capsula, toro, piano, lathe) non riportavano ∂P/∂u, ∂P/∂v: il filtro delle immagini usava la tangente unitaria (sfocatura sbagliata) e il bump non aveva la scala. Ora tutte, verificate numericamente (`PrimitiveUvDerivativeTests`). Il box capovolgeva la bitangente su tre facce (BUG-09) per avere un frame destrorso: il canale verde delle normal map era invertito lì; ora la bitangente segue +V ovunque, come MikkTSpace sulle isole UV specchiate. Capsula, cono e lathe avevano tangente o bitangente contro ∂P/∂u o ∂P/∂v. Le mesh avevano una tangente costante per faccia (cuciture con le normal map cotte): ora tangenti per vertice MikkTSpace. `NormalMapTexture` leggeva a 8 bit con mezzo texel di sfasamento e clamp ai bordi: ora passa per `ImageTexture` (16 bit, EXR, centrata, wrap). |
| 3.48 | ✅ **corretto** (2026-10-21, Fase 4), era **[BUG]** | `Core/Perlin.cs` (`Fbm`, `Ridged`, `Billow`), `Textures/NoiseTexture.cs` | Quando il footprint toglieva le ottave fini, fBm, ridged e billow normalizzavano per l'ampiezza delle sole ottave tenute (e HeteroTerrain/Hybrid per la loro varianza): il rumore lontano guadagnava contrasto invece di sfumare alla media (una ottava su otto: ampiezza doppia). Ora un'ottava filtrata conta per la sua media (0 per fBm e multifrattali, E[\|n\|] per billow, E[(1−\|n\|)²] per ridged) e la normalizzazione copre tutte le ottave. Test `FilteredOctaves_FadeToTheMean_WithoutGainingContrast`. |
| 3.49 | ✅ **corretto** (2026-10-22, Fase 6), era **[BUG]** | `Rendering/Renderer.cs` (MIS), ora `Rendering/LightMisTable.cs` | Con una sola luce scelta per vertice (`--light-sampling power\|uniform`, i preset FINAL-FAST) il peso MIS dell'emissione colpita dalla BSDF ometteva la probabilità di scelta: luce diretta al 76-89% del valore giusto. Ora la probabilità entra in entrambi i lati (test `OneLightPicked_IsUnbiased`). |
| 3.50 | ✅ **corretto** (2026-10-22, Fase 6), era **[BUG]** | `Lights/PortalLight.cs`, MIS | Environment e portali campionano lo stesso cielo ma erano pesati come se fossero soli: un portale aggiungeva il 7-19% sopra l'environment. Ora formano un gruppo MIS (densità sommate). |
| 3.51 | ✅ **corretto** (2026-10-22, Fase 6), era **[BUG]** | MIS di `DirectionalLight` (disco) e `PhysicalSun` | Il disco di una luce direzionale con `angular_radius`, che nessun raggio BSDF può raggiungere, teneva un peso MIS minore di uno e perdeva luce; il sole analitico non era raccolto dai raggi BSDF. Ora il primo pesa 1, il secondo è raggiungibile e pesato (`PhysicalSun.RadianceToward`). |
| 3.52 | ✅ **corretto** (2026-10-22, Fase 6), era **[BUG]** | tutte le luci (stratificazione), `Box`, `Mesh` | Griglia di stratificazione ⌈√n⌉²: con 8 campioni d'ombra (il default dei portali) un nono della luce non era mai campionato (+0.7-1.1%); il box sceglieva le facce a turno, la mesh non arrivava mai in fondo alla sua CDF. `MathUtils.Stratum2D` divide in esattamente n celle. |
| 3.53 | ✅ **corretto** (2026-10-22, Fase 6), era **[BUG]** | MIS degli emissivi | La luce di una superficie emissiva colpita era trovata dal materiale: più superfici con lo stesso materiale emissivo, o un `mix` con una parte emissiva, prendevano la pdf della prima. Ora dalla superficie colpita. |
| 3.54 | ✅ **corretto** (2026-10-22, Fase 6), era **[BUG]** piccolo | `Lights/SphereLight.cs` (`ApproximatePower`) | Ignorava il raggio: la scelta proporzionale alla potenza sbagliava il peso delle sphere light. `LightPowerTests` confronta la potenza dichiarata di ogni luce finita con il flusso misurato. |
| 3.55 | ✅ **fatto** (2026-10-22, Fase 6), era **[NON-STANDARD]** | `Lights/AreaLight.cs`, `PortalLight.cs`, `Geometry/Quad.cs` | Luci rettangolari campionate per area: il termine cos/d² esplode vicino alla luce. Ora angolo solido sferico (Ureña 2013): deviazione standard relativa a 0.3 / 1 / 4 lati dalla luce da 27% / 6.1% / 0.71% a 4.8% / 1.7% / 0.19%. Pdf di area e portale da una faccia sola come i campioni. |
| 3.56 | ✅ **corretto** (2026-10-22, Fase 6), era **[BIAS]** | `Rendering/Renderer.cs` (`SampleSky`, LOD mip dell'HDRI) | Sui rimbalzi BSDF il cielo HDRI era letto pre-filtrato (mip sfocato) anche sotto MIS: la luce di una zona luminosa finiva in direzioni pesate per la BSDF e contava due volte, +26% su un pavimento diffuso (da solo ne perdeva un terzo). Ora il mip si usa solo per un cielo che nessuna luce campiona, mai in riferimento. In più la compensazione MIS di Karlík 2019 sulla CDF dell'HDRI. |
| 3.57 | ✅ **corretto** (2026-10-22, Fase 6), era **[BUG]** grave | `Rendering/Sky/*` (sole di Preetham, "Hosek-Wilkie", Nishita), `PhysicalSun` | Ogni cielo fisico dava al disco del sole come radianza il valore che usava come irradianza dello scattering: il sole era circa **15 000 volte** troppo debole rispetto al suo cielo, le ombre quasi assenti. Ora il disco vale l'illuminanza al limite dell'atmosfera (133.8 klux) per la trasmittanza lungo il percorso, diviso l'angolo solido (`Atmosphere`, `AnalyticSky`). A mezzogiorno sereno 75-110 klux a terra, il sole 3-5 volte il cielo, il disco 1.35×10⁹ nit. L'oscuramento al bordo usava l'angolo nel cielo (nessun effetto su 0.265°): ora sulla superficie del sole, normalizzato. Test `SkyModelTests`. |
| 3.58 | ✅ **fatto** (2026-10-22, Fase 6), era **[NON-STANDARD]** | `Rendering/Sky/HosekWilkieSky.cs` | `hosek_wilkie` era un alias di Preetham (il README prometteva Hosek-Wilkie). Ora il modello RGB vero dalle tabelle degli autori (`validation/gen_hosek_wilkie_data.py`), verificato su 24 punti contro il loro codice C compilato (scarto sotto 1e-4). |
| 3.59 | **[BUG]**, da Fase 8 | `Volumetrics/` (height fog, atmosfera di Nishita) | NaN/Inf azzerati nei render con un mezzo: `showcases/ground-alpine-lake.yaml` 31 627 a draft-tiny (1.5% dei campioni, già al primo vertice, spariscono togliendo l'`height_fog`), `showcases/sky-aerial.yaml` 688. Presenti anche prima della Fase 6. Fase 8. |
| 3.60 | **[BUG]**, da Fase 8 | `Volumetrics/NishitaAtmosphereMedium.cs` | In `showcases/sky-aerial.yaml` una banda di firefly nel cielo verso il sole, con netta separazione a un cerchio massimo, già a profondità 1 e con entrambi i sampler: la NEE del mezzo verso il sole. Invisibile prima della Fase 6 (sole 15 000 volte più debole). Sospetti: il rapporto cromatico `exp(τ̄ - τc)` senza limite e il tetto di 256 passi del delta tracking. Fase 8. |
| 3.61 | ✅ **corretto** (2026-10-24, Fase 7), era **[BUG]** | `Rendering/Renderer.cs` (`ClassifyScatteredRay`, `SampleSky`, SSS e mezzi) | Categoria del raggio per i flag di visibilità sbagliata in cinque casi: i raggi usciti dal SSS e quelli diffusi da un mezzo restavano "camera" e saltavano le superfici `visible_to_camera: false` (marmo SSS senza la luce dei pannelli nascosti, -0.36%); un rimbalzo glossy non delta era "diffuse"; il cielo visto in uno specchio era letto come "camera" (lo sfondo riservato alla camera e i suoi flag comparivano nei riflessi); un raggio che attraversa un cut-out diventava "transmission" (dietro una foglia la camera vedeva ciò che le è nascosto). Ora la categoria viene dal lobo campionato (`BsdfLobe`) e un cut-out la lascia com'è. |

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

- [x] Attributo `color_space` per texture (`srgb`, `linear`, `raw`) e default automatico per
      ruolo (colore = sRGB, roughness/metallic/normal/bump/displacement = raw). Corregge 3.3.
- [x] Curva sRGB esatta (3.4). Loader `.hdr`/`.exr`: canali, esponente RGBE, half float (3.42, 3.43).
- [x] Filtraggio: mipmap e anisotropico (fino a 8 campioni trilineari) verificati e corretti
      (3.40, 3.41); differenziali con minimo 1/8 (3.31). Un EWA vero non serve: a parità di
      footprint il filtro a campioni trilineari è quello delle GPU e dei renderer di produzione.
      UDIM e texture a tile/cache: funzionalità, non correttezza, rimandate in PLANNING.
- [x] Normal map: convenzione tangenti **MikkTSpace** (lo standard di tutti i baker: senza, le
      normal map importate mostrano cuciture), OpenGL/DirectX, `strength` (3.47).
- [x] Bump: scala in unità mondo, coerenza con il displacement (test esistenti da rivedere) (3.46).
- [x] Procedurali (marble, wood, voronoi, noise, brick, gradient, color ramp): range, antialias
      (clamp delle ottave vs footprint), coerenza tra spazio oggetto e mondo (3.30, 3.48).
- [x] `HdriSunExtractor`: conservazione dell'energia quando il sole viene tolto dall'HDRI (3.44, 3.45).

#### Esito Fase 4 (2026-10-21)

Suite: 951 test verdi, 0 skip (era 893 alla Fase 5).

**Dati e immagini.** `color_space` per texture con default per ruolo (3.3): una sola scena del repo
cambia (il bump di cemento di `showcases/texture-bump-map.yaml`). Immagini lette a 16 bit, EXR in
scala di grigi e RLE, mappe dati EXR con segno (3.43), Radiance con orientamento e mezzo bit di
mantissa (3.42). Piramide mipmap che conserva la media (3.40), livello scelto in texel (3.41),
differenziali con minimo 1/8 del pixel (3.31). Solo il primo hit del raggio di camera è filtrato,
per scelta: i rimbalzi sono campionati in un punto e mediati dai campioni (non distorto).

**Frame e altezze.** Ogni primitiva riporta ∂P/∂u, ∂P/∂v; tangente lungo +U e bitangente lungo +V
ovunque (3.47); mesh con tangenti MikkTSpace. Bump in unità di scena per ogni tipo di texture e
ogni transform (3.46). Scena di verifica `scenes/review/texture-frames.yaml`
(`images/texture-frames.jpg`): tre cubi ruotati con la stessa normal map, tre sfere col bump
ferma, ruotata, scalata; la prima e la terza sfera escono identiche.

**Rumore.** Improved Perlin di default con la stessa deviazione standard della base classica
(3.30), `noise_basis: classic` per i disegni di prima. Le ottave tolte dall'antialias contano
per la loro media (3.48).

**HDRI.** Il campionamento d'importanza mirava al texel specchiato (3.44): non distorto ma con
molto più rumore del necessario sulle HDRI con il sole; il sole estratto stava all'azimut
specchiato e non conservava l'energia (3.45). Tocca la Fase 6 (luci): i render con HDRI e sole
estratto cambiano direzione delle ombre e rumore.

**Rimandato** (PLANNING): marble e wood senza clamp delle ottave sul footprint (campionati in un
punto, documentato); compressioni EXR PIZ, DWAA/DWAB, B44, PXR24 e file a tile; UDIM e cache di
texture; l'autobump di una mesh scalata usa l'altezza in unità oggetto mentre il bump è in unità di
scena; sui gradini piatti del lathe V va dal raggio interno all'esterno qualunque sia il verso del
profilo.

**Verdetto**: texture e dati corretti; i cambi d'aspetto attesi (bump da immagine, disegno del
rumore, HDRI con sole) si rivedono con le altre scene alla fine della review.

### Fase 5 - Materiali e BSDF (`Materials/`): il cuore del realismo

- [x] **Lambertian**: furnace = albedo, chi-quadro del coseno.
- [x] **Dielectric liscio**: Fresnel esatto, TIR senza consumare numeri casuali, IOR relativo con
      `IorStack`, fattore `(η_i/η_t)²` della radianza (3.35), Beer-Lambert.
- [x] **Microfacet core** (`Microfacet.cs`): GGX normalizzata, Smith G2 correlato in altezza,
      campionamento VNDF (Heitz 2018), anisotropia alla OpenPBR.
- [x] **Conduttore**: Fresnel complesso con IOR `n + ik` (15 preset), F82, lobo lambertiano tolto
      (3.7), Kulla-Conty con tabella offline.
- [x] **Dielectric rough** (Walter 2007): BTDF, pdf, reciprocità generalizzata, MIS, NEE attraverso
      la superficie (3.6, 3.36).
- [x] **Disney/Principled**: decisione OpenPBR presa (sotto); il tipo `disney` è tradotto su OpenPBR
      e i suoi bug 3.2, 3.6, 3.17, 3.18 sono chiusi con la traduzione (3.39).
- [x] **MixMaterial**: `Evaluate`/`Pdf` miscela pesata, `Sample` sceglie un figlio con probabilità t,
      riporta i suoi lobi delta invariati e per quelli continui la miscela intera (coerente).
- [x] **Emissive**: unilaterale (solo faccia anteriore), nessuna BSDF; unità fisiche rimandate alla
      Fase 6 con le luci.
- [x] **Shadow terminator** (3.11): fattore lato luce in entrambe le strategie MIS, fattore lato
      vista tolto. La correzione dell'aggiunto per i path dalla luce non serve: il photon tracer
      segue solo lobi delta, dove la normale di shading non entra.

**Decisione strategica, presa il 2026-10-21 dal proprietario**: OpenPBR Surface come materiale a
strati (`openpbr`), con il tipo `disney` tradotto su di esso. `lambertian`, `metal` e `dielectric`
restano classi proprie: sono già validate contro Mitsuba e coincidono esattamente con i casi
limite di OpenPBR (test di riduzione). API unificata su `Sample`/`Evaluate`/`Pdf` (3.12).

#### Esito Fase 5 (2026-10-21)

Suite: 893 test verdi, 0 skip (era 776 + 4 skip alla Fase 3). I tre bug Disney che erano test
disattivati (3.2, 3.17, 3.18) ora passano.

**Materiali classici rifatti e validati.** Metallo (Fresnel complesso o F82, Kulla-Conty), vetro
liscio e satinato (Walter 2007), fattore η² della radianza, NEE attraverso le superfici che
trasmettono, default trasparente del `dielectric` (3.37, il grigio 0.5 tingeva 13 vetri del repo).

**OpenPBR** (3.38): materiale a strati completo, ogni parametro numero, colore o texture. Mattoni
verificati contro i loro integrali (EON, fuzz LTC, tabelle di albedo isotropa, anisotropa e del
dielettrico generate offline in `validation/gen_*.py`). Fornace bianca in tutte le configurazioni
della specifica: test del BSDF e render completo (`scenes/review/openpbr-white-furnace.yaml`,
0.997-1.000; il vetro ruvido 0.984 è la perdita a singolo scattering dell'interfaccia, identica
al `dielectric`). Riduzioni esatte al `metal` e al vetro ruvido. Scelte rispetto alla specifica:
diffuso sotto lo speculare con il peso reciproco; coat e fuzz con lo scaling della sola vista
della specifica (conserva l'energia su ogni base, non reciproco); lobo delta sotto roughness 0.03.

**Disney tradotto su OpenPBR** (3.39). Confronto visivo prima/dopo in
`scenes/review/images/disney-to-openpbr.jpg`: spariscono i firefly della plastica liscia (3.2) e
l'energia in più di sheen e clearcoat (3.17).

**Validazione contro Mitsuba** (riferimento | default):

| Scena | Fase 3 | Fase 5 |
|---|---|---|
| cornell-lambert d32 | 0.9998 / 0.9999 | 0.9998 / 0.9999 |
| lights | 0.9997 | 0.9997 |
| glass-shadow | 0.9997 / 0.9995 | 0.9995 / 0.9995 |
| null-shell | 1.0001 | 1.0001 |
| metal-rough | 1.378 | **1.0032** (con il G separabile di Mitsuba 1.0000) |
| metal-presets (nuova) | | **1.0001** |
| glass-rough (nuova) | 0.948 | **1.0013** / 1.0005 |
| furnace-disney-glass | 1.0332 | **1.0000** |
| disney-spheres | 1.0239 | 0.9771 (differenza di modello: il principled di Mitsuba somma sheen e clearcoat ×0.25 sopra la base, OpenPBR li stratifica) |

**Pulizia dell'API** (3.12): tolti `Scatter` ed `EvaluateDirect`; la NEE usa `Evaluate`, la stessa
funzione che il campionamento stima. Render identici alla quarta cifra.

**Scene di riferimento conservate** (richiesta del proprietario): `scenes/review/` con un README che
dice per ciascuna cosa verifica e cosa aspettarsi.

**Verdetto**: i materiali sono fisicamente corretti e verificati uno per uno; il `disney` non crea
più energia. Cambia l'aspetto delle scene con sheen, clearcoat o vetri senza `color`
(documentato nel riferimento delle scene). Restano per altre fasi: unità fisiche dell'emissione
(Fase 6), Fresnel all'uscita del random walk SSS (Fase 8), dispersione (spettrale, Fase 10).

### Fase 6 - Luci e illuminazione diretta (`Lights/`, `Rendering/Sky/`)

- [x] Per ogni luce (point, spot, directional, area, sphere, geometry, portal, environment,
      physical sun): radianza, pdf in angolo solido, conversione area → angolo solido,
      `ApproximatePower` coerente con la potenza reale (serve alla scelta proporzionale).
      Fatto con `LightTransportTests` (render contro la quadratura) e `LightPowerTests`;
      trovati e corretti 3.49-3.54.
- [x] Area light rettangolari: campionamento sferico del rettangolo (Ureña 2013) invece che
      per area: molto meno rumore vicino alla luce (3.55).
- [x] Spot: profili **IES** (estensione). Il falloff del cono resta la smoothstep artistica fra
      `inner_angle` e `outer_angle`; la distribuzione fisica di un apparecchio la dà l'IES.
- [x] **Unità fisiche** (3.14): `power` in W o lm, `intensity` in cd, `luminance` in nit, sole in
      lux; `temperature` in Kelvin (corpo nero); flag `normalize` (potenza indipendente
      dall'area, come Arnold/Karma); esposizione per luce in stop. Più l'esposizione della
      camera in EV100 (3.34).
- [x] Many-light sampling: **light BVH** (Conty-Kulla 2018) per scene con molte luci/emissivi.
- [x] HDRI: campionamento continuo nel texel (3.9, Fase 1), rotazione (`orientation`, esistente),
      MIS compensation (Karlík 2019). Il mip pre-filtrato sotto MIS era distorto (3.56).
- [x] **Cielo**: Preetham corretto (3.1) e **Hosek-Wilkie** vero (3.58); Nishita con ozono e
      atmosfera condivisa; sole coerente con il cielo in tutti i modelli (3.57), unità relative
      o fotometriche. Prague 2021 non fatto: Hosek-Wilkie basta per il cielo sereno diurno, il
      crepuscolo lo copre Nishita.
- [x] Ombre trasparenti (3.8, 3.21): `--transparent-shadows`, spente per default e in riferimento
      (2026-10-20). Da valutare un controllo per materiale nella Fase 10.

#### Esito Fase 6 (2026-10-22)

Suite: 1065 test verdi, 0 skip (era 951 alla Fase 4).

**Ogni luce verificata contro il suo integrale.** `LightTransportTests` renderizza un pavimento
illuminato da ogni tipo di luce a profondità 2 (NEE più un rimbalzo BSDF, combinati dal MIS) e lo
confronta con la quadratura di ∫ L f cos sulla luce, su pavimento diffuso, lucido e quasi speculare,
con ogni strategia di scelta. Ha trovato sei errori, tutti corretti (3.49-3.54): il più grave la
probabilità di scelta assente dal lato BSDF del MIS (luce diretta al 76-89% con `power`/`uniform`),
poi portali che si sommavano all'environment (+7-19%), dischi irraggiungibili pesati come
raggiungibili, griglie di stratificazione con celle vuote (+0.7-1.1% a 8 campioni). La tabella delle
densità MIS è ora un oggetto solo (`LightMisTable`).

**Campionamento.** Rettangoli per angolo solido (3.55: rumore da 27% a 4.8% a 0.3 lati dalla luce).
Light BVH: con 90 luci a griglia media identica a `all` (0.8372 contro 0.8345), rumore per pixel 4.3
volte minore di `uniform`; `auto` lo attiva oltre 16 luci. HDRI con la compensazione di Karlík e
senza il mip pre-filtrato sotto MIS (3.56, era +26%).

**Unità fisiche** (3.14, 3.34): watt, lumen, candele, nit, lux, kelvin, IES; esposizione EV100.
`scenes/review/light-units.yaml`: quattro luci in quattro unità sopra un pavimento grigio,
luminanza misurata uguale al calcolo a mano entro lo 0.1% (2.596, 8.868, 17.89, 19.80 nit).

**Cieli** (3.1, 3.57, 3.58). Hosek-Wilkie vero, verificato contro il C degli autori; Preetham con la
Perez corretta; atmosfera comune con ozono. Il sole era 15 000 volte troppo debole rispetto al cielo
in ogni modello: ora viene dalla stessa luce. Rapporto sole/cielo sul piano orizzontale (torbidità
3): Hosek-Wilkie 0.83 / 2.2 / 4.1 con il sole a 15° / 30° / 60°, Preetham 0.56 / 1.4 / 2.5 (cielo più
luminoso, noto), Nishita 5 / 10 / 18 (scattering singolo in aria pulitissima: il cielo è circa metà
di uno misurato). In nit, sole a 60°: 97 klux a terra con Hosek-Wilkie, disco 1.35×10⁹ nit.
`scenes/review/daylight-units.yaml` (EV100 15): pavimento grigio 18% misurato 4290 nit contro i
4308 attesi dall'illuminanza del cielo (-0.4%). Confronto dei modelli in
`images/daylight-sky-models.jpg`, prima/dopo delle otto scene con cielo fisico in
`images/sky-before-after.jpg`.

**Denoiser** (3.33 mitigato): il sole in uno specchio non diventa più un quadrato.

**Validazione Mitsuba**: invariata rispetto alla Fase 5 su tutte le scene (cornell 0.9998 / 0.9999,
lights 0.9997, glass-shadow 0.9996, metal-presets 1.0001 / 1.0000, glass-rough 1.0009 / 1.0001,
null-shell 1.0001).

**Verdetto**: luci e illuminazione diretta corrette e verificate tipo per tipo; le unità fisiche
permettono di illuminare ed esporre una scena come nella realtà. Cambia l'aspetto delle scene con
un cielo fisico (sole molto più forte, ombre piene: vanno riviste con le altre alla fine della
review, insieme al passaggio a unità fotometriche dove serve) e di quelle con `hosek_wilkie` (ora un
modello diverso da Preetham). Restano per altre fasi: NaN e firefly nei mezzi con il sole vero (3.59,
3.60, Fase 8), scattering multiplo per Nishita (PLANNING), le cuciture del denoiser sui bordi HDR
(3.33, Fase 9).

### Fase 7 - Integratore (`Rendering/PathIntegrator.cs`, `Rendering/Renderer.cs`)

- [x] Riscrivere `TraceRay` come **loop iterativo** con throughput esplicito (prerequisito per
      path guiding, LPE/AOV per lobo, debug, profondità per tipo di lobo). `TracePath` in
      `Rendering/PathIntegrator.cs`, identico al bit alla ricorsione dove nulla doveva cambiare.
- [x] MIS: pesi corretti per luce scelta (`p_pick x p_light`), emissione colpita via BSDF, lobi
      delta, coerenza delle pdf (3.13). Test Veach (scena dei 4 piani lucidi e 4 luci):
      `validation/scenes/veach-mis.yaml`, uguale a Mitsuba con `balance` e con `power`.
- [x] Russian Roulette standard sul throughput (con correzione `η²` dopo le rifrazioni), niente
      classificazione euristica della scena (3.8).
- [x] Profondità separate per tipo (diffuse, glossy, transmission, volume) come i motori di
      produzione: il vetro richiede molti rimbalzi, il diffuso pochi. Più i cut-out, fuori dal
      totale.
- [x] Clamp: spento di default, `-C` opzionale (3.8, 3.20; 2026-10-20).
- [x] Caustiche: la photon map è biased per natura. Valutare **MNEE** (manifold next-event
      estimation, per le caustiche attraverso vetro/acqua) e/o **path guiding** (roadmap #23)
      come strada premium non biased. Valutazione nell'esito: path guiding prima, MNEE dopo,
      ciascuno come lavoro a sé (§4 Fase 7b).
- [x] Unbiasedness: confronto con il `path` integrator di Mitsuba a parità di profondità su
      tutte le scene canoniche.

#### Esito Fase 7 (2026-10-24)

Suite: 1072 test verdi, 0 skip (1065 alla Fase 6).

**Loop iterativo** (3.16). `Rendering/PathIntegrator.cs` sostituisce `TraceRay`, `ShadeSurface` e
`ShadeSampleBounce`: un `PathState` porta raggio, throughput β, pdf e flag delta del rimbalzo
precedente, categoria del raggio, pile di mezzi e IOR, contatori per tipo e radianza; ogni
contributo è β × X. Il random walk del SSS restituisce radianza, raggio d'uscita e peso, e il loop
prosegue da lì. Confronto con la ricorsione (stesso sampler, 256 campioni, 15 scene): identico al
bit (relMSE < 1e-15) su Cornell, vetri, luci, unità, caustiche, acqua, nebbia locale, SSS annidato,
`-C`; diverso solo dove la ricorsione sbagliava:
- i raggi usciti dal SSS e quelli diffusi da un mezzo erano etichettati "camera" e saltavano le
  superfici `visible_to_camera: false`: nel marmo SSS mancava la luce dei pannelli emissivi
  nascosti alla camera (+0.36% in media, +0.03 per pixel dove la vede) (3.61);
- il β della roulette non conteneva l'assorbimento di Beer-Lambert: decisioni di roulette
  diverse nelle scene con mezzi, stessa media (nebbia omogenea +0.46% contro ±0.34% di rumore
  fra due seed).
Altri errori di categoria corretti col loop (3.61): un rimbalzo glossy era etichettato diffuse
(i flag `visibility.glossy` non agivano), un riflesso speculare del cielo leggeva la categoria
camera (lo sfondo riservato alla camera compariva negli specchi), un raggio passato da un cut-out
diventava "transmission". Un cut-out non è un rimbalzo: non cambia la categoria, non conta nel
totale e non rilancia la roulette.

**Russian roulette standard** (3.8). Dal secondo rimbalzo, sopravvivenza q = max(β × η²), divisione
per q, nessun minimo, tetto o classificazione: tolti il classificatore "indirect-dominant" (soglia
sull'irradianza media della scena, due tarature 4/0.15 e 8/0.5) e la soglia dei path morti
(`DeadPathThroughputEpsilon`), che erano bias o euristiche dipendenti dalla scena. Il riferimento
usa la stessa roulette.

**Profondità per tipo.** Ogni campione BSDF riporta il suo lobo (`BsdfLobe`: diffuse, glossy,
transmission, transparent) e il rapporto degli indici; il path conta i rimbalzi per tipo
(`--depth-diffuse`, `--depth-glossy`, `--depth-transmission`, `--depth-volume`) sotto il totale
(`-d`, i vertici), e i cut-out a parte (`--depth-transparent`, 16), fuori dal totale. Preset:
draft 8 (3/4/8/3), standard 12 (4/6/12/4), pre-final e final 16 (8/8/16/8), ultra 32 (16/16/32/16);
un `-d` esplicito porta ogni tipo a quel valore, il riferimento usa profondità uniformi.
`standard` non forza più `--light-sampling power`. Test analitico: interno di una sfera
lambertiana con una luce al centro, L = aE/π × (1-a^d)/(1-a) a profondità 1, 3 e 200 entro l'1.5%,
limite diffuso, limite dei cut-out (`IntegratorTests`). Scena `scenes/review/path-depths.yaml`:
nocciolo di vetro annidato e pila di cut-out, prima (profondità 4 per tutto: nocciolo nero) e dopo
in `images/path-depths.jpg`.

**MIS di Veach** (`validation/scenes/veach-mis.yaml`, 4 lastre d'argento da α 0.0004 a 0.14 sotto
4 luci sferiche di uguale potenza, raggi 0.05-1): rapporto con Mitsuba 0.9991 (`balance`) e 0.9993
(`power`), relMSE 3.7e-4. Il convertitore ignorava l'alias `roughness` del metallo (rendeva le
lastre specchi) e aveva un minimo di α diverso dal motore: corretti.

**Validazione Mitsuba**: invariata su tutte le scene canoniche (cornell d32 0.9998, d2 0.9981,
lights 0.9997, glass-shadow 0.9994, metal-presets 0.9998, metal-rough 1.0032, glass-rough 1.0016,
null-shell 1.0001, fornace 1.0000; disney-spheres 0.9771, differenza di modello nota).

**Caustiche: valutazione di path guiding e MNEE.** La photon map resta lo stimatore delle
caustiche: distorta (stima di densità) ma consistente con più fotoni. Le due strade non distorte:
- *Path guiding* (Müller 2017 "Practical Path Guiding", SD-tree spazio-direzionale; varianti di
  Rath 2020 e Ruppert 2020): impara la radianza incidente nelle passate precedenti e la mescola al
  campionamento della BSDF con MIS a un campione. Generale: aiuta interni illuminati da una
  finestra, luce che arriva da una sola apertura, caustiche viste da superfici diffuse quando la
  luce è un emettitore esteso. Non può nulla su catene con soli lobi delta verso una luce
  puntiforme (probabilità zero per qualunque path tracer). Richiede il loop iterativo (fatto),
  passate progressive di addestramento, un albero aggiornato in modo concorrente; una libreria
  esistente (Open PGL, Apache-2.0) è C++ nativo: meglio un'implementazione C#.
- *MNEE* (Hanika 2015) e la sua versione non distorta *SMS* (Zeltner 2020): risolve con Newton la
  catena speculare fra il punto diffuso e la luce. Copre proprio il caso che il guiding non può
  (luce piccola attraverso vetro o acqua lisci), ma richiede le derivate della normale di ogni
  primitiva, è limitato a catene corte di interfacce lisce e non gestisce normal map e vetri
  ruvidi.
Decisione: path guiding come primo lavoro (generale, sfrutta il loop), MNEE/SMS dopo per le
caustiche da luci piccole attraverso dielettrici lisci, photon map tenuta come opzione rapida.
Sono lavori a sé, nella Fase 7b qui sotto, da fare dopo le Fasi 8 e 9 (volumi e denoiser
cambiano ciò che il guiding deve imparare) o prima, su richiesta del proprietario.

**Verdetto**: l'integratore è un loop non distorto e verificato (analitico, Mitsuba, Veach), con
profondità per tipo come i motori di produzione e senza euristiche dipendenti dalla scena.
Cambiano i preset (più profondità totale, meno rimbalzi diffusi nel draft) e l'aspetto delle
scene con vetri annidati o cut-out impilati (prima scure per mancanza di profondità). Restano:
path guiding e MNEE (Fase 7b), NaN e firefly nei mezzi (3.59, 3.60, Fase 8).

### Fase 7b - Path guiding e caustiche non distorte (rinviata)

- [ ] Path guiding spazio-direzionale (SD-tree, Müller 2017, con le migliorie di Rath 2020 per
      il MIS fra BSDF e guida): passate progressive di addestramento, superfici e volumi.
- [ ] MNEE / SMS (Hanika 2015, Zeltner 2020) per le caustiche da luci piccole attraverso
      dielettrici lisci: derivate della normale per primitiva, catene di una o due interfacce.
- [ ] Confronto con la photon map e con il riferimento su `glass-caustics`, acqua, finestra.

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

**Risposta breve: sì, va modificato.** YAML come formato di authoring è una buona scelta; i
problemi sono di semantica, non di sintassi. Nessun vincolo di compatibilità (§2.0): il formato
nuovo è quello giusto, non quello che le scene vecchie possono già leggere; un tool di migrazione
serve solo se fa risparmiare lavoro nella revisione delle scene, non come requisito.

Cosa manca rispetto a un formato premium:

1. **Versione** del formato: `format_version: 2`. Migrazione dalla v1 (generalizzare
   `MigrateFakeSss` in un tool `SceneMigrate`) solo se conviene per la revisione delle scene.
0. **Decisioni rimandate per compatibilità** nelle fasi chiuse: la lista in §2.0, da chiudere qui.
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
4. Scena MIS di Veach (4 lastre a rugosità crescente x 4 luci di dimensione crescente): `validation/scenes/veach-mis.yaml` (Fase 7).
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
| Integratore | **rifatto** (Fase 7) | loop iterativo con throughput esplicito, roulette standard, profondità per tipo; uguale a Mitsuba su tutte le scene canoniche e su Veach. Restano path guiding e MNEE (Fase 7b) |
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
   descrive il motore definitivo e le scene si adeguano una volta sola.
7. Fase 11.
8. **Revisione delle scene** (§2.0), alla fine: ogni scena del repo (`scenes/`, `showcases/`,
   `presets/`, i tutorial) si confronta con il motore definitivo. Si corregge dove usa chiavi,
   materiali, proprietà o oggetti cambiati; si riscrive se non regge più; altrimenti si sposta in
   `scenes/obsolete/` per essere riprogettata o eliminata. Si guarda anche il risultato
   (illuminazione, materiali, esposizione), con il proprietario. Le scene di `scenes/review/` e
   `validation/scenes/` si aggiornano ma restano.
