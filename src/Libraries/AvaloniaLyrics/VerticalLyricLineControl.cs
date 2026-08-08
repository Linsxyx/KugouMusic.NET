using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AvaloniaLyrics;

public sealed class VerticalLyricLineControl : UserControl
{
    private static readonly TimeSpan MaxPositionClockDrift = TimeSpan.FromMilliseconds(240);

    public static readonly StyledProperty<LyricLine?> LineProperty =
        AvaloniaProperty.Register<VerticalLyricLineControl, LyricLine?>(nameof(Line));

    public static readonly StyledProperty<LyricLine?> ActiveLineProperty =
        AvaloniaProperty.Register<VerticalLyricLineControl, LyricLine?>(nameof(ActiveLine));

    public static readonly StyledProperty<TimeSpan> PositionProperty =
        AvaloniaProperty.Register<VerticalLyricLineControl, TimeSpan>(nameof(Position));

    public static readonly StyledProperty<bool> IsPositionClockRunningProperty =
        AvaloniaProperty.Register<VerticalLyricLineControl, bool>(nameof(IsPositionClockRunning));

    public static readonly StyledProperty<bool> ShowTranslationProperty =
        AvaloniaProperty.Register<VerticalLyricLineControl, bool>(nameof(ShowTranslation), true);

    public static readonly StyledProperty<LyricWordRenderMode> WordRenderModeProperty =
        AvaloniaProperty.Register<VerticalLyricLineControl, LyricWordRenderMode>(
            nameof(WordRenderMode), LyricWordRenderMode.Clip);

    public new static readonly StyledProperty<FontFamily> FontFamilyProperty =
        TextBlock.FontFamilyProperty.AddOwner<VerticalLyricLineControl>();

    public static readonly StyledProperty<double> PrimaryFontSizeProperty =
        AvaloniaProperty.Register<VerticalLyricLineControl, double>(nameof(PrimaryFontSize), 30d);

    public static readonly StyledProperty<double> TranslationFontSizeProperty =
        AvaloniaProperty.Register<VerticalLyricLineControl, double>(nameof(TranslationFontSize), 18d);

    public static readonly StyledProperty<IBrush> PrimaryForegroundProperty =
        AvaloniaProperty.Register<VerticalLyricLineControl, IBrush>(nameof(PrimaryForeground), Brushes.White);

    public static readonly StyledProperty<IBrush> PrimaryPlayedForegroundProperty =
        AvaloniaProperty.Register<VerticalLyricLineControl, IBrush>(nameof(PrimaryPlayedForeground), Brushes.White);

    public static readonly StyledProperty<IBrush> TranslationForegroundProperty =
        AvaloniaProperty.Register<VerticalLyricLineControl, IBrush>(
            nameof(TranslationForeground), new SolidColorBrush(Color.Parse("#CCFFFFFF")));

    public static readonly StyledProperty<TextAlignment> TextAlignmentProperty =
        TextBlock.TextAlignmentProperty.AddOwner<VerticalLyricLineControl>();

    public static readonly StyledProperty<double> ColumnSpacingProperty =
        AvaloniaProperty.Register<VerticalLyricLineControl, double>(nameof(ColumnSpacing), 12d);

    private readonly StackPanel _rootPanel;
    private readonly VerticalKaraokeTextBlock _primaryTextBlock;
    private readonly VerticalFlowPanel _primaryWordPanel;
    private readonly VerticalKaraokeTextBlock _translationTextBlock;
    private readonly List<WordVisual> _primaryWordVisuals = [];
    private TimeSpan _positionAnchor;
    private long _positionAnchorTimestamp;
    private bool _positionFrameQueued;
    private TimeSpan _renderPosition;

    public VerticalLyricLineControl()
    {
        _primaryTextBlock = CreateTextBlock();
        _primaryWordPanel = new VerticalFlowPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            ColumnSpacing = ColumnSpacing
        };
        _translationTextBlock = CreateTextBlock();

        _rootPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = ColumnSpacing,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _rootPanel.Children.Add(_primaryTextBlock);
        _rootPanel.Children.Add(_primaryWordPanel);
        _rootPanel.Children.Add(_translationTextBlock);
        Content = _rootPanel;

        _positionAnchor = Position;
        _positionAnchorTimestamp = Stopwatch.GetTimestamp();
        _renderPosition = Position;
        UpdateLayoutState(rebuildWords: true);
    }

    public LyricLine? Line
    {
        get => GetValue(LineProperty);
        set => SetValue(LineProperty, value);
    }

    public LyricLine? ActiveLine
    {
        get => GetValue(ActiveLineProperty);
        set => SetValue(ActiveLineProperty, value);
    }

    public TimeSpan Position
    {
        get => GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    public bool IsPositionClockRunning
    {
        get => GetValue(IsPositionClockRunningProperty);
        set => SetValue(IsPositionClockRunningProperty, value);
    }

    public bool ShowTranslation
    {
        get => GetValue(ShowTranslationProperty);
        set => SetValue(ShowTranslationProperty, value);
    }

    public LyricWordRenderMode WordRenderMode
    {
        get => GetValue(WordRenderModeProperty);
        set => SetValue(WordRenderModeProperty, value);
    }

    public new FontFamily FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double PrimaryFontSize
    {
        get => GetValue(PrimaryFontSizeProperty);
        set => SetValue(PrimaryFontSizeProperty, value);
    }

    public double TranslationFontSize
    {
        get => GetValue(TranslationFontSizeProperty);
        set => SetValue(TranslationFontSizeProperty, value);
    }

    public IBrush PrimaryForeground
    {
        get => GetValue(PrimaryForegroundProperty);
        set => SetValue(PrimaryForegroundProperty, value);
    }

    public IBrush PrimaryPlayedForeground
    {
        get => GetValue(PrimaryPlayedForegroundProperty);
        set => SetValue(PrimaryPlayedForegroundProperty, value);
    }

    public IBrush TranslationForeground
    {
        get => GetValue(TranslationForegroundProperty);
        set => SetValue(TranslationForegroundProperty, value);
    }

    public TextAlignment TextAlignment
    {
        get => GetValue(TextAlignmentProperty);
        set => SetValue(TextAlignmentProperty, value);
    }

    public double ColumnSpacing
    {
        get => GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == LineProperty || change.Property == WordRenderModeProperty)
        {
            UpdateLayoutState(rebuildWords: true);
            return;
        }

        if (change.Property == PositionProperty)
        {
            SyncPositionAnchor();
            if (!ShouldRunPositionClock())
                SetRenderPosition(Position);
            if (ReferenceEquals(Line, ActiveLine))
                RefreshWordProgress();
            EnsurePositionClockRunning();
            return;
        }

        if (change.Property == ActiveLineProperty)
        {
            UpdateLayoutState(rebuildWords: false);
            EnsurePositionClockRunning();
            return;
        }

        if (change.Property == IsPositionClockRunningProperty || change.Property == IsVisibleProperty)
        {
            if (!ShouldRunPositionClock())
                StopPositionClock(syncToPosition: true);
            else
                EnsurePositionClockRunning();
            return;
        }

        if (change.Property == FontFamilyProperty ||
            change.Property == PrimaryFontSizeProperty ||
            change.Property == TranslationFontSizeProperty)
        {
            UpdateLayoutState(rebuildWords: true);
            return;
        }

        if (change.Property == ShowTranslationProperty ||
            change.Property == PrimaryForegroundProperty ||
            change.Property == PrimaryPlayedForegroundProperty ||
            change.Property == TranslationForegroundProperty ||
            change.Property == TextAlignmentProperty ||
            change.Property == ColumnSpacingProperty)
        {
            UpdateLayoutState(rebuildWords: false);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopPositionClock(syncToPosition: false);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SyncPositionAnchor();
        EnsurePositionClockRunning();
    }

    private void UpdateLayoutState(bool rebuildWords)
    {
        var line = Line;
        var isActive = ReferenceEquals(line, ActiveLine);
        var showWords = isActive && line is { Words.Count: > 0 } && WordRenderMode == LyricWordRenderMode.Clip;

        _rootPanel.Spacing = ColumnSpacing;
        _rootPanel.HorizontalAlignment = TextAlignment switch
        {
            TextAlignment.Left or TextAlignment.Start => HorizontalAlignment.Left,
            TextAlignment.Right or TextAlignment.End => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Center
        };
        _primaryWordPanel.ColumnSpacing = ColumnSpacing;
        _primaryWordPanel.HorizontalContentAlignment = _rootPanel.HorizontalAlignment;

        ConfigureTextBlock(
            _primaryTextBlock,
            line?.Text,
            PrimaryFontSize,
            PrimaryForeground,
            PrimaryPlayedForeground,
            isActive ? 1d : 0.48d);
        ConfigureTextBlock(
            _translationTextBlock,
            line?.Translation,
            TranslationFontSize,
            TranslationForeground,
            TranslationForeground,
            0.82d);

        if (rebuildWords)
            RebuildWordVisuals(line?.Words);

        _primaryWordPanel.IsVisible = showWords;
        _primaryTextBlock.IsVisible = !showWords && !string.IsNullOrWhiteSpace(line?.Text);
        _translationTextBlock.IsVisible = ShowTranslation && !string.IsNullOrWhiteSpace(line?.Translation);
        RefreshWordProgress();
    }

    private void RebuildWordVisuals(IReadOnlyList<LyricWord>? words)
    {
        _primaryWordPanel.Children.Clear();
        _primaryWordVisuals.Clear();
        if (words == null)
            return;

        foreach (var word in words)
        {
            var control = CreateTextBlock();
            control.Text = word.Text;
            control.FontSize = PrimaryFontSize;
            control.FontFamily = FontFamily;
            control.FontWeight = FontWeight.Bold;
            control.Foreground = PrimaryForeground;
            control.PlayedForeground = PrimaryPlayedForeground;
            control.UnplayedOpacity = 0.34d;
            control.UsePlayedGradient = true;
            control.Progress = 0d;
            _primaryWordVisuals.Add(new WordVisual(word, control));
            _primaryWordPanel.Children.Add(control);
        }
    }

    private void RefreshWordProgress()
    {
        var isActive = ReferenceEquals(Line, ActiveLine);
        foreach (var visual in _primaryWordVisuals)
        {
            visual.Control.FontFamily = FontFamily;
            visual.Control.FontSize = PrimaryFontSize;
            visual.Control.Foreground = PrimaryForeground;
            visual.Control.PlayedForeground = PrimaryPlayedForeground;
            visual.Control.UnplayedOpacity = isActive ? 0.34d : 0.52d;
            visual.Control.Opacity = isActive ? 1d : 0.92d;
            visual.Control.Progress = isActive
                ? LyricProgressCalculator.GetProgress(_renderPosition, visual.Word.Start, visual.Word.Duration)
                : 0d;
        }
    }

    private void SyncPositionAnchor()
    {
        _positionAnchor = Position;
        _positionAnchorTimestamp = Stopwatch.GetTimestamp();
        _renderPosition = Position;
    }

    private void SetRenderPosition(TimeSpan value)
    {
        if (_renderPosition == value)
            return;
        _renderPosition = value;
        RefreshWordProgress();
    }

    private bool ShouldRunPositionClock()
    {
        return IsPositionClockRunning && IsVisible && ReferenceEquals(Line, ActiveLine) &&
               _primaryWordVisuals.Count > 0 && TopLevel.GetTopLevel(this) != null;
    }

    private void EnsurePositionClockRunning()
    {
        if (_positionFrameQueued || !ShouldRunPositionClock())
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;

        _positionFrameQueued = true;
        topLevel.RequestAnimationFrame(OnPositionAnimationFrame);
    }

    private void StopPositionClock(bool syncToPosition)
    {
        _positionFrameQueued = false;
        if (syncToPosition)
            SetRenderPosition(Position);
    }

    private void OnPositionAnimationFrame(TimeSpan timestamp)
    {
        _positionFrameQueued = false;
        if (!ShouldRunPositionClock())
        {
            StopPositionClock(syncToPosition: true);
            return;
        }

        var elapsed = Stopwatch.GetElapsedTime(_positionAnchorTimestamp);
        SetRenderPosition(_positionAnchor + (elapsed > MaxPositionClockDrift ? MaxPositionClockDrift : elapsed));
        EnsurePositionClockRunning();
    }

    private static VerticalKaraokeTextBlock CreateTextBlock()
    {
        return new VerticalKaraokeTextBlock
        {
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private void ConfigureTextBlock(
        VerticalKaraokeTextBlock block,
        string? text,
        double fontSize,
        IBrush foreground,
        IBrush playedForeground,
        double opacity)
    {
        block.Text = text;
        block.FontSize = fontSize;
        block.FontFamily = FontFamily;
        block.Foreground = foreground;
        block.PlayedForeground = playedForeground;
        block.UnplayedOpacity = opacity;
        block.PlayedOpacity = opacity;
        block.UsePlayedGradient = false;
        block.Progress = 1d;
        block.ColumnSpacing = ColumnSpacing;
    }

    private sealed record WordVisual(LyricWord Word, VerticalKaraokeTextBlock Control);
}
