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
    [STAThread]
    public static void Main(string[] args)
    {
        CrashReporting.ConfigureLogging();
        CrashReporting.RegisterGlobalHandlers();
        GraphicsBackendSelection.Initialize();

        var launchResult = StartupInstanceCoordinator.Current.TryAcquireOrForward(args);
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

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

        // Native first (Metal / Vulkan), the previous OpenGL family as the fallback; see GraphicsBackendSelection.
        var openGl = GraphicsBackendSelection.PreferOpenGl;
#if KUGOU_MACOS
        builder = builder.With(new AvaloniaNativePlatformOptions
        {
            RenderingMode = openGl
                ? [AvaloniaNativeRenderingMode.OpenGl, AvaloniaNativeRenderingMode.Metal, AvaloniaNativeRenderingMode.Software]
                : [AvaloniaNativeRenderingMode.Metal, AvaloniaNativeRenderingMode.OpenGl, AvaloniaNativeRenderingMode.Software],
        });
#elif KUGOU_WINDOWS
        builder = builder.With(new Win32PlatformOptions
        {
            RenderingMode = openGl
                ? [Win32RenderingMode.AngleEgl, Win32RenderingMode.Wgl, Win32RenderingMode.Software]
                : [Win32RenderingMode.Vulkan, Win32RenderingMode.AngleEgl, Win32RenderingMode.Wgl, Win32RenderingMode.Software],
        });
#elif KUGOU_LINUX
        builder = builder.With(new X11PlatformOptions
        {
            RenderingMode = openGl
                ? [X11RenderingMode.Egl, X11RenderingMode.Glx, X11RenderingMode.Software]
                : [X11RenderingMode.Vulkan, X11RenderingMode.Egl, X11RenderingMode.Glx, X11RenderingMode.Software],
        });
#endif

        return builder;
    }
}
