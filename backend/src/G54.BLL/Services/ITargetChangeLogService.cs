using G54.DAL.Entities;

namespace G54.BLL.Services;

/// <summary>
/// Structured, queryable log of target value changes (Target Allocation v3, D10) — beyond the generic
/// <see cref="AuditLog"/>. Written by tier-3 save/publish (manual edits, publish) and by the employee
/// "my-targets" feedback/reply flow. Read back by <see cref="IMyTargetsService"/> to show an employee which
/// KPIs changed (old/new value) since their last confirmation.
/// </summary>
public interface ITargetChangeLogService
{
    /// <summary>Appends one change-log entry. Never throws for business reasons; this is a side-record, not a
    /// validation gate.</summary>
    Task LogAsync(
        Guid actorId,
        string action,
        int tier,
        Guid kpiId,
        string unit,
        string period,
        decimal? oldValue,
        decimal? newValue,
        string? basis = null,
        Guid? employeeId = null,
        Guid? departmentId = null,
        int? year = null,
        decimal? roundingStep = null,
        CancellationToken cancellationToken = default);

    /// <summary>Change-log rows for one employee's year, newest first.</summary>
    Task<IReadOnlyList<TargetChangeLog>> GetByEmployeeYearAsync(
        Guid employeeId, int year, CancellationToken cancellationToken = default);
}
