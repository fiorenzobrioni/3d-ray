# Strutture di Accelerazione (BVH)

Senza accelerazione spaziale, un ray tracer dovrebbe testare ogni raggio contro tutti gli oggetti della scena ($O(N)$ per ogni raggio). In scene con migliaia di oggetti, questo renderebbe il rendering proibitivo. 3D-Ray utilizza una gerarchia di volumi avvolgenti (**Bounding Volume Hierarchy - BVH**).

## 1. Bounding Volume Hierarchy (BVH)

Il BVH riduce la complessità dell'intersezione raggio-scena da lineare $O(N)$ a logaritmica $O(\log N)$.

### 1.1 Concetto Base
Un **BVH Node** è un contenitore che racchiude un gruppo di oggetti (foglie) o altri nodi BVH (figli) all'interno di un **AABB (Axis-Aligned Bounding Box)**.
Se un raggio non colpisce l'AABB del nodo, possiamo scartare istantaneamente tutto il contenuto del nodo senza testare i singoli oggetti al suo interno.

### 1.2 Algoritmo di Costruzione — SAH binning

Il motore usa la **Surface Area Heuristic (SAH)** con **binning** in stile PBRT §4.3. Per ogni nodo interno l'algoritmo:

1.  **Centroid bounds**: calcoliamo il bounding box dei centroidi delle primitive nel range.
2.  **Binning**: ogni primitiva viene assegnata ad uno dei 16 bin equispaziati lungo l'asse candidato, accumulando per-bin un AABB ed un contatore.
3.  **Prefix/suffix sums**: si accumulano left/right box e count sui bin, così ogni *candidate split* (i 15 confini tra bin) costa $O(1)$.
4.  **Costo SAH**: per ogni split si valuta

$$
C = C_{\text{trav}} + \frac{N_L \cdot A_L + N_R \cdot A_R}{A_{\text{parent}}}
$$

    e si sceglie il minimo fra i $3 \times 15 = 45$ candidati (tre assi × quindici confini). $A$ è la *surface area* del box, $N$ il conteggio di primitive; $C_{\text{trav}}$ è il costo relativo di traversal vs intersezione (0.5 di default).
5.  **Partizione in place**: una singola passata $O(N)$ con swap riordina il range attorno al bin boundary scelto — non serve un sort.
6.  **Fallback**: se tutti i centroidi coincidono ($\text{extent} = 0$ su ogni asse) oppure lo split minimo è completamente imbalanced, si ripiega sul median split per numero di oggetti così la ricorsione termina sempre.

### 1.3 Ottimizzazioni strutturali

Sopra la SAH, l'implementazione applica tre tecniche standard di BVH production-grade:

- **Fat leaves** (`MaxPrimitivesPerLeaf = 4`): range di 4 primitive o meno vengono salvati in un array piatto e testati con scan lineare. Questo limita la profondità dell'albero e riduce la pressione sui test AABB su scene clusterizzate.
- **Ordered traversal**: ogni nodo interno memorizza l'asse su cui è avvenuto lo split. A runtime, il figlio sul lato del raggio rispetto a quell'asse viene testato per primo — così `tMax` è già stretto quando si visita il figlio lontano, massimizzando il pruning.
- **Parallel build** (`Parallel.Invoke` sopra `ParallelBuildSpanThreshold = 8192`): i due sottoalberi sono costruiti in parallelo su thread indipendenti. I range sono disgiunti e la partizione è in place, quindi non serve alcun lock.

### 1.4 AABB slab test vettorizzato

L'intersezione raggio-AABB usa l'algoritmo di **Kay-Kajiya** ("slab method") in forma branchless vettorizzata:

- Il raggio precomputa `InvDirection = 1 / Direction` una sola volta nel costruttore.
- I tre slab vengono valutati in parallelo con `Vector3.Min`/`Vector3.Max` (`System.Numerics` SIMD).
- Niente divisioni nel hot path, niente switch per asse.

Questo è il test più frequente di tutto il renderer: un raggio primario ne esegue decine o centinaia per pixel.

## 2. Costanti di configurazione

| Costante | Valore | Dove |
|---|---|---|
| `MaxPrimitivesPerLeaf` | 4 | `BvhNode.cs` — soglia fat-leaf |
| `NumBins` | 16 | `BvhNode.cs` — bin SAH per asse |
| `TraversalCost` | 0.5 | `BvhNode.cs` — costo nodo SAH |
| `ParallelBuildSpanThreshold` | 8192 | `BvhNode.cs` — soglia build parallelo |
| `BvhThreshold` | 4 | `SceneLoader.cs`, `Group.cs` — attivazione BVH |

## 3. Ottimizzazioni di memoria

La costruzione del BVH è "allocation-free" nel ciclo ricorsivo:

- **Stackalloc** per i bin, i prefix sums, i suffix sums (6 span da 16 elementi) — zero allocazioni per nodo interno.
- **Partizione in place** tramite swap sulla `List<IHittable>` passata dal chiamante — niente liste temporanee per livello.
- **Fat leaves** difensive: il range viene copiato in un `IHittable[]` privato cosicché il chiamante possa mutare la sua lista dopo.
- **`Transform.BoundingBox` cachato**: il bbox world-space è calcolato una volta nel costruttore di `Transform`, riusato da tutti i `BoundingBox()` chiamati durante la build.

## 4. Verifica

Il progetto `RayTracer.Tests` contiene test di equivalenza `BvhNode.Hit` vs `HittableList.Hit` su 1/2/3/4/5/20/200/2000 primitive, più casi limite: raggi axis-aligned, centroidi coincidenti, raggi che partono dentro il bounding del sottoalbero. Ogni raggio deve produrre lo stesso hit/miss e la stessa distanza `rec.T` entro `1e-4`. Vedi [`docs/technical/testing.md`](./testing.md).

Il progetto `RayTracer.Benchmarks` (BenchmarkDotNet) include un harness AABB/BVH dedicato per validare i miglioramenti di performance. Vedi [`docs/technical/benchmarks.md`](./benchmarks.md).
