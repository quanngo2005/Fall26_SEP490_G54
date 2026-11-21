using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Common.Validation;
using G54.BLL.Dtos.TargetAllocation;
using G54.DAL;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

/// <summary>
/// Implementation of the employee-facing "my-targets" flow (Target Allocation v3, BR-36).
/// Contract: docs/api/TARGET_ALLOCATION_CONTRACT.md "Employee side".
/// </summary>
public sealed class MyTargetsService(
    IUnitOfWork uow, AppDbContext db, IPermissionService permissions, ITargetChangeLogService changeLog, TimeProvider time,
    Microsoft.Extensions.Options.IOptions<G54.BLL.Common.SystemParametersOptions> systemParameters)
    : IMyTargetsService
{
    private const int ReasonMaxLength = 500;

    public async Task<MyTargetsDto> GetMyTargetsAsync(Guid userId, int year, CancellationToken cancellationToken = default)
    {
        var employee = await ResolveEmployeeAsync(userId, cancellationToken);
        var yearPeriod = await uow.EvaluationPeriods.GetYearAsync(year, cancellationToken)
            ?? throw new NotFoundException($"Kỳ đánh giá năm {year} không tồn tại.");

        var quarterPeriods = (await uow.EvaluationPeriods.GetInRangeAsync(
                PeriodType.Quarter, yearPeriod.StartDate, yearPeriod.EndDate, cancellationToken))
            .OrderBy(p => p.StartDate)
            .ToList();

        var yearRows = await db.TargetAssignments
            .AsNoTracking()
            .Include(t => t.KpiDefinition)
            .Where(t => t.EmployeeId == employee.Id
                && t.EvaluationPeriodId == yearPeriod.Id
                && t.SplitDimension == TargetSplitDimension.Org
                && t.Status == TargetAssignmentStatus.Active)
            .ToListAsync(cancellationToken);

        var yearRowIds = yearRows.Select(r => r.Id).ToList();
        var quarterRows = yearRowIds.Count == 0
            ? []
            : await db.TargetAssignments
                .AsNoTracking()
                .Where(t => t.ParentAssignmentId != null && yearRowIds.Contains(t.ParentAssignmentId.Value)
                    && t.SplitDimension == TargetSplitDimension.Period
                    && t.Status == TargetAssignmentStatus.Active)
                .ToListAsync(cancellationToken);
        var quarterByYear = quarterRows.GroupBy(q => q.ParentAssignmentId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var previousYearPeriod = await uow.EvaluationPeriods.GetYearAsync(year - 1, cancellationToken);
        var prevYearByKpi = new Dictionary<Guid, TargetAssignment>();
        var prevActualByKpi = new Dictionary<Guid, decimal>();
        if (previousYearPeriod is not null)
        {
            var prevRows = await db.TargetAssignments
                .AsNoTracking()
                .Where(t => t.EmployeeId == employee.Id
                    && t.EvaluationPeriodId == previousYearPeriod.Id
                    && t.SplitDimension == TargetSplitDimension.Org
                    && t.Status == TargetAssignmentStatus.Active)
                .ToListAsync(cancellationToken);
            prevYearByKpi = prevRows.GroupBy(r => r.KpiDefinitionId).ToDictionary(g => g.Key, g => g.First());

            var kpiIds = yearRows.Select(r => r.KpiDefinitionId).ToList();
            var actuals = await db.ActualProgresses
                .AsNoTracking()
                .Where(ap => ap.Status == ActualProgressStatus.Valid
                    && ap.TargetAssignment.EmployeeId == employee.Id
                    && kpiIds.Contains(ap.TargetAssignment.KpiDefinitionId)
                    && ap.ProgressDate >= previousYearPeriod.StartDate
                    && ap.ProgressDate <= previousYearPeriod.EndDate)
                .Select(ap => new { ap.TargetAssignment.KpiDefinitionId, ap.Value })
                .ToListAsync(cancellationToken);
            prevActualByKpi = actuals.GroupBy(a => a.KpiDefinitionId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Value));
        }

        var ack = await db.EmployeeTargetAcks.AsNoTracking()
            .FirstOrDefaultAsync(a => a.EmployeeId == employee.Id && a.Year == year, cancellationToken);

        var changeLogs = await changeLog.GetByEmployeeYearAsync(employee.Id, year, cancellationToken);
        var changesByKpi = changeLogs
            .Where(c => c.Action != TargetChangeLogAction.Publish && c.Period == "YEAR")
            .GroupBy(c => c.KpiDefinitionId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.OccurredAt).First());

        var feedbackKpiIds = (await db.TargetAssignmentFeedbacks
                .AsNoTracking()
                .Where(f => f.EmployeeId == employee.Id
                    && (f.ProposedYear != null || f.ProposedQ1 != null || f.ProposedQ2 != null
                        || f.ProposedQ3 != null || f.ProposedQ4 != null))
                .Select(f => f.TargetAssignment.KpiDefinitionId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var rows = yearRows
            .OrderBy(r => r.KpiDefinition.Code)
            .Select(yr =>
            {
                var quarters = quarterByYear.TryGetValue(yr.Id, out var qs) ? qs : [];
                decimal? GetQuarter(int index) => index < quarterPeriods.Count
                    ? quarters.FirstOrDefault(q => q.EvaluationPeriodId == quarterPeriods[index].Id)?.TargetValue
                    : null;

                prevYearByKpi.TryGetValue(yr.KpiDefinitionId, out var prevRow);
                var hasPrevActual = prevActualByKpi.TryGetValue(yr.KpiDefinitionId, out var prevActual);
                changesByKpi.TryGetValue(yr.KpiDefinitionId, out var change);

                return new MyTargetKpiRowDto(
                    yr.KpiDefinitionId, yr.KpiDefinition.Code, yr.KpiDefinition.Name, yr.KpiDefinition.Unit,
                    yr.KpiDefinition.AggregationType.ToEnumString(),
                    yr.TargetValue, GetQuarter(0), GetQuarter(1), GetQuarter(2), GetQuarter(3),
                    prevRow?.TargetValue, hasPrevActual ? prevActual : null,
                    change is not null, change?.OldValue, change?.NewValue,
                    feedbackKpiIds.Contains(yr.KpiDefinitionId),
                    systemParameters.Value.TargetSuggestionCeilingPercent);
            })
            .ToList();

        return new MyTargetsDto(
            year, employee.Id, employee.FullName, employee.DepartmentId, employee.DepartmentName,
            ack?.Status ?? EmployeeTargetAckStatus.Pending, ack?.ConfirmedAt, rows);
    }

    public async Task<ConfirmMyTargetsResultDto> ConfirmAsync(Guid userId, int year, CancellationToken cancellationToken = default)
    {
        var employee = await ResolveEmployeeAsync(userId, cancellationToken);

        var ack = await db.EmployeeTargetAcks
            .FirstOrDefaultAsync(a => a.EmployeeId == employee.Id && a.Year == year, cancellationToken);

        var now = time.GetUtcNow();
        if (ack is null)
        {
            ack = new EmployeeTargetAck
            {
                Id = Guid.NewGuid(),
                EmployeeId = employee.Id,
                Year = year,
                Status = EmployeeTargetAckStatus.Confirmed,
                ConfirmedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.EmployeeTargetAcks.Add(ack);
        }
        else
        {
            if (ack.Status == EmployeeTargetAckStatus.Confirmed)
            {
                throw new ConflictException(
                    "Chỉ tiêu năm nay đã được xác nhận trước đó.", ErrorCodes.TargetAckInvalidState);
            }

            ack.Status = EmployeeTargetAckStatus.Confirmed;
            ack.ConfirmedAt = now;
            ack.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);

        return new ConfirmMyTargetsResultDto(year, ack.Status, now);
    }

    public async Task<TargetFeedbackDto> SubmitFeedbackAsync(
        Guid userId, int year, SubmitTargetFeedbackRequest request, CancellationToken cancellationToken = default)
    {
        var employee = await ResolveEmployeeAsync(userId, cancellationToken);

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new ValidationException("Lý do góp ý là bắt buộc.");
        }

        if (request.Reason.Length > ReasonMaxLength)
        {
            throw new BusinessRuleException(
                ErrorCodes.MaxLengthExceeded,
                $"Lý do góp ý không được vượt quá {ReasonMaxLength} ký tự.");
        }

        var yearPeriod = await uow.EvaluationPeriods.GetYearAsync(year, cancellationToken)
            ?? throw new NotFoundException($"Kỳ đánh giá năm {year} không tồn tại.");

        var targetAssignment = await db.TargetAssignments
            .Include(t => t.KpiDefinition)
            .FirstOrDefaultAsync(t => t.EmployeeId == employee.Id
                && t.KpiDefinitionId == request.KpiId
                && t.EvaluationPeriodId == yearPeriod.Id
                && t.SplitDimension == TargetSplitDimension.Org, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy chỉ tiêu KPI của bạn cho năm này.");

        if (request.ProposedYear.HasValue
            && targetAssignment.KpiDefinition.AggregationType == KpiAggregationType.Sum)
        {
            var sum = (request.ProposedQ1 ?? 0) + (request.ProposedQ2 ?? 0)
                + (request.ProposedQ3 ?? 0) + (request.ProposedQ4 ?? 0);
            if (sum != request.ProposedYear.Value)
            {
                throw new BusinessRuleException(
                    ErrorCodes.TargetFeedbackProposedSumMismatch, statusCode: 422);
            }
        }

        var now = time.GetUtcNow();
        var feedback = new TargetAssignmentFeedback
        {
            Id = Guid.NewGuid(),
            TargetAssignmentId = targetAssignment.Id,
            EmployeeId = employee.Id,
            Type = TargetAssignmentFeedbackType.Comment,
            Comment = request.Reason,
            ProposedYear = request.ProposedYear,
            ProposedQ1 = request.ProposedQ1,
            ProposedQ2 = request.ProposedQ2,
            ProposedQ3 = request.ProposedQ3,
            ProposedQ4 = request.ProposedQ4,
            CreatedAt = now
        };

        db.TargetAssignmentFeedbacks.Add(feedback);
        await db.SaveChangesAsync(cancellationToken);

        await changeLog.LogAsync(
            userId, TargetChangeLogAction.Feedback, tier: 3, kpiId: request.KpiId,
            unit: targetAssignment.KpiDefinition.Unit, period: "YEAR",
            oldValue: targetAssignment.TargetValue, newValue: request.ProposedYear,
            basis: "FEEDBACK", employeeId: employee.Id, departmentId: employee.DepartmentId, year: year,
            cancellationToken: cancellationToken);

        return ToFeedbackDto(feedback, targetAssignment.KpiDefinitionId, targetAssignment.KpiDefinition.Name, employee.FullName, null);
    }

    public async Task<TargetFeedbackDto> ReplyFeedbackAsync(
        Guid userId, Guid feedbackId, ReplyTargetFeedbackRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reply))
        {
            throw new ValidationException("Nội dung phản hồi là bắt buộc.");
        }

        if (request.Reply.Length > ReasonMaxLength)
        {
            throw new BusinessRuleException(
                ErrorCodes.MaxLengthExceeded,
                $"Nội dung phản hồi không được vượt quá {ReasonMaxLength} ký tự.");
        }

        var feedback = await db.TargetAssignmentFeedbacks
            .Include(f => f.TargetAssignment).ThenInclude(t => t.KpiDefinition)
            .Include(f => f.Employee)
            .FirstOrDefaultAsync(f => f.Id == feedbackId, cancellationToken)
            ?? throw new NotFoundException("Góp ý", feedbackId);

        // DM scope: own department only (reuses the Phase 2/3 target:manage department-access rule).
        await DepartmentAccessGuard.EnsureDepartmentAccessAsync(
            uow, permissions, userId, feedback.Employee.DepartmentId, cancellationToken);

        var now = time.GetUtcNow();
        feedback.Reply = request.Reply;
        feedback.RepliedBy = userId;
        feedback.RepliedAt = now;

        await db.SaveChangesAsync(cancellationToken);

        await changeLog.LogAsync(
            userId, TargetChangeLogAction.Reply, tier: 3, kpiId: feedback.TargetAssignment.KpiDefinitionId,
            unit: feedback.TargetAssignment.KpiDefinition.Unit, period: "YEAR",
            oldValue: null, newValue: null, basis: "REPLY",
            employeeId: feedback.EmployeeId, departmentId: feedback.Employee.DepartmentId,
            cancellationToken: cancellationToken);

        return ToFeedbackDto(
            feedback, feedback.TargetAssignment.KpiDefinitionId, feedback.TargetAssignment.KpiDefinition.Name,
            feedback.Employee.FullName, null);
    }

    public async Task<MyTargetsProgressDto> GetProgressAsync(
        Guid userId, int year, string? period, CancellationToken cancellationToken = default)
    {
        var employee = await ResolveEmployeeAsync(userId, cancellationToken);
        var yearPeriod = await uow.EvaluationPeriods.GetYearAsync(year, cancellationToken)
            ?? throw new NotFoundException($"Kỳ đánh giá năm {year} không tồn tại.");

        var targetPeriod = await ResolvePeriodAsync(yearPeriod, period, cancellationToken);

        var yearRows = await db.TargetAssignments
            .AsNoTracking()
            .Include(t => t.KpiDefinition)
            .Where(t => t.EmployeeId == employee.Id
                && t.EvaluationPeriodId == yearPeriod.Id
                && t.SplitDimension == TargetSplitDimension.Org
                && t.Status == TargetAssignmentStatus.Active)
            .ToListAsync(cancellationToken);

        var rows = new List<MyTargetsProgressRowDto>();
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var totalDays = Math.Max(1, targetPeriod.EndDate.DayNumber - targetPeriod.StartDate.DayNumber + 1);
        var elapsedDays = Math.Clamp(today.DayNumber - targetPeriod.StartDate.DayNumber + 1, 0, totalDays);
        var expectedPace = Math.Round(100m * elapsedDays / totalDays, 0, MidpointRounding.AwayFromZero);

        foreach (var yr in yearRows.OrderBy(r => r.KpiDefinition.Code))
        {
            decimal targetValue = yr.KpiDefinition.AggregationType == KpiAggregationType.Sum
                ? await ResolveQuarterTargetAsync(yr.Id, targetPeriod, cancellationToken)
                : yr.TargetValue;

            var achieved = await db.ActualProgresses
                .AsNoTracking()
                .Where(ap => ap.Status == ActualProgressStatus.Valid
                    && ap.TargetAssignment.EmployeeId == employee.Id
                    && ap.TargetAssignment.KpiDefinitionId == yr.KpiDefinitionId
                    && ap.ProgressDate >= targetPeriod.StartDate
                    && ap.ProgressDate <= targetPeriod.EndDate)
                .SumAsync(ap => (decimal?)ap.Value, cancellationToken) ?? 0m;

            var periodStart = new DateTimeOffset(targetPeriod.StartDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var periodEnd = new DateTimeOffset(targetPeriod.EndDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
            var inReviewCount = await db.WorkTransactions
                .AsNoTracking()
                .Where(wt => wt.Status == WorkTransactionStatus.InReview
                    && wt.Work.AssigneeEmployeeId == employee.Id
                    && wt.Work.WorkTypeId != null
                    && db.WorkTypeKpis.Any(wk => wk.WorkTypeId == wt.Work.WorkTypeId && wk.KpiDefinitionId == yr.KpiDefinitionId)
                    && wt.CreatedAt >= periodStart
                    && wt.CreatedAt <= periodEnd)
                .CountAsync(cancellationToken);

            var achievedPercent = targetValue > 0
                ? Math.Round(achieved / targetValue * 100m, 0, MidpointRounding.AwayFromZero)
                : 0m;
            var gap = achievedPercent - expectedPace;
            var paceStatus = achievedPercent >= 100m ? "ACHIEVED"
                : gap >= 0 ? "ON_PACE"
                : gap >= -10 ? "SLOW"
                : "AT_RISK";

            rows.Add(new MyTargetsProgressRowDto(
                yr.KpiDefinitionId, yr.KpiDefinition.Code, yr.KpiDefinition.Name, yr.KpiDefinition.Unit,
                new MyTargetsProgressQuarterDto(targetValue, achieved, inReviewCount, achievedPercent, expectedPace, paceStatus)));
        }

        return new MyTargetsProgressDto(year, targetPeriod.Code, rows);
    }

    private async Task<decimal> ResolveQuarterTargetAsync(Guid yearRowId, EvaluationPeriod targetPeriod, CancellationToken ct)
    {
        if (targetPeriod.PeriodType == PeriodType.Year)
        {
            return await db.TargetAssignments.AsNoTracking()
                .Where(t => t.Id == yearRowId)
                .Select(t => t.TargetValue)
                .FirstOrDefaultAsync(ct);
        }

        return await db.TargetAssignments
            .AsNoTracking()
            .Where(t => t.ParentAssignmentId == yearRowId
                && t.SplitDimension == TargetSplitDimension.Period
                && t.EvaluationPeriodId == targetPeriod.Id
                && t.Status == TargetAssignmentStatus.Active)
            .Select(t => t.TargetValue)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<EvaluationPeriod> ResolvePeriodAsync(EvaluationPeriod yearPeriod, string? period, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(period) || string.Equals(period, "YEAR", StringComparison.OrdinalIgnoreCase))
        {
            return yearPeriod;
        }

        var quarters = (await uow.EvaluationPeriods.GetInRangeAsync(
                PeriodType.Quarter, yearPeriod.StartDate, yearPeriod.EndDate, ct))
            .OrderBy(p => p.StartDate)
            .ToList();

        var index = period.Trim().ToUpperInvariant() switch
        {
            "Q1" => 0,
            "Q2" => 1,
            "Q3" => 2,
            "Q4" => 3,
            _ => throw new ValidationException("period phải là YEAR, Q1, Q2, Q3 hoặc Q4.")
        };

        return index < quarters.Count
            ? quarters[index]
            : throw new NotFoundException("Không tìm thấy kỳ quý tương ứng.");
    }

    private async Task<CallerInfo> ResolveEmployeeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var employee = await db.Employees
            .AsNoTracking()
            .Include(e => e.Department)
            .Where(e => e.Id == userId || (e.Account != null && e.Account.Id == userId))
            .Select(e => new CallerInfo(e.Id, e.FullName, e.DepartmentId, e.Department!.Name))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ForbiddenException("Tài khoản chưa gắn với nhân viên.");

        return employee;
    }

    public async Task<IReadOnlyList<TargetFeedbackDto>> GetFeedbackAsync(
        Guid userId, int year, Guid departmentId, Guid? employeeId, CancellationToken cancellationToken = default)
    {
        await DepartmentAccessGuard.EnsureDepartmentAccessAsync(uow, permissions, userId, departmentId, cancellationToken);

        var query = db.TargetAssignmentFeedbacks
            .AsNoTracking()
            .Include(f => f.TargetAssignment).ThenInclude(t => t.KpiDefinition)
            .Include(f => f.TargetAssignment).ThenInclude(t => t.EvaluationPeriod)
            .Include(f => f.Employee)
            .Where(f => f.Employee.DepartmentId == departmentId
                && f.TargetAssignment.EvaluationPeriod.StartDate.Year == year);

        if (employeeId.HasValue)
        {
            query = query.Where(f => f.EmployeeId == employeeId.Value);
        }

        var rows = await query.OrderByDescending(f => f.CreatedAt).ToListAsync(cancellationToken);

        var replierIds = rows.Where(f => f.RepliedBy.HasValue).Select(f => f.RepliedBy!.Value).Distinct().ToList();
        var replierNames = replierIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Employees.AsNoTracking().Where(e => replierIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, e => e.FullName, cancellationToken);

        return rows
            .Select(f => ToFeedbackDto(
                f, f.TargetAssignment.KpiDefinitionId, f.TargetAssignment.KpiDefinition.Name, f.Employee.FullName,
                f.RepliedBy.HasValue ? replierNames.GetValueOrDefault(f.RepliedBy.Value) : null))
            .ToList();
    }

    public async Task<IReadOnlyList<TargetChangeLogEntryDto>> GetChangeLogAsync(
        Guid userId, int year, Guid employeeId, Guid? kpiId, CancellationToken cancellationToken = default)
    {
        var caller = await ResolveEmployeeAsync(userId, cancellationToken);
        if (caller.Id != employeeId)
        {
            var target = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken)
                ?? throw new NotFoundException("Nhân viên", employeeId);
            await DepartmentAccessGuard.EnsureDepartmentAccessAsync(uow, permissions, userId, target.DepartmentId, cancellationToken);
        }

        var entries = await changeLog.GetByEmployeeYearAsync(employeeId, year, cancellationToken);
        return entries
            .Where(e => kpiId is null || e.KpiDefinitionId == kpiId.Value)
            .Select(e => new TargetChangeLogEntryDto(
                e.Id, e.OccurredAt, e.Action, e.Tier, e.KpiDefinitionId, e.KpiDefinition.Name, e.Unit, e.Period,
                e.OldValue, e.NewValue, e.Basis))
            .ToList();
    }

    public async Task<IReadOnlyList<TargetFeedbackDto>> GetMyFeedbackAsync(
        Guid userId, int year, Guid? kpiId, CancellationToken cancellationToken = default)
    {
        var employee = await ResolveEmployeeAsync(userId, cancellationToken);

        var query = db.TargetAssignmentFeedbacks
            .AsNoTracking()
            .Include(f => f.TargetAssignment).ThenInclude(t => t.KpiDefinition)
            .Include(f => f.TargetAssignment).ThenInclude(t => t.EvaluationPeriod)
            .Where(f => f.EmployeeId == employee.Id && f.TargetAssignment.EvaluationPeriod.StartDate.Year == year);

        if (kpiId.HasValue)
        {
            query = query.Where(f => f.TargetAssignment.KpiDefinitionId == kpiId.Value);
        }

        var rows = await query.OrderByDescending(f => f.CreatedAt).ToListAsync(cancellationToken);

        var replierIds = rows.Where(f => f.RepliedBy.HasValue).Select(f => f.RepliedBy!.Value).Distinct().ToList();
        var replierNames = replierIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Employees.AsNoTracking().Where(e => replierIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, e => e.FullName, cancellationToken);

        return rows
            .Select(f => ToFeedbackDto(
                f, f.TargetAssignment.KpiDefinitionId, f.TargetAssignment.KpiDefinition.Name, employee.FullName,
                f.RepliedBy.HasValue ? replierNames.GetValueOrDefault(f.RepliedBy.Value) : null))
            .ToList();
    }

    public async Task<IReadOnlyList<MyContributionDto>> GetMyContributionsAsync(
        Guid userId, int year, Guid kpiId, CancellationToken cancellationToken = default)
    {
        var employee = await ResolveEmployeeAsync(userId, cancellationToken);
        var yearPeriod = await uow.EvaluationPeriods.GetYearAsync(year, cancellationToken)
            ?? throw new NotFoundException($"Kỳ đánh giá năm {year} không tồn tại.");

        var employeeYearRow = await db.TargetAssignments
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.EmployeeId == employee.Id && t.KpiDefinitionId == kpiId
                && t.EvaluationPeriodId == yearPeriod.Id && t.SplitDimension == TargetSplitDimension.Org, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy chỉ tiêu KPI của bạn cho năm này.");

        var quarterIds = await db.TargetAssignments
            .AsNoTracking()
            .Where(t => t.ParentAssignmentId == employeeYearRow.Id && t.SplitDimension == TargetSplitDimension.Period)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        var assignmentIds = new List<Guid> { employeeYearRow.Id };
        assignmentIds.AddRange(quarterIds);

        var items = await db.WorkResultItems
            .AsNoTracking()
            .Include(w => w.ActualProgress)
            .Include(w => w.VerifiedWorkResult).ThenInclude(v => v!.WorkTransaction).ThenInclude(t => t!.ImportedTransaction)
            .Where(w => w.EmployeeId == employee.Id && assignmentIds.Contains(w.ActualProgress.TargetAssignmentId))
            .OrderByDescending(w => w.UpdatedAt)
            .ToListAsync(cancellationToken);

        return items.Select(w =>
        {
            var workTransaction = w.VerifiedWorkResult?.WorkTransaction;
            var imported = workTransaction?.ImportedTransaction;
            var status = workTransaction?.Status switch
            {
                WorkTransactionStatus.Matched => "AUTO_RECONCILED",
                WorkTransactionStatus.NotMatched => "NOT_MATCHED",
                _ => "IN_REVIEW",
            };

            return new MyContributionDto(
                w.Id,
                imported?.TransactionDate ?? w.ActualProgress.ProgressDate,
                workTransaction?.TransactionCode ?? imported?.TransactionCode,
                imported?.CustomerCode,
                imported?.ReferEmployeeCode,
                w.Value,
                status);
        }).ToList();
    }

    private static TargetFeedbackDto ToFeedbackDto(
        TargetAssignmentFeedback feedback, Guid kpiId, string kpiName, string employeeName, string? repliedByName) => new(
        feedback.Id, feedback.TargetAssignmentId, kpiId, kpiName,
        feedback.EmployeeId, employeeName, feedback.Comment,
        feedback.ProposedYear, feedback.ProposedQ1, feedback.ProposedQ2, feedback.ProposedQ3, feedback.ProposedQ4,
        feedback.Reply, feedback.RepliedBy, repliedByName, feedback.RepliedAt, feedback.CreatedAt);

    private sealed record CallerInfo(Guid Id, string FullName, Guid DepartmentId, string DepartmentName);
}
