using System.Data;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Common.Models;
using G54.BLL.Common.Validation;
using G54.BLL.Common.Workflow;
using G54.BLL.Dtos.Quality;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace G54.BLL.Services;

public sealed class QualityAssuranceService(
    AppDbContext db,
    TimeProvider time,
    INotifier notifier,
    ILogger<QualityAssuranceService> logger) : IQualityAssuranceService
{
    private const int MinTextLength = 10;
    private const int MaxTextLength = 2000;
    private const int MaxPageSize = 100;

    private static readonly Action<ILogger, string, Guid, Guid, Exception?> LogAction =
        LoggerMessage.Define<string, Guid, Guid>(
            LogLevel.Information,
            new EventId(1, nameof(LogAction)),
            "QA audit: Action={Action}, TargetId={TargetId}, By={ActorId}");

    public async Task<QualityFlagDto> CreateFlagAsync(
        Guid submissionId,
        Guid userId,
        CreateQualityFlagDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var reason = RequireText(dto.Reason, "Lý do gắn cờ");
        var severity = ParseSeverity(dto.Severity);
        var caller = await ResolveCallerAsync(userId, cancellationToken);

        var submission = await db.ResultSubmissions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == submissionId, cancellationToken)
            ?? throw new NotFoundException("Hồ sơ nộp kết quả", submissionId);
        EnsureApproved(submission);
        EnsureNotSelf(submission.SubmitterEmployeeId, caller);

        if (await db.QualityFlags.AnyAsync(
                f => f.ResultSubmissionId == submissionId && f.Status == QualityFlagStatus.Open, cancellationToken))
        {
            throw new ConflictException("Kết quả này đang có cờ chất lượng chưa xử lý.");
        }

        var flag = NewFlag(submission, caller, severity, reason, null, time.GetUtcNow());
        db.QualityFlags.Add(flag);
        await db.SaveChangesAsync(cancellationToken);

        LogAction(logger, "FLAG", flag.Id, caller.Id, null);
        return await LoadFlagDtoAsync(flag.Id, cancellationToken);
    }

    public async Task<PagedResult<QualityFlagDto>> GetFlagsAsync(QualityFlagFilterDto filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var status = EnumApiFormat.ParseEnum<QualityFlagStatus>(filter.Status, "status");
        var (page, pageSize) = ClampPaging(filter.Page, filter.PageSize);

        var query = FlagQuery();
        if (status is { } s)
        {
            query = query.Where(f => f.Status == s);
        }

        if (filter.EmployeeId is { } employeeId)
        {
            query = query.Where(f => f.EmployeeId == employeeId);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(f => f.CreatedAt).ThenBy(f => f.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<QualityFlagDto>(items.Select(ToDto).ToList(), total, page, pageSize);
    }

    public Task<QualityFlagDto> GetFlagAsync(Guid flagId, CancellationToken cancellationToken = default)
        => LoadFlagDtoAsync(flagId, cancellationToken);

    public async Task<QualityFlagDto> RequestReworkAsync(
        Guid flagId,
        Guid userId,
        RequestReworkDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var comment = RequireText(dto.Comment, "Nội dung yêu cầu làm lại");
        var caller = await ResolveCallerAsync(userId, cancellationToken);

        await ExecuteSerializableAsync(async () =>
        {
            var flag = await db.QualityFlags
                .Include(f => f.ResultSubmission).ThenInclude(s => s.Work)
                .FirstOrDefaultAsync(f => f.Id == flagId, cancellationToken)
                ?? throw new NotFoundException("Cờ chất lượng", flagId);
            EnsureNotSelf(flag.EmployeeId, caller);
            EnsureOpen(flag);

            var now = time.GetUtcNow();
            await ApplyReworkAsync(flag, comment, now, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        });

        LogAction(logger, "REWORK", flagId, caller.Id, null);
        await NotifyReworkAsync(flagId, caller, cancellationToken);
        return await LoadFlagDtoAsync(flagId, cancellationToken);
    }

    public async Task<QualityFlagDto> DismissFlagAsync(Guid flagId, Guid userId, CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(userId, cancellationToken);
        var flag = await db.QualityFlags.FirstOrDefaultAsync(f => f.Id == flagId, cancellationToken)
            ?? throw new NotFoundException("Cờ chất lượng", flagId);
        EnsureNotSelf(flag.EmployeeId, caller);
        EnsureOpen(flag);

        flag.Status = QualityFlagStatus.Dismissed;
        flag.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        LogAction(logger, "DISMISS", flag.Id, caller.Id, null);
        return await LoadFlagDtoAsync(flag.Id, cancellationToken);
    }

    public async Task<PagedResult<QaSampleDto>> GetSamplesAsync(QaSampleFilterDto filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var status = EnumApiFormat.ParseEnum<QaSampleStatus>(filter.Status, "status");
        var (page, pageSize) = ClampPaging(filter.Page, filter.PageSize);

        var query = SampleQuery();
        if (status is { } s)
        {
            query = query.Where(x => x.Status == s);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.Status).ThenByDescending(x => x.SampledAt).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<QaSampleDto>(items.Select(ToDto).ToList(), total, page, pageSize);
    }

    public async Task<QaSampleDecisionDto> DecideSampleAsync(
        Guid sampleId,
        Guid userId,
        DecideQaSampleDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var result = EnumApiFormat.ParseEnum<QaSampleStatus>(dto.Result, "result");
        if (result is not (QaSampleStatus.Passed or QaSampleStatus.Failed))
        {
            throw new ValidationException("Kết quả kiểm tra chỉ được là PASSED hoặc FAILED.");
        }

        var passed = result == QaSampleStatus.Passed;
        if (passed && dto.RequestRework)
        {
            throw new ValidationException("Chỉ có thể yêu cầu làm lại khi kết quả kiểm tra là FAILED.");
        }

        var comment = passed ? dto.Comment?.Trim() : RequireText(dto.Comment, "Nhận xét kiểm tra");
        var severity = ParseSeverity(dto.Severity);
        var caller = await ResolveCallerAsync(userId, cancellationToken);

        Guid? flagId = null;
        await ExecuteSerializableAsync(async () =>
        {
            var sample = await db.QaSamples
                .Include(x => x.ResultSubmission).ThenInclude(s => s.Work)
                .FirstOrDefaultAsync(x => x.Id == sampleId, cancellationToken)
                ?? throw new NotFoundException("Mẫu kiểm tra QA", sampleId);
            if (sample.Status != QaSampleStatus.Pending)
            {
                throw new ConflictException("Mẫu kiểm tra này đã được QA xử lý.");
            }

            EnsureNotSelf(sample.SubmitterEmployeeId, caller);

            var now = time.GetUtcNow();
            sample.Status = result!.Value;
            sample.ReviewedByEmployeeId = caller.Id;
            sample.ReviewedAt = now;
            sample.Comment = comment;

            if (!passed)
            {
                EnsureApproved(sample.ResultSubmission);
                var flag = NewFlag(sample.ResultSubmission, caller, severity, comment!, sample.Id, now);
                db.QualityFlags.Add(flag);
                if (dto.RequestRework)
                {
                    await ApplyReworkAsync(flag, comment!, now, cancellationToken);
                }

                flagId = flag.Id;
            }

            await db.SaveChangesAsync(cancellationToken);
        });

        LogAction(logger, passed ? "SAMPLE_PASS" : "SAMPLE_FAIL", sampleId, caller.Id, null);
        QualityFlagDto? flagDto = null;
        if (flagId is { } id)
        {
            if (dto.RequestRework)
            {
                await NotifyReworkAsync(id, caller, cancellationToken);
            }

            flagDto = await LoadFlagDtoAsync(id, cancellationToken);
        }

        var sampleDto = ToDto(await SampleQuery().FirstAsync(x => x.Id == sampleId, cancellationToken));
        return new QaSampleDecisionDto(sampleDto, flagDto);
    }

    /// <summary>
    /// Takes the approved result back: its verified result becomes INVALID (Postgres then takes the KPI progress back out, see
    /// <c>invalidate_source_actual_progress</c>) and the work returns to IN_PROGRESS so the employee can submit it again.
    /// Refused with 409 <c>PERIOD_LOCKED</c> when any of its progress belongs to a locked period.
    /// </summary>
    private async Task ApplyReworkAsync(QualityFlag flag, string comment, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var submission = flag.ResultSubmission;
        EnsureApproved(submission);

        var results = await db.VerifiedWorkResults
            .Include(r => r.WorkResultItems).ThenInclude(i => i.ActualProgress)
                .ThenInclude(p => p.TargetAssignment).ThenInclude(t => t.EvaluationPeriod)
            .Where(r => r.Verification!.ResultSubmissionId == submission.Id && r.Status == VerifiedResultStatus.Valid)
            .ToListAsync(cancellationToken);
        if (results.Count == 0)
        {
            throw new ConflictException("Kết quả này không còn hiệu lực để yêu cầu làm lại.", ErrorCodes.VerificationInvalidState);
        }

        var work = submission.Work;
        if (!WorkStateMachine.TryGetNextStatus(work.Status, WorkAction.Rework, out var nextWorkStatus))
        {
            throw new ConflictException(
                $"Công việc đang ở trạng thái {work.Status.ToEnumString()}, không thể yêu cầu làm lại.",
                ErrorCodes.VerificationInvalidState);
        }

        foreach (var item in results.SelectMany(r => r.WorkResultItems))
        {
            PeriodLockGuard.EnsureWritable(item.ActualProgress.TargetAssignment.EvaluationPeriod);
        }

        foreach (var result in results)
        {
            result.Status = VerifiedResultStatus.Invalid;
            result.InvalidatedAt = now;
            result.UpdatedAt = now;
        }

        work.Status = nextWorkStatus;
        work.UpdatedAt = now;
        flag.Status = QualityFlagStatus.ReworkRequested;
        flag.ReworkComment = comment;
        flag.ReworkRequestedAt = now;
        flag.UpdatedAt = now;
    }

    private async Task NotifyReworkAsync(Guid flagId, Employee caller, CancellationToken cancellationToken)
    {
        var flag = await FlagQuery().FirstAsync(f => f.Id == flagId, cancellationToken);
        var work = flag.ResultSubmission.Work;
        var body = $"QA yêu cầu làm lại công việc {work.Code} - {work.Title}: {flag.ReworkComment}";
        var recipients = new[] { flag.EmployeeId, work.AssigneeEmployeeId }.Distinct();
        var drafts = recipients
            .Select(id => new NotificationDraft(
                id,
                NotificationType.ReworkRequested,
                "Yêu cầu làm lại công việc",
                body.Length > 2000 ? body[..2000] : body,
                NotificationSeverity.Important,
                NotificationRelatedType.Work,
                work.Id,
                caller.Id))
            .ToList();
        await notifier.TryPublishAsync(drafts, cancellationToken);
    }

    private async Task ExecuteSerializableAsync(Func<Task> action)
    {
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable)
            : null;
        try
        {
            await action();
            if (transaction is not null)
            {
                await transaction.CommitAsync();
            }
        }
        catch (Exception ex) when (PostgresConflict.IsSerializationFailure(ex))
        {
            throw new ConflictException("Dữ liệu đã được xử lý đồng thời. Vui lòng tải lại.", ErrorCodes.VerificationInvalidState);
        }
    }

    private static QualityFlag NewFlag(
        ResultSubmission submission,
        Employee caller,
        QualityFlagSeverity severity,
        string reason,
        Guid? sampleId,
        DateTimeOffset now) => new()
        {
            Id = Guid.NewGuid(),
            ResultSubmissionId = submission.Id,
            EmployeeId = submission.SubmitterEmployeeId,
            FlaggedByEmployeeId = caller.Id,
            QaSampleId = sampleId,
            Severity = severity,
            Status = QualityFlagStatus.Open,
            Reason = reason,
            CreatedAt = now,
            UpdatedAt = now,
        };

    private IQueryable<QualityFlag> FlagQuery() => db.QualityFlags.AsNoTracking()
        .Include(f => f.ResultSubmission).ThenInclude(s => s.Work)
        .Include(f => f.Employee)
        .Include(f => f.FlaggedByEmployee);

    private IQueryable<QaSample> SampleQuery() => db.QaSamples.AsNoTracking()
        .Include(x => x.ResultSubmission).ThenInclude(s => s.Work)
        .Include(x => x.SubmitterEmployee)
        .Include(x => x.ReviewedByEmployee)
        .Include(x => x.QualityFlag);

    private async Task<QualityFlagDto> LoadFlagDtoAsync(Guid flagId, CancellationToken cancellationToken)
        => ToDto(await FlagQuery().FirstOrDefaultAsync(f => f.Id == flagId, cancellationToken)
            ?? throw new NotFoundException("Cờ chất lượng", flagId));

    private async Task<Employee> ResolveCallerAsync(Guid userId, CancellationToken cancellationToken)
    {
        var caller = await db.Employees.AsNoTracking()
            .Include(e => e.Account)
            .FirstOrDefaultAsync(e => e.Id == userId || (e.Account != null && e.Account.Id == userId), cancellationToken);
        if (caller is null || caller.Status != EmployeeStatus.Active || caller.Account is { IsLocked: true })
        {
            throw new UnauthorizedException("Không tìm thấy thông tin nhân viên hợp lệ hoặc tài khoản đã bị khóa.");
        }

        return caller;
    }

    private static void EnsureApproved(ResultSubmission submission)
    {
        if (submission.Status != SubmissionStatus.Approved)
        {
            throw new ConflictException(
                $"Chỉ kết quả đã duyệt (APPROVED) mới được kiểm tra chất lượng. Trạng thái hiện tại: {submission.Status.ToEnumString()}.",
                ErrorCodes.VerificationInvalidState);
        }
    }

    private static void EnsureOpen(QualityFlag flag)
    {
        if (flag.Status != QualityFlagStatus.Open)
        {
            throw new ConflictException(
                $"Cờ chất lượng đang ở trạng thái {flag.Status.ToEnumString()}, không thể xử lý.",
                ErrorCodes.VerificationInvalidState);
        }
    }

    private static void EnsureNotSelf(Guid subjectEmployeeId, Employee caller)
    {
        if (subjectEmployeeId == caller.Id)
        {
            throw new ForbiddenException("Bạn không thể kiểm tra chất lượng kết quả của chính mình.");
        }
    }

    private static string RequireText(string? value, string label)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length < MinTextLength || text.Length > MaxTextLength)
        {
            throw new ValidationException($"{label} là bắt buộc, từ {MinTextLength} đến {MaxTextLength} ký tự.");
        }

        return text;
    }

    private static QualityFlagSeverity ParseSeverity(string? value)
        => EnumApiFormat.ParseEnum<QualityFlagSeverity>(value, "severity") ?? QualityFlagSeverity.Medium;

    private static (int Page, int PageSize) ClampPaging(int page, int pageSize)
        => (Math.Max(page, 1), Math.Clamp(pageSize, 1, MaxPageSize));

    private static QualityFlagDto ToDto(QualityFlag f) => new(
        f.Id,
        f.ResultSubmissionId,
        f.ResultSubmission.SubmissionNo,
        f.ResultSubmission.WorkId,
        f.ResultSubmission.Work.Code,
        f.ResultSubmission.Work.Title,
        f.EmployeeId,
        f.Employee.EmployeeCode,
        f.Employee.FullName,
        f.FlaggedByEmployeeId,
        f.FlaggedByEmployee.FullName,
        f.QaSampleId,
        f.Severity.ToEnumString(),
        f.Reason,
        f.Status.ToEnumString(),
        f.ReworkComment,
        f.ReworkRequestedAt,
        f.CreatedAt);

    private static QaSampleDto ToDto(QaSample x) => new(
        x.Id,
        x.ResultSubmissionId,
        x.ResultSubmission.SubmissionNo,
        x.ResultSubmission.WorkId,
        x.ResultSubmission.Work.Code,
        x.ResultSubmission.Work.Title,
        x.SubmitterEmployeeId,
        x.SubmitterEmployee.EmployeeCode,
        x.SubmitterEmployee.FullName,
        x.Status.ToEnumString(),
        x.SampledAt,
        x.ReviewedByEmployeeId,
        x.ReviewedByEmployee?.FullName,
        x.ReviewedAt,
        x.Comment,
        x.QualityFlag?.Id);
}
