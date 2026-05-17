using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Textures;

/// <summary>
/// Blends the tangent-space normals of two child <see cref="IBumpMap"/>s
/// using the same mask/blend factor a parent <see cref="Materials.MixMaterial"/>
/// uses to mix its BSDF children. Lets a Mix material expose a single autobump
/// on the resulting <see cref="Geometry.Mesh"/> while keeping each child's
/// independent <c>autobump_strength</c> / <c>autobump_scale</c> (Cycles
/// "Mix Shader → Displacement" parity).
///
/// <para>The blend is performed in tangent-space-normal space:
/// <c>n = normalize((1−t)·nA + t·nB)</c>. Both child bumps are sampled at the
/// same UV / position / TBN, so each child's finite-difference perturbation
/// is honoured with its own amplitude and frequency — only the final shading
/// normal is combined.</para>
/// </summary>
public sealed class MixBumpMapTexture : IBumpMap
{
    private readonly IBumpMap _a;
    private readonly IBumpMap _b;
    private readonly ITexture? _mask;
    private readonly float _blend;

    /// <param name="a">Bump map of the "low blend" child (t → 0).</param>
    /// <param name="b">Bump map of the "high blend" child (t → 1).</param>
    /// <param name="mask">Optional texture mask. When null the constant
    /// <paramref name="blend"/> applies; when set, the mask's Rec.709
    /// luminance at the shading point drives the blend (same convention as
    /// MixMaterial's mask).</param>
    /// <param name="blend">Constant blend in [0, 1]. Used when
    /// <paramref name="mask"/> is null.</param>
    public MixBumpMapTexture(IBumpMap a, IBumpMap b, ITexture? mask, float blend)
    {
        _a = a;
        _b = b;
        _mask = mask;
        _blend = Math.Clamp(blend, 0f, 1f);
    }

    public Vector3 SampleTangentNormal(float u, float v, Vector3 p,
                                       Vector3 tangent, Vector3 bitangent,
                                       int seed)
    {
        float t = _blend;
        if (_mask != null)
        {
            Vector3 maskColor = _mask.Value(u, v, p, seed);
            t = Math.Clamp(MathUtils.Luminance(maskColor), 0f, 1f);
        }

        Vector3 nA = _a.SampleTangentNormal(u, v, p, tangent, bitangent, seed);
        Vector3 nB = _b.SampleTangentNormal(u, v, p, tangent, bitangent, seed);

        Vector3 blended = (1f - t) * nA + t * nB;
        float lenSq = blended.LengthSquared();
        return lenSq > 1e-12f ? blended / MathF.Sqrt(lenSq) : Vector3.UnitZ;
    }
}
