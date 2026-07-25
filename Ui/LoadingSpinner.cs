using System.Numerics;
using ImGuiNET;

namespace TcgBootLog.Ui;

public static class LoadingSpinner
{
    /// <summary>Draw a spinning arc (loading wheel) at the current cursor.</summary>
    public static void Draw(float radius = 10f, float thickness = 3f)
    {
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var center = pos + new Vector2(radius + 2f, radius + 2f);

        float t = (float)ImGui.GetTime();
        float start = t * 6.5f;
        float span = MathF.PI * 1.4f;

        uint col = ImGui.ColorConvertFloat4ToU32(Theme.AccentHover);
        uint dim = ImGui.ColorConvertFloat4ToU32(Theme.AccentSoft);

        // Dim full ring
        dl.AddCircle(center, radius, dim, 32, thickness * 0.7f);
        // Bright arc
        dl.PathClear();
        dl.PathArcTo(center, radius, start, start + span, 24);
        dl.PathStroke(col, ImDrawFlags.None, thickness);

        ImGui.Dummy(new Vector2((radius + 2f) * 2f + 6f, (radius + 2f) * 2f));
    }
}
