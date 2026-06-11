namespace RayTracer.Denoising;

/// <summary>Denoiser backend selector (CLI <c>--denoiser</c>).</summary>
public enum DenoiserKind
{
    None,
    /// <summary>Joint NL-means: dual-buffer cross-filtered non-local means
    /// guided by the prefiltered albedo/normal/depth features. Cheaper than
    /// <see cref="Nfor"/>, slightly softer on fine texture.</summary>
    Nlm,
    /// <summary>Feature-guided first-order regression (NFOR family): the
    /// NL-means weights drive a per-window weighted linear fit on the
    /// prefiltered features, reconstructing detail the plain weighted average
    /// blurs away. The production default.</summary>
    Nfor,
}

/// <summary>Speed/quality trade-off (CLI <c>--denoise-quality</c>).</summary>
public enum DenoiseQuality
{
    /// <summary>Smaller search window, single filter candidate.</summary>
    Fast,
    /// <summary>Full search window, two filter-strength candidates combined
    /// per pixel by estimated MSE.</summary>
    High,
}

/// <summary>Resolved denoiser configuration.</summary>
public sealed class DenoiserOptions
{
    public DenoiserKind Kind { get; init; } = DenoiserKind.Nfor;
    public DenoiseQuality Quality { get; init; } = DenoiseQuality.High;

    /// <summary>Search-window radius R of the main filter (window is
    /// (2R+1)²).</summary>
    internal int SearchRadius => Quality == DenoiseQuality.High ? 9 : 7;

    /// <summary>Patch radius P of the NL-means patch distance (7×7).</summary>
    internal const int PatchRadius = 3;

    /// <summary>Filter-strength candidates k (NL-means bandwidth scale). High
    /// quality runs two and picks per pixel by estimated MSE.</summary>
    internal float[] CandidateK => Quality == DenoiseQuality.High
        ? new[] { 0.5f, 1.0f }
        : new[] { 0.7f };

    /// <summary>NFOR regression windows are solved on a stride-2 grid and
    /// splatted collaboratively over their full window (the overlapping
    /// predictions average out) — 4× fewer solves at no visible cost.</summary>
    internal const int RegressionStride = 2;
}
