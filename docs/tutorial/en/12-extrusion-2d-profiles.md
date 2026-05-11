# Chapter 12: Extruded Profiles (Extrusion)

The previous chapter introduced the `lathe`: take a 2D profile and spin
it around an axis. That covers everything you would put on a potter's
wheel — vases, glasses, columns — but a huge family of real-world
objects is built the *other* way: they have a constant cross-section
that runs straight along an axis, like cookie cutters pushed into a
block of dough.

Think about it for a second. A **steel I-beam** has the same `I` shape
along its whole length. A **gear** has the same toothed silhouette top
to bottom. A **hexagonal pencil**, a **honeycomb cell**, a **cookie**, a
**heraldic shield**, the **letter A** in a 3D logo, a **rounded
medallion**, an **L-shaped concrete pillar**, a **window frame**, a
**washer with a square hole** — none of these can be turned on a lathe,
but every one of them can be described by drawing the cross-section
once and saying "now stretch this for half a meter".

This is exactly what 3D-Ray's `extrusion` primitive does.

You author a closed 2D loop in the XZ plane and tell the engine how
tall to make it. Out comes a prism with side walls and end caps, ready
to render. No external mesh, no manual triangulation, no boolean
trickery. And — unlike a real cookie cutter — the cross-section can be
**concave**: stars, gears, plus signs, letters, logos, anything you can
draw without crossing lines, the engine's automatic ear-clipping
triangulator handles for you.

This chapter walks through the three profile modes (`linear`,
`catmull_rom`, `bezier`), the optional `twist` and `taper` modifiers
that turn a boring straight prism into a sculpted column, the
end-cap rules, and a complete showcase scene you can copy and start
modifying immediately.

---

## 12.1 Mental Model: A Profile in the XZ Plane

An extrusion is defined by a **closed 2D profile** in the XZ plane.
Each profile point is a pair `[x, z]` — the loop you draw in the
floorplan view, looking down from the +Y axis.

The engine then takes that flat shape and **stretches it along +Y**
from `y = 0` to `y = height`. Visually:

```
profile point (x, z)    →    vertical line { (x, y, z)  |  y ∈ [0, height] }
```

The full 3D surface is the union of:
- the **side walls** — one vertical strip per profile edge,
- the **bottom cap** at `y = 0`,
- the **top cap** at `y = height`.

**Rules of the profile** (the loader enforces them and warns on
violations):

1. **At least 3 points** — a profile is a polygon, so you need at
   minimum a triangle.
2. **The loop is implicitly closed** — do *not* repeat the first point
   at the end. If you do, the loader silently drops the duplicate.
3. **No self-intersection** — the polygon's edges must not cross
   themselves. Figure-eights would produce undefined caps.
4. **Orientation is auto-corrected** — counter-clockwise (CCW) winding
   is the canonical convention (interior to the left of each edge).
   If you author your profile clockwise, the loader silently reverses
   it so the wall normals still point outward. You don't have to
   worry about it.
5. **Concave is fine** — stars, L-shapes, gears all work. The cap
   triangulator (ear clipping) handles concave-but-simple polygons
   robustly.

Once you internalise the picture — a 2D outline pushed straight up —
the three profile modes are just different ways of drawing that
outline.

---

## 12.2 Linear Profile — The Faceted Prism

The `linear` mode treats the profile as a **polyline**: consecutive
points are joined by straight edges, and each edge becomes a flat
vertical wall after extrusion. Vertex normals are sharp at every
profile vertex, so the silhouette has crisp ridges — exactly the look
of an aluminium extrusion or a CNC-cut acrylic shape.

```yaml
- name: "star"
  type: "extrusion"
  profile_type: "linear"                # default — can be omitted
  height: 0.4
  caps: "both"
  material: "gold"
  profile:                              # 5-pointed star, 10 vertices
    - [ 0.55,  0.00]
    - [ 0.18,  0.13]
    - [ 0.17,  0.52]
    - [-0.07,  0.21]
    - [-0.44,  0.32]
    - [-0.22,  0.00]
    - [-0.44, -0.32]
    - [-0.07, -0.21]
    - [ 0.17, -0.52]
    - [ 0.18, -0.13]
```

| Parameter        | Default  | Description                                                |
|------------------|----------|------------------------------------------------------------|
| `profile_type`   | `linear` | Interpolation mode                                         |
| `profile`        | --       | Closed list of `[x, z]` points, minimum 3                  |
| `height`         | `1`      | Length of the extrusion along +Y                           |
| `caps`           | `both`   | `both` / `start` / `end` / `none`                          |
| `twist_degrees`  | `0`      | Total rotation of the top profile around Y, in degrees     |
| `taper`          | `1`      | Uniform XZ scale at the top (1 = straight, < 1 narrows)    |
| `curve_samples`  | `16`     | Polyline resolution for curved modes (ignored by `linear`) |
| `crease_angle`   | `0`      | Normal-blend threshold for `linear` walls, degrees (0 = flat) |
| `material`       | --       | Applied uniformly to walls and caps                        |
| `center` / `translate` / `rotate` / `scale` | -- | Standard primitive transforms     |

**When to use it**

- Industrial / architectural sections (I-beams, U-channels, T-bars,
  L-angles, cornices, skirting boards).
- Heraldic shapes: stars, crosses, shields, banners.
- Gears, sprockets, ratchets — the toothed silhouette is the whole
  point and you want every tooth crisp.
- Logo extrusions, 3D typography (most letters are concave-but-simple
  polygons — no special handling needed).
- Any time a real-world object would be **machined or cut** rather
  than sculpted.

**What it looks like**

Hard ridges at every profile vertex, identical to what a CNC mill or a
cookie cutter produces. If you want a smooth silhouette but need to keep
the polygon as-is, use `crease_angle` below. If you want the engine to
produce the smooth outline from fewer control points, use one of the
spline modes below.

### Smoothing Linear Profiles with `crease_angle`

A `linear` profile defaults to fully flat-shaded normals — ideal for
machined parts, but a 12-sided polygon approximating a circle will show
12 visible facets in highlights. `crease_angle` solves this without
switching profile mode:

```yaml
- name: "round_column"
  type: "extrusion"
  profile_type: "linear"
  height: 2.0
  crease_angle: 40            # blend normals on edges below 40°
  caps: "both"
  material: "plaster"
  profile:
    - [ 1.000,  0.000]
    - [ 0.866,  0.500]
    - [ 0.500,  0.866]
    - [ 0.000,  1.000]
    - [-0.500,  0.866]
    - [-0.866,  0.500]
    - [-1.000,  0.000]
    - [-0.866, -0.500]
    - [-0.500, -0.866]
    - [ 0.000, -1.000]
    - [ 0.500, -0.866]
    - [ 0.866, -0.500]
```

**How it works**: at every profile vertex the engine measures the
dihedral angle between the two adjacent wall faces. If the angle is
*below* `crease_angle`, the vertex gets a blended normal (smooth
shading — the edge vanishes in specular highlights). If the angle is
*above* `crease_angle`, each face keeps its own flat normal (hard edge
— the ridge stays crisp).

**Choosing a threshold**:

| `crease_angle` | Effect |
|----------------|--------|
| `0` (default)  | Fully faceted — every edge is a hard ridge. |
| `30°`          | Smooths curves approximated by polylines; preserves 90° corners (letters, gears, channels). |
| `45°`          | Same as 30° but also softens 45° chamfers. |
| `90°`          | Smooth everywhere except right-angle corners — typical DCC default. |
| `180°`         | Fully smooth regardless of corner angle. |

30° (Blender's and Maya's classic default) is the best starting point for
any polyline-approximated rounded shape: a 12-sided circle, an 8-lobed
column cross-section, a rounded hexagon.

**`crease_angle` vs `catmull_rom`**: use `crease_angle` when the polygon
already has the right shape (exported from CAD, traced from an SVG, or
authored with a known number of sides) and you only want to fix the
shading. Use `catmull_rom` when you want the engine to produce a smooth
silhouette from fewer authoring points.

---

## 12.3 Catmull-Rom Profile — The Sculpted Column

`catmull_rom` is the mode you want when the profile has to look
**smooth** and you want to author it by listing the points the curve
should pass through. It uses the same centripetal Catmull-Rom
interpolation as the lathe — the curve passes through every control
point, is C¹ continuous, and never overshoots.

```yaml
- name: "fluted_column"
  type: "extrusion"
  profile_type: "catmull_rom"           # aliases: "catmull", "smooth"
  height: 3.4
  twist_degrees: 60                     # gentle spiral up the column
  taper: 0.88                           # narrows slightly at the top
  curve_samples: 24                     # smoother silhouette
  caps: "both"
  material: "marble"
  profile:                              # 16-lobed cross-section
    - [ 0.55,  0.000]
    - [ 0.43,  0.180]
    - [ 0.39,  0.390]
    - [ 0.30,  0.300]
    - [ 0.000, 0.55]
    - [-0.300, 0.300]
    - [-0.39,  0.390]
    - [-0.43,  0.180]
    - [-0.55,  0.000]
    - [-0.43, -0.180]
    - [-0.39, -0.390]
    - [-0.300,-0.300]
    - [ 0.000,-0.55]
    - [ 0.300,-0.300]
    - [ 0.39, -0.390]
    - [ 0.43, -0.180]
```

The loader densifies each input edge into `curve_samples` little
straight segments before triangulating, and emits **smooth-shaded**
side walls so the silhouette reads as a continuous curve at any zoom
level.

**When to use it**

- Sculpted columns, decorative balusters, ornamental moldings.
- Soft organic shapes (a leaf cross-section, a peanut, a bean,
  a cloud-shaped patch of grass).
- Any cross-section you would draw freehand by clicking points
  along the silhouette.

**Constraints**

- At least **3 points** are needed (with 3 points you essentially get
  a smooth triangle — uncommon but valid).
- `curve_samples` defaults to 16 per input segment. Bump it to 24 or
  32 for hero-shot close-ups; lower it to 8 for distant or crowded
  objects to keep the BVH compact.

---

## 12.4 Bezier Profile — Total Control

When you already have a hand-authored Bezier curve — exported from
Illustrator, a CAD package, an SVG file, or a published shape spec —
use `bezier` mode and supply the four cubic control points for every
segment explicitly. The profile is interpreted as a **closed loop**:
the last segment wraps back to the first vertex.

```yaml
- name: "shield"
  type: "extrusion"
  profile_type: "bezier"
  height: 0.3
  caps: "both"
  material: "emerald_glass"
  profile:                              # endpoints of each segment (N segments, closed)
    - [ 0.6,  0.0]
    - [ 0.0,  0.7]
    - [-0.6,  0.0]
    - [ 0.0, -0.5]
  profile_bezier_controls:              # exactly 4 × N points, concatenated
    # Segment 1: right → top
    - [ 0.60,  0.00]                    # P0 (== profile[0])
    - [ 0.60,  0.42]                    # P1
    - [ 0.36,  0.70]                    # P2
    - [ 0.00,  0.70]                    # P3 (== profile[1])
    # Segment 2: top → left
    - [ 0.00,  0.70]
    - [-0.36,  0.70]
    - [-0.60,  0.42]
    - [-0.60,  0.00]
    # Segment 3: left → bottom
    - [-0.60,  0.00]
    - [-0.60, -0.30]
    - [-0.32, -0.50]
    - [ 0.00, -0.50]
    # Segment 4: bottom → right (closes the loop)
    - [ 0.00, -0.50]
    - [ 0.32, -0.50]
    - [ 0.60, -0.30]
    - [ 0.60,  0.00]
```

**Rules**

- `profile` lists the **endpoint of each segment** — N points define
  N segments. There is no "open profile" for `bezier`: the last
  segment always closes the loop back to `profile[0]`.
- `profile_bezier_controls` lists **four control points per segment**,
  concatenated. For N endpoints that is `4 × N` entries (note: this
  differs from the lathe's `4 × (N-1)`, because the extrusion loop is
  closed). A mismatched count makes the loader reject the entity with
  a deferred warning.
- For **C¹ continuity** across segments, make sure
  `P3(segment k) == P0(segment k+1)` *and* the outgoing tangent
  matches the incoming tangent (`P3(k) - P2(k) == P1(k+1) - P0(k+1)`).
  Breaking this rule gives you an **intentional sharp corner** —
  perfect for crisp tips, gem facets, or angular emblems.

**When to use it**

- You already have Bezier control data (SVG, Illustrator, CAD).
- You want to reproduce a published shape defined by Bezier
  controls (typography, classical ornaments, brand marks).
- You want explicit control over tangent direction and magnitude —
  for instance to give a heart shape exactly the right swell and
  cusp at the bottom.

---

## 12.5 End Caps: Open Shells, Trays, and Solid Prisms

The `caps` parameter chooses which ends of the extrusion are closed by
a triangulated cap:

| Value     | Bottom cap | Top cap | Use case                                      |
|-----------|------------|---------|-----------------------------------------------|
| `both`    | yes        | yes     | Default. Solid prism, no holes anywhere.      |
| `start`   | yes        | no      | A tray, a basin, a mug body without a lid.    |
| `end`     | no         | yes     | A roof, a lid, a hat brim viewed from below.  |
| `none`    | no         | no      | A pure tube — useful for ribbons, cookie     |
|           |            |         | cutters, hollow CSG operands.                 |

A capped extrusion is a closed solid suitable as a CSG operand
(you can `subtract` a cylinder from it to drill a hole, for example).
A `caps: "none"` extrusion is a one-sided shell: rays can pass through
the missing ends, and the engine will see the inside of the walls.
That's exactly what you want for a window frame or a hollow tube, but
it disqualifies the extrusion from CSG use until you cap it.

---

## 12.6 Twist and Taper: From Prisms to Sculpted Columns

A straight extrusion looks like an aluminium profile. Add `twist` or
`taper`, and suddenly it can sculpt the kind of shapes that take a
multi-step modifier stack in Blender or a `polyextrude` chain in
Houdini.

### Twist

`twist_degrees` rotates the top of the extrusion around the Y axis
by the given angle, with the rotation interpolating linearly along
the height.

- A **square profile + 45° twist** turns a prism into a tapered
  spiral pillar.
- A **gear profile + 360° twist** turns a gear into a helical drill
  bit.
- A **scalloped profile + 60° twist** turns a column into the kind
  of corkscrew architectural feature you see in Antoni Gaudí's work.

Twist composes with cap triangulation transparently: the bottom cap
is triangulated for the un-twisted profile, and the top cap is
triangulated for the rotated copy. Both look correct.

### Taper

`taper` is a uniform XZ scale applied to the top profile relative
to the bottom:

- `taper: 1.0` — straight prism (default).
- `taper: 0.0` — degenerate point at the top (a true pyramid /
  pinnacle). In practice keep it slightly above zero (`0.01`) to
  avoid numerically zero-area triangles.
- `taper: 0.5` — top is half the size of the bottom (a frustum).
- `taper: 1.5` — top flares 50% wider than the bottom (an inverted
  pyramid, useful for inverted skirts and mushroom caps).

### Combining the two

Twist + taper together produce the entire family of **architectural
columns** (Solomonic, Gaudí, art-deco), **decorative balusters**,
**wrung handles**, **soft-serve ice-cream towers**, and **classical
finials** — all from a single primitive, no modifier stack required.

```yaml
# Solomonic column: square base, 180° helix up the height, narrows by 20% at the top
- name: "solomonic"
  type: "extrusion"
  profile_type: "linear"
  height: 4.0
  twist_degrees: 180
  taper: 0.80
  caps: "both"
  material: "marble"
  profile:
    - [ 0.5,  0.5]
    - [ 0.5, -0.5]
    - [-0.5, -0.5]
    - [-0.5,  0.5]
```

---

## 12.7 A Complete Scene: Showcase of the Three Modes

The repository ships a reference scene at
`scenes/showcases/extrusion-showcase.yaml` that puts all three modes
on the same stage. Here is a distilled version you can paste into a
new file:

```yaml
camera:
  position: [0, 2.0, -5.6]
  look_at: [0, 1.4, 0]
  fov: 42

world:
  sky:
    type: "gradient"
    zenith_color: [0.06, 0.08, 0.14]
    horizon_color: [0.20, 0.18, 0.22]
  ground:
    type: "plane"
    material: "floor"
    y: 0

materials:
  - id: "floor"
    type: "disney"
    texture:
      type: "checker"
      colors: [[0.92, 0.90, 0.85], [0.10, 0.10, 0.12]]
      scale: 1.5
    roughness: 0.18
  - id: "gold"
    type: "disney"
    color: [1.00, 0.78, 0.34]
    metallic: 1.0
    roughness: 0.22
  - id: "marble"
    type: "disney"
    color: [0.94, 0.93, 0.90]
    roughness: 0.15
    clearcoat: 0.8
  - id: "emerald"
    type: "disney"
    color: [0.22, 0.78, 0.42]
    roughness: 0.05
    spec_trans: 1.0
    ior: 1.55

lights:
  - type: "directional"
    direction: [0.45, -0.7, 0.55]
    color: [1.0, 0.92, 0.78]
    intensity: 2.6
    angular_radius_deg: 0.55
  - type: "point"
    position: [-4.5, 3.5, -3.0]
    color: [1.0, 0.74, 0.50]
    intensity: 22

entities:
  # Hero 1 — gold star (linear concave profile)
  - name: "star"
    type: "extrusion"
    profile_type: "linear"
    height: 0.18
    caps: "both"
    material: "gold"
    rotate: [90, 0, 0]
    translate: [-2.6, 1.55, 0]
    profile:
      - [ 0.55,  0.00]
      - [ 0.18,  0.13]
      - [ 0.17,  0.52]
      - [-0.07,  0.21]
      - [-0.44,  0.32]
      - [-0.22,  0.00]
      - [-0.44, -0.32]
      - [-0.07, -0.21]
      - [ 0.17, -0.52]
      - [ 0.18, -0.13]

  # Hero 2 — twisted scalloped marble column (catmull_rom + twist + taper)
  - name: "column"
    type: "extrusion"
    profile_type: "catmull_rom"
    height: 3.4
    twist_degrees: 60
    taper: 0.88
    curve_samples: 24
    caps: "both"
    material: "marble"
    translate: [0, 0, 0]
    profile:
      - [ 0.55,  0.000]
      - [ 0.000, 0.55]
      - [-0.55,  0.000]
      - [ 0.000,-0.55]

  # Hero 3 — emerald Bezier shield
  - name: "shield"
    type: "extrusion"
    profile_type: "bezier"
    height: 0.25
    caps: "both"
    material: "emerald"
    rotate: [90, 0, 0]
    translate: [2.6, 1.55, 0]
    profile:
      - [ 0.6,  0.0]
      - [ 0.0,  0.7]
      - [-0.6,  0.0]
      - [ 0.0, -0.5]
    profile_bezier_controls:
      - [ 0.60,  0.00]
      - [ 0.60,  0.42]
      - [ 0.36,  0.70]
      - [ 0.00,  0.70]
      - [ 0.00,  0.70]
      - [-0.36,  0.70]
      - [-0.60,  0.42]
      - [-0.60,  0.00]
      - [-0.60,  0.00]
      - [-0.60, -0.30]
      - [-0.32, -0.50]
      - [ 0.00, -0.50]
      - [ 0.00, -0.50]
      - [ 0.32, -0.50]
      - [ 0.60, -0.30]
      - [ 0.60,  0.00]
```

Render it:

```
dotnet run --project src/RayTracer -c Release -- \
  -i scenes/showcases/extrusion-showcase.yaml -o renders/extrusion.png \
  -w 1280 -H 720 -s 256 -d 8
```

You should see: a faceted gold star on the left (every concave notch
crisp, no holes in the cap), a twisted scalloped marble column in the
middle (the silhouette spirals visibly while narrowing at the top),
and a smooth emerald-glass shield on the right (rounded Bezier
silhouette, refraction tinting whatever is behind it).

---

## 12.8 UV Mapping and Textures

Extrusions emit UV coordinates that match how a designer would lay
out a wraparound texture on a real prismatic object:

- **U** is the **arc length along the profile**, normalised to
  `[0, 1]`. A texture you wrap around the side walls tiles seamlessly:
  `u = 0` is the first profile vertex, `u = 1` is back to the same
  vertex after one trip around the loop.
- **V** is the **height along the extrusion axis**, normalised to
  `[0, 1]`. `v = 0` is the bottom cap, `v = 1` is the top cap.
- **Caps** use barycentric UVs from the cap triangulation — fine
  for a uniform colour or a flat pattern, less ideal for a
  precisely registered logo. If you need a logo on the cap, project
  it with a `Quad` pinned slightly above the cap surface.

Two practical consequences:

- A **noise** or **marble** procedural texture wraps without any
  visible seam, regardless of how many profile vertices you have.
- A **striped wood** texture on a column runs parallel to the
  extrusion axis, with stripe density inversely proportional to the
  local profile width — exactly how grain reads on a real wooden
  pillar.

---

## 12.9 Extrusions as Light Sources

If the material assigned to an extrusion is `emissive`, the primitive
joins the **next-event estimation (NEE)** pool automatically — the
same mechanism used by the lathe and area lights. No extra
configuration is needed.

Internally the sampler walks an **area-weighted CDF** across all the
walls and cap triangles, so each point on the extrusion is picked
with density proportional to its contribution to the total emitted
energy. The result is **noise-free direct lighting** from concave
emissive shapes:

- A **star-shaped neon sign** glowing against a brick wall.
- A **letter "A"** glowing as part of a 3D logo.
- A **gear**-shaped emissive panel inside a steampunk control room.
- A **plus**-shaped first-aid sign over a hospital door.

All of these would be painful to author as classic area lights
(stacked rectangles, then composited) and trivial as a single
emissive extrusion.

---

## 12.10 How the Geometry Is Built Internally

You do not need to understand this section to use the feature, but
the cost model it explains will help you keep your scenes fast.

When you create an extrusion, the loader:

1. **Tessellates the profile** into a fine 2D polyline:
   - For `linear` it uses your points as-is.
   - For `catmull_rom` and `bezier` it samples each input segment
     into `curve_samples` small straight pieces.
2. **Auto-corrects the orientation** so the loop is counter-clockwise
   (interior on the left of every edge). This guarantees that wall
   outward normals point away from the interior.
3. **Builds the side walls** as a strip of triangles between a
   bottom ring (at `y = 0`) and a top ring (at `y = height`). For
   curved profiles the engine emits **smooth-shaded** triangles, so
   adjacent triangles share averaged normals at their common edge
   and the silhouette reads as a continuous curve at any zoom level.
   For `linear`, the engine emits **flat-shaded** triangles by default
   to keep ridges crisp; setting `crease_angle > 0` enables per-vertex
   normal blending at edges whose dihedral angle is below the threshold.
4. **Triangulates the caps** with the classical **ear-clipping**
   algorithm. Every `n`-vertex cap becomes `n - 2` triangles.
   Concave-but-simple polygons are handled correctly without
   manual decomposition.
5. **Wraps every triangle in an internal BVH** — exactly like an
   OBJ mesh. The outer scene BVH then sees the entire extrusion as
   a single leaf with one AABB, and the internal BVH handles fast
   traversal of the hundreds-to-thousands of triangles that a
   complex profile may produce.

**Cost model**

- A `linear` profile of N vertices produces `2N` wall triangles plus
  up to `2 × (N - 2)` cap triangles. A 5-pointed star (10 vertices)
  is ~36 triangles in total — comparable to a low-poly box.
- A `catmull_rom` or `bezier` profile of N vertices with
  `curve_samples = 16` produces ~`32N` wall triangles plus
  `2 × (16N - 2)` cap triangles. A 16-vertex scalloped column
  becomes ~1100 triangles — still a single BVH leaf for the outer
  scene, and traversal stays O(log) inside.

In practice: feel free to use extrusions liberally even for
hero-shot close-ups. The only knob worth tuning when you have many
of them is `curve_samples` — drop it to 8 for distant objects, raise
it to 24-32 for primary subjects.

---

## 12.11 Choosing the Right Mode and Modifiers

| Want                                                              | Use                                  |
|-------------------------------------------------------------------|--------------------------------------|
| Industrial profile (I-beam, channel, angle)                       | `linear`                             |
| Stars, crosses, plus signs, logos                                 | `linear` (concave is fine)           |
| Gears, sprockets, ratchets                                        | `linear`                             |
| 3D typography (letters as logos)                                  | `linear`                             |
| Sculpted columns, balusters, decorative moldings                  | `catmull_rom` + `twist` + `taper`    |
| Soft organic cross-sections (leaves, beans)                       | `catmull_rom`                        |
| Reproducing an SVG/Illustrator path or CAD curve                  | `bezier`                             |
| Heart, teardrop, deliberate cusp/tangent breaks                   | `bezier`                             |
| Cookie-cutter / stencil shapes                                    | `linear`, `caps: "none"`             |
| Helical drill bit                                                 | `linear`, `twist_degrees: 360+`      |
| Pyramids, obelisks, finials                                       | any mode, `taper: 0.0`–`0.3`         |
| Polyline polygon smoothed to read as a curved surface             | `linear` + `crease_angle: 30`        |

Two rules of thumb when in doubt:

1. If the cross-section has **distinct corners that should stay
   sharp**, use `linear`.
2. If you would describe the profile by saying "draw a smooth curve
   through these points", use `catmull_rom`. Reach for `bezier` only
   when you need explicit tangent control or are importing an
   existing curve from another tool.

---

## 12.12 Troubleshooting

**My concave profile renders with a hole in the cap.**
Double-check that the polygon is **simple** (no self-intersections).
The ear-clipping triangulator can handle any concave polygon as long
as its edges do not cross. If a mid-loop edge crosses the cap will
have spurious holes. Visualise the profile in 2D first — a tool like
GeoGebra or even a quick `matplotlib` plot makes the problem obvious.

**My linear profile looks faceted even though it approximates a smooth curve.**
Add `crease_angle: 30` to blend normals across the adjacent flat walls. This
makes polyline-approximated circles, rounded hexagons, and similar shapes look
smooth without switching to `catmull_rom`. Raise the value if ridges are still
visible; lower it if sharp corners you want to keep have started to soften.

**The silhouette is jagged but I used `catmull_rom`.**
Bump `curve_samples` from the default 16 to 24 or 32. The
default produces ~5° of arc per sample on a typical column, which
looks smooth in mid-shots but reads as faceted in tight close-ups.

**My twisted column shows visible facets winding around.**
Same fix: increase `curve_samples`. Twist makes the wall triangulation
visible because each ring of triangles is rotated relative to the next.
A column at `twist_degrees: 360` with `curve_samples: 8` will look
clearly faceted; the same column at `curve_samples: 24` looks smooth.

**My Bezier shield has a kink at one segment boundary.**
You broke C¹ continuity between two adjacent segments. Either align
the tangents as described in 12.4, or accept the kink as an
intentional design feature (perfect for shield tips and gem facets).

**My emissive extrusion does not light the scene as much as expected.**
The emitted power is proportional to the **total surface area** of
the extrusion (walls + caps). A thin or narrow extrusion has little
area. Either widen the profile, increase `height`, or scale up the
`emission` value on the material.

**The bottom cap is missing on a tall thin object.**
Make sure `caps` is `both` (the default) or `start`. Check the loader
warnings printed after "Loading scene... done" to see if the profile
was rejected for being degenerate (collinear points, near-zero area)
— in that case the cap triangulator bails out gracefully and only the
walls are emitted.

**The first cap triangle looks misplaced when I use rotation.**
Remember the transform order is `scale → rotate → translate` around
the **global** origin. If you use `center:` in combination with
`rotate:` you'll rotate around the world origin instead of the
extrusion's own center. Either omit `center:` and use `translate:`
for placement, or wrap the extrusion in a `group` whose transform
gives you the local pivot you want. (See chapter 5 for the full
discussion of transform order.)

---

## What You Have Learned

- `extrusion` (also known as `prism` or `linear_extrude`) sweeps a
  closed 2D profile in the XZ plane along the local +Y axis,
  producing a prism with side walls and optional end caps.
- Three profile modes mirror the lathe: `linear` for sharp ridges
  and faceted industrial / heraldic shapes, `catmull_rom` for smooth
  silhouettes authored by silhouette points, `bezier` for total
  control over tangent direction and magnitude.
- **Concave profiles work** out of the box thanks to ear-clipped
  caps — stars, gears, letters, L-shapes are all single-primitive
  jobs.
- `caps: both | start | end | none` chooses which ends are closed,
  enabling solid prisms, trays, hats, and pure tubes.
- `twist_degrees` and `taper` turn straight prisms into sculpted
  columns, helical drill bits, finials, and pyramids — without
  needing a modifier stack or a CSG chain.
- `crease_angle` (linear only, default 0) blends vertex normals across
  adjacent wall pairs whose dihedral angle is below the threshold, smoothing
  polyline-approximated curves while keeping wider-angle corners hard. 30° is
  a safe starting point that preserves right-angle corners on letters, gears,
  and engineered sections.
- UV is `(arc length, height)`, perfect for wraparound textures
  that run along the extrusion axis with stripe density tied to
  the local profile width.
- Emissive extrusions join the NEE pool automatically with
  area-weighted sampling — neon stars, glowing letters, and
  star-shaped panels all "just work" as light sources.
- Internally each extrusion is a triangle list inside its own BVH,
  so the outer scene BVH sees one leaf per extrusion regardless
  of profile complexity.

---

[Previous: Surfaces of Revolution (Lathe)](./11-lathe-surface-of-revolution.md) | [Tutorial Index](./README.md)
