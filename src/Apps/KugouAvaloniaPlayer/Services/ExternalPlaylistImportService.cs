using System;
using System.Collections.Generic;
using ZLinq;
using System.Threading;
using System.Threading.Tasks;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Clients;
using KuGou.Net.ExternalPlaylists;
using Microsoft.Extensions.Logging;

namespace KugouAvaloniaPlayer.Services;

public sealed class ExternalPlaylistImportResult
{
    public bool Success => string.IsNullOrWhiteSpace(ErrorMessage);
    public string ErrorMessage { get; init; } = string.Empty;
    public int Total { get; init; }
    public int Matched { get; init; }
    public int Imported { get; init; }
    public List<string> FailedNames { get; init; } = new();
}

public sealed class ExternalPlaylistImportProgress
{
    public string Stage { get; init; } = string.Empty;
    public int Processed { get; init; }
    public int Total { get; init; }
    public double Percentage => Total <= 0 ? 0 : Math.Clamp(Processed * 100.0 / Total, 0, 100);
    public string Message { get; init; } = string.Empty;
}

public interface IExternalPlaylistImportService
{
    Task<ExternalPlaylistParseResult> ParseAndLoadAsync(
        string sourceText,
        CancellationToken cancellationToken = default);

    Task<ExternalPlaylistImportResult> ImportToKugouAsync(
        ExternalPlaylistParseResult parseResult,
        string targetPlaylistName,
        IProgress<ExternalPlaylistImportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class ExternalPlaylistImportService(
    IExternalPlaylistParser externalPlaylistParser,
    SearchClient searchClient,
    PlaylistClient playlistClient,
    UserClient userClient,
    ILogger<ExternalPlaylistImportService> logger) : IExternalPlaylistImportService
{
    private const int SongBatchSize = 100;

    public Task<ExternalPlaylistParseResult> ParseAndLoadAsync(
        string sourceText,
        CancellationToken cancellationToken = default)
    {
        return externalPlaylistParser.ParseAndLoadAsync(sourceText, cancellationToken);
    }

    public async Task<ExternalPlaylistImportResult> ImportToKugouAsync(
        ExternalPlaylistParseResult parseResult,
        string targetPlaylistName,
        IProgress<ExternalPlaylistImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!parseResult.Success)
            return new ExternalPlaylistImportResult { ErrorMessage = parseResult.ErrorMessage };

        if (parseResult.SongNames.Count == 0)
        {
            return new ExternalPlaylistImportResult
            {
                ErrorMessage = "来源歌单没有可导入歌曲。"
            };
        }

        if (string.IsNullOrWhiteSpace(targetPlaylistName))
            return new ExternalPlaylistImportResult { ErrorMessage = "目标歌单名称不能为空。" };

        try
        {
            await playlistClient.CreatePlaylistAsync(targetPlaylistName);
            var target = await WaitForCreatedPlaylistAsync(targetPlaylistName, cancellationToken);

            if (target == null)
                return new ExternalPlaylistImportResult { ErrorMessage = "创建成功，但未找到目标歌单，请稍后重试。" };

            var sourceSongNames = parseResult.SongNames.AsValueEnumerable().Reverse().ToList();
            var total = sourceSongNames.Count;
            var matchedSongs =
                new List<(string SourceName, string Name, string Hash, string AlbumId, string MixSongId)>();
            var failedNames = new List<string>();
            var matchedProcessed = 0;

            progress?.Report(new ExternalPlaylistImportProgress
            {
                Stage = "matching",
                Processed = 0,
                Total = total,
                Message = $"正在匹配歌曲 0/{total}"
            });

            foreach (var songName in sourceSongNames)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var searchResult = await searchClient.SearchAsync(songName);
                    var first = searchResult.AsValueEnumerable().FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Hash));
                    if (first == null)
                    {
                        failedNames.Add(songName);
                        continue;
                    }

                    matchedSongs.Add((
                        songName,
                        string.IsNullOrWhiteSpace(first.Name) ? songName : first.Name,
                        first.Hash,
                        string.IsNullOrWhiteSpace(first.AlbumId) ? "0" : first.AlbumId,
                        "0"));
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "搜索歌曲失败，已跳过。 song={SongName}", songName);
                    failedNames.Add(songName);
                }

                matchedProcessed++;
                progress?.Report(new ExternalPlaylistImportProgress
                {
                    Stage = "matching",
                    Processed = matchedProcessed,
                    Total = total,
                    Message = $"正在匹配歌曲 {matchedProcessed}/{total}"
                });
            }

            var imported = 0;
            var addProcessed = 0;
            var addTotal = Math.Max(matchedSongs.Count, 1);

            progress?.Report(new ExternalPlaylistImportProgress
            {
                Stage = "adding",
                Processed = 0,
                Total = addTotal,
                Message = $"正在写入歌单 0/{matchedSongs.Count}"
            });

            var chunks = matchedSongs.AsValueEnumerable().Chunk(SongBatchSize).ToList();

            foreach (var chunk in chunks)
            {
                try
                {
                    var payload = chunk
                        .AsValueEnumerable().Select(x => (x.Name, x.Hash, x.AlbumId, x.MixSongId))
                        .ToList();

                    var addResult = await playlistClient.AddSongsAsync(target.ListId.ToString(), payload);
                    imported += addResult?.AddedSongs.Count ?? 0;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "批量添加歌曲失败。 targetListId={ListId}", target.ListId);
                    failedNames.AddRange(chunk.AsValueEnumerable().Select(x => x.SourceName).ToArray());
                }

                addProcessed += chunk.Length;
                progress?.Report(new ExternalPlaylistImportProgress
                {
                    Stage = "adding",
                    Processed = Math.Min(addProcessed, addTotal),
                    Total = addTotal,
                    Message = $"正在写入歌单 {Math.Min(addProcessed, matchedSongs.Count)}/{matchedSongs.Count}"
                });
            }

            return new ExternalPlaylistImportResult
            {
                Total = total,
                Matched = matchedSongs.Count,
                Imported = imported,
                FailedNames = failedNames.AsValueEnumerable().Distinct(StringComparer.Ordinal).ToList()
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "导入外部歌单失败。 targetPlaylist={TargetPlaylist}", targetPlaylistName);
            return new ExternalPlaylistImportResult { ErrorMessage = $"导入失败：{ex.Message}" };
        }
    }

    private async Task<UserPlaylistItem?> WaitForCreatedPlaylistAsync(
        string targetPlaylistName,
        CancellationToken cancellationToken)
    {
        const int maxRetries = 8;
        var delay = TimeSpan.FromMilliseconds(500);

        for (var i = 0; i < maxRetries; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var playlists = await userClient.GetPlaylistsAsync();
            var target = playlists?.Playlists?
                .AsValueEnumerable().Where(x => !string.IsNullOrWhiteSpace(x.ListCreateId)
                                                && string.Equals(x.Name, targetPlaylistName, StringComparison.Ordinal))
                .OrderByDescending(x => x.ListId)
                .FirstOrDefault();

            if (target != null)
                return target;

            await Task.Delay(delay, cancellationToken);
        }

        return null;
    }
}
