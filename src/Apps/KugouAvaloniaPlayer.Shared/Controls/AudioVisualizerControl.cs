using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Controls;

public class AudioVisualizerControl : Control
{
    public AvaloniaSilkEffects.EffectCpuWorkMeter DrawingPerformance { get; } = new();

    public static readonly StyledProperty<PlayerViewModel?> PlayerProperty =
        AvaloniaProperty.Register<AudioVisualizerControl, PlayerViewModel?>(nameof(Player));

    public PlayerViewModel? Player
    {
        get => GetValue(PlayerProperty);
        set => SetValue(PlayerProperty, value);
    }
    
    private static readonly IBrush GlowBrush = new ImmutableSolidColorBrush(Color.Parse("#3DFFFFFF"));
    private static readonly IBrush MainBrush;
    private static readonly IBrush HighlightBrush;
    
    private int _invalidateQueued;

    static AudioVisualizerControl()
    {
        MainBrush = new ImmutableLinearGradientBrush(
            [
                new ImmutableGradientStop(0.0, Color.Parse("#08FFFFFF")),
                new ImmutableGradientStop(0.34, Color.Parse("#54FFFFFF")),
                new ImmutableGradientStop(0.72, Color.Parse("#D8FFFFFF")),
                new ImmutableGradientStop(1.0, Color.Parse("#A8F8FFFF"))
            ],
            opacity: 1.0,
            spreadMethod: GradientSpreadMethod.Pad,
            startPoint: new RelativePoint(0, 1, RelativeUnit.Relative),
            endPoint: new RelativePoint(0, 0, RelativeUnit.Relative));

        HighlightBrush = new ImmutableLinearGradientBrush(
            [
                new ImmutableGradientStop(0.0, Color.Parse("#FFFFFFFF")),
                new ImmutableGradientStop(0.42, Color.Parse("#66FFFFFF")),
                new ImmutableGradientStop(1.0, Color.Parse("#00FFFFFF"))
            ],
            opacity: 1.0,
            spreadMethod: GradientSpreadMethod.Pad,
            startPoint: new RelativePoint(0, 0, RelativeUnit.Relative),
            endPoint: new RelativePoint(0, 1, RelativeUnit.Relative));
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Player != null) Player.VisualizerUpdated += OnVisualizerUpdated;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (Player != null) Player.VisualizerUpdated -= OnVisualizerUpdated;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PlayerProperty)
        {
            if (change.OldValue is PlayerViewModel oldPlayer)
                oldPlayer.VisualizerUpdated -= OnVisualizerUpdated;
            if (change.NewValue is PlayerViewModel newPlayer && TopLevel.GetTopLevel(this) != null)
                newPlayer.VisualizerUpdated += OnVisualizerUpdated;
        }
    }

    private void OnVisualizerUpdated()
    {
        if (Interlocked.Exchange(ref _invalidateQueued, 1) == 1)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            _invalidateQueued = 0;
            InvalidateVisual();
        }, DispatcherPriority.Render);
    }

    public override void Render(DrawingContext context)
    {
        using var timing = DrawingPerformance.Measure();
        var player = Player;

        var bands = player?.NowPlayingVisualizerBars;
        if (bands == null || bands.Length == 0) return;

        var width = Bounds.Width;
        var height = Bounds.Height;
        var count = bands.Length;
        var step = width / count;

        for (var i = 0; i < count; i++)
        {
            ref var band = ref bands[i];
            var barHeight = Math.Min(band.Height, height);
            var x = i * step + step / 2;
            var y = height - barHeight;
            
            using (context.PushOpacity(band.Opacity))
            {
                var glowRect = new Rect(x - 6, Math.Max(0, y - 2), 12, barHeight + 2);
                context.DrawRectangle(GlowBrush, null, glowRect, 6, 6);
                
                var mainRect = new Rect(x - 2, y, 4, barHeight);
                context.DrawRectangle(MainBrush, null, mainRect, 2, 2);
                
                var hlRect = new Rect(x - 0.5, y, 1, barHeight);
                context.DrawRectangle(HighlightBrush, null, hlRect, 0.5, 0.5);
            }
        }
    }
}