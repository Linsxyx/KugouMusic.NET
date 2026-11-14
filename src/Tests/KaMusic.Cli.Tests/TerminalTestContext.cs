using System.Net;
using System.Text.Json;
using KaMusic.Cli.Models;
using KaMusic.Cli.Services;
using KuGou.Net.Clients;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Protocol.Raw;
using KuGou.Net.Protocol.Session;
using KuGou.Net.Protocol.Transport;
using Microsoft.Extensions.Logging.Abstractions;

namespace KaMusic.Cli.Tests;

// Real clients, parsers and services; only the transport is replaced. Never load a user session,
// initialize an audio device, return a playable URL, or write the user's settings/database.
internal sealed class TerminalTestContext : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"kamusic-tests-{Guid.NewGuid():N}");

    public TerminalTestContext(Func<KgRequest, JsonElement>? respond = null, bool shuffle = false)
    {
        Directory.CreateDirectory(_directory);
        Transport = new RecordingTransport(respond ?? (_ => Json("{\"status\":0}")));
        var session = new KgSessionManager(new CookieContainer());
        Clients = new TerminalKugouClients
        {
            SessionManager = session,
            Login = new(new RawLoginApi(Transport, session, NullLogger<RawLoginApi>.Instance), session, NullLogger<LoginClient>.Instance),
            Device = new(new RawDeviceApi(Transport, session, NullLogger<RawDeviceApi>.Instance), session, NullLogger<RegisterClient>.Instance),
            Recommend = new(new RawDiscoveryApi(Transport), session),
            Search = new(new RawSearchApi(Transport), session),
            Song = new(new RawSongApi(Transport, session)),
            Playlist = new(new RawPlaylistApi(Transport, NullLogger<RawPlaylistApi>.Instance), session),
            User = new(new RawUserApi(Transport), session),
            Lyric = new(new RawLyricApi(Transport)),
            Rank = new(new RawRankApi(Transport)),
            Album = new(new RawAlbumApi(Transport)),
            Artist = new(new RawArtistApi(Transport, session), new RawSearchApi(Transport), session)
        };
        Settings = new TerminalAppSettings { Shuffle = shuffle };
        Data = new TerminalDataService(Clients, new TerminalLocalMusicService(Path.Combine(_directory, "music.db")));
        PersonalFm = new TerminalPersonalFmService(Clients, Data.IsLiked);
        Playback = new TerminalPlaybackService(Clients.Song, new TerminalLyricsService(Clients.Lyric),
            new TerminalSettingsStore(Path.Combine(_directory, "settings.json")), Settings, personalFm: PersonalFm);
    }

    public RecordingTransport Transport { get; }
    public TerminalKugouClients Clients { get; }
    public TerminalAppSettings Settings { get; }
    public TerminalPlaybackService Playback { get; }
    public TerminalPersonalFmService PersonalFm { get; }
    public TerminalDataService Data { get; }

    public static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public void Dispose()
    {
        Playback.Dispose();
        PersonalFm.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }
}

internal sealed class RecordingTransport(Func<KgRequest, JsonElement> respond) : IKgTransport
{
    public List<KgRequest> Requests { get; } = [];

    public Task<JsonElement> SendAsync(KgRequest request)
    {
        Requests.Add(request);
        return Task.FromResult(respond(request));
    }
}
