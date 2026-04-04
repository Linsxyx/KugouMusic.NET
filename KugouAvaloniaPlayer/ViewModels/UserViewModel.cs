using System;
using System.Threading.Tasks;
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
    private readonly AuthClient _authClient;
    private readonly ISukiDialogManager _dialogManager;
    private readonly EqSettingsViewModel _eqSettingsViewModel;
    private readonly UserClient _userClient;
    [ObservableProperty] private bool _autoCheckUpdate;

    [ObservableProperty] private bool _enableSurround;
    [ObservableProperty] private bool _isCheckingUpdate;
    [ObservableProperty] private bool _isLoading = true;

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
    }

    public string[] EQPresetOptions { get; }

    private PlayerViewModel Player { get; }


    public override string DisplayName => "用户中心";
    public override string Icon => "avares://KugouAvaloniaPlayer/Assets/default_singer.png";


    public CloseBehavior[] AvailableCloseBehaviors { get; } = Enum.GetValues<CloseBehavior>();


    public string[] QualityOptions { get; } = { "128", "320", "flac", "high" };


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

    public void SetCheckingUpdateState(bool isChecking)
    {
        IsCheckingUpdate = isChecking;
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

    [RelayCommand]
    private void OpenEqSettings()
    {
        var EqSettings = new EqSettingsControl
        {
            DataContext = _eqSettingsViewModel
        };

        _dialogManager.CreateDialog()
            .WithContent(EqSettings)
            .WithActionButton("确定", _ => { }, true)
            .TryShow();
    }
}