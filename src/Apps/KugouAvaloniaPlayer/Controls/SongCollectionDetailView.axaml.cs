using System;
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using ZLinq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.ViewModels;
using SukiUI;

namespace KugouAvaloniaPlayer.Controls;

public partial class SongCollectionDetailView : UserControl
{
    private INotifyCollectionChanged? _songsCollectionNotifier;
    private PlayerViewModel? _subscribedPlayer;

    public static readonly StyledProperty<string?> CoverProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, string?>(nameof(Cover));

    public static readonly StyledProperty<bool> ShowCoverProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, bool>(nameof(ShowCover), true);

    public static readonly StyledProperty<bool> ShowSongListProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, bool>(nameof(ShowSongList), true);

    public static readonly StyledProperty<bool> ShowPlayAllButtonProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, bool>(nameof(ShowPlayAllButton), true);

    public static readonly StyledProperty<string> HeroBackgroundProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, string>(nameof(HeroBackground), string.Empty);

    public static readonly StyledProperty<string> LightHeroBackgroundProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, string>(
            nameof(LightHeroBackground),
            "avares://KugouAvaloniaPlayer/Assets/light.png");

    public static readonly StyledProperty<string> NightHeroBackgroundProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, string>(
            nameof(NightHeroBackground),
            "avares://KugouAvaloniaPlayer/Assets/night.png");

    public static readonly StyledProperty<string> CurrentHeroBackgroundProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, string>(
            nameof(CurrentHeroBackground),
            "avares://KugouAvaloniaPlayer/Assets/light.png");

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, string?>(nameof(Title));

    public static readonly StyledProperty<string?> SubtitleProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, string?>(nameof(Subtitle));

    public static readonly StyledProperty<bool> HasSubtitleProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, bool>(nameof(HasSubtitle));

    public static readonly StyledProperty<IEnumerable?> SongsProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, IEnumerable?>(nameof(Songs));

    public static readonly StyledProperty<ICommand?> LoadMoreCommandProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, ICommand?>(nameof(LoadMoreCommand));

    public static readonly StyledProperty<ICommand?> BackCommandProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, ICommand?>(nameof(BackCommand));

    public static readonly StyledProperty<bool> ShowBackButtonProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, bool>(nameof(ShowBackButton), true);

    public static readonly StyledProperty<ICommand?> PlayFirstCommandProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, ICommand?>(nameof(PlayFirstCommand));

    public static readonly StyledProperty<bool> HasPlayFirstCommandProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, bool>(nameof(HasPlayFirstCommand));

    public static readonly StyledProperty<bool> ShowAddLoadedSongsToQueueButtonProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, bool>(nameof(ShowAddLoadedSongsToQueueButton));

    public static readonly StyledProperty<bool> AllowAddToPlaylistActionProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, bool>(nameof(AllowAddToPlaylistAction), true);

    public static readonly StyledProperty<IEnumerable?> HeroDropdownItemsProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, IEnumerable?>(nameof(HeroDropdownItems));

    public static readonly StyledProperty<object?> HeroDropdownSelectedItemProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, object?>(nameof(HeroDropdownSelectedItem));

    public static readonly StyledProperty<string?> HeroDropdownLabelProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, string?>(nameof(HeroDropdownLabel));

    public static readonly StyledProperty<bool> HasHeroDropdownProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, bool>(nameof(HasHeroDropdown));

    public static readonly StyledProperty<bool> HasHeroDropdownLabelProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, bool>(nameof(HasHeroDropdownLabel));

    public static readonly StyledProperty<bool> IsLoadingMoreProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, bool>(nameof(IsLoadingMore));

    public static readonly StyledProperty<bool> IsLoadingProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, bool>(nameof(IsLoading));

    public static readonly StyledProperty<string> LoadingTextProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, string>(nameof(LoadingText), "正在加载...");

    public static readonly StyledProperty<string> LoadingMoreTextProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, string>(nameof(LoadingMoreText), "正在加载歌曲...");

    public static readonly StyledProperty<ICommand?> HeroActionCommandProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, ICommand?>(nameof(HeroActionCommand));

    public static readonly StyledProperty<string?> HeroActionTextProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, string?>(nameof(HeroActionText));

    public static readonly StyledProperty<string?> HeroActionSvgPathProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, string?>(nameof(HeroActionSvgPath));

    public static readonly StyledProperty<Geometry?> HeroActionIconDataProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, Geometry?>(nameof(HeroActionIconData));

    public static readonly StyledProperty<bool> HeroActionIsVisibleProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, bool>(nameof(HeroActionIsVisible), true);

    public static readonly StyledProperty<bool> HasHeroActionProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, bool>(nameof(HasHeroAction));

    public static readonly StyledProperty<bool> HasHeroActionTextProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, bool>(nameof(HasHeroActionText));

    public static readonly StyledProperty<bool> HasHeroActionSvgPathProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, bool>(nameof(HasHeroActionSvgPath));

    public static readonly StyledProperty<bool> HasHeroActionIconDataProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, bool>(nameof(HasHeroActionIconData));

    public static readonly StyledProperty<Thickness> HeaderMarginProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, Thickness>(nameof(HeaderMargin), new Thickness(0));

    public static readonly StyledProperty<Thickness> ListMarginProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, Thickness>(nameof(ListMargin), new Thickness(0, 15, 0, 0));

    public static readonly StyledProperty<Thickness> ListPaddingProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, Thickness>(nameof(ListPadding), new Thickness(0, 0, 0, 80));

    public static readonly StyledProperty<double> TitleFontSizeProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, double>(nameof(TitleFontSize), 28);

    public static readonly StyledProperty<double> TitleMaxWidthProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, double>(nameof(TitleMaxWidth), 560);

    public SongCollectionDetailView()
    {
        PlayFirstSongCommand = new RelayCommand(PlayFirstSong);
        OpenBatchActionsCommand = new RelayCommand(OpenBatchActions);
        ScrollToPlayingSongCommand = new RelayCommand(ScrollToPlayingSong);
        InitializeComponent();
        UpdateCurrentHeroBackground();
    }

    public ICommand PlayFirstSongCommand { get; }
    public ICommand OpenBatchActionsCommand { get; }
    public ICommand ScrollToPlayingSongCommand { get; }

    public string? Cover
    {
        get => GetValue(CoverProperty);
        set => SetValue(CoverProperty, value);
    }

    public bool ShowCover
    {
        get => GetValue(ShowCoverProperty);
        set => SetValue(ShowCoverProperty, value);
    }

    public bool ShowSongList
    {
        get => GetValue(ShowSongListProperty);
        set => SetValue(ShowSongListProperty, value);
    }

    public bool ShowPlayAllButton
    {
        get => GetValue(ShowPlayAllButtonProperty);
        set => SetValue(ShowPlayAllButtonProperty, value);
    }

    public string HeroBackground
    {
        get => GetValue(HeroBackgroundProperty);
        set => SetValue(HeroBackgroundProperty, value);
    }

    public string LightHeroBackground
    {
        get => GetValue(LightHeroBackgroundProperty);
        set => SetValue(LightHeroBackgroundProperty, value);
    }

    public string NightHeroBackground
    {
        get => GetValue(NightHeroBackgroundProperty);
        set => SetValue(NightHeroBackgroundProperty, value);
    }

    public string CurrentHeroBackground
    {
        get => GetValue(CurrentHeroBackgroundProperty);
        private set => SetValue(CurrentHeroBackgroundProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Subtitle
    {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public bool HasSubtitle
    {
        get => GetValue(HasSubtitleProperty);
        private set => SetValue(HasSubtitleProperty, value);
    }

    public IEnumerable? Songs
    {
        get => GetValue(SongsProperty);
        set => SetValue(SongsProperty, value);
    }

    public ICommand? LoadMoreCommand
    {
        get => GetValue(LoadMoreCommandProperty);
        set => SetValue(LoadMoreCommandProperty, value);
    }

    public ICommand? BackCommand
    {
        get => GetValue(BackCommandProperty);
        set => SetValue(BackCommandProperty, value);
    }

    public bool ShowBackButton
    {
        get => GetValue(ShowBackButtonProperty);
        set => SetValue(ShowBackButtonProperty, value);
    }

    public ICommand? PlayFirstCommand
    {
        get => GetValue(PlayFirstCommandProperty);
        set => SetValue(PlayFirstCommandProperty, value);
    }

    public bool HasPlayFirstCommand
    {
        get => GetValue(HasPlayFirstCommandProperty);
        private set => SetValue(HasPlayFirstCommandProperty, value);
    }

    public bool ShowAddLoadedSongsToQueueButton
    {
        get => GetValue(ShowAddLoadedSongsToQueueButtonProperty);
        set => SetValue(ShowAddLoadedSongsToQueueButtonProperty, value);
    }

    public bool AllowAddToPlaylistAction
    {
        get => GetValue(AllowAddToPlaylistActionProperty);
        set => SetValue(AllowAddToPlaylistActionProperty, value);
    }

    public IEnumerable? HeroDropdownItems
    {
        get => GetValue(HeroDropdownItemsProperty);
        set => SetValue(HeroDropdownItemsProperty, value);
    }

    public object? HeroDropdownSelectedItem
    {
        get => GetValue(HeroDropdownSelectedItemProperty);
        set => SetValue(HeroDropdownSelectedItemProperty, value);
    }

    public string? HeroDropdownLabel
    {
        get => GetValue(HeroDropdownLabelProperty);
        set => SetValue(HeroDropdownLabelProperty, value);
    }

    public bool HasHeroDropdown
    {
        get => GetValue(HasHeroDropdownProperty);
        private set => SetValue(HasHeroDropdownProperty, value);
    }

    public bool HasHeroDropdownLabel
    {
        get => GetValue(HasHeroDropdownLabelProperty);
        private set => SetValue(HasHeroDropdownLabelProperty, value);
    }

    public bool IsLoadingMore
    {
        get => GetValue(IsLoadingMoreProperty);
        set => SetValue(IsLoadingMoreProperty, value);
    }

    public bool IsLoading
    {
        get => GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    public string LoadingText
    {
        get => GetValue(LoadingTextProperty);
        set => SetValue(LoadingTextProperty, value);
    }

    public string LoadingMoreText
    {
        get => GetValue(LoadingMoreTextProperty);
        set => SetValue(LoadingMoreTextProperty, value);
    }

    public ICommand? HeroActionCommand
    {
        get => GetValue(HeroActionCommandProperty);
        set => SetValue(HeroActionCommandProperty, value);
    }

    public string? HeroActionText
    {
        get => GetValue(HeroActionTextProperty);
        set => SetValue(HeroActionTextProperty, value);
    }

    public string? HeroActionSvgPath
    {
        get => GetValue(HeroActionSvgPathProperty);
        set => SetValue(HeroActionSvgPathProperty, value);
    }

    public Geometry? HeroActionIconData
    {
        get => GetValue(HeroActionIconDataProperty);
        set => SetValue(HeroActionIconDataProperty, value);
    }

    public bool HeroActionIsVisible
    {
        get => GetValue(HeroActionIsVisibleProperty);
        set => SetValue(HeroActionIsVisibleProperty, value);
    }

    public bool HasHeroAction
    {
        get => GetValue(HasHeroActionProperty);
        private set => SetValue(HasHeroActionProperty, value);
    }

    public bool HasHeroActionText
    {
        get => GetValue(HasHeroActionTextProperty);
        private set => SetValue(HasHeroActionTextProperty, value);
    }

    public bool HasHeroActionSvgPath
    {
        get => GetValue(HasHeroActionSvgPathProperty);
        private set => SetValue(HasHeroActionSvgPathProperty, value);
    }

    public bool HasHeroActionIconData
    {
        get => GetValue(HasHeroActionIconDataProperty);
        private set => SetValue(HasHeroActionIconDataProperty, value);
    }

    public Thickness HeaderMargin
    {
        get => GetValue(HeaderMarginProperty);
        set => SetValue(HeaderMarginProperty, value);
    }

    public Thickness ListMargin
    {
        get => GetValue(ListMarginProperty);
        set => SetValue(ListMarginProperty, value);
    }

    public Thickness ListPadding
    {
        get => GetValue(ListPaddingProperty);
        set => SetValue(ListPaddingProperty, value);
    }

    public double TitleFontSize
    {
        get => GetValue(TitleFontSizeProperty);
        set => SetValue(TitleFontSizeProperty, value);
    }

    public double TitleMaxWidth
    {
        get => GetValue(TitleMaxWidthProperty);
        set => SetValue(TitleMaxWidthProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SubtitleProperty)
            HasSubtitle = !string.IsNullOrWhiteSpace(change.NewValue as string);

        if (change.Property == HeroActionCommandProperty ||
            change.Property == HeroActionTextProperty ||
            change.Property == HeroActionSvgPathProperty ||
            change.Property == HeroActionIconDataProperty ||
            change.Property == HeroActionIsVisibleProperty)
            UpdateHeroActionState();

        if (change.Property == PlayFirstCommandProperty)
            HasPlayFirstCommand = change.NewValue is not null;

        if (change.Property == HeroDropdownItemsProperty ||
            change.Property == HeroDropdownLabelProperty)
            UpdateHeroDropdownState();

        if (change.Property == HeroBackgroundProperty ||
            change.Property == LightHeroBackgroundProperty ||
            change.Property == NightHeroBackgroundProperty)
            UpdateCurrentHeroBackground();

        if (change.Property == SongsProperty)
        {
            DetachSongsCollectionChanged(change.OldValue as IEnumerable);
            AttachSongsCollectionChanged(change.NewValue as IEnumerable);
            SyncPlayingState();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SukiTheme.GetInstance().PropertyChanged += OnSukiThemePropertyChanged;
        AttachSongsCollectionChanged(Songs);
        AttachPlayerPropertyChanged();
        UpdateCurrentHeroBackground();
        SyncPlayingState();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        SukiTheme.GetInstance().PropertyChanged -= OnSukiThemePropertyChanged;
        DetachSongsCollectionChanged(Songs);
        DetachPlayerPropertyChanged();
        base.OnDetachedFromVisualTree(e);
    }

    private void PlayFirstSong()
    {
        if (PlayFirstCommand?.CanExecute(null) == true)
        {
            PlayFirstCommand.Execute(null);
            return;
        }

        var firstSong = Songs?.AsValueEnumerable().OfType<SongItem>().FirstOrDefault();
        if (firstSong?.PlayCommand.CanExecute(null) == true)
            firstSong.PlayCommand.Execute(null);
    }

    private void OpenBatchActions()
    {
        var loadedSongs = Songs?.AsValueEnumerable().OfType<SongItem>().ToList() ?? [];
        WeakReferenceMessenger.Default.Send(new ShowSongBatchActionDialogMessage(loadedSongs, AllowAddToPlaylistAction));
    }

    private void ScrollToPlayingSong()
    {
        var currentSong = ResolveCurrentPlayingSong();
        var playingSong = Songs?
            .AsValueEnumerable()
            .OfType<SongItem>()
            .FirstOrDefault(song => IsSameSong(song, currentSong));

        if (playingSong is null)
        {
            playingSong = Songs?.AsValueEnumerable().OfType<SongItem>().FirstOrDefault(song => song.IsPlaying);
        }

        if (playingSong is null)
            return;

        SongList.ScrollIntoView(playingSong);
        Dispatcher.UIThread.Post(() => AdjustScrollPosition(playingSong), DispatcherPriority.Background);
    }

    private SongItem? ResolveCurrentPlayingSong()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        return (topLevel?.DataContext as MainWindowViewModel)?.Player.CurrentPlayingSong;
    }

    private void AttachSongsCollectionChanged(IEnumerable? songs)
    {
        if (ReferenceEquals(_songsCollectionNotifier, songs))
            return;

        DetachSongsCollectionChanged(_songsCollectionNotifier as IEnumerable);
        _songsCollectionNotifier = songs as INotifyCollectionChanged;
        if (_songsCollectionNotifier != null)
            _songsCollectionNotifier.CollectionChanged += OnSongsCollectionChanged;
    }

    private void DetachSongsCollectionChanged(IEnumerable? songs)
    {
        var notifier = songs as INotifyCollectionChanged;
        if (notifier != null)
            notifier.CollectionChanged -= OnSongsCollectionChanged;

        if (ReferenceEquals(_songsCollectionNotifier, notifier))
            _songsCollectionNotifier = null;
    }

    private void AttachPlayerPropertyChanged()
    {
        var player = (TopLevel.GetTopLevel(this)?.DataContext as MainWindowViewModel)?.Player;
        if (ReferenceEquals(_subscribedPlayer, player))
            return;

        DetachPlayerPropertyChanged();
        _subscribedPlayer = player;
        if (_subscribedPlayer != null)
            _subscribedPlayer.PropertyChanged += OnPlayerPropertyChanged;
    }

    private void DetachPlayerPropertyChanged()
    {
        if (_subscribedPlayer != null)
            _subscribedPlayer.PropertyChanged -= OnPlayerPropertyChanged;

        _subscribedPlayer = null;
    }

    private void OnSongsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SyncPlayingState();
    }

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PlayerViewModel.CurrentPlayingSong))
            return;

        SyncPlayingState();
    }

    private void SyncPlayingState()
    {
        void Update()
        {
            var currentSong = ResolveCurrentPlayingSong();
            var songs = Songs?.AsValueEnumerable().OfType<SongItem>().ToList();
            if (songs == null || songs.Count == 0)
                return;

            foreach (var song in songs)
                song.IsPlaying = IsSameSong(song, currentSong);
        }

        if (Dispatcher.UIThread.CheckAccess())
            Update();
        else
            Dispatcher.UIThread.Post(Update);
    }

    private static bool IsSameSong(SongItem candidate, SongItem? currentSong)
    {
        if (currentSong is null)
            return false;

        if (ReferenceEquals(candidate, currentSong))
            return true;

        return !string.IsNullOrWhiteSpace(candidate.Hash) &&
               string.Equals(candidate.Hash, currentSong.Hash, StringComparison.OrdinalIgnoreCase);
    }

    private void AdjustScrollPosition(SongItem playingSong)
    {
        var scrollViewer = SongList.GetVisualDescendants().AsValueEnumerable().OfType<ScrollViewer>().FirstOrDefault();
        if (scrollViewer is null)
            return;

        var target = SongList.GetVisualDescendants()
            .AsValueEnumerable()
            .OfType<Control>()
            .FirstOrDefault(control => ReferenceEquals(control.DataContext, playingSong));
        if (target is null)
            return;

        var targetTopLeft = target.TranslatePoint(new Point(0, 0), scrollViewer);
        if (targetTopLeft is null)
            return;

        var currentOffset = scrollViewer.Offset;
        var desiredY = currentOffset.Y + targetTopLeft.Value.Y - (scrollViewer.Viewport.Height * 0.2);
        var maxOffsetY = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        var clampedY = Math.Clamp(desiredY, 0, maxOffsetY);

        scrollViewer.Offset = new Vector(currentOffset.X, clampedY);
    }

    private void UpdateHeroActionState()
    {
        HasHeroActionText = !string.IsNullOrWhiteSpace(HeroActionText);
        HasHeroActionSvgPath = !string.IsNullOrWhiteSpace(HeroActionSvgPath);
        HasHeroActionIconData = HeroActionIconData is not null;
        HasHeroAction = HeroActionIsVisible &&
                        (HeroActionCommand is not null ||
                         HasHeroActionText ||
                         HasHeroActionSvgPath ||
                         HasHeroActionIconData);
    }

    private void UpdateHeroDropdownState()
    {
        HasHeroDropdown = HeroDropdownItems is not null;
        HasHeroDropdownLabel = !string.IsNullOrWhiteSpace(HeroDropdownLabel);
    }

    private void OnSukiThemePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        UpdateCurrentHeroBackground();
    }

    private void UpdateCurrentHeroBackground()
    {
        if (!string.IsNullOrWhiteSpace(HeroBackground))
        {
            CurrentHeroBackground = HeroBackground;
            return;
        }

        CurrentHeroBackground = SukiTheme.GetInstance().ActiveBaseTheme == ThemeVariant.Dark
            ? NightHeroBackground
            : LightHeroBackground;
    }
}
