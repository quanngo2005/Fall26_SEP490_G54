using G54.DAL.Entities;

namespace G54.BLL.Dtos.EvaluationProfile;

/// <summary>`Status` is the UPPER_SNAKE API value of <see cref="EvaluationProfileStatus"/>.</summary>
public sealed record EvaluationProfileListItemDto(
    Guid Id,
    string Code,
    string Name,
    Guid JobPositionId,
    string JobPositionCode,
    string JobPositionTitle,
    int Version,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string Status,
    int ItemCount,
    decimal TotalWeightPercent,
    DateTimeOffset UpdatedAt);
