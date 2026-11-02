using G54.BLL.Common.Import.Reading;
using G54.BLL.Services.Import.EmployeeOrganization;
using G54.DAL;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace G54.UnitTests.Import;

public sealed class EmployeeOrgCommitterTests
{
    private static readonly string[] Headers =
    [
        "Mã chi nhánh*", "Tên chi nhánh*", "Mã CN cha", "Mã phòng ban*", "Tên phòng ban*", "Đơn vị cha",
        "Mã NV*", "Họ tên*", "Email*", "Vị trí*", "Mã quản lý trực tiếp", "Trạng thái*", "Ngày vào làm*",
    ];

    private static (AppDbContext Db, EmployeeOrgImportDefinition Definition, Department ExistingBranch) CreateWithSeed()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new AppDbContext(options);

        string[] positionCodes = ["TELLER", "SALES", "DEPT_MANAGER", "BRANCH_MANAGER", "QA", "HR_OFFICER", "SYS_ADMIN"];
        foreach (var code in positionCodes)
        {
            db.JobPositions.Add(new JobPosition { Id = Guid.NewGuid(), Code = code, Title = code, Status = RecordStatus.Active });
        }

        string[] roleCodes = ["EMPLOYEE", "DEPT_MANAGER", "BRANCH_MANAGER", "VERIFIER", "HR_OFFICER", "SYSTEM_ADMIN"];
        foreach (var code in roleCodes)
        {
            db.Roles.Add(new Role { Id = Guid.NewGuid(), Code = code, Name = code, Status = RecordStatus.Active });
        }

        var branch = new Department { Id = Guid.NewGuid(), Code = "HN01", Name = "CN Hà Nội", LevelType = DepartmentLevelType.Branch, Status = RecordStatus.Active };
        db.Departments.Add(branch);

        // An employee that the import file never mentions: BR-52 says it must stay untouched.
        var untouchedEmployee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "NV999",
            FullName = "Người không trong file",
            Email = "unrelated@bank.local",
            DepartmentId = branch.Id,
            JobPositionId = db.JobPositions.Local.First(j => j.Code == "TELLER").Id,
            Status = EmployeeStatus.Active,
        };
        db.Employees.Add(untouchedEmployee);
        db.SaveChanges();

        var uow = new UnitOfWork(db);
        var committer = new EmployeeOrgCommitter(uow, db, new PlainPasswordEncoder());
        return (db, new EmployeeOrgImportDefinition(uow, db, committer), branch);
    }

    private static ExcelSheetResult Sheet(params (string Code, string Name, string Email, string Position, string Manager, string Status, string Date)[] employees)
    {
        var rows = employees.Select((e, i) => new RawRow(i + 2,
        [
            "HN01", "CN Hà Nội", "", "PGD-HK", "Phòng giao dịch Hoàng Kiếm", "",
            e.Code, e.Name, e.Email, e.Position, e.Manager, e.Status, e.Date,
        ])).ToList();
        return new ExcelSheetResult(Headers, rows);
    }

    [Fact]
    public async Task CommitAsync_NewEmployees_CreatesAccountsWithRolesAndTemporaryPasswords()
    {
        var (db, definition, _) = CreateWithSeed();
        var sheet = Sheet(
            ("NV001", "Nguyễn Văn An", "an.nguyen@bank.local", "BRANCH_MANAGER", "", "ACTIVE", "01/01/2020"),
            ("NV002", "Trần Thị Bình", "binh.tran@bank.local", "TELLER", "NV001", "ACTIVE", "01/02/2020"));

        var result = await definition.CommitAsync(sheet, CancellationToken.None);

        Assert.Equal(2, result.Created);
        Assert.Equal(2, result.NewAccounts.Count);
        Assert.All(result.NewAccounts, a => Assert.False(string.IsNullOrEmpty(a.TemporaryPassword)));

        var employee = db.Employees.Single(e => e.EmployeeCode == "NV002");
        var manager = db.Employees.Single(e => e.EmployeeCode == "NV001");
        Assert.Equal(manager.Id, employee.ManagerId);

        var account = db.Accounts.Single(a => a.EmployeeId == employee.Id);
        Assert.False(account.IsLocked);
        Assert.Single(db.UserRoles.Where(ur => ur.AccountId == account.Id));
    }

    [Fact]
    public async Task CommitAsync_TerminatedEmployee_LocksTheNewAccount()
    {
        var (db, definition, _) = CreateWithSeed();
        var sheet = Sheet(("NV003", "Lê Văn Cường", "cuong.le@bank.local", "TELLER", "", "TERMINATED", "01/01/2020"));

        await definition.CommitAsync(sheet, CancellationToken.None);

        var employee = db.Employees.Single(e => e.EmployeeCode == "NV003");
        Assert.Equal(EmployeeStatus.Terminated, employee.Status);
        var account = db.Accounts.Single(a => a.EmployeeId == employee.Id);
        Assert.True(account.IsLocked);
    }

    [Fact]
    public async Task CommitAsync_EmployeeAbsentFromFile_IsNeverTouched()
    {
        var (db, definition, _) = CreateWithSeed();
        var sheet = Sheet(("NV004", "Phạm Thị Dung", "dung.pham@bank.local", "TELLER", "", "ACTIVE", "01/01/2020"));

        await definition.CommitAsync(sheet, CancellationToken.None);

        var untouched = db.Employees.Single(e => e.EmployeeCode == "NV999");
        Assert.Equal("Người không trong file", untouched.FullName);
        Assert.Equal(EmployeeStatus.Active, untouched.Status);
    }

    [Fact]
    public async Task CommitAsync_ReimportingSameRow_ClassifiesUnchanged()
    {
        var (_, definition, _) = CreateWithSeed();
        var sheet = Sheet(("NV005", "Ngô Thị Kim", "kim.ngo@bank.local", "TELLER", "", "ACTIVE", "01/01/2020"));
        await definition.CommitAsync(sheet, CancellationToken.None);

        var preview = await definition.PreviewAsync(sheet, CancellationToken.None);

        Assert.Equal("UNCHANGED", preview.Rows.Single().Action);
    }
}
