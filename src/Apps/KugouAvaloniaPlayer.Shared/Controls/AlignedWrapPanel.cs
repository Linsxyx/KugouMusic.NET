using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace KugouAvaloniaPlayer.Controls;

public class AlignedWrapPanel : Panel
{
    public static readonly StyledProperty<HorizontalAlignment> HorizontalContentAlignmentProperty =
        AvaloniaProperty.Register<AlignedWrapPanel, HorizontalAlignment>(
            nameof(HorizontalContentAlignment),
            HorizontalAlignment.Left);

    static AlignedWrapPanel()
    {
        AffectsMeasure<AlignedWrapPanel>(HorizontalContentAlignmentProperty);
        AffectsArrange<AlignedWrapPanel>(HorizontalContentAlignmentProperty);
    }

    public HorizontalAlignment HorizontalContentAlignment
    {
        get => GetValue(HorizontalContentAlignmentProperty);
        set => SetValue(HorizontalContentAlignmentProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var constraintWidth = double.IsFinite(availableSize.Width)
            ? availableSize.Width
            : double.PositiveInfinity;
        var lineWidth = 0d;
        var lineHeight = 0d;
        var desiredWidth = 0d;
        var desiredHeight = 0d;

        foreach (var child in Children)
        {
            child.Measure(new Size(constraintWidth, availableSize.Height));
            var childSize = child.DesiredSize;
            var shouldWrap = lineWidth > 0 &&
                             double.IsFinite(constraintWidth) &&
                             lineWidth + childSize.Width > constraintWidth;

            if (shouldWrap)
            {
                desiredWidth = Math.Max(desiredWidth, lineWidth);
                desiredHeight += lineHeight;
                lineWidth = 0;
                lineHeight = 0;
            }

            lineWidth += childSize.Width;
            lineHeight = Math.Max(lineHeight, childSize.Height);
        }

        desiredWidth = Math.Max(desiredWidth, lineWidth);
        desiredHeight += lineHeight;

        return new Size(
            double.IsFinite(availableSize.Width) ? availableSize.Width : desiredWidth,
            desiredHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var line = new LineLayout(Children.Count);
        var y = 0d;
        var lineWidth = 0d;
        var lineHeight = 0d;

        foreach (var child in Children)
        {
            var childSize = child.DesiredSize;
            var shouldWrap = line.Count > 0 && lineWidth + childSize.Width > finalSize.Width;

            if (shouldWrap)
            {
                ArrangeLine(line, finalSize.Width, y, lineWidth, lineHeight);
                y += lineHeight;
                line.Clear();
                lineWidth = 0;
                lineHeight = 0;
            }

            line.Add(child);
            lineWidth += childSize.Width;
            lineHeight = Math.Max(lineHeight, childSize.Height);
        }

        ArrangeLine(line, finalSize.Width, y, lineWidth, lineHeight);
        return finalSize;
    }

    private void ArrangeLine(LineLayout line, double finalWidth, double y, double lineWidth, double lineHeight)
    {
        if (line.Count == 0) return;

        var x = HorizontalContentAlignment switch
        {
            HorizontalAlignment.Center => Math.Max(0, (finalWidth - lineWidth) / 2),
            HorizontalAlignment.Right => Math.Max(0, finalWidth - lineWidth),
            _ => 0
        };

        for (var i = 0; i < line.Count; i++)
        {
            var child = line.Children[i];
            var childSize = child.DesiredSize;
            child.Arrange(new Rect(x, y, childSize.Width, lineHeight));
            x += childSize.Width;
        }
    }

    private sealed class LineLayout(int capacity)
    {
        public Control[] Children { get; } = new Control[Math.Max(1, capacity)];
        public int Count { get; private set; }

        public void Add(Control child)
        {
            Children[Count++] = child;
        }

        public void Clear()
        {
            Count = 0;
        }
    }
}
