using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace KugouAvaloniaPlayer.Controls;

/// <summary>Keep labels and editors aligned; stack editors below the label on narrow settings pages.</summary>
public sealed class SettingsRow : Grid
{
    private string? _wideColumns;
    private int[]? _widePositions;
    private bool? _compact;
    public SettingsRow()
    {
        ColumnSpacing = 16;
        RowSpacing = 10;
        Margin = new Thickness(0, 8);
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        if (_wideColumns == null && Children.Count > 0)
        {
            _wideColumns = ColumnDefinitions.ToString();
            _widePositions = Children.Select(GetColumn).ToArray();
        }
        var simple = Children.Count == 2 && (Children[1] is ToggleSwitch or Button or TextBlock);
        var compact = availableSize.Width < (simple ? 320 : 560);
        if (_wideColumns != null && _compact != compact)
        {
            _compact = compact;
            ColumnDefinitions = new ColumnDefinitions(compact
                ? string.Join(",", Enumerable.Range(0, Math.Max(1, Children.Count - 1)).Select(i => i == 0 ? "*" : "Auto"))
                : _wideColumns);
            RowDefinitions = new RowDefinitions(compact ? "Auto,Auto" : "Auto");
            for (var i = 0; i < Children.Count; i++)
            {
                SetRow(Children[i], compact && i > 0 ? 1 : 0);
                SetColumn(Children[i], compact ? Math.Max(0, i - 1) : _widePositions![i]);
                SetColumnSpan(Children[i], compact && i == 0 ? Math.Max(1, Children.Count - 1) : 1);
                Children[i].VerticalAlignment = VerticalAlignment.Center;
                if (i > 0) Children[i].HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            }
        }
        return base.MeasureOverride(availableSize);
    }
}
