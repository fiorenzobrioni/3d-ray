using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Textures;

/// <summary>
/// Procedural noise texture with pro-grade controls.
///
/// <para>
/// Seven noise families are exposed through <see cref="NoiseType"/>:
/// <list type="bullet">
///   <item><description><b>perlin</b> — smooth gradient noise, signed remap to [0,1].</description></item>
///   <item><description><b>fbm</b> — fractional Brownian motion (Arnold/Cycles/RenderMan style).</description></item>
///   <item><description><b>turbulence</b> — classic |Σ noise/2^i|, sharp & cloud-like.</description></item>
///   <item><description><b>ridged</b> — Musgrave ridged multifractal, sharp ridges (rocks, veins).</description></item>
///   <item><description><b>billow</b> — Σ|noise| octaves, puffy / clumpy.</description></item>
///   <item><description><b>hetero_terrain</b> — Musgrave heterogeneous terrain
///     (§16.3.3) — rough peaks + smooth valleys, the canonical eroded-terrain look.</description></item>
///   <item><description><b>hybrid_multifractal</b> — Musgrave hybrid multifractal
///     (§16.3.4) — stratified rock layers + sharp crests.</description></item>
/// </list>
/// </para>
///
/// <para>
/// <see cref="Octaves"/>, <see cref="Lacunarity"/> and <see cref="Gain"/>
/// configure the fractal sum. <see cref="Distortion"/> domain-warps the input
/// position with a secondary Perlin sample (Inigo Quilez style) for organic
/// shapes that real Perlin can't produce on its own. <see cref="FractalIncrement"/>
/// (H) and <see cref="FractalOffset"/> drive the two Musgrave multifractal
/// variants. Backward-compat default: when no new parameters are set and
/// <c>noise_strength == 0</c>, output is identical to the legacy implementation.
/// </para>
///
/// In YAML:
/// <code>
/// texture:
///   type: "noise"
///   noise_type: "fbm"        # perlin | fbm | turbulence | ridged | billow | hetero_terrain | hybrid_multifractal
///   scale: 5.0
///   octaves: 5               # 1..16 (default 5 for fbm/ridged/billow, 7 for legacy turbulence)
///   lacunarity: 2.0          # frequency multiplier between octaves
///   gain: 0.5                # amplitude decay between octaves (fbm/ridged/billow)
///   fractal_increment: 1.0   # Musgrave H — only used by hetero_terrain / hybrid_multifractal
///   fractal_offset: 0.7      # Musgrave offset / "sea level" — only used by hetero_terrain / hybrid_multifractal
///   distortion: 0.0          # 0 = no warp, ~1 = strong organic warp
///   noise_strength: 0.0      # legacy: 0=smooth perlin, >0=turbulent (overridden by noise_type if set)
///   colors: [[0,0,0], [1,1,1]]
///   offset: [0,0,0]
///   rotation: [0,0,0]
///   randomize_offset: false
///   randomize_rotation: false
/// </code>
/// </summary>
public class NoiseTexture : ITexture
{
    public enum NoiseKind
    {
        /// <summary>Auto-select from legacy <c>noise_strength</c>: 0 → Perlin, &gt;0 → Turbulence.</summary>
        Auto,
        Perlin,
        Fbm,
        Turbulence,
        Ridged,
        Billow,
        /// <summary>Musgrave heterogeneous terrain (§16.3.3 of "Texturing &amp; Modeling").</summary>
        HeteroTerrain,
        /// <summary>Musgrave hybrid multifractal (§16.3.4).</summary>
        HybridMultifractal,
    }

    private readonly Perlin _noise;
    private readonly float _scale;
    private readonly Vector3 _colorA;
    private readonly Vector3 _colorB;

    public Vector3 Offset { get; set; } = Vector3.Zero;
    public Vector3 Rotation { get; set; } = Vector3.Zero;
    /// <summary>
    /// Per-axis scale ratio (each component in <c>[-1, 1]</c>) applied to the
    /// sample point before the scalar <c>_scale</c>, enabling anisotropic
    /// stretch from a vector <c>scale</c>. Default <see cref="Vector3.One"/> is
    /// a no-op. The dominant frequency is folded into <c>_scale</c> so the
    /// Nyquist octave clamp stays conservative.
    /// </summary>
    public Vector3 ScaleRatio { get; set; } = Vector3.One;
    public bool RandomizeOffset { get; set; }
    public bool RandomizeRotation { get; set; }

    /// <summary>
    /// Legacy turbulence weight. Kept for backward compatibility with scenes
    /// written before the multi-mode upgrade. When <see cref="NoiseType"/> is
    /// left as <see cref="NoiseKind.Auto"/>, a positive value selects classic
    /// turbulence; otherwise it scales the chosen noise output.
    /// </summary>
    public float NoiseStrength { get; set; } = 0f;

    public NoiseKind NoiseType { get; set; } = NoiseKind.Auto;
    public int Octaves { get; set; } = 5;
    public float Lacunarity { get; set; } = 2f;
    public float Gain { get; set; } = 0.5f;

    /// <summary>
    /// Domain-warp amplitude. The input position is offset by a vector-valued
    /// Perlin sample scaled by this factor, producing organic, non-axis-aligned
    /// patterns (the Inigo Quilez technique used by Cycles' Noise Distortion).
    /// </summary>
    public float Distortion { get; set; } = 0f;

    /// <summary>
    /// Musgrave <b>fractal increment</b> H (Ebert/Musgrave/Peachey/Perlin
    /// §16.3.3). Only used by <see cref="NoiseKind.HeteroTerrain"/> and
    /// <see cref="NoiseKind.HybridMultifractal"/>. Spectral weight of octave i
    /// is <c>lacunarity^(-i·H)</c>: H = 1 ⇒ statistical self-similarity,
    /// H → 0 ⇒ white-noise-ish, H ≫ 1 ⇒ smooth, low-frequency dominated.
    /// Typical terrain values land in <c>[0.1, 1.5]</c>.
    /// </summary>
    public float FractalIncrement { get; set; } = 1f;

    /// <summary>
    /// Musgrave <b>offset</b> (a.k.a. "sea level"). Only used by
    /// <see cref="NoiseKind.HeteroTerrain"/> and <see cref="NoiseKind.HybridMultifractal"/>.
    /// Additive bias inside each octave: values around 0.7 produce the canonical
    /// terrain look; raising it sinks more area below the multiplier
    /// threshold (more flat plains), lowering it raises mountains everywhere.
    /// </summary>
    public float FractalOffset { get; set; } = 0.7f;

    /// <summary>
    /// Optional multi-stop colour ramp. When set, the final scalar value
    /// <c>n ∈ [0, 1]</c> is looked up on the ramp instead of being linearly
    /// blended between the two constructor colours.
    /// </summary>
    public ColorRamp? ColorRamp { get; set; }

    public NoiseTexture(float scale = 1f)
        : this(scale, Vector3.Zero, Vector3.One) { }

    public NoiseTexture(float scale, Vector3 colorA, Vector3 colorB)
    {
        _noise = new Perlin();
        _scale = scale;
        _colorA = colorA;
        _colorB = colorB;
    }

    public Vector3 Value(float u, float v, Vector3 p, int objectSeed)
        => ValueCore(u, v, p, objectSeed, octaveOverride: -1);

    public Vector3 Value(float u, float v, Vector3 p, int objectSeed, in FilterFootprint footprint)
    {
        if (!footprint.HasFootprint) return ValueCore(u, v, p, objectSeed, octaveOverride: -1);

        // ── Analytic octave clamp (Heidrich-Slusallek 1998 §4) ──────────────
        // Perlin noise at frequency f aliases when the footprint exceeds the
        // Nyquist period 1/(2f). After the texture's _scale and per-octave
        // lacunarity, octave i has frequency f_i = scale × lacunarity^i, so
        // its period in the same units as the footprint is 1/f_i. Drop any
        // octave whose period is below 2 × footprint extent — its energy
        // would alias into the visible image instead of contributing detail.
        //
        // The footprint is in the same space as p (object/LocalPoint after
        // Transform inverse-mapped the aux rays, or world for transform-free
        // primitives). The renderer's _scale multiplies p into "noise units"
        // identically to the noise sampler below.
        float footprintExtent = footprint.MaxWorldAxis();
        int octClamped = ComputeMaxOctaves(footprintExtent);

        return ValueCore(u, v, p, objectSeed, octaveOverride: octClamped);
    }

    /// <summary>
    /// Per-frequency Nyquist criterion: the smallest octave index whose
    /// period <c>1/(scale·lacunarity^i)</c> falls below twice the footprint
    /// extent is the first to alias and must be dropped.
    /// </summary>
    private int ComputeMaxOctaves(float footprintExtent)
    {
        if (footprintExtent <= 0f) return Octaves;
        float lac = Lacunarity > 1f ? Lacunarity : 2f;
        // Period of octave 0 in noise space (after _scale):
        //   T_0 = 1 / _scale   (footprint is in p-space, so multiply by _scale to compare)
        // Octave i aliases when:
        //   T_i = 1 / (_scale · lac^i) < 2 · footprintExtent
        //   ⟹ i > log_lac (1 / (2 · _scale · footprintExtent))
        float denom = 2f * MathF.Max(_scale, 1e-12f) * footprintExtent;
        if (denom <= 0f || float.IsInfinity(denom)) return 1;
        float maxOct = MathF.Log(1f / denom) / MathF.Log(lac);
        int oct = (int)MathF.Floor(maxOct) + 1; // include the last sub-Nyquist octave
        return Math.Clamp(oct, 1, Octaves);
    }

    private Vector3 ValueCore(float u, float v, Vector3 p, int objectSeed, int octaveOverride)
    {
        // Pure-noise texture: every read of `transformedP` feeds Perlin /
        // turbulence / fBm, so the seed offset is safe to apply here on the
        // full point. The 1000-wu magnitude becomes `_scale × 1000` once the
        // line below multiplies by scale, giving thousands of noise periods
        // of decorrelation between instances.
        Vector3 transformedP = TextureTransform.ApplyManual(p, ScaleRatio, Offset, Rotation);
        transformedP = TextureTransform.ApplyRandomRotation(transformedP, objectSeed, RandomizeRotation);
        transformedP += TextureTransform.SeedOffset(objectSeed, RandomizeOffset);

        Perlin noise = objectSeed != 0 ? Perlin.GetOrCreate(objectSeed) : _noise;

        Vector3 q = _scale * transformedP;
        if (Distortion > 0f)
        {
            q += Distortion * noise.NoiseVector(q + new Vector3(13.7f, 91.3f, 27.1f));
        }

        NoiseKind kind = NoiseType;
        if (kind == NoiseKind.Auto)
        {
            kind = NoiseStrength > 0f ? NoiseKind.Turbulence : NoiseKind.Perlin;
        }

        int effOct = octaveOverride > 0 ? octaveOverride : Octaves;
        int legacyTurbulenceOct = octaveOverride > 0
            ? octaveOverride
            : (NoiseType == NoiseKind.Auto ? 7 : Octaves);

        float n = kind switch
        {
            NoiseKind.Perlin              => (noise.Noise(q) + 1f) * 0.5f,
            NoiseKind.Fbm                 => noise.Fbm(q, effOct, Lacunarity, Gain),
            NoiseKind.Ridged              => noise.Ridged(q, effOct, Lacunarity, Gain),
            NoiseKind.Billow              => noise.Billow(q, effOct, Lacunarity, Gain),
            // Legacy turbulence keeps its historical 7-octave default when the
            // user hasn't explicitly overridden `octaves`, matching the old
            // visual output exactly.
            NoiseKind.Turbulence          => noise.Turbulence(q, legacyTurbulenceOct),
            // Musgrave multifractals — the raw algorithm in Perlin.cs follows
            // the textbook recurrence (Ebert/Musgrave §16.3.3-§16.3.4) and
            // returns unbounded signed values whose magnitude diverges
            // exponentially with octave count when the running multiplier
            // exceeds 1 (canonical offset ≈ 0.7 with low H = 0.25 hits this
            // routinely — a single bounce above 1 cascades into a value of
            // ~30+, which a [0,1] clamp would flatten into pure white).
            //
            // The texture layer is responsible for mapping that unbounded
            // signal into a color-rampable [0,1] range. We do that with a
            // bounded-and-normalized variant that mirrors fBm's
            // `accum / maxAmp` rescaling: track the per-octave analytic
            // upper bound (Noise=+1 with the running multiplier saturated
            // at 1, matching the HybridMultifractal weight clamp) alongside
            // the actual value and divide at the end. The clamp prevents
            // exponential blow-up and the division puts typical samples
            // squarely in the [0,1] color-ramp domain.
            NoiseKind.HeteroTerrain       => HeteroTerrainNormalized(noise, q, effOct, Lacunarity, FractalIncrement, FractalOffset),
            NoiseKind.HybridMultifractal  => HybridMultifractalNormalized(noise, q, effOct, Lacunarity, FractalIncrement, FractalOffset),
            _                             => (noise.Noise(q) + 1f) * 0.5f,
        };

        // Apply legacy noise_strength as an amplitude multiplier for Auto-Turbulence,
        // preserving the pre-upgrade visual output bit-for-bit when neither
        // noise_type nor the new fractal params are set.
        if (NoiseType == NoiseKind.Auto && NoiseStrength > 0f)
        {
            n = Math.Clamp(n * NoiseStrength, 0f, 1f);
        }
        else
        {
            n = Math.Clamp(n, 0f, 1f);
        }

        return ColorRamp is { } ramp ? ramp.Sample(n) : Vector3.Lerp(_colorA, _colorB, n);
    }

    /// <summary>
    /// Bounded + DC-centered HeteroTerrain. Same recurrence as
    /// <see cref="Perlin.HeteroTerrain"/> but with the running multiplier
    /// clamped to [0, 1] (matching the weight invariant in Musgrave's
    /// HybridMultifractal — prevents exponential divergence when the
    /// running value exceeds 1) and the output recentered on its DC
    /// component (parallel Noise=0 path).
    ///
    /// <para>
    /// The DC component absorbs the per-octave <c>offset</c> bias that
    /// would otherwise push the field far above 1; subtracting it puts
    /// the variance band symmetric around 0. We then scale by the sum
    /// of per-octave Noise amplitudes (each octave contributes a signed
    /// term in <c>[-fw·bounded, +fw·bounded]</c>) and remap to [0, 1].
    /// Output is the same for both <c>offset=0.7</c> (canonical terrain)
    /// and <c>offset=0.2</c> (alpine) up to a vertical reshuffling — the
    /// distinction the showcase wants to highlight.
    /// </para>
    /// </summary>
    private static float HeteroTerrainNormalized(Perlin noise, Vector3 p, int octaves, float lacunarity, float h, float offset)
    {
        float value = offset + noise.Noise(p);
        float dc = offset;                    // Noise=0 path (per-octave DC)
        float ampSumSq = 1f;                  // Σ (fw[i] · bounded_dc[i])² — variance accumulator
        p *= lacunarity;

        float frequencyWeight = 1f;
        float weightDecay = MathF.Pow(lacunarity, -h);
        for (int i = 1; i < octaves; i++)
        {
            frequencyWeight *= weightDecay;
            float bounded   = Math.Clamp(value, 0f, 1f);
            float boundedDc = Math.Clamp(dc,    0f, 1f);
            value += (noise.Noise(p) + offset) * frequencyWeight * bounded;
            dc    += offset                    * frequencyWeight * boundedDc;
            float amp = frequencyWeight * boundedDc;
            ampSumSq += amp * amp;
            p *= lacunarity;
        }

        // Center on DC, rescale by ~1.5σ. Each octave's Noise contribution
        // is independent (gradient noise decorrelates after lacunarity-scaled
        // domain shifts), so Var[value − dc] = σ²_Perlin · ampSumSq.
        // Mapping ±1.5σ → ±0.5 covers ~87% of the distribution inside [0,1]
        // and clamps only the extreme peaks (snow caps) and troughs (deep
        // water) — exactly what the terrain ramp wants to emphasize.
        float sigma = MathF.Sqrt(ampSumSq);
        return sigma > 0f ? Math.Clamp(0.5f + 0.5f * (value - dc) / (1.5f * sigma), 0f, 1f) : 0.5f;
    }

    /// <summary>
    /// Bounded + DC-centered HybridMultifractal. The weight clamp is already
    /// present in <see cref="Perlin.HybridMultifractal"/>; here we additionally
    /// track the DC path (Noise=0) and the per-octave Noise amplitude sum
    /// so the output is recentered and rescaled into [0, 1].
    /// </summary>
    private static float HybridMultifractalNormalized(Perlin noise, Vector3 p, int octaves, float lacunarity, float h, float offset)
    {
        float frequencyWeight = 1f;
        float weightDecay = MathF.Pow(lacunarity, -h);

        float result   = (noise.Noise(p) + offset) * frequencyWeight;
        float dc       = offset * frequencyWeight;        // Noise=0 path
        float ampSumSq = frequencyWeight * frequencyWeight;  // first-octave Noise variance
        float weight   = result;
        float weightDc = dc;
        p *= lacunarity;

        for (int i = 1; i < octaves; i++)
        {
            if (weight   > 1f) weight   = 1f;
            if (weightDc > 1f) weightDc = 1f;

            frequencyWeight *= weightDecay;
            float signal   = (noise.Noise(p) + offset) * frequencyWeight;
            float signalDc = offset                    * frequencyWeight;

            result += weight   * signal;
            dc     += weightDc * signalDc;
            float amp = weightDc * frequencyWeight;
            ampSumSq += amp * amp;

            weight   *= signal;
            weightDc *= signalDc;
            p *= lacunarity;
        }

        float sigma = MathF.Sqrt(ampSumSq);
        return sigma > 0f ? Math.Clamp(0.5f + 0.5f * (result - dc) / (1.5f * sigma), 0f, 1f) : 0.5f;
    }
}
