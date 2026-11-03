using G54.BLL.Common.Constants;
using G54.BLL.Common.Extensions;
using G54.BLL.Common.Import.Pipeline;
using G54.BLL.Common.Import.Rules;
using G54.BLL.Common.Import.Template;
using G54.BLL.Services.Import.EmployeeOrganization.Rules;
using G54.DAL;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services.Import.EmployeeOrganization;

/// <summary>
/// Employee/Organization import: template "02_Nhan_su_To_chuc.xlsx". Classifies employees as CREATE/UPDATE/
/// UNCHANGED, commits branch -&gt; department -&gt; employee -&gt; account in one transaction (see
/// <see cref="EmployeeOrgCommitter"/>). Registered as <c>IImportDefinition</c> under Type
/// <c>"employee-organization"</c>; adding a new import type never requires touching this class.
/// </summary>
public sealed class EmployeeOrgImportDefinition(IUnitOfWork uow, AppDbContext db, EmployeeOrgCommitter committer)
    : ImportDefinition<EmployeeOrgRow, EmployeeOrgImportContext>
{
    public const string ImportType = "employee-organization";

    private static readonly string[] StatusDropdown = ["ACTIVE", "ON_LEAVE", "PENDING_RESIGNATION", "TERMINATED"];

    public override string Type => ImportType;

    protected override void ConfigureTemplate(TemplateBuilder<EmployeeOrgRow> builder) => builder
        .Column("Mã chi nhánh*", (r, v) => r.BranchCodeRaw = v)
        .Column("Tên chi nhánh*", (r, v) => r.BranchNameRaw = v)
        .Column("Mã CN cha", (r, v) => r.BranchParentCodeRaw = v, required: false)
        .Column("Mã phòng ban*", (r, v) => r.DeptCodeRaw = v)
        .Column("Tên phòng ban*", (r, v) => r.DeptNameRaw = v)
        .Column("Đơn vị cha", (r, v) => r.DeptParentCodeRaw = v, required: false)
        .Column("Mã NV*", (r, v) => r.EmployeeCodeRaw = v)
        .Column("Họ tên*", (r, v) => r.FullNameRaw = v)
        .Column("Email*", (r, v) => r.EmailRaw = v)
        .Column("Vị trí*", (r, v) => r.PositionRaw = v, dropdown: PositionMapping.Codes)
        .Column("Mã quản lý trực tiếp", (r, v) => r.ManagerCodeRaw = v, required: false)
        .Column("Trạng thái*", (r, v) => r.StatusRaw = v, dropdown: StatusDropdown)
        .Column("Ngày vào làm*", (r, v) => r.HireDateRaw = v);

    protected override IReadOnlyList<IImportRule<EmployeeOrgRow>> CreateRules() =>
    [
        new RequiredRule<EmployeeOrgRow>("Mã chi nhánh*", r => r.BranchCodeRaw),
        new RequiredRule<EmployeeOrgRow>("Tên chi nhánh*", r => r.BranchNameRaw),
        new RequiredRule<EmployeeOrgRow>("Mã phòng ban*", r => r.DeptCodeRaw),
        new RequiredRule<EmployeeOrgRow>("Tên phòng ban*", r => r.DeptNameRaw),
        new RequiredRule<EmployeeOrgRow>("Mã NV*", r => r.EmployeeCodeRaw),
        new RequiredRule<EmployeeOrgRow>("Họ tên*", r => r.FullNameRaw),
        new RequiredRule<EmployeeOrgRow>("Email*", r => r.EmailRaw),
        new RequiredRule<EmployeeOrgRow>("Vị trí*", r => r.PositionRaw),
        new RequiredRule<EmployeeOrgRow>("Trạng thái*", r => r.StatusRaw),
        new RequiredRule<EmployeeOrgRow>("Ngày vào làm*", r => r.HireDateRaw),
        new EmployeeOrgFieldFormatRule(),
        new DuplicateInFileRule<EmployeeOrgRow, string>(
            "Mã NV*", r => r.EmployeeCode, ErrorCodes.DataImportDuplicateCode, "Mã nhân viên '{0}' trùng lặp trong file."),
        new DuplicateInFileRule<EmployeeOrgRow, string>(
            "Email*", r => r.Email, ErrorCodes.DataImportDuplicateEmail, "Email '{0}' trùng lặp trong file."),
        new EmployeeOrgOrganizationRule(),
        new EmployeeOrgEmailInUseRule(),
        new EmployeeOrgManagerRule(),
    ];

    protected override async Task<EmployeeOrgImportContext> LoadContextAsync(
        IReadOnlyList<EmployeeOrgRow> rows, CancellationToken cancellationToken)
    {
        // Runs before the rule chain (Template Method order), so the row's Norm/typed properties
        // (filled in by EmployeeOrgFieldFormatRule) are not populated yet - normalize the Raw text here too.
        static string? NormalizeCode(string? raw) =>
            string.IsNullOrWhiteSpace(raw) ? null : raw.Trim().ToUpperInvariant();
        static string? NormalizeEmail(string? raw) =>
            string.IsNullOrWhiteSpace(raw) ? null : raw.Trim().ToLowerInvariant();

        var orgCodes = rows
            .SelectMany(r => new[]
            {
                NormalizeCode(r.BranchCodeRaw),
                NormalizeCode(r.DeptCodeRaw),
                NormalizeCode(string.IsNullOrWhiteSpace(r.DeptParentCodeRaw) ? r.BranchCodeRaw : r.DeptParentCodeRaw),
            })
            .Where(c => c is not null)
            .Select(c => c!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var employeeCodes = rows
            .SelectMany(r => new[] { NormalizeCode(r.EmployeeCodeRaw), NormalizeCode(r.ManagerCodeRaw) })
            .Where(c => c is not null)
            .Select(c => c!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var emails = rows
            .Select(r => NormalizeEmail(r.EmailRaw))
            .Where(e => e is not null)
            .Select(e => e!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var departments = orgCodes.Count == 0
            ? []
            : await uow.Departments.ListAsync(d => orgCodes.Contains(d.Code), cancellationToken);
        var employeesByCode = employeeCodes.Count == 0
            ? []
            : await uow.Employees.ListAsync(e => employeeCodes.Contains(e.EmployeeCode), cancellationToken);
        var employeesByEmail = emails.Count == 0
            ? []
            : await uow.Employees.ListAsync(e => emails.Contains(e.Email), cancellationToken);
        var jobPositions = await uow.JobPositions.ListAsync(cancellationToken);
        var roles = await db.Roles.AsNoTracking().ToListAsync(cancellationToken);
        var employeeIds = employeesByCode.Select(e => e.Id).ToList();
        var accounts = emails.Count == 0 && employeeIds.Count == 0
            ? []
            : await db.Accounts
                .Where(a => emails.Contains(a.Username.ToLowerInvariant()) || employeeIds.Contains(a.EmployeeId))
                .ToListAsync(cancellationToken);

        return new EmployeeOrgImportContext
        {
            DepartmentsByCode = departments.ToDictionary(d => d.Code, StringComparer.OrdinalIgnoreCase),
            EmployeesByCode = employeesByCode.ToDictionary(e => e.EmployeeCode, StringComparer.OrdinalIgnoreCase),
            EmployeesByEmail = employeesByEmail.ToDictionary(e => e.Email, StringComparer.OrdinalIgnoreCase),
            JobPositionsByCode = jobPositions.ToDictionary(j => j.Code, StringComparer.OrdinalIgnoreCase),
            RolesByCode = roles.ToDictionary(r => r.Code, StringComparer.OrdinalIgnoreCase),
            AccountsByUsername = accounts
                .GroupBy(a => a.Username, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase),
            AccountsByEmployeeId = accounts.ToDictionary(a => a.EmployeeId),
        };
    }

    protected override void Classify(ImportRow<EmployeeOrgRow> row, EmployeeOrgImportContext context)
    {
        var data = row.Data;
        if (!context.EmployeesByCode.TryGetValue(data.EmployeeCode!, out var existing))
        {
            row.Action = ImportAction.Create;
            return;
        }

        var (jobPositionCode, _) = PositionMapping.Resolve(data.Position!);
        var targetDepartmentId = context.DepartmentsByCode.TryGetValue(data.DeptCode!, out var dept) ? dept.Id : (Guid?)null;
        var targetJobPositionId = context.JobPositionsByCode.TryGetValue(jobPositionCode, out var jp) ? jp.Id : (Guid?)null;
        var targetManagerId = data.ManagerCode is not null && context.EmployeesByCode.TryGetValue(data.ManagerCode, out var manager)
            ? manager.Id
            : (Guid?)null;

        var changed =
            !string.Equals(existing.FullName, data.FullNameRaw?.Trim(), StringComparison.Ordinal) ||
            !string.Equals(existing.Email, data.Email, StringComparison.OrdinalIgnoreCase) ||
            existing.DepartmentId != targetDepartmentId ||
            existing.JobPositionId != targetJobPositionId ||
            existing.ManagerId != targetManagerId ||
            existing.Status != data.Status ||
            existing.HireDate != data.HireDate;

        row.Action = changed ? ImportAction.Update : ImportAction.Unchanged;
    }

    protected override Task<ImportCommitResult> CommitRowsAsync(
        IReadOnlyList<ImportRow<EmployeeOrgRow>> rows,
        EmployeeOrgImportContext context,
        CancellationToken cancellationToken) =>
        committer.CommitAsync(rows, context, cancellationToken);

    protected override Dictionary<string, string?> ToDictionary(EmployeeOrgRow row) => new()
    {
        ["Mã chi nhánh*"] = row.BranchCodeRaw,
        ["Tên chi nhánh*"] = row.BranchNameRaw,
        ["Mã CN cha"] = row.BranchParentCodeRaw,
        ["Mã phòng ban*"] = row.DeptCodeRaw,
        ["Tên phòng ban*"] = row.DeptNameRaw,
        ["Đơn vị cha"] = row.DeptParentCodeRaw,
        ["Mã NV*"] = row.EmployeeCodeRaw,
        ["Họ tên*"] = row.FullNameRaw,
        ["Email*"] = row.EmailRaw,
        ["Vị trí*"] = row.PositionRaw,
        ["Mã quản lý trực tiếp"] = row.ManagerCodeRaw,
        ["Trạng thái*"] = row.StatusRaw,
        ["Ngày vào làm*"] = row.HireDateRaw,
    };

    protected override IReadOnlyList<ImportRelatedEntity> BuildRelatedEntities(
        IReadOnlyList<ImportRow<EmployeeOrgRow>> rows, EmployeeOrgImportContext context)
    {
        var organizations = new Dictionary<string, ImportRelatedEntity>(StringComparer.OrdinalIgnoreCase);

        void AddOrg(string? code, string? name, DepartmentLevelType levelType, string? parentCode)
        {
            if (code is null || organizations.ContainsKey(code))
            {
                return;
            }

            name = name?.Trim();
            var action = ImportAction.Create;
            if (context.DepartmentsByCode.TryGetValue(code, out var existing))
            {
                var parentId = parentCode is not null && context.DepartmentsByCode.TryGetValue(parentCode, out var parent) ? parent.Id : (Guid?)null;
                var changed = !string.Equals(existing.Name, name, StringComparison.Ordinal) ||
                              existing.ParentDepartmentId != parentId ||
                              existing.Status != RecordStatus.Active;
                action = changed ? ImportAction.Update : ImportAction.Unchanged;
            }

            organizations[code] = new ImportRelatedEntity(levelType.ToEnumString(), code, name, parentCode, action.ToEnumString());
        }

        foreach (var row in rows.Where(r => !r.HasErrors))
        {
            AddOrg(row.Data.BranchCode, row.Data.BranchNameRaw, DepartmentLevelType.Branch, null);
            AddOrg(row.Data.DeptCode, row.Data.DeptNameRaw, DepartmentLevelType.Department, row.Data.DeptParentCode);
        }

        return [.. organizations.Values];
    }
}
