using System;
using System.Collections.Generic;

namespace KugouAvaloniaPlayer.Controls;

internal readonly record struct FumeAudioEnergy(
    double Bass,
    double LowMid,
    double Mid,
    double Vocal,
    double Treble)
{
    public double At(int index) => index switch
    {
        0 => Bass,
        1 => LowMid,
        2 => Mid,
        3 => Vocal,
        _ => Treble
    };
}

internal enum FumeShapeKind
{
    Ring,
    Square,
    Cross,
    Spark
}

internal readonly record struct FumeBackgroundShape(
    FumeShapeKind Kind,
    double X,
    double Y,
    double Size,
    double Rotation,
    double RotationSpeed,
    double Opacity,
    double Depth,
    int AudioBand,
    double StrokeWidth = 1,
    bool IsAccent = false,
    double RingGapStart = -Math.PI * 0.18,
    double RingGapSize = Math.PI * 0.2);

internal readonly record struct FumeFrame(
    FumeArticleLayout Article,
    IReadOnlyList<FumeBackgroundShape> BackgroundShapes,
    double PlaybackSeconds,
    int CurrentLineIndex,
    double ClockSeconds,
    double CameraX,
    double CameraY,
    double CameraScale,
    FumeAudioEnergy Energy,
    double BackgroundObjectOpacity,
    double TextHoldRatio,
    double GlowIntensity,
    string FontFamilyName,
    bool IsOverview);
