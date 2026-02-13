using CommunityToolkit.Mvvm.ComponentModel;

namespace TestMusic.ViewModels;

public partial class SongItem : ObservableObject
{
    [ObservableProperty] private string _albumId = "";
    [ObservableProperty] private string? _cover = "avares://TestMusic/Assets/Default.png";
    [ObservableProperty] private double _durationSeconds;
    
    [ObservableProperty] private string _hash = "";

    
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _singer = "";
}

public partial class PlaylistItem : ObservableObject
{
    [ObservableProperty] private int _count;
    [ObservableProperty] private string _id = "";
    [ObservableProperty] private string _name = "";
}