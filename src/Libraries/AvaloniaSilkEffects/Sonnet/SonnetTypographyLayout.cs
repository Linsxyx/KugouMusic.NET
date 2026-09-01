namespace AvaloniaSilkEffects.Sonnet;

public static class SonnetTypographyLayout
{
    public static bool IsEmphasis(SonnetSegmentRole role) => SonnetTypographyRoles.IsEmphasis(role);

    public static int ResolveFontWeight(int? configured, SonnetSegmentRole role)
    {
        return SonnetTypographyRoles.ResolveFontWeight(configured, role);
    }

    public static int VisibleLength(SonnetSemanticSegment segment) =>
        SonnetTypographyRoles.VisibleLength(segment);

    public static double HeroScore(SonnetSemanticSegment segment) =>
        SonnetTypographyRoles.HeroScore(segment);

    public static int FindHero(IReadOnlyList<SonnetSemanticSegment> segments)
    {
        return SonnetTypographyRoles.FindHeroIndex(segments);
    }

    public static IReadOnlyList<int> FindSemiHeroes(IReadOnlyList<SonnetSemanticSegment> segments, int heroIndex)
    {
        return SonnetTypographyRoles.FindSemiHeroIndices(segments, heroIndex);
    }

    public static IReadOnlyList<SonnetTypographyPlacement> Resolve(
        IReadOnlyList<IReadOnlyList<SonnetSemanticSegment>> lines,
        SonnetShotKind shotKind,
        SonnetParagraphKind paragraphKind,
        float width,
        float height,
        float baseFontSize,
        Func<string, float, int, (float Width, float Height)> measure,
        int? configuredWeight = null)
    {
        var segments = lines.SelectMany(item => item).ToArray();
        if (segments.Length == 0) return [];
        var roles = Enumerable.Repeat(SonnetSegmentRole.Support, segments.Length).ToArray();
        var offset = 0;
        foreach (var line in lines)
        {
            var hero = FindHero(line);
            roles[offset + hero] = SonnetSegmentRole.Hero;
            foreach (var semi in FindSemiHeroes(line, hero)) roles[offset + semi] = SonnetSegmentRole.SemiHero;
            offset += line.Count;
        }

        var startTime = segments.Min(item => (item.StartTime + item.EndTime) / 2);
        var endTime = segments.Max(item => (item.StartTime + item.EndTime) / 2);
        var seed = SonnetRandom.Hash(string.Join('\u241f', segments.Select(item => item.Text)));
        var layoutVariantSeed = segments.Sum(item => Math.Max(1, item.Text.Trim().Length)) + segments.Length;
        var globalHeroIndex = FindHero(segments);
        var editorialVariant = layoutVariantSeed % 5;
        var secondaryHeroIndex = -1;
        if (editorialVariant == 3 && segments.Length > 2)
        {
            var bestScore = double.NegativeInfinity;
            for (var index = 0; index < segments.Length; index++)
            {
                var segment = segments[index];
                if (index == globalHeroIndex || !segment.IsWordLike || VisibleLength(segment) == 0) continue;
                var distanceBonus = Math.Abs(index - globalHeroIndex) > 1 ? 50 : 0;
                var score = HeroScore(segment) + distanceBonus;
                if (score <= bestScore) continue;
                bestScore = score;
                secondaryHeroIndex = index;
            }
            if (secondaryHeroIndex == -1) editorialVariant = 0;
        }
        else if (editorialVariant == 3)
        {
            editorialVariant = 0;
        }
        else if (editorialVariant == 4 && segments.Length < 2)
        {
            editorialVariant = 2;
        }
        if (shotKind == SonnetShotKind.EditorialColumn && editorialVariant == 3 && secondaryHeroIndex >= 0)
            roles[secondaryHeroIndex] = SonnetSegmentRole.Hero;
        var boxes = new List<Box>(segments.Length);
        for (var index = 0; index < segments.Length; index++)
        {
            var role = roles[index];
            float heroScale;
            float supportScale;
            var verticalIntent = false;
            switch (shotKind)
            {
                case SonnetShotKind.EditorialColumn:
                    if (editorialVariant == 3)
                    {
                        heroScale = 3.8f;
                        supportScale = 1.3f;
                    }
                    else if (editorialVariant == 4)
                    {
                        heroScale = 4.2f;
                        supportScale = 1.25f;
                        verticalIntent = IsEmphasis(role);
                    }
                    else
                    {
                        heroScale = editorialVariant == 2 ? 3.2f : 4;
                        supportScale = 1.2f;
                        verticalIntent = IsEmphasis(role) && editorialVariant != 2;
                    }
                    break;
                case SonnetShotKind.TypeImpact:
                    heroScale = 5.5f; supportScale = 1.5f;
                    break;
                case SonnetShotKind.FragmentCollage:
                    heroScale = 3.2f; supportScale = 1.35f;
                    verticalIntent = role == SonnetSegmentRole.SemiHero || index % 4 == 0;
                    break;
                case SonnetShotKind.TrackingRibbon:
                    heroScale = 3.5f; supportScale = 1.5f;
                    break;
                case SonnetShotKind.MaskReveal:
                    heroScale = 4.5f; supportScale = 1.6f;
                    verticalIntent = IsEmphasis(role);
                    break;
                case SonnetShotKind.PosterBlocks:
                    heroScale = 4.4f; supportScale = 1.15f;
                    break;
                default:
                    heroScale = 3; supportScale = 1.15f;
                    verticalIntent = IsEmphasis(role) && layoutVariantSeed % 4 is 0 or 1;
                    break;
            }
            var scale = role == SonnetSegmentRole.Hero ? heroScale : role == SonnetSegmentRole.SemiHero ? Math.Max(supportScale * 1.35f, heroScale * 0.72f) : supportScale;
            var segment = segments[index];
            var chars = segment.Graphemes.Count > 0
                ? segment.Graphemes.Select(item => item.Text).ToArray()
                : segment.Text.EnumerateRunes().Select(rune => rune.ToString()).ToArray();
            var rotatesNonCjk = verticalIntent && VisibleLength(segment) > 1 && !ContainsCjk(segment.Text);
            var vertical = verticalIntent && !rotatesNonCjk;
            var rotation = rotatesNonCjk ? MathF.PI / 2 : 0;
            var display = vertical ? string.Join('\n', chars) : segment.Text;
            var fontSize = baseFontSize * scale;
            var weight = ResolveFontWeight(configuredWeight, role);
            float measuredWidth;
            float measuredHeight;
            if (rotatesNonCjk)
            {
                var horizontalAdvance = chars.Where(character => character.Trim().Length > 0)
                    .Sum(character => Math.Max(fontSize * 0.2f, measure(character, fontSize, weight).Width));
                measuredWidth = fontSize * 1.2f;
                measuredHeight = horizontalAdvance;
            }
            else if (vertical)
            {
                var glyphAdvances = chars.Where(character => character.Trim().Length > 0)
                    .Select(character => Math.Max(fontSize * 0.2f, measure(character, fontSize, weight).Width)).ToArray();
                measuredWidth = glyphAdvances.Length > 0 ? glyphAdvances.Max() : fontSize;
                measuredHeight = Math.Max(1, chars.Length) * fontSize * 0.9f;
            }
            else
            {
                measuredWidth = measure(display, fontSize, weight).Width;
                measuredHeight = fontSize * 1.2f;
            }
            var fit = Math.Min(1, Math.Min(width * 0.82f / Math.Max(1, measuredWidth), height * 0.82f / Math.Max(1, measuredHeight)));
            var box = new Box(index, display, role, scale * fit, measuredWidth * fit, measuredHeight * fit, vertical,
                endTime - startTime > 0.001 ? (float)(((segment.StartTime + segment.EndTime) / 2 - startTime) / (endTime - startTime)) : index / (float)Math.Max(1, segments.Length - 1))
            {
                Rotation = rotation,
            };
            if (shotKind == SonnetShotKind.PosterBlocks && ContainsCjk(segment.Text))
            {
                var targetFontSize = fontSize * fit;
                var columnWidths = chars.Where(character => character.Trim().Length > 0)
                    .Select(character => Math.Max(targetFontSize * 0.2f, measure(character, targetFontSize, weight).Width))
                    .ToArray();
                var columnWidth = columnWidths.Length > 0 ? columnWidths.Max() : targetFontSize;
                var columnHeight = Math.Max(1, chars.Length) * targetFontSize * 0.9f;
                var verticalFit = Math.Min(1, Math.Min(
                    width * 0.82f / Math.Max(1, columnWidth),
                    height * 0.82f / Math.Max(1, columnHeight)));
                box.VerticalText = string.Join('\n', chars);
                box.VerticalWidth = columnWidth * verticalFit;
                box.VerticalHeight = columnHeight * verticalFit;
                box.VerticalScale = scale * fit * verticalFit;
            }
            boxes.Add(box);
        }

        Place(boxes, shotKind, width, height, baseFontSize, seed, layoutVariantSeed,
            globalHeroIndex, editorialVariant, secondaryHeroIndex);
        return boxes.Select(box => new SonnetTypographyPlacement(
            box.Index, box.Text, box.Role, box.Scale, box.Width, box.Height,
            box.X, box.Y, box.Rotation, box.EnterX, box.EnterY, box.Vertical, box.Phase, box.LayoutDirection)).ToArray();
    }

    private static void Place(
        List<Box> boxes, SonnetShotKind kind, float width, float height, float fontSize,
        uint seed, int layoutVariantSeed, int heroIndex, int editorialVariant, int secondaryHeroIndex)
    {
        if (kind == SonnetShotKind.PosterBlocks)
            ApplyPosterBlocks(boxes, width, height, fontSize, seed, heroIndex);
        else
            ApplyExactFlow(boxes, kind, width, height, fontSize, layoutVariantSeed,
                heroIndex, editorialVariant, secondaryHeroIndex);
    }

    private static void ApplyPosterBlocks(
        List<Box> boxes, float width, float height, float baseFontSize, uint seed, int heroIndex)
    {
        var posterBoxes = boxes.Select((box, index) => new SonnetPosterBlockBox
        {
            IsHero = index == heroIndex,
            IsSemiHero = box.Role == SonnetSegmentRole.SemiHero,
            DisplayText = box.Text,
            VerticalDisplayText = box.VerticalText,
            VerticalMeasuredWidth = box.VerticalWidth,
            VerticalMeasuredHeight = box.VerticalHeight,
            VerticalFontScale = box.VerticalScale,
            FontScale = box.Scale,
            MeasuredWidth = box.Width,
            MeasuredHeight = box.Height,
            X = box.X,
            Y = box.Y,
            Rotation = box.Rotation,
            Vertical = box.Vertical,
            LayoutDirection = box.LayoutDirection,
            EnterX = box.EnterX,
            EnterY = box.EnterY,
        }).ToArray();
        SonnetPosterBlocksLayout.Layout(posterBoxes, width, height, baseFontSize, seed);
        posterBoxes[heroIndex].EnterX = 0;
        posterBoxes[heroIndex].EnterY = height * 0.15;
        for (var index = 0; index < boxes.Count; index++)
        {
            var source = posterBoxes[index];
            var target = boxes[index];
            target.Text = source.DisplayText;
            target.Scale = (float)source.FontScale;
            target.Width = (float)source.MeasuredWidth;
            target.Height = (float)source.MeasuredHeight;
            target.X = (float)source.X;
            target.Y = (float)source.Y;
            target.Rotation = (float)source.Rotation;
            target.Vertical = source.Vertical;
            target.LayoutDirection = source.LayoutDirection;
            target.EnterX = (float)source.EnterX;
            target.EnterY = (float)source.EnterY;
        }
    }

    private static void ApplyExactFlow(
        List<Box> boxes, SonnetShotKind kind, float width, float height, float baseFontSize,
        int layoutVariantSeed, int heroIndex, int editorialVariant, int secondaryHeroIndex)
    {
        var flowBoxes = boxes.Select(box => new SonnetFlowLayoutBox
        {
            Index = box.Index,
            IsHero = box.Index == heroIndex,
            IsSemiHero = box.Role == SonnetSegmentRole.SemiHero,
            DisplayText = box.Text,
            FontScale = box.Scale,
            MeasuredWidth = box.Width,
            MeasuredHeight = box.Height,
            Vertical = box.Vertical,
            LayoutDirection = SonnetLayoutDirection.Horizontal,
            Rotation = box.Rotation,
        }).ToArray();
        var gaps = SonnetShotFlowLayouts.ResolveGaps(baseFontSize);
        var context = new SonnetFlowLayoutContext(
            flowBoxes, heroIndex, width, height, gaps.FlowGap, gaps.StackGap);
        if (kind == SonnetShotKind.QuietTableau)
            SonnetShotFlowLayouts.LayoutQuietTableau(context, layoutVariantSeed % 4);
        else if (kind == SonnetShotKind.TrackingRibbon)
            SonnetShotFlowLayouts.LayoutTrackingRibbon(context, layoutVariantSeed % 3);
        else if (kind is SonnetShotKind.TypeImpact or SonnetShotKind.MaskReveal)
            SonnetShotFlowLayouts.LayoutCrossStack(context);
        else if (kind == SonnetShotKind.EditorialColumn)
            SonnetShotFlowLayouts.LayoutEditorialColumn(context, editorialVariant, secondaryHeroIndex);
        else
            SonnetShotFlowLayouts.LayoutFragmentCollage(context, layoutVariantSeed % 3);

        flowBoxes[heroIndex].EnterX = 0;
        flowBoxes[heroIndex].EnterY = height * 0.15;
        for (var index = 0; index < boxes.Count; index++)
        {
            var source = flowBoxes[index];
            var target = boxes[index];
            target.Scale = (float)source.FontScale;
            target.Width = (float)source.MeasuredWidth;
            target.Height = (float)source.MeasuredHeight;
            target.X = (float)source.X;
            target.Y = (float)source.Y;
            target.Rotation = (float)source.Rotation;
            target.EnterX = (float)source.EnterX;
            target.EnterY = (float)source.EnterY;
            target.LayoutDirection = source.LayoutDirection;
        }
    }

    private static bool ContainsCjk(string text) => text.Any(character =>
        character is >= '\u3040' and <= '\u30ff' or >= '\u3400' and <= '\u9fff' or >= '\uac00' and <= '\ud7af');

    private sealed class Box(int index, string text, SonnetSegmentRole role, float scale, float width, float height, bool vertical, float phase)
    {
        public int Index { get; } = index; public string Text { get; set; } = text; public SonnetSegmentRole Role { get; } = role;
        public float Scale { get; set; } = scale; public float Width { get; set; } = width; public float Height { get; set; } = height;
        public bool Vertical { get; set; } = vertical; public float Phase { get; } = phase;
        public string? VerticalText { get; set; }
        public float? VerticalWidth { get; set; }
        public float? VerticalHeight { get; set; }
        public float? VerticalScale { get; set; }
        public SonnetLayoutDirection LayoutDirection { get; set; }
        public float X { get; set; } public float Y { get; set; } public float Rotation { get; set; }
        public float EnterX { get; set; } public float EnterY { get; set; }
    }
}
