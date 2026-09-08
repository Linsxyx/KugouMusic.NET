using System;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Layout;
using SukiUI.Toasts;

namespace KugouAvaloniaPlayer.Services;

/// <summary>
///     Toast 统一构建扩展：内容区带可点击的关闭按钮（小 ✕），
///     点击 ✕ 立即关闭，无需等待倒计时结束。
/// </summary>
public static class SukiToastExtensions
{
    public static void ShowDismissibleToast(
        this ISukiToastManager manager,
        NotificationType type,
        string title,
        string content,
        TimeSpan? dismissAfter = null)
    {
        // 闭包捕获 toast 引用：Queue() 返回 ISukiToast 后回填给关闭按钮的 Click 处理器
        ISukiToast? toastRef = null;

        var closeButton = new Button
        {
            Content = "✕",
            Padding = new Avalonia.Thickness(4, 0),
            Margin = new Avalonia.Thickness(8, -2, -4, 0),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = Avalonia.Media.Brushes.Transparent,
            BorderThickness = new Avalonia.Thickness(0),
            FontSize = 13,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        closeButton.Click += (_, _) =>
        {
            if (toastRef is { } toast)
                manager.Dismiss(toast, SukiToastDismissSource.Code);
        };

        var text = new TextBlock
        {
            Text = content,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto")
        };
        Grid.SetColumn(text, 0);
        Grid.SetColumn(closeButton, 1);
        grid.Children.Add(text);
        grid.Children.Add(closeButton);

        toastRef = manager.CreateToast()
            .OfType(type)
            .WithTitle(title)
            .WithContent(grid)
            .Dismiss()
            .After(dismissAfter ?? TimeSpan.FromSeconds(4))
            .Queue();
    }
}
