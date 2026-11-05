using ClosedXML.Excel;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;

namespace G54.UnitTests;

public sealed class TransactionImportServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 3, 0, 0, TimeSpan.Zero);
    private static readonly string[] Header =
        ["transaction_code", "employee_code", "product_code", "transaction_date", "amount", "denominator", "refer_employee_code"];

    private static (AppDbContext Db, TransactionImportService Service) Create()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Employees.Add(new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "E001",
            FullName = "A",
            Email = "a@x.vn",
            DepartmentId = Guid.NewGuid(),
            JobPositionId = Guid.NewGuid(),
            UpdatedAt = Now,
            LastSyncAt = Now,
        });
        db.SaveChanges();
        return (db, new TransactionImportService(new UnitOfWork(db), TimeProvider.System));
    }

    private static MemoryStream Workbook(string[] header, params object?[][] rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet();
        for (var c = 0; c < header.Length; c++) ws.Cell(1, c + 1).Value = header[c];
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++)
                if (rows[r][c] is { } v) ws.Cell(r + 2, c + 1).Value = XLCellValue.FromObject(v, System.Globalization.CultureInfo.InvariantCulture);
        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    private static object?[] Row(string code, string emp = "E001", string product = "CARD", decimal amount = 10m, object? denominator = null) =>
        [code, emp, product, new DateTime(2026, 11, 1), amount, denominator, null];

    [Fact]
    public async Task Import_StoresValidRowsAndReportsTheRest()
    {
        var (db, service) = Create();
        using var file = Workbook(Header, Row("T1"), Row("T2", emp: "NOPE"), Row("T3", amount: -1m), Row("T1"));

        var result = await service.ImportAsync("tx.xlsx", file, Guid.NewGuid());

        Assert.Equal(4, result.TotalRows);
        Assert.Equal(1, result.ImportedRows);
        Assert.Equal(3, result.ErrorRows);
        Assert.Contains(result.Errors!, e => e.Row == 3 && e.Code == "EMPLOYEE_NOT_FOUND");
        Assert.Contains(result.Errors!, e => e.Row == 4 && e.Code == "INVALID_VALUE");
        Assert.Contains(result.Errors!, e => e.Row == 5 && e.Code == "DUPLICATE_IN_FILE");
        Assert.Equal(1, await db.ImportedTransactions.CountAsync());
    }

    [Fact]
    public async Task Import_SkipsTransactionCodesAlreadyImported()
    {
        var (db, service) = Create();
        using (var first = Workbook(Header, Row("T1"))) await service.ImportAsync("a.xlsx", first, Guid.NewGuid());
        using var second = Workbook(Header, Row("T1"), Row("T2"));

        var result = await service.ImportAsync("b.xlsx", second, Guid.NewGuid());

        Assert.Equal(1, result.DuplicateRows);
        Assert.Equal(1, result.ImportedRows);
        Assert.Equal(2, await db.ImportedTransactions.CountAsync());
    }

    [Fact]
    public async Task Import_FlagsMissingRequiredFieldsAndBadDenominator()
    {
        var (_, service) = Create();
        using var file = Workbook(Header, Row("T1", product: ""), Row("T2", denominator: 0m));

        var result = await service.ImportAsync("tx.xlsx", file, Guid.NewGuid());

        Assert.Equal(0, result.ImportedRows);
        Assert.Contains(result.Errors!, e => e.Row == 2 && e.Code == "REQUIRED_FIELD_MISSING");
        Assert.Contains(result.Errors!, e => e.Row == 3 && e.Field == "denominator");
    }

    [Fact]
    public async Task Import_RejectsWrongHeaderEmptyAndNonWorkbookFiles()
    {
        var (_, service) = Create();
        using var wrongHeader = Workbook(["code", "employee_code", "product_code", "transaction_date", "amount"], Row("T1"));
        using var noRows = Workbook(Header);
        using var junk = new MemoryStream([1, 2, 3]);

        foreach (var stream in new[] { wrongHeader, noRows, junk })
        {
            var ex = await Assert.ThrowsAsync<AppException>(() => service.ImportAsync("tx.xlsx", stream, Guid.NewGuid()));
            Assert.Equal(ErrorCodes.ImportInvalidFile, ex.ErrorCode);
        }
    }

    [Fact]
    public async Task ListTransactions_FiltersAndThrowsNotFoundForUnknownBatch()
    {
        var (_, service) = Create();
        using var file = Workbook(Header, Row("T1", product: "CARD"), Row("T2", product: "LOAN"));
        var batch = await service.ImportAsync("tx.xlsx", file, Guid.NewGuid());

        var page = await service.ListTransactionsAsync(batch.Id, null, "LOAN", 1, 20);

        Assert.Equal("T2", Assert.Single(page.Items).TransactionCode);
        await Assert.ThrowsAsync<NotFoundException>(() => service.ListTransactionsAsync(Guid.NewGuid(), null, null, 1, 20));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetBatchAsync(Guid.NewGuid()));
    }
}
