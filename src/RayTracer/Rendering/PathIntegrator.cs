using System.Numerics;
using RayTracer.Core;
using RayTracer.Core.Sampling;
using RayTracer.Geometry;
using RayTracer.Materials;
using RayTracer.Volumetrics;

namespace RayTracer.Rendering;

/// <summary>
/// Most bounces of each kind a path may take (PIANO-REVIEW.md Phase 7): a
/// diffuse, glossy or transmission bounce, a scatter in a medium, a
/// cut-out the ray passes through. The total vertex budget is the
/// renderer's max depth; cut-outs are outside it.
/// </summary>
public readonly record struct PathDepths(int Diffuse, int Glossy, int Transmission, int Volume, int Transparent)
{
    /// <summary>Default cap on cut-outs crossed by one path.</summary>
    public const int DefaultTransparent = 16;

    /// <summary>No limit below the total: every kind may use all <paramref name="total"/> bounces.</summary>
    public static PathDepths Uniform(int total) => new(total, total, total, total, Math.Max(DefaultTransparent, total));
}

/// <summary>
/// The path tracer as one iterative loop (PIANO-REVIEW.md Phase 7): a path
/// carries its throughput β explicitly, every vertex adds β times what it
/// emits or gathers to the estimate, and the next vertex starts where the
/// sampled bounce leads. The recursion it replaces returned radiance up the
/// call stack and multiplied it by each bounce's attenuation on the way back;
/// the loop does the same arithmetic forwards, so per-vertex state (lobe,
/// depth counters, the path so far) is in one place for path guiding,
/// per-lobe depths and light path expressions.
/// </summary>
public partial class Renderer
{
    /// <summary>
    /// State of the path being traced. Mutated in place by the loop: the
    /// medium and IOR stacks too, which the recursion had to copy at every
    /// refraction so the caller's stack survived the return.
    /// </summary>
    private struct PathState
    {
        public Ray Ray;
        public Vector3 Beta;            // throughput from the camera to the current ray
        public float PrevPdf;           // density of the bounce that produced Ray (0 for delta)
        public bool PrevIsDelta;        // camera ray or delta bounce: emission at full weight
        public Vector3 SigmaA;          // absorption of the dielectric interior the ray travels in
        public RayCategory Category;    // which kind of ray this is, for visibility flags
        public int CausticState;        // see CausticNone / CausticDiffuse / CausticSuppress
        public float EtaScale;          // product of η² over the refractions so far (for the roulette)

        // Bounces taken: in total (cut-outs aside) and of each kind.
        public int Bounces, Diffuse, Glossy, Transmission, Volume, Transparent;
        public MediumStack Mediums;
        public IorStack Iors;

        // The estimate. With the optional indirect clamp (-C with
        // --indirect-clamp-factor) what arrives through the first vertex's
        // bounce is kept apart (Rest) and clamped as a whole at the end.
        public Vector3 First, Rest;
        public bool PastFirstBounce;

        public void Add(Vector3 contribution)
        {
            contribution = SanitizeRadiance(contribution);
            if (PastFirstBounce) Rest += contribution; else First += contribution;
        }
    }

    /// <summary>
    /// Radiance arriving at the camera along <paramref name="cameraRay"/>.
    /// </summary>
    private Vector3 TracePath(Ray cameraRay)
    {
        var path = new PathState
        {
            Ray = cameraRay,
            Beta = Vector3.One,
            PrevPdf = 0f,
            PrevIsDelta = true,
            SigmaA = Vector3.Zero,
            Category = RayCategory.Camera,
            CausticState = CausticNone,
            EtaScale = 1f,
            Mediums = new MediumStack(),
            Iors = new IorStack(),
        };

        while (path.Bounces < _maxDepth)
        {
            // Each vertex has its own block of sampler dimensions.
            Sampler.BeginVertex();

            float betaMax = MathF.Max(path.Beta.X, MathF.Max(path.Beta.Y, path.Beta.Z));
            if (float.IsNaN(betaMax) || float.IsInfinity(betaMax))
            {
                Interlocked.Increment(ref s_nonFiniteCount);
                break;
            }
            if (!(betaMax > 0f)) break;

            bool hit = FindVisibleHit(ref path, out HitRecord rec);
            Ray ray = path.Ray;

            // AOV depth: the first surface the camera ray meets (camera rays
            // are not normalised, so scale by |direction|).
            if (_captureAovs && hit && path.Category == RayCategory.Camera
                && t_aovProbe is { Pending: true, HaveDepth: false } depthProbe)
            {
                depthProbe.Depth = rec.T * ray.Direction.Length();
                depthProbe.HaveDepth = true;
            }

            // Analytic texture footprint, for rays that carry differentials.
            if (hit && ray.HasDifferentials)
            {
                Vector3 dpdu = rec.DpDu.LengthSquared() > 0f ? rec.DpDu : rec.Tangent;
                Vector3 dpdv = rec.DpDv.LengthSquared() > 0f ? rec.DpDv : rec.Bitangent;
                rec.Footprint = FootprintMath.Compute(ray, rec.Point, rec.Normal, dpdu, dpdv);
            }

            // ── Participating medium along the segment ──────────────────────
            // The entity-bound medium on top of the stack, else the global one.
            IMedium? medium = path.Mediums.Top ?? _globalMedium;
            if (medium != null)
            {
                float tMax = hit ? rec.T : 1e30f;
                bool scattered = medium.Sample(ray, tMax, out float tMed, out Vector3 mediumBeta, out _);
                path.Beta *= mediumBeta;
                if (scattered)
                {
                    path.Beta *= BeerLambert(path.SigmaA, tMed);
                    if (!MediumVertex(ref path, medium, tMed)) break;
                    continue;
                }
            }

            // ── Escape to the sky ───────────────────────────────────────────
            if (!hit)
            {
                // A ray escaping from inside an absorbing interior never
                // reaches the sky (exp(-σ_a ∞) = 0).
                if (!HasAbsorption(path.SigmaA))
                    path.Add(path.Beta * SampleSky(ray, path.PrevPdf, path.PrevIsDelta, path.CausticState, path.Category));
                break;
            }

            path.Beta *= BeerLambert(path.SigmaA, rec.T);

            // ── Surface vertex ──────────────────────────────────────────────
            if (!SurfaceVertex(ref path, ref rec)) break;
        }

        return float.IsPositiveInfinity(_indirectMaxSampleRadiance)
            ? path.First + path.Rest
            : path.First + ClampRadianceIndirect(path.Rest);
    }

    /// <summary>
    /// Intersects the path's ray with the scene, walking past surfaces hidden
    /// from this kind of ray (per-category visibility flags), at most eight.
    /// Advances <c>path.Ray</c> past the skipped surfaces and stamps the
    /// path's time on the hit.
    /// </summary>
    private bool FindVisibleHit(ref PathState path, out HitRecord rec)
    {
        HitVisibilityMask ignore = CategoryToMask(path.Category);
        const int MaxVisibilitySkips = 8;
        Ray ray = path.Ray;
        bool hit;
        int skips = 0;
        while (true)
        {
            rec = new HitRecord();
            hit = _world.Hit(ray, MathUtils.RayEpsilon, MathUtils.Infinity, ref rec);
            if (!hit || ignore == HitVisibilityMask.None || (rec.VisibilityMask & ignore) == 0)
                break;
            // Same differentials past the hidden surface, so the footprint on
            // what lies behind still tracks the pixel.
            Vector3 origin = MathUtils.OffsetOrigin(rec.Point, ray.Direction);
            ray = ray.HasDifferentials
                ? new Ray(origin, ray.Direction, ray.Differentials, ray.Time)
                : new Ray(origin, ray.Direction, ray.Time);
            if (++skips >= MaxVisibilitySkips) break;
        }
        rec.Time = ray.Time;
        path.Ray = ray;
        return hit;
    }

    /// <summary>
    /// A scattering event in a medium at distance <paramref name="tMed"/>:
    /// in-scattered light by next-event estimation, then a phase-sampled
    /// continuation. False when the path ends here.
    /// </summary>
    private bool MediumVertex(ref PathState path, IMedium medium, float tMed)
    {
        Ray ray = path.Ray;
        Vector3 p = ray.Origin + ray.Direction * tMed;

        // Volumes have no albedo or normal: featureless guides.
        if (_captureAovs && t_aovProbe is { Pending: true } medProbe)
        {
            if (!medProbe.HaveDepth)
            {
                medProbe.Depth = tMed * ray.Direction.Length();
                medProbe.HaveDepth = true;
            }
            medProbe.CommitAs(Vector3.One, Vector3.Zero);
        }

        IHittable? boundEntity = path.Mediums.Top != null ? path.Mediums.TopEntity : null;
        path.Add(path.Beta * ComputeDirectLightingMedium(p, ray.Direction, medium, boundEntity, ray.Time));

        path.PastFirstBounce = true;
        if (++path.Volume > _depths.Volume) return false;
        path.Bounces++;
        if (!SurviveRoulette(ref path)) return false;

        var (wi, phasePdf) = medium.Phase.Sample(ray.Direction);
        float phaseVal = medium.Phase.Evaluate(ray.Direction, wi);
        if (!(phasePdf > 1e-20f)) return false;
        path.Beta *= phaseVal / phasePdf;

        path.Ray = new Ray(p, wi, ray.Time);
        path.PrevPdf = phasePdf;
        path.PrevIsDelta = false;
        path.Category = RayCategory.Diffuse;   // a volume bounce scatters like a diffuse one
        path.CausticState = NextCausticState(path.CausticState, isDelta: false, shadowPassable: false);
        return true;
    }

    /// <summary>
    /// A surface hit: shading-normal perturbations, emission (MIS-weighted),
    /// next-event estimation and the photon caustics, then the sampled
    /// bounce (with its medium and IOR transitions and the random-walk
    /// subsurface hand-off). False when the path ends here.
    /// </summary>
    private bool SurfaceVertex(ref PathState path, ref HitRecord rec)
    {
        Ray ray = path.Ray;
        IMaterial? material = rec.Material;

        // The unperturbed shading normal, reference of the shadow-terminator term.
        rec.ShadingNg = rec.Normal;
        if (material?.NormalMap != null && rec.Tangent.LengthSquared() > 0.5f)
            ApplyNormalMap(ref rec, material.NormalMap);
        if (material?.BumpMap != null && rec.Tangent.LengthSquared() > 0.5f)
            ApplyBumpMap(ref rec, material.BumpMap);
        // Mesh autobump after the material's maps: normal map → bump → autobump.
        if (rec.AutoBump != null && rec.Tangent.LengthSquared() > 0.5f)
            ApplyBumpMap(ref rec, rec.AutoBump);

        // AOV: this surface is the guide unless a delta bounce follows it.
        if (_captureAovs && t_aovProbe is { Pending: true } aovProbe)
        {
            aovProbe.TentNormal = rec.Normal;
            aovProbe.TentAlbedo = material != null ? Clamp01(material.AovAlbedo(in rec)) : Vector3.One;
        }

        // Emission, MIS-weighted; dropped when another estimator already
        // carries it (photon caustics, transparent shadows).
        if (material != null && !SuppressChainEmission(path.CausticState))
        {
            Vector3 raw = material.Emit(rec.U, rec.V, rec.LocalPoint, rec.ObjectSeed, rec.FrontFace);
            if (raw.X > 0f || raw.Y > 0f || raw.Z > 0f)
                path.Add(path.Beta * WeightEmission(raw, material, ray, rec.T, path.PrevPdf, path.PrevIsDelta));
        }

        Vector3 viewDir = Vector3.Normalize(-ray.Direction);

        // Relative IOR from the IOR stack (nested dielectrics): entering, the
        // medium outside is the stack top; leaving, it is the enclosing one.
        if (material != null && material.TryGetDielectricIor(in rec, out float matIor) && matIor > 0f)
            rec.RelativeEta = rec.FrontFace ? (path.Iors.Top / matIor) : (matIor / path.Iors.Enclosing);

        // Next-event estimation, with the medium the surface sits in.
        bool needsLight = material?.NeedsDirectLighting ?? true;
        if (needsLight)
        {
            IMedium? shadowMedium = path.Mediums.Top ?? _globalMedium;
            IHittable? shadowEntity = path.Mediums.Top != null ? path.Mediums.TopEntity : null;
            Vector3 direct = DirectLightingAt(in rec, viewDir, material, shadowMedium, shadowEntity);
            if (_causticsActive && material != null && !material.IsDeltaScatter)
                direct += GatherCaustics(in rec, viewDir, material);
            path.Add(path.Beta * direct);
        }

        if (material == null) return false;

        // ── Sampled bounce ──────────────────────────────────────────────────
        BsdfSample? sampled = material.Sample(viewDir, rec);
        if (!sampled.HasValue) return false;
        BsdfSample s = sampled.Value;

        Vector3 attenuation;
        if (s.IsDelta)
            attenuation = s.F;          // delta lobe: F carries the whole weight
        else
        {
            float absCos = MathF.Abs(Vector3.Dot(rec.Normal, s.Wo));
            if (absCos <= 0f || s.Pdf <= 0f) return false;
            // The shadow-terminator factor belongs to the integrand: the
            // sampled bounce carries it as NEE does (§3.11).
            attenuation = s.F * (absCos * LightSideTerminator(in rec, s.Wo) / s.Pdf);
        }

        // AOV: delta lobes accumulate their tint, the first non-delta lobe
        // commits the surface. Before the roulette, so the guides stay noise-free.
        if (_captureAovs && t_aovProbe is { Pending: true } aov)
        {
            if (s.IsDelta) aov.AlbedoWeight *= Clamp01(s.F);
            else aov.Commit();
        }

        // A cut-out is not a bounce: the ray goes on as the same ray (same
        // category for the visibility flags, still the camera's through a
        // leaf), and with β unchanged the roulette has nothing new to weigh.
        bool cutOut = s.Lobe == BsdfLobe.Transparent;
        path.Beta *= attenuation;
        if (!cutOut) path.PastFirstBounce = true;
        if (!CountBounce(ref path, s.Lobe)) return false;
        if (s.Lobe == BsdfLobe.Transmission) path.EtaScale *= s.Eta * s.Eta;
        if (!cutOut && !SurviveRoulette(ref path)) return false;

        // Origin on the side of the surface the bounce leaves from.
        Vector3 offsetDir = Vector3.Dot(s.Wo, rec.Normal) >= 0f ? rec.Normal : -rec.Normal;
        var next = new Ray(MathUtils.OffsetOrigin(rec.Point, offsetDir), s.Wo, rec.Time);

        int nextCaustic = NextCausticState(path.CausticState, s.IsDelta, ShadowPassable(material, in rec, s.Wo));
        path.Ray = next;
        path.PrevPdf = s.IsDelta ? 0f : s.Pdf;
        path.PrevIsDelta = s.IsDelta;
        // A refraction switches the absorption tracked along the next segment.
        path.SigmaA = s.NextSegmentAbsorption ?? path.SigmaA;
        path.Category = s.Lobe switch
        {
            BsdfLobe.Diffuse     => RayCategory.Diffuse,
            BsdfLobe.Glossy      => RayCategory.Glossy,
            BsdfLobe.Transparent => path.Category,
            _                    => RayCategory.Transmission,
        };
        path.CausticState = nextCaustic;

        if (s.Transition == MediumTransition.None) return true;

        // ── Refraction through a medium boundary ────────────────────────────
        // Entering pushes the interior medium (with the entity, so NEE inside
        // can clip its shadow rays to the in-medium segment) and the
        // material's IOR; leaving pops both.
        if (s.Transition == MediumTransition.Enter)
        {
            path.Mediums.Push(ResolvePushedMedium(rec.MediumIface.Interior), rec.EntityRoot);
            path.Iors.Push(material.TryGetDielectricIor(in rec, out float pushIor) ? pushIor : path.Iors.Top);
        }
        else
        {
            path.Mediums.Pop();
            path.Iors.Pop();
            return true;
        }

        // Entering a scattering medium bound to an entity: the random walk
        // takes over until the light leaves the entity again.
        if (_sssMode == SssMode.Auto
            && path.Mediums.Top is HomogeneousMedium interior
            && IsScatteringMedium(interior)
            && rec.EntityRoot is { } entityRoot)
        {
            var walk = RandomWalkSubsurface(next, interior, entityRoot, ref path.Mediums, ref path.Iors, path.Beta);
            path.Add(path.Beta * walk.Radiance);
            if (!walk.Escaped) return false;
            path.Beta *= walk.Throughput;
            path.Ray = walk.EscapeRay;
            // An escape after a phase scatter is the phase-sampling half of
            // the NEE done at that scatter; before any scatter it continues
            // the entry refraction, a delta event.
            path.PrevPdf = walk.Scattered ? walk.LastPhasePdf : 0f;
            path.PrevIsDelta = !walk.Scattered;
            path.Category = walk.Scattered ? RayCategory.Diffuse : RayCategory.Transmission;
        }
        return true;
    }

    /// <summary>
    /// Counts a bounce of kind <paramref name="lobe"/>; false when its kind
    /// (or, for a cut-out, the cut-out limit) is used up. The total is
    /// checked by the loop.
    /// </summary>
    private bool CountBounce(ref PathState path, BsdfLobe lobe)
    {
        switch (lobe)
        {
            case BsdfLobe.Transparent:
                return ++path.Transparent <= _depths.Transparent;
            case BsdfLobe.Diffuse:
                if (++path.Diffuse > _depths.Diffuse) return false;
                break;
            case BsdfLobe.Glossy:
                if (++path.Glossy > _depths.Glossy) return false;
                break;
            default:
                if (++path.Transmission > _depths.Transmission) return false;
                break;
        }
        path.Bounces++;
        return true;
    }

    /// <summary>
    /// Russian roulette from the second bounce on (Veach 1997; PBRT-v4): the
    /// path survives with probability q = max channel of β, with the η²
    /// radiance factor of the refractions undone (light inside glass is not
    /// dimmer for being there), and β is divided by q. Unbiased; no floor,
    /// no cap, no reading of the scene: a path keeps going exactly as long as
    /// what it can still carry is worth the cost.
    /// </summary>
    private static bool SurviveRoulette(ref PathState path)
    {
        if (path.Bounces < 2) return true;
        Vector3 rr = path.Beta * path.EtaScale;
        float q = MathF.Max(rr.X, MathF.Max(rr.Y, rr.Z));
        if (q >= 1f) return true;
        if (!(q > 0f) || MathUtils.RandomFloat() >= q) return false;
        path.Beta /= q;
        return true;
    }

    private static bool HasAbsorption(Vector3 sigma) => sigma.X > 0f || sigma.Y > 0f || sigma.Z > 0f;

    /// <summary>exp(−σ_a t) per channel; 1 in vacuum.</summary>
    private static Vector3 BeerLambert(Vector3 sigma, float t)
    {
        if (!HasAbsorption(sigma) || !(t > 0f)) return Vector3.One;
        return new Vector3(
            sigma.X > 0f ? MathF.Exp(-sigma.X * t) : 1f,
            sigma.Y > 0f ? MathF.Exp(-sigma.Y * t) : 1f,
            sigma.Z > 0f ? MathF.Exp(-sigma.Z * t) : 1f);
    }
}
