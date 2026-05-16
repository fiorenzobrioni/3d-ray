using System.Numerics;
using RayTracer.Core;
using RayTracer.Textures;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// CoordinateTexture contract — DEVLOG "Texturing VFX production-grade" step 7.
///
/// <para>
/// Validates that the new node returns the correct coordinate space (Object /
/// UV / Generated / World), that the <see cref="ITexture.Value(in HitRecord)"/>
/// overload is byte-identical to the legacy 5-arg path for every existing
/// texture (back-compat invariant — the materials switch to <c>Value(in rec)</c>
/// in this cycle so the default forwarding must produce the same result), and
/// that the new YAML knobs (<c>bounds_min</c>, <c>bounds_max</c>, <c>scale</c>,
/// <c>offset</c>) behave as documented.
/// </para>
/// </summary>
public class CoordinateTextureTests
{
    // ───────────────────────────────────────────────────────────────────
    //  Mode: Object
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Object_ReturnsFract_OfLocalPoint()
    {
        // Object mode wraps rec.LocalPoint through fract() so the output is
        // in [0, 1]³. A LocalPoint of (2.25, -0.75, 0.5) ⇒ (0.25, 0.25, 0.5).
        var tex = new CoordinateTexture { Mode = CoordinateTexture.CoordMode.Object };
        HitRecord rec = MakeRec(
            local: new Vector3(2.25f, -0.75f, 0.5f),
            world: new Vector3(99f, 99f, 99f), // ignored in Object mode
            u: 0.42f, v: 0.13f);

        Vector3 c = tex.Value(in rec);
        Assert.Equal(0.25f, c.X, 5);
        Assert.Equal(0.25f, c.Y, 5);
        Assert.Equal(0.50f, c.Z, 5);
    }

    [Fact]
    public void Object_OutputStaysIn_UnitCube_ForArbitraryInputs()
    {
        // fract() guarantees [0, 1) for any finite input. Sample a wide range
        // (negative, large, near-integer) and verify the contract.
        var tex = new CoordinateTexture { Mode = CoordinateTexture.CoordMode.Object };
        Vector3[] probes =
        {
            new(0, 0, 0), new(-1, -2, -3), new(7.7f, -100.3f, 42.42f),
            new(0.999999f, 1.000001f, -0.0000001f),
        };
        foreach (var p in probes)
        {
            HitRecord rec = MakeRec(local: p, world: p, u: 0, v: 0);
            Vector3 c = tex.Value(in rec);
            Assert.InRange(c.X, 0f, 1f);
            Assert.InRange(c.Y, 0f, 1f);
            Assert.InRange(c.Z, 0f, 1f);
        }
    }

    [Fact]
    public void Object_Scale_PacksMoreBandsPerUnit()
    {
        // scale = 4 ⇒ the [0, 0.25] LocalPoint range now spans one full
        // fract period — so LocalPoint = 0.125 should map to 0.5 (mid-cell).
        var tex = new CoordinateTexture
        {
            Mode = CoordinateTexture.CoordMode.Object,
            Scale = 4f,
        };
        HitRecord rec = MakeRec(local: new Vector3(0.125f, 0, 0), world: Vector3.Zero, u: 0, v: 0);
        Vector3 c = tex.Value(in rec);
        Assert.Equal(0.5f, c.X, 5);
    }

    // ───────────────────────────────────────────────────────────────────
    //  Mode: UV
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void UV_Returns_UvZero_LinearWithoutFract()
    {
        // UV mode is the canonical 2-D parametric coord — kept linear (no
        // fract) because primitives already deliver U,V ∈ [0,1]. A clean
        // smooth gradient is exactly what artists want for UV debug.
        var tex = new CoordinateTexture { Mode = CoordinateTexture.CoordMode.UV };
        HitRecord rec = MakeRec(local: Vector3.One, world: Vector3.One, u: 0.3f, v: 0.7f);
        Vector3 c = tex.Value(in rec);
        Assert.Equal(0.3f, c.X, 5);
        Assert.Equal(0.7f, c.Y, 5);
        Assert.Equal(0.0f, c.Z, 5);
    }

    [Fact]
    public void UV_Offset_ShiftsUV_Channel()
    {
        var tex = new CoordinateTexture
        {
            Mode = CoordinateTexture.CoordMode.UV,
            Offset = new Vector3(0.1f, -0.2f, 0f),
        };
        HitRecord rec = MakeRec(local: Vector3.Zero, world: Vector3.Zero, u: 0.5f, v: 0.5f);
        Vector3 c = tex.Value(in rec);
        Assert.Equal(0.6f, c.X, 5);
        Assert.Equal(0.3f, c.Y, 5);
    }

    // ───────────────────────────────────────────────────────────────────
    //  Mode: Generated (reference-space)
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Generated_NormalisesToUnitCube_FromBounds()
    {
        // Default bounds [-1, 1]³ ⇒ midpoint 0 ⇒ Generated = (0.5, 0.5, 0.5).
        // Corner +1 ⇒ Generated = (1, 1, 1). Corner -1 ⇒ Generated = (0, 0, 0).
        var tex = new CoordinateTexture { Mode = CoordinateTexture.CoordMode.Generated };
        HitRecord recMid = MakeRec(local: Vector3.Zero, world: Vector3.Zero, u: 0, v: 0);
        HitRecord recHi  = MakeRec(local: new Vector3( 1,  1,  1), world: Vector3.Zero, u: 0, v: 0);
        HitRecord recLo  = MakeRec(local: new Vector3(-1, -1, -1), world: Vector3.Zero, u: 0, v: 0);
        Vector3 mid = tex.Value(in recMid);
        Vector3 hi  = tex.Value(in recHi);
        Vector3 lo  = tex.Value(in recLo);
        Assert.Equal(new Vector3(0.5f, 0.5f, 0.5f), mid);
        Assert.Equal(new Vector3(1f,   1f,   1f),   hi);
        Assert.Equal(new Vector3(0f,   0f,   0f),   lo);
    }

    [Fact]
    public void Generated_Clamps_OutOfBoundsToZeroOrOne()
    {
        // Hits outside the declared reference box (e.g. after surface
        // displacement) clamp to 0 or 1 — no holes in the debug view.
        var tex = new CoordinateTexture { Mode = CoordinateTexture.CoordMode.Generated };
        HitRecord rec = MakeRec(local: new Vector3(5f, -10f, 0.3f), world: Vector3.Zero, u: 0, v: 0);
        Vector3 c = tex.Value(in rec);
        Assert.Equal(1f, c.X);
        Assert.Equal(0f, c.Y);
        // 0.3 within [-1, 1] ⇒ (0.3 - (-1)) / 2 = 0.65
        Assert.Equal(0.65f, c.Z, 5);
    }

    [Fact]
    public void Generated_CustomBounds_RescaleCorrectly()
    {
        // Bounds [0, 10]³ ⇒ LocalPoint=5 → 0.5, LocalPoint=10 → 1.
        var tex = new CoordinateTexture
        {
            Mode = CoordinateTexture.CoordMode.Generated,
            BoundsMin = Vector3.Zero,
            BoundsMax = new Vector3(10f, 10f, 10f),
        };
        HitRecord rec = MakeRec(local: new Vector3(5f, 10f, 0f), world: Vector3.Zero, u: 0, v: 0);
        Vector3 c = tex.Value(in rec);
        Assert.Equal(0.5f, c.X, 5);
        Assert.Equal(1f,   c.Y, 5);
        Assert.Equal(0f,   c.Z, 5);
    }

    [Fact]
    public void Generated_DegenerateBounds_FallsToHalf()
    {
        // BoundsMin == BoundsMax on an axis ⇒ division by zero guard ⇒ output
        // stuck at 0.5 (mid-cell) on that axis. Prevents NaN that would
        // otherwise propagate through tone mapping.
        var tex = new CoordinateTexture
        {
            Mode = CoordinateTexture.CoordMode.Generated,
            BoundsMin = new Vector3(2f, 2f, 2f),
            BoundsMax = new Vector3(2f, 5f, 8f),
        };
        HitRecord rec = MakeRec(local: new Vector3(2f, 3.5f, 5f), world: Vector3.Zero, u: 0, v: 0);
        Vector3 c = tex.Value(in rec);
        Assert.Equal(0.5f, c.X, 5);   // degenerate axis → fallback
        Assert.Equal(0.5f, c.Y, 5);   // (3.5 - 2) / (5 - 2) = 0.5
        Assert.Equal(0.5f, c.Z, 5);   // (5   - 2) / (8 - 2) = 0.5
        Assert.False(float.IsNaN(c.X));
    }

    // ───────────────────────────────────────────────────────────────────
    //  Mode: World
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void World_ReturnsFract_OfWorldPoint_NotLocalPoint()
    {
        // World mode reads rec.Point (post-Transform world space) and
        // ignores rec.LocalPoint. Critical for the "world-locked overlay"
        // use case (laser grid, dust shells that don't follow the object).
        var tex = new CoordinateTexture { Mode = CoordinateTexture.CoordMode.World };
        HitRecord rec = MakeRec(
            local: new Vector3(0.5f, 0.5f, 0.5f),  // ignored
            world: new Vector3(3.75f, -2.25f, 0.1f),
            u: 0, v: 0);
        Vector3 c = tex.Value(in rec);
        Assert.Equal(0.75f, c.X, 5);   // 3.75 - 3 = 0.75
        Assert.Equal(0.75f, c.Y, 5);   // -2.25 + 3 = 0.75
        Assert.Equal(0.1f,  c.Z, 5);
    }

    [Fact]
    public void World_DiffersFromObject_OnTransformedPoint()
    {
        // If world ≠ local (object placed somewhere in scene), the two modes
        // produce different RGB outputs at the same hit. This is the whole
        // raison d'être of the World mode.
        var world = new CoordinateTexture { Mode = CoordinateTexture.CoordMode.World };
        var obj   = new CoordinateTexture { Mode = CoordinateTexture.CoordMode.Object };
        HitRecord rec = MakeRec(
            local: new Vector3(0.3f, 0.4f, 0.5f),
            world: new Vector3(7.6f, 8.2f, 9.9f),
            u: 0, v: 0);
        Vector3 cw = world.Value(in rec);
        Vector3 co = obj.Value(in rec);
        Assert.NotEqual(cw, co);
    }

    // ───────────────────────────────────────────────────────────────────
    //  Back-compat: default Value(in rec) overload on every other texture
    // ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(typeof(SolidColor))]
    [InlineData(typeof(CheckerTexture))]
    [InlineData(typeof(NoiseTexture))]
    [InlineData(typeof(MarbleTexture))]
    [InlineData(typeof(WoodTexture))]
    [InlineData(typeof(VoronoiTexture))]
    [InlineData(typeof(BrickTexture))]
    [InlineData(typeof(GradientTexture))]
    public void DefaultValueInRec_IsByteIdentical_To5ArgPath(Type texType)
    {
        // The materials switch from `Albedo.Value(rec.U, rec.V, rec.LocalPoint,
        // rec.ObjectSeed, rec.Footprint)` to `Albedo.Value(in rec)` in this
        // cycle. The default ITexture.Value(in rec) forwards exactly those
        // five arguments, so for every texture that doesn't override the new
        // overload the result must be bit-identical. Anything else would
        // silently break the byte-identical back-compat invariant.
        ITexture tex = (ITexture)Activator.CreateInstance(texType,
            args: BestEffortCtorArgs(texType))!;

        Vector3[] probes =
        {
            new(0.5f, 0.5f, 0.5f),
            new(0f, 0f, 0f),
            new(1.7f, -2.3f, 4.4f),
            new(-0.001f, 0.001f, 0f),
            new(12.3f, -5.5f, 7.7f),
        };
        foreach (var p in probes)
        {
            HitRecord rec = MakeRec(local: p, world: p, u: 0.3f, v: 0.7f);
            Vector3 viaLegacy = tex.Value(rec.U, rec.V, rec.LocalPoint, rec.ObjectSeed, rec.Footprint);
            Vector3 viaRec    = tex.Value(in rec);
            Assert.Equal(viaLegacy, viaRec);
        }
    }

    [Fact]
    public void LegacyValue4Arg_OnCoordinateTexture_DegradesGracefully()
    {
        // When called via the legacy 4-arg path (e.g. Emissive.Emit), no
        // HitRecord is available — World mode cannot be honoured. We fall
        // back to treating `p` as both LocalPoint and Point. The texture
        // must NOT crash and must produce a finite [0,1]³ result.
        var tex = new CoordinateTexture { Mode = CoordinateTexture.CoordMode.World };
        Vector3 c = tex.Value(0.5f, 0.5f, new Vector3(2.5f, -1.3f, 0.8f), 0);
        Assert.False(float.IsNaN(c.X) || float.IsNaN(c.Y) || float.IsNaN(c.Z));
        Assert.InRange(c.X, 0f, 1f);
        Assert.InRange(c.Y, 0f, 1f);
        Assert.InRange(c.Z, 0f, 1f);
    }

    // ───────────────────────────────────────────────────────────────────
    //  Helpers
    // ───────────────────────────────────────────────────────────────────

    private static HitRecord MakeRec(Vector3 local, Vector3 world, float u, float v)
    {
        HitRecord rec = default;
        rec.U = u;
        rec.V = v;
        rec.LocalPoint = local;
        rec.Point = world;
        rec.Normal = Vector3.UnitY;
        rec.FrontFace = true;
        rec.ObjectSeed = 0;
        return rec;
    }

    private static object[] BestEffortCtorArgs(Type t)
    {
        // Minimal constructor args for the parametric texture classes used
        // in the back-compat theory. Mirrors the defaults in
        // SceneLoader.CreateTexture so the probe values are realistic.
        if (t == typeof(SolidColor))       return new object[] { Vector3.One };
        if (t == typeof(CheckerTexture))   return new object[] { 1f, Vector3.Zero, Vector3.One };
        if (t == typeof(NoiseTexture))     return new object[] { 4f, Vector3.Zero, Vector3.One };
        if (t == typeof(MarbleTexture))    return new object[] { 4f, new Vector3(0.9f), new Vector3(0.1f) };
        if (t == typeof(WoodTexture))      return new object[] { 4f, 2f, new Vector3(0.85f, 0.65f, 0.4f), new Vector3(0.6f, 0.4f, 0.2f) };
        if (t == typeof(VoronoiTexture))   return new object[] { 4f, Vector3.Zero, Vector3.One };
        if (t == typeof(BrickTexture))     return new object[] { new Vector3(0.65f, 0.27f, 0.20f), new Vector3(0.55f, 0.20f, 0.15f), new Vector3(0.85f, 0.83f, 0.78f) };
        if (t == typeof(GradientTexture))  return new object[] { Vector3.Zero, Vector3.One };
        return Array.Empty<object>();
    }
}
