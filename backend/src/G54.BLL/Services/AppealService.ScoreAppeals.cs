using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Common.Validation;
using G54.BLL.Dtos.Appeal;
using G54.DAL.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace G54.BLL.Services;

/// <summary>Appeals against an evaluation score (UC114-117): filing, withdrawing and the Branch decision.</summary>
public sealed partial class AppealService
{
    // Hardcoded on the project owner's explicit request, an exception to the no-hardcode rule (PROJECT_AND_WORKFLOW_NOTES rule 1):
    // the window is not stated in the requirements. Value and storage place are awaiting confirmation.
    private static readonly TimeSpan ScoreAppealWindow = TimeSpan.FromHours(48);

    private static readonly Action<ILogger, Guid, Guid, Guid, Exception?> LogScoreAppealFiled =
        LoggerMessage.Define<Guid, Guid, Guid>(
            LogLevel.Information,
            new EventId(4, "ScoreAppealFiled"),
            "Appeal audit: Action=FILE_SCORE, AppealId={AppealId}, EvaluationId={EvaluationId}, EmployeeId={EmployeeId}");

    private static readonly Action<ILogger, Guid, Guid, Guid, Exception?> LogScoreAppealWithdrawn =
        LoggerMessage.Define<Guid, Guid, Guid>(
            LogLevel.Information,
            new EventId(5, "ScoreAppealWithdrawn"),
            "Appeal audit: Action=WITHDRAW_SCORE, AppealId={AppealId}, EvaluationId={EvaluationId}, EmployeeId={EmployeeId}");

    private static readonly Action<ILogger, string, Guid, Guid, Guid, Exception?> LogScoreAppealDecided =
        LoggerMessage.Define<string, Guid, Guid, Guid>(
            LogLevel.Information,
            new EventId(6, "ScoreAppealDecided"),
            "Appeal audit: Action=DECIDE_SCORE, Decision={Decision}, AppealId={AppealId}, EvaluationId={EvaluationId}, ResolverId={ResolverId}");

    public async Task<AppealDetailDto> CreateScoreAppealAsync(
        Guid evaluationId,
        Guid userId,
        CreateAppealRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var caller = await ResolveCallerAsync(userId, cancellationToken);
        var reason = ValidateAppealForm(request);
        var now = time.GetUtcNow();

        // Reject before any file is written, so a refused request never touches the storage.
        // The same checks run again under the transaction below, on tracked data.
        await LoadAppealableScoreAsync(evaluationId, caller, now, track: false, cancellationToken);

        IReadOnlyList<StoredFileMetadata> uploadedFiles = [];
        if (request.Files is { Count: > 0 })
        {
            uploadedFiles = await storage.UploadEvidenceFilesAsync(request.Files, cancellationToken);
        }

        Appeal appeal;
        PerformanceEvaluation evaluation;
        try
        {
            await using var transaction = await BeginSerializableAsync(cancellationToken);
            evaluation = await LoadAppealableScoreAsync(evaluationId, caller, now, track: true, cancellationToken);
            appeal = new Appeal
            {
                Id = Guid.NewGuid(),
                EmployeeId = caller.Employee.Id,
                PerformanceEvaluationId = evaluation.Id,
                Reason = reason,
                Status = AppealStatus.Open,
                FiledAt = now,
                UpdatedAt = now,
            };
            db.Appeals.Add(appeal);
            FreezeScore(evaluation, caller.Employee.Id, reason, now);
            foreach (var file in uploadedFiles)
            {
                db.Evidences.Add(new Evidence
                {
                    Id = Guid.NewGuid(),
                    AppealId = appeal.Id,
                    FileName = file.FileName,
                    FilePath = file.FilePath,
                    MimeType = file.MimeType,
                    FileSize = file.FileSize,
                    UploadedAt = now,
                    UpdatedAt = now,
                });
            }

            await CommitSerializableAsync(transaction, ScoreChangedConcurrently, cancellationToken);
        }
        catch
        {
            // The files are already on disk; without an appeal row nothing would ever reference them.
            await storage.DeleteEvidenceFilesAsync(uploadedFiles, CancellationToken.None);
            throw;
        }

        LogScoreAppealFiled(logger, appeal.Id, evaluation.Id, caller.Employee.Id, null);

        var created = await LoadAppealAsync(appeal.Id, cancellationToken);
        return MapDetail(created!);
    }

    /// <summary>
    /// The score the caller may appeal right now, or the conflict that says why not. <paramref name="track"/> is true
    /// when the caller goes on to change it.
    /// </summary>
    private async Task<PerformanceEvaluation> LoadAppealableScoreAsync(
        Guid evaluationId,
        Caller caller,
        DateTimeOffset now,
        bool track,
        CancellationToken cancellationToken)
    {
        IQueryable<PerformanceEvaluation> query = db.PerformanceEvaluations.Include(e => e.EvaluationPeriod);
        if (!track)
        {
            query = query.AsNoTracking();
        }

        var evaluation = await query.FirstOrDefaultAsync(e => e.Id == evaluationId, cancellationToken)
            ?? throw new NotFoundException("Đánh giá hiệu suất", evaluationId);

        if (evaluation.EmployeeId != caller.Employee.Id)
        {
            throw new ForbiddenException("Chỉ nhân viên được đánh giá mới có quyền khiếu nại điểm của mình.");
        }

        PeriodLockGuard.EnsureWritable(evaluation.EvaluationPeriod);

        if (await db.Appeals.Where(InProgress).AnyAsync(a => a.PerformanceEvaluationId == evaluationId, cancellationToken))
        {
            throw AlreadyOpen();
        }

        if (await db.Appeals.AnyAsync(
                a => a.PerformanceEvaluationId == evaluationId
                    && (a.Status == AppealStatus.Accepted || a.Status == AppealStatus.Rejected),
                cancellationToken))
        {
            throw new ConflictException("Điểm đánh giá này đã có khiếu nại được xử lý.", ErrorCodes.AppealInvalidState);
        }

        if (evaluation.Status is not (PerformanceEvaluationStatus.Calculated or PerformanceEvaluationStatus.InReview)
            || evaluation.TotalScore is null
            || evaluation.CalculatedAt is not { } calculatedAt)
        {
            throw new ConflictException(
                $"Chỉ có thể khiếu nại điểm đã được tính (CALCULATED hoặc IN_REVIEW). Trạng thái hiện tại: {evaluation.Status.ToEnumString()}.",
                ErrorCodes.AppealInvalidState);
        }

        if (now - calculatedAt > ScoreAppealWindow)
        {
            throw new BusinessRuleException(
                ErrorCodes.AppealScoreWindowExpired,
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        return evaluation;
    }

    private static ConflictException ScoreChangedConcurrently() =>
        new("Điểm đánh giá vừa được thay đổi bởi thao tác khác, vui lòng thử lại.", ErrorCodes.Conflict);

    /// <summary>
    /// Decision of a score appeal. <c>ADJUST</c> adds points to the score through a ledger entry and accepts the appeal;
    /// <c>UPHOLD</c> keeps the score and rejects it. Either way the frozen score returns to its previous status.
    /// </summary>
    private async Task<AppealDecisionResultDto> DecideScoreAppealAsync(
        Guid appealId,
        Caller caller,
        string decision,
        string? noteText,
        decimal? requestedPoints,
        CancellationToken cancellationToken)
    {
        if (decision is not (AppealDecisions.Adjust or AppealDecisions.Uphold))
        {
            throw new ValidationException($"Quyết định khiếu nại điểm chỉ được là {AppealDecisions.Adjust} hoặc {AppealDecisions.Uphold}.");
        }

        var note = ValidateReason(noteText);
        decimal? points = null;
        if (decision == AppealDecisions.Adjust)
        {
            if (requestedPoints is not { } added)
            {
                throw new ValidationException("Điều chỉnh điểm phải kèm số điểm được cộng thêm.");
            }

            ValidatePoints(added);
            points = added;
        }
        else if (requestedPoints.HasValue)
        {
            throw new ValidationException($"Số điểm chỉ được gửi khi quyết định là {AppealDecisions.Adjust}.");
        }

        var granted = caller.Employee.Account is null
            ? []
            : await permissions.GetEffectivePermissionsAsync(caller.Employee.Account.Id, cancellationToken);
        var level = AccessScope.LevelFor(granted, "verification", "decide");
        if (level < AccessScope.BranchLevel)
        {
            throw new ForbiddenException("Chỉ cấp chi nhánh trở lên mới được xử lý khiếu nại điểm.");
        }

        var hierarchy = await DepartmentHierarchy.LoadAsync(db.Departments.AsNoTracking(), cancellationToken);
        // Null means every department (All scope).
        var departmentsInScope = AccessScope.DepartmentsInScope(level, caller.Employee.DepartmentId, hierarchy);
        await using var transaction = await BeginSerializableAsync(cancellationToken);
        var appeal = await db.Appeals
            .Include(a => a.Employee)
            .Include(a => a.Evidences)
            .Include(a => a.PerformanceEvaluation!).ThenInclude(e => e.EvaluationPeriod)
            .FirstOrDefaultAsync(a => a.Id == appealId && a.PerformanceEvaluationId != null, cancellationToken)
            ?? throw new NotFoundException("Khiếu nại", appealId);
        var evaluation = appeal.PerformanceEvaluation!;

        if (departmentsInScope is not null && !departmentsInScope.Contains(appeal.Employee.DepartmentId))
        {
            throw new ForbiddenException("Bạn không có quyền xử lý khiếu nại này.");
        }

        if (appeal.EmployeeId == caller.Employee.Id)
        {
            throw new BusinessRuleException(ErrorCodes.MakerCannotBeChecker, statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        if (appeal.Status is not (AppealStatus.Open or AppealStatus.UnderReview) || !evaluation.IsDisputedPending())
        {
            throw InvalidDecisionState();
        }

        PeriodLockGuard.EnsureWritable(evaluation.EvaluationPeriod);

        var now = time.GetUtcNow();
        var scoreBefore = evaluation.TotalScore;
        ScoreEntry? entry = null;
        if (points is { } change)
        {
            if (scoreBefore is null)
            {
                throw new ConflictException("Điểm đánh giá chưa có tổng điểm để điều chỉnh.", ErrorCodes.AppealInvalidState);
            }

            entry = AppealScoreLedger.Add(
                db, evaluation, change, ScoreEntryKind.Adjustment, ScoreEntrySource.Manager, note, caller.Employee.Id, now, appeal.Id);
        }

        appeal.Status = decision == AppealDecisions.Adjust ? AppealStatus.Accepted : AppealStatus.Rejected;
        appeal.Note = note;
        appeal.ResolvedBy = caller.Employee.Id;
        // Use a tracked resolver; attaching the no-tracking caller graph marks account/employee as modified.
        appeal.ResolvedByNavigation = await db.Employees.FindAsync([caller.Employee.Id], cancellationToken);
        appeal.DecidedAt = now;
        appeal.UpdatedAt = now;
        await ReleaseScoreFreezeAsync(
            appeal, evaluation, caller.Employee.Id, PerformanceEvaluationHistoryAction.AppealResolved, note, scoreBefore, now, cancellationToken);
        await CommitSerializableAsync(transaction, InvalidDecisionState, cancellationToken);

        LogScoreAppealDecided(logger, decision, appeal.Id, evaluation.Id, caller.Employee.Id, null);
        return new AppealDecisionResultDto(MapDetail(appeal), decision, null, null, null, entry?.Id);
    }

    /// <summary>Freezes the score as Disputed_Pending (BR-31) and records it in the evaluation history. Saved with the appeal.</summary>
    private void FreezeScore(PerformanceEvaluation evaluation, Guid actorId, string reason, DateTimeOffset now)
    {
        var from = evaluation.Status;
        evaluation.PreDisputeStatus = from;
        evaluation.Status = PerformanceEvaluationStatus.Disputed;
        evaluation.DisputedAt = now;
        evaluation.UpdatedAt = now;
        AddEvaluationHistory(evaluation, PerformanceEvaluationHistoryAction.Disputed, from, evaluation.TotalScore, actorId, reason, now);
    }

    /// <summary>
    /// Gives the score back its pre-dispute status unless another appeal of the same score or period is still in
    /// progress, and records the change in the evaluation history. A withdrawal that leaves the status as it was
    /// (another appeal keeps the score disputed) writes no history row. Saved with the appeal.
    /// </summary>
    private async Task ReleaseScoreFreezeAsync(
        Appeal appeal,
        PerformanceEvaluation evaluation,
        Guid actorId,
        PerformanceEvaluationHistoryAction action,
        string? comment,
        decimal? scoreBefore,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var from = evaluation.Status;
        if (evaluation.IsDisputedPending() && !await AnotherAppealInProgressAsync(appeal.Id, evaluation, cancellationToken))
        {
            evaluation.Status = evaluation.PreDisputeStatus ?? PerformanceEvaluationStatus.Calculated;
            evaluation.PreDisputeStatus = null;
            evaluation.DisputedAt = null;
        }

        evaluation.UpdatedAt = now;
        if (action == PerformanceEvaluationHistoryAction.DisputeWithdrawn && evaluation.Status == from)
        {
            return;
        }

        AddEvaluationHistory(evaluation, action, from, scoreBefore, actorId, comment, now);
    }

    /// <summary>True when the employee has another appeal in progress on this score or on a result submitted in its period.</summary>
    private Task<bool> AnotherAppealInProgressAsync(Guid appealId, PerformanceEvaluation evaluation, CancellationToken cancellationToken)
    {
        var from = StartOfBusinessDay(evaluation.EvaluationPeriod.StartDate);
        var to = StartOfBusinessDay(evaluation.EvaluationPeriod.EndDate.AddDays(1));
        return db.Appeals
            .Where(InProgress)
            .AnyAsync(
                a => a.Id != appealId
                    && a.EmployeeId == evaluation.EmployeeId
                    && (a.PerformanceEvaluationId == evaluation.Id
                        || (a.ResultSubmissionId != null
                            && a.ResultSubmission!.SubmittedAt >= from
                            && a.ResultSubmission.SubmittedAt < to)),
                cancellationToken);
    }

    private void AddEvaluationHistory(
        PerformanceEvaluation evaluation,
        PerformanceEvaluationHistoryAction action,
        PerformanceEvaluationStatus from,
        decimal? scoreBefore,
        Guid actorId,
        string? comment,
        DateTimeOffset now) =>
        db.PerformanceEvaluationHistories.Add(new PerformanceEvaluationHistory
        {
            Id = Guid.NewGuid(),
            PerformanceEvaluationId = evaluation.Id,
            Action = action,
            FromStatus = from,
            ToStatus = evaluation.Status,
            OldTotalScore = scoreBefore,
            NewTotalScore = evaluation.TotalScore,
            ChangedBy = actorId,
            Comment = comment,
            CreatedAt = now,
        });
}
