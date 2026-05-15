using System.Numerics;

namespace RayTracer.Textures;

/// <summary>
/// Procedural gradient texture — linear / quadratic / radial / spherical
/// interpolation between two colours along a configurable axis or radial
/// origin. Mirrors Cycles' Gradient Texture and Arnold's <c>ramp</c>.
///
/// In YAML:
/// <code>
/// texture:
///   type: "gradient"
///   mode: "linear"             # linear | quadratic | easing | spherical | radial
///   axis: [1, 0, 0]            # used by linear / quadratic / easing
///   colors: [[0,0,0], [1,1,1]] # endpoints (start, end)
///   length: 1.0                # world-space span over which the gradient runs
///   offset: [0,0,0]
///   rotation: [0,0,0]
/// </code>
/// </summary>
public class GradientTexture : ITexture
{
    public enum GradientMode { Linear, Quadratic, Easing, Spherical, Radial }

    private readonly Vector3 _colorA;
    private readonly Vector3 _colorB;

    public Vector3 Offset { get; set; } = Vector3.Zero;
    public Vector3 Rotation { get; set; } = Vector3.Zero;
    public bool RandomizeOffset { get; set; }
    public bool RandomizeRotation { get; set; }

    public GradientMode Mode { get; set; } = GradientMode.Linear;
    public Vector3 Axis { get; set; } = Vector3.UnitX;
    public float Length { get; set; } = 1f;

    public GradientTexture(Vector3 colorA, Vector3 colorB)
    {
        _colorA = colorA;
        _colorB = colorB;
    }

    public Vector3 Value(float u, float v, Vector3 p, int objectSeed)
    {
        Vector3 transformedP = TextureTransform.Apply(
            p, Offset, Rotation, objectSeed, RandomizeOffset, RandomizeRotation);

        float L = MathF.Max(Length, 1e-6f);
        float t;

        switch (Mode)
        {
            case GradientMode.Spherical:
                t = transformedP.Length() / L;
                break;
            case GradientMode.Radial:
            {
                Vector3 axis = Axis.LengthSquared() > 1e-12f ? Vector3.Normalize(Axis) : Vector3.UnitY;
                float along = Vector3.Dot(transformedP, axis);
                Vector3 radial = transformedP - along * axis;
                t = radial.Length() / L;
                break;
            }
            default:
            {
                Vector3 axis = Axis.LengthSquared() > 1e-12f ? Vector3.Normalize(Axis) : Vector3.UnitX;
                t = Vector3.Dot(transformedP, axis) / L;
                break;
            }
        }

        t = Math.Clamp(t, 0f, 1f);

        t = Mode switch
        {
            GradientMode.Quadratic => t * t,
            GradientMode.Easing    => t * t * (3f - 2f * t),
            _                      => t,
        };

        return Vector3.Lerp(_colorA, _colorB, t);
    }
}
