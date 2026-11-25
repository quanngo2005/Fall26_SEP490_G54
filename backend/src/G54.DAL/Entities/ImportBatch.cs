using NpgsqlTypes;

namespace G54.DAL.Entities;

/// <summary>What an import batch holds. Stored as the <see cref="PgNameAttribute"/> text in a varchar column (not a PostgreSQL enum type).</summary>
public enum ImportFileType
{
    /// <summary>Core Banking transactions (docs/api/TRANSACTION_IMPORT_CONTRACT.md).</summary>
    [PgName("TRANSACTION")] Transaction,

    /// <summary>Customer service ratings (docs/api/SERVICE_RATING_IMPORT_CONTRACT.md).</summary>
    [PgName("SERVICE_RATING")] ServiceRating,
}

public sealed class ImportBatch
{
    public Guid Id { get; set; }
    public ImportFileType FileType { get; set; }
    public string FileName { get; set; } = null!;
    public int TotalRows { get; set; }
    public int ImportedRows { get; set; }
    public int DuplicateRows { get; set; }
    public int ErrorRows { get; set; }
    public Guid ImportedBy { get; set; }
    public DateTimeOffset ImportedAt { get; set; }

    public ICollection<ImportedTransaction> Transactions { get; set; } = new List<ImportedTransaction>();
    public ICollection<ServiceRating> ServiceRatings { get; set; } = new List<ServiceRating>();
    public ICollection<ImportBatchError> Errors { get; set; } = new List<ImportBatchError>();
}
