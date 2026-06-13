# Motion Blur — Time-Sampled Transforms

Motion blur is the streak a moving object (or a moving camera) leaves on a frame
because the shutter is open for a finite time. 3D-Ray reproduces it physically:
each camera sample is assigned one instant inside the shutter interval, the
animated transforms are evaluated at that instant, and the whole light path is
traced through that frozen pose. Averaging many samples over the shutter
integrates the moving geometry exactly the way a real exposure does — no
post-process smear, no velocity buffer.

The feature is **transform** motion blur: entities and the camera are animated by
keyframed TRS poses. Deforming geometry (per-vertex animation) is out of scope.

**Bit-identical invariant.** When nothing in the scene is animated the renderer
draws no time sample and every downstream ray carries `time = 0`, so the output
is byte-for-byte identical to a build without motion blur. This mirrors the same
"feature absent ⇒ identical output" guarantee the volumetrics and AOV-capture
paths hold, and is enforced by `MotionBlurTests.InactiveMotionBlur_PixelsAreBitIdentical`.

---

## 1. Timeline, shutter and ray time

The scene animation lives on a normalized timeline `[0, 1]`. The camera's
`shutter: [open, close]` (`0 ≤ open < close ≤ 1`) selects the exposure
sub-interval. For each camera sample the renderer draws

```
rayTime = open + (close − open) · ξ      ξ ∈ [0, 1) low-discrepancy
```

and stamps it onto the primary ray (`Ray.Time`). A narrower shutter is a shorter
exposure: less motion is integrated, so streaks shorten (a `[0.45, 0.55]` shutter
freezes the motion to ~10 % of the `[0, 1]` streak length).

**Sampler dimension placement.** `ξ` is taken from one extra sampler dimension
drawn *after* the two pixel-jitter dimensions and *before* the lens dimensions,
and **only when motion is active**. Keeping the draw conditional preserves the
exact dimension layout of a still render — dimensions 0–1 stay the joint Sobol
(0,2,2)-net pixel jitter — which is what makes the no-motion output bit-identical.
See `Renderer.cs`, both the Sobol and the stratified sample loops.

**Path time propagation.** A path samples the scene at a single instant: the
primary ray's time is inherited unchanged by every secondary ray — shadow/NEE
rays, BSDF bounces, medium phase scatters, the SSS random walk. The renderer owns
this propagation. It stamps `HitRecord.Time` once at the top of `TraceRay`, and
all ray-construction sites downstream pass it on. Materials build their scattered
rays *without* a time; the renderer re-stamps them via `Ray.WithTime` at the
single consumption point (the legacy `Scatter` path), so the twelve material call
sites stay untouched.

---

## 2. `AnimatedTransform`

`Geometry/AnimatedTransform.cs` is the time-varying counterpart of `Transform`.
It holds `N ≥ 2` keyframes, each a full TRS decomposition

```
MotionKey { float Time; Vector3 Translate; Quaternion Rotate; Vector3 Scale; }
```

stored directly from the YAML (never recovered with `Matrix4x4.Decompose`, which
is lossy for the shear-free poses we build). At every `Hit` the object→world
matrix is rebuilt at the ray's time:

- **Translate / Scale** interpolate linearly (`Vector3.Lerp`).
- **Rotate** interpolates on the quaternion shortest arc (`Quaternion.Slerp`), so
  a key spinning `0° → 350°` sweeps `−10°` rather than the long way around.
- The pose composes in the **same order as the static path** —
  `scale · rotate · translate` (row-vector convention) — and keyframe rotations
  are built as `CreateFromRotationMatrix(Rx·Ry·Rz)` so that *identical* keyframes
  reproduce a static `Transform` bit-for-bit (verified by the equivalence tests).

The hit itself reuses the static-transform machinery: a shared
`Transform.ToLocalRay` maps the world ray into object space (carrying time and
differentials), and `Transform.MapHitToWorld` maps the hit record back (point,
normal via the transpose-inverse, tangent/bitangent, parametric partials,
metric-object-space `LocalPoint`). Factoring these out means the static and
animated paths can never drift.

**Cost.** Each `Hit` on an animated node does one matrix compose + one
`Matrix4x4.Invert` + one transpose. Motion-blurred objects are normally a small
fraction of a scene, so no per-thread matrix caching is attempted (a natural
future optimization). When every keyframe is the same pose the constructor
detects it and falls back to precomputed static matrices.

---

## 3. Conservative bounds over the static BVH

The BVH is built once and stays static. `AnimatedTransform.BoundingBox()`
therefore has to return a box that contains the swept geometry over the *whole*
key range, so the acceleration structure never culls a ray that truly hits the
object at some time. The box is the union of the world-space bounds sampled along
each segment:

- **Translate / scale segments** move box corners along straight lines, and the
  union of AABBs is convex, so the two endpoint boxes suffice.
- **Rotating segments** move corners along arcs that bulge outside the chord
  between sampled poses. Such a segment is sampled every ≤ 15° of rotation, and
  every sampled box is then padded by the maximal sagitta

  ```
  pad = r_max · (1 − cos(θ_step / 2))
  ```

  where `r_max` is the farthest inner-box corner from the object origin under the
  largest keyframe scale and `θ_step` the per-step rotation angle. This is the
  largest distance an arc of `θ_step` can stray from its chord at radius `r_max`,
  so the union is guaranteed conservative.

`AnimatedTransformTests.BoundingBox_NeverCullsATrueHit_*` fire thousands of
seeded rays at random times (including a large-rotation case) and assert that a
true hit always implies a box hit.

An `AnimatedTransform` wrapping an `InfinitePlane` is routed to the linear
(non-BVH) list exactly like a static one — its unbounded box would otherwise
poison the BVH (`SceneLoader.IsInfinitePlane` and `Group`'s private copy both
recurse through the animated wrapper).

---

## 4. Lighting under motion

**Shadow / NEE rays** carry the path time, so occlusion is tested against the
scene at the correct instant — a moving occluder casts a moving shadow. Every
`ILight.IlluminateAndTest(…, float time)` implementation stamps the time onto its
shadow ray, and the transparent-shadow walker (`ShadowRay.Transmittance`) inherits
it for the whole traversal.

**Animated emitters** need care: a `GeometryLight` samples a *static* surface, so
an animated emissive entity is registered for NEE at one fixed **mid-animation
snapshot** (`AnimatedTransform.NeeSnapshotMatrix`). Sample positions, the
solid-angle pdf and the power estimate all share that one self-consistent pose,
which keeps the MIS weights correct; meanwhile shadow rays still run at the true
ray time and a BSDF ray that *hits* the emitter sees its real, blurred motion.
The trade-off is a slight positional bias of the direct-light contribution for
fast-moving emitters — a deliberate, documented limitation flagged with a load
warning.

**Caustics.** The photon pre-pass builds one static photon map. When motion blur
is active the photons are traced at the shutter midpoint (a notice is printed);
distributing photon times would require a per-time photon map for a correct
density estimate.

---

## 5. Camera motion blur

A camera `motion:` list keyframes the look-from / look-at / up / fov. The base
pose is the implicit key at `time = 0`. When animated, `Camera.GetRay` interpolates
the pose at the ray's time and rebuilds the orthonormal basis per ray (a small
`Basis` value type extracted from the constructor); interpolating the *inputs*
rather than the basis vectors keeps the frame orthonormal at every instant. The
still-camera fast path is untouched — it uses the cached basis and only stamps the
time onto the ray, so a static camera stays bit-identical.

---

## 6. Code map

| Concern | File |
|---|---|
| Ray time field + `WithTime` | `Core/Ray.cs` |
| Hit time field | `Core/HitRecord.cs` |
| Animated transform, interpolation, bounds | `Geometry/AnimatedTransform.cs` |
| Shared local-ray / hit-record mapping | `Geometry/Transform.cs` |
| Camera shutter time + camera motion | `Camera/Camera.cs` |
| Shutter settings struct | `Rendering/MotionBlurSettings.cs` |
| Time draw + propagation | `Rendering/Renderer.cs` |
| Time-aware shadow tests | `Lights/*.cs`, `Geometry/ShadowRay.cs` |
| YAML schema + keyframe build | `Scene/SceneData.cs`, `Scene/SceneLoader.cs` |
| Photon-map snapshot time | `Rendering/CausticPhotonTracer.cs` |
| Tests | `RayTracer.Tests/AnimatedTransformTests.cs`, `MotionBlurTests.cs` |

---

## 7. Limitations and future work

- **Transform motion only.** No deforming/per-vertex geometry animation.
- **Animated emitters** use a mid-shutter NEE snapshot (above).
- **Per-hit matrix invert** on animated nodes — cacheable per thread if heavily
  animated scenes ever warrant it.
- **Caustics** use a single mid-shutter photon map.

See the `motion-blur-showcase.yaml` (didactic) and
`motion-blur-billiard-showcase.yaml` (cinematic break shot) scenes under
`scenes/showcases/`.
