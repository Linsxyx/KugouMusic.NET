using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using KuGou.Net.Clients;
using KuGou.Net.Protocol.Session;
using KuGou.Net.util;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using KugouAvaloniaPlayer.Views;

namespace KugouAvaloniaPlayer.Services;

public interface ISecurityVerificationService
{
    Task<bool> VerifyAsync(string eventId, CancellationToken cancellationToken);
}

/// <summary>Hosts a short-lived, loopback-only page for user-completed security challenges.</summary>
public sealed class SecurityVerificationService(VerificationClient verificationClient, KgSessionManager sessionManager)
    : ISecurityVerificationService
{
    public async Task<bool> VerifyAsync(string eventId, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var token = timeout.Token;
        SecurityVerificationWindow? window = null;
        var closedByUser = false;
        try
        {
            var info = await verificationClient.GetInfoAsync(eventId).WaitAsync(token);
            if (info is null || info.Status != 1 || info.ErrorCode is not (null or 0) || info.RequiresVerification)
                throw new InvalidOperationException("获取安全验证信息失败，请稍后重试。");
            if (info.VerificationType is not (23 or 32))
                throw new InvalidOperationException($"暂不支持安全验证类型 {info.VerificationType}。");
            if (info.VerificationType == 23 && string.IsNullOrWhiteSpace(info.TencentAppId))
                throw new InvalidOperationException("安全验证响应缺少腾讯验证码应用 ID。");

            using var listener = StartListener(out var origin);
            var path = "/" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant() + "/";
            var url = origin + path;
            var session = sessionManager.Session;
            var userId = session.UserId;
            var dfid = session.Dfid;
            var config = new JsonObject
            {
                ["type"] = info.VerificationType,
                ["appId"] = info.TencentAppId,
                ["mid"] = KgUtils.CalcNewMid(session.Dfid),
                ["dfid"] = session.Dfid,
                ["userid"] = session.UserId
            };
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                token.ThrowIfCancellationRequested();
                var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow
                    ?? throw new InvalidOperationException("无法找到安全验证窗口的父窗口。");
                window = new SecurityVerificationWindow(new Uri(url));
                window.Closed += (_, _) =>
                {
                    closedByUser = true;
                    timeout.Cancel();
                };
                window.Show(owner);
            });

            while (true)
            {
                var context = await listener.GetContextAsync().WaitAsync(token);
                using var response = context.Response;
                response.Headers["Cache-Control"] = "no-store";
                response.Headers["Referrer-Policy"] = "no-referrer";
                response.Headers["X-Content-Type-Options"] = "nosniff";
                response.Headers["X-Frame-Options"] = "DENY";
                var request = context.Request;
                var requestPath = request.Url?.AbsolutePath ?? "";
                if (request.Url?.Authority != new Uri(origin).Authority ||
                    !requestPath.StartsWith(path, StringComparison.Ordinal))
                {
                    response.StatusCode = 404;
                    continue;
                }

                var resource = requestPath[path.Length..];
                if (session.UserId != userId || session.Dfid != dfid)
                    throw new InvalidOperationException("登录会话已发生变化，请重新发起验证。");
                try
                {
                    if (request.HttpMethod == "GET")
                    {
                        if (resource == "config")
                            await WriteJsonAsync(response, config, token);
                        else
                            await WriteResourceAsync(response, resource, token);
                        continue;
                    }

                    // A nonce path, exact Origin and JSON POST prevent cross-site submissions.
                    if (request.HttpMethod != "POST" || request.Headers["Origin"] != origin ||
                        request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) != true)
                    {
                        response.StatusCode = 403;
                        continue;
                    }

                    if (resource == "cancel")
                    {
                        await WriteJsonAsync(response, new JsonObject { ["ok"] = true }, token);
                        return false;
                    }

                    if (resource != "submit" || request.ContentLength64 is < 0 or > 65536)
                    {
                        response.StatusCode = 400;
                        continue;
                    }

                    using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    requestTimeout.CancelAfter(TimeSpan.FromSeconds(30));
                    var input = await JsonSerializer.DeserializeAsync(request.InputStream,
                        SecurityVerificationJsonContext.Default.JsonObject, requestTimeout.Token);
                    var verifyCode = input?["verifyCode"]?.GetValue<string>();
                    var sid = input?["sid"]?.GetValue<string>();
                    var edt = input?["edt"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(verifyCode) || string.IsNullOrWhiteSpace(sid) ||
                        string.IsNullOrWhiteSpace(edt))
                    {
                        await WriteJsonAsync(response, new JsonObject
                        {
                            ["ok"] = false, ["message"] = "验证数据不完整，请刷新验证页面。"
                        }, token);
                        continue;
                    }

                    var result = await verificationClient.SubmitAsync(eventId, info.VerificationType,
                        verifyCode, sid, edt).WaitAsync(token);
                    var success = result?.IsSuccess == true;
                    await WriteJsonAsync(response, new JsonObject
                    {
                        ["ok"] = success,
                        ["message"] = success ? "验证成功，请返回播放器。" : $"验证未通过（{result?.ErrorCode}），请重试。"
                    }, token);
                    if (success)
                        return true;
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
                {
                    await WriteJsonAsync(response, new JsonObject
                    {
                        ["ok"] = false, ["message"] = "验证数据无效，请重新完成验证。"
                    }, token);
                }
                catch (HttpRequestException)
                {
                    await WriteJsonAsync(response, new JsonObject
                    {
                        ["ok"] = false, ["message"] = "验证请求失败，请检查网络后重试。"
                    }, token);
                }
                catch (IOException)
                {
                    // Browser disconnected while loading a resource. Allow another request until timeout.
                }
                catch (HttpListenerException)
                {
                }
            }
        }
        catch (OperationCanceledException) when (closedByUser && !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("安全验证已超时，请重新发起操作。");
        }
        finally
        {
            if (window is not null)
                await Dispatcher.UIThread.InvokeAsync(window.Close);
        }
    }

    private static HttpListener StartListener(out string origin)
    {
        // Reserve an OS-selected port temporarily; retry if it is claimed before HttpListener binds.
        for (var attempt = 0; ; attempt++)
        {
            int port;
            using (var probe = new TcpListener(IPAddress.Loopback, 0))
            {
                probe.Start();
                port = ((IPEndPoint)probe.LocalEndpoint).Port;
            }
            origin = $"http://127.0.0.1:{port}";
            var listener = new HttpListener();
            listener.Prefixes.Add(origin + "/");
            try
            {
                listener.Start();
                return listener;
            }
            catch (HttpListenerException) when (attempt < 2)
            {
                listener.Close();
            }
            catch
            {
                listener.Close();
                throw;
            }
        }
    }

    private static async Task WriteResourceAsync(HttpListenerResponse response, string name, CancellationToken token)
    {
        var (file, contentType) = name switch
        {
            "" => ("index.html", "text/html; charset=utf-8"),
            "verification.js" => ("verification.js", "text/javascript; charset=utf-8"),
            "verifycode.js" => ("verifycode.js", "text/javascript; charset=utf-8"),
            "verifycode_bg_ios.wasm" => ("verifycode_bg_ios.wasm", "application/wasm"),
            _ => ("", "")
        };
        if (file.Length == 0)
        {
            response.StatusCode = 404;
            return;
        }
        await using var stream = typeof(SecurityVerificationService).Assembly
            .GetManifestResourceStream("SecurityVerification." + file)
            ?? throw new InvalidOperationException("安全验证页面资源缺失。");
        response.ContentType = contentType;
        response.ContentLength64 = stream.Length;
        await stream.CopyToAsync(response.OutputStream, token);
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, JsonObject value, CancellationToken token)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, SecurityVerificationJsonContext.Default.JsonObject));
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, token);
    }
}

[JsonSerializable(typeof(JsonObject))]
internal partial class SecurityVerificationJsonContext : JsonSerializerContext;
