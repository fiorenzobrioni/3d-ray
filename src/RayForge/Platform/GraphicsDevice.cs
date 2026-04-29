using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace RayForge.Platform;

/// <summary>
/// Owns the D3D11 device, immediate context, DXGI swap chain and the back buffer RTV.
/// Exposes simple BeginFrame/EndFrame helpers and a Resize entry point hooked to the window.
/// </summary>
internal sealed class GraphicsDevice : IDisposable
{
    public ID3D11Device Device { get; }
    public ID3D11DeviceContext Context { get; }
    public IDXGISwapChain1 SwapChain { get; private set; }
    public ID3D11RenderTargetView BackBufferRTV { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    public GraphicsDevice(IntPtr hwnd, int width, int height)
    {
        Width = width;
        Height = height;

        var flags = DeviceCreationFlags.BgraSupport;
#if DEBUG
        flags |= DeviceCreationFlags.Debug;
#endif

        var featureLevels = new[]
        {
            FeatureLevel.Level_11_1,
            FeatureLevel.Level_11_0,
        };

        Result hr = D3D11.D3D11CreateDevice(
            adapter: null,
            DriverType.Hardware,
            flags,
            featureLevels,
            out var device,
            out var context);
        if (hr.Failure || device is null || context is null)
            throw new InvalidOperationException($"D3D11CreateDevice failed: {hr}");

        Device = device;
        Context = context;

        using var dxgiDevice = Device.QueryInterface<IDXGIDevice>();
        using var adapter = dxgiDevice.GetAdapter();
        using var factory = adapter.GetParent<IDXGIFactory2>();

        var scDesc = new SwapChainDescription1
        {
            Width = width,
            Height = height,
            Format = Format.R8G8B8A8_UNorm,
            Stereo = false,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = 2,
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipDiscard,
            AlphaMode = AlphaMode.Unspecified,
            Flags = SwapChainFlags.None,
        };

        SwapChain = factory.CreateSwapChainForHwnd(Device, hwnd, scDesc);
        BackBufferRTV = CreateBackBufferRTV();
    }

    private ID3D11RenderTargetView CreateBackBufferRTV()
    {
        using var backBuffer = SwapChain.GetBuffer<ID3D11Texture2D>(0);
        return Device.CreateRenderTargetView(backBuffer);
    }

    public void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        if (width == Width && height == Height) return;

        Context.OMSetRenderTargets(Array.Empty<ID3D11RenderTargetView>(), null);
        BackBufferRTV.Dispose();

        SwapChain.ResizeBuffers(0, width, height, Format.Unknown, SwapChainFlags.None).CheckError();
        BackBufferRTV = CreateBackBufferRTV();

        Width = width;
        Height = height;
    }

    public void BeginFrame(System.Numerics.Vector4 clearColor)
    {
        Context.OMSetRenderTargets(BackBufferRTV);
        var vp = new Viewport(0, 0, Width, Height, 0f, 1f);
        Context.RSSetViewport(vp);
        Context.ClearRenderTargetView(BackBufferRTV, new Vortice.Mathematics.Color4(clearColor.X, clearColor.Y, clearColor.Z, clearColor.W));
    }

    public void Present(bool vsync)
    {
        SwapChain.Present(vsync ? 1 : 0, PresentFlags.None);
    }

    public void Dispose()
    {
        BackBufferRTV.Dispose();
        SwapChain.Dispose();
        Context.Dispose();
        Device.Dispose();
    }
}
