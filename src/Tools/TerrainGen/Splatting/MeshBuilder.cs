using System;
using System.Collections.Generic;
using System.Numerics;
using TerrainGen.Heightmap;
using TerrainGen.Hydrology;

namespace TerrainGen.Splatting;

public sealed class StratumMesh
{
    public List<Vector3> Positions { get; } = new();
    public List<Vector3> Normals   { get; } = new();
    public List<Vector2> Uvs       { get; } = new();
    public List<(int a, int b, int c)> Faces { get; } = new();
    public bool FlatShade { get; init; }
}

public sealed class TerrainMeshBundle
{
    public Dictionary<Stratum, StratumMesh> Land { get; } = new();
    public StratumMesh? WaterSurface { get; set; }
    public float MinY { get; set; }
    public float MaxY { get; set; }
    public float AvgY { get; set; }
}

/// <summary>
/// Tessellates the heightmap into a triangle grid, classifies each triangle by
/// stratum, and emits per-stratum meshes plus an optional water-surface mesh.
///
/// For smooth shading, vertices shared across triangles of the SAME stratum
/// are deduplicated; vertices shared across strata boundaries are duplicated
/// (each stratum mesh has its own vertex list and per-vertex smooth normals).
///
/// For flat shading (Minecraft / Lowpoly styles) all triangles get unique
/// vertices with a face normal — that's what gives the geometric look.
/// </summary>
public static class MeshBuilder
{
    public static TerrainMeshBundle Build(GenerationConfig cfg, Heightmap2D hm, WaterMask mask, bool flatShade, float heightScale)
    {
        var bundle = new TerrainMeshBundle();
        foreach (Stratum s in Enum.GetValues<Stratum>())
            bundle.Land[s] = new StratumMesh { FlatShade = flatShade };

        int n = hm.N;
        float worldHalf = cfg.Size * 0.5f;
        float cellWorld = cfg.Size / (n - 1);

        // Per-stratum dedup map for smooth shading: (gridX, gridZ) -> local vertex index
        var dedup = new Dictionary<Stratum, Dictionary<long, int>>();
        foreach (Stratum s in Enum.GetValues<Stratum>()) dedup[s] = new();

        // Stats for camera framing.
        var (h01min, h01max, h01avg) = hm.Stats();
        bundle.MinY = h01min * heightScale;
        bundle.MaxY = h01max * heightScale;
        bundle.AvgY = h01avg * heightScale;

        // Pre-compute per-grid-vertex world positions and smooth normals.
        var pos = new Vector3[n * n];
        var nor = new Vector3[n * n];
        var uv  = new Vector2[n * n];
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            int i = z * n + x;
            float wx = -worldHalf + x * cellWorld;
            float wz = -worldHalf + z * cellWorld;
            float wy = hm.Data[i] * heightScale;
            pos[i] = new Vector3(wx, wy, wz);
            uv[i]  = new Vector2((float)x / (n - 1), (float)z / (n - 1));
            // Gradient-based normal in world units: dy/dx and dy/dz are scaled.
            float hL = hm.SampleClamped(x - 1, z) * heightScale;
            float hR = hm.SampleClamped(x + 1, z) * heightScale;
            float hD = hm.SampleClamped(x, z - 1) * heightScale;
            float hU = hm.SampleClamped(x, z + 1) * heightScale;
            var nv = new Vector3((hL - hR) / (2f * cellWorld), 1f, (hD - hU) / (2f * cellWorld));
            nor[i] = Vector3.Normalize(nv);
        }

        // Tessellate quads -> 2 triangles, classify, append to right mesh.
        for (int z = 0; z < n - 1; z++)
        for (int x = 0; x < n - 1; x++)
        {
            int i00 = z * n + x;
            int i10 = z * n + (x + 1);
            int i01 = (z + 1) * n + x;
            int i11 = (z + 1) * n + (x + 1);

            // Triangle A: (i00, i10, i11)
            EmitTriangle(cfg, hm, mask, bundle, dedup, pos, nor, uv, n,
                i00, i10, i11, x, z, x + 1, z, x + 1, z + 1, flatShade, heightScale);

            // Triangle B: (i00, i11, i01)
            EmitTriangle(cfg, hm, mask, bundle, dedup, pos, nor, uv, n,
                i00, i11, i01, x, z, x + 1, z + 1, x, z + 1, flatShade, heightScale);
        }

        // Build water surface mesh: same tessellation, but only triangles where
        // ALL THREE underlying cells are wet, with vertex Y replaced by water level.
        bundle.WaterSurface = BuildWaterSurfaceMesh(hm, mask, n, cfg.Size, heightScale, flatShade);

        return bundle;
    }

    private static void EmitTriangle(
        GenerationConfig cfg, Heightmap2D hm, WaterMask mask,
        TerrainMeshBundle bundle, Dictionary<Stratum, Dictionary<long, int>> dedup,
        Vector3[] pos, Vector3[] nor, Vector2[] uv, int n,
        int ia, int ib, int ic,
        int xa, int za, int xb, int zb, int xc, int zc,
        bool flatShade, float heightScale)
    {
        // Average height (normalised) and slope from per-vertex normals.
        float ha = hm.Data[ia], hb = hm.Data[ib], hc = hm.Data[ic];
        float hAvg = (ha + hb + hc) / 3f;
        Vector3 nAvg = Vector3.Normalize(nor[ia] + nor[ib] + nor[ic]);
        float slope = 1f - Math.Clamp(nAvg.Y, 0f, 1f);

        bool wa = mask.IsWet(xa, za), wb = mask.IsWet(xb, zb), wc = mask.IsWet(xc, zc);
        bool allWet = wa && wb && wc;
        bool anyWet = wa || wb || wc;

        var stratum = StratumClassifier.Classify(cfg, hAvg, slope, anyWet, allWet);
        var mesh = bundle.Land[stratum];

        if (flatShade)
        {
            // Compute face normal in world space, append unique vertices.
            Vector3 fn = Vector3.Normalize(Vector3.Cross(pos[ib] - pos[ia], pos[ic] - pos[ia]));
            int va = mesh.Positions.Count;
            mesh.Positions.Add(pos[ia]); mesh.Normals.Add(fn); mesh.Uvs.Add(uv[ia]);
            mesh.Positions.Add(pos[ib]); mesh.Normals.Add(fn); mesh.Uvs.Add(uv[ib]);
            mesh.Positions.Add(pos[ic]); mesh.Normals.Add(fn); mesh.Uvs.Add(uv[ic]);
            mesh.Faces.Add((va, va + 1, va + 2));
        }
        else
        {
            int la = LookupOrAdd(mesh, dedup[stratum], ia, pos, nor, uv);
            int lb = LookupOrAdd(mesh, dedup[stratum], ib, pos, nor, uv);
            int lc = LookupOrAdd(mesh, dedup[stratum], ic, pos, nor, uv);
            mesh.Faces.Add((la, lb, lc));
        }
    }

    private static int LookupOrAdd(StratumMesh mesh, Dictionary<long, int> dedup,
                                   int gridIdx, Vector3[] pos, Vector3[] nor, Vector2[] uv)
    {
        if (dedup.TryGetValue(gridIdx, out int local)) return local;
        local = mesh.Positions.Count;
        mesh.Positions.Add(pos[gridIdx]);
        mesh.Normals.Add(nor[gridIdx]);
        mesh.Uvs.Add(uv[gridIdx]);
        dedup[gridIdx] = local;
        return local;
    }

    private static StratumMesh? BuildWaterSurfaceMesh(Heightmap2D hm, WaterMask mask, int n, float size, float heightScale, bool flatShade)
    {
        if (!mask.HasAnyWater) return null;

        var mesh = new StratumMesh { FlatShade = flatShade };
        float worldHalf = size * 0.5f;
        float cellWorld = size / (n - 1);

        // Build per-grid water vertices ONLY for wet cells. We use a separate dedup
        // map keyed on grid index.
        var dedup = new Dictionary<int, int>();

        Vector3 PosAt(int x, int z)
        {
            int i = z * n + x;
            float wx = -worldHalf + x * cellWorld;
            float wz = -worldHalf + z * cellWorld;
            float wy = mask.WaterLevel[i] * heightScale;
            return new Vector3(wx, wy, wz);
        }

        Vector2 UvAt(int x, int z) => new((float)x / (n - 1), (float)z / (n - 1));

        for (int z = 0; z < n - 1; z++)
        for (int x = 0; x < n - 1; x++)
        {
            int i00 = z * n + x;
            int i10 = z * n + (x + 1);
            int i01 = (z + 1) * n + x;
            int i11 = (z + 1) * n + (x + 1);

            bool w00 = mask.Kind[i00] != WaterKind.None;
            bool w10 = mask.Kind[i10] != WaterKind.None;
            bool w01 = mask.Kind[i01] != WaterKind.None;
            bool w11 = mask.Kind[i11] != WaterKind.None;

            if (w00 && w10 && w11)
                AppendTri(mesh, dedup, x, z, x + 1, z, x + 1, z + 1, PosAt, UvAt, flatShade);
            if (w00 && w11 && w01)
                AppendTri(mesh, dedup, x, z, x + 1, z + 1, x, z + 1, PosAt, UvAt, flatShade);
        }

        return mesh.Faces.Count > 0 ? mesh : null;
    }

    private static void AppendTri(StratumMesh mesh, Dictionary<int, int> dedup,
        int xa, int za, int xb, int zb, int xc, int zc,
        Func<int, int, Vector3> pos, Func<int, int, Vector2> uv, bool flatShade)
    {
        Vector3 pa = pos(xa, za), pb = pos(xb, zb), pc = pos(xc, zc);
        Vector3 normalUp = new(0f, 1f, 0f);

        if (flatShade)
        {
            int va = mesh.Positions.Count;
            mesh.Positions.Add(pa); mesh.Normals.Add(normalUp); mesh.Uvs.Add(uv(xa, za));
            mesh.Positions.Add(pb); mesh.Normals.Add(normalUp); mesh.Uvs.Add(uv(xb, zb));
            mesh.Positions.Add(pc); mesh.Normals.Add(normalUp); mesh.Uvs.Add(uv(xc, zc));
            mesh.Faces.Add((va, va + 1, va + 2));
        }
        else
        {
            int la = LookupOrAddWater(mesh, dedup, xa, za, pos, uv);
            int lb = LookupOrAddWater(mesh, dedup, xb, zb, pos, uv);
            int lc = LookupOrAddWater(mesh, dedup, xc, zc, pos, uv);
            mesh.Faces.Add((la, lb, lc));
        }
    }

    private static int LookupOrAddWater(StratumMesh mesh, Dictionary<int, int> dedup,
        int x, int z, Func<int, int, Vector3> pos, Func<int, int, Vector2> uv)
    {
        // Encode (x,z) into a single key — z is bounded by mesh resolution which
        // never exceeds 1024, so 16 bits each is plenty.
        int key = (z << 16) | x;
        if (dedup.TryGetValue(key, out int local)) return local;
        local = mesh.Positions.Count;
        mesh.Positions.Add(pos(x, z));
        mesh.Normals.Add(new Vector3(0f, 1f, 0f));
        mesh.Uvs.Add(uv(x, z));
        dedup[key] = local;
        return local;
    }
}
