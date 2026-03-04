using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using KuGou.Net.Abstractions.Models;
using KugouAvaloniaPlayer.Models;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class SongItem : ObservableObject
{
    [ObservableProperty] private string _albumId = "";
    [ObservableProperty] private string? _cover = "avares://KugouAvaloniaPlayer/Assets/Default.png";
    [ObservableProperty] private double _durationSeconds;
    [ObservableProperty] private long _fileId; // 用于从歌单中删除歌曲

    [ObservableProperty] private string _hash = "";

    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private string? _localFilePath;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _singer = "";

    public List<SingerLite> Singers { get; set; } = new();
}

public partial class PlaylistItem : ObservableObject
{
    [ObservableProperty] private int _count;
    [ObservableProperty] private string _cover = "avares://KugouAvaloniaPlayer/Assets/Default.png";
    [ObservableProperty] private string _id = "";
    [ObservableProperty] private long _listId; // 用于删除歌单的数字 ID
    [ObservableProperty] private string? _localPath;
    [ObservableProperty] private string _name = "";


    [ObservableProperty] private PlaylistType _type;
}