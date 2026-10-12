using NpgsqlTypes;

namespace G54.DAL.Authentication;

public sealed class Employee
{
    public Guid Id { get; set; }

    public string EmployeeCode { get; set; } = null!;

    public string FullName { get; set; } = null!;

    public string Email { get; set; } = null!;

    public EmployeeStatus Status { get; set; }

    public Account? Account { get; set; }
}

public enum EmployeeStatus
{
    [PgName("ACTIVE")]
    Active,
    [PgName("INACTIVE")]
    Inactive,
    [PgName("ON_LEAVE")]
    OnLeave,
    [PgName("TERMINATED")]
    Terminated,
}
