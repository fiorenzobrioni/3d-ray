using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Textures;

/// <summary>
/// Procedural wood — concentric annual rings perturbed by fractal noise grain.
///
/// <para>
/// The ring radius is measured perpendicular to <see cref="RingAxis"/>: the
/// rings form circles in the plane orthogonal to the axis, so a tree trunk
/// uses <c>ring_axis: [0, 1, 0]</c> (rings appear on cross-cut), a plank uses
/// <c>ring_axis: [0, 0, 1]</c>, etc. Default Y matches RenderMan/Arnold's
/// default wood orientation.
/// </para>
///
/// <para>
/// <b>Studio-quality features</b> on top of the original grain+ring model
/// (DEVLOG step 5/7 "VFX production-grade" textures):
/// </para>
/// <list type="bullet">
///   <item><description>Two-band perturbation: <see cref="GrainScale"/> /
///     <see cref="NoiseStrength"/> (high-frequency fibre detail inside each
///     ring) and <see cref="FigureScale"/> / <see cref="FigureStrength"/>
///     (low-frequency plank-wide "figure" — curly maple, flame mahogany,
///     bird's eye). Each band has its own scale and amplitude.</description></item>
///   <item><description><see cref="RadialAnisotropy"/>: stretches the noise
///     sample point along the local radial direction, mimicking the
///     quartersawn (high anisotropy) vs plain-sawn (low) board cuts.</description></item>
///   <item><description><see cref="KnotDensity"/>: small-scale Voronoi
///     spawns of branch knots that locally bend the ring pattern around a
///     new centre and add a dark heart — same trick used by Arnold's
///     <c>knots</c> map and RenderMan's <c>PxrWoodKnot</c>.</description></item>
///   <item><description>Configurable ring axis (any direction, not hard-coded XZ).</description></item>
///   <item><description><c>RingSharpness</c>: latewood/earlywood transition exponent — 1 = soft, ≥3 = hard ring lines.</description></item>
///   <item><description>Optional axial grain via <c>AxialGrain</c> — long-wave noise along the trunk axis.</description></item>
///   <item><description>Domain warp via <c>Distortion</c> for knots / waved figure.</description></item>
/// </list>
///
/// In YAML:
/// <code>
/// texture:
///   type: "wood"
///   scale: 4.0
///   noise_strength: 2.0        # grain amplitude (alias: grain_strength)
///   colors: [[0.85,0.65,0.40], [0.60,0.40,0.20]]
///   ring_axis: [0, 1, 0]       # axis of the trunk; rings ⊥ axis
///   ring_sharpness: 2.0        # 1=soft (legacy), 3-6=defined latewood
///   axial_grain: 0.0           # long-wave noise along the axis
///   octaves: 4                 # fBm octaves on the grain
///   lacunarity: 2.0
///   gain: 0.5
///   distortion: 0.0            # 0=clean rings, ~0.5=knots/waves
///   grain_scale: 1.0           # multiplier on the high-freq noise sample point
///   figure_scale: 0.25         # multiplier on the low-freq "figure" sample point
///   figure_strength: 0.0       # 0 = disabled, ~1 = pronounced curly maple
///   radial_anisotropy: 0.0     # 0 = isotropic, &gt;0 = quartersawn-stretched
///   knot_density: 0.0          # 0 = no knots, ~0.5 = sparse, ~1 = packed
/// </code>
/// </summary>
public class WoodTexture : ITexture
{
    private readonly Perlin _noise;
    private readonly float _scale;
    private readonly ITexture _lightWoodColor;
    private readonly ITexture _darkWoodColor;

    public Vector3 Offset { get; set; } = Vector3.Zero;
    public Vector3 Rotation { get; set; } = Vector3.Zero;
    public bool RandomizeOffset { get; set; }
    public bool RandomizeRotation { get; set; }
    public float NoiseStrength { get; set; } = 2f;

    public Vector3 RingAxis { get; set; } = Vector3.UnitY;
    public float RingSharpness { get; set; } = 1f;
    public float AxialGrain { get; set; } = 0f;
    public int Octaves { get; set; } = 1;
    public float Lacunarity { get; set; } = 2f;
    public float Gain { get; set; } = 0.5f;
    public float Distortion { get; set; } = 0f;

    /// <summary>
    /// Frequency multiplier on the high-frequency grain noise sample point.
    /// Default 1 reproduces the legacy single-band behaviour bit-for-bit.
    /// Higher values shrink the grain features (more fibre detail per unit
    /// length); lower values stretch them.
    /// </summary>
    public float GrainScale { get; set; } = 1f;

    /// <summary>
    /// Frequency multiplier on the low-frequency "figure" noise sample point.
    /// Only active when <see cref="FigureStrength"/> &gt; 0. The figure band
    /// adds plank-wide undulations independent of the grain — curly maple
    /// stripes, flame mahogany ripples, bird's-eye blooms — that pure grain
    /// noise cannot reproduce because its spectrum is too high-frequency.
    /// Default 0.25 puts the figure feature size at ~4× the grain size.
    /// </summary>
    public float FigureScale { get; set; } = 0.25f;

    /// <summary>
    /// Amplitude of the figure band. 0 (default) ⇒ disabled — the texture is
    /// byte-identical to the legacy single-band wood. Values around 0.5–1.5
    /// produce visible figure; higher values dominate over the grain.
    /// </summary>
    public float FigureStrength { get; set; } = 0f;

    /// <summary>
    /// Anisotropic stretching of the noise sample point along the local
    /// radial direction (perpendicular to <see cref="RingAxis"/>, pointing
    /// away from the trunk axis). 0 (default) ⇒ isotropic, matches the
    /// legacy output. &gt; 0 compresses the radial coordinate of the sample
    /// point, so noise varies slower along the radial direction and the
    /// grain appears "stretched" — the quartersawn-oak look.
    /// </summary>
    public float RadialAnisotropy { get; set; } = 0f;

    /// <summary>
    /// Probability of branch-knot spawning, in [0, 1]. 0 (default) ⇒ no
    /// knots, byte-identical to legacy. Higher values seed more knots via a
    /// small-scale Voronoi: when a sample point falls inside a knot cell,
    /// the local ring centre is pulled toward the knot feature point and a
    /// dark heart is added — same kind of behaviour as Arnold's <c>knots</c>
    /// procedural and RenderMan's <c>PxrWoodKnot</c>.
    /// </summary>
    public float KnotDensity { get; set; } = 0f;

    /// <summary>
    /// Optional multi-stop colour ramp. When set, the ring parameter
    /// <c>t ∈ [0, 1]</c> is looked up on the ramp instead of being linearly
    /// blended between the dark and light wood colours — unlocks sapwood /
    /// heartwood / knot tri-tone authoring.
    /// </summary>
    public ColorRamp? ColorRamp { get; set; }

    public WoodTexture(float scale = 4f, float turbulenceStrength = 2f)
        : this(scale, turbulenceStrength,
               new Vector3(0.85f, 0.65f, 0.40f),
               new Vector3(0.60f, 0.40f, 0.20f)) { }

    public WoodTexture(float scale, float turbulenceStrength, Vector3 lightColor, Vector3 darkColor)
    {
        _noise = new Perlin();
        _scale = scale;
        NoiseStrength = turbulenceStrength;
        _lightWoodColor = new SolidColor(lightColor);
        _darkWoodColor = new SolidColor(darkColor);
    }

    public Vector3 Value(float u, float v, Vector3 p, int objectSeed)
    {
        // Geometric q: drives the radial distance from the ring axis. Must
        // stay rooted at the object origin so rings are concentric — this
        // means NO per-instance seed offset on this path.
        Vector3 qGeom = TextureTransform.ApplyRandomRotation(
            TextureTransform.ApplyManual(p, Offset, Rotation),
            objectSeed, RandomizeRotation);
        Perlin noise = objectSeed != 0 ? Perlin.GetOrCreate(objectSeed) : _noise;

        Vector3 q = qGeom;
        if (Distortion > 0f)
        {
            // Domain warp is a geometric perturbation of the ring shape (waves,
            // knots): use qGeom-space noise so the warp is consistent with the
            // rings' centre.
            q += Distortion * noise.NoiseVector(q + new Vector3(3.1f, 7.7f, 1.9f));
        }

        // Distance from the ring axis (radial coordinate in the plane ⊥ axis).
        Vector3 axis = RingAxis.LengthSquared() > 1e-12f ? Vector3.Normalize(RingAxis) : Vector3.UnitY;
        float along = Vector3.Dot(q, axis);
        Vector3 radial = q - along * axis;
        float dist = radial.Length();

        // ── Per-instance noise decorrelation ──────────────────────────────
        // Added ONLY to the grain/figure sampling input (qNoise) — never to
        // qGeom — so concentric rings stay rooted on the object axis while
        // adjacent instances see uncorrelated fibre patterns.
        Vector3 noiseShift = TextureTransform.SeedOffset(objectSeed, RandomizeOffset);

        // ── Radial anisotropy ──────────────────────────────────────────────
        // Stretch the noise sampling along the local radial direction by
        // shrinking the radial component of the sample point. As anisotropy
        // grows the radial coordinate compresses ⇒ noise varies slowly along
        // the radial axis (the quartersawn look). RadialAnisotropy == 0 is a
        // bit-identical no-op (the back-compat invariant).
        Vector3 qNoise = q;
        if (RadialAnisotropy > 0f && dist > 1e-6f)
        {
            Vector3 rHat = radial / dist;
            float anisoFactor = 1f / (1f + RadialAnisotropy);
            // Replace the radial component with a compressed copy; the axial
            // and tangential components stay the same (only the radial axis
            // is rescaled).
            float rComp = Vector3.Dot(q, rHat);
            qNoise = q - rComp * rHat + (rComp * anisoFactor) * rHat;
        }
        qNoise += noiseShift;

        // ── Two-band grain + figure perturbation ──────────────────────────
        // High-frequency band ("grain"): fibre detail inside each ring. The
        // single-octave Perlin path is kept as the legacy fallback (matches
        // the original ray-tracing-in-one-weekend wood output bit-for-bit).
        Vector3 qGrain = qNoise * GrainScale;
        float grain = Octaves <= 1
            ? noise.Noise(qGrain)
            : noise.Fbm(qGrain, Octaves, Lacunarity, Gain, signed: true);

        // Low-frequency "figure" band — only sampled when enabled.
        // Independent seed offset on the sample point so the two bands don't
        // correlate (the curly-maple wide ripples must not lock-step with
        // the fine grain).
        float figure = 0f;
        if (FigureStrength > 0f)
        {
            Vector3 qFigure = qNoise * FigureScale + new Vector3(127.13f, 89.41f, 53.27f);
            figure = noise.Noise(qFigure);
        }

        // Combined noise contribution: legacy `NoiseStrength * grain` term
        // augmented by the new figure band. With FigureStrength = 0 the
        // expression collapses back to the original.
        float noiseTotal = NoiseStrength * grain + FigureStrength * figure;

        // Optional long-wave variation along the trunk axis (gentle waves on planks).
        if (AxialGrain > 0f)
        {
            noiseTotal += AxialGrain * noise.Noise(new Vector3(along * 0.5f, 0f, 0f));
        }

        // Ring distance — what determines the latewood/earlywood band index.
        float ringDist = dist + noiseTotal;

        // ── Knot spawning via small-scale Voronoi ─────────────────────────
        // Sparse jittered grid of branch-knot candidates. When the sample
        // point falls close to a knot feature the apparent ring centre is
        // pulled toward the knot (concentric rings around the knot) and a
        // dark heart is added on top.
        float knotDarken = 0f;
        if (KnotDensity > 0f)
        {
            WorleyNoise worley = objectSeed != 0
                ? WorleyNoise.GetOrCreate(objectSeed)
                : WorleyNoise.GetOrCreate(0);
            // Voronoi space is the perpendicular plane only — knots extend
            // along the trunk axis like real branch stubs. Decorrelated from
            // the noise sample space by a large constant offset.
            Vector3 perpPlane = q - along * axis + new Vector3(41.17f, 0f, 17.83f);
            // Knot density modulates the cell size: higher density = smaller
            // cells = more knots. Frequency factor calibrated so density = 1
            // yields ~one knot per natural ring at default scale.
            float knotFreq = 0.35f + 0.65f * KnotDensity;
            worley.Evaluate(perpPlane * knotFreq, WorleyNoise.Metric.Euclidean,
                            randomness: 1f, out float kF1, out _, out _);

            // Threshold below which we consider the sample inside a knot.
            // Calibrated so that even modest densities produce knots large
            // enough to host visible concentric rings inside them — matches
            // how Arnold's `knots` map and RenderMan's PxrWoodKnot expose the
            // feature (a knot covering 30–45% of the local cell, not a pixel
            // dot). At density 1 the threshold is 0.45 so cells stay mostly
            // partitioned (no global "everything is knot" degenerate case).
            float knotThreshold = 0.45f * KnotDensity;
            if (kF1 < knotThreshold)
            {
                float t01 = kF1 / knotThreshold;          // 0 = centre, 1 = edge
                float mask = 1f - t01;
                mask *= mask;                              // sharpen falloff

                // Pull the ring centre toward the knot: the local "ring
                // distance" becomes the distance to the knot feature, so
                // the same sin()-style band pattern wraps around the knot.
                float knotRingDist = kF1 / knotFreq;       // back to q-space units
                ringDist += (knotRingDist - ringDist) * mask;

                // Dark heart at the knot centre, sharper than the ring band.
                knotDarken = mask * mask;
            }
        }

        float ring = ringDist * _scale;
        float t = ring - MathF.Floor(ring);

        if (RingSharpness != 1f && RingSharpness > 0f)
        {
            // Smoothstep-like sharpening on a triangular wave centred at 0.5
            // pulls the latewood band into a narrow dark line, matching what
            // Arnold's "wood" and RenderMan's PxrWood produce by default.
            float tri = 1f - MathF.Abs(t * 2f - 1f);
            tri = MathF.Pow(tri, RingSharpness);
            t = tri;
        }

        // Apply knot heart darkening AFTER ring sharpening so the knot reads
        // as a discrete dark spot regardless of which ring band the sample
        // falls into.
        if (knotDarken > 0f)
        {
            t *= (1f - knotDarken);
        }

        if (ColorRamp is { } ramp)
        {
            // Ramp drives the colour directly: t = 0 → first stop (typically
            // the darkest latewood), t = 1 → last stop (typically the
            // brightest earlywood).
            return ramp.Sample(t);
        }

        Vector3 cLight = _lightWoodColor.Value(u, v, qGeom, objectSeed);
        Vector3 cDark = _darkWoodColor.Value(u, v, qGeom, objectSeed);
        return Vector3.Lerp(cDark, cLight, t);
    }
}
