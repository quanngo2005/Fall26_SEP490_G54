using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Evaluation;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace G54.UnitTests;

public sealed class EvaluationServiceTests
{
    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static (AppDbContext Context, EvaluationService Service) CreateTestEnvironment()
    {
        var context = CreateInMemoryDbContext();
        var service = new EvaluationService(context, NullLogger<EvaluationService>.Instance);
        return (context, service);
    }

    private static async Task<(Department Dept, JobPosition Position, EvaluationPeriod Period, Employee Manager, Employee Staff)> SeedBaseScenarioAsync(AppDbContext context)
    {
        var now = DateTimeOffset.UtcNow;

        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Code = "D-SALES",
            Name = "Phòng Khách hàng Cá nhân",
            LevelType = DepartmentLevelType.Department,
            Status = RecordStatus.Active,
            LastSyncAt = now,
            UpdatedAt = now,
        };
        await context.Departments.AddAsync(dept);

        var kpiGroupKd = new KpiGroup
        {
            Id = Guid.NewGuid(),
            Code = "KD",
            Name = "Kinh doanh",
            UpdatedAt = now,
            Status = KpiStatus.Active,
        };
        var kpiGroupCldv = new KpiGroup
        {
            Id = Guid.NewGuid(),
            Code = "CLDV",
            Name = "Chất lượng dịch vụ",
            UpdatedAt = now,
            Status = KpiStatus.Active,
        };
        var kpiGroupTt = new KpiGroup
        {
            Id = Guid.NewGuid(),
            Code = "TT",
            Name = "Vận hành & Tuân thủ",
            UpdatedAt = now,
            Status = KpiStatus.Active,
        };
        await context.KpiGroups.AddRangeAsync(kpiGroupKd, kpiGroupCldv, kpiGroupTt);

        var kpiHdv = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            KpiGroupId = kpiGroupKd.Id,
            Code = "HDV",
            Name = "Huy động vốn",
            Unit = "tỷ VND",
            AggregationType = KpiAggregationType.Sum,
            Direction = KpiDirection.HigherBetter,
            Status = KpiStatus.Active,
            UpdatedAt = now,
        };
        var kpiCsat = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            KpiGroupId = kpiGroupCldv.Id,
            Code = "CSAT",
            Name = "Điểm hài lòng CSAT",
            Unit = "điểm",
            AggregationType = KpiAggregationType.Avg,
            Direction = KpiDirection.HigherBetter,
            Status = KpiStatus.Active,
            UpdatedAt = now,
        };
        await context.KpiDefinitions.AddRangeAsync(kpiHdv, kpiCsat);

        var profile = new EvaluationProfile
        {
            Id = Guid.NewGuid(),
            Code = "EP-TELLER",
            Name = "Hồ sơ đánh giá Giao dịch viên",
            Version = 1,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            Status = EvaluationProfileStatus.Active,
            UpdatedAt = now,
        };
        await context.EvaluationProfiles.AddAsync(profile);

        var itemKd = new EvaluationProfileItem
        {
            Id = Guid.NewGuid(),
            EvaluationProfileId = profile.Id,
            KpiGroupId = kpiGroupKd.Id,
            WeightPercent = 40m,
            DisplayOrder = 1,
            UpdatedAt = now,
        };
        var itemCldv = new EvaluationProfileItem
        {
            Id = Guid.NewGuid(),
            EvaluationProfileId = profile.Id,
            KpiGroupId = kpiGroupCldv.Id,
            WeightPercent = 30m,
            DisplayOrder = 2,
            UpdatedAt = now,
        };
        var itemTt = new EvaluationProfileItem
        {
            Id = Guid.NewGuid(),
            EvaluationProfileId = profile.Id,
            KpiGroupId = kpiGroupTt.Id,
            WeightPercent = 30m,
            DisplayOrder = 3,
            UpdatedAt = now,
        };
        await context.EvaluationProfileItems.AddRangeAsync(itemKd, itemCldv, itemTt);

        var position = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = "TELLER",
            Title = "Giao dịch viên",
            EvaluationProfileId = profile.Id,
            Status = RecordStatus.Active,
            LastSyncAt = now,
            UpdatedAt = now,
        };
        await context.JobPositions.AddAsync(position);

        var period = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = "2026-10",
            Name = "Tháng 10/2026",
            PeriodType = PeriodType.Month,
            StartDate = new DateOnly(2026, 10, 1),
            EndDate = new DateOnly(2026, 10, 31),
            Status = PeriodStatus.Open,
            UpdatedAt = now,
        };
        await context.EvaluationPeriods.AddAsync(period);

        var manager = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "MGR001",
            FullName = "Nguyễn Văn Quản Lý",
            Email = "manager@progest.local",
            DepartmentId = dept.Id,
            JobPositionId = position.Id,
            Status = EmployeeStatus.Active,
            LastSyncAt = now,
            UpdatedAt = now,
        };
        var managerAccount = new Account
        {
            Id = Guid.NewGuid(),
            EmployeeId = manager.Id,
            Username = manager.Email,
            PasswordHash = "hash",
            LastSyncAt = now,
            UpdatedAt = now,
        };
        manager.Account = managerAccount;
        await context.Employees.AddAsync(manager);
        await context.Accounts.AddAsync(managerAccount);

        var staff = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "NV001",
            FullName = "Trần Thị Nhân Viên",
            Email = "staff@progest.local",
            DepartmentId = dept.Id,
            JobPositionId = position.Id,
            ManagerId = manager.Id,
            Status = EmployeeStatus.Active,
            LastSyncAt = now,
            UpdatedAt = now,
        };
        var staffAccount = new Account
        {
            Id = Guid.NewGuid(),
            EmployeeId = staff.Id,
            Username = staff.Email,
            PasswordHash = "hash",
            LastSyncAt = now,
            UpdatedAt = now,
        };
        staff.Account = staffAccount;
        await context.Employees.AddAsync(staff);
        await context.Accounts.AddAsync(staffAccount);

        // Target: HDV = 10 tỷ
        var targetHdv = new TargetAssignment
        {
            Id = Guid.NewGuid(),
            EmployeeId = staff.Id,
            EvaluationPeriodId = period.Id,
            KpiDefinitionId = kpiHdv.Id,
            TargetValue = 10m,
            Version = 1,
            Status = TargetAssignmentStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await context.TargetAssignments.AddAsync(targetHdv);

        // Actual: HDV = 10 tỷ (100% đạt)
        var actualHdv = new ActualProgress
        {
            Id = Guid.NewGuid(),
            EmployeeId = staff.Id,
            EvaluationPeriodId = period.Id,
            KpiDefinitionId = kpiHdv.Id,
            Value = 10m,
            ProgressDate = new DateOnly(2026, 10, 15),
            Status = ActualProgressStatus.Valid,
            UpdatedAt = now,
        };
        await context.ActualProgresses.AddAsync(actualHdv);

        await context.SaveChangesAsync();

        return (dept, position, period, manager, staff);
    }

    [Fact]
    public async Task GetEvaluationFormAsync_ReturnsPrepopulatedForm()
    {
        var (context, service) = CreateTestEnvironment();
        var (_, _, period, manager, staff) = await SeedBaseScenarioAsync(context);

        var form = await service.GetEvaluationFormAsync(staff.Id, period.Id, manager.Id);

        Assert.NotNull(form);
        Assert.Equal(staff.Id, form.Employee.Id);
        Assert.Equal("NV001", form.Employee.EmployeeCode);
        Assert.Equal(period.Id, form.Period.Id);
        Assert.Equal("EP-TELLER", form.Profile.Code);
        Assert.Equal(3, form.CriteriaGroups.Count);
        Assert.True(form.CanEdit);

        var kdGroup = form.CriteriaGroups.First(g => g.GroupCode == "KD");
        Assert.Equal(40m, kdGroup.WeightPercent);
        Assert.Single(kdGroup.KpiItems);

        var hdvItem = kdGroup.KpiItems[0];
        Assert.Equal("HDV", hdvItem.KpiCode);
        Assert.Equal(10m, hdvItem.TargetValue);
        Assert.Equal(10m, hdvItem.ActualValue);
        Assert.Equal(100m, hdvItem.AchievementPercent);
    }

    [Fact]
    public async Task GetEvaluationFormAsync_ThrowsNotFound_WhenEmployeeMissing()
    {
        var (context, service) = CreateTestEnvironment();
        var (_, _, period, manager, _) = await SeedBaseScenarioAsync(context);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.GetEvaluationFormAsync(Guid.NewGuid(), period.Id, manager.Id));
    }

    [Fact]
    public async Task SubmitEvaluationAsync_ThrowsBusinessRule_WhenManagerEvaluatesSelf_EnforcingBR02()
    {
        var (context, service) = CreateTestEnvironment();
        var (_, _, period, manager, _) = await SeedBaseScenarioAsync(context);

        var request = new SubmitEvaluationRequestDto(
            EmployeeId: manager.Id, // Self-evaluation attempt
            PeriodId: period.Id,
            Comment: "Self evaluation");

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.SubmitEvaluationAsync(request, manager.Id));

        Assert.Equal(ErrorCodes.MSG10, ex.ErrorCode);
    }

    [Fact]
    public async Task SubmitEvaluationAsync_ThrowsBusinessRule_WhenPeriodIsLocked()
    {
        var (context, service) = CreateTestEnvironment();
        var (_, _, period, manager, staff) = await SeedBaseScenarioAsync(context);

        period.Status = PeriodStatus.Locked;
        await context.SaveChangesAsync();

        var request = new SubmitEvaluationRequestDto(
            EmployeeId: staff.Id,
            PeriodId: period.Id,
            Comment: "Attempt on locked period");

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.SubmitEvaluationAsync(request, manager.Id));
    }

    [Fact]
    public async Task SubmitEvaluationAsync_CalculatesScoreAndSavesCoachingPlan_Successfully()
    {
        var (context, service) = CreateTestEnvironment();
        var (_, _, period, manager, staff) = await SeedBaseScenarioAsync(context);

        var request = new SubmitEvaluationRequestDto(
            EmployeeId: staff.Id,
            PeriodId: period.Id,
            Comment: "Hoàn thành tốt nhiệm vụ.",
            CoachingPlan: "Tham gia khóa đào tạo kỹ năng bán chéo nâng cao",
            CoachingRequired: true,
            IsDraft: false);

        var result = await service.SubmitEvaluationAsync(request, manager.Id);

        Assert.NotNull(result);
        Assert.Equal(staff.Id, result.EmployeeId);
        Assert.Equal("CALCULATED", result.Status);
        Assert.True(result.TotalScore > 0);

        // Verify entity persisted in database
        var savedEvaluation = await context.PerformanceEvaluations
            .FirstOrDefaultAsync(pe => pe.EmployeeId == staff.Id && pe.EvaluationPeriodId == period.Id);

        Assert.NotNull(savedEvaluation);
        Assert.Equal(result.TotalScore, savedEvaluation.TotalScore);
        Assert.Equal(PerformanceEvaluationStatus.Calculated, savedEvaluation.Status);
        Assert.Contains("Tham gia khóa đào tạo kỹ năng bán chéo nâng cao", savedEvaluation.Comment);
    }

    [Fact]
    public async Task SubmitEvaluationAsync_DeductsRiskPenalty_WhenRiskEventsOccurred()
    {
        var (context, service) = CreateTestEnvironment();
        var (_, _, period, manager, staff) = await SeedBaseScenarioAsync(context);

        // Add a CRITICAL risk event during period (penalty = 20 points, demoted tier)
        var risk = new RiskEvent
        {
            Id = Guid.NewGuid(),
            EmployeeId = staff.Id,
            Code = "RSK-001",
            EventType = "VI_PHAM_QUY_TRINH",
            Description = "Lỗi nghiêm trọng giao dịch không kiểm tra CMND",
            Severity = RiskEventSeverity.Critical,
            Status = RiskEventStatus.Confirmed,
            OccurredAt = new DateTimeOffset(2026, 10, 10, 8, 0, 0, TimeSpan.Zero),
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await context.RiskEvents.AddAsync(risk);
        await context.SaveChangesAsync();

        var cldvGroup = await context.KpiGroups.FirstAsync(g => g.Code == "CLDV");
        var request = new SubmitEvaluationRequestDto(
            EmployeeId: staff.Id,
            PeriodId: period.Id,
            Comment: "Có vi phạm nghiêm trọng trong kỳ",
            QualitativeScores: new List<QualitativeScoreInputDto>
            {
                new(cldvGroup.Id, 100m, "Chăm sóc khách hàng tốt"),
            },
            IsDraft: false);

        var result = await service.SubmitEvaluationAsync(request, manager.Id);

        Assert.NotNull(result);
        // Due to critical risk, tier is capped at B
        Assert.Equal("B", result.ProposedTier);
    }

    [Fact]
    public async Task GetEvaluationPeriodsAsync_ReturnsPeriods()
    {
        var (context, service) = CreateTestEnvironment();
        await SeedBaseScenarioAsync(context);

        var periods = await service.GetEvaluationPeriodsAsync();

        Assert.NotEmpty(periods);
        Assert.Contains(periods, p => p.Code == "2026-10");
    }

    [Fact]
    public async Task GetDepartmentEmployeesForEvaluationAsync_ReturnsRoster()
    {
        var (context, service) = CreateTestEnvironment();
        var (dept, _, period, manager, staff) = await SeedBaseScenarioAsync(context);

        var roster = await service.GetDepartmentEmployeesForEvaluationAsync(period.Id, dept.Id, manager.Id);

        Assert.NotEmpty(roster);
        Assert.Contains(roster, r => r.EmployeeId == staff.Id);
    }
}
