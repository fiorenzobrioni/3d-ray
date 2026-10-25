using System.Reflection;
using System.Runtime.InteropServices;

namespace RayTracer.Denoising;

/// <summary>
/// Minimal binding of the Intel Open Image Denoise 2.x C API (Apache-2.0):
/// only what <see cref="OidnDenoiser"/> needs (CPU device, the "RT" filter,
/// shared float3 images). The native libraries ship with the build as the
/// native assets of a NuGet package (linux-x64, win-x64, osx-x64,
/// osx-arm64); <c>OIDN_DIR</c> points the loader at another OIDN 2.x
/// installation instead (its <c>lib</c>/<c>bin</c> folder or the folder
/// itself), for a newer release or a platform the package lacks.
/// </summary>
public static class Oidn
{
    private const string Lib = "OpenImageDenoise";

    private static readonly object s_lock = new();
    private static bool s_probed;
    private static string? s_loadError;

    static Oidn()
    {
        NativeLibrary.SetDllImportResolver(typeof(Oidn).Assembly, Resolve);
    }

    /// <summary>
    /// True when the native library loads and a CPU device commits. The probe
    /// runs once; <see cref="LoadError"/> then says why it failed.
    /// </summary>
    public static bool IsAvailable
    {
        get
        {
            Probe();
            return s_loadError == null;
        }
    }

    /// <summary>Why OIDN is unavailable, or null when it loaded.</summary>
    public static string? LoadError
    {
        get
        {
            Probe();
            return s_loadError;
        }
    }

    private static void Probe()
    {
        lock (s_lock)
        {
            if (s_probed) return;
            s_probed = true;
            try
            {
                using var device = new Device();
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException
                                          or BadImageFormatException or OidnException)
            {
                s_loadError = e.Message;
            }
        }
    }

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path)
    {
        if (name != Lib) return IntPtr.Zero;
        string? dir = Environment.GetEnvironmentVariable("OIDN_DIR");
        if (string.IsNullOrEmpty(dir)) return IntPtr.Zero;   // default probing: the package's native assets
        string file = OperatingSystem.IsWindows() ? "OpenImageDenoise.dll"
                    : OperatingSystem.IsMacOS() ? "libOpenImageDenoise.dylib"
                    : "libOpenImageDenoise.so";
        foreach (var sub in new[] { "", "lib", "lib64", "bin" })
        {
            string candidate = Path.Combine(dir, sub, file);
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle))
                return handle;
        }
        return IntPtr.Zero;
    }

    // ── C API ────────────────────────────────────────────────────────────────

    private const int DeviceTypeCpu = 1;
    internal const int FormatFloat3 = 3;
    internal const int QualityBalanced = 5;
    internal const int QualityHigh = 6;

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr oidnNewDevice(int type);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void oidnCommitDevice(IntPtr device);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void oidnReleaseDevice(IntPtr device);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int oidnGetDeviceError(IntPtr device, out IntPtr message);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr oidnNewFilter(IntPtr device,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string type);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void oidnSetSharedFilterImage(IntPtr filter,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, IntPtr devPtr, int format,
        nuint width, nuint height, nuint byteOffset, nuint pixelByteStride, nuint rowByteStride);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void oidnSetFilterBool(IntPtr filter,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.U1)] bool value);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void oidnSetFilterInt(IntPtr filter,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int value);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void oidnCommitFilter(IntPtr filter);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void oidnExecuteFilter(IntPtr filter);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void oidnReleaseFilter(IntPtr filter);

    internal sealed class OidnException(string message) : Exception(message);

    /// <summary>A committed CPU device; errors surface as <see cref="OidnException"/>.</summary>
    internal sealed class Device : IDisposable
    {
        public IntPtr Handle { get; private set; }

        public Device()
        {
            Handle = oidnNewDevice(DeviceTypeCpu);
            if (Handle == IntPtr.Zero) throw new OidnException("OIDN: no CPU device.");
            oidnCommitDevice(Handle);
            Check();
        }

        public void Check()
        {
            int code = oidnGetDeviceError(Handle, out var msg);
            if (code == 0) return;
            string text = msg != IntPtr.Zero ? Marshal.PtrToStringUTF8(msg) ?? "" : "";
            throw new OidnException($"OIDN error {code}: {text}");
        }

        public void Dispose()
        {
            if (Handle == IntPtr.Zero) return;
            oidnReleaseDevice(Handle);
            Handle = IntPtr.Zero;
        }
    }

    /// <summary>
    /// One "RT" filter over pinned, interleaved RGB float images. The images
    /// stay pinned until the filter is disposed.
    /// </summary>
    internal sealed class Filter : IDisposable
    {
        private readonly Device _device;
        private readonly List<GCHandle> _pins = new();
        private IntPtr _handle;

        public Filter(Device device)
        {
            _device = device;
            _handle = oidnNewFilter(device.Handle, "RT");
            device.Check();
        }

        public void SetImage(string name, float[] rgb, int width, int height)
        {
            if (rgb.Length != 3 * width * height)
                throw new ArgumentException($"Image '{name}' is not {width}×{height} RGB.");
            var pin = GCHandle.Alloc(rgb, GCHandleType.Pinned);
            _pins.Add(pin);
            oidnSetSharedFilterImage(_handle, name, pin.AddrOfPinnedObject(), FormatFloat3,
                (nuint)width, (nuint)height, 0, 0, 0);
        }

        public void Set(string name, bool value) => oidnSetFilterBool(_handle, name, value);
        public void Set(string name, int value) => oidnSetFilterInt(_handle, name, value);

        public void Run()
        {
            oidnCommitFilter(_handle);
            _device.Check();
            oidnExecuteFilter(_handle);
            _device.Check();
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                oidnReleaseFilter(_handle);
                _handle = IntPtr.Zero;
            }
            foreach (var pin in _pins) pin.Free();
            _pins.Clear();
        }
    }
}
