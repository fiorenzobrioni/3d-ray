# 🚩 3D-Ray: Roadmap & Sviluppo Futuro

![Status](https://img.shields.io/badge/status-active-success) ![Version](https://img.shields.io/badge/version-1.0.0-blue) ![Planning](https://img.shields.io/badge/planning-agile-orange)

Questo documento serve a tracciare l'evoluzione del motore **3D-Ray**, raccogliendo le idee, i bug noti, le future implementazioni e le ispirazioni per nuove scene. È il "diario di bordo" dello sviluppo tecnico e creativo.

> **English Description:** *This document tracks the evolution of the 3D-Ray engine, collecting ideas, known bugs, future implementations, and inspirations for new scenes. It's the "logbook" of technical and creative development.*

---

## 🗺️ Roadmap Generale (Milestones)

Obiettivi macro per le prossime versioni del motore.

- [ ] **v1.1 - Ottimizzazione & Post-Processing**
    - [ ] Implementazione di un Denoiser (semplice blur pesato o integrazione esterna).
    - [ ] Supporto per Bloom e Glare sugli highlight.
    - [ ] Ottimizzazione ulteriore del BVH (SAH completo).
- [ ] **v1.2 - Volumetric Rendering**
    - [ ] Supporto per nebbia uniforme (Global Fog).
    - [ ] Materiali volumetrici (fumo, nuvole semplici via OpenVDB o simili).
    - [ ] Sottosuperficie (SSS) più accurata nel Disney BSDF.
- [ ] **v2.0 - Animazione & Video**
    - [ ] Sistema di keyframing YAML per camera e oggetti.
    - [ ] Rendering di sequenze di frame con motion blur.

---

## 🐛 Bachi da Sistemare (Bug Tracker)

Criticità tecniche identificate che richiedono una soluzione.

| Priorità | Descrizione | Stato | Note |
|:---:|---|:---:|---|
| 🔴 **Alta** | Valore seed nei materiali sembra non funzionare | ⏳ In analisi | Verificare implementazione seed. |
| 🟠 **Media** | | ⏳ In analisi | |
| 🟡 **Bassa** | | ✅ Risolto | |
| 🟡 **Bassa** | | 📅 Pianificato | |

---

## ✨ Nuove Implementazioni (Feature Ideas)

Idee tecniche per espandere le capacità di rendering.

### Core & Performance
- [ ] **GPU Acceleration**: Esplorare l'uso di Compute Shader (Vulkan/DirectX) per il path tracing.
- [ ] **Material Layering**: Possibilità di mescolare più materiali (es. ruggine sopra il metallo).
- [ ] **Dispersion**: Rifrazione fisica dipendente dalla lunghezza d'onda (arcobbaleni nel vetro).

### Geometrie & Asset
- [ ] **Supporto MTL per Mesh (OBJ)**: Integrazione dei file materiale `.mtl` per definire materiali diversi per ogni faccia o gruppo di triangoli.

---

## 🖼️ Idee per Scene (Creative Roadmap)

Composizioni nuove da provare.

1. **Showcase primitive**: Valutare se fare una scena di showcase unica per le primitive
2. **Macro Photography**: Primo piano estremo di un orologio meccanico (usando la primitiva `Annulus` e `Cylinder`) con profondità di campo (DOF) molto spinta.
3. **Abstract CSG**: Una scultura astratta creata esclusivamente sottraendo sfere e cubi, con un materiale Disney.

---

## 🧪 Checklist Verifiche (Testing)

Procedure da eseguire prima di ogni commit importante.

- [ ] **Smoke Test**: Eseguire il render della scena `utah-teapot.yaml` (100 samples) e verificare che non ci siano crash.
- [ ] **Visual Regression**: Confrontare il render di `cornell-box.yaml` con l'immagine di riferimento.
- [ ] **Performance Check**: Verificare che il tempo di render di una scena standard non sia aumentato più del 5% senza motivo.
- [ ] **YAML Validation**: Assicurarsi che ogni nuova proprietà YAML abbia un valore di default sensato nel codice.

---

## 📝 Note Varie
- Ricordarsi di aggiornare i tutorial ogni volta che si aggiunge una nuova primitiva.
- Esplorare la possibilità di un set di icone personalizzato per i log in console.
- Fare una review completa dei tutorials (cartella `tutorials/`): 
  - verifica correttezza informazioni coerenti con il codice
  - verifica eventuali omissioni di info e feature
  - verifica grammatica corretta
  - verifica descrizioni corrette
  - verifica esempi corretti
  - verifica file di indici se sono allineati con le sezioni dei tutorial

