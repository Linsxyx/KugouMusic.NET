using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Protocol.Session;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.ViewModels;
using Microsoft.Extensions.Logging;

namespace KugouAvaloniaPlayer.Services;

public sealed class PlaybackHistoryService(
    KgSessionManager sessionManager,
    ILogger<PlaybackHistoryService> logger)
{
    private const int CacheSchemaVersion = 1;
    private const int MaxHistoryCount = 100;
    private readonly SemaphoreSlim _historyLock = new(1, 1);
    private PlaybackHistoryFileModel? _latestCache;

    public event EventHandler? HistoryChanged;

    public async Task<IReadOnlyList<SongItem>> LoadSongsAsync()
    {
        await _historyLock.WaitAsync();
        try
        {
            var cache = EnsureCacheForCurrentUser();
            return cache.Items.Select(ToSongItem).ToList();
        }
        finally
        {
            _historyLock.Release();
        }
    }

    public async Task RecordPlayedAsync(SongItem song)
    {
        if (string.IsNullOrWhiteSpace(song.Hash) &&
            string.IsNullOrWhiteSpace(song.LocalFilePath) &&
            string.IsNullOrWhiteSpace(song.Name))
            return;

        await _historyLock.WaitAsync();
        try
        {
            var cache = EnsureCacheForCurrentUser();
            var item = ToHistoryItem(song);
            var key = GetHistoryKey(item);

            cache.Items = cache.Items
                .Where(x => !string.Equals(GetHistoryKey(x), key, StringComparison.OrdinalIgnoreCase))
                .Prepend(item)
                .Take(MaxHistoryCount)
                .ToList();

            cache.UpdatedAt = DateTimeOffset.Now.ToString("O");
            SaveHistoryToDisk(cache);
        }
        finally
        {
            _historyLock.Release();
        }

        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task ClearAsync()
    {
        await _historyLock.WaitAsync();
        try
        {
            var cache = EnsureCacheForCurrentUser();
            cache.Items.Clear();
            cache.UpdatedAt = DateTimeOffset.Now.ToString("O");
            SaveHistoryToDisk(cache);
        }
        finally
        {
            _historyLock.Release();
        }

        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private PlaybackHistoryFileModel EnsureCacheForCurrentUser()
    {
        var currentUserId = GetCurrentUserId();
        if (_latestCache != null && string.Equals(_latestCache.UserId, currentUserId, StringComparison.Ordinal))
            return _latestCache;

        if (TryLoadHistoryFromDisk(out var diskCache))
            return _latestCache = diskCache!;

        return _latestCache = new PlaybackHistoryFileModel
        {
            SchemaVersion = CacheSchemaVersion,
            UserId = currentUserId,
            UpdatedAt = DateTimeOffset.Now.ToString("O"),
            Items = new List<PlaybackHistoryItem>()
        };
    }

    private bool TryLoadHistoryFromDisk(out PlaybackHistoryFileModel? cache)
    {
        cache = null;
        try
        {
            var filePath = GetHistoryFilePath();
            if (!File.Exists(filePath))
                return false;

            var json = File.ReadAllText(filePath);
            var model = JsonSerializer.Deserialize(json, PlaybackHistoryJsonContext.Default.PlaybackHistoryFileModel);
            if (model?.Items == null)
                return false;

            model.SchemaVersion = model.SchemaVersion <= 0 ? CacheSchemaVersion : model.SchemaVersion;
            model.UserId = string.IsNullOrWhiteSpace(model.UserId) ? GetCurrentUserId() : model.UserId;
            model.UpdatedAt = string.IsNullOrWhiteSpace(model.UpdatedAt)
                ? DateTimeOffset.Now.ToString("O")
                : model.UpdatedAt;
            model.Items = model.Items
                .Where(x => !string.IsNullOrWhiteSpace(x.Hash) ||
                            !string.IsNullOrWhiteSpace(x.LocalFilePath) ||
                            !string.IsNullOrWhiteSpace(x.Name))
                .Take(MaxHistoryCount)
                .ToList();

            cache = model;
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "读取本地播放历史失败。 path={Path}", GetHistoryFilePath());
            return false;
        }
    }

    private void SaveHistoryToDisk(PlaybackHistoryFileModel cache)
    {
        try
        {
            var filePath = GetHistoryFilePath();
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var tempPath = filePath + ".tmp";
            var json = JsonSerializer.Serialize(cache, PlaybackHistoryJsonContext.Default.PlaybackHistoryFileModel);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, filePath, true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "写入本地播放历史失败。 path={Path}", GetHistoryFilePath());
        }
    }

    private string GetHistoryFilePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "kugou",
            $"playback_history_{GetCurrentUserId()}.json");
    }

    private string GetCurrentUserId()
    {
        var uid = sessionManager.Session.UserId;
        return string.IsNullOrWhiteSpace(uid) ? "0" : uid;
    }

    private static PlaybackHistoryItem ToHistoryItem(SongItem song)
    {
        return new PlaybackHistoryItem
        {
            Hash = song.Hash,
            FileId = song.FileId,
            Name = song.Name,
            Singer = song.Singer,
            Singers = song.Singers?.ToList() ?? new List<SingerLite>(),
            AlbumId = song.AlbumId,
            AlbumName = song.AlbumName,
            AudioId = song.AudioId,
            Cover = song.Cover,
            DurationSeconds = song.DurationSeconds,
            LocalFilePath = song.LocalFilePath,
            PlaybackSource = song.PlaybackSource,
            PlayedAt = DateTimeOffset.Now.ToString("O")
        };
    }

    private static SongItem ToSongItem(PlaybackHistoryItem item)
    {
        return new SongItem
        {
            Name = string.IsNullOrWhiteSpace(item.Name) ? "未知" : item.Name,
            Singer = string.IsNullOrWhiteSpace(item.Singer) ? "未知" : item.Singer,
            Hash = item.Hash,
            AlbumId = item.AlbumId,
            AlbumName = item.AlbumName,
            AudioId = item.AudioId,
            FileId = item.FileId,
            Singers = item.Singers ?? new List<SingerLite>(),
            Cover = string.IsNullOrWhiteSpace(item.Cover)
                ? "avares://KugouAvaloniaPlayer/Assets/default_song.png"
                : item.Cover,
            DurationSeconds = item.DurationSeconds,
            LocalFilePath = item.LocalFilePath,
            PlaybackSource = item.PlaybackSource
        };
    }

    private static string GetHistoryKey(PlaybackHistoryItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Hash))
            return $"hash:{item.Hash.Trim().ToLowerInvariant()}";

        if (!string.IsNullOrWhiteSpace(item.LocalFilePath))
            return $"local:{Path.GetFullPath(item.LocalFilePath).ToLowerInvariant()}";

        return $"song:{item.Name.Trim().ToLowerInvariant()}:{item.Singer.Trim().ToLowerInvariant()}";
    }
}

public sealed class PlaybackHistoryFileModel
{
    public int SchemaVersion { get; set; } = 1;
    public string UserId { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public List<PlaybackHistoryItem> Items { get; set; } = new();
}

public sealed class PlaybackHistoryItem
{
    public string Hash { get; set; } = "";
    public long FileId { get; set; }
    public string Name { get; set; } = "";
    public string Singer { get; set; } = "";
    public List<SingerLite> Singers { get; set; } = new();
    public string AlbumId { get; set; } = "";
    public string AlbumName { get; set; } = "";
    public long AudioId { get; set; }
    public string? Cover { get; set; }
    public double DurationSeconds { get; set; }
    public string? LocalFilePath { get; set; }
    public SongPlaybackSource PlaybackSource { get; set; } = SongPlaybackSource.Default;
    public string PlayedAt { get; set; } = "";
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNameCaseInsensitive = true
)]
[JsonSerializable(typeof(PlaybackHistoryFileModel))]
internal partial class PlaybackHistoryJsonContext : JsonSerializerContext
{
}
