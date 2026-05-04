using System.Collections;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Controls;

public partial class SongCollectionDetailView : UserControl
{
    public static readonly StyledProperty<string?> CoverProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, string?>(nameof(Cover));

    public static readonly StyledProperty<string> HeroBackgroundProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, string>(
            nameof(HeroBackground),
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

    public static readonly StyledProperty<ICommand?> PlayFirstCommandProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, ICommand?>(nameof(PlayFirstCommand));

    public static readonly StyledProperty<bool> HasPlayFirstCommandProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, bool>(nameof(HasPlayFirstCommand));

    public static readonly StyledProperty<bool> IsLoadingMoreProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, bool>(nameof(IsLoadingMore));

    public static readonly StyledProperty<bool> IsLoadingProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, bool>(nameof(IsLoading));

    public static readonly StyledProperty<string> LoadingTextProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, string>(nameof(LoadingText), "正在加载...");

    public static readonly StyledProperty<string> LoadingMoreTextProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, string>(nameof(LoadingMoreText), "正在加载歌曲...");

    public static readonly StyledProperty<object?> ActionsProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, object?>(nameof(Actions));

    public static readonly StyledProperty<bool> HasActionsProperty =
        AvaloniaProperty.Register<SongCollectionDetailView, bool>(nameof(HasActions));

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
        InitializeComponent();
    }

    public ICommand PlayFirstSongCommand { get; }

    public string? Cover
    {
        get => GetValue(CoverProperty);
        set => SetValue(CoverProperty, value);
    }

    public string HeroBackground
    {
        get => GetValue(HeroBackgroundProperty);
        set => SetValue(HeroBackgroundProperty, value);
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

    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    public bool HasActions
    {
        get => GetValue(HasActionsProperty);
        private set => SetValue(HasActionsProperty, value);
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

        if (change.Property == ActionsProperty)
            HasActions = change.NewValue is not null;

        if (change.Property == PlayFirstCommandProperty)
            HasPlayFirstCommand = change.NewValue is not null;
    }

    private void PlayFirstSong()
    {
        if (PlayFirstCommand?.CanExecute(null) == true)
        {
            PlayFirstCommand.Execute(null);
            return;
        }

        var firstSong = Songs?.OfType<SongItem>().FirstOrDefault();
        if (firstSong?.PlayCommand.CanExecute(null) == true)
            firstSong.PlayCommand.Execute(null);
    }
}
