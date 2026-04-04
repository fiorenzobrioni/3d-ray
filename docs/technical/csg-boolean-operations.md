# CSG — Constructive Solid Geometry

La **Constructive Solid Geometry (CSG)** è una tecnica di modellazione che permette di creare forme complesse combinando primitive semplici tramite operazioni booleane. Anziché modellare un oggetto triangolo per triangolo, si descrivono le forme come operazioni matematiche su solidi: un dado forato è un cilindro *sottratto* da un cubo; una lente biconvessa è l'*intersezione* di due sfere; una colonna scavata è una *differenza* tra un cilindro esterno e uno interno.

---

## 1. Le Tre Operazioni

### Union — A ∪ B

Il volume combinato di entrambe le forme. La superficie risultante è la shell esterna dell'unione: le porzioni di A interne a B e le porzioni di B interne a A vengono nascoste, lasciando visibile solo la superficie esterna dell'insieme.

**Uso tipico:** unire forme per creare oggetti organici, lettere 3D, forme meccaniche composite.

```
  A     B          A ∪ B
 ╭─╮  ╭─╮         ╭───╮
 │ ╰──╯ │   →     │   │
 ╰──────╯         ╰───╯
```

### Intersection — A ∩ B

Solo il volume in cui le due forme si sovrappongono. Tutto ciò che è esclusivo di A o di B viene scartato.

**Uso tipico:** lenti ottiche (intersezione di due sfere), profili di taglio, forme convesse complesse difficili da modellare direttamente.

```
  A     B          A ∩ B
 ╭─╮  ╭─╮           ╭╮
 │ ╰──╯ │   →       ││
 ╰──────╯           ╰╯
```

### Subtraction — A \ B

La forma A con il volume di B sottratto. Le superfici di B che tagliano A diventano superfici interne del risultato, con le normali invertite automaticamente per puntare verso l'esterno della cavità.

> ⚠️ **La sottrazione non è commutativa.** `A \ B` produce A forata da B. `B \ A` produce B forata da A. In YAML, `left` è il solido che rimane, `right` è lo stampo che viene rimosso.

**Uso tipico:** bulloni, colonne scavate, modelli architettonici con aperture, qualsiasi forma con cavità interne.

```
  A     B          A \ B
 ╭─╮  ╭─╮         ╭─╮╭╮
 │ ╰──╯ │   →     │  ╰╯
 ╰──────╯         ╰─────
```

---

## 2. Algoritmo: Classificazione per Tutti i Passaggi (All-Hits)

L'implementazione CSG si basa su un algoritmo di classificazione a intervalli che funziona correttamente anche per solidi non-convessi e alberi annidati.

### 2.1 Raccolta di tutte le intersezioni

Per ogni raggio, il motore raccoglie **tutte** le intersezioni con ciascun figlio (non solo la prima), chiamando ripetutamente `Hit()` e avanzando il parametro `tMin` oltre ogni punto trovato. Ogni intersezione riporta la flag `FrontFace` che indica se il raggio sta **entrando** (front face) o **uscendo** (back face) dal solido.

Per una primitiva convessa (sfera, box, cilindro) il risultato è sempre 0 intersezioni (mancato) o 2 intersezioni (entrata + uscita). Per un figlio CSG non-convesso (es. unione di due sfere non sovrapposte) ci possono essere più coppie di intersezioni.

### 2.2 Test di "interno al solido"

Per determinare se un punto a parametro `t` è interno a un solido si conta il numero di intersezioni con quel solido che si trovano prima di `t`: se il conteggio è dispari, il punto è interno.

### 2.3 Selezione dei punti visibili

La regola di selezione dipende dall'operazione:

| Operazione | Superficie di A visibile se... | Superficie di B visibile se... |
|------------|-------------------------------|-------------------------------|
| **Union** | il punto **non è** interno a B | il punto **non è** interno a A |
| **Intersection** | il punto **è** interno a B | il punto **è** interno a A |
| **Subtraction** | il punto **non è** interno a B | il punto **è** interno a A *(normale invertita)* |

Tra tutti i candidati validi viene scelto quello con il `t` minore (il più vicino alla camera).

### 2.4 Perché non l'approccio a due soli raggi

Un approccio alternativo è calcolare gli intervalli `[t_enter, t_exit]` per ciascun figlio e combinarli con la logica dell'operazione. Questo funziona correttamente per coppie di convessi, ma fallisce non appena un figlio è a sua volta un CSG non-convesso (es. `union` di due sfere separate): l'intervallo singolo non può rappresentare due span disgiunti.

L'approccio all-hits usato in 3D-Ray non ha questa limitazione ed è fondamentale per la correttezza degli alberi CSG annidati.

---

## 3. Gestione delle Normali

### 3.1 Superfici dei figli originali (Union e Intersection)

Le normali, le coordinate UV, le tangenti e i bitangenti di ogni figlio vengono preservati esattamente come calcolati dalla primitiva originale. Questo garantisce che texture e normal map funzionino correttamente su qualsiasi superficie CSG.

### 3.2 Superficie tagliante nella Subtraction

Quando una superficie di B è visibile nella sottrazione (cioè forma il bordo della cavità), la sua normale viene **invertita** automaticamente per puntare verso l'esterno della cavità anziché verso l'interno di B.

Oltre alla normale, vengono invertiti:
- `FrontFace` — mantiene la coerenza con la pipeline di shading (usata da Dielectric e Disney BSDF per determinare la direzione dell'IOR)
- `Bitangent` — invertire la normale cambia la chiralità dello spazio tangente; invertire il bitangente mantiene il frame TBN destrorso, garantendo che le normal map si orientino correttamente

Questo significa che un materiale `dielectric` (vetro) applicato alla forma B tagliante si comporta correttamente anche all'interno della cavità: il raggio che entra nella cavità trova la normale orientata verso di lui e la rifrazione funziona come atteso.

---

## 4. Alberi CSG Annidati

Un nodo CSG è esso stesso una primitiva completa: implementa la stessa interfaccia di una sfera o di un box, e può quindi essere usato come figlio di un altro nodo CSG. Questo permette di costruire espressioni booleane arbitrariamente complesse:

```
(A ∪ B) \ C        →   subtraction( union(A, B), C )
(A ∩ B) ∪ (C \ D)  →   union( intersection(A,B), subtraction(C,D) )
```

Non esiste un limite teorico alla profondità dell'albero. In pratica, la complessità del render cresce linearmente con il numero di nodi, perché ogni nodo deve raccogliere tutte le intersezioni dei suoi figli.

### Esempio YAML — Dado forato con tre fori ortogonali

```yaml
- name: "dado_triplo_foro"
  type: "csg"
  operation: "subtraction"
  material: "acciaio"
  left:
    # Prima sottrazione: cubo - foro X
    type: "csg"
    operation: "subtraction"
    left:
      # Seconda sottrazione: cubo - foro Y
      type: "csg"
      operation: "subtraction"
      left:
        type: "box"
        scale: [2, 2, 2]
        translate: [0, 1, 0]
      right:
        type: "cylinder"        # Foro Y (verticale)
        center: [0, -0.5, 0]
        radius: 0.4
        height: 3.0
    right:
      type: "cylinder"          # Foro X (orizzontale)
      center: [0, 1, 0]
      radius: 0.4
      height: 3.0
      rotate: [0, 0, 90]
  right:
    type: "cylinder"            # Foro Z (in profondità)
    center: [0, 1, 0]
    radius: 0.4
    height: 3.0
    rotate: [90, 0, 0]
```

---

## 5. Compatibilità con il Sistema di Trasformazioni

Un nodo CSG può essere avvolto in un `Transform` (scale, rotate, translate) esattamente come qualsiasi altra primitiva. Le trasformazioni vengono applicate all'intero albero CSG come unità, con gestione corretta delle normali via matrice inversa trasposta.

```yaml
- name: "dado_ruotato"
  type: "csg"
  operation: "subtraction"
  material: "metallo"
  rotate: [0, 45, 0]
  translate: [2, 0.5, 0]
  left:
    type: "box"
    scale: [1.5, 1.5, 1.5]
  right:
    type: "cylinder"
    center: [0, -1, 0]
    radius: 0.4
    height: 3.0
```

Le trasformazioni locali sui figli (dentro il nodo CSG) e le trasformazioni globali sul nodo CSG stesso sono indipendenti e si compongono correttamente.

---

## 6. Materiali per-figlio e Fallback

Ogni figlio di un nodo CSG può specificare il proprio `material`. Se un figlio non ha un materiale proprio, eredita quello dichiarato nel nodo CSG padre (`material` a livello radice del nodo).

```yaml
- name: "lente"
  type: "csg"
  operation: "intersection"
  material: "vetro_fallback"     # Usato se un figlio non ha material
  left:
    type: "sphere"
    center: [0, 1, -0.4]
    radius: 1.0
    material: "vetro_verde"      # Material esplicito per questo figlio
  right:
    type: "sphere"
    center: [0, 1, 0.4]
    radius: 1.0
    # Nessun material: usa "vetro_fallback"
```

Questo meccanismo permette di assegnare materiali diversi alle superfici di A e B nello stesso oggetto CSG — fondamentale per effetti come metallo tagliato che espone un nucleo di colore diverso, o vetro incastonato in una cornice metallica.

---

## 7. NEE e Illuminazione

I nodi CSG non implementano `ISamplable` e quindi non partecipano come sorgenti di luce geometriche nella NEE. Questo è intenzionale: le superfici CSG hanno forme potenzialmente complesse e non-convesse per le quali il campionamento diretto sarebbe inaccurato.

Se hai bisogno di un oggetto CSG luminoso, assegna un materiale `emissive` a uno dei figli — la luce si propagherà tramite i rimbalzi del path tracer con qualità identica ma senza campionamento diretto.

---

## 8. Bounding Box

Per ottimizzare le intersezioni con il BVH, ogni nodo CSG calcola una AABB stretta in base all'operazione:

| Operazione | AABB del nodo |
|------------|---------------|
| **Union** | AABB che racchiude entrambi i figli |
| **Intersection** | Intersezione delle due AABB (sempre ≤ di entrambe) |
| **Subtraction** | AABB di A (il risultato non può superare A) |

Questo garantisce che il BVH possa scartare rapidamente i nodi CSG non intersecati senza invocare la logica booleana interna.

---

## Riferimenti

- [Tutorial YAML — CSG](../../tutorials/02-tutorial-scene/06-entities.md#614-csg--constructive-solid-geometry)
- [Libreria Preset CSG](../../tutorials/04-libreria-csg.md)
- Codice sorgente: `src/RayTracer/Geometry/CsgObject.cs`, `CsgOperation.cs`
