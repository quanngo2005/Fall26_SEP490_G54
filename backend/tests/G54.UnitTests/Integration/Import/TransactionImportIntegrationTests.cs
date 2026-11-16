using System.Net;
using System.Text.Json;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using static G54.UnitTests.Integration.Import.XlsxBuilder;

namespace G54.UnitTests.Integration.Import;

/// <summary>
/// Core Banking transaction import (<c>POST /transaction-imports</c>) over HTTP against PostgreSQL.
/// Plan: docs/plans/IMPORT_INTEGRATION_TEST_PLAN.md section B. Seeded employees NV001-NV021 and the product catalog
/// (WorkType.ProductCode) come from the demo seed. Built files use per-test transaction codes and periods in
/// years nobody else uses, so tests do not depend on each other.
/// </summary>
[Trait("Category", "Integration")]
public sealed class TransactionImportIntegrationTests(ImportApiFixture api) : IClassFixture<ImportApiFixture>
{
    private const string Admin = "NV013";
    private const string Staff = "NV004";
    private const string Url = "/transaction-imports";

    private static string NewCode(string tag = "") => $"IT{tag}{Guid.NewGuid():N}"[..16].ToUpperInvariant();

    private static List<JsonElement> Errors(ApiResult result) =>
        [.. result.Data.GetProperty("errors").EnumerateArray().OrderBy(e => e.GetProperty("row").GetInt32())];

    private Task<ImportedTransaction?> StoredAsync(string code) =>
        api.QueryAsync(db => db.ImportedTransactions.AsNoTracking().Include(t => t.Employee).SingleOrDefaultAsync(t => t.TransactionCode == code));

    // B1 + B2 + B3 (one test: the sample's codes are fixed, so import and re-import must run in this order)
    [ImportIntegrationFact(SampleFile = "06_Giao_dich.xlsx")]
    public async Task B1_RealSample_Imports18Flags5StoresValuesThenReimportIsAllDuplicates()
    {
        await api.SetEmployeeStatusAsync("NV006", EmployeeStatus.Terminated);
        var september = await api.QueryAsync(db => db.EvaluationPeriods
            .Where(p => p.PeriodType == PeriodType.Month && p.StartDate == new DateOnly(2026, 9, 1)).Select(p => p.Id).SingleAsync());
        await api.SetPeriodStatusAsync(september, PeriodStatus.Locked);
        var sample = await File.ReadAllBytesAsync(ImportApiFixture.SamplePath("06_Giao_dich.xlsx"));

        var result = await api.PostFileAsync(Url, sample, Admin, "06_Giao_dich.xlsx");

        api.AssertStatus(result, HttpStatusCode.Created);
        Assert.Equal(23, result.Data.GetProperty("totalRows").GetInt32());
        Assert.Equal(18, result.Data.GetProperty("importedRows").GetInt32());
        Assert.Equal(5, result.Data.GetProperty("errorRows").GetInt32());
        var errors = Errors(result);
        Assert.Equal([20, 21, 22, 23, 24], errors.Select(e => e.GetProperty("row").GetInt32()));
        Assert.Equal(["DUPLICATE_IN_FILE", "PERIOD_LOCKED", "EMPLOYEE_NOT_FOUND", "INVALID_VALUE", "EMPLOYEE_TERMINATED"],
            errors.Select(e => e.GetProperty("code").GetString()));
        Assert.Equal("Mã giao dịch*", errors[0].GetProperty("field").GetString());
        Assert.Equal("Số tiền (VND)*", errors[3].GetProperty("field").GetString());

        // B2: stored values and the batch listings
        var stored = await StoredAsync("GD2610001");
        Assert.NotNull(stored);
        Assert.Equal(new DateOnly(2026, 10, 5), stored.TransactionDate);
        Assert.Equal(15000000m, stored.Amount);
        Assert.Equal("KH10021", stored.CustomerCode);
        Assert.Equal("NV001", stored.Employee.EmployeeCode);
        var batchId = result.Data.GetProperty("id").GetGuid();
        var filtered = await api.SendAsync(HttpMethod.Get, $"{Url}/{batchId}/transactions?employeeCode=NV012&pageSize=50", Admin);
        api.AssertStatus(filtered, HttpStatusCode.OK);
        Assert.Equal(["GD2610014", "GD2610015", "GD2610016"],
            filtered.Data.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("transactionCode").GetString()));
        var listedErrors = await api.SendAsync(HttpMethod.Get, $"{Url}/{batchId}/errors", Admin);
        Assert.Equal(5, listedErrors.Data.GetProperty("totalCount").GetInt32());

        // B3: the same file again
        var again = await api.PostFileAsync(Url, sample, Admin, "06_Giao_dich.xlsx");
        api.AssertStatus(again, HttpStatusCode.Created);
        Assert.Equal(0, again.Data.GetProperty("importedRows").GetInt32());
        Assert.Equal(18, again.Data.GetProperty("duplicateRows").GetInt32());
        Assert.Equal(18, await api.QueryAsync(db => db.ImportedTransactions.CountAsync(t => t.TransactionCode.StartsWith("GD26"))));
    }

    // B4
    [ImportIntegrationFact]
    public async Task B4_SameCodeWithDifferentAmount_IsConflictingDuplicateAndKeepsTheFirstRow()
    {
        var code = NewCode();
        api.AssertStatus(await api.PostFileAsync(Url, Transactions(Tx(code, "05/10/2026", "NV001", "DEPOSIT", 100m)), Admin), HttpStatusCode.Created);

        var result = await api.PostFileAsync(Url, Transactions(Tx(code, "05/10/2026", "NV001", "DEPOSIT", 200m)), Admin);

        api.AssertStatus(result, HttpStatusCode.Created);
        Assert.Equal("CONFLICTING_DUPLICATE", Assert.Single(Errors(result)).GetProperty("code").GetString());
        Assert.Equal(100m, (await StoredAsync(code))!.Amount);
    }

    // B5: existing codes are matched with upper() on PostgreSQL; a code in another case is not a new row
    [ImportIntegrationFact]
    public async Task B5_CodeInAnotherCase_IsTheSameTransaction()
    {
        var code = NewCode();
        var legacy = code.ToLowerInvariant();
        await api.WithDbAsync(async db =>
        {
            var admin = await db.Accounts.Where(a => a.Employee.EmployeeCode == Admin).Select(a => a.Id).SingleAsync();
            var nv001 = await db.Employees.Where(e => e.EmployeeCode == "NV001").Select(e => e.Id).SingleAsync();
            var batch = new ImportBatch { Id = Guid.NewGuid(), FileName = "legacy.xlsx", ImportedBy = admin, ImportedAt = DateTimeOffset.UtcNow, TotalRows = 1, ImportedRows = 1 };
            batch.Transactions.Add(new ImportedTransaction
            {
                Id = Guid.NewGuid(),
                ImportBatchId = batch.Id,
                TransactionCode = legacy, // stored before codes were upper-cased
                EmployeeId = nv001,
                ProductCode = "DEPOSIT",
                TransactionDate = new DateOnly(2026, 10, 5),
                Amount = 100m,
                CustomerCode = "KH1",
            });
            db.ImportBatches.Add(batch);
            await db.SaveChangesAsync();
        });

        var same = await api.PostFileAsync(Url, Transactions(Tx(code, "05/10/2026", "NV001", "DEPOSIT", 100m)), Admin);
        var different = await api.PostFileAsync(Url, Transactions(Tx(code, "05/10/2026", "NV001", "DEPOSIT", 999m)), Admin);

        api.AssertStatus(same, HttpStatusCode.Created);
        Assert.Equal(1, same.Data.GetProperty("duplicateRows").GetInt32());
        Assert.Equal("CONFLICTING_DUPLICATE", Assert.Single(Errors(different)).GetProperty("code").GetString());
        Assert.Equal(1, await api.QueryAsync(db => db.ImportedTransactions.CountAsync(t => t.TransactionCode == code || t.TransactionCode == legacy)));
    }

    // B6
    [ImportIntegrationFact]
    public async Task B6_LegacyEnglishHeaders_ImportWithoutCustomerCode()
    {
        var code = NewCode();
        var file = Build(
            ["transaction_code", "employee_code", "product_code", "transaction_date", "amount", "denominator", "refer_employee_code"],
            [code.ToLowerInvariant(), "NV002", "TRANSFER", "2026-10-07", 3500000m, null, null]);

        var result = await api.PostFileAsync(Url, file, Admin);

        api.AssertStatus(result, HttpStatusCode.Created);
        Assert.Equal(1, result.Data.GetProperty("importedRows").GetInt32());
        var stored = await StoredAsync(code); // stored upper-cased
        Assert.NotNull(stored);
        Assert.Null(stored.CustomerCode);
        Assert.Equal(new DateOnly(2026, 10, 7), stored.TransactionDate);
    }

    // B7
    [ImportIntegrationFact]
    public async Task B7_DateAndAmountCells_RealDateTextDateAndFormattedAmountsParseTheSame()
    {
        var realDate = NewCode("A");
        var textDate = NewCode("B");
        var dayFirst = NewCode("C");
        var commaAmount = NewCode("D");
        var badAmount = NewCode("E");
        var badDate = NewCode("F");
        var file = Transactions(
            Tx(realDate, new DateTime(2026, 10, 5), "NV001", "DEPOSIT", 15000000m),
            Tx(textDate, "05/10/2026", "NV001", "DEPOSIT", "15.000.000"),
            Tx(dayFirst, "10/05/2026", "NV001", "DEPOSIT", "1"),
            Tx(commaAmount, "2026-10-05", "NV001", "DEPOSIT", "15,000,000"),
            Tx(badAmount, "05/10/2026", "NV001", "DEPOSIT", "abc"),
            Tx(badDate, "31/02/2026", "NV001", "DEPOSIT", 1m));

        var result = await api.PostFileAsync(Url, file, Admin);

        api.AssertStatus(result, HttpStatusCode.Created);
        Assert.Equal(4, result.Data.GetProperty("importedRows").GetInt32());
        Assert.Equal(new DateOnly(2026, 10, 5), (await StoredAsync(realDate))!.TransactionDate);
        Assert.Equal(new DateOnly(2026, 10, 5), (await StoredAsync(textDate))!.TransactionDate);
        Assert.Equal(new DateOnly(2026, 5, 10), (await StoredAsync(dayFirst))!.TransactionDate); // dd/MM, never MM/dd
        Assert.Equal(15000000m, (await StoredAsync(textDate))!.Amount);
        Assert.Equal(15000000m, (await StoredAsync(commaAmount))!.Amount);
        var errors = Errors(result);
        Assert.Equal(["Số tiền (VND)*", "Ngày GD*"], errors.Select(e => e.GetProperty("field").GetString()));
        Assert.All(errors, e => Assert.Equal("INVALID_VALUE", e.GetProperty("code").GetString()));
    }

    // B8: a row is rejected when ANY month, quarter or year containing its date is LOCKED or CLOSED
    [ImportIntegrationTheory]
    [InlineData(PeriodType.Month, PeriodStatus.Locked, true, 2041)]
    [InlineData(PeriodType.Quarter, PeriodStatus.Locked, true, 2042)]
    [InlineData(PeriodType.Year, PeriodStatus.Locked, true, 2043)]
    [InlineData(PeriodType.Month, PeriodStatus.Closed, true, 2044)]
    [InlineData(PeriodType.Year, PeriodStatus.Closed, true, 2045)]
    [InlineData(PeriodType.Quarter, PeriodStatus.InReview, false, 2046)]
    [InlineData(PeriodType.Month, PeriodStatus.Open, false, 2047)]
    public async Task B8_PeriodLock_EachPeriodTypeBlocksRowsInsideIt(PeriodType lockedType, PeriodStatus status, bool rejected, int year)
    {
        // Each case owns a year that no other test or the seed uses.
        var yearId = await api.AddPeriodAsync(PeriodType.Year, new DateOnly(year, 1, 1), new DateOnly(year, 12, 31),
            lockedType == PeriodType.Year ? status : PeriodStatus.Open);
        var halfId = await api.AddPeriodAsync(PeriodType.HalfYear, new DateOnly(year, 1, 1), new DateOnly(year, 6, 30), PeriodStatus.Open, yearId);
        var quarterId = await api.AddPeriodAsync(PeriodType.Quarter, new DateOnly(year, 1, 1), new DateOnly(year, 3, 31),
            lockedType == PeriodType.Quarter ? status : PeriodStatus.Open, halfId);
        await api.AddPeriodAsync(PeriodType.Month, new DateOnly(year, 1, 1), new DateOnly(year, 1, 31),
            lockedType == PeriodType.Month ? status : PeriodStatus.Open, quarterId);
        var inside = NewCode("I");
        var outsideQuarter = NewCode("O");

        var result = await api.PostFileAsync(Url, Transactions(
            Tx(inside, $"15/01/{year}", "NV001", "DEPOSIT", 1m),
            Tx(outsideQuarter, $"15/07/{year}", "NV001", "DEPOSIT", 1m)), Admin);

        api.AssertStatus(result, HttpStatusCode.Created);
        var errors = Errors(result);
        Assert.Equal(rejected, errors.Any(e => e.GetProperty("row").GetInt32() == 2 && e.GetProperty("code").GetString() == "PERIOD_LOCKED"));
        Assert.Equal(!rejected, await StoredAsync(inside) is not null);
        // July is outside the month and quarter; only a locked/closed year also covers it.
        var yearBlocks = lockedType == PeriodType.Year && rejected;
        Assert.Equal(!yearBlocks, await StoredAsync(outsideQuarter) is not null);
    }

    // B9
    [ImportIntegrationFact]
    public async Task B9_ProductCatalogAndReferEmployee_AreValidated()
    {
        var unknownProduct = NewCode("P");
        var unknownRefer = NewCode("R");
        var validRefer = NewCode("V");
        var file = Transactions(
            Tx(unknownProduct, "05/10/2026", "NV001", "LOAN", 1m),
            Tx(unknownRefer, "05/10/2026", "NV001", "DEPOSIT", 1m, refer: "NV999"),
            Tx(validRefer, "05/10/2026", "NV001", "deposit", 1m, refer: "nv002"));

        var result = await api.PostFileAsync(Url, file, Admin);

        api.AssertStatus(result, HttpStatusCode.Created);
        Assert.Equal(["PRODUCT_NOT_FOUND", "REFER_EMPLOYEE_NOT_FOUND"], Errors(result).Select(e => e.GetProperty("code").GetString()));
        var stored = await StoredAsync(validRefer);
        Assert.NotNull(stored);
        Assert.Equal("NV002", stored.ReferEmployeeCode);
        Assert.Equal("DEPOSIT", stored.ProductCode);
    }

    // B10
    [ImportIntegrationFact]
    public async Task B10_ParallelImportsOfOverlappingCodes_StoreEachCodeOnce()
    {
        var codes = Enumerable.Range(0, 30).Select(i => NewCode($"{i:D2}")).ToList();
        var rows = codes.Select(c => Tx(c, "05/10/2026", "NV001", "DEPOSIT", 1m)).ToArray();
        var first = Transactions(rows[..20]);
        var second = Transactions(rows[10..]);

        var results = await Task.WhenAll(
            api.PostFileAsync(Url, first, Admin),
            api.PostFileAsync(Url, second, Admin),
            api.PostFileAsync(Url, first, Admin));

        foreach (var r in results)
        {
            api.AssertStatus(r, HttpStatusCode.Created);
            Assert.Equal(0, r.Data.GetProperty("errorRows").GetInt32());
        }

        Assert.Equal(30, results.Sum(r => r.Data.GetProperty("importedRows").GetInt32()));
        Assert.Equal(30, await api.QueryAsync(db => db.ImportedTransactions.CountAsync(t => codes.Contains(t.TransactionCode))));
    }

    // B11
    [ImportIntegrationFact]
    public async Task B11_LimitsAuthAndHistoryPaging()
    {
        var file = Transactions(Tx(NewCode(), "05/10/2026", "NV001", "DEPOSIT", 1m));
        api.AssertStatus(await api.PostFileAsync(Url, file, null), HttpStatusCode.Unauthorized);
        api.AssertStatus(await api.PostFileAsync(Url, file, Staff), HttpStatusCode.Forbidden);

        var tooMany = Transactions([.. Enumerable.Range(0, 5001).Select(i => Tx($"MAX{i}", "05/10/2026", "NV001", "DEPOSIT", 1m))]);
        var limit = await api.PostFileAsync(Url, tooMany, Admin);
        api.AssertStatus(limit, HttpStatusCode.BadRequest);
        Assert.False(await api.QueryAsync(db => db.ImportedTransactions.AnyAsync(t => t.TransactionCode == "MAX0")));

        var missingColumn = Build(["Mã giao dịch*", "Mã NV*"], ["X", "NV001"]);
        var missing = await api.PostFileAsync(Url, missingColumn, Admin);
        api.AssertStatus(missing, HttpStatusCode.BadRequest);
        Assert.Equal("IMPORT_INVALID_FILE", missing.ErrorCode);

        var history = await api.SendAsync(HttpMethod.Get, $"{Url}?page=0&pageSize=500", Admin);
        api.AssertStatus(history, HttpStatusCode.OK);
        Assert.Equal(1, history.Data.GetProperty("page").GetInt32());
        Assert.Equal(200, history.Data.GetProperty("pageSize").GetInt32());

        using var template = await api.GetRawAsync($"{Url}/template", Admin);
        Assert.Equal(HttpStatusCode.OK, template.StatusCode);
        api.AssertStatus(await api.SendAsync(HttpMethod.Get, $"{Url}/{Guid.NewGuid()}", Admin), HttpStatusCode.NotFound);
    }
}
