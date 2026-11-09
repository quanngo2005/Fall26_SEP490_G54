namespace G54.DAL.Entities;

public sealed class ImportBatch
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = null!;
    public int TotalRows { get; set; }
    public int ImportedRows { get; set; }
    public int DuplicateRows { get; set; }
    public int ErrorRows { get; set; }
    public Guid ImportedBy { get; set; }
    public DateTimeOffset ImportedAt { get; set; }

    public ICollection<ImportedTransaction> Transactions { get; set; } = new List<ImportedTransaction>();
}
