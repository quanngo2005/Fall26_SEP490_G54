namespace G54.DAL.Authentication;

public sealed class RolePermissionAssignment
{
    public Guid Id { get; set; }

    public Guid RoleId { get; set; }

    public Guid PermissionId { get; set; }

    public Role Role { get; set; } = null!;

    public PermissionRecord Permission { get; set; } = null!;
}
