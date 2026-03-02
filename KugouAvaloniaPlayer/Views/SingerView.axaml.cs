using Avalonia.Controls;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Views;

public partial class SingerView : UserControl
{
    public SingerView()
    {
        InitializeComponent();
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer) return;
        if (DataContext is not SingerViewModel vm) return;
        var currentBottom = scrollViewer.Offset.Y + scrollViewer.Viewport.Height;
        if (currentBottom >= scrollViewer.Extent.Height - 50)
            // 如果 ViewModel 有加载更多的命令，则执行
            if (vm.LoadMoreCommand.CanExecute(null))
                vm.LoadMoreCommand.Execute(null);
    }
}