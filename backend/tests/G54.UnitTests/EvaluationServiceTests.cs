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

    [Fact]
    public async Task GetDepartmentCalibrationAsync_ReturnsRankedRoster_AndGaussianPoints_AndQuota()
    {
        var (context, service) = CreateTestEnvironment();
        var (dept, position, period, manager, staff) = await SeedBaseScenarioAsync(context);

        var staff2 = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "NV002",
            FullName = "Lê Văn B",
            Email = "staff2@progest.local",
            DepartmentId = dept.Id,
            JobPositionId = position.Id,
            ManagerId = manager.Id,
            Status = EmployeeStatus.Active,
            LastSyncAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await context.Employees.AddAsync(staff2);

        var eval1 = new PerformanceEvaluation
        {
            Id = Guid.NewGuid(),
            EmployeeId = staff.Id,
            EvaluationPeriodId = period.Id,
            EvaluationProfileId = position.EvaluationProfileId,
            TotalScore = 90m,
            Status = PerformanceEvaluationStatus.Calculated,
            CalculatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var eval2 = new PerformanceEvaluation
        {
            Id = Guid.NewGuid(),
            EmployeeId = staff2.Id,
            EvaluationPeriodId = period.Id,
            EvaluationProfileId = position.EvaluationProfileId,
            TotalScore = 75m,
            Status = PerformanceEvaluationStatus.Calculated,
            CalculatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await context.PerformanceEvaluations.AddRangeAsync(eval1, eval2);
        await context.SaveChangesAsync();

        var result = await service.GetDepartmentCalibrationAsync(period.Id, dept.Id, null, manager.Id);

        Assert.NotNull(result);
        Assert.Equal(dept.Id, result.Department.Id);
        Assert.True(result.RankedRoster.Count >= 2);
        Assert.Equal(staff.Id, result.RankedRoster[0].EmployeeId);
        Assert.Equal(1, result.RankedRoster[0].Rank);
        Assert.NotEmpty(result.BellCurve.Points);
        Assert.NotNull(result.Quota);
        Assert.True(result.Quota.MaxTierAQuota >= 1);
    }

    [Fact]
    public async Task SubmitDepartmentCalibrationAsync_SavesDraft_WithoutEnforcingTierACap()
    {
        var (context, service) = CreateTestEnvironment();
        var (dept, position, period, manager, staff) = await SeedBaseScenarioAsync(context);

        var eval = new PerformanceEvaluation
        {
            Id = Guid.NewGuid(),
            EmployeeId = staff.Id,
            EvaluationPeriodId = period.Id,
            EvaluationProfileId = position.EvaluationProfileId,
            TotalScore = 80m,
            Status = PerformanceEvaluationStatus.Calculated,
            CalculatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await context.PerformanceEvaluations.AddAsync(eval);
        await context.SaveChangesAsync();

        var request = new SubmitCalibrationRequestDto(
            DepartmentId: dept.Id,
            PeriodId: period.Id,
            Adjustments: new List<CalibrationAdjustmentItemDto>
            {
                new(staff.Id, "A1", "Đặc cách xếp A1 trong bản nháp", false)
            },
            Notes: "Lưu nháp thử nghiệm",
            IsDraft: true);

        var result = await service.SubmitDepartmentCalibrationAsync(request, manager.Id);

        Assert.NotNull(result);
        Assert.Equal("CALCULATED", result.Status);

        var updatedEval = await context.PerformanceEvaluations.FindAsync(eval.Id);
        Assert.NotNull(updatedEval);
        Assert.Contains("CALIBRATION_JSON", updatedEval.Comment);
    }

    [Fact]
    public async Task SubmitDepartmentCalibrationAsync_ThrowsBusinessRuleException_WhenTierAExceedsCap()
    {
        var (context, service) = CreateTestEnvironment();
        var (dept, position, period, manager, _) = await SeedBaseScenarioAsync(context);

        var staffList = new List<Employee>();
        var evalList = new List<PerformanceEvaluation>();
        for (int i = 1; i <= 10; i++)
        {
            var emp = new Employee
            {
                Id = Guid.NewGuid(),
                EmployeeCode = $"EMP{i:D3}",
                FullName = $"Nhân Viên {i}",
                Email = $"emp{i}@progest.local",
                DepartmentId = dept.Id,
                JobPositionId = position.Id,
                ManagerId = manager.Id,
                Status = EmployeeStatus.Active,
                LastSyncAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            staffList.Add(emp);

            evalList.Add(new PerformanceEvaluation
            {
                Id = Guid.NewGuid(),
                EmployeeId = emp.Id,
                EvaluationPeriodId = period.Id,
                EvaluationProfileId = position.EvaluationProfileId,
                TotalScore = 70m + i,
                Status = PerformanceEvaluationStatus.Calculated,
                CalculatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
        await context.Employees.AddRangeAsync(staffList);
        await context.PerformanceEvaluations.AddRangeAsync(evalList);
        await context.SaveChangesAsync();

        var adjustments = new List<CalibrationAdjustmentItemDto>
        {
            new(staffList[0].Id, "A1", "Vượt trội 1", false),
            new(staffList[1].Id, "A1", "Vượt trội 2", false),
            new(staffList[2].Id, "A2", "Vượt trội 3", false),
            new(staffList[3].Id, "A3", "Vượt trội 4", false),
        };

        var request = new SubmitCalibrationRequestDto(
            DepartmentId: dept.Id,
            PeriodId: period.Id,
            Adjustments: adjustments,
            Notes: "Trình duyệt vượt hạn mức",
            IsDraft: false);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.SubmitDepartmentCalibrationAsync(request, manager.Id));

        Assert.Equal(ErrorCodes.BR37, ex.ErrorCode);
    }

    [Fact]
    public async Task SubmitDepartmentCalibrationAsync_UpdatesStatusToInReview_WhenValid()
    {
        var (context, service) = CreateTestEnvironment();
        var (dept, position, period, manager, staff) = await SeedBaseScenarioAsync(context);

        var eval = new PerformanceEvaluation
        {
            Id = Guid.NewGuid(),
            EmployeeId = staff.Id,
            EvaluationPeriodId = period.Id,
            EvaluationProfileId = position.EvaluationProfileId,
            TotalScore = 88m,
            Status = PerformanceEvaluationStatus.Calculated,
            CalculatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await context.PerformanceEvaluations.AddAsync(eval);
        await context.SaveChangesAsync();

        var request = new SubmitCalibrationRequestDto(
            DepartmentId: dept.Id,
            PeriodId: period.Id,
            Adjustments: new List<CalibrationAdjustmentItemDto>
            {
                new(staff.Id, "A2", "Đạt chuẩn nâng hạng A2", false)
            },
            Notes: "Trình duyệt đúng quota",
            IsDraft: false);

        var result = await service.SubmitDepartmentCalibrationAsync(request, manager.Id);

        Assert.NotNull(result);
        Assert.Equal("INREVIEW", result.Status);
        Assert.True(result.IsQuotaCompliant);

        var updatedEval = await context.PerformanceEvaluations.FindAsync(eval.Id);
        Assert.NotNull(updatedEval);
        Assert.Equal(PerformanceEvaluationStatus.InReview, updatedEval.Status);
    }

    [Fact]
    public async Task GetBranchApprovalOverviewAsync_ReturnsAllDepartments_AndBranchTotals()
    {
        var (context, service) = CreateTestEnvironment();
        var (dept, position, period, manager, staff) = await SeedBaseScenarioAsync(context);

        var eval = new PerformanceEvaluation
        {
            Id = Guid.NewGuid(),
            EmployeeId = staff.Id,
            EvaluationPeriodId = period.Id,
            EvaluationProfileId = position.EvaluationProfileId,
            TotalScore = 85m,
            Status = PerformanceEvaluationStatus.InReview,
            CalculatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await context.PerformanceEvaluations.AddAsync(eval);
        await context.SaveChangesAsync();

        var overview = await service.GetBranchApprovalOverviewAsync(period.Id, manager.Id);

        Assert.NotNull(overview);
        Assert.Equal(period.Id, overview.Period.Id);
        Assert.NotEmpty(overview.Departments);
        Assert.Contains(overview.Departments, d => d.DepartmentId == dept.Id);
        Assert.True(overview.TotalEvaluated >= 1);
    }

    [Fact]
    public async Task RejectDepartmentCalibrationAsync_RevertsToCalculated_AndStoresFeedback()
    {
        var (context, service) = CreateTestEnvironment();
        var (dept, position, period, manager, staff) = await SeedBaseScenarioAsync(context);

        var eval = new PerformanceEvaluation
        {
            Id = Guid.NewGuid(),
            EmployeeId = staff.Id,
            EvaluationPeriodId = period.Id,
            EvaluationProfileId = position.EvaluationProfileId,
            TotalScore = 90m,
            Status = PerformanceEvaluationStatus.InReview,
            CalculatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await context.PerformanceEvaluations.AddAsync(eval);
        await context.SaveChangesAsync();

        var request = new RejectCalibrationRequestDto(period.Id, "Tỷ lệ Tier A chưa đồng đều, đề nghị cân chỉnh lại.");
        var result = await service.RejectDepartmentCalibrationAsync(dept.Id, request, manager.Id);

        Assert.NotNull(result);
        Assert.Equal("REVISION_REQUIRED", result.Status);

        var updatedEval = await context.PerformanceEvaluations.FindAsync(eval.Id);
        Assert.NotNull(updatedEval);
        Assert.Equal(PerformanceEvaluationStatus.Calculated, updatedEval.Status);
        Assert.Contains("RECALIBRATION_REASON", updatedEval.Comment);
        Assert.Contains("Tỷ lệ Tier A chưa đồng đều", updatedEval.Comment);
    }

    [Fact]
    public async Task FinalizeAndLockPeriodAsync_LocksPeriod_AndMarksAllEvaluationsFinal()
    {
        var (context, service) = CreateTestEnvironment();
        var (_, position, period, manager, staff) = await SeedBaseScenarioAsync(context);

        var eval = new PerformanceEvaluation
        {
            Id = Guid.NewGuid(),
            EmployeeId = staff.Id,
            EvaluationPeriodId = period.Id,
            EvaluationProfileId = position.EvaluationProfileId,
            TotalScore = 92m,
            Status = PerformanceEvaluationStatus.InReview,
            CalculatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await context.PerformanceEvaluations.AddAsync(eval);
        await context.SaveChangesAsync();

        var request = new FinalizeEvaluationPeriodRequestDto(period.Id, null, "Phê duyệt chính thức toàn chi nhánh.");
        var result = await service.FinalizeAndLockPeriodAsync(request, manager.Id);

        Assert.NotNull(result);
        Assert.Equal("LOCKED", result.PeriodStatus);
        Assert.True(result.TotalEvaluationsFinalized >= 1);

        var updatedPeriod = await context.EvaluationPeriods.FindAsync(period.Id);
        Assert.NotNull(updatedPeriod);
        Assert.Equal(PeriodStatus.Locked, updatedPeriod.Status);

        var updatedEval = await context.PerformanceEvaluations.FindAsync(eval.Id);
        Assert.NotNull(updatedEval);
        Assert.Equal(PerformanceEvaluationStatus.Final, updatedEval.Status);
        Assert.NotNull(updatedEval.ApprovedAt);
        Assert.Contains("APPROVAL_COMMENTS", updatedEval.Comment);
    }

    [Fact]
    public async Task FinalizeAndLockPeriodAsync_ThrowsWhenPeriodAlreadyLocked()
    {
        var (context, service) = CreateTestEnvironment();
        var (_, _, period, manager, _) = await SeedBaseScenarioAsync(context);

        period.Status = PeriodStatus.Locked;
        await context.SaveChangesAsync();

        var request = new FinalizeEvaluationPeriodRequestDto(period.Id, null, "Khóa lại");
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.FinalizeAndLockPeriodAsync(request, manager.Id));

        Assert.Equal(ErrorCodes.Conflict, ex.ErrorCode);
    }
}
