using System.Numerics;
using RayTracer.Acceleration;
using RayTracer.Core;
using RayTracer.Materials;

namespace RayTracer.Geometry;

/// <summary>
/// Profile interpolation mode for an <see cref="Extrusion"/>. Mirrors
/// <see cref="LatheMode"/> but operates on a closed XZ polygon instead of
/// a meridian half-profile.
/// </summary>
public enum ExtrusionMode
{
    /// <summary>Polyline profile — each consecutive pair of points defines a
    /// straight side wall. Hard edges at every vertex (faceted look).</summary>
    Linear,

    /// <summary>Centripetal Catmull-Rom through the given points (closed loop)
    /// — passes exactly through every control point and is C¹ continuous.
    /// Side walls receive smooth, averaged-edge normals.</summary>
    CatmullRom,

    /// <summary>Explicit cubic Bezier segments forming a closed loop. The
    /// profile gives the segment endpoints; a parallel
    /// <c>profile_bezier_controls</c> list supplies four control points per
    /// segment for full authoring control.</summary>
    Bezier,
}

/// <summary>
/// Which ends of the extrusion are closed by a polygonal cap.
/// </summary>
public enum ExtrusionCaps
{
    None,
    Start,  // y = 0 only
    End,    // y = height only
    Both,
}

/// <summary>
/// A linear extrusion (also known as a <em>prism</em>) of a closed 2D profile
/// along the local Y axis. Profiles can be polyline, centripetal Catmull-Rom
/// or explicit cubic Bezier — the same three modes offered by <see cref="Lathe"/>
/// for surfaces of revolution. Optional twist around the axis and uniform
/// taper at the top end produce the wide range of architectural / industrial
/// shapes found in commercial DCC tools (Blender's "Extrude with twist",
/// Houdini's <c>polyextrude</c>, OpenSCAD's <c>linear_extrude</c>, POV-Ray's
/// <c>prism</c>).
///
/// <b>Local frame:</b> the bottom cap sits at <c>y = 0</c> and the top at
/// <c>y = height</c> (origin <em>not</em> centred — apply a downward
/// translation if you want a centred extrusion).
///
/// <b>Capping:</b> caps are required to make the extrusion a closed manifold.
/// Use <see cref="ExtrusionCaps.None"/> only for purely decorative geometry —
/// CSG operations and participating-media boundaries assume a closed surface
/// and will misbehave on an open extrusion.
///
/// Internally the profile is tessellated into a fine 2D polyline, the side
/// walls are emitted as a strip of triangles between bottom and top loops
/// (smooth-shaded for curved profiles, flat for linear), and the caps are
/// produced by ear-clipping the polygon — this handles concave shapes
/// (gears, stars, letters) without manual decomposition. Every triangle goes
/// into a single internal <see cref="BvhNode"/>, exactly as <see cref="Mesh"/>
/// does, so a complex extrusion costs the outer scene BVH one leaf.
///
/// <b>Smooth normals:</b> for curved profile modes the per-vertex normal is
/// the analytic outward normal of the ruled surface
/// <c>P(u,v) = (1−v)·B(u) + v·T(u)</c>, computed as
/// <c>n = normalize((T(u) − B(u)) × ∂P/∂u)</c>. This formula correctly
/// accounts for non-uniform <c>taper</c> (frustum slant — normals tilt up or
/// down) and non-zero <c>twist</c> (the ruling line is no longer parallel to
/// Y). Lifting the 2D outward normal into XZ unchanged would silently produce
/// the wrong specular highlight on tapered or twisted surfaces.
///
/// Implements <see cref="ISamplable"/> with an area-weighted CDF over its
/// triangles so an emissive <c>Extrusion</c> automatically becomes a
/// GeometryLight in the NEE pool.
/// </summary>
public sealed class Extrusion : IHittable, ISamplable
{
    private readonly IHittable _bvh;
    private readonly List<IHittable> _triangles;
    private readonly IMaterial _material;
    private int _seed;

    private readonly float[] _cumulativeAreas;
    private readonly float _totalArea;

    /// <summary>Number of triangles produced by tessellation + capping.</summary>
    public int TriangleCount => _triangles.Count;

    /// <summary>
    /// Builds an extrusion of <paramref name="profile"/> along the local Y axis
    /// over a height of <paramref name="height"/>.
    /// </summary>
    /// <param name="profile">Closed 2D loop in the XZ plane (no duplicate end
    /// point). Must contain at least three points.</param>
    /// <param name="mode">Profile interpolation mode.</param>
    /// <param name="height">Extrusion length along +Y. Must be &gt; 0.</param>
    /// <param name="caps">Which ends to close with a triangulated cap.</param>
    /// <param name="material">Shared material for all faces.</param>
    /// <param name="bezierControls">For <see cref="ExtrusionMode.Bezier"/>:
    /// 4·N control points (one cubic per profile segment, closed loop).
    /// Ignored for the other modes.</param>
    /// <param name="twistDegrees">Total rotation of the top profile around the
    /// Y axis, in degrees. 0 disables twist.</param>
    /// <param name="taper">Uniform XZ scale of the top profile relative to
    /// the bottom (1 = straight prism, &lt;1 narrowing, &gt;1 flaring).</param>
    /// <param name="curveSamples">For curved modes, how many polyline samples
    /// per input segment. Higher = smoother silhouette, more triangles.</param>
    /// <param name="creaseAngleDeg">For Linear mode only: dihedral threshold
    /// for auto-smoothing across adjacent profile segments. Pairs of side
    /// walls whose face normals make an angle below this value share a
    /// blended vertex normal (smooth shading); pairs above it keep their
    /// own face normals (hard edge). 0 disables smoothing entirely
    /// (every segment is flat). 30° is a common default that flattens
    /// polyline-approximated curves while preserving 90° corners on
    /// letters and engineered profiles.</param>
    public Extrusion(
        IReadOnlyList<Vector2> profile,
        ExtrusionMode mode,
        float height,
        ExtrusionCaps caps,
        IMaterial material,
        IReadOnlyList<Vector2>? bezierControls = null,
        float twistDegrees = 0f,
        float taper = 1f,
        int curveSamples = 16,
        float creaseAngleDeg = 0f)
    {
        if (profile == null || profile.Count < 3)
            throw new ArgumentException("Extrusion requires a profile of at least 3 points.", nameof(profile));
        if (!(height > 0f))
            throw new ArgumentException("Extrusion height must be positive.", nameof(height));
        if (!(taper > 0f))
            throw new ArgumentException("Extrusion taper must be positive.", nameof(taper));

        _material = material;

        // 1) Tessellate the profile into a fine closed polyline.
        var loop = BuildLoop(profile, mode, bezierControls, curveSamples);

        // 2) Ensure CCW orientation so wall outward normals point away from the
        //    interior. If the input is CW, reverse it in place.
        if (Polygon2D.SignedArea(loop) < 0f) loop.Reverse();

        bool linearSmooth = mode == ExtrusionMode.Linear && creaseAngleDeg > 0f;
        bool smoothSides = mode != ExtrusionMode.Linear || linearSmooth;
        int n = loop.Count;

        // 3) Build the bottom and top vertex rings with twist + taper.
        var bottom = new Vector3[n];
        var top    = new Vector3[n];
        float twistRad = twistDegrees * MathF.PI / 180f;
        float cosT = MathF.Cos(twistRad);
        float sinT = MathF.Sin(twistRad);
        for (int i = 0; i < n; i++)
        {
            Vector2 p = loop[i];
            bottom[i] = new Vector3(p.X, 0f, p.Y);
            // Top = rotate(twist) ∘ scale(taper).
            float tx = p.X * taper;
            float tz = p.Y * taper;
            float rx = cosT * tx - sinT * tz;
            float rz = sinT * tx + cosT * tz;
            top[i] = new Vector3(rx, height, rz);
        }

        // 4) Per-quad-corner side normals (only consumed when smoothSides).
        //    Two code paths share the same emission stage at step 7:
        //
        //    a) Linear with creaseAngleDeg > 0 (linearSmooth): per-edge face
        //       normals, then blended at each vertex only when the dihedral
        //       angle with the adjacent edge stays below the crease
        //       threshold. Above the threshold the vertex keeps the
        //       current edge's own face normal — so right-angled corners
        //       in letters / gears / brackets stay crisp while gentle
        //       curves approximated by many short segments shade as
        //       smooth surfaces. Bottom and top normals differ when
        //       taper or twist warps the wall into a ruled (non-planar)
        //       quad.
        //
        //    b) CatmullRom / Bezier (always smooth): per-vertex
        //       ruled-surface normal n = normalize(R × t) where
        //       R = top[i] − bottom[i] is the ruling line and t is the
        //       along-profile tangent — correct for any combination of
        //       taper and twist (lifting the 2D outward normal into XZ
        //       unchanged would produce the wrong specular highlight on
        //       tapered or twisted surfaces). The same normal serves
        //       both adjacent quads at that vertex (no creases).
        //
        // Index layout for the per-quad arrays: entry [i] is the corner of
        // quad i sitting at profile vertex i (the "left" / start side); the
        // corner of quad i at vertex i+1 (the "right" / end side) is stored
        // in entry [i] of the right-side arrays. With this layout the two
        // sides of a smooth (non-crease) vertex i contain the same blended
        // normal in leftBottomN[i] and rightBottomN[(i − 1 + n) % n].
        var leftBottomN  = smoothSides ? new Vector3[n] : Array.Empty<Vector3>();
        var leftTopN     = smoothSides ? new Vector3[n] : Array.Empty<Vector3>();
        var rightBottomN = smoothSides ? new Vector3[n] : Array.Empty<Vector3>();
        var rightTopN    = smoothSides ? new Vector3[n] : Array.Empty<Vector3>();
        if (smoothSides)
        {
            // Fallback 2D outward normals for vertices whose ruled normal
            // collapses (e.g. R parallel to the local tangent at near-zero
            // taper combined with grazing geometry).
            var fallback2D = ComputeOutwardEdgeNormals2D(loop);

            if (linearSmooth)
            {
                float cosCrease = MathF.Cos(MathF.Min(MathF.Max(creaseAngleDeg, 0f), 180f)
                                            * MathF.PI / 180f);

                // Per-edge face normals at both ends of the ruling. With
                // taper or twist the side quad is a ruled (warped) surface
                // and the outward normal varies along Y; we sample it at
                // the bottom and the top so SmoothTriangle interpolates
                // a faithful shading normal across the wall.
                var edgeNB = new Vector3[n];
                var edgeNT = new Vector3[n];
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    Vector3 b0 = bottom[i], b1 = bottom[j];
                    Vector3 t0 = top[i],    t1 = top[j];
                    // Outward face normal at the bottom of the quad: same
                    // winding as triangle (b0, t0, b1), which matches the
                    // CCW lifted-2D-CCW orientation comment at step 7.
                    Vector3 nBot = Vector3.Cross(t0 - b0, b1 - b0);
                    // Outward face normal at the top of the quad: triangle
                    // (t0, t1, b1) seen from the same outward side.
                    Vector3 nTop = Vector3.Cross(t1 - t0, b1 - t0);
                    edgeNB[i] = SafeNormalize(nBot, fallback2D[i]);
                    edgeNT[i] = SafeNormalize(nTop, fallback2D[i]);
                }

                for (int i = 0; i < n; i++)
                {
                    int prev = (i - 1 + n) % n;
                    int next = (i + 1) % n;

                    // Left corner of quad i (profile vertex i): blend with
                    // the previous edge when the bottom-face dihedral is
                    // below the crease threshold.
                    if (Vector3.Dot(edgeNB[prev], edgeNB[i]) >= cosCrease)
                    {
                        leftBottomN[i] = SafeNormalize(edgeNB[prev] + edgeNB[i], fallback2D[i]);
                        leftTopN[i]    = SafeNormalize(edgeNT[prev] + edgeNT[i], fallback2D[i]);
                    }
                    else
                    {
                        leftBottomN[i] = edgeNB[i];
                        leftTopN[i]    = edgeNT[i];
                    }

                    // Right corner of quad i (profile vertex i+1).
                    if (Vector3.Dot(edgeNB[i], edgeNB[next]) >= cosCrease)
                    {
                        rightBottomN[i] = SafeNormalize(edgeNB[i] + edgeNB[next], fallback2D[next]);
                        rightTopN[i]    = SafeNormalize(edgeNT[i] + edgeNT[next], fallback2D[next]);
                    }
                    else
                    {
                        rightBottomN[i] = edgeNB[i];
                        rightTopN[i]    = edgeNT[i];
                    }
                }
            }
            else
            {
                for (int i = 0; i < n; i++)
                {
                    int prev = (i - 1 + n) % n;
                    int next = (i + 1) % n;

                    // Smooth tangent: (next − prev) is length-weighted (longer
                    // adjacent edges contribute proportionally) and matches the
                    // C¹ tangent of the underlying spline at the control point.
                    Vector3 tB = bottom[next] - bottom[prev];
                    Vector3 tT = top[next]    - top[prev];

                    Vector3 R = top[i] - bottom[i];

                    Vector3 nB = Vector3.Cross(R, tB);
                    Vector3 nT = Vector3.Cross(R, tT);

                    Vector3 vertexBotN = SafeNormalize(nB, fallback2D[i]);
                    Vector3 vertexTopN = SafeNormalize(nT, fallback2D[i]);

                    // Vertex i is the left corner of quad i AND the right
                    // corner of quad (i − 1).
                    leftBottomN[i]     = vertexBotN;
                    leftTopN[i]        = vertexTopN;
                    rightBottomN[prev] = vertexBotN;
                    rightTopN[prev]    = vertexTopN;
                }
            }
        }

        // 5) Side-wall U coordinate as cumulative bottom-loop arc length so a
        //    wrapping texture has no seam-stretch in the linear case. With
        //    taper ≠ 1 the top edges have a different absolute length but
        //    share the same logical U so the texture stretches uniformly
        //    (matches Blender's "preserve texture" behaviour on tapered
        //    extrudes).
        var uArc = new float[n + 1];
        uArc[0] = 0f;
        for (int i = 0; i < n; i++)
        {
            float dx = loop[(i + 1) % n].X - loop[i].X;
            float dy = loop[(i + 1) % n].Y - loop[i].Y;
            uArc[i + 1] = uArc[i] + MathF.Sqrt(dx * dx + dy * dy);
        }
        float perim = uArc[n] > 0f ? uArc[n] : 1f;

        // 6) Pre-compute cap triangulation once (it depends only on the
        //    densified 2D loop and is identical for both caps). We detect
        //    partial triangulation now and surface it as an ArgumentException
        //    so the SceneLoader can warn the user — silently emitting a
        //    holed cap was the previous behaviour.
        bool needsCaps = caps != ExtrusionCaps.None;
        List<(int A, int B, int C)>? capTris = null;
        Vector2[]? capUV = null;
        if (needsCaps)
        {
            capTris = Polygon2D.EarClip(loop, out bool fullyTriangulated);
            if (!fullyTriangulated)
                throw new ArgumentException(
                    "cap triangulation failed — the profile is degenerate " +
                    "(self-intersecting, collinear, or zero-area). " +
                    $"ear-clip produced {capTris.Count}/{n - 2} triangles.",
                    nameof(profile));
            capUV = ComputePlanarCapUVs(loop);
        }

        // 7) Emit triangles. Pre-size the list to its exact final length to
        //    avoid List<T> growth re-allocations during construction.
        //
        // Winding note: a 2D-CCW loop lifted with (X, Y_2D) → (X, 0, Z_3D)
        // has natural cross-product face normal pointing toward −Y when
        // triangulated in CCW order. To get an outward face normal on the
        // side wall (e.g. +X for the +X side of the polygon) and on the
        // caps (−Y for bottom, +Y for top) we therefore use:
        //   • side walls:   (b0, t1, b1)  and  (b0, t0, t1)
        //   • bottom cap:   (a, b, c)      [keep ear-clip CCW]
        //   • top cap:      (c, b, a)      [reverse]
        // SmoothTriangle relies on the geometric face normal for FrontFace
        // classification and would silently render back-face hits with a
        // flipped shading normal — i.e. solid black — if the winding gave
        // an inward face. Triangle (used for the linear flat path) is
        // immune via SetFaceNormal, but we keep the same winding for
        // consistency.
        int sideTriCount = 2 * n;
        int capTriCount = (capTris?.Count ?? 0) * (caps == ExtrusionCaps.Both ? 2 : 1);
        _triangles = new List<IHittable>(sideTriCount + capTriCount);

        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            Vector3 b0 = bottom[i], b1 = bottom[j];
            Vector3 t0 = top[i],    t1 = top[j];

            float u0 = uArc[i] / perim;
            float u1 = uArc[i + 1] / perim;

            if (smoothSides)
            {
                Vector3 nB0 = leftBottomN[i],  nB1 = rightBottomN[i];
                Vector3 nT0 = leftTopN[i],     nT1 = rightTopN[i];

                _triangles.Add(new SmoothTriangle(
                    b0, t1, b1, nB0, nT1, nB1,
                    new Vector2(u0, 0f), new Vector2(u1, 1f), new Vector2(u1, 0f), material));
                _triangles.Add(new SmoothTriangle(
                    b0, t0, t1, nB0, nT0, nT1,
                    new Vector2(u0, 0f), new Vector2(u0, 1f), new Vector2(u1, 1f), material));
            }
            else
            {
                _triangles.Add(new Triangle(b0, t1, b1, material));
                _triangles.Add(new Triangle(b0, t0, t1, material));
            }
        }

        // 8) Caps as smooth triangles with planar UVs anchored to the
        //    bottom-loop AABB. Both caps share the same UV layout so a
        //    stamped texture (logo, gear teeth) registers identically on
        //    both ends regardless of taper or twist.
        if (capTris != null && capUV != null)
        {
            Vector3 nDown = -Vector3.UnitY;
            Vector3 nUp   =  Vector3.UnitY;
            if (caps == ExtrusionCaps.Start || caps == ExtrusionCaps.Both)
            {
                // Keep ear-clip CCW: 2D-CCW lifted to y = 0 has cross-product
                // face normal −Y, which is the outward direction for the
                // bottom cap.
                foreach (var (a, b, c) in capTris)
                    _triangles.Add(new SmoothTriangle(
                        bottom[a], bottom[b], bottom[c],
                        nDown, nDown, nDown,
                        capUV[a], capUV[b], capUV[c], material));
            }
            if (caps == ExtrusionCaps.End || caps == ExtrusionCaps.Both)
            {
                // Reverse the ear-clip CCW so the top cap face normal points
                // +Y (outward).
                foreach (var (a, b, c) in capTris)
                    _triangles.Add(new SmoothTriangle(
                        top[c], top[b], top[a],
                        nUp, nUp, nUp,
                        capUV[c], capUV[b], capUV[a], material));
            }
        }

        // 9) Internal BVH (same threshold as Mesh).
        _bvh = _triangles.Count > 2
            ? new BvhNode(new List<IHittable>(_triangles))
            : new HittableList(_triangles);

        // 10) Cumulative area CDF for ISamplable.
        _cumulativeAreas = new float[_triangles.Count];
        float sum = 0f;
        for (int i = 0; i < _triangles.Count; i++)
        {
            sum += ((ISamplable)_triangles[i]).SurfaceArea;
            _cumulativeAreas[i] = sum;
        }
        _totalArea = sum > 0f ? sum : 1f;
    }

    public bool Hit(in Ray ray, float tMin, float tMax, ref HitRecord rec)
        => _bvh.Hit(ray, tMin, tMax, ref rec);

    public AABB BoundingBox() => _bvh.BoundingBox();

    public int Seed
    {
        get => _seed;
        set
        {
            _seed = value;
            foreach (var tri in _triangles) tri.Seed = value;
        }
    }

    /// <inheritdoc/>
    public float SurfaceArea => _totalArea;

    public (Vector3 Point, Vector3 Normal, Vector2 Uv, float Area) Sample()
    {
        if (_triangles.Count == 0)
            return (Vector3.Zero, Vector3.UnitY, new Vector2(0.5f, 0.5f), 0f);
        float target = MathUtils.RandomFloat() * _totalArea;
        int idx = PickTriangleByCdf(target);
        var (p, n, uv, _) = ((ISamplable)_triangles[idx]).Sample();
        return (p, n, uv, _totalArea);
    }

    public (Vector3 Point, Vector3 Normal, Vector2 Uv, float Area) SampleStratified(int sampleIndex, int sqrtSamples)
    {
        if (_triangles.Count == 0)
            return (Vector3.Zero, Vector3.UnitY, new Vector2(0.5f, 0.5f), 0f);
        int totalStrata = Math.Max(1, sqrtSamples * sqrtSamples);
        float stratum = 1f / totalStrata;
        float jittered = (sampleIndex + MathUtils.RandomFloat()) * stratum;
        float target = Math.Clamp(jittered, 0f, 0.9999999f) * _totalArea;
        int idx = PickTriangleByCdf(target);
        var (p, n, uv, _) = ((ISamplable)_triangles[idx]).Sample();
        return (p, n, uv, _totalArea);
    }

    private int PickTriangleByCdf(float target)
    {
        int idx = Array.BinarySearch(_cumulativeAreas, target);
        if (idx < 0) idx = ~idx;
        return Math.Clamp(idx, 0, _triangles.Count - 1);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Profile construction
    // ─────────────────────────────────────────────────────────────────────────

    private static List<Vector2> BuildLoop(
        IReadOnlyList<Vector2> profile, ExtrusionMode mode,
        IReadOnlyList<Vector2>? bezierControls, int curveSamples)
    {
        switch (mode)
        {
            case ExtrusionMode.Linear:
                return new List<Vector2>(profile);

            case ExtrusionMode.CatmullRom:
                if (profile.Count < 3)
                    return new List<Vector2>(profile);
                return Polygon2D.TessellateClosedCatmullRom(profile, Math.Max(2, curveSamples));

            case ExtrusionMode.Bezier:
                if (bezierControls == null || bezierControls.Count != 4 * profile.Count)
                    throw new ArgumentException(
                        $"Bezier extrusion requires exactly 4·N = {4 * profile.Count} control points " +
                        $"(one cubic per profile segment in a closed loop), got " +
                        $"{bezierControls?.Count ?? 0}.", nameof(bezierControls));
                return Polygon2D.TessellateClosedBezier(bezierControls, Math.Max(2, curveSamples));

            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported extrusion mode.");
        }
    }

    /// <summary>
    /// Per-edge outward 2D normals on a CCW closed polygon. Used only as a
    /// fallback when the ruled-surface cross product collapses at a vertex
    /// (degenerate adjacent edges or ruling parallel to the tangent).
    /// </summary>
    private static Vector2[] ComputeOutwardEdgeNormals2D(IReadOnlyList<Vector2> loop)
    {
        int n = loop.Count;
        var edgeN = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            Vector2 a = loop[i];
            Vector2 b = loop[(i + 1) % n];
            Vector2 e = b - a;
            float len = e.Length();
            // CCW polygon → outward 2D normal is the right-hand rotation of
            // the edge: (e.y, −e.x) before normalisation.
            edgeN[i] = len > 1e-12f ? new Vector2(e.Y, -e.X) / len : Vector2.UnitX;
        }
        return edgeN;
    }

    /// <summary>
    /// Per-vertex planar UVs anchored to the bottom-loop 2D AABB. Both caps
    /// reuse the same array so a stamped texture lands identically on top
    /// and bottom regardless of taper / twist (the top vertex order is
    /// preserved through the loop reversal stage, so capUV[i] consistently
    /// maps to the i-th cap vertex on either end).
    /// </summary>
    private static Vector2[] ComputePlanarCapUVs(IReadOnlyList<Vector2> loop)
    {
        int n = loop.Count;
        float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
        float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            Vector2 p = loop[i];
            if (p.X < minX) minX = p.X;
            if (p.X > maxX) maxX = p.X;
            if (p.Y < minZ) minZ = p.Y;
            if (p.Y > maxZ) maxZ = p.Y;
        }
        float rangeX = MathF.Max(maxX - minX, 1e-12f);
        float rangeZ = MathF.Max(maxZ - minZ, 1e-12f);
        var uvs = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            Vector2 p = loop[i];
            uvs[i] = new Vector2((p.X - minX) / rangeX, (p.Y - minZ) / rangeZ);
        }
        return uvs;
    }

    private static Vector3 SafeNormalize(Vector3 v, Vector2 fallback2D)
    {
        float len = v.Length();
        if (len > 1e-8f) return v / len;
        // Lift the 2D fallback into XZ — already unit length by construction.
        return new Vector3(fallback2D.X, 0f, fallback2D.Y);
    }
}
