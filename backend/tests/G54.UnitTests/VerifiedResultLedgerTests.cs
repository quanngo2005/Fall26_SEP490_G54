using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Permission;
using G54.BLL.Dtos.Submission;
using G54.BLL.Dtos.Verification;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace G54.UnitTests;

/// <summary>
/// How an APPROVED verification writes the KPI ledger (<c>VerifiedResultRecorder</c>, reached through
/// <see cref="VerificationService.SubmitDecisionAsync"/>): which target receives the result, how results add up, and what
/// is written when nothing can be attributed. Complements <see cref="VerificationServiceTests"/>.
/// </summary>
public sealed class VerifiedResultLedgerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 4, 0, 0, TimeSpan.Zero); // 11:00 in Vietnam, 21 Oct
    private static readonly DateOnly BusinessToday = new(2026, 10, 21);

    // ----- Which target receives the result -------------------------------------------------

    [Fact]
    public async Task Approve_AttributesTheResultToTheTargetWhosePeriodEndsFirst()
    {
        using var rig = await Rig.CreateAsync();
        var kpi = rig.AddWorkTypeKpi();
        var wide = rig.AddTarget(kpi, rig.AddPeriod("YEAR", BusinessToday.AddDays(-200), BusinessToday.AddDays(100)));
        var narrow = rig.AddTarget(kpi, rig.AddPeriod("MONTH", BusinessToday.AddDays(-10), BusinessToday.AddDays(10)));
        await rig.SaveAsync();

        await rig.ApproveAsync(25m);

        var progress = await rig.Db.ActualProgresses.SingleAsync();
        Assert.Equal(narrow.Id, progress.TargetAssignmentId);
        Assert.NotEqual(wide.Id, progress.TargetAssignmentId);
        Assert.Equal(25m, progress.Value);
        Assert.Equal(BusinessToday, progress.ProgressDate);
    }

    [Theory]
    [InlineData(0, 10)] // starts today
    [InlineData(-10, 0)] // ends today
    public async Task Approve_UsesATargetWhileItsPeriodCoversTheBusinessToday(int startOffset, int endOffset)
    {
        using var rig = await Rig.CreateAsync();
        var kpi = rig.AddWorkTypeKpi();
        rig.AddTarget(kpi, rig.AddPeriod("P", BusinessToday.AddDays(startOffset), BusinessToday.AddDays(endOffset)));
        await rig.SaveAsync();

        await rig.ApproveAsync(25m);

        Assert.Equal(1, await rig.Db.WorkResultItems.CountAsync());
        Assert.Equal(1, await rig.Db.ActualProgresses.CountAsync());
        Assert.Equal(1, await rig.Db.VerifiedWorkResults.CountAsync());
        Assert.Equal(SubmissionStatus.Approved, (await rig.Db.ResultSubmissions.SingleAsync()).Status);
    }

    [Theory]
    [InlineData(1, 10)] // starts tomorrow
    [InlineData(-10, -1)] // ended yesterday
    public async Task Approve_RejectsWhenTheTargetPeriodDoesNotCoverTheBusinessToday(int startOffset, int endOffset)
    {
        using var rig = await Rig.CreateAsync();
        var kpi = rig.AddWorkTypeKpi();
        rig.AddTarget(kpi, rig.AddPeriod("P", BusinessToday.AddDays(startOffset), BusinessToday.AddDays(endOffset)));
        await rig.SaveAsync();

        await rig.AssertApprovalRejectedForMissingTargetAsync(25m);
    }

    [Fact]
    public async Task Approve_RejectsWhenThereIsNoTargetAtAll()
    {
        using var rig = await Rig.CreateAsync();
        rig.AddWorkTypeKpi();
        await rig.SaveAsync();

        await rig.AssertApprovalRejectedForMissingTargetAsync(25m);
    }

    [Theory]
    [InlineData(TargetAssignmentStatus.Superseded)]
    [InlineData(TargetAssignmentStatus.Closed)]
    [InlineData(TargetAssignmentStatus.Draft)]
    public async Task Approve_RejectsWhenTheOnlyTargetIsNotActive(TargetAssignmentStatus status)
    {
        using var rig = await Rig.CreateAsync();
        var kpi = rig.AddWorkTypeKpi();
        rig.AddTarget(kpi, rig.AddPeriod("P", BusinessToday.AddDays(-5), BusinessToday.AddDays(5)), status);
        await rig.SaveAsync();

        await rig.AssertApprovalRejectedForMissingTargetAsync(25m);
    }

    // ----- BR-15 mid-period target change: a result is attributed to the version in effect on its date -----

    [Fact]
    public async Task Approve_OnOrAfterTheNewVersionsEffectiveDate_UsesTheActiveVersion()
    {
        using var rig = await Rig.CreateAsync();
        var kpi = rig.AddWorkTypeKpi();
        var period = rig.AddPeriod("P", BusinessToday.AddDays(-30), BusinessToday.AddDays(30));
        var old = rig.AddTarget(kpi, period, TargetAssignmentStatus.Superseded);
        old.EffectiveFrom = BusinessToday.AddDays(-30);
        old.EffectiveTo = BusinessToday.AddDays(-1);
        var active = rig.AddTarget(kpi, period);
        active.EffectiveFrom = BusinessToday;
        active.PreviousVersionId = old.Id;
        active.Version = 2;
        await rig.SaveAsync();

        await rig.ApproveAsync(25m);

        var progress = await rig.Db.ActualProgresses.SingleAsync();
        Assert.Equal(active.Id, progress.TargetAssignmentId);
    }

    [Fact]
    public async Task Approve_BeforeTheNewVersionsEffectiveDate_FallsBackToTheFrozenVersionInEffectOnThatDate()
    {
        using var rig = await Rig.CreateAsync();
        var kpi = rig.AddWorkTypeKpi();
        var period = rig.AddPeriod("P", BusinessToday.AddDays(-30), BusinessToday.AddDays(30));
        var old = rig.AddTarget(kpi, period, TargetAssignmentStatus.Superseded);
        old.EffectiveFrom = BusinessToday.AddDays(-30);
        old.EffectiveTo = BusinessToday.AddDays(5);
        var active = rig.AddTarget(kpi, period);
        active.EffectiveFrom = BusinessToday.AddDays(6); // starts after today: today's late result still belongs to "old"
        active.PreviousVersionId = old.Id;
        active.Version = 2;
        await rig.SaveAsync();

        await rig.ApproveAsync(25m);

        var progress = await rig.Db.ActualProgresses.SingleAsync();
        Assert.Equal(old.Id, progress.TargetAssignmentId);
    }

    [Fact]
    public async Task Approve_RejectsWhenTheOnlyTargetBelongsToAnotherEmployee()
    {
        using var rig = await Rig.CreateAsync();
        var kpi = rig.AddWorkTypeKpi();
        rig.AddTarget(kpi, rig.AddPeriod("P", BusinessToday.AddDays(-5), BusinessToday.AddDays(5)), owner: rig.Checker);
        await rig.SaveAsync();

        await rig.AssertApprovalRejectedForMissingTargetAsync(25m);
    }

    [Fact]
    public async Task Approve_RejectsWhenOnlyOneHandoverRecipientHasATarget()
    {
        using var rig = await Rig.CreateAsync();
        var kpi = rig.AddWorkTypeKpi();
        var period = rig.AddPeriod("P", BusinessToday.AddDays(-5), BusinessToday.AddDays(5));
        rig.AddTarget(kpi, period); // the maker (referrer) has a target, the checker (processor) does not
        rig.AddAcceptedHandover(from: rig.Maker, to: rig.Checker, referrerSharePercent: 30m);
        await rig.SaveAsync();

        await rig.AssertApprovalRejectedForMissingTargetAsync(100m);
    }

    [Fact]
    public async Task Approve_SplitsAcrossBothHandoverRecipientsWhenBothHaveATarget()
    {
        using var rig = await Rig.CreateAsync();
        var kpi = rig.AddWorkTypeKpi();
        var period = rig.AddPeriod("P", BusinessToday.AddDays(-5), BusinessToday.AddDays(5));
        rig.AddTarget(kpi, period);
        rig.AddTarget(kpi, period, owner: rig.Checker);
        rig.AddAcceptedHandover(from: rig.Maker, to: rig.Checker, referrerSharePercent: 30m);
        await rig.SaveAsync();

        await rig.ApproveAsync(100m);

        var items = await rig.Db.WorkResultItems.ToListAsync();
        Assert.Equal(30m, items.Single(i => i.EmployeeId == rig.Maker.Id).Value);
        Assert.Equal(70m, items.Single(i => i.EmployeeId == rig.Checker.Id).Value);
    }

    [Fact]
    public async Task Approve_StillCreditsATargetThatAlreadyReachedItsGoal()
    {
        using var rig = await Rig.CreateAsync();
        var kpi = rig.AddWorkTypeKpi();
        var target = rig.AddTarget(kpi, rig.AddPeriod("P", BusinessToday.AddDays(-5), BusinessToday.AddDays(5)));
        target.TargetValue = 10m;
        await rig.SaveAsync();
        await rig.ApproveAsync(10m);

        var second = await rig.AddSecondWorkAsync();
        await rig.ApproveAsync(5m, second);

        Assert.Equal(15m, (await rig.Db.ActualProgresses.SingleAsync()).Value);
        Assert.Equal(2, await rig.Db.WorkResultItems.CountAsync());
    }

    // ----- Accumulation ---------------------------------------------------------------------

    [Fact]
    public async Task Approve_AddsASecondResultOfTheSameDayToTheExistingProgressRow()
    {
        using var rig = await Rig.CreateAsync();
        var kpi = rig.AddWorkTypeKpi();
        var target = rig.AddTarget(kpi, rig.AddPeriod("P", BusinessToday.AddDays(-5), BusinessToday.AddDays(5)));
        await rig.SaveAsync();
        await rig.ApproveAsync(25m);

        var second = await rig.AddSecondWorkAsync();
        await rig.ApproveAsync(15m, second);

        var progress = await rig.Db.ActualProgresses.SingleAsync(); // one row for the day, not two
        Assert.Equal(target.Id, progress.TargetAssignmentId);
        Assert.Equal(40m, progress.Value);
        var items = await rig.Db.WorkResultItems.OrderBy(i => i.Value).ToListAsync();
        Assert.Equal([15m, 25m], items.Select(i => i.Value));
        Assert.All(items, item => Assert.Equal(progress.Id, item.ActualProgressId));
        Assert.Equal(2, await rig.Db.VerifiedWorkResults.CountAsync());
    }

    [Fact]
    public async Task Approve_AValueOfZeroIsRecordedAsAnItemWithNoPoints()
    {
        using var rig = await Rig.CreateAsync();
        var kpi = rig.AddWorkTypeKpi();
        rig.AddTarget(kpi, rig.AddPeriod("P", BusinessToday.AddDays(-5), BusinessToday.AddDays(5)));
        await rig.SaveAsync();

        await rig.ApproveAsync(0m);

        var item = await rig.Db.WorkResultItems.SingleAsync();
        Assert.Equal(0m, item.Value);
        Assert.Equal(0m, (await rig.Db.ActualProgresses.SingleAsync()).Value);
    }

    // ----- Multiplier (BR-48 Task Multiplier) -----------------------------------------------

    [Fact]
    public async Task Approve_AppliesTheMappingMultiplierToTheRawValueAndStoresBoth()
    {
        using var rig = await Rig.CreateAsync();
        var kpi = rig.AddWorkTypeKpi(multiplier: 1.5m);
        rig.AddTarget(kpi, rig.AddPeriod("P", BusinessToday.AddDays(-5), BusinessToday.AddDays(5)));
        await rig.SaveAsync();

        await rig.ApproveAsync(1m);

        var item = await rig.Db.WorkResultItems.SingleAsync();
        Assert.Equal(1m, item.RawValue);
        Assert.Equal(1.5m, item.MultiplierApplied);
        Assert.Equal(1.5m, item.Value);
        // The ledger holds the real amount; the multiplier is applied only when the KPI is scored.
        Assert.Equal(1m, (await rig.Db.ActualProgresses.SingleAsync()).Value);
    }

    [Fact]
    public async Task Approve_SplitsTheHandoverOnTheRawValueThenAppliesTheMultiplierToEachShare()
    {
        using var rig = await Rig.CreateAsync();
        var kpi = rig.AddWorkTypeKpi(multiplier: 1.5m);
        var period = rig.AddPeriod("P", BusinessToday.AddDays(-5), BusinessToday.AddDays(5));
        rig.AddTarget(kpi, period);
        rig.AddTarget(kpi, period, owner: rig.Checker);
        rig.AddAcceptedHandover(from: rig.Maker, to: rig.Checker, referrerSharePercent: 30m);
        await rig.SaveAsync();

        await rig.ApproveAsync(1m);

        var items = await rig.Db.WorkResultItems.ToListAsync();
        var referrer = items.Single(i => i.EmployeeId == rig.Maker.Id);
        var processor = items.Single(i => i.EmployeeId == rig.Checker.Id);
        Assert.Equal(0.3m, referrer.RawValue);
        Assert.Equal(0.45m, referrer.Value);
        Assert.Equal(0.7m, processor.RawValue);
        Assert.Equal(1.05m, processor.Value);
        Assert.All(items, i => Assert.Equal(1.5m, i.MultiplierApplied));
    }

    [Fact]
    public async Task Approve_UsesTheMappingInEffectTodayOverAnExpiredOne()
    {
        using var rig = await Rig.CreateAsync();
        var kpi = rig.AddWorkTypeKpiWithMapping(1.0m, BusinessToday.AddDays(-365), BusinessToday.AddDays(-1)); // expired yesterday
        rig.AddMapping(kpi, 1.5m, BusinessToday, null); // in effect from today
        rig.AddTarget(kpi, rig.AddPeriod("P", BusinessToday.AddDays(-5), BusinessToday.AddDays(5)));
        await rig.SaveAsync();

        await rig.ApproveAsync(1m);

        var item = await rig.Db.WorkResultItems.SingleAsync();
        Assert.Equal(1.5m, item.MultiplierApplied);
        Assert.Equal(1.5m, item.Value);
    }

    [Fact]
    public async Task Approve_UsesTheLatestEffectiveFromWhenMultipleMappingsAreInEffect()
    {
        using var rig = await Rig.CreateAsync();
        var kpi = rig.AddWorkTypeKpiWithMapping(1.0m, BusinessToday.AddDays(-365), null); // open-ended, older
        rig.AddMapping(kpi, 1.2m, BusinessToday.AddDays(-30), null); // open-ended, more recent EffectiveFrom
        rig.AddTarget(kpi, rig.AddPeriod("P", BusinessToday.AddDays(-5), BusinessToday.AddDays(5)));
        await rig.SaveAsync();

        await rig.ApproveAsync(1m);

        Assert.Equal(1.2m, (await rig.Db.WorkResultItems.SingleAsync()).MultiplierApplied);
    }

    [Fact]
    public async Task Approve_CreditsEveryMappedKpiIncludingAnOptionalOne()
    {
        using var rig = await Rig.CreateAsync();
        var optional = rig.AddWorkTypeKpi(isRequired: false, effectiveFromDaysAgo: 1);
        var required = rig.AddWorkTypeKpi(effectiveFromDaysAgo: 365);
        var period = rig.AddPeriod("P", BusinessToday.AddDays(-5), BusinessToday.AddDays(5));
        rig.AddTarget(optional, period);
        var requiredTarget = rig.AddTarget(required, period);
        await rig.SaveAsync();

        await rig.ApproveAsync(1m);

        var progress = await rig.Db.ActualProgresses.ToListAsync();
        Assert.Equal(2, progress.Count);
        Assert.Contains(progress, p => p.TargetAssignmentId == requiredTarget.Id);
    }

    [Fact]
    public void SelectInEffectMapping_BreaksATieByIdWhateverTheOrderOfTheMappings()
    {
        var from = BusinessToday.AddDays(-1);
        var low = new WorkTypeKpi { Id = new Guid("00000000-0000-0000-0000-000000000001"), IsRequired = true, EffectiveFrom = from };
        var high = new WorkTypeKpi { Id = new Guid("00000000-0000-0000-0000-000000000002"), IsRequired = true, EffectiveFrom = from };

        Assert.Same(low, VerifiedResultRecorder.SelectInEffectMapping(new WorkType { WorkTypeKpis = [high, low] }, BusinessToday));
        Assert.Same(low, VerifiedResultRecorder.SelectInEffectMapping(new WorkType { WorkTypeKpis = [low, high] }, BusinessToday));
    }

    [Fact]
    public async Task Approve_RecordsNoContributionWhenNoMappingIsInEffectToday()
    {
        using var rig = await Rig.CreateAsync();
        var kpi = rig.AddWorkTypeKpiWithMapping(1.5m, BusinessToday.AddDays(-365), BusinessToday.AddDays(-1)); // expired
        rig.AddTarget(kpi, rig.AddPeriod("P", BusinessToday.AddDays(-5), BusinessToday.AddDays(5)));
        await rig.SaveAsync();

        var result = await rig.ApproveAsync(1m);

        Assert.Equal("APPROVED", result.SubmissionStatus);
        Assert.Equal(1, await rig.Db.VerifiedWorkResults.CountAsync());
        Assert.Empty(await rig.Db.WorkResultItems.ToListAsync());
        Assert.Empty(await rig.Db.ActualProgresses.ToListAsync());
    }

    [Fact]
    public async Task Approve_EditingTheMultiplierAfterwardsDoesNotChangeAlreadyStoredItems()
    {
        using var rig = await Rig.CreateAsync();
        var kpi = rig.AddWorkTypeKpi(multiplier: 1.5m);
        var target = rig.AddTarget(kpi, rig.AddPeriod("P", BusinessToday.AddDays(-5), BusinessToday.AddDays(5)));
        await rig.SaveAsync();
        await rig.ApproveAsync(1m);

        var firstItem = await rig.Db.WorkResultItems.SingleAsync();
        Assert.Equal(1.5m, firstItem.Value);

        // Admin changes the rate in place (same mapping row, new multiplier) for future approvals.
        var mapping = await rig.Db.WorkTypeKpis.SingleAsync(k => k.KpiDefinitionId == kpi.Id);
        mapping.Multiplier = 1.6m;
        await rig.SaveAsync();

        var second = await rig.AddSecondWorkAsync();
        await rig.ApproveAsync(1m, second);

        // The already-stored item keeps its old value; only the new approval uses the new rate.
        var items = await rig.Db.WorkResultItems.OrderBy(i => i.Value).ToListAsync();
        Assert.Equal([1.5m, 1.6m], items.Select(i => i.Value));
        Assert.Equal(2m, (await rig.Db.ActualProgresses.SingleAsync(p => p.TargetAssignmentId == target.Id)).Value);
    }

    // ----- Nothing to credit ----------------------------------------------------------------

    [Fact]
    public async Task Approve_AWorkTypeWithoutAnyKpiStillRecordsTheVerifiedResultButNoItem()
    {
        using var rig = await Rig.CreateAsync(); // its work type has no KPI mapping

        var result = await rig.ApproveAsync(25m);

        Assert.Equal("APPROVED", result.SubmissionStatus);
        Assert.Equal("VERIFIED", result.WorkStatus);
        Assert.Equal(1, await rig.Db.VerifiedWorkResults.CountAsync());
        Assert.Empty(await rig.Db.WorkResultItems.ToListAsync());
        Assert.Empty(await rig.Db.ActualProgresses.ToListAsync());
    }

    [Fact]
    public async Task Approve_AWorkWithoutAWorkTypeStillRecordsTheVerifiedResultButNoItem()
    {
        using var rig = await Rig.CreateAsync(withWorkType: false);

        await rig.ApproveAsync(25m);

        Assert.Equal(1, await rig.Db.VerifiedWorkResults.CountAsync());
        Assert.Empty(await rig.Db.WorkResultItems.ToListAsync());
    }

    // ----- Several KPIs and the aggregation types -------------------------------------------

    [Fact]
    public async Task Approve_CreditsEveryKpiOfTheWorkTypeWithItsOwnSubmittedValue()
    {
        using var rig = await Rig.CreateAsync();
        var cards = rig.AddKpi(KpiAggregationType.Sum);
        var amount = rig.AddKpi(KpiAggregationType.Sum, multiplier: 2m);
        var period = rig.AddPeriod("P", BusinessToday.AddDays(-5), BusinessToday.AddDays(5));
        var cardsTarget = rig.AddTarget(cards, period);
        var amountTarget = rig.AddTarget(amount, period);
        await rig.SaveAsync();

        await rig.ApproveWithMetricsAsync([
            new SubmissionMetricInputDto(cards.Id, 1m, null),
            new SubmissionMetricInputDto(amount.Id, 500m, null),
        ]);

        var progress = await rig.Db.ActualProgresses.ToListAsync();
        Assert.Equal(1m, progress.Single(p => p.TargetAssignmentId == cardsTarget.Id).Value);
        Assert.Equal(500m, progress.Single(p => p.TargetAssignmentId == amountTarget.Id).Value);
        var items = await rig.Db.WorkResultItems.ToListAsync();
        Assert.Equal(2, items.Count);
        Assert.Equal(1000m, items.Single(i => i.RawValue == 500m).Value);
        Assert.Equal(1, await rig.Db.VerifiedWorkResults.CountAsync());
    }

    [Fact]
    public async Task Approve_WhenOneOfSeveralKpisHasNoTarget_RejectsAndWritesNothing()
    {
        using var rig = await Rig.CreateAsync();
        var cards = rig.AddKpi(KpiAggregationType.Sum);
        var amount = rig.AddKpi(KpiAggregationType.Sum);
        rig.AddTarget(cards, rig.AddPeriod("P", BusinessToday.AddDays(-5), BusinessToday.AddDays(5)));
        await rig.SaveAsync();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => rig.ApproveWithMetricsAsync([
            new SubmissionMetricInputDto(cards.Id, 1m, null),
            new SubmissionMetricInputDto(amount.Id, 500m, null),
        ]));

        Assert.Equal(ErrorCodes.KpiTargetNotAssigned, ex.ErrorCode);
        Assert.Empty(await rig.Db.WorkResultItems.ToListAsync());
        Assert.Empty(await rig.Db.ActualProgresses.ToListAsync());
        Assert.Equal(SubmissionStatus.Submitted, (await rig.Db.ResultSubmissions.SingleAsync()).Status);
    }

    [Fact]
    public async Task Approve_IgnoresAMetricWhoseKpiIsNotMappedToTheWorkType()
    {
        using var rig = await Rig.CreateAsync();
        var mapped = rig.AddKpi(KpiAggregationType.Sum);
        rig.AddTarget(mapped, rig.AddPeriod("P", BusinessToday.AddDays(-5), BusinessToday.AddDays(5)));
        await rig.SaveAsync();

        await rig.ApproveWithMetricsAsync([
            new SubmissionMetricInputDto(mapped.Id, 3m, null),
            new SubmissionMetricInputDto(Guid.NewGuid(), 99m, null),
        ]);

        Assert.Equal(3m, (await rig.Db.ActualProgresses.SingleAsync()).Value);
    }

    [Fact]
    public async Task Approve_ARatioKpiAddsItsNumeratorAndDenominatorToTheDayRow()
    {
        using var rig = await Rig.CreateAsync();
        var ratio = rig.AddKpi(KpiAggregationType.Ratio);
        rig.AddTarget(ratio, rig.AddPeriod("P", BusinessToday.AddDays(-5), BusinessToday.AddDays(5)));
        await rig.SaveAsync();
        await rig.ApproveWithMetricsAsync([new SubmissionMetricInputDto(ratio.Id, 2m, 50m)]);

        var second = await rig.AddSecondWorkAsync();
        await rig.ApproveWithMetricsAsync([new SubmissionMetricInputDto(ratio.Id, 1m, 50m)], second);

        var progress = await rig.Db.ActualProgresses.SingleAsync();
        Assert.Equal(3m, progress.Value);
        Assert.Equal(100m, progress.Denominator);
    }

    [Fact]
    public async Task Approve_ARatioKpiWithoutADenominatorIsRejected()
    {
        using var rig = await Rig.CreateAsync();
        var ratio = rig.AddKpi(KpiAggregationType.Ratio);
        rig.AddTarget(ratio, rig.AddPeriod("P", BusinessToday.AddDays(-5), BusinessToday.AddDays(5)));
        await rig.SaveAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            rig.ApproveWithMetricsAsync([new SubmissionMetricInputDto(ratio.Id, 2m, null)]));

        Assert.Empty(await rig.Db.ActualProgresses.ToListAsync());
    }

    [Fact]
    public async Task Approve_AnAverageKpiCountsOneEntryPerContribution()
    {
        using var rig = await Rig.CreateAsync();
        var avg = rig.AddKpi(KpiAggregationType.Avg);
        rig.AddTarget(avg, rig.AddPeriod("P", BusinessToday.AddDays(-5), BusinessToday.AddDays(5)));
        await rig.SaveAsync();
        await rig.ApproveWithMetricsAsync([new SubmissionMetricInputDto(avg.Id, 4m, null)]);

        var second = await rig.AddSecondWorkAsync();
        await rig.ApproveWithMetricsAsync([new SubmissionMetricInputDto(avg.Id, 6m, null)], second);

        var progress = await rig.Db.ActualProgresses.SingleAsync();
        Assert.Equal(10m, progress.Value);
        Assert.Equal(2m, progress.Denominator); // average = 10 / 2 = 5
    }

    [Fact]
    public async Task Approve_ASumKpiLeavesTheDenominatorEmpty()
    {
        using var rig = await Rig.CreateAsync();
        var sum = rig.AddKpi(KpiAggregationType.Sum);
        rig.AddTarget(sum, rig.AddPeriod("P", BusinessToday.AddDays(-5), BusinessToday.AddDays(5)));
        await rig.SaveAsync();

        await rig.ApproveWithMetricsAsync([new SubmissionMetricInputDto(sum.Id, 4m, null)]);

        Assert.Null((await rig.Db.ActualProgresses.SingleAsync()).Denominator);
    }

    [Fact]
    public async Task Approve_ANoAggregationKpiIsNotCredited()
    {
        using var rig = await Rig.CreateAsync();
        var none = rig.AddKpi(KpiAggregationType.NoAggregation);
        await rig.SaveAsync();

        await rig.ApproveWithMetricsAsync([new SubmissionMetricInputDto(none.Id, 4m, null)]);

        Assert.Empty(await rig.Db.ActualProgresses.ToListAsync());
        Assert.Empty(await rig.Db.WorkResultItems.ToListAsync());
    }

    // ----- Decisions that must not touch the ledger -----------------------------------------

    [Theory]
    [InlineData("REVISION_REQUIRED")]
    [InlineData("REJECTED")]
    public async Task NonApprovingDecisions_NeverWriteTheLedger(string decision)
    {
        using var rig = await Rig.CreateAsync();
        var kpi = rig.AddWorkTypeKpi();
        rig.AddTarget(kpi, rig.AddPeriod("P", BusinessToday.AddDays(-5), BusinessToday.AddDays(5)));
        await rig.SaveAsync();

        await rig.Service().SubmitDecisionAsync(rig.Submission.Id, rig.CheckerAccount.Id, new SubmitVerificationDecisionDto
        {
            Decision = decision,
            Feedback = "Lý do đủ dài để hợp lệ.",
            // The payload check compares with the real clock, not the injected TimeProvider.
            ResubmitDeadline = DateTimeOffset.UtcNow.AddDays(3),
        });

        Assert.Empty(await rig.Db.VerifiedWorkResults.ToListAsync());
        Assert.Empty(await rig.Db.WorkResultItems.ToListAsync());
        Assert.Empty(await rig.Db.ActualProgresses.ToListAsync());
    }

    // ----- a locked period never receives progress ------------------------------------------------

    [Theory]
    [InlineData(PeriodStatus.Locked)]
    [InlineData(PeriodStatus.Closed)]
    public async Task Approve_IntoALockedOrClosedPeriod_ThrowsPeriodLockedAndWritesNothing(PeriodStatus status)
    {
        using var rig = await Rig.CreateAsync();
        var kpi = rig.AddWorkTypeKpi();
        var target = rig.AddTarget(kpi, rig.AddPeriod("P", BusinessToday.AddDays(-5), BusinessToday.AddDays(5), status));
        await rig.SaveAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => rig.ApproveAsync(25m));

        Assert.Equal(ErrorCodes.PeriodLocked, ex.ErrorCode);
        Assert.Equal(409, ex.StatusCode);
        Assert.Empty(await rig.Db.Verifications.ToListAsync());
        Assert.Empty(await rig.Db.VerifiedWorkResults.ToListAsync());
        Assert.Empty(await rig.Db.WorkResultItems.ToListAsync());
        Assert.Empty(await rig.Db.ActualProgresses.Where(p => p.TargetAssignmentId == target.Id).ToListAsync());
        Assert.Equal(SubmissionStatus.Submitted, (await rig.Db.ResultSubmissions.SingleAsync()).Status);
        Assert.Equal(WorkStatus.Submitted, (await rig.Db.Works.SingleAsync()).Status);
    }

    [Theory]
    [InlineData(PeriodStatus.Open)]
    [InlineData(PeriodStatus.InReview)]
    public async Task Approve_IntoAnOpenOrInReviewPeriod_StillRecordsProgress(PeriodStatus status)
    {
        using var rig = await Rig.CreateAsync();
        var kpi = rig.AddWorkTypeKpi();
        var target = rig.AddTarget(kpi, rig.AddPeriod("P", BusinessToday.AddDays(-5), BusinessToday.AddDays(5), status));
        await rig.SaveAsync();

        await rig.ApproveAsync(25m);

        Assert.Equal(25m, (await rig.Db.ActualProgresses.SingleAsync(p => p.TargetAssignmentId == target.Id)).Value);
    }

    [Fact]
    public async Task Approve_WhenALockedPeriodIsOnlyOneOfSeveralCoveringTargets_UsesTheEarliestEndingOne()
    {
        using var rig = await Rig.CreateAsync();
        var kpi = rig.AddWorkTypeKpi();
        // The month ends first, so it is the one that receives the progress. A locked year does not block an open month.
        rig.AddTarget(kpi, rig.AddPeriod("YEAR", BusinessToday.AddDays(-200), BusinessToday.AddDays(100), PeriodStatus.Locked));
        var month = rig.AddTarget(kpi, rig.AddPeriod("MONTH", BusinessToday.AddDays(-10), BusinessToday.AddDays(10)));
        await rig.SaveAsync();

        await rig.ApproveAsync(25m);

        Assert.Equal(25m, (await rig.Db.ActualProgresses.SingleAsync(p => p.TargetAssignmentId == month.Id)).Value);
    }

    // ----- Fixture --------------------------------------------------------------------------

    private sealed class Rig : IDisposable
    {
        private int _periodCounter;

        public required AppDbContext Db { get; init; }
        public required Department Department { get; init; }
        public required JobPosition Position { get; init; }
        public required Employee Maker { get; init; }
        public required Employee Checker { get; init; }
        public required Account CheckerAccount { get; init; }
        public required Work Work { get; init; }
        public required ResultSubmission Submission { get; init; }
        public required WorkType? WorkType { get; init; }

        public static async Task<Rig> CreateAsync(bool withWorkType = true)
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

            var department = new Department
            {
                Id = Guid.NewGuid(),
                Code = "D",
                Name = "Department",
                LevelType = DepartmentLevelType.Department,
                Status = RecordStatus.Active,
                UpdatedAt = Now,
            };
            var position = new JobPosition
            {
                Id = Guid.NewGuid(),
                Code = "P",
                Title = "Position",
                Status = RecordStatus.Active,
                UpdatedAt = Now,
            };
            db.Departments.Add(department);
            db.JobPositions.Add(position);

            var maker = Person("MAKER", department, position);
            var checker = Person("CHECKER", department, position);
            db.Employees.AddRange(maker, checker);
            var account = new Account
            {
                Id = Guid.NewGuid(),
                EmployeeId = checker.Id,
                Username = "checker",
                PasswordHash = "hash",
                LastSyncAt = Now,
                UpdatedAt = Now,
            };
            db.Accounts.Add(account);

            WorkType? workType = null;
            if (withWorkType)
            {
                workType = new WorkType
                {
                    Id = Guid.NewGuid(),
                    Code = "WT",
                    Name = "Work type",
                    Status = RecordStatus.Active,
                    UpdatedAt = Now,
                };
                db.WorkTypes.Add(workType);
            }

            var (work, submission) = NewSubmittedWork("WRK-1", maker, workType);
            db.Works.Add(work);
            db.ResultSubmissions.Add(submission);
            await db.SaveChangesAsync();

            return new Rig
            {
                Db = db,
                Department = department,
                Position = position,
                Maker = maker,
                Checker = checker,
                CheckerAccount = account,
                Work = work,
                Submission = submission,
                WorkType = workType,
            };
        }

        public VerificationService Service() => new(
            Db,
            NullLogger<VerificationService>.Instance,
            new FixedTime(Now),
            new FakePermissionService(null, new UserPermissionDto("verification", "decide", "All", "VERIFICATION_DECIDE")));

        public Task<int> SaveAsync() => Db.SaveChangesAsync();

        /// <summary>Approves with <paramref name="value"/> as the number the employee submitted for every KPI mapped to the work type.</summary>
        public async Task<VerificationDecisionResultDto> ApproveAsync(decimal value, ResultSubmission? submission = null)
        {
            var kpiIds = await Db.WorkTypeKpis.Select(k => k.KpiDefinitionId).Distinct().ToListAsync();
            return await ApproveWithMetricsAsync(kpiIds.Select(id => new SubmissionMetricInputDto(id, value, null)).ToList(), submission);
        }

        public async Task<VerificationDecisionResultDto> ApproveWithMetricsAsync(
            List<SubmissionMetricInputDto> metrics,
            ResultSubmission? submission = null)
        {
            var target = await Db.ResultSubmissions.SingleAsync(s => s.Id == (submission ?? Submission).Id);
            target.Comment = ResultSubmissionService.BuildStoredComment(null, metrics);
            await Db.SaveChangesAsync();
            return await Service().SubmitDecisionAsync(
                target.Id,
                CheckerAccount.Id,
                new SubmitVerificationDecisionDto { Decision = "APPROVED" });
        }

        public KpiDefinition AddKpi(KpiAggregationType aggregation, bool isRequired = true, decimal multiplier = 1m)
        {
            var kpi = NewKpiDefinition();
            kpi.AggregationType = aggregation;
            AddMapping(kpi, multiplier, BusinessToday.AddDays(-365), null, isRequired);
            return kpi;
        }

        public KpiDefinition AddWorkTypeKpi(decimal multiplier = 1m, bool isRequired = true, int effectiveFromDaysAgo = 365)
        {
            var kpi = NewKpiDefinition();
            AddMapping(kpi, multiplier, BusinessToday.AddDays(-effectiveFromDaysAgo), null, isRequired);
            return kpi;
        }

        /// <summary>Adds another WorkTypeKpi mapping row for the same (new) KPI, with its own effective window and multiplier.</summary>
        public KpiDefinition AddWorkTypeKpiWithMapping(decimal multiplier, DateOnly effectiveFrom, DateOnly? effectiveTo)
        {
            var kpi = NewKpiDefinition();
            AddMapping(kpi, multiplier, effectiveFrom, effectiveTo);
            return kpi;
        }

        /// <summary>Adds a second mapping row of <paramref name="kpi"/> on the same work type (BR-48: a new EffectiveFrom row for a rate change).</summary>
        public void AddMapping(KpiDefinition kpi, decimal multiplier, DateOnly effectiveFrom, DateOnly? effectiveTo, bool isRequired = true)
        {
            Db.WorkTypeKpis.Add(new WorkTypeKpi
            {
                Id = Guid.NewGuid(),
                WorkTypeId = WorkType!.Id,
                KpiDefinitionId = kpi.Id,
                IsRequired = isRequired,
                Multiplier = multiplier,
                EffectiveFrom = effectiveFrom,
                EffectiveTo = effectiveTo,
                UpdatedAt = Now,
            });
        }

        private KpiDefinition NewKpiDefinition()
        {
            var group = new KpiGroup { Id = Guid.NewGuid(), Code = "G", Name = "Group", Status = KpiStatus.Active, UpdatedAt = Now };
            var kpi = new KpiDefinition
            {
                Id = Guid.NewGuid(),
                KpiGroupId = group.Id,
                Code = "KPI",
                Name = "KPI",
                Unit = "unit",
                AggregationType = KpiAggregationType.Sum,
                Direction = KpiDirection.HigherBetter,
                Status = KpiStatus.Active,
                UpdatedAt = Now,
            };
            Db.KpiGroups.Add(group);
            Db.KpiDefinitions.Add(kpi);
            return kpi;
        }

        public EvaluationPeriod AddPeriod(string code, DateOnly start, DateOnly end, PeriodStatus status = PeriodStatus.Open)
        {
            var period = new EvaluationPeriod
            {
                Id = Guid.NewGuid(),
                Code = $"{code}-{++_periodCounter}",
                Name = code,
                PeriodType = PeriodType.Month,
                StartDate = start,
                EndDate = end,
                Status = status,
                UpdatedAt = Now,
            };
            Db.EvaluationPeriods.Add(period);
            return period;
        }

        public TargetAssignment AddTarget(
            KpiDefinition kpi,
            EvaluationPeriod period,
            TargetAssignmentStatus status = TargetAssignmentStatus.Active,
            Employee? owner = null)
        {
            var target = new TargetAssignment
            {
                Id = Guid.NewGuid(),
                KpiDefinitionId = kpi.Id,
                EmployeeId = (owner ?? Maker).Id,
                EvaluationPeriodId = period.Id,
                Version = 1,
                TargetValue = 100m,
                Status = status,
                CreatedAt = Now,
                UpdatedAt = Now,
            };
            Db.TargetAssignments.Add(target);
            return target;
        }

        public TaskHandover AddAcceptedHandover(Employee from, Employee to, decimal referrerSharePercent)
        {
            var handover = new TaskHandover
            {
                Id = Guid.NewGuid(),
                WorkId = Work.Id,
                FromEmployeeId = from.Id,
                ToEmployeeId = to.Id,
                Status = HandoverStatus.Accepted,
                Reason = "Handover",
                ReferrerSharePercent = referrerSharePercent,
                HandoverAt = Now,
                AcceptedAt = Now,
                UpdatedAt = Now,
            };
            Db.TaskHandovers.Add(handover);
            return handover;
        }

        /// <summary>Approving must fail with 409 KPI_TARGET_NOT_ASSIGNED and leave the submission, work and ledger untouched.</summary>
        public async Task AssertApprovalRejectedForMissingTargetAsync(decimal value)
        {
            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => ApproveAsync(value));

            Assert.Equal(ErrorCodes.KpiTargetNotAssigned, ex.ErrorCode);
            Assert.Equal(StatusCodes.Status409Conflict, ex.StatusCode);
            Assert.Empty(await Db.Verifications.ToListAsync());
            Assert.Empty(await Db.VerifiedWorkResults.ToListAsync());
            Assert.Empty(await Db.WorkResultItems.ToListAsync());
            Assert.Empty(await Db.ActualProgresses.ToListAsync());
            Assert.Equal(SubmissionStatus.Submitted, (await Db.ResultSubmissions.SingleAsync()).Status);
            Assert.Equal(WorkStatus.Submitted, (await Db.Works.SingleAsync()).Status);
        }

        public async Task<ResultSubmission> AddSecondWorkAsync()
        {
            var (work, submission) = NewSubmittedWork("WRK-2", Maker, WorkType);
            Db.Works.Add(work);
            Db.ResultSubmissions.Add(submission);
            await Db.SaveChangesAsync();
            return submission;
        }

        public void Dispose() => Db.Dispose();

        private static (Work Work, ResultSubmission Submission) NewSubmittedWork(string code, Employee maker, WorkType? workType)
        {
            var work = new Work
            {
                Id = Guid.NewGuid(),
                Code = code,
                Title = code,
                WorkTypeId = workType?.Id,
                AssigneeEmployeeId = maker.Id,
                CreatedBy = maker.Id,
                Status = WorkStatus.Submitted,
                Priority = WorkPriority.Medium,
                UpdatedAt = Now,
            };
            var submission = new ResultSubmission
            {
                Id = Guid.NewGuid(),
                WorkId = work.Id,
                SubmitterEmployeeId = maker.Id,
                SubmissionNo = 1,
                Status = SubmissionStatus.Submitted,
                SubmittedAt = Now,
                UpdatedAt = Now,
            };
            return (work, submission);
        }

        private static Employee Person(string code, Department department, JobPosition position) => new()
        {
            Id = Guid.NewGuid(),
            EmployeeCode = code,
            FullName = code,
            Email = $"{code.ToLowerInvariant()}@example.test",
            DepartmentId = department.Id,
            JobPositionId = position.Id,
            Status = EmployeeStatus.Active,
            LastSyncAt = Now,
            UpdatedAt = Now,
        };
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
