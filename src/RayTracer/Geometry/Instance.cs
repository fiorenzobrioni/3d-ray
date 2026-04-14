using RayTracer.Core;
using RayTracer.Materials;

namespace RayTracer.Geometry;

/// <summary>
/// Wraps a shared template <see cref="IHittable"/> so multiple Instance objects
/// can point to the same geometry — paying memory for geometry, BVH and meshes
/// only once per template — while keeping per-instance Seed and an optional
/// per-instance Material override.
///
/// <b>Memory model:</b>
/// <see cref="Scene.SceneLoader"/> builds each template once and caches it.
/// Every YAML <c>type: instance</c> produces a new Instance wrapping the same
/// reference. For 1000 instances of a 100k-triangle mesh, geometry and BVH
/// memory are paid a single time.
///
/// <b>Per-instance state intercepted in Hit():</b>
/// <list type="bullet">
///   <item><c>rec.ObjectSeed</c>: forced to this instance's <see cref="Seed"/>,
///   so procedural textures (marble, wood, noise) differ between instances even
///   when the underlying geometry is shared.</item>
///   <item><c>rec.Material</c>: replaced by <c>OverrideMaterial</c> if non-null,
///   leaving the template's per-child materials in place otherwise.</item>
/// </list>
///
/// <b>Material override semantics</b> (intentional design choice, see DEVLOG #22):
/// when <c>OverrideMaterial</c> is set, ALL surfaces of the instance use that
/// material — even template children that had explicit materials. To preserve
/// the template's internal material variety, omit the <c>material</c> field on
/// the YAML instance and the per-child materials from the template will show
/// through unchanged.
///
/// <b>Seed handling:</b> the per-instance Seed is stored locally and applied to
/// <c>rec.ObjectSeed</c> after the template's Hit. It is NEVER written into the
/// shared template — that would race between instances during parallel rendering
/// and stomp on the seeds of other instances at load time.
///
/// <b>Limitation — emissives in templates:</b> because a single shared geometry
/// is registered as a single object, emissive surfaces inside an instanced
/// template do not participate in Next Event Estimation as separate lights per
/// instance. They remain visible through BSDF sampling (indirect rays will hit
/// them), but soft shadows / direct sampling are not provided. This is an
/// accepted trade-off: scenes needing many emissive instances are uncommon and
/// supporting them would require per-instance light registration with composed
/// transforms — a sizable complication for a marginal use case.
/// </summary>
public class Instance : IHittable
{
    private readonly IHittable _template;
    private readonly IMaterial? _overrideMaterial;
    private int _seed;

    public Instance(IHittable template, IMaterial? overrideMaterial = null)
    {
        _template = template;
        _overrideMaterial = overrideMaterial;
    }

    /// <summary>
    /// Per-instance seed for procedural textures. Stored locally and applied
    /// to <c>rec.ObjectSeed</c> in <see cref="Hit"/>; never propagated into the
    /// shared template.
    /// </summary>
    public int Seed
    {
        get => _seed;
        set => _seed = value;
    }

    public bool Hit(Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        if (!_template.Hit(ray, tMin, tMax, ref rec))
            return false;

        rec.ObjectSeed = _seed;

        if (_overrideMaterial != null)
            rec.Material = _overrideMaterial;

        return true;
    }

    public AABB BoundingBox() => _template.BoundingBox();
}
