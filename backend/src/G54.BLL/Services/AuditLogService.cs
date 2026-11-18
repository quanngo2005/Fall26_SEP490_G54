using System.Text.Json;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.AuditLog;
using G54.DAL.Entities;
using G54.DAL.Repositories;

namespace G54.BLL.Services;

public sealed class AuditLogService(IAuditLogRepository repository) : IAuditLogService
{
    private static readonly string[] ValidActions = ["Create", "Update", "Delete", "SoftDelete"];
    private const int MaxPageSize = 100;

    public async Task<PagedResult<AuditLogListItemDto>> GetPagedAsync(AuditLogQueryDto query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var errors = new List<string>();
        if (query.From.HasValue && query.To.HasValue && query.From.Value > query.To.Value)
        {
            errors.Add("Khoảng thời gian không hợp lệ: 'from' phải trước hoặc bằng 'to'.");
        }

        if (query.Action is { Length: > 0 } action && !ValidActions.Contains(action, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add("Giá trị 'action' không hợp lệ: Create, Update, Delete hoặc SoftDelete.");
        }

        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0 ? 20 : query.PageSize;
        if (pageSize > MaxPageSize)
        {
            errors.Add($"'pageSize' tối đa là {MaxPageSize}.");
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var sortDesc = !string.Equals(query.Sort, "occurredAt asc", StringComparison.OrdinalIgnoreCase);

        var filter = new AuditLogFilter(query.EntityName, query.EntityId, query.Action, query.ActorId, query.From, query.To, query.Keyword);
        var (items, totalCount) = await repository.GetPagedAsync(filter, page, pageSize, sortDesc, ct);

        var dtos = items.Select(ToListItemDto).ToList();
        return new PagedResult<AuditLogListItemDto>(dtos, totalCount, page, pageSize);
    }

    public async Task<AuditLogDetailDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await repository.GetByIdAsync(id, ct) ?? throw new NotFoundException("Nhật ký audit", id);
        return ToDetailDto(entity);
    }

    private static AuditLogListItemDto ToListItemDto(AuditLog a) => new(
        a.Id,
        a.EntityName,
        a.EntityId,
        a.Action,
        a.ActorId,
        a.ActorName,
        a.IpAddress,
        a.OccurredAt,
        ParseChangedColumns(a.ChangedColumns));

    private static AuditLogDetailDto ToDetailDto(AuditLog a) => new(
        a.Id,
        a.EntityName,
        a.EntityId,
        a.Action,
        a.ActorId,
        a.ActorName,
        a.IpAddress,
        a.UserAgent,
        a.HttpMethod,
        a.RequestPath,
        a.TraceId,
        a.OccurredAt,
        ParseChangedColumns(a.ChangedColumns),
        ParseJson(a.OldValues),
        ParseJson(a.NewValues));

    private static string[]? ParseChangedColumns(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<string[]>(json);

    private static JsonElement? ParseJson(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<JsonElement>(json);
}
