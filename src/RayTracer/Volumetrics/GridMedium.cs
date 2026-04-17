using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Volumetrics;

/// <summary>
/// Reconstruction filter used when sampling the density grid between voxels.
///   * <see cref="Trilinear"/> — 8-tap C⁰ filter, default. Cheap; shows
///     visible cell-boundary kinks at very low resolutions.
///   * <see cref="Tricubic"/> — 64-tap Catmull-Rom cardinal spline. C¹
///     continuous → no kinks, at ~8× the sample cost. Matches the
///     "cubic"/"smooth" filter offered by Arnold, RenderMan and Houdini
///     on low-resolution VDB grids.
/// </summary>
public enum GridInterpolation
{
    Trilinear,
    Tricubic,
}

/// <summary>
/// Heterogeneous medium backed by a regular 3D density grid inside an
/// axis-aligned bounding box — the model used by Arnold's <c>volume</c> node
/// (when reading VDB), V-Ray <c>VolumeGrid</c>, RenderMan <c>PxrVolume</c>
/// grid mode, and PBRT <c>GridMedium</c>.
///
/// The grid stores a scalar density d(i,j,k) ∈ [0, 1]. Local coefficients
/// come from base values scaled by the interpolated density:
///   σ_T(p) = σ_base_T · d(p),   σ_S(p) = σ_base_S · d(p).
///
/// Free-path sampling uses delta tracking with majorant
/// σ_maj = σ_base_T · maxDensity (precomputed once at load). Rays are
/// clipped to the grid's AABB so empty space costs nothing.
///
/// The grid format itself is format-agnostic inside this class: the
/// constructor takes raw data + bounds + resolution. Parsing of the YAML
/// schema or of the custom <c>.vol</c> file format lives in
/// <see cref="Scene.SceneLoader"/>.
/// </summary>
public sealed class GridMedium : IMedium
{
    private readonly Vector3 _sigmaBaseA;
    private readonly Vector3 _sigmaBaseS;
    private readonly Vector3 _sigmaBaseT;
    private readonly Vector3 _boundsMin;
    private readonly Vector3 _boundsMax;
    private readonly Vector3 _invExtent;
    private readonly int _nx, _ny, _nz;
    private readonly float[] _density;
    private readonly float _maxDensity;
    private readonly GridInterpolation _interpolation;

    private const int MaxIterations = 4096;

    public IPhaseFunction Phase { get; }

    public GridMedium(
        Vector3 sigmaBaseA, Vector3 sigmaBaseS,
        Vector3 boundsMin, Vector3 boundsMax,
        int nx, int ny, int nz, float[] density,
        IPhaseFunction phase,
        GridInterpolation interpolation = GridInterpolation.Trilinear)
    {
        if (density.Length != nx * ny * nz)
            throw new ArgumentException(
                $"Grid data length {density.Length} does not match nx*ny*nz = {nx * ny * nz}.");
        if (nx < 2 || ny < 2 || nz < 2)
            throw new ArgumentException("Grid resolution must be at least 2 per axis.");

        _sigmaBaseA = Vector3.Max(sigmaBaseA, Vector3.Zero);
        _sigmaBaseS = Vector3.Max(sigmaBaseS, Vector3.Zero);
        _sigmaBaseT = _sigmaBaseA + _sigmaBaseS;

        // Ensure well-ordered bounds.
        _boundsMin = Vector3.Min(boundsMin, boundsMax);
        _boundsMax = Vector3.Max(boundsMin, boundsMax);
        Vector3 extent = _boundsMax - _boundsMin;
        _invExtent = new Vector3(
            extent.X > 1e-20f ? 1f / extent.X : 0f,
            extent.Y > 1e-20f ? 1f / extent.Y : 0f,
            extent.Z > 1e-20f ? 1f / extent.Z : 0f);

        _nx = nx; _ny = ny; _nz = nz;
        _density = density;
        _interpolation = interpolation;

        float maxD = 0f;
        for (int i = 0; i < density.Length; i++)
        {
            float v = density[i];
            if (v < 0f) v = 0f;
            if (v > 1f) v = 1f;
            if (v > maxD) maxD = v;
        }
        _maxDensity = maxD;

        Phase = phase;
    }

    private float Fetch(int ix, int iy, int iz)
    {
        // z-major: most common for volume data exports, cache-friendly for xy slices.
        float v = _density[(iz * _ny + iy) * _nx + ix];
        if (v < 0f) v = 0f;
        if (v > 1f) v = 1f;
        return v;
    }

    private float FetchClamped(int ix, int iy, int iz)
    {
        if (ix < 0) ix = 0; else if (ix > _nx - 1) ix = _nx - 1;
        if (iy < 0) iy = 0; else if (iy > _ny - 1) iy = _ny - 1;
        if (iz < 0) iz = 0; else if (iz > _nz - 1) iz = _nz - 1;
        return Fetch(ix, iy, iz);
    }

    private float DensityAt(Vector3 p)
    {
        Vector3 u = (p - _boundsMin) * _invExtent;  // normalised grid coords ∈ [0, 1]
        if (u.X < 0f || u.X > 1f || u.Y < 0f || u.Y > 1f || u.Z < 0f || u.Z > 1f)
            return 0f;

        float gx = u.X * (_nx - 1);
        float gy = u.Y * (_ny - 1);
        float gz = u.Z * (_nz - 1);

        return _interpolation == GridInterpolation.Tricubic
            ? TricubicAt(gx, gy, gz)
            : TrilinearAt(gx, gy, gz);
    }

    private float TrilinearAt(float gx, float gy, float gz)
    {
        int ix = (int)MathF.Floor(gx);
        int iy = (int)MathF.Floor(gy);
        int iz = (int)MathF.Floor(gz);
        if (ix < 0) ix = 0; if (ix > _nx - 2) ix = _nx - 2;
        if (iy < 0) iy = 0; if (iy > _ny - 2) iy = _ny - 2;
        if (iz < 0) iz = 0; if (iz > _nz - 2) iz = _nz - 2;

        float fx = gx - ix;
        float fy = gy - iy;
        float fz = gz - iz;

        float d000 = Fetch(ix,     iy,     iz    );
        float d100 = Fetch(ix + 1, iy,     iz    );
        float d010 = Fetch(ix,     iy + 1, iz    );
        float d110 = Fetch(ix + 1, iy + 1, iz    );
        float d001 = Fetch(ix,     iy,     iz + 1);
        float d101 = Fetch(ix + 1, iy,     iz + 1);
        float d011 = Fetch(ix,     iy + 1, iz + 1);
        float d111 = Fetch(ix + 1, iy + 1, iz + 1);

        float d00 = d000 + fx * (d100 - d000);
        float d10 = d010 + fx * (d110 - d010);
        float d01 = d001 + fx * (d101 - d001);
        float d11 = d011 + fx * (d111 - d011);
        float d0  = d00  + fy * (d10  - d00 );
        float d1  = d01  + fy * (d11  - d01 );
        return d0 + fz * (d1 - d0);
    }

    /// <summary>
    /// Separable tricubic reconstruction with a Catmull-Rom basis (τ = 0.5).
    /// 16 interpolations along x → 4 along y → 1 along z = 64 taps.
    /// Boundary voxels are addressed with clamp-to-edge so the filter stays
    /// well-defined at the grid border. The Catmull-Rom spline can overshoot
    /// slightly between voxels, so the result is clamped to [0, 1] — this is
    /// required for the delta-tracking majorant invariant (σ_T ≤ σ_maj).
    /// </summary>
    private float TricubicAt(float gx, float gy, float gz)
    {
        int ix = (int)MathF.Floor(gx);
        int iy = (int)MathF.Floor(gy);
        int iz = (int)MathF.Floor(gz);
        float fx = gx - ix;
        float fy = gy - iy;
        float fz = gz - iz;

        Span<float> cy = stackalloc float[4];
        Span<float> cz = stackalloc float[4];

        for (int kk = -1; kk <= 2; kk++)
        {
            int zk = iz + kk;
            for (int jj = -1; jj <= 2; jj++)
            {
                int yj = iy + jj;
                float c0 = FetchClamped(ix - 1, yj, zk);
                float c1 = FetchClamped(ix,     yj, zk);
                float c2 = FetchClamped(ix + 1, yj, zk);
                float c3 = FetchClamped(ix + 2, yj, zk);
                cy[jj + 1] = CatmullRom(c0, c1, c2, c3, fx);
            }
            cz[kk + 1] = CatmullRom(cy[0], cy[1], cy[2], cy[3], fy);
        }

        float d = CatmullRom(cz[0], cz[1], cz[2], cz[3], fz);
        if (d < 0f) d = 0f;
        if (d > 1f) d = 1f;
        return d;
    }

    /// <summary>
    /// Catmull-Rom cardinal spline, τ = 0.5. Interpolates between p1 and p2
    /// with p0 and p3 as tangent hints. Returns p1 at t=0 and p2 at t=1.
    /// </summary>
    private static float CatmullRom(float p0, float p1, float p2, float p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        // 0.5 * ((2p1) + (-p0 + p2)t + (2p0 - 5p1 + 4p2 - p3)t² + (-p0 + 3p1 - 3p2 + p3)t³)
        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    /// <summary>
    /// Slab ray-AABB intersection. Returns the sub-interval [tEnter, tExit] of
    /// [0, tMax] that lies inside the grid bounds. Used to skip empty space.
    /// </summary>
    private bool ClipToBounds(Ray ray, float tMax, out float tEnter, out float tExit)
    {
        tEnter = 0f;
        tExit = tMax;
        for (int axis = 0; axis < 3; axis++)
        {
            float o = axis == 0 ? ray.Origin.X : axis == 1 ? ray.Origin.Y : ray.Origin.Z;
            float d = axis == 0 ? ray.Direction.X : axis == 1 ? ray.Direction.Y : ray.Direction.Z;
            float mn = axis == 0 ? _boundsMin.X : axis == 1 ? _boundsMin.Y : _boundsMin.Z;
            float mx = axis == 0 ? _boundsMax.X : axis == 1 ? _boundsMax.Y : _boundsMax.Z;

            if (MathF.Abs(d) < 1e-20f)
            {
                if (o < mn || o > mx) return false;
                continue;
            }
            float invD = 1f / d;
            float t0 = (mn - o) * invD;
            float t1 = (mx - o) * invD;
            if (t0 > t1) (t0, t1) = (t1, t0);
            if (t0 > tEnter) tEnter = t0;
            if (t1 < tExit) tExit = t1;
            if (tExit <= tEnter) return false;
        }
        if (tEnter < 0f) tEnter = 0f;
        return tExit > tEnter;
    }

    public Vector3 Transmittance(Ray ray, float tMax)
    {
        float tEnd = MathF.Min(MathF.Max(tMax, 0f), 1e30f);
        if (!ClipToBounds(ray, tEnd, out float tEnter, out float tExit))
            return Vector3.One;   // ray misses grid → no attenuation

        float sigmaMaj = MathF.Max(MathF.Max(_sigmaBaseT.X, _sigmaBaseT.Y), _sigmaBaseT.Z)
                        * _maxDensity;
        if (sigmaMaj <= 0f) return Vector3.One;

        Vector3 Tr = Vector3.One;
        float t = tEnter;
        for (int iter = 0; iter < MaxIterations; iter++)
        {
            float dt = -MathF.Log(1f - MathUtils.RandomFloat()) / sigmaMaj;
            t += dt;
            if (t >= tExit) break;

            Vector3 p = ray.Origin + ray.Direction * t;
            float d = DensityAt(p);
            Vector3 sigmaT = _sigmaBaseT * d;
            Tr *= Vector3.One - sigmaT / sigmaMaj;

            if (Tr.X < 1e-5f && Tr.Y < 1e-5f && Tr.Z < 1e-5f) break;
        }
        return Tr;
    }

    public bool Sample(Ray ray, float tMax, out float t, out Vector3 beta, out bool scattered)
    {
        if (!ClipToBounds(ray, tMax, out float tEnter, out float tExit))
        {
            // Ray never enters the grid — the volumetric path is a no-op, same
            // as the null-medium fast path.
            t = tMax;
            beta = Vector3.One;
            scattered = false;
            return false;
        }

        // Channel-selected delta tracking, scaled by the max density so that
        // σ_maj_ch actually bounds σ_T[ch](p) tightly over the grid.
        int ch = (int)(MathUtils.RandomFloat() * 3f);
        if (ch > 2) ch = 2;
        float baseCh = ch == 0 ? _sigmaBaseT.X : ch == 1 ? _sigmaBaseT.Y : _sigmaBaseT.Z;
        float sigmaMajCh = baseCh * _maxDensity;

        if (sigmaMajCh <= 0f)
        {
            t = tMax;
            beta = Vector3.One;
            scattered = false;
            return false;
        }

        float tCur = tEnter;
        for (int iter = 0; iter < MaxIterations; iter++)
        {
            float dt = -MathF.Log(1f - MathUtils.RandomFloat()) / sigmaMajCh;
            tCur += dt;
            if (tCur >= tExit) break;

            Vector3 p = ray.Origin + ray.Direction * tCur;
            float d = DensityAt(p);
            float sigmaTch = baseCh * d;

            if (MathUtils.RandomFloat() * sigmaMajCh < sigmaTch)
            {
                t = tCur;
                scattered = true;
                Vector3 sigmaS = _sigmaBaseS * d;
                float denom = MathF.Max(sigmaTch, 1e-20f);
                beta = sigmaS / denom;
                return true;
            }
        }

        t = tMax;
        beta = Vector3.One;
        scattered = false;
        return false;
    }
}
