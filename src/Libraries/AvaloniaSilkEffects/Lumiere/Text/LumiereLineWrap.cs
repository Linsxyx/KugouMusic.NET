using System.Globalization;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace AvaloniaSilkEffects.Lumiere.Text;

public readonly record struct LumierePoint(double X, double Y);

/// <summary>Where text may go inside the overlay's corner brackets (height units, x from left, y from top).</summary>
public readonly record struct LumiereFrameBand(double Left, double Right, double Top, double Bottom);

/// <summary>One orientation × wrap variant of a line: glyph centers relative to the block center, the spot path, sizes.</summary>
public sealed record LumiereLineVariant(
    LumierePoint[] Points,
    LumierePoint[] Spots,
    double Along,
    double Across,
    int Lines,
    double InkWidth,
    double InkHeight);

/// <summary>A line's glyph as the flow sees it.</summary>
public readonly record struct LumiereFlowGlyph(string Char, double Scale, double Advance, bool Upright, double Jag);

/// <summary>
/// Measures text like pretext does for Folia: natural width with letter spacing after every grapheme (last included),
/// matching the per-glyph canvas widths. Cached per (size, text).
/// </summary>
internal sealed class LumiereTextMeasurer : IDisposable
{
    private readonly SKTypeface _typeface;
    private readonly double _letterSpacing;
    private readonly Dictionary<(double, string), double> _cache = [];
    private readonly Dictionary<double, SKFont> _fonts = [];

    public LumiereTextMeasurer(string family, int weight, double letterSpacing, string sample)
    {
        _typeface = EffectTextureCache.ResolveTypeface(family,
            new SKFontStyle(Math.Clamp(weight, 100, 900), (int)SKFontStyleWidth.Normal, SKFontStyleSlant.Upright), sample);
        _letterSpacing = letterSpacing;
    }

    public double Spacing(double px) => _letterSpacing * px;

    public double Natural(string text, double px)
    {
        if (_cache.TryGetValue((px, text), out var width)) return width;
        if (!_fonts.TryGetValue(px, out var font)) _fonts[px] = font = new SKFont(_typeface, (float)px) { Subpixel = true };
        var count = new StringInfo(text).LengthInTextElements;
        width = font.MeasureText(text) + Spacing(px) * count;
        _cache[(px, text)] = width;
        return width;
    }

    public void Dispose()
    {
        foreach (var font in _fonts.Values) font.Dispose();
        _typeface.Dispose();
    }
}

/// <summary>
/// Folia text/lineWrap.ts: a line that is too long first gets more room (the full frame width horizontally, the full
/// frame height vertically), may shrink slightly (to <see cref="SingleMinFit"/>), and only then wraps to two rows /
/// two columns (vertical reads right to left); after that it shrinks further. Words are atomic boxes with their own
/// size; Folia breaks them with pretext's rich-inline, emulated here by the same greedy fill (spaces collapse to one
/// word gap, no gap at a row start, breaks allowed between any two boxes).
/// </summary>
public static partial class LumiereLineWrap
{
    public const double SingleMinFit = 0.8;
    public const int MaxLines = 2;
    public const double RowPitch = 1.6;
    public const double ColumnPitch = 1.6;
    private const double FrameClearanceX = 0.04;
    private const double FrameClearanceTop = 0.035;
    /// <summary>Room at the bottom for the shared subtitles (translation / next line), height units.</summary>
    private const double SubtitleClearance = 0.2;

    [GeneratedRegex(@"^[(\[{（【《「『〈〔［“‘]+$")]
    private static partial Regex Opening();

    [GeneratedRegex(@"[\p{L}\p{N}]")]
    private static partial Regex LetterOrNumber();

    /// <summary>Corner bracket insets (logical px); the overlay uses them too.</summary>
    public static (double PadX, double PadY) FrameInsets(double width, double height) =>
        (Math.Max(30, width * 0.065), Math.Max(30, height * 0.085));

    public static LumiereFrameBand FrameBand(double width, double height)
    {
        var (padX, padY) = FrameInsets(width, height);
        var left = padX / height + FrameClearanceX;
        return new LumiereFrameBand(left, width / height - left, padY / height + FrameClearanceTop,
            Math.Max(padY / height + FrameClearanceTop + 0.2, 1 - SubtitleClearance));
    }

    private enum SpanKind { Word, Punct, Space }

    private readonly record struct Span(int Start, int End, SpanKind Kind);

    private sealed class Unit
    {
        public int Start;
        public int End;
        public required string Text;
        public double Px;
    }

    private readonly record struct Piece(Unit? Unit, Span Space)
    {
        public bool IsSpace => Unit is null;
    }

    private static bool IsSpace(IReadOnlyList<LumiereFlowGlyph> glyphs, int index) => string.IsNullOrWhiteSpace(glyphs[index].Char);

    private static string TextOf(IReadOnlyList<LumiereFlowGlyph> glyphs, int start, int end) =>
        string.Concat(Enumerable.Range(start, end - start).Select(index => glyphs[index].Char));

    /// <summary>Spans: word edges' whitespace stands alone (rich-inline gathers it into gaps); uncovered glyphs one each.</summary>
    private static List<Span> SpansOf(IReadOnlyList<LumiereFlowGlyph> glyphs, IReadOnlyList<LumiereWordSpan> words)
    {
        var ranges = new List<(int Start, int End, bool Word)>();
        var cursor = 0;
        foreach (var word in words)
        {
            var start = Math.Max(cursor, Math.Min(glyphs.Count, word.Start));
            var end = Math.Min(glyphs.Count, word.End);
            for (var index = cursor; index < start; index++) ranges.Add((index, index + 1, false));
            if (end > start) ranges.Add((start, end, !word.Blank));
            cursor = Math.Max(cursor, end);
        }
        for (var index = cursor; index < glyphs.Count; index++) ranges.Add((index, index + 1, false));
        var spans = new List<Span>();
        foreach (var range in ranges)
        {
            var (start, end, isWord) = range;
            var lead = new List<Span>();
            var tail = new List<Span>();
            while (start < end && IsSpace(glyphs, start)) { lead.Add(new Span(start, start + 1, SpanKind.Space)); start++; }
            while (end > start && IsSpace(glyphs, end - 1)) { tail.Insert(0, new Span(end - 1, end, SpanKind.Space)); end--; }
            spans.AddRange(lead);
            if (end > start) spans.Add(new Span(start, end, isWord ? SpanKind.Word : SpanKind.Punct));
            spans.AddRange(tail);
        }
        return spans;
    }

    /// <summary>Break units: words with the punctuation touching them (opening marks stick to the next word).</summary>
    private static List<Piece> UnitsOf(IReadOnlyList<LumiereFlowGlyph> glyphs, List<Span> spans, double heroPx)
    {
        var pieces = new List<Piece>();
        double PxOf(int index) => heroPx * glyphs[index].Scale;
        Span? pendingOpen = null;
        for (var index = 0; index < spans.Count; index++)
        {
            var span = spans[index];
            if (span.Kind == SpanKind.Space)
            {
                if (pendingOpen is { } open)
                    pieces.Add(new Piece(new Unit { Start = open.Start, End = open.End, Text = TextOf(glyphs, open.Start, open.End), Px = PxOf(open.Start) }, default));
                pendingOpen = null;
                pieces.Add(new Piece(null, span));
                continue;
            }
            var previous = pieces.Count > 0 ? pieces[^1] : (Piece?)null;
            Span? next = index + 1 < spans.Count ? spans[index + 1] : null;
            if (span.Kind == SpanKind.Punct)
            {
                var text = TextOf(glyphs, span.Start, span.End);
                if (Opening().IsMatch(text) && next is { } n && n.Kind != SpanKind.Space)
                {
                    pendingOpen = pendingOpen is { } p ? p with { End = span.End } : span;
                    continue;
                }
                if (pendingOpen is null && previous is { IsSpace: false } prior && prior.Unit!.End == span.Start)
                {
                    prior.Unit.End = span.End;
                    continue;
                }
            }
            var start = pendingOpen?.Start ?? span.Start;
            pendingOpen = null;
            pieces.Add(new Piece(new Unit { Start = start, End = span.End, Text = TextOf(glyphs, span.Start, span.End), Px = PxOf(span.Start) }, default));
        }
        if (pendingOpen is { } last)
            pieces.Add(new Piece(new Unit { Start = last.Start, End = last.End, Text = TextOf(glyphs, last.Start, last.End), Px = PxOf(last.Start) }, default));
        return pieces;
    }

    private readonly record struct Fragment(int Item, double GapBefore, double Occupied);

    private sealed record Row(List<Fragment> Fragments, double Width);

    private static double Extent(Unit unit, IReadOnlyList<double> advances)
    {
        var sum = 0.0;
        for (var index = unit.Start; index < unit.End; index++) sum += advances[index];
        return sum;
    }

    /// <summary>Rows of the line: each glyph's row and center offset along it, and each row's length.</summary>
    private static (int[] Row, double[] Offset, double[] Lengths) BreakLines(LumiereTextMeasurer measurer,
        IReadOnlyList<LumiereFlowGlyph> glyphs, List<Piece> pieces, IReadOnlyList<double> advances, double heroPx, int lines)
    {
        var widths = new double[pieces.Count];
        var widest = 0.0;
        var widestGap = 0.0;
        for (var index = 0; index < pieces.Count; index++)
        {
            var piece = pieces[index];
            if (piece.IsSpace)
            {
                var px = heroPx * glyphs[piece.Space.Start].Scale;
                // A collapsed word gap: pretext measures a lone space as 0, so use "a a" minus "aa".
                widths[index] = measurer.Natural("a a", px) - measurer.Natural("aa", px);
                widestGap = Math.Max(widestGap, widths[index]);
                continue;
            }
            widths[index] = Extent(piece.Unit!, advances);
            widest = Math.Max(widest, widths[index]);
        }

        List<Row> Walk(double maxWidth)
        {
            var rows = new List<Row>();
            var fragments = new List<Fragment>();
            var cursor = 0.0;
            var pendingGap = -1.0;
            for (var index = 0; index < pieces.Count; index++)
            {
                if (pieces[index].IsSpace)
                {
                    if (pendingGap < 0) pendingGap = widths[index];
                    continue;
                }
                var gap = fragments.Count == 0 ? 0 : Math.Max(0, pendingGap);
                if (fragments.Count > 0 && cursor + gap + widths[index] > maxWidth + 1e-9)
                {
                    rows.Add(new Row(fragments, cursor));
                    fragments = [];
                    cursor = 0;
                    gap = 0;
                }
                fragments.Add(new Fragment(index, gap, widths[index]));
                cursor += gap + widths[index];
                pendingGap = -1;
            }
            if (fragments.Count > 0 || rows.Count == 0) rows.Add(new Row(fragments, cursor));
            return rows;
        }

        var rows = Walk(double.PositiveInfinity);
        if (lines > 1 && rows.Count == 1 && rows[0].Fragments.Count > 1)
        {
            // Two balanced rows: target = (total + widest word) / 2 + one gap; greedy filling cannot then make the first row
            // a word longer than the second, and the rest fits the second. Relax once if it still gives more rows.
            var total = rows[0].Width;
            var target = (total + widest) / 2 + widestGap;
            if (Walk(target).Count > lines) target = total / 2 + widest + widestGap;
            rows = Walk(target);
            if (rows.Count > lines)
            {
                var kept = rows.Take(lines - 1).ToList();
                var rest = rows.Skip(lines - 1).ToList();
                var merged = rest.SelectMany((row, r) => row.Fragments.Select((fragment, k) =>
                    r > 0 && k == 0 ? fragment with { GapBefore = widestGap } : fragment)).ToList();
                kept.Add(new Row(merged, merged.Sum(fragment => fragment.GapBefore + fragment.Occupied)));
                rows = kept;
            }
        }

        var row = Enumerable.Repeat(-1, glyphs.Count).ToArray();
        var offset = new double[glyphs.Count];
        var lengths = rows.Select(line => line.Width).ToArray();
        for (var r = 0; r < rows.Count; r++)
        {
            var cursor = 0.0;
            foreach (var fragment in rows[r].Fragments)
            {
                var unit = pieces[fragment.Item].Unit!;
                var gapStart = cursor;
                cursor += fragment.GapBefore;
                // Whitespace before the word sits in the middle of the gap (at the row start when it opens a row).
                for (var index = unit.Start - 1; index >= 0 && row[index] == -1 && IsSpace(glyphs, index); index--)
                {
                    row[index] = r;
                    offset[index] = gapStart + fragment.GapBefore / 2;
                }
                // The word's width spreads over its glyphs; each center sits in the middle of its share.
                var extent = Extent(unit, advances);
                var ratio = extent > 0 ? fragment.Occupied / extent : 0;
                var inner = cursor;
                for (var index = unit.Start; index < unit.End; index++)
                {
                    var advance = advances[index] * ratio;
                    row[index] = r;
                    offset[index] = inner + advance / 2;
                    inner += advance;
                }
                cursor += fragment.Occupied;
            }
        }
        // Whitespace left at row ends / starts follows the previous glyph (or the next one).
        for (var index = 0; index < glyphs.Count; index++)
        {
            if (row[index] != -1) continue;
            var previous = index - 1;
            if (previous >= 0 && row[previous] != -1)
            {
                row[index] = row[previous];
                offset[index] = offset[previous] + advances[previous] / 2;
            }
        }
        for (var index = glyphs.Count - 1; index >= 0; index--)
        {
            if (row[index] != -1) continue;
            row[index] = index + 1 < glyphs.Count && row[index + 1] != -1 ? row[index + 1] : 0;
            offset[index] = index + 1 < glyphs.Count ? Math.Max(0, offset[index + 1] - advances[index + 1] / 2) : 0;
        }
        return (row, offset, lengths.Length > 0 ? lengths : [0]);
    }

    /// <summary>Units longer than the limit split into single glyphs; punctuation still sticks to the glyph before.</summary>
    private static List<Piece> SplitLongUnits(IReadOnlyList<LumiereFlowGlyph> glyphs, List<Piece> pieces,
        IReadOnlyList<double> advances, double limit, double heroPx)
    {
        var output = new List<Piece>();
        foreach (var piece in pieces)
        {
            if (piece.IsSpace) { output.Add(piece); continue; }
            var unit = piece.Unit!;
            if (Extent(unit, advances) <= limit || unit.End - unit.Start < 2)
            {
                output.Add(new Piece(new Unit { Start = unit.Start, End = unit.End, Text = unit.Text, Px = unit.Px }, default));
                continue;
            }
            var first = output.Count;
            for (var index = unit.Start; index < unit.End; index++)
            {
                var c = glyphs[index].Char;
                if (output.Count > first && !LetterOrNumber().IsMatch(c))
                {
                    output[^1].Unit!.End = index + 1;
                    continue;
                }
                output.Add(new Piece(new Unit { Start = index, End = index + 1, Text = c, Px = heroPx * glyphs[index].Scale }, default));
            }
        }
        return output;
    }

    /// <summary>The four layouts of a line: [orientation 0 horizontal / 1 vertical][0 single / 1 wrapped].</summary>
    internal static LumiereLineVariant[][] Flow(LumiereTextMeasurer measurer, IReadOnlyList<LumiereFlowGlyph> glyphs,
        IReadOnlyList<LumiereWordSpan> words, double heroPx, double horizontalLimit, double verticalLimit)
    {
        var spans = SpansOf(glyphs, words);
        var basePieces = UnitsOf(glyphs, spans, heroPx);

        // Horizontal per-glyph widths: each span's measured width spread by canvas widths.
        var horizontal = new double[glyphs.Count];
        foreach (var span in spans)
        {
            var px = heroPx * glyphs[span.Start].Scale;
            var width = measurer.Natural(TextOf(glyphs, span.Start, span.End), px);
            var canvas = 0.0;
            for (var index = span.Start; index < span.End; index++) canvas += glyphs[index].Advance;
            for (var index = span.Start; index < span.End; index++)
                horizontal[index] = canvas > 0 ? width * glyphs[index].Advance / canvas : width / (span.End - span.Start);
        }
        // Vertical lengths: upright glyphs take one size (plus spacing); turned Latin takes its horizontal width.
        var vertical = glyphs.Select((glyph, index) =>
            glyph.Upright ? heroPx * glyph.Scale * (1 + measurer.Spacing(1)) : horizontal[index]).ToArray();

        LumiereLineVariant Variant(int orient, int lines)
        {
            var advances = orient == 0 ? horizontal : vertical;
            var pieces = SplitLongUnits(glyphs, basePieces, advances, orient == 0 ? horizontalLimit : verticalLimit, heroPx);
            var (row, offset, lengths) = BreakLines(measurer, glyphs, pieces, advances, heroPx, lines);
            var count = lengths.Length;
            var along = lengths.Max();
            var pitch = (orient == 0 ? RowPitch : ColumnPitch) * heroPx;
            var points = new LumierePoint[glyphs.Count];
            var spots = new LumierePoint[glyphs.Count];
            double inkX = 0, inkY = 0;
            for (var index = 0; index < glyphs.Count; index++)
            {
                var glyph = glyphs[index];
                var r = row[index];
                var size = heroPx * glyph.Scale;
                var ink = !string.IsNullOrWhiteSpace(glyph.Char);
                if (orient == 0)
                {
                    var x = offset[index] - lengths[r] / 2;
                    var rowY = (r - (count - 1) / 2.0) * pitch;
                    points[index] = new LumierePoint(x, rowY + (1 - glyph.Scale) * heroPx * 0.32 + glyph.Jag);
                    spots[index] = new LumierePoint(x, rowY);
                    if (ink)
                    {
                        inkX = Math.Max(inkX, Math.Abs(points[index].X) + advances[index] / 2);
                        inkY = Math.Max(inkY, Math.Abs(points[index].Y) + size / 2);
                    }
                }
                else
                {
                    var columnX = ((count - 1) / 2.0 - r) * pitch;
                    var y = offset[index] - lengths[r] / 2;
                    points[index] = new LumierePoint(columnX + glyph.Jag * 0.8, y);
                    spots[index] = new LumierePoint(columnX, y);
                    if (ink)
                    {
                        inkX = Math.Max(inkX, Math.Abs(points[index].X) + size / 2);
                        inkY = Math.Max(inkY, Math.Abs(points[index].Y) + advances[index] / 2);
                    }
                }
            }
            return new LumiereLineVariant(points, spots, along, (count - 1) * pitch + heroPx, count, inkX * 2, inkY * 2);
        }

        return [[Variant(0, 1), Variant(0, MaxLines)], [Variant(1, 1), Variant(1, MaxLines)]];
    }

    /// <summary>Extra shrink so a length <paramref name="along"/> at <paramref name="scale"/> fits the budget (never grows).</summary>
    public static double FitScale(double along, double budget, double scale = 1) => Math.Min(1, budget / Math.Max(scale * along, 1e-6));

    /// <summary>Wrap when a single row shrunk to <see cref="SingleMinFit"/> still does not fit, and it can wrap.</summary>
    public static bool ShouldWrap(LumiereLineVariant[] flow, double budget, double scale) =>
        flow[1].Lines > 1 && scale * flow[0].Along * SingleMinFit > budget;

    /// <summary>Puts a block of <paramref name="extent"/> centered at <paramref name="center"/> into [lo, hi].</summary>
    public static double ClampInto(double center, double extent, double lo, double hi) =>
        extent >= hi - lo ? (lo + hi) / 2 : Math.Min(hi - extent / 2, Math.Max(lo + extent / 2, center));
}
