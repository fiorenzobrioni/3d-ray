using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Camera;

/// <summary>
/// One pose keyframe of an animated camera (camera motion blur): the full
/// look-at specification at a normalized time in [0, 1]. Between keys the
/// pose is interpolated component-wise (lookFrom/lookAt/vUp lerp, fov lerp)
/// and the orthonormal basis is rebuilt per ray — interpolating the *inputs*
/// rather than the basis vectors keeps the frame orthonormal at every time.
/// </summary>
public readonly record struct CameraKey(float Time, Vector3 LookFrom, Vector3 LookAt, Vector3 Vup, float Fov);

public class Camera
{
    /// <summary>Precomputed view frame for one camera pose.</summary>
    private readonly struct Basis
    {
        public readonly Vector3 Origin;
        public readonly Vector3 LowerLeftCorner;
        public readonly Vector3 Horizontal;
        public readonly Vector3 Vertical;
        public readonly Vector3 U, V;

        public Basis(Vector3 lookFrom, Vector3 lookAt, Vector3 vUp,
                     float vFovDeg, float aspectRatio, float focusDist)
        {
            float theta = MathUtils.DegreesToRadians(vFovDeg);
            float h = MathF.Tan(theta / 2f);
            float viewportHeight = 2f * h;
            float viewportWidth = aspectRatio * viewportHeight;

            Vector3 w = Vector3.Normalize(lookFrom - lookAt);

            // When the view direction is (anti-)parallel to vUp, the cross
            // product w × vUp is zero and the camera basis degenerates (all NaN).
            // This happens when looking straight down or straight up with the default
            // vUp = (0,1,0). Detect the singularity and fall back to an alternative
            // up vector perpendicular to w.
            if (Vector3.Cross(w, vUp).LengthSquared() < 1e-8f)
                vUp = MathF.Abs(w.X) < 0.9f ? Vector3.UnitX : Vector3.UnitZ;

            U = Vector3.Normalize(Vector3.Cross(w, vUp));
            V = Vector3.Cross(U, w);

            Origin = lookFrom;
            Horizontal = focusDist * viewportWidth * U;
            Vertical = focusDist * viewportHeight * V;
            LowerLeftCorner = Origin - Horizontal / 2f - Vertical / 2f - focusDist * w;
        }
    }

    private readonly Basis _basis;
    private readonly float _lensRadius;

    // Retained only for the animated path, which rebuilds the basis per ray.
    private readonly CameraKey[]? _motion;
    private readonly float _aspectRatio;
    private readonly float _focusDist;

    /// <summary>True when this camera carries motion keys (camera motion blur).</summary>
    public bool HasMotion => _motion != null;

    public Camera(
        Vector3 lookFrom,
        Vector3 lookAt,
        Vector3 vUp,
        float vFovDeg,
        float aspectRatio,
        float aperture,
        float focusDist,
        IReadOnlyList<CameraKey>? motion = null)
    {
        _basis = new Basis(lookFrom, lookAt, vUp, vFovDeg, aspectRatio, focusDist);
        _lensRadius = aperture / 2f;
        _aspectRatio = aspectRatio;
        _focusDist = focusDist;
        _motion = motion is { Count: >= 2 } ? motion.OrderBy(k => k.Time).ToArray() : null;
    }

    public Ray GetRay(float s, float t, float time = 0f)
    {
        Basis basis = _motion == null ? _basis : BasisAt(time);

        // Pinhole fast path. Sampling the disk for a zero-radius lens
        // would burn two Sobol dimensions on a value that gets multiplied
        // by zero — wasting the structurally best Sobol(2D) dims for an
        // unused lens offset and shifting every downstream BSDF/light
        // decision onto worse-stratified hash dimensions.
        if (_lensRadius == 0f)
        {
            return new Ray(
                basis.Origin,
                basis.LowerLeftCorner + s * basis.Horizontal + t * basis.Vertical - basis.Origin,
                time);
        }

        Vector3 rd = _lensRadius * MathUtils.RandomInUnitDisk();
        Vector3 offset = basis.U * rd.X + basis.V * rd.Y;

        return new Ray(
            basis.Origin + offset,
            basis.LowerLeftCorner + s * basis.Horizontal + t * basis.Vertical - basis.Origin - offset,
            time);
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
    /// Likewise the auxiliary rays share the primary's time, so the
    /// differential measures pixel area at one frozen instant, not the
    /// motion-blur smear (handled by spp like DoF).
    /// </para>
    /// </summary>
    public Ray GetRayWithDifferentials(float s, float t, float dsdx, float dtdy, float time = 0f)
    {
        Basis basis = _motion == null ? _basis : BasisAt(time);

        Vector3 pixelTarget = basis.LowerLeftCorner + s * basis.Horizontal + t * basis.Vertical;
        Vector3 pixelTargetX = basis.LowerLeftCorner + (s + dsdx) * basis.Horizontal + t * basis.Vertical;
        Vector3 pixelTargetY = basis.LowerLeftCorner + s * basis.Horizontal + (t - dtdy) * basis.Vertical;

        if (_lensRadius == 0f)
        {
            var dir  = pixelTarget  - basis.Origin;
            var dirX = pixelTargetX - basis.Origin;
            var dirY = pixelTargetY - basis.Origin;
            var diff = new RayDifferential(basis.Origin, dirX, basis.Origin, dirY);
            return new Ray(basis.Origin, dir, diff, time);
        }

        Vector3 rd = _lensRadius * MathUtils.RandomInUnitDisk();
        Vector3 offset = basis.U * rd.X + basis.V * rd.Y;
        Vector3 lensOrigin = basis.Origin + offset;

        var dirMain  = pixelTarget  - lensOrigin;
        var dirXa = pixelTargetX - lensOrigin;
        var dirYa = pixelTargetY - lensOrigin;
        var diffA = new RayDifferential(lensOrigin, dirXa, lensOrigin, dirYa);
        return new Ray(lensOrigin, dirMain, diffA, time);
    }

    /// <summary>Interpolated camera pose at <paramref name="time"/> (clamped to the key range).</summary>
    private Basis BasisAt(float time)
    {
        var keys = _motion!;
        CameraKey pose;
        if (time <= keys[0].Time) pose = keys[0];
        else if (time >= keys[^1].Time) pose = keys[^1];
        else
        {
            int i = 1;
            while (keys[i].Time < time) i++;
            CameraKey a = keys[i - 1];
            CameraKey b = keys[i];
            float span = b.Time - a.Time;
            float u = span > 0f ? (time - a.Time) / span : 0f;
            pose = new CameraKey(
                time,
                Vector3.Lerp(a.LookFrom, b.LookFrom, u),
                Vector3.Lerp(a.LookAt, b.LookAt, u),
                Vector3.Lerp(a.Vup, b.Vup, u),
                a.Fov + (b.Fov - a.Fov) * u);
        }
        return new Basis(pose.LookFrom, pose.LookAt, pose.Vup, pose.Fov, _aspectRatio, _focusDist);
    }
}
