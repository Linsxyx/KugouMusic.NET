using KuGou.Net.Clients;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Protocol.Raw;

var (transport, session) = KgHttpClientFactory.CreateWithSession();
var musicClient = new MusicClient(new RawSearchApi(transport), session);
var _playlistClient = new PlaylistClient(new RawPlaylistApi(transport), session);
Console.WriteLine("=== 歌单详情测试 ===");

var pid = "collection_3_2413242059_4_0";
var page = await _playlistClient.GetInfoAsync(pid);
var songs = await _playlistClient.GetSongsAsync(pid, pageSize: page.SongCount);

foreach (var song in songs)
{
    Console.WriteLine($"Found: {song.Name}");

    var playInfo = await musicClient.GetPlayInfoAsync(song.Hash);
    Console.WriteLine($"   -> URL: {playInfo?.Urls?.FirstOrDefault()}");
}