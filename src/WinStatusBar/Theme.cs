using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace WinStatusBar;

/// <summary>Follows the Windows mode (the setting that colors the taskbar).</summary>
internal static class Theme
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool IsLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Key);
        return key?.GetValue("SystemUsesLightTheme") is int v && v != 0;
    }

    public static void Apply(ResourceDictionary res)
    {
        bool light = IsLight();
        Set(res, "BarBackground", light ? "#F3F3F3" : "#202020");
        Set(res, "BarBorder", light ? "#D6D6D6" : "#3A3A3A");
        Set(res, "BarText", light ? "#1A1A1A" : "#F0F0F0");
        Set(res, "BarMuted", light ? "#6B6B6B" : "#9A9A9A");
    }

    private static void Set(ResourceDictionary res, string key, string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        res[key] = brush;
    }
}
