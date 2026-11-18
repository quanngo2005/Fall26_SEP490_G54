using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using G54.BLL.Common;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Dashboard;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

public sealed class MyDashboardService(
    AppDbContext db,
    IWorkingDayCalendar workingCalendar,
    IPerformanceScoreFormula formula,
    TimeProvider time) : IMyDashboardService
{
    private const string ActionUrl = "/my-targets";

    public async Task<MyProgressCardDto> GetMyProgressCardAsync(
        Guid accountId,
        MyProgressCardQueryDto query,
        CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(accountId, cancellationToken);
        var period = await ResolvePeriodAsync(query, cancellationToken);

        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var workingDays = await workingCalendar.GetWorkingDaysAsync(null, period.StartDate, period.EndDate, cancellationToken);
        var totalWorkingDays = workingDays.Count;
        var elapsedWorkingDays = workingDays.Count(d => d <= today);

        decimal? expectedRateToday = totalWorkingDays > 0
            ? Math.Round((decimal)elapsedWorkingDays / totalWorkingDays * 100m, 0, MidpointRounding.AwayFromZero)
            : 0m;

        var role = caller.JobPositionCode switch
        {
            JobPositionCodes.Teller => "TELLER",
            JobPositionCodes.Sales => "SALES",
            _ => "OTHER"
        };

        var currentRate = await CalculateRateForPeriodAsync(caller, period, cancellationToken);

        var previousPeriod = await ResolvePreviousPeriodAsync(period, cancellationToken);
        PeriodComparisonDto? comparison = null;
        if (previousPeriod is not null)
        {
            var previousRate = await CalculateRateForPeriodAsync(caller, previousPeriod, cancellationToken);
            comparison = BuildComparison(period, previousPeriod, currentRate, previousRate, role);
        }

        var isSales = role == "SALES";
        var isQuarter = period.PeriodType == PeriodType.Quarter;

        var cardTitle = isQuarter
            ? (isSales ? $"Doanh số {period.Name}" : $"Hiệu suất {period.Name}")
            : (isSales ? $"Doanh số tháng {period.StartDate.Month}" : $"Hiệu suất tháng {period.StartDate.Month}");

        var (pacingStatus, pacingStatusLabel) = ResolvePacing(currentRate, expectedRateToday);

        string? policyNote = isSales ? "KPI Sales không trần" : null;
        string? expectedRateTodayText = isSales && currentRate >= 100m
            ? null
            : $"Kỳ vọng hôm nay {expectedRateToday:0}%";

        var periodDto = new DashboardPeriodInfoDto(
            period.Id,
            period.Code,
            period.Name,
            period.PeriodType.ToString().ToUpperInvariant(),
            period.StartDate,
            period.EndDate);

        var workingDaysDto = new ProgressWorkingDaysDto(totalWorkingDays, elapsedWorkingDays);

        return new MyProgressCardDto(
            Role: role,
            CardTitle: cardTitle,
            Period: periodDto,
            CurrentRate: Math.Round(currentRate, 0, MidpointRounding.AwayFromZero),
            PacingStatus: pacingStatus,
            PacingStatusLabel: pacingStatusLabel,
            ExpectedRateToday: expectedRateToday,
            ExpectedRateTodayText: expectedRateTodayText,
            PolicyNote: policyNote,
            PreviousPeriodComparison: comparison,
            WorkingDays: workingDaysDto,
            ActionUrl: ActionUrl);
    }

    private async Task<CallerInfo> ResolveCallerAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.AsNoTracking()
            .Where(a => a.Id == accountId && !a.IsLocked && a.Employee.Status == EmployeeStatus.Active)
            .Select(a => new CallerInfo(
                a.EmployeeId,
                a.Employee.FullName,
                a.Employee.DepartmentId,
                a.Employee.JobPositionId,
                a.Employee.JobPosition.Code))
            .SingleOrDefaultAsync(cancellationToken);

        if (account is null)
        {
            throw new ForbiddenException("Tài khoản chưa gắn với nhân viên hoặc đã bị khóa.");
        }

        return account;
    }

    private async Task<EvaluationPeriod> ResolvePeriodAsync(MyProgressCardQueryDto query, CancellationToken cancellationToken)
    {
        if (query.PeriodId.HasValue)
        {
            var period = await db.EvaluationPeriods.AsNoTracking()
                .SingleOrDefaultAsync(p => p.Id == query.PeriodId.Value, cancellationToken)
                ?? throw new NotFoundException("Kỳ đánh giá", query.PeriodId.Value);

            if (period.PeriodType is not (PeriodType.Month or PeriodType.Quarter))
            {
                throw new ValidationException("Thẻ tiến độ cá nhân chỉ hỗ trợ kỳ Tháng (MONTH) hoặc Quý (QUARTER).");
            }

            return period;
        }

        var isQuarter = string.Equals(query.PeriodType, "QUARTER", StringComparison.OrdinalIgnoreCase);
        var targetType = isQuarter ? PeriodType.Quarter : PeriodType.Month;
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

        var current = await db.EvaluationPeriods.AsNoTracking()
            .Where(p => p.PeriodType == targetType && p.StartDate <= today && p.EndDate >= today)
            .OrderByDescending(p => p.StartDate)
            .FirstOrDefaultAsync(cancellationToken);

        if (current is not null)
        {
            return current;
        }

        // Fallback to latest existing period of the requested type
        var fallback = await db.EvaluationPeriods.AsNoTracking()
            .Where(p => p.PeriodType == targetType)
            .OrderByDescending(p => p.EndDate)
            .FirstOrDefaultAsync(cancellationToken);

        return fallback ?? throw new NotFoundException("Không tìm thấy kỳ đánh giá phù hợp.");
    }

    private async Task<EvaluationPeriod?> ResolvePreviousPeriodAsync(EvaluationPeriod current, CancellationToken cancellationToken)
    {
        return await db.EvaluationPeriods.AsNoTracking()
            .Where(p => p.PeriodType == current.PeriodType && p.EndDate < current.StartDate)
            .OrderByDescending(p => p.EndDate)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<decimal> CalculateRateForPeriodAsync(
        CallerInfo caller,
        EvaluationPeriod period,
        CancellationToken cancellationToken)
    {
        // 1. Fetch active targets for this employee in this period
        var targets = await db.TargetAssignments.AsNoTracking()
            .Where(t => t.EmployeeId == caller.EmployeeId
                && t.EvaluationPeriodId == period.Id
                && t.Status == TargetAssignmentStatus.Active)
            .Include(t => t.KpiDefinition)
            .ToListAsync(cancellationToken);

        // If monthly period and no direct targets found, check if parent quarter has targets to prorate
        if (targets.Count == 0 && period.PeriodType == PeriodType.Month && period.ParentPeriodId.HasValue)
        {
            var quarterTargets = await db.TargetAssignments.AsNoTracking()
                .Where(t => t.EmployeeId == caller.EmployeeId
                    && t.EvaluationPeriodId == period.ParentPeriodId.Value
                    && t.Status == TargetAssignmentStatus.Active)
                .Include(t => t.KpiDefinition)
                .ToListAsync(cancellationToken);

            if (quarterTargets.Count > 0)
            {
                var quarterPeriod = await db.EvaluationPeriods.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == period.ParentPeriodId.Value, cancellationToken);

                if (quarterPeriod != null)
                {
                    var quarterDays = await workingCalendar.GetWorkingDaysAsync(null, quarterPeriod.StartDate, quarterPeriod.EndDate, cancellationToken);
                    var monthDays = await workingCalendar.GetWorkingDaysAsync(null, period.StartDate, period.EndDate, cancellationToken);

                    if (quarterDays.Count > 0)
                    {
                        var ratio = (decimal)monthDays.Count / quarterDays.Count;
                        targets = quarterTargets.Select(qt => new TargetAssignment
                        {
                            Id = qt.Id,
                            EmployeeId = qt.EmployeeId,
                            KpiDefinitionId = qt.KpiDefinitionId,
                            KpiDefinition = qt.KpiDefinition,
                            EvaluationPeriodId = period.Id,
                            TargetValue = Math.Round(qt.TargetValue * ratio, 4, MidpointRounding.AwayFromZero),
                            Status = qt.Status
                        }).ToList();
                    }
                }
            }
        }

        if (targets.Count == 0)
        {
            return 0m;
        }

        // 2. Fetch actual progress within period dates
        var actualProgresses = await db.ActualProgresses.AsNoTracking()
            .Where(ap => ap.Status == ActualProgressStatus.Valid
                && ap.TargetAssignment.EmployeeId == caller.EmployeeId
                && ap.ProgressDate >= period.StartDate
                && ap.ProgressDate <= period.EndDate)
            .Select(ap => new
            {
                ap.TargetAssignment.KpiDefinitionId,
                ap.Value,
                ap.Denominator
            })
            .ToListAsync(cancellationToken);

        var actualByKpi = actualProgresses.GroupBy(a => a.KpiDefinitionId)
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    SumValue = g.Sum(x => x.Value),
                    SumDenominator = g.Sum(x => x.Denominator ?? 0m)
                });

        var isTeller = string.Equals(caller.JobPositionCode, JobPositionCodes.Teller, StringComparison.OrdinalIgnoreCase);
        var isSales = string.Equals(caller.JobPositionCode, JobPositionCodes.Sales, StringComparison.OrdinalIgnoreCase);

        if (isSales)
        {
            // Sales Officer: Uncapped sales volume performance
            var validTargets = targets.Where(t => t.TargetValue > 0).ToList();
            if (validTargets.Count == 0)
            {
                return 0m;
            }

            var sumAchievement = 0m;
            foreach (var t in validTargets)
            {
                var actual = actualByKpi.TryGetValue(t.KpiDefinitionId, out var act) ? act.SumValue : 0m;
                var rawPercent = (actual / t.TargetValue) * 100m;
                sumAchievement += rawPercent;
            }

            return Math.Round(sumAchievement / validTargets.Count, 2, MidpointRounding.AwayFromZero);
        }

        if (isTeller)
        {
            // Teller: Weighted sum based on EvaluationProfileItem with capped achievement
            var profile = await db.EvaluationProfiles.AsNoTracking()
                .Include(ep => ep.EvaluationProfileItems)
                    .ThenInclude(epi => epi.KpiDefinition)
                .Where(ep => ep.JobPositionId == caller.JobPositionId
                    && ep.Status == EvaluationProfileStatus.Active
                    && ep.EffectiveFrom <= period.EndDate
                    && (ep.EffectiveTo == null || ep.EffectiveTo >= period.StartDate))
                .OrderByDescending(ep => ep.EffectiveFrom)
                .FirstOrDefaultAsync(cancellationToken);

            var leafItems = profile?.EvaluationProfileItems
                .Where(i => i.KpiDefinitionId.HasValue && i.KpiDefinition != null)
                .ToList();

            if (leafItems != null && leafItems.Count > 0)
            {
                var targetDict = targets.ToDictionary(t => t.KpiDefinitionId, t => t.TargetValue);
                var totalWeight = leafItems.Sum(i => i.WeightPercent);
                if (totalWeight <= 0m)
                {
                    totalWeight = 100m;
                }

                var weightedScore = 0m;
                foreach (var leaf in leafItems)
                {
                    var kpiId = leaf.KpiDefinitionId!.Value;
                    var targetVal = targetDict.TryGetValue(kpiId, out var tv) ? tv : 0m;
                    actualByKpi.TryGetValue(kpiId, out var act);

                    var kpiInput = new KpiScoreInput(
                        leaf.KpiDefinitionId!.Value,
                        leaf.KpiDefinition!.Code,
                        leaf.KpiDefinition.Unit,
                        leaf.KpiDefinition.Direction,
                        leaf.KpiDefinition.AggregationType,
                        leaf.KpiDefinition.MaxAchievementPercent,
                        targetVal,
                        act?.SumValue ?? 0m,
                        act?.SumDenominator > 0 ? act.SumDenominator : null);

                    var kpiResult = formula.CalculateKpi(kpiInput);
                    var score = kpiResult.ComponentScore ?? 0m;
                    weightedScore += score * (leaf.WeightPercent / totalWeight);
                }

                return Math.Round(weightedScore, 2, MidpointRounding.AwayFromZero);
            }
        }

        // Standard fallback for any other role
        var scoredTargets = targets.Where(t => t.TargetValue > 0).ToList();
        if (scoredTargets.Count == 0)
        {
            return 0m;
        }

        var totalScore = 0m;
        foreach (var t in scoredTargets)
        {
            actualByKpi.TryGetValue(t.KpiDefinitionId, out var act);
            var kpiInput = new KpiScoreInput(
                t.KpiDefinitionId,
                t.KpiDefinition.Code,
                t.KpiDefinition.Unit,
                t.KpiDefinition.Direction,
                t.KpiDefinition.AggregationType,
                t.KpiDefinition.MaxAchievementPercent,
                t.TargetValue,
                act?.SumValue ?? 0m,
                act?.SumDenominator > 0 ? act.SumDenominator : null);

            var result = formula.CalculateKpi(kpiInput);
            totalScore += result.ComponentScore ?? 0m;
        }

        return Math.Round(totalScore / scoredTargets.Count, 2, MidpointRounding.AwayFromZero);
    }

    private static (string PacingStatus, string PacingStatusLabel) ResolvePacing(decimal currentRate, decimal? expectedRateToday)
    {
        if (currentRate >= 100m)
        {
            return ("AHEAD", "Vượt");
        }

        if (expectedRateToday.HasValue && currentRate >= expectedRateToday.Value)
        {
            return ("ON_PACE", "Đạt nhịp");
        }

        return ("BEHIND", "Chậm nhịp");
    }

    private static PeriodComparisonDto BuildComparison(
        EvaluationPeriod current,
        EvaluationPeriod previous,
        decimal currentRate,
        decimal previousRate,
        string role)
    {
        var delta = Math.Round(currentRate - previousRate, 0, MidpointRounding.AwayFromZero);
        var isQuarter = current.PeriodType == PeriodType.Quarter;
        var comparisonType = isQuarter ? "QoQ" : "MoM";

        var direction = delta > 0 ? "UP" : delta < 0 ? "DOWN" : "FLAT";
        var prefix = delta > 0 ? "▲" : delta < 0 ? "▼" : "=";
        var absDelta = Math.Abs(delta);

        var comparedPeriodName = isQuarter
            ? $"Q{(previous.StartDate.Month + 2) / 3}"
            : $"T{previous.StartDate.Month}";

        string text;
        if (role == "SALES")
        {
            text = delta == 0
                ? $"= 0% {comparisonType}"
                : $"{prefix} {absDelta:0}% {comparisonType}";
        }
        else
        {
            text = delta == 0
                ? $"= 0% so với {comparedPeriodName}"
                : $"{prefix} {absDelta:0}% so với {comparedPeriodName}";
        }

        return new PeriodComparisonDto(
            ComparisonType: comparisonType,
            Direction: direction,
            Delta: delta,
            ComparedPeriodName: comparedPeriodName,
            Text: text);
    }

    private sealed record CallerInfo(
        Guid EmployeeId,
        string FullName,
        Guid DepartmentId,
        Guid JobPositionId,
        string JobPositionCode);
}
