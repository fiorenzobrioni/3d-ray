using System.Numerics;
using RayTracer.Geometry;
using RayTracer.Geometry.Subdivision;
using Xunit;
using Xunit.Abstractions;

namespace RayTracer.Tests.Validation;

/// <summary>
/// Subdivision against its limit surface (PIANO-REVIEW.md Phase 2). The
/// limit point of a vertex, and its normal, do not change with further
/// subdivision; the limit masks give them directly. So projecting after one
/// level and after four must put every vertex of the first level at the same
/// point with the same normal: smooth vertices of every valence, boundaries,
/// corners and creases. A mask that did not match the rules (wrong
/// eigenvector) would move. Then: a fully creased cube stays a cube, sharper
/// creases come closer to it, and corners stay put.
/// </summary>
public class SubdivisionLimitTests
{
    private readonly ITestOutputHelper _out;
    public SubdivisionLimitTests(ITestOutputHelper output) => _out = output;

    private static PolyMesh Copy(PolyMesh m)
    {
        var c = new PolyMesh();
        c.Positions.AddRange(m.Positions);
        foreach (var f in m.FacePositions) c.FacePositions.Add((int[])f.Clone());
        if (m.Creases != null) c.Creases = new Dictionary<long, float>(m.Creases);
        return c;
    }

    private static PolyMesh Perturbed(PolyMesh m, int seed, float amount)
    {
        var rng = new Random(seed);
        for (int i = 0; i < m.Positions.Count; i++)
            m.Positions[i] += amount * new Vector3(rng.NextSingle() - 0.5f, rng.NextSingle() - 0.5f, rng.NextSingle() - 0.5f);
        return m;
    }

    private static PolyMesh Cube()
    {
        var m = new PolyMesh();
        for (int i = 0; i < 8; i++) m.Positions.Add(new Vector3((i & 1) * 2 - 1, ((i >> 1) & 1) * 2 - 1, ((i >> 2) & 1) * 2 - 1));
        m.FacePositions.Add(new[] { 0, 2, 3, 1 });
        m.FacePositions.Add(new[] { 4, 5, 7, 6 });
        m.FacePositions.Add(new[] { 0, 4, 6, 2 });
        m.FacePositions.Add(new[] { 1, 3, 7, 5 });
        m.FacePositions.Add(new[] { 0, 1, 5, 4 });
        m.FacePositions.Add(new[] { 2, 6, 7, 3 });
        return m;
    }

    /// <summary>A pyramid on a pentagon: valence 5 at the apex and at the base's face point.</summary>
    private static PolyMesh Pyramid()
    {
        var m = new PolyMesh();
        for (int i = 0; i < 5; i++) m.Positions.Add(new Vector3(MathF.Cos(2 * MathF.PI * i / 5), 0, MathF.Sin(2 * MathF.PI * i / 5)));
        m.Positions.Add(new Vector3(0, 1.3f, 0));
        m.FacePositions.Add(new[] { 0, 1, 2, 3, 4 });
        for (int i = 0; i < 5; i++) m.FacePositions.Add(new[] { 5, (i + 1) % 5, i });
        return m;
    }

    private static PolyMesh Icosahedron()
    {
        var tris = GeometryAuditTests.Icosphere(1f, 0, out _);
        var m = new PolyMesh();
        var index = new Dictionary<Vector3, int>();
        int Id(Vector3 p) { if (!index.TryGetValue(p, out int i)) { i = m.Positions.Count; m.Positions.Add(p); index[p] = i; } return i; }
        foreach (var t in tris) m.FacePositions.Add(new[] { Id(t.V0), Id(t.V1), Id(t.V2) });
        return m;
    }

    /// <summary>The octahedron's top half with a vertex of valence 3 added: valences 3 to 6.</summary>
    private static PolyMesh Irregular()
    {
        var m = Icosahedron();
        // Split one face at its centroid: three valence-3 vertices' worth of irregularity.
        int[] f = m.FacePositions[0];
        m.Positions.Add((m.Positions[f[0]] + m.Positions[f[1]] + m.Positions[f[2]]) / 3f * 1.05f);
        int c = m.Positions.Count - 1;
        m.FacePositions[0] = new[] { f[0], f[1], c };
        m.FacePositions.Add(new[] { f[1], f[2], c });
        m.FacePositions.Add(new[] { f[2], f[0], c });
        return m;
    }

    /// <summary>An open sheet of n×n quads (or triangles), with a hump.</summary>
    private static PolyMesh Sheet(int n, bool triangles)
    {
        var m = new PolyMesh();
        for (int j = 0; j <= n; j++)
            for (int i = 0; i <= n; i++)
            {
                float x = i / (float)n - 0.5f, z = j / (float)n - 0.5f;
                m.Positions.Add(new Vector3(x, 0.4f * MathF.Exp(-8f * (x * x + z * z)), z));
            }
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int a = j * (n + 1) + i, b = a + 1, c = a + n + 2, d = a + n + 1;
                if (triangles) { m.FacePositions.Add(new[] { a, d, c }); m.FacePositions.Add(new[] { a, c, b }); }
                else m.FacePositions.Add(new[] { a, d, c, b });
            }
        return m;
    }

    public static TheoryData<string> Meshes => new()
    {
        "cc-cube", "cc-pyramid", "cc-sheet", "cc-sheet-edge-only", "cc-crease",
        "loop-icosahedron", "loop-irregular", "loop-sheet", "loop-sheet-edge-only", "loop-crease",
    };

    private static (PolyMesh Mesh, SubdivisionScheme Scheme, BoundaryInterpolation Boundary) Build(string name)
    {
        var edgeOnly = name.EndsWith("edge-only") ? BoundaryInterpolation.EdgeOnly : BoundaryInterpolation.EdgeAndCorner;
        PolyMesh m = name switch
        {
            "cc-cube" => Perturbed(Cube(), 1, 0.3f),
            "cc-pyramid" => Perturbed(Pyramid(), 2, 0.2f),
            "cc-sheet" or "cc-sheet-edge-only" => Perturbed(Sheet(4, false), 3, 0.05f),
            "cc-crease" => Perturbed(Cube(), 4, 0.3f),
            "loop-icosahedron" => Perturbed(Icosahedron(), 5, 0.2f),
            "loop-irregular" => Perturbed(Irregular(), 6, 0.1f),
            "loop-sheet" or "loop-sheet-edge-only" => Perturbed(Sheet(4, true), 7, 0.05f),
            "loop-crease" => Perturbed(Icosahedron(), 8, 0.1f),
            _ => throw new ArgumentException(name),
        };
        if (name.EndsWith("crease"))
        {
            // A closed chain of sharp edges (a crease curve with crease
            // vertices along it) and one sharp edge ending in the open (darts
            // at its ends are not on it).
            var topo = new SubdivisionTopology(m, BoundaryInterpolation.EdgeAndCorner);
            m.Creases = new Dictionary<long, float>();
            int[] loop = name.StartsWith("cc") ? new[] { 0, 1, 3, 2 } : FaceLoop(m);
            for (int i = 0; i < loop.Length; i++)
                m.Creases[SubdivisionTopology.EdgeKey(loop[i], loop[(i + 1) % loop.Length])] = float.PositiveInfinity;
            Assert.All(m.Creases.Keys, k => Assert.True(topo.Edges.ContainsKey(k)));
        }
        return (m, name.StartsWith("cc") ? SubdivisionScheme.CatmullClark : SubdivisionScheme.Loop, edgeOnly);
    }

    // The ring of edges around a vertex of the icosahedron (a closed crease of 5 vertices).
    private static int[] FaceLoop(PolyMesh m)
    {
        var topo = new SubdivisionTopology(m, BoundaryInterpolation.EdgeAndCorner);
        var ring = LimitSurface.OrderedFaces(m, 0, topo, LimitSurface.DirectedEdges(m))!;
        return ring.Select(r => m.FacePositions[r.Face][(r.K + 1) % 3]).ToArray();
    }

    private static PolyMesh Subdivided(PolyMesh m, SubdivisionScheme scheme, BoundaryInterpolation boundary, int levels)
    {
        var c = Copy(m);
        if (scheme == SubdivisionScheme.CatmullClark) CatmullClarkSubdivider.Subdivide(c, levels, boundary);
        else LoopSubdivider.Subdivide(c, levels, boundary);
        LimitSurface.Project(c, scheme, boundary);
        return c;
    }

    [Theory]
    [MemberData(nameof(Meshes))]
    public void LimitPointsAndNormals_DoNotMoveWithFurtherSubdivision(string name)
    {
        var (m, scheme, boundary) = Build(name);
        var coarse = Subdivided(m, scheme, boundary, 1);
        var fine = Subdivided(m, scheme, boundary, 4);
        var topo = new SubdivisionTopology(coarse, boundary);

        float worstP = 0f, worstN = 0f;
        int worstV = -1;
        for (int v = 0; v < coarse.Positions.Count; v++)
        {
            float dp = (coarse.Positions[v] - fine.Positions[v]).Length();
            if (dp > worstP) { worstP = dp; worstV = v; }
            // Normals only where the surface has one: not on a crease or a corner.
            if (topo.Rules[v] == VertexRule.Smooth)
                worstN = MathF.Max(worstN, (coarse.LimitNormals![v] - fine.LimitNormals![v]).Length());
        }
        _out.WriteLine($"{name}: {coarse.Positions.Count} vertices, largest move {worstP:E2} (vertex {worstV}, {topo.Rules[Math.Max(worstV, 0)]}), normal {worstN:E2}");
        Assert.True(worstP < 2e-5f, $"limit point moved by {worstP} at vertex {worstV}");
        Assert.True(worstN < 2e-4f, $"limit normal moved by {worstN}");
    }

    [Theory]
    [MemberData(nameof(Meshes))]
    public void LimitNormals_AreThoseOfTheFineSurface(string name)
    {
        // The limit normal at a vertex of level 1 against the normal of the
        // fine mesh around it four levels later (its faces are then 1/16 the
        // size, close to the tangent plane).
        var (m, scheme, boundary) = Build(name);
        var coarse = Subdivided(m, scheme, boundary, 1);
        var fine = Subdivided(m, scheme, boundary, 5);
        var fineFaces = LimitSurface.AngleWeightedNormals(fine);
        var topo = new SubdivisionTopology(coarse, boundary);
        float worst = 0f;
        for (int v = 0; v < coarse.Positions.Count; v++)
        {
            if (topo.Rules[v] != VertexRule.Smooth) continue;
            float angle = MathF.Acos(Math.Clamp(Vector3.Dot(coarse.LimitNormals![v], fineFaces[v]), -1f, 1f));
            worst = MathF.Max(worst, angle);
        }
        _out.WriteLine($"{name}: largest angle to the fine mesh's normal {worst * 180 / MathF.PI:F3}°");
        Assert.True(worst < 1.5f * MathF.PI / 180f);
    }

    [Fact]
    public void FullyCreasedCube_StaysACube_WithFlatFaces()
    {
        var m = Cube();
        var opts = new SubdivisionOptions
        {
            Scheme = SubdivisionScheme.CatmullClark, Iterations = 3, MaxIterations = 6,
            CreaseAngle = 45f, CreaseSharpness = float.PositiveInfinity,
        };
        SubdivisionEngine.Apply(m, opts);
        foreach (var p in m.Positions)
        {
            float a = MathF.Max(MathF.Abs(p.X), MathF.Max(MathF.Abs(p.Y), MathF.Abs(p.Z)));
            Assert.InRange(a, 1f - 1e-5f, 1f + 1e-5f);
        }
        var tris = SubdivisionEngine.Triangulate(m, new RayTracer.Materials.Lambertian(Vector3.One));
        foreach (SmoothTriangle t in tris)
        {
            Vector3 face = Vector3.Normalize(Vector3.Cross(t.V1 - t.V0, t.V2 - t.V0));
            foreach (var n in new[] { t.N0, t.N1, t.N2 })
                Assert.True(Vector3.Dot(n, face) > 0.99999f, $"shading normal {n} on a face of normal {face}");
        }
    }

    [Fact]
    public void SharperCreases_ComeCloserToTheCube()
    {
        float Extent(float sharpness)
        {
            var m = Cube();
            SubdivisionEngine.Apply(m, new SubdivisionOptions
            {
                Scheme = SubdivisionScheme.CatmullClark, Iterations = 4, MaxIterations = 6,
                CreaseAngle = sharpness > 0 ? 45f : 0f, CreaseSharpness = sharpness,
            });
            return m.Positions.Max(p => p.X + p.Y + p.Z); // toward the corner (1, 1, 1)
        }
        float[] s = { 0f, 0.5f, 1f, 2f, 3f, float.PositiveInfinity };
        var extents = s.Select(Extent).ToArray();
        _out.WriteLine(string.Join(", ", s.Zip(extents, (a, b) => $"{a}: {b:F4}")));
        for (int i = 1; i < extents.Length; i++) Assert.True(extents[i] > extents[i - 1] + 1e-4f);
        Assert.Equal(3f, extents[^1], 4);
    }

    [Theory]
    [InlineData(SubdivisionScheme.CatmullClark)]
    [InlineData(SubdivisionScheme.Loop)]
    public void SheetCorners_StayPut_WithEdgeAndCorner(SubdivisionScheme scheme)
    {
        // A corner is a boundary vertex with a single face: all four of the
        // quad sheet's, two of the triangulated one's (the other two have the
        // diagonal, two faces, and round off along the boundary).
        var m = Sheet(3, scheme == SubdivisionScheme.Loop);
        int[] idx = scheme == SubdivisionScheme.Loop ? new[] { 3, 12 } : new[] { 0, 3, 12, 15 };
        var corners = idx.Select(i => m.Positions[i]).ToArray();
        var keep = Subdivided(m, scheme, BoundaryInterpolation.EdgeAndCorner, 3);
        var round = Subdivided(m, scheme, BoundaryInterpolation.EdgeOnly, 3);
        for (int i = 0; i < idx.Length; i++)
        {
            Assert.True((keep.Positions[idx[i]] - corners[i]).Length() < 1e-6f);
            Assert.True((round.Positions[idx[i]] - corners[i]).Length() > 1e-2f);
        }
    }
}
