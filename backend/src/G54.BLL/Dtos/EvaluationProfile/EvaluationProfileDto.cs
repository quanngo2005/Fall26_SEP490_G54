using G54.DAL.Entities;

namespace G54.BLL.Dtos.EvaluationProfile;

/// <summary>
/// `Status` is the UPPER_SNAKE API value of <see cref="EvaluationProfileStatus"/>.
/// `IsInUse` is true once any performance evaluation references the profile.
/// `RejectReason` is set while the profile is REJECTED.
/// </summary>
public sealed record EvaluationProfileDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    Guid JobPositionId,
    string JobPositionCode,
    string JobPositionTitle,
    int Version,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string Status,
    string? RejectReason,
    bool IsInUse,
    decimal TotalWeightPercent,
    DateTimeOffset UpdatedAt,
    List<EvaluationProfileItemDto> Items);
