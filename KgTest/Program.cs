using System.Text.Json;
using KuGou.Net.Adapters.Lyrics;
using KuGou.Net.Clients;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Protocol.Raw;
using KuGou.Net.Protocol.Session;
using SimpleAudio;

namespace KgTest;

// 1. 本地视图模型 (ViewModel) - 为了方便 UI 显示
// 我们把 SDK 返回的复杂数据转成这个简单类
public class SongViewModel
{
    public string Name { get; set; } = "";
    public string Singer { get; set; } = "";
    public string Hash { get; set; } = "";
    public string AlbumId { get; set; } = ""; // 用不上但保留字段
    public double DurationSeconds { get; set; }
}

public class UserPlaylistViewModel
{
    public string Name { get; set; } = "";
    public string Id { get; set; } = "";
    public int Count { get; set; }
}

internal class Program
{
    // --- 新 SDK 客户端 ---
    private static KgSessionManager? _sessionManager;
    private static AuthClient? _authClient;
    private static DeviceClient? _deviceClient;
    private static DiscoveryClient? _discoveryClient;
    private static MusicClient? _musicClient;
    private static PlaylistClient? _playlistClient;
    private static UserClient? _userClient;
    private static LyricClient? _lyricClient;

    // 播放器 (保持不变)
    private static SimpleAudioPlayer? _player;

    // 状态数据
    private static readonly List<SongViewModel> CurrentList = new();
    private static readonly List<UserPlaylistViewModel> UserPlaylists = new();

    private static int _currentIndex = -1;

    private static async Task Main()
    {
        // 1. 初始化 SDK (替代 DI 容器)
        var (transport, sessionMgr) = KgHttpClientFactory.CreateWithSession();
        _sessionManager = sessionMgr;

        // 组装各层 (Raw -> Client)
        var rawLogin = new RawLoginApi(transport, _sessionManager);
        var rawSearch = new RawSearchApi(transport);
        var rawDevice = new RawDeviceApi(transport, _sessionManager);
        var rawUser = new RawUserApi(transport);
        var rawPlaylist = new RawPlaylistApi(transport);
        var rawLyric = new RawLyricApi(transport);
        var rawDiscovery = new RawDiscoveryApi(transport);
        _authClient = new AuthClient(rawLogin, _sessionManager);
        _deviceClient = new DeviceClient(rawDevice, _sessionManager);
        _musicClient = new MusicClient(rawSearch, _sessionManager);
        _playlistClient = new PlaylistClient(rawPlaylist, _sessionManager);
        _userClient = new UserClient(rawUser, _sessionManager);
        _lyricClient = new LyricClient(rawLyric);
        _discoveryClient = new DiscoveryClient(rawDiscovery, _sessionManager);


        _player = new SimpleAudioPlayer();

        await LoadLocalSessionOrLogin();


        Console.WriteLine("=== KuGou Console Player (SDK v2) ===");

        while (true)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"当前列表歌曲数: {CurrentList.Count}");
            if (UserPlaylists.Count > 0) Console.WriteLine($"已加载个人歌单数: {UserPlaylists.Count}");
            Console.ResetColor();

            Console.WriteLine("\n[指令说明]");
            Console.WriteLine(" s <关键词>   : 搜索歌曲");
            Console.WriteLine(" m            : 获取我的个人歌单列表");
            Console.WriteLine(" sel <序号>   : 选择并加载'我的歌单'");
            Console.WriteLine(" p <ID>       : 加载公共歌单");
            Console.WriteLine(" play <序号>  : 播放当前列表中的歌曲");
            Console.WriteLine(" q            : 退出程序");
            Console.Write("\n请输入指令 > ");

            var input = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(input)) continue;

            try
            {
                if (input == "q") break;

                if (input.StartsWith("s "))
                {
                    await SearchSongs(input.Substring(2));
                }
                else if (input == "m")
                {
                    await ListUserPlaylists();
                }
                else if (input == "daily")
                {
                    await GetDailyRecommendations();
                }
                else if (input.StartsWith("sel "))
                {
                    if (int.TryParse(input.Substring(4), out var index))
                        await SelectUserPlaylist(index - 1);
                }
                else if (input.StartsWith("p "))
                {
                    await GetPlaylist(input.Substring(2));
                }
                else if (input.StartsWith("play "))
                {
                    if (int.TryParse(input.Substring(5), out var index))
                        await PlaySong(index - 1);
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[系统错误] {ex.Message}");
                Console.ResetColor();
            }
        }

        _player?.Dispose();
    }

    // ==========================================
    //              业务逻辑适配
    // ==========================================

    private static async Task LoadLocalSessionOrLogin()
    {
        var saved = KgSessionStore.Load();
        if (saved != null && !string.IsNullOrEmpty(saved.Token))
        {
            Console.WriteLine($"[系统] 已加载本地用户: {saved.UserId}");
            //_sessionManager?.UpdateAuth(saved.UserId, saved.Token, saved.VipType, saved.VipToken);
            // 恢复风控信息，避免重复注册
            if (!string.IsNullOrEmpty(saved.Dfid))
            {
                _sessionManager!.Session.Dfid = saved.Dfid;
                _sessionManager.Session.Mid = saved.Mid;
                _sessionManager.Session.Uuid = saved.Uuid;
            }

            // 尝试刷新一下 Token 保活
            // await _authClient!.RefreshSessionAsync();
        }
        else
        {
            Console.WriteLine("[系统] 未登录，以游客身份运行。");
        }
    }

    private static async Task GetDailyRecommendations()
    {
        Console.WriteLine("正在获取每日推荐...");

        // 使用 DiscoveryClient 获取强类型数据
        var response = await _discoveryClient!.GetRecommendedSongsAsync();

        if (response == null || response.Songs.Count == 0)
        {
            Console.WriteLine("未获取到推荐歌曲 (可能未登录或 Token 失效)。");
            return;
        }

        CurrentList.Clear();
        Console.WriteLine($"\n--- {response.Date} 每日推荐 ---");

        foreach (var item in response.Songs)
            CurrentList.Add(new SongViewModel
            {
                Name = item.Name,
                Singer = item.SingerName, // 每日推荐接口里的 author_name 已经是拼接好的
                Hash = item.Hash, // 这里也可以优先取 item.HashFlac 或 item.Hash320
                AlbumId = item.AlbumId,
                DurationSeconds = item.Duration
            });

        PrintList();
    }

    // --- 获取个人歌单 (适配 UserClient) ---
    private static async Task ListUserPlaylists()
    {
        Console.WriteLine("正在从服务器获取个人歌单...");

        // 1. 获取强类型列表
        var playlists = await _userClient.GetPlaylistsAsync();

        if (playlists == null || playlists.Count == 0)
        {
            Console.WriteLine("未找到个人歌单 (或未登录)。");
            return;
        }

        UserPlaylists.Clear();
        Console.WriteLine("\n--- 我的歌单列表 ---");

        var index = 1;
        foreach (var item in playlists)
            // 2. 映射到 ViewModel
            // 注意：global_collection_id 是最关键的，用于后续 GetPlaylist
            if (!string.IsNullOrEmpty(item.GlobalId))
            {
                UserPlaylists.Add(new UserPlaylistViewModel
                {
                    Name = item.Name,
                    Id = item.GlobalId,
                    Count = item.Count
                });

                // 稍微美化一下输出
                var typeMark = item.IsDefault switch
                {
                    1 => "[默认]",
                    2 => "[红心]",
                    _ => "      "
                };

                Console.WriteLine($"[{index}] {typeMark} {item.Name.PadRight(20)} (歌曲数: {item.Count})");
                index++;
            }
    }

    private static async Task SelectUserPlaylist(int index)
    {
        if (UserPlaylists.Count == 0) return;
        if (index < 0 || index >= UserPlaylists.Count) return;

        var playlist = UserPlaylists[index];
        Console.WriteLine($"正在加载歌单: [{playlist.Name}] ...");
        await GetPlaylist(playlist.Id);
    }

    // --- 搜索歌曲 (适配 MusicClient 强类型) ---
    private static async Task SearchSongs(string keyword)
    {
        Console.WriteLine($"正在搜索: {keyword} ...");

        var response = await _musicClient!.SearchAsync(keyword);


        CurrentList.Clear();
        foreach (var item in response)

            CurrentList.Add(new SongViewModel
            {
                Name = item.Name,
                Singer = item.Singer,
                Hash = item.Hash,
                AlbumId = "",
                DurationSeconds = item.Duration
            });
        PrintList();
    }

    // --- 加载歌单 (适配 PlaylistClient) ---
    private static async Task GetPlaylist(string pid)
    {
        var songs = await _playlistClient.GetSongsAsync(pid, pageSize: 60);

        if (songs == null || songs.Count == 0)
        {
            Console.WriteLine("歌单为空或加载失败。");
            return;
        }

        CurrentList.Clear();
        foreach (var item in songs)
        {
            // 2. 处理歌手名
            // 优先把 singerinfo 里的名字用 "、" 拼接
            var singerName = "未知";
            if (item.Singers != null && item.Singers.Count > 0)
                singerName = string.Join("、", item.Singers.Select(s => s.Name));
            else if (item.Name.Contains("-"))
                // 兜底：切割 "歌手 - 歌名"
                singerName = item.Name.Split('-')[0].Trim();

            // 3. 转换 ViewModel
            CurrentList.Add(new SongViewModel
            {
                Name = item.Name, // JSON里的 name 已经是完整歌名
                Singer = singerName,
                Hash = item.Hash,
                AlbumId = item.AlbumId,
                DurationSeconds = item.DurationMs / 1000.0 // 毫秒转秒
            });
        }

        PrintList();
    }

    private static async Task PlaySong(int index)
    {
        if (index < 0 || index >= CurrentList.Count) return;
        _currentIndex = index;

        // 进入播放循环
        while (_currentIndex >= 0 && _currentIndex < CurrentList.Count)
        {
            var song = CurrentList[_currentIndex];
            Console.WriteLine($"\n[队列] 正在准备第 {_currentIndex + 1} 首: {song.Name}");

            // 1. 获取播放地址
            var playData = await _musicClient!.GetPlayInfoAsync(song.Hash, "high");
            if (playData?.Status != 1 || playData.Urls.Count == 0)
            {
                Console.WriteLine($"❌ 无法获取播放链接: {song.Name}，3秒后尝试下一首...");
                await Task.Delay(3000);
                _currentIndex++;
                continue;
            }

            var url = playData.Urls.FirstOrDefault(x => !string.IsNullOrEmpty(x));

            // 2. 获取歌词
            var lyrics = await LoadLyrics(song.Hash, song.Name);

            // 3. 载入并进入 UI
            if (url != null && _player!.Load(url))
            {
                var result = EnterPlaybackMode(song, lyrics);

                if (result == PlaybackResult.Exit) break; // 彻底退出循环，回到主菜单

                if (result == PlaybackResult.Previous)
                    _currentIndex--; // 索引减1，循环会继续执行
                else // PlaybackResult.Next
                    _currentIndex++; // 索引加1
            }
            else
            {
                Console.WriteLine("❌ 播放失败，尝试下一首...");
                _currentIndex++;
                await Task.Delay(2000);
            }
        }

        Console.Clear();
        Console.WriteLine("已停止播放，返回主菜单。");
    }

    private static async Task<KrcLyric?> LoadLyrics(string hash, string name)
    {
        try
        {
            // 搜索歌词
            var searchJson = await _lyricClient!.SearchLyricAsync(hash, null, name, "no");

            if (!searchJson.TryGetProperty("candidates", out var candidatesElem) ||
                candidatesElem.ValueKind != JsonValueKind.Array)
                return null;

            var candidates = candidatesElem.EnumerateArray().ToList();
            if (candidates.Count == 0) return null;

            var bestMatch = candidates.First();
            var id = bestMatch.TryGetProperty("id", out var idElem) ? idElem.GetString() : null;
            var key = bestMatch.TryGetProperty("accesskey", out var keyElem) ? keyElem.GetString() : null;
            var fmt = bestMatch.TryGetProperty("fmt", out var fmtElem) ? fmtElem.GetString() ?? "krc" : "krc";

            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(key)) return null;

            // 下载并解密
            var lyricResult = await _lyricClient.GetLyricAsync(id, key, fmt);

            var decodeContent = lyricResult.DecodedContent;
            if (string.IsNullOrEmpty(decodeContent)) return null;

            return KrcParser.Parse(decodeContent);
        }
        catch
        {
            return null;
        }
    }

    private static void PrintList()
    {
        Console.WriteLine($"\n--- 歌曲列表 ({CurrentList.Count} 首) ---");
        for (var i = 0; i < CurrentList.Count; i++)
        {
            var s = CurrentList[i];
            var timeStr = TimeSpan.FromSeconds(s.DurationSeconds).ToString(@"mm\:ss");
            Console.WriteLine($"[{i + 1}] {s.Name} - {s.Singer} ({timeStr})");
        }
    }

    // --- UI 逻辑 (保持不变) ---
    // --- UI 逻辑 (修复歌词显示问题) ---
    private static PlaybackResult EnterPlaybackMode(SongViewModel song, KrcLyric? lyrics)
    {
        _player!.SetVolume(1.0f);
        _player.Play();

        Console.Clear();
        Console.WriteLine("==========================================");
        Console.WriteLine($"正在播放: {song.Name}");
        Console.WriteLine($"歌手:     {song.Singer}");
        Console.WriteLine("==========================================");

        // 定义歌词区域常量
        const int LYRICS_AREA_HEIGHT = 5;
        var lyricStartRow = Console.CursorTop + 1;

        // 预分配歌词行空间
        for (var i = 0; i < LYRICS_AREA_HEIGHT; i++) Console.WriteLine(new string(' ', Console.WindowWidth - 1));

        Console.WriteLine("------------------------------------------");
        var statusRow = Console.CursorTop;

        var duration = _player.GetDuration();
        if (duration == TimeSpan.Zero && song.DurationSeconds > 0)
            duration = TimeSpan.FromSeconds(song.DurationSeconds);

        Console.CursorVisible = false;
        var currentVol = 1.0f;
        var isPlaying = true;

        // 状态变量
        var lastLineIndex = -999; // 强制初始刷新
        var lyricMode = LyricMode.Translation;
        var lyricLines = lyrics?.Lines ?? new List<KrcLine>();

        Console.SetCursorPosition(0, statusRow + 2);
        Console.WriteLine("[操作] Q: 停止 | ↑/↓: 音量 | T: 切换翻译模式");

        while (isPlaying)
        {
            if (_player.IsStopped) return PlaybackResult.Next;

            var currentPos = _player.GetPosition();
            var currentMs = currentPos.TotalMilliseconds;

            // 1. 歌词渲染逻辑
            if (lyricLines.Count > 0)
            {
                // A. 查找当前行
                var currentLineIndex = -1;

                // 优先找时间区间内的
                for (var i = 0; i < lyricLines.Count; i++)
                    if (currentMs >= lyricLines[i].StartTime &&
                        currentMs < lyricLines[i].StartTime + lyricLines[i].Duration)
                    {
                        currentLineIndex = i;
                        break;
                    }

                // 兜底：找最近的过去一行
                if (currentLineIndex == -1)
                    for (var i = lyricLines.Count - 1; i >= 0; i--)
                        if (lyricLines[i].StartTime <= currentMs)
                        {
                            currentLineIndex = i;
                            break;
                        }

                // B. 只有当行发生变化（或模式切换强制刷新）时才重绘
                if (currentLineIndex != lastLineIndex)
                {
                    lastLineIndex = currentLineIndex;

                    // 清空歌词区域
                    for (var i = 0; i < LYRICS_AREA_HEIGHT; i++)
                    {
                        Console.SetCursorPosition(0, lyricStartRow + i);
                        Console.Write(new string(' ', Console.WindowWidth - 1));
                    }

                    // C. 核心渲染逻辑：根据模式填充5行槽位
                    var isMultiLine = lyricMode != LyricMode.Original;

                    if (!isMultiLine)
                    {
                        // === 单行模式 (显示5句) ===
                        // Row 0: current-2
                        // Row 1: current-1
                        // Row 2: current (高亮)
                        // Row 3: current+1
                        // Row 4: current+2
                        for (var offset = -2; offset <= 2; offset++)
                        {
                            var targetIndex = currentLineIndex + offset;
                            var screenRow = offset + 2; // 映射到 0~4

                            DrawSingleLine(lyricLines, targetIndex, lyricStartRow + screenRow, offset == 0);
                        }
                    }
                    else
                    {
                        // === 双行模式 (显示3句信息，中间句带翻译) ===
                        // 屏幕高度有限，策略是：
                        // Row 0: current-1 (原文)
                        // Row 1: current-1 (翻译)
                        // Row 2: current   (原文) [高亮]
                        // Row 3: current   (翻译) [高亮]
                        // Row 4: current+1 (原文)

                        // 1. 绘制上一句 (Row 0, 1)
                        if (currentLineIndex > 0)
                        {
                            DrawLineContent(lyricLines[currentLineIndex - 1], lyricStartRow, false, false); // 原文
                            DrawLineExtra(lyricLines[currentLineIndex - 1], lyricStartRow + 1, lyricMode); // 翻译
                        }

                        // 2. 绘制当前句 (Row 2, 3)
                        if (currentLineIndex >= 0 && currentLineIndex < lyricLines.Count)
                        {
                            DrawLineContent(lyricLines[currentLineIndex], lyricStartRow + 2, true, true); // 原文
                            DrawLineExtra(lyricLines[currentLineIndex], lyricStartRow + 3, lyricMode); // 翻译
                        }

                        // 3. 绘制下一句 (Row 4) - 空间不够显示下一句的翻译了，只显示原文
                        if (currentLineIndex + 1 < lyricLines.Count)
                            DrawLineContent(lyricLines[currentLineIndex + 1], lyricStartRow + 4, false, false);
                    }
                }
            }
            else
            {
                if (lastLineIndex == -999) // 只画一次
                {
                    Console.SetCursorPosition(0, lyricStartRow + 2);
                    Console.WriteLine("无歌词数据");
                    lastLineIndex = 0;
                }
            }

            // 2. 状态栏渲染
            Console.SetCursorPosition(0, statusRow);
            Console.Write($"进度: {currentPos:mm\\:ss} / {duration:mm\\:ss}  | 音量: {(int)(currentVol * 100)}%   ");

            // 3. 键盘事件
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(true).Key;
                switch (key)
                {
                    case ConsoleKey.Q:
                        _player.Stop();
                        return PlaybackResult.Exit;
                    case ConsoleKey.N:
                        _player.Stop();
                        return PlaybackResult.Next;
                    case ConsoleKey.B:
                        _player.Stop();
                        return PlaybackResult.Previous;
                    case ConsoleKey.UpArrow:
                        currentVol = Math.Clamp(currentVol + 0.05f, 0f, 1f);
                        _player.SetVolume(currentVol);
                        break;
                    case ConsoleKey.DownArrow:
                        currentVol = Math.Clamp(currentVol - 0.05f, 0f, 1f);
                        _player.SetVolume(currentVol);
                        break;
                    case ConsoleKey.T:
                        lyricMode = lyricMode switch
                        {
                            LyricMode.Original => LyricMode.Translation,
                            LyricMode.Translation => LyricMode.Romanization,
                            LyricMode.Romanization => LyricMode.Combined,
                            _ => LyricMode.Original
                        };
                        lastLineIndex = -999; // 触发强制重绘
                        Console.SetCursorPosition(0, statusRow + 3);
                        Console.Write($"当前模式: {lyricMode}".PadRight(20));
                        break;
                }
            }

            Thread.Sleep(50);
        }

        /*Console.CursorVisible = true;
        Console.Clear();
        Console.WriteLine("播放结束。");*/
        return PlaybackResult.Next;
    }

// --- 辅助绘制方法 ---

    private static void DrawSingleLine(List<KrcLine> lines, int index, int row, bool isHighlight)
    {
        if (index >= 0 && index < lines.Count)
        {
            Console.SetCursorPosition(0, row);
            if (isHighlight)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("▶ " + lines[index].Content);
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write("  " + lines[index].Content);
            }

            Console.ResetColor();
        }
    }

    private static void DrawLineContent(KrcLine line, int row, bool isHighlight, bool showCursor)
    {
        Console.SetCursorPosition(0, row);
        if (isHighlight)
            Console.ForegroundColor = ConsoleColor.Cyan;
        else
            Console.ForegroundColor = ConsoleColor.Gray;

        Console.Write((showCursor ? "▶ " : "  ") + line.Content);
        Console.ResetColor();
    }

    private static void DrawLineExtra(KrcLine line, int row, LyricMode mode)
    {
        Console.SetCursorPosition(0, row);
        Console.ForegroundColor = ConsoleColor.DarkGray;

        var extra = mode switch
        {
            LyricMode.Translation => line.Translation,
            LyricMode.Romanization => line.Romanization,
            LyricMode.Combined => $"{line.Translation} | {line.Romanization}",
            _ => ""
        };

        if (!string.IsNullOrEmpty(extra)) Console.Write("  " + extra);
        Console.ResetColor();
    }

    // 辅助方法：获取 JSON 属性
    private static bool TryGetJsonProperty(JsonElement element, string propertyName, out JsonElement? result)
    {
        result = null;
        if (element.TryGetProperty(propertyName, out var elem))
        {
            result = elem;
            return true;
        }

        return false;
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var result) ? result.GetString() : null;
    }

    private static int GetIntProperty(JsonElement element, string propertyName, int @default = 0)
    {
        return element.TryGetProperty(propertyName, out var result) ? result.GetInt32() : @default;
    }

    private enum PlaybackResult
    {
        Next,
        Previous,
        Exit
    }

    private enum LyricMode
    {
        Original, // 原文
        Translation, // 翻译
        Romanization, // 罗马音
        Combined // 混合
    }
}