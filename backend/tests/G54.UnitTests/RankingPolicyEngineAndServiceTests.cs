using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Workflow;
using G54.BLL.Dtos.SystemParameter;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.UnitTests;

public sealed class RankingPolicyEngineAndServiceTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    public void Dispose() => _db.Dispose();

    private RankingPolicyService CreateService() =>
        new(_db, new PerformanceScoreCalculator(_db, new PerformanceScoreFormula()));

    [Fact]
    public void GB910_10_FormulaEvaluator_EvaluatesConditionalAndMathFunctionsSafely()
    {
        var formula = "IF(DepartmentAvgScore < 70 && CriticalRiskCount == 0, ROUND(TotalScore * 0.8, 2), CLAMP(MIN(TotalScore, QualitativeScore + 10), 0, 100))";
        Assert.Empty(RankingFormulaEvaluator.Validate(formula));

        var penalized = RankingFormulaEvaluator.Evaluate(
            formula,
            new Dictionary<string, decimal>
            {
                ["DepartmentAvgScore"] = 65m,
                ["CriticalRiskCount"] = 0m,
                ["TotalScore"] = 92.5m,
                ["QualitativeScore"] = 85m,
            },
            92.5m);
        Assert.Equal(74.0m, penalized);

        var clamped = RankingFormulaEvaluator.Evaluate(
            formula,
            new Dictionary<string, decimal>
            {
                ["DepartmentAvgScore"] = 82m,
                ["CriticalRiskCount"] = 0m,
                ["TotalScore"] = 98m,
                ["QualitativeScore"] = 84m,
            },
            98m);
        Assert.Equal(94.0m, clamped);
    }

    [Theory]
    [InlineData("")]
    [InlineData("System.IO.File.Delete('x')")]
    [InlineData("UnknownVar + 10")]
    [InlineData("EVAL(TotalScore)")]
    [InlineData("TotalScore >= 80")]
    [InlineData("IF(TotalScore, 10, 20)")]
    [InlineData("(TotalScore + 5")]
    public void GB910_11_FormulaEvaluator_RejectsUnsafeOrMalformedExpressions(string badFormula)
    {
        var errors = RankingFormulaEvaluator.Validate(badFormula);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void GB910_12_RuleEngine_AppliesTieBreakersRiskCapQuotaCutAndProducesExplanationTrace()
    {
        var policy = new RankingPolicyDefinitionDto(
            ScoreFormula: "TotalScore",
            Tiers:
            [
                new("A1", 1, 95m),
                new("A2", 2, 90m),
                new("A3", 3, 85m),
                new("B", 4, 70m),
                new("C", 5, 50m),
                new("D", 6, 0m),
            ],
            TieBreakers:
            [
                "EFFECTIVE_SCORE_DESC",
                "SERVICE_QUALITY_SCORE_DESC",
                "BUSINESS_SCORE_DESC",
                "EMPLOYEE_CODE_ASC",
            ],
            Rules:
            [
                new(
                    Id: "r1",
                    Name: "Hạ trần rủi ro CRITICAL",
                    Enabled: true,
                    StopOnMatch: true,
                    Conditions: new("AND", [new("MAX_RISK_SEVERITY", "GTE", 4m)], []),
                    Actions:
                    [
                        new("CAP_MAX_TIER", TierCode: "C", Message: "Bị khống chế trần hạng C do vi phạm rủi ro nghiêm trọng"),
                        new("SET_COACHING_FLAG", FlagValue: true),
                        new("SET_WARNING_LABEL", Message: "Vi phạm CRITICAL"),
                    ]),
                new(
                    Id: "r2",
                    Name: "Điểm liệt thành phần < 50",
                    Enabled: true,
                    StopOnMatch: false,
                    Conditions: new("AND", [new("MIN_COMPONENT_SCORE", "LT", 50m)], []),
                    Actions: [new("DEMOTE_STEPS", NumericValue: 1m)]),
                new(
                    Id: "r3",
                    Name: "Cắt hạn ngạch Top 25% Tier A",
                    Enabled: true,
                    StopOnMatch: false,
                    Conditions: new("AND", [new("CURRENT_TIER", "IN", Values: ["A1", "A2", "A3"])], []),
                    Actions: [new("ENFORCE_GROUP_QUOTA_PERCENT", TierCode: "B", NumericValue: 25m)]),
            ]);

        Assert.Empty(RankingRuleEngine.ValidateDefinition(policy));

        var emp1 = Guid.NewGuid();
        var emp2 = Guid.NewGuid();
        var emp3 = Guid.NewGuid();
        var emp4 = Guid.NewGuid();

        var roster = new List<RankingSubjectInput>
        {
            // Tied at 92.0 total, but emp2 has higher ServiceQualityScore (96 vs 88) -> emp2 wins rank #1!
            new(emp1, "NV01", "SALES_OFFICER", 92m, BusinessScore: 95m, ServiceQualityScore: 88m, OperationsScore: 90m),
            new(emp2, "NV02", "TELLER", 92m, BusinessScore: 89m, ServiceQualityScore: 96m, OperationsScore: 91m),
            // High score 96.0, but has CRITICAL risk -> capped at C + stopOnMatch!
            new(emp3, "NV03", "SALES_OFFICER", 96m, BusinessScore: 96m, ServiceQualityScore: 96m, OperationsScore: 96m,
                RiskEvents: [new RiskEvent { Severity = RiskEventSeverity.Critical, Status = RiskEventStatus.Confirmed }]),
            // Score 75 (base B), but BusinessScore = 45 (< 50) -> demoted 1 step to C!
            new(emp4, "NV04", "TELLER", 75m, BusinessScore: 45m, ServiceQualityScore: 85m, OperationsScore: 80m),
        };

        var results = RankingRuleEngine.EvaluateRoster(roster, policy);

        // Rank order: emp3 (96) is #1, emp2 (92, Svc 96) is #2, emp1 (92, Svc 88) is #3, emp4 (75) is #4
        // Top 25% of 4 employees = max 1 slot!
        // emp3 is Rank #1, capped at C by r1 (and stopOnMatch).
        // emp2 is Rank #2 -> exceeds max 1 slot of 25% quota -> demoted from A2 to B by r3!
        // emp1 is Rank #3 -> exceeds quota -> demoted from A2 to B by r3!
        // emp4 is Rank #4 -> demoted 1 step from B to C by r2!
        Assert.Equal("C", results[emp3].ProposedTier);
        Assert.True(results[emp3].CoachingFlag);
        Assert.Equal("Vi phạm CRITICAL", results[emp3].WarningLabel);
        Assert.Contains(results[emp3].Explanation, line => line.Contains("trần hạng C"));

        Assert.Equal(2, results[emp2].Rank);
        Assert.Equal(3, results[emp1].Rank);
        Assert.Equal("B", results[emp2].ProposedTier);
        Assert.Equal("B", results[emp1].ProposedTier);
        Assert.Equal("C", results[emp4].ProposedTier);
    }

    [Fact]
    public async Task GB910_13_RankingPolicyService_UpdatesGlobalAndPeriodPolicyAndProtectsLockedSnapshots()
    {
        var service = CreateService();
        var openPeriod = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = "Q4-2026",
            Name = "Quý 4/2026",
            PeriodType = PeriodType.Quarter,
            StartDate = new DateOnly(2026, 10, 1),
            EndDate = new DateOnly(2026, 12, 31),
            Status = PeriodStatus.Open,
        };
        _db.EvaluationPeriods.Add(openPeriod);
        await _db.SaveChangesAsync();

        var initial = await service.GetPolicyAsync();
        Assert.Equal("DEFAULT_POLICY", initial.Code);
        Assert.NotEmpty(initial.Catalog.Metrics);

        // Save global policy update and verify legacy ranking_tier_threshold sync
        var updatedDef = initial.Definition with
        {
            ScoreFormula = "ROUND(TotalScore * 0.95, 2)",
            Tiers =
            [
                new("A1", 1, 93m),
                new("A2", 2, 88m),
                new("B", 3, 70m),
                new("C", 4, 0m),
            ],
            Rules = [],
        };
        var savedGlobal = await service.UpdatePolicyAsync(new UpdateRankingPolicyDto(
            "Chính sách Quý mới",
            "Điều chỉnh điểm sàn A1 xuống 93",
            null,
            updatedDef));

        Assert.Equal("Chính sách Quý mới", savedGlobal.Name);
        Assert.Equal(4, await _db.RankingTierThresholds.CountAsync());
        Assert.Equal(93m, (await _db.RankingTierThresholds.SingleAsync(t => t.TierCode == "A1")).MinScore);

        // Freeze snapshot on period lock, then mutate global policy again -> locked period keeps its frozen policy!
        openPeriod.RankingPolicySnapshotJson = await service.BuildSnapshotJsonAsync(openPeriod);
        openPeriod.Status = PeriodStatus.Locked;
        await _db.SaveChangesAsync();

        await service.UpdatePolicyAsync(new UpdateRankingPolicyDto(
            "Chính sách Năm 2027",
            null,
            null,
            updatedDef with { ScoreFormula = "TotalScore" }));

        var lockedPolicy = await service.GetPolicyAsync(openPeriod.Id);
        Assert.True(lockedPolicy.IsLockedSnapshot);
        Assert.Equal("ROUND(TotalScore * 0.95, 2)", lockedPolicy.Definition.ScoreFormula);

        // Updating a locked period policy is blocked with 409 PERIOD_LOCKED
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.UpdatePolicyAsync(new UpdateRankingPolicyDto(
            "Sửa kỳ khóa",
            null,
            openPeriod.Id,
            updatedDef)));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task GB910_14_SimulateAsync_RunsDryRunPreviewWithoutMutatingDatabase()
    {
        var service = CreateService();
        var basePolicy = await service.GetPolicyAsync();

        var strictDraft = basePolicy.Definition with
        {
            ScoreFormula = "ROUND(TotalScore * 0.9, 2)",
            Rules =
            [
                new(
                    Id: "strict-cap",
                    Name: "Giới hạn mọi nhân sự tối đa A3",
                    Enabled: true,
                    StopOnMatch: false,
                    Conditions: new("AND", [new("TOTAL_SCORE", "GTE", 0m)], []),
                    Actions: [new("CAP_MAX_TIER", TierCode: "A3")]),
            ],
        };

        var simulation = await service.SimulateAsync(new SimulateRankingPolicyRequestDto(null, null, strictDraft));

        Assert.True(simulation.IsSyntheticSample);
        Assert.Equal(8, simulation.TotalEmployees);
        Assert.True(simulation.ChangedCount > 0);
        var topTiers = new[] { "A1", "A2" };
        Assert.All(simulation.Rows, r => Assert.DoesNotContain(r.AfterTier, topTiers));
        Assert.Empty(await _db.RankingPolicies.ToListAsync());
    }
}
