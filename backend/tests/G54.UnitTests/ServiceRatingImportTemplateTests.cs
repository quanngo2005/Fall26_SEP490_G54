using System.Reflection;
using ClosedXML.Excel;
using G54.Api.Authorization;
using G54.Api.Controllers;
using G54.BLL.Common;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Import.Reading;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using G54.DAL.Migrations;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Options;

namespace G54.UnitTests;

public sealed class ServiceRatingImportTemplateTests
{
    private static readonly string[] ExpectedHeaders =
        ["Ngày đánh giá*", "Mã NV*", "Mã giao dịch*", "Điểm (1–5)*", "Nhận xét KH", "Ghi chú (demo)"];

    [Fact]
    public void Template_HasTheRatingSheetWithHeadersSampleRowAndNotes()
    {
        using var workbook = new XLWorkbook(new MemoryStream(new ServiceRatingImportTemplateProvider().GetTemplate()));

        var sheet = workbook.Worksheets.First();
        Assert.Equal("Đánh giá dịch vụ", sheet.Name);
        Assert.Equal(ExpectedHeaders, Enumerable.Range(1, ExpectedHeaders.Length).Select(c => sheet.Cell(1, c).GetString()));
        Assert.Equal([true, true, true, true, false, false], ServiceRatingImportTemplateProvider.Template.Columns.Select(c => c.Required));
        Assert.Equal(5, sheet.Cell(2, 4).GetValue<int>());
        Assert.Equal(ExpectedHeaders, Enumerable.Range(2, ExpectedHeaders.Length).Select(r => workbook.Worksheet("Notes").Cell(r, 1).GetString()));
    }

    [Fact]
    public async Task Template_IsAcceptedByTheImporter()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Employees.Add(new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "NV001",
            FullName = "A",
            Email = "a@x.vn",
            DepartmentId = Guid.NewGuid(),
            JobPositionId = Guid.NewGuid(),
            Status = EmployeeStatus.Active,
        });
        await db.SaveChangesAsync();
        var service = new ServiceRatingImportService(
            new UnitOfWork(db), db, TimeProvider.System, new ClosedXmlSheetReader(), Options.Create(new SystemParametersOptions()));

        var batch = await service.ImportAsync("t.xlsx", new MemoryStream(new ServiceRatingImportTemplateProvider().GetTemplate()), Guid.NewGuid());

        Assert.Equal((1, 1, 0), (batch.TotalRows, batch.ImportedRows, batch.ErrorRows));
        Assert.Equal(5, (await db.ServiceRatings.SingleAsync()).Score);
    }

    [Fact]
    public void Controller_RequiresServiceRatingImportManageAtAllScope()
    {
        var permission = typeof(ServiceRatingImportsController).GetCustomAttribute<HasPermissionAttribute>();

        Assert.NotNull(permission);
        Assert.Equal(("service_rating_import", "manage", PermissionScopes.All), (permission.Resource, permission.Action, permission.Scope));
        Assert.NotEmpty(typeof(ServiceRatingImportsController).GetCustomAttributes<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>());
    }

    [Fact]
    public void Migration_SeedsThePermissionForTheTransactionImportRolesAndRemovesItOnDown()
    {
        var migration = new AddServiceRatingImport();

        var up = string.Join("\n", migration.UpOperations.OfType<SqlOperation>().Select(op => op.Sql));
        var down = string.Join("\n", migration.DownOperations.OfType<SqlOperation>().Select(op => op.Sql));

        Assert.Contains("'SERVICE_RATING_IMPORT_MANAGE', 'Import đánh giá dịch vụ', 'service_rating_import', 'manage'", up, StringComparison.Ordinal);
        Assert.Contains("'SYSTEM_ADMIN', 'DEPT_MANAGER', 'BRANCH_MANAGER', 'VERIFIER'", up, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(up, "ON CONFLICT DO NOTHING"));
        Assert.Contains("DELETE FROM permission WHERE code = 'SERVICE_RATING_IMPORT_MANAGE'", down, StringComparison.Ordinal);
        Assert.Contains(migration.UpOperations, op => op is AddColumnOperation { Table: "import_batch", Name: "file_type", DefaultValue: "TRANSACTION" });
        var table = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.Equal("service_rating", table.Name);
        Assert.Equal("score BETWEEN 1 AND 5", Assert.Single(table.CheckConstraints).Sql);
    }

    private static int CountOccurrences(string text, string value) => text.Split(value).Length - 1;
}
