using RayTracer.Geometry.Subdivision;
using RayTracer.Textures;

namespace RayTracer.Materials;

/// <summary>
/// Which surface-displacement effects the material requests from the loader.
/// Matches Cycles' "Material Output → Displacement" socket modes 1:1.
///
/// <para><b>Both</b> (default) — geometric displacement of the subdivided mesh
/// AND a residual <see cref="BumpMapTexture"/> derived from the same texture
/// (when <see cref="DisplacementOptions.Autobump"/> is on). This is the
/// canonical Arnold/RenderMan/Cycles "Displacement + Bump" path.</para>
///
/// <para><b>Displacement</b> — geometric displacement only; no autobump derived
/// even if the displacement texture would be high-frequency enough for one.
/// Use this when the macro silhouette is the only thing you want and the
/// shading normals from the displaced limit topology already capture
/// everything needed.</para>
///
/// <para><b>BumpOnly</b> — no geometric displacement at all; the texture is
/// turned into a <see cref="BumpMapTexture"/> as if <c>autobump</c> had been
/// requested, then attached to the mesh. Lets authors prototype the
/// displacement texture as a pure shading effect (Cycles "Bump Only" mode)
/// without paying the subdivision cost.</para>
/// </summary>
public enum DisplacementMethod
{
    Both,
    Displacement,
    BumpOnly,
}

/// <summary>
/// Material-level displacement descriptor. Lives on <see cref="IMaterial"/>
/// (mirrors Cycles/RenderMan: the displacement is part of the material/shader
/// network so any object using that material gets it, no per-entity
/// duplication). The actual application is still eager: the mesh loader reads
/// <see cref="IMaterial.Displacement"/> off the resolved entity material and
/// hands it to <see cref="DisplacementEngine"/> before BVH construction.
///
/// <para>Two concrete kinds, chosen by the material the slot is attached to:
/// <see cref="LeafDisplacement"/> for ordinary materials (Disney, Lambertian,
/// Metal, …) and <see cref="MixDisplacement"/> for <see cref="MixMaterial"/>
/// whose two children both define their own displacement and the user wants
/// to vector-blend them at vertex time (Cycles' Mix-Shader-driven
/// displacement path).</para>
/// </summary>
public abstract class MaterialDisplacement
{
    /// <summary>
    /// Maximum displacement amplitude in world units. Drives BVH leaf-AABB
    /// padding (Arnold's <c>disp_padding</c> / RenderMan's <c>dispBound</c>).
    /// Auto-derived from <see cref="DisplacementOptions.Bound"/> in the leaf
    /// case and from <c>max(A, B)</c> in the mix case.
    /// </summary>
    public abstract float Bound { get; }

    /// <summary>
    /// True when the descriptor would change any vertex position if applied.
    /// Mix descriptors are active when either child is active.
    /// </summary>
    public abstract bool RequestsGeometricDisplacement { get; }

    /// <summary>
    /// True when an autobump should be derived from this descriptor and
    /// attached to the resulting mesh. Mix descriptors compose the children's
    /// autobump textures via <see cref="MixBumpMapTexture"/>.
    /// </summary>
    public abstract bool RequestsAutobump { get; }
}

/// <summary>
/// Wraps a single <see cref="DisplacementOptions"/> (the engine-level struct).
/// Used by every non-mix material that has a <c>displacement:</c> block in
/// its YAML entry.
/// </summary>
public sealed class LeafDisplacement : MaterialDisplacement
{
    /// <summary>The engine-level displacement parameters parsed from YAML.</summary>
    public DisplacementOptions Options { get; }

    /// <summary>
    /// Tri-state Cycles-style mode: geometric only, bump only, or both
    /// (default). When <see cref="DisplacementMethod.BumpOnly"/> the loader
    /// skips <see cref="DisplacementEngine.Apply"/> but still derives the
    /// autobump (the texture is reduced to a pure shading bump).
    /// </summary>
    public DisplacementMethod Method { get; }

    public LeafDisplacement(DisplacementOptions options, DisplacementMethod method = DisplacementMethod.Both)
    {
        Options = options;
        Method = method;
    }

    public override float Bound => Options.Bound;

    public override bool RequestsGeometricDisplacement
        => Options.IsActive && Method != DisplacementMethod.BumpOnly;

    public override bool RequestsAutobump
        => Options.Texture != null
           && Method != DisplacementMethod.Displacement
           && (Method == DisplacementMethod.BumpOnly
               || (Options.Autobump && Options.AutobumpStrength > 0f));
}

/// <summary>
/// Mix-of-displacements: vector-blends the two children's per-vertex offsets
/// using a mask/blend factor evaluated at the vertex. Matches the Cycles
/// "Mix Shader → Displacement" path where the same Mix factor drives both
/// the BSDF blend and the displacement blend so the geometry is C0-continuous
/// across material seams.
///
/// <para>The blend factor is evaluated at the vertex via the parent
/// <see cref="MixMaterial.EvaluateBlendFactorAt"/> (UV + 3D position + object
/// seed). When the mask is null the constant <c>Blend</c> applies.</para>
/// </summary>
public sealed class MixDisplacement : MaterialDisplacement
{
    /// <summary>The parent mix material that supplies the mask/blend factor.</summary>
    public MixMaterial Parent { get; }

    /// <summary>Displacement of the "low-blend" child (t → 0). Never null.</summary>
    public MaterialDisplacement A { get; }

    /// <summary>Displacement of the "high-blend" child (t → 1). Never null.</summary>
    public MaterialDisplacement B { get; }

    public MixDisplacement(MixMaterial parent, MaterialDisplacement a, MaterialDisplacement b)
    {
        Parent = parent;
        A = a;
        B = b;
    }

    public override float Bound => MathF.Max(A.Bound, B.Bound);

    public override bool RequestsGeometricDisplacement
        => A.RequestsGeometricDisplacement || B.RequestsGeometricDisplacement;

    public override bool RequestsAutobump
        => A.RequestsAutobump || B.RequestsAutobump;
}
