# 📋 DEVLOG — 3D-Ray Development Log

Documento di lavoro per roadmap, attività, bug noti e note di sviluppo.

> **Convenzione stati:** `✅ Completato` · `🔧 In corso` · `⬜ Da fare`

---

## 🗺️ Roadmap

### Piano di avanzamento

| #  | Feature                    | Fase | Stato         |
|----|----------------------------|------|---------------|
| 1  | Emissive Material          | 1    | ✅ Completato |
| 2  | Gradient Sky               | 1    | ✅ Completato |
| 3  | Image Textures (PNG/JPG)   | 1    | ✅ Completato |
| 4  | IBL / HDRI                 | 1    | ✅ Completato |
| 5  | Normal Mapping             | 1    | ✅ Completato |
| 6  | Disney BSDF / PBR          | 2    | ✅ Completato |
| 7  | OBJ Mesh Loader            | 2    | ✅ Completato |
| 8  | Torus Primitive            | 2    | ✅ Completato |
| 9  | Mix Material               | 2    | ⬜ Da fare    |
| 10 | Sphere Light               | 2    | ⬜ Da fare    |
| 11 | Scene Graph / Groups       | 2    | ⬜ Da fare    |
| 12 | Importance Sampling        | 3    | ✅ Completato |
| 13 | Multi-Importance Sampling  | 3    | ⬜ Da fare    |
| 14 | Adaptive Sampling          | 3    | ⬜ Da fare    |
| 15 | Tile-based Rendering       | 3    | ⬜ Da fare    |
| 16 | Denoiser                   | 3    | ⬜ Da fare    |
| 17 | HDR Output (EXR/PFM)       | 3    | ⬜ Da fare    |
| 18 | Motion Blur                | 4    | ⬜ Da fare    |
| 19 | Volumetric Rendering       | 4    | ⬜ Da fare    |
| 20 | Subsurface Scattering      | 4    | ⬜ Da fare    |
| 21 | CSG (Boolean Operations)   | 4    | ✅ Completato |
| 22 | Instancing                 | 4    | ⬜ Da fare    |
| 23 | Bidirectional Path Tracing | 5    | ⬜ Da fare    |
| 24 | Spectral Rendering         | 5    | ⬜ Da fare    |
| 25 | Displacement Mapping       | 5    | ⬜ Da fare    |
| 26 | GPU Acceleration           | 5    | ⬜ Da fare    |

**Progresso complessivo: 10 / 26 completati**

### Fase 1 — Fondamenta visive ✅

> Obiettivo: trasformare il motore da progetto educativo a renderer presentabile, con il massimo impatto visivo e il minimo rischio architetturale.

Questa fase sfrutta le interfacce esistenti (`ITexture`, `IMaterial`, `CalculateSkyColor`) per aggiungere capacità visive fondamentali senza modificare il core del path tracer.

**1. Emissive Material ✅** — Superfici auto-illuminanti. Il path tracer supporta un termine di emissione in `TraceRay()`. Gli oggetti emissivi con geometria `ISamplable` partecipano automaticamente alla NEE come `GeometryLight`.

**2. Gradient Sky ✅** — Cielo procedurale con gradiente orizzonte-zenit e sun disk opzionale.

**3. Image Textures ✅** — Caricamento texture PNG/JPG con bilinear filtering via ImageSharp.

**4. IBL / HDRI ✅** — Environment map in formato Radiance `.hdr` con importance sampling via CDF per NEE efficiente.

**5. Normal Mapping ✅** — Perturbazione normali tramite texture nello spazio TBN (Tangent-Bitangent-Normal) con ortogonalizzazione di Gram-Schmidt.

### Fase 2 — Materiali e geometria professionali 🔄

> Obiettivo: sistema PBR completo, geometria da file, strumenti di composizione avanzati.

**6. Disney BSDF / PBR ✅** — Materiale unificato con `roughness`, `metallic`, `specular`, `clearcoat`, `sheen`, `subsurface`, `spec_trans`. Multi-lobe sampling stocastico (diffuse, specular GGX, clearcoat, sheen, transmission).

**7. OBJ Mesh Loader ✅** — Parser Wavefront OBJ con smooth normals (Phong), artist UV, BVH interno dedicato, `ISamplable` per NEE. Supporta `v/vt/vn`, indici negativi, quad auto-triangolati.

**8. Torus Primitive ✅** — Intersezione analitica via risolutore di quartiche (Ferrari) in `QuarticSolver`. UV toroidale, `ISamplable`, CSG compatibile. Alias YAML: `torus`, `donut`, `ring`.

**9. Mix Material ⬜** — Materiale che interpola tra due materiali con peso costante o texture mask. Essenziale per ruggine, usura, transizioni graduali. Impatto: nuova classe `MixMaterial : IMaterial`.

**10. Sphere Light ⬜** — Luce sferica dedicata con solid-angle sampling sulla porzione visibile. Nota: attualmente una sfera emissiva + `GeometryLight` funziona come area light sferica, ma senza l'ottimizzazione del solid-angle sampling.

**11. Scene Graph / Groups ⬜** — Raggruppamento gerarchico con trasformazioni ereditate. Nuova classe `Group : IHittable` con transform cumulativo.

### Fase 3 — Convergenza e sampling avanzato

> Obiettivo: migliorare la qualità del campionamento e ridurre i tempi di rendering.

**12. Importance Sampling ✅** — GGX importance sampling in `Metal` e `DisneyBsdf` (specular, clearcoat, transmission). Environment map importance sampling via CDF. Il diffuse usa `N + RandomUnitVector()` che è cosine-weighted by construction.

**13. Multi-Importance Sampling ⬜** — Balance heuristic (Veach) tra NEE e BSDF sampling. Attualmente i contributi diretti e indiretti sono sommati indipendentemente. Dipende da: #12.

**14. Adaptive Sampling ⬜** — Campionamento per pixel basato sulla varianza. Pixel convergenti terminano in anticipo. Dipende da: #15.

**15. Tile-based Rendering ⬜** — Sostituzione di `Parallel.For` su righe con sistema a tile (es. 32×32). Benefici: cache locality, preview progressivo, prerequisito per adaptive sampling e denoiser.

**16. Denoiser ⬜** — Filtro post-processo (bilateral o NLMeans) guidato da buffer ausiliari (normal, albedo, depth). Dipende da: #15.

**17. HDR Output ⬜** — Salvataggio buffer lineare pre-tone-mapping in formato PFM o EXR.

### Fase 4 — Effetti cinematografici

> Obiettivo: effetti di rendering avanzati per qualità cinematografica.

**18. Motion Blur ⬜** — Parametro temporale nel `Ray` con interpolazione posizioni.

**19. Volumetric Rendering ⬜** — Fog/fumo con Beer-Lambert e free-path sampling.

**20. Subsurface Scattering ⬜** — BSSRDF o random-walk SSS per materiali traslucidi (pelle, cera, marmo).

**21. CSG ✅** — Operazioni booleane (union, intersection, subtraction) con annidamento ricorsivo, materiali per-figlio, compatibilità BVH e Transform.

**22. Instancing ⬜** — Copie efficienti con geometria condivisa + transform individuale. Dipende da: #7, #11.

### Fase 5 — Frontiera (ricerca)

> Feature ad alto costo implementativo, riservate a esigenze specifiche o interesse accademico.

**23. Bidirectional Path Tracing ⬜** — Raggi da camera + luci, connessione sotto-path. Dipende da: #13.

**24. Spectral Rendering ⬜** — Lunghezze d'onda individuali per dispersione prismatica.

**25. Displacement Mapping ⬜** — Modifica geometria da height map con tessellation runtime. Dipende da: #7.

**26. GPU Acceleration ⬜** — Rewrite target a lungo termine (CUDA/Vulkan Compute). Praticamente un progetto parallelo.

### Dipendenze tra feature

```
#3 Image Textures ──► #5 Normal Mapping
                  ──► #7 OBJ Loader (texture .mtl)
                  ──► #9 Mix Material (maschere blend)
#6 Disney BSDF   ──► #7 OBJ Loader (materiali PBR)
                  ──► #20 SSS (parametro subsurface)
#7 OBJ Loader    ──► #22 Instancing
                  ──► #25 Displacement Mapping
#11 Scene Graph  ──► #22 Instancing
#12 Importance S.──► #13 MIS ──► #23 Bidirectional PT
#15 Tile-based   ──► #14 Adaptive Sampling
                 ──► #16 Denoiser
```

---

## ✅ TODO

- [ ] Fare una review completa dei tutorials (cartella `tutorials/`): 
  - verifica correttezza informazioni coerenti con il codice
  - verifica eventuali omissioni di info e feature
  - verifica grammatica corretta
  - verifica descrizioni corrette
  - verifica esempi corretti
  - verifica file di indici se sono allineati con le sezioni dei tutorial
- [ ] —
- [ ] —

---

## 🐛 Bug Noti

| # | Descrizione | Severità | Scena / File | Stato |
|---|-------------|----------|--------------|-------|
| 1 | Valore seed nei materiali sembra non funzionare | 🔴 **Alta** | — | ⬜ |

Severità: 🔴 **Alta** 🟠 **Media** 🟡 **Bassa**

---

## 📝 Note

- Ricordarsi di aggiornare i tutorial ogni volta che si aggiunge una nuova primitiva o una feature.
- Esplorare la possibilità di un set di icone personalizzato per i log in console.
- Idee per scene creative:
  - **Showcase primitive**: Valutare se fare una scena di showcase unica per le primitive
  - **Macro Photography**: Primo piano estremo di un orologio meccanico (usando la primitiva `Annulus` e `Cylinder`) con profondità di campo (DOF) molto spinta.

---

## 🧪 Checklist Verifiche (Testing)

Procedure da eseguire prima di ogni commit importante.

- [ ] **Smoke Test**: Eseguire il render della scena `utah-teapot.yaml` (100 samples) e verificare che non ci siano crash.
- [ ] **Visual Regression**: Confrontare il render di `cornell-box.yaml` con l'immagine di riferimento.
- [ ] **Performance Check**: Verificare che il tempo di render di una scena standard non sia aumentato più del 5% senza motivo.
- [ ] **YAML Validation**: Assicurarsi che ogni nuova proprietà YAML abbia un valore di default sensato nel codice.
- [ ] 
