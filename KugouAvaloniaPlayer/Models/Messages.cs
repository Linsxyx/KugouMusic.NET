using KuGou.Net.Abstractions.Models;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Models;

public record PlaySongMessage(SongItem Song);

public record AddToNextMessage(SongItem Song);

public record ShowPlaylistDialogMessage(SongItem Song);

public record NavigateToSingerMessage(SingerLite Singer);

public record RemoveFromPlaylistMessage(SongItem Song);

public record AuthStateChangedMessage(bool IsLoggedIn);

public record NavigatePageMessage(PageViewModelBase TargetPage);