namespace AvaloniaSilkEffects.Sonnet;

// Exact port of Folia v0.7.2 sonnetPosterBlocksLayout.ts.
public sealed class SonnetPosterBlockBox
{
    public bool IsHero { get; init; }
    public bool IsSemiHero { get; init; }
    public string DisplayText { get; set; } = "";
    public string? VerticalDisplayText { get; init; }
    public double? VerticalMeasuredWidth { get; init; }
    public double? VerticalMeasuredHeight { get; init; }
    public double? VerticalFontScale { get; init; }
    public double FontScale { get; set; }
    public double MeasuredWidth { get; set; }
    public double MeasuredHeight { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Rotation { get; set; }
    public bool Vertical { get; set; }
    public SonnetLayoutDirection LayoutDirection { get; set; }
    public double EnterX { get; set; }
    public double EnterY { get; set; }
}

public sealed record SonnetPosterBlocksPlan(
    IReadOnlyList<SonnetPosterBlockBox> Placements,
    double Width,
    double Height,
    double Gap);

public static class SonnetPosterBlocksLayout
{
    public static SonnetPosterBlocksPlan Layout(
        IReadOnlyList<SonnetPosterBlockBox> boxes,
        double width,
        double height,
        double baseFontSize,
        uint seed = 0)
    {
        if (boxes.Count == 0) return new([], 0, 0, 0);
        var gap = Math.Clamp(baseFontSize * 0.35, 16, 40);
        var lineGap = gap * 1.15;
        var canvas = new ScreenRect(-width * 0.42, -height * 0.40, width * 0.84, height * 0.80);
        var orientation = seed % 2 == 0 ? SonnetLayoutDirection.Horizontal : SonnetLayoutDirection.Vertical;
        var space = orientation == SonnetLayoutDirection.Horizontal
            ? new FlowSpace(orientation, canvas.Width, canvas.Height)
            : new FlowSpace(orientation, canvas.Height, canvas.Width);

        var attempt = AttemptFlowLayout(boxes, space, 1, gap, lineGap, seed);
        foreach (var globalScale in new[] { 0.92, 0.84, 0.76, 0.68, 0.6, 0.52 })
        {
            if (attempt.VTotal <= space.V + 0.5) break;
            attempt = AttemptFlowLayout(boxes, space, globalScale, gap, lineGap, seed);
        }
        if (attempt.VTotal > space.V)
        {
            var fitScale = space.V / attempt.VTotal;
            foreach (var placement in attempt.Placements)
            {
                placement.Rect = new(
                    placement.Rect.U * fitScale,
                    placement.Rect.V * fitScale,
                    placement.Rect.USize * fitScale,
                    placement.Rect.VSize * fitScale);
                placement.Scale *= fitScale;
            }
            attempt.VTotal = space.V;
        }

        var vShift = Math.Max(0, (space.V - attempt.VTotal) / 2);
        foreach (var placement in attempt.Placements)
        {
            var box = placement.Box;
            var rect = placement.Rect with { V = placement.Rect.V + vShift };
            var screen = FlowToScreen(space, rect, canvas);
            box.FontScale = placement.Scale;
            box.MeasuredWidth = screen.Width;
            box.MeasuredHeight = screen.Height;
            box.X = screen.X + screen.Width / 2;
            box.Y = screen.Y + screen.Height / 2;
            box.Rotation = 0;
            box.Vertical = placement.Vertical;
            if (placement.Vertical && box.VerticalDisplayText is not null)
                box.DisplayText = box.VerticalDisplayText;
            box.LayoutDirection = orientation;
            if (orientation == SonnetLayoutDirection.Horizontal)
            {
                box.EnterX = (box.X < 0 ? -1 : 1) * Math.Min(28, baseFontSize * 0.45);
                box.EnterY = Math.Min(18, baseFontSize * 0.25);
            }
            else
            {
                box.EnterX = Math.Min(18, baseFontSize * 0.25);
                box.EnterY = (box.Y < 0 ? -1 : 1) * Math.Min(28, baseFontSize * 0.45);
            }
        }

        return new(boxes, canvas.Width, canvas.Height, gap);
    }

    private static FlowAttempt AttemptFlowLayout(
        IReadOnlyList<SonnetPosterBlockBox> boxes,
        FlowSpace space,
        double globalScale,
        double chipGap,
        double lineGap,
        uint seed)
    {
        var items = Partition(boxes);
        var placements = new List<FlowPlacement>();
        var floats = new List<ZoneFloat>();
        var vCursor = 0d;
        var ownBandOnEndSide = ((seed >> 1) & 1) == 1;

        Dimensions Measure(SonnetPosterBlockBox box)
        {
            var useVertical = space.Orientation == SonnetLayoutDirection.Vertical
                && box.VerticalMeasuredWidth.HasValue
                && box.VerticalMeasuredHeight.HasValue
                && box.VerticalFontScale.HasValue;
            var baseScale = useVertical ? box.VerticalFontScale!.Value : box.FontScale;
            return new(
                useVertical,
                baseScale,
                (useVertical ? box.VerticalMeasuredWidth!.Value : box.MeasuredWidth) * globalScale,
                (useVertical ? box.VerticalMeasuredHeight!.Value : box.MeasuredHeight) * globalScale);
        }

        (double USize, double VSize) ToFlowSize(double boxWidth, double boxHeight) =>
            space.Orientation == SonnetLayoutDirection.Horizontal
                ? (boxWidth, boxHeight)
                : (boxHeight, boxWidth);

        void PruneFloats()
        {
            for (var index = floats.Count - 1; index >= 0; index--)
                if (floats[index].VBottom <= vCursor) floats.RemoveAt(index);
        }

        for (var itemIndex = 0; itemIndex < items.Count; itemIndex++)
        {
            PruneFloats();
            var item = items[itemIndex];
            if (item.Group is not null)
            {
                var reservedU = floats.Sum(entry => entry.Extent);
                var capacity = Math.Max(chipGap * 2, space.U - reservedU);
                var uStart = reservedU;
                var chips = item.Group.Select(box =>
                {
                    var dims = Measure(box);
                    var flow = ToFlowSize(dims.Width, dims.Height);
                    return new Chip(box, dims, flow.USize, flow.VSize);
                }).ToArray();
                var line = new List<Chip>();
                var lineUsedU = 0d;

                void FlushLine()
                {
                    if (line.Count == 0) return;
                    var lineV = line.Max(chip => chip.VSize * chip.Shrink);
                    var leftover = capacity - lineUsedU;
                    var spread = line.Count > 1 && leftover > 0
                        ? Math.Min(leftover / (line.Count - 1), chipGap * 2.5)
                        : 0;
                    var uCursor = uStart;
                    foreach (var chip in line)
                    {
                        placements.Add(new(
                            chip.Box,
                            new(uCursor, vCursor, chip.USize * chip.Shrink, chip.VSize * chip.Shrink),
                            chip.Dimensions.BaseScale * globalScale * chip.Shrink,
                            chip.Dimensions.UseVertical));
                        uCursor += chip.USize * chip.Shrink + chipGap + spread;
                    }
                    vCursor += lineV + lineGap;
                    PruneFloats();
                    line.Clear();
                    lineUsedU = 0;
                }

                foreach (var chip in chips)
                {
                    var needed = lineUsedU + (line.Count > 0 ? chipGap : 0) + chip.USize;
                    if (needed > capacity && line.Count > 0) FlushLine();
                    if (chip.USize > capacity)
                    {
                        chip.Shrink = Math.Max(0.5, capacity / chip.USize);
                        lineUsedU = 0;
                        line.Add(chip);
                        FlushLine();
                        continue;
                    }
                    lineUsedU += (line.Count > 0 ? chipGap : 0) + chip.USize;
                    line.Add(chip);
                }
                FlushLine();
                continue;
            }

            var zone = item.Zone!;
            vCursor = Math.Max(vCursor, floats.Count == 0 ? 0 : floats.Max(entry => entry.VBottom));
            floats.Clear();
            var dims = Measure(zone);
            var flow = ToFlowSize(dims.Width, dims.Height);
            var followedByGroup = itemIndex + 1 < items.Count && items[itemIndex + 1].Group is not null;
            var zoneShrink = Math.Min(1, Math.Min(
                space.U * (followedByGroup ? 0.62 : 0.9) / flow.USize,
                space.V * 0.66 / flow.VSize));
            var uSize = flow.USize * zoneShrink;
            var vSize = flow.VSize * zoneShrink;
            var u = items.Count == 1
                ? (space.U - uSize) / 2
                : followedByGroup ? 0
                : ownBandOnEndSide ? space.U - uSize : 0;
            placements.Add(new(zone, new(u, vCursor, uSize, vSize),
                dims.BaseScale * globalScale * zoneShrink, dims.UseVertical));
            if (followedByGroup)
                floats.Add(new(uSize + chipGap, vCursor + vSize + lineGap));
            else
            {
                vCursor += vSize + lineGap;
                ownBandOnEndSide = !ownBandOnEndSide;
            }
        }

        return new(placements, placements.Count == 0
            ? 0
            : placements.Max(placement => placement.Rect.V + placement.Rect.VSize));
    }

    private static List<FlowItem> Partition(IReadOnlyList<SonnetPosterBlockBox> boxes)
    {
        var items = new List<FlowItem>();
        var group = new List<SonnetPosterBlockBox>();
        foreach (var box in boxes)
        {
            if (box.IsHero || box.IsSemiHero)
            {
                if (group.Count > 0) items.Add(new(null, [.. group]));
                group.Clear();
                items.Add(new(box, null));
            }
            else group.Add(box);
        }
        if (group.Count > 0) items.Add(new(null, [.. group]));
        return items;
    }

    private static ScreenRect FlowToScreen(FlowSpace space, FlowRect rect, ScreenRect canvas) =>
        space.Orientation == SonnetLayoutDirection.Horizontal
            ? new(canvas.X + rect.U, canvas.Y + rect.V, rect.USize, rect.VSize)
            : new(canvas.X + canvas.Width - rect.V - rect.VSize, canvas.Y + rect.U, rect.VSize, rect.USize);

    private sealed record FlowItem(SonnetPosterBlockBox? Zone, IReadOnlyList<SonnetPosterBlockBox>? Group);
    private sealed record FlowSpace(SonnetLayoutDirection Orientation, double U, double V);
    private sealed record ZoneFloat(double Extent, double VBottom);
    private sealed record Dimensions(bool UseVertical, double BaseScale, double Width, double Height);
    private sealed class Chip(SonnetPosterBlockBox box, Dimensions dimensions, double uSize, double vSize)
    {
        public SonnetPosterBlockBox Box { get; } = box;
        public Dimensions Dimensions { get; } = dimensions;
        public double USize { get; } = uSize;
        public double VSize { get; } = vSize;
        public double Shrink { get; set; } = 1;
    }
    private sealed class FlowPlacement(SonnetPosterBlockBox box, FlowRect rect, double scale, bool vertical)
    {
        public SonnetPosterBlockBox Box { get; } = box;
        public FlowRect Rect { get; set; } = rect;
        public double Scale { get; set; } = scale;
        public bool Vertical { get; } = vertical;
    }
    private sealed class FlowAttempt(List<FlowPlacement> placements, double vTotal)
    {
        public List<FlowPlacement> Placements { get; } = placements;
        public double VTotal { get; set; } = vTotal;
    }
    private sealed record FlowRect(double U, double V, double USize, double VSize);
    private sealed record ScreenRect(double X, double Y, double Width, double Height);
}
