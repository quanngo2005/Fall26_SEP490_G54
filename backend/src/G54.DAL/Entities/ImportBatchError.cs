namespace G54.DAL.Entities;

/// <summary>One rejected row of a transaction import batch (docs/api/TRANSACTION_IMPORT_CONTRACT.md, UC33).</summary>
public sealed class ImportBatchError
{
    public Guid Id { get; set; }
    public Guid ImportBatchId { get; set; }
    public int RowNumber { get; set; }
    public string Field { get; set; } = null!;
    public string Code { get; set; } = null!;
    public string Message { get; set; } = null!;

    public ImportBatch ImportBatch { get; set; } = null!;
}
