using System.IO.Compression;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KuGou.Net.util;

public static class KgCrypto
{
    // ---------------- AES 部分 ----------------

    /// <summary>
    ///     AES 加密
    /// </summary>
    /// <returns>返回 (HexStr, Key)</returns>
    public static (string str, string key) AesEncrypt(string data, string key = null, string iv = null)
    {
        var tempKey = key;
        string actualKeyStr;
        string actualIvStr;

        if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(iv))
        {
            actualKeyStr = key;
            actualIvStr = iv;
        }
        else
        {
            tempKey = key ?? KgUtils.RandomString().ToLower();
            actualKeyStr = KgUtils.Md5(tempKey);
            actualIvStr = actualKeyStr.Substring(actualKeyStr.Length - 16);
        }


        var keyBytes = Encoding.UTF8.GetBytes(actualKeyStr);
        var ivBytes = Encoding.UTF8.GetBytes(actualIvStr);
        var dataBytes = Encoding.UTF8.GetBytes(data);

        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = keyBytes;
        aes.IV = ivBytes;

        using var encryptor = aes.CreateEncryptor();
        var encryptedBytes = encryptor.TransformFinalBlock(dataBytes, 0, dataBytes.Length);

        return (Convert.ToHexString(encryptedBytes).ToLower(), tempKey);
    }

    /// <summary>
    ///     AES 解密
    /// </summary>
    public static string AesDecrypt(string hexData, string key)
    {
        var actualKeyStr = KgUtils.Md5(key);
        var actualIvStr = actualKeyStr.Substring(actualKeyStr.Length - 16);

        var keyBytes = Encoding.UTF8.GetBytes(actualKeyStr);
        var ivBytes = Encoding.UTF8.GetBytes(actualIvStr);
        var encryptedBytes = Convert.FromHexString(hexData);

        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = keyBytes;
        aes.IV = ivBytes;

        using var decryptor = aes.CreateDecryptor();
        var decryptedBytes = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);

        return Encoding.UTF8.GetString(decryptedBytes);
    }

    // ---------------- RSA 部分 ----------------

    /// <summary>
    ///     RSA 加密
    /// </summary>
    public static string RsaEncryptNoPadding(string data, bool isLite = true)
    {
        var pem = isLite ? Constants.PublicLiteRasKey : Constants.PublicRasKey;
        var dataBytes = Encoding.UTF8.GetBytes(data);

        var paddedData = new byte[128];
        Array.Copy(dataBytes, 0, paddedData, 0, dataBytes.Length);

        using var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        var parameters = rsa.ExportParameters(false);


        var m = new BigInteger(paddedData, true, true);

        var e = new BigInteger(parameters.Exponent!, true, true);
        var n = new BigInteger(parameters.Modulus!, true, true);


        var c = BigInteger.ModPow(m, e, n);


        var resBytes = c.ToByteArray(true, true);

        if (resBytes.Length < 128)
        {
            var temp = new byte[128];
            Array.Copy(resBytes, 0, temp, 128 - resBytes.Length, resBytes.Length);
            resBytes = temp;
        }
        else if (resBytes.Length > 128)
        {
            var temp = new byte[128];
            Array.Copy(resBytes, resBytes.Length - 128, temp, 0, 128);
            resBytes = temp;
        }

        return Convert.ToHexString(resBytes).ToLower();
    }

    /// <summary>
    ///     RSA 加密
    /// </summary>
    public static string RsaEncryptPkcs1(string data, bool isLite = true)
    {
        var pem = isLite ? Constants.PublicLiteRasKey : Constants.PublicRasKey;
        var dataBytes = Encoding.UTF8.GetBytes(data);

        using var rsa = RSA.Create();
        rsa.ImportFromPem(pem);


        var encryptedBytes = rsa.Encrypt(dataBytes, RSAEncryptionPadding.Pkcs1);
        return Convert.ToHexString(encryptedBytes).ToLower();
    }

    public static (string str, string key) PlaylistAesEncrypt(object data)
    {
        var json = JsonSerializer.Serialize(data, KgUtils.JsonOptions);
        var key = KgUtils.RandomString(6).ToLower();

        var md5Key = KgUtils.Md5(key);
        var encryptKey = md5Key.Substring(0, 16);
        var iv = md5Key.Substring(16, 16);

        using var aes = Aes.Create();
        aes.KeySize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = Encoding.UTF8.GetBytes(encryptKey);
        aes.IV = Encoding.UTF8.GetBytes(iv);

        using var encryptor = aes.CreateEncryptor();
        var dataBytes = Encoding.UTF8.GetBytes(json);
        var encrypted = encryptor.TransformFinalBlock(dataBytes, 0, dataBytes.Length);

        return (Convert.ToBase64String(encrypted), key);
    }


    public static string PlaylistAesDecrypt(string str, string key)
    {
        var md5Key = KgUtils.Md5(key);
        var encryptKey = md5Key.Substring(0, 16);
        var iv = md5Key.Substring(16, 16);

        using var aes = Aes.Create();
        aes.KeySize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = Encoding.UTF8.GetBytes(encryptKey);
        aes.IV = Encoding.UTF8.GetBytes(iv);

        using var decryptor = aes.CreateDecryptor();
        var encryptedBytes = Convert.FromBase64String(str);
        var decrypted = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);

        return Encoding.UTF8.GetString(decrypted);
    }

    public static string DecodeLyrics(string base64Str)
    {
        if (string.IsNullOrEmpty(base64Str)) return "";
        var bytes = Convert.FromBase64String(base64Str);
        if (bytes.Length <= 4) return "";


        byte[] enKey = { 64, 71, 97, 119, 94, 50, 116, 71, 81, 54, 49, 45, 206, 210, 110, 105 };

        var krcBytes = new byte[bytes.Length - 4];
        Array.Copy(bytes, 4, krcBytes, 0, krcBytes.Length);

        for (var i = 0; i < krcBytes.Length; i++) krcBytes[i] = (byte)(krcBytes[i] ^ enKey[i % enKey.Length]);

        try
        {
            using var msInput = new MemoryStream(krcBytes);
            using var zlib = new ZLibStream(msInput, CompressionMode.Decompress);
            using var msOutput = new MemoryStream();
            zlib.CopyTo(msOutput);
            return Encoding.UTF8.GetString(msOutput.ToArray());
        }
        catch
        {
            return "";
        }
    }
}