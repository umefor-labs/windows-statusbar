using System.Windows;
using Microsoft.Web.WebView2.Wpf;

namespace WinStatusBar;

/// <summary>Browser window where the user signs in to a service; cookies are shared with UsageService.</summary>
internal sealed class LoginWindow : Window
{
    public LoginWindow(UsageProvider provider)
    {
        Title = $"Đăng nhập {provider.Name} — đóng cửa sổ khi đã đăng nhập xong";
        Width = 1000;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;

        var web = new WebView2();
        Content = web;
        Loaded += async (_, _) =>
        {
            var env = await UsageService.GetEnvironmentAsync();
            await web.EnsureCoreWebView2Async(env);
            web.CoreWebView2.Navigate(provider.LoginUrl);
        };
    }
}
