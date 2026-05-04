using System;
using System.Collections.Generic;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Services;

public sealed class NavigationService : INavigationService
{
    private readonly Stack<PageViewModelBase> _stack = new();

    public PageViewModelBase? CurrentPage => _stack.Count > 0 ? _stack.Peek() : null;

    public bool CanGoBack => _stack.Count > 1;

    public event Action<PageViewModelBase?>? CurrentPageChanged;

    public void ReplaceRoot(PageViewModelBase page)
    {
        _stack.Clear();
        _stack.Push(page);
        CurrentPageChanged?.Invoke(CurrentPage);
    }

    public void Push(PageViewModelBase page)
    {
        if (CurrentPage == page)
            return;

        _stack.Push(page);
        CurrentPageChanged?.Invoke(CurrentPage);
    }

    public bool TryGoBack()
    {
        if (!CanGoBack)
            return false;

        _stack.Pop();
        CurrentPageChanged?.Invoke(CurrentPage);
        return true;
    }
}