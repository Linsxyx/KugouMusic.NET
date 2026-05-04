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
    
    public string InstallDev { get; set; } = "";
    
    public string InstallMac { get; set; } = "";
    
    public string InstallGuid { get; set; } = "";

    public string? T1 { get; set; } = "";
}