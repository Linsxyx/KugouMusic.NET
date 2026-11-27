using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace KugouAvaloniaPlayer.Controls;

/// <summary>
/// Lightweight Vela-accented circular loading spinner replacing SukiUI.Controls.Loading.
/// Driven by <see cref="TopLevel.RequestAnimationFrame"/> only while effectively visible and rendered,
/// ensuring zero idle CPU usage and zero redraws when hidden by an ancestor container or cached page.
/// </summary>
public sealed class KugouLoading : Control
{
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<KugouLoading, IBrush?>(nameof(Foreground));

    private double _angle;
    private bool _framePending;
    private TimeSpan? _lastFrameTs;

    private double _cachedRadius = -1;
    private StreamGeometry? _cachedArc;
    private IBrush? _cachedTrackBrush;
    private double _cachedTrackStroke = -1;
    private Pen? _cachedTrackPen;
    private IBrush? _cachedAccentBrush;
    private double _cachedAccentStroke = -1;
    private Pen? _cachedAccentPen;

    static KugouLoading()
    {
        AffectsRender<KugouLoading>(ForegroundProperty);
        IsVisibleProperty.Changed.AddClassHandler<KugouLoading>((c, _) => c.OnVisibilityMaybeChanged());
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (IsEffectivelyVisible)
            ScheduleNextFrame();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        StopAnimation();
    }

    private void OnVisibilityMaybeChanged()
    {
        if (IsEffectivelyVisible && VisualRoot is not null)
            ScheduleNextFrame();
        else
            StopAnimation();
    }

    private void StopAnimation()
    {
        _framePending = false;
        _lastFrameTs = null;
    }

    private void ScheduleNextFrame()
    {
        if (_framePending || !IsEffectivelyVisible)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        _framePending = true;
        topLevel.RequestAnimationFrame(OnAnimationFrame);
    }

    private void OnAnimationFrame(TimeSpan timestamp)
    {
        _framePending = false;

        if (VisualRoot is null || !IsEffectivelyVisible)
        {
            _lastFrameTs = null;
            return;
        }

        var dt = _lastFrameTs.HasValue
            ? Math.Clamp((timestamp - _lastFrameTs.Value).TotalSeconds, 0.001, 0.05)
            : 1.0 / 60.0;
        _lastFrameTs = timestamp;

        _angle = (_angle + dt * 360.0) % 360.0;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 2)
            return;

        // Only continue requesting frames when Avalonia actually renders this visible control.
        ScheduleNextFrame();

        var stroke = Math.Clamp(size * 0.11, 1.8, 3.6);
        var radius = (size - stroke) * 0.5;
        if (radius <= 0.5)
            return;

        var center = new Point(Bounds.Width * 0.5, Bounds.Height * 0.5);
        var accent = Foreground
                     ?? (this.TryFindResource("VelaAccentBrush", ActualThemeVariant, out var res) && res is IBrush b
                         ? b
                         : Brushes.DodgerBlue);

        var trackBrush = this.TryFindResource("VelaBorderSubtleBrush", ActualThemeVariant, out var tb) && tb is IBrush track
            ? track
            : FallbackTrackBrush;

        if (!ReferenceEquals(_cachedTrackBrush, trackBrush) || Math.Abs(_cachedTrackStroke - stroke) > 0.01 || _cachedTrackPen is null)
        {
            _cachedTrackBrush = trackBrush;
            _cachedTrackStroke = stroke;
            _cachedTrackPen = new Pen(trackBrush, stroke);
        }

        if (!ReferenceEquals(_cachedAccentBrush, accent) || Math.Abs(_cachedAccentStroke - stroke) > 0.01 || _cachedAccentPen is null)
        {
            _cachedAccentBrush = accent;
            _cachedAccentStroke = stroke;
            _cachedAccentPen = new Pen(accent, stroke, lineCap: PenLineCap.Round);
        }

        context.DrawEllipse(null, _cachedTrackPen, center, radius, radius);

        var arc = GetOrCreateArc(radius);
        var transform = Matrix.CreateRotation(_angle * Math.PI / 180.0) * Matrix.CreateTranslation(center.X, center.Y);
        using var _ = context.PushTransform(transform);
        context.DrawGeometry(null, _cachedAccentPen, arc);
    }

    private StreamGeometry GetOrCreateArc(double radius)
    {
        if (_cachedArc is not null && Math.Abs(_cachedRadius - radius) < 0.01)
            return _cachedArc;

        const double sweepRad = 250.0 * Math.PI / 180.0;
        var p0 = new Point(radius, 0);
        var p1 = new Point(radius * Math.Cos(sweepRad), radius * Math.Sin(sweepRad));

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(p0, false);
            ctx.ArcTo(p1, new Size(radius, radius), 0, isLargeArc: true, SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }

        _cachedRadius = radius;
        _cachedArc = geo;
        return geo;
    }

    private static readonly IBrush FallbackTrackBrush = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128));
}
