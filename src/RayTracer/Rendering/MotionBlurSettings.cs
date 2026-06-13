namespace RayTracer.Rendering;

/// <summary>
/// Motion-blur configuration produced by the scene loader and consumed by the
/// renderer. <see cref="Active"/> is true only when the scene actually moves
/// (at least one animated entity or an animated camera): when false the
/// renderer draws no time sample and the output stays bit-identical to a
/// build without motion blur — the same invariant volumetrics and AOV capture
/// follow. A shutter declared on a scene with nothing animated is reported by
/// the loader and leaves <see cref="Active"/> false.
/// </summary>
public readonly struct MotionBlurSettings
{
    /// <summary>Shutter-open time, normalized to the scene animation range [0, 1].</summary>
    public float ShutterOpen { get; }

    /// <summary>Shutter-close time, normalized to the scene animation range [0, 1].</summary>
    public float ShutterClose { get; }

    /// <summary>True when at least one entity or the camera is animated.</summary>
    public bool Active { get; }

    /// <summary>
    /// Shutter midpoint — the fixed time used for passes that cannot afford a
    /// per-sample time (NEE snapshot of animated emissives, caustic photons).
    /// </summary>
    public float MidTime => 0.5f * (ShutterOpen + ShutterClose);

    public MotionBlurSettings(float shutterOpen, float shutterClose, bool active)
    {
        ShutterOpen = shutterOpen;
        ShutterClose = shutterClose;
        Active = active;
    }
}
