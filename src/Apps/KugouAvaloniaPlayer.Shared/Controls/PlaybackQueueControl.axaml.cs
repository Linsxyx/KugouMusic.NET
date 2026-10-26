using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using KuGou.Net.Abstractions.Models;
using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Controls;

public partial class PlaybackQueueControl : UserControl
{
    public PlaybackQueueControl()
    {
        AddToNextCommand = new RelayCommand<SongItem?>(AddToNext);
        ViewSingerCommand = new RelayCommand<SingerLite?>(ViewSinger);
        ShowPlaylistDialogCommand = new AsyncRelayCommand<SongItem?>(ShowPlaylistDialogAsync);
        InitializeComponent();
    }

    public ICommand AddToNextCommand { get; }
    public ICommand ViewSingerCommand { get; }
    public ICommand ShowPlaylistDialogCommand { get; }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private void AddToNext(SongItem? song)
    {
        if (song != null)
            ((IPlaybackCommands?)ViewModel?.Player)?.AddToNext(song);
    }

    private void ViewSinger(SingerLite? singer)
    {
        if (singer != null)
            ViewModel?.SongInteractions.NavigateToSinger(singer);
    }

    private async Task ShowPlaylistDialogAsync(SongItem? song)
    {
        if (song != null && ViewModel is { } viewModel)
            await viewModel.SongInteractions.ShowAddToPlaylistDialogAsync(song);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        TopLevel.GetTopLevel(this)?.AddHandler(
            PointerPressedEvent,
            OnTopLevelPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        TopLevel.GetTopLevel(this)?.RemoveHandler(
            PointerPressedEvent,
            OnTopLevelPointerPressed);

        base.OnDetachedFromVisualTree(e);
    }

    private void OnTopLevelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        foreach (var visual in this.GetVisualDescendants())
        {
            if (visual is Control { ContextFlyout: PopupFlyoutBase contextFlyout })
                contextFlyout.Hide();
        }
    }
}
