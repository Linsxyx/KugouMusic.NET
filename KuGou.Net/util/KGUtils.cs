using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace KuGou.Net.util;

public static class KgUtils
{
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
        if (string.IsNullOrEmpty(input)) return "";
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = MD5.HashData(bytes);
        return Convert.ToHexString(hash).ToLower();
    }

    /// <summary>
    ///     MD5 加密，输入为 bytes (辅助方法)
    /// </summary>
    public static string Md5(byte[] input)
    {
        var hash = MD5.HashData(input);
        return Convert.ToHexString(hash).ToLower();
    }

    public static string CalcNewMid(string guid)
    {
        var md5Hex = Md5(guid);

        // 2. 将 Hex 字符串解析为 BigInteger
        // JS 逻辑是把 Hex 当作无符号大数。
        // 在 C# BigInteger.Parse 中，如果在 Hex 模式下最高位 > 7 会被当成负数。
        // 所以我们在前面拼一个 "0" 确保它被识别为正数。
        var bigInt = BigInteger.Parse("0" + md5Hex, System.Globalization.NumberStyles.HexNumber);

        // 3. 转为十进制字符串
        return bigInt.ToString();
    }
}