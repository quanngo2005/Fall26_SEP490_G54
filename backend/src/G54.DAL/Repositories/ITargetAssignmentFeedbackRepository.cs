using G54.DAL.Entities;

namespace G54.DAL.Repositories;

public interface ITargetAssignmentFeedbackRepository : IRepository<TargetAssignmentFeedback>
{
    /// <summary>Latest feedback row for a target (any type), or null. No tracking.</summary>
    Task<TargetAssignmentFeedback?> GetLatestAsync(Guid targetAssignmentId, CancellationToken ct = default);

    /// <summary>All feedback for a target, newest first. No tracking.</summary>
    Task<IReadOnlyList<TargetAssignmentFeedback>> ListForTargetAsync(Guid targetAssignmentId, CancellationToken ct = default);

    /// <summary>Latest feedback per target, for the given targets (used to compute feedbackStatus in bulk).</summary>
    Task<IReadOnlyDictionary<Guid, TargetAssignmentFeedback>> GetLatestForTargetsAsync(IReadOnlyCollection<Guid> targetAssignmentIds, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
