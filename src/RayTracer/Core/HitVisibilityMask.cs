namespace RayTracer.Core;

/// <summary>
/// Per-ray-category "invisible to this kind of ray" bitmask stored on a
/// <see cref="HitRecord"/>. Mirrors Arnold's <c>polymesh.visibility</c> /
/// Cycles' "Ray Visibility" panel. A set bit means the renderer must skip the
/// hit when traversing a ray of the corresponding category.
///
/// <para>The categories map 1:1 with <see cref="Rendering.RayCategory"/> used
/// by the sky environment so the engine has a single coherent vocabulary for
/// visibility across surfaces, lights and the environment.</para>
/// </summary>
[System.Flags]
public enum HitVisibilityMask : byte
{
    None         = 0,
    Camera       = 1 << 0,
    Diffuse      = 1 << 1,
    Glossy       = 1 << 2,
    Transmission = 1 << 3,
    Shadow       = 1 << 4,
}
