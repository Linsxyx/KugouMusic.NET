using System.Text.Json;
using System.Text.Json.Serialization;

namespace KuGou.Net.Abstractions.Models;

/// <summary>
///     登录响应数据
/// </summary>
public record LoginResponse : KgBaseModel
{
    /// <summary>
    ///     用户 ID。
    /// </summary>
    [property: JsonPropertyName("userid")] public long? UserId { get; set; }

    /// <summary>
    ///     登录 Token。
    /// </summary>
    [property: JsonPropertyName("token")] public string? Token { get; set; }

    /// <summary>
    ///     附加登录凭证 `t1`。
    /// </summary>
    [property: JsonPropertyName("t1")] public string? T1 { get; set; }

    /// <summary>
    ///     多账号登录时返回的候选账号数据。
    /// </summary>
    [property: JsonPropertyName("data")]
    [property: JsonConverter(typeof(LoginMultiAccountDataJsonConverter))]
    public LoginMultiAccountData? Data { get; set; }

    /// <summary>
    ///     是否需要调用方选择账号后携带 userid 重新登录。
    /// </summary>
    [JsonIgnore]
    public bool RequiresUserSelection => ErrorCode == 34175 && Data?.InfoList is { Count: > 0 };

    [JsonIgnore]
    public string? FailureMessage => Data?.Message
                                     ?? GetExtraString("error_msg")
                                     ?? GetExtraString("errmsg")
                                     ?? GetExtraString("msg")
                                     ?? GetExtraString("message");
}

public record LoginMultiAccountData
{
    [property: JsonPropertyName("info_list")] public List<LoginAccountInfo> InfoList { get; set; } = [];

    [JsonIgnore] public string? Message { get; set; }
}

public record LoginAccountInfo
{
    [property: JsonPropertyName("nickname")] public string? Nickname { get; set; }

    [property: JsonPropertyName("pic")] public string? Pic { get; set; }

    [property: JsonPropertyName("userid")] public long UserId { get; set; }

    [property: JsonPropertyName("appid")] public int AppId { get; set; }

    [property: JsonPropertyName("username")] public string? Username { get; set; }
}

/// <summary>
///     发送验证码响应
/// </summary>
public record SendCodeResponse : KgBaseModel
{
    /// <summary>
    ///     接口返回状态码。
    /// </summary>
    [property: JsonPropertyName("code")] public long Code { get; set; }
}

public sealed class LoginMultiAccountDataJsonConverter : JsonConverter<LoginMultiAccountData?>
{
    public override LoginMultiAccountData? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => new LoginMultiAccountData { Message = reader.GetString() },
            JsonTokenType.StartObject => ReadObject(ref reader),
            _ => SkipUnexpectedValue(ref reader)
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        LoginMultiAccountData? value,
        JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        if (value.InfoList.Count == 0 && !string.IsNullOrWhiteSpace(value.Message))
        {
            writer.WriteStringValue(value.Message);
            return;
        }

        writer.WriteStartObject();
        writer.WritePropertyName("info_list");
        writer.WriteStartArray();

        foreach (var account in value.InfoList)
        {
            writer.WriteStartObject();
            writer.WriteString("nickname", account.Nickname);
            writer.WriteString("pic", account.Pic);
            writer.WriteNumber("userid", account.UserId);
            writer.WriteNumber("appid", account.AppId);
            writer.WriteString("username", account.Username);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static LoginMultiAccountData ReadObject(ref Utf8JsonReader reader)
    {
        var data = new LoginMultiAccountData();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return data;

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                reader.Skip();
                continue;
            }

            var propertyName = reader.GetString();
            if (!reader.Read())
                return data;

            if (propertyName is "info_list")
                ReadInfoList(ref reader, data.InfoList);
            else
                reader.Skip();
        }

        return data;
    }

    private static void ReadInfoList(ref Utf8JsonReader reader, List<LoginAccountInfo> accounts)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            reader.Skip();
            return;
        }

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
                return;

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                reader.Skip();
                continue;
            }

            accounts.Add(ReadAccount(ref reader));
        }
    }

    private static LoginAccountInfo ReadAccount(ref Utf8JsonReader reader)
    {
        var account = new LoginAccountInfo();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return account;

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                reader.Skip();
                continue;
            }

            var propertyName = reader.GetString();
            if (!reader.Read())
                return account;

            switch (propertyName)
            {
                case "nickname":
                    account.Nickname = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                    break;
                case "pic":
                    account.Pic = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                    break;
                case "userid":
                    account.UserId = ReadInt64(ref reader);
                    break;
                case "appid":
                    account.AppId = ReadInt32(ref reader);
                    break;
                case "username":
                    account.Username = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        return account;
    }

    private static LoginMultiAccountData? SkipUnexpectedValue(ref Utf8JsonReader reader)
    {
        reader.Skip();
        return null;
    }

    private static int ReadInt32(ref Utf8JsonReader reader)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Number when reader.TryGetInt32(out var value) => value,
            JsonTokenType.String when int.TryParse(reader.GetString(), out var value) => value,
            _ => 0
        };
    }

    private static long ReadInt64(ref Utf8JsonReader reader)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Number when reader.TryGetInt64(out var value) => value,
            JsonTokenType.String when long.TryParse(reader.GetString(), out var value) => value,
            _ => 0L
        };
    }
}
