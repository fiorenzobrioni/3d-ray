# DEVLOG 3D-Ray

Diario di sviluppo del progetto. Le voci più recenti vanno in alto.
Ogni voce annota cosa è stato fatto, decisioni prese, problemi incontrati e cosa viene dopo.

Formato suggerito per ogni voce:

## YYYY-MM-DD - Titolo breve
**Fatto:** cosa è stato completato  
**Decisioni:** scelte tecniche/di design e il perché  
**Problemi:** cosa si è bloccato e come (o se) è stato risolto  
**Prossimo:** il passo successivo  

---

## 2026-10-20 - Correzioni anticipate 3.19, 3.20, 3.21 (review)
**Fatto:** 3.19: i raggi d'ombra verso la geometria di una luce passano da `ShadowRay.TransmittanceToPoint` (PBRT "spawn ray to": entrambi gli estremi spostati lungo la propria normale); campionamento conico di `SphereLight` e `Sphere.SampleSolidAngle` rifatto con la costruzione geometrica di PBRT-v4 e la forma per angoli piccoli; `Sphere.Hit` con discriminante robusto e radici stabili. 3.20: firefly clamp opzionale (`-C`), clamp indiretto solo con `-C`. 3.21: raggi d'ombra opachi al vetro che rifrange per default, trasmissione rettilinea (`IMaterial.TransmitsStraight`: IOR 1, `thin_walled` liscio) sempre attraversata; `--transparent-shadows on` come approssimazione coerente (soppressione della catena diffuso → attraverso il vetro → luce/cielo con il bit `ChainShadowPassable`, peso MIS 1 al campione di luce che ha attraversato il vetro); il random walk SSS vede il proprio bordo come interfaccia nulla (`ShadowRay.PassThroughEntity`). Emersi e corretti: `Dielectric.Scatter` con Schlick invece del Fresnel esatto (3.24), `GeometryLight` che non passava il tempo del motion blur (3.25). Nuova scena di validazione `null-shell`; l'harness supporta `extra_modes` e usa `volpath` di Mitsuba quando c'è un BSDF `null`. Test: `LightSelfShadowTests`, `SphereIntersectionPrecisionTests`, clamp opzionale; 661 verdi + 4 skip.  
**Decisioni:** clamp e ombre trasparenti restano disponibili ma solo su richiesta, perché introducono bias; il default è il risultato fisico. Le interfacce rettilinee si attraversano sempre perché lì lo shadow ray è il trasporto esatto (senza, una luce puntiforme non illuminerebbe mai l'interno di un volume delimitato). Il random walk esce senza rifrazione né Fresnel, quindi anche la sua NEE attraversa il bordo con fattore 1 (prima (1-F)): il SSS può risultare leggermente più luminoso, ora coerente fra le due strategie MIS. Nel test SSS dei firefly il clamp è passato esplicitamente perché è proprio la pipeline del clamp che verifica.  
**Problemi:** la prima versione dello spawn ray risolveva area ed emissivi ma non le sfere piccole: la causa era la precisione di `Sphere.Hit` (cancellazione nel discriminante), trovata con una sonda. Il riferimento Mitsuba del guscio IOR 1 era sbagliato con l'integratore `path` (il BSDF `null` blocca i raggi d'ombra): serve `volpath`. Misure prima/dopo in `PIANO-REVIEW.md` §4 Fase 0.  
**Prossimo:** Fase 1 (fondamenta matematiche e sampler).

---

## 2026-10-20 - Review Fase 0: infrastruttura di verifica e prima misura del motore
**Fatto:** `--reference` (stimatore non distorto: niente clamp, ombre trasparenti, soglia "path morto", denoiser e photon map; RR a soglie fisse) e `--seed <n>` (sequenze indipendenti; 0 = bit-identico). Conteggio dei NaN/Inf azzerati stampato a fine render. Kit statistico BSDF in `src/RayTracer.Tests/Validation/` (chi-quadro di Pearson con pooling, integrale della pdf, white furnace, cross-check dell'albedo campionato contro la quadratura di `Evaluate`, reciprocità, coerenza `EvaluateDirect` contro `Evaluate*cos`), con gate (`BsdfStatisticalTests`), report completo opt-in (`BsdfAuditTests`) e scene analitiche (`AnalyticSceneTests`). Tool `src/Tools/ImageCompare` (rapporto delle medie, relMSE, SMAPE, mappa differenza). Harness `validation/` con Mitsuba 3.9.1: convertitore YAML → Mitsuba (solo traduzioni esatte), 6 scene canoniche, render in modalità riferimento e default, FLIP, test di convergenza (spp crescenti e K seed indipendenti). Suite: 653 verdi + 4 skip di bug noti.  
**Decisioni:** il chi-quadro confronta la distribuzione condizionata sul supporto della BSDF: il Disney segue la convenzione PBRT (pdf VNDF anche sotto l'orizzonte, campione scartato), legittima, e la normalizzazione la verifica il cross-check dell'albedo. Il cross-check usa una tolleranza statistica (5 errori standard) invece di una soglia fissa. I bug noti sono test con `Skip` che citano la sezione di `PIANO-REVIEW.md`: verificato che falliscono tutti se riattivati. Il convertitore Mitsuba rifiuta tutto ciò che non sa tradurre esattamente (Metal colorato, vetro tinto) e riproduce i default del motore (colore 0.5).  
**Problemi:** risultati in `PIANO-REVIEW.md` §4 Fase 0 e §3. Integratore corretto (Cornell = Mitsuba entro 0.1%, convergenza 1/N). Trovati: raggi d'ombra che colpiscono il proxy della luce (luce d'area senza luce oltre ~52° dalla normale, -12%), clamp `-C 10` che toglie il 22% di energia alla Cornell, ombre trasparenti che cancellano l'ombra del vetro, Metal con lobo lambertiano solo nella NEE (+37%), strati Disney che creano energia (sheen, clearcoat, vetro), default `color` 0.5 anche per vetro ed emissivi, chiavi YAML sconosciute ignorate. Il sospetto 3.13 (pesi MIS del Disney) è risultato infondato. Durante la costruzione della scena del vetro sono caduto io stesso nel default 0.5 del dielettrico: la scena ora dichiara `color: [1, 1, 1]`.  
**Prossimo:** decidere se anticipare i tre bug gravi e piccoli (3.19, 3.20, 3.21) prima della Fase 1; poi Fase 1 (fondamenta matematiche e sampler).

---

## 2026-10-20 - Piano di review completa del motore
**Fatto:** stesura di `PIANO-REVIEW.md`: metodologia (test statistici sulle BSDF, scene analitiche, confronto con Mitsuba 3 / PBRT-v4, modalità `--reference`), 12 fasi dalle fondamenta (`Core/`, sampler) a geometria, camera e colore, texture, materiali, luci e cielo, integratore, volumi/SSS, denoiser, formato scene v2, performance. Baseline: build pulita, 590 test verdi.  
**Decisioni:** riferimento "premium" = OpenPBR Surface come materiale unico, color management con view transform AgX/ACES, unità fotometriche per luci e camera, integratore iterativo; formato YAML da evolvere (v2 con migrazione), non da riscrivere.  
**Problemi:** già confermati leggendo il codice: Perez del Preetham sbagliata, bias nella selezione dei lobi Disney, texture dati decodificate come sRGB, vetro satinato senza NEE/MIS, lobo lambertiano nel Metal, HDRI campionata ai centri dei texel, diversi bias attivi di default (clamp, ombre trasparenti, RR euristica). Registrati in `PLANNING.md` §Bug noti.  
**Prossimo:** Fase 0 del piano (strumenti di verifica e modalità di riferimento).

---
