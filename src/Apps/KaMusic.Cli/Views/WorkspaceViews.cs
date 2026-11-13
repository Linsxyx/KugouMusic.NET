using KaMusic.Cli.Graphics;
using KaMusic.Cli.Models;
using KaMusic.Cli.Services;
using KaMusic.Cli.Tui;

namespace KaMusic.Cli.Views;

// 主界面四栏与覆盖层的绘制逻辑（导航栏、内容区、右侧正在播放、底部进度栏、搜索框与帮助弹窗）。

internal static class WorkspaceViews
{
    public static void RenderNavigation(
        Canvas canvas,
        Rect area,
        IReadOnlyList<TerminalPage> pages,
        TerminalPage currentPage,
        TerminalUserProfile profile,
        TerminalUpdateService updater,
        bool focused,
        Theme theme)
    {
        var border = focused ? new CellStyle(theme.Accent) : theme.BorderStyle;
        canvas.Box(area, border, "导航", focused ? theme.AccentBold : theme.MutedStyle);
        var inner = area.Inset(1, 1);
        if (inner.IsEmpty)
        {
            return;
        }

        var y = inner.Y;
        canvas.Spans(inner.X + 1, y++, inner.W - 2,
            ("♪ KA Music ", theme.AccentBold),
            ($"v{updater.CurrentVersion}", theme.FaintStyle));

        var userIcon = profile.IsLoggedIn ? "● " : "○ ";
        var userColor = profile.IsLoggedIn ? theme.Playing : theme.Warning;
        canvas.Spans(inner.X + 1, y++, inner.W - 2,
            (userIcon, new CellStyle(userColor)),
            (profile.Name, theme.MutedStyle));
        if (updater.HasUpdate && !string.IsNullOrEmpty(updater.LatestVersion))
        {
            canvas.Text(inner.X + 1, y++, $"↑ 新版 v{updater.LatestVersion} (按U)",
                new CellStyle(theme.Warning, Attr: TextAttr.Bold), inner.W - 2);
        }
        else
        {
            y++;
        }

        for (var i = 0; i < pages.Count && y < inner.Bottom; i++)
        {
            var page = pages[i];
            var active = page == currentPage;
            var rowRect = new Rect(inner.X, y, inner.W, 1);
            var bg = active ? focused ? theme.SelectionBg : theme.SelectionDimBg : (Rgb?)null;
            var style = active
                ? new CellStyle(theme.Text, bg, TextAttr.Bold)
                : theme.TextStyle;
            var numStyle = active
                ? new CellStyle(theme.Accent, bg, TextAttr.Bold)
                : theme.FaintStyle;

            if (bg.HasValue)
            {
                canvas.Fill(rowRect, style);
            }

            canvas.Spans(inner.X + 1, y, inner.W - 2,
                ($"{i + 1} ", numStyle),
                (PageTitle(page), style));
            y++;
        }

        var footerY = inner.Bottom - 5;
        if (footerY > y)
        {
            canvas.HLine(inner.X + 1, footerY++, inner.W - 2, theme.FaintStyle);
            canvas.Text(inner.X + 1, footerY++, "/ 搜索   i 沉浸页", theme.FaintStyle, inner.W - 2);
            canvas.Text(inner.X + 1, footerY++, "Space 暂停  n/b 切歌", theme.FaintStyle, inner.W - 2);
            canvas.Text(inner.X + 1, footerY++, "f 加歌单 a 下一首", theme.FaintStyle, inner.W - 2);
            canvas.Text(inner.X + 1, footerY++, "g 歌手   p 图片协议", theme.FaintStyle, inner.W - 2);
            canvas.Text(inner.X + 1, footerY, "q / Esc 返回或退出", theme.FaintStyle, inner.W - 2);
        }
    }

    public static void RenderSongs(
        Canvas canvas,
        Rect area,
        string title,
        string? subtitle,
        IReadOnlyList<TerminalSongItem> songs,
        int selectedIndex,
        bool focused,
        bool isLoading,
        Theme theme)
    {
        var border = focused ? new CellStyle(theme.Accent) : theme.BorderStyle;
        var header = string.IsNullOrWhiteSpace(subtitle) ? title : $"{title} · {subtitle}";
        canvas.Box(area, border, header, focused ? theme.AccentBold : theme.Bold);
        var inner = area.Inset(2, 1);
        if (inner.H < 2 || inner.W < 16)
        {
            return;
        }

        if (isLoading)
        {
            canvas.TextAligned(inner, inner.Y + inner.H / 2, "正在加载...", theme.AccentStyle, 1);
            return;
        }

        RenderSongsTable(canvas, inner, songs, selectedIndex, focused, theme);
    }

    public static void RenderPlaylists(
        Canvas canvas,
        Rect area,
        string title,
        string? subtitle,
        IReadOnlyList<TerminalPlaylistItem> playlists,
        int selectedIndex,
        bool focused,
        bool isLoading,
        ImageCache images,
        Theme theme,
        string? emptyMessage = null)
    {
        var border = focused ? new CellStyle(theme.Accent) : theme.BorderStyle;
        var header = string.IsNullOrWhiteSpace(subtitle) ? title : $"{title} · {subtitle}";
        canvas.Box(area, border, header, focused ? theme.AccentBold : theme.Bold);
        var inner = area.Inset(2, 1);
        if (inner.H < 2 || inner.W < 16)
        {
            return;
        }

        // 若选中项有封面且空间足够，在顶部展示歌单详情与封面预览。
        var listArea = inner;
        if (selectedIndex >= 0 && selectedIndex < playlists.Count && inner.H >= 14 &&
            canvas.ImageMode != TerminalImageMode.Off)
        {
            var current = playlists[selectedIndex];
            var preview = inner.TakeTop(5);
            var artBox = new Rect(preview.X, preview.Y, 10, 5);
            var cover = images.Get(current.Cover);
            if (cover != null)
            {
                canvas.Image("playlist-preview", artBox, cover);
            }
            else
            {
                canvas.Box(artBox, theme.FaintStyle);
                canvas.TextAligned(artBox, artBox.Y + 2, "COVER", theme.FaintStyle, 1);
            }

            var infoX = artBox.Right + 2;
            var infoW = Math.Max(0, preview.Right - infoX);
            canvas.Text(infoX, preview.Y + 1, current.Name, theme.Bold, infoW);
            canvas.Text(infoX, preview.Y + 2, current.Subtitle, theme.MutedStyle, infoW);
            var actionText = current.Kind == TerminalPlaylistKind.Local
                ? $"共 {current.Count} 首 · Enter 打开 · o 导入音频 · c 新建歌单 · x 删除"
                : current.Count > 0 ? $"共 {current.Count} 首 · 按 Enter 打开" : "按 Enter 打开歌单";
            canvas.Text(infoX, preview.Y + 3, actionText, theme.AccentStyle, infoW);
            canvas.HLine(inner.X, preview.Bottom, inner.W, theme.FaintStyle);
            listArea = inner.SkipTop(6);
        }

        var numW = 4;
        var countW = 7;
        var rest = Math.Max(10, listArea.W - numW - countW - 3);
        var subW = Math.Clamp(rest / 3, 12, 26);
        var nameW = Math.Max(10, rest - subW);

        var x = listArea.X;
        canvas.Text(x, listArea.Y, "#", theme.MutedStyle, numW);
        x += numW + 1;
        canvas.Text(x, listArea.Y, "歌单 / 榜单", theme.MutedStyle, nameW);
        x += nameW + 1;
        canvas.Text(x, listArea.Y, "说明", theme.MutedStyle, subW);
        x += subW + 1;
        canvas.Text(x, listArea.Y, "曲目", theme.MutedStyle, countW);
        canvas.HLine(listArea.X, listArea.Y + 1, listArea.W, theme.FaintStyle);

        var rowsArea = listArea.SkipTop(2);
        if (isLoading)
        {
            canvas.TextAligned(rowsArea, rowsArea.Y + rowsArea.H / 2, "正在加载...", theme.AccentStyle, 1);
            return;
        }

        if (playlists.Count == 0)
        {
            canvas.TextAligned(
                rowsArea,
                rowsArea.Y + rowsArea.H / 2,
                string.IsNullOrWhiteSpace(emptyMessage) ? "暂无歌单" : emptyMessage,
                theme.MutedStyle,
                1);
            return;
        }

        var visible = Math.Max(1, rowsArea.H);
        var start = ComputeScrollStart(selectedIndex, playlists.Count, visible);
        for (var row = 0; row < visible && start + row < playlists.Count; row++)
        {
            var idx = start + row;
            var item = playlists[idx];
            var selected = idx == selectedIndex;
            var y = rowsArea.Y + row;
            var bg = selected ? focused ? theme.SelectionBg : theme.SelectionDimBg : (Rgb?)null;
            if (bg.HasValue)
            {
                canvas.Fill(new Rect(rowsArea.X - 1, y, rowsArea.W + 2, 1), new CellStyle(Bg: bg));
            }

            var primary = new CellStyle(theme.Text, bg, selected ? TextAttr.Bold : TextAttr.None);
            var secondary = new CellStyle(theme.Muted, bg);
            var col = rowsArea.X;
            canvas.Text(col, y, selected ? "▸" : $"{idx + 1}", primary, numW);
            col += numW + 1;
            canvas.Text(col, y, item.Name, primary, nameW);
            col += nameW + 1;
            canvas.Text(col, y, item.Subtitle, secondary, subW);
            col += subW + 1;
            canvas.Text(col, y, item.Count > 0 ? item.Count.ToString() : "-", secondary, countW);
        }
    }

    public static void RenderSingerPage(
        Canvas canvas,
        Rect area,
        TerminalSingerDetail singer,
        int selectedIndex,
        bool focused,
        bool isLoading,
        ImageCache images,
        Theme theme)
    {
        var border = focused ? new CellStyle(theme.Accent) : theme.BorderStyle;
        var header = $"歌手 · {singer.Name}";
        canvas.Box(area, border, header, focused ? theme.AccentBold : theme.Bold);
        var inner = area.Inset(2, 1);
        if (inner.H < 3 || inner.W < 16)
        {
            return;
        }

        var y = inner.Y;

        var tab1Active = singer.Tab == TerminalSingerTab.HotSongs;
        var tab2Active = singer.Tab == TerminalSingerTab.NewSongs;
        var tab3Active = singer.Tab == TerminalSingerTab.Albums;

        var tab1Style = tab1Active ? new CellStyle(theme.Text, theme.SelectionBg, TextAttr.Bold) : theme.MutedStyle;
        var tab2Style = tab2Active ? new CellStyle(theme.Text, theme.SelectionBg, TextAttr.Bold) : theme.MutedStyle;
        var tab3Style = tab3Active ? new CellStyle(theme.Text, theme.SelectionBg, TextAttr.Bold) : theme.MutedStyle;

        var tabX = inner.X;
        canvas.Text(tabX, y, " [热门单曲] ", tab1Style, 12);
        tabX += 13;
        canvas.Text(tabX, y, " [最新单曲] ", tab2Style, 12);
        tabX += 13;
        canvas.Text(tabX, y, " [歌手专辑] ", tab3Style, 12);
        tabX += 14;

        if (inner.W > 62)
        {
            var hint = "s 切换热门/最新 · [ / ] 切换标签 · Enter 播放/查看 · Backspace 返回";
            canvas.Text(tabX, y, hint, theme.FaintStyle, inner.W - tabX + inner.X);
        }

        y++;
        canvas.HLine(inner.X, y++, inner.W, theme.FaintStyle);

        var contentArea = new Rect(inner.X, y, inner.W, inner.Bottom - y);
        if (isLoading)
        {
            canvas.TextAligned(contentArea, contentArea.Y + contentArea.H / 2, "正在加载...", theme.AccentStyle, 1);
            return;
        }

        if (singer.Tab == TerminalSingerTab.Albums)
        {
            RenderSingerAlbums(canvas, contentArea, singer.Albums, selectedIndex, focused, theme);
        }
        else
        {
            var songs = singer.CurrentSongs;
            RenderSongsTable(canvas, contentArea, songs, selectedIndex, focused, theme);
        }
    }

    private static void RenderSingerAlbums(
        Canvas canvas,
        Rect area,
        IReadOnlyList<TerminalSingerAlbumItem> albums,
        int selectedIndex,
        bool focused,
        Theme theme)
    {
        if (area.H < 2 || area.W < 16)
        {
            return;
        }

        if (albums.Count == 0)
        {
            canvas.TextAligned(area, area.Y + area.H / 2, "暂无专辑", theme.MutedStyle, 1);
            return;
        }

        var numW = 4;
        var dateW = 12;
        var rest = Math.Max(10, area.W - numW - dateW - 2);
        var nameW = rest;

        var x = area.X;
        canvas.Text(x, area.Y, "#", theme.MutedStyle, numW);
        x += numW + 1;
        canvas.Text(x, area.Y, "专辑名称", theme.MutedStyle, nameW);
        x += nameW + 1;
        canvas.Text(x, area.Y, "发行时间", theme.MutedStyle, dateW);

        canvas.HLine(area.X, area.Y + 1, area.W, theme.FaintStyle);
        var listArea = area.SkipTop(2);
        var visible = Math.Max(1, listArea.H);
        var start = ComputeScrollStart(selectedIndex, albums.Count, visible);

        for (var row = 0; row < visible && start + row < albums.Count; row++)
        {
            var idx = start + row;
            var album = albums[idx];
            var selected = idx == selectedIndex;
            var y = listArea.Y + row;
            var bg = selected ? focused ? theme.SelectionBg : theme.SelectionDimBg : (Rgb?)null;

            if (bg.HasValue)
            {
                canvas.Fill(new Rect(area.X - 1, y, area.W + 2, 1), new CellStyle(Bg: bg));
            }

            var primary = new CellStyle(theme.Text, bg, selected ? TextAttr.Bold : TextAttr.None);
            var secondary = new CellStyle(theme.Muted, bg);

            var col = area.X;
            canvas.Text(col, y, selected ? "▸" : $"{idx + 1}", primary, numW);
            col += numW + 1;
            canvas.Text(col, y, album.Name, primary, nameW);
            col += nameW + 1;
            canvas.Text(col, y, string.IsNullOrWhiteSpace(album.PublishDate) ? "-" : album.PublishDate, secondary, dateW);
        }
    }

    public static void RenderSongsTable(
        Canvas canvas,
        Rect area,
        IReadOnlyList<TerminalSongItem> songs,
        int selectedIndex,
        bool focused,
        Theme theme)
    {
        if (area.H < 2 || area.W < 16)
        {
            return;
        }

        var numW = 4;
        var timeW = 7;
        var sourceW = area.W >= 62 ? 10 : 0;
        var rest = Math.Max(10, area.W - numW - timeW - sourceW - 4);
        var singerW = Math.Clamp(rest / 3, 10, 24);
        var nameW = Math.Max(8, rest - singerW);

        var x = area.X;
        canvas.Text(x, area.Y, "#", theme.MutedStyle, numW);
        x += numW + 1;
        canvas.Text(x, area.Y, "歌曲", theme.MutedStyle, nameW);
        x += nameW + 1;
        canvas.Text(x, area.Y, "歌手", theme.MutedStyle, singerW);
        x += singerW + 1;
        canvas.Text(x, area.Y, "时长", theme.MutedStyle, timeW);
        if (sourceW > 0)
        {
            x += timeW + 1;
            canvas.Text(x, area.Y, "来源", theme.MutedStyle, sourceW);
        }

        canvas.HLine(area.X, area.Y + 1, area.W, theme.FaintStyle);

        var listArea = area.SkipTop(2);
        if (songs.Count == 0)
        {
            canvas.TextAligned(listArea, listArea.Y + listArea.H / 2, "暂无歌曲", theme.MutedStyle, 1);
            return;
        }

        var visible = Math.Max(1, listArea.H);
        var start = ComputeScrollStart(selectedIndex, songs.Count, visible);
        for (var row = 0; row < visible && start + row < songs.Count; row++)
        {
            var idx = start + row;
            var song = songs[idx];
            var selected = idx == selectedIndex;
            var y = listArea.Y + row;
            var bg = selected ? focused ? theme.SelectionBg : theme.SelectionDimBg : (Rgb?)null;

            if (bg.HasValue)
            {
                canvas.Fill(new Rect(area.X - 1, y, area.W + 2, 1), new CellStyle(Bg: bg));
            }

            var fg = song.IsPlaying ? theme.Playing : theme.Text;
            var primary = new CellStyle(fg, bg, selected || song.IsPlaying ? TextAttr.Bold : TextAttr.None);
            var secondary = new CellStyle(song.IsPlaying ? theme.Playing : theme.Muted, bg);
            var prefix = song.IsPlaying ? "♪" : selected ? "▸" : $"{idx + 1}";

            var col = area.X;
            canvas.Text(col, y, prefix, primary, numW);
            col += numW + 1;
            canvas.Text(col, y, song.Name, primary, nameW);
            col += nameW + 1;
            canvas.Text(col, y, song.Singer, secondary, singerW);
            col += singerW + 1;
            canvas.Text(col, y, VisualizerView.FormatTime(song.DurationSeconds), secondary, timeW);
            if (sourceW > 0)
            {
                col += timeW + 1;
                canvas.Text(col, y, song.Source, new CellStyle(theme.Faint, bg), sourceW);
            }
        }
    }

    public static void RenderSingerPickerModal(
        Canvas canvas,
        IReadOnlyList<(long Id, string Name)> singers,
        int selectedIndex,
        Theme theme)
    {
        var h = Math.Min(singers.Count + 4, 14);
        var modal = canvas.Bounds.Center(Math.Min(50, canvas.Width - 6), h);
        if (modal.IsEmpty)
            return;

        canvas.ClearImages(modal);
        canvas.Fill(modal, new CellStyle(theme.Text, theme.PanelBg));
        canvas.Box(modal, new CellStyle(theme.Accent, theme.PanelBg), "选择歌手 (Enter 确认 · Esc 取消)",
            new CellStyle(theme.Accent, theme.PanelBg, TextAttr.Bold));

        var inner = modal.Inset(2, 1);
        var start = ComputeScrollStart(selectedIndex, singers.Count, inner.H);
        for (var i = 0; i < inner.H && start + i < singers.Count; i++)
        {
            var idx = start + i;
            var singer = singers[idx];
            var selected = idx == selectedIndex;
            var y = inner.Y + i;
            var bg = selected ? theme.SelectionBg : (Rgb?)null;
            if (bg.HasValue)
            {
                canvas.Fill(new Rect(inner.X, y, inner.W, 1), new CellStyle(Bg: bg));
            }
            var style = new CellStyle(theme.Text, bg, selected ? TextAttr.Bold : TextAttr.None);
            canvas.Text(inner.X + 1, y, $"{idx + 1}. {singer.Name}", style, inner.W - 2);
        }
    }

    // 渲染「添加到歌单」选择弹窗，支持展示自建歌单与「我喜欢」，并实时高亮选择项。
    public static void RenderPlaylistPickerModal(
        Canvas canvas,
        string songName,
        IReadOnlyList<TerminalPlaylistItem> playlists,
        int selectedIndex,
        Theme theme)
    {
        var h = Math.Min(playlists.Count + 4, 16);
        var modal = canvas.Bounds.Center(Math.Min(60, canvas.Width - 6), h);
        if (modal.IsEmpty)
            return;

        canvas.ClearImages(modal);
        canvas.Fill(modal, new CellStyle(theme.Text, theme.PanelBg));
        canvas.Box(modal, new CellStyle(theme.Accent, theme.PanelBg), $"添加到歌单: 《{songName}》 (Enter 确认 · Esc 取消)",
            new CellStyle(theme.Accent, theme.PanelBg, TextAttr.Bold));

        var inner = modal.Inset(2, 1);
        var start = ComputeScrollStart(selectedIndex, playlists.Count, inner.H);
        for (var i = 0; i < inner.H && start + i < playlists.Count; i++)
        {
            var idx = start + i;
            var playlist = playlists[idx];
            var selected = idx == selectedIndex;
            var y = inner.Y + i;
            var bg = selected ? theme.SelectionBg : (Rgb?)null;
            if (bg.HasValue)
            {
                canvas.Fill(new Rect(inner.X, y, inner.W, 1), new CellStyle(Bg: bg));
            }

            var countText = playlist.Count > 0 ? $"({playlist.Count}首)" : "";
            var textStyle = new CellStyle(theme.Text, bg, selected ? TextAttr.Bold : TextAttr.None);

            canvas.Text(inner.X + 1, y, $"{idx + 1}. ", textStyle, 4);
            canvas.Text(inner.X + 5, y, playlist.Name, textStyle, inner.W - 16);
            if (!string.IsNullOrEmpty(countText))
            {
                canvas.TextAligned(new Rect(inner.X, y, inner.W - 2, 1), y, countText, new CellStyle(theme.Muted, bg), 2);
            }
        }
    }

    public static void RenderSettings(
        Canvas canvas,
        Rect area,
        TerminalAppSettings settings,
        TerminalImageMode effectiveImageMode,
        TerminalPlaybackService playback,
        TerminalUpdateService updater,
        int selectedIndex,
        bool focused,
        Theme theme)
    {
        var border = focused ? new CellStyle(theme.Accent) : theme.BorderStyle;
        canvas.Box(area, border, "设置 · ↑/↓ 选择 · ←/→ 或 Enter 调整", focused ? theme.AccentBold : theme.Bold);
        var inner = area.Inset(2, 1);
        if (inner.IsEmpty)
        {
            return;
        }

        var items = new (string Label, string Value, string Hint)[]
        {
            ("音质选择", playback.MusicQuality, "128 / 320 / flac / hires"),
            ("环绕音效", settings.EnableSurround ? "开启" : "关闭", "立体声空间增强"),
            ("智能无缝过渡", settings.EnableSeamlessTransition ? "开启" : "关闭", "自动分析拍点与交叉混音"),
            ("随机播放", settings.Shuffle ? "开启" : "关闭", "快捷键 s"),
            ("歌词显示模式", settings.LyricMode.ToString(), "原文 / 翻译 / 罗马音 / 组合（快捷键 l）"),
            ("终端图片协议",
                settings.ImageMode == TerminalImageMode.Auto
                    ? $"自动（当前：{TerminalCapabilities.DisplayName(effectiveImageMode)}）"
                    : TerminalCapabilities.DisplayName(settings.ImageMode),
                "Auto / Kitty / iTerm2 / Sixel / 半块字符 / 关闭（快捷键 p）"),
            ("版本与更新", updater.DisplayValue, updater.HintText)
        };

        var y = inner.Y;
        for (var i = 0; i < items.Length && y < inner.Bottom; i++)
        {
            var selected = i == selectedIndex;
            var bg = selected ? focused ? theme.SelectionBg : theme.SelectionDimBg : (Rgb?)null;
            if (bg.HasValue)
            {
                canvas.Fill(new Rect(inner.X, y, inner.W, 1), new CellStyle(Bg: bg));
            }

            var (label, value, hint) = items[i];
            canvas.Text(inner.X + 1, y, label,
                new CellStyle(theme.Text, bg, selected ? TextAttr.Bold : TextAttr.None), 18);
            canvas.Text(inner.X + 20, y, $"‹ {value} ›", new CellStyle(theme.Accent, bg, TextAttr.Bold), 28);
            if (inner.W > 52)
            {
                canvas.Text(inner.X + 49, y, hint, new CellStyle(theme.Muted, bg), inner.W - 50);
            }

            y++;
        }

        y++;
        if (y < inner.Bottom)
        {
            canvas.Text(inner.X + 1, y++, "10 段均衡器 (EQ) · ←/→ 或 -/+ 调节增益 (-12dB ~ +12dB)", theme.MutedStyle, inner.W - 2);
        }

        string[] eqFreqs = ["31Hz", "62Hz", "125Hz", "250Hz", "500Hz", "1kHz", "2kHz", "4kHz", "8kHz", "16kHz"];
        for (var i = 0; i < playback.EqGains.Length && y < inner.Bottom; i++)
        {
            var rowIndex = items.Length + i;
            var selected = rowIndex == selectedIndex;
            var bg = selected ? focused ? theme.SelectionBg : theme.SelectionDimBg : (Rgb?)null;
            if (bg.HasValue)
            {
                canvas.Fill(new Rect(inner.X, y, inner.W, 1), new CellStyle(Bg: bg));
            }

            var gain = playback.EqGains[i];
            canvas.Text(inner.X + 1, y, $"EQ {i + 1,2} ({eqFreqs[i],5})",
                new CellStyle(theme.Text, bg, selected ? TextAttr.Bold : TextAttr.None), 18);
            DrawEqSlider(canvas, inner.X + 20, y, Math.Clamp(inner.W - 32, 12, 28), gain, bg, theme);
            canvas.Text(inner.X + 20 + Math.Clamp(inner.W - 32, 12, 28) + 2, y, $"{gain:+0.0;-0.0;0.0} dB",
                new CellStyle(theme.Accent, bg), 10);
            y++;
        }
    }

    public static void RenderNowPlayingSide(
        Canvas canvas,
        Rect area,
        PlaybackStateSnapshot snapshot,
        string quality,
        ImageCache images,
        Theme theme)
    {
        canvas.Box(area, theme.BorderStyle, "正在播放", theme.MutedStyle);
        var inner = area.Inset(2, 1);
        if (inner.IsEmpty)
        {
            return;
        }

        var song = snapshot.CurrentSong;
        var cover = images.Get(song?.Cover);
        var accent = cover?.Accent ?? theme.Accent;
        var y = inner.Y;

        if (canvas.ImageMode != TerminalImageMode.Off && inner.H >= 15)
        {
            var artRows = Math.Clamp((inner.H - 11) / 2, 5, 11);
            var artBox = new Rect(inner.X, y, inner.W, artRows);
            if (cover != null)
            {
                canvas.Image("side-cover", artBox, cover);
            }
            else
            {
                var frame = Canvas.FitImage(artBox, 1.0, canvas.CellAspect);
                canvas.Box(frame, theme.FaintStyle);
                canvas.TextAligned(frame, frame.Y + frame.H / 2, song == null ? "NO TRACK" : "LOADING",
                    theme.FaintStyle, 1);
            }

            y += artRows + 1;
        }

        if (song == null)
        {
            canvas.TextAligned(inner, y, "尚未播放歌曲", theme.MutedStyle, 1);
            y += 2;
        }
        else
        {
            canvas.TextAligned(inner, y++, song.Name, new CellStyle(accent, Attr: TextAttr.Bold), 1);
            canvas.TextAligned(inner, y++, song.Singer, theme.MutedStyle, 1);
            y++;
        }

        // 歌词窗口
        var metaRows = 4;
        var lyricHeight = Math.Max(0, inner.Bottom - y - metaRows);
        if (lyricHeight >= 3)
        {
            var lyricArea = new Rect(inner.X, y, inner.W, lyricHeight);
            VisualizerView.RenderLyrics(canvas, lyricArea, snapshot, accent, theme);
            y += lyricHeight;
        }

        if (inner.Bottom - y >= 3)
        {
            canvas.HLine(inner.X, inner.Bottom - 3, inner.W, theme.FaintStyle);
            canvas.Text(inner.X, inner.Bottom - 2,
                $"音质 {quality}  音量 {(int)Math.Round(snapshot.Volume * 100)}%  队列 {snapshot.Queue.Count}",
                theme.MutedStyle, inner.W);
            var flags =
                $"随机 {(snapshot.IsShuffle ? "开" : "关")} · 环绕 {(snapshot.EnableSurround ? "开" : "关")} · 过渡 {(snapshot.EnableSeamlessTransition ? "开" : "关")}";
            canvas.Text(inner.X, inner.Bottom - 1, flags, theme.FaintStyle, inner.W);
        }
    }

    public static void RenderPlaybackBar(
        Canvas canvas,
        Rect area,
        PlaybackStateSnapshot snapshot,
        string playbackStatus,
        string pageStatus,
        TerminalImageMode effectiveImageMode,
        Theme theme)
    {
        canvas.Box(area, theme.BorderStyle);
        var inner = area.Inset(2, 1);
        if (inner.IsEmpty)
        {
            return;
        }

        var stateIcon = snapshot.IsPlaying ? "▶ 播放" : snapshot.IsPaused ? "⏸ 暂停" : "⏹ 停止";
        var stateColor = snapshot.IsPlaying ? theme.Playing : snapshot.IsPaused ? theme.Warning : theme.Muted;
        var songText = snapshot.CurrentSong == null
            ? "未选择曲目"
            : $"{snapshot.CurrentSong.Name} - {snapshot.CurrentSong.Singer}";

        var leftW = Math.Clamp(inner.W / 4, 18, 36);
        canvas.Spans(inner.X, inner.Y, leftW,
            (stateIcon, new CellStyle(stateColor, Attr: TextAttr.Bold)),
            ("  ", theme.TextStyle),
            (songText, theme.Bold));

        var progressX = inner.X + leftW + 2;
        var progressW = Math.Max(12, inner.Right - progressX);
        VisualizerView.DrawProgressLine(canvas, progressX, inner.Y, progressW, snapshot.Position, snapshot.Duration,
            theme.Accent, theme);

        if (inner.H >= 2)
        {
            var status = $"{playbackStatus} · {pageStatus}";
            var rightTag = $"图片: {TerminalCapabilities.DisplayName(effectiveImageMode)} (p)  ? 帮助";
            var rightW = TextWidth.Of(rightTag);
            canvas.Text(inner.X, inner.Y + 1, status, theme.MutedStyle, Math.Max(10, inner.W - rightW - 2));
            canvas.Text(inner.Right - rightW, inner.Y + 1, rightTag, theme.FaintStyle, rightW);
        }
    }

    public static void RenderSearchModal(Canvas canvas, TextField input, Theme theme)
    {
        var modal = canvas.Bounds.Center(Math.Min(64, canvas.Width - 6), 5);
        if (modal.IsEmpty)
        {
            return;
        }

        canvas.ClearImages(modal);
        canvas.Fill(modal, new CellStyle(theme.Text, theme.PanelBg));
        canvas.Box(modal, new CellStyle(theme.Accent, theme.PanelBg), "搜索歌曲与歌单",
            new CellStyle(theme.Accent, theme.PanelBg, TextAttr.Bold));
        input.Render(canvas, modal.X + 2, modal.Y + 2, modal.W - 4, true, theme);
        canvas.Text(modal.X + 2, modal.Y + 3, "Enter 搜索   Esc 取消   在搜索结果页按 v 切换歌曲/歌单",
            new CellStyle(theme.Muted, theme.PanelBg), modal.W - 4);
    }

    public static void RenderLocalInputModal(Canvas canvas, string title, string hint, TextField input, Theme theme)
    {
        var modal = canvas.Bounds.Center(Math.Min(70, canvas.Width - 6), 6);
        if (modal.IsEmpty)
        {
            return;
        }

        canvas.ClearImages(modal);
        canvas.Fill(modal, new CellStyle(theme.Text, theme.PanelBg));
        canvas.Box(modal, new CellStyle(theme.Accent, theme.PanelBg), title,
            new CellStyle(theme.Accent, theme.PanelBg, TextAttr.Bold));
        input.Render(canvas, modal.X + 2, modal.Y + 2, modal.W - 4, true, theme);
        canvas.Text(modal.X + 2, modal.Y + 4, hint,
            new CellStyle(theme.Muted, theme.PanelBg), modal.W - 4);
    }

    public static void RenderLoginRequiredModal(Canvas canvas, string? songName, Theme theme)
    {
        var modal = canvas.Bounds.Center(Math.Min(58, canvas.Width - 6), 8);
        if (modal.IsEmpty)
        {
            return;
        }

        canvas.ClearImages(modal);
        canvas.Fill(modal, new CellStyle(theme.Text, theme.PanelBg));
        canvas.Box(modal, new CellStyle(theme.Warning, theme.PanelBg), "请先登录",
            new CellStyle(theme.Warning, theme.PanelBg, TextAttr.Bold));

        var prompt = string.IsNullOrWhiteSpace(songName)
            ? "需要登录酷狗账号后才能播放在线音乐"
            : $"需要先登录账号才能播放《{songName}》";
        canvas.TextAligned(modal, modal.Y + 2, prompt,
            new CellStyle(theme.Text, theme.PanelBg, TextAttr.Bold), 1);
        canvas.TextAligned(modal, modal.Y + 3, "（本地音乐页面中的本地音频文件无需登录即可直接播放）",
            new CellStyle(theme.Muted, theme.PanelBg), 1);

        var btnY = modal.Y + 5;
        var centerX = modal.X + modal.W / 2;
        canvas.Text(centerX - 24, btnY, " 扫码登录 (Enter/F5) ",
            new CellStyle(theme.Text, theme.SelectionBg, TextAttr.Bold), 21);
        canvas.Text(centerX - 1, btnY, " 短信登录 (F6) ",
            new CellStyle(theme.Accent, theme.InputBg, TextAttr.Bold), 15);
        canvas.Text(centerX + 16, btnY, " 取消 (Esc) ",
            new CellStyle(theme.Muted, theme.InputBg), 12);
    }

    public static void RenderHelpModal(Canvas canvas, Theme theme)
    {
        var modal = canvas.Bounds.Center(Math.Min(76, canvas.Width - 4), Math.Min(24, canvas.Height - 2));
        if (modal.IsEmpty)
        {
            return;
        }

        canvas.ClearImages(modal);
        canvas.Fill(modal, new CellStyle(theme.Text, theme.PanelBg));
        canvas.Box(modal, new CellStyle(theme.Accent, theme.PanelBg), "快捷键说明（按任意键关闭）",
            new CellStyle(theme.Accent, theme.PanelBg, TextAttr.Bold));

        (string Key, string Desc)[] rows =
        [
            ("↑ / ↓ / ← / → (h/j/k/l)", "列表上下选择；探索发现页中二维移动卡片焦点"),
            ("[ / ] 与 , / .", "探索发现页或歌手页切换标签栏；非探索页快退/快进"),
            ("PgUp / PgDn / Home / End", "翻页 / 跳到首尾"),
            ("Tab", "切换焦点区域（导航栏 ↔ 内容区）"),
            ("1 - 9", "直接切换到对应页面（3 探索发现 / 4 本地音乐）"),
            ("Enter", "播放歌曲 / 打开探索卡片或歌单 / 打开歌手专辑"),
            ("Backspace / Esc", "从歌手主页、歌单或卡片详情返回上一层 / 关闭弹窗"),
            ("o / c / x（本地音乐页）", "导入本地文件夹或音频路径 / 新建本地歌单 / 删除歌单或曲目"),
            ("m / v / d / c（私人 FM）", "切换频道（红心/小众/速览）/ 口味与风格 / 不喜欢 / 红心收藏"),
            ("Space", "播放 / 暂停"),
            ("n / b", "下一首 / 上一首"),
            ("- / +", "调整音量（设置页中调整 EQ 增益）"),
            ("a / x", "将当前或选中歌曲插入下一首播放 / 从队列移除"),
            ("f", "添加到歌单（弹出自建歌单与“我喜欢”列表）"),
            ("g", "跳转当前或选中歌曲的歌手主页（热门/最新/专辑）"),
            ("s", "歌手页切换热门/最新排序；其他页面切换随机播放"),
            ("v", "在搜索页切换歌曲/歌单结果；在歌手页切换单曲/专辑"),
            ("i", "进入 / 退出沉浸式播放页（大封面 + 逐字歌词 + 频谱）"),
            ("p", "循环切换终端图片协议（Kitty / iTerm2 / Sixel / 半块 / 关）"),
            ("t / l", "智能过渡 / 歌词模式切换"),
            ("F5 / F6 / L", "扫码登录 / 短信登录 / 退出登录"),
            ("u / U", "检查 npm 更新 / 发现新版本时一键更新重启"),
            ("/   r   q", "搜索 / 刷新当前页 / 退出程序")
        ];

        var inner = modal.Inset(3, 2);
        for (var i = 0; i < rows.Length && i < inner.H; i++)
        {
            canvas.Text(inner.X, inner.Y + i, rows[i].Key,
                new CellStyle(theme.Accent, theme.PanelBg, TextAttr.Bold), 26);
            canvas.Text(inner.X + 27, inner.Y + i, rows[i].Desc,
                new CellStyle(theme.Text, theme.PanelBg), inner.W - 27);
        }
    }

    public static void RenderExitConfirmModal(Canvas canvas, bool confirmSelected, Theme theme)
    {
        var modal = canvas.Bounds.Center(Math.Min(46, canvas.Width - 6), 7);
        if (modal.IsEmpty)
        {
            return;
        }

        canvas.ClearImages(modal);
        canvas.Fill(modal, new CellStyle(theme.Text, theme.PanelBg));
        canvas.Box(modal, new CellStyle(theme.Warning, theme.PanelBg), "确认退出",
            new CellStyle(theme.Warning, theme.PanelBg, TextAttr.Bold));

        canvas.TextAligned(modal, modal.Y + 2, "确定要退出 Kugou TUI 播放器吗？",
            new CellStyle(theme.Text, theme.PanelBg, TextAttr.Bold), 1);

        var yesStyle = confirmSelected
            ? new CellStyle(theme.Text, theme.SelectionBg, TextAttr.Bold)
            : new CellStyle(theme.Muted, theme.InputBg);
        var noStyle = !confirmSelected
            ? new CellStyle(theme.Text, theme.SelectionBg, TextAttr.Bold)
            : new CellStyle(theme.Muted, theme.InputBg);

        var btnY = modal.Y + 4;
        var centerX = modal.X + modal.W / 2;
        canvas.Text(centerX - 16, btnY, "  确认退出 (q/Enter)  ", yesStyle, 22);
        canvas.Text(centerX + 4, btnY, "  取消 (Esc)  ", noStyle, 14);
    }

    public static string PageTitle(TerminalPage page) => page switch
    {
        TerminalPage.Daily => "每日推荐",
        TerminalPage.PersonalFm => "私人 FM",
        TerminalPage.Discover => "探索发现",
        TerminalPage.LocalMusic => "本地音乐",
        TerminalPage.Search => "搜索",
        TerminalPage.MyPlaylists => "我的歌单",
        TerminalPage.Queue => "播放队列",
        TerminalPage.Settings => "设置",
        TerminalPage.Account => "账号与登录",
        _ => page.ToString()
    };

    private static void DrawEqSlider(Canvas canvas, int x, int y, int width, float gain, Rgb? bg, Theme theme)
    {
        var center = width / 2;
        var pos = (int)Math.Round((gain + 12f) / 24f * (width - 1));
        pos = Math.Clamp(pos, 0, width - 1);
        for (var i = 0; i < width; i++)
        {
            if (i == pos)
            {
                canvas.Set(x + i, y, '●', new CellStyle(theme.Accent, bg, TextAttr.Bold));
            }
            else if (i == center)
            {
                canvas.Set(x + i, y, '┼', new CellStyle(theme.Muted, bg));
            }
            else
            {
                canvas.Set(x + i, y, '─', new CellStyle(theme.Faint, bg));
            }
        }
    }

    public static int ComputeScrollStart(int selectedIndex, int total, int visible)
    {
        if (total <= visible)
        {
            return 0;
        }

        var half = visible / 2;
        return Math.Clamp(selectedIndex - half, 0, total - visible);
    }
}
