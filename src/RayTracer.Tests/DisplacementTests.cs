using System.Numerics;
using RayTracer.Geometry.Subdivision;
using RayTracer.Textures;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Tests for <see cref="DisplacementEngine"/> — step 3 of the DEVLOG
/// "Stack completo surface displacement" cycle. Validates:
///
/// <list type="number">
///   <item><description>Disabled options are a no-op (positions unchanged).</description></item>
///   <item><description>Uniform-height texture shifts every vertex by
///     <c>scale·(h−midlevel)</c> along its smooth normal.</description></item>
///   <item><description>Midlevel = h produces zero net displacement.</description></item>
///   <item><description>Negative scale produces inward displacement (Arnold/RM convention).</description></item>
///   <item><description><c>maxDisplacement</c> is reported correctly for warning logic.</description></item>
///   <item><description>The displacement direction matches the angle-weighted
///     smooth normal (planar surfaces shift exactly along the face normal).</description></item>
///   <item><description>Per-vertex UV sampling picks the first incident
///     corner — gradient textures displace the right vertex.</description></item>
/// </list>
/// </summary>
public class DisplacementTests
{
    // ─── Synthetic textures ───────────────────────────────────────────────

    /// <summary>Uniform grey: luminance = <c>level</c> everywhere.</summary>
    private sealed class ConstLevel : ITexture
    {
        private readonly Vector3 _c;
        public ConstLevel(float level) { _c = new Vector3(level); }
        public Vector3 Value(float u, float v, Vector3 p, int seed) => _c;
    }

    /// <summary>Linear U gradient — luminance increases with u in [0, 1].</summary>
    private sealed class UGradient : ITexture
    {
        public Vector3 Value(float u, float v, Vector3 p, int seed)
            => new(u, u, u);
    }

    /// <summary>Position-driven gradient on Z: useful for UV-less meshes.</summary>
    private sealed class ZGradient : ITexture
    {
        public Vector3 Value(float u, float v, Vector3 p, int seed)
            => new(p.Z, p.Z, p.Z);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Flat unit quad on the y=0 plane with the winding chosen so the
    /// angle-weighted smooth normal at every vertex is <c>+Y</c> — viewed
    /// from above (+Y), the corner order traces a counter-clockwise loop.
    /// </summary>
    private static PolyMesh BuildFlatQuad()
    {
        var m = new PolyMesh();
        m.Positions.Add(new Vector3(0, 0, 0));
        m.Positions.Add(new Vector3(0, 0, 1));
        m.Positions.Add(new Vector3(1, 0, 1));
        m.Positions.Add(new Vector3(1, 0, 0));
        m.FacePositions.Add(new[] { 0, 1, 2, 3 });

        // UVs lined up so vertex 0 carries (0,0), vertex 2 carries (1,1) —
        // matches the gradient-displacement assertions.
        m.UVs = new List<Vector2>
        {
            new(0, 0), new(0, 1), new(1, 1), new(1, 0),
        };
        m.FaceUVs = new List<int[]> { new[] { 0, 1, 2, 3 } };
        return m;
    }

    /// <summary>
    /// Flat square as two triangles, +Y-facing winding so the smooth normal
    /// at every vertex is <c>+Y</c>. No UV channel — used to exercise the
    /// position-based texture sampling fall-through.
    /// </summary>
    private static PolyMesh BuildFlatSquareTris()
    {
        var m = new PolyMesh();
        m.Positions.Add(new Vector3(0, 0, 0));
        m.Positions.Add(new Vector3(0, 0, 1));
        m.Positions.Add(new Vector3(1, 0, 1));
        m.Positions.Add(new Vector3(1, 0, 0));
        m.FacePositions.Add(new[] { 0, 1, 2 });
        m.FacePositions.Add(new[] { 0, 2, 3 });
        return m;
    }

    // ─── Tests ─────────────────────────────────────────────────────────────

    [Fact]
    public void Disabled_LeavesPositionsUntouched()
    {
        var m = BuildFlatQuad();
        var before = m.Positions.ToArray();
        float maxDisp = DisplacementEngine.Apply(m, DisplacementOptions.Disabled, 0);
        Assert.Equal(0f, maxDisp);
        for (int i = 0; i < before.Length; i++)
            Assert.Equal(before[i], m.Positions[i]);
    }

    [Fact]
    public void NullTexture_IsNoOp()
    {
        var m = BuildFlatQuad();
        var before = m.Positions.ToArray();
        // Active-looking but missing texture: should bail out silently.
        var opts = new DisplacementOptions
        {
            Texture = null, Scale = 0.5f, Midlevel = 0f, Bound = 0.5f, UvScale = 1f
        };
        float maxDisp = DisplacementEngine.Apply(m, opts, 0);
        Assert.Equal(0f, maxDisp);
        for (int i = 0; i < before.Length; i++)
            Assert.Equal(before[i], m.Positions[i]);
    }

    [Fact]
    public void UniformHeight_ShiftsEveryVertexAlongSmoothNormal()
    {
        // Flat quad on y=0 plane → smooth normal at every vertex is +Y.
        // Uniform luminance 1.0, scale 0.25, midlevel 0 → every vertex
        // moves by exactly 0.25 in +Y.
        var m = BuildFlatQuad();
        var opts = new DisplacementOptions
        {
            Texture = new ConstLevel(1f),
            Scale = 0.25f, Midlevel = 0f, Bound = 0.25f, UvScale = 1f,
        };

        float maxDisp = DisplacementEngine.Apply(m, opts, 0);

        Assert.Equal(0.25f, maxDisp, 5);
        foreach (var p in m.Positions)
        {
            Assert.Equal(0.25f, p.Y, 5);
        }
    }

    [Fact]
    public void MidlevelMatchingHeight_ProducesZeroDisplacement()
    {
        // h = 0.5 everywhere, midlevel = 0.5 → (h - mid) = 0 → no offset.
        var m = BuildFlatQuad();
        var before = m.Positions.ToArray();

        var opts = new DisplacementOptions
        {
            Texture = new ConstLevel(0.5f),
            Scale = 10f, Midlevel = 0.5f, Bound = 5f, UvScale = 1f,
        };

        float maxDisp = DisplacementEngine.Apply(m, opts, 0);

        Assert.Equal(0f, maxDisp, 5);
        for (int i = 0; i < before.Length; i++)
        {
            Assert.Equal(before[i].X, m.Positions[i].X, 5);
            Assert.Equal(before[i].Y, m.Positions[i].Y, 5);
            Assert.Equal(before[i].Z, m.Positions[i].Z, 5);
        }
    }

    [Fact]
    public void NegativeScale_DisplacesInward()
    {
        // Positive height, negative scale → inward displacement (Arnold's
        // convention: scale carries the sign of the offset).
        var m = BuildFlatQuad();
        var opts = new DisplacementOptions
        {
            Texture = new ConstLevel(1f),
            Scale = -0.3f, Midlevel = 0f, Bound = 0.3f, UvScale = 1f,
        };

        DisplacementEngine.Apply(m, opts, 0);

        foreach (var p in m.Positions)
        {
            Assert.Equal(-0.3f, p.Y, 5);
        }
    }

    [Fact]
    public void MaxDisplacementReport_TracksLargestVertexOffset()
    {
        // U gradient on a quad with UV ∈ [0,1]² → vertex at UV(1,0) gets
        // luminance 1.0, vertex at UV(0,0) gets 0.0. Max disp = scale·(1−0).
        var m = BuildFlatQuad();
        var opts = new DisplacementOptions
        {
            Texture = new UGradient(),
            Scale = 0.4f, Midlevel = 0f, Bound = 0.4f, UvScale = 1f,
        };
        float maxDisp = DisplacementEngine.Apply(m, opts, 0);
        Assert.Equal(0.4f, maxDisp, 5);
    }

    [Fact]
    public void UvLookup_PicksFirstIncidentCornerPerVertex()
    {
        // UGradient ignores p and uses u alone. With the +Y winding,
        // vertices 0 and 1 carry UV.x = 0 (no displacement) while
        // vertices 2 and 3 carry UV.x = 1 (displaced by scale).
        var m = BuildFlatQuad();
        var opts = new DisplacementOptions
        {
            Texture = new UGradient(),
            Scale = 0.5f, Midlevel = 0f, Bound = 0.5f, UvScale = 1f,
        };
        DisplacementEngine.Apply(m, opts, 0);

        Assert.Equal(0f,   m.Positions[0].Y, 5);
        Assert.Equal(0f,   m.Positions[1].Y, 5);
        Assert.Equal(0.5f, m.Positions[2].Y, 5);
        Assert.Equal(0.5f, m.Positions[3].Y, 5);
    }

    [Fact]
    public void NoUvChannel_FallsBackToPositionSampling()
    {
        // Without UVs the engine samples on 3D position. ZGradient returns
        // p.Z as luminance — vertex at z=0 gets no displacement, vertex at
        // z=1 gets scale·1.
        var m = BuildFlatSquareTris();
        var opts = new DisplacementOptions
        {
            Texture = new ZGradient(),
            Scale = 0.7f, Midlevel = 0f, Bound = 0.7f, UvScale = 1f,
        };
        DisplacementEngine.Apply(m, opts, 0);

        // Positions[0] = (0,0,0); Positions[1] = (0,0,1)
        Assert.Equal(0f,   m.Positions[0].Y, 5);
        Assert.Equal(0.7f, m.Positions[1].Y, 5);
    }

    [Fact]
    public void UvScale_Multiplies_TextureLookupCoordinates()
    {
        // UGradient sees u·uv_scale at the lookup. We build a +Y-facing quad
        // and put vertex 2 at UV(0.25, 1) (the only vertex with u ≠ 0 here).
        // With uv_scale=1 the lookup is u=0.25 → h=0.25, displacement +0.25.
        // With uv_scale=4 the lookup is u=1.00 → h=1.00, displacement +1.00.
        var m = new PolyMesh();
        m.Positions.Add(new Vector3(0, 0, 0));
        m.Positions.Add(new Vector3(0, 0, 1));
        m.Positions.Add(new Vector3(1, 0, 1));
        m.Positions.Add(new Vector3(1, 0, 0));
        m.FacePositions.Add(new[] { 0, 1, 2, 3 });
        m.UVs = new List<Vector2>
        {
            new(0, 0), new(0, 1), new(0.25f, 1), new(0.25f, 0),
        };
        m.FaceUVs = new List<int[]> { new[] { 0, 1, 2, 3 } };

        var optsRaw = new DisplacementOptions
        {
            Texture = new UGradient(), Scale = 1f, Midlevel = 0f, UvScale = 1f,
        };
        var optsScaled = new DisplacementOptions
        {
            Texture = new UGradient(), Scale = 1f, Midlevel = 0f, UvScale = 4f,
        };

        var mRaw = ClonePolyMesh(m);
        var mScaled = ClonePolyMesh(m);

        DisplacementEngine.Apply(mRaw, optsRaw, 0);
        DisplacementEngine.Apply(mScaled, optsScaled, 0);

        // Corner 2 at UV(0.25, 1): uv_scale=1 → y=0.25; uv_scale=4 → y=1.0
        Assert.Equal(0.25f, mRaw.Positions[2].Y, 5);
        Assert.Equal(1.00f, mScaled.Positions[2].Y, 5);
    }

    [Fact]
    public void EmptyMesh_DoesNotThrow()
    {
        var m = new PolyMesh();
        var opts = new DisplacementOptions
        {
            Texture = new ConstLevel(0.5f), Scale = 0.1f, Bound = 0.1f, UvScale = 1f,
        };
        float maxDisp = DisplacementEngine.Apply(m, opts, 0);
        Assert.Equal(0f, maxDisp);
    }

    [Fact]
    public void ZeroScale_TreatedAsDisabled()
    {
        var m = BuildFlatQuad();
        var before = m.Positions.ToArray();
        var opts = new DisplacementOptions
        {
            Texture = new ConstLevel(1f), Scale = 0f, Bound = 0f, UvScale = 1f,
        };
        Assert.False(opts.IsActive);
        float maxDisp = DisplacementEngine.Apply(m, opts, 0);
        Assert.Equal(0f, maxDisp);
        for (int i = 0; i < before.Length; i++)
            Assert.Equal(before[i], m.Positions[i]);
    }

    [Fact]
    public void DisplacedClosedSurface_BoundingBoxGrowsByDisplacement()
    {
        // Subdivide a cube into a quasi-sphere, then displace outward by
        // 0.2 along the smooth normal. The displaced BBox must extend
        // beyond the pre-displacement BBox by approximately the scale.
        var m = BuildUnitCubeQuads();
        CatmullClarkSubdivider.Subdivide(m, 3);
        var (preMin, preMax) = m.BoundingBox();

        var opts = new DisplacementOptions
        {
            Texture = new ConstLevel(1f),
            Scale = 0.2f, Midlevel = 0f, Bound = 0.2f, UvScale = 1f,
        };
        float maxDisp = DisplacementEngine.Apply(m, opts, 0);

        var (postMin, postMax) = m.BoundingBox();
        // Each side must have grown outward — strict inequality.
        Assert.True(postMax.X > preMax.X, $"+X side did not grow: {preMax.X} -> {postMax.X}");
        Assert.True(postMax.Y > preMax.Y, $"+Y side did not grow: {preMax.Y} -> {postMax.Y}");
        Assert.True(postMax.Z > preMax.Z, $"+Z side did not grow: {preMax.Z} -> {postMax.Z}");
        Assert.True(postMin.X < preMin.X, $"-X side did not shrink: {preMin.X} -> {postMin.X}");
        Assert.True(postMin.Y < preMin.Y, $"-Y side did not shrink: {preMin.Y} -> {postMin.Y}");
        Assert.True(postMin.Z < preMin.Z, $"-Z side did not shrink: {preMin.Z} -> {postMin.Z}");

        // maxDisp must be close to scale (heights all ≈ 1 on a uniform texture).
        Assert.Equal(0.2f, maxDisp, 5);
    }

    // ─── Helpers used by the displaced-bbox test ──────────────────────────

    private static PolyMesh BuildUnitCubeQuads()
    {
        var m = new PolyMesh();
        m.Positions.Add(new Vector3(-1, -1, -1));
        m.Positions.Add(new Vector3( 1, -1, -1));
        m.Positions.Add(new Vector3( 1,  1, -1));
        m.Positions.Add(new Vector3(-1,  1, -1));
        m.Positions.Add(new Vector3(-1, -1,  1));
        m.Positions.Add(new Vector3( 1, -1,  1));
        m.Positions.Add(new Vector3( 1,  1,  1));
        m.Positions.Add(new Vector3(-1,  1,  1));
        m.FacePositions.Add(new[] { 0, 3, 2, 1 });
        m.FacePositions.Add(new[] { 4, 5, 6, 7 });
        m.FacePositions.Add(new[] { 0, 4, 7, 3 });
        m.FacePositions.Add(new[] { 1, 2, 6, 5 });
        m.FacePositions.Add(new[] { 0, 1, 5, 4 });
        m.FacePositions.Add(new[] { 3, 7, 6, 2 });
        return m;
    }

    private static PolyMesh ClonePolyMesh(PolyMesh src)
    {
        var m = new PolyMesh();
        foreach (var p in src.Positions) m.Positions.Add(p);
        if (src.UVs != null)
        {
            m.UVs = new List<Vector2>();
            foreach (var uv in src.UVs) m.UVs.Add(uv);
        }
        foreach (var f in src.FacePositions) m.FacePositions.Add((int[])f.Clone());
        if (src.FaceUVs != null)
        {
            m.FaceUVs = new List<int[]>();
            foreach (var f in src.FaceUVs) m.FaceUVs.Add((int[])f.Clone());
        }
        return m;
    }
}
