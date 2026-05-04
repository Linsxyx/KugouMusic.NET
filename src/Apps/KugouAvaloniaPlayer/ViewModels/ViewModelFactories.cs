using KuGou.Net.Clients;
using KugouAvaloniaPlayer.Services.DesktopLyric;
using Microsoft.Extensions.Logging;

namespace KugouAvaloniaPlayer.ViewModels;

public interface ISingerViewModelFactory
{
    SingerViewModel Create(string authorId, string singerName);
}

public sealed class SingerViewModelFactory(ArtistClient artistClient, ILogger<SingerViewModel> logger)
    : ISingerViewModelFactory
{
    public SingerViewModel Create(string authorId, string singerName)
    {
        return new SingerViewModel(artistClient, logger, authorId, singerName);
    }
}

public interface IDesktopLyricViewModelFactory
{
    DesktopLyricViewModel Create();
}

public sealed class DesktopLyricViewModelFactory(
    PlayerViewModel playerViewModel,
    IDesktopLyricMousePassthroughService desktopLyricMousePassthroughService)
    : IDesktopLyricViewModelFactory
{
    public DesktopLyricViewModel Create()
    {
        return new DesktopLyricViewModel(
            playerViewModel,
            desktopLyricMousePassthroughService.IsSupported,
            usesSeparateLockOverlay: !desktopLyricMousePassthroughService.SupportsSelectiveHitTesting);
    }
}
