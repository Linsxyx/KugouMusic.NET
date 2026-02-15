using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TestMusic.ViewModels;

public partial class MyPlaylistsViewModel : PageViewModelBase
{
    // 4. 控制 UI 显示：True 显示歌曲列表，False 显示歌单大格子
    [ObservableProperty] private bool _isShowingSongs;

    // 2. 当前选中的歌单对象
    [ObservableProperty] private PlaylistItem? _selectedPlaylist;

    public override string DisplayName => "我的歌单";
    public override string Icon => "/Assets/music-player-svgrepo-com.svg";

    // 1. 所有的歌单列表
    public ObservableCollection<PlaylistItem> Playlists { get; } = new();

    // 3. 选中歌单内的歌曲列表
    public ObservableCollection<SongItem> SelectedPlaylistSongs { get; } = new();

    // 返回歌单列表的命令
    [RelayCommand]
    private void GoBack()
    {
        IsShowingSongs = false;
        SelectedPlaylist = null;
        SelectedPlaylistSongs.Clear();
    }
}