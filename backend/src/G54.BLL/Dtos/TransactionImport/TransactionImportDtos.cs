namespace G54.BLL.Dtos.TransactionImport;

// docs/api/TRANSACTION_IMPORT_CONTRACT.md
public sealed record ImportRowErrorDto(int Row, string Field, string Code, string Message);

public sealed record ImportBatchDto(
    Guid Id,
    string FileName,
    string Status,
    int TotalRows,
    int ImportedRows,
    int DuplicateRows,
    int ErrorRows,
    List<ImportRowErrorDto>? Errors,
    DateTimeOffset ImportedAt,
    Guid ImportedBy,
    ReconcileSummaryDto? Reconcile = null);

/// <summary>How the work transaction codes found in a batch ended up after the automatic reconcile (UC87).</summary>
public sealed record ReconcileSummaryDto(int Matched, int NotMatched, int InReview);

public sealed record ImportedTransactionDto(
    Guid Id,
    string TransactionCode,
    string EmployeeCode,
    string ProductCode,
    DateOnly TransactionDate,
    decimal Amount,
    decimal? Denominator,
    string? ReferEmployeeCode);
