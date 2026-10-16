namespace G54.DAL.Authentication;

public sealed class RolePermissionAssignment
{
    public Guid Id { get; set; }

    public Guid RoleId { get; set; }

    public Guid PermissionId { get; set; }

    public string Scope { get; set; } = "Own";

    public Role Role { get; set; } = null!;

    public PermissionRecord Permission { get; set; } = null!;
}
