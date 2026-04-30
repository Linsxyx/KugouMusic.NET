using KuGou.Net.Clients;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Protocol.Raw;
using KuGou.Net.Protocol.Session;
using Microsoft.Extensions.Logging.Abstractions;

namespace KgTest.Services;

internal sealed class TerminalKugouClients
{
    public required KgSessionManager SessionManager { get; init; }
    public required LoginClient Login { get; init; }
    public required RecommendClient Recommend { get; init; }
    public required SearchClient Search { get; init; }
    public required SongClient Song { get; init; }
    public required PlaylistClient Playlist { get; init; }
    public required UserClient User { get; init; }
    public required LyricClient Lyric { get; init; }
    public required RankClient Rank { get; init; }
    public required AlbumClient Album { get; init; }
}

internal static class TerminalKugouClientFactory
{
    public static TerminalKugouClients Create()
    {
        var (transport, sessionManager) = KgHttpClientFactory.CreateWithSession(new TerminalSessionPersistence());

        var rawLogin = new RawLoginApi(transport, sessionManager, NullLogger<RawLoginApi>.Instance);
        var rawSearch = new RawSearchApi(transport);
        var rawUser = new RawUserApi(transport);
        var rawPlaylist = new RawPlaylistApi(transport, NullLogger<RawPlaylistApi>.Instance);
        var rawLyric = new RawLyricApi(transport);
        var rawDiscovery = new RawDiscoveryApi(transport);
        var rawRank = new RawRankApi(transport);
        var rawAlbum = new RawAlbumApi(transport);
        var rawSong = new RawSongApi(transport, sessionManager);

        return new TerminalKugouClients
        {
            SessionManager = sessionManager,
            Login = new LoginClient(rawLogin, sessionManager, NullLogger<LoginClient>.Instance),
            Recommend = new RecommendClient(rawDiscovery, sessionManager),
            Search = new SearchClient(rawSearch, sessionManager),
            Song = new SongClient(rawSong, rawSearch),
            Playlist = new PlaylistClient(rawPlaylist, sessionManager),
            User = new UserClient(rawUser, sessionManager),
            Lyric = new LyricClient(rawLyric),
            Rank = new RankClient(rawRank),
            Album = new AlbumClient(rawAlbum)
        };
    }
}
