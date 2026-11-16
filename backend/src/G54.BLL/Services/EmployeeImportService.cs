using System.Net.Mail;
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
/// Imports <c>02_Nhan_su_To_chuc.xlsx</c> (exported from HRM): read -> map -> validate -> upsert
/// branch, department, employee, account in one transaction (committing valid rows and skipping invalid ones).
/// A new employee's account uses the email as username and a random temporary password returned once.
/// </summary>
public sealed class EmployeeImportService(
    IUnitOfWork uow,
    AppDbContext db,
    IExcelSheetReader reader,
    IPasswordEncoder passwordEncoder,
    IOptions<SystemParametersOptions> parameters) : IEmployeeImportService
{
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
        var validRows = rows.Where(r => r.Errors.Count == 0).ToList();
        if (validRows.Count == 0)
        {
            throw new BusinessRuleException(ErrorCodes.DataImportHasErrors, null, 422);
        }

        var skippedRows = rows
            .Where(r => r.Errors.Count > 0)
            .Select(r => new ImportSkippedRowDto(
                r.RowNumber,
                r.Errors.Select(e => new ImportErrorDto(e.Column, e.Code, e.Message)).ToList()))
            .ToList();

        try
        {
            return await uow.ExecuteInTransactionAsync(ct => WriteAsync(validRows, skippedRows, ctx, ct), cancellationToken);
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

        if (sheet.Rows.Count == 0)
        {
            throw new ValidationException("File không có dòng dữ liệu nào (chỉ có dòng tiêu đề hoặc để trống).");
        }

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

            if (row.Email is not null)
            {
                if (!IsValidEmail(row.Email))
                {
                    row.AddError(EmailHeader, ErrorCodes.DataImportInvalidValue, $"Email '{row.Email}' không đúng định dạng.");
                }
                else if (IsEmailOwnedByAnotherEmployee(row, ctx))
                {
                    row.AddError(EmailHeader, ErrorCodes.DataImportDuplicate, $"Email '{row.Email}' đã được sử dụng bởi nhân viên khác.");
                }
            }

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

            if (row.ManagerCode is not null && string.Equals(row.ManagerCode, row.EmployeeCode, StringComparison.OrdinalIgnoreCase))
            {
                row.AddError(ManagerCodeHeader, ErrorCodes.DataImportInvalidValue, "Nhân viên không thể tự quản lý mình.");
            }
        }

        // Duplicate employee codes: keep the first occurrence and flag subsequent rows.
        foreach (var group in rows
            .Where(r => r.EmployeeCode is not null)
            .GroupBy(r => r.EmployeeCode!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1))
        {
            var first = group.First();
            foreach (var row in group.Skip(1))
            {
                row.AddError(EmployeeCodeHeader, ErrorCodes.DataImportDuplicate, $"Mã nhân viên '{first.EmployeeCode}' trùng với dòng {first.RowNumber}.");
            }
        }

        // Duplicate emails inside the file: keep the first occurrence and flag subsequent rows.
        foreach (var group in rows
            .Where(r => r.Email is not null && IsValidEmail(r.Email))
            .GroupBy(r => r.Email!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1))
        {
            var first = group.First();
            foreach (var row in group.Skip(1))
            {
                row.AddError(EmailHeader, ErrorCodes.DataImportDuplicate, $"Email '{first.Email}' trùng với dòng {first.RowNumber}.");
            }
        }

        // Manager codes must exist in the DB or on a VALID row in the file; cascade until stable.
        bool changed;
        do
        {
            changed = false;
            var validCodesInFile = rows
                .Where(r => r.Errors.Count == 0 && r.EmployeeCode is not null)
                .Select(r => r.EmployeeCode!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows)
            {
                if (row.ManagerCode is null ||
                    string.Equals(row.ManagerCode, row.EmployeeCode, StringComparison.OrdinalIgnoreCase) ||
                    row.Errors.Any(e => e.Column == ManagerCodeHeader))
                {
                    continue;
                }

                if (!validCodesInFile.Contains(row.ManagerCode) && !ctx.EmployeesByCode.ContainsKey(row.ManagerCode))
                {
                    var wasValid = row.Errors.Count == 0;
                    row.AddError(ManagerCodeHeader, ErrorCodes.DataImportReferenceNotFound, $"Mã quản lý '{row.ManagerCode}' không tồn tại.");
                    if (wasValid)
                    {
                        changed = true;
                    }
                }
            }
        }
        while (changed);
    }

    private static bool IsValidEmail(string email)
    {
        if (email.Any(char.IsWhiteSpace) || !MailAddress.TryCreate(email, out var parsed) ||
            !string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var at = parsed.Host;
        return at.Contains('.', StringComparison.Ordinal)
            && !at.StartsWith('.')
            && !at.EndsWith('.');
    }

    private static bool IsEmailOwnedByAnotherEmployee(EmployeeOrgRow row, ImportContext ctx)
    {
        if (row.Email is null)
        {
            return false;
        }

        if (ctx.EmployeeCodesByEmail.TryGetValue(row.Email, out var ownerCode) &&
            !string.Equals(ownerCode, row.EmployeeCode, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (ctx.AccountOwnerCodesByUsername.TryGetValue(row.Email, out var accountOwnerCode) &&
            !string.Equals(accountOwnerCode, row.EmployeeCode, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
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
        public required Dictionary<string, string> EmployeeCodesByEmail { get; init; }
        public required Dictionary<string, string> AccountOwnerCodesByUsername { get; init; }
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
        var emails = rows
            .Select(r => r.Email)
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

        var emailOwners = emails.Count == 0 ? [] : await QueryEmailOwnersAsync(emails, ct);
        var usernameOwners = emails.Count == 0 ? [] : await QueryUsernameOwnersAsync(emails, ct);

        var employeeCodesByEmail = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (email, employeeCode) in emailOwners)
        {
            employeeCodesByEmail.TryAdd(email, employeeCode);
        }

        var accountOwnerCodesByUsername = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (username, employeeCode) in usernameOwners)
        {
            accountOwnerCodesByUsername.TryAdd(username, employeeCode);
        }

        return new ImportContext
        {
            DepartmentsByCode = departments.ToDictionary(d => d.Code, StringComparer.OrdinalIgnoreCase),
            EmployeesByCode = employees.ToDictionary(e => e.EmployeeCode, StringComparer.OrdinalIgnoreCase),
            EmployeeCodesByEmail = employeeCodesByEmail,
            AccountOwnerCodesByUsername = accountOwnerCodesByUsername,
            JobPositionsByCode = jobPositions.ToDictionary(j => j.Code, StringComparer.OrdinalIgnoreCase),
            RolesByCode = roles.ToDictionary(r => r.Code, StringComparer.OrdinalIgnoreCase),
            AccountsByEmployeeId = accounts.ToDictionary(a => a.EmployeeId),
        };
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1311:Specify a culture or use an invariant version", Justification = "Translated to PostgreSQL lower(), using database collation rather than CLR culture.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1304:Specify CultureInfo", Justification = "Npgsql translates only the parameterless ToLower() to SQL lower().")]
    private async Task<List<(string Email, string EmployeeCode)>> QueryEmailOwnersAsync(List<string> lowerEmails, CancellationToken ct) =>
        (await db.Employees.AsNoTracking()
            .Where(e => e.Email != null && lowerEmails.Contains(e.Email.ToLower()))
            .Select(e => new { Email = e.Email!, e.EmployeeCode })
            .ToListAsync(ct))
        .Select(x => (x.Email, x.EmployeeCode))
        .ToList();

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1311:Specify a culture or use an invariant version", Justification = "Translated to PostgreSQL lower(), using database collation rather than CLR culture.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1304:Specify CultureInfo", Justification = "Npgsql translates only the parameterless ToLower() to SQL lower().")]
    private async Task<List<(string Username, string EmployeeCode)>> QueryUsernameOwnersAsync(List<string> lowerEmails, CancellationToken ct) =>
        (await db.Accounts.AsNoTracking()
            .Where(a => lowerEmails.Contains(a.Username.ToLower()))
            .Select(a => new { a.Username, a.Employee.EmployeeCode })
            .ToListAsync(ct))
        .Select(x => (x.Username, x.EmployeeCode))
        .ToList();

    // ---- write -----------------------------------------------------------------------------------------

    private async Task<ImportCommitResultDto> WriteAsync(
        List<EmployeeOrgRow> rows,
        List<ImportSkippedRowDto> skippedRows,
        ImportContext ctx,
        CancellationToken ct)
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
                var temporaryPassword = TemporaryPassword.Generate();
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
        return new ImportCommitResultDto(created, updated, skippedRows.Count, newAccounts, skippedRows);
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
