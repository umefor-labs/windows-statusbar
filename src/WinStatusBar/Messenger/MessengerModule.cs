using System.Windows;
using System.Windows.Controls;
using MessengerBubbles.Core;
using Microsoft.Web.WebView2.Core;

namespace MessengerBubbles;

/// <summary>
/// Messenger Bubbles running inside the status bar: the same watcher, chat panel and rules as the standalone
/// app (Program.StartAsync), with the avatar strip in place of floating bubbles and a bar menu in place of the tray.
/// Uses the standalone app's folder %LOCALAPPDATA%\MessengerBubbles, so an existing Facebook login carries over.
/// </summary>
sealed class MessengerModule : IDisposable
{
    // Same name as the standalone app: holding it keeps the two from showing every message twice.
    const string MutexName = "MessengerBubbles.SingleInstance";

    readonly Mutex? _mutex;
    readonly Settings _settings;
    readonly Watcher? _watcher;
    readonly ChatPanel? _panel;
    readonly GlobalHotkey? _hotkey;

    public MessengerStrip Strip { get; }

    /// <summary>False when the standalone Messenger Bubbles is already running; the strip then only says so.</summary>
    public bool Active => _watcher != null;

    public MessengerModule()
    {
        Texts.Current = Texts.Vietnamese;
        _settings = Settings.Load(AppPaths.SettingsFile);

        var mutex = new Mutex(true, MutexName, out var first);
        if (!first)
        {
            mutex.Dispose();
            Strip = new MessengerStrip(_settings, _ => Task.FromResult<byte[]?>(null), _ => Task.FromResult(false), () => null);
            Strip.Notice("Messenger Bubbles bản riêng đang chạy. Thoát nó rồi mở lại thanh để dùng chat trên thanh.");
            return;
        }
        _mutex = mutex;

        _watcher = new Watcher();
        _panel = new ChatPanel(_settings);
        Strip = new MessengerStrip(_settings, _watcher.GetLoadedResourceAsync, _panel.IsShowingAsync, () => _panel.ScreenBox);
        _hotkey = new GlobalHotkey();
        _hotkey.Pressed += Strip.ToggleHidden;
        if (!_hotkey.Registered) AppPaths.Log("hotkey taken");

        _watcher.Scanned += Strip.Apply;
        _watcher.NeedsLogin += Strip.SetLogin;
        _watcher.HealthChanged += Strip.SetBroken;
        Strip.AvatarClicked += _panel.Toggle;
        _panel.LayoutBroken += () => Strip.Notice(Texts.Current.PanelBroken);
        _panel.LeftOnOtherThread += (from, path) =>
        {
            if (_watcher.FindRow(path) is { } row) Strip.SwitchTo(from, row);
        };
    }

    public async Task StartAsync()
    {
        if (_watcher == null || _panel == null) return;
        try
        {
            await _watcher.StartAsync(await AppPaths.CreateEnvironmentAsync());
            _panel.Preload();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            Strip.Notice(Texts.Current.NoRuntime);
        }
        catch (Exception e)
        {
            AppPaths.Log($"messenger start failed: {e.GetType().Name}");
            Strip.Notice("Không khởi động được Messenger. Xem %LOCALAPPDATA%\\MessengerBubbles\\app.log");
        }
    }

    /// <summary>The tray menu of the standalone app, as a submenu of the bar's right-click menu.</summary>
    public MenuItem BuildMenu()
    {
        var t = Texts.Current;
        var root = new MenuItem { Header = "Messenger" };
        if (_watcher == null)
        {
            root.Items.Add(new MenuItem { Header = "Đang tắt: Messenger Bubbles bản riêng đang chạy", IsEnabled = false });
            return root;
        }

        root.Items.Add(Item(t.OpenFacebook, _watcher.ShowWindow));
        var hide = new MenuItem { Header = "Ẩn avatar chat", InputGestureText = "Ctrl+Alt+M", IsCheckable = true };
        hide.Click += (_, _) => Strip.ToggleHidden();
        root.SubmenuOpened += (_, _) => hide.IsChecked = Strip.Hidden;
        root.Items.Add(hide);
        root.Items.Add(new Separator());
        root.Items.Add(Toggle(t.Preview, _settings.Preview, on => _settings.Preview = on));
        root.Items.Add(Toggle(t.Sound, _settings.Sound, on => _settings.Sound = on));
        root.Items.Add(SnoozeMenu());
        root.Items.Add(SizeMenu());
        root.Items.Add(Toggle(t.DisableGpu, _settings.DisableGpu, on => _settings.DisableGpu = on, t.RestartToApply));
        return root;

        MenuItem SnoozeMenu()
        {
            var parent = new MenuItem { Header = t.Snooze };
            var off = Item(t.SnoozeOff, () => SetSnooze(null));
            parent.Items.Add(Item(t.SnoozeHour, () => SetSnooze(Snooze.ForOneHour(DateTime.Now))));
            parent.Items.Add(Item(t.SnoozeMorning, () => SetSnooze(Snooze.UntilMorning(DateTime.Now))));
            parent.Items.Add(new Separator());
            parent.Items.Add(off);
            // The pause ends by itself, so the label is worked out each time the menu opens.
            root.SubmenuOpened += (_, _) =>
            {
                var on = _settings.IsSnoozed(DateTime.Now);
                parent.Header = on ? string.Format(t.SnoozedUntil, _settings.SnoozedUntil) : t.Snooze;
                parent.IsChecked = on;
                off.IsEnabled = on;
            };
            return parent;
        }

        MenuItem SizeMenu()
        {
            var parent = new MenuItem { Header = t.PanelSize };
            foreach (var (text, size) in new[] { (t.Small, PanelSize.Small), (t.Medium, PanelSize.Medium), (t.Large, PanelSize.Large) })
            {
                var item = new MenuItem { Header = text, IsChecked = _settings.PanelSize == size };
                item.Click += (_, _) =>
                {
                    foreach (var other in parent.Items.OfType<MenuItem>()) other.IsChecked = other == item;
                    _settings.PanelSize = size;
                    Save();
                };
                parent.Items.Add(item);
            }
            return parent;
        }
    }

    void SetSnooze(DateTime? until)
    {
        _settings.SnoozedUntil = until;
        Save();
        Strip.Refresh();
    }

    MenuItem Toggle(string text, bool state, Action<bool> apply, string? note = null)
    {
        var item = new MenuItem { Header = text, IsCheckable = true, IsChecked = state, ToolTip = note };
        item.Click += (_, _) =>
        {
            apply(item.IsChecked);
            Save();
            Strip.Refresh();
        };
        return item;
    }

    static MenuItem Item(string text, Action click)
    {
        var item = new MenuItem { Header = text };
        item.Click += (_, _) => click();
        return item;
    }

    void Save()
    {
        try
        {
            _settings.Save(AppPaths.SettingsFile);
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            AppPaths.Log($"settings not saved: {e.GetType().Name}");
        }
    }

    public void Dispose()
    {
        _hotkey?.Dispose();
        _watcher?.Exit();
        _panel?.Close();
        _mutex?.Dispose();
    }
}
