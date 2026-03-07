using Avalonia.Controls;
using Avalonia.Interactivity;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Views;

public partial class RankView : UserControl
{
    public RankView()
    {
        InitializeComponent();
    }

    private void OnRankCardClick(object? sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        if (button?.DataContext is not RankItem item) return;

        if (DataContext is not RankViewModel vm) return;
        vm.OpenRankCommand.Execute(item);
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer) return;
        if (DataContext is not RankViewModel vm) return;

        var currentBottom = scrollViewer.Offset.Y + scrollViewer.Viewport.Height;

        if (currentBottom >= scrollViewer.Extent.Height - 50)
            if (vm.LoadMoreCommand.CanExecute(null))
                vm.LoadMoreCommand.Execute(null);
    }
}