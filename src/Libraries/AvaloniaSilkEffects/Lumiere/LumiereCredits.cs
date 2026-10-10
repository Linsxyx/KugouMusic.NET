using System.Numerics;
using AvaloniaSilkEffects.Lumiere.LineArt;
using AvaloniaSilkEffects.Lumiere.Light;
using AvaloniaSilkEffects.Lumiere.Rigs;
using AvaloniaSilkEffects.Lumiere.Text;

namespace AvaloniaSilkEffects.Lumiere;

public sealed record LumiereSongMetadata(string? Title = null, string? Artist = null, string? Album = null)
{
    public bool HasCredits => !string.IsNullOrWhiteSpace(Title) || !string.IsNullOrWhiteSpace(Artist) || !string.IsNullOrWhiteSpace(Album);
}

/// <summary>The credits timeline: lyric layers' alpha and blur, the card's alpha and scale.</summary>
public readonly record struct LumiereCreditsFrame(bool Active, double LyricAlpha, double LyricBlur, double PosterAlpha, double PosterScale)
{
    public static LumiereCreditsFrame Inactive { get; } = new(false, 1, 0, 0, 1);

    private static double Smooth(double value)
    {
        var t = Math.Clamp(value, 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>Lyrics defocus out over 1.6 s (like lights-out); the card fades in from 0.5 s over 1.4 s, settling its scale.</summary>
    public static LumiereCreditsFrame At(double time, double finalEndTime)
    {
        var elapsed = time - finalEndTime;
        if (elapsed <= 0) return new LumiereCreditsFrame(false, 1, 0, 0, 0.985);
        var exit = Smooth(elapsed / 1.6);
        var enter = Smooth((elapsed - 0.5) / 1.4);
        return new LumiereCreditsFrame(true, 1 - exit, exit * 10, enter, 0.985 + 0.015 * enter);
    }
}

/// <summary>
/// Folia credits.ts: after the last line the lyrics defocus out, a top beam falls again, the title lights glyph by glyph in it
/// (the lyric window's engraving, flashes and halos), the artist above and the album below develop slowly; a protractor halo
/// and viewfinder draw themselves. Pure in the time since the credits started.
/// </summary>
internal sealed class LumiereCredits : IDisposable
{
    private const double TitleStart = 1.5;
    private const double TitleSpan = 2.4;
    private const double TitleStep = 0.14;

    private readonly float _width;
    private readonly float _height;
    private readonly LumiereSceneTuning _tuning;
    private readonly LumierePalette _palette;
    private readonly LumiereLightRig _rig;
    private readonly LumiereLineArtLayer _art;
    private readonly LumiereMotes _motes;
    private readonly LumiereLyricWindow? _window;
    private readonly List<(LumiereGlyphLine Line, float Y, Vector3 Color, double Delay)> _details = [];
    private readonly LumiereLightFieldFrame _field = new();
    private double _time;

    public LumiereCredits(LumiereGpu gpu, float width, float height, float resolution, LumiereTheme theme,
        LumiereSongMetadata metadata, LumiereSceneTuning tuning)
    {
        _width = width;
        _height = height;
        _tuning = tuning;
        var aspect = width / (double)height;
        _palette = LumierePalette.Resolve(theme, tuning.ThemeColorMix);
        var title = metadata.Title?.Trim() ?? "";
        var artist = metadata.Artist?.Trim() ?? "";
        var album = metadata.Album?.Trim() ?? "";
        var random = new LumiereRng("lumiere:credits");

        // A top beam falling on the title, and a faint fan around it.
        _rig = new LumiereLightRig(
            [
                LumiereRigBase.Shaft with { Spread = 0.1, Width = 0.06, Length = 0.9, Intensity = 0.85 },
                LumiereRigBase.Shaft with { Spread = 0.3, Width = 0.1, Length = 0.6, Softness = 0.9, Intensity = 0.22, Streaks = 0.85, StreakFreq = 26, Core = 0.3 },
            ],
            LumiereRigBase.Fog with { Density = 0.9 },
            LumiereRigBase.Glare with { Intensity = 0.8 });
        _art = new LumiereLineArtLayer(height, LumiereLineArtSpec.Merge(
            LumiereRecipes.ProtractorHalo(aspect * 0.5, 0.03, 0.3, alpha: 0.55),
            LumiereRecipes.ViewfinderFrame(aspect, 0.16, 0.27, 0.84, 0.7, 0.1, 0.32),
            LumiereRecipes.ScatteredSparks(aspect, 14, random, 0.3)), gpu.Sprites.Star);
        _motes = new LumiereMotes(width, height, "lumiere:credits:motes",
            LumiereRigBase.Motes with { Count = (int)Math.Round(LumiereRigBase.Motes.Count * tuning.MoteAmount) }, gpu.Sprites.Dot);

        if (title.Length > 0)
        {
            // The title as one "lyric" line, glyph by glyph.
            var glyphs = LumiereReveal.Graphemes(title);
            var step = Math.Min(TitleStep, TitleSpan / Math.Max(glyphs.Count, 1));
            var line = new LumiereLine(title, TitleStart, TitleStart + step * glyphs.Count + 0.6,
                [.. glyphs.Select((glyph, i) => new LumiereWord(glyph, TitleStart + i * step, TitleStart + (i + 1) * step))]);
            _window = new LumiereLyricWindow(gpu.Gl, new LumiereLyricWindowOptions
            {
                Width = width,
                Height = height,
                Lines = [line],
                Font = theme.FontFamily,
                Weight = theme.FontWeight,
                Resolution = resolution,
                Region = new LumiereRegion(0.5 * aspect, 0.49, 0.6 * aspect, 0.2),
                HeroPx = 0.1 * height,
                Neighbors = 1,
                Typography = LumiereTypography.Horizontal,
                Decay = new LumiereDecaySpec(0, 1),
                Drift = 0,
                Seed = "lumiere:credits:title",
                LetterSpacing = 0.06,
            });
        }
        var detailSize = Math.Max(13, height * 0.028f);
        var titleDelay = title.Length > 0
            ? TitleStart + Math.Min(TitleSpan, TitleStep * LumiereReveal.Graphemes(title).Count) - 0.6
            : 0;
        if (artist.Length > 0)
            _details.Add((new LumiereGlyphLine(gpu.Gl, artist.ToUpperInvariant(), detailSize, theme.FontFamily, theme.FontWeight, resolution, 0.32f),
                0.35f, _palette.Lit, 0.9 + titleDelay));
        if (album.Length > 0)
            _details.Add((new LumiereGlyphLine(gpu.Gl, album, detailSize * 0.85f, theme.FontFamily, theme.FontWeight, resolution, 0.16f),
                0.65f, _palette.Unlit, 1.2 + titleDelay));
    }

    private static double Smooth(double value)
    {
        var t = Math.Clamp(value, 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <param name="elapsed">Seconds since the credits started.</param>
    public void Update(double elapsed)
    {
        var time = Math.Max(0, elapsed);
        _time = time;
        // The light rises from 0.6 s over 1.6 s; the source flashes as it ignites.
        var rise = Smooth((time - 0.6) / 1.6);
        var ignite = time >= 0.9 ? 1.2 * Math.Exp(-(time - 0.9) / 0.4) : 0;
        _field.Beams.Clear();
        LumiereLight.ResolveBeams(_rig, time, _width / (double)_height,
            new LumiereLightDrive(rise * _tuning.LightIntensity, 0, _palette.Light, time), _field.Beams);
        _field.Rig = _rig;
        _field.Time = time;
        _field.FogScale = _tuning.FogDensity;
        _field.Color = _palette.Light;
        _field.GlareScale = rise * _tuning.LightIntensity * (1 + ignite);
        _field.Dark = Vector4.Zero;
        _field.Octaves = _tuning.FogOctaves;
        _field.TextRegion = null;
    }

    public void Render(LumiereGpu gpu, EffectPrimitiveRenderer primitives, LumiereBinding output, float pixelScale)
    {
        var time = _time;
        var rise = Smooth((time - 0.6) / 1.6);
        LumiereGroups.Render(gpu, primitives, output, pixelScale, Matrix3x2.Identity, _width, _height, 0, _tuning,
            _tuning.TextOnly ? null : (root, viewport) =>
            {
                gpu.LightField.Render(_field, root, viewport, _width, _height, 0);
                var draw = (time - 0.8) / 3.6;
                if (_tuning.LineArt && draw > 0) _art.Draw(primitives, root, time, draw, Smooth((time - 0.8) / 1.2), _field.Beams, _palette.Light);
                _motes.Draw(primitives, root, time, _field.Beams, _palette.Light, rise);
            },
            (root, _) =>
            {
                _window?.Draw(primitives, gpu.Sprites, root, new LumiereWindowFrame(time, _field.Beams, _palette.Lit, _palette.Unlit,
                    _tuning.UnlitOpacity, Smooth((time - 0.4) / 0.8), _tuning.HideTrails));
                foreach (var (line, y, color, delay) in _details)
                {
                    var alpha = (float)(Smooth((time - delay) / 1.4) * 0.9);
                    if (alpha <= 0.002f) continue;
                    var size = new Vector2(line.Width + line.Pad * 2, line.Height + line.Pad * 2);
                    var transform = Matrix3x2.CreateTranslation(_width / 2 - size.X / 2, y * _height - size.Y / 2) * root;
                    primitives.DrawTextureRegion(line.Texture, transform, size, new Vector4(0, 0, 1, 1), alpha, LumiereColor.Tint(color));
                }
            });
    }

    public void Dispose()
    {
        _window?.Dispose();
        foreach (var detail in _details) detail.Line.Dispose();
    }
}
