using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Adapters.Lyrics;
using KuGou.Net.Protocol.Session;

namespace KuGou.Net.util;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString
)]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(JsonNode))]
[JsonSerializable(typeof(JsonObject))]
[JsonSerializable(typeof(JsonArray))]
[JsonSerializable(typeof(JsonValue))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(KgSession))]
[JsonSerializable(typeof(LoginResponse))]
[JsonSerializable(typeof(QrKeyResponse))]
[JsonSerializable(typeof(QrStatusResponse))]
[JsonSerializable(typeof(SendCodeResponse))]
[JsonSerializable(typeof(RefreshTokenResponse))]
[JsonSerializable(typeof(SearchResultData))]
[JsonSerializable(typeof(PlayUrlData))]
[JsonSerializable(typeof(PlaylistSongResponse))]
[JsonSerializable(typeof(PlaylistInfo))]
[JsonSerializable(typeof(List<PlaylistInfo>))]
[JsonSerializable(typeof(UserPlaylistResponse))]
[JsonSerializable(typeof(UserPlaylistItem))]
[JsonSerializable(typeof(List<UserPlaylistItem>))]
[JsonSerializable(typeof(LanguageContainer))]
[JsonSerializable(typeof(SearchHotResponse))]
[JsonSerializable(typeof(List<SearchHotCategory>))]
[JsonSerializable(typeof(List<SearchHotKeyword>))]
[JsonSerializable(typeof(VipReceiveHistoryResponse))]
[JsonSerializable(typeof(UserVipResponse))]
[JsonSerializable(typeof(UserDetailModel))]
[JsonSerializable(typeof(OneDayVipModel))]
[JsonSerializable(typeof(UpgradeVipModel))]
[JsonSerializable(typeof(List<BusiVipInfo>))]
[JsonSerializable(typeof(DailyRecommendResponse))]
[JsonSerializable(typeof(List<DailyRecommendSong>))]
internal partial class AppJsonContext : JsonSerializerContext
{
}