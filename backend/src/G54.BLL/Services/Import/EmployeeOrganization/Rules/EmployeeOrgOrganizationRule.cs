using G54.BLL.Common.Constants;
using G54.BLL.Common.Import.Pipeline;
using G54.BLL.Common.Import.Rules;
using G54.DAL.Entities;

namespace G54.BLL.Services.Import.EmployeeOrganization.Rules;

/// <summary>
/// Rule 8: the branch/department columns describe a small org tree repeated once per employee row, so every
/// row sharing a code must agree on its name and parent, and the parent chain and level types must be sane.
/// </summary>
public sealed class EmployeeOrgOrganizationRule : IImportRule<EmployeeOrgRow>
{
    public int Order => -20;

    public void Apply(ImportValidationContext<EmployeeOrgRow> context)
    {
        var ctx = (EmployeeOrgImportContext)context.Context!;
        var rows = context.Rows.Where(r => !r.HasErrors).ToList();

        var branchCodesInFile = rows.Select(r => r.Data.BranchCode).Where(c => c is not null).ToHashSet(StringComparer.OrdinalIgnoreCase)!;

        CheckConsistentGroup(rows, r => r.Data.BranchCode, r => r.Data.BranchNameRaw?.Trim(), null, "Tên chi nhánh*");
        CheckConsistentGroup(rows, r => r.Data.DeptCode, r => r.Data.DeptNameRaw?.Trim(), r => r.Data.DeptParentCode, "Tên phòng ban*");

        foreach (var row in rows)
        {
            var data = row.Data;

            if (data.BranchCode is not null &&
                ctx.DepartmentsByCode.TryGetValue(data.BranchCode, out var existingBranch) &&
                existingBranch.LevelType != DepartmentLevelType.Branch)
            {
                row.AddError("Mã chi nhánh*", ErrorCodes.DataImportOrgLevelMismatch, $"Mã '{data.BranchCode}' đã tồn tại nhưng không phải chi nhánh.");
            }

            if (data.DeptCode is not null &&
                ctx.DepartmentsByCode.TryGetValue(data.DeptCode, out var existingDept) &&
                existingDept.LevelType == DepartmentLevelType.Branch)
            {
                row.AddError("Mã phòng ban*", ErrorCodes.DataImportOrgLevelMismatch, $"Mã '{data.DeptCode}' đã tồn tại nhưng là chi nhánh, không phải phòng ban.");
            }

            if (data.DeptParentCode is null)
            {
                continue;
            }

            var parentIsBranchInFile = branchCodesInFile.Contains(data.DeptParentCode);
            var parentIsBranchInDb = ctx.DepartmentsByCode.TryGetValue(data.DeptParentCode, out var parentDept) &&
                                      parentDept.LevelType == DepartmentLevelType.Branch;

            if (!parentIsBranchInFile && !parentIsBranchInDb)
            {
                row.AddError("Đơn vị cha", ErrorCodes.DataImportOrgMismatch, $"Đơn vị cha '{data.DeptParentCode}' không phải chi nhánh hợp lệ.");
            }
        }
    }

    private static void CheckConsistentGroup(
        List<ImportRow<EmployeeOrgRow>> rows,
        Func<ImportRow<EmployeeOrgRow>, string?> keySelector,
        Func<ImportRow<EmployeeOrgRow>, string?> nameSelector,
        Func<ImportRow<EmployeeOrgRow>, string?>? parentSelector,
        string column)
    {
        var groups = rows.Where(r => keySelector(r) is not null).GroupBy(r => keySelector(r)!, StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            var names = group.Select(nameSelector).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var parents = parentSelector is null ? [] : group.Select(parentSelector).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            if (names.Count <= 1 && parents.Count <= 1)
            {
                continue;
            }

            foreach (var row in group)
            {
                row.AddError(column, ErrorCodes.DataImportOrgMismatch, $"Thông tin đơn vị '{group.Key}' không nhất quán giữa các dòng.");
            }
        }
    }
}
