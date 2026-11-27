using System;
using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace KugouAvaloniaPlayer.Controls;

/// <summary>
/// Zero-SVG-library vector icon renderer backed by <see cref="KugouIconRegistry"/>.
/// Renders precompiled <see cref="StreamGeometry"/> shapes directly via <see cref="DrawingContext"/>
/// with theme-aware <see cref="Foreground"/> tinting.
/// </summary>
public sealed class KugouIcon : Control
{
    private static readonly ConcurrentDictionary<string, Geometry?> GeometryCache = new(StringComparer.Ordinal);

    public static readonly StyledProperty<string?> PathProperty =
        AvaloniaProperty.Register<KugouIcon, string?>(nameof(Path));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<KugouIcon>();

    static KugouIcon()
    {
        AffectsRender<KugouIcon>(PathProperty, ForegroundProperty);
        AffectsMeasure<KugouIcon>(PathProperty);
    }

    public string? Path
    {
        get => GetValue(PathProperty);
        set => SetValue(PathProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var w = double.IsNaN(Width) ? 18 : Width;
        var h = double.IsNaN(Height) ? 18 : Height;
        return new Size(
            Math.Min(w, double.IsInfinity(availableSize.Width) ? w : availableSize.Width),
            Math.Min(h, double.IsInfinity(availableSize.Height) ? h : availableSize.Height));
    }

    public override void Render(DrawingContext context)
    {
        if (!KugouIconRegistry.TryGet(Path, out var def) || def is null)
            return;

        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0 || def.ViewBox.Width <= 0 || def.ViewBox.Height <= 0)
            return;

        var brush = Foreground ?? Brushes.CurrentColorFallback;
        var scale = Math.Min(bounds.Width / def.ViewBox.Width, bounds.Height / def.ViewBox.Height);
        var scaledW = def.ViewBox.Width * scale;
        var scaledH = def.ViewBox.Height * scale;
        var offsetX = (bounds.Width - scaledW) * 0.5 - def.ViewBox.X * scale;
        var offsetY = (bounds.Height - scaledH) * 0.5 - def.ViewBox.Y * scale;

        var viewTransform = Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offsetX, offsetY);
        using var _ = context.PushTransform(viewTransform);

        foreach (var shape in def.Shapes)
        {
            var geometry = GetGeometry(shape.PathData);
            if (geometry is null)
                continue;

            IDisposable? shapePush = null;
            if (shape.HasTransform)
            {
                var mat = new Matrix(shape.M11, shape.M12, shape.M21, shape.M22, shape.M31, shape.M32);
                shapePush = context.PushTransform(mat);
            }

            try
            {
                var fillBrush = shape.IsFill ? brush : null;
                IPen? pen = null;
                if (shape.IsStroke)
                {
                    var strokeWidth = shape.StrokeWidth > 0 ? shape.StrokeWidth : 1.5;
                    pen = new Pen(brush, strokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
                }

                context.DrawGeometry(fillBrush, pen, geometry);
            }
            finally
            {
                shapePush?.Dispose();
            }
        }
    }

    private static Geometry? GetGeometry(string pathData)
    {
        return GeometryCache.GetOrAdd(pathData, static data =>
        {
            try
            {
                return StreamGeometry.Parse(data);
            }
            catch
            {
                return null;
            }
        });
    }

    private static class Brushes
    {
        public static readonly IBrush CurrentColorFallback = new SolidColorBrush(Colors.Gray);
    }
}
