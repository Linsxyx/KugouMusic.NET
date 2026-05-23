using System.Text.Json;

namespace KgWebApi.Net.Models;

public sealed record NeteaseAudioMatchRequest(int Duration, string AudioFP);

public sealed record NeteaseAudioMatchResponse(int Code, JsonElement? Data, string? AudioFP = null);
