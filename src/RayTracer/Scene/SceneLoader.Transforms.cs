using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Geometry.Subdivision;
using RayTracer.Materials;
using RayTracer.Lights;
using RayTracer.Acceleration;
using RayTracer.Textures;
using RayTracer.Rendering;
using RayTracer.Rendering.Sky;
using RayTracer.Volumetrics;

namespace RayTracer.Scene;
public partial class SceneLoader
{
    /// <summary>
    /// An entity's transform: <c>scale</c>, then <c>rotate</c> (Euler degrees in
    /// <c>rotate_order</c>, default xyz: X first), then <c>translate</c>; or a
    /// <c>matrix</c> (16 numbers, row by row, acting on column vectors).
    /// </summary>
    private static Matrix4x4 ComputeTransformMatrix(EntityData e)
    {
        if (e.Matrix != null)
        {
            if (e.Translate != null || e.Rotate != null || e.Scale != null)
                Warn($"Entity '{e.Name ?? e.Type ?? "(unnamed)"}': 'matrix' replaces translate / rotate / scale; those are ignored.");
            if (e.Matrix.Count == 16)
            {
                var a = e.Matrix;
                // System.Numerics multiplies row vectors: the transpose.
                return new Matrix4x4(a[0], a[4], a[8], a[12], a[1], a[5], a[9], a[13],
                                     a[2], a[6], a[10], a[14], a[3], a[7], a[11], a[15]);
            }
            Warn($"Entity '{e.Name ?? e.Type ?? "(unnamed)"}': 'matrix' needs 16 numbers (got {e.Matrix.Count}); ignored.");
            return Matrix4x4.Identity;
        }

        var m = Matrix4x4.CreateScale(ParseScaleVector(e.Scale));
        m *= RotationMatrix(e.Rotate, e.RotateOrder);
        if (e.Translate != null && e.Translate.Count >= 3)
            m *= Matrix4x4.CreateTranslation(e.Translate[0], e.Translate[1], e.Translate[2]);
        return m;
    }

    /// <summary>Euler degrees applied axis by axis in <paramref name="order"/> (default xyz).</summary>
    private static Matrix4x4 RotationMatrix(List<float>? rotate, string? order)
    {
        if (rotate == null || rotate.Count < 3) return Matrix4x4.Identity;
        var m = Matrix4x4.Identity;
        foreach (char axis in order ?? "xyz")
        {
            m *= axis switch
            {
                'x' => Matrix4x4.CreateRotationX(MathUtils.DegreesToRadians(rotate[0])),
                'y' => Matrix4x4.CreateRotationY(MathUtils.DegreesToRadians(rotate[1])),
                _   => Matrix4x4.CreateRotationZ(MathUtils.DegreesToRadians(rotate[2])),
            };
        }
        return m;
    }

    // =========================================================================
    // Motion blur (entity keyframes, camera keyframes, shutter)
    // =========================================================================

    /// <summary>
    /// Parses a YAML <c>scale</c> value (scalar or <c>[x, y, z]</c> list) into
    /// a scale vector.
    /// </summary>
    private static Vector3 ParseScaleVector(object? scale)
    {
        if (scale == null) return Vector3.One;
        if (scale is List<object> list && list.Count >= 3)
        {
            return new Vector3(
                Convert.ToSingle(list[0], System.Globalization.CultureInfo.InvariantCulture),
                Convert.ToSingle(list[1], System.Globalization.CultureInfo.InvariantCulture),
                Convert.ToSingle(list[2], System.Globalization.CultureInfo.InvariantCulture));
        }
        float s = Convert.ToSingle(scale, System.Globalization.CultureInfo.InvariantCulture);
        return new Vector3(s);
    }

    /// <summary>
    /// Euler angles as a quaternion, with the order and row-vector convention
    /// of <see cref="ComputeTransformMatrix"/>, so slerped keyframe rotations
    /// agree with the static transform at the keys.
    /// </summary>
    private static Quaternion RotationQuaternion(List<float>? rotate, string? order = null)
    {
        if (rotate == null || rotate.Count < 3) return Quaternion.Identity;
        return Quaternion.CreateFromRotationMatrix(RotationMatrix(rotate, order));
    }

    /// <summary>
    /// Builds the sorted keyframe list for an entity's <c>motion:</c> block.
    /// The base <c>translate</c>/<c>rotate</c>/<c>scale</c> is the implicit key
    /// at time 0; components omitted in a motion key inherit the base pose.
    /// Out-of-range times are clamped to [0, 1] and duplicated times collapse
    /// last-wins, both with a warning.
    /// </summary>
    private static MotionKey[] BuildMotionKeys(EntityData e)
    {
        string label = e.Name ?? e.Type ?? "(unnamed)";

        var baseT = ToVector3(e.Translate) ?? Vector3.Zero;
        var baseR = RotationQuaternion(e.Rotate, e.RotateOrder);
        var baseS = ParseScaleVector(e.Scale);

        // time → key; the base pose seeds time 0 and an explicit `time: 0`
        // motion key overrides it (last-wins).
        var byTime = new SortedDictionary<float, MotionKey>
        {
            [0f] = new MotionKey(0f, baseT, baseR, baseS)
        };

        foreach (var k in e.Motion!)
        {
            float t = k.Time;
            if (t < 0f || t > 1f)
            {
                Warn($"Entity '{label}': motion key time {t:0.###} outside [0, 1] — clamped.");
                t = Math.Clamp(t, 0f, 1f);
            }
            if (byTime.ContainsKey(t) && t != 0f)
                Warn($"Entity '{label}': duplicate motion key at time {t:0.###} — last one wins.");

            byTime[t] = new MotionKey(
                t,
                k.Translate != null ? (ToVector3(k.Translate) ?? baseT) : baseT,
                k.Rotate != null ? RotationQuaternion(k.Rotate, e.RotateOrder) : baseR,
                k.Scale != null ? ParseScaleVector(k.Scale) : baseS);
        }

        return byTime.Values.ToArray();
    }
}
