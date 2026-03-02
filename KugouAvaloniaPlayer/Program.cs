using System;
using System.Threading;
using Avalonia;

namespace KugouAvaloniaPlayer;

internal sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    private static Mutex? _mutex;
    [STAThread]
    public static void Main(string[] args)
    {
        _mutex = new Mutex(true, "KugouAvaloniaPlayer", out var createdNew);
        if (!createdNew) return;
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}