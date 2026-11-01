# Strutture di Accelerazione (BVH)

Senza accelerazione spaziale, un ray tracer dovrebbe testare ogni raggio contro tutti gli oggetti della scena ($O(N)$ per ogni raggio). In scene con migliaia di oggetti, questo renderebbe il rendering proibitivo. 3D-Ray utilizza una gerarchia di volumi avvolgenti (**Bounding Volume Hierarchy - BVH**), appiattita in un array e attraversata senza ricorsione (PIANO-REVIEW.md, Fase 11).

## 1. Bounding Volume Hierarchy (BVH)

Il BVH riduce la complessità dell'intersezione raggio-scena da lineare $O(N)$ a logaritmica $O(\log N)$.

### 1.1 Concetto base
Un nodo racchiude un gruppo di oggetti (foglia) o due nodi figli all'interno di un **AABB (Axis-Aligned Bounding Box)**. Se un raggio non colpisce l'AABB del nodo, tutto il contenuto del nodo è scartato senza testare i singoli oggetti.

Tre classi usano lo stesso costruttore (`Acceleration/BvhBuilder.cs`):

- **`Bvh`**: BVH su `IHittable` qualsiasi. È il livello alto della scena (`SceneLoader`), dei gruppi (`Group`) e la ricerca delle luci colpite nella tabella MIS (`LightMisTable`).
- **`TriangleBvh`**: BVH dei triangoli di una mesh (`Mesh`, `Extrusion`). I vertici sono copiati in un unico array nell'ordine delle foglie (i triangoli di una foglia sono contigui in memoria), il taglio (shear) del test watertight del raggio è calcolato una volta per raggio invece che per triangolo (`TriangleIntersection.Shear`), la traversata cerca solo la distanza del triangolo più vicino e l'`HitRecord` è riempito una volta sola, alla fine, dal triangolo vincente con la stessa aritmetica: ogni campo è identico bit per bit a quello della lista dei triangoli (`BvhEquivalenceTests`).
- **`CsgObject`** non è un BVH ma interroga i figli in modo pigro (sezione 4).

### 1.2 Algoritmo di costruzione: SAH binning

Il motore usa la **Surface Area Heuristic (SAH)** con **binning** (Pharr, Jakob e Humphreys, *Physically Based Rendering*, §4.3). Per ogni nodo l'algoritmo:

1.  **Centroid bounds**: calcola il bounding box dei centroidi delle primitive nel range.
2.  **Binning**: ogni primitiva viene assegnata ad uno dei 16 bin equispaziati lungo l'asse candidato, accumulando per bin un AABB ed un contatore.
3.  **Prefix/suffix sums**: si accumulano box e conteggi a sinistra e a destra sui bin, così ogni split candidato (i 15 confini tra bin) costa $O(1)$.
4.  **Costo SAH**: per ogni split si valuta

$$
C = C_{\text{trav}} + \frac{N_L \cdot A_L + N_R \cdot A_R}{A_{\text{parent}}}
$$

    e si sceglie il minimo fra i $3 \times 15 = 45$ candidati. $A$ è la *surface area* del box, $N$ il conteggio di primitive; $C_{\text{trav}}$ è il costo relativo di un nodo rispetto a un'intersezione (0.5).
5.  **Foglia o split**: un range di al più 4 primitive è sempre una foglia; fino a 8 diventa una foglia quando nessuno split costa meno di testarle tutte ($C \geq N$).
6.  **Partizione**: una passata $O(N)$ riordina un array di indici attorno al confine scelto (l'input non viene modificato).
7.  **Fallback**: se tutti i centroidi coincidono si divide il range a metà per numero, così la costruzione termina sempre. Oltre la profondità 64 si divide sempre a metà: la traversata ha così bisogno al più di 128 posti di stack (uno per livello).

### 1.3 Layout appiattito

I nodi (`BvhFlatNode`, 32 byte: due per linea di cache) stanno in un array:

| Campo | Nodo interno | Foglia |
|---|---|---|
| `Min`, `Max` | box del nodo | box del nodo |
| `Offset` | indice del primo figlio (il secondo è `Offset + 1`) | prima primitiva nell'ordine delle foglie |
| `Count` | 0 | numero di primitive |

I due figli di un nodo sono allocati a coppie, adiacenti: la traversata li testa insieme da una sola linea di cache. La costruzione dei sottoalberi grandi (oltre 8192 primitive) è parallela; le coppie di nodi sono prese da un contatore condiviso (`Interlocked`), quindi non serve alcun lock.

### 1.4 Traversata iterativa

`Bvh.Hit` e `TriangleBvh.Hit` non sono ricorsive. Lo stack degli indici dei nodi in attesa (con la loro distanza d'ingresso) vive sullo stack del thread (`stackalloc`), quindi ogni livello di gerarchia annidata (un gruppo dentro una trasformazione dentro la scena) ha il proprio, senza stato condiviso. `[SkipLocalsInit]` evita di azzerarlo a ogni chiamata (l'azzeramento costava un quarto del tempo di un raggio). A ogni nodo interno:

1. si testano entrambi i box figli;
2. se li colpisce entrambi il raggio visita prima il più vicino (distanza d'ingresso minore) e mette l'altro sullo stack;
3. quando riprende un nodo dallo stack lo scarta se nel frattempo è stato trovato un hit più vicino del suo ingresso.

### 1.5 AABB slab test

L'intersezione raggio-AABB usa l'algoritmo di **Kay-Kajiya** ("slab method") in forma branchless vettorizzata (`AABB.Hit`, e la stessa formula in `BvhRay.Hit` per i nodi piatti):

- Il raggio precomputa `InvDirection = 1 / Direction` una sola volta nel costruttore.
- I tre slab vengono valutati in parallelo con `Vector3.Min`/`Vector3.Max` (`System.Numerics` SIMD).
- Uno slab che dà NaN (raggio parallelo a un asse con l'origine sul piano di una faccia) non vincola il raggio.
- **Conservativo** (PIANO-REVIEW.md, Fase 2): le distanze degli slab portano un errore di arrotondamento fino a γ₃ ciascuna, quindi `tExit` è allargato di 1 + 2γ₃ (Ericson 2004, "Real-Time Collision Detection"). Un raggio per lo spigolo di un box, o per il box di spessore zero di un triangolo piatto, non viene mai scartato per arrotondamento. Le primitive non allargano i propri bound di un epsilon assoluto.

### 1.6 Qualità della costruzione

`Bvh.SahCost()` e `TriangleBvh.SahCost()` misurano l'albero: il lavoro atteso di un raggio che colpisce il box radice, ogni nodo pesato dalla probabilità (rapporto delle aree) che lo attraversi anche lui, `TraversalCost` per nodo interno e uno per primitiva di una foglia. È indipendente dalla macchina. `BvhQualityTests` lo confronta con una costruzione top-down che prova ogni split di oggetti su ogni asse con la stessa regola delle foglie (sweep completo): con 16 bin il costo resta entro il 10% (in pratica l'1-2%) su teiera, icosfera, una zuppa di triangoli di dimensioni diverse e un prato di fili sottili.

## 2. Costanti di configurazione

| Costante | Valore | Dove |
|---|---|---|
| `MaxLeafSize` | 4 | `BvhBuilder.cs`: range sempre foglia |
| `MaxSahLeafSize` | 8 | `BvhBuilder.cs`: foglia più grande che la SAH può scegliere |
| `NumBins` | 16 | `BvhBuilder.cs`: bin SAH per asse |
| `TraversalCost` | 0.5 | `BvhBuilder.cs`: costo di un nodo |
| `MaxStack` | 128 | `BvhBuilder.cs`: stack della traversata (profondità SAH al più 64) |
| `ParallelThreshold` | 8192 | `BvhBuilder.cs`: soglia della costruzione parallela |
| `BvhThreshold` | 4 | `SceneLoader.cs`, `Group.cs`, `Mesh.cs`: attivazione BVH |

## 3. Memoria

- **Stackalloc** per bin, somme prefisse e suffisse (zero allocazioni per nodo) e per lo stack della traversata.
- **Nessuna modifica dell'input**: la costruzione partiziona un array di indici e copia le primitive (o i vertici, per `TriangleBvh`) nell'ordine delle foglie.
- **`Transform.BoundingBox` cachato**: il bbox world-space è calcolato una volta nel costruttore di `Transform`, riusato durante la build.

## 4. CSG

`CsgObject.Hit` combina gli attraversamenti dei due figli in ordine di distanza e restituisce il primo punto dove lo stato del risultato cambia (dettagli in [`csg-boolean-operations.md`](./csg-boolean-operations.md)). Gli attraversamenti sono interrogati in modo pigro, uno alla volta per figlio, solo fino all'evento che decide il risultato: le query fatte e il loro ordine sono quelli della raccolta completa, quindi il risultato è identico, senza copiare array di record completi.

## 5. Verifica

`BvhEquivalenceTests` confronta `Bvh.Hit` e `TriangleBvh.Hit` con `HittableList.Hit` su 1/2/3/4/5/20/200/2000 primitive, più casi limite: raggi axis-aligned, centroidi coincidenti, raggi che partono dentro il box, una distribuzione geometrica che porta la SAH alla profondità massima. Ogni raggio deve produrre lo stesso hit/miss e la stessa distanza entro `1e-4`; per `TriangleBvh` l'intero record deve essere identico bit per bit. Vedi [`docs/technical/testing.md`](./testing.md).

Il progetto `RayTracer.Benchmarks` (BenchmarkDotNet) include un harness AABB/BVH dedicato per validare i miglioramenti di performance. Vedi [`docs/technical/benchmarks.md`](./benchmarks.md).
