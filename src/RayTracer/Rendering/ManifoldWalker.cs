using System.Numerics;
using System.Runtime.CompilerServices;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Materials;

namespace RayTracer.Rendering;

/// <summary>
/// Manifold Next Event Estimation (MNEE) — the Phase-2 caustics solver.
///
/// <para>Given a (diffuse) receiver point <c>x</c> and a sampled emitter point
/// <c>y</c>, the walker finds the smooth specular vertices on a caustic caster
/// (a glass sphere/lens, or a mirror) such that the chain
/// <c>x → p₁ (→ p₂) → y</c> satisfies Snell's law / the law of reflection at
/// every vertex. It then evaluates the unbiased single-/two-bounce specular
/// connection: the receiver sees the light through the focusing interface,
/// producing the focused caustic that ordinary next-event estimation (a
/// straight shadow ray) cannot.</para>
///
/// <para><b>Method.</b> Newton-Raphson on the specular manifold (Jakob &amp;
/// Marschner 2012; Hanika, Droske &amp; Manzi 2015). The unknowns are the
/// surface parameters (u, v) of each vertex; the residual is the tangential
/// component of the generalized half-vector
/// <c>ĥ = η_a·ω_a + η_b·ω_b</c> at each vertex (zero ⇔ ĥ ∥ n ⇔ Snell). The same
/// residual unifies reflection (η = 1 on both sides) and 1- or 2-interface
/// refraction — the per-side η is selected purely from the geometry
/// (<c>dot(endpoint − p, n) &gt; 0 ⇒ air, else glass</c>). The Jacobian is formed
/// by central finite differences in (u, v); each evaluation is pure arithmetic
/// (no ray casts), so a full solve is cheap.</para>
///
/// <para><b>Geometric term.</b> The estimator reparameterises the receiver's
/// direct-lighting integral from solid angle at <c>x</c> to area on the light:
/// <c>L = f_r(x,ω_x)·L_e(y)·T·G/pdf_A(y)</c>, where <c>T</c> is the product of
/// Fresnel transmission/reflection and Beer-Lambert interior absorption and
/// <c>G = dΩ_x/dA_y</c> is the generalized geometric term. <c>G</c> is computed
/// by perturbing <c>y</c> across the light surface and re-solving (warm-started)
/// — finite-difference ray differentials through the specular chain.</para>
///
/// <para>All buffers are stack-allocated spans; the walker allocates nothing on
/// the hot path. Failure (divergence, leaving the parameter domain, total
/// internal reflection, wrong-side solution) returns false and the caller
/// simply adds no caustic contribution — unbiased by construction.</para>
/// </summary>
public static class ManifoldWalker
{
    public const int   DefaultMaxIterations = 20;
    private const float ConvergenceTol      = 1e-5f;
    private const float FdStep              = 5e-4f;   // (u,v) finite-difference step
    private const int   MaxVertices         = 2;

    // Per-vertex seed charts are reference types, so they cannot be stackalloc'd
    // (stackalloc requires unmanaged element types). InlineArray keeps these
    // tiny fixed-size buffers on the stack — zero heap on the hot path — while
    // still exposing a Span the rest of the walk threads through.
    [System.Runtime.CompilerServices.InlineArray(MaxVertices)]
    private struct SeedBuf { private ManifoldSeed _e; }
    [System.Runtime.CompilerServices.InlineArray(MaxVertices)]
    private struct SurfBuf { private IManifoldSurface _e; }
    // A facet has up to three edge-neighbours; the neighbor-seed retry needs a
    // fixed buffer of that width (ManifoldSeed carries a reference, so InlineArray
    // — not stackalloc — keeps it on the stack).
    private const int MaxFacetNeighbors = 3;
    [System.Runtime.CompilerServices.InlineArray(MaxFacetNeighbors)]
    private struct NbrBuf { private ManifoldSeed _e; }

    /// <summary>A solved specular connection ready for the radiance estimator.</summary>
    public readonly struct CausticConnection
    {
        /// <summary>Unit direction from the receiver toward the first vertex (the caustic direction ω_x).</summary>
        public readonly Vector3 WiAtReceiver;
        /// <summary>First specular vertex p₁ (for the x → p₁ visibility test).</summary>
        public readonly Vector3 FirstVertex;
        /// <summary>Last specular vertex p_K (for the p_K → y visibility test).</summary>
        public readonly Vector3 LastVertex;
        /// <summary>∏ Fresnel·tint over the vertices, × Beer-Lambert interior absorption (per channel).</summary>
        public readonly Vector3 Throughput;
        /// <summary>Generalized geometric term dΩ_x/dA_y.</summary>
        public readonly float G;

        public CausticConnection(Vector3 wi, Vector3 first, Vector3 last, Vector3 throughput, float g)
        {
            WiAtReceiver = wi; FirstVertex = first; LastVertex = last; Throughput = throughput; G = g;
        }
    }

    /// <summary>
    /// Attempts to solve a SMOOTH (delta) caustic connection x → caster → y
    /// (Phase-2 MNEE). Returns false when no valid specular path exists or the
    /// solve does not converge.
    /// </summary>
    public static bool Connect(in CausticCasterRegistry.Caster caster,
                               in CausticInterface ci,
                               Vector3 x, Vector3 y, Vector3 yNormal,
                               int maxIter,
                               out CausticConnection conn)
    {
        conn = default;

        SeedBuf seedBuf = default;
        Span<ManifoldSeed> seeds = seedBuf;
        if (!caster.Seeder.SeedManifold(x, y, ci, seeds, out int k) || k < 1) return false;
        if (k > MaxVertices) k = MaxVertices;

        if (TrySmooth(caster, ci, x, y, yNormal, maxIter, seeds, k, out conn)) return true;

        // Per-triangle clamp rejected the converged vertex: for a mesh caster the
        // true specular vertex may lie on an edge-adjacent facet, so retry the
        // SAME solve with the offending vertex re-seeded on each neighbour. A no-op
        // for analytic/CSG casters (they don't implement INeighborSeedCaster).
        return RetryNeighborSeeds(caster, ci, x, y, yNormal, maxIter, seeds, k, rough: false, out conn);
    }

    // Smooth (delta) solve from an explicit seed set: no microfacet offset, so the
    // manifold targets the geometric normal at each vertex.
    private static bool TrySmooth(in CausticCasterRegistry.Caster caster,
                                  in CausticInterface ci,
                                  Vector3 x, Vector3 y, Vector3 yNormal, int maxIter,
                                  ReadOnlySpan<ManifoldSeed> seeds, int k,
                                  out CausticConnection conn)
    {
        Span<Vector2> uv = stackalloc Vector2[MaxVertices];
        SurfBuf surfBuf = default;
        Span<IManifoldSurface> surfs = surfBuf;
        for (int i = 0; i < k; i++) { uv[i] = seeds[i].Uv; surfs[i] = seeds[i].Chart; }

        return SolveAndEstimate(caster, ci, x, y, yNormal, maxIter, k, uv, surfs,
                                ReadOnlySpan<Vector3>.Empty, out conn);
    }

    /// <summary>
    /// Attempts to solve a ROUGH (frosted) caustic connection x → caster → y by
    /// Specular Manifold Sampling (Phase-2b; Zeltner, Georgiev &amp; Jakob 2020).
    /// A microfacet normal is sampled from the caster's GGX VNDF at each vertex
    /// (visible from the receiver-side incident direction) and the manifold is
    /// solved so the generalized half-vector aligns with that sampled microfacet
    /// rather than the bare geometric normal. The caller runs several trials and
    /// averages — this is one stochastic trial of the biased SMS estimator
    /// (Zeltner §4.1); the unbiased reciprocal-probability estimator (§4.2) is a
    /// future Phase-2c extension.
    /// </summary>
    public static bool ConnectRough(in CausticCasterRegistry.Caster caster,
                                    in CausticInterface ci,
                                    Vector3 x, Vector3 y, Vector3 yNormal,
                                    int maxIter,
                                    out CausticConnection conn)
    {
        conn = default;

        SeedBuf seedBuf = default;
        Span<ManifoldSeed> seeds = seedBuf;
        if (!caster.Seeder.SeedManifold(x, y, ci, seeds, out int k) || k < 1) return false;
        if (k > MaxVertices) k = MaxVertices;

        if (TryRough(caster, ci, x, y, yNormal, maxIter, seeds, k, out conn)) return true;

        // Same neighbor-seed retry as the smooth path (mesh edge-crossing tier 1).
        return RetryNeighborSeeds(caster, ci, x, y, yNormal, maxIter, seeds, k, rough: true, out conn);
    }

    // One stochastic SMS trial from an explicit seed set: samples a microfacet
    // normal per vertex from the GGX VNDF and solves the manifold against it.
    private static bool TryRough(in CausticCasterRegistry.Caster caster,
                                 in CausticInterface ci,
                                 Vector3 x, Vector3 y, Vector3 yNormal, int maxIter,
                                 ReadOnlySpan<ManifoldSeed> seeds, int k,
                                 out CausticConnection conn)
    {
        conn = default;

        Span<Vector2> uv = stackalloc Vector2[MaxVertices];
        SurfBuf surfBuf = default;
        Span<IManifoldSurface> surfs = surfBuf;
        for (int i = 0; i < k; i++) { uv[i] = seeds[i].Uv; surfs[i] = seeds[i].Chart; }

        // Sample one microfacet normal per seed vertex from the GGX VNDF, in the
        // vertex's tangent frame, visible from the receiver-side incident dir.
        Span<ManifoldPoint> seedPts = stackalloc ManifoldPoint[MaxVertices];
        for (int i = 0; i < k; i++)
            if (!surfs[i].EvaluateManifold(uv[i].X, uv[i].Y, out seedPts[i]))
                return false;

        Span<Vector3> micro = stackalloc Vector3[MaxVertices];
        for (int i = 0; i < k; i++)
        {
            Vector3 p   = seedPts[i].P;
            Vector3 nrm = seedPts[i].N;
            Vector3 a   = (i == 0) ? x : seedPts[i - 1].P;     // receiver-side neighbour
            Vector3 wa  = a - p;
            float   la  = wa.Length();
            if (la < 1e-9f) return false;
            wa /= la;

            Microfacet.BuildTangentFrame(nrm, out Vector3 T, out Vector3 B);
            // The VNDF is defined on the outward hemisphere; flip the incident
            // direction below the surface so the visible-normal sample is valid.
            float side = Vector3.Dot(wa, nrm) < 0f ? -1f : 1f;
            Vector3 Vloc = new(Vector3.Dot(wa, T), Vector3.Dot(wa, B), side * Vector3.Dot(wa, nrm));
            Vector3 Hloc = Microfacet.SampleGgxVndfAniso(Vloc, ci.AlphaX, ci.AlphaY,
                                                         MathUtils.RandomFloat(), MathUtils.RandomFloat());
            micro[i] = new Vector3(Hloc.X, Hloc.Y, side * Hloc.Z);   // local microfacet normal
        }

        return SolveAndEstimate(caster, ci, x, y, yNormal, maxIter, k, uv, surfs, micro, out conn);
    }

    // ── Mesh edge-crossing, neighbor-seed tier ───────────────────────────────
    // When the primary solve is rejected (the converged vertex slid off its seed
    // facet), retry the connection with one vertex re-seeded on each edge-adjacent
    // facet. Each retry is a full independent solve on the neighbour chart, whose
    // own per-triangle clamp accepts only if the vertex truly lands there — so the
    // estimator stays unbiased and a successful retry is the connection the seed
    // facet should have produced. Returns false (unchanged behaviour) for casters
    // that do not implement INeighborSeedCaster, i.e. analytic primitives and CSG.
    private static bool RetryNeighborSeeds(in CausticCasterRegistry.Caster caster,
                                           in CausticInterface ci,
                                           Vector3 x, Vector3 y, Vector3 yNormal, int maxIter,
                                           ReadOnlySpan<ManifoldSeed> baseSeeds, int k,
                                           bool rough, out CausticConnection conn)
    {
        conn = default;
        if (caster.Seeder is not INeighborSeedCaster nb) return false;

        SeedBuf trialBuf = default;
        Span<ManifoldSeed> trial = trialBuf;
        for (int v = 0; v < k; v++)
        {
            NbrBuf nbrBuf = default;
            Span<ManifoldSeed> nbrs = nbrBuf;
            int nc = nb.FacetNeighbors(baseSeeds[v], nbrs);
            for (int j = 0; j < nc; j++)
            {
                for (int i = 0; i < k; i++) trial[i] = baseSeeds[i];
                trial[v] = nbrs[j];

                bool ok = rough
                    ? TryRough(caster, ci, x, y, yNormal, maxIter, trial, k, out conn)
                    : TrySmooth(caster, ci, x, y, yNormal, maxIter, trial, k, out conn);
                if (ok) return true;
            }
        }
        return false;
    }

    // Solve the manifold (with an optional per-vertex microfacet offset), then
    // validate, accumulate throughput, and form the geometric term. The
    // <paramref name="micro"/> span is empty for a smooth caster (geometric
    // normal) or carries one local-frame microfacet normal per vertex for SMS.
    private static bool SolveAndEstimate(in CausticCasterRegistry.Caster caster,
                                         in CausticInterface ci,
                                         Vector3 x, Vector3 y, Vector3 yNormal,
                                         int maxIter, int k, Span<Vector2> uv,
                                         ReadOnlySpan<IManifoldSurface> surfs,
                                         ReadOnlySpan<Vector3> micro,
                                         out CausticConnection conn)
    {
        conn = default;

        // ── Newton solve on the 2K-dimensional manifold ─────────────────────
        Span<ManifoldPoint> pts = stackalloc ManifoldPoint[MaxVertices];
        if (!Solve(surfs, x, y, ci, k, uv, pts, maxIter, micro)) return false;

        // ── Validate orientation & physical admissibility ───────────────────
        if (!Validate(x, y, ci, k, pts)) return false;

        // ── Per-chart acceptance clamp (post-convergence) ───────────────────
        // A CSG chart accepts the converged vertex only if it still lies on the
        // boolean result's boundary (not in a region a subtraction/intersection
        // removed) — the CSG analog of the mesh per-triangle barycentric clamp,
        // run once here rather than every Newton step so the solve stays cheap.
        for (int i = 0; i < k; i++)
            if (surfs[i] is IClampedChart cc && !cc.Accept(pts[i])) return false;

        Vector3 p1 = pts[0].P;
        Vector3 pK = pts[k - 1].P;
        Vector3 wi = Vector3.Normalize(p1 - x);

        // ── Throughput: Fresnel·tint (+ rough microfacet G1) + Beer-Lambert ──
        if (!ComputeThroughput(x, y, ci, k, pts, out Vector3 throughput)) return false;

        // ── Geometric term G = dΩ_x/dA_y via light-perturbation re-solve ─────
        float g = ComputeGeometricTerm(surfs, x, y, yNormal, ci, k, uv, wi, maxIter, micro);
        if (!(g > 0f) || float.IsNaN(g) || float.IsInfinity(g)) return false;

        conn = new CausticConnection(wi, p1, pK, throughput, g);
        return true;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Newton-Raphson solve
    // ────────────────────────────────────────────────────────────────────────

    private static bool Solve(ReadOnlySpan<IManifoldSurface> surfs, Vector3 x, Vector3 y,
                              in CausticInterface ci, int k,
                              Span<Vector2> uv, Span<ManifoldPoint> pts, int maxIter,
                              ReadOnlySpan<Vector3> micro)
    {
        int n = 2 * k;
        Span<float>   F     = stackalloc float[4];
        Span<float>   Ftmp  = stackalloc float[4];
        Span<float>   J     = stackalloc float[16]; // row-major n×n
        Span<float>   dq    = stackalloc float[4];
        Span<Vector2> trial = stackalloc Vector2[MaxVertices];

        if (!Evaluate(surfs, x, y, ci, k, uv, pts, F, micro)) return false;
        float fNorm = Norm(F, n);

        for (int iter = 0; iter < maxIter; iter++)
        {
            if (fNorm < ConvergenceTol) return true;

            // Jacobian by central differences in each (u,v) component. The
            // microfacet offset is held fixed across the differencing, so the
            // Jacobian captures ∂F/∂(u,v) exactly as in the smooth case.
            for (int j = 0; j < n; j++)
            {
                int vert = j >> 1, comp = j & 1;
                Vector2 saved = uv[vert];

                uv[vert] = Offset(saved, comp, FdStep);
                if (!Evaluate(surfs, x, y, ci, k, uv, pts, Ftmp, micro)) { uv[vert] = saved; return false; }
                for (int i = 0; i < n; i++) J[i * n + j] = Ftmp[i];

                uv[vert] = Offset(saved, comp, -FdStep);
                if (!Evaluate(surfs, x, y, ci, k, uv, pts, Ftmp, micro)) { uv[vert] = saved; return false; }
                float inv = 1f / (2f * FdStep);
                for (int i = 0; i < n; i++) J[i * n + j] = (J[i * n + j] - Ftmp[i]) * inv;

                uv[vert] = saved;
            }

            // Base residual is F (uv unchanged since central differences restore it).
            for (int i = 0; i < n; i++) dq[i] = -F[i];
            if (!SolveLinear(J, dq, n)) return false;

            // Damped step with backtracking line search on |F|.
            float lambda = 1f;
            bool improved = false;
            for (int bt = 0; bt < 8; bt++)
            {
                for (int vert = 0; vert < k; vert++)
                    trial[vert] = new Vector2(uv[vert].X + lambda * dq[vert * 2],
                                              uv[vert].Y + lambda * dq[vert * 2 + 1]);
                if (Evaluate(surfs, x, y, ci, k, trial, pts, Ftmp, micro))
                {
                    float trialNorm = Norm(Ftmp, n);
                    if (trialNorm < fNorm)
                    {
                        for (int vert = 0; vert < k; vert++) uv[vert] = trial[vert];
                        for (int i = 0; i < n; i++) F[i] = Ftmp[i];
                        fNorm = trialNorm;
                        improved = true;
                        break;
                    }
                }
                lambda *= 0.5f;
            }
            if (!improved) return false;
        }
        return fNorm < ConvergenceTol;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector2 Offset(Vector2 uv, int comp, float d)
        => comp == 0 ? new Vector2(uv.X + d, uv.Y) : new Vector2(uv.X, uv.Y + d);

    // Evaluates the residual vector F (length 2k) for the current parameters,
    // and fills pts with the evaluated vertices. Returns false if any vertex
    // evaluation is degenerate.
    //
    // The residual drives the generalized half-vector ĥ = η_a·ω_a + η_b·ω_b to
    // be parallel to the manifold target normal: the geometric normal n for a
    // smooth caster (<paramref name="micro"/> empty), or the sampled microfacet
    // normal m for SMS. Tangential components measured against the target's ONB
    // vanish exactly when ĥ ∥ target, i.e. (rough) Snell/reflection holds.
    private static bool Evaluate(ReadOnlySpan<IManifoldSurface> surfs, Vector3 x, Vector3 y,
                                 in CausticInterface ci, int k,
                                 Span<Vector2> uv, Span<ManifoldPoint> pts, Span<float> F,
                                 ReadOnlySpan<Vector3> micro)
    {
        for (int i = 0; i < k; i++)
        {
            if (!surfs[i].EvaluateManifold(uv[i].X, uv[i].Y, out pts[i])) return false;
        }

        bool rough = micro.Length > 0;
        for (int i = 0; i < k; i++)
        {
            Vector3 p = pts[i].P;
            Vector3 nrm = pts[i].N;
            Vector3 a = (i == 0) ? x : pts[i - 1].P;
            Vector3 b = (i == k - 1) ? y : pts[i + 1].P;

            Vector3 wa = a - p; float la = wa.Length();
            Vector3 wb = b - p; float lb = wb.Length();
            if (la < 1e-9f || lb < 1e-9f) return false;
            wa /= la; wb /= lb;

            float ea = EtaOnSide(wa, nrm, ci);
            float eb = EtaOnSide(wb, nrm, ci);
            Vector3 h = ea * wa + eb * wb;
            float hl = h.Length();
            if (hl < 1e-9f) return false;
            Vector3 hHat = h / hl;

            // Manifold target: geometric normal (smooth) or the fixed microfacet
            // normal m (rough), re-expressed in world space from its local frame.
            Vector3 target = rough ? MicroNormalWorld(nrm, micro[i]) : nrm;
            Onb(target, out Vector3 s, out Vector3 t);
            F[i * 2]     = Vector3.Dot(s, hHat);
            F[i * 2 + 1] = Vector3.Dot(t, hHat);
        }
        return true;
    }

    // Re-expresses a local-frame microfacet normal (sampled in the seed tangent
    // frame) in world space around the current geometric normal. Using the same
    // Frisvad frame the sampler used keeps the offset consistent as the vertex
    // slides during the Newton iteration.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector3 MicroNormalWorld(Vector3 nrm, Vector3 hLocal)
    {
        Microfacet.BuildTangentFrame(nrm, out Vector3 T, out Vector3 B);
        return Vector3.Normalize(T * hLocal.X + B * hLocal.Y + nrm * hLocal.Z);
    }

    // η of the medium on the side the direction w (already pointing away from p)
    // leads to. Outward (dot>0) = the surrounding medium (ci.AmbientIor: air = 1
    // for an open caster, the enclosing dielectric's IOR for a nested one); the
    // caster interior (dot<0) = ci.Ior. Pure reflection uses η = 1 on both sides.
    // With AmbientIor = 1 this is identical to the legacy always-air form.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float EtaOnSide(Vector3 w, Vector3 n, in CausticInterface ci)
    {
        if (!ci.IsTransmissive) return 1f;
        float ambient = ci.AmbientIor > 0f ? ci.AmbientIor : 1f;
        return Vector3.Dot(w, n) > 0f ? ambient : ci.Ior;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Validation, throughput, geometric term
    // ────────────────────────────────────────────────────────────────────────

    private static bool Validate(Vector3 x, Vector3 y, in CausticInterface ci, int k,
                                 Span<ManifoldPoint> pts)
    {
        for (int i = 0; i < k; i++)
        {
            Vector3 p = pts[i].P, nrm = pts[i].N;
            Vector3 a = (i == 0) ? x : pts[i - 1].P;
            Vector3 b = (i == k - 1) ? y : pts[i + 1].P;
            float da = Vector3.Dot(Vector3.Normalize(a - p), nrm);
            float db = Vector3.Dot(Vector3.Normalize(b - p), nrm);
            if (MathF.Abs(da) < 1e-4f || MathF.Abs(db) < 1e-4f) return false; // grazing

            if (ci.IsTransmissive)
            {
                // Refraction: the two endpoints must straddle the interface.
                if (MathF.Sign(da) == MathF.Sign(db)) return false;
            }
            else
            {
                // Reflection: both endpoints on the outward side.
                if (da <= 0f || db <= 0f) return false;
            }
        }
        return true;
    }

    private static bool ComputeThroughput(Vector3 x, Vector3 y, in CausticInterface ci, int k,
                                          Span<ManifoldPoint> pts, out Vector3 throughput)
    {
        bool rough = ci.IsRough;
        // Transmission starts from the glass tint; reflection accumulates the
        // conductor Schlick term (which already carries the metal reflectance).
        throughput = ci.IsTransmissive ? ci.Tint : Vector3.One;
        for (int i = 0; i < k; i++)
        {
            Vector3 p = pts[i].P, nrm = pts[i].N;
            Vector3 a = (i == 0) ? x : pts[i - 1].P;
            Vector3 wbDir = Vector3.Normalize(((i == k - 1) ? y : pts[i + 1].P) - p);
            Vector3 wa = Vector3.Normalize(a - p);

            float etaIncident = EtaOnSide(wa, nrm, ci);
            float etaTrans    = EtaOnSide(wbDir, nrm, ci);

            // Fresnel is evaluated against the microfacet normal: the geometric
            // normal for a smooth caster, or the converged half-vector m (which
            // the solver aligned ĥ to) for SMS.
            Vector3 mN;
            if (rough)
            {
                Vector3 h = etaIncident * wa + etaTrans * wbDir;
                float hl = h.Length();
                if (hl < 1e-9f) return false;
                mN = h / hl;
                if (Vector3.Dot(mN, nrm) < 0f) mN = -mN; // orient to the outward hemisphere
            }
            else mN = nrm;

            float cosI = MathF.Min(MathF.Abs(Vector3.Dot(wa, mN)), 1f);

            if (ci.IsTransmissive)
            {
                float eta = etaIncident / etaTrans;
                float fr = MathUtils.FresnelDielectric(cosI, eta);
                if (fr >= 1f) return false; // TIR — no transmitted path
                throughput *= (1f - fr);
            }
            else
            {
                // Schlick-conductor Fresnel, F0 = tint (the metal reflectance):
                // F = F0 + (1 − F0)·(1 − cosθ)⁵.
                float m1 = 1f - cosI;
                float m5 = m1 * m1; m5 = m5 * m5 * m1;
                throughput *= ci.Tint + (Vector3.One - ci.Tint) * m5;
            }

            // SMS microfacet shadowing-masking: VNDF sampling cancels D and the
            // view-side G1, leaving the light-side G1(L) — the same BSDF/pdf
            // weight the rough-glass scatter applies (see DisneyBsdf.ScatterTransmission).
            if (rough)
            {
                Microfacet.BuildTangentFrame(nrm, out Vector3 T, out Vector3 B);
                Vector3 lLoc = new(Vector3.Dot(wbDir, T), Vector3.Dot(wbDir, B),
                                   MathF.Abs(Vector3.Dot(wbDir, nrm)));
                throughput *= Microfacet.G1GgxAniso(lLoc, ci.AlphaX, ci.AlphaY);
            }
        }

        // Beer-Lambert across the interior segment of a two-interface solid.
        if (ci.IsTransmissive && k == 2 && (ci.Absorption.X > 0f || ci.Absorption.Y > 0f || ci.Absorption.Z > 0f))
        {
            float d = (pts[1].P - pts[0].P).Length();
            throughput *= new Vector3(
                MathF.Exp(-ci.Absorption.X * d),
                MathF.Exp(-ci.Absorption.Y * d),
                MathF.Exp(-ci.Absorption.Z * d));
        }
        return throughput.X > 0f || throughput.Y > 0f || throughput.Z > 0f;
    }

    private static float ComputeGeometricTerm(ReadOnlySpan<IManifoldSurface> surfs, Vector3 x, Vector3 y,
                                              Vector3 yNormal, in CausticInterface ci, int k,
                                              Span<Vector2> uv, Vector3 wi, int maxIter,
                                              ReadOnlySpan<Vector3> micro)
    {
        // Perturb the light point across its OWN surface plane (⊥ light normal)
        // and re-solve (warm-started) to get ∂ω_x/∂(light area). Using the light
        // tangent frame keeps the area measure consistent with the emitter's
        // area pdf, so the resulting G = dΩ_x/dA_y reduces to the familiar
        // cosθ_y/r² for a trivial (non-specular) connection and generalizes it
        // through the specular chain otherwise. G = |det| of the 2×2 map
        // expressed in a basis perpendicular to ω_x.
        Onb(wi, out Vector3 bu, out Vector3 bv);

        float eps = 1e-3f * MathF.Max(1f, (y - x).Length());
        Onb(yNormal, out Vector3 e1, out Vector3 e2);

        if (!ResolveWiPerturbed(surfs, x, y + eps * e1, ci, k, uv, maxIter, micro, out Vector3 wi1)) return 0f;
        if (!ResolveWiPerturbed(surfs, x, y + eps * e2, ci, k, uv, maxIter, micro, out Vector3 wi2)) return 0f;

        Vector3 d1 = (wi1 - wi) / eps;
        Vector3 d2 = (wi2 - wi) / eps;

        float m00 = Vector3.Dot(bu, d1), m01 = Vector3.Dot(bu, d2);
        float m10 = Vector3.Dot(bv, d1), m11 = Vector3.Dot(bv, d2);
        return MathF.Abs(m00 * m11 - m01 * m10);
    }

    private static bool ResolveWiPerturbed(ReadOnlySpan<IManifoldSurface> surfs, Vector3 x, Vector3 yPert,
                                           in CausticInterface ci, int k,
                                           Span<Vector2> uvSeed, int maxIter,
                                           ReadOnlySpan<Vector3> micro, out Vector3 wi)
    {
        wi = default;
        Span<Vector2> uv = stackalloc Vector2[MaxVertices];
        for (int i = 0; i < k; i++) uv[i] = uvSeed[i];
        Span<ManifoldPoint> pts = stackalloc ManifoldPoint[MaxVertices];
        // The microfacet offset is held fixed across the light perturbation, so
        // G = dΩ_x/dA_y is the geometric term of the rough path at that offset.
        if (!Solve(surfs, x, yPert, ci, k, uv, pts, maxIter, micro)) return false;
        wi = Vector3.Normalize(pts[0].P - x);
        return true;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Small numerics
    // ────────────────────────────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Norm(Span<float> v, int n)
    {
        float s = 0f;
        for (int i = 0; i < n; i++) s += v[i] * v[i];
        return MathF.Sqrt(s);
    }

    // Builds a right-handed orthonormal basis (a, b) spanning the plane ⊥ n.
    private static void Onb(Vector3 n, out Vector3 a, out Vector3 b)
    {
        // Duff et al. 2017 — branchless ONB.
        float sign = MathF.CopySign(1f, n.Z);
        float aa = -1f / (sign + n.Z);
        float bb = n.X * n.Y * aa;
        a = new Vector3(1f + sign * n.X * n.X * aa, sign * bb, -sign * n.X);
        b = new Vector3(bb, sign + n.Y * n.Y * aa, -n.Y);
    }

    // Gaussian elimination with partial pivoting for n ≤ 4. Solves A·x = b
    // in place (A row-major n×n, b length n, result written back into b).
    private static bool SolveLinear(Span<float> A, Span<float> b, int n)
    {
        for (int col = 0; col < n; col++)
        {
            int piv = col;
            float maxv = MathF.Abs(A[col * n + col]);
            for (int r = col + 1; r < n; r++)
            {
                float v = MathF.Abs(A[r * n + col]);
                if (v > maxv) { maxv = v; piv = r; }
            }
            if (maxv < 1e-12f) return false;
            if (piv != col)
            {
                for (int c = 0; c < n; c++)
                    (A[col * n + c], A[piv * n + c]) = (A[piv * n + c], A[col * n + c]);
                (b[col], b[piv]) = (b[piv], b[col]);
            }
            float inv = 1f / A[col * n + col];
            for (int r = 0; r < n; r++)
            {
                if (r == col) continue;
                float f = A[r * n + col] * inv;
                if (f == 0f) continue;
                for (int c = col; c < n; c++) A[r * n + c] -= f * A[col * n + c];
                b[r] -= f * b[col];
            }
        }
        for (int i = 0; i < n; i++) b[i] /= A[i * n + i];
        return true;
    }
}
