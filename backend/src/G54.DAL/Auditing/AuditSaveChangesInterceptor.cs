using System.Text.Json;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace G54.DAL.Auditing;

/// <summary>
/// Writes an <see cref="AuditLog"/> row for every Added/Modified/Deleted entity in the change tracker, and
/// fills <see cref="IAuditableEntity"/> stamps (CreatedBy/CreatedAt or UpdatedBy/UpdatedAt). Best-effort:
/// a failure while persisting audit rows is logged and swallowed, never breaks the caller's save.
/// </summary>
public sealed class AuditSaveChangesInterceptor(
    IAuditContextProvider contextProvider,
    TimeProvider timeProvider,
    ILogger<AuditSaveChangesInterceptor> logger) : SaveChangesInterceptor
{
    private static readonly string[] MaskedSuffixes = ["token", "secret"];
    private static readonly string[] MaskedNames = ["passwordhash", "password"];
    private const string MaskedValue = "***";

    private readonly List<PendingAudit> _pending = [];
    private bool _writingAuditLogs;

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Flush(eventData.Context);
        return base.SavedChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        await FlushAsync(eventData.Context, cancellationToken);
        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    private void Capture(DbContext? context)
    {
        if (context is null || _writingAuditLogs)
        {
            return;
        }

        _pending.Clear();
        var now = timeProvider.GetUtcNow();
        var auditContext = contextProvider.GetContext();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (entry.Entity is AuditLog || IsIgnored(entry.Entity.GetType()))
            {
                continue;
            }

            StampAuditableEntity(entry, now, auditContext.ActorId);

            var action = ResolveAction(entry);
            var old = action is "Update" or "Delete" or "SoftDelete" ? Snapshot(entry, useOriginalValues: true, changedOnly: action == "Update") : null;
            var @new = action is "Create" or "Update" or "SoftDelete" ? Snapshot(entry, useOriginalValues: false, changedOnly: action == "Update") : null;
            var changedColumns = action == "Update"
                ? entry.Properties.Where(p => p.IsModified && !IsPropertyIgnored(p.Metadata.PropertyInfo)).Select(p => p.Metadata.Name).ToArray()
                : null;

            _pending.Add(new PendingAudit(entry, action, entry.Entity.GetType().Name, old, @new, changedColumns, now, auditContext));
        }
    }

    private void Flush(DbContext? context)
    {
        if (context is null || _pending.Count == 0 || _writingAuditLogs)
        {
            return;
        }

        try
        {
            var logs = _pending.Select(BuildLog).ToList();
            _pending.Clear();
            if (logs.Count == 0)
            {
                return;
            }

            _writingAuditLogs = true;
            context.Set<AuditLog>().AddRange(logs);
            context.SaveChanges();
        }
        catch (Exception ex)
        {
            Log.AuditWriteFailed(logger, ex);
        }
        finally
        {
            _writingAuditLogs = false;
        }
    }

    private async Task FlushAsync(DbContext? context, CancellationToken cancellationToken)
    {
        if (context is null || _pending.Count == 0 || _writingAuditLogs)
        {
            return;
        }

        try
        {
            var logs = _pending.Select(BuildLog).ToList();
            _pending.Clear();
            if (logs.Count == 0)
            {
                return;
            }

            _writingAuditLogs = true;
            await context.Set<AuditLog>().AddRangeAsync(logs, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Log.AuditWriteFailed(logger, ex);
        }
        finally
        {
            _writingAuditLogs = false;
        }
    }

    private static AuditLog BuildLog(PendingAudit p)
    {
        var keyValue = p.Entry.Properties.FirstOrDefault(prop => prop.Metadata.IsPrimaryKey())?.CurrentValue;
        return new AuditLog
        {
            Id = Guid.NewGuid(),
            EntityName = p.EntityName,
            EntityId = keyValue?.ToString() ?? string.Empty,
            Action = p.Action,
            OldValues = p.OldValues,
            NewValues = p.NewValues,
            ChangedColumns = p.ChangedColumns is { Length: > 0 } ? JsonSerializer.Serialize(p.ChangedColumns) : null,
            ActorId = p.Context.ActorId,
            ActorName = p.Context.ActorName,
            IpAddress = p.Context.IpAddress,
            UserAgent = p.Context.UserAgent,
            HttpMethod = p.Context.HttpMethod,
            RequestPath = p.Context.RequestPath,
            TraceId = p.Context.TraceId,
            OccurredAt = p.Now,
        };
    }

    private static string ResolveAction(EntityEntry entry)
    {
        if (entry.State == EntityState.Added)
        {
            return "Create";
        }

        if (entry.State == EntityState.Deleted)
        {
            return "Delete";
        }

        // Modified: becoming soft-deleted is a SoftDelete, not a plain Update.
        var isDeletedProperty = entry.Properties.FirstOrDefault(p => p.Metadata.Name == nameof(ISoftDeletable.IsDeleted));
        if (isDeletedProperty is { IsModified: true } && entry.Entity is ISoftDeletable &&
            isDeletedProperty.OriginalValue is false && isDeletedProperty.CurrentValue is true)
        {
            return "SoftDelete";
        }

        return "Update";
    }

    private static void StampAuditableEntity(EntityEntry entry, DateTimeOffset now, Guid? actorId)
    {
        if (entry.Entity is not IAuditableEntity auditable)
        {
            return;
        }

        if (entry.State == EntityState.Added)
        {
            auditable.CreatedAt = now;
            auditable.CreatedBy = actorId;
        }
        else if (entry.State == EntityState.Modified)
        {
            auditable.UpdatedAt = now;
            auditable.UpdatedBy = actorId;
        }
    }

    private static string? Snapshot(EntityEntry entry, bool useOriginalValues, bool changedOnly)
    {
        var values = new Dictionary<string, object?>();
        foreach (var property in entry.Properties)
        {
            if (IsPropertyIgnored(property.Metadata.PropertyInfo))
            {
                continue;
            }

            if (changedOnly && !property.IsModified)
            {
                continue;
            }

            var raw = useOriginalValues ? property.OriginalValue : property.CurrentValue;
            values[property.Metadata.Name] = Mask(property.Metadata.Name, raw);
        }

        return values.Count == 0 ? null : JsonSerializer.Serialize(values);
    }

    private static object? Mask(string propertyName, object? value)
    {
        var lower = propertyName.ToLowerInvariant();
        if (MaskedNames.Contains(lower) || MaskedSuffixes.Any(lower.EndsWith))
        {
            return value is null ? null : MaskedValue;
        }

        return value;
    }

    private static bool IsIgnored(Type entityType) =>
        entityType.GetCustomAttributes(typeof(AuditIgnoreAttribute), inherit: true).Length > 0;

    private static bool IsPropertyIgnored(System.Reflection.PropertyInfo? property) =>
        property is not null && property.GetCustomAttributes(typeof(AuditIgnoreAttribute), inherit: true).Length > 0;

    private sealed record PendingAudit(
        EntityEntry Entry,
        string Action,
        string EntityName,
        string? OldValues,
        string? NewValues,
        string[]? ChangedColumns,
        DateTimeOffset Now,
        AuditContext Context);

    private static class Log
    {
        private static readonly Action<ILogger, Exception> AuditWriteFailedAction =
            LoggerMessage.Define(LogLevel.Warning, new EventId(1, nameof(AuditWriteFailed)), "Failed to write audit log entries; the originating save was not affected.");

        public static void AuditWriteFailed(ILogger logger, Exception ex) => AuditWriteFailedAction(logger, ex);
    }
}
