using System.Numerics;

namespace AvaloniaSilkEffects.Sonnet;

/// <summary>
/// Folia's sonnetStaffView.ts: the La Folia theme on a five-line staff with a looping playback
/// cursor, shown in place of lyrics for instrumental tracks. Nodes are built once; each frame
/// only moves the cursor and re-weights the notes.
/// </summary>
public sealed class SonnetStaffView
{
    /// <summary>The text Folia uses for a virtual instrumental line.</summary>
    internal const string Marker = "♪";
    private const double CycleSeconds = 8;

    // La Folia's public-domain D-minor theme in 3/4: (staff step, beats, sharp).
    private static readonly (int Step, double Beats, bool Sharp)[] Notes =
    [
        (6, 1, false), (6, 1.5, false), (7, 0.5, false), (5, 1, true), (5, 1, true), (5, 1, true),
        (6, 1, false), (6, 1.5, false), (6, 0.5, false), (7, 1, false), (7, 1, false), (7, 1, false),
        (8, 1, false), (8, 1.5, false), (8, 0.5, false), (7, 1, false), (7, 1, false), (7, 1, false),
        (6, 1, false), (6, 1.5, false), (5, 0.5, true), (6, 3, false),
    ];

    private static readonly double TotalBeats = Notes.Sum(note => note.Beats);

    private readonly double _shotStart;
    private readonly float _playableWidth;
    private readonly float _beatWidth;
    private readonly ShapeNode _cursor;
    private readonly NoteView[] _notes;

    internal SonnetStaffView(SonnetTypographyPlacement placement, SonnetTheme theme, float baseFontSize,
        double shotStart, float width)
    {
        _shotStart = shotStart;
        Root = new EffectContainer { Position = new Vector2(placement.X, placement.Y), Rotation = placement.Rotation, Alpha = 0 };
        var staffWidth = Math.Max(300, width * 0.6f);
        var spacing = baseFontSize * 0.25f;
        var halfWidth = staffWidth / 2;
        var halfHeight = spacing * 2;
        _playableWidth = staffWidth * 0.92f;
        _beatWidth = (float)(_playableWidth / TotalBeats);
        var primary = Rgb(theme.Primary);

        var staff = new SonnetDrawList();
        for (var line = 0; line < 5; line++)
            staff.MoveTo(-halfWidth, -halfHeight + line * spacing).LineTo(halfWidth, -halfHeight + line * spacing);
        staff.Stroke(primary, 2, 0.3);
        for (var bar = 1; bar < 8; bar++)
        {
            var x = -_playableWidth / 2 + _beatWidth * bar * 3;
            staff.MoveTo(x, -halfHeight).LineTo(x, halfHeight);
        }
        staff.Stroke(primary, 1, 0.16);
        staff.MoveTo(-halfWidth + 10, -halfHeight).LineTo(-halfWidth + 10, halfHeight).Stroke(primary, 4, 0.5);
        staff.MoveTo(halfWidth - 10, -halfHeight).LineTo(halfWidth - 10, halfHeight).Stroke(primary, 2, 0.5);
        staff.MoveTo(halfWidth - 4, -halfHeight).LineTo(halfWidth - 4, halfHeight).Stroke(primary, 6, 0.5);
        Root.Add(staff.Replay());

        _cursor = new ShapeNode
        {
            Shape = EffectShapeKind.Line, Size = new Vector2(0, halfHeight * 2 + spacing * 1.6f), StrokeWidth = 1.5f,
            Color = theme.Accent with { A = 0.34f },
        };
        Root.Add(_cursor);

        _notes = new NoteView[Notes.Length];
        var startBeat = 0d;
        for (var index = 0; index < Notes.Length; index++)
        {
            var note = Notes[index];
            var x = (float)(-_playableWidth / 2 + _beatWidth * (startBeat + note.Beats * 0.5));
            var y = halfHeight - note.Step * spacing * 0.5f;
            var radiusX = spacing * 0.42f;
            var stemDown = note.Step >= 6;
            var stemX = x + (stemDown ? -radiusX : radiusX);
            var stemEnd = y + (stemDown ? spacing * 3.1f : -spacing * 3.1f);
            var head = new ShapeNode
            {
                Shape = EffectShapeKind.Ellipse, Size = new Vector2(radiusX * 2, spacing * 0.29f * 2),
                Pivot = new Vector2(radiusX, spacing * 0.29f), Position = new Vector2(x, y), Color = theme.Accent,
            };
            var marks = new SonnetDrawList().MoveTo(stemX, y).LineTo(stemX, stemEnd).Stroke(primary, 1.6, 1);
            if (note.Beats <= 0.5)
            {
                var flag = stemDown ? -1 : 1;
                marks.MoveTo(stemX, stemEnd)
                    .QuadraticCurveTo(stemX + spacing * 1.1, stemEnd + spacing * 0.55 * flag, stemX + spacing * 0.1, stemEnd + spacing * flag)
                    .Stroke(primary, 1.6, 1);
            }
            if (note.Sharp)
            {
                var sharpX = x - radiusX * 2.3f;
                var sharpHeight = spacing * 1.15f;
                marks.MoveTo(sharpX - spacing * 0.16, y - sharpHeight * 0.5).LineTo(sharpX - spacing * 0.16, y + sharpHeight * 0.5)
                    .MoveTo(sharpX + spacing * 0.16, y - sharpHeight * 0.5).LineTo(sharpX + spacing * 0.16, y + sharpHeight * 0.5)
                    .MoveTo(sharpX - spacing * 0.34, y - spacing * 0.12).LineTo(sharpX + spacing * 0.34, y - spacing * 0.28)
                    .MoveTo(sharpX - spacing * 0.34, y + spacing * 0.28).LineTo(sharpX + spacing * 0.34, y + spacing * 0.12)
                    .Stroke(primary, 1.2, 1);
            }
            var markNode = marks.Replay();
            Root.Add(head).Add(markNode);
            _notes[index] = new NoteView(head, markNode, startBeat, note.Beats, note.Sharp);
            startBeat += note.Beats;
        }
        Update(shotStart);
    }

    internal EffectContainer Root { get; }

    internal void Update(double time)
    {
        var cycle = ((time - _shotStart) % CycleSeconds + CycleSeconds) % CycleSeconds;
        var beat = cycle / CycleSeconds * TotalBeats;
        _cursor.Position = new Vector2((float)(-_playableWidth / 2 + _beatWidth * beat), -_cursor.Size.Y / 2);
        for (var index = 0; index < _notes.Length; index++)
        {
            var note = _notes[index];
            var active = beat >= note.StartBeat && beat < note.StartBeat + note.Beats;
            var pulse = active ? (Math.Sin(cycle * Math.PI * 5 + index * 0.4) + 1) * 0.5 : 0;
            var alpha = active ? 0.78 + pulse * 0.16 : 0.28 + index % 3 * 0.03;
            note.Head.Scale = new Vector2((float)(active ? 1 + pulse * 0.12 : 1));
            note.Head.Alpha = (float)alpha;
            // Stems, flags and sharps share one ceiling in Folia; the sharp's is slightly lower.
            note.Marks.Alpha = (float)Math.Min(note.Sharp ? 0.86 : 0.9, alpha + 0.08);
        }
    }

    /// <summary>Folia's virtual instrumental program: a staff line every eight seconds.</summary>
    public static IReadOnlyList<SonnetLine> VirtualLines(int count = 60) =>
        Enumerable.Range(0, count).Select(index => new SonnetLine(Marker, index * 8, index * 8 + 6, [])).ToArray();

    private static uint Rgb(EffectColor color) =>
        (uint)(Math.Clamp((int)MathF.Round(color.R * 255), 0, 255) << 16
            | Math.Clamp((int)MathF.Round(color.G * 255), 0, 255) << 8
            | Math.Clamp((int)MathF.Round(color.B * 255), 0, 255));

    private sealed record NoteView(ShapeNode Head, EffectContainer Marks, double StartBeat, double Beats, bool Sharp);
}
