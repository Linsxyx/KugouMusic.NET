using System.Collections.Concurrent;

namespace KgTest.Tui;

// 单线程 UI 调度器：await 之后的续体都回到主循环线程执行，UI 状态无需加锁。

internal sealed class UiDispatcher : SynchronizationContext
{
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
    private readonly AutoResetEvent _signal = new(false);
    private readonly int _threadId = Environment.CurrentManagedThreadId;

    public event Action<Exception>? UnhandledException;

    public override void Post(SendOrPostCallback d, object? state)
    {
        _queue.Enqueue((d, state));
        _signal.Set();
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        if (Environment.CurrentManagedThreadId == _threadId)
        {
            d(state);
            return;
        }

        using var done = new ManualResetEventSlim(false);
        Post(_ =>
        {
            try
            {
                d(state);
            }
            finally
            {
                done.Set();
            }
        }, null);
        done.Wait();
    }

    public override SynchronizationContext CreateCopy() => this;

    public void Post(Action action) => Post(_ => action(), null);

    /// <summary>
    ///     唤醒主循环（例如后台图片加载完成后请求重绘）。
    /// </summary>
    public void Wake() => _signal.Set();

    /// <summary>
    ///     执行当前队列中的全部回调，返回是否执行过任何回调。
    /// </summary>
    public bool Drain()
    {
        var any = false;
        while (_queue.TryDequeue(out var item))
        {
            any = true;
            try
            {
                item.Callback(item.State);
            }
            catch (Exception ex)
            {
                UnhandledException?.Invoke(ex);
            }
        }

        return any;
    }

    public void Wait(int milliseconds) => _signal.WaitOne(milliseconds);
}
