namespace RayTracer.Rendering;

/// <summary>
/// Turns display-encoded values in [0, 1] into integer code values for the
/// saved image (PIANO-REVIEW.md, Phase 3). 8-bit output is dithered by
/// default: rounding a smooth gradient to 256 levels draws visible bands in
/// skies and soft shadows, and triangular (TPDF) noise of ±1 code before
/// rounding turns them into invisible grain while keeping every pixel's mean
/// equal to its exact value. The noise is a hash of the pixel and channel, so
/// the output is deterministic.
/// </summary>
public static class DisplayQuantizer
{
    public static byte To8Bit(float value, int x, int y, int channel, bool dither)
    {
        float v = float.IsNaN(value) ? 0f : Math.Clamp(value, 0f, 1f) * 255f;
        if (dither)
        {
            uint h = Hash((uint)x * 0x8da6b343u ^ (uint)y * 0xd8163841u ^ (uint)channel * 0xcb1ab31fu);
            float a = (h & 0xFFFFu) * (1f / 65536f), b = (h >> 16) * (1f / 65536f);
            v += a - b;     // triangular on (-1, 1): the sum of two uniforms
        }
        return (byte)Math.Clamp((int)MathF.Round(v), 0, 255);
    }

    public static ushort To16Bit(float value)
    {
        float v = float.IsNaN(value) ? 0f : Math.Clamp(value, 0f, 1f);
        return (ushort)Math.Clamp((int)MathF.Round(v * 65535f), 0, 65535);
    }

    private static uint Hash(uint v)
    {
        v ^= v >> 16; v *= 0x7feb352du;
        v ^= v >> 15; v *= 0x846ca68bu;
        v ^= v >> 16;
        return v;
    }
}
