using System;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Services;

public interface INavigationService
{
    PageViewModelBase? CurrentPage { get; }
    bool CanGoBack { get; }

    event Action<PageViewModelBase?>? CurrentPageChanged;

    void ReplaceRoot(PageViewModelBase page);
    void Push(PageViewModelBase page);
    bool TryGoBack();
}