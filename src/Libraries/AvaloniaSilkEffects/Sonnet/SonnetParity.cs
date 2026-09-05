namespace AvaloniaSilkEffects.Sonnet;

public readonly record struct SonnetCreditsFrame(
    bool Active, double LyricAlpha, double LyricBlur,
    double PosterAlpha, double PosterOffsetY, double PosterScale);

public static class SonnetCredits
{
    public static SonnetCreditsFrame Resolve(double time, double finalLyricEndTime)
    {
        var elapsed = time - finalLyricEndTime;
        if (elapsed <= 0) return new SonnetCreditsFrame(false, 1, 0, 0, 0.04, 0.965);
        var lyricExit = SonnetMotion.EaseInOut(SonnetMotion.Clamp01(elapsed / 1.25));
        var posterEnter = SonnetMotion.EaseInOut(SonnetMotion.Clamp01((elapsed - 0.38) / 1.55));
        return new SonnetCreditsFrame(true, 1 - lyricExit, lyricExit * 18, posterEnter,
            (1 - posterEnter) * 0.04, 0.965 + posterEnter * 0.035);
    }

    public static bool HasMetadata(SonnetSongMetadata metadata) =>
        !string.IsNullOrWhiteSpace(metadata.Title) ||
        !string.IsNullOrWhiteSpace(metadata.Artist) ||
        !string.IsNullOrWhiteSpace(metadata.Album);
}

public static class SonnetVariantResolver
{
    public const int GeometryVariantCount = 100;
    public const int BackgroundVariantCount = 8;
    public const int FixedGeometryVariantCount = 8;
    public const int BackgroundDecorVariantCount = 6;

    public static int Geometry(int seed) => PositiveModulo(seed, GeometryVariantCount);
    public static int Molecule(int seed) => PositiveModulo((int)Math.Floor(Math.Truncate((double)seed) / GeometryVariantCount), 3);
    public static int HudRotationQuarterTurns(int seed) =>
        PositiveModulo((int)Math.Floor(Math.Truncate((double)seed) / GeometryVariantCount), 4);
    public static int Background(uint seed) => (int)(SonnetRandom.Mix(seed, 0x9e3779b9u) % BackgroundVariantCount);
    public static int FixedGeometry(uint seed) => (int)(SonnetRandom.Mix(seed, 0x85ebca6bu) % FixedGeometryVariantCount);
    public static int BackgroundDecor(uint seed) => (int)(SonnetRandom.Mix(seed, 0xc2b2ae35u) % BackgroundDecorVariantCount);

    private static int PositiveModulo(int value, int modulo) => (value % modulo + modulo) % modulo;
}
