namespace AvaloniaSilkEffects.Sonnet;

// Exact port of Folia v0.7.2 sonnetTypographyRoles.ts.
public static class SonnetTypographyRoles
{
    private const int SemiHeroMinGap = 2;
    private const int SemiHeroMinVisibleLength = 2;
    private const int SemiHeroMinLineWords = 4;
    private const double SemiHeroScoreRatio = 0.35;
    private const int SemiHeroMultiWordCount = 9;

    public static bool IsEmphasis(SonnetSegmentRole role) =>
        role is SonnetSegmentRole.Hero or SonnetSegmentRole.SemiHero;

    public static int ResolveFontWeight(int? configuredFontWeight, SonnetSegmentRole role)
    {
        if (configuredFontWeight is not null)
        {
            var clamped = Math.Clamp(configuredFontWeight.Value, 100, 900);
            return (int)Math.Round(clamped / 10d, MidpointRounding.AwayFromZero) * 10;
        }

        if (IsEmphasis(role)) return 900;
        return role == SonnetSegmentRole.Decoration ? 300 : 700;
    }

    public static int VisibleLength(SonnetSemanticSegment segment) =>
        segment.Graphemes.Count(item => item.Text.Trim().Length > 0);

    public static double HeroScore(SonnetSemanticSegment segment)
    {
        var lengthScore = Math.Min(VisibleLength(segment), 8) * 14;
        var durationScore = Math.Min(2.5, Math.Max(0, segment.EndTime - segment.StartTime)) * 18;
        return lengthScore + durationScore;
    }

    public static int FindHeroIndex(IReadOnlyList<SonnetSemanticSegment> segments)
    {
        var bestIndex = -1;
        for (var index = 0; index < segments.Count; index++)
        {
            if (segments[index].IsWordLike)
            {
                bestIndex = index;
                break;
            }
        }

        var bestScore = double.NegativeInfinity;
        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            if (!segment.IsWordLike || VisibleLength(segment) == 0) continue;
            var score = HeroScore(segment);
            if (score <= bestScore) continue;
            bestScore = score;
            bestIndex = index;
        }

        return Math.Max(0, bestIndex);
    }

    public static IReadOnlyList<int> FindSemiHeroIndices(
        IReadOnlyList<SonnetSemanticSegment> segments,
        int heroIndex)
    {
        if (heroIndex < 0 || heroIndex >= segments.Count) return [];

        var wordLikeCount = segments.Count(segment =>
            segment.IsWordLike && VisibleLength(segment) > 0);
        if (wordLikeCount < SemiHeroMinLineWords) return [];

        var threshold = HeroScore(segments[heroIndex]) * SemiHeroScoreRatio;
        var candidates = segments
            .Select((segment, index) => new Candidate(segment, index))
            .Where(item =>
                item.Index != heroIndex
                && item.Segment.IsWordLike
                && VisibleLength(item.Segment) >= SemiHeroMinVisibleLength
                && Math.Abs(item.Index - heroIndex) >= SemiHeroMinGap
                && HeroScore(item.Segment) >= threshold)
            .ToArray();
        if (candidates.Length == 0) return [];

        Candidate? BestOf(IEnumerable<Candidate> source)
        {
            Candidate? best = null;
            foreach (var item in source)
            {
                if (best is null || HeroScore(item.Segment) > HeroScore(best.Segment)) best = item;
            }
            return best;
        }

        var heroLeansEarly = heroIndex <= (segments.Count - 1) / 2d;
        var primarySide = candidates.Where(item => heroLeansEarly
            ? item.Index > heroIndex
            : item.Index < heroIndex).ToArray();
        var secondarySide = candidates.Where(item => heroLeansEarly
            ? item.Index < heroIndex
            : item.Index > heroIndex).ToArray();

        var picks = new List<int>(2);
        var primary = BestOf(primarySide) ?? BestOf(secondarySide);
        if (primary is not null) picks.Add(primary.Index);
        if (wordLikeCount >= SemiHeroMultiWordCount && primary is not null)
        {
            var secondary = BestOf(secondarySide.Where(item =>
                Math.Abs(item.Index - primary.Index) >= SemiHeroMinGap));
            if (secondary is not null) picks.Add(secondary.Index);
        }

        picks.Sort();
        return picks;
    }

    public static int FindSemiHeroIndex(
        IReadOnlyList<SonnetSemanticSegment> segments,
        int heroIndex) => FindSemiHeroIndices(segments, heroIndex).FirstOrDefault(-1);

    private sealed record Candidate(SonnetSemanticSegment Segment, int Index);
}
