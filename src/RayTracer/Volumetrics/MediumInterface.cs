namespace RayTracer.Volumetrics;

// Stage 2 placeholder: per-entity inside/outside media boundaries.
public sealed class MediumInterface
{
    public IMedium? Inside { get; }
    public IMedium? Outside { get; }
    public MediumInterface(IMedium? inside, IMedium? outside) { Inside = inside; Outside = outside; }
    public bool IsTransition => !ReferenceEquals(Inside, Outside);
}
