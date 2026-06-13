using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Geometry;

/// <summary>
/// One pose keyframe of an <see cref="AnimatedTransform"/>: a full TRS
/// decomposition at a normalized time in [0, 1]. Keys are stored as the
/// separate components (never recovered via <see cref="Matrix4x4.Decompose"/>)
/// so interpolation is exact: translation and scale lerp linearly, rotation
/// interpolates on the quaternion shortest arc.
/// </summary>
public readonly record struct MotionKey(float Time, Vector3 Translate, Quaternion Rotate, Vector3 Scale)
{
    /// <summary>
    /// Composed object→world matrix of this key. Matches the static-transform
    /// composition order (scale → rotate → translate, row-vector convention).
    /// </summary>
    public Matrix4x4 ToMatrix() =>
        Matrix4x4.CreateScale(Scale) *
        Matrix4x4.CreateFromQuaternion(Rotate) *
        Matrix4x4.CreateTranslation(Translate);
}

/// <summary>
/// Time-varying counterpart of <see cref="Transform"/> for transform motion
/// blur. Holds N ≥ 2 TRS keyframes; at every <c>Hit</c> the object→world
/// matrix is rebuilt at the ray's <see cref="Ray.Time"/> by interpolating the
/// surrounding pair of keys (component-wise lerp for translation/scale,
/// quaternion slerp for rotation — PBRT §2.9 AnimatedTransform, restricted to
/// TRS which is all the YAML schema can express). Ray times outside the key
/// range clamp to the end poses.
///
/// The per-hit cost is one matrix compose + invert + transpose; motion-blurred
/// objects are typically a small fraction of the scene, so no per-thread
/// matrix caching is attempted (noted as a follow-up in DEVLOG). When all keys
/// describe the same pose the constructor precomputes the static matrices once
/// and Hit follows a fast path identical in results to a static Transform.
///
/// <see cref="BoundingBox"/> returns a cached union of the world-space bounds
/// swept over the whole key range, so the static BVH built on top never culls
/// a true hit at any time: each segment is sampled at enough intermediate
/// poses (rotation-angle driven), and each sampled box is padded by the
/// chord-vs-arc bound so corners travelling on rotation arcs between samples
/// stay inside.
///
/// Deliberately NOT ISamplable: animated emissive geometry is registered for
/// NEE as a static snapshot at the mid-shutter pose (see
/// <see cref="NeeSnapshotMatrix"/> and SceneLoader.ExtractGeometryLightsRecursive).
/// </summary>
public class AnimatedTransform : IHittable
{
    private readonly IHittable _object;
    private readonly MotionKey[] _keys;

    // Fast path when every key is the same pose: behave exactly like a static
    // Transform (also makes the identical-keys equivalence test exact).
    private readonly bool _isStatic;
    private readonly Matrix4x4 _staticTransform;
    private readonly Matrix4x4 _staticInverse;
    private readonly Matrix4x4 _staticNormalMatrix;

    private readonly AABB _motionBox;

    /// <summary>
    /// The wrapped IHittable (in object space). Used by SceneLoader/Group
    /// IsInfinitePlane() detection and by ExtractGeometryLightsRecursive().
    /// </summary>
    public IHittable Inner => _object;

    /// <summary>
    /// Object→world matrix at the midpoint of the key range — the static pose
    /// used to register animated emissive geometry in the NEE light pool
    /// (sample positions, pdf probes and ApproximatePower all share this one
    /// self-consistent snapshot; visibility rays still run at the true ray
    /// time and BSDF-hit emission sees the real motion).
    /// </summary>
    public Matrix4x4 NeeSnapshotMatrix { get; }

    public AnimatedTransform(IHittable hittable, IReadOnlyList<MotionKey> keys)
    {
        if (keys == null || keys.Count < 2)
            throw new ArgumentException("AnimatedTransform requires at least 2 motion keys.", nameof(keys));

        _object = hittable;
        _keys = keys.OrderBy(k => k.Time).ToArray();

        _isStatic = true;
        for (int i = 1; i < _keys.Length && _isStatic; i++)
        {
            _isStatic = _keys[i].Translate == _keys[0].Translate
                     && _keys[i].Scale == _keys[0].Scale
                     && QuaternionsEquivalent(_keys[i].Rotate, _keys[0].Rotate);
        }

        if (_isStatic)
        {
            _staticTransform = _keys[0].ToMatrix();
            if (!Matrix4x4.Invert(_staticTransform, out _staticInverse))
                _staticInverse = Matrix4x4.Identity;
            _staticNormalMatrix = Matrix4x4.Transpose(_staticInverse);
        }

        NeeSnapshotMatrix = MatrixAt(0.5f * (_keys[0].Time + _keys[^1].Time));
        _motionBox = ComputeMotionBox();
    }

    public int Seed
    {
        get => _object.Seed;
        set => _object.Seed = value;
    }

    /// <summary>Interpolated TRS pose at time <paramref name="t"/> (clamped to the key range).</summary>
    public MotionKey PoseAt(float t)
    {
        if (t <= _keys[0].Time) return _keys[0];
        if (t >= _keys[^1].Time) return _keys[^1];

        int i = 1;
        while (_keys[i].Time < t) i++;
        ref readonly MotionKey a = ref _keys[i - 1];
        ref readonly MotionKey b = ref _keys[i];

        float span = b.Time - a.Time;
        float u = span > 0f ? (t - a.Time) / span : 0f;
        return new MotionKey(
            t,
            Vector3.Lerp(a.Translate, b.Translate, u),
            Quaternion.Slerp(a.Rotate, b.Rotate, u), // shortest arc (System.Numerics negates as needed)
            Vector3.Lerp(a.Scale, b.Scale, u));
    }

    /// <summary>Object→world matrix at time <paramref name="t"/> (clamped to the key range).</summary>
    public Matrix4x4 MatrixAt(float t) => PoseAt(t).ToMatrix();

    public bool Hit(in Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        Matrix4x4 transform, inverse, normalMatrix;
        Vector3 scale;

        if (_isStatic)
        {
            transform = _staticTransform;
            inverse = _staticInverse;
            normalMatrix = _staticNormalMatrix;
            scale = _keys[0].Scale;
        }
        else
        {
            MotionKey pose = PoseAt(ray.Time);
            transform = pose.ToMatrix();
            if (!Matrix4x4.Invert(transform, out inverse))
                inverse = Matrix4x4.Identity;
            normalMatrix = Matrix4x4.Transpose(inverse);
            scale = pose.Scale;
        }

        Ray localRay = Transform.ToLocalRay(ray, inverse);

        if (!_object.Hit(localRay, tMin, tMax, ref rec))
            return false;

        Transform.MapHitToWorld(ref rec, transform, normalMatrix, Vector3.Abs(scale));
        return true;
    }

    public AABB BoundingBox() => _motionBox;

    /// <summary>
    /// Conservative union of the world-space bounds over the whole key range.
    /// Translation/scale lerp moves box corners along straight lines, so for
    /// those segments the two endpoint boxes suffice (AABB union is convex).
    /// Rotation moves corners along arcs that bulge outside the chord between
    /// sampled poses, so rotating segments are (a) sampled every ≤15° of
    /// rotation and (b) each sampled box is padded by the maximal sagitta
    /// r_max·(1 − cos(θ_step/2)) — the largest distance an arc of θ_step can
    /// stray from its chord at radius r_max (farthest inner-box corner from
    /// the object origin under the largest key scale).
    /// </summary>
    private AABB ComputeMotionBox()
    {
        AABB localBox = _object.BoundingBox();

        if (_isStatic)
            return Transform.ComputeWorldBox(localBox, _staticTransform);

        AABB box = AABB.Empty;
        float maxSagitta = 0f;

        // Farthest corner of the local box from the object-space origin (the
        // rotation pivot), under the largest |scale| component of any key.
        float maxScale = 0f;
        foreach (var k in _keys)
        {
            Vector3 s = Vector3.Abs(k.Scale);
            maxScale = MathF.Max(maxScale, MathF.Max(s.X, MathF.Max(s.Y, s.Z)));
        }
        Vector3 farCorner = Vector3.Max(Vector3.Abs(localBox.Min), Vector3.Abs(localBox.Max));
        float rMax = farCorner.Length() * maxScale;

        for (int i = 1; i < _keys.Length; i++)
        {
            ref readonly MotionKey a = ref _keys[i - 1];
            ref readonly MotionKey b = ref _keys[i];

            // Rotation swept by this segment (shortest arc between the keys).
            float cosHalf = MathF.Min(1f, MathF.Abs(Quaternion.Dot(
                Quaternion.Normalize(a.Rotate), Quaternion.Normalize(b.Rotate))));
            float angleDeg = 2f * MathF.Acos(cosHalf) * (180f / MathF.PI);

            int steps = angleDeg > 1e-3f
                ? Math.Clamp((int)MathF.Ceiling(angleDeg / 15f), 1, 32)
                : 1;

            for (int s = (i == 1 ? 0 : 1); s <= steps; s++)
            {
                float t = a.Time + (b.Time - a.Time) * (s / (float)steps);
                box = AABB.SurroundingBox(box, Transform.ComputeWorldBox(localBox, MatrixAt(t)));
            }

            if (angleDeg > 1e-3f && float.IsFinite(rMax))
            {
                float stepRad = (angleDeg / steps) * (MathF.PI / 180f);
                maxSagitta = MathF.Max(maxSagitta, rMax * (1f - MathF.Cos(stepRad * 0.5f)));
            }
        }

        if (maxSagitta > 0f)
        {
            var pad = new Vector3(maxSagitta);
            box = new AABB(box.Min - pad, box.Max + pad);
        }
        return box;
    }

    private static bool QuaternionsEquivalent(Quaternion a, Quaternion b) =>
        a == b || a == -b; // q and -q encode the same rotation
}
