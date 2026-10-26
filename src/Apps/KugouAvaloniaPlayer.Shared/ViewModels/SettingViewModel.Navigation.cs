using System.Collections.Generic;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.Services;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class SettingViewModel
{
    public SettingsCategory[] Categories => SettingsCatalog.Categories;
    public Dictionary<string, Vector> ScrollPositions { get; } = new();
    [ObservableProperty] public partial SettingsCategory SelectedCategory { get; set; } = SettingsCatalog.Categories[0];
    [ObservableProperty] public partial string SelectedLyricSection { get; set; } = "播放页";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDesktopColorError))]
    public partial string DesktopColorError { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlayPageColorError))]
    public partial string PlayPageColorError { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTaskbarUnplayedColorError))]
    public partial string TaskbarUnplayedColorError { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTaskbarPlayedColorError))]
    public partial string TaskbarPlayedColorError { get; set; } = "";
    [ObservableProperty] public partial string UpdateStatus { get; set; } = "尚未检查更新";
    public bool HasDesktopColorError => !string.IsNullOrEmpty(DesktopColorError);
    public bool HasPlayPageColorError => !string.IsNullOrEmpty(PlayPageColorError);
    public bool HasTaskbarUnplayedColorError => !string.IsNullOrEmpty(TaskbarUnplayedColorError);
    public bool HasTaskbarPlayedColorError => !string.IsNullOrEmpty(TaskbarPlayedColorError);
    public string[] LyricSections => IsTaskbarLyricsSupported ? ["播放页", "桌面", "任务栏"] : ["播放页", "桌面"];
    public bool IsAppearanceSection => SelectedSettingsSection == "appearance";
    public bool IsPlayerLyrics => SelectedLyricSection == "播放页";
    public bool IsDesktopLyrics => SelectedLyricSection == "桌面";
    public bool IsTaskbarLyrics => IsTaskbarLyricsSupported && SelectedLyricSection == "任务栏";
    public string ScrollKey => SelectedSettingsSection + "/" + (IsLyricsSection ? SelectedLyricSection : "");

    partial void OnSelectedCategoryChanged(SettingsCategory value)
    {
        if (value != null) SelectedSettingsSection = value.Id;
    }
    partial void OnSelectedLyricSectionChanged(string value)
    {
        OnPropertyChanged(nameof(IsPlayerLyrics));
        OnPropertyChanged(nameof(IsDesktopLyrics));
        OnPropertyChanged(nameof(IsTaskbarLyrics));
        OnPropertyChanged(nameof(ScrollKey));
    }
    [RelayCommand] public void CancelShortcutRecording()
    {
        foreach (var item in ShortcutItems)
            if (item.IsRecording) { item.IsRecording = false; item.ClearStatus(); }
        RefreshShortcutTexts();
    }
    public void SyncEqPreset()
    {
        _isApplyingSettingsSnapshot = true;
        try { SelectedEQPreset = SettingsManager.Settings.EQPreset; }
        finally { _isApplyingSettingsSnapshot = false; }
    }
    partial void OnTaskbarUnplayedColorHexInputChanged(string value) => TaskbarUnplayedColorError = "";
    partial void OnTaskbarPlayedColorHexInputChanged(string value) => TaskbarPlayedColorError = "";
}
