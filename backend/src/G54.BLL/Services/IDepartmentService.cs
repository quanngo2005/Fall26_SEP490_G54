using G54.BLL.Common.Models;
using G54.BLL.Dtos.Department;

namespace G54.BLL.Services;

public interface IDepartmentService
{
    Task<PagedResult<DepartmentSummaryDto>> GetDepartmentsAsync(
        Guid accountId,
        DepartmentFilterDto filter,
        CancellationToken cancellationToken = default);

    Task<DepartmentDetailDto> GetDepartmentAsync(
        Guid accountId,
        Guid departmentId,
        CancellationToken cancellationToken = default);

    Task<DepartmentOverviewDto> GetOverviewAsync(
        Guid accountId,
        Guid departmentId,
        Guid? periodId,
        CancellationToken cancellationToken = default);

    Task<PagedResult<DepartmentMemberDto>> GetMembersAsync(
        Guid accountId,
        Guid departmentId,
        DepartmentMemberFilterDto filter,
        CancellationToken cancellationToken = default);

    /// <summary>The departments visible to the caller as a forest (roots are those whose parent is not visible).</summary>
    Task<IReadOnlyList<DepartmentTreeNodeDto>> GetTreeAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);

    Task<DepartmentDetailDto> CreateDepartmentAsync(
        CreateDepartmentDto dto,
        CancellationToken cancellationToken = default);

    Task<DepartmentDetailDto> UpdateDepartmentAsync(
        Guid departmentId,
        UpdateDepartmentDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>Activates, deactivates (INACTIVE) or archives a department.</summary>
    Task<DepartmentDetailDto> ChangeStatusAsync(
        Guid departmentId,
        DepartmentStatusDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>Soft delete: marks the department ARCHIVED (hidden from lists and the tree). Refused while it has non-archived children or non-terminated employees; deleting an archived department is a no-op.</summary>
    Task DeleteDepartmentAsync(
        Guid departmentId,
        CancellationToken cancellationToken = default);
}
