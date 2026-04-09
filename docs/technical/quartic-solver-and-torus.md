# QuarticSolver — Documentazione tecnica

## Panoramica

`QuarticSolver` è un solutore analitico per equazioni polinomiali fino al grado 4 (quartica). È stato creato specificamente per l'intersezione raggio-toro, ma è una utility generica riutilizzabile per qualsiasi futura primitiva che produca equazioni di grado ≤ 4 (ad esempio superfici di Bézier, ciclidi, o superfici di rivoluzione arbitrarie).

## Architettura

Il solver è strutturato in tre livelli, ognuno che delega al livello inferiore quando il coefficiente leading è ~0:

```
SolveQuartic (grado 4)
  └── SolveCubic (grado 3)  — usato sia come fallback sia internamente per il risolvente
        └── SolveQuadratic (grado 2)
              └── Lineare (grado 1, inline)
```

**Proprietà:**
- Allocation-free: usa `Span<double>` su stack (`stackalloc`)
- Tutte le operazioni in `double` per precisione (il ray tracer lavora in `float`, ma la quartica è sensibile a errori di arrotondamento)
- I risultati tornano ordinati e filtrati nel range `[tMin, tMax]`
- Deduplicazione automatica delle radici quasi-identiche (tolleranza 1e-6)

## Algoritmi

### Quartica — Metodo di Ferrari

1. **Normalizzazione** a forma monica: `t⁴ + c₃t³ + c₂t² + c₁t + c₀ = 0`
2. **Sostituzione** `t = u - c₃/4` per ottenere la quartica depressa: `u⁴ + pu² + qu + r = 0`
3. Se `q ≈ 0`: biquadratica (quadratica in u²), soluzione diretta
4. Altrimenti: **cubica risolvente** `y³ - (p/2)y² - ry + (pr/2 - q²/8) = 0`
5. La radice reale `y` fattorizza la quartica in **due quadratiche**:
   - `u² + su + (y + q/(2s)) = 0`
   - `u² - su + (y - q/(2s)) = 0`
   dove `s = √(2y - p)`
6. Risolvo le due quadratiche → fino a 4 radici reali
7. **Undo** sostituzione: `t = u - c₃/4`

### Cubica — Formula di Cardano + Metodo Trigonometrico

- **1 radice reale** (discriminante < 0): Cardano classico con radici cubiche
- **3 radici reali** (discriminante > 0): metodo trigonometrico (`cos(θ/3)`)
- **Radici multiple** (discriminante ≈ 0): gestito con `Cbrt(-b/2)`

### Quadratica — Formula standard

Con guard per discriminante negativo e deduplicazione radici doppie.

## Robustezza numerica

La quartica del toro è notoriamente il caso più critico in ray tracing per quanto riguarda la precisione numerica. Le contromisure adottate:

1. **Double precision**: tutti i calcoli interni in `double`, cast a `float` solo al ritorno
2. **Epsilon relativo** (1e-12) per classificazione discriminante, non assoluto
3. **Radice cubica reale**: `Cbrt(x)` gestisce argomenti negativi con `-Cbrt(|x|)`
4. **Selezione radice risolvente**: si sceglie la radice più grande della cubica risolvente per migliore condizionamento numerico
5. **Max guard**: `Math.Max(0.0, ...)` su tutti gli argomenti di `Math.Sqrt` per evitare NaN da discriminanti leggermente negativi per errore di arrotondamento
6. **Deduplicazione**: radici a distanza < 1e-6 vengono fuse (evita hit duplicati sul toro alle tangenze)
7. **Newton-Raphson refinement**: 2-3 iterazioni dopo Ferrari per recuperare la precisione persa nella cancellazione catastrofica quando il parametro risolvente `s` è piccolo (`q/(2s)` amplifica gli errori)
8. **Normalizzazione della direzione**: `Torus.Hit()` normalizza la direzione del raggio prima di calcolare i coefficienti della quartica, garantendo `c₄ = 1` (vedi sezione dedicata sotto)
9. **Validazione delle radici**: ogni radice viene verificata contro l'equazione implicita del toro per scartare radici fantasma residue (vedi sezione dedicata sotto)

## Performance

Su hardware moderno (Zen 4 / Raptor Lake), una chiamata a `SolveQuartic` richiede ~150-250 ns. Per confronto, l'intersezione raggio-sfera è ~20 ns. Il toro è ~8-12× più costoso per primitiva, ma in scene tipiche il BVH elimina la maggior parte dei test, rendendo il costo complessivo trascurabile.

---

# Torus — Matematica dell'intersezione

## Equazione implicita

Il toro centrato nell'origine con asse Y, raggio maggiore R e raggio minore r:

```
(x² + y² + z² + R² - r²)² = 4R²(x² + z²)
```

Questa è la forma di quarto grado. Il lato sinistro è il quadrato di "distanza dall'origine meno la differenza dei raggi", il lato destro è "4R² volte la distanza dall'asse Y al quadrato".

## Derivazione dei coefficienti della quartica

Dato il raggio `P(t) = O + tD̂` con `D̂` **direzione unitaria** (`|D̂| = 1`):

```
Sia:
  od   = O·D̂           (dot product origine-direzione)
  oo   = O·O           (dot product origine)
  dxz² = d̂x² + d̂z²     (componenti XZ della direzione)
  odxz = ox·d̂x + oz·d̂z (prodotto XZ origine-direzione)
  oxz² = ox² + oz²     (componenti XZ dell'origine)
  K    = oo + R² - r²

Allora: t⁴ + c₃·t³ + c₂·t² + c₁·t + c₀ = 0

dove:
  c₃ = 4·od
  c₂ = 4·od² + 2·K - 4R²·dxz²
  c₁ = 4·od·K - 8R²·odxz
  c₀ = K² - 4R²·oxz²
```

Con `|D̂| = 1`, il coefficiente leading `c₄ = (D̂·D̂)² = 1` e la quartica è già in forma monica — pronta per Ferrari senza la divisione per `c₄` che introduce errori di arrotondamento.

### Condizionamento della quartica e normalizzazione della direzione

Il condizionamento numerico della quartica è **criticamente dipendente** dalla lunghezza del vettore direzione del raggio. Con una direzione non unitaria `D` dove `|D| = L`:

```
c₄ = L⁴    c₃ = 4·L²·(O·D)    c₂ = ...    etc.
```

La normalizzazione a forma monica (divisione di tutti i coefficienti per `c₄ = L⁴`) amplifica gli errori di arrotondamento nei coefficienti minori. L'effetto peggiora con L distante da 1:

| Sorgente | `|D|` tipico | `c₄` | Impatto |
|----------|:---:|:---:|---------|
| Camera pinhole (`focal_dist = 1`) | ~1.2 | ~2 | Trascurabile |
| Camera thin-lens (`focal_dist = 8`) | ~8 | ~4096 | Critico: deformazioni geometriche visibili |
| Transform con scale 2× | ~0.5 | ~0.06 | Moderato |
| Transform scale + DoF | variabile | variabile | Cumulativo |

**Soluzione**: `Torus.Hit()` normalizza la direzione prima di costruire i coefficienti. Il parametro `t` cambia di un fattore `|D|`:

```
D̂ = D / |D|
t_normalizzato = t_originale × |D|
```

Dopo la risoluzione della quartica, le radici vengono riscalate: `t = t_norm / |D|`. Il costo è una `sqrt` + una divisione per intersezione (~5 ns su hardware moderno).

> **Nota per futuri sviluppatori**: la normalizzazione è essenziale per il torus e per qualsiasi futura primitiva con intersezione di grado > 2. Non rimuoverla neppure se il QuarticSolver "sembra funzionare" senza — i problemi emergono solo con specifiche combinazioni di Camera (DoF con focal_dist grande) e Transform (scale), e sono difficili da diagnosticare perché si manifestano come deformazioni geometriche sottili, non come crash.

## Validazione delle radici (Surface Residual Check)

Anche con la normalizzazione e Newton-Raphson, il quartic solver può produrre **radici fantasma** in edge case: raggi quasi-tangenti alla superficie, radici doppie, o configurazioni dove la cubica risolvente è mal condizionata.

Per catturarle, dopo aver calcolato il punto di hit `P = ray.At(t)`, la primitiva verifica che il punto soddisfi l'equazione implicita:

```
F(P) = (|P|² + R² - r²)² - 4R²(px² + pz²)
```

Se `|F(P)|` supera una tolleranza proporzionale alle dimensioni del torus (`∝ (R + r)⁴`), la radice viene scartata e si passa alla successiva.

La tolleranza è dimensionata generosamente: accetta tutti i punti la cui distanza dalla superficie è nell'ordine di `10⁻²` unità (per un torus di raggio ~1), ben al di sopra dell'errore float tipico (~10⁻⁵ per `t` nell'ordine di 10). Questo scarta solo i falsi positivi gravi senza rifiutare hit legittimi agli angoli radenti.

## Normale alla superficie

Calcolata geometricamente, non tramite gradiente (più stabile numericamente):

1. Proietta il punto P sul piano XZ: `d_xz = √(px² + pz²)`
2. Trova il punto più vicino sull'anello maggiore: `Q = R · (px/d_xz, 0, pz/d_xz)`
3. Normale = `normalize(P - Q)`

## UV mapping toroidale

- **U = (φ + π) / 2π** dove φ = atan2(pz, px) — angolo azimutale attorno a Y
- **V = (θ + π) / 2π** dove θ = atan2(py, d_xz - R) — angolo poloidale attorno al tubo

Questa parametrizzazione naturale mappa texture su tori come una griglia "longitudine × latitudine" sulla superficie toroidale.

## Note per la composizione di scene con torus

Quando si assembla un oggetto composto (ad esempio un pedone scacchistico) usando un torus come elemento decorativo (base, colletto), è importante che le geometrie adiacenti **coprano completamente** il tubo del torus dove non deve essere visibile. In particolare:

- Il raggio del cono/cilindro sovrapposto deve essere ≥ `MajorRadius - MinorRadius` (il bordo interno del tubo) con un margine di sicurezza.
- Esempio: un torus con R=0.35, r=0.06 ha bordo interno a 0.29 — il cono adiacente dovrebbe avere raggio ≥ 0.32 (non 0.30) per evitare che il tubo del torus protruda attraverso la superficie del cono.
- In alternativa, si può usare CSG subtraction per rimuovere la porzione del torus che interseca il cono, al costo di un'intersezione più complessa.
