namespace KuGou.Net.util;

public static class KuGouConfig
{
    // ================= 身份标识 (Lite版) =================
    public const string AppId = "3116";
    public const string ClientVer = "11436";
    public const string Version = "11436";
    public const string UserAgent = "Android15-1070-11083-46-0-DiscoveryDRADProtocol-wifi";

    // ================= 盐值 (Lite版) =================
    // 用于计算 Key (v5接口)
    public const string V5KeySalt = "185672dd44712f60bb1736df5a377e82";

    // 用于计算 Signature (通用)
    public const string LiteSalt = "LnT6xpN3khm36zse0QzvmgTZ3waWdRSA";

    // ================= 设备指纹 (建议持久化，这里暂时写死) =================
    public const string Dfid = "-";
    public const string Mid = "336d5ebc5436534e61d16e63ddfca327";
    public const string Uuid = "15e772e1213bdd0718d0c1d10d64e06f";


    public const string WebSignatureSalt = "NVPh5oo715z5DIWAeQlhMDsWXXQV4hwt";
}