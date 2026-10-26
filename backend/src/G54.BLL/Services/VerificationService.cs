using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Common.Workflow;
using G54.BLL.Dtos.Verification;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace G54.BLL.Services;

public sealed class VerificationService(
    AppDbContext db,
    ILogger<VerificationService> logger,
    TimeProvider time) : IVerificationService
{
    private static readonly Action<ILogger, string, Guid, Guid, Exception?> LogVerificationAudit =
        LoggerMessage.Define<string, Guid, Guid>(
            LogLevel.Information,
            new EventId(1, "VerificationAudit"),
            "Verification audit: Decision={Decision}, SubmissionId={SubmissionId}, VerifierId={VerifierId}");

    private static readonly Action<ILogger, Guid, Guid, Exception?> LogContributionSkipped =
        LoggerMessage.Define<Guid, Guid>(
            LogLevel.Warning,
            new EventId(2, "ContributionSkippedNoActiveTarget"),
            "KPI contribution skipped: EmployeeId={EmployeeId} has no active target for KpiDefinitionId={KpiDefinitionId}");

    private const string DossierNotFoundMessage = "Không tìm thấy hồ sơ nộp kết quả công việc yêu cầu thẩm định.";

    public async Task<VerificationDossierDto> GetDossierAsync(Guid submissionId, CancellationToken cancellationToken = default)
    {
        var submission = await db.ResultSubmissions
            .AsNoTracking()
            .Include(s => s.SubmitterEmployee)
                .ThenInclude(e => e.Department)
            .Include(s => s.SubmitterEmployee)
                .ThenInclude(e => e.JobPosition)
            .Include(s => s.Work)
                .ThenInclude(w => w.WorkType)
                    .ThenInclude(wt => wt!.WorkTypeKpis)
                        .ThenInclude(k => k.KpiDefinition)
            .Include(s => s.Evidences)
            .FirstOrDefaultAsync(s => s.Id == submissionId, cancellationToken)
            ?? throw new NotFoundException(DossierNotFoundMessage);

        var submitter = new SubmitterInfoDto(
            submission.SubmitterEmployeeId,
            submission.SubmitterEmployee?.EmployeeCode ?? string.Empty,
            submission.SubmitterEmployee?.FullName ?? "Cán bộ nộp",
            submission.SubmitterEmployee?.JobPosition?.Title ?? "Nhân viên",
            submission.SubmitterEmployee?.Department?.Name ?? "Phòng ban");

        var primaryKpi = submission.Work.WorkType?.WorkTypeKpis.FirstOrDefault()?.KpiDefinition;
        KpiItemDto? kpiItem = null;
        if (primaryKpi is not null)
        {
            kpiItem = new KpiItemDto(
                primaryKpi.Id,
                primaryKpi.Code,
                primaryKpi.Name,
                15.0m, // Giá trị mặc định mô phỏng sản lượng nộp
                primaryKpi.Unit);
        }

        var evidenceFiles = submission.Evidences
            .OrderBy(e => e.FileName)
            .Select(e => new EvidenceItemDto(
                e.Id,
                e.FileName,
                e.FileSize,
                e.MimeType,
                $"/evidence/{e.Id}/preview",
                $"/evidence/{e.Id}/download"))
            .ToList();

        var controlStandards = new List<ControlStandardDto>
        {
            new("STD-01", "Số lượng khớp với báo cáo đối soát Core Banking", true),
            new("STD-02", "Báo cáo nộp đúng hạn thỏa thuận SLA", true),
            new("STD-03", "Tuân thủ chữ ký pháp lý trên hợp đồng tín dụng", true),
        };

        var submissionCode = $"#SUB-{submission.Work.Code}-V{submission.SubmissionNo}";

        return new VerificationDossierDto(
            submission.Id,
            submissionCode,
            submission.SubmissionNo,
            submission.WorkId,
            submission.Work.Code,
            submission.Work.Title,
            submitter,
            submission.SubmittedAt,
            kpiItem,
            submission.Comment,
            evidenceFiles,
            controlStandards,
            submission.Status.ToEnumString());
    }

    public async Task<VerificationDecisionResultDto> SubmitDecisionAsync(
        Guid submissionId,
        Guid actorAccountId,
        SubmitVerificationDecisionDto dto,
        CancellationToken cancellationToken = default)
    {
        ValidateDecisionPayload(dto);

        var account = await db.Accounts
            .Include(a => a.Employee)
            .FirstOrDefaultAsync(a => a.Id == actorAccountId, cancellationToken);

        if (account?.Employee is null)
        {
            throw new UnauthorizedException("Không xác định được thông tin nhân sự thẩm định từ tài khoản hiện tại.");
        }

        var verifier = account.Employee;

        var submission = await db.ResultSubmissions
            .Include(s => s.Work)
                .ThenInclude(w => w.WorkType)
                    .ThenInclude(wt => wt!.WorkTypeKpis)
            .Include(s => s.Verifications)
            .FirstOrDefaultAsync(s => s.Id == submissionId, cancellationToken);

        if (submission is null)
        {
            throw new NotFoundException(DossierNotFoundMessage);
        }

        // BR-02: Maker-Checker rule: Verifier cannot be the submitter (business rule violation → 422)
        if (submission.SubmitterEmployeeId == verifier.Id)
        {
            throw new BusinessRuleException(ErrorCodes.MSG10, statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        // BR-13: Approved submission is permanently locked
        if (submission.Status == SubmissionStatus.Approved)
        {
            throw new BusinessRuleException(ErrorCodes.MSG23, statusCode: StatusCodes.Status409Conflict);
        }

        var normalizedDecision = dto.Decision.Trim().ToUpperInvariant();
        var (dbDecision, action, workAction, actionText) = normalizedDecision switch
        {
            "APPROVED" => (
                VerificationDecision.Approved,
                SubmissionAction.Approve,
                WorkAction.Verify,
                "Phê duyệt kết quả hoàn tất"),
            "REVISION_REQUIRED" => (
                VerificationDecision.NeedMoreInfo,
                SubmissionAction.RequestRevision,
                WorkAction.Reopen,
                "Yêu cầu bổ sung/làm lại đã được gửi tới cán bộ nộp"),
            "REJECTED" => (
                VerificationDecision.Rejected,
                SubmissionAction.Reject,
                WorkAction.Reject,
                "Đã từ chối hồ sơ kết quả công việc"),
            _ => throw new ValidationException("Quyết định thẩm định không hợp lệ.")
        };

        if (!SubmissionStateMachine.TryGetNextStatus(submission.Status, action, out var newSubmissionStatus))
        {
            throw new ConflictException(
                $"Hồ sơ đang ở trạng thái {submission.Status.ToEnumString()}, không thể thẩm định.",
                ErrorCodes.VerificationInvalidState);
        }

        if (!WorkStateMachine.TryGetNextStatus(submission.Work.Status, workAction, out var newWorkStatus))
        {
            throw new ConflictException(
                $"Công việc đang ở trạng thái {submission.Work.Status.ToEnumString()}, không thể thẩm định.",
                ErrorCodes.VerificationInvalidState);
        }

        var nextRoundNo = submission.Verifications.Count > 0
            ? submission.Verifications.Max(v => v.RoundNo) + 1
            : 1;

        var now = time.GetUtcNow();
        var verification = new Verification
        {
            Id = Guid.NewGuid(),
            ResultSubmissionId = submission.Id,
            VerifierEmployeeId = verifier.Id,
            RoundNo = nextRoundNo,
            Decision = dbDecision,
            Comment = dto.Feedback?.Trim(),
            VerifiedAt = now,
            UpdatedAt = now,
        };

        db.Verifications.Add(verification);

        submission.Status = newSubmissionStatus;
        submission.UpdatedAt = now;

        submission.Work.Status = newWorkStatus;
        submission.Work.UpdatedAt = now;

        if (dbDecision == VerificationDecision.Approved)
        {
            // A work item (Task ID) may contribute KPI to the system only once.
            var alreadyContributed = await db.WorkResultItems.AnyAsync(
                i => i.VerifiedWorkResult.Verification.ResultSubmission.WorkId == submission.WorkId,
                cancellationToken);
            if (alreadyContributed)
            {
                throw new BusinessRuleException(ErrorCodes.MSG22, statusCode: StatusCodes.Status409Conflict);
            }

            var verifiedResult = new VerifiedWorkResult
            {
                Id = Guid.NewGuid(),
                VerificationId = verification.Id,
                VerifiedAt = now,
                UpdatedAt = now,
                Status = VerifiedResultStatus.Valid,
            };
            db.VerifiedWorkResults.Add(verifiedResult);

            var primaryKpiId = submission.Work.WorkType?.WorkTypeKpis.FirstOrDefault()?.KpiDefinitionId;
            if (primaryKpiId.HasValue && dto.VerifiedValue.HasValue)
            {
                var handover = await db.TaskHandovers
                    .AsNoTracking()
                    .Where(h => h.WorkId == submission.WorkId && h.Status == HandoverStatus.Accepted)
                    .OrderByDescending(h => h.AcceptedAt ?? h.HandoverAt)
                    .FirstOrDefaultAsync(cancellationToken);

                var contributions = handover is null
                    ? [(submission.SubmitterEmployeeId, dto.VerifiedValue.Value)]
                    : SplitHandoverContributions(handover, dto.VerifiedValue.Value);

                foreach (var (employeeId, value) in contributions)
                {
                    await AddResultItemAsync(
                        verifiedResult.Id,
                        primaryKpiId.Value,
                        employeeId,
                        value,
                        now,
                        cancellationToken);
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        LogVerificationAudit(logger, normalizedDecision, submission.Id, verifier.Id, null);

        var message = $"Quyết định thẩm định đã được ghi nhận: {actionText}.";

        return new VerificationDecisionResultDto(
            verification.Id,
            dbDecision.ToApiDecision(),
            newSubmissionStatus.ToEnumString(),
            newWorkStatus.ToEnumString(),
            now,
            message);
    }

    public async Task<List<VerificationHistoryDto>> GetHistoryAsync(Guid submissionId, CancellationToken cancellationToken = default)
    {
        if (!await db.ResultSubmissions.AnyAsync(s => s.Id == submissionId, cancellationToken))
        {
            throw new NotFoundException(DossierNotFoundMessage);
        }

        var verifications = await db.Verifications
            .AsNoTracking()
            .Include(v => v.VerifierEmployee)
            .Where(v => v.ResultSubmissionId == submissionId)
            .OrderBy(v => v.RoundNo)
            .ToListAsync(cancellationToken);

        return verifications
            .Select(v => new VerificationHistoryDto(
                v.Id,
                v.ResultSubmissionId,
                v.RoundNo,
                v.Decision.ToApiDecision(),
                v.Comment,
                v.VerifierEmployeeId,
                v.VerifierEmployee.FullName,
                v.VerifiedAt))
            .ToList();
    }

    private static void ValidateDecisionPayload(SubmitVerificationDecisionDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Decision))
        {
            throw new ValidationException("Quyết định thẩm định là bắt buộc.");
        }

        var dec = dto.Decision.Trim().ToUpperInvariant();
        if (dec is not ("APPROVED" or "REVISION_REQUIRED" or "REJECTED"))
        {
            throw new ValidationException("Quyết định thẩm định chỉ được là APPROVED, REVISION_REQUIRED hoặc REJECTED.");
        }

        if (dec is "REVISION_REQUIRED" or "REJECTED")
        {
            if (string.IsNullOrWhiteSpace(dto.Feedback) || dto.Feedback.Trim().Length < 10)
            {
                throw new ValidationException("Ý kiến chỉ đạo/lý do là bắt buộc và phải có độ dài tối thiểu 10 ký tự khi yêu cầu làm lại hoặc từ chối.");
            }
        }

        if (dec is "REVISION_REQUIRED")
        {
            if (!dto.ResubmitDeadline.HasValue)
            {
                throw new ValidationException("Hạn chót nộp lại là bắt buộc khi yêu cầu sửa đổi hồ sơ.");
            }

            if (dto.ResubmitDeadline.Value <= DateTimeOffset.UtcNow)
            {
                throw new ValidationException("Hạn chót nộp lại phải lớn hơn thời điểm hiện tại.");
            }
        }

        if (dec is "APPROVED" && dto.VerifiedValue.HasValue && dto.VerifiedValue.Value < 0)
        {
            throw new ValidationException("Sản lượng công nhận không được là số âm.");
        }
    }

    private static (Guid EmployeeId, decimal Value)[] SplitHandoverContributions(TaskHandover handover, decimal totalPoints)
    {
        var (referrerPoints, processorPoints) = ReferralPointSplit.Split(totalPoints, handover.ReferrerSharePercent);
        return
        [
            (handover.FromEmployeeId, referrerPoints),
            (handover.ToEmployeeId, processorPoints),
        ];
    }

    private async Task AddResultItemAsync(
        Guid verifiedWorkResultId,
        Guid kpiDefinitionId,
        Guid employeeId,
        decimal value,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var progress = await ResolveProgressAsync(
            kpiDefinitionId,
            employeeId,
            time.GetBusinessToday(),
            value,
            now,
            cancellationToken);

        // A verified result item must feed an employee target's progress; without an active target there is nothing to attribute it to.
        if (progress is null)
        {
            LogContributionSkipped(logger, employeeId, kpiDefinitionId, null);
            return;
        }

        db.WorkResultItems.Add(new WorkResultItem
        {
            Id = Guid.NewGuid(),
            VerifiedWorkResultId = verifiedWorkResultId,
            ActualProgressId = progress.Id,
            EmployeeId = employeeId,
            Value = value,
            UpdatedAt = now,
        });
    }

    private async Task<ActualProgress?> ResolveProgressAsync(
        Guid kpiDefinitionId,
        Guid employeeId,
        DateOnly progressDate,
        decimal value,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var target = await db.TargetAssignments
            .Where(t => t.KpiDefinitionId == kpiDefinitionId
                && t.EmployeeId == employeeId
                && t.Status == TargetAssignmentStatus.Active
                && t.EvaluationPeriod.StartDate <= progressDate
                && t.EvaluationPeriod.EndDate >= progressDate)
            .OrderBy(t => t.EvaluationPeriod.EndDate)
            .FirstOrDefaultAsync(cancellationToken);
        if (target is null)
        {
            return null;
        }

        var progress = db.ActualProgresses.Local
                .FirstOrDefault(p => p.TargetAssignmentId == target.Id && p.ProgressDate == progressDate)
            ?? await db.ActualProgresses.FirstOrDefaultAsync(
                p => p.TargetAssignmentId == target.Id && p.ProgressDate == progressDate,
                cancellationToken);
        if (progress is null)
        {
            progress = new ActualProgress
            {
                Id = Guid.NewGuid(),
                TargetAssignmentId = target.Id,
                ProgressDate = progressDate,
                Value = 0,
                Status = ActualProgressStatus.Valid,
                UpdatedAt = now,
            };
            db.ActualProgresses.Add(progress);
        }

        progress.Value += value;
        progress.UpdatedAt = now;
        return progress;
    }
}
