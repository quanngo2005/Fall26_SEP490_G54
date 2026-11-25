using ClosedXML.Excel;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Evaluation;
using G54.BLL.Dtos.Permission;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace G54.UnitTests;

/// <summary>
/// Scope checks of EvaluationService. Phòng A (manager + staff) and Phòng B (staff) are siblings under one branch.
/// Gated calls that are allowed fail later on the missing evaluation period, which proves the scope gate passed.
/// </summary>
public sealed class EvaluationScopeTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static EvaluationService CreateService(AppDbContext context, params UserPermissionDto[] grants)
    {
        DowngradeTestData.Seed(context);
        return new(
            context,
            NullLogger<EvaluationService>.Instance,
            new FakePermissionService(null, grants),
            new PerformanceScoreCalculator(context, new PerformanceScoreFormula()),
            new RecordingNotifier());
    }

    private static UserPermissionDto Grant(string action, string scope) => new("evaluation", action, scope, "EVALUATION_" + action.ToUpperInvariant());

    private sealed record Scenario(
        Guid DeptAId,
        Guid DeptBId,
        Employee ManagerA,
        Employee StaffA,
        Employee StaffB,
        Account ManagerAAccount,
        Account StaffAAccount);

    private static async Task<Scenario> SeedAsync(AppDbContext context)
    {
        var now = DateTimeOffset.UtcNow;

        Department Dept(string code, DepartmentLevelType level, Guid? parentId) => new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = $"Đơn vị {code}",
            LevelType = level,
            ParentDepartmentId = parentId,
            Status = RecordStatus.Active,
            LastSyncAt = now,
            UpdatedAt = now,
        };

        var branch = Dept("CN", DepartmentLevelType.Branch, null);
        var deptA = Dept("A", DepartmentLevelType.Department, branch.Id);
        var deptB = Dept("B", DepartmentLevelType.Department, branch.Id);
        context.Departments.AddRange(branch, deptA, deptB);

        var position = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = "P-SCOPE",
            Title = "Chuyên viên",
            Status = RecordStatus.Active,
            UpdatedAt = now,
        };
        context.JobPositions.Add(position);

        Employee Emp(string code, Guid departmentId) => new()
        {
            Id = Guid.NewGuid(),
            EmployeeCode = code,
            FullName = $"Nhân viên {code}",
            Email = $"{code.ToLowerInvariant()}@bpms.com",
            DepartmentId = departmentId,
            JobPositionId = position.Id,
            Status = EmployeeStatus.Active,
            LastSyncAt = now,
            UpdatedAt = now,
        };

        var managerA = Emp("MGR-A", deptA.Id);
        var staffA = Emp("STF-A", deptA.Id);
        var staffB = Emp("STF-B", deptB.Id);
        context.Employees.AddRange(managerA, staffA, staffB);

        Account Acc(Employee e) => new()
        {
            Id = Guid.NewGuid(),
            EmployeeId = e.Id,
            Username = e.Email,
            PasswordHash = "hash",
            LastSyncAt = now,
            UpdatedAt = now,
        };
        var managerAAccount = Acc(managerA);
        var staffAAccount = Acc(staffA);
        context.Accounts.AddRange(managerAAccount, staffAAccount, Acc(staffB));

        await context.SaveChangesAsync();
        return new Scenario(deptA.Id, deptB.Id, managerA, staffA, staffB, managerAAccount, staffAAccount);
    }

    [Fact]
    public async Task GetEvaluationForm_EmployeeReadingColleague_ThrowsForbidden()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var service = CreateService(context, Grant("read", PermissionScopes.Own));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetEvaluationFormAsync(s.StaffB.Id, Guid.NewGuid(), s.StaffAAccount.Id));
    }

    [Fact]
    public async Task GetEvaluationForm_EmployeeReadingOwnForm_PassesScopeGate()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var service = CreateService(context, Grant("read", PermissionScopes.Own));

        // The period is missing on purpose: reaching the period lookup means the scope gate allowed the read.
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.GetEvaluationFormAsync(s.StaffA.Id, Guid.NewGuid(), s.StaffAAccount.Id));
    }

    [Fact]
    public async Task GetEvaluationForm_DeptManagerReadingStaffOutsideDepartment_ThrowsForbidden()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var service = CreateService(context, Grant("read", PermissionScopes.Dept));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetEvaluationFormAsync(s.StaffB.Id, Guid.NewGuid(), s.ManagerAAccount.Id));
    }

    [Fact]
    public async Task GetEvaluationForm_DeptManagerReadingStaffInDepartment_PassesScopeGate()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var service = CreateService(context, Grant("read", PermissionScopes.Dept));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.GetEvaluationFormAsync(s.StaffA.Id, Guid.NewGuid(), s.ManagerAAccount.Id));
    }

    [Fact]
    public async Task SubmitEvaluation_DeptManagerForStaffOutsideDepartment_ThrowsForbidden()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var service = CreateService(context, Grant("submit", PermissionScopes.Dept));

        var request = new SubmitEvaluationRequestDto(s.StaffB.Id, Guid.NewGuid(), Comment: "Nhận xét");

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.SubmitEvaluationAsync(request, s.ManagerAAccount.Id));
    }

    [Fact]
    public async Task SubmitEvaluation_CallerWithoutSubmitPermission_ThrowsForbidden()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var service = CreateService(context, Grant("read", PermissionScopes.Dept));

        var request = new SubmitEvaluationRequestDto(s.StaffA.Id, Guid.NewGuid(), Comment: "Nhận xét");

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.SubmitEvaluationAsync(request, s.ManagerAAccount.Id));
    }

    [Fact]
    public async Task GetDepartmentEmployees_DeptManagerRequestingOtherDepartment_ThrowsForbidden()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var service = CreateService(context, Grant("read", PermissionScopes.Dept));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetDepartmentEmployeesForEvaluationAsync(Guid.NewGuid(), s.DeptBId, s.ManagerAAccount.Id));
    }

    [Fact]
    public async Task GetDepartmentEmployees_DeptManagerOwnDepartment_Succeeds()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var service = CreateService(context, Grant("read", PermissionScopes.Dept));
        var period = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = "2026-M11",
            Name = "Tháng 11/2026",
            PeriodType = PeriodType.Month,
            StartDate = new DateOnly(2026, 11, 1),
            EndDate = new DateOnly(2026, 11, 30),
            Status = PeriodStatus.Open,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        context.EvaluationPeriods.Add(period);
        await context.SaveChangesAsync();

        var rows = await service.GetDepartmentEmployeesForEvaluationAsync(period.Id, null, s.ManagerAAccount.Id);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal(s.DeptAId, row.DepartmentId));
    }

    [Fact]
    public async Task GetDepartmentEmployees_UnknownPeriod_ThrowsNotFound()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var service = CreateService(context, Grant("read", PermissionScopes.Dept));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.GetDepartmentEmployeesForEvaluationAsync(Guid.NewGuid(), null, s.ManagerAAccount.Id));
    }

    [Fact]
    public async Task GetDepartmentEmployees_EmployeeWithOwnScope_ThrowsForbidden()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var service = CreateService(context, Grant("read", PermissionScopes.Own));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetDepartmentEmployeesForEvaluationAsync(Guid.NewGuid(), null, s.StaffAAccount.Id));
    }

    [Fact]
    public async Task SubmitDepartmentCalibration_DeptManagerForOtherDepartment_ThrowsForbidden()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var service = CreateService(context, Grant("calibrate", PermissionScopes.Dept));

        var request = new SubmitCalibrationRequestDto(s.DeptBId, Guid.NewGuid(), [], IsDraft: true);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.SubmitDepartmentCalibrationAsync(request, s.ManagerAAccount.Id));
    }

    [Fact]
    public async Task RejectDepartmentCalibration_DeptManagerForOtherDepartment_ThrowsForbidden()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var service = CreateService(context, Grant("finalize", PermissionScopes.Dept));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.RejectDepartmentCalibrationAsync(s.DeptBId, new RejectCalibrationRequestDto(Guid.NewGuid(), "Cần hiệu chuẩn lại"), s.ManagerAAccount.Id));
    }

    [Fact]
    public async Task GetDepartmentCalibration_DeptManagerForOtherDepartment_ThrowsForbidden()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var service = CreateService(context, Grant("read", PermissionScopes.Dept));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetDepartmentCalibrationAsync(Guid.NewGuid(), s.DeptBId, null, s.ManagerAAccount.Id));
    }

    [Fact]
    public async Task GetDepartmentEvaluationDashboard_BranchManagerOrAdmin_ThrowsForbidden()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var branchService = CreateService(context, Grant("read", PermissionScopes.Branch));
        var adminService = CreateService(context, new UserPermissionDto("*", "*", PermissionScopes.All, "FULL_ACCESS"));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            branchService.GetDepartmentEvaluationDashboardAsync(null, null, s.ManagerAAccount.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            adminService.GetDepartmentEvaluationDashboardAsync(null, null, s.ManagerAAccount.Id));
    }

    [Fact]
    public async Task GetDepartmentEvaluationDashboard_EmployeeWithOwnScope_ThrowsForbidden()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var service = CreateService(context, Grant("read", PermissionScopes.Own));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetDepartmentEvaluationDashboardAsync(null, null, s.StaffAAccount.Id));
    }

    [Fact]
    public async Task GetDepartmentEvaluationDashboard_DeptManager_SucceedsForOwnDepartment()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var service = CreateService(context, Grant("read", PermissionScopes.Dept), Grant("submit", PermissionScopes.Dept));

        var result = await service.GetDepartmentEvaluationDashboardAsync(null, null, s.ManagerAAccount.Id);

        Assert.Equal(s.DeptAId, result.Department.Id);
        Assert.Equal("A", result.Department.Code);
        Assert.Equal(5, result.TierDistribution.Tiers.Count);
        Assert.Equal(20m, result.TierDistribution.TierACapPercent);
    }

    [Fact]
    public async Task GetDepartmentEvaluationDashboard_CalculatesSlaOnlyForWorksWithPlannedEndDate()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var period = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = "2026-M11",
            Name = "Tháng 11/2026",
            PeriodType = PeriodType.Month,
            StartDate = new DateOnly(2026, 11, 1),
            EndDate = new DateOnly(2026, 11, 30),
            Status = PeriodStatus.Open,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        context.EvaluationPeriods.Add(period);

        // Work 1: Completed on time (PlannedEndDate = Nov 15, UpdatedAt = Nov 14 UTC+7)
        // Work 2: Completed late (PlannedEndDate = Nov 10, UpdatedAt = Nov 14 UTC+7)
        // Work 3: Completed without PlannedEndDate -> excluded from SLA
        // Work 4: Open and Overdue with PlannedEndDate -> counted in Overdue
        var nov14Utc = new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.FromHours(7)).ToUniversalTime();
        context.Works.AddRange(
            new Work
            {
                Id = Guid.NewGuid(),
                Code = "W-ONTIME",
                Title = "On-time work",
                AssigneeEmployeeId = s.StaffA.Id,
                CreatedBy = s.ManagerA.Id,
                Status = WorkStatus.Done,
                Priority = WorkPriority.Medium,
                PlannedEndDate = new DateOnly(2026, 11, 15),
                AssignedAt = nov14Utc,
                UpdatedAt = nov14Utc,
            },
            new Work
            {
                Id = Guid.NewGuid(),
                Code = "W-LATE",
                Title = "Late completed work",
                AssigneeEmployeeId = s.StaffA.Id,
                CreatedBy = s.ManagerA.Id,
                Status = WorkStatus.Verified,
                Priority = WorkPriority.Medium,
                PlannedEndDate = new DateOnly(2026, 11, 10),
                AssignedAt = nov14Utc,
                UpdatedAt = nov14Utc,
            },
            new Work
            {
                Id = Guid.NewGuid(),
                Code = "W-NO-DEADLINE",
                Title = "No deadline work",
                AssigneeEmployeeId = s.StaffA.Id,
                CreatedBy = s.ManagerA.Id,
                Status = WorkStatus.Done,
                Priority = WorkPriority.Medium,
                PlannedEndDate = null,
                AssignedAt = nov14Utc,
                UpdatedAt = nov14Utc,
            },
            new Work
            {
                Id = Guid.NewGuid(),
                Code = "W-OPEN-OVERDUE",
                Title = "Open overdue work",
                AssigneeEmployeeId = s.StaffA.Id,
                CreatedBy = s.ManagerA.Id,
                Status = WorkStatus.Overdue,
                Priority = WorkPriority.High,
                PlannedEndDate = new DateOnly(2026, 11, 5),
                AssignedAt = nov14Utc,
                UpdatedAt = nov14Utc,
            });
        await context.SaveChangesAsync();

        var service = CreateService(context, Grant("read", PermissionScopes.Dept), Grant("submit", PermissionScopes.Dept));
        var dashboard = await service.GetDepartmentEvaluationDashboardAsync(period.Id, null, s.ManagerAAccount.Id);

        Assert.Equal(1, dashboard.Operations.OnTimeCompletedCount);
        Assert.Equal(2, dashboard.Operations.OverdueCount);
        Assert.Equal(33.33m, dashboard.Operations.OnTimeRatePercent);

        var export = await service.ExportDepartmentEvaluationDashboardAsync(period.Id, null, s.ManagerAAccount.Id);
        Assert.StartsWith("danh-gia-phong-", export.FileName);
        Assert.NotEmpty(export.Content);
        Assert.DoesNotContain(dashboard.TierDistribution.ActionAlerts, a => a.Kind == "TOP");

        using var stream = new MemoryStream(export.Content);
        using var workbook = new XLWorkbook(stream);
        Assert.Equal(["Tong quan", "KPI", "Xu huong", "Xep loai", "Cong viec & SLA", "Can bo"], workbook.Worksheets.Select(w => w.Name).ToArray());
        Assert.Equal("33.33", workbook.Worksheet("Tong quan").Cell(17, 2).GetValue<string>());
        Assert.Equal("Hoàn thành đúng hạn SLA (%)", workbook.Worksheet("Tong quan").Cell(17, 1).GetString());
    }

    [Fact]
    public async Task GetDepartmentEvaluationDashboard_AggregatesEmployeeTargetsWhenDepartmentTargetMissing()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var now = DateTimeOffset.UtcNow;

        var period = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = "2026-11",
            Name = "Tháng 11/2026",
            PeriodType = PeriodType.Month,
            StartDate = new DateOnly(2026, 11, 1),
            EndDate = new DateOnly(2026, 11, 30),
            Status = PeriodStatus.Open,
            UpdatedAt = now,
        };
        context.EvaluationPeriods.Add(period);

        var kpiSum = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            Code = "HDV",
            Name = "Huy động vốn",
            Unit = "Tỷ VND",
            Direction = KpiDirection.HigherBetter,
            AggregationType = KpiAggregationType.Sum,
            Status = KpiStatus.Active,
            UpdatedAt = now,
        };
        var kpiAvg = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            Code = "CSAT",
            Name = "Chỉ số hài lòng khách hàng",
            Unit = "%",
            Direction = KpiDirection.HigherBetter,
            AggregationType = KpiAggregationType.Avg,
            Status = KpiStatus.Active,
            UpdatedAt = now,
        };
        context.KpiDefinitions.AddRange(kpiSum, kpiAvg);

        var profile = new EvaluationProfile
        {
            Id = Guid.NewGuid(),
            Code = "EP-SCOPE",
            Name = "Khung đánh giá Chuyên viên",
            JobPositionId = s.StaffA.JobPositionId,
            Version = 1,
            Status = EvaluationProfileStatus.Active,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            UpdatedAt = now,
        };
        var groupBusiness = new EvaluationProfileItem
        {
            Id = Guid.NewGuid(),
            EvaluationProfileId = profile.Id,
            Name = "Kinh doanh",
            WeightPercent = 70m,
            DisplayOrder = 1,
            UpdatedAt = now,
        };
        var groupService = new EvaluationProfileItem
        {
            Id = Guid.NewGuid(),
            EvaluationProfileId = profile.Id,
            Name = "Chất lượng dịch vụ",
            WeightPercent = 20m,
            DisplayOrder = 2,
            UpdatedAt = now,
        };
        var leafSum = new EvaluationProfileItem
        {
            Id = Guid.NewGuid(),
            EvaluationProfileId = profile.Id,
            ParentItemId = groupBusiness.Id,
            KpiDefinitionId = kpiSum.Id,
            WeightPercent = 100m,
            DisplayOrder = 1,
            UpdatedAt = now,
        };
        var leafAvg = new EvaluationProfileItem
        {
            Id = Guid.NewGuid(),
            EvaluationProfileId = profile.Id,
            ParentItemId = groupService.Id,
            KpiDefinitionId = kpiAvg.Id,
            WeightPercent = 100m,
            DisplayOrder = 1,
            UpdatedAt = now,
        };
        context.EvaluationProfiles.Add(profile);
        context.EvaluationProfileItems.AddRange(groupBusiness, groupService, leafSum, leafAvg);

        var targetSumMgr = new TargetAssignment
        {
            Id = Guid.NewGuid(),
            KpiDefinitionId = kpiSum.Id,
            EmployeeId = s.ManagerA.Id,
            EvaluationPeriodId = period.Id,
            TargetValue = 15m,
            Version = 1,
            Status = TargetAssignmentStatus.Active,
            AssignedAt = now,
            EffectiveFrom = period.StartDate,
        };
        var targetSumStaff = new TargetAssignment
        {
            Id = Guid.NewGuid(),
            KpiDefinitionId = kpiSum.Id,
            EmployeeId = s.StaffA.Id,
            EvaluationPeriodId = period.Id,
            TargetValue = 12m,
            Version = 1,
            Status = TargetAssignmentStatus.Active,
            AssignedAt = now,
            EffectiveFrom = period.StartDate,
        };
        var targetAvgMgr = new TargetAssignment
        {
            Id = Guid.NewGuid(),
            KpiDefinitionId = kpiAvg.Id,
            EmployeeId = s.ManagerA.Id,
            EvaluationPeriodId = period.Id,
            TargetValue = 90m,
            Version = 1,
            Status = TargetAssignmentStatus.Active,
            AssignedAt = now,
            EffectiveFrom = period.StartDate,
        };
        var targetAvgStaff = new TargetAssignment
        {
            Id = Guid.NewGuid(),
            KpiDefinitionId = kpiAvg.Id,
            EmployeeId = s.StaffA.Id,
            EvaluationPeriodId = period.Id,
            TargetValue = 94m,
            Version = 1,
            Status = TargetAssignmentStatus.Active,
            AssignedAt = now,
            EffectiveFrom = period.StartDate,
        };
        context.TargetAssignments.AddRange(targetSumMgr, targetSumStaff, targetAvgMgr, targetAvgStaff);

        context.ActualProgresses.AddRange(
            new ActualProgress
            {
                Id = Guid.NewGuid(),
                TargetAssignmentId = targetSumMgr.Id,
                Value = 16m,
                ProgressDate = new DateOnly(2026, 11, 15),
                Status = ActualProgressStatus.Valid,
                UpdatedAt = now,
            },
            new ActualProgress
            {
                Id = Guid.NewGuid(),
                TargetAssignmentId = targetSumStaff.Id,
                Value = 11m,
                ProgressDate = new DateOnly(2026, 11, 20),
                Status = ActualProgressStatus.Valid,
                UpdatedAt = now,
            },
            new ActualProgress
            {
                Id = Guid.NewGuid(),
                TargetAssignmentId = targetAvgMgr.Id,
                Value = 92m,
                Denominator = 1m,
                ProgressDate = new DateOnly(2026, 11, 15),
                Status = ActualProgressStatus.Valid,
                UpdatedAt = now,
            },
            new ActualProgress
            {
                Id = Guid.NewGuid(),
                TargetAssignmentId = targetAvgStaff.Id,
                Value = 96m,
                Denominator = 1m,
                ProgressDate = new DateOnly(2026, 11, 20),
                Status = ActualProgressStatus.Valid,
                UpdatedAt = now,
            });
        await context.SaveChangesAsync();

        var service = CreateService(context, Grant("read", PermissionScopes.Dept), Grant("submit", PermissionScopes.Dept));
        var dashboard = await service.GetDepartmentEvaluationDashboardAsync(period.Id, null, s.ManagerAAccount.Id);

        Assert.Equal(2, dashboard.Performance.KpiGroups.Count);
        var businessKpi = Assert.Single(dashboard.Performance.KpiGroups.Single(g => g.Name == "Kinh doanh").Kpis);
        Assert.Equal("HDV", businessKpi.Code);
        Assert.Equal(27m, businessKpi.TargetValue);
        Assert.Equal(27m, businessKpi.ActualValue);
        Assert.Equal(100m, businessKpi.AchievementPercent);

        var serviceKpi = Assert.Single(dashboard.Performance.KpiGroups.Single(g => g.Name == "Chất lượng dịch vụ").Kpis);
        Assert.Equal("CSAT", serviceKpi.Code);
        Assert.Equal(92m, serviceKpi.TargetValue);
        Assert.Equal(94m, serviceKpi.ActualValue);
    }

    [Fact]
    public async Task GetDepartmentEvaluationDashboard_IncludesDisputedScoreInDepartmentAverage()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var now = DateTimeOffset.UtcNow;
        var period = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = "2026-M11",
            Name = "Tháng 11/2026",
            PeriodType = PeriodType.Month,
            StartDate = new DateOnly(2026, 11, 1),
            EndDate = new DateOnly(2026, 11, 30),
            Status = PeriodStatus.Open,
            UpdatedAt = now,
        };
        context.EvaluationPeriods.Add(period);
        context.PerformanceEvaluations.AddRange(
            new PerformanceEvaluation
            {
                Id = Guid.NewGuid(),
                EmployeeId = s.ManagerA.Id,
                EvaluationPeriodId = period.Id,
                EvaluationProfileId = Guid.NewGuid(),
                TotalScore = 70m,
                Status = PerformanceEvaluationStatus.Final,
                UpdatedAt = now,
            },
            new PerformanceEvaluation
            {
                Id = Guid.NewGuid(),
                EmployeeId = s.StaffA.Id,
                EvaluationPeriodId = period.Id,
                EvaluationProfileId = Guid.NewGuid(),
                TotalScore = 50m,
                Status = PerformanceEvaluationStatus.Disputed,
                UpdatedAt = now,
            });
        await context.SaveChangesAsync();

        var service = CreateService(context, Grant("read", PermissionScopes.Dept), Grant("submit", PermissionScopes.Dept));
        var dashboard = await service.GetDepartmentEvaluationDashboardAsync(period.Id, null, s.ManagerAAccount.Id);

        Assert.Equal(60m, dashboard.Performance.Average);
        Assert.Equal(2, dashboard.Performance.EvaluatedEmployeeCount);
        Assert.Equal(1, dashboard.Performance.AppealCount);
    }

    [Fact]
    public async Task GetDepartmentEvaluationDashboard_WithoutPeriodId_PrioritizesCurrentActiveMonthOverQuarterAndYear()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var yearPeriod = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = $"YEAR-{today.Year}",
            Name = $"Năm {today.Year}",
            PeriodType = PeriodType.Year,
            StartDate = new DateOnly(today.Year, 1, 1),
            EndDate = new DateOnly(today.Year, 12, 31),
            Status = PeriodStatus.Open,
        };

        var quarterPeriod = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = $"Q-{today.Year}",
            Name = $"Quý {today.Year}",
            PeriodType = PeriodType.Quarter,
            StartDate = today.AddDays(-15),
            EndDate = today.AddDays(45),
            Status = PeriodStatus.Open,
        };

        var monthPeriod = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = $"M-{today.Year}-{today.Month}",
            Name = $"Tháng {today.Month}/{today.Year}",
            PeriodType = PeriodType.Month,
            StartDate = new DateOnly(today.Year, today.Month, 1),
            EndDate = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month)),
            Status = PeriodStatus.Open,
        };

        context.EvaluationPeriods.AddRange(yearPeriod, quarterPeriod, monthPeriod);
        await context.SaveChangesAsync();

        var service = CreateService(context, Grant("read", PermissionScopes.Dept), Grant("submit", PermissionScopes.Dept));
        var dashboard = await service.GetDepartmentEvaluationDashboardAsync(null, null, s.ManagerAAccount.Id);

        Assert.NotNull(dashboard.Period);
        Assert.Equal(monthPeriod.Id, dashboard.Period.Id);
        Assert.Equal(PeriodType.Month.ToString().ToUpperInvariant(), dashboard.Period.PeriodType);
    }
}
