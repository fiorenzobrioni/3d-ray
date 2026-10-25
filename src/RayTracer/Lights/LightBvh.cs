using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;

namespace RayTracer.Lights;

/// <summary>
/// Many-light sampling with a bounding volume hierarchy over the lights
/// (Conty Estevez and Kulla 2018, "Importance Sampling of Many Lights with
/// Adaptive Tree Splitting"; the form of PBRT-v4's <c>BVHLightSampler</c>).
///
/// <para>
/// Each node bounds its lights' positions, total power and emission cone.
/// From a shading point the tree is walked from the root, each child picked
/// in proportion to an importance that bounds what it can deliver there:
/// power, falling with the squared distance, cut by the angle between the
/// lights' emission cone and the direction to the point. A light far away,
/// or facing away, is rarely picked; the probability of the light reached is
/// the product of the choices, and <see cref="Pmf"/> recomputes it for any
/// light by following its path, which the MIS weights need.
/// </para>
///
/// <para>
/// Lights at infinity (directional, sun, environment) have no position: they
/// are picked first, each with probability 1 / (infinite + 1), the tree
/// taking the remaining share. The receiver's normal is not used, so the
/// probabilities are those a BSDF-sampled ray can recompute from its origin
/// alone.
/// </para>
/// </summary>
public sealed class LightBvh
{
    private readonly struct Bounds
    {
        public readonly AABB Box;
        public readonly float Phi;
        public readonly Vector3 W;
        public readonly float CosThetaO, CosThetaE;
        public readonly bool TwoSided;
        public Bounds(AABB box, float phi, Vector3 w, float cosO, float cosE, bool twoSided)
        { Box = box; Phi = phi; W = w; CosThetaO = cosO; CosThetaE = cosE; TwoSided = twoSided; }
        public Vector3 Centroid => 0.5f * (Box.Min + Box.Max);
    }

    private struct Node
    {
        public Bounds B;
        public int Child0, Child1;   // -1 for a leaf
        public int Light;            // index in the light list, leaves only
    }

    private readonly List<Node> _nodes = new();
    private readonly int[] _infinite;
    private readonly Dictionary<int, (uint Bits, int Depth)> _paths = new();
    private readonly HashSet<int> _infiniteSet;
    private readonly float _pInfinite;      // probability of each infinite light

    public LightBvh(IReadOnlyList<ILight> lights, AABB sceneBounds)
    {
        var finite = new List<(int Index, Bounds B)>();
        var infinite = new List<int>();
        for (int i = 0; i < lights.Count; i++)
        {
            var b = BoundsOf(lights[i], sceneBounds);
            if (b is Bounds lb) { if (lb.Phi > 0f) finite.Add((i, lb)); }
            else infinite.Add(i);
        }
        _infinite = infinite.ToArray();
        _infiniteSet = new HashSet<int>(_infinite);
        int slots = _infinite.Length + (finite.Count > 0 ? 1 : 0);
        _pInfinite = slots > 0 ? 1f / slots : 0f;
        if (finite.Count > 0) Build(finite, 0, finite.Count, 0u, 0);
    }

    /// <summary>
    /// Picks a light for a point at <paramref name="p"/> from the uniform
    /// <paramref name="u"/>; returns its index and probability, or (-1, 0)
    /// when no light can reach the point.
    /// </summary>
    public (int Index, float Pmf) Sample(Vector3 p, float u)
    {
        float pInfTotal = _pInfinite * _infinite.Length;
        if (u < pInfTotal)
        {
            int k = Math.Min((int)(u / _pInfinite), _infinite.Length - 1);
            return (_infinite[k], _pInfinite);
        }
        if (_nodes.Count == 0) return (-1, 0f);
        u = Math.Min((u - pInfTotal) / (1f - pInfTotal), 0.99999994f);
        float pmf = 1f - pInfTotal;
        int node = 0;
        while (true)
        {
            Node n = _nodes[node];
            if (n.Child0 < 0) return (n.Light, pmf);
            float i0 = Importance(_nodes[n.Child0].B, p), i1 = Importance(_nodes[n.Child1].B, p);
            if (i0 <= 0f && i1 <= 0f) return (-1, 0f);
            float p0 = i0 / (i0 + i1);
            if (u < p0) { node = n.Child0; u = Math.Min(u / p0, 0.99999994f); pmf *= p0; }
            else { node = n.Child1; u = Math.Min((u - p0) / (1f - p0), 0.99999994f); pmf *= 1f - p0; }
        }
    }

    /// <summary>Probability that <see cref="Sample"/> picks light <paramref name="index"/> at <paramref name="p"/>.</summary>
    public float Pmf(Vector3 p, int index)
    {
        if (_infiniteSet.Contains(index)) return _pInfinite;
        if (!_paths.TryGetValue(index, out var path)) return 0f;
        float pmf = 1f - _pInfinite * _infinite.Length;
        int node = 0;
        for (int d = 0; d < path.Depth; d++)
        {
            Node n = _nodes[node];
            float i0 = Importance(_nodes[n.Child0].B, p), i1 = Importance(_nodes[n.Child1].B, p);
            if (i0 + i1 <= 0f) return 0f;
            bool second = ((path.Bits >> d) & 1u) != 0;
            pmf *= (second ? i1 : i0) / (i0 + i1);
            node = second ? n.Child1 : n.Child0;
        }
        return pmf;
    }

    private int Build(List<(int Index, Bounds B)> lights, int start, int end, uint bits, int depth)
    {
        int self = _nodes.Count;
        _nodes.Add(default);
        if (end - start == 1)
        {
            _nodes[self] = new Node { B = lights[start].B, Child0 = -1, Child1 = -1, Light = lights[start].Index };
            _paths[lights[start].Index] = (bits, depth);
            return self;
        }
        // Split at the median of the centroids along their widest extent
        // (depth is capped by the 32 path bits: a balanced tree of 2^32 lights).
        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
        for (int i = start; i < end; i++) { lo = Vector3.Min(lo, lights[i].B.Centroid); hi = Vector3.Max(hi, lights[i].B.Centroid); }
        Vector3 ext = hi - lo;
        int axis = ext.X >= ext.Y && ext.X >= ext.Z ? 0 : ext.Y >= ext.Z ? 1 : 2;
        lights.Sort(start, end - start, Comparer<(int, Bounds)>.Create((a, b) =>
            Component(a.Item2.Centroid, axis).CompareTo(Component(b.Item2.Centroid, axis))));
        int mid = (start + end) / 2;
        int c0 = Build(lights, start, mid, bits, depth + 1);
        int c1 = Build(lights, mid, end, bits | (1u << depth), depth + 1);
        _nodes[self] = new Node { B = Union(_nodes[c0].B, _nodes[c1].B), Child0 = c0, Child1 = c1, Light = -1 };
        return self;
    }

    private static float Component(Vector3 v, int a) => a == 0 ? v.X : a == 1 ? v.Y : v.Z;

    /// <summary>
    /// The bounds of a light with a position; null for a light at infinity.
    /// </summary>
    private static Bounds? BoundsOf(ILight light, AABB scene)
    {
        float phi = MathF.Max(0f, light.ApproximatePower(scene));
        switch (light)
        {
            case PointLight pl:
                return new Bounds(new AABB(pl.Position, pl.Position), phi, Vector3.UnitY, -1f, 0f, false);
            case SpotLight sl:
                return new Bounds(new AABB(sl.Position, sl.Position), phi, sl.Direction, 1f, sl.CosOuterAngle, false);
            case AreaLight al:
            {
                Vector3 a = al.Corner, b = al.Corner + al.U, c = al.Corner + al.V, d = al.Corner + al.U + al.V;
                var box = new AABB(Vector3.Min(Vector3.Min(a, b), Vector3.Min(c, d)), Vector3.Max(Vector3.Max(a, b), Vector3.Max(c, d)));
                return new Bounds(box, phi, Vector3.Normalize(Vector3.Cross(al.U, al.V)), 1f, 0f, false);
            }
            case PortalLight po:
            {
                Vector3 a = po.Anchor, b = po.Anchor + po.U, c = po.Anchor + po.V, d = po.Anchor + po.U + po.V;
                var box = new AABB(Vector3.Min(Vector3.Min(a, b), Vector3.Min(c, d)), Vector3.Max(Vector3.Max(a, b), Vector3.Max(c, d)));
                return new Bounds(box, phi, po.Normal, 1f, 0f, false);
            }
            case SphereLight sp:
                return new Bounds(new AABB(sp.Center - new Vector3(sp.Radius), sp.Center + new Vector3(sp.Radius)),
                                  phi, Vector3.UnitY, -1f, 0f, false);
            case GeometryLight gl when gl.Geometry is IHittable h:
            {
                // A flat one-sided emitter has one normal; anything else may
                // emit every way.
                if (gl.Geometry is Quad q)
                    return new Bounds(h.BoundingBox(), phi, Vector3.Normalize(Vector3.Cross(q.U, q.V)), 1f, 0f, false);
                return new Bounds(h.BoundingBox(), phi, Vector3.UnitY, -1f, 0f, false);
            }
            default:
                return null;   // directional, sun, environment
        }
    }

    // ── Importance (PBRT-v4 LightBounds::Importance, without the receiver normal) ──

    private static float Importance(in Bounds b, Vector3 p)
    {
        Vector3 pc = b.Centroid;
        float d2 = Vector3.DistanceSquared(p, pc);
        float halfDiag = 0.5f * (b.Box.Max - b.Box.Min).Length();
        d2 = MathF.Max(d2, halfDiag);
        if (d2 <= 0f) return b.Phi;

        Vector3 wi = Vector3.Normalize(p - pc);
        float cosW = Vector3.Dot(b.W, wi);
        if (b.TwoSided) cosW = MathF.Abs(cosW);
        float sinW = MathF.Sqrt(MathF.Max(0f, 1f - cosW * cosW));

        // Angle the box subtends at p.
        float cosB, sinB;
        float r2 = halfDiag * halfDiag;
        float dc2 = Vector3.DistanceSquared(p, pc);
        if (dc2 < r2) { cosB = -1f; sinB = 0f; }
        else { float s2 = r2 / dc2; sinB = MathF.Sqrt(s2); cosB = MathF.Sqrt(MathF.Max(0f, 1f - s2)); }

        float sinO = MathF.Sqrt(MathF.Max(0f, 1f - b.CosThetaO * b.CosThetaO));
        float cosX = CosSub(sinW, cosW, sinO, b.CosThetaO);
        float sinX = SinSub(sinW, cosW, sinO, b.CosThetaO);
        float cosP = CosSub(sinX, cosX, sinB, cosB);
        if (cosP <= b.CosThetaE) return 0f;
        return MathF.Max(0f, b.Phi * cosP / d2);
    }

    // cos(max(0, a − b)) and sin(max(0, a − b)) from the sines and cosines.
    private static float CosSub(float sinA, float cosA, float sinB, float cosB)
        => cosA > cosB ? 1f : cosA * cosB + sinA * sinB;
    private static float SinSub(float sinA, float cosA, float sinB, float cosB)
        => cosA > cosB ? 0f : sinA * cosB - cosA * sinB;

    private static Bounds Union(in Bounds a, in Bounds b)
    {
        if (a.Phi <= 0f) return b;
        if (b.Phi <= 0f) return a;
        var (w, cosO) = ConeUnion(a.W, a.CosThetaO, b.W, b.CosThetaO);
        var box = new AABB(Vector3.Min(a.Box.Min, b.Box.Min), Vector3.Max(a.Box.Max, b.Box.Max));
        return new Bounds(box, a.Phi + b.Phi, w, cosO, MathF.Min(a.CosThetaE, b.CosThetaE), a.TwoSided || b.TwoSided);
    }

    /// <summary>The smallest cone of directions containing two cones (PBRT-v4 DirectionCone::Union).</summary>
    private static (Vector3 W, float CosTheta) ConeUnion(Vector3 wa, float cosA, Vector3 wb, float cosB)
    {
        float ta = MathF.Acos(Math.Clamp(cosA, -1f, 1f)), tb = MathF.Acos(Math.Clamp(cosB, -1f, 1f));
        float td = MathF.Acos(Math.Clamp(Vector3.Dot(wa, wb), -1f, 1f));
        if (MathF.Min(td + tb, MathF.PI) <= ta) return (wa, cosA);
        if (MathF.Min(td + ta, MathF.PI) <= tb) return (wb, cosB);
        float to = 0.5f * (ta + td + tb);
        if (to >= MathF.PI) return (wa, -1f);
        Vector3 axis = Vector3.Cross(wa, wb);
        if (axis.LengthSquared() < 1e-12f) return (wa, -1f);
        axis = Vector3.Normalize(axis);
        float tr = to - ta;
        // Rodrigues: rotate wa about axis by tr.
        Vector3 w = wa * MathF.Cos(tr) + Vector3.Cross(axis, wa) * MathF.Sin(tr)
                  + axis * Vector3.Dot(axis, wa) * (1f - MathF.Cos(tr));
        return (Vector3.Normalize(w), MathF.Cos(to));
    }
}
