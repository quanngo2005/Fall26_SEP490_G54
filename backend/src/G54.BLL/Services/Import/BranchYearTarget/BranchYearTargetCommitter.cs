using G54.DAL.Entities;
using G54.DAL.Uow;

namespace G54.BLL.Services.Import.BranchYearTarget;

/// <summary>
/// Writes a validated Branch-Year Target batch: insert a DRAFT row for every new KPI, update the target value
/// in place for an existing DRAFT row (ACTIVE rows are blocked upstream, G6/D5). A value change on a row that
/// already has quarter/department children marks those DRAFT children <c>NEEDS_REVIEW: re-imported</c> so the
/// phase-2 allocation screens can flag them, instead of deleting or migrating them (no phase-1 equivalent).
/// A KPI that has a target but is absent from the file is left untouched (section 3.5).
/// </summary>
public sealed class BranchYearTargetCommitter(IUnitOfWork uow)
{
    private const string NeedsReviewReason = "NEEDS_REVIEW: re-imported";

    /// <summary>Writes <paramref name="rows"/> (valid rows only) in the caller's transaction.</summary>
    public async Task<BranchYearTargetCommitResult> WriteAsync(
        IReadOnlyList<BranchYearTargetRow> rows,
        BranchYearTargetContext context,
        CancellationToken ct)
    {
        var created = 0;
        var updated = 0;
        var unchanged = 0;
        var needsReview = false;

        foreach (var row in rows)
        {
            var data = row;
            var kpi = context.KpiByCode[data.KpiCode!];
            var targetValue = data.YearValue!.Value;

            if (!context.CurrentByKpiId.TryGetValue(kpi.Id, out var existing))
            {
                // created_at must not be left to the DB default now(): Postgres fixes it at BEGIN, which Npgsql sends
                // lazily with the first INSERT, so it would land after AssignedAt and break ck_target_assignment_assigned_time.
                var now = DateTimeOffset.UtcNow;
                await uow.TargetAssignments.AddAsync(new TargetAssignment
                {
                    Id = Guid.NewGuid(),
                    DepartmentId = context.Branch.Id,
                    KpiDefinitionId = kpi.Id,
                    EvaluationPeriodId = context.YearPeriod.Id,
                    ParentAssignmentId = null,
                    SplitDimension = null,
                    TargetValue = targetValue,
                    Version = 1,
                    Status = TargetAssignmentStatus.Draft,
                    CreatedAt = now,
                    AssignedAt = now,
                }, ct);
                created++;
                continue;
            }

            if (existing.TargetValue == targetValue)
            {
                unchanged++;
                continue;
            }

            existing.TargetValue = targetValue;
            updated++;

            var children = await uow.TargetAssignments.GetCurrentChildrenAsync(existing.Id, ct);
            foreach (var child in children.Where(c => c.Status == TargetAssignmentStatus.Draft))
            {
                child.ChangeReason = NeedsReviewReason;
                needsReview = true;
            }
        }

        return new BranchYearTargetCommitResult(created, updated, unchanged, needsReview);
    }
}

public sealed record BranchYearTargetCommitResult(int Created, int Updated, int Unchanged, bool NeedsReview);
