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
/// Tests for the bump + displacement combination — step 5 of the DEVLOG
/// "Stack completo surface displacement" cycle. Validates the autobump
/// pipeline introduced on top of the existing scalar / vector displacement
/// engine, plus the propagation of the mesh-level autobump through the
/// <see cref="HitRecord"/> at primary-hit time:
///
/// <list type="number">
///   <item><description>
///     <see cref="DisplacementOptions.IsAutobumpActive"/> tracks the
///     <c>Autobump</c> + <c>Texture</c> + <c>AutobumpStrength &gt; 0</c>
///     contract exactly.
///   </description></item>
///   <item><description>
///     A mesh whose <see cref="Mesh.AutoBump"/> is set surfaces that
///     bump through <see cref="HitRecord.AutoBump"/> on a successful hit;
///     a mesh without an autobump leaves the slot null.
///   </description></item>
///   <item><description>
///     End-to-end: <see cref="ObjLoader.Load"/> attaches the autobump
///     when <c>displacement.autobump: true</c>; disabling it (or zeroing
///     the strength) leaves the slot null and the path is byte-identical
///     to the no-autobump case.
///   </description></item>
///   <item><description>
///     The autobump strength scales linearly with the requested
///     <c>autobump_strength</c> multiplier — a 2× multiplier produces a
///     ~2× larger tangent-normal tilt away from <c>(0,0,1)</c>.
///   </description></item>
///   <item><description>
///     The autobump <c>BumpMapTexture</c> is independent from the
///     material's <c>BumpMap</c> — both can coexist on the same shading
///     hit, which is the Arnold-style composition the step targets.
///   </description></item>
/// </list>
/// </summary>
public class BumpDisplacementCombinationTests
{
    // ─── Synthetic textures ───────────────────────────────────────────────

    /// <summary>Constant unity colour — luminance 1 everywhere.</summary>
    private sealed class ConstOne : ITexture
    {
        public Vector3 Value(float u, float v, Vector3 p, int seed)
            => Vector3.One;
    }

    /// <summary>Linear gradient on u: luminance increases with u.</summary>
    private sealed class UGradient : ITexture
    {
        public Vector3 Value(float u, float v, Vector3 p, int seed)
            => new(u, u, u);
    }

    // ─── Tests ─────────────────────────────────────────────────────────────

    [Fact]
    public void IsAutobumpActive_FollowsFlagAndStrengthAndTexture()
    {
        var withTex = new ConstOne();

        // Flag off → inactive, regardless of strength.
        var off = new DisplacementOptions
        {
            Texture = withTex, Scale = 0.1f, Autobump = false,
            AutobumpStrength = 1f, AutobumpScale = 1f,
        };
        Assert.False(off.IsAutobumpActive);

        // Flag on but no texture → inactive (sanity guard).
        var noTex = new DisplacementOptions
        {
            Texture = null, Scale = 0.1f, Autobump = true,
            AutobumpStrength = 1f, AutobumpScale = 1f,
        };
        Assert.False(noTex.IsAutobumpActive);

        // Flag on but strength ≤ 0 → inactive.
        var zeroStrength = new DisplacementOptions
        {
            Texture = withTex, Scale = 0.1f, Autobump = true,
            AutobumpStrength = 0f, AutobumpScale = 1f,
        };
        Assert.False(zeroStrength.IsAutobumpActive);

        // All three set → active.
        var on = new DisplacementOptions
        {
            Texture = withTex, Scale = 0.1f, Autobump = true,
            AutobumpStrength = 1f, AutobumpScale = 1f,
        };
        Assert.True(on.IsAutobumpActive);
    }

    [Fact]
    public void MeshHit_PropagatesAutoBumpToHitRecord()
    {
        // Build a single-triangle mesh on the y=0 plane and attach an
        // autobump. A primary ray straight down at (0.5, 1, 0.25) must
        // hit the tri and copy AutoBump into the HitRecord.
        var v0 = new Vector3(0, 0, 0);
        var v1 = new Vector3(1, 0, 0);
        var v2 = new Vector3(0, 0, 1);
        var mat = new Lambertian(new Vector3(0.5f, 0.5f, 0.5f));
        var tri = new Triangle(v0, v1, v2, mat);

        var mesh = new Mesh(new List<IHittable> { tri }, mat, vertexCount: 3);
        var bump = new BumpMapTexture(new UGradient(), strength: 0.5f, scale: 1f);
        mesh.AutoBump = bump;

        var ray = new Ray(new Vector3(0.25f, 1f, 0.25f), new Vector3(0, -1f, 0));
        var rec = new HitRecord();
        bool hit = mesh.Hit(ray, 0.001f, 100f, ref rec);

        Assert.True(hit);
        Assert.Same(bump, rec.AutoBump);
    }

    [Fact]
    public void MeshHit_NullAutoBump_LeavesHitRecordSlotNull()
    {
        var v0 = new Vector3(0, 0, 0);
        var v1 = new Vector3(1, 0, 0);
        var v2 = new Vector3(0, 0, 1);
        var mat = new Lambertian(new Vector3(0.5f, 0.5f, 0.5f));
        var tri = new Triangle(v0, v1, v2, mat);

        var mesh = new Mesh(new List<IHittable> { tri }, mat, vertexCount: 3);
        Assert.Null(mesh.AutoBump);

        var ray = new Ray(new Vector3(0.25f, 1f, 0.25f), new Vector3(0, -1f, 0));
        var rec = new HitRecord();
        bool hit = mesh.Hit(ray, 0.001f, 100f, ref rec);

        Assert.True(hit);
        Assert.Null(rec.AutoBump);
    }

    [Fact]
    public void ObjLoader_AttachesAutoBump_WhenFlagEnabled()
    {
        // Use the canonical displacement-plane.obj that ships with the
        // showcases — single-quad unit plane on the y=0 plane.
        string repoRoot = FindRepoRoot();
        string objPath = Path.Combine(repoRoot, "scenes", "models", "displacement-plane.obj");
        Assert.True(File.Exists(objPath), $"OBJ fixture missing: {objPath}");

        var mat = new Lambertian(new Vector3(0.7f, 0.7f, 0.7f));
        var subdivision = SubdivisionOptions.Disabled;

        // Build active displacement options with autobump on.
        var disp = new DisplacementOptions
        {
            Mode = DisplacementMode.Scalar,
            Space = DisplacementSpace.Tangent,
            Texture = new UGradient(),
            Scale = 0.05f,
            Midlevel = 0f,
            Bound = 0.05f,
            UvScale = 1f,
            Autobump = true,
            AutobumpStrength = 1f,
            AutobumpScale = 1f,
        };

        var warnings = new List<string>();
        var mesh = ObjLoader.Load(objPath, mat, subdivision, disp,
            objectSeed: 0, warnings,
            out _, out _, out _);

        Assert.NotNull(mesh);
        Assert.NotNull(mesh!.AutoBump);
    }

    [Fact]
    public void ObjLoader_LeavesAutoBumpNull_WhenFlagOff()
    {
        string repoRoot = FindRepoRoot();
        string objPath = Path.Combine(repoRoot, "scenes", "models", "displacement-plane.obj");

        var mat = new Lambertian(new Vector3(0.7f, 0.7f, 0.7f));

        // Active displacement, autobump disabled.
        var disp = new DisplacementOptions
        {
            Mode = DisplacementMode.Scalar,
            Space = DisplacementSpace.Tangent,
            Texture = new UGradient(),
            Scale = 0.05f,
            Midlevel = 0f,
            Bound = 0.05f,
            UvScale = 1f,
            Autobump = false,
            AutobumpStrength = 1f,
            AutobumpScale = 1f,
        };

        var mesh = ObjLoader.Load(objPath, mat, SubdivisionOptions.Disabled, disp,
            objectSeed: 0, warnings: null,
            out _, out _, out _);

        Assert.NotNull(mesh);
        Assert.Null(mesh!.AutoBump);
    }

    [Fact]
    public void AutobumpStrength_ScalesPerturbationLinearly()
    {
        // The autobump's BumpMapTexture is built with
        //     bumpStrength = AutobumpStrength · |Scale|
        // so doubling AutobumpStrength must produce a ~2× larger tangent
        // tilt away from (0,0,1). Verified at small strength so the
        // tangent of the tilt stays in the linear regime.
        string repoRoot = FindRepoRoot();
        string objPath = Path.Combine(repoRoot, "scenes", "models", "displacement-plane.obj");

        var mat = new Lambertian(new Vector3(0.7f));

        var baseOpts = new DisplacementOptions
        {
            Mode = DisplacementMode.Scalar,
            Space = DisplacementSpace.Tangent,
            Texture = new UGradient(),
            Scale = 0.02f, Midlevel = 0f, Bound = 0.02f, UvScale = 1f,
            Autobump = true, AutobumpScale = 1f,
        };

        var lo = baseOpts with { AutobumpStrength = 0.5f };
        var hi = baseOpts with { AutobumpStrength = 1.0f };

        var meshLo = ObjLoader.Load(objPath, mat, SubdivisionOptions.Disabled, lo,
            0, null, out _, out _, out _);
        var meshHi = ObjLoader.Load(objPath, mat, SubdivisionOptions.Disabled, hi,
            0, null, out _, out _, out _);

        Assert.NotNull(meshLo?.AutoBump);
        Assert.NotNull(meshHi?.AutoBump);

        Vector3 nLo = meshLo!.AutoBump!.SampleTangentNormal(
            0.5f, 0.5f, Vector3.Zero, Vector3.UnitX, Vector3.UnitY, 0);
        Vector3 nHi = meshHi!.AutoBump!.SampleTangentNormal(
            0.5f, 0.5f, Vector3.Zero, Vector3.UnitX, Vector3.UnitY, 0);

        // Tangent of the tilt = |xy| / z. At small strength the ratio
        // matches AutobumpStrength's ratio (=2) to within ~10%.
        float tanLo = MathF.Sqrt(nLo.X * nLo.X + nLo.Y * nLo.Y) / nLo.Z;
        float tanHi = MathF.Sqrt(nHi.X * nHi.X + nHi.Y * nHi.Y) / nHi.Z;

        Assert.True(tanLo > 0f, $"low tilt should be non-zero, got {tanLo}");
        float ratio = tanHi / tanLo;
        Assert.InRange(ratio, 1.8f, 2.2f);
    }

    [Fact]
    public void AutoBump_IndependentFromMaterialBumpMap()
    {
        // Material carries a bump_map; mesh additionally carries an
        // autobump. Both must be reachable from the same HitRecord — they
        // compose at shade time, not mutually exclude.
        var matBump = new BumpMapTexture(new ConstOne(), strength: 0.3f, scale: 1f);
        var mat = new Lambertian(new Vector3(0.5f))
        {
            BumpMap = matBump,
        };

        var v0 = new Vector3(0, 0, 0);
        var v1 = new Vector3(1, 0, 0);
        var v2 = new Vector3(0, 0, 1);
        var tri = new Triangle(v0, v1, v2, mat);

        var mesh = new Mesh(new List<IHittable> { tri }, mat, vertexCount: 3);
        var autoBump = new BumpMapTexture(new UGradient(), strength: 0.7f, scale: 1f);
        mesh.AutoBump = autoBump;

        var ray = new Ray(new Vector3(0.2f, 1f, 0.2f), new Vector3(0, -1f, 0));
        var rec = new HitRecord();
        bool hit = mesh.Hit(ray, 0.001f, 100f, ref rec);

        Assert.True(hit);
        Assert.Same(matBump, rec.Material!.BumpMap);
        Assert.Same(autoBump, rec.AutoBump);
        Assert.NotSame(matBump, autoBump);
    }

    [Fact]
    public void Disabled_BackCompat_MeshShapeUnchanged()
    {
        // Loading an OBJ with all displacement-related fields off must
        // produce the same mesh — same face count, same AutoBump (null) —
        // as the legacy 3-arg overload. Step-5 guard against silent
        // back-compat drift.
        string repoRoot = FindRepoRoot();
        string objPath = Path.Combine(repoRoot, "scenes", "models", "displacement-plane.obj");

        var mat = new Lambertian(new Vector3(0.7f));

        var meshLegacy = ObjLoader.Load(objPath, mat);
        var meshNew    = ObjLoader.Load(objPath, mat, SubdivisionOptions.Disabled,
            DisplacementOptions.Disabled, 0, null, out _, out _, out _);

        Assert.NotNull(meshLegacy);
        Assert.NotNull(meshNew);
        Assert.Equal(meshLegacy!.FaceCount, meshNew!.FaceCount);
        Assert.Equal(meshLegacy.VertexCount, meshNew.VertexCount);
        Assert.Null(meshLegacy.AutoBump);
        Assert.Null(meshNew.AutoBump);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Walks up from the test assembly directory until a directory
    /// containing <c>scenes/models</c> is found. Tests can run from many
    /// CWDs (CLI <c>dotnet test</c>, in-IDE runners, CI), so anchoring on
    /// a known repo-relative folder is more robust than a hard-coded path.
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "scenes", "models")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate repo root (scenes/models directory).");
    }
}
