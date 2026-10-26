using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class TaskHandover : IBaseEntity
{
    /// <summary>Referrer share applied to a handover when none is specified; matches the column default.</summary>
    public const decimal DefaultReferrerSharePercent = 30m;

    public Guid Id { get; set; }

    public Guid WorkId { get; set; }

    public Guid FromEmployeeId { get; set; }

    public Guid ToEmployeeId { get; set; }

    public Guid? ApprovedBy { get; set; }

    public string Reason { get; set; } = null!;

    /// <summary>
    /// Percentage (0-100) of the transaction's KPI points awarded to <see cref="FromEmployeeId"/> (the referrer).
    /// The processor (<see cref="ToEmployeeId"/>) receives the remainder.
    /// </summary>
    public decimal ReferrerSharePercent { get; set; } = DefaultReferrerSharePercent;

    public DateTimeOffset HandoverAt { get; set; }

    public DateTimeOffset? AcceptedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public HandoverStatus Status { get; set; }

    public virtual Employee? ApprovedByNavigation { get; set; }

    public virtual Employee FromEmployee { get; set; } = null!;

    public virtual Employee ToEmployee { get; set; } = null!;

    public virtual Work Work { get; set; } = null!;
}
