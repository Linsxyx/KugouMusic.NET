using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AvaloniaLyrics;

public class LyricLineControl : UserControl
{
    public static readonly StyledProperty<LyricViewPreset> PresetProperty =
        AvaloniaProperty.Register<LyricLineControl, LyricViewPreset>(nameof(Preset), LyricViewPreset.Default);

    public static readonly StyledProperty<LyricLine?> LineProperty =
        AvaloniaProperty.Register<LyricLineControl, LyricLine?>(nameof(Line));

    public static readonly StyledProperty<LyricLine?> ActiveLineProperty =
        AvaloniaProperty.Register<LyricLineControl, LyricLine?>(nameof(ActiveLine));

    public static readonly StyledProperty<TimeSpan> PositionProperty =
        AvaloniaProperty.Register<LyricLineControl, TimeSpan>(nameof(Position));

    public static readonly StyledProperty<bool> ShowPrimaryTextProperty =
        AvaloniaProperty.Register<LyricLineControl, bool>(nameof(ShowPrimaryText), true);

    public static readonly StyledProperty<bool> ShowTranslationProperty =
        AvaloniaProperty.Register<LyricLineControl, bool>(nameof(ShowTranslation), true);

    public static readonly StyledProperty<bool> ShowRomanizationProperty =
        AvaloniaProperty.Register<LyricLineControl, bool>(nameof(ShowRomanization), true);

    public static readonly StyledProperty<LyricWordRenderMode> WordRenderModeProperty =
        AvaloniaProperty.Register<LyricLineControl, LyricWordRenderMode>(nameof(WordRenderMode), LyricWordRenderMode.Clip);

    public static readonly StyledProperty<KaraokeClipMode> ClipModeProperty =
        AvaloniaProperty.Register<LyricLineControl, KaraokeClipMode>(nameof(ClipMode), KaraokeClipMode.ByTextWidth);

    public new static readonly StyledProperty<FontFamily> FontFamilyProperty =
        TextBlock.FontFamilyProperty.AddOwner<LyricLineControl>();

    public static readonly StyledProperty<double> PrimaryFontSizeProperty =
        AvaloniaProperty.Register<LyricLineControl, double>(nameof(PrimaryFontSize), 26d);

    public static readonly StyledProperty<double> TranslationFontSizeProperty =
        AvaloniaProperty.Register<LyricLineControl, double>(nameof(TranslationFontSize), 16d);

    public static readonly StyledProperty<double> RomanizationFontSizeProperty =
        AvaloniaProperty.Register<LyricLineControl, double>(nameof(RomanizationFontSize), 16d);

    public static readonly StyledProperty<IBrush> PrimaryForegroundProperty =
        AvaloniaProperty.Register<LyricLineControl, IBrush>(nameof(PrimaryForeground), Brushes.White);

    public static readonly StyledProperty<IBrush> PrimaryPlayedForegroundProperty =
        AvaloniaProperty.Register<LyricLineControl, IBrush>(nameof(PrimaryPlayedForeground), Brushes.White);

    public static readonly StyledProperty<IBrush> TranslationForegroundProperty =
        AvaloniaProperty.Register<LyricLineControl, IBrush>(nameof(TranslationForeground), new SolidColorBrush(Color.Parse("#CCFFFFFF")));

    public static readonly StyledProperty<IBrush> TranslationPlayedForegroundProperty =
        AvaloniaProperty.Register<LyricLineControl, IBrush>(nameof(TranslationPlayedForeground), Brushes.White);

    public static readonly StyledProperty<IBrush> RomanizationForegroundProperty =
        AvaloniaProperty.Register<LyricLineControl, IBrush>(nameof(RomanizationForeground), new SolidColorBrush(Color.Parse("#CCFFFFFF")));

    public static readonly StyledProperty<TextAlignment> TextAlignmentProperty =
        TextBlock.TextAlignmentProperty.AddOwner<LyricLineControl>();

    public static readonly StyledProperty<double> SectionSpacingProperty =
        AvaloniaProperty.Register<LyricLineControl, double>(nameof(SectionSpacing), 0d);

    public static readonly StyledProperty<double> SecondaryTopSpacingProperty =
        AvaloniaProperty.Register<LyricLineControl, double>(nameof(SecondaryTopSpacing), 0d);

    public static readonly StyledProperty<double> InactivePrimaryOpacityProperty =
        AvaloniaProperty.Register<LyricLineControl, double>(nameof(InactivePrimaryOpacity), 0.48d);

    public static readonly StyledProperty<double> InactiveSecondaryOpacityProperty =
        AvaloniaProperty.Register<LyricLineControl, double>(nameof(InactiveSecondaryOpacity), 0.6d);

    private readonly StackPanel _rootPanel;
    private readonly TextBlock _primaryTextBlock;
    private readonly AlignedWrapPanel _primaryWordPanel;
    private readonly TextBlock _translationTextBlock;
    private readonly AlignedWrapPanel _translationWordPanel;
    private readonly TextBlock _romanizationTextBlock;
    private readonly List<WordVisual> _primaryWordVisuals = [];
    private readonly List<WordVisual> _translationWordVisuals = [];
    private bool _isApplyingPreset;

    public LyricLineControl()
    {
        _primaryTextBlock = CreateTextBlock();
        _translationTextBlock = CreateTextBlock();
        _romanizationTextBlock = CreateTextBlock();
        _primaryWordPanel = CreateWordPanel();
        _translationWordPanel = CreateWordPanel();

        _rootPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = SectionSpacing,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        _rootPanel.Children.Add(_primaryTextBlock);
        _rootPanel.Children.Add(_primaryWordPanel);
        _rootPanel.Children.Add(_translationTextBlock);
        _rootPanel.Children.Add(_translationWordPanel);
        _rootPanel.Children.Add(_romanizationTextBlock);
        Content = _rootPanel;

        UpdateLayoutState(rebuildWords: true);
    }

    public LyricViewPreset Preset
    {
        get => GetValue(PresetProperty);
        set => SetValue(PresetProperty, value);
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

    public bool ShowPrimaryText
    {
        get => GetValue(ShowPrimaryTextProperty);
        set => SetValue(ShowPrimaryTextProperty, value);
    }

    public bool ShowTranslation
    {
        get => GetValue(ShowTranslationProperty);
        set => SetValue(ShowTranslationProperty, value);
    }

    public bool ShowRomanization
    {
        get => GetValue(ShowRomanizationProperty);
        set => SetValue(ShowRomanizationProperty, value);
    }

    public LyricWordRenderMode WordRenderMode
    {
        get => GetValue(WordRenderModeProperty);
        set => SetValue(WordRenderModeProperty, value);
    }

    public KaraokeClipMode ClipMode
    {
        get => GetValue(ClipModeProperty);
        set => SetValue(ClipModeProperty, value);
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

    public double RomanizationFontSize
    {
        get => GetValue(RomanizationFontSizeProperty);
        set => SetValue(RomanizationFontSizeProperty, value);
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

    public IBrush TranslationPlayedForeground
    {
        get => GetValue(TranslationPlayedForegroundProperty);
        set => SetValue(TranslationPlayedForegroundProperty, value);
    }

    public IBrush RomanizationForeground
    {
        get => GetValue(RomanizationForegroundProperty);
        set => SetValue(RomanizationForegroundProperty, value);
    }

    public TextAlignment TextAlignment
    {
        get => GetValue(TextAlignmentProperty);
        set => SetValue(TextAlignmentProperty, value);
    }

    public double SectionSpacing
    {
        get => GetValue(SectionSpacingProperty);
        set => SetValue(SectionSpacingProperty, value);
    }

    public double SecondaryTopSpacing
    {
        get => GetValue(SecondaryTopSpacingProperty);
        set => SetValue(SecondaryTopSpacingProperty, value);
    }

    public double InactivePrimaryOpacity
    {
        get => GetValue(InactivePrimaryOpacityProperty);
        set => SetValue(InactivePrimaryOpacityProperty, value);
    }

    public double InactiveSecondaryOpacity
    {
        get => GetValue(InactiveSecondaryOpacityProperty);
        set => SetValue(InactiveSecondaryOpacityProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == PresetProperty)
        {
            ApplyPreset((LyricViewPreset)change.NewValue!);
            UpdateLayoutState(rebuildWords: true);
            return;
        }

        if (change.Property == LineProperty || change.Property == WordRenderModeProperty)
        {
            UpdateLayoutState(rebuildWords: true);
            return;
        }

        if (change.Property == PositionProperty || change.Property == ActiveLineProperty)
        {
            UpdateLayoutState(rebuildWords: false);
            return;
        }

        if (change.Property == ShowPrimaryTextProperty ||
            change.Property == ShowTranslationProperty ||
            change.Property == ShowRomanizationProperty ||
            change.Property == PrimaryFontSizeProperty ||
            change.Property == TranslationFontSizeProperty ||
            change.Property == RomanizationFontSizeProperty ||
            change.Property == PrimaryForegroundProperty ||
            change.Property == PrimaryPlayedForegroundProperty ||
            change.Property == TranslationForegroundProperty ||
            change.Property == TranslationPlayedForegroundProperty ||
            change.Property == RomanizationForegroundProperty ||
            change.Property == FontFamilyProperty ||
            change.Property == TextAlignmentProperty ||
            change.Property == SectionSpacingProperty ||
            change.Property == SecondaryTopSpacingProperty ||
            change.Property == InactivePrimaryOpacityProperty ||
            change.Property == InactiveSecondaryOpacityProperty ||
            change.Property == ClipModeProperty)
        {
            UpdateLayoutState(rebuildWords: false);
        }
    }

    private void ApplyPreset(LyricViewPreset preset)
    {
        if (_isApplyingPreset)
            return;

        _isApplyingPreset = true;
        try
        {
            ClipMode = KaraokeClipMode.ByTextWidth;
            switch (preset)
            {
                case LyricViewPreset.AppleMusicClassic:
                    TextAlignment = TextAlignment.Center;
                    SecondaryTopSpacing = 5d;
                    break;

                case LyricViewPreset.DesktopClassic:
                    TextAlignment = TextAlignment.Center;
                    SecondaryTopSpacing = 0d;
                    break;

                default:
                    TextAlignment = TextAlignment.Left;
                    SecondaryTopSpacing = 0d;
                    break;
            }

            SectionSpacing = 0d;
            InactivePrimaryOpacity = 0.48d;
            InactiveSecondaryOpacity = 0.6d;
        }
        finally
        {
            _isApplyingPreset = false;
        }
    }

    private void UpdateLayoutState(bool rebuildWords)
    {
        var line = Line;
        var isActive = ReferenceEquals(line, ActiveLine);
        _rootPanel.Spacing = SectionSpacing;

        var horizontalAlignment = ToHorizontalAlignment(TextAlignment);
        _primaryWordPanel.HorizontalContentAlignment = horizontalAlignment;
        _translationWordPanel.HorizontalContentAlignment = horizontalAlignment;

        ApplyTextBlockStyle(_primaryTextBlock, PrimaryFontSize, PrimaryForeground, isActive, InactivePrimaryOpacity, FontWeight.ExtraLight, FontWeight.ExtraLight);
        ApplyTextBlockStyle(_translationTextBlock, TranslationFontSize, TranslationForeground, isActive, InactiveSecondaryOpacity, FontWeight.Medium, FontWeight.Medium);
        ApplyTextBlockStyle(_romanizationTextBlock, RomanizationFontSize, RomanizationForeground, isActive, InactiveSecondaryOpacity, FontWeight.Medium, FontWeight.Medium);

        _primaryTextBlock.Text = line?.Text ?? string.Empty;
        _translationTextBlock.Text = line?.Translation ?? string.Empty;
        _romanizationTextBlock.Text = line?.Romanization ?? string.Empty;
        _translationTextBlock.Margin = new Thickness(0, SecondaryTopSpacing, 0, 0);
        _translationWordPanel.Margin = new Thickness(0, SecondaryTopSpacing, 0, 0);
        _romanizationTextBlock.Margin = new Thickness(0, SecondaryTopSpacing, 0, 0);

        if (rebuildWords)
        {
            RebuildWordVisuals(_primaryWordPanel, _primaryWordVisuals, line?.Words, PrimaryFontSize, PrimaryForeground, PrimaryPlayedForeground);
            RebuildWordVisuals(_translationWordPanel, _translationWordVisuals, line?.TranslationWords, TranslationFontSize, TranslationForeground, TranslationPlayedForeground);
        }

        var showPrimaryWords = ShowPrimaryText && line is { Words.Count: > 0 } && WordRenderMode != LyricWordRenderMode.Plain;
        _primaryWordPanel.IsVisible = showPrimaryWords;
        _primaryTextBlock.IsVisible = ShowPrimaryText && !showPrimaryWords && !string.IsNullOrWhiteSpace(line?.Text);

        var showTranslationWords = ShowTranslation && line is { TranslationWords.Count: > 0 } && WordRenderMode != LyricWordRenderMode.Plain;
        _translationWordPanel.IsVisible = showTranslationWords;
        _translationTextBlock.IsVisible = ShowTranslation && !showTranslationWords && !string.IsNullOrWhiteSpace(line?.Translation);
        _romanizationTextBlock.IsVisible = ShowRomanization && !string.IsNullOrWhiteSpace(line?.Romanization);

        RefreshWordVisuals(_primaryWordVisuals, Position, isActive, PrimaryForeground, PrimaryPlayedForeground, 0.34d, WordRenderMode == LyricWordRenderMode.LegacyLift);
        RefreshWordVisuals(_translationWordVisuals, Position, isActive, TranslationForeground, TranslationPlayedForeground, 0.4d, WordRenderMode == LyricWordRenderMode.LegacyLift);
    }

    private void RebuildWordVisuals(
        Panel panel,
        List<WordVisual> visuals,
        IReadOnlyList<LyricWord>? words,
        double fontSize,
        IBrush foreground,
        IBrush playedForeground)
    {
        panel.Children.Clear();
        visuals.Clear();

        if (words == null || words.Count == 0)
            return;

        foreach (var word in words)
        {
            WordVisual visual = WordRenderMode switch
            {
                LyricWordRenderMode.Clip => CreateClipWordVisual(word, fontSize, foreground, playedForeground),
                _ => CreatePlainWordVisual(word, fontSize, foreground)
            };

            visuals.Add(visual);
            panel.Children.Add(visual.Control);
        }
    }

    private WordVisual CreateClipWordVisual(LyricWord word, double fontSize, IBrush foreground, IBrush playedForeground)
    {
        var control = new KaraokeTextBlock
        {
            Text = word.Text,
            FontFamily = FontFamily,
            FontSize = fontSize,
            FontWeight = FontWeight.ExtraLight,
            Foreground = foreground,
            PlayedForeground = playedForeground,
            ClipMode = ClipMode,
            UsePlayedGradient = true
        };

        return new WordVisual(word, control);
    }

    private WordVisual CreatePlainWordVisual(LyricWord word, double fontSize, IBrush foreground)
    {
        var transform = new TranslateTransform();
        var control = new TextBlock
        {
            Text = word.Text,
            FontFamily = FontFamily,
            FontSize = fontSize,
            FontWeight = FontWeight.ExtraLight,
            Foreground = foreground,
            RenderTransform = transform
        };

        return new WordVisual(word, control, transform);
    }

    private void RefreshWordVisuals(
        IReadOnlyList<WordVisual> visuals,
        TimeSpan position,
        bool isActive,
        IBrush foreground,
        IBrush playedForeground,
        double unplayedOpacity,
        bool enableLift)
    {
        foreach (var visual in visuals)
        {
            var progress = LyricProgressCalculator.GetProgress(position, visual.Word.Start, visual.Word.Duration);
            var isCurrent = LyricProgressCalculator.IsCurrent(progress);
            var isPlayed = LyricProgressCalculator.IsPlayed(progress);

            switch (visual.Control)
            {
                case KaraokeTextBlock karaoke:
                    karaoke.FontFamily = FontFamily;
                    karaoke.Progress = progress;
                    karaoke.Foreground = foreground;
                    karaoke.PlayedForeground = playedForeground;
                    karaoke.ClipMode = ClipMode;
                    karaoke.UnplayedOpacity = isActive ? unplayedOpacity : Math.Min(0.78d, unplayedOpacity + 0.18d);
                    karaoke.Opacity = isActive ? 1d : 0.92d;
                    break;

                case TextBlock textBlock:
                    textBlock.FontFamily = FontFamily;
                    textBlock.Foreground = foreground;
                    textBlock.Opacity = !isActive
                        ? Math.Min(0.84d, unplayedOpacity + 0.18d)
                        : isCurrent
                            ? 1d
                            : isPlayed
                                ? 0.96d
                                : unplayedOpacity;
                    textBlock.FontWeight = FontWeight.ExtraLight;
                    visual.Transform?.Y = enableLift ? LyricProgressCalculator.GetLiftOffset(progress) : 0d;
                    break;
            }
        }
    }

    private void ApplyTextBlockStyle(
        TextBlock textBlock,
        double fontSize,
        IBrush foreground,
        bool isActive,
        double inactiveOpacity,
        FontWeight inactiveWeight,
        FontWeight activeWeight)
    {
        textBlock.FontSize = fontSize;
        textBlock.FontFamily = FontFamily;
        textBlock.Foreground = foreground;
        textBlock.TextAlignment = TextAlignment;
        textBlock.TextWrapping = TextWrapping.Wrap;
        textBlock.HorizontalAlignment = HorizontalAlignment.Stretch;
        textBlock.Opacity = isActive ? 1d : inactiveOpacity;
        textBlock.FontWeight = isActive ? activeWeight : inactiveWeight;
    }

    private static TextBlock CreateTextBlock()
    {
        return new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    private static AlignedWrapPanel CreateWordPanel()
    {
        return new AlignedWrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    private static HorizontalAlignment ToHorizontalAlignment(TextAlignment textAlignment)
    {
        return textAlignment switch
        {
            TextAlignment.Center => HorizontalAlignment.Center,
            TextAlignment.Right => HorizontalAlignment.Right,
            TextAlignment.End => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Left
        };
    }

    private sealed class WordVisual(LyricWord word, Control control, TranslateTransform? transform = null)
    {
        public LyricWord Word { get; } = word;

        public Control Control { get; } = control;

        public TranslateTransform? Transform { get; } = transform;
    }
}
