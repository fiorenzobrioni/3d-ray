using System.Numerics;
using RayTracer.Core;
using RayTracer.Materials;
using RayTracer.Textures;

namespace RayTracer.Geometry;

/// <summary>
/// A heightfield primitive — a continuous surface <c>y = h(x, z) · HeightScale</c>
/// over the XZ rectangle <c>[XMin, XMax] × [ZMin, ZMax]</c>. The height function
/// is bilinearly interpolated from a regular <c>(Resolution+1) × (Resolution+1)</c>
/// sample grid; the grid itself can be supplied as a baked heightmap (PNG-16
/// via <see cref="HeightmapLoader"/>) or synthesised from a procedural
/// <see cref="ITexture"/> at construction time.
///
/// Ray intersection follows Tevs/Ihrke/Seidel 2008 — a hierarchical min/max
/// pyramid (<see cref="MinMaxMipmap"/>) prunes XZ cells whose Y envelope misses
/// the ray; the closest surviving cell is then bisected on the bilinear patch.
/// This trades a <c>O(N²)</c> linear march for <c>O(log N)</c> traversal,
/// which is what lets a single primitive replace a tessellated mesh of
/// 100k+ triangles without the BVH.
/// </summary>
public sealed class HeightField : IHittable
{
    public float XMin { get; }
    public float ZMin { get; }
    public float XMax { get; }
    public float ZMax { get; }
    public float HeightScale { get; }
    public IMaterial Material { get; }
    public int Seed { get; set; }

    private readonly float[] _samples;
    private readonly int _samplesX;
    private readonly int _samplesZ;
    private readonly float _invCellX;
    private readonly float _invCellZ;
    private readonly MinMaxMipmap _accel;
    private readonly AABB _aabb;

    // Perlin used to perturb the altitude/slope coordinates of the strata
    // selector — gives the band transitions an organic, noise-driven
    // boundary instead of a sharp altitude contour.
    private readonly Perlin _noise = Perlin.GetOrCreate(0);
    // Maximum jitter amplitude in normalised-altitude units. Calibrated
    // so a band transition on the ~25-unit-tall demo terrain spreads
    // over roughly 3-4 world units — comparable to a real snowline that
    // fluctuates ~100 m on a 1 km peak between sunlit and shaded faces.
    // Larger values fragment bands into interleaved patches; smaller
    // values keep the contour too geodetic.
    private const float StratumJitter = 0.12f;
    // Slope jitter range in degrees. Cliff bands (rock, min_slope_deg ≥
    // 25) need a meaningful slope perturbation or they keep tracing
    // exact gradient contours; ±15° is one stratum-width on a 0-90° axis.
    private const float SlopeJitterDeg = 15f;

    public float? SeaLevel { get; }
    public IMaterial? SeaMaterial { get; }
    public IReadOnlyList<StratumBand>? Strata { get; }

    /// <summary>
    /// Constructs the primitive from a pre-sampled height grid (heights in
    /// <c>[0, 1]</c>, pre-<see cref="HeightScale"/>). Sample (0, 0) sits at
    /// <c>(XMin, ZMin)</c>; sample <c>(samplesX-1, samplesZ-1)</c> at
    /// <c>(XMax, ZMax)</c>. The pyramid is built immediately.
    /// </summary>
    public HeightField(
        float xMin, float zMin, float xMax, float zMax,
        float[] samples, int samplesX, int samplesZ,
        float heightScale, IMaterial material,
        float? seaLevel = null, IMaterial? seaMaterial = null,
        IReadOnlyList<StratumBand>? strata = null)
    {
        if (samplesX < 2 || samplesZ < 2)
            throw new ArgumentException("HeightField needs at least 2 samples per axis.");
        if (samples.Length != samplesX * samplesZ)
            throw new ArgumentException("Heightmap size mismatch.");
        if (xMax <= xMin || zMax <= zMin)
            throw new ArgumentException("HeightField bounds must satisfy xMax>xMin and zMax>zMin.");

        XMin = xMin; ZMin = zMin; XMax = xMax; ZMax = zMax;
        HeightScale = heightScale;
        Material = material;
        SeaLevel = seaLevel;
        SeaMaterial = seaMaterial;
        Strata = strata;

        _samples = samples;
        _samplesX = samplesX;
        _samplesZ = samplesZ;
        _invCellX = (samplesX - 1) / (xMax - xMin);
        _invCellZ = (samplesZ - 1) / (zMax - zMin);

        // Scale the heights once into world units for the pyramid so the
        // ray-AABB slab test compares world-space Y values directly.
        var scaled = new float[samples.Length];
        float maxH = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            scaled[i] = samples[i] * heightScale;
            if (scaled[i] > maxH) maxH = scaled[i];
        }
        _accel = new MinMaxMipmap(scaled, samplesX, samplesZ, xMin, zMin, xMax, zMax);

        float yLo = 0f;
        float yHi = MathF.Max(maxH, seaLevel ?? 0f);
        _aabb = new AABB(
            new Vector3(xMin, yLo - 1e-3f, zMin),
            new Vector3(xMax, yHi + 1e-3f, zMax));
    }

    /// <summary>
    /// Convenience overload that pre-samples an <see cref="ITexture"/> onto
    /// a <c>resolution × resolution</c> grid using the texture's RGB
    /// luminance as the height. Used for procedural-mode heightfields where
    /// the YAML supplies a noise definition rather than a baked PNG.
    /// </summary>
    public static HeightField FromProceduralTexture(
        float xMin, float zMin, float xMax, float zMax,
        ITexture heightTexture, int resolution,
        float heightScale, IMaterial material,
        float? seaLevel = null, IMaterial? seaMaterial = null,
        IReadOnlyList<StratumBand>? strata = null)
    {
        if (resolution < 4) resolution = 4;
        int n = resolution + 1;
        var samples = new float[n * n];
        float dx = (xMax - xMin) / resolution;
        float dz = (zMax - zMin) / resolution;
        for (int j = 0; j < n; j++)
        {
            float z = zMin + j * dz;
            float v = (float)j / resolution;
            for (int i = 0; i < n; i++)
            {
                float x = xMin + i * dx;
                float u = (float)i / resolution;
                var c = heightTexture.Value(u, v, new Vector3(x, 0f, z), 0);
                // Luminance — same coefficients used elsewhere in the engine
                // for greyscale collapse (Rec.709).
                float h = 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;
                if (h < 0f) h = 0f;
                if (h > 1f) h = 1f;
                samples[i + j * n] = h;
            }
        }
        return new HeightField(xMin, zMin, xMax, zMax, samples, n, n,
                               heightScale, material,
                               seaLevel, seaMaterial, strata);
    }

    public AABB BoundingBox() => _aabb;

    public bool Hit(Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        // Hold the best-so-far terrain hit; finalize after we know whether
        // the optional sea plane wins. The traversal visits leaf cells in
        // front-to-back order so the first successful bisection is also the
        // closest — but we re-check tHit < terrainT defensively because a
        // single cell's bisection interval may straddle a level-boundary
        // grazing case where the next cell starts at a slightly smaller t.
        bool terrainFound = false;
        float terrainT = float.PositiveInfinity;
        float traversalTMax = tMax;

        _accel.TraverseRay(ray, tMin, tMax,
            (int cellX, int cellZ, float cellTEnter, float cellTExit, out float newTMax) =>
            {
                newTMax = traversalTMax;
                if (TryBisectCell(ray, cellX, cellZ, cellTEnter, cellTExit, out float tHit)
                    && tHit < terrainT)
                {
                    terrainT = tHit;
                    terrainFound = true;
                    newTMax = tHit;
                    return true;
                }
                return false;
            },
            ref traversalTMax);

        // ── Optional sea-level plane ────────────────────────────────────
        bool seaFound = false;
        float seaT = float.PositiveInfinity;
        if (SeaLevel.HasValue && SeaMaterial != null && MathF.Abs(ray.Direction.Y) > 1e-8f)
        {
            float t = (SeaLevel.Value - ray.Origin.Y) / ray.Direction.Y;
            if (t >= tMin && t <= tMax)
            {
                Vector3 p = ray.At(t);
                if (p.X >= XMin && p.X <= XMax && p.Z >= ZMin && p.Z <= ZMax)
                {
                    // Only treat the water plane as visible where the terrain
                    // beneath actually sits below sea level (no water sheets
                    // hovering above dry land).
                    float terrainHere = SampleHeight(p.X, p.Z);
                    if (terrainHere <= SeaLevel.Value + 1e-4f)
                    {
                        seaT = t;
                        seaFound = true;
                    }
                }
            }
        }

        if (!terrainFound && !seaFound) return false;

        if (seaFound && (!terrainFound || seaT < terrainT))
        {
            Vector3 p = ray.At(seaT);
            rec.T = seaT;
            rec.Point = p;
            rec.LocalPoint = p - new Vector3(XMin, 0f, ZMin);
            rec.U = (p.X - XMin) / (XMax - XMin);
            rec.V = (p.Z - ZMin) / (ZMax - ZMin);
            rec.SetFaceNormal(ray, Vector3.UnitY);
            rec.Tangent = Vector3.UnitX;
            rec.Bitangent = Vector3.UnitZ;
            rec.DpDu = new Vector3(XMax - XMin, 0f, 0f);
            rec.DpDv = new Vector3(0f, 0f, ZMax - ZMin);
            rec.Material = SeaMaterial;
            rec.ObjectSeed = Seed;
            return true;
        }

        FillTerrainRecord(ray, terrainT, ref rec);
        return true;
    }

    private void FillTerrainRecord(Ray ray, float t, ref HitRecord rec)
    {
        Vector3 p = ray.At(t);
        rec.T = t;
        rec.Point = p;
        rec.LocalPoint = p - new Vector3(XMin, 0f, ZMin);
        rec.U = (p.X - XMin) / (XMax - XMin);
        rec.V = (p.Z - ZMin) / (ZMax - ZMin);

        Vector3 n = ComputeNormal(p.X, p.Z);
        rec.SetFaceNormal(ray, n);

        // Tangent along +X (in the height-gradient plane), bitangent = N × T
        // so the (T, B, N) frame stays orthonormal.
        float dhdx = SampleHeightDerivX(p.X, p.Z);
        Vector3 tangent = Vector3.Normalize(new Vector3(1f, dhdx, 0f));
        Vector3 bitangent = Vector3.Normalize(Vector3.Cross(rec.Normal, tangent));
        rec.Tangent = tangent;
        rec.Bitangent = bitangent;
        rec.DpDu = new Vector3(XMax - XMin, 0f, 0f);
        rec.DpDv = new Vector3(0f, 0f, ZMax - ZMin);

        rec.Material = SelectMaterial(p, rec.Normal);
        rec.ObjectSeed = Seed;
    }

    private IMaterial SelectMaterial(Vector3 p, Vector3 normal)
    {
        if (Strata == null || Strata.Count == 0) return Material;

        // Normalise altitude against HeightScale — the maximum the surface
        // *could* reach when normalised samples = 1. Using the actual peak
        // observed in the heightmap would compress altNorm into [0, 1] even
        // for half-filled grids, so the user's `min_altitude` / `max_altitude`
        // values would have nothing to do with the YAML max_height intent.
        // Slope is the angle off vertical, in degrees — 0 = flat, 90 = cliff.
        float baseY = SeaLevel ?? 0f;
        float altSpan = MathF.Max(HeightScale - baseY, 1e-4f);
        float altNorm = Math.Clamp((p.Y - baseY) / altSpan, 0f, 1f);
        float slopeDeg = MathF.Acos(Math.Clamp(normal.Y, -1f, 1f)) * (180f / MathF.PI);

        // ── Noise-perturbed band selection (Frostbite/Unreal terrain trick) ──
        // The hit point's altitude is jittered by an fBm sample before band
        // weighting so the alt/slope contours stop being geodesic lines and
        // start tracing organic, biome-like boundaries with detail at
        // multiple scales (large lobes + small protrusions, like a real
        // snow line). Selection remains winner-takes-all (no BRDF mixing,
        // no per-hit random) — the perceived blend comes from the boundary
        // curve following noise contours and from each band material's own
        // texture noise hiding the residual discontinuity.
        //
        // The base frequency gives ~6 large blobs across the terrain;
        // two octaves of fBm add finer fragmentation (~3-unit speckle on
        // the demo 100-unit terrain). Both axes use a different noise
        // domain so the altitude and slope perturbations are decorrelated.
        float jitterFreq = 6f / (XMax - XMin);
        float altJitter = StratumJitter * _noise.Fbm(
            new Vector3(p.X * jitterFreq, 0f, p.Z * jitterFreq),
            octaves: 2, lacunarity: 2.5f, gain: 0.5f, signed: true);
        altNorm = Math.Clamp(altNorm + altJitter, 0f, 1f);

        // Slope gets its own decorrelated jitter so cliff faces also
        // fragment the rock/grass contour, not just the altitude bands.
        // Offset by a constant 3D vector to decorrelate from the altitude
        // sample without paying for a second Perlin permutation table.
        float slopeJitter = SlopeJitterDeg * _noise.Fbm(
            new Vector3(p.X * jitterFreq + 137f, 0f, p.Z * jitterFreq + 91f),
            octaves: 2, lacunarity: 2.5f, gain: 0.5f, signed: true);
        slopeDeg = Math.Clamp(slopeDeg + slopeJitter, 0f, 90f);

        // Iteration order matters for ties. The band list is emitted by
        // TerrainGen in increasing specificity (sand → ground → rock →
        // snow), so on the snow/rock overlap zone where both bands have
        // weight 1, we want the *later* (more specialised) band to win.
        // `>=` makes the last band touch the score replace the previous
        // tie-winner — natural for the "snow caps the peak even when rock
        // is technically also valid up there" behaviour.
        IMaterial best = Material;
        float bestScore = 0f;
        foreach (var s in Strata)
        {
            float wAlt = BandWeight(altNorm, s.MinAltitude, s.MaxAltitude, s.BlendWidth);
            float wSlope = BandWeight(slopeDeg, s.MinSlopeDeg, s.MaxSlopeDeg, MathF.Max(s.BlendWidth * 90f, 5f));
            float w = wAlt * wSlope;
            if (w > 0f && w >= bestScore)
            {
                bestScore = w;
                best = s.Material;
            }
        }
        return bestScore > 0f ? best : Material;
    }

    private static float BandWeight(float v, float lo, float hi, float blend)
    {
        // Plateau of 1 inside (lo, hi), linear fade outside over blend width.
        // blend = 0 collapses to a hard {0, 1} step at the band boundaries.
        if (v >= lo && v <= hi) return 1f;
        if (blend <= 0f) return 0f;
        if (v < lo) return MathF.Max(0f, 1f - (lo - v) / blend);
        return MathF.Max(0f, 1f - (v - hi) / blend);
    }

    // ── Heightmap sampling ──────────────────────────────────────────────

    private float SampleHeight(float x, float z)
    {
        float u = (x - XMin) * _invCellX;
        float v = (z - ZMin) * _invCellZ;
        if (u < 0f) u = 0f; else if (u > _samplesX - 1) u = _samplesX - 1;
        if (v < 0f) v = 0f; else if (v > _samplesZ - 1) v = _samplesZ - 1;
        int i0 = (int)u; int j0 = (int)v;
        if (i0 >= _samplesX - 1) i0 = _samplesX - 2;
        if (j0 >= _samplesZ - 1) j0 = _samplesZ - 2;
        float fu = u - i0;
        float fv = v - j0;
        float h00 = _samples[i0     + j0     * _samplesX];
        float h10 = _samples[i0 + 1 + j0     * _samplesX];
        float h01 = _samples[i0     + (j0+1) * _samplesX];
        float h11 = _samples[i0 + 1 + (j0+1) * _samplesX];
        float a = h00 + (h10 - h00) * fu;
        float b = h01 + (h11 - h01) * fu;
        return (a + (b - a) * fv) * HeightScale;
    }

    private float SampleHeightDerivX(float x, float z)
    {
        // Central differences on the bilinear patch; ε scaled to one cell so
        // the derivative is consistent with the surface that's actually being
        // rendered (analytic ∂/∂x of a bilinear patch).
        float eps = 0.5f / _invCellX;
        return (SampleHeight(x + eps, z) - SampleHeight(x - eps, z)) / (2f * eps);
    }

    private float SampleHeightDerivZ(float x, float z)
    {
        float eps = 0.5f / _invCellZ;
        return (SampleHeight(x, z + eps) - SampleHeight(x, z - eps)) / (2f * eps);
    }

    private Vector3 ComputeNormal(float x, float z)
    {
        // Surface y = h(x, z). Tangents are (1, ∂h/∂x, 0) and (0, ∂h/∂z, 1);
        // their cross product gives the outward (upward-Y) normal.
        float hx = SampleHeightDerivX(x, z);
        float hz = SampleHeightDerivZ(x, z);
        return Vector3.Normalize(new Vector3(-hx, 1f, -hz));
    }

    // ── Per-cell bisection on the bilinear patch ────────────────────────

    private bool TryBisectCell(Ray ray, int cellX, int cellZ,
                               float tEnter, float tExit, out float tHit)
    {
        tHit = 0f;

        // Sign of f(t) = ray.At(t).Y - h(ray.At(t).XZ). When it crosses zero
        // the ray pierces the surface. Sample at both ends of the cell entry
        // interval; if both have the same sign and small magnitude, recurse
        // a couple of times to detect grazing hits, otherwise bisect.
        float tA = tEnter;
        float tB = tExit;
        float fA = SignedHeightDiff(ray, tA);
        float fB = SignedHeightDiff(ray, tB);

        // Skip cells where both endpoints are clearly above the surface — the
        // pyramid bound says the cell envelope is pierced, but the ray's Y
        // trajectory may still ride above the surface within this single cell
        // (e.g. coarse cell, ray glancing the tip of a peak).
        if (fA > 0f && fB > 0f) return false;
        if (fA < 0f && fB < 0f) return false;

        // Standard 12-step bisection: at ~10 we're already at 1e-3 of the
        // interval; 12 gives sub-pixel accuracy for any reasonable cell size.
        for (int i = 0; i < 12; i++)
        {
            float tM = 0.5f * (tA + tB);
            float fM = SignedHeightDiff(ray, tM);
            if (Math.Sign(fM) == Math.Sign(fA))
            {
                tA = tM; fA = fM;
            }
            else
            {
                tB = tM; fB = fM;
            }
        }
        tHit = 0.5f * (tA + tB);
        return true;
    }

    private float SignedHeightDiff(Ray ray, float t)
    {
        Vector3 p = ray.At(t);
        return p.Y - SampleHeight(p.X, p.Z);
    }

    /// <summary>
    /// Strata band — one altitude/slope window mapped to a material. Multiple
    /// bands may overlap; the band with the highest combined weight wins at
    /// each shading point (the v1 stratum selector is "winner takes all";
    /// proper inter-band blending is a TODO).
    /// </summary>
    public sealed class StratumBand
    {
        public float MinAltitude { get; init; } = 0f;
        public float MaxAltitude { get; init; } = 1f;
        public float MinSlopeDeg { get; init; } = 0f;
        public float MaxSlopeDeg { get; init; } = 90f;
        public float BlendWidth { get; init; } = 0f;
        public IMaterial Material { get; init; } = null!;
    }
}
