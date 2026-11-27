using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

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

    public static readonly StyledProperty<bool> IsScrollingEnabledProperty =
        AvaloniaProperty.Register<ScrollingTextBlock, bool>(nameof(IsScrollingEnabled), true);

    private TimeSpan? _lastFrameTime;
    private double _offset;
    private bool _isAttached;
    private bool _frameRequested;

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
            FontStyleProperty,
            IsScrollingEnabledProperty);
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

    public bool IsScrollingEnabled
    {
        get => GetValue(IsScrollingEnabledProperty);
        set => SetValue(IsScrollingEnabledProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        _lastFrameTime = null;
        InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _isAttached = false;
        _frameRequested = false;
        _lastFrameTime = null;
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (!IsScrollingEnabled && _offset != 0)
        {
            _offset = 0;
            _lastFrameTime = null;
        }
        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == BoundsProperty)
        {
            if (_isAttached)
                InvalidateVisual();
            return;
        }

        if (change.Property == IsScrollingEnabledProperty)
        {
            _lastFrameTime = null;
            if (!IsScrollingEnabled && !IsPointerOver && _offset != 0)
                _offset = 0;
            if (_isAttached)
                InvalidateVisual();
            return;
        }

        if (change.Property == TextProperty ||
            change.Property == FontSizeProperty ||
            change.Property == FontFamilyProperty ||
            change.Property == FontWeightProperty ||
            change.Property == FontStyleProperty ||
            change.Property == ForegroundProperty)
        {
            _cachedFormattedText = null;
            _offset = 0;
            _lastFrameTime = null;
            if (_isAttached)
                InvalidateVisual();
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
                _offset = 0;
                _lastFrameTime = null;
                context.DrawText(formattedText, new Point(0, originY));
                return;
            }

            if (!ShouldScroll())
            {
                _lastFrameTime = null;
                context.DrawText(formattedText, new Point(0, originY));
                return;
            }

            var cycleWidth = _cachedTextWidth + DefaultGap;
            var x = -(_offset % cycleWidth);
            context.DrawText(formattedText, new Point(x, originY));
            context.DrawText(formattedText, new Point(x + cycleWidth, originY));
        }

        RequestNextFrame();
    }

    private bool ShouldScroll()
    {
        if (!_isAttached || !IsEffectivelyVisible)
            return false;

        if (!IsScrollingEnabled && !IsPointerOver)
            return false;

        // Avoid ticking behind transparent overlays (e.g. MiniPlayer faded to Opacity=0 when NowPlaying is open).
        Visual? current = this;
        while (current is not null)
        {
            if (current.Opacity <= 0.001)
                return false;
            current = current.GetVisualParent();
        }

        return true;
    }

    private void RequestNextFrame()
    {
        if (_frameRequested || !_isAttached)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        _frameRequested = true;
        topLevel.RequestAnimationFrame(OnAnimationFrame);
    }

    private void OnAnimationFrame(TimeSpan frameTime)
    {
        _frameRequested = false;

        if (!_isAttached || GetOrUpdateFormattedText() == null || Bounds.Width <= 0 || _cachedTextWidth <= Bounds.Width)
        {
            _lastFrameTime = null;
            if (_offset != 0)
            {
                _offset = 0;
                InvalidateVisual();
            }
            return;
        }

        if (!ShouldScroll())
        {
            _lastFrameTime = null;
            if (_offset != 0)
            {
                _offset = 0;
                InvalidateVisual();
            }
            return;
        }

        var dt = _lastFrameTime.HasValue
            ? Math.Clamp((frameTime - _lastFrameTime.Value).TotalSeconds, 0.0, 0.1)
            : 0.0;
        _lastFrameTime = frameTime;

        _offset += dt * DefaultSpeed;
        InvalidateVisual();
    }
}
