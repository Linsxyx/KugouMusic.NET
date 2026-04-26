using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace KugouAvaloniaPlayer.Controls;

public class KaraokeGradientTextBlock : Control
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<KaraokeGradientTextBlock, string?>(nameof(Text));

    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<KaraokeGradientTextBlock, double>(nameof(Progress));

    public static readonly StyledProperty<IBrush> ForegroundProperty =
        AvaloniaProperty.Register<KaraokeGradientTextBlock, IBrush>(nameof(Foreground), Brushes.White);

    public static readonly StyledProperty<IBrush> PlayedForegroundProperty =
        AvaloniaProperty.Register<KaraokeGradientTextBlock, IBrush>(nameof(PlayedForeground), Brushes.White);

    public static readonly StyledProperty<double> UnplayedOpacityProperty =
        AvaloniaProperty.Register<KaraokeGradientTextBlock, double>(nameof(UnplayedOpacity), 0.34);

    public static readonly StyledProperty<double> PlayedOpacityProperty =
        AvaloniaProperty.Register<KaraokeGradientTextBlock, double>(nameof(PlayedOpacity), 1);

    public static readonly StyledProperty<double> FontSizeProperty =
        TextBlock.FontSizeProperty.AddOwner<KaraokeGradientTextBlock>();

    public static readonly StyledProperty<FontFamily?> FontFamilyProperty =
        AvaloniaProperty.Register<KaraokeGradientTextBlock, FontFamily?>(nameof(FontFamily));

    public static readonly StyledProperty<FontWeight> FontWeightProperty =
        TextBlock.FontWeightProperty.AddOwner<KaraokeGradientTextBlock>();

    public static readonly StyledProperty<FontStyle> FontStyleProperty =
        TextBlock.FontStyleProperty.AddOwner<KaraokeGradientTextBlock>();

    public static readonly StyledProperty<TextAlignment> TextAlignmentProperty =
        TextBlock.TextAlignmentProperty.AddOwner<KaraokeGradientTextBlock>();

    public static readonly StyledProperty<TextWrapping> TextWrappingProperty =
        TextBlock.TextWrappingProperty.AddOwner<KaraokeGradientTextBlock>();

    static KaraokeGradientTextBlock()
    {
        AffectsMeasure<KaraokeGradientTextBlock>(
            TextProperty,
            FontSizeProperty,
            FontFamilyProperty,
            FontWeightProperty,
            FontStyleProperty,
            TextAlignmentProperty,
            TextWrappingProperty);

        AffectsRender<KaraokeGradientTextBlock>(
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
            TextWrappingProperty);
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

    protected override Size MeasureOverride(Size availableSize)
    {
        var formattedText = CreateFormattedText(availableSize.Width, CreateOpacityBrush(Foreground, UnplayedOpacity));
        return new Size(
            Math.Ceiling(formattedText.WidthIncludingTrailingWhitespace),
            Math.Ceiling(formattedText.Height));
    }

    public override void Render(DrawingContext context)
    {
        var text = Text;
        if (string.IsNullOrEmpty(text))
            return;

        var unplayedBrush = CreateOpacityBrush(Foreground, UnplayedOpacity);
        var formattedText = CreateFormattedText(Bounds.Width, unplayedBrush);
        var origin = new Point(0, Math.Max(0, (Bounds.Height - formattedText.Height) / 2));
        context.DrawText(formattedText, origin);

        var progress = Math.Clamp(Progress, 0, 1);
        if (progress <= 0)
            return;

        var clipWidth = Math.Max(0, Bounds.Width * progress);
        if (clipWidth <= 0)
            return;

        formattedText.SetForegroundBrush(CreatePlayedBrush());
        using (context.PushClip(new Rect(0, 0, clipWidth, Bounds.Height)))
        {
            context.DrawText(formattedText, origin);
        }
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

    private IBrush CreatePlayedBrush()
    {
        var playedBrush = CreateOpacityBrush(PlayedForeground, PlayedOpacity);
        if (playedBrush is not ISolidColorBrush solid)
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
        opacity = Math.Clamp(opacity, 0, 1);
        if (brush is ISolidColorBrush solid)
        {
            var color = solid.Color;
            return new SolidColorBrush(Color.FromArgb(
                (byte)Math.Round(color.A * opacity),
                color.R,
                color.G,
                color.B));
        }

        return brush;
    }
}
