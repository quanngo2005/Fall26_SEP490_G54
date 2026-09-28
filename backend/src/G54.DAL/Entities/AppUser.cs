namespace G54.DAL.Entities;

public sealed class AppUser : BaseEntity
{
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public string Role { get; set; } = "User";
}
