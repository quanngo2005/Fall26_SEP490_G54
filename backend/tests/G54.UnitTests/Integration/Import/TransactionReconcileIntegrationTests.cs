using System.Net;
using System.Text.Json;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using static G54.UnitTests.Integration.Import.XlsxBuilder;

namespace G54.UnitTests.Integration.Import;

/// <summary>
/// Reconcile of work transaction codes with imported Core Banking rows, end to end over HTTP against PostgreSQL:
/// the assignee adds codes (<c>POST /works/{id}/transactions</c>), an admin imports a file (<c>POST /transaction-imports</c>).
/// Plan: docs/plans/IMPORT_INTEGRATION_TEST_PLAN.md section C. Every test builds its own work type (own product code),
/// KPI, period tree (own year) and target, so tests do not share state.
/// </summary>
[Trait("Category", "Integration")]
public sealed class TransactionReconcileIntegrationTests(ImportApiFixture api) : IClassFixture<ImportApiFixture>
{
    private const string Admin = "NV013";
    private const string Assignee = "NV005";
    private const string Referrer = "NV004";
    private const string Other = "NV007";
    private static int _nextYear = 2060;

    private sealed record Scenario(Guid WorkId, string Product, int Year, Guid MonthId, Guid AssigneeTargetId, Guid? ReferrerTargetId)
    {
        public string Date => $"15/01/{Year}";
    }

    /// <summary>
    /// A work of a new work type (product <c>ITPxxxx</c>, one COUNT KPI) assigned to <see cref="Assignee"/>, and an
    /// ACTIVE target for January of a year of its own (YEAR > HALF_YEAR > QUARTER > MONTH, all OPEN).
    /// </summary>
    private async Task<Scenario> ArrangeAsync(
        bool withHandover = false, bool? referrerHasTarget = null, string? customerCif = null, DateTimeOffset? handoverAt = null)
    {
        var year = Interlocked.Increment(ref _nextYear);
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var product = $"ITP{suffix}";
        var yearId = await api.AddPeriodAsync(PeriodType.Year, new DateOnly(year, 1, 1), new DateOnly(year, 12, 31), PeriodStatus.Open);
        var halfId = await api.AddPeriodAsync(PeriodType.HalfYear, new DateOnly(year, 1, 1), new DateOnly(year, 6, 30), PeriodStatus.Open, yearId);
        var quarterId = await api.AddPeriodAsync(PeriodType.Quarter, new DateOnly(year, 1, 1), new DateOnly(year, 3, 31), PeriodStatus.Open, halfId);
        var monthId = await api.AddPeriodAsync(PeriodType.Month, new DateOnly(year, 1, 1), new DateOnly(year, 1, 31), PeriodStatus.Open, quarterId);

        var workId = Guid.NewGuid();
        var assigneeTarget = Guid.NewGuid();
        Guid? referrerTarget = referrerHasTarget ?? withHandover ? Guid.NewGuid() : null;
        await api.WithDbAsync(async db =>
        {
            var employees = await db.Employees.Where(e => e.EmployeeCode == Assignee || e.EmployeeCode == Referrer || e.EmployeeCode == "NV001")
                .ToDictionaryAsync(e => e.EmployeeCode, e => e.Id);
            var groupId = await db.KpiGroups.Select(g => g.Id).FirstAsync();
            var now = DateTimeOffset.UtcNow;

            var kpi = new KpiDefinition
            {
                Id = Guid.NewGuid(),
                KpiGroupId = groupId,
                Code = $"ITK{suffix}",
                Name = $"KPI tích hợp {suffix}",
                Unit = "GD",
                AggregationType = KpiAggregationType.Sum,
                Direction = KpiDirection.HigherBetter,
                Status = KpiStatus.Active,
                UpdatedAt = now,
            };
            var workType = new WorkType { Id = Guid.NewGuid(), Code = $"ITW{suffix}", Name = $"Loại việc {suffix}", ProductCode = product, Status = RecordStatus.Active, UpdatedAt = now };
            db.KpiDefinitions.Add(kpi);
            db.WorkTypes.Add(workType);
            db.WorkTypeKpis.Add(new WorkTypeKpi
            {
                Id = Guid.NewGuid(),
                WorkTypeId = workType.Id,
                KpiDefinitionId = kpi.Id,
                IsRequired = true,
                Multiplier = 1m,
                ValueSource = KpiValueSource.Count,
                // Effective only from the transaction's year (after today): the DB trigger must check the mapping on the
                // progress (transaction) date like VerifiedResultRecorder, not on the verification date.
                EffectiveFrom = new DateOnly(year, 1, 1),
                UpdatedAt = now,
            });

            TargetAssignment Target(Guid id, Guid employeeId) => new()
            {
                Id = id,
                KpiDefinitionId = kpi.Id,
                EmployeeId = employeeId,
                EvaluationPeriodId = monthId,
                Version = 1,
                TargetValue = 10m,
                Status = TargetAssignmentStatus.Active,
                AssignedAt = now,
                ApprovedAt = now,
                EffectiveFrom = new DateOnly(year, 1, 1),
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.TargetAssignments.Add(Target(assigneeTarget, employees[Assignee]));
            if (referrerTarget is { } referrerTargetId)
            {
                db.TargetAssignments.Add(Target(referrerTargetId, employees[Referrer]));
            }

            db.Works.Add(new Work
            {
                Id = workId,
                WorkTypeId = workType.Id,
                AssigneeEmployeeId = employees[Assignee],
                CreatedBy = employees["NV001"],
                Code = $"W-IT-{suffix}",
                Title = "Mở thẻ cho khách hàng",
                CustomerCif = customerCif,
                Priority = WorkPriority.Medium,
                Status = WorkStatus.InProgress,
                PlannedStartDate = new DateOnly(year, 1, 1),
                PlannedEndDate = new DateOnly(year, 1, 31),
                UpdatedAt = now,
            });

            if (withHandover)
            {
                db.TaskHandovers.Add(new TaskHandover
                {
                    Id = Guid.NewGuid(),
                    WorkId = workId,
                    FromEmployeeId = employees[Referrer],
                    ToEmployeeId = employees[Assignee],
                    Reason = "Khách hàng do giao dịch viên giới thiệu",
                    Status = HandoverStatus.Accepted,
                    HandoverAt = handoverAt?.ToUniversalTime() ?? now,
                    AcceptedAt = handoverAt?.ToUniversalTime() ?? now,
                    UpdatedAt = now,
                });
            }

            await db.SaveChangesAsync();
        });

        return new Scenario(workId, product, year, monthId, assigneeTarget, referrerTarget);
    }

    private Task<ApiResult> AddCodesAsync(Scenario s, params string[] codes) =>
        api.SendAsync(HttpMethod.Post, $"/works/{s.WorkId}/transactions", Assignee, new { transactionCodes = codes });

    private Task<ApiResult> ImportAsync(params object?[][] rows) => api.PostFileAsync("/transaction-imports", Transactions(rows), Admin);

    private static string NewCode() => $"RC{Guid.NewGuid():N}"[..14].ToUpperInvariant();

    private Task<WorkStatus> WorkStatusAsync(Scenario s) => api.QueryAsync(db => db.Works.Where(w => w.Id == s.WorkId).Select(w => w.Status).SingleAsync());

    private Task<decimal> ProgressAsync(Guid targetId) =>
        api.QueryAsync(db => db.ActualProgresses.Where(p => p.TargetAssignmentId == targetId).SumAsync(p => p.Value));

    private Task<List<WorkTransaction>> CodesAsync(Scenario s) =>
        api.QueryAsync(db => db.WorkTransactions.AsNoTracking().Where(w => w.WorkId == s.WorkId).OrderBy(w => w.TransactionCode).ToListAsync());

    // C1
    [ImportIntegrationFact]
    public async Task C1_CodeAddedBeforeImport_IsMatchedByTheImportAndTheWorkIsDone()
    {
        var s = await ArrangeAsync();
        var code = NewCode();
        var added = await AddCodesAsync(s, code);
        api.AssertStatus(added, HttpStatusCode.OK);
        Assert.Equal("IN_REVIEW", Assert.Single(added.Data.GetProperty("items").EnumerateArray()).GetProperty("status").GetString());

        var import = await ImportAsync(Tx(code, s.Date, Assignee, s.Product, 1000m));

        api.AssertStatus(import, HttpStatusCode.Created);
        Assert.Equal(1, import.Data.GetProperty("reconcile").GetProperty("matched").GetInt32());
        Assert.Equal(WorkStatus.Done, await WorkStatusAsync(s));
        Assert.Equal(1m, await ProgressAsync(s.AssigneeTargetId));
        var stored = Assert.Single(await CodesAsync(s));
        Assert.Equal(WorkTransactionStatus.Matched, stored.Status);
        Assert.NotNull(stored.ImportedTransactionId);
    }

    // C2
    [ImportIntegrationFact]
    public async Task C2_CodeAddedAfterImport_IsMatchedImmediately()
    {
        var s = await ArrangeAsync();
        var code = NewCode();
        api.AssertStatus(await ImportAsync(Tx(code, s.Date, Assignee, s.Product, 1m)), HttpStatusCode.Created);

        var added = await AddCodesAsync(s, code);

        api.AssertStatus(added, HttpStatusCode.OK);
        Assert.Equal("MATCHED", Assert.Single(added.Data.GetProperty("items").EnumerateArray()).GetProperty("status").GetString());
        Assert.Equal("DONE", added.Data.GetProperty("workStatus").GetString());
    }

    // C3
    [ImportIntegrationFact]
    public async Task C3_WrongEmployeeOrProduct_IsNotMatchedAndTheWorkStaysSubmitted()
    {
        var s = await ArrangeAsync();
        var otherEmployee = NewCode();
        var otherProduct = NewCode();
        api.AssertStatus(await ImportAsync(
            Tx(otherEmployee, s.Date, Other, s.Product, 1m),
            Tx(otherProduct, s.Date, Assignee, "DEPOSIT", 1m)), HttpStatusCode.Created);

        var added = await AddCodesAsync(s, otherEmployee, otherProduct);

        api.AssertStatus(added, HttpStatusCode.OK);
        var items = added.Data.GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("transactionCode").GetString()!);
        Assert.Equal("EMPLOYEE_MISMATCH", items[otherEmployee].GetProperty("reason").GetString());
        Assert.Equal("PRODUCT_MISMATCH", items[otherProduct].GetProperty("reason").GetString());
        Assert.All(items.Values, i => Assert.Equal("NOT_MATCHED", i.GetProperty("status").GetString()));
        Assert.Equal(WorkStatus.Submitted, await WorkStatusAsync(s));
        Assert.Equal(0m, await ProgressAsync(s.AssigneeTargetId));
    }

    // C4: codes are stored upper-cased and compared with upper() on PostgreSQL
    [ImportIntegrationFact]
    public async Task C4_CodeInAnotherCase_MatchesAndCannotBeAddedTwice()
    {
        var s = await ArrangeAsync();
        var code = NewCode();
        api.AssertStatus(await AddCodesAsync(s, code.ToLowerInvariant()), HttpStatusCode.OK);

        var import = await ImportAsync(Tx(code, s.Date, Assignee, s.Product, 1m));

        api.AssertStatus(import, HttpStatusCode.Created);
        var stored = Assert.Single(await CodesAsync(s));
        Assert.Equal(code, stored.TransactionCode);
        Assert.Equal(WorkTransactionStatus.Matched, stored.Status);

        var again = await AddCodesAsync(await ArrangeAsync(), code);
        api.AssertStatus(again, HttpStatusCode.Conflict);
        Assert.Equal("WORK_TRANSACTION_DUPLICATE", again.ErrorCode);
    }

    // C5
    [ImportIntegrationFact]
    public async Task C5_CodeBlockedByALockedPeriod_IsRetriedByALaterUnrelatedImport()
    {
        var s = await ArrangeAsync();
        var code = NewCode();
        api.AssertStatus(await ImportAsync(Tx(code, s.Date, Assignee, s.Product, 1m)), HttpStatusCode.Created);
        // Lock the month only after the row is imported, so the import accepts it and the credit is what is blocked.
        await api.SetPeriodStatusAsync(s.MonthId, PeriodStatus.Locked);

        var added = await AddCodesAsync(s, code);
        var item = Assert.Single(added.Data.GetProperty("items").EnumerateArray());
        Assert.Equal("IN_REVIEW", item.GetProperty("status").GetString());
        Assert.Equal("PERIOD_LOCKED", item.GetProperty("reason").GetString());

        await api.SetPeriodStatusAsync(s.MonthId, PeriodStatus.Open);
        var unrelated = await ImportAsync(Tx(NewCode(), "05/10/2026", "NV001", "DEPOSIT", 1m));

        api.AssertStatus(unrelated, HttpStatusCode.Created);
        Assert.True(unrelated.Data.GetProperty("reconcile").GetProperty("matched").GetInt32() >= 1);
        Assert.Equal(WorkTransactionStatus.Matched, Assert.Single(await CodesAsync(s)).Status);
        Assert.Equal(WorkStatus.Done, await WorkStatusAsync(s));
        Assert.Equal(1m, await ProgressAsync(s.AssigneeTargetId));
    }

    // C6
    [ImportIntegrationFact]
    public async Task C6_ReimportAndRepeatedReconcile_CreditOnlyOnce()
    {
        var s = await ArrangeAsync();
        var code = NewCode();
        api.AssertStatus(await AddCodesAsync(s, code), HttpStatusCode.OK);
        var row = Tx(code, s.Date, Assignee, s.Product, 1m);

        api.AssertStatus(await ImportAsync(row), HttpStatusCode.Created);
        api.AssertStatus(await ImportAsync(row), HttpStatusCode.Created);
        api.AssertStatus(await ImportAsync(Tx(NewCode(), "05/10/2026", "NV001", "DEPOSIT", 1m)), HttpStatusCode.Created);

        Assert.Equal(1m, await ProgressAsync(s.AssigneeTargetId));
        Assert.Equal(1, await api.QueryAsync(db => db.WorkResultItems.CountAsync(i => i.ActualProgress.TargetAssignmentId == s.AssigneeTargetId)));
    }

    // C8
    [ImportIntegrationFact]
    public async Task C8_HandedOverWork_SplitsTheCredit30To70()
    {
        var s = await ArrangeAsync(withHandover: true);
        var code = NewCode();
        api.AssertStatus(await AddCodesAsync(s, code), HttpStatusCode.OK);

        api.AssertStatus(await ImportAsync(Tx(code, s.Date, Assignee, s.Product, 1m)), HttpStatusCode.Created);

        Assert.Equal(0.3m, await ProgressAsync(s.ReferrerTargetId!.Value));
        Assert.Equal(0.7m, await ProgressAsync(s.AssigneeTargetId));
    }

    // C9 (BR-29): an open month under a locked quarter or year takes no new KPI data, like the import; the code stays
    // IN_REVIEW and is credited by a later import once the period is reopened.
    [ImportIntegrationTheory]
    [InlineData(PeriodType.Quarter)]
    [InlineData(PeriodType.Year)]
    public async Task C9_LockedQuarterOrYear_KeepsTheCodeInReviewUntilReopened(PeriodType lockedType)
    {
        var s = await ArrangeAsync();
        var code = NewCode();
        api.AssertStatus(await ImportAsync(Tx(code, s.Date, Assignee, s.Product, 1m)), HttpStatusCode.Created);
        var lockedId = await api.QueryAsync(db => db.EvaluationPeriods
            .Where(p => p.PeriodType == lockedType && p.StartDate == new DateOnly(s.Year, 1, 1)).Select(p => p.Id).SingleAsync());
        await api.SetPeriodStatusAsync(lockedId, PeriodStatus.Locked);

        var item = SingleItem(await AddCodesAsync(s, code));

        Assert.Equal("IN_REVIEW", item.GetProperty("status").GetString());
        Assert.Equal("PERIOD_LOCKED", item.GetProperty("reason").GetString());
        Assert.Equal(0m, await ProgressAsync(s.AssigneeTargetId));

        await api.SetPeriodStatusAsync(lockedId, PeriodStatus.Open);
        api.AssertStatus(await ImportAsync(Tx(NewCode(), "05/10/2026", "NV001", "DEPOSIT", 1m)), HttpStatusCode.Created);
        Assert.Equal(WorkTransactionStatus.Matched, Assert.Single(await CodesAsync(s)).Status);
        Assert.Equal(1m, await ProgressAsync(s.AssigneeTargetId));
    }

    private static JsonElement SingleItem(ApiResult added) => Assert.Single(added.Data.GetProperty("items").EnumerateArray());

    // C10: the work's customer (CIF) must be the transaction's customer; compared ignoring case and spaces.
    [ImportIntegrationFact]
    public async Task C10_CustomerOfTheWork_MustMatchTheTransactionCustomer()
    {
        var other = await ArrangeAsync(customerCif: "KH999");
        var otherCode = NewCode();
        api.AssertStatus(await ImportAsync(Tx(otherCode, other.Date, Assignee, other.Product, 1m, customer: "KH1")), HttpStatusCode.Created);
        var rejected = SingleItem(await AddCodesAsync(other, otherCode));
        Assert.Equal("NOT_MATCHED", rejected.GetProperty("status").GetString());
        Assert.Equal("CUSTOMER_MISMATCH", rejected.GetProperty("reason").GetString());
        Assert.Equal(0m, await ProgressAsync(other.AssigneeTargetId));

        var same = await ArrangeAsync(customerCif: " kh1 ");
        var sameCode = NewCode();
        api.AssertStatus(await ImportAsync(Tx(sameCode, same.Date, Assignee, same.Product, 1m, customer: "KH1")), HttpStatusCode.Created);
        Assert.Equal("MATCHED", SingleItem(await AddCodesAsync(same, sameCode)).GetProperty("status").GetString());
        Assert.Equal(1m, await ProgressAsync(same.AssigneeTargetId));
    }

    // C11: a handed-over work only takes transactions made on or after the handover day (UTC+7).
    [ImportIntegrationFact]
    public async Task C11_TransactionBeforeTheHandover_IsNotMatched()
    {
        var late = await ArrangeAsync(withHandover: true, handoverAt: new DateTimeOffset(_nextYear + 1, 1, 20, 9, 0, 0, TimeSpan.FromHours(7)));
        var code = NewCode();
        api.AssertStatus(await ImportAsync(Tx(code, late.Date, Assignee, late.Product, 1m)), HttpStatusCode.Created);
        var item = SingleItem(await AddCodesAsync(late, code));
        Assert.Equal("NOT_MATCHED", item.GetProperty("status").GetString());
        Assert.Equal("TRANSACTION_BEFORE_HANDOVER", item.GetProperty("reason").GetString());
        Assert.Equal(0m, await ProgressAsync(late.ReferrerTargetId!.Value));
        Assert.Equal(0m, await ProgressAsync(late.AssigneeTargetId));

        // Same calendar day in Vietnam (00:30 on the 15th = 17:30 UTC on the 14th) is accepted.
        var sameDay = await ArrangeAsync(withHandover: true, handoverAt: new DateTimeOffset(_nextYear + 1, 1, 15, 0, 30, 0, TimeSpan.FromHours(7)));
        var sameDayCode = NewCode();
        api.AssertStatus(await ImportAsync(Tx(sameDayCode, sameDay.Date, Assignee, sameDay.Product, 1m)), HttpStatusCode.Created);
        Assert.Equal("MATCHED", SingleItem(await AddCodesAsync(sameDay, sameDayCode)).GetProperty("status").GetString());
        Assert.Equal(0.7m, await ProgressAsync(sameDay.AssigneeTargetId));
    }

    // C12: without a handover in the system, the file's Refer ID splits the credit with the default 30/70.
    [ImportIntegrationFact]
    public async Task C12_ReferIdWithoutHandover_SplitsTheCredit30To70()
    {
        var s = await ArrangeAsync(referrerHasTarget: true);
        var code = NewCode();
        api.AssertStatus(await AddCodesAsync(s, code), HttpStatusCode.OK);

        api.AssertStatus(await ImportAsync(Tx(code, s.Date, Assignee, s.Product, 1m, refer: Referrer)), HttpStatusCode.Created);

        Assert.Equal(WorkTransactionStatus.Matched, Assert.Single(await CodesAsync(s)).Status);
        Assert.Equal(0.3m, await ProgressAsync(s.ReferrerTargetId!.Value));
        Assert.Equal(0.7m, await ProgressAsync(s.AssigneeTargetId));
    }

    // C13: a Refer ID that names someone else than the handover's referrer is not matched; the same referrer is fine.
    [ImportIntegrationFact]
    public async Task C13_ReferIdDifferentFromTheHandover_IsNotMatched()
    {
        var s = await ArrangeAsync(withHandover: true);
        var code = NewCode();
        api.AssertStatus(await ImportAsync(Tx(code, s.Date, Assignee, s.Product, 1m, refer: Other)), HttpStatusCode.Created);
        var item = SingleItem(await AddCodesAsync(s, code));
        Assert.Equal("NOT_MATCHED", item.GetProperty("status").GetString());
        Assert.Equal("REFERRER_MISMATCH", item.GetProperty("reason").GetString());
        Assert.Equal(0m, await ProgressAsync(s.AssigneeTargetId));

        var agreed = await ArrangeAsync(withHandover: true);
        var agreedCode = NewCode();
        api.AssertStatus(await ImportAsync(Tx(agreedCode, agreed.Date, Assignee, agreed.Product, 1m, refer: Referrer)), HttpStatusCode.Created);
        Assert.Equal("MATCHED", SingleItem(await AddCodesAsync(agreed, agreedCode)).GetProperty("status").GetString());
        Assert.Equal(0.3m, await ProgressAsync(agreed.ReferrerTargetId!.Value));
    }

    // C14: a referrer without a target for the product's KPI (a Teller referring a term deposit) no longer blocks the
    // processor: the processor gets 70 %, the referrer's share is not credited, the code is MATCHED with a reason.
    [ImportIntegrationFact]
    public async Task C14_ReferrerWithoutTarget_StillCreditsTheProcessor()
    {
        var s = await ArrangeAsync(withHandover: true, referrerHasTarget: false);
        var code = NewCode();
        api.AssertStatus(await AddCodesAsync(s, code), HttpStatusCode.OK);

        var import = await ImportAsync(Tx(code, s.Date, Assignee, s.Product, 1m));

        api.AssertStatus(import, HttpStatusCode.Created);
        Assert.Equal(1, import.Data.GetProperty("reconcile").GetProperty("matched").GetInt32());
        var stored = Assert.Single(await CodesAsync(s));
        Assert.Equal(WorkTransactionStatus.Matched, stored.Status);
        Assert.Equal("REFERRER_NOT_CREDITED", stored.Reason);
        Assert.Equal(WorkStatus.Done, await WorkStatusAsync(s));
        Assert.Equal(0.7m, await ProgressAsync(s.AssigneeTargetId));
        var referrerId = await api.QueryAsync(db => db.Employees.Where(e => e.EmployeeCode == Referrer).Select(e => e.Id).SingleAsync());
        Assert.False(await api.QueryAsync(db => db.WorkResultItems.AnyAsync(i => i.EmployeeId == referrerId
            && i.VerifiedWorkResult.WorkTransactionId == stored.Id)));
    }
}
