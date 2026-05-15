using System.Numerics;

namespace RayTracer.Textures;

/// <summary>
/// Multi-stop colour gradient evaluated at scalar position <c>t ∈ [0, 1]</c>.
///
/// <para>
/// Replaces the implicit two-colour lerp baked into every procedural texture
/// (<see cref="NoiseTexture"/>, <see cref="MarbleTexture"/>,
/// <see cref="WoodTexture"/>, <see cref="VoronoiTexture"/>,
/// <see cref="GradientTexture"/>) with an arbitrary number of stops, each
/// carrying its own outgoing interpolation curve. The classic two-stop
/// <c>colors: [A, B]</c> remains the default; setting <c>color_ramp: [...]</c>
/// overrides it.
/// </para>
///
/// <para>
/// Matches the feature set of Cycles' ColorRamp node, Arnold's
/// <c>ramp_rgb</c> shader and RenderMan's <c>PxrRamp</c>:
/// <list type="bullet">
///   <item><description><b>Linear</b> — straight lerp between consecutive stops.</description></item>
///   <item><description><b>Smoothstep</b> — Hermite cubic <c>3t² − 2t³</c> (C¹ continuity).</description></item>
///   <item><description><b>Ease</b> — Perlin smootherstep <c>6t⁵ − 15t⁴ + 10t³</c> (C² continuity).</description></item>
///   <item><description><b>Constant</b> — hold the stop's colour until the next stop is reached.</description></item>
/// </list>
/// The interpolation kind on each stop describes how the gradient leaves
/// <i>that</i> stop on its way to the next one (Blender's convention).
/// </para>
/// </summary>
public sealed class ColorRamp
{
    public enum Interp
    {
        Linear,
        Smoothstep,
        Constant,
        Ease,
    }

    public readonly struct Stop
    {
        public readonly float Position;
        public readonly Vector3 Color;
        public readonly Interp Interpolation;

        public Stop(float position, Vector3 color, Interp interpolation = Interp.Linear)
        {
            Position = position;
            Color = color;
            Interpolation = interpolation;
        }
    }

    private readonly Stop[] _stops;

    public int StopCount => _stops.Length;
    public Stop this[int i] => _stops[i];

    /// <summary>
    /// Builds a ramp from a (possibly unsorted) sequence of stops. Positions
    /// are clamped to <c>[0, 1]</c> and stops are sorted ascending by
    /// position. Coincident stops are preserved (artist trick for sharp
    /// transitions: two stops at the same <c>position</c> with different
    /// colours).
    /// </summary>
    public ColorRamp(IEnumerable<Stop> stops)
    {
        ArgumentNullException.ThrowIfNull(stops);
        Stop[] arr = stops
            .Select(s => new Stop(Math.Clamp(s.Position, 0f, 1f), s.Color, s.Interpolation))
            .OrderBy(s => s.Position)
            .ToArray();
        if (arr.Length == 0)
            throw new ArgumentException("ColorRamp requires at least one stop.", nameof(stops));
        _stops = arr;
    }

    /// <summary>
    /// Returns the colour at parameter <paramref name="t"/>. Values outside
    /// <c>[0, 1]</c> clamp to the first / last stop. With a single stop the
    /// ramp returns that stop's colour everywhere.
    /// </summary>
    public Vector3 Sample(float t)
    {
        if (_stops.Length == 1) return _stops[0].Color;
        if (float.IsNaN(t) || t <= _stops[0].Position) return _stops[0].Color;
        if (t >= _stops[^1].Position) return _stops[^1].Color;

        // Binary search for the segment [left, right] that brackets t.
        int lo = 0, hi = _stops.Length - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (_stops[mid].Position <= t) lo = mid; else hi = mid;
        }

        Stop left = _stops[lo];
        Stop right = _stops[hi];

        // Coincident stops: zero-width segment → snap to the right stop's
        // colour. Artists rely on this to author hard breaks.
        float span = right.Position - left.Position;
        if (span <= 0f) return right.Color;

        float u = (t - left.Position) / span;
        u = Math.Clamp(u, 0f, 1f);

        return left.Interpolation switch
        {
            Interp.Constant   => left.Color,
            Interp.Smoothstep => Vector3.Lerp(left.Color, right.Color, u * u * (3f - 2f * u)),
            Interp.Ease       => Vector3.Lerp(left.Color, right.Color, u * u * u * (u * (u * 6f - 15f) + 10f)),
            _                 => Vector3.Lerp(left.Color, right.Color, u),
        };
    }
}
