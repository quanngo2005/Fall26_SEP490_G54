using G54.BLL.Dtos.TargetAllocation;

namespace G54.BLL.Dtos.TargetImport;

// =========================================================================
// Year Target Import DTOs (docs/api/TARGET_ALLOCATION_CONTRACT.md, phase 1)
// =========================================================================

public sealed record TargetImportErrorDto(string Column, string Code, string Message);

public sealed record TargetImportRowDto(
    int RowNumber,
    string? KpiCode,
    string? KpiName,
    string? Unit,
    decimal? Value,
    decimal? PreviousYearValue,
    decimal? Delta,
    decimal? DeltaPercent,
    string Action,
    IReadOnlyList<TargetImportErrorDto> Errors,
    IReadOnlyList<TargetImportErrorDto> Warnings);

public sealed record TargetImportSummaryDto(int Total, int Valid, int Invalid, int Create, int Update, int Unchanged);

public sealed record TargetImportKpiNotInFileDto(string KpiCode, string KpiName);

public sealed record TargetImportQuarterRatiosDto(decimal? Q1, decimal? Q2, decimal? Q3, decimal? Q4);

public sealed record TargetImportPreviewDto(
    string FileName,
    Guid BranchId,
    int Year,
    TargetImportSummaryDto Summary,
    TargetImportQuarterRatiosDto QuarterRatios,
    IReadOnlyList<TargetImportKpiNotInFileDto> KpisNotInFile,
    IReadOnlyList<TargetImportRowDto> Rows);

public sealed record TargetImportCommitResultDto(
    Guid BranchId,
    int Year,
    List<TargetNodeDto> Targets,
    int Committed,
    int Skipped,
    bool NeedsReview);
