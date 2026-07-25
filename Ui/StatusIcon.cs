using System.Numerics;
using ImGuiNET;

namespace TcgBootLog.Ui;

/// <summary>
/// Draws a filled check / cross icon with the draw list so we do not depend on font glyphs
/// (Cascadia Code often renders ✔ as "?").
/// </summary>
public static class StatusIcon
{
    public static void Draw(bool ok, float size = 0f)
    {
        if (size <= 0) size = ImGui.GetTextLineHeight() * 1.15f;

        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var center = new Vector2(p.X + size * 0.5f, p.Y + size * 0.5f);
        float r = size * 0.42f;

        uint fill = ImGui.ColorConvertFloat4ToU32(ok ? Theme.Ok : Theme.Danger);
        uint ink = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.95f));

        dl.AddCircleFilled(center, r, fill, 24);

        if (ok)
        {
            // Checkmark
            var a = center + new Vector2(-r * 0.45f, 0.05f * r);
            var b = center + new Vector2(-r * 0.08f, r * 0.40f);
            var c = center + new Vector2(r * 0.48f, -r * 0.35f);
            dl.AddLine(a, b, ink, 2.6f);
            dl.AddLine(b, c, ink, 2.6f);
        }
        else
        {
            // Cross
            float o = r * 0.32f;
            dl.AddLine(center + new Vector2(-o, -o), center + new Vector2(o, o), ink, 2.6f);
            dl.AddLine(center + new Vector2(o, -o), center + new Vector2(-o, o), ink, 2.6f);
        }

        ImGui.Dummy(new Vector2(size + 6f, size));
    }
}
