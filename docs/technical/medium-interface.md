# MediumInterface — Per-Entity Participating Media

## Ownership model

A `MediumInterface` is an immutable value `struct { IMedium? Interior; IMedium? Exterior }` carried on an entity. The loader resolves the YAML `interior_medium` / `exterior_medium` IDs against the scene's named `mediums:` block and wraps the entity's final `IHittable` in a `MediumBoundHittable` decorator. The decorator's only job is to stamp `rec.MediumIface` and `rec.EntityRoot` on every hit it forwards to the path tracer.

Media live in **one** place — the `Dictionary<string,IMedium>` built by `SceneLoader` from the named `mediums:` block. Entities carry **references** (string IDs) into it. The same `marble_int` medium can therefore back many bodies of marble, all sharing the same σ-coefficients but living in independent geometric volumes. For ready-to-paste `mediums:` blocks see `scenes/presets/mediums.md`.

Materials never own media. Coupling a shading model to scene topology would bind the YAML schema to specific binding strategies; instead, the BSDF emits a `MediumTransition` token (`None` / `Enter` / `Exit`) on transmission events, and the path tracer interprets the token in light of the local entity's `MediumInterface`.

## Stack semantics

The path tracer threads a `MediumStack` (a plain value struct over an 8-slot inline array of media and their bounding entities, zero-allocation) through every recursion, next to an `IorStack` that tracks the refractive indices of nested dielectrics. The stack records *which media the ray is currently inside*.

```
ray enters a refractive boundary:  push(rec.MediumIface.Interior)
ray exits  a refractive boundary:  pop()
active medium at any point:        Top ?? _globalMedium
```

This is non-negotiable for correctness on nested transmissive bodies. Single-current-medium designs break the instant you have a glass ampoule containing marble: when the ray exits marble back into glass-interior-vacuum, the single-current logic loses track of "we're still inside glass" and either applies the wrong absorption or re-pushes the wrong medium. The 8-slot inline array handles the realistic deepest stack (ice in water in tank, ≈ 4 deep) with headroom; overflow drops the **oldest** entry (`Push` returns `false`; the renderer does not report it).

The stack is copy-on-write at every transmission event. `ShadeSampleBounce` copies the caller's stack (a plain struct copy) only when the sample carries an `Enter`/`Exit` transition, pushes or pops on the copy, and hands the copy by `ref` to the recursive `TraceRay` or to the random walk, so the parent frame's view is intact when the recursion returns. Samples with no transition recurse on the caller's stack directly, without copying.

## Refraction transitions

Every BSDF capable of transmitting light emits a `BsdfSample.Transition` enum on its scattered sample:

- `MediumTransition.None` — surface reflection or thin-walled transmission. No stack change.
- `MediumTransition.Enter` — front-face refraction into the geometry. Stack pushes `MediumIface.Interior`.
- `MediumTransition.Exit`: back-face refraction out of the geometry. Stack pops (an empty stack pops to nothing, which guards the rare grazing-angle path whose Enter/Exit pairing desyncs); the renderer does not check which medium was popped.

`Dielectric` and `OpenPbr` (which also serves the `disney` type) are the two materials that emit these transitions, on their refraction samples: the smooth (delta) and the rough (non-delta) refraction alike, while reflection samples return `None`. `MixMaterial` passes on the transition of the child it sampled. Thin-walled surfaces (`geometry_thin_walled: true` on `openpbr`, `thin_walled: true` on `disney`) never change the stack: the ray model treats a thin sheet as a single interface, not a volume, so their transmission returns `None` and `TryGetDielectricIor` keeps them out of the IOR stack too.

## Walk dispatch

After the BSDF samples a refraction event, the renderer applies the same predicate everywhere:

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
    indirect = TraceRay(scattered, ...);   // legacy Beer-Lambert volumetric path
}
```

The walk uses `entityRoot.Hit(...)` for boundary detection — never `_world.Hit`. This is the "restricted BVH query" that prevents the random walk from leaking into adjacent geometry: the walk is constrained to the same BVH it entered, intersected against the *same* sub-tree. Cost: `O(log primitives_in_entity)` per intersection.

## Performance notes

- The wrapper is a single-virtual-call indirection over the underlying `IHittable.Hit`. AABBs are forwarded unchanged so the BVH builder sees the inner geometry's box exactly.
- The stack is a value struct whose inline-array storage lives in the struct itself (two 8-slot arrays of references plus a count), so it stays on the call stack. No GC traffic in the hot loop.
- Restricted-BVH queries during the walk are typically faster than full-world queries because they're scoped to the entity's local BVH only.
- `IsScatteringMedium(hm)` is a sub-microsecond predicate (`σ_s.X + σ_s.Y + σ_s.Z > 0`) so the dispatch site adds no measurable overhead even for non-SSS scenes.

The dispatch is **all-or-nothing per refraction event**: if any of the four conditions in the predicate fails (no entity root, scalar-zero σ_s, off-mode, non-Enter transition) the path falls back to the legacy free-flight loop. Scenes without any SSS binding therefore pay zero walk-side cost.
