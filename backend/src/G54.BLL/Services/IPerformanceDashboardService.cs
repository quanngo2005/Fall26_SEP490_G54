using G54.BLL.Dtos.PerformanceDashboard;

namespace G54.BLL.Services;

public interface IPerformanceDashboardService
{
    /// <summary>The caller's own performance.</summary>
    Task<PersonalPerformanceDashboardDto> GetMyDashboardAsync(
        Guid accountId,
        Guid? periodId,
        CancellationToken cancellationToken = default);

    /// <summary>One employee's performance: always allowed for the caller's own id, otherwise limited to the caller's visible departments.</summary>
    Task<PersonalPerformanceDashboardDto> GetEmployeeDashboardAsync(
        Guid accountId,
        Guid employeeId,
        Guid? periodId,
        CancellationToken cancellationToken = default);

    /// <summary>The performance of a department subtree.</summary>
    Task<UnitPerformanceDashboardDto> GetDepartmentDashboardAsync(
        Guid accountId,
        Guid departmentId,
        Guid? periodId,
        CancellationToken cancellationToken = default);

    /// <summary>The performance of a branch subtree; the node must be a BRANCH.</summary>
    Task<UnitPerformanceDashboardDto> GetBranchDashboardAsync(
        Guid accountId,
        Guid branchId,
        Guid? periodId,
        CancellationToken cancellationToken = default);
}
