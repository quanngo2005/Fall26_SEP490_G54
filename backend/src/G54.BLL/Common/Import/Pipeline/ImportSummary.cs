namespace G54.BLL.Common.Import.Pipeline;

public sealed record ImportSummary(int TotalRows, int Valid, int Invalid, int Create, int Update, int Unchanged);

/// <summary>One previewed row, shaped for the API (no business types leak out of the generic engine).</summary>
public sealed record ImportRowResult(int RowNumber, string Action, Dictionary<string, string?> Data, IReadOnlyList<ImportError> Errors);

/// <summary>
/// A record the import creates or updates besides the row's own entity (e.g. a branch/department implied by
/// employee rows). <c>Kind</c> is the definition's own label, such as <c>BRANCH</c> or <c>DEPARTMENT</c>.
/// </summary>
public sealed record ImportRelatedEntity(string Kind, string Code, string? Name, string? ParentCode, string Action);

/// <summary>Result of running the pipeline up to classification.</summary>
public sealed record ImportPipelineResult(
    ImportSummary Summary,
    IReadOnlyList<ImportRowResult> Rows,
    IReadOnlyList<ImportRelatedEntity> RelatedEntities);

/// <summary>Result of a successful commit. <c>NewAccounts</c> carries one-time temporary passwords only.</summary>
public sealed record ImportCommitResult(
    int Created,
    int Updated,
    int Unchanged,
    IReadOnlyList<ImportNewAccount> NewAccounts);

public sealed record ImportNewAccount(string EmployeeCode, string Username, string TemporaryPassword);
