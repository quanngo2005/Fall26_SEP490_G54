using System.Security.Cryptography;
using G54.BLL.Common.Import.Pipeline;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services.Import.EmployeeOrganization;

/// <summary>
/// Writes a validated Employee/Organization batch: Branch -&gt; Department (upsert) -&gt; Employee (upsert,
/// manager assigned in a second pass) -&gt; Account for new employees (temporary password, BR-22 lock on
/// TERMINATED). Existing employees absent from the file are never touched (BR-52).
/// </summary>
public sealed class EmployeeOrgCommitter(IUnitOfWork uow, AppDbContext db, IPasswordEncoder passwordEncoder)
{
    private const string PasswordAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789!@#$%";

    public async Task<ImportCommitResult> CommitAsync(
        IReadOnlyList<ImportRow<EmployeeOrgRow>> rows,
        EmployeeOrgImportContext ctx,
        CancellationToken cancellationToken)
    {
        return await uow.ExecuteInTransactionAsync(async ct =>
        {
            var departmentsByCode = new Dictionary<string, Department>(ctx.DepartmentsByCode, StringComparer.OrdinalIgnoreCase);

            // Branches first (a department's parent may be a branch created in this same batch).
            foreach (var row in BranchRows(rows))
            {
                UpsertDepartment(row.Data.BranchCode!, row.Data.BranchNameRaw!.Trim(), DepartmentLevelType.Branch, null, departmentsByCode);
            }

            foreach (var row in DeptRows(rows))
            {
                var parentId = row.Data.DeptParentCode is not null && departmentsByCode.TryGetValue(row.Data.DeptParentCode, out var parent)
                    ? parent.Id
                    : (Guid?)null;
                UpsertDepartment(row.Data.DeptCode!, row.Data.DeptNameRaw!.Trim(), DepartmentLevelType.Department, parentId, departmentsByCode);
            }

            await db.SaveChangesAsync(ct);

            var employeesByCode = new Dictionary<string, Employee>(ctx.EmployeesByCode, StringComparer.OrdinalIgnoreCase);
            var created = 0;
            var updated = 0;
            var unchanged = 0;
            var newAccounts = new List<ImportNewAccount>();

            foreach (var row in rows)
            {
                var data = row.Data;
                var (jobPositionCode, _) = PositionMapping.Resolve(data.Position!);
                var jobPosition = ctx.JobPositionsByCode[jobPositionCode];
                var department = departmentsByCode[data.DeptCode!];

                if (!employeesByCode.TryGetValue(data.EmployeeCode!, out var employee))
                {
                    employee = new Employee
                    {
                        Id = Guid.NewGuid(),
                        EmployeeCode = data.EmployeeCode!,
                        FullName = data.FullNameRaw!.Trim(),
                        Email = data.Email!,
                        DepartmentId = department.Id,
                        JobPositionId = jobPosition.Id,
                        Status = data.Status!.Value,
                        HireDate = data.HireDate,
                    };
                    db.Employees.Add(employee);
                    employeesByCode[employee.EmployeeCode] = employee;
                    created++;
                }
                else
                {
                    employee.FullName = data.FullNameRaw!.Trim();
                    employee.Email = data.Email!;
                    employee.DepartmentId = department.Id;
                    employee.JobPositionId = jobPosition.Id;
                    employee.Status = data.Status!.Value;
                    employee.HireDate = data.HireDate;
                    if (row.Action == ImportAction.Update)
                    {
                        updated++;
                    }
                    else
                    {
                        unchanged++;
                    }
                }
            }

            await db.SaveChangesAsync(ct);

            // Second pass: every employee now has an Id, so manager links can be resolved safely.
            foreach (var row in rows)
            {
                var employee = employeesByCode[row.Data.EmployeeCode!];
                employee.ManagerId = row.Data.ManagerCode is not null && employeesByCode.TryGetValue(row.Data.ManagerCode, out var manager)
                    ? manager.Id
                    : null;
            }

            await db.SaveChangesAsync(ct);

            // Accounts: created for new employees, locked for TERMINATED (BR-22), never created for a merely-existing one.
            var newEmployeeCodes = rows
                .Where(r => !ctx.EmployeesByCode.ContainsKey(r.Data.EmployeeCode!))
                .Select(r => r.Data.EmployeeCode!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows)
            {
                var data = row.Data;
                var employee = employeesByCode[data.EmployeeCode!];
                var (_, roleCode) = PositionMapping.Resolve(data.Position!);

                ctx.AccountsByEmployeeId.TryGetValue(employee.Id, out var account);
                if (account is null && newEmployeeCodes.Contains(data.EmployeeCode!))
                {
                    var temporaryPassword = GenerateTemporaryPassword();
                    account = new Account
                    {
                        Id = Guid.NewGuid(),
                        EmployeeId = employee.Id,
                        Username = data.Email!,
                        PasswordHash = passwordEncoder.Encode(temporaryPassword),
                        IsLocked = data.Status == EmployeeStatus.Terminated,
                    };
                    db.Accounts.Add(account);

                    if (ctx.RolesByCode.TryGetValue(roleCode, out var role))
                    {
                        db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), AccountId = account.Id, RoleId = role.Id });
                    }

                    newAccounts.Add(new ImportNewAccount(data.EmployeeCode!, account.Username, temporaryPassword));
                }
                else if (account is not null)
                {
                    account.IsLocked = data.Status == EmployeeStatus.Terminated || account.IsLocked;
                }
            }

            await db.SaveChangesAsync(ct);

            return new ImportCommitResult(created, updated, unchanged, newAccounts);
        }, cancellationToken);
    }

    private void UpsertDepartment(
        string code, string name, DepartmentLevelType levelType, Guid? parentId, Dictionary<string, Department> byCode)
    {
        if (byCode.TryGetValue(code, out var department))
        {
            department.Name = name;
            department.ParentDepartmentId = parentId;
            department.Status = RecordStatus.Active;
            return;
        }

        department = new Department
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            LevelType = levelType,
            ParentDepartmentId = parentId,
            Status = RecordStatus.Active,
        };
        db.Departments.Add(department);
        byCode[code] = department;
    }

    private static IEnumerable<ImportRow<EmployeeOrgRow>> BranchRows(IReadOnlyList<ImportRow<EmployeeOrgRow>> rows) =>
        rows.GroupBy(r => r.Data.BranchCode, StringComparer.OrdinalIgnoreCase).Select(g => g.First());

    private static IEnumerable<ImportRow<EmployeeOrgRow>> DeptRows(IReadOnlyList<ImportRow<EmployeeOrgRow>> rows) =>
        rows.GroupBy(r => r.Data.DeptCode, StringComparer.OrdinalIgnoreCase).Select(g => g.First());

    // RandomNumberGenerator.GetString samples without modulo bias.
    private static string GenerateTemporaryPassword() => RandomNumberGenerator.GetString(PasswordAlphabet, 16);
}
