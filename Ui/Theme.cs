using System.Numerics;
using ImGuiNET;

namespace TcgBootLog.Ui;

public static class Theme
{
    // Smooth slate + soft teal accents (no harsh neon / purple)
    public static readonly Vector4 Bg0 = Hex(0x0E1218);
    public static readonly Vector4 Bg1 = Hex(0x151B24);
    public static readonly Vector4 Bg2 = Hex(0x1C2430);
    public static readonly Vector4 Bg3 = Hex(0x243040);
    public static readonly Vector4 Border = Hex(0x2E3A4A, 0.85f);
    public static readonly Vector4 Text = Hex(0xF4F7FB);
    public static readonly Vector4 TextDim = Hex(0xA8B4C4);
    public static readonly Vector4 Accent = Hex(0x5FA8A0);
    public static readonly Vector4 AccentSoft = Hex(0x5FA8A0, 0.35f);
    public static readonly Vector4 AccentHover = Hex(0x78C0B7);
    public static readonly Vector4 Danger = Hex(0xD17A6E);
    public static readonly Vector4 Warn = Hex(0xD2B06A);
    public static readonly Vector4 Ok = Hex(0x7BBF8E);
    public static readonly Vector4 RowAlt = Hex(0x121821, 0.55f);
    public static readonly Vector4 EfiPath = Hex(0x9BC4FF);

    public static void Apply()
    {
        var style = ImGui.GetStyle();
        style.WindowRounding = 10f;
        style.ChildRounding = 8f;
        style.FrameRounding = 6f;
        style.PopupRounding = 6f;
        style.ScrollbarRounding = 8f;
        style.GrabRounding = 6f;
        style.TabRounding = 6f;
        style.WindowBorderSize = 1f;
        style.FrameBorderSize = 0f;
        style.PopupBorderSize = 1f;
        style.WindowPadding = new Vector2(20, 18);
        style.FramePadding = new Vector2(14, 10);
        style.CellPadding = new Vector2(12, 10);
        style.ItemSpacing = new Vector2(12, 12);
        style.ItemInnerSpacing = new Vector2(10, 7);
        style.IndentSpacing = 22f;
        style.ScrollbarSize = 16f;
        style.GrabMinSize = 12f;
        style.Alpha = 1f;

        var c = style.Colors;
        c[(int)ImGuiCol.Text] = Text;
        c[(int)ImGuiCol.TextDisabled] = TextDim;
        c[(int)ImGuiCol.WindowBg] = Bg0;
        c[(int)ImGuiCol.ChildBg] = Bg1;
        c[(int)ImGuiCol.PopupBg] = Bg2;
        c[(int)ImGuiCol.Border] = Border;
        c[(int)ImGuiCol.BorderShadow] = new Vector4(0, 0, 0, 0);
        c[(int)ImGuiCol.FrameBg] = Bg2;
        c[(int)ImGuiCol.FrameBgHovered] = Bg3;
        c[(int)ImGuiCol.FrameBgActive] = AccentSoft;
        c[(int)ImGuiCol.TitleBg] = Bg0;
        c[(int)ImGuiCol.TitleBgActive] = Bg1;
        c[(int)ImGuiCol.TitleBgCollapsed] = Bg0;
        c[(int)ImGuiCol.MenuBarBg] = Bg1;
        c[(int)ImGuiCol.ScrollbarBg] = Bg0;
        c[(int)ImGuiCol.ScrollbarGrab] = Bg3;
        c[(int)ImGuiCol.ScrollbarGrabHovered] = AccentSoft;
        c[(int)ImGuiCol.ScrollbarGrabActive] = Accent;
        c[(int)ImGuiCol.CheckMark] = Accent;
        c[(int)ImGuiCol.SliderGrab] = Accent;
        c[(int)ImGuiCol.SliderGrabActive] = AccentHover;
        c[(int)ImGuiCol.Button] = Bg3;
        c[(int)ImGuiCol.ButtonHovered] = AccentSoft;
        c[(int)ImGuiCol.ButtonActive] = Accent;
        c[(int)ImGuiCol.Header] = Bg3;
        c[(int)ImGuiCol.HeaderHovered] = AccentSoft;
        c[(int)ImGuiCol.HeaderActive] = Accent;
        c[(int)ImGuiCol.Separator] = Border;
        c[(int)ImGuiCol.SeparatorHovered] = Accent;
        c[(int)ImGuiCol.SeparatorActive] = AccentHover;
        c[(int)ImGuiCol.ResizeGrip] = AccentSoft;
        c[(int)ImGuiCol.ResizeGripHovered] = Accent;
        c[(int)ImGuiCol.ResizeGripActive] = AccentHover;
        c[(int)ImGuiCol.Tab] = Bg2;
        c[(int)ImGuiCol.TabHovered] = AccentSoft;
        c[(int)ImGuiCol.TabSelected] = Bg3;
        c[(int)ImGuiCol.TableHeaderBg] = Bg2;
        c[(int)ImGuiCol.TableBorderStrong] = Border;
        c[(int)ImGuiCol.TableBorderLight] = Hex(0x2E3A4A, 0.45f);
        c[(int)ImGuiCol.TableRowBg] = new Vector4(0, 0, 0, 0);
        c[(int)ImGuiCol.TableRowBgAlt] = RowAlt;
        c[(int)ImGuiCol.TextSelectedBg] = AccentSoft;
        c[(int)ImGuiCol.PlotHistogram] = Accent;
    }

    public static Vector4 Hex(uint rgb, float a = 1f)
    {
        float r = ((rgb >> 16) & 0xFF) / 255f;
        float g = ((rgb >> 8) & 0xFF) / 255f;
        float b = (rgb & 0xFF) / 255f;
        return new Vector4(r, g, b, a);
    }
}
