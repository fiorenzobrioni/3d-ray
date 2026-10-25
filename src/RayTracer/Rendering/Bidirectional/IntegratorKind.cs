namespace RayTracer.Rendering;

/// <summary>Light transport algorithm (<c>--integrator</c>).</summary>
public enum IntegratorKind
{
    /// <summary>Unidirectional path tracing with next-event estimation (the default).</summary>
    Path,
    /// <summary>
    /// Bidirectional path tracing: camera and light subpaths joined at every
    /// pair of vertices, the strategies combined by MIS (Veach 1997).
    /// </summary>
    Bdpt,
    /// <summary>
    /// Vertex connection and merging (Georgiev et al. 2012): bidirectional
    /// path tracing plus progressive photon merging, for the caustics no
    /// connection reaches (light focused by glass or water and seen through it).
    /// </summary>
    Vcm,
}
