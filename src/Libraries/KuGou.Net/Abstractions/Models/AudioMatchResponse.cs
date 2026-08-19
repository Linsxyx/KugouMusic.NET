using System.Text.Json;
using System.Text.Json.Serialization;
using KuGou.Net.util;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
///     听歌识曲响应。
/// </summary>
public record AudioMatchResponse : KgBaseModel
{
    [property: JsonPropertyName("server_time")] public int? ServerTime { get; set; }

    [property: JsonPropertyName("pcm_second")] public string PcmSecond { get; set; } = string.Empty;

    [property: JsonPropertyName("process")] public JsonElement? Process { get; set; }

    [property: JsonPropertyName("data")] public List<AudioMatchItem> Data { get; set; } = [];
}

/// <summary>
///     单条识曲结果。
/// </summary>
public record AudioMatchItem
{
    [property: JsonPropertyName("songid")] public long SongId { get; set; }

    [property: JsonPropertyName("mixsongid")] public long MixSongId { get; set; }

    [property: JsonPropertyName("audio_id")] public long AudioId { get; set; }

    [property: JsonPropertyName("album_id")] public string AlbumId { get; set; } = string.Empty;

    [property: JsonPropertyName("scid")] public string ScId { get; set; } = string.Empty;

    [property: JsonPropertyName("similar_scid")] public long? SimilarScId { get; set; }

    [property: JsonPropertyName("songname")] public string SongName { get; set; } = string.Empty;

    [property: JsonPropertyName("songNameSuffix")] public string SongNameSuffix { get; set; } = string.Empty;

    [property: JsonPropertyName("singername")] public string SingerName { get; set; } = string.Empty;

    [property: JsonPropertyName("remark")] public string Remark { get; set; } = string.Empty;

    [property: JsonPropertyName("str_song_type")] public string SongTypeText { get; set; } = string.Empty;

    [property: JsonPropertyName("source")] public string Source { get; set; } = string.Empty;

    [property: JsonPropertyName("union_cover")]
    public string UnionCover
    {
        get;
        set => field = value.Replace("{size}", "400");
    } = string.Empty;

    [property: JsonPropertyName("hash_128")] public string Hash128 { get; set; } = string.Empty;

    [property: JsonPropertyName("hash_320")] public string Hash320 { get; set; } = string.Empty;

    [property: JsonPropertyName("hash_flac")] public string HashFlac { get; set; } = string.Empty;

    [property: JsonPropertyName("dist")] public double? Distance { get; set; }

    [property: JsonPropertyName("dist_nomelody")] public double? DistanceNoMelody { get; set; }

    [property: JsonPropertyName("timeoffset")] public int? TimeOffset { get; set; }

    [property: JsonPropertyName("timelength_128")] public long? TimeLength128 { get; set; }

    [property: JsonPropertyName("timelength_320")] public long? TimeLength320 { get; set; }

    [property: JsonPropertyName("timelength_flac")] public long? TimeLengthFlac { get; set; }

    [property: JsonPropertyName("privilege")] public int? Privilege { get; set; }

    [property: JsonPropertyName("is_play_free")] public int? IsPlayFree { get; set; }

    [property: JsonPropertyName("is_original")] public string IsOriginal { get; set; } = string.Empty;

    [property: JsonPropertyName("lyric_id")] public long? LyricId { get; set; }

    [property: JsonPropertyName("authors")] public List<AudioMatchAuthor> Authors { get; set; } = [];

    [property: JsonPropertyName("album")]
    [property: JsonConverter(typeof(AudioMatchAlbumListJsonConverter))]
    public List<AudioMatchAlbum> Album { get; set; } = [];

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extras { get; set; }
}

public record AudioMatchAuthor
{
    [property: JsonPropertyName("author_id")] public long AuthorId { get; set; }

    [property: JsonPropertyName("author_name")] public string AuthorName { get; set; } = string.Empty;

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extras { get; set; }
}

public record AudioMatchAlbum
{
    [property: JsonPropertyName("albumid")] public string AlbumId { get; set; } = string.Empty;

    [property: JsonPropertyName("albumname")] public string AlbumName { get; set; } = string.Empty;

    [property: JsonPropertyName("publish_date")] public string PublishDate { get; set; } = string.Empty;

    [property: JsonPropertyName("img")] public string Image { get; set; } = string.Empty;

    [property: JsonPropertyName("scid")] public string ScId { get; set; } = string.Empty;

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extras { get; set; }
}

/// <summary>
///     兼容 album 字段偶发返回 [] / {} / 单对象 三种形态。
/// </summary>
public sealed class AudioMatchAlbumListJsonConverter : JsonConverter<List<AudioMatchAlbum>>
{
    public override List<AudioMatchAlbum> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return [];
            case JsonTokenType.StartArray:
                return JsonSerializer.Deserialize(ref reader, AppJsonContext.Default.ListAudioMatchAlbum) ?? [];
            case JsonTokenType.StartObject:
            {
                using var document = JsonDocument.ParseValue(ref reader);
                var root = document.RootElement;
                if (!root.EnumerateObject().Any())
                    return [];

                var album = root.Deserialize(AppJsonContext.Default.AudioMatchAlbum);
                return album is null ? [] : [album];
            }
            default:
                throw new JsonException($"Unexpected token {reader.TokenType} when parsing audio match album list.");
        }
    }

    public override void Write(Utf8JsonWriter writer, List<AudioMatchAlbum> value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, AppJsonContext.Default.ListAudioMatchAlbum);
    }
}
