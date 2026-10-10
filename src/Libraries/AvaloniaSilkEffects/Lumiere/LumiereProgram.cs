using System.Text.RegularExpressions;
using AvaloniaSilkEffects.Lumiere.Rigs;
using AvaloniaSilkEffects.Lumiere.Text;

namespace AvaloniaSilkEffects.Lumiere;

public enum LumiereParagraphKind
{
    Breath,
    Verse,
    Lift,
    Chorus,
    Break,
    Outro,
}

public enum LumiereParagraphBoundary
{
    SongStart,
    TimeGap,
    Metadata,
    DurationCap,
    LineCap,
    Intro,
    Instrumental,
}

public enum LumiereTransitionKind
{
    /// <summary>熄灯: the scene itself puts its light out.</summary>
    LightsOut,
    /// <summary>闪白: slight zoom + blur peaking at the boundary.</summary>
    FlareCut,
    /// <summary>拉焦: blur out, blur in.</summary>
    FocusPull,
}

/// <summary>A compiled shot: profile kind, covered lines (song line indices; empty for interludes), time span.</summary>
public sealed record LumiereProgramShot(string Id, string Kind, IReadOnlyList<int> LineIndices, double StartTime, double EndTime,
    double LyricEndTime, bool IsBridge);

public sealed record LumiereTransitionOut(LumiereTransitionKind Kind, double StartTime, double EndTime);

/// <summary>
/// One scene unit: starts at its first line (the first at 0) and runs to the next paragraph's start (the last to the song end);
/// a long interlude after it shows up as bridge shots inside it.
/// </summary>
public sealed record LumiereParagraph(
    string Id,
    int Index,
    LumiereParagraphKind Kind,
    LumiereParagraphBoundary Boundary,
    double StartTime,
    double EndTime,
    double LyricEndTime,
    IReadOnlyList<int> LineIndices,
    IReadOnlyList<LumiereLine> Lines,
    IReadOnlyList<LumiereProgramShot> Shots,
    LumiereTransitionOut? TransitionOut,
    bool Opening,
    IReadOnlyList<LumiereSection>? Sections = null);

public sealed record LumiereProgram(
    string Seed,
    bool Instrumental,
    double ParagraphGapThreshold,
    // End of the last paragraph (the program covers [0, Duration]).
    double Duration,
    // When the last line finishes (credits start there); null for instrumentals.
    double? LyricEndTime,
    IReadOnlyList<LumiereParagraph> Paragraphs)
{
    /// <summary>The paragraph at <paramref name="time"/> (before the first counts as the first).</summary>
    public int ParagraphIndexAt(double time)
    {
        for (var index = Paragraphs.Count - 1; index >= 0; index--)
            if (time >= Paragraphs[index].StartTime) return index;
        return 0;
    }
}

/// <summary>
/// Folia lumiereProgram.ts + program.ts + lumiereStructure.ts + lumiereSeamless.ts: compiles a whole song. Lyrics →
/// paragraphs (same rules as tempera / sonnet) → per paragraph, shots of 1–2 lines with interlude shots for long gaps, each
/// cast a profile (mood from the paragraph kind, families not repeating, recent profiles avoided; the chain runs across
/// paragraphs) → paragraph transitions and starfall openings. Pure: the same lyrics and seed give the same program.
/// Instrumentals compile to interlude-only paragraphs over the duration (480 s by default) instead of virtual lines.
/// </summary>
public static partial class LumiereProgramCompiler
{
    // Shot planning.
    private const double MaxShotDuration = 7;
    private const int MaxLinesPerShot = 2;
    private const double LongLine = 3.6;
    private const double BridgeGap = 4;
    private const int Recent = 3;
    private static readonly string[] BridgeFamilies = ["motes", "astral", "zenith"];
    // Structure.
    private const double GapMultiplier = 2.5;
    private const double GapMin = 1.25;
    private const double GapMax = 3.5;
    private const int MaxParagraphLines = 6;
    private const double MaxParagraphDuration = 18;
    /// <summary>Replay the starfall opening only after a gap this long since the previous paragraph's last line.</summary>
    public const double ReopenGap = 2.5;
    private const double InstrumentalDuration = 480;
    private const double InstrumentalParagraph = 32;
    private const double BridgeShot = 10;
    private const double BridgeSplit = 16;

    [GeneratedRegex("chorus|副歌", RegexOptions.IgnoreCase)]
    private static partial Regex ChorusPart();

    [GeneratedRegex("bridge|break|間奏|ブリッジ", RegexOptions.IgnoreCase)]
    private static partial Regex BreakPart();

    [GeneratedRegex("[!?！？…]")]
    private static partial Regex Exclamation();

    private sealed record StructureLine(int SourceIndex, LumiereLine Line, double RenderEndTime);

    private sealed record Draft(List<StructureLine> Lines, LumiereParagraphBoundary Boundary);

    private sealed record Plan(List<StructureLine> Lines, LumiereParagraphKind Kind, LumiereParagraphBoundary Boundary,
        double StartTime, double EndTime, double LyricEndTime);

    private sealed record PlannedShot(List<StructureLine> Lines, double StartTime, double EndTime, double LyricEndTime, bool IsBridge);

    public static LumiereProgram Compile(IReadOnlyList<LumiereLine> lines, string? seed = null, double? duration = null, bool seamless = false)
    {
        var resolvedSeed = seed ?? "lumiere";
        var instrumental = lines.Count == 0;
        List<Plan> plans;
        double threshold = 0, programEnd;
        double? lyricEndTime = null;
        if (instrumental)
        {
            programEnd = Math.Max(1, duration is > 0 ? duration.Value : InstrumentalDuration);
            plans = InstrumentalPlans(programEnd);
        }
        else
        {
            var structure = lines.Select((line, index) => new StructureLine(index, line,
                Math.Max(line.StartTime, Math.Min(line.RenderEndTime, index + 1 < lines.Count ? lines[index + 1].StartTime : double.PositiveInfinity)))).ToList();
            threshold = GapThreshold(lines);
            var drafts = DraftParagraphs(structure, threshold);
            lyricEndTime = structure.Max(line => line.RenderEndTime);
            programEnd = Math.Max(lyricEndTime.Value, duration ?? 0);
            var firstStart = structure[0].Line.StartTime;
            plans = [];
            // A long enough intro gets its own interlude paragraph: the opening plays at 0 and the first line is not born dark.
            var intro = firstStart >= BridgeGap;
            if (intro) plans.Add(new Plan([], LumiereParagraphKind.Break, LumiereParagraphBoundary.Intro, 0, firstStart, firstStart));
            for (var index = 0; index < drafts.Count; index++)
            {
                var draft = drafts[index];
                var next = index + 1 < drafts.Count ? drafts[index + 1] : null;
                plans.Add(new Plan(draft.Lines, Classify(draft.Lines, index, drafts.Count), draft.Boundary,
                    index == 0 && !intro ? Math.Min(0, firstStart) : draft.Lines[0].Line.StartTime,
                    next is not null ? next.Lines[0].Line.StartTime : programEnd,
                    draft.Lines[^1].RenderEndTime));
            }
        }

        var recent = new List<string>();
        string? family = null;
        LumiereTransitionKind? previousTransition = null;
        var paragraphs = new List<LumiereParagraph>();
        for (var index = 0; index < plans.Count; index++)
        {
            var plan = plans[index];
            var planned = SplitLongBridges(PlanShots(plan.Lines, plan.StartTime, plan.EndTime));
            var shots = new List<LumiereProgramShot>();
            for (var shotIndex = 0; shotIndex < planned.Count; shotIndex++)
            {
                var shot = planned[shotIndex];
                var kind = CastShot(resolvedSeed, index, shotIndex, plan.Kind, shot.IsBridge, recent, family);
                recent.Add(kind);
                if (recent.Count > Recent) recent.RemoveAt(0);
                family = LumiereCatalog.ProfileOf(kind).Family;
                shots.Add(new LumiereProgramShot($"p{index}-lu{shotIndex}", kind, [.. shot.Lines.Select(line => line.SourceIndex)],
                    shot.StartTime, shot.EndTime, shot.LyricEndTime, shot.IsBridge));
            }

            // Transition out: ends where the next paragraph starts; its length follows the gap to it.
            LumiereTransitionOut? transitionOut = null;
            if (index + 1 < plans.Count)
            {
                var kind = LumiereTransitions.Choose(resolvedSeed, index, previousTransition);
                previousTransition = kind;
                var gap = plans[index + 1].StartTime - plan.LyricEndTime;
                transitionOut = new LumiereTransitionOut(kind,
                    Math.Max(plan.StartTime, plan.EndTime - LumiereTransitions.Duration(kind, gap)), plan.EndTime);
            }
            // The opening plays for the first paragraph, or after a clear gap since the previous one was sung.
            var opening = index == 0 || plan.StartTime - plans[index - 1].LyricEndTime >= ReopenGap;
            var lineIndices = plan.Lines.Select(line => line.SourceIndex).ToArray();
            paragraphs.Add(new LumiereParagraph($"lumiere-p{index}", index, plan.Kind, plan.Boundary, plan.StartTime, plan.EndTime,
                plan.LyricEndTime, lineIndices, [.. lineIndices.Select(lineIndex => lines[lineIndex])], shots, transitionOut, opening));
        }

        return new LumiereProgram(resolvedSeed, instrumental, threshold, programEnd, lyricEndTime,
            seamless ? Merge(paragraphs) : paragraphs);
    }

    /// <summary>Seamless: all paragraphs as one unit; shots kept, transitions and re-openings dropped, sections remembered.</summary>
    private static List<LumiereParagraph> Merge(List<LumiereParagraph> paragraphs)
    {
        if (paragraphs.Count < 2) return paragraphs;
        var first = paragraphs[0];
        var last = paragraphs[^1];
        return
        [
            new LumiereParagraph("lumiere-seamless", 0, first.Kind, first.Boundary, first.StartTime, last.EndTime, last.LyricEndTime,
                [.. paragraphs.SelectMany(p => p.LineIndices)], [.. paragraphs.SelectMany(p => p.Lines)], [.. paragraphs.SelectMany(p => p.Shots)],
                null, first.Opening, [.. paragraphs.Select(p => new LumiereSection(p.StartTime, p.EndTime))]),
        ];
    }

    private static List<Plan> InstrumentalPlans(double duration)
    {
        var count = Math.Max(1, (int)Math.Round(duration / InstrumentalParagraph, MidpointRounding.AwayFromZero));
        return [.. Enumerable.Range(0, count).Select(index =>
        {
            var start = duration * index / count;
            var end = index == count - 1 ? duration : duration * (index + 1) / count;
            return new Plan([], LumiereParagraphKind.Break, index == 0 ? LumiereParagraphBoundary.SongStart : LumiereParagraphBoundary.Instrumental, start, end, end);
        })];
    }

    // ---- structure (lumiereStructure.ts) ---------------------------------------------------------------------

    private static double Median(List<double> values)
    {
        if (values.Count == 0) return 0.5;
        values.Sort();
        var middle = values.Count / 2;
        return values.Count % 2 == 0 ? (values[middle - 1] + values[middle]) / 2 : values[middle];
    }

    public static double GapThreshold(IReadOnlyList<LumiereLine> lines)
    {
        var gaps = new List<double>();
        for (var index = 1; index < lines.Count; index++)
        {
            var gap = lines[index].StartTime - Math.Min(lines[index - 1].RenderEndTime, lines[index].StartTime);
            if (gap > 0) gaps.Add(gap);
        }
        return Math.Clamp(Median(gaps) * GapMultiplier, GapMin, GapMax);
    }

    private static bool MetadataChanged(LumiereLine previous, LumiereLine next) =>
        previous.BlockIndex is { } a && next.BlockIndex is { } b && a != b
        || previous.SongPart is { } p && next.SongPart is { } q && p != q;

    private static List<Draft> SplitOversized(Draft draft)
    {
        var output = new List<Draft>();
        var remaining = draft.Lines;
        var boundary = draft.Boundary;
        var guard = 0;
        while (remaining.Count > MaxParagraphLines
               || remaining.Count > 1 && remaining[^1].RenderEndTime - remaining[0].Line.StartTime > MaxParagraphDuration)
        {
            if (guard++ > 1000) break;
            var candidates = new List<(int SplitIndex, double Gap)>();
            for (var offset = 0; offset + 2 < remaining.Count - 1; offset++)
                candidates.Add((offset + 2, remaining[offset + 2].Line.StartTime - remaining[offset + 1].RenderEndTime));
            var valid = candidates.Where(candidate => !double.IsNaN(candidate.Gap)).OrderByDescending(candidate => candidate.Gap).ToList();
            var splitIndex = Math.Max(1, valid.Count > 0 ? valid[0].SplitIndex : Math.Min(4, remaining.Count - 1));
            output.AddRange(SplitOversized(new Draft(remaining[..splitIndex], boundary)));
            remaining = remaining[splitIndex..];
            boundary = output[^1].Lines.Count >= MaxParagraphLines ? LumiereParagraphBoundary.LineCap : LumiereParagraphBoundary.DurationCap;
        }
        output.Add(new Draft(remaining, boundary));
        return output;
    }

    private static List<Draft> DraftParagraphs(List<StructureLine> lines, double threshold)
    {
        var drafts = new List<Draft>();
        var current = new Draft([], LumiereParagraphBoundary.SongStart);
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var previous = index > 0 ? lines[index - 1] : null;
            var gap = previous is null ? 0 : line.Line.StartTime - previous.RenderEndTime;
            LumiereParagraphBoundary? boundary = previous is not null && MetadataChanged(previous.Line, line.Line)
                ? LumiereParagraphBoundary.Metadata
                : previous is not null && gap >= threshold ? LumiereParagraphBoundary.TimeGap : null;
            if (boundary is { } b && current.Lines.Count > 0)
            {
                drafts.AddRange(SplitOversized(current));
                current = new Draft([], b);
            }
            current.Lines.Add(line);
        }
        if (current.Lines.Count > 0) drafts.AddRange(SplitOversized(current));
        return drafts;
    }

    private static LumiereParagraphKind Classify(List<StructureLine> lines, int index, int total)
    {
        if (lines.Any(item => item.Line.IsChorus || ChorusPart().IsMatch(item.Line.SongPart ?? ""))) return LumiereParagraphKind.Chorus;
        if (lines.Any(item => BreakPart().IsMatch(item.Line.SongPart ?? ""))) return LumiereParagraphKind.Break;
        if (index == total - 1) return LumiereParagraphKind.Outro;
        var duration = lines[^1].RenderEndTime - lines[0].Line.StartTime;
        var segments = lines.Sum(line => LumiereWords.Segment(line.Line.Text).Count(word => !word.Blank));
        var punctuation = lines.Sum(line => Exclamation().Count(line.Line.Text));
        if (duration <= 3.5 || segments <= 3) return LumiereParagraphKind.Breath;
        if (punctuation >= 2 || segments / Math.Max(duration, 1) > 2.5) return LumiereParagraphKind.Lift;
        return LumiereParagraphKind.Verse;
    }

    // ---- shots (program.ts) ----------------------------------------------------------------------------------

    /// <summary>Short lines pair up into one shot (within the duration cap); long lines stand alone.</summary>
    private static List<List<StructureLine>> GroupLines(List<StructureLine> lines)
    {
        var groups = new List<List<StructureLine>>();
        foreach (var line in lines)
        {
            var duration = line.RenderEndTime - line.Line.StartTime;
            var last = groups.Count > 0 ? groups[^1] : null;
            var lastStart = last?[0].Line.StartTime ?? 0;
            var lastLong = last is null || last.Count == 1 && last[0].RenderEndTime - lastStart >= LongLine;
            if (last is not null && !lastLong && duration < LongLine && last.Count < MaxLinesPerShot
                && line.RenderEndTime - lastStart <= MaxShotDuration)
                last.Add(line);
            else
                groups.Add([line]);
        }
        return groups;
    }

    /// <summary>
    /// Shot times: each from its first line (the paragraph's first from the paragraph start) to the next shot; a gap after a
    /// line longer than BridgeGap ends the shot 0.6 s after the line and hands the gap to an interlude shot.
    /// </summary>
    private static List<PlannedShot> PlanShots(List<StructureLine> lines, double paragraphStart, double paragraphEnd)
    {
        var groups = GroupLines(lines);
        var shots = new List<PlannedShot>();
        for (var index = 0; index < groups.Count; index++)
        {
            var group = groups[index];
            var start = index == 0 ? Math.Min(paragraphStart, group[0].Line.StartTime) : group[0].Line.StartTime;
            var lyricEnd = group.Max(line => line.RenderEndTime);
            var nextStart = index + 1 < groups.Count ? groups[index + 1][0].Line.StartTime : paragraphEnd;
            if (nextStart - lyricEnd >= BridgeGap)
            {
                var end = lyricEnd + 0.6;
                shots.Add(new PlannedShot(group, start, end, lyricEnd, false));
                shots.Add(new PlannedShot([], end, nextStart, nextStart, true));
            }
            else
                shots.Add(new PlannedShot(group, start, Math.Max(nextStart, lyricEnd), Math.Min(lyricEnd, nextStart), false));
        }
        if (groups.Count == 0 && paragraphEnd > paragraphStart)
            shots.Add(new PlannedShot([], paragraphStart, paragraphEnd, paragraphEnd, true));
        return shots;
    }

    /// <summary>Interludes longer than BridgeSplit split into ~BridgeShot pieces (one profile hanging too long reads as stuck).</summary>
    private static List<PlannedShot> SplitLongBridges(List<PlannedShot> shots) => [.. shots.SelectMany(shot =>
    {
        var length = shot.EndTime - shot.StartTime;
        if (!shot.IsBridge || length <= BridgeSplit) return [shot];
        var count = Math.Max(2, (int)Math.Round(length / BridgeShot, MidpointRounding.AwayFromZero));
        return Enumerable.Range(0, count).Select(index =>
        {
            var start = shot.StartTime + length * index / count;
            var end = index == count - 1 ? shot.EndTime : shot.StartTime + length * (index + 1) / count;
            return new PlannedShot([], start, end, end, true);
        });
    })];

    private static LumiereMood[]? MoodsOf(LumiereParagraphKind kind) => kind switch
    {
        LumiereParagraphKind.Chorus or LumiereParagraphKind.Lift => [LumiereMood.Loud, LumiereMood.Neutral],
        LumiereParagraphKind.Verse or LumiereParagraphKind.Breath => [LumiereMood.Quiet, LumiereMood.Neutral],
        LumiereParagraphKind.Outro => [LumiereMood.Quiet],
        _ => null,
    };

    /// <summary>All profiles → bridge families → mood → not recent → not the previous family; each step falls back if empty.</summary>
    private static string CastShot(string seed, int paragraphIndex, int shotIndex, LumiereParagraphKind kind, bool isBridge,
        IReadOnlyList<string> recent, string? family)
    {
        IReadOnlyList<string> candidates = LumiereCatalog.Kinds;
        void Narrow(Func<LumiereProfile, bool> keep, int atLeast = 1)
        {
            var next = candidates.Where(k => keep(LumiereCatalog.ProfileOf(k))).ToList();
            if (next.Count >= atLeast) candidates = next;
        }
        var moods = MoodsOf(kind);
        if (isBridge) Narrow(profile => BridgeFamilies.Contains(profile.Family) && profile.Mood != LumiereMood.Loud);
        if (moods is not null) Narrow(profile => moods.Contains(profile.Mood));
        Narrow(profile => !recent.Contains(profile.Kind));
        if (family is not null) Narrow(profile => profile.Family != family, 3);
        var random = new LumiereRng($"{seed}:{paragraphIndex}:{shotIndex}:cast");
        return candidates[(int)Math.Floor(random.Next() * candidates.Count)];
    }
}

/// <summary>A layer's transition frame: alpha, scale about the frame center, blur strength (logical px).</summary>
public readonly record struct LumiereLayerFrame(int Index, double Alpha, double Scale, double Blur);

/// <summary>
/// Folia lumiereTransitions.ts + lumiereSceneFrames.ts. Lights-out darkens inside the scene (its fadeOut); flare-cut zooms a
/// little and blurs with the peak at the boundary; focus-pull blurs out then in. After a boundary the new paragraph enters
/// while the previous one, held at its exit peak, cross-fades away — both sides are continuous.
/// </summary>
public static class LumiereTransitions
{
    private static readonly LumiereTransitionKind[] Kinds = [LumiereTransitionKind.LightsOut, LumiereTransitionKind.FlareCut, LumiereTransitionKind.FocusPull];

    public static double Duration(LumiereTransitionKind kind, double gap) => kind == LumiereTransitionKind.FocusPull
        ? Math.Min(0.6, Math.Max(0.35, gap > 0 ? gap * 0.5 : 0.45))
        : Math.Min(1.1, Math.Max(0.5, gap > 0 ? gap * 0.6 : 0.8));

    public static double EnterDuration(LumiereTransitionOut transition)
    {
        var (min, max) = transition.Kind == LumiereTransitionKind.FocusPull ? (0.3, 0.6) : (0.4, 0.9);
        return Math.Max(min, Math.Min(max, transition.EndTime - transition.StartTime));
    }

    /// <summary>Never the same as the previous choice; hashed from (seed, paragraph index) like Folia's 31× string hash.</summary>
    public static LumiereTransitionKind Choose(string seed, int paragraphIndex, LumiereTransitionKind? previous)
    {
        var choices = Kinds.Where(kind => kind != previous).ToArray();
        var hash = 0;
        foreach (var c in $"{seed}:{paragraphIndex}:transition") hash = unchecked(hash * 31 + c);
        return choices[(int)(Math.Abs((long)hash) % choices.Length)];
    }

    private static double Smooth(double value)
    {
        var t = Math.Clamp(value, 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>exit: 0 → 1 approaching the boundary; enter: 0 → 1 leaving it.</summary>
    public static (double Alpha, double Scale, double Blur) Frame(LumiereTransitionKind kind, bool exit, double progress)
    {
        var near = exit ? Smooth(progress) : 1 - Smooth(progress);
        return kind switch
        {
            LumiereTransitionKind.LightsOut => (1 - near, 1, 0),
            LumiereTransitionKind.FlareCut => (1 - 0.35 * near * near, 1 + 0.03 * near, 7 * near * near),
            _ => (1, 1 + 0.015 * near, 12 * near),
        };
    }

    private static (double Alpha, double Scale, double Blur) ExitFrame(LumiereProgram program, int index, double time)
    {
        var transition = program.Paragraphs[index].TransitionOut;
        if (transition is null || time < transition.StartTime) return (1, 1, 0);
        var progress = (time - transition.StartTime) / Math.Max(transition.EndTime - transition.StartTime, 1e-3);
        var frame = Frame(transition.Kind, true, progress);
        // Lights-out darkens inside the scene; dimming the layer too would let a light background flash through.
        return transition.Kind == LumiereTransitionKind.LightsOut ? frame with { Alpha = 1 } : frame;
    }

    /// <summary>Which paragraph layers draw at <paramref name="time"/>, bottom to top. No transitions in static mode.</summary>
    public static (int ActiveIndex, List<LumiereLayerFrame> Layers) Resolve(LumiereProgram program, double time, bool transitions = true)
    {
        var layers = new List<LumiereLayerFrame>();
        if (program.Paragraphs.Count == 0) return (-1, layers);
        var active = program.ParagraphIndexAt(time);
        if (!transitions)
        {
            layers.Add(new LumiereLayerFrame(active, 1, 1, 0));
            return (active, layers);
        }
        var paragraph = program.Paragraphs[active];
        var current = ExitFrame(program, active, time);
        var previousOut = active > 0 ? program.Paragraphs[active - 1].TransitionOut : null;
        if (previousOut is not null)
        {
            var progress = (time - paragraph.StartTime) / EnterDuration(previousOut);
            if (progress >= 0 && progress < 1)
            {
                var enter = Frame(previousOut.Kind, false, progress);
                var weight = Smooth(progress);
                // The previous paragraph holds its exit peak and fades with the entry.
                var peak = ExitFrame(program, active - 1, previousOut.EndTime);
                if (peak.Alpha * (1 - weight) > 0.002)
                    layers.Add(new LumiereLayerFrame(active - 1, peak.Alpha * (1 - weight), peak.Scale, peak.Blur));
                layers.Add(new LumiereLayerFrame(active, Math.Min(enter.Alpha, weight) * current.Alpha, enter.Scale * current.Scale,
                    Math.Max(enter.Blur, current.Blur)));
                return (active, layers);
            }
        }
        layers.Add(new LumiereLayerFrame(active, current.Alpha, current.Scale, current.Blur));
        return (active, layers);
    }
}
