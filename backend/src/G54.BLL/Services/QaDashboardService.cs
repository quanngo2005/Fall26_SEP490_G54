using System.Globalization;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Quality;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

public sealed class QaDashboardService(AppDbContext db, TimeProvider? timeProvider = null) : IQaDashboardService
{
    private static readonly TimeSpan BusinessOffset = TimeSpan.FromHours(7);
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<QaDashboardDto> GetDashboardAsync(QaDashboardFilterDto filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var now = _time.GetUtcNow();
        var today = DateOnly.FromDateTime(now.ToOffset(BusinessOffset).DateTime);

        // 1. Resolve Period
        var period = await ResolvePeriodAsync(filter.PeriodId, today, cancellationToken);
        var periodStartOffset = new DateTimeOffset(period.StartDate.ToDateTime(TimeOnly.MinValue), BusinessOffset).ToUniversalTime();
        var periodEndOffset = new DateTimeOffset(period.EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue), BusinessOffset).ToUniversalTime();
        var lockDeadline = new DateTimeOffset(period.EndDate.ToDateTime(new TimeOnly(23, 59, 59)), BusinessOffset);
        var isLocked = now >= lockDeadline || period.Status is PeriodStatus.Locked or PeriodStatus.Closed;

        var periodDto = new QaDashboardPeriodDto(
            period.Id,
            period.Code,
            FormatPeriodName(period),
            period.StartDate,
            period.EndDate,
            lockDeadline,
            isLocked);

        // 2. Resolve Department Hierarchy and Job Position filter
        var departmentIds = filter.DepartmentId is { } deptId
            ? (await DepartmentHierarchy.LoadAsync(db.Departments, cancellationToken)).Subtree(deptId)
            : null;

        // 3. Transactions query in period
        var txQuery = db.WorkTransactions.AsNoTracking()
            .Include(t => t.Work).ThenInclude(w => w.AssigneeEmployee).ThenInclude(e => e.JobPosition)
            .Include(t => t.Work).ThenInclude(w => w.AssigneeEmployee).ThenInclude(e => e.Department)
            .Include(t => t.ImportedTransaction)
            .Where(t => t.CreatedAt >= periodStartOffset && t.CreatedAt < periodEndOffset);

        if (departmentIds is not null)
        {
            txQuery = txQuery.Where(t => departmentIds.Contains(t.Work.AssigneeEmployee.DepartmentId));
        }

        if (filter.JobPositionId is { } posId)
        {
            txQuery = txQuery.Where(t => t.Work.AssigneeEmployee.JobPositionId == posId);
        }

        if (!string.IsNullOrWhiteSpace(filter.JobPositionCode))
        {
            var code = filter.JobPositionCode.Trim();
            txQuery = txQuery.Where(t => t.Work.AssigneeEmployee.JobPosition.Code == code);
        }

        var transactions = await txQuery.ToListAsync(cancellationToken);

        var notMatchedList = transactions.Where(t => t.Status == WorkTransactionStatus.NotMatched).ToList();
        var inReviewList = transactions.Where(t => t.Status == WorkTransactionStatus.InReview).ToList();
        var matchedList = transactions.Where(t => t.Status == WorkTransactionStatus.Matched).ToList();

        // 4. Warning Banner & In Review Card
        var inReviewCount = inReviewList.Count;
        var banner = new QaDashboardBannerDto(
            inReviewCount,
            $"Còn {inReviewCount} kết quả In Review chưa có giao dịch import",
            $"Tháng {periodDto.Name} khóa KPI lúc 23:59:59 {period.EndDate:dd/MM/yyyy} (BR-29). Sau thời điểm này không import lại được giao dịch của tháng.",
            inReviewCount > 0);

        // 5. Not Matched Card
        int? oldestDays = null;
        if (notMatchedList.Count > 0)
        {
            var oldestDate = notMatchedList.Min(t => t.CreatedAt);
            oldestDays = Math.Max(0, (int)(now - oldestDate).TotalDays);
        }

        var notMatchedCard = new QaDashboardNotMatchedCardDto(
            notMatchedList.Count,
            oldestDays,
            oldestDays.HasValue ? $"cũ nhất {oldestDays.Value} ngày" : "0 ngày");

        var inReviewCard = new QaDashboardInReviewCardDto(
            inReviewCount,
            "chờ GD import (MSG13)");

        // 6. QA Queue Card
        var flagsQuery = db.QualityFlags.AsNoTracking()
            .Include(f => f.Employee)
            .Where(f => f.Status == QualityFlagStatus.Open);

        if (departmentIds is not null)
        {
            flagsQuery = flagsQuery.Where(f => departmentIds.Contains(f.Employee.DepartmentId));
        }

        if (filter.JobPositionId is { } flagPosId)
        {
            flagsQuery = flagsQuery.Where(f => f.Employee.JobPositionId == flagPosId);
        }

        if (!string.IsNullOrWhiteSpace(filter.JobPositionCode))
        {
            var flagCode = filter.JobPositionCode.Trim();
            flagsQuery = flagsQuery.Where(f => f.Employee.JobPosition.Code == flagCode);
        }

        var openFlagsCount = await flagsQuery.CountAsync(cancellationToken);

        var samplesQuery = db.QaSamples.AsNoTracking()
            .Include(s => s.SubmitterEmployee)
            .Where(s => s.Status == QaSampleStatus.Pending && s.QualityFlag == null);

        if (departmentIds is not null)
        {
            samplesQuery = samplesQuery.Where(s => departmentIds.Contains(s.SubmitterEmployee.DepartmentId));
        }

        if (filter.JobPositionId is { } samplePosId)
        {
            samplesQuery = samplesQuery.Where(s => s.SubmitterEmployee.JobPositionId == samplePosId);
        }

        if (!string.IsNullOrWhiteSpace(filter.JobPositionCode))
        {
            var sampCode = filter.JobPositionCode.Trim();
            samplesQuery = samplesQuery.Where(s => s.SubmitterEmployee.JobPosition.Code == sampCode);
        }

        var pendingSamplesCount = await samplesQuery.CountAsync(cancellationToken);

        var qaQueueCard = new QaDashboardQaQueueCardDto(
            pendingSamplesCount + openFlagsCount,
            pendingSamplesCount,
            openFlagsCount,
            $"{pendingSamplesCount} mẫu · {openFlagsCount} cờ");

        // 7. Risk Events Card
        var riskQuery = db.RiskEvents.AsNoTracking()
            .Include(r => r.Employee)
            .Where(r => r.ReversedAt == null && r.OccurredAt >= periodStartOffset && r.OccurredAt < periodEndOffset);

        if (departmentIds is not null)
        {
            riskQuery = riskQuery.Where(r => departmentIds.Contains(r.Employee.DepartmentId));
        }

        if (filter.JobPositionId is { } riskPosId)
        {
            riskQuery = riskQuery.Where(r => r.Employee.JobPositionId == riskPosId);
        }

        if (!string.IsNullOrWhiteSpace(filter.JobPositionCode))
        {
            var riskCode = filter.JobPositionCode.Trim();
            riskQuery = riskQuery.Where(r => r.Employee.JobPosition.Code == riskCode);
        }

        var riskEvents = await riskQuery.ToListAsync(cancellationToken);
        var totalPenalty = riskEvents.Sum(EvaluationService.RiskPenalty);

        var riskEventsCard = new QaDashboardRiskEventsCardDto(
            riskEvents.Count,
            totalPenalty,
            $"tổng phạt -{totalPenalty:0.#} điểm");

        var cards = new QaDashboardCardsDto(notMatchedCard, inReviewCard, qaQueueCard, riskEventsCard);

        // 8. Weekly Matched Rate Chart (Last 8 calendar weeks)
        // Anchor on the selected period (not today) and read the 8-week window independently of the period range,
        // otherwise weeks before the period start would be empty and misreported.
        var chartAnchor = today < period.StartDate ? period.StartDate : (today > period.EndDate ? period.EndDate : today);
        var chartTransactions = await LoadChartTransactionsAsync(chartAnchor, departmentIds, filter, cancellationToken);
        var weeklyChart = BuildWeeklyMatchedRateChart(chartAnchor, chartTransactions);

        // 9. Top Discrepancy Employees (flag column = open flags, consistent with the QA queue card)
        var openFlagsByEmployee = await flagsQuery
            .GroupBy(f => f.EmployeeId)
            .Select(g => new { EmployeeId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.EmployeeId, x => x.Count, cancellationToken);

        var topEmployees = notMatchedList
            .GroupBy(t => t.Work.AssigneeEmployeeId)
            .Select(g =>
            {
                var first = g.First();
                var emp = first.Work.AssigneeEmployee;
                var discCount = g.Count();
                var flagCount = openFlagsByEmployee.GetValueOrDefault(emp.Id);

                return new QaDashboardTopEmployeeDto(
                    emp.Id,
                    emp.EmployeeCode,
                    emp.FullName,
                    emp.JobPosition?.Title ?? "N/A",
                    emp.Department?.Name,
                    discCount,
                    flagCount);
            })
            .OrderByDescending(e => e.DiscrepancyCount)
            .ThenByDescending(e => e.FlagCount)
            .ThenBy(e => e.EmployeeCode, StringComparer.Ordinal)
            .Take(10)
            .ToList();

        // 10. Common Discrepancy Reasons
        var commonReasons = BuildCommonReasons(notMatchedList, inReviewList);

        return new QaDashboardDto(
            now,
            periodDto,
            banner,
            cards,
            weeklyChart,
            topEmployees,
            commonReasons);
    }

    public async Task<PagedResult<QaReconciliationItemDto>> GetReconciliationsAsync(
        QaReconciliationFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var query = db.WorkTransactions.AsNoTracking()
            .Include(t => t.Work).ThenInclude(w => w.AssigneeEmployee).ThenInclude(e => e.JobPosition)
            .Include(t => t.Work).ThenInclude(w => w.AssigneeEmployee).ThenInclude(e => e.Department)
            .Include(t => t.ImportedTransaction)
            .AsQueryable();

        if (filter.PeriodId is { } periodId)
        {
            var period = await db.EvaluationPeriods.AsNoTracking().FirstOrDefaultAsync(p => p.Id == periodId, cancellationToken)
                ?? throw new NotFoundException("Kỳ đánh giá", periodId);

            var startOffset = new DateTimeOffset(period.StartDate.ToDateTime(TimeOnly.MinValue), BusinessOffset).ToUniversalTime();
            var endOffset = new DateTimeOffset(period.EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue), BusinessOffset).ToUniversalTime();
            query = query.Where(t => t.CreatedAt >= startOffset && t.CreatedAt < endOffset);
        }

        if (filter.DepartmentId is { } deptId)
        {
            var deptIds = (await DepartmentHierarchy.LoadAsync(db.Departments, cancellationToken)).Subtree(deptId);
            query = query.Where(t => deptIds.Contains(t.Work.AssigneeEmployee.DepartmentId));
        }

        if (filter.JobPositionId is { } posId)
        {
            query = query.Where(t => t.Work.AssigneeEmployee.JobPositionId == posId);
        }

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var status = filter.Status.Trim().ToUpperInvariant();
            if (status is not (WorkTransactionStatus.Matched or WorkTransactionStatus.NotMatched or WorkTransactionStatus.InReview))
            {
                throw new ValidationException($"Trạng thái '{filter.Status}' không hợp lệ. Giá trị cho phép: MATCHED, NOT_MATCHED, IN_REVIEW.");
            }

            query = query.Where(t => t.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(filter.Reason))
        {
            var reason = filter.Reason.Trim();
            query = query.Where(t => t.Reason == reason);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            query = query.Where(t =>
                t.TransactionCode.Contains(search) ||
                t.Work.Code.Contains(search) ||
                t.Work.Title.Contains(search) ||
                t.Work.AssigneeEmployee.EmployeeCode.Contains(search) ||
                t.Work.AssigneeEmployee.FullName.Contains(search));
        }

        var totalItems = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new
            {
                t.Id,
                t.TransactionCode,
                t.WorkId,
                WorkCode = t.Work.Code,
                WorkTitle = t.Work.Title,
                t.Work.AssigneeEmployeeId,
                EmployeeCode = t.Work.AssigneeEmployee.EmployeeCode,
                FullName = t.Work.AssigneeEmployee.FullName,
                JobTitle = t.Work.AssigneeEmployee.JobPosition != null ? t.Work.AssigneeEmployee.JobPosition.Title : null,
                DepartmentName = t.Work.AssigneeEmployee.Department != null ? t.Work.AssigneeEmployee.Department.Name : null,
                t.Status,
                t.Reason,
                ProductCode = t.ImportedTransaction != null ? t.ImportedTransaction.ProductCode : null,
                Amount = t.ImportedTransaction != null ? (decimal?)t.ImportedTransaction.Amount : null,
                TransactionDate = t.ImportedTransaction != null ? (DateOnly?)t.ImportedTransaction.TransactionDate : null,
                t.CreatedAt,
                t.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(t => new QaReconciliationItemDto(
            t.Id,
            t.TransactionCode,
            t.WorkId,
            t.WorkCode,
            t.WorkTitle,
            t.AssigneeEmployeeId,
            t.EmployeeCode,
            t.FullName,
            t.JobTitle,
            t.DepartmentName,
            t.Status,
            t.Reason,
            FormatReasonDescription(t.Status, t.Reason),
            t.ProductCode,
            t.Amount,
            t.TransactionDate,
            t.CreatedAt,
            t.UpdatedAt)).ToList();

        return new PagedResult<QaReconciliationItemDto>(items, totalItems, page, pageSize);
    }

    public async Task<QaDashboardFilterOptionsDto> GetFilterOptionsAsync(CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(_time.GetUtcNow().ToOffset(BusinessOffset).DateTime);

        // The dashboard header selector lists monthly periods only (mockup: "Kỳ: 10/2026").
        var periods = await db.EvaluationPeriods.AsNoTracking()
            .Where(p => p.PeriodType == PeriodType.Month)
            .OrderByDescending(p => p.StartDate)
            .Select(p => new QaDashboardPeriodOptionDto(
                p.Id,
                p.Code,
                FormatPeriodName(p),
                p.StartDate <= today && p.EndDate >= today))
            .ToListAsync(cancellationToken);

        var departments = await db.Departments.AsNoTracking()
            .Where(d => d.Status == RecordStatus.Active)
            .OrderBy(d => d.Code)
            .Select(d => new QaDashboardDepartmentOptionDto(d.Id, d.Code, d.Name))
            .ToListAsync(cancellationToken);

        var positions = await db.JobPositions.AsNoTracking()
            .OrderBy(j => j.Code)
            .Select(j => new QaDashboardJobPositionOptionDto(j.Id, j.Code, j.Title))
            .ToListAsync(cancellationToken);

        return new QaDashboardFilterOptionsDto(periods, departments, positions);
    }

    private async Task<EvaluationPeriod> ResolvePeriodAsync(Guid? periodId, DateOnly today, CancellationToken cancellationToken)
    {
        if (periodId is { } id)
        {
            return await db.EvaluationPeriods.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                ?? throw new NotFoundException("Kỳ đánh giá", id);
        }

        // Active period covering today
        var currentPeriod = await db.EvaluationPeriods.AsNoTracking()
            .Where(p => p.PeriodType == PeriodType.Month && p.StartDate <= today && p.EndDate >= today)
            .OrderByDescending(p => p.StartDate)
            .FirstOrDefaultAsync(cancellationToken);

        if (currentPeriod is not null)
        {
            return currentPeriod;
        }

        // Latest period if no active covering today
        return await db.EvaluationPeriods.AsNoTracking()
            .Where(p => p.PeriodType == PeriodType.Month)
            .OrderByDescending(p => p.EndDate)
            .FirstOrDefaultAsync(cancellationToken)
            ?? new EvaluationPeriod
            {
                Id = Guid.NewGuid(),
                Code = $"PER-{today:yyyy-MM}",
                Name = $"Tháng {today:MM/yyyy}",
                StartDate = new DateOnly(today.Year, today.Month, 1),
                EndDate = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month)),
                Status = PeriodStatus.Open,
            };
    }

    private static string FormatPeriodName(EvaluationPeriod period)
    {
        if (!string.IsNullOrWhiteSpace(period.Name) && (period.Name.Contains('/') || period.Name.StartsWith("Tháng", StringComparison.OrdinalIgnoreCase)))
        {
            return period.Name.Replace("Tháng ", string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        return $"{period.StartDate:MM/yyyy}";
    }

    private async Task<List<WorkTransaction>> LoadChartTransactionsAsync(
        DateOnly anchorDate,
        IReadOnlyCollection<Guid>? departmentIds,
        QaDashboardFilterDto filter,
        CancellationToken cancellationToken)
    {
        var diffToMonday = anchorDate.DayOfWeek == DayOfWeek.Sunday ? 6 : (int)anchorDate.DayOfWeek - 1;
        var firstMonday = anchorDate.AddDays(-diffToMonday - 7 * 7);
        var lastSunday = anchorDate.AddDays(-diffToMonday + 6);
        var from = new DateTimeOffset(firstMonday.ToDateTime(TimeOnly.MinValue), BusinessOffset).ToUniversalTime();
        var to = new DateTimeOffset(lastSunday.AddDays(1).ToDateTime(TimeOnly.MinValue), BusinessOffset).ToUniversalTime();

        var query = db.WorkTransactions.AsNoTracking()
            .Where(t => t.CreatedAt >= from && t.CreatedAt < to);

        if (departmentIds is not null)
        {
            query = query.Where(t => departmentIds.Contains(t.Work.AssigneeEmployee.DepartmentId));
        }

        if (filter.JobPositionId is { } posId)
        {
            query = query.Where(t => t.Work.AssigneeEmployee.JobPositionId == posId);
        }

        if (!string.IsNullOrWhiteSpace(filter.JobPositionCode))
        {
            var code = filter.JobPositionCode.Trim();
            query = query.Where(t => t.Work.AssigneeEmployee.JobPosition.Code == code);
        }

        return await query.ToListAsync(cancellationToken);
    }

    private static QaDashboardMatchedRateChartDto BuildWeeklyMatchedRateChart(
        DateOnly anchorDate,
        IReadOnlyList<WorkTransaction> transactions)
    {
        // 8 weeks ending on the current week's Sunday
        var weeks = new List<QaDashboardWeeklyRateDto>();

        // Find Monday of the current anchor week
        var currentDayOfWeek = (int)anchorDate.DayOfWeek;
        // In .NET, Sunday is 0. Convert so Monday is 0, Sunday is 6
        var diffToMonday = currentDayOfWeek == 0 ? 6 : currentDayOfWeek - 1;
        var currentWeekMonday = anchorDate.AddDays(-diffToMonday);

        for (var i = 7; i >= 0; i--)
        {
            var weekMonday = currentWeekMonday.AddDays(-i * 7);
            var weekSunday = weekMonday.AddDays(6);
            var weekNum = ISOWeek.GetWeekOfYear(weekMonday.ToDateTime(TimeOnly.MinValue));
            var year = weekMonday.Year;

            var weekStartOffset = new DateTimeOffset(weekMonday.ToDateTime(TimeOnly.MinValue), BusinessOffset).ToUniversalTime();
            var weekEndOffset = new DateTimeOffset(weekSunday.AddDays(1).ToDateTime(TimeOnly.MinValue), BusinessOffset).ToUniversalTime();

            var weekTxs = transactions.Where(t => t.CreatedAt >= weekStartOffset && t.CreatedAt < weekEndOffset).ToList();
            var matched = weekTxs.Count(t => t.Status == WorkTransactionStatus.Matched);
            var total = weekTxs.Count(t => t.Status is WorkTransactionStatus.Matched or WorkTransactionStatus.NotMatched);

            // No decided transactions in the week => no rate (null), never a fake 100%.
            decimal? rate = total > 0 ? Math.Round((decimal)matched / total * 100m, 1) : null;

            weeks.Add(new QaDashboardWeeklyRateDto(
                $"T{weekNum}",
                weekNum,
                year,
                weekMonday,
                weekSunday,
                matched,
                total,
                rate));
        }

        // Headline = latest week that actually has decided transactions (mockup: "T43 · 95,6%").
        var latest = weeks.LastOrDefault(w => w.MatchRate.HasValue) ?? weeks[^1];
        return new QaDashboardMatchedRateChartDto(
            "Tỷ lệ Matched theo tuần",
            "8 tuần gần nhất",
            latest.MatchRate,
            latest.WeekLabel,
            weeks);
    }

    private static List<QaDashboardReasonCountDto> BuildCommonReasons(
        List<WorkTransaction> notMatched,
        List<WorkTransaction> inReview)
    {
        var dict = new Dictionary<string, (string Label, int Count)>(StringComparer.OrdinalIgnoreCase);

        void Add(string code, string label, int count)
        {
            if (count <= 0) return;
            if (dict.TryGetValue(code, out var existing))
            {
                dict[code] = (existing.Label, existing.Count + count);
            }
            else
            {
                dict[code] = (label, count);
            }
        }

        foreach (var item in notMatched)
        {
            var code = item.Reason == "REFER_MISMATCH" ? "REFER_ID_MISSING" : item.Reason ?? "UNKNOWN_MISMATCH";
            var label = FormatReasonDescription(WorkTransactionStatus.NotMatched, item.Reason);
            Add(code, label, 1);
        }

        if (inReview.Count > 0)
        {
            Add("NO_IMPORTED_TRANSACTION", "Chưa có GD import", inReview.Count);
        }

        return dict
            .OrderByDescending(kv => kv.Value.Count)
            .ThenBy(kv => kv.Value.Label, StringComparer.Ordinal)
            .Select(kv => new QaDashboardReasonCountDto(kv.Key, kv.Value.Label, kv.Value.Count))
            .ToList();
    }

    private static string FormatReasonDescription(string status, string? reason)
    {
        if (status == WorkTransactionStatus.InReview)
        {
            return "Chưa có GD import";
        }

        return reason switch
        {
            "EMPLOYEE_MISMATCH" => "Sai mã nhân viên",
            "PRODUCT_MISMATCH" => "Sai sản phẩm",
            "REFER_ID_MISSING" or "REFER_MISMATCH" => "Thiếu Refer ID",
            "AMOUNT_MISMATCH" => "Lệch số tiền",
            "METRIC_TYPE_MISMATCH" => "Sai loại chỉ số",
            "KPI_TARGET_NOT_ASSIGNED" => "Chưa phân bổ mục tiêu KPI",
            "KPI_AGGREGATION_NOT_SUPPORTED" => "Phương pháp tổng hợp KPI không hỗ trợ",
            _ => string.IsNullOrWhiteSpace(reason) ? "Không khớp" : reason,
        };
    }
}
