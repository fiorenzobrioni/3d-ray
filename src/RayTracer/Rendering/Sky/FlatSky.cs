using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Rendering.Sky;

/// <summary>
/// Uniform-color sky: same radiance in every direction. Used for indoor/
/// studio scenes that want a constant ambient fill, or as the deliberate
/// "black sky" for HDRI-replacement testing.
/// </summary>
public class FlatSky : ISkyModel
{
    public Vector3 Color { get; }

    public FlatSky(Vector3 color) { Color = color; }

    public Vector3 EvaluateRadiance(Vector3 dirLocal) => Color;

    public float EstimatedAverageLuminance => MathUtils.Luminance(Color);

    // Uniform sphere sampling is degenerate: every direction has the same PDF
    // and the same radiance, so MC with N samples gives the exact answer with
    // zero variance — no need to expose a CDF. We still report HasImportanceSampling=true
    // when the colour is non-zero so the EnvironmentLight registers in NEE
    // (otherwise a black sky would pollute the light list).
    public bool HasImportanceSampling => MathUtils.Luminance(Color) > 1e-6f;

    private const float UniformSpherePdf = 1f / (4f * MathF.PI);

    public (Vector3 Direction, Vector3 Radiance, float Pdf) ImportanceSample()
    {
        return (MathUtils.RandomUnitVector(), Color, UniformSpherePdf);
    }

    public float Pdf(Vector3 dirLocal) => HasImportanceSampling ? UniformSpherePdf : 0f;

    public bool HasAnalyticalSun => false;
    public (Vector3 Direction, Vector3 Radiance, float CosHalfAngle, bool LimbDarkening) AnalyticalSun
        => (Vector3.UnitY, Vector3.Zero, 1f, false);
}
