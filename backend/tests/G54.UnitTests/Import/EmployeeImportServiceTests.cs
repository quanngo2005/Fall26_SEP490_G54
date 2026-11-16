using ClosedXML.Excel;
using G54.BLL.Common;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Import.Reading;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace G54.UnitTests.Import;

public sealed class EmployeeImportServiceTests
{
    private static readonly string[] Headers =
    [
        "Mã chi nhánh*", "Tên chi nhánh*", "Mã CN cha", "Mã phòng ban*", "Tên phòng ban*", "Đơn vị cha",
        "Mã NV*", "Họ tên*", "Email*", "Vị trí*", "Mã quản lý trực tiếp", "Trạng thái*",
    ];

    private static (AppDbContext Db, EmployeeImportService Service) Create(bool seedUnrelatedEmployee = true)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new AppDbContext(options);

        foreach (var code in new[] { "TELLER", "SALES", "DEPT_MANAGER", "BRANCH_MANAGER", "QA", "HR_OFFICER", "SYS_ADMIN" })
        {
            db.JobPositions.Add(new JobPosition { Id = Guid.NewGuid(), Code = code, Title = code, Status = RecordStatus.Active });
        }

        foreach (var code in new[] { "EMPLOYEE", "DEPT_MANAGER", "BRANCH_MANAGER", "VERIFIER", "HR_OFFICER", "SYSTEM_ADMIN" })
        {
            db.Roles.Add(new Role { Id = Guid.NewGuid(), Code = code, Name = code, Status = RecordStatus.Active });
        }

        var branch = new Department { Id = Guid.NewGuid(), Code = "HN01", Name = "CN Hà Nội", LevelType = DepartmentLevelType.Branch, Status = RecordStatus.Active };
        db.Departments.Add(branch);

        if (seedUnrelatedEmployee)
        {
            // An employee the import file never mentions: BR-52 says it must stay untouched.
            var unrelated = new Employee
            {
                Id = Guid.NewGuid(),
                EmployeeCode = "NV888",
                FullName = "Người không trong file",
                Email = "unrelated@bank.local",
                DepartmentId = branch.Id,
                JobPositionId = db.JobPositions.Local.First(j => j.Code == "TELLER").Id,
                Status = EmployeeStatus.Active,
            };
            db.Employees.Add(unrelated);
            db.Accounts.Add(new Account
            {
                Id = Guid.NewGuid(),
                EmployeeId = unrelated.Id,
                Username = "unrelated@bank.local",
                PasswordHash = "hash",
            });
        }

        db.SaveChanges();

        var service = new EmployeeImportService(
            new UnitOfWork(db),
            db,
            new ClosedXmlSheetReader(),
            new PlainPasswordEncoder(),
            Options.Create(new SystemParametersOptions()));
        return (db, service);
    }

    private static MemoryStream Workbook(params (string Code, string Name, string Email, string Position, string Manager, string Status)[] employees)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Data");
        for (var c = 0; c < Headers.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = Headers[c];
        }

        for (var i = 0; i < employees.Length; i++)
        {
            var e = employees[i];
            string[] cells =
            [
                "hn01", "CN Hà Nội", "", "PGD-HK", "Phòng giao dịch Hoàng Kiếm", "",
                e.Code, e.Name, e.Email, e.Position, e.Manager, e.Status,
            ];
            for (var c = 0; c < cells.Length; c++)
            {
                sheet.Cell(i + 2, c + 1).Value = cells[c];
            }
        }

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    [SampleFileFact("02_Nhan_su_To_chuc.xlsx")]
    public async Task PreviewAsync_RealSampleFile_Returns21Total17ValidAnd4ExpectedInvalidRows()
    {
        var (_, service) = Create(seedUnrelatedEmployee: false);
        await using var file = EmployeeOrgSampleWorkbook.OpenRealFile();

        var preview = await service.PreviewAsync("02_Nhan_su_To_chuc.xlsx", file, CancellationToken.None);

        Assert.Equal(21, preview.Summary.TotalRows);
        Assert.Equal(17, preview.Summary.Valid);
        Assert.Equal(4, preview.Summary.Invalid);
        Assert.Equal(17, preview.Summary.Create);
        Assert.Equal(0, preview.Summary.Update);

        var invalidRows = preview.Rows.Where(r => r.Action == "INVALID").OrderBy(r => r.RowNumber).ToList();
        Assert.Equal(EmployeeOrgSampleWorkbook.InvalidRowNumbers, invalidRows.Select(r => r.RowNumber));
        Assert.Equal(["NV007", "NV008", "NV005", "NV019"], invalidRows.Select(r => r.Data["Mã NV*"]));

        Assert.Contains(invalidRows[0].Errors, e => e.Column == "Email*" && e.Code == ErrorCodes.DataImportInvalidValue);
        Assert.Contains(invalidRows[1].Errors, e => e.Column == "Họ tên*" && e.Code == ErrorCodes.RequiredField);
        Assert.Contains(invalidRows[1].Errors, e => e.Column == "Email*" && e.Code == ErrorCodes.RequiredField);
        Assert.Contains(invalidRows[1].Errors, e => e.Column == "Vị trí*" && e.Code == ErrorCodes.DataImportInvalidValue);
        Assert.Contains(invalidRows[2].Errors, e => e.Column == "Mã NV*" && e.Code == ErrorCodes.DataImportDuplicate && e.Message.Contains("dòng 9", StringComparison.Ordinal));
        Assert.Contains(invalidRows[3].Errors, e => e.Column == "Mã quản lý trực tiếp" && e.Code == ErrorCodes.DataImportReferenceNotFound);

        var row9 = preview.Rows.Single(r => r.RowNumber == 9);
        Assert.Equal("CREATE", row9.Action);
        Assert.Equal("NV005", row9.Data["Mã NV*"]);
    }

    [SampleFileFact("02_Nhan_su_To_chuc.xlsx")]
    public async Task CommitAsync_RealSampleFile_Commits17ValidRowsSkips4AndLocksTerminatedAccountOnly()
    {
        var (db, service) = Create(seedUnrelatedEmployee: false);
        await using var file = EmployeeOrgSampleWorkbook.OpenRealFile();

        var result = await service.CommitAsync(file, CancellationToken.None);

        Assert.Equal(17, result.Created);
        Assert.Equal(0, result.Updated);
        Assert.Equal(4, result.Skipped);
        Assert.Equal(17, result.NewAccounts.Count);
        Assert.Equal(EmployeeOrgSampleWorkbook.InvalidRowNumbers, result.Errors.Select(e => e.RowNumber).Order());

        var nv006 = await db.Employees.SingleAsync(e => e.EmployeeCode == "NV006");
        Assert.Equal(EmployeeStatus.Terminated, nv006.Status);
        Assert.True((await db.Accounts.SingleAsync(a => a.EmployeeId == nv006.Id)).IsLocked);

        var nv015 = await db.Employees.SingleAsync(e => e.EmployeeCode == "NV015");
        Assert.Equal(EmployeeStatus.OnLeave, nv015.Status);
        Assert.False((await db.Accounts.SingleAsync(a => a.EmployeeId == nv015.Id)).IsLocked);

        var nv005 = await db.Employees.SingleAsync(e => e.EmployeeCode == "NV005");
        Assert.Equal("Vũ Thị Em", nv005.FullName);
        Assert.Equal("em.vt@bank.vn", nv005.Email);
    }

    [Fact]
    public async Task PreviewAsync_SampleWorkbookBuilder_MatchesRealSampleInvalidRows()
    {
        var (_, service) = Create(seedUnrelatedEmployee: false);

        var preview = await service.PreviewAsync("sample.xlsx", EmployeeOrgSampleWorkbook.Build(), CancellationToken.None);

        var invalid = preview.Rows.Where(r => r.Action == "INVALID").Select(r => r.RowNumber).Order().ToArray();
        Assert.Equal(EmployeeOrgSampleWorkbook.InvalidRowNumbers, invalid);
        Assert.Equal(4, preview.Summary.Invalid);
        Assert.Equal(17, preview.Summary.Create);
    }

    [Fact]
    public async Task PreviewAsync_ExistingEmployeeCode_IsUpdateAndWritesNothing()
    {
        var (db, service) = Create();

        var preview = await service.PreviewAsync(
            "f.xlsx", Workbook(("nv888", "Tên mới", "new@bank.local", "TELLER", "", "ACTIVE")), CancellationToken.None);

        Assert.Equal("UPDATE", preview.Rows.Single().Action);
        Assert.Equal("Người không trong file", db.Employees.Single(e => e.EmployeeCode == "NV888").FullName);
    }

    [Fact]
    public async Task CommitAsync_WhenZeroRowsAreValid_Throws422AndWritesNothing()
    {
        var (db, service) = Create();

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.CommitAsync(
                Workbook(
                    ("NV007", "Mai Thanh Long", "long.mt", "TELLER", "", "ACTIVE"),
                    ("NV019", "Lâm Gia Yến", "yen.lg@bank.vn", "TELLER", "NV099", "ACTIVE")),
                CancellationToken.None));

        Assert.Equal("DATA_IMPORT_HAS_ERRORS", exception.ErrorCode);
        Assert.Single(db.Employees);
    }

    [Fact]
    public async Task PreviewAndCommit_BadEmailDuplicateEmailAndExistingEmailCollision_FlagsExpectedRows()
    {
        var (_, service) = Create(); // seeds NV888 with email/username unrelated@bank.local

        using var file = Workbook(
            ("NV001", "Nguyễn Văn An", "an.nv@bank.vn", "TELLER", "", "ACTIVE"),
            ("NV002", "Trần Thị Bình", "not-an-email", "TELLER", "", "ACTIVE"),
            ("NV003", "Lê Hoàng Cường", "AN.NV@BANK.VN", "TELLER", "", "ACTIVE"),
            ("NV004", "Phạm Minh Dũng", "unrelated@bank.local", "TELLER", "", "ACTIVE"));

        var preview = await service.PreviewAsync("emails.xlsx", file, CancellationToken.None);

        Assert.Equal(1, preview.Summary.Valid);
        Assert.Equal(3, preview.Summary.Invalid);
        Assert.Contains(preview.Rows[1].Errors, e => e.Column == "Email*" && e.Code == ErrorCodes.DataImportInvalidValue);
        Assert.Contains(preview.Rows[2].Errors, e => e.Column == "Email*" && e.Code == ErrorCodes.DataImportDuplicate && e.Message.Contains("dòng 2", StringComparison.Ordinal));
        Assert.Contains(preview.Rows[3].Errors, e => e.Column == "Email*" && e.Code == ErrorCodes.DataImportDuplicate && e.Message.Contains("nhân viên khác", StringComparison.Ordinal));

        file.Position = 0;
        var commit = await service.CommitAsync(file, CancellationToken.None);
        Assert.Equal(1, commit.Created);
        Assert.Equal(3, commit.Skipped);
    }

    [Fact]
    public async Task PreviewAndCommit_ManagerExistsOnlyOnInvalidRow_CascadesToSubordinates()
    {
        var (_, service) = Create(seedUnrelatedEmployee: false);

        using var file = Workbook(
            ("NV010", "Quản lý lỗi email", "bad-email", "DEPT_MANAGER", "", "ACTIVE"),
            ("NV001", "Nhân viên cấp 1", "nv001@bank.vn", "TELLER", "NV010", "ACTIVE"),
            ("NV002", "Nhân viên cấp 2", "nv002@bank.vn", "TELLER", "NV001", "ACTIVE"),
            ("NV003", "Nhân viên độc lập", "nv003@bank.vn", "TELLER", "", "ACTIVE"));

        var preview = await service.PreviewAsync("cascade.xlsx", file, CancellationToken.None);

        Assert.Equal(1, preview.Summary.Valid);
        Assert.Equal(3, preview.Summary.Invalid);
        Assert.Contains(preview.Rows[1].Errors, e => e.Column == "Mã quản lý trực tiếp" && e.Code == ErrorCodes.DataImportReferenceNotFound);
        Assert.Contains(preview.Rows[2].Errors, e => e.Column == "Mã quản lý trực tiếp" && e.Code == ErrorCodes.DataImportReferenceNotFound);
        Assert.Equal("CREATE", preview.Rows[3].Action);

        file.Position = 0;
        var commit = await service.CommitAsync(file, CancellationToken.None);
        Assert.Equal(1, commit.Created);
        Assert.Equal(3, commit.Skipped);
    }

    [Fact]
    public async Task CommitAsync_NewEmployees_CreatesAccountsWithEmailAsUsernameAndRoles()
    {
        var (db, service) = Create();

        var result = await service.CommitAsync(
            Workbook(
                ("NV001", "Nguyễn Văn An", "An.Nguyen@Bank.Local", "BRANCH_MANAGER", "", "ACTIVE"),
                ("NV002", "Trần Thị Bình", "binh.tran@bank.local", "TELLER", "NV001", "ACTIVE")),
            CancellationToken.None);

        Assert.Equal(2, result.Created);
        Assert.Equal(0, result.Updated);
        Assert.Equal(0, result.Skipped);
        Assert.Empty(result.Errors);
        Assert.All(result.NewAccounts, a => Assert.False(string.IsNullOrEmpty(a.TemporaryPassword)));

        var manager = db.Employees.Single(e => e.EmployeeCode == "NV001");
        var employee = db.Employees.Single(e => e.EmployeeCode == "NV002");
        Assert.Equal(manager.Id, employee.ManagerId);

        var account = db.Accounts.Single(a => a.EmployeeId == manager.Id);
        Assert.Equal("an.nguyen@bank.local", account.Username);
        Assert.False(account.IsLocked);
        Assert.Single(db.UserRoles.Where(ur => ur.AccountId == account.Id));

        var dept = db.Departments.Single(d => d.Code == "PGD-HK");
        Assert.Equal(db.Departments.Single(d => d.Code == "HN01").Id, dept.ParentDepartmentId);
        Assert.Equal(dept.Id, employee.DepartmentId);
    }

    [Fact]
    public async Task CommitAsync_TerminatedEmployee_LocksTheNewAccount()
    {
        var (db, service) = Create();

        await service.CommitAsync(
            Workbook(("NV003", "Lê Văn Cường", "cuong.le@bank.local", "TELLER", "", "TERMINATED")), CancellationToken.None);

        var employee = db.Employees.Single(e => e.EmployeeCode == "NV003");
        Assert.Equal(EmployeeStatus.Terminated, employee.Status);
        Assert.True(db.Accounts.Single(a => a.EmployeeId == employee.Id).IsLocked);
    }

    [Fact]
    public async Task CommitAsync_EmployeeAbsentFromFile_IsNeverTouched()
    {
        var (db, service) = Create();

        await service.CommitAsync(
            Workbook(("NV004", "Phạm Thị Dung", "dung.pham@bank.local", "TELLER", "", "ACTIVE")), CancellationToken.None);

        var untouched = db.Employees.Single(e => e.EmployeeCode == "NV888");
        Assert.Equal("Người không trong file", untouched.FullName);
        Assert.Equal(EmployeeStatus.Active, untouched.Status);
    }

    [Fact]
    public async Task CommitAsync_ExistingEmployee_PersistsChangesWhenNothingIsTrackedYet()
    {
        var (db, service) = Create();
        db.ChangeTracker.Clear(); // like a real request: the entities are loaded by the service itself

        await service.CommitAsync(
            Workbook(("NV888", "Tên mới", "moi@bank.local", "SALES_OFFICER", "", "ON_LEAVE")), CancellationToken.None);

        var saved = await db.Employees.AsNoTracking().SingleAsync(e => e.EmployeeCode == "NV888");
        Assert.Equal("Tên mới", saved.FullName);
        Assert.Equal("moi@bank.local", saved.Email);
        Assert.Equal(EmployeeStatus.OnLeave, saved.Status);
    }

    [Fact]
    public async Task CommitAsync_ReimportingSameFile_UpdatesWithoutCreatingAnotherAccount()
    {
        var (db, service) = Create(seedUnrelatedEmployee: false);
        var row = ("NV005", "Ngô Thị Kim", "kim.ngo@bank.local", "TELLER", "", "ACTIVE");
        await service.CommitAsync(Workbook(row), CancellationToken.None);

        var second = await service.CommitAsync(Workbook(row), CancellationToken.None);

        Assert.Equal(0, second.Created);
        Assert.Equal(1, second.Updated);
        Assert.Empty(second.NewAccounts);
        Assert.Single(db.Accounts);
    }

    [Fact]
    public void GetTemplate_HasTheTwelveColumnsAndNoHireDate()
    {
        var (_, service) = Create();

        var sheet = new ClosedXmlSheetReader().Read(new MemoryStream(service.GetTemplate()), 10);

        Assert.Equal(12, sheet.Headers.Count);
        Assert.Contains("Mã CN cha", sheet.Headers);
        Assert.DoesNotContain(sheet.Headers, h => h.Contains("Ngày vào làm"));
    }
}
