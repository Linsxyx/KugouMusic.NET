using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AvaloniaLyrics;

public class KaraokeTextBlock : Control
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<KaraokeTextBlock, string?>(nameof(Text));

    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<KaraokeTextBlock, double>(nameof(Progress));

    public static readonly StyledProperty<IBrush> ForegroundProperty =
        AvaloniaProperty.Register<KaraokeTextBlock, IBrush>(nameof(Foreground), Brushes.White);

    public static readonly StyledProperty<IBrush> PlayedForegroundProperty =
        AvaloniaProperty.Register<KaraokeTextBlock, IBrush>(nameof(PlayedForeground), Brushes.White);

    public static readonly StyledProperty<double> UnplayedOpacityProperty =
        AvaloniaProperty.Register<KaraokeTextBlock, double>(nameof(UnplayedOpacity), 0.34d);

    public static readonly StyledProperty<double> PlayedOpacityProperty =
        AvaloniaProperty.Register<KaraokeTextBlock, double>(nameof(PlayedOpacity), 1d);

    public static readonly StyledProperty<double> FontSizeProperty =
        TextBlock.FontSizeProperty.AddOwner<KaraokeTextBlock>();

    public static readonly StyledProperty<FontFamily?> FontFamilyProperty =
        AvaloniaProperty.Register<KaraokeTextBlock, FontFamily?>(nameof(FontFamily));

    public static readonly StyledProperty<FontWeight> FontWeightProperty =
        TextBlock.FontWeightProperty.AddOwner<KaraokeTextBlock>();

    public static readonly StyledProperty<FontStyle> FontStyleProperty =
        TextBlock.FontStyleProperty.AddOwner<KaraokeTextBlock>();

    public static readonly StyledProperty<TextAlignment> TextAlignmentProperty =
        TextBlock.TextAlignmentProperty.AddOwner<KaraokeTextBlock>();

    public static readonly StyledProperty<TextWrapping> TextWrappingProperty =
        TextBlock.TextWrappingProperty.AddOwner<KaraokeTextBlock>();

    public static readonly StyledProperty<KaraokeClipMode> ClipModeProperty =
        AvaloniaProperty.Register<KaraokeTextBlock, KaraokeClipMode>(nameof(ClipMode), KaraokeClipMode.ByTextWidth);

    public static readonly StyledProperty<bool> UsePlayedGradientProperty =
        AvaloniaProperty.Register<KaraokeTextBlock, bool>(nameof(UsePlayedGradient), true);

    private FormattedText? _formattedText;
    private double _formattedTextMaxWidth = double.NaN;
    private IBrush? _playedBrush;
    private IBrush? _unplayedBrush;

    static KaraokeTextBlock()
    {
        AffectsMeasure<KaraokeTextBlock>(
            TextProperty,
            FontSizeProperty,
            FontFamilyProperty,
            FontWeightProperty,
            FontStyleProperty,
            TextAlignmentProperty,
            TextWrappingProperty);

        AffectsRender<KaraokeTextBlock>(
            TextProperty,
            ProgressProperty,
            ForegroundProperty,
            PlayedForegroundProperty,
            UnplayedOpacityProperty,
            PlayedOpacityProperty,
            FontSizeProperty,
            FontFamilyProperty,
            FontWeightProperty,
            FontStyleProperty,
            TextAlignmentProperty,
            TextWrappingProperty,
            ClipModeProperty,
            UsePlayedGradientProperty);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double Progress
    {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public IBrush Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public IBrush PlayedForeground
    {
        get => GetValue(PlayedForegroundProperty);
        set => SetValue(PlayedForegroundProperty, value);
    }

    public double UnplayedOpacity
    {
        get => GetValue(UnplayedOpacityProperty);
        set => SetValue(UnplayedOpacityProperty, value);
    }

    public double PlayedOpacity
    {
        get => GetValue(PlayedOpacityProperty);
        set => SetValue(PlayedOpacityProperty, value);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontFamily? FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public FontWeight FontWeight
    {
        get => GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public FontStyle FontStyle
    {
        get => GetValue(FontStyleProperty);
        set => SetValue(FontStyleProperty, value);
    }

    public TextAlignment TextAlignment
    {
        get => GetValue(TextAlignmentProperty);
        set => SetValue(TextAlignmentProperty, value);
    }

    public TextWrapping TextWrapping
    {
        get => GetValue(TextWrappingProperty);
        set => SetValue(TextWrappingProperty, value);
    }

    public KaraokeClipMode ClipMode
    {
        get => GetValue(ClipModeProperty);
        set => SetValue(ClipModeProperty, value);
    }

    public bool UsePlayedGradient
    {
        get => GetValue(UsePlayedGradientProperty);
        set => SetValue(UsePlayedGradientProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ProgressProperty)
            return;

        if (change.Property == PlayedForegroundProperty ||
            change.Property == PlayedOpacityProperty ||
            change.Property == UsePlayedGradientProperty)
        {
            _playedBrush = null;
            return;
        }

        _formattedText = null;
        _unplayedBrush = null;
        if (change.Property == ForegroundProperty || change.Property == UnplayedOpacityProperty)
            return;

        _playedBrush = null;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var formattedText = GetFormattedText(availableSize.Width);
        return new Size(
            Math.Ceiling(formattedText.WidthIncludingTrailingWhitespace),
            Math.Ceiling(formattedText.Height));
    }

    public override void Render(DrawingContext context)
    {
        var text = Text;
        if (string.IsNullOrEmpty(text))
            return;

        var formattedText = GetFormattedText(Bounds.Width);
        var origin = new Point(0, Math.Max(0, (Bounds.Height - formattedText.Height) / 2));

        var progress = Math.Clamp(Progress, 0d, 1d);
        if (progress <= 0d)
        {
            context.DrawText(formattedText, origin);
            return;
        }

        var clipWidth = ClipMode switch
        {
            KaraokeClipMode.ByControlWidth => Bounds.Width * progress,
            _ => formattedText.WidthIncludingTrailingWhitespace * progress
        };

        clipWidth = Math.Clamp(clipWidth, 0d, Bounds.Width);
        if (clipWidth <= 0d)
        {
            context.DrawText(formattedText, origin);
            return;
        }

        if (clipWidth < Bounds.Width)
        {
            using (context.PushClip(new Rect(clipWidth, 0, Bounds.Width - clipWidth, Bounds.Height)))
            {
                context.DrawText(formattedText, origin);
            }
        }

        formattedText.SetForegroundBrush(GetPlayedBrush());
        using (context.PushClip(new Rect(0, 0, clipWidth, Bounds.Height)))
        {
            context.DrawText(formattedText, origin);
        }

        formattedText.SetForegroundBrush(GetUnplayedBrush());
    }

    private FormattedText GetFormattedText(double maxWidth)
    {
        if (_formattedText != null && Math.Abs(_formattedTextMaxWidth - maxWidth) < 0.1d)
            return _formattedText;

        _formattedText = CreateFormattedText(maxWidth, GetUnplayedBrush());
        _formattedTextMaxWidth = maxWidth;
        return _formattedText;
    }

    private FormattedText CreateFormattedText(double maxWidth, IBrush foreground)
    {
        var typeface = new Typeface(
            FontFamily ?? FontFamily.Default,
            FontStyle,
            FontWeight,
            FontStretch.Normal);

        var formattedText = new FormattedText(
            Text ?? string.Empty,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            FontSize,
            foreground)
        {
            TextAlignment = TextAlignment
        };

        if (TextWrapping != TextWrapping.NoWrap && double.IsFinite(maxWidth) && maxWidth > 0)
            formattedText.MaxTextWidth = maxWidth;

        return formattedText;
    }

    private IBrush GetUnplayedBrush()
    {
        return _unplayedBrush ??= CreateOpacityBrush(Foreground, UnplayedOpacity);
    }

    private IBrush GetPlayedBrush()
    {
        return _playedBrush ??= CreatePlayedBrush();
    }

    private IBrush CreatePlayedBrush()
    {
        var playedBrush = CreateOpacityBrush(PlayedForeground, PlayedOpacity);
        if (!UsePlayedGradient || playedBrush is not ISolidColorBrush solid)
            return playedBrush;

        var color = solid.Color;
        var leading = Color.FromArgb((byte)Math.Round(color.A * 0.78), color.R, color.G, color.B);
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(leading, 0),
                new GradientStop(color, 0.35),
                new GradientStop(color, 1)
            }
        };
    }

    private static IBrush CreateOpacityBrush(IBrush brush, double opacity)
    {
        opacity = Math.Clamp(opacity, 0d, 1d);
        if (brush is ISolidColorBrush solidBrush)
        {
            var color = solidBrush.Color;
            return new SolidColorBrush(color, solidBrush.Opacity * opacity);
        }

        return brush;
    }
}
