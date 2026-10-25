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

## 2. Algoritmo: fusione degli attraversamenti

L'implementazione CSG classifica il raggio a intervalli e funziona anche per solidi non convessi, alberi annidati e facce coincidenti (PIANO-REVIEW.md, Fase 2).

### 2.1 Raccolta degli attraversamenti

Per ogni raggio il motore raccoglie gli attraversamenti di ciascun figlio nell'intervallo richiesto, chiamando ripetutamente `Hit()` con `tMin` appena oltre il punto trovato (circa 8 ulp di t: un passo più lungo salterebbe l'uscita di un raggio che sfiora uno spigolo). Ogni attraversamento porta la flag `FrontFace`: il raggio **entra** nel figlio (true) o ne **esce** (false). Lo stato all'inizio dell'intervallo viene dal primo attraversamento (se esce, il raggio partiva dentro); senza attraversamenti nell'intervallo, una sola interrogazione oltre di esso lo dice.

### 2.2 Fusione e stato

Gli attraversamenti dei due figli sono fusi in ordine di t. Quelli più vicini di una tolleranza formano **un solo evento**: due facce coincidenti (un box a filo con la faccia di un altro, due box che si toccano) sono trovate da primitive diverse a pochi ulp di distanza. La tolleranza è una distanza **attraverso la superficie** (relativa alle coordinate del punto, 2^-17, con un minimo che scala con la scena), quindi lungo il raggio vale quella distanza divisa per |cos θ|: a incidenza radente le distanze di due facce coincidenti differiscono del loro errore di posizione diviso il coseno. Elementi più sottili della tolleranza vengono fusi.

Dopo ogni evento lo stato di ciascun figlio è la `FrontFace` del suo ultimo attraversamento nell'evento, non un conteggio di parità: un attraversamento trovato due volte non inverte il resto del raggio.

### 2.3 Superfici

Il risultato è dentro dove vale op(dentro A, dentro B):

| Operazione | Dentro il risultato |
|------------|--------------------|
| **Union** | dentro A **o** dentro B |
| **Intersection** | dentro A **e** dentro B |
| **Subtraction** | dentro A **e non** dentro B |

Il risultato ha una superficie **esattamente dove il suo stato cambia**, e solo lì. Ne seguono i casi di facce coincidenti: la faccia che due solidi a contatto condividono dentro un'unione non è una superficie, né la bocca di una tasca scavata a filo di una faccia, e A \ A è vuoto. Il primo cambiamento di stato in [tMin, tMax] è il punto colpito.

### 2.4 Verifica

`CsgAuditTests` confronta il primo punto colpito e `FrontFace` con un riferimento esatto: sfere e box hanno intervalli lungo il raggio in forma chiusa, e gli intervalli di un albero booleano sono le operazioni insiemistiche di quelli (due intervalli che si toccano sono uno, un intervallo di lunghezza zero non c'è). Nove casi, otto seed, 20000 raggi ciascuno, con raggi mirati a facce coincidenti e spigoli: unione di sfere, intersezione sfera-box, sottrazione box-sfera, tasca a filo, box che si toccano in unione, intersezione con faccia condivisa, sottrazione di un box che tocca da fuori, albero annidato a filo, A \ A.

---

## 3. Gestione delle Normali

### 3.1 Superfici dei figli originali

Il punto colpito è il record di un figlio il cui attraversamento va nel verso del risultato (A se entrambi). Normali, coordinate UV, tangenti e bitangenti restano quelle calcolate dalla primitiva originale: texture e normal map funzionano su qualsiasi superficie CSG.

### 3.2 Superficie tagliante nella Subtraction

In A \ B un attraversamento di B delimita il risultato nel verso opposto: uscire da B è entrare nel risultato. Per questo nella sottrazione si inverte solo `FrontFace` del record di B. `rec.Normal` è la normale di shading, sempre orientata contro il raggio qualunque sia il solido che possiede la faccia, e resta com'è (invertirla la renderebbe concorde col raggio e azzererebbe i lobi di riflessione).

Così un materiale `dielectric` (vetro) sulla forma B tagliante si comporta correttamente dentro la cavità: la direzione dell'IOR segue `FrontFace`.

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

## 4bis. Tipi ammessi come figli CSG

La classificazione richiede che ogni figlio di un nodo CSG sia una **primitiva solida** con una funzione interno/esterno ben definita, i cui attraversamenti alternano entrate e uscite. Solo così lo stato di dentro/fuori del §2.2 è ben posto. `quad`, `disk`, `annulus` e `triangle` sono superfici aperte: ammesse, ma il loro "interno" è il lato dietro la faccia per il resto del raggio.

**Tipi ammessi come `left` / `right`:**
- Primitive analitiche solide: `sphere`, `box`, `cylinder`, `cone` (anche troncato), `torus`, `capsule`, `quad`, `disk`, `annulus`, `triangle`, `lathe` (alias `revolution` / `surface_of_revolution`).
- Un altro nodo `csg`, per alberi booleani annidati con profondità arbitraria (§4).

**Tipi NON ammessi**, scartati silenziosamente dal loader con il messaggio `CSG entity '…': failed to create one or both children. Skipping.` — il nodo CSG intero viene rimosso dalla scena:

| Tipo scartato | Motivo tecnico |
|---------------|----------------|
| `group` | Un gruppo è una collezione di hittable senza interno/esterno definito. Lo stato "dentro il solido" (§2.2) richiederebbe di contare le intersezioni con l'unione, che è un concetto booleano diverso dal raggruppamento scene-graph. |
| `mesh` / `obj` | Le mesh triangolari non sono garantite essere chiuse né orientabili: lo stato di dentro/fuori dato dalle facce può essere sbagliato. |
| `instance` | Risolvere il template richiederebbe accesso al dizionario dei templates durante la costruzione CSG, che al momento non avviene nel percorso `BuildCsgChild`. |
| `plane` / `infinite_plane` | Un piano infinito ha "interno" non definito (metà spazio? entrambi?), incompatibile con l'algoritmo di classificazione. |

### Ricetta: sostituire un `group` con un `csg: union`

L'uso più comune di `group` come figlio CSG è "sottrai questi N cilindri da un box". La riscrittura corretta è un albero di `csg: union`:

```yaml
# ❌ NON FUNZIONA — il group viene scartato, la CSG salta
- type: "csg"
  operation: "subtraction"
  left:  { type: "box",  scale: [2, 2, 2] }
  right:
    type: "group"
    children:
      - { type: "cylinder", center: [0,0,0],  radius: 0.3, height: 3 }
      - { type: "cylinder", center: [0,0,0],  radius: 0.3, height: 3, rotate: [0,0,90] }

# ✅ FUNZIONA — union esplicito fra i due cilindri, poi sottrazione
- type: "csg"
  operation: "subtraction"
  left:  { type: "box",  scale: [2, 2, 2] }
  right:
    type: "csg"
    operation: "union"
    left:  { type: "cylinder", center: [0,0,0], radius: 0.3, height: 3 }
    right: { type: "cylinder", center: [0,0,0], radius: 0.3, height: 3, rotate: [0,0,90] }
```

Per N ≥ 3 cilindri si annidano più `csg: union`: l'albero ha profondità `log₂(N)` in forma bilanciata o `N−1` in forma lineare; il costo di intersezione scala linearmente con il numero di foglie.

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

- [Tutorial YAML — CSG](../tutorials/02-tutorial-scene/06-entities.md#614-csg--constructive-solid-geometry)
- [Tutorial CSG](../tutorial/it/08-csg.md)
- Codice sorgente: `src/RayTracer/Geometry/CsgObject.cs`, `CsgOperation.cs`
