using static RayForge.Platform.Win32Native;

namespace RayForge.Platform;

/// <summary>
/// Minimal Win32 top-level window. Owns the message pump and exposes resize / close events.
/// The WndProc is intentionally pluggable so the ImGui Win32 backend can intercept input messages.
/// </summary>
internal sealed class Win32Window : IDisposable
{
    public IntPtr Hwnd { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    public event Action<int, int>? Resized;
    public event Action? Closed;

    public delegate IntPtr ExtraWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    public ExtraWndProc? ImGuiHook;

    private readonly WndProcDelegate _wndProc;
    private readonly string _className;
    private readonly IntPtr _hInstance;
    private bool _quit;

    public Win32Window(string title, int width, int height)
    {
        Width = width;
        Height = height;
        _className = $"RayForgeWnd_{Guid.NewGuid():N}";
        _hInstance = GetModuleHandleW(null);
        _wndProc = WndProc;

        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            style = CS_HREDRAW | CS_VREDRAW | CS_OWNDC,
            lpfnWndProc = _wndProc,
            hInstance = _hInstance,
            hCursor = LoadCursorW(IntPtr.Zero, IDC_ARROW),
            lpszClassName = _className,
        };
        if (RegisterClassExW(ref wc) == 0)
            throw new InvalidOperationException($"RegisterClassExW failed: {Marshal.GetLastWin32Error()}");

        Hwnd = CreateWindowExW(
            WS_EX_APPWINDOW,
            _className, title,
            WS_OVERLAPPEDWINDOW | WS_VISIBLE,
            CW_USEDEFAULT, CW_USEDEFAULT, width, height,
            IntPtr.Zero, IntPtr.Zero, _hInstance, IntPtr.Zero);
        if (Hwnd == IntPtr.Zero)
            throw new InvalidOperationException($"CreateWindowExW failed: {Marshal.GetLastWin32Error()}");

        ShowWindow(Hwnd, SW_SHOWDEFAULT);
        UpdateWindow(Hwnd);
    }

    public bool PumpMessages()
    {
        while (PeekMessageW(out var msg, IntPtr.Zero, 0, 0, PM_REMOVE))
        {
            if (msg.message == WM_QUIT)
            {
                _quit = true;
                return false;
            }
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
        return !_quit;
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        // Let the ImGui backend look at the message first.
        if (ImGuiHook is not null)
        {
            var r = ImGuiHook(hWnd, msg, wParam, lParam);
            if (r != IntPtr.Zero) return r;
        }

        switch (msg)
        {
            case WM_SIZE:
            {
                int w = (int)(lParam.ToInt64() & 0xFFFF);
                int h = (int)((lParam.ToInt64() >> 16) & 0xFFFF);
                if (w > 0 && h > 0 && (w != Width || h != Height))
                {
                    Width = w;
                    Height = h;
                    Resized?.Invoke(w, h);
                }
                return IntPtr.Zero;
            }
            case WM_CLOSE:
                Closed?.Invoke();
                DestroyWindow(hWnd);
                return IntPtr.Zero;
            case WM_DESTROY:
                PostQuitMessage(0);
                return IntPtr.Zero;
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (Hwnd != IntPtr.Zero)
        {
            DestroyWindow(Hwnd);
            Hwnd = IntPtr.Zero;
        }
    }
}
