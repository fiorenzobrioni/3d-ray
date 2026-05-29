# Chapter 11: Surfaces of Revolution (Lathe)

Every primitive you have met so far is defined by a handful of numbers:
a centre and a radius, a pair of corners, maybe a top radius for a
cone. That is enough for billiards, architecture, and chess pieces --
but it breaks the moment you want to render a **ceramic vase**, a
**wine glass**, a **turned column**, or a **chess bishop with a
curved body**. Those objects have an outline that varies continuously
along the axis, and no fixed-parameter primitive can describe them.

The classical answer is the **surface of revolution**: take a 2D
profile drawn in the `(r, y)` plane and spin it 360° around the Y
axis. The result is an analytic surface that can be as simple as a
cylinder or as ornate as a Ming vase -- all from a short list of
points.

3D-Ray exposes this as the `lathe` primitive. Unlike a tessellated
mesh, it is an **implicit surface** -- the ray tracer intersects the
mathematical equation directly, with no polygons in between. The
silhouette stays smooth at any zoom level, and the file you author
is measured in lines, not megabytes.

This chapter covers the three profile modes (`linear`, `catmull_rom`,
`bezier`), the design rules that keep profiles well-behaved, and the
cost model you need to choose the right mode for the job.

---

## 11.1 Mental Model: The Profile in 2D

A lathe is defined by a **2D profile curve** in the right half-plane
`r >= 0, y ∈ ℝ`. Each profile point is a pair `[r, y]`:

- `r` is the distance from the Y axis (the radius at that height)
- `y` is the height along the axis

When the renderer processes the lathe, it mathematically rotates this
curve around the Y axis:

```
profile point (r, y)    →    circle { (r·cos θ, y, r·sin θ)  |  θ ∈ [0, 2π) }
```

The entire 3D surface is the union of these circles for every point
of the profile.

**Rules of the profile** (the loader enforces them and warns on
violations):

1. **`r >= 0`** -- negative radii are meaningless (they would fold
   the profile across the axis).
2. **`y` is monotonically non-decreasing** -- the profile must move
   upward (or stay flat) as you list the points. A profile that goes
   up then down would produce a self-intersecting surface. If your
   points are out of order, the loader sorts them by `y` and emits
   a warning.
3. **At least 2 points** -- a single point is not a profile.
4. **`r = 0` closes that end** -- when the first or last point has
   `r = 0`, the profile *touches the axis* and the corresponding end
   cap is implicit (the surface is already closed). When `r > 0` at
   an end, a flat disc cap is added automatically.

Once you internalise that picture -- a curve in the meridian plane,
spun around Y -- the three profile modes are just different ways of
drawing that curve.

---

## 11.2 Linear Profile -- The Turned Column

The `linear` mode treats the profile as a **polyline**: consecutive
points are joined by straight segments, and each segment becomes a
**truncated cone** (a frustum) after revolution.

```yaml
- name: "column"
  type: "lathe"
  profile_type: "linear"                # default -- can be omitted
  material: "marble"
  translate: [0, 0, 0]
  profile:
    - [0.30, 0.0]                       # base
    - [0.30, 0.1]                       # base plinth
    - [0.25, 0.2]                       # neck of the base
    - [0.28, 2.0]                       # shaft
    - [0.35, 2.1]                       # capital
```

| Parameter       | Default  | Description                                  |
|-----------------|----------|----------------------------------------------|
| `profile_type`  | `linear` | Interpolation mode                           |
| `profile`       | --       | List of `[r, y]` points, minimum 2            |
| `material`      | --       | Applied uniformly to the whole surface        |
| `center` / `translate` / `rotate` / `scale` | -- | Standard primitive transforms |

**When to use it**

- Turned wooden furniture legs, balusters, candlesticks -- anything
  where a visible faceted transition *is* the look.
- High-poly-count profiles where you want each segment to be
  geometrically distinct.
- Maximum speed: the intersection of a ray with a frustum is a
  quadratic equation -- the same cost as a cone.

**What it looks like**

At every profile point the normal changes abruptly: light bounces
differently on either side of the vertex. This is exactly what a
real lathe tool produces when you stop at a corner. For smooth,
vase-like shapes use one of the spline modes below.

---

## 11.3 Catmull-Rom Profile -- The Ceramic Vase

`catmull_rom` is the mode you want when the profile has to look
**smooth** but you still want to author it by listing the points the
surface should pass through. It uses **centripetal Catmull-Rom**
interpolation (Yuksel et al. 2011), which has two important
properties:

1. It **passes through every control point** -- what you type is
   what you get, vertex by vertex.
2. It is **C¹ continuous and free of self-intersections** even when
   two points are very close together -- the centripetal
   parametrisation kills the classical "overshoot" that plagues
   uniform Catmull-Rom.

```yaml
- name: "vase"
  type: "lathe"
  profile_type: "catmull_rom"           # aliases: "catmull", "smooth"
  material: "porcelain"
  profile:
    - [0.00, 0.00]                      # closed bottom (on the axis)
    - [0.30, 0.00]
    - [0.35, 0.10]
    - [0.55, 0.40]                      # belly of the vase
    - [0.40, 0.80]                      # neck starts
    - [0.50, 0.95]                      # lip flares out
    - [0.00, 0.95]                      # closed top (on the axis)
```

The loader turns each consecutive pair of profile points into a
**cubic Bezier segment**, deriving the inner control points from the
centripetal tangent rule. Internally, Catmull-Rom and Bezier modes
share the same segment implementation -- only the setup differs.

**When to use it**

- Ceramics, glassware, metal bowls, finials, urns.
- Any profile where you prefer to describe the shape as "here are
  the silhouette points" rather than "here are the control handles".
- Organic, hand-modelled shapes where tuning tangents by hand would
  be painful.

**Constraints**

- At least **4 points** are needed to define interior tangents. If
  you supply 2 or 3, the loader silently downgrades the shape to
  `linear` and warns you.
- Phantom endpoints are reflected across the first/last vertex, so
  tangent behaviour at the ends is natural (the curve leaves the
  endpoint in the direction of the adjacent segment).

---

## 11.4 Bezier Profile -- Total Control

When you already have a hand-authored Bezier curve -- from a vector
editor, a CAD export, or a published formula -- use `bezier` mode
and supply the four cubic control points for every segment
explicitly.

```yaml
- name: "bowl"
  type: "lathe"
  profile_type: "bezier"
  material: "glazed_ceramic"
  profile:                              # endpoints of each segment (N points = N-1 segments)
    - [0.0, 0.0]
    - [0.5, 0.3]
    - [0.5, 0.6]
  profile_bezier_controls:              # exactly 4 × (N - 1) points, concatenated
    - [0.0, 0.0]                        # segment 1, P0
    - [0.3, 0.0]                        #            P1
    - [0.5, 0.1]                        #            P2
    - [0.5, 0.3]                        #            P3  (== segment 2, P0)
    - [0.5, 0.3]                        # segment 2, P0
    - [0.5, 0.45]                       #            P1
    - [0.5, 0.5]                        #            P2
    - [0.5, 0.6]                        #            P3
```

**Rules**

- `profile` lists the **endpoint of each segment** -- N points define
  N-1 segments.
- `profile_bezier_controls` lists **four control points per
  segment**, concatenated. For N endpoints that is `4 × (N - 1)`
  entries. A mismatched count makes the loader reject the entity and
  fall back to a grey Lambertian so you notice immediately.
- For **C¹ continuity** across segments, make sure
  `P3(segment k) == P0(segment k+1)` *and*
  `P3(k) - P2(k) == P1(k+1) - P0(k+1)` (the outgoing tangent equals
  the incoming tangent in both magnitude and direction). Breaking
  this rule gives you a sharp corner on purpose -- useful for crisp
  lips, moulding, and shouldered profiles.

**When to use it**

- You already have Bezier control data (SVG, Illustrator, a CAD
  tool).
- You want to author deliberate cusps and tangent breaks that
  Catmull-Rom would smooth away.
- You are reproducing a published shape defined in terms of Bezier
  controls (typography, classical vase taxonomies, etc.).

---

## 11.5 End Caps and Closed Profiles

Every lathe has at most two **cap discs** -- flat circles that close
the top and bottom:

- The bottom cap is added at `y = y_first` when `r_first > 0`.
- The top cap is added at `y = y_last` when `r_last > 0`.
- When `r = 0` at an end, the profile already meets the axis, the
  surface is geometrically closed, and no cap is drawn.

This gives you two idioms for closed shapes:

```yaml
# Closed via caps (flat bottom, flat top)
profile:
  - [0.5, 0.0]
  - [0.6, 0.5]
  - [0.5, 1.0]

# Closed via the axis (rounded like a lemon)
profile:
  - [0.00, 0.0]
  - [0.35, 0.2]
  - [0.50, 0.5]
  - [0.35, 0.8]
  - [0.00, 1.0]
```

The two profiles above render as very different solids: the first has
two visible disc faces, the second has none.

---

## 11.6 A Complete Scene: Showcase of the Three Modes

The repository ships a reference scene at
`scenes/showcases/primitive-lathe.yaml` that puts all three modes on
the same stage. Here is a distilled version you can copy into a new
file:

```yaml
camera:
  position: [0, 3.5, -9]
  look_at: [0, 1.2, 0]
  fov: 42

world:
  sky:
    type: "gradient"
    zenith_color: [0.04, 0.05, 0.10]
    horizon_color: [0.12, 0.12, 0.18]

materials:
  - id: "floor"
    type: "disney"
    roughness: 0.85
    texture:
      type: "checker"
      colors: [[0.10, 0.10, 0.11], [0.22, 0.22, 0.23]]
      scale: 0.7
  - id: "porcelain"
    type: "disney"
    color: [0.94, 0.92, 0.88]
    roughness: 0.25
    specular: 0.6
  - id: "marble"
    type: "disney"
    color: [0.88, 0.85, 0.80]
    roughness: 0.4
  - id: "glow"
    type: "emissive"
    emission: [6.0, 4.0, 1.8]

entities:
  # Smooth vase -- Catmull-Rom
  - name: "vase"
    type: "lathe"
    profile_type: "catmull_rom"
    material: "porcelain"
    translate: [-2.2, 0, 0]
    profile:
      - [0.00, 0.00]
      - [0.30, 0.00]
      - [0.35, 0.10]
      - [0.55, 0.40]
      - [0.40, 0.80]
      - [0.50, 0.95]
      - [0.00, 0.95]

  # Faceted column -- Linear
  - name: "column"
    type: "lathe"
    profile_type: "linear"
    material: "marble"
    profile:
      - [0.30, 0.0]
      - [0.30, 0.1]
      - [0.25, 0.2]
      - [0.28, 2.0]
      - [0.35, 2.1]

  # Emissive bowl -- Bezier (lights the scene via NEE)
  - name: "bowl"
    type: "lathe"
    profile_type: "bezier"
    material: "glow"
    translate: [2.2, 0.5, 0]
    profile:
      - [0.0, 0.0]
      - [0.5, 0.3]
      - [0.5, 0.6]
    profile_bezier_controls:
      - [0.0, 0.0]
      - [0.3, 0.0]
      - [0.5, 0.1]
      - [0.5, 0.3]
      - [0.5, 0.3]
      - [0.5, 0.45]
      - [0.5, 0.5]
      - [0.5, 0.6]
```

Render it:

```
dotnet run --project src/RayTracer -c Release -- \
  -i scenes/showcases/primitive-lathe.yaml -o renders/lathe.png \
  -w 1280 -H 720 -s 256 -d 8
```

You should see: a smooth ceramic vase on the left (no faceting at
any zoom), a visibly faceted marble column in the middle (each
profile vertex produces a ring of shading discontinuity), and a
glowing Bezier bowl on the right that illuminates the floor without
any extra area light -- the lathe itself is the light source.

---

## 11.7 UV Mapping and Textures

Lathes follow the same UV conventions as `cylinder` and `cone` so
materials designed for those primitives transfer without changes:

- **U** is the **azimuthal angle** around the Y axis, wrapped into
  `[0, 1)` -- seamlessly tileable.
- **V** is the **cumulative arc length** along the profile,
  normalised to `[0, 1]`. For a linear profile this is piecewise
  linear in `y`. For a spline profile the arc length is precomputed
  with 8-point Gauss-Legendre quadrature per segment, which reaches
  relative error below `1e-6` for any cubic.

In practice: a checker texture on a vase wraps evenly around the
belly, and a striped wood texture on a column runs along the height
with the stripes packed closer where the radius shrinks (true to
how a tape would follow the surface).

---

## 11.8 Lathes as Light Sources

If the material assigned to a lathe is `emissive`, the primitive
joins the **next-event estimation (NEE)** pool automatically -- the
same mechanism used by area and sphere lights. No extra
configuration is needed.

Internally the sampler walks an area-weighted CDF across all
segments and caps, so each point on the lathe surface is picked with
density proportional to its contribution to the total emitted
energy. The result is **noise-free direct lighting** from curved
emissive objects: neon signs, glowing pottery, fluorescent lamp
housings. See Chapter 6 for more on how NEE interacts with the
other light types.

---

## 11.9 How the Ray Intersection Works (and Why It Matters)

You do not need to understand this section to use the feature, but
the cost model it explains will help you size your scenes.

For a **linear** profile each segment is a frustum, and ray-frustum
intersection reduces to a **quadratic equation** -- identical to
`cone`. Cheap, exact, and has been in computer graphics since the
1970s.

For a **spline** profile (`catmull_rom` or `bezier`) each segment is
a cubic curve in the meridian plane. Revolving a cubic around Y and
intersecting with a line yields a **polynomial of degree 6** in the
curve parameter `u`. There is no closed-form solution beyond degree
4, so 3D-Ray solves it numerically using the approach taken by
PovRay's `lathe` and PBRT's hair `Curve` primitive:

1. **Build a Sturm chain** from the degree-6 polynomial and its
   derivatives.
2. **Isolate each real root** in `[0, 1]` by counting sign changes
   in the chain (Sturm's theorem guarantees the count).
3. **Refine** each isolated root with **Newton-Raphson**, falling
   back to bisection if Newton leaves the bracket.
4. **Reconstruct** `t`, the ray parameter, from the accepted `u`.

The solver is implemented in `Core/SturmSolver.cs` as a
general-purpose polynomial root finder you could reuse for other
implicit surfaces.

**Cost model**

- `linear` segment: about the same as a `cone` -- comparable
  quadratic.
- Spline segment: roughly **10× a cone hit**. Still fast in
  absolute terms (a few hundred nanoseconds), but it adds up across
  many samples per pixel.

A per-segment AABB is precomputed from the control points plus the
derivative zeros of `Y(u)` and `R(u)`, so segments outside the
current ray are pruned almost for free -- the 10× cost only applies
to segments the ray actually touches.

---

## 11.10 Choosing the Right Mode

| Want                                              | Use             |
|---------------------------------------------------|-----------------|
| Maximum speed, faceted look is fine               | `linear`        |
| Smooth silhouette, author by silhouette points    | `catmull_rom`   |
| Smooth silhouette, need deliberate sharp corners  | `bezier`        |
| Convert an SVG/Illustrator path                   | `bezier`        |
| Hand-tuned tangent control                        | `bezier`        |
| Reproduce a turned chair leg, chess rook, balustrade | `linear`     |
| Reproduce a ceramic vase, wine glass, goblet      | `catmull_rom`   |

Two rules of thumb when in doubt:

1. If you would model the object on a **physical lathe** (one pass
   with a straight tool), use `linear`.
2. If you would model it by **throwing clay on a wheel** and pulling
   the shape up continuously, use `catmull_rom`.

---

## 11.11 Troubleshooting

**The silhouette is jagged but I used `catmull_rom`.**
Check the number of profile points -- Catmull-Rom needs at least 4.
With 2 or 3, the loader silently switches to `linear`. Read the
loader warnings printed after "Loading scene... done" to confirm.

**My profile self-intersects / renders inside-out.**
Non-monotonic `y` will be sorted by the loader, which can make
"doubling back" shapes look nothing like you expected. Lathe
profiles must be **single-valued functions of y** in the `(r, y)`
plane. If you really need a self-intersecting silhouette (a torus,
a figure-eight), model it as CSG instead.

**Bezier bowl has a visible crease at the segment boundary.**
Your adjacent segments break tangent continuity. For a smooth
transition, align the last two controls of segment *k* with the
first two controls of segment *k+1* as described in 11.4. For a
deliberate crease, you can leave them misaligned.

**My emissive lathe does not cast noticeable light.**
The total emitted power scales with the **surface area**, not with
how the profile looks. A tall thin lathe with `r` near zero has
very little area -- scale up `emission` or widen the profile.

**Renders are 5-10× slower than I expected.**
Spline segments are ~10× the cost of a cone hit. If the profile has
many segments or you use lots of spline lathes, consider: (a)
switching to `linear` for objects that are small on screen, (b)
reducing total profile points (more subdivisions are rarely
perceptible past 10-12 points), (c) using Preview quality while
iterating on composition.

---

## What You Have Learned

- Lathes revolve a 2D `(r, y)` profile around the Y axis to produce
  an analytic surface of revolution, with no tessellation.
- Three modes cover the full design space: `linear` (faceted,
  fast), `catmull_rom` (smooth, passes through every point),
  `bezier` (explicit control handles).
- End caps are added automatically when the profile does not touch
  the axis at the ends; `r = 0` closes that end mathematically.
- UV follows cylinder/cone conventions: U is azimuth, V is
  normalised cumulative arc length -- textures wrap naturally.
- Emissive lathes join the NEE pool automatically and sample with
  area-weighted density.
- Spline intersections are solved with a Sturm chain + Newton
  hybrid (~10× the cost of a cone). Linear is quadratic, the same
  cost as a cone.
- Choose mode by how you *think* about the object: machined (linear)
  versus sculpted (spline).

---

[Previous: Presets and Projects](./10-libraries-and-projects.md) | [Tutorial Index](./README.md) | [Next: Extruded Profiles (Extrusion)](./12-extrusion-2d-profiles.md)
