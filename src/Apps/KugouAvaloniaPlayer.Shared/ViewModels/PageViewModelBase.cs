using CommunityToolkit.Mvvm.ComponentModel;

namespace KugouAvaloniaPlayer.ViewModels;

public abstract class PageViewModelBase : ObservableObject
{
    public abstract string DisplayName { get; }
    public abstract string Icon { get; }
}