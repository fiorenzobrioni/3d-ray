using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Textures;

/// <summary>
/// Coordinate-system texture — DEVLOG "Texturing VFX production-grade" step 7.
///
/// <para>
/// Returns the shading-point coordinates as RGB. The chosen
/// <see cref="CoordMode"/> selects which space the output represents:
/// <list type="bullet">
///   <item><description><b>Object</b>: <c>rec.LocalPoint</c> — <b>metric</b> object-local space
///       (the object's own axes, in world units; the entity's scale is applied but its rotation
///       and translation are not), the same space every procedural texture (Noise / Marble /
///       Wood / Voronoi) samples in. Feature size is therefore set by the texture's <c>scale</c>
///       and is invariant to the entity's (non-uniform) scale. Use it for debug overlays that
///       match the procedural sampling space, and as a deterministic XYZ driver downstream.
///       For the normalised unit-cube workflow use <b>Generated</b> instead.</description></item>
///   <item><description><b>UV</b>: <c>(rec.U, rec.V, 0)</c> — the primitive's parametric coordinates.
///       Fundamental UV debug: visualises spherical UV unwrap on spheres, planar UV on quads,
///       cylindrical UV on cylinders. Equivalent to Arnold <c>utility/uv</c>,
///       RenderMan <c>uvCoord</c>, Cycles <c>Texture Coordinate → UV</c>.</description></item>
///   <item><description><b>Generated</b>: object-local position normalised by an explicit
///       <see cref="BoundsMin"/>/<see cref="BoundsMax"/> reference box to <c>[0, 1]³</c> —
///       the "reference space" workflow popularised by RenderMan <c>Pref</c>: artists declare
///       the canonical bounds of the object (typically the rest-pose AABB) and every shading
///       node downstream sees a tidy unit-cube parameter regardless of how the surface is
///       transformed at render time. Cycles auto-derives this from the mesh AABB at import;
///       our renderer keeps it explicit so the same bounds reproduce identically across
///       instancing, displacement and scene re-imports.</description></item>
///   <item><description><b>World</b>: <c>rec.Point</c> — world-space position (post forward <see cref="Geometry.Transform"/>),
///       useful for placing world-aligned overlays (laser-grid, "you-are-here" debug spheres,
///       world-locked dust shells) that must NOT follow the object as it moves. Equivalent to
///       Cycles <c>Generated → Object Info Texture Coordinate</c> output and RenderMan
///       <c>Pworld</c>.</description></item>
/// </list>
/// </para>
///
/// <para>
/// <b>Wrapping convention</b> for <c>Object</c> and <c>World</c>: the raw coordinates can
/// span any range so they are passed through <c>p − floor(p)</c> (fractional component) to
/// produce a periodic <c>[0, 1]³</c> output suitable both for direct RGB visualisation and
/// for feeding into other textures that expect a normalised XYZ vector. This matches the
/// implicit wrap built into every procedural noise we ship (Worley, Perlin, fBm) and gives
/// a tilable colour-cube debug view by default. The standard <c>offset</c>, <c>rotation</c>
/// and <c>scale</c> transform applies BEFORE the fract, so the artist can pick any cell size
/// for the debug pattern via <c>scale</c>.
/// </para>
///
/// In YAML:
/// <code>
/// texture:
///   type: "coordinate"
///   mode: "object"             # object | uv | generated | world
///   scale: 1.0                 # uniform multiplier applied before fract / before generated clamp
///   bounds_min: [-1, -1, -1]   # only used in mode: "generated" (reference-space lower corner)
///   bounds_max: [1, 1, 1]      # only used in mode: "generated" (reference-space upper corner)
///   offset: [0, 0, 0]
///   rotation: [0, 0, 0]
/// </code>
///
/// <para>
/// <b>Aliasing</b>: the texture is deterministic and continuous everywhere except at the
/// integer boundaries of <c>fract()</c> (for the Object / World modes). The shading point
/// crosses those at most once per pixel on any sane <c>scale</c>, so analytic anti-aliasing
/// is not implemented — the perceived band thickness is purely a function of <c>scale</c>
/// and never aliases in the Worley/Voronoi sense. UV and Generated modes are smooth
/// everywhere by construction.
/// </para>
/// </summary>
public sealed class CoordinateTexture : ITexture
{
    public enum CoordMode { Object, UV, Generated, World }

    public CoordMode Mode { get; set; } = CoordMode.Object;

    /// <summary>
    /// Uniform multiplier applied to the raw coordinates BEFORE the <c>fract</c>
    /// wrap (Object / World) or BEFORE the bounds normalisation (Generated). A
    /// larger value packs more debug bands into the visible surface; the default
    /// of <c>1.0</c> means one full <c>[0, 1]³</c> cube per unit of object-local
    /// (or world) space — a reasonable default for unit-sized objects.
    /// </summary>
    public float Scale { get; set; } = 1f;

    public Vector3 Offset { get; set; } = Vector3.Zero;
    public Vector3 Rotation { get; set; } = Vector3.Zero;

    /// <summary>
    /// Lower corner of the reference box used by <see cref="CoordMode.Generated"/>.
    /// Default <c>(-1, -1, -1)</c> matches the unit object-space bounds of a sphere
    /// of radius 1 / a cube of half-extent 1 — the canonical reference geometry
    /// of every primitive we ship.
    /// </summary>
    public Vector3 BoundsMin { get; set; } = new(-1f, -1f, -1f);

    /// <summary>
    /// Upper corner of the reference box used by <see cref="CoordMode.Generated"/>.
    /// Default <c>(1, 1, 1)</c>. See <see cref="BoundsMin"/>.
    /// </summary>
    public Vector3 BoundsMax { get; set; } = new( 1f,  1f,  1f);

    public Vector3 Value(float u, float v, Vector3 p, int objectSeed)
    {
        // Called via the legacy 4-arg path (e.g. from Emissive.Emit or any
        // non-rec-aware consumer). Without a HitRecord we cannot honour the
        // World mode — `p` IS the local point in this contract. We treat
        // World as if it were Object in this path: graceful degradation that
        // never produces a wrong space silently (the debug view will still be
        // useful, just not world-locked). UV mode is fully supported.
        HitRecord rec = default;
        rec.U = u;
        rec.V = v;
        rec.LocalPoint = p;
        rec.Point = p; // best-effort: callers without rec context can't distinguish
        rec.ObjectSeed = objectSeed;
        return Value(in rec);
    }

    public Vector3 Value(in HitRecord rec)
    {
        switch (Mode)
        {
            case CoordMode.UV:
            {
                // UV is a 2-D parametric coordinate — keep it linear (no fract,
                // no clamp): primitives already deliver U,V ∈ [0,1] for every
                // standard parameterisation, and exposing it raw is what the
                // debug-view use case wants (a clean smooth gradient with no
                // tiling artefacts on the seam line).
                float uu = rec.U * Scale + Offset.X;
                float vv = rec.V * Scale + Offset.Y;
                return new Vector3(uu, vv, 0f);
            }

            case CoordMode.Generated:
            {
                // Normalise object-local position by the user-declared
                // reference box. Clamp to [0,1] so a hit slightly outside the
                // declared bounds (e.g. after displacement) doesn't punch a
                // hole in the debug view. Division-by-zero guard for the
                // degenerate min == max case (returns mid-cell 0.5).
                Vector3 size = BoundsMax - BoundsMin;
                Vector3 local = TransformedLocal(rec.LocalPoint);
                float gx = MathF.Abs(size.X) > 1e-12f ? (local.X - BoundsMin.X) / size.X : 0.5f;
                float gy = MathF.Abs(size.Y) > 1e-12f ? (local.Y - BoundsMin.Y) / size.Y : 0.5f;
                float gz = MathF.Abs(size.Z) > 1e-12f ? (local.Z - BoundsMin.Z) / size.Z : 0.5f;
                return new Vector3(
                    Math.Clamp(gx, 0f, 1f),
                    Math.Clamp(gy, 0f, 1f),
                    Math.Clamp(gz, 0f, 1f));
            }

            case CoordMode.World:
            {
                Vector3 world = rec.Point * Scale;
                // Debug coordinate display: explicitly never seeded.
                world = TextureTransform.ApplyManual(world, Offset, Rotation);
                return Fract(world);
            }

            case CoordMode.Object:
            default:
            {
                Vector3 local = TransformedLocal(rec.LocalPoint);
                return Fract(local);
            }
        }
    }

    private Vector3 TransformedLocal(Vector3 p)
    {
        Vector3 q = p * Scale;
        // Debug coordinate display: only manual offset/rotation; never seeded.
        return TextureTransform.ApplyManual(q, Offset, Rotation);
    }

    private static Vector3 Fract(Vector3 p)
        => new(p.X - MathF.Floor(p.X),
               p.Y - MathF.Floor(p.Y),
               p.Z - MathF.Floor(p.Z));
}
