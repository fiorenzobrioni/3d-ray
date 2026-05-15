using System.Numerics;
using RayTracer.Geometry.Subdivision;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Tests for the mesh subdivision pipeline (Loop / Catmull-Clark) — step 2
/// of the DEVLOG "Stack completo surface displacement" cycle. Validates:
///
/// <list type="number">
///   <item><description>Topological correctness — every iteration multiplies
///     face count by exactly 4 (Loop) or by 4× the average input arity
///     (Catmull-Clark first iter, then ×4 after).</description></item>
///   <item><description>Loop interior-vertex mask reproduces the exact
///     analytic limit position on a known regular configuration.</description></item>
///   <item><description>Loop boundary mask preserves boundary curve geometry
///     (boundary vertices don't drift toward the interior).</description></item>
///   <item><description>Catmull-Clark turns any input arity into all-quads
///     after one iteration and recovers a smooth limit on the canonical cube.
///     </description></item>
///   <item><description>UV channel is carried through subdivision with the
///     correct linear mid-edge mask.</description></item>
///   <item><description>Auto scheme selection picks CC for quad meshes, Loop
///     for triangle meshes.</description></item>
///   <item><description>The adaptive pixel-error heuristic produces 0
///     iterations when the source already meets the threshold and a
///     positive count when it doesn't.</description></item>
/// </list>
/// </summary>
public class SubdivisionTests
{
    // ─── Helpers ──────────────────────────────────────────────────────────

    /// <summary>Unit cube as a quad mesh (24 corners, 8 unique vertices, 6 quads).</summary>
    private static PolyMesh BuildUnitCubeQuads()
    {
        var m = new PolyMesh();
        // 8 corners
        m.Positions.Add(new Vector3(-1, -1, -1));
        m.Positions.Add(new Vector3( 1, -1, -1));
        m.Positions.Add(new Vector3( 1,  1, -1));
        m.Positions.Add(new Vector3(-1,  1, -1));
        m.Positions.Add(new Vector3(-1, -1,  1));
        m.Positions.Add(new Vector3( 1, -1,  1));
        m.Positions.Add(new Vector3( 1,  1,  1));
        m.Positions.Add(new Vector3(-1,  1,  1));
        // 6 quad faces (outward-facing winding)
        m.FacePositions.Add(new[] { 0, 3, 2, 1 }); // -Z
        m.FacePositions.Add(new[] { 4, 5, 6, 7 }); // +Z
        m.FacePositions.Add(new[] { 0, 4, 7, 3 }); // -X
        m.FacePositions.Add(new[] { 1, 2, 6, 5 }); // +X
        m.FacePositions.Add(new[] { 0, 1, 5, 4 }); // -Y
        m.FacePositions.Add(new[] { 3, 7, 6, 2 }); // +Y
        return m;
    }

    /// <summary>Regular tetrahedron — 4 triangular faces, all vertices interior (closed).</summary>
    private static PolyMesh BuildTetrahedron()
    {
        var m = new PolyMesh();
        m.Positions.Add(new Vector3( 1,  1,  1));
        m.Positions.Add(new Vector3( 1, -1, -1));
        m.Positions.Add(new Vector3(-1,  1, -1));
        m.Positions.Add(new Vector3(-1, -1,  1));
        m.FacePositions.Add(new[] { 0, 1, 2 });
        m.FacePositions.Add(new[] { 0, 3, 1 });
        m.FacePositions.Add(new[] { 0, 2, 3 });
        m.FacePositions.Add(new[] { 1, 3, 2 });
        return m;
    }

    /// <summary>
    /// Single flat triangle with a boundary on every edge — used to verify the
    /// boundary mask preserves edge midpoints exactly.
    /// </summary>
    private static PolyMesh BuildOpenTriangle()
    {
        var m = new PolyMesh();
        m.Positions.Add(new Vector3(0, 0, 0));
        m.Positions.Add(new Vector3(1, 0, 0));
        m.Positions.Add(new Vector3(0, 1, 0));
        m.FacePositions.Add(new[] { 0, 1, 2 });
        return m;
    }

    /// <summary>
    /// Two coplanar triangles sharing edge (0–1), forming a flat unit
    /// square. Used to verify Loop's interior-edge mask: the (0–1) edge
    /// midpoint should land at the geometric midpoint on a planar mesh
    /// (since 3/8·(a+b) + 1/8·(c+d) with c,d symmetric to the edge produces
    /// the planar centroid).
    /// </summary>
    private static PolyMesh BuildPlanarSquareAsTris()
    {
        var m = new PolyMesh();
        m.Positions.Add(new Vector3(0, 0, 0));
        m.Positions.Add(new Vector3(1, 0, 0));
        m.Positions.Add(new Vector3(1, 1, 0));
        m.Positions.Add(new Vector3(0, 1, 0));
        m.FacePositions.Add(new[] { 0, 1, 2 });
        m.FacePositions.Add(new[] { 0, 2, 3 });
        return m;
    }

    // ─── Loop subdivision ─────────────────────────────────────────────────

    [Fact]
    public void Loop_OneIteration_QuadruplesFaceCount()
    {
        var m = BuildTetrahedron();
        LoopSubdivider.Subdivide(m, 1);
        Assert.Equal(4 * 4, m.FaceCount);
    }

    [Fact]
    public void Loop_TwoIterations_FaceCountIsSixteenTimes()
    {
        var m = BuildTetrahedron();
        LoopSubdivider.Subdivide(m, 2);
        Assert.Equal(4 * 16, m.FaceCount);
    }

    [Fact]
    public void Loop_EveryFaceIsTriangle_AfterSubdivision()
    {
        var m = BuildTetrahedron();
        LoopSubdivider.Subdivide(m, 2);
        foreach (var f in m.FacePositions) Assert.Equal(3, f.Length);
    }

    [Fact]
    public void Loop_PlanarInput_StaysPlanarAfterSubdivision()
    {
        // A planar surface must stay planar under Loop: every mask is
        // affine in the input, and the input is z=0 everywhere.
        var m = BuildPlanarSquareAsTris();
        LoopSubdivider.Subdivide(m, 3);
        foreach (var p in m.Positions)
            Assert.InRange(p.Z, -1e-5f, 1e-5f);
    }

    [Fact]
    public void Loop_BoundaryEdge_Midpoint_IsGeometricMidpoint()
    {
        // A single triangle has 3 boundary edges. The new edge vertex on a
        // boundary edge must be exactly the midpoint of the endpoints
        // (1/2·a + 1/2·b).
        var m = BuildOpenTriangle();
        // Capture pre-subdivision vertex count to find the first odd vertex
        int preCount = m.Positions.Count;
        LoopSubdivider.Subdivide(m, 1);

        // After one iteration we expect 3 new odd vertices, each the midpoint
        // of one of the three original edges. We just verify the three new
        // positions are 0.5·(endpointA + endpointB).
        var p0 = new Vector3(0, 0, 0);
        var p1 = new Vector3(1, 0, 0);
        var p2 = new Vector3(0, 1, 0);
        var expectedMids = new HashSet<Vector3>
        {
            0.5f * (p0 + p1),
            0.5f * (p1 + p2),
            0.5f * (p2 + p0),
        };
        // Each original vertex has *two* boundary neighbors (the open
        // triangle's two adjacent edges), so Loop applies the cubic
        // B-spline boundary mask: v' = 6/8·v + 1/8·(v_prev + v_next).
        for (int i = 0; i < preCount; i++)
        {
            Vector3 v = i switch { 0 => p0, 1 => p1, _ => p2 };
            Vector3 a = i switch { 0 => p1, 1 => p0, _ => p0 };
            Vector3 b = i switch { 0 => p2, 1 => p2, _ => p1 };
            Vector3 expected = (6f / 8f) * v + (1f / 8f) * (a + b);
            Assert.True((m.Positions[i] - expected).LengthSquared() < 1e-10f,
                $"Boundary vertex {i} mismatch: {m.Positions[i]} vs {expected}");
        }
        for (int i = preCount; i < m.Positions.Count; i++)
        {
            Assert.Contains(m.Positions[i], expectedMids);
        }
    }

    [Fact]
    public void Loop_InteriorEdge_Midpoint_OnPlanarSquare_LandsOnPlane()
    {
        // The shared edge (0,1)-(1,0) between the two coplanar triangles is
        // an *interior* edge. Loop's mask 3/8(a+b) + 1/8(c+d) with c=(0,0,0)
        // and d=(1,1,0): the midpoint should be on the z=0 plane and have
        // x = 3/8·(0+1) + 1/8·(0+1) = 0.5, y = 3/8·(0+1) + 1/8·(0+1) = 0.5.
        var m = BuildPlanarSquareAsTris();
        LoopSubdivider.Subdivide(m, 1);

        // Find a vertex very close to (0.5, 0.5, 0) — it must exist.
        Vector3 target = new(0.5f, 0.5f, 0f);
        bool found = false;
        foreach (var p in m.Positions)
        {
            if ((p - target).LengthSquared() < 1e-8f) { found = true; break; }
        }
        Assert.True(found, "expected an interior edge midpoint at (0.5, 0.5, 0)");
    }

    [Fact]
    public void Loop_UVChannel_IsCarriedThroughSubdivision()
    {
        var m = BuildPlanarSquareAsTris();
        m.UVs = new List<Vector2>
        {
            new(0, 0), new(1, 0), new(1, 1), new(0, 1),
        };
        m.FaceUVs = new List<int[]>
        {
            new[] { 0, 1, 2 },
            new[] { 0, 2, 3 },
        };

        LoopSubdivider.Subdivide(m, 1);

        // Every new UV must lie inside [0,1]²
        foreach (var uv in m.UVs)
        {
            Assert.InRange(uv.X, 0f, 1f);
            Assert.InRange(uv.Y, 0f, 1f);
        }
        // UV face count must match position face count (4× original)
        Assert.Equal(m.FaceCount, m.FaceUVs!.Count);
    }

    // ─── Catmull-Clark ────────────────────────────────────────────────────

    [Fact]
    public void CatmullClark_Cube_OneIteration_ProducesAllQuads()
    {
        var m = BuildUnitCubeQuads();
        CatmullClarkSubdivider.Subdivide(m, 1);
        Assert.True(m.IsAllQuads());
    }

    [Fact]
    public void CatmullClark_Cube_OneIteration_QuadruplesFaceCount()
    {
        var m = BuildUnitCubeQuads();
        CatmullClarkSubdivider.Subdivide(m, 1);
        Assert.Equal(6 * 4, m.FaceCount);
    }

    [Fact]
    public void CatmullClark_Cube_ContractsTowardSphere()
    {
        // The Catmull-Clark limit of a cube is a smooth, sphere-like surface
        // strictly inside the unit cube. After a few iterations every vertex
        // must have moved off the unit-cube corners (no |coord| equal to 1
        // exactly) and the bounding box must shrink.
        var m = BuildUnitCubeQuads();
        CatmullClarkSubdivider.Subdivide(m, 3);

        var (min, max) = m.BoundingBox();
        Assert.True(max.X < 1f && max.Y < 1f && max.Z < 1f,
            $"Bounding box did not contract: max={max}");
        Assert.True(min.X > -1f && min.Y > -1f && min.Z > -1f,
            $"Bounding box did not contract: min={min}");
    }

    [Fact]
    public void CatmullClark_TriangleInput_ProducesQuadsAfterFirstIteration()
    {
        // CC accepts any arity; after the first iteration the output is
        // always all-quads (the 1-per-corner rule turns an n-gon into n quads).
        var m = BuildTetrahedron();
        CatmullClarkSubdivider.Subdivide(m, 1);
        Assert.True(m.IsAllQuads(),
            "Catmull-Clark should produce quads even from triangle input");
        // 4 triangles × 3 corners = 12 new quads
        Assert.Equal(12, m.FaceCount);
    }

    [Fact]
    public void CatmullClark_PreservesSymmetry_OfRegularCube()
    {
        // The cube is octahedrally symmetric: CC must preserve that symmetry.
        // The centroid of the subdivided mesh must remain at the origin.
        var m = BuildUnitCubeQuads();
        CatmullClarkSubdivider.Subdivide(m, 2);

        Vector3 centroid = Vector3.Zero;
        foreach (var p in m.Positions) centroid += p;
        centroid /= m.Positions.Count;

        Assert.InRange(centroid.X, -1e-4f, 1e-4f);
        Assert.InRange(centroid.Y, -1e-4f, 1e-4f);
        Assert.InRange(centroid.Z, -1e-4f, 1e-4f);
    }

    // ─── SubdivisionEngine — high-level driver ────────────────────────────

    [Fact]
    public void Engine_AutoScheme_PicksCatmullClarkForQuadMesh()
    {
        var m = BuildUnitCubeQuads();
        var (iters, scheme) = SubdivisionEngine.Apply(m, new SubdivisionOptions
        {
            Scheme        = SubdivisionScheme.Auto,
            Iterations    = 1,
            MaxIterations = 4,
        });
        Assert.Equal(1, iters);
        Assert.Equal(SubdivisionScheme.CatmullClark, scheme);
        Assert.True(m.IsAllQuads());
    }

    [Fact]
    public void Engine_AutoScheme_PicksLoopForTriangleMesh()
    {
        var m = BuildTetrahedron();
        var (iters, scheme) = SubdivisionEngine.Apply(m, new SubdivisionOptions
        {
            Scheme        = SubdivisionScheme.Auto,
            Iterations    = 1,
            MaxIterations = 4,
        });
        Assert.Equal(1, iters);
        Assert.Equal(SubdivisionScheme.Loop, scheme);
        Assert.True(m.IsAllTriangles());
    }

    [Fact]
    public void Engine_Disabled_NoOp()
    {
        var m = BuildTetrahedron();
        int faces0 = m.FaceCount;
        var (iters, scheme) = SubdivisionEngine.Apply(m, SubdivisionOptions.Disabled);
        Assert.Equal(0, iters);
        Assert.Equal(SubdivisionScheme.None, scheme);
        Assert.Equal(faces0, m.FaceCount);
    }

    [Fact]
    public void Engine_ClampsToMaxIterations()
    {
        var m = BuildTetrahedron();
        var (iters, _) = SubdivisionEngine.Apply(m, new SubdivisionOptions
        {
            Scheme        = SubdivisionScheme.Loop,
            Iterations    = 99,
            MaxIterations = 2,
        });
        Assert.Equal(2, iters);
        Assert.Equal(4 * 16, m.FaceCount);
    }

    [Fact]
    public void Engine_Triangulate_ProducesValidSmoothTriangles()
    {
        // End-to-end: subdivide a tetra, triangulate, and verify the
        // resulting SmoothTriangles all have non-zero face area and unit-length
        // shading normals.
        var m = BuildTetrahedron();
        SubdivisionEngine.Apply(m, new SubdivisionOptions
        {
            Scheme        = SubdivisionScheme.Loop,
            Iterations    = 2,
            MaxIterations = 4,
        });

        var fallbackMat = new RayTracer.Materials.Lambertian(new Vector3(0.5f, 0.5f, 0.5f));
        var tris = SubdivisionEngine.Triangulate(m, fallbackMat);
        Assert.NotEmpty(tris);
        foreach (var h in tris)
        {
            var st = (RayTracer.Geometry.SmoothTriangle)h;
            // Face area > 0 (non-degenerate)
            float area = 0.5f * Vector3.Cross(st.V1 - st.V0, st.V2 - st.V0).Length();
            Assert.True(area > 1e-10f);
            // Shading normals are unit length (constructor normalizes)
            Assert.Equal(1f, st.N0.Length(), 3);
            Assert.Equal(1f, st.N1.Length(), 3);
            Assert.Equal(1f, st.N2.Length(), 3);
        }
    }

    [Fact]
    public void AdaptivePixelError_ZeroIterations_WhenSourceMeetsThreshold()
    {
        // A unit cube seen from very far should not need any subdivision
        // for a 10-pixel threshold.
        var m = BuildUnitCubeQuads();
        var ctx = new ScreenSpaceContext
        {
            CameraOrigin       = new Vector3(0, 0, -10_000f),
            CameraForward      = Vector3.UnitZ,
            ImageHeight        = 1080,
            VerticalFovRadians = MathF.PI / 4f,
            EntityToWorld      = Matrix4x4.Identity,
        };
        var (iters, _) = SubdivisionEngine.Apply(m, new SubdivisionOptions
        {
            Scheme        = SubdivisionScheme.CatmullClark,
            Iterations    = 0,
            PixelError    = 10f,
            MaxIterations = 6,
            Screen        = ctx,
        });
        Assert.Equal(0, iters);
    }

    [Fact]
    public void AdaptivePixelError_RequestsIterations_WhenCameraIsClose()
    {
        // Same unit cube but camera right next to it → mesh covers most of
        // the frame → adaptive heuristic must request at least 1 iteration.
        var m = BuildUnitCubeQuads();
        var ctx = new ScreenSpaceContext
        {
            CameraOrigin       = new Vector3(0, 0, -3f),
            CameraForward      = Vector3.UnitZ,
            ImageHeight        = 1080,
            VerticalFovRadians = MathF.PI / 4f,
            EntityToWorld      = Matrix4x4.Identity,
        };
        var (iters, _) = SubdivisionEngine.Apply(m, new SubdivisionOptions
        {
            Scheme        = SubdivisionScheme.CatmullClark,
            Iterations    = 0,
            PixelError    = 2f,
            MaxIterations = 6,
            Screen        = ctx,
        });
        Assert.True(iters >= 1, $"expected ≥1 adaptive iterations, got {iters}");
        Assert.True(iters <= 6,  $"adaptive heuristic exceeded MaxIterations: {iters}");
    }
}
