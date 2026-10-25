# Geometry and Meshes

How the primitives, triangle meshes, subdivision surfaces and model files
are made exact, and how that is checked (PIANO-REVIEW.md Phase 2). The
Boolean operations have their own page ([CSG](./csg-boolean-operations.md)),
as do the [heightfield](./heightfield.md) and the
[BVH](./acceleration-structures.md).

## 1. Primitives

Every primitive answers the same questions, and `GeometryAuditTests` asks
them of each one, also scaled non-uniformly, mirrored and sheared through a
`Transform`:

- **First hit.** Against an implicit function of the solid (a signed
  distance where one exists), marched in small steps along the ray and
  bisected: the same distance, from outside and from inside, with the
  outward normal on the side the ray comes from and `FrontFace` true when it
  enters.
- **Area.** By the Cauchy-Crofton formula: lines with isotropic directions
  through the bounding sphere of radius R cross the surface on average
  A / (2πR²) times. Against the closed form and against `SurfaceArea`.
- **Area sampling.** The density `AreaPdf` of a sample times the area is 1
  for a uniform sampler; the samples lie on the surface, carry the outward
  normal and the UV that `Hit` reports at the same point; the mean of
  area × f over samples equals the Crofton integral of f.

The audit found and the phase fixed: the sampled UVs of the box, torus,
cylinder, cone and frustum caps, capsule, annulus and lathe (a textured
emitter showed one texture to the camera and another to light samples); the
frustum's sampled normal; holes in spline lathes on nearly horizontal rays
(the polynomial root is now polished by Newton steps in double precision);
in the heightfield, misses and wrong first hits in a cell (the ray against
the bilinear patch is now a quadratic solved exactly), `FrontFace` taken
from the smoothed normal instead of the patch, and `dpdu`/`dpdv` without the
slope.

**Transforms.** Normals go through the inverse transpose; a mirroring
transform keeps `FrontFace` right. A non-uniform scale makes the area
density of a sampled emitter vary over the surface: `ISamplable.AreaPdf` is
the true density, the inner density divided by |det M| × |M⁻ᵀ n|, used by
the light's solid-angle density and by the bidirectional emission
densities. `LightTransportTests` renders a non-uniformly scaled emissive
ellipsoid against its integral.

**Motion bounds.** An `AnimatedTransform`'s box is the union of boxes at
poses at most 15° of rotation apart, padded by the error of linear
interpolation, max|p''| / 8 over a step: for a corner p = T + R S c with R
turning at a constant rate θ and S changing linearly,
|p''| ≤ θ² |S c| + 2 θ |S′ c|. The second term, a turn and a change of scale
together, was missing.

## 2. Triangles

Triangles are intersected with the watertight algorithm of Woop, Benthin and
Wald (2013): the ray is moved to the origin and sheared to run along +z,
the vertices with it, and the test is a 2D test of the origin against three
edge functions. Two triangles sharing an edge compute the same edge function
for it with opposite signs, so a ray through the edge or a shared vertex
hits at least one of them; an edge function that rounds to exactly zero is
recomputed in double. There is no epsilon on the determinant.

`MeshWatertightTests`: on a closed icosphere of radius 1e-4, 1 and 3000,
rays aimed exactly at edges and vertices always hit (Möller-Trumbore with
its fixed 1e-7 threshold missed every one at radius 1e-4), and a line
entering the mesh crosses it exactly twice (230 of 1280 lines crossed it an
odd number of times before).

Triangle bounds are exact: the BVH's slab test is conservative (tExit
widened by 1 + 2γ₃), so no primitive pads its box by an absolute epsilon.

## 3. Subdivision surfaces

`Geometry/Subdivision/`: Loop for triangle meshes, Catmull-Clark for any
polygons, with one set of vertex rules (`SubdivisionTopology`, after DeRose,
Kass and Truong 1998):

- Boundary edges, and edges shared by more than two faces, are infinitely
  sharp; crease edges have a sharpness s (sharp for s levels, then smooth;
  their halves get s − 1). `subdivision_crease_angle` marks the edges whose
  faces meet at more than an angle, `subdivision_crease_sharpness` sets s.
- A vertex with no sharp edge, or one (a dart), follows the smooth rule;
  with two, the crease curve (6S + A + B)/8; with three or more, or a
  boundary vertex with a single face under `edge_and_corner`, it is a
  corner and stays put. A vertex sharpness between 0 and 1 blends the smooth
  and the sharp position.
- New edge points: smooth rule, the midpoint on a sharp edge, a blend for a
  sharpness below 1.

After the last level the vertices are moved onto the **limit surface**
(`LimitSurface`): the limit masks give the point the next levels would
converge to, and the tangent masks the normal there.

| Vertex | Limit point | Tangents |
|---|---|---|
| Loop, smooth, valence n | (ω S + Σ eᵢ)/(ω + n), ω = 3/(8β) | Σ cos(2πi/n) eᵢ, Σ sin(2πi/n) eᵢ |
| Catmull-Clark, smooth, valence n | (n² S + 4 Σ eᵢ + Σ dᵢ)/(n(n + 5)) | Σ Aₙ cos(2πi/n) eᵢ + (cos(2πi/n) + cos(2π(i+1)/n)) dᵢ, and with sines; Aₙ = 1 + cos(2π/n) + cos(π/n) √(2(9 + cos(2π/n))) |
| Crease | (A + 4S + B)/6 | |
| Corner | S | |

(eᵢ are the edge neighbours in order around the vertex, dᵢ the diagonal
ones; Hoppe et al. 1994, Halstead, Kass and DeRose 1993.)

The shading normal of a smooth vertex is the limit normal; at a crease or a
corner each face corner gets the angle-weighted normal of its own sector
(the faces reached around the vertex without crossing a sharp edge), so a
crease stays sharp in the shading. A crease still semi-sharp after the last
level counts as sharp at the limit.

`SubdivisionLimitTests` checks the masks against the rules: the limit point
and normal of every vertex are the same whether the mesh is projected after
one level or after four (to 1e-7 and 1e-6), for smooth vertices of valence
3 to 6, boundaries, corners and creases, in both schemes; the limit normals
agree with the normals of the mesh five levels down (within 0.25°); a cube
with all edges creased stays a cube with flat faces; sharper creases come
closer to the cube.

**Displacement** runs after the projection, along the limit normals (one
direction per vertex, so a crease does not open), then the normals are
rebuilt from the displaced mesh. The stack's order is fixed: bump,
subdivision, scalar or vector displacement, autobump.

## 4. Model files

`Scene/MeshImporter.cs` reads Wavefront OBJ (with MTL), glTF 2.0 (`.gltf`,
`.glb`) and PLY into one polygon mesh with a material slot per face, then
runs one pipeline: subdivision, displacement, triangulation, MikkTSpace
tangents, and one `Mesh` per material (each sampled as a light on its own).
What each format reads is in the [scene reference](../reference/scene-reference.md)
(§7.12).

- **Polygons** are triangulated by `PolyMesh.TriangulateFace`: a quad along
  its shorter diagonal, or along the one through its reflex corner when it
  is concave; a larger polygon by ear clipping in the plane of its Newell
  normal. A fan from the first corner covered area outside a concave
  polygon.
- **Normals**: the file's where a face has them; an OBJ face without normals
  in a smoothing group gets the angle-weighted normals of the group's faces
  around each vertex; otherwise flat. A face with UVs and no normals keeps
  its UVs.
- **Materials**: the entity's `material` covers every face; otherwise the
  file's materials, translated into OpenPBR, renamed by
  `material_overrides`. MTL: Kd, Ks (specular weight), Ns (Phong exponent n
  to a Beckmann α = √(2/(n + 2)), roughness √α), the PBR extension (Pr, Pm,
  Pc, Pcr, Ps), d/Tr/map_d (opacity, or transmission for illum 4, 6, 7, 9),
  Ke, bump and normal maps. glTF: metallic-roughness (roughness and
  metalness from the G and B channels), alpha MASK as a cut-out and BLEND as
  stochastic opacity, normal and emissive maps, and the KHR material
  extensions for emission strength, IOR, transmission (thin-walled without
  `KHR_materials_volume`, as glTF defines it), volume, clear coat, sheen,
  specular, iridescence, anisotropy and unlit, with KHR_texture_transform.
- **glTF space**: the default scene's nodes are flattened into the entity's
  space; a node whose matrix mirrors reverses the winding; positions shared
  by a mesh's primitives are welded so subdivision sees one surface across
  UV seams; texture V is read as 1 − v (glTF's runs downwards).

`MeshImportTests` writes small files of each kind and checks the geometry,
the materials and the transforms read from them.
