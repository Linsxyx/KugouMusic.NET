using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaSilkEffects;
using KugouAvaloniaPlayer.Controls;

namespace KugouAvaloniaPlayer.Views;

public partial class NowPlayingView
{
    private DispatcherTimer? _performanceTimer;
    private TextBlock? _performanceText;
    private Border? _performanceOverlay;

    private void InitializePerformanceDiagnostics()
    {
        if (Environment.GetEnvironmentVariable("KUGOU_RENDER_DIAGNOSTICS") != "1") return;
        _performanceText = new TextBlock
        {
            FontSize = 11, FontFamily = FontFamily.Parse("monospace"),
            Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap,
        };
        _performanceOverlay = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(225, 12, 16, 24)),
            Padding = new Thickness(12), Margin = new Thickness(24, 90, 24, 24),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            MaxWidth = 1000, IsHitTestVisible = false, IsVisible = false,
            Child = _performanceText,
        };
        Root.Children.Add(_performanceOverlay);
        _performanceTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background,
            (_, _) => RefreshPerformanceDiagnostics());
        _performanceTimer.Stop();
    }

    private void RefreshPerformanceDiagnostics()
    {
        if (_performanceText is null || _performanceOverlay is null) return;
        var window = TopLevel.GetTopLevel(this) as Window;
        _performanceOverlay.IsVisible = _nowPlayingViewModel?.IsOpen == true &&
            window?.IsVisible == true && window.WindowState != WindowState.Minimized;
        if (!_performanceOverlay.IsVisible) return;
        var reports = new List<string> { "CPU submission / calling-thread allocations / estimated GPU storage (not GPU execution time)" };
        foreach (var visual in this.GetVisualDescendants())
        {
            if (visual is SilkEffectControl surface && surface.IsVisible && surface.Scene is not null)
            {
                reports.Add(EffectPerformanceReport.Format(surface.Scene.GetType().Name,
                    surface.FrameStatistics, surface.Performance.Snapshot()));
                var build = surface.SceneBuildPerformance.History.Snapshot();
                if (build.SampleCount > 0) reports.Add(EffectPerformanceReport.FormatWork("UI scene compilation", build));
                if (surface is FumeVisualizerControl fume)
                    reports.Add(EffectPerformanceReport.FormatWork("Fume UI preparation", fume.PreparationPerformance.History.Snapshot()));
            }
            else if (visual is PendoloVisualizerControl pendolo)
                reports.Add(EffectPerformanceReport.FormatWork("Pendolo Draw commands", pendolo.DrawingPerformance.History.Snapshot()));
            else if (visual is AudioVisualizerControl spectrum)
                reports.Add(EffectPerformanceReport.FormatWork("Classic spectrum Draw commands", spectrum.DrawingPerformance.History.Snapshot()));
        }
        _performanceText.Text = string.Join("\n\n", reports);
    }
}
