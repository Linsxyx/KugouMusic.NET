using System.Collections.Generic;
using System.Threading.Tasks;
using KuGou.Net.Abstractions.Models;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Services;

public enum LocalLyricMatchAction
{
    Temporary,
    Embed
}

public sealed record LocalLyricMatchResult(SongInfo Song, LocalLyricMatchAction Action);

public interface ILocalLyricMatchService
{
    Task<LocalLyricMatchResult?> MatchLyricAsync(SongItem localSong, List<SongInfo> candidates);
}
