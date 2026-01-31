namespace KuGou.Net.Protocol.Session;

public class KgSession
{
    public string UserId { get; set; } = "0";
    public string Token { get; set; } = "";
    public string VipType { get; set; } = "0";
    public string VipToken { get; set; } = "";
    public string Dfid { get; set; } = "-";
    public string Mid { get; set; } = "-";
    public string Uuid { get; set; } = "-";
    
    // === 新增：T1/T2 校验所需的持久化指纹 ===
    public string InstallDev { get; set; } = ""; 
    // 对应 JS: params.cookie?.KUGOU_API_MAC
    public string InstallMac { get; set; } = ""; 
    // 对应 JS: params.cookie?.KUGOU_API_GUID
    public string InstallGuid { get; set; } = ""; 
    
    public string? T1 { get; set; } = "";
}