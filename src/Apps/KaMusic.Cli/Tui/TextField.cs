using System.Globalization;
using System.Text;

namespace KaMusic.Cli.Tui;

// 单行文本输入框：支持中文输入、光标移动、删除，以及按显示宽度横向滚动。

internal sealed class TextField
{
    private readonly StringBuilder _text = new();
    private int _cursor; // 以 text element（字素）为单位

    public TextField(string placeholder = "", int maxLength = 64, Func<char, bool>? filter = null)
    {
        Placeholder = placeholder;
        MaxLength = maxLength;
        Filter = filter;
    }

    public string Placeholder { get; set; }

    public int MaxLength { get; }

    public Func<char, bool>? Filter { get; }

    public string Text
    {
        get => _text.ToString();
        set
        {
            _text.Clear().Append(value);
            _cursor = Elements().Count;
        }
    }

    /// <summary>
    ///     处理一次按键，返回是否被输入框消费。
    /// </summary>
    public bool HandleKey(ConsoleKeyInfo key)
    {
        var elements = Elements();
        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
                _cursor = Math.Max(0, _cursor - 1);
                return true;
            case ConsoleKey.RightArrow:
                _cursor = Math.Min(elements.Count, _cursor + 1);
                return true;
            case ConsoleKey.Home:
                _cursor = 0;
                return true;
            case ConsoleKey.End:
                _cursor = elements.Count;
                return true;
            case ConsoleKey.Backspace:
                if (_cursor > 0)
                {
                    elements.RemoveAt(_cursor - 1);
                    _cursor--;
                    Rebuild(elements);
                }

                return true;
            case ConsoleKey.Delete:
                if (_cursor < elements.Count)
                {
                    elements.RemoveAt(_cursor);
                    Rebuild(elements);
                }

                return true;
        }

        if ((key.Modifiers & ConsoleModifiers.Control) != 0)
        {
            if (key.Key == ConsoleKey.U)
            {
                Text = "";
                _cursor = 0;
                return true;
            }

            if (key.Key == ConsoleKey.A)
            {
                _cursor = 0;
                return true;
            }

            if (key.Key == ConsoleKey.E)
            {
                _cursor = elements.Count;
                return true;
            }

            return false;
        }

        var ch = key.KeyChar;
        if (ch == '\0' || char.IsControl(ch))
        {
            return false;
        }

        if (Filter != null && !Filter(ch))
        {
            return true;
        }

        if (_text.Length >= MaxLength)
        {
            return true;
        }

        elements.Insert(_cursor, ch.ToString());
        _cursor++;
        Rebuild(elements);
        return true;
    }

    /// <summary>
    ///     根据点击在输入框内的列偏移移动光标。
    /// </summary>
    public void SetCursorFromVisualColumn(int columnOffset, int width)
    {
        var elements = Elements();
        if (elements.Count == 0)
        {
            _cursor = 0;
            return;
        }

        var before = string.Concat(elements.Take(_cursor));
        var scroll = Math.Max(0, TextWidth.Of(before) - (width - 2));
        var targetVisual = Math.Max(0, columnOffset + scroll);
        var acc = 0;
        for (var i = 0; i < elements.Count; i++)
        {
            var w = Math.Max(1, TextWidth.Of(elements[i]));
            if (targetVisual < acc + w)
            {
                _cursor = i;
                return;
            }

            acc += w;
        }

        _cursor = elements.Count;
    }

    /// <summary>
    ///     在给定位置绘制输入框；focused 时显示块状光标。
    /// </summary>
    public void Render(Canvas canvas, int x, int y, int width, bool focused, Theme theme)
    {
        var area = new Rect(x, y, width, 1);
        canvas.Fill(area, new CellStyle(Bg: focused ? theme.InputFocusBg : theme.InputBg));
        if (_text.Length == 0)
        {
            if (focused)
            {
                canvas.Set(x, y, ' ', new CellStyle(theme.InputFocusBg, theme.Text));
            }

            canvas.Text(x + (focused ? 1 : 0), y, Placeholder,
                new CellStyle(theme.Muted, focused ? theme.InputFocusBg : theme.InputBg), width - 1);
            return;
        }

        var elements = Elements();
        var before = string.Concat(elements.Take(_cursor));
        var beforeWidth = TextWidth.Of(before);
        var scroll = Math.Max(0, beforeWidth - (width - 2));

        var style = new CellStyle(theme.Text, focused ? theme.InputFocusBg : theme.InputBg);
        var column = -scroll;
        for (var i = 0; i <= elements.Count; i++)
        {
            var isCursor = focused && i == _cursor;
            var element = i < elements.Count ? elements[i] : " ";
            var w = Math.Max(1, TextWidth.Of(element));
            if (column >= 0 && column + w <= width)
            {
                var cellStyle = isCursor ? new CellStyle(style.Bg, theme.Text) : style;
                canvas.Text(x + column, y, element, cellStyle, w);
            }

            column += w;
        }
    }

    private List<string> Elements()
    {
        var result = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(_text.ToString());
        while (enumerator.MoveNext())
        {
            result.Add(enumerator.GetTextElement());
        }

        return result;
    }

    private void Rebuild(List<string> elements)
    {
        _text.Clear();
        foreach (var element in elements)
        {
            _text.Append(element);
        }

        _cursor = Math.Clamp(_cursor, 0, elements.Count);
    }
}
