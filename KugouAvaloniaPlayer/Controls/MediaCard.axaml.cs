using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace KugouAvaloniaPlayer.Controls;

public partial class MediaCard : UserControl
{
    public static readonly StyledProperty<string> CoverProperty =
        AvaloniaProperty.Register<MediaCard, string>(nameof(Cover));

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<MediaCard, string>(nameof(Title));

    public static readonly StyledProperty<string?> SubtitleProperty =
        AvaloniaProperty.Register<MediaCard, string?>(nameof(Subtitle));

    public static readonly StyledProperty<bool> HasSubtitleProperty =
        AvaloniaProperty.Register<MediaCard, bool>(nameof(HasSubtitle));

    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<MediaCard, ICommand?>(nameof(Command));

    public static readonly StyledProperty<object?> CommandParameterProperty =
        AvaloniaProperty.Register<MediaCard, object?>(nameof(CommandParameter));

    public static readonly StyledProperty<object?> OverlayContentProperty =
        AvaloniaProperty.Register<MediaCard, object?>(nameof(OverlayContent));

    public new static readonly StyledProperty<ContextMenu?> ContextMenuProperty =
        AvaloniaProperty.Register<MediaCard, ContextMenu?>(nameof(ContextMenu));

    public MediaCard()
    {
        InitializeComponent();
    }

    public string Cover
    {
        get => GetValue(CoverProperty);
        set => SetValue(CoverProperty, value);
    }

    public string Title
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

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public object? OverlayContent
    {
        get => GetValue(OverlayContentProperty);
        set => SetValue(OverlayContentProperty, value);
    }

    public new ContextMenu? ContextMenu
    {
        get => GetValue(ContextMenuProperty);
        set => SetValue(ContextMenuProperty, value);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SubtitleProperty) HasSubtitle = !string.IsNullOrEmpty(change.NewValue as string);
    }
}