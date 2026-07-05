using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace KugouAvaloniaPlayer.Services.Startup;

internal sealed class StartupInstanceCoordinator : IDisposable
{
    private readonly object _syncRoot = new();
    private Mutex? _mutex;
    private bool _ownsMutex;

    public StartupInstanceLaunchResult TryAcquireOrForward(string[] args)
    {
        if (TryAcquireMutex())
            return StartupInstanceLaunchResult.LaunchAsPrimary;

        var request = StartupActivationRequest.CreateActivate(args);
        if (TryForwardWithRetry(request))
            return StartupInstanceLaunchResult.ForwardedToPrimary;

        var acquiredAfterRetry = TryAcquireMutex();
        Trace.WriteLine(
            acquiredAfterRetry
                ? "[Startup] Forwarding failed; mutex became available, continuing as primary."
                : "[Startup] Forwarding failed after retries; continuing startup without confirmed handoff.");

        return StartupInstanceLaunchResult.LaunchAsPrimaryAfterForwardRetryFailure;
    }

    private bool TryAcquireMutex()
    {
        lock (_syncRoot)
        {
            if (_ownsMutex)
                return true;

            _mutex ??= new Mutex(false, StartupActivationConstants.MutexName);

            try
            {
                _ownsMutex = _mutex.WaitOne(TimeSpan.Zero, false);
            }
            catch (AbandonedMutexException)
            {
                _ownsMutex = true;
            }

            return _ownsMutex;
        }
    }

    private static bool TryForwardWithRetry(StartupActivationRequest request)
    {
        for (var attempt = 1; attempt <= StartupActivationConstants.ForwardRetryCount; attempt++)
        {
            if (TryForwardOnce(request))
            {
                Trace.WriteLine($"[Startup] Forwarded activation request on attempt {attempt}.");
                return true;
            }

            Trace.WriteLine($"[Startup] Forward attempt {attempt} failed.");

            if (attempt < StartupActivationConstants.ForwardRetryCount)
                Thread.Sleep(StartupActivationConstants.RetryDelayMilliseconds);
        }

        return false;
    }

    private static bool TryForwardOnce(StartupActivationRequest request)
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".",
                StartupActivationConstants.PipeName,
                PipeDirection.Out);
            client.Connect(StartupActivationConstants.ConnectTimeoutMilliseconds);

            var json = JsonSerializer.Serialize(
                request,
                StartupActivationJsonContext.Default.StartupActivationRequest);
            using var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, leaveOpen: true);
            writer.Write(json);
            writer.Flush();
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_mutex == null)
                return;

            if (_ownsMutex)
            {
                _mutex.ReleaseMutex();
                _ownsMutex = false;
            }

            _mutex.Dispose();
            _mutex = null;
        }
    }
}
