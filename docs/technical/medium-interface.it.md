# MediumInterface — Mezzi partecipanti per oggetto

## Modello di ownership

Una `MediumInterface` è un value `struct { IMedium? Interior; IMedium? Exterior }` immutabile, trasportato sull'entity. Il loader risolve gli ID YAML `interior_medium` / `exterior_medium` contro il blocco `mediums:` con nome della scena e avvolge l'`IHittable` finale dell'entity con un decorator `MediumBoundHittable`. L'unico compito del decorator è stampare `rec.MediumIface` e `rec.EntityRoot` su ogni hit che inoltra al path tracer.

I mezzi vivono in **un solo** posto — il `Dictionary<string,IMedium>` costruito da `SceneLoader` a partire dal blocco `mediums:` con nome. Le entity portano **riferimenti** (stringhe ID) verso di esso. Lo stesso `marble_int` può quindi servire molti corpi di marmo, condividendo i coefficienti σ ma vivendo in volumi geometrici indipendenti. Per blocchi `mediums:` pronti da copiare vedi `scenes/presets/mediums.md`.

I material non possiedono mai un medium. Accoppiare un modello di shading alla topologia della scena legherebbe lo schema YAML a strategie di binding specifiche; al contrario, il BSDF emette un token `MediumTransition` (`None` / `Enter` / `Exit`) sugli eventi di transmission, e il path tracer interpreta il token alla luce della `MediumInterface` dell'entity locale.

## Semantica dello stack

Il path tracer fa passare uno `MediumStack` (un `ref struct` su `InlineArray8<IMedium?>`, zero-allocation) attraverso ogni ricorsione. Lo stack registra *in quali mezzi il raggio si trova attualmente*.

```
il raggio entra in un boundary refrattivo:  push(rec.MediumIface.Interior)
il raggio esce  da un boundary refrattivo:  pop()
medium attivo in ogni punto:                Top ?? _globalMedium
```

È non-negoziabile per la correttezza su corpi trasmissivi annidati. I design "single-current medium" si rompono nel momento in cui hai un'ampolla di vetro che contiene marmo: quando il raggio esce dal marmo nel guscio interno di vetro, la logica single-current perde "siamo ancora dentro al vetro" e applica l'assorbimento sbagliato o ripiazza il medium sbagliato. L'array inline da 8 slot gestisce lo stack più profondo realistico (ghiaccio in acqua in tank, ≈ 4 deep) con margine; l'overflow scarta l'entry **più vecchia** e emette un warning deferito.

Lo stack è copy-on-write a ogni evento di transmission. La chiamata ricorsiva `TraceRay` riceve un `ref` allo stack del **chiamante** e lo muta, ma quando una transizione richiede branching (es. durante il dispatch random-walk) l'integratore SSS `Clone()`-a prima lo stack per non corrompere la vista del frame parent.

## Transizioni di refrazione

Ogni BSDF capace di trasmettere luce emette un enum `BsdfSample.Transition` sul sample uscente:

- `MediumTransition.None` — riflessione di superficie o transmission thin-walled. Nessun cambio di stack.
- `MediumTransition.Enter` — refrazione front-face dentro la geometria. Lo stack pusha `MediumIface.Interior`.
- `MediumTransition.Exit` — refrazione back-face fuori dalla geometria. Lo stack pop-pa; se il medium popato non è quello atteso (mismatch — es. un bug d'autoring) il renderer logga un warning deferito e continua con il nuovo top.

`DisneyBsdf.ScatterTransmission` e `Dielectric` sono i due BSDF che emettono queste transizioni oggi. La transmission thin-walled (`thin_walled: true` su Disney) è l'unico caso in cui la transmission NON cambia lo stack: il modello tratta i fogli sottili come un'unica interfaccia, non un volume.

## Dispatch del walk

Dopo che il BSDF campiona un evento di refrazione, il renderer applica lo stesso predicato ovunque:

```
if (sssMode == Auto
    && s.Transition == Enter
    && nextMediums.Top is HomogeneousMedium hm
    && IsScatteringMedium(hm)
    && rec.EntityRoot is IHittable entityRoot)
{
    indirect = RandomWalkSubsurface(scattered, hm, entityRoot, ...);
}
else
{
    indirect = TraceRay(scattered, ...);   // path Beer-Lambert volumetrico legacy
}
```

Il walk usa `entityRoot.Hit(...)` per la boundary detection — mai `_world.Hit`. È la "restricted BVH query" che impedisce al random walk di leakare in geometria adiacente: il walk è vincolato allo stesso BVH in cui è entrato, intersezionato contro lo *stesso* sotto-albero. Costo: `O(log primitive_in_entity)` per intersezione.

## Note di performance

- Il wrapper è una sola indirezione virtual call sopra il sottostante `IHittable.Hit`. Gli AABB sono forwardati invariati così il BVH builder vede il box della geometria interna esattamente.
- Lo stack è `ref struct` e vive interamente sullo stack (letteralmente — la storage `InlineArray8` è inline nello struct). Niente traffico GC nel hot loop.
- Le restricted-BVH query durante il walk sono tipicamente più veloci di query full-world perché sono scoped al BVH locale dell'entity.
- `IsScatteringMedium(hm)` è un predicato sub-microsecondo (`σ_s.X + σ_s.Y + σ_s.Z > 0`) quindi il dispatch non aggiunge overhead misurabile nemmeno su scene non-SSS.

Il dispatch è **tutto-o-niente per evento di refrazione**: se una qualsiasi delle quattro condizioni del predicato fallisce (no entity root, σ_s scalare zero, modalità off, transition non-Enter) il path ricade sul loop free-flight legacy. Le scene senza binding SSS pagano quindi costo zero lato walk.
