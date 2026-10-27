using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace G54.BLL.Services;

/// <summary>
/// BR-21: periodically escalates handovers not accepted within the SLA to the receiver's manager through
/// <see cref="IHandoverEscalationService"/>. Configuration: <c>HandoverEscalation:Enabled</c> (default true),
/// <c>HandoverEscalation:IntervalMinutes</c> (default 15), <c>HandoverEscalation:SlaHours</c> (default 4).
/// </summary>
public sealed class HandoverEscalationBackgroundService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<HandoverEscalationBackgroundService> logger) : BackgroundService
{
    private const int DefaultIntervalMinutes = 15;
    private const int DefaultSlaHours = 4;

    private static readonly Action<ILogger, int, Exception?> LogEscalated =
        LoggerMessage.Define<int>(LogLevel.Information, new EventId(1, "HandoverEscalated"), "Escalated {Count} overdue handover(s) to a manager");

    private static readonly Action<ILogger, Exception?> LogFailed =
        LoggerMessage.Define(LogLevel.Error, new EventId(2, "HandoverEscalationFailed"), "Handover escalation sweep failed");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("HandoverEscalation:Enabled", true))
        {
            return;
        }

        var minutes = configuration.GetValue("HandoverEscalation:IntervalMinutes", DefaultIntervalMinutes);
        var hours = configuration.GetValue("HandoverEscalation:SlaHours", DefaultSlaHours);
        var sla = TimeSpan.FromHours(hours > 0 ? hours : DefaultSlaHours);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minutes > 0 ? minutes : DefaultIntervalMinutes));
        try
        {
            do
            {
                await SweepAsync(sla, stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    private async Task SweepAsync(TimeSpan sla, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IHandoverEscalationService>();
            var count = await service.EscalateOverdueAsync(sla, cancellationToken);
            if (count > 0)
            {
                LogEscalated(logger, count, null);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, ex);
        }
    }
}
