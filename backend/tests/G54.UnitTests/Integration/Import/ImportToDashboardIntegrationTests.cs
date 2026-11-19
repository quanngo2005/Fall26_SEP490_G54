using System.Net;
using System.Text.Json;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using static G54.UnitTests.Integration.Import.XlsxBuilder;

namespace G54.UnitTests.Integration.Import;

/// <summary>
/// GB-682: the main flow end to end over HTTP against PostgreSQL:
/// Import Transaction (<c>POST /transaction-imports</c>) → Reconcile (work transaction codes) → KPI
/// (<c>POST /performance-evaluations/{id}/calculate</c>) → Dashboard (<c>/performance-dashboard/*</c>, ledger and
/// transactions drill-down). Every test builds its own work type, KPI, period tree (own year), profile, target and
/// evaluation, so tests do not share state.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ImportToDashboardIntegrationTests(ImportApiFixture api) : IClassFixture<ImportApiFixture>
{
    private const string Admin = "NV013";
    private const string Assignee = "NV005";
    private const string Referrer = "NV004";
    private const string Manager = "NV002";
    private const decimal TargetValue = 10m;
    private static int _nextYear = 2080;

    private sealed record Scenario(
        Guid WorkId,
        Guid KpiId,
        string KpiCode,
        string Product,
        int Year,
        Guid MonthId,
        IReadOnlyDictionary<string, Guid> EmployeeIds,
        IReadOnlyDictionary<string, Guid> EvaluationIds,
        Guid AssigneeDepartmentId)
    {
        public string Date => $"15/01/{Year}";
    }

    /// <summary>
    /// A work of a new work type (product <c>ITDxxxx</c>, one SUM KPI counting transactions) assigned to
    /// <see cref="Assignee"/>; an ACTIVE target of <see cref="TargetValue"/> for January of a year of its own; an ACTIVE
    /// evaluation profile with that KPI as the only leaf (weight 100) and a DRAFT evaluation of January. With a handover
    /// the referrer gets the same target, profile and evaluation.
    /// </summary>
    private async Task<Scenario> ArrangeAsync(bool withHandover = false)
    {
        var year = Interlocked.Increment(ref _nextYear);
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var product = $"ITD{suffix}";
        var yearId = await api.AddPeriodAsync(PeriodType.Year, new DateOnly(year, 1, 1), new DateOnly(year, 12, 31), PeriodStatus.Open);
        var halfId = await api.AddPeriodAsync(PeriodType.HalfYear, new DateOnly(year, 1, 1), new DateOnly(year, 6, 30), PeriodStatus.Open, yearId);
        var quarterId = await api.AddPeriodAsync(PeriodType.Quarter, new DateOnly(year, 1, 1), new DateOnly(year, 3, 31), PeriodStatus.Open, halfId);
        var monthId = await api.AddPeriodAsync(PeriodType.Month, new DateOnly(year, 1, 1), new DateOnly(year, 1, 31), PeriodStatus.Open, quarterId);

        var workId = Guid.NewGuid();
        var kpiId = Guid.NewGuid();
        var kpiCode = $"ITD{suffix}";
        var credited = withHandover ? new[] { Assignee, Referrer } : new[] { Assignee };
        var evaluationIds = new Dictionary<string, Guid>();
        Dictionary<string, Guid> employeeIds = [];
        Guid departmentId = default;

        await api.WithDbAsync(async db =>
        {
            var employees = await db.Employees
                .Where(e => e.EmployeeCode == Assignee || e.EmployeeCode == Referrer || e.EmployeeCode == "NV001")
                .ToListAsync();
            employeeIds = employees.ToDictionary(e => e.EmployeeCode, e => e.Id);
            departmentId = employees.Single(e => e.EmployeeCode == Assignee).DepartmentId;
            var groupId = await db.KpiGroups.Select(g => g.Id).FirstAsync();
            var now = DateTimeOffset.UtcNow;
            var start = new DateOnly(year, 1, 1);

            db.KpiDefinitions.Add(new KpiDefinition
            {
                Id = kpiId,
                KpiGroupId = groupId,
                Code = kpiCode,
                Name = $"KPI luồng chính {suffix}",
                Unit = "GD",
                AggregationType = KpiAggregationType.Sum,
                Direction = KpiDirection.HigherBetter,
                Status = KpiStatus.Active,
                UpdatedAt = now,
            });
            var workType = new WorkType { Id = Guid.NewGuid(), Code = $"ITD{suffix}", Name = $"Loại việc {suffix}", ProductCode = product, Status = RecordStatus.Active, UpdatedAt = now };
            db.WorkTypes.Add(workType);
            db.WorkTypeKpis.Add(new WorkTypeKpi
            {
                Id = Guid.NewGuid(),
                WorkTypeId = workType.Id,
                KpiDefinitionId = kpiId,
                IsRequired = true,
                Multiplier = 1m,
                ValueSource = KpiValueSource.Count,
                EffectiveFrom = start,
                UpdatedAt = now,
            });

            // One profile per job position (unique on position + effective date).
            var profiles = new Dictionary<Guid, Guid>();
            foreach (var code in credited)
            {
                var employee = employees.Single(e => e.EmployeeCode == code);
                if (!profiles.TryGetValue(employee.JobPositionId, out var profileId))
                {
                    profileId = Guid.NewGuid();
                    profiles[employee.JobPositionId] = profileId;
                    var rootId = Guid.NewGuid();
                    db.EvaluationProfiles.Add(new EvaluationProfile
                    {
                        Id = profileId,
                        JobPositionId = employee.JobPositionId,
                        Code = $"ITD-{suffix}-{profiles.Count}",
                        Name = $"Hồ sơ đánh giá {suffix}",
                        Version = 1,
                        EffectiveFrom = start,
                        Status = EvaluationProfileStatus.Active,
                        UpdatedAt = now,
                    });
                    db.EvaluationProfileItems.AddRange(
                        new EvaluationProfileItem { Id = rootId, EvaluationProfileId = profileId, Name = "Kinh doanh", WeightPercent = 100m, DisplayOrder = 1, UpdatedAt = now },
                        new EvaluationProfileItem { Id = Guid.NewGuid(), EvaluationProfileId = profileId, ParentItemId = rootId, KpiDefinitionId = kpiId, WeightPercent = 100m, DisplayOrder = 1, UpdatedAt = now });
                }

                db.TargetAssignments.Add(new TargetAssignment
                {
                    Id = Guid.NewGuid(),
                    KpiDefinitionId = kpiId,
                    EmployeeId = employee.Id,
                    EvaluationPeriodId = monthId,
                    Version = 1,
                    TargetValue = TargetValue,
                    Status = TargetAssignmentStatus.Active,
                    AssignedAt = now,
                    ApprovedAt = now,
                    EffectiveFrom = start,
                    CreatedAt = now,
                    UpdatedAt = now,
                });

                var evaluationId = Guid.NewGuid();
                evaluationIds[code] = evaluationId;
                db.PerformanceEvaluations.Add(new PerformanceEvaluation
                {
                    Id = evaluationId,
                    EmployeeId = employee.Id,
                    EvaluationPeriodId = monthId,
                    EvaluationProfileId = profileId,
                    Status = PerformanceEvaluationStatus.Draft,
                    UpdatedAt = now,
                });
            }

            db.Works.Add(new Work
            {
                Id = workId,
                WorkTypeId = workType.Id,
                AssigneeEmployeeId = employeeIds[Assignee],
                CreatedBy = employeeIds["NV001"],
                Code = $"W-ITD-{suffix}",
                Title = "Mở thẻ cho khách hàng",
                Priority = WorkPriority.Medium,
                Status = WorkStatus.InProgress,
                PlannedStartDate = start,
                PlannedEndDate = new DateOnly(year, 1, 31),
                UpdatedAt = now,
            });

            if (withHandover)
            {
                db.TaskHandovers.Add(new TaskHandover
                {
                    Id = Guid.NewGuid(),
                    WorkId = workId,
                    FromEmployeeId = employeeIds[Referrer],
                    ToEmployeeId = employeeIds[Assignee],
                    Reason = "Khách hàng do giao dịch viên giới thiệu",
                    Status = HandoverStatus.Accepted,
                    HandoverAt = now,
                    AcceptedAt = now,
                    UpdatedAt = now,
                });
            }

            await db.SaveChangesAsync();
        });

        return new Scenario(workId, kpiId, kpiCode, product, year, monthId, employeeIds, evaluationIds, departmentId);
    }

    // ---- steps ---------------------------------------------------------------------------------------

    private static string NewCode() => $"DB{Guid.NewGuid():N}"[..14].ToUpperInvariant();

    private Task<ApiResult> AddCodesAsync(Scenario s, params string[] codes) =>
        api.SendAsync(HttpMethod.Post, $"/works/{s.WorkId}/transactions", Assignee, new { transactionCodes = codes });

    private Task<ApiResult> ImportAsync(string fileName, params object?[][] rows) =>
        api.PostFileAsync("/transaction-imports", Transactions(rows), Admin, fileName);

    private async Task<JsonElement> CalculateAsync(Scenario s, string employeeCode, string who = Admin)
    {
        var result = await api.SendAsync(HttpMethod.Post, $"/performance-evaluations/{s.EvaluationIds[employeeCode]}/calculate", who);
        api.AssertStatus(result, HttpStatusCode.OK);
        return result.Data;
    }

    private async Task<JsonElement> GetAsync(string url, string who)
    {
        var result = await api.SendAsync(HttpMethod.Get, url, who);
        api.AssertStatus(result, HttpStatusCode.OK);
        return result.Data;
    }

    private static JsonElement SingleKpi(JsonElement withGroups) =>
        Assert.Single(Assert.Single(withGroups.GetProperty("groups").EnumerateArray()).GetProperty("kpis").EnumerateArray());

    private static decimal Dec(JsonElement element, string name) => element.GetProperty(name).GetDecimal();

    // ---- tests ---------------------------------------------------------------------------------------

    // D1: the whole chain for one matched transaction: 1 of 10 → 10 %.
    [ImportIntegrationFact]
    public async Task D1_ImportedTransaction_FlowsThroughReconcileAndKpiToTheDashboard()
    {
        var s = await ArrangeAsync();
        var code = NewCode();
        var fileName = $"giao_dich_{s.Year}.xlsx";

        // 1. Work transaction code entered by the assignee: waits for Core Banking.
        var added = await AddCodesAsync(s, code);
        api.AssertStatus(added, HttpStatusCode.OK);
        Assert.Equal("IN_REVIEW", Assert.Single(added.Data.GetProperty("items").EnumerateArray()).GetProperty("status").GetString());

        // 2. Import Transaction + 3. Reconcile.
        var import = await ImportAsync(fileName, Tx(code, s.Date, Assignee, s.Product, 2_500_000m));
        api.AssertStatus(import, HttpStatusCode.Created);
        Assert.Equal(1, import.Data.GetProperty("reconcile").GetProperty("matched").GetInt32());

        // The home progress card reads the verified progress directly (no calculation needed).
        var card = await GetAsync($"/my-dashboard/progress-card?periodId={s.MonthId}", Assignee);
        Assert.Equal(s.MonthId, card.GetProperty("period").GetProperty("id").GetGuid());
        Assert.Equal(10m, Dec(card, "currentRate"));

        // 4. KPI: the calculation reads the verified progress written by the reconcile. The employee cannot calculate;
        // the department manager can (evaluation:calculate, scope Department, seeded by GrantEvaluationCalculate).
        var denied = await api.SendAsync(HttpMethod.Post, $"/performance-evaluations/{s.EvaluationIds[Assignee]}/calculate", Assignee);
        api.AssertStatus(denied, HttpStatusCode.Forbidden);
        var evaluation = await CalculateAsync(s, Assignee, Manager);
        Assert.Equal("CALCULATED", evaluation.GetProperty("status").GetString());
        Assert.Equal(10m, Dec(evaluation, "totalScore"));
        var calculated = SingleKpi(evaluation);
        Assert.Equal(s.KpiId, calculated.GetProperty("kpiDefinitionId").GetGuid());
        Assert.Equal(TargetValue, Dec(calculated, "targetValue"));
        Assert.Equal(1m, Dec(calculated, "actualValue"));
        Assert.Equal(10m, Dec(calculated, "achievementPercentRaw"));

        // 5. Dashboard: the employee's own view.
        var mine = await GetAsync($"/performance-dashboard/me?periodId={s.MonthId}", Assignee);
        Assert.Equal(s.EmployeeIds[Assignee], mine.GetProperty("employee").GetProperty("id").GetGuid());
        Assert.Equal(s.MonthId, mine.GetProperty("period").GetProperty("id").GetGuid());
        Assert.Equal("CALCULATED", mine.GetProperty("evaluationStatus").GetString());
        Assert.Equal(10m, Dec(mine, "totalScore"));
        var kpi = SingleKpi(mine);
        Assert.Equal(s.KpiCode, kpi.GetProperty("code").GetString());
        Assert.Equal(TargetValue, Dec(kpi, "targetValue"));
        Assert.Equal(1m, Dec(kpi, "actualValue"));
        Assert.Equal(10m, Dec(kpi, "achievementPercentCapped"));

        // ... the department view (manager drill-down).
        var department = await GetAsync($"/performance-dashboard/departments/{s.AssigneeDepartmentId}?periodId={s.MonthId}", Admin);
        var unitKpi = department.GetProperty("kpis").EnumerateArray().Single(k => k.GetProperty("code").GetString() == s.KpiCode);
        Assert.Equal(1, unitKpi.GetProperty("employeeCount").GetInt32());
        Assert.Equal(10m, Dec(unitKpi, "averageAchievementPercent"));
        var member = department.GetProperty("members").EnumerateArray().Single(m => m.GetProperty("employeeCode").GetString() == Assignee);
        Assert.Equal(10m, Dec(member, "totalScore"));

        // ... the ledger line behind the score and the imported transaction behind the line.
        var ledger = await GetAsync($"/performance-dashboard/employees/{s.EmployeeIds[Assignee]}/ledger?periodId={s.MonthId}&kpiId={s.KpiId}", Assignee);
        Assert.Equal(1m, Dec(ledger.GetProperty("summary"), "resultPoints"));
        var line = Assert.Single(ledger.GetProperty("entries").GetProperty("items").EnumerateArray());
        Assert.Equal("RESULT", line.GetProperty("type").GetString());
        Assert.Equal(1m, Dec(line, "points"));
        Assert.Equal(s.WorkId, line.GetProperty("workId").GetGuid());

        var transactions = await GetAsync($"/performance-dashboard/ledger-entries/{line.GetProperty("id").GetGuid()}/transactions", Assignee);
        var transaction = Assert.Single(transactions.GetProperty("items").EnumerateArray());
        Assert.Equal(code, transaction.GetProperty("transactionCode").GetString());
        Assert.Equal("MATCHED", transaction.GetProperty("status").GetString());
        Assert.Equal(s.Product, transaction.GetProperty("productCode").GetString());
        Assert.Equal(2_500_000m, Dec(transaction, "amount"));
        Assert.Equal(fileName, transaction.GetProperty("importFileName").GetString());
    }

    // D2: a later import raises the score only after the next calculation (the dashboard never recalculates).
    [ImportIntegrationFact]
    public async Task D2_LaterImport_RaisesTheDashboardAfterRecalculation()
    {
        var s = await ArrangeAsync();
        var first = Enumerable.Range(0, 4).Select(_ => NewCode()).ToArray();
        var second = Enumerable.Range(0, 6).Select(_ => NewCode()).ToArray();
        api.AssertStatus(await AddCodesAsync(s, [.. first, .. second]), HttpStatusCode.OK);

        api.AssertStatus(await ImportAsync("dot_1.xlsx", first.Select(c => Tx(c, s.Date, Assignee, s.Product, 1m)).ToArray()), HttpStatusCode.Created);
        await CalculateAsync(s, Assignee);
        Assert.Equal(40m, Dec(await GetAsync($"/performance-dashboard/me?periodId={s.MonthId}", Assignee), "totalScore"));

        var import = await ImportAsync("dot_2.xlsx", second.Select(c => Tx(c, s.Date, Assignee, s.Product, 1m)).ToArray());
        api.AssertStatus(import, HttpStatusCode.Created);
        Assert.Equal(6, import.Data.GetProperty("reconcile").GetProperty("matched").GetInt32());
        // Still the stored score until the KPI is calculated again.
        Assert.Equal(40m, Dec(await GetAsync($"/performance-dashboard/me?periodId={s.MonthId}", Assignee), "totalScore"));

        await CalculateAsync(s, Assignee);
        var mine = await GetAsync($"/performance-dashboard/me?periodId={s.MonthId}", Assignee);
        Assert.Equal(100m, Dec(mine, "totalScore"));
        Assert.Equal(10m, Dec(SingleKpi(mine), "actualValue"));

        var ledger = await GetAsync($"/performance-dashboard/employees/{s.EmployeeIds[Assignee]}/ledger?periodId={s.MonthId}&kpiId={s.KpiId}", Assignee);
        Assert.Equal(10m, Dec(ledger.GetProperty("summary"), "resultPoints"));
        Assert.Equal(10, ledger.GetProperty("entries").GetProperty("totalCount").GetInt32());
    }

    // D3: a code that does not match (wrong employee) never reaches the KPI or the dashboard.
    [ImportIntegrationFact]
    public async Task D3_UnmatchedTransaction_LeavesTheKpiEmpty()
    {
        var s = await ArrangeAsync();
        var code = NewCode();
        api.AssertStatus(await AddCodesAsync(s, code), HttpStatusCode.OK);

        var import = await ImportAsync("sai_nhan_vien.xlsx", Tx(code, s.Date, "NV007", s.Product, 1m));
        api.AssertStatus(import, HttpStatusCode.Created);
        Assert.Equal(0, import.Data.GetProperty("reconcile").GetProperty("matched").GetInt32());

        var evaluation = await CalculateAsync(s, Assignee);
        Assert.Equal(0m, Dec(evaluation, "totalScore"));
        Assert.Empty(Assert.Single(evaluation.GetProperty("groups").EnumerateArray()).GetProperty("kpis").EnumerateArray());

        var mine = await GetAsync($"/performance-dashboard/me?periodId={s.MonthId}", Assignee);
        Assert.Equal(0m, Dec(mine, "totalScore"));
        Assert.Empty(Assert.Single(mine.GetProperty("groups").EnumerateArray()).GetProperty("kpis").EnumerateArray());
        var ledger = await GetAsync($"/performance-dashboard/employees/{s.EmployeeIds[Assignee]}/ledger?periodId={s.MonthId}&kpiId={s.KpiId}", Assignee);
        Assert.Equal(0, ledger.GetProperty("entries").GetProperty("totalCount").GetInt32());
    }

    // D4: a handed-over work splits the credit 30/70 all the way to both employees' dashboards.
    [ImportIntegrationFact]
    public async Task D4_HandedOverWork_ShowsTheSplitOnBothDashboards()
    {
        var s = await ArrangeAsync(withHandover: true);
        var code = NewCode();
        api.AssertStatus(await AddCodesAsync(s, code), HttpStatusCode.OK);
        api.AssertStatus(await ImportAsync("ban_giao.xlsx", Tx(code, s.Date, Assignee, s.Product, 1m)), HttpStatusCode.Created);

        await CalculateAsync(s, Assignee);
        await CalculateAsync(s, Referrer);

        var assignee = await GetAsync($"/performance-dashboard/me?periodId={s.MonthId}", Assignee);
        Assert.Equal(0.7m, Dec(SingleKpi(assignee), "actualValue"));
        Assert.Equal(7m, Dec(assignee, "totalScore"));

        var referrer = await GetAsync($"/performance-dashboard/me?periodId={s.MonthId}", Referrer);
        Assert.Equal(0.3m, Dec(SingleKpi(referrer), "actualValue"));
        Assert.Equal(3m, Dec(referrer, "totalScore"));

        var referrerLedger = await GetAsync($"/performance-dashboard/employees/{s.EmployeeIds[Referrer]}/ledger?periodId={s.MonthId}&kpiId={s.KpiId}", Referrer);
        var line = Assert.Single(referrerLedger.GetProperty("entries").GetProperty("items").EnumerateArray());
        Assert.Equal(0.3m, Dec(line, "points"));
        var transactions = await GetAsync($"/performance-dashboard/ledger-entries/{line.GetProperty("id").GetGuid()}/transactions", Referrer);
        Assert.Equal(code, Assert.Single(transactions.GetProperty("items").EnumerateArray()).GetProperty("transactionCode").GetString());
    }
}
