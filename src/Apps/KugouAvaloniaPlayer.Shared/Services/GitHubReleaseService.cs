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

            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
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
            logger.LogWarning("加载 GitHub Releases 失败: {Error}", DescribeException(ex));
            return [];
        }
    }

    private static string DescribeException(Exception exception)
    {
        return exception.InnerException is null
            ? $"{exception.GetType().Name}: {exception.Message}"
            : $"{exception.GetType().Name}: {exception.Message} Inner={exception.InnerException.GetType().Name}: {exception.InnerException.Message}";
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
        text = ExtractReleaseNotesText(text);
        text = MarkdownImageRegex().Replace(text, string.Empty);
        text = MarkdownLinkRegex().Replace(text, "$1");
        text = MarkdownDecorationRegex().Replace(text, string.Empty);

        var lines = new List<string>();
        foreach (var rawLine in text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (IsDownloadLine(rawLine))
                continue;

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

    private static string ExtractReleaseNotesText(string body)
    {
        var changelogSection = ExtractNamedSection(body, ChangelogHeadingRegex());
        if (!string.IsNullOrWhiteSpace(changelogSection))
            return changelogSection;

        return RemoveDownloadBlocks(body);
    }

    private static string ExtractNamedSection(string body, Regex headingRegex)
    {
        var lines = body.Split('\n');
        var sectionLines = new List<string>();
        var inSection = false;
        var sectionLevel = 0;

        foreach (var rawLine in lines)
        {
            var heading = MarkdownHeadingRegex().Match(rawLine);
            if (!inSection)
            {
                if (!heading.Success || !headingRegex.IsMatch(heading.Groups["title"].Value))
                    continue;

                inSection = true;
                sectionLevel = heading.Groups["marks"].Value.Length;
                continue;
            }

            if (heading.Success && heading.Groups["marks"].Value.Length <= sectionLevel)
                break;

            sectionLines.Add(rawLine);
        }

        return string.Join('\n', sectionLines);
    }

    private static string RemoveDownloadBlocks(string body)
    {
        var lines = new List<string>();
        var skippingDownloadBlock = false;

        foreach (var rawLine in body.Split('\n'))
        {
            var heading = MarkdownHeadingRegex().Match(rawLine);
            if (heading.Success)
            {
                skippingDownloadBlock = DownloadHeadingRegex().IsMatch(heading.Groups["title"].Value);
                if (skippingDownloadBlock)
                    continue;
            }

            if (skippingDownloadBlock)
            {
                if (string.IsNullOrWhiteSpace(rawLine) || IsDownloadLine(rawLine))
                    continue;

                skippingDownloadBlock = false;
            }

            lines.Add(rawLine);
        }

        return string.Join('\n', lines);
    }

    private static bool IsDownloadLine(string line)
    {
        var trimmed = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return false;

        return DownloadLineRegex().IsMatch(trimmed);
    }

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]+\)", RegexOptions.Compiled)]
    private static partial Regex MarkdownImageRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]+\)", RegexOptions.Compiled)]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex(@"[`*_>]", RegexOptions.Compiled)]
    private static partial Regex MarkdownDecorationRegex();

    [GeneratedRegex(@"^(?<marks>#{1,6})\s+(?<title>.+?)\s*$", RegexOptions.Compiled)]
    private static partial Regex MarkdownHeadingRegex();

    [GeneratedRegex(@"^(更新日志|更新内容|changelog|release\s+notes)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ChangelogHeadingRegex();

    [GeneratedRegex(@"^(下载|downloads?|assets?|安装包|安装文件)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DownloadHeadingRegex();

    [GeneratedRegex(@"^[-*+]\s*(windows|linux|macos|mac|osx|download|下载|安装包)[^:：]*[:：]\s*(\[.+?\]\(.+?\)|https?://\S+|.+\.(exe|zip|appimage|pkg|tar\.gz|nupkg))\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DownloadLineRegex();
}
