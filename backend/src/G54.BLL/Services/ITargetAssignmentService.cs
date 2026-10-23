using G54.BLL.Dtos.TargetAssignment;

namespace G54.BLL.Services;

public interface ITargetAssignmentService
{
    /// <summary>Creates the ACTIVE root target of a KPI and period for a branch. Administrators only.</summary>
    Task<TargetAssignmentDto> CreateRootAsync(
        Guid accountId,
        CreateTargetAssignmentDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Splits an ACTIVE department target across the level directly below it (child departments, or employees of a
    /// department/team). Creates every child ACTIVE in one transaction. For a SUM KPI the children must total the parent.
    /// </summary>
    Task<TargetAllocationResultDto> AllocateAsync(
        Guid accountId,
        Guid parentAssignmentId,
        AllocateTargetDto dto,
        CancellationToken cancellationToken = default);

    Task<TargetAssignmentDto> GetAsync(
        Guid accountId,
        Guid assignmentId,
        CancellationToken cancellationToken = default);
}
