using System.Numerics;

namespace RayTracer.Textures;

/// <summary>
/// Helpers for transforming a point before it feeds a procedural texture.
///
/// <para>
/// Two concerns are intentionally kept on separate paths:
/// </para>
/// <list type="bullet">
///   <item><description><b>Manual transforms</b> (<see cref="ApplyManual"/>,
///     <see cref="ApplyRandomRotation"/>): translate / rotate the point in a way
///     that is part of the pattern's <i>geometry</i>. Used for radial
///     coordinates, directional projections, grid indexing, gradient phase —
///     i.e. anywhere the texture asks "where is this point in object space?".</description></item>
///   <item><description><b>Seed offset</b> (<see cref="SeedOffset"/>): a large
///     per-instance translation whose only purpose is to <i>decorrelate the
///     noise lookup</i> between identical-material instances. The caller adds
///     it <b>only</b> to the input of <c>Perlin</c> / <c>Voronoi</c> / fBm
///     sampling, never to the point that drives geometric terms.</description></item>
/// </list>
///
/// <para>
/// This split mirrors the convention used by Arnold (object-space <c>P</c>
/// vs <c>user_data</c> seed wired only into the noise offset), Cycles
/// (<c>Texture Coordinate → Object</c> for geometry vs
/// <c>Object Info → Random</c> wired only into the Vector input of the noise
/// node) and RenderMan (<c>Pref + PxrManifold3D</c> for geometry vs
/// <c>PxrAttribute → PxrVary</c> for per-instance randomisation). Coupling the
/// two — as the deprecated <see cref="Apply"/> did — flattens radial textures
/// (wood rings curve as 1/offset) and shifts gradient bands off-object.
/// </para>
/// </summary>
public static class TextureTransform
{
    /// <summary>
    /// Default magnitude (in world units) of <see cref="SeedOffset"/>. Chosen
    /// large enough that 6+ adjacent instances sample far-apart regions of
    /// Perlin noise (≫ the texture period at any practical <c>scale</c>) — the
    /// offset is never added to a geometric term, so its size doesn't affect
    /// radial curvature or gradient phase.
    /// </summary>
    public const float DefaultSeedOffsetMagnitude = 1000f;

    /// <summary>
    /// Apply only the user-controlled manual offset and rotation. Geometry-safe:
    /// the resulting point is what the texture should use for radial distance,
    /// directional projection, grid indexing, sine-vein phase — anything that
    /// is part of the pattern's spatial layout.
    /// </summary>
    public static Vector3 ApplyManual(Vector3 p, Vector3 offset, Vector3 rotation)
        => ApplyManual(p, Vector3.One, offset, rotation);

    /// <summary>
    /// As <see cref="ApplyManual(Vector3, Vector3, Vector3)"/>, but with a
    /// per-axis <paramref name="scale"/> applied to the point <i>first</i>
    /// (scale → translate → rotate). A <paramref name="scale"/> of
    /// <see cref="Vector3.One"/> reproduces the scale-free path exactly, so
    /// callers that don't opt in stay byte-identical.
    /// </summary>
    public static Vector3 ApplyManual(Vector3 p, Vector3 scale, Vector3 offset, Vector3 rotation)
    {
        Vector3 q = scale != Vector3.One ? scale * p : p;
        q += offset;
        if (rotation != Vector3.Zero)
        {
            q = Rotate(q, rotation);
        }
        return q;
    }

    /// <summary>
    /// Per-instance random rotation around the origin. Preserves <c>‖p‖</c>
    /// (it's a rigid rotation), so wood rings stay concentric and radial
    /// gradients keep their centre. Returns <paramref name="p"/> unchanged when
    /// disabled or when <paramref name="objectSeed"/> is zero.
    /// </summary>
    public static Vector3 ApplyRandomRotation(Vector3 p, int objectSeed, bool enabled)
    {
        if (!enabled || objectSeed == 0) return p;
        float rx = Hash(objectSeed, 3) * 360f;
        float ry = Hash(objectSeed, 4) * 360f;
        float rz = Hash(objectSeed, 5) * 360f;
        return Rotate(p, new Vector3(rx, ry, rz));
    }

    /// <summary>
    /// Per-instance random translation, intended <b>only</b> as an input to
    /// noise / Voronoi sampling. Returns the offset vector itself (zero when
    /// disabled or when <paramref name="objectSeed"/> is zero); the caller adds
    /// it to <c>qNoise</c>, never to the geometric <c>qGeom</c>.
    ///
    /// <para>
    /// At the default magnitude of <see cref="DefaultSeedOffsetMagnitude"/>
    /// the shift covers ≈ <c>scale × 1000</c> noise periods between instances
    /// — well past the autocorrelation length of Perlin noise — so two
    /// instances of the same material look fully decorrelated.
    /// </para>
    /// </summary>
    public static Vector3 SeedOffset(int objectSeed, bool enabled, float magnitude = DefaultSeedOffsetMagnitude)
    {
        if (!enabled || objectSeed == 0) return Vector3.Zero;
        float ox = Hash(objectSeed, 0) * magnitude;
        float oy = Hash(objectSeed, 1) * magnitude;
        float oz = Hash(objectSeed, 2) * magnitude;
        return new Vector3(ox, oy, oz);
    }

    /// <summary>
    /// Uniform value in [0, 1) from a seed and an axis, by integer mixing
    /// (SplitMix64 finaliser) so it is the same on every platform. The shader
    /// hash fract(sin(x) * 43758.5453) it replaces amplified the last bits of
    /// <c>sin</c>, which differ between the math libraries of Linux, Windows
    /// and macOS: the same scene placed its texture offsets differently.
    /// </summary>
    private static float Hash(int seed, int axis)
    {
        ulong z = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL + (ulong)(axis + 1) * 0xD1B54A32D192ED03UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (z >> 40) * (1f / (1UL << 24));
    }

    public static Vector3 Rotate(Vector3 p, Vector3 degrees)
    {
        float rx = degrees.X * MathF.PI / 180f;
        float ry = degrees.Y * MathF.PI / 180f;
        float rz = degrees.Z * MathF.PI / 180f;

        Vector3 v = p;

        if (rx != 0)
        {
            float cos = MathF.Cos(rx);
            float sin = MathF.Sin(rx);
            float y = v.Y * cos - v.Z * sin;
            float z = v.Y * sin + v.Z * cos;
            v = new Vector3(v.X, y, z);
        }

        if (ry != 0)
        {
            float cos = MathF.Cos(ry);
            float sin = MathF.Sin(ry);
            float x = v.X * cos + v.Z * sin;
            float z = -v.X * sin + v.Z * cos;
            v = new Vector3(x, v.Y, z);
        }

        if (rz != 0)
        {
            float cos = MathF.Cos(rz);
            float sin = MathF.Sin(rz);
            float x = v.X * cos - v.Y * sin;
            float y = v.X * sin + v.Y * cos;
            v = new Vector3(x, y, v.Z);
        }

        return v;
    }
}
