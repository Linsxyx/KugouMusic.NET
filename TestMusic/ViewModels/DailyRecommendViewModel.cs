using System.Collections.ObjectModel;

namespace TestMusic.ViewModels;

public class DailyRecommendViewModel : PageViewModelBase
{
    public override string DisplayName => "每日推荐";
    public override string Icon => "/Assets/Radio.svg";

    public ObservableCollection<SongItem> Songs { get; } = new();
}