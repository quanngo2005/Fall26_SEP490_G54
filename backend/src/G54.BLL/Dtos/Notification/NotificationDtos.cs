namespace G54.BLL.Dtos.Notification;

/// <summary>Body of <c>POST /notifications</c>. Without <see cref="RecipientEmployeeIds"/> it goes to everyone in the sender's scope.</summary>
public sealed record SendNotificationDto
{
    public string? Title { get; init; }

    public string? Body { get; init; }

    /// <summary><c>NORMAL</c> (default) or <c>IMPORTANT</c>.</summary>
    public string? Severity { get; init; }

    /// <summary>Picks the recipients; each must be an active employee inside the sender's <c>notification:send</c> scope.</summary>
    public IReadOnlyList<Guid>? RecipientEmployeeIds { get; init; }

    /// <summary><c>WORK</c> or <c>CAMPAIGN</c>; sent together with <see cref="RelatedId"/> to link the notification to a record.</summary>
    public string? RelatedType { get; init; }

    public Guid? RelatedId { get; init; }
}

public sealed record SendNotificationResultDto(int RecipientCount);

public sealed record NotificationFilterDto
{
    /// <summary>True lists only unread notifications.</summary>
    public bool? Unread { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}

public sealed record NotificationDto(
    Guid Id,
    string Type,
    string Severity,
    string Title,
    string? Body,
    string? RelatedType,
    Guid? RelatedId,
    Guid? SenderEmployeeId,
    string? SenderName,
    DateTimeOffset CreatedAt,
    bool IsRead,
    DateTimeOffset? ReadAt);

/// <summary>An unread total, or the number of notifications a bulk action changed.</summary>
public sealed record NotificationCountDto(int Count);
