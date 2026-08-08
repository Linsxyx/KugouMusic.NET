using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace AvaloniaLyrics;

public sealed class VerticalFlowPanel : Panel
{
    public static readonly StyledProperty<HorizontalAlignment> HorizontalContentAlignmentProperty =
        AvaloniaProperty.Register<VerticalFlowPanel, HorizontalAlignment>(
            nameof(HorizontalContentAlignment),
            HorizontalAlignment.Left);

    public static readonly StyledProperty<double> ColumnSpacingProperty =
        AvaloniaProperty.Register<VerticalFlowPanel, double>(nameof(ColumnSpacing), 10d);

    public static readonly StyledProperty<double> ItemSpacingProperty =
        AvaloniaProperty.Register<VerticalFlowPanel, double>(nameof(ItemSpacing), 0d);

    static VerticalFlowPanel()
    {
        AffectsMeasure<VerticalFlowPanel>(HorizontalContentAlignmentProperty, ColumnSpacingProperty, ItemSpacingProperty);
        AffectsArrange<VerticalFlowPanel>(HorizontalContentAlignmentProperty, ColumnSpacingProperty, ItemSpacingProperty);
    }

    public HorizontalAlignment HorizontalContentAlignment
    {
        get => GetValue(HorizontalContentAlignmentProperty);
        set => SetValue(HorizontalContentAlignmentProperty, value);
    }

    public double ColumnSpacing
    {
        get => GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    public double ItemSpacing
    {
        get => GetValue(ItemSpacingProperty);
        set => SetValue(ItemSpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var heightLimit = double.IsFinite(availableSize.Height)
            ? Math.Max(1d, availableSize.Height)
            : double.PositiveInfinity;
        var columnHeight = 0d;
        var columnWidth = 0d;
        var desiredWidth = 0d;
        var desiredHeight = 0d;
        var columnCount = 0;

        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, heightLimit));
            var childSize = child.DesiredSize;
            var nextHeight = columnHeight <= 0d ? childSize.Height : columnHeight + ItemSpacing + childSize.Height;
            if (columnHeight > 0d && nextHeight > heightLimit)
            {
                desiredWidth += (columnCount > 0 ? ColumnSpacing : 0d) + columnWidth;
                desiredHeight = Math.Max(desiredHeight, columnHeight);
                columnCount++;
                columnHeight = 0d;
                columnWidth = 0d;
            }

            columnHeight = columnHeight <= 0d ? childSize.Height : columnHeight + ItemSpacing + childSize.Height;
            columnWidth = Math.Max(columnWidth, childSize.Width);
        }

        if (columnWidth > 0d || columnHeight > 0d)
        {
            desiredWidth += (columnCount > 0 ? ColumnSpacing : 0d) + columnWidth;
            desiredHeight = Math.Max(desiredHeight, columnHeight);
        }

        return new Size(Math.Ceiling(desiredWidth), Math.Ceiling(desiredHeight));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var desiredWidth = DesiredSize.Width;
        var x = HorizontalContentAlignment switch
        {
            HorizontalAlignment.Center => Math.Max(0d, (finalSize.Width - desiredWidth) / 2d),
            HorizontalAlignment.Right => Math.Max(0d, finalSize.Width - desiredWidth),
            _ => 0d
        };
        var y = 0d;
        var columnWidth = 0d;

        foreach (var child in Children)
        {
            var childSize = child.DesiredSize;
            var nextY = y <= 0d ? childSize.Height : y + ItemSpacing + childSize.Height;
            if (y > 0d && nextY > finalSize.Height)
            {
                x += columnWidth + ColumnSpacing;
                y = 0d;
                columnWidth = 0d;
            }

            if (y > 0d)
                y += ItemSpacing;
            child.Arrange(new Rect(x, y, childSize.Width, childSize.Height));
            y += childSize.Height;
            columnWidth = Math.Max(columnWidth, childSize.Width);
        }

        return finalSize;
    }
}
