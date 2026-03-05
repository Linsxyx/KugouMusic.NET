using System;
using System.Threading.Tasks;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KuGou.Net.Clients;
using KugouAvaloniaPlayer.Services;
using SukiUI;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class UserViewModel : PageViewModelBase
{
    private readonly AuthClient _authClient;
    private readonly UserClient _userClient;
    [ObservableProperty] private bool _isLoading = true;

    [ObservableProperty] private CloseBehavior _selectedCloseBehavior;

    [ObservableProperty] private string _selectedQuality;
    [ObservableProperty] private string? _userAvatar;
    [ObservableProperty] private string _userId = "";
    [ObservableProperty] private string _userName = "加载中...";
    [ObservableProperty] private string _vipStatus = "未开通";

    public UserViewModel(PlayerViewModel player, UserClient userClient, AuthClient authClient)
    {
        _userClient = userClient;
        _authClient = authClient;
        Player = player;
        SelectedCloseBehavior = SettingsManager.Settings.CloseBehavior;
        SelectedQuality = SettingsManager.Settings.MusicQuality;
    }

    private PlayerViewModel Player { get; }


    public override string DisplayName => "用户中心";
    public override string Icon => "/Assets/user-svgrepo-com.svg";


    public CloseBehavior[] AvailableCloseBehaviors { get; } = Enum.GetValues<CloseBehavior>();


    public string[] QualityOptions { get; } = { "128", "320", "flac", "high" };

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
        LogoutRequested?.Invoke();
        await Task.CompletedTask;
    }

    public event Action? LogoutRequested;

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
    
    
    public bool IsDarkMode
    {
        get => SukiTheme.GetInstance().ActiveBaseTheme == ThemeVariant.Dark;
        set
        {
            SukiTheme.GetInstance().ChangeBaseTheme(value ? ThemeVariant.Dark : ThemeVariant.Light);
            OnPropertyChanged();
        }
    }
}