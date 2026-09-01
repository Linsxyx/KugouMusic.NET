using System.Numerics;

namespace AvaloniaSilkEffects.Sonnet;

// Ported from Folia Sonnet v0.7.2, commit d5b8b24d5c873362f17bb372028afdbc30a4d2b2.
public sealed record SonnetWordTiming(string Text, double StartTime, double EndTime);

public sealed record SonnetLine(
    string FullText,
    double StartTime,
    double EndTime,
    IReadOnlyList<SonnetWordTiming> Words,
    string? SongPart = null,
    int? BlockIndex = null,
    bool IsChorus = false,
    double? RenderEndTime = null);

public sealed record SonnetTheme(
    EffectColor Background,
    EffectColor Primary,
    EffectColor Accent,
    EffectColor Secondary,
    string FontFamily = "PingFang SC",
    int? FontWeight = null,
    SonnetAnimationIntensity AnimationIntensity = SonnetAnimationIntensity.Normal,
    string Name = "",
    string Description = "",
    SonnetFontStyle FontStyle = SonnetFontStyle.Sans,
    IReadOnlyList<string>? FontFamilyStack = null,
    IReadOnlyList<SonnetWordColor>? WordColors = null,
    IReadOnlyList<string>? LyricsIcons = null);

public enum SonnetAnimationIntensity { Calm, Normal, Chaotic }
public enum SonnetFontStyle { Sans, Serif, Mono }
public enum SonnetOuterFrameMode { None, Frame, Full }
public sealed record SonnetWordColor(string Word, EffectColor Color);

public sealed class SonnetTuning
{
    public float CameraIntensity { get; set; } = 1;
    public float TypographyMotion { get; set; } = 1;
    public float MgDensity { get; set; } = 1;
    public bool ShowOnlyText { get; set; }
    public bool ShowGuide { get; set; } = true;
    public bool ShowBackgroundMg { get; set; } = true;
    public bool ShowFixedGeo { get; set; } = true;
    public bool ShowGiantDecorativeText { get; set; } = true;
    public bool ShowBackgroundDecor { get; set; } = true;
    public bool ShowChromaticSplit { get; set; } = true;
    public bool EnableTransitions { get; set; } = true;
    public SonnetOuterFrameMode OuterFrameMode { get; set; } = SonnetOuterFrameMode.Full;
    public float TextureResolution { get; set; } = 1.5f;
    public bool PostProcessEnabled { get; set; }
    public float PostProcessGrain { get; set; } = 0.2f;
    public float PostProcessContrast { get; set; }
    public float PostProcessRgbShift { get; set; }
    public float PostProcessHalftone { get; set; }
    public float PostProcessVignette { get; set; } = 0.85f;
    public float PostProcessLensDistortion { get; set; } = 0.3f;
    public float PostProcessLensDispersion { get; set; } = 0.6f;
}

public sealed record SonnetSongMetadata(string? Title = null, string? Artist = null, string? Album = null);
public sealed record SonnetSongContext(
    string TrackIdentity,
    string Seed,
    SonnetProgram Program,
    SonnetTheme Theme,
    SonnetSongMetadata? Metadata = null);

public enum SonnetSongSwapMode { Immediate, Animated }

public sealed class SonnetSceneOptions
{
    public SonnetTuning Tuning { get; init; } = new();
    public float LyricsFontScale { get; set; } = 1;
    public bool StaticMode { get; set; }
    public bool TransparentBackground { get; set; }
}

public sealed class SonnetModulation
{
    public float CameraScale { get; set; } = 1;
    public float MotionScale { get; set; } = 1;
    public float ParallaxScale { get; set; } = 1;
    public float MgSwimScale { get; set; } = 1;
    public float BreathScale { get; set; } = 1;
    public float GhostScale { get; set; } = 1;
    public float TransitionMotionScale { get; set; } = 1;
    public float TransitionBlurScale { get; set; } = 1;
    public float TransitionGlitchScale { get; set; } = 1;
}

public enum SonnetParagraphKind { Breath, Verse, Lift, Chorus, Break, Outro }
public enum SonnetParagraphBoundary { SongStart, TimeGap, Metadata, DurationCap, LineCap }
public enum SonnetShotKind { EditorialColumn, TypeImpact, FragmentCollage, TrackingRibbon, MaskReveal, PosterBlocks, QuietTableau }
public enum SonnetTransitionKind { FastBlur, MonoGlitch, CameraPull }
public enum SonnetSegmentRole { Hero, SemiHero, Support, Decoration }
public enum SonnetLayoutDirection { Horizontal, Vertical }

public sealed record SonnetGraphemeTiming(string Text, double StartTime, double EndTime, int? WordIndex = null);
public sealed record SonnetSemanticSegment(
    string Text,
    int StartOffset,
    int EndOffset,
    double StartTime,
    double EndTime,
    IReadOnlyList<int> WordIndices,
    IReadOnlyList<SonnetGraphemeTiming> Graphemes,
    bool IsWordLike);

public sealed record SonnetCompiledLine(
    int SourceIndex,
    SonnetLine Line,
    double RenderEndTime,
    IReadOnlyList<SonnetSemanticSegment> Segments);

public sealed record SonnetAnimationCue(
    double At, double Duration, string Kind, int SegmentStart, int SegmentEnd);

public sealed record SonnetCamera(double X, double Y, double Zoom, double Rotation);

public sealed record SonnetShot(
    string Id,
    SonnetShotKind Kind,
    double StartTime,
    double EndTime,
    IReadOnlyList<int> LineIndices,
    IReadOnlyList<SonnetAnimationCue> Cues,
    SonnetCamera Camera);

public sealed record SonnetTransition(SonnetTransitionKind Kind, double StartTime, double EndTime);

public sealed record SonnetParagraph(
    string Id,
    SonnetParagraphKind Kind,
    SonnetParagraphBoundary Boundary,
    double StartTime,
    double EndTime,
    IReadOnlyList<SonnetCompiledLine> Lines,
    IReadOnlyList<SonnetShot> Shots,
    SonnetTransition? TransitionOut);

public sealed record SonnetProgram(string Seed, double ParagraphGapThreshold, IReadOnlyList<SonnetParagraph> Paragraphs)
{
    public const int Version = 1;
}

public sealed record SonnetTypographyPlacement(
    int SegmentIndex,
    string DisplayText,
    SonnetSegmentRole Role,
    float FontScale,
    float MeasuredWidth,
    float MeasuredHeight,
    float X,
    float Y,
    float Rotation,
    float EnterX,
    float EnterY,
    bool Vertical,
    float TimingPhase,
    SonnetLayoutDirection LayoutDirection = SonnetLayoutDirection.Horizontal);

public sealed record SonnetGlyphPlacement(
    string Text,
    Vector2 Position,
    Vector2 Entrance,
    float EntryRotation,
    double StartTime,
    double SettleTime);
