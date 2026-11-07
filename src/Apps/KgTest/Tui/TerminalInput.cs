using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace KgTest.Tui;

// 跨平台终端输入读取器：在 Windows 使用 ReadConsoleInputW 读取按键与鼠标事件，在 Unix 解析 VT 序列与 SGR 1006 鼠标报告。

internal enum MouseKind
{
    LeftDown,
    LeftDoubleClick,
    RightDown,
    WheelUp,
    WheelDown
}

internal readonly record struct MouseEvent(int X, int Y, MouseKind Kind);

internal sealed class TerminalInput : IDisposable
{
    private const int StdInputHandle = -10;
    private const ushort KeyEventType = 0x0001;
    private const ushort MouseEventType = 0x0002;
    private const uint FromLeft1StButtonPressed = 0x0001;
    private const uint RightmostButtonPressed = 0x0002;
    private const uint DoubleClickFlag = 0x0002;
    private const uint MouseWheeledFlag = 0x0004;

    private readonly ConcurrentQueue<ConsoleKeyInfo> _keys = new();
    private readonly ConcurrentQueue<MouseEvent> _mice = new();
    private readonly Action _wake;
    private readonly CancellationTokenSource _cts = new();
    private uint _previousButtons;
    private int _lastClickX = -1;
    private int _lastClickY = -1;
    private long _lastClickTick;

    public TerminalInput(Action wake)
    {
        _wake = wake;
        if (Console.IsInputRedirected)
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            var thread = new Thread(WindowsInputLoop) { IsBackground = true, Name = "KgTest-WinInput" };
            thread.Start();
        }
        else
        {
            var thread = new Thread(UnixInputLoop) { IsBackground = true, Name = "KgTest-UnixInput" };
            thread.Start();
        }
    }

    public bool HasPending => !_keys.IsEmpty || !_mice.IsEmpty;

    public bool TryReadKey(out ConsoleKeyInfo key) => _keys.TryDequeue(out key);

    public bool TryReadMouse(out MouseEvent mouse) => _mice.TryDequeue(out mouse);

    private void WindowsInputLoop()
    {
        var handle = GetStdHandle(StdInputHandle);
        if (handle == nint.Zero)
        {
            return;
        }

        var records = new InputRecord[16];
        while (!_cts.IsCancellationRequested)
        {
            if (!ReadConsoleInputW(handle, records, (uint)records.Length, out var read) || read == 0)
            {
                Thread.Sleep(10);
                continue;
            }

            var notified = false;
            for (var i = 0; i < read; i++)
            {
                var rec = records[i];
                if (rec.EventType == KeyEventType)
                {
                    var k = rec.KeyEvent;
                    if (k.KeyDown == 0 || k.VirtualKeyCode is 0x10 or 0x11 or 0x12 or 0x14 or 0x5B or 0x5C)
                    {
                        continue;
                    }

                    var shift = (k.ControlKeyState & 0x0010) != 0;
                    var alt = (k.ControlKeyState & (0x0001 | 0x0002)) != 0;
                    var ctrl = (k.ControlKeyState & (0x0004 | 0x0008)) != 0;
                    var repeat = Math.Max((ushort)1, k.RepeatCount);
                    var info = new ConsoleKeyInfo(k.UnicodeChar, (ConsoleKey)k.VirtualKeyCode, shift, alt, ctrl);
                    for (var r = 0; r < repeat; r++)
                    {
                        _keys.Enqueue(info);
                    }

                    notified = true;
                }
                else if (rec.EventType == MouseEventType)
                {
                    if (TryTranslateWindowsMouse(rec.MouseEvent, out var evt))
                    {
                        _mice.Enqueue(evt);
                        notified = true;
                    }
                }
            }

            if (notified)
            {
                _wake();
            }
        }
    }

    private bool TryTranslateWindowsMouse(MouseEventRecord m, out MouseEvent evt)
    {
        var x = (int)m.PositionX;
        var y = (int)m.PositionY;
        if ((m.EventFlags & MouseWheeledFlag) != 0)
        {
            var delta = (short)(m.ButtonState >> 16);
            evt = new MouseEvent(x, y, delta > 0 ? MouseKind.WheelUp : MouseKind.WheelDown);
            return true;
        }

        var leftNow = (m.ButtonState & FromLeft1StButtonPressed) != 0;
        var leftWas = (_previousButtons & FromLeft1StButtonPressed) != 0;
        var rightNow = (m.ButtonState & RightmostButtonPressed) != 0;
        var rightWas = (_previousButtons & RightmostButtonPressed) != 0;
        _previousButtons = m.ButtonState;

        if ((m.EventFlags & DoubleClickFlag) != 0 && leftNow)
        {
            evt = new MouseEvent(x, y, MouseKind.LeftDoubleClick);
            return true;
        }

        if (m.EventFlags == 0)
        {
            if (leftNow && !leftWas)
            {
                evt = ClassifyLeftClick(x, y);
                return true;
            }

            if (rightNow && !rightWas)
            {
                evt = new MouseEvent(x, y, MouseKind.RightDown);
                return true;
            }
        }

        evt = default;
        return false;
    }

    private void UnixInputLoop()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var first = Console.ReadKey(true);
                if (first.Key == ConsoleKey.Escape && Console.KeyAvailable)
                {
                    ParseUnixEscapeSequence();
                }
                else
                {
                    _keys.Enqueue(first);
                }

                _wake();
            }
        }
        catch
        {
        }
    }

    private void ParseUnixEscapeSequence()
    {
        var second = Console.ReadKey(true);
        if (second.KeyChar != '[' || !Console.KeyAvailable)
        {
            _keys.Enqueue(new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, false, false, false));
            _keys.Enqueue(second);
            return;
        }

        var third = Console.ReadKey(true);
        if (third.KeyChar == '<')
        {
            // SGR 鼠标序列：\x1b[<btn;x;yM 或 m
            var buffer = new char[32];
            var len = 0;
            var terminator = '\0';
            while (len < buffer.Length)
            {
                var k = Console.ReadKey(true);
                if (k.KeyChar is 'M' or 'm')
                {
                    terminator = k.KeyChar;
                    break;
                }

                buffer[len++] = k.KeyChar;
            }

            var parts = new string(buffer, 0, len).Split(';');
            if (parts.Length == 3 &&
                int.TryParse(parts[0], out var btn) &&
                int.TryParse(parts[1], out var cx) &&
                int.TryParse(parts[2], out var cy))
            {
                var x = Math.Max(0, cx - 1);
                var y = Math.Max(0, cy - 1);
                if (btn == 64)
                {
                    _mice.Enqueue(new MouseEvent(x, y, MouseKind.WheelUp));
                }
                else if (btn == 65)
                {
                    _mice.Enqueue(new MouseEvent(x, y, MouseKind.WheelDown));
                }
                else if (terminator == 'M' && (btn & 32) == 0)
                {
                    if ((btn & 3) == 0)
                    {
                        _mice.Enqueue(ClassifyLeftClick(x, y));
                    }
                    else if ((btn & 3) == 2)
                    {
                        _mice.Enqueue(new MouseEvent(x, y, MouseKind.RightDown));
                    }
                }
            }

            return;
        }

        var mapped = third.KeyChar switch
        {
            'A' => ConsoleKey.UpArrow,
            'B' => ConsoleKey.DownArrow,
            'C' => ConsoleKey.RightArrow,
            'D' => ConsoleKey.LeftArrow,
            'H' => ConsoleKey.Home,
            'F' => ConsoleKey.End,
            _ => ConsoleKey.None
        };
        if (mapped != ConsoleKey.None)
        {
            _keys.Enqueue(new ConsoleKeyInfo('\0', mapped, false, false, false));
        }
    }

    private MouseEvent ClassifyLeftClick(int x, int y)
    {
        var now = Environment.TickCount64;
        if (x == _lastClickX && y == _lastClickY && now - _lastClickTick <= 420)
        {
            _lastClickTick = 0;
            return new MouseEvent(x, y, MouseKind.LeftDoubleClick);
        }

        _lastClickX = x;
        _lastClickY = y;
        _lastClickTick = now;
        return new MouseEvent(x, y, MouseKind.LeftDown);
    }

    public void Dispose()
    {
        try
        {
            _cts.Cancel();
            _cts.Dispose();
        }
        catch
        {
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputRecord
    {
        [FieldOffset(0)] public ushort EventType;
        [FieldOffset(4)] public KeyEventRecord KeyEvent;
        [FieldOffset(4)] public MouseEventRecord MouseEvent;
    }

    [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode)]
    private struct KeyEventRecord
    {
        [FieldOffset(0)] public int KeyDown;
        [FieldOffset(4)] public ushort RepeatCount;
        [FieldOffset(6)] public ushort VirtualKeyCode;
        [FieldOffset(8)] public ushort VirtualScanCode;
        [FieldOffset(10)] public char UnicodeChar;
        [FieldOffset(12)] public uint ControlKeyState;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct MouseEventRecord
    {
        [FieldOffset(0)] public short PositionX;
        [FieldOffset(2)] public short PositionY;
        [FieldOffset(4)] public uint ButtonState;
        [FieldOffset(8)] public uint ControlKeyState;
        [FieldOffset(12)] public uint EventFlags;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", EntryPoint = "ReadConsoleInputW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ReadConsoleInputW(
        nint hConsoleInput,
        [Out] InputRecord[] lpBuffer,
        uint nLength,
        out uint lpNumberOfEventsRead);
}
