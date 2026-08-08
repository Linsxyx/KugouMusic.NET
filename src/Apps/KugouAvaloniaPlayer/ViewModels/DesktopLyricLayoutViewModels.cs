using System;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaLyrics;
using CommunityToolkit.Mvvm.ComponentModel;
using KugouAvaloniaPlayer.Models;

namespace KugouAvaloniaPlayer.ViewModels;

public interface IDesktopLyricLayoutViewModel : IDisposable
{
    DesktopLyricViewModel Owner { get; }
    DesktopLyricLayoutMode Mode { get; }
    void RefreshFromPlayer();
}

public partial class HorizontalDesktopLyricLayoutViewModel : ViewModelBase, IDesktopLyricLayoutViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SingleLineActiveLine))]
    public partial LyricLine? CurrentRenderLyricLine { get; set; }
    [ObservableProperty] public partial LyricLine? TopLyricLine { get; set; }
    [ObservableProperty] public partial LyricLine? BottomLyricLine { get; set; }
    [ObservableProperty] public partial bool IsTopLyricLineCurrent { get; set; }
    [ObservableProperty] public partial bool IsBottomLyricLineCurrent { get; set; }
    [ObservableProperty] public partial double TopLaneOpacity { get; set; } = 1;
    [ObservableProperty] public partial double BottomLaneOpacity { get; set; } = 1;
    [ObservableProperty] public partial double TopLaneTranslateY { get; set; }
    [ObservableProperty] public partial double BottomLaneTranslateY { get; set; }

    private CancellationTokenSource? _topLaneAnimationCancellation;
    private CancellationTokenSource? _bottomLaneAnimationCancellation;
    private bool _disposed;

    public HorizontalDesktopLyricLayoutViewModel(DesktopLyricViewModel owner)
    {
        Owner = owner;
        Owner.Player.PropertyChanged += OnPlayerPropertyChanged;
        RefreshFromPlayer();
    }

    public DesktopLyricViewModel Owner { get; }
    public DesktopLyricLayoutMode Mode => DesktopLyricLayoutMode.Horizontal;
    public bool IsTopLyricLineVisible => Owner.IsDoubleLineEnabled && TopLyricLine != null;
    public bool IsBottomLyricLineVisible => Owner.IsDoubleLineEnabled && BottomLyricLine != null;
    public LyricLine? SingleLineActiveLine => CurrentRenderLyricLine;
    public LyricLine? TopActiveLine => IsTopLyricLineCurrent ? TopLyricLine : null;
    public LyricLine? BottomActiveLine => IsBottomLyricLineCurrent ? BottomLyricLine : null;
    public LyricWordRenderMode SingleLineWordRenderMode => LyricWordRenderMode.Clip;
    public LyricWordRenderMode TopLaneWordRenderMode =>
        IsTopLyricLineCurrent ? LyricWordRenderMode.Clip : LyricWordRenderMode.Plain;
    public LyricWordRenderMode BottomLaneWordRenderMode =>
        IsBottomLyricLineCurrent ? LyricWordRenderMode.Clip : LyricWordRenderMode.Plain;

    public void RefreshFromPlayer()
    {
        if (_disposed)
            return;

        var currentIndex = Owner.Player.CurrentLyricIndex;
        CurrentRenderLyricLine = GetRenderLineAt(currentIndex);
        Owner.NotifyHorizontalContentChanged();

        if (!Owner.IsDoubleLineEnabled || CurrentRenderLyricLine == null || currentIndex < 0)
        {
            SetTopLaneImmediate(null, false);
            SetBottomLaneImmediate(null, false);
            return;
        }

        var currentLine = CurrentRenderLyricLine;
        var nextLine = GetRenderLineAt(currentIndex + 1);
        if (currentIndex % 2 == 0)
        {
            SetTopLane(currentLine, true);
            SetBottomLane(nextLine, false);
        }
        else
        {
            SetTopLane(nextLine, false);
            SetBottomLane(currentLine, true);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        CancelAndDisposeLaneAnimations();
        Owner.Player.PropertyChanged -= OnPlayerPropertyChanged;
        CurrentRenderLyricLine = null;
        TopLyricLine = null;
        BottomLyricLine = null;
    }

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Owner.Player.CurrentLyricLine) or nameof(Owner.Player.CurrentLyricIndex) or
            nameof(Owner.Player.NextLyricLine))
            RefreshFromPlayer();
    }

    private LyricLine? GetRenderLineAt(int index)
    {
        return index >= 0 && index < Owner.Player.RenderLyricLines.Count
            ? Owner.Player.RenderLyricLines[index]
            : null;
    }

    private void SetTopLane(LyricLine? line, bool isCurrent)
    {
        if (ReferenceEquals(TopLyricLine, line))
        {
            IsTopLyricLineCurrent = isCurrent;
            RaiseComputedProperties();
            return;
        }

        if (TopLyricLine == null || line == null)
            SetTopLaneImmediate(line, isCurrent);
        else
            _ = AnimateTopLaneChangeAsync(line, isCurrent);
    }

    private void SetBottomLane(LyricLine? line, bool isCurrent)
    {
        if (ReferenceEquals(BottomLyricLine, line))
        {
            IsBottomLyricLineCurrent = isCurrent;
            RaiseComputedProperties();
            return;
        }

        if (BottomLyricLine == null || line == null)
            SetBottomLaneImmediate(line, isCurrent);
        else
            _ = AnimateBottomLaneChangeAsync(line, isCurrent);
    }

    private void SetTopLaneImmediate(LyricLine? line, bool isCurrent)
    {
        CancelAndDisposeTopLaneAnimation();
        TopLyricLine = line;
        IsTopLyricLineCurrent = isCurrent;
        TopLaneOpacity = 1;
        TopLaneTranslateY = 0;
        RaiseComputedProperties();
    }

    private void SetBottomLaneImmediate(LyricLine? line, bool isCurrent)
    {
        CancelAndDisposeBottomLaneAnimation();
        BottomLyricLine = line;
        IsBottomLyricLineCurrent = isCurrent;
        BottomLaneOpacity = 1;
        BottomLaneTranslateY = 0;
        RaiseComputedProperties();
    }

    private async Task AnimateTopLaneChangeAsync(LyricLine line, bool isCurrent)
    {
        CancelAndDisposeTopLaneAnimation();
        var cts = new CancellationTokenSource();
        _topLaneAnimationCancellation = cts;
        try
        {
            TopLaneOpacity = 0;
            TopLaneTranslateY = -8;
            await Task.Delay(120, cts.Token);
            TopLyricLine = line;
            IsTopLyricLineCurrent = isCurrent;
            RaiseComputedProperties();
            TopLaneTranslateY = 8;
            await Task.Delay(16, cts.Token);
            TopLaneOpacity = 1;
            TopLaneTranslateY = 0;
        }
        catch (TaskCanceledException)
        {
        }
    }

    private async Task AnimateBottomLaneChangeAsync(LyricLine line, bool isCurrent)
    {
        CancelAndDisposeBottomLaneAnimation();
        var cts = new CancellationTokenSource();
        _bottomLaneAnimationCancellation = cts;
        try
        {
            BottomLaneOpacity = 0;
            BottomLaneTranslateY = 8;
            await Task.Delay(120, cts.Token);
            BottomLyricLine = line;
            IsBottomLyricLineCurrent = isCurrent;
            RaiseComputedProperties();
            BottomLaneTranslateY = -8;
            await Task.Delay(16, cts.Token);
            BottomLaneOpacity = 1;
            BottomLaneTranslateY = 0;
        }
        catch (TaskCanceledException)
        {
        }
    }

    private void RaiseComputedProperties()
    {
        OnPropertyChanged(nameof(IsTopLyricLineVisible));
        OnPropertyChanged(nameof(IsBottomLyricLineVisible));
        OnPropertyChanged(nameof(TopActiveLine));
        OnPropertyChanged(nameof(BottomActiveLine));
        OnPropertyChanged(nameof(TopLaneWordRenderMode));
        OnPropertyChanged(nameof(BottomLaneWordRenderMode));
    }

    private void CancelAndDisposeLaneAnimations()
    {
        CancelAndDisposeTopLaneAnimation();
        CancelAndDisposeBottomLaneAnimation();
    }

    private void CancelAndDisposeTopLaneAnimation()
    {
        _topLaneAnimationCancellation?.Cancel();
        _topLaneAnimationCancellation?.Dispose();
        _topLaneAnimationCancellation = null;
    }

    private void CancelAndDisposeBottomLaneAnimation()
    {
        _bottomLaneAnimationCancellation?.Cancel();
        _bottomLaneAnimationCancellation?.Dispose();
        _bottomLaneAnimationCancellation = null;
    }
}

public partial class VerticalDesktopLyricLayoutViewModel : ViewModelBase, IDesktopLyricLayoutViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentActiveLine))]
    public partial LyricLine? CurrentLine { get; set; }
    [ObservableProperty] public partial LyricLine? NextLine { get; set; }
    [ObservableProperty] public partial double CurrentLaneOpacity { get; set; } = 1;
    [ObservableProperty] public partial double NextLaneOpacity { get; set; } = 1;
    [ObservableProperty] public partial double CurrentLaneTranslateX { get; set; }
    [ObservableProperty] public partial double NextLaneTranslateX { get; set; }

    private int _lastLyricIndex = -1;
    private CancellationTokenSource? _laneAnimationCancellation;
    private bool _disposed;

    public VerticalDesktopLyricLayoutViewModel(DesktopLyricViewModel owner)
    {
        Owner = owner;
        Owner.Player.PropertyChanged += OnPlayerPropertyChanged;
        RefreshFromPlayer();
    }

    public DesktopLyricViewModel Owner { get; }
    public DesktopLyricLayoutMode Mode => DesktopLyricLayoutMode.Vertical;
    public LyricLine? CurrentActiveLine => CurrentLine;
    public LyricWordRenderMode CurrentWordRenderMode => LyricWordRenderMode.Clip;
    public LyricWordRenderMode NextWordRenderMode => LyricWordRenderMode.Plain;

    public void RefreshFromPlayer()
    {
        if (_disposed)
            return;

        var currentIndex = Owner.Player.CurrentLyricIndex;
        if (currentIndex < 0 || currentIndex < _lastLyricIndex)
            Owner.ResetVerticalDesiredWidth();
        _lastLyricIndex = currentIndex;

        var currentLine = GetRenderLineAt(currentIndex);
        var nextLine = Owner.IsDoubleLineEnabled ? GetRenderLineAt(currentIndex + 1) : null;
        SetLines(currentLine, nextLine);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        CancelAndDisposeLaneAnimation();
        Owner.Player.PropertyChanged -= OnPlayerPropertyChanged;
        CurrentLine = null;
        NextLine = null;
    }

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Owner.Player.CurrentLyricLine) or nameof(Owner.Player.CurrentLyricIndex) or
            nameof(Owner.Player.NextLyricLine))
            RefreshFromPlayer();
    }

    private LyricLine? GetRenderLineAt(int index)
    {
        return index >= 0 && index < Owner.Player.RenderLyricLines.Count
            ? Owner.Player.RenderLyricLines[index]
            : null;
    }

    private void SetLines(LyricLine? currentLine, LyricLine? nextLine)
    {
        if (ReferenceEquals(CurrentLine, currentLine) && ReferenceEquals(NextLine, nextLine))
        {
            Owner.ReportVerticalDesiredWidth(CalculateRequiredWidth());
            return;
        }

        if (!Owner.IsDoubleLineEnabled || CurrentLine == null || currentLine == null)
        {
            SetLinesImmediate(currentLine, nextLine);
            return;
        }

        _ = AnimateLineChangeAsync(currentLine, nextLine);
    }

    private void SetLinesImmediate(LyricLine? currentLine, LyricLine? nextLine)
    {
        CancelAndDisposeLaneAnimation();
        CurrentLine = currentLine;
        NextLine = nextLine;
        CurrentLaneOpacity = 1;
        NextLaneOpacity = 1;
        CurrentLaneTranslateX = 0;
        NextLaneTranslateX = 0;
        Owner.ReportVerticalDesiredWidth(CalculateRequiredWidth());
    }

    private async Task AnimateLineChangeAsync(LyricLine currentLine, LyricLine? nextLine)
    {
        CancelAndDisposeLaneAnimation();
        var cts = new CancellationTokenSource();
        _laneAnimationCancellation = cts;
        try
        {
            CurrentLaneOpacity = 0;
            NextLaneOpacity = 0;
            CurrentLaneTranslateX = -8;
            NextLaneTranslateX = 8;
            await Task.Delay(120, cts.Token);

            CurrentLine = currentLine;
            NextLine = nextLine;
            Owner.ReportVerticalDesiredWidth(CalculateRequiredWidth());
            CurrentLaneTranslateX = 8;
            NextLaneTranslateX = -8;
            await Task.Delay(16, cts.Token);

            CurrentLaneOpacity = 1;
            NextLaneOpacity = 1;
            CurrentLaneTranslateX = 0;
            NextLaneTranslateX = 0;
        }
        catch (TaskCanceledException)
        {
        }
    }

    private void CancelAndDisposeLaneAnimation()
    {
        _laneAnimationCancellation?.Cancel();
        _laneAnimationCancellation?.Dispose();
        _laneAnimationCancellation = null;
    }

    private double CalculateRequiredWidth()
    {
        var currentWidth = CalculateLineWidth(
            CurrentLine,
            Owner.IsDesktopTranslationActuallyVisible,
            Owner.VerticalContentHeight);
        var contentWidth = currentWidth;
        if (Owner.IsDoubleLineEnabled && NextLine != null)
            contentWidth += 18d + CalculateLineWidth(NextLine, false, Owner.VerticalContentHeight);

        return Math.Max(DesktopLyricViewModel.VerticalBaseWindowWidth, contentWidth + 48d);
    }

    private double CalculateLineWidth(LyricLine? line, bool includeTranslation, double availableHeight)
    {
        if (line == null)
            return 0d;

        var primaryWidth = CalculateTextWidth(line.Text, Owner.FontSize, availableHeight);
        if (!includeTranslation || string.IsNullOrWhiteSpace(line.Translation))
            return primaryWidth;

        return primaryWidth + 12d + CalculateTextWidth(line.Translation, Owner.TranslationFontSize, availableHeight);
    }

    private static double CalculateTextWidth(string? text, double fontSize, double availableHeight)
    {
        var elementCount = string.IsNullOrEmpty(text)
            ? 0
            : StringInfo.ParseCombiningCharacters(text).Length;
        if (elementCount == 0)
            return 0d;

        var cellHeight = Math.Max(1d, fontSize * 1.16d);
        var rows = Math.Max(1, (int)Math.Floor(availableHeight / cellHeight));
        var columns = Math.Max(1, (int)Math.Ceiling(elementCount / (double)rows));
        return columns * Math.Max(fontSize, 1d) + Math.Max(0, columns - 1) * 8d;
    }
}
