using System.Numerics;
using RayTracer.Core;
using RayTracer.Materials;

namespace RayTracer.Geometry;

/// <summary>
/// Revolution mode for a <see cref="Lathe"/> profile.
/// </summary>
public enum LatheMode
{
    /// <summary>
    /// Polyline profile — each consecutive pair of points defines a frustum
    /// (analytic quadratic). Produces the "faceted" look of a real lathe cut
    /// with hard vertex ridges. Fastest mode.
    /// </summary>
    Linear,

    /// <summary>
    /// Centripetal Catmull-Rom through the given points — passes exactly
    /// through every control point and is C¹ continuous. Chosen over the
    /// uniform / chord-length variants because it is the only parameterisation
    /// that never self-intersects and never develops cusps in clustered
    /// regions (Yuksel et al. 2011).
    /// </summary>
    CatmullRom,

    /// <summary>
    /// Explicit cubic Bezier segments. The profile gives the segment
    /// endpoints; a parallel <c>profile_bezier_controls</c> list supplies
    /// the 4 control points per segment for full authoring control.
    /// </summary>
    Bezier,
}

/// <summary>
/// A surface of revolution (lathe) around the local Y axis, built from a
/// sequence of profile points <c>(r_i, y_i)</c>. Depending on the selected
/// <see cref="LatheMode"/>, consecutive points are joined by linear segments
/// (analytic quadratic intersection) or by cubic splines (implicit degree-6
/// polynomial solved numerically with <see cref="SturmSolver"/>).
///
/// Cap discs close the lathe at its Y extrema whenever the profile leaves the
/// axis (<c>r &gt; 0</c>) at that end, matching the convention used by the
/// engine's cylinder and cone primitives.
///
/// Implements <see cref="ISamplable"/> so that an emissive Lathe becomes a
/// GeometryLight with correctly area-weighted NEE samples — selection among
/// segments and caps is proportional to world-space area.
/// </summary>
public sealed class Lathe : IHittable, ISamplable
{
    private readonly ILatheSegment[] _segments;
    private readonly IMaterial _material;

    // Cumulative arc length at the start of each segment, for UV V mapping.
    private readonly float[] _vBase;
    private readonly float _totalArc;

    // Cap plane descriptors. A cap only exists when the profile leaves the
    // axis at that end of the lathe.
    private readonly bool _hasBottomCap;
    private readonly bool _hasTopCap;
    private readonly float _yBottom;
    private readonly float _yTop;
    private readonly float _rBottom;
    private readonly float _rTop;

    // NEE weighting: area of every samplable piece and the cumulative CDF.
    private readonly float _totalArea;
    private readonly float[] _areaCdf;

    private readonly AABB _bounds;

    public Lathe(IReadOnlyList<Vector2> profile, LatheMode mode, IMaterial material,
                 IReadOnlyList<Vector2>? bezierControls = null)
    {
        if (profile == null || profile.Count < 2)
            throw new ArgumentException("Lathe requires at least 2 profile points.", nameof(profile));

        _material = material;
        _segments = BuildSegments(profile, mode, bezierControls);

        int n = _segments.Length;
        _vBase = new float[n + 1];
        _vBase[0] = 0f;
        for (int i = 0; i < n; i++)
            _vBase[i + 1] = _vBase[i] + _segments[i].ArcLength;
        _totalArc = _vBase[n] > 0f ? _vBase[n] : 1f;

        // Cap discs at the extrema.
        _yBottom = profile[0].Y;
        _yTop    = profile[profile.Count - 1].Y;
        _rBottom = profile[0].X;
        _rTop    = profile[profile.Count - 1].X;
        _hasBottomCap = _rBottom > 1e-6f;
        _hasTopCap    = _rTop    > 1e-6f;

        // AABB = union of segment bounds + caps (caps are already inside segment bounds).
        AABB acc = _segments[0].LocalBounds;
        for (int i = 1; i < n; i++) acc = AABB.SurroundingBox(acc, _segments[i].LocalBounds);
        _bounds = acc;

        // NEE CDF: segments first, then caps (if any).
        int entries = n + (_hasBottomCap ? 1 : 0) + (_hasTopCap ? 1 : 0);
        _areaCdf = new float[entries];
        float areaAcc = 0f;
        for (int i = 0; i < n; i++)
        {
            areaAcc += _segments[i].LateralArea;
            _areaCdf[i] = areaAcc;
        }
        int cursor = n;
        if (_hasBottomCap) { areaAcc += MathF.PI * _rBottom * _rBottom; _areaCdf[cursor++] = areaAcc; }
        if (_hasTopCap)    { areaAcc += MathF.PI * _rTop    * _rTop;    _areaCdf[cursor++] = areaAcc; }
        _totalArea = areaAcc > 0f ? areaAcc : 1f;
    }

    public int Seed { get; set; }
    public AABB BoundingBox() => _bounds;

    // ─────────────────────────────────────────────────────────────────────────
    // Hit
    // ─────────────────────────────────────────────────────────────────────────

    public bool Hit(Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        bool hitAnything = false;
        float closestT = tMax;

        for (int i = 0; i < _segments.Length; i++)
        {
            var seg = _segments[i];

            // Cheap Y-slab reject: if the ray can't reach this segment's Y
            // range in (tMin, closestT], skip the expensive intersection.
            if (!SegmentYReachable(ray, seg.YMin, seg.YMax, tMin, closestT)) continue;

            if (seg.Hit(ray, tMin, closestT, out float tSeg, out Vector3 n, out float vSeg))
            {
                closestT = tSeg;
                FillHit(ref rec, ray, tSeg, n, vSeg, i);
                hitAnything = true;
            }
        }

        if (_hasBottomCap && MathF.Abs(ray.Direction.Y) > 1e-8f)
        {
            float t = (_yBottom - ray.Origin.Y) / ray.Direction.Y;
            if (t > tMin && t < closestT)
            {
                Vector3 p = ray.At(t);
                if (p.X * p.X + p.Z * p.Z <= _rBottom * _rBottom)
                {
                    closestT = t;
                    FillCapHit(ref rec, ray, p, t, -Vector3.UnitY, _rBottom, 0f);
                    hitAnything = true;
                }
            }
        }

        if (_hasTopCap && MathF.Abs(ray.Direction.Y) > 1e-8f)
        {
            float t = (_yTop - ray.Origin.Y) / ray.Direction.Y;
            if (t > tMin && t < closestT)
            {
                Vector3 p = ray.At(t);
                if (p.X * p.X + p.Z * p.Z <= _rTop * _rTop)
                {
                    closestT = t;
                    FillCapHit(ref rec, ray, p, t, Vector3.UnitY, _rTop, 1f);
                    hitAnything = true;
                }
            }
        }

        return hitAnything;
    }

    private static bool SegmentYReachable(Ray ray, float yMin, float yMax, float tMin, float tMax)
    {
        float dy = ray.Direction.Y;
        if (MathF.Abs(dy) < 1e-10f)
            return ray.Origin.Y >= yMin && ray.Origin.Y <= yMax;

        float invDy = 1f / dy;
        float t0 = (yMin - ray.Origin.Y) * invDy;
        float t1 = (yMax - ray.Origin.Y) * invDy;
        if (t0 > t1) (t0, t1) = (t1, t0);
        return t1 >= tMin && t0 <= tMax;
    }

    private void FillHit(ref HitRecord rec, Ray ray, float t, Vector3 outwardNormal,
                         float vSegment, int segmentIndex)
    {
        rec.T = t;
        Vector3 p = ray.At(t);
        rec.Point = p;
        rec.LocalPoint = p;
        rec.SetFaceNormal(ray, outwardNormal);

        float theta = MathF.Atan2(p.Z, p.X);
        rec.U = (theta + MathF.PI) / (2f * MathF.PI);
        // V maps to cumulative arc length normalised to the full profile.
        float segArc = _segments[segmentIndex].ArcLength;
        rec.V = (_vBase[segmentIndex] + vSegment * segArc) / _totalArc;

        // Azimuthal tangent, same convention as Cone/Cylinder.
        Vector3 tDir = new Vector3(-p.Z, 0f, p.X);
        rec.Tangent = tDir.LengthSquared() < 1e-8f
            ? Vector3.UnitX
            : Vector3.Normalize(tDir);
        rec.Bitangent = Vector3.Normalize(Vector3.Cross(outwardNormal, rec.Tangent));

        rec.ObjectSeed = Seed;
        rec.Material = _material;
    }

    private void FillCapHit(ref HitRecord rec, Ray ray, Vector3 p, float t,
                            Vector3 normal, float capRadius, float vCoord)
    {
        rec.T = t;
        rec.Point = p;
        rec.LocalPoint = p;
        rec.SetFaceNormal(ray, normal);
        // Planar projection UV, like Cone caps.
        rec.U = (p.X + capRadius) / (2f * capRadius);
        rec.V = (p.Z + capRadius) / (2f * capRadius);
        // Unused by V mapping convention; override the profile V with the cap flag.
        _ = vCoord;
        rec.Tangent = Vector3.UnitX;
        rec.Bitangent = Vector3.UnitZ;
        rec.ObjectSeed = Seed;
        rec.Material = _material;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Sampling (NEE)
    // ─────────────────────────────────────────────────────────────────────────

    public (Vector3 Point, Vector3 Normal, Vector2 Uv, float Area) Sample()
        => SampleImpl(MathUtils.RandomFloat(), MathUtils.RandomFloat(), MathUtils.RandomFloat());

    public (Vector3 Point, Vector3 Normal, Vector2 Uv, float Area) SampleStratified(int sampleIndex, int sqrtSamples)
    {
        float inv = 1f / sqrtSamples;
        int su = sampleIndex % sqrtSamples;
        int sv = sampleIndex / sqrtSamples;
        float xiPart = MathUtils.RandomFloat();
        float xi1 = (su + MathUtils.RandomFloat()) * inv;
        float xi2 = (sv + MathUtils.RandomFloat()) * inv;
        return SampleImpl(xiPart, xi1, xi2);
    }

    private (Vector3 Point, Vector3 Normal, Vector2 Uv, float Area) SampleImpl(
        float xiPart, float xi1, float xi2)
    {
        float target = xiPart * _totalArea;
        int idx = 0;
        while (idx < _areaCdf.Length - 1 && _areaCdf[idx] < target) idx++;

        if (idx < _segments.Length)
        {
            var (p, nrm, vSeg, theta) = _segments[idx].Sample(xi1, xi2);
            float segArc = _segments[idx].ArcLength;
            float v = (_vBase[idx] + vSeg * segArc) / _totalArc;
            float u = theta / (2f * MathF.PI);
            return (p, nrm, new Vector2(u, v), _totalArea);
        }

        // Cap sampling — uniform disc, same derivation as Cone.SampleDisk.
        bool bottom = _hasBottomCap && idx == _segments.Length;
        float capR = bottom ? _rBottom : _rTop;
        float capY = bottom ? _yBottom : _yTop;
        Vector3 n = bottom ? -Vector3.UnitY : Vector3.UnitY;

        float r = MathF.Sqrt(xi1) * capR;
        float thetaC = xi2 * 2f * MathF.PI;
        float x = r * MathF.Cos(thetaC);
        float z = r * MathF.Sin(thetaC);
        var point = new Vector3(x, capY, z);
        float invR = capR > 0f ? 1f / capR : 0f;
        var uv = new Vector2((x * invR + 1f) * 0.5f, (z * invR + 1f) * 0.5f);
        return (point, n, uv, _totalArea);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Segment construction
    // ─────────────────────────────────────────────────────────────────────────

    private static ILatheSegment[] BuildSegments(
        IReadOnlyList<Vector2> profile, LatheMode mode,
        IReadOnlyList<Vector2>? bezierControls)
    {
        switch (mode)
        {
            case LatheMode.Linear:
            {
                var segs = new ILatheSegment[profile.Count - 1];
                for (int i = 0; i < segs.Length; i++)
                {
                    var a = profile[i];
                    var b = profile[i + 1];
                    segs[i] = new FrustumSegment(a.X, a.Y, b.X, b.Y);
                }
                return segs;
            }

            case LatheMode.CatmullRom:
            {
                // Catmull-Rom requires at least 4 control points; the caller is
                // expected to have downgraded shorter profiles to Linear already.
                var beziers = CatmullRomToBezier.Convert(profile);
                var segs = new ILatheSegment[beziers.Count];
                for (int i = 0; i < segs.Length; i++)
                {
                    var q = beziers[i];
                    segs[i] = new SplineSegment(q[0], q[1], q[2], q[3]);
                }
                return segs;
            }

            case LatheMode.Bezier:
            {
                if (bezierControls == null || bezierControls.Count != 4 * (profile.Count - 1))
                    throw new ArgumentException(
                        $"Bezier lathe requires exactly 4*(N-1) = {4 * (profile.Count - 1)} " +
                        $"control points, got {bezierControls?.Count ?? 0}.", nameof(bezierControls));

                var segs = new ILatheSegment[profile.Count - 1];
                for (int i = 0; i < segs.Length; i++)
                {
                    int k = i * 4;
                    segs[i] = new SplineSegment(
                        bezierControls[k],
                        bezierControls[k + 1],
                        bezierControls[k + 2],
                        bezierControls[k + 3]);
                }
                return segs;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported lathe mode.");
        }
    }
}
