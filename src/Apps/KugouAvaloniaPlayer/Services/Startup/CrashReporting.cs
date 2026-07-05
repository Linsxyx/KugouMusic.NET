using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;
using Serilog;
using Serilog.Events;

namespace KugouAvaloniaPlayer.Services.Startup;

internal static class CrashReporting
{
    private static readonly object SyncRoot = new();
    private static bool _loggingConfigured;
    private static bool _globalHandlersRegistered;
    private static bool _uiThreadHandlerRegistered;

    private static string LogDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "kugou",
            "logs");

    public static void ConfigureLogging()
    {
        lock (SyncRoot)
        {
            if (_loggingConfigured)
                return;

            Directory.CreateDirectory(LogDirectory);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.Extensions.Http", LogEventLevel.Warning)
                .MinimumLevel.Override("Avalonia", LogEventLevel.Warning)
                .Enrich.FromLogContext()
#if DEBUG
                .WriteTo.Async(a => a.Debug(outputTemplate:
                    "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"))
                .WriteTo.Async(a => a.Console(outputTemplate:
                    "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"))
#else
                .WriteTo.Async(a => a.File(
                    Path.Combine(LogDirectory, "kg-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 20,
                    retainedFileTimeLimit: TimeSpan.FromDays(14),
                    fileSizeLimitBytes: 10 * 1024 * 1024,
                    rollOnFileSizeLimit: true,
                    outputTemplate:
                    "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"))
#endif
                .CreateLogger();

            _loggingConfigured = true;
        }
    }

    public static void RegisterGlobalHandlers()
    {
        lock (SyncRoot)
        {
            if (_globalHandlersRegistered)
                return;

            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            _globalHandlersRegistered = true;
        }
    }

    public static void RegisterUiThreadHandler()
    {
        lock (SyncRoot)
        {
            if (_uiThreadHandlerRegistered)
                return;

            Dispatcher.UIThread.UnhandledException += OnUiThreadUnhandledException;
            _uiThreadHandlerRegistered = true;
        }
    }

    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception;
        TryLogFatal(exception, "发生未处理的应用程序异常，IsTerminating={IsTerminating}", e.IsTerminating);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        TryLogFatal(e.Exception, "发生未观察到的任务异常");
    }

    private static void OnUiThreadUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        TryLogFatal(e.Exception, "发生未处理的 UI 线程异常");
    }

    private static void TryLogFatal(Exception? exception, string messageTemplate, params object?[] args)
    {
        try
        {
            ConfigureLogging();
            Log.Fatal(exception, messageTemplate, args);
            Log.CloseAndFlush();
        }
        catch (Exception loggingException)
        {
            WriteFallbackCrashLog(exception, messageTemplate, args, loggingException);
        }
    }

    private static void WriteFallbackCrashLog(
        Exception? exception,
        string messageTemplate,
        object?[] args,
        Exception loggingException)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);

            var filePath = Path.Combine(LogDirectory, "kg-crash-fallback.log");
            var builder = new StringBuilder();
            builder.AppendLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {messageTemplate}");
            if (args.Length > 0)
                builder.AppendLine($"Args: {string.Join(", ", args)}");
            if (exception is not null)
            {
                builder.AppendLine("Exception:");
                builder.AppendLine(exception.ToString());
            }

            builder.AppendLine("LoggingException:");
            builder.AppendLine(loggingException.ToString());
            builder.AppendLine();

            File.AppendAllText(filePath, builder.ToString(), Encoding.UTF8);
        }
        catch
        {
            // Final fallback: process is already failing, and there is nowhere else to report.
        }
    }
}
