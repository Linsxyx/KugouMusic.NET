using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace KugouAvaloniaPlayer.Controls;

// Drives a mouse-parallax 3D tilt on a target visual. The plane pivots around its
// centre and the side under the pointer sinks inward (away from the viewer), easing
// back to flat (identity) when the pointer leaves the window.
//
// The helper owns its own animation loop so the return-to-flat still animates even
// when the host visualizer's frame loop is idle (e.g. playback stopped).
public sealed class MouseParallax3D(Control target) {
    // Maximum tilt in degrees; larger = steeper.
    public double MaxTilt { get; set; } = 14;
    public double Response { get; set; } = 7.5;

    // When disabled the plane stays flat: input is ignored and the transform is
    // reset. The property is applied live via ResetToFlatIfDisabled whenever a
    // top-level pointer event lands.
    public bool Enabled { get; set; } = true;

    private RelativePoint _origin = RelativePoint.Center;

    // Render pivot of the tilt. Setting it live updates the visual's pivot so the
    // input math (OnPointerMoved) and the rendered rotation agree immediately.
    public RelativePoint Origin {
        get => _origin;
        set {
            if (_origin == value)
                return;
            _origin = value;
            target.RenderTransformOrigin = value;
        }
    }

    private readonly Rotate3DTransform _transform = new();

    private TopLevel? _topLevel;
    private WindowBase? _windowBase;
    private bool _attached;
    private bool _tickQueued;
    private bool _hasFrameTimestamp;
    private TimeSpan _lastFrameTimestamp;
    private double _currentX;
    private double _currentY;
    private double _targetX;
    private double _targetY;

    public void Attach() {
        if (_attached)
            return;
        _attached = true;

        // RenderTransformOrigin already defaults to 50%, 50% so the tilt pivots at the
        // visual's center with a single pivot.
        target.RenderTransformOrigin = Origin;
        target.RenderTransform = _transform;
        _transform.Depth = ResolveDepth();

        _topLevel = TopLevel.GetTopLevel(target);
        _windowBase = _topLevel as WindowBase;
        if (_topLevel != null) {
            _topLevel.PointerMoved += OnPointerMoved;
            _topLevel.PointerExited += OnPointerExited;
            _topLevel.Closed += OnClosed;
        }

        if (_windowBase != null)
            _windowBase.Deactivated += OnDeactivated;

        _currentX = 0;
        _currentY = 0;
        _targetX = 0;
        _targetY = 0;
        _transform.AngleX = 0;
        _transform.AngleY = 0;
    }

    public void Detach() {
        if (!_attached)
            return;
        _attached = false;

        if (_topLevel != null) {
            _topLevel.PointerMoved -= OnPointerMoved;
            _topLevel.PointerExited -= OnPointerExited;
            _topLevel.Closed -= OnClosed;
        }

        if (_windowBase != null)
            _windowBase.Deactivated -= OnDeactivated;

        _topLevel = null;
        _windowBase = null;
        _tickQueued = false;
        target.RenderTransform = null;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e) {
        if (!_attached || _topLevel == null)
            return;
        if (!Enabled) {
            ResetToFlatIfDisabled();
            return;
        }

        // Measure the pointer relative to the configured pivot (RenderTransformOrigin),
        // normalized by half the target's size. For a centered origin this reduces to
        // the original window-normalized math; an off-center origin shifts both the
        // rotation point and where the plane reads as "flat under the pointer".
        var size = target.Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
            return;

        var position = e.GetPosition(target);
        var origin = Origin.ToPixels(size);
        _targetX = Math.Clamp((position.X - origin.X) / (size.Width * 0.5), -1, 1);
        _targetY = Math.Clamp((position.Y - origin.Y) / (size.Height * 0.5), -1, 1);
        RequestTick();
    }

    private void OnPointerExited(object? sender, PointerEventArgs e) {
        ResetToFlat();
        ResetToFlatIfDisabled();
    }

    private void OnDeactivated(object? sender, EventArgs e) {
        ResetToFlat();
        ResetToFlatIfDisabled();
    }

    private void OnClosed(object? sender, EventArgs e) => Detach();

    private void ResetToFlat() {
        if (!_attached)
            return;
        _targetX = 0;
        _targetY = 0;
        RequestTick();
    }

    // Ensure the plane reads flat whenever the effect is disabled, even if the
    // state flips while the pointer stays still inside the window.
    private void ResetToFlatIfDisabled() {
        if (_attached && !Enabled) {
            _targetX = 0;
            _targetY = 0;
            RequestTick();
        }
    }

    private void RequestTick() {
        if (_tickQueued || !_attached)
            return;
        var topLevel = _topLevel;
        if (topLevel == null)
            return;

        _tickQueued = true;
        topLevel.RequestAnimationFrame(OnAnimationFrame);
    }

    private void OnAnimationFrame(TimeSpan timestamp) {
        _tickQueued = false;
        if (!_attached) {
            _hasFrameTimestamp = false;
            return;
        }

        var deltaSeconds = _hasFrameTimestamp ?
            Math.Clamp((timestamp - _lastFrameTimestamp).TotalSeconds, 1d / 240d, 0.05d) :
            1d / 60d;
        _hasFrameTimestamp = true;
        _lastFrameTimestamp = timestamp;

        var ease = 1 - Math.Exp(-Response * deltaSeconds);
        _currentX += (_targetX - _currentX) * ease;
        _currentY += (_targetY - _currentY) * ease;

        // Positive AngleY pivots the right edge inward, positive AngleX pivots the
        // bottom edge toward the viewer, so mirror Y to sink the pointer's side under.
        _transform.AngleY = _currentX * MaxTilt;
        _transform.AngleX = -_currentY * MaxTilt;

        if (Math.Abs(_targetX - _currentX) < 0.0005 && Math.Abs(_targetY - _currentY) < 0.0005) {
            _hasFrameTimestamp = false;
            return;
        }

        RequestTick();
    }

    private double ResolveDepth() {
        var size = _topLevel?.ClientSize ?? default;
        var minSide = Math.Min(Math.Max(size.Width, 1), Math.Max(size.Height, 1));
        return Math.Clamp(minSide * 3, 480, 1400);
    }
}