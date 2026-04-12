using Avalonia.Controls;
using Avalonia.Input;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Controls;

public partial class PlaybackQueueControl : UserControl
{
    public PlaybackQueueControl()
    {
        InitializeComponent();
    }

    private void OnQueueItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: SongItem songItem }) return;
        if (!songItem.PlayCommand.CanExecute(null)) return;

        songItem.PlayCommand.Execute(null);
        e.Handled = true;
    }
}
