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
/// Internally the profile is tessellated into a fine 2D polyline, the side
/// walls are emitted as a strip of triangles between bottom and top loops
/// (smooth-shaded for curved profiles, flat for linear), and the caps are
/// produced by ear-clipping the polygon — this handles concave shapes
/// (gears, stars, letters) without manual decomposition. Every triangle goes
/// into a single internal <see cref="BvhNode"/>, exactly as <see cref="Mesh"/>
/// does, so a complex extrusion costs the outer scene BVH one leaf.
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
    public Extrusion(
        IReadOnlyList<Vector2> profile,
        ExtrusionMode mode,
        float height,
        ExtrusionCaps caps,
        IMaterial material,
        IReadOnlyList<Vector2>? bezierControls = null,
        float twistDegrees = 0f,
        float taper = 1f,
        int curveSamples = 16)
    {
        if (profile == null || profile.Count < 3)
            throw new ArgumentException("Extrusion requires a profile of at least 3 points.", nameof(profile));
        if (!(height > 0f))
            throw new ArgumentException("Extrusion height must be positive.", nameof(height));
        if (!(taper > 0f))
            throw new ArgumentException("Extrusion taper must be positive.", nameof(taper));

        _material = material;

        // 1) Tessellate the profile into a fine closed polyline (CCW).
        var loop = BuildLoop(profile, mode, bezierControls, curveSamples);

        // 2) Ensure CCW orientation so wall outward normals point away from the
        //    interior. If the input is CW, reverse it in place.
        if (Polygon2D.SignedArea(loop) < 0f) loop.Reverse();

        // 3) Compute per-vertex 2D edge normals (smoothed for curved modes,
        //    sharp/face-only for linear). Used to give the side walls smooth
        //    shading along the profile direction (the v direction is always
        //    flat — sides are ruled along Y).
        bool smoothSides = mode != ExtrusionMode.Linear;
        var sideNormals2D = ComputeVertexNormals2D(loop, smoothSides);

        // 4) Build the bottom and top vertex rings with twist + taper.
        int n = loop.Count;
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

        // 5) Compute per-vertex 3D side normals: rotate the 2D edge normal at
        //    bottom level (no rotation applied) and at top level (apply twist),
        //    project onto the plane perpendicular to the local wall direction
        //    so smooth normals match the actual ruled surface.
        var bottomNormals3D = new Vector3[n];
        var topNormals3D    = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            Vector2 nrm = sideNormals2D[i];
            bottomNormals3D[i] = Vector3.Normalize(new Vector3(nrm.X, 0f, nrm.Y));
            float nx = cosT * nrm.X - sinT * nrm.Y;
            float nz = sinT * nrm.X + cosT * nrm.Y;
            topNormals3D[i] = Vector3.Normalize(new Vector3(nx, 0f, nz));
        }

        // 6) Compute UV V-coordinates as cumulative arc length (perimeter
        //    fraction) so a wrapping texture has no seam-stretch.
        var uArc = new float[n + 1];
        uArc[0] = 0f;
        for (int i = 0; i < n; i++)
        {
            float dx = loop[(i + 1) % n].X - loop[i].X;
            float dy = loop[(i + 1) % n].Y - loop[i].Y;
            uArc[i + 1] = uArc[i] + MathF.Sqrt(dx * dx + dy * dy);
        }
        float perim = uArc[n] > 0f ? uArc[n] : 1f;

        // 7) Emit triangles.
        _triangles = new List<IHittable>((n * 2) + Math.Max(0, 2 * (n - 2)));
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            Vector3 b0 = bottom[i], b1 = bottom[j];
            Vector3 t0 = top[i],    t1 = top[j];

            float u0 = uArc[i] / perim;
            float u1 = uArc[i + 1] / perim;

            if (smoothSides)
            {
                Vector3 nB0 = bottomNormals3D[i], nB1 = bottomNormals3D[j];
                Vector3 nT0 = topNormals3D[i],    nT1 = topNormals3D[j];

                _triangles.Add(new SmoothTriangle(
                    b0, b1, t1, nB0, nB1, nT1,
                    new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, 1f), material));
                _triangles.Add(new SmoothTriangle(
                    b0, t1, t0, nB0, nT1, nT0,
                    new Vector2(u0, 0f), new Vector2(u1, 1f), new Vector2(u0, 1f), material));
            }
            else
            {
                _triangles.Add(new Triangle(b0, b1, t1, material));
                _triangles.Add(new Triangle(b0, t1, t0, material));
            }
        }

        // 8) Caps via ear-clipping. Bottom cap winds CW so its outward normal
        //    points -Y; top cap keeps CCW for +Y.
        if (caps == ExtrusionCaps.Start || caps == ExtrusionCaps.Both)
        {
            var tris = Polygon2D.EarClip(loop);
            foreach (var (a, b, c) in tris)
                _triangles.Add(new Triangle(bottom[c], bottom[b], bottom[a], material));
        }
        if (caps == ExtrusionCaps.End || caps == ExtrusionCaps.Both)
        {
            var tris = Polygon2D.EarClip(loop);
            foreach (var (a, b, c) in tris)
                _triangles.Add(new Triangle(top[a], top[b], top[c], material));
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

    public bool Hit(Ray ray, float tMin, float tMax, ref HitRecord rec)
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
    /// Per-vertex 2D normals on a CCW closed polygon. When
    /// <paramref name="smooth"/> is true, each vertex normal is the
    /// length-weighted mean of its two adjacent edge normals (Phong-style
    /// across the silhouette); otherwise we duplicate the next-edge normal
    /// so adjacent triangles in the side wall keep the same flat face
    /// normal — the strip then renders with hard ridges, mirroring the
    /// faceted look of the linear lathe.
    /// </summary>
    private static Vector2[] ComputeVertexNormals2D(IReadOnlyList<Vector2> loop, bool smooth)
    {
        int n = loop.Count;
        var edgeN = new Vector2[n];
        var edgeLen = new float[n];
        for (int i = 0; i < n; i++)
        {
            Vector2 a = loop[i];
            Vector2 b = loop[(i + 1) % n];
            Vector2 e = b - a;
            float len = e.Length();
            edgeLen[i] = len;
            // CCW polygon → outward normal is edge rotated -90° in XZ plane
            // (i.e. (e.y, -e.x), giving a right-handed outward direction when
            // mapped to (x, 0, z) so the wall faces away from the interior).
            edgeN[i] = len > 1e-12f ? new Vector2(e.Y, -e.X) / len : Vector2.UnitX;
        }

        var result = new Vector2[n];
        if (smooth)
        {
            for (int i = 0; i < n; i++)
            {
                int prev = (i - 1 + n) % n;
                Vector2 sum = edgeN[prev] * edgeLen[prev] + edgeN[i] * edgeLen[i];
                float len = sum.Length();
                result[i] = len > 1e-12f ? sum / len : edgeN[i];
            }
        }
        else
        {
            // Sharp shading: vertex i sits between edge i-1 and edge i, but a
            // wall triangle for edge i uses (loop[i], loop[i+1]) — give both
            // endpoints edge i's normal so the triangle has a uniform normal.
            for (int i = 0; i < n; i++) result[i] = edgeN[i];
        }
        return result;
    }
}
