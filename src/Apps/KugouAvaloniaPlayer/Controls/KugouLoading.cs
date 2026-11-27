using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace KugouAvaloniaPlayer.Controls;

/// <summary>
/// Lightweight Vela-accented circular loading spinner replacing SukiUI.Controls.Loading.
/// Stops ticking automatically when detached or hidden (zero idle CPU cost).
/// </summary>
public sealed class KugouLoading : Control
{
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<KugouLoading, IBrush?>(nameof(Foreground));

    private DispatcherTimer? _timer;
    private double _angle;

    static KugouLoading()
    {
        AffectsRender<KugouLoading>(ForegroundProperty);
        IsVisibleProperty.Changed.AddClassHandler<KugouLoading>((c, _) => c.SyncTimer());
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SyncTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        StopTimer();
    }

    private void SyncTimer()
    {
        if (IsVisible && VisualRoot is not null)
        {
            if (_timer is null)
            {
                _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) =>
                {
                    _angle = (_angle + 7.5) % 360.0;
                    InvalidateVisual();
                });
            }
            if (!_timer.IsEnabled)
                _timer.Start();
        }
        else
        {
            StopTimer();
        }
    }

    private void StopTimer()
    {
        _timer?.Stop();
    }

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 2)
            return;

        var stroke = Math.Clamp(size * 0.11, 1.8, 3.6);
        var radius = (size - stroke) * 0.5;
        if (radius <= 0.5)
            return;

        var center = new Point(Bounds.Width * 0.5, Bounds.Height * 0.5);
        var accent = Foreground
                     ?? (this.TryFindResource("VelaAccentBrush", ActualThemeVariant, out var res) && res is IBrush b
                         ? b
                         : Brushes.DodgerBlue);

        // Subtle track ring
        var trackBrush = this.TryFindResource("VelaBorderSubtleBrush", ActualThemeVariant, out var tb) && tb is IBrush track
            ? track
            : new SolidColorBrush(Color.FromArgb(40, 128, 128, 128));
        context.DrawEllipse(null, new Pen(trackBrush, stroke), center, radius, radius);

        // Animated 250-degree arc
        var startRad = _angle * Math.PI / 180.0;
        var sweepRad = 250.0 * Math.PI / 180.0;
        var endRad = startRad + sweepRad;

        var p0 = new Point(center.X + radius * Math.Cos(startRad), center.Y + radius * Math.Sin(startRad));
        var p1 = new Point(center.X + radius * Math.Cos(endRad), center.Y + radius * Math.Sin(endRad));

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(p0, false);
            ctx.ArcTo(p1, new Size(radius, radius), 0, isLargeArc: true, SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }

        context.DrawGeometry(null, new Pen(accent, stroke, lineCap: PenLineCap.Round), geo);
    }
}
