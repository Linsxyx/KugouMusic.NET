namespace AvaloniaSilkEffects.Sonnet;

public enum SonnetTextFixedGeoVariant
{
    StraightFrame, RotatedFrame, OrbitCrosshair, SplitArches, OrbHatch, MusicSteps, BentLines,
}

/// <summary>Folia's sonnetTextFixedGeo.ts: deterministic geometry sitting behind ordinary text.</summary>
public static class SonnetTextFixedGeo
{
    private static readonly SonnetTextFixedGeoVariant[] Hollow =
    [
        SonnetTextFixedGeoVariant.StraightFrame, SonnetTextFixedGeoVariant.RotatedFrame,
        SonnetTextFixedGeoVariant.OrbitCrosshair, SonnetTextFixedGeoVariant.SplitArches,
    ];

    private static readonly SonnetTextFixedGeoVariant[] Solid =
    [
        SonnetTextFixedGeoVariant.OrbHatch, SonnetTextFixedGeoVariant.MusicSteps, SonnetTextFixedGeoVariant.BentLines,
    ];

    /// <summary>JS `text.split('').reduce((a, b) => a + b.charCodeAt(0), 0) + segmentIndex * 13`.</summary>
    public static int Seed(string text, int segmentIndex) => text.Sum(character => (int)character) + segmentIndex * 13;

    public static bool IsChorusEffect(int seed, SonnetParagraphKind paragraphKind) =>
        paragraphKind == SonnetParagraphKind.Chorus || seed % 100 < 35;

    public static bool ShouldApply(int seed, bool isChorusEffect) => seed % 100 < (isChorusEffect ? 26 : 15);

    public static SonnetTextFixedGeoVariant ResolvePlan(int seed, bool isChorusEffect)
    {
        if (isChorusEffect)
        {
            var chorusSeed = Mod(seed, 10);
            return chorusSeed < 9 ? HollowVariant(seed, 10, chorusSeed) : Solid[seed / 10 % Solid.Length];
        }
        var legacyType = Mod(seed, 4);
        return legacyType is 1 or 2 ? HollowVariant(seed, 4, legacyType) : Solid[seed / 4 % Solid.Length];
    }

    public static EffectContainer Build(int seed, bool isChorusEffect, float fontSize, float layoutWidth, SonnetTheme theme)
    {
        var plan = ResolvePlan(seed, isChorusEffect);
        var g = new SonnetDrawList();
        var color = Rgb(seed % 2 == 0 ? theme.Primary : theme.Secondary);
        var secondary = Rgb(theme.Secondary);
        var accent = Rgb(theme.Accent);
        var alpha = (isChorusEffect ? 0.4 : 0.25) + seed % 10 * 0.03;
        var scaleMultiplier = isChorusEffect ? 1.5f + seed % 5 * 0.3f : 1;
        var width = Math.Max(fontSize * 2.5f * scaleMultiplier, layoutWidth * 0.12f * scaleMultiplier);
        var height = Math.Max(fontSize * 1.8f * scaleMultiplier, layoutWidth * 0.08f * scaleMultiplier);
        var rotation = 0f;
        switch (plan)
        {
            case SonnetTextFixedGeoVariant.OrbitCrosshair:
            {
                var radius = Math.Min(width, height) * 0.46;
                g.Circle(0, 0, radius).Stroke(color, 1.5, alpha);
                g.Circle(-width * 0.17, 0, radius * 0.72).Stroke(secondary, 1, alpha * 0.72);
                g.Circle(width * 0.17, 0, radius * 0.72).Stroke(secondary, 1, alpha * 0.72);
                g.MoveTo(-width * 0.62, 0).LineTo(width * 0.62, 0).Stroke(color, 1, alpha * 0.64);
                g.MoveTo(0, -height * 0.62).LineTo(0, height * 0.62).Stroke(color, 1, alpha * 0.64);
                break;
            }
            case SonnetTextFixedGeoVariant.SplitArches:
            {
                var halfWidth = width * 0.46;
                var archRadius = Math.Min(width * 0.34, height * 0.52);
                for (var index = 0; index < 2; index++)
                {
                    var x = (index == 0 ? -1 : 1) * halfWidth * 0.42;
                    g.MoveTo(x - archRadius * 0.72, height * 0.42).LineTo(x - archRadius * 0.72, 0)
                        .Arc(x, 0, archRadius * 0.72, Math.PI, 0).LineTo(x + archRadius * 0.72, height * 0.42)
                        .Stroke(index == 0 ? color : secondary, 1.5, alpha);
                    g.MoveTo(x - archRadius * 0.48, height * 0.42).LineTo(x - archRadius * 0.48, 0)
                        .Arc(x, 0, archRadius * 0.48, Math.PI, 0).LineTo(x + archRadius * 0.48, height * 0.42)
                        .Stroke(index == 0 ? secondary : color, 1, alpha * 0.58);
                }
                g.MoveTo(-halfWidth, height * 0.42).LineTo(halfWidth, height * 0.42).Stroke(color, 2, alpha * 0.72);
                break;
            }
            case SonnetTextFixedGeoVariant.StraightFrame or SonnetTextFixedGeoVariant.RotatedFrame:
            {
                var rotated = plan == SonnetTextFixedGeoVariant.RotatedFrame;
                var frameWidth = rotated ? width * 0.8 : width;
                var frameHeight = rotated ? height * 0.8 : height;
                g.Rectangle(-frameWidth / 2, -frameHeight / 2, frameWidth, frameHeight)
                    .Stroke(color, Math.Max(1.5, fontSize * 0.02), alpha);
                if (isChorusEffect && seed % 2 == 0)
                    g.Rectangle(-frameWidth * 0.6, -frameHeight * 0.6, frameWidth * 1.2, frameHeight * 1.2)
                        .Stroke(color, 1, alpha * 0.5);
                if (rotated) rotation = MathF.PI / 4;
                break;
            }
            case SonnetTextFixedGeoVariant.MusicSteps:
            {
                double[] heights = [0.24, 0.35, 0.2, 0.82, 0.3, 0.1, 0.23, 0.16];
                var spacing = width / (heights.Length + 1);
                for (var index = 0; index < heights.Length; index++)
                {
                    var x = -width / 2 + spacing * (index + 1);
                    var baseline = height * (0.12 - index * 0.035);
                    g.MoveTo(x - spacing * 0.12, baseline - height * heights[index] * 0.5)
                        .LineTo(x + spacing * 0.12, baseline + height * heights[index] * 0.5)
                        .Stroke(index % 2 == 0 ? accent : secondary, Math.Max(2, height * 0.025), alpha * 0.52);
                }
                break;
            }
            case SonnetTextFixedGeoVariant.BentLines:
            {
                for (var index = 0; index < 5; index++)
                {
                    var x = -width * 0.34 + index * width * 0.17;
                    g.MoveTo(x - width * 0.16, -height * (0.42 - index * 0.035))
                        .LineTo(x, -height * (0.08 - index * 0.025))
                        .LineTo(x - width * 0.015, height * (0.35 + index * 0.035))
                        .Stroke(index % 2 == 0 ? accent : secondary, Math.Max(2, height * 0.022), alpha * 0.52);
                }
                break;
            }
            default:
            {
                var radius = width * 0.5;
                g.Circle(0, 0, radius).Fill(color, alpha * 0.15);
                var spacing = Math.Max(4, width * 0.05);
                for (var offset = -radius; offset < radius; offset += spacing)
                {
                    var lineHeight = Math.Sqrt(Math.Max(0, radius * radius - offset * offset));
                    g.MoveTo(offset + radius * 0.4, -lineHeight + radius * 0.4)
                        .LineTo(offset + radius * 0.4, lineHeight + radius * 0.4);
                }
                g.Stroke(color, 1.5, alpha * 0.6);
                break;
            }
        }
        var root = g.Replay(48);
        root.Rotation = rotation;
        return root;
    }

    private static SonnetTextFixedGeoVariant HollowVariant(int seed, int divisor, int offset) =>
        Hollow[Mod(seed / divisor + offset, Hollow.Length)];

    private static int Mod(int value, int modulo) => (value % modulo + modulo) % modulo;

    private static uint Rgb(EffectColor color) =>
        (uint)(Math.Clamp((int)MathF.Round(color.R * 255), 0, 255) << 16
            | Math.Clamp((int)MathF.Round(color.G * 255), 0, 255) << 8
            | Math.Clamp((int)MathF.Round(color.B * 255), 0, 255));
}
