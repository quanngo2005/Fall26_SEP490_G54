namespace G54.DAL.Entities;

public static class WorkTransactionStatus
{
    public const string InReview = "IN_REVIEW";
    public const string Matched = "MATCHED";
    public const string NotMatched = "NOT_MATCHED";
}

public static class KpiValueSource
{
    public const string Count = "COUNT";
    public const string Amount = "AMOUNT";
}

public sealed class WorkTransaction
{
    public Guid Id { get; set; }
    public Guid WorkId { get; set; }
    public string TransactionCode { get; set; } = null!;
    public string Status { get; set; } = WorkTransactionStatus.InReview;
    public string? Reason { get; set; }
    public Guid? ImportedTransactionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Work Work { get; set; } = null!;
    public ImportedTransaction? ImportedTransaction { get; set; }
}
