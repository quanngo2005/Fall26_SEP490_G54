using G54.BLL.Dtos.Notification;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace G54.BLL.Services;

public sealed class Notifier(
    AppDbContext db,
    INotificationPusher pusher,
    TimeProvider time,
    ILogger<Notifier> logger) : INotifier
{
    private static readonly Action<ILogger, Exception?> LogPublishFailed =
        LoggerMessage.Define(LogLevel.Warning, new EventId(1, "NotificationPublishFailed"), "A notification could not be stored");

    private static readonly Action<ILogger, Guid, Exception?> LogPushFailed =
        LoggerMessage.Define<Guid>(LogLevel.Warning, new EventId(2, "NotificationPushFailed"), "A notification could not be pushed to employee {EmployeeId}");

    public async Task PublishAsync(IReadOnlyCollection<NotificationDraft> drafts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(drafts);
        if (drafts.Count == 0)
        {
            return;
        }

        var now = time.GetUtcNow();
        var rows = drafts.Select(draft => new Notification
        {
            RecipientEmployeeId = draft.RecipientEmployeeId,
            SenderEmployeeId = draft.SenderEmployeeId,
            Type = draft.Type,
            Severity = draft.Severity,
            Title = draft.Title,
            Body = draft.Body,
            RelatedType = draft.RelatedType,
            RelatedId = draft.RelatedId,
            CreatedAt = now,
        }).ToList();

        db.Notifications.AddRange(rows);
        await db.SaveChangesAsync(cancellationToken);

        var senderIds = rows.Where(row => row.SenderEmployeeId is not null).Select(row => row.SenderEmployeeId!.Value).Distinct().ToList();
        var senderNames = senderIds.Count == 0
            ? []
            : await db.Employees.AsNoTracking()
                .Where(employee => senderIds.Contains(employee.Id))
                .ToDictionaryAsync(employee => employee.Id, employee => employee.FullName, cancellationToken);

        foreach (var row in rows)
        {
            var senderName = row.SenderEmployeeId is { } senderId ? senderNames.GetValueOrDefault(senderId) : null;
            try
            {
                await pusher.PushAsync(row.RecipientEmployeeId, NotificationMapper.ToDto(row, senderName), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogPushFailed(logger, row.RecipientEmployeeId, ex);
            }
        }
    }

    public async Task TryPublishAsync(IReadOnlyCollection<NotificationDraft> drafts, CancellationToken cancellationToken = default)
    {
        try
        {
            await PublishAsync(drafts, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogPublishFailed(logger, ex);
        }
    }
}
