using G54.DAL.Entities;

namespace G54.BLL.Services.Import.EmployeeOrganization;

/// <summary>Everything the Employee/Organization rules and commit need, loaded once per batch (no N+1).</summary>
public sealed class EmployeeOrgImportContext
{
    public required Dictionary<string, Department> DepartmentsByCode { get; init; }

    public required Dictionary<string, Employee> EmployeesByCode { get; init; }

    public required Dictionary<string, Employee> EmployeesByEmail { get; init; }

    public required Dictionary<string, JobPosition> JobPositionsByCode { get; init; }

    public required Dictionary<string, Role> RolesByCode { get; init; }

    /// <summary>Existing accounts whose username equals an email in the file (username = email for new accounts).</summary>
    public required Dictionary<string, Account> AccountsByUsername { get; init; }

    /// <summary>Existing accounts of the employees in the file, keyed by employee id (commit locks/creates without per-row queries).</summary>
    public required Dictionary<Guid, Account> AccountsByEmployeeId { get; init; }
}
