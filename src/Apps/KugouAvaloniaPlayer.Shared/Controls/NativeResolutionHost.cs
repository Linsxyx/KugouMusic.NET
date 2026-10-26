using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaSilkEffects;
using KugouAvaloniaPlayer.Services;

namespace KugouAvaloniaPlayer.Controls;

/// <summary>
///     Hosts a GL effect surface inside the scaled main UI. OpenGL surfaces are sized from
///     their layout bounds, so under the UI scale they would render at a lower resolution
///     and be stretched. This host cancels the UI scale for layout and passes it to the
///     effect as <see cref="SilkEffectControl.ContentScaleProperty" /> instead.
/// </summary>
public sealed class NativeResolutionHost : LayoutTransformControl
{
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UiScale.CurrentChanged += OnUiScaleChanged;
        ApplyScale();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        UiScale.CurrentChanged -= OnUiScaleChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnUiScaleChanged(object? sender, EventArgs e) => ApplyScale();

    private void ApplyScale()
    {
        var scale = UiScale.Current;
        var isScaled = Math.Abs(scale - 1) >= 0.001;
        LayoutTransform = isScaled ? new ScaleTransform(1 / scale, 1 / scale) : null;
        SilkEffectControl.SetContentScale(this, scale);
    }
}
