using System.Buffers;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace KuGou.Net.util;

public static class KgUtils
{
    private const int MaxStackallocUtf8Bytes = 256;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
        TypeInfoResolver = AppJsonContext.Default
    };

    /// <summary>
    ///     生成指定长度的随机字符串
    /// </summary>
    public static string RandomString(int length = 16)
    {
        const string chars = "1234567890ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var sb = new StringBuilder(length);
        var rnd = Random.Shared;
        for (var i = 0; i < length; i++) sb.Append(chars[rnd.Next(chars.Length)]);
        return sb.ToString();
    }

    /// <summary>
    ///     MD5 加密，返回小写 Hex 字符串
    /// </summary>
    public static string Md5(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var byteCount = Encoding.UTF8.GetByteCount(input);
        byte[]? rentedBytes = null;
        Span<byte> utf8Bytes = byteCount <= MaxStackallocUtf8Bytes
            ? stackalloc byte[byteCount]
            : (rentedBytes = ArrayPool<byte>.Shared.Rent(byteCount));

        try
        {
            var bytesWritten = Encoding.UTF8.GetBytes(input, utf8Bytes);
            Span<byte> hash = stackalloc byte[MD5.HashSizeInBytes];
            MD5.HashData(utf8Bytes[..bytesWritten], hash);
            return Convert.ToHexStringLower(hash);
        }
        finally
        {
            if (rentedBytes != null)
                ArrayPool<byte>.Shared.Return(rentedBytes);
        }
    }

    public static string CalcNewMid(string guid)
    {
        var md5Hex = Md5(guid);
        var bigInt = BigInteger.Parse("0" + md5Hex, NumberStyles.HexNumber);

        return bigInt.ToString();
    }
}
