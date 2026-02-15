using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TestMusic.ViewModels;

// 1. 基类：添加 SukiSideMenu 需要的 DisplayName 和 Icon
public abstract class PageViewModelBase : ObservableObject
{
    public abstract string DisplayName { get; }
    public abstract string Icon { get; }
}

public class DailyRecommendViewModel : PageViewModelBase
{
    public override string DisplayName => "每日推荐";
    public override string Icon => "/Assets/Radio.svg";

    public ObservableCollection<SongItem> Songs { get; } = new();
}