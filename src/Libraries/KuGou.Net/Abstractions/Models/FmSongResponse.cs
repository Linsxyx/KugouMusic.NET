using System.Text.Json;
using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
/// FM 歌曲列表响应数据
/// </summary>
public record FmSongResponse : KgBaseModel
{
    [JsonPropertyName("data")]
    public List<FmSongData> Data { get; set; } = new();
}

public record FmSongData
{
    [JsonPropertyName("fmid")]
    public long FmId { get; set; }

    [JsonPropertyName("fmtype")]
    public int FmType { get; set; }

    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    [JsonPropertyName("size")]
    public int Size { get; set; }

    [JsonPropertyName("songs")]
    public List<FmSongItem> Songs { get; set; } = new();
}

public record FmSongItem : KgBaseModel
{
    [property: JsonPropertyName("name")]
    public string RawName
    {
        get;
        set
        {
            field = value;
            var dashIndex = value?.IndexOf(" - ", StringComparison.Ordinal) ?? -1;
            if (dashIndex > 0)
            {
                SingerName = value![..dashIndex].Trim();
                Name = value[(dashIndex + 3)..].Trim();
            }
            else
            {
                SingerName = "未知歌手";
                Name = value?.Trim() ?? "未知歌曲";
            }
        }
    } = "";

    /// <summary>
    /// 拆分后的纯歌名
    /// </summary>
    [JsonIgnore]
    public string Name { get; private set; } = "";

    /// <summary>
    /// 拆分后的歌手名
    /// </summary>
    [JsonIgnore]
    public string SingerName { get; private set; } = "未知歌手";

    [property: JsonPropertyName("hash")]
    public string Hash { get; set; } = "";

    [property: JsonPropertyName("320hash")]
    public string Hash320 { get; set; } = "";

    [property: JsonPropertyName("hashflac")]
    public string HashFlac { get; set; } = "";

    [property: JsonPropertyName("hash_high")]
    public string HashHigh { get; set; } = "";

    [property: JsonPropertyName("audio_id")]
    public long AudioId { get; set; }

    [property: JsonPropertyName("album_id")]
    [property: JsonConverter(typeof(EmptyStringLongJsonConverter))]
    public long AlbumId { get; set; }

    [property: JsonPropertyName("album_audio_id")]
    public long AlbumAudioId { get; set; }

    [property: JsonPropertyName("time")]
    public int Time { get; set; } // 毫秒

    [property: JsonPropertyName("320time")]
    public int Time320 { get; set; } // 毫秒

    [property: JsonPropertyName("size")]
    public long Size { get; set; }

    [property: JsonPropertyName("320size")]
    public long Size320 { get; set; }

    [property: JsonPropertyName("filesize_flac")]
    public long FileSizeFlac { get; set; }

    [property: JsonPropertyName("privilege")]
    public int Privilege { get; set; }

    [property: JsonPropertyName("vip")]
    public int Vip { get; set; }

    [property: JsonPropertyName("ext")]
    public string Ext { get; set; } = "";

    [property: JsonPropertyName("trans_param")]
    public FmTransParam? TransParam { get; set; }
    
    [property: JsonPropertyName("tracker_info")]
    public FmTrackerInfo? TrackerInfo { get; set; }

    [property: JsonPropertyName("all_privs")]
    public JsonElement? AllPrivs { get; set; }
}

public record FmTransParam
{
    /// <summary>
    /// 封面 (自动处理 {size} 替换为 400)
    /// </summary>
    [JsonPropertyName("union_cover")]
    public string? UnionCover
    {
        get => field?.Replace("{size}", "400");
        set;
    }

    [JsonPropertyName("language")]
    public string Language { get; set; } = "";

    [JsonPropertyName("is_original")]
    public int IsOriginal { get; set; }

    [JsonPropertyName("cid")]
    public long Cid { get; set; }
}

public record FmTrackerInfo
{
    [JsonPropertyName("auth")]
    public string Auth { get; set; } = "";

    [JsonPropertyName("module_id")]
    public int ModuleId { get; set; }

    [JsonPropertyName("open_time")]
    public string OpenTime { get; set; } = "";
}

internal sealed class EmptyStringLongJsonConverter : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Number => reader.GetInt64(),
            JsonTokenType.String => ParseStringValue(reader.GetString()),
            JsonTokenType.Null => 0,
            _ => throw new JsonException(
                $"Unexpected token {reader.TokenType} when parsing a flexible long value.")
        };
    }

    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }

    private static long ParseStringValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        if (long.TryParse(value, out var parsed))
        {
            return parsed;
        }

        throw new JsonException($"Cannot parse '{value}' as a long value.");
    }
}
