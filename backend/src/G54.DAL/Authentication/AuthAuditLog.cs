namespace G54.DAL.Authentication;

public sealed class AuthAuditLog
{
    public Guid Id { get; set; }

    public Guid? ActorId { get; set; }

    public string Action { get; set; } = null!;

    public DateTimeOffset OccurredAt { get; set; }

    public string? IpAddress { get; set; }

    public Account? Actor { get; set; }
}
