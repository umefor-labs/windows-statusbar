using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WinStatusBar;

/// <summary>One provider on the left: logo + two stacked bars (top = 5-hour window, bottom = 7-day window).</summary>
internal sealed class UsageMeter : StackPanel
{
    private const double BarWidth = 48, BarHeight = 4, IconSize = 14;

    /// <param name="brandColor">Hex color, or null to follow the bar's text color (light/dark).</param>
    public UsageMeter(string name, string iconPath, string? brandColor)
        : this(name, MakeIcon(name, iconPath, brandColor)) { }

    public UsageMeter(string name, ImageSource image)
        : this(name, MakeImage(image)) { }

    private UsageMeter(string name, FrameworkElement icon)
    {
        _name = name;
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        Margin = new Thickness(0, 0, 14, 0);
        Background = Brushes.Transparent; // hit-testable for clicks
        Cursor = System.Windows.Input.Cursors.Hand;

        Children.Add(icon);

        var bars = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        (_trackShort, _fillShort) = MakeBar();
        (_trackLong, _fillLong) = MakeBar();
        _trackLong.Margin = new Thickness(0, 3, 0, 0);
        bars.Children.Add(_trackShort);
        bars.Children.Add(_trackLong);
        Children.Add(bars);

        Set(null, null);
    }

    /// <summary>Percent used (0-100) or null when unknown / not signed in.</summary>
    public void Set(double? fiveHour, double? sevenDay, string? fiveHourReset = null, string? sevenDayReset = null)
    {
        Apply(_fillShort, fiveHour);
        Apply(_fillLong, sevenDay);
        ToolTip = $"{_name}\n5 giờ: {Fmt(fiveHour)}{Reset(fiveHourReset)}\n7 ngày: {Fmt(sevenDay)}{Reset(sevenDayReset)}";
    }

    public void Show(UsageResult r)
    {
        if (!r.SignedIn)
        {
            Set(null, null);
            ToolTip = $"{_name}\nChưa đăng nhập. Bấm để đăng nhập.";
            return;
        }
        Set(r.Short, r.Long, When(r.ShortReset), When(r.LongReset));
        if (r.Short == null && r.Long == null && r.Error != null)
            ToolTip += $"\nLỗi: {r.Error}";
    }

    private static string? When(DateTimeOffset? t)
    {
        if (t is not DateTimeOffset v) return null;
        var local = v.ToLocalTime();
        return local - DateTimeOffset.Now < TimeSpan.FromHours(24)
            ? local.ToString("HH:mm")
            : local.ToString("dd/MM HH:mm");
    }

    private static FrameworkElement MakeImage(ImageSource src)
    {
        var img = new Image
        {
            Source = src, Width = IconSize, Height = IconSize,
            Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        return img;
    }

    private static FrameworkElement MakeIcon(string name, string data, string? color)
    {
        var margin = new Thickness(0, 0, 6, 0);
        try
        {
            var path = new Path
            {
                Data = Geometry.Parse(data),
                Stretch = Stretch.Uniform,
                Width = IconSize,
                Height = IconSize,
                Margin = margin,
                VerticalAlignment = VerticalAlignment.Center,
            };
            if (color != null) path.Fill = (Brush)new BrushConverter().ConvertFromString(color)!;
            else path.SetResourceReference(Shape.FillProperty, "BarText");
            return path;
        }
        catch
        {
            // Bad path data: fall back to the name.
            var t = new TextBlock { Text = name, Margin = margin, VerticalAlignment = VerticalAlignment.Center };
            t.SetResourceReference(TextBlock.ForegroundProperty, "BarMuted");
            return t;
        }
    }

    private static string Fmt(double? p) => p is double v ? $"{v:0}%" : "chưa có dữ liệu";
    private static string Reset(string? r) => string.IsNullOrEmpty(r) ? "" : $" (reset {r})";

    private static void Apply(Border fill, double? pct)
    {
        double p = Math.Clamp(pct ?? 0, 0, 100);
        fill.Width = BarWidth * p / 100;
        fill.Background = new SolidColorBrush(p >= 80 ? Color.FromRgb(0xE5, 0x48, 0x4D)
                                            : p >= 50 ? Color.FromRgb(0xF0, 0xA0, 0x20)
                                            : Color.FromRgb(0x3C, 0xB3, 0x71));
    }

    private static (Border track, Border fill) MakeBar()
    {
        var fill = new Border { HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(2) };
        var track = new Border { Width = BarWidth, Height = BarHeight, CornerRadius = new CornerRadius(2), Child = fill };
        track.SetResourceReference(Border.BackgroundProperty, "BarBorder");
        return (track, fill);
    }

    private readonly string _name;
    private readonly Border _trackShort, _fillShort, _trackLong, _fillLong;
}
