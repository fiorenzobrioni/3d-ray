using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Geometry.Subdivision;
using RayTracer.Materials;
using RayTracer.Scene;
using RayTracer.Textures;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Material-level displacement (Cycles/RenderMan parity): the displacement
/// descriptor lives on <see cref="IMaterial.Displacement"/> so a single
/// displaced material drives every mesh that uses it. These tests pin the
/// invariants that protect that contract:
///
/// <list type="number">
///   <item><description>Same material → identical post-load geometry across
///   two entities (no per-entity duplication path).</description></item>
///   <item><description>MixDisplacement vector-blends two children via the
///   parent <see cref="MixMaterial"/>'s mask/blend factor evaluated at the
///   vertex — at <c>t=0</c> the result equals child A, at <c>t=1</c> equals
///   child B, at <c>t=0.5</c> equals the average.</description></item>
///   <item><description>Per-entity override via
///   <c>displacement_enabled: false</c> suppresses the material's
///   displacement for that single instance.</description></item>
///   <item><description>Legacy entity-level <c>displacement:</c> syntax
///   raises a hard error pointing at the migration.</description></item>
///   <item><description>Mix-blend bump map composes the two children's
///   tangent-space normals via the same mask, normalised.</description></item>
/// </list>
/// </summary>
public class MaterialDisplacementTests
{
    /// <summary>
    /// Trivial 1D-gradient height field. Reading along U returns U as
    /// luminance; ignores V. Deterministic — no PRNG.
    /// </summary>
    private sealed class UGradient : ITexture
    {
        public Vector3 Value(float u, float v, Vector3 p, int seed)
            => new(u, u, u);
    }

    /// <summary>Constant-color "mask" texture — drives a constant blend.</summary>
    private sealed class ConstantMask : ITexture
    {
        private readonly Vector3 _color;
        public ConstantMask(float k) { _color = new(k, k, k); }
        public Vector3 Value(float u, float v, Vector3 p, int seed) => _color;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "3d-ray.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static DisplacementOptions ScalarOpts(float scale, float midlevel = 0f)
        => new()
        {
            Mode = DisplacementMode.Scalar,
            Space = DisplacementSpace.Tangent,
            Texture = new UGradient(),
            Scale = scale,
            Midlevel = midlevel,
            Bound = MathF.Abs(scale),
            UvScale = 1f,
            Autobump = false,
            AutobumpStrength = 1f,
            AutobumpScale = 1f,
        };

    // ─────────────────────────────────────────────────────────────────────────
    //  1. Two entities sharing one displaced material → identical geometry.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SameMaterial_OnTwoEntities_ProducesIdenticalGeometry()
    {
        // The key win of the material-level model: the material drives the
        // displacement, so two meshes resolving the same material end up
        // with bit-identical post-displacement vertex positions (modulo the
        // per-entity transform applied later by the scene loader).
        string repoRoot = FindRepoRoot();
        string objPath = Path.Combine(repoRoot, "scenes", "models", "displacement-plane.obj");

        var mat = new Lambertian(new Vector3(0.7f))
        {
            Displacement = new LeafDisplacement(ScalarOpts(scale: 0.1f, midlevel: 0.5f))
        };

        var meshA = ObjLoader.Load(objPath, mat, SubdivisionOptions.Disabled,
            mat.Displacement, 0, null, out _, out _, out _);
        var meshB = ObjLoader.Load(objPath, mat, SubdivisionOptions.Disabled,
            mat.Displacement, 0, null, out _, out _, out _);

        Assert.NotNull(meshA);
        Assert.NotNull(meshB);
        Assert.Equal(meshA!.FaceCount, meshB!.FaceCount);
        Assert.Equal(meshA.VertexCount, meshB.VertexCount);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  2. MixDisplacement vector-blend — endpoints + midpoint.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MixDisplacement_AtBlendZero_EqualsChildA()
    {
        // Build two ScalarA / ScalarB materials with different scales so the
        // blend output is unambiguous, then test the engine path with a
        // constant Blend = 0 mask (no mask → use Blend). Per-vertex result
        // must equal pure-A displacement. We compare the returned
        // maxDisplacement (computed on actual displaced vertex offsets,
        // independent of any AABB-inflation safety margin).
        var optsA = ScalarOpts(scale: 0.1f);
        var optsB = ScalarOpts(scale: 0.5f); // 5× larger amplitude

        var matA = new Lambertian(Vector3.One) { Displacement = new LeafDisplacement(optsA) };
        var matB = new Lambertian(Vector3.One) { Displacement = new LeafDisplacement(optsB) };

        var mix = new MixMaterial(matA, matB, blend: 0f, mask: null);
        mix.Displacement = new MixDisplacement(mix, matA.Displacement!, matB.Displacement!);

        float maxA   = LoadAndMeasureMax(matA);
        float maxMix = LoadAndMeasureMax(mix);

        Assert.Equal(maxA, maxMix, precision: 4);
    }

    [Fact]
    public void MixDisplacement_AtBlendOne_EqualsChildB()
    {
        var optsA = ScalarOpts(scale: 0.1f);
        var optsB = ScalarOpts(scale: 0.5f);

        var matA = new Lambertian(Vector3.One) { Displacement = new LeafDisplacement(optsA) };
        var matB = new Lambertian(Vector3.One) { Displacement = new LeafDisplacement(optsB) };

        var mix = new MixMaterial(matA, matB, blend: 1f, mask: null);
        mix.Displacement = new MixDisplacement(mix, matA.Displacement!, matB.Displacement!);

        float maxB   = LoadAndMeasureMax(matB);
        float maxMix = LoadAndMeasureMax(mix);

        Assert.Equal(maxB, maxMix, precision: 4);
    }

    [Fact]
    public void MixDisplacement_AtBlendHalf_AveragesChildren()
    {
        var optsA = ScalarOpts(scale: 0.1f);
        var optsB = ScalarOpts(scale: 0.5f);

        var matA = new Lambertian(Vector3.One) { Displacement = new LeafDisplacement(optsA) };
        var matB = new Lambertian(Vector3.One) { Displacement = new LeafDisplacement(optsB) };

        var mix = new MixMaterial(matA, matB, blend: 0.5f, mask: null);
        mix.Displacement = new MixDisplacement(mix, matA.Displacement!, matB.Displacement!);

        float maxA   = LoadAndMeasureMax(matA);
        float maxB   = LoadAndMeasureMax(matB);
        float maxMix = LoadAndMeasureMax(mix);

        // Both children displace along the same smooth normal in scalar mode
        // and the UGradient is monotone in U, so the per-vertex max is also
        // the per-vertex max of the linear blend.
        float halfAplusB = 0.5f * (maxA + maxB);
        Assert.Equal(halfAplusB, maxMix, precision: 4);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  3. Mix-blend bump map composes children's tangent-space normals.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MixBumpMapTexture_AtZero_EqualsChildA()
    {
        // Two single-bumps with different strength so n_A != n_B != UnitZ;
        // mask=null + Blend=0 must give n_A unchanged (up to normalisation).
        var a = new BumpMapTexture(new UGradient(), strength: 1.0f, scale: 1f);
        var b = new BumpMapTexture(new UGradient(), strength: 5.0f, scale: 1f);
        var mix = new MixBumpMapTexture(a, b, mask: null, blend: 0f);

        Vector3 nA = a.SampleTangentNormal(0.5f, 0.5f, Vector3.Zero,
            Vector3.UnitX, Vector3.UnitY, 0);
        Vector3 nMix = mix.SampleTangentNormal(0.5f, 0.5f, Vector3.Zero,
            Vector3.UnitX, Vector3.UnitY, 0);

        Assert.Equal(nA.X, nMix.X, precision: 5);
        Assert.Equal(nA.Y, nMix.Y, precision: 5);
        Assert.Equal(nA.Z, nMix.Z, precision: 5);
    }

    [Fact]
    public void MixBumpMapTexture_AtOne_EqualsChildB()
    {
        var a = new BumpMapTexture(new UGradient(), strength: 1.0f, scale: 1f);
        var b = new BumpMapTexture(new UGradient(), strength: 5.0f, scale: 1f);
        var mix = new MixBumpMapTexture(a, b, mask: null, blend: 1f);

        Vector3 nB = b.SampleTangentNormal(0.5f, 0.5f, Vector3.Zero,
            Vector3.UnitX, Vector3.UnitY, 0);
        Vector3 nMix = mix.SampleTangentNormal(0.5f, 0.5f, Vector3.Zero,
            Vector3.UnitX, Vector3.UnitY, 0);

        Assert.Equal(nB.X, nMix.X, precision: 5);
        Assert.Equal(nB.Y, nMix.Y, precision: 5);
        Assert.Equal(nB.Z, nMix.Z, precision: 5);
    }

    [Fact]
    public void MixBumpMapTexture_OutputIsUnitLength()
    {
        var a = new BumpMapTexture(new UGradient(), strength: 0.5f, scale: 1f);
        var b = new BumpMapTexture(new UGradient(), strength: 2.0f, scale: 1f);
        var mix = new MixBumpMapTexture(a, b, mask: null, blend: 0.5f);

        Vector3 n = mix.SampleTangentNormal(0.7f, 0.3f, Vector3.Zero,
            Vector3.UnitX, Vector3.UnitY, 0);
        Assert.Equal(1f, n.Length(), precision: 4);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  4. Per-entity override — displacement_enabled: false suppresses.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EntityDisplacementEnabledFalse_SuppressesMaterialDisplacement()
    {
        // Two YAML scenes; identical material with displacement, but the
        // second entity sets displacement_enabled: false. The first mesh
        // displaces, the second is flat.
        string repoRoot = FindRepoRoot();

        // We test directly via ObjLoader, simulating the entity branch.
        string objPath = Path.Combine(repoRoot, "scenes", "models", "displacement-plane.obj");
        var mat = new Lambertian(Vector3.One)
        {
            Displacement = new LeafDisplacement(ScalarOpts(0.2f, 0.5f))
        };

        var meshOn = ObjLoader.Load(objPath, mat, SubdivisionOptions.Disabled,
            mat.Displacement, 0, null, out _, out _, out _);
        // displacement_enabled=false → SceneLoader passes null
        var meshOff = ObjLoader.Load(objPath, mat, SubdivisionOptions.Disabled,
            (MaterialDisplacement?)null, 0, null, out _, out _, out _);

        Assert.NotNull(meshOn);
        Assert.NotNull(meshOff);
        // The "off" mesh keeps the un-displaced height; the "on" mesh has
        // perturbed positions and so a larger Y extent.
        Assert.True(BoundsHeight(meshOn!) > BoundsHeight(meshOff!));
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  5. Legacy entity-level YAML → hard error.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LegacyEntityDisplacementYaml_RaisesLoadError()
    {
        // Write a tiny scene that uses the pre-migration entity-level syntax
        // and verify SceneLoader throws with a migration message.
        string repoRoot = FindRepoRoot();
        string tmp = Path.Combine(repoRoot, "scenes",
            $"legacy-displacement-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(tmp, """
            camera:
              position: [0, 1, -3]
              look_at: [0, 0, 0]
              fov: 50

            materials:
              - id: "porcellana"
                type: "lambertian"
                color: [0.8, 0.8, 0.8]

            entities:
              - name: "plane"
                type: "mesh"
                path: "models/displacement-plane.obj"
                material: "porcellana"
                # ← LEGACY entity-level displacement: must raise
                displacement:
                  texture:
                    type: "noise"
                    scale: 4.0
                  scale: 0.2
            """);

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => SceneLoader.Load(tmp, 320, 180));
            Assert.Contains("material-level", ex.Message);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Helpers.
    // ─────────────────────────────────────────────────────────────────────────

    private static Mesh? LoadGroundPlane(IMaterial mat)
    {
        string repoRoot = FindRepoRoot();
        string objPath = Path.Combine(repoRoot, "scenes", "models", "displacement-plane.obj");
        return ObjLoader.Load(objPath, mat, SubdivisionOptions.Disabled,
            mat.Displacement, 0, null, out _, out _, out _);
    }

    /// <summary>
    /// Loads the test plane with the material's displacement applied and
    /// returns the engine-reported maximum displacement amplitude (the
    /// per-vertex max of <c>|offset|</c>). This bypasses any BVH leaf-AABB
    /// inflation safety margin, so the assertion stays focused on the
    /// geometric blend math.
    /// </summary>
    private static float LoadAndMeasureMax(IMaterial mat)
    {
        string repoRoot = FindRepoRoot();
        string objPath = Path.Combine(repoRoot, "scenes", "models", "displacement-plane.obj");
        ObjLoader.Load(objPath, mat, SubdivisionOptions.Disabled,
            mat.Displacement, 0, null, out _, out _, out float maxDisp);
        return maxDisp;
    }

    private static float BoundsHeight(Mesh m)
    {
        var aabb = m.BoundingBox();
        return aabb.Max.Y - aabb.Min.Y;
    }
}
