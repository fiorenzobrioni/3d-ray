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

## 2026-10-20 - Piano di review completa del motore
**Fatto:** stesura di `PIANO-REVIEW.md`: metodologia (test statistici sulle BSDF, scene analitiche, confronto con Mitsuba 3 / PBRT-v4, modalità `--reference`), 12 fasi dalle fondamenta (`Core/`, sampler) a geometria, camera e colore, texture, materiali, luci e cielo, integratore, volumi/SSS, denoiser, formato scene v2, performance. Baseline: build pulita, 590 test verdi.  
**Decisioni:** riferimento "premium" = OpenPBR Surface come materiale unico, color management con view transform AgX/ACES, unità fotometriche per luci e camera, integratore iterativo; formato YAML da evolvere (v2 con migrazione), non da riscrivere.  
**Problemi:** già confermati leggendo il codice: Perez del Preetham sbagliata, bias nella selezione dei lobi Disney, texture dati decodificate come sRGB, vetro satinato senza NEE/MIS, lobo lambertiano nel Metal, HDRI campionata ai centri dei texel, diversi bias attivi di default (clamp, ombre trasparenti, RR euristica). Registrati in `PLANNING.md` §Bug noti.  
**Prossimo:** Fase 0 del piano (strumenti di verifica e modalità di riferimento).

---
