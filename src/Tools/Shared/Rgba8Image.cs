using RayTracer.Imaging;

/// <summary>An 8-bit RGBA colour (alpha opaque by default).</summary>
readonly record struct Rgba32(byte R, byte G, byte B, byte A = 255);

/// <summary>
/// A writable 8-bit RGBA image for the texture generators, saved through the
/// engine's own PNG writer (<see cref="Png"/>).
/// </summary>
sealed class Rgba8Image(int width, int height) : IDisposable
{
    private readonly RasterImage _image = new(width, height, 4, 8);

    public int Width => width;
    public int Height => height;

    public Rgba32 this[int x, int y]
    {
        get
        {
            _image.GetRgba(x, y, out int r, out int g, out int b, out int a);
            return new Rgba32((byte)r, (byte)g, (byte)b, (byte)a);
        }
        set
        {
            int o = (y * width + x) * 4;
            var s = _image.Samples;
            s[o] = value.R;
            s[o + 1] = value.G;
            s[o + 2] = value.B;
            s[o + 3] = value.A;
        }
    }

    public void SaveAsPng(string path) => Png.Save(path, _image);

    public void Dispose() { }
}
