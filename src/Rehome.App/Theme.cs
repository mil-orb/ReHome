using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Rehome.Shell;

/// <summary>Matches the native title bar and window background to the page, so there's no seam or white flash.</summary>
static partial class Theme
{
    public static bool IsDark => Preferences.Current.Theme switch
    {
        "dark" => true,
        "light" => false,
        _ => SystemIsDark,
    };

    static bool SystemIsDark
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
    }

    // Same values as --bg in styles.css.
    public static Color Background => IsDark ? Color.FromArgb(0x16, 0x16, 0x18) : Color.FromArgb(0xFB, 0xFB, 0xFA);

    public static void ApplyTitleBar(nint hwnd)
    {
        var dark = IsDark ? 1 : 0;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

        // Windows 11 only. Older builds ignore it and keep the default caption.
        var bg = Background;
        var colorRef = bg.R | (bg.G << 8) | (bg.B << 16);
        _ = DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref colorRef, sizeof(int));
    }

    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    const int DWMWA_CAPTION_COLOR = 35;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
