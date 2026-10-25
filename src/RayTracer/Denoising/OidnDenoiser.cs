using RayTracer.Rendering;

namespace RayTracer.Denoising;

/// <summary>
/// Learned denoiser: Intel Open Image Denoise's "RT" filter (a U-Net trained
/// on path-traced images) on the linear HDR beauty, guided by the albedo and
/// normal AOVs (full means of the A/B halves). The two guides are first
/// denoised with their own RT filters and the main filter is told they are
/// clean (<c>cleanAux</c>), as the OIDN documentation recommends for noisy
/// guides. Without it a texture finer than a pixel, whose albedo is still
/// noisy at 64 spp, came out with its noise kept as detail (relMSE 3.7
/// times higher on <c>showcases/texture-pro.yaml</c>). <c>high</c> runs the
/// high-quality networks, <c>fast</c> the balanced ones. The output stays
/// linear: the display transform runs afterwards, as for the other denoisers.
/// </summary>
public static class OidnDenoiser
{
    public static FrameBuffer Denoise(RenderBuffers buffers, DenoiserOptions opts)
    {
        if (!buffers.HasAovs)
            throw new InvalidOperationException(
                "Denoising requires AOV capture (RenderCaptureOptions.Full).");
        if (!Oidn.IsAvailable)
            throw new InvalidOperationException($"Open Image Denoise is not available: {Oidn.LoadError}");

        int w = buffers.Width, h = buffers.Height;
        float[] color = Interleave(buffers.Beauty, 0f, float.MaxValue);
        float[] albedo = Interleave(buffers.CombineHalves(buffers.AlbedoA!, buffers.AlbedoB!), 0f, 1f);
        float[] normal = Interleave(buffers.CombineHalves(buffers.NormalA!, buffers.NormalB!), -1f, 1f);
        float[] output = new float[color.Length];

        int quality = opts.Quality == DenoiseQuality.High ? Oidn.QualityHigh : Oidn.QualityBalanced;

        using var device = new Oidn.Device();
        Prefilter(device, "albedo", albedo, w, h, quality);
        Prefilter(device, "normal", normal, w, h, quality);

        using (var filter = new Oidn.Filter(device))
        {
            filter.SetImage("color", color, w, h);
            filter.SetImage("albedo", albedo, w, h);
            filter.SetImage("normal", normal, w, h);
            filter.SetImage("output", output, w, h);
            filter.Set("hdr", true);
            filter.Set("cleanAux", true);
            filter.Set("quality", quality);
            filter.Run();
        }
        return Deinterleave(output, w, h);
    }

    /// <summary>Denoises one guide in place with its own RT filter.</summary>
    private static void Prefilter(Oidn.Device device, string name, float[] image, int w, int h, int quality)
    {
        using var filter = new Oidn.Filter(device);
        filter.SetImage(name, image, w, h);
        filter.SetImage("output", image, w, h);
        filter.Set("quality", quality);
        filter.Run();
    }

    /// <summary>Planar RGB → interleaved, clamped to the range OIDN expects
    /// (non-finite values become the lower bound).</summary>
    internal static float[] Interleave(FrameBuffer fb, float lo, float hi)
    {
        int n = fb.Width * fb.Height;
        var d = fb.Data;
        var dst = new float[3 * n];
        for (int i = 0; i < n; i++)
        for (int c = 0; c < 3; c++)
        {
            float v = d[c * n + i];
            dst[3 * i + c] = float.IsFinite(v) ? Math.Clamp(v, lo, hi) : lo;
        }
        return dst;
    }

    internal static FrameBuffer Deinterleave(float[] rgb, int w, int h)
    {
        int n = w * h;
        var fb = new FrameBuffer(w, h, 3);
        var d = fb.Data;
        for (int i = 0; i < n; i++)
        for (int c = 0; c < 3; c++)
            d[c * n + i] = MathF.Max(0f, rgb[3 * i + c]);
        return fb;
    }
}
