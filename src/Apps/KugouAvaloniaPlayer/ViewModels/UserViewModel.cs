using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using KuGou.Net.Clients;
using KuGou.Net.Protocol.Session;
using KugouAvaloniaPlayer.Behaviors;
using KugouAvaloniaPlayer.Controls;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.Services.GlobalShortcutService;
using SukiUI;
using SukiUI.Dialogs;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class UserViewModel : PageViewModelBase
{
    private const string SettingsSectionGeneral = "常规";
    private const string SettingsSectionPlayback = "播放与音效";
    private const string SettingsSectionShortcuts = "快捷键";
    private const string SettingsSectionLyrics = "歌词设置";
    private const string SettingsSectionUpdate = "更新与关于";
    private const string SettingsSectionAccount = "账户";
    private const string RepositoryUrl = "https://github.com/Linsxyx/KugouMusic.NET";
    private const string LyricScopeDesktop = "桌面歌词";
    private const string LyricScopePlayPage = "播放页面歌词";
    private const string LyricTargetMain = "歌词";
    private const string LyricTargetTranslation = "歌词翻译";
    private const string LyricAlignmentCenter = "居中";
    private const string LyricAlignmentLeft = "居左";
    private const string LyricAlignmentRight = "居右";
    private const string LyricColorModeDefault = "默认";
    private const string LyricColorModeCustom = "自定义";

    private readonly LoginClient _authClient;
    private readonly HashSet<string> _availableLyricFonts;
    private readonly ISukiDialogManager _dialogManager;
    private readonly EqSettingsViewModel _eqSettingsViewModel;
    private readonly IGlobalShortcutService _globalShortcutService;
    private readonly IFolderPickerService _folderPickerService;
    private readonly IGitHubReleaseService _releaseService;
    private readonly KgSessionManager _sessionManager;
    private readonly UserClient _userClient;

    private bool _isApplyingSettingsSnapshot;

    [ObservableProperty]
    public partial bool AutoCheckUpdate { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CustomBackgroundImageStatus))]
    public partial bool UseCustomBackgroundImage { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CustomBackgroundImageStatus))]
    public partial string? CustomBackgroundImagePath { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CustomBackgroundImageOpacityDisplay))]
    public partial double CustomBackgroundImageOpacity { get; set; } = 0.35;
    [ObservableProperty]
    public partial string DesktopLyricColorHexInput { get; set; } = "#FFFFFFFF";
    [ObservableProperty]
    public partial string DesktopSelectedLyricColorMode { get; set; } = LyricColorModeDefault;
    [ObservableProperty]
    public partial string DesktopSelectedLyricColorTarget { get; set; } = LyricTargetMain;
    [ObservableProperty] 
    public partial string? DesktopSelectedLyricFontFamily{ get; set; }
    [ObservableProperty]
    public partial string DesktopSelectedLyricFontMode { get; set; } = LyricColorModeDefault;

    [ObservableProperty]
    public partial bool DesktopLyricDoubleLineEnabled { get; set; }

    [ObservableProperty]
    public partial bool EnableLegacyWordLyricEffect { get; set; }

    [ObservableProperty]
    public partial bool EnableGlobalShortcuts { get; set; }

    [ObservableProperty]
    public partial bool EnableNowPlayingVisualizer { get; set; }

    [ObservableProperty] 
    public partial bool EnableSeamlessTransition { get; set; } = true;

    [ObservableProperty]
    public partial bool EnableSurround { get; set; }

    [ObservableProperty]
    public partial bool IsCheckingUpdate { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingReleaseNotes { get; set; }

    private bool _isInitializingLyricColorEditor;
    private bool _isInitializingLyricFontEditor;
    [ObservableProperty]
    public partial bool IsLoading { get; set; } = true;

    [ObservableProperty]
    public partial string PlayPageLyricColorHexInput { get; set; } = "#FFFFFFFF";

    [ObservableProperty]
    public partial double PlayPageLyricFontSize { get; set; } = 26;

    [ObservableProperty]
    public partial string PlayPageSelectedLyricAlignment { get; set; } = LyricAlignmentCenter;

    [ObservableProperty]
    public partial string PlayPageSelectedLyricColorMode { get; set; } = LyricColorModeDefault;

    [ObservableProperty]
    public partial string PlayPageSelectedLyricColorTarget { get; set; } = LyricTargetMain;

    [ObservableProperty]
    public partial string? PlayPageSelectedLyricFontFamily { get; set; }

    [ObservableProperty]
    public partial string PlayPageSelectedLyricFontMode { get; set; } = LyricColorModeDefault;

    [ObservableProperty]
    public partial string ReleaseNotesStatus { get; set; } = "正在加载最近版本发布说明...";

    [ObservableProperty]
    public partial CloseBehavior SelectedCloseBehavior { get; set; }

    [ObservableProperty]
    public partial string SelectedEQPreset { get; set; }

    [ObservableProperty]
    public partial string SelectedSettingsSection { get; set; } = SettingsSectionGeneral;

    [ObservableProperty]
    public partial string? UserAvatar { get; set; }

    [ObservableProperty]
    public partial string UserId { get; set; }

    [ObservableProperty]
    public partial string UserName { get; set; } = "加载中...";

    [ObservableProperty]
    public partial string VipStatus { get; set; } = "未开通";

    public UserViewModel(PlayerViewModel player, UserClient userClient, LoginClient authClient,
        ISukiDialogManager dialogManager, EqSettingsViewModel eqSettingsViewModel, KgSessionManager sessionManager,
        IGlobalShortcutService globalShortcutService, IGitHubReleaseService releaseService,
        IFolderPickerService folderPickerService)
    {
        _userClient = userClient;
        _authClient = authClient;
        _dialogManager = dialogManager;
        _eqSettingsViewModel = eqSettingsViewModel;
        _sessionManager = sessionManager;
        _globalShortcutService = globalShortcutService;
        _releaseService = releaseService;
        _folderPickerService = folderPickerService;

        Player = player;
        SelectedCloseBehavior = SettingsManager.Settings.CloseBehavior;
        AutoCheckUpdate = SettingsManager.Settings.AutoCheckUpdate;
        UseCustomBackgroundImage = SettingsManager.Settings.UseCustomBackgroundImage;
        CustomBackgroundImagePath = SettingsManager.Settings.CustomBackgroundImagePath;
        CustomBackgroundImageOpacity = Math.Clamp(SettingsManager.Settings.CustomBackgroundImageOpacity, 0.1, 1.0);
        EnableGlobalShortcuts = SettingsManager.Settings.GlobalShortcuts.EnableGlobalShortcuts;
        EQPresetOptions = ["原声", "流行", "摇滚", "爵士", "古典", "嘻哈", "布鲁斯", "电子音乐", "金属", "自定义"];

        var preset = SettingsManager.Settings.EQPreset;
        SelectedEQPreset = Array.Exists(EQPresetOptions, x => x == preset) ? preset : "原声";

        EnableSurround = SettingsManager.Settings.EnableSurround;
        EnableSeamlessTransition = SettingsManager.Settings.EnableSeamlessTransition;
        EnableNowPlayingVisualizer = SettingsManager.Settings.EnableNowPlayingVisualizer;
        EnableLegacyWordLyricEffect = SettingsManager.Settings.EnableLegacyWordLyricEffect;
        DesktopLyricDoubleLineEnabled = SettingsManager.Settings.DesktopLyricDoubleLineEnabled;
        LyricFontFamilyOptions = LoadSystemFontFamilies();
        _availableLyricFonts = new HashSet<string>(LyricFontFamilyOptions, StringComparer.OrdinalIgnoreCase);
        UserId = _sessionManager.Session.UserId;
        LoadDesktopLyricColorEditorFromSettings();
        LoadDesktopLyricFontEditorFromSettings();
        LoadPlayPageLyricColorEditorFromSettings();
        LoadPlayPageLyricFontEditorFromSettings();
        LoadPlayPageLyricAlignmentFromSettings();
        ShortcutItems =
        [
            new GlobalShortcutItemViewModel(GlobalShortcutAction.PlayPause, "播放/暂停"),
            new GlobalShortcutItemViewModel(GlobalShortcutAction.PreviousTrack, "上一首"),
            new GlobalShortcutItemViewModel(GlobalShortcutAction.NextTrack, "下一首"),
            new GlobalShortcutItemViewModel(GlobalShortcutAction.ShowMainWindow, "显示窗口"),
            new GlobalShortcutItemViewModel(GlobalShortcutAction.ToggleDesktopLyric, "显示桌面歌词")
        ];
        RefreshShortcutTexts();
        ApplyRegistrationResults(_globalShortcutService.CurrentResults);
        _globalShortcutService.RegistrationChanged += ApplyRegistrationResults;
    }

    public string[] EQPresetOptions { get; }

    public string[] SettingsSections { get; } =
    [
        SettingsSectionGeneral, SettingsSectionPlayback, SettingsSectionShortcuts, SettingsSectionLyrics,
        SettingsSectionUpdate,
        SettingsSectionAccount
    ];

    public string[] LyricColorTargetOptions { get; } = [LyricTargetMain, LyricTargetTranslation];
    public string[] LyricSettingsScopeOptions { get; } = [LyricScopeDesktop, LyricScopePlayPage];
    public string[] LyricAlignmentOptions { get; } = [LyricAlignmentCenter, LyricAlignmentLeft, LyricAlignmentRight];

    public string[] LyricColorModeOptions { get; } = [LyricColorModeDefault, LyricColorModeCustom];
    public string[] LyricFontModeOptions { get; } = [LyricColorModeDefault, LyricColorModeCustom];

    public string[] LyricColorPalette { get; } =
    [
        "#FFFFFFFF",
        "#FFCCFFFFFF",
        "#FFFFE082",
        "#FFFFAB91",
        "#FFA5D6A7",
        "#FF80DEEA",
        "#FF90CAF9",
        "#FFB39DDB",
        "#FFF48FB1",
        "#FFFFF59D",
        "#FFB0BEC5",
        "#FFFFCDD2"
    ];

    public string[] LyricFontFamilyOptions { get; }
    public GlobalShortcutItemViewModel[] ShortcutItems { get; }
    public ObservableCollection<ReleaseNoteItemViewModel> RecentReleaseNotes { get; } = [];

    public PlayerViewModel Player { get; }

    public override string DisplayName => "设置";
    public override string Icon => "/Assets/gear-svgrepo-com.svg";
    public string AppDisplayName => "KA Music";
    public string AppVersion => $"v {GetCurrentVersion()}";

    public CloseBehavior[] AvailableCloseBehaviors { get; } = Enum.GetValues<CloseBehavior>();

    public bool IsDesktopLyricColorCustomMode => DesktopSelectedLyricColorMode == LyricColorModeCustom;
    public bool IsDesktopLyricFontCustomMode => DesktopSelectedLyricFontMode == LyricColorModeCustom;
    public bool IsPlayPageLyricColorCustomMode => PlayPageSelectedLyricColorMode == LyricColorModeCustom;
    public bool IsPlayPageLyricFontCustomMode => PlayPageSelectedLyricFontMode == LyricColorModeCustom;
    public bool IsGeneralSection => SelectedSettingsSection == SettingsSectionGeneral;
    public bool IsPlaybackSection => SelectedSettingsSection == SettingsSectionPlayback;
    public bool IsShortcutsSection => SelectedSettingsSection == SettingsSectionShortcuts;
    public bool IsLyricsSection => SelectedSettingsSection == SettingsSectionLyrics;
    public bool IsUpdateSection => SelectedSettingsSection == SettingsSectionUpdate;
    public bool IsAccountSection => SelectedSettingsSection == SettingsSectionAccount;
    public bool HasReleaseNotes => RecentReleaseNotes.Count > 0;
    public bool IsReleaseNotesStatusVisible => IsLoadingReleaseNotes || !HasReleaseNotes;

    public IBrush DesktopLyricColorPreviewBrush =>
        new SolidColorBrush(ParseColorOrDefault(DesktopLyricColorHexInput, Colors.Transparent));

    public IBrush PlayPageLyricColorPreviewBrush =>
        new SolidColorBrush(ParseColorOrDefault(PlayPageLyricColorHexInput, Colors.Transparent));

    public string PlayPageLyricFontSizeDisplay => $"{Math.Round(PlayPageLyricFontSize):0}pt";
    public string CustomBackgroundImageOpacityDisplay => $"{Math.Round(CustomBackgroundImageOpacity * 100):0}%";
    public string CustomBackgroundImageStatus =>
        string.IsNullOrWhiteSpace(CustomBackgroundImagePath)
            ? "未选择图片"
            : CustomBackgroundImagePath;

    public bool IsDarkMode
    {
        get => SukiTheme.GetInstance().ActiveBaseTheme == ThemeVariant.Dark;
        set
        {
            SukiTheme.GetInstance().ChangeBaseTheme(value ? ThemeVariant.Dark : ThemeVariant.Light);
            OnPropertyChanged();
        }
    }

    public async Task LoadUserInfoAsync()
    {
        IsLoading = true;
        try
        {
            var userInfo = await _userClient.GetUserInfoAsync();
            if (userInfo != null)
            {
                UserName = userInfo.Name;
                UserAvatar = string.IsNullOrWhiteSpace(userInfo.Pic) ? null : userInfo.Pic;
                UserId = _sessionManager.Session.UserId;
            }

            var vipInfo = await _userClient.GetVipInfoAsync();
            if (vipInfo != null) VipStatus = vipInfo.IsVip is 1 ? "VIP会员" : "普通用户";
        }
        catch
        {
            UserName = "加载失败";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task Logout()
    {
        _authClient.LogOutAsync();
        WeakReferenceMessenger.Default.Send(new AuthStateChangedMessage(false));
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task CheckForUpdate()
    {
        if (IsCheckingUpdate) return;
        IsCheckingUpdate = true;
        CheckForUpdateRequested?.Invoke();
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task RefreshReleaseNotes()
    {
        await LoadReleaseNotesAsync(forceRefresh: true);
    }

    [RelayCommand]
    private void OpenRepository()
    {
        OpenUrl(RepositoryUrl);
    }

    [RelayCommand]
    private void PickDesktopLyricPaletteColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return;
        DesktopLyricColorHexInput = hex;
        ApplyDesktopLyricColorHex();
    }

    [RelayCommand]
    private void ApplyDesktopLyricColorHex()
    {
        var normalized = NormalizeColorHex(DesktopLyricColorHexInput);
        if (normalized == null) return;

        if (!IsDesktopLyricColorCustomMode)
        {
            _isInitializingLyricColorEditor = true;
            DesktopSelectedLyricColorMode = LyricColorModeCustom;
            _isInitializingLyricColorEditor = false;
            SetDesktopTargetCustomEnabled(true);
            OnPropertyChanged(nameof(IsDesktopLyricColorCustomMode));
        }

        SetDesktopTargetCustomColor(normalized);
        DesktopLyricColorHexInput = normalized;
        OnPropertyChanged(nameof(DesktopLyricColorPreviewBrush));
        SettingsManager.Save();
        NotifyLyricStyleChanged(LyricSettingsScope.Desktop);
    }

    [RelayCommand]
    private void PickPlayPageLyricPaletteColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return;
        PlayPageLyricColorHexInput = hex;
        ApplyPlayPageLyricColorHex();
    }

    [RelayCommand]
    private void ApplyPlayPageLyricColorHex()
    {
        var normalized = NormalizeColorHex(PlayPageLyricColorHexInput);
        if (normalized == null) return;

        if (!IsPlayPageLyricColorCustomMode)
        {
            _isInitializingLyricColorEditor = true;
            PlayPageSelectedLyricColorMode = LyricColorModeCustom;
            _isInitializingLyricColorEditor = false;
            SetPlayPageTargetCustomEnabled(true);
            OnPropertyChanged(nameof(IsPlayPageLyricColorCustomMode));
        }

        SetPlayPageTargetCustomColor(normalized);
        PlayPageLyricColorHexInput = normalized;
        OnPropertyChanged(nameof(PlayPageLyricColorPreviewBrush));
        SettingsManager.Save();
        NotifyLyricStyleChanged(LyricSettingsScope.PlayPage);
    }

    public event Action? CheckForUpdateRequested;

    [RelayCommand]
    private void SwitchSettingsSection(string? section)
    {
        if (string.IsNullOrWhiteSpace(section)) return;
        if (SettingsSections.Contains(section)) SelectedSettingsSection = section;
    }

    partial void OnSelectedCloseBehaviorChanged(CloseBehavior value)
    {
        if (_isApplyingSettingsSnapshot) return;

        SettingsManager.Settings.CloseBehavior = value;
        SettingsManager.Save();
    }

    partial void OnAutoCheckUpdateChanged(bool value)
    {
        if (_isApplyingSettingsSnapshot) return;

        SettingsManager.Settings.AutoCheckUpdate = value;
        SettingsManager.Save();
    }

    partial void OnUseCustomBackgroundImageChanged(bool value)
    {
        if (_isApplyingSettingsSnapshot) return;

        SettingsManager.Settings.UseCustomBackgroundImage = value;
        SaveAndNotifyBackgroundSettings();
    }

    partial void OnCustomBackgroundImageOpacityChanged(double value)
    {
        OnPropertyChanged(nameof(CustomBackgroundImageOpacityDisplay));
        if (_isApplyingSettingsSnapshot) return;

        var clamped = Math.Clamp(value, 0.1, 1.0);
        if (Math.Abs(clamped - value) > double.Epsilon)
        {
            CustomBackgroundImageOpacity = clamped;
            return;
        }

        SettingsManager.Settings.CustomBackgroundImageOpacity = clamped;
        SaveAndNotifyBackgroundSettings();
    }

    partial void OnEnableGlobalShortcutsChanged(bool value)
    {
        if (_isApplyingSettingsSnapshot) return;

        if (ShortcutItems == null)
            return;

        var candidate = BuildShortcutSettingsSnapshot();
        candidate.EnableGlobalShortcuts = value;
        var applyResult = _globalShortcutService.TryApplySettings(candidate);
        if (!applyResult.Success)
        {
            EnableGlobalShortcuts = !value;
            return;
        }

        SettingsManager.Settings.GlobalShortcuts = candidate;
        SettingsManager.Save();
        ApplyRegistrationResults(applyResult.Results);
    }

    partial void OnSelectedEQPresetChanged(string value)
    {
        if (_isApplyingSettingsSnapshot) return;

        SettingsManager.Settings.EQPreset = value;
        SettingsManager.Save();
        Player.UpdateAudioEffects(value, EnableSurround);
    }

    partial void OnEnableSurroundChanged(bool value)
    {
        if (_isApplyingSettingsSnapshot) return;

        SettingsManager.Settings.EnableSurround = value;
        SettingsManager.Save();
        Player.UpdateAudioEffects(SelectedEQPreset, value);
    }

    partial void OnEnableSeamlessTransitionChanged(bool value)
    {
        if (_isApplyingSettingsSnapshot) return;

        SettingsManager.Settings.EnableSeamlessTransition = value;
        SettingsManager.Save();
        Player.SetSeamlessTransitionEnabled(value);
    }

    partial void OnEnableNowPlayingVisualizerChanged(bool value)
    {
        if (_isApplyingSettingsSnapshot) return;

        SettingsManager.Settings.EnableNowPlayingVisualizer = value;
        SettingsManager.Save();
        Player.SetNowPlayingVisualizerEnabled(value);
    }

    partial void OnEnableLegacyWordLyricEffectChanged(bool value)
    {
        if (_isApplyingSettingsSnapshot) return;

        SettingsManager.Settings.EnableLegacyWordLyricEffect = value;
        SettingsManager.Save();
        NotifyLyricStyleChanged(LyricSettingsScope.Desktop);
        NotifyLyricStyleChanged(LyricSettingsScope.PlayPage);
    }

    partial void OnDesktopLyricDoubleLineEnabledChanged(bool value)
    {
        if (_isApplyingSettingsSnapshot) return;

        SettingsManager.Settings.DesktopLyricDoubleLineEnabled = value;
        SettingsManager.Save();
        WeakReferenceMessenger.Default.Send(new DesktopLyricDoubleLineChangedMessage(value));
    }

    partial void OnDesktopSelectedLyricColorTargetChanged(string value)
    {
        LoadDesktopLyricColorEditorFromSettings();
    }

    partial void OnPlayPageSelectedLyricColorTargetChanged(string value)
    {
        LoadPlayPageLyricColorEditorFromSettings();
    }

    partial void OnDesktopSelectedLyricColorModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsDesktopLyricColorCustomMode));
        if (_isInitializingLyricColorEditor) return;

        SetDesktopTargetCustomEnabled(value == LyricColorModeCustom);
        SettingsManager.Save();
        NotifyLyricStyleChanged(LyricSettingsScope.Desktop);
    }

    partial void OnPlayPageSelectedLyricColorModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsPlayPageLyricColorCustomMode));
        if (_isInitializingLyricColorEditor) return;

        SetPlayPageTargetCustomEnabled(value == LyricColorModeCustom);
        SettingsManager.Save();
        NotifyLyricStyleChanged(LyricSettingsScope.PlayPage);
    }

    partial void OnDesktopLyricColorHexInputChanged(string value)
    {
        OnPropertyChanged(nameof(DesktopLyricColorPreviewBrush));
    }

    partial void OnPlayPageLyricColorHexInputChanged(string value)
    {
        OnPropertyChanged(nameof(PlayPageLyricColorPreviewBrush));
    }

    partial void OnDesktopSelectedLyricFontModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsDesktopLyricFontCustomMode));
        if (_isInitializingLyricFontEditor) return;

        SettingsManager.Settings.DesktopLyricUseCustomFont = value == LyricColorModeCustom;
        if (SettingsManager.Settings.DesktopLyricUseCustomFont &&
            !string.IsNullOrWhiteSpace(DesktopSelectedLyricFontFamily))
            SettingsManager.Settings.DesktopLyricCustomFontFamily = DesktopSelectedLyricFontFamily;
        SettingsManager.Save();
        NotifyLyricStyleChanged(LyricSettingsScope.Desktop);
    }

    partial void OnPlayPageSelectedLyricFontModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsPlayPageLyricFontCustomMode));
        if (_isInitializingLyricFontEditor) return;

        SettingsManager.Settings.PlayPageLyricUseCustomFont = value == LyricColorModeCustom;
        if (SettingsManager.Settings.PlayPageLyricUseCustomFont &&
            !string.IsNullOrWhiteSpace(PlayPageSelectedLyricFontFamily))
            SettingsManager.Settings.PlayPageLyricCustomFontFamily = PlayPageSelectedLyricFontFamily;
        SettingsManager.Save();
        NotifyLyricStyleChanged(LyricSettingsScope.PlayPage);
    }

    partial void OnDesktopSelectedLyricFontFamilyChanged(string? value)
    {
        if (_isInitializingLyricFontEditor) return;

        var normalized = NormalizeFontName(value);
        if (normalized == null)
            return;

        if (!string.Equals(value, normalized, StringComparison.Ordinal))
        {
            _isInitializingLyricFontEditor = true;
            DesktopSelectedLyricFontFamily = normalized;
            _isInitializingLyricFontEditor = false;
        }

        SettingsManager.Settings.DesktopLyricCustomFontFamily = normalized;
        SettingsManager.Settings.DesktopLyricUseCustomFont = DesktopSelectedLyricFontMode == LyricColorModeCustom;
        SettingsManager.Save();
        NotifyLyricStyleChanged(LyricSettingsScope.Desktop);
    }

    partial void OnPlayPageSelectedLyricFontFamilyChanged(string? value)
    {
        if (_isInitializingLyricFontEditor) return;

        var normalized = NormalizeFontName(value);
        if (normalized == null)
            return;

        if (!string.Equals(value, normalized, StringComparison.Ordinal))
        {
            _isInitializingLyricFontEditor = true;
            PlayPageSelectedLyricFontFamily = normalized;
            _isInitializingLyricFontEditor = false;
        }

        SettingsManager.Settings.PlayPageLyricCustomFontFamily = normalized;
        SettingsManager.Settings.PlayPageLyricUseCustomFont = PlayPageSelectedLyricFontMode == LyricColorModeCustom;
        SettingsManager.Save();
        NotifyLyricStyleChanged(LyricSettingsScope.PlayPage);
    }

    partial void OnPlayPageSelectedLyricAlignmentChanged(string value)
    {
        if (_isApplyingSettingsSnapshot) return;

        SettingsManager.Settings.PlayPageLyricAlignment = ParseAlignment(value);
        SettingsManager.Save();
        NotifyLyricStyleChanged(LyricSettingsScope.PlayPage);
    }

    partial void OnPlayPageLyricFontSizeChanged(double value)
    {
        if (_isApplyingSettingsSnapshot)
        {
            OnPropertyChanged(nameof(PlayPageLyricFontSizeDisplay));
            return;
        }

        var clamped = Math.Clamp(value, 18, 42);
        if (Math.Abs(clamped - value) > double.Epsilon)
        {
            PlayPageLyricFontSize = clamped;
            return;
        }

        SettingsManager.Settings.PlayPageLyricFontSize = clamped;
        SettingsManager.Save();
        OnPropertyChanged(nameof(PlayPageLyricFontSizeDisplay));
        NotifyLyricStyleChanged(LyricSettingsScope.PlayPage);
    }

    public void SetCheckingUpdateState(bool isChecking)
    {
        IsCheckingUpdate = isChecking;
    }

    [RelayCommand]
    private void OpenEqSettings()
    {
        var eqSettings = new EqSettingsControl
        {
            DataContext = _eqSettingsViewModel
        };

        _dialogManager.CreateDialog()
            .WithContent(eqSettings)
            .WithActionButton("确定", _ => { }, true)
            .TryShow();
    }

    [RelayCommand]
    private async Task PickCustomBackgroundImage()
    {
        var path = await _folderPickerService.PickSingleImageFileAsync("选择自定义背景图");
        if (string.IsNullOrWhiteSpace(path)) return;

        CustomBackgroundImagePath = path;
        UseCustomBackgroundImage = true;
        SettingsManager.Settings.CustomBackgroundImagePath = path;
        SettingsManager.Settings.UseCustomBackgroundImage = true;
        SaveAndNotifyBackgroundSettings();
    }

    [RelayCommand]
    private void ClearCustomBackgroundImage()
    {
        CustomBackgroundImagePath = null;
        UseCustomBackgroundImage = false;
        SettingsManager.Settings.CustomBackgroundImagePath = null;
        SettingsManager.Settings.UseCustomBackgroundImage = false;
        SaveAndNotifyBackgroundSettings();
    }

    [RelayCommand]
    private void ResetAllSettings()
    {
        _dialogManager.CreateDialog()
            .WithTitle("重置设置")
            .WithContent("确认要一键重置所有设置吗？这会将设置恢复为默认值，但不会清除本地音乐记录。")
            .WithActionButton("取消", _ => { }, true, "Standard")
            .WithActionButton("确认", _ =>
            {
                SettingsManager.ResetSettings();
                ApplySettingsSnapshot();
            }, true)
            .TryShow();
    }

    private void ApplySettingsSnapshot()
    {
        StopRecording(true);

        _isApplyingSettingsSnapshot = true;
        try
        {
            SelectedCloseBehavior = SettingsManager.Settings.CloseBehavior;
            AutoCheckUpdate = SettingsManager.Settings.AutoCheckUpdate;
            UseCustomBackgroundImage = SettingsManager.Settings.UseCustomBackgroundImage;
            CustomBackgroundImagePath = SettingsManager.Settings.CustomBackgroundImagePath;
            CustomBackgroundImageOpacity = Math.Clamp(SettingsManager.Settings.CustomBackgroundImageOpacity, 0.1, 1.0);
            EnableGlobalShortcuts = SettingsManager.Settings.GlobalShortcuts.EnableGlobalShortcuts;
            SelectedEQPreset = Array.Exists(EQPresetOptions, x => x == SettingsManager.Settings.EQPreset)
                ? SettingsManager.Settings.EQPreset
                : "原声";
            EnableSurround = SettingsManager.Settings.EnableSurround;
            EnableSeamlessTransition = SettingsManager.Settings.EnableSeamlessTransition;
            EnableNowPlayingVisualizer = SettingsManager.Settings.EnableNowPlayingVisualizer;
            EnableLegacyWordLyricEffect = SettingsManager.Settings.EnableLegacyWordLyricEffect;
            DesktopLyricDoubleLineEnabled = SettingsManager.Settings.DesktopLyricDoubleLineEnabled;

            LoadDesktopLyricColorEditorFromSettings();
            LoadDesktopLyricFontEditorFromSettings();
            LoadPlayPageLyricColorEditorFromSettings();
            LoadPlayPageLyricFontEditorFromSettings();
            LoadPlayPageLyricAlignmentFromSettings();
        }
        finally
        {
            _isApplyingSettingsSnapshot = false;
        }

        Player.MusicQuality = SettingsManager.Settings.MusicQuality;
        Player.UpdateAudioEffects(SelectedEQPreset, EnableSurround);
        Player.SetSeamlessTransitionEnabled(EnableSeamlessTransition);
        Player.SetNowPlayingVisualizerEnabled(EnableNowPlayingVisualizer);
        _eqSettingsViewModel.ReloadFromSettings();

        var shortcutApplyResult = _globalShortcutService.TryApplySettings(SettingsManager.Settings.GlobalShortcuts);
        RefreshShortcutTexts();
        ApplyRegistrationResults(shortcutApplyResult.Results);

        NotifyLyricStyleChanged(LyricSettingsScope.Desktop);
        NotifyLyricStyleChanged(LyricSettingsScope.PlayPage);
        NotifyBackgroundSettingsChanged();
        WeakReferenceMessenger.Default.Send(
            new DesktopLyricDoubleLineChangedMessage(SettingsManager.Settings.DesktopLyricDoubleLineEnabled));
    }

    private static void SaveAndNotifyBackgroundSettings()
    {
        SettingsManager.Save();
        NotifyBackgroundSettingsChanged();
    }

    private static void NotifyBackgroundSettingsChanged()
    {
        WeakReferenceMessenger.Default.Send(new AppBackgroundSettingsChangedMessage(
            SettingsManager.Settings.UseCustomBackgroundImage,
            SettingsManager.Settings.CustomBackgroundImagePath,
            SettingsManager.Settings.CustomBackgroundImageOpacity));
    }

    private void LoadDesktopLyricColorEditorFromSettings()
    {
        _isInitializingLyricColorEditor = true;

        if (IsEditingDesktopMainLyricColor())
        {
            DesktopSelectedLyricColorMode = SettingsManager.Settings.DesktopLyricUseCustomMainColor
                ? LyricColorModeCustom
                : LyricColorModeDefault;
            DesktopLyricColorHexInput = SettingsManager.Settings.DesktopLyricCustomMainColor;
        }
        else
        {
            DesktopSelectedLyricColorMode = SettingsManager.Settings.DesktopLyricUseCustomTranslationColor
                ? LyricColorModeCustom
                : LyricColorModeDefault;
            DesktopLyricColorHexInput = SettingsManager.Settings.DesktopLyricCustomTranslationColor;
        }

        _isInitializingLyricColorEditor = false;
        OnPropertyChanged(nameof(IsDesktopLyricColorCustomMode));
        OnPropertyChanged(nameof(DesktopLyricColorPreviewBrush));
    }

    private void LoadPlayPageLyricColorEditorFromSettings()
    {
        _isInitializingLyricColorEditor = true;

        if (IsEditingPlayPageMainLyricColor())
        {
            PlayPageSelectedLyricColorMode = SettingsManager.Settings.PlayPageLyricUseCustomMainColor
                ? LyricColorModeCustom
                : LyricColorModeDefault;
            PlayPageLyricColorHexInput = SettingsManager.Settings.PlayPageLyricCustomMainColor;
        }
        else
        {
            PlayPageSelectedLyricColorMode = SettingsManager.Settings.PlayPageLyricUseCustomTranslationColor
                ? LyricColorModeCustom
                : LyricColorModeDefault;
            PlayPageLyricColorHexInput = SettingsManager.Settings.PlayPageLyricCustomTranslationColor;
        }

        _isInitializingLyricColorEditor = false;
        OnPropertyChanged(nameof(IsPlayPageLyricColorCustomMode));
        OnPropertyChanged(nameof(PlayPageLyricColorPreviewBrush));
    }

    private void LoadDesktopLyricFontEditorFromSettings()
    {
        _isInitializingLyricFontEditor = true;

        DesktopSelectedLyricFontMode = SettingsManager.Settings.DesktopLyricUseCustomFont
            ? LyricColorModeCustom
            : LyricColorModeDefault;
        DesktopSelectedLyricFontFamily = NormalizeFontName(SettingsManager.Settings.DesktopLyricCustomFontFamily);

        _isInitializingLyricFontEditor = false;
        OnPropertyChanged(nameof(IsDesktopLyricFontCustomMode));
    }

    private void LoadPlayPageLyricFontEditorFromSettings()
    {
        _isInitializingLyricFontEditor = true;

        PlayPageSelectedLyricFontMode = SettingsManager.Settings.PlayPageLyricUseCustomFont
            ? LyricColorModeCustom
            : LyricColorModeDefault;
        PlayPageSelectedLyricFontFamily = NormalizeFontName(SettingsManager.Settings.PlayPageLyricCustomFontFamily);

        _isInitializingLyricFontEditor = false;
        OnPropertyChanged(nameof(IsPlayPageLyricFontCustomMode));
    }

    private void LoadPlayPageLyricAlignmentFromSettings()
    {
        PlayPageSelectedLyricAlignment = FormatAlignment(SettingsManager.Settings.PlayPageLyricAlignment);
        PlayPageLyricFontSize = Math.Clamp(SettingsManager.Settings.PlayPageLyricFontSize, 18, 42);
    }

    private bool IsEditingDesktopMainLyricColor()
    {
        return DesktopSelectedLyricColorTarget == LyricTargetMain;
    }

    private bool IsEditingPlayPageMainLyricColor()
    {
        return PlayPageSelectedLyricColorTarget == LyricTargetMain;
    }

    private void SetDesktopTargetCustomEnabled(bool enabled)
    {
        if (IsEditingDesktopMainLyricColor())
            SettingsManager.Settings.DesktopLyricUseCustomMainColor = enabled;
        else
            SettingsManager.Settings.DesktopLyricUseCustomTranslationColor = enabled;
    }

    private void SetPlayPageTargetCustomEnabled(bool enabled)
    {
        if (IsEditingPlayPageMainLyricColor())
            SettingsManager.Settings.PlayPageLyricUseCustomMainColor = enabled;
        else
            SettingsManager.Settings.PlayPageLyricUseCustomTranslationColor = enabled;
    }

    private void SetDesktopTargetCustomColor(string normalizedHex)
    {
        if (IsEditingDesktopMainLyricColor())
            SettingsManager.Settings.DesktopLyricCustomMainColor = normalizedHex;
        else
            SettingsManager.Settings.DesktopLyricCustomTranslationColor = normalizedHex;
    }

    private void SetPlayPageTargetCustomColor(string normalizedHex)
    {
        if (IsEditingPlayPageMainLyricColor())
            SettingsManager.Settings.PlayPageLyricCustomMainColor = normalizedHex;
        else
            SettingsManager.Settings.PlayPageLyricCustomTranslationColor = normalizedHex;
    }

    private static string? NormalizeColorHex(string? colorText)
    {
        if (string.IsNullOrWhiteSpace(colorText)) return null;
        return Color.TryParse(colorText.Trim(), out var parsed) ? parsed.ToString() : null;
    }

    private string? NormalizeFontName(string? fontName)
    {
        if (string.IsNullOrWhiteSpace(fontName)) return null;
        var trimmed = fontName.Trim();

        if (!_availableLyricFonts.Contains(trimmed))
            return null;

        return LyricFontFamilyOptions.FirstOrDefault(x =>
            string.Equals(x, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    private static Color ParseColorOrDefault(string? colorText, Color fallback)
    {
        return Color.TryParse(colorText, out var parsed) ? parsed : fallback;
    }

    private static void NotifyLyricStyleChanged(LyricSettingsScope scope)
    {
        var isDesktop = scope == LyricSettingsScope.Desktop;
        WeakReferenceMessenger.Default.Send(new LyricStyleSettingsChangedMessage(
            scope,
            isDesktop
                ? SettingsManager.Settings.DesktopLyricUseCustomMainColor
                : SettingsManager.Settings.PlayPageLyricUseCustomMainColor,
            isDesktop
                ? SettingsManager.Settings.DesktopLyricCustomMainColor
                : SettingsManager.Settings.PlayPageLyricCustomMainColor,
            isDesktop
                ? SettingsManager.Settings.DesktopLyricUseCustomTranslationColor
                : SettingsManager.Settings.PlayPageLyricUseCustomTranslationColor,
            isDesktop
                ? SettingsManager.Settings.DesktopLyricCustomTranslationColor
                : SettingsManager.Settings.PlayPageLyricCustomTranslationColor,
            isDesktop
                ? SettingsManager.Settings.DesktopLyricUseCustomFont
                : SettingsManager.Settings.PlayPageLyricUseCustomFont,
            isDesktop
                ? SettingsManager.Settings.DesktopLyricCustomFontFamily
                : SettingsManager.Settings.PlayPageLyricCustomFontFamily,
            isDesktop
                ? LyricAlignmentOption.Center
                : SettingsManager.Settings.PlayPageLyricAlignment,
            isDesktop
                ? SettingsManager.Settings.DesktopLyricFontSize
                : SettingsManager.Settings.PlayPageLyricFontSize,
            SettingsManager.Settings.EnableLegacyWordLyricEffect));
    }

    private static LyricAlignmentOption ParseAlignment(string? alignment)
    {
        return alignment switch
        {
            LyricAlignmentLeft => LyricAlignmentOption.Left,
            LyricAlignmentRight => LyricAlignmentOption.Right,
            _ => LyricAlignmentOption.Center
        };
    }

    private static string FormatAlignment(LyricAlignmentOption alignment)
    {
        return alignment switch
        {
            LyricAlignmentOption.Left => LyricAlignmentLeft,
            LyricAlignmentOption.Right => LyricAlignmentRight,
            _ => LyricAlignmentCenter
        };
    }

    private static string[] LoadSystemFontFamilies()
    {
        return FontManager.Current.SystemFonts
            .Select(f => f.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    partial void OnSelectedSettingsSectionChanged(string value)
    {
        OnPropertyChanged(nameof(IsGeneralSection));
        OnPropertyChanged(nameof(IsPlaybackSection));
        OnPropertyChanged(nameof(IsShortcutsSection));
        OnPropertyChanged(nameof(IsLyricsSection));
        OnPropertyChanged(nameof(IsUpdateSection));
        OnPropertyChanged(nameof(IsAccountSection));

        if (value == SettingsSectionUpdate)
            _ = LoadReleaseNotesAsync();
    }

    partial void OnIsLoadingReleaseNotesChanged(bool value)
    {
        OnPropertyChanged(nameof(IsReleaseNotesStatusVisible));
    }

    private async Task LoadReleaseNotesAsync(bool forceRefresh = false)
    {
        if (IsLoadingReleaseNotes)
            return;

        if (!forceRefresh && RecentReleaseNotes.Count > 0)
            return;

        IsLoadingReleaseNotes = true;
        ReleaseNotesStatus = "正在加载最近版本发布说明...";

        try
        {
            var releases = await _releaseService.GetRecentReleasesAsync(3);
            RecentReleaseNotes.Clear();
            foreach (var release in releases)
                RecentReleaseNotes.Add(new ReleaseNoteItemViewModel(release));

            ReleaseNotesStatus = RecentReleaseNotes.Count == 0
                ? "暂时没有获取到发布说明，可前往 Releases 页面查看。"
                : string.Empty;
        }
        finally
        {
            IsLoadingReleaseNotes = false;
            OnPropertyChanged(nameof(HasReleaseNotes));
            OnPropertyChanged(nameof(IsReleaseNotesStatusVisible));
        }
    }

    private static string GetCurrentVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version
                      ?? Assembly.GetExecutingAssembly().GetName().Version;

        return version == null
            ? "未知"
            : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static void OpenUrl(string url)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // Browser launch is a convenience action; keep this page usable if the OS blocks it.
        }
    }

    [RelayCommand]
    private void BeginShortcutRecording(GlobalShortcutItemViewModel? item)
    {
        if (item == null)
            return;

        StopRecording(false);
        item.IsRecording = true;
        item.SetInfo("按下新的快捷键，Esc 取消。");
    }

    [RelayCommand]
    private void CaptureShortcutKey(InteractionBehaviors.KeyDownCommandContext? context)
    {
        if (context?.Parameter is not GlobalShortcutItemViewModel item || !item.IsRecording)
            return;

        var args = context.EventArgs;
        args.Handled = true;
        if (args.Key == Key.Escape)
        {
            item.IsRecording = false;
            item.ClearStatus();
            RefreshShortcutTexts();
            return;
        }

        if (GlobalShortcutParser.IsModifierOnlyKey(args.Key))
        {
            var modifierText = GlobalShortcutParser.FormatModifiers(args.KeyModifiers);
            item.SetInfo(string.IsNullOrWhiteSpace(modifierText)
                ? "按下修饰键后继续输入主键。"
                : $"继续按下主键... 当前: {modifierText}");
            item.ShortcutText = string.IsNullOrWhiteSpace(modifierText)
                ? "按下快捷键..."
                : $"{modifierText}+...";
            return;
        }

        if (!GlobalShortcutParser.TryCreateFromKeyEvent(args, out var gesture, out var error))
        {
            item.IsRecording = false;
            item.SetError(error);
            RefreshShortcutTexts();
            return;
        }

        var gestureText = GlobalShortcutParser.Format(gesture);
        var conflict = ShortcutItems.FirstOrDefault(x =>
            x != item &&
            string.Equals(
                GlobalShortcutParser.NormalizeText(SettingsManager.Settings.GlobalShortcuts.GetShortcut(x.Action)),
                gestureText, StringComparison.Ordinal));
        if (conflict != null)
        {
            item.IsRecording = false;
            item.SetError($"与“{conflict.DisplayName}”冲突。");
            RefreshShortcutTexts();
            return;
        }

        var candidate = BuildShortcutSettingsSnapshot();
        candidate.SetShortcut(item.Action, gestureText);
        var applyResult = _globalShortcutService.TryApplySettings(candidate);
        item.IsRecording = false;
        if (!applyResult.Success)
        {
            item.SetError(applyResult.Results.TryGetValue(item.Action, out var result)
                ? result.ErrorMessage
                : "保存失败。");
            RefreshShortcutTexts();
            ApplyRegistrationResults(_globalShortcutService.CurrentResults);
            return;
        }

        SettingsManager.Settings.GlobalShortcuts = candidate;
        SettingsManager.Save();
        RefreshShortcutTexts();
        ApplyRegistrationResults(applyResult.Results);
    }

    [RelayCommand]
    private void ClearShortcut(GlobalShortcutItemViewModel? item)
    {
        if (item == null)
            return;

        var candidate = BuildShortcutSettingsSnapshot();
        candidate.SetShortcut(item.Action, null);
        var applyResult = _globalShortcutService.TryApplySettings(candidate);
        if (!applyResult.Success)
        {
            item.SetError(applyResult.Results.TryGetValue(item.Action, out var result)
                ? result.ErrorMessage
                : "清空失败。");
            return;
        }

        SettingsManager.Settings.GlobalShortcuts = candidate;
        SettingsManager.Save();
        RefreshShortcutTexts();
        ApplyRegistrationResults(applyResult.Results);
    }

    private void RefreshShortcutTexts()
    {
        foreach (var item in ShortcutItems)
        {
            if (item.IsRecording)
                continue;

            item.ApplyShortcutText(GlobalShortcutParser.NormalizeText(
                SettingsManager.Settings.GlobalShortcuts.GetShortcut(item.Action)));
        }
    }

    private void ApplyRegistrationResults(
        IReadOnlyDictionary<GlobalShortcutAction, GlobalShortcutRegistrationResult> results)
    {
        foreach (var item in ShortcutItems)
        {
            if (item.IsRecording)
                continue;

            if (!results.TryGetValue(item.Action, out var result))
            {
                item.ClearStatus();
                continue;
            }

            if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                item.SetError(result.ErrorMessage);
                continue;
            }

            item.ClearStatus();
        }
    }

    private void StopRecording(bool clearStatus)
    {
        foreach (var shortcutItem in ShortcutItems)
        {
            shortcutItem.IsRecording = false;
            if (clearStatus)
                shortcutItem.ClearStatus();
        }
    }

    private GlobalShortcutSettings BuildShortcutSettingsSnapshot()
    {
        var snapshot = SettingsManager.Settings.GlobalShortcuts.Clone();
        snapshot.EnableGlobalShortcuts = EnableGlobalShortcuts;
        return snapshot;
    }
}

public sealed class ReleaseNoteItemViewModel(GitHubReleaseInfo release)
{
    public string Title { get; } = release.Title;
    public string PublishedAt { get; } = release.PublishedAt;
    public string Summary { get; } = release.Summary;
}
