# 11. Galleria delle Scene di Esempio

In questa sezione troverai una raccolta completa di tutti i file di scena (`.yaml`) disponibili nella cartella `scenes/` del repository. Questi file sono progettati per mostrare le varie potenzialità del motore — dai test tecnici basilari a complessi set cinematografici.

Ogni esempio include una breve descrizione e il comando suggerito per il rendering.

---

## 🏗️ Scene Complesse e Cinematografiche
Queste scene sono ideali per valutare la resa finale del motore e includono asset complessi, illuminazione bilanciata e materiali avanzati.

### **Alchemist Lab**
`[alchemist-lab.yaml](../scenes/alchemist-lab.yaml)`
- **Cosa mostra:** Un interno ricco di dettagli (ampolle, libri, tavoli) con illuminazione soffusa da spot e area light. Utilizza materiali `disney` per metalli e vetro.
- **Comando suggerito:**
  ```powershell
  dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/alchemist-lab.yaml -s 64 -w 960 -H 540
  ```

### **Chess (Scacchiera)**
`[chess.yaml](../scenes/chess.yaml)`
- **Cosa mostra:** Un classico del ray tracing. Riflessi speculari perfetti, texture a scacchiera procedurale e materiali metallici con rugosità (`fuzz`).
- **Comando suggerito:**
  ```powershell
  dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/chess.yaml -s 32 -w 800 -H 600
  ```

### **Big Ben & Castello Sforzesco**
`[big-ben.yaml](../scenes/big-ben.yaml)` | `[castello-sforzesco.yaml](../scenes/castello-sforzesco.yaml)`
- **Cosa mostra:** Rendering di architetture monumentali con alta densità di primitive. Ottimo per testare le performance del sistema BVH (O(log N)).

---

## 🧬 Galleria CSG (Modellazione Booleana)
Esempi dedicati alla creazione di oggetti tramite unione, sottrazione e intersezione di solidi.

### **CSG Showcase**
`[csg-showcase.yaml](../scenes/csg-showcase.yaml)`
- **Cosa mostra:** Tutte le operazioni booleane (`union`, `intersection`, `subtraction`) presentate affiancate. Dimostra come i materiali vengano correttamente ereditati dai figli CSG.
- **Comando suggerito:**
  ```powershell
  dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/csg-showcase.yaml -s 16
  ```

### **Reliquary (Reliquiario)**
`[csg-reliquary.yaml](../scenes/csg-reliquary.yaml)`
- **Cosa mostra:** Un oggetto sacro (colonna scavata con all'interno una sfera di cristallo) costruito interamente via CSG. Utilizza sottrazioni multiple per creare la corona e l'alloggiamento interno.

---

## 🔬 Tech Showcase (Feature Test)
Scene focalizzate sulla dimostrazione di una singola feature tecnica.

### **Disney BSDF Showcase**
`[disney-bsdf-showcase.yaml](../scenes/disney-bsdf-showcase.yaml)`
- **Cosa mostra:** Una matrice di sfere che variano i parametri `metallic`, `roughness` e `spec_trans`. Dimostra la continuità del campionamento dallo specchio perfetto alla plastica pura fino al vetro PBR.
- **Comando suggerito:**
  ```powershell
  dotnet run --project src/RayTracer/RayTracer.csproj -- -i scenes/disney-bsdf-showcase.yaml -s 128 -w 1000 -H 600
  ```

### **Normal Map & Texture**
`[normal-map-showcase.yaml](../scenes/normal-map-showcase.yaml)` | `[image-texture-showcase.yaml](../scenes/image-texture-showcase.yaml)`
- **Cosa mostra:** Dettaglio di superficie micro-geometrico tramite normal map (mattoni, metallo graffiato) e mapping UV di file PNG esterni.

### **Gradient Sky & HDRI**
`[gradient-sky-showcase.yaml](../scenes/gradient-sky-showcase.yaml)` | `[hdri-showcase.yaml](../scenes/hdri-showcase.yaml)`
- **Cosa mostra:** Illuminazione tramite ambiente globale. Il gradient sky simula la luce solare procedurale, mentre l'HDRI mostra l'illuminazione fotografica IBL.

---

## 📐 Cornell Box & Test Luci
Il set standard per validare la correttezza del trasporto della luce.

### **Cornell Box (Classic & Crystal)**
`[cornell-box.yaml](../scenes/cornell-box.yaml)` | `[cornell-box-crystal.yaml](../scenes/cornell-box-crystal.yaml)`
- **Cosa mostra:** Illuminazione indiretta (Global Illumination) e ombre morbide da area light. La versione `crystal` sostituisce i cubi con box di vetro per testare caustiche e rimbalzi ricorsivi.

---

[← Torna all'indice](../02-tutorial-scene.md)
