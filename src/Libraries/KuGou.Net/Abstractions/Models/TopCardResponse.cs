using System.Text.Json;
using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
/// 对应 /top/card 接口 (猜你喜欢/各种推荐卡片) 的响应数据
/// </summary>
public record TopCardResponse : KgBaseModel
{
    [JsonPropertyName("OlexpIds")]
    public string OlexpIds { get; set; } = "";

    [JsonPropertyName("song_list")]
    public List<TopCardSong> Songs { get; set; } = [];

    [JsonPropertyName("song_list_size")]
    public int SongListSize { get; set; }

    [JsonPropertyName("bi_biz")]
    public string BiBiz { get; set; } = "";

    [JsonPropertyName("rec_desc")]
    public string RecommendDescription { get; set; } = "";

    [JsonPropertyName("rec_user_nickname")]
    public string RecommendUserNickname { get; set; } = "";

    [JsonPropertyName("sign")]
    public string Sign { get; set; } = "";

    [JsonPropertyName("card_id")]
    public int CardId { get; set; }
}

/// <summary>
/// 卡片推荐的单曲信息
/// </summary>
public record TopCardSong : KgBaseModel
{
    [JsonPropertyName("songname")]
    public string Name { get; set; } = "";

    [JsonPropertyName("author_name")]
    public string SingerName { get; set; } = "";

    [JsonPropertyName("hash")]
    public string Hash { get; set; } = "";
    
    [JsonPropertyName("album_id")]
    public long AlbumId { get; set; }

    [JsonPropertyName("album_name")]
    public string AlbumName { get; set; } = "";
    
    [JsonPropertyName("songid")]
    public long AudioId { get; set; }
    
    [JsonPropertyName("mixsongid")]
    public string MixSongId { get; set; } = "";
    
    [JsonPropertyName("time_length")]
    public double Duration { get; set; }

    [JsonPropertyName("sizable_cover")]
    public string? SizableCover {
        get => field?.Replace("{size}", "400");
        set;
    } = "";

    [JsonPropertyName("privilege")]
    public int Privilege { get; set; }

    [JsonPropertyName("singerinfo")]
    public JsonElement? SingerInfoRaw { get; set; }

    [JsonIgnore]
    public List<SingerLite> Singers 
    { 
        get
        {
            if (!SingerInfoRaw.HasValue)
                return [];

            try
            {
                return SingerInfoRaw.Value.Deserialize<List<SingerLite>>() ?? [];
            }
            catch
            {
                try
                {
                    var single = SingerInfoRaw.Value.Deserialize<SingerLite>();
                    return single != null ? [single] : [];
                }
                catch
                {
                    return [];
                }
            }
        }
    }
}