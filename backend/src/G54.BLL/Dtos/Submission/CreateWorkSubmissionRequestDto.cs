using Microsoft.AspNetCore.Http;

namespace G54.BLL.Dtos.Submission;

public sealed class CreateWorkSubmissionRequestDto
{
    public string? Comment { get; set; }

    public List<SubmissionMetricInputDto>? MetricItems { get; set; }

    public string? MetricItemsJson { get; set; }

    public List<IFormFile>? Files { get; set; }
}

public sealed record SubmissionMetricInputDto(
    Guid KpiDefinitionId,
    decimal Value,
    decimal? Denominator);
