using System.Collections.Generic;
using KuGou.Net.Abstractions;

namespace KugouAvaloniaPlayer.Models;

public enum PlayMode
{
    Normal,
    RepeatOne,
    Shuffle
}

public enum CloseBehavior
{
    Exit,
    MinimizeToTray
}

public enum GlobalShortcutAction
{
    PlayPause,
    PreviousTrack,
    NextTrack,
    ShowMainWindow,
    ToggleDesktopLyric,
    VolumeUp,
    VolumeDown
}

public enum LyricAlignmentOption
{
    Center,
    Left,
    Right
}

public enum NowPlayingLyricDisplayMode
{
    LyricsWithTranslation,
    LyricsOnly,
    LyricsWithRomanization
}

public enum NowPlayingBackgroundSource
{
    Cover,
    CustomImage
}

public enum SavedMainWindowState
{
    Normal,
    Maximized
}

public class MainWindowStateSettings
{
    public bool HasValue { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public SavedMainWindowState State { get; set; } = SavedMainWindowState.Normal;
}

public class DesktopLyricWindowPositionSettings
{
    public bool HasValue { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
}

public class GlobalShortcutSettings
{
    public bool EnableGlobalShortcuts { get; set; } = true;
    public string? PlayPause { get; set; } = "Ctrl+Alt+Space";
    public string? PreviousTrack { get; set; } = "Ctrl+Alt+Left";
    public string? NextTrack { get; set; } = "Ctrl+Alt+Right";
    public string? ShowMainWindow { get; set; } = "Ctrl+Alt+Up";
    public string? ToggleDesktopLyric { get; set; } = "Ctrl+Alt+L";
    public string? VolumeUp { get; set; } = "Ctrl+Alt+Shift+Up";
    public string? VolumeDown { get; set; } = "Ctrl+Alt+Shift+Down";

    public GlobalShortcutSettings Clone()
    {
        return new GlobalShortcutSettings
        {
            EnableGlobalShortcuts = EnableGlobalShortcuts,
            PlayPause = PlayPause,
            PreviousTrack = PreviousTrack,
            NextTrack = NextTrack,
            ShowMainWindow = ShowMainWindow,
            ToggleDesktopLyric = ToggleDesktopLyric,
            VolumeUp = VolumeUp,
            VolumeDown = VolumeDown
        };
    }

    public string? GetShortcut(GlobalShortcutAction action)
    {
        return action switch
        {
            GlobalShortcutAction.PlayPause => PlayPause,
            GlobalShortcutAction.PreviousTrack => PreviousTrack,
            GlobalShortcutAction.NextTrack => NextTrack,
            GlobalShortcutAction.ShowMainWindow => ShowMainWindow,
            GlobalShortcutAction.ToggleDesktopLyric => ToggleDesktopLyric,
            GlobalShortcutAction.VolumeUp => VolumeUp,
            GlobalShortcutAction.VolumeDown => VolumeDown,
            _ => null
        };
    }

    public void SetShortcut(GlobalShortcutAction action, string? shortcut)
    {
        switch (action)
        {
            case GlobalShortcutAction.PlayPause:
                PlayPause = shortcut;
                break;
            case GlobalShortcutAction.PreviousTrack:
                PreviousTrack = shortcut;
                break;
            case GlobalShortcutAction.NextTrack:
                NextTrack = shortcut;
                break;
            case GlobalShortcutAction.ShowMainWindow:
                ShowMainWindow = shortcut;
                break;
            case GlobalShortcutAction.ToggleDesktopLyric:
                ToggleDesktopLyric = shortcut;
                break;
            case GlobalShortcutAction.VolumeUp:
                VolumeUp = shortcut;
                break;
            case GlobalShortcutAction.VolumeDown:
                VolumeDown = shortcut;
                break;
        }
    }
}

public class AppSettings
{
    public const int CustomEqBandCount = 10;

    public const string ThemeDefault = "Default";
    public const string ThemeDark = "Dark";
    public const string ThemeLight = "Light";

    public CloseBehavior CloseBehavior { get; set; } = CloseBehavior.MinimizeToTray;
    public string AppTheme { get; set; } = ThemeDefault;
    public string MusicQuality { get; set; } = AudioQuality.Default;
    public PlayMode PlaybackMode { get; set; } = PlayMode.Normal;
    public List<string> LocalMusicFolders { get; set; } = new();
    public Dictionary<string, LocalPlaylistMeta> LocalPlaylistMetas { get; set; } = new();
    public Dictionary<string, JellyfinServerSettings> JellyfinServers { get; set; } = new();
    public string? LastJellyfinServerFingerprint { get; set; }
    public bool AutoCheckUpdate { get; set; } = true;
    public bool UseCustomBackgroundImage { get; set; }
    public string? CustomBackgroundImagePath { get; set; }
    public double CustomBackgroundImageOpacity { get; set; } = 0.35;

    public string EQPreset { get; set; } = "原声";

    public bool EnableSurround { get; set; }

    public bool EnableVolumeNormalization { get; set; }

    public bool EnableSeamlessTransition { get; set; } = true;

    public bool EnableNowPlayingVisualizer { get; set; }

    public bool UseLightweightNowPlayingLyricScroll { get; set; }

    public float MusicVolume { get; set; } = 0.8f;

    public float[] CustomEqGains { get; set; } = new float[CustomEqBandCount];

    public bool DesktopLyricUseCustomMainColor { get; set; }
    public string DesktopLyricCustomMainColor { get; set; } = "#FFFFFFFF";
    public bool DesktopLyricUseCustomTranslationColor { get; set; }
    public string DesktopLyricCustomTranslationColor { get; set; } = "#CCFFFFFF";
    public bool DesktopLyricUseCustomFont { get; set; }
    public string DesktopLyricCustomFontFamily { get; set; } = string.Empty;
    public LyricAlignmentOption DesktopLyricAlignment { get; set; } = LyricAlignmentOption.Left;
    public double DesktopLyricFontSize { get; set; } = 30;
    public bool DesktopLyricShowTranslation { get; set; } = true;
    public bool DesktopLyricDoubleLineEnabled { get; set; }
    public bool OpenDesktopLyricOnStartup { get; set; }
    public DesktopLyricWindowPositionSettings DesktopLyricWindowPosition { get; set; } = new();

    public bool PlayPageLyricUseCustomMainColor { get; set; }
    public string PlayPageLyricCustomMainColor { get; set; } = "#FFFFFFFF";
    public bool PlayPageLyricUseCustomTranslationColor { get; set; }
    public string PlayPageLyricCustomTranslationColor { get; set; } = "#CCFFFFFF";
    public bool PlayPageLyricUseCustomFont { get; set; }
    public string PlayPageLyricCustomFontFamily { get; set; } = string.Empty;
    public LyricAlignmentOption PlayPageLyricAlignment { get; set; } = LyricAlignmentOption.Left;
    public double PlayPageLyricFontSize { get; set; } = 33;

    public NowPlayingLyricDisplayMode PlayPageLyricDisplayMode { get; set; } =
        NowPlayingLyricDisplayMode.LyricsWithTranslation;

    public double NowPlayingBackgroundBlurRadius { get; set; } = 40;
    public NowPlayingBackgroundSource NowPlayingBackgroundSource { get; set; } =
        NowPlayingBackgroundSource.Cover;

    public MainWindowStateSettings MainWindowState { get; set; } = new();

    public GlobalShortcutSettings GlobalShortcuts { get; set; } = new();
}

public class LocalPlaylistMeta
{
    public string? Name { get; set; }
    public string? CoverPath { get; set; }
    public Dictionary<string, string> SongCoverPaths { get; set; } = new();
}

public class JellyfinServerSettings
{
    public string ServerUrl { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
}
