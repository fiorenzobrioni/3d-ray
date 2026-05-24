using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Textures;

/// <summary>
/// Procedural marble — production-grade ridged-multifractal vein field with
/// recursive (IQ) domain warping, anisotropic geological folds, multi-scale
/// vein layers, low-frequency background tonal variation and optional
/// mineral impurities.
///
/// <para>
/// <b>Algorithm</b> (per shade):
/// <list type="number">
///   <item><description>Texture transform (offset / rotation / random per
///     instance) applied to the local point.</description></item>
///   <item><description>Anisotropic geological fold via
///     <see cref="DomainWarp.Anisotropic"/> — large-scale shear that simulates
///     tectonic deformation. Per-axis amplitude lets one direction (typically
///     <see cref="VeinAxis"/>) dominate.</description></item>
///   <item><description>Recursive IQ warp via <see cref="DomainWarp.Recursive"/>
///     — kills every visible tiling on the vein field. 2 iterations is the
///     canonical recipe; 3 produces aggressive flow.</description></item>
///   <item><description>Multi-scale ridged vein field via
///     <see cref="MultiScaleRidgedField.Sample"/> — 1..3 independent ridged
///     layers at decoupled scales, composited via soft-max so thin and thick
///     veins co-exist and locally dominate (Calacatta / Arabescato look).</description></item>
///   <item><description>Vein-thickness remap — smoothstep on the ridged value
///     centred on <see cref="VeinThickness"/> with half-width
///     <see cref="VeinSoftness"/>. Replaces the broken power-of-sin
///     <c>vein_sharpness</c> from the legacy implementation.</description></item>
///   <item><description>Background fBm — signed low-frequency noise that
///     shifts the color-ramp lookup, eliminating flat base color.</description></item>
///   <item><description>Mineral impurities — sparse Voronoi specks (inline)
///     <i>or</i> any user-supplied <see cref="ImpuritiesTexture"/> bias the
///     ramp lookup toward the impurity stop.</description></item>
///   <item><description>Final mapping: <see cref="ColorRamp"/> lookup, or
///     two-colour lerp between <c>VeinColor</c> and <c>BaseColor</c>.</description></item>
/// </list>
/// </para>
///
/// <para>
/// <b>YAML example — White Carrara (defaults):</b>
/// </para>
/// <code>
/// texture:
///   type: "marble"
///   scale: 4.0
///   colors: [[0.92, 0.92, 0.94], [0.18, 0.18, 0.22]]
/// </code>
///
/// <para>
/// <b>YAML example — Calacatta with gold veins and impurities:</b>
/// </para>
/// <code>
/// texture:
///   type: "marble"
///   scale: 3.5
///   vein_axis: [0, 1, 0]
///   warp_amplitude: 0.9
///   warp_iterations: 2
///   fold_amplitude: [0.9, 0.2, 0.5]
///   vein_layers: 3
///   vein_scale:  [0.7, 1.6, 3.4]
///   vein_weight: [1.0, 0.7, 0.4]
///   vein_thickness: 0.55
///   vein_softness: 0.10
///   color_variation: 0.10
///   impurities_density: 0.03
///   color_ramp:
///     - { position: 0.00, color: [0.96, 0.96, 0.97] }
///     - { position: 0.55, color: [0.85, 0.78, 0.55] }
///     - { position: 0.80, color: [0.55, 0.40, 0.18] }
///     - { position: 1.00, color: [0.12, 0.10, 0.10] }
/// </code>
/// </summary>
public class MarbleTexture : ITexture
{
    /// <summary>
    /// What the texture returns at <see cref="Value(float, float, Vector3, int)"/>.
    /// <list type="bullet">
    ///   <item><description><c>Color</c> (default) — RGB after ramp / lerp. The
    ///     normal authoring path.</description></item>
    ///   <item><description><c>Mask</c> — the scalar vein-region parameter
    ///     <c>t ∈ [0, 1]</c> packed as <c>(t, t, t)</c>. Drop this same texture
    ///     block under a Disney material's <c>roughness_texture</c> /
    ///     <c>subsurface_texture</c> / etc. to drive scalar BSDF parameters
    ///     from the vein mask — vein zones can be glossier than the matte base,
    ///     SSS can attenuate over dark calcite veins, sheen can ride on the
    ///     base only.</description></item>
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
    /// Per-axis space stretch applied BEFORE the fold / warp / ridged pipeline.
    /// Real geological slabs are seldom isotropic — compression along the bed
    /// plane stretches features perpendicular to it. <c>(1, 1, 1)</c> (default)
    /// is isotropic; <c>(0.4, 1.8, 1.0)</c> compresses X (features grow
    /// horizontally), stretches Y (features grow vertically): the classic
    /// "stratified" Carrara plate look. Independent from <see cref="FoldAmplitude"/>:
    /// stretch is a linear pre-multiply on the sample point, fold is a
    /// non-linear noise-driven shear — they compose multiplicatively.
    /// </summary>
    public Vector3 SpaceStretch { get; set; } = Vector3.One;

    /// <summary>
    /// Direction the geological fold preferentially aligns with. The fold's
    /// largest per-axis amplitude is rotated toward this axis at sample time,
    /// so users can pin the slab's dominant vein orientation without
    /// micro-managing <see cref="FoldAmplitude"/>.
    /// </summary>
    public Vector3 VeinAxis { get; set; } = Vector3.UnitY;

    // ── Global "drama" dial ────────────────────────────────────────────────

    /// <summary>
    /// Global multiplier on <see cref="WarpAmplitude"/> and
    /// <see cref="FoldAmplitude"/>. One knob to dial overall chaos — leave at
    /// 1 to use the per-knob amplitudes as authored.
    /// </summary>
    public float NoiseStrength { get; set; } = 1f;

    // ── Recursive IQ domain warp ───────────────────────────────────────────

    public float WarpAmplitude { get; set; } = 0.75f;
    public float WarpScale { get; set; } = 2.0f;
    public int WarpIterations { get; set; } = 2;

    // ── Anisotropic geological fold ────────────────────────────────────────

    public Vector3 FoldAmplitude { get; set; } = new(0.6f, 0.2f, 0.4f);
    public float FoldScale { get; set; } = 6.0f;

    // ── Multi-scale ridged vein field ──────────────────────────────────────

    /// <summary>
    /// Number of independent ridged layers (1..3). Per-layer arrays
    /// <see cref="VeinScales"/> and <see cref="VeinWeights"/> must have this
    /// length; the loader enforces it.
    /// </summary>
    public int VeinLayers { get; set; } = 2;

    /// <summary>
    /// Per-layer spatial scale multiplier. Decoupling the scales is what
    /// makes thin <i>and</i> thick veins co-exist instead of producing a
    /// single dominant frequency. Length must match <see cref="VeinLayers"/>.
    /// </summary>
    public float[] VeinScales { get; set; } = { 1.0f, 2.6f };

    /// <summary>
    /// Per-layer soft-max weight. Higher weight = the layer wins more often
    /// in the soft-max composite. Length must match <see cref="VeinLayers"/>.
    /// </summary>
    public float[] VeinWeights { get; set; } = { 1.0f, 0.55f };

    /// <summary>Octaves used inside each ridged layer.</summary>
    public int Octaves { get; set; } = 5;

    public float Lacunarity { get; set; } = 2.0f;
    public float Gain { get; set; } = 0.5f;

    /// <summary>
    /// Sharpness of the soft-max compositing the ridged layers. Higher → the
    /// dominant layer wins crisper; lower → layers blend gently. 8 is a sane
    /// default chosen empirically against real marble photographs.
    /// </summary>
    public float SoftMaxSharpness { get; set; } = 8f;

    // ── Vein thickness remap ───────────────────────────────────────────────

    /// <summary>
    /// Fraction of the slab area occupied by veins. <c>0</c> ≈ no veins,
    /// <c>1</c> ≈ vein dominates the whole surface. <c>0.15</c> is the
    /// production Carrara default: mostly white base with thin vein
    /// structures. Strictly monotone wrt visible vein area.
    ///
    /// <para>
    /// Internally this is a smoothstep threshold on the ridged field at
    /// <c>1 - thickness</c>, so only the upper tail of the ridge distribution
    /// (the actual peaks of the multifractal) crosses into the vein region.
    /// </para>
    /// </summary>
    public float VeinThickness { get; set; } = 0.15f;

    /// <summary>
    /// Smoothstep half-width on the thickness threshold. Very small
    /// (0.02-0.05) produces razor-sharp vein edges (Marquina); larger
    /// (0.15-0.25) produces soft watery transitions (Statuario).
    /// </summary>
    public float VeinSoftness { get; set; } = 0.08f;

    // ── Background tonal variation ─────────────────────────────────────────

    public float BackgroundScale { get; set; } = 12f;
    public int BackgroundOctaves { get; set; } = 3;

    /// <summary>
    /// How much the background fBm shifts the ramp lookup. Small values
    /// (0.05-0.12) produce gentle tonal richness on the matte base; large
    /// values pull the ramp lookup across multiple stops, which is usually
    /// unwanted artistically.
    /// </summary>
    public float ColorVariation { get; set; } = 0.08f;

    // ── Impurities — inline Voronoi (default) or external texture ──────────

    /// <summary>
    /// Probability (per Voronoi cell, approximately) that an inline impurity
    /// speck is rendered. 0 disables the inline path entirely; ~0.05 gives
    /// Verde Alpi inclusions. Ignored when
    /// <see cref="ImpuritiesTexture"/> is set.
    /// </summary>
    public float ImpuritiesDensity { get; set; } = 0f;

    public float ImpuritiesScale { get; set; } = 8f;

    /// <summary>
    /// How strongly an impurity shifts the ramp lookup toward the impurity
    /// stop. Independent from density so artists can tune count and contrast
    /// separately.
    /// </summary>
    public float ImpurityWeight { get; set; } = 0.12f;

    /// <summary>
    /// Optional external impurity texture — if set, its luminance is used
    /// instead of the inline Voronoi path. Lets users compose any pattern
    /// (image, custom Voronoi, noise) as the impurity mask without leaving
    /// YAML. When set, <see cref="ImpuritiesDensity"/> and
    /// <see cref="ImpuritiesScale"/> are ignored.
    /// </summary>
    public ITexture? ImpuritiesTexture { get; set; }

    // ── Secondary linear cracks (Worley F2 − F1 overlay) ───────────────────

    /// <summary>
    /// Intensity of the sharp linear-crack overlay (Worley F2 − F1 ridge).
    /// <c>0</c> (default) disables the path entirely — no Worley evaluation,
    /// no perf cost. <c>0.3</c> = restrained Calacatta cracks, <c>0.6</c> =
    /// breccia slabs criss-crossed by fractures, <c>1.0</c> = maximum
    /// intensity (mostly used with low <see cref="CracksWeight"/> for stylised
    /// shattered looks).
    /// </summary>
    public float CracksDensity { get; set; } = 0f;

    /// <summary>
    /// Spatial scale of the crack network (Worley cell size).
    /// Lower = wider plates between cracks.
    /// </summary>
    public float CracksScale { get; set; } = 2.0f;

    /// <summary>
    /// Width of the crack lines in normalised <c>F2 − F1</c> ridge units.
    /// Crack lines appear where the ridge is below this threshold (i.e. near
    /// Voronoi cell borders). <c>0.01-0.03</c> produces razor-thin geological
    /// fractures (Marquinia), <c>0.05-0.10</c> produces soft branching veins
    /// (Calacatta), <c>0.15+</c> wide diffuse cracks. Independent from
    /// <see cref="VeinSoftness"/>.
    /// </summary>
    public float CracksSoftness { get; set; } = 0.05f;

    /// <summary>
    /// Soft-max weight of the crack layer when composited with the multi-scale
    /// ridged field. <c>0.6</c> = cracks visible but second to the ridged
    /// pattern; <c>1.0</c> = cracks compete with the strongest ridged layer;
    /// <c>1.3</c> = cracks dominate.
    /// </summary>
    public float CracksWeight { get; set; } = 0.9f;

    // ── Color output ───────────────────────────────────────────────────────

    /// <summary>
    /// Optional multi-stop colour ramp. When set, the ridged-vein parameter
    /// (after background variation and impurity bias) is looked up on the
    /// ramp; <c>BaseColor</c> / <c>VeinColor</c> are ignored.
    /// </summary>
    public ColorRamp? ColorRamp { get; set; }

    private readonly Perlin _noise;
    private readonly float _scale;
    private readonly Vector3 _baseColor;
    private readonly Vector3 _veinColor;

    public MarbleTexture(float scale = 4f)
        : this(scale, new Vector3(0.92f, 0.92f, 0.94f), new Vector3(0.18f, 0.18f, 0.22f)) { }

    public MarbleTexture(float scale, Vector3 baseColor, Vector3 veinColor)
    {
        _noise = Perlin.GetOrCreate(0);
        _scale = scale;
        _baseColor = baseColor;
        _veinColor = veinColor;
    }

    public Vector3 Value(float u, float v, Vector3 p, int objectSeed)
    {
        // ── 1. Texture transform ───────────────────────────────────────────
        Vector3 qGeom = TextureTransform.ApplyRandomRotation(
            TextureTransform.ApplyManual(p, Offset, Rotation),
            objectSeed, RandomizeRotation);
        qGeom *= _scale;
        Perlin noise = objectSeed != 0 ? Perlin.GetOrCreate(objectSeed) : _noise;

        Vector3 qNoise = qGeom + TextureTransform.SeedOffset(objectSeed, RandomizeOffset);

        // Anisotropic linear pre-stretch — geological compression along the
        // bed plane. Composes BEFORE the fold so the fold operates in the
        // stretched space and the stretch becomes the dominant directional
        // signature of the slab.
        if (SpaceStretch != Vector3.One)
            qNoise *= SpaceStretch;

        // ── 2. Geological fold (anisotropic, large-scale shear) ────────────
        Vector3 foldAmp = OrientedFoldAmplitude() * NoiseStrength;
        Vector3 q2 = DomainWarp.Anisotropic(noise, qNoise, foldAmp, FoldScale);

        // ── 3. Recursive IQ domain warp ────────────────────────────────────
        Vector3 qW = DomainWarp.Recursive(
            noise, q2, WarpIterations, WarpAmplitude * NoiseStrength, WarpScale);

        // ── 4. Multi-scale ridged vein field ───────────────────────────────
        int n = Math.Clamp(VeinLayers, 1, 3);
        Span<float> scales  = stackalloc float[3];
        Span<float> weights = stackalloc float[3];
        for (int i = 0; i < n; i++)
        {
            scales[i]  = i < VeinScales.Length  ? VeinScales[i]  : 1f;
            weights[i] = i < VeinWeights.Length ? VeinWeights[i] : 1f;
        }
        float vein = MultiScaleRidgedField.Sample(
            noise, qW, scales[..n], weights[..n],
            Octaves, Lacunarity, Gain, SoftMaxSharpness);

        // ── 4b. Secondary linear cracks (Worley F2 − F1 overlay) ───────────
        // Sharp network-like fractures that the ridged multifractal alone
        // cannot reach — its statistics are too organic for the long, linear
        // breccia / Calacatta crack patterns. The Worley F2 − F1 ridge is
        // SMALL at cell borders (we're equidistant to two cells) and LARGE at
        // cell centers; so the crack mask is `1 - smoothstep(0, lineWidth, ridge)`
        // — 1 on the thin band where the ridge is near zero, 0 elsewhere.
        // <see cref="CracksSoftness"/> directly controls the crack line width;
        // <see cref="CracksDensity"/> scales overall crack intensity (0..1)
        // before the soft-max with the vein layer. Compositing via soft-max
        // keeps the boundary between the two networks C¹ continuous.
        if (CracksDensity > 0f && CracksWeight > 0f)
        {
            var worley = objectSeed != 0 ? WorleyNoise.GetOrCreate(objectSeed) : WorleyNoise.GetOrCreate(0);
            worley.Evaluate(qW * CracksScale + new Vector3(53.7f, 11.3f, 79.1f),
                            WorleyNoise.Metric.Euclidean, 1f,
                            out float f1, out float f2, out _);
            float ridge = f2 - f1;
            float lineWidth = MathF.Max(CracksSoftness, 5e-3f);
            float cracks = 1f - Smoothstep(0f, lineWidth, ridge);
            cracks *= CracksDensity * CracksWeight;

            // Soft-max with the existing vein scalar. log-sum-exp rebased on
            // max keeps the float exp() arguments well-conditioned.
            float k = SoftMaxSharpness;
            float hi = MathF.Max(vein, cracks);
            double s = Math.Exp(k * (vein - hi)) + Math.Exp(k * (cracks - hi));
            vein = Math.Clamp(hi + (float)(Math.Log(s) / k), 0f, 1f);
        }

        // ── 5. Vein-thickness remap (smoothstep) ───────────────────────────
        float half = MathF.Max(VeinSoftness * 0.5f, 1e-4f);
        float edge0 = 1f - VeinThickness - half;
        float edge1 = 1f - VeinThickness + half;
        float veinT = Smoothstep(edge0, edge1, vein);

        // ── 6. Background tonal variation ──────────────────────────────────
        float bg = 0f;
        if (ColorVariation > 0f)
        {
            float bgScale = BackgroundScale > 0f ? 1f / BackgroundScale : 1f;
            bg = noise.Fbm(
                qNoise * bgScale + new Vector3(101.0f, 53.7f, 217.1f),
                Math.Max(BackgroundOctaves, 1), Lacunarity, Gain, signed: true);
        }

        // ── 7. Impurities ──────────────────────────────────────────────────
        float impurity = 0f;
        if (ImpuritiesTexture is not null)
        {
            Vector3 c = ImpuritiesTexture.Value(u, v, qGeom, objectSeed);
            impurity = 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;
        }
        else if (ImpuritiesDensity > 0f)
        {
            impurity = InlineImpurity(noise, qNoise * ImpuritiesScale, objectSeed);
        }

        // ── 8. Final mapping ───────────────────────────────────────────────
        float t = Math.Clamp(veinT + ColorVariation * bg + ImpurityWeight * impurity, 0f, 1f);

        // Mask output: pack the scalar t as (t, t, t) so downstream
        // FloatTexture reduction (channel average) recovers exactly t. Used
        // to drive Disney roughness_texture / subsurface_texture / etc. from
        // the vein mask.
        if (Output == OutputMode.Mask)
            return new Vector3(t);

        if (ColorRamp is { } ramp)
            return ramp.Sample(t);

        return Vector3.Lerp(_baseColor, _veinColor, t);
    }

    /// <summary>
    /// Rotates the per-axis fold amplitudes so the largest component aligns
    /// with <see cref="VeinAxis"/>. Lets a user pick a slab orientation with
    /// a single <c>vein_axis</c> vector without re-permuting the amplitude
    /// list manually.
    /// </summary>
    private Vector3 OrientedFoldAmplitude()
    {
        Vector3 axis = VeinAxis.LengthSquared() > 1e-12f
            ? Vector3.Normalize(VeinAxis) : Vector3.UnitY;
        Vector3 amp = FoldAmplitude;
        float aX = MathF.Abs(amp.X);
        float aY = MathF.Abs(amp.Y);
        float aZ = MathF.Abs(amp.Z);
        float maxAmp = MathF.Max(aX, MathF.Max(aY, aZ));
        float midAmp = aX + aY + aZ - maxAmp - MathF.Min(aX, MathF.Min(aY, aZ));
        float minAmp = MathF.Min(aX, MathF.Min(aY, aZ));

        // Project the sorted amplitudes onto the unit basis built around the
        // requested axis: dominant component along axis, secondary along the
        // largest perpendicular, weakest along the third axis.
        Vector3 perp = MathF.Abs(axis.X) < 0.9f ? Vector3.UnitX : Vector3.UnitZ;
        Vector3 b1 = Vector3.Normalize(perp - Vector3.Dot(perp, axis) * axis);
        Vector3 b2 = Vector3.Cross(axis, b1);
        return Vector3.Abs(axis) * maxAmp + Vector3.Abs(b1) * midAmp + Vector3.Abs(b2) * minAmp;
    }

    private float InlineImpurity(Perlin noise, Vector3 p, int objectSeed)
    {
        // Sparse Voronoi specks: only the brightest cells (whose hash falls
        // below ImpuritiesDensity) emit an impurity, and the speck shape is
        // shaped by the F1 distance so the centre is hot and the edge fades.
        var worley = objectSeed != 0 ? WorleyNoise.GetOrCreate(objectSeed) : WorleyNoise.GetOrCreate(1);
        worley.Evaluate(p, WorleyNoise.Metric.Euclidean, 1f, out float f1, out _, out int cellId);

        // Per-cell deterministic scalar — only cells under the density gate
        // produce a speck. Avoids the visible regular Voronoi grid that a
        // pure F1 threshold would create.
        float cellGate = WorleyNoise.CellScalar(cellId);
        if (cellGate > ImpuritiesDensity) return 0f;

        // Speck profile: bright at the feature centre, fading by ~0.35 cell
        // units. Small radius keeps inclusions tight; soft falloff avoids
        // visible ring banding under high-roughness shading.
        float radius = 0.35f;
        float t = Math.Clamp(1f - f1 / radius, 0f, 1f);
        return t * t * (3f - 2f * t); // smoothstep profile
    }

    private static float Smoothstep(float edge0, float edge1, float x)
    {
        if (edge1 <= edge0) return x >= edge1 ? 1f : 0f;
        float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
