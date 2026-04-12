using Avalonia.Controls;
using Avalonia.Input;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Controls;

public partial class SongListItemControl : UserControl
{
    public SongListItemControl()
    {
        InitializeComponent();
    }

    private void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is SongItem songItem && songItem.PlayCommand.CanExecute(null))
        {
            songItem.PlayCommand.Execute(null);
            e.Handled = true;
        }
    }
}
