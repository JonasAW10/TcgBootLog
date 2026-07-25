using System.Numerics;
using ImGuiNET;
using TcgBootLog.Services;

namespace TcgBootLog.Ui;

/// <summary>
/// Small vector logos for Windows Security features (drawn, not font glyphs).
/// </summary>
public static class FeatureIcons
{
    public static void Draw(SecurityFeatureKind kind, float size = 0f)
    {
        if (size <= 0) size = ImGui.GetTextLineHeight() * 1.55f;

        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var c = p + new Vector2(size * 0.5f, size * 0.5f);
        float s = size * 0.42f;

        uint accent = ImGui.ColorConvertFloat4ToU32(Theme.AccentHover);
        uint fill = ImGui.ColorConvertFloat4ToU32(Theme.Bg3);
        uint ink = ImGui.ColorConvertFloat4ToU32(Theme.Text);

        dl.AddCircleFilled(c, size * 0.48f, fill, 24);
        dl.AddCircle(c, size * 0.48f, accent, 24, 1.5f);

        switch (kind)
        {
            case SecurityFeatureKind.Hypervisor:
                // CPU chip
                dl.AddRectFilled(c + new Vector2(-s * 0.55f, -s * 0.45f), c + new Vector2(s * 0.55f, s * 0.45f), accent, 2f);
                dl.AddRectFilled(c + new Vector2(-s * 0.28f, -s * 0.22f), c + new Vector2(s * 0.28f, s * 0.22f), fill, 1f);
                break;

            case SecurityFeatureKind.Vbs:
                // Layered shield
                DrawShield(dl, c, s * 1.05f, accent);
                DrawShield(dl, c + new Vector2(0, s * 0.08f), s * 0.62f, ink);
                break;

            case SecurityFeatureKind.Hvci:
                DrawShield(dl, c, s, accent);
                // Lock body
                dl.AddRectFilled(c + new Vector2(-s * 0.28f, -s * 0.05f), c + new Vector2(s * 0.28f, s * 0.38f), fill, 2f);
                dl.PathClear();
                dl.PathArcTo(c + new Vector2(0, -s * 0.05f), s * 0.22f, MathF.PI, 0, 12);
                dl.PathStroke(fill, ImDrawFlags.None, 2.2f);
                break;

            case SecurityFeatureKind.SecureBoot:
                // Padlock
                dl.PathClear();
                dl.PathArcTo(c + new Vector2(0, -s * 0.15f), s * 0.32f, MathF.PI, 0, 14);
                dl.PathStroke(accent, ImDrawFlags.None, 2.4f);
                dl.AddRectFilled(c + new Vector2(-s * 0.38f, -s * 0.05f), c + new Vector2(s * 0.38f, s * 0.45f), accent, 3f);
                dl.AddCircleFilled(c + new Vector2(0, s * 0.12f), s * 0.1f, fill, 12);
                break;

            case SecurityFeatureKind.DriverSignature:
                // Certificate / pen
                dl.AddRectFilled(c + new Vector2(-s * 0.4f, -s * 0.45f), c + new Vector2(s * 0.4f, s * 0.45f), accent, 2f);
                dl.AddLine(c + new Vector2(-s * 0.22f, -s * 0.15f), c + new Vector2(s * 0.22f, -s * 0.15f), fill, 2f);
                dl.AddLine(c + new Vector2(-s * 0.22f, s * 0.05f), c + new Vector2(s * 0.12f, s * 0.05f), fill, 2f);
                dl.AddCircleFilled(c + new Vector2(s * 0.18f, s * 0.22f), s * 0.12f, fill, 10);
                break;

            case SecurityFeatureKind.CodeIntegrity:
                // Badge with check
                dl.AddNgonFilled(c, s * 0.7f, accent, 6);
                var a = c + new Vector2(-s * 0.28f, 0.02f * s);
                var b = c + new Vector2(-s * 0.05f, s * 0.28f);
                var d = c + new Vector2(s * 0.32f, -s * 0.22f);
                dl.AddLine(a, b, fill, 2.4f);
                dl.AddLine(b, d, fill, 2.4f);
                break;

            case SecurityFeatureKind.VulnerableDrivers:
                // Ban / shield-off
                dl.AddCircle(c, s * 0.62f, accent, 24, 2.4f);
                dl.AddLine(c + new Vector2(-s * 0.42f, -s * 0.42f), c + new Vector2(s * 0.42f, s * 0.42f), accent, 2.6f);
                break;
        }

        ImGui.Dummy(new Vector2(size + 8f, size));
    }

    private static void DrawShield(ImDrawListPtr dl, Vector2 c, float s, uint col)
    {
        // Simple shield polygon
        dl.PathClear();
        dl.PathLineTo(c + new Vector2(0, -s * 0.75f));
        dl.PathLineTo(c + new Vector2(s * 0.65f, -s * 0.35f));
        dl.PathLineTo(c + new Vector2(s * 0.55f, s * 0.25f));
        dl.PathLineTo(c + new Vector2(0, s * 0.75f));
        dl.PathLineTo(c + new Vector2(-s * 0.55f, s * 0.25f));
        dl.PathLineTo(c + new Vector2(-s * 0.65f, -s * 0.35f));
        dl.PathFillConvex(col);
    }
}
