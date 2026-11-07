using KgTest.Graphics;
using KgTest.Models;
using KgTest.Services;
using KgTest.Tui;
using KgTest.Views;

namespace KgTest;

// 终端播放器主控制器：单线程事件循环、焦点与模态路由、异步页面加载与差量渲染。

internal enum FocusZone
{
    Navigation,
    Content
}

internal sealed class TerminalApp : IAsyncDisposable
{
    private const int SettingsHeaderItemCount = 6;
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

    private static readonly TerminalImageMode[] ImageModes =
    [
        TerminalImageMode.Auto,
        TerminalImageMode.Kitty,
        TerminalImageMode.ITerm,
        TerminalImageMode.Sixel,
        TerminalImageMode.Blocks,
        TerminalImageMode.Off
    ];

    private readonly UiDispatcher _dispatcher = new();
    private readonly TerminalSettingsStore _settingsStore = new();
    private readonly TerminalAppSettings _settings;
    private readonly TerminalKugouClients _clients;
    private readonly TerminalDataService _data;
    private readonly TerminalVipEntitlementService _vipEntitlement;
    private readonly TerminalLyricsService _lyrics;
    private readonly TerminalPlaybackService _playback;
    private readonly ImageCache _images;
    private readonly Canvas _canvas = new();
    private readonly TerminalRenderer _renderer = new();
    private readonly Theme _theme = new();
    private readonly VisualizerView _visualizer = new();
    private readonly LoginController _login;
    private readonly TextField _searchInput = new("输入歌名、歌手或专辑关键词", 64);
    private readonly Dictionary<TerminalPage, int> _savedSelection = [];
    private TerminalInput? _input;

    private TerminalPage _page = TerminalPage.Daily;
    private FocusZone _focus = FocusZone.Content;
    private readonly List<TerminalSongItem> _songs = [];
    private readonly List<TerminalPlaylistItem> _playlists = [];
    private TerminalPlaylistItem? _openedPlaylist;
    private int _playlistReturnIndex;
    private TerminalUserProfile _profile = new();
    private string _title = "每日推荐";
    private string _searchKeyword = "";
    private string _status = "启动中";
    private int _selectedIndex;
    private int _settingsIndex;
    private int _loadVersion;
    private bool _isLoading;
    private bool _showingSongs = true;
    private bool _searchShowsPlaylists;
    private bool _searchModalOpen;
    private bool _helpModalOpen;
    private bool _exitConfirmOpen;
    private bool _exitConfirmSelected = true;
    private bool _isImmersive;
    private bool _exitRequested;
    private bool _dirty = true;

    public TerminalApp()
    {
        _settings = _settingsStore.Load();
        _clients = TerminalKugouClientFactory.Create();
        _data = new TerminalDataService(_clients);
        _vipEntitlement = new TerminalVipEntitlementService(_clients);
        _lyrics = new TerminalLyricsService(_clients.Lyric);
        _playback = new TerminalPlaybackService(_clients.Song, _lyrics, _settingsStore, _settings, _vipEntitlement);
        _images = new ImageCache(RequestRender);
        _login = new LoginController(_clients, _images, RequestRender, OnLoginCompletedAsync);
        _renderer.TrueColor = TerminalCapabilities.DetectTrueColor();
        _renderer.CellPixelSize = TerminalCapabilities.GetCellPixelSize();
        _dispatcher.UnhandledException += ex =>
        {
            _status = ex.Message;
            _dirty = true;
        };
    }

    public Task RunAsync()
    {
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(_dispatcher);
        _renderer.Enter();
        _input = new TerminalInput(_dispatcher.Wake);
        try
        {
            _ = InitializeAsync();
            var nextTick = Environment.TickCount64;

            while (!_exitRequested)
            {
                while (_input.TryReadKey(out var key))
                {
                    _ = HandleKeyAsync(key);
                    _dirty = true;
                }

                while (_input.TryReadMouse(out var mouse))
                {
                    _ = HandleMouseAsync(mouse);
                    _dirty = true;
                }

                if (_dispatcher.Drain())
                {
                    _dirty = true;
                }

                var now = Environment.TickCount64;
                if (_dirty || now >= nextTick)
                {
                    RenderFrame();
                    _dirty = false;
                    var snapshot = _playback.GetSnapshot();
                    nextTick = now + GetFrameIntervalMs(snapshot);
                }

                var wait = (int)Math.Clamp(nextTick - Environment.TickCount64, 5, 45);
                if (!_input.HasPending)
                {
                    _dispatcher.Wait(wait);
                }
            }
        }
        finally
        {
            _input.Dispose();
            _login.Dispose();
            _renderer.Exit();
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        return Task.CompletedTask;
    }

    private async Task InitializeAsync()
    {
        _profile = await _data.GetUserProfileAsync();
        if (_profile.IsLoggedIn)
        {
            _ = EnsureDailyVipInBackgroundAsync();
        }

        await LoadPageAsync(_page);
    }

    private async Task OnLoginCompletedAsync(string nickname)
    {
        _profile = await _data.GetUserProfileAsync();
        if (!string.IsNullOrWhiteSpace(nickname) && _profile.Name.StartsWith("用户 ", StringComparison.Ordinal))
        {
            _profile = new TerminalUserProfile
            {
                IsLoggedIn = true,
                UserId = _profile.UserId,
                Name = nickname,
                AvatarUrl = _profile.AvatarUrl,
                VipType = _profile.VipType
            };
        }

        _status = $"登录成功：{_profile.Name}";
        RequestRender();
        _ = EnsureDailyVipInBackgroundAsync();
        await LoadPageAsync(TerminalPage.Daily, force: true);
    }

    private async Task EnsureDailyVipInBackgroundAsync()
    {
        try
        {
            var result = await _vipEntitlement.TryEnsureDailyVipAsync();
            _status = result.Summary;
            _profile = await _data.GetUserProfileAsync();
            RequestRender();
        }
        catch (Exception ex)
        {
            _status = $"VIP 初始化失败：{ex.Message}";
            RequestRender();
        }
    }

    private void RequestRender()
    {
        _dirty = true;
        _dispatcher.Wake();
    }

    private async Task HandleKeyAsync(ConsoleKeyInfo key)
    {
        if (_exitConfirmOpen)
        {
            HandleExitConfirmKey(key);
            return;
        }

        if (_helpModalOpen)
        {
            _helpModalOpen = false;
            _renderer.Invalidate();
            return;
        }

        if (_searchModalOpen)
        {
            await HandleSearchModalKeyAsync(key);
            return;
        }

        // 在账号页的输入框处于焦点时，优先把字符键交给输入框。
        if (_page == TerminalPage.Account && _focus == FocusZone.Content && _login.IsEditingText)
        {
            if (key.Key == ConsoleKey.Escape)
            {
                _focus = FocusZone.Navigation;
                return;
            }

            if (await _login.HandleKeyAsync(key))
            {
                return;
            }
        }

        // 全局功能键
        switch (key.Key)
        {
            case ConsoleKey.F5:
                _isImmersive = false;
                await SwitchToAccountAsync(LoginTab.Qr);
                return;
            case ConsoleKey.F6:
                _isImmersive = false;
                await SwitchToAccountAsync(LoginTab.Sms);
                return;
            case ConsoleKey.F9:
                Logout();
                return;
        }

        if (key.KeyChar == '?')
        {
            _helpModalOpen = true;
            return;
        }

        if (key.Key == ConsoleKey.Oem2 || key.KeyChar == '/')
        {
            _isImmersive = false;
            _searchModalOpen = true;
            return;
        }

        if (_isImmersive)
        {
            await HandleImmersiveKeyAsync(key);
            return;
        }

        // 数字键 1-9 直接切页
        if (key.KeyChar is >= '1' and <= '9' && !(_page == TerminalPage.Account && _focus == FocusZone.Content))
        {
            var idx = key.KeyChar - '1';
            if (idx < Pages.Length)
            {
                _focus = FocusZone.Content;
                await SetPageAsync(Pages[idx]);
                return;
            }
        }

        if (_page == TerminalPage.Account && _focus == FocusZone.Content)
        {
            if (key.KeyChar == 'L')
            {
                Logout();
                return;
            }

            if (key.KeyChar is 'v' or 'V')
            {
                _ = EnsureDailyVipInBackgroundAsync();
                return;
            }

            if (await _login.HandleKeyAsync(key))
            {
                return;
            }
        }

        switch (key.Key)
        {
            case ConsoleKey.Tab:
                _focus = _focus == FocusZone.Navigation ? FocusZone.Content : FocusZone.Navigation;
                break;
            case ConsoleKey.LeftArrow:
            case ConsoleKey.H:
                if (_page == TerminalPage.Settings && _focus == FocusZone.Content)
                {
                    AdjustSetting(-1);
                }
                else
                {
                    await CyclePageAsync(-1);
                }

                break;
            case ConsoleKey.RightArrow:
            case ConsoleKey.L when key.KeyChar != 'l' && key.KeyChar != 'L':
                if (_page == TerminalPage.Settings && _focus == FocusZone.Content)
                {
                    AdjustSetting(1);
                }
                else
                {
                    await CyclePageAsync(1);
                }

                break;
            case ConsoleKey.UpArrow:
            case ConsoleKey.K:
                if (_focus == FocusZone.Navigation)
                {
                    await CyclePageAsync(-1);
                }
                else
                {
                    MoveSelection(-1);
                }

                break;
            case ConsoleKey.DownArrow:
            case ConsoleKey.J:
                if (_focus == FocusZone.Navigation)
                {
                    await CyclePageAsync(1);
                }
                else
                {
                    MoveSelection(1);
                }

                break;
            case ConsoleKey.PageUp:
                MoveSelection(-12);
                break;
            case ConsoleKey.PageDown:
                MoveSelection(12);
                break;
            case ConsoleKey.Home:
                JumpSelection(false);
                break;
            case ConsoleKey.End:
                JumpSelection(true);
                break;
            case ConsoleKey.Enter:
                if (_focus == FocusZone.Navigation)
                {
                    _focus = FocusZone.Content;
                }
                else
                {
                    await ActivateSelectionAsync();
                }

                break;
            case ConsoleKey.Backspace:
                ReturnFromPlaylist();
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
            case ConsoleKey.Oem4:
                _playback.SeekRelative(TimeSpan.FromSeconds(-SeekStepSeconds));
                break;
            case ConsoleKey.Oem6:
                _playback.SeekRelative(TimeSpan.FromSeconds(SeekStepSeconds));
                break;
            case ConsoleKey.Add:
            case ConsoleKey.OemPlus:
                if (_page == TerminalPage.Settings && _focus == FocusZone.Content)
                {
                    AdjustSetting(1);
                }
                else
                {
                    _playback.ChangeVolume(0.05f);
                }

                break;
            case ConsoleKey.Subtract:
            case ConsoleKey.OemMinus:
                if (_page == TerminalPage.Settings && _focus == FocusZone.Content)
                {
                    AdjustSetting(-1);
                }
                else
                {
                    _playback.ChangeVolume(-0.05f);
                }

                break;
            case ConsoleKey.S:
                _playback.ToggleShuffle();
                break;
            case ConsoleKey.T:
                _playback.ToggleSeamlessTransition();
                break;
            case ConsoleKey.L:
                if (key.KeyChar == 'L')
                {
                    Logout();
                }
                else
                {
                    _playback.CycleLyricMode();
                }

                break;
            case ConsoleKey.P:
                CycleImageMode();
                break;
            case ConsoleKey.I:
                _isImmersive = true;
                _renderer.Invalidate();
                break;
            case ConsoleKey.V:
                if (_page == TerminalPage.Search && _openedPlaylist == null)
                {
                    _searchShowsPlaylists = !_searchShowsPlaylists;
                    _showingSongs = !_searchShowsPlaylists;
                    _selectedIndex = 0;
                }

                break;
            case ConsoleKey.A:
                if (SelectedSong() is { } toAdd)
                {
                    _playback.EnqueueNext(toAdd);
                }

                break;
            case ConsoleKey.X:
            case ConsoleKey.Delete:
                if (_page == TerminalPage.Queue && _selectedIndex >= 0 && _selectedIndex < _songs.Count)
                {
                    _playback.RemoveFromQueueAt(_selectedIndex);
                    _songs.Clear();
                    _songs.AddRange(_playback.Queue);
                    _selectedIndex = Math.Clamp(_selectedIndex, 0, Math.Max(0, _songs.Count - 1));
                }

                break;
            case ConsoleKey.D:
                if (_page == TerminalPage.PersonalFm)
                {
                    await _data.ReportPersonalFmAsync(_playback.CurrentSong,
                        (int)_playback.GetSnapshot().Position.TotalSeconds, false, "garbage");
                    await _playback.PlayNextAsync();
                }

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
            case ConsoleKey.R:
                await LoadPageAsync(_page, force: true);
                break;
            case ConsoleKey.Escape:
                if (!ReturnFromPlaylist())
                {
                    _focus = _focus == FocusZone.Content ? FocusZone.Navigation : FocusZone.Content;
                }

                break;
            case ConsoleKey.Q:
                if (!ReturnFromPlaylist())
                {
                    _exitConfirmOpen = true;
                    _exitConfirmSelected = true;
                }

                break;
        }
    }

    private async Task HandleImmersiveKeyAsync(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.I:
            case ConsoleKey.Escape:
            case ConsoleKey.Q:
            case ConsoleKey.Backspace:
                _isImmersive = false;
                _renderer.Invalidate();
                break;
            case ConsoleKey.Spacebar:
                _playback.TogglePlayPause();
                break;
            case ConsoleKey.LeftArrow:
            case ConsoleKey.Oem4:
                _playback.SeekRelative(TimeSpan.FromSeconds(-SeekStepSeconds));
                break;
            case ConsoleKey.RightArrow:
            case ConsoleKey.Oem6:
                _playback.SeekRelative(TimeSpan.FromSeconds(SeekStepSeconds));
                break;
            case ConsoleKey.UpArrow:
            case ConsoleKey.Add:
            case ConsoleKey.OemPlus:
                _playback.ChangeVolume(0.05f);
                break;
            case ConsoleKey.DownArrow:
            case ConsoleKey.Subtract:
            case ConsoleKey.OemMinus:
                _playback.ChangeVolume(-0.05f);
                break;
            case ConsoleKey.N:
                await _playback.PlayNextAsync();
                break;
            case ConsoleKey.B:
                await _playback.PlayPreviousAsync();
                break;
            case ConsoleKey.L:
                _playback.CycleLyricMode();
                break;
            case ConsoleKey.P:
                CycleImageMode();
                break;
            case ConsoleKey.S:
                _playback.ToggleShuffle();
                break;
            case ConsoleKey.T:
                _playback.ToggleSeamlessTransition();
                break;
        }
    }

    private async Task HandleSearchModalKeyAsync(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
                _searchModalOpen = false;
                _renderer.Invalidate();
                return;
            case ConsoleKey.Enter:
                _searchModalOpen = false;
                _renderer.Invalidate();
                var keyword = _searchInput.Text.Trim();
                if (!string.IsNullOrEmpty(keyword))
                {
                    await ExecuteSearchAsync(keyword);
                }

                return;
        }

        _searchInput.HandleKey(key);
    }

    private void HandleExitConfirmKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Q:
            case ConsoleKey.Y:
                _exitRequested = true;
                break;
            case ConsoleKey.Enter:
            case ConsoleKey.Spacebar:
                if (_exitConfirmSelected)
                {
                    _exitRequested = true;
                }
                else
                {
                    _exitConfirmOpen = false;
                    _renderer.Invalidate();
                }

                break;
            case ConsoleKey.Escape:
            case ConsoleKey.N:
            case ConsoleKey.Backspace:
                _exitConfirmOpen = false;
                _renderer.Invalidate();
                break;
            case ConsoleKey.LeftArrow:
            case ConsoleKey.RightArrow:
            case ConsoleKey.Tab:
            case ConsoleKey.H:
            case ConsoleKey.L:
                _exitConfirmSelected = !_exitConfirmSelected;
                break;
        }
    }

    private async Task HandleMouseAsync(MouseEvent mouse)
    {
        if (_exitConfirmOpen)
        {
            if (mouse.Kind is not (MouseKind.LeftDown or MouseKind.LeftDoubleClick or MouseKind.RightDown))
            {
                return;
            }

            if (mouse.Kind == MouseKind.RightDown)
            {
                _exitConfirmOpen = false;
                _renderer.Invalidate();
                return;
            }

            var modal = _canvas.Bounds.Center(Math.Min(46, _canvas.Width - 6), 7);
            if (!modal.Contains(mouse.X, mouse.Y))
            {
                _exitConfirmOpen = false;
                _renderer.Invalidate();
                return;
            }

            var btnY = modal.Y + 4;
            var centerX = modal.X + modal.W / 2;
            if (mouse.Y == btnY)
            {
                if (mouse.X >= centerX - 16 && mouse.X < centerX + 2)
                {
                    _exitRequested = true;
                }
                else if (mouse.X >= centerX + 4 && mouse.X < centerX + 18)
                {
                    _exitConfirmOpen = false;
                    _renderer.Invalidate();
                }
            }

            return;
        }

        if (_helpModalOpen)
        {
            if (mouse.Kind is MouseKind.LeftDown or MouseKind.RightDown)
            {
                _helpModalOpen = false;
                _renderer.Invalidate();
            }

            return;
        }

        if (_searchModalOpen)
        {
            var modal = _canvas.Bounds.Center(Math.Min(64, _canvas.Width - 6), 5);
            if (!modal.Contains(mouse.X, mouse.Y))
            {
                if (mouse.Kind is MouseKind.LeftDown or MouseKind.RightDown)
                {
                    _searchModalOpen = false;
                    _renderer.Invalidate();
                }

                return;
            }

            if (mouse.Kind == MouseKind.LeftDown && mouse.Y == modal.Y + 2)
            {
                _searchInput.SetCursorFromVisualColumn(mouse.X - (modal.X + 2), modal.W - 4);
            }

            return;
        }

        var bounds = _canvas.Bounds;
        if (_isImmersive)
        {
            HandleImmersiveMouse(mouse, bounds);
            return;
        }

        var barHeight = 4;
        var main = bounds.SkipBottom(barHeight);
        var bar = bounds.TakeBottom(barHeight);
        var navWidth = bounds.W >= 88 ? 22 : 0;
        var sideWidth = bounds.W >= 112 ? Math.Clamp(bounds.W * 28 / 100, 30, 42) : 0;
        var navRect = navWidth > 0 ? main.TakeLeft(navWidth) : Rect.Empty;
        var sideRect = sideWidth > 0 ? main.TakeRight(sideWidth) : Rect.Empty;
        var contentRect = main.SkipLeft(navWidth).SkipRight(sideWidth);

        // 滚轮
        if (mouse.Kind is MouseKind.WheelUp or MouseKind.WheelDown)
        {
            var dir = mouse.Kind == MouseKind.WheelUp ? -1 : 1;
            if (bar.Contains(mouse.X, mouse.Y) || sideRect.Contains(mouse.X, mouse.Y))
            {
                _playback.ChangeVolume(dir < 0 ? 0.05f : -0.05f);
            }
            else if (navRect.Contains(mouse.X, mouse.Y))
            {
                await CyclePageAsync(dir);
            }
            else
            {
                MoveSelection(dir * 3);
            }

            return;
        }

        if (mouse.Kind == MouseKind.RightDown)
        {
            ReturnFromPlaylist();
            return;
        }

        // 底部播放控制栏点击（播放/暂停 & 进度条跳转）
        if (bar.Contains(mouse.X, mouse.Y))
        {
            HandlePlaybackBarMouse(mouse, bar);
            return;
        }

        // 右侧正在播放点击进入沉浸页
        if (sideRect.Contains(mouse.X, mouse.Y))
        {
            if (mouse.Kind == MouseKind.LeftDoubleClick)
            {
                _isImmersive = true;
                _renderer.Invalidate();
            }

            return;
        }

        // 左侧导航栏点击切页
        if (navRect.Contains(mouse.X, mouse.Y))
        {
            _focus = FocusZone.Navigation;
            var inner = navRect.Inset(1, 1);
            var pageIndex = mouse.Y - (inner.Y + 3);
            if (pageIndex >= 0 && pageIndex < Pages.Length)
            {
                _focus = FocusZone.Content;
                await SetPageAsync(Pages[pageIndex]);
            }

            return;
        }

        // 中间内容区点击
        if (contentRect.Contains(mouse.X, mouse.Y))
        {
            _focus = FocusZone.Content;
            await HandleContentMouseAsync(mouse, contentRect);
        }
    }

    private async Task HandleContentMouseAsync(MouseEvent mouse, Rect area)
    {
        if (_page == TerminalPage.Account)
        {
            await _login.HandleMouseAsync(mouse, area.Inset(2, 1));
            return;
        }

        var inner = area.Inset(2, 1);
        if (!inner.Contains(mouse.X, mouse.Y))
        {
            return;
        }

        if (_page == TerminalPage.Settings)
        {
            var relY = mouse.Y - inner.Y;
            if (relY >= 0 && relY < SettingsHeaderItemCount)
            {
                var same = _settingsIndex == relY;
                _settingsIndex = relY;
                if (same || mouse.Kind == MouseKind.LeftDoubleClick || mouse.X >= inner.X + 20)
                {
                    AdjustSetting(1);
                }
            }
            else if (relY >= SettingsHeaderItemCount + 2 && relY < SettingsHeaderItemCount + 12)
            {
                var band = relY - (SettingsHeaderItemCount + 2);
                _settingsIndex = SettingsHeaderItemCount + band;
                var sliderX = inner.X + 20;
                var sliderW = Math.Clamp(inner.W - 32, 12, 28);
                if (mouse.X >= sliderX && mouse.X < sliderX + sliderW)
                {
                    var ratio = (mouse.X - sliderX) / (double)Math.Max(1, sliderW - 1);
                    var targetGain = (float)Math.Round(ratio * 24.0 - 12.0);
                    var current = _playback.EqGains[band];
                    _playback.SetEqBand(band, targetGain - current);
                }
            }

            return;
        }

        if (_showingSongs)
        {
            var listArea = inner.SkipTop(2);
            if (!listArea.Contains(mouse.X, mouse.Y) || _songs.Count == 0)
            {
                return;
            }

            var visible = Math.Max(1, listArea.H);
            var start = WorkspaceViews.ComputeScrollStart(_selectedIndex, _songs.Count, visible);
            var idx = start + (mouse.Y - listArea.Y);
            if (idx >= 0 && idx < _songs.Count)
            {
                var wasSelected = _selectedIndex == idx;
                _selectedIndex = idx;
                if (mouse.Kind == MouseKind.LeftDoubleClick || wasSelected)
                {
                    await ActivateSelectionAsync();
                }
            }

            return;
        }

        // 歌单列表
        var playlistArea = inner;
        if (_selectedIndex >= 0 && _selectedIndex < _playlists.Count && inner.H >= 14 &&
            EffectiveImageMode() != TerminalImageMode.Off)
        {
            if (mouse.Y < inner.Y + 5)
            {
                await ActivateSelectionAsync();
                return;
            }

            playlistArea = inner.SkipTop(6);
        }

        var rowsArea = playlistArea.SkipTop(2);
        if (!rowsArea.Contains(mouse.X, mouse.Y) || _playlists.Count == 0)
        {
            return;
        }

        var visibleRows = Math.Max(1, rowsArea.H);
        var rowStart = WorkspaceViews.ComputeScrollStart(_selectedIndex, _playlists.Count, visibleRows);
        var playlistIdx = rowStart + (mouse.Y - rowsArea.Y);
        if (playlistIdx >= 0 && playlistIdx < _playlists.Count)
        {
            var wasSelected = _selectedIndex == playlistIdx;
            _selectedIndex = playlistIdx;
            if (mouse.Kind == MouseKind.LeftDoubleClick || wasSelected)
            {
                await ActivateSelectionAsync();
            }
        }
    }

    private void HandlePlaybackBarMouse(MouseEvent mouse, Rect bar)
    {
        var inner = bar.Inset(2, 1);
        if (mouse.Y != inner.Y)
        {
            return;
        }

        var leftW = Math.Clamp(inner.W / 4, 18, 36);
        if (mouse.X < inner.X + 8)
        {
            _playback.TogglePlayPause();
            return;
        }

        var progressX = inner.X + leftW + 2;
        var progressW = Math.Max(12, inner.Right - progressX);
        var snapshot = _playback.GetSnapshot();
        var posLen = VisualizerView.FormatTime(snapshot.Position.TotalSeconds).Length;
        var durLen = VisualizerView.FormatTime(snapshot.Duration.TotalSeconds).Length;
        var barX = progressX + posLen + 2;
        var barW = progressW - posLen - durLen - 4;
        if (barW >= 6 && mouse.X >= barX && mouse.X <= barX + barW)
        {
            var ratio = (mouse.X - barX) / (double)Math.Max(1, barW);
            _playback.SeekToRatio(ratio);
        }
    }

    private void HandleImmersiveMouse(MouseEvent mouse, Rect bounds)
    {
        if (mouse.Kind == MouseKind.RightDown)
        {
            _isImmersive = false;
            _renderer.Invalidate();
            return;
        }

        if (mouse.Kind is MouseKind.WheelUp or MouseKind.WheelDown)
        {
            _playback.ChangeVolume(mouse.Kind == MouseKind.WheelUp ? 0.05f : -0.05f);
            return;
        }

        var footer = bounds.TakeBottom(4).Inset(4, 0);
        if (mouse.Y == footer.Y + 1)
        {
            var snapshot = _playback.GetSnapshot();
            var posLen = VisualizerView.FormatTime(snapshot.Position.TotalSeconds).Length;
            var durLen = VisualizerView.FormatTime(snapshot.Duration.TotalSeconds).Length;
            var barX = footer.X + posLen + 2;
            var barW = footer.W - posLen - durLen - 4;
            if (barW >= 6 && mouse.X >= barX && mouse.X <= barX + barW)
            {
                _playback.SeekToRatio((mouse.X - barX) / (double)Math.Max(1, barW));
            }
        }
        else if (mouse.Kind == MouseKind.LeftDoubleClick)
        {
            _playback.TogglePlayPause();
        }
    }

    private async Task ExecuteSearchAsync(string keyword)
    {
        _searchKeyword = keyword;
        _page = TerminalPage.Search;
        _focus = FocusZone.Content;
        _openedPlaylist = null;
        _selectedIndex = 0;
        var version = ++_loadVersion;
        _isLoading = true;
        _title = $"搜索：{keyword}";
        _status = $"正在搜索 \"{keyword}\"...";
        _songs.Clear();
        _playlists.Clear();
        RequestRender();

        try
        {
            var songsTask = _data.SearchSongsAsync(keyword);
            var playlistsTask = _data.SearchPlaylistsAsync(keyword);
            await Task.WhenAll(songsTask, playlistsTask);
            if (version != _loadVersion)
            {
                return;
            }

            _songs.AddRange(await songsTask);
            _playlists.AddRange(await playlistsTask);
            _searchShowsPlaylists = _songs.Count == 0 && _playlists.Count > 0;
            _showingSongs = !_searchShowsPlaylists;
            _status = $"找到歌曲 {_songs.Count} 首 · 歌单 {_playlists.Count} 个（按 v 切换）";
        }
        catch (Exception ex)
        {
            if (version == _loadVersion)
            {
                _status = $"搜索失败：{ex.Message}";
            }
        }
        finally
        {
            if (version == _loadVersion)
            {
                _isLoading = false;
                RequestRender();
            }
        }
    }

    private async Task SwitchToAccountAsync(LoginTab tab)
    {
        await SetPageAsync(TerminalPage.Account);
        _focus = FocusZone.Content;
        _login.SwitchTab(tab);
        if (tab == LoginTab.Qr)
        {
            await _login.RefreshQrAsync();
        }
    }

    private async Task CyclePageAsync(int delta)
    {
        var index = Array.IndexOf(Pages, _page);
        index = (index + delta + Pages.Length) % Pages.Length;
        await SetPageAsync(Pages[index]);
    }

    private async Task SetPageAsync(TerminalPage page)
    {
        if (_page != page)
        {
            _savedSelection[_page] = _selectedIndex;
        }

        if (_page == TerminalPage.Account && page != TerminalPage.Account)
        {
            _login.StopPolling();
        }

        _page = page;
        _openedPlaylist = null;
        _searchShowsPlaylists = false;
        await LoadPageAsync(page);
    }

    private async Task LoadPageAsync(TerminalPage page, bool force = false)
    {
        if (page == TerminalPage.Search && !force && !string.IsNullOrEmpty(_searchKeyword) &&
            (_songs.Count > 0 || _playlists.Count > 0))
        {
            _title = $"搜索：{_searchKeyword}";
            _showingSongs = !_searchShowsPlaylists;
            return;
        }

        if (page == TerminalPage.Search && !string.IsNullOrEmpty(_searchKeyword) && force)
        {
            await ExecuteSearchAsync(_searchKeyword);
            return;
        }

        var version = ++_loadVersion;
        _isLoading = true;
        _songs.Clear();
        _playlists.Clear();
        _title = WorkspaceViews.PageTitle(page);
        _showingSongs = page is TerminalPage.Daily or TerminalPage.PersonalFm or TerminalPage.Queue;
        _status = "正在加载...";
        RequestRender();

        try
        {
            switch (page)
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
                    break;
                case TerminalPage.Account:
                    _profile = await _data.GetUserProfileAsync();
                    _login.EnsureStarted();
                    break;
                case TerminalPage.Search:
                    _status = "按 / 输入关键词开始搜索";
                    break;
            }

            if (version != _loadVersion)
            {
                return;
            }

            SyncPlayingFlag();
            var count = CurrentListCount();
            _selectedIndex = _savedSelection.TryGetValue(page, out var saved) && count > 0
                ? Math.Clamp(saved, 0, count - 1)
                : 0;

            if (page != TerminalPage.Search)
            {
                _status = count > 0 || page is TerminalPage.Settings or TerminalPage.Account
                    ? "就绪"
                    : _profile.IsLoggedIn
                        ? "暂无内容"
                        : "暂无内容（部分功能需按 F5 登录）";
            }
        }
        catch (Exception ex)
        {
            if (version == _loadVersion)
            {
                _status = ex.Message;
            }
        }
        finally
        {
            if (version == _loadVersion)
            {
                _isLoading = false;
                RequestRender();
            }
        }
    }

    private async Task ActivateSelectionAsync()
    {
        if (_page == TerminalPage.Settings)
        {
            AdjustSetting(1);
            return;
        }

        if (_page == TerminalPage.Search && _songs.Count == 0 && _playlists.Count == 0)
        {
            _searchModalOpen = true;
            return;
        }

        var song = SelectedSong();
        if (song != null)
        {
            await _playback.PlayAsync(song, CurrentSongContext());
            SyncPlayingFlag();
            return;
        }

        var playlist = SelectedPlaylist();
        if (playlist == null)
        {
            return;
        }

        _playlistReturnIndex = _selectedIndex;
        _openedPlaylist = playlist;
        _showingSongs = true;
        _title = playlist.Name;
        _songs.Clear();
        _selectedIndex = 0;
        var version = ++_loadVersion;
        _isLoading = true;
        _status = $"正在打开《{playlist.Name}》...";
        RequestRender();

        try
        {
            _songs.AddRange(await _data.GetPlaylistSongsAsync(playlist));
            if (version != _loadVersion)
            {
                return;
            }

            SyncPlayingFlag();
            _status = _songs.Count > 0 ? $"共 {_songs.Count} 首 · Backspace 返回" : "歌单为空 · Backspace 返回";
        }
        catch (Exception ex)
        {
            if (version == _loadVersion)
            {
                _status = ex.Message;
            }
        }
        finally
        {
            if (version == _loadVersion)
            {
                _isLoading = false;
                RequestRender();
            }
        }
    }

    private bool ReturnFromPlaylist()
    {
        if (_openedPlaylist == null)
        {
            return false;
        }

        _openedPlaylist = null;
        _showingSongs = _page == TerminalPage.Search && !_searchShowsPlaylists;
        _songs.Clear();
        _selectedIndex = Math.Clamp(_playlistReturnIndex, 0, Math.Max(0, _playlists.Count - 1));
        _title = _page == TerminalPage.Search && !string.IsNullOrEmpty(_searchKeyword)
            ? $"搜索：{_searchKeyword}"
            : WorkspaceViews.PageTitle(_page);
        _status = "就绪";
        return true;
    }

    private void MoveSelection(int delta)
    {
        if (_page == TerminalPage.Settings)
        {
            _settingsIndex = Math.Clamp(_settingsIndex + delta, 0, SettingsItemCount - 1);
            return;
        }

        var count = CurrentListCount();
        if (count <= 0)
        {
            _selectedIndex = 0;
            return;
        }

        if (Math.Abs(delta) == 1)
        {
            _selectedIndex = (_selectedIndex + delta + count) % count;
        }
        else
        {
            _selectedIndex = Math.Clamp(_selectedIndex + delta, 0, count - 1);
        }
    }

    private void JumpSelection(bool toEnd)
    {
        if (_page == TerminalPage.Settings)
        {
            _settingsIndex = toEnd ? SettingsItemCount - 1 : 0;
            return;
        }

        var count = CurrentListCount();
        _selectedIndex = toEnd ? Math.Max(0, count - 1) : 0;
    }

    private int CurrentListCount() =>
        _showingSongs ? _songs.Count : _playlists.Count;

    private IReadOnlyList<TerminalSongItem> CurrentSongContext() =>
        _page == TerminalPage.Queue ? _playback.Queue : _songs;

    private TerminalSongItem? SelectedSong() =>
        _showingSongs && _selectedIndex >= 0 && _selectedIndex < _songs.Count ? _songs[_selectedIndex] : null;

    private TerminalPlaylistItem? SelectedPlaylist() =>
        !_showingSongs && _selectedIndex >= 0 && _selectedIndex < _playlists.Count ? _playlists[_selectedIndex] : null;

    private void AdjustSetting(int direction)
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
            case 5:
                CycleImageMode(direction);
                break;
            default:
                _playback.SetEqBand(_settingsIndex - SettingsHeaderItemCount, direction);
                break;
        }
    }

    private void CycleImageMode(int direction = 1)
    {
        var idx = Array.IndexOf(ImageModes, _settings.ImageMode);
        idx = (idx + direction + ImageModes.Length) % ImageModes.Length;
        _settings.ImageMode = ImageModes[idx];
        _settingsStore.Save(_settings);
        _renderer.Invalidate();
        var effective = EffectiveImageMode();
        _status = $"图片显示：{TerminalCapabilities.DisplayName(_settings.ImageMode)}（当前生效：{TerminalCapabilities.DisplayName(effective)}）";
    }

    private TerminalImageMode EffectiveImageMode() =>
        _settings.ImageMode == TerminalImageMode.Auto
            ? TerminalCapabilities.DetectImageMode()
            : _settings.ImageMode;

    private void Logout()
    {
        _login.StopPolling();
        _clients.Login.LogOutAsync();
        _profile = new TerminalUserProfile { IsLoggedIn = false, Name = "未登录" };
        _status = "已退出登录";
    }

    private void SyncPlayingFlag()
    {
        var currentHash = _playback.CurrentSong?.Hash;
        foreach (var song in _songs)
        {
            song.IsPlaying = !string.IsNullOrEmpty(currentHash) &&
                             string.Equals(song.Hash, currentHash, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void RenderFrame()
    {
        var (width, height) = GetConsoleSize();
        var effectiveMode = EffectiveImageMode();
        _canvas.ImageMode = effectiveMode;
        _canvas.Reset(width, height);

        if (_page == TerminalPage.Queue && !_isLoading)
        {
            _songs.Clear();
            _songs.AddRange(_playback.Queue);
            _selectedIndex = Math.Clamp(_selectedIndex, 0, Math.Max(0, _songs.Count - 1));
        }

        SyncPlayingFlag();
        var snapshot = _playback.GetSnapshot();

        if (_isImmersive)
        {
            _visualizer.RenderImmersive(_canvas, snapshot, _images, _theme);
        }
        else
        {
            RenderWorkspace(snapshot, effectiveMode);
        }

        if (_searchModalOpen)
        {
            WorkspaceViews.RenderSearchModal(_canvas, _searchInput, _theme);
        }

        if (_helpModalOpen)
        {
            WorkspaceViews.RenderHelpModal(_canvas, _theme);
        }

        if (_exitConfirmOpen)
        {
            WorkspaceViews.RenderExitConfirmModal(_canvas, _exitConfirmSelected, _theme);
        }

        _renderer.Present(_canvas);
    }

    private void RenderWorkspace(PlaybackStateSnapshot snapshot, TerminalImageMode effectiveMode)
    {
        var bounds = _canvas.Bounds;
        var barHeight = 4;
        var main = bounds.SkipBottom(barHeight);
        var bar = bounds.TakeBottom(barHeight);

        var navWidth = bounds.W >= 88 ? 22 : 0;
        var sideWidth = bounds.W >= 112 ? Math.Clamp(bounds.W * 28 / 100, 30, 42) : 0;
        var contentRect = main.SkipLeft(navWidth).SkipRight(sideWidth);

        if (navWidth > 0)
        {
            WorkspaceViews.RenderNavigation(
                _canvas,
                main.TakeLeft(navWidth),
                Pages,
                _page,
                _profile,
                _focus == FocusZone.Navigation,
                _theme);
        }

        RenderContentArea(contentRect);

        if (sideWidth > 0)
        {
            WorkspaceViews.RenderNowPlayingSide(
                _canvas,
                main.TakeRight(sideWidth),
                snapshot,
                _playback.MusicQuality,
                _images,
                _theme);
        }

        WorkspaceViews.RenderPlaybackBar(
            _canvas,
            bar,
            snapshot,
            _playback.StatusMessage,
            _status,
            effectiveMode,
            _theme);
    }

    private void RenderContentArea(Rect area)
    {
        var focused = _focus == FocusZone.Content;
        switch (_page)
        {
            case TerminalPage.Settings:
                WorkspaceViews.RenderSettings(
                    _canvas,
                    area,
                    _settings,
                    EffectiveImageMode(),
                    _playback,
                    _settingsIndex,
                    focused,
                    _theme);
                return;
            case TerminalPage.Account:
                var border = focused ? new CellStyle(_theme.Accent) : _theme.BorderStyle;
                _canvas.Box(area, border, "账号与登录 · F5 扫码 · F6 短信 · L 退出登录",
                    focused ? _theme.AccentBold : _theme.Bold);
                _login.Render(_canvas, area.Inset(2, 1), _profile, _theme, focused);
                return;
        }

        var subtitle = BuildContentSubtitle();
        if (_showingSongs)
        {
            WorkspaceViews.RenderSongs(
                _canvas,
                area,
                _title,
                subtitle,
                _songs,
                _selectedIndex,
                focused,
                _isLoading,
                _theme);
        }
        else
        {
            WorkspaceViews.RenderPlaylists(
                _canvas,
                area,
                _title,
                subtitle,
                _playlists,
                _selectedIndex,
                focused,
                _isLoading,
                _images,
                _theme);
        }
    }

    private string BuildContentSubtitle()
    {
        if (_openedPlaylist != null)
        {
            return $"共 {_songs.Count} 首 · Backspace 返回";
        }

        if (_page == TerminalPage.Search)
        {
            return _searchShowsPlaylists
                ? $"歌单 ({_playlists.Count}) · 按 v 切到歌曲 ({_songs.Count}) · / 重新搜索"
                : $"歌曲 ({_songs.Count}) · 按 v 切到歌单 ({_playlists.Count}) · / 重新搜索";
        }

        if (_page == TerminalPage.Queue)
        {
            return $"共 {_songs.Count} 首 · x 移除选中";
        }

        if (_page == TerminalPage.PersonalFm)
        {
            return "Enter 播放 · d 不喜欢并切歌 · r 换一批";
        }

        return _showingSongs ? $"共 {_songs.Count} 首 · Enter 播放 · a 下一首播放" : $"共 {_playlists.Count} 个 · Enter 打开";
    }

    private int GetFrameIntervalMs(PlaybackStateSnapshot snapshot)
    {
        if (_page == TerminalPage.Account && _login.CooldownSeconds > 0)
        {
            return 250;
        }

        if (_isImmersive && snapshot.IsPlaying)
        {
            return 60;
        }

        return snapshot.IsPlaying ? 250 : 900;
    }

    private static (int Width, int Height) GetConsoleSize()
    {
        try
        {
            var w = Console.WindowWidth;
            var h = Console.WindowHeight;
            if (w > 20 && h > 10)
            {
                return (w, h);
            }
        }
        catch
        {
        }

        return (120, 34);
    }

    public async ValueTask DisposeAsync()
    {
        _input?.Dispose();
        _login.Dispose();
        _images.Dispose();
        _playback.Dispose();
        _renderer.Dispose();
        await Task.CompletedTask;
    }
}
