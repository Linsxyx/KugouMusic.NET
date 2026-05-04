using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Clients;
using KugouAvaloniaPlayer.Models;
using Microsoft.Extensions.Logging;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class LoginViewModel(LoginClient authClient, RegisterClient deviceClient, ILogger<LoginViewModel> logger)
    : ObservableObject
{
    [ObservableProperty] private string _code = "";
    [ObservableProperty] private int _countdown;
    [ObservableProperty] private bool _isLoggingIn;
    [ObservableProperty] private bool _isQrExpired;

    [ObservableProperty] private bool _isQrLoginSelected;
    [ObservableProperty] private bool _isSendingCode;
    [ObservableProperty] private string _mobile = "";
    [ObservableProperty] private string? _qrCodeImageUrl;

    private string? _qrCodeKey;
    private CancellationTokenSource? _qrPollingCts;
    [ObservableProperty] private string _qrStatusMessage = "请使用酷狗音乐概念版App扫码";
    [ObservableProperty] private string _statusMessage = "";

    partial void OnIsQrLoginSelectedChanged(bool value)
    {
        if (value)
            _ = RefreshQrCode();
        else
            StopQrPolling();
    }

    [RelayCommand]
    public async Task RefreshQrCode()
    {
        StopQrPolling();
        QrStatusMessage = "正在获取二维码...";
        QrCodeImageUrl = null;
        IsQrExpired = false;

        try
        {
            var qr = await authClient.GetQrCodeAsync();
            if (qr != null && !string.IsNullOrEmpty(qr.Qrcode))
            {
                _qrCodeKey = qr.Qrcode;
                QrCodeImageUrl = qr.QrcodeImg;
                QrStatusMessage = "请使用酷狗音乐App扫码";
                StartQrPolling();
            }
            else
            {
                QrStatusMessage = "获取二维码失败，请重试";
                IsQrExpired = true;
            }
        }
        catch (Exception ex)
        {
            QrStatusMessage = $"获取二维码出错: {ex.Message}";
            IsQrExpired = true;
        }
    }

    private void StartQrPolling()
    {
        _qrPollingCts = new CancellationTokenSource();
        var token = _qrPollingCts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(2000, token);
                if (token.IsCancellationRequested) break;

                if (string.IsNullOrEmpty(_qrCodeKey)) continue;

                try
                {
                    var status = await authClient.CheckQrStatusAsync(_qrCodeKey);

                    if (status?.QrStatus == QrLoginStatus.WaitingForScan)
                    {
                        Dispatcher.UIThread.Post(() => QrStatusMessage = "请使用酷狗音乐App扫码");
                    }
                    else if (status?.QrStatus == QrLoginStatus.WaitingForConfirm)
                    {
                        Dispatcher.UIThread.Post(() => QrStatusMessage = "扫码成功，请在手机上确认");
                    }
                    else if (status?.QrStatus == QrLoginStatus.Success)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            QrStatusMessage = "登录成功";
                            StopQrPolling();

                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await deviceClient.InitDeviceAsync();
                                    await authClient.RefreshSessionAsync();
                                }
                                catch (Exception ex)
                                {
                                    logger.LogError($"设备验证失败: {ex.Message}");
                                }

                                Dispatcher.UIThread.Post(() =>
                                {
                                    WeakReferenceMessenger.Default.Send(new AuthStateChangedMessage(true));
                                });
                            });
                        });
                        break;
                    }
                    else if (status?.QrStatus == QrLoginStatus.Expired)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            QrStatusMessage = "二维码已过期，请点击刷新";
                            IsQrExpired = true;
                        });
                        StopQrPolling();
                        break;
                    }
                }
                catch
                {
                    logger.LogError("轮询出错");
                }
            }
        }, token);
    }

    public void StopQrPolling()
    {
        if (_qrPollingCts != null)
        {
            _qrPollingCts.Cancel();
            _qrPollingCts.Dispose();
            _qrPollingCts = null;
        }
    }

    [RelayCommand]
    private void StopQrPollingOnUnload()
    {
        StopQrPolling();
    }

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
            if (result is not null && result.Status == 1)
            {
                StatusMessage = "验证码已发送";
                StartCountdown();
            }
            else
            {
                var msg = result?.ErrorCode;
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
            if (result is not null && result.Status == 1)
            {
                StatusMessage = "登录成功";

                try
                {
                    await deviceClient.InitDeviceAsync();
                }
                catch (Exception ex)
                {
                    logger.LogError($"设备初始化失败: {ex.Message}");
                }

                WeakReferenceMessenger.Default.Send(new AuthStateChangedMessage(true));
            }
            else
            {
                StatusMessage = "登录失败，若没有账号请先在酷狗音乐概念版App注册";
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
}