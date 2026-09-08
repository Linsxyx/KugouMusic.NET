using System;
using System.Collections.Generic;
using System.Text.Json;
using ZLinq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Clients;
using KugouAvaloniaPlayer.Services;
using Microsoft.Extensions.Logging;
using SukiUI.Toasts;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class SimilarSongsViewModel : PageViewModelBase, IDisposable
{
    private const string DefaultCover = "avares://KugouAvaloniaPlayer/Assets/default_song.png";

    private readonly RecommendClient _recommendClient;
    private readonly ISukiToastManager _toastManager;
    private readonly ILogger<SimilarSongsViewModel> _logger;
    private readonly SongItem _sourceSong;

    private bool _isDisposed;
    private int _requestVersion;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingMore { get; set; }

    [ObservableProperty]
    public partial string SourceCover { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; }

    public AvaloniaList<SongItem> Songs { get; } = new();

    public SimilarSongsViewModel(
        RecommendClient recommendClient,
        ISukiToastManager toastManager,
        ILogger<SimilarSongsViewModel> logger,
        SongItem sourceSong)
    {
        _recommendClient = recommendClient;
        _toastManager = toastManager;
        _logger = logger;
        _sourceSong = sourceSong;

        SourceCover = string.IsNullOrWhiteSpace(sourceSong.Cover) ? DefaultCover : sourceSong.Cover;
        Title = $"{sourceSong.Name} 的相似推荐";

        _ = LoadAsync();
    }

    public override string DisplayName => "相似歌曲";
    public override string Icon => DefaultCover;

    private async Task LoadAsync()
    {
        if (_isDisposed)
            return;

        if (_sourceSong.AlbumAudioId == 0)
        {
            ShowToast("相似歌曲", "当前歌曲缺少在线信息，无法推荐", Avalonia.Controls.Notifications.NotificationType.Warning);
            return;
        }

        var requestVersion = Interlocked.Increment(ref _requestVersion);
        IsLoading = true;

        try
        {
            var json = await _recommendClient.GetAiRecommendAsync(_sourceSong.AlbumAudioId.ToString());
            if (!IsCurrentRequest(requestVersion))
                return;

            var songs = ParseSimilarSongs(json);
            if (!IsCurrentRequest(requestVersion))
                return;

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                Songs.Clear();
                Songs.AddRange(songs);
            });

            if (songs.Count == 0)
                ShowToast("相似歌曲", "暂时没有相似的推荐", Avalonia.Controls.Notifications.NotificationType.Information);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载相似歌曲失败");
            ShowToast("相似歌曲", "加载相似歌曲失败，请稍后再试", Avalonia.Controls.Notifications.NotificationType.Error);
        }
        finally
        {
            if (IsCurrentRequest(requestVersion))
                IsLoading = false;
        }
    }

    /// <summary>
    ///     容错解析 AI 相似推荐响应。返回结构尚未校准，按多个候选 key 定位歌曲数组与字段。
    ///     实跑对照日志后可收紧为固定模型反序列化。
    /// </summary>
    private List<SongItem> ParseSimilarSongs(JsonElement root)
    {
        var result = new List<SongItem>();

        // 1. 提取 data 节点（酷狗常见外层包裹）
        var dataElement = root;
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            dataElement = data;

        // 2. 在 data / root 里找歌曲数组（候选 key）
        var songsArray = FindSongsArray(dataElement) ?? FindSongsArray(root);
        if (songsArray is null || songsArray.Value.ValueKind != JsonValueKind.Array)
        {
            _logger.LogWarning("相似推荐响应中未找到歌曲数组，候选 key 均不匹配");
            return result;
        }

        foreach (var item in songsArray.Value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var hash = GetString(item, "hash", "filename_hash");
            if (string.IsNullOrWhiteSpace(hash))
                continue;

            var song = new SongItem
            {
                Name = GetString(item, "songname", "name", "song_name", "filename") ?? "",
                Singer = GetString(item, "author_name", "singername", "singer_name", "author") ?? "",
                Hash = hash,
                AlbumId = GetString(item, "album_id", "albumid") ?? "",
                AlbumName = GetString(item, "album_name", "albumname") ?? "",
                Cover = GetCover(item),
                DurationSeconds = GetInt(item, "time_length", "duration", "timelength"),
                AudioId = GetLong(item, "songid", "audio_id", "song_id"),
                AlbumAudioId = GetLong(item, "mixsongid", "album_audio_id", "mix_song_id")
            };

            result.Add(song);
        }

        return result;
    }

    private static JsonElement? FindSongsArray(JsonElement obj)
    {
        if (obj.ValueKind != JsonValueKind.Object)
            return null;

        var candidates = new[] { "song_list", "songs", "list", "playlist", "audios", "songList", "data" };
        foreach (var key in candidates)
        {
            if (obj.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.Array)
                return prop;
        }

        return null;
    }

    private static string? GetString(JsonElement obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (obj.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                var v = prop.GetString();
                if (!string.IsNullOrWhiteSpace(v))
                    return v;
            }
        }
        return null;
    }

    private static int GetInt(JsonElement obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (obj.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.Number)
            {
                if (prop.TryGetInt32(out var i))
                    return i;
            }
        }
        return 0;
    }

    private static long GetLong(JsonElement obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!obj.TryGetProperty(key, out var prop))
                continue;

            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var l))
                return l;

            // 酷狗常把 id 以字符串返回，如 mixsongid: "288598536"
            if (prop.ValueKind == JsonValueKind.String &&
                long.TryParse(prop.GetString(), out var sl))
                return sl;
        }
        return 0;
    }

    private static string GetCover(JsonElement obj)
    {
        var cover = GetString(obj, "sizable_cover", "cover", "cover_img_url", "img");
        if (string.IsNullOrWhiteSpace(cover) && obj.TryGetProperty("trans_param", out var trans) &&
            trans.ValueKind == JsonValueKind.Object)
        {
            cover = GetString(trans, "union_cover", "cover");
        }

        if (string.IsNullOrWhiteSpace(cover))
            return DefaultCover;
        return cover.Replace("{size}", "400");
    }

    private bool IsCurrentRequest(int requestVersion) =>
        !_isDisposed && requestVersion == _requestVersion;

    private void ShowToast(string title, string content, Avalonia.Controls.Notifications.NotificationType type)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _toastManager.ShowDismissibleToast(type, title, content);
        });
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        Interlocked.Increment(ref _requestVersion);
        Songs.Clear();
    }
}
