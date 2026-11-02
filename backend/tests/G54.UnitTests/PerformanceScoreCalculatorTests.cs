using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Evaluation;
using G54.BLL.Dtos.Permission;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace G54.UnitTests;

/// <summary>
/// The shared score calculation and the three paths that use it: the evaluation form, the form save and the calculation
/// endpoint must show and store the same numbers for the same data. One department with a manager (reviewer) and staff
/// in a team beneath it; the profile has a KPI group (Sales 60 with two KPIs, Service 40 with one KPI) and a
/// qualitative group (no KPI) worth 20.
/// </summary>
public sealed class PerformanceScoreCalculatorTests
{
    private static readonly DateOnly PeriodStart = new(2026, 10, 1);
    private static readonly DateOnly PeriodEnd = new(2026, 10, 31);
    private static readonly DateOnly InPeriod = new(2026, 10, 15);
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);

    // ----- calculator: missing data -----------------------------------------------------------------

    [Fact]
    public async Task Calculate_KpiWithoutTarget_CountsAsZeroInItsGroupButShowsNoValue()
    {
        using var w = await World.CreateAsync();
        w.AddTarget(w.Hdv, 100m);
        w.AddProgress(w.Hdv, 80m);
        w.AddProgress(w.Kh, 5m); // KH has progress but no target

        var result = await w.ScoreAsync(w.Staff1);

        var sales = result.Groups.Single(g => g.Name == "Sales");
        Assert.Equal(40m, sales.Score); // (80 + 0) / 2: KH counts as 0 and keeps its share
        var kh = sales.Kpis.Single(k => k.Code == "KH");
        Assert.Null(kh.TargetValue);
        Assert.Null(kh.ComponentScore);
        Assert.False(kh.HasContribution);
    }

    [Fact]
    public async Task Calculate_KpiWithTargetButNoProgress_HasNoScoreAndNoContribution()
    {
        using var w = await World.CreateAsync();
        w.AddTarget(w.Hdv, 100m);

        var result = await w.ScoreAsync(w.Staff1);

        var hdv = result.Groups.Single(g => g.Name == "Sales").Kpis.Single(k => k.Code == "HDV");
        Assert.Equal(100m, hdv.TargetValue);
        Assert.Null(hdv.ActualValue);
        Assert.Null(hdv.ComponentScore);
        Assert.False(hdv.HasContribution);
    }

    [Fact]
    public async Task Calculate_NothingCanBeScored_ScoresEveryGroupAsZeroNotAsMissing()
    {
        using var w = await World.CreateAsync();
        w.QualitativeScore = null;

        var result = await w.ScoreAsync(w.Staff1);

        // Everything that cannot be scored counts as 0 at every level and keeps its weight (awaiting BA confirmation).
        Assert.Equal(0m, result.Groups.Single(g => g.Name == "Sales").Score);
        Assert.Equal(0m, result.Groups.Single(g => g.Name == "Service").Score);
        Assert.Equal(0m, result.Groups.Single(g => g.IsQualitative).Score);
        Assert.Equal(0m, result.QuantitativeScore);
        Assert.Equal(0m, result.WeightedScore);
        Assert.Equal(0m, result.TotalScore);
    }

    [Fact]
    public async Task Calculate_WithOnlyKpiGroupsAndNoData_HasAZeroTotal()
    {
        using var w = await World.CreateAsync(withQualitativeGroup: false);

        var result = await w.ScoreAsync(w.Staff1);

        Assert.Equal(0m, result.WeightedScore);
        Assert.Equal(0m, result.TotalScore);
    }

    [Fact]
    public async Task Calculate_AMissingKpiKeepsItsWeight_InsteadOfLettingTheOthersTakeItOver()
    {
        // HDV 100 and KH has no data: the group is (100 + 0) / 2 = 50. Without the qualitative group Sales weighs 60 and
        // Service 40, so the weighted score is 50 x 60% + 0 x 40% = 30.
        using var w = await World.CreateAsync(withQualitativeGroup: false);
        w.AddTarget(w.Hdv, 100m);
        w.AddProgress(w.Hdv, 100m);

        var result = await w.ScoreAsync(w.Staff1);

        Assert.Equal(50m, result.Groups.Single(g => g.Name == "Sales").Score);
        Assert.Equal(30m, result.WeightedScore);
    }

    [Fact]
    public async Task Calculate_AProfileWithoutRootGroups_HasNullScores()
    {
        using var w = await World.CreateAsync();
        w.Db.EvaluationProfileItems.RemoveRange(w.Db.EvaluationProfileItems);

        var result = await w.ScoreAsync(w.Staff1);

        Assert.Empty(result.Groups);
        Assert.Null(result.WeightedScore);
        Assert.Null(result.TotalScore);
        Assert.Null(result.QuantitativeScore);
    }

    [Fact]
    public async Task Calculate_AnInactiveKpiIsNotPartOfTheProfile_AndLeavesItsShareToTheOthers()
    {
        using var w = await World.CreateAsync(withQualitativeGroup: false);
        w.Kh.Status = KpiStatus.Inactive;
        w.AddTarget(w.Hdv, 100m);
        w.AddProgress(w.Hdv, 80m);

        var result = await w.ScoreAsync(w.Staff1);

        var sales = result.Groups.Single(g => g.Name == "Sales");
        Assert.Equal(80m, sales.Score); // HDV carries the whole group
        Assert.DoesNotContain(sales.Kpis, k => k.Code == "KH");
    }

    // ----- calculator: targets ------------------------------------------------------------------------

    [Fact]
    public async Task Calculate_UsesTheDepartmentTargetWhenTheEmployeeHasNone()
    {
        using var w = await World.CreateAsync();
        w.AddTarget(w.Hdv, 200m, departmentId: w.Team.Id);
        w.AddProgress(w.Hdv, 100m);

        var result = await w.ScoreAsync(w.Staff1);

        Assert.Equal(50m, result.Groups.Single(g => g.Name == "Sales").Kpis.Single(k => k.Code == "HDV").ComponentScore);
    }

    [Fact]
    public async Task Calculate_EmployeeTargetWinsOverTheDepartmentTarget()
    {
        using var w = await World.CreateAsync();
        w.AddTarget(w.Hdv, 200m, departmentId: w.Team.Id, version: 5);
        w.AddTarget(w.Hdv, 100m, employee: w.Staff1, version: 1);
        w.AddProgress(w.Hdv, 100m);

        var result = await w.ScoreAsync(w.Staff1);

        Assert.Equal(100m, result.Groups.Single(g => g.Name == "Sales").Kpis.Single(k => k.Code == "HDV").ComponentScore);
    }

    [Fact]
    public async Task Calculate_HighestTargetVersionWinsWithinALevel()
    {
        using var w = await World.CreateAsync();
        w.AddTarget(w.Hdv, 100m, employee: w.Staff1, version: 1);
        w.AddTarget(w.Hdv, 50m, employee: w.Staff1, version: 2);
        w.AddProgress(w.Hdv, 50m);

        var kpi = (await w.ScoreAsync(w.Staff1)).Groups.Single(g => g.Name == "Sales").Kpis.Single(k => k.Code == "HDV");

        Assert.Equal(2, kpi.TargetVersion);
        Assert.Equal(100m, kpi.ComponentScore);
    }

    [Fact]
    public async Task Calculate_ProgressOutsideThePeriodOrNotValid_IsIgnored()
    {
        using var w = await World.CreateAsync();
        w.AddTarget(w.Hdv, 100m);
        w.AddProgress(w.Hdv, 100m, date: PeriodStart.AddDays(-1));
        w.AddProgress(w.Hdv, 100m, date: PeriodEnd.AddDays(1));
        w.AddProgress(w.Hdv, 100m, status: ActualProgressStatus.Invalid);
        w.AddProgress(w.Hdv, 30m, date: PeriodEnd);

        var kpi = (await w.ScoreAsync(w.Staff1)).Groups.Single(g => g.Name == "Sales").Kpis.Single(k => k.Code == "HDV");

        Assert.Equal(30m, kpi.ActualValue);
    }

    // ----- calculator: boundaries ---------------------------------------------------------------------

    [Fact]
    public async Task Calculate_ZeroActualAgainstATarget_IsZeroNotNull()
    {
        using var w = await World.CreateAsync();
        w.AddTarget(w.Hdv, 100m);
        w.AddProgress(w.Hdv, 0m);

        var kpi = (await w.ScoreAsync(w.Staff1)).Groups.Single(g => g.Name == "Sales").Kpis.Single(k => k.Code == "HDV");

        Assert.Equal(0m, kpi.ComponentScore);
        Assert.True(kpi.HasContribution);
    }

    [Fact]
    public async Task Calculate_AchievementAboveTheKpiMaximum_IsCapped()
    {
        using var w = await World.CreateAsync();
        w.Hdv.MaxAchievementPercent = 120m;
        await w.Db.SaveChangesAsync();
        w.AddTarget(w.Hdv, 100m);
        w.AddProgress(w.Hdv, 300m);

        var kpi = (await w.ScoreAsync(w.Staff1)).Groups.Single(g => g.Name == "Sales").Kpis.Single(k => k.Code == "HDV");

        Assert.Equal(300m, kpi.AchievementPercentRaw);
        Assert.Equal(120m, kpi.AchievementPercentCapped);
        Assert.Equal(120m, kpi.ComponentScore);
    }

    [Fact]
    public async Task Calculate_NegativeProgress_NeverGivesANegativeAchievement()
    {
        using var w = await World.CreateAsync();
        w.AddTarget(w.Hdv, 100m);
        w.AddProgress(w.Hdv, -50m);

        var kpi = (await w.ScoreAsync(w.Staff1)).Groups.Single(g => g.Name == "Sales").Kpis.Single(k => k.Code == "HDV");

        Assert.Equal(0m, kpi.AchievementPercentRaw);
    }

    [Fact]
    public async Task Calculate_ScoresAreRoundedToFourDecimals()
    {
        using var w = await World.CreateAsync();
        w.AddTarget(w.Hdv, 3m);
        w.AddProgress(w.Hdv, 1m); // 33.3333...

        var kpi = (await w.ScoreAsync(w.Staff1)).Groups.Single(g => g.Name == "Sales").Kpis.Single(k => k.Code == "HDV");

        Assert.Equal(33.3333m, kpi.ComponentScore);
    }

    // ----- calculator: total ----------------------------------------------------------------------------

    [Fact]
    public async Task Calculate_Total_IsTheWeightedGroupsMinusRiskPlusAppealAdjustments()
    {
        using var w = await World.CreateAsync();
        w.AddTarget(w.Hdv, 100m);
        w.AddProgress(w.Hdv, 100m);
        w.AddTarget(w.Csat, 100m);
        w.AddProgress(w.Csat, 100m);
        w.QualitativeScore = 50m;
        w.AddRisk(RiskEventSeverity.Medium);
        w.AddLedgerEntry(w.Staff1, 3m);

        var result = await w.ScoreAsync(w.Staff1);

        // Sales: HDV 100 and KH has no data (0), so (100 + 0) / 2 = 50 -> 50 x 40% = 20;
        // Service 100 x 40% = 40; qualitative 50 x 20% = 10; weighted score 70.
        Assert.Equal(70m, result.WeightedScore);
        Assert.Equal(EvaluationService.MapRiskPenalty(RiskEventSeverity.Medium), result.RiskPenalty);
        Assert.Equal(70m - result.RiskPenalty + 3m, result.TotalScore);
    }

    [Fact]
    public async Task Calculate_RiskPenaltyNeverTakesTheScoreBelowZero()
    {
        using var w = await World.CreateAsync();
        w.AddTarget(w.Hdv, 100m);
        w.AddProgress(w.Hdv, 1m);
        for (var i = 0; i < 6; i++)
        {
            w.AddRisk(RiskEventSeverity.Critical);
        }

        var result = await w.ScoreAsync(w.Staff1);

        Assert.Equal(0m, result.TotalScore);
    }

    [Fact]
    public async Task Calculate_DismissedAndOutOfPeriodRiskEvents_AreNotCounted()
    {
        using var w = await World.CreateAsync();
        w.AddTarget(w.Hdv, 100m);
        w.AddProgress(w.Hdv, 100m);
        w.AddRisk(RiskEventSeverity.Critical, RiskEventStatus.Dismissed);
        w.AddRisk(RiskEventSeverity.Critical, occurredAt: new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero));

        var result = await w.ScoreAsync(w.Staff1);

        Assert.Empty(result.RiskEvents);
        Assert.Equal(0m, result.RiskPenalty);
    }

    [Fact]
    public async Task Calculate_AQualitativeGroupTakesTheReviewersScoreAndNotesAndCountsAsZeroUntilScored()
    {
        using var w = await World.CreateAsync();
        w.QualitativeScore = null;
        var unscored = await w.ScoreAsync(w.Staff1);
        Assert.Equal(0m, unscored.Groups.Single(g => g.IsQualitative).Score);
        Assert.Null(unscored.Groups.Single(g => g.IsQualitative).QualitativeScore);

        w.QualitativeScore = 75m;
        var scored = await w.ScoreAsync(w.Staff1);

        var group = scored.Groups.Single(g => g.IsQualitative);
        Assert.Equal(75m, group.Score);
        Assert.Equal(75m, group.QualitativeScore);
        Assert.Equal("notes", group.QualitativeNotes);
    }

    [Fact]
    public async Task Calculate_QuantitativeScore_LeavesOutTheQualitativeGroup()
    {
        using var w = await World.CreateAsync();
        w.AddTarget(w.Hdv, 100m);
        w.AddProgress(w.Hdv, 100m);
        w.QualitativeScore = 0m;

        var result = await w.ScoreAsync(w.Staff1);

        // Sales is (100 + 0) / 2 = 50 (KH has no data) and Service is 0 (CSAT has none); both KPI groups weigh 40, so the
        // quantitative score is 25. The qualitative group (score 0, weight 20) does not dilute it.
        Assert.Equal(25m, result.QuantitativeScore);
    }

    // ----- calculator: several employees at once ---------------------------------------------------------

    [Fact]
    public async Task Calculate_ManyEmployees_ScoresEachOneIndependently()
    {
        using var w = await World.CreateAsync();
        w.AddTarget(w.Hdv, 100m, employee: w.Staff1);
        w.AddTarget(w.Hdv, 100m, employee: w.Staff2);
        w.AddProgress(w.Hdv, 100m, owner: w.Staff1);
        w.AddProgress(w.Hdv, 40m, owner: w.Staff2);
        w.AddRisk(RiskEventSeverity.High, owner: w.Staff2);
        w.AddLedgerEntry(w.Staff1, 2m);

        var scored = await w.ScoreManyAsync(w.Staff1, w.Staff2, w.Staff3);

        Assert.Equal(3, scored.Count);
        // KH has no data for anyone, so it counts as 0 and every Sales score is half of the HDV score.
        Assert.Equal(50m, scored[w.Staff1.Id].Groups.Single(g => g.Name == "Sales").Score);
        Assert.Equal(20m, scored[w.Staff2.Id].Groups.Single(g => g.Name == "Sales").Score);
        Assert.Equal(0m, scored[w.Staff3.Id].Groups.Single(g => g.Name == "Sales").Score);
        Assert.Empty(scored[w.Staff1.Id].RiskEvents);
        Assert.Single(scored[w.Staff2.Id].RiskEvents);
        Assert.Equal(2m, scored[w.Staff1.Id].TotalScore - scored[w.Staff1.Id].WeightedScore);
    }

    [Fact]
    public async Task Calculate_NoSubjects_GivesAnEmptyResult()
    {
        using var w = await World.CreateAsync();

        var scored = await w.Calculator.CalculateAsync([], w.Period);

        Assert.Empty(scored);
    }

    // ----- the three paths agree ------------------------------------------------------------------------------

    [Fact]
    public async Task FormSaveAndCalculate_GiveTheSameScoreAndTheSameKpiRowsForTheSameData()
    {
        using var w = await World.CreateAsync();
        w.AddTarget(w.Hdv, 100m);
        w.AddProgress(w.Hdv, 60m);
        w.AddProgress(w.Kh, 5m); // no target: left out on every path
        w.AddTarget(w.Csat, 50m);
        w.AddProgress(w.Csat, 40m);
        w.AddRisk(RiskEventSeverity.Low);
        w.AddLedgerEntry(w.Staff1, 1.5m);
        await w.Db.SaveChangesAsync();

        var saved = await w.SubmitAsync(w.Staff1, isDraft: false);
        var form = await w.FormAsync(w.Staff1); // after the save: the form reads the qualitative score the save stored
        var evaluationId = saved.EvaluationId;
        var calculated = await w.CalculateEndpointAsync(evaluationId);

        Assert.Equal(form.Summary.TotalScore, saved.TotalScore);
        Assert.Equal(saved.TotalScore, calculated.TotalScore);
        // Every group of the form adds up to the weighted score the calculation endpoint reports.
        var groupScores = form.CriteriaGroups.ToDictionary(g => g.EvaluationProfileItemId, g => g.GroupScore);
        foreach (var group in calculated.Groups)
        {
            Assert.Equal(groupScores[group.EvaluationProfileItemId], group.ComponentScore);
        }

        var rows = await w.Db.PerformanceContributions.AsNoTracking()
            .Where(c => c.PerformanceEvaluationId == evaluationId && c.RiskEventId == null)
            .ToListAsync();
        Assert.Equal(2, rows.Count); // HDV and CSAT have both a target and progress; KH does not
        Assert.Equal(calculated.Groups.SelectMany(g => g.Kpis).Count(), rows.Count);
    }

    [Fact]
    public async Task List_QuantitativeScore_MatchesTheScoreOfTheForm()
    {
        using var w = await World.CreateAsync();
        w.AddTarget(w.Hdv, 100m);
        w.AddProgress(w.Hdv, 70m);
        w.AddTarget(w.Csat, 100m);
        w.AddProgress(w.Csat, 90m);
        await w.Db.SaveChangesAsync();

        var form = await w.FormAsync(w.Staff1);
        var row = (await w.Evaluation.GetDepartmentEmployeesForEvaluationAsync(w.Period.Id, w.Team.Id, w.Manager.Id))
            .Single(e => e.EmployeeId == w.Staff1.Id);

        var kpiGroups = form.CriteriaGroups.Where(g => !g.IsQualitative).ToList();
        var expected = kpiGroups.Sum(g => g.GroupScore!.Value * g.WeightPercent) / kpiGroups.Sum(g => g.WeightPercent);
        Assert.Equal(Math.Round(expected, 4), row.QuantitativeScore);
    }

    // ----- the form ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Form_KpiWithoutProgress_ShowsNotApplicableButCountsAsZeroInTheGroup()
    {
        using var w = await World.CreateAsync();
        w.AddTarget(w.Hdv, 100m);
        await w.Db.SaveChangesAsync();

        var form = await w.FormAsync(w.Staff1);

        var sales = form.CriteriaGroups.Single(g => g.GroupName == "Sales");
        var hdv = sales.KpiItems.Single(k => k.KpiCode == "HDV");
        Assert.Equal(100m, hdv.TargetValue);
        Assert.Null(hdv.ActualValue);
        Assert.Null(hdv.AchievementPercent);
        Assert.Null(hdv.ComponentScore);
        Assert.Equal(0m, sales.GroupScore); // shown as N/A on the KPI row, but 0 in the group score
    }

    [Fact]
    public async Task Form_WithNothingToScore_HasAZeroTotalAndALowestTier()
    {
        using var w = await World.CreateAsync(withQualitativeGroup: false);

        var form = await w.FormAsync(w.Staff1);

        Assert.Equal(0m, form.Summary.TotalScore);
        Assert.Equal(0m, form.Summary.WeightedScore);
        Assert.NotNull(form.Summary.ProposedTier);
        Assert.Null(form.PreviousPeriod);
    }

    [Fact]
    public async Task Form_OfAProfileWithoutRootGroups_HasANullTotalAndNoProposedTier()
    {
        using var w = await World.CreateAsync();
        w.Db.EvaluationProfileItems.RemoveRange(w.Db.EvaluationProfileItems);
        await w.Db.SaveChangesAsync();

        var form = await w.FormAsync(w.Staff1);

        Assert.Null(form.Summary.TotalScore);
        Assert.Null(form.Summary.WeightedScore);
        Assert.Null(form.Summary.ProposedTier);
    }

    [Fact]
    public async Task Form_OfAnEvaluationSentForReview_KeepsTheStoredScoreInsteadOfRecalculating()
    {
        using var w = await World.CreateAsync();
        w.AddTarget(w.Hdv, 100m);
        w.AddProgress(w.Hdv, 100m);
        await w.Db.SaveChangesAsync();
        var evaluation = w.AddEvaluation(w.Staff1, 12.5m, PerformanceEvaluationStatus.InReview);
        await w.Db.SaveChangesAsync();

        var form = await w.FormAsync(w.Staff1);

        Assert.Equal(12.5m, form.Summary.TotalScore);
        Assert.Equal(evaluation.Id, form.CurrentEvaluation.Id);
    }

    [Fact]
    public async Task Form_OfACalculatedEvaluation_ShowsTheScoreJustCalculated()
    {
        using var w = await World.CreateAsync();
        w.AddTarget(w.Hdv, 100m);
        w.AddProgress(w.Hdv, 100m);
        w.AddEvaluation(w.Staff1, 12.5m, PerformanceEvaluationStatus.Calculated);
        await w.Db.SaveChangesAsync();

        var form = await w.FormAsync(w.Staff1);

        Assert.NotEqual(12.5m, form.Summary.TotalScore);
        var group = form.CriteriaGroups.Single(g => g.GroupName == "Sales");
        Assert.Equal(50m, group.GroupScore); // HDV 100 and KH without data (0)
    }

    // ----- saving the form ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Submit_Completed_StoresTheScoreTheKpiRowsAndTheCalculatedStatusInOneSave()
    {
        using var w = await World.CreateAsync();
        w.AddTarget(w.Hdv, 100m);
        w.AddProgress(w.Hdv, 100m);
        await w.Db.SaveChangesAsync();

        var result = await w.SubmitAsync(w.Staff1, isDraft: false);

        Assert.Equal("CALCULATED", result.Status);
        Assert.NotNull(result.TotalScore);
        Assert.NotNull(result.CalculatedAt);
        var stored = await w.Db.PerformanceEvaluations.AsNoTracking().SingleAsync(e => e.Id == result.EvaluationId);
        Assert.Equal(result.TotalScore, stored.TotalScore);
        Assert.Equal(PerformanceEvaluationStatus.Calculated, stored.Status);
        Assert.Equal(1, await w.Db.PerformanceContributions.CountAsync(c => c.PerformanceEvaluationId == stored.Id));
    }

    [Fact]
    public async Task Submit_Draft_StoresTheKpiRowsAndTheScoreButKeepsTheDraftStatus()
    {
        using var w = await World.CreateAsync();
        w.AddTarget(w.Hdv, 100m);
        w.AddProgress(w.Hdv, 100m);
        await w.Db.SaveChangesAsync();

        var result = await w.SubmitAsync(w.Staff1, isDraft: true);

        Assert.Equal("DRAFT", result.Status);
        Assert.NotNull(result.TotalScore);
        Assert.Equal(1, await w.Db.PerformanceContributions.CountAsync(c => c.PerformanceEvaluationId == result.EvaluationId));
    }

    [Fact]
    public async Task Submit_Draft_WithNothingToScore_StoresAZeroTotalAndNoKpiRows()
    {
        using var w = await World.CreateAsync(withQualitativeGroup: false);

        var result = await w.SubmitAsync(w.Staff1, isDraft: true);

        Assert.Equal(0m, result.TotalScore);
        Assert.NotNull(result.ProposedTier);
        Assert.Equal("DRAFT", result.Status);
        Assert.Equal(0, await w.Db.PerformanceContributions.CountAsync()); // no KPI has both a target and progress
    }

    [Fact]
    public async Task Submit_Completed_WithNothingToScore_StoresAZeroTotal()
    {
        using var w = await World.CreateAsync(withQualitativeGroup: false);

        var result = await w.SubmitAsync(w.Staff1, isDraft: false);

        Assert.Equal(0m, result.TotalScore);
        Assert.Equal("CALCULATED", result.Status);
        Assert.Equal(0, await w.Db.PerformanceContributions.CountAsync());
    }

    [Fact]
    public async Task Submit_Draft_KeepsAnAlreadyCalculatedEvaluationCalculated()
    {
        using var w = await World.CreateAsync();
        w.AddTarget(w.Hdv, 100m);
        w.AddProgress(w.Hdv, 100m);
        await w.Db.SaveChangesAsync();
        var completed = await w.SubmitAsync(w.Staff1, isDraft: false);
        Assert.Equal("CALCULATED", completed.Status);

        var again = await w.SubmitAsync(w.Staff1, isDraft: true);

        Assert.Equal("CALCULATED", again.Status);
        Assert.NotNull(again.TotalScore);
        var stored = await w.Db.PerformanceEvaluations.AsNoTracking().SingleAsync(e => e.Id == again.EvaluationId);
        Assert.Equal(PerformanceEvaluationStatus.Calculated, stored.Status);
        Assert.NotNull(stored.CalculatedAt);
    }

    [Fact]
    public async Task Submit_Draft_OfADraftEvaluation_StaysDraft()
    {
        using var w = await World.CreateAsync();
        w.AddTarget(w.Hdv, 100m);
        w.AddProgress(w.Hdv, 100m);
        await w.Db.SaveChangesAsync();
        await w.SubmitAsync(w.Staff1, isDraft: true);

        var again = await w.SubmitAsync(w.Staff1, isDraft: true);

        Assert.Equal("DRAFT", again.Status);
    }

    [Fact]
    public async Task Submit_Completed_OfAProfileWithoutRootGroups_IsRefusedAndChangesNothing()
    {
        using var w = await World.CreateAsync();
        w.Db.EvaluationProfileItems.RemoveRange(w.Db.EvaluationProfileItems);
        await w.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<G54.BLL.Common.Exceptions.ValidationException>(
            () => w.SubmitAsync(w.Staff1, isDraft: false));

        Assert.Equal(0, await w.Db.PerformanceEvaluations.CountAsync());
        Assert.Equal(0, await w.Db.PerformanceContributions.CountAsync());
    }

    [Fact]
    public async Task Submit_Again_UpdatesTheSameKpiRowsInsteadOfAddingMore()
    {
        using var w = await World.CreateAsync();
        w.AddTarget(w.Hdv, 100m);
        w.AddProgress(w.Hdv, 50m);
        await w.Db.SaveChangesAsync();
        var first = await w.SubmitAsync(w.Staff1, isDraft: true);
        var firstRow = await w.Db.PerformanceContributions.AsNoTracking().SingleAsync();

        w.AddProgress(w.Hdv, 50m, date: InPeriod.AddDays(1));
        await w.Db.SaveChangesAsync();
        var second = await w.SubmitAsync(w.Staff1, isDraft: false);

        Assert.Equal(first.EvaluationId, second.EvaluationId);
        var rows = await w.Db.PerformanceContributions.AsNoTracking().ToListAsync();
        Assert.Single(rows);
        Assert.Equal(firstRow.Id, rows[0].Id);
        Assert.Equal(100m, rows[0].ActualValue);
    }

    [Fact]
    public async Task Submit_AfterAKpiLosesItsProgress_RemovesItsStaleKpiRow()
    {
        using var w = await World.CreateAsync();
        w.AddTarget(w.Hdv, 100m);
        var progress = w.AddProgress(w.Hdv, 50m);
        await w.Db.SaveChangesAsync();
        await w.SubmitAsync(w.Staff1, isDraft: true);
        Assert.Equal(1, await w.Db.PerformanceContributions.CountAsync());

        progress.Status = ActualProgressStatus.Invalid;
        await w.Db.SaveChangesAsync();
        await w.SubmitAsync(w.Staff1, isDraft: true);

        Assert.Equal(0, await w.Db.PerformanceContributions.CountAsync());
    }

    [Fact]
    public async Task Submit_LeavesTheRowsOfRiskEventsAlone()
    {
        using var w = await World.CreateAsync();
        w.AddTarget(w.Hdv, 100m);
        w.AddProgress(w.Hdv, 50m);
        var risk = w.AddRisk(RiskEventSeverity.High);
        await w.Db.SaveChangesAsync();
        var evaluation = w.AddEvaluation(w.Staff1, null, PerformanceEvaluationStatus.Draft);
        w.Db.PerformanceContributions.Add(new PerformanceContribution
        {
            Id = Guid.NewGuid(),
            PerformanceEvaluationId = evaluation.Id,
            EmployeeId = w.Staff1.Id,
            EvaluationPeriodId = w.Period.Id,
            RiskEventId = risk.Id,
            ComponentScore = -10m,
            UpdatedAt = Now,
        });
        await w.Db.SaveChangesAsync();

        await w.SubmitAsync(w.Staff1, isDraft: true);

        Assert.Equal(1, await w.Db.PerformanceContributions.CountAsync(c => c.RiskEventId == risk.Id));
    }

    [Fact]
    public async Task Submit_KeepsTheCalibrationMarkerOfTheStoredComment()
    {
        using var w = await World.CreateAsync();
        w.AddTarget(w.Hdv, 100m);
        w.AddProgress(w.Hdv, 100m);
        var evaluation = w.AddEvaluation(w.Staff1, null, PerformanceEvaluationStatus.Draft);
        evaluation.Comment = "old\n\n<!-- CALIBRATION_JSON: {\"calibratedTier\":\"A1\",\"reason\":null,\"coachingFlag\":false} -->";
        await w.Db.SaveChangesAsync();

        await w.SubmitAsync(w.Staff1, isDraft: true);

        var comment = (await w.Db.PerformanceEvaluations.AsNoTracking().SingleAsync()).Comment;
        Assert.Contains("CALIBRATION_JSON", comment);
        Assert.Contains("A1", comment);
    }

    // ----- saving the form: input a standard client never sends -----------------------------------------------------

    [Fact]
    public async Task Submit_ForAnEmployeeOutsideTheReviewersScope_IsForbiddenAndChangesNothing()
    {
        using var w = await World.CreateAsync(submitScope: PermissionScopes.Dept);

        await Assert.ThrowsAsync<ForbiddenException>(() => w.SubmitAsync(w.OutsideStaff, isDraft: true));

        Assert.Equal(0, await w.Db.PerformanceEvaluations.CountAsync());
        Assert.Equal(0, await w.Db.PerformanceContributions.CountAsync());
    }

    [Fact]
    public async Task Submit_ForTheReviewerThemselves_IsRefusedAndChangesNothing()
    {
        using var w = await World.CreateAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() => w.SubmitAsync(w.Manager, isDraft: true));

        Assert.Equal(0, await w.Db.PerformanceEvaluations.CountAsync());
    }

    [Theory]
    [InlineData(PerformanceEvaluationStatus.InReview)]
    [InlineData(PerformanceEvaluationStatus.Approved)]
    [InlineData(PerformanceEvaluationStatus.Final)]
    public async Task Submit_OverAnEvaluationAlreadySentForReview_IsRefusedAndKeepsItsScoreAndRows(PerformanceEvaluationStatus status)
    {
        using var w = await World.CreateAsync();
        w.AddTarget(w.Hdv, 100m);
        w.AddProgress(w.Hdv, 100m);
        w.AddEvaluation(w.Staff1, 33m, status);
        await w.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() => w.SubmitAsync(w.Staff1, isDraft: false));

        var stored = await w.Db.PerformanceEvaluations.AsNoTracking().SingleAsync();
        Assert.Equal(33m, stored.TotalScore);
        Assert.Equal(status, stored.Status);
        Assert.Equal(0, await w.Db.PerformanceContributions.CountAsync());
    }

    [Fact]
    public async Task Submit_InALockedPeriod_IsRefusedAndChangesNothing()
    {
        using var w = await World.CreateAsync();
        w.Period.Status = PeriodStatus.Locked;
        await w.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() => w.SubmitAsync(w.Staff1, isDraft: true));

        Assert.Equal(0, await w.Db.PerformanceEvaluations.CountAsync());
    }

    [Fact]
    public async Task Submit_WithAQualitativeScoreForAGroupThatHasKpis_IsRefusedAndChangesNothing()
    {
        using var w = await World.CreateAsync();

        var request = new SubmitEvaluationRequestDto(
            w.Staff1.Id,
            w.Period.Id,
            QualitativeScores: [new QualitativeScoreInputDto(w.SalesRoot.Id, 90m)],
            IsDraft: true);

        await Assert.ThrowsAsync<G54.BLL.Common.Exceptions.ValidationException>(() => w.SubmitAsync(request));

        Assert.Equal(0, await w.Db.PerformanceEvaluations.CountAsync());
    }

    [Fact]
    public async Task Submit_Completing_WithoutTheQualitativeScore_IsRefusedAndChangesNothing()
    {
        using var w = await World.CreateAsync();
        w.AddTarget(w.Hdv, 100m);
        w.AddProgress(w.Hdv, 100m);
        await w.Db.SaveChangesAsync();
        w.QualitativeScore = null;

        await Assert.ThrowsAsync<G54.BLL.Common.Exceptions.ValidationException>(() => w.SubmitAsync(w.Staff1, isDraft: false));

        Assert.Equal(0, await w.Db.PerformanceEvaluations.CountAsync());
        Assert.Equal(0, await w.Db.PerformanceContributions.CountAsync());
    }

    // ----- the calculation endpoint ------------------------------------------------------------------------------------

    [Fact]
    public async Task CalculateEndpoint_UsesTheQualitativeScoreSavedWithTheForm()
    {
        using var w = await World.CreateAsync();
        w.AddTarget(w.Hdv, 100m);
        w.AddProgress(w.Hdv, 100m);
        await w.Db.SaveChangesAsync();
        w.QualitativeScore = 80m;
        var saved = await w.SubmitAsync(w.Staff1, isDraft: true);

        var calculated = await w.CalculateEndpointAsync(saved.EvaluationId);

        Assert.Equal(saved.TotalScore, calculated.TotalScore);
        Assert.Equal(80m, calculated.Groups.Single(g => g.GroupName == "Quality").ComponentScore);
    }

    [Fact]
    public async Task CalculateEndpoint_SubtractsTheRiskPenaltyAndAddsTheAppealAdjustmentsLikeTheForm()
    {
        using var w = await World.CreateAsync();
        w.AddTarget(w.Hdv, 100m);
        w.AddProgress(w.Hdv, 100m);
        w.AddRisk(RiskEventSeverity.Medium);
        var evaluation = w.AddEvaluation(w.Staff1, null, PerformanceEvaluationStatus.Draft);
        w.AddLedgerEntry(w.Staff1, 4m, evaluation);
        await w.Db.SaveChangesAsync();
        var form = await w.FormAsync(w.Staff1);

        var calculated = await w.CalculateEndpointAsync(evaluation.Id);

        Assert.Equal(form.Summary.TotalScore, calculated.TotalScore);
    }

    // ----- calibration ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Calibration_AnEmployeeWithoutAProfile_HasNoScoreRankOrTier()
    {
        using var w = await World.CreateAsync(withQualitativeGroup: false);
        w.AddTarget(w.Hdv, 100m, employee: w.Staff1);
        w.AddProgress(w.Hdv, 100m, owner: w.Staff1);
        await w.Db.SaveChangesAsync();
        var noProfilePosition = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = "NOPROFILE",
            Title = "No profile",
            Status = RecordStatus.Active,
            LastSyncAt = Now,
            UpdatedAt = Now,
        };
        w.Db.JobPositions.Add(noProfilePosition);
        w.Staff2.JobPositionId = noProfilePosition.Id;
        await w.Db.SaveChangesAsync();

        var overview = await w.Evaluation.GetDepartmentCalibrationAsync(w.Period.Id, w.Team.Id, null, w.Manager.Id);

        var scored = overview.RankedRoster.Single(r => r.EmployeeId == w.Staff1.Id);
        var unscored = overview.RankedRoster.Single(r => r.EmployeeId == w.Staff2.Id);
        Assert.NotNull(scored.TotalScore);
        Assert.Equal(1, scored.Rank);
        Assert.NotNull(scored.ProposedTier);
        Assert.Null(unscored.TotalScore);
        Assert.Null(unscored.Rank);
        Assert.Null(unscored.ProposedTier);
        Assert.Null(unscored.CalibratedTier);
        Assert.False(unscored.IsAdjusted);
    }

    [Fact]
    public async Task Calibration_EmployeesWithoutAProfile_AreListedAfterTheRankedOnesAndLeaveTheQuotaAndCurveAlone()
    {
        using var w = await World.CreateAsync(withQualitativeGroup: false);
        w.AddTarget(w.Hdv, 100m, employee: w.Staff2);
        w.AddProgress(w.Hdv, 100m, owner: w.Staff2);
        await w.Db.SaveChangesAsync();
        var noProfilePosition = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = "NOPROFILE",
            Title = "No profile",
            Status = RecordStatus.Active,
            LastSyncAt = Now,
            UpdatedAt = Now,
        };
        w.Db.JobPositions.Add(noProfilePosition);
        w.Staff1.JobPositionId = noProfilePosition.Id;
        w.Staff3.JobPositionId = noProfilePosition.Id;
        await w.Db.SaveChangesAsync();

        var overview = await w.Evaluation.GetDepartmentCalibrationAsync(w.Period.Id, w.Team.Id, null, w.Manager.Id);

        Assert.Equal(w.Staff2.Id, overview.RankedRoster[0].EmployeeId);
        Assert.All(overview.RankedRoster.Skip(1), r => Assert.Null(r.Rank));
        Assert.Equal(1, overview.Quota.TotalEmployees);
        Assert.Equal(overview.RankedRoster[0].TotalScore, overview.BellCurve.MeanScore);
    }

    [Fact]
    public async Task Calibration_QualitativeScore_IsNullWhenTheProfileHasNoQualitativeGroup()
    {
        using var w = await World.CreateAsync(withQualitativeGroup: false);
        w.AddTarget(w.Hdv, 100m, employee: w.Staff1);
        w.AddProgress(w.Hdv, 100m, owner: w.Staff1);
        await w.Db.SaveChangesAsync();

        var overview = await w.Evaluation.GetDepartmentCalibrationAsync(w.Period.Id, w.Team.Id, null, w.Manager.Id);

        Assert.All(overview.RankedRoster, r => Assert.Null(r.QualitativeScore));
    }

    [Fact]
    public async Task SubmitCalibration_ForAnEmployeeWithoutAnEvaluation_StoresTheCalculatedScoreAndItsKpiRows()
    {
        using var w = await World.CreateAsync(withQualitativeGroup: false);
        w.AddTarget(w.Hdv, 100m, employee: w.Staff1);
        w.AddProgress(w.Hdv, 100m, owner: w.Staff1);
        await w.Db.SaveChangesAsync();
        var expected = (await w.ScoreAsync(w.Staff1)).TotalScore;

        await w.Evaluation.SubmitDepartmentCalibrationAsync(
            new SubmitCalibrationRequestDto(
                w.Team.Id, w.Period.Id, [new CalibrationAdjustmentItemDto(w.Staff1.Id, "B", null, false)], IsDraft: true),
            w.Manager.Id);

        var stored = await w.Db.PerformanceEvaluations.AsNoTracking().SingleAsync(e => e.EmployeeId == w.Staff1.Id);
        Assert.Equal(expected, stored.TotalScore); // the calculated score, not a fixed default
        Assert.NotEqual(80m, stored.TotalScore);
        Assert.Equal(PerformanceEvaluationStatus.Calculated, stored.Status);
        Assert.Equal(1, await w.Db.PerformanceContributions.CountAsync(c => c.PerformanceEvaluationId == stored.Id));
    }

    [Fact]
    public async Task SubmitCalibration_ForAnEmployeeWithoutAnEvaluationAndWithoutAProfile_IsRefusedAndChangesNothing()
    {
        using var w = await World.CreateAsync(withQualitativeGroup: false);
        var noProfilePosition = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = "NOPROFILE",
            Title = "No profile",
            Status = RecordStatus.Active,
            LastSyncAt = Now,
            UpdatedAt = Now,
        };
        w.Db.JobPositions.Add(noProfilePosition);
        w.Staff1.JobPositionId = noProfilePosition.Id;
        await w.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<G54.BLL.Common.Exceptions.ValidationException>(() =>
            w.Evaluation.SubmitDepartmentCalibrationAsync(
                new SubmitCalibrationRequestDto(
                    w.Team.Id, w.Period.Id, [new CalibrationAdjustmentItemDto(w.Staff1.Id, "B", null, false)], IsDraft: true),
                w.Manager.Id));

        Assert.Contains("S1", ex.Message);
        Assert.Equal(0, await w.Db.PerformanceEvaluations.CountAsync());
    }

    [Fact]
    public async Task Calibration_ADraftEmployee_IsScoredByTheSharedCalculation()
    {
        using var w = await World.CreateAsync();
        w.AddTarget(w.Hdv, 100m, employee: w.Staff1);
        w.AddProgress(w.Hdv, 100m, owner: w.Staff1);
        w.AddEvaluation(w.Staff1, null, PerformanceEvaluationStatus.Draft);
        await w.Db.SaveChangesAsync();
        var form = await w.FormAsync(w.Staff1);

        var overview = await w.Evaluation.GetDepartmentCalibrationAsync(w.Period.Id, w.Team.Id, null, w.Manager.Id);

        Assert.Equal(form.Summary.TotalScore, overview.RankedRoster.Single(r => r.EmployeeId == w.Staff1.Id).TotalScore);
    }

    [Fact]
    public async Task Calibration_AnEmployeeWithAStoredScoreThatWasSentForReview_KeepsThatScore()
    {
        using var w = await World.CreateAsync();
        w.AddTarget(w.Hdv, 100m, employee: w.Staff1);
        w.AddProgress(w.Hdv, 100m, owner: w.Staff1);
        w.AddEvaluation(w.Staff1, 61m, PerformanceEvaluationStatus.InReview);
        await w.Db.SaveChangesAsync();

        var overview = await w.Evaluation.GetDepartmentCalibrationAsync(w.Period.Id, w.Team.Id, null, w.Manager.Id);

        Assert.Equal(61m, overview.RankedRoster.Single(r => r.EmployeeId == w.Staff1.Id).TotalScore);
    }

    // ----- fixture ---------------------------------------------------------------------------------------------------------

    private sealed class World : IDisposable
    {
        private int _riskSequence;

        public required AppDbContext Db { get; init; }
        public required Department Team { get; init; }
        public required EvaluationPeriod Period { get; init; }
        public required EvaluationProfile Profile { get; init; }
        public required EvaluationProfileItem SalesRoot { get; init; }
        public required KpiDefinition Hdv { get; init; }
        public required KpiDefinition Kh { get; init; }
        public required KpiDefinition Csat { get; init; }
        public required Employee Manager { get; init; }
        public required Employee Staff1 { get; init; }
        public required Employee Staff2 { get; init; }
        public required Employee Staff3 { get; init; }
        public required Employee OutsideStaff { get; init; }
        public required PerformanceScoreCalculator Calculator { get; init; }
        public required EvaluationService Evaluation { get; init; }
        public required PerformanceEvaluationService Endpoint { get; init; }
        public required EvaluationProfileItem QualitativeRoot { get; init; }

        /// <summary>The reviewer's qualitative score for the qualitative group, sent with every form save; null sends none.</summary>
        public decimal? QualitativeScore { get; set; } = 70m;

        public static async Task<World> CreateAsync(bool withQualitativeGroup = true, string submitScope = PermissionScopes.Dept)
        {
            var qualitativeScore = withQualitativeGroup ? (decimal?)70m : null;
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            DowngradeTestData.Seed(db);

            Department Dept(string code, DepartmentLevelType level, Guid? parent) => new()
            {
                Id = Guid.NewGuid(),
                Code = code,
                Name = code,
                LevelType = level,
                ParentDepartmentId = parent,
                Status = RecordStatus.Active,
                LastSyncAt = Now,
                UpdatedAt = Now,
            };

            var branch = Dept("BR", DepartmentLevelType.Branch, null);
            var sales = Dept("SALES", DepartmentLevelType.Department, branch.Id);
            var other = Dept("OTHER", DepartmentLevelType.Department, branch.Id);
            var team = Dept("T1", DepartmentLevelType.Team, sales.Id);
            db.Departments.AddRange(branch, sales, other, team);

            var position = new JobPosition { Id = Guid.NewGuid(), Code = "POS", Title = "Position", Status = RecordStatus.Active, LastSyncAt = Now, UpdatedAt = Now };
            db.JobPositions.Add(position);

            var group = new KpiGroup { Id = Guid.NewGuid(), Code = "KD", Name = "Business", Status = KpiStatus.Active, UpdatedAt = Now };
            db.KpiGroups.Add(group);
            KpiDefinition Kpi(string code) => new()
            {
                Id = Guid.NewGuid(),
                KpiGroupId = group.Id,
                Code = code,
                Name = code,
                Unit = "unit",
                AggregationType = KpiAggregationType.Sum,
                Direction = KpiDirection.HigherBetter,
                Status = KpiStatus.Active,
                UpdatedAt = Now,
            };
            var hdv = Kpi("HDV");
            var kh = Kpi("KH");
            var csat = Kpi("CSAT");
            db.KpiDefinitions.AddRange(hdv, kh, csat);

            var profile = new EvaluationProfile
            {
                Id = Guid.NewGuid(),
                JobPositionId = position.Id,
                Code = "EP",
                Name = "Profile",
                Version = 1,
                EffectiveFrom = new DateOnly(2026, 1, 1),
                Status = EvaluationProfileStatus.Active,
                UpdatedAt = Now,
            };
            db.EvaluationProfiles.Add(profile);

            EvaluationProfileItem Node(Guid? parent, string? name, KpiDefinition? kpi, decimal weight, int order) => new()
            {
                Id = Guid.NewGuid(),
                EvaluationProfileId = profile.Id,
                ParentItemId = parent,
                Name = name,
                KpiDefinitionId = kpi?.Id,
                WeightPercent = weight,
                DisplayOrder = order,
                UpdatedAt = Now,
            };

            // Sales (60 of the 80 KPI weight) holds HDV and KH; Service (40) holds CSAT; Quality (20) has no KPI.
            var salesRoot = Node(null, "Sales", null, withQualitativeGroup ? 40m : 60m, 1);
            var serviceRoot = Node(null, "Service", null, withQualitativeGroup ? 40m : 40m, 2);
            var qualityRoot = Node(null, "Quality", null, 20m, 3);
            var items = new List<EvaluationProfileItem>
            {
                salesRoot,
                serviceRoot,
                Node(salesRoot.Id, null, hdv, 50m, 1),
                Node(salesRoot.Id, null, kh, 50m, 2),
                Node(serviceRoot.Id, null, csat, 100m, 1),
            };
            if (withQualitativeGroup)
            {
                items.Add(qualityRoot);
            }

            db.EvaluationProfileItems.AddRange(items);

            var period = new EvaluationPeriod
            {
                Id = Guid.NewGuid(),
                Code = "2026-10",
                Name = "2026-10",
                PeriodType = PeriodType.Month,
                StartDate = PeriodStart,
                EndDate = PeriodEnd,
                Status = PeriodStatus.Open,
                UpdatedAt = Now,
            };
            db.EvaluationPeriods.Add(period);

            Employee Emp(string code, Department dept) => new()
            {
                Id = Guid.NewGuid(),
                EmployeeCode = code,
                FullName = code,
                Email = $"{code.ToLowerInvariant()}@bpms.com",
                DepartmentId = dept.Id,
                JobPositionId = position.Id,
                Status = EmployeeStatus.Active,
                LastSyncAt = Now,
                UpdatedAt = Now,
            };
            var manager = Emp("MGR", sales);
            var staff1 = Emp("S1", team);
            var staff2 = Emp("S2", team);
            var staff3 = Emp("S3", team);
            var outside = Emp("OUT", other);
            db.Employees.AddRange(manager, staff1, staff2, staff3, outside);
            foreach (var employee in new[] { manager, staff1, staff2, staff3, outside })
            {
                db.Accounts.Add(new Account
                {
                    Id = employee.Id,
                    EmployeeId = employee.Id,
                    Username = employee.Email,
                    PasswordHash = "hash",
                    LastSyncAt = Now,
                    UpdatedAt = Now,
                });
            }

            await db.SaveChangesAsync();

            var formula = new PerformanceScoreFormula();
            var calculator = new PerformanceScoreCalculator(db, formula);
            var grants = new[]
            {
                new UserPermissionDto("evaluation", "read", PermissionScopes.Dept, "EVALUATION_READ"),
                new UserPermissionDto("evaluation", "submit", submitScope, "EVALUATION_SUBMIT"),
                new UserPermissionDto("evaluation", "calculate", PermissionScopes.Dept, "EVALUATION_CALCULATE"),
                new UserPermissionDto("evaluation", "calibrate", PermissionScopes.Dept, "EVALUATION_CALIBRATE"),
            };
            var permissions = new FakePermissionService(null, grants);

            return new World
            {
                Db = db,
                Team = team,
                Period = period,
                Profile = profile,
                SalesRoot = salesRoot,
                QualitativeRoot = qualityRoot,
                Hdv = hdv,
                Kh = kh,
                Csat = csat,
                Manager = manager,
                Staff1 = staff1,
                Staff2 = staff2,
                Staff3 = staff3,
                OutsideStaff = outside,
                QualitativeScore = qualitativeScore,
                Calculator = calculator,
                Evaluation = new EvaluationService(db, NullLogger<EvaluationService>.Instance, permissions, calculator),
                Endpoint = new PerformanceEvaluationService(
                    db, permissions, calculator, new FixedTime(Now), NullLogger<PerformanceEvaluationService>.Instance),
            };
        }

        public Task<PerformanceScoreResult> ScoreAsync(Employee employee) =>
            ScoreManyAsync(employee).ContinueWith(t => t.Result[employee.Id]);

        public async Task<IReadOnlyDictionary<Guid, PerformanceScoreResult>> ScoreManyAsync(params Employee[] employees)
        {
            await Db.SaveChangesAsync();
            var profile = await Db.EvaluationProfiles
                .Include(p => p.EvaluationProfileItems).ThenInclude(i => i.KpiDefinition).ThenInclude(k => k!.KpiGroup)
                .SingleAsync(p => p.Id == Profile.Id);
            var qualitative = new Dictionary<Guid, QualitativeScoreInputDto>();
            if (QualitativeScore.HasValue)
            {
                qualitative[QualitativeRoot.Id] = new QualitativeScoreInputDto(QualitativeRoot.Id, QualitativeScore.Value, "notes");
            }

            return await Calculator.CalculateAsync(
                employees.Select(e => new PerformanceScoreSubject(e.Id, e.DepartmentId, profile, qualitative)).ToList(),
                Period);
        }

        public Task<EvaluationFormDto> FormAsync(Employee employee) =>
            Evaluation.GetEvaluationFormAsync(employee.Id, Period.Id, Manager.Id);

        public Task<EvaluationResultDto> SubmitAsync(Employee employee, bool isDraft) =>
            SubmitAsync(new SubmitEvaluationRequestDto(
                employee.Id,
                Period.Id,
                Comment: "ok",
                QualitativeScores: QualitativeScore.HasValue
                    ? [new QualitativeScoreInputDto(QualitativeRoot.Id, QualitativeScore.Value, "notes")]
                    : null,
                IsDraft: isDraft));

        public Task<EvaluationResultDto> SubmitAsync(SubmitEvaluationRequestDto request) =>
            Evaluation.SubmitEvaluationAsync(request, Manager.Id);

        public Task<G54.BLL.Dtos.Performance.PerformanceEvaluationDto> CalculateEndpointAsync(Guid evaluationId) =>
            Endpoint.CalculateAsync(Manager.Id, evaluationId);

        public TargetAssignment AddTarget(
            KpiDefinition kpi,
            decimal value,
            Employee? employee = null,
            Guid? departmentId = null,
            int version = 1)
        {
            var target = new TargetAssignment
            {
                Id = Guid.NewGuid(),
                KpiDefinitionId = kpi.Id,
                EmployeeId = departmentId is null ? (employee ?? Staff1).Id : null,
                DepartmentId = departmentId,
                EvaluationPeriodId = Period.Id,
                Version = version,
                TargetValue = value,
                Status = TargetAssignmentStatus.Active,
                CreatedAt = Now,
                UpdatedAt = Now,
            };
            Db.TargetAssignments.Add(target);
            return target;
        }

        /// <summary>
        /// Records progress the way verification does: on a superseded carrier target of the owner, so it never competes in
        /// target resolution.
        /// </summary>
        public ActualProgress AddProgress(
            KpiDefinition kpi,
            decimal value,
            DateOnly? date = null,
            Employee? owner = null,
            ActualProgressStatus status = ActualProgressStatus.Valid)
        {
            var employee = owner ?? Staff1;
            var carrier = Db.TargetAssignments.Local.FirstOrDefault(t =>
                t.KpiDefinitionId == kpi.Id && t.EmployeeId == employee.Id && t.Status == TargetAssignmentStatus.Superseded);
            if (carrier is null)
            {
                carrier = new TargetAssignment
                {
                    Id = Guid.NewGuid(),
                    KpiDefinitionId = kpi.Id,
                    EmployeeId = employee.Id,
                    EvaluationPeriodId = Period.Id,
                    Version = 1,
                    TargetValue = 0m,
                    Status = TargetAssignmentStatus.Superseded,
                    CreatedAt = Now,
                    UpdatedAt = Now,
                };
                Db.TargetAssignments.Add(carrier);
            }

            var progress = new ActualProgress
            {
                Id = Guid.NewGuid(),
                TargetAssignmentId = carrier.Id,
                ProgressDate = date ?? InPeriod,
                Value = value,
                Status = status,
                UpdatedAt = Now,
            };
            Db.ActualProgresses.Add(progress);
            return progress;
        }

        public RiskEvent AddRisk(
            RiskEventSeverity severity,
            RiskEventStatus status = RiskEventStatus.Open,
            Employee? owner = null,
            DateTimeOffset? occurredAt = null)
        {
            var risk = new RiskEvent
            {
                Id = Guid.NewGuid(),
                EmployeeId = (owner ?? Staff1).Id,
                RecordedBy = Manager.Id,
                Code = $"RSK-{Interlocked.Increment(ref _riskSequence)}",
                EventType = "QA",
                Description = "risk",
                OccurredAt = occurredAt ?? new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero),
                Severity = severity,
                Status = status,
                UpdatedAt = Now,
            };
            Db.RiskEvents.Add(risk);
            return risk;
        }

        public PerformanceEvaluation AddEvaluation(Employee employee, decimal? score, PerformanceEvaluationStatus status)
        {
            var evaluation = new PerformanceEvaluation
            {
                Id = Guid.NewGuid(),
                EmployeeId = employee.Id,
                EvaluationPeriodId = Period.Id,
                EvaluationProfileId = Profile.Id,
                TotalScore = score,
                Status = status,
                CalculatedAt = score.HasValue ? Now : null,
                UpdatedAt = Now,
            };
            Db.PerformanceEvaluations.Add(evaluation);
            return evaluation;
        }

        public void AddLedgerEntry(Employee employee, decimal points, PerformanceEvaluation? evaluation = null)
        {
            evaluation ??= Db.PerformanceEvaluations.Local.FirstOrDefault(e => e.EmployeeId == employee.Id)
                ?? AddEvaluation(employee, null, PerformanceEvaluationStatus.Draft);
            Db.ScoreEntries.Add(new ScoreEntry
            {
                Id = Guid.NewGuid(),
                EvaluationId = evaluation.Id,
                EmployeeId = employee.Id,
                RecordedByEmployeeId = Manager.Id,
                Points = points,
                Kind = ScoreEntryKind.TaskDeduction,
                Source = ScoreEntrySource.Manager,
                Reason = "test",
                CreatedAt = Now,
            });
        }

        public void Dispose() => Db.Dispose();
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
