using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
/// 音乐评论列表响应数据 
/// </summary>
public record MusicCommentResponse : KgBaseModel
{
    [property: JsonPropertyName("msg")]
    public string Msg { get; set; } = string.Empty;

    [property: JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [property: JsonPropertyName("childrenid")]
    public string ChildrenId { get; set; } = string.Empty;

    [property: JsonPropertyName("count")]
    public int Count { get; set; }

    [property: JsonPropertyName("combine_count")]
    public int CombineCount { get; set; }

    [property: JsonPropertyName("current_page")]
    public int CurrentPage { get; set; }

    [property: JsonPropertyName("maxPage")]
    public int MaxPage { get; set; }

    [property: JsonPropertyName("list")]
    public List<MusicCommentItem> Comments { get; set; } = new();

    [property: JsonPropertyName("hot_word_list")]
    public List<CommentHotWord>? HotWordList { get; set; }

    [property: JsonPropertyName("classify_list")]
    public List<CommentClassifyItem>? ClassifyList { get; set; }

    [property: JsonPropertyName("tag")]
    public List<CommentTag>? Tags { get; set; }

    [property: JsonPropertyName("config")]
    public CommentConfig? Config { get; set; }

    [property: JsonPropertyName("song_score")]
    public CommentSongScore? SongScore { get; set; }
}

/// <summary>
/// 单条评论内容
/// </summary>
public record MusicCommentItem
{
    [property: JsonPropertyName("id")]
    public long Id { get; set; }

    [property: JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [property: JsonPropertyName("addtime")]
    public string AddTime { get; set; } = string.Empty;

    [property: JsonPropertyName("reply_num")]
    public int ReplyNum { get; set; }

    [property: JsonPropertyName("user_id")]
    public long UserId { get; set; }

    [property: JsonPropertyName("user_name")]
    public string UserName { get; set; } = string.Empty;

    [property: JsonPropertyName("user_pic")]
    public string? UserPic 
    { 
        get => field?.Replace("{size}", "150"); 
        set; 
    }

    [property: JsonPropertyName("user_sex")]
    public int UserSex { get; set; }

    [property: JsonPropertyName("like")]
    public CommentLikeInfo? Like { get; set; }

    [property: JsonPropertyName("images")]
    public List<CommentImage>? Images { get; set; }

    [property: JsonPropertyName("location")]
    public string? Location { get; set; }

    [property: JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    [property: JsonPropertyName("score")]
    public long Score { get; set; }

    [property: JsonPropertyName("vipinfo")]
    public CommentVipInfo? VipInfo { get; set; }

    [property: JsonPropertyName("udetail")]
    public CommentUserDetail? UserDetail { get; set; }

    [property: JsonPropertyName("machine_tail")]
    public string? MachineTail { get; set; }

    [property: JsonPropertyName("tail")]
    public CommentTailInfo? Tail { get; set; }
}

public record CommentLikeInfo
{
    [property: JsonPropertyName("count")]
    public int Count { get; set; }

    [property: JsonPropertyName("haslike")]
    public bool HasLike { get; set; }

    [property: JsonPropertyName("likenum")]
    public int LikeNum { get; set; }
}

public record CommentImage
{
    [property: JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [property: JsonPropertyName("width")]
    public int Width { get; set; }

    [property: JsonPropertyName("height")]
    public int Height { get; set; }
}

public record CommentVipInfo
{
    [property: JsonPropertyName("vip_type")]
    public int VipType { get; set; }

    [property: JsonPropertyName("m_type")]
    public int MType { get; set; }

    [property: JsonPropertyName("user_type")]
    public int UserType { get; set; }
}

public record CommentUserDetail
{
    [property: JsonPropertyName("medal_type")]
    public string? MedalType { get; set; }

    [property: JsonPropertyName("medal_roll_word")]
    public string? MedalRollWord { get; set; }

    [property: JsonPropertyName("word_v3")]
    public string? WordV3 { get; set; }

    [property: JsonPropertyName("pendant_name")]
    public string? PendantName { get; set; }

    [property: JsonPropertyName("pendant_url")]
    public string? PendantUrl { get; set; }
}

public record CommentTailInfo
{
    [property: JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [property: JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public record CommentHotWord
{
    [property: JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [property: JsonPropertyName("count")]
    public int Count { get; set; }
}

public record CommentClassifyItem
{
    [property: JsonPropertyName("id")]
    public int Id { get; set; }

    [property: JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [property: JsonPropertyName("icon")]
    public string Icon { get; set; } = string.Empty;

    [property: JsonPropertyName("cnt")]
    public int Count { get; set; }
}

public record CommentTag
{
    [property: JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [property: JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [property: JsonPropertyName("count")]
    public int Count { get; set; }
}

public record CommentConfig
{
    [property: JsonPropertyName("emptyTip")]
    public string EmptyTip { get; set; } = string.Empty;

    [property: JsonPropertyName("input_hint")]
    public string InputHint { get; set; } = string.Empty;
}

public record CommentSongScore
{
    [property: JsonPropertyName("score_user_count")]
    public int ScoreUserCount { get; set; }

    [property: JsonPropertyName("song_score")]
    public double SongScore { get; set; }
}