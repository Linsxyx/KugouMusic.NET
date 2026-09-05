namespace AvaloniaSilkEffects.Sonnet;

/// <summary>Folia d5b8b24d colorPalette.ts weighted median-cut of straight RGBA pixels.</summary>
public static class SonnetCoverPalette
{
    public static IReadOnlyList<EffectColor> Extract(ReadOnlySpan<byte> rgba, int count = 6)
    {
        if (rgba.Length % 4 != 0) throw new ArgumentException("Expected complete RGBA pixels.", nameof(rgba));
        if (count <= 0) return [];
        var histogram = new Dictionary<int, Sample>();
        var ordered = new List<Sample>();
        for (var i = 0; i < rgba.Length; i += 4)
        {
            if (rgba[i + 3] < 128) continue;
            var key = (rgba[i] >> 4) << 8 | (rgba[i + 1] >> 4) << 4 | rgba[i + 2] >> 4;
            if (!histogram.TryGetValue(key, out var sample))
            {
                sample = new Sample();
                histogram.Add(key, sample);
                ordered.Add(sample);
            }
            sample.R += rgba[i]; sample.G += rgba[i + 1]; sample.B += rgba[i + 2]; sample.Weight++;
        }
        if (ordered.Count == 0) return [];
        foreach (var sample in ordered)
        {
            sample.R = Round(sample.R / (double)sample.Weight);
            sample.G = Round(sample.G / (double)sample.Weight);
            sample.B = Round(sample.B / (double)sample.Weight);
        }
        var buckets = new List<List<Sample>> { ordered };
        while (buckets.Count < Math.Min(count, ordered.Count))
        {
            var selected = -1;
            var score = -1d;
            for (var i = 0; i < buckets.Count; i++)
            {
                var bucket = buckets[i];
                if (bucket.Count < 2) continue;
                var candidate = Enumerable.Range(0, 3).Max(c => Range(bucket, c)) * Math.Sqrt(bucket.Sum(s => s.Weight));
                if (candidate <= score) continue;
                selected = i; score = candidate;
            }
            if (selected < 0) break;
            var source = buckets[selected];
            var channel = 0;
            for (var c = 1; c < 3; c++)
                if (Range(source, c) > Range(source, channel)) channel = c;
            // OrderBy preserves source order for ties, as JavaScript's stable sort does.
            var sorted = source.OrderBy(s => s.Channel(channel)).ToList();
            var midpoint = source.Sum(s => s.Weight) / 2d;
            long accumulated = 0;
            var split = 1;
            for (var i = 0; i < sorted.Count - 1; i++)
            {
                accumulated += sorted[i].Weight;
                split = i + 1;
                if (accumulated >= midpoint) break;
            }
            buckets.RemoveAt(selected);
            buckets.Insert(selected, sorted.GetRange(split, sorted.Count - split));
            buckets.Insert(selected, sorted.GetRange(0, split));
        }
        return buckets.Select(bucket =>
        {
            var weight = bucket.Sum(s => s.Weight);
            var color = new EffectColor(
                Round(bucket.Sum(s => s.R * s.Weight) / (double)weight) / 255f,
                Round(bucket.Sum(s => s.G * s.Weight) / (double)weight) / 255f,
                Round(bucket.Sum(s => s.B * s.Weight) / (double)weight) / 255f);
            return (color, weight);
        }).OrderByDescending(item => item.weight).Select(item => item.color).ToArray();
    }

    private static long Round(double value) => (long)Math.Floor(value + 0.5);
    private static long Range(List<Sample> colors, int channel) =>
        colors.Max(s => s.Channel(channel)) - colors.Min(s => s.Channel(channel));
    private sealed class Sample
    {
        internal long R, G, B, Weight;
        internal long Channel(int channel) => channel == 0 ? R : channel == 1 ? G : B;
    }
}
