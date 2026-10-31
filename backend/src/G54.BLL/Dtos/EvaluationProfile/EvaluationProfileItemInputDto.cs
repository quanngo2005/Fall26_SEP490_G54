using System.ComponentModel.DataAnnotations;

namespace G54.BLL.Dtos.EvaluationProfile;

/// <summary>
/// A node of the weight tree sent when a profile is created or replaced. Exactly one of `Name` (a group) and
/// `KpiDefinitionId` (a KPI leaf) is set. Root nodes are groups; a leaf has no children.
/// </summary>
public sealed record EvaluationProfileItemInputDto
{
    /// <summary>Group name, up to 200 characters. Empty for a KPI leaf.</summary>
    [StringLength(200)]
    public string? Name { get; init; }

    /// <summary>The KPI of a leaf. Empty for a group.</summary>
    public Guid? KpiDefinitionId { get; init; }

    /// <summary>Greater than 0 and at most 100, up to 4 decimal places; relative to the node's siblings.</summary>
    public decimal WeightPercent { get; init; }

    /// <summary>1 or greater.</summary>
    public int DisplayOrder { get; init; }

    /// <summary>Child nodes of a group, up to 3 levels in total.</summary>
    public List<EvaluationProfileItemInputDto> Children { get; init; } = [];
}
