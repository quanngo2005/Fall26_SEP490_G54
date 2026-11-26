namespace G54.DAL.Entities;

/// <summary>Headcount cap of one ranking tier (A1, A2, A3, B) for a department. Tier C takes the remaining employees.</summary>
public sealed class RankingSlotQuota : BaseEntity
{
    public Guid DepartmentId { get; set; }

    public Guid? EvaluationPeriodId { get; set; }

    public string TierCode { get; set; } = null!;

    public int Headcount { get; set; }

    public Department Department { get; set; } = null!;

    public EvaluationPeriod? EvaluationPeriod { get; set; }
}
