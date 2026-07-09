using System.Collections.Generic;
using KuGou.Net.Abstractions.Models;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Models;

public record PlaySongMessage(SongItem Song);

public record AddToNextMessage(SongItem Song);

public record AddLoadedSongsToQueueMessage(IReadOnlyList<SongItem> Songs);

public record ShowSongBatchActionDialogMessage(IReadOnlyList<SongItem> Songs, bool AllowAddToPlaylist = true);

public record ShowPlaylistDialogMessage(SongItem Song);

public record ReplacePlaybackQueueMessage(IReadOnlyList<SongItem> Songs, SongItem? StartSong = null);

public record NavigateToSingerMessage(SingerLite Singer);

public record RemoveFromPlaylistMessage(SongItem Song);

public record SetLocalSongCoverMessage(SongItem Song);

public record AuthStateChangedMessage(bool IsLoggedIn);

public record RequestNavigateBackMessage;

public record ShowMainWindowMessage;

public record MainWindowChromeActionMessage(MainWindowChromeAction Action);

public record PlaybackControlMessage(PlaybackControlAction Action);

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

public record DesktopLyricDoubleLineChangedMessage(bool IsEnabled);

public record AppBackgroundSettingsChangedMessage(
    bool UseCustomImage,
    string? CustomImagePath,
    double CustomImageOpacity);

public record NowPlayingBackgroundBlurRadiusChangedMessage(double Radius);

public record NowPlayingBackgroundSourceChangedMessage(NowPlayingBackgroundSource Source);

public record LightweightNowPlayingLyricScrollChangedMessage(bool IsEnabled);

public enum LyricSettingsScope
{
    Desktop,
    PlayPage
}

public enum MainWindowChromeAction
{
    Minimize,
    ToggleFullScreen,
    ToggleMaximize,
    Close
}

public enum PlaybackControlAction
{
    TogglePlayPause,
    PreviousTrack,
    NextTrack
}
