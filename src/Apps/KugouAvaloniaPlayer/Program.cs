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
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

#if KUGOU_MACOS
        // Sonnet uses OpenGlControlBase. Prefer OpenGL on macOS while retaining
        // the existing Metal renderer and software renderer as fallbacks.
        builder = builder.With(new AvaloniaNativePlatformOptions
        {
            RenderingMode =
            [
                AvaloniaNativeRenderingMode.OpenGl,
                AvaloniaNativeRenderingMode.Metal,
                AvaloniaNativeRenderingMode.Software,
            ],
        });
#elif KUGOU_WINDOWS
        // Sonnet's shaders target desktop GLSL 3.30, so prefer the native WGL
        // backend first. ANGLE remains a fallback for systems where WGL cannot
        // create a context, and software rendering keeps startup graceful.
        builder = builder.With(new Win32PlatformOptions
        {
            RenderingMode =
            [
                Win32RenderingMode.Wgl,
                Win32RenderingMode.AngleEgl,
                Win32RenderingMode.Software,
            ],
        });
#elif KUGOU_LINUX
        // On Linux, prefer the native GLX/EGL paths used by Avalonia's X11
        // backend. Software remains available for headless or unsupported GPUs.
        builder = builder.With(new X11PlatformOptions
        {
            RenderingMode =
            [
                X11RenderingMode.Glx,
                X11RenderingMode.Egl,
                X11RenderingMode.Software,
            ],
        });
#endif

        return builder;
    }
}
