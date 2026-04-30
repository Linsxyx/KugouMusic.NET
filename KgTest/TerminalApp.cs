using KuGou.Net.Abstractions.Models;
using KgTest.Models;
using KgTest.Services;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.Text;

namespace KgTest;

internal sealed class TerminalApp : IAsyncDisposable
{
    private const int SettingsHeaderItemCount = 5;
    private const int SeekStepSeconds = 10;
    private static int SettingsItemCount => SettingsHeaderItemCount + 10;

    private static readonly TerminalPage[] Pages =
    [
        TerminalPage.Daily,
        TerminalPage.PersonalFm,
        TerminalPage.Discover,
        TerminalPage.Ranks,
        TerminalPage.Search,
        TerminalPage.MyPlaylists,
        TerminalPage.Queue,
        TerminalPage.Settings,
        TerminalPage.Account
    ];

    private readonly TerminalSettingsStore _settingsStore = new();
    private readonly TerminalAppSettings _settings;
    private readonly TerminalKugouClients _clients;
    private readonly TerminalDataService _data;
    private readonly TerminalLyricsService _lyrics;
    private readonly TerminalPlaybackService _playback;

    private TerminalPage _page = TerminalPage.Daily;
    private readonly List<TerminalSongItem> _songs = [];
    private readonly List<TerminalPlaylistItem> _playlists = [];
    private TerminalPlaylistItem? _openedPlaylist;
    private string _title = "每日推荐";
    private string _status = "启动中";
    private string _userName = "未登录";
    private int _selectedIndex;
    private int _settingsIndex;
    private bool _showingSongs;
    private bool _searchShowsPlaylists;
    private bool _exitRequested;
    private bool _screenInitialized;
    private bool _isImmersive;
    private float[] _visualizerBars = [];
    private float[] _visualizerPeaks = [];

    public TerminalApp()
    {
        _settings = _settingsStore.Load();
        _clients = TerminalKugouClientFactory.Create();
        _data = new TerminalDataService(_clients);
        _lyrics = new TerminalLyricsService(_clients.Lyric);
        _playback = new TerminalPlaybackService(_clients.Song, _lyrics, _settingsStore, _settings);
    }

    public async Task RunAsync()
    {
        Console.Write("\u001b[?1049h\u001b[?25l");
        Console.CursorVisible = false;
        try
        {
            _userName = await SafeLoadAsync(_data.GetUserDisplayNameAsync(), "未登录");
            await LoadCurrentPageAsync();
            Render();
            var nextPassiveRender = DateTimeOffset.UtcNow.AddMilliseconds(500);

            while (!_exitRequested)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);
                    await HandleKeyAsync(key);
                    Render();
                    nextPassiveRender = DateTimeOffset.UtcNow.AddMilliseconds(GetRenderDelayMs(_playback.GetSnapshot()));
                    continue;
                }

                var now = DateTimeOffset.UtcNow;
                if (now >= nextPassiveRender)
                {
                    Render();
                    var snapshot = _playback.GetSnapshot();
                    nextPassiveRender = now.AddMilliseconds(GetRenderDelayMs(snapshot));
                }

                await Task.Delay(50);
            }
        }
        finally
        {
            Console.CursorVisible = true;
            Console.Write("\u001b[?25h\u001b[?1049l");
        }
    }

    private async Task HandleKeyAsync(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
                if (_isImmersive)
                {
                    _playback.SeekRelative(TimeSpan.FromSeconds(-SeekStepSeconds));
                }
                else
                {
                    await SwitchPageAsync(-1);
                }
                break;
            case ConsoleKey.RightArrow:
                if (_isImmersive)
                {
                    _playback.SeekRelative(TimeSpan.FromSeconds(SeekStepSeconds));
                }
                else
                {
                    await SwitchPageAsync(1);
                }
                break;
            case ConsoleKey.Tab:
                if (!_isImmersive)
                {
                    await SwitchPageAsync(1);
                }
                break;
            case ConsoleKey.UpArrow:
                MoveSelection(-1);
                break;
            case ConsoleKey.DownArrow:
                MoveSelection(1);
                break;
            case ConsoleKey.Enter:
                await ActivateSelectionAsync();
                break;
            case ConsoleKey.Spacebar:
                _playback.TogglePlayPause();
                break;
            case ConsoleKey.N:
                await _playback.PlayNextAsync();
                break;
            case ConsoleKey.B:
                await _playback.PlayPreviousAsync();
                break;
            case ConsoleKey.Add:
            case ConsoleKey.OemPlus:
                HandlePlus();
                break;
            case ConsoleKey.Subtract:
            case ConsoleKey.OemMinus:
                HandleMinus();
                break;
            case ConsoleKey.S:
                _playback.ToggleShuffle();
                break;
            case ConsoleKey.L:
                _playback.CycleLyricMode();
                break;
            case ConsoleKey.T:
                _playback.ToggleSeamlessTransition();
                break;
            case ConsoleKey.E:
                if (_page == TerminalPage.Settings)
                {
                    _playback.ToggleSurround();
                }
                else
                {
                    await SetPageAsync(TerminalPage.Settings);
                }
                break;
            case ConsoleKey.I:
                _isImmersive = !_isImmersive;
                _status = _isImmersive ? "进入沉浸播放页" : "返回列表";
                break;
            case ConsoleKey.Oem4:
                _playback.SeekRelative(TimeSpan.FromSeconds(-SeekStepSeconds));
                break;
            case ConsoleKey.Oem6:
                _playback.SeekRelative(TimeSpan.FromSeconds(SeekStepSeconds));
                break;
            case ConsoleKey.R:
                await LoadCurrentPageAsync(true);
                break;
            case ConsoleKey.D:
                if (_page == TerminalPage.PersonalFm)
                {
                    await _data.ReportPersonalFmAsync(_playback.CurrentSong, (int)_playback.GetSnapshot().Position.TotalSeconds, false, "garbage");
                    await _playback.PlayNextAsync();
                }
                break;
            case ConsoleKey.Q:
            case ConsoleKey.Escape:
                if (_isImmersive)
                {
                    _isImmersive = false;
                }
                else if (_showingSongs && _openedPlaylist != null)
                {
                    _showingSongs = false;
                    _openedPlaylist = null;
                    _songs.Clear();
                    _selectedIndex = 0;
                    _title = PageTitle(_page);
                }
                else
                {
                    _exitRequested = true;
                }
                break;
            case ConsoleKey.Oem2:
                await PromptSearchAsync();
                break;
            case ConsoleKey.F5:
                await LoginWithQrAsync();
                break;
            case ConsoleKey.F6:
                await LoginWithSmsAsync();
                break;
            case ConsoleKey.F9:
                Logout();
                break;
        }
    }

    private async Task SwitchPageAsync(int delta)
    {
        var index = Array.IndexOf(Pages, _page);
        index = (index + delta + Pages.Length) % Pages.Length;
        await SetPageAsync(Pages[index]);
    }

    private async Task SetPageAsync(TerminalPage page)
    {
        _page = page;
        _selectedIndex = 0;
        _showingSongs = false;
        _openedPlaylist = null;
        _searchShowsPlaylists = false;
        await LoadCurrentPageAsync();
    }

    private async Task LoadCurrentPageAsync(bool force = false)
    {
        _ = force;
        _status = "加载中...";
        _songs.Clear();
        _playlists.Clear();
        _title = PageTitle(_page);
        _selectedIndex = 0;
        _showingSongs = _page is TerminalPage.Daily or TerminalPage.PersonalFm;

        try
        {
            switch (_page)
            {
                case TerminalPage.Daily:
                    _songs.AddRange(await _data.GetDailySongsAsync());
                    break;
                case TerminalPage.PersonalFm:
                    _songs.AddRange(await _data.GetPersonalFmSongsAsync());
                    break;
                case TerminalPage.Discover:
                    _playlists.AddRange(await _data.GetDiscoverPlaylistsAsync());
                    break;
                case TerminalPage.Ranks:
                    _playlists.AddRange(await _data.GetRanksAsync());
                    break;
                case TerminalPage.MyPlaylists:
                    _playlists.AddRange(await _data.GetMyPlaylistsAsync());
                    break;
                case TerminalPage.Queue:
                    _songs.AddRange(_playback.Queue);
                    _showingSongs = true;
                    break;
                case TerminalPage.Account:
                    _userName = await _data.GetUserDisplayNameAsync();
                    break;
            }

            _status = _songs.Count > 0 || _playlists.Count > 0 || _page is TerminalPage.Settings or TerminalPage.Account
                ? "就绪"
                : "没有可显示的内容";
        }
        catch (Exception ex)
        {
            _status = ex.Message;
        }
    }

    private async Task PromptSearchAsync()
    {
        Console.CursorVisible = true;
        AnsiConsole.Clear();
        var keyword = AnsiConsole.Ask<string>("[cyan]搜索关键词[/]");
        Console.CursorVisible = false;
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return;
        }

        _page = TerminalPage.Search;
        _title = $"搜索：{keyword}";
        _status = "搜索中...";
        _selectedIndex = 0;
        _songs.Clear();
        _playlists.Clear();
        try
        {
            _songs.AddRange(await _data.SearchSongsAsync(keyword));
            _playlists.AddRange(await _data.SearchPlaylistsAsync(keyword));
            _showingSongs = true;
            _searchShowsPlaylists = _songs.Count == 0 && _playlists.Count > 0;
            _status = $"歌曲 {_songs.Count} 首，歌单 {_playlists.Count} 个";
        }
        catch (Exception ex)
        {
            _status = ex.Message;
        }
    }

    private async Task ActivateSelectionAsync()
    {
        if (_page == TerminalPage.Settings)
        {
            ActivateSetting();
            return;
        }

        var song = SelectedSong();
        if (song != null)
        {
            await _playback.PlayAsync(song, CurrentSongContext());
            return;
        }

        var playlist = SelectedPlaylist();
        if (playlist == null)
        {
            return;
        }

        _openedPlaylist = playlist;
        _showingSongs = true;
        _title = playlist.Name;
        _songs.Clear();
        _selectedIndex = 0;
        _status = $"加载 {playlist.Name}...";

        try
        {
            _songs.AddRange(await _data.GetPlaylistSongsAsync(playlist));
            _status = _songs.Count > 0 ? "就绪" : "歌单为空";
        }
        catch (Exception ex)
        {
            _status = ex.Message;
        }
    }

    private IReadOnlyList<TerminalSongItem> CurrentSongContext()
    {
        return _page == TerminalPage.Queue ? _playback.Queue : _songs;
    }

    private TerminalSongItem? SelectedSong()
    {
        if (_showingSongs && _selectedIndex >= 0 && _selectedIndex < _songs.Count)
        {
            return _songs[_selectedIndex];
        }

        return null;
    }

    private TerminalPlaylistItem? SelectedPlaylist()
    {
        if (_searchShowsPlaylists)
        {
            return _selectedIndex >= 0 && _selectedIndex < _playlists.Count ? _playlists[_selectedIndex] : null;
        }

        if (!_showingSongs && _selectedIndex >= 0 && _selectedIndex < _playlists.Count)
        {
            return _playlists[_selectedIndex];
        }

        return null;
    }

    private void MoveSelection(int delta)
    {
        if (_page == TerminalPage.Settings)
        {
            _settingsIndex = Math.Clamp(_settingsIndex + delta, 0, SettingsItemCount - 1);
            return;
        }

        var count = _searchShowsPlaylists ? _playlists.Count : _showingSongs ? _songs.Count : _playlists.Count;
        if (count == 0)
        {
            _selectedIndex = 0;
            return;
        }

        _selectedIndex = (_selectedIndex + delta + count) % count;
    }

    private void HandlePlus()
    {
        if (_page == TerminalPage.Settings)
        {
            AdjustSetting(1);
        }
        else
        {
            _playback.ChangeVolume(0.05f);
        }
    }

    private void HandleMinus()
    {
        if (_page == TerminalPage.Settings)
        {
            AdjustSetting(-1);
        }
        else
        {
            _playback.ChangeVolume(-0.05f);
        }
    }

    private void ActivateSetting()
    {
        switch (_settingsIndex)
        {
            case 0:
                _playback.CycleQuality();
                break;
            case 1:
                _playback.ToggleSurround();
                break;
            case 2:
                _playback.ToggleSeamlessTransition();
                break;
            case 3:
                _playback.ToggleShuffle();
                break;
            case 4:
                _playback.CycleLyricMode();
                break;
        }
    }

    private void AdjustSetting(int direction)
    {
        if (_settingsIndex < SettingsHeaderItemCount)
        {
            ActivateSetting();
            return;
        }

        _playback.SetEqBand(_settingsIndex - SettingsHeaderItemCount, direction);
    }

    private async Task LoginWithQrAsync()
    {
        AnsiConsole.Clear();
        _status = "正在获取二维码...";
        try
        {
            var qr = await _clients.Login.GetQrCodeAsync();
            if (qr == null || string.IsNullOrWhiteSpace(qr.Qrcode))
            {
                _status = "二维码获取失败";
                return;
            }

            AnsiConsole.MarkupLine("[bold cyan]酷狗二维码登录[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]图片显示已关闭。请复制下面地址到可扫码环境：[/]");
            AnsiConsole.MarkupLine(Markup.Escape(qr.Qrcode));

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]请用酷狗客户端扫码确认，按 Q 取消。[/]");

            for (var i = 0; i < 90; i++)
            {
                if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Q)
                {
                    _status = "已取消登录";
                    return;
                }

                var status = await _clients.Login.CheckQrStatusAsync(qr.Qrcode);
                if (status?.QrStatus == QrLoginStatus.Success)
                {
                    _userName = status.Nickname ?? await _data.GetUserDisplayNameAsync();
                    _status = $"登录成功：{_userName}";
                    return;
                }

                if (status?.QrStatus == QrLoginStatus.Expired)
                {
                    _status = "二维码已过期";
                    return;
                }

                await Task.Delay(2000);
            }

            _status = "二维码登录超时";
        }
        catch (Exception ex)
        {
            _status = ex.Message;
        }
    }

    private async Task LoginWithSmsAsync()
    {
        Console.CursorVisible = true;
        AnsiConsole.Clear();
        try
        {
            var mobile = AnsiConsole.Ask<string>("[cyan]手机号[/]");
            var send = await _clients.Login.SendCodeAsync(mobile);
            AnsiConsole.MarkupLine(send?.Status == 1 ? "[green]验证码已发送[/]" : "[yellow]验证码发送请求已提交，请留意手机[/]");
            var code = AnsiConsole.Ask<string>("[cyan]验证码[/]");
            var login = await _clients.Login.LoginByMobileAsync(mobile, code);
            _status = login?.Status == 1
                ? "短信登录成功"
                : $"短信登录失败：{login?.GetExtraString("error_msg") ?? login?.ErrorCode.ToString() ?? "未知错误"}";
            _userName = await _data.GetUserDisplayNameAsync();
        }
        catch (Exception ex)
        {
            _status = ex.Message;
        }
        finally
        {
            Console.CursorVisible = false;
        }
    }

    private void Logout()
    {
        _clients.Login.LogOutAsync();
        _userName = "未登录";
        _status = "已退出登录";
    }

    private void Render()
    {
        if (!_screenInitialized)
        {
            AnsiConsole.Clear();
            _screenInitialized = true;
        }
        else
        {
            Console.SetCursorPosition(0, 0);
        }

        var snapshot = _playback.GetSnapshot();
        if (_isImmersive)
        {
            AnsiConsole.Write(BuildImmersive(snapshot));
            Console.Write("\u001b[J");
            return;
        }

        var root = new Layout("root")
            .SplitRows(
                new Layout("main").Ratio(1),
                new Layout("bar").Size(5));

        root["main"].SplitColumns(
            new Layout("nav").Size(22),
            new Layout("content").Ratio(2),
            new Layout("side").Ratio(1));

        root["nav"].Update(BuildNavigation());
        root["content"].Update(BuildContent());
        root["side"].Update(BuildSide(snapshot));
        root["bar"].Update(BuildPlaybackBar(snapshot));
        AnsiConsole.Write(root);
        Console.Write("\u001b[J");
    }

    private IRenderable BuildNavigation()
    {
        var lines = new List<string>
        {
            $"[bold green]KgTest[/]",
            $"[grey]{Markup.Escape(_userName)}[/]",
            ""
        };

        foreach (var page in Pages)
        {
            var active = page == _page;
            var text = $"{(active ? ">" : " ")} {PageTitle(page)}";
            lines.Add(active ? $"[black on green]{Markup.Escape(text)}[/]" : Markup.Escape(text));
        }

        lines.Add("");
        lines.Add("[grey]F5 二维码登录[/]");
        lines.Add("[grey]F6 短信登录[/]");
        lines.Add("[grey]F9 退出登录[/]");
        lines.Add("[grey]/ 搜索  q 退出[/]");

        return new Panel(new Markup(string.Join(Environment.NewLine, lines)))
            .Header(Header("导航"))
            .Border(BoxBorder.Rounded);
    }

    private IRenderable BuildContent()
    {
        if (_page == TerminalPage.Settings)
        {
            return BuildSettings();
        }

        if (_page == TerminalPage.Account)
        {
            return BuildAccount();
        }

        if (_searchShowsPlaylists)
        {
            return BuildPlaylistTable(_playlists, _title);
        }

        if (_showingSongs)
        {
            return BuildSongTable(_songs, _title);
        }

        return BuildPlaylistTable(_playlists, _title);
    }

    private IRenderable BuildSongTable(IReadOnlyList<TerminalSongItem> songs, string title)
    {
        var table = new Table().Expand().Border(TableBorder.Rounded);
        table.AddColumn(new TableColumn("#").Width(4));
        table.AddColumn("歌曲");
        table.AddColumn("歌手");
        table.AddColumn(new TableColumn("时长").Width(7));
        table.AddColumn(new TableColumn("来源").Width(10));

        if (songs.Count == 0)
        {
            table.AddRow("", "[grey]暂无歌曲[/]", "", "", "");
        }
        else
        {
            var start = Math.Max(0, Math.Min(_selectedIndex - 10, Math.Max(0, songs.Count - 22)));
            foreach (var (song, visibleIndex) in songs.Skip(start).Take(22).Select((song, i) => (song, start + i)))
            {
                var selected = visibleIndex == _selectedIndex;
                var marker = selected ? ">" : song.IsPlaying ? "*" : "";
                table.AddRow(
                    Paint($"{marker}{visibleIndex + 1}", selected),
                    Paint(song.Name, selected),
                    Paint(song.Singer, selected),
                    Paint(FormatTime(song.DurationSeconds), selected),
                    Paint(song.Source, selected));
            }
        }

        return new Panel(table)
            .Header(Header($"{title} · Enter 播放 · i 沉浸页 · 方括号键快退/快进"))
            .Border(BoxBorder.Rounded);
    }

    private IRenderable BuildPlaylistTable(IReadOnlyList<TerminalPlaylistItem> playlists, string title)
    {
        var table = new Table().Expand().Border(TableBorder.Rounded);
        table.AddColumn(new TableColumn("#").Width(4));
        table.AddColumn("名称");
        table.AddColumn("说明");
        table.AddColumn(new TableColumn("歌曲").Width(8));

        if (playlists.Count == 0)
        {
            table.AddRow("", "[grey]暂无歌单[/]", "", "");
        }
        else
        {
            var start = Math.Max(0, Math.Min(_selectedIndex - 10, Math.Max(0, playlists.Count - 22)));
            foreach (var (playlist, visibleIndex) in playlists.Skip(start).Take(22).Select((item, i) => (item, start + i)))
            {
                var selected = visibleIndex == _selectedIndex;
                table.AddRow(
                    Paint($">{visibleIndex + 1}".Replace(">", selected ? ">" : ""), selected),
                    Paint(playlist.Name, selected),
                    Paint(playlist.Subtitle, selected),
                    Paint(playlist.Count > 0 ? playlist.Count.ToString() : "-", selected));
            }
        }

        return new Panel(table)
            .Header(Header($"{title} · Enter 打开 · i 沉浸页"))
            .Border(BoxBorder.Rounded);
    }

    private IRenderable BuildSettings()
    {
        var table = new Table().Expand().Border(TableBorder.Rounded);
        table.AddColumn("设置");
        table.AddColumn("值");

        table.AddRow(PaintSetting("音质", 0), PaintSetting(_playback.MusicQuality, 0));
        table.AddRow(PaintSetting("环绕", 1), PaintSetting(_settings.EnableSurround ? "开启" : "关闭", 1));
        table.AddRow(PaintSetting("智能过渡", 2), PaintSetting(_settings.EnableSeamlessTransition ? "开启" : "关闭", 2));
        table.AddRow(PaintSetting("随机播放", 3), PaintSetting(_settings.Shuffle ? "开启" : "关闭", 3));
        table.AddRow(PaintSetting("歌词模式", 4), PaintSetting(_settings.LyricMode.ToString(), 4));
        table.AddEmptyRow();

        for (var i = 0; i < _playback.EqGains.Length; i++)
        {
            var rowIndex = SettingsHeaderItemCount + i;
            table.AddRow(PaintSetting($"EQ {i + 1}", rowIndex), PaintSetting($"{_playback.EqGains[i]:0.#} dB", rowIndex));
        }

        return new Panel(table)
            .Header(Header("设置 · ↑/↓ 选择 · Enter/+/- 调整"))
            .Border(BoxBorder.Rounded);
    }

    private IRenderable BuildAccount()
    {
        var loggedIn = !string.IsNullOrWhiteSpace(_clients.SessionManager.Session.Token) &&
                       _clients.SessionManager.Session.UserId != "0";
        var rows = new Rows(
            new Markup($"[bold]用户[/] {Markup.Escape(_userName)}"),
            new Markup($"[bold]状态[/] {(loggedIn ? "[green]已登录[/]" : "[yellow]未登录[/]")}"),
            new Markup($"[bold]UserId[/] {Markup.Escape(_clients.SessionManager.Session.UserId)}"),
            new Text(""),
            new Markup("[grey]F5 二维码登录，F6 短信验证码登录，F9 退出登录。[/]"),
            new Markup("[grey]KgTest 与 Avalonia 客户端共用 ApplicationData/kugou/session.json。[/]"));

        return new Panel(rows)
            .Header(Header("账号"))
            .Border(BoxBorder.Rounded);
    }

    private IRenderable BuildSide(PlaybackStateSnapshot snapshot)
    {
        var song = snapshot.CurrentSong;
        var lines = new List<IRenderable>
        {
            new Markup(song == null ? "[grey]还没有播放歌曲[/]" : $"[bold cyan]{Markup.Escape(song.Name)}[/]"),
            new Markup(song == null ? "" : Markup.Escape(song.Singer)),
            new Text("")
        };

        foreach (var lyric in snapshot.LyricWindow.Take(7))
        {
            var escaped = Markup.Escape(TrimToWidth(lyric, 34));
            lines.Add(new Markup(lyric.StartsWith("> ", StringComparison.Ordinal) ? $"[green]{escaped}[/]" : $"[grey]{escaped}[/]"));
        }

        lines.Add(new Text(""));
        lines.Add(new Markup($"[grey]音质[/] {_playback.MusicQuality}   [grey]音量[/] {(int)(snapshot.Volume * 100)}%"));
        lines.Add(new Markup($"[grey]随机[/] {(snapshot.IsShuffle ? "on" : "off")}   [grey]环绕[/] {(snapshot.EnableSurround ? "on" : "off")}"));
        lines.Add(new Markup($"[grey]智能过渡[/] {(snapshot.EnableSeamlessTransition ? "on" : "off")}   [grey]混音[/] {(snapshot.IsCrossfading ? "on" : "off")}"));
        lines.Add(new Markup($"[grey]队列[/] {snapshot.Queue.Count} 首"));
        lines.Add(new Text(""));
        lines.Add(new Markup("[grey]Space 暂停 · n/b 切歌[/]"));
        lines.Add(new Markup("[grey]+/- 音量 · r 刷新[/]"));

        return new Panel(new Rows(lines))
            .Header(Header("正在播放"))
            .Border(BoxBorder.Rounded);
    }

    private IRenderable BuildPlaybackBar(PlaybackStateSnapshot snapshot)
    {
        var percent = snapshot.Duration.TotalSeconds > 0
            ? Math.Clamp(snapshot.Position.TotalSeconds / snapshot.Duration.TotalSeconds, 0, 1)
            : 0;
        var barWidth = Math.Max(10, Console.WindowWidth - 42);
        var filled = (int)(barWidth * percent);
        var bar = new string('=', filled) + new string('-', barWidth - filled);
        var state = snapshot.IsPlaying ? "播放" : snapshot.IsPaused ? "暂停" : "停止";
        var text =
            $"[{state}] {FormatTime(snapshot.Position.TotalSeconds)} / {FormatTime(snapshot.Duration.TotalSeconds)}  {bar}  {_playback.StatusMessage} · {_status}";

        return new Panel(new Markup(Markup.Escape(TrimToWidth(text, Math.Max(40, Console.WindowWidth - 8)))))
            .Border(BoxBorder.Rounded);
    }

    private IRenderable BuildImmersive(PlaybackStateSnapshot snapshot)
    {
        var song = snapshot.CurrentSong;
        var title = song == null ? "未播放" : $"{song.Name} - {song.Singer}";
        var root = new Layout("immersive")
            .SplitRows(
                new Layout("header").Size(5),
                new Layout("visualizer").Ratio(1),
                new Layout("lyrics").Size(10),
                new Layout("bar").Size(5));

        root["header"].Update(new Panel(new Rows(
                new Markup($"[bold cyan]{Markup.Escape(TrimToWidth(title, Math.Max(20, Console.WindowWidth - 10)))}[/]"),
                new Markup($"[grey]{Markup.Escape($"i 返回 · Space 暂停 · ←/→ 或 方括号键快退/快进 {SeekStepSeconds}s · n/b 切歌")}[/]")))
            .Border(BoxBorder.Rounded));

        root["visualizer"].Update(new Panel(new Markup(BuildVisualizer(snapshot)))
            .Header(Header("频谱"))
            .Border(BoxBorder.Rounded));

        var lyricRows = snapshot.LyricWindow
            .Take(9)
            .Select(line =>
            {
                var escaped = Markup.Escape(TrimToWidth(line, Math.Max(20, Console.WindowWidth - 10)));
                return line.StartsWith("> ", StringComparison.Ordinal)
                    ? new Markup($"[bold green]{escaped}[/]")
                    : new Markup($"[grey]{escaped}[/]");
            })
            .Cast<IRenderable>()
            .ToList();
        if (lyricRows.Count == 0)
        {
            lyricRows.Add(new Markup("[grey]暂无歌词[/]"));
        }

        root["lyrics"].Update(new Panel(new Rows(lyricRows))
            .Header(Header("歌词"))
            .Border(BoxBorder.Rounded));
        root["bar"].Update(BuildPlaybackBar(snapshot));
        return root;
    }

    private string BuildVisualizer(PlaybackStateSnapshot snapshot)
    {
        if (snapshot.SpectrumBands.Count == 0)
        {
            return CenterVisualizerText("等待音频数据...");
        }

        var width = Math.Clamp(Console.WindowWidth - 8, 32, 118);
        var height = Math.Clamp(Console.WindowHeight - 24, 10, 28);
        var targetBars = Math.Clamp(width, 24, 112);
        var rawBands = ResampleBands(snapshot.SpectrumBands, targetBars);
        EnsureVisualizerState(targetBars);

        for (var i = 0; i < targetBars; i++)
        {
            var shaped = (float)Math.Pow(Math.Clamp(rawBands[i], 0f, 1f), 0.62);
            var body = Math.Clamp(shaped * (0.82f + (float)Math.Clamp(snapshot.Rms * 5.5, 0, 0.55)), 0f, 1f);
            var attack = body > _visualizerBars[i] ? 0.62f : 0.24f;
            _visualizerBars[i] = Lerp(_visualizerBars[i], body, attack);
            _visualizerPeaks[i] = Math.Max(_visualizerBars[i], _visualizerPeaks[i] - 0.018f);
        }

        var output = new StringBuilder();
        output.Append("[grey]");
        output.Append(Markup.Escape(BuildVuLine(snapshot.Rms, targetBars)));
        output.AppendLine("[/]");

        for (var row = height; row >= 1; row--)
        {
            var threshold = row / (float)height;
            var line = new StringBuilder(targetBars);
            for (var i = 0; i < targetBars; i++)
            {
                if (_visualizerBars[i] >= threshold)
                {
                    line.Append('█');
                }
                else if (Math.Abs(_visualizerPeaks[i] - threshold) <= 0.5f / height)
                {
                    line.Append('▀');
                }
                else if (row == 1 && i % 4 == 0)
                {
                    line.Append('▁');
                }
                else
                {
                    line.Append(' ');
                }
            }

            output.Append(VisualizerGradientTag(row, height));
            output.Append(Markup.Escape(line.ToString()));
            output.AppendLine("[/]");
        }

        output.Append("[grey]");
        output.Append(Markup.Escape(BuildFrequencyRail(targetBars)));
        output.Append("[/]");
        return output.ToString();
    }

    private void EnsureVisualizerState(int count)
    {
        if (_visualizerBars.Length == count && _visualizerPeaks.Length == count)
        {
            return;
        }

        _visualizerBars = new float[count];
        _visualizerPeaks = new float[count];
    }

    private static float[] ResampleBands(IReadOnlyList<float> source, int count)
    {
        var result = new float[count];
        if (source.Count == 0)
        {
            return result;
        }

        if (source.Count == 1)
        {
            Array.Fill(result, source[0]);
            return result;
        }

        for (var i = 0; i < count; i++)
        {
            var position = i * (source.Count - 1) / (double)Math.Max(1, count - 1);
            var left = (int)Math.Floor(position);
            var right = Math.Min(source.Count - 1, left + 1);
            var t = (float)(position - left);
            result[i] = Lerp(source[left], source[right], t);
        }

        return result;
    }

    private static string BuildVuLine(double rms, int width)
    {
        var label = " VU ";
        var safeWidth = Math.Max(12, width - label.Length - 1);
        var filled = (int)Math.Clamp(rms * 11.5 * safeWidth, 0, safeWidth);
        return label + new string('━', filled) + new string('─', safeWidth - filled);
    }

    private static string BuildFrequencyRail(int width)
    {
        if (width < 20)
        {
            return new string('─', width);
        }

        var line = new char[width];
        Array.Fill(line, '─');
        WriteRailLabel(line, 0, "50Hz");
        WriteRailLabel(line, width / 2 - 2, "1k");
        WriteRailLabel(line, Math.Max(0, width - 5), "16k");
        return new string(line);
    }

    private static void WriteRailLabel(char[] line, int start, string label)
    {
        for (var i = 0; i < label.Length && start + i < line.Length; i++)
        {
            line[start + i] = label[i];
        }
    }

    private static string CenterVisualizerText(string text)
    {
        var width = Math.Max(20, Console.WindowWidth - 8);
        var padding = Math.Max(0, (width - text.Length) / 2);
        return $"[grey]{Markup.Escape(new string(' ', padding) + text)}[/]";
    }

    private static string VisualizerGradientTag(int row, int height)
    {
        var ratio = row / (float)Math.Max(1, height);
        var (r, g, b) = InterpolateGradient(ratio);
        return $"[#{r:X2}{g:X2}{b:X2}]";
    }

    private static (int R, int G, int B) InterpolateGradient(float ratio)
    {
        ReadOnlySpan<(float Stop, int R, int G, int B)> stops =
        [
            (0.00f, 92, 75, 255),
            (0.24f, 0, 194, 255),
            (0.48f, 33, 214, 132),
            (0.70f, 245, 222, 87),
            (0.86f, 255, 137, 54),
            (1.00f, 255, 72, 96)
        ];

        var safe = Math.Clamp(ratio, 0f, 1f);
        for (var i = 1; i < stops.Length; i++)
        {
            if (safe > stops[i].Stop)
            {
                continue;
            }

            var from = stops[i - 1];
            var to = stops[i];
            var local = (safe - from.Stop) / Math.Max(0.001f, to.Stop - from.Stop);
            return (
                (int)Math.Round(Lerp(from.R, to.R, local)),
                (int)Math.Round(Lerp(from.G, to.G, local)),
                (int)Math.Round(Lerp(from.B, to.B, local)));
        }

        var last = stops[^1];
        return (last.R, last.G, last.B);
    }

    private static float Lerp(float from, float to, float amount)
    {
        return from + (to - from) * Math.Clamp(amount, 0f, 1f);
    }

    private int GetRenderDelayMs(PlaybackStateSnapshot snapshot)
    {
        if (_isImmersive && snapshot.IsPlaying)
        {
            return 80;
        }

        return snapshot.IsPlaying ? 350 : 1200;
    }

    private static string PageTitle(TerminalPage page)
    {
        return page switch
        {
            TerminalPage.Daily => "每日推荐",
            TerminalPage.PersonalFm => "私人 FM",
            TerminalPage.Discover => "发现歌单",
            TerminalPage.Ranks => "排行榜",
            TerminalPage.Search => "搜索",
            TerminalPage.MyPlaylists => "我的歌单",
            TerminalPage.Queue => "播放队列",
            TerminalPage.Settings => "设置",
            TerminalPage.Account => "账号",
            _ => page.ToString()
        };
    }

    private static string Paint(string value, bool selected)
    {
        var text = Markup.Escape(TrimToWidth(value, 80));
        return selected ? $"[black on green]{text}[/]" : text;
    }

    private static string Header(string value)
    {
        return Markup.Escape(value);
    }

    private string PaintSetting(string value, int rowIndex)
    {
        return Paint(value, _settingsIndex == rowIndex);
    }

    private static string FormatTime(double seconds)
    {
        if (seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
        {
            return "--:--";
        }

        return TimeSpan.FromSeconds(seconds).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"mm\:ss");
    }

    private static string TrimToWidth(string? text, int width)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var normalized = text.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= width ? normalized : normalized[..Math.Max(0, width - 1)] + "…";
    }

    private static async Task<T> SafeLoadAsync<T>(Task<T> task, T fallback)
    {
        try
        {
            return await task;
        }
        catch
        {
            return fallback;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _playback.Dispose();
        await Task.CompletedTask;
    }
}
