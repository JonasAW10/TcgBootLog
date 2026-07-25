using System.Runtime.InteropServices;
using Silk.NET.Core;
using Silk.NET.Windowing;
using StbImageSharp;

namespace TcgBootLog.Ui;

/// <summary>
/// Dark title bar (matches app Bg0) + window / taskbar icon.
/// </summary>
public static class WindowChrome
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    // COLORREF is 0x00BBGGRR — match Theme Bg0 #0E1218 and text #F4F7FB
    private const uint CaptionColorBgr = 0x0018120E;
    private const uint BorderColorBgr = 0x0018120E;
    private const uint TextColorBgr = 0x00FBF7F4;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref uint attrValue, int attrSize);

    public static void Apply(IWindow window)
    {
        ApplyDarkTitleBar(window);
        ApplyIcon(window);
    }

    public static void ApplyDarkTitleBar(IWindow window)
    {
        try
        {
            if (window.Native?.Win32 is not { } win32)
                return;

            IntPtr hwnd = (IntPtr)win32.Hwnd;
            if (hwnd == IntPtr.Zero) return;

            int dark = 1;
            // Windows 10 20H1+ uses attribute 20; older 19
            if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeBefore20H1, ref dark, sizeof(int));

            // Windows 11 custom caption / border colors
            uint caption = CaptionColorBgr;
            uint border = BorderColorBgr;
            uint text = TextColorBgr;
            _ = DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref caption, sizeof(uint));
            _ = DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref border, sizeof(uint));
            _ = DwmSetWindowAttribute(hwnd, DwmwaTextColor, ref text, sizeof(uint));
        }
        catch
        {
            // Non-fatal — window still works with default chrome
        }
    }

    public static void ApplyIcon(IWindow window)
    {
        try
        {
            string? path = FindIconPng();
            if (path == null) return;

            using var fs = File.OpenRead(path);
            var image = ImageResult.FromStream(fs, ColorComponents.RedGreenBlueAlpha);
            // Keep pixel buffer alive for the SetWindowIcon call
            var pixels = image.Data;
            var raw = new RawImage(image.Width, image.Height, pixels);
            window.SetWindowIcon(ref raw);
        }
        catch
        {
            // Non-fatal
        }
    }

    private static string? FindIconPng()
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "Assets", "tcgbootlog-icon.png"),
            Path.Combine(AppContext.BaseDirectory, "tcgbootlog-icon.png"),
            Path.Combine(Directory.GetCurrentDirectory(), "Assets", "tcgbootlog-icon.png"),
        ];
        return candidates.FirstOrDefault(File.Exists);
    }
}
