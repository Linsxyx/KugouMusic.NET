using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace KaMusic.Cli.Services;

// CLI 轻量级文件日志：按天滚动写入 kugou/logs/cli-YYYYMMDD.log，仅记录关键状态、阻塞告警与异常，零反射适配 NativeAOT。

internal static class CliLog
{
    private const long DefaultMaxFileBytes = 10 * 1024 * 1024;
    private const int DefaultRetainedFiles = 14;
    private static readonly TimeSpan DefaultRetainedAge = TimeSpan.FromDays(14);
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly Lock SyncRoot = new();

    private static string? _logDirectory;
    private static long _maxFileBytes = DefaultMaxFileBytes;
    private static int _retainedFiles = DefaultRetainedFiles;
    private static TimeSpan _retainedAge = DefaultRetainedAge;
    private static DateOnly _currentDate;
    private static int _currentPart;
    private static long _currentFileBytes;
    private static string? _currentFilePath;
    private static StreamWriter? _writer;
    private static bool _initialized;
    private static bool _globalHandlersRegistered;

    public static string DefaultLogDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "kugou",
            "logs");

    public static string? CurrentLogFilePath
    {
        get
        {
            lock (SyncRoot)
            {
                return _currentFilePath;
            }
        }
    }

    // 初始化文件日志目录与滚动策略，并清理过期旧日志。
    public static void Initialize(
        string? logDirectory = null,
        long maxFileBytes = DefaultMaxFileBytes,
        int retainedFiles = DefaultRetainedFiles,
        TimeSpan? retainedAge = null)
    {
        lock (SyncRoot)
        {
            CloseWriterNoLock();
            _logDirectory = string.IsNullOrWhiteSpace(logDirectory) ? DefaultLogDirectory : logDirectory;
            _maxFileBytes = Math.Max(1024, maxFileBytes);
            _retainedFiles = Math.Max(1, retainedFiles);
            _retainedAge = retainedAge ?? DefaultRetainedAge;
            _initialized = true;

            try
            {
                Directory.CreateDirectory(_logDirectory);
                CleanupOldLogsNoLock(DateTime.Now);
                EnsureWriterNoLock(DateTime.Now);
            }
            catch
            {
                CloseWriterNoLock();
                _initialized = false;
            }
        }
    }

    // 注册进程级未处理异常和未观察任务异常钩子。
    public static void RegisterGlobalExceptionHandlers()
    {
        lock (SyncRoot)
        {
            if (_globalHandlersRegistered)
            {
                return;
            }

            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            _globalHandlersRegistered = true;
        }
    }

    public static void Info(string category, string message) =>
        Write("INF", category, message, null);

    public static void Warn(string category, string message, Exception? ex = null) =>
        Write("WRN", category, message, ex);

    public static void Error(string category, string message, Exception? ex = null) =>
        Write("ERR", category, message, ex);

    // 返回仅记录 Warning 及以上级别的 ILogger<T>，避免底层 API 产生大量常规日志。
    public static ILogger<T> CreateLogger<T>() => new CliCategoryLogger<T>();

    // 移除 URL 中的查询参数，避免将签名或鉴权参数写入日志。
    public static string SanitizeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "<empty>";
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return "<invalid-url>";
        }

        return $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath}";
    }

    public static void Shutdown()
    {
        lock (SyncRoot)
        {
            CloseWriterNoLock();
            _initialized = false;
        }
    }

    // 将单条格式化日志写入当前日志文件，必要时按天或按文件大小滚动。
    private static void Write(string level, string category, string message, Exception? ex)
    {
        lock (SyncRoot)
        {
            if (!_initialized || string.IsNullOrEmpty(_logDirectory))
            {
                return;
            }

            try
            {
                var now = DateTime.Now;
                EnsureWriterNoLock(now);
                if (_writer == null)
                {
                    return;
                }

                var sb = new StringBuilder(160);
                sb.Append('[')
                    .Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
                    .Append(' ')
                    .Append(level)
                    .Append("] ")
                    .Append(category)
                    .Append(": ")
                    .Append(message);

                if (ex != null)
                {
                    sb.AppendLine();
                    sb.Append(ex);
                }

                var line = sb.ToString();
                _writer.WriteLine(line);
                _currentFileBytes += Utf8NoBom.GetByteCount(line) + Environment.NewLine.Length;
            }
            catch
            {
                // 日志写入失败不应影响主流程。
            }
        }
    }

    // 确保写入器指向当天的有效文件；跨天或超出体积上限时自动切分新文件。
    private static void EnsureWriterNoLock(DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        var dayChanged = _writer == null || today != _currentDate;
        var sizeExceeded = _writer != null && _currentFileBytes >= _maxFileBytes;
        if (!dayChanged && !sizeExceeded)
        {
            return;
        }

        CloseWriterNoLock();
        if (string.IsNullOrEmpty(_logDirectory))
        {
            return;
        }

        Directory.CreateDirectory(_logDirectory);
        if (dayChanged)
        {
            _currentDate = today;
            _currentPart = 0;
            CleanupOldLogsNoLock(now);
        }
        else
        {
            _currentPart++;
        }

        var path = ResolveWritablePathNoLock(today, ref _currentPart);
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _currentFileBytes = stream.Length;
        _currentFilePath = path;
        _writer = new StreamWriter(stream, Utf8NoBom) { AutoFlush = true };
    }

    // 为当天日志寻找尚未超过大小上限的目标文件路径。
    private static string ResolveWritablePathNoLock(DateOnly date, ref int part)
    {
        var stamp = date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        while (true)
        {
            var fileName = part == 0 ? $"cli-{stamp}.log" : $"cli-{stamp}_{part:000}.log";
            var path = Path.Combine(_logDirectory!, fileName);
            if (!File.Exists(path) || new FileInfo(path).Length < _maxFileBytes)
            {
                return path;
            }

            part++;
        }
    }

    // 按保留天数与最大文件数清理历史 CLI 日志，不影响桌面端 kg-*.log。
    private static void CleanupOldLogsNoLock(DateTime now)
    {
        if (string.IsNullOrEmpty(_logDirectory) || !Directory.Exists(_logDirectory))
        {
            return;
        }

        try
        {
            var cutoff = now - _retainedAge;
            var files = new DirectoryInfo(_logDirectory)
                .GetFiles("cli-*.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            for (var i = 0; i < files.Count; i++)
            {
                var file = files[i];
                if (i >= _retainedFiles || file.LastWriteTime < cutoff)
                {
                    try
                    {
                        file.Delete();
                    }
                    catch
                    {
                        // 忽略被占用或无权删除的旧日志。
                    }
                }
            }
        }
        catch
        {
            // 清理失败不应阻止日志写入。
        }
    }

    private static void CloseWriterNoLock()
    {
        if (_writer != null)
        {
            try
            {
                _writer.Dispose();
            }
            catch
            {
                // Ignore dispose errors.
            }

            _writer = null;
        }

        _currentFilePath = null;
        _currentFileBytes = 0;
    }

    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        var detail = ex == null ? $"IsTerminating={e.IsTerminating}, Obj={e.ExceptionObject}" : $"IsTerminating={e.IsTerminating}";
        Error("AppDomain", $"未处理异常 ({detail})", ex);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Error("TaskScheduler", "未观察的任务异常", e.Exception);
    }

    private sealed class CliCategoryLogger<T> : ILogger<T>
    {
        private static readonly string Category = typeof(T).Name;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            if (logLevel >= LogLevel.Error)
            {
                Error(Category, message, exception);
            }
            else
            {
                Warn(Category, message, exception);
            }
        }
    }
}
