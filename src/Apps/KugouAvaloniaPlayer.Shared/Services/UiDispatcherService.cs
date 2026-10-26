using System;
using Avalonia.Threading;

namespace KugouAvaloniaPlayer.Services;

public interface IUiDispatcherService
{
    Dispatcher Dispatcher { get; }
    bool CheckAccess();
    void RunOrPost(Action action);
    void RunOrPost(Action action, DispatcherPriority priority);
}

public sealed class UiDispatcherService(Dispatcher dispatcher) : IUiDispatcherService
{
    public Dispatcher Dispatcher { get; } = dispatcher;

    public bool CheckAccess()
    {
        return Dispatcher.CheckAccess();
    }

    public void RunOrPost(Action action)
    {
        RunOrPost(action, DispatcherPriority.Normal);
    }

    public void RunOrPost(Action action, DispatcherPriority priority)
    {
        if (Dispatcher.CheckAccess())
            action();
        else
            Dispatcher.Post(action, priority);
    }
}
