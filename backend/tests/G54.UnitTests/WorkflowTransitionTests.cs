using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Permission;
using G54.BLL.Dtos.Submission;
using G54.BLL.Dtos.Verification;
using G54.BLL.Dtos.Work;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace G54.UnitTests;

/// <summary>
/// The Work and ResultSubmission state machines as the services drive them: which statuses each entry point accepts,
/// what the work and the submission end up as, and that a refused transition changes nothing. The pure transition tables are
/// covered by <see cref="WorkStateMachineTests"/> and <see cref="SubmissionStateMachineTests"/>.
/// </summary>
public sealed class WorkflowTransitionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 4, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 21);

    // ----- Work.ChangeStatus: Start ---------------------------------------------------------

    [Theory]
    [InlineData(WorkStatus.New)]
    [InlineData(WorkStatus.HandedOver)]
    [InlineData(WorkStatus.Rejected)]
    public async Task Start_IsAcceptedFromEveryStatusThatMayStart(WorkStatus from)
    {
        using var rig = await Rig.CreateAsync(from);

        var detail = await rig.Works().ChangeStatusAsync(rig.Work.Id, rig.Assignee.Id, new WorkStatusDto { Status = "InProgress" });

        Assert.Equal("InProgress", detail.Status);
        Assert.Equal(WorkStatus.InProgress, await rig.WorkStatusAsync());
    }

    [Theory]
    [InlineData(WorkStatus.InProgress)] // already started
    [InlineData(WorkStatus.Submitted)]
    [InlineData(WorkStatus.Verified)]
    [InlineData(WorkStatus.Overdue)]
    [InlineData(WorkStatus.Done)]
    [InlineData(WorkStatus.Cancelled)]
    public async Task Start_IsRefusedFromEveryOtherStatus_AndLeavesTheWorkUntouched(WorkStatus from)
    {
        using var rig = await Rig.CreateAsync(from);

        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            rig.Works().ChangeStatusAsync(rig.Work.Id, rig.Assignee.Id, new WorkStatusDto { Status = "InProgress" }));

        Assert.Equal(ErrorCodes.WorkInvalidState, error.ErrorCode);
        Assert.Equal(from, await rig.WorkStatusAsync());
    }

    // ----- Work.ChangeStatus: Cancel --------------------------------------------------------

    [Theory]
    [InlineData(WorkStatus.New)]
    [InlineData(WorkStatus.InProgress)]
    [InlineData(WorkStatus.HandedOver)]
    [InlineData(WorkStatus.Rejected)]
    [InlineData(WorkStatus.Overdue)]
    public async Task Cancel_IsAcceptedFromEveryOpenStatusByTheDirectManager(WorkStatus from)
    {
        using var rig = await Rig.CreateAsync(from);

        var detail = await rig.Works().ChangeStatusAsync(
            rig.Work.Id, rig.Manager.Id, new WorkStatusDto { Status = "Cancelled", Reason = "Khách hàng rút yêu cầu" });

        Assert.Equal("Cancelled", detail.Status);
        Assert.Equal(WorkStatus.Cancelled, await rig.WorkStatusAsync());
    }

    [Theory]
    [InlineData(WorkStatus.Submitted)] // a result is waiting for verification
    [InlineData(WorkStatus.Verified)] // already approved: it can only be completed
    [InlineData(WorkStatus.Done)]
    [InlineData(WorkStatus.Cancelled)]
    public async Task Cancel_IsRefusedOnceAResultIsSubmittedOrTheWorkIsClosed(WorkStatus from)
    {
        using var rig = await Rig.CreateAsync(from);

        var error = await Assert.ThrowsAsync<ConflictException>(() => rig.Works().ChangeStatusAsync(
            rig.Work.Id, rig.Manager.Id, new WorkStatusDto { Status = "Cancelled", Reason = "Không còn cần thiết" }));

        Assert.Equal(ErrorCodes.WorkInvalidState, error.ErrorCode);
        Assert.Equal(from, await rig.WorkStatusAsync());
    }

    [Fact]
    public async Task Cancel_DoesNotCloseTheOpenSubmissionOfACancelledWork()
    {
        // Documents current behaviour: cancelling only moves the work. A submission that was sent back for revision stays
        // UnderReview, so the history of a cancelled work can still show an open submission.
        using var rig = await Rig.CreateAsync(WorkStatus.InProgress);
        var submission = rig.AddSubmission(SubmissionStatus.UnderReview);
        await rig.SaveAsync();

        await rig.Works().ChangeStatusAsync(
            rig.Work.Id, rig.Manager.Id, new WorkStatusDto { Status = "Cancelled", Reason = "Dừng triển khai" });

        Assert.Equal(WorkStatus.Cancelled, await rig.WorkStatusAsync());
        Assert.Equal(SubmissionStatus.UnderReview, (await rig.Db.ResultSubmissions.AsNoTracking().SingleAsync(s => s.Id == submission.Id)).Status);
    }

    // ----- Work.MarkOverdue -----------------------------------------------------------------

    [Fact]
    public async Task MarkOverdue_UsesAStrictLessThanOnThePlannedEndDate()
    {
        using var rig = await Rig.CreateAsync(WorkStatus.InProgress, plannedEnd: Today);

        var dueToday = await rig.Works().MarkOverdueAsync(Today);
        rig.Work.PlannedEndDate = Today.AddDays(-1);
        await rig.SaveAsync();
        var dueYesterday = await rig.Works().MarkOverdueAsync(Today);

        Assert.Equal(0, dueToday);
        Assert.Equal(1, dueYesterday);
        Assert.Equal(WorkStatus.Overdue, await rig.WorkStatusAsync());
    }

    [Fact]
    public async Task MarkOverdue_NeverTouchesAWorkWithoutAPlannedEndDate()
    {
        using var rig = await Rig.CreateAsync(WorkStatus.New, plannedEnd: null);

        Assert.Equal(0, await rig.Works().MarkOverdueAsync(Today.AddYears(10)));
        Assert.Equal(WorkStatus.New, await rig.WorkStatusAsync());
    }

    // ----- ResultSubmission.Create ----------------------------------------------------------

    [Theory]
    [InlineData(WorkStatus.New)]
    [InlineData(WorkStatus.InProgress)]
    [InlineData(WorkStatus.Rejected)]
    [InlineData(WorkStatus.Overdue)]
    public async Task Submit_IsAcceptedFromEveryStatusThatMaySubmit(WorkStatus from)
    {
        using var rig = await Rig.CreateAsync(from);

        var result = await rig.Submissions().CreateSubmissionAsync(rig.Work.Id, rig.Assignee.Id, rig.ValidRequest());

        Assert.Equal("SUBMITTED", result.Status);
        Assert.Equal(WorkStatus.Submitted, await rig.WorkStatusAsync());
        Assert.Equal(SubmissionStatus.Submitted, (await rig.Db.ResultSubmissions.SingleAsync()).Status);
    }

    [Theory]
    [InlineData(WorkStatus.HandedOver)] // the work now belongs to someone who has not started it
    [InlineData(WorkStatus.Submitted)]
    [InlineData(WorkStatus.Verified)]
    [InlineData(WorkStatus.Done)]
    [InlineData(WorkStatus.Cancelled)]
    public async Task Submit_IsRefusedFromEveryOtherStatus_AndCreatesNothing(WorkStatus from)
    {
        using var rig = await Rig.CreateAsync(from);

        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            rig.Submissions().CreateSubmissionAsync(rig.Work.Id, rig.Assignee.Id, rig.ValidRequest()));

        Assert.Equal(ErrorCodes.SubmissionInvalidState, error.ErrorCode);
        Assert.Equal(from, await rig.WorkStatusAsync());
        Assert.Empty(await rig.Db.ResultSubmissions.ToListAsync());
    }

    [Fact]
    public async Task Submit_AfterARevisionRequestLeavesTheEarlierSubmissionUnderReview()
    {
        // Documents current behaviour: the revised work can be submitted again, but the earlier submission that asked for the
        // revision is not closed, so a work can hold two live submissions until the verifier decides.
        using var rig = await Rig.CreateAsync(WorkStatus.InProgress);
        var earlier = rig.AddSubmission(SubmissionStatus.UnderReview);
        await rig.SaveAsync();

        var result = await rig.Submissions().CreateSubmissionAsync(rig.Work.Id, rig.Assignee.Id, rig.ValidRequest());

        Assert.Equal(2, result.SubmissionNo);
        var stored = await rig.Db.ResultSubmissions.AsNoTracking().OrderBy(s => s.SubmissionNo).ToListAsync();
        Assert.Equal([SubmissionStatus.UnderReview, SubmissionStatus.Submitted], stored.Select(s => s.Status));
        Assert.Equal(earlier.Id, stored[0].Id);
        Assert.Equal(WorkStatus.Submitted, await rig.WorkStatusAsync());
    }

    // ----- ResultSubmission.Withdraw --------------------------------------------------------

    [Theory]
    [InlineData(SubmissionStatus.UnderReview)]
    [InlineData(SubmissionStatus.Approved)]
    [InlineData(SubmissionStatus.Rejected)]
    [InlineData(SubmissionStatus.Withdrawn)]
    [InlineData(SubmissionStatus.Draft)]
    public async Task Withdraw_IsRefusedUnlessTheSubmissionIsStillSubmitted(SubmissionStatus status)
    {
        using var rig = await Rig.CreateAsync(WorkStatus.Submitted);
        var submission = rig.AddSubmission(status);
        await rig.SaveAsync();

        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            rig.Submissions().WithdrawSubmissionAsync(submission.Id, rig.Assignee.Id));

        Assert.Equal(ErrorCodes.SubmissionInvalidState, error.ErrorCode);
        Assert.Equal(status, (await rig.Db.ResultSubmissions.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(WorkStatus.Submitted, await rig.WorkStatusAsync());
    }

    [Theory]
    [InlineData(WorkStatus.New)]
    [InlineData(WorkStatus.Verified)]
    [InlineData(WorkStatus.Done)]
    [InlineData(WorkStatus.Cancelled)]
    [InlineData(WorkStatus.Overdue)]
    public async Task Withdraw_IsRefusedWhenTheWorkCannotBeReopened_AndKeepsTheSubmissionSubmitted(WorkStatus workStatus)
    {
        using var rig = await Rig.CreateAsync(workStatus);
        var submission = rig.AddSubmission(SubmissionStatus.Submitted);
        await rig.SaveAsync();

        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            rig.Submissions().WithdrawSubmissionAsync(submission.Id, rig.Assignee.Id));

        Assert.Equal(ErrorCodes.SubmissionInvalidState, error.ErrorCode);
        // The submission is only changed once both machines agree, so a refusal must not leave it half-withdrawn.
        Assert.Equal(SubmissionStatus.Submitted, (await rig.Db.ResultSubmissions.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(workStatus, await rig.WorkStatusAsync());
    }

    // ----- Verification: both machines must agree -------------------------------------------

    [Theory]
    [InlineData("APPROVED")]
    [InlineData("REJECTED")]
    [InlineData("REVISION_REQUIRED")]
    public async Task Verification_IsRefusedWhenTheWorkIsInAStateTheDecisionCannotLeave(string decision)
    {
        // The submission is waiting for a decision, but its work was already closed (here: done) by another path.
        using var rig = await Rig.CreateAsync(WorkStatus.Done);
        var submission = rig.AddSubmission(SubmissionStatus.Submitted);
        await rig.SaveAsync();

        var error = await Assert.ThrowsAsync<ConflictException>(() => rig.Verifications().SubmitDecisionAsync(
            submission.Id,
            rig.CheckerAccount.Id,
            new SubmitVerificationDecisionDto
            {
                Decision = decision,
                VerifiedValue = 1m,
                Feedback = "Lý do đủ dài để hợp lệ.",
                ResubmitDeadline = DateTimeOffset.UtcNow.AddDays(2),
            }));

        Assert.Equal(ErrorCodes.VerificationInvalidState, error.ErrorCode);
        Assert.Equal(SubmissionStatus.Submitted, (await rig.Db.ResultSubmissions.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(WorkStatus.Done, await rig.WorkStatusAsync());
        Assert.Empty(await rig.Db.Verifications.ToListAsync());
    }

    [Theory]
    [InlineData("APPROVED", "APPROVED", "VERIFIED")]
    [InlineData("REJECTED", "REJECTED", "REJECTED")]
    [InlineData("REVISION_REQUIRED", "UNDER_REVIEW", "IN_PROGRESS")]
    public async Task Verification_MovesTheSubmissionAndTheWorkTogether(string decision, string submissionStatus, string workStatus)
    {
        using var rig = await Rig.CreateAsync(WorkStatus.Submitted);
        var submission = rig.AddSubmission(SubmissionStatus.Submitted);
        await rig.SaveAsync();

        var result = await rig.Verifications().SubmitDecisionAsync(
            submission.Id,
            rig.CheckerAccount.Id,
            new SubmitVerificationDecisionDto
            {
                Decision = decision,
                Feedback = "Lý do đủ dài để hợp lệ.",
                ResubmitDeadline = DateTimeOffset.UtcNow.AddDays(2),
            });

        Assert.Equal(submissionStatus, result.SubmissionStatus);
        Assert.Equal(workStatus, result.WorkStatus);
    }

    [Fact]
    public async Task Verification_OfARevisedSubmissionMayBeDecidedAgainWhileTheWorkIsInProgress()
    {
        // After REVISION_REQUIRED the work is IN_PROGRESS and the submission UNDER_REVIEW; the verifier may still decide.
        using var rig = await Rig.CreateAsync(WorkStatus.InProgress);
        var submission = rig.AddSubmission(SubmissionStatus.UnderReview);
        await rig.SaveAsync();

        var result = await rig.Verifications().SubmitDecisionAsync(
            submission.Id, rig.CheckerAccount.Id, new SubmitVerificationDecisionDto { Decision = "APPROVED", VerifiedValue = 1m });

        Assert.Equal("APPROVED", result.SubmissionStatus);
        Assert.Equal("VERIFIED", result.WorkStatus);
    }

    [Fact]
    public async Task Verification_ByALockedAccountIsRefusedAndChangesNothing()
    {
        using var rig = await Rig.CreateAsync(WorkStatus.Submitted);
        var submission = rig.AddSubmission(SubmissionStatus.Submitted);
        rig.CheckerAccount.IsLocked = true;
        await rig.SaveAsync();

        await Assert.ThrowsAsync<UnauthorizedException>(() => rig.Verifications().SubmitDecisionAsync(
            submission.Id, rig.CheckerAccount.Id, new SubmitVerificationDecisionDto { Decision = "APPROVED", VerifiedValue = 1m }));

        Assert.Equal(SubmissionStatus.Submitted, (await rig.Db.ResultSubmissions.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(WorkStatus.Submitted, await rig.WorkStatusAsync());
    }

    [Fact]
    public async Task Verification_ByAnInactiveEmployeeIsRefusedAndChangesNothing()
    {
        using var rig = await Rig.CreateAsync(WorkStatus.Submitted);
        var submission = rig.AddSubmission(SubmissionStatus.Submitted);
        rig.Checker.Status = EmployeeStatus.Inactive;
        await rig.SaveAsync();

        await Assert.ThrowsAsync<UnauthorizedException>(() => rig.Verifications().SubmitDecisionAsync(
            submission.Id, rig.CheckerAccount.Id, new SubmitVerificationDecisionDto { Decision = "APPROVED", VerifiedValue = 1m }));

        Assert.Equal(WorkStatus.Submitted, await rig.WorkStatusAsync());
    }

    [Theory]
    [InlineData("MAYBE")]
    [InlineData("approve")]
    [InlineData("")]
    public async Task Verification_RefusesADecisionThatIsNotOneOfTheThree(string decision)
    {
        using var rig = await Rig.CreateAsync(WorkStatus.Submitted);
        var submission = rig.AddSubmission(SubmissionStatus.Submitted);
        await rig.SaveAsync();

        await Assert.ThrowsAsync<ValidationException>(() => rig.Verifications().SubmitDecisionAsync(
            submission.Id, rig.CheckerAccount.Id, new SubmitVerificationDecisionDto { Decision = decision }));

        Assert.Equal(WorkStatus.Submitted, await rig.WorkStatusAsync());
    }

    // ----- Fixture --------------------------------------------------------------------------

    private sealed class Rig : IDisposable
    {
        private int _submissionNo;

        public required AppDbContext Db { get; init; }
        public required Employee Manager { get; init; }
        public required Employee Assignee { get; init; }
        public required Employee Checker { get; init; }
        public required Account CheckerAccount { get; init; }
        public required Work Work { get; init; }
        public required KpiDefinition Kpi { get; init; }

        public static async Task<Rig> CreateAsync(WorkStatus status, DateOnly? plannedEnd = null)
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options);

            var department = new Department
            {
                Id = Guid.NewGuid(),
                Code = "D",
                Name = "Department",
                LevelType = DepartmentLevelType.Department,
                Status = RecordStatus.Active,
                UpdatedAt = Now,
            };
            var position = new JobPosition
            {
                Id = Guid.NewGuid(),
                Code = "P",
                Title = "Position",
                Status = RecordStatus.Active,
                UpdatedAt = Now,
            };
            db.Departments.Add(department);
            db.JobPositions.Add(position);

            var manager = Person("MANAGER", department, position);
            var assignee = Person("ASSIGNEE", department, position);
            assignee.ManagerId = manager.Id;
            var checker = Person("CHECKER", department, position);
            db.Employees.AddRange(manager, assignee, checker);
            var account = new Account
            {
                Id = Guid.NewGuid(),
                EmployeeId = checker.Id,
                Username = "checker",
                PasswordHash = "hash",
                LastSyncAt = Now,
                UpdatedAt = Now,
            };
            db.Accounts.Add(account);

            var workType = new WorkType { Id = Guid.NewGuid(), Code = "WT", Name = "Work type", Status = RecordStatus.Active, UpdatedAt = Now };
            var group = new KpiGroup { Id = Guid.NewGuid(), Code = "G", Name = "Group", Status = KpiStatus.Active, UpdatedAt = Now };
            var kpi = new KpiDefinition
            {
                Id = Guid.NewGuid(),
                KpiGroupId = group.Id,
                Code = "KPI",
                Name = "KPI",
                Unit = "unit",
                AggregationType = KpiAggregationType.Sum,
                Direction = KpiDirection.HigherBetter,
                Status = KpiStatus.Active,
                UpdatedAt = Now,
            };
            db.WorkTypes.Add(workType);
            db.KpiGroups.Add(group);
            db.KpiDefinitions.Add(kpi);
            db.WorkTypeKpis.Add(new WorkTypeKpi
            {
                Id = Guid.NewGuid(),
                WorkTypeId = workType.Id,
                KpiDefinitionId = kpi.Id,
                IsRequired = true,
                EffectiveFrom = Today.AddDays(-365),
                UpdatedAt = Now,
            });

            var work = new Work
            {
                Id = Guid.NewGuid(),
                Code = "WRK-1",
                Title = "Work",
                WorkTypeId = workType.Id,
                AssigneeEmployeeId = assignee.Id,
                CreatedBy = assignee.Id,
                Status = status,
                Priority = WorkPriority.Medium,
                PlannedEndDate = plannedEnd,
                UpdatedAt = Now,
            };
            db.Works.Add(work);
            await db.SaveChangesAsync();

            return new Rig
            {
                Db = db,
                Manager = manager,
                Assignee = assignee,
                Checker = checker,
                CheckerAccount = account,
                Work = work,
                Kpi = kpi,
            };
        }

        public WorkService Works() => new(
            new UnitOfWork(Db),
            NullLogger<WorkService>.Instance,
            new FixedTime(Now),
            new FakePermissionService(null, FakePermissionService.WorkCreate("All")));

        public ResultSubmissionService Submissions()
        {
            var uow = new UnitOfWork(Db);
            var storage = new StorageService(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:EvidencePath"] = Path.Combine(Path.GetTempPath(), "G54_Workflow_" + Guid.NewGuid().ToString("N")) })
                    .Build(),
                NullLogger<StorageService>.Instance);
            return new ResultSubmissionService(
                uow,
                storage,
                new WorkSubmissionValidator(uow, new FixedTime(Now)),
                new EventBus(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), NullLogger<EventBus>.Instance),
                NullLogger<ResultSubmissionService>.Instance,
                new FixedTime(Now),
                new FakePermissionService(null));
        }

        public VerificationService Verifications() => new(
            Db,
            NullLogger<VerificationService>.Instance,
            new FixedTime(Now),
            new FakePermissionService(null, new UserPermissionDto("verification", "decide", "All", "VERIFICATION_DECIDE")));

        public Task<int> SaveAsync() => Db.SaveChangesAsync();

        public async Task<WorkStatus> WorkStatusAsync() =>
            (await Db.Works.AsNoTracking().SingleAsync(w => w.Id == Work.Id)).Status;

        public CreateWorkSubmissionRequestDto ValidRequest() => new()
        {
            Comment = "Kết quả công việc",
            MetricItems = [new SubmissionMetricInputDto(Kpi.Id, 10m, null)],
        };

        public ResultSubmission AddSubmission(SubmissionStatus status)
        {
            var submission = new ResultSubmission
            {
                Id = Guid.NewGuid(),
                WorkId = Work.Id,
                SubmitterEmployeeId = Assignee.Id,
                SubmissionNo = ++_submissionNo,
                Status = status,
                SubmittedAt = Now,
                UpdatedAt = Now,
            };
            Db.ResultSubmissions.Add(submission);
            return submission;
        }

        public void Dispose() => Db.Dispose();

        private static Employee Person(string code, Department department, JobPosition position) => new()
        {
            Id = Guid.NewGuid(),
            EmployeeCode = code,
            FullName = code,
            Email = $"{code.ToLowerInvariant()}@example.test",
            DepartmentId = department.Id,
            JobPositionId = position.Id,
            Status = EmployeeStatus.Active,
            LastSyncAt = Now,
            UpdatedAt = Now,
        };
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
