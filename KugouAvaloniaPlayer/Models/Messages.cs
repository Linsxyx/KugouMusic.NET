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

public record RequestNavigateBackMessage;

public record RefreshPlaylistsMessage;

public record LyricStyleSettingsChangedMessage(
    LyricSettingsScope Scope,
    bool UseCustomMainColor,
    string MainColorHex,
    bool UseCustomTranslationColor,
    string TranslationColorHex,
    bool UseCustomFont,
    string FontFamilyName,
    LyricAlignmentOption Alignment,
    double FontSize);

public enum LyricSettingsScope
{
    Desktop,
    PlayPage
}