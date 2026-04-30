using System.Text.Json.Nodes;

namespace KuGou.Net.Protocol.Transport;

public enum SignatureType
{
    Default, // V3/V4 常规签名
    V5, // V5 获取播放链接专用
    Web, // 扫码登录等 Web 接口
    Register, // 设备注册等注册接口
    None // 不签名
}

public class KgRequest
{
    public HttpMethod Method { get; set; } = HttpMethod.Get;

    // API 路径，例如 "/v3/search/song"
    public required string Path { get; set; }

    // 基础参数
    public Dictionary<string, string> Params { get; set; } = new();

    // POST Body (如果是 GET 则忽略)
    public JsonObject? Body { get; set; }

    // 签名策略
    public SignatureType SignatureType { get; set; } = SignatureType.Default;

    // 兼容参考项目 clearDefaultParams：只使用 Params 中显式给出的参数
    public bool ClearDefaultParams { get; set; }

    // 兼容参考项目 notSignature/notSign：保留默认参数，但不追加 signature
    public bool NotSignature { get; set; }

    // 指定路由 (x-router header)，例如 "complexsearch.kugou.com"
    public string? SpecificRouter { get; set; }

    // 覆盖用的 Dfid (很少用，但在 GetPlayUrlAsync 里用到了)
    public string? SpecificDfid { get; set; }

    // 指定 BaseUrl (如果不指定，默认走 gateway)
    public string? BaseUrl { get; set; }


    // 新增：支持原始字符串 Body (用于注册接口发送 Base64)
    public string? RawBody { get; set; }

    // 支持二进制 Body，例如云盘接口发送 AES 后的 bytes
    public byte[]? BinaryBody { get; set; }

    // 新增：自定义 Content-Type
    public string ContentType { get; set; } = "application/json";

    public Dictionary<string, string>? CustomHeaders { get; set; }

    // 当前请求需要覆盖服务端 session 时使用，例如播放链接临时 dfid、userid/token/vipToken
    public Dictionary<string, string>? SessionOverrides { get; set; }
}
