namespace RayTracer.Geometry;

/// <summary>
/// Boolean operations for Constructive Solid Geometry (CSG).
/// </summary>
public enum CsgOperation
{
    /// <summary>A ∪ B — combined volume of both solids.</summary>
    Union,

    /// <summary>A ∩ B — only the overlapping volume.</summary>
    Intersection,

    /// <summary>A \ B — solid A with solid B carved out.</summary>
    Subtraction
}
