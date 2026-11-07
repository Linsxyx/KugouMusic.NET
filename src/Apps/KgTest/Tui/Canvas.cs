using System.Text;
using KgTest.Graphics;

namespace KgTest.Tui;

// 一帧画面的绘制目标：字符格缓冲区 + 本帧的图片放置记录。所有视图都只往这里画。

internal sealed class ImagePlacement
{
    public required int Id { get; init; }
    public required TerminalImage Image { get; init; }
    public required Rect Area { get; init; }
}

internal sealed class Canvas
{
    private readonly Dictionary<string, int> _placementIds = [];
    private int _nextPlacementId = 1;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public Cell[] Cells { get; private set; } = [];
    public List<ImagePlacement> Placements { get; } = [];
    public TerminalImageMode ImageMode { get; set; } = TerminalImageMode.Blocks;
    public Rect Bounds => new(0, 0, Width, Height);

    public void Reset(int width, int height)
    {
        if (width != Width || height != Height)
        {
            Width = width;
            Height = height;
            Cells = new Cell[width * height];
        }

        Array.Fill(Cells, Cell.Blank(CellStyle.Default));
        Placements.Clear();
        if (_placementIds.Count > 256)
        {
            _placementIds.Clear();
        }
    }

    public ref Cell At(int x, int y) => ref Cells[y * Width + x];

    public void Set(int x, int y, int rune, CellStyle style, int width = 1)
    {
        if (y < 0 || y >= Height || x < 0 || x >= Width)
        {
            return;
        }

        if (width == 2 && x + 1 >= Width)
        {
            rune = ' ';
            width = 1;
        }

        ref var cell = ref At(x, y);
        // 覆盖宽字符的一半时，把另一半清成空格，避免残影。
        if (cell.Width == 0 && x > 0)
        {
            ref var lead = ref At(x - 1, y);
            lead = Cell.Blank(lead.Style);
        }
        else if (cell.Width == 2 && x + 1 < Width && width == 1)
        {
            ref var tail = ref At(x + 1, y);
            tail = Cell.Blank(cell.Style);
        }

        cell = new Cell { Rune = rune, Width = (byte)width, Style = style };
        if (width == 2)
        {
            ref var tail = ref At(x + 1, y);
            if (tail.Width == 2 && x + 2 < Width)
            {
                ref var next = ref At(x + 2, y);
                next = Cell.Blank(next.Style);
            }

            tail = new Cell { Rune = ' ', Width = 0, Style = style };
        }
    }

    /// <summary>
    ///     在 (x, y) 写入一行文本，按显示宽度裁剪到 maxWidth，超出部分以省略号结尾。返回实际占用的列数。
    /// </summary>
    public int Text(int x, int y, string? text, CellStyle style, int maxWidth = int.MaxValue)
    {
        if (string.IsNullOrEmpty(text) || y < 0 || y >= Height)
        {
            return 0;
        }

        var limit = Math.Min(maxWidth, Width - x);
        if (limit <= 0)
        {
            return 0;
        }

        var value = TextWidth.Of(text) > limit ? TextWidth.Truncate(text, limit) : text;
        var column = x;
        foreach (var rune in value.EnumerateRunes())
        {
            var cp = rune.Value is '\n' or '\r' or '\t' ? ' ' : rune.Value;
            var w = TextWidth.Of(cp);
            if (w == 0)
            {
                continue;
            }

            if (column + w > x + limit)
            {
                break;
            }

            if (column >= 0)
            {
                Set(column, y, cp, style, w);
            }

            column += w;
        }

        return column - x;
    }

    /// <summary>
    ///     在区域内按对齐方式写一行文本（0 左，1 居中，2 右）。
    /// </summary>
    public int TextAligned(Rect area, int y, string? text, CellStyle style, int align)
    {
        var value = TextWidth.Truncate(text, area.W);
        var width = TextWidth.Of(value);
        var x = align switch
        {
            1 => area.X + (area.W - width) / 2,
            2 => area.Right - width,
            _ => area.X
        };
        return Text(x, y, value, style, area.W);
    }

    /// <summary>
    ///     依次写入多段不同样式的文本，返回结束列。
    /// </summary>
    public int Spans(int x, int y, int maxWidth, params (string Text, CellStyle Style)[] spans)
    {
        var column = x;
        var end = x + maxWidth;
        foreach (var (text, style) in spans)
        {
            if (column >= end)
            {
                break;
            }

            column += Text(column, y, text, style, end - column);
        }

        return column;
    }

    public void Fill(Rect area, CellStyle style, int rune = ' ')
    {
        for (var y = Math.Max(0, area.Y); y < Math.Min(Height, area.Bottom); y++)
        {
            for (var x = Math.Max(0, area.X); x < Math.Min(Width, area.Right); x++)
            {
                Set(x, y, rune, style);
            }
        }
    }

    public void HLine(int x, int y, int width, CellStyle style, int rune = '─')
    {
        for (var i = 0; i < width; i++)
        {
            Set(x + i, y, rune, style);
        }
    }

    public void VLine(int x, int y, int height, CellStyle style, int rune = '│')
    {
        for (var i = 0; i < height; i++)
        {
            Set(x, y + i, rune, style);
        }
    }

    public void Box(Rect area, CellStyle border, string? title = null, CellStyle? titleStyle = null)
    {
        if (area.W < 2 || area.H < 2)
        {
            return;
        }

        Set(area.X, area.Y, '╭', border);
        Set(area.Right - 1, area.Y, '╮', border);
        Set(area.X, area.Bottom - 1, '╰', border);
        Set(area.Right - 1, area.Bottom - 1, '╯', border);
        HLine(area.X + 1, area.Y, area.W - 2, border);
        HLine(area.X + 1, area.Bottom - 1, area.W - 2, border);
        VLine(area.X, area.Y + 1, area.H - 2, border);
        VLine(area.Right - 1, area.Y + 1, area.H - 2, border);

        if (!string.IsNullOrEmpty(title) && area.W > 6)
        {
            Text(area.X + 2, area.Y, $" {title} ", titleStyle ?? border, area.W - 4);
        }
    }

    /// <summary>
    ///     计算图片按比例放入区域后的字符格矩形（假设字符格高宽比约为 2:1）。
    /// </summary>
    public static Rect FitImage(Rect area, double aspect)
    {
        if (area.IsEmpty)
        {
            return area;
        }

        var safeAspect = aspect <= 0 ? 1 : aspect;
        var rows = area.H;
        var cols = (int)Math.Round(rows * safeAspect * 2);
        if (cols > area.W)
        {
            cols = area.W;
            rows = Math.Max(1, (int)Math.Round(cols / (safeAspect * 2)));
        }

        return area.Center(cols, rows);
    }

    /// <summary>
    ///     绘制图片：字符块模式直接写入半块字符，图片协议模式只占位并记录放置信息，由渲染器在刷新时输出。
    /// </summary>
    public Rect Image(string slot, Rect area, TerminalImage image)
    {
        var target = FitImage(area, image.Width / (double)Math.Max(1, image.Height));
        if (target.IsEmpty)
        {
            return target;
        }

        switch (ImageMode)
        {
            case TerminalImageMode.Off:
                return Rect.Empty;
            case TerminalImageMode.Blocks:
            case TerminalImageMode.Auto:
                DrawHalfBlocks(target, image);
                return target;
            default:
                Place(slot, target, image);
                return target;
        }
    }

    /// <summary>
    ///     绘制二维码：图片协议模式输出原图；字符模式按模块矩阵绘制，保证可以被手机扫描。
    /// </summary>
    public Rect Qr(Rect area, TerminalImage image)
    {
        if (ImageMode is TerminalImageMode.Kitty or TerminalImageMode.ITerm or TerminalImageMode.Sixel)
        {
            return Image("qr", area, image);
        }

        var modules = image.GetQrModules();
        if (modules == null)
        {
            var fallback = FitImage(area, image.Width / (double)Math.Max(1, image.Height));
            DrawHalfBlocks(fallback, image);
            return fallback;
        }

        const int quiet = 2;
        var size = modules.GetLength(0);
        var total = size + quiet * 2;
        var rows = (total + 1) / 2;
        if (total > area.W || rows > area.H)
        {
            return Rect.Empty;
        }

        var target = area.Center(total, rows);
        var black = new Rgb(0, 0, 0);
        var white = new Rgb(255, 255, 255);
        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < total; col++)
            {
                var top = IsDark(modules, col - quiet, row * 2 - quiet);
                var bottom = IsDark(modules, col - quiet, row * 2 + 1 - quiet);
                Set(target.X + col, target.Y + row, '▀',
                    new CellStyle(top ? black : white, bottom ? black : white));
            }
        }

        return target;
    }

    /// <summary>
    ///     覆盖层绘制前调用：移除与该区域相交的图片放置，避免图片盖住弹窗。
    /// </summary>
    public void ClearImages(Rect area)
    {
        for (var i = Placements.Count - 1; i >= 0; i--)
        {
            var placement = Placements[i];
            if (!placement.Area.Intersects(area))
            {
                continue;
            }

            Placements.RemoveAt(i);
            var a = placement.Area;
            for (var y = a.Y; y < a.Bottom; y++)
            {
                for (var x = a.X; x < a.Right; x++)
                {
                    if (x >= 0 && y >= 0 && x < Width && y < Height)
                    {
                        At(x, y).ImageId = 0;
                    }
                }
            }
        }
    }

    public string Snapshot()
    {
        var builder = new StringBuilder();
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var cell = At(x, y);
                if (cell.Width == 0)
                {
                    continue;
                }

                builder.Append(cell.ImageId != 0 ? "▒" : char.ConvertFromUtf32(cell.Rune));
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static bool IsDark(bool[,] modules, int x, int y)
    {
        var size = modules.GetLength(0);
        return x >= 0 && y >= 0 && x < size && y < size && modules[x, y];
    }

    private void DrawHalfBlocks(Rect target, TerminalImage image)
    {
        var pixels = image.GetPixels(target.W, target.H * 2);
        for (var row = 0; row < target.H; row++)
        {
            for (var col = 0; col < target.W; col++)
            {
                var top = pixels[row * 2 * target.W + col];
                var bottom = pixels[(row * 2 + 1) * target.W + col];
                Set(target.X + col, target.Y + row, '▀', new CellStyle(top, bottom));
            }
        }
    }

    private void Place(string slot, Rect target, TerminalImage image)
    {
        var key = $"{slot}|{image.Key}|{target}";
        if (!_placementIds.TryGetValue(key, out var id))
        {
            id = _nextPlacementId++;
            _placementIds[key] = id;
        }

        for (var y = target.Y; y < target.Bottom; y++)
        {
            for (var x = target.X; x < target.Right; x++)
            {
                Set(x, y, ' ', CellStyle.Default);
                At(x, y).ImageId = id;
            }
        }

        Placements.Add(new ImagePlacement { Id = id, Image = image, Area = target });
    }
}
