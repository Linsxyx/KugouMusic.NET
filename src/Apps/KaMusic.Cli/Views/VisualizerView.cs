using KaMusic.Cli.Graphics;
using KaMusic.Cli.Models;
using KaMusic.Cli.Tui;

namespace KaMusic.Cli.Views;

// 频谱与沉浸式播放页渲染：包含专辑封面、动态主题色、KRC 逐字高亮歌词与平滑频谱。

internal sealed class VisualizerView
{
    private float[] _bars = [];
    private float[] _peaks = [];

    public void RenderImmersive(Canvas canvas, PlaybackStateSnapshot snapshot, ImageCache images, Theme theme)
    {
        var bounds = canvas.Bounds;
        var song = snapshot.CurrentSong;
        var cover = images.Get(song?.Cover);
        var accent = cover?.Accent ?? theme.Accent;

        var header = bounds.TakeTop(3);
        var footer = bounds.TakeBottom(4);
        var middle = bounds.SkipTop(3).SkipBottom(4);

        RenderHeader(canvas, header, song, accent, theme);

        // 左侧封面 + 右侧逐字歌词，下方频谱。
        var specHeight = Math.Clamp(middle.H / 3, 6, 14);
        var upper = middle.SkipBottom(specHeight);
        var lower = middle.TakeBottom(specHeight);

        if (cover != null && canvas.ImageMode != TerminalImageMode.Off && upper.W >= 54 && upper.H >= 8)
        {
            var artWidth = Math.Clamp(upper.H * 2 + 4, 20, upper.W * 2 / 5);
            var artBox = upper.TakeLeft(artWidth).Inset(2, 1);
            var lyricBox = upper.SkipLeft(artWidth).Inset(2, 1);
            canvas.Image("immersive-cover", artBox, cover);
            RenderLyrics(canvas, lyricBox, snapshot, accent, theme);
        }
        else
        {
            RenderLyrics(canvas, upper.Inset(3, 1), snapshot, accent, theme);
        }

        RenderSpectrum(canvas, lower.Inset(2, 0), snapshot, accent, theme);
        RenderImmersiveFooter(canvas, footer, snapshot, accent, theme);
    }

    public void RenderSpectrum(Canvas canvas, Rect area, PlaybackStateSnapshot snapshot, Rgb accent, Theme theme)
    {
        if (area.W < 12 || area.H < 3)
        {
            return;
        }

        if (snapshot.SpectrumBands.Count == 0)
        {
            canvas.TextAligned(area, area.Y + area.H / 2, "等待音频频谱数据...", theme.FaintStyle, 1);
            return;
        }

        var barCount = Math.Clamp(area.W / 2, 16, 96);
        var bands = Resample(snapshot.SpectrumBands, barCount);
        EnsureCount(barCount);

        var boost = 0.84f + (float)Math.Clamp(snapshot.Rms * 5.2, 0, 0.55);
        for (var i = 0; i < barCount; i++)
        {
            var shaped = (float)Math.Pow(Math.Clamp(bands[i], 0f, 1f), 0.62);
            var target = Math.Clamp(shaped * boost, 0f, 1f);
            var rate = target > _bars[i] ? 0.62f : 0.24f;
            _bars[i] = _bars[i] + (target - _bars[i]) * rate;
            _peaks[i] = Math.Max(_bars[i], _peaks[i] - 0.018f);
        }

        var chartHeight = area.H - 1;
        var totalWidth = barCount * 2 - 1;
        var startX = area.X + Math.Max(0, (area.W - totalWidth) / 2);

        for (var row = 0; row < chartHeight; row++)
        {
            var levelFromBottom = chartHeight - row;
            var threshold = levelFromBottom / (float)chartHeight;
            var color = accent.Mix(new Rgb(255, 120, 180), (levelFromBottom - 1f) / Math.Max(1, chartHeight - 1));
            var barStyle = new CellStyle(color);
            var peakStyle = new CellStyle(color.Mix(new Rgb(255, 255, 255), 0.35f));

            for (var i = 0; i < barCount; i++)
            {
                var x = startX + i * 2;
                if (x >= area.Right)
                {
                    break;
                }

                var value = _bars[i];
                if (value >= threshold)
                {
                    canvas.Set(x, area.Y + row, '█', barStyle);
                }
                else if (value >= threshold - 0.5f / chartHeight)
                {
                    canvas.Set(x, area.Y + row, '▄', barStyle);
                }
                else if (Math.Abs(_peaks[i] - threshold) <= 0.55f / chartHeight)
                {
                    canvas.Set(x, area.Y + row, '▀', peakStyle);
                }
                else if (levelFromBottom == 1)
                {
                    canvas.Set(x, area.Y + row, '▁', theme.FaintStyle);
                }
            }
        }

        var railY = area.Bottom - 1;
        canvas.Text(startX, railY, "50Hz", theme.FaintStyle, 6);
        canvas.TextAligned(new Rect(startX, railY, Math.Min(totalWidth, area.W), 1), railY, "1kHz", theme.FaintStyle, 1);
        canvas.TextAligned(new Rect(startX, railY, Math.Min(totalWidth, area.W), 1), railY, "16kHz", theme.FaintStyle, 2);
    }

    /// <summary>
    ///     渲染歌词窗口；当前行若带有 KRC 逐字时间轴则按播放进度染色。
    /// </summary>
    public static void RenderLyrics(Canvas canvas, Rect area, PlaybackStateSnapshot snapshot, Rgb accent, Theme theme)
    {
        if (area.IsEmpty)
        {
            return;
        }

        var lines = snapshot.LyricLines;
        if (lines.Count == 0)
        {
            canvas.TextAligned(area, area.Y + area.H / 2, "暂无歌词", theme.MutedStyle, 1);
            return;
        }

        var centerY = area.Y + area.H / 2;
        foreach (var line in lines)
        {
            var y = centerY + line.Offset * 2;
            if (y < area.Y || y >= area.Bottom || string.IsNullOrWhiteSpace(line.Primary))
            {
                continue;
            }

            if (line.IsCurrent)
            {
                RenderCurrentLyricLine(canvas, area, y, line, accent, theme);
                if (!string.IsNullOrWhiteSpace(line.Secondary) && y + 1 < area.Bottom)
                {
                    canvas.TextAligned(area, y + 1, line.Secondary, new CellStyle(accent.Mix(theme.Muted, 0.5f)), 1);
                }
            }
            else
            {
                var distance = Math.Abs(line.Offset);
                var fg = distance == 1 ? theme.Muted : theme.Faint;
                canvas.TextAligned(area, y, line.Primary, new CellStyle(fg), 1);
                if (distance == 1 && !string.IsNullOrWhiteSpace(line.Secondary) && y + 1 < area.Bottom &&
                    y + 1 != centerY)
                {
                    canvas.TextAligned(area, y + 1, line.Secondary, theme.FaintStyle, 1);
                }
            }
        }
    }

    private static void RenderCurrentLyricLine(Canvas canvas, Rect area, int y, TerminalLyricLineView line, Rgb accent,
        Theme theme)
    {
        var sungStyle = new CellStyle(accent, Attr: TextAttr.Bold);
        var unsungStyle = new CellStyle(theme.Text, Attr: TextAttr.Bold);

        if (line.Words.Count == 0 || TextWidth.Of(line.Primary) > area.W)
        {
            canvas.TextAligned(area, y, line.Primary, sungStyle, 1);
            return;
        }

        var totalWidth = TextWidth.Of(line.Primary);
        var x = area.X + Math.Max(0, (area.W - totalWidth) / 2);
        foreach (var (word, progress) in line.Words)
        {
            if (string.IsNullOrEmpty(word))
            {
                continue;
            }

            var style = progress >= 0.99f
                ? sungStyle
                : progress <= 0.01f
                    ? unsungStyle
                    : new CellStyle(unsungStyle.Fg!.Value.Mix(accent, progress), Attr: TextAttr.Bold);
            x += canvas.Text(x, y, word, style, area.Right - x);
        }
    }

    private static void RenderHeader(Canvas canvas, Rect area, TerminalSongItem? song, Rgb accent, Theme theme)
    {
        var title = song == null ? "尚未播放歌曲" : song.Name;
        var subtitle = song == null ? "在列表中选择一首歌曲按 Enter 播放" : $"{song.Singer}  ·  {song.Source}";
        canvas.TextAligned(area, area.Y + 1, title, new CellStyle(accent, Attr: TextAttr.Bold), 1);
        canvas.TextAligned(area, area.Y + 2, subtitle, theme.MutedStyle, 1);
    }

    private static void RenderImmersiveFooter(Canvas canvas, Rect area, PlaybackStateSnapshot snapshot, Rgb accent,
        Theme theme)
    {
        var inner = area.Inset(4, 0);
        if (inner.IsEmpty)
        {
            return;
        }

        DrawProgressLine(canvas, inner.X, inner.Y + 1, inner.W, snapshot.Position, snapshot.Duration, accent, theme);
        var hints =
            $"Space {(snapshot.IsPlaying ? "暂停" : "播放")}   ←/→ 快退/快进   n/b 切歌   l 歌词({snapshot.LyricMode})   p 图片协议   i/Esc 返回";
        canvas.TextAligned(inner, inner.Y + 2, hints, theme.MutedStyle, 1);
    }

    public static void DrawProgressLine(Canvas canvas, int x, int y, int width, TimeSpan position, TimeSpan duration,
        Rgb accent, Theme theme)
    {
        var posText = FormatTime(position.TotalSeconds);
        var durText = FormatTime(duration.TotalSeconds);
        canvas.Text(x, y, posText, theme.MutedStyle, 8);
        canvas.Text(x + width - durText.Length, y, durText, theme.MutedStyle, 8);

        var barX = x + posText.Length + 2;
        var barW = width - posText.Length - durText.Length - 4;
        if (barW < 6)
        {
            return;
        }

        var ratio = duration.TotalSeconds > 0
            ? Math.Clamp(position.TotalSeconds / duration.TotalSeconds, 0.0, 1.0)
            : 0.0;
        var filled = (int)Math.Round(barW * ratio);
        var activeStyle = new CellStyle(accent);
        for (var i = 0; i < barW; i++)
        {
            if (i < filled)
            {
                canvas.Set(barX + i, y, '━', activeStyle);
            }
            else if (i == filled && duration.TotalSeconds > 0)
            {
                canvas.Set(barX + i, y, '●', new CellStyle(theme.Text, Attr: TextAttr.Bold));
            }
            else
            {
                canvas.Set(barX + i, y, '─', theme.FaintStyle);
            }
        }
    }

    public static string FormatTime(double seconds)
    {
        if (seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
        {
            return "--:--";
        }

        return TimeSpan.FromSeconds(seconds).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"mm\:ss");
    }

    private void EnsureCount(int count)
    {
        if (_bars.Length == count)
        {
            return;
        }

        _bars = new float[count];
        _peaks = new float[count];
    }

    private static float[] Resample(IReadOnlyList<float> source, int count)
    {
        var result = new float[count];
        if (source.Count == 0)
        {
            return result;
        }

        if (source.Count == 1)
        {
            Array.Fill(result, source[0]);
            return result;
        }

        for (var i = 0; i < count; i++)
        {
            var pos = i * (source.Count - 1) / (double)Math.Max(1, count - 1);
            var left = (int)Math.Floor(pos);
            var right = Math.Min(source.Count - 1, left + 1);
            var t = (float)(pos - left);
            result[i] = source[left] + (source[right] - source[left]) * t;
        }

        return result;
    }
}
