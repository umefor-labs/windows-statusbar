using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinStatusBar;

/// <summary>Loads an application's own icon from its .exe.</summary>
internal static class ExeIcon
{
    public static ImageSource? Load(string? exePath)
    {
        if (string.IsNullOrEmpty(exePath)) return null;
        var large = new IntPtr[1];
        try
        {
            if (ExtractIconEx(exePath, 0, large, null, 1) == 0 || large[0] == IntPtr.Zero) return null;
            var src = Imaging.CreateBitmapSourceFromHIcon(large[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (large[0] != IntPtr.Zero) DestroyIcon(large[0]);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, IntPtr[]? large, IntPtr[]? small, uint count);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr h);
}
