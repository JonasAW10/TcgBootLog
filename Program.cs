using System.Drawing;
using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using TcgBootLog.Ui;

namespace TcgBootLog;

static class Program
{
    private static IWindow? _window;
    private static GL? _gl;
    private static ImGuiController? _imgui;
    private static AppUi? _ui;

    [STAThread]
    static void Main()
    {
        var options = WindowOptions.Default;
        options.Title = "TcgBootLog";
        options.Size = new Vector2D<int>(1380, 860);
        options.API = new GraphicsAPI(
            ContextAPI.OpenGL,
            ContextProfile.Core,
            ContextFlags.ForwardCompatible,
            new APIVersion(3, 3));
        options.PreferredDepthBufferBits = 0;
        options.PreferredStencilBufferBits = 0;
        options.VSync = true;

        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.Render += OnRender;
        _window.Closing += OnClosing;
        _window.FramebufferResize += size => _gl?.Viewport(size);
        _window.Run();
    }

    private static void OnLoad()
    {
        IWindow window = _window ?? throw new InvalidOperationException("Window not created.");

        // Dark title bar + custom logo (matches ImGui theme)
        WindowChrome.Apply(window);

        _gl = window.CreateOpenGL();
        var input = window.CreateInput();
        _imgui = new ImGuiController(_gl, window, input);
        _ui = new AppUi();

        // Soft clear color matching theme Bg0
        _gl.ClearColor(Color.FromArgb(255, 14, 18, 24));
    }

    private static void OnRender(double delta)
    {
        if (_gl is null || _imgui is null || _ui is null || _window is null) return;

        _gl.Clear(ClearBufferMask.ColorBufferBit);
        _imgui.Update((float)delta);

        var size = new Vector2(_window.Size.X, _window.Size.Y);
        _ui.Draw(size);

        _imgui.Render();
    }

    private static void OnClosing()
    {
        _imgui?.Dispose();
        _gl?.Dispose();
    }
}
