using System;
using Avalonia;
#if KUGOU_WINDOWS
using KugouAvaloniaPlayer.Services.SystemMediaSession;
#endif
using KugouAvaloniaPlayer.Services.Startup;
using Serilog;
using Velopack;

namespace KugouAvaloniaPlayer;

internal sealed class Program
{
    private static readonly StartupInstanceCoordinator StartupCoordinator = new();

    [STAThread]
    public static void Main(string[] args)
    {
        CrashReporting.ConfigureLogging();
        CrashReporting.RegisterGlobalHandlers();

        var launchResult = StartupCoordinator.TryAcquireOrForward(args);
        if (launchResult == StartupInstanceLaunchResult.ForwardedToPrimary)
        {
            Log.CloseAndFlush();
            return;
        }

#if KUGOU_WINDOWS
        WindowsAppIdentity.Register();
#endif
        var velopack = VelopackApp.Build();

        try
        {
            velopack.Run();
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "桌面程序启动或运行期间发生致命异常");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
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
