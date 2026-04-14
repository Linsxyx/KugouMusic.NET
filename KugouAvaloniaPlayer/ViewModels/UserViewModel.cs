using System;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using KuGou.Net.Clients;
using KugouAvaloniaPlayer.Controls;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.Services;
using SukiUI;
using SukiUI.Dialogs;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class UserViewModel : PageViewModelBase
{
    private const string LyricTargetMain = "歌词";
    private const string LyricTargetTranslation = "歌词翻译";
    private const string LyricColorModeDefault = "默认";
    private const string LyricColorModeCustom = "自定义";

    private readonly AuthClient _authClient;
    private readonly ISukiDialogManager _dialogManager;
    private readonly EqSettingsViewModel _eqSettingsViewModel;
    private readonly UserClient _userClient;
    private bool _isInitializingLyricColorEditor;

    [ObservableProperty] private bool _autoCheckUpdate;
    [ObservableProperty] private bool _enableSurround;
    [ObservableProperty] private bool _isCheckingUpdate;
    [ObservableProperty] private bool _isLoading = true;

    [ObservableProperty] private string _lyricColorHexInput = "#FFFFFFFF";
    [ObservableProperty] private string _selectedLyricColorMode = LyricColorModeDefault;
    [ObservableProperty] private string _selectedLyricColorTarget = LyricTargetMain;

    [ObservableProperty] private CloseBehavior _selectedCloseBehavior;
    [ObservableProperty] private string _selectedEQPreset;
    [ObservableProperty] private string _selectedQuality;
    [ObservableProperty] private string? _userAvatar;
    [ObservableProperty] private string _userId = "";
    [ObservableProperty] private string _userName = "加载中...";
    [ObservableProperty] private string _vipStatus = "未开通";

    public UserViewModel(PlayerViewModel player, UserClient userClient, AuthClient authClient,
        ISukiDialogManager dialogManager, EqSettingsViewModel eqSettingsViewModel)
    {
        _userClient = userClient;
        _authClient = authClient;
        _dialogManager = dialogManager;
        _eqSettingsViewModel = eqSettingsViewModel;

        Player = player;
        SelectedCloseBehavior = SettingsManager.Settings.CloseBehavior;
        SelectedQuality = SettingsManager.Settings.MusicQuality;
        AutoCheckUpdate = SettingsManager.Settings.AutoCheckUpdate;
        EQPresetOptions = ["原声", "流行", "摇滚", "爵士", "古典", "嘻哈", "布鲁斯", "电子音乐", "金属", "自定义"];

        var preset = SettingsManager.Settings.EQPreset;
        SelectedEQPreset = Array.Exists(EQPresetOptions, x => x == preset) ? preset : "原声";

        EnableSurround = SettingsManager.Settings.EnableSurround;
        LoadLyricColorEditorFromSettings();
    }

    public string[] EQPresetOptions { get; }

    public string[] LyricColorTargetOptions { get; } = [LyricTargetMain, LyricTargetTranslation];

    public string[] LyricColorModeOptions { get; } = [LyricColorModeDefault, LyricColorModeCustom];

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

    private PlayerViewModel Player { get; }

    public override string DisplayName => "用户中心";
    public override string Icon => "avares://KugouAvaloniaPlayer/Assets/default_singer.png";

    public CloseBehavior[] AvailableCloseBehaviors { get; } = Enum.GetValues<CloseBehavior>();

    public string[] QualityOptions { get; } = { "128", "320", "flac", "high" };

    public bool IsLyricColorCustomMode => SelectedLyricColorMode == LyricColorModeCustom;

    public IBrush LyricColorPreviewBrush => new SolidColorBrush(ParseColorOrDefault(LyricColorHexInput, Colors.Transparent));

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
    private void PickLyricPaletteColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return;
        LyricColorHexInput = hex;
        ApplyLyricColorHex();
    }

    [RelayCommand]
    private void ApplyLyricColorHex()
    {
        var normalized = NormalizeColorHex(LyricColorHexInput);
        if (normalized == null) return;

        // 输入颜色时自动进入自定义模式
        if (!IsLyricColorCustomMode)
        {
            _isInitializingLyricColorEditor = true;
            SelectedLyricColorMode = LyricColorModeCustom;
            _isInitializingLyricColorEditor = false;
            SetCurrentTargetCustomEnabled(true);
            OnPropertyChanged(nameof(IsLyricColorCustomMode));
        }

        SetCurrentTargetCustomColor(normalized);
        LyricColorHexInput = normalized;
        OnPropertyChanged(nameof(LyricColorPreviewBrush));
        SettingsManager.Save();
        NotifyDesktopLyricColorChanged();
    }

    public event Action? CheckForUpdateRequested;

    partial void OnSelectedCloseBehaviorChanged(CloseBehavior value)
    {
        SettingsManager.Settings.CloseBehavior = value;
        SettingsManager.Save();
    }

    partial void OnSelectedQualityChanged(string value)
    {
        SettingsManager.Settings.MusicQuality = value;
        Player.MusicQuality = value;
        SettingsManager.Save();
    }

    partial void OnAutoCheckUpdateChanged(bool value)
    {
        SettingsManager.Settings.AutoCheckUpdate = value;
        SettingsManager.Save();
    }

    partial void OnSelectedEQPresetChanged(string value)
    {
        SettingsManager.Settings.EQPreset = value;
        SettingsManager.Save();
        Player.UpdateAudioEffects(value, EnableSurround);
    }

    partial void OnEnableSurroundChanged(bool value)
    {
        SettingsManager.Settings.EnableSurround = value;
        SettingsManager.Save();
        Player.UpdateAudioEffects(SelectedEQPreset, value);
    }

    partial void OnSelectedLyricColorTargetChanged(string value)
    {
        LoadLyricColorEditorFromSettings();
    }

    partial void OnSelectedLyricColorModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsLyricColorCustomMode));
        if (_isInitializingLyricColorEditor) return;

        SetCurrentTargetCustomEnabled(value == LyricColorModeCustom);
        SettingsManager.Save();
        NotifyDesktopLyricColorChanged();
    }

    partial void OnLyricColorHexInputChanged(string value)
    {
        OnPropertyChanged(nameof(LyricColorPreviewBrush));
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

    private void LoadLyricColorEditorFromSettings()
    {
        _isInitializingLyricColorEditor = true;

        if (IsEditingMainLyricColor())
        {
            SelectedLyricColorMode = SettingsManager.Settings.DesktopLyricUseCustomMainColor
                ? LyricColorModeCustom
                : LyricColorModeDefault;
            LyricColorHexInput = SettingsManager.Settings.DesktopLyricCustomMainColor;
        }
        else
        {
            SelectedLyricColorMode = SettingsManager.Settings.DesktopLyricUseCustomTranslationColor
                ? LyricColorModeCustom
                : LyricColorModeDefault;
            LyricColorHexInput = SettingsManager.Settings.DesktopLyricCustomTranslationColor;
        }

        _isInitializingLyricColorEditor = false;
        OnPropertyChanged(nameof(IsLyricColorCustomMode));
        OnPropertyChanged(nameof(LyricColorPreviewBrush));
    }

    private bool IsEditingMainLyricColor()
    {
        return SelectedLyricColorTarget == LyricTargetMain;
    }

    private void SetCurrentTargetCustomEnabled(bool enabled)
    {
        if (IsEditingMainLyricColor())
            SettingsManager.Settings.DesktopLyricUseCustomMainColor = enabled;
        else
            SettingsManager.Settings.DesktopLyricUseCustomTranslationColor = enabled;
    }

    private void SetCurrentTargetCustomColor(string normalizedHex)
    {
        if (IsEditingMainLyricColor())
            SettingsManager.Settings.DesktopLyricCustomMainColor = normalizedHex;
        else
            SettingsManager.Settings.DesktopLyricCustomTranslationColor = normalizedHex;
    }

    private static string? NormalizeColorHex(string? colorText)
    {
        if (string.IsNullOrWhiteSpace(colorText)) return null;
        return Color.TryParse(colorText.Trim(), out var parsed) ? parsed.ToString() : null;
    }

    private static Color ParseColorOrDefault(string? colorText, Color fallback)
    {
        return Color.TryParse(colorText, out var parsed) ? parsed : fallback;
    }

    private static void NotifyDesktopLyricColorChanged()
    {
        WeakReferenceMessenger.Default.Send(new DesktopLyricColorSettingsChangedMessage(
            SettingsManager.Settings.DesktopLyricUseCustomMainColor,
            SettingsManager.Settings.DesktopLyricCustomMainColor,
            SettingsManager.Settings.DesktopLyricUseCustomTranslationColor,
            SettingsManager.Settings.DesktopLyricCustomTranslationColor));
    }
}
