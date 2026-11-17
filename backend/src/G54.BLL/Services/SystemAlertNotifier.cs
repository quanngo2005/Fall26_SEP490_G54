using G54.DAL;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace G54.BLL.Services;

public interface ISystemAlertNotifier
{
    /// <summary>Best-effort alert pushed to active System Administrators when an unhandled exception or security lockout occurs.</summary>
    Task NotifyAdminsAsync(string title, string body, CancellationToken cancellationToken = default);
}

public sealed class SystemAlertNotifier(
    IServiceScopeFactory scopeFactory,
    ILogger<SystemAlertNotifier>? logger = null) : ISystemAlertNotifier
{
    private static readonly Action<ILogger, Exception?> LogAlertFailed =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1, "SystemAlertFailed"),
            "Could not store or push a system alert notification to administrators.");

    private readonly ILogger<SystemAlertNotifier> _logger = logger ?? NullLogger<SystemAlertNotifier>.Instance;

    public async Task NotifyAdminsAsync(string title, string body, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notifier = scope.ServiceProvider.GetService<INotifier>();
            if (notifier is null)
            {
                return;
            }

            var adminIds = await NotificationRecipients.ActiveAdminsAsync(db, cancellationToken);
            if (adminIds.Count == 0)
            {
                return;
            }

            var drafts = AutomaticNotifications.SystemAlertForAdmins(adminIds, title, body);
            await notifier.TryPublishAsync(drafts, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogAlertFailed(_logger, ex);
        }
    }
}
