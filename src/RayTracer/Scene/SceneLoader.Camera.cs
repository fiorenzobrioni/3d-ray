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
    // =========================================================================
    // Camera resolution
    // =========================================================================

    /// <summary>
    /// Reads the YAML file and lists all cameras defined in <c>cameras:</c>.
    /// Called by Program.cs when <c>--list-cameras</c> is passed.
    /// </summary>
    /// <remarks>
    /// This method is invoked standalone (outside the Load flow), so it writes
    /// directly to Console — there is no "Loading scene... done" wrapper to conflict with.
    /// </remarks>
    public static void TryListCameras(string yamlPath)
    {
        var data = Parse(yamlPath).Data;
        if (data.Cameras == null || data.Cameras.Count == 0)
        {
            Console.WriteLine("No camera defined in the scene.");
            return;
        }

        Console.WriteLine($"Cameras in scene ({data.Cameras.Count}):");
        for (int i = 0; i < data.Cameras.Count; i++)
        {
            var c     = data.Cameras[i];
            string label = c.Name != null ? $"  #{i}  \"{c.Name}\"" : $"  #{i}  (unnamed)";
            // A physical lens is listed as written: its vertical fov depends on
            // the image aspect, which is not known here.
            string lens = c.FocalLength is float f
                ? $"focal={f:0.#}mm/{c.SensorWidth:0.#}mm sensor" + (c.FStop is float n ? $" f/{n:0.##}" : "")
                : $"fov={c.Fov}°";
            Console.WriteLine($"{label}   {lens}  pos={FormatVec(c.Position)}");
        }
    }

    /// <summary>
    /// The camera to render: <paramref name="selector"/> matched by name
    /// (case-insensitive), then by zero-based index; without a selector the
    /// first camera (with a note when there are several); a default camera
    /// when the scene has none.
    /// </summary>
    private static CameraData ResolveCamera(SceneData data, string? selector)
    {
        if (data.Cameras == null || data.Cameras.Count == 0)
        {
            if (selector != null)
                Warn($"--camera '{selector}' was specified but the scene has no cameras. Ignoring selector.");
            return new CameraData();
        }

        var list = data.Cameras;

        if (selector != null)
        {
            var byName = list.FirstOrDefault(
                c => string.Equals(c.Name, selector, StringComparison.OrdinalIgnoreCase));
            if (byName != null)
                return byName;

            if (int.TryParse(selector, out int idx))
            {
                if (idx >= 0 && idx < list.Count)
                    return list[idx];

                Warn($"--camera index {idx} is out of range " +
                     $"(scene has {list.Count} cameras, indices 0–{list.Count - 1}). " +
                     "Using camera 0.");
                return list[0];
            }

            var names = string.Join(", ",
                list.Select((c, i) => c.Name != null ? $"\"{c.Name}\" (#{i})" : $"#{i}"));
            Warn($"--camera '{selector}' not found. Using camera 0.");
            Warn($"Available cameras: {names}");
            return list[0];
        }

        if (list.Count == 1)
            return list[0];

        var cameraNames = string.Join(", ",
            list.Select((c, i) => c.Name != null ? $"\"{c.Name}\" (#{i})" : $"#{i}"));
        Info($"Cameras:     {list.Count} - using camera 0 (available cameras: {cameraNames})");
        return list[0];
    }

    private static string FormatVec(List<float>? v) =>
        v is { Count: >= 3 } ? $"[{v[0]:F2}, {v[1]:F2}, {v[2]:F2}]" : "(default)";

    // =========================================================================
    // Factory helpers
    // =========================================================================

    /// <summary>
    /// Scans all scene objects and registers ISamplable emissives as GeometryLights
    /// for Next Event Estimation (NEE / direct illumination).
    ///
    /// Handles:
    ///   1. Bare primitive with Emissive material.
    ///   2. Transform-wrapped primitive (at any nesting depth, transforms composed).
    ///   3. Group / Transform(Group(...)) / Transform(Transform(...)) : recurses
    ///      into children, composing transforms for correct world-space sampling.
    ///   4. MixMaterial(Emissive, …) on a primitive — registered as a geometry light
    ///      using a synthetic <see cref="Emissive"/> built from the blended emission
    ///      of the two sub-materials (see <see cref="ExtractMixEmissive"/>).
    ///   5. CSG containing an Emissive leaf — emits a one-time warning because
    ///      CSG is not ISamplable and therefore cannot participate in NEE.
    /// </summary>
    private static void ExtractGeometryLights(List<IHittable> objects, List<ILight> lights, int shadowSamples)
    {
        foreach (var obj in objects)
            ExtractGeometryLightsRecursive(obj, lights, shadowSamples, Matrix4x4.Identity, hasOuterTransform: false);
    }

    /// <summary>
    /// Converts the photographic camera keys into the thin-lens parameters the
    /// camera uses: <c>focal_length</c> + <c>sensor_width</c> → vertical field
    /// of view (the sensor width spans the image width, as on a still camera;
    /// the lens is treated as focused at infinity, so no focus breathing), and
    /// <c>f_stop</c> → aperture diameter = focal length / N, in scene units via
    /// the scene's <c>units.length</c>. Without <c>focal_length</c>, <c>f_stop</c> uses
    /// the focal length that gives the scene's <c>fov</c> on that sensor.
    /// Returns the aperture shape (blades, rotation, ratio).
    /// <paramref name="metersPerUnit"/>: the scene's length unit (default: the scene being loaded).
    /// </summary>
    public static Camera.ApertureShape ApplyPhysicalCamera(CameraData cam, float aspect, float? metersPerUnit = null)
    {
        float mpu = metersPerUnit ?? _metersPerUnit;
        float sensorWidth = cam.SensorWidth;
        if (!(sensorWidth > 0f))
        {
            Warn($"Camera 'sensor_width' must be > 0 (got {sensorWidth}); using 36 mm.");
            sensorWidth = 36f;
        }
        float sensorHeight = sensorWidth / aspect;

        if (cam.FocalLength is float f)
        {
            if (f > 0f)
                cam.Fov = 2f * MathF.Atan(sensorHeight / (2f * f)) * 180f / MathF.PI;
            else
                Warn($"Camera 'focal_length' must be > 0 mm (got {f}); keeping fov = {cam.Fov}.");
        }

        if (cam.FStop is float n)
        {
            if (n > 0f)
            {
                float focalMm = cam.FocalLength is > 0f
                    ? cam.FocalLength.Value
                    : sensorHeight / (2f * MathF.Tan(cam.Fov * MathF.PI / 360f));
                if (cam.Aperture > 0f)
                    Info($"Camera 'f_stop' {n} overrides 'aperture' {cam.Aperture}.");
                cam.Aperture = focalMm / n / 1000f / mpu;
            }
            else
                Warn($"Camera 'f_stop' must be > 0 (got {n}); keeping aperture = {cam.Aperture}.");
        }

        int blades = cam.ApertureBlades;
        if (blades is 1 or 2 || blades < 0)
        {
            Warn($"Camera 'aperture_blades' must be 0 (round) or at least 3 (got {blades}); using a round aperture.");
            blades = 0;
        }
        float ratio = cam.ApertureRatio;
        if (!(ratio > 0f))
        {
            Warn($"Camera 'aperture_ratio' must be > 0 (got {ratio}); using 1.");
            ratio = 1f;
        }
        return new Camera.ApertureShape(blades, cam.ApertureRotation, ratio);
    }

    /// <summary>
    /// Resolves the camera focus distance from either a YAML
    /// <c>focus_point: [x, y, z]</c> (Arnold/Cycles "Focus Point" workflow)
    /// or the scalar <c>focus_distance</c> fallback.
    ///
    /// <para><b>Math.</b> When <paramref name="focalPos"/> is set, the
    /// focus distance is the projection of the camera→focal-point vector
    /// onto the optical axis <c>forward = normalize(lookAt − camPos)</c>:
    /// <c>focusDist = (F − camPos) · forward</c>. The focus plane is
    /// perpendicular to the view direction passing through the focal
    /// point — so a focal point off-axis at <c>(3, 4, −5)</c> with the
    /// camera at the origin looking along <c>−Z</c> yields focus distance
    /// <c>5</c>, not the Euclidean <c>√50</c>. This matches every
    /// production renderer (Arnold, Cycles, RenderMan).</para>
    ///
    /// <para><b>Anomaly handling.</b> Falls back to
    /// <paramref name="fallbackFocalDist"/> with a <see cref="Warn"/> when
    /// the focal point is malformed, behind the camera, coincident with
    /// it, or when the camera itself is degenerate (lookAt == camPos).
    /// Emits <see cref="Info"/> when both fields are set so the user knows
    /// which one took effect.</para>
    /// </summary>
    public static float ComputeFocusDistance(
        Vector3 camPos, Vector3 lookAt,
        List<float>? focalPos, float fallbackFocalDist)
    {
        if (focalPos == null) return fallbackFocalDist;

        if (focalPos.Count < 3)
        {
            Warn("Camera 'focus_point' requires 3 components [x, y, z]. " +
                 "Falling back to 'focus_distance'.");
            return fallbackFocalDist;
        }

        var forward = lookAt - camPos;
        float forwardLen = forward.Length();
        if (forwardLen < 1e-6f)
        {
            Warn("Camera 'focus_point' ignored: 'position' and 'look_at' " +
                 "coincide (degenerate camera). Falling back to 'focus_distance'.");
            return fallbackFocalDist;
        }
        forward /= forwardLen;

        var F = new Vector3(focalPos[0], focalPos[1], focalPos[2]);
        float projected = Vector3.Dot(F - camPos, forward);
        if (projected <= 1e-4f)
        {
            Warn($"Camera 'focus_point' projects behind or onto the camera " +
                 $"(distance = {projected:F4}). Falling back to 'focus_distance'.");
            return fallbackFocalDist;
        }

        // focus_distance defaults to 1: any other value was written.
        if (MathF.Abs(fallbackFocalDist - 1f) > 1e-6f)
            Info($"Camera 'focus_point' overrides 'focus_distance' " +
                 $"(computed focus distance = {projected:F3}, " +
                 $"YAML focus_distance = {fallbackFocalDist:F3}).");

        return projected;
    }

    /// <summary>
    /// Builds the camera keyframe list from a <c>camera.motion:</c> block, or
    /// returns null when the camera is static. The camera's base pose is the
    /// implicit key at time 0; omitted fields in a key inherit the base.
    /// </summary>
    private static Camera.CameraKey[]? BuildCameraMotionKeys(
        CameraData camData, Vector3 basePos, Vector3 baseLookAt, Vector3 baseVup)
    {
        if (camData.Motion is not { Count: > 0 }) return null;

        var byTime = new SortedDictionary<float, Camera.CameraKey>
        {
            [0f] = new Camera.CameraKey(0f, basePos, baseLookAt, baseVup, camData.Fov)
        };

        foreach (var k in camData.Motion)
        {
            float t = k.Time;
            if (t < 0f || t > 1f)
            {
                Warn($"Camera '{camData.Name ?? "(default)"}': motion key time {t:0.###} outside [0, 1] — clamped.");
                t = Math.Clamp(t, 0f, 1f);
            }
            byTime[t] = new Camera.CameraKey(
                t,
                ToVector3(k.Position) ?? basePos,
                ToVector3(k.LookAt) ?? baseLookAt,
                ToVector3(k.Up) ?? baseVup,
                k.Fov ?? camData.Fov);
        }

        return byTime.Count >= 2 ? byTime.Values.ToArray() : null;
    }

    /// <summary>
    /// Validates the camera <c>shutter_interval: [open, close]</c> and combines it with
    /// the scene-motion flag. The renderer samples a time per camera ray only
    /// when <see cref="MotionBlurSettings.Active"/> is true, so a static scene
    /// stays bit-identical regardless of any declared shutter.
    /// </summary>
    private static MotionBlurSettings ResolveMotionBlurSettings(List<float>? shutter, bool sceneHasMotion)
    {
        float open = 0f, close = 1f;
        if (shutter != null)
        {
            if (shutter.Count == 2 && shutter[0] >= 0f && shutter[0] < shutter[1] && shutter[1] <= 1f)
            {
                open = shutter[0];
                close = shutter[1];
            }
            else
            {
                Warn($"Invalid camera shutter_interval [{string.Join(", ", shutter)}] — expected [open, close] " +
                     $"with 0 <= open < close <= 1. Using [0, 1].");
            }

            if (!sceneHasMotion)
                Info("Camera shutter_interval declared but nothing is animated — motion blur inactive.");
        }

        return new MotionBlurSettings(open, close, sceneHasMotion);
    }
}
