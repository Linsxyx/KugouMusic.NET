namespace AvaloniaSilkEffects.Sonnet;

// Exact incremental port of Folia v0.7.2 sonnetShotFlowLayouts.ts.
public sealed class SonnetFlowLayoutBox
{
    public int Index { get; init; }
    public bool IsHero { get; init; }
    public bool IsSemiHero { get; init; }
    public string DisplayText { get; init; } = "";
    public double FontScale { get; set; }
    public double MeasuredWidth { get; set; }
    public double MeasuredHeight { get; set; }
    public bool Vertical { get; init; }
    public SonnetLayoutDirection LayoutDirection { get; set; }
    public double Rotation { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double EnterX { get; set; }
    public double EnterY { get; set; }
}

public sealed record SonnetFlowLayoutContext(
    IReadOnlyList<SonnetFlowLayoutBox> Boxes,
    int HeroIndex,
    double Width,
    double Height,
    double FlowGap,
    double StackGap);

public static class SonnetShotFlowLayouts
{
    private static readonly double[] GlobalFitScales = [1, 0.92, 0.84, 0.76, 0.68, 0.6, 0.52];

    public static (double FlowGap, double StackGap) ResolveGaps(double baseFontSize)
    {
        var flowGap = Math.Clamp(baseFontSize * 0.35, 16, 40);
        return (flowGap, Math.Max(24, flowGap * 1.35));
    }

    public static void PlaceWithGlobalFit(
        SonnetFlowLayoutContext context,
        Action<double> place)
    {
        var snapshot = context.Boxes.Select(box => (
            box.FontScale, box.MeasuredWidth, box.MeasuredHeight)).ToArray();
        var safeHalfWidth = context.Width * 0.48;
        var safeHalfHeight = context.Height * 0.46;
        foreach (var globalScale in GlobalFitScales)
        {
            for (var index = 0; index < context.Boxes.Count; index++)
            {
                var box = context.Boxes[index];
                box.FontScale = snapshot[index].FontScale * globalScale;
                box.MeasuredWidth = snapshot[index].MeasuredWidth * globalScale;
                box.MeasuredHeight = snapshot[index].MeasuredHeight * globalScale;
            }

            place(globalScale);
            var fits = context.Boxes.All(box =>
                Math.Abs(box.X) + box.MeasuredWidth / 2 <= safeHalfWidth + 0.5
                && Math.Abs(box.Y) + box.MeasuredHeight / 2 <= safeHalfHeight + 0.5);
            if (fits) return;
        }
    }

    public static void LayoutQuietTableau(SonnetFlowLayoutContext context, int variant)
    {
        var boxes = context.Boxes;
        var heroBox = boxes[context.HeroIndex];
        var horizontalCard = variant is 2 or 3;
        foreach (var box in boxes)
            box.LayoutDirection = horizontalCard ? SonnetLayoutDirection.Horizontal : SonnetLayoutDirection.Vertical;
        var safeHalfHeight = context.Height * 0.46;

        PlaceWithGlobalFit(context, _ =>
        {
            heroBox.X = 0;
            heroBox.Y = horizontalCard ? 0 : -context.Height * 0.1;
            var stagger = variant == 3 ? 70 : 0;
            var columnStep = boxes.Max(box => box.MeasuredWidth) + context.StackGap + stagger;

            double XFor(SonnetFlowLayoutBox box, int index)
            {
                if (variant == 1) return heroBox.X - heroBox.MeasuredWidth / 2 + box.MeasuredWidth / 2;
                if (variant == 3) return heroBox.X + (index % 2 == 0 ? 1 : -1) * 35;
                return heroBox.X;
            }

            var column = 0;
            var currentY = heroBox.Y - heroBox.MeasuredHeight / 2 - context.StackGap;
            for (var index = context.HeroIndex - 1; index >= 0; index--)
            {
                var box = boxes[index];
                if (currentY - box.MeasuredHeight < -safeHalfHeight)
                {
                    column++;
                    currentY = safeHalfHeight;
                }

                box.X = XFor(box, index) + column * columnStep;
                box.Y = currentY - box.MeasuredHeight / 2;
                currentY -= box.MeasuredHeight + context.StackGap;
                if (variant == 1) { box.EnterX = 20; box.EnterY = 0; }
                else if (variant == 3) { box.EnterX = box.X > heroBox.X ? 30 : -30; box.EnterY = 0; }
                else { box.EnterX = 0; box.EnterY = 20; }
            }

            column = 0;
            currentY = heroBox.Y + heroBox.MeasuredHeight / 2 + context.StackGap;
            for (var index = context.HeroIndex + 1; index < boxes.Count; index++)
            {
                var box = boxes[index];
                if (currentY + box.MeasuredHeight > safeHalfHeight)
                {
                    column++;
                    currentY = -safeHalfHeight;
                }

                box.X = XFor(box, index) - column * columnStep;
                box.Y = currentY + box.MeasuredHeight / 2;
                currentY += box.MeasuredHeight + context.StackGap;
                if (variant == 1) { box.EnterX = -20; box.EnterY = 0; }
                else if (variant == 3) { box.EnterX = box.X > heroBox.X ? 30 : -30; box.EnterY = 0; }
                else { box.EnterX = 0; box.EnterY = -20; }
            }
        });
    }

    public static void LayoutTrackingRibbon(SonnetFlowLayoutContext context, int variant)
    {
        var boxes = context.Boxes;
        var heroBox = boxes[context.HeroIndex];
        foreach (var box in boxes) box.LayoutDirection = SonnetLayoutDirection.Horizontal;

        PlaceWithGlobalFit(context, _ =>
        {
            heroBox.X = 0;
            heroBox.Y = 0;

            double AlignY(SonnetFlowLayoutBox box, int index) => variant switch
            {
                1 => heroBox.Y + heroBox.MeasuredHeight / 2 - box.MeasuredHeight / 2,
                2 => heroBox.Y - heroBox.MeasuredHeight / 2 + box.MeasuredHeight / 2,
                _ => heroBox.Y + (index % 2 == 0 ? 10 : -10),
            };

            var enter = variant == 2 ? 20 : 30;
            var currentX = heroBox.X - heroBox.MeasuredWidth / 2 - context.FlowGap;
            for (var index = context.HeroIndex - 1; index >= 0; index--)
            {
                var box = boxes[index];
                box.X = currentX - box.MeasuredWidth / 2;
                box.Y = AlignY(box, index);
                currentX -= box.MeasuredWidth + context.FlowGap;
                box.EnterX = enter;
                box.EnterY = 0;
            }

            currentX = heroBox.X + heroBox.MeasuredWidth / 2 + context.FlowGap;
            for (var index = context.HeroIndex + 1; index < boxes.Count; index++)
            {
                var box = boxes[index];
                box.X = currentX + box.MeasuredWidth / 2;
                box.Y = AlignY(box, index);
                currentX += box.MeasuredWidth + context.FlowGap;
                box.EnterX = -enter;
                box.EnterY = 0;
            }
        });
    }

    public static void LayoutEditorialColumn(
        SonnetFlowLayoutContext context,
        int variant,
        int secondaryHeroIndex)
    {
        var boxes = context.Boxes;
        var heroBox = boxes[context.HeroIndex];

        if (variant == 0)
        {
            foreach (var box in boxes) box.LayoutDirection = SonnetLayoutDirection.Vertical;
            PlaceWithGlobalFit(context, _ =>
            {
                heroBox.X = -context.Width * 0.15;
                heroBox.Y = 0;
                var currentY = heroBox.Y - heroBox.MeasuredHeight / 2 + context.StackGap * 0.5;
                for (var index = 0; index < context.HeroIndex; index++)
                {
                    var box = boxes[index];
                    box.X = heroBox.X + heroBox.MeasuredWidth / 2 + context.FlowGap + box.MeasuredWidth / 2;
                    box.Y = currentY + box.MeasuredHeight / 2;
                    currentY += box.MeasuredHeight + context.StackGap;
                    box.EnterX = -20;
                    box.EnterY = 0;
                }

                currentY = heroBox.Y - heroBox.MeasuredHeight / 2 + context.StackGap * 0.5;
                for (var index = context.HeroIndex + 1; index < boxes.Count; index++)
                {
                    var box = boxes[index];
                    box.X = heroBox.X - heroBox.MeasuredWidth / 2 - context.FlowGap - box.MeasuredWidth / 2;
                    box.Y = currentY + box.MeasuredHeight / 2;
                    currentY += box.MeasuredHeight + context.StackGap;
                    box.EnterX = 20;
                    box.EnterY = 0;
                }
            });
            return;
        }

        if (variant == 1)
        {
            foreach (var box in boxes) box.LayoutDirection = SonnetLayoutDirection.Vertical;
            PlaceWithGlobalFit(context, _ =>
            {
                var rightEdge = context.Width * 0.28;
                var safeHalfHeight = context.Height * 0.46;
                var railStep = boxes.Max(box => box.MeasuredWidth) + context.StackGap;
                var totalHeight = boxes.Sum(box => box.MeasuredHeight) + context.StackGap * (boxes.Count - 1);
                var fitsSingleRail = boxes.Sum(box => box.MeasuredHeight) * 0.52
                    + context.StackGap * (boxes.Count - 1) <= safeHalfHeight * 2;
                if (fitsSingleRail)
                {
                    var currentY = -totalHeight / 2;
                    foreach (var box in boxes)
                    {
                        box.X = rightEdge - box.MeasuredWidth / 2;
                        box.Y = currentY + box.MeasuredHeight / 2;
                        currentY += box.MeasuredHeight + context.StackGap;
                        box.EnterX = 20;
                        box.EnterY = 0;
                    }
                    return;
                }

                var rail = 0;
                var wrappedY = -safeHalfHeight;
                foreach (var box in boxes)
                {
                    if (wrappedY + box.MeasuredHeight > safeHalfHeight)
                    {
                        rail++;
                        wrappedY = -safeHalfHeight;
                    }
                    box.X = rightEdge - rail * railStep - box.MeasuredWidth / 2;
                    box.Y = wrappedY + box.MeasuredHeight / 2;
                    wrappedY += box.MeasuredHeight + context.StackGap;
                    box.EnterX = 20;
                    box.EnterY = 0;
                }
            });
            return;
        }

        if (variant == 2)
        {
            foreach (var box in boxes) box.LayoutDirection = SonnetLayoutDirection.Horizontal;
            PlaceWithGlobalFit(context, _ =>
            {
                heroBox.X = 0;
                heroBox.Y = -context.Height * 0.25;
                var before = boxes.Take(context.HeroIndex).ToArray();
                var after = boxes.Skip(context.HeroIndex + 1).ToArray();
                if (before.Length > 0)
                {
                    var kickerHeight = before.Max(box => box.MeasuredHeight);
                    var kickerWidth = before.Sum(box => box.MeasuredWidth) + context.FlowGap * (before.Length - 1);
                    var kickerY = heroBox.Y - heroBox.MeasuredHeight / 2 - context.StackGap - kickerHeight / 2;
                    var currentX = heroBox.X - kickerWidth / 2;
                    foreach (var box in before)
                    {
                        box.X = currentX + box.MeasuredWidth / 2;
                        box.Y = kickerY;
                        currentX += box.MeasuredWidth + context.FlowGap;
                        box.EnterX = 0;
                        box.EnterY = -20;
                    }
                }

                var leftAnchor = heroBox.X - heroBox.MeasuredWidth * 0.25 - context.FlowGap;
                var rightAnchor = heroBox.X + heroBox.MeasuredWidth * 0.25 + context.FlowGap;
                var currentY = heroBox.Y + heroBox.MeasuredHeight / 2 + context.StackGap;
                for (var pair = 0; pair < after.Length; pair += 2)
                {
                    var left = after[pair];
                    var right = pair + 1 < after.Length ? after[pair + 1] : null;
                    var rowHeight = Math.Max(left.MeasuredHeight, right?.MeasuredHeight ?? 0);
                    left.X = leftAnchor - left.MeasuredWidth / 2;
                    left.Y = currentY + left.MeasuredHeight / 2;
                    left.EnterX = -20;
                    left.EnterY = 0;
                    if (right is not null)
                    {
                        right.X = rightAnchor + right.MeasuredWidth / 2;
                        right.Y = currentY + right.MeasuredHeight / 2;
                        right.EnterX = 20;
                        right.EnterY = 0;
                    }
                    currentY += rowHeight + context.StackGap;
                }
            });
            return;
        }

        if (variant == 3)
        {
            foreach (var box in boxes) box.LayoutDirection = SonnetLayoutDirection.Horizontal;
            PlaceWithGlobalFit(context, _ =>
            {
                heroBox.X = 0;
                heroBox.Y = 0;
                var firstHero = Math.Min(context.HeroIndex, secondaryHeroIndex);
                var line1 = boxes.Take(firstHero + 1).ToArray();
                var line2 = boxes.Skip(firstHero + 1).ToArray();
                var line1Height = line1.Max(box => box.MeasuredHeight);
                var line2Height = line2.Max(box => box.MeasuredHeight);
                var totalHeight = line1Height + context.StackGap + line2Height;
                var line1Y = heroBox.Y - totalHeight / 2 + line1Height / 2;
                var line2Y = line1Y + line1Height / 2 + context.StackGap + line2Height / 2;

                double LayLine(IReadOnlyList<SonnetFlowLayoutBox> line, double lineY, double enterX)
                {
                    var lineWidth = line.Sum(box => box.MeasuredWidth) + context.FlowGap * (line.Count - 1);
                    var currentX = -lineWidth / 2;
                    foreach (var box in line)
                    {
                        box.X = currentX + box.MeasuredWidth / 2;
                        box.Y = lineY;
                        currentX += box.MeasuredWidth + context.FlowGap;
                        box.EnterX = enterX;
                        box.EnterY = 0;
                    }
                    return lineWidth;
                }

                var line1Width = LayLine(line1, line1Y, 30);
                var line2Width = LayLine(line2, line2Y, -30);
                var offsetAmount = Math.Max(line1Width, line2Width) * 0.12;
                foreach (var box in line1) box.X -= offsetAmount;
                foreach (var box in line2) box.X += offsetAmount;
            });
            return;
        }

        foreach (var box in boxes)
            box.LayoutDirection = box.Index == context.HeroIndex
                ? SonnetLayoutDirection.Vertical
                : SonnetLayoutDirection.Horizontal;
        PlaceWithGlobalFit(context, _ =>
        {
            var heroOnRight = context.HeroIndex == boxes.Count - 1;
            var blockLeft = -context.Width * 0.40;
            var blockRight = context.Width * 0.40;
            var currentY = -context.Height * 0.34;

            void FlowWords(IReadOnlyList<int> indices, Func<double, (double Left, double Right)> regionFor)
            {
                var region = regionFor(currentY);
                var currentX = region.Left;
                var rowHeight = 0d;
                foreach (var index in indices)
                {
                    var box = boxes[index];
                    if (currentX > region.Left && currentX + box.MeasuredWidth > region.Right)
                    {
                        currentY += rowHeight + context.StackGap;
                        region = regionFor(currentY);
                        currentX = region.Left;
                        rowHeight = 0;
                    }
                    box.X = currentX + box.MeasuredWidth / 2;
                    box.Y = currentY + box.MeasuredHeight / 2;
                    box.EnterX = heroOnRight ? -25 : 25;
                    box.EnterY = 0;
                    currentX += box.MeasuredWidth + context.FlowGap;
                    rowHeight = Math.Max(rowHeight, box.MeasuredHeight);
                }
                if (indices.Count > 0) currentY += rowHeight;
            }

            var beforeIndices = boxes.Take(context.HeroIndex).Select(box => box.Index).ToArray();
            var afterIndices = boxes.Skip(context.HeroIndex + 1).Select(box => box.Index).ToArray();
            FlowWords(beforeIndices, _ => (blockLeft, blockRight));
            currentY += context.StackGap;

            var pillarLeft = heroOnRight ? blockRight - heroBox.MeasuredWidth : blockLeft;
            heroBox.X = pillarLeft + heroBox.MeasuredWidth / 2;
            heroBox.Y = currentY + heroBox.MeasuredHeight / 2;
            var pillarBottom = currentY + heroBox.MeasuredHeight + context.StackGap;
            var besideLeft = heroOnRight ? blockLeft : pillarLeft + heroBox.MeasuredWidth + context.FlowGap;
            var besideRight = heroOnRight ? pillarLeft - context.FlowGap : blockRight;
            FlowWords(afterIndices, rowTop => rowTop < pillarBottom - 0.5
                ? (besideLeft, besideRight)
                : (blockLeft, blockRight));
        });
    }

    public static void LayoutFragmentCollage(SonnetFlowLayoutContext context, int variant)
    {
        var boxes = context.Boxes;
        var heroBox = boxes[context.HeroIndex];

        static double RectSeparation(Rect first, Rect second) => Math.Max(
            Math.Max(first.Left - second.Right, second.Left - first.Right),
            Math.Max(first.Top - second.Bottom, second.Top - first.Bottom));

        for (var index = 0; index < boxes.Count; index++)
        {
            var box = boxes[index];
            if (index == context.HeroIndex) continue;
            if (Math.Abs((int)Math.Round(box.Rotation / (Math.PI / 2), MidpointRounding.AwayFromZero) % 2) == 1)
            {
                var rotatedWidth = box.MeasuredHeight;
                box.MeasuredHeight = box.MeasuredWidth;
                box.MeasuredWidth = rotatedWidth;
            }
            box.Rotation = 0;
        }

        PlaceWithGlobalFit(context, globalScale =>
        {
            heroBox.X = 0;
            heroBox.Y = 0;
            var baseRadius = Math.Sqrt(
                heroBox.MeasuredWidth * heroBox.MeasuredWidth
                + heroBox.MeasuredHeight * heroBox.MeasuredHeight) / 2 + context.StackGap;
            var count = Math.Max(1, boxes.Count - 1);
            const double squash = 0.65;
            var placed = new List<Rect>
            {
                new(
                    heroBox.X - heroBox.MeasuredWidth / 2,
                    heroBox.X + heroBox.MeasuredWidth / 2,
                    heroBox.Y - heroBox.MeasuredHeight / 2,
                    heroBox.Y + heroBox.MeasuredHeight / 2),
            };
            var angle = Math.PI / 4;
            var supportIndex = 0;
            for (var index = 0; index < boxes.Count; index++)
            {
                if (index == context.HeroIndex) continue;
                var box = boxes[index];
                var radius = baseRadius;
                if (variant == 1)
                    radius += (35 + supportIndex / (double)count * 150) * globalScale;
                else if (variant == 2)
                    radius += (supportIndex % 2 == 1 ? 140 : 50) * globalScale;
                else
                    radius += (45 + supportIndex * 23 % 90) * globalScale;
                supportIndex++;

                var candidate = angle;
                var rect = new Rect(0, 0, 0, 0);
                var resolvedRadius = radius;
                var placedClear = false;
                for (var ring = 0; ring < 14 && !placedClear; ring++)
                {
                    for (var attempt = 0; attempt < 400; attempt++)
                    {
                        rect = new Rect(
                            Math.Cos(candidate) * resolvedRadius - box.MeasuredWidth / 2,
                            Math.Cos(candidate) * resolvedRadius + box.MeasuredWidth / 2,
                            Math.Sin(candidate) * resolvedRadius * squash - box.MeasuredHeight / 2,
                            Math.Sin(candidate) * resolvedRadius * squash + box.MeasuredHeight / 2);
                        if (placed.All(entry => RectSeparation(entry, rect) >= context.FlowGap))
                        {
                            placedClear = true;
                            break;
                        }
                        candidate += 0.07;
                    }
                    if (!placedClear) resolvedRadius += (36 + ring * 12) * globalScale;
                }

                angle = candidate + 0.02;
                placed.Add(rect);
                box.X = heroBox.X + Math.Cos(candidate) * resolvedRadius;
                box.Y = heroBox.Y + Math.Sin(candidate) * resolvedRadius * squash;
                box.LayoutDirection = Math.Abs(Math.Cos(candidate)) >= Math.Abs(Math.Sin(candidate))
                    ? SonnetLayoutDirection.Vertical
                    : SonnetLayoutDirection.Horizontal;
                box.EnterX = Math.Cos(candidate) * -60;
                box.EnterY = Math.Sin(candidate) * -60;
            }
        });
    }

    public static void LayoutCrossStack(SonnetFlowLayoutContext context)
    {
        var boxes = context.Boxes;
        var heroBox = boxes[context.HeroIndex];
        var topCount = context.HeroIndex / 2;
        var afterCount = boxes.Count - 1 - context.HeroIndex;
        var rightCount = (int)Math.Ceiling(afterCount / 2d);

        double FillColumn(IReadOnlyList<SonnetFlowLayoutBox> column)
        {
            if (column.Count == 0) return 0;
            var available = Math.Max(0, context.Height * 0.46 - heroBox.MeasuredHeight / 2 - context.StackGap);
            if (available <= 0) return 0;
            var gaps = context.StackGap * (column.Count - 1);
            var contentHeight = column.Sum(box => box.MeasuredHeight);
            var target = available * 0.72;
            if (contentHeight + gaps < target)
            {
                var boost = Math.Min(2.2, (target - gaps) / Math.Max(1, contentHeight));
                foreach (var box in column)
                {
                    var capped = Math.Min(boost, heroBox.FontScale * 0.6 / box.FontScale);
                    if (capped <= 1.05) continue;
                    box.FontScale *= capped;
                    box.MeasuredWidth *= capped;
                    box.MeasuredHeight *= capped;
                }
            }

            if (column.Count < 2) return 0;
            var grown = column.Sum(box => box.MeasuredHeight);
            var pitch = (available * 0.95 - grown) / (column.Count - 1);
            return Math.Max(0, Math.Min(context.StackGap * 2, pitch - context.StackGap));
        }

        PlaceWithGlobalFit(context, _ =>
        {
            heroBox.X = 0;
            heroBox.Y = 0;
            var topStretch = FillColumn(boxes.Take(topCount).ToArray());
            var bottomStretch = FillColumn(boxes.Skip(context.HeroIndex + rightCount + 1).ToArray());

            var currentX = heroBox.X - heroBox.MeasuredWidth / 2 - context.StackGap;
            for (var index = context.HeroIndex - 1; index >= topCount; index--)
            {
                var box = boxes[index];
                box.LayoutDirection = SonnetLayoutDirection.Horizontal;
                box.X = currentX - box.MeasuredWidth / 2;
                box.Y = heroBox.Y + (index % 2 == 0 ? 10 : -10);
                currentX -= box.MeasuredWidth + context.FlowGap;
                box.EnterX = -30;
                box.EnterY = 0;
            }

            var currentY = heroBox.Y - heroBox.MeasuredHeight / 2 - context.StackGap;
            for (var index = topCount - 1; index >= 0; index--)
            {
                var box = boxes[index];
                box.LayoutDirection = SonnetLayoutDirection.Vertical;
                box.X = heroBox.X + (index % 2 == 0 ? 15 : -15);
                box.Y = currentY - box.MeasuredHeight / 2;
                currentY -= box.MeasuredHeight + context.StackGap + topStretch;
                box.EnterX = 0;
                box.EnterY = -30;
            }

            currentX = heroBox.X + heroBox.MeasuredWidth / 2 + context.StackGap;
            for (var index = context.HeroIndex + 1; index <= context.HeroIndex + rightCount; index++)
            {
                var box = boxes[index];
                box.LayoutDirection = SonnetLayoutDirection.Horizontal;
                box.X = currentX + box.MeasuredWidth / 2;
                box.Y = heroBox.Y + (index % 2 == 0 ? 10 : -10);
                currentX += box.MeasuredWidth + context.FlowGap;
                box.EnterX = 30;
                box.EnterY = 0;
            }

            currentY = heroBox.Y + heroBox.MeasuredHeight / 2 + context.StackGap;
            for (var index = context.HeroIndex + rightCount + 1; index < boxes.Count; index++)
            {
                var box = boxes[index];
                box.LayoutDirection = SonnetLayoutDirection.Vertical;
                box.X = heroBox.X + (index % 2 == 0 ? 15 : -15);
                box.Y = currentY + box.MeasuredHeight / 2;
                currentY += box.MeasuredHeight + context.StackGap + bottomStretch;
                box.EnterX = 0;
                box.EnterY = 30;
            }
        });
    }

    private readonly record struct Rect(double Left, double Right, double Top, double Bottom);
}
