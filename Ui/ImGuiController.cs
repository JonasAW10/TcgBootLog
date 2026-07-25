using System.Drawing;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace TcgBootLog.Ui;

public sealed class ImGuiController : IDisposable
{
    private readonly GL _gl;
    private readonly IView _view;
    private readonly IInputContext _input;
    private readonly List<char> _pressedChars = [];
    private readonly List<Key> _keysDown = [];

    private int _attribLocationTex;
    private int _attribLocationProjMtx;
    private int _attribLocationVtxPos;
    private int _attribLocationVtxUV;
    private int _attribLocationVtxColor;
    private uint _vboHandle;
    private uint _elementsHandle;
    private uint _vertexArrayObject;
    private uint _fontTexture;
    private uint _shader;

    private bool _frameBegun;
    private int _windowWidth;
    private int _windowHeight;

    public ImGuiController(GL gl, IView view, IInputContext input)
    {
        _gl = gl;
        _view = view;
        _input = input;
        _windowWidth = view.Size.X;
        _windowHeight = view.Size.Y;

        IntPtr ctx = ImGui.CreateContext();
        ImGui.SetCurrentContext(ctx);
        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
        LoadFonts(io);

        Theme.Apply();
        CreateDeviceResources();
        SetPerFrameImGuiData(1f / 60f);
        SetKeyMappings();

        view.Resize += OnViewResized;
        foreach (var keyboard in input.Keyboards)
        {
            keyboard.KeyChar += OnKeyChar;
            keyboard.KeyDown += OnKeyDown;
            keyboard.KeyUp += OnKeyUp;
        }
    }

    public void Update(float deltaSeconds)
    {
        if (_frameBegun)
            ImGui.Render();

        SetPerFrameImGuiData(deltaSeconds);
        UpdateInput();

        _frameBegun = true;
        ImGui.NewFrame();
    }

    public void Render()
    {
        if (!_frameBegun) return;
        _frameBegun = false;
        ImGui.Render();
        RenderImDrawData(ImGui.GetDrawData());
    }

    private void OnViewResized(Vector2D<int> size)
    {
        _windowWidth = size.X;
        _windowHeight = size.Y;
    }

    private static void LoadFonts(ImGuiIOPtr io)
    {
        // Large Cascadia Code — clear for digests + EFI paths. Falls back if missing.
        const float size = 22f;
        string[] candidates =
        [
            @"C:\Windows\Fonts\CascadiaCode.ttf",
            @"C:\Windows\Fonts\CascadiaMono.ttf",
            @"C:\Windows\Fonts\consola.ttf",
            @"C:\Windows\Fonts\segoeui.ttf",
        ];

        bool loaded = false;
        foreach (var path in candidates)
        {
            if (!File.Exists(path)) continue;
            io.Fonts.AddFontFromFileTTF(path, size);
            loaded = true;
            break;
        }

        if (!loaded)
            io.Fonts.AddFontDefault();

        io.FontGlobalScale = 1.05f;
    }

    private void OnKeyChar(IKeyboard keyboard, char c) => _pressedChars.Add(c);
    private void OnKeyDown(IKeyboard keyboard, Key key, int scancode) => _keysDown.Add(key);
    private void OnKeyUp(IKeyboard keyboard, Key key, int scancode) => _keysDown.Remove(key);

    private void SetPerFrameImGuiData(float deltaSeconds)
    {
        var io = ImGui.GetIO();
        io.DisplaySize = new Vector2(_windowWidth, _windowHeight);
        io.DisplayFramebufferScale = new Vector2(
            _view.FramebufferSize.X / (float)_windowWidth,
            _view.FramebufferSize.Y / (float)_windowHeight);
        io.DeltaTime = deltaSeconds > 0 ? deltaSeconds : 1f / 60f;
    }

    private void UpdateInput()
    {
        var io = ImGui.GetIO();
        var mouse = _input.Mice[0];
        var keyboard = _input.Keyboards[0];

        io.MouseDown[0] = mouse.IsButtonPressed(MouseButton.Left);
        io.MouseDown[1] = mouse.IsButtonPressed(MouseButton.Right);
        io.MouseDown[2] = mouse.IsButtonPressed(MouseButton.Middle);
        var point = new Point((int)mouse.Position.X, (int)mouse.Position.Y);
        io.MousePos = new Vector2(point.X, point.Y);
        io.MouseWheel = mouse.ScrollWheels[0].Y;

        foreach (var c in _pressedChars)
            io.AddInputCharacter(c);
        _pressedChars.Clear();

        foreach (ImGuiKey key in Enum.GetValues<ImGuiKey>())
        {
            if (TryMapImGuiKey(key, out var silkKey))
                io.AddKeyEvent(key, keyboard.IsKeyPressed(silkKey));
        }

        io.KeyCtrl = keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight);
        io.KeyAlt = keyboard.IsKeyPressed(Key.AltLeft) || keyboard.IsKeyPressed(Key.AltRight);
        io.KeyShift = keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight);
        io.KeySuper = keyboard.IsKeyPressed(Key.SuperLeft) || keyboard.IsKeyPressed(Key.SuperRight);
    }

    private static bool TryMapImGuiKey(ImGuiKey key, out Key silkKey)
    {
        silkKey = key switch
        {
            ImGuiKey.Tab => Key.Tab,
            ImGuiKey.LeftArrow => Key.Left,
            ImGuiKey.RightArrow => Key.Right,
            ImGuiKey.UpArrow => Key.Up,
            ImGuiKey.DownArrow => Key.Down,
            ImGuiKey.PageUp => Key.PageUp,
            ImGuiKey.PageDown => Key.PageDown,
            ImGuiKey.Home => Key.Home,
            ImGuiKey.End => Key.End,
            ImGuiKey.Insert => Key.Insert,
            ImGuiKey.Delete => Key.Delete,
            ImGuiKey.Backspace => Key.Backspace,
            ImGuiKey.Space => Key.Space,
            ImGuiKey.Enter => Key.Enter,
            ImGuiKey.Escape => Key.Escape,
            ImGuiKey.A => Key.A,
            ImGuiKey.C => Key.C,
            ImGuiKey.V => Key.V,
            ImGuiKey.X => Key.X,
            ImGuiKey.Y => Key.Y,
            ImGuiKey.Z => Key.Z,
            _ => Key.Unknown,
        };
        return silkKey != Key.Unknown;
    }

    private void SetKeyMappings()
    {
        // ImGui 1.87+ uses AddKeyEvent; legacy map kept empty.
    }

    private unsafe void CreateDeviceResources()
    {
        _vertexArrayObject = _gl.GenVertexArray();
        _vboHandle = _gl.GenBuffer();
        _elementsHandle = _gl.GenBuffer();

        string vertexSource = """
            #version 330 core
            layout (location = 0) in vec2 Position;
            layout (location = 1) in vec2 UV;
            layout (location = 2) in vec4 Color;
            uniform mat4 ProjMtx;
            out vec2 Frag_UV;
            out vec4 Frag_Color;
            void main()
            {
                Frag_UV = UV;
                Frag_Color = Color;
                gl_Position = ProjMtx * vec4(Position.xy, 0, 1);
            }
            """;

        string fragmentSource = """
            #version 330 core
            in vec2 Frag_UV;
            in vec4 Frag_Color;
            uniform sampler2D Texture;
            layout (location = 0) out vec4 Out_Color;
            void main()
            {
                Out_Color = Frag_Color * texture(Texture, Frag_UV.st);
            }
            """;

        uint vert = CompileShader(ShaderType.VertexShader, vertexSource);
        uint frag = CompileShader(ShaderType.FragmentShader, fragmentSource);
        _shader = _gl.CreateProgram();
        _gl.AttachShader(_shader, vert);
        _gl.AttachShader(_shader, frag);
        _gl.LinkProgram(_shader);
        _gl.GetProgram(_shader, GLEnum.LinkStatus, out int linked);
        if (linked == 0)
            throw new Exception("ImGui shader link failed: " + _gl.GetProgramInfoLog(_shader));

        _gl.DetachShader(_shader, vert);
        _gl.DetachShader(_shader, frag);
        _gl.DeleteShader(vert);
        _gl.DeleteShader(frag);

        _attribLocationTex = _gl.GetUniformLocation(_shader, "Texture");
        _attribLocationProjMtx = _gl.GetUniformLocation(_shader, "ProjMtx");
        _attribLocationVtxPos = _gl.GetAttribLocation(_shader, "Position");
        _attribLocationVtxUV = _gl.GetAttribLocation(_shader, "UV");
        _attribLocationVtxColor = _gl.GetAttribLocation(_shader, "Color");

        RecreateFontDeviceTexture();
    }

    private uint CompileShader(ShaderType type, string source)
    {
        uint shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
        if (status == 0)
            throw new Exception($"Shader compile failed ({type}): {_gl.GetShaderInfoLog(shader)}");
        return shader;
    }

    private unsafe void RecreateFontDeviceTexture()
    {
        var io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out _);

        _fontTexture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _fontTexture);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, (uint)width, (uint)height, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, (void*)pixels);
        io.Fonts.SetTexID((IntPtr)_fontTexture);
        io.Fonts.ClearTexData();
    }

    private unsafe void RenderImDrawData(ImDrawDataPtr drawData)
    {
        if (drawData.CmdListsCount == 0) return;

        int fbWidth = (int)(drawData.DisplaySize.X * drawData.FramebufferScale.X);
        int fbHeight = (int)(drawData.DisplaySize.Y * drawData.FramebufferScale.Y);
        if (fbWidth <= 0 || fbHeight <= 0) return;

        drawData.ScaleClipRects(drawData.FramebufferScale);

        _gl.Enable(EnableCap.Blend);
        _gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
        _gl.BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha,
            BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.StencilTest);
        _gl.Enable(EnableCap.ScissorTest);

        _gl.Viewport(0, 0, (uint)fbWidth, (uint)fbHeight);

        float L = drawData.DisplayPos.X;
        float R = drawData.DisplayPos.X + drawData.DisplaySize.X;
        float T = drawData.DisplayPos.Y;
        float B = drawData.DisplayPos.Y + drawData.DisplaySize.Y;

        float[] mvp =
        [
            2f / (R - L), 0, 0, 0,
            0, 2f / (T - B), 0, 0,
            0, 0, -1, 0,
            (R + L) / (L - R), (T + B) / (B - T), 0, 1,
        ];

        _gl.UseProgram(_shader);
        _gl.Uniform1(_attribLocationTex, 0);
        _gl.UniformMatrix4(_attribLocationProjMtx, 1, false, mvp);
        _gl.BindVertexArray(_vertexArrayObject);

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboHandle);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _elementsHandle);

        _gl.EnableVertexAttribArray((uint)_attribLocationVtxPos);
        _gl.EnableVertexAttribArray((uint)_attribLocationVtxUV);
        _gl.EnableVertexAttribArray((uint)_attribLocationVtxColor);
        _gl.VertexAttribPointer((uint)_attribLocationVtxPos, 2, VertexAttribPointerType.Float, false, (uint)sizeof(ImDrawVert), (void*)0);
        _gl.VertexAttribPointer((uint)_attribLocationVtxUV, 2, VertexAttribPointerType.Float, false, (uint)sizeof(ImDrawVert), (void*)8);
        _gl.VertexAttribPointer((uint)_attribLocationVtxColor, 4, VertexAttribPointerType.UnsignedByte, true, (uint)sizeof(ImDrawVert), (void*)16);

        Vector2 clipOff = drawData.DisplayPos;
        Vector2 clipScale = drawData.FramebufferScale;

        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            ImDrawListPtr cmdList = drawData.CmdLists[n];

            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(cmdList.VtxBuffer.Size * sizeof(ImDrawVert)),
                (void*)cmdList.VtxBuffer.Data, BufferUsageARB.StreamDraw);
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(cmdList.IdxBuffer.Size * sizeof(ushort)),
                (void*)cmdList.IdxBuffer.Data, BufferUsageARB.StreamDraw);

            for (int cmd_i = 0; cmd_i < cmdList.CmdBuffer.Size; cmd_i++)
            {
                ImDrawCmdPtr pcmd = cmdList.CmdBuffer[cmd_i];
                if (pcmd.UserCallback != IntPtr.Zero)
                    continue;

                Vector4 clipRect;
                clipRect.X = (pcmd.ClipRect.X - clipOff.X) * clipScale.X;
                clipRect.Y = (pcmd.ClipRect.Y - clipOff.Y) * clipScale.Y;
                clipRect.Z = (pcmd.ClipRect.Z - clipOff.X) * clipScale.X;
                clipRect.W = (pcmd.ClipRect.W - clipOff.Y) * clipScale.Y;

                if (clipRect.X < fbWidth && clipRect.Y < fbHeight && clipRect.Z >= 0 && clipRect.W >= 0)
                {
                    _gl.Scissor((int)clipRect.X, (int)(fbHeight - clipRect.W),
                        (uint)(clipRect.Z - clipRect.X), (uint)(clipRect.W - clipRect.Y));

                    _gl.ActiveTexture(TextureUnit.Texture0);
                    _gl.BindTexture(TextureTarget.Texture2D, (uint)pcmd.TextureId);
                    _gl.DrawElementsBaseVertex(PrimitiveType.Triangles, pcmd.ElemCount,
                        DrawElementsType.UnsignedShort, (void*)(pcmd.IdxOffset * sizeof(ushort)), (int)pcmd.VtxOffset);
                }
            }
        }

        _gl.Disable(EnableCap.ScissorTest);
    }

    public void Dispose()
    {
        _view.Resize -= OnViewResized;
        foreach (var keyboard in _input.Keyboards)
        {
            keyboard.KeyChar -= OnKeyChar;
            keyboard.KeyDown -= OnKeyDown;
            keyboard.KeyUp -= OnKeyUp;
        }

        _gl.DeleteBuffer(_vboHandle);
        _gl.DeleteBuffer(_elementsHandle);
        _gl.DeleteVertexArray(_vertexArrayObject);
        _gl.DeleteTexture(_fontTexture);
        _gl.DeleteProgram(_shader);
        ImGui.DestroyContext();
    }
}
