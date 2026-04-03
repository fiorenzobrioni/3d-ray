# 6. Sezione `entities`

Gli oggetti 3D nella scena. Ogni entità ha un `name`, un `type`, parametri specifici per la geometria e un `material` (riferimento all'id del materiale).

## 6.1 Sphere (Sfera)
```yaml
  - name: "sfera_principale"
    type: "sphere"
    center: [0, 1, 0]
    radius: 1.0
    material: "marmo_bianco"
```
| Campo | Tipo | Descrizione |
|-------|------|-------------|
| `center` | `[X, Y, Z]` | Centro della sfera nel mondo |
| `radius` | float | Raggio |

> **UV Mapping:** Sferico (longitudine/latitudine). `U` ruota attorno all'asse Y ($0$ a $1$), `V` va dal polo sud al polo nord ($0$ a $1$).

> **Area light:** La sfera implementa `ISamplable` e può essere usata come area light emissiva con NEE. Il campionamento è uniforme sulla superficie sferica.

## 6.2 Box (Cubo/Parallelepipedo)

Il Box è definito come un **cubo unitario** centrato nell'origine (da -0.5 a 0.5 su tutti gli assi). Viene poi posizionato e dimensionato tramite le trasformazioni `scale` e `translate`.

```yaml
  - name: "piedistallo"
    type: "box"
    scale: [2.0, 0.5, 2.0]         # Larghezza 2, altezza 0.5, profondità 2
    translate: [0.0, 0.25, 0.0]     # Posizionato con la base a Y=0
    material: "marmo_base"
```
| Campo | Tipo | Descrizione |
|-------|------|-------------|
| `scale` | `[X, Y, Z]` | Dimensioni del box (larghezza, altezza, profondità) |
| `translate` | `[X, Y, Z]` | Posizione del **centro** del box nel mondo |
| `rotate` | `[X, Y, Z]` | Rotazione in gradi (opzionale) |

> **UV Mapping:** Planare per faccia. Ogni faccia del box mappa l'intero intervallo $[0, 1]$ della texture.

### Sintassi alternativa: `min`/`max` (coordinate assolute)

In alternativa a `scale`+`translate`, puoi specificare direttamente gli angoli del box:

```yaml
  - name: "cassa"
    type: "box"
    min: [-1, 0, -2]
    max: [1, 1.5, 2]
    material: "legno"
```

| Campo | Tipo | Descrizione |
|-------|------|-------------|
| `min` | `[X, Y, Z]` | Angolo minimo del box (coordinata assoluta) |
| `max` | `[X, Y, Z]` | Angolo massimo del box (coordinata assoluta) |

> **Nota:** `min`/`max` e `scale`/`translate` sono mutualmente esclusivi per definire forma e posizione. Puoi comunque aggiungere `rotate` a un box definito con `min`/`max` — la rotazione viene applicata sopra.

> **⚠️ Importante:** Il `translate` posiziona il **centro** del box. Se vuoi che la base sia a Y=0, traslaci di `altezza / 2` in Y. Esempio: box alto 1.0 con base a terra → `translate: [0, 0.5, 0]`.

> **Area light:** Il box implementa `ISamplable` e può essere usato come area light emissiva con NEE. Il campionamento è pesato per area tra le 6 facce del box.

## 6.3 Cylinder (Cilindro)
Cilindro finito allineato all'asse Y, con dischi di chiusura (caps) in alto e in basso.
```yaml
  - name: "colonna"
    type: "cylinder"
    center: [0, 0, 0]        # Centro della base inferiore
    radius: 0.4
    height: 3.0
    material: "marmo"
```
| Campo | Tipo | Descrizione |
|-------|------|-------------|
| `center` | `[X, Y, Z]` | Centro della **base inferiore** del cilindro |
| `radius` | float | Raggio del cilindro |
| `height` | float | Altezza (estensione verso +Y dal center) |

> **UV Mapping:** Cilindrico sul corpo (U gira attorno a Y, V sale lungo l'altezza) e planare sui dischi di chiusura (caps).

> **Area light:** Il cilindro implementa `ISamplable` e può essere usato come area light emissiva con NEE.

## 6.4 Cone (Cono / Tronco di Cono)
Cono finito allineato all'asse Y. Può essere un cono appuntito (`top_radius: 0`, default) o un tronco di cono / frustum (`top_radius > 0`). Include dischi di chiusura (caps): sempre alla base, e anche in cima se è un tronco di cono.

### Cono appuntito
```yaml
  - name: "cono"
    type: "cone"
    center: [0, 0, 0]        # Centro della base inferiore
    radius: 1.0               # Raggio della base
    height: 2.0               # Altezza (estensione verso +Y)
    material: "metallo"
```

### Tronco di cono (frustum)
```yaml
  - name: "secchio"
    type: "cone"
    center: [0, 0, 0]
    radius: 1.5               # Raggio base (più largo)
    top_radius: 1.0           # Raggio sommità (più stretto)
    height: 2.0
    material: "plastica"
```

### Cono invertito (più stretto alla base)
```yaml
  - name: "imbuto"
    type: "cone"
    center: [0, 0, 0]
    radius: 0.5               # Raggio base (più stretto)
    top_radius: 2.0           # Raggio sommità (più largo)
    height: 1.5
    material: "acciaio"
```

| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `center` | `[X, Y, Z]` | `[0, 0, 0]` | Centro della **base inferiore** del cono |
| `radius` | float | `1.0` | Raggio alla base |
| `top_radius` | float | `0.0` | Raggio alla sommità. `0` = cono appuntito, `> 0` | tronco di cono |
| `height` | float | `1.0` | Altezza (estensione verso +Y dal center) |

> **UV Mapping:** Cilindrico sul corpo (U gira attorno a Y, V sale lungo l'altezza) e planare sui dischi di chiusura (caps).

> **Alias tipo:** Oltre a `"cone"`, sono accettati anche `"truncated_cone"` e `"frustum"`.

> **Trasformazioni:** Come tutte le entità, il cono supporta `translate`, `rotate` e `scale`. Per inclinare un cono, usa `rotate`. Per un cono capovolto con la punta in basso, ruota di 180° su X: `rotate: [180, 0, 0]`.

> **CSG:** Il cono è un solido convesso e funziona perfettamente come operando CSG. Utile per creare punte, imbuti, raccordi conici, tetti, e forme industriali.

> **Area light:** Il cono implementa `ISamplable` e può essere usato come area light emissiva con NEE. Il campionamento è pesato per area tra superficie laterale e dischi di chiusura.

## 6.5 Triangle (Triangolo)
Triangolo definito da tre vertici.
```yaml
  - name: "triangolo"
    type: "triangle"
    v0: [0, 0, 0]
    v1: [1, 0, 0]
    v2: [0.5, 1, 0]
    material: "rosso"
```
| Campo | Tipo | Descrizione |
|-------|------|-------------|
| `v0` | `[X, Y, Z]` | Primo vertice |
| `v1` | `[X, Y, Z]` | Secondo vertice |
| `v2` | `[X, Y, Z]` | Terzo vertice |

> **UV Mapping:** Baricentrico. Le coordinate `u` e `v` corrispondono ai pesi dei vertici V1 e V2.

> **Area light:** Il triangolo implementa `ISamplable` e può essere usato come area light emissiva con NEE.

## 6.6 Quad (Quadrilatero)
Un parallelogramma definito da un punto d'origine Q e due vettori U e V che definiscono i lati.
```yaml
  - name: "parete"
    type: "quad"
    q: [-5, 0, 5]
    u: [10, 0, 0]
    v: [0, 5, 0]
    material: "muro"
```
| Campo | Tipo | Descrizione |
|-------|------|-------------|
| `q` | `[X, Y, Z]` | Punto d'origine del quad |
| `u` | `[X, Y, Z]` | Primo vettore lato |
| `v` | `[X, Y, Z]` | Secondo vettore lato |

> **UV Mapping:** Parametrico/Planare. La texture viene stesa sul parallelogramma definito dai vettori U e V, mappando $[0, 1]$ su entrambi gli assi.

> **Area light:** Il quad implementa `ISamplable` e può essere usato come area light emissiva con NEE.

## 6.7 Disk (Disco)
Disco piatto con centro, normale e raggio.
```yaml
  - name: "disco"
    type: "disk"
    center: [0, 0, 0]
    normal: [0, 1, 0]
    radius: 2.0
    material: "metallo"
```
| Campo | Tipo | Descrizione |
|-------|------|-------------|
| `center` | `[X, Y, Z]` | Centro del disco |
| `normal` | `[X, Y, Z]` | Normale del piano del disco |
| `radius` | float | Raggio |

> **UV Mapping:** Planare. Proiezione locale sul piano del disco; il raggio viene mappato nell'intervallo $[0, 1]$.

> **Area light:** Il disco implementa `ISamplable` e può essere usato come area light emissiva con NEE.

## 6.8 Plane / Infinite Plane (Piano Infinito)
Piano infinito utile per pavimenti o sfondi.
```yaml
  - name: "pavimento"
    type: "infinite_plane"
    point: [0, 0, 0]
    normal: [0, 1, 0]
    material: "scacchiera"
```
| Campo | Tipo | Descrizione |
|-------|------|-------------|
| `point` | `[X, Y, Z]` | Un punto appartenente al piano |
| `normal` | `[X, Y, Z]` | Normale del piano |

> **UV Mapping:** Planare "tiled". La texture viene ripetuta all'infinito sul piano. Utile per pavimenti e sfondi.

## 6.9 Torus (Toro / Ciambella)

Toro centrato nell'origine, giacente nel piano XZ (asse del foro = Y). Definito da un raggio maggiore (distanza dal centro al centro del tubo) e un raggio minore (raggio del tubo). Usare `translate`, `rotate` e `scale` per posizionamento nel mondo.

### Toro classico (anello con foro)
```yaml
  - name: "ciambella"
    type: "torus"
    major_radius: 2.0          # Distanza centro → centro tubo
    minor_radius: 0.5          # Raggio del tubo
    translate: [0, 1.5, 0]
    material: "ceramica"
```

### Anello sottile (gioiello)
```yaml
  - name: "anello_oro"
    type: "torus"
    major_radius: 0.8
    minor_radius: 0.08
    translate: [0, 1, 0]
    material: "oro_lucido"
```

### Pneumatico (tubo grosso)
```yaml
  - name: "pneumatico"
    type: "torus"
    major_radius: 1.5
    minor_radius: 0.6
    translate: [0, 2.1, 0]
    rotate: [90, 0, 0]         # Ruotato per stare "in piedi"
    material: "gomma_nera"
```

| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `major_radius` | float | `1.0` | Distanza dal centro del toro al centro del tubo (R) |
| `minor_radius` | float | `0.25` | Raggio del tubo (r) |

> **Alias tipo:** Oltre a `"torus"`, sono accettati anche `"donut"` e `"ring"`.

> **Varianti geometriche:**
> - **Ring torus** (R > r): il classico anello con foro visibile. Il caso più comune.
> - **Horn torus** (R = r): il foro scompare, il tubo tocca il centro.
> - **Spindle torus** (R < r): il tubo si auto-interseca, produce una forma a "mela".

> **Posizionamento:** Il toro è centrato nell'origine come il Box. Usare `translate` per posizionarlo nel mondo. Per un toro verticale (come un pneumatico in piedi), ruotare di 90° su X: `rotate: [90, 0, 0]`.

> **Trasformazioni:** `scale` funziona correttamente — uno scale non-uniforme deforma il toro in un ellissoide toroidale. Il wrapper Transform gestisce normali e area con il Jacobiano.

> **CSG:** Il toro è **non-convesso** e può produrre fino a 4 intersezioni per raggio. Il motore CSG (CollectAllHits con MaxHitsPerChild = 16) lo gestisce correttamente. Utile per creare guarnizioni, raccordi O-ring, tubature e forme meccaniche complesse.

> **Area light:** Il toro implementa `ISamplable` con area = 4π²Rr. Può essere usato come area light emissiva con NEE — ideale per neon ad anello o luci decorative.

> **Intersezione raggio-toro:** Il toro è una forma geometricamente complessa. Il motore risolve l'intersezione in modo analitico per garantire risultati esatti e senza artefatti, anche se questo richiede una capacità di calcolo leggermente superiore rispetto alle primitive più semplici.

## 6.10 Trasformazioni (Translate, Rotate, Scale)

Qualsiasi entità supporta trasformazioni opzionali:

```yaml
  - name: "cubo_ruotato"
    type: "box"
    material: "legno"
    scale:     [1.0, 2.0, 1.0]    # Prima scala
    rotate:    [0, 45, 0]          # Poi ruota (gradi attorno agli assi X, Y, Z)
    translate: [2, 1, 0]           # Poi trasla
```

Le trasformazioni vengono applicate nell'ordine: **Scale → Rotate → Translate**.

## 6.11 Parametro Seed

Il parametro `seed` controlla la randomizzazione delle texture procedurali per ogni oggetto. Specificarlo rende il risultato **riproducibile** tra render successivi:

```yaml
  - name: "sfera_marmo"
    type: "sphere"
    center: [0, 1, 0]
    radius: 1.0
    material: "marmo_variegato"
    seed: 42    # Seed fisso = texture identica
```

Se `seed` è omesso, viene generato un valore casuale ogni volta che la scena viene caricata — le venature cambiano tra render successivi.

---

## 6.12 CSG — Constructive Solid Geometry

La CSG (Geometria Solida Costruttiva) permette di creare forme complesse combinando primitive con operazioni booleane. Un'entità `csg` è essa stessa un `IHittable` e può essere usata come figlio di altri nodi CSG, costruendo alberi booleani arbitrariamente complessi.

### Sintassi Base

```yaml
- name: "nome_oggetto"
  type: "csg"
  operation: "union"        # "union", "intersection" oppure "subtraction"
  material: "mat_default"   # Materiale fallback per i figli senza materiale proprio
  left:
    type: "sphere"
    center: [0, 1, 0]
    radius: 1.0
    material: "mat_a"       # Materiale per-figlio (opzionale)
  right:
    type: "sphere"
    center: [0.8, 1, 0]
    radius: 1.0
    # Nessun material: usa il fallback "mat_default" del padre
```

### Campi

| Campo | Tipo | Obbligatorio | Descrizione |
|-------|------|:---:|-------------|
| `type` | stringa | ✓ | Deve essere `"csg"` |
| `operation` | stringa | ✓ | `"union"`, `"intersection"`, `"subtraction"` (alias: `"subtract"`, `"difference"`) |
| `left` | EntityData | ✓ | Operando sinistro (A). Qualsiasi primitiva o altro nodo `csg`. |
| `right` | EntityData | ✓ | Operando destro (B). Qualsiasi primitiva o altro nodo `csg`. |
| `material` | stringa | — | Materiale fallback per i figli senza `material` proprio. |

### Le Tre Operazioni

**`union` — A ∪ B:** il volume combinato di entrambe le forme.

**`intersection` — A ∩ B:** solo il volume in cui le due forme si sovrappongono.

**`subtraction` — A \ B:** la forma A con il volume di B sottratto. Le normali della superficie B tagliante vengono invertite automaticamente.

> **⚠️ La sottrazione non è commutativa.** `A \ B` e `B \ A` producono forme diverse. In `subtraction`, `left` è il solido che rimane, `right` è lo stampo che viene rimosso.

### Materiale per Figlio

Ogni figlio può specificare il proprio `material`. Se non lo fa, eredita il `material` del nodo padre CSG:

```yaml
- name: "lente"
  type: "csg"
  operation: "intersection"
  material: "vetro"          # Entrambi i figli erediteranno questo materiale
  left:
    type: "sphere"
    center: [0, 1, 0]
    radius: 1.2
  right:
    type: "sphere"
    center: [0, 1, 0.7]
    radius: 1.2
```

### Trasformazioni sui Figli

I figli supportano `translate`, `rotate`, `scale` esattamente come le entità normali:

```yaml
- name: "dado_con_foro"
  type: "csg"
  operation: "subtraction"
  material: "metallo"
  left:
    type: "box"
    scale: [2, 2, 2]
    translate: [0, 1, 0]
  right:
    type: "cylinder"
    center: [0, 0, 0]
    radius: 0.5
    height: 3.0
```

### Alberi CSG Annidati

Un figlio `csg` può essere a sua volta un nodo CSG, costruendo espressioni come `(A ∪ B) \ C`:

```yaml
- name: "forma_complessa"
  type: "csg"
  operation: "subtraction"
  material: "bronzo"
  left:
    type: "csg"               # (A ∪ B)
    operation: "union"
    left:
      type: "sphere"
      center: [-0.5, 1, 0]
      radius: 1.0
    right:
      type: "sphere"
      center: [0.5, 1, 0]
      radius: 1.0
  right:
    type: "box"               # \ C
    scale: [3, 1, 3]
    translate: [0, 0.5, 0]
```

### Compatibilità

| Feature | Supporto CSG |
|---------|:---:|
| Trasformazioni (translate, rotate, scale) | ✓ sul nodo CSG e sui figli |
| Texture procedurali e image texture | ✓ (per materiale) |
| Normal mapping | ✓ |
| BVH | ✓ (AABB calcolato per operazione) |
| Annidamento ricorsivo | ✓ |
| Tipo `infinite_plane` come figlio | ✗ (non convesso) |

> **💡 Per preset CSG pronti all'uso** (lenti, anelli, bulloni, colonne scavate ecc.) consulta la [Libreria dei Preset CSG](../04-libreria-csg.md).

---

---

[← Torna all'indice](../02-tutorial-scene.md)
