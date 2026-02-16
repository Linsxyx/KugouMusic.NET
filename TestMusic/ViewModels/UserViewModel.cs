using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KuGou.Net.Clients;

namespace TestMusic.ViewModels;

public partial class UserViewModel(UserClient userClient, AuthClient authClient) : PageViewModelBase
{
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string? _userAvatar;
    [ObservableProperty] private string _userId = "";
    [ObservableProperty] private string _userName = "加载中...";
    [ObservableProperty] private string _vipStatus = "未开通";


    public override string DisplayName => "用户中心";
    public override string Icon => "/Assets/user-svgrepo-com.svg";

    public async Task LoadUserInfoAsync()
    {
        IsLoading = true;
        try
        {
            var userInfo = await userClient.GetUserInfoAsync();
            if (userInfo != null)
            {
                UserName = userInfo.Name;
                UserAvatar = string.IsNullOrWhiteSpace(userInfo.Pic) ? null : userInfo.Pic;
            }

            var vipInfo = await userClient.GetVipInfoAsync();
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
        authClient.LogOutAsync();
        LogoutRequested?.Invoke();
        await Task.CompletedTask;
    }

    public event Action? LogoutRequested;
}