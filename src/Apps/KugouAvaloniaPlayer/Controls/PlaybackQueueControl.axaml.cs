using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using KuGou.Net.Abstractions.Models;
using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.ViewModels;
using Vela.Avalonia.Controls;

namespace KugouAvaloniaPlayer.Controls;

public partial class PlaybackQueueControl : UserControl
{
    private VelaContextMenu? _queueItemMenu;

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
        {
            this.FindAncestorOfType<VelaDrawerHost>()?.Close();
            ViewModel?.SongInteractions.NavigateToSinger(singer);
        }
    }

    private async Task ShowPlaylistDialogAsync(SongItem? song)
    {
        if (song != null && ViewModel is { } viewModel)
            await viewModel.SongInteractions.ShowAddToPlaylistDialogAsync(song);
    }

    private void QueueItemRoot_OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control target || target.DataContext is not SongItem song)
            return;

        _queueItemMenu ??= new VelaContextMenu { MenuMinWidth = 188 };
        _queueItemMenu.Items.Clear();

        _queueItemMenu.Items.Add(new VelaMenuItem
        {
            Header = "下一首播放",
            Icon = KugouIcons.QueueNext,
            Command = AddToNextCommand,
            CommandParameter = song
        });

        if (song.Singers.Count == 1)
        {
            var only = song.Singers[0];
            var name = string.IsNullOrWhiteSpace(only.Name) ? "未知歌手" : only.Name;
            _queueItemMenu.Items.Add(new VelaMenuItem
            {
                Header = $"查看歌手：{name}",
                Icon = KugouIcons.Singer,
                Command = ViewSingerCommand,
                CommandParameter = only
            });
        }
        else if (song.Singers.Count > 1)
        {
            var singerParent = new VelaMenuItem
            {
                Header = "查看歌手",
                Icon = KugouIcons.Singer
            };
            foreach (var singer in song.Singers)
            {
                singerParent.Items.Add(new VelaMenuItem
                {
                    Header = string.IsNullOrWhiteSpace(singer.Name) ? "未知歌手" : singer.Name,
                    Icon = KugouIcons.Singer,
                    Command = ViewSingerCommand,
                    CommandParameter = singer
                });
            }
            _queueItemMenu.Items.Add(singerParent);
        }

        _queueItemMenu.Items.Add(new VelaMenuItem
        {
            Header = "添加到歌单",
            Icon = KugouIcons.PlaylistAdd,
            Command = ShowPlaylistDialogCommand,
            CommandParameter = song
        });

        _queueItemMenu.Items.Add(new VelaMenuItem { IsSeparator = true });
        _queueItemMenu.Items.Add(new VelaMenuItem
        {
            Header = "从队列移除",
            Icon = KugouIcons.Trash,
            IsDestructive = true,
            Command = ViewModel?.Player.RemoveFromQueueCommand,
            CommandParameter = song
        });

        _queueItemMenu.ShowAt(target, showAtPointer: e.TryGetPosition(target, out _));
        e.Handled = true;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _queueItemMenu?.Close();
        _queueItemMenu?.Items.Clear();
        _queueItemMenu = null;
        base.OnDetachedFromVisualTree(e);
    }
}
