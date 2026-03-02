using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Views;

public partial class SearchView : UserControl
{
    public SearchView()
    {
        InitializeComponent();
    }
    
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
    
    private void OnDetailScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer) return;
        if (DataContext is not SearchViewModel vm) return;
        
        var currentBottom = scrollViewer.Offset.Y + scrollViewer.Viewport.Height;
        
        if (currentBottom >= scrollViewer.Extent.Height - 50)
        {
            if (vm.LoadMoreDetailsCommand.CanExecute(null))
            {
                vm.LoadMoreDetailsCommand.Execute(null);
            }
        }
    }
}