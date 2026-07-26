using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace PrinterTool;

/// <summary>
/// Windows 11 look & feel: follows the system App theme (light/dark) and the
/// user's accent color, switches live when Windows settings change, and turns
/// the native title bar dark via DWM.
/// </summary>
public static class ThemeManager
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public static bool IsDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    public static Color SystemAccent()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (k?.GetValue("ColorizationColor") is int c)
                return Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c);
        }
        catch { }
        return Color.FromRgb(0x00, 0x67, 0xC0);   // Win11 default blue
    }

    public static void Apply(bool dark)
    {
        var r = Application.Current.Resources;
        void Set(string key, Color c) { var b = new SolidColorBrush(c); b.Freeze(); r[key] = b; }

        Color accent = SystemAccent();
        // In dark mode Windows lightens the accent for text/controls — approximate that
        Color accentFg = dark ? Lighten(accent, 0.30) : accent;
        Color accentHover = dark ? Lighten(accent, 0.42) : Darken(accent, 0.12);

        if (dark)
        {
            Set("Paper", Color.FromRgb(0x20, 0x20, 0x20));   // Win11 dark window
            Set("Card", Color.FromRgb(0x2B, 0x2B, 0x2B));    // dark card surface
            Set("Ink", Color.FromRgb(0xFF, 0xFF, 0xFF));
            Set("InkSoft", Color.FromRgb(0xA6, 0xA6, 0xA6));
            Set("Line", Color.FromRgb(0x3A, 0x3A, 0x3A));
            Set("Hover", Color.FromRgb(0x33, 0x33, 0x33));
            Set("TonerTrack", Color.FromRgb(0x40, 0x40, 0x40));
            Set("TonerK", Color.FromRgb(0xC9, 0xCD, 0xD2));  // "black" toner readable on dark
            Set("Hint", Color.FromRgb(0x77, 0x77, 0x77));
            Set("PinnedBg", Color.FromRgb(0x2C, 0x31, 0x38));
            Set("DefaultRowBg", Color.FromRgb(0x1F, 0x33, 0x4A));
            Set("Danger", Color.FromRgb(0xD8, 0x4A, 0x3E));
            Set("DangerHover", Color.FromRgb(0xE8, 0x62, 0x56));
            r["DangerSoft"] = Freeze(new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0x77, 0x6B)));
        }
        else
        {
            Set("Paper", Color.FromRgb(0xF3, 0xF3, 0xF3));   // Win11 light window
            Set("Card", Color.FromRgb(0xFF, 0xFF, 0xFF));
            Set("Ink", Color.FromRgb(0x1B, 0x1B, 0x1B));
            Set("InkSoft", Color.FromRgb(0x5D, 0x5D, 0x5D));
            Set("Line", Color.FromRgb(0xE5, 0xE5, 0xE5));
            Set("Hover", Color.FromRgb(0xF2, 0xF5, 0xF9));
            Set("TonerTrack", Color.FromRgb(0xED, 0xEF, 0xF3));
            Set("TonerK", Color.FromRgb(0x30, 0x33, 0x38));
            Set("Hint", Color.FromRgb(0x9A, 0xA1, 0xAC));
            Set("PinnedBg", Color.FromRgb(0xF4, 0xF9, 0xFE));
            Set("DefaultRowBg", Color.FromRgb(0xDC, 0xE8, 0xF7));
            Set("Danger", Color.FromRgb(0xC4, 0x2B, 0x1C));
            Set("DangerHover", Color.FromRgb(0xA5, 0x23, 0x1A));
            r["DangerSoft"] = Freeze(new SolidColorBrush(Color.FromArgb(0x1E, 0xC4, 0x2B, 0x1C)));
        }

        Set("Accent", accentFg);
        Set("AccentHover", accentHover);
        r["AccentSoft"] = Freeze(new SolidColorBrush(Color.FromArgb(dark ? (byte)0x33 : (byte)0x1E,
            accentFg.R, accentFg.G, accentFg.B)));
    }

    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    /// <summary>
    /// Paints the native titlebar in the window's own background colour so the frame
    /// blends seamlessly with the content, exactly like Windows 11's inbox apps.
    /// The colour attributes exist from Windows 11 22000; on older builds the calls
    /// fail harmlessly and the classic dark/light toggle still applies.
    /// </summary>
    public static void ApplyTitlebar(Window w, bool dark)
    {
        var h = new WindowInteropHelper(w).Handle;
        if (h == IntPtr.Zero) return;

        int v = dark ? 1 : 0;
        DwmSetWindowAttribute(h, DWMWA_USE_IMMERSIVE_DARK_MODE, ref v, sizeof(int));

        if (Application.Current.Resources["Paper"] is SolidColorBrush paper)
        {
            int caption = ToColorRef(paper.Color);
            DwmSetWindowAttribute(h, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
            DwmSetWindowAttribute(h, DWMWA_BORDER_COLOR, ref caption, sizeof(int));
        }
        if (Application.Current.Resources["Paper"] is SolidColorBrush paper2)
        {
            // Caption text in the caption's own color = invisible in the frame.
            // The app name lives once, in the content header; taskbar and Alt-Tab
            // still show title and icon normally.
            int text = ToColorRef(paper2.Color);
            DwmSetWindowAttribute(h, DWMWA_TEXT_COLOR, ref text, sizeof(int));
        }
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_DLGMODALFRAME = 0x0001;
    private const int SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_FRAMECHANGED = 0x0020;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int index, int value);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    /// <summary>Removes the small icon from the window frame (brand stays in the content).</summary>
    public static void HideCaptionIcon(Window w)
    {
        var h = new WindowInteropHelper(w).Handle;
        if (h == IntPtr.Zero) return;
        SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) | WS_EX_DLGMODALFRAME);
        SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER | SWP_FRAMECHANGED);
    }

    /// <summary>WPF Color → Win32 COLORREF (0x00BBGGRR).</summary>
    private static int ToColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

    private static Brush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

    private static Color Lighten(Color c, double f) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * f), (byte)(c.G + (255 - c.G) * f), (byte)(c.B + (255 - c.B) * f));

    private static Color Darken(Color c, double f) => Color.FromRgb(
        (byte)(c.R * (1 - f)), (byte)(c.G * (1 - f)), (byte)(c.B * (1 - f)));
}
