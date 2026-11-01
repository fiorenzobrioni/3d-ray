using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Geometry.Subdivision;
using RayTracer.Materials;
using RayTracer.Lights;
using RayTracer.Acceleration;
using RayTracer.Textures;
using RayTracer.Rendering;
using RayTracer.Rendering.Sky;
using RayTracer.Volumetrics;

namespace RayTracer.Scene;
public partial class SceneLoader
{
    /// <summary>
    /// Creates a mesh entity from a model file (.obj with its MTL, .gltf,
    /// .glb, .ply; <see cref="MeshImporter"/>), resolved relative to the scene
    /// YAML, with subdivision and the material's displacement. The entity's
    /// <c>material</c>, when given, covers every face; otherwise the file's
    /// own materials are used, renamed by <c>material_overrides</c>, and
    /// <paramref name="mat"/> (the default, or a group's) covers faces with none.
    /// </summary>
    private static IHittable? CreateMeshEntity(EntityData e, IMaterial mat,
        Dictionary<string, IMaterial> materials, string sceneDir, int entityIndex)
    {
        var screen = _currentScreenContext;
        string label = e.Name ?? "(unnamed)";
        if (string.IsNullOrWhiteSpace(e.Path))
        {
            Warn($"Mesh entity '{label}' requires a 'path' to a model file. Skipping.");
            return null;
        }

        string modelPath = Path.IsPathRooted(e.Path) ? e.Path : Path.Combine(sceneDir, e.Path);
        if (!File.Exists(modelPath))
        {
            Warn($"Mesh entity '{label}': model file not found at '{modelPath}'. Skipping.");
            return null;
        }
        if (!MeshImporter.IsSupported(modelPath))
        {
            Warn($"Mesh entity '{label}': '{Path.GetExtension(modelPath)}' is not a supported model " +
                 "format (.obj, .gltf, .glb, .ply). Skipping.");
            return null;
        }
        string shown = e.Name ?? Path.GetFileName(modelPath);

        // The entity transform is folded into the screen-space context so the
        // adaptive heuristic measures the world-space projected edge length.
        var subdivision = BuildSubdivisionOptions(e, screen);

        // Displacement lives on the material; the entity can suppress it.
        int seed = e.Seed ?? StableSeed(entityIndex, e.Type, e.Name);
        var matDisp = e.DisplacementEnabled ? mat.Displacement : null;

        Dictionary<string, IMaterial>? overrides = null;
        if (e.MaterialOverrides is { Count: > 0 })
        {
            overrides = new Dictionary<string, IMaterial>(StringComparer.Ordinal);
            foreach (var (fileName, id) in e.MaterialOverrides)
                overrides[fileName] = GetMaterial(materials, id);
        }

        var warnings = new List<string>();
        var imported = MeshImporter.Read(modelPath, warnings);
        ImportResult? result = imported == null ? null
            : MeshImporter.Build(imported, e.Material != null ? mat : null, overrides, mat,
                                 subdivision, matDisp, seed, warnings,
                                 shift: _shift);
        foreach (var w in warnings)
            Warn($"Mesh '{shown}': {w}");
        if (result == null)
        {
            Warn($"Mesh entity '{label}': failed to load '{modelPath}'. Skipping.");
            return null;
        }
        if (imported != null && e.MaterialOverrides != null)
            foreach (var name in e.MaterialOverrides.Keys)
                if (!imported.SlotNames.Contains(name))
                    Warn($"Mesh '{shown}': material_overrides names '{name}', which the file does not have.");

        // An explicit bound smaller than the displacement applied: a hint for
        // the author (the displaced bounds are exact either way).
        float effectiveBound = matDisp?.Bound ?? 0f;
        if (matDisp != null && matDisp.RequestsGeometricDisplacement
            && effectiveBound > 0f && result.MaxDisplacement > effectiveBound * 1.0001f)
        {
            Warn($"Mesh '{shown}': material's displacement.bound={effectiveBound:F4} is smaller " +
                 $"than the maximum applied displacement {result.MaxDisplacement:F4}. " +
                 $"Raise displacement.bound to {result.MaxDisplacement:F4} or higher " +
                 $"to fully enclose the displaced silhouette.");
        }

        var details = new List<string>();
        if (result.MaterialCount > 1) details.Add($"{result.MaterialCount} materials");
        if (result.Iterations > 0) details.Add($"subdivision: {result.Scheme} \u00d7 {result.Iterations}");
        if (matDisp != null && matDisp.RequestsGeometricDisplacement)
            details.Add($"displacement: {DescribeDisplacement(matDisp)}, max={result.MaxDisplacement:F4}");
        Info($"Mesh:        {shown} \u2014 {result.FaceCount:N0} faces, {result.VertexCount:N0} vertices" +
             (details.Count > 0 ? $" ({string.Join(", ", details)})" : ""));

        result.Hittable.Seed = seed;
        // Vertices moved into the render space: textures read them where they were.
        if (!_shift.IsZero)
        {
            var offset = _shift.ToVector3();
            if (result.Hittable is Mesh single) single.LocalPointOffset = offset;
            else if (result.Hittable is Group g)
                foreach (var child in g.Children)
                    if (child is Mesh m) m.LocalPointOffset = offset;
        }
        return result.Hittable;
    }

    /// <summary>
    /// Short tag describing the displacement requested by a material, for
    /// load-time info logging.
    /// </summary>
    private static string DescribeDisplacement(MaterialDisplacement? d)
    {
        if (d == null) return "none";
        if (d is MixDisplacement mix)
            return $"mix({DescribeDisplacement(mix.A)},{DescribeDisplacement(mix.B)})";
        if (d is LeafDisplacement leaf)
        {
            string tag = leaf.Method == DisplacementMethod.BumpOnly
                ? "bump_only"
                : leaf.Options.Mode == DisplacementMode.Vector
                    ? $"vector-{leaf.Options.Space.ToString().ToLowerInvariant()}"
                    : "scalar";
            if (leaf.RequestsAutobump && leaf.Method != DisplacementMethod.BumpOnly)
                tag += "+autobump";
            return tag;
        }
        return "unknown";
    }

    /// <summary>
    /// Translates the <see cref="MaterialData"/> displacement block into a
    /// <see cref="LeafDisplacement"/> wrapping the engine-level
    /// <see cref="DisplacementOptions"/> struct. Material-level (Cycles/
    /// RenderMan parity): one displaced material drives every mesh that uses
    /// it, no per-entity duplication. Returns <c>null</c> when the block is
    /// absent, malformed, or has a zero scale.
    /// </summary>
    private static LeafDisplacement? BuildLeafDisplacement(
        MaterialData m, string sceneDir)
    {
        var disp = m.Displacement;
        if (disp == null) return null;

        string ownerTag = m.Id ?? "(unnamed material)";

        if (disp.Texture == null)
        {
            Warn($"Material '{ownerTag}': displacement requires a " +
                 $"'texture' block. Ignoring.");
            return null;
        }

        if (disp.Scale == 0f)
        {
            // Silent: scale=0 means "wire up the texture but don't displace"
            // \u2014 a valid authoring state.
            return null;
        }

        // Mode, space (vector mode) and method; the values are checked by the
        // scene validator.
        var mode = disp.Mode == "vector" ? DisplacementMode.Vector : DisplacementMode.Scalar;
        var space = disp.Space == "object" ? DisplacementSpace.Object : DisplacementSpace.Tangent;
        if (disp.Space != null && mode == DisplacementMode.Scalar)
            Warn($"Material '{ownerTag}': 'space' is only used in vector mode; ignored on a scalar displacement.");
        var method = disp.Method switch
        {
            "displacement" => DisplacementMethod.Displacement,
            "bump_only"    => DisplacementMethod.BumpOnly,
            _              => DisplacementMethod.Both,
        };

        ITexture inner;
        try
        {
            inner = CreateTexture(disp.Texture, sceneDir, TextureRole.Data);
        }
        catch (Exception ex)
        {
            Warn($"Material '{ownerTag}': failed to build " +
                 $"displacement texture: {ex.Message}. Ignoring.");
            return null;
        }

        // displacement_bound default: in scalar mode the maximum offset is
        // bounded by |scale\u00b7(h_max\u2212midlevel)| \u2264 |scale| for a luminance
        // texture in [0,1]; in vector mode the L2 length of an offset whose
        // components each lie in [-|scale|, +|scale|] is bounded by
        // |scale|\u00b7sqrt(3). The post-displacement warning fires whenever the
        // observed maximum exceeds the bound, so authors using out-of-range
        // textures (e.g. an HDR-EXR vector map storing values > 1) will be
        // told the exact bound they should set.
        float defaultBound = mode == DisplacementMode.Vector
            ? MathF.Abs(disp.Scale) * 1.732051f
            : MathF.Abs(disp.Scale);
        float bound = disp.Bound > 0f
            ? disp.Bound
            : defaultBound;

        // ── Autobump (step 5 of the surface-displacement stack) ────────────
        // Optional "autobump" that derives a residual BumpMapTexture from
        // the same displacement texture. Mirrors Arnold's autobump_visibility
        // flag on polymesh nodes: subdivision/displacement build the macro
        // silhouette, the autobump recovers sub-pixel detail finer than the
        // subdivision grid. Defaults are byte-identical to pre-step-5
        // (autobump: false; strength/scale ignored when off).
        bool autobump = disp.Autobump;
        float autobumpStrength = disp.AutobumpStrength;
        float autobumpScale = disp.AutobumpScale > 0f ? disp.AutobumpScale : 1f;

        if (autobump && autobumpStrength <= 0f)
        {
            Warn($"Material '{ownerTag}': 'autobump' is enabled but " +
                 $"'autobump_strength' is {autobumpStrength} (≤ 0). The autobump " +
                 $"would be a no-op; disabling it.");
            autobump = false;
        }

        // The Cycles tri-state forces autobump on/off independently of the
        // YAML 'autobump:' toggle: bump_only means the texture IS the bump
        // (autobump always on); displacement_only means no bump at all.
        if (method == DisplacementMethod.BumpOnly)
            autobump = true;
        if (method == DisplacementMethod.Displacement)
            autobump = false;

        var opts = new DisplacementOptions
        {
            Mode             = mode,
            Space            = space,
            Texture          = inner,
            Scale            = disp.Scale,
            Midlevel         = disp.Midlevel,
            Bound            = bound,
            UvScale          = disp.UvScale > 0f ? disp.UvScale : 1f,
            Autobump         = autobump,
            AutobumpStrength = autobumpStrength,
            AutobumpScale    = autobumpScale,
        };

        return new LeafDisplacement(opts, method);
    }

    /// <summary>
    /// Translates the <see cref="EntityData"/> subdivision fields into
    /// an engine-level <see cref="SubdivisionOptions"/> struct. Unknown
    /// scheme strings produce a warning and disable subdivision; the
    /// entity's translate/rotate/scale matrix is composed with the
    /// camera context so the pixel-error heuristic is measured in
    /// world-space.
    /// </summary>
    private static SubdivisionOptions BuildSubdivisionOptions(
        EntityData e, SubdivisionScreenContext screen)
    {
        if (e.SubdivisionIterations <= 0 && e.SubdivisionPixelError <= 0f
            && string.IsNullOrWhiteSpace(e.SubdivisionScheme))
        {
            return SubdivisionOptions.Disabled;
        }

        SubdivisionScheme scheme = SubdivisionScheme.Auto;
        if (!string.IsNullOrWhiteSpace(e.SubdivisionScheme))
        {
            scheme = e.SubdivisionScheme switch
            {
                "none"          => SubdivisionScheme.None,
                "loop"          => SubdivisionScheme.Loop,
                "catmull_clark" => SubdivisionScheme.CatmullClark,
                "auto"          => SubdivisionScheme.Auto,
                _ => Warned(e.SubdivisionScheme, e.Name)
            };
        }

        var entityToWorld = ComputeTransformMatrix(e);
        var ctx = new ScreenSpaceContext
        {
            CameraOrigin       = screen.CameraOrigin,
            CameraForward      = screen.CameraForward,
            ImageHeight        = screen.ImageHeight,
            VerticalFovRadians = screen.VerticalFovRadians,
            EntityToWorld      = entityToWorld,
        };

        var boundary = BoundaryInterpolation.EdgeAndCorner;
        if (!string.IsNullOrWhiteSpace(e.SubdivisionBoundary))
        {
            boundary = e.SubdivisionBoundary switch
            {
                "edge_and_corner" => BoundaryInterpolation.EdgeAndCorner,
                "edge_only"       => BoundaryInterpolation.EdgeOnly,
                _ => WarnedBoundary(e.SubdivisionBoundary, e.Name),
            };
        }

        return new SubdivisionOptions
        {
            Scheme          = scheme,
            Iterations      = Math.Max(0, e.SubdivisionIterations),
            PixelError      = Math.Max(0f, e.SubdivisionPixelError),
            MaxIterations   = Math.Max(1, e.SubdivisionMaxIterations),
            Screen          = ctx,
            Boundary        = boundary,
            CreaseAngle     = Math.Clamp(e.SubdivisionCreaseAngle, 0f, 180f),
            CreaseSharpness = e.SubdivisionCreaseSharpness is float cs && cs > 0f ? cs : float.PositiveInfinity,
        };

        static BoundaryInterpolation WarnedBoundary(string s, string? name)
        {
            Warn($"Mesh '{name ?? "(unnamed)"}': unknown subdivision_boundary " +
                 $"'{s}'. Expected edge_and_corner|edge_only. Using edge_and_corner.");
            return BoundaryInterpolation.EdgeAndCorner;
        }

        static SubdivisionScheme Warned(string s, string? name)
        {
            Warn($"Mesh '{name ?? "(unnamed)"}': unknown subdivision_scheme " +
                 $"'{s}'. Expected one of none|loop|catmull_clark|auto. " +
                 $"Falling back to auto.");
            return SubdivisionScheme.Auto;
        }
    }

    /// <summary>
    /// Camera context handed to <see cref="CreateMeshEntity"/> so the
    /// adaptive pixel-error heuristic can project mesh edges to screen
    /// space without needing the final <see cref="Camera.Camera"/> object.
    /// </summary>
    internal readonly struct SubdivisionScreenContext
    {
        public Vector3 CameraOrigin       { get; init; }
        public Vector3 CameraForward      { get; init; }
        public int      ImageHeight       { get; init; }
        public float    VerticalFovRadians { get; init; }
    }
}
