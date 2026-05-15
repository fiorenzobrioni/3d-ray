using RayTracer.Textures;

namespace RayTracer.Geometry.Subdivision;

/// <summary>
/// Which displacement formula <see cref="DisplacementEngine"/> applies to
/// the limit-surface vertices.
///
/// <para><b>Scalar</b> — Rec.709 luminance of the texture drives a single
/// signed amount that is added along the smooth normal. Step 3 of the
/// DEVLOG surface-displacement roadmap. The canonical "height-field
/// displacement" of Arnold (<c>displacementShader</c>), RenderMan
/// (<c>PxrDisplace</c>) and Cycles ("True Displacement").</para>
///
/// <para><b>Vector</b> — the full RGB of the texture is interpreted as a
/// 3D offset and added directly to the position. Step 4 of the roadmap;
/// the standard pipeline for sculpted hi-res detail baked onto a low-poly
/// cage that produces overhangs and crinkles a single-axis height map
/// cannot represent. Matches Arnold's <c>vector_displacement</c>, Cycles'
/// vector-displacement output of the Displacement node and RenderMan's
/// <c>PxrDispVectorLayer</c> / <c>PxrDispScalarLayer</c> family.</para>
/// </summary>
public enum DisplacementMode
{
    Scalar,
    Vector,
}

/// <summary>
/// Coordinate frame in which a vector displacement texture is authored.
/// Ignored when <see cref="DisplacementOptions.Mode"/> is
/// <see cref="DisplacementMode.Scalar"/>.
///
/// <para><b>Tangent</b> — the typical bake-from-sculpt convention:
/// <c>R → T</c>, <c>G → B</c>, <c>B → N</c>. The engine builds a
/// per-vertex TBN basis on the limit topology (Lengyel-style tangents
/// from UV gradients, plus the smooth normal) and transforms each
/// sampled RGB into the local-space offset. Requires a UV channel.
/// This is the canonical Mudbox / Maya / ZBrush / Cycles-tangent bake
/// format.</para>
///
/// <para><b>Object</b> — the RGB triplet is interpreted directly as an
/// <c>(x, y, z)</c> offset in the mesh's local coordinate system, with no
/// TBN rotation. Matches Arnold's <c>vector_space: object</c>, Cycles'
/// "Object" space and RenderMan's <c>dispSpace="object"</c>. Works on
/// meshes that have no UV channel at all (procedural-driven offsets read
/// from the 3D position) and on meshes whose tangent frames are ambiguous
/// (concentric spirals, dense seams).</para>
///
/// <para>World space is intentionally not supported: in our pipeline
/// displacement runs in the mesh's local frame before the entity transform
/// is applied, so a world-space mode would have to fold the transform into
/// the engine. The two modes above cover the same authoring surface as
/// every production renderer.</para>
/// </summary>
public enum DisplacementSpace
{
    Tangent,
    Object,
}

/// <summary>
/// Surface displacement parameters. Translates the YAML
/// <c>displacement: {...}</c> block on a mesh entity into engine-level
/// values consumed by <see cref="DisplacementEngine"/>.
///
/// <para>Scalar mode applies <c>v' = v + scale · (h − midlevel) · n_smooth</c>
/// with <c>h = Rec.709 luminance(texture.Value(u, v, p))</c>. Vector mode
/// applies <c>v' = v + scale · (rgb − midlevel) · basis</c> where
/// <c>basis</c> is the per-vertex TBN (tangent space) or the identity
/// (object space).</para>
///
/// <para>The smooth normal <c>n_smooth</c> — and the basis-N axis for
/// tangent-space vector displacement — is the angle-weighted average of
/// incident face normals (Max 1999 — the production default of Blender,
/// Maya and OpenSubdiv).</para>
///
/// <para>This is the canonical surface-displacement parameter set of
/// Arnold (<c>displacementShader</c> / <c>vector_displacement</c>),
/// RenderMan (<c>PxrDisplace</c>'s <c>dispAmount</c>/<c>dispMidpoint</c>/
/// <c>dispBound</c> plus <c>PxrDispVectorLayer</c>) and Cycles (height /
/// vector / tangent-vector "True Displacement").</para>
/// </summary>
public readonly struct DisplacementOptions
{
    /// <summary>
    /// Which displacement formula to apply. Defaults to
    /// <see cref="DisplacementMode.Scalar"/> for back-compat with step-3
    /// scenes — the field can stay absent in YAML and the legacy
    /// height-field path runs unchanged.
    /// </summary>
    public DisplacementMode Mode { get; init; }

    /// <summary>
    /// Frame in which a vector-displacement texture is authored. Defaults
    /// to <see cref="DisplacementSpace.Tangent"/>. Has no effect when
    /// <see cref="Mode"/> is <see cref="DisplacementMode.Scalar"/>.
    /// </summary>
    public DisplacementSpace Space { get; init; }

    /// <summary>
    /// Inner displacement texture. In scalar mode the engine reads its
    /// Rec.709 luminance; in vector mode the full RGB is the
    /// <c>(x, y, z)</c> (or <c>(T, B, N)</c>) offset triplet. Same
    /// <see cref="ITexture"/> contract as <see cref="BumpMapTexture"/>'s
    /// inner texture, so a single procedural can drive any of the three
    /// channels.
    /// </summary>
    public ITexture? Texture { get; init; }

    /// <summary>
    /// World-unit amplitude of the displacement (signed). In scalar mode
    /// the actual offset is <c>scale · (h − midlevel)</c>; in vector mode
    /// the actual offset is <c>scale · (rgb − midlevel)</c> applied
    /// component-wise. 0 disables the displacement.
    /// </summary>
    public float Scale { get; init; }

    /// <summary>
    /// Reference texture value treated as "no displacement". Broadcast to
    /// all three channels in vector mode (so the natural value 0.5 maps
    /// the standard 8-bit signed-stored range <c>[0,1]</c> to centered
    /// <c>[-0.5,+0.5]</c>). Defaults to 0 (matches RenderMan's
    /// <c>dispMidpoint</c> 0 and Arnold's <c>vector_zero_value</c> 0 for
    /// signed-float EXRs).
    /// </summary>
    public float Midlevel { get; init; }

    /// <summary>
    /// Maximum expected displacement amplitude in world units. Used to pad
    /// the BVH leaf AABBs so any future shading-time bump or numerical
    /// drift on the displaced silhouette stays inside the box. Mirrors
    /// Arnold's <c>disp_padding</c> and RenderMan's <c>dispBound</c>.
    /// </summary>
    /// <remarks>
    /// In our eager-displacement pipeline the triangle vertices already
    /// reflect the post-displacement positions, so the bound is not strictly
    /// required for correctness — it functions as a safety margin and a
    /// validation hint: when the actually-applied displacement exceeds the
    /// bound the loader emits a warning so the user knows the value is
    /// under-set. In vector mode the bound is compared against the
    /// Euclidean length of the per-vertex offset (the maximum radius the
    /// displacement has pushed any vertex away from its un-displaced position).
    /// </remarks>
    public float Bound { get; init; }

    /// <summary>
    /// Uniform UV multiplier stacked on top of any per-texture <c>uv_scale</c>.
    /// Identical to the <see cref="BumpMapTexture"/> field of the same name,
    /// so authors can dial the bump and the displacement together with the
    /// same number when sharing a procedural.
    /// </summary>
    public float UvScale { get; init; }

    /// <summary>
    /// "Autobump" — when <c>true</c>, the loader builds a
    /// <see cref="BumpMapTexture"/> from <see cref="Texture"/> and attaches
    /// it to the resulting <see cref="Geometry.Mesh"/>. The renderer then
    /// applies that bump on top of any material-level <c>bump_map</c> at
    /// shading time, recovering sub-pixel detail finer than what the
    /// subdivision grid resolved geometrically. Mirrors Arnold's
    /// <c>autobump_visibility</c> on <c>polymesh</c> nodes. Disabled by
    /// default — step-3 / step-4 scenes are byte-identical when this stays
    /// off.
    /// </summary>
    public bool Autobump { get; init; }

    /// <summary>
    /// Bump-strength multiplier for the autobump-derived
    /// <see cref="BumpMapTexture"/>. Final strength passed to the bump
    /// constructor is <c>AutobumpStrength · |Scale|</c> — the displacement
    /// amplitude is the natural unit (Arnold ties the two together), and
    /// authors that want a different ratio dial this multiplier without
    /// touching the macro displacement. Ignored when
    /// <see cref="Autobump"/> is <c>false</c>.
    /// </summary>
    public float AutobumpStrength { get; init; }

    /// <summary>
    /// UV-frequency multiplier for the autobump's
    /// <see cref="BumpMapTexture"/>. Composes multiplicatively with
    /// <see cref="UvScale"/> so authors can sample the bump at finer
    /// frequency than the displacement (the typical "macro displacement +
    /// micro autobump" workflow). Ignored when <see cref="Autobump"/> is
    /// <c>false</c>.
    /// </summary>
    public float AutobumpScale { get; init; }

    /// <summary>True when both <see cref="Texture"/> is set and the scale is non-zero.</summary>
    public bool IsActive => Texture != null && Scale != 0f;

    /// <summary>True when an autobump should be generated for this mesh.</summary>
    public bool IsAutobumpActive => Autobump && Texture != null && AutobumpStrength > 0f;

    public static DisplacementOptions Disabled => new()
    {
        Mode = DisplacementMode.Scalar,
        Space = DisplacementSpace.Tangent,
        Texture = null,
        Scale = 0f,
        Midlevel = 0f,
        Bound = 0f,
        UvScale = 1f,
        Autobump = false,
        AutobumpStrength = 1f,
        AutobumpScale = 1f,
    };
}
