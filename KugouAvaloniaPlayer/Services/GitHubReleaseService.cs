using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace KugouAvaloniaPlayer.Services;

public sealed record GitHubReleaseInfo(
    string Title,
    string PublishedAt,
    string Summary,
    string Url);

public interface IGitHubReleaseService
{
    Task<IReadOnlyList<GitHubReleaseInfo>> GetRecentReleasesAsync(int count, CancellationToken cancellationToken = default);
}

public sealed partial class GitHubReleaseService(
    IHttpClientFactory httpClientFactory,
    ILogger<GitHubReleaseService> logger) : IGitHubReleaseService
{
    private const string ReleasesApi = "https://api.github.com/repos/Linsxyx/KugouMusic.NET/releases";

    public async Task<IReadOnlyList<GitHubReleaseInfo>> GetRecentReleasesAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        if (count <= 0)
            return [];

        try
        {
            using var client = httpClientFactory.CreateClient(nameof(GitHubReleaseService));
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{ReleasesApi}?per_page={count}");
            request.Headers.UserAgent.ParseAdd("KA-Music/1.0");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return [];

            var releases = new List<GitHubReleaseInfo>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var tagName = GetString(item, "tag_name");
                if (string.IsNullOrWhiteSpace(tagName))
                    continue;

                var name = GetString(item, "name");
                var title = string.IsNullOrWhiteSpace(name) ? tagName : name;
                var summary = FormatReleaseSummary(GetString(item, "body"));
                var publishedAt = FormatPublishedAt(GetString(item, "published_at"));
                var url = GetString(item, "html_url");

                releases.Add(new GitHubReleaseInfo(
                    title,
                    publishedAt,
                    summary,
                    url));
            }

            return releases;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "加载 GitHub Releases 失败。");
            return [];
        }
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string FormatPublishedAt(string value)
    {
        return DateTimeOffset.TryParse(value, out var date)
            ? date.ToLocalTime().ToString("yyyy-MM-dd")
            : "发布日期未知";
    }

    private static string FormatReleaseSummary(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "该版本暂未填写发布说明。";

        var text = body.Replace("\r\n", "\n").Replace('\r', '\n');
        text = MarkdownImageRegex().Replace(text, string.Empty);
        text = MarkdownLinkRegex().Replace(text, "$1");
        text = MarkdownDecorationRegex().Replace(text, string.Empty);

        var lines = new List<string>();
        foreach (var rawLine in text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.TrimStart('#', '-', '*', '+', ' ', '\t');
            if (string.IsNullOrWhiteSpace(line))
                continue;

            lines.Add(line);
            if (lines.Count >= 5)
                break;
        }

        var summary = string.Join(Environment.NewLine, lines);
        return string.IsNullOrWhiteSpace(summary) ? "该版本暂未填写发布说明。" : summary;
    }

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]+\)", RegexOptions.Compiled)]
    private static partial Regex MarkdownImageRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]+\)", RegexOptions.Compiled)]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex(@"[`*_>]", RegexOptions.Compiled)]
    private static partial Regex MarkdownDecorationRegex();
}
