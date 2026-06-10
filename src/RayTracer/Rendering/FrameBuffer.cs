using System.Numerics;
using System.Runtime.CompilerServices;

namespace RayTracer.Rendering;

/// <summary>
/// Planar (structure-of-arrays) float image used by the AOV/denoising
/// pipeline. Channel planes are stored back-to-back —
/// <c>Data[c·W·H + y·W + x]</c>, row 0 = top — so per-channel filters run
/// SIMD-friendly over contiguous rows and single-channel buffers pay for
/// exactly one plane.
/// </summary>
public sealed class FrameBuffer
{
    public int Width { get; }
    public int Height { get; }
    public int Channels { get; }
    public float[] Data { get; }

    private readonly int _planeSize;

    public FrameBuffer(int width, int height, int channels)
    {
        Width = width;
        Height = height;
        Channels = channels;
        _planeSize = width * height;
        Data = new float[_planeSize * channels];
    }

    /// <summary>One full channel plane (W·H floats).</summary>
    public Span<float> Plane(int c) => Data.AsSpan(c * _planeSize, _planeSize);

    /// <summary>One scanline of one channel plane (W floats).</summary>
    public Span<float> Row(int c, int y) => Data.AsSpan(c * _planeSize + y * Width, Width);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Get(int c, int x, int y) => Data[c * _planeSize + y * Width + x];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(int c, int x, int y, float v) => Data[c * _planeSize + y * Width + x] = v;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector3 GetRgb(int x, int y)
    {
        int i = y * Width + x;
        return new Vector3(Data[i], Data[_planeSize + i], Data[2 * _planeSize + i]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetRgb(int x, int y, Vector3 v)
    {
        int i = y * Width + x;
        Data[i] = v.X;
        Data[_planeSize + i] = v.Y;
        Data[2 * _planeSize + i] = v.Z;
    }

    public FrameBuffer Clone()
    {
        var copy = new FrameBuffer(Width, Height, Channels);
        Array.Copy(Data, copy.Data, Data.Length);
        return copy;
    }

    /// <summary>Writes this buffer as a PFM file (PF for 3 channels, Pf for 1).</summary>
    public void SavePfm(string path) => PfmImage.Write(path, Data, Width, Height, Channels);

    public static FrameBuffer LoadPfm(string path)
    {
        var (planes, w, h, c) = PfmImage.Read(path);
        var fb = new FrameBuffer(w, h, c);
        Array.Copy(planes, fb.Data, planes.Length);
        return fb;
    }
}
