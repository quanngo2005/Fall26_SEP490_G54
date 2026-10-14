using System.Text.Json;
using System.Text.RegularExpressions;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Submission;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace G54.BLL.Services;

public sealed partial class ResultSubmissionService(
    IUnitOfWork unitOfWork,
    IStorageService storageService,
    IWorkSubmissionValidator validator,
    IEventBus eventBus,
    ILogger<ResultSubmissionService> logger) : IResultSubmissionService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly Action<ILogger, string, Guid, WorkStatus, WorkStatus, Guid, Exception?> LogWorkflowTransition =
        LoggerMessage.Define<string, Guid, WorkStatus, WorkStatus, Guid>(
            LogLevel.Information,
            new EventId(1, "WorkflowTransition"),
            "Workflow transition logged: Work {WorkCode} ({WorkId}) moved from {FromState} to {ToState} by Actor {ActorId} with Action SUBMIT");

    private static readonly Action<ILogger, int, Guid, string, Guid, Exception?> LogSubmissionWithdrawn =
        LoggerMessage.Define<int, Guid, string, Guid>(
            LogLevel.Information,
            new EventId(2, "SubmissionWithdrawn"),
            "Submission #{SubmissionNo} (ID: {SubmissionId}) on Work {WorkCode} withdrawn by Submitter {EmployeeId}. Work status reverted to IN_PROGRESS");

    [GeneratedRegex(@"<!--\s*METRICS_JSON:\s*(.*?)\s*-->", RegexOptions.Singleline)]
    private static partial Regex MetricsMarkerRegex();

    public async Task<SubmissionContextDto> GetSubmissionContextAsync(
        Guid workId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var employee = await ResolveEmployeeAsync(userId, cancellationToken);
        var work = await unitOfWork.Works.GetWithDetailsAsync(workId, cancellationToken);
        if (work is null)
        {
            throw new NotFoundException("Công việc", workId);
        }

        if (work.AssigneeEmployeeId != employee.Id)
        {
            throw new ForbiddenException("Bạn không phải là người được phân công thực hiện công việc này.");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var kpiCriteria = new List<SubmissionKpiCriteriaDto>();

        if (work.WorkTypeId.HasValue)
        {
            var workTypeKpis = await unitOfWork.WorkTypeKpis.Query()
                .Include(wtk => wtk.KpiDefinition)
                .Where(wtk => wtk.WorkTypeId == work.WorkTypeId.Value
                    && wtk.EffectiveFrom <= today
                    && (wtk.EffectiveTo == null || wtk.EffectiveTo >= today))
                .ToListAsync(cancellationToken);

            foreach (var wtk in workTypeKpis.OrderByDescending(k => k.IsRequired).ThenBy(k => k.KpiDefinition.Code))
            {
                kpiCriteria.Add(new SubmissionKpiCriteriaDto(
                    KpiDefinitionId: wtk.KpiDefinitionId,
                    Code: wtk.KpiDefinition.Code,
                    Name: wtk.KpiDefinition.Name,
                    Description: wtk.KpiDefinition.Description,
                    Unit: wtk.KpiDefinition.Unit,
                    AggregationType: wtk.KpiDefinition.AggregationType.ToString(),
                    Direction: wtk.KpiDefinition.Direction.ToString(),
                    IsRequired: wtk.IsRequired,
                    Formula: wtk.KpiDefinition.Formula));
            }
        }

        var existingSubmissions = await unitOfWork.ResultSubmissions.Query()
            .Where(s => s.WorkId == workId)
            .ToListAsync(cancellationToken);

        var previousCount = existingSubmissions.Count;
        var nextSubmissionNo = previousCount > 0 ? existingSubmissions.Max(s => s.SubmissionNo) + 1 : 1;

        return new SubmissionContextDto(
            WorkId: work.Id,
            WorkCode: work.Code,
            WorkTitle: work.Title,
            WorkStatus: work.Status.ToString(),
            AssigneeEmployeeId: work.AssigneeEmployeeId,
            AssigneeEmployeeName: work.AssigneeEmployee?.FullName ?? "",
            AssigneeEmployeeCode: work.AssigneeEmployee?.EmployeeCode ?? "",
            WorkTypeId: work.WorkTypeId,
            WorkTypeName: work.WorkType?.Name,
            NextSubmissionNo: nextSubmissionNo,
            PreviousSubmissionsCount: previousCount,
            KpiCriteria: kpiCriteria,
            AllowedFileTypes: StorageService.AllowedExtensions.ToArray(),
            MaxFileSizeBytes: StorageService.DefaultMaxFileSizeBytes,
            MaxFileCount: 10);
    }

    public async Task<SubmissionResultDto> CreateSubmissionAsync(
        Guid workId,
        Guid userId,
        CreateWorkSubmissionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var employee = await ResolveEmployeeAsync(userId, cancellationToken);
        var work = await unitOfWork.Works.GetWithDetailsAsync(workId, cancellationToken);
        if (work is null)
        {
            throw new NotFoundException("Công việc", workId);
        }

        var validationResult = await validator.ValidateAsync(work, employee.Id, request, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.Errors);
        }

        IReadOnlyList<StoredFileMetadata> uploadedFiles = Array.Empty<StoredFileMetadata>();
        if (request.Files is { Count: > 0 })
        {
            uploadedFiles = await storageService.UploadEvidenceFilesAsync(request.Files, cancellationToken);
        }

        var parsedMetrics = ExtractMetricItems(request);
        var storedComment = BuildStoredComment(request.Comment, parsedMetrics);

        var submission = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var existingSubmissions = await unitOfWork.ResultSubmissions.Query(asNoTracking: false)
                .Where(s => s.WorkId == workId)
                .ToListAsync(ct);

            var nextSubmissionNo = existingSubmissions.Count > 0 ? existingSubmissions.Max(s => s.SubmissionNo) + 1 : 1;

            Guid? workflowDefinitionId = null;
            if (work.WorkTypeId.HasValue)
            {
                var workType = await unitOfWork.WorkTypes.GetByIdAsync(work.WorkTypeId.Value, ct);
                workflowDefinitionId = workType?.WorkflowDefinitionId;
            }

            var newSubmission = new ResultSubmission
            {
                Id = Guid.NewGuid(),
                WorkId = work.Id,
                SubmitterEmployeeId = employee.Id,
                WorkflowDefinitionId = workflowDefinitionId,
                SubmissionNo = nextSubmissionNo,
                Comment = storedComment,
                Status = SubmissionStatus.Submitted,
                SubmittedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };

            await unitOfWork.ResultSubmissions.AddAsync(newSubmission, ct);

            foreach (var file in uploadedFiles)
            {
                var evidence = new Evidence
                {
                    Id = Guid.NewGuid(),
                    ResultSubmissionId = newSubmission.Id,
                    FileName = file.FileName,
                    FilePath = file.FilePath,
                    MimeType = file.MimeType,
                    FileSize = file.FileSize,
                    Description = request.Comment?.Trim(),
                    UploadedBy = employee.Id,
                    UploadedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                };
                await unitOfWork.Evidences.AddAsync(evidence, ct);
            }

            var workToUpdate = await unitOfWork.Works.GetByIdAsync(work.Id, ct);
            if (workToUpdate is not null)
            {
                var fromState = workToUpdate.Status;
                workToUpdate.Status = WorkStatus.Submitted;
                workToUpdate.UpdatedAt = DateTimeOffset.UtcNow;
                unitOfWork.Works.Update(workToUpdate);

                LogWorkflowTransition(
                    logger,
                    workToUpdate.Code,
                    workToUpdate.Id,
                    fromState,
                    WorkStatus.Submitted,
                    employee.Id,
                    null);
            }

            return newSubmission;
        }, cancellationToken);

        await eventBus.PublishAsync(new WorkSubmittedEvent(
            SubmissionId: submission.Id,
            WorkId: work.Id,
            SubmitterId: employee.Id,
            SubmissionNo: submission.SubmissionNo,
            WorkCode: work.Code,
            WorkTitle: work.Title,
            SubmittedAt: submission.SubmittedAt), cancellationToken);

        return new SubmissionResultDto(
            SubmissionId: submission.Id,
            WorkId: work.Id,
            WorkCode: work.Code,
            SubmissionNo: submission.SubmissionNo,
            Status: submission.Status.ToString(),
            SubmittedAt: submission.SubmittedAt,
            EvidenceCount: uploadedFiles.Count,
            MetricCount: parsedMetrics.Count,
            Comment: request.Comment?.Trim());
    }

    public async Task<IReadOnlyList<SubmissionDetailDto>> GetSubmissionsByWorkIdAsync(
        Guid workId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await ResolveEmployeeAsync(userId, cancellationToken);
        var work = await unitOfWork.Works.GetByIdAsync(workId, cancellationToken);
        if (work is null)
        {
            throw new NotFoundException("Công việc", workId);
        }

        var submissions = await unitOfWork.ResultSubmissions.Query()
            .Include(s => s.SubmitterEmployee)
            .Include(s => s.Evidences)
            .Where(s => s.WorkId == workId)
            .OrderByDescending(s => s.SubmissionNo)
            .ToListAsync(cancellationToken);

        var kpiIds = new HashSet<Guid>();
        var parsedList = new List<(ResultSubmission Sub, string? CleanComment, List<SubmissionMetricInputDto> Metrics)>();

        foreach (var sub in submissions)
        {
            var (cleanComment, metrics) = ParseStoredComment(sub.Comment);
            parsedList.Add((sub, cleanComment, metrics));
            foreach (var m in metrics)
            {
                kpiIds.Add(m.KpiDefinitionId);
            }
        }

        var kpiDict = await unitOfWork.KpiDefinitions.Query()
            .Where(k => kpiIds.Contains(k.Id))
            .ToDictionaryAsync(k => k.Id, cancellationToken);

        var result = new List<SubmissionDetailDto>();
        foreach (var (sub, cleanComment, metrics) in parsedList)
        {
            var evidenceDtos = sub.Evidences.Select(e => new SubmissionEvidenceDto(
                Id: e.Id,
                FileName: e.FileName,
                FilePath: e.FilePath,
                MimeType: e.MimeType,
                FileSize: e.FileSize,
                UploadedAt: e.UploadedAt)).ToList();

            var metricDtos = metrics.Select(m =>
            {
                kpiDict.TryGetValue(m.KpiDefinitionId, out var def);
                return new SubmissionMetricItemDto(
                    KpiDefinitionId: m.KpiDefinitionId,
                    KpiCode: def?.Code ?? "UNKNOWN",
                    KpiName: def?.Name ?? "Unknown KPI",
                    Unit: def?.Unit ?? "",
                    Value: m.Value,
                    Denominator: m.Denominator);
            }).ToList();

            result.Add(new SubmissionDetailDto(
                Id: sub.Id,
                WorkId: work.Id,
                WorkCode: work.Code,
                WorkTitle: work.Title,
                SubmitterEmployeeId: sub.SubmitterEmployeeId,
                SubmitterEmployeeName: sub.SubmitterEmployee?.FullName ?? "",
                SubmitterEmployeeCode: sub.SubmitterEmployee?.EmployeeCode ?? "",
                SubmissionNo: sub.SubmissionNo,
                Status: sub.Status.ToString(),
                Comment: cleanComment,
                SubmittedAt: sub.SubmittedAt,
                UpdatedAt: sub.UpdatedAt,
                Evidences: evidenceDtos,
                Metrics: metricDtos));
        }

        return result;
    }

    public async Task<SubmissionDetailDto?> GetSubmissionByIdAsync(
        Guid submissionId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await ResolveEmployeeAsync(userId, cancellationToken);

        var submission = await unitOfWork.ResultSubmissions.Query()
            .Include(s => s.Work)
            .Include(s => s.SubmitterEmployee)
            .Include(s => s.Evidences)
            .FirstOrDefaultAsync(s => s.Id == submissionId, cancellationToken);

        if (submission is null)
        {
            return null;
        }

        var (cleanComment, metrics) = ParseStoredComment(submission.Comment);

        var kpiIds = metrics.Select(m => m.KpiDefinitionId).Distinct().ToList();
        var kpiDict = await unitOfWork.KpiDefinitions.Query()
            .Where(k => kpiIds.Contains(k.Id))
            .ToDictionaryAsync(k => k.Id, cancellationToken);

        var evidenceDtos = submission.Evidences.Select(e => new SubmissionEvidenceDto(
            Id: e.Id,
            FileName: e.FileName,
            FilePath: e.FilePath,
            MimeType: e.MimeType,
            FileSize: e.FileSize,
            UploadedAt: e.UploadedAt)).ToList();

        var metricDtos = metrics.Select(m =>
        {
            kpiDict.TryGetValue(m.KpiDefinitionId, out var def);
            return new SubmissionMetricItemDto(
                KpiDefinitionId: m.KpiDefinitionId,
                KpiCode: def?.Code ?? "UNKNOWN",
                KpiName: def?.Name ?? "Unknown KPI",
                Unit: def?.Unit ?? "",
                Value: m.Value,
                Denominator: m.Denominator);
        }).ToList();

        return new SubmissionDetailDto(
            Id: submission.Id,
            WorkId: submission.WorkId,
            WorkCode: submission.Work?.Code ?? "",
            WorkTitle: submission.Work?.Title ?? "",
            SubmitterEmployeeId: submission.SubmitterEmployeeId,
            SubmitterEmployeeName: submission.SubmitterEmployee?.FullName ?? "",
            SubmitterEmployeeCode: submission.SubmitterEmployee?.EmployeeCode ?? "",
            SubmissionNo: submission.SubmissionNo,
            Status: submission.Status.ToString(),
            Comment: cleanComment,
            SubmittedAt: submission.SubmittedAt,
            UpdatedAt: submission.UpdatedAt,
            Evidences: evidenceDtos,
            Metrics: metricDtos);
    }

    public async Task<SubmissionResultDto> WithdrawSubmissionAsync(
        Guid submissionId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var employee = await ResolveEmployeeAsync(userId, cancellationToken);

        var submission = await unitOfWork.ResultSubmissions.Query(asNoTracking: false)
            .Include(s => s.Work)
            .FirstOrDefaultAsync(s => s.Id == submissionId, cancellationToken);

        if (submission is null)
        {
            throw new NotFoundException("Đợt nộp kết quả", submissionId);
        }

        if (submission.SubmitterEmployeeId != employee.Id)
        {
            throw new ForbiddenException("Bạn không phải là người đã nộp báo cáo kết quả này.");
        }

        if (submission.Status != SubmissionStatus.Submitted)
        {
            throw new ValidationException(
                $"Chỉ có thể thu hồi báo cáo ở trạng thái đã nộp (SUBMITTED). Trạng thái hiện tại: {submission.Status}.");
        }

        var work = await unitOfWork.Works.GetByIdAsync(submission.WorkId, cancellationToken);
        if (work is null)
        {
            throw new NotFoundException("Công việc", submission.WorkId);
        }

        submission.Status = SubmissionStatus.Withdrawn;
        submission.UpdatedAt = DateTimeOffset.UtcNow;
        unitOfWork.ResultSubmissions.Update(submission);

        work.Status = WorkStatus.InProgress;
        work.UpdatedAt = DateTimeOffset.UtcNow;
        unitOfWork.Works.Update(work);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        LogSubmissionWithdrawn(logger, submission.SubmissionNo, submission.Id, work.Code, employee.Id, null);

        var (_, metrics) = ParseStoredComment(submission.Comment);

        return new SubmissionResultDto(
            SubmissionId: submission.Id,
            WorkId: work.Id,
            WorkCode: work.Code,
            SubmissionNo: submission.SubmissionNo,
            Status: submission.Status.ToString(),
            SubmittedAt: submission.SubmittedAt,
            EvidenceCount: submission.Evidences.Count,
            MetricCount: metrics.Count,
            Comment: "Thu hồi báo cáo thành công.");
    }

    private async Task<Employee> ResolveEmployeeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var employee = await unitOfWork.Employees.Query()
            .Include(e => e.Account)
            .FirstOrDefaultAsync(e => e.Id == userId || e.Account!.Id == userId, cancellationToken);

        if (employee is null || employee.Status != EmployeeStatus.Active)
        {
            throw new UnauthorizedException("Không tìm thấy thông tin nhân viên hợp lệ hoặc tài khoản đã bị khóa.");
        }

        return employee;
    }

    private static List<SubmissionMetricInputDto> ExtractMetricItems(CreateWorkSubmissionRequestDto request)
    {
        if (request.MetricItems is { Count: > 0 })
        {
            return request.MetricItems;
        }

        if (!string.IsNullOrWhiteSpace(request.MetricItemsJson))
        {
            try
            {
                return JsonSerializer.Deserialize<List<SubmissionMetricInputDto>>(
                    request.MetricItemsJson,
                    JsonOptions) ?? new();
            }
            catch
            {
                return new();
            }
        }

        return new();
    }

    private static string? BuildStoredComment(string? userComment, List<SubmissionMetricInputDto> metrics)
    {
        var trimmedComment = userComment?.Trim();
        if (metrics.Count == 0)
        {
            return trimmedComment;
        }

        var metricsJson = JsonSerializer.Serialize(metrics, JsonOptions);
        var marker = $"<!-- METRICS_JSON: {metricsJson} -->";

        return string.IsNullOrWhiteSpace(trimmedComment)
            ? marker
            : $"{trimmedComment}\n\n{marker}";
    }

    private static (string? CleanComment, List<SubmissionMetricInputDto> Metrics) ParseStoredComment(string? storedComment)
    {
        if (string.IsNullOrWhiteSpace(storedComment))
        {
            return (null, new List<SubmissionMetricInputDto>());
        }

        var match = MetricsMarkerRegex().Match(storedComment);
        if (!match.Success)
        {
            return (storedComment.Trim(), new List<SubmissionMetricInputDto>());
        }

        var cleanComment = storedComment.Replace(match.Value, "").Trim();
        var resultComment = string.IsNullOrEmpty(cleanComment) ? null : cleanComment;

        try
        {
            var json = match.Groups[1].Value;
            var metrics = JsonSerializer.Deserialize<List<SubmissionMetricInputDto>>(json, JsonOptions)
                ?? new List<SubmissionMetricInputDto>();
            return (resultComment, metrics);
        }
        catch
        {
            return (resultComment, new List<SubmissionMetricInputDto>());
        }
    }
}
