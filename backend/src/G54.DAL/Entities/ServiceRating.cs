namespace G54.DAL.Entities;

/// <summary>One customer rating of a served transaction, imported from Excel (UC32, docs/api/SERVICE_RATING_IMPORT_CONTRACT.md).</summary>
public sealed class ServiceRating
{
    public Guid Id { get; set; }
    public Guid ImportBatchId { get; set; }
    public Guid EmployeeId { get; set; }

    /// <summary>The rated Core Banking transaction; one rating per transaction.</summary>
    public string TransactionCode { get; set; } = null!;

    public DateOnly RatingDate { get; set; }

    /// <summary>1 to 5 stars; 1 star alerts the department manager (BR-17).</summary>
    public short Score { get; set; }

    public string? CustomerComment { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public ImportBatch ImportBatch { get; set; } = null!;
    public Employee Employee { get; set; } = null!;
}
