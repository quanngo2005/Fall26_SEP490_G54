using System;

namespace G54.DAL.Entities;

/// <summary>Snapshot written each time a performance evaluation is recalculated or changes status.</summary>
public partial class PerformanceEvaluationHistory
{
    public Guid Id { get; set; }

    public Guid PerformanceEvaluationId { get; set; }

    public PerformanceEvaluationHistoryAction Action { get; set; }

    public PerformanceEvaluationStatus? FromStatus { get; set; }

    public PerformanceEvaluationStatus ToStatus { get; set; }

    public decimal? OldTotalScore { get; set; }

    public decimal? NewTotalScore { get; set; }

    public Guid? ChangedBy { get; set; }

    public string? Comment { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public virtual PerformanceEvaluation PerformanceEvaluation { get; set; } = null!;
}
