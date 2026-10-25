using G54.BLL.Dtos.TargetAssignment;

namespace G54.BLL.Services;

public interface ITargetAssignmentService
{
    /// <summary>Creates the ACTIVE root target of a KPI and period for a branch. Administrators only.</summary>
    Task<TargetAssignmentDto> CreateRootAsync(Guid accountId, CreateTargetAssignmentDto dto);

    /// <summary>Creates every child of <paramref name="parentAssignmentId"/> ACTIVE in one transaction.</summary>
    Task<IReadOnlyList<TargetAssignmentDto>> AllocateAsync(Guid accountId, Guid parentAssignmentId, AllocateTargetDto dto);

    Task<TargetAssignmentDto> GetAsync(Guid accountId, Guid assignmentId);
}
