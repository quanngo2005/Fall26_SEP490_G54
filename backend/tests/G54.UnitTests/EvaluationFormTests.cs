using G54.BLL.Common.Constants;
using G54.BLL.Dtos.Permission;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace G54.UnitTests;

/// <summary>
/// The evaluation form (SCR-EVL-01-D1) and the roster of the evaluation list: the fields the form adds to the
/// calculation, the edit gate, the previous-period comparison and the department subtree the roster covers.
/// Branch -> Sales dept (manager) -> Team 1 (staff 1, staff 2) and Team 2 (staff 3); Support dept (staff 4) is a sibling.
/// </summary>
public sealed class EvaluationFormTests
{
    private sealed record Scenario(
        Department Sales,
        Department Team1,
        Department Team2,
        Department SupportDept,
        EvaluationPeriod Period,
        EvaluationProfile Profile,
        KpiGroup KpiGroup,
        KpiDefinition Kpi,
        Employee Manager,
        Employee Staff1,
        Employee Staff2,
        Employee Staff3,
        Employee SupportStaff);

    private static AppDbContext CreateContext()
    {
        var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        DowngradeTestData.Seed(context);
        return context;
    }

    private static EvaluationService CreateService(AppDbContext context, string readScope, string? submitScope)
    {
        var grants = new List<UserPermissionDto> { new("evaluation", "read", readScope, "EVALUATION_READ") };
        if (submitScope is not null)
        {
            grants.Add(new("evaluation", "submit", submitScope, "EVALUATION_SUBMIT"));
        }

        return new EvaluationService(
            context,
            NullLogger<EvaluationService>.Instance,
            new FakePermissionService(null, grants.ToArray()));
    }

    private static async Task<Scenario> SeedAsync(AppDbContext context)
    {
        var now = DateTimeOffset.UtcNow;

        Department Dept(string code, DepartmentLevelType level, Guid? parentId) => new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = $"Unit {code}",
            LevelType = level,
            ParentDepartmentId = parentId,
            Status = RecordStatus.Active,
            LastSyncAt = now,
            UpdatedAt = now,
        };

        var branch = Dept("BR", DepartmentLevelType.Branch, null);
        var sales = Dept("SALES", DepartmentLevelType.Department, branch.Id);
        var support = Dept("SUPPORT", DepartmentLevelType.Department, branch.Id);
        var team1 = Dept("T1", DepartmentLevelType.Team, sales.Id);
        var team2 = Dept("T2", DepartmentLevelType.Team, sales.Id);
        context.Departments.AddRange(branch, sales, support, team1, team2);

        var position = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = "P-FORM",
            Title = "Specialist",
            Status = RecordStatus.Active,
            LastSyncAt = now,
            UpdatedAt = now,
        };
        context.JobPositions.Add(position);

        var group = new KpiGroup { Id = Guid.NewGuid(), Code = "KD", Name = "Business", Status = KpiStatus.Active, UpdatedAt = now };
        var kpi = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            KpiGroupId = group.Id,
            Code = "HDV",
            Name = "Deposits",
            Unit = "bn",
            AggregationType = KpiAggregationType.Sum,
            Direction = KpiDirection.HigherBetter,
            Status = KpiStatus.Active,
            UpdatedAt = now,
        };
        var profile = new EvaluationProfile
        {
            Id = Guid.NewGuid(),
            JobPositionId = position.Id,
            Code = "EP-FORM",
            Name = "Form profile",
            Version = 1,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            Status = EvaluationProfileStatus.Active,
            UpdatedAt = now,
        };
        context.KpiGroups.Add(group);
        context.KpiDefinitions.Add(kpi);
        context.EvaluationProfiles.Add(profile);
        context.EvaluationProfileItems.Add(new EvaluationProfileItem
        {
            Id = Guid.NewGuid(),
            EvaluationProfileId = profile.Id,
            KpiGroupId = group.Id,
            WeightPercent = 100m,
            DisplayOrder = 1,
            UpdatedAt = now,
        });

        EvaluationPeriod Period(string code, DateOnly start, DateOnly end, PeriodStatus status) => new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = code,
            PeriodType = PeriodType.Month,
            StartDate = start,
            EndDate = end,
            Status = status,
            UpdatedAt = now,
        };

        var period = Period("2026-10", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), PeriodStatus.Open);
        context.EvaluationPeriods.AddRange(period, Period("2026-09", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), PeriodStatus.Closed));

        Employee Emp(string code, Department dept) => new()
        {
            Id = Guid.NewGuid(),
            EmployeeCode = code,
            FullName = $"Employee {code}",
            Email = $"{code.ToLowerInvariant()}@bpms.com",
            DepartmentId = dept.Id,
            JobPositionId = position.Id,
            Status = EmployeeStatus.Active,
            LastSyncAt = now,
            UpdatedAt = now,
        };

        var manager = Emp("MGR", sales);
        var staff1 = Emp("S1", team1);
        var staff2 = Emp("S2", team1);
        var staff3 = Emp("S3", team2);
        var supportStaff = Emp("S4", support);
        context.Employees.AddRange(manager, staff1, staff2, staff3, supportStaff);
        foreach (var employee in new[] { manager, staff1, staff2, staff3, supportStaff })
        {
            context.Accounts.Add(new Account
            {
                Id = employee.Id,
                EmployeeId = employee.Id,
                Username = employee.Email,
                PasswordHash = "hash",
                LastSyncAt = now,
                UpdatedAt = now,
            });
        }

        await context.SaveChangesAsync();
        return new Scenario(sales, team1, team2, support, period, profile, group, kpi, manager, staff1, staff2, staff3, supportStaff);
    }

    private static async Task<PerformanceEvaluation> AddEvaluationAsync(
        AppDbContext context,
        Scenario s,
        Employee employee,
        EvaluationPeriod period,
        decimal? score,
        PerformanceEvaluationStatus status = PerformanceEvaluationStatus.Calculated)
    {
        var evaluation = new PerformanceEvaluation
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            EvaluationPeriodId = period.Id,
            EvaluationProfileId = s.Profile.Id,
            TotalScore = score,
            Status = status,
            CalculatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        context.PerformanceEvaluations.Add(evaluation);
        await context.SaveChangesAsync();
        return evaluation;
    }

    private static async Task AssignTargetAsync(AppDbContext context, Scenario s, Employee employee, decimal target)
    {
        context.TargetAssignments.Add(new TargetAssignment
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            EvaluationPeriodId = s.Period.Id,
            KpiDefinitionId = s.Kpi.Id,
            TargetValue = target,
            Version = 1,
            Status = TargetAssignmentStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync();
    }

    // ----- the roster covers the department subtree ------------------------------------------------

    [Fact]
    public async Task Roster_DeptManager_CoversTheOwnDepartmentAndEveryDepartmentBeneathIt()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var service = CreateService(context, PermissionScopes.Dept, PermissionScopes.Dept);

        var roster = await service.GetDepartmentEmployeesForEvaluationAsync(s.Period.Id, null, s.Manager.Id);

        Assert.Equal(
            new[] { s.Manager.Id, s.Staff1.Id, s.Staff2.Id, s.Staff3.Id }.Order(),
            roster.Select(r => r.EmployeeId).Order());
        Assert.DoesNotContain(roster, r => r.EmployeeId == s.SupportStaff.Id);
    }

    [Fact]
    public async Task Roster_LeavesOutInactiveEmployees()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        s.Staff2.Status = EmployeeStatus.Inactive;
        await context.SaveChangesAsync();
        var service = CreateService(context, PermissionScopes.Dept, PermissionScopes.Dept);

        var roster = await service.GetDepartmentEmployeesForEvaluationAsync(s.Period.Id, null, s.Manager.Id);

        Assert.DoesNotContain(roster, r => r.EmployeeId == s.Staff2.Id);
        Assert.Contains(roster, r => r.EmployeeId == s.Staff1.Id);
    }

    [Fact]
    public async Task Roster_BranchScope_RequestedForASubDepartment_StaysInsideThatSubtree()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var service = CreateService(context, PermissionScopes.Branch, PermissionScopes.Branch);

        var roster = await service.GetDepartmentEmployeesForEvaluationAsync(s.Period.Id, s.Team1.Id, s.Manager.Id);

        Assert.Equal(new[] { s.Staff1.Id, s.Staff2.Id }.Order(), roster.Select(r => r.EmployeeId).Order());
    }

    [Fact]
    public async Task Roster_CanEvaluate_FollowsTheRowOwnDepartmentAndExcludesTheCaller()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var service = CreateService(context, PermissionScopes.Dept, PermissionScopes.Dept);

        var roster = await service.GetDepartmentEmployeesForEvaluationAsync(s.Period.Id, null, s.Manager.Id);

        Assert.False(roster.Single(r => r.EmployeeId == s.Manager.Id).CanEvaluate);
        Assert.All(
            roster.Where(r => r.EmployeeId != s.Manager.Id),
            row => Assert.True(row.CanEvaluate, $"{row.EmployeeCode} lies beneath the caller's department"));
    }

    [Fact]
    public async Task Roster_CallerWithoutSubmitPermission_CanEvaluateNobody()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var service = CreateService(context, PermissionScopes.Dept, submitScope: null);

        var roster = await service.GetDepartmentEmployeesForEvaluationAsync(s.Period.Id, null, s.Manager.Id);

        Assert.NotEmpty(roster);
        Assert.All(roster, row => Assert.False(row.CanEvaluate));
    }

    [Fact]
    public async Task Roster_ClosedPeriod_CanEvaluateNobody()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        s.Period.Status = PeriodStatus.Closed;
        await context.SaveChangesAsync();
        var service = CreateService(context, PermissionScopes.Dept, PermissionScopes.Dept);

        var roster = await service.GetDepartmentEmployeesForEvaluationAsync(s.Period.Id, null, s.Manager.Id);

        Assert.All(roster, row => Assert.False(row.CanEvaluate));
    }

    // ----- the edit gate ----------------------------------------------------------------------------

    [Fact]
    public async Task Form_CanEdit_ManagerWithSubmitScopeOverTheEmployeeDepartment()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var service = CreateService(context, PermissionScopes.Dept, PermissionScopes.Dept);

        var form = await service.GetEvaluationFormAsync(s.Staff1.Id, s.Period.Id, s.Manager.Id);

        Assert.True(form.CanEdit);
    }

    [Fact]
    public async Task Form_CanEdit_IsFalseForAReaderWithoutSubmitPermission()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var service = CreateService(context, PermissionScopes.All, submitScope: null);

        var form = await service.GetEvaluationFormAsync(s.Staff1.Id, s.Period.Id, s.Manager.Id);

        Assert.False(form.CanEdit);
    }

    [Fact]
    public async Task Form_CanEdit_IsFalseWhenTheReaderIsTheEvaluatedEmployee()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var service = CreateService(context, PermissionScopes.Dept, PermissionScopes.Dept);

        var form = await service.GetEvaluationFormAsync(s.Manager.Id, s.Period.Id, s.Manager.Id);

        Assert.False(form.CanEdit);
    }

    [Fact]
    public async Task Form_CanEdit_IsFalseOnceTheEvaluationIsInReview()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        await AddEvaluationAsync(context, s, s.Staff1, s.Period, 80m, PerformanceEvaluationStatus.InReview);
        var service = CreateService(context, PermissionScopes.Dept, PermissionScopes.Dept);

        var form = await service.GetEvaluationFormAsync(s.Staff1.Id, s.Period.Id, s.Manager.Id);

        Assert.False(form.CanEdit);
    }

    [Fact]
    public async Task Form_CanEdit_IsFalseWhenTheScoreIsDisputed_AndReportsTheDispute()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        await AddEvaluationAsync(context, s, s.Staff1, s.Period, 80m, PerformanceEvaluationStatus.Disputed);
        var service = CreateService(context, PermissionScopes.Dept, PermissionScopes.Dept);

        var form = await service.GetEvaluationFormAsync(s.Staff1.Id, s.Period.Id, s.Manager.Id);

        Assert.False(form.CanEdit);
        Assert.True(form.HasOpenDispute);
    }

    [Fact]
    public async Task Form_CanEdit_IsFalseOnAClosedPeriod()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        s.Period.Status = PeriodStatus.Closed;
        await context.SaveChangesAsync();
        var service = CreateService(context, PermissionScopes.Dept, PermissionScopes.Dept);

        var form = await service.GetEvaluationFormAsync(s.Staff1.Id, s.Period.Id, s.Manager.Id);

        Assert.False(form.CanEdit);
    }

    // ----- counts and the dispute flag ----------------------------------------------------------------

    [Fact]
    public async Task Form_OpenAppealCount_CountsOnlyOpenAndUnderReviewAppeals()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var evaluation = await AddEvaluationAsync(context, s, s.Staff1, s.Period, 80m);
        foreach (var status in new[] { AppealStatus.Open, AppealStatus.UnderReview, AppealStatus.Withdrawn })
        {
            context.Appeals.Add(new Appeal
            {
                Id = Guid.NewGuid(),
                EmployeeId = s.Staff1.Id,
                PerformanceEvaluationId = evaluation.Id,
                Reason = "Disagree",
                Status = status,
                FiledAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }

        await context.SaveChangesAsync();
        var service = CreateService(context, PermissionScopes.Dept, PermissionScopes.Dept);

        var form = await service.GetEvaluationFormAsync(s.Staff1.Id, s.Period.Id, s.Manager.Id);

        Assert.Equal(2, form.OpenAppealCount);
        Assert.True(form.HasOpenDispute);
        Assert.False(form.CanEdit);
    }

    [Fact]
    public async Task Form_PendingSubmissionCount_CountsSubmittedAndUnderReviewInsideThePeriodOnly()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var inside = new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
        var outside = new DateTimeOffset(2026, 9, 12, 9, 0, 0, TimeSpan.Zero);
        var cases = new (SubmissionStatus Status, DateTimeOffset At)[]
        {
            (SubmissionStatus.Submitted, inside),
            (SubmissionStatus.UnderReview, inside),
            (SubmissionStatus.Approved, inside),
            (SubmissionStatus.Submitted, outside),
        };

        var number = 0;
        foreach (var (status, at) in cases)
        {
            var work = new Work
            {
                Id = Guid.NewGuid(),
                Code = $"W-{++number}",
                Title = "Work",
                AssigneeEmployeeId = s.Staff1.Id,
                CreatedBy = s.Manager.Id,
                Status = WorkStatus.Submitted,
                Priority = WorkPriority.Medium,
                UpdatedAt = at,
            };
            context.Works.Add(work);
            context.ResultSubmissions.Add(new ResultSubmission
            {
                Id = Guid.NewGuid(),
                WorkId = work.Id,
                SubmitterEmployeeId = s.Staff1.Id,
                SubmissionNo = 1,
                Status = status,
                SubmittedAt = at,
                UpdatedAt = at,
            });
        }

        await context.SaveChangesAsync();
        var service = CreateService(context, PermissionScopes.Dept, PermissionScopes.Dept);

        var form = await service.GetEvaluationFormAsync(s.Staff1.Id, s.Period.Id, s.Manager.Id);

        Assert.Equal(2, form.PendingSubmissionCount);
    }

    // ----- the previous period ------------------------------------------------------------------------

    [Fact]
    public async Task Form_PreviousPeriod_IsNullWhenTheEmployeeHasNoEarlierScoredEvaluation()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var service = CreateService(context, PermissionScopes.Dept, PermissionScopes.Dept);

        var form = await service.GetEvaluationFormAsync(s.Staff1.Id, s.Period.Id, s.Manager.Id);

        Assert.Null(form.PreviousPeriod);
    }

    [Fact]
    public async Task Form_PreviousPeriod_CarriesTheEarlierScoreTierAndTheDifference()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var earlier = context.EvaluationPeriods.Single(p => p.Code == "2026-09");
        const decimal earlierScore = 70m;
        await AddEvaluationAsync(context, s, s.Staff1, earlier, earlierScore, PerformanceEvaluationStatus.Final);
        var service = CreateService(context, PermissionScopes.Dept, PermissionScopes.Dept);

        var form = await service.GetEvaluationFormAsync(s.Staff1.Id, s.Period.Id, s.Manager.Id);

        Assert.NotNull(form.PreviousPeriod);
        Assert.Equal("2026-09", form.PreviousPeriod!.PeriodCode);
        Assert.Equal(earlierScore, form.PreviousPeriod.TotalScore);
        Assert.Equal(form.Summary.TotalScore - earlierScore, form.PreviousPeriod.ScoreDifference);
        Assert.False(string.IsNullOrWhiteSpace(form.PreviousPeriod.Tier));
    }

    [Fact]
    public async Task Form_PreviousPeriod_IgnoresAnEarlierEvaluationWithoutAScore()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var earlier = context.EvaluationPeriods.Single(p => p.Code == "2026-09");
        await AddEvaluationAsync(context, s, s.Staff1, earlier, score: null, PerformanceEvaluationStatus.Draft);
        var service = CreateService(context, PermissionScopes.Dept, PermissionScopes.Dept);

        var form = await service.GetEvaluationFormAsync(s.Staff1.Id, s.Period.Id, s.Manager.Id);

        Assert.Null(form.PreviousPeriod);
    }

    // ----- the calculation ----------------------------------------------------------------------------

    [Fact]
    public async Task Form_Kpi_WithoutATarget_HasNoTargetValueInsteadOfZero()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var service = CreateService(context, PermissionScopes.Dept, PermissionScopes.Dept);

        var form = await service.GetEvaluationFormAsync(s.Staff1.Id, s.Period.Id, s.Manager.Id);

        Assert.Null(form.CriteriaGroups.Single().KpiItems.Single().TargetValue);
    }

    [Fact]
    public async Task Form_Kpi_WithATarget_ReturnsThatTarget()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        const decimal target = 225m;
        await AssignTargetAsync(context, s, s.Staff1, target);
        var service = CreateService(context, PermissionScopes.Dept, PermissionScopes.Dept);

        var form = await service.GetEvaluationFormAsync(s.Staff1.Id, s.Period.Id, s.Manager.Id);

        Assert.Equal(target, form.CriteriaGroups.Single().KpiItems.Single().TargetValue);
    }

    [Fact]
    public async Task Form_Summary_WeightedScoreIsTheTotalBeforeThePenalty()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        const decimal target = 10m;
        await AssignTargetAsync(context, s, s.Staff1, target);
        var progressTarget = context.TargetAssignments.Single();
        context.ActualProgresses.Add(new ActualProgress
        {
            Id = Guid.NewGuid(),
            TargetAssignmentId = progressTarget.Id,
            Value = target / 2,
            ProgressDate = new DateOnly(2026, 10, 15),
            Status = ActualProgressStatus.Valid,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync();
        var service = CreateService(context, PermissionScopes.Dept, PermissionScopes.Dept);

        var form = await service.GetEvaluationFormAsync(s.Staff1.Id, s.Period.Id, s.Manager.Id);

        var group = form.CriteriaGroups.Single();
        Assert.Equal(group.GroupScore * group.WeightPercent / 100m, form.Summary.WeightedScore);
        Assert.Equal(form.Summary.WeightedScore - form.Summary.TotalRiskPenalty, form.Summary.TotalScore);
    }

    [Fact]
    public async Task Form_Group_WithoutActiveKpis_IsMarkedQualitative()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        s.Kpi.Status = KpiStatus.Inactive;
        await context.SaveChangesAsync();
        var service = CreateService(context, PermissionScopes.Dept, PermissionScopes.Dept);

        var form = await service.GetEvaluationFormAsync(s.Staff1.Id, s.Period.Id, s.Manager.Id);

        Assert.True(form.CriteriaGroups.Single().IsQualitative);
    }

    [Fact]
    public async Task Form_Group_WithActiveKpis_IsNotQualitative()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var service = CreateService(context, PermissionScopes.Dept, PermissionScopes.Dept);

        var form = await service.GetEvaluationFormAsync(s.Staff1.Id, s.Period.Id, s.Manager.Id);

        Assert.False(form.CriteriaGroups.Single().IsQualitative);
    }
}
