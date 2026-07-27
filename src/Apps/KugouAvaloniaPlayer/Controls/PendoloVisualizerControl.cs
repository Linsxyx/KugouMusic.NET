using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaLyrics;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Controls;

// A direct Avalonia interpretation of Folia's Pendolo clockwork lyric visualizer.
public sealed class PendoloVisualizerControl : Control
{
    private const double ArcAngleRadians = 100d * Math.PI / 180d;

    public static readonly StyledProperty<PlayerViewModel?> PlayerProperty =
        AvaloniaProperty.Register<PendoloVisualizerControl, PlayerViewModel?>(nameof(Player));

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<PendoloVisualizerControl, bool>(nameof(IsActive));

    public static readonly StyledProperty<bool> ShowTranslationProperty =
        AvaloniaProperty.Register<PendoloVisualizerControl, bool>(nameof(ShowTranslation), true);

    public static readonly StyledProperty<bool> ShowRomanizationProperty =
        AvaloniaProperty.Register<PendoloVisualizerControl, bool>(nameof(ShowRomanization));

    public static readonly StyledProperty<FontFamily> LyricFontFamilyProperty =
        AvaloniaProperty.Register<PendoloVisualizerControl, FontFamily>(
            nameof(LyricFontFamily),
            FontFamily.Default);

    public static readonly StyledProperty<Color> BackgroundColorProperty =
        AvaloniaProperty.Register<PendoloVisualizerControl, Color>(
            nameof(BackgroundColor),
            Color.Parse("#243248"));

    public static readonly StyledProperty<Color> PrimaryColorProperty =
        AvaloniaProperty.Register<PendoloVisualizerControl, Color>(
            nameof(PrimaryColor),
            Color.Parse("#D6E1DE"));

    public static readonly StyledProperty<Color> AccentColorProperty =
        AvaloniaProperty.Register<PendoloVisualizerControl, Color>(
            nameof(AccentColor),
            Color.Parse("#9BC8BE"));

    public static readonly StyledProperty<Color> SecondaryColorProperty =
        AvaloniaProperty.Register<PendoloVisualizerControl, Color>(
            nameof(SecondaryColor),
            Color.Parse("#8797A1"));

    private bool _frameQueued;
    private bool _hasFrameTimestamp;
    private TimeSpan _lastFrameTimestamp;
    private double _displayLineIndex = -1;
    private double _lineVelocity;
    private double _clockSeconds;
    private double _smoothedEnergy = 0.15;

    static PendoloVisualizerControl()
    {
        AffectsRender<PendoloVisualizerControl>(
            PlayerProperty,
            ShowTranslationProperty,
            ShowRomanizationProperty,
            LyricFontFamilyProperty,
            BackgroundColorProperty,
            PrimaryColorProperty,
            AccentColorProperty,
            SecondaryColorProperty);
    }

    public PlayerViewModel? Player
    {
        get => GetValue(PlayerProperty);
        set => SetValue(PlayerProperty, value);
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
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

    public FontFamily LyricFontFamily
    {
        get => GetValue(LyricFontFamilyProperty);
        set => SetValue(LyricFontFamilyProperty, value);
    }

    public Color BackgroundColor
    {
        get => GetValue(BackgroundColorProperty);
        set => SetValue(BackgroundColorProperty, value);
    }

    public Color PrimaryColor
    {
        get => GetValue(PrimaryColorProperty);
        set => SetValue(PrimaryColorProperty, value);
    }

    public Color AccentColor
    {
        get => GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    public Color SecondaryColor
    {
        get => GetValue(SecondaryColorProperty);
        set => SetValue(SecondaryColorProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RequestNextFrame();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _frameQueued = false;
        _hasFrameTimestamp = false;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsActiveProperty && change.NewValue is true)
        {
            _hasFrameTimestamp = false;
            RequestNextFrame();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 1 || height <= 1)
            return;

        DrawBackground(context, width, height);

        var player = Player;
        if (player == null)
            return;

        var minDimension = Math.Min(width, height);
        var center = new Point(0, height * 0.5);
        var baseRadius = minDimension * 0.42;
        var lyricRadius = baseRadius + minDimension * 0.06;
        var gearRotation = -_displayLineIndex * Math.PI * 2d / 36d;

        DrawClockwork(context, center, baseRadius, lyricRadius, gearRotation, _smoothedEnergy);
        DrawLyrics(context, player, center, lyricRadius, width, height);
    }

    private void RequestNextFrame()
    {
        if (_frameQueued || !ShouldAnimate())
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;

        _frameQueued = true;
        topLevel.RequestAnimationFrame(OnAnimationFrame);
    }

    private bool ShouldAnimate()
    {
        return IsActive &&
               IsVisible &&
               Bounds.Width > 0 &&
               Bounds.Height > 0 &&
               TopLevel.GetTopLevel(this) != null;
    }

    private void OnAnimationFrame(TimeSpan timestamp)
    {
        _frameQueued = false;
        if (!ShouldAnimate())
        {
            _hasFrameTimestamp = false;
            return;
        }

        var deltaSeconds = _hasFrameTimestamp
            ? Math.Clamp((timestamp - _lastFrameTimestamp).TotalSeconds, 1d / 240d, 0.05d)
            : 1d / 60d;
        _hasFrameTimestamp = true;
        _lastFrameTimestamp = timestamp;

        if (Player?.IsPlayingAudio == true)
            _clockSeconds += deltaSeconds;

        var targetLineIndex = Math.Max(-1, Player?.CurrentLyricIndex ?? -1);
        if (_displayLineIndex < -0.5 && targetLineIndex >= 0)
        {
            _displayLineIndex = targetLineIndex;
            _lineVelocity = 0;
        }
        else
        {
            // Folia's default Pendolo tuning: stiffness 360, damping 20, mass 0.8.
            var displacement = targetLineIndex - _displayLineIndex;
            _lineVelocity += displacement * 450d * deltaSeconds;
            _lineVelocity *= Math.Exp(-25d * deltaSeconds);
            _displayLineIndex += _lineVelocity * deltaSeconds;
        }

        var targetEnergy = ResolveAudioEnergy(Player);
        var response = targetEnergy >= _smoothedEnergy ? 10d : 4d;
        _smoothedEnergy += (targetEnergy - _smoothedEnergy) *
                           (1d - Math.Exp(-response * deltaSeconds));

        InvalidateVisual();
        RequestNextFrame();
    }

    private void DrawBackground(DrawingContext context, double width, double height)
    {
        context.DrawRectangle(new SolidColorBrush(BackgroundColor), null, new Rect(0, 0, width, height));

        var opaqueAtmosphere = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.45, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.55, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Color.Parse("#314959"), 0),
                new GradientStop(Color.Parse("#2C3D53"), 0.44),
                new GradientStop(Color.Parse("#243248"), 0.72),
                new GradientStop(Color.Parse("#202D42"), 1)
            ]
        };
        context.DrawRectangle(opaqueAtmosphere, null, new Rect(0, 0, width, height));

        var centerHaze = new RadialGradientBrush
        {
            Center = new RelativePoint(0.52, 0.45, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.52, 0.45, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.52, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.74, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Color.Parse("#183F4D62"), 0),
                new GradientStop(Color.Parse("#0C8090A0"), 0.42),
                new GradientStop(Colors.Transparent, 1)
            ]
        };
        context.DrawRectangle(centerHaze, null, new Rect(0, 0, width, height));

        DrawAmbientShapes(context, width, height);
    }

    private static void DrawAmbientShapes(DrawingContext context, double width, double height)
    {
        var shapes = new[]
        {
            (X: 0.46, Y: 0.12, Size: 24d, Rotation: 0.78, Alpha: (byte)13),
            (X: 0.61, Y: 0.26, Size: 17d, Rotation: 0.28, Alpha: (byte)11),
            (X: 0.53, Y: 0.52, Size: 39d, Rotation: 0.62, Alpha: (byte)12),
            (X: 0.41, Y: 0.79, Size: 35d, Rotation: 0.18, Alpha: (byte)12),
            (X: 0.72, Y: 0.68, Size: 27d, Rotation: 0.95, Alpha: (byte)7)
        };

        foreach (var shape in shapes)
        {
            var center = new Point(width * shape.X, height * shape.Y);
            var rect = new Rect(
                center.X - shape.Size * 0.5,
                center.Y - shape.Size * 0.5,
                shape.Size,
                shape.Size);
            using (context.PushTransform(Matrix.CreateRotation(shape.Rotation, center)))
            {
                context.DrawRectangle(
                    new SolidColorBrush(Color.FromArgb(shape.Alpha, 190, 210, 215)),
                    null,
                    rect);
            }
        }
    }

    private void DrawClockwork(
        DrawingContext context,
        Point center,
        double baseRadius,
        double lyricRadius,
        double gearRotation,
        double energy)
    {
        var primary10 = WithAlpha(PrimaryColor, 15);
        var primary15 = WithAlpha(PrimaryColor, 23);
        var primary25 = WithAlpha(PrimaryColor, 38);
        var gearPrimary = WithAlpha(PrimaryColor, 64);
        var gearPrimarySubtle = WithAlpha(PrimaryColor, 49);
        var gearAccent = WithAlpha(AccentColor, 89);
        var gearAccentStrong = WithAlpha(AccentColor, 110);

        DrawWatchFace(context, center, baseRadius, gearRotation, primary10, primary15, gearAccent);
        DrawGear(context, center, baseRadius + 8, 36, 10, gearRotation, gearAccentStrong, 2.2, true);
        DrawSpokedWheel(context, center, baseRadius * 0.2, baseRadius * 0.85, 6, gearRotation, gearPrimary, 1.8);

        DrawCircle(context, center, baseRadius * 0.88, primary25, 0.8);
        DrawCircle(context, center, baseRadius * 0.92, primary25, 0.8);

        for (var index = 0; index < 48; index++)
        {
            var angle = gearRotation + index * Math.PI * 2 / 48;
            var innerRadius = baseRadius * (index % 2 == 0 ? 0.62 : 0.683);
            DrawRadialLine(
                context,
                center,
                innerRadius,
                baseRadius * 0.83,
                angle,
                index % 2 == 0 ? primary10 : WithAlpha(PrimaryColor, 11),
                0.7);
        }

        for (var index = 0; index < 12; index++)
        {
            var angle = gearRotation + index * Math.PI * 2 / 12;
            var rivet = PolarPoint(center, baseRadius * 0.96, angle);
            DrawCircle(context, rivet, 2.2, primary25, 0.8);
        }

        DrawGear(context, center, baseRadius * 0.22, 12, 6, -gearRotation * 2.5, gearAccentStrong, 2.1);
        DrawJewel(context, center);

        var orbitRadius = baseRadius * 0.52;
        var planetRadius = baseRadius * 0.16;
        for (var index = 0; index < 3; index++)
        {
            var orbitAngle = gearRotation * 0.4 + index * Math.PI * 2 / 3;
            var planetCenter = PolarPoint(center, orbitRadius, orbitAngle);
            DrawGear(
                context,
                planetCenter,
                planetRadius,
                14,
                5,
                -gearRotation * 3 + index * 0.5,
                WithAlpha(PrimaryColor, 37),
                1.7);
            DrawJewel(context, planetCenter);
        }

        var balanceCenter = new Point(center.X + baseRadius * 0.2, center.Y - baseRadius * 0.75);
        var balanceRadius = baseRadius * 0.28;
        var balancePhase = _clockSeconds * (2.8 + energy * 3.5);
        var balanceGearAngle = balancePhase * 0.1;
        var balanceOscillation = Math.Sin(balancePhase) * (0.15 + energy * 0.7);
        DrawGear(
            context,
            balanceCenter,
            balanceRadius,
            20,
            7,
            balanceGearAngle,
            gearAccentStrong,
            2.1,
            true);
        DrawCircle(context, balanceCenter, balanceRadius * 0.52, gearPrimarySubtle, 1.8);
        DrawHairspring(
            context,
            balanceCenter,
            balanceRadius * 0.56,
            balanceRadius * 0.88,
            3.5,
            balanceOscillation * 0.6 + balanceGearAngle * 0.3,
            WithAlpha(AccentColor, 46));
        DrawJewel(context, balanceCenter);

        var transmissionCenter = new Point(center.X + baseRadius * 0.32, center.Y + baseRadius * 0.78);
        var transmissionRadius = baseRadius * 0.34;
        var transmissionAngle = -gearRotation * 1.4;
        DrawGear(
            context,
            transmissionCenter,
            transmissionRadius,
            24,
            7,
            transmissionAngle,
            gearPrimary,
            1.8);
        DrawSpokedWheel(
            context,
            transmissionCenter,
            transmissionRadius * 0.25,
            transmissionRadius * 0.85,
            5,
            transmissionAngle,
            gearPrimarySubtle,
            1.5);
        DrawGenevaStripes(context, transmissionCenter, transmissionRadius, transmissionAngle, primary10);
        DrawJewel(context, transmissionCenter);

        var secondsCenter = new Point(center.X + baseRadius * 0.68, center.Y + baseRadius * 0.76);
        var secondsRadius = baseRadius * 0.16;
        var secondsAngle = Math.Floor(_clockSeconds) * Math.PI * 2 / 15;
        DrawGear(context, secondsCenter, secondsRadius, 15, 5, secondsAngle, gearAccent, 1.8, true);
        DrawSpokedWheel(
            context,
            secondsCenter,
            secondsRadius * 0.28,
            secondsRadius * 0.76,
            4,
            -secondsAngle,
            gearPrimarySubtle,
            1.3);
        DrawJewel(context, secondsCenter, 0.8);

        var idlerCenter = new Point(
            (transmissionCenter.X + secondsCenter.X) * 0.5 + baseRadius * 0.04,
            (transmissionCenter.Y + secondsCenter.Y) * 0.5 - baseRadius * 0.02);
        var idlerRadius = baseRadius * 0.08;
        DrawGear(context, idlerCenter, idlerRadius, 10, 3.5, gearRotation * 2.2, gearPrimarySubtle, 1.3);
        DrawCircle(context, idlerCenter, idlerRadius * 0.45, primary25, 0.8);
        DrawJewel(context, idlerCenter, 0.65);

        var focalEnd = lyricRadius - Math.Max(14, baseRadius * 0.025);
        var axisPen = new Pen(new SolidColorBrush(gearAccentStrong), 2);
        context.DrawLine(
            axisPen,
            new Point(center.X + baseRadius * 0.8, center.Y),
            new Point(center.X + focalEnd, center.Y));
        DrawArrowHead(context, new Point(center.X + focalEnd, center.Y), gearAccentStrong);
    }

    private void DrawWatchFace(
        DrawingContext context,
        Point center,
        double baseRadius,
        double rotation,
        Color primary10,
        Color primary15,
        Color accent)
    {
        var glass = new RadialGradientBrush
        {
            Center = new RelativePoint(0.36, 0.32, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.3, 0.25, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.7, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.7, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Color.Parse("#78375055"), 0),
                new GradientStop(Color.Parse("#4A2E454D"), 0.58),
                new GradientStop(Color.Parse("#12263742"), 1)
            ]
        };
        context.DrawEllipse(glass, null, center, baseRadius * 1.15, baseRadius * 1.15);

        foreach (var multiplier in new[] { 0.3, 0.6, 0.85, 1.15, 1.4 })
            DrawCircle(context, center, baseRadius * multiplier, primary15, 1);

        for (var index = 0; index < 60; index++)
        {
            var angle = index * Math.PI * 2 / 60 + rotation * 0.2;
            var major = index % 5 == 0;
            DrawRadialLine(
                context,
                center,
                baseRadius * 1.15,
                baseRadius * 1.15 + (major ? 12 : 6),
                angle,
                major ? accent : primary10,
                major ? 1.5 : 1);
        }
    }

    private void DrawLyrics(
        DrawingContext context,
        PlayerViewModel player,
        Point center,
        double lyricRadius,
        double width,
        double height)
    {
        var lines = player.RenderLyricLines;
        if (lines.Count == 0)
        {
            DrawWaitingText(context, center, lyricRadius);
            return;
        }

        var focusIndex = player.CurrentLyricIndex >= 0
            ? player.CurrentLyricIndex
            : Math.Clamp((int)Math.Round(_displayLineIndex), 0, lines.Count - 1);
        var first = Math.Max(0, focusIndex - 5);
        var last = Math.Min(lines.Count - 1, focusIndex + 5);
        var angleStep = ArcAngleRadians / 8d;
        var baseFontSize = Math.Clamp(Math.Min(width, height) * 0.029, 20, 29);

        for (var index = first; index <= last; index++)
        {
            var offset = index - _displayLineIndex;
            var angle = offset * angleStep;
            if (Math.Abs(angle) > 110d * Math.PI / 180d)
                continue;

            var distance = Math.Abs(offset);
            var scale = index == focusIndex
                ? 1.25
                : Math.Max(0.7, 1 - distance * 0.08);
            var curveAlpha = Math.Pow(Math.Max(0, Math.Cos(angle * 0.75)), 2.5);
            var opacity = Math.Clamp(curveAlpha * (1 - distance * 0.18), 0.12, 1);
            var anchor = PolarPoint(center, lyricRadius, angle);
            var maxTextWidth = Math.Max(150, width - anchor.X - 34);
            var line = lines[index];
            var fontSize = baseFontSize * scale;
            var typeface = new Typeface(
                LyricFontFamily,
                FontStyle.Normal,
                index == focusIndex ? FontWeight.SemiBold : FontWeight.Normal);
            var lineColor = index == focusIndex
                ? WithAlpha(PrimaryColor, 133)
                : WithAlpha(PrimaryColor, (byte)Math.Round(opacity * 255));
            var text = new FormattedText(
                line.Text,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                new SolidColorBrush(lineColor))
            {
                MaxTextWidth = maxTextWidth,
                TextAlignment = TextAlignment.Left
            };

            var origin = new Point(
                anchor.X,
                Math.Clamp(anchor.Y - text.Height * 0.5, 20, Math.Max(20, height - text.Height - 20)));
            var lineRotation = angle * 0.35;
            using (context.PushTransform(Matrix.CreateRotation(lineRotation, anchor)))
            {
                context.DrawText(text, origin);

                if (index == focusIndex)
                {
                    DrawActiveSweep(context, text, origin, line, player.CurrentPositionSeconds, typeface, fontSize);
                    DrawAlternateText(context, line, origin, text.Height, maxTextWidth);
                }
            }
        }
    }

    private void DrawActiveSweep(
        DrawingContext context,
        FormattedText text,
        Point origin,
        LyricLine line,
        double playbackSeconds,
        Typeface typeface,
        double fontSize)
    {
        var sweepWidth = ResolveSweepWidth(line, playbackSeconds, typeface, fontSize, text);
        if (sweepWidth <= 0)
            return;

        var sungColor = Mix(PrimaryColor, AccentColor, 0.58, 255);
        text.SetForegroundBrush(new SolidColorBrush(sungColor));
        using (context.PushClip(new Rect(origin.X, origin.Y, sweepWidth, text.Height + 2)))
        {
            context.DrawText(text, origin);
        }
    }

    private double ResolveSweepWidth(
        LyricLine line,
        double playbackSeconds,
        Typeface typeface,
        double fontSize,
        FormattedText fullText)
    {
        if (line.Words.Count == 0)
        {
            var duration = Math.Max(0.08, line.Duration.TotalSeconds);
            var progress = Math.Clamp((playbackSeconds - line.Start.TotalSeconds) / duration, 0, 1);
            return fullText.WidthIncludingTrailingWhitespace * progress;
        }

        var characterProgress = 0d;
        var searchStart = 0;
        foreach (var word in line.Words)
        {
            var wordStart = line.Text.IndexOf(word.Text, searchStart, StringComparison.Ordinal);
            if (wordStart < 0)
                wordStart = searchStart;

            var wordEnd = Math.Min(line.Text.Length, wordStart + word.Text.Length);
            var startSeconds = word.Start.TotalSeconds;
            var endSeconds = startSeconds + Math.Max(0.02, word.Duration.TotalSeconds);
            if (playbackSeconds >= endSeconds)
            {
                characterProgress = wordEnd;
                searchStart = wordEnd;
                continue;
            }

            if (playbackSeconds > startSeconds)
            {
                var localProgress = Math.Clamp((playbackSeconds - startSeconds) / (endSeconds - startSeconds), 0, 1);
                characterProgress = wordStart + word.Text.Length * localProgress;
            }
            break;
        }

        var wholeCharacters = Math.Clamp((int)Math.Floor(characterProgress), 0, line.Text.Length);
        var fractionalCharacter = characterProgress - wholeCharacters;
        var prefix = line.Text[..wholeCharacters];
        var prefixText = new FormattedText(
            prefix,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.White);
        var width = prefixText.WidthIncludingTrailingWhitespace;

        if (fractionalCharacter > 0 && wholeCharacters < line.Text.Length)
        {
            var nextText = new FormattedText(
                line.Text[..(wholeCharacters + 1)],
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                Brushes.White);
            width += (nextText.WidthIncludingTrailingWhitespace - width) * fractionalCharacter;
        }

        return Math.Clamp(width, 0, fullText.WidthIncludingTrailingWhitespace);
    }

    private void DrawAlternateText(
        DrawingContext context,
        LyricLine line,
        Point origin,
        double primaryHeight,
        double maxTextWidth)
    {
        var alternate = ShowTranslation && !string.IsNullOrWhiteSpace(line.Translation)
            ? line.Translation
            : ShowRomanization && !string.IsNullOrWhiteSpace(line.Romanization)
                ? line.Romanization
                : null;
        if (alternate == null)
            return;

        var text = new FormattedText(
            alternate,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(LyricFontFamily, FontStyle.Normal, FontWeight.Normal),
            13,
            new SolidColorBrush(WithAlpha(SecondaryColor, 205)))
        {
            MaxTextWidth = maxTextWidth,
            TextAlignment = TextAlignment.Left
        };
        context.DrawText(text, new Point(origin.X, origin.Y + primaryHeight + 7));
    }

    private void DrawWaitingText(DrawingContext context, Point center, double lyricRadius)
    {
        var text = new FormattedText(
            "等待歌词时序",
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(LyricFontFamily, FontStyle.Normal, FontWeight.Normal),
            22,
            new SolidColorBrush(WithAlpha(SecondaryColor, 180)));
        context.DrawText(text, new Point(center.X + lyricRadius, center.Y - text.Height / 2));
    }

    private static void DrawGear(
        DrawingContext context,
        Point center,
        double radius,
        int teeth,
        double toothDepth,
        double rotation,
        Color color,
        double lineWidth,
        bool fill = false)
    {
        var pen = new Pen(new SolidColorBrush(color), lineWidth);
        var fillBrush = fill ? new SolidColorBrush(WithAlpha(color, 15)) : null;
        var innerRadius = Math.Max(2, radius - toothDepth);
        var points = new Point[teeth * 4];

        for (var index = 0; index < teeth; index++)
        {
            var baseAngle = rotation + index * Math.PI * 2 / teeth;
            var anglePerTooth = Math.PI * 2 / teeth;
            points[index * 4] = PolarPoint(center, innerRadius, baseAngle - anglePerTooth * 0.22);
            points[index * 4 + 1] = PolarPoint(center, radius, baseAngle - anglePerTooth * 0.12);
            points[index * 4 + 2] = PolarPoint(center, radius, baseAngle + anglePerTooth * 0.12);
            points[index * 4 + 3] = PolarPoint(center, innerRadius, baseAngle + anglePerTooth * 0.22);
        }

        if (fillBrush != null)
        {
            // The translucent inner plate is enough to give the wireframe assembly depth.
            context.DrawEllipse(fillBrush, null, center, innerRadius, innerRadius);
        }

        for (var index = 0; index < points.Length; index++)
            context.DrawLine(pen, points[index], points[(index + 1) % points.Length]);
    }

    private static void DrawSpokedWheel(
        DrawingContext context,
        Point center,
        double hubRadius,
        double rimRadius,
        int spokeCount,
        double rotation,
        Color color,
        double lineWidth)
    {
        DrawCircle(context, center, hubRadius, color, lineWidth);
        DrawCircle(context, center, rimRadius, color, lineWidth);
        var pen = new Pen(new SolidColorBrush(color), lineWidth);
        var middleRadius = (hubRadius + rimRadius) * 0.5;
        var holeRadius = (rimRadius - hubRadius) * 0.22;

        for (var index = 0; index < spokeCount; index++)
        {
            var angle = rotation + index * Math.PI * 2 / spokeCount;
            context.DrawLine(
                pen,
                PolarPoint(center, hubRadius, angle),
                PolarPoint(center, rimRadius, angle));
            DrawCircle(
                context,
                PolarPoint(center, middleRadius, angle + Math.PI / spokeCount),
                holeRadius,
                color,
                lineWidth);
        }
    }

    private static void DrawHairspring(
        DrawingContext context,
        Point center,
        double startRadius,
        double endRadius,
        double coils,
        double rotation,
        Color color)
    {
        var pen = new Pen(new SolidColorBrush(color), 0.8);
        var steps = (int)(coils * 60);
        var previous = PolarPoint(center, startRadius, rotation);
        for (var index = 1; index <= steps; index++)
        {
            var progress = index / (double)steps;
            var angle = rotation + progress * coils * Math.PI * 2;
            var radius = startRadius + (endRadius - startRadius) * Math.Pow(progress, 0.9);
            var current = PolarPoint(center, radius, angle);
            context.DrawLine(pen, previous, current);
            previous = current;
        }
    }

    private static void DrawGenevaStripes(
        DrawingContext context,
        Point center,
        double radius,
        double rotation,
        Color color)
    {
        using (context.PushTransform(Matrix.CreateRotation(rotation, center)))
        {
            var pen = new Pen(new SolidColorBrush(color), 0.7);
            var span = radius * 1.6;
            for (var index = 1; index <= 9; index++)
            {
                var localY = -span * 0.5 + index * span / 10;
                var halfWidth = Math.Sqrt(Math.Max(0, radius * radius * 0.64 - localY * localY));
                context.DrawLine(
                    pen,
                    new Point(center.X - halfWidth, center.Y + localY),
                    new Point(center.X + halfWidth, center.Y + localY));
            }
        }
    }

    private void DrawJewel(DrawingContext context, Point center, double scale = 1)
    {
        DrawCircle(context, center, 4.5 * scale, WithAlpha(AccentColor, 166), 1.1);
        DrawCircle(context, center, 2.5 * scale, WithAlpha(AccentColor, 72), 0.8);
    }

    private static void DrawArrowHead(DrawingContext context, Point tipBase, Color color)
    {
        var pen = new Pen(new SolidColorBrush(color), 2);
        context.DrawLine(pen, new Point(tipBase.X, tipBase.Y - 4), new Point(tipBase.X + 8, tipBase.Y));
        context.DrawLine(pen, new Point(tipBase.X + 8, tipBase.Y), new Point(tipBase.X, tipBase.Y + 4));
        context.DrawLine(pen, new Point(tipBase.X, tipBase.Y + 4), new Point(tipBase.X, tipBase.Y - 4));
    }

    private static void DrawCircle(
        DrawingContext context,
        Point center,
        double radius,
        Color color,
        double lineWidth)
    {
        context.DrawEllipse(
            null,
            new Pen(new SolidColorBrush(color), lineWidth),
            center,
            radius,
            radius);
    }

    private static void DrawRadialLine(
        DrawingContext context,
        Point center,
        double startRadius,
        double endRadius,
        double angle,
        Color color,
        double width)
    {
        context.DrawLine(
            new Pen(new SolidColorBrush(color), width),
            PolarPoint(center, startRadius, angle),
            PolarPoint(center, endRadius, angle));
    }

    private static Point PolarPoint(Point center, double radius, double angle)
    {
        return new Point(
            center.X + Math.Cos(angle) * radius,
            center.Y + Math.Sin(angle) * radius);
    }

    private static double ResolveAudioEnergy(PlayerViewModel? player)
    {
        var bars = player?.NowPlayingVisualizerBars;
        if (bars == null || bars.Length == 0)
            return 0;

        var count = Math.Min(22, bars.Length);
        var total = 0d;
        for (var index = 0; index < count; index++)
            total += Math.Clamp((bars[index].Height - 6d) / 170d, 0d, 1d);

        return Math.Clamp(total / count, 0d, 1d);
    }

    private static Color Mix(Color first, Color second, double amount, byte alpha)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            alpha,
            (byte)Math.Round(first.R + (second.R - first.R) * amount),
            (byte)Math.Round(first.G + (second.G - first.G) * amount),
            (byte)Math.Round(first.B + (second.B - first.B) * amount));
    }

    private static Color WithAlpha(Color color, byte alpha)
    {
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }
}
