using System;
using System.Collections.Generic;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Services;

public sealed class NavigationService : INavigationService
{
    private const int MaxHistoryDepth = 4;
    private readonly Stack<NavigationEntry> _stack = new();

    public PageViewModelBase? CurrentPage => _stack.Count > 0 ? _stack.Peek().Page : null;

    public bool CanGoBack => _stack.Count > 1;

    public event Action<PageViewModelBase?>? CurrentPageChanged;

    public void NavigateRoot(PageViewModelBase page)
    {
        DisposeEntries(_stack);
        _stack.Push(new NavigationEntry(page, false));
        CurrentPageChanged?.Invoke(CurrentPage);
    }

    public void Navigate(PageViewModelBase page)
    {
        Navigate(page, disposeOnRemoval: false);
    }

    public void NavigateTransient(PageViewModelBase page)
    {
        Navigate(page, disposeOnRemoval: true);
    }

    private void Navigate(PageViewModelBase page, bool disposeOnRemoval)
    {
        if (CurrentPage == page)
            return;

        _stack.Push(new NavigationEntry(page, disposeOnRemoval));
        TrimHistory();
        CurrentPageChanged?.Invoke(CurrentPage);
    }

    public bool GoBack()
    {
        if (!CanGoBack)
            return false;

        DisposeEntry(_stack.Pop());
        CurrentPageChanged?.Invoke(CurrentPage);
        return true;
    }

    private void TrimHistory()
    {
        if (_stack.Count <= MaxHistoryDepth)
            return;

        var newestToOldest = _stack.ToArray();
        _stack.Clear();

        for (var i = MaxHistoryDepth - 1; i >= 0; i--)
            _stack.Push(newestToOldest[i]);

        for (var i = MaxHistoryDepth; i < newestToOldest.Length; i++)
            DisposeEntry(newestToOldest[i]);
    }

    private static void DisposeEntries(Stack<NavigationEntry> entries)
    {
        while (entries.Count > 0)
            DisposeEntry(entries.Pop());
    }

    private static void DisposeEntry(NavigationEntry entry)
    {
        if (entry.DisposeOnRemoval)
            (entry.Page as IDisposable)?.Dispose();
    }

    private sealed record NavigationEntry(PageViewModelBase Page, bool DisposeOnRemoval);
}
