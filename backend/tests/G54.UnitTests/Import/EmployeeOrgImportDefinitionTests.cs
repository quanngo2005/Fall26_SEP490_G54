using G54.BLL.Common.Import.Reading;
using G54.BLL.Services.Import.EmployeeOrganization;
using G54.DAL;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace G54.UnitTests.Import;

public sealed class EmployeeOrgImportDefinitionTests
{
    private static (AppDbContext Db, EmployeeOrgImportDefinition Definition) CreateDefinition()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new AppDbContext(options);
        SeedPositionsAndRoles(db);
        db.SaveChanges();

        var uow = new UnitOfWork(db);
        var committer = new EmployeeOrgCommitter(uow, db, new PlainPasswordEncoder());
        return (db, new EmployeeOrgImportDefinition(uow, db, committer));
    }

    private static void SeedPositionsAndRoles(AppDbContext db)
    {
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
    }

    private static ExcelSheetResult ReadSample()
    {
        using var stream = EmployeeOrgSampleWorkbook.Build();
        return new ClosedXmlSheetReader().Read(stream, 5000);
    }

    [Fact]
    public async Task PreviewAsync_SampleWorkbook_FlagsExactlyTheFiveInvalidRows()
    {
        var (_, definition) = CreateDefinition();

        var result = await definition.PreviewAsync(ReadSample(), CancellationToken.None);

        var invalidRowNumbers = result.Rows.Where(r => r.Action == "INVALID").Select(r => r.RowNumber).OrderBy(n => n).ToList();
        Assert.Equal(EmployeeOrgSampleWorkbook.InvalidRowNumbers, invalidRowNumbers);
        Assert.Equal(5, result.Summary.Invalid);
        Assert.Equal(result.Rows.Count - 5, result.Summary.Valid);
    }

    [Fact]
    public async Task PreviewAsync_SampleWorkbook_DuplicateRowsBothCarryDuplicateError()
    {
        var (_, definition) = CreateDefinition();

        var result = await definition.PreviewAsync(ReadSample(), CancellationToken.None);

        var row9 = result.Rows.Single(r => r.RowNumber == 9);
        var row21 = result.Rows.Single(r => r.RowNumber == 21);
        Assert.Contains(row9.Errors, e => e.Code == "DATA_IMPORT_DUPLICATE_CODE");
        Assert.Contains(row21.Errors, e => e.Code == "DATA_IMPORT_DUPLICATE_CODE");
    }

    [Fact]
    public async Task PreviewAsync_SampleWorkbook_ValidRowsAreClassifiedCreate()
    {
        var (_, definition) = CreateDefinition();

        var result = await definition.PreviewAsync(ReadSample(), CancellationToken.None);

        Assert.All(result.Rows.Where(r => r.Action != "INVALID"), r => Assert.Equal("CREATE", r.Action));
    }

    [Fact]
    public async Task PreviewAsync_SampleWorkbook_OrganizationRollupHasOneBranchAndFiveDepartments()
    {
        var (_, definition) = CreateDefinition();

        var result = await definition.PreviewAsync(ReadSample(), CancellationToken.None);

        var organizations = result.RelatedEntities;
        Assert.Single(organizations, o => o.Kind == "BRANCH" && o.Code == "HN01");
        var departments = organizations.Where(o => o.Kind == "DEPARTMENT").Select(o => o.Code).ToHashSet();
        Assert.Equal(EmployeeOrgSampleWorkbook.DepartmentCodes.ToHashSet(), departments);
    }

    [Fact]
    public async Task CommitAsync_SampleWorkbookHasErrors_ThrowsDataImportHasErrors()
    {
        var (_, definition) = CreateDefinition();

        var exception = await Assert.ThrowsAsync<G54.BLL.Common.Exceptions.BusinessRuleException>(
            () => definition.CommitAsync(ReadSample(), CancellationToken.None));

        Assert.Equal("DATA_IMPORT_HAS_ERRORS", exception.ErrorCode);
        Assert.Equal(422, exception.StatusCode);
    }

    private static Employee SeedOtherEmployee(AppDbContext db, string code, string email)
    {
        var department = new Department
        {
            Id = Guid.NewGuid(),
            Code = "OTHER-DEPT",
            Name = "Other",
            LevelType = DepartmentLevelType.Branch,
            Status = RecordStatus.Active,
        };
        db.Departments.Add(department);
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = code,
            FullName = "Other",
            Email = email,
            DepartmentId = department.Id,
            JobPositionId = db.JobPositions.Local.First().Id,
            Status = EmployeeStatus.Active,
        };
        db.Employees.Add(employee);
        return employee;
    }

    [Fact]
    public async Task PreviewAsync_EmailOwnedByAnotherEmployeeInDb_FlagsRow()
    {
        var (db, definition) = CreateDefinition();
        SeedOtherEmployee(db, "NV900", "an.nguyen@bank.local");
        await db.SaveChangesAsync();

        var result = await definition.PreviewAsync(ReadSample(), CancellationToken.None);

        var row = result.Rows.Single(r => r.RowNumber == 2);
        Assert.Equal("INVALID", row.Action);
        Assert.Contains(row.Errors, e => e.Code == "DATA_IMPORT_EMAIL_IN_USE");
    }

    [Fact]
    public async Task PreviewAsync_EmailUsedAsAnotherAccountsUsername_FlagsRow()
    {
        var (db, definition) = CreateDefinition();
        var other = SeedOtherEmployee(db, "NV900", "other@bank.local");
        db.Accounts.Add(new Account { Id = Guid.NewGuid(), EmployeeId = other.Id, Username = "An.Nguyen@bank.local", PasswordHash = "x" });
        await db.SaveChangesAsync();

        var result = await definition.PreviewAsync(ReadSample(), CancellationToken.None);

        var row = result.Rows.Single(r => r.RowNumber == 2);
        Assert.Equal("INVALID", row.Action);
        Assert.Contains(row.Errors, e => e.Code == "DATA_IMPORT_EMAIL_IN_USE");
    }

    [Fact]
    public async Task PreviewAsync_StatusOutsideTemplateValues_FlagsRow()
    {
        var (_, definition) = CreateDefinition();
        var sample = ReadSample();
        var statusColumn = sample.Headers.ToList().IndexOf("Trạng thái*");
        var cells = sample.Rows[0].Cells.ToArray();
        cells[statusColumn] = "INACTIVE";
        var sheet = sample with { Rows = [sample.Rows[0] with { Cells = cells }, .. sample.Rows.Skip(1)] };

        var result = await definition.PreviewAsync(sheet, CancellationToken.None);

        var row = result.Rows.Single(r => r.RowNumber == sample.Rows[0].RowNumber);
        Assert.Contains(row.Errors, e => e.Code == "DATA_IMPORT_INVALID_STATUS");
    }

    [Fact]
    public void Read_NonZipContent_ThrowsValidationException()
    {
        using var stream = new MemoryStream("PK\u0003\u0004 not really a zip"u8.ToArray());

        Assert.Throws<G54.BLL.Common.Exceptions.ValidationException>(() => new ClosedXmlSheetReader().Read(stream, 5000));
    }
}
