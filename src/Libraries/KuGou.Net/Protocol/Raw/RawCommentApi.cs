using System.Text.Json;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Protocol.Transport;

namespace KuGou.Net.Protocol.Raw;

public class RawCommentApi(IKgTransport transport)
{
    public Task<JsonElement> GetMusicCommentsAsync(string mixSongId, int page = 1, int pageSize = 30,
        int showClassify = 1, int showHotwordList = 1)
    {
        return SendCommentListAsync("/mcomment/v1/cmtlist", new Dictionary<string, string>
        {
            ["mixsongid"] = mixSongId,
            ["need_show_image"] = "1",
            ["p"] = page.ToString(),
            ["pagesize"] = pageSize.ToString(),
            ["show_classify"] = showClassify.ToString(),
            ["show_hotword_list"] = showHotwordList.ToString(),
            ["extdata"] = "0",
            ["code"] = "fc4be23b4e972707f36b8a828a93ba8a"
        });
    }

    public Task<JsonElement> GetPlaylistCommentsAsync(string id, int page = 1, int pageSize = 30,
        int showClassify = 1, int showHotwordList = 1)
    {
        return SendCommentListAsync("/m.comment.service/v1/cmtlist", new Dictionary<string, string>
        {
            ["childrenid"] = id,
            ["need_show_image"] = "1",
            ["p"] = page.ToString(),
            ["pagesize"] = pageSize.ToString(),
            ["show_classify"] = showClassify.ToString(),
            ["show_hotword_list"] = showHotwordList.ToString(),
            ["code"] = "ca53b96fe5a1d9c22d71c8f522ef7c4f",
            ["content_type"] = "0",
            ["tag"] = "5"
        });
    }

    public Task<JsonElement> GetAlbumCommentsAsync(string id, int page = 1, int pageSize = 30,
        int showClassify = 1, int showHotwordList = 1)
    {
        return SendCommentListAsync("/m.comment.service/v1/cmtlist", new Dictionary<string, string>
        {
            ["childrenid"] = id,
            ["need_show_image"] = "1",
            ["p"] = page.ToString(),
            ["pagesize"] = pageSize.ToString(),
            ["show_classify"] = showClassify.ToString(),
            ["show_hotword_list"] = showHotwordList.ToString(),
            ["code"] = "94f1792ced1df89aa68a7939eaf2efca"
        });
    }

    public Task<JsonElement> GetCommentCountAsync(string? hash = null, string? specialId = null)
    {
        var parameters = new Dictionary<string, string>
        {
            ["r"] = "comments/getcommentsnum",
            ["code"] = "fc4be23b4e972707f36b8a828a93ba8a"
        };

        if (!string.IsNullOrWhiteSpace(hash))
            parameters["hash"] = hash;
        else if (!string.IsNullOrWhiteSpace(specialId))
            parameters["childrenid"] = specialId;

        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Get,
            Path = "/index.php",
            Params = parameters,
            SpecificRouter = "sum.comment.service.kugou.com",
            SignatureType = SignatureType.Web
        });
    }

    public Task<JsonElement> GetFloorCommentsAsync(
        string? specialId,
        string tid,
        string? mixSongId = null,
        string resourceType = "song",
        int page = 1,
        int pageSize = 30,
        int showClassify = 1,
        int showHotwordList = 1,
        string? code = null)
    {
        const string songCode = "fc4be23b4e972707f36b8a828a93ba8a";
        const string playlistCode = "ca53b96fe5a1d9c22d71c8f522ef7c4f";
        const string albumCode = "94f1792ced1df89aa68a7939eaf2efca";

        var normalizedType = resourceType.ToLowerInvariant();
        var resolvedCode = !string.IsNullOrWhiteSpace(code)
            ? code
            : normalizedType switch
            {
                "playlist" => playlistCode,
                "album" => albumCode,
                _ => songCode
            };

        var useServiceEndpoint = normalizedType is "playlist" or "album" ||
                                 resolvedCode is playlistCode or albumCode;

        var parameters = new Dictionary<string, string>
        {
            ["childrenid"] = specialId ?? "",
            ["need_show_image"] = "1",
            ["p"] = page.ToString(),
            ["pagesize"] = pageSize.ToString(),
            ["show_classify"] = showClassify.ToString(),
            ["show_hotword_list"] = showHotwordList.ToString(),
            ["code"] = resolvedCode,
            ["tid"] = tid
        };

        if (!string.IsNullOrWhiteSpace(mixSongId))
            parameters["mixsongid"] = mixSongId;

        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = useServiceEndpoint ? "/m.comment.service/v1/hot_replylist" : "/mcomment/v1/hot_replylist",
            Params = parameters,
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetMusicCommentClassifyAsync(string mixSongId, string typeId, int page = 1,
        int pageSize = 30, int sort = 1)
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/mcomment/v1/cmt_classify_list",
            Params = new Dictionary<string, string>
            {
                ["mixsongid"] = mixSongId,
                ["need_show_image"] = "1",
                ["page"] = page.ToString(),
                ["pagesize"] = pageSize.ToString(),
                ["type_id"] = typeId,
                ["extdata"] = "0",
                ["code"] = "fc4be23b4e972707f36b8a828a93ba8a",
                ["sort_method"] = sort == 2 ? "2" : "1"
            },
            SignatureType = SignatureType.Default
        });
    }

    public Task<JsonElement> GetMusicCommentHotwordAsync(string mixSongId, string hotWord, int page = 1,
        int pageSize = 30)
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = "/mcomment/v1/get_hot_word",
            Params = new Dictionary<string, string>
            {
                ["mixsongid"] = mixSongId,
                ["need_show_image"] = "1",
                ["p"] = page.ToString(),
                ["pagesize"] = pageSize.ToString(),
                ["hot_word"] = hotWord,
                ["extdata"] = "0",
                ["code"] = "fc4be23b4e972707f36b8a828a93ba8a"
            },
            SignatureType = SignatureType.Default
        });
    }

    private Task<JsonElement> SendCommentListAsync(string path, Dictionary<string, string> parameters)
    {
        return transport.SendAsync(new KgRequest
        {
            Method = HttpMethod.Post,
            Path = path,
            Params = parameters,
            SignatureType = SignatureType.Default
        });
    }
}
