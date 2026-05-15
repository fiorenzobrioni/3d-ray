using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Camera;

public class Camera
{
    private readonly Vector3 _origin;
    private readonly Vector3 _lowerLeftCorner;
    private readonly Vector3 _horizontal;
    private readonly Vector3 _vertical;
    private readonly Vector3 _u, _v, _w;
    private readonly float _lensRadius;

    public Camera(
        Vector3 lookFrom,
        Vector3 lookAt,
        Vector3 vUp,
        float vFovDeg,
        float aspectRatio,
        float aperture,
        float focusDist)
    {
        float theta = MathUtils.DegreesToRadians(vFovDeg);
        float h = MathF.Tan(theta / 2f);
        float viewportHeight = 2f * h;
        float viewportWidth = aspectRatio * viewportHeight;

        _w = Vector3.Normalize(lookFrom - lookAt);

        // When the view direction is (anti-)parallel to vUp, the cross
        // product _w × vUp is zero and the camera basis degenerates (all NaN).
        // This happens when looking straight down or straight up with the default
        // vUp = (0,1,0). Detect the singularity and fall back to an alternative
        // up vector perpendicular to _w.
        if (Vector3.Cross(_w, vUp).LengthSquared() < 1e-8f)
            vUp = MathF.Abs(_w.X) < 0.9f ? Vector3.UnitX : Vector3.UnitZ;

        _u = Vector3.Normalize(Vector3.Cross(_w, vUp));
        _v = Vector3.Cross(_u, _w);

        _origin = lookFrom;
        _horizontal = focusDist * viewportWidth * _u;
        _vertical = focusDist * viewportHeight * _v;
        _lowerLeftCorner = _origin - _horizontal / 2f - _vertical / 2f - focusDist * _w;
        _lensRadius = aperture / 2f;
    }

    public Ray GetRay(float s, float t)
    {
        // Pinhole fast path. Sampling the disk for a zero-radius lens
        // would burn two Sobol dimensions on a value that gets multiplied
        // by zero — wasting the structurally best Sobol(2D) dims for an
        // unused lens offset and shifting every downstream BSDF/light
        // decision onto worse-stratified hash dimensions.
        if (_lensRadius == 0f)
        {
            return new Ray(
                _origin,
                _lowerLeftCorner + s * _horizontal + t * _vertical - _origin);
        }

        Vector3 rd = _lensRadius * MathUtils.RandomInUnitDisk();
        Vector3 offset = _u * rd.X + _v * rd.Y;

        return new Ray(
            _origin + offset,
            _lowerLeftCorner + s * _horizontal + t * _vertical - _origin - offset);
    }

    /// <summary>
    /// Builds a primary ray carrying ray differentials through the +x and +y
    /// neighbour pixels (PBRT §6.2.3). <paramref name="dsdx"/> and
    /// <paramref name="dtdy"/> are the screen-space deltas for one pixel
    /// (typically <c>1/width</c> and <c>1/height</c>). Used by the renderer
    /// to drive analytic anti-aliasing in filtered textures.
    ///
    /// <para>
    /// For a pinhole camera the differential origin equals the primary origin
    /// and only the direction varies — that's the "thin-lens at lens radius=0"
    /// limit. For a finite-aperture (depth-of-field) camera the auxiliary
    /// rays share the same lens-sample offset as the primary so the
    /// differential captures pixel-area, not aperture-area; texture filtering
    /// then stays stable as DoF blur grows (the latter is handled by spp).
    /// </para>
    /// </summary>
    public Ray GetRayWithDifferentials(float s, float t, float dsdx, float dtdy)
    {
        Vector3 pixelTarget = _lowerLeftCorner + s * _horizontal + t * _vertical;
        Vector3 pixelTargetX = _lowerLeftCorner + (s + dsdx) * _horizontal + t * _vertical;
        Vector3 pixelTargetY = _lowerLeftCorner + s * _horizontal + (t - dtdy) * _vertical;

        if (_lensRadius == 0f)
        {
            var dir  = pixelTarget  - _origin;
            var dirX = pixelTargetX - _origin;
            var dirY = pixelTargetY - _origin;
            var diff = new RayDifferential(_origin, dirX, _origin, dirY);
            return new Ray(_origin, dir, diff);
        }

        Vector3 rd = _lensRadius * MathUtils.RandomInUnitDisk();
        Vector3 offset = _u * rd.X + _v * rd.Y;
        Vector3 lensOrigin = _origin + offset;

        var dirMain  = pixelTarget  - lensOrigin;
        var dirXa = pixelTargetX - lensOrigin;
        var dirYa = pixelTargetY - lensOrigin;
        var diffA = new RayDifferential(lensOrigin, dirXa, lensOrigin, dirYa);
        return new Ray(lensOrigin, dirMain, diffA);
    }
}
