using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;

namespace KugouAvaloniaPlayer.Views;

/// <summary>A dedicated native surface keeps captcha overlays clear of other Avalonia controls.</summary>
public sealed class SecurityVerificationWindow : Window
{
    public SecurityVerificationWindow(Uri page)
    {
        Title = "KA Music · 安全验证";
        Width = 560;
        Height = 680;
        MinWidth = 420;
        MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var status = new TextBlock
        {
            Text = "正在加载系统 WebView…",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(12),
            VerticalAlignment = VerticalAlignment.Center
        };
        var webView = new NativeWebView { Source = page };
        webView.EnvironmentRequested += (_, args) =>
        {
            switch (args)
            {
                case WindowsWebView2EnvironmentRequestedEventArgs windows:
                    windows.UserDataFolder = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "KA Music", "VerificationWebView");
                    windows.IsInPrivateModeEnabled = true;
                    break;
                case AppleWKWebViewEnvironmentRequestedEventArgs apple:
                    apple.NonPersistentDataStore = true;
                    break;
                case GtkWebViewEnvironmentRequestedEventArgs gtk:
                    gtk.EphemeralDataManager = true;
                    break;
            }
        };
        webView.PropertyChanged += (_, args) =>
        {
            if (args.Property == NativeWebView.AdapterInfoProperty &&
                webView.AdapterInfo is { Type: WebViewAdapterType.Unknown })
                status.Text = "系统 WebView 不可用。Windows 请安装 WebView2 Runtime，Linux 请安装 WebKit 运行库。";
        };
        var close = new Button { Content = "取消验证", Margin = new Avalonia.Thickness(12) };
        close.Click += (_, _) => Close();
        webView.NavigationStarted += (_, e) =>
        {
            if (e.Request is { } target && target != page && target.AbsoluteUri != "about:blank")
                e.Cancel = true;
        };
        webView.NewWindowRequested += (_, e) => e.Handled = true;
        webView.NavigationCompleted += (_, e) =>
            status.Text = e.IsSuccess ? "请完成下方验证。关闭窗口可取消。" : "验证页加载失败，请关闭窗口后重试。";

        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        Grid.SetRow(webView, 1);
        Grid.SetRow(close, 2);
        layout.Children.Add(status);
        layout.Children.Add(webView);
        layout.Children.Add(close);
        Content = layout;
    }
}
