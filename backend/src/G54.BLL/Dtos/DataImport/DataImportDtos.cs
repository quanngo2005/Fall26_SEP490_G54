namespace G54.BLL.Dtos.DataImport;

public sealed record ImportSummaryDto(int TotalRows, int Valid, int Invalid, int Create, int Update);

public sealed record ImportErrorDto(string Column, string Code, string Message);

/// <summary><c>Action</c> is <c>CREATE</c>, <c>UPDATE</c> or <c>INVALID</c>.</summary>
public sealed record ImportPreviewRowDto(int RowNumber, string Action, Dictionary<string, string?> Data, IReadOnlyList<ImportErrorDto> Errors);

public sealed record ImportPreviewDto(string FileName, ImportSummaryDto Summary, IReadOnlyList<ImportPreviewRowDto> Rows);

public sealed record ImportNewAccountDto(string EmployeeCode, string Username, string TemporaryPassword);

public sealed record ImportSkippedRowDto(int RowNumber, IReadOnlyList<ImportErrorDto> Errors);

public sealed record ImportCommitResultDto(
    int Created,
    int Updated,
    int Skipped,
    IReadOnlyList<ImportNewAccountDto> NewAccounts,
    IReadOnlyList<ImportSkippedRowDto> Errors);
