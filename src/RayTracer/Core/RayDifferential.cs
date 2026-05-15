using System.Numerics;

namespace RayTracer.Core;

/// <summary>
/// Auxiliary "differential rays" attached to a primary camera ray to enable
/// analytic anti-aliasing in textures via ray differentials (PBRT §10.1).
///
/// <para>
/// The differential ray pair (Rx, Ry) is what would be cast through the pixel
/// centres immediately to the right (+x screen direction) and immediately below
/// (+y screen direction) of the primary ray's pixel. After a surface hit, the
/// auxiliary rays are projected onto the tangent plane at the hit point to
/// derive the surface footprint <c>(∂P/∂x, ∂P/∂y)</c> — the area of the texel
/// that this single shading sample is meant to represent.
/// </para>
///
/// <para>
/// Differentials propagate through <see cref="Geometry.Transform"/> by the same
/// inverse matrix that transforms the primary ray (the Jacobian of an affine
/// transform is the matrix itself) and are emitted by
/// <see cref="Camera.Camera.GetRay"/> when ray-differential tracking is enabled.
/// They are intentionally absent (<see cref="Ray.HasDifferentials"/> = false)
/// on shadow rays, BSDF bounces and NEE evaluation — anti-aliasing past the
/// first surface hit is handled by stochastic sampling, not by re-deriving the
/// footprint.
/// </para>
/// </summary>
public readonly struct RayDifferential
{
    public Vector3 OriginX { get; }
    public Vector3 DirectionX { get; }
    public Vector3 OriginY { get; }
    public Vector3 DirectionY { get; }

    public RayDifferential(Vector3 originX, Vector3 directionX, Vector3 originY, Vector3 directionY)
    {
        OriginX = originX;
        DirectionX = directionX;
        OriginY = originY;
        DirectionY = directionY;
    }
}
