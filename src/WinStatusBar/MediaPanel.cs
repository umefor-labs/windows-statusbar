using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Windows.Media.Control;

namespace WinStatusBar;

/// <summary>Center panel: now playing + prev / play-pause / next for whatever app is playing.</summary>
internal sealed class MediaPanel : StackPanel
{
    private const string IconFont = "Segoe Fluent Icons, Segoe MDL2 Assets";
    private const string IconPrev = "", IconPlay = "", IconPause = "", IconNext = "";

    public MediaPanel()
    {
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        Visibility = Visibility.Collapsed;

        _prev = MakeButton(IconPrev, async () => await (_session?.TrySkipPreviousAsync()).AsTaskOrDone());
        _play = MakeButton(IconPlay, async () => await (_session?.TryTogglePlayPauseAsync()).AsTaskOrDone());
        _next = MakeButton(IconNext, async () => await (_session?.TrySkipNextAsync()).AsTaskOrDone());
        _title = new TextBlock
        {
            MaxWidth = 220,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };

        Children.Add(_prev);
        Children.Add(_play);
        Children.Add(_next);
        Children.Add(_title);

        Loaded += async (_, _) => await InitAsync();
    }

    private async Task InitAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += (_, _) => Dispatcher.Invoke(AttachCurrent);
            AttachCurrent();
        }
        catch
        {
            // Media controls unavailable on this system; panel stays hidden.
        }
    }

    private void AttachCurrent()
    {
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnMediaChanged;
            _session.PlaybackInfoChanged -= OnPlaybackChanged;
        }
        _session = _manager?.GetCurrentSession();
        if (_session != null)
        {
            _session.MediaPropertiesChanged += OnMediaChanged;
            _session.PlaybackInfoChanged += OnPlaybackChanged;
        }
        _ = RefreshAsync();
    }

    private void OnMediaChanged(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e)
        => Dispatcher.InvokeAsync(RefreshAsync);

    private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e)
        => Dispatcher.InvokeAsync(RefreshAsync);

    private async Task RefreshAsync()
    {
        var session = _session;
        if (session == null)
        {
            Visibility = Visibility.Collapsed;
            return;
        }
        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            var info = session.GetPlaybackInfo();
            string title = props?.Title ?? "";
            string artist = props?.Artist ?? "";
            _title.Text = string.IsNullOrEmpty(artist) ? title : $"{title} · {artist}";
            _title.ToolTip = _title.Text;
            bool playing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            ((TextBlock)_play.Content).Text = playing ? IconPause : IconPlay;
            _prev.IsEnabled = info.Controls.IsPreviousEnabled;
            _next.IsEnabled = info.Controls.IsNextEnabled;
            Visibility = string.IsNullOrEmpty(title) ? Visibility.Collapsed : Visibility.Visible;
        }
        catch
        {
            Visibility = Visibility.Collapsed;
        }
    }

    private Button MakeButton(string glyph, Func<Task> onClick)
    {
        var text = new TextBlock { Text = glyph, FontFamily = new FontFamily(IconFont), FontSize = 11 };
        var b = new Button
        {
            Content = text,
            Padding = new Thickness(5, 2, 5, 2),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Focusable = false,
            VerticalAlignment = VerticalAlignment.Center,
        };
        b.SetResourceReference(Control.ForegroundProperty, "BarText");
        b.Click += async (_, _) =>
        {
            try { await onClick(); } catch { }
        };
        return b;
    }

    private readonly Button _prev, _play, _next;
    private readonly TextBlock _title;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
}

internal static class AsyncOpExtensions
{
    public static Task AsTaskOrDone(this global::Windows.Foundation.IAsyncOperation<bool>? op)
        => op == null ? Task.CompletedTask : System.WindowsRuntimeSystemExtensions.AsTask(op);
}
