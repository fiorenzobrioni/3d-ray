namespace RayTracer.Scene;

/// <summary>The scene's <c>render:</c> block, read by the CLI (PIANO-REVIEW.md Phase 10).</summary>
public static class RenderBlock
{
    /// <summary>
    /// The scene's <c>render:</c> block as command-line flags (each key is the
    /// flag of the same name). When the command line has its own <c>-q</c>,
    /// the keys a preset sets are left out: the CLI preset wins over them.
    /// The resolution is applied by the caller (a preset scales it).
    /// </summary>
    public static List<string> ToArgs(RenderData? r, bool cliQuality)
    {
        var a = new List<string>();
        if (r == null) return a;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        void Add(string flag, object? v, bool presetKey = false)
        {
            if (v == null || (presetKey && cliQuality)) return;
            a.Add(flag);
            a.Add(v switch
            {
                bool b => b ? "on" : "off",
                float f => f.ToString("R", inv),
                IFormattable fm => fm.ToString(null, inv),
                _ => v.ToString()!,
            });
        }
        if (!cliQuality) Add("--quality", r.Quality);
        Add("--samples", r.Samples, true);
        Add("--depth", r.Depth, true);
        Add("--depth-diffuse", r.DepthDiffuse, true);
        Add("--depth-glossy", r.DepthGlossy, true);
        Add("--depth-transmission", r.DepthTransmission, true);
        Add("--depth-volume", r.DepthVolume, true);
        Add("--depth-transparent", r.DepthTransparent, true);
        Add("--shadow-samples", r.ShadowSamples, true);
        Add("--light-sampling", r.LightSampling, true);
        Add("--indirect-clamp-factor", r.IndirectClampFactor, true);
        Add("--sss-mode", r.SssMode, true);
        Add("--denoiser", r.Denoiser, true);
        Add("--denoise-quality", r.DenoiseQuality, true);
        Add("--adaptive", r.Adaptive, true);
        Add("--adaptive-threshold", r.AdaptiveThreshold);
        Add("--adaptive-min-spp", r.AdaptiveMinSpp);
        Add("--integrator", r.Integrator);
        Add("--vcm-radius", r.VcmRadius);
        Add("--sampler", r.Sampler);
        Add("--mis", r.Mis);
        Add("--guiding", r.Guiding);
        Add("--guiding-training", r.GuidingTraining);
        Add("--clamp", r.Clamp);
        Add("--transparent-shadows", r.TransparentShadows);
        Add("--texture-filtering", r.TextureFiltering);
        Add("--filter", r.Filter);
        Add("--view", r.View);
        Add("--look", r.Look);
        Add("--exposure", r.Exposure);
        Add("--white-balance", r.WhiteBalance);
        Add("--bit-depth", r.BitDepth);
        Add("--dither", r.Dither);
        Add("--seed", r.Seed);
        Add("--camera", r.Camera);
        return a;
    }
}
