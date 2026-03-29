using KuGou.Net.Clients;

namespace KugouAvaloniaPlayer.ViewModels;

public interface ISingerViewModelFactory
{
    SingerViewModel Create(string authorId, string singerName);
}

public sealed class SingerViewModelFactory(MusicClient musicClient) : ISingerViewModelFactory
{
    public SingerViewModel Create(string authorId, string singerName)
    {
        return new SingerViewModel(musicClient, authorId, singerName);
    }
}

public interface IDesktopLyricViewModelFactory
{
    DesktopLyricViewModel Create();
}

public sealed class DesktopLyricViewModelFactory(PlayerViewModel playerViewModel) : IDesktopLyricViewModelFactory
{
    public DesktopLyricViewModel Create()
    {
        return new DesktopLyricViewModel(playerViewModel);
    }
}