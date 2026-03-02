using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace KugouAvaloniaPlayer.Views;

public partial class DesktopLyricWindow : Window
{
    public DesktopLyricWindow()
    {
        InitializeComponent();
    }

    // 实现拖拽功能
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginMoveDrag(e);
    }

    // 关闭窗口
    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}