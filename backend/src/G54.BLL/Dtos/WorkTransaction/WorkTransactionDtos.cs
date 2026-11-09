namespace G54.BLL.Dtos.WorkTransaction;

// docs/api/WORK_TRANSACTION_CONTRACT.md
public sealed record AddWorkTransactionsRequestDto(List<string>? TransactionCodes);

public sealed record WorkTransactionDto(Guid Id, string TransactionCode, string Status, string? Reason, DateTimeOffset UpdatedAt);

public sealed record WorkTransactionsDto(Guid WorkId, string WorkStatus, List<WorkTransactionDto> Items);
