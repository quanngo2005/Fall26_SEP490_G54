using System;

namespace G54.DAL.Entities;

/// <summary>Employee's year-level acknowledgement state for the Target Allocation v3 employee flow (D10, BR-36).</summary>
public static class EmployeeTargetAckStatus
{
    public const string Pending = "PENDING";
    public const string Confirmed = "CONFIRMED";
    public const string Reconfirm = "RECONFIRM";
}

/// <summary>One row per employee per year: tracks whether the employee has confirmed their targets, or must
/// reconfirm after a mid-term adjustment changed one of their KPIs (D10, D12/D13).</summary>
public sealed class EmployeeTargetAck
{
    public Guid Id { get; set; }

    public Guid EmployeeId { get; set; }

    public int Year { get; set; }

    public string Status { get; set; } = EmployeeTargetAckStatus.Pending;

    /// <summary>Set when the employee confirms (or reconfirms); null while PENDING/RECONFIRM.</summary>
    public DateTimeOffset? ConfirmedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Employee Employee { get; set; } = null!;
}
