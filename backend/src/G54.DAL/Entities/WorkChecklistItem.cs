namespace G54.DAL.Entities;

/// <summary>One line of a work's checklist (SCR-WRK-03). Progress of the work is the share of items that are done.</summary>
public sealed class WorkChecklistItem
{
    public Guid Id { get; set; }

    public Guid WorkId { get; set; }

    public string Title { get; set; } = null!;

    /// <summary>Display order, 0-based.</summary>
    public int SortOrder { get; set; }

    public bool IsDone { get; set; }

    public DateTimeOffset? DoneAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Work Work { get; set; } = null!;
}
