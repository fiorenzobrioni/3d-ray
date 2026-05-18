# HeightField — Terreni stile Mitsuba

Il primitivo `HeightField` rappresenta un terreno come una superficie continua `y = h(x, z) · height_scale` su un rettangolo XZ, intersecata direttamente dai raggi senza tassellazione. Una sola entità sostituisce una mesh terreno da centomila triangoli e relativi BVH, e la precisione è limitata solo dalla risoluzione della heightmap — non da quanti triangoli si è disposti a buttare nella scena.

L'idea base viene dal plugin `heightfield.cpp` di **Mitsuba 0.x** (oggi `heightfield2` in Mitsuba 3): una **min/max mipmap** gerarchica funge da quadtree di accelerazione, prunando in `O(log N)` tutte le celle XZ il cui inviluppo Y manca il raggio.

---

## 1. Modello geometrico

Il primitivo è definito su:

- **Rettangolo XZ** — `[xMin, xMax] × [zMin, zMax]` dal campo `bounds`.
- **Heightmap** — una griglia `(N+1) × (N+1)` di campioni `h ∈ [0, 1]`. La cella `(i, j)` ha quattro vertici di altezza ai sample `(i, j)`, `(i+1, j)`, `(i, j+1)`, `(i+1, j+1)`. La superficie all'interno della cella è la **patch bilineare** che interpola i quattro vertici.
- **Scala Y** — i valori normalizzati vengono moltiplicati per `height_scale` al caricamento, quindi tutte le coordinate Y che il ray tracer manipola sono già in unità world.

La heightmap può essere:

- **Baked** — caricata da un PNG-16 grayscale (`L16`) via `HeightmapLoader.Load`. PNG-8 è accettato con warning di terracing.
- **Procedurale** — sintetizzata al caricamento da una `ITexture` (qualunque noise) campionandone la luminanza su una griglia `resolution × resolution`. La griglia diventa l'heightmap definitiva — la texture non viene più valutata al hit-time. La risoluzione di sampling controlla la tightness dell'accelerazione, non la qualità visiva (le bisezioni finali colmano l'errore sotto-cella).

> **Perché bilineare e non bicubico?** Il `min` e `max` analitici di una patch bilineare coincidono esattamente con il `min` e `max` dei quattro campioni d'angolo (combinazione convessa). Questo rende la costruzione della piramide min/max esatta. Una patch bicubica richiederebbe un inviluppo conservativo non banale.

---

## 2. Piramide min/max

La struttura di accelerazione è una piramide quadtree: ogni cella foglia memorizza `(minH, maxH)` dei quattro vertici della cella; ogni nodo interno aggrega `(minH, maxH)` dei suoi quattro figli (2×2). La piramide si fa crescere fino al nodo radice `1 × 1`.

```
livello 0:  CellsX × CellsZ        — celle bilineari
livello 1:  ⌈CellsX/2⌉ × ⌈CellsZ/2⌉
...
livello k:  1 × 1                  — radice
```

La piramide vive in `MinMaxMipmap.cs`. Costruita in `O(N²)` al costruttore; ogni livello aggiunge `1/4` dei nodi del precedente, quindi memoria totale `O(N²) × 4/3`.

**Esattezza dei bound.** Poiché la min/max di una patch bilineare è esatta sui quattro vertici, e poiché il min/max gerarchico aggrega min/max esatti, ogni nodo della piramide fornisce un inviluppo `[minH, maxH]` valido per tutti i punti della superficie nel suo footprint XZ.

---

## 3. Algoritmo di intersezione

Tre fasi, per ogni raggio:

### 3.1 Slab AABB globale

`HeightField.BoundingBox()` restituisce il box `[xMin, 0, zMin] → [xMax, yMax + ε, zMax]`. Il BVH del mondo ha già fatto lo slab test contro questo box prima di chiamare `HeightField.Hit()`. Niente da rifare qui.

### 3.2 Traversal min/max mipmap (`MinMaxMipmap.TraverseRay`)

Walk ricorsivo della piramide, dalla radice (livello più alto) verso le foglie (livello 0):

1. Slab test 3D sulla cella corrente — XZ dal footprint, Y dal range `[minH, maxH]` del nodo.
2. Se la cella non viene perforata dal raggio in `[tMin, tMax]`, scarta l'intero sotto-albero.
3. Se è una foglia (livello 0), invoca il callback `LeafVisitor`.
4. Altrimenti, calcola i quattro figli, ordinali per `tEnter` (front-to-back), e ricorri.

L'ordinamento near-to-far è la chiave: la prima foglia che produce un hit ha automaticamente il `t` più piccolo, e il `tMax` viene contratto dinamicamente — i nodi più lontani vengono prunati senza essere visitati. Costo aspettato `O(log N)` per raggio sui terreni tipici, vs `O(N)` per un linear march.

> **Caso degenere.** Un raggio assiale (`dir.X = 0` o `dir.Z = 0`) origina inv-dir infiniti che diventano NaN nello slab test quando origine e bordo cella coincidono. In pratica i raggi camera non cadono mai esattamente su un edge di griglia, ma le scene di test devono evitare di posizionare l'origine su `xMin + k · cellSizeX` per qualche `k`.

### 3.3 Bisezione sulla patch bilineare (`HeightField.TryBisectCell`)

Quando il traversal raggiunge una foglia, il raggio attraversa il footprint della cella in `[tEnter, tExit]`. Su quell'intervallo, definiamo:

$$f(t) = \mathrm{ray}.y(t) - h(\mathrm{ray}.x(t), \mathrm{ray}.z(t))$$

`f > 0` significa "il raggio è sopra la superficie a quel `t`", `f < 0` significa "sotto". Un cambio di segno tra `tEnter` e `tExit` indica una intersezione interna alla cella.

**Algoritmo.** 12 iterazioni di bisezione classica: dimezza l'intervallo, valuta `f` al punto medio, scarta la metà col segno coerente con uno degli estremi. Dopo 12 step il `t` è preciso a `2⁻¹² ≈ 2.4 × 10⁻⁴` del passo cella — sotto il limite di precisione del path tracer per qualsiasi risoluzione di render ragionevole.

**Reject early.** Quando entrambi gli endpoint hanno lo stesso segno, la cella viene saltata: la piramide ha portato il raggio dentro l'inviluppo `[minH, maxH]` ma il raggio scivola sopra (o sotto) la superficie senza tagliarla. Capita su grazing angles e su celle ai bordi della catena montuosa.

---

## 4. Normale di superficie

Differenze finite centrali sulla patch bilineare:

$$\frac{\partial h}{\partial x} \approx \frac{h(x + \epsilon, z) - h(x - \epsilon, z)}{2\epsilon}$$

con `ε = 0.5 / invCellX` (mezza cella). La normale è poi:

$$\mathbf{n} = \mathrm{normalize}\left(-\frac{\partial h}{\partial x}, 1, -\frac{\partial h}{\partial z}\right)$$

`SetFaceNormal` flippa la normale se il raggio la guarda da sotto, mantenendo `FrontFace` coerente con il resto del motore.

La scelta di `ε = mezza cella` produce normali stabili e congruenti con la patch bilineare effettivamente intersecata. `ε` più piccoli (es. `1e-3`) campionerebbero rumore di quantizzazione PNG e introdurrebbero shimmer.

---

## 5. Strata altitudine/pendenza

Il sistema `strata` è il rimpiazzo runtime dell'approccio "una mesh per stratum" della pipeline mesh. Ogni `StratumBand` definisce una finestra `(min_altitude, max_altitude, min_slope_deg, max_slope_deg)` e un materiale.

Al hit-time:

1. `altNorm = clamp((p.y − seaY) / (height_scale − seaY), 0, 1)` — altitudine normalizzata sopra il livello del mare.
2. `slopeDeg = acos(normal.y) · 180 / π` — pendenza dalla verticale.
3. Per ogni band, peso `w = bandWeight(altNorm, ...) · bandWeight(slopeDeg, ...)` con `bandWeight` plateau-con-fade lineare oltre i bordi.
4. Vince la band col peso massimo; `rec.Material` diventa il materiale della band.

**v1 = winner-takes-all.** Le band possono sovrapporsi e `blend_width` definisce un alone di dominanza, ma il selettore restituisce un singolo materiale. Le transizioni sono soft solo per quanto i materiali stessi (con texture noise procedurale di base) maschereranno il salto. Il lerp interno dei BRDF tra band adiacenti è un follow-up post-v1 (sarebbe equivalente a un `MixMaterial` runtime).

---

## 6. Piano d'acqua opzionale

Quando `sea_level` e `sea_material` sono impostati, il primitivo testa anche l'intersezione con il piano orizzontale `y = sea_level` clippato a `bounds`. Il piano è visibile solo dove il terreno sottostante è sotto il livello (`terrainHeight ≤ sea_level`) — niente piani d'acqua fluttuanti sopra le coste sollevate.

L'ordine dei test:

1. Traversal del terreno → `t_terrain` (può essere `+∞` se il raggio non lo colpisce).
2. Intersezione del piano d'acqua → `t_water`, se valida E se il terreno sottostante è sotto il livello.
3. Vince il `t` più piccolo. Sea hit: normale `(0, 1, 0)`, materiale `sea_material`.

L'acqua è renderizzata come un disco trasparente Disney con `spec_trans` alto e absorption color teal — un singolo materiale, niente caustiche o complicazioni.

---

## 7. File e mapping al codice

| File                                                | Ruolo |
|-----------------------------------------------------|-------|
| `src/RayTracer/Geometry/HeightField.cs`             | Il primitivo `IHittable` — costruzione, hit, normali, strata, sea level. |
| `src/RayTracer/Geometry/MinMaxMipmap.cs`            | Piramide min/max + traversal quadtree near-to-far. |
| `src/RayTracer/Textures/HeightmapLoader.cs`         | Caricamento PNG-16 grayscale (e fallback PNG-8). |
| `src/RayTracer/Scene/SceneLoader.cs:CreateHeightFieldEntity` | Factory YAML → `HeightField` (parser bounds, height_texture / heightmap_path, strata). |
| `src/RayTracer.Tests/HeightFieldTests.cs`           | Test unitari (flat / ramp / sea / strata). |

## 8. Limiti noti (v1)

- **Strata blending hard.** `blend_width` allarga la dominance ma non lerpa i BRDF — le transizioni hanno banding a livello di materiale (le texture noise lo mascherano in buona parte).
- **Acqua singola.** Un solo `sea_level` per primitivo. Laghi e fiumi a quote diverse sono scolpiti nella heightmap come depressioni ma non hanno superfici d'acqua distinte.
- **Niente EXR.** Solo PNG-16 / PNG-8. L'aggiunta di EXR è triviale ma richiede una dipendenza extra.
- **Raggi assiali su edge di cella.** Possibili NaN nel slab test quando origin e bordo coincidono. Non capita su camere reali; mitigato in fase di test scegliendo coordinate non allineate.

## 9. Riferimenti

- **Tevs, Ihrke, Seidel (2008)** — *Maximum Mipmaps for Fast, Accurate, and Scalable Dynamic Height Field Rendering*. SIGGRAPH I3D 2008. L'algoritmo di traversal qui implementato.
- **Mitsuba 0.x `heightfield.cpp`** — implementazione di riferimento dello stesso algoritmo nel renderer di Wenzel Jakob. Il pattern di costruzione della piramide e il dispatch baked/procedural sono modellati da lì.
- **Ebert, Musgrave, Peachey, Perlin (2003)** — *Texturing & Modeling: A Procedural Approach*, §16.3.3. La `hetero_terrain` di `NoiseTexture` (citata in `docs/tutorial/it/03-materials.md`) è la noise di riferimento per la modalità procedurale del HeightField.
