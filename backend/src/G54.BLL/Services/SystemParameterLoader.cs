using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace G54.BLL.Services;

/// <summary>Loads saved system-parameter overrides into <see cref="SystemParameterStore"/> when the host starts.</summary>
public sealed class SystemParameterLoader(IServiceScopeFactory scopeFactory, ILogger<SystemParameterLoader> logger) : IHostedService
{
    private static readonly Action<ILogger, Exception?> LogLoadFailed =
        LoggerMessage.Define(LogLevel.Warning, new EventId(1, "SystemParametersLoadFailed"), "Could not load system parameters; using appsettings defaults");

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ISystemParameterService>().RefreshAsync(cancellationToken);
        }
#pragma warning disable CA1031 // A missing/unmigrated table must not stop the API from starting.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogLoadFailed(logger, ex);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
