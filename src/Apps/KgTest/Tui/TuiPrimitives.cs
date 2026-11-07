namespace KgTest.Tui;

// 终端 UI 的基础类型：颜色、样式、单元格与矩形区域。

internal readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb FromHex(uint hex) => new((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex);

    public Rgb Mix(Rgb other, float amount)
    {
        var t = Math.Clamp(amount, 0f, 1f);
        return new Rgb(
            (byte)(R + (other.R - R) * t),
            (byte)(G + (other.G - G) * t),
            (byte)(B + (other.B - B) * t));
    }

    public float Luminance => (0.2126f * R + 0.7152f * G + 0.0722f * B) / 255f;
}

[Flags]
internal enum TextAttr : byte
{
    None = 0,
    Bold = 1,
    Dim = 2,
    Italic = 4,
    Underline = 8
}

internal readonly record struct CellStyle(Rgb? Fg = null, Rgb? Bg = null, TextAttr Attr = TextAttr.None)
{
    public static readonly CellStyle Default = new();

    public CellStyle WithFg(Rgb? fg) => this with { Fg = fg };

    public CellStyle WithBg(Rgb? bg) => this with { Bg = bg };

    public CellStyle With(TextAttr attr) => this with { Attr = Attr | attr };
}

/// <summary>
///     屏幕上的单个字符格。Width 为 0 表示宽字符的右半部分；ImageId 非 0 表示该格被图片占用。
/// </summary>
internal struct Cell : IEquatable<Cell>
{
    public int Rune;
    public byte Width;
    public CellStyle Style;
    public int ImageId;

    public static Cell Blank(CellStyle style) => new() { Rune = ' ', Width = 1, Style = style };

    public readonly bool Equals(Cell other) =>
        Rune == other.Rune && Width == other.Width && ImageId == other.ImageId && Style.Equals(other.Style);

    public override readonly bool Equals(object? obj) => obj is Cell other && Equals(other);

    public override readonly int GetHashCode() => HashCode.Combine(Rune, Width, Style, ImageId);
}

internal readonly record struct Rect(int X, int Y, int W, int H)
{
    public static readonly Rect Empty = new(0, 0, 0, 0);

    public int Right => X + W;
    public int Bottom => Y + H;
    public bool IsEmpty => W <= 0 || H <= 0;

    public Rect Inset(int dx, int dy) => new(X + dx, Y + dy, Math.Max(0, W - dx * 2), Math.Max(0, H - dy * 2));

    public Rect Inset(int left, int top, int right, int bottom) =>
        new(X + left, Y + top, Math.Max(0, W - left - right), Math.Max(0, H - top - bottom));

    public Rect TakeTop(int h) => new(X, Y, W, Math.Clamp(h, 0, H));
    public Rect TakeBottom(int h) => new(X, Bottom - Math.Clamp(h, 0, H), W, Math.Clamp(h, 0, H));
    public Rect TakeLeft(int w) => new(X, Y, Math.Clamp(w, 0, W), H);
    public Rect TakeRight(int w) => new(Right - Math.Clamp(w, 0, W), Y, Math.Clamp(w, 0, W), H);
    public Rect SkipTop(int h) => new(X, Y + Math.Clamp(h, 0, H), W, H - Math.Clamp(h, 0, H));
    public Rect SkipBottom(int h) => new(X, Y, W, H - Math.Clamp(h, 0, H));
    public Rect SkipLeft(int w) => new(X + Math.Clamp(w, 0, W), Y, W - Math.Clamp(w, 0, W), H);
    public Rect SkipRight(int w) => new(X, Y, W - Math.Clamp(w, 0, W), H);

    public Rect Center(int w, int h)
    {
        var cw = Math.Min(w, W);
        var ch = Math.Min(h, H);
        return new Rect(X + (W - cw) / 2, Y + (H - ch) / 2, cw, ch);
    }

    public bool Intersects(Rect other) =>
        !IsEmpty && !other.IsEmpty && X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;

    public bool Contains(int x, int y) =>
        !IsEmpty && x >= X && x < Right && y >= Y && y < Bottom;
}
