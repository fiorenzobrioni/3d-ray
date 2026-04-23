using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Geometry;

/// <summary>
/// One piece of the meridian profile of a <see cref="Lathe"/>. Every segment
/// exposes the same hit / sample contract so the outer <see cref="Lathe"/>
/// can scan segments uniformly regardless of their representation (linear
/// frustum or cubic Bezier).
///
/// Coordinates are expressed in lathe-local space: the axis of revolution is
/// the Y axis, the profile lives in the half-plane <c>x ≥ 0, z = 0</c>.
/// </summary>
internal interface ILatheSegment
{
    AABB LocalBounds { get; }
    float YMin { get; }
    float YMax { get; }

    /// <summary>Arc length of the meridian curve (not the surface of revolution).</summary>
    float ArcLength { get; }

    /// <summary>Total lateral surface area of the revolved segment.</summary>
    float LateralArea { get; }

    /// <summary>
    /// Ray-surface intersection in lathe-local space. When a hit occurs in
    /// <c>(tMin, tMax]</c>, writes the parametric distance, outward surface
    /// normal, and segment-local arc-length parameter <c>v ∈ [0, 1]</c>
    /// (used by the caller to compute the UV V coordinate).
    /// </summary>
    bool Hit(Ray ray, float tMin, float tMax,
             out float tHit, out Vector3 outwardNormal, out float vSegment);

    /// <summary>
    /// Uniform-area sampling of the lateral surface. Returns a local-space
    /// point, its outward normal, and the azimuthal / meridional parameters
    /// the caller needs to fill UV and compute the world-space area.
    /// </summary>
    (Vector3 Point, Vector3 Normal, float VSegment, float Theta) Sample(float xiV, float xiTheta);
}

// ═════════════════════════════════════════════════════════════════════════════
// Linear frustum segment — analytic quadratic (same math as Cone)
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Linear profile segment revolved into a frustum. Intersection reduces to a
/// quadratic in <c>t</c> identical to <see cref="Cone"/>, but clipped to the
/// segment's Y range instead of the cone's full height. Sharing the same
/// analytic formulation ensures the implicit surface is exact at
/// floating-point precision — no Sturm iteration needed.
/// </summary>
internal sealed class FrustumSegment : ILatheSegment
{
    private readonly float _rBase;
    private readonly float _rTop;
    private readonly float _yBase;
    private readonly float _yTop;
    private readonly float _slope;     // dr/dy
    private readonly float _arcLength; // slant height of the meridian

    public FrustumSegment(float rBase, float yBase, float rTop, float yTop)
    {
        _rBase = rBase;
        _rTop  = rTop;
        _yBase = yBase;
        _yTop  = yTop;
        float dy = yTop - yBase;
        _slope  = dy > 1e-12f ? (rTop - rBase) / dy : 0f;
        _arcLength = MathF.Sqrt(dy * dy + (rTop - rBase) * (rTop - rBase));

        float rMax = MathF.Max(rBase, rTop);
        LocalBounds = new AABB(
            new Vector3(-rMax, yBase, -rMax),
            new Vector3( rMax, yTop,   rMax));

        // Lateral area of a frustum: π (R + r) × slant.
        LateralArea = MathF.PI * (rBase + rTop) * _arcLength;
    }

    public AABB LocalBounds { get; }
    public float YMin => _yBase;
    public float YMax => _yTop;
    public float ArcLength => _arcLength;
    public float LateralArea { get; }

    public bool Hit(Ray ray, float tMin, float tMax,
                    out float tHit, out Vector3 outwardNormal, out float vSegment)
    {
        tHit = 0f;
        outwardNormal = default;
        vSegment = 0f;

        float dx = ray.Direction.X;
        float dy = ray.Direction.Y;
        float dz = ray.Direction.Z;
        float ox = ray.Origin.X;
        float oy = ray.Origin.Y - _yBase;
        float oz = ray.Origin.Z;

        float rAtO = _rBase + _slope * oy;

        // Coefficients of the quadratic (dx²+dz² − s²dy²) t² + … (derivation in Cone.cs).
        float a     = dx * dx + dz * dz - _slope * _slope * dy * dy;
        float halfB = ox * dx + oz * dz - _slope * dy * rAtO;
        float c     = ox * ox + oz * oz - rAtO * rAtO;

        if (MathF.Abs(a) < 1e-10f) return false;
        float disc = halfB * halfB - a * c;
        if (disc < 0f) return false;

        float sqrtD = MathF.Sqrt(disc);
        bool found = false;
        float tBest = tMax;

        for (int i = 0; i < 2; i++)
        {
            float t = (-halfB + (i == 0 ? -sqrtD : sqrtD)) / a;
            if (t <= tMin || t >= tBest) continue;

            float y = ray.Origin.Y + t * dy;
            if (y < _yBase || y > _yTop) continue;

            tBest = t;
            tHit  = t;
            float px = ray.Origin.X + t * dx;
            float pz = ray.Origin.Z + t * dz;
            float rAtY = _rBase + _slope * (y - _yBase);

            // Gradient of x² + z² − r(y)² yields (2x, −2 r(y) s, 2z).
            outwardNormal = Vector3.Normalize(new Vector3(px, -rAtY * _slope, pz));

            float dyTot = _yTop - _yBase;
            vSegment = dyTot > 1e-12f ? (y - _yBase) / dyTot : 0f;
            found = true;
        }

        return found;
    }

    public (Vector3 Point, Vector3 Normal, float VSegment, float Theta) Sample(float xiV, float xiTheta)
    {
        // Area-weighted inverse CDF along v: P(v) ∝ r(v) — same derivation as Cone.
        float v;
        if (MathF.Abs(_rTop - _rBase) < 1e-6f)
        {
            v = xiV;
        }
        else
        {
            float r2B = _rBase * _rBase;
            float r2T = _rTop  * _rTop;
            v = (MathF.Sqrt(r2B + xiV * (r2T - r2B)) - _rBase) / (_rTop - _rBase);
        }

        float theta = xiTheta * 2f * MathF.PI;
        float r = _rBase + (_rTop - _rBase) * v;
        float y = _yBase + v * (_yTop - _yBase);

        float cosT = MathF.Cos(theta);
        float sinT = MathF.Sin(theta);
        var point  = new Vector3(r * cosT, y, r * sinT);
        var normal = Vector3.Normalize(new Vector3(cosT, -r * _slope, sinT));
        return (point, normal, v, theta);
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// Cubic Bezier spline segment — degree-6 implicit solved by Sturm
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Cubic Bezier profile segment revolved around the Y axis. Ray-surface
/// intersection reduces to a polynomial of degree 6 in the profile parameter
/// <c>u ∈ [0, 1]</c>, solved numerically with <see cref="SturmSolver"/>.
///
/// Derivation (main path, valid when <c>|dy| &gt; ε</c>):
///   From <c>oy + t·dy = Y(u)</c> we get <c>t(u) = (Y(u) − oy) / dy</c>.
///   Substituting into the lathe implicit <c>x² + z² = R(u)²</c> and clearing
///   <c>dy</c> yields
///       F(u) = (ox·dy + dx·(Y(u)−oy))² + (oz·dy + dz·(Y(u)−oy))² − dy²·R(u)² = 0
///   Both <c>Y</c> and <c>R</c> are cubics in <c>u</c>, so <c>F</c> is a
///   polynomial of degree 6.
///
/// The horizontal-ray fallback (<c>|dy| ≤ ε</c>) reduces to a cubic in u
/// (<c>Y(u) = y_ray</c>) plus a quadratic in t per real root — both handled
/// analytically by <see cref="QuarticSolver"/>.
/// </summary>
internal sealed class SplineSegment : ILatheSegment
{
    // Bezier control points (r and y separately, in float for local math).
    private readonly float _br0, _br1, _br2, _br3;
    private readonly float _by0, _by1, _by2, _by3;

    // Power-basis coefficients (doubles for Sturm robustness): Y(u) = Σ y_k u^k.
    private readonly double _y0, _y1, _y2, _y3;
    private readonly double _r0, _r1, _r2, _r3;

    private readonly float _arcLength;
    private readonly float _lateralArea;

    private const int TableSize = 64;
    // Cumulative meridian arc length at u = i / TableSize.
    private readonly float[] _arcTable = new float[TableSize + 1];
    // Cumulative lateral area up to u, used as inverse CDF for NEE sampling.
    private readonly float[] _areaTable = new float[TableSize + 1];

    public SplineSegment(Vector2 b0, Vector2 b1, Vector2 b2, Vector2 b3)
    {
        _br0 = b0.X; _br1 = b1.X; _br2 = b2.X; _br3 = b3.X;
        _by0 = b0.Y; _by1 = b1.Y; _by2 = b2.Y; _by3 = b3.Y;

        // Bezier → power basis:
        //   c_0 = b0
        //   c_1 = −3 b0 + 3 b1
        //   c_2 =  3 b0 − 6 b1 + 3 b2
        //   c_3 = − b0 + 3 b1 − 3 b2 +  b3
        _y0 = _by0;
        _y1 = -3.0 * _by0 + 3.0 * _by1;
        _y2 =  3.0 * _by0 - 6.0 * _by1 + 3.0 * _by2;
        _y3 =       -_by0 + 3.0 * _by1 - 3.0 * _by2 + _by3;

        _r0 = _br0;
        _r1 = -3.0 * _br0 + 3.0 * _br1;
        _r2 =  3.0 * _br0 - 6.0 * _br1 + 3.0 * _br2;
        _r3 =       -_br0 + 3.0 * _br1 - 3.0 * _br2 + _br3;

        // Tight AABB via Y'(u) = 0, R'(u) = 0 (quadratics in u).
        (float yMin, float yMax) = ExtremaOnUnit((float)_y0, (float)_y1, (float)_y2, (float)_y3);
        (float rMin, float rMax) = ExtremaOnUnit((float)_r0, (float)_r1, (float)_r2, (float)_r3);
        rMax = MathF.Max(rMax, 0f);
        YMin = yMin;
        YMax = yMax;
        LocalBounds = new AABB(
            new Vector3(-rMax, yMin, -rMax),
            new Vector3( rMax, yMax,  rMax));

        BuildArcLengthAndAreaTables(out _arcLength, out _lateralArea);
    }

    public AABB LocalBounds { get; }
    public float YMin { get; }
    public float YMax { get; }
    public float ArcLength => _arcLength;
    public float LateralArea => _lateralArea;

    // ── Polynomial evaluation helpers (power basis) ──────────────────────────

    private double Y(double u) => _y0 + u * (_y1 + u * (_y2 + u * _y3));
    private double R(double u) => _r0 + u * (_r1 + u * (_r2 + u * _r3));
    private double Yp(double u) => _y1 + u * (2.0 * _y2 + u * (3.0 * _y3));
    private double Rp(double u) => _r1 + u * (2.0 * _r2 + u * (3.0 * _r3));

    // ── Hit (main path + horizontal-ray fallback) ────────────────────────────

    public bool Hit(Ray ray, float tMin, float tMax,
                    out float tHit, out Vector3 outwardNormal, out float vSegment)
    {
        tHit = 0f;
        outwardNormal = default;
        vSegment = 0f;

        double ox = ray.Origin.X;
        double oy = ray.Origin.Y;
        double oz = ray.Origin.Z;
        double dx = ray.Direction.X;
        double dy = ray.Direction.Y;
        double dz = ray.Direction.Z;

        const double DyEps = 1e-7;
        bool hit;
        if (System.Math.Abs(dy) > DyEps)
        {
            hit = HitMainPath(ray, ox, oy, oz, dx, dy, dz, tMin, tMax,
                              out tHit, out outwardNormal, out vSegment);
        }
        else
        {
            hit = HitHorizontalRay(ray, ox, oy, oz, dx, dz, tMin, tMax,
                                   out tHit, out outwardNormal, out vSegment);
        }
        return hit;
    }

    /// <summary>
    /// Main intersection path: build the degree-6 polynomial F(u), isolate
    /// real roots in (0, 1] with Sturm, then pick the one whose corresponding
    /// t lies in (tMin, tMax] and is smallest.
    /// </summary>
    private bool HitMainPath(
        Ray ray, double ox, double oy, double oz, double dx, double dy, double dz,
        float tMin, float tMax, out float tHit, out Vector3 outwardNormal, out float vSegment)
    {
        tHit = 0f;
        outwardNormal = default;
        vSegment = 0f;

        // Power-basis coefficients of U(u) = ox·dy + dx·(Y(u) − oy):
        //   U = (ox·dy − dx·oy) + dx·y_0 + dx·y_1·u + dx·y_2·u² + dx·y_3·u³
        double aU = ox * dy - dx * oy;
        Span<double> u = stackalloc double[4];
        u[0] = aU + dx * _y0;
        u[1] =      dx * _y1;
        u[2] =      dx * _y2;
        u[3] =      dx * _y3;

        double aV = oz * dy - dz * oy;
        Span<double> v = stackalloc double[4];
        v[0] = aV + dz * _y0;
        v[1] =      dz * _y1;
        v[2] =      dz * _y2;
        v[3] =      dz * _y3;

        // dy · R(u), cubic in u.
        Span<double> dyR = stackalloc double[4];
        dyR[0] = dy * _r0;
        dyR[1] = dy * _r1;
        dyR[2] = dy * _r2;
        dyR[3] = dy * _r3;

        // F(u) = U(u)² + V(u)² − (dy·R(u))²
        Span<double> coeffs = stackalloc double[7];
        for (int i = 0; i < 7; i++) coeffs[i] = 0.0;
        AccumulateSquare(coeffs, u, +1.0);
        AccumulateSquare(coeffs, v, +1.0);
        AccumulateSquare(coeffs, dyR, -1.0);

        Span<double> roots = stackalloc double[6];
        int count = SturmSolver.FindRoots(coeffs, 0.0, 1.0, roots);
        if (count == 0) return false;

        float invDy = (float)(1.0 / dy);
        float oyF = (float)oy;
        bool found = false;
        float tBest = tMax;

        for (int i = 0; i < count; i++)
        {
            double uParam = roots[i];
            float y = (float)Y(uParam);
            float t = (y - oyF) * invDy;
            if (t <= tMin || t >= tBest) continue;

            // Residual guard: reject phantom roots whose implicit value is far
            // from zero at the candidate point. This rarely fires but catches
            // ill-conditioned rays (tangent or grazing) before they corrupt
            // the normal.
            float px = (float)(ox + t * dx);
            float pz = (float)(oz + t * dz);
            float rAtU = (float)R(uParam);
            float residual = px * px + pz * pz - rAtU * rAtU;
            float tolScale = 1f + rAtU * rAtU + px * px + pz * pz;
            if (MathF.Abs(residual) > 1e-4f * tolScale) continue;

            tHit = t;
            tBest = t;
            vSegment = (float)uParam;

            // Meridian tangent (R'(u), Y'(u)); outward normal rotates it 90°
            // clockwise in the meridian plane (positive-r side), then revolves
            // around Y.
            double rp = Rp(uParam);
            double yp = Yp(uParam);
            float invR = rAtU > 1e-12f ? 1f / rAtU : 0f;
            float cosT = px * invR;
            float sinT = pz * invR;
            float nR = (float)yp;
            float nY = (float)(-rp);
            outwardNormal = Vector3.Normalize(new Vector3(nR * cosT, nY, nR * sinT));
            found = true;
        }

        return found;
    }

    /// <summary>
    /// Horizontal-ray fallback: when |dy| is below the precision threshold, the
    /// main formulation becomes singular (division by dy). We solve the cubic
    /// <c>Y(u) = y_ray</c> analytically, then a quadratic in t per real u-root.
    /// </summary>
    private bool HitHorizontalRay(
        Ray ray, double ox, double oy, double oz, double dx, double dz,
        float tMin, float tMax, out float tHit, out Vector3 outwardNormal, out float vSegment)
    {
        tHit = 0f;
        outwardNormal = default;
        vSegment = 0f;

        // Y(u) − oy = 0 → cubic in u solved via QuarticSolver.SolveCubic.
        // Coefficients in QuarticSolver are leading-first (a t³ + b t² + c t + d).
        Span<double> uRoots = stackalloc double[3];
        int nU = QuarticSolver.SolveCubic(_y3, _y2, _y1, _y0 - oy, uRoots, 0.0, 1.0);
        if (nU == 0) return false;

        bool found = false;
        float tBest = tMax;

        for (int i = 0; i < nU; i++)
        {
            double uParam = uRoots[i];
            double rAtU = R(uParam);
            if (rAtU < 0.0) continue;

            // (ox + t dx)² + (oz + t dz)² = rAtU²
            double qa = dx * dx + dz * dz;
            if (qa < 1e-18) continue;
            double qb = 2.0 * (ox * dx + oz * dz);
            double qc = ox * ox + oz * oz - rAtU * rAtU;
            double disc = qb * qb - 4.0 * qa * qc;
            if (disc < 0.0) continue;
            double sqrtD = System.Math.Sqrt(disc);

            for (int s = 0; s < 2; s++)
            {
                double t = (-qb + (s == 0 ? -sqrtD : sqrtD)) / (2.0 * qa);
                if (t <= tMin || t >= tBest) continue;

                float px = (float)(ox + t * dx);
                float pz = (float)(oz + t * dz);
                tHit = (float)t;
                tBest = (float)t;
                vSegment = (float)uParam;

                double rp = Rp(uParam);
                double yp = Yp(uParam);
                float invR = rAtU > 1e-12 ? (float)(1.0 / rAtU) : 0f;
                float cosT = px * invR;
                float sinT = pz * invR;
                float nR = (float)yp;
                float nY = (float)(-rp);
                outwardNormal = Vector3.Normalize(new Vector3(nR * cosT, nY, nR * sinT));
                found = true;
            }
        }

        return found;
    }

    // ── Sampling (NEE) ────────────────────────────────────────────────────────

    public (Vector3 Point, Vector3 Normal, float VSegment, float Theta) Sample(float xiV, float xiTheta)
    {
        // Inverse-CDF on the precomputed area table: picks u ∈ [0, 1] with
        // density proportional to 2π R(u) · |C'(u)| — the surface-of-revolution
        // area element, so samples are uniform per unit world area.
        float target = xiV * _lateralArea;
        int lo = 0, hi = TableSize;
        while (lo + 1 < hi)
        {
            int mid = (lo + hi) >> 1;
            if (_areaTable[mid] < target) lo = mid; else hi = mid;
        }
        float denom = _areaTable[hi] - _areaTable[lo];
        float frac = denom > 1e-9f ? (target - _areaTable[lo]) / denom : 0.5f;
        double u = (lo + frac) / (double)TableSize;

        float rU = (float)R(u);
        float yU = (float)Y(u);
        float theta = xiTheta * 2f * MathF.PI;
        float cosT = MathF.Cos(theta);
        float sinT = MathF.Sin(theta);

        var point = new Vector3(rU * cosT, yU, rU * sinT);
        double rp = Rp(u);
        double yp = Yp(u);
        var normal = Vector3.Normalize(new Vector3((float)yp * cosT, (float)(-rp), (float)yp * sinT));
        return (point, normal, (float)u, theta);
    }

    // ── Setup helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Accumulates (sign × P²) into <paramref name="coeffs"/> where P is a
    /// cubic polynomial given in power-basis <paramref name="p"/>. The result
    /// is a polynomial of degree 6 stored constant-first.
    /// </summary>
    private static void AccumulateSquare(Span<double> coeffs, Span<double> p, double sign)
    {
        for (int i = 0; i <= 3; i++)
            for (int j = 0; j <= 3; j++)
                coeffs[i + j] += sign * p[i] * p[j];
    }

    /// <summary>
    /// Finds min/max of a cubic p(u) = c0 + c1 u + c2 u² + c3 u³ on [0, 1]
    /// by locating the critical points of its quadratic derivative and
    /// comparing them with the endpoints.
    /// </summary>
    private static (float Min, float Max) ExtremaOnUnit(float c0, float c1, float c2, float c3)
    {
        float Eval(float u) => c0 + u * (c1 + u * (c2 + u * c3));

        float min = MathF.Min(Eval(0f), Eval(1f));
        float max = MathF.Max(Eval(0f), Eval(1f));

        // p'(u) = c1 + 2 c2 u + 3 c3 u²  → quadratic.
        float a = 3f * c3;
        float b = 2f * c2;
        float cc = c1;

        if (MathF.Abs(a) < 1e-10f)
        {
            if (MathF.Abs(b) > 1e-10f)
            {
                float u = -cc / b;
                if (u > 0f && u < 1f)
                {
                    float v = Eval(u);
                    if (v < min) min = v;
                    if (v > max) max = v;
                }
            }
        }
        else
        {
            float disc = b * b - 4f * a * cc;
            if (disc >= 0f)
            {
                float sqrtD = MathF.Sqrt(disc);
                for (int i = 0; i < 2; i++)
                {
                    float u = (-b + (i == 0 ? -sqrtD : sqrtD)) / (2f * a);
                    if (u > 0f && u < 1f)
                    {
                        float v = Eval(u);
                        if (v < min) min = v;
                        if (v > max) max = v;
                    }
                }
            }
        }

        return (min, max);
    }

    /// <summary>
    /// Tabulates cumulative meridian arc length and lateral area along the
    /// segment at 65 uniform u-samples. Within each sub-interval we use 4-point
    /// Gauss-Legendre quadrature, which integrates degree-7 polynomials
    /// exactly — sufficient headroom for <c>|C'(u)|</c> (square root of a
    /// degree-4 polynomial) to land well under <c>1e-6</c> relative error.
    /// </summary>
    private void BuildArcLengthAndAreaTables(out float arcLength, out float lateralArea)
    {
        // Gauss-Legendre nodes/weights on [-1, 1] for n = 4. We map them onto
        // each sub-interval [u_i, u_{i+1}] at sample time.
        ReadOnlySpan<double> gx = stackalloc double[]
        {
            -0.861136311594052575,
            -0.339981043584856265,
             0.339981043584856265,
             0.861136311594052575
        };
        ReadOnlySpan<double> gw = stackalloc double[]
        {
            0.347854845137453857,
            0.652145154862546143,
            0.652145154862546143,
            0.347854845137453857
        };

        double arc = 0.0;
        double area = 0.0;
        _arcTable[0] = 0f;
        _areaTable[0] = 0f;

        double h = 1.0 / TableSize;
        for (int i = 0; i < TableSize; i++)
        {
            double uLo = i * h;
            double uHi = uLo + h;
            double half = 0.5 * (uHi - uLo);
            double mid  = 0.5 * (uHi + uLo);

            double dArc = 0.0;
            double dArea = 0.0;
            for (int k = 0; k < 4; k++)
            {
                double u = mid + half * gx[k];
                double rp = Rp(u);
                double yp = Yp(u);
                double speed = System.Math.Sqrt(rp * rp + yp * yp);
                double rU = System.Math.Max(0.0, R(u));
                dArc  += gw[k] * speed;
                dArea += gw[k] * speed * rU;
            }
            dArc  *= half;
            dArea *= half * 2.0 * System.Math.PI;

            arc  += dArc;
            area += dArea;
            _arcTable[i + 1]  = (float)arc;
            _areaTable[i + 1] = (float)area;
        }

        arcLength   = (float)arc;
        lateralArea = (float)area;
    }
}
