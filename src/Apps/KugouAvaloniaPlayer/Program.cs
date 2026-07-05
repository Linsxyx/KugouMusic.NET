using System;
using Avalonia;
#if KUGOU_WINDOWS
using KugouAvaloniaPlayer.Services.SystemMediaSession;
#endif
using KugouAvaloniaPlayer.Services.Startup;
using Velopack;

namespace KugouAvaloniaPlayer;

internal sealed class Program
{
    private static readonly StartupInstanceCoordinator StartupCoordinator = new();

    [STAThread]
    public static void Main(string[] args)
    {
        var launchResult = StartupCoordinator.TryAcquireOrForward(args);
        if (launchResult == StartupInstanceLaunchResult.ForwardedToPrimary)
            return;

#if KUGOU_WINDOWS
        WindowsAppIdentity.Register();
#endif
        var velopack = VelopackApp.Build();

        velopack.Run();
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    internal static void ShutdownStartupCoordinator()
    {
        StartupCoordinator.Dispose();
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
