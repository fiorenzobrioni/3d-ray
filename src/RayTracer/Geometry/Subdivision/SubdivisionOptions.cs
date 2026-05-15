namespace RayTracer.Geometry.Subdivision;

/// <summary>
/// Surface subdivision scheme. Matches the YAML <c>subdivision_scheme</c>
/// field and the algorithms exposed by Arnold, RenderMan and Cycles.
///
/// <list type="bullet">
///   <item>
///     <term>None</term>
///     <description>No subdivision. Default for legacy meshes.</description>
///   </item>
///   <item>
///     <term>Loop</term>
///     <description>Loop 1987 subdivision — works on triangle meshes.
///     n-gons in the source are fan-triangulated first.</description>
///   </item>
///   <item>
///     <term>CatmullClark</term>
///     <description>Catmull-Clark 1978 — works on quad meshes. Triangles
///     and n-gons are accepted and produce quads in the first iteration,
///     after which the mesh is all-quads and the regular masks apply.
///     </description>
///   </item>
///   <item>
///     <term>Auto</term>
///     <description>Pick Catmull-Clark when every input face is a quad,
///     Loop when every input face is a triangle, Catmull-Clark otherwise.
///     Mirrors the auto-detection in OpenSubdiv's API tutorials.</description>
///   </item>
/// </list>
/// </summary>
public enum SubdivisionScheme
{
    None,
    Loop,
    CatmullClark,
    Auto,
}

/// <summary>
/// Parameters controlling the subdivision pass run by <see cref="ObjLoader"/>
/// after parsing the file. Fields with default values mean "no subdivision";
/// the loader treats <see cref="Iterations"/> 0 and a <see cref="Scheme"/>
/// of <see cref="SubdivisionScheme.None"/> as a no-op.
/// </summary>
public readonly struct SubdivisionOptions
{
    /// <summary>Subdivision algorithm to use.</summary>
    public SubdivisionScheme Scheme { get; init; }

    /// <summary>
    /// Number of subdivision iterations (uniform mode). Each iteration
    /// multiplies face count by 4 and roughly halves average edge length.
    /// Clamped to <c>[0, MaxIterations]</c> by the loader.
    /// </summary>
    public int Iterations { get; init; }

    /// <summary>
    /// Target screen-space edge length in pixels for the adaptive heuristic.
    /// When &gt; 0 the loader computes an iteration count that brings the
    /// projected average edge below this threshold, then takes the larger of
    /// this estimate and <see cref="Iterations"/>. Set to 0 to disable.
    /// </summary>
    public float PixelError { get; init; }

    /// <summary>
    /// Upper bound on iterations regardless of the adaptive estimate. Each
    /// step costs 4× memory so we cap the runaway case. Default 6 (≈ 4 096×
    /// face explosion).
    /// </summary>
    public int MaxIterations { get; init; }

    /// <summary>
    /// Camera-to-mesh world-space context for the adaptive heuristic.
    /// Populated by <see cref="Scene.SceneLoader"/> from the resolved camera.
    /// </summary>
    public ScreenSpaceContext? Screen { get; init; }

    public bool IsActive =>
        Scheme != SubdivisionScheme.None && (Iterations > 0 || PixelError > 0);

    public static SubdivisionOptions Disabled => new()
    {
        Scheme = SubdivisionScheme.None,
        Iterations = 0,
        PixelError = 0f,
        MaxIterations = 6,
        Screen = null,
    };
}

/// <summary>
/// World-to-pixel projection context used by the adaptive pixel-error
/// heuristic. Stores the camera origin, the camera-to-target axis and the
/// vertical pixels-per-radian factor so the loader can convert a world-space
/// edge length to its on-screen footprint without depending on the rendering
/// transform of the entity itself.
/// </summary>
public readonly struct ScreenSpaceContext
{
    public System.Numerics.Vector3 CameraOrigin { get; init; }
    public System.Numerics.Vector3 CameraForward { get; init; }
    /// <summary>Image height in pixels.</summary>
    public int ImageHeight { get; init; }
    /// <summary>Vertical field of view in radians.</summary>
    public float VerticalFovRadians { get; init; }
    /// <summary>Camera→entity transform applied to mesh-local positions
    /// before projection (entity translate/rotate/scale composed).</summary>
    public System.Numerics.Matrix4x4 EntityToWorld { get; init; }
}
