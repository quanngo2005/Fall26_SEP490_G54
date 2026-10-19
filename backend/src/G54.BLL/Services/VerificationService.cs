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
    ILogger<VerificationService> logger) : IVerificationService
{
    private static readonly Action<ILogger, string, Guid, Guid, Exception?> LogVerificationAudit =
        LoggerMessage.Define<string, Guid, Guid>(
            LogLevel.Information,
            new EventId(1, "VerificationAudit"),
            "Verification audit: Decision={Decision}, SubmissionId={SubmissionId}, VerifierId={VerifierId}");

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
                $"/api/v1/evidence/{e.Id}/preview",
                $"/api/v1/evidence/{e.Id}/download"))
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
            submission.Status.ToWire());
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

        if (submission.Status is not (SubmissionStatus.Submitted or SubmissionStatus.UnderReview))
        {
            throw new ConflictException(
                $"Hồ sơ đang ở trạng thái {submission.Status.ToWire()}, không thể thẩm định.",
                ErrorCodes.VerificationInvalidState);
        }

        var normalizedDecision = dto.Decision.Trim().ToUpperInvariant();
        var (dbDecision, newSubmissionStatus, workTrigger, actionText) = normalizedDecision switch
        {
            "APPROVED" => (
                VerificationDecision.Approved,
                SubmissionStatus.Approved,
                WorkTrigger.Approve,
                "Phê duyệt kết quả hoàn tất"),
            "REVISION_REQUIRED" => (
                VerificationDecision.NeedMoreInfo,
                SubmissionStatus.UnderReview,
                WorkTrigger.RequestRevision,
                "Yêu cầu bổ sung/làm lại đã được gửi tới cán bộ nộp"),
            "REJECTED" => (
                VerificationDecision.Rejected,
                SubmissionStatus.Rejected,
                WorkTrigger.Reject,
                "Đã từ chối hồ sơ kết quả công việc"),
            _ => throw new ValidationException("Quyết định thẩm định không hợp lệ.")
        };

        if (!WorkStateMachine.TryFire(submission.Work.Status, workTrigger, out var newWorkStatus))
        {
            throw new ConflictException(
                $"Công việc đang ở trạng thái {submission.Work.Status.ToWire()}, không thể thẩm định.",
                ErrorCodes.VerificationInvalidState);
        }

        var nextRoundNo = submission.Verifications.Count > 0
            ? submission.Verifications.Max(v => v.RoundNo) + 1
            : 1;

        var now = DateTimeOffset.UtcNow;
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
                var resultItem = new WorkResultItem
                {
                    Id = Guid.NewGuid(),
                    VerifiedWorkResultId = verifiedResult.Id,
                    KpiDefinitionId = primaryKpiId.Value,
                    Value = dto.VerifiedValue.Value,
                    UpdatedAt = now,
                };
                db.WorkResultItems.Add(resultItem);
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        LogVerificationAudit(logger, normalizedDecision, submission.Id, verifier.Id, null);

        var message = $"Quyết định thẩm định đã được ghi nhận: {actionText}.";

        return new VerificationDecisionResultDto(
            verification.Id,
            dbDecision.ToApiDecision(),
            newSubmissionStatus.ToWire(),
            newWorkStatus.ToWire(),
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
}
