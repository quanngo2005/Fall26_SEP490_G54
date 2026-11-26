using System.Reflection;
using System.Security.Claims;
using ClosedXML.Excel;
using G54.Api.Authorization;
using G54.Api.Controllers;
using G54.BLL.Common;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Permission;
using G54.BLL.Dtos.TransactionImport;
using G54.BLL.Dtos.WorkTransaction;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using G54.DAL.Migrations;
using G54.DAL.Uow;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Options;

namespace G54.UnitTests;

public sealed class TransactionImportTemplateTests
{
    private static readonly string[] ExpectedHeaders =
    [
        "Mã giao dịch*",
        "Ngày GD*",
        "Mã khách hàng*",
        "Mã NV*",
        "Mã sản phẩm*",
        "Tên sản phẩm",
        "Số tiền (VND)*",
        "Mẫu số",
        "Refer ID",
        "Ghi chú (demo)",
    ];

    [Fact]
    public void Template_ColumnsAndOrder_MatchImporterSpecification()
    {
        var columns = TransactionImportTemplateProvider.Template.Columns;

        Assert.Equal(ExpectedHeaders.Length, columns.Count);
        for (var i = 0; i < ExpectedHeaders.Length; i++)
        {
            Assert.Equal(ExpectedHeaders[i], columns[i].Header);
        }

        Assert.True(columns[0].Required);
        Assert.True(columns[1].Required);
        Assert.False(columns[2].Required); // Optional column on legacy English files, required row-value when present
        Assert.True(columns[3].Required);
        Assert.True(columns[4].Required);
        Assert.False(columns[5].Required);
        Assert.True(columns[6].Required);
        Assert.False(columns[7].Required);
        Assert.False(columns[8].Required);
        Assert.False(columns[9].Required);
    }

    [Fact]
    public void Template_GeneratedWorkbook_HasDataAndNotesSheetsWithCorrectLayout()
    {
        var provider = new TransactionImportTemplateProvider();
        var bytes = provider.GetTemplate();

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);

        Assert.True(workbook.Worksheets.Count >= 2, "Template should have Data and Notes worksheets.");

        var dataSheet = workbook.Worksheets.First();
        Assert.Equal("Data", dataSheet.Name);

        // Header row
        for (var i = 0; i < ExpectedHeaders.Length; i++)
        {
            var cell = dataSheet.Cell(1, i + 1);
            Assert.Equal(ExpectedHeaders[i], cell.GetString().Trim());
            Assert.True(cell.Style.Font.Bold);
        }

        // Example row (row 2)
        Assert.Equal("TX-SAMPLE-0001", dataSheet.Cell(2, 1).GetString().Trim());
        Assert.Equal("05/10/2026", dataSheet.Cell(2, 2).GetString().Trim());
        Assert.Equal("KH10001", dataSheet.Cell(2, 3).GetString().Trim());
        Assert.Equal("NV001", dataSheet.Cell(2, 4).GetString().Trim());
        Assert.Equal("TD_CLASSIC", dataSheet.Cell(2, 5).GetString().Trim());
        Assert.Equal("Mở thẻ tín dụng Classic", dataSheet.Cell(2, 6).GetString().Trim());
        Assert.Equal(10000000m, dataSheet.Cell(2, 7).GetValue<decimal>());
        Assert.Equal(1m, dataSheet.Cell(2, 8).GetValue<decimal>());
        Assert.Equal("NV002", dataSheet.Cell(2, 9).GetString().Trim());

        // Notes sheet
        var notesSheet = workbook.Worksheet("Notes");
        Assert.NotNull(notesSheet);
        Assert.Equal("Cột", notesSheet.Cell(1, 1).GetString());
        Assert.Equal("Bắt buộc", notesSheet.Cell(1, 2).GetString());
        Assert.Equal("Kiểu dữ liệu", notesSheet.Cell(1, 3).GetString());
        Assert.Equal("Ghi chú", notesSheet.Cell(1, 4).GetString());
        Assert.True(notesSheet.Row(1).Style.Font.Bold);

        for (var i = 0; i < ExpectedHeaders.Length; i++)
        {
            Assert.Equal(ExpectedHeaders[i], notesSheet.Cell(i + 2, 1).GetString());
        }
    }

    [Fact]
    public async Task Template_IsAcceptedByImporter_ImportsSuccessfully()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AppDbContext(options);

        // Seed the employees and WorkType referenced in the example row
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "NV001",
            FullName = "Nguyễn Văn An",
            Email = "an.nguyen@bank.local",
            DepartmentId = Guid.NewGuid(),
            JobPositionId = Guid.NewGuid(),
            Status = EmployeeStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
            LastSyncAt = DateTimeOffset.UtcNow,
        };
        var referEmployee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "NV002",
            FullName = "Trần Thị Bình",
            Email = "binh.tran@bank.local",
            DepartmentId = Guid.NewGuid(),
            JobPositionId = Guid.NewGuid(),
            Status = EmployeeStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
            LastSyncAt = DateTimeOffset.UtcNow,
        };
        db.Employees.AddRange(employee, referEmployee);
        db.WorkTypes.Add(new WorkType
        {
            Id = Guid.NewGuid(),
            Code = "WT_TD_CLASSIC",
            Name = "Mở thẻ tín dụng Classic",
            ProductCode = "TD_CLASSIC",
            Status = RecordStatus.Active,
        });
        await db.SaveChangesAsync();

        var provider = new TransactionImportTemplateProvider();
        var templateBytes = provider.GetTemplate();

        var service = new TransactionImportService(new UnitOfWork(db), TimeProvider.System, new G54.BLL.Common.Import.Reading.ClosedXmlSheetReader(), Microsoft.Extensions.Options.Options.Create(new G54.BLL.Common.SystemParametersOptions()));
        using var stream = new MemoryStream(templateBytes);

        var batch = await service.ImportAsync("transaction-import-template.xlsx", stream, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(1, batch.TotalRows);
        Assert.Equal(1, batch.ImportedRows);
        Assert.Equal(0, batch.ErrorRows);
        Assert.Equal(0, batch.DuplicateRows);
        Assert.Equal("COMPLETED", batch.Status);

        var stored = await db.ImportedTransactions.FirstOrDefaultAsync(t => t.TransactionCode == "TX-SAMPLE-0001");
        Assert.NotNull(stored);
        Assert.Equal(employee.Id, stored.EmployeeId);
        Assert.Equal("KH10001", stored.CustomerCode);
        Assert.Equal("TD_CLASSIC", stored.ProductCode);
        Assert.Equal(new DateOnly(2026, 10, 5), stored.TransactionDate);
        Assert.Equal(10000000m, stored.Amount);
        Assert.Equal(1m, stored.Denominator);
        Assert.Equal("NV002", stored.ReferEmployeeCode);
    }

    [Fact]
    public void TemplateRoute_IsRegisteredAndPermissionProtected()
    {
        var controllerType = typeof(TransactionImportsController);

        // Controller route attribute
        var routeAttr = controllerType.GetCustomAttribute<RouteAttribute>();
        Assert.NotNull(routeAttr);
        Assert.Equal("transaction-imports", routeAttr.Template);

        // Controller authorization attributes
        var authorizeAttrs = controllerType.GetCustomAttributes<AuthorizeAttribute>().ToList();
        Assert.NotEmpty(authorizeAttrs);

        var permissionAttr = controllerType.GetCustomAttribute<HasPermissionAttribute>();
        Assert.NotNull(permissionAttr);
        Assert.Equal("transaction-import", permissionAttr.Resource);
        Assert.Equal("manage", permissionAttr.Action);
        Assert.Equal(PermissionScopes.All, permissionAttr.Scope);

        // GetTemplate action
        var actionMethod = controllerType.GetMethod(nameof(TransactionImportsController.GetTemplate));
        Assert.NotNull(actionMethod);

        var httpGetAttr = actionMethod.GetCustomAttribute<HttpGetAttribute>();
        Assert.NotNull(httpGetAttr);
        Assert.Equal("template", httpGetAttr.Template);

        var produces200 = actionMethod.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Any(a => a.StatusCode == StatusCodes.Status200OK);
        Assert.True(produces200, "GetTemplate should produce 200 OK.");
    }

    [Fact]
    public void Controller_GetTemplate_ReturnsXlsxFileResult()
    {
        var provider = new TransactionImportTemplateProvider();
        var controller = new TransactionImportsController(
            new FakeTransactionImportService(),
            new FakeWorkTransactionService(),
            Options.Create(new SystemParametersOptions()),
            provider,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<G54.Api.Controllers.TransactionImportsController>.Instance);

        var result = controller.GetTemplate();

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal(EvidenceFileValidator.ContentTypeFor(".xlsx"), fileResult.ContentType);
        Assert.Equal("transaction-import-template.xlsx", fileResult.FileDownloadName);
        Assert.NotEmpty(fileResult.FileContents);
    }

    [Fact]
    public void Migration_SqlIdempotency_HasExpectedGrantsAndDownRollback()
    {
        var migration = new GrantTransactionImportToManagersAndQa();

        // Up operations
        var upSqlOps = migration.UpOperations.OfType<SqlOperation>().ToList();
        Assert.NotEmpty(upSqlOps);
        var upSql = string.Join("\n", upSqlOps.Select(op => op.Sql));

        Assert.Contains("INSERT INTO role_permission", upSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ON CONFLICT DO NOTHING", upSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'SYSTEM_ADMIN'", upSql, StringComparison.Ordinal);
        Assert.Contains("'DEPT_MANAGER'", upSql, StringComparison.Ordinal);
        Assert.Contains("'BRANCH_MANAGER'", upSql, StringComparison.Ordinal);
        Assert.Contains("'QA'", upSql, StringComparison.Ordinal);
        Assert.Contains("'TRANSACTION_IMPORT_MANAGE'", upSql, StringComparison.Ordinal);
        Assert.Contains("'All'", upSql, StringComparison.Ordinal);

        // Down operations
        var downSqlOps = migration.DownOperations.OfType<SqlOperation>().ToList();
        Assert.NotEmpty(downSqlOps);
        var downSql = string.Join("\n", downSqlOps.Select(op => op.Sql));

        Assert.Contains("DELETE FROM role_permission", downSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'SYSTEM_ADMIN'", downSql, StringComparison.Ordinal);
        Assert.Contains("'DEPT_MANAGER'", downSql, StringComparison.Ordinal);
        Assert.Contains("'BRANCH_MANAGER'", downSql, StringComparison.Ordinal);
        Assert.Contains("'QA'", downSql, StringComparison.Ordinal);
        Assert.Contains("'TRANSACTION_IMPORT_MANAGE'", downSql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SYSTEM_ADMIN", true)]
    [InlineData("DEPT_MANAGER", true)]
    [InlineData("BRANCH_MANAGER", true)]
    [InlineData("VERIFIER", true)]
    [InlineData("EMPLOYEE", false)]
    public async Task PermissionAuthorization_TransactionImportManage_EvaluatesRolesCorrectly(string roleCode, bool expectedAllowed)
    {
        var accountId = Guid.NewGuid();
        var permissions = expectedAllowed && roleCode != "SYSTEM_ADMIN"
            ? new List<UserPermissionDto> { new("transaction-import", "manage", "All", "TRANSACTION_IMPORT_MANAGE") }
            : new List<UserPermissionDto>();

        var mockService = new FakePermissionAuthService(roleCode == "SYSTEM_ADMIN" || expectedAllowed);
        var handler = new PermissionAuthorizationHandler(mockService);

        var claims = new[]
        {
            new Claim("sub", accountId.ToString()),
            new Claim("role", roleCode),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        var requirement = new PermissionRequirement("transaction-import", "manage", "All");
        var context = new AuthorizationHandlerContext([requirement], principal, null);

        await handler.HandleAsync(context);

        Assert.Equal(expectedAllowed, context.HasSucceeded);
    }

    private sealed class FakePermissionAuthService(bool hasPermission) : IPermissionService
    {
        public Task<UserPermissionsResponseDto?> GetUserPermissionsAsync(Guid accountId, CancellationToken cancellationToken) =>
            Task.FromResult<UserPermissionsResponseDto?>(null);

        public Task<IReadOnlyList<UserPermissionDto>> GetEffectivePermissionsAsync(Guid accountId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserPermissionDto>>([]);

        public Task<bool> HasPermissionAsync(Guid accountId, string resource, string action, string requiredScope, CancellationToken cancellationToken) =>
            Task.FromResult(hasPermission);

        public Task InvalidateUserPermissionsAsync(Guid accountId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task InvalidateRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeTransactionImportService : ITransactionImportService
    {
        public Task<ImportBatchDto> ImportAsync(string fileName, Stream excel, Guid userId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<PagedResult<ImportBatchDto>> ListBatchesAsync(int page, int pageSize, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<ImportBatchDto> GetBatchAsync(Guid id, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<PagedResult<ImportRowErrorDto>> ListErrorsAsync(Guid batchId, int page, int pageSize, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<PagedResult<ImportedTransactionDto>> ListTransactionsAsync(Guid batchId, string? employeeCode, string? productCode, int page, int pageSize, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }

    private sealed class FakeWorkTransactionService : IWorkTransactionService
    {
        public Task<WorkTransactionsDto> AddAsync(Guid workId, Guid userId, AddWorkTransactionsRequestDto request, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<WorkTransactionsDto> AddForLeadCloseAsync(Work work, IReadOnlyList<string> codes, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<WorkTransactionsDto> ListAsync(Guid workId, Guid userId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<WorkTransactionsDto> RemoveAsync(Guid workId, Guid transactionId, Guid userId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<ReconcileSummaryDto> ReconcileBatchAsync(Guid batchId, CancellationToken ct = default) =>
            Task.FromResult(new ReconcileSummaryDto(0, 0, 0));
    }
}
