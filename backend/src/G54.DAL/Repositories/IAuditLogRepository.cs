using G54.DAL.Entities;

namespace G54.DAL.Repositories;

public interface IAuditLogRepository
{
    Task<(List<AuditLog> Items, int TotalCount)> GetPagedAsync(
        AuditLogFilter filter,
        int page,
        int pageSize,
        bool sortDesc,
        CancellationToken ct = default);

    Task<AuditLog?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task AddRangeAsync(IEnumerable<AuditLog> logs, CancellationToken ct = default);
}
