using System.Globalization;
using ClosedXML.Excel;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Dtos.Evaluation;
using G54.BLL.Dtos.SystemParameter;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

/// <summary>
/// Department ranking board (`/evaluations/department-ranking`): tier slots, system and final tiers, the send to the
/// Branch Director, its Excel export and the two-employee comparison (`docs/api/RANKING_BOARD_CONTRACT.md`).
/// </summary>
public sealed partial class EvaluationService
{
    private static readonly string[] RankingBoardTierCodes = [.. RankingSlotTierCodes.All];

    private static readonly (string Key, string Label)[] CompareGroupKeys =
    [
        ("business", "Kinh doanh"),
        ("serviceQuality", "Chất lượng dịch vụ"),
        ("compliance", "Tuân thủ"),
    ];

    public async Task<RankingBoardOverviewDto> GetRankingBoardAsync(
        Guid periodId,
        Guid? departmentId,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var calibration = await GetDepartmentCalibrationAsync(periodId, departmentId, null, currentUserId, cancellationToken);
        var period = await db.EvaluationPeriods.AsNoTracking().FirstAsync(p => p.Id == periodId, cancellationToken);
        var deptId = calibration.Department.Id;
        var rows = calibration.RankedRoster;

        var slots = await db.RankingSlotQuotas
            .AsNoTracking()
            .Where(q => q.DepartmentId == deptId && !q.IsDeleted)
            .ToDictionaryAsync(q => q.TierCode, q => q.Headcount, cancellationToken);

        var tiers = RankingBoardTierCodes.Select(code =>
        {
            int? headcount = slots.TryGetValue(code, out var value) ? value : null;
            var actual = rows.Count(r => r.CalibratedTier == code);
            return new RankingBoardTierDto(code, headcount, actual, headcount == actual);
        }).ToList();

        var comparePeriod = await ResolveRankingComparePeriodAsync(period, cancellationToken);
        var staffRows = await BuildRankingStaffRowsAsync(rows, period, comparePeriod, currentUserId, cancellationToken);

        var blockers = BuildRankingSendBlockers(tiers, staffRows);
        var isLocked = IsRankingLocked(period);

        return new RankingBoardOverviewDto(
            deptId,
            calibration.Department.Name,
            period.Id,
            period.Code,
            period.Name,
            comparePeriod?.Id,
            comparePeriod?.Code,
            Params.RankingLockDay,
            isLocked,
            isLocked && blockers.Count == 0,
            blockers,
            tiers,
            staffRows,
            timeProvider.GetUtcNow());
    }

    public async Task<RankingBoardOverviewDto> SaveRankingBoardDraftAsync(
        SaveRankingBoardRequestDto request,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        await SaveRankingAdjustmentsAsync(request, isDraft: true, currentUserId, cancellationToken);
        return await GetRankingBoardAsync(request.PeriodId, request.DepartmentId, currentUserId, cancellationToken);
    }

    public async Task<RankingBoardSendResultDto> SendRankingBoardToDirectorAsync(
        SaveRankingBoardRequestDto request,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        await EnsureBranchScopeAsync(currentUserId, "finalize", cancellationToken);

        var board = await GetRankingBoardAsync(request.PeriodId, request.DepartmentId, currentUserId, cancellationToken);
        if (!board.IsLocked)
        {
            throw new BusinessRuleException(
                ErrorCodes.Conflict,
                $"Chưa đến ngày chốt xếp hạng ({board.RankingLockDay}), chưa thể gửi giám đốc chi nhánh.", 409);
        }

        var blockers = board.SendBlockers;
        if (blockers.Count > 0)
        {
            throw new BusinessRuleException(ErrorCodes.Conflict, string.Join(" ", blockers), 409);
        }

        await SaveRankingAdjustmentsAsync(request, isDraft: false, currentUserId, cancellationToken);

        return new RankingBoardSendResultDto(
            request.DepartmentId,
            request.PeriodId,
            "IN_REVIEW",
            timeProvider.GetUtcNow(),
            "Đã gửi bảng xếp hạng cho giám đốc chi nhánh.");
    }

    public async Task<RankingBoardExportDto> ExportRankingBoardAsync(
        Guid periodId,
        Guid? departmentId,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var board = await GetRankingBoardAsync(periodId, departmentId, currentUserId, cancellationToken);

        using var workbook = new XLWorkbook();
        workbook.Style.Font.FontName = "Segoe UI";
        workbook.Style.Font.FontSize = 10;

        var tierSheet = workbook.AddWorksheet("Hạng");
        WriteRankingRow(tierSheet, 1, "Hạng", "Slot", "Thực tế");
        for (var i = 0; i < board.Tiers.Count; i++)
        {
            var tier = board.Tiers[i];
            WriteRankingRow(
                tierSheet,
                i + 2,
                tier.TierCode,
                tier.Headcount?.ToString(CultureInfo.InvariantCulture) ?? "—",
                tier.ActualCount.ToString(CultureInfo.InvariantCulture));
        }

        var staffSheet = workbook.AddWorksheet("Nhân viên");
        WriteRankingRow(staffSheet, 1, "STT", "Nhân viên", "SLA đúng hạn (%)", "Đạt chỉ tiêu", "Sự kiện rủi ro",
            "Phạt", "Điểm định lượng", "Hạng hệ thống", "Hạng chốt", "Lý do điều chỉnh");
        for (var i = 0; i < board.StaffRows.Count; i++)
        {
            var row = board.StaffRows[i];
            WriteRankingRow(
                staffSheet,
                i + 2,
                row.Rank.ToString(CultureInfo.InvariantCulture),
                row.FullName,
                FormatRankingNumber(row.OnTimeSla.Value),
                row.AchievedCriteria.HasValue
                    ? string.Create(CultureInfo.InvariantCulture, $"{row.AchievedCriteria}/{row.TotalCriteria}")
                    : "—",
                FormatRankingNumber(row.RiskEventCount.Value),
                FormatRankingNumber(row.RiskPenalty.Value),
                FormatRankingNumber(row.QuantitativeScore.Value),
                row.SystemTier ?? "—",
                row.FinalTier ?? "—",
                row.AdjustmentReason ?? string.Empty);
        }

        foreach (var sheet in workbook.Worksheets)
        {
            sheet.Row(1).Style.Font.Bold = true;
            sheet.Columns().AdjustToContents();
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return new RankingBoardExportDto(
            stream.ToArray(),
            $"xep-hang-phong-{board.PeriodCode}.xlsx");
    }

    public async Task<RankingCompareDto> GetRankingCompareAsync(
        Guid periodId,
        Guid employeeId1,
        Guid employeeId2,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var period = await db.EvaluationPeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == periodId, cancellationToken)
            ?? throw new NotFoundException("Kỳ đánh giá", periodId);

        var first = await LoadRankingCompareEmployeeAsync(employeeId1, period, currentUserId, cancellationToken);
        var second = await LoadRankingCompareEmployeeAsync(employeeId2, period, currentUserId, cancellationToken);

        var metrics = new List<RankingCompareMetricDto>
        {
            CompareRankingMetric("quantitative", "Điểm định lượng", first.Quantitative, second.Quantitative, higherIsBetter: true),
            CompareRankingMetric("criteria", "Chỉ tiêu đạt", first.CriteriaRate, second.CriteriaRate, higherIsBetter: true),
            CompareRankingMetric("sla", "SLA đúng hạn", first.Sla, second.Sla, higherIsBetter: true),
            CompareRankingMetric("risk", "Sự kiện rủi ro", first.RiskCount, second.RiskCount, higherIsBetter: false),
        };

        foreach (var (key, label) in CompareGroupKeys)
        {
            metrics.Add(CompareRankingMetric(
                key,
                label,
                first.Groups.GetValueOrDefault(key),
                second.Groups.GetValueOrDefault(key),
                higherIsBetter: true));
        }

        return new RankingCompareDto(
            new RankingCompareEmployeeDto(first.Id, first.Code, first.Name),
            new RankingCompareEmployeeDto(second.Id, second.Code, second.Name),
            metrics);
    }

    private async Task<EvaluationPeriod?> ResolveRankingComparePeriodAsync(EvaluationPeriod period, CancellationToken cancellationToken)
    {
        return await db.EvaluationPeriods
            .AsNoTracking()
            .Where(p => p.PeriodType == period.PeriodType && p.StartDate < period.StartDate)
            .OrderByDescending(p => p.StartDate)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<List<RankingBoardStaffRowDto>> BuildRankingStaffRowsAsync(
        List<CalibrationEmployeeRowDto> rows,
        EvaluationPeriod period,
        EvaluationPeriod? comparePeriod,
        Guid currentUserId,
        CancellationToken cancellationToken)
    {
        var employeeIds = rows.Select(r => r.EmployeeId).ToList();
        var compareRows = comparePeriod is null
            ? []
            : (await GetDepartmentCalibrationAsync(comparePeriod.Id, rows.Count > 0 ? rows[0].DepartmentId : null, null, currentUserId, cancellationToken))
                .RankedRoster;

        var risks = await LoadRiskEventsByEmployeeAsync(employeeIds, period, cancellationToken);
        var compareRisks = comparePeriod is null
            ? new Dictionary<Guid, List<RiskEvent>>()
            : await LoadRiskEventsByEmployeeAsync(employeeIds, comparePeriod, cancellationToken);

        var staffRows = new List<RankingBoardStaffRowDto>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var previous = compareRows.FirstOrDefault(r => r.EmployeeId == row.EmployeeId);
            var sla = (await LoadEmployeeSlaSummaryAsync(row.EmployeeId, period, cancellationToken)).OnTimeRatePercent;
            var prevSla = comparePeriod is null
                ? null
                : (await LoadEmployeeSlaSummaryAsync(row.EmployeeId, comparePeriod, cancellationToken)).OnTimeRatePercent;
            var riskCount = risks.GetValueOrDefault(row.EmployeeId)?.Count ?? 0;
            var prevRiskCount = compareRisks.GetValueOrDefault(row.EmployeeId)?.Count;
            var penalty = (risks.GetValueOrDefault(row.EmployeeId) ?? []).Sum(r => RiskPenalty(r, Params));
            var prevPenalty = comparePeriod is null
                ? (decimal?)null
                : (compareRisks.GetValueOrDefault(row.EmployeeId) ?? []).Sum(r => RiskPenalty(r, Params));
            var (achieved, total) = await LoadRankingCriteriaCountAsync(row.EmployeeId, period, cancellationToken);

            staffRows.Add(new RankingBoardStaffRowDto(
                i + 1,
                row.EmployeeId,
                row.EmployeeCode,
                row.FullName,
                DeltaOf(sla, prevSla),
                achieved,
                total,
                DeltaOf(riskCount, prevRiskCount),
                DeltaOf(penalty, prevPenalty),
                DeltaOf(row.TotalScore, previous?.TotalScore),
                row.ProposedTier,
                row.CalibratedTier,
                row.AdjustmentReason,
                row.EvaluationStatus is "IN_REVIEW" or "APPROVED" or "FINAL"));
        }

        return staffRows;
    }

    private async Task<(int? Achieved, int? Total)> LoadRankingCriteriaCountAsync(
        Guid employeeId,
        EvaluationPeriod period,
        CancellationToken cancellationToken)
    {
        var profile = await FindProfileAsync(
            await db.Employees.AsNoTracking().Where(e => e.Id == employeeId).Select(e => e.JobPositionId).FirstAsync(cancellationToken),
            period,
            cancellationToken);
        if (profile is null)
        {
            return (null, null);
        }

        var employee = await db.Employees.AsNoTracking().FirstAsync(e => e.Id == employeeId, cancellationToken);
        var scored = (await calculator.CalculateAsync(
            [new PerformanceScoreSubject(employee.Id, employee.DepartmentId, profile, new Dictionary<Guid, QualitativeScoreInputDto>())],
            period,
            cancellationToken))[employeeId];

        var kpis = scored.Groups.Where(g => !g.IsQualitative).SelectMany(g => g.Kpis).ToList();
        var achieved = kpis.Count(k => k.AchievementPercentCapped.HasValue && k.AchievementPercentCapped.Value >= Params.EvalKpiMetThresholdPercent);
        return (achieved, kpis.Count);
    }

    private static RankingBoardDeltaDto DeltaOf(decimal? value, decimal? previous) =>
        new(value, previous, value.HasValue && previous.HasValue ? value.Value - previous.Value : null);

    private static RankingBoardDeltaDto DeltaOf(int value, int? previous) =>
        new(value, previous, previous.HasValue ? value - previous.Value : null);

    private bool IsRankingLocked(EvaluationPeriod period) =>
        timeProvider.GetBusinessToday() >= RankingLockDate(period, Params.RankingLockDay);

    /// <summary>
    /// Month periods lock in their own month, quarter periods in the last month of the quarter and year periods in
    /// December. The lock day is capped at the last day of that month.
    /// </summary>
    private static DateOnly RankingLockDate(EvaluationPeriod period, int lockDay)
    {
        var lockMonthDate = period.PeriodType switch
        {
            PeriodType.Quarter => period.EndDate,
            PeriodType.Year => new DateOnly(period.EndDate.Year, 12, 1),
            _ => period.EndDate,
        };
        var day = Math.Min(lockDay, DateTime.DaysInMonth(lockMonthDate.Year, lockMonthDate.Month));
        return new DateOnly(lockMonthDate.Year, lockMonthDate.Month, day);
    }

    private static List<string> BuildRankingSendBlockers(
        IReadOnlyList<RankingBoardTierDto> tiers,
        IReadOnlyList<RankingBoardStaffRowDto> staffRows)
    {
        var blockers = new List<string>();
        foreach (var tier in tiers.Where(t => t.TierCode != "C"))
        {
            if (tier.Headcount is null)
            {
                blockers.Add($"Hạng {tier.TierCode} chưa cấu hình slot.");
            }
            else if (!tier.IsFilled)
            {
                blockers.Add($"Hạng {tier.TierCode} chưa đủ slot ({tier.ActualCount}/{tier.Headcount}).");
            }
        }

        var unconfirmed = staffRows.Count(r => !r.IsConfirmed);
        if (unconfirmed > 0)
        {
            blockers.Add($"Còn {unconfirmed} nhân viên chưa được xác nhận.");
        }

        var missingReason = staffRows.Count(r => r.FinalTier is not null && r.FinalTier != r.SystemTier && string.IsNullOrWhiteSpace(r.AdjustmentReason));
        if (missingReason > 0)
        {
            blockers.Add($"Còn {missingReason} nhân viên đổi hạng nhưng thiếu lý do điều chỉnh.");
        }

        return blockers;
    }

    private async Task SaveRankingAdjustmentsAsync(
        SaveRankingBoardRequestDto request,
        bool isDraft,
        Guid currentUserId,
        CancellationToken cancellationToken)
    {
        var adjustments = request.Adjustments
            .Select(a => new CalibrationAdjustmentItemDto(a.EmployeeId, a.FinalTier, a.Reason))
            .ToList();
        await SubmitDepartmentCalibrationAsync(
            new SubmitCalibrationRequestDto(request.DepartmentId, request.PeriodId, adjustments, null, isDraft),
            currentUserId,
            enforceTierAQuota: false,
            cancellationToken);
    }

    private static void WriteRankingRow(IXLWorksheet sheet, int row, params string[] values)
    {
        for (var c = 0; c < values.Length; c++)
        {
            sheet.Cell(row, c + 1).Value = values[c];
        }
    }

    private static string FormatRankingNumber(decimal? value) =>
        value?.ToString("0.#", CultureInfo.InvariantCulture) ?? "—";

    private async Task EnsureBranchScopeAsync(Guid actorAccountId, string action, CancellationToken cancellationToken)
    {
        var granted = await permissions.GetEffectivePermissionsAsync(actorAccountId, cancellationToken);
        if (AccessScope.LevelFor(granted, "evaluation", action) < AccessScope.BranchLevel)
        {
            throw new ForbiddenException("Bạn không có quyền gửi bảng xếp hạng cho giám đốc chi nhánh.");
        }
    }

    private async Task<RankingCompareSubject> LoadRankingCompareEmployeeAsync(
        Guid employeeId,
        EvaluationPeriod period,
        Guid currentUserId,
        CancellationToken cancellationToken)
    {
        var employee = await db.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken)
            ?? throw new NotFoundException("Nhân viên", employeeId);
        var reviewer = await ResolveEmployeeAsync(currentUserId, cancellationToken);
        await EnsureDepartmentInScopeAsync(currentUserId, "read", reviewer, employee.DepartmentId, cancellationToken);

        var profile = await FindProfileAsync(employee.JobPositionId, period, cancellationToken);
        var evaluation = await db.PerformanceEvaluations
            .AsNoTracking()
            .FirstOrDefaultAsync(pe => pe.EmployeeId == employeeId && pe.EvaluationPeriodId == period.Id, cancellationToken);
        var scored = profile is null
            ? null
            : (await calculator.CalculateAsync(
                [new PerformanceScoreSubject(employee.Id, employee.DepartmentId, profile, new Dictionary<Guid, QualitativeScoreInputDto>())],
                period,
                cancellationToken))[employeeId];

        var (business, serviceQuality, operations) = scored?.ComponentScores() ?? (null, null, null);
        var groups = new Dictionary<string, decimal?>
        {
            ["business"] = business,
            ["serviceQuality"] = serviceQuality,
            ["compliance"] = operations,
        };

        var sla = (await LoadEmployeeSlaSummaryAsync(employeeId, period, cancellationToken)).OnTimeRatePercent;
        var counts = await LoadRankingCriteriaCountAsync(employeeId, period, cancellationToken);
        var risks = (await LoadRiskEventsByEmployeeAsync([employeeId], period, cancellationToken)).GetValueOrDefault(employeeId) ?? [];

        decimal? criteriaRate = counts.Total is > 0
            ? Math.Round((decimal)counts.Achieved!.Value * 100m / counts.Total.Value, 1, MidpointRounding.AwayFromZero)
            : null;

        return new RankingCompareSubject(
            employee.Id,
            employee.EmployeeCode,
            employee.FullName,
            KeepsStoredScore(evaluation) ? evaluation!.TotalScore : scored?.TotalScore,
            criteriaRate,
            sla,
            risks.Count,
            groups);
    }

    private sealed record RankingCompareSubject(
        Guid Id,
        string Code,
        string Name,
        decimal? Quantitative,
        decimal? CriteriaRate,
        decimal? Sla,
        int RiskCount,
        Dictionary<string, decimal?> Groups);

    private static RankingCompareMetricDto CompareRankingMetric(
        string key,
        string label,
        decimal? value1,
        decimal? value2,
        bool higherIsBetter)
    {
        string? winner = null;
        decimal? difference = null;
        if (value1.HasValue && value2.HasValue && value1.Value != value2.Value)
        {
            var employee1Wins = (value1.Value > value2.Value) == higherIsBetter;
            winner = employee1Wins ? "EMPLOYEE_1" : "EMPLOYEE_2";
            difference = Math.Abs(value1.Value - value2.Value);
        }

        return new RankingCompareMetricDto(key, label, value1, value2, winner, difference, []);
    }
}
