using G54.BLL.Common;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Workflow;
using G54.BLL.Dtos.SystemParameter;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace G54.UnitTests;

public sealed class EvaluationSettingsServiceTests
{
    private static AppDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static EvaluationSettingsService CreateService(AppDbContext db) => new(db);

    private static TierCriterionDto Criterion(
        string code = "BUSINESS",
        string name = "Điểm kinh doanh",
        string metric = TierCriterionMetrics.BusinessScore,
        decimal threshold = 80m,
        params string[] requiredFor) =>
        new(code, name, metric, threshold, requiredFor.ToDictionary(t => t, _ => true));

    // ---- comparison baseline ------------------------------------------------------------------------

    [Fact]
    public async Task GetComparisonAsync_WithNothingSaved_DefaultsToThePreviousPeriod()
    {
        using var db = CreateDb();

        var result = await CreateService(db).GetComparisonAsync();

        Assert.Equal(EvaluationComparisonBaselines.PreviousPeriod, result.Baseline);
        Assert.Equal(EvaluationComparisonBaselines.MinLookback, result.LookbackPeriods);
    }

    [Fact]
    public async Task UpdateComparisonAsync_SavesAndIsReadBackAtOnce_ThenUpdatesTheSameRow()
    {
        using var db = CreateDb();
        var service = CreateService(db);

        await service.UpdateComparisonAsync(new EvaluationComparisonDto(EvaluationComparisonBaselines.AverageLastN, 6));
        var first = await service.GetComparisonAsync();
        await service.UpdateComparisonAsync(new EvaluationComparisonDto(EvaluationComparisonBaselines.SamePeriodLastYear, 3));
        var second = await service.GetComparisonAsync();

        Assert.Equal((EvaluationComparisonBaselines.AverageLastN, 6), (first.Baseline, first.LookbackPeriods));
        Assert.Equal((EvaluationComparisonBaselines.SamePeriodLastYear, 3), (second.Baseline, second.LookbackPeriods));
        Assert.Equal(1, await db.EvaluationComparisonSettings.CountAsync());
    }

    [Theory]
    [InlineData("NOT_A_BASELINE", 3)]
    [InlineData(EvaluationComparisonBaselines.AverageLastN, 1)]
    [InlineData(EvaluationComparisonBaselines.AverageLastN, 13)]
    public async Task UpdateComparisonAsync_InvalidValues_ThrowValidationAndSaveNothing(string baseline, int lookback)
    {
        using var db = CreateDb();

        await Assert.ThrowsAsync<ValidationException>(
            () => CreateService(db).UpdateComparisonAsync(new EvaluationComparisonDto(baseline, lookback)));

        Assert.Empty(db.EvaluationComparisonSettings);
    }

    [Fact]
    public async Task UpdateComparisonAsync_NullRequest_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(() => CreateService(CreateDb()).UpdateComparisonAsync(null!));

    // ---- tier criteria ------------------------------------------------------------------------------

    [Fact]
    public async Task GetTierCriteriaAsync_ReturnsTheTiersTheMetricsAndNoCriteriaYet()
    {
        using var db = CreateDb();
        DowngradeTestData.Seed(db);

        var matrix = await CreateService(db).GetTierCriteriaAsync();

        Assert.Equal(["A1", "A2", "A3", "B", "C"], matrix.Tiers.Select(t => t.Code));
        Assert.Equal(TierCriterionMetrics.All, matrix.Metrics);
        Assert.Empty(matrix.Criteria);
    }

    [Fact]
    public async Task UpdateTierCriteriaAsync_CreatesCriteriaInOrderWithTheirRequirements()
    {
        using var db = CreateDb();
        DowngradeTestData.Seed(db);
        var service = CreateService(db);

        var matrix = await service.UpdateTierCriteriaAsync(new UpdateTierCriteriaDto(
        [
            Criterion("RISK", "Sự kiện rủi ro", TierCriterionMetrics.OpenRiskEvents, 1m, "A1", "A2"),
            Criterion("BUSINESS", "Điểm kinh doanh", TierCriterionMetrics.BusinessScore, 80m, "A1"),
        ]));

        Assert.Equal(["RISK", "BUSINESS"], matrix.Criteria.Select(c => c.Code));
        Assert.True(matrix.Criteria[0].Required["A2"]);
        Assert.False(matrix.Criteria[0].Required["A3"]);
        Assert.True(matrix.Criteria[1].Required["A1"]);
        Assert.False(matrix.Criteria[1].Required["A2"]);
        Assert.Equal(5, matrix.Criteria[0].Required.Count);
    }

    [Fact]
    public async Task UpdateTierCriteriaAsync_EditsExistingRemovesMissingAndRespectsTheNewOrder()
    {
        using var db = CreateDb();
        DowngradeTestData.Seed(db);
        var service = CreateService(db);
        await service.UpdateTierCriteriaAsync(new UpdateTierCriteriaDto(
        [
            Criterion("A", "Tiêu chí A", threshold: 50m, requiredFor: ["A1"]),
            Criterion("B", "Tiêu chí B", threshold: 60m, requiredFor: ["A1"]),
        ]));

        var matrix = await service.UpdateTierCriteriaAsync(new UpdateTierCriteriaDto(
        [
            Criterion("B", "Tiêu chí B (sửa)", threshold: 75m, requiredFor: ["A3"]),
        ]));

        var only = Assert.Single(matrix.Criteria);
        Assert.Equal("B", only.Code);
        Assert.Equal("Tiêu chí B (sửa)", only.Name);
        Assert.Equal(75m, only.Threshold);
        Assert.True(only.Required["A3"]);
        Assert.False(only.Required["A1"]);
        Assert.Equal(1, await db.TierCriteria.CountAsync());
    }

    [Fact]
    public async Task LoadCriteriaAsync_ReturnsOnlyTheRequiredTiersForTheDeterminer()
    {
        using var db = CreateDb();
        DowngradeTestData.Seed(db);
        await CreateService(db).UpdateTierCriteriaAsync(new UpdateTierCriteriaDto(
        [
            Criterion("BUSINESS", threshold: 80m, requiredFor: ["A1", "A2"]),
        ]));

        var rules = await EvaluationSettingsService.LoadCriteriaAsync(db, CancellationToken.None);

        var rule = Assert.Single(rules);
        Assert.Equal("BUSINESS", rule.Code);
        Assert.Equal(80m, rule.Threshold);
        Assert.Equal(["A1", "A2"], rule.RequiredForTiers.OrderBy(x => x));
    }

    public static TheoryData<string, UpdateTierCriteriaDto> InvalidMatrices => new()
    {
        { "no code", new(new[] { Criterion(code: " ") }) },
        { "code too long", new(new[] { Criterion(code: new string('x', 51)) }) },
        { "duplicate code", new(new[] { Criterion("DUP"), Criterion("DUP") }) },
        { "blank name", new(new[] { Criterion(name: " ") }) },
        { "name too long", new(new[] { Criterion(name: new string('n', 201)) }) },
        { "unknown metric", new(new[] { Criterion(metric: "NO_SUCH") }) },
        { "negative threshold", new(new[] { Criterion(threshold: -1m) }) },
        { "score threshold above 100", new(new[] { Criterion(threshold: 101m) }) },
        { "risk threshold above 1000", new(new[] { Criterion(metric: TierCriterionMetrics.OpenRiskEvents, threshold: 1001m) }) },
        { "unknown tier", new(new[] { Criterion(requiredFor: ["ZZ"]) }) },
        { "too many criteria", new(Enumerable.Range(0, 21).Select(i => Criterion("C" + i)).ToList()) },
    };

    [Theory]
    [MemberData(nameof(InvalidMatrices))]
    public async Task UpdateTierCriteriaAsync_InvalidRequest_ThrowsValidationAndSavesNothing(string reason, UpdateTierCriteriaDto request)
    {
        using var db = CreateDb();
        DowngradeTestData.Seed(db);

        await Assert.ThrowsAsync<ValidationException>(() => CreateService(db).UpdateTierCriteriaAsync(request));

        Assert.True(!await db.TierCriteria.AnyAsync(), reason);
    }

    [Fact]
    public async Task UpdateTierCriteriaAsync_NullCriteriaList_ThrowsValidation()
    {
        using var db = CreateDb();
        DowngradeTestData.Seed(db);

        await Assert.ThrowsAsync<ValidationException>(() => CreateService(db).UpdateTierCriteriaAsync(new UpdateTierCriteriaDto(null!)));
    }

    [Fact]
    public async Task UpdateTierCriteriaAsync_NullRequest_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(() => CreateService(CreateDb()).UpdateTierCriteriaAsync(null!));
}
