using G54.BLL.Common.Models;

namespace G54.BLL.Dtos.PerformanceDashboard;

// docs/api/TRACEABILITY_CONTRACT.md

/// <summary>One line behind a score: a verified KPI result, a risk penalty or an appeal score entry.</summary>
public sealed record LedgerEntryDto(
    Guid Id,
    string Type,
    DateTimeOffset OccurredAt,
    decimal Points,
    decimal? RawValue,
    decimal? Multiplier,
    Guid? KpiDefinitionId,
    string? KpiCode,
    Guid? WorkId,
    string? WorkCode,
    string? Description);

public sealed record LedgerSummaryDto(decimal ResultPoints, decimal RiskPenaltyPoints, decimal ScoreEntryPoints);

public sealed record EmployeeLedgerDto(
    DashboardPeriodDto? Period,
    LedgerSummaryDto Summary,
    PagedResult<LedgerEntryDto> Entries);

public sealed record LedgerWorkDto(Guid Id, string Code, string Title, string? WorkTypeCode, string Status);

public sealed record LedgerSubmissionDto(
    Guid Id,
    int SubmissionNo,
    string Status,
    DateTimeOffset SubmittedAt,
    string? Comment,
    string? VerifierName,
    string? Decision,
    DateTimeOffset? VerifiedAt);

public sealed record LedgerRiskEventDto(
    Guid Id,
    string Code,
    string EventType,
    string Severity,
    string Status,
    string Description);

public sealed record LedgerAppealDto(Guid Id, string Status, string Reason);

public sealed record LedgerEntryDetailDto(
    LedgerEntryDto Entry,
    Guid EmployeeId,
    LedgerWorkDto? Work,
    LedgerSubmissionDto? Submission,
    LedgerRiskEventDto? RiskEvent,
    LedgerAppealDto? Appeal,
    string? RecordedByName);

public sealed record LedgerTransactionDto(
    Guid Id,
    string TransactionCode,
    string Status,
    string? Reason,
    bool IsSource,
    string? ProductCode,
    decimal? Amount,
    DateOnly? TransactionDate,
    string? ReferEmployeeCode,
    string? ImportFileName,
    DateTimeOffset? ImportedAt);

public sealed record LedgerTransactionsDto(Guid EntryId, Guid? WorkId, IReadOnlyList<LedgerTransactionDto> Items);
