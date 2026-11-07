using System.Runtime.InteropServices;
using System.Text;
using KaMusic.Cli.Graphics;

namespace KaMusic.Cli.Tui;

// 差量渲染器：只把与上一帧不同的字符格写到终端，并负责图片协议的输出与清理，避免整屏闪烁。

internal sealed class TerminalRenderer : IDisposable
{
    private const int StdOutputHandle = -11;
    private const int StdInputHandle = -10;
    private const uint EnableProcessedOutput = 0x0001;
    private const uint EnableVirtualTerminalProcessing = 0x0004;
    private const uint DisableNewlineAutoReturn = 0x0008;
    private const uint EnableMouseInput = 0x0010;
    private const uint EnableQuickEditMode = 0x0040;
    private const uint EnableExtendedFlags = 0x0080;
    private const uint EnableVirtualTerminalInput = 0x0200;
    private const uint CpUtf8 = 65001;

    private static readonly int[] CubeLevels = [0, 95, 135, 175, 215, 255];

    private readonly Stream _output = Console.OpenStandardOutput();
    private readonly StringBuilder _buffer = new(64 * 1024);
    private readonly Dictionary<int, ImagePlacement> _shown = [];
    private Cell[] _previous = [];
    private int _previousWidth;
    private int _previousHeight;
    private bool _forceFull = true;
    private bool _kittyUsed;
    private bool _entered;
    private uint _savedOutMode;
    private uint _savedInMode;
    private bool _hasSavedConsoleMode;

    public bool TrueColor { get; set; } = true;

    public (int Width, int Height) CellPixelSize { get; set; } = (10, 20);

    public void Enter()
    {
        if (_entered)
        {
            return;
        }

        _entered = true;
        EnableWindowsVirtualTerminal();
        try
        {
            Console.CursorVisible = false;
        }
        catch
        {
        }

        // 备用屏幕、隐藏光标、关闭自动换行；Unix 终端开启 SGR 1006 鼠标事件报告。
        var enterSeq = OperatingSystem.IsWindows()
            ? "\u001b[?1049h\u001b[?25l\u001b[?7l\u001b[2J\u001b[H"
            : "\u001b[?1049h\u001b[?25l\u001b[?7l\u001b[?1000h\u001b[?1006h\u001b[2J\u001b[H";
        WriteRaw(enterSeq);
    }

    public void Exit()
    {
        if (!_entered)
        {
            return;
        }

        _entered = false;
        var builder = new StringBuilder();
        if (_kittyUsed)
        {
            ImageProtocolEncoder.AppendKittyDeleteAll(builder);
        }

        if (!OperatingSystem.IsWindows())
        {
            builder.Append("\u001b[?1006l\u001b[?1000l");
        }

        builder.Append("\u001b[0m\u001b[2J\u001b[?7h\u001b[?25h\u001b[?1049l");
        WriteRaw(builder.ToString());
        try
        {
            Console.CursorVisible = true;
        }
        catch
        {
        }

        RestoreWindowsConsoleMode();
    }

    /// <summary>
    ///     下一帧整屏重绘（窗口尺寸变化或图片模式切换后调用）。
    /// </summary>
    public void Invalidate()
    {
        _forceFull = true;
    }

    public void Present(Canvas canvas)
    {
        var width = canvas.Width;
        var height = canvas.Height;
        var full = _forceFull || width != _previousWidth || height != _previousHeight ||
                   _previous.Length != canvas.Cells.Length;
        _buffer.Clear();
        _buffer.Append("\u001b[?25l");

        if (full)
        {
            if (_kittyUsed)
            {
                ImageProtocolEncoder.AppendKittyDeleteAll(_buffer);
            }

            _buffer.Append("\u001b[0m\u001b[2J");
            _shown.Clear();
        }

        var current = new Dictionary<int, ImagePlacement>();
        foreach (var placement in canvas.Placements)
        {
            current[placement.Id] = placement;
        }

        foreach (var id in _shown.Keys)
        {
            if (!current.ContainsKey(id) && _kittyUsed)
            {
                ImageProtocolEncoder.AppendKittyDelete(_buffer, id);
            }
        }

        WriteCellDiff(canvas, full);
        WritePlacements(canvas, current);

        _buffer.Append("\u001b[0m");
        WriteRaw(_buffer.ToString());

        if (_previous.Length != canvas.Cells.Length)
        {
            _previous = new Cell[canvas.Cells.Length];
        }

        Array.Copy(canvas.Cells, _previous, canvas.Cells.Length);
        _previousWidth = width;
        _previousHeight = height;
        _forceFull = false;
        _shown.Clear();
        foreach (var pair in current)
        {
            _shown[pair.Key] = pair.Value;
        }
    }

    private void WriteCellDiff(Canvas canvas, bool full)
    {
        var width = canvas.Width;
        var height = canvas.Height;
        var cursorX = -1;
        var cursorY = -1;
        CellStyle? activeStyle = null;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                var cell = canvas.Cells[index];
                if (!full && cell.Equals(_previous[index]))
                {
                    continue;
                }

                if (cell.Width == 0)
                {
                    // 宽字符右半边：若左半边没变则需要重写左半边。
                    if (x > 0 && !full && canvas.Cells[index - 1].Equals(_previous[index - 1]))
                    {
                        var lead = canvas.Cells[index - 1];
                        MoveTo(x - 1, y, ref cursorX, ref cursorY);
                        ApplyStyle(lead.Style, ref activeStyle);
                        AppendRune(lead.Rune);
                        cursorX += lead.Width;
                    }

                    continue;
                }

                MoveTo(x, y, ref cursorX, ref cursorY);
                ApplyStyle(cell.Style, ref activeStyle);
                AppendRune(cell.ImageId != 0 ? ' ' : cell.Rune);
                cursorX += cell.Width;
            }
        }
    }

    private void WritePlacements(Canvas canvas, Dictionary<int, ImagePlacement> current)
    {
        foreach (var placement in current.Values)
        {
            if (_shown.ContainsKey(placement.Id))
            {
                continue;
            }

            var area = placement.Area;
            _buffer.Append("\u001b[0m");
            _buffer.Append("\u001b[").Append(area.Y + 1).Append(';').Append(area.X + 1).Append('H');
            switch (canvas.ImageMode)
            {
                case TerminalImageMode.Kitty:
                {
                    var (pw, ph) = ProtocolPixelSize(placement);
                    ImageProtocolEncoder.AppendKitty(_buffer, placement.Image, placement.Id, area.W, area.H, pw, ph);
                    _kittyUsed = true;
                    break;
                }
                case TerminalImageMode.ITerm:
                {
                    var (pw, ph) = ProtocolPixelSize(placement);
                    ImageProtocolEncoder.AppendITerm(_buffer, placement.Image, area.W, area.H, pw, ph);
                    break;
                }
                case TerminalImageMode.Sixel:
                    ImageProtocolEncoder.AppendSixel(_buffer, placement.Image,
                        Math.Max(1, area.W * CellPixelSize.Width), Math.Max(1, area.H * CellPixelSize.Height));
                    break;
            }
        }
    }

    private static (int Width, int Height) ProtocolPixelSize(ImagePlacement placement)
    {
        var image = placement.Image;
        if (image.Crisp)
        {
            return (image.Width, image.Height);
        }

        // 以 1:2 字符格估算目标像素，限制最大边长以控制传输量。
        var width = placement.Area.W * 12;
        var height = placement.Area.H * 24;
        var scale = Math.Min(1.0, 720.0 / Math.Max(width, height));
        return (Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)));
    }

    private void MoveTo(int x, int y, ref int cursorX, ref int cursorY)
    {
        if (x == cursorX && y == cursorY)
        {
            return;
        }

        _buffer.Append("\u001b[").Append(y + 1).Append(';').Append(x + 1).Append('H');
        cursorX = x;
        cursorY = y;
    }

    private void ApplyStyle(CellStyle style, ref CellStyle? active)
    {
        if (active.HasValue && active.Value.Equals(style))
        {
            return;
        }

        active = style;
        _buffer.Append("\u001b[0");
        if (style.Attr.HasFlag(TextAttr.Bold))
        {
            _buffer.Append(";1");
        }

        if (style.Attr.HasFlag(TextAttr.Dim))
        {
            _buffer.Append(";2");
        }

        if (style.Attr.HasFlag(TextAttr.Italic))
        {
            _buffer.Append(";3");
        }

        if (style.Attr.HasFlag(TextAttr.Underline))
        {
            _buffer.Append(";4");
        }

        if (style.Fg is { } fg)
        {
            AppendColor(38, fg);
        }

        if (style.Bg is { } bg)
        {
            AppendColor(48, bg);
        }

        _buffer.Append('m');
    }

    private void AppendColor(int prefix, Rgb color)
    {
        if (TrueColor)
        {
            _buffer.Append(';').Append(prefix).Append(";2;").Append(color.R).Append(';').Append(color.G).Append(';')
                .Append(color.B);
        }
        else
        {
            _buffer.Append(';').Append(prefix).Append(";5;").Append(To256(color));
        }
    }

    private void AppendRune(int rune)
    {
        if (rune < 0x10000)
        {
            _buffer.Append((char)rune);
        }
        else
        {
            _buffer.Append(char.ConvertFromUtf32(rune));
        }
    }

    /// <summary>
    ///     将 RGB 映射到 xterm 256 色（6×6×6 色立方或灰阶中更接近者）。
    /// </summary>
    private static int To256(Rgb color)
    {
        static int CubeIndex(int v) => v < 48 ? 0 : v < 115 ? 1 : Math.Min(5, (v - 35) / 40);

        var ri = CubeIndex(color.R);
        var gi = CubeIndex(color.G);
        var bi = CubeIndex(color.B);
        var cubeR = CubeLevels[ri];
        var cubeG = CubeLevels[gi];
        var cubeB = CubeLevels[bi];

        var average = (color.R + color.G + color.B) / 3;
        var grayIndex = average > 238 ? 23 : Math.Max(0, (average - 3) / 10);
        var grayValue = 8 + grayIndex * 10;

        var cubeDistance = Sq(color.R - cubeR) + Sq(color.G - cubeG) + Sq(color.B - cubeB);
        var grayDistance = Sq(color.R - grayValue) + Sq(color.G - grayValue) + Sq(color.B - grayValue);
        return grayDistance < cubeDistance ? 232 + grayIndex : 16 + ri * 36 + gi * 6 + bi;
    }

    private static int Sq(int v) => v * v;

    private void WriteRaw(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        _output.Write(bytes, 0, bytes.Length);
        _output.Flush();
    }

    /// <summary>
    ///     Windows 经典控制台主机（conhost.exe）默认关闭了 ENABLE_VIRTUAL_TERMINAL_PROCESSING，需显式开启，否则所有 \x1b[... 会直接打印成乱码。
    /// </summary>
    public static void EnableWindowsVirtualTerminalStatic()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            SetConsoleOutputCP(CpUtf8);
            SetConsoleCP(CpUtf8);
            var outHandle = GetStdHandle(StdOutputHandle);
            if (outHandle != nint.Zero && GetConsoleMode(outHandle, out var outMode))
            {
                SetConsoleMode(outHandle,
                    outMode | EnableProcessedOutput | EnableVirtualTerminalProcessing | DisableNewlineAutoReturn);
            }
        }
        catch
        {
        }
    }

    private void EnableWindowsVirtualTerminal()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            SetConsoleOutputCP(CpUtf8);
            SetConsoleCP(CpUtf8);
            var outHandle = GetStdHandle(StdOutputHandle);
            var inHandle = GetStdHandle(StdInputHandle);
            if (outHandle != nint.Zero && GetConsoleMode(outHandle, out _savedOutMode))
            {
                _hasSavedConsoleMode = true;
                SetConsoleMode(outHandle,
                    _savedOutMode | EnableProcessedOutput | EnableVirtualTerminalProcessing | DisableNewlineAutoReturn);
            }

            if (inHandle != nint.Zero && GetConsoleMode(inHandle, out _savedInMode))
            {
                // 注意：不能在输入句柄上开启 ENABLE_VIRTUAL_TERMINAL_INPUT（会把方向键拆成 ESC [ A 三次按键）；
                // 开启 ENABLE_MOUSE_INPUT 支持鼠标点击/滚轮，关闭 ENABLE_QUICK_EDIT_MODE 避免点击控制台挂起进程。
                var newInMode = (_savedInMode | EnableMouseInput | EnableExtendedFlags) &
                                ~(EnableVirtualTerminalInput | EnableQuickEditMode);
                SetConsoleMode(inHandle, newInMode);
            }
        }
        catch
        {
        }
    }

    private void RestoreWindowsConsoleMode()
    {
        if (!OperatingSystem.IsWindows() || !_hasSavedConsoleMode)
        {
            return;
        }

        try
        {
            var outHandle = GetStdHandle(StdOutputHandle);
            var inHandle = GetStdHandle(StdInputHandle);
            if (outHandle != nint.Zero)
            {
                SetConsoleMode(outHandle, _savedOutMode);
            }

            if (inHandle != nint.Zero && _savedInMode != 0)
            {
                SetConsoleMode(inHandle, _savedInMode);
            }
        }
        catch
        {
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(nint hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(nint hConsoleHandle, uint dwMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleOutputCP(uint wCodePageID);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCP(uint wCodePageID);

    public void Dispose()
    {
        Exit();
        _output.Dispose();
    }
}
