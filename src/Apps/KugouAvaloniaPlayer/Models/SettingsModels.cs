using System.Collections.Generic;

namespace KugouAvaloniaPlayer.Models;

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
    ToggleDesktopLyric
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

public class GlobalShortcutSettings
{
    public bool EnableGlobalShortcuts { get; set; } = true;
    public string? PlayPause { get; set; } = "Ctrl+Alt+Space";
    public string? PreviousTrack { get; set; } = "Ctrl+Alt+Left";
    public string? NextTrack { get; set; } = "Ctrl+Alt+Right";
    public string? ShowMainWindow { get; set; } = "Ctrl+Alt+Up";
    public string? ToggleDesktopLyric { get; set; } = "Ctrl+Alt+L";

    public GlobalShortcutSettings Clone()
    {
        return new GlobalShortcutSettings
        {
            EnableGlobalShortcuts = EnableGlobalShortcuts,
            PlayPause = PlayPause,
            PreviousTrack = PreviousTrack,
            NextTrack = NextTrack,
            ShowMainWindow = ShowMainWindow,
            ToggleDesktopLyric = ToggleDesktopLyric
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
        }
    }
}

public class AppSettings
{
    public CloseBehavior CloseBehavior { get; set; } = CloseBehavior.MinimizeToTray;
    public string MusicQuality { get; set; } = "128";
    public List<string> LocalMusicFolders { get; set; } = new();
    public Dictionary<string, LocalPlaylistMeta> LocalPlaylistMetas { get; set; } = new();
    public bool AutoCheckUpdate { get; set; } = true;

    public string EQPreset { get; set; } = "原声";

    public bool EnableSurround { get; set; }

    public bool EnableSeamlessTransition { get; set; }

    public bool EnableNowPlayingVisualizer { get; set; }

    public float[] CustomEqGains { get; set; } = new float[10];

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

    public bool PlayPageLyricUseCustomMainColor { get; set; }
    public string PlayPageLyricCustomMainColor { get; set; } = "#FFFFFFFF";
    public bool PlayPageLyricUseCustomTranslationColor { get; set; }
    public string PlayPageLyricCustomTranslationColor { get; set; } = "#CCFFFFFF";
    public bool PlayPageLyricUseCustomFont { get; set; }
    public string PlayPageLyricCustomFontFamily { get; set; } = string.Empty;
    public LyricAlignmentOption PlayPageLyricAlignment { get; set; } = LyricAlignmentOption.Left;
    public double PlayPageLyricFontSize { get; set; } = 33;
    public bool EnableLegacyWordLyricEffect { get; set; }

    public NowPlayingLyricDisplayMode PlayPageLyricDisplayMode { get; set; } =
        NowPlayingLyricDisplayMode.LyricsWithTranslation;

    public GlobalShortcutSettings GlobalShortcuts { get; set; } = new();
}

public class LocalPlaylistMeta
{
    public string? Name { get; set; }
    public string? CoverPath { get; set; }
    public Dictionary<string, string> SongCoverPaths { get; set; } = new();
}
