using ClosedXML.Excel;
using G54.BLL.Common;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Import.Reading;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace G54.UnitTests;

public sealed class ServiceRatingImportServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 3, 0, 0, TimeSpan.Zero);
    private static readonly string[] Header =
        ["Ngày đánh giá*", "Mã NV*", "Mã giao dịch*", "Điểm (1–5)*", "Nhận xét KH", "Ghi chú (demo)"];

    private sealed record Fixture(AppDbContext Db, ServiceRatingImportService Service, RecordingNotifier Notifier, Guid DepartmentId, Guid ManagerId);

    private static Fixture Create()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var department = new Department { Id = Guid.NewGuid(), Code = "D1", Name = "Phòng 1", LevelType = DepartmentLevelType.Department, Status = RecordStatus.Active };
        var position = new JobPosition { Id = Guid.NewGuid(), Code = "TELLER", Title = "Giao dịch viên", Status = RecordStatus.Active };
        var managerRole = new Role { Id = Guid.NewGuid(), Code = "DEPT_MANAGER", Name = "Trưởng phòng", Status = RecordStatus.Active };
        var manager = NewEmployee("M001", department.Id, position.Id);
        var account = new Account { Id = Guid.NewGuid(), EmployeeId = manager.Id, Username = "m001", PasswordHash = "h" };
        db.AddRange(department, position, managerRole, manager, account, NewEmployee("E001", department.Id, position.Id));
        db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), AccountId = account.Id, RoleId = managerRole.Id });
        db.EvaluationPeriods.Add(new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = "M-2026-09",
            Name = "Tháng 09/2026",
            StartDate = new DateOnly(2026, 9, 1),
            EndDate = new DateOnly(2026, 9, 30),
            PeriodType = PeriodType.Month,
            Status = PeriodStatus.Locked,
        });
        db.SaveChanges();
        var notifier = new RecordingNotifier();
        var service = new ServiceRatingImportService(
            new UnitOfWork(db), db, TimeProvider.System, new ClosedXmlSheetReader(), Options.Create(new SystemParametersOptions()), notifier);
        return new Fixture(db, service, notifier, department.Id, manager.Id);
    }

    private static Employee NewEmployee(string code, Guid departmentId, Guid positionId) => new()
    {
        Id = Guid.NewGuid(),
        EmployeeCode = code,
        FullName = $"Nhân viên {code}",
        Email = $"{code.ToLowerInvariant()}@x.vn",
        DepartmentId = departmentId,
        JobPositionId = positionId,
        Status = EmployeeStatus.Active,
        UpdatedAt = Now,
        LastSyncAt = Now,
    };

    private static MemoryStream Workbook(params object?[][] rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Đánh giá dịch vụ");
        for (var c = 0; c < Header.Length; c++) ws.Cell(1, c + 1).Value = Header[c];
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++)
                if (rows[r][c] is { } v) ws.Cell(r + 2, c + 1).Value = XLCellValue.FromObject(v, System.Globalization.CultureInfo.InvariantCulture);
        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    private static object?[] Row(string code, object score, string employee = "E001", string date = "06/10/2026", string? comment = null) =>
        [date, employee, code, score, comment, null];

    [Fact]
    public async Task Import_StoresValidRatingsReportsTheRestAndListsThem()
    {
        var f = Create();
        using var file = Workbook(
            Row("GD1", 5, comment: "Nhiệt tình"),
            Row("GD2", 6),
            Row("GD3", 2.5),
            Row("GD4", 4, employee: "NOPE"),
            Row("GD5", 4, date: "15/09/2026"),
            Row("GD6", 4, date: "2026/10/06"),
            Row("GD1", 5, comment: "Nhiệt tình"),
            Row("GD1", 3),
            Row("", 3));

        var result = await f.Service.ImportAsync("07.xlsx", file, Guid.NewGuid());

        Assert.Equal(9, result.TotalRows);
        Assert.Equal(1, result.ImportedRows);
        Assert.Equal(1, result.DuplicateRows);
        Assert.Equal(7, result.ErrorRows);
        Assert.Null(result.Reconcile);
        Assert.Equal(
            [(3, "INVALID_VALUE"), (4, "INVALID_VALUE"), (5, "EMPLOYEE_NOT_FOUND"), (6, "PERIOD_LOCKED"), (7, "INVALID_VALUE"), (9, "CONFLICTING_DUPLICATE"), (10, "REQUIRED_FIELD_MISSING")],
            result.Errors!.Select(e => (e.Row, e.Code)));
        Assert.Equal("Điểm (1–5)*", result.Errors![0].Field);

        var errors = await f.Service.ListErrorsAsync(result.Id, 1, 20);
        Assert.Equal(7, errors.TotalCount);
        var rating = Assert.Single((await f.Service.ListRatingsAsync(result.Id, 1, 20)).Items);
        Assert.Equal(("GD1", "E001", new DateOnly(2026, 10, 6), 5, "Nhiệt tình"), (rating.TransactionCode, rating.EmployeeCode, rating.RatingDate, rating.Score, rating.CustomerComment));
        Assert.Equal(ImportFileType.ServiceRating, (await f.Db.ImportBatches.SingleAsync()).FileType);
    }

    [Fact]
    public async Task Import_CountsARatingAlreadyStoredAsDuplicateAndADifferentOneAsConflict()
    {
        var f = Create();
        using (var first = Workbook(Row("GD1", 4), Row("GD2", 4))) await f.Service.ImportAsync("a.xlsx", first, Guid.NewGuid());
        using var second = Workbook(Row("gd1", 4), Row("GD2", 5));

        var result = await f.Service.ImportAsync("b.xlsx", second, Guid.NewGuid());

        Assert.Equal((0, 1), (result.ImportedRows, result.DuplicateRows));
        Assert.Equal("CONFLICTING_DUPLICATE", Assert.Single(result.Errors!).Code);
        Assert.Equal(2, await f.Db.ServiceRatings.CountAsync());
    }

    [Fact]
    public async Task Import_AlertsTheDepartmentManagerOncePerOneStarRating()
    {
        var f = Create();
        using var file = Workbook(Row("GD1", 1, comment: "Chờ lâu"), Row("GD2", 1), Row("GD3", 2));

        var result = await f.Service.ImportAsync("07.xlsx", file, Guid.NewGuid());

        Assert.Equal(2, f.Notifier.Drafts.Count);
        Assert.All(f.Notifier.Drafts, d =>
        {
            Assert.Equal(f.ManagerId, d.RecipientEmployeeId);
            Assert.Equal(NotificationType.RatingOneStar, d.Type);
            Assert.Equal(NotificationRelatedType.ImportBatch, d.RelatedType);
            Assert.Equal(result.Id, d.RelatedId);
        });
        Assert.Contains("GD1", f.Notifier.Drafts[0].Body);
        Assert.Contains("Chờ lâu", f.Notifier.Drafts[0].Body);
    }

    [Fact]
    public async Task Import_AlertsTheManagerOfTheParentDepartmentForAnEmployeeOfATeam()
    {
        var f = Create();
        var team = new Department { Id = Guid.NewGuid(), Code = "D1-T1", Name = "Nhóm 1", ParentDepartmentId = f.DepartmentId, LevelType = DepartmentLevelType.Team, Status = RecordStatus.Active };
        f.Db.Departments.Add(team);
        f.Db.Employees.Add(NewEmployee("E002", team.Id, f.Db.JobPositions.Single().Id));
        await f.Db.SaveChangesAsync();
        using var file = Workbook(Row("GD1", 1, employee: "E002"));

        await f.Service.ImportAsync("07.xlsx", file, Guid.NewGuid());

        Assert.Equal(f.ManagerId, Assert.Single(f.Notifier.Drafts).RecipientEmployeeId);
    }

    [Fact]
    public async Task Import_RejectsAFileWithoutTheRequiredColumns()
    {
        var f = Create();
        using var wb = new XLWorkbook();
        wb.AddWorksheet().Cell(1, 1).Value = "Mã NV*";
        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        stream.Position = 0;

        var ex = await Assert.ThrowsAsync<AppException>(() => f.Service.ImportAsync("x.xlsx", stream, Guid.NewGuid()));

        Assert.Equal(G54.BLL.Common.Constants.ErrorCodes.ImportInvalidFile, ex.ErrorCode);
    }

    [Fact]
    public async Task History_OnlyShowsRatingBatchesAndAnswersNotFoundForATransactionBatch()
    {
        var f = Create();
        var transactionBatch = new ImportBatch { Id = Guid.NewGuid(), FileName = "06.xlsx", ImportedAt = Now };
        f.Db.ImportBatches.Add(transactionBatch);
        await f.Db.SaveChangesAsync();
        using (var file = Workbook(Row("GD1", 5))) await f.Service.ImportAsync("07.xlsx", file, Guid.NewGuid());

        var list = await f.Service.ListBatchesAsync(1, 20);

        Assert.Equal("07.xlsx", Assert.Single(list.Items).FileName);
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetBatchAsync(transactionBatch.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.ListRatingsAsync(transactionBatch.Id, 1, 20));
    }
}
