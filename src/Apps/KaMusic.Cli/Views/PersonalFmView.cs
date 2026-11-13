using KaMusic.Cli.Graphics;
using KaMusic.Cli.Models;
using KaMusic.Cli.Tui;

namespace KaMusic.Cli.Views;

// 私人 FM 手机播放页风格 TUI 视图：顶部频道/偏好切换、居中封面与动态主题色、逐字歌词、进度条与交互控制栏。

internal sealed class PersonalFmViewState
{
    public bool IsLoggedIn { get; init; }
    public bool IsLoading { get; init; }
    public bool IsFmActive { get; init; }
    public bool IsPlaying { get; init; }
    public bool IsPaused { get; init; }
    public TerminalPersonalFmMode SelectedMode { get; init; }
    public TerminalPersonalFmSongPoolId SelectedSongPoolId { get; init; }
    public TerminalSongItem? CurrentSong { get; init; }
    public IReadOnlyList<TerminalSongItem> UpcomingSongs { get; init; } = [];
    public bool HasHistory { get; init; }
    public string Quality { get; init; } = "high";
    public PlaybackStateSnapshot PlaybackSnapshot { get; init; } = new();
}

internal sealed class PersonalFmHitRegions
{
    public Rect ModeNormalRect { get; set; }
    public Rect ModeSmallRect { get; set; }
    public Rect ModePeakRect { get; set; }
    public Rect PoolTasteRect { get; set; }
    public Rect PoolStyleRect { get; set; }
    public Rect ProgressBarRect { get; set; }
    public int ProgressBarStartX { get; set; }
    public int ProgressBarWidth { get; set; }
    public Rect PreviousButtonRect { get; set; }
    public Rect DislikeButtonRect { get; set; }
    public Rect PlayPauseButtonRect { get; set; }
    public Rect NextButtonRect { get; set; }
    public Rect FavoriteButtonRect { get; set; }
    public Rect RefreshButtonRect { get; set; }

    public void Reset()
    {
        ModeNormalRect = Rect.Empty;
        ModeSmallRect = Rect.Empty;
        ModePeakRect = Rect.Empty;
        PoolTasteRect = Rect.Empty;
        PoolStyleRect = Rect.Empty;
        ProgressBarRect = Rect.Empty;
        ProgressBarStartX = 0;
        ProgressBarWidth = 0;
        PreviousButtonRect = Rect.Empty;
        DislikeButtonRect = Rect.Empty;
        PlayPauseButtonRect = Rect.Empty;
        NextButtonRect = Rect.Empty;
        FavoriteButtonRect = Rect.Empty;
        RefreshButtonRect = Rect.Empty;
    }
}

internal static class PersonalFmView
{
    public static void Render(
        Canvas canvas,
        Rect area,
        PersonalFmViewState state,
        PersonalFmHitRegions hitRegions,
        bool focused,
        ImageCache images,
        Theme theme)
    {
        hitRegions.Reset();
        var song = state.CurrentSong;
        var cover = images.Get(song?.Cover);
        var accent = cover?.Accent ?? theme.Accent;

        var borderStyle = focused ? new CellStyle(accent) : theme.BorderStyle;
        var titleText = $"♫ 私人 FM · {TerminalPersonalFmPresentation.GetTitle(state.SelectedMode)}";
        canvas.Box(area, borderStyle, titleText, focused ? new CellStyle(accent, Attr: TextAttr.Bold) : theme.Bold);

        var inner = area.Inset(2, 1);
        if (inner.IsEmpty || inner.H < 6)
        {
            return;
        }

        RenderTopSelectorBar(canvas, inner, state, hitRegions, accent, theme);
        canvas.HLine(inner.X, inner.Y + 2, inner.W, theme.FaintStyle);

        var body = inner.SkipTop(3);
        if (body.IsEmpty)
        {
            return;
        }

        if (!state.IsLoggedIn)
        {
            RenderCenteredMessage(
                canvas,
                body,
                "登录后开启私人 FM",
                "猜你喜欢和实时推荐需要登录状态",
                "按 F5 扫码登录 · 按 F6 短信登录",
                accent,
                theme);
            return;
        }

        if (state.IsLoading && song == null)
        {
            RenderCenteredMessage(
                canvas,
                body,
                "正在为你更新专属推荐...",
                $"{TerminalPersonalFmPresentation.GetTitle(state.SelectedMode)} · {TerminalPersonalFmPresentation.GetSongPoolLabel(state.SelectedSongPoolId)}",
                "请稍候",
                accent,
                theme);
            return;
        }

        if (song == null)
        {
            RenderCenteredMessage(
                canvas,
                body,
                "暂时没有新的私人 FM 推荐",
                "可以切换频道或选曲偏好后再试",
                "按 r 换一批 · 按 m 切频道 · 按 v 切口味/风格",
                accent,
                theme);
            return;
        }

        var cardWidth = Math.Min(body.W, 74);
        var card = new Rect(body.X + Math.Max(0, (body.W - cardWidth) / 2), body.Y, cardWidth, body.H);

        if (card.H >= 19)
        {
            RenderVerticalMobileLayout(canvas, card, state, song, cover, hitRegions, accent, theme);
        }
        else
        {
            RenderCompactSplitLayout(canvas, card, state, song, cover, hitRegions, accent, theme);
        }
    }

    private static void RenderTopSelectorBar(
        Canvas canvas,
        Rect inner,
        PersonalFmViewState state,
        PersonalFmHitRegions hitRegions,
        Rgb accent,
        Theme theme)
    {
        var x = inner.X;
        var y = inner.Y;

        // 左侧频道模式标签：红心 / 小众 / 速览
        hitRegions.ModeNormalRect = DrawChip(canvas, x, y, "红心", state.SelectedMode == TerminalPersonalFmMode.Normal, accent, theme);
        x = hitRegions.ModeNormalRect.Right + 1;
        hitRegions.ModeSmallRect = DrawChip(canvas, x, y, "小众", state.SelectedMode == TerminalPersonalFmMode.Small, accent, theme);
        x = hitRegions.ModeSmallRect.Right + 1;
        hitRegions.ModePeakRect = DrawChip(canvas, x, y, "速览", state.SelectedMode == TerminalPersonalFmMode.Peak, accent, theme);

        // 右侧选曲池标签：根据口味 / 根据风格
        var styleLabel = "根据风格";
        var tasteLabel = "根据口味";
        var styleW = TextWidth.Of(styleLabel) + 4;
        var tasteW = TextWidth.Of(tasteLabel) + 4;
        var rightStart = inner.Right - styleW - 1 - tasteW;
        if (rightStart > hitRegions.ModePeakRect.Right + 2)
        {
            hitRegions.PoolTasteRect = DrawChip(canvas, rightStart, y, tasteLabel,
                state.SelectedSongPoolId == TerminalPersonalFmSongPoolId.Taste, accent, theme);
            hitRegions.PoolStyleRect = DrawChip(canvas, hitRegions.PoolTasteRect.Right + 1, y, styleLabel,
                state.SelectedSongPoolId == TerminalPersonalFmSongPoolId.Style, accent, theme);
        }

        // 第二行：频道副标题 + 当前状态
        var subtitle = TerminalPersonalFmPresentation.GetSubtitle(state.SelectedMode);
        var status = state.IsLoading
            ? "正在调频..."
            : state.IsFmActive
                ? (state.IsPlaying ? $"● 实时推荐中 · {state.Quality}" : $"⏸ 已暂停 · {state.Quality}")
                : "○ 预览就绪 · Enter/Space 开始";
        var statusW = TextWidth.Of(status);
        var leftMax = Math.Max(8, inner.W - statusW - 2);
        canvas.Text(inner.X, y + 1, subtitle, theme.MutedStyle, leftMax);
        canvas.Text(inner.Right - statusW, y + 1, status,
            state.IsFmActive && state.IsPlaying ? new CellStyle(accent) : theme.FaintStyle, statusW);
    }

    private static void RenderVerticalMobileLayout(
        Canvas canvas,
        Rect card,
        PersonalFmViewState state,
        TerminalSongItem song,
        TerminalImage? cover,
        PersonalFmHitRegions hitRegions,
        Rgb accent,
        Theme theme)
    {
        var y = card.Y;
        var footerReserved = 5; // 进度条(1) + 空行(1) + 控制栏(1) + 空行/下一首提示(2)
        var titleRows = 3;
        var availableForArtAndLyrics = Math.Max(6, card.H - footerReserved - titleRows);
        var artRows = Math.Clamp(availableForArtAndLyrics * 55 / 100, 5, 11);

        // 1. 居中封面
        var artBox = new Rect(card.X, y, card.W, artRows);
        DrawCoverArt(canvas, artBox, cover, song, accent, theme);
        y += artRows + 1;

        // 2. 歌曲标题 + 歌手 + 红心标记
        var heart = song.IsFavorite ? " ♥" : " ♡";
        var heartStyle = song.IsFavorite ? new CellStyle(new Rgb(255, 95, 125), Attr: TextAttr.Bold) : theme.MutedStyle;
        var titleMaxW = Math.Max(6, card.W - 3);
        var clippedTitle = TextWidth.Truncate(song.Name, titleMaxW);
        var titleSpanW = TextWidth.Of(clippedTitle) + TextWidth.Of(heart);
        var titleX = card.X + Math.Max(0, (card.W - titleSpanW) / 2);
        canvas.Spans(titleX, y, card.W,
            (clippedTitle, new CellStyle(accent, Attr: TextAttr.Bold)),
            (heart, heartStyle));
        y++;

        var singerLine = string.IsNullOrWhiteSpace(song.AlbumName)
            ? song.Singer
            : $"{song.Singer} · {song.AlbumName}";
        canvas.TextAligned(card, y, singerLine, theme.MutedStyle, 1);
        y += 2;

        // 3. 逐字歌词预览区（位于封面/歌名与底部控制栏之间）
        var footerY = card.Bottom - footerReserved;
        var lyricHeight = footerY - y - 1;
        if (lyricHeight >= 3)
        {
            var lyricArea = new Rect(card.X, y, card.W, lyricHeight);
            if (state.IsFmActive)
            {
                VisualizerView.RenderLyrics(canvas, lyricArea, state.PlaybackSnapshot, accent, theme);
            }
            else
            {
                canvas.TextAligned(lyricArea, lyricArea.Y + lyricArea.H / 2,
                    "按 Enter 或 Space 开始播放当前私人 FM 频道", theme.MutedStyle, 1);
            }
        }

        // 4. 进度条 + 控制按钮 + 下一首预告
        RenderBottomPlayerControls(canvas, card, footerY, state, song, hitRegions, accent, theme);
    }

    private static void RenderCompactSplitLayout(
        Canvas canvas,
        Rect card,
        PersonalFmViewState state,
        TerminalSongItem song,
        TerminalImage? cover,
        PersonalFmHitRegions hitRegions,
        Rgb accent,
        Theme theme)
    {
        var footerReserved = 4;
        var upper = card.SkipBottom(footerReserved);
        var artColW = Math.Clamp(upper.H * 2 + 4, 16, Math.Max(16, upper.W * 2 / 5));
        var leftCol = upper.TakeLeft(artColW);
        var rightCol = upper.SkipLeft(artColW + 2);

        var artRows = Math.Max(4, leftCol.H - 2);
        DrawCoverArt(canvas, new Rect(leftCol.X, leftCol.Y, leftCol.W, artRows), cover, song, accent, theme);
        canvas.TextAligned(leftCol, leftCol.Y + artRows, song.Name, new CellStyle(accent, Attr: TextAttr.Bold), 1);
        canvas.TextAligned(leftCol, leftCol.Y + artRows + 1, song.Singer, theme.MutedStyle, 1);

        if (!rightCol.IsEmpty && rightCol.H >= 3)
        {
            if (state.IsFmActive)
            {
                VisualizerView.RenderLyrics(canvas, rightCol, state.PlaybackSnapshot, accent, theme);
            }
            else
            {
                canvas.TextAligned(rightCol, rightCol.Y + rightCol.H / 2,
                    "按 Enter 或 Space 开始播放私人 FM", theme.MutedStyle, 1);
            }
        }

        RenderBottomPlayerControls(canvas, card, card.Bottom - footerReserved, state, song, hitRegions, accent, theme);
    }

    private static void RenderBottomPlayerControls(
        Canvas canvas,
        Rect card,
        int startY,
        PersonalFmViewState state,
        TerminalSongItem song,
        PersonalFmHitRegions hitRegions,
        Rgb accent,
        Theme theme)
    {
        var y = startY;
        if (y >= card.Bottom)
        {
            return;
        }

        // 进度条
        var position = state.IsFmActive ? state.PlaybackSnapshot.Position : TimeSpan.Zero;
        var duration = state.IsFmActive && state.PlaybackSnapshot.Duration > TimeSpan.Zero
            ? state.PlaybackSnapshot.Duration
            : TimeSpan.FromSeconds(Math.Max(0, song.DurationSeconds));
        VisualizerView.DrawProgressLine(canvas, card.X, y, card.W, position, duration, accent, theme);

        var posLen = VisualizerView.FormatTime(position.TotalSeconds).Length;
        var durLen = VisualizerView.FormatTime(duration.TotalSeconds).Length;
        var barStart = card.X + posLen + 2;
        var barWidth = Math.Max(0, card.W - posLen - durLen - 4);
        hitRegions.ProgressBarRect = new Rect(barStart, y, barWidth, 1);
        hitRegions.ProgressBarStartX = barStart;
        hitRegions.ProgressBarWidth = barWidth;

        y += 2;
        if (y >= card.Bottom)
        {
            return;
        }

        // 控制按钮栏
        var playLabel = state.IsFmActive && state.IsPlaying ? "⏸ 暂停 (Space)" : "▶ 播放 (Space)";
        var favLabel = song.IsFavorite ? "♥ 已红心 (c)" : "♡ 红心 (c)";

        var buttons = new (string Label, bool Primary, bool Enabled, Action<Rect> Assign)[]
        {
            ("⏮ 上一首 (b)", false, !state.IsFmActive || state.HasHistory, r => hitRegions.PreviousButtonRect = r),
            ("✕ 不喜欢 (d)", false, state.IsFmActive, r => hitRegions.DislikeButtonRect = r),
            (playLabel, true, true, r => hitRegions.PlayPauseButtonRect = r),
            ("⏭ 下一首 (n)", false, true, r => hitRegions.NextButtonRect = r),
            (favLabel, false, true, r => hitRegions.FavoriteButtonRect = r),
            ("↻ 换一批 (r)", false, !state.IsLoading, r => hitRegions.RefreshButtonRect = r)
        };

        // 如果宽度不够，省略快捷键提示文字只保留图标+短词
        var totalWidth = buttons.Sum(b => TextWidth.Of($"[{b.Label}]") + 1) - 1;
        if (totalWidth > card.W)
        {
            buttons =
            [
                ("⏮ b", false, !state.IsFmActive || state.HasHistory, r => hitRegions.PreviousButtonRect = r),
                ("✕ d", false, state.IsFmActive, r => hitRegions.DislikeButtonRect = r),
                (state.IsFmActive && state.IsPlaying ? "⏸ Space" : "▶ Space", true, true, r => hitRegions.PlayPauseButtonRect = r),
                ("⏭ n", false, true, r => hitRegions.NextButtonRect = r),
                (song.IsFavorite ? "♥ c" : "♡ c", false, true, r => hitRegions.FavoriteButtonRect = r),
                ("↻ r", false, !state.IsLoading, r => hitRegions.RefreshButtonRect = r)
            ];
            totalWidth = buttons.Sum(b => TextWidth.Of($"[{b.Label}]") + 1) - 1;
        }

        var btnX = card.X + Math.Max(0, (card.W - totalWidth) / 2);
        foreach (var (label, primary, enabled, assign) in buttons)
        {
            var text = $"[{label}]";
            var w = TextWidth.Of(text);
            if (btnX + w > card.Right)
            {
                break;
            }

            var style = primary
                ? new CellStyle(theme.PanelBg, accent, TextAttr.Bold)
                : enabled
                    ? new CellStyle(theme.Text)
                    : theme.FaintStyle;
            canvas.Text(btnX, y, text, style, w);
            assign(new Rect(btnX, y, w, 1));
            btnX += w + 1;
        }

        y += 2;
        if (y < card.Bottom)
        {
            var nextText = state.UpcomingSongs.Count > 0
                ? $"接下来: {string.Join(" · ", state.UpcomingSongs.Take(2).Select(s => $"{s.Name} - {s.Singer}"))}"
                : "m 切频道 · v 口味/风格 · f 加歌单 · g 歌手主页 · i 沉浸频谱";
            canvas.TextAligned(card, y, nextText, theme.FaintStyle, 1);
        }
    }

    private static void DrawCoverArt(
        Canvas canvas,
        Rect artBox,
        TerminalImage? cover,
        TerminalSongItem song,
        Rgb accent,
        Theme theme)
    {
        if (cover != null && canvas.ImageMode != TerminalImageMode.Off)
        {
            canvas.Image("fm-cover", artBox, cover);
            return;
        }

        var frame = Canvas.FitImage(artBox, 1.0);
        if (frame.IsEmpty)
        {
            return;
        }

        canvas.Box(frame, new CellStyle(accent));
        var midY = frame.Y + frame.H / 2;
        if (frame.H >= 5)
        {
            canvas.TextAligned(frame, midY - 1, "♪ RADIO FM ♪", new CellStyle(accent, Attr: TextAttr.Bold), 1);
            canvas.TextAligned(frame, midY + 1, song.Name, theme.MutedStyle, 1);
        }
        else
        {
            canvas.TextAligned(frame, midY, "♪ FM ♪", new CellStyle(accent, Attr: TextAttr.Bold), 1);
        }
    }

    private static Rect DrawChip(
        Canvas canvas,
        int x,
        int y,
        string label,
        bool active,
        Rgb accent,
        Theme theme)
    {
        var text = active ? $"[{label}]" : $" {label} ";
        var w = TextWidth.Of(text);
        var style = active
            ? new CellStyle(theme.PanelBg, accent, TextAttr.Bold)
            : theme.MutedStyle;
        canvas.Text(x, y, text, style, w);
        return new Rect(x, y, w, 1);
    }

    private static void RenderCenteredMessage(
        Canvas canvas,
        Rect area,
        string title,
        string subtitle,
        string hint,
        Rgb accent,
        Theme theme)
    {
        var midY = area.Y + Math.Max(0, area.H / 2 - 1);
        canvas.TextAligned(area, midY, title, new CellStyle(accent, Attr: TextAttr.Bold), 1);
        if (midY + 1 < area.Bottom)
        {
            canvas.TextAligned(area, midY + 1, subtitle, theme.MutedStyle, 1);
        }

        if (midY + 3 < area.Bottom)
        {
            canvas.TextAligned(area, midY + 3, hint, theme.FaintStyle, 1);
        }
    }
}
