using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace WinStatusBar;

public enum BarEdge { Top = 1, Bottom = 3 }

/// <summary>
/// Registers a WPF window as a Windows AppBar (like a second taskbar):
/// the shell reserves the strip, so maximized windows stop above/below it.
/// </summary>
internal sealed class AppBar : IDisposable
{
    public AppBar(Window window, double heightDip, BarEdge edge)
    {
        _window = window;
        _heightDip = heightDip;
        Edge = edge;

        _hwnd = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(_hwnd);
        _source.AddHook(WndProc);

        // Hide from Alt+Tab.
        int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
        SetWindowLong(_hwnd, GWL_EXSTYLE, (ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW);

        _callbackMsg = RegisterWindowMessage("WinStatusBar.AppBarCallback");
        var abd = NewData();
        abd.uCallbackMessage = _callbackMsg;
        SHAppBarMessage(ABM_NEW, ref abd);
        _registered = true;

        Reposition();
    }

    public BarEdge Edge { get; private set; }

    public void SetEdge(BarEdge edge)
    {
        Edge = edge;
        Reposition();
    }

    public void Reposition()
    {
        if (!_registered) return;

        // Primary monitor, in physical pixels.
        IntPtr mon = MonitorFromPoint(new POINT(), MONITOR_DEFAULTTOPRIMARY);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(mon, ref mi);

        double scale = VisualTreeHelper.GetDpi(_window).DpiScaleY;
        int height = (int)Math.Round(_heightDip * scale);

        var abd = NewData();
        abd.uEdge = (uint)Edge;
        // Use the work area's left/right so a vertical taskbar (left or right) stays uncovered.
        abd.rc.left = mi.rcWork.left;
        abd.rc.right = mi.rcWork.right;
        if (Edge == BarEdge.Bottom)
        {
            abd.rc.bottom = mi.rcMonitor.bottom;
            abd.rc.top = abd.rc.bottom - height;
        }
        else
        {
            abd.rc.top = mi.rcMonitor.top;
            abd.rc.bottom = abd.rc.top + height;
        }

        SHAppBarMessage(ABM_QUERYPOS, ref abd);
        // QUERYPOS may shift us past other bars on the same edge; restore our height.
        if (Edge == BarEdge.Bottom) abd.rc.top = abd.rc.bottom - height;
        else abd.rc.bottom = abd.rc.top + height;
        SHAppBarMessage(ABM_SETPOS, ref abd);

        MoveWindow(_hwnd, abd.rc.left, abd.rc.top,
            abd.rc.right - abd.rc.left, abd.rc.bottom - abd.rc.top, true);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == _callbackMsg)
        {
            switch (wParam.ToInt32())
            {
                case ABN_POSCHANGED:
                    _window.Dispatcher.BeginInvoke(new Action(Reposition));
                    break;
                case ABN_FULLSCREENAPP:
                    // Let full-screen games/videos cover the bar; come back afterwards.
                    _window.Topmost = lParam == IntPtr.Zero;
                    break;
            }
            handled = true;
        }
        else if (msg == WM_ACTIVATE)
        {
            var abd = NewData();
            SHAppBarMessage(ABM_ACTIVATE, ref abd);
        }
        else if (msg == WM_WINDOWPOSCHANGED)
        {
            var abd = NewData();
            SHAppBarMessage(ABM_WINDOWPOSCHANGED, ref abd);
        }
        else if (msg == WM_DISPLAYCHANGE || msg == WM_DPICHANGED || msg == WM_SETTINGCHANGE)
        {
            _window.Dispatcher.BeginInvoke(new Action(Reposition));
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (!_registered) return;
        _registered = false;
        var abd = NewData();
        SHAppBarMessage(ABM_REMOVE, ref abd);
        _source.RemoveHook(WndProc);
    }

    private APPBARDATA NewData() => new() { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = _hwnd };

    private readonly Window _window;
    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;
    private readonly double _heightDip;
    private readonly int _callbackMsg;
    private bool _registered;

    // ---- Win32 ----
    private const int ABM_NEW = 0, ABM_REMOVE = 1, ABM_QUERYPOS = 2, ABM_SETPOS = 3,
        ABM_ACTIVATE = 6, ABM_WINDOWPOSCHANGED = 9;
    private const int ABN_POSCHANGED = 1, ABN_FULLSCREENAPP = 2;
    private const int WM_ACTIVATE = 0x0006, WM_WINDOWPOSCHANGED = 0x0047, WM_SETTINGCHANGE = 0x001A,
        WM_DISPLAYCHANGE = 0x007E, WM_DPICHANGED = 0x02E0;
    private const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000;
    private const uint MONITOR_DEFAULTTOPRIMARY = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("shell32.dll")]
    private static extern IntPtr SHAppBarMessage(int dwMessage, ref APPBARDATA pData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string msg);

    [DllImport("user32.dll")]
    private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int w, int h, bool repaint);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO mi);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int index, int value);
}
