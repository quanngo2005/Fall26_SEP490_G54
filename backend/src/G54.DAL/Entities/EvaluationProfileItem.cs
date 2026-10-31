using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

/// <summary>
/// Node of an evaluation profile's weight tree. A group node carries <see cref="Name"/> and has children;
/// a leaf carries <see cref="KpiDefinitionId"/>. Weights of siblings sum to 100.
/// </summary>
public partial class EvaluationProfileItem
{
    public Guid Id { get; set; }

    public Guid EvaluationProfileId { get; set; }

    public Guid? ParentItemId { get; set; }

    public Guid? KpiDefinitionId { get; set; }

    public string? Name { get; set; }

    public decimal WeightPercent { get; set; }

    public int DisplayOrder { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public virtual EvaluationProfile EvaluationProfile { get; set; } = null!;

    public virtual EvaluationProfileItem? ParentItem { get; set; }

    public virtual ICollection<EvaluationProfileItem> ChildItems { get; set; } = new List<EvaluationProfileItem>();

    public virtual KpiDefinition? KpiDefinition { get; set; }
}
