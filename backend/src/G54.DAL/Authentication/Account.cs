namespace G54.DAL.Authentication;

public sealed class Account
{
    public Guid Id { get; set; }

    public Guid EmployeeId { get; set; }

    public string Username { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public bool IsLocked { get; set; }

    public DateTimeOffset LastSyncAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public int FailedLoginAttempts { get; set; }

    public DateTimeOffset? LockedUntil { get; set; }

    public Employee Employee { get; set; } = null!;

    public ICollection<AuthAuditLog> AuthAuditLogs { get; set; } = new List<AuthAuditLog>();

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}
