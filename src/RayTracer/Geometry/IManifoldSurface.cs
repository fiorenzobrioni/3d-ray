using System.Numerics;

namespace RayTracer.Geometry;

/// <summary>
/// One evaluated point on a specular caustic-caster surface: world-space
/// position and outward (away-from-interior) unit normal. The
/// <see cref="Rendering.ManifoldWalker"/> consumes only these two quantities —
/// the Newton-Raphson manifold walk computes its Jacobian by finite differences
/// in (u, v), so no analytic surface derivatives are required here.
/// </summary>
public readonly struct ManifoldPoint
{
    public readonly Vector3 P;
    public readonly Vector3 N;
    public ManifoldPoint(Vector3 p, Vector3 n) { P = p; N = n; }
}

/// <summary>
/// A surface that can be evaluated parametrically at an arbitrary (u, v) — the
/// inverse of the usual ray → (u, v) intersection. Implemented by smooth
/// specular primitives that can act as MNEE caustic casters (currently
/// <see cref="Sphere"/>, propagated through <see cref="Transform"/>). The
/// parameterisation MUST match the one the primitive writes into
/// <c>HitRecord.U/V</c> during <c>Hit</c>, so the manifold walk can seed itself
/// from the (u, v) of a straight-line intersection and converge to a
/// neighbouring specular vertex.
/// </summary>
public interface IManifoldSurface
{
    /// <summary>
    /// Evaluates the surface at parameter (u, v). Returns false when (u, v) is
    /// outside the valid domain or the evaluation is degenerate (e.g. a pole),
    /// in which case the manifold walk abandons this attempt without bias.
    /// World space for un-transformed primitives; <see cref="Transform"/>
    /// composes the mapping so the result is always world space.
    /// </summary>
    bool EvaluateManifold(float u, float v, out ManifoldPoint pt);
}
