using System.Data;
using System.Linq.Expressions;
using G54.BLL.Common;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Appeal;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace G54.BLL.Services;

public sealed class AppealService(
    AppDbContext db,
    IPermissionService permissions,
    IStorageService storage,
    ILogger<AppealService> logger,
    TimeProvider time,
    IOptions<SystemParametersOptions> parameters) : IAppealService
{
    private const int MaxReasonLength = 2000;
    private const int MaxFileCount = 10;
    private static readonly int DepartmentScopeLevel = PermissionScopes.GetScopeLevel(PermissionScopes.Dept);
    private static readonly int BranchScopeLevel = PermissionScopes.GetScopeLevel(PermissionScopes.Branch);
    private static readonly int AllScopeLevel = PermissionScopes.GetScopeLevel(PermissionScopes.All);

    /// <summary>An appeal still awaiting a decision (OPEN or UNDER_REVIEW); WITHDRAWN and decided ones are not.</summary>
    private static readonly Expression<Func<Appeal, bool>> InProgress =
        a => a.Status == AppealStatus.Open || a.Status == AppealStatus.UnderReview;

    private static readonly Action<ILogger, string, Guid, Guid, Exception?> LogAppealDecided =
        LoggerMessage.Define<string, Guid, Guid>(
            LogLevel.Information,
            new EventId(3, "AppealDecided"),
            "Appeal audit: Decision={Decision}, AppealId={AppealId}, ResolverId={ResolverId}");

    public async Task<AppealDecisionResultDto> DecideAppealAsync(
        Guid appealId,
        Guid userId,
        DecideAppealRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var decision = request.Decision?.Trim().ToUpperInvariant();
        var note = request.Note?.Trim();
        if (decision is not ("ACCEPT" or "ADJUST" or "REJECT"))
        {
            throw new ValidationException("Quyết định khiếu nại chỉ được là ACCEPT, ADJUST hoặc REJECT.");
        }

        if (note?.Length > MaxReasonLength
            || (decision is "ADJUST" or "REJECT" && (string.IsNullOrEmpty(note) || note.Length < 10)))
        {
            throw new ValidationException("Ghi chú tối đa 2000 ký tự; yêu cầu bổ sung hoặc từ chối phải có lý do tối thiểu 10 ký tự.");
        }

        if (request.VerifiedValue is < 0 || (decision != "ACCEPT" && request.VerifiedValue.HasValue))
        {
            throw new ValidationException("Sản lượng công nhận phải không âm và chỉ được gửi khi ACCEPT.");
        }

        var caller = await ResolveCallerAsync(userId, cancellationToken);
        var visibility = await ResolveVisibilityAsync(caller, cancellationToken);
        // Serializable isolation prevents competing appeal decisions from both committing.
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        var appeal = await db.Appeals
            .Include(a => a.Employee)
            .Include(a => a.Evidences)
            .Include(a => a.ResultSubmission!)
                .ThenInclude(s => s.SubmitterEmployee)
            .Include(a => a.ResultSubmission!)
                .ThenInclude(s => s.Verifications)
            .Include(a => a.ResultSubmission!)
                .ThenInclude(s => s.Work)
                    .ThenInclude(w => w.WorkType)
                        .ThenInclude(wt => wt!.WorkTypeKpis)
            .FirstOrDefaultAsync(a => a.Id == appealId && a.ResultSubmissionId != null, cancellationToken)
            ?? throw new NotFoundException("Khiếu nại", appealId);
        var submission = appeal.ResultSubmission!;
        if (!visibility.SeesAll
            && (!visibility.DepartmentIds.Contains(appeal.Employee.DepartmentId)
                || !visibility.DepartmentIds.Contains(submission.SubmitterEmployee.DepartmentId)))
        {
            throw new ForbiddenException("Bạn không có quyền xử lý khiếu nại này.");
        }

        if (appeal.EmployeeId == caller.Employee.Id || submission.SubmitterEmployeeId == caller.Employee.Id)
        {
            throw new BusinessRuleException(ErrorCodes.MakerCannotBeChecker, statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        if (appeal.Status is not (AppealStatus.Open or AppealStatus.UnderReview)
            || submission.Status != SubmissionStatus.Rejected
            || submission.Work.Status != WorkStatus.Rejected
            || await db.ResultSubmissions.AnyAsync(
                s => s.WorkId == submission.WorkId && s.SubmissionNo > submission.SubmissionNo, cancellationToken))
        {
            throw InvalidDecisionState();
        }

        var now = time.GetUtcNow();
        Verification? verification = null;
        try
        {
            if (decision != "REJECT")
            {
                verification = new Verification
                {
                    Id = Guid.NewGuid(),
                    ResultSubmissionId = submission.Id,
                    VerifierEmployeeId = caller.Employee.Id,
                    RoundNo = submission.Verifications.Select(v => v.RoundNo).DefaultIfEmpty(0).Max() + 1,
                    Decision = decision == "ACCEPT" ? VerificationDecision.Approved : VerificationDecision.NeedMoreInfo,
                    Comment = string.IsNullOrEmpty(note) ? null : note,
                    VerifiedAt = now,
                    UpdatedAt = now,
                };
                if (decision == "ACCEPT")
                {
                    await VerifiedResultRecorder.RecordAsync(
                        db, logger, time, verification, submission.Work, submission.SubmitterEmployeeId, request.VerifiedValue, now, cancellationToken);
                }

                db.Verifications.Add(verification);
                // The lifecycle trigger rejects new verifications after a submission is APPROVED.
                // Persist the verification first, inside the same transaction as the state changes.
                if (transaction is not null)
                {
                    await db.SaveChangesAsync(cancellationToken);
                }

                submission.Status = decision == "ACCEPT" ? SubmissionStatus.Approved : SubmissionStatus.UnderReview;
                submission.Work.Status = decision == "ACCEPT" ? WorkStatus.Verified : WorkStatus.InProgress;
                submission.UpdatedAt = now;
                submission.Work.UpdatedAt = now;
            }

            appeal.Status = decision == "REJECT" ? AppealStatus.Rejected : AppealStatus.Accepted;
            appeal.Note = string.IsNullOrEmpty(note) ? null : note;
            appeal.ResolvedBy = caller.Employee.Id;
            // Use a tracked resolver; attaching the no-tracking caller graph marks account/employee as modified.
            appeal.ResolvedByNavigation = await db.Employees.FindAsync([caller.Employee.Id], cancellationToken);
            appeal.DecidedAt = now;
            appeal.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (PostgresConflict.IsSerializationFailure(ex))
        {
            throw InvalidDecisionState();
        }

        LogAppealDecided(logger, decision, appeal.Id, caller.Employee.Id, null);
        return new AppealDecisionResultDto(
            MapDetail(appeal), decision, submission.Status.ToEnumString(), submission.Work.Status.ToEnumString(), verification?.Id);
    }

    private static ConflictException InvalidDecisionState() =>
        new("Khiếu nại hoặc hồ sơ không còn ở trạng thái cho phép xử lý.", ErrorCodes.AppealInvalidState);

    private static readonly Action<ILogger, Guid, Guid, Guid, Exception?> LogAppealFiled =
        LoggerMessage.Define<Guid, Guid, Guid>(
            LogLevel.Information,
            new EventId(1, "AppealFiled"),
            "Appeal audit: Action=FILE, AppealId={AppealId}, SubmissionId={SubmissionId}, EmployeeId={EmployeeId}");

    private static readonly Action<ILogger, Guid, Guid, Guid, Exception?> LogAppealWithdrawn =
        LoggerMessage.Define<Guid, Guid, Guid>(
            LogLevel.Information,
            new EventId(2, "AppealWithdrawn"),
            "Appeal audit: Action=WITHDRAW, AppealId={AppealId}, SubmissionId={SubmissionId}, EmployeeId={EmployeeId}");

    public async Task<AppealDetailDto> CreateAppealAsync(
        Guid submissionId,
        Guid userId,
        CreateAppealRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var caller = await ResolveCallerAsync(userId, cancellationToken);

        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason))
        {
            throw new ValidationException("Lý do khiếu nại là bắt buộc.");
        }

        if (reason.Length > MaxReasonLength)
        {
            throw new ValidationException($"Lý do khiếu nại tối đa {MaxReasonLength} ký tự.");
        }

        if (request.Files is { Count: > MaxFileCount })
        {
            throw new ValidationException($"Số lượng tệp đính kèm tối đa là {MaxFileCount} tệp.");
        }

        var submission = await db.ResultSubmissions.AsNoTracking()
            .Include(s => s.Verifications)
            .FirstOrDefaultAsync(s => s.Id == submissionId, cancellationToken)
            ?? throw new NotFoundException("Đợt nộp kết quả", submissionId);

        if (submission.SubmitterEmployeeId != caller.Employee.Id)
        {
            throw new ForbiddenException("Chỉ người đã nộp kết quả mới có quyền khiếu nại.");
        }

        if (submission.Status != SubmissionStatus.Rejected)
        {
            throw new ConflictException(
                $"Chỉ có thể khiếu nại kết quả đã bị từ chối (REJECTED). Trạng thái hiện tại: {submission.Status.ToEnumString()}.",
                ErrorCodes.AppealInvalidState);
        }

        var now = time.GetUtcNow();
        var rejectedAt = submission.Verifications
            .Where(v => v.Decision == VerificationDecision.Rejected)
            .Max(v => v.VerifiedAt)
            ?? submission.UpdatedAt;
        var appealWindowHours = parameters.Value.AppealRejectedHours;
        var appealWindow = TimeSpan.FromHours(appealWindowHours);
        if (now - rejectedAt > appealWindow)
        {
            throw new BusinessRuleException(
                ErrorCodes.AppealWindowExpired,
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        if (await HasAppealInProgressAsync(submissionId, cancellationToken))
        {
            throw AlreadyOpen();
        }

        IReadOnlyList<StoredFileMetadata> uploadedFiles = Array.Empty<StoredFileMetadata>();
        if (request.Files is { Count: > 0 })
        {
            uploadedFiles = await storage.UploadEvidenceFilesAsync(request.Files, cancellationToken);
        }

        var appeal = new Appeal
        {
            Id = Guid.NewGuid(),
            EmployeeId = caller.Employee.Id,
            ResultSubmissionId = submissionId,
            Reason = reason,
            Status = AppealStatus.Open,
            FiledAt = now,
            UpdatedAt = now,
        };
        db.Appeals.Add(appeal);

        // BR-31: the score of the period this result belongs to is frozen as Disputed_Pending until the appeal ends.
        await MarkDisputedPendingAsync(caller.Employee.Id, submission.SubmittedAt, now, cancellationToken);

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

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // The files are already on disk; without an appeal row nothing would ever reference them.
            await storage.DeleteEvidenceFilesAsync(uploadedFiles, CancellationToken.None);
            throw;
        }

        LogAppealFiled(logger, appeal.Id, submissionId, caller.Employee.Id, null);

        var created = await LoadAppealAsync(appeal.Id, cancellationToken);
        return MapDetail(created!);
    }

    public async Task<AppealDetailDto> WithdrawAppealAsync(
        Guid appealId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(userId, cancellationToken);

        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        var appeal = await db.Appeals
            .Include(a => a.Employee)
            .Include(a => a.ResolvedByNavigation)
            .Include(a => a.ResultSubmission!)
                .ThenInclude(s => s.Work)
            .Include(a => a.Evidences)
            .FirstOrDefaultAsync(a => a.Id == appealId && a.ResultSubmissionId != null, cancellationToken)
            ?? throw new NotFoundException("Khiếu nại", appealId);

        if (appeal.EmployeeId != caller.Employee.Id)
        {
            throw new ForbiddenException("Chỉ người đã gửi khiếu nại mới có quyền rút khiếu nại.");
        }

        if (appeal.Status != AppealStatus.Open)
        {
            throw new ConflictException(
                $"Chỉ có thể rút khiếu nại đang mở (OPEN). Trạng thái hiện tại: {appeal.Status.ToEnumString()}.",
                ErrorCodes.AppealInvalidState);
        }

        var now = time.GetUtcNow();
        appeal.Status = AppealStatus.Withdrawn;
        appeal.UpdatedAt = now;

        // BR-31: give the score back its previous status unless another appeal of the period is still in progress.
        await ReleaseDisputedPendingAsync(appeal.Id, appeal.EmployeeId, appeal.ResultSubmission!.SubmittedAt, now, cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (PostgresConflict.IsSerializationFailure(ex))
        {
            throw InvalidDecisionState();
        }

        LogAppealWithdrawn(logger, appeal.Id, appeal.ResultSubmissionId!.Value, caller.Employee.Id, null);

        return MapDetail(appeal);
    }

    public async Task<PagedResult<AppealListItemDto>> GetAppealsAsync(
        Guid userId,
        AppealFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var caller = await ResolveCallerAsync(userId, cancellationToken);
        var status = EnumApiFormat.ParseEnum<AppealStatus>(filter.Status, "status");
        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var visibility = await ResolveVisibilityAsync(caller, cancellationToken);

        var query = db.Appeals.AsNoTracking().Where(a => a.ResultSubmissionId != null);
        if (status.HasValue)
        {
            query = query.Where(a => a.Status == status.Value);
        }

        if (!visibility.SeesAll)
        {
            var ownId = caller.Employee.Id;
            var departmentIds = visibility.DepartmentIds;
            query = query.Where(a => a.EmployeeId == ownId || departmentIds.Contains(a.Employee.DepartmentId));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(a => a.FiledAt)
            .ThenBy(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new
            {
                a.Id,
                ResultSubmissionId = a.ResultSubmissionId!.Value,
                a.ResultSubmission!.WorkId,
                WorkCode = a.ResultSubmission.Work.Code,
                WorkTitle = a.ResultSubmission.Work.Title,
                a.ResultSubmission.SubmissionNo,
                a.EmployeeId,
                a.Employee.EmployeeCode,
                EmployeeName = a.Employee.FullName,
                a.Status,
                a.FiledAt,
                a.DecidedAt,
                a.UpdatedAt,
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r => new AppealListItemDto(
                r.Id,
                r.ResultSubmissionId,
                r.WorkId,
                r.WorkCode,
                r.WorkTitle,
                r.SubmissionNo,
                r.EmployeeId,
                r.EmployeeCode,
                r.EmployeeName,
                r.Status.ToEnumString(),
                r.FiledAt,
                r.DecidedAt,
                r.UpdatedAt))
            .ToList();

        return new PagedResult<AppealListItemDto>(items, totalCount, page, pageSize);
    }

    public async Task<AppealDetailDto> GetAppealByIdAsync(
        Guid appealId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(userId, cancellationToken);

        var appeal = await LoadAppealAsync(appealId, cancellationToken)
            ?? throw new NotFoundException("Khiếu nại", appealId);

        var visibility = await ResolveVisibilityAsync(caller, cancellationToken);
        var canSee = visibility.SeesAll
            || appeal.EmployeeId == caller.Employee.Id
            || visibility.DepartmentIds.Contains(appeal.Employee.DepartmentId);
        if (!canSee)
        {
            throw new ForbiddenException("Bạn không có quyền xem khiếu nại này.");
        }

        return MapDetail(appeal);
    }

    /// <summary>
    /// BR-31: marks every disputable evaluation of the employee whose period covers the submission date as
    /// <c>DISPUTED</c> (Disputed_Pending). Drafts carry no score and FINAL / locked-period scores cannot change.
    /// Only changes tracked entities; the caller saves them together with the appeal.
    /// </summary>
    private async Task MarkDisputedPendingAsync(
        Guid employeeId,
        DateTimeOffset submittedAt,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var evaluations = await LoadEvaluationsOfPeriodAsync(employeeId, submittedAt, cancellationToken);
        foreach (var evaluation in evaluations)
        {
            if (evaluation.Status is not (PerformanceEvaluationStatus.Calculated
                    or PerformanceEvaluationStatus.InReview
                    or PerformanceEvaluationStatus.Approved)
                || evaluation.EvaluationPeriod.Status is PeriodStatus.Locked or PeriodStatus.Closed)
            {
                continue;
            }

            evaluation.PreDisputeStatus = evaluation.Status;
            evaluation.Status = PerformanceEvaluationStatus.Disputed;
            evaluation.DisputedAt = now;
            evaluation.UpdatedAt = now;
        }
    }

    /// <summary>
    /// BR-31: restores the pre-dispute status of the employee's disputed evaluations that have no other appeal in
    /// progress. <paramref name="appealId"/> is the appeal being closed and is ignored when looking for others.
    /// </summary>
    private async Task ReleaseDisputedPendingAsync(
        Guid appealId,
        Guid employeeId,
        DateTimeOffset submittedAt,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var evaluations = await LoadEvaluationsOfPeriodAsync(employeeId, submittedAt, cancellationToken);
        foreach (var evaluation in evaluations.Where(e => e.IsDisputedPending()))
        {
            var period = evaluation.EvaluationPeriod;
            var from = StartOfBusinessDay(period.StartDate);
            var to = StartOfBusinessDay(period.EndDate.AddDays(1));

            var anotherInProgress = await db.Appeals
                .Where(InProgress)
                .AnyAsync(
                    a => a.Id != appealId
                        && a.EmployeeId == employeeId
                        && a.ResultSubmissionId != null
                        && a.ResultSubmission!.SubmittedAt >= from
                        && a.ResultSubmission.SubmittedAt < to,
                    cancellationToken);
            if (anotherInProgress)
            {
                continue;
            }

            evaluation.Status = evaluation.PreDisputeStatus ?? PerformanceEvaluationStatus.Calculated;
            evaluation.PreDisputeStatus = null;
            evaluation.DisputedAt = null;
            evaluation.UpdatedAt = now;
        }
    }

    /// <summary>The employee's evaluations whose period contains the submission date (UTC+7 calendar day).</summary>
    private Task<List<PerformanceEvaluation>> LoadEvaluationsOfPeriodAsync(
        Guid employeeId,
        DateTimeOffset submittedAt,
        CancellationToken cancellationToken)
    {
        var date = DateOnly.FromDateTime(submittedAt.ToOffset(TimeProviderExtensions.BusinessUtcOffset).DateTime);
        return db.PerformanceEvaluations
            .Include(e => e.EvaluationPeriod)
            .Where(e => e.EmployeeId == employeeId
                && e.EvaluationPeriod.StartDate <= date
                && e.EvaluationPeriod.EndDate >= date)
            .ToListAsync(cancellationToken);
    }

    private static DateTimeOffset StartOfBusinessDay(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue), TimeProviderExtensions.BusinessUtcOffset);
    private static ConflictException AlreadyOpen()
        => new(ErrorCodes.GetDefaultMessage(ErrorCodes.AppealAlreadyOpen), ErrorCodes.AppealAlreadyOpen);

    private Task<bool> HasAppealInProgressAsync(Guid submissionId, CancellationToken cancellationToken)
        => db.Appeals
            .Where(InProgress)
            .AnyAsync(a => a.ResultSubmissionId == submissionId, cancellationToken);

    private Task<Appeal?> LoadAppealAsync(Guid appealId, CancellationToken cancellationToken)
        => db.Appeals.AsNoTracking()
            .Include(a => a.Employee)
            .Include(a => a.ResolvedByNavigation)
            .Include(a => a.ResultSubmission!)
                .ThenInclude(s => s.Work)
            .Include(a => a.Evidences)
            .FirstOrDefaultAsync(a => a.Id == appealId && a.ResultSubmissionId != null, cancellationToken);

    private static AppealDetailDto MapDetail(Appeal appeal)
    {
        var submission = appeal.ResultSubmission!;
        var evidences = appeal.Evidences
            .OrderBy(e => e.UploadedAt)
            .ThenBy(e => e.FileName, StringComparer.Ordinal)
            .Select(e => new AppealEvidenceDto(
                e.Id,
                e.FileName,
                e.FileSize,
                e.MimeType,
                $"/evidence/{e.Id}/preview",
                $"/evidence/{e.Id}/download"))
            .ToList();

        return new AppealDetailDto(
            appeal.Id,
            submission.Id,
            submission.WorkId,
            submission.Work.Code,
            submission.Work.Title,
            submission.SubmissionNo,
            appeal.EmployeeId,
            appeal.Employee.EmployeeCode,
            appeal.Employee.FullName,
            appeal.Status.ToEnumString(),
            appeal.FiledAt,
            appeal.DecidedAt,
            appeal.UpdatedAt,
            appeal.Reason,
            appeal.Note,
            appeal.ResolvedBy,
            appeal.ResolvedByNavigation?.FullName,
            evidences);
    }

    private async Task<Caller> ResolveCallerAsync(Guid userId, CancellationToken cancellationToken)
    {
        var employee = await db.Employees.AsNoTracking()
            .Include(e => e.Account)
            .FirstOrDefaultAsync(e => e.Id == userId || (e.Account != null && e.Account.Id == userId), cancellationToken);

        if (employee is null || employee.Status != EmployeeStatus.Active || employee.Account is { IsLocked: true })
        {
            throw new UnauthorizedException("Không tìm thấy thông tin nhân viên hợp lệ hoặc tài khoản đã bị khóa.");
        }

        return new Caller(employee);
    }

    /// <summary>
    /// What the caller may see: always their own appeals, plus those of employees inside the department scope of their
    /// <c>verification:decide</c> permission (Dept = own department tree, Branch = nearest branch tree, All = everyone).
    /// </summary>
    private async Task<Visibility> ResolveVisibilityAsync(Caller caller, CancellationToken cancellationToken)
    {
        var employee = caller.Employee;
        if (employee.Account is null)
        {
            return Visibility.OwnOnly;
        }

        var granted = await permissions.GetEffectivePermissionsAsync(employee.Account.Id, cancellationToken);
        var level = granted
            .Where(p => (p.Resource == "*" && p.Action == "*")
                || (string.Equals(p.Resource, "verification", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(p.Action, "decide", StringComparison.OrdinalIgnoreCase)))
            .Select(p => PermissionScopes.GetScopeLevel(p.Scope))
            .DefaultIfEmpty(0)
            .Max();

        if (level >= AllScopeLevel)
        {
            return Visibility.Everything;
        }

        if (level < DepartmentScopeLevel)
        {
            return Visibility.OwnOnly;
        }

        var departments = await db.Departments.AsNoTracking()
            .ToDictionaryAsync(d => d.Id, d => new DepartmentLink(d.ParentDepartmentId, d.LevelType), cancellationToken);
        if (!departments.ContainsKey(employee.DepartmentId))
        {
            return Visibility.OwnOnly;
        }

        var rootId = level >= BranchScopeLevel ? BranchRoot(departments, employee.DepartmentId) : employee.DepartmentId;
        var inScope = departments.Keys
            .Where(departmentId => IsSelfOrDescendant(departments, departmentId, rootId))
            .ToList();
        return new Visibility(false, inScope);
    }

    /// <summary>Nearest BRANCH ancestor-or-self; the root-most ancestor when none exists.</summary>
    private static Guid BranchRoot(Dictionary<Guid, DepartmentLink> departments, Guid departmentId)
    {
        var current = departmentId;
        var seen = new HashSet<Guid>();
        while (seen.Add(current))
        {
            var link = departments[current];
            if (link.Level == DepartmentLevelType.Branch
                || link.ParentId is not { } parent
                || !departments.ContainsKey(parent))
            {
                return current;
            }

            current = parent;
        }

        return current;
    }

    /// <summary>True when <paramref name="rootId"/> is on the path from <paramref name="departmentId"/> up to the top (cycle-safe).</summary>
    private static bool IsSelfOrDescendant(Dictionary<Guid, DepartmentLink> departments, Guid departmentId, Guid rootId)
    {
        var current = departmentId;
        var seen = new HashSet<Guid>();
        while (seen.Add(current))
        {
            if (current == rootId)
            {
                return true;
            }

            if (!departments.TryGetValue(current, out var link) || link.ParentId is not { } parent)
            {
                return false;
            }

            current = parent;
        }

        return false;
    }

    private sealed record Caller(Employee Employee);

    private sealed record Visibility(bool SeesAll, IReadOnlyList<Guid> DepartmentIds)
    {
        public static Visibility Everything { get; } = new(true, Array.Empty<Guid>());

        public static Visibility OwnOnly { get; } = new(false, Array.Empty<Guid>());
    }

    private readonly record struct DepartmentLink(Guid? ParentId, DepartmentLevelType Level);
}
