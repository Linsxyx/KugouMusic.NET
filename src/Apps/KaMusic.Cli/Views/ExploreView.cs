using KaMusic.Cli.Graphics;
using KaMusic.Cli.Models;
using KaMusic.Cli.Tui;

namespace KaMusic.Cli.Views;

// 探索发现页（ExploreView）：整合官方推荐歌曲卡、分类歌单广场与巅峰榜单墙，提供二维卡片网格与交互钻取体验。

internal sealed class ExploreViewState
{
    public TerminalExploreTab ActiveTab { get; set; } = TerminalExploreTab.Overview;
    public int SelectedCardIndex { get; set; }
    public int SelectedTagIndex { get; set; }
    public int LastGridColumns { get; set; } = 2;

    public IReadOnlyList<TerminalExploreCard> TopCards { get; set; } = [];
    public IReadOnlyList<TerminalExploreCard> PlaylistCards { get; set; } = [];
    public IReadOnlyList<TerminalExploreCard> FeaturedRankCards { get; set; } = [];
    public IReadOnlyList<TerminalExploreCard> AllRankCards { get; set; } = [];
    public IReadOnlyList<TerminalPlaylistTagItem> Tags { get; set; } =
    [
        new() { Id = 0, CategoryId = 0, Name = "全部推荐", GroupName = "精选" }
    ];

    public List<(Rect Rect, TerminalExploreTab Tab)> TabHitBoxes { get; } = [];
    public List<(Rect Rect, int TagIndex)> TagHitBoxes { get; } = [];
    public List<(Rect Rect, int CardIndex)> CardHitBoxes { get; } = [];

    // 根据当前激活的子栏目聚合需要展示的卡片集合。
    public IReadOnlyList<TerminalExploreCard> GetActiveCards()
    {
        return ActiveTab switch
        {
            TerminalExploreTab.TopCards => TopCards,
            TerminalExploreTab.Playlists => PlaylistCards,
            TerminalExploreTab.Ranks => AllRankCards.Count > 0 ? AllRankCards : FeaturedRankCards,
            _ => BuildOverviewCards()
        };
    }

    private IReadOnlyList<TerminalExploreCard> BuildOverviewCards()
    {
        var list = new List<TerminalExploreCard>(TopCards.Count + 6 + 12);
        list.AddRange(TopCards);
        list.AddRange(FeaturedRankCards.Take(6));
        list.AddRange(PlaylistCards.Take(12));

        if (AllRankCards.Count > 6)
        {
            list.Add(new TerminalExploreCard
            {
                Id = "action:ranks",
                Title = "查看全部巅峰榜单 →",
                Subtitle = $"共 {AllRankCards.Count} 个官方分类榜单",
                Badge = "榜单墙",
                SectionTitle = "快捷入口",
                Kind = TerminalExploreCardKind.Action,
                TargetTab = TerminalExploreTab.Ranks,
                PreviewLines = ["星耀榜 · 地区榜 · 特色榜", "全球榜 · 曲风榜 · 实时热歌"]
            });
        }

        if (PlaylistCards.Count > 0)
        {
            list.Add(new TerminalExploreCard
            {
                Id = "action:playlists",
                Title = "进入分类歌单广场 →",
                Subtitle = $"支持 {Math.Max(1, Tags.Count)} 个主题标签筛选",
                Badge = "歌单广场",
                SectionTitle = "快捷入口",
                Kind = TerminalExploreCardKind.Action,
                TargetTab = TerminalExploreTab.Playlists,
                PreviewLines = ["按语种、风格、场景、心情筛选", "发现更多宝藏歌单"]
            });
        }

        return list;
    }
}

internal static class ExploreView
{
    private static readonly (TerminalExploreTab Tab, string Label)[] Tabs =
    [
        (TerminalExploreTab.Overview, "✦ 综合探索"),
        (TerminalExploreTab.TopCards, "♪ 专属推荐"),
        (TerminalExploreTab.Playlists, "❖ 歌单广场"),
        (TerminalExploreTab.Ranks, "♛ 巅峰榜单")
    ];

    // 渲染探索发现页面：包含顶部栏目切换栏、可选标签筛选条、焦点卡片横幅与自适应二维卡片网格。
    public static void Render(
        Canvas canvas,
        Rect area,
        ExploreViewState state,
        bool focused,
        bool isLoading,
        ImageCache images,
        Theme theme)
    {
        state.TabHitBoxes.Clear();
        state.TagHitBoxes.Clear();
        state.CardHitBoxes.Clear();

        var border = focused ? new CellStyle(theme.Accent) : theme.BorderStyle;
        var activeCards = state.GetActiveCards();
        var clampedSelection = activeCards.Count == 0
            ? 0
            : Math.Clamp(state.SelectedCardIndex, 0, activeCards.Count - 1);
        state.SelectedCardIndex = clampedSelection;

        var subtitle = state.ActiveTab switch
        {
            TerminalExploreTab.TopCards => $"官方推荐卡片 ({activeCards.Count})",
            TerminalExploreTab.Playlists => state.SelectedTagIndex >= 0 && state.SelectedTagIndex < state.Tags.Count
                ? $"歌单广场 · {state.Tags[state.SelectedTagIndex].Name} ({activeCards.Count})"
                : $"歌单广场 ({activeCards.Count})",
            TerminalExploreTab.Ranks => $"巅峰榜单墙 ({activeCards.Count})",
            _ => $"精选推荐 · 歌单 · 榜单 ({activeCards.Count})"
        };

        canvas.Box(area, border, $"探索发现 · {subtitle}", focused ? theme.AccentBold : theme.Bold);
        var inner = area.Inset(2, 1);
        if (inner.H < 4 || inner.W < 20)
        {
            return;
        }

        // 1. 顶部子栏目导航条
        RenderTabBar(canvas, inner.TakeTop(1), state, theme);
        canvas.HLine(inner.X, inner.Y + 1, inner.W, theme.FaintStyle);
        var body = inner.SkipTop(2);

        // 2. 歌单广场模式下展示分类标签条
        if (state.ActiveTab == TerminalExploreTab.Playlists && state.Tags.Count > 0 && body.H >= 8)
        {
            RenderTagStrip(canvas, body.TakeTop(1), state, theme);
            canvas.HLine(body.X, body.Y + 1, body.W, theme.FaintStyle);
            body = body.SkipTop(2);
        }

        if (isLoading && activeCards.Count == 0)
        {
            canvas.TextAligned(body, body.Y + body.H / 2, "正在加载探索发现卡片...", theme.AccentStyle, 1);
            return;
        }

        if (activeCards.Count == 0)
        {
            canvas.TextAligned(body, body.Y + body.H / 2, "暂无探索卡片数据（按 r 重新加载）", theme.MutedStyle, 1);
            return;
        }

        // 3. 若高度充足，在上方展示当前焦点卡片的聚光灯详情横幅
        var selectedCard = activeCards[clampedSelection];
        if (body.H >= 15)
        {
            var spotlightRect = body.TakeTop(5);
            RenderSpotlightBanner(canvas, spotlightRect, selectedCard, images, theme);
            canvas.HLine(body.X, spotlightRect.Bottom, body.W, theme.FaintStyle);
            body = body.SkipTop(6);
        }

        // 4. 计算自适应二维卡片网格列数与滚动窗口
        var columns = body.W switch
        {
            >= 96 => 3,
            >= 50 => 2,
            _ => 1
        };
        state.LastGridColumns = columns;

        const int cardHeight = 6;
        var visibleRows = Math.Max(1, body.H / cardHeight);
        var totalRows = (activeCards.Count + columns - 1) / columns;
        var selectedRow = clampedSelection / columns;
        var startRow = ComputeRowScrollStart(selectedRow, totalRows, visibleRows);

        var gapX = columns > 1 ? 1 : 0;
        var cardWidth = Math.Max(16, (body.W - gapX * (columns - 1)) / columns);

        for (var visRow = 0; visRow < visibleRows; visRow++)
        {
            var rowIndex = startRow + visRow;
            if (rowIndex >= totalRows)
            {
                break;
            }

            var cardY = body.Y + visRow * cardHeight;
            var actualH = Math.Min(cardHeight, body.Bottom - cardY);
            if (actualH < 4)
            {
                break;
            }

            for (var col = 0; col < columns; col++)
            {
                var cardIndex = rowIndex * columns + col;
                if (cardIndex >= activeCards.Count)
                {
                    break;
                }

                var cardX = body.X + col * (cardWidth + gapX);
                var w = col == columns - 1 ? Math.Max(cardWidth, body.Right - cardX) : cardWidth;
                var cardRect = new Rect(cardX, cardY, w, actualH);
                var isSelected = cardIndex == clampedSelection;

                RenderCard(canvas, cardRect, activeCards[cardIndex], cardIndex, isSelected, focused, images, theme);
                state.CardHitBoxes.Add((cardRect, cardIndex));
            }
        }
    }

    private static void RenderTabBar(Canvas canvas, Rect row, ExploreViewState state, Theme theme)
    {
        var x = row.X;
        foreach (var (tab, label) in Tabs)
        {
            var active = state.ActiveTab == tab;
            var text = $" {label} ";
            var w = TextWidth.Of(text);
            if (x + w > row.Right)
            {
                break;
            }

            var pillRect = new Rect(x, row.Y, w, 1);
            var style = active
                ? new CellStyle(theme.Text, theme.SelectionBg, TextAttr.Bold)
                : new CellStyle(theme.Muted);
            if (active)
            {
                canvas.Fill(pillRect, style);
            }

            canvas.Text(x, row.Y, text, style, w);
            state.TabHitBoxes.Add((pillRect, tab));
            x += w + 1;
        }

        var hint = "[ / ] 切栏目  ←↑↓→ 选卡片  Enter 进入";
        var hintW = TextWidth.Of(hint);
        if (row.Right - x >= hintW + 2)
        {
            canvas.Text(row.Right - hintW, row.Y, hint, theme.FaintStyle, hintW);
        }
    }

    private static void RenderTagStrip(Canvas canvas, Rect row, ExploreViewState state, Theme theme)
    {
        var x = row.X;
        canvas.Text(x, row.Y, "标签(,/.):", theme.FaintStyle, 10);
        x += 11;

        var maxTags = state.Tags.Count;
        var selected = Math.Clamp(state.SelectedTagIndex, 0, Math.Max(0, maxTags - 1));
        var startIdx = Math.Max(0, selected - 3);

        for (var i = startIdx; i < maxTags; i++)
        {
            var tag = state.Tags[i];
            var active = i == selected;
            var text = active ? $"[{tag.Name}]" : $" {tag.Name} ";
            var w = TextWidth.Of(text);
            if (x + w > row.Right)
            {
                break;
            }

            var tagRect = new Rect(x, row.Y, w, 1);
            var style = active
                ? new CellStyle(theme.Accent, theme.SelectionDimBg, TextAttr.Bold)
                : theme.MutedStyle;
            if (active)
            {
                canvas.Fill(tagRect, style);
            }

            canvas.Text(x, row.Y, text, style, w);
            state.TagHitBoxes.Add((tagRect, i));
            x += w + 1;
        }
    }

    private static void RenderSpotlightBanner(
        Canvas canvas,
        Rect area,
        TerminalExploreCard card,
        ImageCache images,
        Theme theme)
    {
        var infoX = area.X;
        if (canvas.ImageMode != TerminalImageMode.Off && area.W >= 36)
        {
            var artBox = new Rect(area.X, area.Y, 10, 5);
            var cover = images.Get(card.Cover);
            if (cover != null)
            {
                canvas.Image($"explore-spotlight-{card.Id}", artBox, cover);
            }
            else
            {
                canvas.Box(artBox, theme.FaintStyle);
                canvas.TextAligned(artBox, artBox.Y + 2, KindIcon(card.Kind), theme.AccentBold, 1);
            }

            infoX = artBox.Right + 2;
        }

        var infoW = Math.Max(0, area.Right - infoX);
        if (infoW <= 4)
        {
            return;
        }

        var badge = string.IsNullOrWhiteSpace(card.Badge) ? card.SectionTitle : card.Badge;
        canvas.Spans(infoX, area.Y, infoW,
            ($"[{badge}] ", theme.AccentBold),
            (card.Title, theme.Bold));

        canvas.Text(infoX, area.Y + 1, card.Subtitle, theme.MutedStyle, infoW);

        var preview1 = card.PreviewLines.Count > 0 ? card.PreviewLines[0] : "";
        var preview2 = card.PreviewLines.Count > 1 ? card.PreviewLines[1] : "";
        var previewJoined = !string.IsNullOrWhiteSpace(preview2) ? $"{preview1}   {preview2}" : preview1;
        if (!string.IsNullOrWhiteSpace(previewJoined))
        {
            canvas.Text(infoX, area.Y + 2, previewJoined, theme.TextStyle, infoW);
        }

        var preview3 = card.PreviewLines.Count > 2 ? card.PreviewLines[2] : "";
        if (!string.IsNullOrWhiteSpace(preview3))
        {
            canvas.Text(infoX, area.Y + 3, preview3, theme.MutedStyle, infoW);
        }

        var actionHint = card.Kind == TerminalExploreCardKind.Action
            ? "按 Enter 切换到对应专区"
            : "按 Enter 打开卡片详情 · 双击卡片进入歌单";
        canvas.Text(infoX, area.Y + 4, actionHint, theme.AccentStyle, infoW);
    }

    // 绘制单张探索发现卡片（包含圆角边框、类型角标、可选缩略图、标题与曲目预览）。
    private static void RenderCard(
        Canvas canvas,
        Rect rect,
        TerminalExploreCard card,
        int cardIndex,
        bool selected,
        bool focused,
        ImageCache images,
        Theme theme)
    {
        var bg = selected ? focused ? theme.SelectionBg : theme.SelectionDimBg : (Rgb?)null;
        if (bg.HasValue)
        {
            canvas.Fill(rect.Inset(1, 1), new CellStyle(Bg: bg));
        }

        var borderStyle = selected
            ? new CellStyle(focused ? theme.Accent : theme.Text, Attr: TextAttr.Bold)
            : new CellStyle(theme.Border);
        var titleTag = $"{KindIcon(card.Kind)} {card.SectionTitle}";
        var titleStyle = selected ? theme.AccentBold : theme.MutedStyle;
        canvas.Box(rect, borderStyle, titleTag, titleStyle);

        var inner = rect.Inset(1, 1);
        if (inner.IsEmpty)
        {
            return;
        }

        var textX = inner.X + 1;
        if (canvas.ImageMode != TerminalImageMode.Off && inner.W >= 30 && inner.H >= 4 && !string.IsNullOrWhiteSpace(card.Cover))
        {
            var thumbRect = new Rect(inner.X, inner.Y, 8, Math.Min(4, inner.H));
            var cover = images.Get(card.Cover);
            if (cover != null)
            {
                canvas.Image($"explore-card-{cardIndex}", thumbRect, cover);
                textX = thumbRect.Right + 1;
            }
        }

        var textW = Math.Max(0, inner.Right - textX);
        if (textW <= 2)
        {
            return;
        }

        var primaryStyle = new CellStyle(theme.Text, bg, selected ? TextAttr.Bold : TextAttr.None);
        var subStyle = new CellStyle(theme.Muted, bg);
        var badgeStyle = new CellStyle(selected ? theme.Accent : theme.Warning, bg, TextAttr.Bold);

        // 第 1 行：卡片主标题 + 右侧角标
        var prefix = selected ? "▸ " : "";
        canvas.Text(textX, inner.Y, prefix + card.Title, primaryStyle, textW);
        if (!string.IsNullOrWhiteSpace(card.Badge) && textW >= 18)
        {
            var badgeText = $" {card.Badge}";
            var badgeW = TextWidth.Of(badgeText);
            if (badgeW < textW / 2)
            {
                canvas.Text(inner.Right - badgeW, inner.Y, badgeText, badgeStyle, badgeW);
            }
        }

        // 第 2 行：副标题 / 描述
        if (inner.H >= 2)
        {
            canvas.Text(textX, inner.Y + 1, card.Subtitle, subStyle, textW);
        }

        // 第 3~4 行：预览歌曲列表或说明
        for (var i = 0; i < Math.Min(2, card.PreviewLines.Count) && i + 2 < inner.H; i++)
        {
            var lineStyle = new CellStyle(i == 0 && selected ? theme.Text : theme.Faint, bg);
            canvas.Text(textX, inner.Y + 2 + i, card.PreviewLines[i], lineStyle, textW);
        }
    }

    public static int MoveCardSelection(int currentIndex, int dx, int dy, int count, int columns)
    {
        if (count <= 0)
        {
            return 0;
        }

        var cols = Math.Max(1, columns);
        var current = Math.Clamp(currentIndex, 0, count - 1);
        if (dx != 0)
        {
            return Math.Clamp(current + dx, 0, count - 1);
        }

        if (dy != 0)
        {
            var target = current + dy * cols;
            if (target < 0)
            {
                return current;
            }

            return Math.Min(count - 1, target);
        }

        return current;
    }

    private static string KindIcon(TerminalExploreCardKind kind) => kind switch
    {
        TerminalExploreCardKind.TopCard => "✦",
        TerminalExploreCardKind.Playlist => "❖",
        TerminalExploreCardKind.Rank => "♛",
        _ => "➜"
    };

    private static int ComputeRowScrollStart(int selectedRow, int totalRows, int visibleRows)
    {
        if (totalRows <= visibleRows)
        {
            return 0;
        }

        var half = visibleRows / 2;
        return Math.Clamp(selectedRow - half, 0, totalRows - visibleRows);
    }
}
