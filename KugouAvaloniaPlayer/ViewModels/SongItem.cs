using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using KuGou.Net.Abstractions.Models;
using KugouAvaloniaPlayer.Models;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class SongItem : ObservableObject
{
    [ObservableProperty] private string _albumId = "";
    [ObservableProperty] private long _audioId;
    [ObservableProperty] private string? _cover = "avares://KugouAvaloniaPlayer/Assets/default_song.png";
    [ObservableProperty] private double _durationSeconds;
    [ObservableProperty] private long _fileId; // 用于从歌单中删除歌曲

    [ObservableProperty] private string _hash = "";

    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private string? _localFilePath;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private SongPlaybackSource _playbackSource = SongPlaybackSource.Default;
    [ObservableProperty] private string _singer = "";

    public List<SingerLite> Singers { get; set; } = new();

    [RelayCommand]
    private void Play()
    {
        WeakReferenceMessenger.Default.Send(new PlaySongMessage(this));
    }

    [RelayCommand]
    private void AddToNext()
    {
        WeakReferenceMessenger.Default.Send(new AddToNextMessage(this));
    }

    [RelayCommand]
    private void ShowPlaylistDialog()
    {
        WeakReferenceMessenger.Default.Send(new ShowPlaylistDialogMessage(this));
    }

    [RelayCommand]
    private void ViewSinger(SingerLite? singer)
    {
        if (singer != null)
            WeakReferenceMessenger.Default.Send(new NavigateToSingerMessage(singer));
    }

    [RelayCommand]
    private void RemoveFromPlaylist()
    {
        WeakReferenceMessenger.Default.Send(new RemoveFromPlaylistMessage(this));
    }

    [RelayCommand]
    private void SetLocalCover()
    {
        WeakReferenceMessenger.Default.Send(new SetLocalSongCoverMessage(this));
    }
}

public partial class PlaylistItem : ObservableObject
{
    [ObservableProperty] private int _count;
    [ObservableProperty] private string _cover = "avares://KugouAvaloniaPlayer/Assets/default_song.png";
    [ObservableProperty] private string _id = "";
    [ObservableProperty] private long _listId; // 用于删除歌单的数字 ID
    [ObservableProperty] private string? _localPath;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _subtitle = "";

    [ObservableProperty] private PlaylistType _type;
}
