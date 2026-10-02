using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Controls;

public sealed class EqualizerCurveControl : Control
{
    public static readonly StyledProperty<IBrush> AccentProperty = AvaloniaProperty.Register<EqualizerCurveControl, IBrush>(nameof(Accent), Brushes.DodgerBlue);
    public static readonly StyledProperty<IBrush> GridBrushProperty = AvaloniaProperty.Register<EqualizerCurveControl, IBrush>(nameof(GridBrush), Brushes.Gray);
    public static readonly StyledProperty<IBrush> TextBrushProperty = AvaloniaProperty.Register<EqualizerCurveControl, IBrush>(nameof(TextBrush), Brushes.Gray);
    public IBrush Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public IBrush GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public IBrush TextBrush { get => GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    private EqSettingsViewModel? _model;
    private Point? _previous;
    private Point? _guide;
    private IPointer? _pointer;
    private Window? _window;
    private Rect Plot => new(38, 20, Math.Max(1, Bounds.Width - 60), Math.Max(1, Bounds.Height - 55));
    static EqualizerCurveControl() => AffectsRender<EqualizerCurveControl>(AccentProperty, GridBrushProperty, TextBrushProperty);
    public EqualizerCurveControl()
    {
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Cross);
        DataContextChanged += (_, _) => BindModel();
    }
    private void BindModel()
    {
        EndStroke();
        if (_model != null) _model.CurveChanged -= InvalidateVisual;
        _model = DataContext as EqSettingsViewModel;
        if (_model != null) _model.CurveChanged += InvalidateVisual;
        InvalidateVisual();
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        BindModel();
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window != null) _window.Deactivated += OnDeactivated;
    }
    private void OnDeactivated(object? sender, EventArgs e) => EndStroke();
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        EndStroke();
        if (_model != null) _model.CurveChanged -= InvalidateVisual;
        if (_window != null) _window.Deactivated -= OnDeactivated;
        _window = null;
        base.OnDetachedFromVisualTree(e);
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brushes.Transparent, Bounds.WithX(0).WithY(0));
        var plot = Plot;
        for (var gain = -15; gain <= 15; gain += 5)
        {
            var y = plot.Y + (15 - gain) / 30.0 * plot.Height;
            context.DrawLine(new Pen(GridBrush, gain == 0 ? 1.5 : 0.5), new Point(plot.X, y), new Point(plot.Right, y));
            Label(context, gain > 0 ? $"+{gain}" : gain.ToString(CultureInfo.InvariantCulture), new Point(0, y - 7));
        }
        for (var i = 0; i < 10; i++)
        {
            var x = plot.X + EqualizerCurve.Position(i) * plot.Width;
            context.DrawLine(new Pen(GridBrush, 0.5), new Point(x, plot.Y), new Point(x, plot.Bottom));
            var f = EqualizerCurve.Frequencies[i];
            Label(context, f >= 1000 ? $"{f / 1000:0}k" : $"{f:0}", new Point(x - 9, plot.Bottom + 10));
        }
        if (_model == null) return;
        Point? last = null;
        for (var i = 0; i < 10; i++)
        {
            var point = new Point(plot.X + EqualizerCurve.Position(i) * plot.Width, plot.Y + (15 - _model.Gains[i]) / 30 * plot.Height);
            if (last is { } start) context.DrawLine(new Pen(Accent, 2.5), start, point);
            context.DrawEllipse(Accent, null, point, 4, 4);
            last = point;
        }
        if (_guide is { } guide)
        {
            var p = new Point(plot.X + guide.X * plot.Width, plot.Y + guide.Y * plot.Height);
            context.DrawEllipse(null, new Pen(Accent, 1), p, 9, 9);
            var hz = 32 * Math.Pow(500, guide.X);
            Label(context, $"{hz:0} Hz · {EqualizerCurve.Gain(guide.Y):+0.0;-0.0;0.0} dB", new Point(Math.Clamp(p.X - 60, plot.Left, Math.Max(plot.Left, plot.Right - 130)), Math.Max(plot.Top, p.Y - 27)));
        }
    }
    private void Label(DrawingContext context, string text, Point point) => context.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 11, TextBrush), point);
    private Point Normalize(Point p) => new(Math.Clamp((p.X - Plot.X) / Plot.Width, 0, 1), Math.Clamp((p.Y - Plot.Y) / Plot.Height, 0, 1));
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_model == null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || !Plot.Contains(e.GetPosition(this))) return;
        _model.BeginStroke();
        _previous = _guide = Normalize(e.GetPosition(this));
        _pointer = e.Pointer;
        e.Pointer.Capture(this);
        var p = _previous.Value;
        _model.Draw(p.X, p.Y, p.X, p.Y);
        e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_previous is not { } previous || _model == null) return;
        var p = Normalize(e.GetPosition(this));
        _model.Draw(previous.X, previous.Y, p.X, p.Y);
        _previous = _guide = p;
        e.Handled = true;
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_previous is { } previous && _model != null)
        {
            var p = Normalize(e.GetPosition(this));
            _model.Draw(previous.X, previous.Y, p.X, p.Y);
            e.Handled = true;
        }
        EndStroke();
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { base.OnPointerCaptureLost(e); EndStroke(); }
    private void EndStroke()
    {
        _previous = _guide = null;
        var pointer = _pointer;
        _pointer = null;
        pointer?.Capture(null);
        _model?.Flush();
        InvalidateVisual();
    }
}
