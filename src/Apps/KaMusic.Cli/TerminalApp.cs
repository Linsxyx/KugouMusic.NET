using KaMusic.Cli.Graphics;
using KaMusic.Cli.Models;
using KaMusic.Cli.Services;
using KaMusic.Cli.Tui;
using KaMusic.Cli.Views;

namespace KaMusic.Cli;

// 终端播放器主控制器：单线程事件循环、焦点与模态路由、异步页面加载与差量渲染。

internal enum FocusZone
{
    Navigation,
    Content
}

internal enum LocalInputModalMode
{
    None,
    ImportPath,
    CreatePlaylist
}

internal sealed class TerminalApp : IAsyncDisposable
{
    private const int SettingsHeaderItemCount = 7;
    private const int SeekStepSeconds = 10;
    private static int SettingsItemCount => SettingsHeaderItemCount + 10;

    private static readonly TerminalPage[] Pages =
    [
        TerminalPage.Daily,
        TerminalPage.PersonalFm,
        TerminalPage.Discover,
        TerminalPage.LocalMusic,
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
    private readonly TerminalPersonalFmService _personalFm;
    private readonly TerminalPlaybackService _playback;
    private readonly ImageCache _images;
    private readonly Canvas _canvas = new();
    private readonly TerminalRenderer _renderer = new();
    private readonly Theme _theme = new();
    private readonly VisualizerView _visualizer = new();
    private readonly LoginController _login;
    private readonly TerminalUpdateService _updater = new();
    private readonly ExploreViewState _exploreState = new();
    private readonly PersonalFmHitRegions _fmHitRegions = new();
    private readonly List<TerminalSongItem> _fmPreviewSongs = [];
    private TerminalPersonalFmMode _fmMode = TerminalPersonalFmMode.Normal;
    private TerminalPersonalFmSongPoolId _fmSongPoolId = TerminalPersonalFmSongPoolId.Taste;
    private bool _fmLoading;
    private int _fmLoadVersion;
    private readonly TextField _searchInput = new("输入歌名、歌手或专辑关键词", 64);
    private readonly TextField _localPathInput = new("输入本地音乐文件夹或音频文件路径（支持 ~ 展开）", 260);
    private readonly TextField _localPlaylistNameInput = new("输入新建本地歌单名称", 64);
    private readonly Dictionary<TerminalPage, int> _savedSelection = [];
    private TerminalInput? _input;

    private TerminalPage _page = TerminalPage.Daily;
    private FocusZone _focus = FocusZone.Content;
    private readonly List<TerminalSongItem> _songs = [];
    private readonly List<TerminalPlaylistItem> _playlists = [];
    private TerminalPlaylistItem? _openedPlaylist;
    private int _playlistReturnIndex;
    private TerminalSingerDetail? _openedSinger;
    private int _singerReturnIndex;
    private bool _openedFromSinger;
    private bool _singerPickerOpen;
    private int _singerPickerIndex;
    private IReadOnlyList<(long Id, string Name)> _singerPickerItems = [];
    private bool _playlistPickerOpen;
    private int _playlistPickerIndex;
    private TerminalSongItem? _playlistPickerTargetSong;
    private readonly List<TerminalPlaylistItem> _playlistPickerItems = [];
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
    private LocalInputModalMode _localModalMode;
    private bool _loginPromptOpen;
    private string? _loginPromptSongName;
    private bool _helpModalOpen;
    private bool _exitConfirmOpen;
    private bool _exitConfirmSelected = true;
    private bool _isImmersive;
    private bool _exitRequested;
    private bool _dirty = true;

    public bool UpdateRequested { get; private set; }

    public TerminalApp()
    {
        _settings = _settingsStore.Load();
        _clients = TerminalKugouClientFactory.Create();
        _data = new TerminalDataService(_clients);
        _vipEntitlement = new TerminalVipEntitlementService(_clients);
        _lyrics = new TerminalLyricsService(_clients.Lyric);
        _personalFm = new TerminalPersonalFmService(_clients, _data.IsLiked);
        _personalFm.StateChanged += () => _dispatcher.Post(() =>
        {
            if (_personalFm.IsActive)
            {
                _fmMode = _personalFm.CurrentMode;
                _fmSongPoolId = _personalFm.CurrentSongPoolId;
            }

            RequestRender();
        });
        _playback = new TerminalPlaybackService(
            _clients.Song,
            _lyrics,
            _settingsStore,
            _settings,
            _vipEntitlement,
            () => _clients.User.IsLoggedIn(),
            _personalFm);
        _playback.LoginRequired += OnPlaybackLoginRequired;
        _images = new ImageCache(RequestRender);
        _login = new LoginController(_clients, _images, RequestRender, OnLoginCompletedAsync);
        _renderer.TrueColor = TerminalCapabilities.DetectTrueColor();
        _renderer.UpdateCellPixelSize(_canvas, TerminalCapabilities.GetCellPixelSize());
        _dispatcher.UnhandledException += ex =>
        {
            _status = ex.Message;
            _dirty = true;
        };
    }

    private void OnPlaybackLoginRequired(TerminalSongItem song)
    {
        _loginPromptSongName = song.Name;
        _loginPromptOpen = true;
        _status = $"请先登录：登录后才能播放在线音乐《{song.Name}》";
        RequestRender();
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
        _ = CheckUpdateInBackgroundAsync(silentWhenLatest: true);
        _profile = await _data.GetUserProfileAsync();
        if (_profile.IsLoggedIn)
        {
            _ = EnsureDailyVipInBackgroundAsync();
            _ = _data.EnsureLikeListLoadedAsync();
        }

        await LoadPageAsync(_page);
    }

    private async Task CheckUpdateInBackgroundAsync(bool silentWhenLatest)
    {
        try
        {
            if (!silentWhenLatest)
            {
                _status = "正在检查 npm (@linsxyx/kamusic) 最新版本...";
                RequestRender();
            }

            var (hasUpdate, message) = await _updater.CheckForUpdateAsync();
            if (hasUpdate || !silentWhenLatest)
            {
                _status = message;
            }

            RequestRender();
        }
        catch (Exception ex)
        {
            if (!silentWhenLatest)
            {
                _status = $"检查更新失败：{ex.Message}";
                RequestRender();
            }
        }
    }

    private void TriggerCheckOrApplyUpdate()
    {
        if (_updater.HasUpdate)
        {
            UpdateRequested = true;
            _exitRequested = true;
            return;
        }

        _ = CheckUpdateInBackgroundAsync(silentWhenLatest: false);
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
        _ = _data.EnsureLikeListLoadedAsync();
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

        if (_loginPromptOpen)
        {
            await HandleLoginPromptKeyAsync(key);
            return;
        }

        if (_helpModalOpen)
        {
            _helpModalOpen = false;
            _renderer.Invalidate();
            return;
        }

        if (_localModalMode != LocalInputModalMode.None)
        {
            await HandleLocalModalKeyAsync(key);
            return;
        }

        if (_singerPickerOpen)
        {
            await HandleSingerPickerKeyAsync(key);
            return;
        }

        if (_playlistPickerOpen)
        {
            await HandlePlaylistPickerKeyAsync(key);
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
                else if (IsExploreCardViewActive() && _focus == FocusZone.Content)
                {
                    MoveExploreCard(-1, 0);
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
                else if (IsExploreCardViewActive() && _focus == FocusZone.Content)
                {
                    MoveExploreCard(1, 0);
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
                else if (IsExploreCardViewActive())
                {
                    MoveExploreCard(0, -1);
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
                else if (IsExploreCardViewActive())
                {
                    MoveExploreCard(0, 1);
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
                else if (_page == TerminalPage.PersonalFm && _openedPlaylist == null && _openedSinger == null)
                {
                    await StartOrTogglePersonalFmAsync();
                }
                else
                {
                    await ActivateSelectionAsync();
                }

                break;
            case ConsoleKey.Backspace:
                ReturnFromDetail();
                break;
            case ConsoleKey.Spacebar:
                if (_page == TerminalPage.PersonalFm && !_personalFm.IsActive && _openedPlaylist == null && _openedSinger == null)
                {
                    await StartOrTogglePersonalFmAsync();
                }
                else
                {
                    _playback.TogglePlayPause();
                }

                break;
            case ConsoleKey.N:
                await _playback.PlayNextAsync();
                break;
            case ConsoleKey.B:
                await _playback.PlayPreviousAsync();
                break;
            case ConsoleKey.Oem4:
                if (_openedSinger != null)
                {
                    await CycleSingerTabAsync(-1);
                }
                else if (IsExploreCardViewActive())
                {
                    CycleExploreTab(-1);
                }
                else
                {
                    _playback.SeekRelative(TimeSpan.FromSeconds(-SeekStepSeconds));
                }

                break;
            case ConsoleKey.Oem6:
                if (_openedSinger != null)
                {
                    await CycleSingerTabAsync(1);
                }
                else if (IsExploreCardViewActive())
                {
                    CycleExploreTab(1);
                }
                else
                {
                    _playback.SeekRelative(TimeSpan.FromSeconds(SeekStepSeconds));
                }

                break;
            case ConsoleKey.OemComma:
                if (_openedSinger != null)
                {
                    await CycleSingerTabAsync(-1);
                }
                else if (IsExploreCardViewActive() && _exploreState.ActiveTab == TerminalExploreTab.Playlists)
                {
                    await CycleExploreTagAsync(-1);
                }

                break;
            case ConsoleKey.OemPeriod:
                if (_openedSinger != null)
                {
                    await CycleSingerTabAsync(1);
                }
                else if (IsExploreCardViewActive() && _exploreState.ActiveTab == TerminalExploreTab.Playlists)
                {
                    await CycleExploreTagAsync(1);
                }

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
                if (_openedSinger != null)
                {
                    await SwitchSingerSortAsync();
                }
                else
                {
                    _playback.ToggleShuffle();
                }

                break;
            case ConsoleKey.T:
                _playback.ToggleSeamlessTransition();
                break;
            case ConsoleKey.A:
                EnqueueNextForCurrentOrSelected();
                break;
            case ConsoleKey.F:
                await OpenAddToPlaylistPickerAsync();
                break;
            case ConsoleKey.G:
                await JumpToSingerAsync();
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
                if (_openedSinger != null && _openedPlaylist == null)
                {
                    await SetSingerTabAsync(_openedSinger.Tab == TerminalSingerTab.Albums
                        ? TerminalSingerTab.HotSongs
                        : TerminalSingerTab.Albums);
                }
                else if (_page == TerminalPage.PersonalFm && _openedPlaylist == null)
                {
                    var nextPool = _fmSongPoolId == TerminalPersonalFmSongPoolId.Taste
                        ? TerminalPersonalFmSongPoolId.Style
                        : TerminalPersonalFmSongPoolId.Taste;
                    await SwitchPersonalFmSongPoolAsync(nextPool);
                }
                else if (_page == TerminalPage.Search && _openedPlaylist == null)
                {
                    _searchShowsPlaylists = !_searchShowsPlaylists;
                    _showingSongs = !_searchShowsPlaylists;
                    _selectedIndex = 0;
                }

                break;
            case ConsoleKey.M:
                if (_page == TerminalPage.PersonalFm && _openedPlaylist == null && _openedSinger == null)
                {
                    await SwitchPersonalFmModeAsync(TerminalPersonalFmPresentation.GetNextMode(_fmMode));
                }

                break;
            case ConsoleKey.O:
                if (_page == TerminalPage.LocalMusic)
                {
                    OpenLocalInputModal(LocalInputModalMode.ImportPath);
                }

                break;
            case ConsoleKey.C:
                if (_page == TerminalPage.LocalMusic && _openedPlaylist == null)
                {
                    OpenLocalInputModal(LocalInputModalMode.CreatePlaylist);
                }
                else if (_page == TerminalPage.PersonalFm && _openedPlaylist == null && _openedSinger == null)
                {
                    await ToggleFavoriteForCurrentOrSelectedAsync();
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
                else if (_page == TerminalPage.LocalMusic)
                {
                    await DeleteSelectedLocalItemAsync();
                }

                break;
            case ConsoleKey.D:
                if (_page == TerminalPage.PersonalFm)
                {
                    await DislikePersonalFmAsync();
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
            case ConsoleKey.U:
                TriggerCheckOrApplyUpdate();
                break;
            case ConsoleKey.Escape:
                if (!ReturnFromDetail())
                {
                    _focus = _focus == FocusZone.Content ? FocusZone.Navigation : FocusZone.Content;
                }

                break;
            case ConsoleKey.Q:
                if (!ReturnFromDetail())
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
            case ConsoleKey.A:
                EnqueueNextForCurrentOrSelected();
                break;
            case ConsoleKey.F:
                await OpenAddToPlaylistPickerAsync();
                break;
            case ConsoleKey.G:
                await JumpToSingerAsync();
                break;
        }
    }

    private async Task HandleLoginPromptKeyAsync(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Enter:
            case ConsoleKey.F5:
                _loginPromptOpen = false;
                _isImmersive = false;
                _renderer.Invalidate();
                await SwitchToAccountAsync(LoginTab.Qr);
                break;
            case ConsoleKey.F6:
                _loginPromptOpen = false;
                _isImmersive = false;
                _renderer.Invalidate();
                await SwitchToAccountAsync(LoginTab.Sms);
                break;
            case ConsoleKey.Escape:
            case ConsoleKey.Backspace:
            case ConsoleKey.Q:
                _loginPromptOpen = false;
                _renderer.Invalidate();
                break;
        }
    }

    private void OpenLocalInputModal(LocalInputModalMode mode)
    {
        _localModalMode = mode;
        if (mode == LocalInputModalMode.CreatePlaylist)
        {
            _localPlaylistNameInput.Text = "";
        }
    }

    private async Task HandleLocalModalKeyAsync(ConsoleKeyInfo key)
    {
        var activeInput = _localModalMode == LocalInputModalMode.CreatePlaylist
            ? _localPlaylistNameInput
            : _localPathInput;

        switch (key.Key)
        {
            case ConsoleKey.Escape:
                _localModalMode = LocalInputModalMode.None;
                _renderer.Invalidate();
                return;
            case ConsoleKey.Enter:
                var mode = _localModalMode;
                var value = activeInput.Text.Trim();
                _localModalMode = LocalInputModalMode.None;
                _renderer.Invalidate();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    await ExecuteLocalModalActionAsync(mode, value);
                }

                return;
        }

        activeInput.HandleKey(key);
    }

    // 执行本地音乐歌单导入或新建操作，并同步刷新本地音乐库视图。
    private async Task ExecuteLocalModalActionAsync(LocalInputModalMode mode, string rawValue)
    {
        try
        {
            if (mode == LocalInputModalMode.CreatePlaylist)
            {
                var created = await _data.LocalMusic.CreatePlaylistAsync(rawValue);
                _status = $"已创建本地歌单：《{created.Name}》（按 o 可向歌单导入音频路径）";
                await LoadPageAsync(TerminalPage.LocalMusic, force: true);
                var idx = _playlists.FindIndex(p => p.LocalPlaylistId == created.LocalPlaylistId);
                if (idx >= 0)
                {
                    _selectedIndex = idx;
                }

                return;
            }

            _isLoading = true;
            _status = $"正在扫描并导入本地音乐：{rawValue} ...";
            RequestRender();

            var targetPlaylistId = _openedPlaylist?.Kind == TerminalPlaylistKind.Local
                ? _openedPlaylist.LocalPlaylistId
                : (long?)null;
            var (playlist, addedCount) = await _data.LocalMusic.ImportPathAsync(rawValue, targetPlaylistId);
            if (_openedPlaylist != null && _openedPlaylist.LocalPlaylistId == playlist.LocalPlaylistId)
            {
                _songs.Clear();
                _songs.AddRange(await _data.LocalMusic.GetPlaylistTracksAsync(playlist.LocalPlaylistId, playlist.Name));
                _openedPlaylist.Count = _songs.Count;
                SyncPlayingFlag();
                _status = $"已向《{playlist.Name}》导入 {addedCount} 首本地歌曲（共 {_songs.Count} 首）";
            }
            else
            {
                await LoadPageAsync(TerminalPage.LocalMusic, force: true);
                var idx = _playlists.FindIndex(p => p.LocalPlaylistId == playlist.LocalPlaylistId);
                if (idx >= 0)
                {
                    _selectedIndex = idx;
                }

                _status = $"本地歌单《{playlist.Name}》导入完成，共 {playlist.Count} 首歌曲";
            }
        }
        catch (Exception ex)
        {
            _status = $"本地导入失败：{ex.Message}";
        }
        finally
        {
            _isLoading = false;
            RequestRender();
        }
    }

    private async Task DeleteSelectedLocalItemAsync()
    {
        if (_openedPlaylist?.Kind == TerminalPlaylistKind.Local)
        {
            var song = SelectedSong();
            if (song == null || song.LocalTrackId <= 0)
            {
                return;
            }

            await _data.LocalMusic.RemoveTrackFromPlaylistAsync(_openedPlaylist.LocalPlaylistId, song.LocalTrackId);
            _songs.RemoveAt(_selectedIndex);
            _selectedIndex = Math.Clamp(_selectedIndex, 0, Math.Max(0, _songs.Count - 1));
            _status = $"已从《{_openedPlaylist.Name}》移除：{song.Name}";
            return;
        }

        var playlist = SelectedPlaylist();
        if (playlist == null || playlist.Kind != TerminalPlaylistKind.Local || playlist.LocalPlaylistId <= 0)
        {
            return;
        }

        await _data.LocalMusic.DeletePlaylistAsync(playlist.LocalPlaylistId);
        _playlists.RemoveAt(_selectedIndex);
        _selectedIndex = Math.Clamp(_selectedIndex, 0, Math.Max(0, _playlists.Count - 1));
        _status = $"已删除本地歌单：《{playlist.Name}》";
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

        if (_loginPromptOpen)
        {
            if (mouse.Kind is not (MouseKind.LeftDown or MouseKind.LeftDoubleClick or MouseKind.RightDown))
            {
                return;
            }

            if (mouse.Kind == MouseKind.RightDown)
            {
                _loginPromptOpen = false;
                _renderer.Invalidate();
                return;
            }

            var modal = _canvas.Bounds.Center(Math.Min(58, _canvas.Width - 6), 8);
            if (!modal.Contains(mouse.X, mouse.Y))
            {
                _loginPromptOpen = false;
                _renderer.Invalidate();
                return;
            }

            var btnY = modal.Y + 5;
            var centerX = modal.X + modal.W / 2;
            if (mouse.Y == btnY)
            {
                if (mouse.X >= centerX - 24 && mouse.X < centerX - 3)
                {
                    _loginPromptOpen = false;
                    _isImmersive = false;
                    _renderer.Invalidate();
                    await SwitchToAccountAsync(LoginTab.Qr);
                }
                else if (mouse.X >= centerX - 1 && mouse.X < centerX + 14)
                {
                    _loginPromptOpen = false;
                    _isImmersive = false;
                    _renderer.Invalidate();
                    await SwitchToAccountAsync(LoginTab.Sms);
                }
                else if (mouse.X >= centerX + 16 && mouse.X < centerX + 28)
                {
                    _loginPromptOpen = false;
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

        if (_localModalMode != LocalInputModalMode.None)
        {
            var modal = _canvas.Bounds.Center(Math.Min(70, _canvas.Width - 6), 6);
            if (!modal.Contains(mouse.X, mouse.Y))
            {
                if (mouse.Kind is MouseKind.LeftDown or MouseKind.RightDown)
                {
                    _localModalMode = LocalInputModalMode.None;
                    _renderer.Invalidate();
                }

                return;
            }

            if (mouse.Kind == MouseKind.LeftDown && mouse.Y == modal.Y + 2)
            {
                var input = _localModalMode == LocalInputModalMode.CreatePlaylist
                    ? _localPlaylistNameInput
                    : _localPathInput;
                input.SetCursorFromVisualColumn(mouse.X - (modal.X + 2), modal.W - 4);
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

        if (_playlistPickerOpen)
        {
            var h = Math.Min(_playlistPickerItems.Count + 4, 16);
            var modal = _canvas.Bounds.Center(Math.Min(60, _canvas.Width - 6), h);
            if (!modal.Contains(mouse.X, mouse.Y))
            {
                if (mouse.Kind is MouseKind.LeftDown or MouseKind.RightDown)
                {
                    _playlistPickerOpen = false;
                    _renderer.Invalidate();
                }

                return;
            }

            var inner = modal.Inset(2, 1);
            if (mouse.Kind is MouseKind.LeftDown or MouseKind.LeftDoubleClick)
            {
                var clickedRow = mouse.Y - inner.Y;
                var start = WorkspaceViews.ComputeScrollStart(_playlistPickerIndex, _playlistPickerItems.Count, inner.H);
                var targetIdx = start + clickedRow;
                if (targetIdx >= 0 && targetIdx < _playlistPickerItems.Count)
                {
                    _playlistPickerIndex = targetIdx;
                    var chosen = _playlistPickerItems[targetIdx];
                    var song = _playlistPickerTargetSong;
                    _playlistPickerOpen = false;
                    _renderer.Invalidate();
                    if (song != null)
                    {
                        await AddSongToPlaylistAsync(song, chosen);
                    }
                }
            }
            else if (mouse.Kind == MouseKind.WheelUp)
            {
                _playlistPickerIndex = Math.Max(0, _playlistPickerIndex - 1);
                _renderer.Invalidate();
            }
            else if (mouse.Kind == MouseKind.WheelDown)
            {
                _playlistPickerIndex = Math.Min(_playlistPickerItems.Count - 1, _playlistPickerIndex + 1);
                _renderer.Invalidate();
            }

            return;
        }

        if (_singerPickerOpen)
        {
            var h = Math.Min(_singerPickerItems.Count + 4, 14);
            var modal = _canvas.Bounds.Center(Math.Min(50, _canvas.Width - 6), h);
            if (!modal.Contains(mouse.X, mouse.Y))
            {
                if (mouse.Kind is MouseKind.LeftDown or MouseKind.RightDown)
                {
                    _singerPickerOpen = false;
                    _renderer.Invalidate();
                }

                return;
            }

            var inner = modal.Inset(2, 1);
            if (mouse.Kind is MouseKind.LeftDown or MouseKind.LeftDoubleClick)
            {
                var clickedRow = mouse.Y - inner.Y;
                var start = WorkspaceViews.ComputeScrollStart(_singerPickerIndex, _singerPickerItems.Count, inner.H);
                var targetIdx = start + clickedRow;
                if (targetIdx >= 0 && targetIdx < _singerPickerItems.Count)
                {
                    _singerPickerIndex = targetIdx;
                    var chosen = _singerPickerItems[targetIdx];
                    _singerPickerOpen = false;
                    _renderer.Invalidate();
                    await OpenSingerPageAsync(chosen.Id, chosen.Name);
                }
            }
            else if (mouse.Kind == MouseKind.WheelUp)
            {
                _singerPickerIndex = Math.Max(0, _singerPickerIndex - 1);
                _renderer.Invalidate();
            }
            else if (mouse.Kind == MouseKind.WheelDown)
            {
                _singerPickerIndex = Math.Min(_singerPickerItems.Count - 1, _singerPickerIndex + 1);
                _renderer.Invalidate();
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
        var hideSidePanel = _page == TerminalPage.PersonalFm && _openedPlaylist == null && _openedSinger == null;
        var sideWidth = !hideSidePanel && bounds.W >= 112 ? Math.Clamp(bounds.W * 28 / 100, 30, 42) : 0;
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
            else if (IsExploreCardViewActive())
            {
                MoveExploreCard(0, dir);
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

        if (_page == TerminalPage.PersonalFm && _openedPlaylist == null && _openedSinger == null)
        {
            await HandlePersonalFmMouseAsync(mouse);
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

        if (IsExploreCardViewActive())
        {
            await HandleExploreMouseAsync(mouse);
            return;
        }

        if (_openedSinger != null && _openedPlaylist == null)
        {
            if (mouse.Y == inner.Y)
            {
                if (mouse.X >= inner.X && mouse.X < inner.X + 13)
                    await SetSingerTabAsync(TerminalSingerTab.HotSongs);
                else if (mouse.X >= inner.X + 13 && mouse.X < inner.X + 26)
                    await SetSingerTabAsync(TerminalSingerTab.NewSongs);
                else if (mouse.X >= inner.X + 26 && mouse.X < inner.X + 39)
                    await SetSingerTabAsync(TerminalSingerTab.Albums);
                return;
            }

            var listArea = inner.SkipTop(4);
            if (!listArea.Contains(mouse.X, mouse.Y))
                return;

            var count = CurrentListCount();
            if (count == 0) return;

            var visible = Math.Max(1, listArea.H);
            var start = WorkspaceViews.ComputeScrollStart(_selectedIndex, count, visible);
            var idx = start + (mouse.Y - listArea.Y);
            if (idx >= 0 && idx < count)
            {
                var wasSelected = _selectedIndex == idx;
                _selectedIndex = idx;
                if (mouse.Kind == MouseKind.LeftDoubleClick || wasSelected)
                {
                    await ActivateSelectionAsync();
                }
                else
                {
                    RequestRender();
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
                else
                {
                    RequestRender();
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
            var localTask = _data.LocalMusic.SearchTracksAsync(keyword);
            var songsTask = _data.SearchSongsAsync(keyword);
            var playlistsTask = _data.SearchPlaylistsAsync(keyword);
            await Task.WhenAll(localTask, songsTask, playlistsTask);
            if (version != _loadVersion)
            {
                return;
            }

            _songs.AddRange(await localTask);
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
        _openedSinger = null;
        _openedFromSinger = false;
        _singerPickerOpen = false;
        _playlistPickerOpen = false;
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
        if (page == TerminalPage.PersonalFm)
        {
            _showingSongs = false;
            await LoadPersonalFmPageAsync(force);
            return;
        }

        _showingSongs = page is TerminalPage.Daily or TerminalPage.Queue;
        _status = "正在加载...";
        RequestRender();

        try
        {
            switch (page)
            {
                case TerminalPage.Daily:
                    _songs.AddRange(await _data.GetDailySongsAsync());
                    break;
                case TerminalPage.Discover:
                    var topCardsTask = _data.GetTopCardsAsync();
                    var tagsTask = _data.GetPlaylistTagsAsync();
                    var selectedTag = _exploreState.SelectedTagIndex >= 0 && _exploreState.SelectedTagIndex < _exploreState.Tags.Count
                        ? _exploreState.Tags[_exploreState.SelectedTagIndex]
                        : new TerminalPlaylistTagItem { Id = 0, CategoryId = 0, Name = "全部推荐" };
                    var playlistCardsTask = _data.GetDiscoverPlaylistCardsAsync(selectedTag.CategoryId, selectedTag.Name);
                    var rankCardsTask = _data.GetRankExploreCardsAsync();
                    await Task.WhenAll(topCardsTask, tagsTask, playlistCardsTask, rankCardsTask);
                    _exploreState.TopCards = await topCardsTask;
                    _exploreState.Tags = await tagsTask;
                    _exploreState.PlaylistCards = await playlistCardsTask;
                    var (featuredRanks, allRanks) = await rankCardsTask;
                    _exploreState.FeaturedRankCards = featuredRanks;
                    _exploreState.AllRankCards = allRanks;
                    break;
                case TerminalPage.LocalMusic:
                    if (force)
                    {
                        _ = await _data.LocalMusic.RefreshImportedFoldersAsync();
                    }

                    _playlists.AddRange(await _data.LocalMusic.GetPlaylistsAsync());
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

            if (page == TerminalPage.LocalMusic)
            {
                _status = _playlists.Count > 0
                    ? $"共 {_playlists.Count} 个本地歌单 · o 导入音频目录或文件 · c 新建歌单 · r 刷新文件夹"
                    : "暂无本地歌单 · 按 o 或 Enter 导入本地音乐文件夹（与桌面端共享本地库）";
            }
            else if (page == TerminalPage.Discover)
            {
                var cardCount = _exploreState.GetActiveCards().Count;
                _status = cardCount > 0
                    ? $"探索发现就绪（{cardCount} 张卡片）· ←↑↓→ 移动卡片 · [ / ] 切换专区 · Enter 打开"
                    : "暂无探索卡片数据（按 r 重试）";
            }
            else if (page != TerminalPage.Search)
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
        if (_openedSinger != null && _openedPlaylist == null)
        {
            if (_openedSinger.Tab == TerminalSingerTab.Albums)
            {
                if (_selectedIndex >= 0 && _selectedIndex < _openedSinger.Albums.Count)
                {
                    var album = _openedSinger.Albums[_selectedIndex];
                    _openedFromSinger = true;
                    await OpenPlaylistDetailAsync(new TerminalPlaylistItem
                    {
                        Id = album.AlbumId.ToString(),
                        Name = album.Name,
                        Subtitle = album.Subtitle,
                        Cover = album.Cover,
                        Kind = TerminalPlaylistKind.Album
                    }, _selectedIndex);
                }
            }
            else
            {
                var singerSongs = _openedSinger.CurrentSongs;
                if (_selectedIndex >= 0 && _selectedIndex < singerSongs.Count)
                {
                    await _playback.PlayAsync(singerSongs[_selectedIndex], singerSongs);
                    SyncPlayingFlag();
                }
            }
            return;
        }

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

        if (_page == TerminalPage.LocalMusic && !_showingSongs && _playlists.Count == 0)
        {
            OpenLocalInputModal(LocalInputModalMode.ImportPath);
            return;
        }

        if (IsExploreCardViewActive())
        {
            await ActivateSelectedExploreCardAsync();
            return;
        }

        var song = SelectedSong();
        if (song != null)
        {
            // 与 PC 队列页一致：选择已有队列项时不传歌单上下文。
            var isQueuePage = _page == TerminalPage.Queue && _openedPlaylist == null && _openedSinger == null;
            await _playback.PlayAsync(song, isQueuePage ? null : CurrentSongContext());
            SyncPlayingFlag();
            return;
        }

        var playlist = SelectedPlaylist();
        if (playlist == null)
        {
            return;
        }

        await OpenPlaylistDetailAsync(playlist, _selectedIndex);
    }

    private async Task OpenPlaylistDetailAsync(TerminalPlaylistItem playlist, int returnIndex)
    {
        _playlistReturnIndex = returnIndex;
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
            // 大歌单（如「我喜欢」1598 首）必须按服务端总歌曲数逐页翻到底，并按歌单原始顺序追加。
            var loadedSongs = await _data.GetAllPlaylistSongsAsync(
                playlist,
                new Progress<int>(count =>
                {
                    if (version == _loadVersion)
                    {
                        _status = $"正在打开《{playlist.Name}》... 已加载 {count} 首";
                        RequestRender();
                    }
                }));
            if (version != _loadVersion)
            {
                return;
            }

            _songs.AddRange(loadedSongs);
            SyncPlayingFlag();
            _status = _songs.Count > 0
                ? playlist.Kind == TerminalPlaylistKind.Local
                    ? $"共 {_songs.Count} 首 · Enter 播放 · o 导入音频 · x 移除 · Backspace 返回"
                    : $"共 {_songs.Count} 首 · Enter 播放 · Backspace 返回"
                : playlist.Kind == TerminalPlaylistKind.Local
                    ? "本地歌单为空 · 按 o 导入音频文件夹或音频文件 · Backspace 返回"
                    : "歌单为空 · Backspace 返回";
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
        if (_page == TerminalPage.Discover)
        {
            var cardCount = _exploreState.GetActiveCards().Count;
            _exploreState.SelectedCardIndex = Math.Clamp(_playlistReturnIndex, 0, Math.Max(0, cardCount - 1));
        }
        else
        {
            _selectedIndex = Math.Clamp(_playlistReturnIndex, 0, Math.Max(0, _playlists.Count - 1));
        }

        _title = _page == TerminalPage.Search && !string.IsNullOrEmpty(_searchKeyword)
            ? $"搜索：{_searchKeyword}"
            : WorkspaceViews.PageTitle(_page);
        _status = "就绪";
        return true;
    }

    private bool ReturnFromDetail()
    {
        if (_playlistPickerOpen)
        {
            _playlistPickerOpen = false;
            RequestRender();
            return true;
        }

        if (_singerPickerOpen)
        {
            _singerPickerOpen = false;
            RequestRender();
            return true;
        }

        if (_openedPlaylist != null)
        {
            if (_openedFromSinger)
            {
                _openedPlaylist = null;
                _openedFromSinger = false;
                _selectedIndex = Math.Clamp(_playlistReturnIndex, 0, Math.Max(0, (_openedSinger?.Albums.Count ?? 1) - 1));
                _title = $"歌手 · {_openedSinger?.Name}";
                _status = $"返回「{_openedSinger?.Name}」歌手主页";
                RequestRender();
                return true;
            }

            return ReturnFromPlaylist();
        }

        if (_openedSinger != null)
        {
            var singerName = _openedSinger.Name;
            _openedSinger = null;
            _selectedIndex = _singerReturnIndex;
            _title = WorkspaceViews.PageTitle(_page);
            _status = $"已从「{singerName}」返回";
            RequestRender();
            return true;
        }

        return false;
    }

    private TerminalSongItem? ResolveTargetSong()
    {
        if (_isImmersive)
        {
            return _playback.CurrentSong;
        }

        if (_page == TerminalPage.PersonalFm && _openedPlaylist == null && _openedSinger == null)
        {
            return _personalFm.IsActive
                ? _personalFm.CurrentSong ?? _playback.CurrentSong
                : _fmPreviewSongs.FirstOrDefault() ?? _playback.CurrentSong;
        }

        return SelectedSong() ?? _playback.CurrentSong;
    }

    // 弹出「添加到歌单」对话框，异步获取用户歌单列表供选择。
    private async Task OpenAddToPlaylistPickerAsync()
    {
        var target = ResolveTargetSong();
        if (target == null)
        {
            _status = "未选择歌曲";
            RequestRender();
            return;
        }

        if (!_profile.IsLoggedIn)
        {
            ShowLoginPrompt(target.Name);
            return;
        }

        if (string.IsNullOrWhiteSpace(target.Hash))
        {
            _status = "该歌曲没有在线音频标识，无法添加到歌单";
            RequestRender();
            return;
        }

        _status = "正在加载歌单列表...";
        RequestRender();

        var playlists = await _data.GetUserCreatedPlaylistsAsync();
        if (playlists.Count == 0)
        {
            _status = "未找到可用歌单，请先在手机或网页端创建歌单";
            RequestRender();
            return;
        }

        _playlistPickerTargetSong = target;
        _playlistPickerItems.Clear();
        _playlistPickerItems.AddRange(playlists);
        _playlistPickerIndex = 0;
        _playlistPickerOpen = true;
        _status = $"请选择要将《{target.Name}》添加到的目标歌单";
        RequestRender();
    }

    // 处理「添加到歌单」弹窗的快捷键选择与回车确认。
    private async Task HandlePlaylistPickerKeyAsync(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
            case ConsoleKey.Backspace:
            case ConsoleKey.Q:
                _playlistPickerOpen = false;
                _status = "已取消添加到歌单";
                RequestRender();
                break;
            case ConsoleKey.UpArrow:
            case ConsoleKey.K:
                _playlistPickerIndex = Math.Max(0, _playlistPickerIndex - 1);
                RequestRender();
                break;
            case ConsoleKey.DownArrow:
            case ConsoleKey.J:
                _playlistPickerIndex = Math.Min(_playlistPickerItems.Count - 1, _playlistPickerIndex + 1);
                RequestRender();
                break;
            case ConsoleKey.Enter:
                if (_playlistPickerIndex >= 0 && _playlistPickerIndex < _playlistPickerItems.Count && _playlistPickerTargetSong != null)
                {
                    var chosen = _playlistPickerItems[_playlistPickerIndex];
                    var song = _playlistPickerTargetSong;
                    _playlistPickerOpen = false;
                    RequestRender();
                    await AddSongToPlaylistAsync(song, chosen);
                }
                break;
            default:
                if (key.KeyChar is >= '1' and <= '9')
                {
                    var idx = key.KeyChar - '1';
                    if (idx < _playlistPickerItems.Count && _playlistPickerTargetSong != null)
                    {
                        var chosen = _playlistPickerItems[idx];
                        var song = _playlistPickerTargetSong;
                        _playlistPickerOpen = false;
                        RequestRender();
                        await AddSongToPlaylistAsync(song, chosen);
                    }
                }
                break;
        }
    }

    // 执行添加单曲到目标歌单，并在状态栏提示操作结果。
    private async Task AddSongToPlaylistAsync(TerminalSongItem song, TerminalPlaylistItem playlist)
    {
        _status = $"正在将《{song.Name}》添加到「{playlist.Name}」...";
        RequestRender();

        var (_, message) = await _data.AddSongToPlaylistAsync(song, playlist);
        _status = message;
        SyncPlayingFlag();
        RequestRender();
    }

    private async Task ToggleFavoriteForCurrentOrSelectedAsync()
    {
        var target = ResolveTargetSong();
        if (target == null)
        {
            _status = "未选择歌曲";
            RequestRender();
            return;
        }

        if (!_profile.IsLoggedIn)
        {
            ShowLoginPrompt(target.Name);
            return;
        }

        var (_, _, message) = await _data.ToggleFavoriteAsync(target);
        _status = message;
        SyncPlayingFlag();
        RequestRender();
    }

    private void EnqueueNextForCurrentOrSelected()
    {
        var target = ResolveTargetSong();
        if (target == null)
        {
            _status = "未选择歌曲";
            RequestRender();
            return;
        }

        _playback.EnqueueNext(target);
        _status = $"已加入下一首播放：《{target.Name}》";
        if (_page == TerminalPage.Queue && _openedSinger == null && _openedPlaylist == null)
        {
            _songs.Clear();
            _songs.AddRange(_playback.Queue);
        }

        RequestRender();
    }

    private async Task JumpToSingerAsync()
    {
        var song = ResolveTargetSong();
        if (song == null)
        {
            _status = "没有正在播放或选中的歌曲";
            RequestRender();
            return;
        }

        if (song.Singers is { Count: > 1 })
        {
            _singerPickerItems = song.Singers.Select(s => (s.Id, s.Name)).ToList();
            _singerPickerIndex = 0;
            _singerPickerOpen = true;
            RequestRender();
            return;
        }

        var authorId = song.AuthorId;
        var singerName = song.Singer;
        if (authorId <= 0 && song.Singers is { Count: 1 } list)
        {
            authorId = list[0].Id;
            singerName = list[0].Name;
        }

        await OpenSingerPageAsync(authorId, singerName);
    }

    private async Task OpenSingerPageAsync(long authorId, string singerName)
    {
        if (string.IsNullOrWhiteSpace(singerName) && authorId <= 0)
        {
            _status = "无法识别歌手信息";
            RequestRender();
            return;
        }

        if (authorId <= 0)
        {
            _status = $"正在查找歌手 \"{singerName}\"...";
            RequestRender();
            var results = await _data.SearchSingersAsync(singerName);
            if (results.Count == 0)
            {
                _status = $"未找到歌手 \"{singerName}\"";
                RequestRender();
                return;
            }

            if (results.Count > 1)
            {
                var exact = results.FirstOrDefault(r => string.Equals(r.Name, singerName, StringComparison.OrdinalIgnoreCase));
                if (exact == null)
                {
                    _singerPickerItems = results.Select(r => (r.AuthorId, r.Name)).ToList();
                    _singerPickerIndex = 0;
                    _singerPickerOpen = true;
                    RequestRender();
                    return;
                }

                authorId = exact.AuthorId;
                singerName = exact.Name;
            }
            else
            {
                authorId = results[0].AuthorId;
                singerName = results[0].Name;
            }
        }

        _isImmersive = false;
        _singerReturnIndex = _selectedIndex;
        _selectedIndex = 0;
        _isLoading = true;
        _status = $"正在加载歌手「{singerName}」...";
        RequestRender();

        var detail = await _data.GetSingerDetailAsync(authorId, singerName);
        if (detail == null)
        {
            _status = "加载歌手失败";
            _isLoading = false;
            RequestRender();
            return;
        }

        detail.HotSongs.AddRange(await _data.GetSingerSongsAsync(authorId, "hot"));
        detail.HasLoadedHotSongs = true;
        _openedSinger = detail;
        _title = $"歌手 · {detail.Name}";
        _isLoading = false;
        SyncPlayingFlag();
        _status = $"已打开「{detail.Name}」主页 · {detail.HotSongs.Count} 首热门单曲 · s 切换排序 · [ / ] 切换专辑";
        RequestRender();
    }

    private async Task SwitchSingerSortAsync()
    {
        if (_openedSinger == null)
            return;

        if (_openedSinger.Tab == TerminalSingerTab.Albums)
        {
            await SetSingerTabAsync(TerminalSingerTab.HotSongs);
            return;
        }

        if (_openedSinger.Tab == TerminalSingerTab.HotSongs)
        {
            _openedSinger.Tab = TerminalSingerTab.NewSongs;
            if (!_openedSinger.HasLoadedNewSongs)
            {
                _isLoading = true;
                _status = $"正在加载「{_openedSinger.Name}」最新单曲...";
                RequestRender();
                _openedSinger.NewSongs.AddRange(await _data.GetSingerSongsAsync(_openedSinger.AuthorId, "new"));
                _openedSinger.HasLoadedNewSongs = true;
                _isLoading = false;
            }

            _selectedIndex = 0;
            SyncPlayingFlag();
            _status = $"已切换到「{_openedSinger.Name}」最新单曲 · 共 {_openedSinger.NewSongs.Count} 首";
        }
        else
        {
            _openedSinger.Tab = TerminalSingerTab.HotSongs;
            if (!_openedSinger.HasLoadedHotSongs)
            {
                _isLoading = true;
                _status = $"正在加载「{_openedSinger.Name}」热门单曲...";
                RequestRender();
                _openedSinger.HotSongs.AddRange(await _data.GetSingerSongsAsync(_openedSinger.AuthorId, "hot"));
                _openedSinger.HasLoadedHotSongs = true;
                _isLoading = false;
            }

            _selectedIndex = 0;
            SyncPlayingFlag();
            _status = $"已切换到「{_openedSinger.Name}」热门单曲 · 共 {_openedSinger.HotSongs.Count} 首";
        }

        RequestRender();
    }

    private async Task CycleSingerTabAsync(int direction)
    {
        if (_openedSinger == null)
            return;

        var current = (int)_openedSinger.Tab;
        var next = (current + direction) % 3;
        if (next < 0) next += 3;
        await SetSingerTabAsync((TerminalSingerTab)next);
    }

    private async Task SetSingerTabAsync(TerminalSingerTab tab)
    {
        if (_openedSinger == null)
            return;

        _openedSinger.Tab = tab;
        _selectedIndex = 0;
        if (tab == TerminalSingerTab.Albums && !_openedSinger.HasLoadedAlbums)
        {
            _isLoading = true;
            _status = $"正在加载「{_openedSinger.Name}」专辑列表...";
            RequestRender();
            _openedSinger.Albums.AddRange(await _data.GetSingerAlbumsAsync(_openedSinger.AuthorId));
            _openedSinger.HasLoadedAlbums = true;
            _isLoading = false;
            _status = $"已加载「{_openedSinger.Name}」专辑共 {_openedSinger.Albums.Count} 张 · Enter 查看曲目 · Backspace 返回";
        }
        else if (tab == TerminalSingerTab.NewSongs && !_openedSinger.HasLoadedNewSongs)
        {
            _isLoading = true;
            _status = $"正在加载「{_openedSinger.Name}」最新单曲...";
            RequestRender();
            _openedSinger.NewSongs.AddRange(await _data.GetSingerSongsAsync(_openedSinger.AuthorId, "new"));
            _openedSinger.HasLoadedNewSongs = true;
            _isLoading = false;
            _status = $"已加载「{_openedSinger.Name}」最新单曲共 {_openedSinger.NewSongs.Count} 首";
        }
        else if (tab == TerminalSingerTab.HotSongs && !_openedSinger.HasLoadedHotSongs)
        {
            _isLoading = true;
            _status = $"正在加载「{_openedSinger.Name}」热门单曲...";
            RequestRender();
            _openedSinger.HotSongs.AddRange(await _data.GetSingerSongsAsync(_openedSinger.AuthorId, "hot"));
            _openedSinger.HasLoadedHotSongs = true;
            _isLoading = false;
            _status = $"已加载「{_openedSinger.Name}」热门单曲共 {_openedSinger.HotSongs.Count} 首";
        }

        SyncPlayingFlag();
        RequestRender();
    }

    private async Task HandleSingerPickerKeyAsync(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
            case ConsoleKey.Backspace:
            case ConsoleKey.Q:
                _singerPickerOpen = false;
                RequestRender();
                break;
            case ConsoleKey.UpArrow:
            case ConsoleKey.K:
                _singerPickerIndex = Math.Max(0, _singerPickerIndex - 1);
                RequestRender();
                break;
            case ConsoleKey.DownArrow:
            case ConsoleKey.J:
                _singerPickerIndex = Math.Min(_singerPickerItems.Count - 1, _singerPickerIndex + 1);
                RequestRender();
                break;
            case ConsoleKey.Enter:
                if (_singerPickerIndex >= 0 && _singerPickerIndex < _singerPickerItems.Count)
                {
                    var chosen = _singerPickerItems[_singerPickerIndex];
                    _singerPickerOpen = false;
                    await OpenSingerPageAsync(chosen.Id, chosen.Name);
                }
                break;
            default:
                if (key.KeyChar is >= '1' and <= '9')
                {
                    var idx = key.KeyChar - '1';
                    if (idx < _singerPickerItems.Count)
                    {
                        var chosen = _singerPickerItems[idx];
                        _singerPickerOpen = false;
                        await OpenSingerPageAsync(chosen.Id, chosen.Name);
                    }
                }
                break;
        }
    }

    private void ShowLoginPrompt(string? songName)
    {
        _loginPromptSongName = songName;
        _loginPromptOpen = true;
        _status = string.IsNullOrWhiteSpace(songName)
            ? "请先登录酷狗账号（按 F5 扫码登录 / F6 短信登录）"
            : $"请先登录：登录后才能使用此功能《{songName}》";
        RequestRender();
    }

    private bool IsExploreCardViewActive() =>
        _page == TerminalPage.Discover && _openedPlaylist == null;

    private void MoveExploreCard(int dx, int dy)
    {
        var cards = _exploreState.GetActiveCards();
        _exploreState.SelectedCardIndex = ExploreView.MoveCardSelection(
            _exploreState.SelectedCardIndex,
            dx,
            dy,
            cards.Count,
            _exploreState.LastGridColumns);
        _selectedIndex = _exploreState.SelectedCardIndex;
    }

    private void CycleExploreTab(int delta)
    {
        TerminalExploreTab[] tabs =
        [
            TerminalExploreTab.Overview,
            TerminalExploreTab.TopCards,
            TerminalExploreTab.Playlists,
            TerminalExploreTab.Ranks
        ];
        var idx = Array.IndexOf(tabs, _exploreState.ActiveTab);
        idx = (idx + delta + tabs.Length) % tabs.Length;
        _exploreState.ActiveTab = tabs[idx];
        _exploreState.SelectedCardIndex = 0;
        _selectedIndex = 0;
    }

    private async Task CycleExploreTagAsync(int delta)
    {
        if (_exploreState.Tags.Count == 0)
        {
            return;
        }

        var nextIdx = (_exploreState.SelectedTagIndex + delta + _exploreState.Tags.Count) % _exploreState.Tags.Count;
        await SelectExploreTagAsync(nextIdx);
    }

    private async Task SelectExploreTagAsync(int tagIndex)
    {
        if (tagIndex < 0 || tagIndex >= _exploreState.Tags.Count)
        {
            return;
        }

        _exploreState.SelectedTagIndex = tagIndex;
        _exploreState.SelectedCardIndex = 0;
        var tag = _exploreState.Tags[tagIndex];
        _isLoading = true;
        _status = $"正在加载「{tag.Name}」歌单...";
        RequestRender();

        try
        {
            _exploreState.PlaylistCards = await _data.GetDiscoverPlaylistCardsAsync(tag.CategoryId, tag.Name);
            _status = $"已切换歌单标签：{tag.Name}（{_exploreState.PlaylistCards.Count} 个歌单）";
        }
        catch (Exception ex)
        {
            _status = $"加载分类歌单失败：{ex.Message}";
        }
        finally
        {
            _isLoading = false;
            RequestRender();
        }
    }

    private async Task ActivateSelectedExploreCardAsync()
    {
        var cards = _exploreState.GetActiveCards();
        if (cards.Count == 0)
        {
            return;
        }

        var idx = Math.Clamp(_exploreState.SelectedCardIndex, 0, cards.Count - 1);
        var card = cards[idx];
        if (card.Kind == TerminalExploreCardKind.Action && card.TargetTab.HasValue)
        {
            _exploreState.ActiveTab = card.TargetTab.Value;
            _exploreState.SelectedCardIndex = 0;
            _selectedIndex = 0;
            return;
        }

        if (card.Playlist != null)
        {
            await OpenPlaylistDetailAsync(card.Playlist, idx);
        }
    }

    private async Task HandleExploreMouseAsync(MouseEvent mouse)
    {
        if (mouse.Kind is not (MouseKind.LeftDown or MouseKind.LeftDoubleClick))
        {
            return;
        }

        foreach (var (rect, tab) in _exploreState.TabHitBoxes)
        {
            if (rect.Contains(mouse.X, mouse.Y))
            {
                _exploreState.ActiveTab = tab;
                _exploreState.SelectedCardIndex = 0;
                _selectedIndex = 0;
                return;
            }
        }

        foreach (var (rect, tagIdx) in _exploreState.TagHitBoxes)
        {
            if (rect.Contains(mouse.X, mouse.Y))
            {
                await SelectExploreTagAsync(tagIdx);
                return;
            }
        }

        foreach (var (rect, cardIdx) in _exploreState.CardHitBoxes)
        {
            if (rect.Contains(mouse.X, mouse.Y))
            {
                var wasSelected = _exploreState.SelectedCardIndex == cardIdx;
                _exploreState.SelectedCardIndex = cardIdx;
                _selectedIndex = cardIdx;
                if (mouse.Kind == MouseKind.LeftDoubleClick || wasSelected)
                {
                    await ActivateSelectedExploreCardAsync();
                }

                return;
            }
        }
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

    private int CurrentListCount()
    {
        if (_openedSinger != null && _openedPlaylist == null)
        {
            return _openedSinger.Tab == TerminalSingerTab.Albums
                ? _openedSinger.Albums.Count
                : _openedSinger.CurrentSongs.Count;
        }

        if (IsExploreCardViewActive())
        {
            return _exploreState.GetActiveCards().Count;
        }

        return _showingSongs ? CurrentSongContext().Count : _playlists.Count;
    }

    private IReadOnlyList<TerminalSongItem> CurrentSongContext()
    {
        if (_openedSinger != null && _openedPlaylist == null)
        {
            return _openedSinger.CurrentSongs;
        }

        return _page == TerminalPage.Queue && _openedPlaylist == null ? _playback.Queue : _songs;
    }

    private TerminalSongItem? SelectedSong()
    {
        if (_openedSinger != null && _openedPlaylist == null)
        {
            if (_openedSinger.Tab == TerminalSingerTab.Albums)
                return null;

            var list = _openedSinger.CurrentSongs;
            return _selectedIndex >= 0 && _selectedIndex < list.Count ? list[_selectedIndex] : null;
        }

        var context = CurrentSongContext();
        return _showingSongs && _selectedIndex >= 0 && _selectedIndex < context.Count ? context[_selectedIndex] : null;
    }

    private TerminalPlaylistItem? SelectedPlaylist() =>
        !_showingSongs && _openedSinger == null && _selectedIndex >= 0 && _selectedIndex < _playlists.Count ? _playlists[_selectedIndex] : null;

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
            case 6:
                TriggerCheckOrApplyUpdate();
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
        _personalFm.Clear();
        _fmPreviewSongs.Clear();
        _clients.Login.LogOutAsync();
        _profile = new TerminalUserProfile { IsLoggedIn = false, Name = "未登录" };
        _status = "已退出登录";
    }

    private void SyncPlayingFlag()
    {
        var current = _playback.CurrentSong;
        foreach (var song in _songs)
        {
            song.IsPlaying = song.IsSameTrack(current);
            song.IsFavorite = _data.IsLiked(song.Hash);
        }

        if (_openedSinger != null)
        {
            foreach (var song in _openedSinger.HotSongs)
            {
                song.IsPlaying = song.IsSameTrack(current);
                song.IsFavorite = _data.IsLiked(song.Hash);
            }
            foreach (var song in _openedSinger.NewSongs)
            {
                song.IsPlaying = song.IsSameTrack(current);
                song.IsFavorite = _data.IsLiked(song.Hash);
            }
        }

        if (current != null)
        {
            current.IsFavorite = _data.IsLiked(current.Hash);
        }

        if (_personalFm.CurrentSong != null)
        {
            _personalFm.CurrentSong.IsFavorite = _data.IsLiked(_personalFm.CurrentSong.Hash);
        }

        foreach (var song in _fmPreviewSongs)
        {
            song.IsPlaying = song.IsSameTrack(current);
            song.IsFavorite = _data.IsLiked(song.Hash);
        }
    }

    private void RenderFrame()
    {
        // Font/line-spacing changes can leave the row and column counts unchanged.
        _renderer.UpdateCellPixelSize(_canvas, TerminalCapabilities.GetCellPixelSize());
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

        if (_singerPickerOpen)
        {
            WorkspaceViews.RenderSingerPickerModal(_canvas, _singerPickerItems, _singerPickerIndex, _theme);
        }

        if (_playlistPickerOpen && _playlistPickerTargetSong != null)
        {
            WorkspaceViews.RenderPlaylistPickerModal(_canvas, _playlistPickerTargetSong.Name, _playlistPickerItems, _playlistPickerIndex, _theme);
        }

        if (_searchModalOpen)
        {
            WorkspaceViews.RenderSearchModal(_canvas, _searchInput, _theme);
        }

        if (_localModalMode != LocalInputModalMode.None)
        {
            var isCreate = _localModalMode == LocalInputModalMode.CreatePlaylist;
            var modalTitle = isCreate
                ? "新建本地歌单"
                : _openedPlaylist?.Kind == TerminalPlaylistKind.Local
                    ? $"导入本地音频到《{_openedPlaylist.Name}》"
                    : "导入本地歌单（支持音频文件夹或音频文件路径）";
            var modalHint = isCreate
                ? "Enter 创建本地歌单   Esc 取消"
                : "Enter 立即扫描并写入共享本地音乐库   Esc 取消";
            var input = isCreate ? _localPlaylistNameInput : _localPathInput;
            WorkspaceViews.RenderLocalInputModal(_canvas, modalTitle, modalHint, input, _theme);
        }

        if (_loginPromptOpen)
        {
            WorkspaceViews.RenderLoginRequiredModal(_canvas, _loginPromptSongName, _theme);
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
        var hideSidePanel = _page == TerminalPage.PersonalFm && _openedPlaylist == null && _openedSinger == null;
        var sideWidth = !hideSidePanel && bounds.W >= 112 ? Math.Clamp(bounds.W * 28 / 100, 30, 42) : 0;
        var contentRect = main.SkipLeft(navWidth).SkipRight(sideWidth);

        if (navWidth > 0)
        {
            WorkspaceViews.RenderNavigation(
                _canvas,
                main.TakeLeft(navWidth),
                Pages,
                _page,
                _profile,
                _updater,
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
                    _updater,
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
            case TerminalPage.PersonalFm when _openedPlaylist == null && _openedSinger == null:
                var snapshot = _playback.GetSnapshot();
                var fmSongs = _personalFm.IsActive ? _personalFm.GetDisplaySongs(5) : _fmPreviewSongs;
                var fmState = new PersonalFmViewState
                {
                    IsLoggedIn = _profile.IsLoggedIn,
                    IsLoading = _fmLoading,
                    IsFmActive = _personalFm.IsActive,
                    IsPlaying = snapshot.IsPlaying,
                    IsPaused = snapshot.IsPaused,
                    SelectedMode = _fmMode,
                    SelectedSongPoolId = _fmSongPoolId,
                    CurrentSong = _personalFm.IsActive
                        ? _personalFm.CurrentSong ?? snapshot.CurrentSong
                        : _fmPreviewSongs.FirstOrDefault(),
                    UpcomingSongs = fmSongs.Skip(1).ToList(),
                    HasHistory = _personalFm.HistoryCount > 0,
                    Quality = _playback.MusicQuality,
                    PlaybackSnapshot = snapshot
                };
                PersonalFmView.Render(_canvas, area, fmState, _fmHitRegions, focused, _images, _theme);
                return;
        }

        var subtitle = BuildContentSubtitle();
        if (IsExploreCardViewActive())
        {
            ExploreView.Render(_canvas, area, _exploreState, focused, _isLoading, _images, _theme);
            return;
        }

        if (_openedSinger != null && _openedPlaylist == null)
        {
            WorkspaceViews.RenderSingerPage(
                _canvas,
                area,
                _openedSinger,
                _selectedIndex,
                focused,
                _isLoading,
                _images,
                _theme);
            return;
        }

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
            var emptyMessage = _page == TerminalPage.LocalMusic
                ? "暂无本地歌单 · 按 o 或 Enter 导入本地音乐文件夹（与桌面客户端共享本地库）· 按 c 新建歌单"
                : null;
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
                _theme,
                emptyMessage);
        }
    }

    private string BuildContentSubtitle()
    {
        if (_openedPlaylist != null)
        {
            return _openedPlaylist.Kind == TerminalPlaylistKind.Local
                ? $"共 {_songs.Count} 首 · Enter 播放 · o 导入音频 · x 移除 · Backspace 返回"
                : $"共 {_songs.Count} 首 · Enter 播放 · a 下一首 · f 加歌单 · g 歌手 · Backspace 返回";
        }

        if (_page == TerminalPage.LocalMusic)
        {
            return $"共 {_playlists.Count} 个本地歌单 · Enter 打开 · o 导入目录/文件 · c 新建歌单 · r 刷新 · x 删除";
        }

        if (_page == TerminalPage.Search)
        {
            return _searchShowsPlaylists
                ? $"歌单 ({_playlists.Count}) · 按 v 切到歌曲 ({_songs.Count}) · / 重新搜索"
                : $"歌曲 ({_songs.Count}) · 按 v 切到歌单 ({_playlists.Count}) · / 重新搜索";
        }

        if (_page == TerminalPage.Queue)
        {
            return $"共 {_songs.Count} 首 · a 下一首 · f 加歌单 · g 歌手 · x 移除选中";
        }

        if (_page == TerminalPage.PersonalFm)
        {
            return "Enter 播放 · a 下一首 · f 加歌单 · g 歌手 · d 不喜欢并切歌 · r 换一批";
        }

        return _showingSongs ? $"共 {_songs.Count} 首 · Enter 播放 · a 下一首 · f 加歌单 · g 歌手" : $"共 {_playlists.Count} 个 · Enter 打开";
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

    // 加载或刷新私人 FM 页面：未激活时拉取预览并在空闲时自动开播；已激活且非强制刷新时直接同步会话状态。
    private async Task LoadPersonalFmPageAsync(bool force)
    {
        if (!_profile.IsLoggedIn)
        {
            _fmPreviewSongs.Clear();
            _fmLoading = false;
            _status = "登录后开启私人 FM（按 F5 扫码 / F6 短信）";
            RequestRender();
            return;
        }

        if (_personalFm.IsActive && !force)
        {
            _fmMode = _personalFm.CurrentMode;
            _fmSongPoolId = _personalFm.CurrentSongPoolId;
            _fmLoading = false;
            _status = $"私人 FM · {TerminalPersonalFmPresentation.GetTitle(_fmMode)}";
            RequestRender();
            return;
        }

        var fmVersion = ++_fmLoadVersion;
        _fmLoading = true;
        _status = "正在获取私人 FM 推荐...";
        RequestRender();

        try
        {
            var songs = await _personalFm.FetchPreviewAsync(_fmMode, _fmSongPoolId);
            if (fmVersion != _fmLoadVersion)
            {
                return;
            }

            _fmPreviewSongs.Clear();
            _fmPreviewSongs.AddRange(songs);

            if (songs.Count == 0)
            {
                _status = "暂时没有新的私人 FM 推荐";
                return;
            }

            if (force || _playback.CurrentSong == null || _personalFm.IsActive)
            {
                await _playback.StartPersonalFmAsync(songs, _fmMode, _fmSongPoolId, songs[0]);
                SyncPlayingFlag();
                _status = $"私人 FM 已就绪：{songs[0].Name}";
            }
            else
            {
                _status = $"私人 FM 预览已加载：{songs[0].Name}（按 Enter/Space 开始播放）";
            }
        }
        catch (Exception ex)
        {
            if (fmVersion == _fmLoadVersion)
            {
                _status = $"私人 FM 加载失败：{ex.Message}";
            }
        }
        finally
        {
            if (fmVersion == _fmLoadVersion)
            {
                _fmLoading = false;
                RequestRender();
            }
        }
    }

    private async Task StartOrTogglePersonalFmAsync()
    {
        if (!_profile.IsLoggedIn)
        {
            ShowLoginPrompt("私人 FM");
            return;
        }

        if (_personalFm.IsActive)
        {
            _playback.TogglePlayPause();
            return;
        }

        if (_fmPreviewSongs.Count > 0)
        {
            await _playback.StartPersonalFmAsync(_fmPreviewSongs, _fmMode, _fmSongPoolId, _fmPreviewSongs[0]);
            SyncPlayingFlag();
            return;
        }

        await LoadPersonalFmPageAsync(force: true);
    }

    private async Task DislikePersonalFmAsync()
    {
        if (!_personalFm.IsActive)
        {
            _status = "请先开始播放私人 FM";
            RequestRender();
            return;
        }

        var dislikedName = _personalFm.CurrentSong?.Name;
        var ok = await _playback.DislikeCurrentPersonalFmAsync();
        SyncPlayingFlag();
        if (ok && !string.IsNullOrWhiteSpace(dislikedName))
        {
            _status = $"已标记不喜欢《{dislikedName}》，已切到下一首";
        }

        RequestRender();
    }

    private async Task SwitchPersonalFmModeAsync(TerminalPersonalFmMode mode)
    {
        if (_fmMode == mode && _personalFm.IsActive)
        {
            return;
        }

        _fmMode = mode;
        await LoadPersonalFmPageAsync(force: true);
    }

    private async Task SwitchPersonalFmSongPoolAsync(TerminalPersonalFmSongPoolId songPoolId)
    {
        if (_fmSongPoolId == songPoolId && _personalFm.IsActive)
        {
            return;
        }

        _fmSongPoolId = songPoolId;
        await LoadPersonalFmPageAsync(force: true);
    }

    private async Task HandlePersonalFmMouseAsync(MouseEvent mouse)
    {
        if (mouse.Kind is not (MouseKind.LeftDown or MouseKind.LeftDoubleClick))
        {
            return;
        }

        if (_fmHitRegions.ModeNormalRect.Contains(mouse.X, mouse.Y))
        {
            await SwitchPersonalFmModeAsync(TerminalPersonalFmMode.Normal);
            return;
        }

        if (_fmHitRegions.ModeSmallRect.Contains(mouse.X, mouse.Y))
        {
            await SwitchPersonalFmModeAsync(TerminalPersonalFmMode.Small);
            return;
        }

        if (_fmHitRegions.ModePeakRect.Contains(mouse.X, mouse.Y))
        {
            await SwitchPersonalFmModeAsync(TerminalPersonalFmMode.Peak);
            return;
        }

        if (_fmHitRegions.PoolTasteRect.Contains(mouse.X, mouse.Y))
        {
            await SwitchPersonalFmSongPoolAsync(TerminalPersonalFmSongPoolId.Taste);
            return;
        }

        if (_fmHitRegions.PoolStyleRect.Contains(mouse.X, mouse.Y))
        {
            await SwitchPersonalFmSongPoolAsync(TerminalPersonalFmSongPoolId.Style);
            return;
        }

        if (_personalFm.IsActive &&
            _fmHitRegions.ProgressBarWidth > 0 &&
            _fmHitRegions.ProgressBarRect.Contains(mouse.X, mouse.Y))
        {
            var ratio = (mouse.X - _fmHitRegions.ProgressBarStartX) / (double)Math.Max(1, _fmHitRegions.ProgressBarWidth - 1);
            _playback.SeekToRatio(ratio);
            return;
        }

        if (_fmHitRegions.PreviousButtonRect.Contains(mouse.X, mouse.Y))
        {
            await _playback.PlayPreviousAsync();
            return;
        }

        if (_fmHitRegions.DislikeButtonRect.Contains(mouse.X, mouse.Y))
        {
            await DislikePersonalFmAsync();
            return;
        }

        if (_fmHitRegions.PlayPauseButtonRect.Contains(mouse.X, mouse.Y))
        {
            await StartOrTogglePersonalFmAsync();
            return;
        }

        if (_fmHitRegions.NextButtonRect.Contains(mouse.X, mouse.Y))
        {
            if (_personalFm.IsActive)
            {
                await _playback.PlayNextAsync();
            }
            else
            {
                await StartOrTogglePersonalFmAsync();
            }

            return;
        }

        if (_fmHitRegions.FavoriteButtonRect.Contains(mouse.X, mouse.Y))
        {
            await ToggleFavoriteForCurrentOrSelectedAsync();
            return;
        }

        if (_fmHitRegions.RefreshButtonRect.Contains(mouse.X, mouse.Y))
        {
            await LoadPersonalFmPageAsync(force: true);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _input?.Dispose();
        _login.Dispose();
        _images.Dispose();
        _playback.Dispose();
        _personalFm.Dispose();
        _renderer.Dispose();
        await Task.CompletedTask;
    }
}
