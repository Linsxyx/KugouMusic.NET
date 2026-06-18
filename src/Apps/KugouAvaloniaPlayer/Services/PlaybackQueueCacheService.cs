using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using KuGou.Net.Abstractions.Models;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.ViewModels;
using Microsoft.Extensions.Logging;
using ZLinq;

namespace KugouAvaloniaPlayer.Services;

public sealed class PlaybackQueueCacheService(ILogger<PlaybackQueueCacheService> logger)
{
    private const int CacheSchemaVersion = 1;
    private const string StoreScope = "playback_queue";
    private const string StoreKey = "app";
    private const string DefaultCover = "avares://KugouAvaloniaPlayer/Assets/default_song.png";

    public PlaybackQueueCacheSnapshot? Load()
    {
        try
        {
            var json = AppSqliteStore.LoadValue(StoreScope, StoreKey);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            var model = JsonSerializer.Deserialize(json, PlaybackQueueCacheJsonContext.Default.PlaybackQueueCacheFileModel);
            if (model?.Items == null || model.Items.Count == 0)
                return null;

            model.SchemaVersion = model.SchemaVersion <= 0 ? CacheSchemaVersion : model.SchemaVersion;
            model.UpdatedAt = string.IsNullOrWhiteSpace(model.UpdatedAt)
                ? DateTimeOffset.Now.ToString("O")
                : model.UpdatedAt;
            model.Items = model.Items
                .AsValueEnumerable()
                .Where(IsValidCacheItem)
                .ToList();

            if (model.Items.Count == 0)
                return null;

            return new PlaybackQueueCacheSnapshot
            {
                CurrentSongKey = model.CurrentSongKey ?? "",
                Songs = model.Items.AsValueEnumerable().Select(ToSongItem).ToList()
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "读取播放队列缓存失败。");
            return null;
        }
    }

    public void Save(IReadOnlyList<SongItem> songs, SongItem? currentSong)
    {
        try
        {
            if (songs.Count == 0)
            {
                AppSqliteStore.DeleteValue(StoreScope, StoreKey);
                return;
            }

            var model = new PlaybackQueueCacheFileModel
            {
                SchemaVersion = CacheSchemaVersion,
                UpdatedAt = DateTimeOffset.Now.ToString("O"),
                CurrentSongKey = BuildSongKey(currentSong),
                Items = songs.AsValueEnumerable().Select(ToCacheItem).ToList()
            };

            var json = JsonSerializer.Serialize(model, PlaybackQueueCacheJsonContext.Default.PlaybackQueueCacheFileModel);
            AppSqliteStore.SaveValue(StoreScope, StoreKey, json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "写入播放队列缓存失败。");
        }
    }

    public static string BuildSongKey(SongItem? song)
    {
        if (song == null)
            return "";

        if (!string.IsNullOrWhiteSpace(song.Hash))
            return $"hash:{song.Hash.Trim().ToLowerInvariant()}";

        if (!string.IsNullOrWhiteSpace(song.LocalFilePath))
            return $"local:{Path.GetFullPath(song.LocalFilePath).ToLowerInvariant()}";

        if (song.FileId > 0)
            return $"file:{song.FileId}";

        return $"song:{song.Name.Trim().ToLowerInvariant()}:{song.Singer.Trim().ToLowerInvariant()}";
    }

    private static bool IsValidCacheItem(PlaybackQueueCacheItem item)
    {
        return !string.IsNullOrWhiteSpace(item.Hash) ||
               !string.IsNullOrWhiteSpace(item.LocalFilePath) ||
               item.FileId > 0 ||
               !string.IsNullOrWhiteSpace(item.Name);
    }

    private static PlaybackQueueCacheItem ToCacheItem(SongItem song)
    {
        return new PlaybackQueueCacheItem
        {
            Hash = song.Hash,
            FileId = song.FileId,
            Name = song.Name,
            Singer = song.Singer,
            Singers = song.Singers?.AsValueEnumerable().ToList() ?? new List<SingerLite>(),
            AlbumId = song.AlbumId,
            AlbumAudioId = song.AlbumAudioId,
            AlbumName = song.AlbumName,
            AudioId = song.AudioId,
            Cover = song.Cover,
            DurationSeconds = song.DurationSeconds,
            LocalFilePath = song.LocalFilePath,
            LocalSourceType = song.LocalSourceType,
            LocalTrackId = song.LocalTrackId,
            RemoteUrl = song.RemoteUrl,
            PlaybackSource = song.PlaybackSource
        };
    }

    private static SongItem ToSongItem(PlaybackQueueCacheItem item)
    {
        return new SongItem
        {
            Name = string.IsNullOrWhiteSpace(item.Name) ? "未知" : item.Name,
            Singer = string.IsNullOrWhiteSpace(item.Singer) ? "未知" : item.Singer,
            Hash = item.Hash,
            AlbumId = item.AlbumId,
            AlbumAudioId = item.AlbumAudioId,
            AlbumName = item.AlbumName,
            AudioId = item.AudioId,
            FileId = item.FileId,
            Singers = item.Singers ?? new List<SingerLite>(),
            Cover = string.IsNullOrWhiteSpace(item.Cover) ? DefaultCover : item.Cover,
            DurationSeconds = item.DurationSeconds,
            LocalFilePath = item.LocalFilePath,
            LocalSourceType = item.LocalSourceType,
            LocalTrackId = item.LocalTrackId,
            RemoteUrl = item.RemoteUrl,
            PlaybackSource = item.PlaybackSource
        };
    }
}

public sealed class PlaybackQueueCacheSnapshot
{
    public string CurrentSongKey { get; set; } = "";
    public List<SongItem> Songs { get; set; } = new();
}

public sealed class PlaybackQueueCacheFileModel
{
    public int SchemaVersion { get; set; } = 1;
    public string UpdatedAt { get; set; } = "";
    public string? CurrentSongKey { get; set; }
    public List<PlaybackQueueCacheItem> Items { get; set; } = new();
}

public sealed class PlaybackQueueCacheItem
{
    public string Hash { get; set; } = "";
    public long FileId { get; set; }
    public string Name { get; set; } = "";
    public string Singer { get; set; } = "";
    public List<SingerLite> Singers { get; set; } = new();
    public string AlbumId { get; set; } = "";
    public long AlbumAudioId { get; set; }
    public string AlbumName { get; set; } = "";
    public long AudioId { get; set; }
    public string? Cover { get; set; }
    public double DurationSeconds { get; set; }
    public string? LocalFilePath { get; set; }
    public string? LocalSourceType { get; set; }
    public long LocalTrackId { get; set; }
    public string? RemoteUrl { get; set; }
    public SongPlaybackSource PlaybackSource { get; set; } = SongPlaybackSource.Default;
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNameCaseInsensitive = true
)]
[JsonSerializable(typeof(PlaybackQueueCacheFileModel))]
internal partial class PlaybackQueueCacheJsonContext : JsonSerializerContext
{
}
