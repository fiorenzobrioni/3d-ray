# Strutture di Accelerazione (BVH)

Senza accelerazione spaziale, un ray tracer dovrebbe testare ogni raggio contro tutti gli oggetti della scena ($O(N)$ per ogni raggio). In scene con migliaia di oggetti, questo renderebbe il rendering proibitivo. 3D-Ray utilizza una gerarchia di volumi avvolgenti (**Bounding Volume Hierarchy - BVH**).

## 1. Bounding Volume Hierarchy (BVH)

Il BVH riduce la complessità dell'intersezione raggio-scena da lineare $O(N)$ a logaritmica $O(\log N)$.

### 1.1 Concetto Base
Un **BVH Node** è un contenitore che racchiude un gruppo di oggetti (foglie) o altri nodi BVH (figli) all'interno di un **AABB (Axis-Aligned Bounding Box)**.
Se un raggio non colpisce l'AABB del nodo, possiamo scartare istantaneamente tutto il contenuto del nodo senza testare i singoli oggetti al suo interno.

### 1.2 Algoritmo di Costruzione
Il motore costruisce l'albero in modo ricorsivo:
1.  **Analisi dei Centroidi**: Per l'insieme di oggetti corrente, calcoliamo il bounding box dei loro centroidi (i punti centrali degli AABB degli oggetti).
2.  **Scelta dell'Asse (SAH-lite)**: Identifichiamo l'asse (X, Y o Z) con l'estensione maggiore. Questo assicura che lo "split" degli oggetti sia il più bilanciato possibile lungo la dimensione spaziale dominante.
3.  **Ordinamento e Partizione**: Gli oggetti vengono ordinati lungo l'asse scelto e divisi in due metà (sinistra e destra).
4.  **Ricorsione**: Il processo continua fino a quando ogni nodo contiene uno o due oggetti al massimo.

---

## 2. AABB (Axis-Aligned Bounding Box)

L'AABB è un parallelepipedo allineato agli assi del mondo, definito da due punti: `Min` e `Max`.

### 2.1 Intersezione Raggio-AABB
Utilizziamo una versione ottimizzata dell'algoritmo di **Kay-Kajiya** ("slab method"):
- Per ogni asse $i \in \{x, y, z\}$, calcoliamo l'intervallo $[t_{min,i}, t_{max,i}]$ in cui il raggio attraversa le due facce parallele all'asse.
- L'intersezione con l'intero AABB è l'intersezione di questi tre intervalli.
- Se l'intervallo risultante è vuoto o termina prima dell'inizio del raggio ($t_{max} < 0$), non c'è collisione.

Questo calcolo è estremamente rapido e viene eseguito migliaia di volte prima di testare le geometrie "pesanti" (come il Toro o il CSG).

---

## 3. Ottimizzazioni di Memoria

La costruzione del BVH in 3D-Ray è progettata per essere "allocation-free" o quasi:
- **Pre-allocazione Comparatori**: Utilizziamo delegati statici per il confronto degli assi, evitando migliaia di allocazioni di oggetti `Comparer` durante la fase di build.
- **In-place Sort**: Utilizziamo `List.Sort` sull'array di oggetti originale (partizionato tra `start` ed `end`) per evitare di creare nuove liste ad ogni livello della gerarchia.
- **Centroid-based splitting**: Suddividere gli oggetti in base al loro centro invece che in base al volume totale riduce la sovrapposizione tra i nodi figli, migliorando la performance di ricerca.
