# 📋 DEVLOG — 3D-Ray Development Log

Documento di lavoro per roadmap, attività, bug noti e note di sviluppo.

> **Convenzione stati:** `✅ Completato` · `🔧 In corso` · `⬜ Da fare`

---

## 🗺️ Roadmap

La roadmap è divisa in due parti: **Fase 0** copre le fondamenta del motore (già implementate prima della pianificazione delle fasi successive); le **Fasi 1–5** coprono le feature sviluppate o pianificate in modo incrementale.

---

### Fase 0 — Fondamenta del Motore ✅

> Tutto quello che esisteva prima che la roadmap per fasi venisse definita.

| Feature | Stato |
|---------|-------|
| Path Tracer multi-bounce con profondità configurabile | ✅ |
| Rendering parallelo multi-core (`Parallel.For`) | ✅ |
| BVH (Bounding Volume Hierarchy) con split SAH-inspired | ✅ |
| Camera con thin lens model e Depth of Field (`aperture`, `focal_dist`) | ✅ |
| Multi-Camera — lista `cameras:` con selezione da CLI (`--camera`) | ✅ |
| Primitivi: Sphere, Box, Cylinder, Triangle, Quad, InfinitePlane | ✅ |
| Primitivi: Cone (tronco), Capsule, Disk, Annulus, SmoothTriangle | ✅ |
| Sistema di Trasformazioni — Scale, Rotate, Translate su qualsiasi primitiva | ✅ |
| Materiali: Lambertian, Metal, Dielectric | ✅ |
| Luci: Point, Directional, Spot, Area (con soft shadows e stratified sampling) | ✅ |
| Next Event Estimation (NEE) — campionamento diretto di tutte le luci | ✅ |
| Russian Roulette adattiva — calibrata sul tipo di illuminazione della scena | ✅ |
| Campionamento Stratificato `√N × √N` per pixel e per area light | ✅ |
| ACES Filmic Tone Mapping + Gamma 2.2 + Firefly Guard | ✅ |
| YAML Scene Loader con validazione e fallback | ✅ |
| Seed deterministico per texture procedurali per-oggetto | ✅ |
| Output PNG / JPEG / BMP con rilevamento automatico dall'estensione | ✅ |
| CI con smoke test (GitHub Actions) | ✅ |

---

### Fase 1 — Fondamenta visive ✅

> Obiettivo: trasformare il motore da progetto educativo a renderer presentabile, con il massimo impatto visivo e il minimo rischio architetturale.

| # | Feature | Stato |
|---|---------|-------|
| 1 | Emissive Material | ✅ Completato |
| 2 | Gradient Sky | ✅ Completato |
| 3 | Image Textures (PNG/JPG/BMP/TIFF/WebP) | ✅ Completato |
| 4 | IBL / HDRI | ✅ Completato |
| 5 | Normal Mapping | ✅ Completato |

**1. Emissive Material ✅** — Superfici auto-illuminanti. Gli oggetti emissivi con geometria campionabile (Sphere, Box, Cylinder, Cone, Torus, Capsule, Annulus, Mesh, SmoothTriangle, Quad, Triangle, Disk) partecipano automaticamente alla NEE come GeometryLight.

**2. Gradient Sky ✅** — Cielo procedurale con gradiente verticale a 3 bande (zenith, orizzonte, terreno) e sun disk con glow halo configurabile. Partecipa alla NEE come EnvironmentLight.

**3. Image Textures ✅** — Caricamento texture da file (PNG, JPEG, BMP, GIF, TIFF, WebP) con bilinear filtering e tiling configurabile via ImageSharp.

**4. IBL / HDRI ✅** — Environment map in formato Radiance `.hdr` con importance sampling via CDF 2D (marginal + conditional) per NEE efficiente. Supporta rotazione Y-axis e moltiplicatore di intensità. Partecipa alla NEE come EnvironmentLight.

**5. Normal Mapping ✅** — Perturbazione normali tramite texture nello spazio TBN (Tangent-Bitangent-Normal) con ortogonalizzazione di Gram-Schmidt. Compatibile OpenGL e DirectX-style (`flip_y`).

---

### Fase 2 — Materiali e geometria professionali 🔄

> Obiettivo: sistema PBR completo, geometria da file, strumenti di composizione avanzati.

| # | Feature | Stato |
|---|---------|-------|
| 6 | Disney BSDF / PBR | ✅ Completato |
| 7 | OBJ Mesh Loader | ✅ Completato |
| 8 | Torus Primitive | ✅ Completato |
| 9 | Mix Material | ⬜ Da fare |
| 10 | Sphere Light | ⬜ Da fare |
| 11 | Scene Graph / Groups | ⬜ Da fare |

**6. Disney BSDF / PBR ✅** — Materiale unificato con sampling stocastico a 5 lobi (diffuse, specular GGX, transmission, sheen, clearcoat). Pesi calibrati su F₀ per minimizzare la varianza. GGX importance sampling per specular e clearcoat. Frosted glass con campionamento di micronormali GGX. Consistenza energetica direct/indirect tramite Cook-Torrance analitico in `EvaluateDirect`.

**7. OBJ Mesh Loader ✅** — Parser Wavefront OBJ con smooth normals (interpolazione Phong), artist UV, TBN da gradiente UV per normal mapping, BVH interno dedicato. Supporta `v/vt/vn`, indici negativi, quad auto-triangolati. Alias YAML: `"mesh"`, `"obj"`.

**8. Torus Primitive ✅** — Intersezione analitica via risolutore di quartiche (metodo di Ferrari) in `QuarticSolver`. UV toroidale, `ISamplable` per NEE, compatibile CSG e Transform. Alias YAML: `"torus"`, `"donut"`, `"ring"`.

**9. Mix Material ⬜** — Materiale che interpola tra due materiali con peso costante o texture mask. Essenziale per ruggine, usura, transizioni graduali. Impatto: nuova classe `MixMaterial : IMaterial`.

**10. Sphere Light ⬜** — Luce sferica dedicata con solid-angle sampling sulla porzione visibile. Nota: una sfera emissiva + GeometryLight funziona già come area light sferica, ma senza l'ottimizzazione del solid-angle sampling.

**11. Scene Graph / Groups ⬜** — Raggruppamento gerarchico con trasformazioni ereditate. Nuova classe `Group : IHittable` con transform cumulativo.

---

### Fase 3 — Convergenza e sampling avanzato 🔄

> Obiettivo: migliorare la qualità del campionamento e ridurre i tempi di rendering.

| # | Feature | Stato |
|---|---------|-------|
| 12 | Importance Sampling | ✅ Completato |
| 13 | Multi-Importance Sampling | ⬜ Da fare |
| 14 | Adaptive Sampling | ⬜ Da fare |
| 15 | Tile-based Rendering | ⬜ Da fare |
| 16 | Denoiser | ⬜ Da fare |
| 17 | HDR Output (EXR/PFM) | ⬜ Da fare |

**12. Importance Sampling ✅** — GGX importance sampling in `Metal` e `DisneyBsdf` (specular, clearcoat, transmission). Environment map importance sampling via CDF 2D. Il diffuse usa cosine-weighted sampling by construction.

**13. Multi-Importance Sampling ⬜** — Balance heuristic (Veach) tra NEE e BSDF sampling. Attualmente i contributi diretti e indiretti sono sommati indipendentemente. Dipende da: #12.

**14. Adaptive Sampling ⬜** — Campionamento per pixel basato sulla varianza. Pixel convergenti terminano in anticipo. Dipende da: #15.

**15. Tile-based Rendering ⬜** — Sostituzione di `Parallel.For` su righe con sistema a tile (es. 32×32). Benefici: cache locality, preview progressivo, prerequisito per adaptive sampling e denoiser.

**16. Denoiser ⬜** — Filtro post-processo (bilateral o NLMeans) guidato da buffer ausiliari (normal, albedo, depth). Dipende da: #15.

**17. HDR Output ⬜** — Salvataggio buffer lineare pre-tone-mapping in formato PFM o EXR.

---

### Fase 4 — Effetti cinematografici ⬜

> Obiettivo: effetti di rendering avanzati per qualità cinematografica.

| # | Feature | Stato |
|---|---------|-------|
| 18 | Motion Blur | ⬜ Da fare |
| 19 | Volumetric Rendering | ⬜ Da fare |
| 20 | Subsurface Scattering | ⬜ Da fare |
| 21 | CSG (Boolean Operations) | ✅ Completato |
| 22 | Instancing | ⬜ Da fare |

**18. Motion Blur ⬜** — Parametro temporale nel `Ray` con interpolazione posizioni.

**19. Volumetric Rendering ⬜** — Fog/fumo con Beer-Lambert e free-path sampling.

**20. Subsurface Scattering ⬜** — BSSRDF o random-walk SSS per materiali traslucidi (pelle, cera, marmo). Il parametro `subsurface` del Disney BSDF è già presente come approssimazione flat.

**21. CSG ✅** — Operazioni booleane union, intersection, subtraction con algoritmo all-hits per correttezza su solidi non-convessi. Annidamento ricorsivo arbitrario, materiali per-figlio, compatibilità BVH (AABB tight per tipo di operazione) e Transform (Jacobian area-preserving). Normali invertite automaticamente sulla superficie tagliante della subtraction con propagazione corretta del frame TBN.

**22. Instancing ⬜** — Copie efficienti con geometria condivisa e transform individuale. Dipende da: #7, #11.

---

### Fase 5 — Frontiera (ricerca) ⬜

> Feature ad alto costo implementativo, riservate a esigenze specifiche o interesse accademico.

| # | Feature | Stato |
|---|---------|-------|
| 23 | Bidirectional Path Tracing | ⬜ Da fare |
| 24 | Spectral Rendering | ⬜ Da fare |
| 25 | Displacement Mapping | ⬜ Da fare |
| 26 | GPU Acceleration | ⬜ Da fare |

**23. Bidirectional Path Tracing ⬜** — Raggi da camera + luci, connessione sotto-path. Dipende da: #13.

**24. Spectral Rendering ⬜** — Lunghezze d'onda individuali per dispersione prismatica.

**25. Displacement Mapping ⬜** — Modifica geometria da height map con tessellation runtime. Dipende da: #7.

**26. GPU Acceleration ⬜** — Rewrite target a lungo termine (CUDA/Vulkan Compute). Praticamente un progetto separato.

---

### Dipendenze tra feature

```
#3 Image Textures ──► #5 Normal Mapping
                  ──► #9 Mix Material (maschere blend)
#6 Disney BSDF   ──► #20 SSS (parametro subsurface già presente)
#7 OBJ Loader    ──► #22 Instancing
                  ──► #25 Displacement Mapping
#11 Scene Graph  ──► #22 Instancing
#12 Importance S.──► #13 MIS ──► #23 Bidirectional PT
#15 Tile-based   ──► #14 Adaptive Sampling
                 ──► #16 Denoiser
```

---

## ✅ TODO

- [ ] Fare una review completa dei tutorials (`tutorials/`): correttezza rispetto al codice, omissioni di feature, grammatica, esempi, indici.
- [ ] Verificare se `utah-teapot.yaml` esiste effettivamente o va rimossa dalla checklist di testing.
- [ ] Aggiornare la checklist di testing con le scene di riferimento corrette.

---

## 🐛 Bug Noti

| # | Descrizione | Severità | Scena / File | Stato |
|---|-------------|----------|--------------|-------|
| 1 | Il parametro `seed` nei materiali procedurali non produce risultati riproducibili tra render della stessa scena. Atteso: seed fisso → texture identica a ogni render. Reale: venature cambiano. Da verificare se il problema è nella randomizzazione del seed o nell'ordine di costruzione degli oggetti. | 🔴 **Alta** | Qualsiasi scena con `seed` esplicito e texture `marble`/`wood`/`noise` | ⬜ |

Severità: 🔴 **Alta** 🟠 **Media** 🟡 **Bassa**

---

## 📝 Note

- Aggiornare i tutorial ogni volta che si aggiunge una nuova primitiva o una feature.
- Idee per scene creative:
  - **Macro Photography**: Primo piano estremo di un orologio meccanico (usando `Annulus` e `Cylinder`) con DOF molto spinta.

---

## 🧪 Checklist Verifiche (Testing)

Procedure da eseguire prima di ogni commit importante.

- [ ] **Smoke Test**: Eseguire il render di `primitive-showcase.yaml` (16 samples) e verificare che non ci siano crash.
- [ ] **Visual Regression**: Confrontare il render di `cornell-box.yaml` con l'immagine di riferimento.
- [ ] **Performance Check**: Verificare che il tempo di render di una scena standard non sia aumentato più del 5% senza motivo.
- [ ] **YAML Validation**: Assicurarsi che ogni nuova proprietà YAML abbia un valore di default sensato nel codice.
- [ ] **CSG Regression**: Render di `csg-showcase.yaml` — verificare union, intersection e subtraction visivamente.
- [ ] **HDRI Test**: Render di `hdri-showcase.yaml` — verificare riflessi, rifrazioni e illuminazione globale.
