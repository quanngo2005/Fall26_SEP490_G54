using G54.BLL.Common;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Appeal;
using G54.BLL.Dtos.Permission;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace G54.UnitTests;

public sealed class AppealServiceTests : IDisposable
{
    private static readonly DateTimeOffset RejectedAt = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);
    private static readonly byte[] PdfBytes = "%PDF-1.7 appeal"u8.ToArray();

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "G54_Appeal_" + Guid.NewGuid().ToString("N"));
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _salesId = Guid.NewGuid();
    private readonly Guid _tellerId = Guid.NewGuid();
    private readonly Guid _salesTeamId = Guid.NewGuid();

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private StorageService Storage => new(
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:EvidencePath"] = _folder })
            .Build(),
        NullLogger<StorageService>.Instance);

    private int _appealRejectedHours = 48;

    private AppealService ServiceAt(DateTimeOffset now, params (string Resource, string Action, string Scope)[] grants)
    {
        return new(_db, new GrantingPermissions(grants), Storage, NullLogger<AppealService>.Instance, new FixedTime(now), Options.Create(new SystemParametersOptions { AppealRejectedHours = _appealRejectedHours }));
    }

    private async Task SeedDepartmentsAsync()
    {
        var now = DateTimeOffset.UtcNow;
        _db.Departments.AddRange(
            new Department { Id = _branchId, Code = "BR", Name = "Branch", LevelType = DepartmentLevelType.Branch, Status = RecordStatus.Active, UpdatedAt = now },
            new Department { Id = _salesId, Code = "SA", Name = "Sales", ParentDepartmentId = _branchId, LevelType = DepartmentLevelType.Department, Status = RecordStatus.Active, UpdatedAt = now },
            new Department { Id = _tellerId, Code = "TE", Name = "Teller", ParentDepartmentId = _branchId, LevelType = DepartmentLevelType.Department, Status = RecordStatus.Active, UpdatedAt = now },
            new Department { Id = _salesTeamId, Code = "ST", Name = "Sales team", ParentDepartmentId = _salesId, LevelType = DepartmentLevelType.Team, Status = RecordStatus.Active, UpdatedAt = now });
        await _db.SaveChangesAsync();
    }


    private async Task<Employee> AddEmployeeAsync(string code, Guid departmentId, EmployeeStatus status = EmployeeStatus.Active)
    {
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = code,
            FullName = $"Name {code}",
            Email = $"{code}@bank.test",
            DepartmentId = departmentId,
            JobPositionId = Guid.NewGuid(),
            Status = status,
            LastSyncAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Employees.Add(employee);
        _db.Accounts.Add(new Account
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            Username = code,
            PasswordHash = "hash",
            LastSyncAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();
        return employee;
    }

    /// <summary>A submission by <paramref name="submitter"/> that a verifier rejected at <paramref name="rejectedAt"/>.</summary>
    private async Task<ResultSubmission> AddSubmissionAsync(
        Employee submitter,
        SubmissionStatus status = SubmissionStatus.Rejected,
        DateTimeOffset? rejectedAt = null,
        bool withVerification = true)
    {
        var at = rejectedAt ?? RejectedAt;
        var work = new Work
        {
            Id = Guid.NewGuid(),
            Code = $"W-{Guid.NewGuid():N}"[..10],
            Title = "Work",
            AssigneeEmployeeId = submitter.Id,
            Status = WorkStatus.Rejected,
            Priority = WorkPriority.Medium,
            UpdatedAt = at,
        };
        var submission = new ResultSubmission
        {
            Id = Guid.NewGuid(),
            WorkId = work.Id,
            SubmitterEmployeeId = submitter.Id,
            SubmissionNo = 1,
            Status = status,
            SubmittedAt = at.AddHours(-2),
            UpdatedAt = at,
        };
        _db.Works.Add(work);
        _db.ResultSubmissions.Add(submission);
        if (withVerification)
        {
            _db.Verifications.Add(new Verification
            {
                Id = Guid.NewGuid(),
                ResultSubmissionId = submission.Id,
                VerifierEmployeeId = Guid.NewGuid(),
                RoundNo = 1,
                Decision = VerificationDecision.Rejected,
                VerifiedAt = at,
                UpdatedAt = at,
            });
        }

        await _db.SaveChangesAsync();
        return submission;
    }

    private async Task<Appeal> AddAppealAsync(
        Employee employee,
        ResultSubmission? submission,
        AppealStatus status = AppealStatus.Open,
        DateTimeOffset? filedAt = null)
    {
        var at = filedAt ?? RejectedAt.AddHours(1);
        var appeal = new Appeal
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            ResultSubmissionId = submission?.Id,
            PerformanceEvaluationId = submission is null ? Guid.NewGuid() : null,
            Reason = "Reason",
            Status = status,
            FiledAt = at,
            UpdatedAt = at,
        };
        _db.Appeals.Add(appeal);
        await _db.SaveChangesAsync();
        return appeal;
    }

    private static CreateAppealRequestDto Request(string? reason = "Kết quả đã đúng yêu cầu", params IFormFile[] files) =>
        new() { Reason = reason, Files = files.ToList() };

    private static FormFile Pdf(string name = "bang chung.pdf") =>
        new(new MemoryStream(PdfBytes), 0, PdfBytes.Length, "Files", name);

    private async Task<Employee> SubmitterWithRejectedSubmissionAsync()
    {
        await SeedDepartmentsAsync();
        return await AddEmployeeAsync("SUBMITTER", _salesTeamId);
    }

    [Theory]
    [InlineData(" accept ", SubmissionStatus.Approved, WorkStatus.Verified, VerificationDecision.Approved)]
    [InlineData("adjust", SubmissionStatus.UnderReview, WorkStatus.InProgress, VerificationDecision.NeedMoreInfo)]
    public async Task Decide_AcceptOrAdjust_UpdatesSubmissionAndRecordsANewVerification(
        string decision, SubmissionStatus status, WorkStatus workStatus, VerificationDecision verificationDecision)
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var submission = await AddSubmissionAsync(submitter);
        var appeal = await AddAppealAsync(submitter, submission, AppealStatus.UnderReview);
        var resolver = await AddEmployeeAsync("MANAGER", _salesId);
        var now = RejectedAt.AddDays(4); // Resolution is not limited by the filing window.
        var result = await ServiceAt(now, ("verification", "decide", "Dept")).DecideAppealAsync(
            appeal.Id, resolver.Account!.Id,
            new DecideAppealRequestDto { Decision = decision, Note = "  Bổ sung chứng từ đối soát  " });

        Assert.Equal("ACCEPTED", result.Appeal.Status);
        Assert.Equal(resolver.Id, result.Appeal.ResolvedByEmployeeId);
        Assert.Equal(resolver.FullName, result.Appeal.ResolvedByEmployeeName);
        Assert.Equal(now, result.Appeal.DecidedAt);
        Assert.Equal("Bổ sung chứng từ đối soát", result.Appeal.Note);
        Assert.Equal(status, submission.Status);
        Assert.Equal(workStatus, submission.Work.Status);
        var verification = await _db.Verifications.SingleAsync(v => v.Id == result.VerificationId);
        Assert.Equal(2, verification.RoundNo);
        Assert.Equal(verificationDecision, verification.Decision);
        Assert.Equal(resolver.Id, verification.VerifierEmployeeId);
        Assert.Equal(result.Appeal.Note, verification.Comment);
        Assert.Equal(decision.Trim().ToUpperInvariant(), result.Decision);
        Assert.Equal(decision.Trim().Equals("accept", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
            await _db.VerifiedWorkResults.CountAsync());
        Assert.Empty(await _db.WorkResultItems.ToListAsync());
    }

    [Fact]
    public async Task Decide_Reject_KeepsTheOriginalRejectionAndDoesNotResetTheAppealWindow()
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var submission = await AddSubmissionAsync(submitter);
        var appeal = await AddAppealAsync(submitter, submission);
        var resolver = await AddEmployeeAsync("MANAGER", _salesId);
        var result = await ServiceAt(RejectedAt.AddHours(5), ("*", "*", "All")).DecideAppealAsync(
            appeal.Id, resolver.Id, new DecideAppealRequestDto { Decision = "REJECT", Note = "Hồ sơ chưa đáp ứng yêu cầu" });

        Assert.Equal("REJECTED", result.Appeal.Status);
        Assert.Equal("REJECTED", result.SubmissionStatus);
        Assert.Equal("REJECTED", result.WorkStatus);
        Assert.Null(result.VerificationId);
        Assert.Equal(RejectedAt, submission.UpdatedAt);
        Assert.Single(await _db.Verifications.ToListAsync());
        Assert.Empty(await _db.VerifiedWorkResults.ToListAsync());
    }

    [Fact]
    public async Task Decide_AcceptWithVerifiedValue_ContributesKpiOnce()
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var submission = await AddSubmissionAsync(submitter);
        var appeal = await AddAppealAsync(submitter, submission);
        var resolver = await AddEmployeeAsync("MANAGER", _salesId);
        var workType = new WorkType { Id = Guid.NewGuid(), Code = "WT", Name = "Work type", Status = RecordStatus.Active };
        var kpi = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            Code = "KPI",
            Name = "KPI",
            Unit = "Điểm",
            KpiGroupId = Guid.NewGuid(),
            AggregationType = KpiAggregationType.Sum,
            Direction = KpiDirection.HigherBetter,
            Status = KpiStatus.Active,
        };
        var period = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = "P",
            Name = "Period",
            PeriodType = PeriodType.Month,
            StartDate = new DateOnly(2026, 10, 1),
            EndDate = new DateOnly(2026, 10, 31),
            Status = PeriodStatus.Open,
        };
        _db.WorkTypes.Add(workType);
        _db.KpiDefinitions.Add(kpi);
        _db.EvaluationPeriods.Add(period);
        _db.WorkTypeKpis.Add(new WorkTypeKpi
        {
            Id = Guid.NewGuid(),
            WorkTypeId = workType.Id,
            KpiDefinitionId = kpi.Id,
            EffectiveFrom = period.StartDate,
        });
        _db.TargetAssignments.Add(new TargetAssignment
        {
            Id = Guid.NewGuid(),
            KpiDefinitionId = kpi.Id,
            EmployeeId = submitter.Id,
            EvaluationPeriodId = period.Id,
            Version = 1,
            TargetValue = 100,
            Status = TargetAssignmentStatus.Active,
        });
        submission.Work.WorkTypeId = workType.Id;
        await _db.SaveChangesAsync();
        var service = ServiceAt(RejectedAt.AddHours(5), ("verification", "decide", "Branch"));
        var request = new DecideAppealRequestDto { Decision = "ACCEPT", VerifiedValue = 12.5m };

        var result = await service.DecideAppealAsync(appeal.Id, resolver.Id, request);

        var item = await _db.WorkResultItems.SingleAsync();
        Assert.Equal(submitter.Id, item.EmployeeId);
        Assert.Equal(12.5m, item.Value);
        Assert.Equal(12.5m, (await _db.ActualProgresses.SingleAsync()).Value);
        Assert.Equal(result.VerificationId, (await _db.VerifiedWorkResults.SingleAsync()).VerificationId);
        await Assert.ThrowsAsync<ConflictException>(() => service.DecideAppealAsync(appeal.Id, resolver.Id, request));
        Assert.Single(await _db.WorkResultItems.ToListAsync());
        Assert.Equal(12.5m, (await _db.ActualProgresses.SingleAsync()).Value);
    }

    [Fact]
    public async Task Decide_OverlongNote_IsInvalid()
    {
        await Assert.ThrowsAsync<ValidationException>(() => ServiceAt(RejectedAt).DecideAppealAsync(
            Guid.NewGuid(), Guid.NewGuid(), new DecideAppealRequestDto { Decision = "ACCEPT", Note = new string('a', 2001) }));
    }

    [Fact]
    public async Task Decide_InactiveCaller_IsUnauthorized()
    {
        await SeedDepartmentsAsync();
        var resolver = await AddEmployeeAsync("MANAGER", _salesId, EmployeeStatus.Inactive);
        await Assert.ThrowsAsync<UnauthorizedException>(() => ServiceAt(RejectedAt, ("*", "*", "All")).DecideAppealAsync(
            Guid.NewGuid(), resolver.Id, new DecideAppealRequestDto { Decision = "ACCEPT" }));
    }

    [Fact]
    public async Task Decide_LockedAccount_IsUnauthorizedEvenWithCachedPermissions()
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var appeal = await AddAppealAsync(submitter, await AddSubmissionAsync(submitter));
        var resolver = await AddEmployeeAsync("MANAGER", _salesId);
        resolver.Account!.IsLocked = true;
        await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedException>(() => ServiceAt(RejectedAt, ("*", "*", "All")).DecideAppealAsync(
            appeal.Id, resolver.Account.Id, new DecideAppealRequestDto { Decision = "ACCEPT" }));
        Assert.Equal(AppealStatus.Open, appeal.Status);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("UNKNOWN", null, null)]
    [InlineData("ADJUST", null, null)]
    [InlineData("REJECT", "short", null)]
    [InlineData("ACCEPT", null, -1)]
    [InlineData("ADJUST", "Bổ sung chứng từ đối soát", 1)]
    [InlineData("REJECT", "Hồ sơ không đạt yêu cầu", 0)]
    public async Task Decide_InvalidPayload_StoresNothing(string? decision, string? note, int? value)
    {
        await Assert.ThrowsAsync<ValidationException>(() => ServiceAt(RejectedAt).DecideAppealAsync(
            Guid.NewGuid(), Guid.NewGuid(), new DecideAppealRequestDto { Decision = decision, Note = note, VerifiedValue = value }));
        Assert.Empty(await _db.Appeals.ToListAsync());
    }

    [Theory]
    [InlineData("Own", "MANAGER")]
    [InlineData("Dept", "OUTSIDE")]
    [InlineData("", "MANAGER")]
    public async Task Decide_WithoutPermissionOrOutsideScope_IsForbidden(string scope, string code)
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var submission = await AddSubmissionAsync(submitter);
        var appeal = await AddAppealAsync(submitter, submission);
        var resolver = await AddEmployeeAsync(code, code == "OUTSIDE" ? _tellerId : _salesId);
        await Assert.ThrowsAsync<ForbiddenException>(() => ServiceAt(RejectedAt, ("verification", "decide", scope)).DecideAppealAsync(
            appeal.Id, resolver.Id, new DecideAppealRequestDto { Decision = "ACCEPT" }));
        Assert.Equal(AppealStatus.Open, appeal.Status);
        Assert.Equal(SubmissionStatus.Rejected, submission.Status);
    }

    [Fact]
    public async Task Decide_OwnAppeal_IsBlockedEvenForAnAdmin()
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var appeal = await AddAppealAsync(submitter, await AddSubmissionAsync(submitter));
        var exception = await Assert.ThrowsAsync<BusinessRuleException>(() => ServiceAt(RejectedAt, ("*", "*", "All")).DecideAppealAsync(
            appeal.Id, submitter.Id, new DecideAppealRequestDto { Decision = "ACCEPT" }));
        Assert.Equal(ErrorCodes.MakerCannotBeChecker, exception.ErrorCode);
    }

    [Theory]
    [InlineData(AppealStatus.Accepted)]
    [InlineData(AppealStatus.Rejected)]
    [InlineData(AppealStatus.Withdrawn)]
    public async Task Decide_AlreadyClosed_IsAConflict(AppealStatus status)
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var appeal = await AddAppealAsync(submitter, await AddSubmissionAsync(submitter), status);
        var resolver = await AddEmployeeAsync("MANAGER", _salesId);
        var exception = await Assert.ThrowsAsync<ConflictException>(() => ServiceAt(RejectedAt, ("*", "*", "All")).DecideAppealAsync(
            appeal.Id, resolver.Id, new DecideAppealRequestDto { Decision = "ACCEPT" }));
        Assert.Equal(ErrorCodes.AppealInvalidState, exception.ErrorCode);
        Assert.Single(await _db.Verifications.ToListAsync());
    }

    [Theory]
    [InlineData("submission")]
    [InlineData("work")]
    [InlineData("newer")]
    public async Task Decide_StaleSubmissionOrWork_IsAConflict(string changed)
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var submission = await AddSubmissionAsync(submitter);
        var appeal = await AddAppealAsync(submitter, submission);
        var resolver = await AddEmployeeAsync("MANAGER", _salesId);
        if (changed == "submission")
        {
            submission.Status = SubmissionStatus.Approved;
        }
        else if (changed == "work")
        {
            submission.Work.Status = WorkStatus.Cancelled;
        }
        else
        {
            _db.ResultSubmissions.Add(new ResultSubmission
            {
                Id = Guid.NewGuid(),
                WorkId = submission.WorkId,
                SubmitterEmployeeId = submitter.Id,
                SubmissionNo = 2,
                Status = SubmissionStatus.Submitted,
                SubmittedAt = RejectedAt,
                UpdatedAt = RejectedAt,
            });
        }

        await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => ServiceAt(RejectedAt, ("*", "*", "All")).DecideAppealAsync(
            appeal.Id, resolver.Id, new DecideAppealRequestDto { Decision = "ACCEPT" }));
        Assert.Equal(AppealStatus.Open, appeal.Status);
    }

    [Fact]
    public async Task Decide_MissingOrEvaluationAppeal_IsNotFound()
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var resolver = await AddEmployeeAsync("MANAGER", _salesId);
        var evaluationAppeal = await AddAppealAsync(submitter, null);
        var service = ServiceAt(RejectedAt, ("*", "*", "All"));
        await Assert.ThrowsAsync<NotFoundException>(() => service.DecideAppealAsync(
            Guid.NewGuid(), resolver.Id, new DecideAppealRequestDto { Decision = "ACCEPT" }));
        await Assert.ThrowsAsync<NotFoundException>(() => service.DecideAppealAsync(
            evaluationAppeal.Id, resolver.Id, new DecideAppealRequestDto { Decision = "ACCEPT" }));
    }

    // ----- Create ----------------------------------------------------------------------------

    [Fact]
    public async Task Create_WithinWindow_StoresAnOpenAppealWithItsEvidence()
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var submission = await AddSubmissionAsync(submitter);
        var now = RejectedAt.AddHours(5);

        var result = await ServiceAt(now).CreateAppealAsync(submission.Id, submitter.Id, Request(files: Pdf()));

        Assert.Equal("OPEN", result.Status);
        Assert.Equal(submission.Id, result.ResultSubmissionId);
        Assert.Equal(submitter.Id, result.EmployeeId);
        Assert.Equal("Kết quả đã đúng yêu cầu", result.Reason);
        Assert.Equal(now, result.FiledAt);
        Assert.Null(result.DecidedAt);
        var evidence = Assert.Single(result.Evidences);
        Assert.Equal("bang chung.pdf", evidence.FileName);
        Assert.Equal($"/evidence/{evidence.Id}/download", evidence.DownloadUrl);

        var stored = await _db.Appeals.Include(a => a.Evidences).SingleAsync();
        Assert.Equal(AppealStatus.Open, stored.Status);
        var storedEvidence = Assert.Single(stored.Evidences);
        Assert.Equal(stored.Id, storedEvidence.AppealId);
        Assert.Null(storedEvidence.ResultSubmissionId);
        Assert.True(File.Exists(Path.Combine(_folder, storedEvidence.FilePath)));
    }

    [Fact]
    public async Task Create_ByAccountId_ResolvesTheEmployee()
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var submission = await AddSubmissionAsync(submitter);
        var accountId = (await _db.Accounts.SingleAsync(a => a.EmployeeId == submitter.Id)).Id;

        var result = await ServiceAt(RejectedAt.AddHours(1)).CreateAppealAsync(submission.Id, accountId, Request());

        Assert.Equal(submitter.Id, result.EmployeeId);
        Assert.Empty(result.Evidences);
    }

    [Fact]
    public async Task Create_TrimsTheReason()
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var submission = await AddSubmissionAsync(submitter);

        var result = await ServiceAt(RejectedAt.AddHours(1))
            .CreateAppealAsync(submission.Id, submitter.Id, Request("   lý do   "));

        Assert.Equal("lý do", result.Reason);
    }

    [Fact]
    public async Task Create_ExactlyAtTheEndOfTheWindow_IsStillAllowed()
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var submission = await AddSubmissionAsync(submitter);

        var result = await ServiceAt(RejectedAt.AddHours(48)).CreateAppealAsync(submission.Id, submitter.Id, Request());

        Assert.Equal("OPEN", result.Status);
    }

    [Fact]
    public async Task Create_OneSecondAfterTheWindow_IsRejectedAndStoresNothing()
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var submission = await AddSubmissionAsync(submitter);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            ServiceAt(RejectedAt.AddHours(48).AddSeconds(1))
                .CreateAppealAsync(submission.Id, submitter.Id, Request(files: Pdf())));

        Assert.Equal(ErrorCodes.AppealWindowExpired, ex.ErrorCode);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, ex.StatusCode);
        Assert.Empty(await _db.Appeals.ToListAsync());
        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public async Task Create_MeasuresTheWindowFromTheLatestRejection()
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var submission = await AddSubmissionAsync(submitter);
        var later = RejectedAt.AddHours(40);
        _db.Verifications.Add(new Verification
        {
            Id = Guid.NewGuid(),
            ResultSubmissionId = submission.Id,
            VerifierEmployeeId = Guid.NewGuid(),
            RoundNo = 2,
            Decision = VerificationDecision.Rejected,
            VerifiedAt = later,
            UpdatedAt = later,
        });
        await _db.SaveChangesAsync();

        // 60 hours after the first rejection but only 20 after the latest one.
        var result = await ServiceAt(RejectedAt.AddHours(60)).CreateAppealAsync(submission.Id, submitter.Id, Request());

        Assert.Equal("OPEN", result.Status);
    }

    [Fact]
    public async Task Create_UsesTheConfiguredAppealWindow()
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var submission = await AddSubmissionAsync(submitter);
        _appealRejectedHours = 24;
        var service = ServiceAt(RejectedAt.AddHours(30));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.CreateAppealAsync(submission.Id, submitter.Id, Request()));

        Assert.Equal(ErrorCodes.AppealWindowExpired, ex.ErrorCode);
    }

    [Fact]
    public async Task Create_IgnoresVerificationsThatAreNotRejections()
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var submission = await AddSubmissionAsync(submitter);
        _db.Verifications.Add(new Verification
        {
            Id = Guid.NewGuid(),
            ResultSubmissionId = submission.Id,
            VerifierEmployeeId = Guid.NewGuid(),
            RoundNo = 2,
            Decision = VerificationDecision.NeedMoreInfo,
            VerifiedAt = RejectedAt.AddHours(100),
            UpdatedAt = RejectedAt.AddHours(100),
        });
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            ServiceAt(RejectedAt.AddHours(60)).CreateAppealAsync(submission.Id, submitter.Id, Request()));

        Assert.Equal(ErrorCodes.AppealWindowExpired, ex.ErrorCode);
    }

    [Fact]
    public async Task Create_WithoutAVerificationTimestamp_FallsBackToTheSubmissionUpdateTime()
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var submission = await AddSubmissionAsync(submitter, withVerification: false);

        var ok = await ServiceAt(RejectedAt.AddHours(48)).CreateAppealAsync(submission.Id, submitter.Id, Request());
        Assert.Equal("OPEN", ok.Status);

        var other = await AddSubmissionAsync(submitter, withVerification: false);
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            ServiceAt(RejectedAt.AddHours(49)).CreateAppealAsync(other.Id, submitter.Id, Request()));
        Assert.Equal(ErrorCodes.AppealWindowExpired, ex.ErrorCode);
    }

    [Theory]
    [InlineData(SubmissionStatus.Draft)]
    [InlineData(SubmissionStatus.Submitted)]
    [InlineData(SubmissionStatus.UnderReview)]
    [InlineData(SubmissionStatus.Approved)]
    [InlineData(SubmissionStatus.Withdrawn)]
    public async Task Create_WhenTheSubmissionIsNotRejected_IsAConflict(SubmissionStatus status)
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var submission = await AddSubmissionAsync(submitter, status);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            ServiceAt(RejectedAt.AddHours(1)).CreateAppealAsync(submission.Id, submitter.Id, Request()));

        Assert.Equal(ErrorCodes.AppealInvalidState, ex.ErrorCode);
        Assert.Equal(StatusCodes.Status409Conflict, ex.StatusCode);
    }

    [Fact]
    public async Task Create_ByAnotherEmployee_IsForbidden()
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var submission = await AddSubmissionAsync(submitter);
        var colleague = await AddEmployeeAsync("COLLEAGUE", _salesTeamId);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            ServiceAt(RejectedAt.AddHours(1)).CreateAppealAsync(submission.Id, colleague.Id, Request()));
        Assert.Empty(await _db.Appeals.ToListAsync());
    }

    [Fact]
    public async Task Create_ForAManagerWithGlobalScope_IsStillForbiddenWhenTheyDidNotSubmit()
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var submission = await AddSubmissionAsync(submitter);
        var admin = await AddEmployeeAsync("ADMIN", _tellerId);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            ServiceAt(RejectedAt.AddHours(1), ("*", "*", "All")).CreateAppealAsync(submission.Id, admin.Id, Request()));
    }

    [Fact]
    public async Task Create_ForAnUnknownSubmission_IsNotFound()
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            ServiceAt(RejectedAt.AddHours(1)).CreateAppealAsync(Guid.NewGuid(), submitter.Id, Request()));
    }

    [Fact]
    public async Task Create_ByAnInactiveOrUnknownCaller_IsUnauthorized()
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var submission = await AddSubmissionAsync(submitter);
        var service = ServiceAt(RejectedAt.AddHours(1));

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            service.CreateAppealAsync(submission.Id, Guid.NewGuid(), Request()));

        var tracked = await _db.Employees.SingleAsync(e => e.Id == submitter.Id);
        tracked.Status = EmployeeStatus.Inactive;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            service.CreateAppealAsync(submission.Id, submitter.Id, Request()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public async Task Create_WithABlankReason_IsAValidationError(string? reason)
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var submission = await AddSubmissionAsync(submitter);

        await Assert.ThrowsAsync<ValidationException>(() =>
            ServiceAt(RejectedAt.AddHours(1)).CreateAppealAsync(submission.Id, submitter.Id, Request(reason)));
        Assert.Empty(await _db.Appeals.ToListAsync());
    }

    [Fact]
    public async Task Create_ReasonLength_IsLimitedTo2000Characters()
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var submission = await AddSubmissionAsync(submitter);
        var service = ServiceAt(RejectedAt.AddHours(1));

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateAppealAsync(submission.Id, submitter.Id, Request(new string('a', 2001))));

        var result = await service.CreateAppealAsync(submission.Id, submitter.Id, Request(new string('a', 2000)));
        Assert.Equal(2000, result.Reason.Length);
    }

    [Fact]
    public async Task Create_WithMoreThanTenFiles_IsAValidationError()
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var submission = await AddSubmissionAsync(submitter);
        var files = Enumerable.Range(0, 11).Select(i => (IFormFile)Pdf($"f{i}.pdf")).ToArray();

        await Assert.ThrowsAsync<ValidationException>(() =>
            ServiceAt(RejectedAt.AddHours(1)).CreateAppealAsync(submission.Id, submitter.Id, Request(files: files)));
        Assert.Empty(await _db.Appeals.ToListAsync());
    }

    [Fact]
    public async Task Create_WithOneInvalidFile_StoresNoAppealAndNoFile()
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var submission = await AddSubmissionAsync(submitter);
        var fake = new FormFile(new MemoryStream("not a pdf"u8.ToArray()), 0, 9, "Files", "fake.pdf");

        await Assert.ThrowsAsync<ValidationException>(() =>
            ServiceAt(RejectedAt.AddHours(1)).CreateAppealAsync(submission.Id, submitter.Id, Request(files: [Pdf(), fake])));

        Assert.Empty(await _db.Appeals.ToListAsync());
        Assert.Empty(await _db.Evidences.ToListAsync());
        Assert.True(!Directory.Exists(_folder) || Directory.GetFiles(_folder).Length == 0);
    }

    [Theory]
    [InlineData(AppealStatus.Open)]
    [InlineData(AppealStatus.UnderReview)]
    public async Task Create_WhileAnAppealIsInProgress_IsAConflict(AppealStatus existing)
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var submission = await AddSubmissionAsync(submitter);
        await AddAppealAsync(submitter, submission, existing);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            ServiceAt(RejectedAt.AddHours(2)).CreateAppealAsync(submission.Id, submitter.Id, Request(files: Pdf())));

        Assert.Equal(ErrorCodes.AppealAlreadyOpen, ex.ErrorCode);
        Assert.Single(await _db.Appeals.ToListAsync());
        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public async Task Create_AfterWithdrawingTheEarlierAppeal_IsAllowedWithinTheWindow()
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var submission = await AddSubmissionAsync(submitter);
        await AddAppealAsync(submitter, submission, AppealStatus.Withdrawn);

        var result = await ServiceAt(RejectedAt.AddHours(10)).CreateAppealAsync(submission.Id, submitter.Id, Request());

        Assert.Equal("OPEN", result.Status);
        Assert.Equal(2, await _db.Appeals.CountAsync());
    }

    [Fact]
    public async Task Create_AnAppealOfAnotherSubmission_DoesNotBlock()
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var first = await AddSubmissionAsync(submitter);
        var second = await AddSubmissionAsync(submitter);
        await AddAppealAsync(submitter, first);

        var result = await ServiceAt(RejectedAt.AddHours(2)).CreateAppealAsync(second.Id, submitter.Id, Request());

        Assert.Equal(second.Id, result.ResultSubmissionId);
    }

    // ----- Withdraw --------------------------------------------------------------------------

    [Fact]
    public async Task Withdraw_AnOpenAppeal_MarksItWithdrawnWithoutADecision()
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var submission = await AddSubmissionAsync(submitter);
        var appeal = await AddAppealAsync(submitter, submission);
        var now = RejectedAt.AddHours(3);

        var result = await ServiceAt(now).WithdrawAppealAsync(appeal.Id, submitter.Id);

        Assert.Equal("WITHDRAWN", result.Status);
        Assert.Null(result.DecidedAt);
        Assert.Equal(now, result.UpdatedAt);
        var stored = await _db.Appeals.SingleAsync();
        Assert.Equal(AppealStatus.Withdrawn, stored.Status);
        Assert.Null(stored.DecidedAt);
        Assert.Null(stored.ResolvedBy);
    }

    [Theory]
    [InlineData(AppealStatus.UnderReview)]
    [InlineData(AppealStatus.Accepted)]
    [InlineData(AppealStatus.Rejected)]
    [InlineData(AppealStatus.Withdrawn)]
    public async Task Withdraw_WhenNotOpen_IsAConflictAndChangesNothing(AppealStatus status)
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var submission = await AddSubmissionAsync(submitter);
        var appeal = await AddAppealAsync(submitter, submission, status);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            ServiceAt(RejectedAt.AddHours(3)).WithdrawAppealAsync(appeal.Id, submitter.Id));

        Assert.Equal(ErrorCodes.AppealInvalidState, ex.ErrorCode);
        Assert.Equal(status, (await _db.Appeals.SingleAsync()).Status);
    }

    [Fact]
    public async Task Withdraw_ByAnyoneButTheOwner_IsForbidden()
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var submission = await AddSubmissionAsync(submitter);
        var appeal = await AddAppealAsync(submitter, submission);
        var manager = await AddEmployeeAsync("MANAGER", _salesId);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            ServiceAt(RejectedAt.AddHours(3), ("verification", "decide", "All")).WithdrawAppealAsync(appeal.Id, manager.Id));

        Assert.Equal(AppealStatus.Open, (await _db.Appeals.SingleAsync()).Status);
    }

    [Fact]
    public async Task Withdraw_AnUnknownAppeal_IsNotFound()
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            ServiceAt(RejectedAt.AddHours(3)).WithdrawAppealAsync(Guid.NewGuid(), submitter.Id));
    }

    [Fact]
    public async Task Withdraw_AnAppealAboutAnEvaluation_IsNotFound()
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var appeal = await AddAppealAsync(submitter, null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            ServiceAt(RejectedAt.AddHours(3)).WithdrawAppealAsync(appeal.Id, submitter.Id));
        Assert.Equal(AppealStatus.Open, (await _db.Appeals.SingleAsync()).Status);
    }

    [Fact]
    public async Task Withdraw_ByAnInactiveCaller_IsUnauthorized()
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var submission = await AddSubmissionAsync(submitter);
        var appeal = await AddAppealAsync(submitter, submission);
        var tracked = await _db.Employees.SingleAsync(e => e.Id == submitter.Id);
        tracked.Status = EmployeeStatus.Inactive;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            ServiceAt(RejectedAt.AddHours(3)).WithdrawAppealAsync(appeal.Id, submitter.Id));
    }

    // ----- List / detail ---------------------------------------------------------------------

    /// <summary>Appeals by an employee in the sales team, in the teller department and at the branch root.</summary>
    private sealed record Org(Employee Seller, Employee Teller, Employee BranchStaff, Appeal SellerAppeal, Appeal TellerAppeal, Appeal BranchAppeal);

    private async Task<Org> SeedOrgAsync()
    {
        await SeedDepartmentsAsync();
        var seller = await AddEmployeeAsync("SELLER", _salesTeamId);
        var teller = await AddEmployeeAsync("TELLER", _tellerId);
        var branchStaff = await AddEmployeeAsync("BRANCH", _branchId);
        var sellerAppeal = await AddAppealAsync(seller, await AddSubmissionAsync(seller), filedAt: RejectedAt.AddHours(1));
        var tellerAppeal = await AddAppealAsync(teller, await AddSubmissionAsync(teller), filedAt: RejectedAt.AddHours(2));
        var branchAppeal = await AddAppealAsync(branchStaff, await AddSubmissionAsync(branchStaff), filedAt: RejectedAt.AddHours(3));
        return new Org(seller, teller, branchStaff, sellerAppeal, tellerAppeal, branchAppeal);
    }

    private static List<Guid> Ids(PagedResult<AppealListItemDto> result) => result.Items.Select(i => i.Id).ToList();

    [Fact]
    public async Task List_AnEmployeeWithoutAVerificationPermission_SeesOnlyTheirOwnAppeals()
    {
        var org = await SeedOrgAsync();

        var result = await ServiceAt(RejectedAt.AddHours(5)).GetAppealsAsync(org.Seller.Id, new AppealFilterDto());

        var item = Assert.Single(result.Items);
        Assert.Equal(org.SellerAppeal.Id, item.Id);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal("SELLER", item.EmployeeCode);
        Assert.Equal("OPEN", item.Status);
    }

    [Fact]
    public async Task List_WithOwnScopeOnly_SeesOnlyTheirOwnAppeals()
    {
        var org = await SeedOrgAsync();

        var result = await ServiceAt(RejectedAt.AddHours(5), ("verification", "decide", "Own"))
            .GetAppealsAsync(org.Seller.Id, new AppealFilterDto());

        Assert.Equal([org.SellerAppeal.Id], Ids(result));
    }

    [Fact]
    public async Task List_WithDepartmentScope_SeesTheirSubtreeButNotASiblingDepartment()
    {
        var org = await SeedOrgAsync();
        var salesManager = await AddEmployeeAsync("SALESMGR", _salesId);

        var result = await ServiceAt(RejectedAt.AddHours(5), ("verification", "decide", "Department"))
            .GetAppealsAsync(salesManager.Id, new AppealFilterDto());

        Assert.Equal([org.SellerAppeal.Id], Ids(result));
    }

    [Fact]
    public async Task List_WithDepartmentScope_AlwaysIncludesTheirOwnAppeals()
    {
        var org = await SeedOrgAsync();
        var ownAppeal = await AddAppealAsync(org.Teller, await AddSubmissionAsync(org.Teller), filedAt: RejectedAt.AddHours(4));

        var result = await ServiceAt(RejectedAt.AddHours(5), ("verification", "decide", "Department"))
            .GetAppealsAsync(org.Teller.Id, new AppealFilterDto());

        Assert.Equal([ownAppeal.Id, org.TellerAppeal.Id], Ids(result));
    }

    [Fact]
    public async Task List_WithBranchScope_SeesTheWholeBranch()
    {
        var org = await SeedOrgAsync();
        var tellerManager = await AddEmployeeAsync("TELLERMGR", _tellerId);

        var result = await ServiceAt(RejectedAt.AddHours(5), ("verification", "decide", "Branch"))
            .GetAppealsAsync(tellerManager.Id, new AppealFilterDto());

        Assert.Equal([org.BranchAppeal.Id, org.TellerAppeal.Id, org.SellerAppeal.Id], Ids(result));
    }

    [Theory]
    [InlineData("All")]
    [InlineData("System")]
    public async Task List_WithGlobalScope_SeesEverything(string scope)
    {
        var org = await SeedOrgAsync();
        var admin = await AddEmployeeAsync("ADMIN", _tellerId);
        // Even an appeal from a department outside the branch tree.
        var other = await AddEmployeeAsync("OTHER", Guid.NewGuid());
        var otherAppeal = await AddAppealAsync(other, await AddSubmissionAsync(other), filedAt: RejectedAt.AddHours(9));

        var result = await ServiceAt(RejectedAt.AddHours(10), ("verification", "decide", scope))
            .GetAppealsAsync(admin.Id, new AppealFilterDto());

        Assert.Equal(4, result.TotalCount);
        Assert.Contains(otherAppeal.Id, Ids(result));
        Assert.Contains(org.SellerAppeal.Id, Ids(result));
    }

    [Fact]
    public async Task List_WithTheAdministratorWildcard_SeesEverything()
    {
        await SeedOrgAsync();
        var admin = await AddEmployeeAsync("ADMIN", _tellerId);

        var result = await ServiceAt(RejectedAt.AddHours(5), ("*", "*", "All"))
            .GetAppealsAsync(admin.Id, new AppealFilterDto());

        Assert.Equal(3, result.TotalCount);
    }

    [Fact]
    public async Task List_APermissionForAnotherResource_DoesNotWidenVisibility()
    {
        var org = await SeedOrgAsync();

        var result = await ServiceAt(RejectedAt.AddHours(5), ("evaluation", "read", "All"))
            .GetAppealsAsync(org.Seller.Id, new AppealFilterDto());

        Assert.Equal([org.SellerAppeal.Id], Ids(result));
    }

    [Fact]
    public async Task List_NeverReturnsAppealsAboutAnEvaluation()
    {
        var org = await SeedOrgAsync();
        await AddAppealAsync(org.Seller, null);

        var result = await ServiceAt(RejectedAt.AddHours(5)).GetAppealsAsync(org.Seller.Id, new AppealFilterDto());

        Assert.Equal([org.SellerAppeal.Id], Ids(result));
    }

    [Fact]
    public async Task List_FiltersByStatus_CaseInsensitively()
    {
        var org = await SeedOrgAsync();
        var withdrawn = await AddAppealAsync(org.Seller, await AddSubmissionAsync(org.Seller), AppealStatus.Withdrawn, RejectedAt.AddHours(6));
        var service = ServiceAt(RejectedAt.AddHours(7));

        var onlyWithdrawn = await service.GetAppealsAsync(org.Seller.Id, new AppealFilterDto { Status = "withdrawn" });
        var onlyUnderReview = await service.GetAppealsAsync(org.Seller.Id, new AppealFilterDto { Status = "UNDER_REVIEW" });

        Assert.Equal([withdrawn.Id], Ids(onlyWithdrawn));
        Assert.Empty(onlyUnderReview.Items);
        Assert.Equal(0, onlyUnderReview.TotalCount);
    }

    [Fact]
    public async Task List_WithAnUnknownStatus_IsAValidationError()
    {
        var org = await SeedOrgAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            ServiceAt(RejectedAt.AddHours(5)).GetAppealsAsync(org.Seller.Id, new AppealFilterDto { Status = "DONE" }));
    }

    [Fact]
    public async Task List_IsPagedNewestFirst_AndClampsThePageSize()
    {
        var org = await SeedOrgAsync();
        var service = ServiceAt(RejectedAt.AddHours(10), ("verification", "decide", "All"));
        var admin = await AddEmployeeAsync("ADMIN", _tellerId);

        var first = await service.GetAppealsAsync(admin.Id, new AppealFilterDto { Page = 1, PageSize = 2 });
        var second = await service.GetAppealsAsync(admin.Id, new AppealFilterDto { Page = 2, PageSize = 2 });
        var clamped = await service.GetAppealsAsync(admin.Id, new AppealFilterDto { Page = 0, PageSize = 1000 });
        var tiny = await service.GetAppealsAsync(admin.Id, new AppealFilterDto { PageSize = 0 });

        Assert.Equal([org.BranchAppeal.Id, org.TellerAppeal.Id], Ids(first));
        Assert.Equal([org.SellerAppeal.Id], Ids(second));
        Assert.Equal(3, first.TotalCount);
        Assert.Equal(2, second.Page);
        Assert.Equal(1, clamped.Page);
        Assert.Equal(100, clamped.PageSize);
        Assert.Equal(1, tiny.PageSize);
        Assert.Single(tiny.Items);
    }

    [Fact]
    public async Task List_ItemsCarryTheWorkAndSubmissionDetails()
    {
        var org = await SeedOrgAsync();

        var result = await ServiceAt(RejectedAt.AddHours(5)).GetAppealsAsync(org.Seller.Id, new AppealFilterDto());

        var item = Assert.Single(result.Items);
        var submission = await _db.ResultSubmissions.Include(s => s.Work).SingleAsync(s => s.Id == org.SellerAppeal.ResultSubmissionId);
        Assert.Equal(submission.Id, item.ResultSubmissionId);
        Assert.Equal(submission.WorkId, item.WorkId);
        Assert.Equal(submission.Work.Code, item.WorkCode);
        Assert.Equal("Work", item.WorkTitle);
        Assert.Equal(1, item.SubmissionNo);
        Assert.Equal(org.Seller.Id, item.EmployeeId);
    }

    [Fact]
    public async Task List_ByAnInactiveCaller_IsUnauthorized()
    {
        var org = await SeedOrgAsync();
        var tracked = await _db.Employees.SingleAsync(e => e.Id == org.Seller.Id);
        tracked.Status = EmployeeStatus.Inactive;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            ServiceAt(RejectedAt.AddHours(5)).GetAppealsAsync(org.Seller.Id, new AppealFilterDto()));
    }

    [Fact]
    public async Task Detail_TheOwner_SeesReasonEvidenceAndResolver()
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var resolver = await AddEmployeeAsync("RESOLVER", _salesId);
        var submission = await AddSubmissionAsync(submitter);
        var appeal = await AddAppealAsync(submitter, submission, AppealStatus.Accepted);
        var tracked = await _db.Appeals.SingleAsync();
        tracked.ResolvedBy = resolver.Id;
        tracked.Note = "Đã xem xét";
        tracked.DecidedAt = RejectedAt.AddHours(5);
        _db.Evidences.Add(new Evidence
        {
            Id = Guid.NewGuid(),
            AppealId = appeal.Id,
            FileName = "a.pdf",
            FilePath = "stored.pdf",
            MimeType = "application/pdf",
            FileSize = 10,
            UploadedAt = RejectedAt,
            UpdatedAt = RejectedAt,
        });
        await _db.SaveChangesAsync();

        var result = await ServiceAt(RejectedAt.AddHours(6)).GetAppealByIdAsync(appeal.Id, submitter.Id);

        Assert.Equal("ACCEPTED", result.Status);
        Assert.Equal("Reason", result.Reason);
        Assert.Equal("Đã xem xét", result.Note);
        Assert.Equal(resolver.Id, result.ResolvedByEmployeeId);
        Assert.Equal("Name RESOLVER", result.ResolvedByEmployeeName);
        Assert.Equal(RejectedAt.AddHours(5), result.DecidedAt);
        var evidence = Assert.Single(result.Evidences);
        Assert.Equal("a.pdf", evidence.FileName);
        Assert.Equal($"/evidence/{evidence.Id}/preview", evidence.PreviewUrl);
    }

    [Fact]
    public async Task Detail_ForAnUnresolvedAppeal_HasNoResolver()
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var appeal = await AddAppealAsync(submitter, await AddSubmissionAsync(submitter));

        var result = await ServiceAt(RejectedAt.AddHours(6)).GetAppealByIdAsync(appeal.Id, submitter.Id);

        Assert.Null(result.ResolvedByEmployeeId);
        Assert.Null(result.ResolvedByEmployeeName);
        Assert.Null(result.Note);
        Assert.Empty(result.Evidences);
    }

    [Fact]
    public async Task Detail_ACoworkerWithoutPermission_IsForbidden()
    {
        var org = await SeedOrgAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            ServiceAt(RejectedAt.AddHours(6)).GetAppealByIdAsync(org.SellerAppeal.Id, org.Teller.Id));
    }

    [Fact]
    public async Task Detail_AManagerSeesOnlyAppealsInsideTheirScope()
    {
        var org = await SeedOrgAsync();
        var salesManager = await AddEmployeeAsync("SALESMGR", _salesId);
        var service = ServiceAt(RejectedAt.AddHours(6), ("verification", "decide", "Department"));

        var inside = await service.GetAppealByIdAsync(org.SellerAppeal.Id, salesManager.Id);

        Assert.Equal(org.SellerAppeal.Id, inside.Id);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetAppealByIdAsync(org.TellerAppeal.Id, salesManager.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetAppealByIdAsync(org.BranchAppeal.Id, salesManager.Id));
    }

    [Fact]
    public async Task Detail_GlobalScope_SeesAnyAppeal()
    {
        var org = await SeedOrgAsync();
        var admin = await AddEmployeeAsync("ADMIN", _tellerId);

        var result = await ServiceAt(RejectedAt.AddHours(6), ("*", "*", "All")).GetAppealByIdAsync(org.SellerAppeal.Id, admin.Id);

        Assert.Equal(org.SellerAppeal.Id, result.Id);
    }

    [Fact]
    public async Task Detail_UnknownOrEvaluationAppeal_IsNotFound()
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var evaluationAppeal = await AddAppealAsync(submitter, null);
        var service = ServiceAt(RejectedAt.AddHours(6), ("*", "*", "All"));

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAppealByIdAsync(Guid.NewGuid(), submitter.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAppealByIdAsync(evaluationAppeal.Id, submitter.Id));
    }

    // ----- BR-31: Disputed_Pending -----------------------------------------------------------

    private async Task<PerformanceEvaluation> AddEvaluationAsync(
        Employee employee,
        PerformanceEvaluationStatus status,
        DateOnly? start = null,
        DateOnly? end = null,
        PeriodStatus periodStatus = PeriodStatus.Open)
    {
        var period = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = $"P-{Guid.NewGuid():N}"[..10],
            Name = "Period",
            PeriodType = PeriodType.Month,
            StartDate = start ?? new DateOnly(2026, 10, 1),
            EndDate = end ?? new DateOnly(2026, 10, 31),
            Status = periodStatus,
            UpdatedAt = RejectedAt,
        };
        var evaluation = new PerformanceEvaluation
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            EvaluationPeriodId = period.Id,
            EvaluationProfileId = Guid.NewGuid(),
            TotalScore = 88m,
            Status = status,
            UpdatedAt = RejectedAt,
        };
        _db.EvaluationPeriods.Add(period);
        _db.PerformanceEvaluations.Add(evaluation);
        await _db.SaveChangesAsync();
        return evaluation;
    }

    [Theory]
    [InlineData(PerformanceEvaluationStatus.Calculated)]
    [InlineData(PerformanceEvaluationStatus.InReview)]
    [InlineData(PerformanceEvaluationStatus.Approved)]
    public async Task Create_MarksTheScoreOfThePeriodDisputedPending_AndKeepsTheStatusItHad(PerformanceEvaluationStatus before)
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var submission = await AddSubmissionAsync(submitter);
        var evaluation = await AddEvaluationAsync(submitter, before);
        var now = RejectedAt.AddHours(5);

        await ServiceAt(now).CreateAppealAsync(submission.Id, submitter.Id, Request());

        var stored = await _db.PerformanceEvaluations.SingleAsync(e => e.Id == evaluation.Id);
        Assert.Equal(PerformanceEvaluationStatus.Disputed, stored.Status);
        Assert.Equal(before, stored.PreDisputeStatus);
        Assert.Equal(now, stored.DisputedAt);
        Assert.Equal(88m, stored.TotalScore);
    }

    [Fact]
    public async Task Create_LeavesDraftFinalLockedOtherPeriodAndOtherEmployeeScoresAlone()
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var colleague = await AddEmployeeAsync("COLLEAGUE", _salesTeamId);
        var submission = await AddSubmissionAsync(submitter);
        var draft = await AddEvaluationAsync(submitter, PerformanceEvaluationStatus.Draft);
        var final = await AddEvaluationAsync(submitter, PerformanceEvaluationStatus.Final);
        var locked = await AddEvaluationAsync(
            submitter, PerformanceEvaluationStatus.Approved, periodStatus: PeriodStatus.Locked);
        var otherPeriod = await AddEvaluationAsync(
            submitter, PerformanceEvaluationStatus.Calculated, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        var others = await AddEvaluationAsync(colleague, PerformanceEvaluationStatus.Calculated);

        await ServiceAt(RejectedAt.AddHours(5)).CreateAppealAsync(submission.Id, submitter.Id, Request());

        foreach (var untouched in new[] { draft, final, locked, otherPeriod, others })
        {
            var stored = await _db.PerformanceEvaluations.SingleAsync(e => e.Id == untouched.Id);
            Assert.Equal(untouched.Status, stored.Status);
            Assert.Null(stored.PreDisputeStatus);
        }
    }

    [Fact]
    public async Task Withdraw_RestoresThePreDisputeStatusOfTheScore()
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var submission = await AddSubmissionAsync(submitter);
        var evaluation = await AddEvaluationAsync(submitter, PerformanceEvaluationStatus.InReview);
        var created = await ServiceAt(RejectedAt.AddHours(2)).CreateAppealAsync(submission.Id, submitter.Id, Request());

        await ServiceAt(RejectedAt.AddHours(3)).WithdrawAppealAsync(created.Id, submitter.Id);

        var stored = await _db.PerformanceEvaluations.SingleAsync(e => e.Id == evaluation.Id);
        Assert.Equal(PerformanceEvaluationStatus.InReview, stored.Status);
        Assert.Null(stored.PreDisputeStatus);
        Assert.Null(stored.DisputedAt);
    }

    [Fact]
    public async Task Withdraw_KeepsTheScoreDisputed_WhileAnotherAppealOfThePeriodIsInProgress()
    {
        var submitter = await SubmitterWithRejectedSubmissionAsync();
        var first = await AddSubmissionAsync(submitter);
        var second = await AddSubmissionAsync(submitter);
        var evaluation = await AddEvaluationAsync(submitter, PerformanceEvaluationStatus.Calculated);
        var service = ServiceAt(RejectedAt.AddHours(2));
        var firstAppeal = await service.CreateAppealAsync(first.Id, submitter.Id, Request());
        await service.CreateAppealAsync(second.Id, submitter.Id, Request());

        await service.WithdrawAppealAsync(firstAppeal.Id, submitter.Id);

        var stored = await _db.PerformanceEvaluations.SingleAsync(e => e.Id == evaluation.Id);
        Assert.Equal(PerformanceEvaluationStatus.Disputed, stored.Status);
        Assert.Equal(PerformanceEvaluationStatus.Calculated, stored.PreDisputeStatus);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class GrantingPermissions((string Resource, string Action, string Scope)[] grants) : IPermissionService
    {
        public Task<UserPermissionsResponseDto?> GetUserPermissionsAsync(Guid accountId, CancellationToken cancellationToken) =>
            Task.FromResult<UserPermissionsResponseDto?>(null);

        public Task<IReadOnlyList<UserPermissionDto>> GetEffectivePermissionsAsync(Guid accountId, CancellationToken cancellationToken)
        {
            IReadOnlyList<UserPermissionDto> result = grants.Select(g => new UserPermissionDto(g.Resource, g.Action, g.Scope)).ToList();
            return Task.FromResult(result);
        }

        public Task<bool> HasPermissionAsync(Guid accountId, string resource, string action, string requiredScope, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task InvalidateUserPermissionsAsync(Guid accountId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task InvalidateRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
