using System.Net;
using System.Text.Json;
using G54.DAL.Entities;
using G54.UnitTests.Import;
using Microsoft.EntityFrameworkCore;
using static G54.UnitTests.Integration.Import.XlsxBuilder;

namespace G54.UnitTests.Integration.Import;

/// <summary>
/// Employee / organization import (<c>/data-imports/employees/*</c>) over HTTP against PostgreSQL.
/// Plan: docs/plans/IMPORT_INTEGRATION_TEST_PLAN.md section A. Files built in a test use codes with a per-test suffix, so
/// tests do not depend on each other; the tests on the real sample are written to pass in any order.
/// </summary>
[Trait("Category", "Integration")]
public sealed class EmployeeImportIntegrationTests(ImportApiFixture api) : IClassFixture<ImportApiFixture>
{
    private const string Admin = "NV013";
    private const string Staff = "NV004";
    private const string Preview = "/data-imports/employees/preview";
    private const string Commit = "/data-imports/employees/commit";
    private static readonly string[] SampleDepartments = [EmployeeOrgSampleWorkbook.BranchCode, .. EmployeeOrgSampleWorkbook.DepartmentCodes];
    private static readonly string[] SampleEmployees = ["NV001", "NV005", "NV006", "NV010", "NV014", "NV015", "NV007"];

    private static byte[] Sample => SampleFiles.Read(SampleFiles.EmployeeOrg);

    private static string NewSuffix() => Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    // A1
    [ImportIntegrationFact]
    public async Task A1_PreviewSample_Reports21Total17Valid4InvalidAndWritesNothing()
    {
        var before = await api.QueryAsync(db => db.Employees.CountAsync());

        var result = await api.PostFileAsync(Preview, Sample, Admin, "02_Nhan_su_To_chuc.xlsx");

        api.AssertStatus(result, HttpStatusCode.OK);
        var summary = result.Data.GetProperty("summary");
        Assert.Equal(21, summary.GetProperty("totalRows").GetInt32());
        Assert.Equal(17, summary.GetProperty("valid").GetInt32());
        Assert.Equal(4, summary.GetProperty("invalid").GetInt32());

        var invalid = result.Data.GetProperty("rows").EnumerateArray()
            .Where(r => r.GetProperty("action").GetString() == "INVALID")
            .Select(r => (Row: r.GetProperty("rowNumber").GetInt32(), Code: r.GetProperty("data").GetProperty("Mã NV*").GetString()))
            .OrderBy(r => r.Row)
            .ToList();
        Assert.Equal(["NV007", "NV008", "NV005", "NV019"], invalid.Select(r => r.Code));
        Assert.Equal(21, invalid.Single(r => r.Code == "NV005").Row); // the first NV005 (row 9) is kept
        Assert.Equal(before, await api.QueryAsync(db => db.Employees.CountAsync()));
    }

    // A2 + A4
    [ImportIntegrationFact]
    public async Task A2_CommitSample_Writes17RowsBuildsOrgTreeLinksManagersAndLocksOnlyTerminated()
    {
        var result = await api.PostFileAsync(Commit, Sample, Admin, "02_Nhan_su_To_chuc.xlsx");

        api.AssertStatus(result, HttpStatusCode.OK);
        Assert.Equal(17, result.Data.GetProperty("created").GetInt32() + result.Data.GetProperty("updated").GetInt32());
        Assert.Equal(4, result.Data.GetProperty("skipped").GetInt32());
        Assert.Equal(4, result.Data.GetProperty("errors").GetArrayLength());

        await api.WithDbAsync(async db =>
        {
            var departments = await db.Departments.AsNoTracking()
                .Where(d => SampleDepartments.Contains(d.Code))
                .ToDictionaryAsync(d => d.Code);
            Assert.Equal(6, departments.Count);
            Assert.Equal(DepartmentLevelType.Branch, departments[EmployeeOrgSampleWorkbook.BranchCode].LevelType);
            Assert.Equal(departments[EmployeeOrgSampleWorkbook.BranchCode].Id, departments["PGD-T1"].ParentDepartmentId);

            var employees = await db.Employees.AsNoTracking().Include(e => e.Account)
                .Where(e => SampleEmployees.Contains(e.EmployeeCode))
                .ToDictionaryAsync(e => e.EmployeeCode);
            Assert.Equal(employees["NV010"].Id, employees["NV001"].ManagerId);
            Assert.Equal(EmployeeOrgSampleWorkbook.EmailOf("NV001"), employees["NV001"].Email);
            Assert.Equal(EmployeeOrgSampleWorkbook.NameOf("NV005"), employees["NV005"].FullName); // first NV005 row kept, duplicate row 21 skipped
            Assert.Equal(EmployeeStatus.Terminated, employees["NV006"].Status);
            Assert.True(employees["NV006"].Account!.IsLocked);
            Assert.Equal(EmployeeStatus.OnLeave, employees["NV015"].Status);
            Assert.False(employees["NV015"].Account?.IsLocked ?? false);
            Assert.Equal(EmployeeStatus.PendingResignation, employees["NV014"].Status);
            Assert.False(employees["NV014"].Account?.IsLocked ?? false);
            Assert.NotEqual("nv007.example.test", employees["NV007"].Email); // the bad-email row was skipped
        });
    }

    // A3
    [ImportIntegrationFact]
    public async Task A3_NewEmployees_GetAnAccountWithRoleAndCanLogInWithTheTemporaryPassword()
    {
        var s = NewSuffix();
        var file = Employees(
            Emp($"M{s}", "Quản lý Tích hợp", $"m{s}@it.test".ToLowerInvariant(), "DEPT_MANAGER", dept: $"D{s}"),
            Emp($"T{s}", "Giao dịch viên Tích hợp", $"t{s}@it.test".ToLowerInvariant(), "TELLER", manager: $"M{s}", dept: $"D{s}"));

        var result = await api.PostFileAsync(Commit, file, Admin);

        api.AssertStatus(result, HttpStatusCode.OK);
        Assert.Equal(2, result.Data.GetProperty("created").GetInt32());
        var accounts = result.Data.GetProperty("newAccounts").EnumerateArray().ToDictionary(a => a.GetProperty("employeeCode").GetString()!);
        Assert.Equal($"t{s}@it.test".ToLowerInvariant(), accounts[$"T{s}"].GetProperty("username").GetString());

        var roles = await api.QueryAsync(db => db.UserRoles.AsNoTracking()
            .Where(ur => ur.Account.Employee.EmployeeCode == $"M{s}" || ur.Account.Employee.EmployeeCode == $"T{s}")
            .Select(ur => new { ur.Account.Employee.EmployeeCode, ur.Role.Code })
            .ToListAsync());
        Assert.Equal("DEPT_MANAGER", roles.Single(r => r.EmployeeCode == $"M{s}").Code);
        Assert.Equal("EMPLOYEE", roles.Single(r => r.EmployeeCode == $"T{s}").Code);

        var login = await api.SendAsync(HttpMethod.Post, "/auth/login", null, new
        {
            email = $"t{s}@it.test".ToLowerInvariant(),
            password = accounts[$"T{s}"].GetProperty("temporaryPassword").GetString(),
            rememberMe = false,
        });
        api.AssertStatus(login, HttpStatusCode.OK);
    }

    // A5
    [ImportIntegrationFact]
    public async Task A5_RecommitSameFile_UpdatesOnlyAndCreatesNoAccountOrDepartment()
    {
        var s = NewSuffix();
        var file = Employees(Emp($"R{s}", "Nhân viên Lặp", $"r{s}@it.test".ToLowerInvariant(), dept: $"D{s}"));
        api.AssertStatus(await api.PostFileAsync(Commit, file, Admin), HttpStatusCode.OK);

        var again = await api.PostFileAsync(Commit, file, Admin);

        api.AssertStatus(again, HttpStatusCode.OK);
        Assert.Equal(0, again.Data.GetProperty("created").GetInt32());
        Assert.Equal(1, again.Data.GetProperty("updated").GetInt32());
        Assert.Equal(0, again.Data.GetProperty("newAccounts").GetArrayLength());
        Assert.Equal(1, await api.QueryAsync(db => db.Accounts.CountAsync(a => a.Employee.EmployeeCode == $"R{s}")));
        Assert.Equal(1, await api.QueryAsync(db => db.Departments.CountAsync(d => d.Code == $"D{s}")));
    }

    // A6: the email/username lookup uses lower() on PostgreSQL
    [ImportIntegrationFact]
    public async Task A6_EmailOfAnotherExistingEmployee_IsFlaggedIgnoringCase()
    {
        var takenEmail = await api.QueryAsync(db => db.Employees.AsNoTracking()
            .Where(e => e.EmployeeCode == Staff).Select(e => e.Email!).SingleAsync());
        var s = NewSuffix();
        var file = Employees(Emp($"E{s}", "Trùng Email", takenEmail.ToUpperInvariant(), dept: $"D{s}"));

        var result = await api.PostFileAsync(Preview, file, Admin);

        api.AssertStatus(result, HttpStatusCode.OK);
        var row = Assert.Single(result.Data.GetProperty("rows").EnumerateArray());
        Assert.Equal("INVALID", row.GetProperty("action").GetString());
        var error = Assert.Single(row.GetProperty("errors").EnumerateArray());
        Assert.Equal("Email*", error.GetProperty("column").GetString());
        Assert.Equal("DATA_IMPORT_DUPLICATE", error.GetProperty("code").GetString());
    }

    // A7
    [ImportIntegrationFact]
    public async Task A7_CommitWithZeroValidRows_Returns422AndWritesNothing()
    {
        var s = NewSuffix();
        var file = Employees(
            Emp($"X{s}", "Email Sai", "not-an-email", dept: $"D{s}"),
            Emp($"Y{s}", "Vị trí Sai", $"y{s}@it.test".ToLowerInvariant(), "CREDIT_OFFICER", dept: $"D{s}"));

        var result = await api.PostFileAsync(Commit, file, Admin);

        api.AssertStatus(result, HttpStatusCode.UnprocessableEntity);
        Assert.Equal("DATA_IMPORT_HAS_ERRORS", result.ErrorCode);
        Assert.False(await api.QueryAsync(db => db.Departments.AnyAsync(d => d.Code == $"D{s}")));
        Assert.False(await api.QueryAsync(db => db.Employees.AnyAsync(e => e.EmployeeCode == $"X{s}" || e.EmployeeCode == $"Y{s}")));
    }

    // A8: two commits of the same new rows race on the unique indexes; nothing is half written.
    [ImportIntegrationFact]
    public async Task A8_ConcurrentCommitsOfTheSameNewRows_LeaveExactlyOneEmployeeAndAccountEach()
    {
        var s = NewSuffix();
        var file = Employees(
            Emp($"C{s}", "Song Song 1", $"c{s}@it.test".ToLowerInvariant(), dept: $"D{s}"),
            Emp($"K{s}", "Song Song 2", $"k{s}@it.test".ToLowerInvariant(), dept: $"D{s}"));

        var results = await Task.WhenAll(
            api.PostFileAsync(Commit, file, Admin),
            api.PostFileAsync(Commit, file, Admin),
            api.PostFileAsync(Commit, file, Admin));

        Assert.Contains(results, r => r.Status == HttpStatusCode.OK);
        Assert.All(results, r => Assert.True(
            r.Status == HttpStatusCode.OK || (r.Status == HttpStatusCode.Conflict && r.ErrorCode == "DATA_IMPORT_CONFLICT"),
            $"Unexpected {(int)r.Status}: {r.Root}"));
        foreach (var code in new[] { $"C{s}", $"K{s}" })
        {
            Assert.Equal(1, await api.QueryAsync(db => db.Employees.CountAsync(e => e.EmployeeCode == code)));
            Assert.Equal(1, await api.QueryAsync(db => db.Accounts.CountAsync(a => a.Employee.EmployeeCode == code)));
        }

        Assert.Equal(1, await api.QueryAsync(db => db.Departments.CountAsync(d => d.Code == $"D{s}")));
    }

    // A9
    [ImportIntegrationFact]
    public async Task A9_FileChecks_MissingHeaderHeaderOnlyNonXlsxAndReorderedUnaccentedHeaders()
    {
        var s = NewSuffix();

        var missingHeader = Build(EmployeeHeaders.Where(h => h != "Email*").ToArray(), [.. Emp($"H{s}", "Thiếu", "x@it.test").Where((_, i) => i != 8)]);
        var missing = await api.PostFileAsync(Preview, missingHeader, Admin);
        api.AssertStatus(missing, HttpStatusCode.BadRequest);
        Assert.Contains("Email", missing.Root.GetProperty("message").GetString());

        var headerOnly = await api.PostFileAsync(Preview, Build(EmployeeHeaders), Admin);
        api.AssertStatus(headerOnly, HttpStatusCode.BadRequest);

        var notXlsx = await api.PostFileAsync(Preview, "a,b,c"u8.ToArray(), Admin, "employees.csv", "text/csv");
        api.AssertStatus(notXlsx, HttpStatusCode.BadRequest);

        var fakeXlsx = await api.PostFileAsync(Preview, "not a zip"u8.ToArray(), Admin, "employees.xlsx");
        api.AssertStatus(fakeXlsx, HttpStatusCode.BadRequest);

        // Columns reversed, headers without accents or "*".
        var plainHeaders = new[] { "Trang thai", "Ma quan ly truc tiep", "Vi tri", "Email", "Ho ten", "Ma NV", "Don vi cha", "Ten phong ban", "Ma phong ban", "Ma CN cha", "Ten chi nhanh", "Ma chi nhanh" };
        var reversed = Build(plainHeaders, [.. Emp($"P{s}", "Không Dấu", $"p{s}@it.test".ToLowerInvariant(), dept: $"D{s}").Reverse()]);
        var accepted = await api.PostFileAsync(Preview, reversed, Admin);
        api.AssertStatus(accepted, HttpStatusCode.OK);
        Assert.Equal(1, accepted.Data.GetProperty("summary").GetProperty("valid").GetInt32());
    }

    // A10
    [ImportIntegrationFact]
    public async Task A10_Auth_AnonymousIs401EmployeeIs403AdminGetsTheTemplate()
    {
        var file = Employees(Emp($"Z{NewSuffix()}", "Phân Quyền", "auth@it.test"));
        api.AssertStatus(await api.PostFileAsync(Preview, file, null), HttpStatusCode.Unauthorized);
        api.AssertStatus(await api.PostFileAsync(Preview, file, Staff), HttpStatusCode.Forbidden);
        api.AssertStatus(await api.PostFileAsync(Commit, file, Staff), HttpStatusCode.Forbidden);

        using var template = await api.GetRawAsync("/data-imports/employees/template", Admin);
        Assert.Equal(HttpStatusCode.OK, template.StatusCode);
        Assert.Equal(ImportApiFixture.XlsxContentType, template.Content.Headers.ContentType?.MediaType);
        Assert.NotEmpty(await template.Content.ReadAsByteArrayAsync());
    }
}
