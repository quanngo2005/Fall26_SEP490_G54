using NpgsqlTypes;

namespace G54.DAL.Entities;

/// <summary>Stored as the <see cref="PgNameAttribute"/> text in a varchar column (not a PostgreSQL enum type).</summary>
public enum ScoreEntryKind
{
    [PgName("HOLD")] Hold,
    [PgName("TASK_DEDUCTION")] TaskDeduction,
    [PgName("REFUND")] Refund,
}

public enum ScoreEntrySource
{
    [PgName("MANAGER")] Manager,
    [PgName("TASK_HISTORY")] TaskHistory,
    [PgName("SYSTEM")] System,
    [PgName("REVERSAL")] Reversal,
}

public sealed class ScoreEntry
{
    public Guid Id { get; set; }
    public Guid EvaluationId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? AppealId { get; set; }
    public Guid? WorkId { get; set; }
    public Guid? ReversesEntryId { get; set; }
    public Guid RecordedByEmployeeId { get; set; }
    public decimal Points { get; set; }
    public ScoreEntryKind Kind { get; set; }
    public ScoreEntrySource Source { get; set; }
    public string Reason { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public PerformanceEvaluation Evaluation { get; set; } = null!;
}

public sealed class AppealScoreSetting
{
    public int Id { get; set; }
    public decimal DefaultPoints { get; set; } = 5m;
}
