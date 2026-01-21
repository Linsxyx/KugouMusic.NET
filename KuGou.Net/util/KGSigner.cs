using System.Text;

namespace KuGou.Net.util;

public static class KgSigner
{
    public static string CalcV5Key(string hash, string userid, string mid)
    {
        var salt = KuGouConfig.V5KeySalt;

        var raw = $"{hash}{salt}{KuGouConfig.AppId}{mid}{userid}";
        return KgUtils.Md5(raw);
    }


    public static string CalcPostSignature(Dictionary<string, string> queryParams, string jsonBody)
    {
        var sb = new StringBuilder();
        var salt = KuGouConfig.LiteSalt;

        sb.Append(salt);

        foreach (var kv in queryParams.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            sb.Append(kv.Key);
            sb.Append('=');
            sb.Append(kv.Value);
        }

        if (!string.IsNullOrEmpty(jsonBody)) sb.Append(jsonBody);

        sb.Append(salt);

        return KgUtils.Md5(sb.ToString());
    }

    public static string CalcLoginKey(long clienttimeMs)
    {
        var salt = KuGouConfig.LiteSalt;
        var raw = $"{KuGouConfig.AppId}{salt}{KuGouConfig.ClientVer}{clienttimeMs}";
        return KgUtils.Md5(raw);
    }

    public static string CalcWebQrSignature(Dictionary<string, string> paramsDict)
    {
        var sb = new StringBuilder();
        const string webSalt = KuGouConfig.WebSignatureSalt;

        sb.Append(webSalt);

        foreach (var kv in paramsDict.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            sb.Append(kv.Key);
            sb.Append('=');
            sb.Append(kv.Value);
        }

        sb.Append(webSalt);

        return KgUtils.Md5(sb.ToString());
    }
}