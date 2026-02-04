using CommunityToolkit.Mvvm.ComponentModel;

namespace TestMusic.ViewModels;

public partial class SongItem : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _singer = "";
    [ObservableProperty] private string _hash = "";
    [ObservableProperty] private string _albumId = "";
    [ObservableProperty] private double _durationSeconds;
    
    // 用于UI显示当前是否正在播放
    [ObservableProperty] private bool _isPlaying; 
}

public partial class PlaylistItem : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _id = "";
    [ObservableProperty] private int _count;
}