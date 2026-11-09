using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Permission;
using G54.BLL.Dtos.Quality;
using G54.BLL.Dtos.Verification;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace G54.UnitTests;

public sealed class QualityAssuranceServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 3, 0, 0, TimeSpan.Zero);
    private const string LongReason = "Số liệu trên chứng từ không khớp với báo cáo.";

    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private readonly RecordingNotifier _notifier = new();

    public void Dispose() => _db.Dispose();

    private QualityAssuranceService Service => new(_db, new FixedTime(Now), _notifier, NullLogger<QualityAssuranceService>.Instance);

    private async Task<Employee> AddEmployeeAsync(string code)
    {
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = code,
            FullName = $"Name {code}",
            Email = $"{code}@bank.test",
            DepartmentId = Guid.NewGuid(),
            JobPositionId = Guid.NewGuid(),
            Status = EmployeeStatus.Active,
            LastSyncAt = Now,
            UpdatedAt = Now,
        };
        _db.Employees.Add(employee);
        _db.Accounts.Add(new Account
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            Username = code,
            PasswordHash = "hash",
            LastSyncAt = Now,
            UpdatedAt = Now,
        });
        await _db.SaveChangesAsync();
        return employee;
    }

    /// <summary>An approved submission with a valid verified result; the work is VERIFIED.</summary>
    private async Task<(Employee Maker, Employee Qa, Work Work, ResultSubmission Submission, VerifiedWorkResult Result)> SeedApprovedAsync(
        SubmissionStatus status = SubmissionStatus.Approved,
        WorkStatus workStatus = WorkStatus.Verified)
    {
        var maker = await AddEmployeeAsync($"MK{Guid.NewGuid():N}"[..10]);
        var qa = await AddEmployeeAsync($"QA{Guid.NewGuid():N}"[..10]);
        var verifier = await AddEmployeeAsync($"VF{Guid.NewGuid():N}"[..10]);
        var work = new Work
        {
            Id = Guid.NewGuid(),
            Code = $"WRK-{Guid.NewGuid():N}"[..12],
            Title = "Công việc ngoài danh mục",
            AssigneeEmployeeId = maker.Id,
            CreatedBy = maker.Id,
            Status = workStatus,
            Priority = WorkPriority.Medium,
            UpdatedAt = Now,
        };
        var submission = new ResultSubmission
        {
            Id = Guid.NewGuid(),
            WorkId = work.Id,
            SubmitterEmployeeId = maker.Id,
            SubmissionNo = 1,
            Status = status,
            SubmittedAt = Now,
            UpdatedAt = Now,
        };
        var verification = new Verification
        {
            Id = Guid.NewGuid(),
            ResultSubmissionId = submission.Id,
            VerifierEmployeeId = verifier.Id,
            RoundNo = 1,
            Decision = VerificationDecision.Approved,
            VerifiedAt = Now,
            UpdatedAt = Now,
        };
        var result = new VerifiedWorkResult
        {
            Id = Guid.NewGuid(),
            VerificationId = verification.Id,
            VerifiedAt = Now,
            UpdatedAt = Now,
            Status = VerifiedResultStatus.Valid,
        };
        _db.Works.Add(work);
        _db.ResultSubmissions.Add(submission);
        _db.Verifications.Add(verification);
        _db.VerifiedWorkResults.Add(result);
        await _db.SaveChangesAsync();
        return (maker, qa, work, submission, result);
    }

    [Fact]
    public async Task CreateFlag_OnAnApprovedResult_FlagsTheSubmitter()
    {
        var (maker, qa, _, submission, _) = await SeedApprovedAsync();

        var flag = await Service.CreateFlagAsync(submission.Id, qa.Id, new CreateQualityFlagDto(LongReason, "high"));

        Assert.Equal("OPEN", flag.Status);
        Assert.Equal("HIGH", flag.Severity);
        Assert.Equal(maker.Id, flag.EmployeeId);
        Assert.Equal(qa.Id, flag.FlaggedByEmployeeId);
    }

    [Fact]
    public async Task CreateFlag_DefaultsSeverityToMedium()
    {
        var (_, qa, _, submission, _) = await SeedApprovedAsync();

        var flag = await Service.CreateFlagAsync(submission.Id, qa.Id, new CreateQualityFlagDto(LongReason, null));

        Assert.Equal("MEDIUM", flag.Severity);
    }

    [Fact]
    public async Task CreateFlag_OnAResultThatIsNotApproved_IsRefused()
    {
        var (_, qa, _, submission, _) = await SeedApprovedAsync(SubmissionStatus.Submitted, WorkStatus.Submitted);

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => Service.CreateFlagAsync(submission.Id, qa.Id, new CreateQualityFlagDto(LongReason, null)));

        Assert.Equal(ErrorCodes.VerificationInvalidState, ex.ErrorCode);
    }

    [Fact]
    public async Task CreateFlag_ByTheSubmitter_IsForbidden()
    {
        var (maker, _, _, submission, _) = await SeedApprovedAsync();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => Service.CreateFlagAsync(submission.Id, maker.Id, new CreateQualityFlagDto(LongReason, null)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("quá ngắn")]
    public async Task CreateFlag_RequiresAReason(string? reason)
    {
        var (_, qa, _, submission, _) = await SeedApprovedAsync();

        await Assert.ThrowsAsync<ValidationException>(
            () => Service.CreateFlagAsync(submission.Id, qa.Id, new CreateQualityFlagDto(reason, null)));
    }

    [Fact]
    public async Task CreateFlag_WhileAnotherFlagIsOpen_IsRefused()
    {
        var (_, qa, _, submission, _) = await SeedApprovedAsync();
        await Service.CreateFlagAsync(submission.Id, qa.Id, new CreateQualityFlagDto(LongReason, null));

        await Assert.ThrowsAsync<ConflictException>(
            () => Service.CreateFlagAsync(submission.Id, qa.Id, new CreateQualityFlagDto(LongReason, null)));
    }

    [Fact]
    public async Task RequestRework_InvalidatesTheResultReopensTheWorkAndTellsTheEmployee()
    {
        var (maker, qa, work, submission, result) = await SeedApprovedAsync();
        var flag = await Service.CreateFlagAsync(submission.Id, qa.Id, new CreateQualityFlagDto(LongReason, null));

        var reworked = await Service.RequestReworkAsync(flag.Id, qa.Id, new RequestReworkDto("Làm lại báo cáo và đính kèm chứng từ."));

        Assert.Equal("REWORK_REQUESTED", reworked.Status);
        Assert.Equal(Now, reworked.ReworkRequestedAt);
        var storedResult = await _db.VerifiedWorkResults.AsNoTracking().SingleAsync(r => r.Id == result.Id);
        Assert.Equal(VerifiedResultStatus.Invalid, storedResult.Status);
        Assert.Equal(Now, storedResult.InvalidatedAt);
        Assert.Equal(WorkStatus.InProgress, (await _db.Works.AsNoTracking().SingleAsync(w => w.Id == work.Id)).Status);
        var draft = Assert.Single(_notifier.Drafts);
        Assert.Equal(maker.Id, draft.RecipientEmployeeId);
        Assert.Equal(NotificationType.ReworkRequested, draft.Type);
        Assert.Equal(work.Id, draft.RelatedId);
    }

    [Fact]
    public async Task RequestRework_TwiceOnTheSameFlag_IsRefused()
    {
        var (_, qa, _, submission, _) = await SeedApprovedAsync();
        var flag = await Service.CreateFlagAsync(submission.Id, qa.Id, new CreateQualityFlagDto(LongReason, null));
        await Service.RequestReworkAsync(flag.Id, qa.Id, new RequestReworkDto("Làm lại báo cáo và đính kèm chứng từ."));

        await Assert.ThrowsAsync<ConflictException>(
            () => Service.RequestReworkAsync(flag.Id, qa.Id, new RequestReworkDto("Làm lại báo cáo và đính kèm chứng từ.")));
    }

    [Fact]
    public async Task RequestRework_WhenTheWorkIsAlreadyClosedAsDone_StillReopensIt()
    {
        var (_, qa, work, submission, _) = await SeedApprovedAsync(workStatus: WorkStatus.Done);
        var flag = await Service.CreateFlagAsync(submission.Id, qa.Id, new CreateQualityFlagDto(LongReason, null));

        await Service.RequestReworkAsync(flag.Id, qa.Id, new RequestReworkDto("Làm lại báo cáo và đính kèm chứng từ."));

        Assert.Equal(WorkStatus.InProgress, (await _db.Works.AsNoTracking().SingleAsync(w => w.Id == work.Id)).Status);
    }

    [Fact]
    public async Task RequestRework_WhenTheWorkCannotBeReopened_IsRefusedAndChangesNothing()
    {
        var (_, qa, work, submission, result) = await SeedApprovedAsync(workStatus: WorkStatus.Cancelled);
        var flag = await Service.CreateFlagAsync(submission.Id, qa.Id, new CreateQualityFlagDto(LongReason, null));

        await Assert.ThrowsAsync<ConflictException>(
            () => Service.RequestReworkAsync(flag.Id, qa.Id, new RequestReworkDto("Làm lại báo cáo và đính kèm chứng từ.")));

        Assert.Equal(VerifiedResultStatus.Valid, (await _db.VerifiedWorkResults.AsNoTracking().SingleAsync(r => r.Id == result.Id)).Status);
        Assert.Equal(WorkStatus.Cancelled, (await _db.Works.AsNoTracking().SingleAsync(w => w.Id == work.Id)).Status);
        Assert.Equal(QualityFlagStatus.Open, (await _db.QualityFlags.AsNoTracking().SingleAsync(f => f.Id == flag.Id)).Status);
    }

    [Fact]
    public async Task Dismiss_ClosesAnOpenFlagWithoutTouchingTheResult()
    {
        var (_, qa, work, submission, result) = await SeedApprovedAsync();
        var flag = await Service.CreateFlagAsync(submission.Id, qa.Id, new CreateQualityFlagDto(LongReason, null));

        var dismissed = await Service.DismissFlagAsync(flag.Id, qa.Id);

        Assert.Equal("DISMISSED", dismissed.Status);
        Assert.Equal(VerifiedResultStatus.Valid, (await _db.VerifiedWorkResults.AsNoTracking().SingleAsync(r => r.Id == result.Id)).Status);
        Assert.Equal(WorkStatus.Verified, (await _db.Works.AsNoTracking().SingleAsync(w => w.Id == work.Id)).Status);
        await Assert.ThrowsAsync<ConflictException>(() => Service.DismissFlagAsync(flag.Id, qa.Id));
    }

    [Fact]
    public async Task GetFlags_FiltersByStatusAndEmployee()
    {
        var (maker, qa, _, first, _) = await SeedApprovedAsync();
        var (_, _, _, second, _) = await SeedApprovedAsync();
        var firstFlag = await Service.CreateFlagAsync(first.Id, qa.Id, new CreateQualityFlagDto(LongReason, null));
        await Service.CreateFlagAsync(second.Id, qa.Id, new CreateQualityFlagDto(LongReason, null));
        await Service.DismissFlagAsync(firstFlag.Id, qa.Id);

        var open = await Service.GetFlagsAsync(new QualityFlagFilterDto { Status = "OPEN" });
        var ofMaker = await Service.GetFlagsAsync(new QualityFlagFilterDto { EmployeeId = maker.Id });

        Assert.Equal(1, open.TotalCount);
        Assert.Equal(1, ofMaker.TotalCount);
        Assert.Equal(firstFlag.Id, ofMaker.Items[0].Id);
        await Assert.ThrowsAsync<ValidationException>(() => Service.GetFlagsAsync(new QualityFlagFilterDto { Status = "NOPE" }));
    }

    private async Task<QaSample> AddSampleAsync(ResultSubmission submission)
    {
        var sample = new QaSample
        {
            Id = Guid.NewGuid(),
            ResultSubmissionId = submission.Id,
            SubmitterEmployeeId = submission.SubmitterEmployeeId,
            Status = QaSampleStatus.Pending,
            SampledAt = Now,
        };
        _db.QaSamples.Add(sample);
        await _db.SaveChangesAsync();
        return sample;
    }

    [Fact]
    public async Task DecideSample_Passed_ClosesTheSampleWithoutAFlag()
    {
        var (_, qa, _, submission, _) = await SeedApprovedAsync();
        var sample = await AddSampleAsync(submission);

        var decision = await Service.DecideSampleAsync(sample.Id, qa.Id, new DecideQaSampleDto("PASSED", null, null, false));

        Assert.Equal("PASSED", decision.Sample.Status);
        Assert.Equal(qa.Id, decision.Sample.ReviewedByEmployeeId);
        Assert.Null(decision.Flag);
        Assert.Empty(_db.QualityFlags);
        await Assert.ThrowsAsync<ConflictException>(
            () => Service.DecideSampleAsync(sample.Id, qa.Id, new DecideQaSampleDto("PASSED", null, null, false)));
    }

    [Fact]
    public async Task DecideSample_Failed_RaisesAFlagLinkedToTheSample()
    {
        var (maker, qa, _, submission, _) = await SeedApprovedAsync();
        var sample = await AddSampleAsync(submission);

        var decision = await Service.DecideSampleAsync(sample.Id, qa.Id, new DecideQaSampleDto("FAILED", LongReason, "LOW", false));

        Assert.Equal("FAILED", decision.Sample.Status);
        Assert.NotNull(decision.Flag);
        Assert.Equal("OPEN", decision.Flag.Status);
        Assert.Equal("LOW", decision.Flag.Severity);
        Assert.Equal(maker.Id, decision.Flag.EmployeeId);
        Assert.Equal(sample.Id, decision.Flag.QaSampleId);
        Assert.Equal(decision.Flag.Id, decision.Sample.QualityFlagId);
        Assert.Empty(_notifier.Drafts);
    }

    [Fact]
    public async Task DecideSample_FailedWithRework_SendsTheWorkBackInTheSameCall()
    {
        var (_, qa, work, submission, result) = await SeedApprovedAsync();
        var sample = await AddSampleAsync(submission);

        var decision = await Service.DecideSampleAsync(sample.Id, qa.Id, new DecideQaSampleDto("FAILED", LongReason, null, true));

        Assert.Equal("REWORK_REQUESTED", decision.Flag!.Status);
        Assert.Equal(VerifiedResultStatus.Invalid, (await _db.VerifiedWorkResults.AsNoTracking().SingleAsync(r => r.Id == result.Id)).Status);
        Assert.Equal(WorkStatus.InProgress, (await _db.Works.AsNoTracking().SingleAsync(w => w.Id == work.Id)).Status);
        Assert.Single(_notifier.Drafts);
    }

    [Fact]
    public async Task DecideSample_Failed_NeedsACommentAndReworkOnlyAppliesToFailed()
    {
        var (_, qa, _, submission, _) = await SeedApprovedAsync();
        var sample = await AddSampleAsync(submission);

        await Assert.ThrowsAsync<ValidationException>(
            () => Service.DecideSampleAsync(sample.Id, qa.Id, new DecideQaSampleDto("FAILED", null, null, false)));
        await Assert.ThrowsAsync<ValidationException>(
            () => Service.DecideSampleAsync(sample.Id, qa.Id, new DecideQaSampleDto("PASSED", null, null, true)));
        await Assert.ThrowsAsync<ValidationException>(
            () => Service.DecideSampleAsync(sample.Id, qa.Id, new DecideQaSampleDto("PENDING", null, null, false)));
        Assert.Equal(QaSampleStatus.Pending, (await _db.QaSamples.AsNoTracking().SingleAsync(x => x.Id == sample.Id)).Status);
    }

    [Fact]
    public async Task DecideSample_BySubmitter_IsForbidden()
    {
        var (maker, _, _, submission, _) = await SeedApprovedAsync();
        var sample = await AddSampleAsync(submission);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => Service.DecideSampleAsync(sample.Id, maker.Id, new DecideQaSampleDto("PASSED", null, null, false)));
    }

    [Fact]
    public async Task GetSamples_ListsPendingFirstAndFiltersByStatus()
    {
        var (_, qa, _, first, _) = await SeedApprovedAsync();
        var (_, _, _, second, _) = await SeedApprovedAsync();
        var passed = await AddSampleAsync(first);
        await AddSampleAsync(second);
        await Service.DecideSampleAsync(passed.Id, qa.Id, new DecideQaSampleDto("PASSED", null, null, false));

        var all = await Service.GetSamplesAsync(new QaSampleFilterDto());
        var pending = await Service.GetSamplesAsync(new QaSampleFilterDto { Status = "pending" });

        Assert.Equal(2, all.TotalCount);
        Assert.Equal("PENDING", all.Items[0].Status);
        Assert.Equal(1, pending.TotalCount);
    }

    // BR-32: the sample is drawn when the verifier approves.
    private sealed class FixedSamplingPolicy(bool sample) : IQaSamplingPolicy
    {
        public bool ShouldSample(Work work) => sample;
    }

    private async Task<(Guid VerifierAccountId, Guid SubmissionId)> SeedSubmittedAsync(bool withWorkType)
    {
        var maker = await AddEmployeeAsync($"MK{Guid.NewGuid():N}"[..10]);
        var verifier = await AddEmployeeAsync($"VF{Guid.NewGuid():N}"[..10]);
        Guid? workTypeId = null;
        if (withWorkType)
        {
            var workType = new WorkType { Id = Guid.NewGuid(), Code = $"WT-{Guid.NewGuid():N}"[..10], Name = "Có danh mục", Status = RecordStatus.Active };
            _db.WorkTypes.Add(workType);
            workTypeId = workType.Id;
        }

        var work = new Work
        {
            Id = Guid.NewGuid(),
            Code = $"WRK-{Guid.NewGuid():N}"[..12],
            Title = "Công việc",
            WorkTypeId = workTypeId,
            AssigneeEmployeeId = maker.Id,
            CreatedBy = maker.Id,
            Status = WorkStatus.Submitted,
            Priority = WorkPriority.Medium,
            UpdatedAt = Now,
        };
        var submission = new ResultSubmission
        {
            Id = Guid.NewGuid(),
            WorkId = work.Id,
            SubmitterEmployeeId = maker.Id,
            SubmissionNo = 1,
            Status = SubmissionStatus.Submitted,
            SubmittedAt = Now,
            UpdatedAt = Now,
        };
        _db.Works.Add(work);
        _db.ResultSubmissions.Add(submission);
        await _db.SaveChangesAsync();
        var accountId = (await _db.Accounts.AsNoTracking().SingleAsync(a => a.EmployeeId == verifier.Id)).Id;
        return (accountId, submission.Id);
    }

    private VerificationService VerificationWith(IQaSamplingPolicy? policy) => new(
        _db,
        NullLogger<VerificationService>.Instance,
        new FixedTime(Now),
        new FakePermissionService(null, new UserPermissionDto("verification", "decide", "All", "VERIFICATION_DECIDE")),
        policy);

    private static SubmitVerificationDecisionDto Approve => new() { Decision = "APPROVED", Feedback = "Hồ sơ đầy đủ, chứng từ hợp lệ." };

    [Fact]
    public async Task Approval_WhenThePolicyPicksTheWork_QueuesAPendingQaSample()
    {
        var (verifierAccountId, submissionId) = await SeedSubmittedAsync(withWorkType: false);

        await VerificationWith(new FixedSamplingPolicy(true)).SubmitDecisionAsync(submissionId, verifierAccountId, Approve);

        var sample = await _db.QaSamples.AsNoTracking().SingleAsync();
        Assert.Equal(submissionId, sample.ResultSubmissionId);
        Assert.Equal(QaSampleStatus.Pending, sample.Status);
        Assert.Equal(Now, sample.SampledAt);
    }

    [Fact]
    public async Task Approval_WhenThePolicyDoesNotPickTheWork_QueuesNothing()
    {
        var (verifierAccountId, submissionId) = await SeedSubmittedAsync(withWorkType: false);

        await VerificationWith(new FixedSamplingPolicy(false)).SubmitDecisionAsync(submissionId, verifierAccountId, Approve);
        await Assert.ThrowsAsync<BusinessRuleException>(
            () => VerificationWith(null).SubmitDecisionAsync(submissionId, verifierAccountId, Approve));

        Assert.Empty(_db.QaSamples);
    }

    [Fact]
    public async Task Approval_WithoutAPolicy_NeverSamples()
    {
        var (verifierAccountId, submissionId) = await SeedSubmittedAsync(withWorkType: false);

        await VerificationWith(null).SubmitDecisionAsync(submissionId, verifierAccountId, Approve);

        Assert.Empty(_db.QaSamples);
    }

    [Fact]
    public async Task Rejection_NeverSamples()
    {
        var (verifierAccountId, submissionId) = await SeedSubmittedAsync(withWorkType: false);

        await VerificationWith(new FixedSamplingPolicy(true)).SubmitDecisionAsync(
            submissionId,
            verifierAccountId,
            new SubmitVerificationDecisionDto { Decision = "REJECTED", Feedback = "Hồ sơ không hợp lệ, thiếu chứng từ." });

        Assert.Empty(_db.QaSamples);
    }

    [Fact]
    public void TheRandomPolicy_SamplesAboutFivePercentOfWork()
    {
        var policy = new RandomQaSamplingPolicy();
        var work = new Work();

        var sampled = Enumerable.Range(0, 20_000).Count(_ => policy.ShouldSample(work));

        Assert.InRange(sampled, 600, 1400);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
