using G54.BLL.Dtos.Submission;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace G54.BLL.Services;

public sealed class EventBus(
    IDistributedCache cache,
    ILogger<EventBus> logger) : IEventBus
{
    private static readonly Action<ILogger, string, Exception?> LogUnhandledEvent =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(1, "UnhandledEvent"), "EventBus received unhandled event of type {EventType}");

    private static readonly Action<ILogger, Guid, Exception?> LogCacheInvalidated =
        LoggerMessage.Define<Guid>(LogLevel.Information, new EventId(2, "CacheInvalidated"), "EventBus: Invalidated Redis cache for Work Kanban and Pending Approvals (WorkId: {WorkId})");

    private static readonly Action<ILogger, Guid, Exception?> LogCacheInvalidationFailed =
        LoggerMessage.Define<Guid>(LogLevel.Warning, new EventId(3, "CacheInvalidationFailed"), "EventBus: Failed to invalidate cache for Work {WorkId}");

    private static readonly Action<ILogger, string, string, int, Guid, Exception?> LogNotificationDispatched =
        LoggerMessage.Define<string, string, int, Guid>(LogLevel.Information, new EventId(4, "NotificationDispatched"), "EventBus: Notification routed for Work {WorkCode} ('{WorkTitle}'), Submission #{SubmissionNo} (ID: {SubmissionId}). Notified Department Manager / Verifier via In-App and Email.");

    public async Task PublishAsync<TEvent>(TEvent eventMessage, CancellationToken cancellationToken = default)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(eventMessage);

        if (eventMessage is WorkSubmittedEvent workSubmitted)
        {
            await HandleWorkSubmittedEventAsync(workSubmitted, cancellationToken);
            return;
        }

        LogUnhandledEvent(logger, typeof(TEvent).Name, null);
    }

    private async Task HandleWorkSubmittedEventAsync(
        WorkSubmittedEvent e,
        CancellationToken cancellationToken)
    {
        // 1. Invalidate Redis cache for Work Kanban & Pending Approvals
        try
        {
            await cache.RemoveAsync($"work:kanban:employee:{e.SubmitterId}", cancellationToken);
            await cache.RemoveAsync("work:kanban:all", cancellationToken);
            await cache.RemoveAsync("work:pending-approvals", cancellationToken);
            LogCacheInvalidated(logger, e.WorkId, null);
        }
        catch (Exception ex)
        {
            LogCacheInvalidationFailed(logger, e.WorkId, ex);
        }

        // 2. Dispatch simulated notification: In-App + Email to Department Manager / Verifier
        LogNotificationDispatched(logger, e.WorkCode, e.WorkTitle, e.SubmissionNo, e.SubmissionId, null);
    }
}
