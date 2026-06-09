using System.Numerics;
using RayTracer.Core;
using RayTracer.Materials;

namespace RayTracer.Geometry;

/// <summary>
/// Torus (donut / ring) primitive centered at the origin, lying in the XZ plane.
/// The hole axis is Y. Use the Transform wrapper to position, rotate, and scale.
///
/// <b>Parameters:</b>
///   <c>MajorRadius</c> (R) — distance from the torus center to the center of the tube.
///   <c>MinorRadius</c> (r) — radius of the tube itself.
///   When R > r the torus has a visible hole (ring torus).
///   When R = r the hole vanishes (horn torus).
///   When R &lt; r the torus self-intersects (spindle torus).
///
/// <b>Implicit equation:</b>
///   (x² + y² + z² + R² - r²)² = 4R²(x² + z²)
///
/// <b>Ray intersection:</b>
///   Substituting the ray P(t) = O + tD into the implicit equation yields a
///   quartic (degree 4) polynomial in t, solved analytically via Ferrari's method
///   (<see cref="QuarticSolver"/>). A torus can produce 0, 2, or 4 intersections
///   per ray.
///
/// <b>UV mapping (toroidal coordinates):</b>
///   U = φ / 2π  — azimuthal angle around the Y axis (major circle)
///   V = θ / 2π  — poloidal angle around the tube cross-section
///   Both mapped from [0, 1]. This gives a natural "latitude/longitude" on the
///   torus surface, ideal for texturing rings, tires, and pipes.
///
/// <b>TBN basis:</b>
///   Tangent  = ∂P/∂φ (direction of increasing U, around the major circle)
///   Bitangent = ∂P/∂θ (direction of increasing V, around the tube)
///   Normal = outward surface normal, perpendicular to both.
///
/// <b>CSG compatibility:</b>
///   The torus is non-convex and can produce up to 4 surface intersections per
///   ray. The CSG engine's CollectAllHits (MaxHitsPerChild = 16) handles this
///   correctly — no special cases needed.
///
/// Implements ISamplable for use as an emissive area light with NEE.
/// Surface area = 4π²Rr.
/// </summary>
public class Torus : IHittable, ISamplable
{
    /// <summary>Distance from the torus center to the center of the tube.</summary>
    public float MajorRadius { get; }

    /// <summary>Radius of the tube.</summary>
    public float MinorRadius { get; }

    public IMaterial Material { get; }

    // Precomputed squares for the intersection math
    private readonly double _R2;
    private readonly double _r2;
    private readonly double _R;
    private readonly double _r;

    public Torus(float majorRadius, float minorRadius, IMaterial material)
    {
        MajorRadius = majorRadius;
        MinorRadius = minorRadius;
        Material = material;
        _R = majorRadius;
        _r = minorRadius;
        _R2 = (double)majorRadius * majorRadius;
        _r2 = (double)minorRadius * minorRadius;
    }

// ═══════════════════════════════════════════════════════════════════════════
// PATCHED Torus.Hit() — Replace the existing Hit() method in Torus.cs
// ═══════════════════════════════════════════════════════════════════════════
//
// FIX (BUG-12): Normalize ray direction before building quartic coefficients.
//
// ROOT CAUSE:
//   Camera.GetRay() returns rays with |Direction| ≈ focal_dist (NOT unit).
//   Transform.Hit() further scales Direction by the inverse transform.
//   The quartic coefficient c4 = (D·D)² scales as |D|⁴.
//   With focal_dist = 8: c4 ≈ 4096, making the monic normalization (÷c4)
//   catastrophically amplify rounding errors in the smaller coefficients.
//
// SYMPTOMS:
//   1. "Ghost contour": the back surface of the torus bleeds through
//      objects in front (cone, cylinder) because quartic roots have
//      enough error to flip the t-ordering at grazing angles.
//   2. Geometric deformations when focal_dist > 1: larger focal_dist
//      → larger |D| → worse quartic conditioning → visible warping.
//
// FIX:
//   Normalize D to unit length inside Hit(), solve with dd=1 (c4=1),
//   then scale the roots back to the original ray parameterization.
//   The implicit-surface residual check catches any remaining
//   phantom roots from edge-case solver instability.
//
// IMPACT:
//   - Torus only (surgical fix, no changes to Camera or Transform)
//   - ~5% slower per torus intersection (one sqrt + division)
//   - Removes the |D|⁴ ill-conditioning of the leading coefficient. Very large
//     or far-from-origin tori can still lose precision in the K = O·O + R² − r²
//     term (use a Transform to keep the object-space torus near unit scale); the
//     relative residual check below rejects the phantom roots that survive.
// ═══════════════════════════════════════════════════════════════════════════

    public bool Hit(in Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        // ═══════════════════════════════════════════════════════════════════
        // BUG-12 FIX: Normalize direction for quartic stability
        //
        // The raw ray direction can have |D| >> 1 (from Camera focal_dist
        // and/or Transform scaling). The quartic c4 = |D|⁴ makes Ferrari's
        // method lose significant digits. Normalizing ensures c4 = 1.
        //
        // Parameter mapping: if D' = D/|D|, then P(t) = O + t·D and
        // P(t') = O + t'·D' give the same point when t' = t·|D|.
        // So we solve in normalized space [tMin·|D|, tMax·|D|] and
        // convert roots back: t = t' / |D|.
        // ═══════════════════════════════════════════════════════════════════

        double ox = ray.Origin.X, oy = ray.Origin.Y, oz = ray.Origin.Z;
        double rawDx = ray.Direction.X, rawDy = ray.Direction.Y, rawDz = ray.Direction.Z;

        double dirLenSq = rawDx * rawDx + rawDy * rawDy + rawDz * rawDz;
        if (dirLenSq < 1e-30)
            return false;

        double dirLen = Math.Sqrt(dirLenSq);
        double invDirLen = 1.0 / dirLen;

        // Normalized direction components
        double dx = rawDx * invDirLen;
        double dy = rawDy * invDirLen;
        double dz = rawDz * invDirLen;

        // Scale t-range to normalized-direction space
        double normTMin = tMin * dirLen;
        double normTMax = tMax * dirLen;

        // ═══════════════════════════════════════════════════════════════════
        // Build the quartic coefficients (now with dd = 1.0)
        //
        // The torus implicit equation:
        //   (x² + y² + z² + R² − r²)² = 4R²(x² + z²)
        //
        // Substituting P(t') = O + t'·D̂ gives a quartic in t':
        //   c4·t'⁴ + c3·t'³ + c2·t'² + c1·t' + c0 = 0
        //
        // With |D̂| = 1: dd = 1, so c4 = 1 — perfectly conditioned.
        // ═══════════════════════════════════════════════════════════════════

        // dd = 1.0 (normalized)
        double od = ox * dx + oy * dy + oz * dz;
        double oo = ox * ox + oy * oy + oz * oz;

        // XZ-plane components only (for the 4R² term)
        double dxz2 = dx * dx + dz * dz;
        double odxz = ox * dx + oz * dz;
        double oxz2 = ox * ox + oz * oz;

        // K = O·O + R² − r²
        double K = oo + _R2 - _r2;

        // Quartic coefficients — c4 = dd² = 1.0
        double c4 = 1.0;
        double c3 = 4.0 * od;
        double c2 = 4.0 * od * od + 2.0 * K - 4.0 * _R2 * dxz2;
        double c1 = 4.0 * od * K - 8.0 * _R2 * odxz;
        double c0 = K * K - 4.0 * _R2 * oxz2;

        // Solve the quartic in normalized-t space
        Span<double> roots = stackalloc double[4];
        int count = QuarticSolver.SolveQuartic(c4, c3, c2, c1, c0, roots, normTMin, normTMax);

        if (count == 0)
            return false;

        // ═══════════════════════════════════════════════════════════════════
        // Root validation + conversion back to original ray parameterization
        //
        // Each root t' (in normalized space) is converted to t = t'/|D|,
        // then the hit point is computed and validated against the implicit
        // equation. This catches phantom roots from solver edge cases
        // (near-tangent rays, double roots at silhouette edges).
        // ═══════════════════════════════════════════════════════════════════

        for (int i = 0; i < count; i++)
        {
            // Convert from normalized-t to original ray parameter
            float t = (float)(roots[i] * invDirLen);
            if (t < tMin || t > tMax)
                continue;

            Vector3 point = ray.At(t);

            // Validate: does this point actually lie on the torus surface?
            double px = point.X, py = point.Y, pz = point.Z;
            double sumSq = px * px + py * py + pz * pz;
            double lhs = sumSq + _R2 - _r2;
            lhs *= lhs;
            double rhs = 4.0 * _R2 * (px * px + pz * pz);
            double residual = Math.Abs(lhs - rhs);

            // RELATIVE residual tolerance for the implicit-surface check.
            // F(P) = (|P|²+R²−r²)² − 4R²(px²+pz²) has units length⁴, so a fixed
            // absolute threshold mis-scales: the old 1e-2·(R+r)⁴ was far too
            // loose for large tori (phantom roots slipped through) and far too
            // tight for sub-unit tori (genuine near-tangent hits were rejected).
            // A relative threshold keyed on the terms' own magnitude
            // ((|P|²+R²+r²) ~ length²) auto-scales with both torus size and the
            // point's distance from the origin (which bounds the float
            // positional error at large t). A genuine hit's residual is
            // ~1e-6 relative; phantom solver roots sit near 1.0 relative, so
            // 1e-4 separates them with a wide margin.
            double refMag = sumSq + _R2 + _r2;
            double tolerance = 1e-4 * refMag * refMag;

            if (residual > tolerance)
                continue; // Phantom root — skip

            FillHitRecord(ref rec, ray, point, t);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Fills the HitRecord with normal, UV, and TBN for a hit point on the torus.
    /// </summary>
    private void FillHitRecord(ref HitRecord rec, Ray ray, Vector3 point, float t)
    {
        rec.T = t;
        rec.Point = point;
        rec.LocalPoint = point;

        float px = point.X, py = point.Y, pz = point.Z;

        // ─────────────────────────────────────────────────────────────────
        // Normal: computed geometrically from the tube center
        //
        // The major circle lies in the XZ plane at distance R from origin.
        // The closest point on the ring to P is:
        //   Q = R · normalize(px, 0, pz)
        // Then normal = normalize(P - Q).
        // ─────────────────────────────────────────────────────────────────
        float dxz = MathF.Sqrt(px * px + pz * pz);
        float invDxz = dxz > 1e-8f ? 1f / dxz : 0f;

        // Direction from origin to P projected onto XZ (unit radial vector)
        float radX = px * invDxz;
        float radZ = pz * invDxz;

        // Tube center for this cross-section
        float qx = MajorRadius * radX;
        float qz = MajorRadius * radZ;

        // Vector from tube center to surface point
        float nx = px - qx;
        float ny = py;
        float nz = pz - qz;
        Vector3 outwardNormal = Vector3.Normalize(new Vector3(nx, ny, nz));

        rec.SetFaceNormal(ray, outwardNormal);

        // ─────────────────────────────────────────────────────────────────
        // UV mapping (toroidal coordinates)
        //
        //   φ = atan2(pz, px)         — azimuthal angle (around Y axis)
        //   θ = atan2(py, dxz - R)    — poloidal angle (around tube)
        //
        //   U = (φ + π) / 2π    ∈ [0, 1]
        //   V = (θ + π) / 2π    ∈ [0, 1]
        // ─────────────────────────────────────────────────────────────────
        float phi = MathF.Atan2(pz, px);
        float theta = MathF.Atan2(py, dxz - MajorRadius);

        rec.U = (phi + MathF.PI) / (2f * MathF.PI);
        rec.V = (theta + MathF.PI) / (2f * MathF.PI);

        // ─────────────────────────────────────────────────────────────────
        // TBN basis
        //
        // Tangent = ∂P/∂φ direction (around the major circle, increasing U)
        //   = (-sin φ, 0, cos φ)   (same as cylinder/sphere azimuthal)
        //
        // Bitangent = ∂P/∂θ direction (around the tube, increasing V)
        //   = (-cos φ · sin θ, cos θ, -sin φ · sin θ)
        // ─────────────────────────────────────────────────────────────────
        float sinPhi = MathF.Sin(phi);
        float cosPhi = MathF.Cos(phi);
        float sinTheta = MathF.Sin(theta);
        float cosTheta = MathF.Cos(theta);

        rec.Tangent = Vector3.Normalize(new Vector3(-sinPhi, 0, cosPhi));
        rec.Bitangent = Vector3.Normalize(new Vector3(
            -cosPhi * sinTheta,
            cosTheta,
            -sinPhi * sinTheta));

        rec.ObjectSeed = Seed;
        rec.Material = Material;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // ISamplable — NEE support for emissive tori
    //
    // Total surface area = 4π²Rr
    //
    // Area element: dA = r(R + r·cosθ) dφ dθ
    // φ is uniform in [0, 2π) — the integrand is symmetric in φ.
    // θ is sampled from the marginal PDF p(θ) = (R + r·cosθ) / (2πR),
    // which is proportional to the area element and eliminates the NEE bias
    // that caused the outer ring (θ≈0) to appear brighter than the inner ring.
    //
    // CDF inversion for θ: F(θ) = (R·θ + r·sinθ) / (2πR) = u
    //   solved numerically via Newton-Raphson (see SampleTheta).
    // ═════════════════════════════════════════════════════════════════════════

    public (Vector3 Point, Vector3 Normal, Vector2 Uv, float Area) Sample()
        => SampleAt(MathUtils.RandomFloat(), MathUtils.RandomFloat());

    /// <inheritdoc/>
    // Surface area of a torus: 4π²Rr  (MajorRadius = R, MinorRadius = r)
    public float SurfaceArea => 4f * MathF.PI * MathF.PI * MajorRadius * MinorRadius;

    /// <summary>
    /// Stratified version: jitters (φ, u_θ) on a sqrtSamples×sqrtSamples grid.
    /// φ is uniform; u_θ is fed into the CDF inversion to produce
    /// area-proportional poloidal angles.
    /// </summary>
    public (Vector3 Point, Vector3 Normal, Vector2 Uv, float Area) SampleStratified(int sampleIndex, int sqrtSamples)
    {
        float inv = 1f / sqrtSamples;
        int su = sampleIndex % sqrtSamples;
        int sv = sampleIndex / sqrtSamples;
        float xi1 = (su + MathUtils.RandomFloat()) * inv;
        float xi2 = (sv + MathUtils.RandomFloat()) * inv;
        return SampleAt(xi1, xi2);
    }

    /// <summary>
    /// Inverts the CDF F(θ) = (R·θ + r·sinθ) / (2πR) = u via Newton-Raphson.
    ///
    /// The derivative f'(θ) = R + r·cosθ is always ≥ R − r. For a ring torus
    /// (R > r) this is strictly positive everywhere, so the function is strictly
    /// monotone and Newton-Raphson converges quadratically from any starting
    /// point. Initial guess θ₀ = 2π·u is exact when r = 0 and close for r ≪ R.
    ///
    /// For a horn torus (R = r), f'(π) = 0 — the inner cusp is a degenerate
    /// double root. The derivative guard prevents division by zero; convergence
    /// is linear there but still reaches 1e-6 in ≤6 steps.
    /// </summary>
    private float SampleTheta(float u)
    {
        float target = u * 2f * MathF.PI * MajorRadius;  // 2πR·u
        float theta  = u * 2f * MathF.PI;                 // exact when r = 0
        for (int i = 0; i < 6; i++)
        {
            float f  = MajorRadius * theta + MinorRadius * MathF.Sin(theta) - target;
            float fp = MajorRadius + MinorRadius * MathF.Cos(theta);
            if (MathF.Abs(fp) < 1e-7f) break;  // horn/spindle cusp — stop
            float step = f / fp;
            theta -= step;
            if (MathF.Abs(step) < 1e-6f) break;
        }
        return theta;
    }

    private (Vector3 Point, Vector3 Normal, Vector2 Uv, float Area) SampleAt(float xi1, float xi2)
    {
        float phi   = xi1 * 2f * MathF.PI;
        float theta = SampleTheta(xi2);   // importance-sampled from p(θ) ∝ R + r·cosθ

        float cosPhi   = MathF.Cos(phi);
        float sinPhi   = MathF.Sin(phi);
        float cosTheta = MathF.Cos(theta);
        float sinTheta = MathF.Sin(theta);

        float rr = MajorRadius + MinorRadius * cosTheta;

        Vector3 point = new(
            rr * cosPhi,
            MinorRadius * sinTheta,
            rr * sinPhi);

        Vector3 normal = new(
            cosTheta * cosPhi,
            sinTheta,
            cosTheta * sinPhi);

        float area = 4f * MathF.PI * MathF.PI * MajorRadius * MinorRadius;

        return (point, normal, new Vector2(xi1, xi2), area);
    }

    public int Seed { get; set; }

    public AABB BoundingBox()
    {
        // The torus extends from -(R+r) to +(R+r) in X and Z,
        // and from -r to +r in Y.
        float extent = MajorRadius + MinorRadius;
        return new AABB(
            new Vector3(-extent, -MinorRadius, -extent),
            new Vector3(extent, MinorRadius, extent));
    }
}
