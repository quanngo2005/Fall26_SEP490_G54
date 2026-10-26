using G54.BLL.Common.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace G54.BLL.Services;

/// <summary>
/// Periodically moves work past its planned end date (compared against today at UTC+7) to Overdue through <see cref="IWorkService.MarkOverdueAsync"/>.
/// Configuration: <c>WorkOverdue:Enabled</c> (default true), <c>WorkOverdue:IntervalMinutes</c> (default 15).
/// </summary>
public sealed class WorkOverdueBackgroundService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<WorkOverdueBackgroundService> logger) : BackgroundService
{
    private const int DefaultIntervalMinutes = 15;

    private static readonly Action<ILogger, int, Exception?> LogMarked =
        LoggerMessage.Define<int>(LogLevel.Information, new EventId(1, "WorkOverdueMarked"), "Marked {Count} work item(s) as overdue");

    private static readonly Action<ILogger, Exception?> LogFailed =
        LoggerMessage.Define(LogLevel.Error, new EventId(2, "WorkOverdueFailed"), "Overdue sweep failed");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("WorkOverdue:Enabled", true))
        {
            return;
        }

        var minutes = configuration.GetValue("WorkOverdue:IntervalMinutes", DefaultIntervalMinutes);
        var interval = TimeSpan.FromMinutes(minutes > 0 ? minutes : DefaultIntervalMinutes);

        using var timer = new PeriodicTimer(interval);
        do
        {
            await SweepAsync(stoppingToken);
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));
    }

    internal async Task SweepAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var workService = scope.ServiceProvider.GetRequiredService<IWorkService>();
            var today = time.GetBusinessToday();
            var count = await workService.MarkOverdueAsync(today, cancellationToken);
            if (count > 0)
            {
                LogMarked(logger, count, null);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            LogFailed(logger, ex);
        }
    }

    private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
