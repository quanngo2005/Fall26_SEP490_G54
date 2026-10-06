namespace G54.DAL.Authentication;

public sealed class PermissionRecord
{
    public Guid Id { get; set; }

    public string Code { get; set; } = null!;

    public ICollection<RolePermissionAssignment> RolePermissions { get; set; } = new List<RolePermissionAssignment>();
}
