namespace RayTracer.Volumetrics;

/// <summary>
/// Value pair identifying the participating media on the two sides of a
/// surface — the medium the ray is in BEFORE the hit (<see cref="Exterior"/>)
/// and the one it would enter on refraction (<see cref="Interior"/>).
///
/// <para>Carried on <see cref="Core.HitRecord.MediumIface"/> for every hit
/// against a geometry that the loader has bound to one or more media via
/// <see cref="Geometry.MediumBoundHittable"/>. A null entry on either side
/// means "no medium" — i.e. vacuum (or the outer stack's current top).
/// </para>
///
/// <para>Pattern Mitsuba 0.6 / PBRT v3 §11.3 / v4 §11.4. The interior /
/// exterior split lets the renderer track refractive entry / exit
/// transitions without consulting the material — the geometry alone
/// determines the volume topology.</para>
/// </summary>
public readonly struct MediumInterface
{
    public readonly IMedium? Interior;
    public readonly IMedium? Exterior;

    public MediumInterface(IMedium? interior, IMedium? exterior)
    {
        Interior = interior;
        Exterior = exterior;
    }

    /// <summary>True when both sides of the boundary refer to the same medium
    /// (or both are null). On such "matched" boundaries the renderer can skip
    /// the stack swap — the ray traverses the same medium across the surface.</summary>
    public bool IsMatched => ReferenceEquals(Interior, Exterior);

    /// <summary>Picks the medium that the ray is *exiting from* (the one it
    /// was just travelling through) given which face it hit. Useful when the
    /// renderer needs the active medium for a shadow ray cast from the
    /// surface — it queries the side the ray is on.</summary>
    public IMedium? On(bool frontFace) => frontFace ? Exterior : Interior;

    /// <summary>Picks the medium the ray would *enter into* on a transmission
    /// across this boundary, given the face it hit.</summary>
    public IMedium? Across(bool frontFace) => frontFace ? Interior : Exterior;
}
