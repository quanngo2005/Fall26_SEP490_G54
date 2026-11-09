using G54.BLL.Common.Models;
using G54.BLL.Dtos.AuditLog;

namespace G54.BLL.Services;

public interface IAuditLogService
{
    Task<PagedResult<AuditLogListItemDto>> GetPagedAsync(AuditLogQueryDto query, CancellationToken ct = default);

    Task<AuditLogDetailDto> GetByIdAsync(Guid id, CancellationToken ct = default);
}
