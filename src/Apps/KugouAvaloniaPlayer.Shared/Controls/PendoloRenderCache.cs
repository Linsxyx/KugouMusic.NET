using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace KugouAvaloniaPlayer.Controls;

/// <summary>Retains only the text slots used by the current frame. Layout inputs keep their exact precision.</summary>
internal sealed class PendoloTextCache
{
    private sealed record Entry(string Text, Typeface Typeface, double FontSize, double Width, string Culture,
        FormattedText Layout, Color Color)
    {
        public Color LastColor = Color;
        public bool Used;
    }

    private readonly Dictionary<int, Entry> _entries = [];
    private readonly List<int> _unused = [];
    private string? _prefixText;
    private Typeface _prefixTypeface;
    private double _prefixFontSize;
    private string? _prefixCulture;
    private readonly Dictionary<int, double> _prefixWidths = [];

    public void BeginFrame()
    {
        foreach (var entry in _entries.Values) entry.Used = false;
    }

    public FormattedText Get(int slot, string text, Typeface typeface, double fontSize, double maxWidth, Color color)
    {
        var culture = CultureInfo.CurrentUICulture;
        if (!_entries.TryGetValue(slot, out var entry) || entry.Text != text || entry.Typeface != typeface ||
            entry.FontSize != fontSize || entry.Width != maxWidth || entry.Culture != culture.Name)
        {
            var layout = new FormattedText(text, culture, FlowDirection.LeftToRight, typeface, fontSize,
                new SolidColorBrush(color)) { MaxTextWidth = maxWidth, TextAlignment = TextAlignment.Left };
            _entries[slot] = entry = new Entry(text, typeface, fontSize, maxWidth, culture.Name, layout, color);
        }
        else if (entry.LastColor != color)
        {
            // Replace the brush so retained draw commands from a previous frame remain immutable.
            entry.Layout.SetForegroundBrush(new SolidColorBrush(color));
            entry.LastColor = color;
        }
        entry.Used = true;
        return entry.Layout;
    }

    public double PrefixWidth(string text, int characters, Typeface typeface, double fontSize)
    {
        var culture = CultureInfo.CurrentUICulture;
        if (_prefixText != text || _prefixTypeface != typeface || _prefixFontSize != fontSize || _prefixCulture != culture.Name)
        {
            _prefixWidths.Clear();
            _prefixText = text; _prefixTypeface = typeface; _prefixFontSize = fontSize; _prefixCulture = culture.Name;
        }
        if (characters == 0) return 0;
        if (_prefixWidths.TryGetValue(characters, out var width)) return width;
        // Preserve the original shaping of each complete prefix (kerning, ligatures, CJK and surrogate pairs).
        var prefix = new FormattedText(text[..characters], culture, FlowDirection.LeftToRight, typeface, fontSize, Brushes.White);
        _prefixWidths[characters] = width = prefix.WidthIncludingTrailingWhitespace;
        return width;
    }

    public void EndFrame()
    {
        _unused.Clear();
        foreach (var (slot, entry) in _entries)
            if (!entry.Used) _unused.Add(slot);
        foreach (var slot in _unused) _entries.Remove(slot);
    }

    public void Clear()
    {
        _entries.Clear(); _unused.Clear(); _prefixWidths.Clear(); _prefixText = null;
    }
}

internal sealed class PendoloGearCache
{
    private readonly Dictionary<(double Radius, int Teeth, double Depth), Point[]> _points = [];

    public Point[] Get(double radius, int teeth, double depth)
    {
        var key = (radius, teeth, depth);
        if (_points.TryGetValue(key, out var cached)) return cached;
        // A frame uses fewer than 16 sizes. Bound storage during interactive resizing too.
        if (_points.Count >= 16) _points.Clear();
        var points = new Point[teeth * 4];
        var inner = Math.Max(2, radius - depth);
        var step = Math.PI * 2 / teeth;
        for (var index = 0; index < teeth; index++)
        {
            var angle = index * Math.PI * 2 / teeth;
            points[index * 4] = PointAt(inner, angle - step * 0.22);
            points[index * 4 + 1] = PointAt(radius, angle - step * 0.12);
            points[index * 4 + 2] = PointAt(radius, angle + step * 0.12);
            points[index * 4 + 3] = PointAt(inner, angle + step * 0.22);
        }
        _points.Add(key, points);
        return points;
    }

    public void Clear() => _points.Clear();
    private static Point PointAt(double radius, double angle) => new(Math.Cos(angle) * radius, Math.Sin(angle) * radius);
}
