namespace G54.DAL.Entities;

public sealed class ImportedTransaction
{
    public Guid Id { get; set; }
    public Guid ImportBatchId { get; set; }
    public string TransactionCode { get; set; } = null!;
    public Guid EmployeeId { get; set; }
    public string ProductCode { get; set; } = null!;
    public DateOnly TransactionDate { get; set; }
    public decimal Amount { get; set; }
    public decimal? Denominator { get; set; }
    public string? ReferEmployeeCode { get; set; }

    public ImportBatch ImportBatch { get; set; } = null!;
    public Employee Employee { get; set; } = null!;
}
