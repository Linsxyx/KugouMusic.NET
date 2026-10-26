using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace KugouAvaloniaPlayer.Controls;

/// <summary>A rounded settings group that draws hairline dividers between its visible rows.</summary>
public sealed class SettingsCard : StackPanel
{
    public static readonly StyledProperty<IBrush?> CardBackgroundProperty =
        AvaloniaProperty.Register<SettingsCard, IBrush?>(nameof(CardBackground));
    public static readonly StyledProperty<IBrush?> CardBorderBrushProperty =
        AvaloniaProperty.Register<SettingsCard, IBrush?>(nameof(CardBorderBrush));
    public static readonly StyledProperty<IBrush?> DividerBrushProperty =
        AvaloniaProperty.Register<SettingsCard, IBrush?>(nameof(DividerBrush));
    public static readonly StyledProperty<double> CornerRadiusProperty =
        AvaloniaProperty.Register<SettingsCard, double>(nameof(CornerRadius), 12);
    public static readonly StyledProperty<double> DividerInsetProperty =
        AvaloniaProperty.Register<SettingsCard, double>(nameof(DividerInset), 16);

    // Panel.Render is sealed, so the surface is painted by a private visual kept behind the rows.
    // It lives after the rows in VisualChildren so Panel's index-based child sync stays aligned.
    private readonly Chrome _chrome;

    public SettingsCard()
    {
        _chrome = new Chrome(this) { ZIndex = -1, IsHitTestVisible = false };
        VisualChildren.Add(_chrome);
    }

    public IBrush? CardBackground { get => GetValue(CardBackgroundProperty); set => SetValue(CardBackgroundProperty, value); }
    public IBrush? CardBorderBrush { get => GetValue(CardBorderBrushProperty); set => SetValue(CardBorderBrushProperty, value); }
    public IBrush? DividerBrush { get => GetValue(DividerBrushProperty); set => SetValue(DividerBrushProperty, value); }
    public double CornerRadius { get => GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    public double DividerInset { get => GetValue(DividerInsetProperty); set => SetValue(DividerInsetProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CardBackgroundProperty || change.Property == CardBorderBrushProperty ||
            change.Property == DividerBrushProperty || change.Property == CornerRadiusProperty ||
            change.Property == DividerInsetProperty)
            _chrome.InvalidateVisual();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        _chrome.Measure(size);
        _chrome.Arrange(new Rect(size));
        // Rows toggle visibility at runtime; dividers follow the arranged rows.
        _chrome.InvalidateVisual();
        return size;
    }

    private sealed class Chrome(SettingsCard owner) : Control
    {
        public override void Render(DrawingContext context)
        {
            var bounds = new Rect(Bounds.Size);
            var pen = owner.CardBorderBrush is { } border ? new Pen(border) : null;
            if (owner.CardBackground != null || pen != null)
                context.DrawRectangle(owner.CardBackground, pen, new RoundedRect(bounds.Deflate(0.5), owner.CornerRadius));
            if (owner.DividerBrush is not { } divider) return;
            var inset = owner.DividerInset;
            var first = true;
            foreach (var child in owner.Children)
            {
                if (!child.IsVisible) continue;
                if (!first)
                {
                    var y = child.Bounds.Top - child.Margin.Top - owner.Spacing / 2 - 0.5;
                    context.FillRectangle(divider, new Rect(inset, y, bounds.Width - inset * 2, 1));
                }
                first = false;
            }
        }
    }
}
