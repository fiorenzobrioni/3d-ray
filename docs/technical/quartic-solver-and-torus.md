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

Dato il raggio `P(t) = O + tD`:

```
Sia:
  dd   = D·D           (dot product direzione)
  od   = O·D           (dot product origine-direzione)
  oo   = O·O           (dot product origine)
  dxz² = dx² + dz²     (componenti XZ della direzione)
  odxz = ox·dx + oz·dz (prodotto XZ origine-direzione)
  oxz² = ox² + oz²     (componenti XZ dell'origine)
  K    = oo + R² - r²

Allora: c₄·t⁴ + c₃·t³ + c₂·t² + c₁·t + c₀ = 0

dove:
  c₄ = dd²
  c₃ = 4·dd·od
  c₂ = 4·od² + 2·dd·K - 4R²·dxz²
  c₁ = 4·od·K - 8R²·odxz
  c₀ = K² - 4R²·oxz²
```

## Normale alla superficie

Calcolata geometricamente, non tramite gradiente (più stabile numericamente):

1. Proietta il punto P sul piano XZ: `d_xz = √(px² + pz²)`
2. Trova il punto più vicino sull'anello maggiore: `Q = R · (px/d_xz, 0, pz/d_xz)`
3. Normale = `normalize(P - Q)`

## UV mapping toroidale

- **U = (φ + π) / 2π** dove φ = atan2(pz, px) — angolo azimutale attorno a Y
- **V = (θ + π) / 2π** dove θ = atan2(py, d_xz - R) — angolo poloidale attorno al tubo

Questa parametrizzazione naturale mappa texture su tori come una griglia "longitudine × latitudine" sulla superficie toroidale.
