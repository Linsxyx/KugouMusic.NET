using System.Reflection;
using System.Text.Json;

namespace KaMusic.Cli.Services;

// 检测 npm 仓库 (@linsxyx/kamusic) 最新版本，并提供版本比对与状态文案。

internal sealed class TerminalUpdateService
{
    public const int UpdateExitCode = 42;

    private static readonly string[] RegistryUrls =
    [
        "https://registry.npmjs.org/@linsxyx%2fkamusic/latest",
        "https://registry.npmmirror.com/@linsxyx/kamusic/latest"
    ];

    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(6)
    };

    public TerminalUpdateService()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 KaMusic.Cli/1.0");
        CurrentVersion = ResolveCurrentVersion();
    }

    public string CurrentVersion { get; }
    public string? LatestVersion { get; private set; }
    public bool HasUpdate { get; private set; }
    public bool IsChecking { get; private set; }

    public string DisplayValue =>
        IsChecking
            ? $"v{CurrentVersion} (正在检查...)"
            : HasUpdate && !string.IsNullOrEmpty(LatestVersion)
                ? $"v{CurrentVersion} -> v{LatestVersion} 可更新"
                : !string.IsNullOrEmpty(LatestVersion)
                    ? $"v{CurrentVersion} (已是最新)"
                    : $"v{CurrentVersion}";

    public string HintText =>
        HasUpdate && !string.IsNullOrEmpty(LatestVersion)
            ? $"按 Enter 或快捷键 U 立即更新至 v{LatestVersion} 并重启"
            : "按 Enter 或快捷键 U 检查 npm 最新版本（命令行可执行 kamusic update）";

    // 异步请求 npm 官方源与镜像源获取最新发布的版本号，并与当前版本进行语义化版本比对。
    public async Task<(bool HasUpdate, string Message)> CheckForUpdateAsync(CancellationToken ct = default)
    {
        if (IsChecking)
        {
            return (HasUpdate, "正在检查更新...");
        }

        IsChecking = true;
        try
        {
            foreach (var url in RegistryUrls)
            {
                try
                {
                    using var resp = await _http.GetAsync(url, ct);
                    if (!resp.IsSuccessStatusCode)
                    {
                        continue;
                    }

                    await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                    using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                    if (!doc.RootElement.TryGetProperty("version", out var verProp))
                    {
                        continue;
                    }

                    var latest = verProp.GetString()?.Trim();
                    if (string.IsNullOrWhiteSpace(latest))
                    {
                        continue;
                    }

                    LatestVersion = latest;
                    HasUpdate = CompareSemVer(latest, CurrentVersion) > 0;
                    return HasUpdate
                        ? (true, $"发现新版本 v{latest}（当前 v{CurrentVersion}），按 U 一键更新")
                        : (false, $"当前已是最新版本 v{CurrentVersion}");
                }
                catch
                {
                    // 尝试下一个镜像源
                }
            }

            return (false, "检查更新失败：无法连接 npm 源");
        }
        finally
        {
            IsChecking = false;
        }
    }

    private static string ResolveCurrentVersion()
    {
        var envVer = Environment.GetEnvironmentVariable("KAMUSIC_VERSION")?.Trim();
        if (!string.IsNullOrWhiteSpace(envVer))
        {
            return NormalizeVersion(envVer);
        }

        var infoVer = typeof(TerminalUpdateService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(infoVer))
        {
            return NormalizeVersion(infoVer);
        }

        var asmVer = typeof(TerminalUpdateService).Assembly.GetName().Version;
        return asmVer != null ? $"{asmVer.Major}.{asmVer.Minor}.{Math.Max(0, asmVer.Build)}" : "0.1.0";
    }

    private static string NormalizeVersion(string raw)
    {
        var clean = raw.TrimStart('v', 'V');
        var plusIdx = clean.IndexOf('+');
        if (plusIdx > 0)
        {
            clean = clean[..plusIdx];
        }

        return clean;
    }

    // 比较两个 x.y.z 语义化版本号，a > b 返回 1，a < b 返回 -1，相等返回 0。
    internal static int CompareSemVer(string a, string b)
    {
        var pa = NormalizeVersion(a).Split('-', 2)[0].Split('.');
        var pb = NormalizeVersion(b).Split('-', 2)[0].Split('.');
        var len = Math.Max(pa.Length, pb.Length);
        for (var i = 0; i < len; i++)
        {
            var va = i < pa.Length && int.TryParse(pa[i], out var na) ? na : 0;
            var vb = i < pb.Length && int.TryParse(pb[i], out var nb) ? nb : 0;
            if (va != vb)
            {
                return va.CompareTo(vb);
            }
        }

        return 0;
    }
}
