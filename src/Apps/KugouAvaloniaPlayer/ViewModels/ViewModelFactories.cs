using KuGou.Net.Clients;
using CommunityToolkit.Mvvm.Messaging;
using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.Services.DesktopLyric;
using Microsoft.Extensions.Logging;
using SukiUI.Toasts;

namespace KugouAvaloniaPlayer.ViewModels;

public interface ISingerViewModelFactory
{
    SingerViewModel Create(string authorId, string singerName);
}

public sealed class SingerViewModelFactory(
    ArtistClient artistClient,
    AlbumClient albumClient,
    PlaylistClient playlistClient,
    ISukiToastManager toastManager,
    IMessenger messenger,
    ILogger<SingerViewModel> logger)
    : ISingerViewModelFactory
{
    public SingerViewModel Create(string authorId, string singerName)
    {
        return new SingerViewModel(
            artistClient,
            albumClient,
            playlistClient,
            toastManager,
            messenger,
            logger,
            authorId,
            singerName);
    }
}

public interface IDiscoverTagViewModelFactory
{
    DiscoverTagViewModel Create();
}

public sealed class DiscoverTagViewModelFactory(
    PlaylistClient playlistClient,
    RecommendClient discoveryClient,
    KugouAvaloniaPlayer.Services.INavigationService navigationService,
    ISukiToastManager toastManager,
    IMessenger messenger,
    ILogger<DiscoverTagViewModel> logger)
    : IDiscoverTagViewModelFactory
{
    public DiscoverTagViewModel Create()
    {
        return new DiscoverTagViewModel(
            playlistClient,
            discoveryClient,
            navigationService,
            toastManager,
            messenger,
            logger);
    }
}

public interface IDesktopLyricViewModelFactory
{
    DesktopLyricViewModel Create();
}

public sealed class DesktopLyricViewModelFactory(
    PlayerViewModel playerViewModel,
    IUiPreferencesState uiPreferencesState,
    IDesktopLyricMousePassthroughService desktopLyricMousePassthroughService)
    : IDesktopLyricViewModelFactory
{
    public DesktopLyricViewModel Create()
    {
        return new DesktopLyricViewModel(
            playerViewModel,
            uiPreferencesState,
            desktopLyricMousePassthroughService.IsSupported,
            usesSeparateLockOverlay: !desktopLyricMousePassthroughService.SupportsSelectiveHitTesting);
    }
}
