using System.Data;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Dtos.Appeal;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace G54.BLL.Services;

public sealed partial class AppealService
{
    public async Task<ScoreEntryDto> ReviewAppealAsync(Guid appealId, Guid userId, ReviewAppealRequestDto request, CancellationToken cancellationToken = default)
    {
        var reason = ValidateScoreRequest(request);
        var caller = await ResolveCallerAsync(userId, cancellationToken);
        // Read-only access is not enough: a reviewer must cover the filer and the submission owner.
        await using var transaction = await BeginSerializableAsync(cancellationToken);
        var appeal = await db.Appeals.Include(a => a.Employee).Include(a => a.ResultSubmission!).ThenInclude(s => s.SubmitterEmployee)
            .SingleOrDefaultAsync(a => a.Id == appealId && a.ResultSubmissionId != null, cancellationToken)
            ?? throw new NotFoundException("Khiếu nại", appealId);
        await EnsureScoreScopeAsync(caller, appeal.EmployeeId, appeal.Employee.DepartmentId, cancellationToken);
        await EnsureScoreScopeAsync(caller, appeal.ResultSubmission!.SubmitterEmployeeId, appeal.ResultSubmission.SubmitterEmployee.DepartmentId, cancellationToken);
        if (appeal.Status != AppealStatus.Open || appeal.ResultSubmission!.Status != SubmissionStatus.Rejected)
        {
            throw InvalidDecisionState();
        }

        var history = -await db.ScoreEntries.Where(e => e.EmployeeId == appeal.EmployeeId && e.Kind == ScoreEntryKind.TaskDeduction)
            .SumAsync(e => (decimal?)e.Points, cancellationToken) ?? 0m;
        var points = request.Points ?? (history > 0 ? history : (await GetScoreSettingCoreAsync(cancellationToken)).DefaultPoints);
        var source = request.Points.HasValue ? ScoreEntrySource.Manager : history > 0 ? ScoreEntrySource.TaskHistory : ScoreEntrySource.System;
        var evaluation = await AppealScoreLedger.CurrentAsync(db, appeal.EmployeeId, time.GetBusinessToday(), cancellationToken);
        var now = time.GetUtcNow();
        var entry = AppealScoreLedger.Add(db, evaluation, -points, ScoreEntryKind.Hold, source, reason, caller.Employee.Id, now, appeal.Id, appeal.ResultSubmission.WorkId);
        appeal.Status = AppealStatus.UnderReview;
        appeal.UpdatedAt = now;
        await CommitSerializableAsync(transaction, InvalidDecisionState, cancellationToken);
        return MapScoreEntry(entry);
    }

    public async Task<ScoreEntryDto> DeductTaskScoreAsync(Guid workId, Guid userId, ReviewAppealRequestDto request, CancellationToken cancellationToken = default)
    {
        var reason = ValidateScoreRequest(request);
        if (!request.Points.HasValue)
        {
            throw new ValidationException("Điểm trừ task là bắt buộc.");
        }

        var caller = await ResolveCallerAsync(userId, cancellationToken);
        await using var transaction = await BeginSerializableAsync(cancellationToken);
        var work = await db.Works.AsNoTracking().Include(w => w.AssigneeEmployee).SingleOrDefaultAsync(w => w.Id == workId, cancellationToken)
            ?? throw new NotFoundException("Công việc", workId);
        if (work.AssigneeEmployee is null)
        {
            throw new ConflictException("Công việc chưa có nhân viên được giao.");
        }

        await EnsureScoreScopeAsync(caller, work.AssigneeEmployee.Id, work.AssigneeEmployee.DepartmentId, cancellationToken);
        var evaluation = await AppealScoreLedger.CurrentAsync(db, work.AssigneeEmployee.Id, time.GetBusinessToday(), cancellationToken);
        var entry = AppealScoreLedger.Add(db, evaluation, -request.Points.Value, ScoreEntryKind.TaskDeduction, ScoreEntrySource.Manager, reason, caller.Employee.Id, time.GetUtcNow(), workId: workId);
        await CommitSerializableAsync(transaction, () => new ConflictException("Điểm KPI đã thay đổi đồng thời, vui lòng tải lại."), cancellationToken);
        return MapScoreEntry(entry);
    }

    public async Task<IReadOnlyList<ScoreEntryDto>> GetScoreEntriesAsync(Guid appealId, Guid userId, CancellationToken cancellationToken = default)
    {
        await GetAppealByIdAsync(appealId, userId, cancellationToken);
        var entries = await db.ScoreEntries.AsNoTracking().Where(e => e.AppealId == appealId).OrderBy(e => e.CreatedAt).ThenBy(e => e.Id).ToListAsync(cancellationToken);
        return entries.Select(MapScoreEntry).ToList();
    }

    public async Task<AppealDetailDto> DisputeScoreAsync(Guid appealId, Guid userId, ScoreDisputeRequestDto request, CancellationToken cancellationToken = default)
    {
        var reason = ValidateReason(request.Reason);
        var caller = await ResolveCallerAsync(userId, cancellationToken);
        var appeal = await db.Appeals.SingleOrDefaultAsync(a => a.Id == appealId && a.ResultSubmissionId != null, cancellationToken)
            ?? throw new NotFoundException("Khiếu nại", appealId);
        if (appeal.EmployeeId != caller.Employee.Id)
        {
            throw new ForbiddenException("Chỉ người gửi khiếu nại được phản hồi khoản giữ điểm.");
        }

        if (!await db.ScoreEntries.AnyAsync(e => e.AppealId == appealId && e.Kind == ScoreEntryKind.Hold, cancellationToken))
        {
            throw new ConflictException("Khiếu nại chưa có khoản giữ điểm.");
        }

        appeal.ScoreDisputeReason = reason;
        appeal.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return await GetAppealByIdAsync(appealId, userId, cancellationToken);
    }

    public async Task<AppealScoreSettingDto> GetScoreSettingAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await EnsureScoreAdminAsync(userId, cancellationToken);
        return await GetScoreSettingCoreAsync(cancellationToken);
    }

    public async Task<AppealScoreSettingDto> UpdateScoreSettingAsync(Guid userId, AppealScoreSettingDto request, CancellationToken cancellationToken = default)
    {
        await EnsureScoreAdminAsync(userId, cancellationToken);
        ValidatePoints(request.DefaultPoints);
        var setting = await db.AppealScoreSettings.SingleOrDefaultAsync(s => s.Id == 1, cancellationToken);
        if (setting is null)
        {
            setting = new AppealScoreSetting { Id = 1 };
            db.AppealScoreSettings.Add(setting);
        }

        setting.DefaultPoints = request.DefaultPoints;
        await db.SaveChangesAsync(cancellationToken);
        return request;
    }

    private async Task EnsureScoreAdminAsync(Guid userId, CancellationToken ct)
    {
        var caller = await ResolveCallerAsync(userId, ct);
        var roleCodes = caller.Employee.Account is null ? []
            : await db.UserRoles.Where(r => r.AccountId == caller.Employee.Account.Id).Select(r => r.Role.Code).ToListAsync(ct);
        if (!roleCodes.Any(code => string.Equals(code, "ADMIN", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ForbiddenException("Chỉ quản trị viên được chỉnh cấu hình giữ điểm.");
        }
    }

    private async Task EnsureScoreScopeAsync(Caller caller, Guid employeeId, Guid departmentId, CancellationToken ct)
    {
        var visibility = await ResolveVisibilityAsync(caller, ct);
        if ((!visibility.SeesAll && !visibility.DepartmentIds.Contains(departmentId)) || employeeId == caller.Employee.Id)
        {
            throw new ForbiddenException("Bạn không có quyền giữ hoặc trừ điểm của nhân viên này.");
        }
    }

    private async Task<AppealScoreSettingDto> GetScoreSettingCoreAsync(CancellationToken ct) =>
        new(await db.AppealScoreSettings.AsNoTracking().Where(s => s.Id == 1).Select(s => (decimal?)s.DefaultPoints).SingleOrDefaultAsync(ct) ?? 5m);

    private static string ValidateScoreRequest(ReviewAppealRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Points.HasValue)
        {
            ValidatePoints(request.Points.Value);
        }

        return ValidateReason(request.Reason);
    }

    private static string ValidateReason(string? value)
    {
        var reason = value?.Trim();
        if (reason is null || reason.Length < 10 || reason.Length > MaxReasonLength)
        {
            throw new ValidationException("Lý do giữ hoặc trừ điểm phải có 10–2000 ký tự.");
        }

        return reason;
    }

    private async Task<IDbContextTransaction?> BeginSerializableAsync(CancellationToken ct) =>
        db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;

    private async Task CommitSerializableAsync(IDbContextTransaction? transaction, Func<Exception> onConflict, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            if (transaction is not null)
            {
                await transaction.CommitAsync(ct);
            }
        }
        catch (Exception ex) when (PostgresConflict.IsSerializationFailure(ex))
        {
            throw onConflict();
        }
    }

    private static void ValidatePoints(decimal points)
    {
        if (points <= 0 || points > 1000 || decimal.Round(points, 4) != points)
        {
            throw new ValidationException("Điểm phải lớn hơn 0, tối đa 1000 và có tối đa 4 chữ số thập phân.");
        }
    }

    private static ScoreEntryDto MapScoreEntry(ScoreEntry e) =>
        new(e.Id, e.AppealId, e.WorkId, e.EvaluationId, e.Points, e.Kind.ToEnumString(), e.Source.ToEnumString(), e.Reason, e.RecordedByEmployeeId, e.CreatedAt, e.ReversesEntryId);
}
