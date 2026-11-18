using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;

namespace WinStatusBar;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--sensors"))
        {
            // Headless elevated helper: publishes CPU temperature for the bar.
            _mutex = new Mutex(true, "WinStatusBar.Sensors", out bool first);
            if (!first)
            {
                Shutdown();
                return;
            }
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            new Thread(SensorHost.Run) { IsBackground = true, Name = "sensors" }.Start();
            return;
        }

        // Only one bar at a time: a second instance would reserve a second strip.
        _mutex = new Mutex(true, "WinStatusBar.SingleInstance", out bool isNew);
        if (!isNew && e.Args.Contains("--replace"))
            isNew = ReplaceRunningInstance();
        if (!isNew)
        {
            Shutdown();
            return;
        }

        Theme.Apply(Resources);
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        base.OnStartup(e);

        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    /// <summary>Ask the running bar to close (so it releases its screen strip), then take over.</summary>
    private bool ReplaceRunningInstance()
    {
        IntPtr hwnd = FindWindow(null, "WinStatusBar");
        if (hwnd != IntPtr.Zero) PostMessage(hwnd, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);

        foreach (var p in Process.GetProcessesByName("WinStatusBar"))
        {
            if (p.Id == Environment.ProcessId) continue;
            try
            {
                if (!p.WaitForExit(5000)) p.Kill();
            }
            catch
            {
                // Different elevation level: cannot touch it.
            }
        }

        try
        {
            return _mutex!.WaitOne(3000);
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // Light/dark switches arrive as the General category.
        if (e.Category == UserPreferenceCategory.General)
            Dispatcher.Invoke(() => Theme.Apply(Resources));
    }

    private Mutex? _mutex;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? cls, string title);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
