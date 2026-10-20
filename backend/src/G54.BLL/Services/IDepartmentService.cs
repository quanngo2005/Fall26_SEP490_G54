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
}
