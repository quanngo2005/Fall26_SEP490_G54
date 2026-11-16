using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;

namespace G54.UnitTests;

public sealed class TransactionImportSampleTests
{
    private static readonly string[] CatalogProductCodes =
    [
        "DEPOSIT",
        "TERM_DEPOSIT",
        "WITHDRAWAL",
        "TRANSFER",
        "ACCOUNT_OPEN",
        "CARD_DEBIT_STD",
        "CARD_DEBIT_GOLD",
        "CARD_CREDIT_STD",
        "CARD_CREDIT_PLAT",
        "EBANKING",
        "SMS_BANKING",
        "QR_MERCHANT",
        "INSURANCE",
    ];

    [SampleFileFact("06_Giao_dich.xlsx")]
    public async Task ImportAsync_RealSampleFile_Imports18ValidRowsAndFlagsRows20To24WithExpectedCodes()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AppDbContext(options);

        var departmentId = Guid.NewGuid();
        var jobPositionId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.Zero);

        for (var i = 1; i <= 13; i++)
        {
            var code = $"NV{i:000}";
            db.Employees.Add(new Employee
            {
                Id = Guid.NewGuid(),
                EmployeeCode = code,
                FullName = $"Nhân viên {code}",
                Email = $"{code.ToLowerInvariant()}@bank.vn",
                DepartmentId = departmentId,
                JobPositionId = jobPositionId,
                Status = code == "NV006" ? EmployeeStatus.Terminated : EmployeeStatus.Active,
                UpdatedAt = now,
                LastSyncAt = now,
            });
        }

        foreach (var productCode in CatalogProductCodes)
        {
            db.WorkTypes.Add(new WorkType
            {
                Id = Guid.NewGuid(),
                Code = $"WT_{productCode}",
                Name = productCode,
                ProductCode = productCode,
                Status = RecordStatus.Active,
            });
        }

        db.EvaluationPeriods.Add(new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = "M-2026-09",
            Name = "Tháng 09/2026",
            StartDate = new DateOnly(2026, 9, 1),
            EndDate = new DateOnly(2026, 9, 30),
            PeriodType = PeriodType.Month,
            Status = PeriodStatus.Locked,
            UpdatedAt = now,
        });

        await db.SaveChangesAsync();

        var service = new TransactionImportService(new UnitOfWork(db), TimeProvider.System, new G54.BLL.Common.Import.Reading.ClosedXmlSheetReader(), Microsoft.Extensions.Options.Options.Create(new G54.BLL.Common.SystemParametersOptions()));
        var samplePath = Path.Combine(AppContext.BaseDirectory, "TestData", "06_Giao_dich.xlsx");
        await using var stream = File.OpenRead(samplePath);

        var batch = await service.ImportAsync("06_Giao_dich.xlsx", stream, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(23, batch.TotalRows);
        Assert.Equal(18, batch.ImportedRows);
        Assert.Equal(0, batch.DuplicateRows);
        Assert.Equal(5, batch.ErrorRows);

        var errors = batch.Errors!.OrderBy(e => e.Row).ToList();
        Assert.Equal([20, 21, 22, 23, 24], errors.Select(e => e.Row));

        Assert.Equal("DUPLICATE_IN_FILE", errors[0].Code);
        Assert.Equal("Mã giao dịch*", errors[0].Field);

        Assert.Equal("PERIOD_LOCKED", errors[1].Code);
        Assert.Equal("Ngày GD*", errors[1].Field);

        Assert.Equal("EMPLOYEE_NOT_FOUND", errors[2].Code);
        Assert.Equal("Mã NV*", errors[2].Field);

        Assert.Equal("INVALID_VALUE", errors[3].Code);
        Assert.Equal("Số tiền (VND)*", errors[3].Field);

        Assert.Equal("EMPLOYEE_TERMINATED", errors[4].Code);
        Assert.Equal("Mã NV*", errors[4].Field);

        var gd2610001 = await db.ImportedTransactions.SingleAsync(t => t.TransactionCode == "GD2610001");
        Assert.Equal(new DateOnly(2026, 10, 5), gd2610001.TransactionDate);
        Assert.Equal("KH10021", gd2610001.CustomerCode);
        Assert.Equal(15_000_000m, gd2610001.Amount);
        Assert.Equal("DEPOSIT", gd2610001.ProductCode);
    }
}
