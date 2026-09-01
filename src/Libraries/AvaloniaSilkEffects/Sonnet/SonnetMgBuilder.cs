using System.Numerics;

namespace AvaloniaSilkEffects.Sonnet;

internal static class SonnetMgBuilder
{
    internal static SonnetMgView BuildShot(
        SonnetShot shot, SonnetTheme theme, float width, float height, uint seed, SonnetTuning tuning)
    {
        var root = new EffectContainer();
        var radius = Math.Min(width, height);
        if (tuning.ShowBackgroundMg) BuildHud(root, theme, width, height, seed);
        if (tuning.ShowFixedGeo && shot.Kind is SonnetShotKind.TypeImpact or SonnetShotKind.FragmentCollage)
            BuildFixedGeometry(root, theme, radius, seed);
        return new SonnetMgView(root, theme, width, height, seed, tuning.MgDensity, tuning.ShowBackgroundDecor);
    }

    internal static EffectContainer BuildOverlay(SonnetTheme theme, float width, float height)
    {
        var root = new EffectContainer();
        var px = Math.Max(30, width * 0.05f);
        var py = Math.Max(30, height * 0.05f);
        AddLine(root, new(px, py + 16), new(px, py + 120), 1, theme.Primary with { A = 0.5f });
        AddRect(root, new(px, py), new(30, 4), theme.Primary with { A = 0.8f });
        AddLine(root, new(width - px - 160, height - py), new(width - px - 20, height - py), 1, theme.Primary with { A = 0.5f });
        AddLine(root, new(width - px, height - py - 180), new(width - px, height - py - 30), 1, theme.Primary with { A = 0.5f });
        AddRect(root, new(width - px - 4, height - py - 16), new(4, 16), theme.Primary with { A = 0.8f });
        AddLine(root, new(width - px - 6, py + 20), new(width - px + 6, py + 20), 1, theme.Primary with { A = 0.8f });
        AddLine(root, new(width - px, py + 14), new(width - px, py + 26), 1, theme.Primary with { A = 0.8f });
        AddDiamond(root, new(px, height - py), 5, theme.Primary with { A = 0.7f });
        return root;
    }

    internal static EffectContainer BuildFrame(
        SonnetTypographyPlacement placement, float fontSize, SonnetTheme theme, uint seed)
    {
        var root = new EffectContainer { Position = new(placement.X, placement.Y), Rotation = placement.Rotation };
        var pad = Math.Clamp(fontSize * 0.22f, 8, 20);
        var halfW = placement.MeasuredWidth / 2 + pad;
        var halfH = placement.MeasuredHeight / 2 + pad;
        var arm = Math.Clamp(Math.Min(halfW, halfH) * 0.3f, 7, 30);
        var color = theme.Primary with { A = 0.42f };
        var variant = seed % 4;
        var corners = new[] { new Vector2(-halfW, -halfH), new Vector2(halfW, -halfH), new Vector2(halfW, halfH), new Vector2(-halfW, halfH) };
        foreach (var corner in corners)
        {
            var sx = MathF.Sign(corner.X);
            var sy = MathF.Sign(corner.Y);
            AddLine(root, corner, corner - new Vector2(sx * arm, 0), 1.4f, color);
            AddLine(root, corner, corner - new Vector2(0, sy * arm), 1.4f, color);
            if (variant == 1) AddCircle(root, corner - new Vector2(sx * 5, sy * 5), 3.5f, theme.Accent with { A = 0.55f });
            else if (variant == 2) AddDiamond(root, corner - new Vector2(sx * 5, sy * 5), 4, theme.Accent with { A = 0.52f });
            else if (variant == 3) AddLine(root, corner - new Vector2(sx * 5, sy * 5), corner - new Vector2(sx * (arm + 5), sy * 5), 0.8f, color with { A = 0.25f });
        }
        return root;
    }

    internal static SonnetGuideView BuildGuide(
        SonnetSemanticSegment segment, SonnetTypographyPlacement placement, float fontSize, SonnetTheme theme, uint seed) =>
        new(segment, placement, fontSize, theme, seed);

    private static void BuildHud(EffectContainer root, SonnetTheme theme, float width, float height, uint seed)
    {
        var hw = width / 2;
        var hh = height / 2;
        var primary = theme.Primary with { A = 0.22f };
        var secondary = theme.Secondary with { A = 0.18f };
        switch (seed % 8)
        {
            case 0: // asymmetric editorial rulers
                for (var i = -5; i <= 5; i++)
                {
                    AddLine(root, new(-hw * 0.84f, i * height * 0.075f), new(-hw * (i % 2 == 0 ? 0.68f : 0.76f), i * height * 0.075f), i % 3 == 0 ? 2 : 1, primary);
                    AddLine(root, new(i * width * 0.07f, -hh * 0.78f), new(i * width * 0.07f, -hh * 0.68f), 1, secondary);
                }
                break;
            case 1: // radar
                for (var i = 1; i <= 5; i++) AddRing(root, Vector2.Zero, Math.Min(width, height) * i * 0.09f, primary);
                AddLine(root, new(-hw * 0.75f, 0), new(hw * 0.75f, 0), 1, primary);
                AddLine(root, new(0, -hh * 0.75f), new(0, hh * 0.75f), 1, primary);
                break;
            case 2: // technical grid
                for (var i = -5; i <= 5; i++) AddLine(root, new(i * width * 0.08f, -hh * 0.72f), new(i * width * 0.08f, hh * 0.72f), 1, primary with { A = 0.1f });
                for (var i = -3; i <= 3; i++) AddLine(root, new(-hw * 0.76f, i * height * 0.12f), new(hw * 0.76f, i * height * 0.12f), 1, secondary with { A = 0.1f });
                break;
            case 3: // concentric diamonds
                for (var i = 1; i <= 4; i++) AddPolygon(root,
                    [new(0, -height * 0.1f * i), new(width * 0.08f * i, 0), new(0, height * 0.1f * i), new(-width * 0.08f * i, 0)],
                    i == 4 ? 2 : 1, primary);
                break;
            case 4: // orbital ellipses
                for (var orbit = 0; orbit < 3; orbit++) AddEllipseRing(root, Vector2.Zero, width * 0.34f, height * (0.08f + orbit * 0.025f), orbit * MathF.PI / 3, primary);
                AddCircle(root, Vector2.Zero, 7, theme.Accent with { A = 0.45f });
                break;
            case 5: // waveform ladders
                for (var row = -3; row <= 3; row++)
                {
                    var points = Enumerable.Range(0, 19).Select(i => new Vector2(-hw * 0.75f + i * width * 0.083f,
                        row * height * 0.1f + MathF.Sin(i * 0.9f + row) * height * 0.025f)).ToArray();
                    AddPolyline(root, points, 1, row == 0 ? primary with { A = 0.36f } : secondary with { A = 0.12f });
                }
                break;
            case 6: // perspective frame
                AddPolygon(root, [new(-hw * 0.76f, -hh * 0.65f), new(hw * 0.64f, -hh * 0.52f), new(hw * 0.76f, hh * 0.64f), new(-hw * 0.62f, hh * 0.54f)], 2, primary);
                for (var i = 1; i < 6; i++) AddLine(root, new(-hw * 0.76f, -hh * 0.65f + i * height * 0.11f), new(hw * 0.76f, -hh * 0.52f + i * height * 0.1f), 1, secondary with { A = 0.11f });
                break;
            default: // open frame fragments
                var marginX = hw * 0.72f; var marginY = hh * 0.65f; var arm = Math.Min(width, height) * 0.13f;
                foreach (var point in new[] { new Vector2(-marginX, -marginY), new(marginX, -marginY), new(marginX, marginY), new(-marginX, marginY) })
                {
                    var sx = MathF.Sign(point.X); var sy = MathF.Sign(point.Y);
                    AddLine(root, point, point - new Vector2(sx * arm, 0), 2, primary);
                    AddLine(root, point, point - new Vector2(0, sy * arm), 2, primary);
                }
                break;
        }
    }

    private static void BuildFixedGeometry(EffectContainer root, SonnetTheme theme, float radius, uint seed)
    {
        var geoVariant = (int)(seed % SonnetVariantResolver.GeometryVariantCount);
        if (geoVariant is >= 14 and <= 17)
        {
            var direction = seed % 2 == 0 ? 1d : -1d;
            var color = ToRgb(theme.Primary);
            if (geoVariant == 14)
            {
                root.Add(SonnetSpatialDrawLists.SolidCuboid(radius * 0.18 * direction, radius * 0.03, radius * 0.62, radius * 0.7, radius * 0.22 * direction, -radius * 0.16, color, 0.34).Replay());
                root.Add(SonnetSpatialDrawLists.SolidCuboid(-radius * 0.48 * direction, radius * 0.24, radius * 0.28, radius * 0.38, radius * 0.12 * direction, -radius * 0.09, color, 0.24).Replay());
                root.Add(SonnetSpatialDrawLists.SolidCuboid(radius * 0.55 * direction, -radius * 0.3, radius * 0.2, radius * 0.26, radius * 0.09 * direction, -radius * 0.07, color, 0.2).Replay());
            }
            else if (geoVariant == 15)
            {
                root.Add(SonnetSpatialDrawLists.TriangularPrism(-radius * 0.12 * direction, radius * 0.02, radius * 0.72, radius * 0.68, radius * 0.18 * direction, -radius * 0.13, color, 0.34).Replay());
                root.Add(SonnetSpatialDrawLists.TriangularPrism(radius * 0.48 * direction, radius * 0.26, radius * 0.28, radius * 0.25, -radius * 0.08 * direction, -radius * 0.06, color, 0.22).Replay());
                root.Add(SonnetSpatialDrawLists.TriangularPrism(-radius * 0.5 * direction, -radius * 0.3, radius * 0.2, radius * 0.18, radius * 0.06 * direction, -radius * 0.05, color, 0.18).Replay());
            }
            else if (geoVariant == 16)
            {
                root.Add(SonnetSpatialDrawLists.HexagonalPrism(radius * 0.12 * direction, 0, radius * 0.68, radius * 0.72, radius * 0.2 * direction, -radius * 0.14, color, 0.32).Replay());
                root.Add(SonnetSpatialDrawLists.HexagonalPrism(-radius * 0.48 * direction, radius * 0.27, radius * 0.25, radius * 0.28, radius * 0.07 * direction, -radius * 0.05, color, 0.2).Replay());
                root.Add(SonnetSpatialDrawLists.HexagonalPrism(radius * 0.52 * direction, -radius * 0.3, radius * 0.18, radius * 0.2, -radius * 0.06 * direction, -radius * 0.045, color, 0.17).Replay());
            }
            else
            {
                root.Add(SonnetSpatialDrawLists.TrapezoidPrism(radius * 0.12 * direction, radius * 0.04, radius * 0.3, radius * 0.68, radius * 0.62, radius * 0.18 * direction, -radius * 0.13, color, 0.34).Replay());
                root.Add(SonnetSpatialDrawLists.TrapezoidPrism(-radius * 0.42 * direction, radius * 0.28, radius * 0.2, radius * 0.38, radius * 0.22, radius * 0.08 * direction, -radius * 0.06, color, 0.21).Replay());
                root.Add(SonnetSpatialDrawLists.TrapezoidPrism(radius * 0.5 * direction, -radius * 0.3, radius * 0.2, radius * 0.12, radius * 0.22, -radius * 0.06 * direction, -radius * 0.05, color, 0.18).Replay());
            }
            return;
        }

        switch ((seed >> 4) % 6)
        {
            case 0:
                AddRing(root, Vector2.Zero, radius * 0.6f, theme.Primary with { A = 0.58f }, 5);
                AddRing(root, Vector2.Zero, radius * 0.57f, theme.Secondary with { A = 0.3f }, 1.5f);
                for (var i = 0; i < 32; i++)
                {
                    var angle = MathF.Tau * i / 32;
                    AddLine(root, new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius * (0.31f + i % 3 * 0.04f),
                        new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius * 0.55f, 1, theme.Primary with { A = 0.22f });
                }
                break;
            case 1:
                for (var scale = 1f; scale >= 0.42f; scale -= 0.28f) AddPolygon(root,
                    [new(0, -radius * 0.68f * scale), new(radius * 0.68f * scale, 0), new(0, radius * 0.68f * scale), new(-radius * 0.68f * scale, 0)],
                    scale > 0.9f ? 5 : 2, theme.Primary with { A = 0.55f * scale });
                break;
            case 2:
                AddRegularPolygon(root, Vector2.Zero, radius * 0.6f, 6, theme.Primary with { A = 0.6f }, 5);
                AddRegularPolygon(root, Vector2.Zero, radius * 0.25f, 6, theme.Secondary with { A = 0.42f }, 2);
                for (var i = 0; i < 6; i++)
                {
                    var angle = i * MathF.PI / 3 - MathF.PI / 6;
                    AddLine(root, new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius * 0.25f,
                        new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius * 0.57f, 2, theme.Primary with { A = 0.32f });
                }
                break;
            case 3:
                for (var i = 0; i < 3; i++) AddEllipseRing(root, Vector2.Zero, radius * 0.68f, radius * 0.12f, i * MathF.PI / 3, theme.Primary with { A = 0.32f });
                AddCircle(root, Vector2.Zero, radius * 0.045f, theme.Accent with { A = 0.7f });
                break;
            case 4:
                for (var i = 1; i <= 6; i++) AddRing(root, Vector2.Zero, radius * 0.13f * i, theme.Primary with { A = 0.13f + i % 3 * 0.08f }, i % 2 == 0 ? 2 : 1);
                for (var i = 0; i < 72; i++)
                {
                    var angle = MathF.Tau * i / 72; var length = i % 18 == 0 ? 20 : i % 6 == 0 ? 10 : 5;
                    AddLine(root, new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius * 0.79f,
                        new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (radius * 0.79f + length), 1, theme.Primary with { A = 0.3f });
                }
                break;
            default:
                var chain = Enumerable.Range(0, 8).Select(i => new Vector2((i - 3.5f) * radius * 0.16f, (i % 2 == 0 ? -1 : 1) * radius * 0.08f)).ToArray();
                AddPolyline(root, chain, 3, theme.Primary with { A = 0.62f });
                foreach (var point in chain) AddCircle(root, point, 5, theme.Accent with { A = 0.55f });
                break;
        }
    }

    private static uint ToRgb(EffectColor color) =>
        (uint)(Math.Clamp((int)MathF.Round(color.R * 255), 0, 255) << 16
            | Math.Clamp((int)MathF.Round(color.G * 255), 0, 255) << 8
            | Math.Clamp((int)MathF.Round(color.B * 255), 0, 255));

    private static void AddRect(EffectContainer root, Vector2 position, Vector2 size, EffectColor color) => root.Add(new ShapeNode { Position = position, Size = size, Color = color });
    private static void AddCircle(EffectContainer root, Vector2 center, float radius, EffectColor color) => root.Add(new ShapeNode { Shape = EffectShapeKind.Ellipse, Position = center - new Vector2(radius), Size = new(radius * 2), Color = color });
    private static void AddRing(EffectContainer root, Vector2 center, float radius, EffectColor color, float width = 1) => AddEllipseRing(root, center, radius, radius, 0, color, width);
    private static void AddEllipseRing(EffectContainer root, Vector2 center, float rx, float ry, float rotation, EffectColor color, float width = 1)
    {
        var points = Enumerable.Range(0, 49).Select(i =>
        {
            var angle = MathF.Tau * i / 48; var local = new Vector2(MathF.Cos(angle) * rx, MathF.Sin(angle) * ry);
            return center + Vector2.Transform(local, Matrix3x2.CreateRotation(rotation));
        }).ToArray();
        AddPolyline(root, points, width, color);
    }
    private static void AddRegularPolygon(EffectContainer root, Vector2 center, float radius, int sides, EffectColor color, float width = 1) =>
        AddPolygon(root, Enumerable.Range(0, sides).Select(i => center + new Vector2(MathF.Cos(MathF.Tau * i / sides - MathF.PI / 2), MathF.Sin(MathF.Tau * i / sides - MathF.PI / 2)) * radius).ToArray(), width, color);
    private static void AddDiamond(EffectContainer root, Vector2 center, float size, EffectColor color) =>
        AddPolygon(root, [center + new Vector2(0, -size), center + new Vector2(size, 0), center + new Vector2(0, size), center + new Vector2(-size, 0)], Math.Max(1, size * 0.35f), color);
    private static void AddPolygon(EffectContainer root, IReadOnlyList<Vector2> points, float width, EffectColor color) => AddPolyline(root, points.Concat([points[0]]).ToArray(), width, color);
    private static void AddPolyline(EffectContainer root, IReadOnlyList<Vector2> points, float width, EffectColor color)
    {
        for (var i = 1; i < points.Count; i++) AddLine(root, points[i - 1], points[i], width, color);
    }
    private static void AddLine(EffectContainer root, Vector2 start, Vector2 end, float width, EffectColor color) => root.Add(new ShapeNode { Shape = EffectShapeKind.Line, Position = start, Size = end - start, StrokeWidth = width, Color = color });
}
