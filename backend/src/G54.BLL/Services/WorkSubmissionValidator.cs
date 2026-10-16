using System.Text.Json;
using G54.BLL.Dtos.Submission;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

public sealed class WorkSubmissionValidator(IUnitOfWork unitOfWork) : IWorkSubmissionValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<WorkSubmissionValidationResult> ValidateAsync(
        Work work,
        Guid employeeId,
        CreateWorkSubmissionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(request);

        // Assignee (403) and work state (409) are enforced by ResultSubmissionService before payload validation.
        var errors = new List<string>();

        // 1. Extract and parse metric items
        var metrics = request.MetricItems ?? new List<SubmissionMetricInputDto>();
        if (metrics.Count == 0 && !string.IsNullOrWhiteSpace(request.MetricItemsJson))
        {
            try
            {
                metrics = JsonSerializer.Deserialize<List<SubmissionMetricInputDto>>(
                    request.MetricItemsJson,
                    JsonOptions) ?? new();
            }
            catch (Exception ex)
            {
                errors.Add($"Định dạng JSON của MetricItems không hợp lệ: {ex.Message}");
            }
        }

        // 2. Validate KPI criteria if work belongs to a WorkType
        if (work.WorkTypeId.HasValue)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var activeWorkTypeKpis = await unitOfWork.WorkTypeKpis.Query()
                .Include(wtk => wtk.KpiDefinition)
                .Where(wtk => wtk.WorkTypeId == work.WorkTypeId.Value
                    && wtk.EffectiveFrom <= today
                    && (wtk.EffectiveTo == null || wtk.EffectiveTo >= today))
                .ToListAsync(cancellationToken);

            var activeKpiMap = activeWorkTypeKpis.ToDictionary(k => k.KpiDefinitionId);

            // Check that all required KPIs are present in submitted metrics
            foreach (var requiredKpi in activeWorkTypeKpis.Where(k => k.IsRequired))
            {
                var submitted = metrics.FirstOrDefault(m => m.KpiDefinitionId == requiredKpi.KpiDefinitionId);
                if (submitted is null)
                {
                    errors.Add(
                        $"Tiêu chí KPI bắt buộc '{requiredKpi.KpiDefinition.Name}' ({requiredKpi.KpiDefinition.Code}) chưa được nhập kết quả.");
                }
            }

            // Validate each submitted metric against its definition
            foreach (var metric in metrics)
            {
                if (!activeKpiMap.TryGetValue(metric.KpiDefinitionId, out var wtk))
                {
                    errors.Add($"Chỉ số KPI với ID '{metric.KpiDefinitionId}' không thuộc loại công việc này hoặc đã hết hiệu lực.");
                    continue;
                }

                if (metric.Value < 0)
                {
                    errors.Add($"Giá trị KPI '{wtk.KpiDefinition.Code}' không được là số âm (giá trị hiện tại: {metric.Value}).");
                }

                if (wtk.KpiDefinition.AggregationType == KpiAggregationType.Ratio)
                {
                    if (!metric.Denominator.HasValue || metric.Denominator.Value <= 0)
                    {
                        errors.Add($"KPI dạng tỷ lệ (RATIO) '{wtk.KpiDefinition.Code}' yêu cầu mẫu số (denominator) lớn hơn 0.");
                    }
                }
            }
        }

        // 3. Validate files if present
        if (request.Files is { Count: > 0 })
        {
            if (request.Files.Count > 10)
            {
                errors.Add("Số lượng tệp đính kèm tối đa là 10 tệp.");
            }

            foreach (var file in request.Files)
            {
                if (file.Length <= 0)
                {
                    errors.Add($"Tệp '{file.FileName}' rỗng (0 bytes).");
                }
                else if (file.Length > StorageService.DefaultMaxFileSizeBytes)
                {
                    errors.Add($"Tệp '{file.FileName}' vượt quá dung lượng tối đa cho phép 25 MB.");
                }

                var ext = Path.GetExtension(file.FileName);
                if (string.IsNullOrWhiteSpace(ext) || !StorageService.AllowedExtensions.Contains(ext))
                {
                    errors.Add(
                        $"Định dạng tệp '{ext}' của file '{file.FileName}' không được chấp nhận. Chỉ chấp nhận: {string.Join(", ", StorageService.AllowedExtensions)}.");
                }
            }
        }

        return new WorkSubmissionValidationResult(errors.Count == 0, errors);
    }
}
