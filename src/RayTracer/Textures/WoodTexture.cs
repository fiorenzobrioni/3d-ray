using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Textures;

/// <summary>
/// Procedural wood — production-grade annual-ring model with asymmetric
/// earlywood/latewood profile, per-ring random width + colour variation,
/// recursive IQ domain warp, anisotropic geological fold, multi-band noise
/// perturbation (grain + figure + axial), open-pore vessels, sapwood/heartwood
/// radial gradient and 3-D cone knot projection.
///
/// <para>
/// <b>Why a rewrite?</b> The legacy <c>sin(ring·scale) ^ sharpness</c> carrier
/// produced a perfectly symmetric ring profile (dark at both edges, bright in
/// the middle). Real annual rings are <i>asymmetric</i>: a long bright
/// earlywood plateau followed by a thin sharp dark latewood band that becomes
/// the visible dark line at the boundary with the next year's ring. Combined
/// with deterministic per-ring random width and colour shifts (no two rings
/// look the same in nature), this asymmetric profile is the single biggest
/// "looks fake → looks real" upgrade and matches the wood node in Cycles,
/// Substance Designer Wood, RenderMan's <c>PxrWoodKnot</c> and Arnold's
/// <c>wood</c>/<c>knots</c> map family.
/// </para>
///
/// <para>
/// <b>Algorithm</b> (per shade):
/// <list type="number">
///   <item><description>Texture transform (offset / rotation / random per
///     instance) and anisotropic <see cref="SpaceStretch"/> pre-multiply.</description></item>
///   <item><description>Decompose into the axial / radial coordinates around
///     <see cref="RingAxis"/>.</description></item>
///   <item><description>Anisotropic geological fold via
///     <see cref="DomainWarp.Anisotropic"/> — large-scale shear that simulates
///     trunk bending. Per-axis amplitude lets the warp run mostly across the
///     grain.</description></item>
///   <item><description>Recursive IQ warp via <see cref="DomainWarp.Recursive"/>
///     — kills every visible ring periodicity. 2 iterations is the canonical
///     recipe; 3 produces aggressive flow.</description></item>
///   <item><description>Radial anisotropy — stretches the noise sample point
///     along the local radial direction (quartersawn-oak look).</description></item>
///   <item><description>Three-band noise perturbation on the radial distance:
///     high-frequency <see cref="GrainScale"/> grain fBm, mid/low-frequency
///     <see cref="FigureScale"/> figure (with optional <see cref="FigureAspect"/>
///     elongation perpendicular to grain), and optional <see cref="AxialGrain"/>
///     long-wave along the axis.</description></item>
///   <item><description>3-D cone knot projection: knots are sparse Worley cells
///     in the radial-axial plane; inside a knot cone the ring centre is pulled
///     toward the knot feature and a dark heart is added on top.</description></item>
///   <item><description>Per-ring randomisation: <c>floor(ringIndex)</c> hashed
///     to a deterministic scalar that perturbs both the ring width
///     (<see cref="RingWidthVariation"/>) and the ring colour offset
///     (<see cref="RingColorVariation"/>).</description></item>
///   <item><description>Asymmetric earlywood/latewood profile: smooth rise out
///     of the prior latewood, long bright plateau, smooth descent into the
///     dark latewood band of width <see cref="LatewoodWidth"/>.
///     <see cref="RingSharpness"/> controls the latewood edge crispness.</description></item>
///   <item><description>Open-pore vessels via axially-elongated Worley — sharp
///     dark micro-specks oriented along the grain (oak/ash/walnut).</description></item>
///   <item><description>Sapwood/heartwood radial colour gradient
///     (<see cref="HeartwoodRadius"/> / <see cref="HeartwoodBlend"/>).</description></item>
///   <item><description>Final mapping: <see cref="ColorRamp"/> lookup, or
///     two-colour lerp; or <see cref="OutputMode.Mask"/> output for driving
///     Disney <c>roughness_texture</c> / <c>sheen_texture</c> &amp;c from the
///     latewood pattern.</description></item>
/// </list>
/// </para>
///
/// <para>
/// <b>YAML example — quartersawn oak with open pores:</b>
/// </para>
/// <code>
/// texture:
///   type: "wood"
///   scale: 4.0
///   ring_axis: [0, 1, 0]
///   latewood_width: 0.22
///   ring_sharpness: 4.0
///   ring_color_variation: 0.18
///   ring_width_variation: 0.12
///   warp_amplitude: 0.6
///   warp_iterations: 2
///   fold_amplitude: [0.6, 0.15, 0.4]
///   grain_strength: 1.6
///   octaves: 5
///   figure_strength: 0.4
///   figure_scale: 0.18
///   figure_aspect: 3.0
///   radial_anisotropy: 2.5
///   pore_density: 0.45
///   pore_scale: 18.0
///   pore_aspect: 5.0
///   pore_strength: 0.55
///   heartwood_radius: 0.8
///   heartwood_blend: 0.25
///   color_ramp:
///     - { position: 0.00, color: [0.18, 0.10, 0.04] }
///     - { position: 0.55, color: [0.78, 0.58, 0.32] }
///     - { position: 1.00, color: [0.95, 0.82, 0.55] }
/// </code>
/// </summary>
public class WoodTexture : ITexture
{
    /// <summary>
    /// What the texture returns at <see cref="Value(float, float, Vector3, int)"/>.
    /// <list type="bullet">
    ///   <item><description><c>Color</c> (default) — RGB after ramp / lerp. The
    ///     normal authoring path.</description></item>
    ///   <item><description><c>Mask</c> — the scalar ring parameter
    ///     <c>t ∈ [0, 1]</c> packed as <c>(t, t, t)</c>. Drop this same texture
    ///     block under a Disney material's <c>roughness_texture</c> /
    ///     <c>sheen_texture</c> / etc. to drive scalar BSDF parameters from the
    ///     latewood mask — earlywood can stay matte while latewood gets glossier
    ///     pores, or sheen can ride on the earlywood only.</description></item>
    /// </list>
    /// </summary>
    public enum OutputMode { Color, Mask }

    public OutputMode Output { get; set; } = OutputMode.Color;

    // ── Geometry seed (kept for radial / directional layout) ───────────────
    public Vector3 Offset { get; set; } = Vector3.Zero;
    public Vector3 Rotation { get; set; } = Vector3.Zero;
    public bool RandomizeOffset { get; set; }
    public bool RandomizeRotation { get; set; }

    /// <summary>
    /// Per-axis space stretch applied BEFORE the fold / warp pipeline. A real
    /// wooden plank is seldom isotropic — fibres run along the axial direction
    /// and compress perpendicular to it. <c>(1, 1, 1)</c> (default) is
    /// isotropic; <c>(0.6, 1.6, 1.0)</c> compresses X and stretches Y (the
    /// classic plank-along-Y look).
    /// </summary>
    public Vector3 SpaceStretch { get; set; } = Vector3.One;

    /// <summary>
    /// Trunk axis. Rings are concentric circles in the plane perpendicular to
    /// this vector, so a tree trunk uses <c>(0, 1, 0)</c> (rings appear on
    /// cross-cut), a horizontal log uses <c>(1, 0, 0)</c>, etc.
    /// </summary>
    public Vector3 RingAxis { get; set; } = Vector3.UnitY;

    // ── Recursive IQ domain warp ───────────────────────────────────────────

    /// <summary>
    /// Recursive (IQ) warp amplitude on the sample point. 0 disables the warp
    /// entirely; ~0.4-0.8 gives organic ring curvature; &gt; 1 produces
    /// stylised flow patterns. Multiplied by <see cref="NoiseStrength"/> at
    /// sample time so the dramatic dial scales every warp uniformly.
    /// </summary>
    public float WarpAmplitude { get; set; } = 0.4f;

    /// <summary>
    /// World-space period of the warp field. Larger values produce broader,
    /// slower deformations; smaller values produce tighter swirls.
    /// </summary>
    public float WarpScale { get; set; } = 2.5f;

    /// <summary>
    /// Number of recursive warp iterations (0..3). 2 is the canonical IQ
    /// recipe; 3 produces aggressive flow.
    /// </summary>
    public int WarpIterations { get; set; } = 2;

    // ── Anisotropic geological fold ────────────────────────────────────────

    /// <summary>
    /// Per-axis amplitude of the anisotropic single-iteration warp that runs
    /// BEFORE the recursive IQ warp. Used to simulate trunk bending /
    /// large-scale shear. Set to <c>Vector3.Zero</c> to disable.
    /// </summary>
    public Vector3 FoldAmplitude { get; set; } = new(0.3f, 0.1f, 0.3f);

    /// <summary>Spatial scale of the geological fold.</summary>
    public float FoldScale { get; set; } = 4.0f;

    // ── Global "drama" dial ────────────────────────────────────────────────

    /// <summary>
    /// Global multiplier on <see cref="WarpAmplitude"/>,
    /// <see cref="FoldAmplitude"/> and the noise bands. One knob to dial
    /// overall chaos — leave at 1 to use the per-knob amplitudes as authored.
    /// </summary>
    public float NoiseStrength { get; set; } = 1f;

    // ── Multi-band noise perturbation on the ring distance ────────────────

    /// <summary>
    /// Amplitude of the high-frequency grain band. Each fBm sample is signed,
    /// so the value adds to (or subtracts from) the radial distance — bending
    /// the rings inward and outward to produce visible fibre detail inside
    /// each ring. Typical: 1.0-2.5.
    /// </summary>
    public float GrainStrength { get; set; } = 1.5f;

    /// <summary>
    /// Frequency multiplier on the high-frequency grain noise sample point.
    /// Higher values shrink the grain features (more fibre detail per unit
    /// length); lower values stretch them. Default 1.
    /// </summary>
    public float GrainScale { get; set; } = 1f;

    /// <summary>
    /// Amplitude of the figure band. 0 disables the figure entirely. Values
    /// around 0.5-1.5 produce visible figure; higher values dominate over the
    /// grain (curly maple, flame mahogany, bird's-eye, ribbon mahogany).
    /// </summary>
    public float FigureStrength { get; set; } = 0f;

    /// <summary>
    /// Frequency multiplier on the figure noise sample point. Only active when
    /// <see cref="FigureStrength"/> &gt; 0. Default 0.25 puts the figure
    /// feature size at ~4× the grain size.
    /// </summary>
    public float FigureScale { get; set; } = 0.25f;

    /// <summary>
    /// Axial elongation of the figure noise. 1 (default) = isotropic figure;
    /// higher values compress the figure noise along the trunk axis so its
    /// stripes run perpendicular to the grain — the classic flame mahogany /
    /// curly maple direction. Typical: 2-5.
    /// </summary>
    public float FigureAspect { get; set; } = 1f;

    /// <summary>
    /// Optional long-wave noise along the trunk axis (gentle waves on planks).
    /// 0 disables the path. Composed additively with the grain/figure bands.
    /// </summary>
    public float AxialGrain { get; set; } = 0f;

    /// <summary>Octaves used by the grain fBm.</summary>
    public int Octaves { get; set; } = 4;

    public float Lacunarity { get; set; } = 2.0f;
    public float Gain { get; set; } = 0.5f;

    // ── Radial anisotropy (quartersawn vs plain-sawn) ──────────────────────

    /// <summary>
    /// Anisotropic stretching of the noise sample point along the local
    /// radial direction (perpendicular to <see cref="RingAxis"/>). 0 = isotropic
    /// (plain-sawn); &gt; 0 compresses the radial component of the sample
    /// point so noise varies slower along radial axis — the quartersawn-oak
    /// look. Typical: 2-5.
    /// </summary>
    public float RadialAnisotropy { get; set; } = 0f;

    // ── Asymmetric earlywood / latewood ring profile ──────────────────────

    /// <summary>
    /// Width of the dark latewood band at the end of each annual ring, as a
    /// fraction of one ring. <c>0.15-0.30</c> is the natural range —
    /// fast-growing softwoods like pine have wide latewood (~0.30);
    /// fine-grain hardwoods like maple have narrow latewood (~0.15).
    /// </summary>
    public float LatewoodWidth { get; set; } = 0.22f;

    /// <summary>
    /// Sharpness of the latewood transition. Controls how abruptly the bright
    /// earlywood gives way to the dark latewood band. <c>1.0</c> = soft S-curve;
    /// <c>3.0-6.0</c> = razor-sharp boundary (the classic dark ring line of
    /// oak, walnut, mahogany). Default 3.0 matches photograph statistics.
    /// </summary>
    public float RingSharpness { get; set; } = 3.0f;

    /// <summary>
    /// Width of the smooth rise out of the prior ring's latewood, as a fraction
    /// of one ring. Very small (~0.05) reproduces the sharp annual-ring
    /// boundary; larger values (~0.15) produce a smoother spring transition
    /// found in tropical hardwoods without distinct annual growth.
    /// </summary>
    public float EarlywoodTransition { get; set; } = 0.05f;

    // ── Per-ring random variation ──────────────────────────────────────────

    /// <summary>
    /// Per-ring colour offset amplitude. Each integer ring index gets a
    /// deterministic hash in <c>[-1, 1]</c> that shifts the colour-ramp lookup
    /// for the entire ring. 0 = every ring identical (the legacy "every ring
    /// looks the same" look); <c>0.10-0.25</c> = natural year-to-year
    /// variation (the single biggest realism upgrade).
    /// </summary>
    public float RingColorVariation { get; set; } = 0.15f;

    /// <summary>
    /// Per-ring radial-width offset amplitude. 0 = uniform ring spacing
    /// (regular tree cross-section); <c>0.10-0.25</c> = irregular growth years
    /// like wet/dry alternation. The width offset is deterministic per ring
    /// index, so adjacent objects with the same seed see the same ring widths.
    /// </summary>
    public float RingWidthVariation { get; set; } = 0.10f;

    // ── Open-pore vessels (oak / ash / walnut) ─────────────────────────────

    /// <summary>
    /// Probability of pore-vessel spawning, in <c>[0, 1]</c>. 0 disables the
    /// path entirely (no Worley evaluation, no cost). ~0.30-0.55 produces the
    /// open-pore look of oak, ash, walnut, mahogany — sharp dark micro-specks
    /// elongated along the grain.
    /// </summary>
    public float PoreDensity { get; set; } = 0f;

    /// <summary>
    /// Spatial scale of the pore cells. Higher = more, smaller pores per unit
    /// area. Default 16 puts typical visible pores at ~0.06 wu apart.
    /// </summary>
    public float PoreScale { get; set; } = 16f;

    /// <summary>
    /// Strength of the pore darkening. Subtracted from the final <c>t</c>
    /// before colour mapping. 0 = no contribution; 1 = pores reach pure
    /// darkness regardless of the underlying ring brightness.
    /// </summary>
    public float PoreStrength { get; set; } = 0.4f;

    /// <summary>
    /// Axial elongation of the pore cells. 1 = isotropic spots; higher values
    /// (~3-8) elongate the cells along the grain axis, reproducing the
    /// long-vessel anatomy of real open-pore species. The pore axis follows
    /// <see cref="RingAxis"/>.
    /// </summary>
    public float PoreAspect { get; set; } = 4f;

    // ── Sapwood / heartwood radial gradient ────────────────────────────────

    /// <summary>
    /// Radial distance (in pre-stretch world units, after <c>scale</c>
    /// multiplication) at which the sapwood/heartwood transition is centred.
    /// 0 disables the gradient entirely. Values around 0.5-1.0 produce
    /// noticeable colour shift across a plank that crosses the heartwood line.
    /// </summary>
    public float HeartwoodRadius { get; set; } = 0f;

    /// <summary>
    /// Amplitude of the sapwood/heartwood shift on the final <c>t</c>.
    /// Positive values darken toward the centre (true heartwood); negative
    /// values would brighten toward the centre. <c>0.15-0.30</c> is the
    /// natural amplitude for walnut, cherry, ipe — species with strong
    /// heartwood-sapwood demarcation.
    /// </summary>
    public float HeartwoodBlend { get; set; } = 0.25f;

    // ── Knot spawning ──────────────────────────────────────────────────────

    /// <summary>
    /// Probability of branch-knot spawning, in <c>[0, 1]</c>. 0 disables the
    /// path (no Voronoi evaluation, no cost). Knots use a 3-D cone projection:
    /// each sparse Worley cell hosts a knot whose visible cone widens with
    /// axial distance from the cell centre. Inside the cone the ring centre is
    /// pulled toward the knot feature and a dark heart is added on top — same
    /// kind of behaviour as Arnold's <c>knots</c> map and RenderMan's
    /// <c>PxrWoodKnot</c>.
    /// </summary>
    public float KnotDensity { get; set; } = 0f;

    /// <summary>
    /// Knot cell size. Larger = wider knot spacing. The default works with
    /// <c>scale ≥ 5</c> so each knot can host multiple concentric rings.
    /// </summary>
    public float KnotScale { get; set; } = 0.6f;

    // ── Color output ───────────────────────────────────────────────────────

    /// <summary>
    /// Optional multi-stop colour ramp. When set, the ring parameter
    /// <c>t ∈ [0, 1]</c> is looked up on the ramp; <c>colors[]</c> is ignored.
    /// </summary>
    public ColorRamp? ColorRamp { get; set; }

    private readonly Perlin _noise;
    private readonly float _scale;
    private readonly ITexture _lightWoodColor;
    private readonly ITexture _darkWoodColor;

    public WoodTexture(float scale = 4f, float grainStrength = 1.5f)
        : this(scale, grainStrength,
               new Vector3(0.85f, 0.65f, 0.40f),
               new Vector3(0.45f, 0.28f, 0.14f)) { }

    public WoodTexture(float scale, float grainStrength, Vector3 lightColor, Vector3 darkColor)
    {
        _noise = Perlin.GetOrCreate(0);
        _scale = scale;
        GrainStrength = grainStrength;
        _lightWoodColor = new SolidColor(lightColor);
        _darkWoodColor = new SolidColor(darkColor);
    }

    /// <summary>
    /// Backward-compatible accessor for the legacy "noise strength" knob —
    /// preserved so library YAML that still says <c>noise_strength</c> keeps
    /// working alongside the preferred <c>grain_strength</c>.
    /// </summary>
    public float LegacyNoiseStrength
    {
        get => GrainStrength;
        set => GrainStrength = value;
    }

    public Vector3 Value(float u, float v, Vector3 p, int objectSeed)
    {
        // ── 1. Texture transform + space stretch ──────────────────────────
        // Geometric q drives the radial distance from the ring axis — must stay
        // rooted at the object origin so concentric rings stay concentric. No
        // per-instance seed offset on this path.
        Vector3 qGeom = TextureTransform.ApplyRandomRotation(
            TextureTransform.ApplyManual(p, Offset, Rotation),
            objectSeed, RandomizeRotation);
        Perlin noise = objectSeed != 0 ? Perlin.GetOrCreate(objectSeed) : _noise;

        // Per-instance decorrelation. NEVER added to the geometric pipeline —
        // the rings must stay concentric around the trunk axis regardless of
        // the object seed. The shift is composed only with the grain/figure
        // sample point so adjacent instances see different fibre detail while
        // the ring topology stays anchored on the trunk axis. Per-instance
        // warp variation comes for free from the seeded Perlin instance.
        Vector3 noiseShift = TextureTransform.SeedOffset(objectSeed, RandomizeOffset);

        // Anisotropic linear pre-stretch — non-isotropic plank cuts.
        Vector3 qStretched = qGeom;
        if (SpaceStretch != Vector3.One) qStretched = qGeom * SpaceStretch;

        // ── 2. Axis decomposition ─────────────────────────────────────────
        Vector3 axis = RingAxis.LengthSquared() > 1e-12f
            ? Vector3.Normalize(RingAxis) : Vector3.UnitY;
        Vector3 perpAxis1 = MathF.Abs(axis.X) < 0.9f ? Vector3.UnitX : Vector3.UnitZ;
        perpAxis1 = Vector3.Normalize(perpAxis1 - Vector3.Dot(perpAxis1, axis) * axis);
        Vector3 perpAxis2 = Vector3.Cross(axis, perpAxis1);

        // ── 3. Anisotropic geological fold ────────────────────────────────
        // Large-scale shear that simulates the tree's trunk bending. Mostly
        // perpendicular to the trunk so the rings curve rather than slide.
        Vector3 foldAmp = FoldAmplitude * NoiseStrength;
        Vector3 qFolded = DomainWarp.Anisotropic(noise, qStretched, foldAmp, FoldScale);

        // ── 4. Recursive IQ domain warp ───────────────────────────────────
        // The headline anti-tiling step. The legacy single-iter `Distortion`
        // is gone — recursive warp produces the organic non-self-similar flow
        // that Arnold's wood, Cycles' Wave Texture and RenderMan PxrWood all
        // ship with by default. With WarpIterations=0 / WarpAmplitude=0 the
        // helper is a strict no-op so rings stay perfectly concentric.
        Vector3 qWarped = DomainWarp.Recursive(
            noise, qFolded, WarpIterations, WarpAmplitude * NoiseStrength, WarpScale);

        // ── 5. Re-decompose into radial / axial after warp ────────────────
        float along = Vector3.Dot(qWarped, axis);
        Vector3 radial = qWarped - along * axis;
        float dist = radial.Length();

        // ── 6. Radial anisotropy ──────────────────────────────────────────
        // Noise sample point is the warped point PLUS the per-instance offset
        // so adjacent objects see decorrelated grain/figure without disturbing
        // the ring geometry computed above.
        Vector3 qNoise = qWarped + noiseShift;
        if (RadialAnisotropy > 0f && dist > 1e-6f)
        {
            Vector3 rHat = radial / dist;
            float anisoFactor = 1f / (1f + RadialAnisotropy);
            float rComp = Vector3.Dot(qNoise, rHat);
            qNoise = qNoise - rComp * rHat + (rComp * anisoFactor) * rHat;
        }

        // ── 7. Multi-band noise perturbation on the radial distance ───────
        // The bands compose by adding to `dist` — bending the rings inward and
        // outward to produce visible fibre detail without breaking the ring
        // topology. Each band uses signed noise so it can shift in both
        // directions; otherwise the rings would only ever grow outward.
        float noiseTotal = 0f;

        if (GrainStrength > 0f)
        {
            Vector3 qGrain = qNoise * GrainScale;
            float grain = Octaves <= 1
                ? noise.Noise(qGrain)
                : noise.Fbm(qGrain, Octaves, Lacunarity, Gain, signed: true);
            noiseTotal += NoiseStrength * GrainStrength * grain;
        }

        if (FigureStrength > 0f)
        {
            // Figure noise — sampled in axis-aligned coords so we can compress
            // the axial direction (FigureAspect) and stretch the figure
            // perpendicular to the grain, the natural orientation of curly
            // maple / flame mahogany.
            float coordA = Vector3.Dot(qNoise, perpAxis1);
            float coordB = Vector3.Dot(qNoise, perpAxis2);
            float coordC = Vector3.Dot(qNoise, axis) / MathF.Max(FigureAspect, 1e-3f);
            Vector3 qFigure = new Vector3(coordA, coordB, coordC) * FigureScale
                            + new Vector3(127.13f, 89.41f, 53.27f);
            float figure = noise.Noise(qFigure);
            noiseTotal += NoiseStrength * FigureStrength * figure;
        }

        if (AxialGrain > 0f)
        {
            noiseTotal += NoiseStrength * AxialGrain * noise.Noise(new Vector3(along * 0.5f, 17.3f, 0f));
        }

        // ── 8. Knot 3-D cone projection ───────────────────────────────────
        // Each Worley cell in the perpendicular plane hosts at most one knot.
        // The knot is modeled as a cone with apex on the trunk axis: at the
        // cone centre's `along` coordinate the knot has its smallest radius;
        // further along the axis the knot widens, then closes again — like a
        // branch stub embedded in the trunk and intersected by the cut plane.
        float knotDarken = 0f;
        float ringDist = dist + noiseTotal;

        if (KnotDensity > 0f)
        {
            WorleyNoise worley = objectSeed != 0
                ? WorleyNoise.GetOrCreate(objectSeed)
                : WorleyNoise.GetOrCreate(0);
            float knotFreq = MathF.Max(KnotScale, 1e-3f);

            // 3-D Worley in (perpA, perpB, along/aspect) space: cells are
            // elongated along the trunk axis by axial_aspect = 2 so a knot
            // covers ~2 cell widths of axial extent before being swallowed.
            float coordA = Vector3.Dot(qWarped, perpAxis1) * knotFreq;
            float coordB = Vector3.Dot(qWarped, perpAxis2) * knotFreq;
            float coordZ = along * knotFreq * 0.5f;       // axial cells 2× elongated
            Vector3 qKnot = new Vector3(coordA + 41.17f, coordB + 17.83f, coordZ + 91.41f);
            worley.Evaluate(qKnot, WorleyNoise.Metric.Euclidean, 1f,
                            out float kF1, out _, out int kCellId);

            // Per-cell density gate — only some cells spawn a knot, otherwise
            // every cell would be a knot and the trunk would be all branches.
            float cellGate = WorleyNoise.CellScalar(kCellId);
            if (cellGate < KnotDensity)
            {
                // Visibility of the knot scales with how close to the cell
                // centre we are. radius 0.45 keeps cells mostly partitioned.
                float radius = 0.40f + 0.05f * KnotDensity;
                if (kF1 < radius)
                {
                    float t01 = kF1 / radius;
                    float mask = 1f - t01;
                    mask *= mask;   // sharpen falloff

                    // Pull the ring centre toward the knot — the local "ring
                    // distance" becomes the knot's F1 distance, so the
                    // sin-style band pattern wraps around the knot apex.
                    float knotRingDist = kF1 / knotFreq;
                    ringDist += (knotRingDist - ringDist) * mask;

                    // Dark heart at the knot centre.
                    knotDarken = mask * mask;
                }
            }
        }

        // ── 9. Per-ring random variation ──────────────────────────────────
        // Each annual ring (integer floor of the ring index) gets a
        // deterministic hash in [-1, 1] that:
        //   • offsets the radial coordinate within the ring (width variation)
        //   • shifts the final colour-ramp lookup (colour variation)
        // The hash combines floor(ringIndex) with objectSeed so different
        // objects see different ring sequences.
        float ringRaw = ringDist * _scale;
        int ringIdx = (int)MathF.Floor(ringRaw);
        float ringRand = HashRing(ringIdx, objectSeed);   // ∈ [-1, 1]

        if (RingWidthVariation > 0f)
        {
            // Smoothly perturb the radial coordinate by up to half a ring
            // width. Adjacent rings then differ in apparent width; the
            // boundary between them stays C¹ continuous because the hash is
            // applied per integer ring (constant within one ring).
            ringRaw += RingWidthVariation * 0.5f * ringRand;
            ringIdx = (int)MathF.Floor(ringRaw);   // re-derive after shift
            ringRand = HashRing(ringIdx, objectSeed);
        }

        float frac = ringRaw - MathF.Floor(ringRaw);

        // ── 10. Asymmetric earlywood / latewood profile ───────────────────
        // Real annual rings: long bright earlywood plateau ending in a thin
        // sharp dark latewood band. The legacy symmetric pow(triangle, sharpness)
        // was bright in the middle and dark on both ends — incorrect.
        //
        // Profile = rise * fall, where:
        //   rise = smoothstep(0, transition, frac)        — quick emergence
        //                                                   from prior latewood
        //   fall = 1 - smoothstep(1 - latewood, 1, frac)  — smooth descent
        //                                                   into latewood band
        // The latewood band sits at the END of each ring, so adjacent rings
        // meet at a thin dark line — the visible annual boundary.
        float lw = Math.Clamp(LatewoodWidth, 0.02f, 0.95f);
        float ew = Math.Clamp(EarlywoodTransition, 0.005f, 0.5f);
        float rise = Smoothstep(0f, ew, frac);
        float fall = 1f - Smoothstep(1f - lw, 1f, frac);
        float t = rise * fall;

        // Sharpness: pow(t, 1/sharpness) at sharpness > 1 widens the bright
        // plateau and thins the latewood band — the classic "razor latewood"
        // of oak/walnut. At sharpness = 1 the profile is the natural product.
        if (RingSharpness > 1f)
            t = MathF.Pow(t, 1f / RingSharpness);
        else if (RingSharpness < 1f && RingSharpness > 0f)
            t = MathF.Pow(t, 1f / MathF.Max(RingSharpness, 0.05f));

        // ── 11. Per-ring colour variation ─────────────────────────────────
        if (RingColorVariation > 0f)
            t = Math.Clamp(t + RingColorVariation * ringRand * 0.5f, 0f, 1f);

        // ── 12. Knot dark heart (applied after ring profile so it stays as
        //        a discrete dark spot regardless of which ring band the sample
        //        falls into).
        if (knotDarken > 0f)
            t *= (1f - knotDarken);

        // ── 13. Open-pore vessels — axially-anisotropic Worley ────────────
        // Sparse dark micro-specks elongated along the grain. The "vessels"
        // are short cylindrical channels in real wood; truncated by the
        // surface cut, they appear as dark dots or short streaks. The cells
        // are elongated along `axis` by PoreAspect so the visible specks are
        // longer along the grain than across it.
        if (PoreDensity > 0f && PoreStrength > 0f)
        {
            WorleyNoise worley = objectSeed != 0
                ? WorleyNoise.GetOrCreate(objectSeed + 7919)
                : WorleyNoise.GetOrCreate(7919);

            // Sample in axis-aligned coords so the pore shape can compress
            // along the trunk axis (aspect ratio).
            float coordA = Vector3.Dot(qWarped, perpAxis1) * PoreScale;
            float coordB = Vector3.Dot(qWarped, perpAxis2) * PoreScale;
            float coordZ = along * PoreScale / MathF.Max(PoreAspect, 1e-3f);
            Vector3 qPore = new Vector3(coordA + 11.7f, coordB + 7.3f, coordZ + 53.9f);
            worley.Evaluate(qPore, WorleyNoise.Metric.Euclidean, 1f,
                            out float pF1, out _, out int pCellId);

            // Per-cell density gate.
            float poreGate = WorleyNoise.CellScalar(pCellId + 13);
            if (poreGate < PoreDensity)
            {
                // Vessel profile: hot at the F1 centre, fading by ~0.30 cell
                // units. Same smoothstep falloff used by marble inclusions.
                float poreRadius = 0.30f;
                float pt = Math.Clamp(1f - pF1 / poreRadius, 0f, 1f);
                float poreMask = pt * pt * (3f - 2f * pt);
                t = Math.Clamp(t - PoreStrength * poreMask, 0f, 1f);
            }
        }

        // ── 14. Sapwood / heartwood radial gradient ───────────────────────
        // Heartwood (interior, older xylem) is typically darker than sapwood
        // (outer, younger). We bias `t` based on radial distance vs the
        // heartwood radius — closer to centre → darker (lower t).
        if (HeartwoodRadius > 0f && HeartwoodBlend != 0f)
        {
            // Use the post-warp dist (after grain has been added to it) so the
            // transition follows the curved rings rather than the bare radius.
            float r = ringDist;
            // Smoothstep centred on HeartwoodRadius, full width = HeartwoodRadius
            float hwT = Smoothstep(HeartwoodRadius * 0.5f, HeartwoodRadius * 1.5f, r);
            // hwT: 0 deep heartwood, 1 fully sapwood
            float hwShift = (hwT - 0.5f) * 2f * HeartwoodBlend;  // [-blend, +blend]
            t = Math.Clamp(t + hwShift, 0f, 1f);
        }

        // ── 15. Output ────────────────────────────────────────────────────
        if (Output == OutputMode.Mask)
            return new Vector3(t);

        if (ColorRamp is { } ramp)
            return ramp.Sample(t);

        Vector3 cLight = _lightWoodColor.Value(u, v, qGeom, objectSeed);
        Vector3 cDark = _darkWoodColor.Value(u, v, qGeom, objectSeed);
        return Vector3.Lerp(cDark, cLight, t);
    }

    /// <summary>
    /// Deterministic per-ring hash in [-1, 1]. Combines the integer ring index
    /// with the object seed so different objects (same seed) see different
    /// ring width / colour sequences while staying reproducible across renders.
    /// </summary>
    private static float HashRing(int ringIndex, int objectSeed)
    {
        uint h = unchecked((uint)(ringIndex * 374761393) ^ (uint)(objectSeed * 668265263));
        h ^= h >> 13;
        h = unchecked(h * 1274126177u);
        h ^= h >> 16;
        // Map low 24 bits to [-1, 1)
        float u = ((h >> 8) & 0xFFFFFFu) * (1f / 8388608f) - 1f;
        return u;
    }

    private static float Smoothstep(float edge0, float edge1, float x)
    {
        if (edge1 <= edge0) return x >= edge1 ? 1f : 0f;
        float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
