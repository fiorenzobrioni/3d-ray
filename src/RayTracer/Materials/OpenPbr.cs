using System.Numerics;
using RayTracer.Core;
using RayTracer.Textures;

namespace RayTracer.Materials;

/// <summary>
/// OpenPBR Surface v1.1 (Academy Software Foundation; PIANO-REVIEW.md,
/// Phase 5): the layered uber-material, reduced to a mixture of lobes as the
/// specification derives it,
///
/// <code>
///   f = α ( F f_fuzz + (1 − F E_fuzz) [ C f_coat + lerp(1, A (1 − E_coat), C) Δ_C f_base ] )
///   f_base = lerp( f^R + T' f^T + (1 − E_R) [D f_diffuse (+ thin subsurface)], f_metal, M )
/// </code>
///
/// with α the opacity, F, C, M, T the fuzz, coat, metalness and transmission
/// weights, A the coat's absorption along the two paths through it, Δ_C its
/// darkening by internal reflections, E_X the directional albedo of lobe X at
/// the view direction (the albedo-scaling layering of the specification), and:
///
/// <list type="bullet">
/// <item>f_metal: GGX with the F82 Fresnel (<c>base_color</c> at normal
/// incidence, <c>specular_color</c> near grazing) and the Kulla-Conty
/// multiple-scattering lobe.</item>
/// <item>f^R, f^T: the dielectric interface of <c>specular_ior</c> (modulated by
/// <c>specular_weight</c>), GGX reflection and Walter 2007 transmission, the
/// latter weighted by T' = T + (1 − T) S: subsurface light enters the object
/// through the same interface and the renderer's random walk carries it inside
/// (the loader binds the subsurface medium to the object). Thin film tints the
/// reflection (Belcour-Barla).</item>
/// <item>f_diffuse: EON, energy-preserving Oren-Nayar.</item>
/// <item>f_coat: GGX dielectric reflection of <c>coat_ior</c>; the coat
/// roughens the base lobes.</item>
/// <item>f_fuzz: the Zeltner 2022 LTC sheen.</item>
/// </list>
///
/// <para>Thin-walled: the translucent base is a sheet (straight or, when rough,
/// mirrored transmission) and the subsurface a diffuse reflection and
/// transmission pair. A ray inside a transmissive object meets only the
/// dielectric interface. An opaque material is two-sided.</para>
///
/// <para>Each layer is energy-conserving: with white colours the material
/// reflects or transmits all the light in every configuration the
/// specification lists for the white furnace test, up to the single-scattering
/// loss of the rough dielectric interface.</para>
/// </summary>
public sealed class OpenPbr : IMaterial
{
    public OpenPbrParameters Parameters { get; }

    public NormalMapTexture? NormalMap { get; set; }
    public BumpMapTexture? BumpMap { get; set; }
    public MaterialDisplacement? Displacement { get; set; }

    /// <summary>The object's interior is a participating medium the loader
    /// bound (subsurface or scattering transmission): Beer-Lambert along the
    /// interior segments is then the medium's, not the material's.</summary>
    public bool InteriorIsMedium { get; init; }

    private readonly bool _mayTransmit;
    private readonly bool _allDelta;

    public OpenPbr(OpenPbrParameters parameters)
    {
        Parameters = parameters;
        var p = parameters;
        _mayTransmit = !p.TransmissionWeight.IsConstant || p.TransmissionWeight.ConstantValue > 0f
                    || !p.SubsurfaceWeight.IsConstant || p.SubsurfaceWeight.ConstantValue > 0f;

        // Every lobe a delta (clear glass, a polished mirror): no light sample
        // or connection can reach it, and emission keeps its weight across the bounce.
        static bool Is(FloatTexture t, float v) => t.IsConstant && t.ConstantValue == v;
        static bool AtLeast(FloatTexture t, float v) => t.IsConstant && t.ConstantValue >= v;
        bool smoothBase = Is(p.SpecularRoughness, 0f) && (Is(p.CoatWeight, 0f) || Is(p.CoatRoughness, 0f));
        bool noDiffuse = AtLeast(p.BaseMetalness, 1f)
                      || (AtLeast(p.TransmissionWeight, 1f) && !p.ThinWalled)
                      || Is(p.BaseWeight, 0f) && Is(p.SubsurfaceWeight, 0f);
        _allDelta = smoothBase && noDiffuse && Is(p.FuzzWeight, 0f)
                 && (Is(p.CoatWeight, 0f) || Is(p.CoatRoughness, 0f))
                 && !(p.ThinWalled && !Is(p.SubsurfaceWeight, 0f));
    }

    public bool IsDeltaScatter => _allDelta;

    /// <summary>
    /// Guided where the surface is mostly diffuse, or its metal is rough; a
    /// polished metal, glass or a translucent sheet keep their BSDF.
    /// </summary>
    public bool Guidable(in HitRecord rec)
    {
        if (_allDelta || HasTransmission(in rec)) return false;
        var p = Parameters;
        float metal = Clamp01(p.BaseMetalness.Value(in rec));
        if (metal < 0.5f) return true;
        float r = Clamp01(p.SpecularRoughness.Value(in rec));
        return r * r >= IMaterial.GuidingMinAlpha;
    }

    // ── Per-hit closure ──────────────────────────────────────────────────────

    private struct Hit
    {
        public bool Inside, Thin;
        public Vector3 N, T, B, Nc, Tc, Bc, Nf;
        public float Opacity;
        public float Fuzz, FuzzRough; public Vector3 FuzzColor;
        public float Coat, CoatAx, CoatAy, CoatEta, CoatDarkening; public bool CoatDelta; public Vector3 CoatColor;
        public float Metal, SpecWeight; public Vector3 MetalF0, MetalTint;
        public float Rough, RoughB, Aniso, Ax, Ay, MsAvg; public bool BaseDelta;
        public float EtaS, Ri, F0Spec; public Vector3 SpecColor;
        public float Trans; public Vector3 TransTint, Sigma;
        public float DiffuseW, DiffRough; public Vector3 Rho;
        public float ThinSss, SubAniso; public Vector3 SubColor;
        public float Film, FilmNm, FilmIor;
        public Vector3 SpecAvg;   // Ē_R: cosine-weighted average of the reflection albedo
        public int Stamp;         // identifies this closure for the view-dependent cache
    }

    [ThreadStatic] private static OpenPbr? _cOwner;
    [ThreadStatic] private static float _cU, _cV, _cEta;
    [ThreadStatic] private static int _cSeed;
    [ThreadStatic] private static bool _cFront;
    [ThreadStatic] private static Vector3 _cP, _cN, _cTan;
    [ThreadStatic] private static Hit _cHit;

    private Hit GetHit(in HitRecord rec)
    {
        if (ReferenceEquals(_cOwner, this) && _cU == rec.U && _cV == rec.V && _cSeed == rec.ObjectSeed
            && _cP == rec.LocalPoint && _cN == rec.Normal && _cFront == rec.FrontFace
            && _cEta == rec.RelativeEta && _cTan == rec.Tangent)
            return _cHit;
        Hit h = BuildHit(in rec);
        h.Stamp = ++_hitStamp;
        _cOwner = this; _cU = rec.U; _cV = rec.V; _cSeed = rec.ObjectSeed; _cP = rec.LocalPoint;
        _cN = rec.Normal; _cFront = rec.FrontFace; _cEta = rec.RelativeEta; _cTan = rec.Tangent;
        _cHit = h;
        return h;
    }

    private static float Clamp01(float x) => Math.Clamp(x, 0f, 1f);
    private static Vector3 Clamp01(Vector3 v) => Vector3.Clamp(v, Vector3.Zero, Vector3.One);
    private static float Lum(Vector3 c) => 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>Below this roughness (α ≤ 1e-3, a lobe narrower than 0.06°) a
    /// specular lobe is a perfect mirror: sampled as a delta, so no
    /// near-singular lobe meets a shadow ray or a connection.</summary>
    private const float DeltaRoughness = 0.0317f;

    /// <summary>OpenPBR anisotropy: α_t = r² √(2 / (1 + (1 − a)²)), α_b = (1 − a) α_t.</summary>
    private static (float ax, float ay) Alphas(float r, float aniso)
    {
        float a = r * r, k = 1f - aniso;
        float at = a * MathF.Sqrt(2f / (1f + k * k));
        return (MathF.Max(at, 1e-4f), MathF.Max(k * at, 1e-4f));
    }

    /// <summary>The IOR whose normal-incidence reflectance is <paramref name="weight"/>
    /// times that of <paramref name="ior"/> (OpenPBR, specular_weight).</summary>
    private static float ModulatedIor(float ior, float weight)
    {
        float f0 = (ior - 1f) / (ior + 1f);
        f0 *= f0;
        if (f0 < 1e-12f) return 1f;
        float xi = Math.Clamp(weight, 0f, 1f / f0);
        float eps = Math.Clamp(MathF.Sign(ior - 1f) * MathF.Sqrt(xi * f0), -0.9999f, 0.9999f);
        return (1f + eps) / (1f - eps);
    }

    private Hit BuildHit(in HitRecord rec)
    {
        var p = Parameters;
        var h = new Hit { Thin = p.ThinWalled };
        h.Inside = !h.Thin && _mayTransmit && !rec.FrontFace;

        // Shading frame: the geometry's tangent when it has one (anisotropy
        // direction), re-orthogonalised against the (normal-mapped) normal.
        h.N = rec.Normal;
        Vector3 t = rec.Tangent - Vector3.Dot(rec.Tangent, h.N) * h.N;
        if (t.LengthSquared() > 1e-10f)
        {
            h.T = Vector3.Normalize(t);
            h.B = Vector3.Cross(h.N, h.T);
        }
        else
        {
            Microfacet.BuildTangentFrame(h.N, out h.T, out h.B);
        }
        if (!p.SpecularRotation.IsConstant || p.SpecularRotation.ConstantValue != 0f)
        {
            float angle = 2f * MathF.PI * p.SpecularRotation.Value(in rec);
            float c = MathF.Cos(angle), s = MathF.Sin(angle);
            (h.T, h.B) = (c * h.T + s * h.B, -s * h.T + c * h.B);
        }

        float ior = MathF.Max(p.SpecularIor.Value(in rec), 1e-3f);
        float nb = ModulatedIor(ior, MathF.Max(0f, p.SpecularWeight.Value(in rec)));
        float rel = rec.RelativeEta;
        float r = Clamp01(p.SpecularRoughness.Value(in rec));

        if (h.Inside)
        {
            // Light inside the object meets the bare dielectric interface.
            h.Ri = rel > 0f ? rel : nb;
            h.EtaS = 1f / h.Ri;
            h.SpecColor = Vector3.One;
            h.Trans = 1f;
            h.TransTint = Vector3.One;
            h.Opacity = 1f;
            h.Rough = r;
            h.RoughB = r;
            h.Aniso = Clamp01(p.SpecularRoughnessAnisotropy.Value(in rec));
            (h.Ax, h.Ay) = Alphas(r, h.Aniso);
            h.BaseDelta = r <= DeltaRoughness || MathF.Abs(h.Ri - 1f) < 1e-4f;
            return h;
        }

        h.Opacity = Clamp01(p.GeometryOpacity.Value(in rec));

        // Fuzz and coat.
        h.Fuzz = Clamp01(p.FuzzWeight.Value(in rec));
        if (h.Fuzz > 0f)
        {
            h.FuzzColor = Clamp01(p.FuzzColor.Value(in rec));
            h.FuzzRough = Clamp01(p.FuzzRoughness.Value(in rec));
        }
        h.Coat = Clamp01(p.CoatWeight.Value(in rec));
        h.Nc = h.N; h.Tc = h.T; h.Bc = h.B;
        float rc = 0f;
        if (h.Coat > 0f)
        {
            h.CoatColor = Clamp01(p.CoatColor.Value(in rec));
            rc = Clamp01(p.CoatRoughness.Value(in rec));
            h.CoatDelta = rc <= DeltaRoughness;
            (h.CoatAx, h.CoatAy) = Alphas(rc, Clamp01(p.CoatRoughnessAnisotropy.Value(in rec)));
            float ci = MathF.Max(p.CoatIor.Value(in rec), 1e-3f);
            h.CoatEta = MathF.Max(ci >= 1f ? ci : 1f / ci, 1.0001f);
            h.CoatDarkening = Clamp01(p.CoatDarkening.Value(in rec));
            if (p.CoatNormal != null) CoatFrame(in rec, p.CoatNormal, ref h);
        }
        h.Nf = h.Coat > 0f ? Vector3.Normalize(Vector3.Lerp(h.N, h.Nc, h.Coat)) : h.N;

        // Base specular: roughened by the coat (variances add, the coat
        // crossed twice), shared by metal and dielectric.
        h.Rough = r;
        float rb = r;
        if (h.Coat > 0f && rc > 0f)
            rb = Lerp(r, MathF.Min(1f, MathF.Pow(r * r * r * r + 2f * rc * rc * rc * rc, 0.25f)), h.Coat);
        h.BaseDelta = rb <= DeltaRoughness;
        h.RoughB = rb;
        h.Aniso = Clamp01(p.SpecularRoughnessAnisotropy.Value(in rec));
        (h.Ax, h.Ay) = Alphas(rb, h.Aniso);

        h.Metal = Clamp01(p.BaseMetalness.Value(in rec));
        float baseWeight = MathF.Max(0f, p.BaseWeight.Value(in rec));
        Vector3 baseColor = Clamp01(p.BaseColor.Value(in rec));
        Vector3 specColor = Clamp01(p.SpecularColor.Value(in rec));
        h.SpecWeight = MathF.Max(0f, p.SpecularWeight.Value(in rec));
        if (h.Metal > 0f)
        {
            h.MetalF0 = Clamp01(baseWeight * baseColor);
            h.MetalTint = specColor;
            h.MsAvg = h.Aniso > 0f ? AnisotropicAlbedo.Avg(h.RoughB, h.Aniso) : MicrofacetAlbedo.Avg(h.Ax);
        }

        // Dielectric interface. Refraction follows the IOR stack; the
        // reflection Fresnel sees the coat, with the TIR fix of OpenPBR
        // (n_c > n_b: invert the ratio).
        // specular_weight modulates the ratio after the coat has set it, so
        // weight 0 removes the base reflection whatever lies above.
        h.Ri = rel > 0f ? rel : 1f / nb;
        if (h.Coat > 0f)
        {
            float outside = rel > 0f ? rel * nb : 1f;
            float etaBc = ior / h.CoatEta;
            if (etaBc < 1f) etaBc = 1f / etaBc;
            h.EtaS = ModulatedIor(Lerp(ior / outside, etaBc, h.Coat), MathF.Max(0f, p.SpecularWeight.Value(in rec)));
        }
        else
        {
            h.EtaS = 1f / h.Ri;
        }
        // An index-matched interface does not bend or reflect: whatever its
        // roughness, light crosses it straight.
        if (MathF.Abs(h.Ri - 1f) < 1e-4f && h.Coat <= 0f) h.BaseDelta = true;
        float f0 = (ior - 1f) / (ior + 1f);
        h.F0Spec = f0 * f0;
        h.SpecColor = specColor;

        h.Film = Clamp01(p.ThinFilmWeight.Value(in rec));
        if (h.Film > 0f)
        {
            h.FilmNm = MathF.Max(0f, p.ThinFilmThickness.Value(in rec)) * 1000f;
            h.FilmIor = MathF.Max(1f, p.ThinFilmIor.Value(in rec));
        }

        float tw = Clamp01(p.TransmissionWeight.Value(in rec));
        float sw = Clamp01(p.SubsurfaceWeight.Value(in rec));
        Vector3 transColor = tw > 0f ? Clamp01(p.TransmissionColor.Value(in rec)) : Vector3.One;
        float depth = tw > 0f ? MathF.Max(0f, p.TransmissionDepth.Value(in rec)) : 0f;
        if (h.Thin)
        {
            h.Trans = tw;
            h.TransTint = transColor;
            h.DiffuseW = (1f - tw) * (1f - sw);
            h.ThinSss = (1f - tw) * sw;
            if (h.ThinSss > 0f)
            {
                h.SubColor = Clamp01(p.SubsurfaceColor.Value(in rec));
                h.SubAniso = Math.Clamp(p.SubsurfaceScatterAnisotropy.Value(in rec), -1f, 1f);
            }
        }
        else
        {
            h.Trans = tw + (1f - tw) * sw;
            h.DiffuseW = (1f - tw) * (1f - sw);
            // Without depth the colour is the tint of a refraction into the
            // object (applied on the way in, once); with depth it is the
            // transmittance at that depth, Beer-Lambert inside. Subsurface
            // light is untinted here: its medium owns the colour.
            Vector3 tint = depth > 0f ? Vector3.One : transColor;
            h.TransTint = h.Trans > 0f ? (tw * tint + (1f - tw) * sw * Vector3.One) / h.Trans : Vector3.One;
            if (depth > 0f && !InteriorIsMedium)
            {
                Vector3 c = Vector3.Max(transColor, new Vector3(1e-4f));
                h.Sigma = new Vector3(-MathF.Log(c.X), -MathF.Log(c.Y), -MathF.Log(c.Z)) / depth;
            }
        }
        h.Rho = Clamp01(baseWeight * baseColor);
        h.DiffRough = Clamp01(p.BaseDiffuseRoughness.Value(in rec));
        h.SpecAvg = SpecularAverage(in h);
        return h;
    }

    private static void CoatFrame(in HitRecord rec, NormalMapTexture map, ref Hit h)
    {
        Vector3 ts = map.SampleNormal(rec.U, rec.V);
        Vector3 T = h.T, B = h.B;
        if (!rec.FrontFace) { T = -T; B = -B; }
        Vector3 n = T * ts.X + B * ts.Y + h.N * ts.Z;
        if (n.LengthSquared() < 1e-10f) return;
        h.Nc = Vector3.Normalize(n);
        Vector3 t = h.T - Vector3.Dot(h.T, h.Nc) * h.Nc;
        if (t.LengthSquared() > 1e-10f)
        {
            h.Tc = Vector3.Normalize(t);
            h.Bc = Vector3.Cross(h.Nc, h.Tc);
        }
        else
        {
            Microfacet.BuildTangentFrame(h.Nc, out h.Tc, out h.Bc);
        }
    }

    // ── Fresnel factors ──────────────────────────────────────────────────────

    /// <summary>Untinted reflectance of the dielectric interface at the
    /// microfacet cosine, with the thin film where present.</summary>
    private static Vector3 DielectricFresnel(in Hit h, float cos)
    {
        float f = MathUtils.FresnelDielectric(cos, 1f / h.EtaS);
        if (h.Film <= 0f) return new Vector3(f);
        float e = h.EtaS >= 1f ? h.EtaS : 1f / h.EtaS;
        float f0 = (e - 1f) / (e + 1f);
        Vector3 film = ThinFilm.Evaluate(cos, h.FilmIor, h.FilmNm, new Vector3(f0 * f0));
        return Vector3.Lerp(new Vector3(f), film, h.Film);
    }

    private static Vector3 MetalFresnel(in Hit h, float cos)
    {
        Vector3 f = ConductorFresnel.F82(cos, h.MetalF0, h.MetalTint);
        if (h.Film > 0f)
            f = Vector3.Lerp(f, ThinFilm.Evaluate(cos, h.FilmIor, h.FilmNm, h.MetalF0), h.Film);
        return Clamp01(f * h.SpecWeight);
    }

    // ── View-dependent layer weights ─────────────────────────────────────────

    private struct Layer
    {
        public float MuV, MuVc, FuzzE, GFuzz, CoatE, EMetalV;
        public Vector3 Dark, ER;
    }

    /// <summary>Directional albedo of the dielectric reflection lobe (with its
    /// tint and film) at the view cosine.</summary>
    /// <summary>The roughness an anisotropic lobe shows along a direction: the
    /// α of the slope distribution projected on its azimuth, which sets the
    /// Smith masking. The isotropic albedo tables are read with it.</summary>
    private static float ProjectedAlpha(in Hit h, Vector3 dir)
    {
        if (h.Ax == h.Ay) return h.Ax;
        float x = Vector3.Dot(dir, h.T), y = Vector3.Dot(dir, h.B), r2 = x * x + y * y;
        if (r2 < 1e-12f) return MathF.Sqrt(h.Ax * h.Ay);
        return MathF.Sqrt((h.Ax * h.Ax * x * x + h.Ay * h.Ay * y * y) / r2);
    }


    /// <summary>White single-scattering albedo of the base's GGX lobe at a
    /// direction: the anisotropic table when the lobe is anisotropic, read
    /// at both directions so that the Kulla-Conty lobe stays reciprocal.</summary>
    private static float WhiteAlbedoAt(in Hit h, Vector3 dir)
    {
        Vector3 l = Local(dir, h.T, h.B, h.N);
        l.Z = MathF.Abs(l.Z);
        return h.Aniso > 0f ? AnisotropicAlbedo.E(l, h.RoughB, h.Aniso) : MicrofacetAlbedo.E(l.Z, h.Ax);
    }

    /// <summary>Albedo of the dielectric reflection lobe (tint, film) at a
    /// direction. Anisotropic: the isotropic Fresnel table at the projected
    /// roughness, times the anisotropic-to-isotropic ratio of the white
    /// tables, so that both ends of the reciprocal diffuse weight agree.</summary>
    private static Vector3 SpecularAlbedoAt(in Hit h, Vector3 dir)
    {
        float mu = MathF.Min(MathF.Abs(Vector3.Dot(h.N, dir)), 1f);
        if (h.BaseDelta || h.Aniso <= 0f) return ReflectionAlbedo(in h, mu, h.Ax);
        float ap = ProjectedAlpha(in h, dir);
        Vector3 e = ReflectionAlbedo(in h, mu, ap);
        float iso = MicrofacetAlbedo.E(mu, ap);
        return iso > 1e-6f ? Clamp01(e * (WhiteAlbedoAt(in h, dir) / iso)) : e;
    }

    private static Vector3 ReflectionAlbedo(in Hit h, float mu, float alpha)
    {
        float e = h.EtaS >= 1f ? h.EtaS : 1f / h.EtaS;
        if (h.BaseDelta) return h.SpecColor * DielectricFresnel(in h, mu);
        float table = DielectricAlbedo.E(mu, alpha, e);
        if (h.Film > 0f)
        {
            float plain = MathUtils.FresnelDielectric(mu, 1f / h.EtaS);
            if (plain > 1e-6f) return Clamp01(h.SpecColor * table * DielectricFresnel(in h, mu) / plain);
        }
        return h.SpecColor * table;
    }

    [ThreadStatic] private static int _hitStamp;
    [ThreadStatic] private static int _wStamp;
    [ThreadStatic] private static Vector3 _wV;
    [ThreadStatic] private static Layer _wLayer;

    private Layer Weights(in Hit h, Vector3 V)
    {
        if (_wStamp == h.Stamp && _wV == V) return _wLayer;
        Layer w = ComputeWeights(in h, V);
        _wStamp = h.Stamp; _wV = V; _wLayer = w;
        return w;
    }

    private Layer ComputeWeights(in Hit h, Vector3 V)
    {
        var w = new Layer { MuV = Vector3.Dot(h.N, V), GFuzz = 1f, Dark = Vector3.One };
        if (h.Inside) return w;
        if (h.Fuzz > 0f)
        {
            w.FuzzE = FuzzLtc.Albedo(MathF.Max(0f, Vector3.Dot(h.Nf, V)), h.FuzzRough);
            w.GFuzz = 1f - h.Fuzz * w.FuzzE;
        }
        float muV = MathF.Max(w.MuV, 0f);
        if (h.Metal < 1f) w.ER = SpecularAlbedoAt(in h, V);
        if (h.Metal > 0f && !h.BaseDelta) w.EMetalV = WhiteAlbedoAt(in h, V);
        if (h.Coat > 0f)
        {
            w.MuVc = MathF.Max(0f, Vector3.Dot(h.Nc, V));
            w.CoatE = h.CoatDelta
                ? MathUtils.FresnelDielectric(w.MuVc, 1f / h.CoatEta)
                : DielectricAlbedo.E(w.MuVc, MathF.Sqrt(h.CoatAx * h.CoatAy), h.CoatEta);

            // Darkening by the light the coat reflects back down (OpenPBR
            // §Coat): Δ = (1 − K) / (1 − E_b K), K between the smooth-base
            // and the rough-base internal reflection.
            if (h.CoatDarkening > 0f)
            {
                float kr = 1f - (1f - DielectricAlbedo.FresnelAvg(h.CoatEta)) / (h.CoatEta * h.CoatEta);
                float ks = MathUtils.FresnelDielectric(w.MuVc, 1f / h.CoatEta);
                float rd = Lerp(1f, h.Rough, Clamp01(h.SpecWeight * h.F0Spec));
                float k = Lerp(ks, kr, Lerp(rd, h.Rough, h.Metal));
                Vector3 er1 = ReflectionAlbedo(in h, 1f, MathF.Sqrt(h.Ax * h.Ay));
                Vector3 diel = er1 + (Vector3.One - er1) * (h.DiffuseW * h.Rho + h.ThinSss * h.SubColor);
                Vector3 eb = Vector3.Lerp(diel, h.MetalF0, h.Metal);
                Vector3 delta = new Vector3(1f - k) / (Vector3.One - eb * k);
                w.Dark = Vector3.Lerp(Vector3.One, delta, h.Coat * h.CoatDarkening);
            }
        }
        return w;
    }

    /// <summary>The coat's transmittance along the paths through it,
    /// coat_color^((1/μ_t,V + 1/μ_t,L) / 2): coat_color at normal incidence,
    /// darker and more saturated at grazing.</summary>
    private static Vector3 CoatAbsorption(in Hit h, float muV, float muL, bool transmitted)
    {
        if (h.CoatColor == Vector3.One) return Vector3.One;
        float inv2 = 1f / (h.CoatEta * h.CoatEta);
        float tv = MathF.Sqrt(MathF.Max(1e-4f, 1f - (1f - muV * muV) * inv2));
        float exponent = 0.5f / tv;
        if (!transmitted)
            exponent += 0.5f / MathF.Sqrt(MathF.Max(1e-4f, 1f - (1f - muL * muL) * inv2));
        return new Vector3(MathF.Pow(h.CoatColor.X, exponent), MathF.Pow(h.CoatColor.Y, exponent),
                           MathF.Pow(h.CoatColor.Z, exponent));
    }

    /// <summary>What reaches the base through the fuzz and the coat.</summary>
    private static Vector3 BaseMultiplier(in Hit h, in Layer w, float muL, bool transmitted)
    {
        if (h.Inside) return Vector3.One;
        Vector3 m = new(w.GFuzz);
        if (h.Coat > 0f)
        {
            Vector3 a = CoatAbsorption(in h, MathF.Max(w.MuV, 0f), MathF.Abs(muL), transmitted);
            m *= Vector3.Lerp(Vector3.One, a * (1f - w.CoatE), h.Coat) * w.Dark;
        }
        return m;
    }

    /// <summary>
    /// What reaches the diffuse base through the specular interface, in the
    /// reciprocal form (1 − E_R(μ_V)) (1 − E_R(μ_L)) / (1 − Ē_R): exactly the
    /// light the interface lets through for a Lambertian base, and symmetric in
    /// the two directions, where OpenPBR's (1 − E_R(μ_V)) alone is not.
    /// </summary>
    private static Vector3 DiffuseUnderSpecular(in Hit h, in Layer w, Vector3 L, float muL)
    {
        if (h.Inside) return Vector3.One;
        Vector3 avg = h.SpecAvg;
        Vector3 eL = SpecularAlbedoAt(in h, L);
        return (Vector3.One - w.ER) * (Vector3.One - eL) / Vector3.Max(Vector3.One - avg, new Vector3(1e-4f));
    }

    private static readonly float[] GaussX = { 0.0198551f, 0.1016668f, 0.2372338f, 0.4082827f, 0.5917173f, 0.7627662f, 0.8983332f, 0.9801449f };
    private static readonly float[] GaussW = { 0.0506143f, 0.1111905f, 0.1568533f, 0.1813419f, 0.1813419f, 0.1568533f, 0.1111905f, 0.0506143f };

    /// <summary>Ē_R, the cosine-weighted average of <see cref="SpecularAlbedoAt"/>
    /// over the hemisphere: from the tables, or, when a thin film colours it
    /// or the lobe is anisotropic, by Gauss-Legendre in μ (8 points) and the
    /// midpoint rule in the azimuth (4 points over a quarter turn).</summary>
    private static Vector3 SpecularAverage(in Hit h)
    {
        float e = h.EtaS >= 1f ? h.EtaS : 1f / h.EtaS;
        bool aniso = h.Aniso > 0f && !h.BaseDelta;
        if (h.Film <= 0f && !aniso)
            return h.SpecColor * (h.BaseDelta ? DielectricAlbedo.FresnelAvg(e) : DielectricAlbedo.Avg(h.Ax, e));
        int nPhi = aniso ? 4 : 1;
        Vector3 sum = Vector3.Zero;
        for (int k = 0; k < nPhi; k++)
        {
            float phi = (k + 0.5f) * (0.5f * MathF.PI / nPhi);
            for (int i = 0; i < GaussX.Length; i++)
            {
                float mu = GaussX[i], s = MathF.Sqrt(1f - mu * mu);
                Vector3 dir = s * MathF.Cos(phi) * h.T + s * MathF.Sin(phi) * h.B + mu * h.N;
                sum += 2f * GaussW[i] * mu * SpecularAlbedoAt(in h, dir);
            }
        }
        return sum / nPhi;
    }

    // ── Lobe probabilities ──────────────────────────────────────────────────

    private struct Probs
    {
        public float Fuzz, Coat, Metal, Iface, Diff, DiffT;
        public float Sum => Fuzz + Coat + Metal + Iface + Diff + DiffT;
    }

    private static Probs Probabilities(in Hit h, in Layer w)
    {
        var p = new Probs();
        if (h.Inside) { p.Iface = 1f; return p; }
        float muV = MathF.Max(w.MuV, 1e-4f);
        p.Fuzz = h.Fuzz * Lum(h.FuzzColor) * w.FuzzE;
        p.Coat = w.GFuzz * h.Coat * w.CoatE;
        float g = Lum(BaseMultiplier(in h, in w, muV, false));
        if (h.Metal > 0f) p.Metal = g * h.Metal * MathF.Max(Lum(MetalFresnel(in h, muV)), 0.02f);
        float d = 1f - h.Metal;
        if (d > 0f)
        {
            float er = Math.Clamp(Lum(w.ER), 0f, 1f);
            p.Iface = g * d * (er + h.Trans * (1f - er));
            float under = g * d * (1f - er);
            p.Diff = under * (h.DiffuseW * Lum(Eon.Albedo(h.Rho, h.DiffRough, muV))
                              + h.ThinSss * 0.5f * (1f - h.SubAniso) * Lum(h.SubColor));
            if (h.Thin) p.DiffT = under * h.ThinSss * 0.5f * (1f + h.SubAniso) * Lum(h.SubColor);
        }
        return p;
    }

    // ── Lobes ───────────────────────────────────────────────────────────────

    private static Vector3 Local(Vector3 v, Vector3 t, Vector3 b, Vector3 n) =>
        new(Vector3.Dot(v, t), Vector3.Dot(v, b), Vector3.Dot(v, n));

    /// <summary>GGX reflection in a frame: D G2 / (4 μV μL) and the visible-normal
    /// pdf G1(V) D / (4 μV); zero when L leaves the frame's hemisphere.</summary>
    private static (float value, float pdf, float vh) GgxReflection(Vector3 V, Vector3 L, Vector3 t, Vector3 b, Vector3 n,
                                                                  float ax, float ay)
    {
        float muV = Vector3.Dot(n, V), muL = Vector3.Dot(n, L);
        if (muV <= 0f || muL <= 0f) return (0f, 0f, 0f);
        Vector3 hr = V + L;
        if (hr.LengthSquared() < 1e-14f) return (0f, 0f, 0f);
        Vector3 hw = Vector3.Normalize(hr);
        Vector3 Vl = Local(V, t, b, n), Ll = Local(L, t, b, n), Hl = Local(hw, t, b, n);
        float D = Microfacet.DGgxAniso(Hl, ax, ay);
        float G = Microfacet.G2GgxAniso(Vl, Ll, ax, ay);
        float g1 = Microfacet.G1GgxAniso(Vl, ax, ay);
        return (D * G / (4f * muV * muL), g1 * D / (4f * muV), Vector3.Dot(V, hw));
    }

    private Vector3 MetalLobe(in Hit h, in Layer w, Vector3 V, Vector3 L, out float pdf)
    {
        pdf = 0f;
        var (val, p, vh) = GgxReflection(V, L, h.T, h.B, h.N, h.Ax, h.Ay);
        float muV = Vector3.Dot(h.N, V), muL = Vector3.Dot(h.N, L);
        if (muV <= 0f || muL <= 0f) return Vector3.Zero;
        Vector3 f = val > 0f ? MetalFresnel(in h, vh) * val : Vector3.Zero;

        // Kulla-Conty: the light the single-scattering lobe loses to
        // inter-reflection between microfacets, returned in the colour of
        // the Fresnel average.
        float eAvg = h.MsAvg;
        float eV = w.EMetalV;
        if (eAvg < 0.9999f)
        {
            Vector3 fAvg = Clamp01(ConductorFresnel.F82Average(h.MetalF0, h.MetalTint) * h.SpecWeight);
            Vector3 fMs = fAvg * fAvg * eAvg / (Vector3.One - fAvg * (1f - eAvg));
            f += fMs * ((1f - eV) * (1f - WhiteAlbedoAt(in h, L)) / (MathF.PI * (1f - eAvg)));
        }
        float q = Math.Clamp(eV, 0.05f, 1f);
        pdf = q * p + (1f - q) * muL / MathF.PI;
        return f;
    }

    /// <summary>Probability of the reflection branch at a microfacet, from
    /// the reflected and transmitted energy there.</summary>
    private static float ReflectProbability(in Hit h, Vector3 fresnel)
    {
        if (h.Trans <= 0f) return 1f;
        float fr = Lum(h.SpecColor * fresnel), ft = h.Trans * Lum(Vector3.One - fresnel);
        return fr + ft > 0f ? fr / (fr + ft) : 1f;
    }

    /// <summary>Half-vector of a transmission pair (Walter 2007), faced to N.</summary>
    private static Vector3 TransmissionHalf(Vector3 V, Vector3 L, Vector3 N, float ri)
    {
        Vector3 hr = V + L / ri;
        if (hr.LengthSquared() < 1e-14f) return Vector3.Zero;
        hr = Vector3.Normalize(hr);
        return Vector3.Dot(hr, N) < 0f ? -hr : hr;
    }

    /// <summary>The rough dielectric interface: GGX reflection and either
    /// Walter transmission or, thin-walled, the reflection lobe mirrored
    /// through the sheet. Returns the BSDF before the layer multiplier.</summary>
    private static Vector3 InterfaceLobe(in Hit h, Vector3 V, Vector3 L, out float pdf, out bool transmitted)
    {
        pdf = 0f;
        Vector3 N = h.N;
        float muV = Vector3.Dot(N, V), muL = Vector3.Dot(N, L);
        transmitted = muL < 0f;
        if (muV <= 0f || muL == 0f) return Vector3.Zero;

        if (!transmitted || h.Thin)
        {
            Vector3 Lr = transmitted ? L - 2f * muL * N : L;
            var (val, p, vh) = GgxReflection(V, Lr, h.T, h.B, N, h.Ax, h.Ay);
            if (val <= 0f) return Vector3.Zero;
            Vector3 fres = DielectricFresnel(in h, vh);
            float pr = ReflectProbability(in h, fres);
            if (!transmitted)
            {
                pdf = pr * p;
                return h.SpecColor * fres * val;
            }
            if (h.Trans <= 0f) return Vector3.Zero;
            pdf = (1f - pr) * p;
            return h.Trans * h.TransTint * (Vector3.One - fres) * val;
        }

        if (h.Trans <= 0f) return Vector3.Zero;
        float ri = h.Ri, eta = 1f / ri;
        Vector3 hw = TransmissionHalf(V, L, N, ri);
        if (hw == Vector3.Zero) return Vector3.Zero;
        float vhT = Vector3.Dot(V, hw), lh = Vector3.Dot(L, hw);
        if (vhT <= 0f || lh >= 0f) return Vector3.Zero;
        Vector3 Vl = Local(V, h.T, h.B, N), Ll = Local(L, h.T, h.B, N), Hl = Local(hw, h.T, h.B, N);
        Ll.Z = -Ll.Z;
        float D = Microfacet.DGgxAniso(Hl, h.Ax, h.Ay);
        float G = Microfacet.G2GgxAniso(Vl, Ll, h.Ax, h.Ay);
        float g1 = Microfacet.G1GgxAniso(Vl, h.Ax, h.Ay);
        Vector3 fresT = DielectricFresnel(in h, vhT);
        float prT = ReflectProbability(in h, fresT);
        float denom = lh + vhT / eta;
        float jac = MathF.Abs(lh) / (denom * denom);
        pdf = (1f - prT) * g1 * vhT * D / muV * jac;
        float ft = D * G * MathF.Abs(lh * vhT / (denom * denom * muL * muV)) * ri * ri;
        return h.Trans * h.TransTint * (Vector3.One - fresT) * ft;
    }

    private static Vector3 FuzzLobe(in Hit h, in Layer w, Vector3 V, Vector3 L, out float pdf)
    {
        pdf = 0f;
        float muL = Vector3.Dot(h.Nf, L);
        if (muL <= 0f) return Vector3.Zero;
        pdf = FuzzLtc.Density(h.Nf, V, L, h.FuzzRough);
        return h.Fuzz * h.FuzzColor * (w.FuzzE * pdf / muL);
    }

    // ── IMaterial: evaluation ───────────────────────────────────────────────

    /// <summary>Non-delta BSDF and its sampling pdf, with the lobe probabilities.</summary>
    private Vector3 EvalAll(in Hit h, in Layer w, in Probs pr, Vector3 V, Vector3 L, out float pdf)
    {
        pdf = 0f;
        float total = pr.Sum;
        if (total <= 0f) return Vector3.Zero;
        Vector3 f = Vector3.Zero;
        float muL = Vector3.Dot(h.N, L);

        if (h.Inside)
        {
            if (!h.BaseDelta)
            {
                f = InterfaceLobe(in h, V, L, out pdf, out _);
            }
            return f;
        }

        if (h.Fuzz > 0f && pr.Fuzz > 0f)
        {
            f += FuzzLobe(in h, in w, V, L, out float p);
            pdf += pr.Fuzz * p;
        }
        if (h.Coat > 0f && !h.CoatDelta)
        {
            var (val, p, vh) = GgxReflection(V, L, h.Tc, h.Bc, h.Nc, h.CoatAx, h.CoatAy);
            if (val > 0f)
            {
                f += new Vector3(w.GFuzz * h.Coat * MathUtils.FresnelDielectric(vh, 1f / h.CoatEta) * val);
                pdf += pr.Coat * p;
            }
        }

        bool transmitted = muL < 0f;
        Vector3 bm = BaseMultiplier(in h, in w, muL, transmitted);
        if (h.Metal > 0f && !h.BaseDelta && !transmitted)
        {
            f += bm * h.Metal * MetalLobe(in h, in w, V, L, out float p);
            pdf += pr.Metal * p;
        }
        float d = 1f - h.Metal;
        if (d > 0f)
        {
            if (!h.BaseDelta)
            {
                Vector3 fi = InterfaceLobe(in h, V, L, out float p, out _);
                f += bm * d * fi;
                pdf += pr.Iface * p;
            }
            float muV = MathF.Max(w.MuV, 0f);
            if (!transmitted)
            {
                float dot = Vector3.Dot(V, L);
                Vector3 fd = Vector3.Zero;
                if (h.DiffuseW > 0f) fd += h.DiffuseW * Eon.Evaluate(h.Rho, h.DiffRough, muL, muV, dot);
                if (h.ThinSss > 0f)
                    fd += h.ThinSss * 0.5f * (1f - h.SubAniso) * h.SubColor
                        * Eon.Evaluate(Vector3.One, h.DiffRough, muL, muV, dot);
                f += bm * d * DiffuseUnderSpecular(in h, in w, L, muL) * fd;
                pdf += pr.Diff * muL / MathF.PI;
            }
            else if (h.Thin && h.ThinSss > 0f)
            {
                Vector3 Lm = L - 2f * muL * h.N;
                // The sheet's diffuse transmission: albedo-1 lobes, split
                // S (1 ∓ g) / 2 between the two sides (OpenPBR thin-walled).
                Vector3 ft = h.ThinSss * 0.5f * (1f + h.SubAniso) * h.SubColor
                           * Eon.Evaluate(Vector3.One, h.DiffRough, -muL, muV, Vector3.Dot(V, Lm));
                f += bm * d * DiffuseUnderSpecular(in h, in w, L, -muL) * ft;
                pdf += pr.DiffT * -muL / MathF.PI;
            }
        }
        f *= h.Opacity;
        pdf *= h.Opacity / total;
        return f;
    }

    public Vector3 Evaluate(Vector3 V, Vector3 L, in HitRecord rec)
    {
        Hit h = GetHit(in rec);
        Layer w = Weights(in h, V);
        if (w.MuV <= 0f) return Vector3.Zero;
        Probs pr = Probabilities(in h, in w);
        return EvalAll(in h, in w, in pr, V, L, out _);
    }

    public float Pdf(Vector3 V, Vector3 L, in HitRecord rec)
    {
        Hit h = GetHit(in rec);
        Layer w = Weights(in h, V);
        if (w.MuV <= 0f) return 0f;
        Probs pr = Probabilities(in h, in w);
        EvalAll(in h, in w, in pr, V, L, out float pdf);
        return pdf;
    }

    // ── IMaterial: sampling ─────────────────────────────────────────────────

    public BsdfSample? Sample(Vector3 V, in HitRecord rec)
    {
        Hit h = GetHit(in rec);

        // Cut-out: the ray continues as if the surface were not there.
        if (h.Opacity < 1f && MathUtils.RandomFloat() >= h.Opacity)
            return new BsdfSample(-V, Vector3.One, 1f, isDelta: true, lobe: BsdfLobe.Transparent);

        Layer w = Weights(in h, V);
        if (w.MuV <= 0f) return null;
        Probs pr = Probabilities(in h, in w);
        float total = pr.Sum;
        if (total <= 0f) return null;

        float u = MathUtils.RandomFloat() * total;
        Vector3 N = h.N;
        Vector3 L;
        bool refraction = false;
        BsdfLobe lobe = BsdfLobe.Glossy;

        // Each lobe keeps only directions inside its own support: the pdf
        // the mixture reports there is then exactly the density of the draw.
        if ((u -= pr.Fuzz) < 0f)
        {
            var (u1, u2) = MathUtils.Random2D();
            L = FuzzLtc.Sample(h.Nf, V, h.FuzzRough, u1, u2);
            if (Vector3.Dot(h.Nf, L) <= 0f) return null;
        }
        else if ((u -= pr.Coat) < 0f)
        {
            if (h.CoatDelta)
            {
                Vector3 m = MathUtils.Reflect(-V, h.Nc);
                float fc = MathUtils.FresnelDielectric(w.MuVc, 1f / h.CoatEta);
                return new BsdfSample(m, new Vector3(w.GFuzz * h.Coat * fc * total / pr.Coat), 1f,
                                      isDelta: true);
            }
            var (u1, u2) = MathUtils.Random2D();
            Vector3 Hl = Microfacet.SampleGgxVndfAniso(Local(V, h.Tc, h.Bc, h.Nc), h.CoatAx, h.CoatAy, u1, u2);
            L = MathUtils.Reflect(-V, Hl.X * h.Tc + Hl.Y * h.Bc + Hl.Z * h.Nc);
            if (Vector3.Dot(h.Nc, L) <= 0f) return null;
        }
        else if ((u -= pr.Metal) < 0f)
        {
            if (h.BaseDelta)
            {
                Vector3 m = MathUtils.Reflect(-V, N);
                Vector3 wgt = BaseMultiplier(in h, in w, w.MuV, false) * h.Metal * MetalFresnel(in h, w.MuV)
                            * (total / pr.Metal);
                return new BsdfSample(m, wgt, 1f, isDelta: true);
            }
            float q = Math.Clamp(w.EMetalV, 0.05f, 1f);
            if (MathUtils.RandomFloat() < q)
            {
                var (u1, u2) = MathUtils.Random2D();
                Vector3 Hl = Microfacet.SampleGgxVndfAniso(Local(V, h.T, h.B, N), h.Ax, h.Ay, u1, u2);
                L = MathUtils.Reflect(-V, Hl.X * h.T + Hl.Y * h.B + Hl.Z * N);
            }
            else
            {
                L = Vector3.Normalize(N + MathUtils.RandomUnitVector());
            }
            if (Vector3.Dot(N, L) <= 0f) return null;
        }
        else if ((u -= pr.Iface) < 0f)
        {
            lobe = BsdfLobe.Transmission;   // set back to glossy on reflection below
            if (h.BaseDelta) return SampleSmoothInterface(in h, in w, V, in rec, pr.Iface / total);
            var (u1, u2) = MathUtils.Random2D();
            Vector3 Hl = Microfacet.SampleGgxVndfAniso(Local(V, h.T, h.B, N), h.Ax, h.Ay, u1, u2);
            Vector3 hw = Hl.X * h.T + Hl.Y * h.B + Hl.Z * N;
            float vh = Vector3.Dot(V, hw);
            if (vh <= 0f) return null;
            float pRefl = ReflectProbability(in h, DielectricFresnel(in h, vh));
            if (MathUtils.RandomFloat() < pRefl)
            {
                L = MathUtils.Reflect(-V, hw);
                if (Vector3.Dot(N, L) <= 0f) return null;
                lobe = BsdfLobe.Glossy;
            }
            else
            {
                if (h.Thin)
                {
                    L = MathUtils.Reflect(-V, hw);
                    L -= 2f * Vector3.Dot(L, N) * N;
                }
                else
                {
                    L = MathUtils.Refract(-V, hw, h.Ri);
                    if (MathUtils.NearZero(L)) return null;
                    refraction = true;
                }
                if (Vector3.Dot(N, L) >= 0f) return null;
            }
        }
        else if ((u -= pr.Diff) < 0f)
        {
            lobe = BsdfLobe.Diffuse;
            L = Vector3.Normalize(N + MathUtils.RandomUnitVector());
            if (Vector3.Dot(N, L) <= 0f) return null;
        }
        else
        {
            lobe = BsdfLobe.Diffuse;        // diffuse transmission (translucency)
            L = Vector3.Normalize(-N + MathUtils.RandomUnitVector());
            if (Vector3.Dot(N, L) >= 0f) return null;
        }

        L = Vector3.Normalize(L);
        Vector3 f = EvalAll(in h, in w, in pr, V, L, out float pdf);
        if (pdf <= 0f || !float.IsFinite(pdf)) return null;
        if (!refraction) return new BsdfSample(L, f, pdf, isDelta: false, lobe: lobe);
        return new BsdfSample(L, f, pdf, isDelta: false,
                              nextSegmentAbsorption: NextAbsorption(in h, rec.FrontFace),
                              transition: rec.FrontFace ? MediumTransition.Enter : MediumTransition.Exit,
                              lobe: BsdfLobe.Transmission, eta: 1f / h.Ri);
    }

    /// <summary>Smooth dielectric interface: Fresnel picks reflection or
    /// transmission, each a delta lobe.</summary>
    private static BsdfSample? SampleSmoothInterface(in Hit h, in Layer w, Vector3 V, in HitRecord rec, float pLobe)
    {
        Vector3 N = h.N;
        float muV = MathF.Min(w.MuV, 1f);
        Vector3 fres = DielectricFresnel(in h, muV);
        float pRefl = ReflectProbability(in h, fres);
        float d = h.Inside ? 1f : 1f - h.Metal;
        if (MathUtils.RandomFloat() < pRefl)
        {
            Vector3 m = MathUtils.Reflect(-V, N);
            Vector3 wgt = BaseMultiplier(in h, in w, muV, false) * d * h.SpecColor * fres / (pLobe * pRefl);
            return new BsdfSample(m, wgt, 1f, isDelta: true);
        }
        Vector3 bm = BaseMultiplier(in h, in w, muV, true) * d * h.Trans * h.TransTint * (Vector3.One - fres)
                   / (pLobe * (1f - pRefl));
        if (h.Thin)
            return new BsdfSample(-V, bm, 1f, isDelta: true, lobe: BsdfLobe.Transmission);
        Vector3 L = MathUtils.Refract(-V, N, h.Ri);
        if (MathUtils.NearZero(L)) return null;
        return new BsdfSample(Vector3.Normalize(L), bm * (h.Ri * h.Ri), 1f, isDelta: true,
                              nextSegmentAbsorption: NextAbsorption(in h, rec.FrontFace),
                              transition: rec.FrontFace ? MediumTransition.Enter : MediumTransition.Exit,
                              lobe: BsdfLobe.Transmission, eta: 1f / h.Ri);
    }

    /// <summary>Beer-Lambert coefficient for the segment after a refraction:
    /// the material's on the way in (none to switch to when it has none),
    /// vacuum on the way out.</summary>
    private static Vector3? NextAbsorption(in Hit h, bool entering) =>
        entering ? (h.Sigma == Vector3.Zero ? null : h.Sigma) : Vector3.Zero;

    // ── IMaterial: transmission queries ─────────────────────────────────────

    public bool HasTransmission(in HitRecord rec)
    {
        Hit h = GetHit(in rec);
        if (h.Inside) return !h.BaseDelta;
        if (h.Metal >= 1f) return false;
        return (h.Trans > 0f && !h.BaseDelta) || (h.Thin && h.ThinSss > 0f);
    }

    public bool TryGetDielectricIor(in HitRecord rec, out float ior)
    {
        ior = 1f;
        if (Parameters.ThinWalled || !_mayTransmit) return false;
        float n = MathF.Max(Parameters.SpecularIor.Value(in rec), 1e-3f);
        ior = ModulatedIor(n, MathF.Max(0f, Parameters.SpecularWeight.Value(in rec)));
        return true;
    }

    public bool TransmitsStraight(in HitRecord rec)
    {
        Hit h = GetHit(in rec);
        if (h.Opacity < 1f) return true;
        if (h.Trans <= 0f || !h.BaseDelta || h.Metal > 0f) return false;
        return h.Thin || MathF.Abs(h.Ri - 1f) < 1e-4f;
    }

    public Vector3 ShadowTransmittance(Vector3 wi, in HitRecord rec)
    {
        Hit h = GetHit(in rec);
        float cos = MathF.Min(MathF.Abs(Vector3.Dot(wi, rec.Normal)), 1f);
        if (h.Inside)
            return new Vector3(1f - MathUtils.FresnelDielectric(cos, 1f / MathF.Max(h.Ri, 1f / h.Ri)));
        Vector3 t = Vector3.Zero;
        if (h.Trans > 0f && h.Metal < 1f)
        {
            // Straight-through approximation: a shadow ray is not refracted,
            // so use the Fresnel of the denser side's critical-free direction.
            float f = MathUtils.FresnelDielectric(cos, MathF.Min(h.Ri, 1f / h.Ri));
            t = (1f - h.Metal) * h.Trans * (1f - f) * h.TransTint;
            if (h.Coat > 0f)
                t *= Vector3.Lerp(Vector3.One,
                    CoatAbsorption(in h, cos, cos, true) * (1f - MathUtils.FresnelDielectric(cos, 1f / h.CoatEta)), h.Coat);
            if (h.Fuzz > 0f) t *= 1f - h.Fuzz * FuzzLtc.Albedo(cos, h.FuzzRough);
        }
        return Vector3.Lerp(Vector3.One, t, h.Opacity);
    }

    public Vector3 ShadowAbsorption(in HitRecord rec)
    {
        Hit h = GetHit(in rec);
        return h.Inside ? Vector3.Zero : h.Sigma;
    }

    // ── IMaterial: emission and guides ──────────────────────────────────────

    public Vector3 Emit(float u, float v, Vector3 point, int objectSeed, bool frontFace)
    {
        var p = Parameters;
        if (!frontFace && !p.ThinWalled) return Vector3.Zero;
        float lum = p.EmissionLuminance.Value(u, v, point, objectSeed);
        if (lum <= 0f) return Vector3.Zero;
        Vector3 e = Vector3.Max(p.EmissionColor.Value(u, v, point, objectSeed), Vector3.Zero) * lum;
        float c = Clamp01(p.CoatWeight.Value(u, v, point, objectSeed));
        if (c > 0f)
        {
            Vector3 cc = Clamp01(p.CoatColor.Value(u, v, point, objectSeed));
            e *= Vector3.Lerp(Vector3.One, new Vector3(MathF.Sqrt(cc.X), MathF.Sqrt(cc.Y), MathF.Sqrt(cc.Z)), c);
        }
        return e * Clamp01(p.GeometryOpacity.Value(u, v, point, objectSeed));
    }

    public Vector3 AovAlbedo(in HitRecord rec)
    {
        Hit h = GetHit(in rec);
        if (h.Inside) return Vector3.One;
        Vector3 diel = (1f - h.Trans) * (h.DiffuseW * h.Rho + h.ThinSss * h.SubColor) + h.Trans * h.TransTint;
        if (!h.Thin && h.Trans > 0f && Parameters.SubsurfaceWeight.Value(in rec) > 0f)
            diel = Vector3.Lerp(diel, Clamp01(Parameters.SubsurfaceColor.Value(in rec)),
                                Clamp01(Parameters.SubsurfaceWeight.Value(in rec)));
        return Vector3.Lerp(diel, h.MetalF0, h.Metal);
    }
}
