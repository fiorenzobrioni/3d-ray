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

    // Lathe-wide bounding cylinder used for a cheap pre-rejection ahead of the
    // per-segment loop. Rays that miss this cylinder cannot hit any segment.
    private readonly float _cylRadiusSq;
    private readonly float _cylYMin;
    private readonly float _cylYMax;

    // Sorted ascending by YMin: lets Hit short-circuit segments above the
    // current best t and skip ones below the ray's reachable Y range.
    private readonly int[] _segOrder;

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

        // Cap discs at the extrema. We anchor them on the *segment* extrema —
        // for spline modes the cubic can dip slightly past the profile point
        // (Catmull-Rom phantom tangents), and capping at the profile y would
        // leave a gap. The cap radius still uses the profile's r, since the
        // profile point is reached at u = 0/1 so r matches there exactly.
        _yBottom = _segments[0].YMin;
        _yTop    = _segments[n - 1].YMax;
        _rBottom = profile[0].X;
        _rTop    = profile[profile.Count - 1].X;
        _hasBottomCap = _rBottom > 1e-6f;
        _hasTopCap    = _rTop    > 1e-6f;

        // AABB = union of segment bounds + caps (caps are already inside segment bounds).
        AABB acc = _segments[0].LocalBounds;
        for (int i = 1; i < n; i++) acc = AABB.SurroundingBox(acc, _segments[i].LocalBounds);
        _bounds = acc;

        // Lathe-wide bounding cylinder: one quadratic ray-vs-cylinder is much
        // tighter than the cubic AABB for tall thin lathes (chess pieces!),
        // pruning rays that pass close to but never inside the swept volume.
        float rMaxAll = MathF.Max(MathF.Abs(acc.Min.X),
                          MathF.Max(MathF.Abs(acc.Max.X),
                          MathF.Max(MathF.Abs(acc.Min.Z), MathF.Abs(acc.Max.Z))));
        _cylRadiusSq = rMaxAll * rMaxAll;
        _cylYMin = acc.Min.Y;
        _cylYMax = acc.Max.Y;

        // Segments are typically authored in y-monotone order (the loader
        // enforces it). Build an order array so Hit can short-circuit far
        // segments once a closer hit is found, regardless of insertion order.
        _segOrder = new int[n];
        for (int i = 0; i < n; i++) _segOrder[i] = i;
        Array.Sort(_segOrder, (i, j) => _segments[i].YMin.CompareTo(_segments[j].YMin));

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
        // Lathe-wide bounding cylinder pre-rejection. The world-level BVH
        // has already kept us inside the axis-aligned box, but a tall slim
        // lathe occupies only the inscribed cylinder of that box. Solving
        // (ox+t·dx)² + (oz+t·dz)² ≤ rMax² combined with the Y-slab gives a
        // much tighter test before we touch any segment.
        if (!CylinderHit(ray, _cylRadiusSq, _cylYMin, _cylYMax, tMin, tMax))
            return false;

        bool hitAnything = false;
        float closestT = tMax;

        // Iterate segments in increasing-Y order. The ray's reachable Y-band
        // is monotone in t along the bounding cylinder, so once a segment's
        // YMin is above the closest hit we can stop scanning the upper tail.
        var segments = _segments;
        var order = _segOrder;
        for (int idx = 0; idx < order.Length; idx++)
        {
            int i = order[idx];
            var seg = segments[i];

            // Reject by AABB: tighter than the y-only slab and uses the
            // precomputed inverse direction. Cheap test, big win for spline
            // segments where the alternative is a Sturm chain build.
            if (!seg.LocalBounds.Hit(ray, tMin, closestT)) continue;

            if (seg.Hit(ray, tMin, closestT, out float tSeg, out Vector3 n, out float vSeg))
            {
                closestT = tSeg;
                FillHit(ref rec, ray, tSeg, n, vSeg, i);
                hitAnything = true;
            }
        }

        // Cap planes: skip when their t is already further than the best hit.
        // We also skip when the ray is grazing-parallel to Y.
        float dy = ray.Direction.Y;
        if (MathF.Abs(dy) > 1e-8f)
        {
            float invDy = 1f / dy;
            if (_hasBottomCap)
            {
                float t = (_yBottom - ray.Origin.Y) * invDy;
                if (t > tMin && t < closestT)
                {
                    float px = ray.Origin.X + t * ray.Direction.X;
                    float pz = ray.Origin.Z + t * ray.Direction.Z;
                    if (px * px + pz * pz <= _rBottom * _rBottom)
                    {
                        closestT = t;
                        FillCapHit(ref rec, ray, new Vector3(px, _yBottom, pz),
                                   t, -Vector3.UnitY, _rBottom);
                        hitAnything = true;
                    }
                }
            }

            if (_hasTopCap)
            {
                float t = (_yTop - ray.Origin.Y) * invDy;
                if (t > tMin && t < closestT)
                {
                    float px = ray.Origin.X + t * ray.Direction.X;
                    float pz = ray.Origin.Z + t * ray.Direction.Z;
                    if (px * px + pz * pz <= _rTop * _rTop)
                    {
                        closestT = t;
                        FillCapHit(ref rec, ray, new Vector3(px, _yTop, pz),
                                   t, Vector3.UnitY, _rTop);
                        hitAnything = true;
                    }
                }
            }
        }

        return hitAnything;
    }

    /// <summary>
    /// Returns whether the ray intersects the cylindrical slab
    /// <c>x²+z² ≤ r² ∧ y ∈ [yMin, yMax]</c> within <c>(tMin, tMax]</c>. Used
    /// as a tight pre-rejection that costs one quadratic + a Y-slab vs the
    /// full per-segment loop (which itself spends Sturm chains on splines).
    /// </summary>
    private static bool CylinderHit(
        Ray ray, float radiusSq, float yMin, float yMax,
        float tMin, float tMax)
    {
        float tLo = tMin, tHi = tMax;

        float dy = ray.Direction.Y;
        if (MathF.Abs(dy) > 1e-10f)
        {
            float invDy = 1f / dy;
            float ty0 = (yMin - ray.Origin.Y) * invDy;
            float ty1 = (yMax - ray.Origin.Y) * invDy;
            if (ty0 > ty1) (ty0, ty1) = (ty1, ty0);
            if (ty0 > tLo) tLo = ty0;
            if (ty1 < tHi) tHi = ty1;
            if (tHi < tLo) return false;
        }
        else if (ray.Origin.Y < yMin || ray.Origin.Y > yMax)
        {
            return false;
        }

        float ox = ray.Origin.X, oz = ray.Origin.Z;
        float dx = ray.Direction.X, dz = ray.Direction.Z;
        float a = dx * dx + dz * dz;
        float c = ox * ox + oz * oz - radiusSq;
        if (a < 1e-20f)
        {
            return c <= 0f;
        }
        float halfB = ox * dx + oz * dz;
        float disc = halfB * halfB - a * c;
        if (disc < 0f) return false;
        float sqrtD = MathF.Sqrt(disc);
        float invA = 1f / a;
        float tc0 = (-halfB - sqrtD) * invA;
        float tc1 = (-halfB + sqrtD) * invA;
        if (tc0 > tLo) tLo = tc0;
        if (tc1 < tHi) tHi = tc1;
        return tHi > tLo;
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
                            Vector3 normal, float capRadius)
    {
        rec.T = t;
        rec.Point = p;
        rec.LocalPoint = p;
        rec.SetFaceNormal(ray, normal);
        // Planar projection UV, like Cone caps.
        rec.U = (p.X + capRadius) / (2f * capRadius);
        rec.V = (p.Z + capRadius) / (2f * capRadius);
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

    /// <inheritdoc/>
    public float SurfaceArea => _totalArea;

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
                    // Horizontal step (Δy = 0, Δr ≠ 0): the frustum quadratic
                    // degenerates — emit an annular disc instead so the face
                    // is actually rendered. A perfectly degenerate step
                    // (Δy = Δr = 0) is dropped via a zero-area annulus.
                    if (MathF.Abs(b.Y - a.Y) < 1e-12f)
                        segs[i] = new AnnulusSegment(a.X, a.Y, b.X, b.Y);
                    else
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
