using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AvaloniaLyrics;

public sealed class VerticalKaraokeTextBlock : Control
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<VerticalKaraokeTextBlock, string?>(nameof(Text));

    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<VerticalKaraokeTextBlock, double>(nameof(Progress));

    public static readonly StyledProperty<IBrush> ForegroundProperty =
        AvaloniaProperty.Register<VerticalKaraokeTextBlock, IBrush>(nameof(Foreground), Brushes.White);

    public static readonly StyledProperty<IBrush> PlayedForegroundProperty =
        AvaloniaProperty.Register<VerticalKaraokeTextBlock, IBrush>(nameof(PlayedForeground), Brushes.White);

    public static readonly StyledProperty<double> UnplayedOpacityProperty =
        AvaloniaProperty.Register<VerticalKaraokeTextBlock, double>(nameof(UnplayedOpacity), 0.34d);

    public static readonly StyledProperty<double> PlayedOpacityProperty =
        AvaloniaProperty.Register<VerticalKaraokeTextBlock, double>(nameof(PlayedOpacity), 1d);

    public static readonly StyledProperty<bool> UsePlayedGradientProperty =
        AvaloniaProperty.Register<VerticalKaraokeTextBlock, bool>(nameof(UsePlayedGradient), true);

    public static readonly StyledProperty<double> FontSizeProperty =
        TextBlock.FontSizeProperty.AddOwner<VerticalKaraokeTextBlock>();

    public static readonly StyledProperty<FontFamily?> FontFamilyProperty =
        AvaloniaProperty.Register<VerticalKaraokeTextBlock, FontFamily?>(nameof(FontFamily));

    public static readonly StyledProperty<FontWeight> FontWeightProperty =
        TextBlock.FontWeightProperty.AddOwner<VerticalKaraokeTextBlock>();

    public static readonly StyledProperty<FontStyle> FontStyleProperty =
        TextBlock.FontStyleProperty.AddOwner<VerticalKaraokeTextBlock>();

    public static readonly StyledProperty<double> ColumnSpacingProperty =
        AvaloniaProperty.Register<VerticalKaraokeTextBlock, double>(nameof(ColumnSpacing), 8d);

    private readonly List<GlyphLayout> _glyphs = [];
    private string? _layoutText;
    private FontFamily? _layoutFontFamily;
    private double _layoutFontSize;
    private FontWeight _layoutFontWeight;
    private FontStyle _layoutFontStyle;
    private double _layoutHeight = double.NaN;
    private double _cellWidth;
    private double _cellHeight;
    private int _rowsPerColumn = 1;
    private IBrush? _playedBrush;
    private IBrush? _unplayedBrush;

    static VerticalKaraokeTextBlock()
    {
        AffectsMeasure<VerticalKaraokeTextBlock>(
            TextProperty,
            FontSizeProperty,
            FontFamilyProperty,
            FontWeightProperty,
            FontStyleProperty,
            ColumnSpacingProperty);

        AffectsRender<VerticalKaraokeTextBlock>(
            TextProperty,
            ProgressProperty,
            ForegroundProperty,
            PlayedForegroundProperty,
            UnplayedOpacityProperty,
            PlayedOpacityProperty,
            UsePlayedGradientProperty,
            FontSizeProperty,
            FontFamilyProperty,
            FontWeightProperty,
            FontStyleProperty,
            ColumnSpacingProperty);
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

    public bool UsePlayedGradient
    {
        get => GetValue(UsePlayedGradientProperty);
        set => SetValue(UsePlayedGradientProperty, value);
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

    public double ColumnSpacing
    {
        get => GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ProgressProperty)
            return;

        if (change.Property == PlayedForegroundProperty || change.Property == PlayedOpacityProperty ||
            change.Property == UsePlayedGradientProperty)
        {
            _playedBrush = null;
            return;
        }

        if (change.Property == ForegroundProperty || change.Property == UnplayedOpacityProperty)
        {
            _unplayedBrush = null;
            return;
        }

        InvalidateLayoutCache();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureLayout(availableSize.Height);
        if (_glyphs.Count == 0)
            return default;

        var columns = (int)Math.Ceiling(_glyphs.Count / (double)_rowsPerColumn);
        var rows = Math.Min(_glyphs.Count, _rowsPerColumn);
        return new Size(
            Math.Ceiling(columns * _cellWidth + Math.Max(0, columns - 1) * ColumnSpacing),
            Math.Ceiling(rows * _cellHeight));
    }

    public override void Render(DrawingContext context)
    {
        EnsureLayout(Bounds.Height);
        if (_glyphs.Count == 0)
            return;

        var progressUnits = Math.Clamp(Progress, 0d, 1d) * _glyphs.Count;
        var unplayedBrush = GetUnplayedBrush();
        var playedBrush = GetPlayedBrush();

        for (var index = 0; index < _glyphs.Count; index++)
        {
            var glyph = _glyphs[index];
            var column = index / _rowsPerColumn;
            var row = index % _rowsPerColumn;
            var x = column * (_cellWidth + ColumnSpacing) + Math.Max(0, (_cellWidth - glyph.Width) / 2d);
            var y = row * _cellHeight + Math.Max(0, (_cellHeight - glyph.Height) / 2d);
            var origin = new Point(x, y);

            glyph.Text.SetForegroundBrush(unplayedBrush);
            context.DrawText(glyph.Text, origin);

            var glyphProgress = Math.Clamp(progressUnits - index, 0d, 1d);
            if (glyphProgress <= 0d)
                continue;

            glyph.Text.SetForegroundBrush(playedBrush);
            using (context.PushClip(new Rect(
                       column * (_cellWidth + ColumnSpacing),
                       row * _cellHeight,
                       _cellWidth,
                       _cellHeight * glyphProgress)))
            {
                context.DrawText(glyph.Text, origin);
            }
        }
    }

    private void EnsureLayout(double availableHeight)
    {
        var normalizedHeight = double.IsFinite(availableHeight) && availableHeight > 0
            ? availableHeight
            : double.PositiveInfinity;
        if (_layoutText == Text && Equals(_layoutFontFamily, FontFamily) &&
            Math.Abs(_layoutFontSize - FontSize) < 0.01d &&
            _layoutFontWeight == FontWeight && _layoutFontStyle == FontStyle &&
            (double.IsPositiveInfinity(normalizedHeight) && double.IsPositiveInfinity(_layoutHeight) ||
             Math.Abs(_layoutHeight - normalizedHeight) < 0.1d))
        {
            return;
        }

        _glyphs.Clear();
        _cellWidth = Math.Max(1d, FontSize);
        _cellHeight = Math.Max(1d, FontSize * 1.16d);

        var enumerator = StringInfo.GetTextElementEnumerator(Text ?? string.Empty);
        var typeface = new Typeface(
            FontFamily ?? FontFamily.Default,
            FontStyle,
            FontWeight,
            FontStretch.Normal);

        while (enumerator.MoveNext())
        {
            var element = enumerator.GetTextElement();
            if (element == "\r")
                continue;

            var formatted = new FormattedText(
                element == "\n" ? " " : element,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                typeface,
                FontSize,
                GetUnplayedBrush());
            var width = Math.Ceiling(formatted.WidthIncludingTrailingWhitespace);
            var height = Math.Ceiling(formatted.Height);
            _cellWidth = Math.Max(_cellWidth, width);
            _cellHeight = Math.Max(_cellHeight, height);
            _glyphs.Add(new GlyphLayout(formatted, width, height));
        }

        _rowsPerColumn = double.IsPositiveInfinity(normalizedHeight)
            ? Math.Max(1, _glyphs.Count)
            : Math.Max(1, (int)Math.Floor(normalizedHeight / _cellHeight));
        _layoutText = Text;
        _layoutFontFamily = FontFamily;
        _layoutFontSize = FontSize;
        _layoutFontWeight = FontWeight;
        _layoutFontStyle = FontStyle;
        _layoutHeight = normalizedHeight;
    }

    private void InvalidateLayoutCache()
    {
        _layoutText = null;
        _layoutHeight = double.NaN;
        _glyphs.Clear();
        _playedBrush = null;
        _unplayedBrush = null;
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
        var leading = Color.FromArgb((byte)Math.Round(color.A * 0.96), color.R, color.G, color.B);
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(leading, 0),
                new GradientStop(color, 0.18),
                new GradientStop(color, 1)
            }
        };
    }

    private static IBrush CreateOpacityBrush(IBrush brush, double opacity)
    {
        opacity = Math.Clamp(opacity, 0d, 1d);
        if (brush is ISolidColorBrush solid)
            return new SolidColorBrush(solid.Color, solid.Opacity * opacity);

        return brush;
    }

    private sealed record GlyphLayout(FormattedText Text, double Width, double Height);
}
