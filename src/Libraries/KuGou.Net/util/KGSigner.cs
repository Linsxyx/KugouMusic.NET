using System.Buffers;
using System.Text;
using System.Security.Cryptography;
using ZLinq;

namespace KuGou.Net.util;

public static class KgSigner
{
    private const int MaxStackallocUtf8Bytes = 256;

    public static string CalcV5Key(string hash, string userid, string mid)
    {
        var salt = KuGouConfig.V5KeySalt;

        var raw = $"{hash}{salt}{KuGouConfig.AppId}{mid}{userid}";
        return KgUtils.Md5(raw);
    }


    public static string CalcPostSignature(Dictionary<string, string> queryParams, string jsonBody,
        string salt = KuGouConfig.LiteSalt)
    {
        var sb = new StringBuilder();

        sb.Append(salt);

        foreach (var kv in queryParams.AsValueEnumerable().OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            sb.Append(kv.Key);
            sb.Append('=');
            sb.Append(kv.Value);
        }

        if (!string.IsNullOrEmpty(jsonBody)) sb.Append(jsonBody);

        sb.Append(salt);

        return KgUtils.Md5(sb.ToString());
    }

    public static string CalcPostSignature(Dictionary<string, string> queryParams, byte[] binaryBody,
        string salt = KuGouConfig.LiteSalt)
    {
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);

        void AppendString(string value)
        {
            var byteCount = Encoding.UTF8.GetByteCount(value);
            byte[]? rentedBytes = null;
            Span<byte> utf8Bytes = byteCount <= MaxStackallocUtf8Bytes
                ? stackalloc byte[byteCount]
                : (rentedBytes = ArrayPool<byte>.Shared.Rent(byteCount));

            try
            {
                var bytesWritten = Encoding.UTF8.GetBytes(value, utf8Bytes);
                md5.AppendData(utf8Bytes[..bytesWritten]);
            }
            finally
            {
                if (rentedBytes != null)
                    ArrayPool<byte>.Shared.Return(rentedBytes);
            }
        }

        AppendString(salt);

        foreach (var kv in queryParams.AsValueEnumerable().OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            AppendString(kv.Key);
            AppendString("=");
            AppendString(kv.Value);
        }

        md5.AppendData(binaryBody);
        AppendString(salt);

        Span<byte> hash = stackalloc byte[MD5.HashSizeInBytes];
        var bytesWritten = md5.GetHashAndReset(hash);
        return Convert.ToHexStringLower(hash[..bytesWritten]);
    }

    public static string CalcLoginKey(long clienttimeMs)
    {
        var salt = KuGouConfig.LiteSalt;
        var raw = $"{KuGouConfig.AppId}{salt}{KuGouConfig.ClientVer}{clienttimeMs}";
        return KgUtils.Md5(raw);
    }

    public static string CalcCloudKey(string hash, int pid)
    {
        const string salt = "ebd1ac3134c880bda6a2194537843caa0162e2e7";
        return KgUtils.Md5($"musicclound{hash}{pid}{salt}");
    }

    public static string CalcWebQrSignature(Dictionary<string, string> paramsDict)
    {
        var sb = new StringBuilder();
        const string webSalt = KuGouConfig.WebSignatureSalt;

        sb.Append(webSalt);

        foreach (var kv in paramsDict.AsValueEnumerable().OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            sb.Append(kv.Key);
            sb.Append('=');
            sb.Append(kv.Value);
        }

        sb.Append(webSalt);

        return KgUtils.Md5(sb.ToString());
    }
}
