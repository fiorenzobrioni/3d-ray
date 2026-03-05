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
        _u = Vector3.Normalize(Vector3.Cross(vUp, _w));
        _v = Vector3.Cross(_w, _u);

        _origin = lookFrom;
        _horizontal = focusDist * viewportWidth * _u;
        _vertical = focusDist * viewportHeight * _v;
        _lowerLeftCorner = _origin - _horizontal / 2f - _vertical / 2f - focusDist * _w;
        _lensRadius = aperture / 2f;
    }

    public Ray GetRay(float s, float t)
    {
        Vector3 rd = _lensRadius * MathUtils.RandomInUnitDisk();
        Vector3 offset = _u * rd.X + _v * rd.Y;

        return new Ray(
            _origin + offset,
            _lowerLeftCorner + s * _horizontal + t * _vertical - _origin - offset);
    }
}
