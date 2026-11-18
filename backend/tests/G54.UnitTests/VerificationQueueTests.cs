using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Permission;
using G54.BLL.Dtos.Verification;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace G54.UnitTests;

/// <summary>
/// GET /verifications. Branch CN holds Phòng A and Phòng B; the verifier sits in Phòng A.
/// </summary>
public sealed class VerificationQueueTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 3, 0, 0, TimeSpan.Zero);

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false).Options);

    private static VerificationService CreateService(AppDbContext context, params UserPermissionDto[] grants) =>
        new(context, NullLogger<VerificationService>.Instance, TimeProvider.System, new FakePermissionService(null, grants));

    private static UserPermissionDto Decide(string scope) => new("verification", "decide", scope, "VERIFICATION_DECIDE");

    private sealed class Scenario
    {
        public required Guid VerifierAccountId { get; init; }
        public required Department DeptA { get; init; }
        public required Department DeptB { get; init; }
        public required Employee VerifierEmployee { get; init; }
        public required Employee SubmitterA { get; init; }
        public required Employee SubmitterB { get; init; }
        public required AppDbContext Context { get; init; }
    }

    private static async Task<Scenario> SeedAsync(AppDbContext context)
    {
        var branch = NewDepartment("CN", DepartmentLevelType.Branch, null);
        var deptA = NewDepartment("A", DepartmentLevelType.Department, branch.Id);
        var deptB = NewDepartment("B", DepartmentLevelType.Department, branch.Id);
        context.Departments.AddRange(branch, deptA, deptB);

        var position = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = "P-Q",
            Title = "Chuyên viên",
            Status = RecordStatus.Active,
            UpdatedAt = Now,
        };
        context.JobPositions.Add(position);

        var verifier = NewEmployee("EMP-V", "Người thẩm định", deptA.Id, position.Id);
        var submitterA = NewEmployee("EMP-A1", "An Phòng A", deptA.Id, position.Id);
        var submitterB = NewEmployee("EMP-B1", "Bình Phòng B", deptB.Id, position.Id);
        context.Employees.AddRange(verifier, submitterA, submitterB);

        var account = new Account
        {
            Id = Guid.NewGuid(),
            EmployeeId = verifier.Id,
            Username = verifier.Email,
            PasswordHash = "hash",
            LastSyncAt = Now,
            UpdatedAt = Now,
        };
        context.Accounts.Add(account);
        await context.SaveChangesAsync();

        return new Scenario
        {
            VerifierAccountId = account.Id,
            DeptA = deptA,
            DeptB = deptB,
            VerifierEmployee = verifier,
            SubmitterA = submitterA,
            SubmitterB = submitterB,
            Context = context,
        };
    }

    private static Department NewDepartment(string code, DepartmentLevelType level, Guid? parentId) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = $"Đơn vị {code}",
        LevelType = level,
        ParentDepartmentId = parentId,
        Status = RecordStatus.Active,
        LastSyncAt = Now,
        UpdatedAt = Now,
    };

    private static Employee NewEmployee(string code, string name, Guid departmentId, Guid positionId) => new()
    {
        Id = Guid.NewGuid(),
        EmployeeCode = code,
        FullName = name,
        Email = $"{code.ToLowerInvariant()}@bpms.com",
        DepartmentId = departmentId,
        JobPositionId = positionId,
        Status = EmployeeStatus.Active,
        LastSyncAt = Now,
        UpdatedAt = Now,
    };

    /// <summary>Adds a work with one submission and one transaction per given status.</summary>
    private static async Task<ResultSubmission> AddSubmissionAsync(
        Scenario scenario,
        Employee submitter,
        string workCode,
        SubmissionStatus status = SubmissionStatus.Submitted,
        TimeSpan? submittedAfter = null,
        string workTitle = "Công việc",
        params string[] transactionStatuses)
    {
        var context = scenario.Context;
        var work = new Work
        {
            Id = Guid.NewGuid(),
            Code = workCode,
            Title = workTitle,
            AssigneeEmployeeId = submitter.Id,
            CreatedBy = submitter.Id,
            Status = WorkStatus.Submitted,
            Priority = WorkPriority.Medium,
            UpdatedAt = Now,
        };
        context.Works.Add(work);

        var submission = new ResultSubmission
        {
            Id = Guid.NewGuid(),
            WorkId = work.Id,
            SubmitterEmployeeId = submitter.Id,
            SubmissionNo = 1,
            Status = status,
            SubmittedAt = Now + (submittedAfter ?? TimeSpan.Zero),
            UpdatedAt = Now,
        };
        context.ResultSubmissions.Add(submission);

        for (var i = 0; i < transactionStatuses.Length; i++)
        {
            context.WorkTransactions.Add(new WorkTransaction
            {
                Id = Guid.NewGuid(),
                WorkId = work.Id,
                TransactionCode = $"{workCode}-TX{i}",
                Status = transactionStatuses[i],
                CreatedAt = Now,
                UpdatedAt = Now,
            });
        }

        await context.SaveChangesAsync();
        return submission;
    }

    [Fact]
    public async Task GetQueue_DeptScope_ReturnsOnlyPendingSubmissionsOfOwnDepartmentOldestFirst()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var newer = await AddSubmissionAsync(s, s.SubmitterA, "WRK-NEW", submittedAfter: TimeSpan.FromHours(2));
        var older = await AddSubmissionAsync(s, s.SubmitterA, "WRK-OLD", SubmissionStatus.UnderReview);
        await AddSubmissionAsync(s, s.SubmitterA, "WRK-DONE", SubmissionStatus.Approved);
        await AddSubmissionAsync(s, s.SubmitterA, "WRK-REJ", SubmissionStatus.Rejected);
        await AddSubmissionAsync(s, s.SubmitterB, "WRK-OTHER-DEPT");
        await AddSubmissionAsync(s, s.VerifierEmployee, "WRK-MINE");

        var result = await CreateService(context, Decide("Dept")).GetQueueAsync(s.VerifierAccountId, new VerificationQueueFilterDto());

        Assert.Equal(2, result.TotalCount);
        Assert.Equal([older.Id, newer.Id], result.Items.Select(i => i.SubmissionId));
        var item = result.Items[0];
        Assert.Equal("UNDER_REVIEW", item.Status);
        Assert.Equal("#SUB-WRK-OLD-V1", item.SubmissionCode);
        Assert.Equal("EMP-A1", item.SubmitterEmployeeCode);
        Assert.Equal(s.DeptA.Id, item.DepartmentId);
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
    }

    [Fact]
    public async Task GetQueue_BranchScope_IncludesSiblingDepartment()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        await AddSubmissionAsync(s, s.SubmitterA, "WRK-A");
        await AddSubmissionAsync(s, s.SubmitterB, "WRK-B");

        var result = await CreateService(context, Decide("Branch")).GetQueueAsync(s.VerifierAccountId, new VerificationQueueFilterDto());

        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task GetQueue_AllScope_IncludesEveryDepartment()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        await AddSubmissionAsync(s, s.SubmitterA, "WRK-A");
        await AddSubmissionAsync(s, s.SubmitterB, "WRK-B");

        var result = await CreateService(context, Decide("All")).GetQueueAsync(s.VerifierAccountId, new VerificationQueueFilterDto());

        Assert.Equal(2, result.TotalCount);
    }

    [Theory]
    [InlineData("Own")]
    [InlineData(null)]
    public async Task GetQueue_OwnScopeOrNoGrant_ThrowsForbidden(string? scope)
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var service = scope is null ? CreateService(context) : CreateService(context, Decide(scope));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetQueueAsync(s.VerifierAccountId, new VerificationQueueFilterDto()));
    }

    [Fact]
    public async Task GetQueue_UnknownAccount_ThrowsUnauthorized()
    {
        using var context = CreateContext();
        await SeedAsync(context);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            CreateService(context, Decide("All")).GetQueueAsync(Guid.NewGuid(), new VerificationQueueFilterDto()));
    }

    [Fact]
    public async Task GetQueue_ReportsMatchStatusFromTransactionCodes()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        await AddSubmissionAsync(s, s.SubmitterA, "WRK-NONE");
        await AddSubmissionAsync(s, s.SubmitterA, "WRK-MATCHED", transactionStatuses: [WorkTransactionStatus.Matched, WorkTransactionStatus.Matched]);
        await AddSubmissionAsync(s, s.SubmitterA, "WRK-MISS", transactionStatuses: [WorkTransactionStatus.Matched, WorkTransactionStatus.NotMatched, WorkTransactionStatus.InReview]);
        await AddSubmissionAsync(s, s.SubmitterA, "WRK-WAIT", transactionStatuses: [WorkTransactionStatus.Matched, WorkTransactionStatus.InReview]);
        var service = CreateService(context, Decide("Dept"));

        var all = await service.GetQueueAsync(s.VerifierAccountId, new VerificationQueueFilterDto());

        var byCode = all.Items.ToDictionary(i => i.WorkCode);
        Assert.Equal("NONE", byCode["WRK-NONE"].MatchStatus);
        Assert.Equal("MATCHED", byCode["WRK-MATCHED"].MatchStatus);
        Assert.Equal("NOT_MATCHED", byCode["WRK-MISS"].MatchStatus);
        Assert.Equal("IN_REVIEW", byCode["WRK-WAIT"].MatchStatus);
        Assert.Equal(3, byCode["WRK-MISS"].TransactionCount);
        Assert.Equal(1, byCode["WRK-MISS"].MatchedTransactionCount);

        foreach (var (filter, expected) in new[]
        {
            ("NONE", "WRK-NONE"),
            ("matched", "WRK-MATCHED"),
            ("NOT_MATCHED", "WRK-MISS"),
            ("IN_REVIEW", "WRK-WAIT"),
        })
        {
            var filtered = await service.GetQueueAsync(s.VerifierAccountId, new VerificationQueueFilterDto { MatchStatus = filter });
            var only = Assert.Single(filtered.Items);
            Assert.Equal(expected, only.WorkCode);
            Assert.Equal(1, filtered.TotalCount);
        }
    }

    [Fact]
    public async Task GetQueue_StatusFilter_NarrowsToOneStatusAndRejectsOthers()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        await AddSubmissionAsync(s, s.SubmitterA, "WRK-SUB");
        await AddSubmissionAsync(s, s.SubmitterA, "WRK-REV", SubmissionStatus.UnderReview);
        var service = CreateService(context, Decide("Dept"));

        var reviewing = await service.GetQueueAsync(s.VerifierAccountId, new VerificationQueueFilterDto { Status = "under_review" });
        Assert.Equal("WRK-REV", Assert.Single(reviewing.Items).WorkCode);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.GetQueueAsync(s.VerifierAccountId, new VerificationQueueFilterDto { Status = "APPROVED" }));
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.GetQueueAsync(s.VerifierAccountId, new VerificationQueueFilterDto { Status = "BOGUS" }));
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.GetQueueAsync(s.VerifierAccountId, new VerificationQueueFilterDto { MatchStatus = "BOGUS" }));
    }

    [Fact]
    public async Task GetQueue_DepartmentFilter_NarrowsWithinScopeAndRejectsOutsideOrUnknown()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        await AddSubmissionAsync(s, s.SubmitterA, "WRK-A");
        await AddSubmissionAsync(s, s.SubmitterB, "WRK-B");

        var branchService = CreateService(context, Decide("Branch"));
        var onlyB = await branchService.GetQueueAsync(s.VerifierAccountId, new VerificationQueueFilterDto { DepartmentId = s.DeptB.Id });
        Assert.Equal("WRK-B", Assert.Single(onlyB.Items).WorkCode);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            branchService.GetQueueAsync(s.VerifierAccountId, new VerificationQueueFilterDto { DepartmentId = Guid.NewGuid() }));

        var deptService = CreateService(context, Decide("Dept"));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            deptService.GetQueueAsync(s.VerifierAccountId, new VerificationQueueFilterDto { DepartmentId = s.DeptB.Id }));
    }

    [Fact]
    public async Task GetQueue_Paging_ReturnsRequestedPageAndClampsInvalidValues()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        for (var i = 0; i < 5; i++)
        {
            await AddSubmissionAsync(s, s.SubmitterA, $"WRK-{i}", submittedAfter: TimeSpan.FromMinutes(i));
        }

        var service = CreateService(context, Decide("Dept"));

        var second = await service.GetQueueAsync(s.VerifierAccountId, new VerificationQueueFilterDto { Page = 2, PageSize = 2 });
        Assert.Equal(5, second.TotalCount);
        Assert.Equal(["WRK-2", "WRK-3"], second.Items.Select(i => i.WorkCode));

        var clamped = await service.GetQueueAsync(s.VerifierAccountId, new VerificationQueueFilterDto { Page = 0, PageSize = 1000 });
        Assert.Equal(1, clamped.Page);
        Assert.Equal(100, clamped.PageSize);
        Assert.Equal(5, clamped.Items.Count);
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(4)]
    public async Task GetQueue_PagePastTheEnd_ReturnsEmptyItemsWithTotalCount(int page)
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        await AddSubmissionAsync(s, s.SubmitterA, "WRK-1");
        await AddSubmissionAsync(s, s.SubmitterA, "WRK-2");

        var result = await CreateService(context, Decide("Dept"))
            .GetQueueAsync(s.VerifierAccountId, new VerificationQueueFilterDto { Page = page, PageSize = 1 });

        Assert.Empty(result.Items);
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(page, result.Page);
    }

    [Fact]
    public async Task GetQueue_ExcludesWithdrawnAndCoversSubDepartmentsOfDeptScope()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var team = NewDepartment("A-T1", DepartmentLevelType.Team, s.DeptA.Id);
        var position = await context.JobPositions.FirstAsync();
        var member = NewEmployee("EMP-T1", "Thành viên tổ", team.Id, position.Id);
        context.Departments.Add(team);
        context.Employees.Add(member);
        await context.SaveChangesAsync();
        await AddSubmissionAsync(s, member, "WRK-TEAM");
        await AddSubmissionAsync(s, s.SubmitterA, "WRK-WITHDRAWN", SubmissionStatus.Withdrawn);

        var result = await CreateService(context, Decide("Dept")).GetQueueAsync(s.VerifierAccountId, new VerificationQueueFilterDto());

        Assert.Equal("WRK-TEAM", Assert.Single(result.Items).WorkCode);
    }

    [Fact]
    public async Task GetQueue_LockedVerifierAccount_ThrowsUnauthorized()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var account = await context.Accounts.SingleAsync();
        account.IsLocked = true;
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            CreateService(context, Decide("All")).GetQueueAsync(s.VerifierAccountId, new VerificationQueueFilterDto()));
    }
}
