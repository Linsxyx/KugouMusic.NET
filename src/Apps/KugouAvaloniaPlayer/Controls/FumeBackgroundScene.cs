using System;
using System.Collections.Generic;
using System.Linq;

namespace KugouAvaloniaPlayer.Controls;

// Fume's paper halo and jittered spark grid, using the reference scene's distributions.
internal static class FumeBackgroundScene
{
    public static IReadOnlyList<FumeBackgroundShape> Build(
        FumeArticleLayout? article, double viewportWidth, double viewportHeight, string seed = "fume")
    {
        if (viewportWidth <= 0 || viewportHeight <= 0)
            return [];
        var width = Math.Max(article?.Width ?? viewportWidth * 1.8, viewportWidth * 1.2);
        var height = Math.Max(article?.Height ?? viewportHeight * 1.8, viewportHeight * 1.2);
        var paper = article?.PaperBounds ?? new FumePaperBounds(width * 0.24, height * 0.18, width * 0.76, height * 0.82);
        var unit = Math.Clamp(Math.Min(viewportWidth, viewportHeight) * 0.72, 320, 760);
        var shapeCount = width > height ? 8 : 7;
        var sparkCount = width > height ? 12 : 9;
        var shapes = new List<FumeBackgroundShape>(shapeCount + sparkCount);
        for (var i = 0; i < shapeCount; i++)
        {
            var key = FormattableString.Invariant($"{seed}:{width}:{height}:{i}");
            var kind = (FumeShapeKind)(i % 3);
            var size = unit * Sample(key, "size", 0.82, 1.36);
            var (x, y) = HaloAnchor(paper, width, height, size, key);
            shapes.Add(new FumeBackgroundShape(kind, x, y, size,
                Sample(key, "rotation", -Math.PI * 0.2, Math.PI * 0.2),
                Sample(key, "rotation-speed", -0.045, 0.045),
                Sample(key, "opacity", 0.01, 0.16), Sample(key, "depth"), -1,
                Sample(key, "stroke-width", 0.25, 2.1), Sample(key, "color") > 0.5,
                Sample(key, "gap-start", -Math.PI, Math.PI),
                Sample(key, "gap-size", Math.PI * 0.12, Math.PI * 0.24)));
        }

        int[] bands = [4, 3, 2, 4, 1];
        var columns = (int)Math.Ceiling(Math.Sqrt(sparkCount * width / Math.Max(height, 1)));
        var rows = (int)Math.Ceiling((double)sparkCount / columns);
        var cellWidth = width * 0.6 / columns;
        var cellHeight = height * 0.64 / rows;
        for (var i = 0; i < sparkCount; i++)
        {
            var key = FormattableString.Invariant($"{seed}:{width}:{height}:spark:{i}");
            var x = width * 0.2 + (i % columns + 0.5 + Sample(key, "jitter-x", -0.32, 0.32)) * cellWidth;
            var y = height * 0.18 + (i / columns + 0.5 + Sample(key, "jitter-y", -0.32, 0.32)) * cellHeight;
            shapes.Add(new FumeBackgroundShape(FumeShapeKind.Spark,
                Math.Clamp(x, width * 0.2, width * 0.8), Math.Clamp(y, height * 0.18, height * 0.82),
                unit * Sample(key, "size", 0.1, 0.24),
                Sample(key, "rotation", -Math.PI, Math.PI), Sample(key, "rotation-speed", -0.18, 0.18),
                Sample(key, "opacity", 0.08, 0.22), Sample(key, "depth"), bands[i % bands.Length],
                Sample(key, "stroke-width", 0.75, 1.7), Sample(key, "color") > 0.5));
        }
        return shapes.OrderBy(shape => shape.Depth).ToArray();
    }

    private static (double X, double Y) HaloAnchor(
        FumePaperBounds paper, double width, double height, double size, string key)
    {
        double x, y;
        if (Sample(key, "inside-chance") < 0.22)
        {
            x = Sample(key, "inside-x", paper.Left + size * 0.12, paper.Right - size * 0.12);
            y = Sample(key, "inside-y", paper.Top + size * 0.12, paper.Bottom - size * 0.12);
        }
        else
        {
            var side = (int)(Sample(key, "side") * 4) % 4;
            var overflowX = size * Sample(key, "overflow-x", 0.16, 0.24);
            var overflowY = size * Sample(key, "overflow-y", 0.16, 0.24);
            x = side switch
            {
                0 => paper.Left - overflowX,
                1 => paper.Right + overflowX,
                _ => Sample(key, "x", paper.Left - size * 0.12, paper.Right + size * 0.12) + size * Sample(key, "span-jitter-x", -0.18, 0.18)
            };
            y = side switch
            {
                2 => paper.Top - overflowY,
                3 => paper.Bottom + overflowY,
                _ => Sample(key, "y", paper.Top - size * 0.12, paper.Bottom + size * 0.12) + size * Sample(key, "span-jitter-y", -0.18, 0.18)
            };
        }
        return (Math.Clamp(x, 0, width), Math.Clamp(y, 0, height));
    }

    private static double Sample(string seed, string property, double min = 0, double max = 1)
    {
        uint hash = 2166136261;
        foreach (var c in $"{seed}:{property}")
            hash = unchecked((hash ^ c) * 16777619);
        return min + (max - min) * (hash % 10000 / 10000d);
    }
}
