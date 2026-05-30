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
    /// Attempts to solve a caustic connection x → caster → y. Returns false when
    /// no valid specular path exists or the solve does not converge.
    /// </summary>
    public static bool Connect(in CausticCasterRegistry.Caster caster,
                               in CausticInterface ci,
                               Vector3 x, Vector3 y, Vector3 yNormal,
                               int maxIter,
                               out CausticConnection conn)
    {
        conn = default;

        // ── Seed: where does the straight x→y segment cross the caster? ──────
        Span<Vector2> uv  = stackalloc Vector2[MaxVertices];
        Span<float>   tcr = stackalloc float[MaxVertices];
        int k = SeedCrossings(caster.Hittable, x, y, uv, tcr);

        if (ci.IsTransmissive)
        {
            // Refraction needs the straight ray to pass through the caster: 1
            // crossing (single interface) or 2 (solid glass, enter + exit).
            if (k < 1) return false;
        }
        else
        {
            // Reflection: the straight x→y ray does not touch the mirror, so
            // seed from the surface point nearest the chord midpoint instead.
            if (!SeedReflection(caster, x, y, out uv[0])) return false;
            k = 1;
        }
        if (k > MaxVertices) k = MaxVertices;

        // ── Newton solve on the 2K-dimensional manifold ─────────────────────
        Span<ManifoldPoint> pts = stackalloc ManifoldPoint[MaxVertices];
        if (!Solve(caster.Surface, x, y, ci, k, uv, pts, maxIter)) return false;

        // ── Validate orientation & physical admissibility ───────────────────
        if (!Validate(x, y, ci, k, pts)) return false;

        Vector3 p1 = pts[0].P;
        Vector3 pK = pts[k - 1].P;
        Vector3 wi = Vector3.Normalize(p1 - x);

        // ── Throughput: Fresnel·tint at each vertex + Beer-Lambert interior ──
        if (!ComputeThroughput(x, y, ci, k, pts, out Vector3 throughput)) return false;

        // ── Geometric term G = dΩ_x/dA_y via light-perturbation re-solve ─────
        float g = ComputeGeometricTerm(caster.Surface, x, y, yNormal, ci, k, uv, wi, maxIter);
        if (!(g > 0f) || float.IsNaN(g) || float.IsInfinity(g)) return false;

        conn = new CausticConnection(wi, p1, pK, throughput, g);
        return true;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Seeding
    // ────────────────────────────────────────────────────────────────────────

    private static int SeedCrossings(IHittable caster, Vector3 x, Vector3 y,
                                     Span<Vector2> uvOut, Span<float> tOut)
    {
        Vector3 d = y - x;
        float len = d.Length();
        if (len < 1e-9f) return 0;
        Vector3 dir = d / len;

        int count = 0;
        float tStart = 1e-4f;
        while (count < MaxVertices)
        {
            var rec = new HitRecord();
            if (!caster.Hit(new Ray(x, dir), tStart, len - 1e-4f, ref rec)) break;
            uvOut[count] = new Vector2(rec.U, rec.V);
            tOut[count]  = rec.T;
            count++;
            tStart = rec.T + 1e-3f;
        }
        return count;
    }

    // Reflection seed: scan the surface for the (u,v) whose normal best bisects
    // x and y (the law-of-reflection seed), then let Newton refine. A coarse
    // 8×4 scan is plenty for a convex mirror and costs only arithmetic.
    private static bool SeedReflection(in CausticCasterRegistry.Caster caster,
                                       Vector3 x, Vector3 y, out Vector2 bestUv)
    {
        bestUv = new Vector2(0.5f, 0.5f);
        float best = float.MaxValue;
        bool found = false;
        for (int iu = 0; iu < 8; iu++)
        for (int iv = 1; iv < 4; iv++)
        {
            float u = (iu + 0.5f) / 8f;
            float v = (iv + 0.5f) / 4f;
            if (!caster.Surface.EvaluateManifold(u, v, out var pt)) continue;
            Vector3 wa = Vector3.Normalize(x - pt.P);
            Vector3 wb = Vector3.Normalize(y - pt.P);
            // Both endpoints must be on the reflective (outward) side.
            if (Vector3.Dot(wa, pt.N) <= 0f || Vector3.Dot(wb, pt.N) <= 0f) continue;
            Vector3 h = Vector3.Normalize(wa + wb);
            float resid = 1f - MathF.Abs(Vector3.Dot(h, pt.N)); // 0 when h ∥ n
            if (resid < best) { best = resid; bestUv = new Vector2(u, v); found = true; }
        }
        return found;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Newton-Raphson solve
    // ────────────────────────────────────────────────────────────────────────

    private static bool Solve(IManifoldSurface surf, Vector3 x, Vector3 y,
                              in CausticInterface ci, int k,
                              Span<Vector2> uv, Span<ManifoldPoint> pts, int maxIter)
    {
        int n = 2 * k;
        Span<float>   F     = stackalloc float[4];
        Span<float>   Ftmp  = stackalloc float[4];
        Span<float>   J     = stackalloc float[16]; // row-major n×n
        Span<float>   dq    = stackalloc float[4];
        Span<Vector2> trial = stackalloc Vector2[MaxVertices];

        if (!Evaluate(surf, x, y, ci, k, uv, pts, F)) return false;
        float fNorm = Norm(F, n);

        for (int iter = 0; iter < maxIter; iter++)
        {
            if (fNorm < ConvergenceTol) return true;

            // Jacobian by central differences in each (u,v) component.
            for (int j = 0; j < n; j++)
            {
                int vert = j >> 1, comp = j & 1;
                Vector2 saved = uv[vert];

                uv[vert] = Offset(saved, comp, FdStep);
                if (!Evaluate(surf, x, y, ci, k, uv, pts, Ftmp)) { uv[vert] = saved; return false; }
                for (int i = 0; i < n; i++) J[i * n + j] = Ftmp[i];

                uv[vert] = Offset(saved, comp, -FdStep);
                if (!Evaluate(surf, x, y, ci, k, uv, pts, Ftmp)) { uv[vert] = saved; return false; }
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
                if (Evaluate(surf, x, y, ci, k, trial, pts, Ftmp))
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
    private static bool Evaluate(IManifoldSurface surf, Vector3 x, Vector3 y,
                                 in CausticInterface ci, int k,
                                 Span<Vector2> uv, Span<ManifoldPoint> pts, Span<float> F)
    {
        for (int i = 0; i < k; i++)
        {
            if (!surf.EvaluateManifold(uv[i].X, uv[i].Y, out pts[i])) return false;
        }

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

            Onb(nrm, out Vector3 s, out Vector3 t);
            F[i * 2]     = Vector3.Dot(s, hHat);
            F[i * 2 + 1] = Vector3.Dot(t, hHat);
        }
        return true;
    }

    // η of the medium on the side the direction w (already pointing away from p)
    // leads to. Air (outward, dot>0) = 1; glass interior (dot<0) = ior. Pure
    // reflection uses η = 1 on both sides.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float EtaOnSide(Vector3 w, Vector3 n, in CausticInterface ci)
    {
        if (!ci.IsTransmissive) return 1f;
        return Vector3.Dot(w, n) > 0f ? 1f : ci.Ior;
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
        throughput = ci.Tint;
        for (int i = 0; i < k; i++)
        {
            Vector3 p = pts[i].P, nrm = pts[i].N;
            Vector3 a = (i == 0) ? x : pts[i - 1].P;
            Vector3 wa = Vector3.Normalize(a - p);
            float cosI = MathF.Min(MathF.Abs(Vector3.Dot(wa, nrm)), 1f);

            if (ci.IsTransmissive)
            {
                float etaIncident = EtaOnSide(wa, nrm, ci);
                Vector3 wbDir = Vector3.Normalize(((i == k - 1) ? y : pts[i + 1].P) - p);
                float etaTrans = EtaOnSide(wbDir, nrm, ci);
                float eta = etaIncident / etaTrans;
                float fr = MathUtils.FresnelDielectric(cosI, eta);
                if (fr >= 1f) return false; // TIR — no transmitted path
                throughput *= (1f - fr);
            }
            else
            {
                float fr = MathUtils.FresnelDielectric(cosI, 1f / MathF.Max(ci.Ior, 1.0001f));
                throughput *= fr;
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

    private static float ComputeGeometricTerm(IManifoldSurface surf, Vector3 x, Vector3 y,
                                              Vector3 yNormal, in CausticInterface ci, int k,
                                              Span<Vector2> uv, Vector3 wi, int maxIter)
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

        if (!ResolveWiPerturbed(surf, x, y + eps * e1, ci, k, uv, maxIter, out Vector3 wi1)) return 0f;
        if (!ResolveWiPerturbed(surf, x, y + eps * e2, ci, k, uv, maxIter, out Vector3 wi2)) return 0f;

        Vector3 d1 = (wi1 - wi) / eps;
        Vector3 d2 = (wi2 - wi) / eps;

        float m00 = Vector3.Dot(bu, d1), m01 = Vector3.Dot(bu, d2);
        float m10 = Vector3.Dot(bv, d1), m11 = Vector3.Dot(bv, d2);
        return MathF.Abs(m00 * m11 - m01 * m10);
    }

    private static bool ResolveWiPerturbed(IManifoldSurface surf, Vector3 x, Vector3 yPert,
                                           in CausticInterface ci, int k,
                                           Span<Vector2> uvSeed, int maxIter, out Vector3 wi)
    {
        wi = default;
        Span<Vector2> uv = stackalloc Vector2[MaxVertices];
        for (int i = 0; i < k; i++) uv[i] = uvSeed[i];
        Span<ManifoldPoint> pts = stackalloc ManifoldPoint[MaxVertices];
        if (!Solve(surf, x, yPert, ci, k, uv, pts, maxIter)) return false;
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
