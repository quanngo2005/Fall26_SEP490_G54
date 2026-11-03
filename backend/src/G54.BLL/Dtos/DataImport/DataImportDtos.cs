namespace G54.BLL.Dtos.DataImport;

public sealed record ImportSummaryDto(int TotalRows, int Valid, int Invalid, int Create, int Update, int Unchanged);

public sealed record ImportErrorDto(string Column, string Code, string Message);

public sealed record ImportRelatedEntityDto(string Kind, string Code, string? Name, string? ParentCode, string Action);

public sealed record ImportPreviewRowDto(int RowNumber, string Action, Dictionary<string, string?> Data, IReadOnlyList<ImportErrorDto> Errors);

public sealed record ImportPreviewDto(
    Guid ImportId,
    string Type,
    string FileName,
    DateTimeOffset ExpiresAt,
    ImportSummaryDto Summary,
    IReadOnlyList<ImportRelatedEntityDto> RelatedEntities,
    Common.Models.PagedResult<ImportPreviewRowDto> Rows);

/// <summary>Query for <c>GET /data-imports/{importId}</c>: paging plus an optional Action filter (CREATE/UPDATE/UNCHANGED/INVALID).</summary>
public sealed class ImportPreviewQueryDto
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Status { get; set; }
}

public sealed record ImportNewAccountDto(string EmployeeCode, string Username, string TemporaryPassword);

public sealed record ImportCommitResultDto(int Created, int Updated, int Unchanged, IReadOnlyList<ImportNewAccountDto> NewAccounts);
