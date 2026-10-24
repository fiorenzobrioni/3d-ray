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
/// axis-aligned box. The grid stores a scalar density d(i,j,k) ∈ [0, 1];
/// σ_t(p) = σ_base_t · d(p), σ_s(p) = σ_base_s · d(p), d reconstructed
/// trilinearly or with a Catmull-Rom tricubic.
///
/// <para>Null-collision tracking (<see cref="TrackedMedium"/>) against a coarse
/// grid of majorants walked with a 3D DDA: each majorant cell holds the
/// largest density its points can reconstruct to (the voxels its filter
/// reaches, times the Catmull-Rom overshoot bound for the tricubic),
/// so empty and thin regions cost few collisions and the bound is never
/// violated. The single global majorant before wasted collisions wherever
/// the smoke was thin, and the tricubic overshoot could exceed it.</para>
///
/// <para>The constructor takes raw data + bounds + resolution; parsing of the
/// YAML schema or of the <c>.vol</c> file lives in
/// <see cref="Scene.SceneLoader"/>.</para>
/// </summary>
public sealed class GridMedium : TrackedMedium
{
    private readonly Vector3 _sigmaBaseA;
    private readonly Vector3 _sigmaBaseS;
    private readonly float _sigmaBaseMax;
    private readonly Vector3 _boundsMin;
    private readonly Vector3 _boundsMax;
    private readonly Vector3 _invExtent;
    private readonly int _nx, _ny, _nz;
    private readonly float[] _density;
    private readonly GridInterpolation _interpolation;

    // Majorant grid: _mx × _my × _mz cells over the bounds.
    private readonly int _mx, _my, _mz;
    private readonly float[] _majorant;
    private readonly Vector3 _cellSize;

    /// <summary>
    /// Bound of the separable Catmull-Rom tricubic over data in [0, M], over
    /// M: per axis the weights' positive part sums to at most P = 1.125 and
    /// their negative part to N = 0.125 (at t = ½), so after three passes the
    /// range [a, b] obeys b − a ≤ (P + N)³ M and b + a = M: b ≤ (1 + 1.25³) / 2.
    /// </summary>
    private const float CubicOvershoot = (1f + 1.25f * 1.25f * 1.25f) / 2f;

    public GridMedium(
        Vector3 sigmaBaseA, Vector3 sigmaBaseS,
        Vector3 boundsMin, Vector3 boundsMax,
        int nx, int ny, int nz, float[] density,
        IPhaseFunction phase,
        GridInterpolation interpolation = GridInterpolation.Trilinear) : base(phase)
    {
        if (density.Length != nx * ny * nz)
            throw new ArgumentException(
                $"Grid data length {density.Length} does not match nx*ny*nz = {nx * ny * nz}.");
        if (nx < 2 || ny < 2 || nz < 2)
            throw new ArgumentException("Grid resolution must be at least 2 per axis.");

        _sigmaBaseA = Vector3.Max(sigmaBaseA, Vector3.Zero);
        _sigmaBaseS = Vector3.Max(sigmaBaseS, Vector3.Zero);
        _sigmaBaseMax = MediumMath.Max(_sigmaBaseA + _sigmaBaseS);

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

        // About four voxel cells per majorant cell, at most 32 per axis.
        _mx = Math.Clamp((nx - 1 + 3) / 4, 1, 32);
        _my = Math.Clamp((ny - 1 + 3) / 4, 1, 32);
        _mz = Math.Clamp((nz - 1 + 3) / 4, 1, 32);
        _cellSize = extent / new Vector3(_mx, _my, _mz);
        _majorant = BuildMajorants();
    }

    private float[] BuildMajorants()
    {
        var maj = new float[_mx * _my * _mz];
        bool cubic = _interpolation == GridInterpolation.Tricubic;
        int reachLo = cubic ? 1 : 0, reachHi = cubic ? 2 : 1;
        float overshoot = cubic ? CubicOvershoot : 1f;
        for (int k = 0; k < _mz; k++)
        for (int j = 0; j < _my; j++)
        for (int i = 0; i < _mx; i++)
        {
            // The cell's range in grid coordinates, and the voxels the filter
            // reads for any point in it.
            (int x0, int x1) = VoxelRange(i, _mx, _nx, reachLo, reachHi);
            (int y0, int y1) = VoxelRange(j, _my, _ny, reachLo, reachHi);
            (int z0, int z1) = VoxelRange(k, _mz, _nz, reachLo, reachHi);
            float m = 0f;
            for (int z = z0; z <= z1; z++)
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                        m = MathF.Max(m, Fetch(x, y, z));
            maj[(k * _my + j) * _mx + i] = MathF.Min(1f, m * overshoot);
        }
        return maj;
    }

    private static (int Lo, int Hi) VoxelRange(int cell, int cells, int voxels, int reachLo, int reachHi)
    {
        float g0 = cell / (float)cells * (voxels - 1);
        float g1 = (cell + 1) / (float)cells * (voxels - 1);
        int lo = (int)MathF.Floor(g0) - reachLo;
        int hi = (int)MathF.Floor(g1) + reachHi;
        return (Math.Clamp(lo, 0, voxels - 1), Math.Clamp(hi, 0, voxels - 1));
    }

    protected override void Coefficients(Vector3 p, out Vector3 sigmaA, out Vector3 sigmaS)
    {
        float d = DensityAt(p);
        sigmaA = _sigmaBaseA * d;
        sigmaS = _sigmaBaseS * d;
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

    protected override MajorantWalk BeginMajorants(Ray ray, float tMax)
    {
        var w = new MajorantWalk();
        if (!ClipToBounds(ray, tMax, out float tEnter, out float tExit) || !(_sigmaBaseMax > 0f))
        {
            w.Done = true;
            return w;
        }
        w.T = tEnter;
        w.TEnd = tExit;
        Vector3 p = ray.Origin + ray.Direction * tEnter;
        Vector3 c = (p - _boundsMin) / _cellSize;
        w.X = Math.Clamp((int)MathF.Floor(c.X), 0, _mx - 1);
        w.Y = Math.Clamp((int)MathF.Floor(c.Y), 0, _my - 1);
        w.Z = Math.Clamp((int)MathF.Floor(c.Z), 0, _mz - 1);
        Axis(ray.Origin.X, ray.Direction.X, _boundsMin.X, _cellSize.X, w.X, out w.StepX, out w.NextX, out w.DeltaX);
        Axis(ray.Origin.Y, ray.Direction.Y, _boundsMin.Y, _cellSize.Y, w.Y, out w.StepY, out w.NextY, out w.DeltaY);
        Axis(ray.Origin.Z, ray.Direction.Z, _boundsMin.Z, _cellSize.Z, w.Z, out w.StepZ, out w.NextZ, out w.DeltaZ);
        return w;
    }

    private static void Axis(float o, float d, float min, float size, int cell, out int step, out float next, out float delta)
    {
        if (d > 0f)
        {
            step = 1;
            next = (min + (cell + 1) * size - o) / d;
            delta = size / d;
        }
        else if (d < 0f)
        {
            step = -1;
            next = (min + cell * size - o) / d;
            delta = -size / d;
        }
        else
        {
            step = 0;
            next = float.PositiveInfinity;
            delta = float.PositiveInfinity;
        }
    }

    protected override bool NextMajorant(Ray ray, ref MajorantWalk w, out MajorantSegment segment)
    {
        segment = default;
        if (w.Done || w.T >= w.TEnd) return false;
        float tNext = MathF.Min(MathF.Min(w.NextX, w.NextY), MathF.Min(w.NextZ, w.TEnd));
        segment.T0 = w.T;
        segment.T1 = MathF.Max(tNext, w.T);
        segment.Sigma = _sigmaBaseMax * _majorant[(w.Z * _my + w.Y) * _mx + w.X];
        w.T = segment.T1;
        if (tNext >= w.TEnd) { w.Done = true; return true; }
        if (w.NextX <= w.NextY && w.NextX <= w.NextZ)
        {
            w.X += w.StepX; w.NextX += w.DeltaX;
            if (w.X < 0 || w.X >= _mx) w.Done = true;
        }
        else if (w.NextY <= w.NextZ)
        {
            w.Y += w.StepY; w.NextY += w.DeltaY;
            if (w.Y < 0 || w.Y >= _my) w.Done = true;
        }
        else
        {
            w.Z += w.StepZ; w.NextZ += w.DeltaZ;
            if (w.Z < 0 || w.Z >= _mz) w.Done = true;
        }
        return true;
    }
}
