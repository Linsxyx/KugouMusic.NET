using System.Numerics;

namespace AvaloniaSilkEffects.Sonnet;

// Ported from Folia v0.7.2 (d5b8b24d): sonnetThemedShotMg.ts and its
// flora, botanical, architecture, landscape and primitive modules.
internal static partial class SonnetMgBuilder
{
    private static void BuildThemedGeometry(
        EffectContainer root,
        SonnetTheme theme,
        float width,
        float height,
        float radius,
        uint seed,
        int variant)
    {
        switch (variant)
        {
            case 24: BuildCamellia(root, theme, radius, seed); break;
            case 25: BuildTulipField(root, theme, radius, seed); break;
            case 26: BuildWildflower(root, theme, radius, seed); break;
            case 27: BuildFern(root, theme, radius, seed); break;
            case 28: BuildGinkgo(root, theme, radius, seed); break;
            case 29: BuildClimbingVine(root, theme, radius, seed); break;
            case 30: BuildGreenhouse(root, theme, width, height, radius, seed); break;
            case 31: BuildPagoda(root, theme, width, height, radius, seed); break;
            case 32: BuildCityFacade(root, theme, width, height, radius, seed); break;
            case 33: BuildTerraces(root, theme, width, height, radius, seed); break;
            case 34: BuildMountainLake(root, theme, width, height, radius, seed); break;
            case 35: BuildCoastalCliff(root, theme, width, height, radius, seed); break;
        }
    }

    private static void BuildCamellia(EffectContainer root, SonnetTheme theme, float radius, uint seed)
    {
        var turn = seed % 12 * MathF.PI / 72;
        for (var ring = 0; ring < 3; ring++)
        {
            var count = 7 + ring * 4;
            for (var index = 0; index < count; index++)
            {
                var angle = turn + index / (float)count * MathF.Tau + ring * 0.12f;
                ThemedLeaf(root, Vector2.Zero, radius * (0.28f + ring * 0.15f),
                    radius * (0.075f + ring * 0.018f), angle,
                    ring == 1 ? theme.Secondary : theme.Primary, 0.07f + ring * 0.045f);
            }
        }
        AddCircle(root, Vector2.Zero, radius * 0.1f, theme.Secondary with { A = 0.22f });
        AddRing(root, Vector2.Zero, radius * 0.13f, theme.Primary with { A = 0.68f }, 3);
    }

    private static void BuildTulipField(EffectContainer root, SonnetTheme theme, float radius, uint seed)
    {
        var direction = seed % 2 == 0 ? 1f : -1f;
        for (var index = 0; index < 7; index++)
        {
            var x = (-0.66f + index * 0.22f) * radius;
            var top = (-0.3f + (seed + (uint)index * 5) % 5 * 0.085f) * radius;
            var bottom = radius * 0.68f;
            AddPolyline(root, ThemedCubic(new Vector2(x, bottom),
                new Vector2(x + radius * 0.04f * direction, radius * 0.28f),
                new Vector2(x - radius * 0.05f * direction, top + radius * 0.12f), new Vector2(x, top)),
                2, (index % 2 != 0 ? theme.Secondary : theme.Primary) with { A = 0.5f });
            ThemedLeaf(root, new Vector2(x, radius * 0.24f), radius * 0.26f, radius * 0.055f,
                index % 2 != 0 ? -2.7f : -0.45f, theme.Primary, 0.1f);

            var bloomColor = index % 3 == 0 ? theme.Secondary : theme.Primary;
            var bloom = new List<Vector2> { new(x, top + radius * 0.14f) };
            ThemedAppendQuadratic(bloom, new Vector2(x - radius * 0.18f, top - radius * 0.04f),
                new Vector2(x - radius * 0.11f, top - radius * 0.2f));
            bloom.Add(new Vector2(x, top - radius * 0.1f));
            bloom.Add(new Vector2(x + radius * 0.11f, top - radius * 0.2f));
            ThemedAppendQuadratic(bloom, new Vector2(x + radius * 0.18f, top - radius * 0.04f),
                new Vector2(x, top + radius * 0.14f));
            AddFillPolygon(root, bloom, bloomColor with { A = 0.12f + index % 3 * 0.045f });
            AddPolyline(root, bloom, 2, bloomColor with { A = 0.65f });
        }
    }

    private static void BuildWildflower(EffectContainer root, SonnetTheme theme, float radius, uint seed)
    {
        for (var stem = 0; stem < 9; stem++)
        {
            var x = (-0.72f + stem * 0.18f) * radius;
            var lean = ((int)((seed + (uint)stem * 7) % 9) - 4) * radius * 0.018f;
            var flowerY = (-0.45f + (seed + (uint)stem * 3) % 6 * 0.08f) * radius;
            AddPolyline(root, ThemedQuadratic(new Vector2(x, radius * 0.72f),
                new Vector2(x - lean, radius * 0.12f), new Vector2(x + lean, flowerY)), 1.5f,
                (stem % 2 != 0 ? theme.Secondary : theme.Primary) with { A = 0.42f });
            for (var petal = 0; petal < 5; petal++)
            {
                var angle = petal / 5f * MathF.Tau - MathF.PI / 2;
                ThemedLeaf(root, new Vector2(x + lean, flowerY), radius * 0.105f, radius * 0.032f,
                    angle, stem % 3 != 0 ? theme.Primary : theme.Secondary, 0.1f);
            }
            AddCircle(root, new Vector2(x + lean, flowerY), radius * 0.025f, theme.Secondary with { A = 0.48f });
        }
    }

    private static void BuildFern(EffectContainer root, SonnetTheme theme, float radius, uint seed)
    {
        var tilt = (seed % 2 != 0 ? 1 : -1) * 0.18f;
        AddPolyline(root, ThemedCubic(new Vector2(-radius * 0.12f, radius * 0.72f),
            new Vector2(-radius * 0.04f, radius * 0.2f), new Vector2(radius * 0.12f, -radius * 0.24f),
            new Vector2(radius * 0.02f, -radius * 0.72f)), 3, theme.Primary with { A = 0.62f });
        for (var index = 0; index < 13; index++)
        {
            var ratio = index / 13f;
            var origin = new Vector2(-radius * 0.12f + radius * 0.14f * ratio,
                radius * (0.63f - ratio * 1.23f));
            var length = radius * (0.3f - MathF.Abs(ratio - 0.5f) * 0.18f);
            ThemedLeaf(root, origin, length, length * 0.22f, MathF.PI + tilt - ratio * 0.25f,
                index % 3 != 0 ? theme.Primary : theme.Secondary, 0.09f + ratio * 0.05f);
            ThemedLeaf(root, origin, length, length * 0.22f, -tilt + ratio * 0.25f,
                index % 3 != 0 ? theme.Secondary : theme.Primary, 0.07f + ratio * 0.04f);
        }
    }

    private static void BuildGinkgo(EffectContainer root, SonnetTheme theme, float radius, uint seed)
    {
        var direction = seed % 2 == 0 ? 1f : -1f;
        AddPolyline(root, ThemedCubic(new Vector2(-radius * 0.72f * direction, radius * 0.55f),
            new Vector2(-radius * 0.25f * direction, radius * 0.16f),
            new Vector2(radius * 0.08f * direction, -radius * 0.12f),
            new Vector2(radius * 0.65f * direction, -radius * 0.5f)), 5, theme.Primary with { A = 0.38f });
        for (var index = 0; index < 8; index++)
        {
            var ratio = index / 7f;
            var origin = new Vector2((-0.58f + ratio * 1.12f) * radius * direction,
                (0.4f - ratio * 0.78f + MathF.Sin(index * 1.8f) * 0.08f) * radius);
            var angle = -1.1f + index % 3 * 0.7f;
            var size = radius * (0.13f + index % 4 * 0.018f);
            var center = origin + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * size;
            AddLine(root, origin, origin + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * size * 0.7f,
                1.5f, theme.Secondary with { A = 0.42f });
            var fan = new List<Vector2> { center };
            ThemedAppendArc(fan, center, size, angle + MathF.PI * 0.1f, angle + MathF.PI * 0.9f);
            AddFillPolygon(root, fan, (index % 2 != 0 ? theme.Primary : theme.Secondary) with
                { A = 0.09f + index % 3 * 0.04f });
            AddPolyline(root, fan.Concat([center]).ToArray(), 1.5f, theme.Primary with { A = 0.58f });
        }
    }

    private static void BuildClimbingVine(EffectContainer root, SonnetTheme theme, float radius, uint seed)
    {
        var mirror = seed % 2 == 0 ? 1f : -1f;
        for (var vine = 0; vine < 3; vine++)
        {
            var offset = (vine - 1) * radius * 0.3f;
            AddPolyline(root, ThemedCubic(new Vector2(offset, radius * 0.76f),
                new Vector2(offset + radius * 0.5f * mirror, radius * 0.38f),
                new Vector2(offset - radius * 0.48f * mirror, -radius * 0.1f),
                new Vector2(offset + radius * 0.22f * mirror, -radius * 0.76f)),
                vine == 1 ? 3 : 1.5f,
                (vine == 1 ? theme.Secondary : theme.Primary) with { A = 0.46f });
            for (var leaf = 0; leaf < 5; leaf++)
            {
                var ratio = (leaf + 1) / 6f;
                var origin = new Vector2(offset + MathF.Sin(ratio * MathF.PI * 4 + vine) * radius * 0.16f,
                    radius * (0.7f - ratio * 1.36f));
                ThemedLeaf(root, origin, radius * 0.2f, radius * 0.055f,
                    leaf % 2 != 0 ? -0.25f : MathF.PI + 0.25f,
                    leaf % 2 != 0 ? theme.Secondary : theme.Primary, 0.08f + vine * 0.035f);
            }
        }
    }

    private static void BuildGreenhouse(EffectContainer root, SonnetTheme theme,
        float width, float height, float radius, uint seed)
    {
        var direction = seed % 2 == 0 ? 1f : -1f;
        var bleed = ThemedBleed(width, height, radius);
        Vector2[] shell = [new(-radius * 0.7f, radius * 0.58f), new(-radius * 0.7f, -radius * 0.12f),
            new(0, -radius * 0.62f), new(radius * 0.7f, -radius * 0.12f), new(radius * 0.7f, radius * 0.58f)];
        ThemedFillAndStroke(root, shell, theme.Primary, 0.055f, 0.68f, 3);
        AddLine(root, new Vector2(0, -radius * 0.62f), new Vector2(0, radius * 0.58f), 2, theme.Secondary with { A = 0.5f });
        for (var pane = -3; pane <= 3; pane++)
        {
            var x = pane * radius * 0.18f;
            AddLine(root, new Vector2(x, radius * 0.58f), new Vector2(x * 0.38f, -radius * (0.58f - Math.Abs(pane) * 0.04f)),
                1, (pane % 2 != 0 ? theme.Secondary : theme.Primary) with { A = 0.32f });
        }
        var doorX = radius * 0.2f * direction;
        AddRect(root, new Vector2(doorX - radius * 0.13f, radius * 0.08f), new Vector2(radius * 0.26f, radius * 0.5f),
            theme.Secondary with { A = 0.1f });
        AddOutlineRect(root, new Vector2(doorX - radius * 0.13f, radius * 0.08f), new Vector2(radius * 0.26f, radius * 0.5f),
            2, theme.Secondary with { A = 0.62f });
        AddLine(root, new Vector2(-bleed.X, radius * 0.58f), new Vector2(-radius * 0.7f, radius * 0.58f), 1,
            theme.Primary with { A = 0.3f });
        AddLine(root, new Vector2(radius * 0.7f, radius * 0.58f), new Vector2(bleed.X, radius * 0.58f), 1,
            theme.Primary with { A = 0.3f });
    }

    private static void BuildPagoda(EffectContainer root, SonnetTheme theme,
        float width, float height, float radius, uint seed)
    {
        var lean = seed % 2 == 0 ? 1f : -1f;
        var bleed = ThemedBleed(width, height, radius);
        for (var floor = 0; floor < 4; floor++)
        {
            var y = radius * (0.47f - floor * 0.27f);
            var halfWidth = radius * (0.5f - floor * 0.075f);
            Vector2[] roof = [new(-halfWidth * 1.18f, y), new(-halfWidth, y - radius * 0.11f),
                new(0, y - radius * 0.19f), new(halfWidth, y - radius * 0.11f), new(halfWidth * 1.18f, y)];
            var color = floor % 2 != 0 ? theme.Secondary : theme.Primary;
            ThemedFillAndStroke(root, roof, color, 0.07f + floor * 0.025f, 0.58f, 2);
            var bodyPosition = new Vector2(-halfWidth * 0.68f, y);
            var bodySize = new Vector2(halfWidth * 1.36f, radius * 0.17f);
            AddRect(root, bodyPosition, bodySize, theme.Primary with { A = 0.035f + floor * 0.018f });
            AddOutlineRect(root, bodyPosition, bodySize, 1, theme.Primary with { A = 0.36f });
        }
        AddLine(root, new Vector2(0, -radius * 0.62f), new Vector2(radius * 0.035f * lean, -radius * 0.78f), 3,
            theme.Secondary with { A = 0.65f });
        AddLine(root, new Vector2(-bleed.X, radius * 0.64f), new Vector2(-radius * 0.52f, radius * 0.64f), 1,
            theme.Secondary with { A = 0.22f });
        AddLine(root, new Vector2(radius * 0.52f, radius * 0.64f), new Vector2(bleed.X, radius * 0.64f), 1,
            theme.Secondary with { A = 0.22f });
    }

    private static void BuildCityFacade(EffectContainer root, SonnetTheme theme,
        float width, float height, float radius, uint seed)
    {
        var bleed = ThemedBleed(width, height, radius);
        float[] heights = [0.52f, 0.88f, 0.66f, 1.08f, 0.74f, 0.94f, 0.58f];
        var buildingWidth = radius * 0.205f;
        for (var index = 0; index < heights.Length; index++)
        {
            var heightRatio = heights[index];
            var x = radius * (-0.73f + index * 0.24f);
            var buildingHeight = radius * heightRatio;
            var color = index % 3 == 1 ? theme.Secondary : theme.Primary;
            var position = new Vector2(x, radius * 0.62f - buildingHeight);
            var size = new Vector2(buildingWidth, buildingHeight);
            AddRect(root, position, size, color with { A = 0.045f + index % 3 * 0.035f });
            AddOutlineRect(root, position, size, index == 3 ? 3 : 1.5f, color with { A = 0.5f });
            for (var row = 0; row < MathF.Floor(heightRatio * 6); row++)
            for (var column = 0; column < 2; column++)
            {
                if (((long)row + column + index + seed) % 3 != 0) continue;
                AddRect(root, new Vector2(x + radius * (0.035f + column * 0.085f),
                        radius * 0.53f - buildingHeight + row * radius * 0.13f),
                    new Vector2(radius * 0.045f, radius * 0.055f),
                    (column != 0 ? theme.Secondary : theme.Primary) with { A = 0.2f });
            }
        }
        AddLine(root, new Vector2(-bleed.X, radius * 0.63f), new Vector2(bleed.X, radius * 0.63f), 4,
            theme.Primary with { A = 0.48f });
    }

    private static void BuildTerraces(EffectContainer root, SonnetTheme theme,
        float width, float height, float radius, uint seed)
    {
        var direction = seed % 2 == 0 ? 1f : -1f;
        var bleed = ThemedBleed(width, height, radius);
        for (var band = 0; band < 7; band++)
        {
            var y = radius * (-0.5f + band * 0.16f);
            var amplitude = radius * (0.09f + band * 0.012f);
            var top = ThemedCubic(new Vector2(-bleed.X, y), new Vector2(-radius * 0.35f, y + amplitude * direction),
                new Vector2(radius * 0.08f, y - amplitude * direction), new Vector2(bleed.X, y + amplitude * 0.35f));
            var area = top.ToList();
            area.Add(new Vector2(bleed.X, y + radius * 0.12f));
            var bottom = ThemedCubic(area[^1], new Vector2(radius * 0.12f, y + radius * 0.04f),
                new Vector2(-radius * 0.3f, y + radius * 0.2f), new Vector2(-bleed.X, y + radius * 0.12f));
            area.AddRange(bottom.Skip(1));
            var color = band % 2 != 0 ? theme.Secondary : theme.Primary;
            AddFillPolygon(root, area, color with { A = 0.025f + band * 0.018f });
            AddPolyline(root, top, band % 3 == 0 ? 2.5f : 1, color with { A = 0.34f + band * 0.04f });
        }
    }

    private static void BuildMountainLake(EffectContainer root, SonnetTheme theme,
        float width, float height, float radius, uint seed)
    {
        var bleed = ThemedBleed(width, height, radius);
        var shift = ((int)(seed % 7) - 3) / 3f * radius * 0.04f;
        Vector2[] back = [new(-bleed.X, radius * 0.1f), new(-radius * 0.42f, -radius * 0.46f),
            new(-radius * 0.14f, -radius * 0.16f), new(radius * 0.22f, -radius * 0.62f), new(bleed.X, radius * 0.1f)];
        Vector2[] front = [new(-bleed.X, radius * 0.22f), new(-radius * 0.28f + shift, -radius * 0.2f),
            new(radius * 0.06f, radius * 0.05f), new(radius * 0.48f + shift, -radius * 0.28f), new(bleed.X, radius * 0.22f)];
        ThemedFillAndStroke(root, back, theme.Secondary, 0.07f, 0.46f, 1.5f);
        ThemedFillAndStroke(root, front, theme.Primary, 0.12f, 0.66f, 2.5f);
        for (var line = 0; line < 7; line++)
        {
            var y = radius * (0.28f + line * 0.07f);
            var inset = radius * (0.08f + line % 3 * 0.08f);
            AddLine(root, new Vector2(-bleed.X + inset, y), new Vector2(bleed.X - inset, y), 1,
                (line % 2 != 0 ? theme.Secondary : theme.Primary) with { A = 0.2f + line * 0.035f });
        }
        AddCircle(root, new Vector2(-radius * 0.48f), radius * 0.1f, theme.Secondary with { A = 0.14f });
        AddRing(root, new Vector2(-radius * 0.48f), radius * 0.13f, theme.Secondary with { A = 0.5f }, 2);
    }

    private static void BuildCoastalCliff(EffectContainer root, SonnetTheme theme,
        float width, float height, float radius, uint seed)
    {
        var direction = seed % 2 == 0 ? 1f : -1f;
        var bleed = ThemedBleed(width, height, radius);
        Vector2[] cliff = [new(-bleed.X * direction, bleed.Y), new(-radius * 0.78f * direction, -radius * 0.1f),
            new(-radius * 0.5f * direction, -radius * 0.34f), new(-radius * 0.18f * direction, radius * 0.08f),
            new(radius * 0.08f * direction, radius * 0.58f)];
        ThemedFillAndStroke(root, cliff, theme.Primary, 0.11f, 0.62f, 2.5f);
        var towerX = -radius * 0.5f * direction;
        var towerPosition = new Vector2(towerX - radius * 0.09f, -radius * 0.42f);
        var towerSize = new Vector2(radius * 0.18f, radius * 0.45f);
        AddRect(root, towerPosition, towerSize, theme.Secondary with { A = 0.12f });
        AddOutlineRect(root, towerPosition, towerSize, 2, theme.Secondary with { A = 0.7f });
        Vector2[] roof = [new(towerX - radius * 0.14f, -radius * 0.42f), new(towerX, -radius * 0.56f),
            new(towerX + radius * 0.14f, -radius * 0.42f)];
        AddFillPolygon(root, roof, theme.Secondary with { A = 0.2f });
        AddPolygon(root, roof, 2, theme.Secondary with { A = 0.72f });
        for (var wave = 0; wave < 6; wave++)
        {
            var y = radius * (0.14f + wave * 0.1f);
            AddPolyline(root, ThemedQuadratic(new Vector2(-radius * 0.05f * direction, y),
                new Vector2(radius * 0.35f * direction, y - radius * 0.08f), new Vector2(bleed.X * direction, y)),
                wave % 3 == 0 ? 2 : 1,
                (wave % 2 != 0 ? theme.Secondary : theme.Primary) with { A = 0.26f + wave * 0.045f });
        }
    }

    private static void ThemedLeaf(EffectContainer root, Vector2 origin, float length, float width,
        float angle, EffectColor color, float fillAlpha)
    {
        var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        var normal = new Vector2(-direction.Y, direction.X);
        var tip = origin + direction * length;
        var outline = new List<Vector2> { origin };
        ThemedAppendQuadratic(outline, origin + direction * length * 0.45f + normal * width, tip);
        ThemedAppendQuadratic(outline, origin + direction * length * 0.45f - normal * width, origin);
        AddFillPolygon(root, outline, color with { A = fillAlpha });
        AddPolyline(root, outline, 1.5f, color with { A = Math.Min(0.8f, fillAlpha * 3.2f) });
        AddLine(root, origin, tip, 1, color with { A = 0.32f });
    }

    private static void ThemedFillAndStroke(EffectContainer root, IReadOnlyList<Vector2> points,
        EffectColor color, float fillAlpha, float strokeAlpha, float strokeWidth)
    {
        AddFillPolygon(root, points, color with { A = fillAlpha });
        AddPolygon(root, points, strokeWidth, color with { A = strokeAlpha });
    }

    private static Vector2 ThemedBleed(float width, float height, float radius) =>
        new(Math.Max(radius * 0.92f, width * 0.64f), Math.Max(radius * 0.92f, height * 0.64f));

    private static IReadOnlyList<Vector2> ThemedQuadratic(Vector2 start, Vector2 control, Vector2 end)
    {
        var points = new List<Vector2> { start };
        ThemedAppendQuadratic(points, control, end);
        return points;
    }

    private static void ThemedAppendQuadratic(List<Vector2> points, Vector2 control, Vector2 end,
        int segments = 16)
    {
        var start = points[^1];
        for (var index = 1; index <= segments; index++)
        {
            var t = index / (float)segments;
            var inverse = 1 - t;
            points.Add(inverse * inverse * start + 2 * inverse * t * control + t * t * end);
        }
    }

    private static IReadOnlyList<Vector2> ThemedCubic(
        Vector2 start, Vector2 control1, Vector2 control2, Vector2 end, int segments = 24)
    {
        var points = new List<Vector2> { start };
        for (var index = 1; index <= segments; index++)
        {
            var t = index / (float)segments;
            var inverse = 1 - t;
            points.Add(inverse * inverse * inverse * start
                + 3 * inverse * inverse * t * control1
                + 3 * inverse * t * t * control2
                + t * t * t * end);
        }
        return points;
    }

    private static void ThemedAppendArc(List<Vector2> points, Vector2 center, float radius,
        float start, float end, int segments = 20)
    {
        for (var index = 0; index <= segments; index++)
        {
            var angle = start + (end - start) * index / segments;
            points.Add(center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius);
        }
    }
}
