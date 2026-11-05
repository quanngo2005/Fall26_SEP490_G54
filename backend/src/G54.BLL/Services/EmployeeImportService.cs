using System.Security.Cryptography;
using G54.BLL.Common;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Import.Parsing;
using G54.BLL.Common.Import.Reading;
using G54.BLL.Common.Import.Template;
using G54.BLL.Dtos.DataImport;
using G54.BLL.Services.Import.EmployeeOrganization;
using G54.DAL;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace G54.BLL.Services;

/// <summary>
/// Imports <c>02_Nhan_su_To_chuc.xlsx</c> (exported from HRM): read -> map -> minimal validation -> upsert
/// branch, department, employee, account in one transaction. The file is trusted, so only what the write
/// cannot do without is checked; email / code collisions with existing data surface at commit as a 409.
/// A new employee's account uses the email as username and a random temporary password returned once.
/// </summary>
public sealed class EmployeeImportService(
    IUnitOfWork uow,
    AppDbContext db,
    IExcelSheetReader reader,
    IPasswordEncoder passwordEncoder,
    IOptions<SystemParametersOptions> parameters) : IEmployeeImportService
{
    private const string PasswordAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789!@#$%";

    private const string BranchCodeHeader = "Mã chi nhánh*";
    private const string BranchNameHeader = "Tên chi nhánh*";
    private const string DeptCodeHeader = "Mã phòng ban*";
    private const string DeptNameHeader = "Tên phòng ban*";
    private const string DeptParentHeader = "Đơn vị cha";
    private const string EmployeeCodeHeader = "Mã NV*";
    private const string FullNameHeader = "Họ tên*";
    private const string EmailHeader = "Email*";
    private const string PositionHeader = "Vị trí*";
    private const string ManagerCodeHeader = "Mã quản lý trực tiếp";
    private const string StatusHeader = "Trạng thái*";

    private static readonly string[] StatusDropdown = ["ACTIVE", "ON_LEAVE", "PENDING_RESIGNATION", "TERMINATED"];

    private static readonly ImportTemplate<EmployeeOrgRow> Template = new TemplateBuilder<EmployeeOrgRow>()
        .Column(BranchCodeHeader, (r, v) => r.BranchCode = Upper(v))
        .Column(BranchNameHeader, (r, v) => r.BranchName = v)
        .Column("Mã CN cha", (_, _) => { }, required: false) // kept for template compatibility; a branch has no parent
        .Column(DeptCodeHeader, (r, v) => r.DeptCode = Upper(v))
        .Column(DeptNameHeader, (r, v) => r.DeptName = v)
        .Column(DeptParentHeader, (r, v) => r.DeptParentCode = Upper(v), required: false)
        .Column(EmployeeCodeHeader, (r, v) => r.EmployeeCode = Upper(v))
        .Column(FullNameHeader, (r, v) => r.FullName = v)
        .Column(EmailHeader, (r, v) => r.Email = string.IsNullOrWhiteSpace(v) ? null : v.ToLowerInvariant())
        .Column(PositionHeader, (r, v) => r.Position = Upper(v), dropdown: PositionMapping.Codes)
        .Column(ManagerCodeHeader, (r, v) => r.ManagerCode = Upper(v), required: false)
        .Column(StatusHeader, (r, v) => r.StatusText = v, dropdown: StatusDropdown)
        .Build();

    public byte[] GetTemplate() => TemplateWorkbookWriter.Write(Template);

    public async Task<ImportPreviewDto> PreviewAsync(string fileName, Stream file, CancellationToken cancellationToken)
    {
        var (rows, _) = await AnalyzeAsync(file, cancellationToken);

        var invalid = rows.Count(r => r.Errors.Count > 0);
        var create = rows.Count(r => r.Errors.Count == 0 && !r.IsUpdate);
        var update = rows.Count(r => r.Errors.Count == 0 && r.IsUpdate);

        return new ImportPreviewDto(
            fileName,
            new ImportSummaryDto(rows.Count, rows.Count - invalid, invalid, create, update),
            rows.Select(ToPreviewRow).ToList());
    }

    public async Task<ImportCommitResultDto> CommitAsync(Stream file, CancellationToken cancellationToken)
    {
        var (rows, ctx) = await AnalyzeAsync(file, cancellationToken);
        if (rows.Any(r => r.Errors.Count > 0))
        {
            throw new BusinessRuleException(ErrorCodes.DataImportHasErrors, null, 422);
        }

        try
        {
            return await uow.ExecuteInTransactionAsync(ct => WriteAsync(rows, ctx, ct), cancellationToken);
        }
        catch (Exception ex) when (PostgresConflict.IsUniqueViolation(ex))
        {
            // The unique index does not say which row collided, so the message is file-level.
            throw new ConflictException(
                "Mã nhân viên, email hoặc tên đăng nhập đã tồn tại. Không có dữ liệu nào được ghi.",
                ErrorCodes.DataImportConflict);
        }
    }

    // ---- read + validate -------------------------------------------------------------------------------

    private async Task<(List<EmployeeOrgRow> Rows, ImportContext Context)> AnalyzeAsync(Stream file, CancellationToken ct)
    {
        var sheet = reader.Read(file, parameters.Value.ImportMaxRows);
        var headerIndex = Template.BuildHeaderIndex(sheet.Headers);

        var rows = sheet.Rows
            .Select(raw =>
            {
                var row = Template.MapRow(raw, headerIndex);
                row.RowNumber = raw.RowNumber;
                row.DeptParentCode ??= row.BranchCode; // blank parent = the row's own branch
                return row;
            })
            .ToList();

        var context = await LoadContextAsync(rows, ct);
        Validate(rows, context);
        foreach (var row in rows.Where(r => r.Errors.Count == 0))
        {
            row.IsUpdate = context.EmployeesByCode.ContainsKey(row.EmployeeCode!);
        }

        return (rows, context);
    }

    private static void Validate(List<EmployeeOrgRow> rows, ImportContext ctx)
    {
        var codesInFile = rows.Where(r => r.EmployeeCode is not null).Select(r => r.EmployeeCode!).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            Require(row, BranchCodeHeader, row.BranchCode);
            Require(row, BranchNameHeader, row.BranchName);
            Require(row, DeptCodeHeader, row.DeptCode);
            Require(row, DeptNameHeader, row.DeptName);
            Require(row, EmployeeCodeHeader, row.EmployeeCode);
            Require(row, FullNameHeader, row.FullName);
            Require(row, EmailHeader, row.Email);
            Require(row, PositionHeader, row.Position);
            Require(row, StatusHeader, row.StatusText);

            if (row.Position is not null && !PositionMapping.IsKnown(row.Position))
            {
                row.AddError(PositionHeader, ErrorCodes.DataImportInvalidValue, $"Vị trí '{row.Position}' không hợp lệ.");
            }

            if (row.StatusText is not null)
            {
                if (StatusDropdown.Contains(row.StatusText, StringComparer.OrdinalIgnoreCase) &&
                    CellParsers.TryParseEnum<EmployeeStatus>(row.StatusText, out var status))
                {
                    row.Status = status;
                }
                else
                {
                    row.AddError(StatusHeader, ErrorCodes.DataImportInvalidValue, $"Trạng thái '{row.StatusText}' không hợp lệ.");
                }
            }

            if (row.ManagerCode is null)
            {
                continue;
            }

            if (string.Equals(row.ManagerCode, row.EmployeeCode, StringComparison.OrdinalIgnoreCase))
            {
                row.AddError(ManagerCodeHeader, ErrorCodes.DataImportInvalidValue, "Nhân viên không thể tự quản lý mình.");
            }
            else if (!codesInFile.Contains(row.ManagerCode) && !ctx.EmployeesByCode.ContainsKey(row.ManagerCode))
            {
                row.AddError(ManagerCodeHeader, ErrorCodes.DataImportReferenceNotFound, $"Mã quản lý '{row.ManagerCode}' không tồn tại.");
            }
        }

        // Two rows with one employee code would insert the same employee twice: flag the whole group.
        foreach (var group in rows.Where(r => r.EmployeeCode is not null).GroupBy(r => r.EmployeeCode!, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            foreach (var row in group)
            {
                row.AddError(EmployeeCodeHeader, ErrorCodes.DataImportDuplicate, $"Mã nhân viên '{group.Key}' trùng lặp trong file.");
            }
        }
    }

    private static void Require(EmployeeOrgRow row, string column, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            row.AddError(column, ErrorCodes.RequiredField, $"'{column}' là bắt buộc nhập.");
        }
    }

    private static string? Upper(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.ToUpperInvariant();

    private static ImportPreviewRowDto ToPreviewRow(EmployeeOrgRow r) => new(
        r.RowNumber,
        r.Errors.Count > 0 ? "INVALID" : r.IsUpdate ? "UPDATE" : "CREATE",
        new Dictionary<string, string?>
        {
            [BranchCodeHeader] = r.BranchCode,
            [BranchNameHeader] = r.BranchName,
            [DeptCodeHeader] = r.DeptCode,
            [DeptNameHeader] = r.DeptName,
            [DeptParentHeader] = r.DeptParentCode,
            [EmployeeCodeHeader] = r.EmployeeCode,
            [FullNameHeader] = r.FullName,
            [EmailHeader] = r.Email,
            [PositionHeader] = r.Position,
            [ManagerCodeHeader] = r.ManagerCode,
            [StatusHeader] = r.StatusText,
        },
        r.Errors.Select(e => new ImportErrorDto(e.Column, e.Code, e.Message)).ToList());

    // ---- load existing data (one batch, no N+1) --------------------------------------------------------

    private sealed class ImportContext
    {
        public required Dictionary<string, Department> DepartmentsByCode { get; init; }
        public required Dictionary<string, Employee> EmployeesByCode { get; init; }
        public required Dictionary<string, JobPosition> JobPositionsByCode { get; init; }
        public required Dictionary<string, Role> RolesByCode { get; init; }
        public required Dictionary<Guid, Account> AccountsByEmployeeId { get; init; }
    }

    private async Task<ImportContext> LoadContextAsync(List<EmployeeOrgRow> rows, CancellationToken ct)
    {
        var orgCodes = rows
            .SelectMany(r => new[] { r.BranchCode, r.DeptCode, r.DeptParentCode })
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var employeeCodes = rows
            .SelectMany(r => new[] { r.EmployeeCode, r.ManagerCode })
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Tracked on purpose: upserts mutate these entities (the repository ListAsync is AsNoTracking).
        var departments = await db.Departments.Where(d => orgCodes.Contains(d.Code)).ToListAsync(ct);
        var employees = await db.Employees.Where(e => employeeCodes.Contains(e.EmployeeCode)).ToListAsync(ct);
        var jobPositions = await uow.JobPositions.ListAsync(ct);
        var roles = await db.Roles.AsNoTracking().ToListAsync(ct);
        var employeeIds = employees.Select(e => e.Id).ToList();
        var accounts = await db.Accounts.Where(a => employeeIds.Contains(a.EmployeeId)).ToListAsync(ct);

        return new ImportContext
        {
            DepartmentsByCode = departments.ToDictionary(d => d.Code, StringComparer.OrdinalIgnoreCase),
            EmployeesByCode = employees.ToDictionary(e => e.EmployeeCode, StringComparer.OrdinalIgnoreCase),
            JobPositionsByCode = jobPositions.ToDictionary(j => j.Code, StringComparer.OrdinalIgnoreCase),
            RolesByCode = roles.ToDictionary(r => r.Code, StringComparer.OrdinalIgnoreCase),
            AccountsByEmployeeId = accounts.ToDictionary(a => a.EmployeeId),
        };
    }

    // ---- write -----------------------------------------------------------------------------------------

    private async Task<ImportCommitResultDto> WriteAsync(List<EmployeeOrgRow> rows, ImportContext ctx, CancellationToken ct)
    {
        var departments = new Dictionary<string, Department>(ctx.DepartmentsByCode, StringComparer.OrdinalIgnoreCase);

        // Branches first: a department's parent may be a branch created in this same batch.
        foreach (var row in rows.DistinctBy(r => r.BranchCode, StringComparer.OrdinalIgnoreCase))
        {
            UpsertDepartment(row.BranchCode!, row.BranchName!, DepartmentLevelType.Branch, null, departments);
        }

        foreach (var row in rows.DistinctBy(r => r.DeptCode, StringComparer.OrdinalIgnoreCase))
        {
            var parentId = departments.TryGetValue(row.DeptParentCode!, out var parent) ? parent.Id : (Guid?)null;
            UpsertDepartment(row.DeptCode!, row.DeptName!, DepartmentLevelType.Department, parentId, departments);
        }

        var employees = new Dictionary<string, Employee>(ctx.EmployeesByCode, StringComparer.OrdinalIgnoreCase);
        var newAccounts = new List<ImportNewAccountDto>();
        var created = 0;
        var updated = 0;

        foreach (var row in rows)
        {
            var (jobPositionCode, roleCode) = PositionMapping.Resolve(row.Position!);
            var jobPositionId = ctx.JobPositionsByCode[jobPositionCode].Id;

            if (!employees.TryGetValue(row.EmployeeCode!, out var employee))
            {
                employee = new Employee { Id = Guid.NewGuid(), EmployeeCode = row.EmployeeCode! };
                db.Employees.Add(employee);
                employees[employee.EmployeeCode] = employee;
                created++;
            }
            else
            {
                updated++;
            }

            employee.FullName = row.FullName!;
            employee.Email = row.Email!;
            employee.DepartmentId = departments[row.DeptCode!].Id;
            employee.JobPositionId = jobPositionId;
            employee.Status = row.Status;

            ctx.AccountsByEmployeeId.TryGetValue(employee.Id, out var account);
            if (account is null && !row.IsUpdate)
            {
                var temporaryPassword = RandomNumberGenerator.GetString(PasswordAlphabet, 16);
                account = new Account
                {
                    Id = Guid.NewGuid(),
                    EmployeeId = employee.Id,
                    Username = employee.Email,
                    PasswordHash = passwordEncoder.Encode(temporaryPassword),
                };
                db.Accounts.Add(account);
                if (ctx.RolesByCode.TryGetValue(roleCode, out var role))
                {
                    db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), AccountId = account.Id, RoleId = role.Id });
                }

                newAccounts.Add(new ImportNewAccountDto(employee.EmployeeCode, account.Username, temporaryPassword));
            }

            // BR-22: a terminated employee's account is locked (never unlocked by an import).
            if (account is not null && row.Status == EmployeeStatus.Terminated)
            {
                account.IsLocked = true;
            }
        }

        // Managers in a second pass so every employee in the batch exists.
        foreach (var row in rows)
        {
            employees[row.EmployeeCode!].ManagerId = row.ManagerCode is not null && employees.TryGetValue(row.ManagerCode, out var manager)
                ? manager.Id
                : null;
        }

        await db.SaveChangesAsync(ct);
        return new ImportCommitResultDto(created, updated, newAccounts);
    }

    private void UpsertDepartment(
        string code, string name, DepartmentLevelType levelType, Guid? parentId, Dictionary<string, Department> byCode)
    {
        if (!byCode.TryGetValue(code, out var department))
        {
            department = new Department { Id = Guid.NewGuid(), Code = code, LevelType = levelType };
            db.Departments.Add(department);
            byCode[code] = department;
        }

        department.Name = name;
        department.ParentDepartmentId = parentId;
        department.Status = RecordStatus.Active;
    }
}
