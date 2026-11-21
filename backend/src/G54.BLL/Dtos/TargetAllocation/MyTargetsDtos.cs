namespace G54.BLL.Dtos.TargetAllocation;

// =========================================================================
// Employee-facing "my-targets" DTOs (Target Allocation v3, docs/api/TARGET_ALLOCATION_CONTRACT.md "Employee side").
// =========================================================================

/// <summary>One KPI row of the employee's year view: year + 4 quarters, last year's target/actual, and a
/// marker when the value changed since the employee last confirmed (RECONFIRM, D10).</summary>
public sealed record MyTargetKpiRowDto(
    Guid KpiId,
    string KpiCode,
    string KpiName,
    string Unit,
    string AggregationType,
    decimal Year,
    decimal? Q1,
    decimal? Q2,
    decimal? Q3,
    decimal? Q4,
    decimal? PreviousYearTarget,
    decimal? PreviousYearActual,
    bool Changed,
    decimal? ChangedOldValue,
    decimal? ChangedNewValue,
    bool HasFeedback,
    /// <summary>D9 ceiling/floor warning band (SystemParameter `targetSuggestionCeilingPercent`, same value the
    /// suggestion engine uses) — informational only; nothing on the employee side is blocked or clamped by it.</summary>
    decimal CeilingWarningPercent = 30m);

public sealed record MyTargetsDto(
    int Year,
    Guid EmployeeId,
    string EmployeeName,
    Guid DepartmentId,
    string DepartmentName,
    string AckStatus,
    DateTimeOffset? ConfirmedAt,
    IReadOnlyList<MyTargetKpiRowDto> Kpis);

public sealed record ConfirmMyTargetsResultDto(
    int Year,
    string AckStatus,
    DateTimeOffset ConfirmedAt);

public sealed record SubmitTargetFeedbackRequest(
    Guid KpiId,
    string Reason,
    decimal? ProposedYear,
    decimal? ProposedQ1,
    decimal? ProposedQ2,
    decimal? ProposedQ3,
    decimal? ProposedQ4);

public sealed record TargetFeedbackDto(
    Guid Id,
    Guid TargetAssignmentId,
    Guid KpiId,
    string KpiName,
    Guid EmployeeId,
    string EmployeeName,
    string? Reason,
    decimal? ProposedYear,
    decimal? ProposedQ1,
    decimal? ProposedQ2,
    decimal? ProposedQ3,
    decimal? ProposedQ4,
    string? Reply,
    Guid? RepliedBy,
    string? RepliedByName,
    DateTimeOffset? RepliedAt,
    DateTimeOffset CreatedAt);

public sealed record ReplyTargetFeedbackRequest(string Reply);

public sealed record MyTargetsProgressQuarterDto(
    decimal Target,
    decimal Achieved,
    int InReviewCount,
    decimal AchievedPercent,
    decimal ExpectedPacePercent,
    string PaceStatus);

public sealed record MyTargetsProgressRowDto(
    Guid KpiId,
    string KpiCode,
    string KpiName,
    string Unit,
    MyTargetsProgressQuarterDto Progress);

public sealed record MyTargetsProgressDto(
    int Year,
    string Period,
    IReadOnlyList<MyTargetsProgressRowDto> Kpis);

// =========================================================================
// DM-facing views (W9/W11): feedback listing by department/employee, and the change-log history behind a
// RECONFIRM marker. Both reuse the structured TargetChangeLog (D10) / TargetAssignmentFeedback tables.
// =========================================================================

/// <summary>One <see cref="G54.DAL.Entities.TargetChangeLog"/> row, for the W11 drawer / E5 history.</summary>
public sealed record TargetChangeLogEntryDto(
    Guid Id,
    DateTimeOffset OccurredAt,
    string Action,
    int Tier,
    Guid KpiId,
    string KpiName,
    string Unit,
    string Period,
    decimal? OldValue,
    decimal? NewValue,
    string? Basis);

/// <summary>One reconciled (or still-pending) transaction behind the employee's own KPI result (E5). `Status` is
/// `AUTO_RECONCILED` (matched to an imported transaction), `NOT_MATCHED`, or `IN_REVIEW` (still pending).
/// `CifCode`/`ReferEmployeeCode`/`TransactionCode` are null when the contribution has no resolvable imported
/// transaction (e.g. a manually-entered actual).</summary>
public sealed record MyContributionDto(
    Guid Id,
    DateOnly TransactionDate,
    string? TransactionCode,
    string? CifCode,
    string? ReferEmployeeCode,
    decimal Amount,
    string Status);
