using System.Numerics;

namespace AvaloniaSilkEffects.Sonnet;

internal static partial class SonnetMgBuilder
{
    internal static SonnetMgView BuildShot(
        SonnetShot shot, SonnetTheme theme, float width, float height, uint seed, SonnetTuning tuning)
    {
        var root = new EffectContainer();
        var radius = Math.Min(width, height);
        EffectContainer? fixedGeometryLayer = null;
        if (tuning.ShowBackgroundMg)
        {
            BuildHud(root, theme, width, height, seed);
            if (shot.Kind is SonnetShotKind.TypeImpact or SonnetShotKind.FragmentCollage)
            {
                var mainGeometryLayer = new EffectContainer();
                var variant = (int)(seed % SonnetVariantResolver.GeometryVariantCount);
                var keepsUpright = variant is 6 or 8 or 9 or 14 or 15 or 16 or 17 or 20 or 22 or 23
                    || variant >= 24;
                if (!keepsUpright)
                    mainGeometryLayer.Rotation = (float)(((ulong)seed * 13 % 360) * Math.PI / 180);
                else if (variant == 8)
                    mainGeometryLayer.Rotation = (float)(((seed / 100) % 4) * Math.PI / 2);
                BuildMainGeometry(mainGeometryLayer, theme, width, height, radius, seed);
                root.Add(mainGeometryLayer);
            }
        }
        if (tuning.ShowFixedGeo && shot.Kind is SonnetShotKind.TypeImpact or SonnetShotKind.FragmentCollage)
        {
            fixedGeometryLayer = new EffectContainer();
            BuildFixedGeometry(fixedGeometryLayer, theme, radius, seed);
            root.Add(fixedGeometryLayer);
        }
        return new SonnetMgView(
            root, fixedGeometryLayer, theme, shot.Kind, width, height, seed, tuning.ShowBackgroundDecor);
    }

    internal static EffectContainer BuildSceneBackdrop(
        SonnetTheme theme,
        float width,
        float height,
        uint seed,
        SonnetTuning tuning,
        bool transparentBackground)
    {
        var root = new EffectContainer();
        if (!tuning.ShowOnlyText && tuning.ShowBackgroundMg)
        {
            if (!transparentBackground)
                AddRect(root, Vector2.Zero, new Vector2(width, height), theme.Background with { A = 0.1f });

            var density = (int)MathF.Round(4 + tuning.MgDensity * 5);
            for (var index = 0; index < density; index++)
            {
                var x = (float)(((ulong)seed + (ulong)index * 97) % 997) / 997 * width;
                var y = (float)(((ulong)seed + (ulong)index * 193) % 991) / 991 * height;
                var length = 32 + (float)(((ulong)seed + (ulong)index * 43) % 180);
                var color = (index % 2 == 0 ? theme.Accent : theme.Secondary) with
                {
                    A = 0.12f + index % 4 * 0.04f,
                };
                AddLine(root, new Vector2(x, y), new Vector2(Math.Min(width, x + length), y), index % 3 == 0 ? 2 : 1, color);
            }
        }

        if (!tuning.ShowOnlyText && tuning.OuterFrameMode == SonnetOuterFrameMode.Full)
        {
            if (!string.IsNullOrWhiteSpace(theme.Name))
            {
                root.Add(new TextNode
                {
                    Text = $"[ THEME ] {theme.Name.ToUpperInvariant()}",
                    FontFamily = theme.FontFamily,
                    FontSize = 14,
                    FontWeight = theme.FontWeight ?? 700,
                    Color = theme.Primary with { A = 0.2f },
                    Position = new Vector2(20, height - 20),
                    Rotation = -MathF.PI / 2,
                    Anchor = new Vector2(0, 1),
                    RasterScale = tuning.TextureResolution,
                });
            }

            if (!string.IsNullOrWhiteSpace(theme.Description))
            {
                root.Add(new TextNode
                {
                    Text = theme.Description,
                    FontFamily = theme.FontFamily,
                    FontSize = 12,
                    FontWeight = theme.FontWeight ?? 400,
                    Color = theme.Secondary with { A = 0.3f },
                    Position = new Vector2(width - 20, 20),
                    Anchor = new Vector2(1, 0),
                    RasterScale = tuning.TextureResolution,
                });
            }
        }

        return root;
    }

    internal static EffectContainer BuildOverlay(SonnetTheme theme, float width, float height)
    {
        var root = new EffectContainer();
        var px = Math.Max(30, width * 0.05f);
        var py = Math.Max(30, height * 0.05f);
        AddLine(root, new Vector2(px, py + 16), new Vector2(px, py + 120), 1, theme.Primary with { A = 0.5f });
        AddRect(root, new Vector2(px, py), new Vector2(30, 4), theme.Primary with { A = 0.8f });
        AddLine(root, new Vector2(width - px - 160, height - py), new Vector2(width - px - 20, height - py), 1, theme.Primary with { A = 0.5f });
        AddLine(root, new Vector2(width - px, height - py - 180), new Vector2(width - px, height - py - 30), 1, theme.Primary with { A = 0.5f });
        AddRect(root, new Vector2(width - px - 4, height - py - 16), new Vector2(4, 16), theme.Primary with { A = 0.8f });
        AddLine(root, new Vector2(width - px - 6, py + 20), new Vector2(width - px + 6, py + 20), 1, theme.Primary with { A = 0.8f });
        AddLine(root, new Vector2(width - px, py + 14), new Vector2(width - px, py + 26), 1, theme.Primary with { A = 0.8f });
        AddDiamond(root, new Vector2(px, height - py), 5, theme.Primary with { A = 0.7f });
        return root;
    }

    internal static EffectContainer BuildFrame(
        SonnetTypographyPlacement placement, float fontSize, SonnetTheme theme, uint seed)
    {
        var root = new EffectContainer { Position = new Vector2(placement.X, placement.Y), Rotation = placement.Rotation };
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
        var marginX = width * 0.05f;
        var marginY = height * 0.05f;
        var left = -hw + marginX;
        var right = hw - marginX;
        var top = -hh + marginY;
        var bottom = hh - marginY;
        var primary = theme.Primary;
        var secondary = theme.Secondary;

        void Cross(Vector2 center, float size, EffectColor color)
        {
            AddLine(root, center + new Vector2(-size, -size), center + new Vector2(size, size), 1, color);
            AddLine(root, center + new Vector2(size, -size), center + new Vector2(-size, size), 1, color);
        }

        void OutlineRect(float x, float y, float w, float h, float strokeWidth, EffectColor color) =>
            AddPolygon(root,
                [new Vector2(x, y), new Vector2(x + w, y), new Vector2(x + w, y + h), new Vector2(x, y + h)],
                strokeWidth,
                color);

        switch (SonnetVariantResolver.Background(seed))
        {
            case 0: // classic-cross
                Cross(new Vector2(left, top), 4, primary with { A = 0.4f });
                Cross(new Vector2(right, top), 4, primary with { A = 0.4f });
                Cross(new Vector2(left, bottom), 4, primary with { A = 0.4f });
                Cross(new Vector2(right, bottom), 4, primary with { A = 0.4f });
                for (var index = 0; index < 8; index++)
                    Cross(new Vector2(left, top + index * 20 + 30), 3, primary with { A = 0.3f });
                var classicBarY = bottom - 10;
                AddLine(root, new Vector2(left + 20, classicBarY), new Vector2(right - 20, classicBarY), 1, primary with { A = 0.3f });
                Cross(new Vector2(left + 10, classicBarY), 3, primary with { A = 0.5f });
                Cross(new Vector2(left + 30, classicBarY), 3, primary with { A = 0.5f });
                Cross(new Vector2(right - 10, classicBarY), 3, primary with { A = 0.5f });
                AddCircle(root, new Vector2(0, classicBarY), 2, secondary with { A = 0.8f });
                break;

            case 1: // corner-brackets
                var arm = Math.Min(hw, hh) * 0.08f;
                const float inset = 6;
                var corners = new[]
                {
                    (new Vector2(left, top), 1f, 1f),
                    (new Vector2(right, top), -1f, 1f),
                    (new Vector2(left, bottom), 1f, -1f),
                    (new Vector2(right, bottom), -1f, -1f),
                };
                for (var index = 0; index < corners.Length; index++)
                {
                    var (corner, sx, sy) = corners[index];
                    AddPolyline(root,
                        [corner + new Vector2(sx * arm, 0), corner, corner + new Vector2(0, sy * arm)],
                        2,
                        primary with { A = 0.55f });
                    AddPolyline(root,
                        [corner + new Vector2(sx * (arm + inset), sy * inset),
                            corner + new Vector2(sx * inset, sy * inset),
                            corner + new Vector2(sx * inset, sy * (arm + inset))],
                        1,
                        primary with { A = 0.25f });
                    if (index % 2 == 0)
                        AddRect(root, corner + new Vector2(sx * arm * 0.4f - 2, sy * arm * 0.4f - 2),
                            new Vector2(4), secondary with { A = 0.6f });
                }
                var rulerY = bottom + inset;
                AddLine(root, new Vector2(left + arm + 12, rulerY), new Vector2(right - arm - 12, rulerY), 1,
                    primary with { A = 0.3f });
                const int bracketTicks = 24;
                var bracketSpan = (right - arm - 12) - (left + arm + 12);
                for (var index = 0; index <= bracketTicks; index++)
                {
                    var x = left + arm + 12 + bracketSpan * index / bracketTicks;
                    var major = index % 6 == 0;
                    AddLine(root, new Vector2(x, rulerY), new Vector2(x, rulerY - (major ? 8 : 4)), 1,
                        (major ? secondary : primary) with { A = major ? 0.55f : 0.3f });
                }
                break;

            case 2: // marquee-strips
                foreach (var direction in new[] { -1, 1 })
                {
                    var y = direction * bottom;
                    AddLine(root, new Vector2(left, y), new Vector2(right, y), 2, primary with { A = 0.45f });
                    AddLine(root, new Vector2(left, y + direction * 6), new Vector2(right, y + direction * 6), 1,
                        primary with { A = 0.2f });
                    AddRect(root, new Vector2(left, y - 3), new Vector2(14, 6), secondary with { A = 0.55f });
                    AddRect(root, new Vector2(right - 14, y - 3), new Vector2(14, 6), secondary with { A = 0.55f });
                    for (var index = 3; index < 18; index += 3)
                    {
                        var x = left + (right - left) * index / 18;
                        AddLine(root, new Vector2(x, y), new Vector2(x, y + direction * 6), 1, primary with { A = 0.35f });
                    }
                }
                Cross(new Vector2(0, top), 4, primary with { A = 0.5f });
                AddPolyline(root, [new Vector2(-6, bottom - 14), new Vector2(0, bottom - 8), new Vector2(6, bottom - 14)], 1,
                    secondary with { A = 0.6f });
                break;

            case 3: // diagonal-corners
                var cornerSize = Math.Min(hw, hh) * 0.12f;
                foreach (var (corner, sx, sy) in new[]
                         {
                             (new Vector2(left, top), 1f, 1f),
                             (new Vector2(right, top), -1f, 1f),
                             (new Vector2(left, bottom), 1f, -1f),
                             (new Vector2(right, bottom), -1f, -1f),
                         })
                {
                    AddPolygon(root,
                        [corner + new Vector2(sx * cornerSize, 0), corner,
                            corner + new Vector2(0, sy * cornerSize)],
                        1.5f,
                        primary with { A = 0.5f });
                    AddLine(root, corner + new Vector2(sx * cornerSize * 0.55f, 0),
                        corner + new Vector2(0, sy * cornerSize * 0.55f), 1, secondary with { A = 0.4f });
                    AddLine(root, corner + new Vector2(sx * cornerSize * 0.3f, 0),
                        corner + new Vector2(0, sy * cornerSize * 0.3f), 1, primary with { A = 0.25f });
                }
                break;

            case 4: // dotted-columns
                const int rows = 14;
                for (var index = 0; index < rows; index++)
                {
                    var y = top + 10 + index * (bottom - top - 20) / (rows - 1);
                    var strong = index % 4 == 0;
                    var dotColor = (strong ? secondary : primary) with { A = strong ? 0.6f : 0.3f };
                    AddCircle(root, new Vector2(left, y), strong ? 2.4f : 1.4f, dotColor);
                    AddCircle(root, new Vector2(right, y), strong ? 2.4f : 1.4f, dotColor);
                }
                AddLine(root, new Vector2(-18, 0), new Vector2(18, 0), 1, primary with { A = 0.22f });
                AddLine(root, new Vector2(0, -18), new Vector2(0, 18), 1, primary with { A = 0.22f });
                AddRing(root, Vector2.Zero, 6, primary with { A = 0.3f });
                break;

            case 5: // double-frame
                var gapX = (right - left) * 0.18f;
                var gapY = (bottom - top) * 0.22f;
                AddLine(root, new Vector2(left, top), new Vector2(-gapX / 2, top), 2, primary with { A = 0.45f });
                AddLine(root, new Vector2(gapX / 2, top), new Vector2(right, top), 2, primary with { A = 0.45f });
                AddLine(root, new Vector2(left, bottom), new Vector2(-gapX / 2, bottom), 2, primary with { A = 0.45f });
                AddLine(root, new Vector2(gapX / 2, bottom), new Vector2(right, bottom), 2, primary with { A = 0.45f });
                AddLine(root, new Vector2(left, top), new Vector2(left, -gapY / 2), 2, primary with { A = 0.45f });
                AddLine(root, new Vector2(left, gapY / 2), new Vector2(left, bottom), 2, primary with { A = 0.45f });
                AddLine(root, new Vector2(right, top), new Vector2(right, -gapY / 2), 2, primary with { A = 0.45f });
                AddLine(root, new Vector2(right, gapY / 2), new Vector2(right, bottom), 2, primary with { A = 0.45f });
                OutlineRect(left + 7, top + 7, right - left - 14, bottom - top - 14, 1,
                    primary with { A = 0.18f });
                foreach (var corner in new[] { new Vector2(left, top), new Vector2(right, top), new Vector2(left, bottom), new Vector2(right, bottom) })
                    AddRect(root, corner - new Vector2(3), new Vector2(6), secondary with { A = 0.6f });
                break;

            case 6: // ruler-frame
                for (var index = 0; index <= 32; index++)
                {
                    var x = left + (right - left) * index / 32;
                    var major = index % 8 == 0;
                    var middle = index % 4 == 0;
                    var length = major ? 12 : middle ? 7 : 4;
                    var color = (major ? secondary : primary) with { A = major ? 0.55f : 0.32f };
                    AddLine(root, new Vector2(x, top), new Vector2(x, top + length), 1, color);
                    AddLine(root, new Vector2(x, bottom), new Vector2(x, bottom - length), 1, color);
                }
                for (var index = 0; index <= 18; index++)
                {
                    var y = top + (bottom - top) * index / 18;
                    var major = index % 6 == 0;
                    var length = major ? 12 : index % 3 == 0 ? 7 : 4;
                    var color = (major ? secondary : primary) with { A = major ? 0.55f : 0.32f };
                    AddLine(root, new Vector2(left, y), new Vector2(left + length, y), 1, color);
                    AddLine(root, new Vector2(right, y), new Vector2(right - length, y), 1, color);
                }
                AddRing(root, Vector2.Zero, 10, primary with { A = 0.25f });
                AddCircle(root, Vector2.Zero, 3, secondary with { A = 0.4f });
                break;

            default: // arc-gauge
                var arcRadius = Math.Min(hw, hh) * 0.11f;
                foreach (var arc in new[]
                         {
                             (new Vector2(left, top), 0f, MathF.PI / 2),
                             (new Vector2(right, top), MathF.PI / 2, MathF.PI),
                             (new Vector2(right, bottom), MathF.PI, MathF.PI * 1.5f),
                             (new Vector2(left, bottom), MathF.PI * 1.5f, MathF.Tau),
                         })
                {
                    AddArc(root, arc.Item1, arcRadius, arc.Item2, arc.Item3, 2, primary with { A = 0.5f });
                    AddArc(root, arc.Item1, arcRadius * 0.72f, arc.Item2, arc.Item3, 1,
                        primary with { A = 0.25f });
                }
                var gaugeY = bottom + arcRadius * 0.4f;
                var gaugeRadius = Math.Min(hw, hh) * 0.16f;
                AddArc(root, new Vector2(0, gaugeY), gaugeRadius, MathF.PI, MathF.Tau, 1.5f,
                    primary with { A = 0.4f });
                var needle = MathF.PI + seed % 100 / 100f * MathF.PI;
                AddLine(root, new Vector2(0, gaugeY),
                    new Vector2(MathF.Cos(needle) * (gaugeRadius - 8), gaugeY + MathF.Sin(needle) * (gaugeRadius - 8)),
                    2,
                    secondary with { A = 0.6f });
                break;
        }
    }

    private static void BuildMainGeometry(
        EffectContainer root, SonnetTheme theme, float width, float height, float radius, uint seed)
    {
        var geoVariant = (int)(seed % SonnetVariantResolver.GeometryVariantCount);
        if (geoVariant is >= 24 and <= 35)
        {
            BuildThemedGeometry(root, theme, width, height, radius, seed, geoVariant);
            return;
        }
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
                    [new Vector2(0, -radius * 0.68f * scale), new Vector2(radius * 0.68f * scale, 0), new Vector2(0, radius * 0.68f * scale), new Vector2(-radius * 0.68f * scale, 0)],
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

    private static void BuildFixedGeometry(EffectContainer root, SonnetTheme theme, float radius, uint seed)
    {
        var primary = theme.Primary;
        var accent = seed % 2 == 0 ? theme.Secondary : theme.Primary;
        var direction = seed % 2 == 0 ? 1f : -1f;

        switch (SonnetVariantResolver.FixedGeometry(seed))
        {
            case 1: // twin-pillars
                AddRect(root, new Vector2(-radius * 0.34f, -radius * 0.28f),
                    new Vector2(radius * 0.12f, radius * 0.56f), accent with { A = 0.65f });
                AddRect(root, new Vector2(-radius * 0.305f, -radius * 0.245f),
                    new Vector2(radius * 0.05f, radius * 0.49f), primary with { A = 0.35f });
                AddOutlineRect(root, new Vector2(radius * 0.06f, -radius * 0.34f),
                    new Vector2(radius * 0.28f, radius * 0.68f), 2, primary with { A = 0.6f });
                AddOutlineRect(root, new Vector2(radius * 0.1f, -radius * 0.3f),
                    new Vector2(radius * 0.2f, radius * 0.6f), 1, primary with { A = 0.3f });
                AddHatching(root, -radius * 0.14f, -radius * 0.2f,
                    radius * 0.12f, radius * 0.4f, 5, primary);
                break;

            case 2: // disc-ring
                AddCircle(root, new Vector2(-radius * 0.2f, radius * 0.12f), radius * 0.15f,
                    accent with { A = 0.7f });
                AddCircle(root, new Vector2(-radius * 0.2f, radius * 0.12f), radius * 0.06f,
                    primary with { A = 0.5f });
                AddRing(root, new Vector2(radius * 0.14f, -radius * 0.06f), radius * 0.3f,
                    primary with { A = 0.6f }, 2);
                AddRing(root, new Vector2(radius * 0.14f, -radius * 0.06f), radius * 0.22f,
                    primary with { A = 0.3f });
                AddHatching(root, radius * 0.02f, -radius * 0.14f,
                    radius * 0.24f, radius * 0.16f, 5, primary);
                break;

            case 3: // diamond-pair
                var diamondCenter = -radius * 0.08f * direction;
                var diamondRadius = radius * 0.3f;
                AddPolygon(root,
                    [new Vector2(diamondCenter, -diamondRadius), new Vector2(diamondCenter + diamondRadius, 0),
                        new Vector2(diamondCenter, diamondRadius), new Vector2(diamondCenter - diamondRadius, 0)],
                    2, primary with { A = 0.6f });
                AddPolygon(root,
                    [new Vector2(diamondCenter, -diamondRadius * 0.7f), new Vector2(diamondCenter + diamondRadius * 0.7f, 0),
                        new Vector2(diamondCenter, diamondRadius * 0.7f), new Vector2(diamondCenter - diamondRadius * 0.7f, 0)],
                    1, primary with { A = 0.3f });
                var smallRadius = radius * 0.11f;
                var smallCenter = new Vector2(radius * 0.3f * direction, -radius * 0.2f);
                AddFillPolygon(root,
                    [smallCenter + new Vector2(0, -smallRadius), smallCenter + new Vector2(smallRadius, 0),
                        smallCenter + new Vector2(0, smallRadius), smallCenter + new Vector2(-smallRadius, 0)],
                    accent with { A = 0.7f });
                AddHatching(root, smallCenter.X - smallRadius * 0.8f, radius * 0.16f,
                    smallRadius * 1.6f, smallRadius * 1.2f, 4, primary);
                break;

            case 4: // stripe-stack
                AddRect(root, new Vector2(-radius * 0.36f * direction - radius * 0.2f, -radius * 0.26f),
                    new Vector2(radius * 0.56f, radius * 0.09f), accent with { A = 0.7f });
                AddOutlineRect(root, new Vector2(-radius * 0.28f, -radius * 0.06f),
                    new Vector2(radius * 0.56f, radius * 0.16f), 2, primary with { A = 0.6f });
                AddRect(root, new Vector2(-radius * 0.2f * direction, radius * 0.2f),
                    new Vector2(radius * 0.4f, radius * 0.045f), primary with { A = 0.5f });
                AddHatching(root, radius * 0.26f * direction, -radius * 0.3f,
                    radius * 0.12f, radius * 0.6f, 5, primary);
                break;

            case 5: // corner-els
                var arm = radius * 0.24f;
                var thick = radius * 0.07f;
                var x1 = -radius * 0.3f * direction;
                var y1 = -radius * 0.24f;
                AddRect(root, new Vector2(x1 - (direction < 0 ? arm : 0), y1), new Vector2(arm, thick),
                    accent with { A = 0.7f });
                AddRect(root, new Vector2(direction < 0 ? x1 - arm : x1, y1), new Vector2(thick, arm),
                    accent with { A = 0.7f });
                var x2 = radius * 0.3f * direction;
                var y2 = radius * 0.24f;
                AddRect(root, new Vector2(direction < 0 ? x2 : x2 - arm, y2 - thick), new Vector2(arm, thick),
                    primary with { A = 0.55f });
                AddRect(root, new Vector2(direction < 0 ? x2 + arm - thick : x2 - thick, y2 - arm), new Vector2(thick, arm),
                    primary with { A = 0.55f });
                AddOutlineRect(root, new Vector2(-radius * 0.13f), new Vector2(radius * 0.26f), 2,
                    primary with { A = 0.6f });
                AddHatching(root, -radius * 0.09f * direction, radius * 0.02f,
                    radius * 0.16f, radius * 0.1f, 4, primary);
                break;

            case 6: // twin-wedges
                var wedgeX = -radius * 0.14f * direction;
                AddFillPolygon(root,
                    [new Vector2(wedgeX, -radius * 0.3f), new Vector2(wedgeX + radius * 0.24f, radius * 0.02f),
                        new Vector2(wedgeX - radius * 0.24f, radius * 0.02f)], accent with { A = 0.6f });
                var hollowX = radius * 0.16f * direction;
                AddPolygon(root,
                    [new Vector2(hollowX, radius * 0.3f), new Vector2(hollowX + radius * 0.24f, -radius * 0.02f),
                        new Vector2(hollowX - radius * 0.24f, -radius * 0.02f)], 2, primary with { A = 0.6f });
                AddPolygon(root,
                    [new Vector2(hollowX, radius * 0.2f), new Vector2(hollowX + radius * 0.15f, 0),
                        new Vector2(hollowX - radius * 0.15f, 0)], 1, primary with { A = 0.3f });
                AddHatching(root, -radius * 0.3f * direction - radius * 0.05f, radius * 0.1f,
                    radius * 0.2f, radius * 0.18f, 5, primary);
                break;

            case 7: // cross-ring
                var crossCenter = ((int)(seed % 3) - 1) * radius * 0.08f;
                var crossArm = radius * 0.17f;
                var crossThick = radius * 0.075f;
                AddRect(root, new Vector2(crossCenter - crossArm, -crossThick / 2),
                    new Vector2(crossArm * 2, crossThick), accent with { A = 0.7f });
                AddRect(root, new Vector2(crossCenter - crossThick / 2, -crossArm),
                    new Vector2(crossThick, crossArm * 2), accent with { A = 0.7f });
                AddRing(root, new Vector2(crossCenter, 0), radius * 0.3f, primary with { A = 0.6f }, 2);
                AddRing(root, new Vector2(crossCenter, 0), radius * 0.36f, primary with { A = 0.25f });
                AddHatching(root, crossCenter + radius * 0.18f, radius * 0.14f,
                    radius * 0.16f, radius * 0.16f, 4, primary);
                break;

            default: // classic-blocks
                AddRect(root, new Vector2(-radius * 0.4f, -radius * 0.2f),
                    new Vector2(radius * 0.6f, radius * 0.15f), primary with { A = 0.7f });
                AddOutlineRect(root, new Vector2(-radius * 0.1f, radius * 0.1f),
                    new Vector2(radius * 0.5f, radius * 0.3f), 2, primary with { A = 0.6f });
                AddHatching(root, -radius * 0.3f, -radius * 0.4f,
                    radius * 0.4f, radius * 0.25f, 6, primary);
                break;
        }
    }

    private static void AddOutlineRect(EffectContainer root, Vector2 position, Vector2 size,
        float width, EffectColor color) =>
        AddPolygon(root,
            [position, position + new Vector2(size.X, 0), position + size,
                position + new Vector2(0, size.Y)], width, color);

    private static void AddHatching(EffectContainer root, float x, float y, float width, float height,
        float spacing, EffectColor primary)
    {
        // Folia clips the diagonal strokes with a rectangular Pixi mask. Resolve
        // the same intersections up front so no hatch segment can protrude.
        for (var offset = -width; offset < width + height; offset += spacing)
        {
            var startT = Math.Max(0, -offset / height);
            var endT = Math.Min(1, (width - offset) / height);
            if (endT <= startT) continue;
            AddLine(root,
                new Vector2(x + offset + startT * height, y + startT * height),
                new Vector2(x + offset + endT * height, y + endT * height),
                1, primary with { A = 0.15f });
        }
    }

    private static uint ToRgb(EffectColor color) =>
        (uint)(Math.Clamp((int)MathF.Round(color.R * 255), 0, 255) << 16
            | Math.Clamp((int)MathF.Round(color.G * 255), 0, 255) << 8
            | Math.Clamp((int)MathF.Round(color.B * 255), 0, 255));

    private static void AddRect(EffectContainer root, Vector2 position, Vector2 size, EffectColor color) => root.Add(new ShapeNode { Position = position, Size = size, Color = color });
    private static void AddCircle(EffectContainer root, Vector2 center, float radius, EffectColor color) => root.Add(new ShapeNode { Shape = EffectShapeKind.Ellipse, Position = center - new Vector2(radius), Size = new Vector2(radius * 2), Color = color });
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
    private static void AddArc(EffectContainer root, Vector2 center, float radius, float start, float end, float width, EffectColor color)
    {
        var points = Enumerable.Range(0, 17).Select(index =>
        {
            var angle = start + (end - start) * index / 16;
            return center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
        }).ToArray();
        AddPolyline(root, points, width, color);
    }
    private static void AddRegularPolygon(EffectContainer root, Vector2 center, float radius, int sides, EffectColor color, float width = 1) =>
        AddPolygon(root, Enumerable.Range(0, sides).Select(i => center + new Vector2(MathF.Cos(MathF.Tau * i / sides - MathF.PI / 2), MathF.Sin(MathF.Tau * i / sides - MathF.PI / 2)) * radius).ToArray(), width, color);
    private static void AddDiamond(EffectContainer root, Vector2 center, float size, EffectColor color) =>
        AddPolygon(root, [center + new Vector2(0, -size), center + new Vector2(size, 0), center + new Vector2(0, size), center + new Vector2(-size, 0)], Math.Max(1, size * 0.35f), color);
    private static void AddFillPolygon(EffectContainer root, IReadOnlyList<Vector2> points, EffectColor color) =>
        root.Add(new PolygonNode { Points = points, Color = color });
    private static void AddPolygon(EffectContainer root, IReadOnlyList<Vector2> points, float width, EffectColor color) => AddPolyline(root, points.Concat([points[0]]).ToArray(), width, color);
    private static void AddPolyline(EffectContainer root, IReadOnlyList<Vector2> points, float width, EffectColor color)
    {
        if (points.Count < 2) return;
        root.Add(new PolylineNode
        {
            Points = points,
            TailWidth = width,
            HeadWidth = width,
            TailAlpha = 1,
            HeadAlpha = 1,
            Color = color,
        });
    }
    private static void AddLine(EffectContainer root, Vector2 start, Vector2 end, float width, EffectColor color) => root.Add(new ShapeNode { Shape = EffectShapeKind.Line, Position = start, Size = end - start, StrokeWidth = width, Color = color });
}
