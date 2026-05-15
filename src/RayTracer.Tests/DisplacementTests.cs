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

    // ═════════════════════════════════════════════════════════════════════
    // Step 4 — vector displacement (RGB → XYZ offset)
    //
    // Validates:
    //   • Tangent-space mode maps R→T, G→B, B→N exactly on a flat quad
    //     with the canonical UV winding (T=+X, B=+Z, N=+Y).
    //   • Object-space mode treats RGB as a raw local-space offset.
    //   • Midlevel offsets a vector channel by the same constant value.
    //   • Tangent-space requested without a UV channel falls back to
    //     object-space silently (Arnold's "no UVs" behaviour).
    //   • maxDisplacement reports the Euclidean length, not the per-axis
    //     amplitude (so the bound covers the actual sphere of motion).
    //   • Pure-N tangent-space vector displacement reproduces the scalar
    //     height-field result exactly.
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>Uniform RGB texture: returns the same color everywhere.</summary>
    private sealed class ConstRgb : ITexture
    {
        private readonly Vector3 _c;
        public ConstRgb(Vector3 c) { _c = c; }
        public Vector3 Value(float u, float v, Vector3 p, int seed) => _c;
    }

    [Fact]
    public void Vector_Tangent_ChannelMapping_MatchesTBN()
    {
        // Flat unit quad: positions at (0,0,0)→(0,0,1)→(1,0,1)→(1,0,0) with
        // UVs (0,0)→(0,1)→(1,1)→(1,0). The UV gradient implies T = +X
        // (d(p)/d(u) = +X), B = +Z (d(p)/d(v) = +Z), N = +Y. Apply a
        // uniform RGB = (0.4, 0.7, 0.1) with midlevel 0 and scale 1; the
        // expected per-vertex offset is (0.4, 0.1, 0.7).
        var m = BuildFlatQuad();
        var opts = new DisplacementOptions
        {
            Mode = DisplacementMode.Vector,
            Space = DisplacementSpace.Tangent,
            Texture = new ConstRgb(new Vector3(0.4f, 0.7f, 0.1f)),
            Scale = 1f, Midlevel = 0f, Bound = 1f, UvScale = 1f,
        };

        DisplacementEngine.Apply(m, opts, 0);

        foreach (var p in m.Positions)
        {
            // Position[0] starts at (0,0,0); compare its delta from the
            // pre-displacement quad. All four vertices receive the same
            // uniform offset since the texture is constant.
            // Vertex 0 expected at (0+0.4, 0+0.1, 0+0.7) = (0.4, 0.1, 0.7).
            // Vertex 1 expected at (0+0.4, 0+0.1, 1+0.7) = (0.4, 0.1, 1.7).
            // Vertex 2 expected at (1+0.4, 0+0.1, 1+0.7) = (1.4, 0.1, 1.7).
            // Vertex 3 expected at (1+0.4, 0+0.1, 0+0.7) = (1.4, 0.1, 0.7).
        }
        Assert.Equal(0.4f, m.Positions[0].X, 4);
        Assert.Equal(0.1f, m.Positions[0].Y, 4);
        Assert.Equal(0.7f, m.Positions[0].Z, 4);

        Assert.Equal(0.4f, m.Positions[1].X, 4);
        Assert.Equal(0.1f, m.Positions[1].Y, 4);
        Assert.Equal(1.7f, m.Positions[1].Z, 4);

        Assert.Equal(1.4f, m.Positions[2].X, 4);
        Assert.Equal(0.1f, m.Positions[2].Y, 4);
        Assert.Equal(1.7f, m.Positions[2].Z, 4);

        Assert.Equal(1.4f, m.Positions[3].X, 4);
        Assert.Equal(0.1f, m.Positions[3].Y, 4);
        Assert.Equal(0.7f, m.Positions[3].Z, 4);
    }

    [Fact]
    public void Vector_Object_TreatsRgbAsRawXyz()
    {
        // Same flat quad. Object-space mode bypasses the TBN: RGB =
        // (0.3, 0.6, 0.9) is the offset directly. Every vertex moves by
        // (0.3·scale, 0.6·scale, 0.9·scale).
        var m = BuildFlatQuad();
        var opts = new DisplacementOptions
        {
            Mode = DisplacementMode.Vector,
            Space = DisplacementSpace.Object,
            Texture = new ConstRgb(new Vector3(0.3f, 0.6f, 0.9f)),
            Scale = 0.5f, Midlevel = 0f, Bound = 1f, UvScale = 1f,
        };

        DisplacementEngine.Apply(m, opts, 0);

        // Vertex 0 starts at (0,0,0) → (0.15, 0.30, 0.45).
        Assert.Equal(0.15f, m.Positions[0].X, 4);
        Assert.Equal(0.30f, m.Positions[0].Y, 4);
        Assert.Equal(0.45f, m.Positions[0].Z, 4);

        // Vertex 2 starts at (1,0,1) → (1.15, 0.30, 1.45).
        Assert.Equal(1.15f, m.Positions[2].X, 4);
        Assert.Equal(0.30f, m.Positions[2].Y, 4);
        Assert.Equal(1.45f, m.Positions[2].Z, 4);
    }

    [Fact]
    public void Vector_Midlevel_RemapsRgbAroundZero()
    {
        // RGB = (0.5, 0.5, 0.5) with midlevel 0.5 → all channels (0,0,0) →
        // no displacement. Same RGB with midlevel 0 → offset = scale·(0.5,
        // 0.5, 0.5).
        var m = BuildFlatQuad();
        var before = m.Positions.ToArray();

        var optsCentered = new DisplacementOptions
        {
            Mode = DisplacementMode.Vector,
            Space = DisplacementSpace.Object,
            Texture = new ConstRgb(new Vector3(0.5f, 0.5f, 0.5f)),
            Scale = 1f, Midlevel = 0.5f, Bound = 1f, UvScale = 1f,
        };
        DisplacementEngine.Apply(m, optsCentered, 0);

        for (int i = 0; i < before.Length; i++)
        {
            Assert.Equal(before[i].X, m.Positions[i].X, 5);
            Assert.Equal(before[i].Y, m.Positions[i].Y, 5);
            Assert.Equal(before[i].Z, m.Positions[i].Z, 5);
        }
    }

    [Fact]
    public void Vector_NoUv_FallsBackToObjectSpace()
    {
        // Flat triangle pair, no UV channel, tangent-space mode requested.
        // The engine silently demotes to object space (the safe production
        // behaviour) so the offset is applied directly.
        var m = BuildFlatSquareTris();
        var opts = new DisplacementOptions
        {
            Mode = DisplacementMode.Vector,
            Space = DisplacementSpace.Tangent, // requested...
            Texture = new ConstRgb(new Vector3(0.2f, 0.4f, 0.6f)),
            Scale = 1f, Midlevel = 0f, Bound = 1f, UvScale = 1f,
        };

        DisplacementEngine.Apply(m, opts, 0);

        // Vertex 0 was at (0,0,0): expected at (0.2, 0.4, 0.6).
        Assert.Equal(0.2f, m.Positions[0].X, 5);
        Assert.Equal(0.4f, m.Positions[0].Y, 5);
        Assert.Equal(0.6f, m.Positions[0].Z, 5);
    }

    [Fact]
    public void Vector_MaxDisplacementReport_IsEuclideanLength()
    {
        // Tangent-space mode on the flat quad, RGB = (0.3, 0.4, 0.0).
        // Per-vertex offset = (0.3, 0.0, 0.4) in world (since R→T=+X,
        // G→B=+Z, B→N=+Y; here R goes to +X, G goes to +Z, B=0 → Y=0).
        // Wait — convention is R→T, G→B (bitangent), B→N (normal). With
        // T=+X, B=+Z, N=+Y: offset = (R, B, G) in world = (0.3, 0.0, 0.4).
        // Euclidean length = sqrt(0.09 + 0 + 0.16) = sqrt(0.25) = 0.5.
        var m = BuildFlatQuad();
        var opts = new DisplacementOptions
        {
            Mode = DisplacementMode.Vector,
            Space = DisplacementSpace.Tangent,
            Texture = new ConstRgb(new Vector3(0.3f, 0.4f, 0.0f)),
            Scale = 1f, Midlevel = 0f, Bound = 1f, UvScale = 1f,
        };
        float maxDisp = DisplacementEngine.Apply(m, opts, 0);
        Assert.Equal(0.5f, maxDisp, 4);
    }

    [Fact]
    public void Vector_PureNormalChannel_MatchesScalarHeightField()
    {
        // Vector tangent-space displacement with RGB = (0, 0, 1) and scale
        // 0.3 produces the same result as scalar displacement with luminance
        // 1 and scale 0.3: every vertex moves +Y by 0.3 on the flat quad.
        var mScalar = BuildFlatQuad();
        var mVector = BuildFlatQuad();

        var scalarOpts = new DisplacementOptions
        {
            Mode = DisplacementMode.Scalar,
            Texture = new ConstLevel(1f),
            Scale = 0.3f, Midlevel = 0f, Bound = 0.3f, UvScale = 1f,
        };
        var vectorOpts = new DisplacementOptions
        {
            Mode = DisplacementMode.Vector,
            Space = DisplacementSpace.Tangent,
            Texture = new ConstRgb(new Vector3(0f, 0f, 1f)),
            Scale = 0.3f, Midlevel = 0f, Bound = 0.3f, UvScale = 1f,
        };

        DisplacementEngine.Apply(mScalar, scalarOpts, 0);
        DisplacementEngine.Apply(mVector, vectorOpts, 0);

        for (int i = 0; i < mScalar.Positions.Count; i++)
        {
            Assert.Equal(mScalar.Positions[i].X, mVector.Positions[i].X, 5);
            Assert.Equal(mScalar.Positions[i].Y, mVector.Positions[i].Y, 5);
            Assert.Equal(mScalar.Positions[i].Z, mVector.Positions[i].Z, 5);
        }
    }

    [Fact]
    public void Vector_Disabled_LeavesPositionsUntouched()
    {
        var m = BuildFlatQuad();
        var before = m.Positions.ToArray();
        var opts = new DisplacementOptions
        {
            Mode = DisplacementMode.Vector,
            Space = DisplacementSpace.Tangent,
            Texture = new ConstRgb(new Vector3(1, 1, 1)),
            Scale = 0f, Midlevel = 0f, Bound = 0f, UvScale = 1f,
        };
        Assert.False(opts.IsActive);
        float maxDisp = DisplacementEngine.Apply(m, opts, 0);
        Assert.Equal(0f, maxDisp);
        for (int i = 0; i < before.Length; i++)
            Assert.Equal(before[i], m.Positions[i]);
    }

    [Fact]
    public void Vector_DisplacedSphereSurface_AABBGrowsOnAllSides()
    {
        // Catmull-Clark cube → quasi-sphere with vertex-varying UVs only on
        // the original quad seams (none for an interior PolyMesh). Object-
        // space vector displacement with constant offset (0.1, 0.1, 0.1)
        // translates every vertex by that constant; the AABB shifts but
        // doesn't grow. To make it actually grow, sign-vary the offset by
        // luminance of position — a custom test texture below.
        var m = BuildUnitCubeQuads();
        CatmullClarkSubdivider.Subdivide(m, 2);
        var (preMin, preMax) = m.BoundingBox();

        // Apply object-space displacement that pushes every vertex outward
        // along its own position direction (proxy for "all directions").
        var opts = new DisplacementOptions
        {
            Mode = DisplacementMode.Vector,
            Space = DisplacementSpace.Object,
            Texture = new RadialOutward(0.15f),
            Scale = 1f, Midlevel = 0f, Bound = 0.3f, UvScale = 1f,
        };
        DisplacementEngine.Apply(m, opts, 0);

        var (postMin, postMax) = m.BoundingBox();
        Assert.True(postMax.X > preMax.X);
        Assert.True(postMax.Y > preMax.Y);
        Assert.True(postMax.Z > preMax.Z);
        Assert.True(postMin.X < preMin.X);
        Assert.True(postMin.Y < preMin.Y);
        Assert.True(postMin.Z < preMin.Z);
    }

    /// <summary>
    /// Test texture: returns <c>amount * normalize(p)</c> as RGB, so vector
    /// displacement pushes each vertex radially outward from the origin.
    /// </summary>
    private sealed class RadialOutward : ITexture
    {
        private readonly float _amount;
        public RadialOutward(float amount) { _amount = amount; }
        public Vector3 Value(float u, float v, Vector3 p, int seed)
        {
            float len = p.Length();
            if (len < 1e-6f) return Vector3.Zero;
            return p * (_amount / len);
        }
    }
}
