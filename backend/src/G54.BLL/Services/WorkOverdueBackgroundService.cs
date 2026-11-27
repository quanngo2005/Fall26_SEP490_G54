using G54.BLL.Common;
using G54.BLL.Common.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace G54.BLL.Services;

/// <summary>
/// Periodically moves work past its planned end date (compared against today at UTC+7) to Overdue through <see cref="IWorkService.MarkOverdueAsync"/>.
/// Reads live <c>workOverdueEnabled</c> and <c>workOverdueSweepMinutes</c> from <see cref="SystemParametersOptions"/> (with <c>IConfiguration</c> override support for tests).
/// </summary>
public sealed class WorkOverdueBackgroundService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<WorkOverdueBackgroundService> logger,
    IOptions<SystemParametersOptions>? parameters = null) : BackgroundService
{
    private const int DefaultIntervalMinutes = 15;

    private static readonly Action<ILogger, int, Exception?> LogMarked =
        LoggerMessage.Define<int>(LogLevel.Information, new EventId(1, "WorkOverdueMarked"), "Marked {Count} work item(s) as overdue");

    private static readonly Action<ILogger, Exception?> LogFailed =
        LoggerMessage.Define(LogLevel.Error, new EventId(2, "WorkOverdueFailed"), "Overdue sweep failed");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configEnabled = configuration.GetSection("WorkOverdue:Enabled");
        if (configEnabled.Exists() && !configuration.GetValue("WorkOverdue:Enabled", true))
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var liveEnabled = configEnabled.Exists()
                ? configuration.GetValue("WorkOverdue:Enabled", true)
                : (parameters?.Value.WorkOverdueEnabled ?? true);
            if (liveEnabled)
            {
                await SweepAsync(stoppingToken);
            }

            var configuredMinutes = configuration.GetSection("WorkOverdue:IntervalMinutes").Exists()
                ? configuration.GetValue("WorkOverdue:IntervalMinutes", DefaultIntervalMinutes)
                : (parameters?.Value.WorkOverdueSweepMinutes ?? DefaultIntervalMinutes);
            var interval = TimeSpan.FromMinutes(configuredMinutes > 0 ? configuredMinutes : DefaultIntervalMinutes);
            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
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

            var campaignService = scope.ServiceProvider.GetRequiredService<ICampaignService>();
            await campaignService.SendEndReminderAsync(today, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down.
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogFailed(logger, ex);
        }
    }
}
