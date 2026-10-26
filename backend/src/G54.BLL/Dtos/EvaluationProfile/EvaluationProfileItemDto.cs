namespace G54.BLL.Dtos.EvaluationProfile;

public sealed record EvaluationProfileItemDto(
    Guid KpiGroupId,
    string KpiGroupCode,
    string KpiGroupName,
    decimal WeightPercent,
    int DisplayOrder);
