using RayForge.Platform;
using RayForge.UI;

namespace RayForge.App;

internal sealed class Application : IDisposable
{
    private readonly Win32Window _window;
    private readonly GraphicsDevice _gfx;
    private readonly ImGuiHost _imgui;
    private readonly MainDockspace _dockspace;

    private bool _running = true;

    public Application(string title, int width, int height)
    {
        _window = new Win32Window(title, width, height);
        _gfx = new GraphicsDevice(_window.Hwnd, _window.Width, _window.Height);
        _imgui = new ImGuiHost(_window.Hwnd, _gfx);
        _dockspace = new MainDockspace();

        _window.ImGuiHook = _imgui.WndProcHandler;
        _window.Resized += OnResized;
        _window.Closed += () => _running = false;
    }

    public int Run()
    {
        var clear = new System.Numerics.Vector4(0.10f, 0.11f, 0.12f, 1.0f);

        while (_running && _window.PumpMessages())
        {
            _imgui.NewFrame();
            _dockspace.Draw();
            if (_dockspace.RequestExit) _running = false;

            _gfx.BeginFrame(clear);
            _imgui.Render();
            _gfx.Present(vsync: true);
        }

        return 0;
    }

    private void OnResized(int w, int h) => _gfx.Resize(w, h);

    public void Dispose()
    {
        _imgui.Dispose();
        _gfx.Dispose();
        _window.Dispose();
    }
}
