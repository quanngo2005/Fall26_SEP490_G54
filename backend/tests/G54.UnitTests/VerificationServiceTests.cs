using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Verification;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace G54.UnitTests;

public sealed class VerificationServiceTests
{
    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static (AppDbContext Context, VerificationService Service) CreateTestEnvironment()
    {
        var context = CreateInMemoryDbContext();
        var service = new VerificationService(context, NullLogger<VerificationService>.Instance);
        return (context, service);
    }

    private static async Task<(Department Dept, JobPosition Pos, Employee Maker, Employee Checker, Account CheckerAccount, Work Work, ResultSubmission Submission)> SeedBasicSubmissionAsync(AppDbContext context)
    {
        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Code = "D-RET",
            Name = "Khối Bán Lẻ",
            LevelType = DepartmentLevelType.Department,
            Status = RecordStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var pos = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = "P-TLR",
            Title = "Giao dịch viên",
            Status = RecordStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        context.Departments.Add(dept);
        context.JobPositions.Add(pos);

        var maker = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "EMP-001",
            FullName = "Nguyễn Văn Maker",
            Email = "maker@bpms.com",
            DepartmentId = dept.Id,
            JobPositionId = pos.Id,
            Status = EmployeeStatus.Active,
            LastSyncAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var checker = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "EMP-002",
            FullName = "Trần Thị Checker",
            Email = "checker@bpms.com",
            DepartmentId = dept.Id,
            JobPositionId = pos.Id,
            Status = EmployeeStatus.Active,
            LastSyncAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        context.Employees.AddRange(maker, checker);

        var checkerAccount = new Account
        {
            Id = Guid.NewGuid(),
            EmployeeId = checker.Id,
            Username = "checker",
            PasswordHash = "hash",
            LastSyncAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        context.Accounts.Add(checkerAccount);

        var work = new Work
        {
            Id = Guid.NewGuid(),
            Code = "WRK-2026-001",
            Title = "Phát hành thẻ tín dụng đợt 1",
            AssigneeEmployeeId = maker.Id,
            CreatedBy = maker.Id,
            Status = WorkStatus.Submitted,
            Priority = WorkPriority.Medium,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        context.Works.Add(work);

        var submission = new ResultSubmission
        {
            Id = Guid.NewGuid(),
            WorkId = work.Id,
            SubmitterEmployeeId = maker.Id,
            SubmissionNo = 1,
            Comment = "Đã phát hành đủ 15 thẻ.",
            Status = SubmissionStatus.Submitted,
            SubmittedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        context.ResultSubmissions.Add(submission);

        await context.SaveChangesAsync();
        return (dept, pos, maker, checker, checkerAccount, work, submission);
    }

    [Fact]
    public async Task GetDossierAsync_WhenSubmissionExists_ReturnsMappedDossier()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (_, _, maker, _, _, work, submission) = await SeedBasicSubmissionAsync(context);

            var evidence = new Evidence
            {
                Id = Guid.NewGuid(),
                ResultSubmissionId = submission.Id,
                FileName = "scan_the.pdf",
                FilePath = "uploads/scan_the.pdf",
                FileSize = 1024,
                MimeType = "application/pdf",
                UploadedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            context.Evidences.Add(evidence);
            await context.SaveChangesAsync();

            var result = await service.GetDossierAsync(submission.Id);

            Assert.NotNull(result);
            Assert.Equal(submission.Id, result.SubmissionId);
            Assert.Equal(work.Code, result.WorkCode);
            Assert.Equal(maker.FullName, result.Submitter.FullName);
            Assert.Single(result.EvidenceFiles);
            Assert.Equal("scan_the.pdf", result.EvidenceFiles[0].FileName);
            Assert.NotEmpty(result.ControlStandards);
        }
    }

    [Fact]
    public async Task GetDossierAsync_WhenSubmissionDoesNotExist_ThrowsNotFoundException()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var ex = await Assert.ThrowsAsync<NotFoundException>(() => service.GetDossierAsync(Guid.NewGuid()));
            Assert.Equal(ErrorCodes.NotFound, ex.ErrorCode);
            Assert.Equal(404, ex.StatusCode);
        }
    }

    [Fact]
    public async Task GetDossierAsync_ReturnsApiFormatStatus()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (_, _, _, _, _, _, submission) = await SeedBasicSubmissionAsync(context);
            submission.Status = SubmissionStatus.UnderReview;
            await context.SaveChangesAsync();

            var result = await service.GetDossierAsync(submission.Id);

            Assert.Equal("UNDER_REVIEW", result.Status);
        }
    }

    [Fact]
    public async Task GetHistoryAsync_WhenSubmissionDoesNotExist_ThrowsNotFoundException()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            await Assert.ThrowsAsync<NotFoundException>(() => service.GetHistoryAsync(Guid.NewGuid()));
        }
    }

    [Fact]
    public async Task GetHistoryAsync_MapsNeedMoreInfoToRevisionRequired()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (_, _, _, _, checkerAccount, _, submission) = await SeedBasicSubmissionAsync(context);
            await service.SubmitDecisionAsync(submission.Id, checkerAccount.Id, new SubmitVerificationDecisionDto
            {
                Decision = "REVISION_REQUIRED",
                Feedback = "Bổ sung chứng từ đối soát Core Banking.",
                ResubmitDeadline = DateTimeOffset.UtcNow.AddDays(1),
            });

            var history = await service.GetHistoryAsync(submission.Id);

            var round = Assert.Single(history);
            Assert.Equal("REVISION_REQUIRED", round.Decision);
        }
    }

    [Fact]
    public async Task SubmitDecisionAsync_WhenApproved_UpdatesStatusAndCreatesVerifiedWorkResult()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (_, _, _, _, checkerAccount, work, submission) = await SeedBasicSubmissionAsync(context);

            var payload = new SubmitVerificationDecisionDto
            {
                Decision = "APPROVED",
                VerifiedValue = 15m,
                Feedback = "Hồ sơ đầy đủ, chứng từ hợp lệ.",
            };

            var result = await service.SubmitDecisionAsync(submission.Id, checkerAccount.Id, payload);

            Assert.NotNull(result);
            Assert.Equal("APPROVED", result.Decision);
            Assert.Equal("APPROVED", result.SubmissionStatus);
            Assert.Equal("VERIFIED", result.WorkStatus);

            var updatedSubmission = await context.ResultSubmissions.FindAsync(submission.Id);
            Assert.NotNull(updatedSubmission);
            Assert.Equal(SubmissionStatus.Approved, updatedSubmission.Status);

            var updatedWork = await context.Works.FindAsync(work.Id);
            Assert.NotNull(updatedWork);
            Assert.Equal(WorkStatus.Verified, updatedWork.Status);

            var verification = await context.Verifications.SingleAsync(v => v.ResultSubmissionId == submission.Id);
            Assert.Equal(VerificationDecision.Approved, verification.Decision);
            Assert.NotNull(verification.VerifiedAt);

            var verifiedResult = await context.VerifiedWorkResults.SingleAsync(vr => vr.VerificationId == verification.Id);
            Assert.Equal(VerifiedResultStatus.Valid, verifiedResult.Status);
        }
    }

    [Fact]
    public async Task SubmitDecisionAsync_WhenRevisionRequired_UpdatesStatusAndSetsFeedback()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (_, _, _, _, checkerAccount, work, submission) = await SeedBasicSubmissionAsync(context);

            var payload = new SubmitVerificationDecisionDto
            {
                Decision = "REVISION_REQUIRED",
                Feedback = "Thiếu chữ ký xác nhận của khách hàng tại trang 3.",
                ResubmitDeadline = DateTimeOffset.UtcNow.AddDays(2),
            };

            var result = await service.SubmitDecisionAsync(submission.Id, checkerAccount.Id, payload);

            Assert.NotNull(result);
            Assert.Equal("REVISION_REQUIRED", result.Decision);
            Assert.Equal("UNDER_REVIEW", result.SubmissionStatus);
            Assert.Equal("IN_PROGRESS", result.WorkStatus);

            var updatedSubmission = await context.ResultSubmissions.FindAsync(submission.Id);
            Assert.NotNull(updatedSubmission);
            Assert.Equal(SubmissionStatus.UnderReview, updatedSubmission.Status);

            var updatedWork = await context.Works.FindAsync(work.Id);
            Assert.NotNull(updatedWork);
            Assert.Equal(WorkStatus.InProgress, updatedWork.Status);

            var verification = await context.Verifications.SingleAsync(v => v.ResultSubmissionId == submission.Id);
            Assert.Equal(VerificationDecision.NeedMoreInfo, verification.Decision);
            Assert.Equal("Thiếu chữ ký xác nhận của khách hàng tại trang 3.", verification.Comment);
        }
    }

    [Fact]
    public async Task SubmitDecisionAsync_WhenRejected_UpdatesStatusToRejected()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (_, _, _, _, checkerAccount, work, submission) = await SeedBasicSubmissionAsync(context);

            var payload = new SubmitVerificationDecisionDto
            {
                Decision = "REJECTED",
                Feedback = "Phát hiện chứng từ có dấu hiệu trùng lặp gian lận.",
            };

            var result = await service.SubmitDecisionAsync(submission.Id, checkerAccount.Id, payload);

            Assert.NotNull(result);
            Assert.Equal("REJECTED", result.Decision);
            Assert.Equal("REJECTED", result.SubmissionStatus);
            Assert.Equal("REJECTED", result.WorkStatus);

            var updatedSubmission = await context.ResultSubmissions.FindAsync(submission.Id);
            Assert.NotNull(updatedSubmission);
            Assert.Equal(SubmissionStatus.Rejected, updatedSubmission.Status);

            var updatedWork = await context.Works.FindAsync(work.Id);
            Assert.NotNull(updatedWork);
            Assert.Equal(WorkStatus.Rejected, updatedWork.Status);
        }
    }

    [Fact]
    public async Task SubmitDecisionAsync_WhenMakerIsChecker_ThrowsBusinessRuleException_MSG10()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (_, _, maker, _, _, _, submission) = await SeedBasicSubmissionAsync(context);

            var makerAccount = new Account
            {
                Id = Guid.NewGuid(),
                EmployeeId = maker.Id,
                Username = "maker_acc",
                PasswordHash = "hash",
                LastSyncAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            context.Accounts.Add(makerAccount);
            await context.SaveChangesAsync();

            var payload = new SubmitVerificationDecisionDto
            {
                Decision = "APPROVED",
                Feedback = "Tự duyệt kết quả của chính mình.",
            };

            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                service.SubmitDecisionAsync(submission.Id, makerAccount.Id, payload));

            Assert.Equal(ErrorCodes.MSG10, ex.ErrorCode);
            Assert.Equal(422, ex.StatusCode);
        }
    }

    [Theory]
    [InlineData(SubmissionStatus.Rejected)]
    [InlineData(SubmissionStatus.Withdrawn)]
    [InlineData(SubmissionStatus.Draft)]
    public async Task SubmitDecisionAsync_WhenNotAwaitingVerification_ThrowsVerificationInvalidState(SubmissionStatus status)
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (_, _, _, _, checkerAccount, _, submission) = await SeedBasicSubmissionAsync(context);
            submission.Status = status;
            await context.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<ConflictException>(() =>
                service.SubmitDecisionAsync(submission.Id, checkerAccount.Id, new SubmitVerificationDecisionDto
                {
                    Decision = "APPROVED",
                    VerifiedValue = 1m,
                }));

            Assert.Equal(ErrorCodes.VerificationInvalidState, ex.ErrorCode);
            Assert.Equal(409, ex.StatusCode);
        }
    }

    [Fact]
    public async Task SubmitDecisionAsync_WhenAlreadyApproved_ThrowsBusinessRuleException_MSG23()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (_, _, _, _, checkerAccount, _, submission) = await SeedBasicSubmissionAsync(context);
            submission.Status = SubmissionStatus.Approved;
            await context.SaveChangesAsync();

            var payload = new SubmitVerificationDecisionDto
            {
                Decision = "APPROVED",
                Feedback = "Cố gắng duyệt lần 2.",
            };

            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                service.SubmitDecisionAsync(submission.Id, checkerAccount.Id, payload));

            Assert.Equal(ErrorCodes.MSG23, ex.ErrorCode);
            Assert.Equal(409, ex.StatusCode);
        }
    }

    [Fact]
    public async Task SubmitDecisionAsync_WhenRevisionRequiredWithoutFeedbackOrDeadline_ThrowsValidationException()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (_, _, _, _, checkerAccount, _, submission) = await SeedBasicSubmissionAsync(context);

            var missingFeedbackPayload = new SubmitVerificationDecisionDto
            {
                Decision = "REVISION_REQUIRED",
                Feedback = "ngắn", // < 10 chars
                ResubmitDeadline = DateTimeOffset.UtcNow.AddDays(1),
            };

            await Assert.ThrowsAsync<ValidationException>(() =>
                service.SubmitDecisionAsync(submission.Id, checkerAccount.Id, missingFeedbackPayload));

            var missingDeadlinePayload = new SubmitVerificationDecisionDto
            {
                Decision = "REVISION_REQUIRED",
                Feedback = "Lý do hợp lệ trên mười ký tự",
                ResubmitDeadline = null,
            };

            await Assert.ThrowsAsync<ValidationException>(() =>
                service.SubmitDecisionAsync(submission.Id, checkerAccount.Id, missingDeadlinePayload));

            var pastDeadlinePayload = new SubmitVerificationDecisionDto
            {
                Decision = "REVISION_REQUIRED",
                Feedback = "Lý do hợp lệ trên mười ký tự",
                ResubmitDeadline = DateTimeOffset.UtcNow.AddHours(-1),
            };

            await Assert.ThrowsAsync<ValidationException>(() =>
                service.SubmitDecisionAsync(submission.Id, checkerAccount.Id, pastDeadlinePayload));
        }
    }

    [Fact]
    public async Task SubmitDecisionAsync_MultipleRounds_IncrementsRoundNo()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (_, _, _, checker, checkerAccount, _, submission) = await SeedBasicSubmissionAsync(context);

            var existingRound1 = new Verification
            {
                Id = Guid.NewGuid(),
                ResultSubmissionId = submission.Id,
                VerifierEmployeeId = checker.Id,
                RoundNo = 1,
                Decision = VerificationDecision.NeedMoreInfo,
                Comment = "Vòng 1 chưa đạt",
                VerifiedAt = DateTimeOffset.UtcNow.AddDays(-1),
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            };
            context.Verifications.Add(existingRound1);
            await context.SaveChangesAsync();

            var payloadRound2 = new SubmitVerificationDecisionDto
            {
                Decision = "APPROVED",
                Feedback = "Vòng 2 đạt chuẩn.",
            };

            var result = await service.SubmitDecisionAsync(submission.Id, checkerAccount.Id, payloadRound2);

            Assert.NotNull(result);
            var round2Verification = await context.Verifications.SingleAsync(v => v.Id == result.VerificationId);
            Assert.Equal(2, round2Verification.RoundNo);
        }
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsAllVerificationRoundsOrdered()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (_, _, _, checker, _, _, submission) = await SeedBasicSubmissionAsync(context);

            var v1 = new Verification
            {
                Id = Guid.NewGuid(),
                ResultSubmissionId = submission.Id,
                VerifierEmployeeId = checker.Id,
                RoundNo = 1,
                Decision = VerificationDecision.NeedMoreInfo,
                Comment = "Vòng 1",
                VerifiedAt = DateTimeOffset.UtcNow.AddDays(-2),
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-2),
            };
            var v2 = new Verification
            {
                Id = Guid.NewGuid(),
                ResultSubmissionId = submission.Id,
                VerifierEmployeeId = checker.Id,
                RoundNo = 2,
                Decision = VerificationDecision.Approved,
                Comment = "Vòng 2",
                VerifiedAt = DateTimeOffset.UtcNow.AddDays(-1),
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            };
            context.Verifications.AddRange(v1, v2);
            await context.SaveChangesAsync();

            var history = await service.GetHistoryAsync(submission.Id);

            Assert.Equal(2, history.Count);
            Assert.Equal(1, history[0].RoundNo);
            Assert.Equal(2, history[1].RoundNo);
            Assert.Equal("APPROVED", history[1].Decision);
        }
    }

    [Fact]
    public async Task SubmitDecisionAsync_WhenDecidingAgainAfterRevisionRequest_AllowsTheSecondDecision()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (_, _, _, _, checkerAccount, work, submission) = await SeedBasicSubmissionAsync(context);

            await service.SubmitDecisionAsync(submission.Id, checkerAccount.Id, new SubmitVerificationDecisionDto
            {
                Decision = "REVISION_REQUIRED",
                Feedback = "Bo sung chu ky xac nhan o trang 3.",
                ResubmitDeadline = DateTimeOffset.UtcNow.AddDays(2),
            });

            var second = await service.SubmitDecisionAsync(submission.Id, checkerAccount.Id, new SubmitVerificationDecisionDto
            {
                Decision = "APPROVED",
                VerifiedValue = 1m,
            });

            Assert.Equal("APPROVED", second.Decision);
            Assert.Equal("APPROVED", second.SubmissionStatus);
            Assert.Equal("VERIFIED", second.WorkStatus);
            Assert.Equal(WorkStatus.Verified, (await context.Works.FindAsync(work.Id))!.Status);
        }
    }
}
