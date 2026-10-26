using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace KugouAvaloniaPlayer.Services.Startup;

internal interface IStartupActivationServer : IDisposable
{
    void Start();
    void Stop();
}

internal sealed class StartupActivationServer(
    IStartupActivationService activationService,
    IUiDispatcherService uiDispatcherService,
    ILogger<StartupActivationServer> logger) : IStartupActivationServer
{
    private readonly IStartupActivationService _activationService = activationService;
    private readonly IUiDispatcherService _uiDispatcherService = uiDispatcherService;
    private readonly ILogger<StartupActivationServer> _logger = logger;

    private readonly object _syncRoot = new();
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _listenTask;
    private bool _started;

    public void Start()
    {
        lock (_syncRoot)
        {
            if (_started)
                return;

            _cancellationTokenSource = new CancellationTokenSource();
            _listenTask = Task.Run(() => ListenLoopAsync(_cancellationTokenSource.Token));
            _started = true;
        }

        _logger.LogInformation(
            "Startup activation server started. PipeName: {PipeName}",
            StartupActivationConstants.PipeName);
    }

    public void Stop()
    {
        Task? listenTask;
        CancellationTokenSource? cancellationTokenSource;

        lock (_syncRoot)
        {
            if (!_started)
                return;

            _started = false;
            listenTask = _listenTask;
            cancellationTokenSource = _cancellationTokenSource;
            _listenTask = null;
            _cancellationTokenSource = null;
        }

        cancellationTokenSource?.Cancel();

        if (listenTask != null)
        {
            try
            {
                listenTask.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException ex)
            {
                _logger.LogDebug(ex, "Startup activation server stopped while waiting for listener completion.");
            }
        }

        cancellationTokenSource?.Dispose();
        _logger.LogInformation("Startup activation server stopped.");
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    StartupActivationConstants.PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(cancellationToken);

                using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, 1024, leaveOpen: true);
                var json = await reader.ReadToEndAsync();
                var request = JsonSerializer.Deserialize(
                    json,
                    StartupActivationJsonContext.Default.StartupActivationRequest);

                if (request == null)
                {
                    _logger.LogWarning("Received empty startup activation payload.");
                    continue;
                }

                _logger.LogInformation(
                    "Received startup activation request. Kind: {Kind}, ArgsCount: {ArgsCount}",
                    request.Kind,
                    request.Args.Length);

                _uiDispatcherService.RunOrPost(() => _ = HandleRequestAsync(request));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Startup activation server I/O error.");
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Startup activation server failed while listening.");
            }
        }
    }

    private async Task HandleRequestAsync(StartupActivationRequest request)
    {
        try
        {
            await _activationService.HandleAsync(request, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle startup activation request.");
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
