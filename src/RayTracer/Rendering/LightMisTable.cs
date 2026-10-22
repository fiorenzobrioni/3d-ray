using System.Numerics;
using RayTracer.Acceleration;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;

namespace RayTracer.Rendering;

/// <summary>
/// Which sampling strategies can produce a light direction, and with what
/// density: what the multiple-importance-sampling weights of next-event
/// estimation and of BSDF-sampled emission need so that, for every direction,
/// the weights of all the strategies that can produce it add up to one
/// (PIANO-REVIEW.md Phase 6).
///
/// <list type="bullet">
///   <item><description>A light sample's density carries the probability of
///   picking that light (<c>--light-sampling power|uniform</c>) on both sides;
///   the BSDF side used to leave it out.</description></item>
///   <item><description>The environment and every portal sample the same sky:
///   one group, whose densities add up in each weight (a portal used to add
///   its light on top of the environment's).</description></item>
///   <item><description>A light no BSDF ray can reach (a directional light's
///   disc, an area or sphere light without its visible proxy) keeps its whole
///   sample: nothing else delivers its light. The analytic sun is reachable:
///   a BSDF ray that leaves the scene inside its disc collects it, weighted.</description></item>
///   <item><description>An emissive surface's light is found by the surface
///   the ray hit, not only by its material: several surfaces sharing one
///   emissive material, or emitting through a mix, each keep their own pdf.</description></item>
/// </list>
/// </summary>
internal sealed class LightMisTable
{
    private readonly LightDistribution? _dist;
    private readonly LightBvh? _bvh;
    private readonly Dictionary<ILight, int> _index = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<ILight> _bsdfReachable = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<ILight> _skySet = new(ReferenceEqualityComparer.Instance);
    private readonly ILight[] _sky;
    private readonly PhysicalSun[] _suns;
    private readonly Dictionary<IMaterial, EmitterOwners> _emitters = new(ReferenceEqualityComparer.Instance);

    public LightMisTable(IReadOnlyList<ILight> lights, LightDistribution? dist, LightBvh? bvh = null)
    {
        _dist = dist;
        _bvh = bvh;
        for (int i = 0; i < lights.Count; i++) _index[lights[i]] = i;

        var sky = new List<ILight>();
        var suns = new List<PhysicalSun>();
        foreach (var light in lights)
        {
            switch (light)
            {
                case GeometryLight gl:
                    Owners(gl.SurfaceMaterial).Add(gl, gl.Geometry as IHittable);
                    _bsdfReachable.Add(gl);
                    break;
                case EnvironmentLight or PortalLight:
                    sky.Add(light);
                    _skySet.Add(light);
                    _bsdfReachable.Add(light);
                    break;
                case PhysicalSun sun:
                    suns.Add(sun);
                    _bsdfReachable.Add(sun);
                    break;
                default:
                    // Area and sphere lights are reachable through their
                    // visible proxy, when they have one.
                    if (light.ProxyMaterial is { } proxy)
                    {
                        Owners(proxy).Add(light, null);
                        _bsdfReachable.Add(light);
                    }
                    break;
            }
        }
        _sky = sky.ToArray();
        _suns = suns.ToArray();
        foreach (var owners in _emitters.Values) owners.Build();
    }

    private EmitterOwners Owners(IMaterial m)
    {
        if (!_emitters.TryGetValue(m, out var owners)) _emitters[m] = owners = new EmitterOwners();
        return owners;
    }

    /// <summary>True when next-event estimation samples one light per vertex instead of every light.</summary>
    public bool PicksOneLight => _dist != null || _bvh != null;

    /// <summary>Picks the light for a vertex at <paramref name="p"/>: its index (−1 for none) and probability.</summary>
    public (int Index, float Pick) Choose(Vector3 p, float u)
        => _bvh != null ? _bvh.Sample(p, u) : _dist!.Sample(u);

    /// <summary>
    /// Probability that next-event estimation at <paramref name="p"/> picks
    /// <paramref name="light"/> (1 when every light is sampled).
    /// </summary>
    public float Pick(ILight light, Vector3 p)
        => _bvh != null ? _bvh.Pmf(p, _index[light])
         : _dist != null ? _dist.PdfPick(_index[light])
         : 1f;

    /// <summary>True when a BSDF (or phase) sample can also deliver this light, so its samples share the MIS weight.</summary>
    public bool PairsWithBsdf(ILight light) => !light.IsDelta && _bsdfReachable.Contains(light);

    /// <summary>The analytic suns BSDF rays that leave the scene can reach.</summary>
    public ReadOnlySpan<PhysicalSun> Suns => _suns;

    /// <summary>True when the environment (or a portal onto it) is a next-event light.</summary>
    public bool HasSkyLights => _sky.Length > 0;

    /// <summary>
    /// Density of next-event estimation for a sample of <paramref name="light"/>
    /// toward <paramref name="dir"/> (<c>Own</c>), and the sum and sum of squares
    /// over every light strategy that samples the same emitter in that direction
    /// (the sky group for the environment and portals, the light alone otherwise).
    /// </summary>
    public (float Own, float Sum, float SumSq) NeeDensity(ILight light, float pick, Vector3 origin, Vector3 dir)
    {
        float own = pick * light.PdfSolidAngle(origin, dir);
        if (!_skySet.Contains(light)) return (own, own, own * own);
        var (sum, sumSq) = SkyDensity(origin, dir);
        return (own, sum, sumSq);
    }

    /// <summary>Summed next-event density of the sky group toward <paramref name="dir"/>.</summary>
    public (float Sum, float SumSq) SkyDensity(Vector3 origin, Vector3 dir)
    {
        float sum = 0f, sumSq = 0f;
        foreach (var light in _sky)
        {
            float p = Pick(light, origin) * light.PdfSolidAngle(origin, dir);
            sum += p;
            sumSq += p * p;
        }
        return (sum, sumSq);
    }

    /// <summary>
    /// The light whose surface a BSDF ray hit at distance <paramref name="t"/>,
    /// given the material it hit; null when no light owns it (an emitter no
    /// next-event sampler covers, which keeps its full weight).
    /// </summary>
    public ILight? EmitterOwner(IMaterial material, in Ray ray, float t)
        => _emitters.TryGetValue(material, out var owners) ? owners.Find(ray, t) : null;

    /// <summary>
    /// The lights that own surfaces with one material. With one, it is the
    /// owner; with several, the ray is intersected again with their surfaces
    /// alone (through a small BVH when there are many) and the one it meets at
    /// the hit distance owns the hit.
    /// </summary>
    private sealed class EmitterOwners
    {
        private readonly List<(ILight Light, IHittable? Surface)> _owners = new();
        private IHittable? _search;

        [ThreadStatic] private static int t_lastTag;

        public void Add(ILight light, IHittable? surface) => _owners.Add((light, surface));

        public void Build()
        {
            if (_owners.Count < 2) return;
            var tagged = new List<IHittable>(_owners.Count);
            for (int i = 0; i < _owners.Count; i++)
                if (_owners[i].Surface is { } s) tagged.Add(new Tagged(s, i));
            _search = tagged.Count > 4 ? new BvhNode(tagged) : new HittableList(tagged);
        }

        public ILight? Find(in Ray ray, float t)
        {
            if (_owners.Count == 1) return _owners[0].Light;
            if (_search == null) return null;
            var rec = new HitRecord();
            t_lastTag = -1;
            float slack = MathF.Max(1e-3f * t, MathUtils.RayEpsilon);
            if (!_search.Hit(ray, MathUtils.RayEpsilon, t + slack, ref rec) || t_lastTag < 0) return null;
            return MathF.Abs(rec.T - t) <= slack ? _owners[t_lastTag].Light : null;
        }

        /// <summary>
        /// Records which owner a hit came from. A traversal only reports a hit
        /// closer than every earlier one, so the last tag written is the closest.
        /// </summary>
        private sealed class Tagged : IHittable
        {
            private readonly IHittable _surface;
            private readonly int _tag;
            public Tagged(IHittable surface, int tag) { _surface = surface; _tag = tag; }
            public int Seed { get => _surface.Seed; set => _surface.Seed = value; }
            public AABB BoundingBox() => _surface.BoundingBox();
            public bool Hit(in Ray ray, float tMin, float tMax, ref HitRecord rec)
            {
                if (!_surface.Hit(ray, tMin, tMax, ref rec)) return false;
                t_lastTag = _tag;
                return true;
            }
        }
    }
}
