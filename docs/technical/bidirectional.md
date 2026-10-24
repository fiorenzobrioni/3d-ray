# Bidirectional Path Tracing and VCM

How `--integrator bdpt` and `--integrator vcm` render. Both are one code
base: VCM is bidirectional path tracing plus vertex merging, and BDPT is the
same code with merging switched off. Path tracing (`--integrator path`) stays
the default and the integrator of every quality preset.

Code: `src/RayTracer/Rendering/Bidirectional/` (`VertexConnectionMerging.cs`,
`VertexGrid.cs`, `IntegratorKind.cs`), the emission sampling of the lights
(`ILight.SampleEmission`, `ILight.EmissionPdfOverCos`, `Lights/LightEmission.cs`)
and the camera's importance (`Camera.ConnectToFilm`, `Camera.FilmDirectionPdf`).
Tests: `src/RayTracer.Tests/Validation/EmissionTests.cs`,
`BidirectionalTests.cs` (which also reruns every test of
`LightTransportTests` with BDPT).

## 1. Why

Path tracing builds a path from the camera and finds the light by sampling a
point on it from each vertex (next-event estimation) or by hitting it. Some
light it finds only by chance:

- a small light seen through glass, or the light a glass ball focuses on a
  floor (light → glass → floor → camera): the floor vertex cannot sample the
  light through the glass, and its bounce rarely hits the light;
- a caustic seen through glass or water (light → water → floor → water →
  camera): no vertex of the path can be joined to another by a straight
  segment at all.

Bidirectional path tracing traces light from the lights too and joins the two
ends, which handles the first case; vertex merging handles the second.

## 2. Strategies

Each iteration traces, for every pixel, one subpath from the camera and one
from a light (picked in proportion to its power). A path of k segments can
then be made in several ways:

| Strategy | Made by |
|---|---|
| s = 0 | the camera subpath hits an emitter (or escapes to the sky) |
| s = 1 | next-event estimation at a camera vertex |
| s ≥ 2, t ≥ 2 | a camera vertex joined to a light vertex by a shadow ray |
| t = 1 | a light vertex joined to the camera (light tracing), splatted into the pixel it projects to |
| merging | a camera vertex and a light vertex within the merging radius (VCM only) |

Every strategy is weighted by the balance heuristic over all the strategies
that could have made the same path. The weights are computed in constant time
from three quantities each subpath carries (dVCM, dVC, dVM), updated at each
vertex from the densities of sampling the next direction forward and the
previous one backward (Georgiev, Křivánek, Davidovič and Slusallek 2012,
"Light Transport Simulation with Vertex Connection and Merging"). With merging
off (BDPT) the merging terms vanish and the weights are those of Veach's
bidirectional path tracing.

Light tracing uses the camera's importance: `ConnectToFilm` projects a point
through the lens (pinhole or thin lens, a point of the aperture sampled) onto
the film, and `FilmDirectionPdf` gives the density with which the camera
samples that direction (f² / (|H| |V| cos³θ) per unit film). The splat goes
through the pixel reconstruction filter, normalised, so a light-traced
sample counts as a camera sample would.

## 3. Light emission

Every light samples an emitted ray (a point and a direction) with its
densities, which `EmissionTests` checks: the mean emitted flux equals each
light's flux, and the density reported for a ray equals the one it was drawn
with.

- Area, sphere and emissive geometry: a uniform point on the surface, a
  cosine-weighted direction.
- Point and spot: a uniform direction (the spot's cone; IES profiles weight
  the radiance), the position a delta.
- Directional and sun: the direction a delta (or a cone for a disc), the
  origin on a disc covering the scene, facing the light.
- Sky (environment): a direction from the sky's own importance sampling, the
  origin on the same disc.

Portals are a sampling aid of path tracing over the same sky: no subpath
starts from them.

The density with which next-event estimation picks a light point depends on
the receiver for spheres, rectangles and skies (solid-angle and
spherical-rectangle sampling), so a light subpath sets its dVCM at its first
vertex, where the receiver is known, instead of at the light.

## 4. Vertices

- **Surfaces.** The BSDF is evaluated with the record oriented toward the side
  of each direction (the relative index inverted on the far side). Light
  subpaths carry importance with the adjoint BSDF: the radiance factor
  (η_i/η_t)² of a refraction is left out. The shadow-terminator factor is
  applied on the light side as in path tracing. Delta lobes are never
  connected or merged; their densities cancel in the weights.
- **Null interfaces.** A surface that transmits straight (an index-matched
  shell, a cut-out, a thin sheet passed through) is crossed without a vertex,
  its media and indices switched: connections cross it exactly as shadow rays
  do, so no path is counted twice.
- **Participating media.** Each subpath samples the free flight of the medium
  it is in (the estimators of [Participating Media](./participating-media.md));
  a scattering event is a vertex whose "BSDF" is the phase function and whose
  cosines are 1. Connections and light tracing open the media context of their
  subpath, so their shadow rays are attenuated by every medium they cross.
  Merging happens on surfaces only, and the merging terms of the weights are
  dropped at medium vertices.
- **Subsurface entities.** Behind a refraction, a subsurface entity is walked
  by the camera subpaths with the path tracer's random walk (its light sample
  at every bounce included), so both integrators render the same material:
  light subpaths stop there, and the walk's last light sample is the only
  strategy the emission met by the ray leaving it is weighted against. Behind
  an index-matched boundary the medium is an ordinary medium for both
  subpaths.
- **Russian roulette** is not part of the densities (the weights still add up
  to one); on light subpaths it acts on the throughput relative to the
  emitted one.

## 5. Iterations and merging

`-s` is the number of iterations: one camera and one light subpath per pixel
each. The light subpaths of an iteration are traced in chunks of at most
2^18 pixels: the chunk's light subpaths first (stored, and connected to the
camera; light tracing is normalised by the image's pixel count), then its
camera subpaths, each joined to the light subpath of its own index and merged
with the chunk's light vertices (a hashed grid of cells twice the radius).

The merging radius at iteration i is r_i = r_0 × i^((α−1)/2) with α = 0.75
(Knaus and Zwicker 2011), r_0 = `--vcm-radius` (default 0.003) times the
scene's radius. The bias of merging falls with the radius, so VCM converges to
the right image as `-s` grows: it is consistent, not unbiased, and
`--reference` refuses it (BDPT is unbiased and allowed).

The shutter time is shared by a pixel's camera and light subpaths in an
iteration, so connections join vertices of the same instant.

## 6. Limits

- The per-kind depths (`--depth-diffuse` and the others) do not apply: `-d`
  limits the number of segments, as in path tracing.
- The per-category visibility flags of entities are not consulted by the
  bidirectional subpaths.
- Path guiding (`--guiding`) is a path tracing option.
- No merging in media (photon beams): a caustic seen through fog is rendered
  by connections only.
- The shading-normal correction of the adjoint BSDF (Veach 1997, §5.3) is not
  applied: with normal and bump maps the light-traced and merged strategies
  see the shading normal as path tracing does.

## 7. Validation

- `EmissionTests`: emitted flux and densities of every light type.
- `BdptLightTransportTests`: every light type against the quadrature of its
  integral, rendered by BDPT (camera hits, light samples and light tracing
  must add up to the same direct light).
- `BidirectionalTests`: a mirrored point light only light tracing reaches,
  pinhole and thin lens (0.07% from the closed form); white furnaces of
  every medium kind with the sky as a light; single scattering in fog against
  quadrature; a caustic seen through a glass ball, VCM against path tracing
  (0.995).
- `validation/`: on the reference scenes BDPT and VCM agree with the
  reference renderer as path tracing does, fog, chromatic subsurface and
  heterogeneous smoke included (mean ratios within 0.3%).
- `scenes/review/vcm-pool.yaml`: the floor of a pool under a small light, at
  equal time path tracing and BDPT show fireflies and VCM the caustic net
  (`scenes/review/images/vcm-pool.jpg`).
