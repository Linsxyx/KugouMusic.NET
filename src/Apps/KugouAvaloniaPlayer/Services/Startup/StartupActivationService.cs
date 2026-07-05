using CommunityToolkit.Mvvm.Messaging;
using KugouAvaloniaPlayer.Models;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace KugouAvaloniaPlayer.Services.Startup;

internal interface IStartupActivationService
{
    Task HandleAsync(StartupActivationRequest request, CancellationToken cancellationToken);
}

internal sealed class StartupActivationService(
    ILogger<StartupActivationService> logger) : IStartupActivationService
{
    private readonly ILogger<StartupActivationService> _logger = logger;

    public Task HandleAsync(StartupActivationRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Handling startup activation request. Kind: {Kind}, ArgsCount: {ArgsCount}",
            request.Kind,
            request.Args.Length);

        if (request.Args.Length == 0)
        {
            WeakReferenceMessenger.Default.Send(new ShowMainWindowMessage());
            return Task.CompletedTask;
        }

        _logger.LogInformation(
            "Startup activation request contains args and is reserved for future handling. Kind: {Kind}",
            request.Kind);
        return Task.CompletedTask;
    }
}
