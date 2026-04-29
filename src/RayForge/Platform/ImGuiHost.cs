using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.D3D11;
using Hexa.NET.ImGui.Backends.Win32;
using Vortice.Direct3D11;

namespace RayForge.Platform;

/// <summary>
/// Hosts the ImGui context plus the Win32 + D3D11 backends.
/// The exact symbol names from Hexa.NET.ImGui.Backends may need a small adjustment when the
/// project is first built on Windows — flagged in DEVLOG-RAYFORGE.md (Phase 0).
/// </summary>
internal sealed unsafe class ImGuiHost : IDisposable
{
    private ImGuiContextPtr _ctx;
    private bool _initialized;

    public ImGuiHost(IntPtr hwnd, GraphicsDevice gfx)
    {
        _ctx = ImGui.CreateContext();
        ImGui.SetCurrentContext(_ctx);

        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        // Multi-viewport (windows that detach from the main window) — keep off until tested:
        // io.ConfigFlags |= ImGuiConfigFlags.ViewportsEnable;

        ImGui.StyleColorsDark();

        ImGuiImplWin32.Init(hwnd);
        ImGuiImplD3D11.Init(
            (ID3D11DevicePtr)(void*)gfx.Device.NativePointer,
            (ID3D11DeviceContextPtr)(void*)gfx.Context.NativePointer);

        _initialized = true;
    }

    /// <summary>Pluggable WndProc hook so the Win32 backend can consume input messages.</summary>
    public IntPtr WndProcHandler(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        return (IntPtr)ImGuiImplWin32.WndProcHandler(hwnd, msg, (nuint)wParam, (nint)lParam);
    }

    public void NewFrame()
    {
        ImGuiImplD3D11.NewFrame();
        ImGuiImplWin32.NewFrame();
        ImGui.NewFrame();
    }

    public void Render()
    {
        ImGui.Render();
        ImGuiImplD3D11.RenderDrawData(ImGui.GetDrawData());
    }

    public void Dispose()
    {
        if (!_initialized) return;
        ImGuiImplD3D11.Shutdown();
        ImGuiImplWin32.Shutdown();
        ImGui.DestroyContext(_ctx);
        _initialized = false;
    }
}
