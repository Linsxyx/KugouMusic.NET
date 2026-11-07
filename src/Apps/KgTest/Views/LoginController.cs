using KuGou.Net.Abstractions.Models;
using KgTest.Graphics;
using KgTest.Models;
using KgTest.Services;
using KgTest.Tui;

namespace KgTest.Views;

// 登录状态机与账号视图：支持非阻塞扫码登录、手机验证码登录、60s 倒计时与多账号选择。

internal enum LoginTab
{
    Qr,
    Sms
}

internal enum SmsField
{
    Mobile,
    Code,
    SendButton,
    LoginButton,
    AccountList
}

internal sealed class LoginController(
    TerminalKugouClients clients,
    ImageCache images,
    Action requestRender,
    Func<string, Task> onLoggedIn)
    : IDisposable
{
    private CancellationTokenSource? _qrCts;
    private int _qrVersion;
    private string? _qrKey;
    private TerminalImage? _qrImage;
    private DateTimeOffset _sendCooldownUntil = DateTimeOffset.MinValue;
    private bool _sending;
    private bool _submitting;
    private int _accountIndex;

    public LoginTab Tab { get; private set; } = LoginTab.Qr;
    public SmsField Field { get; private set; } = SmsField.Mobile;
    public TextField MobileInput { get; } = new("输入 11 位手机号", 11, char.IsDigit);
    public TextField CodeInput { get; } = new("输入短信验证码", 8, char.IsDigit);
    public List<LoginAccountInfo> Accounts { get; } = [];
    public string QrStatus { get; private set; } = "按 Enter 或 r 获取二维码";
    public string SmsStatus { get; private set; } = "输入手机号后按 Tab 切换到发送验证码";
    public bool IsQrExpired { get; private set; }
    public bool IsEditingText => Tab == LoginTab.Sms && Field is SmsField.Mobile or SmsField.Code;

    public int CooldownSeconds =>
        Math.Max(0, (int)Math.Ceiling((_sendCooldownUntil - DateTimeOffset.UtcNow).TotalSeconds));

    public void EnsureStarted()
    {
        if (Tab == LoginTab.Qr && _qrImage == null && _qrCts == null)
        {
            _ = RefreshQrAsync();
        }
    }

    public void StopPolling()
    {
        _qrVersion++;
        _qrCts?.Cancel();
        _qrCts?.Dispose();
        _qrCts = null;
    }

    public async Task<bool> HandleKeyAsync(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Tab)
        {
            AdvanceFocus((key.Modifiers & ConsoleModifiers.Shift) != 0 ? -1 : 1);
            return true;
        }

        if (Tab == LoginTab.Qr)
        {
            switch (key.Key)
            {
                case ConsoleKey.D2:
                    SwitchTab(LoginTab.Sms);
                    return true;
                case ConsoleKey.Enter:
                case ConsoleKey.R:
                    await RefreshQrAsync();
                    return true;
            }

            return false;
        }

        // 短信标签页
        if (Field is SmsField.Mobile or SmsField.Code)
        {
            if (key.Key == ConsoleKey.UpArrow)
            {
                AdvanceFocus(-1);
                return true;
            }

            if (key.Key == ConsoleKey.DownArrow)
            {
                AdvanceFocus(1);
                return true;
            }

            if (key.Key == ConsoleKey.Enter)
            {
                if (Field == SmsField.Mobile)
                {
                    await SendSmsCodeAsync();
                }
                else
                {
                    await SubmitSmsAsync(null);
                }

                return true;
            }

            var target = Field == SmsField.Mobile ? MobileInput : CodeInput;
            var before = target.Text;
            if (target.HandleKey(key))
            {
                if (Field == SmsField.Mobile && target.Text != before)
                {
                    Accounts.Clear();
                }

                return true;
            }

            return false;
        }

        switch (key.Key)
        {
            case ConsoleKey.D1:
                SwitchTab(LoginTab.Qr);
                return true;
            case ConsoleKey.UpArrow:
            case ConsoleKey.K:
                if (Field == SmsField.AccountList && _accountIndex > 0)
                {
                    _accountIndex--;
                }
                else
                {
                    AdvanceFocus(-1);
                }

                return true;
            case ConsoleKey.DownArrow:
            case ConsoleKey.J:
                if (Field == SmsField.AccountList && _accountIndex + 1 < Accounts.Count)
                {
                    _accountIndex++;
                }
                else
                {
                    AdvanceFocus(1);
                }

                return true;
            case ConsoleKey.Enter:
            case ConsoleKey.Spacebar:
                await ActivateFieldAsync();
                return true;
        }

        return false;
    }

    public void SwitchTab(LoginTab tab)
    {
        Tab = tab;
        if (tab == LoginTab.Qr)
        {
            EnsureStarted();
        }
        else
        {
            StopPolling();
        }
    }

    public async Task RefreshQrAsync()
    {
        StopPolling();
        var version = ++_qrVersion;
        _qrImage = null;
        _qrKey = null;
        IsQrExpired = false;
        QrStatus = "正在获取登录二维码...";
        requestRender();

        try
        {
            var qr = await clients.Login.GetQrCodeAsync();
            if (version != _qrVersion)
            {
                return;
            }

            if (qr == null || string.IsNullOrWhiteSpace(qr.Qrcode))
            {
                QrStatus = "二维码获取失败，按 Enter 重试";
                IsQrExpired = true;
                requestRender();
                return;
            }

            _qrKey = qr.Qrcode;
            _qrImage = images.FromBase64($"qr:{qr.Qrcode}", qr.QrcodeImg, crisp: true);
            QrStatus = "请使用酷狗音乐 App 扫码登录";
            requestRender();
            StartPolling(version, qr.Qrcode);
        }
        catch (Exception ex)
        {
            QrStatus = $"获取二维码出错：{ex.Message}";
            IsQrExpired = true;
            requestRender();
        }
    }

    private void StartPolling(int version, string key)
    {
        var cts = new CancellationTokenSource();
        _qrCts = cts;
        _ = PollQrLoopAsync(version, key, cts.Token);
    }

    private async Task PollQrLoopAsync(int version, string key, CancellationToken token)
    {
        try
        {
            for (var i = 0; i < 90 && !token.IsCancellationRequested; i++)
            {
                await Task.Delay(2000, token);
                if (token.IsCancellationRequested || version != _qrVersion)
                {
                    return;
                }

                QrLoginStatusResponse? status;
                try
                {
                    status = await clients.Login.CheckQrStatusAsync(key);
                }
                catch
                {
                    continue;
                }

                if (version != _qrVersion || status == null)
                {
                    continue;
                }

                switch (status.QrStatus)
                {
                    case QrLoginStatus.WaitingForScan:
                        QrStatus = "请使用酷狗音乐 App 扫码登录";
                        break;
                    case QrLoginStatus.WaitingForConfirm:
                        QrStatus = $"已扫码{(string.IsNullOrWhiteSpace(status.Nickname) ? "" : $"（{status.Nickname}）")}，请在手机上确认";
                        break;
                    case QrLoginStatus.Expired:
                        QrStatus = "二维码已过期，按 Enter 或 r 刷新";
                        IsQrExpired = true;
                        StopPolling();
                        requestRender();
                        return;
                    case QrLoginStatus.Success when status.IsSuccess:
                        QrStatus = "授权成功，正在初始化会话...";
                        StopPolling();
                        requestRender();
                        await FinalizeLoginAsync(status.Nickname);
                        return;
                }

                requestRender();
            }

            if (version == _qrVersion)
            {
                QrStatus = "二维码已超时，按 Enter 或 r 重新获取";
                IsQrExpired = true;
                requestRender();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task SendSmsCodeAsync()
    {
        if (_sending || CooldownSeconds > 0)
        {
            return;
        }

        var mobile = MobileInput.Text.Trim();
        if (mobile.Length != 11)
        {
            SmsStatus = "请输入 11 位中国大陆手机号";
            Field = SmsField.Mobile;
            return;
        }

        _sending = true;
        SmsStatus = "正在发送验证码...";
        requestRender();

        try
        {
            var res = await clients.Login.SendCodeAsync(mobile);
            if (res?.Status == 1)
            {
                _sendCooldownUntil = DateTimeOffset.UtcNow.AddSeconds(60);
                SmsStatus = "验证码已发送，请查收短信";
                Field = SmsField.Code;
            }
            else
            {
                SmsStatus = $"发送失败（错误码 {res?.ErrorCode?.ToString() ?? "未知"}）";
            }
        }
        catch (Exception ex)
        {
            SmsStatus = $"发送出错：{ex.Message}";
        }
        finally
        {
            _sending = false;
            requestRender();
        }
    }

    private async Task SubmitSmsAsync(string? userId)
    {
        if (_submitting)
        {
            return;
        }

        var mobile = MobileInput.Text.Trim();
        var code = CodeInput.Text.Trim();
        if (mobile.Length != 11)
        {
            SmsStatus = "请输入 11 位手机号";
            Field = SmsField.Mobile;
            return;
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            SmsStatus = "请输入短信验证码";
            Field = SmsField.Code;
            return;
        }

        _submitting = true;
        SmsStatus = userId == null ? "正在验证登录..." : "正在使用所选账号登录...";
        requestRender();

        try
        {
            var res = await clients.Login.LoginByMobileAsync(mobile, code, userId);
            if (res?.Status == 1)
            {
                Accounts.Clear();
                SmsStatus = "登录成功，正在初始化设备...";
                requestRender();
                await FinalizeLoginAsync(null);
            }
            else if (res?.RequiresUserSelection == true && res.Data?.InfoList is { Count: > 0 } list)
            {
                Accounts.Clear();
                Accounts.AddRange(list);
                _accountIndex = 0;
                Field = SmsField.AccountList;
                SmsStatus = "该手机号绑定了多个账号，请用 ↑/↓ 选择后按 Enter";
            }
            else
            {
                var detail = res?.FailureMessage;
                SmsStatus = string.IsNullOrWhiteSpace(detail)
                    ? $"登录失败（错误码 {res?.ErrorCode?.ToString() ?? "未知"}）"
                    : $"登录失败：{detail}";
            }
        }
        catch (Exception ex)
        {
            SmsStatus = $"登录出错：{ex.Message}";
        }
        finally
        {
            _submitting = false;
            requestRender();
        }
    }

    private async Task FinalizeLoginAsync(string? nickname)
    {
        try
        {
            await clients.Device.InitDeviceAsync();
            if (Tab == LoginTab.Qr)
            {
                await clients.Login.RefreshSessionAsync();
            }
        }
        catch
        {
            // 设备或保活刷新失败不阻断已写入的 Token。
        }

        await onLoggedIn(nickname ?? "");
    }

    private async Task ActivateFieldAsync()
    {
        switch (Field)
        {
            case SmsField.SendButton:
                await SendSmsCodeAsync();
                break;
            case SmsField.LoginButton:
                await SubmitSmsAsync(null);
                break;
            case SmsField.AccountList when _accountIndex >= 0 && _accountIndex < Accounts.Count:
                await SubmitSmsAsync(Accounts[_accountIndex].UserId.ToString());
                break;
        }
    }

    private void AdvanceFocus(int delta)
    {
        if (Tab == LoginTab.Qr)
        {
            SwitchTab(LoginTab.Sms);
            Field = SmsField.Mobile;
            return;
        }

        var order = Accounts.Count > 0
            ? new[] { SmsField.Mobile, SmsField.Code, SmsField.SendButton, SmsField.LoginButton, SmsField.AccountList }
            : new[] { SmsField.Mobile, SmsField.Code, SmsField.SendButton, SmsField.LoginButton };

        var index = Array.IndexOf(order, Field);
        var next = index + delta;
        if (next < 0 || next >= order.Length)
        {
            SwitchTab(LoginTab.Qr);
            return;
        }

        Field = order[next];
    }

    /// <summary>
    ///     渲染账号与登录面板（内嵌在账号页或弹窗中）。
    /// </summary>
    public void Render(Canvas canvas, Rect area, TerminalUserProfile profile, Theme theme, bool focused)
    {
        var y = area.Y;
        RenderProfileCard(canvas, new Rect(area.X, y, area.W, 6), profile, theme);
        y += 7;

        if (y >= area.Bottom)
        {
            return;
        }

        RenderTabs(canvas, area.X, y, area.W, theme, focused);
        y += 2;

        var body = new Rect(area.X, y, area.W, Math.Max(0, area.Bottom - y));
        if (Tab == LoginTab.Qr)
        {
            RenderQrTab(canvas, body, theme);
        }
        else
        {
            RenderSmsTab(canvas, body, theme, focused);
        }
    }

    public async Task<bool> HandleMouseAsync(MouseEvent mouse, Rect area)
    {
        if (!area.Contains(mouse.X, mouse.Y) || mouse.Kind is not (MouseKind.LeftDown or MouseKind.LeftDoubleClick))
        {
            return false;
        }

        var tabY = area.Y + 7;
        if (mouse.Y == tabY)
        {
            var relX = mouse.X - area.X;
            if (relX is >= 0 and < 14)
            {
                SwitchTab(LoginTab.Qr);
                return true;
            }

            if (relX is >= 14 and < 36)
            {
                SwitchTab(LoginTab.Sms);
                Field = SmsField.Mobile;
                return true;
            }
        }

        var bodyY = area.Y + 9;
        if (Tab == LoginTab.Qr)
        {
            if (mouse.Y >= bodyY)
            {
                await RefreshQrAsync();
                return true;
            }

            return false;
        }

        var fieldWidth = Math.Clamp(area.W - 14, 16, 36);
        var inputX = area.X + 10;
        if (mouse.Y == bodyY && mouse.X >= inputX && mouse.X < inputX + fieldWidth)
        {
            Field = SmsField.Mobile;
            MobileInput.SetCursorFromVisualColumn(mouse.X - inputX, fieldWidth);
            return true;
        }

        if (mouse.Y == bodyY + 2 && mouse.X >= inputX && mouse.X < inputX + fieldWidth)
        {
            Field = SmsField.Code;
            CodeInput.SetCursorFromVisualColumn(mouse.X - inputX, fieldWidth);
            return true;
        }

        if (mouse.Y == bodyY + 4 && mouse.X >= inputX)
        {
            var rel = mouse.X - inputX;
            if (rel < 15)
            {
                Field = SmsField.SendButton;
                await SendSmsCodeAsync();
                return true;
            }

            if (rel is >= 15 and < 32)
            {
                Field = SmsField.LoginButton;
                await SubmitSmsAsync(null);
                return true;
            }
        }

        var accountsStartY = bodyY + 9;
        if (Accounts.Count > 0 && mouse.Y >= accountsStartY && mouse.Y < accountsStartY + Accounts.Count)
        {
            Field = SmsField.AccountList;
            _accountIndex = mouse.Y - accountsStartY;
            if (mouse.Kind == MouseKind.LeftDoubleClick)
            {
                await SubmitSmsAsync(Accounts[_accountIndex].UserId.ToString());
            }

            return true;
        }

        return false;
    }

    private void RenderProfileCard(Canvas canvas, Rect area, TerminalUserProfile profile, Theme theme)
    {
        canvas.Box(area, theme.BorderStyle, "当前会话", theme.MutedStyle);
        var inner = area.Inset(2, 1);
        if (inner.IsEmpty)
        {
            return;
        }

        var avatarRect = new Rect(inner.X, inner.Y, 8, Math.Min(4, inner.H));
        var avatar = images.Get(profile.AvatarUrl);
        if (avatar != null && canvas.ImageMode != TerminalImageMode.Off)
        {
            canvas.Image("avatar", avatarRect, avatar);
        }
        else
        {
            canvas.Box(avatarRect, theme.FaintStyle);
            canvas.TextAligned(avatarRect, avatarRect.Y + avatarRect.H / 2, profile.IsLoggedIn ? "USER" : "GUEST",
                theme.MutedStyle, 1);
        }

        var textX = avatarRect.Right + 2;
        var textW = Math.Max(0, inner.Right - textX);
        var badge = profile.IsLoggedIn ? "● 已登录" : "○ 未登录";
        var badgeStyle = new CellStyle(profile.IsLoggedIn ? theme.Playing : theme.Warning, Attr: TextAttr.Bold);
        canvas.Spans(textX, inner.Y, textW,
            (profile.Name, theme.Bold),
            ("  ", theme.TextStyle),
            (badge, badgeStyle));

        canvas.Text(textX, inner.Y + 1,
            $"UserId: {profile.UserId}   VIP: {(profile.VipType is "0" or "" ? "普通用户" : profile.VipType)}",
            theme.MutedStyle, textW);
        canvas.Text(textX, inner.Y + 2,
            "独立会话 KgTestSession.json · V 领取/升级今日 VIP · L 退出登录",
            theme.FaintStyle, textW);
    }

    private void RenderTabs(Canvas canvas, int x, int y, int width, Theme theme, bool focused)
    {
        var qrActive = Tab == LoginTab.Qr;
        var qrStyle = qrActive
            ? new CellStyle(theme.Text, focused ? theme.SelectionBg : theme.SelectionDimBg, TextAttr.Bold)
            : theme.MutedStyle;
        var smsStyle = !qrActive
            ? new CellStyle(theme.Text, focused ? theme.SelectionBg : theme.SelectionDimBg, TextAttr.Bold)
            : theme.MutedStyle;

        canvas.Spans(x, y, width,
            ("  1 扫码登录  ", qrStyle),
            ("  ", theme.TextStyle),
            ("  2 手机验证码登录  ", smsStyle),
            ("   Tab 切换焦点", theme.FaintStyle));
    }

    private void RenderQrTab(Canvas canvas, Rect area, Theme theme)
    {
        if (area.H < 4)
        {
            return;
        }

        var statusColor = IsQrExpired ? theme.Warning : theme.Accent;
        canvas.Text(area.X, area.Y, QrStatus, new CellStyle(statusColor, Attr: TextAttr.Bold), area.W);
        canvas.Text(area.X, area.Y + 1, "Enter / r 刷新二维码   Tab 切换到短信登录", theme.MutedStyle, area.W);

        var qrArea = area.SkipTop(3);
        if (_qrImage != null && qrArea.H >= 6 && qrArea.W >= 16)
        {
            var placed = canvas.Qr(qrArea.SkipBottom(2), _qrImage);
            if (placed.IsEmpty)
            {
                canvas.Text(qrArea.X, qrArea.Y, "窗口高度不足以完整绘制二维码，请放大终端窗口或使用下方链接：",
                    new CellStyle(theme.Warning), qrArea.W);
            }
        }
        else if (_qrImage == null)
        {
            canvas.Text(qrArea.X, qrArea.Y + 1, IsQrExpired ? "二维码不可用，按 Enter 重新获取" : "正在加载二维码图像...",
                theme.MutedStyle, qrArea.W);
        }

        if (!string.IsNullOrEmpty(_qrKey) && area.Bottom - 1 > area.Y + 2)
        {
            canvas.Text(area.X, area.Bottom - 1, $"Key: {_qrKey}", theme.FaintStyle, area.W);
        }
    }

    private void RenderSmsTab(Canvas canvas, Rect area, Theme theme, bool focused)
    {
        var y = area.Y;
        var fieldWidth = Math.Clamp(area.W - 14, 16, 36);

        canvas.Text(area.X, y, "手 机 号", Field == SmsField.Mobile && focused ? theme.AccentBold : theme.MutedStyle, 10);
        MobileInput.Render(canvas, area.X + 10, y, fieldWidth, focused && Field == SmsField.Mobile, theme);
        y += 2;

        canvas.Text(area.X, y, "验 证 码", Field == SmsField.Code && focused ? theme.AccentBold : theme.MutedStyle, 10);
        CodeInput.Render(canvas, area.X + 10, y, fieldWidth, focused && Field == SmsField.Code, theme);
        y += 2;

        var sendLabel = _sending
            ? " 发送中... "
            : CooldownSeconds > 0
                ? $" 重新发送({CooldownSeconds}s) "
                : " 获取验证码 ";
        var sendStyle = focused && Field == SmsField.SendButton
            ? new CellStyle(theme.Text, theme.SelectionBg, TextAttr.Bold)
            : new CellStyle(CooldownSeconds > 0 ? theme.Faint : theme.Text, theme.InputBg);

        var loginLabel = _submitting ? " 登录中... " : " 立即登录 ";
        var loginStyle = focused && Field == SmsField.LoginButton
            ? new CellStyle(theme.Text, theme.SelectionBg, TextAttr.Bold)
            : new CellStyle(theme.Accent, theme.InputBg, TextAttr.Bold);

        canvas.Spans(area.X + 10, y, area.W - 10,
            (sendLabel, sendStyle),
            ("   ", theme.TextStyle),
            (loginLabel, loginStyle));
        y += 2;

        canvas.Text(area.X, y, SmsStatus, new CellStyle(theme.Accent), area.W);
        y += 2;

        if (Accounts.Count > 0 && y < area.Bottom)
        {
            canvas.Text(area.X, y++, "选择要登录的账号（↑/↓ 选择，Enter 确认）：", theme.Bold, area.W);
            for (var i = 0; i < Accounts.Count && y < area.Bottom; i++)
            {
                var acc = Accounts[i];
                var selected = focused && Field == SmsField.AccountList && i == _accountIndex;
                var rowStyle = selected
                    ? new CellStyle(theme.Text, theme.SelectionBg, TextAttr.Bold)
                    : theme.TextStyle;
                var name = string.IsNullOrWhiteSpace(acc.Nickname) ? acc.Username ?? acc.UserId.ToString() : acc.Nickname;
                canvas.Fill(new Rect(area.X, y, Math.Min(area.W, 48), 1), rowStyle);
                canvas.Text(area.X + 1, y, $"{(selected ? "▸" : " ")} {name}  (ID: {acc.UserId})", rowStyle,
                    Math.Min(area.W - 2, 46));
                y++;
            }
        }
    }

    public void Dispose()
    {
        StopPolling();
    }
}
