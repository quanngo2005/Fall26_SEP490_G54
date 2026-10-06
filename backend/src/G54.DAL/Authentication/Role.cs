using NpgsqlTypes;

namespace G54.DAL.Authentication;

public sealed class Role
{
    public Guid Id { get; set; }

    public string Code { get; set; } = null!;

    public AuthRecordStatus Status { get; set; }

    public ICollection<RolePermissionAssignment> RolePermissions { get; set; } = new List<RolePermissionAssignment>();

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}

public enum AuthRecordStatus
{
    [PgName("ACTIVE")]
    Active,
    [PgName("INACTIVE")]
    Inactive,
    [PgName("ARCHIVED")]
    Archived,
}
