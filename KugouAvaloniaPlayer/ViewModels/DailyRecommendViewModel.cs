using Avalonia.Collections;

namespace KugouAvaloniaPlayer.ViewModels;

public class DailyRecommendViewModel : PageViewModelBase
{
    public override string DisplayName => "每日推荐";
    public override string Icon => "/Assets/Radio.svg";

    public AvaloniaList<SongItem> Songs { get; } = new();
}