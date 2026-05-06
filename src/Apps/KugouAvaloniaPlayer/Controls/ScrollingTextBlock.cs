using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace KugouAvaloniaPlayer.Controls;

public class ScrollingTextBlock : Control
{
    private const double DefaultGap = 36;
    private const double DefaultSpeed = 32;

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<ScrollingTextBlock, string?>(nameof(Text));

    public static readonly StyledProperty<IBrush> ForegroundProperty =
        AvaloniaProperty.Register<ScrollingTextBlock, IBrush>(nameof(Foreground), Brushes.White);

    public static readonly StyledProperty<double> FontSizeProperty =
        TextBlock.FontSizeProperty.AddOwner<ScrollingTextBlock>();

    public static readonly StyledProperty<FontFamily?> FontFamilyProperty =
        AvaloniaProperty.Register<ScrollingTextBlock, FontFamily?>(nameof(FontFamily));

    public static readonly StyledProperty<FontWeight> FontWeightProperty =
        TextBlock.FontWeightProperty.AddOwner<ScrollingTextBlock>();

    public static readonly StyledProperty<FontStyle> FontStyleProperty =
        TextBlock.FontStyleProperty.AddOwner<ScrollingTextBlock>();

    private readonly DispatcherTimer _timer;
    private DateTime _lastTick;
    private double _offset;
    
    private FormattedText? _cachedFormattedText;
    private double _cachedTextWidth;
    private double _cachedTextHeight;

    static ScrollingTextBlock()
    {
        AffectsMeasure<ScrollingTextBlock>(
            TextProperty,
            FontSizeProperty,
            FontFamilyProperty,
            FontWeightProperty,
            FontStyleProperty);

        AffectsRender<ScrollingTextBlock>(
            TextProperty,
            ForegroundProperty,
            FontSizeProperty,
            FontFamilyProperty,
            FontWeightProperty,
            FontStyleProperty);
    }

    public ScrollingTextBlock()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += OnTick;
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public IBrush Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
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

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _lastTick = DateTime.UtcNow;
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer.Stop();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        
        if (change.Property == TextProperty ||
            change.Property == FontSizeProperty ||
            change.Property == FontFamilyProperty ||
            change.Property == FontWeightProperty ||
            change.Property == FontStyleProperty ||
            change.Property == ForegroundProperty)
        {
            _cachedFormattedText = null;
            _offset = 0;
        }
    }
    
    private FormattedText? GetOrUpdateFormattedText()
    {
        if (string.IsNullOrEmpty(Text))
            return null;

        if (_cachedFormattedText == null)
        {
            var typeface = new Typeface(
                FontFamily ?? FontFamily.Default,
                FontStyle,
                FontWeight,
                FontStretch.Normal);

            _cachedFormattedText = new FormattedText(
                Text,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                typeface,
                FontSize,
                Foreground);

            _cachedTextWidth = _cachedFormattedText.WidthIncludingTrailingWhitespace;
            _cachedTextHeight = _cachedFormattedText.Height;
        }

        return _cachedFormattedText;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var formattedText = GetOrUpdateFormattedText();
        if (formattedText == null)
            return new Size(0, 0);

        return new Size(
            Math.Min(Math.Ceiling(_cachedTextWidth), availableSize.Width),
            Math.Ceiling(_cachedTextHeight));
    }

    public override void Render(DrawingContext context)
    {
        var formattedText = GetOrUpdateFormattedText();
        if (formattedText == null)
            return;

        var originY = Math.Max(0, (Bounds.Height - _cachedTextHeight) / 2);

        using (context.PushClip(new Rect(Bounds.Size)))
        {
            if (_cachedTextWidth <= Bounds.Width || Bounds.Width <= 0)
            {
                context.DrawText(formattedText, new Point(0, originY));
                return;
            }

            var cycleWidth = _cachedTextWidth + DefaultGap;
            var x = -(_offset % cycleWidth);
            context.DrawText(formattedText, new Point(x, originY));
            context.DrawText(formattedText, new Point(x + cycleWidth, originY));
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var elapsedSeconds = Math.Min(0.1, (now - _lastTick).TotalSeconds);
        _lastTick = now;
        
        if (_cachedFormattedText != null && _cachedTextWidth > Bounds.Width && Bounds.Width > 0)
        {
            _offset += elapsedSeconds * DefaultSpeed;
            InvalidateVisual();
        }
        else if (_offset != 0)
        {
            _offset = 0;
            InvalidateVisual();
        }
    }
}