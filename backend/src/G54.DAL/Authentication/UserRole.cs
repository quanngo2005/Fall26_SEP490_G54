namespace G54.DAL.Authentication;

public sealed class UserRole
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Guid RoleId { get; set; }

    public Account Account { get; set; } = null!;

    public Role Role { get; set; } = null!;
}
