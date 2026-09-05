using System.Text.Json;

namespace KuGou.Net.Native;

internal sealed record NativeRequest(
    string Method,
    string Path,
    Dictionary<string, JsonElement>? Query = null,
    JsonElement? Body = null);

internal sealed record NativeLoginRequest(string Mobile, string Code, string? UserId = null);

internal sealed record NativeLoginAccountSelection(
    int Status,
    int ErrorCode,
    bool RequiresUserSelection,
    string Message,
    IReadOnlyList<NativeLoginAccount> Accounts);

internal sealed record NativeLoginAccount(
    long UserId,
    string? Nickname,
    string? Pic,
    int AppId,
    string? Username);

internal sealed record NativeAddTracksRequest(string ListId, List<NativeAddSongItemDto> Songs);
