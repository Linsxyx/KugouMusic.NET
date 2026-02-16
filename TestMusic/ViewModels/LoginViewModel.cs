using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KuGou.Net.Clients;

namespace TestMusic.ViewModels;

public partial class LoginViewModel(AuthClient authClient, DeviceClient deviceClient) : ObservableObject
{
    [ObservableProperty] private string _code = "";
    [ObservableProperty] private int _countdown;
    [ObservableProperty] private bool _isLoggingIn;
    [ObservableProperty] private bool _isSendingCode;
    [ObservableProperty] private string _mobile = "";
    [ObservableProperty] private string _statusMessage = "";

    [RelayCommand]
    private async Task SendCode()
    {
        if (string.IsNullOrWhiteSpace(Mobile) || Mobile.Length != 11)
        {
            StatusMessage = "请输入正确的手机号";
            return;
        }

        IsSendingCode = true;
        StatusMessage = "正在发送验证码...";

        try
        {
            var result = await authClient.SendCodeAsync(Mobile);
            if (result.TryGetProperty("status", out var statusEl) && statusEl.GetInt32() == 1)
            {
                StatusMessage = "验证码已发送";
                StartCountdown();
            }
            else
            {
                var msg = result.TryGetProperty("msg", out var msgEl) ? msgEl.GetString() : "发送失败";
                StatusMessage = $"发送失败: {msg}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"发送出错: {ex.Message}";
        }
        finally
        {
            IsSendingCode = false;
        }
    }

    [RelayCommand]
    private async Task Login()
    {
        if (string.IsNullOrWhiteSpace(Mobile) || Mobile.Length != 11)
        {
            StatusMessage = "请输入正确的手机号";
            return;
        }

        if (string.IsNullOrWhiteSpace(Code))
        {
            StatusMessage = "请输入验证码";
            return;
        }

        IsLoggingIn = true;
        StatusMessage = "正在登录...";

        try
        {
            var result = await authClient.LoginByMobileAsync(Mobile, Code);
            if (result.TryGetProperty("status", out var statusEl) && statusEl.GetInt32() == 1)
            {
                StatusMessage = "登录成功";

                // 后台初始化设备
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await deviceClient.InitDeviceAsync();
                    }
                    catch
                    {
                        // 忽略设备初始化错误
                    }
                });

                LoginSuccess?.Invoke();
            }
            else
            {
                var msg = result.TryGetProperty("msg", out var msgEl) ? msgEl.GetString() : "登录失败";
                StatusMessage = $"登录失败: {msg}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"登录出错: {ex.Message}";
        }
        finally
        {
            IsLoggingIn = false;
        }
    }

    private void StartCountdown()
    {
        Countdown = 60;
        Task.Run(async () =>
        {
            while (Countdown > 0)
            {
                await Task.Delay(1000);
                Countdown--;
            }
        });
    }

    public event Action? LoginSuccess;
}