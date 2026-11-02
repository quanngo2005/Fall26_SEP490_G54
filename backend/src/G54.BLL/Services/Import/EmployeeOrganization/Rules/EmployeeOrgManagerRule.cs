using G54.BLL.Common.Constants;
using G54.BLL.Common.Import.Pipeline;
using G54.BLL.Common.Import.Rules;
using G54.DAL.Entities;

namespace G54.BLL.Services.Import.EmployeeOrganization.Rules;

/// <summary>
/// Rule 7: the direct manager code must resolve (in the file or the DB), an employee cannot manage itself,
/// and the manager graph built from file + DB must not contain a cycle.
/// </summary>
public sealed class EmployeeOrgManagerRule : IImportRule<EmployeeOrgRow>
{
    public int Order => 0;

    public void Apply(ImportValidationContext<EmployeeOrgRow> context)
    {
        var ctx = (EmployeeOrgImportContext)context.Context!;
        var candidateRows = context.Rows.Where(r => !r.HasErrors && r.Data.EmployeeCode is not null).ToList();

        var codesInFile = candidateRows
            .Select(r => r.Data.EmployeeCode!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // employeeCode -> managerCode; file edges take precedence over DB edges for employees present in the file.
        var managerOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in candidateRows)
        {
            var employeeCode = row.Data.EmployeeCode!;
            var managerCode = row.Data.ManagerCode;
            if (managerCode is null)
            {
                continue;
            }

            if (string.Equals(managerCode, employeeCode, StringComparison.OrdinalIgnoreCase))
            {
                row.AddError("Mã quản lý trực tiếp", ErrorCodes.DataImportManagerSelf, "Nhân viên không thể tự quản lý mình.");
                continue;
            }

            var managerExists = codesInFile.Contains(managerCode) || ctx.EmployeesByCode.ContainsKey(managerCode);
            if (!managerExists)
            {
                row.AddError("Mã quản lý trực tiếp", ErrorCodes.DataImportManagerNotFound, $"Mã quản lý '{managerCode}' không tồn tại.");
                continue;
            }

            managerOf[employeeCode] = managerCode;
        }

        // Known DB edges for codes not touched by this file, so a cycle that only closes through existing data is still caught.
        var codeById = ctx.EmployeesByCode.Values.ToDictionary(e => e.Id, e => e.EmployeeCode);
        foreach (var employee in ctx.EmployeesByCode.Values)
        {
            if (employee.ManagerId is { } managerId &&
                !managerOf.ContainsKey(employee.EmployeeCode) &&
                codeById.TryGetValue(managerId, out var managerCode))
            {
                managerOf[employee.EmployeeCode] = managerCode;
            }
        }

        foreach (var row in candidateRows)
        {
            var employeeCode = row.Data.EmployeeCode!;
            if (row.HasErrors || !managerOf.ContainsKey(employeeCode))
            {
                continue;
            }

            if (HasCycle(employeeCode, managerOf))
            {
                row.AddError("Mã quản lý trực tiếp", ErrorCodes.DataImportManagerCycle, "Cấu trúc quản lý bị vòng lặp.");
            }
        }
    }

    private static bool HasCycle(string start, Dictionary<string, string> managerOf)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { start };
        var current = start;
        while (managerOf.TryGetValue(current, out var next))
        {
            if (!visited.Add(next))
            {
                return true;
            }

            current = next;
        }

        return false;
    }
}
