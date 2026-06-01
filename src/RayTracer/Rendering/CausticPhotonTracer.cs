using System.Numerics;
using RayTracer.Core;
using RayTracer.Core.Sampling;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;

namespace RayTracer.Rendering;

/// <summary>
/// Emits caustic photons from the scene lights and builds the
/// <see cref="PhotonMap"/> the camera pass gathers from. A <b>caustic photon</b>
/// is a power packet that left a light, crossed <b>one or more specular (delta)
/// interfaces</b> — a mirror, smooth metal, glass or water surface — and then
/// landed on the first non-specular (diffuse/glossy) surface it met
/// (the path <c>L S+ D</c>). Photons that reach a diffuse surface with no
/// specular bounce in between are dropped: that transport is ordinary direct /
/// indirect lighting the path tracer already resolves by NEE and BSDF sampling.
///
/// <para>The walk reuses each material's own <see cref="IMaterial.Sample"/> /
/// <see cref="IMaterial.Scatter"/> exactly as the camera path does, so a
/// photon's throughput update matches the radiance path's attenuation
/// (delta lobe → <c>F</c>; otherwise <c>F·|cosθ|/pdf</c>). Emission is sampled
/// per light type below; every finite and directional light participates, which
/// is what lets photon caustics work from area, sphere, point, spot <b>and</b>
/// sun/directional sources — geometry the old manifold walk could not focus.</para>
///
/// <para>Glossy/rough refractive caustics (frosted glass) and HDRI-environment
/// caustics are out of scope for this estimator: a rough interface is non-delta,
/// so the photon deposits on it rather than passing through, and those caustics
/// fall back to the (noisier) unidirectional path. Interior Beer–Lambert
/// absorption is not applied along photon segments — a known small tint
/// difference on deeply coloured glass.</para>
/// </summary>
public static class CausticPhotonTracer
{
    private const int   MaxPhotonBounces = 24;   // specular-chain depth cap (RR trims well before this)
    private const int   RrStartBounce    = 4;    // begin Russian roulette after this many specular bounces
    private const float MinPhotonPower    = 1e-6f;

    private readonly struct Source
    {
        public readonly ILight Light;
        public readonly float  LumPower;   // Rec.709 luminance of the total emitted flux
        public Source(ILight light, float lumPower) { Light = light; LumPower = lumPower; }
    }

    /// <summary>
    /// Traces <paramref name="photonBudget"/> caustic photons and returns the
    /// resulting <see cref="PhotonMap"/>, or <c>null</c> when the scene has no
    /// light able to seed a caustic (no emitter, or no specular material exists
    /// to focus through — that gate is the caller's). <paramref name="cellSize"/>
    /// seeds the grid resolution and the gather-radius scale.
    /// </summary>
    public static PhotonMap? Build(IHittable world, IReadOnlyList<ILight> lights,
                                   AABB sceneBounds, int photonBudget, float cellSize)
    {
        if (photonBudget <= 0) return null;

        // Light power budget: each emittable light gets photons in proportion to
        // its luminous flux, so a dim fill never starves the key light's caustic.
        var sources = new List<Source>();
        float totalLum = 0f;
        foreach (var light in lights)
        {
            if (!CanEmit(light)) continue;
            float lum = MathF.Max(0f, light.ApproximatePower(sceneBounds));
            if (lum <= 0f) continue;
            sources.Add(new Source(light, lum));
            totalLum += lum;
        }
        if (sources.Count == 0 || totalLum <= 0f) return null;

        // Per-source photon counts (each ≥ 1 so a faint caustic light still fires).
        var counts = new int[sources.Count];
        int assigned = 0;
        for (int i = 0; i < sources.Count; i++)
        {
            int n = (int)MathF.Round(photonBudget * (sources[i].LumPower / totalLum));
            counts[i] = Math.Max(1, n);
            assigned += counts[i];
        }

        // Flatten (source, localIndex) into one index space for balanced parallelism.
        var offsets = new int[sources.Count + 1];
        for (int i = 0; i < sources.Count; i++) offsets[i + 1] = offsets[i] + counts[i];
        int total = offsets[sources.Count];

        var photons = new List<Photon>(total / 2);
        var sync = new object();

        Parallel.For(0, total,
            () => new List<Photon>(256),
            (g, _, local) =>
            {
                int si = UpperBound(offsets, g) - 1;
                int localIdx = g - offsets[si];
                Source src = sources[si];
                Vector3 perPhotonScale = Vector3.One / counts[si];

                // Deterministic per-photon sub-sequence (Owen-scrambled Sobol /
                // PRNG, same engine as the camera pass) so the map is identical
                // across runs and thread schedules.
                uint seed = (uint)(si * 0x9E3779B1) ^ 0xC2B2AE35u;
                Sampler.BeginPixelSample(seed, (uint)localIdx);

                if (TryEmit(src.Light, sceneBounds, out Ray ray, out Vector3 flux))
                    Trace(world, ray, flux * perPhotonScale, local);

                Sampler.EndPixelSample();
                return local;
            },
            local => { lock (sync) photons.AddRange(local); });

        if (photons.Count == 0) return null;
        return new PhotonMap(photons.ToArray(), cellSize);
    }

    // ── Photon random walk ───────────────────────────────────────────────────
    private static void Trace(IHittable world, Ray ray, Vector3 power, List<Photon> outPhotons)
    {
        int specularBounces = 0;
        var rec = new HitRecord();

        for (int bounce = 0; bounce < MaxPhotonBounces; bounce++)
        {
            rec = new HitRecord();
            if (!world.Hit(ray, MathUtils.Epsilon, MathUtils.Infinity, ref rec)) return;

            IMaterial? material = rec.Material;
            if (material == null) return;

            Vector3 view = Vector3.Normalize(-ray.Direction);

            // Prefer the Sample() BSDF API; fall back to legacy Scatter() exactly
            // as ShadeSurface does, so deltaness and throughput agree.
            BsdfSample? mis = material.Sample(view, rec);
            if (mis.HasValue)
            {
                BsdfSample s = mis.Value;
                if (s.IsDelta)
                {
                    power *= s.F;                       // delta lobe: F is the full attenuation
                    ray = ContinueRay(rec, s.Wo);
                    specularBounces++;
                }
                else
                {
                    Deposit(outPhotons, rec, ray, power, specularBounces);
                    return;                             // stop at the first diffuse/glossy surface
                }
            }
            else if (material.Scatter(ray, rec, out Vector3 atten, out Ray scattered))
            {
                if (material.IsDeltaScatter)
                {
                    power *= atten;                     // mirror / glass: continue the chain
                    ray = scattered;
                    specularBounces++;
                }
                else
                {
                    Deposit(outPhotons, rec, ray, power, specularBounces);
                    return;
                }
            }
            else
            {
                return;                                 // fully absorbed
            }

            // Russian roulette on the surviving power (after the early specular
            // bounces, where caustics are usually still bright).
            if (specularBounces >= RrStartBounce)
            {
                float survive = MathF.Min(1f, MathF.Max(power.X, MathF.Max(power.Y, power.Z)));
                if (survive <= MinPhotonPower || MathUtils.RandomFloat() >= survive) return;
                power /= survive;
            }
        }
    }

    private static void Deposit(List<Photon> outPhotons, in HitRecord rec, in Ray ray,
                                Vector3 power, int specularBounces)
    {
        // Only L S+ D paths are caustics; a diffuse hit with no specular bounce
        // is direct/indirect light the path tracer already owns.
        if (specularBounces < 1) return;
        if (power.X <= 0f && power.Y <= 0f && power.Z <= 0f) return;
        // IncidentDir points from the surface back toward the previous vertex —
        // the light-incident direction the receiver BRDF is evaluated against.
        Vector3 incident = Vector3.Normalize(-ray.Direction);
        outPhotons.Add(new Photon(rec.Point, incident, power));
    }

    private static Ray ContinueRay(in HitRecord rec, Vector3 wo)
    {
        Vector3 offsetDir = Vector3.Dot(wo, rec.Normal) >= 0f ? rec.Normal : -rec.Normal;
        return new Ray(MathUtils.OffsetOrigin(rec.Point, offsetDir), wo);
    }

    // ── Emission by light type ───────────────────────────────────────────────
    private static bool CanEmit(ILight light) => light switch
    {
        AreaLight or GeometryLight or SphereLight or PointLight or SpotLight => true,
        DirectionalLight dl => true,
        _ => false, // EnvironmentLight (HDRI) photon emission is out of scope for v1
    };

    /// <summary>
    /// Samples one photon from a light: an outgoing ray and the per-channel flux
    /// it carries for the WHOLE light (the caller divides by the photon count).
    /// Cosine-weighted area emitters fold the cosθ and the π into the sampling
    /// so the returned flux is simply Φ_total = ∫∫ L_e cosθ dω dA.
    /// </summary>
    private static bool TryEmit(ILight light, in AABB sceneBounds, out Ray ray, out Vector3 flux)
    {
        ray = default; flux = default;
        switch (light)
        {
            case AreaLight a:
            {
                Vector3 p = a.Corner + MathUtils.RandomFloat() * a.U + MathUtils.RandomFloat() * a.V;
                Vector3 n = Vector3.Normalize(Vector3.Cross(a.U, a.V));
                n = FaceToward(n, RectCentre(a), sceneBounds);
                float area = Vector3.Cross(a.U, a.V).Length();
                ray = new Ray(MathUtils.OffsetOrigin(p, n), CosineDir(n));
                flux = a.Color * a.Intensity * area * MathF.PI;
                return true;
            }
            case GeometryLight g:
            {
                var (p, n, uv, area) = g.Geometry.Sample();
                Vector3 emit = g.Material.EmissionAt(uv.X, uv.Y, p);
                if (emit.X <= 0f && emit.Y <= 0f && emit.Z <= 0f) return false;
                n = FaceToward(n, p, sceneBounds);
                ray = new Ray(MathUtils.OffsetOrigin(p, n), CosineDir(n));
                flux = emit * area * MathF.PI;
                return true;
            }
            case SphereLight s:
            {
                Vector3 dir = MathUtils.RandomUnitVector();
                Vector3 p = s.Center + dir * s.Radius;
                ray = new Ray(MathUtils.OffsetOrigin(p, dir), CosineDir(dir));
                // Φ_total = 4π·Color·Intensity (matches ApproximatePower); with
                // cosine emission over area 4πR² this is L_e·A·π.
                flux = s.Color * s.Intensity * (4f * MathF.PI);
                return true;
            }
            case PointLight pl:
            {
                Vector3 dir = MathUtils.RandomUnitVector();
                ray = new Ray(pl.Position, dir);
                flux = pl.Color * pl.Intensity * (4f * MathF.PI); // Φ = 4π·I
                return true;
            }
            case SpotLight sp:
            {
                // Uniform direction in the outer cone, weighted by the smoothstep²
                // falloff. Ω_outer = 2π(1−cosOuter); the unbiased flux estimate is
                // I·falloff·Ω_outer.
                float cosOuter = sp.CosOuterAngle;
                Vector3 dir = SampleCone(sp.Direction, cosOuter, out float cosTheta);
                float falloff = SpotFalloff(cosTheta, sp.CosInnerAngle, cosOuter);
                if (falloff <= 0f) return false;
                float omega = 2f * MathF.PI * (1f - cosOuter);
                ray = new Ray(sp.Position, dir);
                flux = sp.Color * sp.Intensity * (falloff * omega);
                return true;
            }
            case DirectionalLight dl:
            {
                // Parallel beam: emit from a disc on the scene's bounding sphere,
                // facing into the scene. Φ_total = E·πR² (E = irradiance).
                Vector3 c = 0.5f * (sceneBounds.Min + sceneBounds.Max);
                float r = MathF.Max(0.5f * (sceneBounds.Max - sceneBounds.Min).Length(), 1e-3f);
                Vector3 w = dl.Direction;                 // travel direction (light → scene)
                Onb(w, out Vector3 t, out Vector3 b);
                Vector2 disk = RandomDisk();
                Vector3 origin = c - w * (r * 1.5f) + (t * disk.X + b * disk.Y) * r;
                ray = new Ray(origin, w);
                flux = dl.Color * dl.Intensity * (MathF.PI * r * r);
                return true;
            }
            default:
                return false;
        }
    }

    // ── Sampling helpers ─────────────────────────────────────────────────────
    private static Vector3 RectCentre(AreaLight a) => a.Corner + 0.5f * a.U + 0.5f * a.V;

    /// <summary>Flips <paramref name="n"/> so it points from <paramref name="from"/> toward the scene centre — area/geometry emitters are one-sided panels facing the scene.</summary>
    private static Vector3 FaceToward(Vector3 n, Vector3 from, in AABB sceneBounds)
    {
        Vector3 c = 0.5f * (sceneBounds.Min + sceneBounds.Max);
        return Vector3.Dot(n, c - from) < 0f ? -n : n;
    }

    private static Vector3 CosineDir(Vector3 n)
    {
        // Lambertian (cosine-weighted) direction about n.
        Vector3 d = n + MathUtils.RandomUnitVector();
        if (d.LengthSquared() < 1e-8f) d = n;
        return Vector3.Normalize(d);
    }

    private static Vector3 SampleCone(Vector3 axis, float cosOuter, out float cosTheta)
    {
        float u1 = MathUtils.RandomFloat();
        float u2 = MathUtils.RandomFloat();
        cosTheta = 1f - u1 * (1f - cosOuter);
        float sinTheta = MathF.Sqrt(MathF.Max(0f, 1f - cosTheta * cosTheta));
        float phi = 2f * MathF.PI * u2;
        Onb(axis, out Vector3 t, out Vector3 b);
        return Vector3.Normalize(
            t * (MathF.Cos(phi) * sinTheta) +
            b * (MathF.Sin(phi) * sinTheta) +
            axis * cosTheta);
    }

    private static float SpotFalloff(float cosTheta, float cosInner, float cosOuter)
    {
        if (cosTheta >= cosInner) return 1f;
        if (cosTheta <= cosOuter) return 0f;
        float t = (cosTheta - cosOuter) / MathF.Max(cosInner - cosOuter, 1e-6f);
        return t * t; // smoothstep² (matches SpotLight.IlluminateAndTest)
    }

    private static Vector2 RandomDisk()
    {
        // Uniform point in the unit disk (concentric not needed for photon spread).
        float r = MathF.Sqrt(MathUtils.RandomFloat());
        float a = 2f * MathF.PI * MathUtils.RandomFloat();
        return new Vector2(r * MathF.Cos(a), r * MathF.Sin(a));
    }

    private static void Onb(Vector3 w, out Vector3 t, out Vector3 b)
    {
        Vector3 a = MathF.Abs(w.X) > 0.9f ? Vector3.UnitY : Vector3.UnitX;
        t = Vector3.Normalize(Vector3.Cross(a, w));
        b = Vector3.Cross(w, t);
    }

    /// <summary>Largest index i with arr[i] &lt;= value, for the flattened photon index space.</summary>
    private static int UpperBound(int[] arr, int value)
    {
        int lo = 0, hi = arr.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (arr[mid] <= value) lo = mid + 1; else hi = mid;
        }
        return lo;
    }
}
